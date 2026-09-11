using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Voxels;

namespace Overburden.Core.Structure;

/// <summary>
/// What a sideways step costs at every (sample, row, material) of one chunk — decision 5's
/// curve, evaluated once per derive over the water table rather than once per voxel.
/// </summary>
/// <remarks>
/// <para>
/// The head over a voxel is a property of its column's 2 m sample and its row
/// (<see cref="Saturation.HeadMetresAt"/>), so a chunk has <c>4 × 4 × 32 = 512</c> distinct heads
/// and the table is 512 rows of one cost per material — a few kilobytes, built in microseconds,
/// read once per voxel at a byte index. A dry chunk's rows are all the same; <see cref="Dry"/>
/// builds that once per rule and table and is shared.
/// </para>
/// <para>
/// The cost is rounded here and nowhere else (<see cref="SupportRule.Cost"/>), so the curve is
/// continuous in effect: a head halfway between two of its points is a distance halfway between
/// and a cost rounded from that.
/// </para>
/// </remarks>
public sealed class SupportCostTable
{
    /// <summary>The watershed's cell, 8 voxels — the table is laid out for it and no other.</summary>
    public const int CellVoxels = HeightField.DefaultCellVoxels;

    /// <summary>Samples along one chunk edge at the watershed's 2 m cell: 4.</summary>
    public const int SamplesPerAxis = VoxelConstants.ChunkSize / CellVoxels;

    /// <summary>Rows in the table: 16 samples by 32 voxel rows.</summary>
    public const int Rows = SamplesPerAxis * SamplesPerAxis * VoxelConstants.ChunkSize;

    private readonly byte[] _cost;
    private readonly int _materials;

    private SupportCostTable(byte[] cost, int materials, bool isDry)
    {
        _cost = cost;
        _materials = materials;
        IsDry = isDry;
    }

    /// <summary>True when no row had any head over it: every row is the dry cost.</summary>
    public bool IsDry { get; }

    /// <summary>The table for a chunk nothing stands over — or for every chunk when saturation is off.</summary>
    public static SupportCostTable Dry(MaterialTable materials, SupportRule rule)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(rule);

        int count = materials.Count;
        var cost = new byte[Rows * count];
        for (int material = 0; material < count; material++)
        {
            cost[material] = Clamp(rule.Cost(materials.SupportDistance(material)));
        }

        for (int row = 1; row < Rows; row++)
        {
            Array.Copy(cost, 0, cost, row * count, count);
        }

        return new SupportCostTable(cost, count, isDry: true);
    }

    /// <summary>
    /// The table for one chunk over a water table. A null table is dry everywhere —
    /// <c>--saturation=0</c>, the control.
    /// </summary>
    public static SupportCostTable Build(
        ChunkPos chunk,
        IWaterTable? table,
        MaterialTable materials,
        SupportRule rule)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(rule);

        if (table is null)
        {
            return Dry(materials, rule);
        }

        int count = materials.Count;
        var cost = new byte[Rows * count];
        bool dry = true;
        VoxelPos origin = chunk.OriginVoxel;
        Span<byte> dryRow = stackalloc byte[count];
        for (int material = 0; material < count; material++)
        {
            dryRow[material] = Clamp(rule.Cost(materials.SupportDistance(material)));
        }

        for (int sz = 0; sz < SamplesPerAxis; sz++)
        {
            for (int sx = 0; sx < SamplesPerAxis; sx++)
            {
                for (int y = 0; y < VoxelConstants.ChunkSize; y++)
                {
                    var voxel = new VoxelPos(origin.X + (sx * CellVoxels), origin.Y + y, origin.Z + (sz * CellVoxels));
                    double head = Saturation.HeadMetresAt(voxel, table, CellVoxels);
                    int row = RowIndex(sx, y, sz) * count;
                    if (head <= 0)
                    {
                        dryRow.CopyTo(cost.AsSpan(row, count));
                        continue;
                    }

                    dry = false;
                    for (int material = 0; material < count; material++)
                    {
                        cost[row + material] = Clamp(rule.Cost(materials.SupportDistanceAt(material, head)));
                    }
                }
            }
        }

        return new SupportCostTable(cost, count, dry);
    }

    /// <summary>The sideways step cost into <paramref name="material"/> at chunk-local coordinates.</summary>
    public int Cost(int localX, int localY, int localZ, int material) =>
        _cost[(RowIndex(localX / CellVoxels, localY, localZ / CellVoxels) * _materials) + material];

    private static int RowIndex(int sampleX, int y, int sampleZ) =>
        (((sampleZ * SamplesPerAxis) + sampleX) * VoxelConstants.ChunkSize) + y;

    /// <summary>A cost is at most <c>2K</c>, which is 240 and fits the byte; the rule already bounds it.</summary>
    private static byte Clamp(int cost) => (byte)Math.Clamp(cost, 1, 2 * SupportRule.MaxBudget);
}
