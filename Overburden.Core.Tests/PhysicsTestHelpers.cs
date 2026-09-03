using System.Numerics;
using Overburden.Core.Content;
using Overburden.Core.Physics;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The world and the materials both physics suites run against.
/// </summary>
/// <remarks>
/// <b>Shared rather than copied, and the reason is specific.</b> The property these doubles
/// exist to preserve is that an unloaded chunk answers <i>air <b>and</b> false</i>, exactly as
/// <c>ChunkStreamer</c> does — a second copy of <see cref="TestWorld"/> that drifted into
/// returning something obviously wrong would let a collider or a controller that ignores the
/// return value pass its own suite. The project has already been bitten by a duplicated
/// fixture drifting: <c>InMemoryContentSource</c> quietly stopped matching the shipped content
/// it stood in for, which is why <c>ShippedContentTests</c> now reads the real files.
/// </remarks>
internal static class PhysicsTestHelpers
{
    public const int Air = MaterialIds.AirNumericId;
    public const int Dirt = 1;
    public const int Water = 2;
    public const int Stone = 3;

    /// <summary>Two solids and a fluid, so the solidity predicate has something to get wrong.</summary>
    public static readonly MaterialTable Table = MaterialTable.ForTesting(
        //             air    dirt   water  stone
        isOpaque: [false, true, false, true],
        isFluid: [false, false, true, false],
        textureLayer: [0, 1, 2, 3],
        hardness: [0f, 0.8f, 0f, 3.0f]);

    /// <summary>Solid ground up to and including voxel row 3 — a floor whose top is y = 1 m.</summary>
    public static TestWorld FlatGround() => new() { Background = v => v.Y <= 3 ? Dirt : Air };

    /// <summary>
    /// Ground whose top voxel row is given per horizontal position, and air above it.
    /// </summary>
    /// <remarks>
    /// The shape every terrain test wants: a heightmap. Rows are inclusive, so
    /// <c>_ =&gt; 3</c> is <see cref="FlatGround"/>.
    /// </remarks>
    public static TestWorld Terrain(Func<int, int, int> topRow) =>
        new() { Background = v => v.Y <= topRow(v.X, v.Z) ? Dirt : Air };

    /// <summary>Interior voxels 1..20 on every axis are air; everything outside is stone.</summary>
    public static TestWorld SealedRoom() => new()
    {
        Background = v => v.X >= RoomMinVoxel && v.X <= RoomMaxVoxel
            && v.Y >= RoomMinVoxel && v.Y <= RoomMaxVoxel
            && v.Z >= RoomMinVoxel && v.Z <= RoomMaxVoxel
                ? Air
                : Stone,
    };

    public const int RoomMinVoxel = 1;
    public const int RoomMaxVoxel = 20;
    public const float RoomLowMetres = RoomMinVoxel * VoxelConstants.VoxelSize;
    public const float RoomHighMetres = (RoomMaxVoxel + 1) * VoxelConstants.VoxelSize;

    public static int BitsOf(float value) => BitConverter.SingleToInt32Bits(value);

    public static void AssertSameBits(Aabb expected, Aabb actual, string because)
    {
        Assert.True(
            BitsOf(expected.Min.X) == BitsOf(actual.Min.X)
            && BitsOf(expected.Min.Y) == BitsOf(actual.Min.Y)
            && BitsOf(expected.Min.Z) == BitsOf(actual.Min.Z)
            && BitsOf(expected.Size.X) == BitsOf(actual.Size.X)
            && BitsOf(expected.Size.Y) == BitsOf(actual.Size.Y)
            && BitsOf(expected.Size.Z) == BitsOf(actual.Size.Z),
            $"{because}: expected {expected}, got {actual}");
    }

    public static void AssertSameBits(Vector3 expected, Vector3 actual, string because)
    {
        Assert.True(
            BitsOf(expected.X) == BitsOf(actual.X)
            && BitsOf(expected.Y) == BitsOf(actual.Y)
            && BitsOf(expected.Z) == BitsOf(actual.Z),
            $"{because}: expected {expected}, got {actual}");
    }
}

/// <summary>
/// A world that is air unless <see cref="Background"/> says otherwise, with named chunks that
/// answer "not loaded" — and, separately, named chunks that read but refuse to be written.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Background"/> is a predicate rather than a filled region because a floor that has
/// to extend ten metres in every direction is 65,000 dictionary entries, and the tests that
/// need one are the long ones.
/// </para>
/// <para>
/// <b>It gained the write half at M6c-1</b>, so that a placement and a walk can happen in one
/// world: <c>APlacedRampIsWalkedUpByThePhysicsThatAlreadyExists</c> builds a staircase with
/// <c>Place.Ramp</c> and then walks a body up it, and splitting that across two doubles would
/// have been testing that two fixtures agree rather than that the ramp is walkable.
/// </para>
/// <para>
/// <b>Its older twin is <c>CarveTests.FakeEditor</c></b>, which is the same idea with a uniform
/// background instead of a predicate. Merging them is a one-time tidy and is on the TODO; until
/// then, a behaviour added to one belongs in both — an unloaded chunk answering <i>air <b>and</b>
/// false</i> most of all.
/// </para>
/// </remarks>
internal sealed class TestWorld : IVoxelEditor
{
    private readonly Dictionary<VoxelPos, int> _voxels = [];
    private readonly HashSet<ChunkPos> _unloaded = [];
    private readonly HashSet<ChunkPos> _writeRefused = [];

    public Func<VoxelPos, int> Background { get; init; } = _ => PhysicsTestHelpers.Air;

    /// <summary>Every write that was accepted, in order and including any duplicates.</summary>
    public List<(VoxelPos Voxel, int Material)> Writes { get; } = [];

    public HashSet<VoxelPos> WrittenVoxels => Writes.Select(write => write.Voxel).ToHashSet();

    public int this[VoxelPos voxel] =>
        _voxels.TryGetValue(voxel, out int material) ? material : Background(voxel);

    public void Set(VoxelPos voxel, int material) => _voxels[voxel] = material;

    /// <summary>Reads fine, refuses writes — the two halves of the interface disagreeing.</summary>
    public void RefuseWrites(ChunkPos chunk) => _writeRefused.Add(chunk);

    public bool SetVoxel(VoxelPos voxel, int materialId)
    {
        if (_unloaded.Contains(voxel.Chunk) || _writeRefused.Contains(voxel.Chunk))
        {
            return false;
        }

        Writes.Add((voxel, materialId));
        _voxels[voxel] = materialId;
        return true;
    }

    /// <summary>Fills an inclusive voxel box, in voxel indices.</summary>
    public void Fill(VoxelPos min, VoxelPos max, int material)
    {
        for (int z = min.Z; z <= max.Z; z++)
        for (int x = min.X; x <= max.X; x++)
        for (int y = min.Y; y <= max.Y; y++)
        {
            _voxels[new VoxelPos(x, y, z)] = material;
        }
    }

    public void Unload(ChunkPos chunk) => _unloaded.Add(chunk);

    public bool TryGetMaterial(VoxelPos voxel, out int materialId)
    {
        if (_unloaded.Contains(voxel.Chunk))
        {
            // Air *and* false, exactly as ChunkStreamer does it. A test double that returned
            // something obviously wrong here would let a collider that ignores the return value
            // pass.
            materialId = PhysicsTestHelpers.Air;
            return false;
        }

        materialId = this[voxel];
        return true;
    }
}
