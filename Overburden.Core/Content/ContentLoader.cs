using System.Text.Json;
using Overburden.Core.Machines;

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

            float permeability = dto.Permeability ?? 0f;

            // Refused rather than clamped, as lightEmission is. A mod writing 20 meant "very
            // permeable" against a range it believed was wider; clamping it to 1 would make sand
            // and gravel read identical and nobody would know why. NaN cannot come out of JSON,
            // but a definition can be built without the loader, and the table seals it there.
            if (float.IsNaN(permeability) || permeability < 0f || permeability > 1f)
            {
                errors.Add($"{manifest.Id}/{path}: permeability must be within 0..1.");
                continue;
            }

            int supportDistance = dto.SupportDistance ?? 0;

            // Refused rather than clamped, as the others are. The field is a byte and a step costs
            // round(120 / distance): a mod's 1000 rounds to zero and is a roof that never falls,
            // and whoever wrote it believed the range was wider.
            if (supportDistance < 0 || supportDistance > MaterialDefinition.MaxSupportDistance)
            {
                errors.Add(
                    $"{manifest.Id}/{path}: supportDistance must be within "
                    + $"0..{MaterialDefinition.MaxSupportDistance}.");
                continue;
            }

            if (!TryReadSupportCurve(dto.SupportUnderHead, supportDistance, out var curve, out string? curveError))
            {
                errors.Add($"{manifest.Id}/{path}: {curveError}");
                continue;
            }

            if (!TryReadUnit(dto.Unit, out MaterialUnit unit, out string? unitError))
            {
                errors.Add($"{manifest.Id}/{path}: {unitError}");
                continue;
            }

            MachineDefinition? machine = null;
            if (dto.Machine is not null)
            {
                if (dto.Fluid == true)
                {
                    errors.Add($"{manifest.Id}/{path}: a fluid cannot carry a machine block; a body is solid.");
                    continue;
                }

                if (!TryReadMachine(dto.Machine, out machine, out string? machineError))
                {
                    errors.Add($"{manifest.Id}/{path}: machine: {machineError}");
                    continue;
                }

                // A machine is a body the brush places (M10a); a machine nobody can place is a row
                // nothing can create — unless it is carried (M10c), a pack in the hand that is an
                // item by the same rule the computer is, and then it must be. Refused at load
                // naming the file, like every other rule the format has.
                bool carried = machine!.Carried;
                if (dto.Placeable == false && !carried)
                {
                    errors.Add($"{manifest.Id}/{path}: a machine cannot be \"placeable\": false; its body is placed by the brush.");
                    continue;
                }

                if (carried && dto.Placeable != false)
                {
                    errors.Add($"{manifest.Id}/{path}: a carried storage must be \"placeable\": false; it is an item in the hand, never a body.");
                    continue;
                }
            }

            var definition = new MaterialDefinition
            {
                Id = id,
                Hardness = dto.Hardness ?? 1f,
                SupportDistance = supportDistance,
                SupportUnderHead = curve,
                Permeability = permeability,
                Density = dto.Density ?? 1000f,
                TextureLayer = dto.TextureLayer ?? 0,
                LightEmission = emission,
                IsOpaque = dto.Opaque ?? true,
                IsFluid = dto.Fluid ?? false,
                IsReplaceable = dto.Replaceable ?? false,
                IsPlaceable = dto.Placeable ?? true,
                Machine = machine,
                Unit = unit,
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

            // The bite (2026-09-19): absent is the sphere, the picks' shape; a value this build
            // does not know is refused naming the file, for the via rule's reason — a shape the
            // carve cannot swing would otherwise load as a sphere and read as a typo nobody made.
            ToolBite bite = ToolBite.Sphere;
            if (dto.Bite is { } biteName)
            {
                switch (biteName)
                {
                    case "sphere": bite = ToolBite.Sphere; break;
                    case "cube": bite = ToolBite.Cube; break;
                    case "chunk": bite = ToolBite.Chunk; break;
                    default:
                        errors.Add($"{manifest.Id}/{path}: bite \"{biteName}\" is not a shape this build swings — sphere, cube or chunk.");
                        continue;
                }
            }

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

            // M10c-3: the power block, with a link's via rule and a bound of its own. Refused
            // rather than defaulted, for the loader's usual reason — a tool that was meant to
            // cost something and loaded free would be the gate silently open.
            ToolPower? power = null;
            if (dto.Power is { } powerDto)
            {
                if (string.IsNullOrEmpty(powerDto.Via))
                {
                    errors.Add($"{manifest.Id}/{path}: power names no via.");
                    continue;
                }

                if (!MachineDefinition.ViaIsServed(powerDto.Via, out string? viaError))
                {
                    errors.Add($"{manifest.Id}/{path}: power: {viaError}");
                    continue;
                }

                long joules = powerDto.JoulesPerVoxel ?? 0;
                if (joules < 0 || joules > ToolPower.MaxJoulesPerVoxel)
                {
                    errors.Add($"{manifest.Id}/{path}: power.joulesPerVoxel must be within 0..{ToolPower.MaxJoulesPerVoxel:N0}.");
                    continue;
                }

                power = new ToolPower(powerDto.Via, joules);
            }

            try
            {
                tools.Register(id, new ToolDefinition
                {
                    Id = id,
                    RadiusMetres = radius,
                    HardnessThreshold = threshold,
                    CooldownSeconds = cooldown,
                    Power = power,
                    Bite = bite,
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

    /// <summary>
    /// Reads a material's saturation curve and refuses one a reader could not interpolate.
    /// </summary>
    /// <remarks>
    /// Strictly increasing in head, non-increasing in distance, never above the dry distance and
    /// never below zero; each point exactly two numbers. A curve whose distance <em>rises</em>
    /// with head is the one shape that reads as a typo and would make water shore a roof up, so it
    /// is refused rather than sorted — sorting would silently keep the mistake.
    /// </remarks>
    private static bool TryReadSupportCurve(
        List<List<float>>? points,
        int dryDistance,
        out IReadOnlyList<SupportPoint> curve,
        out string? error)
    {
        error = null;
        if (points is null || points.Count == 0)
        {
            curve = [];
            return true;
        }

        var read = new List<SupportPoint>(points.Count);
        float lastHead = 0f;
        float lastDistance = dryDistance;
        foreach (List<float> point in points)
        {
            if (point is null || point.Count != 2)
            {
                error = "supportUnderHead must be a list of [headMetres, distance] pairs.";
                curve = [];
                return false;
            }

            float head = point[0];
            float distance = point[1];
            if (float.IsNaN(head) || float.IsNaN(distance) || head <= lastHead)
            {
                error = "supportUnderHead heads must be positive and strictly increasing.";
                curve = [];
                return false;
            }

            if (distance < 0f || distance > lastDistance)
            {
                error = "supportUnderHead distances must not rise with head, and must not exceed supportDistance.";
                curve = [];
                return false;
            }

            read.Add(new SupportPoint(head, distance));
            lastHead = head;
            lastDistance = distance;
        }

        curve = read;
        return true;
    }

    private static bool TryReadUnit(string? text, out MaterialUnit unit, out string? error)
    {
        error = null;
        switch (text)
        {
            case null or "litres": unit = MaterialUnit.Litres; return true;
            case "count": unit = MaterialUnit.Count; return true;
            case "cells": unit = MaterialUnit.Cells; return true;
            default:
                unit = MaterialUnit.Litres;
                error = $"unit '{text}' is not one of litres, count, cells.";
                return false;
        }
    }

    /// <summary>
    /// Reads a <c>machine</c> block, refusing anything the engine could not serve with the file
    /// named — the rules are <see cref="MachineDefinition.TryValidate"/>'s, and the shape rules
    /// here are only the ones a DTO cannot express.
    /// </summary>
    /// <remarks>
    /// <b>The whole grammar is read and a <c>via</c> this build does not serve is refused, naming
    /// the part that will</b> (M10a's pass, decision 11). A mod written for M10f that loaded on
    /// M10a and ticked nothing would be the lamp that did not glow, against a format mods write to.
    /// </remarks>
    private static bool TryReadMachine(MachineJson dto, out MachineDefinition? machine, out string? error)
    {
        machine = null;

        MachineKind kind;
        switch (dto.Kind)
        {
            case "pump": kind = MachineKind.Pump; break;
            case "outlet": kind = MachineKind.Outlet; break;
            case "converter": kind = MachineKind.Converter; break;
            case "storage": kind = MachineKind.Storage; break;
            case "generator":
                error = "kind 'generator' is not in this build — the burner and the crank are M10f.";
                return false;
            case "station":
                error = "kind 'station' is not in this build — crafting is M10g.";
                return false;
            case null:
                error = "no 'kind'.";
                return false;
            default:
                error = $"kind '{dto.Kind}' is not a machine kind.";
                return false;
        }

        long millilitresPerSecond = 0;
        if (dto.LitresPerSecond is { } litresPerSecond)
        {
            // Kept in millilitres so the boundary's share is integer arithmetic; a rate that is
            // not a whole millilitre a second is refused rather than rounded, because the tick
            // would then move a different amount from the one the file says.
            double millis = litresPerSecond * 1000.0;
            if (double.IsNaN(millis) || double.IsInfinity(millis) || millis != Math.Round(millis))
            {
                error = "litresPerSecond must be a whole number of millilitres a second (three decimals at most).";
                return false;
            }

            millilitresPerSecond = (long)Math.Round(millis);
        }

        // Thousandths, by the rule litresPerSecond follows: a fraction the tick cannot hold
        // exactly is refused rather than rounded, so the file and the pool agree.
        int efficiencyPerMille = MachineDefinition.PerMille;
        if (dto.Efficiency is { } efficiency)
        {
            double thousandths = efficiency * MachineDefinition.PerMille;
            if (double.IsNaN(thousandths) || double.IsInfinity(thousandths) || thousandths != Math.Round(thousandths))
            {
                error = "efficiency must be a whole number of thousandths (three decimals at most).";
                return false;
            }

            if (thousandths < 0 || thousandths > MachineDefinition.PerMille)
            {
                error = "efficiency must be within 0..1.";
                return false;
            }

            efficiencyPerMille = (int)Math.Round(thousandths);
        }

        long capacityMillijoules = 0;
        if (dto.CapacityJoules is { } capacityJoules)
        {
            if (capacityJoules < 0 || capacityJoules > MachineDefinition.MaxCapacityJoules)
            {
                error = $"capacityJoules must be within 0..{MachineDefinition.MaxCapacityJoules:N0}.";
                return false;
            }

            capacityMillijoules = capacityJoules * MachineDefinition.PerMille;
        }

        var links = new List<MachineLink>();
        if (dto.Links is not null)
        {
            foreach ((string name, MachineLinkJson? link) in dto.Links)
            {
                if (link is null)
                {
                    error = $"link '{name}' is empty.";
                    return false;
                }

                MachineMedium carries;
                switch (link.Carries)
                {
                    case "power": carries = MachineMedium.Power; break;
                    case "fluid": carries = MachineMedium.Fluid; break;
                    default:
                        error = $"link '{name}': carries must be power or fluid.";
                        return false;
                }

                MachineRole role;
                switch (link.Role)
                {
                    case "source": role = MachineRole.Source; break;
                    case "sink": role = MachineRole.Sink; break;
                    default:
                        error = $"link '{name}': role must be source or sink.";
                        return false;
                }

                if (link.Via is null)
                {
                    error = $"link '{name}' names no via.";
                    return false;
                }

                links.Add(new MachineLink(name, carries, role, link.Via, link.Face, link.Port));
            }
        }

        var ports = new List<PortDefinition>();
        if (dto.Ports is not null)
        {
            foreach ((string name, MachinePortJson? port) in dto.Ports)
            {
                if (port is null || port.Min is null || port.Max is null)
                {
                    error = $"port '{name}' needs min and max (and default, which is min when absent).";
                    return false;
                }

                if (!PortDefinition.IsValidName(name))
                {
                    error = $"'{name}' is not a port name: 1..{PortDefinition.MaxNameLength} of lowercase letters, digits, '_', '-' or '.'.";
                    return false;
                }

                long min = port.Min.Value, max = port.Max.Value, @default = port.Default ?? min;
                if (min > max || @default < min || @default > max)
                {
                    error = $"port '{name}': min {min} <= default {@default} <= max {max} does not hold.";
                    return false;
                }

                ports.Add(new PortDefinition(name, @default, min, max));
            }
        }

        var read = new MachineDefinition
        {
            Kind = kind,
            MillilitresPerSecond = millilitresPerSecond,
            HeadMetres = dto.HeadMetres,
            JoulesPerLitre = dto.JoulesPerLitre ?? 0,
            IntakeRadiusCells = dto.IntakeRadiusCells ?? MachineDefinition.MinIntakeRadiusCells,
            RatedWatts = dto.RatedWatts ?? 0,
            EfficiencyPerMille = efficiencyPerMille,
            CapacityMillijoules = capacityMillijoules,
            Carried = dto.Carried ?? false,
            RechargeWatts = dto.RechargeWatts,
            Links = links,
            Ports = ports,
        };

        if (!read.TryValidate(out error))
        {
            return false;
        }

        machine = read;
        return true;
    }

    private sealed class ModManifestJson
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public List<string>? Dependencies { get; set; }
    }

    private sealed class MachineJson
    {
        public string? Kind { get; set; }
        public double? LitresPerSecond { get; set; }
        public double? HeadMetres { get; set; }
        public long? JoulesPerLitre { get; set; }
        public int? IntakeRadiusCells { get; set; }
        public long? RatedWatts { get; set; }
        public double? Efficiency { get; set; }
        public long? CapacityJoules { get; set; }
        public bool? Carried { get; set; }
        public long? RechargeWatts { get; set; }
        public Dictionary<string, MachineLinkJson?>? Links { get; set; }
        public Dictionary<string, MachinePortJson?>? Ports { get; set; }
    }

    private sealed class MachineLinkJson
    {
        public string? Carries { get; set; }
        public string? Role { get; set; }
        public string? Via { get; set; }
        public string? Face { get; set; }
        public string? Port { get; set; }
    }

    private sealed class MachinePortJson
    {
        public long? Default { get; set; }
        public long? Min { get; set; }
        public long? Max { get; set; }
    }

    private sealed class MaterialJson
    {
        public string? Id { get; set; }
        public float? Hardness { get; set; }
        public int? SupportDistance { get; set; }
        public List<List<float>>? SupportUnderHead { get; set; }
        public float? Permeability { get; set; }
        public float? Density { get; set; }
        public int? TextureLayer { get; set; }
        public int? LightEmission { get; set; }
        public bool? Opaque { get; set; }
        public bool? Fluid { get; set; }
        public bool? Replaceable { get; set; }
        public bool? Placeable { get; set; }
        public string? Unit { get; set; }
        public MachineJson? Machine { get; set; }
    }

    private sealed class ToolJson
    {
        public string? Id { get; set; }
        public float? RadiusMetres { get; set; }
        public float? HardnessThreshold { get; set; }
        public float? CooldownSeconds { get; set; }
        public ToolPowerJson? Power { get; set; }
        public string? Bite { get; set; }
    }

    private sealed class ToolPowerJson
    {
        public string? Via { get; set; }
        public long? JoulesPerVoxel { get; set; }
    }
}
