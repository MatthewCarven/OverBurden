using System;
using System.Collections.Generic;
using System.Numerics;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;
using Overburden.Core.Physics;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;

namespace Overburden.Game.Player;

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

    /// <summary>
    /// Whether the player is entangled — the building half of <see cref="MiningController.Entangled"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Set from the miner rather than toggled separately, because <c>Q</c> is one mode and not
    /// two.</b> A player who could mine at range but not build at range would have half a tool,
    /// and the asymmetry would show up as the fill silently doing nothing at the distance the bite
    /// works at.
    /// </para>
    /// <para>
    /// It buys the same two things it buys the miner — <b>how far and how fast</b> — plus the
    /// third the bite added: <see cref="TakesWholeChunk"/>. The clause that stays untouched is the
    /// one that always mattered: <b>entanglement never changes what a placement is allowed to
    /// overwrite.</b> Only air is replaceable at 512 m³ exactly as at one, so this cannot become a
    /// way to delete terrain with the building verb.
    /// </para>
    /// <para>
    /// <b>It also makes the <c>L</c>+<c>Q</c> pairing ordinary.</b> Matthew reported that pairing
    /// as load-bearing in the M7 play — <i>"hitting Q then being able to place a light far off
    /// into the distance has been useful"</i> — and the reason it was needed was that right mouse
    /// was bounded by the player's reach while <c>L</c> was not. Alt+right at range is now that,
    /// without the debug lamp. <b>The recorded revisit trigger for that note — <i>a placement verb
    /// that takes a reach argument</i> — is hereby met</b>, and <c>L</c> keeps working.
    /// </para>
    /// </remarks>
    public bool Entangled { get; set; }

    /// <summary>Whether an entangled placement fills a whole chunk. Off restores the brushes.</summary>
    /// <remarks>
    /// The seam <c>--entanglesphere</c> drives, and it drives both halves together: an A/B that
    /// changed the bite and left the fill at chunk scale would be comparing two different tools.
    /// </remarks>
    public bool EntangledFillsChunk { get; set; } = true;

    /// <summary>A bare placement fills the whole 8 m chunk in front of the crosshair.</summary>
    public bool TakesWholeChunk => Entangled && EntangledFillsChunk;

    /// <summary>
    /// How long between whole-chunk fills, in seconds.
    /// </summary>
    /// <remarks>
    /// <b>The same number the bite uses, and deliberately the same flag.</b> One fill and one bite
    /// are the same 32,768 <c>SetVoxel</c> calls, so they cost within a factor of each other and a
    /// person tuning the felt rate of the mode is tuning one thing. <c>--chunkcooldown=</c> moves
    /// both; splitting them would be two numbers for one feel, which is
    /// <see cref="ReachMetres"/>'s own argument.
    /// </remarks>
    public float ChunkCooldownSeconds { get; set; } =
        MiningController.DefaultEntangledChunkCooldownSeconds;

    /// <summary>The entangled brush's cooldown — the cube bite's, so dig and fill pace the same.</summary>
    /// <remarks>
    /// "Make it all the same 4x4x4" (2026-09-14) is the lattice and the rate together: a fill at
    /// the sphere's forty beside a dig at twenty would be the asymmetry the cube was asked to
    /// remove, one axis over. Main sets it to the cube cooldown on the cube arm and leaves the
    /// sphere arm at the sphere's rate.
    /// </remarks>
    public float BrushCooldownSeconds { get; set; } = MiningController.EntangledCooldownSeconds;

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

    /// <summary>The whole chunk an entangled placement would fill. Drives the 8 m outline.</summary>
    public ChunkPos TargetChunk { get; private set; }

    /// <summary>Of <see cref="Placements"/>, how many filled a whole chunk.</summary>
    public int ChunkPlacements { get; private set; }

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

    /// <summary>Voxels refused because they held water (M9d, decision 3), over the session.</summary>
    public int VoxelsWet { get; private set; }

    /// <summary>Placements that could not reach part of their own brush.</summary>
    public int ClippedPlacements { get; private set; }

    /// <summary>
    /// Whether a chunk fill waits for the fluid boundary instead of landing at the click (M9d,
    /// Matthew's word after the first play: <i>"defer the chunk fill to the boundary"</i>).
    /// </summary>
    /// <remarks>
    /// The fill's wet answer is a copy the last publish took, stale by up to a tick; a brush can
    /// lose 64 cells' worth at most to that and a chunk fill 32,768 — the first play's fifth fill
    /// into a chunk mid-flood refused 3,516 wet voxels and still displaced 18,955 L. So the chunk
    /// arm alone waits for the boundary, where the grid is free and a fresh publish makes the copy
    /// exact; the brush and the precision voxel stay instant. Off with no water, where there is no
    /// boundary and nothing to lose.
    /// </remarks>
    public bool DeferChunkFills { get; set; }

    private readonly List<(ChunkPos Chunk, int MaterialId)> _pendingChunkFills = [];

    /// <summary>Chunk fills clicked since the last boundary, waiting for it.</summary>
    public int PendingChunkFills => _pendingChunkFills.Count;

    /// <summary>Chunk fills queued to the boundary, over the session.</summary>
    public int ChunkFillsDeferred { get; private set; }

    /// <summary>
    /// The machine set a placement of a machine material creates its row in (M10a-2). Null
    /// makes a machine material an ordinary voxel to this controller, which no run wants.
    /// </summary>
    public MachineSet? Machines { get; set; }

    /// <summary>
    /// The water a deferred machine placement lifts and returns through — the fluid simulation.
    /// Null under <c>--nowater</c>, where nothing is ever wet and no placement is deferred.
    /// </summary>
    public ICollapseWater? Water { get; set; }

    private readonly List<(BrushCell Cell, int MaterialId, BrushFacing Facing)> _pendingMachines = [];

    /// <summary>Machine placements refused wet and waiting for the boundary.</summary>
    public int PendingMachines => _pendingMachines.Count;

    /// <summary>Machines placed at the click, over the session.</summary>
    public int MachinesPlaced { get; private set; }

    /// <summary>Machine placements queued to the boundary, over the session.</summary>
    public int MachinesDeferred { get; private set; }

    /// <summary>Machines placed at the boundary through the displacing path.</summary>
    public int MachinesPlacedDisplacing { get; private set; }

    /// <summary>Millilitres a deferred placement lifted, returned around the body, and displaced, over the session.</summary>
    public long MachineLifted { get; private set; }

    public long MachineReturned { get; private set; }

    public long MachineDisplaced { get; private set; }

    /// <summary>Whether the selected material is a machine — the HUD's <c>MACHINE</c>.</summary>
    public bool SelectedIsMachine => MaterialId >= 0 && Machines is not null && materials.IsMachine(MaterialId);

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
        Outcome = VoxelRay.Cast(
            pipeline, originMetres, direction, EffectiveReachMetres, out VoxelHit hit);
        Hit = hit;

        if (Outcome == RayOutcome.Hit)
        {
            TargetCell = Place.TargetCell(hit);
            TargetVoxel = Place.TargetVoxel(hit);
            TargetChunk = Place.TargetChunk(hit);
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

        if (SelectedIsMachine)
        {
            // A machine material places as one cell whatever the brush, mode or modifier (M10a's
            // pass, decision 10): the body is the shape. Refused wet whole, it is queued to the
            // boundary where the water can be lifted and returned; refused any other way it is
            // reported as the brush would report it.
            PlaceResult machine = Place.Machine(
                pipeline, materials, TargetCell, MaterialId, Facing, Machines!, body, inventory);
            _cooldown = BrushCooldownSeconds;
            Placements++;

            if (machine.Wet > 0 && Water is not null)
            {
                _pendingMachines.Add((TargetCell, MaterialId, Facing));
                MachinesDeferred++;
                return true;
            }

            if (machine.Placed > 0)
            {
                MachinesPlaced++;
            }

            Record(machine);
            return true;
        }

        if (!precise && TakesWholeChunk && DeferChunkFills)
        {
            // Queued, not placed: the boundary runs every one through RunPendingChunkFills, in
            // click order, with the same tallies. A list rather than a slot, because the click
            // rate is 4 a second against a 2 Hz boundary and a click that vanished would be the
            // one-click-one-click rule broken from the other side.
            _pendingChunkFills.Add((TargetChunk, MaterialId));
            _cooldown = CooldownForShape(precise);
            ChunkFillsDeferred++;
            return true;
        }

        PlaceResult result = precise
            ? Place.Voxel(pipeline, materials, TargetVoxel, MaterialId, body, inventory)
            : TakesWholeChunk
                ? Place.Chunk(pipeline, materials, TargetChunk, MaterialId, body, inventory)
                : Brush switch
        {
            BrushShape.Cube => Place.Cube(pipeline, materials, TargetCell, MaterialId, body, inventory),
            BrushShape.Slab => Place.Slab(pipeline, materials, TargetCell, MaterialId, body, inventory),
            BrushShape.Ramp =>
                Place.Ramp(pipeline, materials, TargetCell, MaterialId, Facing, body, inventory),
            _ => Place.Sphere(
                pipeline, materials, TargetCell, SphereRadiusMetres, MaterialId, body, inventory),
        };

        _cooldown = CooldownForShape(precise);
        Placements++;

        if (precise)
        {
            PrecisePlacements++;
        }
        else if (TakesWholeChunk)
        {
            ChunkPlacements++;
        }
        Record(result);
        return true;
    }

    /// <summary>
    /// Runs every chunk fill clicked since the last boundary, in click order. The caller
    /// publishes the wet copy first, so the refusal reads the grid as it stands and not as it
    /// stood a tick ago.
    /// </summary>
    /// <returns>The results summed, or null with nothing queued.</returns>
    public PlaceResult? RunPendingChunkFills(Aabb? body)
    {
        if (_pendingChunkFills.Count == 0)
        {
            return null;
        }

        var sum = default(PlaceResult);
        foreach ((ChunkPos chunk, int material) in _pendingChunkFills)
        {
            PlaceResult result = Place.Chunk(pipeline, materials, chunk, material, body, inventory);
            Placements++;
            ChunkPlacements++;
            Record(result);
            sum = new PlaceResult(
                sum.Candidates + result.Candidates, sum.Placed + result.Placed, sum.Occupied + result.Occupied,
                sum.InsidePlayer + result.InsidePlayer, sum.Unreachable + result.Unreachable,
                sum.OutOfStock + result.OutOfStock, sum.Wet + result.Wet);
        }

        _pendingChunkFills.Clear();
        return sum;
    }

    /// <summary>
    /// Runs every machine placement refused wet since the last boundary, in click order, on the
    /// displacing path. The caller has published the wet copy and the grid is the frame's.
    /// </summary>
    /// <returns>The placements summed, or null with nothing queued.</returns>
    public MachinePlacement? RunPendingMachinePlacements(Aabb? body)
    {
        if (_pendingMachines.Count == 0 || Machines is null || Water is null)
        {
            _pendingMachines.Clear();
            return null;
        }

        var sum = default(MachinePlacement);
        foreach ((BrushCell cell, int material, BrushFacing facing) in _pendingMachines)
        {
            MachinePlacement placed = Place.MachineDisplacing(
                pipeline, materials, cell, material, facing, Machines, Water, body, inventory);
            Record(placed.Result);
            if (placed.Result.Placed > 0)
            {
                MachinesPlacedDisplacing++;
            }

            MachineLifted += placed.Lifted;
            MachineReturned += placed.Returned;
            MachineDisplaced += placed.Displaced;

            PlaceResult r = placed.Result, s = sum.Result;
            sum = new MachinePlacement(
                new PlaceResult(
                    s.Candidates + r.Candidates, s.Placed + r.Placed, s.Occupied + r.Occupied,
                    s.InsidePlayer + r.InsidePlayer, s.Unreachable + r.Unreachable,
                    s.OutOfStock + r.OutOfStock, s.Wet + r.Wet),
                sum.Lifted + placed.Lifted, sum.Returned + placed.Returned, sum.Displaced + placed.Displaced);
        }

        _pendingMachines.Clear();
        return sum;
    }

    private void Record(PlaceResult result)
    {
        VoxelsPlaced += result.Placed;
        VoxelsOccupied += result.Occupied;
        VoxelsInsidePlayer += result.InsidePlayer;
        VoxelsOutOfStock += result.OutOfStock;
        VoxelsWet += result.Wet;
        RanOutThisFrame = result.OutOfStock > 0;

        if (!result.Complete)
        {
            ClippedPlacements++;
        }
    }

    /// <summary>How far this placement may reach — the player's arm, or the whole loaded world.</summary>
    /// <remarks>
    /// <see cref="MiningController.EntangledReachMetres"/> rather than a second constant, for the
    /// reason <see cref="ReachMetres"/> is <c>required</c> and defaultless: two numbers for one
    /// reach are two things to drift, and a fill that could not reach as far as the bite would be
    /// a mode that can dig a hole it cannot fill in.
    /// </remarks>
    private float EffectiveReachMetres =>
        Entangled ? MiningController.EntangledReachMetres : ReachMetres;

    /// <summary>
    /// How long until the next placement, by the shape that was just placed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Per shape rather than per mode, and the old argument genuinely does not reach.</b> Until
    /// now precision paid the brush's cooldown so that it could not become the <em>efficient</em>
    /// way to build as well as the accurate one. Against a fill of 32,768 voxels that protects
    /// nothing — the chunk fill is unambiguously the bulk method and no rate of single voxels
    /// competes with it — while pacing Alt to the chunk rate would cost the case the mode is most
    /// useful for, placing single things at range. <b>The mining half made exactly this change for
    /// exactly this reason; the two verbs stay symmetrical.</b>
    /// </para>
    /// <para>
    /// The rate chooses the duty cycle and nothing else, because one fill is atomic: a time slice
    /// cannot bound a tail below the cost of one item. That is M2d's mesh-apply finding, and this
    /// is its fourth outing.
    /// </para>
    /// </remarks>
    private double CooldownForShape(bool precise) => (Entangled, precise) switch
    {
        (true, false) when EntangledFillsChunk => ChunkCooldownSeconds,
        (true, false) => BrushCooldownSeconds,
        (true, _) => MiningController.EntangledCooldownSeconds,
        _ => CooldownSeconds,
    };

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

    /// <summary>
    /// Selects the material in hotbar slot <paramref name="slot"/>, zero-based.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An empty slot changes nothing rather than deselecting.</b> Pressing 4 while carrying
    /// three materials is a miss, and a miss that emptied the hand would mean the next click
    /// placed nothing — punishing a mistyped key with a lost placement. The same reasoning as
    /// <see cref="MaterialId"/> being −1 rather than air: the failure has to be a no-op.
    /// </para>
    /// <para>
    /// The ordering the slot indexes into is <c>VolumetricInventory.MaterialInSlot</c>'s, and it
    /// is documented there together with the cost of it — acquiring a new material shifts the
    /// slots above it.
    /// </para>
    /// </remarks>
    /// <returns>The material now selected, or −1 if nothing is.</returns>
    public int SelectSlot(int slot)
    {
        int materialId = inventory.MaterialInSlot(slot);
        return materialId < 0 ? MaterialId : MaterialId = materialId;
    }

    /// <summary>Selects a material by numeric ID, if the inventory holds any of it. The <c>--material=</c> seam.</summary>
    public bool Select(int materialId)
    {
        if (materialId < 0 || !inventory.Has(materialId))
        {
            return false;
        }

        MaterialId = materialId;
        return true;
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
