using Overburden.Core.Voxels;

namespace Overburden.Core.Jobs;

/// <summary>
/// What a job does. The numeric value <em>is</em> the band2 priority, so job classes are
/// ordered against each other by declaration value alone.
/// </summary>
/// <remarks>
/// Generation outranks meshing because a mesh job cannot run until the data it reads
/// exists. Scheduling a mesh ahead of the generation it depends on just burns a worker
/// slot on a job that has to be postponed.
/// </remarks>
public enum JobKind : byte
{
    Mesh = 10,
    Generate = 20,

    /// <summary>Reading a stored chunk instead of generating it. M6b.</summary>
    /// <remarks>
    /// Level with <see cref="Generate"/> deliberately: it is the same step of the same pipeline
    /// taking a different route, and a chunk that loads is one that would otherwise generate. A
    /// different band would make the world fill in a different order depending on how much of it
    /// the player happened to have dug, which is a difference nobody wants to debug.
    /// </remarks>
    Load = 20,

    /// <summary>Writing the dirty set to the store. M6b.</summary>
    /// <remarks>
    /// <b>Above everything, and starvation is why.</b> The queue is a flat list re-sorted on a
    /// timer and workers take the best entry; under continuous flight it never empties, so a save
    /// ranked below generation would wait for a quiet moment that never comes. The cost of ranking
    /// it top is nil — saves are batched, rare, and about 10 µs a chunk — and the cost of getting
    /// it wrong is losing the work the whole milestone exists to keep.
    /// </remarks>
    Save = 30,

    /// <summary>One tick of the Tier B automaton on a worker. The water thread, slice 3b.</summary>
    /// <remarks>
    /// Level with <see cref="Save"/> on Save's own argument: the queue never empties under flight,
    /// so anything ranked below generation waits for a quiet moment that never comes, and a flood
    /// is exactly when it matters. The honest difference is that a tick is not nil — one worker of
    /// eight for its length — which is the named trigger for a dedicated thread if the
    /// <c>sources</c> line shows generation starving.
    /// </remarks>
    FluidTick = 30,

    /// <summary>One chunk's support derive on a worker. M9b.</summary>
    /// <remarks>
    /// Level with <see cref="Mesh"/>: it is caused by the edit the remesh is caused by, costs a
    /// millisecond, and carries the remesh's urgency byte when an edit caused it, so the tint
    /// lands beside the mesh it belongs to rather than after the horizon has loaded.
    /// </remarks>
    Support = 10,
}

/// <summary>
/// A job's place in the queue, packed into a single <c>uint</c> so that comparing two
/// priorities is one integer comparison rather than a tuple walk.
/// </summary>
/// <remarks>
/// <para>
/// Four byte-wide bands, most significant first, so a plain unsigned comparison gives
/// band3 precedence over band2 over band1 over band0:
/// </para>
/// <list type="table">
///   <item><term>band3</term><description>overrides, from <see cref="IJob.Urgency"/> — a remesh the player is waiting on</description></item>
///   <item><term>band2</term><description>job class, from <see cref="JobKind"/></description></item>
///   <item><term>band1</term><description>unused until M13 LOD</description></item>
///   <item><term>band0</term><description>distance to the nearest viewer, decreasing</description></item>
/// </list>
/// <para>
/// The packing is the whole point. See docs/M2-PLAN.md — the queue re-sorts thousands of
/// entries several times a second, so the comparator has to be trivial.
/// </para>
/// </remarks>
public readonly record struct JobPriority(uint Packed) : IComparable<JobPriority>
{
    /// <summary>Lowest possible priority. Sorts before everything.</summary>
    public static readonly JobPriority Min = new(uint.MinValue);

    /// <summary>Highest possible priority. Sorts after everything.</summary>
    public static readonly JobPriority Max = new(uint.MaxValue);

    public static JobPriority FromBands(byte band0, byte band1, byte band2, byte band3) =>
        new(((uint)band3 << 24) | ((uint)band2 << 16) | ((uint)band1 << 8) | band0);

    public byte Band0 => (byte)(Packed & 0xFF);
    public byte Band1 => (byte)((Packed >> 8) & 0xFF);
    public byte Band2 => (byte)((Packed >> 16) & 0xFF);
    public byte Band3 => (byte)((Packed >> 24) & 0xFF);

    /// <summary>
    /// Priority of a job of <paramref name="kind"/> acting on <paramref name="position"/>,
    /// given where the viewers currently are.
    /// </summary>
    /// <remarks>
    /// Distance is Chebyshev in chunks, matching <see cref="ChunkPos.ChebyshevDistanceTo"/>
    /// and the fact that the loaded region is a box rather than a sphere. Beyond 255 chunks
    /// every job is equally unimportant, and something is wrong with the view distance
    /// anyway.
    /// </remarks>
    public static JobPriority Evaluate(
        JobKind kind, ChunkPos position, ViewerSet viewers, byte urgency = 0)
    {
        int distance = viewers.NearestChebyshevDistance(position);
        byte band0 = (byte)(255 - Math.Clamp(distance, 0, 255));
        return FromBands(band0, band1: 0, band2: (byte)kind, band3: urgency);
    }

    public int CompareTo(JobPriority other) => Packed.CompareTo(other.Packed);

    public static bool operator <(JobPriority a, JobPriority b) => a.Packed < b.Packed;

    public static bool operator >(JobPriority a, JobPriority b) => a.Packed > b.Packed;

    public static bool operator <=(JobPriority a, JobPriority b) => a.Packed <= b.Packed;

    public static bool operator >=(JobPriority a, JobPriority b) => a.Packed >= b.Packed;

    public override string ToString() => $"Priority(0x{Packed:X8})";
}
