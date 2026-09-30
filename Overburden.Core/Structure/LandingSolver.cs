using Overburden.Core.Content;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Structure;

/// <summary>Where a released piece comes to rest, and the steps it takes to get there.</summary>
/// <param name="Orientation">The index into the piece's distinct orientations (0 is as it was).</param>
/// <param name="Shape">The oriented piece's voxels, as offsets from its anchor, the minimum corner at the origin.</param>
/// <param name="Start">The anchor at release.</param>
/// <param name="Rest">The anchor where it rests.</param>
/// <param name="Path">The anchor after every tick, from <see cref="Start"/> to <see cref="Rest"/>.</param>
/// <param name="Candidates">How many placements the search simulated.</param>
public sealed record LandingPlan(
    int Orientation,
    (int X, int Y, int Z)[] Shape,
    VoxelPos Start,
    VoxelPos Rest,
    IReadOnlyList<VoxelPos> Path,
    int Candidates)
{
    /// <summary>
    /// Where each of the piece's voxels sits in <see cref="Shape"/>, in the piece's own order — the
    /// rotation at release, voxel for voxel (COLLAPSE-PLAN part c, fact 3), so a piece can be drawn
    /// in its own materials. Empty for a plan made without a piece to map.
    /// </summary>
    public (int X, int Y, int Z)[] VoxelCells { get; init; } = [];

    /// <summary>Ticks from release to rest: a tick is one mino down, and sideways one mino while steering.</summary>
    public int Steps => Path.Count - 1;

    /// <summary>The voxels the piece occupies at rest — what becomes rubble.</summary>
    public IEnumerable<VoxelPos> RestVoxels => Shape.Select(s => Rest.Offset(s.X, s.Y, s.Z));
}

/// <summary>One placement the search tried: an orientation, a steer in minos, and where it came to rest.</summary>
public readonly record struct LandingCandidate(int Orientation, int SteerX, int SteerZ, int RestBottomY, long SumY, bool Descends);

/// <summary>
/// The Tetris solver (COLLAPSE-PLAN decision 7): for a released piece, every orientation and every
/// steer within the spread, each simulated the way it would fall — sideways one mino a tick toward
/// its target while that is clear, down one mino a tick, the last step voxel-exact — and the
/// lowest resting place taken; ties go to the one farthest from the event's centre (debris
/// spreads away from what still stands), then to a seeded draw.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a descending placement counts.</b> A placement is a candidate when the sum of the
/// piece's voxel heights at rest is strictly below the sum where it was cut. Rotation at release
/// can lift half a piece (an I stood on its end), and a piece that could rest higher than it
/// started could be cut and dropped forever; with the rule, every landing strictly lowers Σ(Y)
/// over the world's solid voxels — M9c-1's termination proof, kept for pieces and for the rubble
/// that tumbles again. When nothing descends (another piece's reserved landing is under it), the
/// solver answers null and the piece waits.
/// </para>
/// <para>
/// <b>What is solid</b>: the world (an unloaded voxel is solid, M4a's rule), and the reserved
/// voxels where pieces in flight will rest; the piece's own voxels are not, whether or not the
/// caller has written them to air yet, so the solver reads the same world either side of the
/// release. Pieces in flight are not solid to each other — they are pictures (decision 6).
/// </para>
/// </remarks>
public static class LandingSolver
{
    /// <summary>The spread's default, metres: how far sideways a piece may steer.</summary>
    public const float DefaultSpreadMetres = 2f;

    /// <summary>A bound on the ticks one simulated fall may take; the loaded world ends long before.</summary>
    public const int MaxTicks = 4096;

    /// <summary>The piece's landing, or null when no placement descends.</summary>
    /// <param name="piece">The piece.</param>
    /// <param name="world">The world, read.</param>
    /// <param name="materials">What is solid.</param>
    /// <param name="reserved">Where pieces in flight will rest, solid to this one; null for none.</param>
    /// <param name="centre">The event's centre; ties go to the landing farthest from it.</param>
    /// <param name="spreadMetres">How far a piece may steer, each way, in metres.</param>
    /// <param name="seed">The event's seed; the last tie breaks on it.</param>
    /// <param name="seen">When given, every placement simulated is added — the oracle's input.</param>
    public static LandingPlan? Solve(
        CollapsePiece piece,
        IVoxelWorld world,
        MaterialTable materials,
        IReadOnlySet<VoxelPos>? reserved,
        VoxelPos centre,
        float spreadMetres,
        ulong seed,
        List<LandingCandidate>? seen = null) =>
        Solve(piece, world, materials, reserved, centre, spreadMetres, seed, seen, wholeShape: false);

    /// <summary>
    /// The solve, with <paramref name="wholeShape"/> checking every voxel of the shape at every
    /// voxel of every move rather than only the face that leads it — the arm the faces are held
    /// equal to, cell for cell, by a test.
    /// </summary>
    internal static LandingPlan? Solve(
        CollapsePiece piece,
        IVoxelWorld world,
        MaterialTable materials,
        IReadOnlySet<VoxelPos>? reserved,
        VoxelPos centre,
        float spreadMetres,
        ulong seed,
        List<LandingCandidate>? seen,
        bool wholeShape)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);
        if (!(spreadMetres >= 0 && float.IsFinite(spreadMetres)))
        {
            throw new ArgumentOutOfRangeException(nameof(spreadMetres), spreadMetres, "The spread is finite and non-negative.");
        }

        if (piece.Voxels.Length == 0)
        {
            return null;
        }

        var own = new HashSet<VoxelPos>(piece.Voxels);
        var probe = new Probe(world, materials, reserved, own);

        int minX = piece.Voxels.Min(v => v.X);
        int minY = piece.Voxels.Min(v => v.Y);
        int minZ = piece.Voxels.Min(v => v.Z);
        var anchor = new VoxelPos(minX, minY, minZ);
        (int X, int Y, int Z)[] local = piece.Voxels.Select(v => (v.X - minX, v.Y - minY, v.Z - minZ)).ToArray();
        long startSumY = piece.Voxels.Sum(v => (long)v.Y);
        List<(int X, int Y, int Z)[]> orientations = Polycubes.Orientations(local);

        // Orientation 0 is the piece as it was: Orientations normalises and sorts, and so is local
        // once sorted; put the unrotated one first so "0 is as it was" holds for the caller.
        (int X, int Y, int Z)[] asItWas = Polycubes.Normalise(local);
        int identity = orientations.FindIndex(o => o.AsSpan().SequenceEqual(asItWas));
        if (identity > 0)
        {
            (orientations[0], orientations[identity]) = (orientations[identity], orientations[0]);
        }

        int mino = piece.MinoVoxels;
        int reach = (int)MathF.Floor(spreadMetres * VoxelConstants.VoxelsPerMetre / mino);
        int n = piece.Voxels.Length;

        LandingPlan? best = null;
        (int BottomY, long NegFar, ulong Tie) bestScore = default;
        int candidates = 0;
        var path = new List<VoxelPos>();
        for (int o = 0; o < orientations.Count; o++)
        {
            (int X, int Y, int Z)[] shape = orientations[o];
            if (!probe.Clear(shape, anchor))
            {
                continue;
            }

            Faces faces = wholeShape ? Faces.Whole(shape) : Faces.Of(shape);

            for (int sx = -reach; sx <= reach; sx++)
            {
                for (int sz = -reach; sz <= reach; sz++)
                {
                    candidates++;
                    path.Clear();
                    VoxelPos rest = Fall(probe, faces, anchor, sx * mino, sz * mino, mino, path);
                    long sumY = 0;
                    long sumX = 0;
                    long sumZ = 0;
                    foreach ((int X, int Y, int Z) s in shape)
                    {
                        sumY += rest.Y + s.Y;
                        sumX += rest.X + s.X;
                        sumZ += rest.Z + s.Z;
                    }

                    bool descends = sumY < startSumY;
                    seen?.Add(new LandingCandidate(o, sx, sz, rest.Y, sumY, descends));
                    if (!descends)
                    {
                        continue;
                    }

                    long dx = sumX - ((long)centre.X * n);
                    long dz = sumZ - ((long)centre.Z * n);
                    (int, long, ulong) score = (rest.Y, -((dx * dx) + (dz * dz)), Hash64.Combine(seed ^ (ulong)o, sx, piece.Order, sz));
                    if (best is null || score.CompareTo(bestScore) < 0)
                    {
                        best = new LandingPlan(o, shape, anchor, rest, [.. path], 0);
                        bestScore = score;
                    }
                }
            }
        }

        return best is null ? null : best with { Candidates = candidates, VoxelCells = MapVoxels(local, best.Shape) };
    }

    /// <summary>
    /// The rotation that turns the piece's own cells into <paramref name="shape"/>, applied voxel by
    /// voxel: the orientations are the 24 rotations deduplicated, so one of them made it.
    /// </summary>
    private static (int X, int Y, int Z)[] MapVoxels((int X, int Y, int Z)[] local, (int X, int Y, int Z)[] shape)
    {
        var target = new HashSet<(int X, int Y, int Z)>(shape);
        var cells = new (int X, int Y, int Z)[local.Length];
        foreach (Func<(int X, int Y, int Z), (int X, int Y, int Z)> rotate in Polycubes.Rotations)
        {
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            for (int i = 0; i < local.Length; i++)
            {
                cells[i] = rotate(local[i]);
                minX = Math.Min(minX, cells[i].X);
                minY = Math.Min(minY, cells[i].Y);
                minZ = Math.Min(minZ, cells[i].Z);
            }

            bool match = true;
            for (int i = 0; i < local.Length && match; i++)
            {
                cells[i] = (cells[i].X - minX, cells[i].Y - minY, cells[i].Z - minZ);
                match = target.Contains(cells[i]);
            }

            if (match)
            {
                return cells;
            }
        }

        throw new InvalidOperationException("No rotation of the piece makes the shape its plan rests in.");
    }

    /// <summary>
    /// One simulated fall: each tick, sideways one mino toward the steer while the whole move is
    /// clear (a blocked move gives up the steer), then down one mino — or as far as is clear, and
    /// rest. Records the anchor after every tick.
    /// </summary>
    /// <remarks>
    /// <b>Only the leading face is checked</b>, a voxel at a time: the shape was clear where it
    /// stood, and a one-voxel move newly occupies exactly its face in that direction — the voxels
    /// whose neighbour that way is not in the shape. For a 1 m mino tetracube that is 64 voxels of
    /// 256 going down. Held equal to the whole-shape check by a test.
    /// </remarks>
    private static VoxelPos Fall(Probe probe, Faces faces, VoxelPos start, int steerX, int steerZ, int mino, List<VoxelPos> path)
    {
        VoxelPos at = start;
        path.Add(at);
        int leftX = steerX;
        int leftZ = steerZ;
        for (int tick = 0; tick < MaxTicks; tick++)
        {
            bool moved = false;
            if (leftX != 0 || leftZ != 0)
            {
                int dx = leftX != 0 ? Math.Sign(leftX) : 0;
                int dz = leftX == 0 ? Math.Sign(leftZ) : 0;
                if (probe.Swept(faces.Toward(dx, dz), at, dx, 0, dz, mino))
                {
                    at = at.Offset(dx * mino, 0, dz * mino);
                    leftX -= dx * mino;
                    leftZ -= dz * mino;
                    moved = true;
                }
                else
                {
                    leftX = 0;
                    leftZ = 0;
                }
            }

            if (leftX == 0 && leftZ == 0 && !faces.IsWhole)
            {
                // Steering is over: the rest is straight down, one jump off the columns' memo,
                // the path filled in a mino a tick exactly as the ticks would have written it.
                int drop = int.MaxValue;
                foreach ((int X, int Y, int Z) f in faces.Down)
                {
                    drop = Math.Min(drop, probe.FreeBelow(at.Offset(f.X, f.Y, f.Z), MaxTicks * mino));
                }

                if (drop == 0 && moved)
                {
                    path.Add(at);
                }

                for (int fallen = mino; fallen <= drop; fallen += mino)
                {
                    path.Add(at.Offset(0, -fallen, 0));
                }

                if (drop % mino != 0)
                {
                    path.Add(at.Offset(0, -drop, 0));
                }

                return at.Offset(0, -drop, 0);
            }

            int down = 0;
            while (down < mino && probe.Clear(faces.Down, at.Offset(0, -(down + 1), 0)))
            {
                down++;
            }

            if (down > 0)
            {
                at = at.Offset(0, -down, 0);
                moved = true;
            }

            if (moved)
            {
                path.Add(at);
            }

            if (down < mino)
            {
                return at;
            }
        }

        return at;
    }

    /// <summary>An oriented shape's leading faces: the voxels whose neighbour that way is not in the shape.</summary>
    private sealed class Faces
    {
        private Faces((int X, int Y, int Z)[] down, (int X, int Y, int Z)[] east, (int X, int Y, int Z)[] west, (int X, int Y, int Z)[] south, (int X, int Y, int Z)[] north)
        {
            Down = down;
            East = east;
            West = west;
            South = south;
            North = north;
        }

        public (int X, int Y, int Z)[] Down { get; }

        public (int X, int Y, int Z)[] East { get; }

        public (int X, int Y, int Z)[] West { get; }

        public (int X, int Y, int Z)[] South { get; }

        public (int X, int Y, int Z)[] North { get; }

        public (int X, int Y, int Z)[] Toward(int dx, int dz) => dx > 0 ? East : dx < 0 ? West : dz > 0 ? South : North;

        public static Faces Of((int X, int Y, int Z)[] shape)
        {
            var set = new HashSet<(int X, int Y, int Z)>(shape);
            (int X, int Y, int Z)[] Face(int dx, int dy, int dz) =>
                shape.Where(v => !set.Contains((v.X + dx, v.Y + dy, v.Z + dz))).ToArray();
            return new Faces(Face(0, -1, 0), Face(1, 0, 0), Face(-1, 0, 0), Face(0, 0, 1), Face(0, 0, -1));
        }

        public static Faces Whole((int X, int Y, int Z)[] shape) => new(shape, shape, shape, shape, shape) { IsWhole = true };

        /// <summary>The whole-shape arm: every check over every voxel, a tick at a time — the reference.</summary>
        public bool IsWhole { get; private init; }
    }

    /// <summary>The world as the solver sees it: solid, reserved, or the piece's own (empty).</summary>
    private sealed class Probe(IVoxelWorld world, MaterialTable materials, IReadOnlySet<VoxelPos>? reserved, HashSet<VoxelPos> own)
    {
        private readonly Dictionary<VoxelPos, int> _freeBelow = [];
        private readonly List<VoxelPos> _walk = [];

        /// <summary>
        /// Free voxels directly under <paramref name="voxel"/> before the first solid one, capped;
        /// memoised for the solve, since every placement of every orientation shares columns.
        /// </summary>
        public int FreeBelow(VoxelPos voxel, int cap)
        {
            _walk.Clear();
            VoxelPos at = voxel;
            int known = -1;
            while (_walk.Count < cap)
            {
                if (_freeBelow.TryGetValue(at, out int memo))
                {
                    known = memo;
                    break;
                }

                VoxelPos under = at.Offset(0, -1, 0);
                if (IsSolid(under))
                {
                    known = 0;
                    _freeBelow[at] = 0;
                    break;
                }

                _walk.Add(at);
                at = under;
            }

            if (known < 0)
            {
                known = 0;   // past the cap: treated as a floor, as an unloaded voxel is
            }

            for (int i = _walk.Count - 1; i >= 0; i--)
            {
                known++;
                _freeBelow[_walk[i]] = known;
            }

            return Math.Min(known, cap);
        }

        public bool IsSolid(VoxelPos voxel)
        {
            if (reserved is not null && reserved.Contains(voxel))
            {
                return true;
            }

            if (own.Contains(voxel))
            {
                return false;
            }

            return !world.TryGetMaterial(voxel, out int material) || materials.IsSolid(material);
        }

        public bool Clear((int X, int Y, int Z)[] shape, VoxelPos at)
        {
            foreach ((int X, int Y, int Z) s in shape)
            {
                if (IsSolid(at.Offset(s.X, s.Y, s.Z)))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether the shape can move <paramref name="distance"/> voxels along a unit direction, clear at every voxel on the way.</summary>
        public bool Swept((int X, int Y, int Z)[] shape, VoxelPos at, int dx, int dy, int dz, int distance)
        {
            for (int k = 1; k <= distance; k++)
            {
                if (!Clear(shape, at.Offset(dx * k, dy * k, dz * k)))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
