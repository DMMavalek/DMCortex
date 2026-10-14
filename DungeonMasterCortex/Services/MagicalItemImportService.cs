using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public sealed record MagicalItemImportResult(
    int TotalRows,
    int ImportedRows,
    int UpdatedRows,
    int CreatedRows,
    int SkippedRows,
    List<string> Warnings);

public sealed class MagicalItemImportService
{
    public MagicalItemImportResult ImportFromWorkbook(string workbookPath)
    {
        if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
            throw new FileNotFoundException("Magical items workbook was not found.", workbookPath);

        var libraryService = new EquipmentLibraryService();
        var existing = libraryService.GetEquipmentLibrary().ToList();
        var byId = existing.ToDictionary(
            x => EquipmentLibraryService.CanonicalizeId(x.Id),
            x => x,
            StringComparer.OrdinalIgnoreCase);

        var byName = existing
            .GroupBy(x => NormalizeName(x.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var warnings = new List<string>();
        int totalRows = 0;
        int importedRows = 0;
        int updatedRows = 0;
        int createdRows = 0;
        int skippedRows = 0;

        using var workbook = new XLWorkbook(workbookPath);
        var sheet = workbook.Worksheets.FirstOrDefault(x => string.Equals(x.Name, "Items", StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.First();

        const int headerRow = 2;
        var headerMap = BuildHeaderMap(sheet, headerRow);
        if (!headerMap.ContainsKey("ItemID") || !headerMap.ContainsKey("ItemName"))
            throw new InvalidOperationException("Workbook must include ItemID and ItemName columns on header row 2.");

        int lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
        for (int rowNum = headerRow + 1; rowNum <= lastRow; rowNum++)
        {
            totalRows++;
            var row = sheet.Row(rowNum);

            string itemIdRaw = GetText(row, headerMap, "ItemID");
            string itemName = GetText(row, headerMap, "ItemName");
            if (string.IsNullOrWhiteSpace(itemIdRaw) || string.IsNullOrWhiteSpace(itemName))
            {
                skippedRows++;
                continue;
            }

            string canonicalId = EquipmentLibraryService.CanonicalizeId(itemIdRaw);
            bool exists = byId.TryGetValue(canonicalId, out var working);
            if (!exists || working is null)
            {
                working = new CustomEquipmentData
                {
                    Id = canonicalId,
                    Name = itemName,
                };
                createdRows++;
            }
            else
            {
                updatedRows++;
            }

            ApplyRowToItem(working, row, headerMap, byName, warnings, rowNum);
            byId[canonicalId] = working;
            importedRows++;
        }

        libraryService.SaveEquipmentLibrary(byId.Values);

        return new MagicalItemImportResult(
            TotalRows: totalRows,
            ImportedRows: importedRows,
            UpdatedRows: updatedRows,
            CreatedRows: createdRows,
            SkippedRows: skippedRows,
            Warnings: warnings);
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLWorksheet sheet, int row)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        for (int col = 1; col <= lastCol; col++)
        {
            string key = sheet.Cell(row, col).GetValue<string>().Trim();
            if (!string.IsNullOrWhiteSpace(key))
                map[key] = col;
        }
        return map;
    }

    private static string GetText(IXLRow row, Dictionary<string, int> headerMap, string key)
    {
        if (!headerMap.TryGetValue(key, out int col))
            return string.Empty;
        return row.Cell(col).GetValue<string>().Trim();
    }

    private static int ParseInt(string text, int fallback = 0)
    {
        if (int.TryParse((text ?? string.Empty).Trim(), out int parsed))
            return parsed;
        return fallback;
    }

    private static bool ParseBool(string text)
    {
        string value = (text ?? string.Empty).Trim();
        if (bool.TryParse(value, out bool parsed))
            return parsed;

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static List<string> BuildCategories(IXLRow row, Dictionary<string, int> headerMap)
    {
        var categories = new List<string>();
        void AddCategory(string key)
        {
            string value = GetText(row, headerMap, key);
            if (!string.IsNullOrWhiteSpace(value)
                && !categories.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                categories.Add(value);
            }
        }

        AddCategory("Category");
        AddCategory("Subcategory");
        AddCategory("ItemFamily");

        if (!categories.Contains("Magical Items", StringComparer.OrdinalIgnoreCase))
            categories.Insert(0, "Magical Items");

        return categories;
    }

    private static List<string> BuildTags(IXLRow row, Dictionary<string, int> headerMap)
    {
        var tags = new List<string>();
        void AddTag(string key, string tagValue)
        {
            if (ParseBool(GetText(row, headerMap, key))
                && !tags.Contains(tagValue, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(tagValue);
            }
        }

        AddTag("Consumable", "consumable");
        AddTag("MustBeWorn", "must_be_worn");
        AddTag("MustBeHeld", "must_be_held");
        AddTag("ChargedItem", "charged_item");
        AddTag("Cursed", "cursed");
        AddTag("Intelligent", "intelligent");
        AddTag("ArtifactOrRelic", "artifact_or_relic");
        AddTag("NeedsManualCoding", "needs_manual_coding");
        AddTag("ComplexEffect", "complex_effect");

        return tags;
    }

    private static string BuildDescription(IXLRow row, Dictionary<string, int> headerMap)
    {
        var descriptionParts = new[]
        {
            GetText(row, headerMap, "ShortDescription"),
            GetText(row, headerMap, "Notes"),
            GetText(row, headerMap, "EffectList"),
            GetText(row, headerMap, "ChargeNotes"),
            GetText(row, headerMap, "AbilityEffectNotes"),
            GetText(row, headerMap, "EffectNotes"),
            GetText(row, headerMap, "ConditionText"),
        }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join("\n\n", descriptionParts);
    }

    private static void ApplyRowToItem(
        CustomEquipmentData item,
        IXLRow row,
        Dictionary<string, int> headerMap,
        Dictionary<string, CustomEquipmentData> baseByName,
        List<string> warnings,
        int rowNum)
    {
        item.Id = EquipmentLibraryService.CanonicalizeId(GetText(row, headerMap, "ItemID"));
        item.Name = GetText(row, headerMap, "ItemName");
        item.IsMagical = true;
        item.Categories = BuildCategories(row, headerMap);
        item.ItemTags = BuildTags(row, headerMap);
        item.Description = BuildDescription(row, headerMap);

        int enhancement = ParseInt(GetText(row, headerMap, "EnhancementBonus"), 0);
        int attackBonus = ParseInt(GetText(row, headerMap, "AttackBonus"), 0);
        int damageBonus = ParseInt(GetText(row, headerMap, "DamageBonus"), 0);
        int thac0Mod = ParseInt(GetText(row, headerMap, "THAC0Mod"), 0);
        int acMod = ParseInt(GetText(row, headerMap, "ACMod"), 0);

        item.EnchantmentBonus = enhancement;
        item.WeaponAttackBonus = attackBonus;
        item.WeaponDamageBonus = damageBonus;
        item.WeaponThac0Modifier = thac0Mod;
        item.ArmorClassModifier = acMod;

        bool looksLikeArmor = item.Categories.Any(c =>
            c.Contains("armor", StringComparison.OrdinalIgnoreCase)
            || c.Contains("shield", StringComparison.OrdinalIgnoreCase));
        bool looksLikeShield = item.Name.Contains("shield", StringComparison.OrdinalIgnoreCase)
            || item.Categories.Any(c => c.Contains("shield", StringComparison.OrdinalIgnoreCase));

        bool looksLikeWeapon = item.Categories.Any(c =>
            c.Contains("weapon", StringComparison.OrdinalIgnoreCase)
            || c.Contains("sword", StringComparison.OrdinalIgnoreCase)
            || c.Contains("axe", StringComparison.OrdinalIgnoreCase)
            || c.Contains("bow", StringComparison.OrdinalIgnoreCase)
            || c.Contains("hammer", StringComparison.OrdinalIgnoreCase)
            || c.Contains("spear", StringComparison.OrdinalIgnoreCase));

        string baseName = GetText(row, headerMap, "BaseItemName");

        if (looksLikeArmor)
        {
            item.IsArmor = !looksLikeShield;
            item.IsShield = looksLikeShield;

            if (looksLikeShield)
            {
                int baseShieldAdj = -1;
                item.ArmorClassValue = baseShieldAdj - enhancement + acMod;
            }
            else
            {
                int baseArmorAc = item.ArmorClassValue <= 0 || item.ArmorClassValue > 10 ? 10 : item.ArmorClassValue;
                if (!string.IsNullOrWhiteSpace(baseName)
                    && baseByName.TryGetValue(NormalizeName(baseName), out var baseItem)
                    && baseItem.IsArmor)
                {
                    baseArmorAc = baseItem.ArmorClassValue;
                }

                if (baseArmorAc == 10)
                    warnings.Add($"Row {rowNum}: could not resolve base AC for '{item.Name}', defaulted to AC 10 baseline.");

                item.ArmorClassValue = Math.Clamp(baseArmorAc - enhancement + acMod, 1, 10);
            }
        }

        if (looksLikeWeapon)
        {
            item.IsWeapon = true;

            if (enhancement != 0)
            {
                if (item.WeaponAttackBonus == 0 && item.WeaponThac0Modifier == 0)
                    item.WeaponAttackBonus = enhancement;
                if (item.WeaponDamageBonus == 0)
                    item.WeaponDamageBonus = enhancement;
            }
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in headerMap)
        {
            string value = row.Cell(kv.Value).GetValue<string>().Trim();
            if (!string.IsNullOrWhiteSpace(value))
                metadata[kv.Key] = value;
        }

        item.MagicalItemMetadata = metadata;
    }
}
