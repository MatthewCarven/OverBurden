using System.Diagnostics.CodeAnalysis;

namespace MinerCraft.Core.Content;

/// <summary>
/// A namespaced content ID — <c>minercraft:stone</c>, <c>deepdig:copper_ore</c>.
/// </summary>
/// <remarks>
/// This is the authoritative identity of everything in the game. It is what appears in
/// JSON, in scripts, and in the world file's ID table; numeric IDs are a runtime
/// convenience layered on top and are never the source of truth.
/// <para>
/// The namespace is what stops two mods that both define "copper_ore" from colliding,
/// which is the entire reason a bare string or an enum will not do.
/// </para>
/// </remarks>
public readonly struct Identifier : IEquatable<Identifier>
{
    /// <summary>Namespace used when a mod's content omits one — the owning mod's ID.</summary>
    public const string BaseNamespace = "minercraft";

    public string Namespace { get; }
    public string Path { get; }

    /// <summary>False for <c>default(Identifier)</c>, which carries no strings at all.</summary>
    public bool IsValid => Namespace is not null;

    public Identifier(string @namespace, string path)
    {
        if (!IsValidNamespace(@namespace))
        {
            throw new ArgumentException(
                $"Invalid identifier namespace '{@namespace}'. Allowed: lowercase a-z, 0-9, '_', '-', '.'",
                nameof(@namespace));
        }

        if (!IsValidPath(path))
        {
            throw new ArgumentException(
                $"Invalid identifier path '{path}'. Allowed: lowercase a-z, 0-9, '_', '-', '.', '/'",
                nameof(path));
        }

        Namespace = @namespace;
        Path = path;
    }

    /// <summary>
    /// Parses <c>namespace:path</c>, or a bare <c>path</c> against
    /// <paramref name="defaultNamespace"/>.
    /// </summary>
    /// <remarks>
    /// The default-namespace form is what lets a mod write <c>"id": "copper_ore"</c> in
    /// its own files and have it land in that mod's namespace automatically — a mod
    /// cannot accidentally register into someone else's namespace, but can do so
    /// deliberately by writing the colon form, which is how compatibility patches work.
    /// </remarks>
    public static Identifier Parse(string text, string defaultNamespace = BaseNamespace)
    {
        if (!TryParse(text, defaultNamespace, out var id, out string? error))
        {
            throw new FormatException(error);
        }

        return id;
    }

    public static bool TryParse(string? text, string defaultNamespace, out Identifier identifier) =>
        TryParse(text, defaultNamespace, out identifier, out _);

    public static bool TryParse(
        string? text,
        string defaultNamespace,
        out Identifier identifier,
        [NotNullWhen(false)] out string? error)
    {
        identifier = default;

        if (string.IsNullOrEmpty(text))
        {
            error = "Identifier is empty.";
            return false;
        }

        int colon = text.IndexOf(':');
        if (colon != text.LastIndexOf(':'))
        {
            error = $"Identifier '{text}' has more than one ':'.";
            return false;
        }

        string ns = colon < 0 ? defaultNamespace : text[..colon];
        string path = colon < 0 ? text : text[(colon + 1)..];

        if (!IsValidNamespace(ns))
        {
            error = $"Invalid namespace '{ns}' in identifier '{text}'. " +
                    "Allowed: lowercase a-z, 0-9, '_', '-', '.'";
            return false;
        }

        if (!IsValidPath(path))
        {
            error = $"Invalid path '{path}' in identifier '{text}'. " +
                    "Allowed: lowercase a-z, 0-9, '_', '-', '.', '/'";
            return false;
        }

        identifier = new Identifier(ns, path);
        error = null;
        return true;
    }

    public static bool IsValidNamespace(string? value) =>
        !string.IsNullOrEmpty(value) && value.All(IsNamespaceChar);

    public static bool IsValidPath(string? value) =>
        !string.IsNullOrEmpty(value) && value.All(c => IsNamespaceChar(c) || c == '/');

    // Lowercase only, deliberately. Case-insensitive comparison across cultures is a
    // source of genuinely surprising bugs (Turkish dotless i being the famous one), and
    // forbidding uppercase outright avoids the question.
    private static bool IsNamespaceChar(char c) =>
        c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.';

    public bool Equals(Identifier other) =>
        string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
        && string.Equals(Path, other.Path, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Identifier other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Namespace, Path);

    public static bool operator ==(Identifier left, Identifier right) => left.Equals(right);

    public static bool operator !=(Identifier left, Identifier right) => !left.Equals(right);

    public override string ToString() => IsValid ? $"{Namespace}:{Path}" : "<invalid>";
}
