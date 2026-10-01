using Overburden.Core.Persistence;
using Overburden.Core.Diagnostics;

namespace Overburden.Core.Hydrology;

/// <summary>
/// Every basin the world has water in, keyed by the world coordinate of its pit — including the
/// ones nobody is looking at.
/// </summary>
/// <remarks>
/// <para>
/// <b>The watershed window is centred on the player and a <see cref="BasinWater"/> only ever holds
/// what is inside it.</b> Walk a kilometre and the analysis is redone somewhere else; the lake you
/// left has no representation at all until you come back. This is where it lives in the meantime,
/// and it is the reason a basin's identity had to be a world coordinate rather than an array index
/// — see <see cref="StoredBasin"/>.
/// </para>
/// <para>
/// <b>The ledger is the truth and the window is a view of part of it.</b> <see cref="Seed"/> pours
/// the ledger's water into a freshly analysed window; <see cref="Absorb"/> takes it back, replacing
/// only the pits that window contains and leaving everywhere else untouched. Getting that
/// asymmetry backwards — absorbing as an <i>addition</i> — would double a lake every time the
/// player crossed the same ground twice.
/// </para>
/// <para>
/// <b>Entries come out sorted, so the same ledger writes the same rows.</b> M6c-2 made that
/// argument for the inventory and it applies unchanged: a shuffled save is a diff nobody can read
/// and a round-trip test that passes for the wrong reason.
/// </para>
/// </remarks>
public sealed class BasinLedger
{
    private readonly Dictionary<(int X, int Z), long> _litres = [];

    /// <summary>Basins holding water anywhere in the world.</summary>
    public int Count => _litres.Count;

    /// <summary>Litres held anywhere in the world, in view or not.</summary>
    public long TotalLitres
    {
        get
        {
            long total = 0;
            foreach (long litres in _litres.Values)
            {
                total += litres;
            }

            return total;
        }
    }

    /// <summary>What one basin holds, by its pit.</summary>
    public long LitresAt(int pitX, int pitZ) =>
        _litres.TryGetValue((pitX, pitZ), out long litres) ? litres : 0;

    /// <summary>
    /// Pours what the ledger holds for this window's basins into <paramref name="water"/>, and
    /// returns what could not be held.
    /// </summary>
    /// <remarks>
    /// The return is normally zero and is worth watching when it is not: it means a basin's stored
    /// water no longer fits the basin, which happens when the window has moved far enough that
    /// part of its catchment now lies outside and its capacity has shrunk. The water leaves rather
    /// than being carried as an invisible debt.
    /// </remarks>
    public long Seed(Watershed watershed, BasinWater water)
    {
        ArgumentNullException.ThrowIfNull(watershed);
        ArgumentNullException.ThrowIfNull(water);

        long spilled = 0;
        for (int index = 0; index < watershed.Basins.Count; index++)
        {
            Basin basin = watershed.Basins[index];
            long litres = LitresAt(basin.PitX, basin.PitZ);
            if (litres > 0)
            {
                spilled += water.Add(index, litres);
            }
        }

        return spilled;
    }

    /// <summary>Takes this window's water back, replacing what the ledger held for those basins.</summary>
    public void Absorb(Watershed watershed, BasinWater water)
    {
        ArgumentNullException.ThrowIfNull(watershed);
        ArgumentNullException.ThrowIfNull(water);

        long[] captured = water.CaptureByBasin();
        for (int index = 0; index < watershed.Basins.Count; index++)
        {
            Basin basin = watershed.Basins[index];
            var key = (basin.PitX, basin.PitZ);

            if (captured[index] > 0)
            {
                _litres[key] = captured[index];
            }
            else
            {
                // A basin that has emptied is absent rather than zero, so the store never carries
                // a row that means "no water here" — which is what every unlisted pit already
                // means.
                _litres.Remove(key);
            }
        }
    }

    private static readonly int WatchShare = CallWatch.Register("raise: share the lakes by level");

    /// <summary>
    /// Replaces what the ledger held for the old window's basins with the old window's water,
    /// shared among the new window's basins by the water that stood over each cell at the old
    /// level (docs/LAKE-EDIT-PLAN.md, decision 2). Rows the old window did not hold are untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>For a re-analysis of the same ground only</b>, where the two windows' samples are the same
    /// cells and differ only by a raise — see <see cref="WaterSystem.Install"/>. A window move stays
    /// by pit (<see cref="Absorb"/> then <see cref="Seed"/>), since the land did not change.
    /// </para>
    /// <para>
    /// <b>Each old lake</b> — the node <see cref="BasinWater.LakeTopOf"/> names for its basins — is
    /// shared in proportion to <c>(old level − old height)</c> over every cell under it, counted to
    /// whichever new basin now holds the cell. So a wall across a full lake leaves each side the
    /// water that stood over it, and the wall's own strip goes to the basin that now holds the
    /// wall's cells, which is decision 3's displacement. <b>The old height, not the new</b>: the
    /// new one would give a raised cell nothing, and the strip's water would be spread over both
    /// sides in proportion instead of landing where the wall drains. For an unchanged lake this
    /// gives back exactly what it held, and for a merge (a wall taken down) everything goes to the
    /// one basin, as by-pit did.
    /// </para>
    /// <para>
    /// <b>Whole litres by largest remainder</b>, so each old lake's total is exact. The shares join
    /// the new pits' rows as additions — a row the old window did not hold may already be waiting
    /// at a new pit — and <see cref="Seed"/> then pours them through <see cref="BasinWater.Add"/>,
    /// so a share that no longer fits fills and spills by the usual rules, counted there. A share
    /// counted to a cell that now drains off the window leaves the world, and is returned as
    /// <see cref="LevelShare.Left"/>.
    /// </para>
    /// <para>
    /// <b>Rows the old window did not hold are left alone, which by-pit did not do.</b> Its
    /// predecessor, <c>Rehome</c>, moved every row inside the window that the new window had no pit
    /// for, so a lake stored by an earlier window whose pit fell on this window's edge ring was
    /// deleted at the first raise, and one whose pit fell inside another basin's catchment was
    /// poured into that basin. Those rows wait for a window that can see them, as a window move
    /// leaves them.
    /// </para>
    /// </remarks>
    public LevelShare ShareByLevel(Watershed old, BasinWater oldWater, Watershed next)
    {
        using CallWatch.Scope callWatch = CallWatch.Time(WatchShare);
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(oldWater);
        ArgumentNullException.ThrowIfNull(next);

        HeightField before = old.Field;
        HeightField after = next.Field;
        if (before.OriginX != after.OriginX || before.OriginZ != after.OriginZ
            || before.Pitch != after.Pitch || before.CellVoxels != after.CellVoxels)
        {
            throw new ArgumentException(
                "Sharing by level reads one cell in both windows, so it is for a re-analysis of the "
                + "same ground only. A window move stays by pit.",
                nameof(next));
        }

        // The old window's rows go; its water comes back below, shared.
        foreach (Basin basin in old.Basins)
        {
            _litres.Remove((basin.PitX, basin.PitZ));
        }

        // Each old basin's lake: the top node, its level, and what it holds. Tops partition the
        // basins (BasinWater.LakeTop), so a lake is found once and every basin in it maps to it.
        int leaves = old.Basins.Count;
        var lakeOf = new int[leaves];
        var lakeByTop = new Dictionary<int, int>();
        var levels = new List<double>();
        var stored = new List<long>();
        var firstBasin = new List<int>();
        for (int basin = 0; basin < leaves; basin++)
        {
            int top = oldWater.LakeTopOf(basin);
            long held = oldWater.StoredIn(top);
            if (held == 0)
            {
                lakeOf[basin] = -1;
                continue;
            }

            if (!lakeByTop.TryGetValue(top, out int lake))
            {
                lake = levels.Count;
                lakeByTop[top] = lake;
                levels.Add(oldWater.LevelAt(basin));
                stored.Add(held);
                firstBasin.Add(basin);
            }

            lakeOf[basin] = lake;
        }

        // The water over each cell at its lake's old level, counted to the new basin holding it.
        var weights = new Dictionary<long, double>();
        double displacedDepth = 0;
        ReadOnlySpan<int> oldHeights = before.Heights;
        ReadOnlySpan<int> newHeights = after.Heights;
        ReadOnlySpan<int> oldLabels = old.Labels;
        ReadOnlySpan<int> newLabels = next.Labels;
        for (int i = 0; i < oldHeights.Length; i++)
        {
            int label = oldLabels[i];
            if (label == Watershed.Outlet || lakeOf[label] < 0)
            {
                continue;
            }

            int lake = lakeOf[label];
            double level = levels[lake];
            int height = oldHeights[i];
            if (height >= level)
            {
                continue;
            }

            long key = ((long)lake << 32) | (uint)newLabels[i];
            weights[key] = weights.GetValueOrDefault(key) + (level - height);

            // What the raise took the room of: the material between the old floor and the old
            // level. A diagnostic only — the share above already sends the whole column's water
            // to the basin holding the cell.
            if (newHeights[i] > height)
            {
                displacedDepth += Math.Min(level, newHeights[i]) - height;
            }
        }

        // Sorted by lake then new basin, so the arithmetic is the same every time — the weights
        // are a dictionary.
        var entries = new List<(int Lake, int Basin, double Weight)>(weights.Count);
        foreach (KeyValuePair<long, double> entry in weights)
        {
            entries.Add(((int)(entry.Key >> 32), (int)(uint)entry.Key, entry.Value));
        }

        entries.Sort(static (a, b) => a.Lake != b.Lake ? a.Lake.CompareTo(b.Lake) : a.Basin.CompareTo(b.Basin));

        var destinations = new int[levels.Count][];
        var shares = new List<(int Basin, long Share, double Fraction)>();
        long total = 0, left = 0;
        int start = 0;
        for (int lake = 0; lake < levels.Count; lake++)
        {
            int end = start;
            double sum = 0;
            while (end < entries.Count && entries[end].Lake == lake)
            {
                sum += entries[end].Weight;
                end++;
            }

            long held = stored[lake];
            total += held;
            shares.Clear();
            if (sum > 0)
            {
                long assigned = 0;
                for (int k = start; k < end; k++)
                {
                    double exact = held * (entries[k].Weight / sum);
                    long share = (long)Math.Floor(exact);
                    shares.Add((entries[k].Basin, share, exact - share));
                    assigned += share;
                }

                Apportion(shares, held - assigned);
            }
            else
            {
                // Unreachable while LevelAt keeps a lake with water strictly above its floor, which
                // VolumeCurve.LevelAt does for any positive volume: the floor cell then weighs
                // something. Kept so a broken invariant moves the lake by its first pit rather
                // than deleting it.
                Basin pit = old.Basins[firstBasin[lake]];
                int sx = (pit.PitX - before.OriginX) / before.CellVoxels;
                int sz = (pit.PitZ - before.OriginZ) / before.CellVoxels;
                shares.Add((next.LabelAt(sx, sz), held, 0));
            }

            var reached = new List<int>(shares.Count);
            foreach ((int basin, long share, _) in shares)
            {
                if (share == 0)
                {
                    continue;
                }

                if (basin == Watershed.Outlet)
                {
                    left += share;
                    continue;
                }

                Basin target = next.Basins[basin];
                var key = (target.PitX, target.PitZ);
                _litres[key] = LitresAt(key.PitX, key.PitZ) + share;
                reached.Add(basin);
            }

            destinations[lake] = [.. reached];
            start = end;
        }

        long displaced = (long)Math.Round(displacedDepth * before.LitresPerVoxelOfDepth, MidpointRounding.AwayFromZero);
        return new LevelShare(levels.Count, total, left, displaced, destinations, [.. stored]);
    }

    /// <summary>
    /// Hands out what the floors left over, one litre at a time, to the largest fractions first
    /// (ties to the lower basin), so a lake's shares add up to exactly what it held.
    /// </summary>
    /// <remarks>
    /// In exact arithmetic the remainder is between zero and one less than the share count. A
    /// floor taken on a rounded product can land one either side, so a negative remainder takes a
    /// litre back from the smallest fractions holding one — the total is the invariant, not which
    /// side a rounding litre falls on.
    /// </remarks>
    private static void Apportion(List<(int Basin, long Share, double Fraction)> shares, long remainder)
    {
        if (remainder == 0)
        {
            return;
        }

        var order = new int[shares.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => shares[a].Fraction != shares[b].Fraction
            ? shares[b].Fraction.CompareTo(shares[a].Fraction)
            : shares[a].Basin.CompareTo(shares[b].Basin));

        while (remainder > 0)
        {
            for (int k = 0; k < order.Length && remainder > 0; k++)
            {
                (int basin, long share, double fraction) = shares[order[k]];
                shares[order[k]] = (basin, share + 1, fraction);
                remainder--;
            }
        }

        while (remainder < 0)
        {
            bool took = false;
            for (int k = order.Length - 1; k >= 0 && remainder < 0; k--)
            {
                (int basin, long share, double fraction) = shares[order[k]];
                if (share > 0)
                {
                    shares[order[k]] = (basin, share - 1, fraction);
                    remainder++;
                    took = true;
                }
            }

            if (!took)
            {
                throw new InvalidOperationException("A lake's shares came to more than it held, with nothing left to take back.");
            }
        }
    }

    /// <summary>Every basin holding water, in a fixed order.</summary>
    public IReadOnlyList<StoredBasin> Entries()
    {
        var entries = new List<StoredBasin>(_litres.Count);
        foreach (KeyValuePair<(int X, int Z), long> entry in _litres)
        {
            entries.Add(new StoredBasin(entry.Key.X, entry.Key.Z, entry.Value));
        }

        entries.Sort(static (left, right) => left.PitX != right.PitX
            ? left.PitX.CompareTo(right.PitX)
            : left.PitZ.CompareTo(right.PitZ));

        return entries;
    }

    /// <summary>
    /// Replaces the ledger with what was stored — unless the lattice has changed underneath it, in
    /// which case the volumes are discarded. Returns whether they were kept.
    /// </summary>
    /// <remarks>
    /// <b>Discard rather than refuse, and rather than reinterpret.</b> A pit is a sample on the
    /// watershed lattice, so a build that changes the lattice renames every basin in the world;
    /// the stored coordinates would then land on whatever pit happened to be nearest, which is
    /// water appearing in the wrong valley with nothing failing anywhere. Refusing to open the
    /// world is worse than losing rain that refills in minutes — the chunks, the inventory and the
    /// player's position are all still exactly right.
    /// </remarks>
    public bool Load(
        IReadOnlyList<StoredBasin> stored, int storedCellMetres, int currentCellMetres)
    {
        ArgumentNullException.ThrowIfNull(stored);

        _litres.Clear();

        if (storedCellMetres != currentCellMetres)
        {
            return false;
        }

        foreach (StoredBasin basin in stored)
        {
            if (basin.Litres > 0)
            {
                _litres[(basin.PitX, basin.PitZ)] = basin.Litres;
            }
        }

        return true;
    }

    public void Clear() => _litres.Clear();
}

/// <summary>What one same-origin re-analysis did with the old window's water (<see cref="BasinLedger.ShareByLevel"/>).</summary>
/// <param name="Lakes">The old window's lakes that held water.</param>
/// <param name="Litres">What they held between them, every litre of it shared.</param>
/// <param name="Left">Litres counted to cells that now drain off the window, which left the world.</param>
/// <param name="Displaced">
/// Litres of the old water whose room a raise took: under each raised cell, the old level or the
/// new top, whichever is lower, less the old floor. A diagnostic; the share already moved them.
/// </param>
/// <param name="Destinations">Per old lake, the new basins that took a share of it.</param>
/// <param name="LakeLitres">Per old lake, what it held.</param>
public sealed record LevelShare(
    int Lakes, long Litres, long Left, long Displaced, int[][] Destinations, long[] LakeLitres);
