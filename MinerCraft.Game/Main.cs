using Godot;
using MinerCraft.Core.Content;
using MinerCraft.Core.Meshing;
using MinerCraft.Core.Voxels;
using MinerCraft.Game.Rendering;

namespace MinerCraft.Game;

/// <summary>
/// M1d entry point: load content, generate one chunk of test terrain, greedy-mesh it, and
/// hand the result to the RenderingServer. Replaced by the chunk manager at M2.
/// </summary>
public partial class Main : Node3D
{
    private VoxelRenderInstance? _chunk;

    public GameContent Content { get; } = new();

    public override void _Ready()
    {
        LoadContent();

        var materials = MaterialTable.From(Content.Materials);
        var voxels = BuildTestTerrain();

        var padded = new PaddedChunk();
        padded.CopyInteriorFrom(voxels);
        padded.FillBorder(MaterialIds.AirNumericId); // a lone chunk, so open on all sides

        var mesh = new ChunkMesh();
        new GreedyMesher().Build(padded, materials, mesh);

        Report(voxels, padded, materials, mesh);

        var material = VoxelMaterial.Create();
        var arrayMesh = ChunkMeshConverter.ToArrayMesh(mesh, material, material);

        if (arrayMesh is not null)
        {
            _chunk = new VoxelRenderInstance(GetWorld3D(), arrayMesh, Transform3D.Identity);
        }
    }

    /// <summary>
    /// A slice of terrain chosen to show what greedy meshing does: broad flat layers that
    /// merge into a handful of quads, a rolling surface that cannot merge as well, and a
    /// carved cavity so the interior geometry is not trivial.
    /// </summary>
    private VoxelChunk BuildTestTerrain()
    {
        int stone = MaterialId("minercraft:stone");
        int dirt = MaterialId("minercraft:dirt");
        int grass = MaterialId("minercraft:grass");

        var chunk = new VoxelChunk(MaterialIds.AirNumericId);
        const int size = VoxelConstants.ChunkSize;

        for (int z = 0; z < size; z++)
        for (int x = 0; x < size; x++)
        {
            int height = 18
                         + (int)(3f * Mathf.Sin(x * 0.28f))
                         + (int)(3f * Mathf.Cos(z * 0.22f));
            height = Mathf.Clamp(height, 1, size - 1);

            for (int y = 0; y <= height; y++)
            {
                chunk.Set(x, y, z, y == height ? grass : y >= height - 3 ? dirt : stone);
            }
        }

        // Carve a cavity, so the mesh has interior surfaces and not just a heightfield.
        var centre = new Vector3(12f, 9f, 16f);
        for (int z = 0; z < size; z++)
        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
        {
            if (new Vector3(x, y, z).DistanceTo(centre) < 6.5f)
            {
                chunk.Set(x, y, z, MaterialIds.AirNumericId);
            }
        }

        return chunk;
    }

    private void Report(
        VoxelChunk voxels, PaddedChunk padded, MaterialTable materials, ChunkMesh mesh)
    {
        // Diagnostic only: what a culled mesher would have emitted, for comparison.
        int culledFaces = 0;
        for (int face = 0; face < VoxelFace.Count; face++)
        {
            int[] offset = VoxelFace.Offset[face];

            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
            for (int x = 0; x < VoxelConstants.ChunkSize; x++)
            for (int y = 0; y < VoxelConstants.ChunkSize; y++)
            {
                int here = padded[x, y, z];
                int there = padded[x + offset[0], y + offset[1], z + offset[2]];

                if (here != MaterialIds.AirNumericId && here != there && !materials.IsOpaque(there))
                {
                    culledFaces++;
                }
            }
        }

        int naive = VoxelConstants.ChunkVolume * 6;
        GD.Print(
            $"M1d: chunk stored as {voxels} ({voxels.EstimatedBytes} B for " +
            $"{VoxelConstants.ChunkVolume} voxels)\n" +
            $"  naive {naive} faces → culled {culledFaces} → greedy {mesh.QuadCount} quads " +
            $"({(float)culledFaces / Mathf.Max(mesh.QuadCount, 1):F1}× better than culled, " +
            $"{(float)naive / Mathf.Max(mesh.QuadCount, 1):F0}× better than naive)\n" +
            $"  {mesh.Opaque.QuadCount} opaque, {mesh.Transparent.QuadCount} transparent, " +
            $"{mesh.Opaque.IndexCount / 3} triangles");
    }

    private void LoadContent()
    {
        string modsPath = ProjectSettings.GlobalizePath("res://mods");
        var result = Content.Load(new DirectoryContentSource(modsPath));

        foreach (string error in result.Errors)
        {
            GD.PushError($"Content: {error}");
        }

        GD.Print($"Content: {result}");
    }

    private int MaterialId(string identifier) =>
        Content.Materials.TryGetNumericId(Identifier.Parse(identifier), out int id)
            ? id
            : MaterialIds.AirNumericId;

    public override void _ExitTree()
    {
        _chunk?.Dispose();
        _chunk = null;
    }
}
