using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;
using UglyToad.PdfPig;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public class CharacterOptionCatalogService
{
    private CharacterOptionCatalog? _catalog;
    private static readonly string DiagnosticsPath = Path.Combine(Path.GetTempPath(), "DMC_CharacterOptions_Load.txt");
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DungeonMasterCortex");
    private static readonly string NwpSettingsPath = Path.Combine(SettingsDirectory, "nwp_settings.json");
    private static readonly string CustomNwpsPath   = Path.Combine(SettingsDirectory, "custom_nwps.json");
    private static readonly string CustomTraitsPath = Path.Combine(SettingsDirectory, "custom_traits.json");
    private static readonly string CustomDisadvantagesPath = Path.Combine(SettingsDirectory, "custom_disadvantages.json");
    private Dictionary<string, NonweaponProficiencySetting>? _nwpSettings;
    private Dictionary<string, CustomNwpData>? _customNwps;
    private HashSet<string>? _legacyCustomNwpIdsMissingAllowedClasses;
    private Dictionary<string, CustomTraitData>? _customTraits;
    private Dictionary<string, CustomDisadvantageData>? _customDisadvantages;

    public CharacterOptionCatalog GetCatalog() => _catalog ??= LoadCatalog();

    private CharacterOptionCatalog LoadCatalog()
    {
        var nonweaponProficiencies = ApplyNwpSettings(LoadNonweaponProficiencies());
        var traits = LoadTraits();
        var disadvantages = LoadDisadvantages();

        WriteDiagnostics(
            $"Loaded character options at {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Assets directory: {FindAssetsDirectory() ?? "(not found)"}",
            $"Skills & Powers dir: {FindSkillsAndPowersDirectory() ?? "(not found)"}",
            $"NWP master import: {FindMasterImportXlsxPath() ?? "(not found)"}",
            $"NWP docx: {FindNonweaponProficiencyDocxPath() ?? "(not found)"}",
            $"NWP pdf:  {FindNonweaponProficiencyPdfPath() ?? "(not found)"}",
            $"Nonweapon proficiencies: {nonweaponProficiencies.Count}",
            $"Traits: {traits.Count}",
            $"Disadvantages: {disadvantages.Count}");

        return new CharacterOptionCatalog(nonweaponProficiencies, traits, disadvantages);
    }

    public IReadOnlyDictionary<string, NonweaponProficiencySetting> GetNonweaponProficiencySettings()
        => new Dictionary<string, NonweaponProficiencySetting>(LoadNwpSettings(), StringComparer.OrdinalIgnoreCase);

    public void SaveNonweaponProficiencySettings(IEnumerable<NonweaponProficiencySetting> settings)
    {
        Directory.CreateDirectory(SettingsDirectory);

        var map = settings
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        var json = JsonSerializer.Serialize(map.Values.OrderBy(s => s.Id).ToList(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(NwpSettingsPath, json);
        _nwpSettings = map;
        _catalog = null;
    }

    public void InvalidateCache()
    {
        _catalog = null;
    }

    // ── Diagnostics / Report APIs ─────────────────────────────────────────────

    /// <summary>
    /// Returns pairs of (DocxName, DocxCategory, PoName, PoCategory) where the PO entry
    /// was matched to a DOCX entry via normalized-name fallback (exact names differ).
    /// These are candidates the user can review and align in the DM editor.
    /// </summary>
    public IReadOnlyList<(string DocxName, string DocxCategory, string PoName, string PoCategory)> GetCrossoverCandidates()
    {
        var legacy = LoadLegacyNonweaponProficiencies();
        var po = LoadPlayersOptionNonweaponProficiencies();

        var docxByNorm = legacy
            .GroupBy(x => NormalizeNwpNameForMerge(x.Name), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var results = new List<(string, string, string, string)>();
        foreach (var nwp in po)
        {
            // Skip exact-name matches (clean crossovers, not candidates)
            bool exactMatch = legacy.Any(x =>
                string.Equals(x.Name, nwp.Name, StringComparison.OrdinalIgnoreCase));
            if (exactMatch)
                continue;

            // Report entries where normalized name matches but exact name differs
            string normalized = NormalizeNwpNameForMerge(nwp.Name);
            if (docxByNorm.TryGetValue(normalized, out var docxMatch))
                results.Add((docxMatch.Name, docxMatch.Category, nwp.Name, nwp.Category));
        }
        return results.OrderBy(x => x.Item3).ToList();
    }

    // ── Custom NWP API ────────────────────────────────────────────────────────

    public IReadOnlyDictionary<string, CustomNwpData> GetCustomNwps()
        => new Dictionary<string, CustomNwpData>(LoadCustomNwps(), StringComparer.OrdinalIgnoreCase);

    public void SaveCustomNwp(CustomNwpData data)
    {
        if (string.IsNullOrWhiteSpace(data.Id))
            throw new ArgumentException("Custom NWP must have a non-empty Id.", nameof(data));

        Directory.CreateDirectory(SettingsDirectory);
        var map = LoadCustomNwps();
        map[data.Id] = data;
        PersistCustomNwps(map);
    }

    public void DeleteCustomNwp(string id)
    {
        var map = LoadCustomNwps();
        if (map.Remove(id))
            PersistCustomNwps(map);
    }

    private void PersistCustomNwps(Dictionary<string, CustomNwpData> map)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(
            map.Values.OrderBy(x => x.Name).ToList(),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(CustomNwpsPath, json);
        _customNwps = map;
        _catalog = null;
    }

    private Dictionary<string, CustomNwpData> LoadCustomNwps()
    {
        if (_customNwps is not null)
            return _customNwps;
        try
        {
            if (!File.Exists(CustomNwpsPath))
                return _customNwps = new Dictionary<string, CustomNwpData>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(CustomNwpsPath);
            var list = JsonSerializer.Deserialize<List<CustomNwpData>>(json) ?? new List<CustomNwpData>();
            _legacyCustomNwpIdsMissingAllowedClasses = DetectLegacyCustomNwpsMissingAllowedClasses(json);

            // Backward-compat: older custom_nwps.json only had one modifier field.
            // For PO sources, treat legacy CheckModifier as PO base rating if the new field is unset.
            foreach (var item in list)
            {
                bool isPoSource = item.Source.Contains("Player's Option", StringComparison.OrdinalIgnoreCase);
                if (isPoSource && item.PlayersOptionBaseRating == 0 && item.CheckModifier != 0)
                    item.PlayersOptionBaseRating = item.CheckModifier;
            }

            _customNwps = list
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _customNwps = new Dictionary<string, CustomNwpData>(StringComparer.OrdinalIgnoreCase);
            _legacyCustomNwpIdsMissingAllowedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        return _customNwps;
    }

    private static HashSet<string> DetectLegacyCustomNwpsMissingAllowedClasses(string json)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return ids;

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                if (!item.TryGetProperty("id", out var idElement))
                    continue;

                string? id = idElement.GetString();
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (!item.TryGetProperty("AllowedClasses", out _)
                    && !item.TryGetProperty("allowedClasses", out _))
                {
                    ids.Add(id);
                }
            }
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return ids;
    }

    // ── Custom Traits API ────────────────────────────────────────────────────

    public IReadOnlyDictionary<string, CustomTraitData> GetCustomTraits()
        => new Dictionary<string, CustomTraitData>(LoadCustomTraits(), StringComparer.OrdinalIgnoreCase);

    public void SaveCustomTrait(CustomTraitData data)
    {
        if (string.IsNullOrWhiteSpace(data.Id))
            throw new ArgumentException("Custom trait must have a non-empty Id.", nameof(data));

        Directory.CreateDirectory(SettingsDirectory);
        var map = LoadCustomTraits();
        map[data.Id] = data;
        PersistCustomTraits(map);
    }

    public void DeleteCustomTrait(string id)
    {
        var map = LoadCustomTraits();
        if (map.Remove(id))
            PersistCustomTraits(map);
    }

    private void PersistCustomTraits(Dictionary<string, CustomTraitData> map)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(
            map.Values.OrderBy(x => x.Name).ToList(),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(CustomTraitsPath, json);
        _customTraits = map;
        _catalog = null;
    }

    private Dictionary<string, CustomTraitData> LoadCustomTraits()
    {
        if (_customTraits is not null)
            return _customTraits;
        try
        {
            if (!File.Exists(CustomTraitsPath))
                return _customTraits = new Dictionary<string, CustomTraitData>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(CustomTraitsPath);
            var list = JsonSerializer.Deserialize<List<CustomTraitData>>(json) ?? new List<CustomTraitData>();
            _customTraits = list
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _customTraits = new Dictionary<string, CustomTraitData>(StringComparer.OrdinalIgnoreCase);
        }
        return _customTraits;
    }

    // ── Custom Disadvantages API ─────────────────────────────────────────────

    public IReadOnlyDictionary<string, CustomDisadvantageData> GetCustomDisadvantages()
        => new Dictionary<string, CustomDisadvantageData>(LoadCustomDisadvantages(), StringComparer.OrdinalIgnoreCase);

    public void SaveCustomDisadvantage(CustomDisadvantageData data)
    {
        if (string.IsNullOrWhiteSpace(data.Id))
            throw new ArgumentException("Custom disadvantage must have a non-empty Id.", nameof(data));

        Directory.CreateDirectory(SettingsDirectory);
        var map = LoadCustomDisadvantages();
        map[data.Id] = data;
        PersistCustomDisadvantages(map);
    }

    public void DeleteCustomDisadvantage(string id)
    {
        var map = LoadCustomDisadvantages();
        if (map.Remove(id))
            PersistCustomDisadvantages(map);
    }

    private void PersistCustomDisadvantages(Dictionary<string, CustomDisadvantageData> map)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(
            map.Values.OrderBy(x => x.Name).ToList(),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(CustomDisadvantagesPath, json);
        _customDisadvantages = map;
        _catalog = null;
    }

    private Dictionary<string, CustomDisadvantageData> LoadCustomDisadvantages()
    {
        if (_customDisadvantages is not null)
            return _customDisadvantages;
        try
        {
            if (!File.Exists(CustomDisadvantagesPath))
                return _customDisadvantages = new Dictionary<string, CustomDisadvantageData>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(CustomDisadvantagesPath);
            var list = JsonSerializer.Deserialize<List<CustomDisadvantageData>>(json) ?? new List<CustomDisadvantageData>();
            _customDisadvantages = list
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _customDisadvantages = new Dictionary<string, CustomDisadvantageData>(StringComparer.OrdinalIgnoreCase);
        }
        return _customDisadvantages;
    }

    private IReadOnlyList<NonweaponProficiencyDefinition> LoadNonweaponProficiencies()
    {
        var imported = LoadMasterImportNonweaponProficiencies();
        if (imported.Count > 0)
        {
            var mergedImported = new Dictionary<string, NonweaponProficiencyDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var nwp in imported)
                mergedImported[$"{nwp.Category}|{nwp.Name}"] = nwp;

            var idToKeyImported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in mergedImported)
                idToKeyImported[kv.Value.Id] = kv.Key;

            var customImportedMap = LoadCustomNwps();
            foreach (var custom in customImportedMap.Values)
            {
                if (idToKeyImported.TryGetValue(custom.Id, out string? oldKey))
                    mergedImported.Remove(oldKey);

                var def = new NonweaponProficiencyDefinition(
                    custom.Id, custom.Name, custom.Category, custom.Slots,
                    custom.CheckAbility, custom.CheckModifier, custom.Description, custom.Source,
                    CpCost: custom.CpCost,
                    PlayersOptionBaseRating: custom.PlayersOptionBaseRating,
                    PlayersOptionCheckAbility: custom.PlayersOptionCheckAbility,
                    AllowedClasses: custom.AllowedClasses.Count > 0 ? (IReadOnlyList<string>)custom.AllowedClasses : null,
                    GroupFamily: custom.GroupFamily,
                    ProficiencyGroup: custom.ProficiencyGroup,
                    IsMultiGroup: custom.IsMultiGroup,
                    GroupsFound: custom.GroupsFound,
                    PlayersOptionRaw: custom.PlayersOptionRaw,
                    SourceBook: custom.SourceBook,
                    SourceTag: custom.SourceTag,
                    SettingName: custom.SettingName,
                    Origin: custom.Origin,
                    DescriptionPreview: custom.DescriptionPreview,
                    HasDescription: custom.HasDescription);
                mergedImported[$"{custom.Category}|{custom.Name}"] = def;
            }

            return mergedImported.Values
                .OrderBy(x => x.Category)
                .ThenBy(x => x.Name)
                .ToList();
        }

        var playersOption = LoadPlayersOptionNonweaponProficiencies();
        var legacy = LoadLegacyNonweaponProficiencies();
        var supplement = LoadPdfNonweaponProficiencies();

        // Merge: detect Core/PO crossover by Category|Name and combine fields.
        // For crossover entries keep core-facing fields from legacy and PO-facing fields from PO.
        // Custom overrides still have highest priority.
        var merged = new Dictionary<string, NonweaponProficiencyDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var nwp in legacy)
            merged[$"{nwp.Category}|{nwp.Name}"] = nwp;

        var normalizedNameToKeys = merged
            .GroupBy(kv => NormalizeNwpNameForMerge(kv.Value.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Key).ToList(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var nwp in playersOption)
        {
            string key = $"{nwp.Category}|{nwp.Name}";
            string? matchKey = null;

            if (merged.ContainsKey(key))
            {
                matchKey = key;
            }
            else
            {
                string normalized = NormalizeNwpNameForMerge(nwp.Name);
                if (normalizedNameToKeys.TryGetValue(normalized, out var possibleKeys)
                    && possibleKeys.Count == 1)
                {
                    // Fallback merge for source name variants like
                    // "Blindfighting" vs "Blind-Fighting".
                    matchKey = possibleKeys[0];
                }
            }

            if (matchKey is not null && merged.TryGetValue(matchKey, out var existing))
            {
                merged[matchKey] = existing with
                {
                    // Preserve current identity for continuity.
                    Id = nwp.Id,
                    Source = "Core & Player's Option",
                    // PO-specific fields come from PO parse.
                    CpCost = nwp.CpCost,
                    PlayersOptionBaseRating = nwp.PlayersOptionBaseRating,
                    PlayersOptionCheckAbility = string.IsNullOrWhiteSpace(nwp.PlayersOptionCheckAbility)
                        ? existing.PlayersOptionCheckAbility
                        : nwp.PlayersOptionCheckAbility,
                    // Prefer PO description if core text is empty.
                    Description = string.IsNullOrWhiteSpace(existing.Description) ? nwp.Description : existing.Description,
                    AllowedClasses = UnionAllowedClasses(existing.AllowedClasses, nwp.AllowedClasses),
                };
                continue;
            }
            merged[key] = nwp;

            string addedNorm = NormalizeNwpNameForMerge(nwp.Name);
            if (!normalizedNameToKeys.TryGetValue(addedNorm, out var keys))
            {
                keys = new List<string>();
                normalizedNameToKeys[addedNorm] = keys;
            }
            keys.Add(key);
        }

        // Supplement PDF layer: add entries not already present by name.
        foreach (var nwp in supplement)
        {
            string key = $"{nwp.Category}|{nwp.Name}";
            if (merged.ContainsKey(key))
                continue;
            string normalized = NormalizeNwpNameForMerge(nwp.Name);
            if (normalizedNameToKeys.ContainsKey(normalized))
                continue; // already covered by DOCX or PO
            merged[key] = nwp;
            if (!normalizedNameToKeys.TryGetValue(normalized, out var suppKeys))
            {
                suppKeys = new List<string>();
                normalizedNameToKeys[normalized] = suppKeys;
            }
            suppKeys.Add(key);
        }

        // Build id→key map so custom can cleanly replace a parsed entry
        var idToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in merged)
            idToKey[kv.Value.Id] = kv.Key;

        var customMap = LoadCustomNwps();
        bool backfilledLegacyAllowedClasses = false;
        var legacyMissingAllowedClasses = _legacyCustomNwpIdsMissingAllowedClasses
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var custom in customMap.Values)
        {
            if (!legacyMissingAllowedClasses.Contains(custom.Id) || custom.AllowedClasses.Count > 0)
                continue;

            if (!idToKey.TryGetValue(custom.Id, out string? parsedKey))
                continue;
            if (!merged.TryGetValue(parsedKey, out var parsedDefinition))
                continue;
            if (parsedDefinition.AllowedClasses is not { Count: > 0 })
                continue;

            custom.AllowedClasses = parsedDefinition.AllowedClasses.ToList();
            backfilledLegacyAllowedClasses = true;
        }

        if (backfilledLegacyAllowedClasses)
        {
            PersistCustomNwps(customMap);
            _legacyCustomNwpIdsMissingAllowedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var custom in customMap.Values)
        {
            // Remove any parsed entry that has this id before inserting updated definition
            if (idToKey.TryGetValue(custom.Id, out string? oldKey))
                merged.Remove(oldKey);

            var def = new NonweaponProficiencyDefinition(
                custom.Id, custom.Name, custom.Category, custom.Slots,
                custom.CheckAbility, custom.CheckModifier, custom.Description, custom.Source,
                CpCost: custom.CpCost,
                PlayersOptionBaseRating: custom.PlayersOptionBaseRating,
                PlayersOptionCheckAbility: custom.PlayersOptionCheckAbility,
                AllowedClasses: custom.AllowedClasses.Count > 0 ? (IReadOnlyList<string>)custom.AllowedClasses : null,
                GroupFamily: custom.GroupFamily,
                ProficiencyGroup: custom.ProficiencyGroup,
                IsMultiGroup: custom.IsMultiGroup,
                GroupsFound: custom.GroupsFound,
                PlayersOptionRaw: custom.PlayersOptionRaw,
                SourceBook: custom.SourceBook,
                SourceTag: custom.SourceTag,
                SettingName: custom.SettingName,
                Origin: custom.Origin,
                DescriptionPreview: custom.DescriptionPreview,
                HasDescription: custom.HasDescription);
            merged[$"{custom.Category}|{custom.Name}"] = def;
        }

        return merged.Values
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ToList();
    }

    private static IReadOnlyList<string>? UnionAllowedClasses(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        var values = (left ?? Array.Empty<string>())
            .Concat(right ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return values.Count == 0 ? null : values;
    }

    private static NonweaponProficiencyDefinition ConsolidateDuplicateNwps(IGrouping<string, NonweaponProficiencyDefinition> group)
    {
        var chosen = group
            .OrderByDescending(x => GetSourcePriority(x.Source))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .First();

        return chosen with
        {
            AllowedClasses = group
                .Select(x => x.AllowedClasses)
                .Aggregate((IReadOnlyList<string>?)null, UnionAllowedClasses)
        };
    }

    private static string NormalizeNwpNameForMerge(string name)
        => Regex.Replace(name ?? string.Empty, "[^a-z0-9]", string.Empty, RegexOptions.IgnoreCase)
            .ToLowerInvariant();

    private static IReadOnlyList<NonweaponProficiencyDefinition> LoadMasterImportNonweaponProficiencies()
    {
        string? xlsxPath = FindMasterImportXlsxPath();
        if (xlsxPath is null || !File.Exists(xlsxPath))
            return Array.Empty<NonweaponProficiencyDefinition>();

        try
        {
            using var archive = ZipFile.OpenRead(xlsxPath);
            var workbook = XDocument.Parse(ReadZipEntryText(archive, "xl/workbook.xml"));
            var rels = XDocument.Parse(ReadZipEntryText(archive, "xl/_rels/workbook.xml.rels"));
            var sharedStrings = LoadSharedStrings(archive);

            XNamespace mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace pkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var sheets = workbook.Root?
                .Element(mainNs + "sheets")?
                .Elements(mainNs + "sheet")
                .ToList() ?? new List<XElement>();
            var relationships = rels.Root?
                .Elements(pkgRelNs + "Relationship")
                .ToList() ?? new List<XElement>();

            var masterSheet = sheets.FirstOrDefault(sheet =>
                string.Equals((string?)sheet.Attribute("name"), "Master NWPs", StringComparison.OrdinalIgnoreCase))
                ?? sheets.FirstOrDefault();
            if (masterSheet is null)
                return Array.Empty<NonweaponProficiencyDefinition>();

            string? rid = (string?)masterSheet.Attribute(relNs + "id");
            string? target = relationships.FirstOrDefault(r => string.Equals((string?)r.Attribute("Id"), rid, StringComparison.OrdinalIgnoreCase))
                ?.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
                return Array.Empty<NonweaponProficiencyDefinition>();

            string sheetPath = target.TrimStart('/');
            var worksheet = XDocument.Parse(ReadZipEntryText(archive, sheetPath));
            var rows = worksheet.Root?
                .Element(mainNs + "sheetData")?
                .Elements(mainNs + "row")
                .ToList() ?? new List<XElement>();
            if (rows.Count == 0)
                return Array.Empty<NonweaponProficiencyDefinition>();

            var headerMap = ParseWorksheetRow(rows[0], sharedStrings)
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value.Trim());
            if (headerMap.Count == 0)
                return Array.Empty<NonweaponProficiencyDefinition>();

            var results = new List<NonweaponProficiencyDefinition>();
            foreach (var row in rows.Skip(1))
            {
                var valuesByIndex = ParseWorksheetRow(row, sharedStrings);
                string Get(string header)
                {
                    var pair = headerMap.FirstOrDefault(kv => string.Equals(kv.Value, header, StringComparison.OrdinalIgnoreCase));
                    return pair.Equals(default(KeyValuePair<int, string>)) ? string.Empty : valuesByIndex.GetValueOrDefault(pair.Key, string.Empty);
                }

                string name = NormalizeWhitespace(Get("Proficiency"));
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                string groupFamily = NormalizeWhitespace(Get("Group Family"));
                string category = NormalizeImportedCategory(groupFamily);
                int slots = ParseImportInt(Get("Core Slots"), 1);
                string coreAbility = NormalizeImportedAbility(Get("Core Ability"));
                int coreModifier = ParseImportInt(Get("Core Modifier"), 0);
                int poCp = ParseImportInt(Get("PO CP"), 0);
                int poInitialRating = ParseImportInt(Get("PO Initial Rating"), 0);
                string poCheckAbility = NormalizeImportedPoCheckAbility(Get("PO Ability/Subability"));
                var allowedClasses = BuildImportedAllowedClasses(headerMap, valuesByIndex);
                string description = NormalizeWhitespace(Get("Description"));
                string descriptionPreview = NormalizeWhitespace(Get("Description Preview"));
                bool hasDescription = ParseImportBool(Get("Has Description"));
                string sourceBook = NormalizeWhitespace(Get("Source Book"));
                string sourceTag = NormalizeWhitespace(Get("Source Tag"));
                string settingName = NormalizeWhitespace(Get("Setting"));
                string origin = NormalizeWhitespace(Get("Origin"));
                string proficiencyGroup = NormalizeWhitespace(Get("Proficiency Group"));
                bool isMultiGroup = ParseImportBool(Get("Multi-Group?"));
                string groupsFound = NormalizeWhitespace(Get("Groups Found"));
                string poRaw = NormalizeWhitespace(Get("PO Raw"));

                string source = DetermineImportedSource(slots, poCp, poInitialRating, sourceTag, sourceBook);

                results.Add(new NonweaponProficiencyDefinition(
                    Slugify($"nwp_{name}"),
                    name,
                    category,
                    slots,
                    string.IsNullOrWhiteSpace(coreAbility) ? "Intelligence" : coreAbility,
                    coreModifier,
                    string.IsNullOrWhiteSpace(description) ? descriptionPreview : description,
                    source,
                    CpCost: poCp,
                    PlayersOptionBaseRating: poInitialRating,
                    PlayersOptionCheckAbility: poCheckAbility,
                    AllowedClasses: allowedClasses,
                    GroupFamily: groupFamily,
                    ProficiencyGroup: proficiencyGroup,
                    IsMultiGroup: isMultiGroup,
                    GroupsFound: groupsFound,
                    PlayersOptionRaw: poRaw,
                    SourceBook: sourceBook,
                    SourceTag: sourceTag,
                    SettingName: settingName,
                    Origin: origin,
                    DescriptionPreview: descriptionPreview,
                    HasDescription: hasDescription));
            }

            return results
                .GroupBy(x => NormalizeNwpNameForMerge(x.Name), StringComparer.OrdinalIgnoreCase)
                .Select(ConsolidateImportedNwps)
                .OrderBy(x => x.Category)
                .ThenBy(x => x.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            WriteDiagnostics($"Master import load failed: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<NonweaponProficiencyDefinition>();
        }
    }

    private static NonweaponProficiencyDefinition ConsolidateImportedNwps(IGrouping<string, NonweaponProficiencyDefinition> group)
    {
        var chosen = group
            .OrderByDescending(x => x.HasDescription)
            .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.Description))
            .ThenByDescending(x => !string.IsNullOrWhiteSpace(x.DescriptionPreview))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .First();

        string CombineDistinct(Func<NonweaponProficiencyDefinition, string> selector)
            => string.Join(", ", group.Select(selector)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        return chosen with
        {
            AllowedClasses = group.Select(x => x.AllowedClasses).Aggregate((IReadOnlyList<string>?)null, UnionAllowedClasses),
            GroupFamily = string.IsNullOrWhiteSpace(chosen.GroupFamily) ? group.Select(x => x.GroupFamily).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty : chosen.GroupFamily,
            ProficiencyGroup = string.IsNullOrWhiteSpace(chosen.ProficiencyGroup) ? group.Select(x => x.ProficiencyGroup).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty : chosen.ProficiencyGroup,
            IsMultiGroup = group.Any(x => x.IsMultiGroup),
            GroupsFound = CombineDistinct(x => x.GroupsFound),
            PlayersOptionRaw = string.IsNullOrWhiteSpace(chosen.PlayersOptionRaw) ? group.Select(x => x.PlayersOptionRaw).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty : chosen.PlayersOptionRaw,
            PlayersOptionCheckAbility = string.IsNullOrWhiteSpace(chosen.PlayersOptionCheckAbility)
                ? group.Select(x => x.PlayersOptionCheckAbility).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty
                : chosen.PlayersOptionCheckAbility,
            SourceBook = CombineDistinct(x => x.SourceBook),
            SourceTag = CombineDistinct(x => x.SourceTag),
            SettingName = CombineDistinct(x => x.SettingName),
            Origin = CombineDistinct(x => x.Origin),
            DescriptionPreview = string.IsNullOrWhiteSpace(chosen.DescriptionPreview) ? group.Select(x => x.DescriptionPreview).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty : chosen.DescriptionPreview,
            HasDescription = group.Any(x => x.HasDescription),
            Source = DetermineImportedSource(chosen.Slots, group.Max(x => x.CpCost), group.Max(x => x.PlayersOptionBaseRating), CombineDistinct(x => x.SourceTag), CombineDistinct(x => x.SourceBook)),
            CpCost = group.Max(x => x.CpCost),
            PlayersOptionBaseRating = group.Max(x => x.PlayersOptionBaseRating),
            Description = !string.IsNullOrWhiteSpace(chosen.Description) ? chosen.Description : group.Select(x => x.Description).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
        };
    }

    private static Dictionary<int, string> ParseWorksheetRow(XElement row, IReadOnlyList<string> sharedStrings)
    {
        XNamespace mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var values = new Dictionary<int, string>();
        foreach (var cell in row.Elements(mainNs + "c"))
        {
            string cellRef = (string?)cell.Attribute("r") ?? string.Empty;
            int index = ColumnNameToIndex(new string(cellRef.TakeWhile(char.IsLetter).ToArray()));
            if (index < 0)
                continue;

            string value = string.Empty;
            string type = (string?)cell.Attribute("t") ?? string.Empty;
            var valueElement = cell.Element(mainNs + "v");
            if (type == "inlineStr")
            {
                value = NormalizeWhitespace(string.Concat(cell.Descendants(mainNs + "t").Select(t => (string?)t ?? string.Empty)));
            }
            else if (type == "s" && valueElement is not null && int.TryParse(valueElement.Value, out int ssIndex) && ssIndex >= 0 && ssIndex < sharedStrings.Count)
            {
                value = sharedStrings[ssIndex];
            }
            else if (valueElement is not null)
            {
                value = NormalizeWhitespace(valueElement.Value);
            }

            values[index] = value;
        }

        return values;
    }

    private static IReadOnlyList<string> LoadSharedStrings(ZipArchive archive)
    {
        try
        {
            var sharedStringsDoc = XDocument.Parse(ReadZipEntryText(archive, "xl/sharedStrings.xml"));
            XNamespace mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var values = sharedStringsDoc.Root?
                .Elements(mainNs + "si")
                .Select(si => NormalizeWhitespace(string.Concat(si.Descendants(mainNs + "t").Select(t => (string?)t ?? string.Empty))))
                .ToList();
            return values ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string ReadZipEntryText(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName.TrimStart('/'))
            ?? throw new InvalidOperationException($"Missing archive entry: {entryName}");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static int ColumnNameToIndex(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            return -1;

        int value = 0;
        foreach (char ch in columnName.ToUpperInvariant())
        {
            if (ch is < 'A' or > 'Z')
                return -1;
            value = (value * 26) + (ch - 'A' + 1);
        }

        return value - 1;
    }

    private static string NormalizeImportedCategory(string groupFamily)
    {
        string value = NormalizeWhitespace(groupFamily);
        if (string.Equals(value, "Wizard", StringComparison.OrdinalIgnoreCase))
            return "Mage";
        return string.IsNullOrWhiteSpace(value) ? "General" : value;
    }

    private static string NormalizeImportedAbility(string raw)
    {
        string value = NormalizeWhitespace(raw)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace('–', '-')
            .Replace('—', '-');
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        if (Regex.IsMatch(value, @"\b(na|n/a|none|ability\s*special)\b", RegexOptions.IgnoreCase))
            return "None";
        if (Regex.IsMatch(value, @"\bstr(ength)?\b", RegexOptions.IgnoreCase)) return "Strength";
        if (Regex.IsMatch(value, @"\bdex(terity)?\b", RegexOptions.IgnoreCase)) return "Dexterity";
        if (Regex.IsMatch(value, @"\bcon(stitution)?\b", RegexOptions.IgnoreCase)) return "Constitution";
        if (Regex.IsMatch(value, @"\bint(elligence)?\b", RegexOptions.IgnoreCase)) return "Intelligence";
        if (Regex.IsMatch(value, @"\bwis(dom)?\b", RegexOptions.IgnoreCase)) return "Wisdom";
        if (Regex.IsMatch(value, @"\bcha(risma)?\b", RegexOptions.IgnoreCase)) return "Charisma";
        return value;
    }

    private static string NormalizeImportedPoCheckAbility(string raw)
    {
        string cleaned = NormalizeWhitespace(raw)
            .Replace('–', '-')
            .Replace('—', '-')
            .Replace("−", "-", StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;

        // Non-ability placeholders that appear in the source workbook.
        if (Regex.IsMatch(cleaned, @"^(na|n/?a|none|general|warrior|rogue|priest|warriors\s*,\s*rogues?)$", RegexOptions.IgnoreCase))
            return string.Empty;

        string compact = cleaned.ToLowerInvariant();

        static int MatchIndex(string text, string pattern)
        {
            var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
            return m.Success ? m.Index : int.MaxValue;
        }

        var subCandidates = new (string Key, string Pattern)[]
        {
            ("str_stamina", @"\bstamina\b|\bstam\b"),
            ("str_muscle", @"\bmuscle\b|\bmusc\b"),
            ("dex_aim", @"\baim\b"),
            ("dex_balance", @"\bbalance\b|\bbalan\b|\bbala\b"),
            ("con_health", @"\bhealth\b"),
            ("con_fitness", @"\bfitness\b|\bfit\b"),
            ("int_reason", @"\breason\b|\breas\b"),
            ("int_knowledge", @"\bknowledge\b|\bknowl\b"),
            ("wis_intuition", @"\bintuition\b|\bintuit\b"),
            ("wis_willpower", @"\bwillpower\b|\bwillp\b"),
            ("wis_perception", @"\bperception\b|\bpercep\b"),
            ("cha_leadership", @"\bleadership\b|\bleadersh\b|\bleaders\b|\blead\b"),
            ("cha_appearance", @"\bappearance\b|\bappear\b"),
        };

        string? firstSub = subCandidates
            .Select(c => (c.Key, Index: MatchIndex(compact, c.Pattern)))
            .Where(x => x.Index != int.MaxValue)
            .OrderBy(x => x.Index)
            .Select(x => x.Key)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstSub))
            return firstSub;

        var baseCandidates = new (string Key, string Pattern)[]
        {
            ("Strength", @"\bstr(ength)?\b"),
            ("Dexterity", @"\bdex(terity)?\b"),
            ("Constitution", @"\bcon(stitution)?\b"),
            ("Intelligence", @"\bint(elligence)?\b"),
            ("Wisdom", @"\bwis(dom)?\b"),
            ("Charisma", @"\bcha(risma)?\b"),
        };

        string? firstBase = baseCandidates
            .Select(c => (c.Key, Index: MatchIndex(compact, c.Pattern)))
            .Where(x => x.Index != int.MaxValue)
            .OrderBy(x => x.Index)
            .Select(x => x.Key)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(firstBase))
            return firstBase;

        return string.Empty;
    }

    private static int ParseImportInt(string raw, int defaultValue)
    {
        string value = NormalizeWhitespace(raw)
            .Replace('–', '-')
            .Replace('—', '-')
            .Replace("−", "-", StringComparison.Ordinal);
        var match = Regex.Match(value, @"-?\d+");
        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : defaultValue;
    }

    private static bool ParseImportBool(string raw)
    {
        string value = NormalizeWhitespace(raw);
        return string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string>? BuildImportedAllowedClasses(IReadOnlyDictionary<int, string> headerMap, IReadOnlyDictionary<int, string> valuesByIndex)
    {
        var classes = new List<string>();
        void AddIfTrue(string header, string classId)
        {
            var column = headerMap.FirstOrDefault(kv => string.Equals(kv.Value, header, StringComparison.OrdinalIgnoreCase));
            if (!column.Equals(default(KeyValuePair<int, string>)) && ParseImportBool(valuesByIndex.GetValueOrDefault(column.Key, string.Empty)))
                classes.Add(classId);
        }

        AddIfTrue("Fighter", "fighter");
        AddIfTrue("Paladin", "paladin");
        AddIfTrue("Ranger", "ranger");
        AddIfTrue("Cleric", "cleric");
        AddIfTrue("Druid", "druid");
        AddIfTrue("Thief", "thief");
        AddIfTrue("Bard", "bard");
        AddIfTrue("Wizard", "wizard");
        AddIfTrue("Psionicist", "psionicist");

        var distinct = classes.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 0 ? null : distinct;
    }

    private static string DetermineImportedSource(int slots, int poCp, int poInitialRating, string sourceTag, string sourceBook)
    {
        bool hasPo = poCp > 0 || poInitialRating > 0 || sourceTag.Contains("Player", StringComparison.OrdinalIgnoreCase) || sourceBook.Contains("Player", StringComparison.OrdinalIgnoreCase);
        bool hasCore = slots > 0 || sourceTag.Contains("Core", StringComparison.OrdinalIgnoreCase) || sourceBook.Contains("Complete", StringComparison.OrdinalIgnoreCase) || sourceBook.Contains("Core", StringComparison.OrdinalIgnoreCase);

        if (hasCore && hasPo)
            return "Core & Player's Option";
        if (hasPo)
            return "Player's Option";
        return "Core";
    }

    private IReadOnlyList<NonweaponProficiencyDefinition> ApplyNwpSettings(IReadOnlyList<NonweaponProficiencyDefinition> source)
    {
        var settings = LoadNwpSettings();
        return source
            .Select(nwp =>
            {
                if (!settings.TryGetValue(nwp.Id, out var s))
                    return nwp;

                return nwp with
                {
                    AllowMultiple = s.AllowMultiple,
                    RequiresPlayerText = s.RequiresPlayerText,
                };
            })
            .ToList();
    }

    private Dictionary<string, NonweaponProficiencySetting> LoadNwpSettings()
    {
        if (_nwpSettings is not null)
            return _nwpSettings;

        try
        {
            if (!File.Exists(NwpSettingsPath))
                return _nwpSettings = new Dictionary<string, NonweaponProficiencySetting>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(NwpSettingsPath);
            var list = JsonSerializer.Deserialize<List<NonweaponProficiencySetting>>(json) ?? new List<NonweaponProficiencySetting>();
            _nwpSettings = list
                .Where(s => !string.IsNullOrWhiteSpace(s.Id))
                .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _nwpSettings = new Dictionary<string, NonweaponProficiencySetting>(StringComparer.OrdinalIgnoreCase);
        }

        return _nwpSettings;
    }

    private static IReadOnlyList<NonweaponProficiencyDefinition> LoadLegacyNonweaponProficiencies()
    {
        var docxPath = FindNonweaponProficiencyDocxPath();
        if (docxPath is null)
            return Array.Empty<NonweaponProficiencyDefinition>();

        try
        {
            using var archive = ZipFile.OpenRead(docxPath);
            var entry = archive.GetEntry("word/document.xml");
            if (entry is null)
                return Array.Empty<NonweaponProficiencyDefinition>();

            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

            var paragraphs = document
                .Descendants(w + "p")
                .Select(p => new ParsedParagraph(
                    GetParagraphStyle(p, w),
                    NormalizeWhitespace(string.Concat(
                        p.Descendants(w + "t").Select(t => (string?)t ?? string.Empty)))))
                .Where(p => !string.IsNullOrWhiteSpace(p.Text))
                .ToList();

            var classAvailabilityByName = ExtractDocxNwpClassAvailabilityMap(document, w);
            var tableFallbackEntries = ExtractDocxTableFallbackNwps(document, w);

            var results = new List<NonweaponProficiencyDefinition>();
            string currentCategory = "General";
            IReadOnlyList<string>? currentAllowedClasses = InferAllowedClassesFromCategory(currentCategory);

            for (int i = 0; i < paragraphs.Count; i++)
            {
                var paragraph = paragraphs[i];
                if (IsCategoryHeading(paragraph))
                {
                    string? newCat = NormalizeCategory(paragraph.Text);
                    if (newCat != null)
                    {
                        currentCategory = newCat;
                        currentAllowedClasses = InferAllowedClassesFromHeading(paragraph.Text, currentCategory);
                    }
                    continue;
                }

                if (!IsEntryHeading(paragraph))
                    continue;

                string rawName = paragraph.Text.Trim().TrimEnd('.');
                if (!IsLikelyProficiencyName(rawName))
                    continue;

                string source = ExtractSource(ref rawName);
                string name = rawName;
                classAvailabilityByName.TryGetValue(NormalizeNwpNameForMerge(name), out var tableClasses);
                int summaryIndex = FindNextSummaryIndex(paragraphs, i + 1);
                if (summaryIndex < 0)
                    continue;

                if (!TryParseProficiencySummary(paragraphs[summaryIndex].Text,
                        out int slots,
                        out string ability,
                        out int modifier))
                {
                    continue;
                }

                var descriptionParts = new List<string>();
                for (int j = summaryIndex + 1; j < paragraphs.Count; j++)
                {
                    var next = paragraphs[j];
                    if (IsEntryHeading(next) || IsCategoryHeading(next))
                        break;
                    if (ShouldIgnoreParagraph(next.Text))
                        continue;
                    descriptionParts.Add(next.Text);
                }

                results.Add(new NonweaponProficiencyDefinition(
                    Slugify($"nwp_{name}"),
                    name,
                    currentCategory,
                    slots,
                    ToTitleCase(ability),
                    modifier,
                    string.Join(" ", descriptionParts),
                    source,
                    AllowedClasses: UnionAllowedClasses(currentAllowedClasses, tableClasses)));
            }

            // Add table-only entries that may not have a matching detailed heading block.
            var existingByNormalizedName = new HashSet<string>(
                results.Select(x => NormalizeNwpNameForMerge(x.Name)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var fallback in tableFallbackEntries)
            {
                string normalized = NormalizeNwpNameForMerge(fallback.Name);
                if (existingByNormalizedName.Contains(normalized))
                    continue;

                results.Add(fallback);
                existingByNormalizedName.Add(normalized);
            }

            return results
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(ConsolidateDuplicateNwps)
                .OrderBy(x => x.Category)
                .ThenBy(x => x.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            WriteDiagnostics($"NWP load failed: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<NonweaponProficiencyDefinition>();
        }
    }

    private static IReadOnlyList<NonweaponProficiencyDefinition> LoadPlayersOptionNonweaponProficiencies()
    {
        string? webHelpDir = FindSkillsAndPowersDirectory();
        if (webHelpDir is null)
            return Array.Empty<NonweaponProficiencyDefinition>();

        string? tablePath = FindFileIgnoreCase(webHelpDir, "DD03011.htm")
            ?? FindFileIgnoreCase(webHelpDir, "DD03011.HTM");
        if (tablePath is null || !File.Exists(tablePath))
            return Array.Empty<NonweaponProficiencyDefinition>();

        try
        {
            string html = File.ReadAllText(tablePath);
            var results = new List<NonweaponProficiencyDefinition>();
            var sections = ExtractPoNwpCategoryTables(html);

            if (sections.Count == 0)
            {
                // Fallback for HTML variants where category heading markup differs.
                sections = new List<(string Category, string TableHtml)>
                {
                    ("General", html)
                };
            }

            foreach (var section in sections)
            {
                var rows = ExtractTableRows(section.TableHtml).ToList();
                string currentCategory = section.Category;
                for (int i = 0; i < rows.Count; i++)
                {
                    string row = rows[i];
                    var cells = Regex.Matches(row, @"(?is)<TD[^>]*>(.*?)</TD>")
                        .Cast<Match>()
                        .Select(m => NormalizeWhitespace(HtmlToPlainText(m.Groups[1].Value)))
                        .ToList();

                    if (cells.Count == 0)
                        continue;

                    if (cells.Count == 1)
                    {
                        string? maybeCategory = NormalizePoCategory(cells[0]);
                        if (maybeCategory is not null)
                            currentCategory = maybeCategory;
                        continue;
                    }

                    if (cells[0].Contains("Proficiency", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var link = Regex.Match(row, @"(?is)<A\s+HREF=\""(?<href>[^\""#]+)(?:#[^\""\s]*)?\""[^>]*>(?<name>[^<]+)</A>");
                    if (!link.Success)
                        continue;

                    string name = NormalizeWhitespace(WebUtility.HtmlDecode(link.Groups["name"].Value));
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (!TryParsePoRating(cells.ElementAtOrDefault(2), out int initialRating))
                        continue;
                    if (!int.TryParse(cells.ElementAtOrDefault(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cost))
                        continue;

                    string abilityText = cells.ElementAtOrDefault(3) ?? string.Empty;
                    if ((string.IsNullOrWhiteSpace(abilityText) || abilityText.EndsWith(",", StringComparison.Ordinal)) && i + 1 < rows.Count)
                    {
                        var nextCells = Regex.Matches(rows[i + 1], @"(?is)<TD[^>]*>(.*?)</TD>")
                            .Cast<Match>()
                            .Select(m => NormalizeWhitespace(HtmlToPlainText(m.Groups[1].Value)))
                            .ToList();
                        if (nextCells.Count >= 4 && string.IsNullOrWhiteSpace(nextCells[0]))
                        {
                            string continuation = nextCells[3];
                            if (!string.IsNullOrWhiteSpace(continuation))
                                abilityText = string.IsNullOrWhiteSpace(abilityText)
                                    ? continuation
                                    : $"{abilityText} {continuation}";
                        }
                    }

                    if (string.IsNullOrWhiteSpace(abilityText))
                        continue;

                    string href = link.Groups["href"].Value;
                    string description = ExtractHtmlDescription(webHelpDir, href, name, "Nonweapon Proficiency Descriptions");

                    string categoryKey = string.IsNullOrWhiteSpace(currentCategory) ? "General" : currentCategory;
                    string idCategorySegment = categoryKey.Replace(" ", "_", StringComparison.Ordinal);

                    results.Add(new NonweaponProficiencyDefinition(
                        Slugify($"nwp_{idCategorySegment}_{name}"),
                        name,
                        categoryKey,
                        1,
                        abilityText,
                        0,
                        description,
                        "Player's Option",
                        CpCost: cost,
                        PlayersOptionBaseRating: initialRating,
                        PlayersOptionCheckAbility: abilityText,
                        AllowedClasses: InferAllowedClassesFromCategory(categoryKey)));
                }
            }

            return results
                .GroupBy(x => $"{x.Name}|{x.Category}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.Category)
                .ThenBy(x => x.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            WriteDiagnostics($"PO NWP load failed: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<NonweaponProficiencyDefinition>();
        }
    }

    private static bool TryParsePoRating(string? text, out int initialRating)
    {
        string value = NormalizeWhitespace(text ?? string.Empty)
            .Replace('–', '-')
            .Replace('—', '-');

        var match = Regex.Match(value, @"(?<rating>\d+)$");
        if (match.Success && int.TryParse(match.Groups["rating"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out initialRating))
            return true;

        initialRating = 0;
        return false;
    }

    private static string? NormalizePoCategory(string text)
    {
        string t = NormalizeWhitespace(text).ToLowerInvariant();
        if (t.Contains("general"))
            return "General";
        if (t.Contains("priest"))
            return "Priest";
        if (t.Contains("rogue"))
            return "Rogue";
        if (t.Contains("wizard"))
            return "Mage";
        if (t.Contains("warrior"))
            return "Warrior";
        return null;
    }

    private static IReadOnlyList<(string Category, string TableHtml)> ExtractPoNwpCategoryTables(string html)
    {
        var sections = new List<(string Category, string TableHtml)>();
        var labels = new (string Label, string Category)[]
        {
            ("GENERAL", "General"),
            ("PRIEST", "Priest"),
            ("ROGUE", "Rogue"),
            ("WIZARD", "Mage"),
            ("WARRIOR", "Warrior"),
        };

        foreach (var (label, category) in labels)
        {
            var headerMatch = Regex.Match(html,
                $@"(?is)<B[^>]*>\s*{Regex.Escape(label)}\b.*?</B>");
            if (!headerMatch.Success)
                continue;

            int tableStart = html.IndexOf("<TABLE", headerMatch.Index + headerMatch.Length, StringComparison.OrdinalIgnoreCase);
            if (tableStart < 0)
                continue;

            int tableClose = html.IndexOf("</TABLE>", tableStart, StringComparison.OrdinalIgnoreCase);
            if (tableClose < 0)
                continue;

            string tableHtml = html.Substring(tableStart, tableClose - tableStart + "</TABLE>".Length);
            sections.Add((category, tableHtml));
        }

        return sections;
    }

    private IReadOnlyList<TraitDefinition> LoadTraits()
    {
        string? webHelpDir = FindSkillsAndPowersDirectory();
        if (webHelpDir is null)
            return LoadCustomTraits().Values
                .Select(x => new TraitDefinition(x.Id, x.Name, x.Cost, x.Description))
                .OrderBy(x => x.Name)
                .ToList();

        string? tablePath = FindFileIgnoreCase(webHelpDir, "DD03012.htm");
        if (tablePath is null)
            tablePath = FindFileIgnoreCase(webHelpDir, "DD03012.HTM");
        if (!File.Exists(tablePath))
            return LoadCustomTraits().Values
                .Select(x => new TraitDefinition(x.Id, x.Name, x.Cost, x.Description))
                .OrderBy(x => x.Name)
                .ToList();

        string html = File.ReadAllText(tablePath);
        var rows = ExtractTableRows(html);
        var results = new List<TraitDefinition>();

        foreach (string row in rows)
        {
            var link = Regex.Match(row, @"<A HREF=""(?<href>[^""]+)"">(?<name>[^<]+)</A>", RegexOptions.IgnoreCase);
            var cost = Regex.Match(row, @">(?<cost>\d+)\s*<BR", RegexOptions.IgnoreCase);
            if (!link.Success || !cost.Success)
                continue;

            string name = WebUtility.HtmlDecode(link.Groups["name"].Value).Trim();
            if (string.Equals(name, "Trait", StringComparison.OrdinalIgnoreCase))
                continue;

            string description = ExtractHtmlDescription(webHelpDir, link.Groups["href"].Value, name, "Trait Descriptions");
            results.Add(new TraitDefinition(
                Slugify($"trait_{name}"),
                name,
                int.Parse(cost.Groups["cost"].Value, CultureInfo.InvariantCulture),
                description));
        }

        var merged = results
            .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);
        foreach (var custom in LoadCustomTraits().Values)
            merged[custom.Id] = new TraitDefinition(custom.Id, custom.Name, custom.Cost, custom.Description);
        return merged.Values.OrderBy(x => x.Name).ToList();
    }

    private IReadOnlyList<DisadvantageDefinition> LoadDisadvantages()
    {
        string? webHelpDir = FindSkillsAndPowersDirectory();
        if (webHelpDir is null)
            return LoadCustomDisadvantages().Values
                .Select(x => new DisadvantageDefinition(x.Id, x.Name, x.ModerateBonus, x.SevereBonus, x.Description))
                .OrderBy(x => x.Name)
                .ToList();

        string? tablePath = FindFileIgnoreCase(webHelpDir, "DD03017.htm");
        if (tablePath is null)
            tablePath = FindFileIgnoreCase(webHelpDir, "DD03017.HTM");
        if (!File.Exists(tablePath))
            return LoadCustomDisadvantages().Values
                .Select(x => new DisadvantageDefinition(x.Id, x.Name, x.ModerateBonus, x.SevereBonus, x.Description))
                .OrderBy(x => x.Name)
                .ToList();

        string html = File.ReadAllText(tablePath);
        var rows = ExtractTableRows(html);
        var results = new List<DisadvantageDefinition>();

        foreach (string row in rows)
        {
            var link = Regex.Match(row, @"<A HREF=""(?<href>[^""]+)"">(?<name>[^<]+)</A>", RegexOptions.IgnoreCase);
            if (!link.Success)
                continue;

            string name = WebUtility.HtmlDecode(link.Groups["name"].Value).Trim();
            if (string.Equals(name, "Disadvantage", StringComparison.OrdinalIgnoreCase))
                continue;

            var numericMatches = Regex.Matches(row, @">(?<value>\d+|�)\s*<BR", RegexOptions.IgnoreCase);
            if (numericMatches.Count < 2)
                continue;

            int moderate = int.Parse(numericMatches[0].Groups["value"].Value, CultureInfo.InvariantCulture);
            int? severe = int.TryParse(numericMatches[1].Groups["value"].Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int severeValue)
                ? severeValue
                : null;

            string description = ExtractHtmlDescription(webHelpDir, link.Groups["href"].Value, name, "Disadvantage Descriptions");
            results.Add(new DisadvantageDefinition(
                Slugify($"disadvantage_{name}"),
                name,
                moderate,
                severe,
                description));
        }

        var merged = results
            .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);
        foreach (var custom in LoadCustomDisadvantages().Values)
            merged[custom.Id] = new DisadvantageDefinition(custom.Id, custom.Name, custom.ModerateBonus, custom.SevereBonus, custom.Description);
        return merged.Values.OrderBy(x => x.Name).ToList();
    }

    private static IEnumerable<string> ExtractTableRows(string html)
    {
        return Regex.Matches(html, @"(?is)<TR\b[^>]*>.*?</TR>")
            .Cast<Match>()
            .Select(m => m.Value);
    }

    private static string ExtractHtmlDescription(string webHelpDir, string href, string name, string titleLine)
    {
        string fileName = href.Split('#')[0];
        string? path = FindFileIgnoreCase(webHelpDir, fileName);
        if (!File.Exists(path))
            return string.Empty;

        string plain = HtmlToPlainText(File.ReadAllText(path));
        var lines = plain
            .Split('\n')
            .Select(line => NormalizeWhitespace(line))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        int headerIndex = lines.FindIndex(line =>
            line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase));

        if (headerIndex < 0)
            headerIndex = lines.FindIndex(line =>
                !string.Equals(line, titleLine, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(line, "Table of Contents", StringComparison.OrdinalIgnoreCase));

        if (headerIndex < 0)
            return string.Empty;

        var descriptionParts = new List<string>();
        string header = lines[headerIndex];
        if (header.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
            descriptionParts.Add(header[(name.Length + 1)..].Trim());

        for (int i = headerIndex + 1; i < lines.Count; i++)
        {
            string line = lines[i];
            if (string.Equals(line, "Table of Contents", StringComparison.OrdinalIgnoreCase)
                || string.Equals(line, titleLine, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            descriptionParts.Add(line);
        }

        return string.Join(" ", descriptionParts).Trim();
    }

    private static string HtmlToPlainText(string html)
    {
        string withLineBreaks = Regex.Replace(html, @"<P[^>]*>", "\n\n", RegexOptions.IgnoreCase);
        withLineBreaks = Regex.Replace(withLineBreaks, @"<BR\s*/?>", "\n", RegexOptions.IgnoreCase);
        string withoutTags = Regex.Replace(withLineBreaks, @"<[^>]+>", string.Empty);
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static int FindNextSummaryIndex(IReadOnlyList<ParsedParagraph> paragraphs, int startIndex)
    {
        for (int i = startIndex; i < paragraphs.Count; i++)
        {
            if (IsEntryHeading(paragraphs[i]) || IsCategoryHeading(paragraphs[i]))
                return -1;
            if (TryParseProficiencySummary(paragraphs[i].Text, out _, out _, out _))
                return i;
        }
        return -1;
    }

    private static bool TryParseProficiencySummary(string text, out int slots, out string ability, out int modifier)
    {
        string normalized = NormalizeWhitespace(text)
            .Replace('–', '-')
            .Replace('—', '-')
            .Replace("−", "-", StringComparison.Ordinal)
            .Replace(",", ", ", StringComparison.Ordinal);
        normalized = NormalizeWhitespace(normalized);

        // Handles variants like:
        // "1 slot, Wisdom +1"
        // "(TcRaH) 1 slot, Wisdom +1"
        // "(PlO:S&P, CP 2, Initial Rating 7, Intelligence/Knowledge)"
        var standard = Regex.Match(normalized,
            @"^(?:\([^)]*\)\s*)?(?<slots>\d+)\s+slots?\s*,\s*(?<ability>[A-Za-z/,\- ]+?)(?:\s*(?<modifier>[+-]\d+))?$",
            RegexOptions.IgnoreCase);
        if (standard.Success)
        {
            slots = int.Parse(standard.Groups["slots"].Value, CultureInfo.InvariantCulture);
            ability = NormalizeParsedAbility(standard.Groups["ability"].Value.Trim());
            modifier = int.TryParse(standard.Groups["modifier"].Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsedModifier)
                ? parsedModifier
                : 0;
            return true;
        }

        var playersOption = Regex.Match(normalized,
            @"^\([^)]*\bCP\s*(?<slots>\d+)\b[^)]*\bInitial\s+Rating\s*(?<rating>[+-]?\d+)\b[^)]*\b(?<ability>[A-Za-z]+(?:\s*/\s*[A-Za-z]+)*)\s*\)$",
            RegexOptions.IgnoreCase);
        if (playersOption.Success)
        {
            slots = int.Parse(playersOption.Groups["slots"].Value, CultureInfo.InvariantCulture);
            ability = NormalizeParsedAbility(playersOption.Groups["ability"].Value.Trim());
            modifier = int.TryParse(playersOption.Groups["rating"].Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsedRating)
                ? parsedRating
                : 0;
            return true;
        }

        slots = 0;
        ability = string.Empty;
        modifier = 0;
        return false;
    }

    private static bool IsEntryHeading(ParsedParagraph paragraph)
        => Regex.IsMatch(paragraph.Style, @"^Heading[6-8]$", RegexOptions.IgnoreCase)
           && !string.IsNullOrWhiteSpace(paragraph.Text);

    private static bool IsLikelyProficiencyName(string text)
    {
        string t = NormalizeWhitespace(text);
        if (string.IsNullOrWhiteSpace(t))
            return false;
        if (Regex.IsMatch(t, @"^table\s+\d+", RegexOptions.IgnoreCase))
            return false;
        if (t.Contains("inhaltsangabe", StringComparison.OrdinalIgnoreCase)
            || t.Contains("contents", StringComparison.OrdinalIgnoreCase)
            || t.Contains("appendix", StringComparison.OrdinalIgnoreCase)
            || t.Contains("chapter", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static bool IsCategoryHeading(ParsedParagraph paragraph)
    {
        if (!Regex.IsMatch(paragraph.Style, @"^Heading[1-5]$", RegexOptions.IgnoreCase))
            return false;
        var t = paragraph.Text;
        return t.Contains("Fighter", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Warrior", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Paladin", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Ranger", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Priest", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Cleric", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Druid", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Rogue", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Thief", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Bard", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Wizard", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Mage", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Common", StringComparison.OrdinalIgnoreCase)
            || (t.Contains("Proficiencies", StringComparison.OrdinalIgnoreCase)
                && !t.Contains("Non-Weapon", StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeCategory(string text)
    {
        var t = text.ToLowerInvariant();
        if (t.Contains("fighter") || t.Contains("warrior") || t.Contains("paladin") || t.Contains("ranger"))
            return "Warrior";
        if (t.Contains("priest") || t.Contains("cleric") || t.Contains("druid"))
            return "Priest";
        if (t.Contains("rogue") || t.Contains("thief") || t.Contains("bard"))
            return "Rogue";
        if (t.Contains("wizard") || t.Contains("mage"))
            return "Mage";
        if (t.Contains("common") || t.Contains("general"))
            return "General";
        return null;
    }

    private static Dictionary<string, IReadOnlyList<string>> ExtractDocxNwpClassAvailabilityMap(XDocument document, XNamespace w)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        string? currentClassHeading = null;

        foreach (var table in document.Descendants(w + "tbl"))
        {
            var rows = table.Descendants(w + "tr").ToList();
            if (rows.Count == 0)
                continue;

            int startRow = 0;

            var firstRowCells = rows[0]
                .Descendants(w + "tc")
                .Select(tc => NormalizeWhitespace(string.Concat(tc.Descendants(w + "t").Select(t => (string?)t ?? string.Empty))))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            string firstRowText = string.Join(" | ", firstRowCells);

            string? explicitHeading = TryExtractDocxClassHeading(firstRowText);
            if (explicitHeading is not null)
            {
                currentClassHeading = explicitHeading;
                startRow = 1; // skip the heading row, but parse the rest of this table
            }

            if (string.IsNullOrWhiteSpace(currentClassHeading))
                continue;

            var classIds = InferAllowedClassesFromHeading(currentClassHeading, NormalizeCategory(currentClassHeading) ?? "General");
            if (classIds is null || classIds.Count == 0)
                continue;

            foreach (var row in rows.Skip(startRow))
            {
                var cells = row
                    .Descendants(w + "tc")
                    .Select(tc => NormalizeWhitespace(string.Concat(tc.Descendants(w + "t").Select(t => (string?)t ?? string.Empty))))
                    .ToList();
                if (cells.Count == 0)
                    continue;

                string rawName = cells[0];
                if (string.IsNullOrWhiteSpace(rawName))
                    continue;
                if (rawName.Contains("Proficiency", StringComparison.OrdinalIgnoreCase)
                    || rawName.Contains("Slots", StringComparison.OrdinalIgnoreCase)
                    || rawName.Contains("Level", StringComparison.OrdinalIgnoreCase)
                    || rawName.Contains("Distance", StringComparison.OrdinalIgnoreCase)
                    || rawName.Contains("Duration", StringComparison.OrdinalIgnoreCase)
                    || rawName.Contains("Improved Score", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = NormalizeDocxTableProficiencyName(rawName);
                if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
                    continue;
                if (!Regex.IsMatch(name, "[A-Za-z]"))
                    continue;
                if (Regex.IsMatch(name, @"^\d+(st|nd|rd|th)?", RegexOptions.IgnoreCase))
                    continue;
                if (!TryExtractDocxTableSlots(cells, out _))
                    continue;

                string key = NormalizeNwpNameForMerge(name);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                string inlineText = string.Join(" | ", cells.Where(c => !string.IsNullOrWhiteSpace(c)));
                var inlineClasses = ExtractClassIdsFromInlineText(inlineText);
                var effectiveClassIds = UnionAllowedClasses(classIds, inlineClasses);
                if (effectiveClassIds is null || effectiveClassIds.Count == 0)
                    continue;

                if (!map.TryGetValue(key, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    map[key] = set;
                }
                foreach (var id in effectiveClassIds)
                    set.Add(id);
            }
        }

        return map.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<NonweaponProficiencyDefinition> ExtractDocxTableFallbackNwps(XDocument document, XNamespace w)
    {
        var entriesByName = new Dictionary<string, NonweaponProficiencyDefinition>(StringComparer.OrdinalIgnoreCase);
        string? currentClassHeading = null;

        foreach (var table in document.Descendants(w + "tbl"))
        {
            var rows = table.Descendants(w + "tr").ToList();
            if (rows.Count == 0)
                continue;

            int startRow = 0;
            var firstRowCells = rows[0]
                .Descendants(w + "tc")
                .Select(tc => NormalizeWhitespace(string.Concat(tc.Descendants(w + "t").Select(t => (string?)t ?? string.Empty))))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            string firstRowText = string.Join(" | ", firstRowCells);

            string? explicitHeading = TryExtractDocxClassHeading(firstRowText);
            if (explicitHeading is not null)
            {
                currentClassHeading = explicitHeading;
                startRow = 1;
            }

            if (string.IsNullOrWhiteSpace(currentClassHeading))
                continue;

            string category = NormalizeCategory(currentClassHeading) ?? "General";
            var headingClasses = InferAllowedClassesFromHeading(currentClassHeading, category);

            foreach (var row in rows.Skip(startRow))
            {
                var cells = row
                    .Descendants(w + "tc")
                    .Select(tc => NormalizeWhitespace(string.Concat(tc.Descendants(w + "t").Select(t => (string?)t ?? string.Empty))))
                    .ToList();
                if (cells.Count == 0)
                    continue;

                string rawName = cells[0];
                if (string.IsNullOrWhiteSpace(rawName))
                    continue;
                if (rawName.Contains("Proficiency", StringComparison.OrdinalIgnoreCase)
                    || rawName.Contains("Slots", StringComparison.OrdinalIgnoreCase)
                    || rawName.Contains("Level", StringComparison.OrdinalIgnoreCase)
                    || rawName.Contains("Distance", StringComparison.OrdinalIgnoreCase)
                    || rawName.Contains("Duration", StringComparison.OrdinalIgnoreCase)
                    || rawName.Contains("Improved Score", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string name = NormalizeDocxTableProficiencyName(rawName);
                if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
                    continue;
                if (!Regex.IsMatch(name, "[A-Za-z]"))
                    continue;
                if (Regex.IsMatch(name, @"^\d+(st|nd|rd|th)?", RegexOptions.IgnoreCase))
                    continue;
                if (!TryExtractDocxTableSlots(cells, out int slots))
                    continue;

                string inlineText = string.Join(" | ", cells.Where(c => !string.IsNullOrWhiteSpace(c)));
                var inlineClasses = ExtractClassIdsFromInlineText(inlineText);
                var allowedClasses = UnionAllowedClasses(headingClasses, inlineClasses);

                string sourceProbe = rawName;
                string source = ExtractSource(ref sourceProbe);
                string ability = TryExtractDocxTableAbility(cells);
                int modifier = TryExtractDocxTableModifier(cells);

                string key = NormalizeNwpNameForMerge(name);
                var entry = new NonweaponProficiencyDefinition(
                    Slugify($"nwp_{name}"),
                    name,
                    category,
                    slots,
                    ability,
                    modifier,
                    "Imported from proficiency table.",
                    source,
                    AllowedClasses: allowedClasses);

                if (entriesByName.TryGetValue(key, out var existing))
                {
                    entriesByName[key] = existing with
                    {
                        AllowedClasses = UnionAllowedClasses(existing.AllowedClasses, entry.AllowedClasses)
                    };
                }
                else
                {
                    entriesByName[key] = entry;
                }
            }
        }

        return entriesByName.Values
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ToList();
    }

    private static bool TryExtractDocxTableSlots(IReadOnlyList<string> cells, out int slots)
    {
        slots = 0;
        if (cells.Count < 2)
            return false;

        int max = Math.Min(cells.Count - 1, 4);
        for (int i = 1; i <= max; i++)
        {
            var match = Regex.Match(cells[i] ?? string.Empty, @"^\s*(\d)\s*$");
            if (!match.Success)
                continue;

            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                continue;

            if (parsed is >= 1 and <= 5)
            {
                slots = parsed;
                return true;
            }
        }

        return false;
    }

    private static string TryExtractDocxTableAbility(IReadOnlyList<string> cells)
    {
        foreach (var cell in cells)
        {
            string t = NormalizeWhitespace(cell ?? string.Empty)
                .Replace('–', '-')
                .Replace('—', '-');

            if (Regex.IsMatch(t, @"\b(na|n/a)\b", RegexOptions.IgnoreCase))
                return "None";

            if (Regex.IsMatch(t, @"\bstr|strength\b", RegexOptions.IgnoreCase)) return "Strength";
            if (Regex.IsMatch(t, @"\bdex|dexterity\b", RegexOptions.IgnoreCase)) return "Dexterity";
            if (Regex.IsMatch(t, @"\bcon|constitution\b", RegexOptions.IgnoreCase)) return "Constitution";
            if (Regex.IsMatch(t, @"\bint|intelligence\b", RegexOptions.IgnoreCase)) return "Intelligence";
            if (Regex.IsMatch(t, @"\bwis|wisdom\b", RegexOptions.IgnoreCase)) return "Wisdom";
            if (Regex.IsMatch(t, @"\bcha|charisma\b", RegexOptions.IgnoreCase)) return "Charisma";
        }

        return "Intelligence";
    }

    private static int TryExtractDocxTableModifier(IReadOnlyList<string> cells)
    {
        foreach (var cell in cells)
        {
            string t = NormalizeWhitespace(cell ?? string.Empty)
                .Replace('–', '-')
                .Replace('—', '-')
                .Replace("−", "-", StringComparison.Ordinal);

            if (Regex.IsMatch(t, @"\b(na|n/a)\b", RegexOptions.IgnoreCase))
                return 0;

            var match = Regex.Match(t, @"(?<!\d)([+-]\d+)(?!\d)");
            if (match.Success
                && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                return value;
            }
        }

        return 0;
    }

    private static string NormalizeDocxTableProficiencyName(string raw)
    {
        string value = NormalizeWhitespace(raw)
            .Replace('–', '-')
            .Replace('—', '-')
            .Replace("−", "-", StringComparison.Ordinal);

        // Remove inlined source tags and table markers, then trim trailing slot digits.
        value = Regex.Replace(value, @"\([^)]*\)", string.Empty);
        value = value.Replace("*", string.Empty, StringComparison.Ordinal);
        value = Regex.Replace(value, @"\d+\s*$", string.Empty);
        return NormalizeWhitespace(value.Trim(',', '.', ';', ':', '-', ' '));
    }

    private static string? TryExtractDocxClassHeading(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string t = NormalizeWhitespace(text);
        if (t.StartsWith("General", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Common", StringComparison.OrdinalIgnoreCase))
            return "General";
        if (t.StartsWith("Bard", StringComparison.OrdinalIgnoreCase))
            return "Bard";
        if (t.StartsWith("Druid", StringComparison.OrdinalIgnoreCase))
            return "Druid";
        if (t.StartsWith("Paladin", StringComparison.OrdinalIgnoreCase))
            return "Paladin";
        if (t.StartsWith("Ranger", StringComparison.OrdinalIgnoreCase))
            return "Ranger";
        if (t.StartsWith("Thief", StringComparison.OrdinalIgnoreCase))
            return "Thief";
        if (t.StartsWith("Rogue", StringComparison.OrdinalIgnoreCase))
            return "Rogue";
        if (t.StartsWith("Warrior", StringComparison.OrdinalIgnoreCase))
            return "Warrior";
        if (t.StartsWith("Priest", StringComparison.OrdinalIgnoreCase))
            return "Priest";
        if (t.StartsWith("Wizard", StringComparison.OrdinalIgnoreCase))
            return "Wizard";
        if (t.StartsWith("Psionicist", StringComparison.OrdinalIgnoreCase))
            return "Psionicist";
        return null;
    }

    private static IReadOnlyList<string>? ExtractClassIdsFromInlineText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        string t = NormalizeWhitespace(text).ToLowerInvariant();
        var classes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Regex.IsMatch(t, @"\bwarrior(s)?\b"))
        {
            classes.Add("fighter");
            classes.Add("paladin");
            classes.Add("ranger");
        }
        if (Regex.IsMatch(t, @"\bpriest(s)?\b"))
        {
            classes.Add("cleric");
            classes.Add("druid");
        }
        if (Regex.IsMatch(t, @"\brogue(s)?\b"))
        {
            classes.Add("thief");
            classes.Add("bard");
        }

        if (Regex.IsMatch(t, @"\bfighter(s)?\b")) classes.Add("fighter");
        if (Regex.IsMatch(t, @"\bpaladin(s)?\b")) classes.Add("paladin");
        if (Regex.IsMatch(t, @"\branger(s)?\b")) classes.Add("ranger");
        if (Regex.IsMatch(t, @"\bcleric(s)?\b")) classes.Add("cleric");
        if (Regex.IsMatch(t, @"\bdruid(s)?\b")) classes.Add("druid");
        if (Regex.IsMatch(t, @"\bthief(s)?\b")) classes.Add("thief");
        if (Regex.IsMatch(t, @"\bbard(s)?\b")) classes.Add("bard");
        if (Regex.IsMatch(t, @"\bwizard(s)?\b")) classes.Add("wizard");
        if (Regex.IsMatch(t, @"\billusionist(s)?\b")) classes.Add("illusionist");
        if (Regex.IsMatch(t, @"\bpsionicist(s)?\b")) classes.Add("psionicist");

        return classes.Count == 0
            ? null
            : classes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string>? InferAllowedClassesFromHeading(string heading, string category)
    {
        string text = NormalizeWhitespace(heading).ToLowerInvariant();

        if (text.Contains("common") || text.Contains("general"))
            return null;

        if (text.Contains("paladin"))
            return new[] { "paladin" };
        if (text.Contains("ranger"))
            return new[] { "ranger" };
        if (text.Contains("fighter") && !text.Contains("warrior"))
            return new[] { "fighter" };

        if (text.Contains("druid"))
            return new[] { "druid" };
        if (text.Contains("cleric"))
            return new[] { "cleric" };
        if (text.Contains("priest"))
            return new[] { "cleric", "druid" };

        if (text.Contains("bard"))
            return new[] { "bard" };
        if (text.Contains("thief") || text.Contains("thiefs"))
            return new[] { "thief" };
        if (text.Contains("rogue"))
            return new[] { "thief", "bard" };

        if (text.Contains("illusionist"))
            return new[] { "illusionist" };
        if (text.Contains("wizard"))
            return new[] { "wizard", "illusionist" };

        if (text.Contains("psionicist"))
            return new[] { "psionicist" };

        if (text.Contains("warrior"))
            return new[] { "fighter", "paladin", "ranger" };

        return InferAllowedClassesFromCategory(category);
    }

    private static IReadOnlyList<string>? InferAllowedClassesFromCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return null;

        if (string.Equals(category, "Warrior", StringComparison.OrdinalIgnoreCase))
            return new[] { "fighter", "paladin", "ranger" };
        if (string.Equals(category, "Priest", StringComparison.OrdinalIgnoreCase))
            return new[] { "cleric", "druid" };
        if (string.Equals(category, "Rogue", StringComparison.OrdinalIgnoreCase))
            return new[] { "thief", "bard" };
        if (string.Equals(category, "Mage", StringComparison.OrdinalIgnoreCase))
            return new[] { "wizard", "illusionist" };

        return null;
    }

    private static readonly Regex SourceSuffixRegex = new(
        @"\s*\(([A-Z][^)]*)\)\s*$", RegexOptions.Compiled);

    private static string ExtractSource(ref string name)
    {
        var m = SourceSuffixRegex.Match(name);
        if (!m.Success) return "Core";
        name = name[..m.Index].Trim().TrimEnd('.');
        string code = m.Groups[1].Value;
        if (code.Contains("PlO", StringComparison.OrdinalIgnoreCase))
            return "Player's Option";
        return "Supplement";
    }

    private static bool ShouldIgnoreParagraph(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (string.Equals(text, "Inhaltsangabe", StringComparison.OrdinalIgnoreCase))
            return true;
        if (Regex.IsMatch(text, @"^\d+$"))
            return true;
        return false;
    }

    private static string GetParagraphStyle(XElement paragraph, XNamespace w)
    {
        return paragraph
            .Element(w + "pPr")?
            .Element(w + "pStyle")?
            .Attribute(w + "val")?
            .Value ?? string.Empty;
    }

    private static string NormalizeWhitespace(string value)
        => Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();

    private static string NormalizeParsedAbility(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string cleaned = NormalizeWhitespace(value)
            .Replace('–', '-')
            .Replace('—', '-')
            .Replace("−", "-", StringComparison.Ordinal);

        // Keep the base ability when source includes sub-ability detail after commas.
        string primary = cleaned.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return string.IsNullOrWhiteSpace(primary) ? cleaned : primary;
    }

    private static string ToTitleCase(string value)
    {
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return string.Join("/", value
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => textInfo.ToTitleCase(part.Trim().ToLowerInvariant())));
    }

    private static string Slugify(string value)
    {
        string normalized = value.ToLowerInvariant();
        normalized = Regex.Replace(normalized, @"[^a-z0-9]+", "_");
        return normalized.Trim('_');
    }

    private static int GetSourcePriority(string source)
    {
        if (string.Equals(source, "Player's Option", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (string.Equals(source, "Supplement", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (string.Equals(source, "Core", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 0;
    }

    private static string? FindAssetsDirectory()
    {
        foreach (var startPath in new[] { AppDomain.CurrentDomain.BaseDirectory, Directory.GetCurrentDirectory() }
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dir = new DirectoryInfo(startPath);
            for (int i = 0; i < 12; i++)
            {
                var candidate = Path.Combine(dir.FullName, "Assets");
                if (Directory.Exists(candidate))
                    return candidate;

                if (string.Equals(dir.Name, "DungeonMasterCortex", StringComparison.OrdinalIgnoreCase)
                    && dir.Parent is not null)
                {
                    var siblingCandidate = Path.Combine(dir.Parent.FullName, "Assets");
                    if (Directory.Exists(siblingCandidate))
                        return siblingCandidate;
                }

                if (dir.Parent is null)
                    break;
                dir = dir.Parent;
            }
        }

        return null;
    }

    private static string? FindSkillsAndPowersDirectory()
    {
        string? assetsDir = FindAssetsDirectory();
        if (assetsDir is null)
            return null;
        string path = Path.Combine(assetsDir, "Core Rules", "WEBHELP", "SP");
        return Directory.Exists(path) ? path : null;
    }

    private static string? FindNonweaponProficiencyDocxPath()
    {
        string? assetsDir = FindAssetsDirectory();
        if (assetsDir is null)
            return null;
        string proficiencyDir = Path.Combine(assetsDir, "Nonweapon Proficiencies");
        return FindFileIgnoreCase(proficiencyDir, "Complete Book of Proficiencies.docx");
    }

    private static string? FindNonweaponProficiencyPdfPath()
    {
        string? assetsDir = FindAssetsDirectory();
        if (assetsDir is null)
            return null;
        string proficiencyDir = Path.Combine(assetsDir, "Nonweapon Proficiencies");
        return FindFileIgnoreCase(proficiencyDir, "The_Complete_Nonweapon_Proficiencies_V1.2.pdf");
    }

    private static string? FindMasterImportXlsxPath()
    {
        string? assetsDir = FindAssetsDirectory();
        if (assetsDir is null)
            return null;
        string proficiencyDir = Path.Combine(assetsDir, "Nonweapon Proficiencies");
        return FindFileIgnoreCase(proficiencyDir, "adnd2e_nwp_master_import.xlsx");
    }

    private static IReadOnlyList<NonweaponProficiencyDefinition> LoadPdfNonweaponProficiencies()
    {
        string? pdfPath = FindNonweaponProficiencyPdfPath();
        if (pdfPath is null)
            return Array.Empty<NonweaponProficiencyDefinition>();

        try
        {
            var paragraphs = new List<ParsedParagraph>();

            using (var document = PdfDocument.Open(pdfPath))
            {
                foreach (var page in document.GetPages())
                {
                    // Group words into visual lines by rounding the baseline Y coordinate.
                    var wordsByLine = page.GetWords()
                        .GroupBy(w => (int)Math.Round(w.BoundingBox.Bottom))
                        .OrderByDescending(g => g.Key);

                    foreach (var lineGroup in wordsByLine)
                    {
                        var words = lineGroup.OrderBy(w => w.BoundingBox.Left).ToList();
                        if (words.Count == 0)
                            continue;

                        string text = NormalizeWhitespace(string.Join(" ", words.Select(w => w.Text)));
                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        // Estimate heading level from average font size.
                        double avgFontSize = words
                            .SelectMany(w => w.Letters)
                            .Select(l => l.FontSize)
                            .DefaultIfEmpty(10.0)
                            .Average();

                        // Assign paragraph style names compatible with IsCategoryHeading/IsEntryHeading.
                        //   avgFontSize >= 13  → "Heading2"  (category-level, matches Heading1-5 regex)
                        //   avgFontSize >= 10  → "Heading7"  (entry-level, matches Heading6-8 regex)
                        //   else               → "Normal"
                        string style = avgFontSize >= 13.0 ? "Heading2"
                                     : avgFontSize >= 10.0 ? "Heading7"
                                     : "Normal";

                        paragraphs.Add(new ParsedParagraph(style, text));
                    }
                }
            }

            var results = new List<NonweaponProficiencyDefinition>();
            string currentCategory = "General";
            IReadOnlyList<string>? currentAllowedClasses = InferAllowedClassesFromCategory(currentCategory);

            for (int i = 0; i < paragraphs.Count; i++)
            {
                var paragraph = paragraphs[i];
                if (IsCategoryHeading(paragraph))
                {
                    string? newCat = NormalizeCategory(paragraph.Text);
                    if (newCat is not null)
                    {
                        currentCategory = newCat;
                        currentAllowedClasses = InferAllowedClassesFromHeading(paragraph.Text, currentCategory);
                    }
                    continue;
                }

                if (!IsEntryHeading(paragraph))
                    continue;

                string rawName = paragraph.Text.Trim().TrimEnd('.');
                if (!IsLikelyProficiencyName(rawName))
                    continue;

                string source = ExtractSource(ref rawName);
                if (string.IsNullOrEmpty(source))
                    source = "Supplement";
                string name = rawName;

                int summaryIndex = FindNextSummaryIndex(paragraphs, i + 1);
                if (summaryIndex < 0)
                    continue;

                if (!TryParseProficiencySummary(paragraphs[summaryIndex].Text,
                        out int slots, out string ability, out int modifier))
                    continue;

                var descParts = new List<string>();
                for (int j = summaryIndex + 1; j < paragraphs.Count; j++)
                {
                    var next = paragraphs[j];
                    if (IsEntryHeading(next) || IsCategoryHeading(next))
                        break;
                    if (ShouldIgnoreParagraph(next.Text))
                        continue;
                    descParts.Add(next.Text);
                }

                results.Add(new NonweaponProficiencyDefinition(
                    Slugify($"nwp_{name}"),
                    name,
                    currentCategory,
                    slots,
                    ToTitleCase(ability),
                    modifier,
                    string.Join(" ", descParts),
                    source,
                    AllowedClasses: currentAllowedClasses));
            }

            return results
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .Select(ConsolidateDuplicateNwps)
                .OrderBy(x => x.Category)
                .ThenBy(x => x.Name)
                .ToList();
        }
        catch (Exception ex)
        {
            WriteDiagnostics($"PDF NWP load failed: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<NonweaponProficiencyDefinition>();
        }
    }

    private static string? FindFileIgnoreCase(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;

        return Directory
            .EnumerateFiles(directory)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteDiagnostics(params string[] lines)
    {
        try
        {
            File.WriteAllLines(DiagnosticsPath, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
        }
        catch
        {
            // Ignore diagnostics failures.
        }
    }

    private sealed record ParsedParagraph(string Style, string Text);
}