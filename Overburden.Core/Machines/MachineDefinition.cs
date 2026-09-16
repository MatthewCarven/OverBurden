using Overburden.Core.Content;

namespace Overburden.Core.Machines;

/// <summary>The engine behaviours a machine can pick by name. Two in M10a; the rest are reserved.</summary>
/// <remarks>
/// <b>A kind is an engine behaviour keyed by a name, with the JSON supplying its numbers</b>
/// (M10-PLAN decision 1) — the way <c>lightEmission</c> is an engine behaviour keyed by a number.
/// A mod can ship a faster pump or a cheaper outlet; a new kind is M12's scripting. The reserved
/// names — <c>generator</c>, <c>converter</c>, <c>storage</c>, <c>station</c> — are refused by the
/// loader naming the part that will serve them, rather than loaded as machines that tick nothing.
/// </remarks>
public enum MachineKind : byte
{
    Pump,
    Outlet,

    /// <summary>Turns one medium into another each boundary — solar into a channel's pool (M10c).</summary>
    Converter,

    /// <summary>Holds joules between boundaries — a placed cell, or a carried pack (M10c).</summary>
    Storage,
}

/// <summary>What a link carries.</summary>
public enum MachineMedium : byte
{
    Power,
    Fluid,
}

/// <summary>Which way a link faces: a source puts the medium out, a sink takes it in.</summary>
public enum MachineRole : byte
{
    Source,
    Sink,
}

/// <summary>
/// One way a medium reaches a machine: what it carries, which way, and by what rule
/// (<c>via</c>) — the membership rule the ladder's one idea turns on.
/// </summary>
/// <param name="Name">The link's name in the JSON, for messages.</param>
/// <param name="Carries">Power or fluid.</param>
/// <param name="Role">Source or sink.</param>
/// <param name="Via">
/// The membership rule: <c>channel</c> is served in M10a; the other engine names are reserved
/// (<see cref="MachineDefinition.ReservedVia"/>) and anything else is a conductor class (M10e).
/// </param>
/// <param name="Face">Which body face, for a physical rule; null for a channel.</param>
/// <param name="Port">The port naming the channel, for <c>via: channel</c>.</param>
public sealed record MachineLink(
    string Name,
    MachineMedium Carries,
    MachineRole Role,
    string Via,
    string? Face,
    string? Port)
{
    /// <summary>The one rule M10a's tick serves.</summary>
    public const string ChannelVia = "channel";

    public bool IsChannel => Via == ChannelVia;
}

/// <summary>
/// The <c>machine</c> block of a material's JSON: what kind of machine a body of that material is,
/// its numbers, its links and its ports.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inside the material rather than a registry of its own</b> (M10-PLAN decision 1): a machine
/// is a material plus a row, and the material half already has an ID in the world's table, a
/// registry entry, a swatch, a hardness and a support distance. One ID, one registry, no second
/// table for non-negotiable 2 to worry about. What the row holds is only what the voxel cannot.
/// </para>
/// <para>
/// <b>Every number here is bounded at load and at use</b>, refused never clamped at load (M3c's
/// rule for the tool radius), because a loop sized by a mod's number is a loop a mod can hang.
/// The rate is kept in millilitres a second so the boundary's share is integer arithmetic.
/// </para>
/// </remarks>
public sealed class MachineDefinition
{
    /// <summary>The most a pump may declare: a 3 × 3 × 3 m box holds 27,000 L, twice a second.</summary>
    public const long MaxMillilitresPerSecond = 54_000L * 1_000L;

    /// <summary>The deepest lift a pump may declare — M4a's float world is a few kilometres across.</summary>
    public const double MaxHeadMetres = 1024.0;

    public const int MinIntakeRadiusCells = 1;

    public const int MaxIntakeRadiusCells = 2;

    /// <summary>The engine's own <c>via</c> names. <see cref="MachineLink.ChannelVia"/> is served from M10a, <see cref="SolarVia"/> from M10c.</summary>
    public static readonly IReadOnlyList<string> ReservedVia = ["channel", "crank", "burner", "solar", "surround"];

    /// <summary>The prefix of the one reserved <c>via</c> that takes an argument.</summary>
    public const string AdjacentViaPrefix = "adjacent:";

    /// <summary>The sky as a medium: a converter's sink link <c>via: solar</c> reads the openness over its body and the overcast (M10c, decision 2).</summary>
    public const string SolarVia = "solar";

    /// <summary>The <c>via</c> rules this build's tick can serve.</summary>
    public static readonly IReadOnlyList<string> ServedVia = [MachineLink.ChannelVia, SolarVia];

    /// <summary>The most a converter may be rated: a gigawatt, so a boundary's millijoules and a channel's sum stay inside a long.</summary>
    public const long MaxRatedWatts = 1_000_000_000L;

    /// <summary>The most a storage may hold, in joules: a terajoule, for the same reason (the vacuum cell is 5 GJ).</summary>
    public const long MaxCapacityJoules = 1_000_000_000_000L;

    /// <summary>The most a litre may cost a pump: a gigajoule, so the largest offer times it fits a long.</summary>
    public const long MaxJoulesPerLitre = 1_000_000_000L;

    /// <summary>Efficiency's unit: a converter's <c>efficiency</c> is read as a whole number of thousandths, never a float in the tick (decision 10).</summary>
    public const int PerMille = 1000;

    public required MachineKind Kind { get; init; }

    /// <summary>A converter's output at full input, in watts. Zero for a kind that has none.</summary>
    public long RatedWatts { get; init; }

    /// <summary>A converter's efficiency in thousandths, 0..1000; 1000 for a kind that has none.</summary>
    public int EfficiencyPerMille { get; init; } = PerMille;

    /// <summary>A storage's capacity in millijoules. Zero for a kind that has none.</summary>
    public long CapacityMillijoules { get; init; }

    /// <summary>Whether the storage is a pack in the hand rather than a body in the world (ladder decision 15).</summary>
    public bool Carried { get; init; }

    /// <summary>How fast a carried storage may take charge, in watts; null is unbounded. Unread on a placed one.</summary>
    public long? RechargeWatts { get; init; }

    /// <summary>A pump's nominal rate. Zero for a kind that has none.</summary>
    public long MillilitresPerSecond { get; init; }

    /// <summary>How far a pump can lift, or null for one with no bound (the relay).</summary>
    public double? HeadMetres { get; init; }

    /// <summary>What a litre costs a pump with no head, in joules. Parsed, unread until M10c.</summary>
    public long JoulesPerLitre { get; init; }

    /// <summary>The box a pump reads, in brush cells each way from its body: 1 is 3 × 3 × 3.</summary>
    public int IntakeRadiusCells { get; init; } = MinIntakeRadiusCells;

    public IReadOnlyList<MachineLink> Links { get; init; } = [];

    public IReadOnlyList<PortDefinition> Ports { get; init; } = [];

    /// <summary>The link called <paramref name="name"/>, or null.</summary>
    public MachineLink? Link(string name)
    {
        foreach (MachineLink link in Links)
        {
            if (link.Name == name) return link;
        }

        return null;
    }

    /// <summary>The port called <paramref name="name"/>, or null.</summary>
    public PortDefinition? Port(string name)
    {
        foreach (PortDefinition port in Ports)
        {
            if (port.Name == name) return port;
        }

        return null;
    }

    /// <summary>Every port at its default — what a freshly placed row holds.</summary>
    public IEnumerable<KeyValuePair<string, long>> DefaultPorts()
    {
        foreach (PortDefinition port in Ports)
        {
            yield return new KeyValuePair<string, long>(port.Name, port.Default);
        }
    }

    /// <summary>
    /// Whether the definition is one this engine can serve, with the reason it is not.
    /// </summary>
    /// <remarks>
    /// The loader's rules, in one place so a definition built without the loader — a test, a
    /// tool — is held to the same ones. Refused with the part named rather than loaded to tick
    /// nothing: M5b shipped a lamp that did not glow for a day inside one milestone, and a
    /// machine that loads and does nothing is that state against a format mods write to.
    /// </remarks>
    public bool TryValidate(out string? error)
    {
        if (!Enum.IsDefined(Kind))
        {
            error = $"kind {Kind} is not a machine kind.";
            return false;
        }

        if (Ports.Count > PortDefinition.MaxPorts)
        {
            error = $"declares {Ports.Count} ports; a machine holds at most {PortDefinition.MaxPorts}.";
            return false;
        }

        var portNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (PortDefinition port in Ports)
        {
            if (!portNames.Add(port.Name))
            {
                error = $"port '{port.Name}' is declared twice.";
                return false;
            }
        }

        if (Kind == MachineKind.Pump)
        {
            if (MillilitresPerSecond <= 0 || MillilitresPerSecond > MaxMillilitresPerSecond)
            {
                error = $"litresPerSecond must be more than 0 and at most {MaxMillilitresPerSecond / 1000:N0} for a pump.";
                return false;
            }

            if (IntakeRadiusCells < MinIntakeRadiusCells || IntakeRadiusCells > MaxIntakeRadiusCells)
            {
                error = $"intakeRadiusCells must be within {MinIntakeRadiusCells}..{MaxIntakeRadiusCells}.";
                return false;
            }
        }

        if (HeadMetres is { } head && (double.IsNaN(head) || head <= 0 || head > MaxHeadMetres))
        {
            error = $"headMetres must be null or within 0..{MaxHeadMetres:F0}.";
            return false;
        }

        if (JoulesPerLitre < 0 || JoulesPerLitre > MaxJoulesPerLitre)
        {
            error = $"joulesPerLitre must be within 0..{MaxJoulesPerLitre:N0}.";
            return false;
        }

        if (Kind == MachineKind.Converter)
        {
            if (RatedWatts <= 0 || RatedWatts > MaxRatedWatts)
            {
                error = $"ratedWatts must be more than 0 and at most {MaxRatedWatts:N0} for a converter.";
                return false;
            }

            if (EfficiencyPerMille < 0 || EfficiencyPerMille > PerMille)
            {
                error = "efficiency must be within 0..1.";
                return false;
            }
        }

        if (Kind == MachineKind.Storage)
        {
            if (CapacityMillijoules <= 0 || CapacityMillijoules > MaxCapacityJoules * PerMille)
            {
                error = $"capacityJoules must be more than 0 and at most {MaxCapacityJoules:N0} for a storage.";
                return false;
            }

            if (RechargeWatts is { } recharge && (recharge <= 0 || recharge > MaxRatedWatts))
            {
                error = $"rechargeWatts must be null or more than 0 and at most {MaxRatedWatts:N0}.";
                return false;
            }
        }
        else if (Carried)
        {
            error = "only a storage can be carried.";
            return false;
        }

        var linkNames = new HashSet<string>(StringComparer.Ordinal);
        bool fluidSource = false, fluidSink = false, powerSource = false, powerSink = false, solar = false;
        foreach (MachineLink link in Links)
        {
            if (!PortDefinition.IsValidName(link.Name))
            {
                error = $"'{link.Name}' is not a link name.";
                return false;
            }

            if (!linkNames.Add(link.Name))
            {
                error = $"link '{link.Name}' is declared twice.";
                return false;
            }

            if (!ViaIsServed(link.Via, out string? viaError))
            {
                error = $"link '{link.Name}': {viaError}";
                return false;
            }

            if (link.IsChannel)
            {
                if (link.Port is null || Port(link.Port) is null)
                {
                    error = $"link '{link.Name}' is via channel and names no declared port for it.";
                    return false;
                }

                if (link.Face is not null)
                {
                    error = $"link '{link.Name}' is via channel and declares a face; a channel has none.";
                    return false;
                }
            }

            if (link.Via == SolarVia)
            {
                // The sky is a medium with one direction: it is drawn from and never poured into,
                // and only a converter turns it into joules a pool can hold.
                if (link.Carries != MachineMedium.Power || link.Role != MachineRole.Sink)
                {
                    error = $"link '{link.Name}' is via solar, which is a power sink and nothing else.";
                    return false;
                }

                if (Kind != MachineKind.Converter)
                {
                    error = $"link '{link.Name}' is via solar, which only a converter can take.";
                    return false;
                }

                if (link.Face is not null)
                {
                    error = $"link '{link.Name}' is via solar and declares a face; the sky is over the top.";
                    return false;
                }

                solar = true;
            }

            if (link.Carries == MachineMedium.Fluid)
            {
                if (link.Role == MachineRole.Source) fluidSource = true;
                else fluidSink = true;
            }
            else
            {
                if (link.Role == MachineRole.Source) powerSource = true;
                else powerSink = true;
            }
        }

        if (Kind == MachineKind.Pump && !fluidSource)
        {
            error = "a pump declares a fluid link with role source — where its water goes.";
            return false;
        }

        if (Kind == MachineKind.Outlet && !fluidSink)
        {
            error = "an outlet declares a fluid link with role sink — where its water comes from.";
            return false;
        }

        if (Kind == MachineKind.Converter && (!powerSink || !powerSource))
        {
            error = "a converter declares a power link with role sink and one with role source — what it turns into what.";
            return false;
        }

        if (Kind == MachineKind.Converter && !solar)
        {
            // Every other input medium is M10f's; a converter with nothing served to drink from
            // would load and generate nothing, which is the state this method exists to refuse.
            error = "a converter's sink is via solar in this build — the other media are M10f.";
            return false;
        }

        if (Kind == MachineKind.Storage)
        {
            int powerLinks = 0;
            foreach (MachineLink link in Links)
            {
                if (link.Carries == MachineMedium.Power) powerLinks++;
            }

            if (powerLinks != 1 || !powerSink || powerSource)
            {
                error = "a storage declares exactly one power link, with role sink — the pool it belongs to.";
                return false;
            }

            if (Carried)
            {
                foreach (MachineLink link in Links)
                {
                    if (link.Face is not null)
                    {
                        error = $"link '{link.Name}' declares a face on a carried storage, which has no body.";
                        return false;
                    }
                }
            }
        }

        error = null;
        return true;
    }

    /// <summary>Whether this build's tick can serve a <c>via</c>, and if not which part will.</summary>
    public static bool ViaIsServed(string via, out string? error)
    {
        if (string.IsNullOrEmpty(via))
        {
            error = "via is empty.";
            return false;
        }

        if (ServedVia.Contains(via))
        {
            error = null;
            return true;
        }

        if (ReservedVia.Contains(via) || via.StartsWith(AdjacentViaPrefix, StringComparison.Ordinal))
        {
            error = $"via '{via}' is an engine rule this build does not serve yet (M10f).";
            return false;
        }

        if (!Identifier.IsValidNamespace(via))
        {
            error = $"via '{via}' is not a rule name: lowercase letters, digits, '_', '-' or '.'.";
            return false;
        }

        error = $"via '{via}' is a conductor class, and the conductor index is M10e.";
        return false;
    }
}
