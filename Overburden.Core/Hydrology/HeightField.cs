using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Hydrology;

/// <summary>
/// A square window of the surface, sampled on a world-anchored lattice.
/// </summary>
/// <remarks>
/// <para>
/// <b>The input to the watershed is a pure function, not the world.</b>
/// <see cref="ISurfaceHeights.SurfaceHeightAt"/> is analytic, seeded, and needs no chunk loaded,
/// generated or streamed — so the analysis needs no margin, no barrier and no
/// <c>GenerationStage</c>. It is not a chunk-shaped computation, and the instinct to make it one
/// is the thing to resist. See <c>docs/M7-PLAN.md</c>.
/// </para>
/// <para>
/// <b>The lattice is anchored in world space and the origin must sit on it.</b> That is the same
/// rule the cave lattice lives under and it exists for the same reason: it is what makes two
/// analyses of overlapping windows agree about where a basin's pit is. Basin identity is the pit's
/// world coordinate, so a lattice that shifted with the window would rename every basin in the
/// world each time the player walked far enough — and stored water would reattach to a different
/// basin with nothing failing anywhere.
/// </para>
/// <para>
/// <b>A sample is the plane the water stands on, not the voxel under it.</b>
/// <see cref="ISurfaceHeights.SurfaceHeightAt"/> reports the topmost <i>solid</i> voxel, which
/// occupies <c>[h, h+1)</c>; the field stores <c>h + 1</c>, the first open voxel — the same plane
/// <c>SkyColumn.Seed</c> calls the sky's start, and a test pins the two to each other. From M7
/// until M8b-2a (2026-09-05) the field stored <c>h</c> itself: self-consistent, since every
/// height moved together and a capacity is a difference of heights, and one slab low — a pond's
/// first litres per sample filled a phantom slab inside the rock and drew nothing, which nobody
/// could see until Tier B put water in real voxels and the two conventions met at the seam.
/// Basin identity is a pit's (x, z) and is untouched; stored litres are untouched and their
/// solved levels sit one voxel higher, which is where they always were.
/// </para>
/// <para>
/// Heights stay integer voxel Y, because integers compare exactly and the flood is nothing but
/// comparisons. Metres appear only where litres are computed.
/// </para>
/// </remarks>
public sealed class HeightField
{
    /// <summary>Voxels per sample on each axis — 16 voxels, 4 m. Settled at M7's planning pass.</summary>
    /// <remarks>
    /// Half a chunk, 2×2 samples per chunk column. Deliberately not the 1 m light cell and not the
    /// 8 m chunk column, so it <b>borrows neither one's revisit trigger</b> — the same argument
    /// that kept <c>BrushCell</c> off <c>LightCellPos</c> at M6c-1. A 30 m pond is about 56
    /// samples, which is enough for a believable volume→level curve; at 8 m it would be 16 and the
    /// curve would be a staircase.
    /// </remarks>
    public const int DefaultCellVoxels = 16;

    /// <summary>
    /// The largest height magnitude the flood's packed sort key can carry.
    /// </summary>
    /// <remarks>
    /// <see cref="Watershed"/> packs (filled, height, index) into one 63-bit key so the sweep can
    /// sort with <c>Array.Sort</c> rather than a comparison delegate. 2^20 voxels is 262 km of
    /// relief, against a world whose float positions run out at a few kilometres — so this cannot
    /// bind before something else does. Checked at sample time rather than trusted, because a
    /// silently wrapped key would produce a plausible watershed of the wrong terrain.
    /// </remarks>
    public const int MaxAbsoluteHeight = 1 << 20;

    /// <summary>The most samples one window may hold, for the same packing reason.</summary>
    public const int MaxSamples = 1 << 21;

    private readonly int[] _heights;

    private HeightField(int originX, int originZ, int pitch, int cellVoxels, int[] heights)
    {
        OriginX = originX;
        OriginZ = originZ;
        Pitch = pitch;
        CellVoxels = cellVoxels;
        _heights = heights;

        // A cell is (cellVoxels × 0.25) metres square and a voxel of depth is 0.25 m, so one voxel
        // of water over one cell is (cellVoxels/4)² × 250 litres — an exact integer for every
        // cell size that is a whole number of metres, which is why the constructor insists on one.
        long metres = cellVoxels / VoxelConstants.VoxelsPerMetre;
        LitresPerVoxelOfDepth = metres * metres * 250L;
        SquareMetresPerCell = metres * metres;
    }

    /// <summary>World voxel X of sample (0, 0).</summary>
    public int OriginX { get; }

    /// <summary>World voxel Z of sample (0, 0).</summary>
    public int OriginZ { get; }

    /// <summary>Samples per side.</summary>
    public int Pitch { get; }

    /// <summary>Voxels per sample on each axis.</summary>
    public int CellVoxels { get; }

    /// <summary>Litres held by one voxel of depth over one cell. Exact.</summary>
    public long LitresPerVoxelOfDepth { get; }

    /// <summary>Ground area of one cell.</summary>
    public long SquareMetresPerCell { get; }

    public int SampleCount => Pitch * Pitch;

    /// <summary>Every sample, row-major, <c>z * Pitch + x</c>.</summary>
    public ReadOnlySpan<int> Heights => _heights;

    public int HeightAt(int sx, int sz) => _heights[(sz * Pitch) + sx];

    public int WorldXOf(int sx) => OriginX + (sx * CellVoxels);

    public int WorldZOf(int sz) => OriginZ + (sz * CellVoxels);

    /// <summary>Samples a square window. <paramref name="originX"/> and
    /// <paramref name="originZ"/> must lie on the lattice.</summary>
    public static HeightField Sample(
        ISurfaceHeights heights,
        int originX,
        int originZ,
        int pitch,
        int cellVoxels = DefaultCellVoxels)
    {
        ArgumentNullException.ThrowIfNull(heights);

        if (cellVoxels < VoxelConstants.VoxelsPerMetre
            || cellVoxels % VoxelConstants.VoxelsPerMetre != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cellVoxels),
                cellVoxels,
                "A watershed cell must be a whole number of metres. Litres per voxel of depth is "
                + "(metres)² × 250 and stays an exact integer only then — and the accumulator is "
                + "integer litres precisely so that 'how full is it' has one answer.");
        }

        if (pitch < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pitch), pitch, "A window needs at least an edge to drain through.");
        }

        if ((long)pitch * pitch > MaxSamples)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pitch), pitch, $"At most {MaxSamples} samples fit the flood's packed key.");
        }

        if (originX % cellVoxels != 0 || originZ % cellVoxels != 0)
        {
            throw new ArgumentException(
                "A window's origin must sit on the world-anchored lattice. Basin identity is the "
                + "world coordinate of a pit, so a lattice that moved with the window would rename "
                + "every basin each time the player walked far enough, and stored water would "
                + "reattach to a different basin with nothing failing anywhere.",
                nameof(originX));
        }

        var samples = new int[pitch * pitch];
        for (int sz = 0; sz < pitch; sz++)
        {
            int worldZ = originZ + (sz * cellVoxels);
            int row = sz * pitch;
            for (int sx = 0; sx < pitch; sx++)
            {
                // One above the topmost solid voxel: the plane the water stands on. See the
                // remarks — the field stored the solid voxel's own index from M7 to M8b-2a.
                int height = heights.SurfaceHeightAt(originX + (sx * cellVoxels), worldZ) + 1;
                if (height <= -MaxAbsoluteHeight || height >= MaxAbsoluteHeight)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(heights),
                        height,
                        $"Surface plane is outside ±{MaxAbsoluteHeight} voxels, which the flood's "
                        + "packed sort key cannot carry.");
                }

                samples[row + sx] = height;
            }
        }

        return new HeightField(originX, originZ, pitch, cellVoxels, samples);
    }
}
