namespace Overburden.Core.Content;

/// <summary>
/// One mod's kit (IDEAL-MACHINES-PLAN decision 53; IM-6's pass, decision 22): what a creative world is given once, at
/// creation, from a file in the mod's <c>kits/</c> folder.
/// </summary>
/// <remarks>
/// <para>
/// <b>A content section, not a path special to the base game</b> (non-negotiable 5): the base game's kit loads through
/// the loader any mod's does, and a mod's kit adds to it. Counts are in the hotbar's units — a body, a cubic metre, a
/// piece — so a file says <c>"stone": 4096</c> and means the 4,096 m³ the slot will show.
/// </para>
/// <para>
/// <b>Identifiers, not numbers</b>: a kit is read before any world exists, and a material's number is the world's.
/// <see cref="Inventory.KitGrant"/> resolves them against the world being created.
/// </para>
/// </remarks>
public sealed class KitDefinition
{
    /// <summary>The kit's ID. Two files giving one are refused, as two materials giving one are.</summary>
    public required Identifier Id { get; init; }

    /// <summary>The file it came from, <c>mod/kits/name.json</c>, for the conflicts that name both files.</summary>
    public required string File { get; init; }

    /// <summary>Each material and its count in the hotbar's unit, in file order, each material once.</summary>
    public IReadOnlyList<KitMaterial> Materials { get; init; } = [];

    /// <summary>The row it sets — nine slots, null an empty one — or null when it sets none.</summary>
    public IReadOnlyList<Identifier?>? Row { get; init; }

    /// <summary>The pack it puts on the player's back, or null when it puts none.</summary>
    public KitPack? Pack { get; init; }

    public override string ToString() => $"{Id} ({File})";
}

/// <summary>A material a kit grants, and how many of the hotbar's units.</summary>
public readonly record struct KitMaterial(Identifier Material, long Count);

/// <summary>The pack a kit puts on the player's back: a carried storage, and the channel it is worn on.</summary>
public readonly record struct KitPack(Identifier Material, int Channel);

/// <summary>Every mod's kits, unioned (decision 22): the materials summed, at most one row and at most one pack.</summary>
/// <remarks>
/// <b>A conflict is refused, never resolved by load order</b>: two kits setting the row, or the pack, are refused naming
/// both files, because "the second wins" would make a world's start depend on how its mods sort. The materials have
/// no such conflict — two kits each giving stone give the sum — so the union is the same in every order.
/// </remarks>
public sealed class KitTable
{
    /// <summary>The folder a mod's kits live in.</summary>
    public const string Folder = "kits";

    private readonly List<KitDefinition> _kits = [];

    /// <summary>The kits, in load order.</summary>
    public IReadOnlyList<KitDefinition> Kits => _kits;

    public int Count => _kits.Count;

    /// <summary>Adds <paramref name="kit"/>, or refuses it naming the file it conflicts with.</summary>
    public bool TryAdd(KitDefinition kit, out string? conflict)
    {
        ArgumentNullException.ThrowIfNull(kit);
        foreach (KitDefinition other in _kits)
        {
            if (other.Id == kit.Id)
            {
                conflict = $"kit {kit.Id} is given by {other.File} already; a kit ID is given once, as a material's is.";
                return false;
            }
        }

        if (kit.Row is not null && _kits.Find(other => other.Row is not null) is { } rowSetter)
        {
            conflict = $"kit {kit.Id} sets the row, and so does {rowSetter.Id} in {rowSetter.File}; one kit may set the "
                       + "row, since which one wins would otherwise turn on how the mods sort.";
            return false;
        }

        if (kit.Pack is not null && _kits.Find(other => other.Pack is not null) is { } packSetter)
        {
            conflict = $"kit {kit.Id} puts a pack on, and so does {packSetter.Id} in {packSetter.File}; one kit may, "
                       + "since a player wears one pack.";
            return false;
        }

        _kits.Add(kit);
        conflict = null;
        return true;
    }
}
