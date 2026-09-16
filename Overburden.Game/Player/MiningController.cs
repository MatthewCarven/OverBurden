using System;
using System.Numerics;
using Overburden.Core.Content;
using Overburden.Core.Mining;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;

namespace Overburden.Game.Player;

/// <summary>
/// Turns "the player is pointing there and holding the button" into swings.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately free of Godot types. It takes an origin and a direction as
/// <see cref="Vector3"/> from <c>System.Numerics</c> and hands back what it found, so the
/// whole of mining's behaviour — reach, cooldown, what a swing does, what it reports — can be
/// driven from a headless profile run with no camera and no input at all. That is what makes
/// <c>--mine</c> a repeatable measurement rather than a person holding a mouse button and
/// guessing.
/// </para>
/// <para>
/// It lives in the Game project rather than Core because a cooldown is <em>input</em> state:
/// it is about how fast a person may act, not about what the world does. Core has no clock
/// and should not grow one.
/// </para>
/// </remarks>
public sealed class MiningController(
    ChunkPipeline pipeline,
    MaterialTable materials,
    ToolDefinition tool)
{
    private double _cooldown;

    /// <summary>The tier being swung. Switched from the number keys.</summary>
    public ToolDefinition Tool { get; set; } = tool;

    /// <summary>Where the material goes, or null to mine into nothing.</summary>
    /// <remarks>
    /// <b>Optional, because a profile run that is measuring the carve does not need one</b> and
    /// because every caller before M6c had none. <c>Carve</c> credits it per material as it
    /// removes rather than from the result, since a bite through a dirt bank with a stone seam in
    /// it takes two materials and <c>Removed</c> is one number.
    /// </remarks>
    public Overburden.Core.Inventory.VolumetricInventory? Inventory { get; init; }

    /// <summary>
    /// The machine set a swing tells what it took of a body, or null to tell nobody.
    /// </summary>
    /// <remarks>
    /// <b>Unwired from M10a-2 until 2026-09-16, and M5c-2's shape of hole exactly</b>: Core's
    /// <c>Carve.Take</c> reports a body voxel through <c>IMachineBodies.Taken</c> and the drain's
    /// credit rule is <c>Lost == Carved</c>, tested end to end in <c>MachineDrainTests</c> with the
    /// set passed in — and this controller passed nothing, so in the game every pick on a pump
    /// read <c>Lost &gt; Carved</c>, the fall's signature, and destroyed the body without credit.
    /// Found by M10c's planning pass reading the swing dispatch for the gate; the wire between
    /// two tested halves had no recipe on it.
    /// </remarks>
    public Overburden.Core.Machines.IMachineBodies? Bodies { get; set; }

    /// <summary>
    /// How far the player can reach, in metres. Supplied by the player, who owns it.
    /// </summary>
    /// <remarks>
    /// <b><c>required</c> and defaultless on purpose.</b> The value moved to
    /// <see cref="PlayerController.ReachMetres"/> at M4c — a tool tier says how big a bite it
    /// takes and what it is hard enough to take it out of, and how far an arm reaches is a
    /// property of the arm. Leaving a default here would leave two numbers that agree until one
    /// of them is tuned, which is the one thing M4d is going to do to it.
    /// </remarks>
    public required float ReachMetres { get; init; }

    /// <summary>
    /// The entangled miner: unlimited reach and a fixed rate, on a debug bind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An endgame tool sketched as a debug mode, and the distinction is deliberate.</b> The
    /// fiction is that entanglement is not distance-sensitive, so the arm stops being what
    /// limits the bite. That is a direct reversal of a decision locked twice — <b>reach is the
    /// player's, not the tool's</b> (M3d, restated at M4c) — and the reversal is *correct* for
    /// this one tool, because its whole concept is that the reach is a property of the
    /// mechanism rather than of the body holding it.
    /// </para>
    /// <para>
    /// So it is <b>not</b> a tool tier and there is no <c>reachMetres</c> in the tool JSON. Once
    /// a field like that exists, every mod writes against it and the locked decision is gone for
    /// every tool rather than for this one. This is a mode on the controller, on Q, in the
    /// same spirit as the L lamp bind: the smallest thing that makes the milestone playable,
    /// left obviously not-the-feature. <b>Making it real content is the moment to settle whether
    /// <c>ToolDefinition</c> gains an optional reach</b>, and that wants its own planning pass.
    /// </para>
    /// <para>
    /// <b>It used to compose with the number keys entirely — "how far and how fast, never how
    /// big a bite or what the bite is hard enough to take". Half of that is now false</b>: see
    /// <see cref="Bite"/>, which makes the bite a 1 m cube or a whole chunk. <b>The hardness
    /// half survives untouched and is the half that was load-bearing</b>, because it is what
    /// keeps <see cref="Carve"/>'s gate the only gate in the game rather than something a
    /// keypress opts out of.
    /// </para>
    /// </remarks>
    public bool Entangled { get; set; }

    /// <summary>
    /// What a bare entangled swing takes: the brush cell's 1 m cube by default, the whole chunk or the tier's
    /// sphere as arms. The remarks are the chunk bite's, kept because the cube inherits them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The fiction earns the mechanic rather than decorating it.</b> This is salvage from a
    /// humanity that did not make it: superconducting coils, a quantum-vacuum tap for power, and
    /// a mass measured in tons — a machine nobody would build to dig a tunnel, indexing matter
    /// by the same lattice the world's storage is chunked on because that is the granularity its
    /// entanglement register addresses. It does not take a bite the size of its head; it
    /// resolves a cell of the lattice, and the cell is 8 m on a side.
    /// </para>
    /// <para>
    /// <b>And it stays a debug bind, which is what the fiction buys.</b> A tool that removes 512
    /// cubic metres a press is not balanceable against a <c>hand_pick</c> and is not trying to
    /// be. What it is for is the same thing Q has always been for — excavating a test volume
    /// large enough to judge something by eye — and <b>M8 needs a bigger one than M5d did</b>: a
    /// mine worth flooding is tens of thousands of cubic metres, and the sphere digs a corridor.
    /// </para>
    /// <para>
    /// <b>Explicitly not implemented, and recorded so nobody adds it as an obvious oversight:
    /// weight.</b> A chunk of rock is 512,000 litres and there is no carry limit, no
    /// encumbrance, and no penalty for holding it. Being over-encumbered has never been fun in
    /// any game that has had it, and the real-world response to a haulage limit is to go and get
    /// a bigger container — a player who cannot will simply walk back and forth, which is
    /// patience taxed and called difficulty. <b>Revisit trigger: a survival mode</b>, which is
    /// already the recorded trigger for shortening reach, and the two would be settled together.
    /// </para>
    /// <para>
    /// <c>--entanglesphere</c> restores the sphere, which is the A/B arm the cost of the chunk was
    /// measured against.
    /// </para>
    /// <para>
    /// <b>Superseded as the default on 2026-09-14 by the cube</b> — <i>"match the 4x4x4 grid
    /// on the right click function"</i> — and kept as an arm: the chunk bite is what excavates a test mine in
    /// seconds and what every recorded bite figure was taken with. <c>--entanglechunk</c> restores
    /// it; the fiction above is the cube's too, one lattice level down.
    /// </para>
    /// </remarks>
    public EntangledBite Bite { get; set; } = EntangledBite.Cube;

    /// <summary>Whether the next ordinary swing would take a whole chunk.</summary>
    /// <remarks>
    /// Exposed so the highlight can outline the cube rather than re-deriving the rule from
    /// two flags. A chunk boundary is invisible in the world, so an outline of one voxel in
    /// front of an 8 m bite is not a small inaccuracy — it is the player being shown the wrong
    /// object. <b>Precision is not folded in here</b> because it is a per-frame held key rather
    /// than controller state, and the caller that knows about the key is the caller that draws.
    /// </remarks>
    public bool TakesWholeChunk => Entangled && Bite == EntangledBite.Chunk;

    /// <summary>Whether the next ordinary swing would take the 1 m cube, for the same outline reason.</summary>
    public bool TakesCube => Entangled && Bite == EntangledBite.Cube;

    /// <summary>
    /// The entangled reach. A backstop, not the real limit — the world is.
    /// </summary>
    /// <remarks>
    /// <b>What actually stops the ray is the edge of the loaded region</b>, where an unloaded
    /// voxel returns <see cref="RayOutcome.Blocked"/> and the swing is refused. Entanglement may
    /// not be distance-sensitive but the streamer is, so the honest reach is "as far as the
    /// world exists", which at r16 is about 144 m. This constant only has to exceed that, and it
    /// bounds the DDA in the pathological case for the same reason
    /// <see cref="ToolDefinition.MaxRadiusMetres"/> bounds the carve: a loop whose length comes
    /// from outside needs a ceiling that does not.
    /// </remarks>
    public const float EntangledReachMetres = 512f;

    /// <summary>Twenty swings a second, fixed, whatever tier is selected.</summary>
    /// <remarks>
    /// <para>
    /// <b>Fixed rather than the tier's</b>, because otherwise the dig rate depends on which
    /// key was pressed last, and this thing exists to excavate a <em>comparable</em> test volume
    /// for M5d-2's look questions.
    /// </para>
    /// <para>
    /// <b>Raised three times at Matthew's request — 0.25 s to 0.125 s to 0.05 s to 0.025 s — and
    /// the first
    /// raise inverted a recorded caveat.</b> At four a second this was marginally *slower* than
    /// <c>steel_pick</c>'s own 0.20 s and the only upgrade was the range. From eight onward it is
    /// faster than every shipped tier, so the mode is a strict upgrade on both axes — which is
    /// what an endgame tool ought to be, and worth saying out loud because it means <b>the tier's
    /// cooldown no longer bounds anything while entangled</b>. A tier balanced by its rate would
    /// be balanced by nothing here.
    /// </para>
    /// <para>
    /// <b>The named ceiling is now close: one swing per frame arrives at 0.0167 s, and this is
    /// 0.025 s.</b> Below the frame time the cooldown stops pacing the tool and the frame rate
    /// becomes the balance, which would make the dig rate depend on the machine. The headroom was
    /// a factor of three at 0.05 s and is <b>a factor of 1.5 now</b>, so this is the last doubling
    /// that can be taken without changing how the cooldown works.
    /// </para>
    /// <para>
    /// <b>And the quantisation is already biting, so the nominal rate is not the delivered one.</b>
    /// <c>_cooldown</c> is <em>reset</em> to the full interval rather than decremented by it, so a
    /// swing can only fire on a frame boundary: at 0.05 s a 60 s run landed at ~18.9 swings/s
    /// against a nominal 20. At 0.025 s against a 16.7 ms frame the next eligible boundary is
    /// 33.4 ms, so <b>the honest expectation is ~30/s at 60 fps rather than 40</b> — nearer 36 at
    /// 144 fps. <b>Delivering a true 40 needs the cooldown to be subtracted rather than reset</b>,
    /// which would let a swing catch up across a long frame; that is a pacing change for every
    /// tool and is deliberately not made here.
    /// </para>
    /// <para>
    /// Not a DoS the way a radius is: the cost per swing is a bounded cast plus a bounded carve.
    /// <b>Re-measured at every rate rather than assumed, and the measurement keeps saying the
    /// same surprising thing — faster is cheaper per swing.</b> Consecutive swings chew the
    /// previous bite, so most of the sphere is already air: 0.139 ms a swing at four, 0.072 at
    /// eight, 0.070 at twenty and <b>0.079 against the 0.05 arm’s 0.089 at forty</b>. What rises is
    /// the <em>remesh</em> load, which is per swing rather than per voxel.
    /// </para>
    /// <para>
    /// <b>Measured as a same-session A/B at r4h/2v, 6 m/s, 30 s, Debug — both arms drawing:</b>
    /// 587 swings against 1164, so <b>19.6/s against 38.8/s, a true 1.98x</b> and not the ~30/s the
    /// frame-boundary arithmetic predicts, because headless frames are ~7.5 ms rather than 16.7.
    /// <b>A windowed run at 60 fps should expect nearer 30.</b> Remesh load doubled exactly as the
    /// per-swing model says — <b>31.5/s to 63.9/s at an unchanged 1.6 chunks per swing</b> — and
    /// clones held at 1.3 per swing. <c>update ms</c> mean went 3.808 to 4.815 (+26%) with the
    /// <em>max</em> unchanged at ~19.9 ms, so the tail is not this. <b>Relight per swing fell again,
    /// 1.2 to 1.0</b>, which every previous raise also did.
    /// </para>
    /// <para>
    /// <b>The number to watch now is the main thread, not the remeshes: 54.21% of one core to
    /// 68.47%</b> in Debug at r4h/2v. Release is roughly 8x cheaper on the light derive that
    /// dominates it (M5d-1), so this is not a ceiling — but it is the first raise where a headline
    /// percentage moved by more than a rounding error, and <b>the next one has to be measured in
    /// Release before it is believed</b>.
    /// </para>
    /// </remarks>
    public const float EntangledCooldownSeconds = 0.025f;

    /// <summary>The rate a whole-chunk bite is paced at. Its own number, and measured.</summary>
    /// <remarks>
    /// <para>
    /// <b>The chunk bite cannot inherit 0.025 s, and the reason is arithmetic rather than
    /// taste.</b> <c>hand_pick</c>'s sphere is about seventeen voxels; a chunk is 32,768, so the
    /// bite went up by roughly <b>1,900×</b>. At forty a second that is 1.3 million voxel writes
    /// a second and forty chunks of world removed — the streamer would be re-meshing and
    /// re-lighting a moving 8 m hole faster than it can generate the terrain around it, and the
    /// thing the mode exists for (looking at a large excavation) would be unwatchable while it
    /// happened.
    /// </para>
    /// <para>
    /// <b>Four a second, and the measurement says the rate is not what needs choosing.</b>
    /// Same-session A/B at r4h/2v, 6 m/s, 30 s, Debug, against <c>--entanglesphere</c>:
    /// <b>one chunk bite costs <c>swing ms</c> mean 16.9 / max 35.6 on the sim thread, against
    /// the sphere's 0.054 / 2.48.</b> That is one to two whole frames, and <b>no cooldown can
    /// bound it, because one bite is atomic</b> — M2d's mesh-apply finding a third time: a time
    /// slice cannot bound the tail below the cost of one item. The rate therefore chooses the
    /// <em>duty cycle</em> and nothing else. At 3.9/s delivered that is <b>6.6% of wall clock
    /// spent inside the carve</b>, and the world still streams: 184 drawn against the sphere
    /// arm's 194, first draw 5.12 s against 4.93 s.
    /// </para>
    /// <para>
    /// <b>Per voxel it is 4.3× cheaper than the sphere</b> — 0.70 µs against 3.0 µs per voxel
    /// removed — because one raycast, one remesh marking and one relight queueing amortise over
    /// 23,964 voxels instead of eighteen. <b>Fifth outing for "the bigger arm is cheaper per
    /// unit"</b>, and the reason the whole-run main thread went <em>down</em>, 65.95% to 52.53%:
    /// an emptied chunk meshes to nothing, so the tool deletes the geometry that costs money
    /// (73,714 quads against 83,004; 9,618 light derives against 13,679). <b>Stated with its
    /// confound: the two arms removed 66,560 m³ and ~333 m³, so that is not "the chunk bite is
    /// cheaper" — it is what these two rates cost.</b> The clean comparisons are per swing and
    /// per voxel.
    /// </para>
    /// <para>
    /// <b>Four a second ships and halving it is recorded rather than done, because the goalpost
    /// does not move mid-run.</b> Matthew raised it — <em>"maybe halve the mining rate, like, or
    /// even quarter… or not"</em> — and then withdrew it as exactly that. **The right shape is
    /// `rainfallScale`'s**: the measurement bounds the range and a person holding the thing picks
    /// inside it. <c>--chunkcooldown=</c> is the seam, so the next opinion costs a flag rather
    /// than a rebuild.
    /// </para>
    /// <para>
    /// <b>The argument for halving, kept because it corrects a claim this comment makes above.</b>
    /// "The rate chooses the duty cycle and nothing else" is true of <em>cost</em> and false of
    /// <em>feel</em>: when one bite is a whole frame, <b>the duty cycle is the felt smoothness</b>.
    /// At 4/s two or three frames are lost every 250 ms with none clean in between, which reads as
    /// a continuous stutter; at 2/s the hitch is identical but there are four clean frames around
    /// it. <b>And 250 ms is inside human reaction time</b>, so "stop, that is enough" costs two
    /// chunks of overshoot — 1,000 m³ that then has to be walked into to be seen. Against it:
    /// 4/s is 2,048 m³/s and clears M8's own 96 m × 96 m × 24 m bench mine in <b>1.8 minutes</b>
    /// against 3.6 at half and 7.2 at a quarter, and the tool exists to stop digging being a
    /// chore. <b>Revisit trigger: the first play that holds the button.</b>
    /// </para>
    /// <para>
    /// <b>Named lever, deliberately not built: a chunk-aware write path</b>
    /// — fill the palette with air once, update the sky column once, mark the neighbourhood
    /// once, instead of 32,768 <c>SetVoxel</c> calls each doing all three. Roughly two orders of
    /// magnitude, and not built because this is a debug bind and it would be a second write path
    /// beside the one thing M6 says must never be wrong. <b>Revisit trigger: the hitch reading
    /// as a stall in play, or this bite becoming content.</b>
    /// </para>
    /// <para>
    /// Well clear of the ceiling the sphere rate is close to: one swing per frame arrives at
    /// 0.0167 s, and the sphere arm has a factor of 1.5 left where this has fifteen. The
    /// quantisation that makes a nominal 40/s deliver 35.8 does not bite here — <b>3.9/s
    /// delivered against 4.0 nominal</b>.
    /// </para>
    /// </remarks>
    public const float DefaultEntangledChunkCooldownSeconds = 0.25f;

    /// <summary>The live value, so <c>--chunkcooldown=</c> can sweep it without a rebuild.</summary>
    /// <remarks>
    /// A settable property rather than the constant, for the reason <c>rainfallScale</c> is one:
    /// this is a feel number the measurement can only bound, so the person judging it has to be
    /// able to move it. The constant stays as the shipped default and as the thing the doc
    /// comment above argues about.
    /// </remarks>
    public float EntangledChunkCooldownSeconds { get; set; } = DefaultEntangledChunkCooldownSeconds;

    /// <summary>Ten cube bites a second — Matthew's number, between the sphere's forty and the chunk's four.</summary>
    /// <remarks>
    /// <para>
    /// A cube is sixty-four voxels, four <c>hand_pick</c> spheres and a 512th of a chunk, so the
    /// sphere's rate nearly transfers and the chunk's does not: at forty a second the cube would
    /// be 2,560 voxels a second through the edit hook, at four it would take two minutes to clear
    /// one chunk's volume. Twenty was the estimate (17.8/s delivered, 0.42 ms a swing headless);
    /// <b>ten is his — <i>"10/second"</i>, 2026-09-14 — and the reason is not cost, it is that
    /// this rate is what a battery will one day pay for</b>: the entangled miner's gate is M10's
    /// power model (TODO, "the gate that makes you earn it is a battery"), so the shipped rate is
    /// a design number for that gate rather than a ceiling the frame can carry. <c>--cubecooldown=</c>
    /// is the sweep, a play bind like the chunk's.
    /// </para>
    /// </remarks>
    public const float DefaultEntangledCubeCooldownSeconds = 0.1f;

    /// <summary>The live value, so <c>--cubecooldown=</c> can sweep it without a rebuild.</summary>
    public float EntangledCubeCooldownSeconds { get; set; } = DefaultEntangledCubeCooldownSeconds;

    private float EffectiveReachMetres => Entangled ? EntangledReachMetres : ReachMetres;

    /// <summary>
    /// The cooldown for the shape actually swung, which is a rule change worth naming.
    /// </summary>
    /// <remarks>
    /// <b>The cooldown used to be per <em>mode</em>, and it is now per <em>shape</em>.</b> The
    /// old rule — a chip pays the same cooldown as a sphere — existed so that precision could
    /// not become the <em>efficient</em> way to excavate as well as the accurate one. That
    /// argument does not reach the chunk bite: at 32,768 voxels a press, the chunk bite is
    /// unambiguously the bulk method and nothing precision does at any rate competes with it.
    /// Pacing a single-voxel chip to 0.25 s would therefore protect nothing, and it would cost
    /// the case Matthew reported as load-bearing — fine work at range, the <c>L</c>+<c>Q</c>
    /// pairing, where the whole point is placing and picking single things far away.
    /// </remarks>
    private float CooldownForShape(bool precise) => (Entangled, precise) switch
    {
        (true, false) when Bite == EntangledBite.Chunk => EntangledChunkCooldownSeconds,
        (true, false) when Bite == EntangledBite.Cube => EntangledCubeCooldownSeconds,
        (true, _) => EntangledCooldownSeconds,
        _ => Tool.CooldownSeconds,
    };

    /// <summary>Of <see cref="Swings"/>, how many were fired entangled.</summary>
    /// <remarks>
    /// Counted for the reason <see cref="PreciseSwings"/> is: an entangled swing carves at
    /// arbitrary distance from the viewer, which is a different cost from one at 5 m and shares
    /// the same <c>swing ms</c> line. <c>--mine</c> never entangles, so a non-zero value in a
    /// profile means the run is not comparable with M3d's baseline.
    /// </remarks>
    public int EntangledSwings { get; private set; }

    /// <summary>Of <see cref="Swings"/>, how many took a whole chunk.</summary>
    /// <remarks>
    /// <b>Counted because it is the denominator every other mining number here is now division
    /// by.</b> A chunk bite and a sphere share the <c>swing ms</c>, <c>remesh per swing</c> and
    /// <c>voxels removed per swing</c> lines while differing by three orders of magnitude, so a
    /// run that mixed them would report a mean of two unrelated quantities — which is exactly
    /// the trap <see cref="PreciseSwings"/> was added for, one shape further along. Zero on
    /// every run that is not entangled.
    /// </remarks>
    public int ChunkSwings { get; private set; }

    /// <summary>Of <see cref="Swings"/>, how many took a 1 m cube — the same denominator argument, one shape more.</summary>
    public int CubeSwings { get; private set; }

    /// <summary>How far this swing actually reached, for the report.</summary>
    public float LastReachMetres { get; private set; }

    /// <summary>The furthest any swing has reached, and the total, for a mean.</summary>
    /// <remarks>
    /// <b>The only thing that can show the entangled reach is actually reaching.</b> Swing count
    /// and voxel count are identical whether the cast ran to 5 m or to 90 — which is the
    /// M5c-2 shape of hole, where every layer was tested and the wire between them was not. A
    /// mean of 4 m under an "unlimited reach" label is the failure this catches.
    /// </remarks>
    public float MaxReachMetres { get; private set; }

    public double TotalReachMetres { get; private set; }

    /// <summary>What the last cast found. Drives the highlight.</summary>
    public RayOutcome Outcome { get; private set; } = RayOutcome.Missed;

    public VoxelHit Hit { get; private set; }

    /// <summary>Whether the last cast is pointing at something a swing would act on.</summary>
    public bool HasTarget => Outcome == RayOutcome.Hit;

    public int Swings { get; private set; }

    public int VoxelsRemoved { get; private set; }

    /// <summary>Voxels a swing reached and the tier could not take.</summary>
    /// <remarks>
    /// The hardness gate, counted. Zero across a whole session means either every tier can
    /// take everything or nobody swung a weak one, and both make the mechanic invisible.
    /// </remarks>
    public int VoxelsRefused { get; private set; }

    /// <summary>Voxels inside a swing that were already empty.</summary>
    /// <remarks>
    /// Against <see cref="VoxelsRemoved"/> this is the number that argues about
    /// <see cref="Carve.SwingCentre"/>: a swing centred on the ray's entry point rather than
    /// the voxel centre would spend roughly half of every bite on air.
    /// </remarks>
    public int VoxelsAlreadyAir { get; private set; }

    /// <summary>Swings that could not reach part of their own sphere.</summary>
    public int ClippedSwings { get; private set; }

    /// <summary>
    /// Of <see cref="Swings"/>, how many were single-voxel rather than a sphere.
    /// </summary>
    /// <remarks>
    /// Counted because the two shapes cost very different amounts and share one
    /// <c>swing ms</c> line. A profile whose swings were mostly chips would report a cheap
    /// number under a label that means "a pick swing", which is how M3d's baseline stops being
    /// comparable without anything looking wrong. <c>--mine</c> holds no modifier, so this is
    /// zero on every profile run, and a non-zero value in one is the thing to notice.
    /// </remarks>
    public int PreciseSwings { get; private set; }

    /// <summary>
    /// Casts, and swings if the button is held and the tier has cooled down.
    /// </summary>
    /// <param name="precise">
    /// Take only the voxel under the crosshair, instead of the tool's sphere.
    /// </param>
    /// <returns>True on a frame where a swing actually happened.</returns>
    /// <remarks>
    /// <b>The shape is a per-frame input, not a mode the controller remembers.</b> Which is
    /// what makes it a held modifier rather than a toggle, and what keeps the decision in the
    /// binding where the key lives. Core owns both shapes because what a swing does to the
    /// world is a game rule a server would have to agree with; which key picks one is not.
    /// </remarks>
    public bool Update(
        double delta,
        Vector3 originMetres,
        Vector3 direction,
        bool mining,
        bool precise = false)
    {
        _cooldown = Math.Max(0.0, _cooldown - delta);

        // Cast every frame regardless of the button, because the highlight has to follow the
        // crosshair and not only the swings.
        Outcome = VoxelRay.Cast(
            pipeline, originMetres, direction, EffectiveReachMetres, out VoxelHit hit);
        Hit = hit;

        if (!mining || _cooldown > 0.0 || Outcome != RayOutcome.Hit)
        {
            return false;
        }

        // Precision wins over the chunk bite, and that ordering is the whole of why Alt still
        // means what it meant. Q makes the bite enormous; Alt is how you put it down again
        // without leaving the mode, which at unlimited reach is the only way to do fine work at
        // the far end of a cave.
        CarveResult result = precise
            ? Carve.Chip(pipeline, materials, hit, Tool, Inventory, Bodies)
            : TakesWholeChunk
                ? Carve.Chunk(pipeline, materials, hit, Tool, Inventory, Bodies)
                : TakesCube
                    ? Carve.Cube(pipeline, materials, hit, Tool, Inventory, Bodies)
                    : Carve.Swing(pipeline, materials, hit, Tool, Inventory, Bodies);

        _cooldown = CooldownForShape(precise);
        Swings++;
        LastReachMetres = hit.DistanceMetres;
        TotalReachMetres += hit.DistanceMetres;

        if (hit.DistanceMetres > MaxReachMetres)
        {
            MaxReachMetres = hit.DistanceMetres;
        }

        if (precise)
        {
            PreciseSwings++;
        }

        if (Entangled)
        {
            EntangledSwings++;
        }

        if (!precise && TakesWholeChunk)
        {
            ChunkSwings++;
        }

        if (!precise && TakesCube)
        {
            CubeSwings++;
        }

        VoxelsRemoved += result.Removed;
        VoxelsRefused += result.TooHard;
        VoxelsAlreadyAir += result.AlreadyAir;

        if (!result.Complete)
        {
            ClippedSwings++;
        }

        return true;
    }

    /// <summary>Debug placement of one material, in the empty voxel in front of the target.</summary>
    /// <remarks>
    /// <para>
    /// <b>M5b needs something in the world that emits light, and nothing can place anything
    /// until M6.</b> So this is one key and one material with no inventory, no cooldown and no
    /// tier — deliberately embarrassing enough that nobody mistakes it for the feature. M6's
    /// placement is a different thing that will replace it outright.
    /// </para>
    /// <para>
    /// <c>Hit.Previous</c> is the empty voxel the ray was in immediately before the one it hit,
    /// which M3b built and labelled "where M6's placement goes". This is the first caller.
    /// </para>
    /// </remarks>
    public bool TryPlaceDebug(int materialId)
    {
        if (Outcome != RayOutcome.Hit) return false;

        DebugPlacements++;
        return pipeline.SetVoxel(Hit.Previous, materialId);
    }

    /// <summary>Voxels placed by the debug bind. Zero on every profile run.</summary>
    public int DebugPlacements { get; private set; }
}

/// <summary>What a bare entangled click takes: the tier's sphere, the brush cell's 1 m cube, or the 8 m chunk.</summary>
/// <remarks>
/// Three shapes and one gate. Whichever is chosen, <see cref="Carve"/>'s hardness gate is still
/// the only gate in the game — the mode changes how far, how fast and how big, never what a
/// bite is hard enough to take (M5d-2's rule, its bite clause reversed on 2026-09-03 and its
/// hardness clause untouched since).
/// </remarks>
public enum EntangledBite
{
    /// <summary>The tier's own sphere at range — <c>--entanglesphere</c>, the arm the others were priced against.</summary>
    Sphere,

    /// <summary>The brush cell — a 4-voxel cube on the 1 m brush lattice — the default since 2026-09-14.</summary>
    Cube,

    /// <summary>The whole 8 m chunk the ray hit — <c>--entanglechunk</c>, the default from 2026-09-03 to 2026-09-14.</summary>
    Chunk,
}

