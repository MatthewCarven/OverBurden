namespace Overburden.Core.Persistence;

/// <summary>What kind of world this is (IDEAL-MACHINES-PLAN decision 51, header v9). A byte in the header, chosen once at creation.</summary>
/// <remarks>
/// <para>
/// <b>The world's, never the player's</b> (IM-6's pass, decision 20): nothing in IM-6 is keyed by player, and a second
/// player would join a creative world, not bring a mode to it (TODO's collision 8).
/// </para>
/// <para>
/// <b>Core carries the byte and never reads it.</b> No rule of water, power or support differs by mode; what differs is
/// which keys answer (<see cref="Inventory.DebugKeyGate"/>), whether a kit is granted at creation, and the first cell's
/// gift — each a door the Game opens, priced and printed.
/// </para>
/// </remarks>
public enum WorldMode : byte
{
    /// <summary>Every world made before v9, and the default: today's game exactly, every test key answering.</summary>
    Sandbox = 0,

    /// <summary>A kit at creation, the first cell's 1 GJ, and the test keys off whatever <c>--dev</c> says.</summary>
    Creative = 1,

    /// <summary>Reserved for survival, and refused at read and at write until it exists.</summary>
    Survival = 2,
}

/// <summary>What a world has already been given, so a door that grants once is never opened twice (decision 20).</summary>
[Flags]
public enum WorldFlags : byte
{
    None = 0,

    /// <summary>Bit 0: the kits were granted into the pocket at creation (decision 22).</summary>
    KitGranted = 1,

    /// <summary>Bit 1: the first placed storage row was given its 1 GJ (decision 23).</summary>
    FirstCellGranted = 2,
}

/// <summary>The mode and flags bytes' one rule, for the writer and the reader alike (decision 20), and the flag's words.</summary>
/// <remarks>
/// <b>One validator, alpha session C's shape</b> (<see cref="Inventory.HotbarSlots.Problem"/>): a header the reader would
/// refuse is refused at write too, so the session that produced it is named rather than the one that later cannot open
/// the world.
/// </remarks>
public static class WorldModes
{
    /// <summary>The flags bits this build knows. Any other bit set is a header from a newer build or a corrupt one.</summary>
    public const WorldFlags KnownFlags = WorldFlags.KitGranted | WorldFlags.FirstCellGranted;

    /// <summary>Why <paramref name="mode"/> and <paramref name="flags"/> cannot be a world's, or null when they can.</summary>
    /// <remarks>
    /// Raw bytes rather than the enums, so a value no enum member names is a refusal and not a cast that succeeds.
    /// <b>Survival is refused until it exists</b>: a world written by a build that has it would otherwise open here as
    /// a mode this build does not play, which is the silent reinterpretation the header refuses everywhere else.
    /// </remarks>
    public static string? Problem(byte mode, byte flags)
    {
        if (mode == (byte)WorldMode.Survival)
        {
            return $"the world's mode is {mode}, survival, which is reserved and not built yet. "
                   + "A world written by a build that has it cannot be played by this one.";
        }

        if (mode > (byte)WorldMode.Creative)
        {
            return $"the world's mode is {mode}; this build knows 0 (sandbox) and 1 (creative).";
        }

        int unknown = flags & ~(int)KnownFlags;
        if (unknown != 0)
        {
            return $"the world's flags are 0x{flags:X2}, with bit(s) 0x{unknown:X2} this build does not know; "
                   + "it knows bit 0 (the kit was granted) and bit 1 (the first cell's charge was granted).";
        }

        return null;
    }

    /// <summary>The mode's word, as the open line, the seed banner and <c>--mode=</c> spell it.</summary>
    public static string Name(WorldMode mode) => mode switch
    {
        WorldMode.Sandbox => "sandbox",
        WorldMode.Creative => "creative",
        WorldMode.Survival => "survival",
        _ => $"mode {(byte)mode}",
    };

    /// <summary>
    /// Reads <c>--mode=</c>'s value: <c>sandbox</c> or <c>creative</c>, any case. Survival is named and refused, since
    /// it is reserved; anything else is refused naming the two this build makes.
    /// </summary>
    public static bool TryParse(string? value, out WorldMode mode, out string? error)
    {
        mode = WorldMode.Sandbox;
        error = null;
        switch (value?.Trim().ToLowerInvariant())
        {
            case "sandbox":
                return true;
            case "creative":
                mode = WorldMode.Creative;
                return true;
            case "survival":
                error = "survival is reserved and not built yet; a world is sandbox or creative.";
                return false;
            default:
                error = $"'{value}' is not a mode; a world is sandbox or creative.";
                return false;
        }
    }
}
