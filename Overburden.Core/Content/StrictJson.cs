using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Overburden.Core.Content;

/// <summary>
/// The strict half of the loader (IM-1, decision 6): walks a file against the class it binds to,
/// and refuses every key that class does not read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a walk and not <c>JsonUnmappedMemberHandling.Disallow</c>.</b> Probed on .NET 8.0.31
/// (IDEAL-MACHINES-PLAN, "IM-1 — the code-level pass", fact 4): the built-in mode stops at the first
/// unknown key, names the loader's private class, refuses <c>$comment</c> too, and suggests nothing.
/// And two silent paths it leaves open: a key given twice loads the second value (<c>"kind"</c>
/// against <c>"Kind"</c> included), and a <c>$comment</c> inside a name-keyed map is read as an
/// entry and throws. A misspelt ceiling key that loads silently is a ceiling that isn't there,
/// which is the whole reason the loader became strict.
/// </para>
/// <para>
/// <b>The known keys are the binding's own</b>: each level's public settable properties, read by
/// reflection, so the walk and the deserialiser cannot disagree about what a file may say. Case is
/// ignored, as the binding ignores it — strict about names, not their case.
/// </para>
/// </remarks>
internal static class StrictJson
{
    /// <summary>A key starting with this is a note to the reader (<c>$comment</c>), allowed and dropped at every level.</summary>
    public const char NotePrefix = '$';

    /// <summary>The document options matching the loader's serialiser options: comments skipped, trailing commas allowed.</summary>
    public static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly ConcurrentDictionary<Type, Level> Levels = new();

    /// <summary>
    /// Walks <paramref name="json"/> against <paramref name="type"/>, adding a problem for every
    /// unknown key and every key given twice, and returns the text the deserialiser should read:
    /// the original when no note was dropped, so its messages keep their line numbers.
    /// </summary>
    /// <exception cref="JsonException">The text is not JSON — the caller reports it as the deserialiser would.</exception>
    public static string Clean(string json, Type type, List<string> problems)
    {
        using JsonDocument document = JsonDocument.Parse(json, DocumentOptions);
        var buffer = new ArrayBufferWriter<byte>();
        bool dropped = false;
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Walk(document.RootElement, type, string.Empty, writer, problems, ref dropped);
        }

        return dropped ? Encoding.UTF8.GetString(buffer.WrittenSpan) : json;
    }

    /// <summary>
    /// The candidate nearest <paramref name="name"/>, case ignored, if it is within two edits (one
    /// for a name of four letters or fewer); otherwise null. Ties go to the ordinal first.
    /// </summary>
    public static string? Nearest(string name, IEnumerable<string> candidates)
    {
        int allowed = name.Length <= 4 ? 1 : 2;
        string lowered = name.ToLowerInvariant();
        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (string candidate in candidates)
        {
            int distance = Distance(lowered, candidate.ToLowerInvariant());
            if (distance < bestDistance || (distance == bestDistance && string.CompareOrdinal(candidate, best) < 0))
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return bestDistance <= allowed ? best : null;
    }

    /// <summary>" — did you mean 'x'?" when a candidate is near, else the empty string.</summary>
    public static string Suggest(string name, IEnumerable<string> candidates) =>
        Nearest(name, candidates) is { } near ? $" — did you mean '{near}'?" : string.Empty;

    private static void Walk(
        JsonElement element, Type type, string path, Utf8JsonWriter writer, List<string> problems, ref bool dropped)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (element.ValueKind == JsonValueKind.Object && MapValue(type) is { } valueType)
        {
            // A name-keyed map (links, ports, extras, the extras table): any name is a name, and
            // the rules for names are the loader's. Only notes and exact repeats are the walk's.
            writer.WriteStartObject();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (IsNote(property.Name))
                {
                    dropped = true;
                    continue;
                }

                string at = Join(path, property.Name);
                if (!seen.Add(property.Name))
                {
                    problems.Add($"'{at}' is given twice.");
                    continue;
                }

                writer.WritePropertyName(property.Name);
                Walk(property.Value, valueType, at, writer, problems, ref dropped);
            }

            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind == JsonValueKind.Object && IsRecord(type))
        {
            Level level = Levels.GetOrAdd(type, Level.Of);
            writer.WriteStartObject();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (IsNote(property.Name))
                {
                    dropped = true;
                    continue;
                }

                string at = Join(path, property.Name);
                if (!level.Keys.TryGetValue(property.Name, out Type? target))
                {
                    problems.Add(Unknown(at, property.Name, level));
                    continue;
                }

                if (!seen.Add(property.Name))
                {
                    problems.Add($"'{at}' is given twice; the second would load over the first without a word.");
                    continue;
                }

                writer.WritePropertyName(property.Name);
                Walk(property.Value, target, at, writer, problems, ref dropped);
            }

            writer.WriteEndObject();
            return;
        }

        if (element.ValueKind == JsonValueKind.Array && ListItem(type) is { } itemType)
        {
            writer.WriteStartArray();
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                Walk(item, itemType, $"{path}[{index}]", writer, problems, ref dropped);
                index++;
            }

            writer.WriteEndArray();
            return;
        }

        // A value, or a shape the class does not expect: written as it stands, so the
        // deserialiser reports the mismatch in its own words, with the path.
        element.WriteTo(writer);
    }

    private static string Unknown(string at, string name, Level level)
    {
        if (level.Display.Count == 0)
        {
            return $"unknown key '{at}'; this object takes no keys yet.";
        }

        return Nearest(name, level.Display) is { } near
            ? $"unknown key '{at}' — did you mean '{near}'?"
            : $"unknown key '{at}'; it is not a key this build reads here ({string.Join(", ", level.Display)}).";
    }

    private static bool IsNote(string name) => name.Length > 0 && name[0] == NotePrefix;

    private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    private static bool IsRecord(Type type) =>
        type.IsClass && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type);

    private static Type? MapValue(Type type) =>
        type.IsGenericType
        && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)
        && type.GetGenericArguments()[0] == typeof(string)
            ? type.GetGenericArguments()[1]
            : null;

    private static Type? ListItem(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
            ? type.GetGenericArguments()[0]
            : null;
    }

    /// <summary>Levenshtein distance, two rows.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int substitute = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitute, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>One class's keys: its public settable properties by name, case ignored, and their camelCase spellings for messages.</summary>
    private sealed class Level
    {
        public required Dictionary<string, Type> Keys { get; init; }

        public required IReadOnlyList<string> Display { get; init; }

        public static Level Of(Type type)
        {
            var keys = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            var display = new List<string>();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanWrite || property.GetCustomAttribute<JsonIgnoreAttribute>() is not null) continue;

                string name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                              ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                keys[name] = property.PropertyType;
                display.Add(name);
            }

            return new Level { Keys = keys, Display = display };
        }
    }
}
