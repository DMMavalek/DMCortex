using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public sealed class EquipmentLibraryService
{
    private static readonly Regex MultiWhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex TrailingCoinChunkRegex = new(
        @"^(?<base>.*?)(?<cost>(\d[\d,]*\s*(cp|sp|ep|gp|pp)\s*)+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CoinValueRegex = new(
        @"(?<amount>\d[\d,]*)\s*(?<coin>cp|sp|ep|gp|pp)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CoinRangeRegex = new(
        @"(?<low>\d[\d,]*)\s*-\s*(?<high>\d[\d,]*)\s*(?<coin>cp|sp|ep|gp|pp)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlRowRegex = new(@"<TR[^>]*>(?<row>.*?)</TR>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HtmlCellRegex = new(@"<TD[^>]*>(?<cell>.*?)</TD>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex NameUnitWeightRegex = new(@"(?<value>\d+(?:\.\d+)?)\s*(?<unit>lb|lbs|pound|pounds|oz)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TrailingFootnoteDigitsRegex = new(@"\d+$", RegexOptions.Compiled);
    // Detects stale old-parser "Sword - One handed" style names (no hyphen in sub-part).
    private static readonly Regex StaleNoHyphenGroupedWeaponRegex = new(@" - (One|Two)\s+handed$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ArmorCategoryHints =
    {
        "armor", "armour", "mail", "shield", "helm", "helmet", "barding", "buckler", "gauntlet", "bracer", "greaves"
    };

    private static readonly string[] MeleeWeaponHints =
    {
        "sword", "dagger", "knife", "axe", "mace", "hammer", "club", "spear", "halberd", "polearm", "flail",
        "morningstar", "staff", "lance", "pike", "trident", "scimitar", "rapier", "katana", "whip", "blackjack"
    };

    private static readonly string[] MissileWeaponHints =
    {
        "bow", "crossbow", "arrow", "arrows", "bolt", "bolts", "quarrel", "quarrels", "sling", "dart", "darts",
        "javelin", "javelins", "hurled", "thrown", "boomerang", "net", "blowgun", "arquebus", "bolas", "lasso",
        "stinkpot", "shuriken", "chakram",
    };

    /// <summary>Sub-item names in weapon tables that need to be prefixed with their section header.</summary>
    private static readonly HashSet<string> GenericWeaponSubNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "One-handed", "Two-handed", "One handed", "Two handed",
        "Attached", "Held", "Light", "Medium", "Heavy", "Jousting",
    };

    private record struct WeaponTableStat(
        int CostGold, int CostSilver, int CostCopper,
        double Weight, string Size, string Type,
        int Speed, string DamageSmallMed, string DamageLarge);

    private static readonly Dictionary<string, int> ArmorClassByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["full plate"] = 1,
        ["field plate"] = 2,
        ["plate mail"] = 3,
        ["bronze plate mail"] = 4,
        ["banded mail"] = 4,
        ["splint mail"] = 4,
        ["chain mail"] = 5,
        ["scale mail"] = 6,
        ["hide"] = 6,
        ["brigandine"] = 6,
        ["ring mail"] = 7,
        ["studded leather"] = 7,
        ["leather"] = 8,
        ["padded"] = 8,
        ["shield"] = 9,
        ["buckler"] = 9,
        // Common "full" armor variants should resolve to their base armor AC.
        ["chain mail, full"] = 5,
        ["plate mail, full"] = 3,
        ["scale mail, full"] = 6,
        ["ring mail, full"] = 7,
        ["studded leather, full"] = 7,
        ["leather, full"] = 8,
        ["padded, full"] = 8,
        ["splint mail, full"] = 4,
        ["banded mail, full"] = 4,
        ["brigandine, full"] = 6,
        ["hide, full"] = 6,
    };

    // Standard shields to guarantee they appear in the Armor category even if PHB HTML parsing misses them.
    private static readonly string[] SupplementalShieldNames =
    {
        "Buckler",
        "Small Shield",
        "Medium Shield",
        "Body Shield",
    };

    // Supplemental armor variants that are frequently referenced in play but are not always present
    // in the imported PHB armor table names.
    private static readonly (string Name, int ArmorClassValue)[] SupplementalArmorVariants =
    {
        ("Padded, partial", 8),
        ("Padded, full", 8),
        ("Leather, partial", 8),
        ("Leather, full", 8),
        ("Studded leather, partial", 7),
        ("Studded leather, full", 7),
        ("Ring mail, partial", 7),
        ("Ring mail, full", 7),
        ("Scale mail, partial", 6),
        ("Scale mail, full", 6),
        ("Hide, partial", 6),
        ("Hide, full", 6),
        ("Brigandine, partial", 6),
        ("Brigandine, full", 6),
        ("Chain mail, partial", 5),
        ("Chain mail, full", 5),
        ("Splint mail, partial", 4),
        ("Splint mail, full", 4),
        ("Banded mail, partial", 4),
        ("Banded mail, full", 4),
        ("Plate mail, partial", 3),
        ("Plate mail, full", 3),
        ("Field plate, full", 2),
        ("Full plate, full", 1),
    };

    private static readonly (string Id, string Name, int CostGold, double Weight, string Description)[] SupplementalSpellbooks =
    {
        (
            "spellbook_traveling",
            "Traveling Spellbook",
            100,
            3.0,
            "Wizard spellbook, 50 pages, 12\" x 6\" x 1\"."
        ),
        (
            "spellbook_standard",
            "Standard Spellbook",
            500,
            15.0,
            "Wizard spellbook, 100 pages, 16\" x 12\" x 6\"."
        ),
        (
            "spellbook_tome",
            "Spellbook Tome",
            3000,
            100.0,
            "Wizard spellbook tome, 500 pages, 20\" x 16\" x 12\"."
        ),
    };

    // Canonical armor names that should always be treated as body armor when encountered.
    private static readonly Dictionary<string, int> CanonicalArmorNameToAc = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Padded"] = 8,
        ["Leather"] = 8,
        ["Studded leather"] = 7,
        ["Ring mail"] = 7,
        ["Scale mail"] = 6,
        ["Hide"] = 6,
        ["Brigandine"] = 6,
        ["Chain mail"] = 5,
        ["Splint mail"] = 4,
        ["Banded mail"] = 4,
        ["Plate mail"] = 3,
        ["Field plate"] = 2,
        ["Full plate"] = 1,
        ["Bronze plate mail"] = 4,
    };

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DungeonMasterCortex");

    private static readonly string EquipmentLibraryPath = Path.Combine(SettingsDirectory, "equipment_library.json");

    public IReadOnlyList<CustomEquipmentData> GetEquipmentLibrary()
    {
        var loaded = LoadPersistedLibrary();
        if (loaded.Count > 0)
        {
            var migrated = AutoMigrateLibrary(loaded, out bool changed);
            if (changed)
                PersistEquipmentLibrary(migrated);
            return migrated;
        }

        var seeded = SeedFromCatalogText();
        var migratedSeeded = AutoMigrateLibrary(seeded, out _);
        PersistEquipmentLibrary(migratedSeeded);
        return migratedSeeded;
    }

    public void SaveEquipmentLibrary(IEnumerable<CustomEquipmentData> entries)
    {
        var normalized = entries
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(Normalize)
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PersistEquipmentLibrary(normalized);
    }

    public EquipmentCleanupResult CleanupEquipmentLibrary()
    {
        var loaded = LoadPersistedLibrary();
        if (loaded.Count == 0)
            return new EquipmentCleanupResult(0, 0);

        int changed = 0;
        var cleaned = new List<CustomEquipmentData>(loaded.Count);

        foreach (var item in loaded)
        {
            string originalName = item.Name ?? string.Empty;
            var normalizedCategories = item.Categories
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => CollapseWhitespace(x.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string cleanedName = StripCategoryPrefixes(originalName, normalizedCategories);
            cleanedName = CollapseWhitespace(cleanedName);

            var updated = new CustomEquipmentData
            {
                Id = item.Id,
                Name = cleanedName,
                Description = item.Description,
                IsMagical = item.IsMagical,
                Categories = normalizedCategories,
                ItemTags = item.ItemTags,
                SizeClass = item.SizeClass,
                Weight = item.Weight,
                IsArmor = item.IsArmor,
                ArmorClassValue = item.ArmorClassValue,
                RogueArmorProfile = item.RogueArmorProfile,
                IsShield = item.IsShield,
                IsWeapon = item.IsWeapon,
                WeaponSpeed = item.WeaponSpeed,
                WeaponDamageSmallMedium = item.WeaponDamageSmallMedium,
                WeaponDamageLarge = item.WeaponDamageLarge,
                WeaponType = item.WeaponType,
                WeaponSize = item.WeaponSize,
                CostCopper = item.CostCopper,
                CostSilver = item.CostSilver,
                CostGold = item.CostGold,
                IsContainer = item.IsContainer,
                ContainerMaxItems = item.ContainerMaxItems,
                ContainerMaxWeight = item.ContainerMaxWeight,
                AllowedContentTags = item.AllowedContentTags,
            };

            var normalized = Normalize(updated);
            cleaned.Add(normalized);

            if (!string.Equals(CollapseWhitespace(originalName), normalized.Name, StringComparison.Ordinal))
                changed++;
        }

        SaveEquipmentLibrary(cleaned);
        return new EquipmentCleanupResult(cleaned.Count, changed);
    }

    public EquipmentDescriptionImportResult ImportMissingDescriptionsFromWebHelp(int maxUnresolvedInReport = 200)
    {
        var loaded = LoadPersistedLibrary();
        if (loaded.Count == 0)
            return new EquipmentDescriptionImportResult(0, 0, 0, 0, Array.Empty<string>(), string.Empty);

        var working = loaded.Select(Normalize).ToList();
        int missingBefore = working.Count(x => string.IsNullOrWhiteSpace(x.Description));
        if (missingBefore == 0)
            return new EquipmentDescriptionImportResult(working.Count, 0, 0, 0, Array.Empty<string>(), string.Empty);

        var htmlDocs = LoadWebHelpDocuments();
        int filled = 0;
        var unresolved = new List<string>();

        for (int i = 0; i < working.Count; i++)
        {
            var item = working[i];
            if (!string.IsNullOrWhiteSpace(item.Description))
                continue;

            if (TryFindEquipmentDescription(item.Name, htmlDocs, out string description))
            {
                item.Description = description;
                working[i] = Normalize(item);
                filled++;
            }
            else
            {
                unresolved.Add(item.Name);
            }
        }

        if (filled > 0)
            SaveEquipmentLibrary(working);

        int missingAfter = Math.Max(0, missingBefore - filled);
        var unresolvedTop = unresolved
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxUnresolvedInReport))
            .ToList();

        string reportPath = WriteDescriptionImportReport(
            totalItems: working.Count,
            missingBefore: missingBefore,
            filled: filled,
            missingAfter: missingAfter,
            unresolvedTop: unresolvedTop);

        return new EquipmentDescriptionImportResult(
            working.Count,
            missingBefore,
            filled,
            missingAfter,
            unresolvedTop,
            reportPath);
    }

    public static bool CanContain(
        CustomEquipmentData container,
        CustomEquipmentData content,
        int quantity,
        int currentItemCount,
        double currentWeight,
        out string reason)
    {
        reason = string.Empty;
        quantity = Math.Max(1, quantity);

        if (!container.IsContainer)
        {
            reason = "Item is not marked as a container.";
            return false;
        }

        if (container.ContainerMaxItems > 0 && currentItemCount + quantity > container.ContainerMaxItems)
        {
            reason = $"Exceeds max item count ({container.ContainerMaxItems}).";
            return false;
        }

        double additionalWeight = Math.Max(0, content.Weight) * quantity;
        if (container.ContainerMaxWeight > 0 && currentWeight + additionalWeight > container.ContainerMaxWeight)
        {
            reason = $"Exceeds max weight ({container.ContainerMaxWeight}).";
            return false;
        }

        if (container.AllowedContentTags.Count > 0)
        {
            bool hasAllowedTag = content.ItemTags
                .Any(tag => container.AllowedContentTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
            if (!hasAllowedTag)
            {
                reason = "Item tags do not match container allowed-content tags.";
                return false;
            }
        }

        int contentSize = GetSizeRank(content.SizeClass);
        int containerSize = GetSizeRank(container.SizeClass);
        if (contentSize > containerSize)
        {
            reason = "Item size is too large for this container.";
            return false;
        }

        return true;
    }

    private static List<CustomEquipmentData> LoadPersistedLibrary()
    {
        try
        {
            if (!File.Exists(EquipmentLibraryPath))
                return new List<CustomEquipmentData>();

            var json = File.ReadAllText(EquipmentLibraryPath);
            var list = JsonSerializer.Deserialize<List<CustomEquipmentData>>(json) ?? new List<CustomEquipmentData>();
            return list.Select(Normalize).ToList();
        }
        catch
        {
            return new List<CustomEquipmentData>();
        }
    }

    private static void PersistEquipmentLibrary(IEnumerable<CustomEquipmentData> entries)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(entries.ToList(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(EquipmentLibraryPath, json);
    }

    private static List<CustomEquipmentData> SeedFromCatalogText()
    {
        var catalog = EquipmentCatalogService.LoadBaseCatalogFromText();
        var byId = new Dictionary<string, CustomEquipmentData>(StringComparer.OrdinalIgnoreCase);

        foreach (var category in catalog.Categories)
        {
            foreach (var item in category.Items)
            {
                string canonicalId = CanonicalizeId(item.ItemId);

                if (!byId.TryGetValue(canonicalId, out var existing))
                {
                    var seeded = new CustomEquipmentData
                    {
                        Id = string.IsNullOrWhiteSpace(item.ItemId) ? BuildId(item.ItemName) : canonicalId,
                        Name = item.ItemName,
                        Description = "",
                        Categories = new List<string> { item.Category },
                        ItemTags = new List<string>(),
                        SizeClass = "Medium",
                        Weight = 0,
                        IsContainer = false,
                        ContainerMaxItems = 0,
                        ContainerMaxWeight = 0,
                        AllowedContentTags = new List<string>()
                    };

                    ApplyCostTextToFields(seeded, item.CostText);
                    byId[canonicalId] = Normalize(seeded);
                }
                else if (!existing.Categories.Contains(item.Category, StringComparer.OrdinalIgnoreCase))
                {
                    existing.Categories.Add(item.Category);
                }
            }
        }

        return byId.Values
            .Select(Normalize)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ApplyCostTextToFields(CustomEquipmentData item, string costText)
    {
        if (!TryExtractCoinTotals(costText, out int gp, out int sp, out int cp))
            return;

        item.CostGold = gp;
        item.CostSilver = sp;
        item.CostCopper = cp;
    }

    private static CustomEquipmentData Normalize(CustomEquipmentData source)
    {
        var normalizedCategories = source.Categories
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var normalizedTags = source.ItemTags
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allowedTags = source.AllowedContentTags
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string cleanName = (source.Name ?? string.Empty).Trim();
        cleanName = CollapseWhitespace(cleanName);
        string id = string.IsNullOrWhiteSpace(source.Id)
            ? BuildId(cleanName)
            : CanonicalizeId(source.Id);

        return new CustomEquipmentData
        {
            Id = id,
            Name = cleanName,
            Description = CollapseWhitespace((source.Description ?? string.Empty).Trim()),
            IsMagical = source.IsMagical,
            Categories = normalizedCategories,
            ItemTags = normalizedTags,
            SizeClass = string.IsNullOrWhiteSpace(source.SizeClass) ? "Medium" : source.SizeClass.Trim(),
            Weight = Math.Max(0, source.Weight),
            IsArmor = source.IsArmor,
            // Shields legitimately use -1 as their AC bonus value; only substitute 10 when the value is 0 (unset).
            ArmorClassValue = source.IsShield
                ? (source.ArmorClassValue == 0 ? -1 : Math.Clamp(source.ArmorClassValue, -5, 0))
                : Math.Clamp(source.ArmorClassValue <= 0 ? 10 : source.ArmorClassValue, 1, 10),
            RogueArmorProfile = string.IsNullOrWhiteSpace(source.RogueArmorProfile)
                ? "no_armor"
                : source.RogueArmorProfile.Trim().ToLowerInvariant(),
            IsShield = source.IsShield,
            IsWeapon = source.IsWeapon,
            WeaponSpeed = Math.Max(0, source.WeaponSpeed),
            WeaponDamageSmallMedium = CollapseWhitespace((source.WeaponDamageSmallMedium ?? string.Empty).Trim()),
            WeaponDamageLarge = CollapseWhitespace((source.WeaponDamageLarge ?? string.Empty).Trim()),
            WeaponType = string.IsNullOrWhiteSpace(source.WeaponType) ? string.Empty : source.WeaponType.Trim().ToUpperInvariant(),
            WeaponSize = string.IsNullOrWhiteSpace(source.WeaponSize) ? string.Empty : source.WeaponSize.Trim().ToUpperInvariant(),
            CostCopper = Math.Max(0, source.CostCopper),
            CostSilver = Math.Max(0, source.CostSilver),
            CostGold = Math.Max(0, source.CostGold),
            IsContainer = source.IsContainer,
            ContainerMaxItems = Math.Max(0, source.ContainerMaxItems),
            ContainerMaxWeight = Math.Max(0, source.ContainerMaxWeight),
            AllowedContentTags = allowedTags,
        };
    }

    private static List<CustomEquipmentData> AutoMigrateLibrary(List<CustomEquipmentData> source, out bool changed)
    {
        changed = false;
        var migrated = new List<CustomEquipmentData>(source.Count);
        var baseCosts = BuildBaseCatalogCostLookup();
        var supplementalWeights = BuildSupplementalWeightLookup();
        var weaponStatLookup = BuildWeaponStatLookup();

        foreach (var item in source)
        {
            var working = Normalize(item);
            bool itemChanged = false;

            // Clear descriptions that were imported with the loose plain-text fallback and are garbage.
            if (!string.IsNullOrWhiteSpace(working.Description) && LooksLikeGarbageDescription(working.Description))
            {
                working.Description = string.Empty;
                itemChanged = true;
            }

            // Recover coin values from trailing name text when cost fields are empty.
            if (working.CostGold == 0 && working.CostSilver == 0 && working.CostCopper == 0
                && TryExtractTrailingCoinCosts(working.Name, out string cleanName, out int gp, out int sp, out int cp))
            {
                working.Name = cleanName;
                working.CostGold = gp;
                working.CostSilver = sp;
                working.CostCopper = cp;
                itemChanged = true;
            }

            // Fill remaining missing costs from base catalog text where IDs align.
            if (working.CostGold == 0 && working.CostSilver == 0 && working.CostCopper == 0)
            {
                string canonicalId = CanonicalizeId(working.Id);
                if (baseCosts.TryGetValue(canonicalId, out var cost)
                    || baseCosts.TryGetValue(BuildId(working.Name), out cost))
                {
                    working.CostGold = cost.Gold;
                    working.CostSilver = cost.Silver;
                    working.CostCopper = cost.Copper;
                    itemChanged = true;
                }
            }

            if (working.Weight <= 0)
            {
                if (TryExtractWeightFromName(working.Name, out double parsedWeight))
                {
                    working.Weight = parsedWeight;
                    itemChanged = true;
                }
                else
                {
                    string nameKey = BuildNameKey(working.Name);
                    if (supplementalWeights.TryGetValue(nameKey, out double tableWeight) && tableWeight > 0)
                    {
                        working.Weight = tableWeight;
                        itemChanged = true;
                    }
                }
            }

            ApplyArmorMetadata(working, ref itemChanged);
            ForceCanonicalArmorClassification(working, ref itemChanged);
            ApplyWeaponMetadata(working, weaponStatLookup, ref itemChanged);

            string? combatCategory = DetectCombatCategory(working);
            if (!string.IsNullOrWhiteSpace(combatCategory))
            {
                var categories = working.Categories
                    .Where(c => !LooksLikeCombatCategory(c))
                    .ToList();

                if (!categories.Contains(combatCategory, StringComparer.OrdinalIgnoreCase))
                    categories.Insert(0, combatCategory);

                if (!working.Categories.SequenceEqual(categories, StringComparer.OrdinalIgnoreCase))
                {
                    working.Categories = categories;
                    itemChanged = true;
                }
            }

            migrated.Add(Normalize(working));
            if (itemChanged)
                changed = true;
        }

        // ── Weapon data cleanup ─────────────────────────────────────────────────────
        // 1. Remove stale old-parser grouped entries like "Sword - One handed" / "Polearm - Two handed".
        //    These were emitted when the section group was wrongly set to "Sword" instead of "Bastard sword".
        //    They will be re-added correctly as "Bastard sword - One-handed" etc. from book tables.
        int staleCleaned = migrated.RemoveAll(x =>
            x.IsWeapon && !x.IsArmor && StaleNoHyphenGroupedWeaponRegex.IsMatch(x.Name));

        // 2. Remove bare section-header artifacts (e.g., standalone "Bastard sword", "Trident", "Spear")
        //    that were created when the weapon section header row was mistakenly emitted as a weapon entry.
        staleCleaned += migrated.RemoveAll(x =>
            x.IsWeapon && !x.IsArmor
            && string.IsNullOrWhiteSpace(x.WeaponDamageSmallMedium)
            && string.IsNullOrWhiteSpace(x.WeaponDamageLarge)
            && x.WeaponSpeed == 0
            && string.IsNullOrWhiteSpace(x.WeaponType)
            && string.IsNullOrWhiteSpace(x.WeaponSize));

        if (staleCleaned > 0) changed = true;
        // ────────────────────────────────────────────────────────────────────────────

        foreach (var armor in BuildPhbArmorEntries())
        {
            string id = CanonicalizeId(armor.Id);
            if (migrated.Any(x => string.Equals(CanonicalizeId(x.Id), id, StringComparison.OrdinalIgnoreCase)))
                continue;
            migrated.Add(Normalize(armor));
            changed = true;
        }

        // Add named armor variants (especially "full" entries) that may not exist in source HTML.
        foreach (var armor in BuildSupplementalArmorEntries())
        {
            string nameKey = BuildNameKey(armor.Name);
            if (migrated.Any(x => string.Equals(BuildNameKey(x.Name), nameKey, StringComparison.OrdinalIgnoreCase)))
                continue;
            migrated.Add(Normalize(armor));
            changed = true;
        }

        // Add standard shields — deduplicate against PHB entries like "Shield - Buckler".
        foreach (var shield in BuildSupplementalShieldEntries())
        {
            string nameKey = BuildNameKey(shield.Name);
            bool alreadyExists = migrated.Any(x =>
                x.IsShield
                && (string.Equals(BuildNameKey(x.Name), nameKey, StringComparison.OrdinalIgnoreCase)
                    || BuildNameKey(x.Name).EndsWith(nameKey, StringComparison.OrdinalIgnoreCase)));
            if (alreadyExists)
                continue;
            migrated.Add(Normalize(shield));
            changed = true;
        }

        // Add core spellbook items so they are purchasable from the equipment catalog.
        foreach (var spellbook in BuildSupplementalSpellbookEntries())
        {
            string nameKey = BuildNameKey(spellbook.Name);
            bool alreadyExists = migrated.Any(x =>
                string.Equals(BuildNameKey(x.Name), nameKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(CanonicalizeId(x.Id), CanonicalizeId(spellbook.Id), StringComparison.OrdinalIgnoreCase));
            if (alreadyExists)
                continue;

            migrated.Add(Normalize(spellbook));
            changed = true;
        }

        // Add weapons from the AEG Master Weapons Chart (most complete weapon reference).
        foreach (var weapon in BuildWeaponEntriesFromFile(@"Core Rules\WEBHELP\AEG\DD00160.HTM"))
        {
            string nameKey = BuildNameKey(weapon.Name);
            if (migrated.Any(x => string.Equals(BuildNameKey(x.Name), nameKey, StringComparison.OrdinalIgnoreCase)))
                continue;
            bool aeWep = false;
            ApplyWeaponMetadata(weapon, weaponStatLookup, ref aeWep);
            migrated.Add(Normalize(weapon));
            changed = true;
        }

        // Add unique weapons from Player's Options: Skills & Powers (oriental / exotic weapons).
        foreach (var weapon in BuildWeaponEntriesFromFile(@"Core Rules\WEBHELP\SP\DD03173.HTM"))
        {
            string nameKey = BuildNameKey(weapon.Name);
            if (migrated.Any(x => string.Equals(BuildNameKey(x.Name), nameKey, StringComparison.OrdinalIgnoreCase)))
                continue;
            bool spWep = false;
            ApplyWeaponMetadata(weapon, weaponStatLookup, ref spWep);
            migrated.Add(Normalize(weapon));
            changed = true;
        }

        return migrated;
    }

    private static void ForceCanonicalArmorClassification(CustomEquipmentData item, ref bool changed)
    {
        if (item.IsShield)
            return;

        string nameKey = BuildNameKey(item.Name);
        foreach (var entry in CanonicalArmorNameToAc)
        {
            string armorKey = BuildNameKey(entry.Key);
            if (!nameKey.Contains(armorKey, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!item.IsArmor)
            {
                item.IsArmor = true;
                changed = true;
            }

            // Set or fix AC for canonical armor rows.
            if (item.ArmorClassValue == 10 || item.ArmorClassValue <= 0)
            {
                item.ArmorClassValue = entry.Value;
                changed = true;
            }

            string profile = InferRogueArmorProfile(item.Name, item.ArmorClassValue);
            if (!string.Equals(item.RogueArmorProfile, profile, StringComparison.OrdinalIgnoreCase))
            {
                item.RogueArmorProfile = profile;
                changed = true;
            }

            if (!item.Categories.Contains("Armor", StringComparer.OrdinalIgnoreCase))
            {
                item.Categories.Insert(0, "Armor");
                changed = true;
            }

            break;
        }
    }

    private static void ApplyArmorMetadata(CustomEquipmentData item, ref bool changed)
    {
        // Detect shields first — they are handled separately from body armor.
        bool isShieldType = ContainsAny(item.Name.ToLowerInvariant(), new[] { "shield", "buckler" });
        if (isShieldType)
        {
            if (!item.IsShield)
            {
                item.IsShield = true;
                changed = true;
            }
            if (item.IsArmor)
            {
                item.IsArmor = false;
                changed = true;
            }
            // Shields grant -1 AC bonus on top of body armor.
            if (item.ArmorClassValue != -1)
            {
                item.ArmorClassValue = -1;
                changed = true;
            }
            if (!item.Categories.Contains("Armor", StringComparer.OrdinalIgnoreCase))
            {
                item.Categories.Insert(0, "Armor");
                changed = true;
            }
            return;
        }

        bool shouldBeArmor = item.IsArmor
            || item.Categories.Contains("Armor", StringComparer.OrdinalIgnoreCase)
            || ContainsAny(item.Name, ArmorCategoryHints)
            || ContainsAny(string.Join(' ', item.ItemTags), ArmorCategoryHints);

        if (!shouldBeArmor)
            return;

        if (!item.IsArmor)
        {
            item.IsArmor = true;
            changed = true;
        }

        int inferredAc = InferArmorClassFromName(item.Name);
        if (item.ArmorClassValue == 10 && inferredAc < 10)
        {
            item.ArmorClassValue = inferredAc;
            changed = true;
        }

        string profile = InferRogueArmorProfile(item.Name, item.ArmorClassValue);
        if (!string.Equals(item.RogueArmorProfile, profile, StringComparison.OrdinalIgnoreCase))
        {
            item.RogueArmorProfile = profile;
            changed = true;
        }

        if (!item.Categories.Contains("Armor", StringComparer.OrdinalIgnoreCase))
        {
            item.Categories.Insert(0, "Armor");
            changed = true;
        }
    }

    private static void ApplyWeaponMetadata(CustomEquipmentData item, Dictionary<string, WeaponTableStat> weaponLookup, ref bool changed)
    {
        string haystack = $"{item.Name} {string.Join(' ', item.Categories)} {string.Join(' ', item.ItemTags)}";
        bool shouldBeWeapon = item.IsWeapon
            || item.Categories.Any(c => c.Contains("weapon", StringComparison.OrdinalIgnoreCase))
            || ContainsAny(haystack, MeleeWeaponHints)
            || ContainsAny(haystack, MissileWeaponHints);

        if (!shouldBeWeapon)
            return;

        if (!item.IsWeapon)
        {
            item.IsWeapon = true;
            changed = true;
        }

        // Apply stats from book tables — always overwrite combat stats so book data is always authoritative.
        // Cost and weight are only back-filled when missing (user may have corrected those).
        string nameKey = BuildNameKey(item.Name);
        if (weaponLookup.TryGetValue(nameKey, out WeaponTableStat tableStat))
        {
            if (!string.IsNullOrWhiteSpace(tableStat.DamageSmallMed)
                && !string.Equals(item.WeaponDamageSmallMedium, tableStat.DamageSmallMed, StringComparison.Ordinal))
            {
                item.WeaponDamageSmallMedium = tableStat.DamageSmallMed;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(tableStat.DamageLarge)
                && !string.Equals(item.WeaponDamageLarge, tableStat.DamageLarge, StringComparison.Ordinal))
            {
                item.WeaponDamageLarge = tableStat.DamageLarge;
                changed = true;
            }
            if (tableStat.Speed > 0 && item.WeaponSpeed != tableStat.Speed)
            {
                item.WeaponSpeed = tableStat.Speed;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(tableStat.Type)
                && !string.Equals(item.WeaponType, tableStat.Type, StringComparison.OrdinalIgnoreCase))
            {
                item.WeaponType = tableStat.Type;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(tableStat.Size)
                && !string.Equals(item.WeaponSize, tableStat.Size, StringComparison.OrdinalIgnoreCase))
            {
                item.WeaponSize = tableStat.Size;
                changed = true;
            }
            // Cost and weight: only fill when not already set.
            if (item.Weight <= 0 && tableStat.Weight > 0)
            {
                item.Weight = tableStat.Weight;
                changed = true;
            }
            if (item.CostGold == 0 && item.CostSilver == 0 && item.CostCopper == 0
                && (tableStat.CostGold > 0 || tableStat.CostSilver > 0 || tableStat.CostCopper > 0))
            {
                item.CostGold = tableStat.CostGold;
                item.CostSilver = tableStat.CostSilver;
                item.CostCopper = tableStat.CostCopper;
                changed = true;
            }
        }

        // Fall back to inference for any still-missing fields.
        if (string.IsNullOrWhiteSpace(item.WeaponSize))
        {
            item.WeaponSize = InferWeaponSize(item.SizeClass);
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(item.WeaponType))
        {
            item.WeaponType = InferWeaponType(item.Name);
            changed = true;
        }
    }

    private static string InferWeaponSize(string sizeClass)
    {
        string normalized = (sizeClass ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "tiny" or "small" => "S",
            "large" or "huge" => "L",
            "medium" => "M",
            _ => "",
        };
    }

    private static string InferWeaponType(string name)
    {
        string text = (name ?? string.Empty).ToLowerInvariant();

        if (text.Contains("mace") || text.Contains("hammer") || text.Contains("club") || text.Contains("staff")
            || text.Contains("flail") || text.Contains("morningstar") || text.Contains("blackjack"))
            return "B";

        if (text.Contains("spear") || text.Contains("arrow") || text.Contains("bolt") || text.Contains("crossbow")
            || text.Contains("bow") || text.Contains("dart") || text.Contains("javelin") || text.Contains("trident")
            || text.Contains("lance") || text.Contains("rapier"))
            return "P";

        if (text.Contains("sword") || text.Contains("axe") || text.Contains("scimitar") || text.Contains("halberd")
            || text.Contains("dagger") || text.Contains("knife") || text.Contains("katana"))
            return "S";

        return string.Empty;
    }

    private static int InferArmorClassFromName(string name)
    {
        string key = BuildNameKey(name);
        foreach (var entry in ArmorClassByName)
        {
            string entryKey = BuildNameKey(entry.Key);
            if (key.Contains(entryKey, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }
        return 10;
    }

    private static string InferRogueArmorProfile(string armorName, int armorClassValue)
    {
        string key = BuildNameKey(armorName);
        if (key.Contains("elven chain", StringComparison.OrdinalIgnoreCase))
            return "elven_chain";
        if (key.Contains("leather", StringComparison.OrdinalIgnoreCase)
            || key.Contains("padded", StringComparison.OrdinalIgnoreCase)
            || key.Contains("studded", StringComparison.OrdinalIgnoreCase)
            || key.Contains("hide", StringComparison.OrdinalIgnoreCase)
            || key.Contains("brigandine", StringComparison.OrdinalIgnoreCase))
            return "studded_leather";
        if (armorClassValue <= 7)
            return "chain_or_ring_mail";
        return "no_armor";
    }

    private static IEnumerable<CustomEquipmentData> BuildPhbArmorEntries()
    {
        string? assetsRoot = ResolveAssetsRoot();
        if (string.IsNullOrWhiteSpace(assetsRoot))
            yield break;

        string path = Path.Combine(assetsRoot, @"Core Rules\WEBHELP\PHB\DD01623.HTM");
        if (!File.Exists(path))
            yield break;

        string html;
        try
        {
            html = File.ReadAllText(path);
        }
        catch
        {
            yield break;
        }

        string section = string.Empty;
        foreach (Match rowMatch in HtmlRowRegex.Matches(html))
        {
            var cells = HtmlCellRegex.Matches(rowMatch.Groups["row"].Value)
                .Select(m => CleanHtmlCell(m.Groups["cell"].Value))
                .ToList();
            if (cells.Count < 3)
                continue;

            string name = NormalizeTableName(cells[0]);
            string costText = cells[1];
            string weightText = cells[2];

            if (IsHeaderCell(name))
                continue;

            bool hasCost = TryExtractCoinTotals(costText, out int gp, out int sp, out int cp);
            bool hasWeight = TryParseWeightCell(weightText, out double weight);

            if (!hasCost && !hasWeight)
            {
                if (!string.IsNullOrWhiteSpace(name) && (costText == "--" || weightText == "--"))
                    section = name;
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
                continue;

            string finalName = name;
            if (name.StartsWith("Great helm", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Basinet", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Body", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Buckler", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Medium", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Small", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(section))
                    finalName = $"{section} - {name}";
            }

            int ac = InferArmorClassFromName(finalName);
            if (ac >= 10 && string.Equals(section, "Shield", StringComparison.OrdinalIgnoreCase))
                ac = 9;

            yield return new CustomEquipmentData
            {
                Id = BuildId(finalName),
                Name = finalName,
                Description = "",
                IsMagical = false,
                Categories = new List<string> { "Armor" },
                ItemTags = new List<string>(),
                SizeClass = "Medium",
                Weight = hasWeight ? weight : 0,
                IsArmor = !string.Equals(section, "Shield", StringComparison.OrdinalIgnoreCase),
                IsShield = string.Equals(section, "Shield", StringComparison.OrdinalIgnoreCase),
                ArmorClassValue = string.Equals(section, "Shield", StringComparison.OrdinalIgnoreCase) ? -1 : ac,
                RogueArmorProfile = string.Equals(section, "Shield", StringComparison.OrdinalIgnoreCase)
                    ? "no_armor"
                    : InferRogueArmorProfile(finalName, ac),
                CostGold = hasCost ? gp : 0,
                CostSilver = hasCost ? sp : 0,
                CostCopper = hasCost ? cp : 0,
                IsContainer = false,
                ContainerMaxItems = 0,
                ContainerMaxWeight = 0,
                AllowedContentTags = new List<string>(),
            };
        }
    }

    private static IEnumerable<CustomEquipmentData> BuildSupplementalShieldEntries()
    {
        foreach (var name in SupplementalShieldNames)
        {
            yield return new CustomEquipmentData
            {
                Id = BuildId(name),
                Name = name,
                Description = "",
                IsMagical = false,
                Categories = new List<string> { "Armor" },
                ItemTags = new List<string>(),
                SizeClass = "Medium",
                Weight = 0,
                IsArmor = false,
                IsShield = true,
                ArmorClassValue = -1,
                RogueArmorProfile = "no_armor",
                CostGold = 0,
                CostSilver = 0,
                CostCopper = 0,
                IsContainer = false,
                ContainerMaxItems = 0,
                ContainerMaxWeight = 0,
                AllowedContentTags = new List<string>(),
            };
        }
    }

    private static IEnumerable<CustomEquipmentData> BuildSupplementalArmorEntries()
    {
        foreach (var (name, ac) in SupplementalArmorVariants)
        {
            yield return new CustomEquipmentData
            {
                Id = BuildId(name),
                Name = name,
                Description = "",
                IsMagical = false,
                Categories = new List<string> { "Armor" },
                ItemTags = new List<string>(),
                SizeClass = "Medium",
                Weight = 0,
                IsArmor = true,
                IsShield = false,
                ArmorClassValue = ac,
                RogueArmorProfile = InferRogueArmorProfile(name, ac),
                CostGold = 0,
                CostSilver = 0,
                CostCopper = 0,
                IsContainer = false,
                ContainerMaxItems = 0,
                ContainerMaxWeight = 0,
                AllowedContentTags = new List<string>(),
            };
        }
    }

    private static IEnumerable<CustomEquipmentData> BuildSupplementalSpellbookEntries()
    {
        foreach (var (id, name, costGold, weight, description) in SupplementalSpellbooks)
        {
            yield return new CustomEquipmentData
            {
                Id = id,
                Name = name,
                Description = description,
                IsMagical = false,
                Categories = new List<string> { "Books & Writing", "Arcane Gear" },
                ItemTags = new List<string> { "spellbook", "wizard", "arcane" },
                SizeClass = "Medium",
                Weight = weight,
                IsArmor = false,
                IsShield = false,
                IsWeapon = false,
                CostGold = costGold,
                CostSilver = 0,
                CostCopper = 0,
                IsContainer = false,
                ContainerMaxItems = 0,
                ContainerMaxWeight = 0,
                AllowedContentTags = new List<string>(),
            };
        }
    }

    /// <summary>Returns true when a weapon table cell value represents "no data" (dash, blank, special char).</summary>
    private static bool IsWeaponDashCell(string text)
    {
        string v = (text ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(v)
            || v == "--" || v == "-"
            || v == "\u2013" || v == "\u2014"  // en-dash, em-dash
            || v == "\u00B7"                    // middle dot used by some S&P cells
            || v == "*";
    }

    /// <summary>
    /// Strips trailing footnote markers (*, **, ***, #, ##, @) and trailing commas from weapon names.
    /// Trailing footnote digits are already handled by <see cref="NormalizeTableName"/>.
    /// </summary>
    private static string StripWeaponNameMarkers(string name)
    {
        string result = Regex.Replace(name.Trim(), @"[\*#@]+$", string.Empty).Trim();
        if (result.EndsWith(",", StringComparison.Ordinal))
            result = result[..^1].Trim();
        return result;
    }

    /// <summary>Extracts the S/M/L size letter, stripping any S&P slot-count parenthetical like "(3)".</summary>
    private static string NormalizeWeaponSize(string raw)
    {
        if (IsWeaponDashCell(raw)) return "";
        int paren = raw.IndexOf('(');
        string s = (paren >= 0 ? raw[..paren] : raw).Trim();
        return s is "S" or "M" or "L" or "T" or "H" ? s : "";
    }

    /// <summary>Returns a clean damage string or empty string for missing/special values.</summary>
    private static string NormalizeDamage(string raw)
    {
        string v = (raw ?? string.Empty).Trim();
        if (IsWeaponDashCell(v) || v == "?" || v == "*") return "";
        // Normalize special minus/dash variants to standard hyphen.
        return v.Replace('\u2013', '-').Replace('\u2212', '-').Replace('\u2014', '-');
    }

    /// <summary>Returns a clean weapon type string or empty for missing values.</summary>
    private static string NormalizeWeaponType(string raw)
    {
        if (IsWeaponDashCell(raw)) return "";
        string v = (raw ?? string.Empty).Trim();
        // Normalize em-dash/en-dash that might appear instead of a real type
        if (v == "\u2013" || v == "\u2014" || v == "\u00B7") return "";
        return v;
    }

    /// <summary>Returns true when the name clearly identifies a missile/ranged weapon.</summary>
    private static bool IsMissileWeaponName(string name)
    {
        string lower = (name ?? string.Empty).ToLowerInvariant();
        return MissileWeaponHints.Any(h => lower.Contains(h));
    }

    /// <summary>Returns true when this name is a known column-header value that should be skipped.</summary>
    private static bool IsWeaponColumnHeader(string name)
    {
        string v = (name ?? string.Empty).Trim();
        return v.Equals("Item", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Weapon", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Cost", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Weight", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Speed", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Factor", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Damage", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Size", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Type", StringComparison.OrdinalIgnoreCase)
            || v.Equals("S-M", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Large", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Sm-Med", StringComparison.OrdinalIgnoreCase)
            || v.Contains("Table", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses an 8-column weapon HTML table (Item|Cost|Weight|Size|Type|Speed|Dmg S-M|Dmg L)
    /// and yields (name, stats) pairs. Handles section headers, sub-item naming, and the S&amp;P
    /// slot-count size format like "M(5)".
    /// </summary>
    private static IEnumerable<(string Name, WeaponTableStat Stat)> ParseWeaponTableRows(string html)
    {
        string currentGroup = string.Empty;

        foreach (Match rowMatch in HtmlRowRegex.Matches(html))
        {
            var cells = HtmlCellRegex.Matches(rowMatch.Groups["row"].Value)
                .Select(m => CleanHtmlCell(m.Groups["cell"].Value))
                .ToList();

            if (cells.Count < 8)
                continue;

            // Name processing: normalize, strip footnote digits, then strip special markers.
            string name = StripWeaponNameMarkers(NormalizeTableName(cells[0]));

            if (string.IsNullOrWhiteSpace(name) || IsWeaponColumnHeader(name))
                continue;

            string costText   = cells[1];
            string weightText = cells[2];
            string sizeText   = NormalizeWeaponSize(cells[3]);
            string typeText   = NormalizeWeaponType(cells[4]);
            string speedText  = cells[5];
            string dmgSMText  = NormalizeDamage(cells[6]);
            string dmgLText   = NormalizeDamage(cells[7]);

            // A row where all non-name cells are blank/dash signals a category section header.
            bool isAllDash = IsWeaponDashCell(costText) && IsWeaponDashCell(weightText)
                && IsWeaponDashCell(speedText) && IsWeaponDashCell(dmgSMText) && IsWeaponDashCell(dmgLText);

            if (isAllDash)
            {
                currentGroup = name;
                continue;
            }

            // Sub-item handling: generic variant names get prefixed with the current section group.
            string finalName = GenericWeaponSubNames.Contains(name) && !string.IsNullOrWhiteSpace(currentGroup)
                ? $"{currentGroup} - {name}"
                : name;

            bool hasCost   = TryExtractCoinTotals(costText, out int gp, out int sp, out int cp);
            bool hasWeight = TryParseWeightCell(weightText, out double weight);
            int speed      = int.TryParse(speedText, out int parsedSpeed) ? parsedSpeed : 0;

            yield return (finalName, new WeaponTableStat(
                hasCost ? gp : 0, hasCost ? sp : 0, hasCost ? cp : 0,
                hasWeight ? weight : 0,
                sizeText, typeText, speed, dmgSMText, dmgLText));

            // If this row has metadata (speed/size/type) but no direct damage, treat it as a
            // functional parent for sub-variant rows that follow (e.g., S&P Katana, Trident
            // where damage varies by grip). Update currentGroup so 'One-handed' sub-items
            // become 'Katana - One-handed' instead of grouping under the prior section header.
            if (string.IsNullOrWhiteSpace(dmgSMText) && string.IsNullOrWhiteSpace(dmgLText)
                && (speed > 0 || !string.IsNullOrWhiteSpace(sizeText) || !string.IsNullOrWhiteSpace(typeText)))
            {
                currentGroup = finalName;
            }
        }
    }

    /// <summary>
    /// Reads the weapon table at <paramref name="relativePath"/> (relative to the Assets root)
    /// and yields <see cref="CustomEquipmentData"/> entries for every weapon row found.
    /// </summary>
    private static IEnumerable<CustomEquipmentData> BuildWeaponEntriesFromFile(string relativePath)
    {
        string? assetsRoot = ResolveAssetsRoot();
        if (string.IsNullOrWhiteSpace(assetsRoot))
            yield break;

        string path = Path.Combine(assetsRoot, relativePath);
        if (!File.Exists(path))
            yield break;

        string html;
        try { html = File.ReadAllText(path); }
        catch { yield break; }

        foreach (var (name, stat) in ParseWeaponTableRows(html))
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var categories = new List<string>
            {
                IsMissileWeaponName(name) ? "Missile Weapons" : "Melee Weapons",
            };

            yield return new CustomEquipmentData
            {
                Id = BuildId(name),
                Name = name,
                Description = "",
                IsMagical = false,
                Categories = categories,
                ItemTags = new List<string>(),
                SizeClass = "Medium",
                Weight = stat.Weight,
                IsWeapon = true,
                WeaponSpeed = stat.Speed,
                WeaponDamageSmallMedium = stat.DamageSmallMed,
                WeaponDamageLarge = stat.DamageLarge,
                WeaponType = stat.Type,
                WeaponSize = stat.Size,
                CostGold = stat.CostGold,
                CostSilver = stat.CostSilver,
                CostCopper = stat.CostCopper,
                IsContainer = false,
                ContainerMaxItems = 0,
                ContainerMaxWeight = 0,
                AllowedContentTags = new List<string>(),
            };
        }
    }

    /// <summary>
    /// Builds a lookup from weapon name key → <see cref="WeaponTableStat"/> by parsing
    /// the PHB, AEG Master Weapons Chart, and S&amp;P weapon tables. PHB entries are preferred
    /// for canonical names; AEG and S&amp;P add additional weapons. For grouped variants like
    /// "Harpoon - One-handed", the base name "Harpoon" is also registered if not already present.
    /// </summary>
    private static Dictionary<string, WeaponTableStat> BuildWeaponStatLookup()
    {
        var lookup = new Dictionary<string, WeaponTableStat>(StringComparer.OrdinalIgnoreCase);
        string? assetsRoot = ResolveAssetsRoot();
        if (string.IsNullOrWhiteSpace(assetsRoot))
            return lookup;

        string[] relativeFiles =
        {
            @"Core Rules\WEBHELP\PHB\DD01624.HTM",    // PHB weapons (canonical names)
            @"Core Rules\WEBHELP\AEG\DD00160.HTM",    // AEG Master Weapons Chart (adds extras)
            @"Core Rules\WEBHELP\SP\DD03173.HTM",     // S&P (oriental / exotic weapons)
        };

        foreach (string relFile in relativeFiles)
        {
            string path = Path.Combine(assetsRoot, relFile);
            if (!File.Exists(path))
                continue;

            string html;
            try { html = File.ReadAllText(path); }
            catch { continue; }

            foreach (var (name, stat) in ParseWeaponTableRows(html))
            {
                string key = BuildNameKey(name);
                if (!lookup.ContainsKey(key))
                    lookup[key] = stat;

                // For "Harpoon - One-handed" also register "Harpoon" so existing items get stats.
                int dashIdx = name.LastIndexOf(" - ", StringComparison.Ordinal);
                if (dashIdx > 0)
                {
                    string baseKey = BuildNameKey(name[..dashIdx]);
                    if (!lookup.ContainsKey(baseKey))
                        lookup[baseKey] = stat;
                }
            }
        }

        return lookup;
    }

    private static Dictionary<string, (int Gold, int Silver, int Copper)> BuildBaseCatalogCostLookup()
    {
        var lookup = new Dictionary<string, (int Gold, int Silver, int Copper)>(StringComparer.OrdinalIgnoreCase);
        var catalog = EquipmentCatalogService.LoadBaseCatalogFromText();

        foreach (var category in catalog.Categories)
        {
            foreach (var item in category.Items)
            {
                if (!TryExtractCoinTotals(item.CostText, out int gp, out int sp, out int cp))
                    continue;

                string id = CanonicalizeId(item.ItemId);
                if (!lookup.ContainsKey(id))
                    lookup[id] = (gp, sp, cp);

                string fallbackNameId = BuildId(item.ItemName);
                if (!lookup.ContainsKey(fallbackNameId))
                    lookup[fallbackNameId] = (gp, sp, cp);
            }
        }

        return lookup;
    }

    private static Dictionary<string, double> BuildSupplementalWeightLookup()
    {
        var lookup = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        string? assetsRoot = ResolveAssetsRoot();
        if (string.IsNullOrWhiteSpace(assetsRoot))
            return lookup;

        string[] relativeFiles =
        {
            @"Core Rules\WEBHELP\PHB\DD01624.HTM", // PHB weapons
            @"Core Rules\WEBHELP\PHB\DD01623.HTM", // PHB armor
            @"Core Rules\WEBHELP\SP\DD03173.HTM",  // S&P master weapons/equipment
            @"Core Rules\WEBHELP\SP\DD03175.HTM",  // S&P armor tables
            @"Core Rules\WEBHELP\SP\DD03176.HTM",  // S&P misc equipment
            @"Core Rules\WEBHELP\SP\DD03177.HTM",  // S&P horse/transport
            @"Core Rules\WEBHELP\SP\DD03178.HTM",  // S&P dwarven equipment
            @"Core Rules\WEBHELP\SP\DD03179.HTM",  // S&P elven equipment
            @"Core Rules\WEBHELP\SP\DD03180.HTM",  // S&P halfling equipment
            @"Core Rules\WEBHELP\SP\DD03181.HTM",  // S&P gnomish equipment
            @"Core Rules\WEBHELP\SP\DD03182.HTM",  // S&P tack/harness etc
        };

        foreach (string relative in relativeFiles)
        {
            string path = Path.Combine(assetsRoot, relative);
            if (!File.Exists(path))
                continue;

            foreach (var pair in ParseWeightRowsFromHtml(path))
            {
                if (!lookup.ContainsKey(pair.Key))
                    lookup[pair.Key] = pair.Value;
            }
        }

        return lookup;
    }

    private static IEnumerable<KeyValuePair<string, double>> ParseWeightRowsFromHtml(string filePath)
    {
        string html;
        try
        {
            html = File.ReadAllText(filePath);
        }
        catch
        {
            yield break;
        }

        foreach (Match rowMatch in HtmlRowRegex.Matches(html))
        {
            string rowHtml = rowMatch.Groups["row"].Value;
            var cells = HtmlCellRegex.Matches(rowHtml)
                .Select(m => CleanHtmlCell(m.Groups["cell"].Value))
                .ToList();

            if (cells.Count < 3)
                continue;

            string nameCell = NormalizeTableName(cells[0]);
            if (string.IsNullOrWhiteSpace(nameCell))
                continue;

            if (IsHeaderCell(nameCell))
                continue;

            if (!TryParseWeightCell(cells[2], out double pounds))
                continue;

            if (pounds <= 0)
                continue;

            string key = BuildNameKey(nameCell);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            yield return new KeyValuePair<string, double>(key, pounds);
        }
    }

    private static string CleanHtmlCell(string html)
    {
        string noTags = HtmlTagRegex.Replace(html, " ");
        string decoded = WebUtility.HtmlDecode(noTags)
            .Replace('\u00A0', ' ')
            .Replace('�', ' ')
            .Replace('\u2212', '-')   // MINUS SIGN → HYPHEN (fixes S&P 'One−handed' sub-item names)
            .Replace('?', ' ');
        return CollapseWhitespace(decoded);
    }

    private static string NormalizeTableName(string name)
    {
        string cleaned = CollapseWhitespace((name ?? string.Empty).Trim());
        cleaned = TrailingFootnoteDigitsRegex.Replace(cleaned, string.Empty).Trim();
        return CollapseWhitespace(cleaned);
    }

    private static bool IsHeaderCell(string text)
    {
        string v = (text ?? string.Empty).Trim();
        return v.Equals("Item", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Weapon", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Armor", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Shield", StringComparison.OrdinalIgnoreCase)
            || v.Equals("Cost", StringComparison.OrdinalIgnoreCase)
            || v.Contains("Table", StringComparison.OrdinalIgnoreCase)
            || v.Contains("Weight", StringComparison.OrdinalIgnoreCase)
            || v.Equals("--", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseWeightCell(string text, out double pounds)
    {
        pounds = 0;
        string v = CollapseWhitespace((text ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(v))
            return false;

        if (v == "*" || v == "--" || v == "-" || v == "\u2013" || v == "\u2014" || v == "\u00B7" || v.Contains("?"))
            return false;

        v = v.Replace("lbs.", "", StringComparison.OrdinalIgnoreCase)
             .Replace("lbs", "", StringComparison.OrdinalIgnoreCase)
             .Replace("lb.", "", StringComparison.OrdinalIgnoreCase)
             .Replace("lb", "", StringComparison.OrdinalIgnoreCase)
             .Trim();

        int paren = v.IndexOf('(');
        if (paren > 0)
            v = v[..paren].Trim();

        // Handle underscore-encoded fractions (e.g., "1_2" → 0.5) used by some HTML sources.
        v = v.Replace('_', '/');

        // Handle fraction strings like "1/2", "2/10".
        int slash = v.IndexOf('/');
        if (slash > 0 && slash < v.Length - 1)
        {
            if (double.TryParse(v[..slash].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double num)
                && double.TryParse(v[(slash + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double den)
                && den > 0)
            {
                pounds = num / den;
                return pounds > 0;
            }
        }

        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double direct) && direct > 0)
        {
            pounds = direct;
            return true;
        }

        return false;
    }

    private static bool TryExtractWeightFromName(string name, out double pounds)
    {
        pounds = 0;
        string text = CollapseWhitespace((name ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = NameUnitWeightRegex.Match(text);
        if (!match.Success)
            return false;

        if (!double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
            return false;

        string unit = match.Groups["unit"].Value.ToLowerInvariant();
        pounds = unit switch
        {
            "oz" => value / 16.0,
            _ => value,
        };

        return pounds > 0;
    }

    private static string BuildNameKey(string name)
    {
        string text = NormalizeTableName(name).ToLowerInvariant();
        text = Regex.Replace(text, @"[^a-z0-9]+", " ");
        return CollapseWhitespace(text);
    }

    private static string? ResolveAssetsRoot()
    {
        string? current = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && !string.IsNullOrWhiteSpace(current); i++)
        {
            string candidate = Path.Combine(current, "Assets");
            if (Directory.Exists(candidate))
                return candidate;
            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    private sealed record WebHelpRow(string NameKey, string Description);

    private sealed record WebHelpDocument(string Path, string Html, string PlainText, string PlainKey, IReadOnlyList<WebHelpRow> Rows);

    private static List<WebHelpDocument> LoadWebHelpDocuments()
    {
        string? assetsRoot = ResolveAssetsRoot();
        if (string.IsNullOrWhiteSpace(assetsRoot))
            return new List<WebHelpDocument>();

        string webHelpPath = Path.Combine(assetsRoot, @"Core Rules\WEBHELP");
        if (!Directory.Exists(webHelpPath))
            return new List<WebHelpDocument>();

        // Prefer files that usually contain equipment and arms details first.
        string[] priorityFragments =
        {
            "\\PHB\\",
            "\\SP\\",
            "\\AEG\\",
            "DD016",
            "DD0317",
            "DD0016",
            "equip",
            "weapon",
            "armor",
            "arms",
        };

        var files = Directory
            .EnumerateFiles(webHelpPath, "*.htm", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(webHelpPath, "*.html", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path =>
                priorityFragments.Count(fragment => path.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var docs = new List<WebHelpDocument>(files.Count);
        foreach (string file in files)
        {
            try
            {
                string html = File.ReadAllText(file);
                string plain = CollapseWhitespace(WebUtility.HtmlDecode(HtmlTagRegex.Replace(html, " ")));
                if (plain.Length < 40)
                    continue;

                var rows = ParseWebHelpRows(html);
                docs.Add(new WebHelpDocument(file, html, plain, BuildLooseLookupKey(plain), rows));
            }
            catch
            {
                // ignore unreadable files and continue import
            }
        }

        return docs;
    }

    private static bool TryFindEquipmentDescription(string equipmentName, IReadOnlyList<WebHelpDocument> docs, out string description)
    {
        description = string.Empty;
        string name = CollapseWhitespace((equipmentName ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
            return false;

        List<string> aliases = BuildDescriptionLookupAliases(name);
        var aliasKeys = aliases
            .Select(BuildLooseLookupKey)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var doc in docs)
        {
            foreach (var row in doc.Rows)
            {
                if (!aliasKeys.Contains(row.NameKey, StringComparer.Ordinal))
                    continue;

                string fromRow = TrimDescriptionCandidate(row.Description);
                if (fromRow.Length >= 16 && !LooksLikeGarbageDescription(fromRow))
                {
                    description = fromRow;
                    return true;
                }
            }

            foreach (string alias in aliases)
            {
                string escaped = Regex.Escape(alias);
                string anchorPattern = $@"(?is)<A[^>]*>\s*{escaped}\s*</A>(?<after>.{{0,1800}})";
                string linePattern = $@"(?is){escaped}\s*[:\-]\s*(?<desc>[^<\r\n]{{16,500}})";

                var anchor = Regex.Match(doc.Html, anchorPattern);
                if (anchor.Success)
                {
                    string after = WebUtility.HtmlDecode(HtmlTagRegex.Replace(anchor.Groups["after"].Value, " "));
                    string cleaned = TrimDescriptionCandidate(after);
                    if (cleaned.Length >= 16 && !LooksLikeGarbageDescription(cleaned))
                    {
                        description = cleaned;
                        return true;
                    }
                }

                var line = Regex.Match(doc.Html, linePattern);
                if (line.Success)
                {
                    string cleaned = TrimDescriptionCandidate(line.Groups["desc"].Value);
                    if (cleaned.Length >= 16 && !LooksLikeGarbageDescription(cleaned))
                    {
                        description = cleaned;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<WebHelpRow> ParseWebHelpRows(string html)
    {
        var rows = new List<WebHelpRow>();

        foreach (Match rowMatch in HtmlRowRegex.Matches(html))
        {
            string rowHtml = rowMatch.Groups["row"].Value;
            var cells = HtmlCellRegex.Matches(rowHtml)
                .Select(match => TrimDescriptionCandidate(WebUtility.HtmlDecode(HtmlTagRegex.Replace(match.Groups["cell"].Value, " "))))
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .ToList();

            if (cells.Count < 2)
                continue;

            string nameCell = cells[0];
            // Skip cells that look like table data columns (cost, weight, AC, damage) — not prose.
            string bestDescription = cells
                .Skip(1)
                .Where(c => !LooksLikeTableDataCell(c))
                .OrderByDescending(x => x.Length)
                .FirstOrDefault() ?? string.Empty;

            if (bestDescription.Length < 12)
                continue;

            string key = BuildLooseLookupKey(nameCell);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            rows.Add(new WebHelpRow(key, bestDescription));
        }

        return rows;
    }

    private static List<string> BuildDescriptionLookupAliases(string name)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CollapseWhitespace(name),
        };

        if (TryExtractTrailingCoinCosts(name, out string noCoinName, out _, out _, out _))
            aliases.Add(noCoinName);

        string noParens = Regex.Replace(name, @"\([^)]*\)", string.Empty);
        if (!string.IsNullOrWhiteSpace(noParens))
            aliases.Add(CollapseWhitespace(noParens));

        if (name.Contains(",", StringComparison.Ordinal))
        {
            foreach (string part in name.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                    aliases.Add(CollapseWhitespace(part));
            }
        }

        if (name.Contains(" - ", StringComparison.Ordinal))
        {
            foreach (string part in name.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                    aliases.Add(CollapseWhitespace(part));
            }
        }

        aliases.Add(CollapseWhitespace(name.Replace("&", " and ", StringComparison.Ordinal)));

        return aliases
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Length)
            .ToList();
    }

    private static string BuildLooseLookupKey(string value)
    {
        string text = WebUtility.HtmlDecode(value ?? string.Empty);
        text = text.ToLowerInvariant();
        text = text.Replace("&", " and ", StringComparison.Ordinal);
        text = Regex.Replace(text, @"\([^)]*\)", " ");
        text = Regex.Replace(text, @"\b(per\s+(month|week|day|hour|lb|oz|pint|quart|gallon))\b", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b\d+[\d,]*(\s*[-/]\s*\d+[\d,]*)?\s*(cp|sp|ep|gp|pp)\b", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"[^a-z0-9]+", " ");
        text = CollapseWhitespace(text);

        if (text.StartsWith("a ", StringComparison.Ordinal))
            text = text[2..].TrimStart();
        else if (text.StartsWith("an ", StringComparison.Ordinal))
            text = text[3..].TrimStart();
        else if (text.StartsWith("the ", StringComparison.Ordinal))
            text = text[4..].TrimStart();

        return text;
    }

    // Matches patterns typical of weapon/armor table data cells: coin costs, weight, AC, damage dice.
    private static readonly Regex TableDataCellRegex = new(
        @"^\s*(\d+\s*(gp|sp|cp|lb|lbs|ac)\b|\d+d\d+|\d+\s*[-–]\s*\d+|[\d]+\s*/\s*[\d]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Heuristics that identify a description string as scraper garbage rather than real prose.
    private static bool LooksLikeGarbageDescription(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        // Multiple semicolons → probably a list of unrelated items concatenated
        if (text.Count(c => c == ';') >= 3) return true;
        // Table-row pattern: digit sequences mixed with cost/size markers
        if (Regex.IsMatch(text, @"\b\d+\s+(gp|sp|cp)\b.*\b\d+\s+(gp|sp|cp)\b", RegexOptions.IgnoreCase)) return true;
        // Weapon table header residue: "Sm Med Large" or "S/M Large"
        if (Regex.IsMatch(text, @"\bSm[.\s]*Med\b|\bS/M\b.*\bLarge\b", RegexOptions.IgnoreCase)) return true;
        // Bronze/Roman era price tables (AEG doc artifacts)
        if (Regex.IsMatch(text, @"\bBronze age\b|\bRoman\b", RegexOptions.IgnoreCase)) return true;
        // Fragment of armor list
        if (Regex.IsMatch(text, @"\bImproved mail\b|\bLight scale\b", RegexOptions.IgnoreCase)) return true;
        return false;
    }

    private static bool LooksLikeTableDataCell(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (TableDataCellRegex.IsMatch(text)) return true;
        // Very short non-prose values
        if (text.Trim().Length < 8) return true;
        return false;
    }

    private static string TrimDescriptionCandidate(string value)
    {
        string text = CollapseWhitespace((value ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Remove boilerplate navigation fragments often present in WebHelp pages.
        text = Regex.Replace(text, @"\b(previous|next|top|contents|index)\b", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s*\|\s*", " ");
        text = CollapseWhitespace(text);

        // Keep one concise paragraph.
        if (text.Length > 420)
            text = text[..420].TrimEnd();

        return text.Trim(' ', '-', ':', ';', '.', ',');
    }

    private static string WriteDescriptionImportReport(int totalItems, int missingBefore, int filled, int missingAfter, IReadOnlyList<string> unresolvedTop)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            string path = Path.Combine(SettingsDirectory, "equipment_description_import_report.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total items: {totalItems}");
            sb.AppendLine($"Missing descriptions before: {missingBefore}");
            sb.AppendLine($"Descriptions filled: {filled}");
            sb.AppendLine($"Missing descriptions after: {missingAfter}");
            sb.AppendLine();
            sb.AppendLine("Unresolved item names (sample):");
            if (unresolvedTop.Count == 0)
            {
                sb.AppendLine("(none)");
            }
            else
            {
                foreach (string name in unresolvedTop)
                    sb.AppendLine($" - {name}");
            }

            File.WriteAllText(path, sb.ToString());
            return path;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryExtractCoinTotals(string? text, out int gp, out int sp, out int cp)
    {
        gp = 0;
        sp = 0;
        cp = 0;

        string working = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(working))
            return false;

        int totalCopper = 0;

        // For ranges like "100-600gp", use the lower bound as the baseline value.
        foreach (Match rangeMatch in CoinRangeRegex.Matches(working))
        {
            if (!int.TryParse(rangeMatch.Groups["low"].Value.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lowAmount))
                continue;
            if (lowAmount <= 0)
                continue;

            totalCopper += CoinToCopper(lowAmount, rangeMatch.Groups["coin"].Value);
        }

        if (CoinRangeRegex.IsMatch(working))
            working = CoinRangeRegex.Replace(working, " ");

        foreach (Match coinMatch in CoinValueRegex.Matches(working))
        {
            if (!int.TryParse(coinMatch.Groups["amount"].Value.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
                continue;
            if (amount <= 0)
                continue;

            totalCopper += CoinToCopper(amount, coinMatch.Groups["coin"].Value);
        }

        if (totalCopper <= 0)
            return false;

        CharacterWealthService.FromCopper(totalCopper, out gp, out sp, out cp);
        return true;
    }

    private static int CoinToCopper(int amount, string coin)
        => coin.Trim().ToLowerInvariant() switch
        {
            "cp" => amount,
            "sp" => amount * 10,
            "ep" => amount * 50,
            "gp" => amount * 100,
            "pp" => amount * 500,
            _ => 0,
        };

    private static bool TryExtractTrailingCoinCosts(string name, out string cleanedName, out int gp, out int sp, out int cp)
    {
        cleanedName = CollapseWhitespace((name ?? string.Empty).Trim());
        gp = 0;
        sp = 0;
        cp = 0;

        if (string.IsNullOrWhiteSpace(cleanedName))
            return false;

        var match = TrailingCoinChunkRegex.Match(cleanedName);
        if (!match.Success)
            return false;

        string baseName = CollapseWhitespace(match.Groups["base"].Value.Trim(' ', '-', ',', ';', ':', '.', '/', '\\'));
        if (string.IsNullOrWhiteSpace(baseName))
            return false;

        int totalCopper = 0;
        foreach (Match coinMatch in CoinValueRegex.Matches(match.Groups["cost"].Value))
        {
            if (!int.TryParse(coinMatch.Groups["amount"].Value.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
                continue;

            if (amount <= 0)
                continue;

            totalCopper += CoinToCopper(amount, coinMatch.Groups["coin"].Value);
        }

        if (totalCopper <= 0)
            return false;

        CharacterWealthService.FromCopper(totalCopper, out gp, out sp, out cp);
        cleanedName = baseName;
        return true;
    }

    private static string? DetectCombatCategory(CustomEquipmentData item)
    {
        string haystack = $"{item.Name} {string.Join(' ', item.Categories)} {string.Join(' ', item.ItemTags)}".ToLowerInvariant();
        if (ContainsAny(haystack, ArmorCategoryHints))
            return "Armor";
        if (ContainsAny(haystack, MissileWeaponHints))
            return "Missile Weapons";
        if (ContainsAny(haystack, MeleeWeaponHints))
            return "Melee Weapons";
        return null;
    }

    private static bool LooksLikeCombatCategory(string? category)
    {
        string text = (category ?? string.Empty).ToLowerInvariant();
        return text.Contains("weapon")
            || text.Contains("missile")
            || text.Contains("armor")
            || text.Contains("armour");
    }

    private static bool ContainsAny(string text, IEnumerable<string> terms)
        => terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    public static string BuildId(string name)
    {
        string baseName = string.IsNullOrWhiteSpace(name)
            ? "equipment_item"
            : CollapseWhitespace(name.Trim()).ToLowerInvariant();
        var chars = baseName
            .Select(c => char.IsLetterOrDigit(c) ? c : '_')
            .ToArray();
        string compact = string.Join(string.Empty, new string(chars)
            .Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(compact))
            compact = "equipment_item";
        return compact;
    }

    public static string CanonicalizeId(string? idOrName)
    {
        string text = string.IsNullOrWhiteSpace(idOrName)
            ? "equipment_item"
            : CollapseWhitespace(idOrName.Trim());
        return BuildId(text);
    }

    private static string CollapseWhitespace(string value)
        => MultiWhitespaceRegex.Replace(value, " ").Trim();

    private static string StripCategoryPrefixes(string name, IReadOnlyList<string> categories)
    {
        string cleaned = name ?? string.Empty;
        cleaned = cleaned.Trim();

        // Remove leading bracketed category labels: [Adventuring Gear] Rope
        while (cleaned.StartsWith("[", StringComparison.Ordinal))
        {
            int close = cleaned.IndexOf(']');
            if (close <= 0)
                break;
            cleaned = cleaned[(close + 1)..].TrimStart();
        }

        foreach (string category in categories)
        {
            if (string.IsNullOrWhiteSpace(category))
                continue;

            if (cleaned.StartsWith(category + " - ", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[(category.Length + 3)..].TrimStart();
                continue;
            }

            if (cleaned.StartsWith(category + ": ", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[(category.Length + 2)..].TrimStart();
                continue;
            }
        }

        return cleaned;
    }

    private static int GetSizeRank(string sizeClass)
        => (sizeClass ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "tiny" => 1,
            "small" => 2,
            "medium" => 3,
            "large" => 4,
            "huge" => 5,
            _ => 3,
        };
}

public sealed record EquipmentCleanupResult(int TotalItems, int ChangedItems);
public sealed record EquipmentDescriptionImportResult(
    int TotalItems,
    int MissingDescriptionsBefore,
    int DescriptionsFilled,
    int MissingDescriptionsAfter,
    IReadOnlyList<string> UnresolvedItemNames,
    string ReportPath);
