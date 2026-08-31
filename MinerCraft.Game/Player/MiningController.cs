using System;
using System.Numerics;
using MinerCraft.Core.Content;
using MinerCraft.Core.Mining;
using MinerCraft.Core.Streaming;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Game.Player;

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
    public MinerCraft.Core.Inventory.VolumetricInventory? Inventory { get; init; }

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
    /// Composes with the number keys rather than replacing them: it changes how far and how
    /// fast, never how big a bite or what the bite is hard enough to take. Both of those stay
    /// the tier's, which is what keeps <see cref="Carve"/>'s hardness gate the only gate.
    /// </para>
    /// </remarks>
    public bool Entangled { get; set; }

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

    /// <summary>Eight swings a second, fixed, whatever tier is selected.</summary>
    /// <remarks>
    /// <para>
    /// <b>Fixed rather than the tier's</b>, because otherwise the dig rate depends on which
    /// number key was pressed last, and this thing exists to excavate a <em>comparable</em> test
    /// volume for M5d-2's look questions.
    /// </para>
    /// <para>
    /// <b>Raised from 0.25 s at Matthew's request, and the raise inverted a recorded caveat.</b>
    /// At four a second this was marginally *slower* than <c>steel_pick</c>'s own 0.20 s and the
    /// only upgrade was the range. At eight it is faster than every shipped tier, so the mode is
    /// now a strict upgrade on both axes — which is what an endgame tool ought to be, and worth
    /// saying out loud because it means <b>the tier's cooldown no longer bounds anything while
    /// entangled</b>. A tier balanced by its rate would be balanced by nothing here.
    /// </para>
    /// <para>
    /// Not a DoS the way a radius is: the cost per swing is a bounded cast plus a bounded carve,
    /// and doubling the rate doubles a number M3d measured at two orders of magnitude inside a
    /// frame. Re-measured at eight rather than assumed — see the worklog.
    /// </para>
    /// </remarks>
    public const float EntangledCooldownSeconds = 0.125f;

    private float EffectiveReachMetres => Entangled ? EntangledReachMetres : ReachMetres;

    private float EffectiveCooldownSeconds =>
        Entangled ? EntangledCooldownSeconds : Tool.CooldownSeconds;

    /// <summary>Of <see cref="Swings"/>, how many were fired entangled.</summary>
    /// <remarks>
    /// Counted for the reason <see cref="PreciseSwings"/> is: an entangled swing carves at
    /// arbitrary distance from the viewer, which is a different cost from one at 5 m and shares
    /// the same <c>swing ms</c> line. <c>--mine</c> never entangles, so a non-zero value in a
    /// profile means the run is not comparable with M3d's baseline.
    /// </remarks>
    public int EntangledSwings { get; private set; }

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

        CarveResult result = precise
            ? Carve.Chip(pipeline, materials, hit, Tool, Inventory)
            : Carve.Swing(pipeline, materials, hit, Tool, Inventory);

        // The same cooldown for both shapes. A chip is a different shape of the same swing, not
        // a faster one — letting precision fire quicker would make it the efficient way to mine
        // as well as the accurate one, and the sphere would stop being the default in practice.
        // Entangled changes the rate, and it changes it for both shapes, for that same reason.
        _cooldown = EffectiveCooldownSeconds;
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
