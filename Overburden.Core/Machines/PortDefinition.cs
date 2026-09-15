using Overburden.Core.Content;

namespace Overburden.Core.Machines;

/// <summary>
/// One programmable integer a machine declares: its name, its default and its bounds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Integers, bounded at load <em>and</em> at use</b> (M10-PLAN decision 6; the ladder's
/// decision 21). Integers because a NaN cannot happen and an integer has one stored form; bounded
/// at use because the loader is not the only caller — a row read from disk is a caller too, and
/// M3c's tool-radius rule applies: a value outside its bounds is clamped and counted, never
/// refused, because refusing a row is refusing the world. A port that wants a fraction declares
/// its unit smaller.
/// </para>
/// <para>
/// <b>The name is in the identifier's path charset</b> — lowercase, digits, <c>_</c>, <c>-</c>,
/// <c>.</c> — so a port's label is derived from its name the way a material's is from its
/// identifier (M6d-3's rule), and a stored name can never be a string the loader would not accept.
/// </para>
/// </remarks>
public readonly record struct PortDefinition
{
    /// <summary>The most ports one machine may declare, so a row's port count fits a byte with room.</summary>
    public const int MaxPorts = 16;

    /// <summary>The longest port name, so a stored name's length fits a byte.</summary>
    public const int MaxNameLength = 32;

    public PortDefinition(string name, long @default, long min, long max)
    {
        if (!IsValidName(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a port name: 1..{MaxNameLength} of lowercase letters, digits, '_', '-' or '.'.",
                nameof(name));
        }

        if (min > max || @default < min || @default > max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(@default),
                $"Port '{name}': min {min} <= default {@default} <= max {max} does not hold.");
        }

        Name = name;
        Default = @default;
        Min = min;
        Max = max;
    }

    public string Name { get; }

    public long Default { get; }

    public long Min { get; }

    public long Max { get; }

    /// <summary>The nearest value inside the bounds.</summary>
    public long Clamp(long value) => Math.Clamp(value, Min, Max);

    /// <summary>Whether <paramref name="name"/> could name a port.</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= MaxNameLength
        && Identifier.IsValidNamespace(name);

    public override string ToString() => $"{Name} = {Default} [{Min}..{Max}]";
}
