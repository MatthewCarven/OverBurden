using System.Numerics;
using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Physics;
using Overburden.Core.Voxels;

namespace Overburden.Core.Building;

/// <summary>What one brush did, with every voxel it looked at accounted for.</summary>
/// <param name="Candidates">
/// Voxels inside the brush. The sum of the four counts below, always — see
/// <see cref="IsAccounted"/>.
/// </param>
/// <param name="Placed">Voxels that took the material. The number M6c-2's inventory pays for.</param>
/// <param name="Occupied">
/// Voxels that already held something. <b>Not a failure — it is the rule</b>: only air is
/// replaceable, so a brush pushed into a hillside fills the air around the rock rather than
/// eating it. See <see cref="Place"/> for why overwriting would break the hardness gate.
/// </param>
/// <param name="InsidePlayer">
/// Voxels overlapping the body that asked for the placement. Refused so that nobody can build
/// themselves into a wall; the rest of the brush still lands.
/// </param>
/// <param name="OutOfStock">
/// Voxels the inventory could not pay for. <b>A partial brush is placed rather than none</b>:
/// running out halfway through a wall leaves the part you could afford, which is both the less
/// surprising behaviour and the one that lets the count itself tell you what you were short.
/// Always zero for a placement with no inventory, which is what creative building is.
/// </param>
/// <param name="Unreachable">
/// Voxels whose chunk is not loaded, or is loaded but has not generated. <b>An unknown, not a
/// refusal</b> — the same distinction <see cref="CarveResult.Unreachable"/> draws, and the same
/// one <see cref="RayOutcome.Blocked"/> draws against <see cref="RayOutcome.Missed"/>.
/// </param>
/// <param name="Wet">
/// Air voxels holding Tier B water, refused (M9d, decision 3). A fill that overwrote one would
/// delete the water it filled — the automaton would count it <c>displaced</c> at the next visit
/// and nothing would put it back — so replaceable means air <i>with no water in it</i>, and the
/// brush fills the dry cells round the pool and leaves the pool. The loop this buys: a flooded
/// room cannot be filled until M10's pump has emptied it. Zero for every editor that does not
/// answer <see cref="IVoxelEditor.HoldsWater"/>.
/// </param>
public readonly record struct PlaceResult(
    int Candidates,
    int Placed,
    int Occupied,
    int InsidePlayer,
    int Unreachable,
    int OutOfStock = 0,
    int Wet = 0)
{
    /// <summary>The whole brush was inside loaded, generated terrain.</summary>
    public bool Complete => Unreachable == 0;

    /// <summary>Every candidate landed in exactly one bucket.</summary>
    /// <remarks>
    /// Asserted by every placement test rather than by one, for the reason
    /// <see cref="CarveResult.IsAccounted"/> records: the failure it catches is a
    /// <c>continue</c> added later that is counted nowhere, which leaves the report plausible
    /// and quietly loses voxels.
    /// </remarks>
    public bool IsAccounted =>
        Candidates == Placed + Occupied + InsidePlayer + Unreachable + OutOfStock + Wet;

    public override string ToString() =>
        $"{Candidates} candidate(s): placed {Placed}, occupied {Occupied}, " +
        $"inside player {InsidePlayer}, unreachable {Unreachable}, out of stock {OutOfStock}, wet {Wet}";
}

/// <summary>
/// Putting material into the world in brush-shaped lots, on the 1 m lattice.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every brush writes through <see cref="IVoxelEditor.SetVoxel"/>, one voxel at a time,
/// exactly as <c>Carve</c> does.</b> A 1 m cube is 64 voxels and writing them straight into a
/// chunk's storage is faster, obvious, and skips four things at once and silently: the sky
/// column update, the light rebuild queue, the eight-chunk border dirty rule, and
/// <c>HasEdits</c> — which since M6b means the placement is never saved. There is no shortcut
/// here that is not also a bug report a week later.
/// </para>
/// <para>
/// <b>Only air is replaceable, and the argument is the hardness gate rather than tidiness.</b>
/// A brush that overwrote material would be a way to delete anything without mining it: place
/// dirt into a bedrock voxel, mine the dirt with a <c>hand_pick</c>, and the bedrock is gone.
/// That makes M3c's per-voxel hardness gate — the mechanic that decides what a tier is
/// <em>for</em> — bypassable by the other verb entirely. So a non-air voxel is
/// <see cref="PlaceResult.Occupied"/> and is not written.
/// </para>
/// <para>
/// <b>Placing air is refused outright</b>, for the same reason and more directly: it is a carve
/// with no tool and no gate. <c>Carve</c> is how material leaves the world.
/// </para>
/// <para>
/// <b>The M8 revisit trigger fired at M9d, and the answer was <i>refuse</i>, not
/// <i>displace</i>.</b> "Replaceable" meant air alone until then because nothing else could be in a
/// voxel; Tier B water is in the same 0.25 m cell as the voxel now, and a brush pushed into a
/// flooded room has nowhere to displace to — every neighbour is full — so displacing would be
/// deleting with a nicer name. Replaceable means air with no water in it (M9-PLAN decision 9,
/// planned at M9d decision 3), through <see cref="IVoxelEditor.HoldsWater"/>; a wet voxel is a
/// <see cref="PlaceResult.Wet"/> refusal and the rest of the brush lands. Water is not a
/// material, so <see cref="MaterialTable.IsFluid"/> still has no reader here.
/// </para>
/// <para>
/// Brushes are methods rather than an <c>IShape</c> the caller supplies, following
/// <c>Carve</c>'s recorded argument: there is no second implementer, and a ramp wants an
/// orientation where a sphere wants a radius, so the interface they would share is not yet
/// knowable.
/// </para>
/// </remarks>
public static class Place
{
    /// <summary>
    /// Which cell a brush lands in, given what the player is pointing at: <b>the cell holding
    /// the last empty voxel before the surface</b>.
    /// </summary>
    /// <remarks>
    /// <see cref="VoxelHit.Previous"/> is the voxel M3b built for exactly this and recorded as
    /// <i>"where M6's placement goes"</i>. Taking its cell rather than the voxel is the lattice
    /// decision: pointing anywhere within a cell's square metre builds the same brick, which is
    /// what lets two placements tile instead of overlapping by a voxel or missing by one.
    /// </remarks>
    public static BrushCell TargetCell(in VoxelHit hit) => BrushCell.FromVoxel(hit.Previous);

    /// <summary>
    /// Which way a ramp rises, given where the player is looking: <b>away from them</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Away rather than toward, so that walking forward onto a ramp climbs it.</b> Both
    /// readings of "facing" are natural and the wrong one builds every staircase backwards, which
    /// is why <see cref="BrushFacing"/> says in its own remarks which end it names.
    /// </para>
    /// <para>
    /// Snapped to the dominant horizontal axis, ignoring Y: a ramp rising along Y is a wall, and
    /// what someone looking down at a step wants is the step going away from them rather than
    /// under them.
    /// </para>
    /// <para>
    /// <b>In Core beside <see cref="TargetCell"/>, on M3d's grounds.</b> Where a brush lands is a
    /// game rule a multiplayer server would need to agree about, and which way it faces is the
    /// same category — a staircase that points one way on the client and the other on the server
    /// is the same bug as one that lands a metre off.
    /// </para>
    /// </remarks>
    public static BrushFacing FacingFrom(Vector3 direction) =>
        MathF.Abs(direction.X) >= MathF.Abs(direction.Z)
            ? direction.X >= 0.0f ? BrushFacing.PositiveX : BrushFacing.NegativeX
            : direction.Z >= 0.0f ? BrushFacing.PositiveZ : BrushFacing.NegativeZ;

    /// <summary>
    /// Fills <b>exactly the one voxel</b> a placement would land in, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The precision counterpart to the brushes, and the mirror of <c>Carve.Voxel</c> — the same
    /// pairing mining has had since M3c, arriving for building a milestone later because M6c's
    /// play found the brush set the wrong granularity for a staircase. A cube riser is a metre and
    /// M4b's step height is half of one, so cube stairs cannot be walked up by arithmetic; the
    /// ramp brush buys exactly one metre of rise and stacking it means aiming at the top face of
    /// the last one, a metre at a time.
    /// </para>
    /// <para>
    /// <b>Deliberately not the default</b>, and M3c's argument transfers without changing a word.
    /// A voxel is 1/64th of a cube, so a wall built this way is sixty-four times the clicks, and
    /// DESIGN's <i>"fine detail comes from carving, not from placing"</i> would become false in
    /// the other direction if precision became the ordinary way to build. Precision is the
    /// exception, and the exception is worth a held key.
    /// </para>
    /// <para>
    /// <b>Same gate, same buckets.</b> It shares <see cref="Put"/> with every brush rather than
    /// restating the rules, so only-air-is-replaceable, the body refusal and the inventory debit
    /// are one definition — the argument M3c measured for <c>Carve.Take</c>, where removing the
    /// hardness check failed nine tests across both shapes because there was only one copy of it.
    /// </para>
    /// </remarks>
    public static PlaceResult Voxel(
        IVoxelEditor world,
        MaterialTable materials,
        VoxelPos voxel,
        int materialId,
        Aabb? body = null,
        VolumetricInventory? from = null)
    {
        (VoxelPos Min, VoxelPos Max)? bodyVoxels = Validate(world, materials, materialId, body);

        var tally = default(Tally);
        tally.Candidate();
        tally.Add(Put(world, voxel, materialId, bodyVoxels, from));

        return tally.ToResult();
    }

    /// <summary>Which voxel a precise placement fills: <b>the empty one before the surface</b>.</summary>
    /// <remarks>
    /// <see cref="VoxelHit.Previous"/>, for the reason <c>Carve.Chip</c> takes
    /// <see cref="VoxelHit.Voxel"/>: the outline the player is looking at then <em>is</em> the
    /// thing about to change, rather than the centre of something that extends past it. That is
    /// the whole of why precision reads as precise.
    /// </remarks>
    public static VoxelPos TargetVoxel(in VoxelHit hit) => hit.Previous;

    /// <summary>
    /// Fills <b>a whole chunk</b> — 32×32×32 voxels, an 8 m cube, 512 m³.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The mirror of <c>Carve.Chunk</c>, and the reason it exists is that the bite needed an
    /// undo.</b> A verb that removes 512 m³ a click and has no counterpart at the same scale
    /// leaves the only way back a brush at 1 m³ — five hundred and twelve clicks to put back what
    /// one took. The pairing is <c>Sphere</c>/<c>Sphere</c> and <c>Voxel</c>/<c>Voxel</c> arriving
    /// at the third shape.
    /// </para>
    /// <para>
    /// <b>It takes the chunk holding <see cref="VoxelHit.Previous"/>, where the bite takes the one
    /// holding <see cref="VoxelHit.Voxel"/>, and the asymmetry is the whole of getting this
    /// right.</b> It is the same split that separates <see cref="TargetVoxel"/> from
    /// <c>Carve.Chip</c>, one scale up: mining names the thing you are pointing <em>at</em>,
    /// building names the space <em>in front of</em> it. Taking <c>hit.Voxel.Chunk</c> here would
    /// aim the fill at whatever is behind the surface — so pointing at the far wall of a hole you
    /// just bit out would fill the wall's chunk, which is solid rock, and report 32,768
    /// <see cref="PlaceResult.Occupied"/> having written nothing. <b>The verb would appear broken
    /// in exactly the situation it was built for.</b> With <c>Previous</c>, that same aim fills
    /// the hole.
    /// </para>
    /// <para>
    /// <b>Only air is replaceable, unchanged — and here that stops being incidental and starts
    /// being what makes the verb safe.</b> At brush scale the rule keeps a cube from eating a
    /// hillside; at chunk scale it is the only thing standing between this and a key that
    /// overwrites 512 m³ of terrain, hardness gate and all. A fill aimed into solid rock is 32,768
    /// refusals and no writes. <b>It is not a chunk-shaped overwrite and must never become one</b>
    /// — that would make M3c's gate bypassable by the building verb at the worst possible scale,
    /// which is the exact argument <see cref="Place"/> records against overwriting in general.
    /// </para>
    /// <para>
    /// <b>The body refusal earns its keep here rather than merely existing.</b> A 1 m brush can be
    /// aimed away from yourself; an 8 m cube centred on the chunk you are standing in cannot, so
    /// without it the ordinary use of this verb — refilling the hole you are stood in — would wall
    /// the player in. It is the same <see cref="PlaceResult.InsidePlayer"/> hole the brushes leave,
    /// and at this scale it is the difference between a fill and a burial.
    /// </para>
    /// <para>
    /// <b><see cref="PlaceResult.OutOfStock"/> stops being an edge case.</b> One fill is 32,768
    /// voxels — <b>512,000 litres</b> of a single material — so running out part-way through is
    /// the ordinary outcome rather than the unusual one, and the partial-fill rule the brushes
    /// established is what makes that bearable: you get what you could afford and the count says
    /// what you were short. Filling a chunk you did not first take a chunk out of will almost
    /// always run dry.
    /// </para>
    /// <para>
    /// <b>Cost is bounded at 32,768 candidates whatever the aim</b>, the same as the bite, and for
    /// the same reason: the shape is the storage grid, so there is no radius to get wrong and no
    /// bounding box to derive. What varies is how many of them are air, because
    /// <see cref="Bucket.Occupied"/> costs a read and no write — so a fill into a chunk that was
    /// bitten out is near its worst case and a fill into rock is near its cheapest, which is the
    /// exact inverse of how the bite's cost varies.
    /// </para>
    /// </remarks>
    public static PlaceResult Chunk(
        IVoxelEditor world,
        MaterialTable materials,
        in VoxelHit hit,
        int materialId,
        Aabb? body = null,
        VolumetricInventory? from = null) =>
        Chunk(world, materials, TargetChunk(hit), materialId, body, from);

    /// <summary>Fills a chunk named directly. The aim-free overload, for tests.</summary>
    public static PlaceResult Chunk(
        IVoxelEditor world,
        MaterialTable materials,
        ChunkPos chunk,
        int materialId,
        Aabb? body = null,
        VolumetricInventory? from = null)
    {
        (VoxelPos Min, VoxelPos Max)? bodyVoxels = Validate(world, materials, materialId, body);

        VoxelPos origin = chunk.OriginVoxel;
        var tally = default(Tally);

        // Z outer and Y inner, matching the [z][x][y] storage order the writes land in — the same
        // nest Carve.Chunk uses, and here too it covers the array exactly once in its own order
        // rather than approximately.
        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        for (int x = 0; x < VoxelConstants.ChunkSize; x++)
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
            tally.Candidate();
            tally.Add(Put(
                world,
                new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z),
                materialId,
                bodyVoxels,
                from));
        }

        return tally.ToResult();
    }

    /// <summary>
    /// Which chunk a fill lands in: <b>the one holding the last empty voxel before the surface</b>.
    /// </summary>
    /// <remarks>
    /// <see cref="VoxelHit.Previous"/>, exactly as <see cref="TargetCell"/> and
    /// <see cref="TargetVoxel"/> use it, so that all three placement scales name the space in front
    /// of what you are pointing at rather than the thing itself. See <see cref="Chunk"/> for why
    /// the mining half makes the opposite choice and why copying it here would break the verb.
    /// </remarks>
    public static ChunkPos TargetChunk(in VoxelHit hit) => hit.Previous.Chunk;

    /// <summary>Fills a whole cell — 4×4×4 voxels, one cubic metre.</summary>
    public static PlaceResult Cube(
        IVoxelEditor world,
        MaterialTable materials,
        BrushCell cell,
        int materialId,
        Aabb? body = null,
        VolumetricInventory? from = null)
    {
        (VoxelPos Min, VoxelPos Max)? bodyVoxels = Validate(world, materials, materialId, body);
        var tally = default(Tally);

        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            tally.Candidate();
            tally.Add(Put(world, cell.VoxelAt(lx, ly, lz), materialId, bodyVoxels, from));
        }

        return tally.ToResult();
    }

    /// <summary>
    /// Fills the bottom half of a cell — 4×2×4 voxels, half a metre thick.
    /// </summary>
    /// <remarks>
    /// <b>The bottom half, not the middle.</b> A slab is a floor, and a floor placed in the cell
    /// you are pointing at should be walkable from the cell beside it. Half of 4 is exactly 2,
    /// which is the second reason the lattice is a metre: a slab of a cell that did not divide
    /// evenly would be a different thickness depending on which way it rounded.
    /// </remarks>
    public static PlaceResult Slab(
        IVoxelEditor world,
        MaterialTable materials,
        BrushCell cell,
        int materialId,
        Aabb? body = null,
        VolumetricInventory? from = null)
    {
        (VoxelPos Min, VoxelPos Max)? bodyVoxels = Validate(world, materials, materialId, body);
        var tally = default(Tally);

        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels / 2; ly++)
        {
            tally.Candidate();
            tally.Add(Put(world, cell.VoxelAt(lx, ly, lz), materialId, bodyVoxels, from));
        }

        return tally.ToResult();
    }

    /// <summary>
    /// Fills a cell as a four-step staircase rising toward <paramref name="facing"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A staircase, not a slope, and that is a non-negotiable rather than a simplification.</b>
    /// M4d's deliberately-not list rules out non-cube collision shapes as <i>"a voxel is one
    /// material or another" violations wearing different hats</i>. So a ramp is an arrangement of
    /// 0.25 m cubes inside a 1 m cell — M3c's <i>"sub-voxel means the voxel is small, not that it
    /// is partly full"</i>, restated for the placement half.
    /// </para>
    /// <para>
    /// <b>It lands well, and the number is why:</b> each step rises 0.25 m, comfortably inside
    /// M4b's 0.5 m step height, so a placed ramp is walkable by physics that already exists —
    /// no new collision shape, no new anything. <c>APlacedRampIsWalkedUpByThePhysicsThatAlready
    /// Exists</c> checks that rather than leaving it as arithmetic.
    /// </para>
    /// <para>
    /// 40 voxels of 64: rows of 1, 2, 3 and 4 across the full width.
    /// </para>
    /// </remarks>
    public static PlaceResult Ramp(
        IVoxelEditor world,
        MaterialTable materials,
        BrushCell cell,
        int materialId,
        BrushFacing facing,
        Aabb? body = null,
        VolumetricInventory? from = null)
    {
        (VoxelPos Min, VoxelPos Max)? bodyVoxels = Validate(world, materials, materialId, body);
        var tally = default(Tally);

        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        {
            int height = StepHeight(facing, lx, lz);

            for (int ly = 0; ly < height; ly++)
            {
                tally.Candidate();
                tally.Add(Put(world, cell.VoxelAt(lx, ly, lz), materialId, bodyVoxels, from));
            }
        }

        return tally.ToResult();
    }

    /// <summary>
    /// Fills a sphere of <paramref name="radiusMetres"/> centred on the cell's centre.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Centred on the cell rather than on the hit voxel</b>, so that it is anchored the same
    /// way every other brush is even though a sphere does not tile. Two spheres placed in
    /// adjacent cells are exactly a metre apart, which is the property that makes a row of them
    /// a deliberate shape rather than a smear.
    /// </para>
    /// <para>
    /// <b>The same sphere <c>Carve</c> removes</b> — both go through
    /// <see cref="VoxelSphere"/>. A placed sphere that did not exactly refill a carved one of
    /// the same radius would leave a rind of stragglers, and nothing would notice until somebody
    /// tried to fill in a hole.
    /// </para>
    /// </remarks>
    /// <param name="radiusMetres">
    /// Metres, never voxels, for the reason <c>Carve.Sphere</c> gives. Zero or less places
    /// nothing; NaN or past <see cref="ToolDefinition.MaxRadiusMetres"/> throws, because the scan
    /// is cubic and a huge radius freezes the game inside a loop rather than building a dome.
    /// <b>Borrowing the tool's bound rather than declaring a second one</b>: it is the same
    /// failure with the same shape and the same order of magnitude, and two constants for one
    /// limit is two things to drift. If a brush ever wants a genuinely different reach, that is
    /// the moment to split them, not before.
    /// </param>
    public static PlaceResult Sphere(
        IVoxelEditor world,
        MaterialTable materials,
        BrushCell cell,
        float radiusMetres,
        int materialId,
        Aabb? body = null,
        VolumetricInventory? from = null) =>
        Sphere(world, materials, cell.CentreMetres, radiusMetres, materialId, body, from);

    /// <summary>Fills a sphere at an arbitrary centre. The lattice-free overload, for tests.</summary>
    public static PlaceResult Sphere(
        IVoxelEditor world,
        MaterialTable materials,
        Vector3 centreMetres,
        float radiusMetres,
        int materialId,
        Aabb? body = null,
        VolumetricInventory? from = null)
    {
        (VoxelPos Min, VoxelPos Max)? bodyVoxels = Validate(world, materials, materialId, body);

        if (float.IsNaN(radiusMetres)
            || radiusMetres < 0.0f
            || radiusMetres > ToolDefinition.MaxRadiusMetres)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radiusMetres),
                radiusMetres,
                $"A brush radius must be a number within 0..{ToolDefinition.MaxRadiusMetres} m.");
        }

        if (!float.IsFinite(centreMetres.X)
            || !float.IsFinite(centreMetres.Y)
            || !float.IsFinite(centreMetres.Z))
        {
            throw new ArgumentException("A brush needs a finite centre.", nameof(centreMetres));
        }

        if (radiusMetres == 0.0f)
        {
            // A clean no-op rather than the knife edge the geometry gives on its own, for the
            // reason Carve.Sphere records: at exactly zero the membership test reads
            // "distance 0 ≤ 0", so a brush aimed at precisely a voxel centre would place one
            // voxel while one aimed a nanometre away placed none.
            return default;
        }

        (VoxelPos min, VoxelPos max) = VoxelSphere.Bounds(centreMetres, radiusMetres);
        float radiusSquared = radiusMetres * radiusMetres;
        var tally = default(Tally);

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

            // Counted here and nowhere else, so every path below lands in exactly one bucket.
            // Counting over the box instead would make Unreachable non-zero for most brushes
            // anywhere near the loaded edge, since the corners reach r(√3 − 1) further out than
            // the sphere ever does.
            tally.Candidate();
            tally.Add(Put(world, voxel, materialId, bodyVoxels, from));
        }

        return tally.ToResult();
    }

    /// <summary>How many voxels tall the staircase is at one horizontal position.</summary>
    private static int StepHeight(BrushFacing facing, int localX, int localZ) => facing switch
    {
        BrushFacing.PositiveX => localX + 1,
        BrushFacing.NegativeX => BrushCell.Voxels - localX,
        BrushFacing.PositiveZ => localZ + 1,
        BrushFacing.NegativeZ => BrushCell.Voxels - localZ,
        _ => throw new ArgumentOutOfRangeException(nameof(facing), facing, "Not a ramp facing."),
    };

    /// <summary>Which bucket one voxel landed in.</summary>
    private enum Bucket
    {
        Placed,
        Occupied,
        InsidePlayer,
        Unreachable,
        OutOfStock,
        Wet,
    }

    /// <summary>
    /// Tries to put one voxel, and says which of the four things happened.
    /// </summary>
    /// <remarks>
    /// <b>One definition of the gate, shared by every brush</b> — the counterpart to
    /// <c>Carve.Take</c>, and load-bearing for the reason M3c measured there: removing the
    /// hardness check from <c>Take</c> failed nine tests across both mining shapes, which a
    /// forked copy in each shape would not have done.
    /// </remarks>
    private static Bucket Put(
        IVoxelEditor world,
        VoxelPos voxel,
        int materialId,
        in (VoxelPos Min, VoxelPos Max)? bodyVoxels,
        VolumetricInventory? from)
    {
        // The body first, before the read. It is the cheaper test, and it is true regardless of
        // what is in the voxel — "we will not build that inside you" does not depend on whether
        // the chunk happens to be loaded.
        if (bodyVoxels is (VoxelPos min, VoxelPos max)
            && voxel.X >= min.X && voxel.X <= max.X
            && voxel.Y >= min.Y && voxel.Y <= max.Y
            && voxel.Z >= min.Z && voxel.Z <= max.Z)
        {
            return Bucket.InsidePlayer;
        }

        if (!world.TryGetMaterial(voxel, out int existing))
        {
            return Bucket.Unreachable;
        }

        if (existing != MaterialIds.AirNumericId)
        {
            // Including the same material: a rewrite is refused rather than waved through, so
            // that placing over what is already there costs nothing and reports honestly.
            // A consequence worth naming: this means placement never reaches SetVoxel's
            // "material is already there" early-out, which its comment predicted it would. The
            // early-out now has no caller that fires it at all — M4a's zeroable-contact-epsilon
            // category — and it is kept for the same reason: it protects the next writer.
            return Bucket.Occupied;
        }

        // Air with water in it is not replaceable (M9d, decision 3). After Occupied because the
        // read is the cheaper test and a wet rock is a contradiction the automaton never writes;
        // before the stock for the reason Occupied is — nothing is charged for a voxel this brush
        // would not fill. One gate here, so the brush, the precision voxel, the entangled fill and
        // the chunk fill refuse exactly the same cells.
        if (world.HoldsWater(voxel))
        {
            return Bucket.Wet;
        }

        // Last of the refusals, and last on purpose: the stock is only consulted once the voxel
        // is known to be one this brush would actually fill. Checking it earlier would charge for
        // voxels that turn out to be Occupied or inside the player, so a wall built against a
        // cliff would cost the volume of the cliff.
        if (from is not null && !from.Has(materialId))
        {
            return Bucket.OutOfStock;
        }

        // Should not happen: a read that succeeded means the chunk is loaded and generated, which
        // is the same condition the write tests. Reported rather than ignored so that an editor
        // whose two halves ever disagree surfaces the voxel instead of losing it.
        if (!world.SetVoxel(voxel, materialId))
        {
            return Bucket.Unreachable;
        }

        // Debited after the write rather than before it, so that a write which fails costs
        // nothing. Checked-then-debited rather than taken-then-refunded because the sim thread is
        // the only writer here, so nothing can come between the two.
        from?.Take(materialId, 1);
        return Bucket.Placed;
    }

    /// <summary>
    /// Checks what every brush needs checked, and resolves the body to a voxel range once.
    /// </summary>
    /// <remarks>
    /// The range is computed here rather than per voxel because
    /// <see cref="Aabb.VoxelBounds"/> is six floating-point conversions and a brush asks about it
    /// sixty-four times.
    /// </remarks>
    private static (VoxelPos Min, VoxelPos Max)? Validate(
        IVoxelEditor world,
        MaterialTable materials,
        int materialId,
        Aabb? body)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        if (materialId == MaterialIds.AirNumericId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(materialId),
                materialId,
                "Placing air would remove material with no tool and no hardness gate. "
                + "Carve is how material leaves the world.");
        }

        if ((uint)materialId >= (uint)materials.Count)
        {
            // Caught here rather than at the write, because a voxel holding an ID no material
            // table has is a crash in the mesher a frame later, a long way from the cause.
            throw new ArgumentOutOfRangeException(
                nameof(materialId),
                materialId,
                $"No material has numeric ID {materialId}; the table holds {materials.Count}.");
        }

        return body?.VoxelBounds;
    }

    /// <summary>The five counters, so that one loop can serve every brush shape.</summary>
    /// <remarks>
    /// <b><see cref="Candidate"/> is separate from <see cref="Add"/> on purpose, and deriving the
    /// total from the buckets instead would have quietly destroyed the invariant it exists for.</b>
    /// <see cref="PlaceResult.IsAccounted"/> is only worth asserting while it can fail: a shape
    /// loop that counts a voxel and then <c>continue</c>s past the gate is exactly the mistake
    /// <see cref="CarveResult.IsAccounted"/> was written to catch, and a tally that computed
    /// <c>Candidates</c> as the sum of its own buckets would report it as fine.
    /// </remarks>
    private struct Tally
    {
        private int _candidates;
        private int _placed;
        private int _occupied;
        private int _insidePlayer;
        private int _unreachable;
        private int _outOfStock;
        private int _wet;

        /// <summary>One voxel is inside the brush. Called by the shape, before the gate.</summary>
        public void Candidate() => _candidates++;

        /// <summary>What the gate decided about it.</summary>
        public void Add(Bucket bucket)
        {
            switch (bucket)
            {
                case Bucket.Placed: _placed++; break;
                case Bucket.Occupied: _occupied++; break;
                case Bucket.InsidePlayer: _insidePlayer++; break;
                case Bucket.OutOfStock: _outOfStock++; break;
                case Bucket.Wet: _wet++; break;
                default: _unreachable++; break;
            }
        }

        public readonly PlaceResult ToResult() =>
            new(_candidates, _placed, _occupied, _insidePlayer, _unreachable, _outOfStock, _wet);
    }
}
