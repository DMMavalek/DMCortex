using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public sealed class EquipmentCatalogService
{
    private static readonly Regex TableHeaderRegex = new(
        "^TABLE\\s+([A-Z0-9]+):\\s*(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PriceTailRegex = new(
        "(?<price>(\\d+[\\d,]*|\\d+\\+|\\d+-\\d+)(?:\\/\\w+)?\\s*(cp|sp|ep|gp|pp))$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public EquipmentCatalog GetCatalog()
    {
        var library = new EquipmentLibraryService().GetEquipmentLibrary();
        var grouped = new Dictionary<string, List<EquipmentCatalogItem>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in library)
        {
            var categories = item.Categories.Count > 0 ? item.Categories : new List<string> { "Uncategorized" };
            foreach (var category in categories)
            {
                if (!grouped.TryGetValue(category, out var list))
                {
                    list = new List<EquipmentCatalogItem>();
                    grouped[category] = list;
                }

                list.Add(new EquipmentCatalogItem(
                    ItemId: item.Id,
                    Category: category,
                    TableCode: "CUSTOM",
                    ItemName: item.Name,
                    CostText: BuildCostText(item)));
            }
        }

        var categoriesOut = grouped
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new EquipmentCategory(
                TableCode: "CUSTOM",
                Name: kv.Key,
                Items: kv.Value
                    .OrderBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();

        string message = categoriesOut.Count == 0
            ? "Equipment library is empty. Add items in DM Tools > Edit Information > Equipment."
            : "";

        return new EquipmentCatalog(categoriesOut, message);
    }

    internal static EquipmentCatalog LoadBaseCatalogFromText()
    {
        var path = ResolveCatalogPath();
        if (path is null || !File.Exists(path))
            return new EquipmentCatalog(Array.Empty<EquipmentCategory>(), "Equipment catalog file was not found.");

        var categories = ParseCatalog(path);
        return new EquipmentCatalog(categories, "");
    }

    private static string BuildCostText(CustomEquipmentData item)
    {
        var parts = new List<string>();
        if (item.CostGold > 0) parts.Add($"{item.CostGold} gp");
        if (item.CostSilver > 0) parts.Add($"{item.CostSilver} sp");
        if (item.CostCopper > 0) parts.Add($"{item.CostCopper} cp");
        return string.Join(", ", parts);
    }

    private static string? ResolveCatalogPath()
    {
        var candidates = new List<string>();

        string baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "Assets", "Equipment", "Equipment_List.txt"));

        string? current = baseDir;
        for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(current); i++)
        {
            candidates.Add(Path.Combine(current, "Assets", "Equipment", "Equipment_List.txt"));
            current = Directory.GetParent(current)?.FullName;
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static IReadOnlyList<EquipmentCategory> ParseCatalog(string path)
    {
        var allLines = File.ReadAllLines(path);
        var categories = new List<EquipmentCategory>();

        EquipmentCategoryBuilder? current = null;
        string pendingParent = string.Empty;

        foreach (string raw in allLines)
        {
            string line = raw.Replace('\t', ' ').TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var headerMatch = TableHeaderRegex.Match(line.TrimStart());
            if (headerMatch.Success)
            {
                if (current is not null)
                    categories.Add(current.Build());

                string tableCode = headerMatch.Groups[1].Value.Trim();
                string tableName = headerMatch.Groups[2].Value.Trim();
                current = new EquipmentCategoryBuilder(tableCode, tableName);
                pendingParent = string.Empty;
                continue;
            }

            if (current is null)
                continue;

            // The source has multi-line parent/variant rows where parent line has no price.
            var priceMatch = PriceTailRegex.Match(line);
            bool hasPrice = priceMatch.Success;

            string itemText = hasPrice
                ? line[..priceMatch.Index].TrimEnd(' ', '.', '\u00A0')
                : line.Trim();

            string priceText = hasPrice ? priceMatch.Groups["price"].Value.Trim() : string.Empty;
            bool isIndented = raw.Length > 0 && char.IsWhiteSpace(raw[0]);

            if (string.IsNullOrWhiteSpace(itemText))
                continue;

            if (!hasPrice && !isIndented)
            {
                pendingParent = itemText;
                continue;
            }

            string finalName = itemText;
            if (isIndented && !string.IsNullOrWhiteSpace(pendingParent))
                finalName = $"{pendingParent} - {itemText}";

            current.Items.Add(new EquipmentCatalogItem(
                ItemId: $"{current.TableCode}|{finalName}",
                Category: current.TableName,
                TableCode: current.TableCode,
                ItemName: finalName,
                CostText: priceText));

            if (!isIndented)
                pendingParent = finalName;
        }

        if (current is not null)
            categories.Add(current.Build());

        return categories
            .Where(c => c.Items.Count > 0)
            .Where(c => !string.Equals(c.TableCode, "XXXI", StringComparison.OrdinalIgnoreCase))
            .Where(c => !string.Equals(c.TableCode, "XXXII", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private sealed class EquipmentCategoryBuilder
    {
        public EquipmentCategoryBuilder(string tableCode, string tableName)
        {
            TableCode = tableCode;
            TableName = tableName;
        }

        public string TableCode { get; }
        public string TableName { get; }
        public List<EquipmentCatalogItem> Items { get; } = new();

        public EquipmentCategory Build() => new(TableCode, TableName, Items);
    }
}

public sealed record EquipmentCatalog(
    IReadOnlyList<EquipmentCategory> Categories,
    string LoadMessage);

public sealed record EquipmentCategory(
    string TableCode,
    string Name,
    IReadOnlyList<EquipmentCatalogItem> Items);

public sealed record EquipmentCatalogItem(
    string ItemId,
    string Category,
    string TableCode,
    string ItemName,
    string CostText);