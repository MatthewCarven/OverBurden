namespace Overburden.Core.Structure;

/// <summary>
/// Shapes of face-connected unit cubes — the 3D Tetris pieces a collapse falls in (COLLAPSE-PLAN
/// decisions 1 and 7): the 24 rotations of a shape, a canonical form that names a shape whatever
/// way round it is, and the free tetracubes enumerated rather than written out.
/// </summary>
/// <remarks>
/// <b>Any four face-connected cubes are a tetracube</b>, so the cutter never consults a shape
/// table: it grows a connected set of up to four minos and the Tetris shape is what that is. The
/// table exists for the solver's orientations and for the test that says so. There are eight free
/// tetracubes under rotation (the five flat tetrominoes and three twisted ones, the two screws being
/// mirror images), and 86 fixed ones — both asserted, which pins the rotation group as well.
/// </remarks>
public static class Polycubes
{
    /// <summary>The 24 proper rotations of the cube, as (axis permutation, signs).</summary>
    public static IReadOnlyList<Func<(int X, int Y, int Z), (int X, int Y, int Z)>> Rotations { get; } = BuildRotations();

    /// <summary>The free tetracubes, one representative each in canonical form.</summary>
    public static IReadOnlyList<(int X, int Y, int Z)[]> FreeTetracubes { get; } = Enumerate(4);

    /// <summary>The distinct orientations of a shape, each translated so its minimum corner is the origin and sorted.</summary>
    public static List<(int X, int Y, int Z)[]> Orientations(IReadOnlyList<(int X, int Y, int Z)> cells)
    {
        var seen = new HashSet<string>();
        var result = new List<(int X, int Y, int Z)[]>();
        foreach (Func<(int X, int Y, int Z), (int X, int Y, int Z)> rotate in Rotations)
        {
            (int X, int Y, int Z)[] rotated = Normalise(cells.Select(rotate));
            if (seen.Add(Key(rotated)))
            {
                result.Add(rotated);
            }
        }

        return result;
    }

    /// <summary>A name for the shape that every rotation and translation of it shares.</summary>
    public static string Canonical(IReadOnlyList<(int X, int Y, int Z)> cells)
    {
        string? best = null;
        foreach (Func<(int X, int Y, int Z), (int X, int Y, int Z)> rotate in Rotations)
        {
            string key = Key(Normalise(cells.Select(rotate)));
            if (best is null || string.CompareOrdinal(key, best) < 0)
            {
                best = key;
            }
        }

        return best ?? string.Empty;
    }

    /// <summary>Whether the cells are face-connected (an empty set is not).</summary>
    public static bool IsConnected(IReadOnlyList<(int X, int Y, int Z)> cells)
    {
        if (cells.Count == 0)
        {
            return false;
        }

        var set = new HashSet<(int X, int Y, int Z)>(cells);
        var reached = new HashSet<(int X, int Y, int Z)> { cells[0] };
        var frontier = new Stack<(int X, int Y, int Z)>();
        frontier.Push(cells[0]);
        while (frontier.Count > 0)
        {
            (int x, int y, int z) = frontier.Pop();
            foreach ((int X, int Y, int Z) next in Neighbours(x, y, z))
            {
                if (set.Contains(next) && reached.Add(next))
                {
                    frontier.Push(next);
                }
            }
        }

        return reached.Count == set.Count;
    }

    /// <summary>The six face neighbours of a cell.</summary>
    public static IEnumerable<(int X, int Y, int Z)> Neighbours(int x, int y, int z)
    {
        yield return (x + 1, y, z);
        yield return (x - 1, y, z);
        yield return (x, y + 1, z);
        yield return (x, y - 1, z);
        yield return (x, y, z + 1);
        yield return (x, y, z - 1);
    }

    /// <summary>Translated so the minimum corner is the origin, and sorted — a fixed shape's name.</summary>
    public static (int X, int Y, int Z)[] Normalise(IEnumerable<(int X, int Y, int Z)> cells)
    {
        (int X, int Y, int Z)[] array = cells.ToArray();
        if (array.Length == 0)
        {
            return array;
        }

        int minX = array.Min(c => c.X);
        int minY = array.Min(c => c.Y);
        int minZ = array.Min(c => c.Z);
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = (array[i].X - minX, array[i].Y - minY, array[i].Z - minZ);
        }

        Array.Sort(array);
        return array;
    }

    private static string Key((int X, int Y, int Z)[] normalised) =>
        string.Join(';', normalised.Select(c => $"{c.X},{c.Y},{c.Z}"));

    private static List<(int X, int Y, int Z)[]> Enumerate(int size)
    {
        var shapes = new Dictionary<string, (int X, int Y, int Z)[]> { [Canonical([(0, 0, 0)])] = [(0, 0, 0)] };
        for (int n = 1; n < size; n++)
        {
            var grown = new Dictionary<string, (int X, int Y, int Z)[]>();
            foreach ((int X, int Y, int Z)[] shape in shapes.Values)
            {
                var set = new HashSet<(int X, int Y, int Z)>(shape);
                foreach ((int X, int Y, int Z) cell in shape)
                {
                    foreach ((int X, int Y, int Z) next in Neighbours(cell.X, cell.Y, cell.Z))
                    {
                        if (set.Contains(next))
                        {
                            continue;
                        }

                        (int X, int Y, int Z)[] bigger = [.. shape, next];
                        string key = Canonical(bigger);
                        if (!grown.ContainsKey(key))
                        {
                            grown[key] = Normalise(bigger);
                        }
                    }
                }
            }

            shapes = grown;
        }

        return [.. shapes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value)];
    }

    private static List<Func<(int X, int Y, int Z), (int X, int Y, int Z)>> BuildRotations()
    {
        var rotations = new List<Func<(int X, int Y, int Z), (int X, int Y, int Z)>>();
        int[][] permutations = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        int[] permutationParity = [1, -1, -1, 1, 1, -1];
        for (int p = 0; p < permutations.Length; p++)
        {
            for (int signs = 0; signs < 8; signs++)
            {
                int sx = (signs & 1) != 0 ? -1 : 1;
                int sy = (signs & 2) != 0 ? -1 : 1;
                int sz = (signs & 4) != 0 ? -1 : 1;
                if (permutationParity[p] * sx * sy * sz != 1)
                {
                    continue;
                }

                int[] perm = permutations[p];
                rotations.Add(c =>
                {
                    int[] v = [c.X, c.Y, c.Z];
                    return (sx * v[perm[0]], sy * v[perm[1]], sz * v[perm[2]]);
                });
            }
        }

        return rotations;
    }
}
