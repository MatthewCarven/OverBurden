using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Jobs;

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
