using System.Numerics;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;
using Overburden.Core.Voxels;

namespace Overburden.Core.Mining;

/// <summary>What one swing did, with every voxel it looked at accounted for.</summary>
/// <param name="Candidates">
/// Voxels inside the shape. The sum of the four counts below, always — see
/// <see cref="IsAccounted"/>.
/// </param>
/// <param name="Removed">Voxels that came away.</param>
/// <param name="TooHard">
/// Voxels the tool could not take. Not a failure — it is the mechanic: the stone seam left
/// standing proud of the dirt bank is this number.
/// </param>
/// <param name="AlreadyAir">
/// Voxels inside the shape that were empty. A sphere centred on a surface is roughly half
/// air, so this is large and normal; it is separate from <see cref="Removed"/> because
/// <see cref="Removed"/> is the volume M6's inventory will read, and air is not volume.
/// </param>
/// <param name="Unreachable">
/// Voxels inside the shape whose chunk is not loaded, or is loaded but has not generated.
/// <b>An unknown, not a refusal</b> — some of these may have been air. It is separate from
/// <see cref="TooHard"/> for the reason <see cref="RayOutcome.Blocked"/> is separate from
/// <see cref="RayOutcome.Missed"/>.
/// </param>
/// <param name="Unpowered">
/// Voxels the tool could take and the budget could not pay for (M10c-3, decision 6). Counted
/// <em>after</em> <see cref="TooHard"/>, so a voxel the tool cannot take costs nothing, and
/// after air, so a bite into an empty cell is free. <b>A bucket rather than a refusal of the
/// whole swing</b>, because a pack that ends mid-bite is what every correct caller produces —
/// <em>takes what the charge buys and stops</em> — and a swing refused whole would strand the
/// pack's last few kilojoules and read as a cliff instead of a running-down.
/// </param>
public readonly record struct CarveResult(
    int Candidates,
    int Removed,
    int TooHard,
    int AlreadyAir,
    int Unreachable,
    int Unpowered = 0)
{
    /// <summary>The whole shape was inside loaded, generated terrain.</summary>
    /// <remarks>
    /// False is the case worth surfacing at M3d: a swing at the edge of the loaded world
    /// silently did less than it looked like it did, and the player has no way to tell from
    /// the hole. Reporting it beats dropping it.
    /// </remarks>
    public bool Complete => Unreachable == 0;

    /// <summary>Every candidate landed in exactly one bucket.</summary>
    /// <remarks>
    /// Asserted by every carve test rather than by one, because the failure it catches is a
    /// <c>continue</c> added later that is counted nowhere: the report stays plausible and
    /// quietly loses voxels. A total that has to add up is the cheapest way to make that
    /// impossible to add by accident.
    /// </remarks>
    public bool IsAccounted => Candidates == Removed + TooHard + AlreadyAir + Unreachable + Unpowered;

    public override string ToString() =>
        $"{Candidates} candidate(s): removed {Removed}, too hard {TooHard}, " +
        $"already air {AlreadyAir}, unreachable {Unreachable}" +
        (Unpowered > 0 ? $", unpowered {Unpowered}" : string.Empty);
}

/// <summary>
/// Removing material in a shape, gated per voxel by what the tool is hard enough to take.
/// </summary>
/// <remarks>
/// <para>
/// <b>"Sub-voxel" means the voxel is small, not that it is partly full.</b> Worth saying
/// plainly because the phrase invites the other reading. A voxel is one material or another;
/// the feeling of carving rather than deleting cubes comes from the 0.25 m grid, where a
/// 0.5 m pick is a bite four voxels across. Partial occupancy would be the two-world-
/// representations decision DESIGN already rejected, arrived at sideways.
/// </para>
/// <para>
/// Shapes are methods on this class rather than an <c>IShape</c> the caller supplies. Cones
/// and other tiers are later work and will be <c>Carve.Cone</c> with the same signature
/// shape; there is no second implementer yet, and a cone wants an angle and a length rather
/// than a radius, so the interface they would share is not yet knowable.
/// </para>
/// <para>
/// <b>Anything that is not air is removable, which stops being true at M8.</b> When fluids
/// exist, a carve through water would turn it into air instead of letting it flow, and this
/// wants a removability predicate rather than a comparison against
/// <see cref="MaterialIds.AirNumericId"/>. That is the recorded revisit trigger, and it is
/// the same one <see cref="VoxelRay"/> already carries for its stop test.
/// </para>
/// </remarks>
public static class Carve
{
    /// <summary>Swings <paramref name="tool"/> at <paramref name="centreMetres"/>.</summary>
    /// <param name="into">
    /// Credited one voxel per material actually removed, or null to mine into nothing.
    /// <b>Per material rather than per swing</b>, because a bite through a dirt bank with a stone
    /// seam in it takes two materials and <see cref="CarveResult.Removed"/> is one number —
    /// crediting from the result would have to guess which. Defaulting to null is what keeps
    /// every pre-M6c caller and test compiling unedited, the same shape as
    /// <c>ChunkPipeline</c>'s optional store.
    /// </param>
    /// <param name="voxelBudget">
    /// How many voxels this swing may remove before the rest land in
    /// <see cref="CarveResult.Unpowered"/> — what the equipped pack can pay for at the tool's
    /// <see cref="ToolPower.JoulesPerVoxel"/>, computed by the caller (M10c-3). Unbounded by
    /// default, which is every tool without a <c>power</c> block and every caller before it.
    /// </param>
    public static CarveResult Swing(
        IVoxelEditor world,
        MaterialTable materials,
        Vector3 centreMetres,
        ToolDefinition tool,
        VolumetricInventory? into = null,
        IMachineBodies? bodies = null,
        int voxelBudget = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return Sphere(world, materials, centreMetres, tool.RadiusMetres, tool.HardnessThreshold, into, bodies, voxelBudget);
    }

    /// <summary>Swings <paramref name="tool"/> at what a raycast found.</summary>
    /// <remarks>
    /// The overload that exists so the centring decision lives in one place rather than at
    /// every call site. See <see cref="SwingCentre"/> for which point it picks and why.
    /// </remarks>
    public static CarveResult Swing(
        IVoxelEditor world,
        MaterialTable materials,
        in VoxelHit hit,
        ToolDefinition tool,
        VolumetricInventory? into = null,
        IMachineBodies? bodies = null,
        int voxelBudget = int.MaxValue) =>
        Swing(world, materials, SwingCentre(hit), tool, into, bodies, voxelBudget);

    /// <summary>
    /// Takes <b>exactly the voxel the ray hit</b> with <paramref name="tool"/>, and nothing
    /// else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The precision counterpart to <see cref="Swing"/>, and deliberately <em>not</em> the
    /// default. DESIGN's "mining is sculpting" holds: a bare swing still removes a sphere,
    /// because this is a game about a mining operation and <c>hand_pick</c>'s sphere is about
    /// seventeen voxels against this one — a single-voxel default would be roughly 17× slower
    /// to excavate with the starting tool. Precision is what you reach for when you have
    /// decided to build something, and is worth a held key at that point.
    /// </para>
    /// <para>
    /// <b>The hardness gate is identical, which is what keeps tiers meaningful.</b> A
    /// <c>hand_pick</c> cannot chip a voxel of stone any more than it can sphere one out;
    /// precision changes how much a swing takes, never what it is strong enough to take.
    /// Cooldown is the tool's too — same swing, different shape.
    /// </para>
    /// <para>
    /// <b>No <see cref="SwingCentre"/> here, and that is the point.</b> A sphere is centred on
    /// the hit voxel's centre because a radius has to mean the same thing from every angle;
    /// this takes the hit voxel itself, so <c>VoxelHighlight</c>'s outline stops being an
    /// indication of roughly where the bite lands and becomes literally the thing that is
    /// about to be removed.
    /// </para>
    /// </remarks>
    public static CarveResult Chip(
        IVoxelEditor world,
        MaterialTable materials,
        in VoxelHit hit,
        ToolDefinition tool,
        VolumetricInventory? into = null,
        IMachineBodies? bodies = null,
        int voxelBudget = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return Voxel(world, materials, hit.Voxel, tool.HardnessThreshold, into, bodies, voxelBudget);
    }

    /// <summary>
    /// Removes one voxel, if it is there and soft enough for
    /// <paramref name="hardnessThreshold"/>.
    /// </summary>
    /// <remarks>
    /// A shape like <see cref="Sphere"/> is, of exactly one voxel, and it shares
    /// <see cref="Take"/> with it rather than restating the gate. The four buckets still add
    /// up to <see cref="CarveResult.Candidates"/> — one — so
    /// <see cref="CarveResult.IsAccounted"/> and <see cref="CarveResult.Complete"/> mean the
    /// same things here as everywhere else and a caller can total the two shapes together.
    /// </remarks>
    public static CarveResult Voxel(
        IVoxelEditor world,
        MaterialTable materials,
        VoxelPos voxel,
        float hardnessThreshold,
        VolumetricInventory? into = null,
        IMachineBodies? bodies = null,
        int voxelBudget = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        if (float.IsNaN(hardnessThreshold))
        {
            // The same failure-open trap Sphere documents: every comparison with NaN is false,
            // so `hardness > threshold` never holds and the gate inverts from "takes nothing"
            // to "takes anything", bedrock included.
            throw new ArgumentOutOfRangeException(
                nameof(hardnessThreshold),
                hardnessThreshold,
                "A hardness threshold of NaN would compare false against every material and "
                + "so remove all of them. Use float.NegativeInfinity for a tool that takes nothing.");
        }

        return Take(world, materials, voxel, hardnessThreshold, into, bodies, ref voxelBudget) switch
        {
            Bucket.Removed => new CarveResult(1, 1, 0, 0, 0),
            Bucket.TooHard => new CarveResult(1, 0, 1, 0, 0),
            Bucket.AlreadyAir => new CarveResult(1, 0, 0, 1, 0),
            Bucket.Unpowered => new CarveResult(1, 0, 0, 0, 0, 1),
            _ => new CarveResult(1, 0, 0, 0, 1),
        };
    }

    /// <summary>
    /// Takes <b>the whole chunk</b> the ray hit — an 8 m cube, 32,768 voxels — gated per
    /// voxel by the same hardness rule as every other shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The entangled miner's bite, and it is the first shape here that is not a shape.</b>
    /// A sphere and a single voxel are both defined in metres about a point the player aimed
    /// at; this one is defined by the world's own storage grid, which the player cannot see.
    /// That is deliberate and it is the whole reason the mode is interesting to build: it is a
    /// machine that resolves matter a *chunk* at a time because that is the unit its
    /// entanglement lattice is indexed by, not a bigger pick.
    /// </para>
    /// <para>
    /// <b>It reverses half of a rule recorded twice, and the surviving half is the important
    /// one.</b> <c>MiningController.Entangled</c> has said since M5d that entanglement "changes
    /// how far and how fast, never how big a bite or what the bite is hard enough to take". The
    /// bite clause is now gone. <b>The hardness clause is not, and it is what stops this being a
    /// delete key</b>: a chunk bite through a bedrock floor leaves the bedrock standing exactly
    /// as a <c>hand_pick</c> leaves a stone seam, so tiers still mean what they mean and
    /// <see cref="Take"/> is still the only gate in the game. A mode that bypassed it would make
    /// M3c's whole argument bypassable by pressing Q. (<b>Since M10c-3 the threshold Q swings is
    /// the entangled miner's own — content, 5.0 — and the gate gained a second half, the budget</b>:
    /// <see cref="Take"/> is still the one place a voxel is refused, and the thing that makes Q
    /// earned is the pack that budget comes from.)
    /// </para>
    /// <para>
    /// <b>Which chunk is decided by <c>hit.Voxel</c>, never by a rounded centre.</b> A chunk
    /// boundary is invisible in the world, so aiming a hand's width to the left of one takes the
    /// cube on the left; there is no version of this that is not sometimes surprising, and the
    /// answer is to <em>draw</em> the boundary rather than to soften the rule —
    /// <c>VoxelHighlight</c> outlines the whole 8 m cube while this mode is armed, which is the
    /// same argument M6c-3 made for outlining the brush cell.
    /// </para>
    /// <para>
    /// <b>There is no weight, no encumbrance and no carry limit, and that is a decision rather
    /// than an omission.</b> One chunk of solid rock is 512,000 litres into
    /// <see cref="VolumetricInventory"/> — a number that in most games would exist to be a
    /// problem. It is not one here: being over-encumbered has never been fun, and the honest
    /// player response to a haulage limit is to go and find a bigger container rather than to
    /// mine less, so the mechanic taxes patience and calls it difficulty. The inventory counts
    /// in <c>long</c>, so the arithmetic does not care either. <b>Revisit trigger: a survival
    /// mode</b>, which is already the recorded trigger for shortening reach — the two would be
    /// decided together or not at all.
    /// </para>
    /// <para>
    /// <b>Cost is bounded and known in advance, which a radius is not.</b> 32,768 candidates
    /// exactly, whatever the aim, so unlike <see cref="Sphere"/> there is no argument to check
    /// and nothing a mod file can write that makes the loop longer. What it is not is
    /// <em>cheap</em> — see <c>MiningController.EntangledChunkCooldownSeconds</c>, which is the
    /// rate this bite is paced at and is a measured number rather than the sphere's.
    /// </para>
    /// </remarks>
    public static CarveResult Chunk(
        IVoxelEditor world,
        MaterialTable materials,
        in VoxelHit hit,
        ToolDefinition tool,
        VolumetricInventory? into = null,
        IMachineBodies? bodies = null,
        int voxelBudget = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return Chunk(world, materials, hit.Voxel.Chunk, tool.HardnessThreshold, into, bodies, voxelBudget);
    }

    /// <summary>Takes every voxel of <paramref name="chunk"/> the threshold allows.</summary>
    /// <remarks>
    /// The named-chunk overload, so that the decision about <em>which</em> chunk a hit maps to
    /// lives in exactly one place above rather than at each call site — the same separation
    /// <see cref="SwingCentre"/> gives the sphere.
    /// </remarks>
    public static CarveResult Chunk(
        IVoxelEditor world,
        MaterialTable materials,
        ChunkPos chunk,
        float hardnessThreshold,
        VolumetricInventory? into = null,
        IMachineBodies? bodies = null,
        int voxelBudget = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        if (float.IsNaN(hardnessThreshold))
        {
            // The same failure-open trap Sphere and Voxel document, and it matters most here:
            // every comparison with NaN is false, so the gate inverts from "takes nothing" to
            // "takes everything" over 32,768 voxels at once rather than over seventeen.
            throw new ArgumentOutOfRangeException(
                nameof(hardnessThreshold),
                hardnessThreshold,
                "A hardness threshold of NaN would compare false against every material and "
                + "so remove all of them. Use float.NegativeInfinity for a tool that takes nothing.");
        }

        VoxelPos origin = chunk.OriginVoxel;

        int candidates = 0;
        int removed = 0;
        int tooHard = 0;
        int alreadyAir = 0;
        int unreachable = 0;
        int unpowered = 0;

        // Z outer and Y inner, matching the [z][x][y] storage order the writes land in. It is
        // the same loop nest as Sphere's and for the same reason, and here it covers the array
        // exactly once in its own order rather than approximately.
        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        for (int x = 0; x < VoxelConstants.ChunkSize; x++)
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
            var voxel = new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z);
            candidates++;

            switch (Take(world, materials, voxel, hardnessThreshold, into, bodies, ref voxelBudget))
            {
                case Bucket.Removed: removed++; break;
                case Bucket.TooHard: tooHard++; break;
                case Bucket.AlreadyAir: alreadyAir++; break;
                case Bucket.Unpowered: unpowered++; break;
                default: unreachable++; break;
            }
        }

        return new CarveResult(candidates, removed, tooHard, alreadyAir, unreachable, unpowered);
    }

    /// <summary>The cube bite's edge in voxels: the brush cell's four, a 1 m cube, 512 to a chunk.</summary>
    /// <remarks>
    /// <b><see cref="BrushCell.Voxels"/> and not a number of its own</b>, because the ask was the
    /// right click's grid — <i>"match the 4x4x4 grid on the right click function"</i> — and two
    /// constants that agree the day they are written are the <c>IsCaveAt</c>/<c>GenerateTerrain</c>
    /// argument: the bite and the cube brush are one lattice, so a hole dug is a cell the brush
    /// fills back exactly. The cell divides the chunk, so a cube never straddles a chunk face and
    /// 512 cube bites are one chunk bite exactly. (First built at eight on a misreading of "8x8",
    /// corrected the same night.)
    /// </remarks>
    public const int CubeVoxels = BrushCell.Voxels;

    /// <summary>The cube of the brush lattice that holds <paramref name="voxel"/>, by its origin.</summary>
    /// <remarks>
    /// Snapped to a lattice rather than centred on the hit voxel, and that is the whole of what
    /// makes the shape a miner's: consecutive bites tile into flat-walled rooms instead of
    /// overlapping into ragged one-voxel steps, which is what the brush lattice buys placement
    /// and what the chunk lattice buys the chunk bite. The brush's own snap — a shift, never a
    /// division, so voxel −1 is in the cell at −4 and no seam runs through the origin.
    /// </remarks>
    public static VoxelPos CubeOrigin(VoxelPos voxel) => BrushCell.FromVoxel(voxel).OriginVoxel;

    /// <summary>
    /// Takes the 1 m cube of the brush lattice that holds the hit voxel — the entangled miner's
    /// bite since 2026-09-14, between the tier's sphere and the whole chunk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Matthew's ask: the cube brush's own cell, dug</b> — the chunk bite was the right scale
    /// for flooding a test mine and the wrong one for shaping anything, 512 m³ gone a press with
    /// the seam drawn nowhere. The brush cell is the lattice a builder already works on, and a
    /// bite on the same lattice means what the right click puts down the left click takes up,
    /// cell for cell.
    /// </para>
    /// <para>
    /// Same <see cref="Take"/>, same gate, same buckets as the other three shapes. Sixty-four
    /// candidates exactly, whatever the aim — bounded like the chunk's and for the same reason.
    /// </para>
    /// </remarks>
    public static CarveResult Cube(
        IVoxelEditor world,
        MaterialTable materials,
        in VoxelHit hit,
        ToolDefinition tool,
        VolumetricInventory? into = null,
        IMachineBodies? bodies = null,
        int voxelBudget = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return Cube(world, materials, CubeOrigin(hit.Voxel), tool.HardnessThreshold, into, bodies, voxelBudget);
    }

    /// <summary>Takes every voxel of the cube at <paramref name="origin"/> the threshold allows.</summary>
    /// <remarks>
    /// The named-origin overload, the way <see cref="Chunk(IVoxelEditor, MaterialTable, ChunkPos, float, VolumetricInventory?)"/>
    /// takes a named chunk: which cube a hit maps to is <see cref="CubeOrigin"/>'s decision and
    /// nobody else's. The origin is not snapped here — a caller naming one off the lattice gets
    /// the cube it named.
    /// </remarks>
    public static CarveResult Cube(
        IVoxelEditor world,
        MaterialTable materials,
        VoxelPos origin,
        float hardnessThreshold,
        VolumetricInventory? into = null,
        IMachineBodies? bodies = null,
        int voxelBudget = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        if (float.IsNaN(hardnessThreshold))
        {
            // The same failure-open trap the other shapes document.
            throw new ArgumentOutOfRangeException(
                nameof(hardnessThreshold),
                hardnessThreshold,
                "A hardness threshold of NaN would compare false against every material and "
                + "so remove all of them. Use float.NegativeInfinity for a tool that takes nothing.");
        }

        return Block(world, materials, origin, CubeVoxels, hardnessThreshold, into, bodies, voxelBudget);
    }

    /// <summary>The tunnel bite's edge in voxels: <see cref="TunnelLattice.Voxels"/>, a 2 m cube, 512 voxels, 64 to a chunk.</summary>
    public const int TunnelVoxels = TunnelLattice.Voxels;

    /// <summary>The 2 m cube of the tunnel lattice that holds <paramref name="voxel"/>, by its origin.</summary>
    public static VoxelPos TunnelOrigin(VoxelPos voxel) => TunnelLattice.OriginOf(voxel);

    /// <summary>
    /// Takes the 2 m cube of the tunnel lattice that holds the hit voxel — the quantum
    /// tunneller's bite (2026-09-23), between the miner's 1 m cube and the excavator's 8 m chunk.
    /// </summary>
    /// <remarks>
    /// The cube's rule one lattice level up: snapped so consecutive bites tile into a
    /// flat-walled 2 × 2 m bore, from <see cref="VoxelHit.Voxel"/> (mining names what you point
    /// at), through the same <see cref="Take"/> and gate as every other shape. Five hundred and
    /// twelve candidates exactly, whatever the aim.
    /// </remarks>
    public static CarveResult Tunnel(
        IVoxelEditor world,
        MaterialTable materials,
        in VoxelHit hit,
        ToolDefinition tool,
        VolumetricInventory? into = null,
        IMachineBodies? bodies = null,
        int voxelBudget = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return Tunnel(world, materials, TunnelOrigin(hit.Voxel), tool.HardnessThreshold, into, bodies, voxelBudget);
    }

    /// <summary>Takes every voxel of the 2 m cube at <paramref name="origin"/> the threshold allows. Not snapped, like the cube's.</summary>
    public static CarveResult Tunnel(
        IVoxelEditor world,
        MaterialTable materials,
        VoxelPos origin,
        float hardnessThreshold,
        VolumetricInventory? into = null,
        IMachineBodies? bodies = null,
        int voxelBudget = int.MaxValue) =>
        Block(world, materials, origin, TunnelVoxels, hardnessThreshold, into, bodies, voxelBudget);

    /// <summary>The cube and the tunnel: every voxel of an axis-aligned cube <paramref name="edge"/> voxels a side, through <see cref="Take"/>.</summary>
    private static CarveResult Block(
        IVoxelEditor world,
        MaterialTable materials,
        VoxelPos origin,
        int edge,
        float hardnessThreshold,
        VolumetricInventory? into,
        IMachineBodies? bodies,
        int voxelBudget)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        if (float.IsNaN(hardnessThreshold))
        {
            // The same failure-open trap the other shapes document.
            throw new ArgumentOutOfRangeException(
                nameof(hardnessThreshold),
                hardnessThreshold,
                "A hardness threshold of NaN would compare false against every material and "
                + "so remove all of them. Use float.NegativeInfinity for a tool that takes nothing.");
        }

        int candidates = 0;
        int removed = 0;
        int tooHard = 0;
        int alreadyAir = 0;
        int unreachable = 0;
        int unpowered = 0;

        // Chunk's loop nest at the block's size: Z outer, Y inner, the storage order.
        for (int z = 0; z < edge; z++)
        for (int x = 0; x < edge; x++)
        for (int y = 0; y < edge; y++)
        {
            var voxel = new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z);
            candidates++;

            switch (Take(world, materials, voxel, hardnessThreshold, into, bodies, ref voxelBudget))
            {
                case Bucket.Removed: removed++; break;
                case Bucket.TooHard: tooHard++; break;
                case Bucket.AlreadyAir: alreadyAir++; break;
                case Bucket.Unpowered: unpowered++; break;
                default: unreachable++; break;
            }
        }

        return new CarveResult(candidates, removed, tooHard, alreadyAir, unreachable, unpowered);
    }

    /// <summary>
    /// Where a swing lands, given what the player is pointing at: <b>the centre of the hit
    /// voxel</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The alternative was <see cref="VoxelHit.DistanceMetres"/> along the ray — the point
    /// where it entered the voxel. That point lies exactly on a voxel face, so roughly half
    /// the sphere lands in air already, and how much of it does depends on the angle of
    /// incidence: the same tool takes a visibly bigger bite square-on than at a glance. A
    /// radius written in a mod file would mean something different on every swing.
    /// </para>
    /// <para>
    /// The voxel centre is grid-aligned, so the bite is the size the JSON says, it is the
    /// same from every angle, and swinging twice at the same voxel does the same thing twice.
    /// The cost is honest and small: aim near the edge of a voxel and the hole is centred a
    /// little off where the crosshair sat, up to an eighth of a metre. At 0.25 m voxels with
    /// a highlight box drawn around the target, that is a discrepancy the player can see the
    /// reason for.
    /// </para>
    /// <para>
    /// Recorded rather than tuned: <see cref="CarveResult.AlreadyAir"/> against
    /// <see cref="CarveResult.Candidates"/> is the number that would argue for the other
    /// choice, and it is reported on every swing.
    /// </para>
    /// </remarks>
    public static Vector3 SwingCentre(in VoxelHit hit)
    {
        (float x, float y, float z) = hit.Voxel.CentreMetres;
        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Removes every voxel whose <b>centre</b> lies within <paramref name="radiusMetres"/> of
    /// <paramref name="centreMetres"/> and whose material is soft enough for
    /// <paramref name="hardnessThreshold"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hardness gates removal per voxel, and that is the interesting behaviour.</b> An
    /// early pick swung at a dirt bank with a stone seam through it takes the dirt and leaves
    /// the stone standing. No special case, no "you cannot mine this" message — the shape of
    /// the hole tells the player what their tool is for. The comparison is "at or below": a
    /// tier written to mine stone whose threshold is stone's hardness must mine stone, or
    /// every number in every mod's JSON is a lie by an epsilon.
    /// </para>
    /// <para>
    /// <b>Air is skipped by numeric identity, never by hardness.</b> Air's hardness is 0, so
    /// it passes every threshold there is, and a sphere centred on a surface is roughly half
    /// air. Counting it as removed would make every swing report about double what it took —
    /// and <see cref="CarveResult.Removed"/> is the number M6 reads as a volume.
    /// </para>
    /// <para>
    /// <b><see cref="Contains"/> is the definition; <see cref="Bounds"/> is only an
    /// optimisation.</b> One predicate decides membership, and it is the one the tests check.
    /// </para>
    /// </remarks>
    /// <param name="radiusMetres">
    /// Metres, never voxels — DESIGN specifies tools in metres, and converting at each call
    /// site is how the two units drift apart. Zero or less takes nothing; NaN or past
    /// <see cref="ToolDefinition.MaxRadiusMetres"/> throws. That last one is re-checked here
    /// rather than trusted from the loader, because the loader is not the only caller and a
    /// cubic scan is not a failure worth discovering by watching the game stop.
    /// </param>
    public static CarveResult Sphere(
        IVoxelEditor world,
        MaterialTable materials,
        Vector3 centreMetres,
        float radiusMetres,
        float hardnessThreshold,
        VolumetricInventory? into = null,
        IMachineBodies? bodies = null,
        int voxelBudget = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        if (float.IsNaN(radiusMetres)
            || radiusMetres < 0.0f
            || radiusMetres > ToolDefinition.MaxRadiusMetres)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radiusMetres),
                radiusMetres,
                $"A carve radius must be a number within 0..{ToolDefinition.MaxRadiusMetres} m.");
        }

        if (!float.IsFinite(centreMetres.X)
            || !float.IsFinite(centreMetres.Y)
            || !float.IsFinite(centreMetres.Z))
        {
            throw new ArgumentException("A carve needs a finite centre.", nameof(centreMetres));
        }

        if (float.IsNaN(hardnessThreshold))
        {
            // Not pedantry, and the one unusable value that fails *open*. Every comparison
            // with NaN is false, so `hardness > threshold` never holds and the gate inverts:
            // a NaN threshold does not mine nothing, it mines everything, bedrock included.
            // The loader rejects non-finite thresholds, but the loader is not the only caller.
            throw new ArgumentOutOfRangeException(
                nameof(hardnessThreshold),
                hardnessThreshold,
                "A hardness threshold of NaN would compare false against every material and "
                + "so remove all of them. Use float.NegativeInfinity for a tool that takes nothing.");
        }

        if (radiusMetres == 0.0f)
        {
            // A no-op rather than a throw, because a tool with no reach is a legal if useless
            // tier — a negative one is a bug and threw above. And a *clean* no-op rather than
            // the knife edge the geometry gives on its own: at exactly zero the test reads
            // "distance 0 ≤ 0", so a swing aimed at precisely a voxel centre would take one
            // voxel while a swing a nanometre away took none. Nothing should ever turn on
            // which side of that a float lands, and ToolDefinition.Unknown is a radius of
            // exactly zero aimed by whatever M3d decides to aim it with.
            return default;
        }

        (VoxelPos min, VoxelPos max) = VoxelSphere.Bounds(centreMetres, radiusMetres);
        float radiusSquared = radiusMetres * radiusMetres;

        int candidates = 0;
        int removed = 0;
        int tooHard = 0;
        int alreadyAir = 0;
        int unreachable = 0;
        int unpowered = 0;

        // Z outer and Y inner, matching the [z][x][y] storage order the writes land in.
        for (int z = min.Z; z <= max.Z; z++)
        for (int x = min.X; x <= max.X; x++)
        for (int y = min.Y; y <= max.Y; y++)
        {
            var voxel = new VoxelPos(x, y, z);
            if (!VoxelSphere.Contains(voxel, centreMetres, radiusSquared))
            {
                continue;
            }

            // Counted here and nowhere else, so that every path below has to land in exactly
            // one bucket. Counting over the box instead would be worse than untidy: the box
            // corners reach r(√3 − 1) further out than the sphere ever does, so Unreachable
            // would be non-zero for most swings anywhere near the loaded edge and Complete
            // would stop meaning anything.
            candidates++;

            switch (Take(world, materials, voxel, hardnessThreshold, into, bodies, ref voxelBudget))
            {
                case Bucket.Removed: removed++; break;
                case Bucket.TooHard: tooHard++; break;
                case Bucket.AlreadyAir: alreadyAir++; break;
                case Bucket.Unpowered: unpowered++; break;
                default: unreachable++; break;
            }
        }

        return new CarveResult(candidates, removed, tooHard, alreadyAir, unreachable, unpowered);
    }

    /// <summary>Which bucket one voxel landed in.</summary>
    private enum Bucket
    {
        Removed,
        TooHard,
        AlreadyAir,
        Unreachable,
        Unpowered,
    }

    /// <summary>
    /// Tries to take one voxel, and says which of the four things happened.
    /// </summary>
    /// <remarks>
    /// <b>One definition of the gate, shared by every shape.</b> <see cref="Sphere"/> and
    /// <see cref="Voxel"/> differ only in which voxels they offer up; what happens to a voxel
    /// once offered — unloaded, already air, too hard, taken — is this, and a second copy is a
    /// second thing to drift. It is the same argument <c>WorldGenerator</c> makes about
    /// <c>IsCaveAt</c> and <c>GenerateTerrain</c> sharing one definition of the cave field.
    /// <b>The budget is the fifth thing that can happen to a voxel, and it is asked last</b>: a
    /// voxel that is unreachable, air or too hard never reaches the budget, so the pack pays for
    /// what came away and nothing else (M10c-3).
    /// </remarks>
    private static Bucket Take(
        IVoxelEditor world,
        MaterialTable materials,
        VoxelPos voxel,
        float hardnessThreshold,
        VolumetricInventory? into,
        IMachineBodies? bodies,
        ref int voxelBudget)
    {
        if (!world.TryGetMaterial(voxel, out int material))
        {
            return Bucket.Unreachable;
        }

        // By numeric identity, never by hardness: air's hardness is 0 and so passes every
        // threshold there is. Counting it as removed would roughly double what a swing
        // reports, and Removed is the number M6's inventory reads as a volume.
        if (material == MaterialIds.AirNumericId)
        {
            return Bucket.AlreadyAir;
        }

        // A vault holding anything reads as too hard until its contents have somewhere to go (IM-3's
        // pass, decision 3) — asked before the hardness check, so a refused pick carves nothing.
        if (bodies is not null && materials.IsMachine(material) && bodies.Refuses(voxel, material))
        {
            return Bucket.TooHard;
        }

        if (materials.Hardness(material) > hardnessThreshold)
        {
            return Bucket.TooHard;
        }

        // The gate's other half (M10c-3): the tool could take it and the pack cannot pay for it.
        // Asked here and not earlier, so a voxel that is air or too hard costs nothing; spent
        // below, after the write, so the budget consumed is exactly Removed and the caller pays
        // for what came away.
        if (voxelBudget <= 0)
        {
            return Bucket.Unpowered;
        }

        // Should not happen: a read that succeeded means the chunk is loaded and generated,
        // which is the same condition the write tests. Reported rather than ignored so that an
        // editor whose two halves ever disagree surfaces the voxel instead of losing it.
        if (!world.SetVoxel(voxel, MaterialIds.AirNumericId))
        {
            return Bucket.Unreachable;
        }

        // Credited here and only here, after the write succeeded. Crediting from the bucket count
        // afterwards would pay for voxels that turned out to be unreachable, and crediting before
        // the write would pay for a hole that was never made. The budget is spent at the same
        // moment for the same reason: what it counts is what came away.
        into?.Add(material, 1);
        voxelBudget--;

        // And the machine set told, after the credit, when the voxel was a body's — the one fact
        // that lets the set tell a pick from a fall at its drain (M10a-2, IMachineBodies). One
        // array read for every other voxel a pick takes.
        if (bodies is not null && materials.IsMachine(material))
        {
            bodies.Taken(voxel, material);
        }

        return Bucket.Removed;
    }

    // The membership predicate and the bounding box moved to VoxelSphere at M6c-1, because
    // Building.Place needs the same sphere: one carved and one placed at the same radius have to
    // be the same voxels, or filling in a hole you just made leaves a rind. The reasoning that
    // was here went with the code.
}
