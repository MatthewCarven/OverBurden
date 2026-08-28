using System;
using System.Collections.Generic;

namespace MinerCraft.Game.Rendering;

/// <summary>
/// Releases chunk meshes a few per frame instead of all at once.
/// </summary>
/// <remarks>
/// <para>
/// <b>A count per frame, not a time slice.</b> Every other budget in this codebase is
/// measured in milliseconds, and this one cannot be: Godot defers the actual buffer
/// deallocation, so the cost is not spent inside the call we would be timing. A stopwatch
/// around <see cref="VoxelRenderInstance.Dispose"/> reports approximately nothing while the
/// real work lands a frame or two later — which is exactly why the resulting stutter gets
/// blamed on meshing, the thing that visibly happened at the same time.
/// </para>
/// <para>
/// godot_voxel needed a whole dedicated runner for this and their header is openly unhappy
/// about it: freeing many meshes at once causes "deferred stutters when a terrain contains a
/// lot of chunks and the camera moves fast". We draw through <c>RenderingServer</c> with
/// explicit RIDs, so we hit the same thing and can fix it the same way, cheaply.
/// </para>
/// <para>
/// <b>The budget scales with the backlog</b>, rather than being a flat N. A fixed small
/// budget is fine when a few chunks unload and unbounded when the player flies: the queue
/// then grows faster than it drains and never catches up. Draining a fixed <em>fraction</em>
/// clears any backlog in roughly <see cref="TargetDrainFrames"/> frames no matter how large
/// it got, while staying gentle when there is little to do.
/// </para>
/// </remarks>
public sealed class MeshFreeQueue
{
    /// <summary>Always free at least this many per frame, so a small queue still drains promptly.</summary>
    public const int MinimumPerFrame = 4;

    /// <summary>
    /// Roughly how many frames a backlog should take to clear — about half a second at 60 Hz,
    /// which is the figure godot_voxel settled on for the same problem.
    /// </summary>
    public const int TargetDrainFrames = 30;

    private readonly Queue<VoxelRenderInstance> _pending = new();

    /// <summary>Meshes waiting to be released.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Longest the queue ever got. The number that says whether the budget is right.</summary>
    public int PeakPending { get; private set; }

    /// <summary>Total meshes released through this queue.</summary>
    public int TotalFreed { get; private set; }

    /// <summary>
    /// Takes ownership of an instance. It stays alive, and drawn, until <see cref="Drain"/>
    /// reaches it.
    /// </summary>
    /// <remarks>
    /// Instances are queued by reference rather than by chunk position on purpose: a chunk
    /// that is re-meshed while its old mesh is still waiting here has a live new instance
    /// under the same position, and freeing by position would take the wrong one.
    /// </remarks>
    public void Enqueue(VoxelRenderInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        // Hidden immediately. The mesh is not freed yet, but it must stop being drawn now —
        // otherwise a chunk that unloaded stays visible for up to half a second, which reads
        // as terrain failing to disappear rather than as a deferred free.
        instance.SetVisible(false);

        _pending.Enqueue(instance);
        if (_pending.Count > PeakPending)
        {
            PeakPending = _pending.Count;
        }
    }

    /// <summary>Frees this frame's allowance. Returns how many went.</summary>
    public int Drain()
    {
        int budget = Math.Max(
            MinimumPerFrame,
            (_pending.Count + TargetDrainFrames - 1) / TargetDrainFrames);

        int freed = 0;
        while (freed < budget && _pending.Count > 0)
        {
            _pending.Dequeue().Dispose();
            freed++;
        }

        TotalFreed += freed;
        return freed;
    }

    /// <summary>
    /// Frees everything still queued, ignoring the budget. For shutdown, where a stutter no
    /// longer matters and a leak would.
    /// </summary>
    public int DrainAll()
    {
        int freed = _pending.Count;
        while (_pending.Count > 0)
        {
            _pending.Dequeue().Dispose();
        }

        TotalFreed += freed;
        return freed;
    }
}
