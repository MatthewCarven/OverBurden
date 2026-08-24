namespace MinerCraft.Core.Content;

/// <summary>
/// Identifiers the engine itself depends on.
/// </summary>
/// <remarks>
/// Keep this list as short as it can possibly be. Every entry here is a place the engine
/// knows a specific material by name rather than by its properties, which is exactly the
/// coupling the registry exists to avoid. <see cref="Air"/> earns its place; almost
/// nothing else should.
/// </remarks>
public static class MaterialIds
{
    /// <summary>
    /// Empty space. Guaranteed numeric ID <see cref="AirNumericId"/>.
    /// </summary>
    public static readonly Identifier Air = new(Identifier.BaseNamespace, "air");

    /// <summary>
    /// Air is always 0, in every world, forever.
    /// </summary>
    /// <remarks>
    /// Two things depend on it: a zero-initialised array is a valid empty chunk with no
    /// fill step, and the uniform-chunk fast path can test emptiness against a constant.
    /// The number is reserved before content loads; the <i>definition</i> still has to
    /// come from <c>mods/minercraft/materials/air.json</c> like anything else, so this
    /// is not a privileged content path.
    /// </remarks>
    public const int AirNumericId = 0;
}
