using Overburden.Core.Persistence;

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
