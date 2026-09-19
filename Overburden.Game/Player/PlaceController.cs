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

    /// <summary>Whether an entangled placement fills one brush cell at range — the cube arm's fill.</summary>
    /// <remarks>
    /// The fill's own flag rather than <c>MiningController.TakesCube</c>, which Main read until
    /// 2026-09-19: the miner's now includes the hold, and a held Q from the pick must not turn
    /// the 5 m brush into a one-click verb on its way past. The hold runs the bite and nothing
    /// else.
    /// </remarks>
    public bool TakesCube => Entangled && !EntangledFillsChunk;

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
    /// A fill the click refused wet, waiting for the boundary to run it displacing (2026-09-19).
    /// </summary>
    /// <remarks>
    /// The click's path is unchanged — M9d's <c>Wet</c> refusal lands the dry part at once — and
    /// the same shape is queued here with what it was refused, so the boundary can run it again
    /// with <see cref="FillWater"/> in hand: the dry voxels are <c>Occupied</c> by then and cost
    /// nothing, the wet ones are lifted, written and their water returned around the shape.
    /// Matthew's word after the first play that built in water: <i>"absorb the water or displace
    /// it or remove it, your choice … it makes it impossible to build where there is even a
    /// slight amount of water"</i>.
    /// </remarks>
    private readonly record struct PendingFill(
        FillShape Shape, VoxelPos Voxel, BrushCell Cell, ChunkPos Chunk, BrushFacing Facing, float RadiusMetres, int MaterialId, int Wet);

    private enum FillShape { Voxel, Cube, Slab, Ramp, Sphere, Chunk }

    private readonly List<PendingFill> _pendingWetFills = [];

    /// <summary>Fills refused wet since the last boundary, waiting for it.</summary>
    public int PendingWetFills => _pendingWetFills.Count;

    /// <summary>Fills queued to the boundary for their water, over the session.</summary>
    public int WetFillsDeferred { get; private set; }

    /// <summary>Voxels placed into water at the boundary, displacing it, over the session.</summary>
    public int WetVoxelsFilled { get; private set; }

    /// <summary>Millilitres a fill lifted out of its voxels, returned around the shape, and displaced, over the session.</summary>
    public long FillLifted { get; private set; }

    public long FillReturned { get; private set; }

    public long FillDisplaced { get; private set; }

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

    /// <summary>
    /// Whether the selected material is an item in the hand rather than a block (M10b): the
    /// computer today, the ladder's parts at M10g. A right-click never places it; with the
    /// computer it opens the machine under the crosshair.
    /// </summary>
    public bool SelectedIsItem => MaterialId >= 0 && !materials.IsPlaceable(MaterialId);

    /// <summary>
    /// The row a right-click edge with the computer selected landed on this frame, for the
    /// panel to open (b-2); null on every other frame. Set and consumed within one update.
    /// </summary>
    public MachineRow? PanelRequest { get; private set; }

    /// <summary>Right-click edges with the computer selected that landed on a machine body, over the session.</summary>
    public int ComputerOpens { get; private set; }

    /// <summary>Right-click edges with the computer selected that landed on anything else — rock, air, a block — and did nothing.</summary>
    public int ComputerMisses { get; private set; }

    // ---------------------------------------------------------------- M10c-3: the pack and the gate

    /// <summary>The pack the fill at range draws on, or null with none equipped — Main's, handed in.</summary>
    public EquippedPack? Pack { get; set; }

    /// <summary>The fill's half of <c>--nogate</c>: every placement free.</summary>
    public bool PowerOff { get; set; }

    /// <summary>
    /// Millijoules one placed voxel costs at range — the entangled tool's <c>joulesPerVoxel</c>,
    /// set by Main from the same block the bite reads, so the fill is the bite's price. Zero is
    /// free, which is every placement inside the arm's reach and the pre-M10c arm.
    /// </summary>
    public long EntangledMillijoulesPerVoxel { get; set; }

    /// <summary>Whether the selected item is a pack — a carried storage — so a right-click equips it (decision 4).</summary>
    public bool SelectedIsPack =>
        SelectedIsItem && materials.Machine(MaterialId) is { Kind: MachineKind.Storage, Carried: true };

    /// <summary>
    /// The pack material a right-click edge asked to equip this frame, or −1 — set and consumed
    /// within one update, <see cref="PanelRequest"/>'s shape. Main does the equipping, because the
    /// swap touches the inventory, the tick and the header at once.
    /// </summary>
    public int EquipRequest { get; private set; } = -1;

    /// <summary>
    /// True on the frame the computer was clicked on nothing with a pack equipped: the pack's
    /// panel opens (the pass amending M10b's decision 3 by one sentence). With none equipped the
    /// miss stays a miss.
    /// </summary>
    public bool PackPanelRequest { get; private set; }

    /// <summary>Equip requests over the session, and the computer's opens of the pack's panel.</summary>
    public int EquipRequests { get; private set; }

    public int PackPanelOpens { get; private set; }

    /// <summary>Placements at range refused whole for want of the shape's cost in the pack, and the voxels they would have offered.</summary>
    public int UnpoweredPlacements { get; private set; }

    public int VoxelsUnpowered { get; private set; }

    /// <summary>Millijoules the pack paid for placed voxels, over the session.</summary>
    public long MillijoulesPaid { get; private set; }

    /// <summary>True on a frame a placement was refused for want of charge, for a one-off message.</summary>
    public bool RefusedUnpoweredThisFrame { get; private set; }

    /// <summary>What the last refused placement needed, in millijoules, for the message.</summary>
    public long LastRefusedNeedMillijoules { get; private set; }

    /// <summary>What a placement at range costs a voxel now: the tool's price unless the gate is off or the arm is the reach.</summary>
    private long FillCostMillijoules => Entangled && !PowerOff ? EntangledMillijoulesPerVoxel : 0;

    /// <summary>
    /// Whether the pack can pay for <paramref name="voxels"/> at the fill's price — the
    /// all-or-nothing rule (decision 6): a fill at range is refused whole under the shape's
    /// cost rather than left as a partial cell, because half a wall is not a wall and the bite
    /// already has the running-down. Free is always affordable.
    /// </summary>
    private bool CanAfford(int voxels, out long need)
    {
        long cost = FillCostMillijoules;
        need = cost * voxels;
        return cost == 0 || (Pack is not null && Pack.VoxelsAffordable(cost) >= voxels);
    }

    /// <summary>Pays the pack for what a placement put down, at the fill's price.</summary>
    private void PayFor(PlaceResult result)
    {
        long cost = FillCostMillijoules;
        if (cost > 0 && Pack is not null && result.Placed > 0)
        {
            MillijoulesPaid += Pack.Pay(result.Placed, cost);
        }
    }

    private void RefuseUnpowered(int voxels, long need)
    {
        UnpoweredPlacements++;
        VoxelsUnpowered += voxels;
        RefusedUnpoweredThisFrame = true;
        LastRefusedNeedMillijoules = need;
    }

    private bool _placingWasDown;

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
        RefusedUnpoweredThisFrame = false;

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

        // The edge of the button, for the one verb that is a click and not a hold.
        bool clicked = placing && !_placingWasDown;
        _placingWasDown = placing;
        PanelRequest = null;
        EquipRequest = -1;
        PackPanelRequest = false;

        if (SelectedIsPack)
        {
            // M10c-3, decision 4: a right-click edge anywhere with a pack in hand equips one
            // from the count — no target, because a pack goes on a back and not on a wall. Main
            // does the swap, since it touches the inventory, the tick and the header together.
            if (clicked)
            {
                EquipRequest = MaterialId;
                EquipRequests++;
            }

            return false;
        }

        if (SelectedIsItem)
        {
            // An item in the hand is never placed (M10b, decisions 2 and 3): the brush would
            // put 64 voxels of computer into the world on the first click, which is fact 1 of
            // the pass. The computer's verb is a right-click edge on a machine body under the
            // crosshair — the hit voxel's cell, mining's aim rather than building's — and on
            // anything else the click opens the equipped pack's panel (M10c-3) or, with none,
            // does nothing at all.
            if (clicked && Outcome == RayOutcome.Hit
                && Machines is not null
                && pipeline.TryGetMaterial(hit.Voxel, out int hitMaterial)
                && materials.IsMachine(hitMaterial)
                && Machines.TryGet(BrushCell.FromVoxel(hit.Voxel), out MachineRow? row))
            {
                PanelRequest = row;
                ComputerOpens++;
            }
            else if (clicked && Pack is not null)
            {
                PackPanelRequest = true;
                PackPanelOpens++;
            }
            else if (clicked)
            {
                ComputerMisses++;
            }

            return false;
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

        // M10c-3: the fill at range is all-or-nothing on the shape's cost — a voxel, a cell, a
        // chunk — and refused whole under it. Asked before the shape runs, so nothing is placed
        // that the pack then cannot pay for; the cooldown runs so a refused click is a click.
        int offered = precise ? 1 : TakesWholeChunk ? VoxelConstants.ChunkVolume : BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;
        if (!CanAfford(offered, out long need))
        {
            RefuseUnpowered(offered, need);
            _cooldown = CooldownForShape(precise);
            return false;
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
        PayFor(result);

        if (result.Wet > 0 && Water is not null)
        {
            FillShape shape = precise ? FillShape.Voxel
                : TakesWholeChunk ? FillShape.Chunk
                : Brush switch
                {
                    BrushShape.Cube => FillShape.Cube,
                    BrushShape.Slab => FillShape.Slab,
                    BrushShape.Ramp => FillShape.Ramp,
                    _ => FillShape.Sphere,
                };
            _pendingWetFills.Add(new PendingFill(shape, TargetVoxel, TargetCell, TargetChunk, Facing, SphereRadiusMetres, MaterialId, result.Wet));
            WetFillsDeferred++;
        }

        return true;
    }

    /// <summary>
    /// Runs every fill the click refused wet since the last boundary, in click order, with the
    /// water in hand. The caller has published the wet copy and the grid is the frame's.
    /// </summary>
    /// <returns>The results summed with the water moved, or null with nothing queued.</returns>
    public (PlaceResult Result, long Lifted, long Returned, long Displaced)? RunPendingWetFills(Aabb? body)
    {
        if (_pendingWetFills.Count == 0 || Water is null)
        {
            _pendingWetFills.Clear();
            return null;
        }

        var sum = default(PlaceResult);
        long lifted = 0, returned = 0, displaced = 0;
        foreach (PendingFill fill in _pendingWetFills)
        {
            // Asked again at the boundary for what is still to place — the wet count — because a
            // bite between the click and the boundary may have spent what the click was
            // budgeted against.
            if (!CanAfford(fill.Wet, out long need))
            {
                RefuseUnpowered(fill.Wet, need);
                continue;
            }

            var displacing = new FillWater(Water);
            PlaceResult result = fill.Shape switch
            {
                FillShape.Voxel => Place.Voxel(pipeline, materials, fill.Voxel, fill.MaterialId, body, inventory, displacing),
                FillShape.Chunk => Place.Chunk(pipeline, materials, fill.Chunk, fill.MaterialId, body, inventory, displacing),
                FillShape.Cube => Place.Cube(pipeline, materials, fill.Cell, fill.MaterialId, body, inventory, displacing),
                FillShape.Slab => Place.Slab(pipeline, materials, fill.Cell, fill.MaterialId, body, inventory, displacing),
                FillShape.Ramp => Place.Ramp(pipeline, materials, fill.Cell, fill.MaterialId, fill.Facing, body, inventory, displacing),
                _ => Place.Sphere(pipeline, materials, fill.Cell, fill.RadiusMetres, fill.MaterialId, body, inventory, displacing),
            };

            // Only what landed is recorded: the dry part was placed and tallied at the click and
            // reads Occupied now, and counting that again would inflate the session's occupied.
            VoxelsPlaced += result.Placed;
            WetVoxelsFilled += result.Placed;
            PayFor(result);
            FillLifted += displacing.Lifted;
            FillReturned += displacing.Returned;
            FillDisplaced += displacing.Displaced;
            lifted += displacing.Lifted;
            returned += displacing.Returned;
            displaced += displacing.Displaced;
            sum = new PlaceResult(
                sum.Candidates + result.Candidates, sum.Placed + result.Placed, sum.Occupied + result.Occupied,
                sum.InsidePlayer + result.InsidePlayer, sum.Unreachable + result.Unreachable,
                sum.OutOfStock + result.OutOfStock, sum.Wet + result.Wet);
        }

        _pendingWetFills.Clear();
        return (sum, lifted, returned, displaced);
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
            // Asked again at the boundary, because a bite between the click and the boundary
            // may have spent what the click was budgeted against — the fill pays exactly what
            // it places or places nothing.
            if (!CanAfford(VoxelConstants.ChunkVolume, out long need))
            {
                RefuseUnpowered(VoxelConstants.ChunkVolume, need);
                continue;
            }

            // With the water in hand since 2026-09-19: a chunk fill at the boundary is already
            // where the grid is free, so its wet voxels are filled displacing rather than left.
            FillWater? displacing = Water is null ? null : new FillWater(Water);
            PlaceResult result = Place.Chunk(pipeline, materials, chunk, material, body, inventory, displacing);
            Placements++;
            ChunkPlacements++;
            Record(result);
            PayFor(result);
            if (displacing is not null)
            {
                FillLifted += displacing.Lifted;
                FillReturned += displacing.Returned;
                FillDisplaced += displacing.Displaced;
            }
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

    /// <summary>The placeable material there is most of — an item only when nothing else is carried — or −1 for an empty inventory.</summary>
    /// <remarks>
    /// <b>Placeable first, since 2026-09-17.</b> The default selection is what the next
    /// right-click builds with, and an item builds nothing; with <c>--stock=</c> seeding every
    /// material alike the tie went to the lowest ID, which from M10b-1 was the computer — so
    /// every <c>--place</c> profile from then to M10c-3 selected the computer and placed nothing,
    /// reading <c>computer 0 open(s) / 1 miss(es)</c> where its placements should have been.
    /// </remarks>
    private int LargestHolding()
    {
        int best = -1, bestItem = -1;
        long most = 0, mostItem = 0;

        foreach ((int materialId, long voxels) in inventory.Entries())
        {
            if (!materials.IsPlaceable(materialId))
            {
                if (voxels > mostItem)
                {
                    mostItem = voxels;
                    bestItem = materialId;
                }

                continue;
            }

            if (voxels > most)
            {
                most = voxels;
                best = materialId;
            }
        }

        return best >= 0 ? best : bestItem;
    }
}
