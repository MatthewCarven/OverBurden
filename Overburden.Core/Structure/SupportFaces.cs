using Overburden.Core.Meshing;

namespace Overburden.Core.Structure;

/// <summary>
/// What a derive knows about the six chunks around the one it is deriving: for each face, the
/// neighbour's layer of budgets touching it, or nothing — in which case the face is anchored.
/// </summary>
/// <remarks>
/// <para>
/// Gathered where <c>PaddedLight</c> is gathered — on the sim thread at submission, in M9b — and
/// owned by the derive from then on. Six times 1,024 bytes, copied rather than referenced, because
/// the neighbour's field is replaced by its own next derive while this one runs on a worker.
/// </para>
/// <para>
/// <b>Absent is anchored, and that is decision 4's starting point.</b> A neighbour that is not
/// analysed, not loaded, or not yet derived contributes a full budget through every solid voxel
/// of its border — the optimistic direction, since <i>nothing falls</i> is the failure that costs
/// nothing — and the iteration descends from there. The derive still reads solidity from the
/// padded copy, so an anchored face over air anchors nothing: the M9a pass's item 4.
/// </para>
/// </remarks>
public sealed class SupportFaces
{
    private readonly byte[]?[] _layers = new byte[VoxelFace.Count][];

    /// <summary>The neighbour's layer across <paramref name="face"/>, or null when the face is anchored.</summary>
    public ReadOnlySpan<byte> Layer(int face) => _layers[face];

    /// <summary>True when nothing is known across this face and it anchors.</summary>
    public bool IsAnchored(int face) => _layers[face] is null;

    /// <summary>Every face anchored — the state a first derive starts from.</summary>
    public void AnchorAll() => Array.Clear(_layers);

    /// <summary>Anchor one face.</summary>
    public void Anchor(int face) => _layers[face] = null;

    /// <summary>
    /// Nothing across this face: a layer of <see cref="ChunkSupport.Unreached"/>, for an analysed
    /// neighbour that has not derived yet. The join's starting point — see <see cref="SupportRegion"/>.
    /// </summary>
    public void Unreached(int face)
    {
        byte[] layer = _layers[face] ?? new byte[ChunkSupport.LayerSize];
        Array.Fill(layer, ChunkSupport.Unreached);
        _layers[face] = layer;
    }

    /// <summary>
    /// Takes the layer of <paramref name="neighbour"/> that touches this chunk across
    /// <paramref name="face"/> — which is the neighbour's <em>opposite</em> face.
    /// </summary>
    public void From(int face, ChunkSupport neighbour)
    {
        ArgumentNullException.ThrowIfNull(neighbour);

        byte[] layer = _layers[face] ?? new byte[ChunkSupport.LayerSize];
        neighbour.CopyLayer(Opposite(face), layer);
        _layers[face] = layer;
    }

    /// <summary>The face across from <paramref name="face"/>: +X ↔ −X and so on, in <see cref="VoxelFace"/>'s order.</summary>
    public static int Opposite(int face) => face ^ 1;
}
