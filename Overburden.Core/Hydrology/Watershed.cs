namespace Overburden.Core.Hydrology;

/// <summary>
/// Finds the depressions in a <see cref="HeightField"/>: where water collects, how much each holds,
/// and where it goes when it overflows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Priority-flood</b> (Barnes, Lehman &amp; Mulla 2014), which fills depressions and labels
/// watersheds in one sweep. Flooding inward from the window edge gives every sample its
/// <i>filled</i> height — the level at which it becomes connected to the outside — and a sample
/// whose filled height exceeds its own is under water when the world is drowned to that level.
/// </para>
/// <para>
/// <b>The window edge is an outlet, not a wall</b>, so a basin larger than the window fills only to
/// the window's rim. Both answers are wrong for such a basin and this is the wrong one to prefer:
/// under-filling means a lake that should exist does not, while walling the edge means water
/// arriving where the player built, for a reason nothing on screen can explain. It also keeps
/// <c>rain in == stored + spilled</c> an exact assertion rather than an inequality.
/// </para>
/// <para>
/// <b>Two passes, not one, and the reason is a trap worth recording.</b> The flood's own pop order
/// is ascending in filled height, which looks like enough to label as it goes — but a chain of
/// cells descending into a depression is popped from the rim <i>inward</i>, so the first cell of
/// the chain is popped while the deeper ones are not yet in the heap. Labelling during the flood
/// therefore declares the first cell of the corridor a pit, and the real pit never becomes one.
/// The sweep has to run over a <b>global</b> sort so that every lower cell is already placed.
/// </para>
/// </remarks>
public sealed class Watershed
{
    /// <summary>Water here leaves the window. Not a basin.</summary>
    public const int Outlet = -1;

    private const int Unassigned = int.MinValue;

    /// <summary>Low bits of the packed key holding the sample index. See <see cref="Key"/>.</summary>
    private const int IndexBits = 21;

    /// <summary>Shifts the packed sort key's fields into the low 63 bits. See <see cref="Key"/>.</summary>
    private const int HeightBias = HeightField.MaxAbsoluteHeight;

    private readonly int[] _labels;
    private readonly int[] _filled;

    private Watershed(HeightField field, int[] labels, int[] filled, Basin[] basins)
    {
        Field = field;
        _labels = labels;
        _filled = filled;
        Basins = basins;
    }

    public HeightField Field { get; }

    /// <summary>Every depression in the window, in the order their pits were found.</summary>
    public IReadOnlyList<Basin> Basins { get; }

    /// <summary>Per sample: a basin index, or <see cref="Outlet"/>. Row-major.</summary>
    public ReadOnlySpan<int> Labels => _labels;

    /// <summary>
    /// Per sample: the level at which it connects to the window edge. Equals the sample's own
    /// height outside a depression and exceeds it inside one.
    /// </summary>
    public ReadOnlySpan<int> FilledHeights => _filled;

    public int LabelAt(int sx, int sz) => _labels[(sz * Field.Pitch) + sx];

    public int FilledAt(int sx, int sz) => _filled[(sz * Field.Pitch) + sx];

    /// <summary>The basin a sample drains into, or null when its water leaves the window.</summary>
    public Basin? BasinAt(int sx, int sz)
    {
        int label = LabelAt(sx, sz);
        return label == Outlet ? null : Basins[label];
    }

    public static Watershed Analyse(HeightField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        int pitch = field.Pitch;
        int count = field.SampleCount;
        ReadOnlySpan<int> heights = field.Heights;

        int[] filled = Flood(field);
        int[] labels = Label(field, filled, out List<int> pits);

        var basins = new Basin[pits.Count];
        if (pits.Count > 0)
        {
            int[] saddle = new int[pits.Count];
            int[] spillTo = new int[pits.Count];
            long[] saddleTie = new long[pits.Count];
            Array.Fill(saddle, int.MaxValue);
            Array.Fill(spillTo, Outlet);
            Array.Fill(saddleTie, long.MaxValue);

            FindSaddles(field, labels, saddle, spillTo, saddleTie);

            int[] catchment = new int[pits.Count];
            int[] lakeCells = new int[pits.Count];
            for (int i = 0; i < count; i++)
            {
                int label = labels[i];
                if (label == Outlet)
                {
                    continue;
                }

                catchment[label]++;
                if (heights[i] < saddle[label])
                {
                    lakeCells[label]++;
                }
            }

            var scratch = new int[pits.Count][];
            var written = new int[pits.Count];
            for (int b = 0; b < pits.Count; b++)
            {
                scratch[b] = new int[lakeCells[b]];
            }

            for (int i = 0; i < count; i++)
            {
                int label = labels[i];
                if (label != Outlet && heights[i] < saddle[label])
                {
                    scratch[label][written[label]++] = heights[i];
                }
            }

            for (int b = 0; b < pits.Count; b++)
            {
                int pit = pits[b];
                var curve = VolumeCurve.Build(scratch[b], saddle[b], field.LitresPerVoxelOfDepth);
                basins[b] = new Basin(
                    b,
                    field.WorldXOf(pit % pitch),
                    field.WorldZOf(pit / pitch),
                    heights[pit],
                    saddle[b],
                    spillTo[b],
                    catchment[b],
                    field.SquareMetresPerCell,
                    curve);
            }
        }

        return new Watershed(field, labels, filled, basins);
    }

    /// <summary>The filled height of every sample, by priority-flood inward from the edge.</summary>
    private static int[] Flood(HeightField field)
    {
        int pitch = field.Pitch;
        int count = field.SampleCount;
        ReadOnlySpan<int> heights = field.Heights;

        var filled = new int[count];
        var queued = new bool[count];
        var heap = new PriorityQueue<int, long>(pitch * 4);

        for (int sz = 0; sz < pitch; sz++)
        {
            for (int sx = 0; sx < pitch; sx++)
            {
                if (sx != 0 && sz != 0 && sx != pitch - 1 && sz != pitch - 1)
                {
                    continue;
                }

                int index = (sz * pitch) + sx;
                filled[index] = heights[index];
                queued[index] = true;
                heap.Enqueue(index, Key(heights[index], heights[index], index));
            }
        }

        while (heap.TryDequeue(out int current, out _))
        {
            int cx = current % pitch;
            int cz = current / pitch;

            for (int direction = 0; direction < 4; direction++)
            {
                if (!Neighbour(cx, cz, direction, pitch, out int neighbour))
                {
                    continue;
                }

                if (queued[neighbour])
                {
                    continue;
                }

                // A sample's filled height is settled the first time the flood reaches it, because
                // the heap hands out cells in ascending order of the level that reached them —
                // there is no cheaper route left to find.
                filled[neighbour] = Math.Max(heights[neighbour], filled[current]);
                queued[neighbour] = true;
                heap.Enqueue(neighbour, Key(filled[neighbour], heights[neighbour], neighbour));
            }
        }

        return filled;
    }

    /// <summary>Assigns every sample the basin it drains into, sweeping in ascending key order.</summary>
    private static int[] Label(HeightField field, int[] filled, out List<int> pits)
    {
        int pitch = field.Pitch;
        int count = field.SampleCount;
        ReadOnlySpan<int> heights = field.Heights;

        var keys = new long[count];
        var order = new int[count];
        for (int i = 0; i < count; i++)
        {
            keys[i] = Key(filled[i], heights[i], i);
            order[i] = i;
        }

        Array.Sort(keys, order);

        var labels = new int[count];
        Array.Fill(labels, Unassigned);
        pits = [];
        var flat = new Queue<int>();

        // Swept a TIER at a time — one run of equal (filled, height) — rather than a cell at a
        // time, and that is what makes flat ground work. Voxel heights are integers sampled every
        // few metres, so equal neighbours are ordinary rather than exotic; and a cell in the middle of a
        // flat has no strictly lower neighbour at all, so a per-cell sweep declares it a pit and
        // the terrain fills with basins that hold nothing. Instead the tier is seeded from
        // whatever lower ground already touches it and the label is spread across the flat, which
        // is Barnes' flat resolution in the one place this algorithm needs it.
        int start = 0;
        while (start < count)
        {
            long tier = keys[start] >> IndexBits;
            int end = start;
            while (end < count && keys[end] >> IndexBits == tier)
            {
                end++;
            }

            for (int k = start; k < end; k++)
            {
                int current = order[k];
                int cx = current % pitch;
                int cz = current / pitch;

                if (cx == 0 || cz == 0 || cx == pitch - 1 || cz == pitch - 1)
                {
                    labels[current] = Outlet;
                    flat.Enqueue(current);
                    continue;
                }

                // Only a lower neighbour says which way water leaves. The `key < own` half of that
                // is BELT AND BRACES and the break-recipe battery says so: every neighbour that is
                // labelled at this point already has a lower key, because the sweep runs in key
                // order and the seeding pass walks a tier in index order. Kept because it states
                // the rule the code relies on rather than the accident that satisfies it — the
                // day this pass is tiled or parallelised, the accident goes and the rule stays.
                long own = keys[k];
                long bestKey = long.MaxValue;
                int bestLabel = Unassigned;
                for (int direction = 0; direction < 4; direction++)
                {
                    if (!Neighbour(cx, cz, direction, pitch, out int neighbour)
                        || labels[neighbour] == Unassigned)
                    {
                        continue;
                    }

                    long key = Key(filled[neighbour], heights[neighbour], neighbour);
                    if (key < own && key < bestKey)
                    {
                        bestKey = key;
                        bestLabel = labels[neighbour];
                    }
                }

                if (bestLabel != Unassigned)
                {
                    labels[current] = bestLabel;
                    flat.Enqueue(current);
                }
            }

            Spread(labels, flat, filled, heights, pitch, tier);

            // Anything the spread could not reach has no downhill exit anywhere in its tier, which
            // is what a pit is — one per flat, taken in index order so the choice is a total order
            // on the world coordinate rather than an artefact of the queue.
            for (int k = start; k < end; k++)
            {
                int current = order[k];
                if (labels[current] != Unassigned)
                {
                    continue;
                }

                labels[current] = pits.Count;
                pits.Add(current);
                flat.Enqueue(current);
                Spread(labels, flat, filled, heights, pitch, tier);
            }

            start = end;
        }

        return labels;
    }

    /// <summary>Spreads labels across one tier of equal (filled, height) samples.</summary>
    private static void Spread(
        int[] labels,
        Queue<int> frontier,
        int[] filled,
        ReadOnlySpan<int> heights,
        int pitch,
        long tier)
    {
        while (frontier.TryDequeue(out int current))
        {
            int cx = current % pitch;
            int cz = current / pitch;

            for (int direction = 0; direction < 4; direction++)
            {
                if (!Neighbour(cx, cz, direction, pitch, out int neighbour)
                    || labels[neighbour] != Unassigned
                    || Key(filled[neighbour], heights[neighbour], neighbour) >> IndexBits != tier)
                {
                    continue;
                }

                labels[neighbour] = labels[current];
                frontier.Enqueue(neighbour);
            }
        }
    }

    /// <summary>
    /// The lowest crossing between each basin and anything else, which is where it overflows.
    /// </summary>
    /// <remarks>
    /// A crossing between adjacent samples costs the higher of the two, because that is the level
    /// water has to reach to get over it. The lowest crossing out of a basin is its saddle, and
    /// what lies on the far side is where the overflow goes.
    /// </remarks>
    private static void FindSaddles(
        HeightField field, int[] labels, int[] saddle, int[] spillTo, long[] saddleTie)
    {
        int pitch = field.Pitch;
        ReadOnlySpan<int> heights = field.Heights;

        for (int index = 0; index < labels.Length; index++)
        {
            int label = labels[index];
            if (label == Outlet)
            {
                continue;
            }

            int cx = index % pitch;
            int cz = index / pitch;

            for (int direction = 0; direction < 4; direction++)
            {
                if (!Neighbour(cx, cz, direction, pitch, out int neighbour))
                {
                    continue;
                }

                int other = labels[neighbour];
                if (other == label)
                {
                    continue;
                }

                int crossing = Math.Max(heights[index], heights[neighbour]);

                // Two crossings of equal height are separated by the pair's lower sample index,
                // which is a total order on the world coordinate: row-major (z, x) survives
                // translation, so a window move cannot change the answer.
                //
                // MEASURED BELT AND BRACES. Replacing all of this with `crossing <= saddle` leaves
                // the suite green, because the scan is already row-major and therefore already
                // translation-stable — it would simply keep the last equal crossing instead of the
                // first. The tie-break is kept because it makes the property a rule rather than a
                // consequence of the loop's shape, and the day this scan is tiled or run in
                // parallel it becomes the only thing holding the property up.
                long tie = Math.Min(index, neighbour);
                if (crossing < saddle[label] || (crossing == saddle[label] && tie < saddleTie[label]))
                {
                    saddle[label] = crossing;
                    spillTo[label] = other;
                    saddleTie[label] = tie;
                }
            }
        }
    }

    private static bool Neighbour(int sx, int sz, int direction, int pitch, out int index)
    {
        int nx = sx + direction switch { 0 => -1, 1 => 1, _ => 0 };
        int nz = sz + direction switch { 2 => -1, 3 => 1, _ => 0 };

        if (nx < 0 || nz < 0 || nx >= pitch || nz >= pitch)
        {
            index = -1;
            return false;
        }

        index = (nz * pitch) + nx;
        return true;
    }

    /// <summary>
    /// Packs (filled, height, index) into one non-negative 63-bit key, so the sweep can sort with
    /// <see cref="Array.Sort(long[], int[])"/> rather than a comparison delegate.
    /// </summary>
    /// <remarks>
    /// The three fields are 21 bits each, which is what <see cref="HeightField.MaxAbsoluteHeight"/>
    /// and <see cref="HeightField.MaxSamples"/> are checked against at sample time — a wrapped key
    /// would produce a plausible watershed of the wrong terrain, which is the kind of wrong that
    /// never announces itself.
    /// </remarks>
    private static long Key(int filled, int height, int index)
        => ((long)(filled + HeightBias) << 42)
            | ((long)(height + HeightBias) << 21)
            | (uint)index;
}
