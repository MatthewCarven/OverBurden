namespace Overburden.Core.Content;

/// <summary>
/// Where mod content is read from.
/// </summary>
/// <remarks>
/// An interface rather than direct file access so the loader can be tested without
/// touching disk, and so the base game can later ship embedded or packed rather than as
/// loose files — an export-packaging problem we have deferred, not solved.
/// </remarks>
public interface IContentSource
{
    /// <summary>Directory names under <c>mods/</c>, in no particular order.</summary>
    IEnumerable<string> ListMods();

    /// <summary>File contents, or null if it does not exist.</summary>
    string? ReadText(string modId, string relativePath);

    /// <summary>
    /// Relative paths of files directly inside <paramref name="relativeDirectory"/> with
    /// the given extension. Empty if the directory is absent.
    /// </summary>
    IEnumerable<string> ListFiles(string modId, string relativeDirectory, string extension);
}

/// <summary>Reads mods from a directory on disk.</summary>
public sealed class DirectoryContentSource(string rootDirectory) : IContentSource
{
    public string RootDirectory { get; } = rootDirectory;

    public IEnumerable<string> ListMods()
    {
        if (!Directory.Exists(RootDirectory)) return [];

        return Directory.EnumerateDirectories(RootDirectory)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!);
    }

    public string? ReadText(string modId, string relativePath)
    {
        string full = Path.Combine(RootDirectory, modId, relativePath);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    public IEnumerable<string> ListFiles(string modId, string relativeDirectory, string extension)
    {
        string full = Path.Combine(RootDirectory, modId, relativeDirectory);
        if (!Directory.Exists(full)) return [];

        return Directory.EnumerateFiles(full, "*" + extension)
            .Select(path => Path.Combine(relativeDirectory, Path.GetFileName(path)))
            .OrderBy(path => path, StringComparer.Ordinal);
    }
}

/// <summary>In-memory content source, for tests.</summary>
public sealed class InMemoryContentSource : IContentSource
{
    // modId -> relativePath -> contents
    private readonly Dictionary<string, Dictionary<string, string>> _mods = [];

    public InMemoryContentSource Add(string modId, string relativePath, string contents)
    {
        if (!_mods.TryGetValue(modId, out var files))
        {
            files = [];
            _mods[modId] = files;
        }

        files[relativePath.Replace('\\', '/')] = contents;
        return this;
    }

    public void RemoveMod(string modId) => _mods.Remove(modId);

    public IEnumerable<string> ListMods() => _mods.Keys;

    public string? ReadText(string modId, string relativePath) =>
        _mods.TryGetValue(modId, out var files)
        && files.TryGetValue(relativePath.Replace('\\', '/'), out string? text)
            ? text
            : null;

    public IEnumerable<string> ListFiles(string modId, string relativeDirectory, string extension)
    {
        if (!_mods.TryGetValue(modId, out var files)) return [];

        string prefix = relativeDirectory.Replace('\\', '/').TrimEnd('/') + "/";

        return files.Keys
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal)
                           && path.EndsWith(extension, StringComparison.Ordinal)
                           && !path[prefix.Length..].Contains('/'))
            .OrderBy(path => path, StringComparer.Ordinal);
    }
}
