using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Utilities;

namespace DungeonMasterCortex.Services;

/// <summary>Loads and exposes AD&amp;D 2e rules from core_2e.json.</summary>
public class RulesEngine
{
    private static readonly string[] AbilityOrder =
        { "str", "dex", "con", "int", "wis", "cha" };
    private readonly Random _rng = new();

    private static readonly int[] StandardArray = { 15, 14, 13, 12, 10, 8 };

    public Dictionary<string, RaceDefinition>  Races   { get; } = new();
    public Dictionary<string, ClassDefinition> Classes { get; } = new();
    public List<KitDefinition>                 Kits    { get; } = new();
    public List<MultiClassComboGroup>          MultiClassCombos { get; } = new();
    public List<MonsterDefinition>             Monsters { get; } = new();
    public List<SpellDefinition>               Spells   { get; } = new();

    // ── Weapon data ───────────────────────────────────────────────────────────
    public List<WeaponDefinition>      Weapons      { get; } = new();
    public List<WeaponGroupDefinition> WeaponGroups { get; } = new();
    /// <summary>Flat lookup: tight_group_id → definition (built from WeaponGroups).</summary>
    public Dictionary<string, TightGroupDefinition> TightGroups { get; } = new();
    /// <summary>Flat lookup: weapon_id → WeaponDefinition.</summary>
    public Dictionary<string, WeaponDefinition> WeaponById { get; } = new();
    // WP slot rules: classId (lowercase) → (initial, perLevels)
    private readonly Dictionary<string, (int initial, int perLevels)> _wpSlotRules = new();
    private readonly HashSet<string> _specializationClasses   = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _poSpecializationClasses = new(StringComparer.OrdinalIgnoreCase);

    public RulesEngine()
    {
        LoadFallback();
        TryLoadFromFiles();
        TryLoadClassAbilitiesWorkbook();
        TryLoadWeapons();
        TryLoadKits();
        TryLoadMultiClassCombos();
        TryLoadMonsters();
        TryLoadSpells();
        PostProcessRules();
    }

    // ── Data loading ─────────────────────────────────────────────────────────

    private void TryLoadFromFiles()
    {
        var basePath = FindBaseRulesetPath();
        if (basePath is null) return;

        try
        {
            using var stream = File.OpenRead(basePath);
            var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (root.TryGetProperty("races", out var racesEl))
            {
                Races.Clear();
                LoadRaces(racesEl);
            }

            if (root.TryGetProperty("classes", out var classesEl))
            {
                Classes.Clear();
                LoadClasses(classesEl);
            }

            var overlayPath = FindPlayersOptionOverlayPath();
            if (overlayPath is not null)
            {
                using var overlayStream = File.OpenRead(overlayPath);
                var overlayDoc = JsonDocument.Parse(overlayStream);
                if (overlayDoc.RootElement.TryGetProperty("races", out var overlayRacesEl))
                    LoadRaces(overlayRacesEl);
                if (overlayDoc.RootElement.TryGetProperty("classes", out var overlayClassesEl))
                    LoadClasses(overlayClassesEl);
            }
        }
        catch { /* keep fallback data */ }
    }

    private void LoadClasses(JsonElement classesEl)
    {
        foreach (var cp in classesEl.EnumerateObject())
        {
            var mins = ParseIntDict(cp.Value, "ability_minimums");
            var allowed = ParseStringList(cp.Value, "allowed_races");
            var name = cp.Value.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? cp.Name : cp.Name;
            var classAbilities = ParseStructuredAbilities(cp.Value, "class_abilities");
            var budget = cp.Value.TryGetProperty("class_point_budget", out var budgetEl)
                && budgetEl.ValueKind == JsonValueKind.Number
                ? budgetEl.GetInt32()
                : 0;
            var specializations = ParseSpecializations(cp.Value);
            Classes[cp.Name] = new ClassDefinition(cp.Name, name, mins, allowed, classAbilities, budget, specializations);
        }
    }

    private static List<WizardSpecialization>? ParseSpecializations(JsonElement classEl)
    {
        if (!classEl.TryGetProperty("specializations", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var result = new List<WizardSpecialization>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id   = item.TryGetProperty("id",   out var idEl)   ? idEl.GetString()   ?? "" : "";
            var sname = item.TryGetProperty("name", out var snEl)   ? snEl.GetString()   ?? "" : "";
            var desc = item.TryGetProperty("description", out var dEl) ? dEl.GetString() ?? "" : "";
            var smins = ParseIntDict(item, "ability_minimums");
            var sallowed = ParseStringList(item, "allowed_races");
            var sbudget = item.TryGetProperty("class_point_budget", out var sbEl) && sbEl.ValueKind == JsonValueKind.Number
                ? sbEl.GetInt32() : 30;
            var autoIds = ParseStringList(item, "auto_select_ability_ids");
            var oppSchools = ParseStringList(item, "opposition_schools");
            result.Add(new WizardSpecialization(id, sname, desc, smins, sallowed, sbudget, autoIds, oppSchools));
        }
        return result.Count > 0 ? result : null;
    }

    private void LoadRaces(JsonElement racesEl)
    {
        foreach (var rp in racesEl.EnumerateObject())
        {
            var mins = ParseIntDict(rp.Value, "ability_minimums");
            var maxs = ParseIntDict(rp.Value, "ability_maximums");
            var mods = ParseIntDict(rp.Value, "ability_modifiers");    // NEW: racial ability adjustments
            var name = rp.Value.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? rp.Name : rp.Name;
            var mode = rp.Value.TryGetProperty("character_mode", out var modeEl)
                ? NormalizeCharacterMode(modeEl.GetString())
                : "all";
            var baseRaceId = rp.Value.TryGetProperty("base_race_id", out var baseRaceEl)
                ? baseRaceEl.GetString() ?? rp.Name
                : rp.Name;

            // Load structured abilities first; build legacy string list from them
            var structured = ParseStructuredAbilities(rp.Value, "racial_abilities");
            var legacyStrings = structured.Count > 0
                ? structured.Select(a => a.Description).ToList()
                : ParseStringList(rp.Value, "racial_abilities");

            var budget = rp.Value.TryGetProperty("racial_point_budget", out var budgetEl)
                && budgetEl.ValueKind == JsonValueKind.Number
                ? budgetEl.GetInt32()
                : InferRaceBudget(mode, rp.Name, structured);

            Races[rp.Name] = new RaceDefinition(rp.Name, name, mode, baseRaceId,
                mins, maxs, mods, legacyStrings, structured, budget);
        }
    }

    private static string? FindRulesDirectory()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir.FullName, "data", "rulesets");
            if (Directory.Exists(candidate)) return candidate;
            if (dir.Parent is null) break;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? FindBaseRulesetPath()
    {
        var rulesDir = FindRulesDirectory();
        if (rulesDir is null) return null;
        var path = Path.Combine(rulesDir, "core_2e.json");
        return File.Exists(path) ? path : null;
    }

    private static string? FindPlayersOptionOverlayPath()
    {
        var rulesDir = FindRulesDirectory();
        if (rulesDir is null) return null;
        var path = Path.Combine(rulesDir, "overlays", "players_option.json");
        return File.Exists(path) ? path : null;
    }

    private static string? FindAssetsDirectory()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir.FullName, "Assets");
            if (Directory.Exists(candidate))
                return candidate;
            if (dir.Parent is null)
                break;
            dir = dir.Parent;
        }
        return null;
    }

    private static string? FindClassAbilitiesWorkbookPath()
    {
        var assetsDir = FindAssetsDirectory();
        if (assetsDir is null)
            return null;

        var path = Path.Combine(assetsDir, "Class Abilities", "Class Abilities.xlsx");
        return File.Exists(path) ? path : null;
    }

    private void TryLoadClassAbilitiesWorkbook()
    {
        var workbookPath = FindClassAbilitiesWorkbookPath();
        if (workbookPath is null)
            return;

        try
        {
            using var workbook = new XLWorkbook(workbookPath);
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet is null)
                return;

            var headerMap = BuildWorkbookHeaderMap(sheet);
            if (headerMap.Count == 0)
                return;

            var importedByClass = new Dictionary<string, List<AbilityDefinition>>(StringComparer.OrdinalIgnoreCase);
            int lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;

            for (int rowIndex = 2; rowIndex <= lastRow; rowIndex++)
            {
                var row = sheet.Row(rowIndex);
                string classToken = ReadWorkbookString(row, headerMap, "ClassName");
                string abilityName = ReadWorkbookString(row, headerMap, "Name");
                if (string.IsNullOrWhiteSpace(classToken) || string.IsNullOrWhiteSpace(abilityName))
                    continue;

                if (ReadWorkbookBool(row, headerMap, "IsDisabled"))
                    continue;

                int pointCost = ReadWorkbookInt(row, headerMap, "PlayersOptionCpCost", "Cost");
                bool autoGranted = pointCost <= 0;
                string abilityLabel = $"{abilityName} ({pointCost} CP)";

                string longDescription = ReadWorkbookString(row, headerMap, "Description");
                string effectText = ReadWorkbookString(row, headerMap, "EffectText");

                string description = abilityLabel;
                if (!string.IsNullOrWhiteSpace(longDescription)
                    && !longDescription.Trim().Equals(abilityName, StringComparison.OrdinalIgnoreCase))
                {
                    description = $"{abilityLabel}: {longDescription.Trim()}";
                }

                if (string.IsNullOrWhiteSpace(longDescription)
                    && !string.IsNullOrWhiteSpace(effectText)
                    && !effectText.Trim().Equals(abilityName, StringComparison.OrdinalIgnoreCase))
                {
                    description = $"{abilityLabel}: {effectText.Trim()}";
                }

                string sourceBook = ReadWorkbookString(row, headerMap, "SourceBook");
                string mechanicsJson = ReadWorkbookString(row, headerMap, "MechanicalRulesJson");
                var effect = ParseWorkbookMechanics(mechanicsJson);
                if (IsEffectEmpty(effect))
                {
                    effect = InferWorkbookMechanics(abilityName, description, effectText);
                }

                var ability = new AbilityDefinition
                {
                    Id = Slugify($"{classToken}_{abilityName}"),
                    Description = description,
                    Category = sourceBook,
                    PointCost = pointCost,
                    AutoGranted = autoGranted,
                    Effect = effect,
                };

                var variants = ExpandMultiCostAbility(ability).ToList();
                foreach (var classId in ResolveWorkbookClassIds(classToken))
                {
                    if (!importedByClass.TryGetValue(classId, out var list))
                    {
                        list = new List<AbilityDefinition>();
                        importedByClass[classId] = list;
                    }

                    foreach (var variant in variants)
                    {
                        list.Add(new AbilityDefinition
                        {
                            Id = variant.Id,
                            Description = variant.Description,
                            Category = variant.Category,
                            PointCost = variant.PointCost,
                            AutoGranted = variant.AutoGranted,
                            Effect = CloneEffect(variant.Effect),
                        });
                    }
                }
            }

            foreach (var kvp in importedByClass)
            {
                if (!Classes.TryGetValue(kvp.Key, out var cls))
                    continue;

                var normalized = kvp.Value
                    .GroupBy(a => string.IsNullOrWhiteSpace(a.Id) ? Slugify(a.Description) : a.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                if (normalized.Count > 0)
                    Classes[kvp.Key] = cls with { StructuredAbilities = normalized };
            }
        }
        catch
        {
            // Keep existing class abilities if workbook import fails.
        }
    }

    private Dictionary<string, int> BuildWorkbookHeaderMap(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerRow = sheet.Row(1);
        int lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (int col = 1; col <= lastColumn; col++)
        {
            string text = headerRow.Cell(col).GetString().Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            map[NormalizeWorkbookHeader(text)] = col;
        }

        return map;
    }

    private static string NormalizeWorkbookHeader(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string ReadWorkbookString(IXLRow row, Dictionary<string, int> headerMap, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            string key = NormalizeWorkbookHeader(alias);
            if (!headerMap.TryGetValue(key, out int col))
                continue;

            string value = row.Cell(col).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static int ReadWorkbookInt(IXLRow row, Dictionary<string, int> headerMap, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            string key = NormalizeWorkbookHeader(alias);
            if (!headerMap.TryGetValue(key, out int col))
                continue;

            var cell = row.Cell(col);
            if (cell.TryGetValue<double>(out double numeric))
                return Convert.ToInt32(Math.Round(numeric));

            if (int.TryParse(cell.GetString().Trim(), out int parsed))
                return parsed;
        }

        return 0;
    }

    private static bool ReadWorkbookBool(IXLRow row, Dictionary<string, int> headerMap, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            string key = NormalizeWorkbookHeader(alias);
            if (!headerMap.TryGetValue(key, out int col))
                continue;

            var cell = row.Cell(col);
            if (cell.TryGetValue<bool>(out bool b))
                return b;

            if (cell.TryGetValue<double>(out double numeric))
                return Math.Abs(numeric) > double.Epsilon;

            string raw = cell.GetString().Trim();
            if (bool.TryParse(raw, out bool parsedBool))
                return parsedBool;

            if (raw == "1" || raw.Equals("yes", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private IEnumerable<string> ResolveWorkbookClassIds(string classToken)
    {
        var tokens = classToken
            .Split(new[] { '/', ';', ',', '+', '&', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (tokens.Count == 0)
            tokens.Add(classToken.Trim());

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            string normalizedToken = NormalizeWorkbookHeader(token);
            foreach (var cls in Classes.Values)
            {
                string idKey = NormalizeWorkbookHeader(cls.Id);
                string nameKey = NormalizeWorkbookHeader(cls.Name);

                if (normalizedToken == idKey || normalizedToken == nameKey)
                    ids.Add(cls.Id);
            }
        }

        return ids;
    }

    private static AbilityEffect ParseWorkbookMechanics(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return new AbilityEffect();

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            JsonElement mech = doc.RootElement;
            if (mech.ValueKind == JsonValueKind.Object
                && mech.TryGetProperty("mechanics", out var nested)
                && nested.ValueKind == JsonValueKind.Object)
            {
                mech = nested;
            }

            if (mech.ValueKind != JsonValueKind.Object)
                return new AbilityEffect();

            return new AbilityEffect
            {
                AcBonus = GetInt(mech, "ac_bonus"),
                AcBonusRequiresNoArmor = GetBool(mech, "ac_bonus_requires_no_armor") || GetBool(mech, "requires_no_armor"),
                AttackBonus = GetInt(mech, "attack_bonus"),
                DamageBonus = GetInt(mech, "damage_bonus"),
                MovementBonus = GetInt(mech, "movement_bonus"),
                XpModifierPercent = GetInt(mech, "xp_modifier_percent"),
                HpPerLevel = GetInt(mech, "hp_per_level"),
                HpFlatBonus = GetInt(mech, "hp_flat_bonus"),
                HpDiceExpression = GetString(mech, "hp_dice_expression"),
                NwpSlotBonus = GetInt(mech, "nwp_slot_bonus"),
                NwpCostReduction = GetInt(mech, "nwp_cost_reduction"),
                NwpCheckBonus = GetInt(mech, "nwp_check_bonus"),
                SurpriseBonus = GetInt(mech, "surprise_bonus"),
                InfravisionFeet = GetInt(mech, "infravision_feet"),
                MagicResistPercent = GetInt(mech, "magic_resist_percent"),
                ReactionBonus = GetInt(mech, "reaction_bonus"),
                GrantsStealth = GetBool(mech, "grants_stealth"),
                DetectSecretDoors = GetBool(mech, "detect_secret_doors"),
                DetectStonework = GetBool(mech, "detect_stonework"),
                SaveBonuses = ParseIntDict(mech, "save_bonuses"),
                SubAbilityBonuses = ParseIntDict(mech, "subability_bonuses"),
                EnemyAttackBonuses = ParseIntDict(mech, "enemy_attack_bonuses"),
                EnemyDamageBonuses = ParseIntDict(mech, "enemy_damage_bonuses"),
                WeaponAttackBonuses = ParseIntDict(mech, "weapon_attack_bonuses"),
                WeaponDamageBonuses = ParseIntDict(mech, "weapon_damage_bonuses"),
            };
        }
        catch
        {
            return new AbilityEffect();
        }
    }

    private static AbilityEffect InferWorkbookMechanics(string abilityName, string description, string effectText)
    {
        string key = NormalizeWorkbookHeader(abilityName);
        string text = $"{description} {effectText}".ToLowerInvariant();
        var effect = new AbilityEffect();

        // Starter deterministic mappings for known abilities in the current workbook.
        switch (key)
        {
            case "learningbonus":
                effect.NwpCostReduction = 1;
                return effect;

            case "appearancebonus":
                effect.ReactionBonus = 1;
                return effect;

            case "fascinate":
                effect.ReactionBonus = 1;
                return effect;

            case "goodreputation":
                effect.ReactionBonus = 2;
                return effect;

            case "improvedthac0":
                effect.AttackBonus = 2;
                return effect;

            case "anomalousintuition":
                effect.DetectSecretDoors = true;
                return effect;

            case "athleticism":
                effect.MovementBonus = 1;
                return effect;

            case "combatsense":
                effect.SurpriseBonus = 1;
                return effect;

            case "coupdegras":
            case "warcry":
                effect.DamageBonus = 1;
                return effect;

            case "iaijutsu":
                effect.AttackBonus = 1;
                return effect;
        }

        // Broad keyword fallback mappings for common mechanics phrasing.
        if (text.Contains("secret door", StringComparison.OrdinalIgnoreCase))
            effect.DetectSecretDoors = true;

        if (text.Contains("stealth", StringComparison.OrdinalIgnoreCase)
            || text.Contains("hide", StringComparison.OrdinalIgnoreCase)
            || text.Contains("move silently", StringComparison.OrdinalIgnoreCase))
        {
            effect.GrantsStealth = true;
        }

        if (text.Contains("surprise", StringComparison.OrdinalIgnoreCase))
            effect.SurpriseBonus = 1;

        return effect;
    }

    private static bool IsEffectEmpty(AbilityEffect effect)
    {
        return effect.AcBonus == 0
            && !effect.AcBonusRequiresNoArmor
            && effect.AttackBonus == 0
            && effect.DamageBonus == 0
            && effect.MovementBonus == 0
            && effect.XpModifierPercent == 0
            && effect.HpPerLevel == 0
            && effect.HpFlatBonus == 0
            && string.IsNullOrWhiteSpace(effect.HpDiceExpression)
            && effect.NwpSlotBonus == 0
            && effect.NwpCostReduction == 0
            && effect.NwpCheckBonus == 0
            && effect.SurpriseBonus == 0
            && effect.InfravisionFeet == 0
            && effect.MagicResistPercent == 0
            && effect.ReactionBonus == 0
            && !effect.GrantsStealth
            && !effect.DetectSecretDoors
            && !effect.DetectStonework
            && effect.SaveBonuses.Count == 0
            && effect.SubAbilityBonuses.Count == 0
            && effect.EnemyAttackBonuses.Count == 0
            && effect.EnemyDamageBonuses.Count == 0
            && effect.WeaponAttackBonuses.Count == 0
            && effect.WeaponDamageBonuses.Count == 0;
    }

    private static string? FindWeaponsDataPath()
    {
        var rulesDir = FindRulesDirectory();
        if (rulesDir is null) return null;
        var path = Path.Combine(rulesDir, "weapons.json");
        return File.Exists(path) ? path : null;
    }

    private static string? FindKitsDataPath()
    {
        var rulesDir = FindRulesDirectory();
        if (rulesDir is null) return null;
        var path = Path.Combine(rulesDir, "kits.json");
        return File.Exists(path) ? path : null;
    }

    private static string? FindSpellsDataPath()
    {
        var rulesDir = FindRulesDirectory();
        if (rulesDir is null) return null;
        var path = Path.Combine(rulesDir, "spells.json");
        return File.Exists(path) ? path : null;
    }

    /// <summary>Persists the supplied kit list to kits.json and reloads.</summary>
    public void SaveKitDefinitions(IEnumerable<KitDefinition> kits)
    {
        var rulesDir = FindRulesDirectory();
        if (rulesDir is null) return;
        var path = Path.Combine(rulesDir, "kits.json");

        var kitList = kits.ToList();
        using var stream = File.Create(path);
        using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteStartArray("kits");
        foreach (var k in kitList)
        {
            writer.WriteStartObject();
            writer.WriteString("id", k.Id);
            writer.WriteString("name", k.Name);
            writer.WriteString("source", k.Source);
            writer.WriteString("description", k.Description);
            writer.WriteStartArray("allowed_races");
            foreach (var r in k.AllowedRaces) writer.WriteStringValue(r);
            writer.WriteEndArray();
            writer.WriteStartArray("allowed_classes");
            foreach (var c in k.AllowedClasses) writer.WriteStringValue(c);
            writer.WriteEndArray();
            writer.WriteStartArray("free_nwps");
            foreach (var n in k.FreeNwpIds) writer.WriteStringValue(n);
            writer.WriteEndArray();
            writer.WriteStartArray("required_nwps");
            foreach (var n in k.RequiredNwpIds) writer.WriteStringValue(n);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        stream.Close();

        TryLoadKits(); // reload in-memory list
    }

    private void TryLoadKits()
    {
        var path = FindKitsDataPath();
        if (path is null) return;

        try
        {
            using var stream = File.OpenRead(path);
            var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (!root.TryGetProperty("kits", out var kitsEl)) return;

            Kits.Clear();
            foreach (var k in kitsEl.EnumerateArray())
            {
                string id          = k.TryGetProperty("id",          out var idEl)   ? idEl.GetString()   ?? "" : "";
                string name        = k.TryGetProperty("name",        out var nEl)    ? nEl.GetString()    ?? "" : "";
                string description = k.TryGetProperty("description", out var dEl)    ? dEl.GetString()    ?? "" : "";
                string source      = k.TryGetProperty("source",      out var srcEl)  ? srcEl.GetString()  ?? "" : "";

                var allowedRaces = new List<string>();
                if (k.TryGetProperty("allowed_races", out var racesEl))
                    foreach (var r in racesEl.EnumerateArray())
                        if (r.GetString() is string rs) allowedRaces.Add(rs);

                var allowedClasses = new List<string>();
                if (k.TryGetProperty("allowed_classes", out var classesEl))
                    foreach (var c in classesEl.EnumerateArray())
                        if (c.GetString() is string cs) allowedClasses.Add(cs);

                var freeNwpIds = new List<string>();
                if (k.TryGetProperty("free_nwps", out var freeEl))
                    foreach (var n in freeEl.EnumerateArray())
                        if (n.GetString() is string ns) freeNwpIds.Add(ns);

                var requiredNwpIds = new List<string>();
                if (k.TryGetProperty("required_nwps", out var reqEl))
                    foreach (var n in reqEl.EnumerateArray())
                        if (n.GetString() is string rs) requiredNwpIds.Add(rs);

                if (!string.IsNullOrEmpty(id))
                    Kits.Add(new KitDefinition(id, name, description, source, allowedRaces, allowedClasses, freeNwpIds, requiredNwpIds));
            }
        }
        catch { /* non-fatal: kits are optional */ }
    }

    private static string? FindMonstersDataPath()
    {
        var rulesDir = FindRulesDirectory();
        if (rulesDir is null) return null;
        var path = Path.Combine(rulesDir, "monsters.json");
        return File.Exists(path) ? path : null;
    }

    private void TryLoadMonsters()
    {
        var path = FindMonstersDataPath();
        if (path is null) return;

        try
        {
            using var stream = File.OpenRead(path);
            var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (!root.TryGetProperty("monsters", out var monstersEl)) return;

            static string ReadString(JsonElement obj, string propName)
            {
                if (!obj.TryGetProperty(propName, out var p)) return "";
                return p.ValueKind switch
                {
                    JsonValueKind.String => p.GetString() ?? "",
                    JsonValueKind.Null => "",
                    _ => p.ToString() ?? "",
                };
            }

            static int ReadInt(JsonElement obj, string propName, int defaultValue)
            {
                if (!obj.TryGetProperty(propName, out var p)) return defaultValue;
                if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int n)) return n;
                if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out n)) return n;
                return defaultValue;
            }

            Monsters.Clear();
            foreach (var m in monstersEl.EnumerateArray())
            {
                try
                {
                    string id          = ReadString(m, "id");
                    string name        = ReadString(m, "name");
                    string monType     = ReadString(m, "monster_type");
                    string source      = ReadString(m, "source");
                    string hitDice     = ReadString(m, "hit_dice");
                    int    ac          = ReadInt(m, "armor_class", 10);
                    string movement    = ReadString(m, "movement");
                    string thac0Text   = ReadString(m, "thac0");
                    int    thac0       = ReadInt(m, "thac0", 20);
                    int    attacks     = ReadInt(m, "attacks", 1);
                    string damage      = ReadString(m, "damage");
                    string specAtk     = ReadString(m, "special_attacks");
                    string specDef     = ReadString(m, "special_defenses");
                    string magicRes    = ReadString(m, "magic_resistance");
                    string size        = ReadString(m, "size");
                    string morale      = ReadString(m, "morale");
                    int    xp          = ReadInt(m, "xp_value", 0);
                    string numAppear   = ReadString(m, "number_appearing");
                    string frequency   = ReadString(m, "frequency");
                    string intel       = ReadString(m, "intelligence");
                    string alignment   = ReadString(m, "alignment");
                    string treasure    = ReadString(m, "treasure_type");
                    string description = ReadString(m, "description");
                    string specialAbilities = ReadString(m, "special_abilities");
                    string combat = ReadString(m, "combat");
                    string habitatSociety = ReadString(m, "habitat_society");
                    string ecology = ReadString(m, "ecology");

                    if (string.IsNullOrEmpty(id)) continue;

                    List<DragonAgeStage>? ageStages = null;
                    if (m.TryGetProperty("age_stages", out var stagesEl) && stagesEl.ValueKind == JsonValueKind.Array)
                    {
                        ageStages = new List<DragonAgeStage>();
                        foreach (var s in stagesEl.EnumerateArray())
                        {
                            int    stNum  = ReadInt(s, "stage", 0);
                            string cat    = ReadString(s, "category");
                            string shd    = ReadString(s, "hit_dice");
                            int    sac    = ReadInt(s, "armor_class", 10);
                            int    sth    = ReadInt(s, "thac0", 20);
                            string bw     = ReadString(s, "breath_weapon");
                            string splvl  = ReadString(s, "spell_level");
                            string mr     = ReadString(s, "magic_resistance");
                            string spAbl  = ReadString(s, "special_abilities");
                            ageStages.Add(new DragonAgeStage(stNum, cat, shd, sac, sth, bw, splvl, mr, spAbl));
                        }
                    }

                    Monsters.Add(new MonsterDefinition(id, name, monType, source, hitDice, ac, movement,
                        thac0, attacks, damage, specAtk, specDef, magicRes, size, morale, xp,
                        numAppear, frequency, intel, alignment, treasure, description, ageStages,
                        specialAbilities, combat, habitatSociety, ecology)
                    {
                        Thac0Text = thac0Text
                    });
                }
                catch { /* skip malformed monster entry and continue loading others */ }
            }
        }
        catch { /* non-fatal */ }
    }

    /// <summary>Persists the supplied monster list to monsters.json and reloads.</summary>
    public void SaveMonsterDefinitions(IEnumerable<MonsterDefinition> monsters)
    {
        var rulesDir = FindRulesDirectory();
        if (rulesDir is null) return;
        var path = Path.Combine(rulesDir, "monsters.json");

        var list = monsters.ToList();
        using var stream = File.Create(path);
        using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteStartArray("monsters");
        foreach (var m in list)
        {
            writer.WriteStartObject();
            writer.WriteString("id",               m.Id);
            writer.WriteString("name",             m.Name);
            writer.WriteString("monster_type",     m.MonsterType);
            writer.WriteString("source",           m.Source);
            writer.WriteString("hit_dice",         m.HitDice);
            writer.WriteNumber("armor_class",      m.ArmorClass);
            writer.WriteString("movement",         m.Movement);
            if (int.TryParse(m.EffectiveThac0Text, out int thac0Numeric))
            {
                writer.WriteNumber("thac0", thac0Numeric);
            }
            else
            {
                writer.WriteString("thac0", m.EffectiveThac0Text);
                writer.WriteString("thac0_text", m.EffectiveThac0Text);
            }
            writer.WriteNumber("attacks",          m.Attacks);
            writer.WriteString("damage",           m.Damage);
            writer.WriteString("special_attacks",  m.SpecialAttacks);
            writer.WriteString("special_defenses", m.SpecialDefenses);
            writer.WriteString("magic_resistance", m.MagicResistance);
            writer.WriteString("size",             m.Size);
            writer.WriteString("morale",           m.Morale);
            writer.WriteNumber("xp_value",         m.XpValue);
            writer.WriteString("number_appearing", m.NumberAppearing);
            writer.WriteString("frequency",        m.Frequency);
            writer.WriteString("intelligence",     m.Intelligence);
            writer.WriteString("alignment",        m.Alignment);
            writer.WriteString("treasure_type",    m.TreasureType);
            writer.WriteString("description",      m.Description);
            writer.WriteString("special_abilities", m.SpecialAbilities);
            writer.WriteString("combat",            m.Combat);
            writer.WriteString("habitat_society",   m.HabitatSociety);
            writer.WriteString("ecology",           m.Ecology);
            if (m.HitDiceMin.HasValue) writer.WriteNumber("hit_dice_min", m.HitDiceMin.Value);
            if (m.HitDiceMax.HasValue) writer.WriteNumber("hit_dice_max", m.HitDiceMax.Value);
            if (m.Thac0Min.HasValue) writer.WriteNumber("thac0_min", m.Thac0Min.Value);
            if (m.Thac0Max.HasValue) writer.WriteNumber("thac0_max", m.Thac0Max.Value);
            // --- age stages (dragons) ---
            if (m.AgeStages is { Count: > 0 })
            {
                writer.WriteStartArray("age_stages");
                foreach (var s in m.AgeStages)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("stage",             s.StageNumber);
                    writer.WriteString("category",          s.Category);
                    writer.WriteString("hit_dice",          s.HitDice);
                    writer.WriteNumber("armor_class",       s.ArmorClass);
                    writer.WriteNumber("thac0",             s.Thac0);
                    writer.WriteString("breath_weapon",     s.BreathWeapon);
                    writer.WriteString("spell_level",       s.SpellLevel);
                    writer.WriteString("magic_resistance",  s.MagicResistance);
                    writer.WriteString("special_abilities", s.SpecialAbilities);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        stream.Close();

        TryLoadMonsters();
    }

    private void TryLoadSpells()
    {
        var path = FindSpellsDataPath();
        if (path is null) return;

        try
        {
            using var stream = File.OpenRead(path);
            var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (!root.TryGetProperty("spells", out var spellsEl)) return;

            static string ReadString(JsonElement obj, string propName)
            {
                if (!obj.TryGetProperty(propName, out var p)) return "";
                return p.ValueKind switch
                {
                    JsonValueKind.String => p.GetString() ?? "",
                    JsonValueKind.Null => "",
                    _ => p.ToString() ?? "",
                };
            }

            static bool ReadBool(JsonElement obj, string propName)
            {
                if (!obj.TryGetProperty(propName, out var p)) return false;
                return p.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => bool.TryParse(p.GetString(), out bool parsed) && parsed,
                    _ => false,
                };
            }

            Spells.Clear();
            foreach (var s in spellsEl.EnumerateArray())
            {
                try
                {
                    string id = ReadString(s, "id");
                    if (string.IsNullOrWhiteSpace(id))
                        id = ReadString(s, "spell_id");

                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    Spells.Add(new SpellDefinition(
                        id,
                        ReadString(s, "category"),
                        ReadString(s, "level"),
                        ReadString(s, "name"),
                        ReadString(s, "reversal"),
                        ReadString(s, "schools"),
                        ReadString(s, "range"),
                        ReadString(s, "components"),
                        ReadString(s, "materials"),
                        ReadString(s, "cast_time"),
                        ReadString(s, "duration"),
                        ReadString(s, "area"),
                        ReadString(s, "save"),
                        ReadString(s, "frequency"),
                        ReadString(s, "volume"),
                        ReadString(s, "page"),
                        ReadBool(s, "is_healing"),
                        ReadString(s, "damage"),
                        ReadString(s, "damage_step"),
                        ReadString(s, "damage_scale_start_level"),
                        ReadString(s, "damage_scale_every_levels"),
                        ReadString(s, "damage_max_at_level"),
                        ReadString(s, "damage_max"),
                        ReadString(s, "brief_description"),
                        ReadString(s, "description")));
                }
                catch { }
            }
        }
        catch { }
    }

    public void SaveSpellDefinitions(IEnumerable<SpellDefinition> spells)
    {
        var rulesDir = FindRulesDirectory();
        if (rulesDir is null) return;
        var path = Path.Combine(rulesDir, "spells.json");

        var list = spells.ToList();
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteStartArray("spells");
        foreach (var spell in list)
        {
            writer.WriteStartObject();
            writer.WriteString("id", spell.Id);
            writer.WriteString("category", spell.Category);
            writer.WriteString("level", spell.Level);
            writer.WriteString("name", spell.Name);
            writer.WriteString("reversal", spell.Reversal);
            writer.WriteString("schools", spell.Schools);
            writer.WriteString("range", spell.Range);
            writer.WriteString("components", spell.Components);
            writer.WriteString("materials", spell.Materials);
            writer.WriteString("cast_time", spell.CastTime);
            writer.WriteString("duration", spell.Duration);
            writer.WriteString("area", spell.Area);
            writer.WriteString("save", spell.Save);
            writer.WriteString("frequency", spell.Frequency);
            writer.WriteString("volume", spell.Volume);
            writer.WriteString("page", spell.Page);
            writer.WriteBoolean("is_healing", spell.IsHealing);
            writer.WriteString("damage", spell.Damage);
            writer.WriteString("damage_step", spell.DamageStep);
            writer.WriteString("damage_scale_start_level", spell.DamageScaleStartLevel);
            writer.WriteString("damage_scale_every_levels", spell.DamageScaleEveryLevels);
            writer.WriteString("damage_max_at_level", spell.DamageMaxAtLevel);
            writer.WriteString("damage_max", spell.DamageMax);
            writer.WriteString("brief_description", spell.BriefDescription);
            writer.WriteString("description", spell.Description);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();

        TryLoadSpells();
    }

    /// <summary>Returns kits available for the given race and class combination.</summary>
    public List<KitDefinition> KitsFor(string raceId, string classId = "")
    {
        if (!Races.TryGetValue(raceId, out var race)) return new List<KitDefinition>();
        string baseRaceId = race.BaseRaceId ?? "";

        return Kits.Where(kit =>
        {
            bool raceOk = kit.AllowedRaces.Count == 0
                || kit.AllowedRaces.Contains(raceId, StringComparer.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(baseRaceId) && kit.AllowedRaces.Contains(baseRaceId, StringComparer.OrdinalIgnoreCase));

            if (!raceOk) return false;

            if (string.IsNullOrEmpty(classId) || kit.AllowedClasses.Count == 0) return true;

            // Match class loosely: kit "fighter" matches "fighter", "fighter/mage", etc.
            return kit.AllowedClasses.Any(kc =>
                classId.Contains(kc, StringComparison.OrdinalIgnoreCase) ||
                kc.Contains(classId, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }

    private static string? FindMultiClassCombosPath()
    {
        var rulesDir = FindRulesDirectory();
        if (rulesDir is null) return null;
        var path = Path.Combine(rulesDir, "multiclass_combos.json");
        return File.Exists(path) ? path : null;
    }

    private void TryLoadMultiClassCombos()
    {
        var path = FindMultiClassCombosPath();
        if (path is null) return;

        try
        {
            using var stream = File.OpenRead(path);
            var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (!root.TryGetProperty("multiclass_combos", out var combosEl)) return;

            MultiClassCombos.Clear();
            foreach (var entry in combosEl.EnumerateObject())
            {
                string raceGroup = entry.Name;
                string source = entry.Value.TryGetProperty("source", out var srcEl)
                    ? srcEl.GetString() ?? "" : "";
                var combos = new List<List<string>>();
                if (entry.Value.TryGetProperty("combos", out var comboArr))
                {
                    foreach (var combo in comboArr.EnumerateArray())
                    {
                        var classList = new List<string>();
                        foreach (var cls in combo.EnumerateArray())
                            if (cls.GetString() is string cs) classList.Add(cs);
                        if (classList.Count >= 2) combos.Add(classList);
                    }
                }

                if (!string.IsNullOrEmpty(raceGroup))
                    MultiClassCombos.Add(new MultiClassComboGroup(raceGroup, source, combos));
            }
        }
        catch { /* non-fatal */ }
    }

    /// <summary>Saves multi-class combos to multiclass_combos.json and reloads.</summary>
    public void SaveMultiClassCombos(IEnumerable<MultiClassComboGroup> groups)
    {
        var rulesDir = FindRulesDirectory();
        if (rulesDir is null) return;
        var path = Path.Combine(rulesDir, "multiclass_combos.json");

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteStartObject("multiclass_combos");
        foreach (var g in groups)
        {
            writer.WriteStartObject(g.RaceGroup);
            writer.WriteString("source", g.Source);
            writer.WriteStartArray("combos");
            foreach (var combo in g.Combos)
            {
                writer.WriteStartArray();
                foreach (var cls in combo) writer.WriteStringValue(cls);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();

        TryLoadMultiClassCombos();
    }

    private void TryLoadWeapons()
    {
        var path = FindWeaponsDataPath();
        if (path is null) return;

        try
        {
            using var stream = File.OpenRead(path);
            var doc  = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            // Load broad weapon groups (and their tight groups)
            if (root.TryGetProperty("weapon_groups", out var groupsEl))
            {
                foreach (var g in groupsEl.EnumerateArray())
                {
                    var group = new WeaponGroupDefinition
                    {
                        Id          = g.TryGetProperty("id",          out var idEl)   ? idEl.GetString() ?? "" : "",
                        Name        = g.TryGetProperty("name",        out var nEl)    ? nEl.GetString()  ?? "" : "",
                        Description = g.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "",
                    };
                    if (g.TryGetProperty("tight_groups", out var tgArr))
                    {
                        foreach (var tg in tgArr.EnumerateArray())
                        {
                            var tight = new TightGroupDefinition
                            {
                                Id   = tg.TryGetProperty("id",   out var tgId)   ? tgId.GetString()  ?? "" : "",
                                Name = tg.TryGetProperty("name", out var tgName) ? tgName.GetString() ?? "" : "",
                            };
                            if (tg.TryGetProperty("weapon_ids", out var wids))
                                foreach (var wid in wids.EnumerateArray())
                                    if (wid.GetString() is string s) tight.WeaponIds.Add(s);
                            group.TightGroups.Add(tight);
                            if (!string.IsNullOrWhiteSpace(tight.Id))
                                TightGroups[tight.Id] = tight;
                        }
                    }
                    WeaponGroups.Add(group);
                }
            }

            // Load individual weapons
            if (root.TryGetProperty("weapons", out var weaponsEl))
            {
                foreach (var w in weaponsEl.EnumerateArray())
                {
                    var weapon = new WeaponDefinition
                    {
                        Id             = w.TryGetProperty("id",              out var wId)  ? wId.GetString()  ?? "" : "",
                        Name           = w.TryGetProperty("name",            out var wN)   ? wN.GetString()   ?? "" : "",
                        GroupId        = w.TryGetProperty("group_id",        out var wG)   ? wG.GetString()   ?? "" : "",
                        TightGroupId   = w.TryGetProperty("tight_group_id",  out var wTG)  ? wTG.GetString()  ?? "" : "",
                        DamageSm       = w.TryGetProperty("damage_sm",       out var wDs)  ? wDs.GetString()  ?? "1d6" : "1d6",
                        DamageL        = w.TryGetProperty("damage_l",        out var wDl)  ? wDl.GetString()  ?? "1d6" : "1d6",
                        Speed          = w.TryGetProperty("speed",           out var wSpd) && wSpd.ValueKind == JsonValueKind.Number ? wSpd.GetInt32() : 5,
                        Type           = w.TryGetProperty("type",            out var wT)   ? wT.GetString()   ?? "B" : "B",
                        Size           = w.TryGetProperty("size",            out var wSz)  ? wSz.GetString()  ?? "M" : "M",
                        AttacksPerRound = w.TryGetProperty("attacks_per_round", out var wApr) ? wApr.GetString() ?? "1" : "1",
                        Notes          = w.TryGetProperty("notes",           out var wNotes) ? wNotes.GetString() ?? "" : "",
                    };
                    Weapons.Add(weapon);
                    if (!string.IsNullOrWhiteSpace(weapon.Id))
                        WeaponById[weapon.Id] = weapon;
                }
            }

            // Load WP slot rules
            if (root.TryGetProperty("weapon_proficiency_slots_by_class", out var slotRulesEl))
            {
                foreach (var kv in slotRulesEl.EnumerateObject())
                {
                    int init = kv.Value.TryGetProperty("initial",    out var iEl) && iEl.ValueKind == JsonValueKind.Number ? iEl.GetInt32() : 2;
                    int per  = kv.Value.TryGetProperty("per_levels", out var pEl) && pEl.ValueKind == JsonValueKind.Number ? pEl.GetInt32() : 4;
                    _wpSlotRules[kv.Name.ToLowerInvariant()] = (init, per);
                }
            }

            // Load specialization rules
            if (root.TryGetProperty("specialization_rules", out var specRulesEl))
            {
                if (specRulesEl.TryGetProperty("classes_that_can_specialize", out var specClasses))
                    foreach (var c in specClasses.EnumerateArray())
                        if (c.GetString() is string s) _specializationClasses.Add(s);

                if (specRulesEl.TryGetProperty("players_option_classes_that_can_specialize", out var poSpecClasses))
                    foreach (var c in poSpecClasses.EnumerateArray())
                        if (c.GetString() is string s) _poSpecializationClasses.Add(s);
            }
        }
        catch { /* weapon data is non-critical */ }
    }

    // ── Weapon Proficiency Public API ─────────────────────────────────────────

    /// <summary>
    /// Returns the number of weapon proficiency slots available to a character
    /// of the given class at the given level.
    /// For multi-class, pass the primary (or most generous) class id.
    /// </summary>
    public int GetWeaponProficiencySlots(string classId, int level)
    {
        string key = (classId ?? "").Trim().ToLowerInvariant();
        if (!_wpSlotRules.TryGetValue(key, out var rule))
            return 2; // default fallback
        int extra = level <= 1 ? 0 : (int)Math.Floor((level - 1.0) / rule.perLevels);
        return rule.initial + extra;
    }

    /// <summary>
    /// Returns whether the given class (optionally in PO mode) can specialize in a weapon.
    /// </summary>
    public bool CanSpecializeInWeapons(string classId, bool playersOption = false)
    {
        if (_specializationClasses.Count == 0) return false; // not loaded yet
        string key = (classId ?? "").Trim().ToLowerInvariant();
        if (playersOption && _poSpecializationClasses.Count > 0)
            return _poSpecializationClasses.Contains(key);
        return _specializationClasses.Contains(key);
    }

    private enum WeaponCostFamily
    {
        Warrior,
        Rogue,
        Priest,
        Wizard,
    }

    private enum WeaponAccessTier
    {
        Wizard,
        RoguePriest,
        Warrior,
    }

    private static readonly HashSet<string> WarriorWeaponClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "fighter", "paladin", "ranger", "warrior",
    };

    private static readonly HashSet<string> RogueWeaponClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "thief", "bard", "rogue",
    };

    // PHB bard "can use any weapon" — no crossover surcharge, only rogue-tier base cost applies.
    private static readonly HashSet<string> AnyWeaponClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "bard",
    };

    private static readonly HashSet<string> PriestWeaponClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "cleric", "druid", "priest",
    };

    private static readonly HashSet<string> WizardWeaponClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "wizard", "mage", "illusionist",
    };

    private static readonly HashSet<string> WizardTierWeaponIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "dagger", "knife", "dart", "sling", "quarterstaff",
    };

    private static readonly HashSet<string> RoguePriestTierWeaponIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "club", "jo_stick", "footmans_mace", "horsemans_mace", "morning_star",
        "hand_crossbow", "short_bow", "dagger", "knife", "main_gauche", "short_sword", "cutlass",
        "long_sword", "broad_sword", "scimitar", "warhammer", "maul",
        "horsemans_flail", "footmans_flail", "chain", "nunchaku",
        "sling", "staff_sling", "spear", "quarterstaff",
    };

    private static readonly HashSet<string> RoguePriestTierGroupIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "clubbing_weapons", "flails_chains", "hammers", "slings", "staffs",
    };

    public bool CanBuyWeaponGroups(IEnumerable<string> classIds)
        => (classIds ?? Array.Empty<string>()).Any(IsWarriorWeaponClass);

    /// <summary>
    /// Returns the AD&amp;D 2e non-proficiency THAC0 penalty for the given class (PHB p.52):
    /// Warriors = −2, Rogues = −3, Priests = −3, Wizards = −5.
    /// For multi-class characters, uses the least severe (best) penalty.
    /// </summary>
    public static int GetNonProficiencyPenalty(string classId)
    {
        if (WarriorWeaponClasses.Contains(classId)) return 2;
        if (RogueWeaponClasses.Contains(classId))   return 3;
        if (PriestWeaponClasses.Contains(classId))  return 3;
        if (WizardWeaponClasses.Contains(classId))  return 5;
        return 3; // default rogue-tier
    }

    /// <summary>
    /// Returns the non-proficiency penalty for a multi-class character,
    /// using the least severe penalty among all classes.
    /// </summary>
    public static int GetNonProficiencyPenalty(IEnumerable<string> classIds)
    {
        int best = 5;
        foreach (var id in classIds ?? Array.Empty<string>())
            best = Math.Min(best, GetNonProficiencyPenalty(id));
        return best;
    }

    /// <summary>
    /// Returns the INT bonus CPs from PHB Table 4 "# of languages" column.
    /// Used as bonus CPs for INT-based NWPs (and weapon proficiencies for warriors).
    /// </summary>
    public static int GetIntBonusCps(int intScore) => intScore switch
    {
        <= 8  => 0,
        9     => 1,
        10 or 11 => 2,
        12 or 13 => 3,
        14 or 15 => 4,
        16    => 5,
        17    => 6,
        _     => 7,  // 18+
    };

    /// <summary>
    /// Returns the NWP CP budget: class modifier (warrior/rogue=6, wizard/priest=8)
    /// plus INT bonus CPs (from PHB Table 4).
    /// For multi-class, uses the highest modifier among all classes.
    /// </summary>
    public int GetNwpCpBudget(IEnumerable<string> classIds, int intScore)
    {
        int classModifier = GetNwpClassModifier(classIds);
        int intBonus = GetIntBonusCps(intScore);
        return classModifier + intBonus;
    }

    private static int GetNwpClassModifier(IEnumerable<string> classIds)
    {
        int best = 0;
        foreach (var classId in classIds ?? Array.Empty<string>())
        {
            int mod = GetNwpClassModifierForSingleClass(classId);
            if (mod > best) best = mod;
        }
        return best == 0 ? 6 : best;
    }

    private static int GetNwpClassModifierForSingleClass(string classId)
    {
        if (WarriorWeaponClasses.Contains(classId)) return 6;
        if (RogueWeaponClasses.Contains(classId))   return 6;
        if (PriestWeaponClasses.Contains(classId))  return 8;
        if (WizardWeaponClasses.Contains(classId))  return 8;
        return 6;
    }

    /// <summary>
    /// Returns the weapon CP allotment (from S&amp;P Chapter 7 introduction):
    /// warriors=8, priests=8, rogues=6, wizards=3.
    /// Warriors additionally receive INT bonus CPs for weapon proficiencies.
    /// For multi-class, uses the highest allotment.
    /// </summary>
    public int GetWeaponCpBudget(IEnumerable<string> classIds, int intScore)
    {
        var classIdList = (classIds ?? Array.Empty<string>()).ToList();
        int allotment = GetWeaponClassAllotment(classIdList);
        bool hasWarrior = classIdList.Any(id => WarriorWeaponClasses.Contains(id));
        int intBonus = hasWarrior ? GetIntBonusCps(intScore) : 0;
        return allotment + intBonus;
    }

    private static int GetWeaponClassAllotment(IEnumerable<string> classIds)
    {
        int best = 0;
        foreach (var classId in classIds ?? Array.Empty<string>())
        {
            int allotment = GetWeaponClassAllotmentForSingleClass(classId);
            if (allotment > best) best = allotment;
        }
        return best == 0 ? 6 : best;
    }

    private static int GetWeaponClassAllotmentForSingleClass(string classId)
    {
        if (WarriorWeaponClasses.Contains(classId)) return 8;
        if (PriestWeaponClasses.Contains(classId))  return 8;
        if (RogueWeaponClasses.Contains(classId))   return 6;
        if (WizardWeaponClasses.Contains(classId))  return 3;
        return 6;
    }

    public int GetWeaponProficiencyCpCost(IEnumerable<string> classIds, string proficiencyId, string profType, bool specialized = false, int characterLevel = -1)
    {
        var effectiveClassIds = (classIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (effectiveClassIds.Count == 0)
            effectiveClassIds.Add("fighter");

        bool isMultiClass = effectiveClassIds.Count > 1;
        int bestCost = int.MaxValue;

        foreach (var classId in effectiveClassIds)
        {
            int? cost = GetWeaponProficiencyCpCostForClass(classId, proficiencyId, profType, specialized, isMultiClass, characterLevel);
            if (cost.HasValue)
                bestCost = Math.Min(bestCost, cost.Value);
        }

        return bestCost == int.MaxValue ? -1 : bestCost;
    }

    public int GetTotalWeaponProficiencyCpUsed(IEnumerable<string> classIds, IEnumerable<WeaponProficiencySelection> selections)
    {
        int total = 0;
        foreach (var selection in selections)
        {
            int cost = GetWeaponProficiencyCpCost(classIds, selection.ProficiencyId, selection.ProficiencyType, selection.Specialized);
            if (cost < 0)
                cost = GetWeaponProficiencySlotCost(selection.ProficiencyType) + (selection.Specialized ? 1 : 0);

            if (selection.WeaponOfChoice)
            {
                int choiceCost = GetWeaponProficiencyCpCost(classIds, "weapon_of_choice", "combat_option", specialized: false);
                if (choiceCost > 0)
                    cost += choiceCost;
            }
            if (selection.WeaponExpertise)
            {
                int expertiseCost = GetWeaponProficiencyCpCost(classIds, "weapon_expertise", "combat_option", specialized: false);
                if (expertiseCost > 0)
                    cost += expertiseCost;
            }

            total += cost;
        }
        return total;
    }

    private int? GetWeaponProficiencyCpCostForClass(string classId, string proficiencyId, string profType, bool specialized, bool isMultiClass, int characterLevel = -1)
    {
        int baseCost;
        switch ((profType ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "combat_option":
                // Generic combat-training picks (weapon of choice, expertise, mastery,
                // armor/shield proficiencies, and fighting styles) share a flat cost.
                baseCost = 2;
                break;

            case "broad_group":
                if (!IsWarriorWeaponClass(classId))
                    return null;
                baseCost = 6;
                break;

            case "tight_group":
                if (!IsWarriorWeaponClass(classId))
                    return null;
                baseCost = 4;
                break;

            default:
                baseCost = GetIndividualWeaponCpCost(classId, proficiencyId);
                break;
        }

        if (!specialized)
            return baseCost;

        if (string.Equals(profType, "combat_option", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!string.Equals(profType, "individual", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!CanSpecializeInWeapons(classId, playersOption: true))
            return null;

        // Enforce minimum level from Table 53 when caller supplies a level.
        if (characterLevel >= 0)
        {
            int minLevel = GetWeaponSpecializationMinLevel(classId, isMultiClass);
            if (characterLevel < minLevel)
                return null;
        }

        return baseCost + GetWeaponSpecializationCpCost(classId, isMultiClass);
    }

    private int GetIndividualWeaponCpCost(string classId, string weaponId)
    {
        // Bard can use any weapon natively — no crossover surcharge, flat rogue-tier base cost.
        if (AnyWeaponClasses.Contains(classId))
            return 3;

        var family = GetWeaponCostFamily(classId);
        var tier = GetWeaponAccessTier(weaponId);

        return family switch
        {
            WeaponCostFamily.Warrior => 2,
            WeaponCostFamily.Rogue => tier == WeaponAccessTier.Warrior ? 4 : 3,
            WeaponCostFamily.Priest => tier == WeaponAccessTier.Warrior ? 4 : 3,
            WeaponCostFamily.Wizard => tier switch
            {
                WeaponAccessTier.Wizard => 3,
                WeaponAccessTier.RoguePriest => 5,
                _ => 6,
            },
            _ => 3,
        };
    }

    /// <summary>
    /// Returns the specialization CP cost for the given class (Table 53).
    /// </summary>
    private static int GetWeaponSpecializationCpCost(string classId, bool isMultiClass)
    {
        if (string.Equals(classId, "fighter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "warrior", StringComparison.OrdinalIgnoreCase))
            return isMultiClass ? 4 : 2;
        if (string.Equals(classId, "paladin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "ranger", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (PriestWeaponClasses.Contains(classId))
            return 6;
        if (RogueWeaponClasses.Contains(classId))
            return 8;
        if (WizardWeaponClasses.Contains(classId))
            return 10;
        return 2;
    }

    /// <summary>
    /// Returns the minimum level required to specialize per Table 53.
    /// Multi-class fighter minimum is 2; returned when isMultiClass is true.
    /// </summary>
    private static int GetWeaponSpecializationMinLevel(string classId, bool isMultiClass)
    {
        if (string.Equals(classId, "fighter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "warrior", StringComparison.OrdinalIgnoreCase))
            return isMultiClass ? 2 : 1;
        if (string.Equals(classId, "paladin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "ranger", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (PriestWeaponClasses.Contains(classId))
            return 5;
        if (RogueWeaponClasses.Contains(classId))
            return 6;
        if (WizardWeaponClasses.Contains(classId))
            return 7;
        return 1;
    }

    /// <summary>
    /// Returns true when at least one effective class meets the Table 53 specialization
    /// minimum level for the given character level.
    /// </summary>
    public bool CanSpecializeAtLevel(IEnumerable<string> classIds, int level)
    {
        bool isMultiClass = (classIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > 1;

        foreach (var classId in classIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(classId)) continue;
            if (!CanSpecializeInWeapons(classId, playersOption: true)) continue;
            if (level >= GetWeaponSpecializationMinLevel(classId, isMultiClass))
                return true;
        }
        return false;
    }

    private static WeaponCostFamily GetWeaponCostFamily(string classId)
    {
        if (IsWarriorWeaponClass(classId))
            return WeaponCostFamily.Warrior;
        if (PriestWeaponClasses.Contains(classId))
            return WeaponCostFamily.Priest;
        if (WizardWeaponClasses.Contains(classId))
            return WeaponCostFamily.Wizard;
        return WeaponCostFamily.Rogue;
    }

    private static bool IsWarriorWeaponClass(string classId)
        => WarriorWeaponClasses.Contains((classId ?? string.Empty).Trim());

    private WeaponAccessTier GetWeaponAccessTier(string weaponId)
    {
        if (WizardTierWeaponIds.Contains(weaponId))
            return WeaponAccessTier.Wizard;

        if (RoguePriestTierWeaponIds.Contains(weaponId)
            || (WeaponById.TryGetValue(weaponId, out var weapon)
                && RoguePriestTierGroupIds.Contains(weapon.GroupId)))
            return WeaponAccessTier.RoguePriest;

        return WeaponAccessTier.Warrior;
    }

    /// <summary>
    /// Returns the slot cost for a given proficiency type.
    /// individual=1, tight_group=1, broad_group=2
    /// </summary>
    public static int GetWeaponProficiencySlotCost(string profType)
        => string.Equals(profType, "broad_group", StringComparison.OrdinalIgnoreCase) ? 2 : 1;

    /// <summary>
    /// Calculates the total slots used by a list of weapon proficiency selections.
    /// Each specialization costs 1 additional slot.
    /// </summary>
    public static int GetTotalWeaponProficiencySlotsUsed(IEnumerable<WeaponProficiencySelection> selections)
    {
        int total = 0;
        foreach (var sel in selections)
        {
            total += GetWeaponProficiencySlotCost(sel.ProficiencyType);
            if (sel.Specialized) total += 1;
        }
        return total;
    }

    /// <summary>
    /// Returns the best WP slot budget for a multi-class character
    /// (uses the most generous class's slot table).
    /// </summary>
    public int GetWeaponProficiencySlotsForClasses(IEnumerable<string> classIds, int level)
    {
        int best = 0;
        foreach (var id in classIds)
            best = Math.Max(best, GetWeaponProficiencySlots(id, level));
        return best == 0 ? 2 : best;
    }

    private static Dictionary<string, int> ParseIntDict(JsonElement el, string prop)
    {
        var result = new Dictionary<string, int>();
        if (el.TryGetProperty(prop, out var dictEl))
            foreach (var p in dictEl.EnumerateObject())
                result[p.Name] = p.Value.GetInt32();
        return result;
    }

    private static List<string> ParseStringList(JsonElement el, string prop)
    {
        var result = new List<string>();
        if (el.TryGetProperty(prop, out var arrEl))
            foreach (var item in arrEl.EnumerateArray())
            {
                // Accept both plain strings and structured objects (read description)
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (s is not null) result.Add(s);
                }
                else if (item.ValueKind == JsonValueKind.Object &&
                         item.TryGetProperty("description", out var dEl))
                {
                    var s = dEl.GetString();
                    if (s is not null) result.Add(s);
                }
            }
        return result;
    }

    /// <summary>
    /// Parses an ability array that may contain either plain strings (legacy)
    /// or structured objects with id / description / mechanics fields.
    /// </summary>
    private static List<AbilityDefinition> ParseStructuredAbilities(JsonElement el, string prop)
    {
        var result = new List<AbilityDefinition>();
        if (!el.TryGetProperty(prop, out var arrEl)) return result;

        foreach (var item in arrEl.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                // Legacy plain string – no mechanical effect
                var s = item.GetString() ?? "";
                result.Add(new AbilityDefinition
                {
                    Id = Slugify(s),
                    Description = s,
                    PointCost = 0,
                    AutoGranted = true,
                });
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var id   = item.TryGetProperty("id",          out var idEl)  ? idEl.GetString()   ?? "" : "";
                var desc = item.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "";
                var effect = new AbilityEffect();

                if (item.TryGetProperty("mechanics", out var mech))
                {
                    effect.AcBonus            = GetInt(mech, "ac_bonus");
                    effect.AcBonusRequiresNoArmor = GetBool(mech, "ac_bonus_requires_no_armor") || GetBool(mech, "requires_no_armor");
                    effect.AttackBonus        = GetInt(mech, "attack_bonus");
                    effect.DamageBonus        = GetInt(mech, "damage_bonus");
                    effect.MovementBonus      = GetInt(mech, "movement_bonus");
                    effect.XpModifierPercent  = GetInt(mech, "xp_modifier_percent");
                    effect.HpPerLevel         = GetInt(mech, "hp_per_level");
                    effect.HpFlatBonus        = GetInt(mech, "hp_flat_bonus");
                    effect.HpDiceExpression   = GetString(mech, "hp_dice_expression");
                    effect.NwpSlotBonus       = GetInt(mech, "nwp_slot_bonus");
                    effect.NwpCostReduction   = GetInt(mech, "nwp_cost_reduction");
                    effect.NwpCheckBonus      = GetInt(mech, "nwp_check_bonus");
                    effect.SurpriseBonus      = GetInt(mech, "surprise_bonus");
                    effect.InfravisionFeet    = GetInt(mech, "infravision_feet");
                    effect.MagicResistPercent = GetInt(mech, "magic_resist_percent");
                    effect.ReactionBonus      = GetInt(mech, "reaction_bonus");
                    effect.GrantsStealth      = GetBool(mech, "grants_stealth");
                    effect.DetectSecretDoors  = GetBool(mech, "detect_secret_doors");
                    effect.DetectStonework    = GetBool(mech, "detect_stonework");
                    effect.SaveBonuses        = ParseIntDict(mech, "save_bonuses");
                    effect.SubAbilityBonuses  = ParseIntDict(mech, "subability_bonuses");
                    effect.EnemyAttackBonuses = ParseIntDict(mech, "enemy_attack_bonuses");
                    effect.EnemyDamageBonuses = ParseIntDict(mech, "enemy_damage_bonuses");
                    effect.WeaponAttackBonuses = ParseIntDict(mech, "weapon_attack_bonuses");
                    effect.WeaponDamageBonuses = ParseIntDict(mech, "weapon_damage_bonuses");
                }

                var explicitCost = item.TryGetProperty("point_cost", out var costEl)
                    && costEl.ValueKind == JsonValueKind.Number
                    ? costEl.GetInt32()
                    : int.MinValue;

                var explicitAuto = item.TryGetProperty("auto_granted", out var autoEl)
                    && (autoEl.ValueKind == JsonValueKind.True || autoEl.ValueKind == JsonValueKind.False)
                    ? autoEl.GetBoolean()
                    : InferAutoGranted(desc);

                var resolvedCost = explicitCost != int.MinValue ? explicitCost : EstimatePointCost(effect, desc);

                var parsedAbility = new AbilityDefinition
                {
                    Id = id,
                    Description = desc,
                    PointCost = resolvedCost,
                    AutoGranted = explicitAuto,
                    Effect = effect,
                };

                foreach (var variant in ExpandMultiCostAbility(parsedAbility))
                    result.Add(variant);
            }
        }
        return result;
    }

    private static IEnumerable<AbilityDefinition> ExpandMultiCostAbility(AbilityDefinition ability)
    {
        if (ability.AutoGranted || string.IsNullOrWhiteSpace(ability.Description))
            return new[] { ability };

        var match = Regex.Match(ability.Description, @"\((?<costs>\d+(?:\s*/\s*\d+)+)\+?\)");
        if (!match.Success)
            return new[] { ability };

        var costs = match.Groups["costs"].Value
            .Split('/')
            .Select(s => int.TryParse(s.Trim(), out var v) ? v : int.MinValue)
            .Where(v => v != int.MinValue)
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        if (costs.Count < 2)
            return new[] { ability };

        var variants = new List<AbilityDefinition>();
        for (int i = 0; i < costs.Count; i++)
        {
            int cost = costs[i];
            variants.Add(new AbilityDefinition
            {
                Id = string.IsNullOrWhiteSpace(ability.Id)
                    ? $"{Slugify(ability.Description)}_cp{cost}"
                    : $"{ability.Id}_cp{cost}",
                Description = RewriteCostInDescription(ability.Description, cost),
                PointCost = cost,
                AutoGranted = false,
                Category = ability.Category,
                Effect = CloneEffect(ability.Effect),
            });
        }

        return variants;
    }

    private static string RewriteCostInDescription(string description, int cost)
    {
        return new Regex(@"\((?<costs>\d+(?:\s*/\s*\d+)+)\+?\)")
            .Replace(description, $"({cost})", 1);
    }

    private static AbilityEffect CloneEffect(AbilityEffect src)
    {
        return new AbilityEffect
        {
            AcBonus = src.AcBonus,
            AcBonusRequiresNoArmor = src.AcBonusRequiresNoArmor,
            AttackBonus = src.AttackBonus,
            DamageBonus = src.DamageBonus,
            MovementBonus = src.MovementBonus,
            SaveBonuses = new Dictionary<string, int>(src.SaveBonuses),
            XpModifierPercent = src.XpModifierPercent,
            HpDiceExpression = src.HpDiceExpression,
            HpPerLevel = src.HpPerLevel,
            HpFlatBonus = src.HpFlatBonus,
            NwpSlotBonus = src.NwpSlotBonus,
            NwpCostReduction = src.NwpCostReduction,
            NwpCheckBonus = src.NwpCheckBonus,
            SurpriseBonus = src.SurpriseBonus,
            SubAbilityBonuses = new Dictionary<string, int>(src.SubAbilityBonuses),
            InfravisionFeet = src.InfravisionFeet,
            MagicResistPercent = src.MagicResistPercent,
            EnemyAttackBonuses = new Dictionary<string, int>(src.EnemyAttackBonuses),
            EnemyDamageBonuses = new Dictionary<string, int>(src.EnemyDamageBonuses),
            WeaponAttackBonuses = new Dictionary<string, int>(src.WeaponAttackBonuses),
            WeaponDamageBonuses = new Dictionary<string, int>(src.WeaponDamageBonuses),
            ReactionBonus = src.ReactionBonus,
            GrantsStealth = src.GrantsStealth,
            DetectSecretDoors = src.DetectSecretDoors,
            DetectStonework = src.DetectStonework,
        };
    }

    private static bool InferAutoGranted(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return true;
        var d = description.ToLowerInvariant();
        return !(d.Contains("may buy") || d.Contains("can buy") || d.Contains("optional"));
    }

    private static int EstimatePointCost(AbilityEffect effect, string description)
    {
        int cost = 0;

        cost += Math.Abs(effect.AcBonus) * 2;
        cost += Math.Abs(effect.AttackBonus) * 4;
        cost += Math.Abs(effect.DamageBonus) * 4;
        cost += effect.SaveBonuses.Values.Sum(v => Math.Abs(v)) / 2;
        cost += Math.Abs(effect.XpModifierPercent) / 5;
        cost += Math.Abs(effect.HpPerLevel) * 8;
        cost += Math.Abs(effect.HpFlatBonus) * 2;
        cost += Math.Abs(effect.NwpSlotBonus) * 3;
        cost += Math.Abs(effect.NwpCostReduction) * 2;
        cost += Math.Abs(effect.NwpCheckBonus) * 2;
        cost += Math.Abs(effect.SurpriseBonus) * 2;
        cost += effect.SubAbilityBonuses.Values.Sum(v => Math.Abs(v)) * 4;
        cost += effect.InfravisionFeet / 30;
        cost += Math.Abs(effect.MagicResistPercent) / 10;
        cost += Math.Abs(effect.ReactionBonus);
        cost += effect.EnemyAttackBonuses.Values.Sum(v => Math.Abs(v));
        cost += effect.EnemyDamageBonuses.Values.Sum(v => Math.Abs(v));
        cost += effect.WeaponAttackBonuses.Values.Sum(v => Math.Abs(v)) * 2;
        cost += effect.WeaponDamageBonuses.Values.Sum(v => Math.Abs(v)) * 2;
        if (effect.GrantsStealth) cost += 4;
        if (effect.DetectSecretDoors) cost += 3;
        if (effect.DetectStonework) cost += 2;

        var hasAnyMechanicalValue = cost > 0;
        if (!hasAnyMechanicalValue && !string.IsNullOrWhiteSpace(description))
        {
            var d = description.ToLowerInvariant();
            if (d.Contains("infravision") || d.Contains("save") || d.Contains("attack") || d.Contains("armor class"))
                cost = 2;
        }

        return Math.Max(0, cost);
    }

    private static int InferRaceBudget(string mode, string raceId, List<AbilityDefinition> abilities)
    {
        if (mode != "players_option") return 0;

        var autoCost = abilities.Where(a => a.AutoGranted).Sum(a => Math.Max(0, a.PointCost));
        var id = raceId.ToLowerInvariant();

        var floor = id switch
        {
            var s when s.Contains("human") => 10,
            var s when s.Contains("dwarf") => 45,
            var s when s.Contains("elf") => 45,
            var s when s.Contains("gnome") => 45,
            var s when s.Contains("halfling") => 35,
            var s when s.Contains("half_elf") || s.Contains("half-elf") => 25,
            var s when s.Contains("half_orc") || s.Contains("half-orc") => 15,
            var s when s.Contains("half_ogre") || s.Contains("half-ogre") => 15,
            _ => 20,
        };

        return Math.Max(floor, autoCost);
    }

    private static int  GetInt (JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    private static bool GetBool(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    private static string? GetString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Slugify(string s) =>
        new string(s.ToLower().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');

    /// <summary>
    /// Aggregates all AbilityEffect records into a single AbilityBonuses object.
    /// </summary>
    public static AbilityBonuses AggregateEffects(IEnumerable<AbilityDefinition> abilities, bool isUnarmored = true)
    {
        var b = new AbilityBonuses();
        foreach (var ab in abilities)
        {
            var e = ab.Effect;
            if (!e.AcBonusRequiresNoArmor || isUnarmored)
                b.AcBonus += e.AcBonus;
            b.AttackBonus        += e.AttackBonus;
            b.DamageBonus        += e.DamageBonus;
            b.MovementBonus      += e.MovementBonus;
            b.XpModifierPercent  += e.XpModifierPercent;
            b.HpPerLevel         += e.HpPerLevel;
            b.HpFlatBonus        += e.HpFlatBonus;
            b.NwpSlotBonus       += e.NwpSlotBonus;
            b.NwpCostReduction   += e.NwpCostReduction;
            b.NwpCheckBonus      += e.NwpCheckBonus;
            b.SurpriseBonus      += e.SurpriseBonus;
            b.InfravisionFeet     = Math.Max(b.InfravisionFeet, e.InfravisionFeet); // use longest
            b.MagicResistPercent += e.MagicResistPercent;
            b.ReactionBonus      += e.ReactionBonus;
            b.GrantsStealth      |= e.GrantsStealth;
            b.DetectSecretDoors  |= e.DetectSecretDoors;
            b.DetectStonework    |= e.DetectStonework;

            foreach (var kv in e.SaveBonuses)
                b.SaveBonuses[kv.Key] = b.SaveBonuses.GetValueOrDefault(kv.Key) + kv.Value;
            foreach (var kv in e.SubAbilityBonuses)
                b.SubAbilityBonuses[kv.Key] = b.SubAbilityBonuses.GetValueOrDefault(kv.Key) + kv.Value;
            foreach (var kv in e.EnemyAttackBonuses)
                b.EnemyAttackBonuses[kv.Key] = b.EnemyAttackBonuses.GetValueOrDefault(kv.Key) + kv.Value;
            foreach (var kv in e.EnemyDamageBonuses)
                b.EnemyDamageBonuses[kv.Key] = b.EnemyDamageBonuses.GetValueOrDefault(kv.Key) + kv.Value;
            foreach (var kv in e.WeaponAttackBonuses)
                b.WeaponAttackBonuses[kv.Key] = b.WeaponAttackBonuses.GetValueOrDefault(kv.Key) + kv.Value;
            foreach (var kv in e.WeaponDamageBonuses)
                b.WeaponDamageBonuses[kv.Key] = b.WeaponDamageBonuses.GetValueOrDefault(kv.Key) + kv.Value;
        }
        return b;
    }

    private static string NormalizeCharacterMode(string? mode) => mode switch
    {
        "core_rules" => "core_rules",
        "players_option" => "players_option",
        _ => "all",
    };

    public IEnumerable<RaceDefinition> RacesForMode(string characterMode)
    {
        var mode = NormalizeCharacterMode(characterMode);
        var filtered = Races.Values
            .Where(r => r.CharacterMode == "all" || r.CharacterMode == mode)
            .GroupBy(r => r.BaseRaceId)
            .Select(group => group
                .OrderByDescending(r => r.CharacterMode == mode)
                .ThenByDescending(r => r.CharacterMode == "all")
                .ThenBy(r => r.Name.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(r => r.Name)
                .First())
            .OrderBy(r => r.Name)
            .ToList();

        return filtered.Count > 0 ? filtered : Races.Values.OrderBy(r => r.Name);
    }

    public IEnumerable<RaceDefinition> SubracesForBase(string characterMode, string baseRaceId)
    {
        var mode = NormalizeCharacterMode(characterMode);
        var baseId = (baseRaceId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baseId)) return Enumerable.Empty<RaceDefinition>();

        var subraces = Races.Values
            .Where(r => string.Equals(r.BaseRaceId, baseId, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.CharacterMode == "all" || r.CharacterMode == mode)
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(r => r.CharacterMode == mode)
                .ThenByDescending(r => r.CharacterMode == "all")
                .ThenBy(r => r.Name.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(r => r.Name)
                .First())
            .OrderBy(r => r.Name.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(r => r.Name)
            .ToList();

        // Hide redundant core subraces whose name duplicates the base race and only contain 0-CP legacy entries.
        subraces = subraces
            .Where(r => !IsRedundantZeroCostBaseSubrace(r, baseId))
            .ToList();

        // In PO mode, don't show the core all-mode base race entry as a subrace choice.
        // This is the "same as base race" no-CP entry users reported (e.g., Halfling under Halfling).
        if (string.Equals(mode, "players_option", StringComparison.OrdinalIgnoreCase))
        {
            subraces = subraces
                .Where(r => !(string.Equals(r.CharacterMode, "all", StringComparison.OrdinalIgnoreCase)
                              && IsBaseNameMatch(r.Name, baseId)))
                .ToList();
        }

        // Player's Option request: default halfling should expose all halfling racial abilities as choices.
        if (string.Equals(mode, "players_option", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(baseId, "halfling", StringComparison.OrdinalIgnoreCase))
        {
            subraces = MergeHalflingOptionsIntoBasic(subraces);
        }

        if (subraces.Count > 0) return subraces;

        // Fallback if only a single race entry exists without explicit base linkage.
        return Races.Values
            .Where(r => string.Equals(r.Id, baseId, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.CharacterMode == "all" || r.CharacterMode == mode)
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(r => r.CharacterMode == mode)
                .ThenByDescending(r => r.CharacterMode == "all")
                .ThenBy(r => r.Name)
                .First())
            .OrderBy(r => r.Name)
            .ToList();
    }

    private static bool IsRedundantZeroCostBaseSubrace(RaceDefinition race, string baseRaceId)
    {
        if (race.StructuredAbilities.Count == 0) return false;
        if (race.StructuredAbilities.Any(a => a.PointCost != 0)) return false;

        return IsBaseNameMatch(race.Name, baseRaceId);
    }

    private static bool IsBaseNameMatch(string? raceName, string? baseRaceId)
    {
        string Normalize(string value) =>
            new string((value ?? string.Empty)
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());

        return Normalize(raceName ?? string.Empty) == Normalize(baseRaceId ?? string.Empty);
    }

    private static List<RaceDefinition> MergeHalflingOptionsIntoBasic(List<RaceDefinition> subraces)
    {
        var basicIndex = subraces.FindIndex(r => r.Name.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase));
        if (basicIndex < 0) return subraces;

        var basic = subraces[basicIndex];
        var mergedAbilities = basic.StructuredAbilities
            .Select(CloneAbility)
            .ToList();

        var knownIds = new HashSet<string>(mergedAbilities.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var sibling in subraces.Where((_, i) => i != basicIndex))
        {
            foreach (var ability in sibling.StructuredAbilities)
            {
                if (string.IsNullOrWhiteSpace(ability.Id) || knownIds.Contains(ability.Id))
                    continue;

                var cloned = CloneAbility(ability);
                cloned.AutoGranted = false;
                mergedAbilities.Add(cloned);
                knownIds.Add(cloned.Id);
            }
        }

        subraces[basicIndex] = basic with { StructuredAbilities = mergedAbilities };
        return subraces;
    }

    private static AbilityDefinition CloneAbility(AbilityDefinition a)
    {
        return new AbilityDefinition
        {
            Id = a.Id,
            Description = a.Description,
            PointCost = a.PointCost,
            AutoGranted = a.AutoGranted,
            Effect = new AbilityEffect
            {
                AcBonus = a.Effect.AcBonus,
                AcBonusRequiresNoArmor = a.Effect.AcBonusRequiresNoArmor,
                AttackBonus = a.Effect.AttackBonus,
                DamageBonus = a.Effect.DamageBonus,
                MovementBonus = a.Effect.MovementBonus,
                SaveBonuses = new Dictionary<string, int>(a.Effect.SaveBonuses),
                XpModifierPercent = a.Effect.XpModifierPercent,
                HpDiceExpression = a.Effect.HpDiceExpression,
                HpPerLevel = a.Effect.HpPerLevel,
                HpFlatBonus = a.Effect.HpFlatBonus,
                NwpSlotBonus = a.Effect.NwpSlotBonus,
                NwpCostReduction = a.Effect.NwpCostReduction,
                NwpCheckBonus = a.Effect.NwpCheckBonus,
                SurpriseBonus = a.Effect.SurpriseBonus,
                SubAbilityBonuses = new Dictionary<string, int>(a.Effect.SubAbilityBonuses),
                InfravisionFeet = a.Effect.InfravisionFeet,
                MagicResistPercent = a.Effect.MagicResistPercent,
                EnemyAttackBonuses = new Dictionary<string, int>(a.Effect.EnemyAttackBonuses),
                EnemyDamageBonuses = new Dictionary<string, int>(a.Effect.EnemyDamageBonuses),
                WeaponAttackBonuses = new Dictionary<string, int>(a.Effect.WeaponAttackBonuses),
                WeaponDamageBonuses = new Dictionary<string, int>(a.Effect.WeaponDamageBonuses),
                ReactionBonus = a.Effect.ReactionBonus,
                GrantsStealth = a.Effect.GrantsStealth,
                DetectSecretDoors = a.Effect.DetectSecretDoors,
                DetectStonework = a.Effect.DetectStonework,
            }
        };
    }

    private void PostProcessRules()
    {
        NormalizeClassVisibility();
        ExpandClassAbilityCatalogs();
    }

    private void NormalizeClassVisibility()
    {
        // User-requested behavior: always show these classes in the chooser regardless of race.
        if (Classes.TryGetValue("bard", out var bard))
            Classes["bard"] = bard with { AllowedRaces = new List<string>() };

        if (Classes.TryGetValue("druid", out var druid))
            Classes["druid"] = druid with { AllowedRaces = new List<string>() };
    }

    private void ExpandClassAbilityCatalogs()
    {
        AddWizardSchoolOptions();
        AddClericSphereOptions();

        AddAbilityIfMissing("fighter", new AbilityDefinition
        {
            Id = "fighter_shield_mastery",
            Description = "Shield mastery (8): Gain +1 AC while using any shield.",
            PointCost = 8,
            AutoGranted = false,
            Effect = new AbilityEffect { AcBonus = -1 }
        });
        AddAbilityIfMissing("fighter", new AbilityDefinition
        {
            Id = "fighter_battle_hardened",
            Description = "Battle hardened (10): +1 bonus on all saving throws.",
            PointCost = 10,
            AutoGranted = false,
            Effect = new AbilityEffect { SaveBonuses = new Dictionary<string, int> { ["all"] = 1 } }
        });

        AddAbilityIfMissing("wizard", new AbilityDefinition
        {
            Id = "wizard_spell_focus",
            Description = "Spell focus (8): Chosen spell school receives +1 effective caster level for duration and range.",
            PointCost = 8,
            AutoGranted = false,
        });
        AddAbilityIfMissing("wizard", new AbilityDefinition
        {
            Id = "wizard_arcane_ward",
            Description = "Arcane ward (10): +1 to saving throws versus hostile spells.",
            PointCost = 10,
            AutoGranted = false,
            Effect = new AbilityEffect { SaveBonuses = new Dictionary<string, int> { ["magic"] = 1 } }
        });

        AddAbilityIfMissing("cleric", new AbilityDefinition
        {
            Id = "cleric_turning_mastery",
            Description = "Turning mastery (10): Turn undead as if one level higher.",
            PointCost = 10,
            AutoGranted = false,
        });
        AddAbilityIfMissing("cleric", new AbilityDefinition
        {
            Id = "cleric_divine_resilience",
            Description = "Divine resilience (8): +1 to saving throws versus magical effects.",
            PointCost = 8,
            AutoGranted = false,
            Effect = new AbilityEffect { SaveBonuses = new Dictionary<string, int> { ["magic"] = 1 } }
        });

        AddAbilityIfMissing("thief", new AbilityDefinition
        {
            Id = "thief_escape_artist",
            Description = "Escape artist (8): Gain superior movement/escape talent and +1 surprise bonus.",
            PointCost = 8,
            AutoGranted = false,
            Effect = new AbilityEffect { SurpriseBonus = 1, GrantsStealth = true }
        });
        AddAbilityIfMissing("thief", new AbilityDefinition
        {
            Id = "thief_backstab_training",
            Description = "Backstab training (10): +1 damage on successful backstab attacks.",
            PointCost = 10,
            AutoGranted = false,
            Effect = new AbilityEffect { DamageBonus = 1 }
        });

        AddAbilityIfMissing("ranger", new AbilityDefinition
        {
            Id = "ranger_trailblazer",
            Description = "Trailblazer (8): +1 surprise and superior wilderness pursuit/navigation.",
            PointCost = 8,
            AutoGranted = false,
            Effect = new AbilityEffect { SurpriseBonus = 1 }
        });
        AddAbilityIfMissing("ranger", new AbilityDefinition
        {
            Id = "ranger_beast_lore",
            Description = "Beast lore (6): +1 reaction bonus with natural beasts and animal companions.",
            PointCost = 6,
            AutoGranted = false,
            Effect = new AbilityEffect { ReactionBonus = 1 }
        });

        AddAbilityIfMissing("paladin", new AbilityDefinition
        {
            Id = "paladin_divine_aura",
            Description = "Divine aura (8): +1 reaction bonus with lawful good creatures and common folk.",
            PointCost = 8,
            AutoGranted = false,
            Effect = new AbilityEffect { ReactionBonus = 1 }
        });
        AddAbilityIfMissing("paladin", new AbilityDefinition
        {
            Id = "paladin_sacred_defense",
            Description = "Sacred defense (10): +1 to all saving throws while acting in good faith.",
            PointCost = 10,
            AutoGranted = false,
            Effect = new AbilityEffect { SaveBonuses = new Dictionary<string, int> { ["all"] = 1 } }
        });

        AddAbilityIfMissing("bard", new AbilityDefinition
        {
            Id = "bard_inspiring_performance",
            Description = "Inspiring performance (8): Allies gain +1 reaction bonus after one round of performance.",
            PointCost = 8,
            AutoGranted = false,
            Effect = new AbilityEffect { ReactionBonus = 1 }
        });
        AddAbilityIfMissing("bard", new AbilityDefinition
        {
            Id = "bard_lorekeeper",
            Description = "Lorekeeper (6): +1 to checks related to history, legends, and identification lore.",
            PointCost = 6,
            AutoGranted = false,
            Effect = new AbilityEffect { NwpCheckBonus = 1 }
        });

        AddAbilityIfMissing("druid", new AbilityDefinition
        {
            Id = "druid_woodland_stride",
            Description = "Woodland stride (6): Move through natural undergrowth unhindered and gain +1 surprise bonus.",
            PointCost = 6,
            AutoGranted = false,
            Effect = new AbilityEffect { SurpriseBonus = 1 }
        });
        AddAbilityIfMissing("druid", new AbilityDefinition
        {
            Id = "druid_nature_ward",
            Description = "Nature ward (8): +1 to saves versus poison and disease.",
            PointCost = 8,
            AutoGranted = false,
            Effect = new AbilityEffect { SaveBonuses = new Dictionary<string, int> { ["poison"] = 1 } }
        });

        AddAbilityIfMissing("psionicist", new AbilityDefinition
        {
            Id = "psionicist_mental_bastion",
            Description = "Mental bastion (10): +1 bonus to saving throws versus mind-affecting magic and psionics.",
            PointCost = 10,
            AutoGranted = false,
            Effect = new AbilityEffect { SaveBonuses = new Dictionary<string, int> { ["magic"] = 1 } }
        });
        AddAbilityIfMissing("psionicist", new AbilityDefinition
        {
            Id = "psionicist_celerity",
            Description = "Psionic celerity (8): +1 initiative-like surprise response through precognitive flashes.",
            PointCost = 8,
            AutoGranted = false,
            Effect = new AbilityEffect { SurpriseBonus = 1 }
        });
    }

    private void AddWizardSchoolOptions()
    {
        // Spells & Magic style school-by-school purchasing.
        // Traditional schools of magic (PH/PHB p.26, S&M p.12)
        var arcaneSchools = new (string id, string name)[]
        {
            ("abjuration",           "Abjuration"),
            ("alteration",           "Alteration"),
            ("conjuration_summoning","Conjuration/Summoning"),
            ("divination",           "Divination"),
            ("enchantment_charm",    "Enchantment/Charm"),
            ("illusion",             "Illusion"),
            ("invocation_evocation", "Invocation/Evocation"),
            ("necromancy",           "Necromancy"),
        };

        foreach (var s in arcaneSchools)
        {
            AddAbilityIfMissing("wizard", new AbilityDefinition
            {
                Id          = $"wizard_school_{s.id}",
                Category    = "Arcane School",
                Description = $"[Arcane School] {s.name} access (5 CP): Gain full spell access to the {s.name} school.",
                PointCost   = 5,
                AutoGranted = false,
            });
        }

        // Thaumaturgy / alternative schools (S&M p.60+)
        var thaumSchools = new (string id, string name)[]
        {
            ("alchemy",     "Alchemy"),
            ("artifice",    "Artifice"),
            ("dimensional", "Dimensional"),
            ("force",       "Force"),
            ("geometry",    "Geometry"),
            ("shadow",      "Shadow"),
            ("song",        "Song"),
            ("wild",        "Wild Magic"),
            ("elemental_air",   "Elemental (Air)"),
            ("elemental_earth", "Elemental (Earth)"),
            ("elemental_fire",  "Elemental (Fire)"),
            ("elemental_water", "Elemental (Water)"),
        };

        foreach (var s in thaumSchools)
        {
            AddAbilityIfMissing("wizard", new AbilityDefinition
            {
                Id          = $"wizard_school_{s.id}",
                Category    = "Thaumaturgy School",
                Description = $"[Thaumaturgy School] {s.name} access (5 CP): Gain full spell access to the {s.name} school.",
                PointCost   = 5,
                AutoGranted = false,
            });
        }
    }

    private void AddClericSphereOptions()
    {
        // Spells & Magic style sphere-by-sphere purchasing.
        // Costs align to PO Skills & Powers Table 6: Access Costs.
        var sphereCosts = new Dictionary<string, (int minor, int major)>(StringComparer.OrdinalIgnoreCase)
        {
            ["All"] = (3, 5),
            ["Animal"] = (5, 10),
            ["Astral"] = (3, 5),
            ["Chaos"] = (5, 8),
            ["Charm"] = (5, 10),
            ["Combat"] = (5, 10),
            ["Creation"] = (5, 10),
            ["Divination"] = (5, 10),
            ["Elemental"] = (8, 20),
            ["Elemental (Air)"] = (2, 5),
            ["Elemental (Earth)"] = (3, 8),
            ["Elemental (Fire)"] = (3, 8),
            ["Elemental (Water)"] = (2, 5),
            ["Guardian"] = (3, 5),
            ["Healing"] = (5, 10),
            ["Law"] = (5, 8),
            ["Necromantic"] = (5, 10),
            ["Numbers"] = (5, 10),
            ["Plant"] = (5, 10),
            ["Protection"] = (5, 10),
            ["Summoning"] = (5, 10),
            ["Sun"] = (3, 5),
            ["Thought"] = (5, 10),
            ["Time"] = (5, 10),
            ["Travelers"] = (3, 5),
            ["War"] = (3, 5),
            ["Wards"] = (5, 10),
            ["Weather"] = (5, 10),
            // Compatibility spheres the user wants to keep available.
            ["Magic"] = (5, 10),
            ["Spells"] = (5, 10),
        };

        var spheres = new (string id, string name)[]
        {
            ("all",            "All"),
            ("animal",         "Animal"),
            ("astral",         "Astral"),
            ("chaos",          "Chaos"),
            ("charm",          "Charm"),
            ("combat",         "Combat"),
            ("creation",       "Creation"),
            ("divination",     "Divination"),
            ("elemental",      "Elemental"),
            ("elemental_air",  "Elemental (Air)"),
            ("elemental_earth","Elemental (Earth)"),
            ("elemental_fire", "Elemental (Fire)"),
            ("elemental_water","Elemental (Water)"),
            ("guardian",       "Guardian"),
            ("healing",        "Healing"),
            ("law",            "Law"),
            ("magic",          "Magic"),
            ("necromantic",    "Necromantic"),
            ("numbers",        "Numbers"),
            ("plant",          "Plant"),
            ("protection",     "Protection"),
            ("spells",         "Spells"),
            ("summoning",      "Summoning"),
            ("sun",            "Sun"),
            ("thought",        "Thought"),
            ("time",           "Time"),
            ("travelers",      "Travelers"),
            ("war",            "War"),
            ("wards",          "Wards"),
            ("weather",        "Weather"),
        };

        foreach (var s in spheres)
        {
            var (minorCost, majorCost) = sphereCosts.TryGetValue(s.name, out var costs) ? costs : (5, 10);

            AddAbilityIfMissing("cleric", new AbilityDefinition
            {
                Id          = $"cleric_sphere_minor_{s.id}",
                Category    = "Sphere - Minor",
                Description = $"[Sphere - Minor] {s.name} ({minorCost} CP): Gain minor access to the {s.name} sphere.",
                PointCost   = minorCost,
                AutoGranted = false,
            });

            AddAbilityIfMissing("cleric", new AbilityDefinition
            {
                Id          = $"cleric_sphere_major_{s.id}",
                Category    = "Sphere - Major",
                Description = $"[Sphere - Major] {s.name} ({majorCost} CP): Gain major access to the {s.name} sphere.",
                PointCost   = majorCost,
                AutoGranted = false,
            });
        }

        AddAbilityIfMissing("cleric", new AbilityDefinition
        {
            Id          = "cleric_sphere_focus_bonus",
            Category    = "Sphere - Bonus",
            Description = "[Sphere - Bonus] Sphere focus (5 CP): Choose one major sphere; cast spells from it at +1 effective caster level.",
            PointCost   = 5,
            AutoGranted = false,
        });
    }

    private void AddAbilityIfMissing(string classId, AbilityDefinition ability)
    {
        if (!Classes.TryGetValue(classId, out var cls)) return;
        if (cls.StructuredAbilities.Any(a => string.Equals(a.Id, ability.Id, StringComparison.OrdinalIgnoreCase)))
            return;

        var expanded = cls.StructuredAbilities.ToList();
        expanded.Add(ability);
        Classes[classId] = cls with { StructuredAbilities = expanded };
    }

    public void SaveRace(RaceDefinition race)
    {
        var legacyList = race.RacialAbilities
            .Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()).ToList();
        // Preserve structured abilities as-is; if none, build minimal flavor entries from legacy strings
        var structured = race.StructuredAbilities.Count > 0
            ? race.StructuredAbilities
            : legacyList.Select(s => new AbilityDefinition { Id = Slugify(s), Description = s }).ToList();

        var normalized = new RaceDefinition(
            race.Id,
            race.Name,
            NormalizeCharacterMode(race.CharacterMode),
            string.IsNullOrWhiteSpace(race.BaseRaceId) ? race.Id : race.BaseRaceId.Trim(),
            new Dictionary<string, int>(race.AbilityMinimums),
            new Dictionary<string, int>(race.AbilityMaximums),
            new Dictionary<string, int>(race.AbilityModifiers),
            legacyList,
            structured,
            race.RacialPointBudget);

        Races[normalized.Id] = normalized;
        PersistRace(normalized);
    }

    public void SaveClass(ClassDefinition cls)
    {
        var normalized = new ClassDefinition(
            cls.Id,
            cls.Name,
            new Dictionary<string, int>(cls.AbilityMinimums),
            new List<string>(cls.AllowedRaces),
            cls.StructuredAbilities.Count > 0
                ? cls.StructuredAbilities
                : new List<AbilityDefinition>(),
            cls.ClassPointBudget,
            cls.Specializations is null ? null : new List<WizardSpecialization>(cls.Specializations));

        Classes[normalized.Id] = normalized;
        PersistClass(normalized);
    }

    public void DeleteRace(string raceId)
    {
        if (!Races.Remove(raceId)) return;
        RemoveRaceFromFile(FindBaseRulesetPath(), raceId);
        RemoveRaceFromFile(FindPlayersOptionOverlayPath(), raceId);
    }

    public void DeleteClass(string classId)
    {
        if (!Classes.Remove(classId)) return;
        RemoveClassFromFile(FindBaseRulesetPath(), classId);
        RemoveClassFromFile(FindPlayersOptionOverlayPath(), classId);
    }

    private void PersistRace(RaceDefinition race)
    {
        var basePath = FindBaseRulesetPath();
        var overlayPath = FindPlayersOptionOverlayPath();
        if (basePath is null || overlayPath is null)
            throw new InvalidOperationException("Unable to locate the ruleset files.");

        RemoveRaceFromFile(basePath, race.Id);
        RemoveRaceFromFile(overlayPath, race.Id);

        var targetPath = race.CharacterMode == "players_option" ? overlayPath : basePath;
        var root = ReadOrCreateJsonObject(targetPath);
        var racesObject = root["races"]?.AsObject() ?? new JsonObject();
        racesObject[race.Id] = ToJsonRaceObject(race);
        root["races"] = racesObject;
        WriteJsonObject(targetPath, root);
    }

    private void PersistClass(ClassDefinition cls)
    {
        var basePath = FindBaseRulesetPath();
        var overlayPath = FindPlayersOptionOverlayPath();
        if (basePath is null || overlayPath is null)
            throw new InvalidOperationException("Unable to locate the ruleset files.");

        RemoveClassFromFile(basePath, cls.Id);
        RemoveClassFromFile(overlayPath, cls.Id);

        // Class data is currently persisted in base ruleset file by default.
        var root = ReadOrCreateJsonObject(basePath);
        var classesObject = root["classes"]?.AsObject() ?? new JsonObject();
        classesObject[cls.Id] = ToJsonClassObject(cls);
        root["classes"] = classesObject;
        WriteJsonObject(basePath, root);
    }

    private static JsonObject ToJsonObject(Dictionary<string, int> values)
    {
        var obj = new JsonObject();
        foreach (var pair in values.OrderBy(p => p.Key)) obj[pair.Key] = pair.Value;
        return obj;
    }

    private static JsonArray ToJsonAbilityArray(List<AbilityDefinition> abilities)
    {
        var arr = new JsonArray();
        foreach (var ab in abilities)
        {
            var e = ab.Effect;
            var mech = new JsonObject();
            if (e.AcBonus            != 0) mech["ac_bonus"]              = e.AcBonus;
            if (e.AttackBonus        != 0) mech["attack_bonus"]          = e.AttackBonus;
            if (e.DamageBonus        != 0) mech["damage_bonus"]          = e.DamageBonus;
            if (e.MovementBonus      != 0) mech["movement_bonus"]        = e.MovementBonus;
            if (e.XpModifierPercent  != 0) mech["xp_modifier_percent"]   = e.XpModifierPercent;
            if (e.HpPerLevel         != 0) mech["hp_per_level"]          = e.HpPerLevel;
            if (e.HpFlatBonus        != 0) mech["hp_flat_bonus"]         = e.HpFlatBonus;
            if (!string.IsNullOrWhiteSpace(e.HpDiceExpression)) mech["hp_dice_expression"] = e.HpDiceExpression;
            if (e.AcBonusRequiresNoArmor) mech["ac_bonus_requires_no_armor"] = true;
            if (e.NwpSlotBonus       != 0) mech["nwp_slot_bonus"]        = e.NwpSlotBonus;
            if (e.NwpCostReduction   != 0) mech["nwp_cost_reduction"]    = e.NwpCostReduction;
            if (e.NwpCheckBonus      != 0) mech["nwp_check_bonus"]       = e.NwpCheckBonus;
            if (e.SurpriseBonus      != 0) mech["surprise_bonus"]        = e.SurpriseBonus;
            if (e.InfravisionFeet    != 0) mech["infravision_feet"]      = e.InfravisionFeet;
            if (e.MagicResistPercent != 0) mech["magic_resist_percent"]  = e.MagicResistPercent;
            if (e.ReactionBonus      != 0) mech["reaction_bonus"]        = e.ReactionBonus;
            if (e.GrantsStealth)           mech["grants_stealth"]        = true;
            if (e.DetectSecretDoors)       mech["detect_secret_doors"]   = true;
            if (e.DetectStonework)         mech["detect_stonework"]      = true;
            if (e.SaveBonuses.Count         > 0) mech["save_bonuses"]          = ToJsonObject(e.SaveBonuses);
            if (e.SubAbilityBonuses.Count   > 0) mech["subability_bonuses"]    = ToJsonObject(e.SubAbilityBonuses);
            if (e.EnemyAttackBonuses.Count  > 0) mech["enemy_attack_bonuses"]  = ToJsonObject(e.EnemyAttackBonuses);
            if (e.EnemyDamageBonuses.Count  > 0) mech["enemy_damage_bonuses"]  = ToJsonObject(e.EnemyDamageBonuses);
            if (e.WeaponAttackBonuses.Count > 0) mech["weapon_attack_bonuses"] = ToJsonObject(e.WeaponAttackBonuses);
            if (e.WeaponDamageBonuses.Count > 0) mech["weapon_damage_bonuses"] = ToJsonObject(e.WeaponDamageBonuses);

            var obj = new JsonObject
            {
                ["id"]          = ab.Id,
                ["description"] = ab.Description,
                ["point_cost"]  = ab.PointCost,
                ["auto_granted"] = ab.AutoGranted,
            };
            if (mech.Count > 0) obj["mechanics"] = mech;
            arr.Add(obj);
        }
        return arr;
    }

    private static JsonObject ToJsonRaceObject(RaceDefinition race)
    {
        return new JsonObject
        {
            ["name"]             = race.Name,
            ["character_mode"]   = race.CharacterMode,
            ["base_race_id"]     = race.BaseRaceId,
            ["ability_minimums"] = ToJsonObject(race.AbilityMinimums),
            ["ability_maximums"] = ToJsonObject(race.AbilityMaximums),
            ["racial_point_budget"] = race.RacialPointBudget,
            ["racial_abilities"] = ToJsonAbilityArray(race.StructuredAbilities),
        };
    }

    private static JsonObject ToJsonClassObject(ClassDefinition cls)
    {
        var obj = new JsonObject
        {
            ["name"] = cls.Name,
            ["ability_minimums"] = ToJsonObject(cls.AbilityMinimums),
            ["allowed_races"] = new JsonArray(cls.AllowedRaces.Select(r => (JsonNode)r).ToArray()),
            ["class_point_budget"] = cls.ClassPointBudget,
            ["class_abilities"] = ToJsonAbilityArray(cls.StructuredAbilities),
        };

        if (cls.Specializations is { Count: > 0 })
        {
            var specs = new JsonArray();
            foreach (var s in cls.Specializations)
            {
                specs.Add(new JsonObject
                {
                    ["id"] = s.Id,
                    ["name"] = s.Name,
                    ["description"] = s.Description,
                    ["ability_minimums"] = ToJsonObject(s.AbilityMinimums),
                    ["allowed_races"] = new JsonArray(s.AllowedRaces.Select(r => (JsonNode)r).ToArray()),
                    ["class_point_budget"] = s.ClassPointBudget,
                    ["auto_select_ability_ids"] = new JsonArray(s.AutoSelectAbilityIds.Select(a => (JsonNode)a).ToArray()),
                    ["opposition_schools"] = new JsonArray(s.OppositionSchools.Select(a => (JsonNode)a).ToArray()),
                });
            }
            obj["specializations"] = specs;
        }

        return obj;
    }

    private static JsonObject ReadOrCreateJsonObject(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonNode.Parse(stream)?.AsObject() ?? new JsonObject();
    }

    private static void WriteJsonObject(string path, JsonObject root)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, root.ToJsonString(options));
    }

    private static void RemoveRaceFromFile(string? path, string raceId)
    {
        if (path is null) return;
        var root = ReadOrCreateJsonObject(path);
        if (root["races"] is not JsonObject racesObject || !racesObject.Remove(raceId)) return;
        root["races"] = racesObject;
        WriteJsonObject(path, root);
    }

    private static void RemoveClassFromFile(string? path, string classId)
    {
        if (path is null) return;
        var root = ReadOrCreateJsonObject(path);
        if (root["classes"] is not JsonObject classesObject || !classesObject.Remove(classId)) return;
        root["classes"] = classesObject;
        WriteJsonObject(path, root);
    }

    // ── Fallback hardcoded data ───────────────────────────────────────────────

    private void LoadFallback()
    {
        Races["human"]    = new("human", "Human", "all", "human", new(), new(), new(), new(), new());
        Races["elf"]      = new("elf", "Elf", "all", "elf",
            new() { {"dex",7} }, new() { {"con",17} },
            new() { {"dex",1}, {"con",-1} },
            new()
            {
                "90% resistance to sleep and charm magic.",
                "Infravision 60 feet.",
                "Detect secret and concealed doors with a careful search.",
            },
            new()
            {
                new() { Id="elf_charm_resist",   Description="90% resistance to sleep and charm magic.",
                    Effect=new(){ MagicResistPercent=90 } },
                new() { Id="elf_infravision",    Description="Infravision 60 feet.",
                    Effect=new(){ InfravisionFeet=60 } },
                new() { Id="elf_secret_doors",   Description="Detect secret and concealed doors with a careful search.",
                    Effect=new(){ DetectSecretDoors=true } },
            });
        Races["dwarf"]    = new("dwarf", "Dwarf", "all", "dwarf",
            new() { {"con",11} }, new() { {"cha",17} },
            new() { {"con",1}, {"cha",-1} },
            new()
            {
                "Infravision 60 feet.",
                "Notice unusual stonework and grade or depth changes.",
                "Bonuses against goblinoids, giants, poison, and magic.",
            },
            new()
            {
                new() { Id="dwarf_infravision",  Description="Infravision 60 feet.",
                    Effect=new(){ InfravisionFeet=60 } },
                new() { Id="dwarf_stonework",    Description="Notice unusual stonework and grade or depth changes.",
                    Effect=new(){ DetectStonework=true } },
                new() { Id="dwarf_goblinoid_bonus", Description="+1 attack bonus versus goblinoids.",
                    Effect=new(){ EnemyAttackBonuses=new(){{"goblinoids",1}} } },
                new() { Id="dwarf_giant_ac",     Description="-4 AC bonus versus giants.",
                    Effect=new(){ EnemyAttackBonuses=new(){{"giants",-4}} } },
                new() { Id="dwarf_poison_save",  Description="Saving throw bonus versus poison.",
                    Effect=new(){ SaveBonuses=new(){{"poison",4}} } },
                new() { Id="dwarf_magic_save",   Description="Saving throw bonus versus magic.",
                    Effect=new(){ SaveBonuses=new(){{"magic",4}} } },
            });
        Races["halfling"] = new("halfling", "Halfling", "all", "halfling",
            new() { {"dex",7},{"con",10} }, new(),
            new() { {"dex",1}, {"str",-1} },
            new()
            {
                "Bonuses with thrown weapons and slings.",
                "Stealth advantages outdoors and in natural cover.",
            },
            new()
            {
                new() { Id="halfling_thrown_bonus", Description="+1 attack bonus with thrown weapons and slings.",
                    Effect=new(){ WeaponAttackBonuses=new(){{"thrown",1},{"slings",1}} } },
                new() { Id="halfling_stealth",      Description="Stealth advantages outdoors and in natural cover.",
                    Effect=new(){ GrantsStealth=true } },
                new() { Id="halfling_poison_save",  Description="Saving throw bonus versus poison.",
                    Effect=new(){ SaveBonuses=new(){{"poison",4}} } },
            });
        Races["gnome"]    = new("gnome", "Gnome", "all", "gnome",
            new() { {"int",6},{"con",8} }, new(),
            new(),
            new()
            {
                "Infravision 60 feet.",
                "Notice grade, depth, and unsafe stonework underground.",
            },
            new()
            {
                new() { Id="gnome_infravision", Description="Infravision 60 feet.",
                    Effect=new(){ InfravisionFeet=60 } },
                new() { Id="gnome_stonework",   Description="Notice grade, depth, and unsafe stonework underground.",
                    Effect=new(){ DetectStonework=true } },
                new() { Id="gnome_save_bonus",  Description="Saving throw bonus versus magic.",
                    Effect=new(){ SaveBonuses=new(){{"magic",4}} } },
            });
        Races["half-elf"] = new("half-elf", "Half-Elf", "all", "half-elf", new(), new(),
            new(),
            new()
            {
                "30% resistance to sleep and charm magic.",
                "Infravision 60 feet.",
                "Detect secret and concealed doors with a careful search.",
            },
            new()
            {
                new() { Id="half_elf_charm_resist", Description="30% resistance to sleep and charm magic.",
                    Effect=new(){ MagicResistPercent=30 } },
                new() { Id="half_elf_infravision",  Description="Infravision 60 feet.",
                    Effect=new(){ InfravisionFeet=60 } },
                new() { Id="half_elf_secret_doors", Description="Detect secret and concealed doors with a careful search.",
                    Effect=new(){ DetectSecretDoors=true } },
            });

        // Classes – no class abilities yet; structured abilities list is empty for now
        Classes["fighter"] = new("fighter", "Fighter",
            new() { {"str",9} },
            new() { "human","elf","dwarf","halfling","gnome","half-elf","half-orc","half-ogre" },
            new());
        Classes["wizard"]  = new("wizard", "Wizard",
            new() { {"int",9} },
            new() { "human","elf","gnome","half-elf" },
            new());
        Classes["cleric"]  = new("cleric", "Cleric",
            new() { {"wis",9} },
            new() { "human","elf","dwarf","halfling","gnome","half-elf","half-orc","half-ogre" },
            new());
        Classes["thief"]   = new("thief", "Thief",
            new() { {"dex",9} },
            new() { "human","elf","dwarf","halfling","gnome","half-elf","half-orc" },
            new());
        Classes["ranger"]  = new("ranger", "Ranger",
            new() { {"str",13},{"dex",13},{"con",14},{"wis",14} },
            new() { "human","elf","half-elf" },
            new());
        Classes["paladin"] = new("paladin", "Paladin",
            new() { {"str",12},{"con",9},{"wis",13},{"cha",17} },
            new() { "human" },
            new());
    }

    // ── Character generation ─────────────────────────────────────────────────

    public Dictionary<string, int> RollAbilities() =>
        GenerateAbilities("method_v_4d6_drop_lowest");

    public Dictionary<string, int> StandardArrayAbilities() =>
        GenerateAbilities("standard_array");

    public Dictionary<string, int> GenerateAbilities(string method)
    {
        return method switch
        {
            "method_i_3d6_in_order" => RollInOrder3d6(),
            "method_ii_3d6_twice_keep_best" => RollInOrder3d6BestOfTwo(),
            "method_iii_3d6_arrange" => RollArrange3d6(),
            "method_iv_3d6_12_choose_6" => RollBest6Of12Arrange(),
            "method_v_4d6_drop_lowest" => Roll4d6DropLowestArrange(),
            "method_vi_8_plus_7d6" => RollMethodVi(),
            "homebrew_4d6_reroll_1s" => Roll4d6DropLowestRerollOnesArrange(),
            "standard_array" => BuildFromScores(StandardArray.ToList()),
            _ => Roll4d6DropLowestArrange(),
        };
    }

    /// <summary>
    /// Apply racial ability modifiers to base ability scores.
    /// Validates against race minimums and maximums and clamps results to valid ranges.
    /// </summary>
    public Dictionary<string, int> ApplyRacialModifiers(
        string raceId,
        Dictionary<string, int> baseAbilities)
    {
        // Defensive: ensure we have valid input
        if (string.IsNullOrEmpty(raceId) || baseAbilities == null || baseAbilities.Count == 0)
            return new Dictionary<string, int>(baseAbilities ?? new());

        if (!Races.TryGetValue(raceId, out var race))
            return new Dictionary<string, int>(baseAbilities);

        var modified = new Dictionary<string, int>(baseAbilities);
        var effectiveModifiers = GetEffectiveAbilityModifiers(raceId);

        // Apply racial modifiers (supports fallback defaults when JSON omits ability_modifiers)
        if (effectiveModifiers.Count > 0)
        {
            foreach (var (ability, modifier) in effectiveModifiers)
            {
                if (modified.TryGetValue(ability, out var baseScore))
                {
                    int newScore = baseScore + modifier;
                    
                    // Validate against racial min/max constraints
                    if (race.AbilityMinimums?.TryGetValue(ability, out var min) ?? false)
                        newScore = Math.Max(newScore, min);
                    
                    if (race.AbilityMaximums?.TryGetValue(ability, out var max) ?? false)
                        newScore = Math.Min(newScore, max);

                    modified[ability] = newScore;
                }
            }
        }

        return modified;
    }

    public Dictionary<string, int> GetEffectiveAbilityModifiers(string raceId)
    {
        if (string.IsNullOrWhiteSpace(raceId))
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (!Races.TryGetValue(raceId, out var race))
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (race.AbilityModifiers is { Count: > 0 })
            return new Dictionary<string, int>(race.AbilityModifiers, StringComparer.OrdinalIgnoreCase);

        string basis = string.IsNullOrWhiteSpace(race.BaseRaceId) ? race.Id : race.BaseRaceId;
        basis = basis.Trim().ToLowerInvariant().Replace("_", "-");

        // Compatibility fallback for core racial adjustments when rules JSON omits ability_modifiers.
        return basis switch
        {
            "elf" => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["dex"] = 1,
                ["con"] = -1,
            },
            "dwarf" => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["con"] = 1,
                ["cha"] = -1,
            },
            "halfling" => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["dex"] = 1,
                ["str"] = -1,
            },
            _ => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        };
    }

    public List<int> GenerateDicePool(string method)
    {
        return method switch
        {
            "method_i_3d6_in_order" => Enumerable.Range(0, 6).Select(_ => RollDice(3)).ToList(),
            "method_ii_3d6_twice_keep_best" => Enumerable.Range(0, 6)
                .Select(_ => Math.Max(RollDice(3), RollDice(3))).ToList(),
            "method_iii_3d6_arrange" => Enumerable.Range(0, 6).Select(_ => RollDice(3)).ToList(),
            "method_iv_3d6_12_choose_6" => Enumerable.Range(0, 12).Select(_ => RollDice(3))
                .OrderByDescending(v => v).Take(6).ToList(),
            "method_v_4d6_drop_lowest" => Enumerable.Range(0, 6)
                .Select(_ => RollDiceKeepHighest(4, 3, rerollOnes: false)).ToList(),
            "method_vi_8_plus_7d6" => Enumerable.Range(0, 7).Select(_ => RollDie(rerollOnes: false)).ToList(),
            "homebrew_4d6_reroll_1s" => Enumerable.Range(0, 6)
                .Select(_ => RollDiceKeepHighest(4, 3, rerollOnes: true)).ToList(),
            "standard_array" => StandardArray.ToList(),
            _ => Enumerable.Range(0, 6)
                .Select(_ => RollDiceKeepHighest(4, 3, rerollOnes: false)).ToList(),
        };
    }

    public static string MethodDisplay(string method) => method switch
    {
        "method_i_3d6_in_order" => "Method I: 3d6 in order",
        "method_ii_3d6_twice_keep_best" => "Method II: 3d6 twice, keep desired score",
        "method_iii_3d6_arrange" => "Method III: 3d6, arrange to taste",
        "method_iv_3d6_12_choose_6" => "Method IV: 3d6 x12, choose best 6",
        "method_v_4d6_drop_lowest" => "Method V: 4d6, drop lowest",
        "method_vi_8_plus_7d6" => "Method VI: Base 8 + 7d6 distributed",
        "homebrew_4d6_reroll_1s" => "Homebrew: 4d6 reroll 1s, drop lowest",
        "manual_entry" => "Manual entry",
        "standard_array" => "Standard array",
        _ => "Custom method",
    };

    public static bool CanAutoRoll(string method) => method != "manual_entry";

    public static bool UsesDistributedDice(string method) => method == "method_vi_8_plus_7d6";

    private Dictionary<string, int> RollInOrder3d6() =>
        BuildFromScores(Enumerable.Range(0, 6).Select(_ => RollDice(3)).ToList());

    private Dictionary<string, int> RollInOrder3d6BestOfTwo() =>
        BuildFromScores(Enumerable.Range(0, 6).Select(_ => Math.Max(RollDice(3), RollDice(3))).ToList());

    private Dictionary<string, int> RollArrange3d6()
    {
        var scores = Enumerable.Range(0, 6).Select(_ => RollDice(3)).OrderByDescending(v => v).ToList();
        return BuildFromScores(scores);
    }

    private Dictionary<string, int> RollBest6Of12Arrange()
    {
        var scores = Enumerable.Range(0, 12).Select(_ => RollDice(3))
            .OrderByDescending(v => v).Take(6).ToList();
        return BuildFromScores(scores);
    }

    private Dictionary<string, int> Roll4d6DropLowestArrange()
    {
        var scores = Enumerable.Range(0, 6)
            .Select(_ => RollDiceKeepHighest(4, 3, rerollOnes: false))
            .OrderByDescending(v => v).ToList();
        return BuildFromScores(scores);
    }

    private Dictionary<string, int> Roll4d6DropLowestRerollOnesArrange()
    {
        var scores = Enumerable.Range(0, 6)
            .Select(_ => RollDiceKeepHighest(4, 3, rerollOnes: true))
            .OrderByDescending(v => v).ToList();
        return BuildFromScores(scores);
    }

    private Dictionary<string, int> RollMethodVi()
    {
        var scores = Enumerable.Repeat(8, 6).ToArray();
        for (int i = 0; i < 7; i++)
        {
            int die = RollDie(rerollOnes: false);
            var candidates = scores
                .Select((value, idx) => new { value, idx })
                .Where(x => x.value + die <= 18)
                .OrderBy(x => x.value)
                .ToList();

            int target = candidates.Count > 0
                ? candidates[0].idx
                : scores.Select((value, idx) => new { value, idx })
                    .OrderBy(x => x.value).First().idx;

            scores[target] = Math.Min(18, scores[target] + die);
        }

        return BuildFromScores(scores.ToList());
    }

    private Dictionary<string, int> BuildFromScores(List<int> scores)
    {
        var result = new Dictionary<string, int>();
        for (int i = 0; i < AbilityOrder.Length && i < scores.Count; i++)
            result[AbilityOrder[i]] = scores[i];
        return result;
    }

    private int RollDice(int count)
    {
        int total = 0;
        for (int i = 0; i < count; i++) total += RollDie(rerollOnes: false);
        return total;
    }

    private int RollDiceKeepHighest(int count, int keep, bool rerollOnes)
    {
        var dice = new List<int>();
        for (int i = 0; i < count; i++) dice.Add(RollDie(rerollOnes));
        return dice.OrderByDescending(d => d).Take(keep).Sum();
    }

    private int RollDie(bool rerollOnes)
    {
        int value = _rng.Next(1, 7);
        if (rerollOnes)
            while (value == 1) value = _rng.Next(1, 7);
        return value;
    }

    // ── Validation ───────────────────────────────────────────────────────────

    public List<string> Validate(string raceId, string classId,
                                  Dictionary<string, int> abilities)
    {
        var issues = new List<string>();
        if (!Races.TryGetValue(raceId, out var race))   { issues.Add($"Unknown race: {raceId}");  return issues; }
        if (!Classes.TryGetValue(classId, out var cls)) { issues.Add($"Unknown class: {classId}"); return issues; }

        foreach (var (ab, min) in race.AbilityMinimums)
            if (abilities.GetValueOrDefault(ab) < min)
                issues.Add($"Race minimum: {AbilityName(ab)} must be at least {min}");

        foreach (var (ab, max) in race.AbilityMaximums)
            if (abilities.GetValueOrDefault(ab) > max)
                issues.Add($"Race maximum: {AbilityName(ab)} must be at most {max}");

        foreach (var (ab, min) in cls.AbilityMinimums)
            if (abilities.GetValueOrDefault(ab) < min)
                issues.Add($"Class minimum: {AbilityName(ab)} must be at least {min}");

        if (cls.AllowedRaces.Count > 0 &&
            !cls.AllowedRaces.Contains(raceId) &&
            !cls.AllowedRaces.Contains(race.BaseRaceId))
            issues.Add($"{race.Name} cannot be a {cls.Name}");

        if (race.RacialPointBudget > 0)
        {
            var autoCost = race.StructuredAbilities.Where(a => a.AutoGranted).Sum(a => a.PointCost);
            if (autoCost > race.RacialPointBudget)
                issues.Add($"{race.Name} auto-assigned abilities spend {autoCost} points, exceeding budget {race.RacialPointBudget}.");
        }

        return issues;
    }

    public (int budget, int spent, int remaining, List<AbilityDefinition> selectedAbilities)
        BuildRacialAbilityPackage(string raceId, IEnumerable<string>? selectedOptionalAbilityIds = null)
    {
        if (!Races.TryGetValue(raceId, out var race))
            return (0, 0, 0, new List<AbilityDefinition>());

        if (string.Equals(race.BaseRaceId, "halfling", StringComparison.OrdinalIgnoreCase) &&
            race.Name.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            var siblings = Races.Values
                .Where(r => string.Equals(r.BaseRaceId, "halfling", StringComparison.OrdinalIgnoreCase))
                .Where(r => string.Equals(r.CharacterMode, race.CharacterMode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            race = MergeHalflingOptionsIntoBasic(new List<RaceDefinition>(siblings))
                .FirstOrDefault(r => string.Equals(r.Id, raceId, StringComparison.OrdinalIgnoreCase))
                ?? race;
        }

        var selected = SelectRacialAbilities(race, selectedOptionalAbilityIds);
        var spent = selected.Sum(a => a.PointCost);
        var budget = Math.Max(0, race.RacialPointBudget);
        var remaining = budget - spent;
        return (budget, spent, remaining, selected);
    }

    public (int budget, int spent, int remaining, List<AbilityDefinition> selectedAbilities)
        BuildClassAbilityPackage(string classId,
                                 IEnumerable<string>? selectedOptionalClassAbilityIds = null,
                                 int classAbilityCarryoverPoints = 0,
                                 string? specializationId = null)
    {
        if (!Classes.TryGetValue(classId, out var cls))
            return (0, 0, 0, new List<AbilityDefinition>());

        // Merge user-selected IDs with any auto-selected IDs from the chosen specialization
        var specAutoIds = new List<string>();
        int classBudget = cls.ClassPointBudget;
        if (!string.IsNullOrWhiteSpace(specializationId) && cls.Specializations is not null)
        {
            var spec = cls.Specializations.FirstOrDefault(s =>
                string.Equals(s.Id, specializationId, StringComparison.OrdinalIgnoreCase));
            if (spec is not null)
            {
                classBudget = spec.ClassPointBudget;
                specAutoIds = spec.AutoSelectAbilityIds;
            }
        }

        var mergedIds = new HashSet<string>(
            (selectedOptionalClassAbilityIds ?? Enumerable.Empty<string>()).Concat(specAutoIds),
            StringComparer.OrdinalIgnoreCase);

        var selected = SelectClassAbilities(cls, mergedIds);
        var spent = selected.Sum(a => a.PointCost);
        var carryover = Math.Clamp(classAbilityCarryoverPoints, 0, 5);
        var budget = Math.Max(0, classBudget) + carryover;
        var remaining = budget - spent;
        return (budget, spent, remaining, selected);
    }

    private static List<AbilityDefinition> SelectRacialAbilities(RaceDefinition race,
        IEnumerable<string>? selectedOptionalAbilityIds)
    {
        var selected = new List<AbilityDefinition>();

        // Automatic package abilities are always granted (sub-race defaults).
        selected.AddRange(race.StructuredAbilities.Where(a => a.AutoGranted));

        // Optional abilities are granted only when selected.
        var optionalIds = new HashSet<string>(selectedOptionalAbilityIds ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        selected.AddRange(race.StructuredAbilities.Where(a => !a.AutoGranted && optionalIds.Contains(a.Id)));

        return selected
            .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static List<AbilityDefinition> SelectClassAbilities(ClassDefinition cls,
        IEnumerable<string>? selectedOptionalAbilityIds)
    {
        var selected = new List<AbilityDefinition>();

        selected.AddRange(cls.StructuredAbilities.Where(a => a.AutoGranted));

        var optionalIds = new HashSet<string>(selectedOptionalAbilityIds ?? Enumerable.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        selected.AddRange(cls.StructuredAbilities.Where(a => !a.AutoGranted && optionalIds.Contains(a.Id)));

        return selected
            .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    public CharacterSheet BuildCharacter(string name, string raceId, string classId,
                                          Dictionary<string, int> abilities,
                                          IEnumerable<string>? selectedOptionalRacialAbilityIds = null,
                                          IEnumerable<string>? selectedOptionalClassAbilityIds = null,
                                          int classAbilityCarryoverPoints = 0,
                                          string? specializationId = null,
                                          Dictionary<string, int>? subAbilities = null,
                                          int exceptionalStrength = 0,
                                          string? armorProfile = null)
    {
        Races.TryGetValue(raceId, out var race);
        Classes.TryGetValue(classId, out var cls);
        string normalizedArmorProfile = string.IsNullOrWhiteSpace(armorProfile)
            ? "no_armor"
            : armorProfile.Trim().ToLowerInvariant();
        bool isUnarmored = normalizedArmorProfile == "no_armor";

        // Gather all structured abilities from race and class
        var allStructured = new List<AbilityDefinition>();
        var selectedRacial = new List<AbilityDefinition>();
        int racialBudget = 0;
        int racialSpent = 0;
        int racialRemaining = 0;
        int classBudget = 0;
        int classSpent = 0;
        int classRemaining = 0;
        var selectedClass = new List<AbilityDefinition>();

        if (race is not null)
        {
            var package = BuildRacialAbilityPackage(raceId, selectedOptionalRacialAbilityIds);
            selectedRacial = package.selectedAbilities;
            racialBudget = package.budget;
            racialSpent = package.spent;
            racialRemaining = package.remaining;
            allStructured.AddRange(selectedRacial);
        }
        if (cls is not null)
        {
            var classPackage = BuildClassAbilityPackage(classId, selectedOptionalClassAbilityIds, classAbilityCarryoverPoints, specializationId);
            selectedClass = classPackage.selectedAbilities;
            classBudget = classPackage.budget;
            classSpent = classPackage.spent;
            classRemaining = classPackage.remaining;
            allStructured.AddRange(selectedClass);
        }

        // Aggregate the race/class ability bonuses first
        var bonuses = AggregateEffects(allStructured, isUnarmored);

        // Seed sub-abilities: use provided values (PO flow), else default to base ability values.
        var effectiveSubAbilities = BuildDefaultSubAbilities(abilities, subAbilities);

        // Apply sub-ability modifiers granted by race/class abilities.
        // Supports either full key (str_muscle) or suffix-only key (muscle).
        foreach (var (key, mod) in bonuses.SubAbilityBonuses)
        {
            if (string.IsNullOrWhiteSpace(key) || mod == 0) continue;

            if (effectiveSubAbilities.ContainsKey(key))
            {
                effectiveSubAbilities[key] += mod;
                continue;
            }

            var matchKey = effectiveSubAbilities.Keys
                .FirstOrDefault(k => k.EndsWith($"_{key}", StringComparison.OrdinalIgnoreCase));
            if (matchKey is not null)
                effectiveSubAbilities[matchKey] += mod;
        }

        // Clamp to playable bounds after modifiers.
        foreach (var k in effectiveSubAbilities.Keys.ToList())
            effectiveSubAbilities[k] = Math.Clamp(effectiveSubAbilities[k], 1, 20);

        // Derive concrete mechanics from sub-abilities and fold into character bonuses.
        var subTotals = SubAbilityTables.CalculateTotals(effectiveSubAbilities, exceptionalStrength, classId);
        bonuses.AttackBonus += subTotals.MeleeAttackBonus;
        bonuses.DamageBonus += subTotals.MeleeDamageBonus;
        bonuses.AcBonus += subTotals.ArmorClassAdjustment;
        bonuses.SurpriseBonus += subTotals.SurpriseAdjustment;
        bonuses.ReactionBonus += subTotals.ReactionAdjustment;
        bonuses.HpPerLevel += subTotals.HpPerLevel;
        bonuses.NwpSlotBonus += subTotals.BonusNwpSlots;
        bonuses.NwpCheckBonus += subTotals.PickPocketsAdjustment;
        bonuses.NwpCheckBonus += subTotals.OpenLocksAdjustment;
        bonuses.NwpCheckBonus += subTotals.MoveSilentlyAdjustment;
        bonuses.NwpCheckBonus += subTotals.ClimbWallsAdjustment;

        if (subTotals.PoisonSaveAdjustment != 0)
            bonuses.SaveBonuses["poison"] = bonuses.SaveBonuses.GetValueOrDefault("poison") + subTotals.PoisonSaveAdjustment;

        if (subTotals.MissileAttackBonus != 0)
        {
            bonuses.WeaponAttackBonuses["missile"] = bonuses.WeaponAttackBonuses.GetValueOrDefault("missile") + subTotals.MissileAttackBonus;
            bonuses.WeaponAttackBonuses["thrown"] = bonuses.WeaponAttackBonuses.GetValueOrDefault("thrown") + subTotals.MissileAttackBonus;
            bonuses.WeaponAttackBonuses["slings"] = bonuses.WeaponAttackBonuses.GetValueOrDefault("slings") + subTotals.MissileAttackBonus;
        }

        if (subTotals.MagicDefenseAdjustment != 0)
            bonuses.SaveBonuses["magic"] = bonuses.SaveBonuses.GetValueOrDefault("magic") + subTotals.MagicDefenseAdjustment;

        if (subTotals.SpellImmunityPercent != 0)
            bonuses.MagicResistPercent += subTotals.SpellImmunityPercent;

        // Preserve thief-style sub-check adjustments explicitly for downstream systems.
        bonuses.SubAbilityBonuses["pick_pockets"] = bonuses.SubAbilityBonuses.GetValueOrDefault("pick_pockets") + subTotals.PickPocketsAdjustment;
        bonuses.SubAbilityBonuses["open_locks"] = bonuses.SubAbilityBonuses.GetValueOrDefault("open_locks") + subTotals.OpenLocksAdjustment;
        bonuses.SubAbilityBonuses["move_silently"] = bonuses.SubAbilityBonuses.GetValueOrDefault("move_silently") + subTotals.MoveSilentlyAdjustment;
        bonuses.SubAbilityBonuses["climb_walls"] = bonuses.SubAbilityBonuses.GetValueOrDefault("climb_walls") + subTotals.ClimbWallsAdjustment;

        // Base HP: check if any ability provides a dice expression; otherwise use class hit die
        int baseHp;
        var hpDiceExpression = allStructured
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Effect?.HpDiceExpression))
            ?.Effect?.HpDiceExpression;

        if (!string.IsNullOrWhiteSpace(hpDiceExpression))
        {
            // Roll the dice from the ability
            baseHp = DiceRoller.Roll(hpDiceExpression);
        }
        else
        {
            // Use default class hit die
            baseHp = BaseHitPoints(classId);
        }

        baseHp += ConModifier(abilities.GetValueOrDefault("con", 10));
        if (baseHp < 1) baseHp = 1;

        // Effective HP at level 1: flat bonuses and hp/level both apply once.
        int effectiveHp = baseHp + bonuses.HpFlatBonus + bonuses.HpPerLevel;
        if (effectiveHp < 1) effectiveHp = 1;

        // Effective AC (descending): base 10 + bonuses.AcBonus.
        // bonuses.AcBonus already includes the DEX defensive adjustment via subTotals.ArmorClassAdjustment,
        // so do NOT add GetDexterityAcAdjustment separately — that would double-count it.
        int effectiveAc = 10 + bonuses.AcBonus;

        // Base racial movement then class/racial ability adjustments.
        int baseMovement = GetBaseMovementRate(raceId, race?.BaseRaceId);
        int effectiveMovement = Math.Max(1, baseMovement + bonuses.MovementBonus);

        var racialAbilities = race?.RacialAbilities.ToList() ?? new List<string>();
        if (selectedRacial.Count > 0)
            racialAbilities = selectedRacial.Select(a => a.Description).ToList();

        var selectedRacialIds = selectedRacial.Select(a => a.Id).ToList();
        var selectedClassIds = selectedClass.Select(a => a.Id).ToList();
        var subEffectSnapshot = effectiveSubAbilities
            .ToDictionary(kv => kv.Key, kv => SubAbilityTables.GetEffect(kv.Key, kv.Value, exceptionalStrength));

        var derivedNotes = subTotals.ToNotes();
        var allNotes = racialAbilities.Select(a => $"Racial Ability: {a}").Concat(derivedNotes).ToList();

        if (racialBudget > 0 && racialRemaining < 0)
            throw new InvalidOperationException($"Racial ability cost exceeds budget for {race?.Name ?? raceId}: spent {racialSpent}, budget {racialBudget}.");
        if (classBudget > 0 && classRemaining < 0)
            throw new InvalidOperationException($"Class ability cost exceeds budget for {cls?.Name ?? classId}: spent {classSpent}, budget {classBudget}.");

        return new CharacterSheet
        {
            Name          = name,
            RaceId        = raceId,
            ClassId       = classId,
            CharacterMode = race?.CharacterMode == "players_option" ? "players_option" : "core_rules",
            Level         = 1,
            BaseHitPoints = baseHp,
            HitPoints     = effectiveHp,
            BaseArmorClass = 10,
            ArmorClass    = effectiveAc,
            ArmorProfile  = normalizedArmorProfile,
            BaseMovement  = baseMovement,
            Movement      = effectiveMovement,
            Revision      = 1,
            LastModified  = DateTime.Now,
            Abilities     = new Dictionary<string, int>(abilities),
            Notes         = allNotes,
            RacialAbilities    = racialAbilities,
            StructuredAbilities = allStructured,
            Bonuses       = bonuses,
            RacialPointBudget = racialBudget,
            RacialPointSpent = racialSpent,
            RacialPointRemaining = racialRemaining,
            ClassPointBudget = classBudget,
            ClassPointSpent = classSpent,
            ClassPointRemaining = classRemaining,
            ClassAbilityCarryoverPoints = Math.Max(0, classAbilityCarryoverPoints),
            SelectedRacialAbilityIds = selectedRacialIds,
            SelectedClassAbilityIds = selectedClassIds,
            SubAbilities = effectiveSubAbilities,
            ExceptionalStrength = exceptionalStrength,
            DerivedStats = subTotals.ToDerivedStats(),
            SubAbilityEffects = subEffectSnapshot,
            RaceName      = race?.Name  ?? raceId,
            ClassName     = cls?.Name   ?? classId,
        };
    }

    private static int GetBaseMovementRate(string? raceId, string? baseRaceId)
    {
        string key = string.IsNullOrWhiteSpace(baseRaceId) ? (raceId ?? string.Empty) : baseRaceId;
        key = key.Trim().ToLowerInvariant().Replace("_", "-");

        return key switch
        {
            "dwarf" or "gnome" or "halfling" => 6,
            _ => 12,
        };
    }

    private static Dictionary<string, int> BuildDefaultSubAbilities(
        IReadOnlyDictionary<string, int> abilities,
        Dictionary<string, int>? supplied)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["str_muscle"] = abilities.GetValueOrDefault("str", 10),
            ["str_stamina"] = abilities.GetValueOrDefault("str", 10),
            ["dex_aim"] = abilities.GetValueOrDefault("dex", 10),
            ["dex_balance"] = abilities.GetValueOrDefault("dex", 10),
            ["con_health"] = abilities.GetValueOrDefault("con", 10),
            ["con_fitness"] = abilities.GetValueOrDefault("con", 10),
            ["int_reason"] = abilities.GetValueOrDefault("int", 10),
            ["int_knowledge"] = abilities.GetValueOrDefault("int", 10),
            ["wis_intuition"] = abilities.GetValueOrDefault("wis", 10),
            ["wis_willpower"] = abilities.GetValueOrDefault("wis", 10),
            ["wis_perception"] = abilities.GetValueOrDefault("wis", 10),
            ["cha_leadership"] = abilities.GetValueOrDefault("cha", 10),
            ["cha_appearance"] = abilities.GetValueOrDefault("cha", 10),
        };

        if (supplied is null) return result;

        foreach (var (k, v) in supplied)
            if (!string.IsNullOrWhiteSpace(k) && result.ContainsKey(k))
                result[k] = v;

        return result;
    }

    // ── Class eligibility for a given race ───────────────────────────────────

    public IEnumerable<ClassDefinition> EligibleClasses(string raceId)
    {
        if (!Races.TryGetValue(raceId, out var race)) 
            return Classes.Values;
        
        return Classes.Values.Where(c => 
            (c.AllowedRaces == null || c.AllowedRaces.Count == 0) ||
            (c.AllowedRaces != null && c.AllowedRaces.Contains(raceId)) ||
            (!string.IsNullOrEmpty(race.BaseRaceId) && c.AllowedRaces != null && c.AllowedRaces.Contains(race.BaseRaceId)));
    }

    // ── Multi-class combination rules ────────────────────────────────────────

    // Each entry is a valid set of class IDs for that race group.
    // Derived from PHB 2e p. 44-45 and Dragon Magazine (ranger/druid for half-elves).
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<HashSet<string>>> _validMultiClassCombos =
        new Dictionary<string, IReadOnlyList<HashSet<string>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["elf"] = new[]
            {
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "wizard" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "thief" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wizard", "thief" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "wizard", "thief" },
            },
            ["gnome"] = new[]
            {
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "wizard" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "thief" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "cleric" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wizard", "thief" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cleric", "wizard" },
            },
            ["half-elf"] = new[]
            {
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cleric", "fighter" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cleric", "ranger" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cleric", "wizard" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "wizard" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "thief" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wizard", "thief" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "wizard", "cleric" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "wizard", "thief" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "druid", "fighter" },  // Dragon Magazine
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "druid", "ranger" },   // Dragon Magazine
            },
            ["halfling"] = new[]
            {
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "thief" },
            },
            ["dwarf"] = new[]
            {
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "cleric" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "thief" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cleric", "thief" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "cleric", "thief" },
            },
            ["half-orc"] = new[]
            {
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cleric", "fighter" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cleric", "thief" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "thief" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cleric", "fighter", "thief" },
            },
            ["half-ogre"] = new[]
            {
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fighter", "thief" },
            },
        };

    private static string GetMultiClassRaceGroup(string raceId)
    {
        string r = raceId.ToLowerInvariant();
        // Order matters: half-elf before elf, half-orc/half-ogre before orc
        if (r.Contains("half") && r.Contains("elf"))  return "half-elf";
        if (r.Contains("half") && r.Contains("ogre")) return "half-ogre";
        if (r.Contains("half") && r.Contains("orc"))  return "half-orc";
        if (r.Contains("elf"))      return "elf";
        if (r.Contains("dwarf"))    return "dwarf";
        if (r.Contains("gnome"))    return "gnome";
        if (r.Contains("halfling") || r.Contains("hairfoot") || r.Contains("stout") || r.Contains("tallfellow"))
            return "halfling";
        return r; // human or unknown — no valid combos defined
    }


    // ── Rogue skill helpers ─────────────────────────────────────────────────
    /// <summary>
    /// Returns valid multi-class combinations for a race as sets of class IDs.
    /// Uses loaded multiclass_combos.json; falls back to hardcoded data.
    /// Returns empty list for races that don't support multiclassing (e.g. human).
    /// </summary>
    public IReadOnlyList<HashSet<string>> GetValidMultiClassCombos(string raceId)
    {
        string group = GetMultiClassRaceGroup(raceId);

        // Resolve base-race group for sub-races
        if (Races.TryGetValue(raceId, out var race) && !string.IsNullOrEmpty(race.BaseRaceId)
            && race.BaseRaceId != raceId)
        {
            string baseGroup = GetMultiClassRaceGroup(race.BaseRaceId);
            if (!string.IsNullOrEmpty(baseGroup)) group = baseGroup;
        }

        // Prefer loaded data
        if (MultiClassCombos.Count > 0)
        {
            var loaded = MultiClassCombos.FirstOrDefault(g =>
                string.Equals(g.RaceGroup, group, StringComparison.OrdinalIgnoreCase));
            if (loaded is not null)
                return loaded.Combos.Select(c =>
                    new HashSet<string>(c, StringComparer.OrdinalIgnoreCase)).ToList();
            return Array.Empty<HashSet<string>>();
        }

        // Fallback to hardcoded table
        return _validMultiClassCombos.TryGetValue(group, out var combos)
            ? combos
            : Array.Empty<HashSet<string>>();
    }


    public static readonly Dictionary<string, string> RogueSkillAbilityToSkillId =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["thief_pick_pockets"] = "pick_pockets",
            ["thief_open_locks"] = "open_locks",
            ["thief_find_remove_traps"] = "find_remove_traps",
            ["thief_move_silently"] = "move_silently",
            ["thief_hide_in_shadows"] = "hide_in_shadows",
            ["thief_detect_noise"] = "detect_noise",
            ["thief_climb_walls"] = "climb_walls",
            ["thief_read_languages"] = "read_languages",
            ["bard_pick_pockets"] = "pick_pockets",
            ["bard_open_locks"] = "open_locks",
            ["bard_find_remove_traps"] = "find_remove_traps",
            ["bard_move_silently"] = "move_silently",
            ["bard_hide_in_shadows"] = "hide_in_shadows",
            ["bard_detect_noise"] = "detect_noise",
            ["bard_climb_walls"] = "climb_walls",
            ["bard_read_languages"] = "read_languages",
        };

    public static readonly Dictionary<string, string> RogueSkillNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pick_pockets"] = "Pick Pockets",
            ["open_locks"] = "Open Locks",
            ["find_remove_traps"] = "Find/Remove Traps",
            ["move_silently"] = "Move Silently",
            ["hide_in_shadows"] = "Hide in Shadows",
            ["detect_noise"] = "Detect Noise",
            ["climb_walls"] = "Climb Walls",
            ["read_languages"] = "Read Languages",
        };

    private static readonly Dictionary<string, int> RogueSkillBaseScores =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pick_pockets"] = 15,
            ["open_locks"] = 10,
            ["find_remove_traps"] = 5,
            ["move_silently"] = 10,
            ["hide_in_shadows"] = 5,
            ["detect_noise"] = 15,
            ["climb_walls"] = 60,
            ["read_languages"] = 0,
        };

    private static readonly Dictionary<string, Dictionary<string, int>> RogueSkillRacialAdjustments =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["dwarf"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["open_locks"] = 10,
                ["find_remove_traps"] = 15,
                ["climb_walls"] = -10,
                ["read_languages"] = -5,
            },
            ["elf"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["pick_pockets"] = 5,
                ["open_locks"] = -5,
                ["move_silently"] = 5,
                ["hide_in_shadows"] = 10,
                ["detect_noise"] = 5,
            },
            ["gnome"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["open_locks"] = 5,
                ["find_remove_traps"] = 10,
                ["move_silently"] = 5,
                ["hide_in_shadows"] = 5,
                ["detect_noise"] = 10,
                ["climb_walls"] = -15,
            },
            ["half_elf"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["pick_pockets"] = 10,
                ["hide_in_shadows"] = 5,
            },
            ["halfling"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["pick_pockets"] = 5,
                ["open_locks"] = 5,
                ["find_remove_traps"] = 5,
                ["move_silently"] = 10,
                ["hide_in_shadows"] = 15,
                ["detect_noise"] = 5,
                ["climb_walls"] = -15,
                ["read_languages"] = -5,
            },
        };

    private static readonly Dictionary<string, Dictionary<string, int>> RogueSkillArmorAdjustments =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["no_armor"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["pick_pockets"] = 5,
                ["move_silently"] = 10,
                ["hide_in_shadows"] = 5,
                ["climb_walls"] = 10,
            },
            ["elven_chain"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["pick_pockets"] = -20,
                ["open_locks"] = -5,
                ["find_remove_traps"] = -5,
                ["move_silently"] = -10,
                ["hide_in_shadows"] = -10,
                ["detect_noise"] = -5,
                ["climb_walls"] = -20,
            },
            ["studded_leather"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["pick_pockets"] = -30,
                ["open_locks"] = -10,
                ["find_remove_traps"] = -10,
                ["move_silently"] = -20,
                ["hide_in_shadows"] = -20,
                ["detect_noise"] = -10,
                ["climb_walls"] = -30,
            },
            ["chain_or_ring_mail"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["pick_pockets"] = -25,
                ["open_locks"] = -10,
                ["find_remove_traps"] = -10,
                ["move_silently"] = -15,
                ["hide_in_shadows"] = -15,
                ["detect_noise"] = -5,
                ["climb_walls"] = -25,
            },
        };

    private static readonly string[] DexterityAdjustedRogueSkills =
    {
        "pick_pockets", "open_locks", "find_remove_traps", "move_silently", "hide_in_shadows"
    };

    public static int GetRogueSkillCreationPool() => 60;

    public static int GetRogueSkillPerLevelGain() => 30;

    public static int GetRogueSkillPointPoolForLevel(int level)
    {
        var clampedLevel = Math.Max(1, level);
        return GetRogueSkillCreationPool() + ((clampedLevel - 1) * GetRogueSkillPerLevelGain());
    }

    public static int GetRogueSkillPerSkillAllocationCap(int level)
    {
        var clampedLevel = Math.Max(1, level);
        return 30 + ((clampedLevel - 1) * 15);
    }

    public static List<string> GetRogueSkillIdsForAbilitySelection(IEnumerable<string> selectedAbilityIds)
    {
        var result = new List<string>();
        foreach (var abilityId in selectedAbilityIds ?? Enumerable.Empty<string>())
        {
            if (RogueSkillAbilityToSkillId.TryGetValue(abilityId, out var skillId) && !result.Contains(skillId))
                result.Add(skillId);
        }
        return result;
    }

    public static string GetRogueSkillName(string skillId)
        => RogueSkillNames.TryGetValue(skillId, out var name) ? name : skillId;

    public static int GetRogueSkillBaseScore(string skillId)
        => RogueSkillBaseScores.TryGetValue(skillId, out var value) ? value : 0;

    public static int GetRogueSkillRacialAdjustment(string skillId, string? raceId)
    {
        var normalizedRace = NormalizeRogueRaceId(raceId);
        if (RogueSkillRacialAdjustments.TryGetValue(normalizedRace, out var table)
            && table.TryGetValue(skillId, out var value))
            return value;
        return 0;
    }

    public static int GetRogueSkillDexterityAdjustment(string skillId, int dexterity)
    {
        if (!DexterityAdjustedRogueSkills.Contains(skillId, StringComparer.OrdinalIgnoreCase))
            return 0;

        return dexterity switch
        {
            <= 9 => skillId == "pick_pockets" ? -15 : skillId == "open_locks" ? -10 : skillId == "find_remove_traps" ? -10 : skillId == "move_silently" ? -20 : -10,
            10 => skillId == "pick_pockets" ? -10 : skillId == "open_locks" ? -5 : skillId == "find_remove_traps" ? -10 : skillId == "move_silently" ? -15 : -5,
            11 => skillId == "pick_pockets" ? -5 : skillId == "open_locks" ? 0 : skillId == "find_remove_traps" ? -5 : skillId == "move_silently" ? -10 : 0,
            12 => skillId == "move_silently" ? -5 : 0,
            <= 15 => 0,
            16 => skillId == "open_locks" ? 5 : 0,
            17 => skillId == "pick_pockets" ? 5 : skillId == "open_locks" ? 10 : skillId == "move_silently" ? 5 : skillId == "hide_in_shadows" ? 5 : 0,
            18 => skillId == "pick_pockets" ? 10 : skillId == "open_locks" ? 15 : skillId == "find_remove_traps" ? 5 : skillId == "move_silently" ? 10 : 10,
            _ => skillId == "pick_pockets" ? 15 : skillId == "open_locks" ? 20 : skillId == "find_remove_traps" ? 10 : skillId == "move_silently" ? 15 : 15,
        };
    }

    public static int GetRogueSkillArmorAdjustment(string skillId, string? armorProfile)
    {
        var key = string.IsNullOrWhiteSpace(armorProfile) ? "no_armor" : armorProfile.Trim().ToLowerInvariant();
        if (RogueSkillArmorAdjustments.TryGetValue(key, out var table)
            && table.TryGetValue(skillId, out var value))
            return value;
        return 0;
    }

    public static List<RogueSkillBreakdown> BuildRogueSkillBreakdown(
        IEnumerable<string> selectedSkillIds,
        string? raceId,
        int dexterity,
        string? armorProfile,
        Dictionary<string, int>? allocatedPoints,
        int level)
    {
        var skillIds = selectedSkillIds?.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            ?? new List<string>();
        var allocations = allocatedPoints ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int perSkillCap = GetRogueSkillPerSkillAllocationCap(level);

        var result = new List<RogueSkillBreakdown>();
        foreach (var skillId in skillIds)
        {
            int baseScore = GetRogueSkillBaseScore(skillId);
            int racial = GetRogueSkillRacialAdjustment(skillId, raceId);
            int dex = GetRogueSkillDexterityAdjustment(skillId, dexterity);
            int armor = GetRogueSkillArmorAdjustment(skillId, armorProfile);
            int allocated = allocations.TryGetValue(skillId, out var points) ? Math.Clamp(points, 0, perSkillCap) : 0;
            int final = Math.Clamp(baseScore + racial + dex + armor + allocated, 0, 95);

            result.Add(new RogueSkillBreakdown(
                skillId,
                GetRogueSkillName(skillId),
                baseScore,
                racial,
                dex,
                armor,
                allocated,
                final));
        }

        return result.OrderBy(r => r.SkillName).ToList();
    }

    private static string NormalizeRogueRaceId(string? raceId)
    {
        if (string.IsNullOrWhiteSpace(raceId)) return string.Empty;
        var id = raceId.Trim().ToLowerInvariant();

        if (id.Contains("half") && id.Contains("elf")) return "half_elf";
        if (id.Contains("halfling")) return "halfling";
        if (id.Contains("dwarf")) return "dwarf";
        if (id.Contains("gnome")) return "gnome";
        if (id.Contains("elf")) return "elf";
        return id;
    }

    // ── Spell-access helpers (for future spell records) ─────────────────────

    public static readonly string[] StandardWizardSchools =
    {
        "Abjuration", "Alteration", "Conjuration/Summoning", "Divination",
        "Greater Divination", "Enchantment/Charm", "Illusion", "Invocation/Evocation", "Necromancy"
    };

    public static readonly string[] StandardPriestSpheres =
    {
        "All", "Animal", "Astral", "Chaos", "Charm", "Combat", "Creation", "Divination",
        "Elemental", "Evil", "Good", "Guardian", "Healing", "Knowledge", "Law",
        "Life", "Magic", "Necromantic", "Numbers", "Plant", "Protection", "Spells", "Summoning", "Sun",
        "Thought", "Time", "Travelers", "War", "Wards", "Weather"
    };

    /// <summary>
    /// Returns true if a wizard can learn/cast a spell from <paramref name="schoolName"/>
    /// based on purchased school selections and optional specialist opposition schools.
    /// </summary>
    public bool WizardHasSchoolAccess(
        string schoolName,
        Dictionary<string, bool>? selectedWizardSchools,
        string? wizardSpecializationId = null)
    {
        if (string.IsNullOrWhiteSpace(schoolName)) return false;
        if (selectedWizardSchools is null || selectedWizardSchools.Count == 0) return false;

        if (!string.IsNullOrWhiteSpace(wizardSpecializationId)
            && Classes.TryGetValue("wizard", out var wizardClass)
            && wizardClass.Specializations is { Count: > 0 } specs)
        {
            var spec = specs.FirstOrDefault(s =>
                string.Equals(s.Id, wizardSpecializationId, StringComparison.OrdinalIgnoreCase));
            if (spec?.OppositionSchools?.Any(s =>
                string.Equals(s, schoolName, StringComparison.OrdinalIgnoreCase)) == true)
                return false;
        }

        return selectedWizardSchools.Any(kv =>
            kv.Value && string.Equals(kv.Key, schoolName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true if a priest has at least the requested sphere access.
    /// Access tiers: none=0, minor=1, major/both=2.
    /// </summary>
    public static bool PriestHasSphereAccess(
        string sphereName,
        Dictionary<string, string>? selectedSpheres,
        string requiredAccess = "minor")
    {
        if (string.IsNullOrWhiteSpace(sphereName)) return false;
        if (selectedSpheres is null || selectedSpheres.Count == 0) return false;

        int neededRank = AccessRank(requiredAccess);
        int sphereRank = SelectedSphereRank(selectedSpheres, sphereName);
        int allRank = SelectedSphereRank(selectedSpheres, "All");

        return Math.Max(sphereRank, allRank) >= neededRank;
    }

    private static int SelectedSphereRank(Dictionary<string, string> selectedSpheres, string sphereName)
    {
        foreach (var (name, access) in selectedSpheres)
        {
            if (string.Equals(name, sphereName, StringComparison.OrdinalIgnoreCase))
                return AccessRank(access);
        }
        return 0;
    }

    private static int AccessRank(string? access) =>
        (access ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "minor" => 1,
            "major" => 2,
            "both" => 2,
            _ => 0,
        };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string AbilityName(string key) => key.ToUpper() switch
    {
        "STR" => "Strength",
        "DEX" => "Dexterity",
        "CON" => "Constitution",
        "INT" => "Intelligence",
        "WIS" => "Wisdom",
        "CHA" => "Charisma",
        _ => key
    };

    public static string AbilityLabel(string key) => AbilityName(key);

    private static int BaseHitPoints(string classId) => classId switch
    {
        "fighter" or "ranger" or "paladin" => 10,
        "cleric" or "druid" or "thief" => 8,
        "wizard" => 4,
        "bard" or "psionicist" => 6,
        _ => 6,
    };

    private static int ConModifier(int con) => con switch
    {
        <= 6 => -2,
        <= 8 => -1,
        <= 14 => 0,
        <= 16 => 1,
        _ => 2,
    };

    private static int GetDexterityAcAdjustment(int dexterity) => dexterity switch
    {
        <= 3 => 3,
        <= 5 => 2,
        <= 7 => 1,
        <= 14 => 0,
        15 => -1,
        16 => -2,
        17 => -3,
        _ => -4,
    };
}
