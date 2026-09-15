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

    /// <summary>The engine's own <c>via</c> names. Only <see cref="MachineLink.ChannelVia"/> is served in M10a.</summary>
    public static readonly IReadOnlyList<string> ReservedVia = ["channel", "crank", "burner", "solar", "surround"];

    /// <summary>The prefix of the one reserved <c>via</c> that takes an argument.</summary>
    public const string AdjacentViaPrefix = "adjacent:";

    /// <summary>The <c>via</c> rules this build's tick can serve.</summary>
    public static readonly IReadOnlyList<string> ServedVia = [MachineLink.ChannelVia];

    public required MachineKind Kind { get; init; }

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

        if (JoulesPerLitre < 0)
        {
            error = "joulesPerLitre must not be negative.";
            return false;
        }

        var linkNames = new HashSet<string>(StringComparer.Ordinal);
        bool fluidSource = false, fluidSink = false;
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

            if (link.Carries == MachineMedium.Fluid)
            {
                if (link.Role == MachineRole.Source) fluidSource = true;
                else fluidSink = true;
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
