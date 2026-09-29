using Overburden.Core.Content;

namespace Overburden.Core.Machines;

/// <summary>
/// The six extras (IDEAL-MACHINES-PLAN decision 2): capabilities defined once in C#, which a
/// machine opts into by name in its JSON. A bit each, so a material's set is one byte.
/// </summary>
/// <remarks>
/// A closed set, as <see cref="MachineKind"/> is: a mod opts in, it cannot invent one. No extra adds
/// a case to a kind switch; each is read at one fixed site, by flag.
/// </remarks>
[Flags]
public enum MachineExtra : byte
{
    None = 0,

    /// <summary>Reads its channel's signal: collapse f-1's readers, and the power sensor (decision 38).</summary>
    Sense = 1 << 0,

    /// <summary>A machine a fall takes returns to the pocket while its channel is solvent (decision 39).</summary>
    Recall = 1 << 1,

    /// <summary>On the operations board, and its panel opens at any range (decision 40).</summary>
    Remote = 1 << 2,

    /// <summary>The quantum backpack's window onto its channel's cells (decision 7).</summary>
    Charge = 1 << 3,

    /// <summary>A priority port, higher paid first (decision 41).</summary>
    Priority = 1 << 4,

    /// <summary>State on F12's pins (decision 42).</summary>
    Telemetry = 1 << 5,
}

/// <summary>
/// The form an extra takes over a medium (decision 3): how far and how it reaches. A closed set,
/// named from the plan's section F; only the quantum forms exist until the lower tiers' membership
/// (M10e/f), whose forms are reserved names in <see cref="MachineExtras"/>.
/// </summary>
public enum ExtraForm : byte
{
    None = 0,

    /// <summary>Sense over a channel: the readers switch on the channel's one boolean.</summary>
    Switch,

    /// <summary>Recall over a channel: the body returns to the pocket.</summary>
    Return,

    /// <summary>Remote over a channel: a row on the operations board, at any range.</summary>
    Board,

    /// <summary>Charge over a channel: a window that holds nothing and draws from the channel's cells.</summary>
    Window,

    /// <summary>Priority over a channel: a port, higher paid first.</summary>
    Port,

    /// <summary>Telemetry over a channel: the running state on the map's pin.</summary>
    State,
}

/// <summary>The extras' names, forms, reach rules and the slice that serves each — the one table the loader and the validator read.</summary>
public static class MachineExtras
{
    /// <summary>Every extra, in the plan's order.</summary>
    public const MachineExtra All =
        MachineExtra.Sense | MachineExtra.Recall | MachineExtra.Remote
        | MachineExtra.Charge | MachineExtra.Priority | MachineExtra.Telemetry;

    /// <summary>
    /// The extras this build serves on a machine (IM-1's pass, decision 2): each is switched on
    /// here by the slice that reads it — IM-2 <c>Charge</c> (the window) and <c>Priority</c> (the
    /// consumers' order); collapse f-1 <c>Sense</c>; IM-9 the other three.
    /// </summary>
    public const MachineExtra Served = MachineExtra.Charge | MachineExtra.Priority;

    /// <summary>The port <c>sense</c> needs: collapse f-1's <c>onsignal</c>, 0 ignore, 1 off while signalled, 2 on only while signalled.</summary>
    public const string OnSignalPort = "onsignal";

    /// <summary>The port <c>priority</c> needs, 0–9 (decision 41).</summary>
    public const string PriorityPort = "priority";

    private static readonly Row[] Rows =
    [
        new(MachineExtra.Sense, "sense", "collapse f-1", ExtraForm.Switch, "switch", ["bell"], OnSignalPort, 0, 2),
        new(MachineExtra.Recall, "recall", "IM-9", ExtraForm.Return, "return", [], null, 0, 0),
        new(MachineExtra.Remote, "remote", "IM-9", ExtraForm.Board, "board", ["station"], null, 0, 0),
        new(MachineExtra.Charge, "charge", "IM-2", ExtraForm.Window, "window", ["dock"], null, 0, 0),
        new(MachineExtra.Priority, "priority", "IM-2", ExtraForm.Port, "port", ["position"], PriorityPort, 0, 9),
        new(MachineExtra.Telemetry, "telemetry", "IM-9", ExtraForm.State, "state", ["pin"], null, 0, 0),
    ];

    /// <summary>Every extra, one bit each, in the plan's order.</summary>
    public static IEnumerable<MachineExtra> Each => Rows.Select(r => r.Extra);

    /// <summary>The name an extra has in JSON.</summary>
    public static string NameOf(MachineExtra extra) => RowOf(extra).Name;

    /// <summary>The slice whose reader serves the extra, for the refusal that names it.</summary>
    public static string ServedBy(MachineExtra extra) => RowOf(extra).ServedBy;

    /// <summary>Whether a form belongs to the extra.</summary>
    public static bool IsFormOf(MachineExtra extra, ExtraForm form) =>
        form != ExtraForm.None && Rows.Any(r => r.Extra == extra && r.Form == form);

    /// <summary>
    /// Whether the extra is a carried storage's (<c>charge</c>, the window) rather than a placed
    /// machine's: a pack has no body, no row and no pin, and a placed body is not a window.
    /// </summary>
    public static bool IsCarriedExtra(MachineExtra extra) => extra == MachineExtra.Charge;

    /// <summary>The port an extra needs and its exact bounds, or null for one that needs none.</summary>
    public static (string Name, long Min, long Max)? RequiredPort(MachineExtra extra) =>
        RowOf(extra) is { Port: { } port } row ? (port, row.PortMin, row.PortMax) : null;

    /// <summary>Reads an extra's name, with a did-you-mean when it is not one.</summary>
    public static bool TryParse(string? name, out MachineExtra extra, out string? error)
    {
        foreach (Row row in Rows)
        {
            if (row.Name == name)
            {
                extra = row.Extra;
                error = null;
                return true;
            }
        }

        extra = MachineExtra.None;
        error = name is null
            ? "an extra names nothing."
            : Sentence(
                $"'{name}' is not an extra (the six are {string.Join(", ", Rows.Select(r => r.Name))})",
                StrictJson.Suggest(name, Rows.Select(r => r.Name)));
        return false;
    }

    /// <summary>
    /// Reads the form an extra takes, refusing a lower tier's form naming its milestone (as a
    /// reserved <c>via</c> is refused) and anything else with a did-you-mean.
    /// </summary>
    public static bool TryParseForm(MachineExtra extra, string? name, out ExtraForm form, out string? error)
    {
        Row row = RowOf(extra);
        form = ExtraForm.None;
        if (string.IsNullOrEmpty(name))
        {
            error = $"{row.Name} names no form.";
            return false;
        }

        if (name == row.FormName)
        {
            form = row.Form;
            error = null;
            return true;
        }

        if (row.Reserved.Contains(name))
        {
            error = $"form '{name}' is a lower tier's {row.Name}, and the lower tiers' membership is M10e/f.";
            return false;
        }

        if (Rows.FirstOrDefault(r => r.FormName == name || r.Reserved.Contains(name)) is { } other)
        {
            error = $"form '{name}' is {other.Name}'s, not {row.Name}'s; {row.Name}'s is '{row.FormName}'.";
            return false;
        }

        error = Sentence($"'{name}' is not a form of {row.Name}", StrictJson.Suggest(name, [row.FormName, .. row.Reserved]));
        return false;
    }

    /// <summary>
    /// Whether an extra can reach over <paramref name="medium"/> in this build (IM-1's pass,
    /// decision 3): a medium is named as a <c>via</c> is and checked by the same rule, so a
    /// misspelt one is refused by the rule that already refuses a misspelt <c>via</c>. The sky is
    /// served as a <c>via</c> but reaches no one.
    /// </summary>
    public static bool MediumIsServed(string medium, out string? error)
    {
        if (medium == MachineDefinition.SolarVia)
        {
            error = "medium 'solar' is the sky, which feeds a converter and reaches no one.";
            return false;
        }

        if (MachineDefinition.ViaIsServed(medium, "medium", out error))
        {
            return true;
        }

        // The via rule's reason stands; a near miss of a medium this build serves is named too.
        string near = StrictJson.Suggest(medium, [MachineLink.ChannelVia]);
        if (near.Length > 0)
        {
            error = error!.TrimEnd('.') + near;
        }

        return false;
    }

    /// <summary>A message ending in its suggestion when there is one, else in a full stop.</summary>
    private static string Sentence(string body, string suggestion) =>
        suggestion.Length > 0 ? body + suggestion : body + ".";

    private static Row RowOf(MachineExtra extra)
    {
        foreach (Row row in Rows)
        {
            if (row.Extra == extra) return row;
        }

        throw new ArgumentOutOfRangeException(nameof(extra), extra, "not one extra");
    }

    private sealed record Row(
        MachineExtra Extra,
        string Name,
        string ServedBy,
        ExtraForm Form,
        string FormName,
        string[] Reserved,
        string? Port,
        long PortMin,
        long PortMax);
}

/// <summary>
/// Every mod's <c>extras.json</c>, unioned: which form each extra takes over each medium
/// (decision 3). One (extra, medium) pair has one owner, as one ID has one definition (IM-1's
/// pass, decision 5).
/// </summary>
public sealed class ExtrasTable
{
    /// <summary>The file, at a mod's root.</summary>
    public const string FileName = "extras.json";

    private readonly Dictionary<(MachineExtra Extra, string Medium), ExtraReach> _reach = [];

    /// <summary>How many (extra, medium) pairs are given.</summary>
    public int Count => _reach.Count;

    /// <summary>The form <paramref name="extra"/> takes over <paramref name="medium"/>, if any mod gives one.</summary>
    public bool TryGetForm(MachineExtra extra, string medium, out ExtraForm form)
    {
        if (_reach.TryGetValue((extra, medium), out ExtraReach reach))
        {
            form = reach.Form;
            return true;
        }

        form = ExtraForm.None;
        return false;
    }

    /// <summary>Every pair, by extra then medium, with the file that gave it.</summary>
    public IReadOnlyList<ExtraReach> Entries() =>
        [.. _reach.Values.OrderBy(r => r.Extra).ThenBy(r => r.Medium, StringComparer.Ordinal)];

    /// <summary>
    /// Adds a pair, or refuses it naming both files when another file already gave it. The loader
    /// adds in load order (the base game, then by ID), so the first owner is the same whatever
    /// order the mods were listed in.
    /// </summary>
    public bool TryAdd(MachineExtra extra, string medium, ExtraForm form, string file, out string? error)
    {
        if (_reach.TryGetValue((extra, medium), out ExtraReach held))
        {
            error =
                $"{MachineExtras.NameOf(extra)} over '{medium}' is already given by {held.File}; one mod owns a " +
                "pair, as one ID has one definition, and the first in load order keeps it.";
            return false;
        }

        _reach[(extra, medium)] = new ExtraReach(extra, medium, form, file);
        error = null;
        return true;
    }
}

/// <summary>One row of the table: an extra, the medium it reaches over, its form there, and the file that said so.</summary>
public readonly record struct ExtraReach(MachineExtra Extra, string Medium, ExtraForm Form, string File);
