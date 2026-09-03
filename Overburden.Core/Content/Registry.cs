using System.Diagnostics.CodeAnalysis;

namespace Overburden.Core.Content;

/// <summary>
/// Maps <see cref="Identifier"/>s to definitions and to the numeric IDs used in hot
/// paths and in chunk storage.
/// </summary>
/// <typeparam name="T">The definition type, e.g. <see cref="MaterialDefinition"/>.</typeparam>
/// <remarks>
/// <para><b>The numeric ID space belongs to the world, not to the process.</b> Assigning
/// numbers in registration order at startup looks fine until a mod is installed, at
/// which point every number shifts and every saved chunk decodes to the wrong material.
/// So a world's saved table is seeded first via <see cref="SeedIdTable"/>, and only
/// content the table has never seen gets a fresh number appended.</para>
///
/// <para><b>Numbers are never reused and the table is never compacted.</b> Removing a mod
/// leaves a hole; the hole stays. Its IDs still resolve — to a placeholder that retains
/// the original <see cref="Identifier"/> — so re-installing the mod restores the world
/// exactly rather than reinterpreting its blocks as something else.</para>
///
/// <para>Mutation is only legal before <see cref="Freeze"/>. After freezing the registry
/// is immutable and therefore safe to read from any thread without synchronisation,
/// which matters because mesh jobs read material properties off worker threads.</para>
/// </remarks>
public sealed class Registry<T> where T : class
{
    private readonly Dictionary<Identifier, int> _idsByIdentifier = [];
    private readonly List<Identifier> _identifiersById = [];
    private readonly List<T?> _valuesById = [];

    /// <summary>
    /// IDs an installed mod actually defined, as opposed to ones that exist only because
    /// a world's saved table remembers them. Distinguishes a real entry from a
    /// placeholder after <see cref="Freeze"/> has filled every slot.
    /// </summary>
    private readonly HashSet<int> _definedIds = [];

    private readonly Func<Identifier, T> _placeholderFactory;

    /// <summary>Human-readable name, used in exception messages.</summary>
    public string Name { get; }

    public bool IsFrozen { get; private set; }

    /// <summary>Number of allocated numeric IDs, including holes left by removed mods.</summary>
    public int Count => _identifiersById.Count;

    /// <param name="name">Used in error messages, e.g. "materials".</param>
    /// <param name="placeholderFactory">
    /// Builds the stand-in for an ID that exists in the world's table but has no
    /// definition loaded — a removed mod. It receives the original identifier and must
    /// retain it, because that is what makes re-installing the mod lossless.
    /// </param>
    public Registry(string name, Func<Identifier, T> placeholderFactory)
    {
        Name = name;
        _placeholderFactory = placeholderFactory;
    }

    /// <summary>
    /// Restores a world's saved ID assignment. Index in <paramref name="table"/> is the
    /// numeric ID; a <c>default</c> entry is a hole and stays a hole.
    /// </summary>
    /// <remarks>
    /// Must be called before any <see cref="Register"/>, and only once — seeding after
    /// content is registered would mean numbers were already handed out against a
    /// different table.
    /// </remarks>
    public void SeedIdTable(IReadOnlyList<Identifier> table)
    {
        ThrowIfFrozen();

        if (_identifiersById.Count > 0)
        {
            throw new InvalidOperationException(
                $"Registry '{Name}' has already assigned IDs; seed the saved table before registering anything.");
        }

        foreach (var identifier in table)
        {
            int id = _identifiersById.Count;
            _identifiersById.Add(identifier);
            _valuesById.Add(null);

            if (identifier.IsValid)
            {
                if (_idsByIdentifier.TryGetValue(identifier, out int first))
                {
                    // Two numbers for one identifier would leave every lookup by name pointing at
                    // one of them and every stored voxel under the other. The way a saved table
                    // gets here is a build that predates a namespace alias opening a newer world:
                    // it does not recognise the current spelling, registers its own, and writes
                    // both. See Identifier.RetiredNamespaces.
                    throw new InvalidOperationException(
                        $"Registry '{Name}': saved IDs {first} and {id} are both '{identifier}'. "
                        + "A world's table names each material once.");
                }

                _idsByIdentifier[identifier] = id;
            }
        }
    }

    /// <summary>
    /// Allocates a numeric ID without a definition. Used to pin well-known IDs — see
    /// <see cref="MaterialIds.Air"/>, which must be 0 — before content loading runs.
    /// </summary>
    /// <remarks>
    /// Reserving the <i>number</i> is not a privileged content path: the definition still
    /// has to arrive from JSON like everything else, and its absence is a load error.
    /// </remarks>
    public int ReserveId(Identifier identifier)
    {
        ThrowIfFrozen();
        return GetOrAllocate(identifier);
    }

    /// <summary>Registers a definition, returning its numeric ID.</summary>
    public int Register(Identifier identifier, T value)
    {
        ThrowIfFrozen();

        int id = GetOrAllocate(identifier);

        if (_valuesById[id] is not null)
        {
            throw new InvalidOperationException(
                $"Registry '{Name}' already has a definition for '{identifier}'.");
        }

        _valuesById[id] = value;
        _definedIds.Add(id);
        return id;
    }

    private int GetOrAllocate(Identifier identifier)
    {
        if (!identifier.IsValid)
        {
            throw new ArgumentException("Cannot register an invalid identifier.", nameof(identifier));
        }

        if (_idsByIdentifier.TryGetValue(identifier, out int existing))
        {
            return existing;
        }

        int id = _identifiersById.Count;
        _identifiersById.Add(identifier);
        _valuesById.Add(null);
        _idsByIdentifier[identifier] = id;
        return id;
    }

    /// <summary>
    /// Seals the registry. Every allocated ID without a definition is filled with a
    /// placeholder, so lookups after this point never return null and a removed mod
    /// degrades to an inert block rather than a crash.
    /// </summary>
    public void Freeze()
    {
        if (IsFrozen) return;

        for (int id = 0; id < _valuesById.Count; id++)
        {
            if (_valuesById[id] is null && _identifiersById[id].IsValid)
            {
                _valuesById[id] = _placeholderFactory(_identifiersById[id]);
            }
        }

        IsFrozen = true;
    }

    public T Get(int numericId)
    {
        if ((uint)numericId >= (uint)_valuesById.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numericId),
                $"Registry '{Name}' has no ID {numericId} (count {_valuesById.Count}).");
        }

        return _valuesById[numericId]
               ?? throw new InvalidOperationException(
                   $"Registry '{Name}' ID {numericId} ('{_identifiersById[numericId]}') has no definition. " +
                   "Freeze() fills these; was the registry read before freezing?");
    }

    public T Get(Identifier identifier) => Get(GetNumericId(identifier));

    public bool TryGet(Identifier identifier, [NotNullWhen(true)] out T? value)
    {
        value = null;
        if (!_idsByIdentifier.TryGetValue(identifier, out int id)) return false;

        value = _valuesById[id];
        return value is not null;
    }

    public bool Contains(Identifier identifier) => _idsByIdentifier.ContainsKey(identifier);

    /// <summary>
    /// True if this ID exists in the world's table but no installed mod defines it, so
    /// <see cref="Get(int)"/> returns a placeholder. Re-installing the mod clears this.
    /// </summary>
    public bool IsPlaceholder(int numericId)
    {
        if ((uint)numericId >= (uint)_valuesById.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numericId),
                $"Registry '{Name}' has no ID {numericId} (count {_valuesById.Count}).");
        }

        return !_definedIds.Contains(numericId);
    }

    public int GetNumericId(Identifier identifier) =>
        _idsByIdentifier.TryGetValue(identifier, out int id)
            ? id
            : throw new KeyNotFoundException($"Registry '{Name}' has no entry for '{identifier}'.");

    public bool TryGetNumericId(Identifier identifier, out int numericId) =>
        _idsByIdentifier.TryGetValue(identifier, out numericId);

    public Identifier GetIdentifier(int numericId)
    {
        if ((uint)numericId >= (uint)_identifiersById.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numericId),
                $"Registry '{Name}' has no ID {numericId} (count {_identifiersById.Count}).");
        }

        return _identifiersById[numericId];
    }

    /// <summary>
    /// The ID table to write into the world file. Index is the numeric ID; a
    /// <c>default</c> entry is a hole that must be preserved on reload.
    /// </summary>
    public IReadOnlyList<Identifier> ExportIdTable() => _identifiersById.AsReadOnly();

    public IEnumerable<(int NumericId, Identifier Identifier, T Value)> Entries()
    {
        for (int id = 0; id < _valuesById.Count; id++)
        {
            if (_valuesById[id] is { } value)
            {
                yield return (id, _identifiersById[id], value);
            }
        }
    }

    private void ThrowIfFrozen()
    {
        if (IsFrozen)
        {
            throw new InvalidOperationException(
                $"Registry '{Name}' is frozen; content can only be registered during the load phase.");
        }
    }
}
