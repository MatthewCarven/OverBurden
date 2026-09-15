using Overburden.Core.Building;
using Overburden.Core.Voxels;

namespace Overburden.Core.Machines;

/// <summary>
/// One machine: the brush cell its body fills, what it is made of, which way it faces, its
/// port values, and whatever state its kind keeps between boundaries.
/// </summary>
/// <remarks>
/// <para>
/// <b>The first authored state this world has held</b> (M10-PLAN): a number the player typed, at
/// a place the player chose, that nothing can recompute. A chunk can be re-dug; a channel number
/// cannot be guessed. So the row holds only what the voxel cannot — the body is 64 voxels of the
/// machine's material in the chunk, and everything the mesher, the light, the collider and the
/// support field need is there already (M10-PLAN decision 1). The row is the truth and the body
/// follows it at every arrival (decision 4, as M10a's pass amended it).
/// </para>
/// <para>
/// <b>Ports are held by name, in ordinal order</b>, so the same row always serialises to the same
/// bytes and a mod that reorders its port declarations rebinds nothing. <see cref="KindState"/>
/// is opaque here: bytes whose first byte is their own version, owned by the kind that ticks the
/// row, empty for both of M10a's kinds, and carried untouched for a kind this build does not know
/// so a removed mod's row survives the way its material hole does.
/// </para>
/// </remarks>
public sealed class MachineRow
{
    private readonly SortedDictionary<string, long> _ports = new(StringComparer.Ordinal);

    public MachineRow(
        BrushCell cell,
        int materialId,
        BrushFacing facing,
        IEnumerable<KeyValuePair<string, long>>? ports = null,
        byte[]? kindState = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(materialId);
        if (!Enum.IsDefined(facing))
        {
            throw new ArgumentOutOfRangeException(nameof(facing), facing, "Not a facing.");
        }

        Cell = cell;
        MaterialId = materialId;
        Facing = facing;
        KindState = kindState ?? [];

        if (ports is not null)
        {
            foreach ((string name, long value) in ports)
            {
                if (!PortDefinition.IsValidName(name))
                {
                    throw new ArgumentException($"'{name}' is not a port name.", nameof(ports));
                }

                if (_ports.Count == PortDefinition.MaxPorts && !_ports.ContainsKey(name))
                {
                    throw new ArgumentException(
                        $"A machine holds at most {PortDefinition.MaxPorts} ports.", nameof(ports));
                }

                _ports[name] = value;
            }
        }
    }

    /// <summary>The brush cell the 64-voxel body occupies.</summary>
    public BrushCell Cell { get; }

    /// <summary>The body's origin voxel — the row's key on disk.</summary>
    public VoxelPos Origin => Cell.OriginVoxel;

    /// <summary>The chunk the body is in. A brush cell never straddles a chunk face.</summary>
    public ChunkPos Chunk => Origin.Chunk;

    /// <summary>The body material's numeric ID, through the world's table.</summary>
    public int MaterialId { get; }

    /// <summary>Which way the machine faces, from the aim at placement. Unread by M10a's kinds.</summary>
    public BrushFacing Facing { get; }

    /// <summary>The port values, by name, in ordinal order.</summary>
    public IReadOnlyDictionary<string, long> Ports => _ports;

    /// <summary>The kind's own state between boundaries, version byte first. Empty when it keeps none.</summary>
    public byte[] KindState { get; set; }

    /// <summary>Body voxels that stopped being the body's material since the row was last whole — the hook's count.</summary>
    /// <remarks>
    /// Session state, never persisted: a damaged row is dismantled or destroyed at the same
    /// frame's drain, so a row on disk is always whole. See <see cref="MachineSet"/>'s credit rule.
    /// </remarks>
    public int Lost { get; internal set; }

    /// <summary>Of those, how many a pick took and credited — <see cref="IMachineBodies.Taken"/>'s count.</summary>
    public int Carved { get; internal set; }

    /// <summary>
    /// A slow lake pump's sub-litre remainder: the millilitres of its budget not yet worth a whole
    /// litre (M10a-3, decision 5). A want, not water — never persisted, so a quit loses a fraction
    /// of one boundary's pumping and never a litre.
    /// </summary>
    public int OwedMillilitres { get; internal set; }

    /// <summary>The port's value, or its absence.</summary>
    public bool TryGetPort(string name, out long value) => _ports.TryGetValue(name, out value);

    /// <summary>Writes a port value with no bounds check — the reader's and the reconcile's door, not the programmer's.</summary>
    /// <remarks>
    /// The programmer's clamped write is M10b's <c>MachineSet.SetPort</c>. This exists so the
    /// restore can put a clamped value back and a reconcile can supply a default, and it is
    /// internal so nothing outside the assembly can write an unbounded port.
    /// </remarks>
    internal void SetPortUnchecked(string name, long value) => _ports[name] = value;

    /// <summary>Drops a port the definition does not declare.</summary>
    internal bool RemovePort(string name) => _ports.Remove(name);

    public override string ToString() =>
        $"machine {MaterialId} at {Cell} facing {Facing}, {_ports.Count} port(s), {KindState.Length} state byte(s)";
}
