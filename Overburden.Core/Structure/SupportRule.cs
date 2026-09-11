using Overburden.Core.Content;

namespace Overburden.Core.Structure;

/// <summary>
/// The budget rule — M9-PLAN decision 1, with the constant the M9a pass moved. A solid voxel
/// carries a budget in <c>[−K, K]</c>; sideways spends the material's share, resting on rock
/// refunds a world constant, hanging costs double; a voxel is at risk when its best budget is
/// negative.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>K = 120</c>, not the plan's 240, because the field is one byte and the deficit's range is
/// the budget's own.</b> The plug a 16 m room sheds is two layers thick because its underside is
/// at <c>−8</c> (now <c>−4</c>), the layer resting on it one refund better and still negative, and
/// the layer above that exactly zero; a deficit stored coarser than the budget would move that
/// sign at a chunk face. So both halves get the byte's full range: 241 values with an offset of
/// <see cref="MaxBudget"/>, and every cost halves exactly — the geometry is decision 1's to the
/// voxel. See M9-PLAN's "M9a — the planning pass", item 1.
/// </para>
/// <para>
/// <b>A path dies when its budget reaches <c>−K</c>, and a voxel no live path reaches is
/// <see cref="ChunkSupport.Unreached"/></b> — the plan's <c>−∞</c>, and every deficit past
/// <c>K</c> with it. That is what bounds the field to a byte while keeping the fixpoint unique:
/// the max over live paths from the anchors, with every cycle strictly negative. The two
/// alternatives the build tried first — the floor as a source everywhere, then only where an
/// anchor reached — each held something it should not have or had a second fixpoint; see
/// <see cref="ChunkSupport.Unreached"/>. Decision 8's <c>deficit / K</c> saturates at the same
/// place.
/// </para>
/// <para>
/// <b>Why hanging costs double and the refund is a world constant: the uniqueness proof, not
/// physics.</b> A closed walk has as many ups as downs; each up refunds <see cref="Refund"/>,
/// each down costs at least twice it, each sideways step costs at least one — so every cycle is
/// strictly negative and the fixpoint is unique. With a per-material refund a dirt voxel over a
/// stone one refunds more going up than it spends coming down, the pair pumps itself full, and a
/// floating slab of dirt on stone never falls. The same pair across a chunk face is what decision
/// 4's chunk-wise iteration would never converge on.
/// </para>
/// <para>
/// A value rather than constants, because <c>--restrefund=</c> and <c>--supportscale=</c> are
/// the named sweeps and a constant cannot be swept. <see cref="Default"/> is what ships.
/// </para>
/// </remarks>
public sealed record SupportRule
{
    /// <summary>The budget an anchor carries, and the deepest deficit the field records.</summary>
    /// <remarks>
    /// Divisible by every shipped support distance (30, 8, 2) so their costs are exact, and the
    /// byte holds <c>2K + 1</c> values with fifteen codes to spare. Rounding is within 10% for a
    /// distance up to 32 and within 20% to <see cref="MaterialDefinition.MaxSupportDistance"/>.
    /// </remarks>
    public const int MaxBudget = 120;

    /// <summary>Where a path dies: a live budget is strictly above this.</summary>
    public const int Floor = -MaxBudget;

    /// <summary>The rule as shipped: a refund per 30 voxels of rest, distances as written.</summary>
    public static readonly SupportRule Default = new();

    private readonly int _restRefundVoxels = 30;
    private readonly float _distanceScale = 1f;

    /// <summary>
    /// Voxels of rock above a roof's underside that buy one full budget of reach — the arch. 30
    /// ships, an estimate: stone's own step, so stone arches at 45° and the shipped soil at ~75°.
    /// </summary>
    public int RestRefundVoxels
    {
        get => _restRefundVoxels;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxBudget);
            _restRefundVoxels = value;
        }
    }

    /// <summary>Multiplies every support distance — <c>--supportscale=</c>. 1 is the content as written.</summary>
    public float DistanceScale
    {
        get => _distanceScale;
        init
        {
            if (float.IsNaN(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The distance scale must be positive.");
            }

            _distanceScale = value;
        }
    }

    /// <summary>What one voxel of rest refunds: <c>K / RestRefundVoxels</c>, at least 1.</summary>
    public int Refund => Math.Max(1, MaxBudget / RestRefundVoxels);

    /// <summary>
    /// What a sideways step into a material with this support distance costs:
    /// <c>round(K / distance)</c>, at least 1, and <c>2K</c> — one step from an anchor is the
    /// floor — for a material that carries nothing sideways.
    /// </summary>
    /// <remarks>
    /// The one place the distance is rounded. A fractional distance off the saturation curve is a
    /// fractional step cost rounded here, which is what makes the curve continuous in effect
    /// rather than only in the JSON.
    /// </remarks>
    public int Cost(float distance)
    {
        float scaled = distance * DistanceScale;
        if (float.IsNaN(scaled) || scaled <= 0f)
        {
            return 2 * MaxBudget;
        }

        return Math.Clamp((int)MathF.Round(MaxBudget / scaled), 1, 2 * MaxBudget);
    }

    /// <summary>What hanging from the voxel above costs a material whose sideways step is <paramref name="cost"/>.</summary>
    /// <remarks>Twice the larger of the refund and the step: the uniqueness proof's term.</remarks>
    public int HangCost(int cost) => 2 * Math.Max(Refund, cost);

    /// <summary>A step up onto a voxel worth <paramref name="below"/>: one refund, capped.</summary>
    public int Rest(int below) => Math.Min(MaxBudget, below + Refund);

    /// <summary>Whether a budget is live — a path carrying it reaches the next voxel at all.</summary>
    public static bool IsLive(int budget) => budget > Floor;

    /// <summary>A step down from a voxel worth <paramref name="above"/> into a material with this step cost.</summary>
    public int Hang(int above, int cost) => above - HangCost(cost);

    /// <summary>A sideways step from a voxel worth <paramref name="beside"/> into a material with this step cost.</summary>
    public int Span(int beside, int cost) => beside - cost;
}
