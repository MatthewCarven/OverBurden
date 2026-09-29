using Overburden.Core.Content;

namespace Overburden.Core.Machines;

/// <summary>The engine behaviours a machine can pick by name: four built (M10a–c); the rest are reserved.</summary>
/// <remarks>
/// <b>A kind is an engine behaviour keyed by a name, with the JSON supplying its numbers</b>
/// (M10-PLAN decision 1) — the way <c>lightEmission</c> is an engine behaviour keyed by a number.
/// A mod can ship a faster pump or a cheaper outlet; a new kind is M12's scripting, with the named
/// exceptions the plans take (collapse f's <c>sensor</c>, the ideal machines' <c>vault</c> and
/// <c>quarry</c>). The reserved names — <c>generator</c>, <c>station</c> — are refused by the loader
/// naming the part that will serve them, rather than loaded as machines that tick nothing. A
/// capability that is not a home for a conserved quantity is an extra (<see cref="MachineExtra"/>),
/// never a kind.
/// </remarks>
public enum MachineKind : byte
{
    Pump,
    Outlet,

    /// <summary>Turns one medium into another each boundary — solar into a channel's pool (M10c).</summary>
    Converter,

    /// <summary>Holds joules between boundaries — a placed cell, or a carried pack (M10c).</summary>
    Storage,

    /// <summary>
    /// Holds blocks, and water, inside a 1 m body that is bigger on the inside — the quantum vault
    /// (IDEAL-MACHINES-PLAN decisions 14–20, IM-3) — and, with no channel and no bill, the crate
    /// (decision 22). A home for a conserved quantity, so a kind and not an extra (decision 1).
    /// </summary>
    Vault,
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

    /// <summary>A cubic metre in millilitres — the vault's volume unit, shared by blocks and water (IM-3).</summary>
    public const long MillilitresPerCubicMetre = 1_000_000L;

    /// <summary>
    /// What one voxel takes inside a vault: a sixty-fourth of a cubic metre, 15.625 L (decision 15:
    /// blocks and water share the inside volume).
    /// </summary>
    public const long VoxelMillilitres = MillilitresPerCubicMetre / 64;

    /// <summary>The largest inside a vault may declare: the god grade's million cubic metres, an engine maximum (decision 5).</summary>
    public const long MaxInsideCubicMetres = 1_000_000L;

    /// <summary>The dearest hold a vault may declare, in milliwatts a cubic metre: a megawatt, so a full god vault's bill stays inside a long.</summary>
    public const long MaxHoldMilliwattsPerCubicMetre = 1_000_000_000L;

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

    /// <summary>A vault's inside, in millilitres — its <c>insideCubicMetres</c>. Zero for a kind that has none.</summary>
    public long InsideMillilitres { get; init; }

    /// <summary>What a cubic metre of water costs to hold, in milliwatts (decision 16). Zero for a crate.</summary>
    public long WaterHoldMilliwattsPerCubicMetre { get; init; }

    /// <summary>What a cubic metre of blocks costs to hold, in milliwatts — a tenth of water's in the shipped grades.</summary>
    public long BlockHoldMilliwattsPerCubicMetre { get; init; }

    /// <summary>Whether the vault takes water (IM-4 reads it); false for a crate (decision 22).</summary>
    public bool HoldsWater { get; init; }

    /// <summary>
    /// Whether this vault belongs to a channel's store and pays a bill from its pool: it has a power
    /// link via <c>channel</c>. A crate has none (decision 22).
    /// </summary>
    public bool IsChannelVault => Kind == MachineKind.Vault && Links.Any(l => l.IsChannel);

    public IReadOnlyList<MachineLink> Links { get; init; } = [];

    public IReadOnlyList<PortDefinition> Ports { get; init; } = [];

    /// <summary>
    /// The extras the machine opted into, each with the form it takes over the machine's medium,
    /// resolved at load from every mod's <c>extras.json</c> (IDEAL-MACHINES-PLAN decisions 2 and 3).
    /// Empty for a machine with none.
    /// </summary>
    public IReadOnlyDictionary<MachineExtra, ExtraForm> ExtraForms { get; init; } = NoExtras;

    private static readonly IReadOnlyDictionary<MachineExtra, ExtraForm> NoExtras = new Dictionary<MachineExtra, ExtraForm>();

    /// <summary>The extras as bits — what the material table projects, one byte a material.</summary>
    public MachineExtra Extras
    {
        get
        {
            MachineExtra bits = MachineExtra.None;
            foreach (MachineExtra extra in ExtraForms.Keys) bits |= extra;
            return bits;
        }
    }

    /// <summary>Whether the machine opted into <paramref name="extra"/>.</summary>
    public bool Has(MachineExtra extra) => ExtraForms.ContainsKey(extra);

    /// <summary>
    /// Whether this is a window (decision 7): a carried storage whose <c>charge</c> takes the
    /// window form, holding nothing and drawing on its channel's cells.
    /// </summary>
    public bool IsWindow => FormOf(MachineExtra.Charge) == ExtraForm.Window;

    /// <summary>The form <paramref name="extra"/> takes on this machine, or <see cref="ExtraForm.None"/>.</summary>
    public ExtraForm FormOf(MachineExtra extra) => ExtraForms.TryGetValue(extra, out ExtraForm form) ? form : ExtraForm.None;

    /// <summary>
    /// The medium a machine's extras reach over: its links' <c>via</c>, the sky aside (IM-1's pass,
    /// decision 3). Null, with the reason, when it names none or two.
    /// </summary>
    public static string? MediumOf(IReadOnlyList<MachineLink> links, out string? error)
    {
        string? medium = null;
        foreach (MachineLink link in links)
        {
            if (link.Via == SolarVia) continue;

            if (medium is null)
            {
                medium = link.Via;
            }
            else if (medium != link.Via)
            {
                error = $"an extra reaches over one medium, and this machine's links name two ('{medium}' and '{link.Via}').";
                return null;
            }
        }

        error = medium is null
            ? "an extra reaches as far as its machine's medium, and this machine names none (no link but the sky's)."
            : null;
        return medium;
    }

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
            // Decision 7: a window holds nothing, so it declares no capacity and takes no charge;
            // every other storage declares one. Refused, not zeroed, for the loader's reason.
            if (IsWindow)
            {
                if (CapacityMillijoules != 0 || RechargeWatts is not null)
                {
                    error = "a window holds nothing: it declares no capacityJoules and no rechargeWatts.";
                    return false;
                }
            }
            else if (CapacityMillijoules <= 0 || CapacityMillijoules > MaxCapacityJoules * PerMille)
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

        if (Kind == MachineKind.Vault && !TryValidateVault(out error))
        {
            return false;
        }

        return TryValidateExtras(out error);
    }

    /// <summary>
    /// The vault's rules (IM-3's pass, decisions 1 and 2): an inside within the engine's maximum;
    /// holds never negative and never over the maximum; a vault on a channel has exactly one power
    /// link, a sink, the pool its bill is paid from; a crate has no links, no bill and no water.
    /// </summary>
    private bool TryValidateVault(out string? error)
    {
        if (InsideMillilitres <= 0 || InsideMillilitres > MaxInsideCubicMetres * MillilitresPerCubicMetre)
        {
            error = $"insideCubicMetres must be more than 0 and at most {MaxInsideCubicMetres:N0} for a vault.";
            return false;
        }

        if (WaterHoldMilliwattsPerCubicMetre is < 0 or > MaxHoldMilliwattsPerCubicMetre
            || BlockHoldMilliwattsPerCubicMetre is < 0 or > MaxHoldMilliwattsPerCubicMetre)
        {
            error = $"holdWattsPerCubicMetre must be within 0..{MaxHoldMilliwattsPerCubicMetre / PerMille:N0} for water and for blocks.";
            return false;
        }

        if (Carried)
        {
            error = "a vault is a body; it cannot be carried.";
            return false;
        }

        if (Links.Count == 0)
        {
            // The crate: no channel, so no store, no bill and no fuse — a box you open by hand.
            if (WaterHoldMilliwattsPerCubicMetre != 0 || BlockHoldMilliwattsPerCubicMetre != 0 || HoldsWater)
            {
                error = "a vault with no links is a crate: it holds blocks only and bills nothing, so it declares no hold and no water.";
                return false;
            }

            error = null;
            return true;
        }

        if (Links.Count != 1 || Links[0] is not { Carries: MachineMedium.Power, Role: MachineRole.Sink, IsChannel: true })
        {
            error = "a vault declares exactly one link, a power sink via channel — the pool its bill is paid from — or none, as a crate.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// The extras' rules that need no table (IM-1's pass, decisions 1, 3 and 4): each a known extra
    /// in one of its own forms; <c>charge</c> a carried storage's and the other five a placed
    /// machine's; the port an extra reads declared at its exact bounds; one medium to reach over.
    /// Whether this build serves an extra is the loader's question, asked last.
    /// </summary>
    private bool TryValidateExtras(out string? error)
    {
        if (ExtraForms.Count == 0)
        {
            error = null;
            return true;
        }

        foreach ((MachineExtra extra, ExtraForm form) in ExtraForms)
        {
            if (!MachineExtras.Each.Contains(extra))
            {
                error = $"extra {extra} is not one extra.";
                return false;
            }

            string name = MachineExtras.NameOf(extra);
            if (!MachineExtras.IsFormOf(extra, form))
            {
                error = $"extra '{name}' takes form {form}, which is not one of its own.";
                return false;
            }

            if (MachineExtras.IsCarriedExtra(extra) != Carried)
            {
                error = Carried
                    ? $"extra '{name}' is a placed machine's, and a carried storage has no body, no row and no pin."
                    : $"extra '{name}' is a carried storage's; a placed machine is not a window.";
                return false;
            }

            if (MachineExtras.RequiredPort(extra) is { } required)
            {
                if (Port(required.Name) is not { } port || port.Min != required.Min || port.Max != required.Max)
                {
                    error = $"extra '{name}' reads a port '{required.Name}' declared {required.Min}..{required.Max}; declare it with exactly those bounds.";
                    return false;
                }
            }
        }

        return MediumOf(Links, out error) is not null;
    }

    /// <summary>Whether this build's tick can serve a <c>via</c>, and if not which part will.</summary>
    public static bool ViaIsServed(string via, out string? error) => ViaIsServed(via, "via", out error);

    /// <summary>
    /// <see cref="ViaIsServed(string, out string?)"/>, with the messages naming <paramref name="noun"/>:
    /// an extra's medium is checked by the same rule (IM-1's pass, decision 3).
    /// </summary>
    public static bool ViaIsServed(string via, string noun, out string? error)
    {
        if (string.IsNullOrEmpty(via))
        {
            error = $"{noun} is empty.";
            return false;
        }

        if (ServedVia.Contains(via))
        {
            error = null;
            return true;
        }

        if (ReservedVia.Contains(via) || via.StartsWith(AdjacentViaPrefix, StringComparison.Ordinal))
        {
            error = $"{noun} '{via}' is an engine rule this build does not serve yet (M10f).";
            return false;
        }

        if (!Identifier.IsValidNamespace(via))
        {
            error = $"{noun} '{via}' is not a rule name: lowercase letters, digits, '_', '-' or '.'.";
            return false;
        }

        error = $"{noun} '{via}' is a conductor class, and the conductor index is M10e.";
        return false;
    }
}
