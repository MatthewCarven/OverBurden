using System.Text.Json;

namespace Overburden.Core.Content;

/// <summary>Outcome of a content load. Errors are collected, not thrown.</summary>
/// <remarks>
/// One mod with a malformed file must not stop the game from starting — that is the
/// difference between a bad mod being diagnosable and a bad mod being a black screen.
/// Only a failure that makes the world unusable (no air definition) is fatal.
/// </remarks>
public sealed class LoadResult
{
    public required IReadOnlyList<ModManifest> Mods { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
    public required int MaterialsLoaded { get; init; }

    /// <summary>
    /// Tool tiers registered. Zero is a playable world you cannot mine in, not a load
    /// failure — a mod set that only adds materials is legitimate.
    /// </summary>
    /// <remarks>
    /// <c>required</c>, like <see cref="MaterialsLoaded"/>, so that an early return cannot
    /// forget it and report zero tools on a path that never tried to load any. A diagnostic
    /// that lies is worse than one that is missing.
    /// </remarks>
    public required int ToolsLoaded { get; init; }

    public bool Success => Errors.Count == 0;

    public override string ToString() =>
        $"{Mods.Count} mod(s), {MaterialsLoaded} material(s), {ToolsLoaded} tool(s), " +
        $"{Errors.Count} error(s)";
}

/// <summary>
/// Populates the registries from <c>mods/</c>.
/// </summary>
/// <remarks>
/// The base game is loaded through this same path, from <c>mods/minercraft/</c>, with no
/// privileged shortcut. That is the dogfood rule from docs/DESIGN.md: if stone cannot be
/// expressed the way a third-party mod would express it, the format is wrong, and we find
/// out now rather than at M12.
/// </remarks>
public sealed class ContentLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public LoadResult Load(
        IContentSource source,
        Registry<MaterialDefinition> materials,
        Registry<ToolDefinition> tools)
    {
        var errors = new List<string>();

        // Pin air to 0 before anything else can take the slot. This reserves the number
        // only — air's properties still have to arrive from JSON below.
        int airId = materials.ReserveId(MaterialIds.Air);
        if (airId != MaterialIds.AirNumericId)
        {
            errors.Add(
                $"'{MaterialIds.Air}' resolved to numeric ID {airId}, expected {MaterialIds.AirNumericId}. " +
                "The world's saved ID table is inconsistent — refusing to load rather than " +
                "silently reinterpreting every empty voxel.");

            return new LoadResult
            {
                Mods = [], Errors = errors, MaterialsLoaded = 0, ToolsLoaded = 0,
            };
        }

        var manifests = ReadManifests(source, errors);
        ValidateDependencies(manifests, errors);

        int materialCount = 0;
        int toolCount = 0;
        foreach (var manifest in manifests)
        {
            materialCount += LoadMaterials(source, manifest, materials, errors);
            toolCount += LoadTools(source, manifest, tools, errors);
        }

        if (!materials.Contains(MaterialIds.Air) || materials.IsPlaceholder(MaterialIds.AirNumericId))
        {
            errors.Add(
                $"No definition for '{MaterialIds.Air}'. It must exist in " +
                $"mods/{Identifier.BaseNamespace}/materials/.");
        }

        materials.Freeze();
        tools.Freeze();

        return new LoadResult
        {
            Mods = manifests,
            Errors = errors,
            MaterialsLoaded = materialCount,
            ToolsLoaded = toolCount,
        };
    }

    /// <summary>
    /// Reads every <c>mod.json</c> and orders them: base game first, then by ID.
    /// </summary>
    /// <remarks>
    /// Alphabetical after the base game is a placeholder for real dependency ordering.
    /// It is deterministic, which is the property that actually matters right now —
    /// numeric IDs are assigned in load order for content a world has not seen before,
    /// so a non-deterministic order would hand out different IDs on different runs.
    /// </remarks>
    private static List<ModManifest> ReadManifests(IContentSource source, List<string> errors)
    {
        var manifests = new List<ModManifest>();

        foreach (string modId in source.ListMods())
        {
            string? json = source.ReadText(modId, "mod.json");
            if (json is null)
            {
                errors.Add($"Mod '{modId}' has no mod.json; skipped.");
                continue;
            }

            ModManifestJson? dto;
            try
            {
                dto = JsonSerializer.Deserialize<ModManifestJson>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                errors.Add($"Mod '{modId}': mod.json is not valid JSON — {ex.Message}");
                continue;
            }

            if (dto?.Id is null)
            {
                errors.Add($"Mod '{modId}': mod.json has no 'id'.");
                continue;
            }

            if (!Identifier.IsValidNamespace(dto.Id))
            {
                errors.Add(
                    $"Mod '{modId}': id '{dto.Id}' is not a valid namespace " +
                    "(lowercase a-z, 0-9, '_', '-', '.').");
                continue;
            }

            if (dto.Id != modId)
            {
                errors.Add(
                    $"Mod directory '{modId}' declares id '{dto.Id}'; they must match so a " +
                    "mod's namespace is predictable from its folder.");
                continue;
            }

            manifests.Add(new ModManifest
            {
                Id = dto.Id,
                Name = dto.Name ?? dto.Id,
                Version = dto.Version ?? "0.0.0",
                // A dependency names a namespace, so a mod written when the base game was
                // minercraft still finds it. The mod's own ID is not aliased: its folder is its
                // namespace, and a folder called minercraft would read its files from nowhere.
                Dependencies = (dto.Dependencies ?? []).Select(Identifier.CanonicalNamespace).ToList(),
            });
        }

        return manifests
            .OrderByDescending(m => m.IsBaseGame)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static void ValidateDependencies(List<ModManifest> manifests, List<string> errors)
    {
        var present = manifests.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var manifest in manifests)
        {
            foreach (string dependency in manifest.Dependencies)
            {
                if (!present.Contains(dependency))
                {
                    errors.Add($"Mod '{manifest.Id}' requires '{dependency}', which is not installed.");
                }
            }
        }
    }

    private static int LoadMaterials(
        IContentSource source,
        ModManifest manifest,
        Registry<MaterialDefinition> materials,
        List<string> errors)
    {
        int loaded = 0;

        foreach (string path in source.ListFiles(manifest.Id, "materials", ".json"))
        {
            string? json = source.ReadText(manifest.Id, path);
            if (json is null) continue;

            MaterialJson? dto;
            try
            {
                dto = JsonSerializer.Deserialize<MaterialJson>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                errors.Add($"{manifest.Id}/{path}: not valid JSON — {ex.Message}");
                continue;
            }

            if (dto?.Id is null)
            {
                errors.Add($"{manifest.Id}/{path}: no 'id'.");
                continue;
            }

            // A bare id lands in the mod's own namespace; the explicit ns:path form is
            // how a compatibility patch deliberately targets another mod's namespace.
            if (!Identifier.TryParse(dto.Id, manifest.Id, out var id, out string? parseError))
            {
                errors.Add($"{manifest.Id}/{path}: {parseError}");
                continue;
            }

            int emission = dto.LightEmission ?? 0;

            // Refused rather than clamped, exactly as an out-of-range tool radius is. A lamp
            // whose JSON says 30 was written by someone who believed the range was wider, and
            // silently giving them 15 hides the misunderstanding until they wonder why two
            // different lamps look identical.
            if (emission < 0 || emission > MaterialDefinition.MaxLightEmission)
            {
                errors.Add(
                    $"{manifest.Id}/{path}: lightEmission must be within "
                    + $"0..{MaterialDefinition.MaxLightEmission}.");
                continue;
            }

            var definition = new MaterialDefinition
            {
                Id = id,
                Hardness = dto.Hardness ?? 1f,
                SupportDistance = dto.SupportDistance ?? 0,
                Permeability = dto.Permeability ?? 0f,
                Density = dto.Density ?? 1000f,
                TextureLayer = dto.TextureLayer ?? 0,
                LightEmission = emission,
                IsOpaque = dto.Opaque ?? true,
                IsFluid = dto.Fluid ?? false,
                IsReplaceable = dto.Replaceable ?? false,
            };

            try
            {
                materials.Register(id, definition);
                loaded++;
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"{manifest.Id}/{path}: {ex.Message}");
            }
        }

        return loaded;
    }

    /// <summary>
    /// Registers one mod's tool tiers from <c>tools/*.json</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately the same shape as <see cref="LoadMaterials"/>, down to the bare-id
    /// namespacing rule, because "tools load like materials do" is the property that makes
    /// the format learnable — and because the moment the two diverge is the moment one of
    /// them has grown a special case the mod API cannot express.
    /// </remarks>
    private static int LoadTools(
        IContentSource source,
        ModManifest manifest,
        Registry<ToolDefinition> tools,
        List<string> errors)
    {
        int loaded = 0;

        foreach (string path in source.ListFiles(manifest.Id, "tools", ".json"))
        {
            string? json = source.ReadText(manifest.Id, path);
            if (json is null) continue;

            ToolJson? dto;
            try
            {
                dto = JsonSerializer.Deserialize<ToolJson>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                errors.Add($"{manifest.Id}/{path}: not valid JSON — {ex.Message}");
                continue;
            }

            if (dto?.Id is null)
            {
                errors.Add($"{manifest.Id}/{path}: no 'id'.");
                continue;
            }

            if (!Identifier.TryParse(dto.Id, manifest.Id, out var id, out string? parseError))
            {
                errors.Add($"{manifest.Id}/{path}: {parseError}");
                continue;
            }

            float radius = dto.RadiusMetres ?? 0.5f;
            float threshold = dto.HardnessThreshold ?? 0f;
            float cooldown = dto.CooldownSeconds ?? 0.25f;

            // Rejected at the door rather than clamped, so that every tool the registry
            // hands out is one the carve can be swung with unconditionally. A NaN radius
            // clamped to something sensible would hide the typo; a NaN radius reaching
            // SphereCarve would be an exception from inside a swing, several layers from
            // the file that caused it.
            if (!float.IsFinite(radius) || radius < 0f || radius > ToolDefinition.MaxRadiusMetres)
            {
                errors.Add(
                    $"{manifest.Id}/{path}: radiusMetres must be finite and within " +
                    $"0..{ToolDefinition.MaxRadiusMetres} m.");
                continue;
            }

            if (!float.IsFinite(threshold))
            {
                errors.Add($"{manifest.Id}/{path}: hardnessThreshold must be finite.");
                continue;
            }

            if (!float.IsFinite(cooldown) || cooldown < 0f)
            {
                errors.Add($"{manifest.Id}/{path}: cooldownSeconds must be finite and not negative.");
                continue;
            }

            try
            {
                tools.Register(id, new ToolDefinition
                {
                    Id = id,
                    RadiusMetres = radius,
                    HardnessThreshold = threshold,
                    CooldownSeconds = cooldown,
                });

                loaded++;
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"{manifest.Id}/{path}: {ex.Message}");
            }
        }

        return loaded;
    }

    private sealed class ModManifestJson
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public List<string>? Dependencies { get; set; }
    }

    private sealed class MaterialJson
    {
        public string? Id { get; set; }
        public float? Hardness { get; set; }
        public int? SupportDistance { get; set; }
        public float? Permeability { get; set; }
        public float? Density { get; set; }
        public int? TextureLayer { get; set; }
        public int? LightEmission { get; set; }
        public bool? Opaque { get; set; }
        public bool? Fluid { get; set; }
        public bool? Replaceable { get; set; }
    }

    private sealed class ToolJson
    {
        public string? Id { get; set; }
        public float? RadiusMetres { get; set; }
        public float? HardnessThreshold { get; set; }
        public float? CooldownSeconds { get; set; }
    }
}
