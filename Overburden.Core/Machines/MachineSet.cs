using Overburden.Core.Building;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Machines;

/// <summary>
/// Every machine row in the world, in memory, whether or not its chunk is loaded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read whole at open and written whole at every session save</b> (M10-PLAN decision 4): rows
/// are hundreds, not millions, every one of them ticks every boundary, and the disk is a mirror
/// of this set rather than the other way round — the basins' arrangement since M7b-2, for the
/// same reason. The set is keyed by brush cell, because a body is exactly one cell and a cell
/// never straddles a chunk face, and indexed by chunk, because the pipeline's hooks and the
/// reconcile ask "what is in this chunk" and must answer in one lookup 32,768 times a bite.
/// </para>
/// <para>
/// <b>M10a-1 builds the set as rows only</b>: nothing here places, ticks or reconciles. Those are
/// M10a-2's and M10a-3's, and they arrive as methods on this type against the same dictionary.
/// </para>
/// </remarks>
public sealed class MachineSet
{
    private readonly Dictionary<BrushCell, MachineRow> _rows = [];
    private readonly Dictionary<ChunkPos, List<MachineRow>> _byChunk = [];

    /// <summary>How many rows the set holds.</summary>
    public int Count => _rows.Count;

    /// <summary>Every row, in no particular order. The tick sorts what it needs sorted.</summary>
    public IReadOnlyCollection<MachineRow> Rows => _rows.Values;

    /// <summary>Bumped on every add and remove, so a caller can cache a derived order and know when it went stale.</summary>
    public long Version { get; private set; }

    /// <summary>The row whose body fills <paramref name="cell"/>, if any.</summary>
    public bool TryGet(BrushCell cell, out MachineRow row) => _rows.TryGetValue(cell, out row!);

    /// <summary>The rows whose bodies are in <paramref name="chunk"/>; empty for a chunk with none.</summary>
    public IReadOnlyList<MachineRow> RowsIn(ChunkPos chunk) =>
        _byChunk.TryGetValue(chunk, out List<MachineRow>? rows) ? rows : [];

    /// <summary>Whether any row's body is in <paramref name="chunk"/>. The hooks' first question.</summary>
    public bool HasRowsIn(ChunkPos chunk) => _byChunk.ContainsKey(chunk);

    /// <summary>Adds a row. Refuses a second row at the same cell: one cell, one body.</summary>
    public void Add(MachineRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!_rows.TryAdd(row.Cell, row))
        {
            throw new InvalidOperationException($"A machine already stands at {row.Cell}.");
        }

        if (!_byChunk.TryGetValue(row.Chunk, out List<MachineRow>? rows))
        {
            rows = [];
            _byChunk[row.Chunk] = rows;
        }

        rows.Add(row);
        Version++;
    }

    /// <summary>Removes the row at <paramref name="cell"/>. False when there was none.</summary>
    public bool Remove(BrushCell cell)
    {
        if (!_rows.Remove(cell, out MachineRow? row))
        {
            return false;
        }

        List<MachineRow> rows = _byChunk[row.Chunk];
        rows.Remove(row);
        if (rows.Count == 0)
        {
            _byChunk.Remove(row.Chunk);
        }

        Version++;
        return true;
    }

    /// <summary>Every row as stored bytes, in origin order, for the session save.</summary>
    /// <remarks>
    /// Origin order rather than dictionary order, so the same set writes the same table twice —
    /// the inventory's <c>Entries()</c> rule, for the reason M6d-3 gave it a second dependant.
    /// </remarks>
    public List<StoredMachine> Snapshot()
    {
        var rows = new List<StoredMachine>(_rows.Count);
        foreach (MachineRow row in _rows.Values)
        {
            rows.Add(new StoredMachine(row.Origin, row.MaterialId, MachineRowBlob.Write(row)));
        }

        rows.Sort(static (a, b) =>
        {
            int c = a.Origin.X.CompareTo(b.Origin.X);
            if (c != 0) return c;
            c = a.Origin.Y.CompareTo(b.Origin.Y);
            return c != 0 ? c : a.Origin.Z.CompareTo(b.Origin.Z);
        });

        return rows;
    }

    /// <summary>
    /// Replaces the set with the stored rows, checking each port against its material's
    /// declaration where the material declares any.
    /// </summary>
    /// <param name="stored">The machine table, whole.</param>
    /// <param name="portsOf">
    /// The ports a material's machine block declares, or null for a material that is not a known
    /// machine — a hole left by a removed mod, or a material with no machine block — whose rows
    /// are kept verbatim and counted <see cref="RestoreReport.Unknown"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>A row is a caller</b> (the ladder's decision 21): a port the declaration does not name
    /// is dropped, a declared port the row lacks takes its default, and a value outside its bounds
    /// is clamped — each counted, none refused, because refusing a row is refusing the world over
    /// a number the programmer can retype. A blob that cannot be <em>read</em> is refused, by the
    /// policy every blob here follows: a corrupt row silently repaired is a machine somewhere the
    /// player did not put it.
    /// </para>
    /// </remarks>
    public RestoreReport Restore(
        IReadOnlyList<StoredMachine> stored,
        Func<int, IReadOnlyList<PortDefinition>?> portsOf)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(portsOf);

        _rows.Clear();
        _byChunk.Clear();
        Version++;

        int unknown = 0, dropped = 0, defaulted = 0, clamped = 0;

        foreach (StoredMachine entry in stored)
        {
            MachineRow row = MachineRowBlob.Read(entry.Blob, entry.Origin, entry.MaterialId);

            IReadOnlyList<PortDefinition>? declared = portsOf(row.MaterialId);
            if (declared is null)
            {
                unknown++;
            }
            else
            {
                foreach (string name in row.Ports.Keys.ToArray())
                {
                    if (!declared.Any(port => port.Name == name))
                    {
                        row.RemovePort(name);
                        dropped++;
                    }
                }

                foreach (PortDefinition port in declared)
                {
                    if (!row.TryGetPort(port.Name, out long value))
                    {
                        row.SetPortUnchecked(port.Name, port.Default);
                        defaulted++;
                        continue;
                    }

                    long inBounds = port.Clamp(value);
                    if (inBounds != value)
                    {
                        row.SetPortUnchecked(port.Name, inBounds);
                        clamped++;
                    }
                }
            }

            if (_rows.ContainsKey(row.Cell))
            {
                throw new BlobFormatException(
                    $"Machine row at {row.Origin}: a second row stands in the same cell. The table is damaged.");
            }

            Add(row);
        }

        return new RestoreReport(stored.Count, unknown, dropped, defaulted, clamped);
    }
}

/// <summary>What <see cref="MachineSet.Restore"/> found, for the open's line.</summary>
/// <param name="Rows">Rows read.</param>
/// <param name="Unknown">Rows whose material declares no ports this build knows — kept verbatim.</param>
/// <param name="Dropped">Ports no declaration names, removed.</param>
/// <param name="Defaulted">Declared ports the row lacked, supplied.</param>
/// <param name="Clamped">Values outside their bounds, clamped.</param>
public readonly record struct RestoreReport(int Rows, int Unknown, int Dropped, int Defaulted, int Clamped)
{
    public override string ToString() =>
        $"{Rows} machine row(s) read ({Unknown} of unknown kind), {Dropped} port(s) dropped, "
        + $"{Defaulted} defaulted, {Clamped} clamped";
}
