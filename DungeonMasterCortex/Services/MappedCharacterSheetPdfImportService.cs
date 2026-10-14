using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DungeonMasterCortex.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace DungeonMasterCortex.Services;

public sealed class MappedCharacterSheetPdfImportService
{
    public const string DocumentType = "dmcortex.character-sheet";
    public const string SchemaVersion = "1";

    public sealed record ImportResult(
        CharacterSheet Character,
        IReadOnlyList<string> Warnings,
        int PopulatedFieldCount);

    public ImportResult Import(
        string filePath,
        RulesEngine rules,
        CharacterOptionCatalog options)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("PDF path is required.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Mapped character-sheet PDF was not found.", filePath);

        using PdfDocument document = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
        PdfDictionary acroForm = document.Internals.Catalog.Elements.GetDictionary("/AcroForm")
            ?? throw new InvalidDataException("PDF does not contain a fillable character-sheet form.");

        string documentType = acroForm.Elements.GetString("/DMCortexDocumentType");
        string schemaVersion = acroForm.Elements.GetString("/DMCortexSchemaVersion");
        if (!string.Equals(documentType, DocumentType, StringComparison.Ordinal))
            throw new InvalidDataException("PDF is not a Dungeon Master Cortex mapped character sheet.");
        if (!string.Equals(schemaVersion, SchemaVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported mapped character-sheet schema '{schemaVersion}'. Expected version {SchemaVersion}.");

        Dictionary<string, string> values = ReadFieldValues(acroForm);
        int populatedFieldCount = values.Values.Count(value => !string.IsNullOrWhiteSpace(value));
        string name = Get(values, "identity.name");
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidDataException("The mapped PDF must contain a character name before it can be imported.");

        var warnings = new List<string>();
        var character = new CharacterSheet
        {
            Name = name,
            PlayerName = Get(values, "identity.player"),
            Gender = Get(values, "identity.gender"),
            RaceName = Get(values, "identity.race"),
            ClassName = Get(values, "identity.class"),
            Level = GetInt(values, "identity.level", 1, 1, 20, warnings),
            ExperiencePoints = GetInt(values, "identity.experience", 0, 0, int.MaxValue, warnings),
            UnspentCharacterPoints = GetInt(values, "identity.character_points", 0, 0, int.MaxValue, warnings),
            LastModified = DateTime.Now,
            Revision = 1,
        };

        character.RaceId = ResolveRaceId(character.RaceName, rules, warnings);
        character.ClassIds = ResolveClassIds(character.ClassName, rules, warnings);
        character.ClassId = string.Join("/", character.ClassIds);
        character.CharacterMode = HasPlayersOptionValues(values) ? "players_option" : "core_rules";

        ImportAbilities(values, character, warnings);
        ImportHitPoints(values, character, warnings);
        ImportTraitsAndDisadvantages(values, character, options, warnings);
        ImportProficiencies(values, character, rules, options, warnings);
        ImportInventory(values, character, warnings);
        ImportAbilitiesAndPowers(values, character);
        ImportSpells(values, character, rules, warnings);

        character.Notes.Add($"Imported from mapped PDF: {Path.GetFileName(filePath)}");
        character.Notes.Add($"Mapped PDF schema: {DocumentType} v{SchemaVersion}");
        if (populatedFieldCount == 1)
            warnings.Add("Only the character name was populated in the PDF.");

        return new ImportResult(character, warnings, populatedFieldCount);
    }

    private static Dictionary<string, string> ReadFieldValues(PdfDictionary acroForm)
    {
        PdfArray fields = acroForm.Elements.GetArray("/Fields")
            ?? throw new InvalidDataException("PDF form does not contain a fields array.");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < fields.Elements.Count; index++)
        {
            PdfDictionary? field = fields.Elements.GetDictionary(index);
            if (field is null)
                continue;

            string name = field.Elements.GetString("/T").Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            values[name] = field.Elements.GetString("/V").Trim();
        }

        return values;
    }

    private static void ImportAbilities(Dictionary<string, string> values, CharacterSheet character, List<string> warnings)
    {
        foreach ((string field, string key) in new[]
        {
            ("abilities.strength", "str"), ("abilities.dexterity", "dex"),
            ("abilities.constitution", "con"), ("abilities.intelligence", "int"),
            ("abilities.wisdom", "wis"), ("abilities.charisma", "cha"),
        })
        {
            int? score = TryGetInt(values, field, 1, 25, warnings);
            if (score.HasValue)
                character.Abilities[key] = score.Value;
        }

        foreach ((string field, string key) in new[]
        {
            ("subabilities.strength.stamina", "str_stamina"),
            ("subabilities.strength.muscle", "str_muscle"),
            ("subabilities.dexterity.aim", "dex_aim"),
            ("subabilities.dexterity.balance", "dex_balance"),
            ("subabilities.constitution.health", "con_health"),
            ("subabilities.constitution.fitness", "con_fitness"),
            ("subabilities.intelligence.reason", "int_reason"),
            ("subabilities.intelligence.knowledge", "int_knowledge"),
            ("subabilities.wisdom.intuition", "wis_intuition"),
            ("subabilities.wisdom.willpower", "wis_willpower"),
            ("subabilities.charisma.leadership", "cha_leadership"),
            ("subabilities.charisma.appearance", "cha_appearance"),
        })
        {
            int? score = TryGetInt(values, field, 1, 25, warnings);
            if (score.HasValue)
                character.SubAbilities[key] = score.Value;
        }
    }

    private static void ImportHitPoints(Dictionary<string, string> values, CharacterSheet character, List<string> warnings)
    {
        int? hitPoints = TryGetInt(values, "combat.hit_points", 1, 9999, warnings);
        if (hitPoints.HasValue)
        {
            character.BaseHitPoints = hitPoints.Value;
            character.HitPoints = hitPoints.Value;
            character.CurrentHitPoints = hitPoints.Value;
        }

        bool containsDerivedCombatValues = values.Any(pair =>
            pair.Key.StartsWith("combat.", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(pair.Key, "combat.hit_points", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(pair.Value));
        if (containsDerivedCombatValues)
            warnings.Add("Calculated combat values such as THAC0 and Armor Class were ignored so the program can calculate them from source data.");
    }

    private static void ImportTraitsAndDisadvantages(
        Dictionary<string, string> values,
        CharacterSheet character,
        CharacterOptionCatalog options,
        List<string> warnings)
    {
        Dictionary<string, TraitDefinition> traitsByName = options.Traits
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, DisadvantageDefinition> disadvantagesByName = options.Disadvantages
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (string value in ReadIndexedValues(values, "traits", "name"))
        {
            character.Traits.Add(value);
            if (traitsByName.TryGetValue(value, out TraitDefinition? definition))
                character.SelectedTraitIds.Add(definition.Id);
            else
                warnings.Add($"Trait was preserved as text but not matched to the catalog: {value}");
        }

        foreach (string value in ReadIndexedValues(values, "disadvantages", "name"))
        {
            character.Disadvantages.Add(value);
            if (disadvantagesByName.TryGetValue(value, out DisadvantageDefinition? definition))
                character.SelectedDisadvantageSeverities[definition.Id] = "moderate";
            else
                warnings.Add($"Disadvantage was preserved as text but not matched to the catalog: {value}");
        }
    }

    private static void ImportProficiencies(
        Dictionary<string, string> values,
        CharacterSheet character,
        RulesEngine rules,
        CharacterOptionCatalog options,
        List<string> warnings)
    {
        Dictionary<string, NonweaponProficiencyDefinition> nwpByName = options.NonweaponProficiencies
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, WeaponDefinition> weaponsByName = rules.Weapons
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (string value in ReadSimpleIndexedValues(values, "proficiencies.nonweapon"))
        {
            character.NonweaponProficiencies.Add(value);
            if (nwpByName.TryGetValue(value, out NonweaponProficiencyDefinition? definition))
                character.NonweaponProficiencyIds.Add(definition.Id);
            else
                warnings.Add($"Non-weapon proficiency was preserved as text but not matched to the catalog: {value}");
        }

        foreach (string value in ReadSimpleIndexedValues(values, "proficiencies.weapon"))
        {
            string displayName = value;
            bool specialized = displayName.EndsWith(" [Specialized]", StringComparison.OrdinalIgnoreCase);
            if (specialized)
                displayName = displayName[..^14].Trim();

            if (weaponsByName.TryGetValue(displayName, out WeaponDefinition? definition))
            {
                character.WeaponProficiencies.Add(new WeaponProficiencySelection
                {
                    ProficiencyId = definition.Id,
                    ProficiencyType = "individual",
                    DisplayName = definition.Name,
                    Specialized = specialized,
                });
            }
            else
            {
                character.WeaponProficiencies.Add(new WeaponProficiencySelection
                {
                    ProficiencyId = NormalizeId(displayName),
                    ProficiencyType = "individual",
                    DisplayName = displayName,
                    Specialized = specialized,
                });
                warnings.Add($"Weapon proficiency was preserved but not matched to the catalog: {displayName}");
            }
        }
    }

    private static void ImportInventory(Dictionary<string, string> values, CharacterSheet character, List<string> warnings)
    {
        for (int index = 0; index < 36; index++)
        {
            string item = Get(values, $"inventory.{index:00}.item");
            if (string.IsNullOrWhiteSpace(item))
                continue;

            int quantity = GetInt(values, $"inventory.{index:00}.quantity", 1, 1, 9999, warnings);
            character.Equipment.Add(quantity > 1 ? $"{item} x{quantity}" : item);
        }

        foreach (string prefix in new[] { "weapons.main", "weapons.off_hand" })
        {
            int rowCount = prefix.EndsWith("main", StringComparison.Ordinal) ? 8 : 6;
            for (int index = 0; index < rowCount; index++)
            {
                string weapon = Get(values, $"{prefix}.{index:00}.name");
                if (!string.IsNullOrWhiteSpace(weapon) && !character.Equipment.Contains(weapon, StringComparer.OrdinalIgnoreCase))
                    character.Equipment.Add(weapon);
            }
        }
    }

    private static void ImportAbilitiesAndPowers(Dictionary<string, string> values, CharacterSheet character)
    {
        character.RacialAbilities.AddRange(ReadSimpleIndexedValues(values, "abilities.racial"));
        foreach (string classAbility in ReadSimpleIndexedValues(values, "abilities.class"))
            character.Notes.Add($"Imported class ability: {classAbility}");
    }

    private static void ImportSpells(Dictionary<string, string> values, CharacterSheet character, RulesEngine rules, List<string> warnings)
    {
        Dictionary<string, SpellDefinition> spellsByName = rules.Spells
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        bool hasArcaneClass = character.ClassIds.Any(IsArcaneClass);
        bool hasDivineClass = character.ClassIds.Any(IsDivineClass);
        Dictionary<int, int> spellSlots = hasDivineClass && !hasArcaneClass
            ? character.DivineSpellSlots
            : character.ArcaneSpellSlots;
        if (hasArcaneClass && hasDivineClass && values.Any(pair =>
                pair.Key.StartsWith("spells.slots.", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(pair.Value)))
        {
            warnings.Add("Spell slots for a mixed arcane/divine class were imported as arcane slots; review them in the program.");
        }

        for (int level = 1; level <= 9; level++)
        {
            int? slots = TryGetInt(values, $"spells.slots.level_{level}", 0, 99, warnings);
            if (slots.HasValue)
                spellSlots[level] = slots.Value;
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < 112; index++)
        {
            string spellName = Get(values, $"spells.known.{index:000}.name");
            if (string.IsNullOrWhiteSpace(spellName) || !seenNames.Add(spellName))
                continue;

            if (spellsByName.TryGetValue(spellName, out SpellDefinition? definition))
            {
                character.TrackedSpellIds.Add(definition.Id);
                if (definition.Category.Contains("wizard", StringComparison.OrdinalIgnoreCase))
                    character.WizardSpellbookIds.Add(definition.Id);
            }
            else
            {
                character.Notes.Add($"Imported unresolved spell: {spellName}");
                warnings.Add($"Spell was preserved as a note but not matched to the catalog: {spellName}");
            }
        }
    }

    private static string ResolveRaceId(string raceName, RulesEngine rules, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(raceName))
        {
            warnings.Add("Race was blank; Human was used as a safe default.");
            return "human";
        }

        string normalized = NormalizeId(raceName);
        if (rules.Races.ContainsKey(normalized))
            return normalized;

        KeyValuePair<string, RaceDefinition>? match = rules.Races
            .FirstOrDefault(pair => string.Equals(pair.Value.Name, raceName, StringComparison.OrdinalIgnoreCase));
        if (match.HasValue && !string.IsNullOrWhiteSpace(match.Value.Key))
            return match.Value.Key;

        warnings.Add($"Race was not matched to the current rules catalog: {raceName}");
        return normalized;
    }

    private static List<string> ResolveClassIds(string className, RulesEngine rules, List<string> warnings)
    {
        string[] tokens = (className ?? string.Empty)
            .Split(new[] { '/', '+', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            warnings.Add("Class was blank; Fighter was used as a safe default.");
            return new List<string> { "fighter" };
        }

        var result = new List<string>();
        foreach (string token in tokens)
        {
            string normalized = NormalizeId(token);
            if (rules.Classes.ContainsKey(normalized))
            {
                result.Add(normalized);
                continue;
            }

            KeyValuePair<string, ClassDefinition>? match = rules.Classes
                .FirstOrDefault(pair => string.Equals(pair.Value.Name, token, StringComparison.OrdinalIgnoreCase));
            if (match.HasValue && !string.IsNullOrWhiteSpace(match.Value.Key))
                result.Add(match.Value.Key);
            else
            {
                result.Add(normalized);
                warnings.Add($"Class was not matched to the current rules catalog: {token}");
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool HasPlayersOptionValues(Dictionary<string, string> values)
        => !string.IsNullOrWhiteSpace(Get(values, "identity.character_points"))
            || values.Any(pair => pair.Key.StartsWith("subabilities.", StringComparison.OrdinalIgnoreCase)
                                  && !string.IsNullOrWhiteSpace(pair.Value));

    private static bool IsArcaneClass(string classId)
        => classId.Contains("mage", StringComparison.OrdinalIgnoreCase)
            || classId.Contains("wizard", StringComparison.OrdinalIgnoreCase)
            || classId.Contains("bard", StringComparison.OrdinalIgnoreCase);

    private static bool IsDivineClass(string classId)
        => classId.Contains("cleric", StringComparison.OrdinalIgnoreCase)
            || classId.Contains("druid", StringComparison.OrdinalIgnoreCase)
            || classId.Contains("paladin", StringComparison.OrdinalIgnoreCase)
            || classId.Contains("ranger", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ReadSimpleIndexedValues(Dictionary<string, string> values, string prefix)
        => values
            .Where(pair => pair.Key.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> ReadIndexedValues(Dictionary<string, string> values, string prefix, string suffix)
        => values
            .Where(pair => pair.Key.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase)
                           && pair.Key.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string Get(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out string? value) ? value.Trim() : string.Empty;

    private static int GetInt(Dictionary<string, string> values, string key, int fallback, int minimum, int maximum, List<string> warnings)
        => TryGetInt(values, key, minimum, maximum, warnings) ?? fallback;

    private static int? TryGetInt(Dictionary<string, string> values, string key, int minimum, int maximum, List<string> warnings)
    {
        string raw = Get(values, key);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (!int.TryParse(raw.Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            warnings.Add($"Ignored non-numeric value for {key}: {raw}");
            return null;
        }
        if (value < minimum || value > maximum)
        {
            warnings.Add($"Clamped {key} from {value} to the supported range {minimum}-{maximum}.");
            value = Math.Clamp(value, minimum, maximum);
        }
        return value;
    }

    private static string NormalizeId(string value)
        => string.Join("_", value.Trim().ToLowerInvariant()
            .Split(new[] { ' ', '-', '\'', '.' }, StringSplitOptions.RemoveEmptyEntries));
}