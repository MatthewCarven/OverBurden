using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Structure;

/// <summary>One piece of a collapse: up to four face-connected minos and the at-risk voxels inside them.</summary>
/// <param name="Order">Its place in the release order, from 0.</param>
/// <param name="MinoVoxels">Voxels along a mino's edge: 1, 2 or 4.</param>
/// <param name="Minos">The minos, in mino-lattice coordinates (a voxel's coordinate shifted down by log2 of the mino).</param>
/// <param name="Voxels">The at-risk voxels the minos hold, sorted by Y, then X, then Z.</param>
/// <param name="FrontDistance">How many minos from the event's origin the piece's first mino is — the corruption's front.</param>
public sealed record CollapsePiece(
    int Order,
    int MinoVoxels,
    (int X, int Y, int Z)[] Minos,
    VoxelPos[] Voxels,
    int FrontDistance)
{
    /// <summary>Whether the piece is a whole tetracube rather than a fragment of one to three minos.</summary>
    public bool IsTetracube => Minos.Length == PieceCutter.MaxMinos;
}

/// <summary>
/// Cuts an at-risk set into Tetris pieces in release order (COLLAPSE-PLAN decisions 1, 2 and 2b):
/// a front spreading from the event's origin across the set — his "like digital corruption
/// spreading" — each piece grown from the next mino on the front to at most four face-connected
/// minos, and no mino released before the at-risk voxels under it.
/// </summary>
/// <remarks>
/// <para>
/// <b>No mino leaves before what it stands on.</b> A mino whose bottom voxels sit on at-risk voxels
/// in the mino below waits for that mino, or goes with it in the same piece. Every released piece
/// therefore has air, water or held rock under every one of its bottom voxels — and by the rule
/// (Collapse's remark: under an at-risk voxel is air or another at-risk voxel) it is air or water.
/// A piece released on top of rock that has not gone yet would land where it stood. The front is a
/// priority and this is a constraint, so on a tall structure the corruption spreads along the base
/// first and climbs.
/// </para>
/// <para>
/// <b>Any four face-connected minos are a tetracube</b>, so the cutter grows a connected set and
/// never consults a shape table. The growth prefers the seed's own layer (flat pieces read as
/// Tetris), then the front, then a seeded draw; where nothing eligible touches the piece it stops
/// short, a fragment of one to three minos.
/// </para>
/// <para>
/// <b>Deterministic from (set, origin, mino, seed)</b>, per non-negotiable 1: every tie breaks on
/// <see cref="Hash64"/>, never on a hash set's iteration order.
/// </para>
/// </remarks>
public static class PieceCutter
{
    /// <summary>The most minos a piece holds: a tetracube.</summary>
    public const int MaxMinos = 4;

    /// <summary>Every at-risk voxel, in pieces, in release order.</summary>
    /// <param name="atRisk">The voxels to cut. Each appears in exactly one piece.</param>
    /// <param name="origin">Where the collapse started; the front spreads from the mino nearest it.</param>
    /// <param name="minoVoxels">Voxels along a mino's edge: 1, 2 or 4.</param>
    /// <param name="seed">The world's seed mixed with the event's; ties break on it.</param>
    public static List<CollapsePiece> Cut(IReadOnlyCollection<VoxelPos> atRisk, VoxelPos origin, int minoVoxels, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(atRisk);
        int shift = minoVoxels switch
        {
            1 => 0,
            2 => 1,
            4 => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(minoVoxels), minoVoxels, "A mino is 1, 2 or 4 voxels on a side."),
        };

        var pieces = new List<CollapsePiece>();
        if (atRisk.Count == 0)
        {
            return pieces;
        }

        var set = atRisk as IReadOnlySet<VoxelPos> ?? new HashSet<VoxelPos>(atRisk);
        var voxelsIn = new Dictionary<(int X, int Y, int Z), List<VoxelPos>>();
        var dependsOnBelow = new HashSet<(int X, int Y, int Z)>();
        foreach (VoxelPos v in set)
        {
            (int X, int Y, int Z) cell = (v.X >> shift, v.Y >> shift, v.Z >> shift);
            if (!voxelsIn.TryGetValue(cell, out List<VoxelPos>? list))
            {
                list = [];
                voxelsIn[cell] = list;
            }

            list.Add(v);

            // Only a voxel on its mino's bottom layer can stand on another mino: the one directly below.
            VoxelPos under = v.Offset(0, -1, 0);
            if ((under.Y >> shift) != cell.Y && set.Contains(under))
            {
                dependsOnBelow.Add(cell);
            }
        }

        Dictionary<(int X, int Y, int Z), int> distance = FrontDistances(voxelsIn.Keys, origin, shift, seed);

        var removed = new HashSet<(int X, int Y, int Z)>();
        var queue = new PriorityQueue<(int X, int Y, int Z), (int Distance, int Y, ulong Tie)>();
        foreach ((int X, int Y, int Z) cell in voxelsIn.Keys)
        {
            if (!dependsOnBelow.Contains(cell))
            {
                queue.Enqueue(cell, (distance[cell], cell.Y, Hash64.Combine(seed, cell.X, cell.Y, cell.Z)));
            }
        }

        var piece = new List<(int X, int Y, int Z)>(MaxMinos);
        var inPiece = new HashSet<(int X, int Y, int Z)>();
        while (removed.Count < voxelsIn.Count)
        {
            (int X, int Y, int Z) seedCell;
            do
            {
                if (!queue.TryDequeue(out seedCell, out _))
                {
                    throw new InvalidOperationException(
                        "No mino is free to fall while minos remain: a dependency pointed somewhere other than straight down.");
                }
            }
            while (removed.Contains(seedCell));

            piece.Clear();
            inPiece.Clear();
            piece.Add(seedCell);
            inPiece.Add(seedCell);
            ulong growSeed = Hash64.Mix(seed ^ (ulong)(pieces.Count + 1));
            while (piece.Count < MaxMinos)
            {
                (int X, int Y, int Z)? best = null;
                (int Layer, int Distance, ulong Tie) bestScore = default;
                foreach ((int X, int Y, int Z) member in piece)
                {
                    foreach ((int X, int Y, int Z) next in Polycubes.Neighbours(member.X, member.Y, member.Z))
                    {
                        if (!voxelsIn.ContainsKey(next) || removed.Contains(next) || inPiece.Contains(next))
                        {
                            continue;
                        }

                        if (dependsOnBelow.Contains(next))
                        {
                            (int X, int Y, int Z) below = (next.X, next.Y - 1, next.Z);
                            if (!removed.Contains(below) && !inPiece.Contains(below))
                            {
                                continue;
                            }
                        }

                        (int, int, ulong) score = (next.Y == seedCell.Y ? 0 : 1, distance[next], Hash64.Combine(growSeed, next.X, next.Y, next.Z));
                        if (best is null || score.CompareTo(bestScore) < 0)
                        {
                            best = next;
                            bestScore = score;
                        }
                    }
                }

                if (best is not { } chosen)
                {
                    break;
                }

                piece.Add(chosen);
                inPiece.Add(chosen);
            }

            var voxels = new List<VoxelPos>();
            foreach ((int X, int Y, int Z) cell in piece)
            {
                removed.Add(cell);
                voxels.AddRange(voxelsIn[cell]);
            }

            // A mino waiting on one of these can go now.
            foreach ((int X, int Y, int Z) cell in piece)
            {
                (int X, int Y, int Z) above = (cell.X, cell.Y + 1, cell.Z);
                if (dependsOnBelow.Contains(above) && voxelsIn.ContainsKey(above) && !removed.Contains(above))
                {
                    queue.Enqueue(above, (distance[above], above.Y, Hash64.Combine(seed, above.X, above.Y, above.Z)));
                }
            }

            voxels.Sort(static (a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
            pieces.Add(new CollapsePiece(pieces.Count, minoVoxels, [.. piece], [.. voxels], distance[seedCell]));
        }

        return pieces;
    }

    /// <summary>
    /// Breadth-first mino distance from the origin's mino (or the nearest present one) over the
    /// set's face-connected minos; a component the origin does not reach starts again from its
    /// lowest mino, one past the farthest distance so far.
    /// </summary>
    private static Dictionary<(int X, int Y, int Z), int> FrontDistances(
        IEnumerable<(int X, int Y, int Z)> cells,
        VoxelPos origin,
        int shift,
        ulong seed)
    {
        var all = cells.ToList();
        (int X, int Y, int Z) o = (origin.X >> shift, origin.Y >> shift, origin.Z >> shift);
        (int X, int Y, int Z) start = all
            .OrderBy(c => ((long)(c.X - o.X) * (c.X - o.X)) + ((long)(c.Y - o.Y) * (c.Y - o.Y)) + ((long)(c.Z - o.Z) * (c.Z - o.Z)))
            .ThenBy(c => Hash64.Combine(seed, c.X, c.Y, c.Z))
            .First();

        var present = new HashSet<(int X, int Y, int Z)>(all);
        var distance = new Dictionary<(int X, int Y, int Z), int>(all.Count);
        int farthest = 0;
        var frontier = new Queue<(int X, int Y, int Z)>();
        while (true)
        {
            distance[start] = distance.Count == 0 ? 0 : farthest + 1;
            farthest = Math.Max(farthest, distance[start]);
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                (int X, int Y, int Z) cell = frontier.Dequeue();
                int d = distance[cell];
                foreach ((int X, int Y, int Z) next in Polycubes.Neighbours(cell.X, cell.Y, cell.Z))
                {
                    if (present.Contains(next) && !distance.ContainsKey(next))
                    {
                        distance[next] = d + 1;
                        farthest = Math.Max(farthest, d + 1);
                        frontier.Enqueue(next);
                    }
                }
            }

            if (distance.Count == all.Count)
            {
                return distance;
            }

            start = all
                .Where(c => !distance.ContainsKey(c))
                .OrderBy(c => c.Y)
                .ThenBy(c => Hash64.Combine(seed, c.X, c.Y, c.Z))
                .First();
        }
    }
}
