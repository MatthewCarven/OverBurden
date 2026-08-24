namespace MinerCraft.Core.Content;

/// <summary>
/// A mod's <c>mod.json</c>. The mod's <see cref="Id"/> is also the namespace its content
/// registers into by default.
/// </summary>
public sealed class ModManifest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }

    /// <summary>
    /// Mod IDs this one needs. Currently validated for presence only — a missing
    /// dependency is a load error — but <b>not</b> topologically sorted. Real load-order
    /// resolution arrives with scripting at M12, when mods can actually conflict.
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];

    /// <summary>True for the base game, which loads before everything else.</summary>
    public bool IsBaseGame => Id == Identifier.BaseNamespace;

    public override string ToString() => $"{Id} {Version}";
}
