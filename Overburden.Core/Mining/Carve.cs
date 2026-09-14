using System.Numerics;
using Overburden.Core.Content;
using Overburden.Core.Inventory;
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
    /// <param name="into">
    /// Credited one voxel per material actually removed, or null to mine into nothing.
    /// <b>Per material rather than per swing</b>, because a bite through a dirt bank with a stone
    /// seam in it takes two materials and <see cref="CarveResult.Removed"/> is one number —
    /// crediting from the result would have to guess which. Defaulting to null is what keeps
    /// every pre-M6c caller and test compiling unedited, the same shape as
    /// <c>ChunkPipeline</c>'s optional store.
    /// </param>
    public static CarveResult Swing(
        IVoxelEditor world,
        MaterialTable materials,
        Vector3 centreMetres,
        ToolDefinition tool,
        VolumetricInventory? into = null)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return Sphere(world, materials, centreMetres, tool.RadiusMetres, tool.HardnessThreshold, into);
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
        VolumetricInventory? into = null) =>
        Swing(world, materials, SwingCentre(hit), tool, into);

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
        VolumetricInventory? into = null)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return Voxel(world, materials, hit.Voxel, tool.HardnessThreshold, into);
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
        VolumetricInventory? into = null)
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

        return Take(world, materials, voxel, hardnessThreshold, into) switch
        {
            Bucket.Removed => new CarveResult(1, 1, 0, 0, 0),
            Bucket.TooHard => new CarveResult(1, 0, 1, 0, 0),
            Bucket.AlreadyAir => new CarveResult(1, 0, 0, 1, 0),
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
    /// M3c's whole argument bypassable by pressing Q.
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
        VolumetricInventory? into = null)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return Chunk(world, materials, hit.Voxel.Chunk, tool.HardnessThreshold, into);
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
        VolumetricInventory? into = null)
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

        // Z outer and Y inner, matching the [z][x][y] storage order the writes land in. It is
        // the same loop nest as Sphere's and for the same reason, and here it covers the array
        // exactly once in its own order rather than approximately.
        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        for (int x = 0; x < VoxelConstants.ChunkSize; x++)
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
            var voxel = new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z);
            candidates++;

            switch (Take(world, materials, voxel, hardnessThreshold, into))
            {
                case Bucket.Removed: removed++; break;
                case Bucket.TooHard: tooHard++; break;
                case Bucket.AlreadyAir: alreadyAir++; break;
                default: unreachable++; break;
            }
        }

        return new CarveResult(candidates, removed, tooHard, alreadyAir, unreachable);
    }

    /// <summary>The cube bite's edge in voxels: eight, a 2 m cube, sixty-four to a chunk.</summary>
    /// <remarks>
    /// A power of two dividing the chunk, for the reason the cave lattice step is one: the cube
    /// lattice then nests inside the chunk lattice, so a cube never straddles a chunk face and
    /// sixty-four cube bites are one chunk bite exactly. <see cref="CubeOrigin"/> relies on it.
    /// </remarks>
    public const int CubeVoxels = 8;

    /// <summary>The cube of the cube lattice that holds <paramref name="voxel"/>, by its origin.</summary>
    /// <remarks>
    /// Snapped to a lattice rather than centred on the hit voxel, and that is the whole of what
    /// makes the shape a miner's: consecutive bites tile into flat-walled rooms instead of
    /// overlapping into ragged one-voxel steps, which is what the 1 m brush lattice buys placement
    /// and what the chunk lattice buys the chunk bite. A mask floors negative coordinates too,
    /// which a division would not — the seam through the origin that
    /// <see cref="VoxelPos.Chunk"/> shifts to avoid.
    /// </remarks>
    public static VoxelPos CubeOrigin(VoxelPos voxel) => new(
        voxel.X & ~(CubeVoxels - 1),
        voxel.Y & ~(CubeVoxels - 1),
        voxel.Z & ~(CubeVoxels - 1));

    /// <summary>
    /// Takes the 2 m cube of the cube lattice that holds the hit voxel — the entangled miner's
    /// bite since 2026-09-14, between the tier's sphere and the whole chunk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Matthew's ask, verbatim: <i>"just take an 8x8 cube like right click"</i></b> — the
    /// chunk bite was the right scale for flooding a test mine and the wrong one for shaping
    /// anything, 512 m³ gone a press with the seam drawn nowhere. Eight voxels is the cube brush's
    /// idea at bite scale: a lattice you can see the edges of, small enough to steer, big enough
    /// that a room is a few presses rather than a few hundred.
    /// </para>
    /// <para>
    /// Same <see cref="Take"/>, same gate, same buckets as the other three shapes. 512 candidates
    /// exactly, whatever the aim — bounded like the chunk's and for the same reason.
    /// </para>
    /// </remarks>
    public static CarveResult Cube(
        IVoxelEditor world,
        MaterialTable materials,
        in VoxelHit hit,
        ToolDefinition tool,
        VolumetricInventory? into = null)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return Cube(world, materials, CubeOrigin(hit.Voxel), tool.HardnessThreshold, into);
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
        VolumetricInventory? into = null)
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

        // Chunk's loop nest at the cube's size: Z outer, Y inner, the storage order.
        for (int z = 0; z < CubeVoxels; z++)
        for (int x = 0; x < CubeVoxels; x++)
        for (int y = 0; y < CubeVoxels; y++)
        {
            var voxel = new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z);
            candidates++;

            switch (Take(world, materials, voxel, hardnessThreshold, into))
            {
                case Bucket.Removed: removed++; break;
                case Bucket.TooHard: tooHard++; break;
                case Bucket.AlreadyAir: alreadyAir++; break;
                default: unreachable++; break;
            }
        }

        return new CarveResult(candidates, removed, tooHard, alreadyAir, unreachable);
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
        VolumetricInventory? into = null)
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

            switch (Take(world, materials, voxel, hardnessThreshold, into))
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
        float hardnessThreshold,
        VolumetricInventory? into)
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
        if (!world.SetVoxel(voxel, MaterialIds.AirNumericId))
        {
            return Bucket.Unreachable;
        }

        // Credited here and only here, after the write succeeded. Crediting from the bucket count
        // afterwards would pay for voxels that turned out to be unreachable, and crediting before
        // the write would pay for a hole that was never made.
        into?.Add(material, 1);
        return Bucket.Removed;
    }

    // The membership predicate and the bounding box moved to VoxelSphere at M6c-1, because
    // Building.Place needs the same sphere: one carved and one placed at the same radius have to
    // be the same voxels, or filling in a hole you just made leaves a rind. The reasoning that
    // was here went with the code.
}
