using System;
using System.Collections.Generic;
using System.Numerics;
using MinerCraft.Core.Building;
using MinerCraft.Core.Content;
using MinerCraft.Core.Inventory;
using MinerCraft.Core.Physics;
using MinerCraft.Core.Streaming;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Game.Player;

/// <summary>Which brush the player is holding.</summary>
/// <remarks>
/// An enum here rather than an <c>IShape</c> in Core, following <c>Place</c>'s own argument: the
/// brushes are methods because a ramp wants an orientation where a sphere wants a radius, so the
/// interface they would share is not yet knowable. Which one a key selects is input state, and
/// input state lives in this project.
/// </remarks>
public enum BrushShape
{
    Cube,
    Slab,
    Ramp,
    Sphere,
}

/// <summary>
/// Turns "the player is pointing there and holding the other button" into placements.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="MiningController"/> and deliberately its twin: free of Godot
/// types, taking an origin and a direction as <see cref="Vector3"/> from
/// <c>System.Numerics</c>, so that <c>--place</c> drives a whole profile window headlessly. A
/// debug bind nobody has run headlessly is a bind that does not work.
/// </para>
/// <para>
/// It lives here rather than in Core for the reason the mining one does: a cooldown is
/// <em>input</em> state — how fast a person may act, not what the world does — and Core has no
/// clock and should not grow one. Where a brush lands and what it fills are Core's, because a
/// multiplayer server would have to agree about both.
/// </para>
/// </remarks>
public sealed class PlaceController(
    ChunkPipeline pipeline,
    MaterialTable materials,
    VolumetricInventory inventory)
{
    /// <summary>
    /// How long between placements, in seconds.
    /// </summary>
    /// <remarks>
    /// <b>Not a tool property, because a brush is not a tool.</b> Mining's rate comes from the
    /// tier's JSON because how fast a pick swings is a property of the pick; placement has no
    /// tier and nothing in the content format describes it. So this is one number on the
    /// controller, chosen to be roughly a mining swing, and M6d is where a person says whether
    /// building at that rate feels right.
    /// </remarks>
    public double CooldownSeconds { get; init; } = 0.2;

    private double _cooldown;

    /// <summary>The brush being placed. Cycled from a key.</summary>
    public BrushShape Brush { get; set; } = BrushShape.Cube;

    /// <summary>
    /// The material being placed, or −1 when the player is carrying nothing.
    /// </summary>
    /// <remarks>
    /// <b>−1 rather than air.</b> Air is a legal numeric ID and placing it is refused by
    /// <c>Place</c> as a carve with no gate, so using it as "nothing selected" would turn an
    /// empty inventory into an exception rather than a no-op.
    /// </remarks>
    public int MaterialId { get; private set; } = -1;

    /// <summary>How far the player can reach, in metres. Supplied by the player, who owns it.</summary>
    /// <remarks>
    /// <b><c>required</c> and defaultless</b>, for the reason <see cref="MiningController.ReachMetres"/>
    /// gives: reach is the player's, and a second default here would be two numbers that agree
    /// until one of them is tuned.
    /// </remarks>
    public required float ReachMetres { get; init; }

    /// <summary>Radius of the sphere brush, in metres.</summary>
    /// <remarks>
    /// Half a cell, so the sphere is the one inscribed in the brush cube and two spheres in
    /// adjacent cells touch rather than overlap.
    /// </remarks>
    public float SphereRadiusMetres { get; init; } = BrushCell.SizeMetres * 0.5f;

    /// <summary>What the last cast found. Drives the cell highlight.</summary>
    public RayOutcome Outcome { get; private set; } = RayOutcome.Missed;

    public VoxelHit Hit { get; private set; }

    /// <summary>Whether the last cast is pointing at somewhere a brush would land.</summary>
    public bool HasTarget => Outcome == RayOutcome.Hit;

    /// <summary>The cell the next brush would fill. Only meaningful while <see cref="HasTarget"/>.</summary>
    public BrushCell TargetCell { get; private set; }

    /// <summary>The single voxel a precise placement would fill.</summary>
    public VoxelPos TargetVoxel { get; private set; }

    /// <summary>Of <see cref="Placements"/>, how many were one voxel rather than a brush.</summary>
    /// <remarks>
    /// Counted for the reason <see cref="MiningController.PreciseSwings"/> is: the two shapes cost
    /// sixty-four times different amounts and share one <c>place ms</c> line, so a run that was
    /// mostly precise would report a cheap number under a label that means "a brush".
    /// </remarks>
    public int PrecisePlacements { get; private set; }

    /// <summary>Which way a ramp would rise, from where the player is looking.</summary>
    public BrushFacing Facing { get; private set; }

    public int Placements { get; private set; }

    public int VoxelsPlaced { get; private set; }

    /// <summary>Voxels a brush met that already held something.</summary>
    /// <remarks>
    /// Only air is replaceable, so pushing a brush into a hillside fills the air around the rock
    /// and reports the rest here. Against <see cref="VoxelsPlaced"/> this is what says whether the
    /// lattice is landing where the player expects: a placement that is nearly all occupied is one
    /// aimed at a cell that was already full.
    /// </remarks>
    public int VoxelsOccupied { get; private set; }

    /// <summary>Voxels refused because they were inside the player's own body.</summary>
    public int VoxelsInsidePlayer { get; private set; }

    /// <summary>Voxels the inventory could not pay for.</summary>
    public int VoxelsOutOfStock { get; private set; }

    /// <summary>Placements that could not reach part of their own brush.</summary>
    public int ClippedPlacements { get; private set; }

    /// <summary>True on the frame a placement ran out of material, for a one-off message.</summary>
    public bool RanOutThisFrame { get; private set; }

    /// <summary>
    /// Casts, and places if the button is held and the brush has cooled down.
    /// </summary>
    /// <param name="body">
    /// The player's box, or null when there is no body to protect. <b>Null while flying</b>: the
    /// body is inert in fly mode and sits wherever the last toggle left it, so passing it would
    /// refuse voxels around a position the player is no longer at.
    /// </param>
    /// <returns>True on a frame where a placement actually happened.</returns>
    /// <param name="precise">
    /// Fill only the voxel under the crosshair instead of the whole brush. <b>A per-frame input,
    /// not a mode this remembers</b> — the same shape as mining's, and for the same reason: it is
    /// the exception rather than the mode, so it belongs to the key that is held rather than to
    /// state that has to be toggled back.
    /// </param>
    public bool Update(
        double delta,
        Vector3 originMetres,
        Vector3 direction,
        Aabb? body,
        bool placing,
        bool precise = false)
    {
        _cooldown = Math.Max(0.0, _cooldown - delta);
        RanOutThisFrame = false;

        // Cast every frame regardless of the button, because the cell highlight has to follow the
        // crosshair and not only the placements.
        Outcome = VoxelRay.Cast(pipeline, originMetres, direction, ReachMetres, out VoxelHit hit);
        Hit = hit;

        if (Outcome == RayOutcome.Hit)
        {
            TargetCell = Place.TargetCell(hit);
            TargetVoxel = Place.TargetVoxel(hit);
            Facing = Place.FacingFrom(direction);
        }

        // Selected here rather than by the caller, so that mining a first bite and then reaching
        // for the other mouse button works without a key press in between. Only ever fills an
        // empty selection; cycling is the player's.
        if (MaterialId < 0 || !inventory.Has(MaterialId))
        {
            MaterialId = LargestHolding();
        }

        if (!placing || _cooldown > 0.0 || Outcome != RayOutcome.Hit || MaterialId < 0)
        {
            return false;
        }

        PlaceResult result = precise
            ? Place.Voxel(pipeline, materials, TargetVoxel, MaterialId, body, inventory)
            : Brush switch
        {
            BrushShape.Cube => Place.Cube(pipeline, materials, TargetCell, MaterialId, body, inventory),
            BrushShape.Slab => Place.Slab(pipeline, materials, TargetCell, MaterialId, body, inventory),
            BrushShape.Ramp =>
                Place.Ramp(pipeline, materials, TargetCell, MaterialId, Facing, body, inventory),
            _ => Place.Sphere(
                pipeline, materials, TargetCell, SphereRadiusMetres, MaterialId, body, inventory),
        };

        // The same cooldown for both shapes, exactly as mining does it. Letting precision fire
        // faster would make it the efficient way to build as well as the accurate one, and the
        // brushes would stop being the default in practice while still being it in the code.
        _cooldown = CooldownSeconds;
        Placements++;

        if (precise)
        {
            PrecisePlacements++;
        }
        VoxelsPlaced += result.Placed;
        VoxelsOccupied += result.Occupied;
        VoxelsInsidePlayer += result.InsidePlayer;
        VoxelsOutOfStock += result.OutOfStock;
        RanOutThisFrame = result.OutOfStock > 0;

        if (!result.Complete)
        {
            ClippedPlacements++;
        }

        return true;
    }

    /// <summary>Moves to the next brush in the cycle.</summary>
    public BrushShape NextBrush() =>
        Brush = Brush == BrushShape.Sphere ? BrushShape.Cube : Brush + 1;

    /// <summary>
    /// Moves to the next material the player is actually carrying.
    /// </summary>
    /// <remarks>
    /// <b>Cycles through the inventory rather than through the material table</b>, because the
    /// selectable set is what you are holding: offering a material you have none of would be a
    /// selection that cannot be acted on, and with five shipped materials most of the list would
    /// be that.
    /// </remarks>
    /// <returns>The material now selected, or −1 if the player is carrying nothing.</returns>
    public int NextMaterial()
    {
        IReadOnlyList<(int MaterialId, long Voxels)> held = inventory.Entries();
        if (held.Count == 0) return MaterialId = -1;

        for (int i = 0; i < held.Count; i++)
        {
            if (held[i].MaterialId > MaterialId)
            {
                return MaterialId = held[i].MaterialId;
            }
        }

        return MaterialId = held[0].MaterialId;
    }

    /// <summary>The material there is most of, or −1 for an empty inventory.</summary>
    private int LargestHolding()
    {
        int best = -1;
        long most = 0;

        foreach ((int materialId, long voxels) in inventory.Entries())
        {
            if (voxels > most)
            {
                most = voxels;
                best = materialId;
            }
        }

        return best;
    }
}
