using MinerCraft.Core.Content;
using MinerCraft.Core.Lighting;
using MinerCraft.Core.Persistence;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Tests;

/// <summary>
/// The sky column is the one derived structure that has to be written, and these are the tests
/// that say so.
/// </summary>
/// <remarks>
/// Light itself is 512 bytes a chunk recomputed in microseconds and is never persisted. A
/// column's open height is the topmost opaque voxel across all Y, and the vertical view radius is
/// 2–3 chunks — so the chunks needed to re-derive it are not loaded. Skipped, a reloaded shaft
/// seeds from the pre-dig surface and goes dark for a reason nothing on screen can explain.
/// </remarks>
public class SkyColumnBlobTests
{
    private const int Size = VoxelConstants.ChunkSize;

    private sealed class FlatWorld(int height) : ISurfaceHeights
    {
        public int SurfaceHeightAt(int worldX, int worldZ) => height;
    }

    private static SkyColumn Dug()
    {
        var column = new SkyColumn(4, -9);
        column.Seed(new FlatWorld(64));

        // A shaft with a stepped floor, so no two rows are alike and a transposed index shows up.
        for (int lz = 0; lz < 8; lz++)
        {
            for (int lx = 0; lx < 5; lx++)
            {
                column.SetOpenAboveY(lx, lz, -30 - lx - (lz * 3));
            }
        }

        return column;
    }

    [Fact]
    public void AColumnRoundTrips()
    {
        SkyColumn original = Dug();
        SkyColumn restored = SkyColumnBlob.Read(SkyColumnBlob.Write(original), 4, -9);

        for (int lz = 0; lz < Size; lz++)
        {
            for (int lx = 0; lx < Size; lx++)
            {
                Assert.Equal(original.OpenAboveY(lx, lz), restored.OpenAboveY(lx, lz));
            }
        }

        Assert.Equal(original.ChunkX, restored.ChunkX);
        Assert.Equal(original.ChunkZ, restored.ChunkZ);
    }

    [Fact]
    public void ARestoredColumnComesBackAlreadyMarkedEdited()
    {
        // A column is only ever written because its heights moved away from the analytic seed, so
        // a restored one is edit-derived by definition. Coming back pristine would mean it was
        // dropped unwritten the next time it unloaded — losing the shaft on the second visit
        // rather than the first, which is a far worse bug to be handed.
        Assert.True(SkyColumnBlob.Read(SkyColumnBlob.Write(Dug()), 4, -9).HasEdits);
    }

    [Fact]
    public void AColumnFiledUnderTheWrongKeyIsRefused()
    {
        byte[] blob = SkyColumnBlob.Write(Dug());

        Assert.Throws<BlobFormatException>(() => SkyColumnBlob.Read(blob, 4, -8));
        Assert.Throws<BlobFormatException>(() => SkyColumnBlob.Read(blob, 5, -9));
    }

    [Fact]
    public void AnUnknownVersionIsRefusedRatherThanRead()
    {
        byte[] blob = SkyColumnBlob.Write(Dug());
        blob[0] = SkyColumnBlob.Version + 1;

        Assert.Throws<BlobFormatException>(() => SkyColumnBlob.Read(blob, 4, -9));
    }

    [Fact]
    public void ATruncatedColumnIsRefused()
    {
        byte[] blob = SkyColumnBlob.Write(Dug());

        for (int length = 0; length < blob.Length; length++)
        {
            byte[] cut = blob[..length];

            // Named, because a loop assertion that fails without saying which iteration is a
            // debugging session rather than a test result.
            try
            {
                SkyColumnBlob.Read(cut, 4, -9);
                Assert.Fail($"{length} of {blob.Length} bytes was accepted.");
            }
            catch (BlobFormatException)
            {
            }
        }
    }

    [Fact]
    public void CorruptionInsideAnIntactBlobIsRefused()
    {
        // The check the stored length cannot make. A truncated blob is caught by arithmetic, but a
        // blob of exactly the right size with a flipped bit in it is not — and that is what zlib's
        // Adler-32 is being paid six bytes for. Without this test the choice of zlib over raw
        // deflate would be decorative: the truncation tests pass either way now.
        byte[] blob = SkyColumnBlob.Write(Dug());
        Assert.Equal(0x01, blob[1] & 0x01); // it really is compressed, or this proves nothing

        int refused = 0;

        // Every byte of the compressed region, one bit each. Some flips land in slack the
        // decompressor ignores, so this asserts a strong majority rather than every single one —
        // and asserts the count, because a loop that silently ran zero times is a pass for the
        // wrong reason.
        for (int i = 14; i < blob.Length; i++)
        {
            byte[] damaged = [.. blob];
            damaged[i] ^= 0x40;

            try
            {
                SkyColumnBlob.Read(damaged, 4, -9);
            }
            catch (BlobFormatException)
            {
                refused++;
            }
        }

        int candidates = blob.Length - 14;
        Assert.True(candidates > 20, $"only {candidates} bytes to damage; the fixture is too small");
        Assert.True(
            refused > candidates * 3 / 4,
            $"only {refused} of {candidates} single-bit corruptions were refused.");
    }

    [Fact]
    public void NegativeHeightsSurvive()
    {
        // A base at the bottom of a deep shaft is the whole point, and a short read that dropped
        // the sign would put its sky heights above the surface rather than far below it.
        var column = new SkyColumn(0, 0);
        for (int lz = 0; lz < Size; lz++)
        {
            for (int lx = 0; lx < Size; lx++)
            {
                column.SetOpenAboveY(lx, lz, -(lx + (lz * Size)) - 1);
            }
        }

        SkyColumn restored = SkyColumnBlob.Read(SkyColumnBlob.Write(column), 0, 0);

        for (int lz = 0; lz < Size; lz++)
        {
            for (int lx = 0; lx < Size; lx++)
            {
                Assert.Equal(column.OpenAboveY(lx, lz), restored.OpenAboveY(lx, lz));
                Assert.True(restored.OpenAboveY(lx, lz) < 0);
            }
        }
    }

    // -------------------------------------------------------------------------------------
    // The dirty bit, which decides whether a column is written at all.
    // -------------------------------------------------------------------------------------

    private sealed class OneVoxelWorld : IVoxelWorld
    {
        private readonly Dictionary<VoxelPos, int> _voxels = [];

        public int DefaultMaterial { get; init; } = 1;

        public void Set(VoxelPos at, int material) => _voxels[at] = material;

        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            materialId = _voxels.TryGetValue(voxel, out int found) ? found : DefaultMaterial;
            return true;
        }
    }

    private static MaterialTable Materials() => MaterialTable.ForTesting(
        isOpaque: [false, true],
        isFluid: [false, false],
        textureLayer: [0, 0],
        hardness: [0f, 1f]);

    [Fact]
    public void AFreshlySeededColumnIsNotEdited()
    {
        var column = new SkyColumn(0, 0);
        column.Seed(new FlatWorld(20));
        Assert.False(column.HasEdits);
    }

    [Fact]
    public void DiggingBelowTheTopDoesNotMarkTheColumnEdited()
    {
        // The tight half of the rule, and most of the columns in a real world. Removing a voxel
        // well under the surface leaves the open height exactly where Seed put it, so the column
        // is still reproducible from the seed and correctly never written. A dirty bit keyed on
        // "a voxel changed" rather than "the height moved" would write nearly every column the
        // player mines under, for nothing.
        var world = new OneVoxelWorld();
        var column = new SkyColumn(0, 0);
        column.Seed(new FlatWorld(20));

        var deep = new VoxelPos(0, 5, 0);
        world.Set(deep, 0);

        Assert.False(column.OnVoxelChanged(deep, world, Materials()));
        Assert.False(column.HasEdits);
    }

    [Fact]
    public void RemovingTheTopmostOpaqueVoxelMarksTheColumnEdited()
    {
        var world = new OneVoxelWorld();
        var column = new SkyColumn(0, 0);
        column.Seed(new FlatWorld(20));

        // The surface is 20, so the column is open above 21. Clearing 20 opens it further.
        var top = new VoxelPos(0, 20, 0);
        for (int y = 20; y > 14; y--) world.Set(new VoxelPos(0, y, 0), 0);

        Assert.True(column.OnVoxelChanged(top, world, Materials()));
        Assert.True(column.HasEdits);
    }

    [Fact]
    public void PlacingSomethingOpaqueAboveTheOpenHeightMarksTheColumnEdited()
    {
        // The other arm of OnVoxelChanged, and it is M6c's: a roof placed over a shaft closes the
        // column back down. Both arms set the flag, and an implementation that set it in only one
        // would lose exactly the columns a builder created rather than the ones a miner did.
        var world = new OneVoxelWorld();
        var column = new SkyColumn(0, 0);
        column.Seed(new FlatWorld(20));

        var roof = new VoxelPos(0, 40, 0);
        world.Set(roof, 1);

        Assert.True(column.OnVoxelChanged(roof, world, Materials()));
        Assert.True(column.HasEdits);
        Assert.Equal(41, column.OpenAboveY(0, 0));
    }
}
