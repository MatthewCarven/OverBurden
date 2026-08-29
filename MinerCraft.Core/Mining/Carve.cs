using System.Numerics;
using MinerCraft.Core.Content;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Mining;

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
public readonly record struct CarveResult(
    int Candidates,
    int Removed,
    int TooHard,
    int AlreadyAir,
    int Unreachable)
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
    public bool IsAccounted => Candidates == Removed + TooHard + AlreadyAir + Unreachable;

    public override string ToString() =>
        $"{Candidates} candidate(s): removed {Removed}, too hard {TooHard}, " +
        $"already air {AlreadyAir}, unreachable {Unreachable}";
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
    public static CarveResult Swing(
        IVoxelEditor world,
        MaterialTable materials,
        Vector3 centreMetres,
        ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return Sphere(world, materials, centreMetres, tool.RadiusMetres, tool.HardnessThreshold);
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
        ToolDefinition tool) =>
        Swing(world, materials, SwingCentre(hit), tool);

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
        ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return Voxel(world, materials, hit.Voxel, tool.HardnessThreshold);
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
        float hardnessThreshold)
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

        return Take(world, materials, voxel, hardnessThreshold) switch
        {
            Bucket.Removed => new CarveResult(1, 1, 0, 0, 0),
            Bucket.TooHard => new CarveResult(1, 0, 1, 0, 0),
            Bucket.AlreadyAir => new CarveResult(1, 0, 0, 1, 0),
            _ => new CarveResult(1, 0, 0, 0, 1),
        };
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
        float hardnessThreshold)
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

        (VoxelPos min, VoxelPos max) = Bounds(centreMetres, radiusMetres);
        float radiusSquared = radiusMetres * radiusMetres;

        int candidates = 0;
        int removed = 0;
        int tooHard = 0;
        int alreadyAir = 0;
        int unreachable = 0;

        // Z outer and Y inner, matching the [z][x][y] storage order the writes land in.
        for (int z = min.Z; z <= max.Z; z++)
        for (int x = min.X; x <= max.X; x++)
        for (int y = min.Y; y <= max.Y; y++)
        {
            var voxel = new VoxelPos(x, y, z);
            if (!Contains(voxel, centreMetres, radiusSquared))
            {
                continue;
            }

            // Counted here and nowhere else, so that every path below has to land in exactly
            // one bucket. Counting over the box instead would be worse than untidy: the box
            // corners reach r(√3 − 1) further out than the sphere ever does, so Unreachable
            // would be non-zero for most swings anywhere near the loaded edge and Complete
            // would stop meaning anything.
            candidates++;

            switch (Take(world, materials, voxel, hardnessThreshold))
            {
                case Bucket.Removed: removed++; break;
                case Bucket.TooHard: tooHard++; break;
                case Bucket.AlreadyAir: alreadyAir++; break;
                default: unreachable++; break;
            }
        }

        return new CarveResult(candidates, removed, tooHard, alreadyAir, unreachable);
    }

    /// <summary>Which bucket one voxel landed in.</summary>
    private enum Bucket
    {
        Removed,
        TooHard,
        AlreadyAir,
        Unreachable,
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
    /// </remarks>
    private static Bucket Take(
        IVoxelEditor world,
        MaterialTable materials,
        VoxelPos voxel,
        float hardnessThreshold)
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

        if (materials.Hardness(material) > hardnessThreshold)
        {
            return Bucket.TooHard;
        }

        // Should not happen: a read that succeeded means the chunk is loaded and generated,
        // which is the same condition the write tests. Reported rather than ignored so that an
        // editor whose two halves ever disagree surfaces the voxel instead of losing it.
        return world.SetVoxel(voxel, MaterialIds.AirNumericId)
            ? Bucket.Removed
            : Bucket.Unreachable;
    }

    /// <summary>Whether <paramref name="voxel"/>'s centre is inside the sphere. The definition.</summary>
    private static bool Contains(VoxelPos voxel, Vector3 centreMetres, float radiusSquared)
    {
        (float x, float y, float z) = voxel.CentreMetres;

        float dx = x - centreMetres.X;
        float dy = y - centreMetres.Y;
        float dz = z - centreMetres.Z;

        return ((dx * dx) + (dy * dy) + (dz * dz)) <= radiusSquared;
    }

    /// <summary>
    /// The smallest voxel box that certainly contains every voxel <see cref="Contains"/>
    /// would keep. An optimisation, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sphere's axis-aligned bounding box in metres is <c>centre ± radius</c>, and a
    /// candidate's centre lies inside it, so flooring both ends into voxel coordinates is
    /// sufficient: <see cref="VoxelPos.FromMetres"/> floors and is monotone, and a voxel
    /// centre at <c>(i + 0.5) × VoxelSize</c> can never sit exactly on a voxel boundary, so
    /// the rounding would have to be wrong by half a voxel to drop a candidate.
    /// </para>
    /// <para>
    /// <b>Reusing <see cref="VoxelPos.FromMetres"/> rather than deriving the tight bound is
    /// the point.</b> A hand-rolled <c>ceil((c − r)/s − 0.5)</c> is a second flooring
    /// primitive that has to be got right for negative coordinates all over again, and — this
    /// was measured — the obvious mutation of it, swapping <c>Math.Ceiling</c>/<c>Math.Floor</c>
    /// for casts, can only ever <em>widen</em> the box, so it is a break-recipe that cannot
    /// fail a test. This version is falsifiable: narrowing either end by one voxel breaks the
    /// membership fuzz within milliseconds, at negative coordinates first.
    /// </para>
    /// </remarks>
    private static (VoxelPos Min, VoxelPos Max) Bounds(Vector3 centre, float radius) => (
        VoxelPos.FromMetres(centre.X - radius, centre.Y - radius, centre.Z - radius),
        VoxelPos.FromMetres(centre.X + radius, centre.Y + radius, centre.Z + radius));
}
