using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// Feeds a <see cref="PortalIndex"/> from the pipeline's three arrival paths, its edit hook and
/// the streamer's forget — and owns the one rule the index deliberately does not: <b>a hole is
/// only what was dug</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>M8b-2's decision 1, as code.</b> Portals are looked for in chunks that have edits — at
/// arrival, when a chunk arrives with them, and at the first edit that gives a chunk any — and
/// nowhere else. A natural cave mouth on a cliff under a lake in an unedited chunk is not a portal,
/// and the lake hangs over it as it did before M8. Revisit trigger, recorded in M8-PLAN: the first
/// play that finds that lake overhanging and reads it as wrong; the widening is to scan every
/// arriving chunk, which is one line here.
/// </para>
/// <para>
/// <b>An unedited chunk arriving beside an edited one re-scans the edited one.</b> A pit whose
/// mouth is the top row of a dug chunk opens onto the bottom row of the chunk above; if that chunk
/// arrives second and unedited, filtering it out would leave the pair unfound for ever. The edited
/// neighbour's scan takes every pair touching it, evaluated against the terrain as it is now, so
/// re-scanning it is what finds the pair — and it files nothing that lies wholly inside the
/// unedited chunk, which keeps decision 1 intact. Idempotent and ~0.1 ms, so six of them on an
/// arrival is nothing.
/// </para>
/// <para>
/// <b>The invariant the fuzz holds this to:</b> the index equals the definition over every pair
/// that touches an edited, loaded chunk and whose both halves hold now. The first edit in a chunk
/// scans it, because pairs that touched it before the edit — a natural bank on its cliff, or an
/// outside voxel in it that a neighbour's dug mouth opens onto — become part of that set the moment
/// the chunk counts as dug.
/// </para>
/// <para>
/// <b>M8c feeds the <see cref="SeepIndex"/> from the same events, under the same filter</b> — a
/// seep face is the portal's shape through rock instead of into air, and "only what was dug seeps"
/// is decision 3 by the argument that named the portal rule: a natural cave under a lake that wept
/// from the moment its chunk generated would be water everywhere with nothing having been done.
/// One tracker rather than two, so the two indices can never disagree about which chunks are dug.
/// </para>
/// </remarks>
public sealed class PortalTracker
{
    private static readonly (int X, int Y, int Z)[] Faces =
    [
        (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
    ];

    /// <summary>
    /// Edits in one chunk between flushes past which the seep index stops being told per voxel
    /// and the chunk is re-scanned whole at the next flush instead.
    /// </summary>
    /// <remarks>
    /// <b>The bite lever (M8c-2).</b> A chunk bite is 32,768 hooks; the seep hook has no
    /// geometric exit below the surface, so that is 14 ms Release and ~45 Debug a bite against
    /// under 2 ms for a forget and a block scan of the chunk. Every face an edit can touch has
    /// the edited voxel as one half, so a scan of that one chunk — which takes faces from either
    /// side — is complete; the neighbours need nothing. Sixty-four is above any hand tool's edits
    /// in a boundary (Alt's chip is 40 a second, 20 a boundary) and far under a bite, and the
    /// per-voxel path stays the immediate one for everything a hand does.
    /// </remarks>
    public const int SeepBurstThreshold = 64;

    private readonly HashSet<ChunkPos> _loaded = [];
    private readonly HashSet<ChunkPos> _edited = [];
    private readonly Dictionary<ChunkPos, int> _burst = [];
    private readonly HashSet<ChunkPos> _rescan = [];

    public PortalTracker(PortalIndex index, SeepIndex? seeps = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        Index = index;
        Seeps = seeps;
        if (seeps is not null)
        {
            // Decision 3 on the inside's chunk: a hollow in an unedited chunk — a natural cave —
            // is nobody's inside however close the dug chunk beside it.
            seeps.IsDug = IsEdited;
        }
    }

    public PortalIndex Index { get; }

    /// <summary>The seep faces, kept to the same dug chunks. Null before M8c's wiring, or on an arm without it.</summary>
    public SeepIndex? Seeps { get; }

    /// <summary>Chunks whose voxels are here.</summary>
    public int LoadedCount => _loaded.Count;

    /// <summary>Chunks that count as dug: arrived with edits, or edited since.</summary>
    public int EditedCount => _edited.Count;

    /// <summary>Scans run, including re-scans of an edited neighbour.</summary>
    public long Scans { get; private set; }

    /// <summary>Chunks re-scanned whole by <see cref="Flush"/> after a burst of edits, over the lifetime.</summary>
    public long Rescans { get; private set; }

    /// <summary>Chunks waiting to be re-scanned: the seep index is stale for them until <see cref="Flush"/>.</summary>
    public int PendingRescans => _rescan.Count;

    public bool IsLoaded(ChunkPos chunk) => _loaded.Contains(chunk);

    public bool IsEdited(ChunkPos chunk) => _edited.Contains(chunk);

    /// <summary>
    /// A chunk's voxels are here — generated, loaded, or back from the cache. Exactly once per
    /// arrival, and the caller says whether it arrived with edits.
    /// </summary>
    public void OnVoxelsReady(ChunkPos chunk, bool hasEdits)
    {
        _loaded.Add(chunk);
        if (hasEdits)
        {
            _edited.Add(chunk);
            Scan(chunk);
        }

        foreach ((int dx, int dy, int dz) in Faces)
        {
            var neighbour = new ChunkPos(chunk.X + dx, chunk.Y + dy, chunk.Z + dz);
            if (_edited.Contains(neighbour) && _loaded.Contains(neighbour))
            {
                Scan(neighbour);
            }
        }
    }

    /// <summary>A voxel was written. The chunk counts as dug from now on.</summary>
    public void OnVoxelChanged(VoxelPos voxel)
    {
        ChunkPos chunk = voxel.Chunk;
        bool newlyEdited = _edited.Add(chunk);
        if (newlyEdited && _loaded.Contains(chunk))
        {
            // Newly dug: everything that touched it before the edit joins the set the invariant
            // names, and the scan sees the edited voxel too, so the per-pair reconcile below is
            // then a no-op for it. Cheaper to let both run than to reason about which.
            Scan(chunk);
        }

        Index.OnVoxelChanged(voxel);
        if (Seeps is null)
        {
            return;
        }

        int edits = (_burst.TryGetValue(chunk, out int sofar) ? sofar : 0) + 1;
        _burst[chunk] = edits;
        if (edits <= SeepBurstThreshold)
        {
            Seeps.OnVoxelChanged(voxel);
        }
        else
        {
            _rescan.Add(chunk);
        }
    }

    /// <summary>
    /// Re-scans every chunk that passed the burst threshold since the last flush, and resets the
    /// counts. Call at the tick boundary before the seep drain reads the index; the portal index
    /// is never stale and needs nothing here.
    /// </summary>
    public void Flush()
    {
        if (Seeps is not null)
        {
            foreach (ChunkPos chunk in _rescan)
            {
                Seeps.Forget(chunk);
                if (_loaded.Contains(chunk))
                {
                    Seeps.Scan(chunk);
                }

                Rescans++;
            }
        }

        _rescan.Clear();
        _burst.Clear();
    }

    /// <summary>A chunk has stopped being tracked — dropped or cached, it makes no difference here.</summary>
    public void OnChunkForgotten(ChunkPos chunk)
    {
        _loaded.Remove(chunk);
        _edited.Remove(chunk);
        _burst.Remove(chunk);
        _rescan.Remove(chunk);
        Index.Forget(chunk);
        Seeps?.Forget(chunk);
    }

    private void Scan(ChunkPos chunk)
    {
        Index.Scan(chunk);
        Seeps?.Scan(chunk);
        Scans++;
    }
}
