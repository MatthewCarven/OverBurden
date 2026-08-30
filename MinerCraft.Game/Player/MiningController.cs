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
        Outcome = VoxelRay.Cast(pipeline, originMetres, direction, ReachMetres, out VoxelHit hit);
        Hit = hit;

        if (!mining || _cooldown > 0.0 || Outcome != RayOutcome.Hit)
        {
            return false;
        }

        CarveResult result = precise
            ? Carve.Chip(pipeline, materials, hit, Tool)
            : Carve.Swing(pipeline, materials, hit, Tool);

        // The tool's cooldown either way. A chip is a different shape of the same swing, not a
        // faster one — letting precision fire quicker would make it the efficient way to mine
        // as well as the accurate one, and the sphere would stop being the default in practice.
        _cooldown = Tool.CooldownSeconds;
        Swings++;

        if (precise)
        {
            PreciseSwings++;
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
