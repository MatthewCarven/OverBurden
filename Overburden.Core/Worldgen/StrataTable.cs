namespace Overburden.Core.Worldgen;

/// <summary>One band of material, measured downward from the surface.</summary>
public readonly record struct StrataLayer(int MaterialId, int ThicknessInVoxels)
{
    public override string ToString() => $"{ThicknessInVoxels} voxels of material {MaterialId}";
}

/// <summary>
/// What material sits at a given depth below the surface.
/// </summary>
/// <remarks>
/// <para>
/// Layers are data, not constants in the generator, because strata are content: a mod
/// that adds a new rock type has to be able to place it without touching worldgen code.
/// The table takes numeric material IDs already resolved through the registry, which is
/// what keeps this class — and all of <c>Worldgen</c> — independent of how content is
/// loaded.
/// </para>
/// <para>
/// Depth 0 is the topmost solid voxel of a column, so a 1-voxel grass layer occupies
/// depth 0 alone. Below the last declared layer, <see cref="BottomMaterialId"/> continues
/// forever.
/// </para>
/// </remarks>
public sealed class StrataTable
{
    private readonly int[] _byDepth;

    public StrataTable(IReadOnlyList<StrataLayer> layers, int bottomMaterialId)
    {
        ArgumentNullException.ThrowIfNull(layers);

        int total = 0;
        foreach (StrataLayer layer in layers)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(layer.ThicknessInVoxels, nameof(layers));
            total += layer.ThicknessInVoxels;
        }

        BottomMaterialId = bottomMaterialId;

        // Flattened once, because this is read per voxel — 32,768 times per chunk — and a
        // loop over layers per lookup is the kind of thing that quietly dominates a profile.
        _byDepth = new int[total];
        int cursor = 0;
        foreach (StrataLayer layer in layers)
        {
            for (int i = 0; i < layer.ThicknessInVoxels; i++)
            {
                _byDepth[cursor++] = layer.MaterialId;
            }
        }
    }

    /// <summary>Material below the deepest declared layer.</summary>
    public int BottomMaterialId { get; }

    /// <summary>Total thickness of the declared layers, in voxels.</summary>
    public int DeclaredDepth => _byDepth.Length;

    /// <param name="depthBelowSurface">0 is the topmost solid voxel of the column.</param>
    public int MaterialAtDepth(int depthBelowSurface) =>
        (uint)depthBelowSurface < (uint)_byDepth.Length
            ? _byDepth[depthBelowSurface]
            : BottomMaterialId;
}
