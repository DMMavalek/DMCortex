using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public static class FoundryCharacterExchangeService
{
    public const string Schema = "dungeon-master-cortex.foundry-character-exchange";
    public const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions MergeJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static void SynchronizeCharacterData(
        CharacterSheet character,
        RulesEngine? rules = null,
        CharacterOptionCatalog? catalog = null)
    {
        character.SavingThrows = CharacterSavingThrowService.CalculateTargets(character, character.SavingThrows);
        MergeFoundrySourceSpells(character);
        HydrateStructuredData(character, rules, catalog);
    }

    public static string ExportCodex(
        CharacterSheet character,
        JsonSerializerOptions options,
        RulesEngine? rules = null,
        CharacterOptionCatalog? catalog = null)
    {
        SynchronizeCharacterData(character, rules, catalog);
        return JsonSerializer.Serialize(character, options);
    }

    public static IReadOnlyList<SpellDefinition> GetExportSpellCandidates(
        CharacterSheet character,
        RulesEngine rules)
    {
        var ids = new HashSet<string>(GetRequiredSpellIds(character), StringComparer.OrdinalIgnoreCase);
        ids.UnionWith(character.ExportSpellIds ?? new List<string>());
        ids.UnionWith(character.TrackedSpellIds ?? new List<string>());
        ids.UnionWith(character.ArcaneSpells.Select(spell => spell.Id));
        ids.UnionWith(character.DivineSpells.Select(spell => spell.Id));
        ids.UnionWith(character.PsionicPowers.Select(spell => spell.Id));

        int maxDivineLevel = character.DivineSpellSlots
            .Where(slot => slot.Value > 0)
            .Select(slot => slot.Key)
            .DefaultIfEmpty(0)
            .Max();
        if (maxDivineLevel > 0)
        {
            Dictionary<string, string> selectedSpheres = GetSelectedSpheres(character);
            foreach (SpellDefinition spell in rules.Spells
                .Where(spell => spell.Category.Equals("Divine", StringComparison.OrdinalIgnoreCase))
                .Where(spell => ParseSpellLevel(spell.Level) is int level && level > 0 && level <= maxDivineLevel)
                .Where(spell => IsDivineSpellVisible(spell, selectedSpheres)))
            {
                ids.Add(spell.Id);
            }
        }

        return rules.Spells
            .Where(spell => ids.Contains(spell.Id))
            .OrderBy(spell => spell.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(spell => ParseSpellLevel(spell.Level))
            .ThenBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlySet<string> GetRequiredSpellIds(CharacterSheet character)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in character.WizardSpellbookIds ?? new List<string>())
            if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
        foreach (NamedSpellList list in character.WizardSpellLists ?? new List<NamedSpellList>())
            foreach (string id in list.SpellIds ?? new List<string>())
                if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
        foreach (WizardSpellbook book in character.WizardSpellbooks ?? new List<WizardSpellbook>())
            foreach (string id in book.SpellPages.Keys)
                if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
        if (character.SpellTracking is CharacterSpellTracking tracking)
        {
            foreach (DailySpellTracking day in tracking.TrackingHistory.Prepend(tracking.CurrentDayTracking))
            {
                foreach (PreparedSpellRecord record in day.PreparedSpells)
                    if (!string.IsNullOrWhiteSpace(record.SpellId)) ids.Add(record.SpellId);
                foreach (CastSpellRecord record in day.CastSpells)
                    if (!string.IsNullOrWhiteSpace(record.SpellId)) ids.Add(record.SpellId);
            }
        }
        return ids;
    }

    public static string Export(
        CharacterSheet character,
        JsonSerializerOptions options,
        RulesEngine? rules = null,
        CharacterOptionCatalog? catalog = null)
    {
        SynchronizeCharacterData(character, rules, catalog);
        JsonObject codexObject = JsonSerializer.SerializeToNode(character, options)?.AsObject()
            ?? throw new InvalidDataException("Unable to serialize the Codex character payload.");
        if (codexObject.TryGetPropertyValue("NonweaponProficiencies", out JsonNode? legacyNwps))
            codexObject["LegacyNonweaponProficiencies"] = legacyNwps?.DeepClone();
        codexObject["NonweaponProficiencies"] = JsonSerializer.SerializeToNode(
            character.StructuredNonweaponProficiencies,
            options);
        JsonElement codex = JsonSerializer.SerializeToElement(codexObject, options);
        var envelope = new CharacterExchangeEnvelope
        {
            Codex = codex,
            Foundry = character.FoundrySourceData,
        };
        return JsonSerializer.Serialize(envelope, options);
    }

    public static bool TryImport(
        string json,
        JsonSerializerOptions options,
        out CharacterSheet? character,
        out string warning,
        RulesEngine? rules = null,
        CharacterOptionCatalog? catalog = null)
    {
        character = null;
        warning = string.Empty;

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (GetString(root, "Schema").Equals(Schema, StringComparison.OrdinalIgnoreCase))
        {
            int version = GetInt(root, "SchemaVersion", 0);
            if (version > SchemaVersion)
                throw new InvalidDataException($"Exchange schema version {version} is newer than this Codex supports.");

            if (!TryGetProperty(root, "Codex", out JsonElement codex))
                throw new InvalidDataException("The exchange file does not contain a Codex payload.");

            character = DeserializeCodexCharacter(codex, options);
            if (character is null || string.IsNullOrWhiteSpace(character.Name))
                throw new InvalidDataException("The exchange file contains an invalid Codex character.");

            if (TryGetProperty(root, "Foundry", out JsonElement foundry) && foundry.ValueKind == JsonValueKind.Object)
                character.FoundrySourceData = foundry.Clone();
            SynchronizeCharacterData(character, rules, catalog);
            return true;
        }

        if (!TryGetProperty(root, "FoundryExtensions", out JsonElement extensions)
            || extensions.ValueKind != JsonValueKind.Object)
            return false;

        character = ImportLegacyFoundry(root, extensions, options);
        SynchronizeCharacterData(character, rules, catalog);
        warning = "Imported a legacy Foundry export. Its complete original payload was retained for lossless exchange export.";
        return true;
    }

    private static CharacterSheet ImportLegacyFoundry(
        JsonElement root,
        JsonElement extensions,
        JsonSerializerOptions options)
    {
        List<string> classIds = ReadStringList(root, "ClassIds")
            .Select(NormalizeId)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string classId = NormalizeId(GetString(root, "ClassId"));
        if (classId.Length == 0)
            classId = classIds.FirstOrDefault() ?? string.Empty;

        var character = new CharacterSheet
        {
            Name = GetString(root, "Name"),
            PlayerName = GetString(root, "PlayerName"),
            Gender = GetString(root, "Gender"),
            Alignment = GetString(root, "Alignment"),
            Party = GetString(root, "Party"),
            RaceId = ResolveRaceId(GetString(root, "RaceId"), GetString(root, "RaceName")),
            RaceName = GetString(root, "RaceName"),
            ClassId = classId,
            ClassName = GetString(root, "ClassName"),
            ClassIds = classIds,
            ClassMode = classIds.Count > 1 ? "multiclass" : GetString(root, "ClassMode"),
            CharacterMode = GetString(root, "CharacterMode", "core_rules"),
            Level = GetInt(root, "Level", 1),
            ExperiencePoints = GetPerClassExperience(root, extensions),
            UnspentCharacterPoints = GetInt(root, "UnspentCharacterPoints"),
            SpentNwpCharacterPoints = GetInt(root, "SpentNwpCharacterPoints"),
            SpentWeaponCharacterPoints = GetInt(root, "SpentWeaponCharacterPoints"),
            SpentHpRerollCharacterPoints = GetInt(root, "SpentHpRerollCharacterPoints"),
            HpRerollsByLevel = DeserializeProperty(root, "HpRerollsByLevel", options, new Dictionary<int, int>()),
            BaseHitPoints = GetInt(root, "BaseHitPoints", 8),
            HitPoints = GetInt(root, "HitPoints", 8),
            CurrentHitPoints = GetInt(root, "CurrentHitPoints", 8),
            BaseArmorClass = GetInt(root, "BaseArmorClass", 10),
            ArmorClass = GetInt(root, "ArmorClass", 10),
            ArmorProfile = GetString(root, "ArmorProfile", "no_armor"),
            Thac0 = GetInt(root, "Thac0", 20),
            AttackRate = GetString(root, "AttackRate", "1/round"),
            BaseMovement = GetInt(root, "BaseMovement", 12),
            Movement = GetInt(root, "Movement", 12),
            PlatinumPieces = GetInt(root, "PlatinumPieces"),
            GoldPieces = GetInt(root, "GoldPieces"),
            SilverPieces = GetInt(root, "SilverPieces"),
            CopperPieces = GetInt(root, "CopperPieces"),
            GemCount = GetInt(root, "GemCount"),
            GemValueGoldPieces = GetInt(root, "GemValueGoldPieces"),
            Abilities = DeserializeProperty(root, "Abilities", options, new Dictionary<string, int>()),
            SubAbilities = DeserializeProperty(root, "SubAbilities", options, new Dictionary<string, int>()),
            DerivedStats = DeserializeProperty(root, "DerivedStats", options, new Dictionary<string, int>()),
            Bonuses = DeserializeProperty(root, "Bonuses", options, new AbilityBonuses()),
            SavingThrows = DeserializeProperty(extensions, "SavingThrows", options, new CharacterSavingThrows()),
            StructuredAbilities = DeserializeProperty(root, "StructuredAbilities", options, new List<AbilityDefinition>()),
            WeaponProficiencies = DeserializeProperty(root, "WeaponProficiencies", options, new List<WeaponProficiencySelection>()),
            Notes = ReadStringList(root, "Notes"),
            Languages = ReadStringList(root, "Languages"),
            Equipment = ReadStringList(root, "Equipment"),
            Traits = ReadStringList(root, "Traits"),
            Disadvantages = ReadStringList(root, "Disadvantages"),
            ExceptionalStrength = GetInt(root, "ExceptionalStrength"),
            FoundrySourceData = root.Clone(),
        };

        character.NonweaponProficiencies = ReadDisplayNames(root, "NonweaponProficiencies");
        character.StructuredNonweaponProficiencies = ReadLegacyFoundryNwps(root);
        character.ArcaneSpells = ReadFoundrySpells(extensions, "ArcaneSpells", options);
        character.DivineSpells = ReadFoundrySpells(extensions, "DivineSpells", options);
        character.PsionicPowers = ReadFoundrySpells(extensions, "PsionicPowers", options);
        character.CastingPools = DeserializeProperty(extensions, "CastingPools", options, new List<SpellCastingPoolRecord>());
        ApplyCastingPoolState(character);
        character.EquipmentSelections = ReadFoundryEquipment(root, extensions, options);
        character.ClassLevels = ReadClassLevels(extensions);
        if (character.ClassIds.Count == 0 && character.ClassId.Length > 0)
            character.ClassIds.Add(character.ClassId);
        return character;
    }

    private static CharacterSheet? DeserializeCodexCharacter(JsonElement codex, JsonSerializerOptions options)
    {
        JsonObject normalized = JsonNode.Parse(codex.GetRawText())?.AsObject()
            ?? throw new InvalidDataException("The Codex payload is not an object.");
        if (normalized["NonweaponProficiencies"] is JsonArray nwps
            && nwps.Any(node => node is JsonObject))
        {
            normalized["StructuredNonweaponProficiencies"] = nwps.DeepClone();
            normalized["NonweaponProficiencies"] = normalized["LegacyNonweaponProficiencies"]?.DeepClone()
                ?? new JsonArray();
        }
        normalized.Remove("LegacyNonweaponProficiencies");
        return normalized.Deserialize<CharacterSheet>(options);
    }

    private static void HydrateStructuredData(
        CharacterSheet character,
        RulesEngine? rules,
        CharacterOptionCatalog? catalog)
    {
        HydrateSpells(character, rules);
        HydrateNwps(character, catalog);
        HydrateEquipment(character);
        HydrateAbilities(character, rules);
        ApplyCastingPoolState(character);
    }

    private static void HydrateSpells(CharacterSheet character, RulesEngine? rules)
    {
        var prepared = (character.SpellTracking?.CurrentDayTracking?.PreparedSpells ?? new List<PreparedSpellRecord>())
            .Where(record => !string.IsNullOrWhiteSpace(record.SpellId))
            .GroupBy(record => record.SpellId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var ids = character.HasExplicitSpellExportSelection
            ? new HashSet<string>(character.ExportSpellIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(character.TrackedSpellIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        ids.UnionWith(GetRequiredSpellIds(character));
        foreach (string id in prepared.Keys)
            ids.Add(id);
        foreach (string id in character.WizardSpellbookIds ?? new List<string>())
            ids.Add(id);
        if (character.SpellTracking is CharacterSpellTracking tracking)
        {
            foreach (DailySpellTracking day in tracking.TrackingHistory.Prepend(tracking.CurrentDayTracking))
            {
                foreach (PreparedSpellRecord record in day.PreparedSpells)
                    if (!string.IsNullOrWhiteSpace(record.SpellId)) ids.Add(record.SpellId);
                foreach (CastSpellRecord record in day.CastSpells)
                    if (!string.IsNullOrWhiteSpace(record.SpellId)) ids.Add(record.SpellId);
            }
        }
        foreach (NamedSpellList list in character.WizardSpellLists ?? new List<NamedSpellList>())
            foreach (string id in list.SpellIds ?? new List<string>())
                ids.Add(id);
        foreach (WizardSpellbook book in character.WizardSpellbooks ?? new List<WizardSpellbook>())
            foreach (string id in book.SpellPages.Keys)
                ids.Add(id);
        if (!character.HasExplicitSpellExportSelection)
        {
            foreach (CharacterSpellRecord spell in character.ArcaneSpells
                .Concat(character.DivineSpells)
                .Concat(character.PsionicPowers))
            {
                if (!string.IsNullOrWhiteSpace(spell.Id))
                    ids.Add(spell.Id);
            }
        }

        if (character.HasExplicitSpellExportSelection)
        {
            character.ArcaneSpells.RemoveAll(spell => !ids.Contains(spell.Id));
            character.DivineSpells.RemoveAll(spell => !ids.Contains(spell.Id));
            character.PsionicPowers.RemoveAll(spell => !ids.Contains(spell.Id));
        }

        foreach (string id in ids)
        {
            SpellDefinition? definition = rules?.Spells.FirstOrDefault(spell =>
                spell.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            List<PreparedSpellRecord> preparedRecords = prepared.GetValueOrDefault(id) ?? new List<PreparedSpellRecord>();
            CharacterSpellRecord? retained = FindSpell(character, id);
            var record = retained ?? BuildSpellRecord(definition, preparedRecords.FirstOrDefault(), id);
            EnrichSpellRecord(record, definition, preparedRecords.FirstOrDefault());
            record.PreparedCount = preparedRecords.Count;
            record.RemainingPreparedCount = preparedRecords.Count(item => !item.IsCast);
            record.RemainingUses = record.RemainingPreparedCount;
            record.Prepared = preparedRecords.Count > 0;
            AddSpellIfMissing(character, record, definition?.Category);
        }
        character.TrackedSpellIds = character.ArcaneSpells
            .Concat(character.DivineSpells)
            .Concat(character.PsionicPowers)
            .Select(spell => spell.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (character.SpellTracking?.DivineConfig?.MaxSpellSlots is { Count: > 0 } divineMax)
            character.DivineSpellSlots = new Dictionary<int, int>(divineMax);
        if (character.SpellTracking?.ArcaneConfig?.MaxSpellSlots is { Count: > 0 } arcaneMax)
            character.ArcaneSpellSlots = new Dictionary<int, int>(arcaneMax);
        character.RemainingDivineSpellSlots = character.SpellTracking?.GetAvailableSpellSlots(true)
            ?? new Dictionary<int, int>(character.DivineSpellSlots);
        character.RemainingArcaneSpellSlots = character.SpellTracking?.GetAvailableSpellSlots(false)
            ?? new Dictionary<int, int>(character.ArcaneSpellSlots);

        if (character.CastingPools.Count == 0)
        {
            AddCastingPool(character.CastingPools, "codex-divine", "Divine", character.DivineSpellSlots, character.RemainingDivineSpellSlots);
            AddCastingPool(character.CastingPools, "codex-arcane", "Arcane", character.ArcaneSpellSlots, character.RemainingArcaneSpellSlots);
        }
    }

    private static CharacterSpellRecord BuildSpellRecord(
        SpellDefinition? definition,
        PreparedSpellRecord? prepared,
        string id)
    {
        int.TryParse(definition?.Level, out int level);
        return new CharacterSpellRecord
        {
            Id = id,
            Name = definition?.Name ?? prepared?.SpellName ?? id,
            Level = level > 0 ? level : prepared?.SpellLevel ?? 0,
            Category = definition?.Category ?? "",
            Sphere = prepared?.Sphere ?? (string.Equals(definition?.Category, "Divine", StringComparison.OrdinalIgnoreCase)
                ? definition?.Schools ?? "" : ""),
            CastingPoolId = string.Equals(definition?.Category, "Divine", StringComparison.OrdinalIgnoreCase)
                ? "codex-divine"
                : string.Equals(definition?.Category, "Psionic", StringComparison.OrdinalIgnoreCase)
                    ? "codex-psionic" : "codex-arcane",
            EffectType = definition?.IsHealing == true ? "Healing" : "",
            Components = definition?.Components ?? "",
            Materials = definition?.Materials ?? "",
            CastingTime = definition?.CastTime ?? "",
            Duration = definition?.Duration ?? "",
            Range = definition?.Range ?? "",
            AreaOfEffect = definition?.Area ?? "",
            SavingThrow = definition?.Save ?? "",
            Reversal = definition?.Reversal ?? "",
            Schools = definition?.Schools ?? "",
            Frequency = definition?.Frequency ?? "",
            Volume = definition?.Volume ?? "",
            Page = definition?.Page ?? "",
            IsHealing = definition?.IsHealing ?? false,
            Description = definition?.Description ?? "",
            BriefDescription = definition?.BriefDescription ?? "",
            Damage = definition?.Damage ?? "",
            DamageStep = definition?.DamageStep ?? "",
            DamageScaleStartLevel = definition?.DamageScaleStartLevel ?? "",
            DamageScaleEveryLevels = definition?.DamageScaleEveryLevels ?? "",
            DamageMaxAtLevel = definition?.DamageMaxAtLevel ?? "",
            DamageMax = definition?.DamageMax ?? "",
        };
    }

    private static void EnrichSpellRecord(
        CharacterSpellRecord record,
        SpellDefinition? definition,
        PreparedSpellRecord? prepared)
    {
        if (definition is null)
        {
            if (string.IsNullOrWhiteSpace(record.Name)) record.Name = prepared?.SpellName ?? record.Id;
            if (record.Level <= 0) record.Level = prepared?.SpellLevel ?? 0;
            if (string.IsNullOrWhiteSpace(record.Sphere)) record.Sphere = prepared?.Sphere ?? "";
            return;
        }

        int.TryParse(definition.Level, out int level);
        if (string.IsNullOrWhiteSpace(record.Name)) record.Name = definition.Name;
        if (record.Level <= 0) record.Level = level;
        if (string.IsNullOrWhiteSpace(record.Category)) record.Category = definition.Category;
        if (string.IsNullOrWhiteSpace(record.Sphere) && string.Equals(definition.Category, "Divine", StringComparison.OrdinalIgnoreCase))
            record.Sphere = prepared?.Sphere ?? definition.Schools;
        if (string.IsNullOrWhiteSpace(record.CastingPoolId))
            record.CastingPoolId = string.Equals(definition.Category, "Divine", StringComparison.OrdinalIgnoreCase)
                ? "codex-divine"
                : string.Equals(definition.Category, "Psionic", StringComparison.OrdinalIgnoreCase)
                    ? "codex-psionic" : "codex-arcane";
        if (string.IsNullOrWhiteSpace(record.EffectType) && definition.IsHealing) record.EffectType = "Healing";
        if (string.IsNullOrWhiteSpace(record.Components)) record.Components = definition.Components;
        if (string.IsNullOrWhiteSpace(record.Materials)) record.Materials = definition.Materials;
        if (string.IsNullOrWhiteSpace(record.CastingTime)) record.CastingTime = definition.CastTime;
        if (string.IsNullOrWhiteSpace(record.Duration)) record.Duration = definition.Duration;
        if (string.IsNullOrWhiteSpace(record.Range)) record.Range = definition.Range;
        if (string.IsNullOrWhiteSpace(record.AreaOfEffect)) record.AreaOfEffect = definition.Area;
        if (string.IsNullOrWhiteSpace(record.SavingThrow)) record.SavingThrow = definition.Save;
        if (string.IsNullOrWhiteSpace(record.Reversal)) record.Reversal = definition.Reversal;
        if (string.IsNullOrWhiteSpace(record.Schools)) record.Schools = definition.Schools;
        if (string.IsNullOrWhiteSpace(record.Frequency)) record.Frequency = definition.Frequency;
        if (string.IsNullOrWhiteSpace(record.Volume)) record.Volume = definition.Volume;
        if (string.IsNullOrWhiteSpace(record.Page)) record.Page = definition.Page;
        record.IsHealing |= definition.IsHealing;
        if (string.IsNullOrWhiteSpace(record.Description)) record.Description = definition.Description;
        if (string.IsNullOrWhiteSpace(record.BriefDescription)) record.BriefDescription = definition.BriefDescription;
        if (string.IsNullOrWhiteSpace(record.Damage)) record.Damage = definition.Damage;
        if (string.IsNullOrWhiteSpace(record.DamageStep)) record.DamageStep = definition.DamageStep;
        if (string.IsNullOrWhiteSpace(record.DamageScaleStartLevel)) record.DamageScaleStartLevel = definition.DamageScaleStartLevel;
        if (string.IsNullOrWhiteSpace(record.DamageScaleEveryLevels)) record.DamageScaleEveryLevels = definition.DamageScaleEveryLevels;
        if (string.IsNullOrWhiteSpace(record.DamageMaxAtLevel)) record.DamageMaxAtLevel = definition.DamageMaxAtLevel;
        if (string.IsNullOrWhiteSpace(record.DamageMax)) record.DamageMax = definition.DamageMax;
    }

    private static void MergeFoundrySourceSpells(CharacterSheet character)
    {
        if (character.FoundrySourceData is not JsonElement source || source.ValueKind != JsonValueKind.Object)
            return;

        JsonElement container = source;
        if (TryGetProperty(container, "FoundryExtensions", out JsonElement extensions))
            container = extensions;
        if (TryGetProperty(container, "Magic", out JsonElement magic))
            container = magic;

        MergeFoundrySpellCollection(character, container, "ArcaneSpells", character.ArcaneSpells);
        MergeFoundrySpellCollection(character, container, "DivineSpells", character.DivineSpells);
        MergeFoundrySpellCollection(character, container, "PsionicPowers", character.PsionicPowers);
    }

    private static void MergeFoundrySpellCollection(
        CharacterSheet character,
        JsonElement container,
        string propertyName,
        List<CharacterSpellRecord> target)
    {
        if (!TryGetProperty(container, propertyName, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
            return;

        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object)
                continue;
            string id = GetString(value, "Id", GetString(value, "SpellId"));
            if (string.IsNullOrWhiteSpace(id))
                continue;
            CharacterSpellRecord? existing = FindSpell(character, id);
            CharacterSpellRecord merged = MergeSpellRecord(existing, value);
            if (existing is null)
                target.Add(merged);
            else
            {
                character.ArcaneSpells.Remove(existing);
                character.DivineSpells.Remove(existing);
                character.PsionicPowers.Remove(existing);
                target.Add(merged);
            }
        }
    }

    private static CharacterSpellRecord MergeSpellRecord(CharacterSpellRecord? existing, JsonElement incoming)
    {
        JsonObject merged = existing is null
            ? new JsonObject()
            : JsonSerializer.SerializeToNode(existing, MergeJsonOptions)?.AsObject() ?? new JsonObject();
        JsonObject incomingObject = JsonNode.Parse(incoming.GetRawText())?.AsObject() ?? new JsonObject();
        foreach ((string key, JsonNode? value) in incomingObject)
        {
            string? existingKey = merged.Select(property => property.Key)
                .FirstOrDefault(candidate => candidate.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existingKey is not null)
                merged.Remove(existingKey);
            merged[key] = value?.DeepClone();
        }
        CharacterSpellRecord result = merged.Deserialize<CharacterSpellRecord>(MergeJsonOptions)
            ?? throw new InvalidDataException("Unable to merge a Foundry spell record.");
        string incomingId = GetString(incoming, "Id", GetString(incoming, "SpellId"));
        if (!string.IsNullOrWhiteSpace(incomingId))
            result.Id = incomingId;
        return result;
    }

    private static CharacterSpellRecord? FindSpell(CharacterSheet character, string id)
        => character.ArcaneSpells.Concat(character.DivineSpells).Concat(character.PsionicPowers)
            .FirstOrDefault(spell => spell.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static void AddSpellIfMissing(CharacterSheet character, CharacterSpellRecord record, string? category)
    {
        if (FindSpell(character, record.Id) is not null)
            return;
        if (string.Equals(category, "Divine", StringComparison.OrdinalIgnoreCase)
            || record.CastingPoolId.Contains("divine", StringComparison.OrdinalIgnoreCase))
            character.DivineSpells.Add(record);
        else if (string.Equals(category, "Psionic", StringComparison.OrdinalIgnoreCase))
            character.PsionicPowers.Add(record);
        else
            character.ArcaneSpells.Add(record);
    }

    private static void AddCastingPool(
        List<SpellCastingPoolRecord> pools,
        string id,
        string category,
        Dictionary<int, int> maxima,
        Dictionary<int, int> remaining)
    {
        if (maxima.Count == 0)
            return;
        pools.Add(new SpellCastingPoolRecord
        {
            Id = id,
            Name = category,
            Category = category,
            MaxLevel = maxima.Keys.Max(),
            Slots = maxima.ToDictionary(
                pair => pair.Key,
                pair => new SpellSlotState { Max = pair.Value, Remaining = remaining.GetValueOrDefault(pair.Key, pair.Value) }),
        });
    }

    private static void ApplyCastingPoolState(CharacterSheet character)
    {
        foreach (SpellCastingPoolRecord pool in character.CastingPools)
        {
            bool divine = pool.Category.Contains("divine", StringComparison.OrdinalIgnoreCase)
                || pool.Id.Contains("divine", StringComparison.OrdinalIgnoreCase);
            Dictionary<int, int> maxima = divine ? character.DivineSpellSlots : character.ArcaneSpellSlots;
            Dictionary<int, int> remaining = divine ? character.RemainingDivineSpellSlots : character.RemainingArcaneSpellSlots;
            foreach ((int level, SpellSlotState state) in pool.Slots)
            {
                maxima[level] = state.Max;
                remaining[level] = state.Remaining;
            }
        }
    }

    private static void HydrateNwps(CharacterSheet character, CharacterOptionCatalog? catalog)
    {
        if (catalog is not null && character.StructuredNonweaponProficiencies.Count > 0)
        {
            foreach (CharacterNonweaponProficiencyRecord record in character.StructuredNonweaponProficiencies)
            {
                if (!string.IsNullOrWhiteSpace(record.ProficiencyId))
                    continue;
                NonweaponProficiencyDefinition? match = FindNwpByDisplayName(catalog, record.DisplayName);
                if (match is null)
                    continue;
                record.Id = match.Id;
                record.ProficiencyId = match.Id;
            }
            if (character.NonweaponProficiencyIds.Count == 0)
            {
                character.NonweaponProficiencyIds = character.StructuredNonweaponProficiencies
                    .Select(record => record.ProficiencyId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        var retainedById = character.StructuredNonweaponProficiencies
            .Where(record => !string.IsNullOrWhiteSpace(record.ProficiencyId))
            .ToDictionary(record => record.ProficiencyId, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<CharacterNonweaponProficiencyRecord>();
        foreach (string id in character.NonweaponProficiencyIds)
        {
            NonweaponProficiencyDefinition? definition = catalog?.NonweaponProficiencies.FirstOrDefault(item =>
                item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            CharacterNonweaponProficiencyRecord record = retainedById.GetValueOrDefault(id) ?? new CharacterNonweaponProficiencyRecord();
            bool hasImprovementHistory = character.SelectedNonweaponProficiencyImprovements.ContainsKey(id);
            int improvementCp = character.SelectedNonweaponProficiencyImprovements.GetValueOrDefault(id);
            int initialCost = definition?.CpCost > 0 ? definition.CpCost : Math.Max(0, definition?.Slots ?? 0);
            int improvementPoints = string.Equals(character.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
                ? improvementCp
                : (definition?.Slots > 0 ? improvementCp / definition.Slots : improvementCp);
            record.Id = id;
            record.ProficiencyId = id;
            record.DisplayName = definition?.Name ?? record.DisplayName;
            record.Ability = ResolveNwpAbilityKey(definition, record.Ability, character.CharacterMode);
            record.CheckModifier = definition?.CheckModifier ?? record.CheckModifier;
            int abilityScore = GetNwpAbilityScore(character, record.Ability);
            int rangerBonus = definition is not null && RangerTrackingRules.IsTracking(definition.Id, definition.Name)
                ? RangerTrackingRules.GetBonus(character)
                : 0;
            if (record.TargetNumber <= 0 || hasImprovementHistory)
            {
                int playersOptionBase = string.Equals(id, "nwp_animal_lore", StringComparison.OrdinalIgnoreCase)
                    ? 8
                    : (definition?.PlayersOptionBaseRating > 0
                        ? definition.PlayersOptionBaseRating
                        : (record.TargetNumber > 0 ? record.TargetNumber : 5));
                record.TargetNumber = string.Equals(character.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
                    ? playersOptionBase + GetTable44Modifier(abilityScore) + improvementPoints + rangerBonus
                    : abilityScore + (definition?.CheckModifier ?? record.CheckModifier) + improvementPoints + rangerBonus;
            }
            record.InitialCost = initialCost;
            record.ImprovementCharacterPoints = improvementCp;
            record.TotalCharacterPoints = initialCost + improvementCp;
            record.GrantedByClass = initialCost == 0;
            record.Notes = definition?.Description ?? record.Notes;
            ordered.Add(record);
        }
        character.StructuredNonweaponProficiencies = ordered;
    }

    private static string ResolveNwpAbilityKey(
        NonweaponProficiencyDefinition? definition,
        string fallback,
        string characterMode)
    {
        if (string.Equals(definition?.Id, "nwp_animal_lore", StringComparison.OrdinalIgnoreCase))
            return "wis_intuition";

        string value = string.Equals(characterMode, "players_option", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(definition?.PlayersOptionCheckAbility)
                ? definition.PlayersOptionCheckAbility
                : definition?.CheckAbility ?? fallback;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Strength/Stamina"] = "str_stamina",
            ["Strength/Muscle"] = "str_muscle",
            ["Dexterity/Aim"] = "dex_aim",
            ["Dexterity/Balance"] = "dex_balance",
            ["Constitution/Health"] = "con_health",
            ["Constitution/Fitness"] = "con_fitness",
            ["Intelligence/Reason"] = "int_reason",
            ["Intelligence/Knowledge"] = "int_knowledge",
            ["Wisdom/Intuition"] = "wis_intuition",
            ["Wisdom/Willpower"] = "wis_willpower",
            ["Wisdom/Perception"] = "wis_perception",
            ["Charisma/Leadership"] = "cha_leadership",
            ["Charisma/Appearance"] = "cha_appearance",
        };
        return map.GetValueOrDefault(value, value);
    }

    private static int GetNwpAbilityScore(CharacterSheet character, string ability)
    {
        if (character.SubAbilities.TryGetValue(ability, out int directScore))
            return directScore;

        var familyMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Strength"] = new[] { "str_stamina", "str_muscle" },
            ["Dexterity"] = new[] { "dex_aim", "dex_balance" },
            ["Constitution"] = new[] { "con_health", "con_fitness" },
            ["Intelligence"] = new[] { "int_reason", "int_knowledge" },
            ["Wisdom"] = new[] { "wis_intuition", "wis_willpower", "wis_perception" },
            ["Charisma"] = new[] { "cha_leadership", "cha_appearance" },
        };
        if (familyMap.TryGetValue(ability, out string[]? keys))
        {
            List<int> scores = keys
                .Where(character.SubAbilities.ContainsKey)
                .Select(key => character.SubAbilities[key])
                .ToList();
            if (scores.Count > 0)
                return (int)Math.Round(scores.Average(), MidpointRounding.AwayFromZero);
        }

        string baseKey = ability.ToLowerInvariant() switch
        {
            "strength" => "str",
            "dexterity" => "dex",
            "constitution" => "con",
            "intelligence" => "int",
            "wisdom" => "wis",
            "charisma" => "cha",
            _ => "",
        };
        return baseKey.Length > 0 ? character.Abilities.GetValueOrDefault(baseKey, 10) : 10;
    }

    private static int GetTable44Modifier(int score) => score switch
    {
        <= 3 => -5,
        4 => -4,
        5 => -3,
        6 => -2,
        7 => -1,
        >= 8 and <= 13 => 0,
        14 => 1,
        15 => 2,
        16 => 3,
        17 => 4,
        _ => 5,
    };

    private static NonweaponProficiencyDefinition? FindNwpByDisplayName(
        CharacterOptionCatalog catalog,
        string displayName)
    {
        string key = NormalizeLookupName(displayName);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["blind fighting"] = "blind fighting",
            ["heal"] = "healing",
            ["modern languages"] = "languages modern",
            ["riding land horse"] = "riding land",
        };
        key = aliases.GetValueOrDefault(key, key);
        return catalog.NonweaponProficiencies.FirstOrDefault(item =>
            NormalizeLookupName(item.Name).Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeLookupName(string value)
        => string.Join(' ', value.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .Aggregate(new List<string>(), (parts, character) =>
            {
                string current = parts.Count == 0 ? string.Empty : parts[^1];
                if (character == ' ')
                {
                    if (current.Length > 0)
                        parts.Add(string.Empty);
                }
                else if (parts.Count == 0)
                    parts.Add(character.ToString());
                else
                    parts[^1] += character;
                return parts;
            })
            .Where(part => part.Length > 0));

    private static void HydrateEquipment(CharacterSheet character)
    {
        if (character.EquipmentSelections.Count == 0 && character.Equipment.Count > 0)
        {
            IReadOnlyList<CustomEquipmentData> library = new EquipmentLibraryService().GetEquipmentLibrary();
            foreach (string displayValue in character.Equipment.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                (string name, int quantity) = ParseEquipmentDisplayValue(displayValue);
                CustomEquipmentData? definition = library.FirstOrDefault(item =>
                    item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                character.EquipmentSelections.Add(BuildEquipmentSelection(definition, name, quantity));
            }
        }

        foreach (EquipmentSelection item in character.EquipmentSelections)
        {
            item.Equipped = item.Equipped
                || item.ItemId.Equals(character.EquippedArmorId, StringComparison.OrdinalIgnoreCase)
                || item.ItemId.Equals(character.EquippedShieldId, StringComparison.OrdinalIgnoreCase)
                || item.ItemId.Equals(character.EquippedWeaponId, StringComparison.OrdinalIgnoreCase)
                || item.ItemId.Equals(character.EquippedMainWeaponId, StringComparison.OrdinalIgnoreCase)
                || item.ItemId.Equals(character.EquippedOffHandWeaponId, StringComparison.OrdinalIgnoreCase)
                || character.EquippedMagicalItemIds.Contains(item.ItemId, StringComparer.OrdinalIgnoreCase);
        }

        if (character.EquipmentSelections.Count > 0)
        {
            character.Equipment = character.EquipmentSelections
                .Select(item => item.Quantity > 1 ? $"{item.ItemName} x{item.Quantity}" : item.ItemName)
                .ToList();
        }
    }

    private static Dictionary<string, string> GetSelectedSpheres(CharacterSheet character)
    {
        var selected = new Dictionary<string, string>(character.SelectedSpheres, StringComparer.OrdinalIgnoreCase);
        foreach (Dictionary<string, string> classSpheres in character.SpheresByClass.Values)
        {
            foreach ((string sphere, string access) in classSpheres)
            {
                if (!string.IsNullOrWhiteSpace(sphere) && !string.IsNullOrWhiteSpace(access))
                    selected[sphere.Trim()] = access.Trim();
            }
        }
        return selected;
    }

    private static bool IsDivineSpellVisible(SpellDefinition spell, Dictionary<string, string> selectedSpheres)
    {
        HashSet<string> spellSpheres = ParseSpellTokens(spell.Schools);
        if (spellSpheres.Count == 0 || selectedSpheres.Count == 0)
            return true;
        string requiredAccess = ParseSpellLevel(spell.Level) <= 3 ? "minor" : "major";
        return spellSpheres.Any(sphere => RulesEngine.PriestHasSphereAccess(sphere, selectedSpheres, requiredAccess));
    }

    private static HashSet<string> ParseSpellTokens(string value)
        => Regex.Split(value ?? string.Empty, @"\s*(?:,|;|\||&|\band\b)\s*", RegexOptions.IgnoreCase)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => token.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static int ParseSpellLevel(string value)
    {
        if (int.TryParse(value?.Trim(), out int exact))
            return Math.Max(0, exact);
        Match match = Regex.Match(value ?? string.Empty, @"\d+");
        return match.Success && int.TryParse(match.Value, out int parsed) ? Math.Max(0, parsed) : 0;
    }

    private static (string Name, int Quantity) ParseEquipmentDisplayValue(string value)
    {
        Match match = Regex.Match(value.Trim(), @"^(?<name>.+?)\s+[x×](?<quantity>\d+)$", RegexOptions.IgnoreCase);
        return match.Success
            ? (match.Groups["name"].Value.Trim(), Math.Max(1, int.Parse(match.Groups["quantity"].Value)))
            : (value.Trim(), 1);
    }

    private static EquipmentSelection BuildEquipmentSelection(CustomEquipmentData? definition, string name, int quantity)
        => new()
        {
            ItemId = definition?.Id ?? EquipmentLibraryService.BuildId(name),
            ItemName = definition?.Name ?? name,
            Category = definition?.Categories.FirstOrDefault() ?? "Equipment",
            Quantity = quantity,
            Description = definition?.Description ?? string.Empty,
            IsArmor = definition?.IsArmor ?? false,
            ArmorClassValue = definition?.ArmorClassValue ?? 10,
            RogueArmorProfile = definition?.RogueArmorProfile ?? "no_armor",
            IsShield = definition?.IsShield ?? false,
            IsWeapon = definition?.IsWeapon ?? false,
            WeaponSpeed = definition?.WeaponSpeed ?? 0,
            WeaponDamageSmallMedium = definition?.WeaponDamageSmallMedium ?? string.Empty,
            WeaponDamageLarge = definition?.WeaponDamageLarge ?? string.Empty,
            WeaponType = definition?.WeaponType ?? string.Empty,
            WeaponSize = definition?.WeaponSize ?? string.Empty,
            WeaponAttackBonus = definition?.WeaponAttackBonus ?? 0,
            WeaponDamageBonus = definition?.WeaponDamageBonus ?? 0,
            WeaponThac0Modifier = definition?.WeaponThac0Modifier ?? 0,
            CostGoldEach = definition?.CostGold ?? 0,
            CostSilverEach = definition?.CostSilver ?? 0,
            CostCopperEach = definition?.CostCopper ?? 0,
            SizeClassEach = definition?.SizeClass ?? "Medium",
            WeightEach = definition?.Weight ?? 0,
        };

    private static void HydrateAbilities(CharacterSheet character, RulesEngine? rules)
    {
        var definitions = new Dictionary<string, (AbilityDefinition Definition, string Category)>(StringComparer.OrdinalIgnoreCase);
        if (rules is not null)
        {
            foreach (AbilityDefinition ability in rules.Races.Values.SelectMany(race => race.StructuredAbilities))
                definitions[ability.Id] = (ability, "Racial");
            foreach (AbilityDefinition ability in rules.Classes.Values.SelectMany(cls => cls.StructuredAbilities))
                definitions[ability.Id] = (ability, "Class");
            foreach (AbilityDefinition ability in rules.ClassAbilityLibrary.Values)
                definitions.TryAdd(ability.Id, (ability, "Class"));
        }

        foreach (AbilityDefinition ability in character.StructuredAbilities)
        {
            definitions.TryGetValue(ability.Id, out var source);
            if (string.IsNullOrWhiteSpace(ability.Name))
                ability.Name = !string.IsNullOrWhiteSpace(source.Definition?.Name)
                    ? source.Definition.Name
                    : GetAbilityDisplayName(!string.IsNullOrWhiteSpace(ability.Description)
                        ? ability.Description
                        : source.Definition?.Description, ability.Id);
            if (string.IsNullOrWhiteSpace(ability.Category))
                ability.Category = !string.IsNullOrWhiteSpace(source.Definition?.Category)
                    ? source.Definition.Category
                    : source.Category ?? "Character Ability";
        }
    }

    private static string GetAbilityDisplayName(string? description, string id)
    {
        string value = (description ?? string.Empty).Split(':', 2)[0].Trim();
        value = Regex.Replace(value, @"\s*\(\d+(?:\s*/\s*\d+)*(?:\s*CP)?\)\s*$", string.Empty, RegexOptions.IgnoreCase);
        if (!string.IsNullOrWhiteSpace(value))
            return value;
        return string.Join(' ', (id ?? string.Empty).Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => char.ToUpperInvariant(token[0]) + token[1..]));
    }

    private static List<CharacterSpellRecord> ReadFoundrySpells(
        JsonElement extensions,
        string propertyName,
        JsonSerializerOptions options)
    {
        if (!TryGetProperty(extensions, "Magic", out JsonElement magic))
            return new List<CharacterSpellRecord>();
        return DeserializeProperty(magic, propertyName, options, new List<CharacterSpellRecord>());
    }

    private static List<CharacterNonweaponProficiencyRecord> ReadLegacyFoundryNwps(JsonElement root)
    {
        var records = new List<CharacterNonweaponProficiencyRecord>();
        if (!TryGetProperty(root, "NonweaponProficiencies", out JsonElement values) || values.ValueKind != JsonValueKind.Array)
            return records;
        foreach (JsonElement value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object)
                continue;
            records.Add(new CharacterNonweaponProficiencyRecord
            {
                DisplayName = GetString(value, "DisplayName", GetString(value, "Name")),
                Ability = GetString(value, "Ability"),
                CheckModifier = GetInt(value, "CheckModifier"),
                TargetNumber = GetInt(value, "TargetNumber"),
                InitialCost = GetInt(value, "InitialCost"),
                TotalCharacterPoints = GetInt(value, "CPSpent"),
                ExtensionData = ReadUnknownProperties(value, "Name", "DisplayName", "Ability", "CheckModifier", "TargetNumber", "InitialCost", "CPSpent"),
            });
        }
        return records;
    }

    private static List<EquipmentSelection> ReadFoundryEquipment(
        JsonElement root,
        JsonElement extensions,
        JsonSerializerOptions options)
    {
        List<EquipmentSelection> equipment = DeserializeProperty(
            root,
            "EquipmentSelections",
            options,
            new List<EquipmentSelection>());
        if (TryGetProperty(root, "Equipment", out JsonElement rootEquipment)
            && rootEquipment.ValueKind == JsonValueKind.Array
            && rootEquipment.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Object))
        {
            equipment.AddRange(rootEquipment.Deserialize<List<EquipmentSelection>>(options)
                ?? new List<EquipmentSelection>());
        }
        equipment.AddRange(DeserializeProperty(
            extensions,
            "CombatWeapons",
            options,
            new List<EquipmentSelection>()));
        return equipment;
    }

    private static Dictionary<string, JsonElement> ReadUnknownProperties(JsonElement element, params string[] known)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
            if (!known.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                result[property.Name] = property.Value.Clone();
        return result;
    }

    private static Dictionary<string, int> ReadClassLevels(JsonElement extensions)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetProperty(extensions, "ClassProgression", out JsonElement progression)
            || progression.ValueKind != JsonValueKind.Array)
            return result;

        foreach (JsonElement entry in progression.EnumerateArray())
        {
            string id = NormalizeId(GetString(entry, "Name"));
            if (id.Length > 0)
                result[id] = GetInt(entry, "Level", 1);
        }
        return result;
    }

    private static int GetPerClassExperience(JsonElement root, JsonElement extensions)
    {
        if (TryGetProperty(extensions, "ClassProgression", out JsonElement progression)
            && progression.ValueKind == JsonValueKind.Array)
        {
            JsonElement first = progression.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object)
                return GetInt(first, "Experience", GetInt(root, "ExperiencePoints"));
        }
        return GetInt(root, "ExperiencePoints");
    }

    private static List<string> ReadDisplayNames(JsonElement root, string propertyName)
    {
        var result = new List<string>();
        if (!TryGetProperty(root, propertyName, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
            return result;

        foreach (JsonElement value in values.EnumerateArray())
        {
            string text = value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : GetString(value, "DisplayName", GetString(value, "Name"));
            if (!string.IsNullOrWhiteSpace(text))
                result.Add(text.Trim());
        }
        return result;
    }

    private static List<string> ReadStringList(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
            return new List<string>();
        return values.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static T DeserializeProperty<T>(JsonElement root, string propertyName, JsonSerializerOptions options, T fallback)
    {
        if (!TryGetProperty(root, propertyName, out JsonElement value))
            return fallback;
        try { return value.Deserialize<T>(options) ?? fallback; }
        catch (JsonException) { return fallback; }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static string GetString(JsonElement element, string name, string fallback = "")
        => TryGetProperty(element, name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int GetInt(JsonElement element, string name, int fallback = 0)
        => TryGetProperty(element, name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : fallback;

    private static string NormalizeId(string value)
        => value.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

    private static string ResolveRaceId(string raceId, string raceName)
    {
        if (!string.IsNullOrWhiteSpace(raceId))
            return NormalizeId(raceId);
        return raceName.Trim().Equals("Half-Elf", StringComparison.OrdinalIgnoreCase)
            ? "half_elf_po"
            : NormalizeId(raceName);
    }

    private sealed class CharacterExchangeEnvelope
    {
        public string Schema { get; set; } = FoundryCharacterExchangeService.Schema;
        public int SchemaVersion { get; set; } = FoundryCharacterExchangeService.SchemaVersion;
        public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
        public JsonElement Codex { get; set; }
        public JsonElement? Foundry { get; set; }
    }
}