using Overburden.Core.Machines;

namespace Overburden.Core.Content;

/// <summary>
/// What a tool draws to take a voxel (M10c-3, the ladder's decision 15): the pool it draws from,
/// named as a link's <c>via</c> is, and the joules a voxel costs.
/// </summary>
/// <param name="Via">
/// The rule the draw goes through — <c>channel</c> today, held to <see cref="MachineDefinition.ServedVia"/>
/// at load the way a link's is, so a tool written for a conductor class M10e has not built is
/// refused naming the part rather than loaded and free.
/// </param>
/// <param name="JoulesPerVoxel">
/// What one voxel <em>removed</em> costs — never one offered, never one the tool could not
/// take. Zero is a tool on the pool that draws nothing, which is legal and dull; the shipped
/// picks declare no block at all and are free by absence.
/// </param>
/// <remarks>
/// <b>On the tool and not on the tier's holder</b>, because the thing that makes the entangled
/// miner <em>earned</em> is a cost the hand pick does not pay: a block on the tiers would gate
/// Q with a steel pick at nothing. The draw itself is the caller's — the pack is Game-side
/// state — and Core's shapes take the budget it computes (<c>Carve</c>'s <c>voxelBudget</c>).
/// </remarks>
public sealed record ToolPower(string Via, long JoulesPerVoxel)
{
    /// <summary>The largest <see cref="JoulesPerVoxel"/> the format accepts — a gigajoule a voxel, a sanity bound.</summary>
    public const long MaxJoulesPerVoxel = 1_000_000_000L;

    /// <summary>Millijoules one removed voxel costs, the pool's unit.</summary>
    public long MillijoulesPerVoxel => JoulesPerVoxel * MachineDefinition.PerMille;

    /// <summary>Whether this block draws anything at all.</summary>
    public bool IsFree => JoulesPerVoxel == 0;

    public override string ToString() => $"{JoulesPerVoxel:N0} J a voxel via {Via}";
}

/// <summary>
/// One mining tool tier: how big a bite it takes, and what it is hard enough to take it out of.
/// </summary>
/// <remarks>
/// <para>
/// <b>A table in C# would have been three lines shorter and wrong.</b> Non-negotiable 5 says
/// the base game is a mod loaded through the identical path a third-party mod uses, and a
/// tool table hardcoded in the engine is precisely the thing that makes a content format
/// wrong — it is only discovered at M12, when the first mod wants a drill and finds there is
/// nowhere to put one. So even the single tier M3 needs arrives from
/// <c>mods/overburden/tools/*.json</c>.
/// </para>
/// <para>
/// The two fields that matter are deliberately the only two that matter. Radius decides how
/// much comes away; the threshold decides <i>what</i> comes away, and that is the whole of
/// the mechanic — an early pick swung at a dirt bank with a stone seam through it takes the
/// dirt and leaves the stone standing proud. Nothing has to say "you cannot mine this"; the
/// shape of the hole says it.
/// </para>
/// </remarks>
public sealed class ToolDefinition
{
    /// <summary>The largest <see cref="RadiusMetres"/> the content format accepts.</summary>
    /// <remarks>
    /// <para>
    /// A sanity bound, not a design limit, and it lives here because it is a rule about what
    /// a tool file may say rather than about how a carve runs. The carve scans a cube around
    /// the sphere, so a mod that writes <c>"radiusMetres": 1000</c> does not carve a huge
    /// hole — it freezes the game inside a loop of half a trillion iterations, which is far
    /// harder to diagnose than a load error.
    /// </para>
    /// <para>
    /// <b>4 m is an estimate derived from cost, not a measurement.</b> Candidates go as
    /// roughly <c>(8r + 2)³</c>: 4 m is ~39,000 box iterations and ~17,000 voxels inside the
    /// sphere, spread over about 27 chunks — obviously absurd for a hand tool, and cheap
    /// enough that a bad mod file is a bad load rather than a stall. 16 m, the first number
    /// written here, was not that: a 64-voxel radius is ~2.2 million iterations and up to 729
    /// chunk clones, which converts a hang into a repeated multi-hundred-millisecond freeze
    /// rather than preventing one. M3d's held-button measurement is what should set this
    /// properly; until then it carries the estimate label.
    /// </para>
    /// </remarks>
    public const float MaxRadiusMetres = 4f;

    public required Identifier Id { get; init; }

    /// <summary>
    /// Radius of the carved sphere, in metres.
    /// </summary>
    /// <remarks>
    /// Metres and not voxels, because DESIGN specifies tools in metres and converting at
    /// every call site is how two units drift apart. At 0.25 m voxels a 0.5 m radius is a
    /// bite four voxels across.
    /// </remarks>
    public float RadiusMetres { get; init; }

    /// <summary>
    /// The hardest material this tool removes. A voxel comes away when its
    /// <see cref="MaterialDefinition.Hardness"/> is at or below this.
    /// </summary>
    public float HardnessThreshold { get; init; }

    /// <summary>
    /// Seconds between swings.
    /// </summary>
    /// <remarks>
    /// Nothing reads this until M3d binds a mouse button, and it is here now for the reason
    /// <see cref="MaterialDefinition"/>'s unused fields are: adding a field to a format mods
    /// have already written against is a migration, and this one is certain to be wanted.
    /// It is also where the answer lives if M3d's open "instant or progressive?" question
    /// resolves toward instant — a slower swing is a tuning value, not a code change.
    /// </remarks>
    public float CooldownSeconds { get; init; } = 0.25f;

    /// <summary>
    /// What a voxel costs to take with this tool, or null for a tool that is free (M10c-3).
    /// </summary>
    /// <remarks>
    /// Null and not a zero-cost block, so "declares no power" and "declares a power of nothing"
    /// stay two different facts a mod can write — the shipped picks are the first, and a tool
    /// on a pool it does not draw from would be the second.
    /// </remarks>
    public ToolPower? Power { get; init; }

    /// <summary>Whether a swing of this tool draws nothing from any pack.</summary>
    public bool IsFree => Power is null || Power.IsFree;

    /// <summary>True for a tool whose defining mod is not installed.</summary>
    public bool IsUnknown { get; init; }

    /// <summary>
    /// The stand-in for a tool whose mod has been removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inert rather than powerful, which is the opposite of the choice
    /// <see cref="MaterialDefinition.Unknown"/> makes, and for the same underlying reason:
    /// pick the failure that cannot destroy a world. A missing material must stay solid so
    /// the ground does not vanish; a missing tool must carve nothing so a save cannot be
    /// hollowed out by a tier whose numbers are no longer known.
    /// </para>
    /// <para>
    /// <b>Inert twice over, because a zero radius alone was not.</b> A threshold of 0 still
    /// takes anything of hardness 0 — which is air, and, from M8, fluids — and a radius of
    /// exactly 0 met the distance test whenever a swing landed precisely on a voxel centre,
    /// which is one of the two centring choices M3d is choosing between. Negative infinity
    /// refuses every real hardness there is, and <c>Carve.Sphere</c> returns early on a
    /// non-positive radius. Either alone would do; a stand-in for content nobody has is not
    /// where to be economical.
    /// </para>
    /// </remarks>
    public static ToolDefinition Unknown(Identifier id) => new()
    {
        Id = id,
        IsUnknown = true,
        RadiusMetres = 0f,
        HardnessThreshold = float.NegativeInfinity,
        CooldownSeconds = 1f,
    };

    public override string ToString() => IsUnknown ? $"{Id} (unknown)" : Id.ToString();
}
