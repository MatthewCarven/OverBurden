namespace Overburden.Core.Content;

/// <summary>
/// A material's or a tool's name on screen, derived from its identifier's path (M6d-3's rule: labels are derived,
/// never authored): underscores read as spaces, and since IM-6b a path <c>a/b</c> reads <c>a (b)</c>.
/// </summary>
/// <remarks>
/// <b>The grade rule</b> (IDEAL-MACHINES-PLAN decision 18; IM-6's pass, decision 6 and fact 1): the vault's grades
/// live in their IDs, <c>quantum_vault/low</c> to <c>quantum_vault/god</c>, and his words put the grade in brackets —
/// "quantum vault (god)". The format has no label key, and this keeps it that way: every path segment after the first
/// is read into one bracket, spaced, so a deeper path stays readable rather than showing its slashes.
/// </remarks>
public static class ContentLabel
{
    /// <summary>The label for <paramref name="id"/>: <c>hand_pick</c> is "hand pick", <c>quantum_vault/god</c> is "quantum vault (god)".</summary>
    public static string Of(Identifier id) => OfPath(id.Path);

    /// <summary>The label for an identifier's path.</summary>
    public static string OfPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string spaced = path.Replace('_', ' ');
        int slash = spaced.IndexOf('/');
        if (slash < 0)
        {
            return spaced;
        }

        string head = spaced[..slash];
        string rest = spaced[(slash + 1)..].Replace('/', ' ');
        return rest.Length == 0 ? head : $"{head} ({rest})";
    }
}
