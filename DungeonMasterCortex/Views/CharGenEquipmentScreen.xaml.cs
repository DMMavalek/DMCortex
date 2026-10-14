using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CharGenEquipmentScreen : UserControl, IScreen
{
    private const string AllCategories = "(All Categories)";
    private const string EquippedGroupKey = "__equipped__";
    private const string EquipmentTypeAll = "All Items";
    private const string EquipmentTypeRegular = "Regular Only";
    private const string EquipmentTypeMagical = "Magical Only";
    private const string ComponentFilterAll = "All Spell Components";
    private const string ComponentFilterNeededOnly = "Needed Only";
    private const string ComponentFilterNotNeededOnly = "Not Needed Only";

    private readonly MainWindow _app;
    private EquipmentCatalog _catalog = new(Array.Empty<EquipmentCategory>(), "");
    private Dictionary<string, CustomEquipmentData> _libraryById = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _componentUsageFullByNormalized = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _componentUsageConciseByNormalized = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, int[]> WizardSpellLimitsByCharacterLevel = new()
    {
        [1] = new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 },
        [2] = new[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 },
        [3] = new[] { 2, 1, 0, 0, 0, 0, 0, 0, 0 },
        [4] = new[] { 3, 2, 0, 0, 0, 0, 0, 0, 0 },
        [5] = new[] { 4, 2, 1, 0, 0, 0, 0, 0, 0 },
        [6] = new[] { 4, 2, 2, 0, 0, 0, 0, 0, 0 },
        [7] = new[] { 4, 3, 2, 1, 0, 0, 0, 0, 0 },
        [8] = new[] { 4, 3, 3, 2, 0, 0, 0, 0, 0 },
        [9] = new[] { 4, 3, 3, 2, 1, 0, 0, 0, 0 },
        [10] = new[] { 4, 4, 3, 2, 2, 0, 0, 0, 0 },
        [11] = new[] { 4, 4, 4, 3, 3, 0, 0, 0, 0 },
        [12] = new[] { 4, 4, 4, 4, 4, 1, 0, 0, 0 },
        [13] = new[] { 5, 5, 5, 4, 4, 2, 0, 0, 0 },
        [14] = new[] { 5, 5, 5, 4, 4, 2, 1, 0, 0 },
        [15] = new[] { 5, 5, 5, 5, 5, 2, 1, 0, 0 },
        [16] = new[] { 5, 5, 5, 5, 5, 3, 2, 1, 0 },
        [17] = new[] { 5, 5, 5, 5, 5, 3, 3, 2, 0 },
        [18] = new[] { 5, 5, 5, 5, 5, 3, 3, 2, 1 },
        [19] = new[] { 5, 5, 5, 5, 5, 3, 3, 3, 1 },
        [20] = new[] { 5, 5, 5, 5, 5, 4, 3, 3, 2 },
    };

    private static readonly Dictionary<string, string> WizardSchoolAccessByAbilityId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wizard_school_abjuration"] = "Abjuration",
        ["wizard_school_alchemy"] = "Alchemy",
        ["wizard_school_alteration"] = "Alteration",
        ["wizard_school_artifice"] = "Artifice",
        ["wizard_school_conjuration_summoning"] = "Conjuration/Summoning",
        ["wizard_school_dimensional"] = "Dimensional",
        ["wizard_school_divination"] = "Divination",
        ["wizard_school_elemental_air"] = "Elemental (Air)",
        ["wizard_school_elemental_earth"] = "Elemental (Earth)",
        ["wizard_school_elemental_fire"] = "Elemental (Fire)",
        ["wizard_school_elemental_water"] = "Elemental (Water)",
        ["wizard_school_enchantment_charm"] = "Enchantment/Charm",
        ["wizard_school_force"] = "Force",
        ["wizard_school_geometry"] = "Geometry",
        ["wizard_school_illusion"] = "Illusion",
        ["wizard_school_invocation_evocation"] = "Invocation/Evocation",
        ["wizard_school_necromancy"] = "Necromancy",
        ["wizard_school_shadow"] = "Shadow",
        ["wizard_school_song"] = "Song",
        ["wizard_school_wild"] = "Wild Magic",
    };
    private static readonly Dictionary<string, Dictionary<string, bool>> ExpansionStateByContext = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> MagicalSlotLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ring"] = 2,
        ["armor"] = 1,
        ["helmet"] = 1,
        ["cloak"] = 1,
        ["belt"] = 1,
        ["boots"] = 1,
        ["bracers"] = 1,
        ["amulet"] = 1,
        ["gloves"] = 1,
        ["robe"] = 1,
        ["other"] = 1,
    };
    private bool _suppressExpansionStateEvents;

    private enum ArmorTier
    {
        None = 0,
        Light = 1,
        Medium = 2,
        Heavy = 3,
    }

    private sealed record ArmorUsageRules(ArmorTier MaxArmorTier, bool CanUseShield, string Source);

    private record AvailableRow(EquipmentCatalogItem Item, string Label, string CostText, string Description, string TooltipDescription, Brush LabelBrush, bool IsSpellComponentItem, bool IsRequiredSpellComponent);
    private record SelectedRow(EquipmentSelection? Selection, string Label, string QuantityText, string CostText, string Description, string TooltipDescription, string SlotIcon, bool IsGem, string GroupName);

    public UIElement View => this;

    public CharGenEquipmentScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.SetBanner(_app.CharGen.IsLevelUpMode
            ? "Character Section  ›  Level Up  ›  Equipment"
            : "Character Blueprint  ›  Equipment");

        bool isPO = _app.CharGen.CharacterMode == "players_option";
        bool isWizardPO = isPO && string.Equals(_app.CharGen.ClassId, "wizard", StringComparison.OrdinalIgnoreCase);
        bool hasWizardSpecs = _app.Rules.Classes.TryGetValue("wizard", out var wc) && wc.Specializations is { Count: > 0 };
        int baseStepTotal = isPO
            ? (isWizardPO && hasWizardSpecs ? 13 : 12)
            : 9;
        bool hasWizardStep = IsWizardCasterInCharGen();
        int stepTotal = hasWizardStep ? baseStepTotal + 1 : baseStepTotal;
        int stepCurrent = hasWizardStep ? stepTotal - 2 : stepTotal - 1;

        _app.SetNavBar(stepCurrent, stepTotal, "Equipment",
            backAction: () => _app.GoTo("chargen_weapon_prof", -1),
            nextAction: () => _app.GoTo(hasWizardStep ? "chargen_wizard_spells" : "chargen_review"));

        _catalog = new EquipmentCatalogService().GetCatalog();
        string? magicalImportNote = LoadEquipmentLibraryWithMagicalFallback();
        if (string.IsNullOrWhiteSpace(_app.CharGen.EquippedMainWeaponId) && !string.IsNullOrWhiteSpace(_app.CharGen.EquippedWeaponId))
            _app.CharGen.EquippedMainWeaponId = _app.CharGen.EquippedWeaponId;
        if (string.IsNullOrWhiteSpace(_app.CharGen.EquippedWeaponId) && !string.IsNullOrWhiteSpace(_app.CharGen.EquippedMainWeaponId))
            _app.CharGen.EquippedWeaponId = _app.CharGen.EquippedMainWeaponId;
        EnsureStartingFundsForCharGen();
        PopulateCategoryFilter();
        PopulateEquipmentTypeFilter();
        PopulateSpellComponentFilter();
        SyncSpellComponentToggleForCurrentClass();
        RefreshLists();
        SetTransactionStatus("Last transaction: none yet.", Brushes.Gray);

        string importStatus = string.IsNullOrWhiteSpace(_catalog.LoadMessage)
            ? "Tables XXXI (Weapons) and XXXII (Armor) are intentionally excluded for dedicated mechanics."
            : _catalog.LoadMessage;

        if (!string.IsNullOrWhiteSpace(magicalImportNote))
            importStatus = $"{importStatus}\n{magicalImportNote}";

        ImportStatusText.Text = importStatus;
    }

    private string? LoadEquipmentLibraryWithMagicalFallback()
    {
        var libraryService = new EquipmentLibraryService();
        var library = libraryService.GetEquipmentLibrary().ToList();

        if (library.Any(x => x.IsMagical))
        {
            SetLibraryLookup(library);
            return null;
        }

        string? workbookPath = FindMagicItemsWorkbookPath();
        if (string.IsNullOrWhiteSpace(workbookPath))
        {
            SetLibraryLookup(library);
            return "Magical workbook not found; magical items were not auto-imported.";
        }

        try
        {
            var importResult = new MagicalItemImportService().ImportFromWorkbook(workbookPath);
            library = libraryService.GetEquipmentLibrary().ToList();
            SetLibraryLookup(library);
            return $"Auto-imported magical items from {Path.GetFileName(workbookPath)} (imported {importResult.ImportedRows}, created {importResult.CreatedRows}, updated {importResult.UpdatedRows}).";
        }
        catch (Exception ex)
        {
            SetLibraryLookup(library);
            return $"Magical auto-import failed: {ex.Message}";
        }
    }

    private void SetLibraryLookup(IEnumerable<CustomEquipmentData> library)
    {
        _libraryById = library
            .GroupBy(x => EquipmentLibraryService.CanonicalizeId(x.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindMagicItemsWorkbookPath()
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
                     .Where(p => !string.IsNullOrWhiteSpace(p))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dir = new DirectoryInfo(startPath);
            while (dir is not null)
            {
                string candidate = Path.Combine(dir.FullName, "Assets", "Magical Items", "Magic Items.xlsx");
                if (File.Exists(candidate))
                    return candidate;

                dir = dir.Parent;
            }
        }

        return null;
    }

    private void SetTransactionStatus(string message, Brush? color = null)
    {
        if (TransactionStatusText is null)
            return;

        TransactionStatusText.Text = message;
        TransactionStatusText.Foreground = color ?? (Brush)FindResource("BrushLightGold");
    }

    private string GetExpansionContextKey()
    {
        if (_app.CharGen.IsLevelUpMode
            && _app.CharGen.LevelUpCharacterIndex >= 0
            && _app.CharGen.LevelUpCharacterIndex < _app.Characters.Count)
        {
            var c = _app.Characters[_app.CharGen.LevelUpCharacterIndex];
            string name = string.IsNullOrWhiteSpace(c.Name) ? "unknown" : c.Name.Trim();
            return $"levelup|{_app.CharGen.LevelUpCharacterIndex}|{name}";
        }

        string charName = string.IsNullOrWhiteSpace(_app.CharGen.Name) ? "new" : _app.CharGen.Name.Trim();
        string classId = string.IsNullOrWhiteSpace(_app.CharGen.ClassId) ? "unknown-class" : _app.CharGen.ClassId.Trim();
        return $"chargen|{charName}|{classId}";
    }

    private Dictionary<string, bool> GetOrCreateExpansionStateMap()
    {
        string key = GetExpansionContextKey();
        if (!ExpansionStateByContext.TryGetValue(key, out var stateMap))
        {
            stateMap = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            ExpansionStateByContext[key] = stateMap;
        }

        return stateMap;
    }

    private bool GetExpandedState(string groupKey, bool fallback = true)
    {
        var stateMap = GetOrCreateExpansionStateMap();
        return stateMap.TryGetValue(groupKey, out bool value) ? value : fallback;
    }

    private void SaveExpandedState(string groupKey, bool isExpanded)
    {
        var stateMap = GetOrCreateExpansionStateMap();
        stateMap[groupKey] = isExpanded;
    }

    private void EquippedExpander_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander)
            return;

        _suppressExpansionStateEvents = true;
        expander.IsExpanded = GetExpandedState(EquippedGroupKey, fallback: true);
        _suppressExpansionStateEvents = false;
    }

    private void EquippedExpander_Expanded(object sender, RoutedEventArgs e)
    {
        if (_suppressExpansionStateEvents)
            return;

        SaveExpandedState(EquippedGroupKey, true);
    }

    private void EquippedExpander_Collapsed(object sender, RoutedEventArgs e)
    {
        if (_suppressExpansionStateEvents)
            return;

        SaveExpandedState(EquippedGroupKey, false);
    }

    private void SelectedGroupExpander_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander)
            return;

        string groupKey = (expander.Tag as string)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(groupKey))
            return;

        _suppressExpansionStateEvents = true;
        expander.IsExpanded = GetExpandedState(groupKey, fallback: true);
        _suppressExpansionStateEvents = false;
    }

    private void SelectedGroupExpander_Expanded(object sender, RoutedEventArgs e)
    {
        if (_suppressExpansionStateEvents || sender is not Expander expander)
            return;

        string groupKey = (expander.Tag as string)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(groupKey))
            return;

        SaveExpandedState(groupKey, true);
    }

    private void SelectedGroupExpander_Collapsed(object sender, RoutedEventArgs e)
    {
        if (_suppressExpansionStateEvents || sender is not Expander expander)
            return;

        string groupKey = (expander.Tag as string)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(groupKey))
            return;

        SaveExpandedState(groupKey, false);
    }

    private void PopulateCategoryFilter()
    {
        var categories = new List<string> { AllCategories };
        categories.AddRange(_libraryById.Values
            .SelectMany(x => x.Categories)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(EquipmentLibraryService.NormalizeCategoryLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        CategoryFilter.ItemsSource = categories;
        CategoryFilter.SelectedIndex = 0;
    }

    private void PopulateEquipmentTypeFilter()
    {
        EquipmentTypeFilter.ItemsSource = new[]
        {
            EquipmentTypeAll,
            EquipmentTypeRegular,
            EquipmentTypeMagical,
        };
        EquipmentTypeFilter.SelectedItem = EquipmentTypeAll;
    }

    private void PopulateSpellComponentFilter()
    {
        SpellComponentNeedFilter.ItemsSource = new[]
        {
            ComponentFilterAll,
            ComponentFilterNeededOnly,
            ComponentFilterNotNeededOnly,
        };
        SpellComponentNeedFilter.SelectedItem = ComponentFilterAll;
    }

    private void SyncSpellComponentToggleForCurrentClass()
    {
        bool canUse = SupportsSpellComponentsForCurrentClass();
        if (!canUse)
            _app.CharGen.UseSpellComponents = false;

        if (UseSpellComponentsToggle is not null)
        {
            UseSpellComponentsToggle.Visibility = canUse ? Visibility.Visible : Visibility.Collapsed;
            UseSpellComponentsToggle.IsChecked = canUse && _app.CharGen.UseSpellComponents;
        }

        if (SpellComponentNeedFilter is not null)
        {
            SpellComponentNeedFilter.Visibility = canUse && _app.CharGen.UseSpellComponents
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private bool SupportsSpellComponentsForCurrentClass()
    {
        var classIds = _app.CharGen.SelectedClassIds.Count > 0
            ? _app.CharGen.SelectedClassIds
            : string.IsNullOrWhiteSpace(_app.CharGen.ClassId)
                ? new List<string>()
                : new List<string> { _app.CharGen.ClassId };

        return classIds.Any(IsSpellComponentClassId);
    }

    private static bool IsSpellComponentClassId(string classId)
    {
        string token = (classId ?? string.Empty).Trim();
        return token.Equals("wizard", StringComparison.OrdinalIgnoreCase)
            || token.Equals("mage", StringComparison.OrdinalIgnoreCase)
            || token.Equals("illusionist", StringComparison.OrdinalIgnoreCase)
            || token.Equals("priest", StringComparison.OrdinalIgnoreCase)
            || token.Equals("cleric", StringComparison.OrdinalIgnoreCase)
            || token.Equals("druid", StringComparison.OrdinalIgnoreCase)
            || token.Equals("paladin", StringComparison.OrdinalIgnoreCase)
            || token.Equals("ranger", StringComparison.OrdinalIgnoreCase)
            || token.Equals("bard", StringComparison.OrdinalIgnoreCase)
            || RulesEngine.IsPriestClassId(token);
    }

    private void RefreshLists()
    {
        EnforceEquippedRestrictions();

        string category = CategoryFilter.SelectedItem as string ?? AllCategories;
        string search = EquipmentSearchBox?.Text.Trim() ?? string.Empty;
        string equipmentType = EquipmentTypeFilter?.SelectedItem as string ?? EquipmentTypeAll;
        string componentFilter = SpellComponentNeedFilter?.SelectedItem as string ?? ComponentFilterAll;
        bool usingSpellComponents = _app.CharGen.UseSpellComponents && SupportsSpellComponentsForCurrentClass();
        RebuildSpellComponentUsageCaches(usingSpellComponents);
        // "Needed" = only the character's actually chosen/known spells (drives filter + highlight)
        var neededComponents = BuildNeededMaterialComponentNames(knownSpellsOnly: true);
        int neededCount = neededComponents.Count;
        var availableSource = BuildAvailableItemsFromLibrary();
        if (usingSpellComponents)
        {
            // For catalog injection also include all accessible-by-school spells so components
            // that could be needed are available to browse, but the "needed" set stays tight.
            var catalogComponents = BuildNeededMaterialComponentNames(knownSpellsOnly: false);
            EnsureNeededSpellComponentsInAvailableList(availableSource, catalogComponents);
        }
        var selectedById = _app.CharGen.SelectedEquipment
            .GroupBy(x => EquipmentLibraryService.CanonicalizeId(x.ItemId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(s => Math.Max(1, s.Quantity)), StringComparer.OrdinalIgnoreCase);

        var available = availableSource
            .Where(i => category == AllCategories || category.Equals(i.Category, StringComparison.OrdinalIgnoreCase))
            .Where(i =>
            {
                if (string.Equals(equipmentType, EquipmentTypeAll, StringComparison.OrdinalIgnoreCase))
                    return true;

                _libraryById.TryGetValue(EquipmentLibraryService.CanonicalizeId(i.ItemId), out var detail);
                bool isMagical = detail?.IsMagical == true;
                if (string.Equals(equipmentType, EquipmentTypeMagical, StringComparison.OrdinalIgnoreCase))
                    return isMagical;
                return !isMagical;
            })
            .Where(i => string.IsNullOrWhiteSpace(search)
                || i.ItemName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || i.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
                || i.CostText.Contains(search, StringComparison.OrdinalIgnoreCase)
                || BuildCatalogItemDescription(i).Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(i =>
            {
                if (!usingSpellComponents)
                    return true;

                bool isSpellComponent = IsSpellComponentsCategory(i.Category);
                if (!isSpellComponent)
                    return true;

                bool required = IsRequiredSpellComponentItem(i, neededComponents);
                return componentFilter switch
                {
                    ComponentFilterNeededOnly => required,
                    ComponentFilterNotNeededOnly => !required,
                    _ => true,
                };
            })
            .Where(i => CanUseCatalogItem(i, out _))
            .OrderBy(i => i.Category)
            .ThenBy(i => NormalizeSwordName(i.ItemName), StringComparer.OrdinalIgnoreCase)
            .Select(i =>
            {
                string qtySuffix = selectedById.TryGetValue(EquipmentLibraryService.CanonicalizeId(i.ItemId), out int qty) ? $"  (owned x{qty})" : string.Empty;
                bool isSpellComponent = IsSpellComponentsCategory(i.Category);
                bool required = usingSpellComponents && isSpellComponent && IsRequiredSpellComponentItem(i, neededComponents);
                Brush labelBrush = usingSpellComponents && isSpellComponent
                    ? (required ? Brushes.Black : Brushes.IndianRed)
                    : (Brush)FindResource("BrushText");
                return new AvailableRow(i, $"{NormalizeSwordName(i.ItemName)}{qtySuffix}", i.CostText, BuildCatalogItemDescription(i), BuildCatalogItemTooltipDescription(i), labelBrush, isSpellComponent, required);
            })
            .ToList();

        var selected = _app.CharGen.SelectedEquipment
            .OrderBy(x => x.Category)
            .ThenBy(x => NormalizeSwordName(x.ItemName), StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                string canonId = EquipmentLibraryService.CanonicalizeId(x.ItemId);
                string slotIcon = string.Empty;
                if (string.Equals(EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedArmorId), canonId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_app.CharGen.EquippedArmorId))
                    slotIcon = "[A]";
                else if (string.Equals(EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedShieldId), canonId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_app.CharGen.EquippedShieldId))
                    slotIcon = "[S]";
                else
                {
                    bool isMain = string.Equals(EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedMainWeaponId), canonId, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(_app.CharGen.EquippedMainWeaponId);
                    bool isOff = string.Equals(EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedOffHandWeaponId), canonId, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(_app.CharGen.EquippedOffHandWeaponId);
                    if (isMain || isOff)
                    {
                        slotIcon = isMain && isOff ? "[M/O]" : isMain ? "[M]" : "[O]";
                    }
                    else
                    {
                        _libraryById.TryGetValue(canonId, out var detail);
                        string magicalSlot = ResolveMagicalSlotKey(detail, x);
                        bool magicalEquipped = !string.IsNullOrWhiteSpace(magicalSlot) && IsMagicalItemEquipped(canonId);
                        if (magicalEquipped)
                            slotIcon = $"[{magicalSlot.ToUpperInvariant()}]";
                    }
                }
                return new SelectedRow(
                    x,
                    NormalizeSwordName(x.ItemName),
                    $"x{Math.Max(1, x.Quantity)}",
                    x.CostText,
                    BuildSelectedItemDescription(x),
                    BuildSelectedItemTooltipDescription(x),
                    slotIcon,
                    false,
                        string.IsNullOrWhiteSpace(x.Category) ? "Other" : EquipmentLibraryService.NormalizeCategoryLabel(x.Category));
            })
            .ToList();

        var gemRows = GetCurrentGemEntries()
            .SelectMany(ExpandGemEntry)
            .Select(gem =>
            {
                int value = Math.Max(0, gem.ValueGoldPieces);
                string name = string.IsNullOrWhiteSpace(gem.Name) ? "Gem" : gem.Name.Trim();
                return new SelectedRow(
                    null,
                    name,
                    string.Empty,
                    $"{value} gp",
                    "Gem item",
                    "Gem item",
                    "[G]",
                    true,
                    "Gems");
            })
            .ToList();

        selected.AddRange(gemRows);

        AvailableList.ItemsSource = available;
        var selectedView = CollectionViewSource.GetDefaultView(selected);
        if (selectedView is not null)
        {
            selectedView.SortDescriptions.Clear();
            selectedView.SortDescriptions.Add(new SortDescription(nameof(SelectedRow.GroupName), ListSortDirection.Ascending));
            selectedView.SortDescriptions.Add(new SortDescription(nameof(SelectedRow.Label), ListSortDirection.Ascending));
            selectedView.GroupDescriptions.Clear();
            selectedView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SelectedRow.GroupName)));
            SelectedList.ItemsSource = selectedView;
        }
        else
        {
            SelectedList.ItemsSource = selected;
        }
        RefreshFundsSummary();
        RefreshEquippedDisplay();
        RefreshSpellComponentStatus(neededCount);
    }

    private void RefreshSpellComponentStatus(int neededCount)
    {
        bool canUse = SupportsSpellComponentsForCurrentClass();
        bool usingSpellComponents = canUse && _app.CharGen.UseSpellComponents;

        if (SpellComponentStatusText is not null)
        {
            SpellComponentStatusText.Visibility = canUse ? Visibility.Visible : Visibility.Collapsed;
            if (!canUse)
            {
                SpellComponentStatusText.Text = string.Empty;
            }
            else if (!usingSpellComponents)
            {
                SpellComponentStatusText.Text = "Spell components are OFF for this character.";
                SpellComponentStatusText.Foreground = (Brush)FindResource("BrushDim");
            }
            else if (neededCount == 0)
            {
                SpellComponentStatusText.Text = "Spell components are ON. No learned/material spell requirements found yet.";
                SpellComponentStatusText.Foreground = (Brush)FindResource("BrushDim");
            }
            else
            {
                SpellComponentStatusText.Text = $"Spell components are ON. Needed materials identified: {neededCount}. Black = needed, red = not currently needed.";
                SpellComponentStatusText.Foreground = (Brush)FindResource("BrushText");
            }
        }

        if (SpellComponentNeedFilter is not null)
            SpellComponentNeedFilter.Visibility = usingSpellComponents ? Visibility.Visible : Visibility.Collapsed;
    }

    private HashSet<string> BuildNeededMaterialComponentNames(bool knownSpellsOnly = false)
    {
        var spellIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in _app.CharGen.WizardSpellbookIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                spellIds.Add(id.Trim());
        }

        foreach (var list in _app.CharGen.WizardSpellLists)
        {
            foreach (string id in list?.SpellIds ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(id))
                    spellIds.Add(id.Trim());
            }
        }

        // When knownSpellsOnly=true, skip school-based expansion so the "needed" filter
        // only highlights components for spells the character has actually chosen.
        // For priest/bard who have no pre-selected spell list, always include accessible spells
        // so they still see relevant components.
        if (!knownSpellsOnly)
        {
            AddClassAccessibleSpellIdsForComponents(spellIds);
        }
        else
        {
            // Still include priest/bard spells (they choose at cast time, not in advance)
            AddPriestAndBardSpellIdsForComponents(spellIds);
        }

        var materials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in spellIds)
        {
            var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (spell is null)
                continue;

            foreach (var material in ExtractMaterialComponents(spell.Materials))
            {
                string normalized = NormalizeComponentName(material);
                if (!string.IsNullOrWhiteSpace(normalized))
                    materials.Add(normalized);
            }

            foreach (var component in ExtractComponentColumnItems(spell.Components))
            {
                string normalized = NormalizeComponentName(component);
                if (!string.IsNullOrWhiteSpace(normalized))
                    materials.Add(normalized);
            }
        }

        return materials;
    }

    private void EnsureNeededSpellComponentsInAvailableList(List<EquipmentCatalogItem> availableSource, HashSet<string> neededComponents)
    {
        if (neededComponents.Count == 0)
            return;

        var knownSpellComponents = availableSource
            .Where(item => IsSpellComponentsCategory(item.Category))
            .Select(item => NormalizeComponentName(item.ItemName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string needed in neededComponents)
        {
            if (string.IsNullOrWhiteSpace(needed) || knownSpellComponents.Contains(needed))
                continue;

            string baseId = "component_auto_" + Regex.Replace(needed.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(baseId) || baseId == "component_auto_")
                baseId = "component_auto_misc";

            string id = baseId;
            int suffix = 2;
            while (availableSource.Any(item => string.Equals(item.ItemId, id, StringComparison.OrdinalIgnoreCase)))
            {
                id = $"{baseId}_{suffix}";
                suffix++;
            }

            availableSource.Add(new EquipmentCatalogItem(
                id,
                "Spell Components",
                "AUTO",
                ToDisplayNameFromComponent(needed),
                "0 gp"));

            knownSpellComponents.Add(needed);
        }
    }

    private static string ToDisplayNameFromComponent(string normalizedComponent)
    {
        string text = (normalizedComponent ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return "Unknown Component";

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.Replace('-', ' '));
    }

    private void AddClassAccessibleSpellIdsForComponents(HashSet<string> spellIds)
    {
        var classIds = _app.CharGen.SelectedClassIds.Count > 0
            ? _app.CharGen.SelectedClassIds
            : string.IsNullOrWhiteSpace(_app.CharGen.ClassId)
                ? new List<string>()
                : new List<string> { _app.CharGen.ClassId };

        bool hasDivineCaster = classIds.Any(IsPriestLikeClassId);
        bool hasBard = classIds.Any(id => string.Equals((id ?? string.Empty).Trim(), "bard", StringComparison.OrdinalIgnoreCase));
        bool hasWizardCaster = classIds.Any(IsWizardClassId);

        if (hasWizardCaster)
            AddWizardAccessibleSpellIdsForComponents(spellIds);

        if (hasDivineCaster)
        {
            var sphereAccess = GatherCharGenSphereAccess();
            foreach (var spell in _app.Rules.Spells.Where(IsDivineSpell))
            {
                if (!IsDivineSpellAllowedForCharGen(spell, sphereAccess))
                    continue;

                if (!string.IsNullOrWhiteSpace(spell.Id))
                    spellIds.Add(spell.Id.Trim());
            }
        }

        if (hasBard)
        {
            int maxBardSpellLevel = GetEstimatedBardArcaneMaxSpellLevel(Math.Max(1, _app.CharGen.CharacterLevel));
            if (maxBardSpellLevel > 0)
            {
                foreach (var spell in _app.Rules.Spells.Where(IsArcaneSpell))
                {
                    if (ParseSpellLevelSafe(spell.Level) <= maxBardSpellLevel
                        && !string.IsNullOrWhiteSpace(spell.Id))
                    {
                        spellIds.Add(spell.Id.Trim());
                    }
                }
            }
        }
    }

    private void AddPriestAndBardSpellIdsForComponents(HashSet<string> spellIds)
    {
        var classIds = _app.CharGen.SelectedClassIds.Count > 0
            ? _app.CharGen.SelectedClassIds
            : string.IsNullOrWhiteSpace(_app.CharGen.ClassId)
                ? new List<string>()
                : new List<string> { _app.CharGen.ClassId };

        bool hasDivineCaster = classIds.Any(IsPriestLikeClassId);
        bool hasBard = classIds.Any(id => string.Equals((id ?? string.Empty).Trim(), "bard", StringComparison.OrdinalIgnoreCase));

        if (hasDivineCaster)
        {
            var sphereAccess = GatherCharGenSphereAccess();
            foreach (var spell in _app.Rules.Spells.Where(IsDivineSpell))
            {
                if (!IsDivineSpellAllowedForCharGen(spell, sphereAccess))
                    continue;
                if (!string.IsNullOrWhiteSpace(spell.Id))
                    spellIds.Add(spell.Id.Trim());
            }
        }

        if (hasBard)
        {
            int maxBardSpellLevel = GetEstimatedBardArcaneMaxSpellLevel(Math.Max(1, _app.CharGen.CharacterLevel));
            if (maxBardSpellLevel > 0)
            {
                foreach (var spell in _app.Rules.Spells.Where(IsArcaneSpell))
                {
                    if (ParseSpellLevelSafe(spell.Level) <= maxBardSpellLevel
                        && !string.IsNullOrWhiteSpace(spell.Id))
                    {
                        spellIds.Add(spell.Id.Trim());
                    }
                }
            }
        }
    }

    private void AddWizardAccessibleSpellIdsForComponents(HashSet<string> spellIds)
    {
        var allowedSchools = GatherCharGenWizardAllowedSchools();
        var blockedSchools = GatherCharGenWizardBlockedSchools();
        int maxCastableLevel = GetHighestWizardCastableSpellLevel();
        if (maxCastableLevel <= 0)
            return;

        foreach (var spell in _app.Rules.Spells.Where(IsArcaneSpell))
        {
            if (ParseSpellLevelSafe(spell.Level) > maxCastableLevel)
                continue;

            if (!IsWizardSpellAllowedForComponents(spell, allowedSchools, blockedSchools))
                continue;

            if (!string.IsNullOrWhiteSpace(spell.Id))
                spellIds.Add(spell.Id.Trim());
        }
    }

    private HashSet<string> GatherCharGenWizardAllowedSchools()
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (school, isSelected) in _app.CharGen.SelectedWizardSchools)
        {
            if (!isSelected)
                continue;

            string normalized = NormalizeWizardSchoolName(school);
            if (!string.IsNullOrWhiteSpace(normalized))
                selected.Add(normalized);
        }

        foreach (string classId in GetWizardClassIdsInCharGen())
        {
            if (!_app.CharGen.SchoolsByClass.TryGetValue(classId, out var classSchools))
                continue;

            foreach (var (school, isSelected) in classSchools)
            {
                if (!isSelected)
                    continue;

                string normalized = NormalizeWizardSchoolName(school);
                if (!string.IsNullOrWhiteSpace(normalized))
                    selected.Add(normalized);
            }
        }

        AddWizardSchoolsFromClassAbilities(selected);
        return selected;
    }

    private void AddWizardSchoolsFromClassAbilities(HashSet<string> selected)
    {
        void CollectFromSelections(IEnumerable<string> entries)
        {
            foreach (string selectionEntry in entries)
            {
                string baseId = RulesEngine.ExtractClassAbilityBaseId(selectionEntry ?? string.Empty);
                if (!WizardSchoolAccessByAbilityId.TryGetValue(baseId, out string? school)
                    || string.IsNullOrWhiteSpace(school))
                {
                    continue;
                }

                string normalized = NormalizeWizardSchoolName(school);
                if (!string.IsNullOrWhiteSpace(normalized))
                    selected.Add(normalized);
            }
        }

        CollectFromSelections(_app.CharGen.SelectedClassAbilityIds);
        foreach (var selectionsByClass in _app.CharGen.SelectedAbilitiesByClass.Values)
            CollectFromSelections(selectionsByClass);
    }

    private HashSet<string> GatherCharGenWizardBlockedSchools()
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!_app.Rules.Classes.TryGetValue("wizard", out var wizardClass) || wizardClass.Specializations is null)
            return blocked;

        var specializationIds = new List<string>();
        if (!string.IsNullOrWhiteSpace(_app.CharGen.WizardSpecializationId))
            specializationIds.Add(_app.CharGen.WizardSpecializationId);

        foreach (string classId in GetWizardClassIdsInCharGen())
        {
            if (_app.CharGen.WizardSpecializationById.TryGetValue(classId, out var id)
                && !string.IsNullOrWhiteSpace(id))
            {
                specializationIds.Add(id);
            }
        }

        foreach (string specId in specializationIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var spec = wizardClass.Specializations.FirstOrDefault(s =>
                string.Equals(s.Id, specId, StringComparison.OrdinalIgnoreCase));
            if (spec is null)
                continue;

            foreach (string school in spec.OppositionSchools)
            {
                string normalized = NormalizeWizardSchoolName(school);
                if (!string.IsNullOrWhiteSpace(normalized))
                    blocked.Add(normalized);
            }
        }

        return blocked;
    }

    private IEnumerable<string> GetWizardClassIdsInCharGen()
    {
        var classIds = _app.CharGen.SelectedClassIds.Count > 0
            ? _app.CharGen.SelectedClassIds
            : string.IsNullOrWhiteSpace(_app.CharGen.ClassId)
                ? new List<string>()
                : new List<string> { _app.CharGen.ClassId };

        return classIds
            .Where(IsWizardClassId)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private int GetHighestWizardCastableSpellLevel()
    {
        int characterLevel = Math.Clamp(_app.CharGen.CharacterLevel, 1, 20);
        if (!WizardSpellLimitsByCharacterLevel.TryGetValue(characterLevel, out int[]? slots))
            return 0;

        for (int i = slots.Length - 1; i >= 0; i--)
        {
            if (slots[i] > 0)
                return i + 1;
        }

        return 0;
    }

    private static bool IsWizardSpellAllowedForComponents(
        SpellDefinition spell,
        HashSet<string> allowedSchools,
        HashSet<string> blockedSchools)
    {
        var spellSchools = ParseWizardSchoolTokens(spell.Schools);
        if (spellSchools.Count == 0)
            return true;

        var permittedSchools = spellSchools
            .Where(school => !blockedSchools.Contains(school))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (permittedSchools.Count == 0)
            return false;

        if (allowedSchools.Count == 0)
            return true;

        return permittedSchools.Any(allowedSchools.Contains);
    }

    private static HashSet<string> ParseWizardSchoolTokens(string schoolsText)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(schoolsText))
            return tokens;

        foreach (string raw in Regex.Split(schoolsText, @"\s*(,|;|\||&|\band\b)\s*", RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(raw)
                || raw == ","
                || raw == ";"
                || raw == "|"
                || raw == "&"
                || string.Equals(raw, "and", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string normalized = NormalizeWizardSchoolName(raw);
            if (!string.IsNullOrWhiteSpace(normalized))
                tokens.Add(normalized);
        }

        return tokens;
    }

    private static string NormalizeWizardSchoolName(string school)
    {
        string token = Regex.Replace((school ?? string.Empty).Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        if (token.Equals("Greater Divination", StringComparison.OrdinalIgnoreCase))
            return "Divination";

        if (token.Equals("Invocation", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Evocation", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Invocation/Evocation", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Evocation/Invocation", StringComparison.OrdinalIgnoreCase))
        {
            return "Invocation/Evocation";
        }

        if (token.Equals("Conjuration", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Summon", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Summoning", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Conjuration/Summon", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Summon/Conjuration", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Conjuration/Summoning", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Summoning/Conjuration", StringComparison.OrdinalIgnoreCase))
        {
            return "Conjuration/Summoning";
        }

        if (token.Equals("Enchantment", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Charm", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Enchantment/Charm", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Charm/Enchantment", StringComparison.OrdinalIgnoreCase))
        {
            return "Enchantment/Charm";
        }

        if (token.Equals("Dimension", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Dimensional", StringComparison.OrdinalIgnoreCase))
        {
            return "Dimensional";
        }

        if (token.Equals("Wild", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Wild Magic", StringComparison.OrdinalIgnoreCase))
        {
            return "Wild Magic";
        }

        if (token.Equals("Air", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Elemental Air", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Elemental (Air)", StringComparison.OrdinalIgnoreCase))
        {
            return "Elemental (Air)";
        }

        if (token.Equals("Earth", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Elemental Earth", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Elemental (Earth)", StringComparison.OrdinalIgnoreCase))
        {
            return "Elemental (Earth)";
        }

        if (token.Equals("Fire", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Elemental Fire", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Elemental (Fire)", StringComparison.OrdinalIgnoreCase))
        {
            return "Elemental (Fire)";
        }

        if (token.Equals("Water", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Elemental Water", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Elemental (Water)", StringComparison.OrdinalIgnoreCase))
        {
            return "Elemental (Water)";
        }

        return token;
    }

    private Dictionary<string, string> GatherCharGenSphereAccess()
    {
        var access = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void MergeSphereSelections(Dictionary<string, string>? selections)
        {
            if (selections is null)
                return;

            foreach (var (sphere, tier) in selections)
            {
                if (string.IsNullOrWhiteSpace(sphere))
                    continue;

                string normalizedTier = string.Equals(tier, "major", StringComparison.OrdinalIgnoreCase)
                    ? "major"
                    : "minor";

                if (!access.TryGetValue(sphere, out var existing))
                {
                    access[sphere] = normalizedTier;
                    continue;
                }

                if (string.Equals(existing, "major", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalizedTier, "major", StringComparison.OrdinalIgnoreCase))
                {
                    access[sphere] = "major";
                }
            }
        }

        MergeSphereSelections(_app.CharGen.SelectedSpheres);
        foreach (var byClass in _app.CharGen.SpheresByClass.Values)
            MergeSphereSelections(byClass);

        bool rangerHasConfiguredClassSpheres = _app.CharGen.SpheresByClass.TryGetValue("ranger", out var rangerSpheres)
            && rangerSpheres.Count > 0;
        if (CharacterHasClass("ranger")
            && !rangerHasConfiguredClassSpheres
            && HasSelectedClassAbility("ranger_priest_spells", "ranger_increased_spell_progression_cp7", "ranger_increased_spell_progression_cp12"))
        {
            if (!access.ContainsKey("Animal"))
                access["Animal"] = "minor";
            if (!access.ContainsKey("Plant"))
                access["Plant"] = "minor";
        }

        bool paladinHasConfiguredClassSpheres = _app.CharGen.SpheresByClass.TryGetValue("paladin", out var paladinSpheres)
            && paladinSpheres.Count > 0;
        if (CharacterHasClass("paladin")
            && !paladinHasConfiguredClassSpheres
            && HasSelectedClassAbility("paladin_priest_spells"))
        {
            if (!access.ContainsKey("Combat"))
                access["Combat"] = "minor";
            if (!access.ContainsKey("Divination"))
                access["Divination"] = "minor";
            if (!access.ContainsKey("Healing"))
                access["Healing"] = "minor";
            if (!access.ContainsKey("Protection"))
                access["Protection"] = "minor";
        }

        return access;
    }

    private bool CharacterHasClass(string classId)
    {
        var classIds = _app.CharGen.SelectedClassIds.Count > 0
            ? _app.CharGen.SelectedClassIds
            : string.IsNullOrWhiteSpace(_app.CharGen.ClassId)
                ? new List<string>()
                : new List<string> { _app.CharGen.ClassId };

        return classIds.Any(id => string.Equals((id ?? string.Empty).Trim(), classId, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasSelectedClassAbility(params string[] abilityBaseIds)
    {
        if (abilityBaseIds is null || abilityBaseIds.Length == 0)
            return false;

        var wanted = abilityBaseIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
            return false;

        foreach (string id in _app.CharGen.SelectedClassAbilityIds)
        {
            string baseId = RulesEngine.ExtractClassAbilityBaseId(id ?? string.Empty);
            if (wanted.Contains(baseId))
                return true;
        }

        foreach (var byClass in _app.CharGen.SelectedAbilitiesByClass.Values)
        {
            foreach (string id in byClass)
            {
                string baseId = RulesEngine.ExtractClassAbilityBaseId(id ?? string.Empty);
                if (wanted.Contains(baseId))
                    return true;
            }
        }

        return false;
    }

    private static bool IsPriestLikeClassId(string classId)
    {
        string token = (classId ?? string.Empty).Trim();
        return token.Equals("priest", StringComparison.OrdinalIgnoreCase)
            || token.Equals("cleric", StringComparison.OrdinalIgnoreCase)
            || token.Equals("druid", StringComparison.OrdinalIgnoreCase)
            || token.Equals("paladin", StringComparison.OrdinalIgnoreCase)
            || token.Equals("ranger", StringComparison.OrdinalIgnoreCase)
            || RulesEngine.IsPriestClassId(token);
    }

    private static bool IsDivineSpell(SpellDefinition spell)
    {
        string category = (spell.Category ?? string.Empty).Trim();
        return category.Equals("divine", StringComparison.OrdinalIgnoreCase)
            || category.Equals("priest", StringComparison.OrdinalIgnoreCase)
            || category.Contains("sphere", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsArcaneSpell(SpellDefinition spell)
        => string.Equals((spell.Category ?? string.Empty).Trim(), "arcane", StringComparison.OrdinalIgnoreCase);

    private static bool IsDivineSpellAllowedForCharGen(SpellDefinition spell, Dictionary<string, string> sphereAccess)
    {
        if (sphereAccess.Count == 0)
            return true;

        var spellSpheres = ParseSpellSchoolTokens(spell.Schools);
        if (spellSpheres.Count == 0)
            return true;

        int spellLevel = ParseSpellLevelSafe(spell.Level);
        string requiredAccess = spellLevel <= 3 ? "minor" : "major";
        return spellSpheres.Any(sphere => RulesEngine.PriestHasSphereAccess(sphere, sphereAccess, requiredAccess));
    }

    private static HashSet<string> ParseSpellSchoolTokens(string schoolsText)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(schoolsText))
            return tokens;

        foreach (string raw in Regex.Split(schoolsText, @"\s*(,|;|/|\||&|\band\b)\s*", RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(raw)
                || raw == ","
                || raw == ";"
                || raw == "/"
                || raw == "|"
                || raw == "&"
                || string.Equals(raw, "and", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string normalized = raw.Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
                tokens.Add(normalized);
        }

        return tokens;
    }

    private static int ParseSpellLevelSafe(string? levelText)
    {
        if (string.IsNullOrWhiteSpace(levelText))
            return 1;
        if (levelText.Equals("Cantrip", StringComparison.OrdinalIgnoreCase))
            return 0;

        string digits = Regex.Replace(levelText, @"[^\d]", string.Empty);
        return int.TryParse(digits, out int level) ? Math.Max(0, level) : 1;
    }

    private static int GetEstimatedBardArcaneMaxSpellLevel(int level)
    {
        int clamped = Math.Max(1, level);
        if (clamped < 2) return 0;
        if (clamped < 5) return 1;
        if (clamped < 10) return 2;
        if (clamped < 15) return 3;
        if (clamped < 20) return 4;
        if (clamped < 23) return 5;
        return 6;
    }

    private static bool IsSpellComponentsCategory(string? category)
        => !string.IsNullOrWhiteSpace(category)
            && category.Contains("spell component", StringComparison.OrdinalIgnoreCase);

    private bool IsRequiredSpellComponentItem(EquipmentCatalogItem item, HashSet<string> neededComponents)
    {
        if (neededComponents.Count == 0)
            return false;

        string id = EquipmentLibraryService.CanonicalizeId(item.ItemId);
        string normalizedName = NormalizeComponentName(item.ItemName);
        if (neededComponents.Contains(normalizedName))
            return true;

        if (_libraryById.TryGetValue(id, out var detail))
        {
            string normalizedDetailName = NormalizeComponentName(detail.Name);
            if (neededComponents.Contains(normalizedDetailName))
                return true;
        }

        return neededComponents.Any(needed =>
            normalizedName.Contains(needed, StringComparison.OrdinalIgnoreCase)
            || needed.Contains(normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeComponentName(string? value)
    {
        string normalized = Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        normalized = Regex.Replace(normalized, @"[^a-z0-9\s'-]", string.Empty).Trim();
        normalized = Regex.Replace(normalized, @"^(a|an|the)\s+", string.Empty, RegexOptions.IgnoreCase).Trim();

        if (Regex.IsMatch(normalized, @"\bholy symbol\b", RegexOptions.IgnoreCase))
            return "holy symbol";
        if (Regex.IsMatch(normalized, @"\b(prayer beads?|rosary)\b", RegexOptions.IgnoreCase))
            return "prayer beads";
        if (Regex.IsMatch(normalized, @"\bmistletoe\b", RegexOptions.IgnoreCase))
            return "mistletoe";
        if (Regex.IsMatch(normalized, @"\bpowdered silver\b|\bsilver powder\b", RegexOptions.IgnoreCase))
            return "powdered silver";
        if (Regex.IsMatch(normalized, @"\bdiamond dust\b", RegexOptions.IgnoreCase))
            return "diamond dust";

        return normalized;
    }

    private static IEnumerable<string> ExtractMaterialComponents(string? materialsText)
    {
        if (string.IsNullOrWhiteSpace(materialsText))
            yield break;

        string cleaned = Regex.Replace(materialsText, @"\([^)]*\)", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\b(material component(?:s)?|requires?|needed|must have|must be|consumes?)\b", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bworth\s+\d+\s*(gp|sp|cp|pp)\b", string.Empty, RegexOptions.IgnoreCase);
        cleaned = cleaned.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            yield break;

        foreach (string part in Regex.Split(cleaned, @"\s*(,|;|\bor\b|\band\b|\bplus\b|/)\s*", RegexOptions.IgnoreCase))
        {
            string token = (part ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token)
                || token == ","
                || token == ";"
                || token == "/"
                || string.Equals(token, "or", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "and", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "plus", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string normalized = token.Trim(' ', '.', ':', '-');
            normalized = Regex.Replace(normalized, @"\b(the caster|caster|priest|wizard|mage)('s)?\b", string.Empty, RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bthat is consumed\b|\bwhich is consumed\b|\bif available\b", string.Empty, RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
                yield return normalized;
        }
    }

    private static IEnumerable<string> ExtractComponentColumnItems(string? componentsText)
    {
        if (string.IsNullOrWhiteSpace(componentsText))
            yield break;

        foreach (Match match in Regex.Matches(componentsText, @"\(([^)]*)\)"))
        {
            string inside = match.Groups[1].Value;
            foreach (string token in ExtractMaterialComponents(inside))
            {
                if (!string.IsNullOrWhiteSpace(token))
                    yield return token;
            }
        }

        string cleaned = Regex.Replace(componentsText, @"\([^)]*\)", string.Empty);
        foreach (string part in Regex.Split(cleaned, @"\s*(,|;|\bor\b|\band\b|\bplus\b|/|\|)\s*", RegexOptions.IgnoreCase))
        {
            string token = (part ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token)
                || token == ","
                || token == ";"
                || token == "/"
                || token == "|"
                || string.Equals(token, "or", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "and", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "plus", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string lowered = token.ToLowerInvariant();
            if (lowered is "v" or "s" or "m" or "f" or "df"
                or "vs" or "vm" or "sm" or "vsm"
                or "verbal" or "somatic" or "material" or "focus" or "divine focus"
                or "none" or "special" or "n/a")
            {
                continue;
            }

            string normalized = Regex.Replace(token, @"\b(verbal|somatic|material|focus|divine focus)\b", string.Empty, RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"^[vsmf/|,\s]+$", string.Empty, RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim(' ', '.', ':', '-');
            if (!string.IsNullOrWhiteSpace(normalized))
                yield return normalized;
        }
    }

    private List<EquipmentCatalogItem> BuildAvailableItemsFromLibrary()
    {
        return _libraryById.Values
            .Select(detail =>
            {
                string category = detail.Categories.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "Equipment";
                category = EquipmentLibraryService.NormalizeCategoryLabel(category);
                string costText = FormatCoinCost(detail.CostGold, detail.CostSilver, detail.CostCopper);
                return new EquipmentCatalogItem(
                    detail.Id,
                    category,
                    "LIB",
                    detail.Name,
                    costText);
            })
            .OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => NormalizeSwordName(x.ItemName), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<GemEntry> GetCurrentGemEntries()
    {
        if (_app.CharGen.IsLevelUpMode
            && _app.CharGen.LevelUpCharacterIndex >= 0
            && _app.CharGen.LevelUpCharacterIndex < _app.Characters.Count)
        {
            return _app.Characters[_app.CharGen.LevelUpCharacterIndex].Gems ?? new List<GemEntry>();
        }

        return _app.CharGen.StartingGems ?? new List<GemEntry>();
    }

    private static IEnumerable<GemEntry> ExpandGemEntry(GemEntry gem)
    {
        int quantity = Math.Max(1, gem.Quantity);
        for (int i = 0; i < quantity; i++)
        {
            yield return new GemEntry
            {
                Name = gem.Name,
                Quantity = 1,
                ValueGoldPieces = Math.Max(0, gem.ValueGoldPieces),
            };
        }
    }

    private bool UseStartingFundsBudget => !_app.CharGen.IsLevelUpMode && !_app.CharGen.IsExistingCharacterMode;

    private void EnsureStartingFundsForCharGen()
    {
        if (!UseStartingFundsBudget)
            return;

        if (_app.CharGen.StartingFundsAssigned)
            return;

        var result = CharacterWealthService.RollStartingFundsForCharGen(_app.CharGen, _app.Rules);
        _app.CharGen.StartingPlatinumPieces = 0;
        _app.CharGen.StartingGoldPieces = Math.Max(0, result.GoldPieces);
        _app.CharGen.StartingSilverPieces = Math.Max(0, result.SilverPieces);
        _app.CharGen.StartingCopperPieces = Math.Max(0, result.CopperPieces);
        _app.CharGen.StartingGemCount = 0;
        _app.CharGen.StartingGemValueGoldPieces = 0;
        _app.CharGen.StartingGems.Clear();
        _app.CharGen.StartingFundsAssigned = true;
        _app.CharGen.StartingFundsRollSummary = result.Summary;
    }

    private int GetStartingFundsCopper()
        => CharacterWealthService.ToCopper(
            _app.CharGen.StartingPlatinumPieces,
            _app.CharGen.StartingGoldPieces,
            _app.CharGen.StartingSilverPieces,
            _app.CharGen.StartingCopperPieces);

    private int GetSelectedEquipmentCopper()
    {
        int total = 0;
        foreach (var item in _app.CharGen.SelectedEquipment)
        {
            int qty = Math.Max(1, item.Quantity);
            total += qty * CharacterWealthService.ToCopper(
                item.CostGoldEach,
                item.CostSilverEach,
                item.CostCopperEach);
        }
        return Math.Max(0, total);
    }

    private static int ParseCostTextCopper(string? costText)
    {
        if (string.IsNullOrWhiteSpace(costText))
            return 0;

        int gp = 0;
        int sp = 0;
        int cp = 0;
        var matches = System.Text.RegularExpressions.Regex.Matches(
            costText,
            @"(?<amount>\d+)\s*(?<coin>gp|sp|cp)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (!match.Success)
                continue;
            int amount = int.TryParse(match.Groups["amount"].Value, out int parsed) ? parsed : 0;
            string coin = match.Groups["coin"].Value.ToLowerInvariant();
            if (coin == "gp") gp += amount;
            else if (coin == "sp") sp += amount;
            else if (coin == "cp") cp += amount;
        }

        return CharacterWealthService.ToCopper(gp, sp, cp);
    }

    private bool CanAffordAdditionalCopper(int additionalCopper)
    {
        if (_app.CharGen.IsLevelUpMode
            && _app.CharGen.LevelUpCharacterIndex >= 0
            && _app.CharGen.LevelUpCharacterIndex < _app.Characters.Count)
        {
            var c = _app.Characters[_app.CharGen.LevelUpCharacterIndex];
            int available = CharacterWealthService.GetTotalCopper(c);
            return Math.Max(0, additionalCopper) <= available;
        }

        if (!UseStartingFundsBudget)
            return true;

        int starting = GetStartingFundsCopper();
        int spent = GetSelectedEquipmentCopper();
        return spent + Math.Max(0, additionalCopper) <= starting;
    }

    private void RefreshFundsSummary()
    {
        if (CurrentMoneyText is null || StartingFundsText is null || EquipmentSpendText is null || RemainingFundsText is null)
            return;

        if (!UseStartingFundsBudget)
        {
            if (_app.CharGen.IsLevelUpMode
                && _app.CharGen.LevelUpCharacterIndex >= 0
                && _app.CharGen.LevelUpCharacterIndex < _app.Characters.Count)
            {
                var c = _app.Characters[_app.CharGen.LevelUpCharacterIndex];
                CurrentMoneyText.Text = $"Current money: {CharacterWealthService.FormatCoins(c)}";
            }
            else
            {
                string gemsText = _app.CharGen.StartingGemCount > 0
                    ? $", gems {_app.CharGen.StartingGemCount} ({Math.Max(0, _app.CharGen.StartingGemValueGoldPieces)} gp value)"
                    : string.Empty;
                CurrentMoneyText.Text = $"Current money: managed outside starting-funds budget{gemsText}.";
            }

            StartingFundsText.Text = "";
            EquipmentSpendText.Text = "";
            RemainingFundsText.Text = "";
            return;
        }

        int starting = GetStartingFundsCopper();
        int spent = GetSelectedEquipmentCopper();
        int remaining = starting - spent;

        CurrentMoneyText.Text = $"Current money: {CharacterWealthService.FormatCoins(Math.Max(0, remaining))}";
        StartingFundsText.Text = $"Starting funds: {CharacterWealthService.FormatCoins(starting)}";
        EquipmentSpendText.Text = $"Spent on equipment: {CharacterWealthService.FormatCoins(spent)}";
        RemainingFundsText.Text = $"Remaining: {CharacterWealthService.FormatCoins(Math.Max(0, remaining))}";
        RemainingFundsText.Foreground = remaining < 0
            ? System.Windows.Media.Brushes.IndianRed
            : (System.Windows.Media.Brush)FindResource("BrushLightGold");
    }

    private static string GetCategoryDisplayName(EquipmentCategory category)
    {
        string title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(category.Name.ToLowerInvariant());
        if (string.Equals(category.TableCode, "CUSTOM", StringComparison.OrdinalIgnoreCase))
            return title;
        return $"TABLE {category.TableCode}: {title}";
    }

    private void AddSelected()
    {
        if (AvailableList.SelectedItem is not AvailableRow row)
            return;

        bool addAsFreeFind = FreePurchaseToggle?.IsChecked == true;

        if (!CanUseCatalogItem(row.Item, out string restrictionReason))
        {
            MessageBox.Show(
                $"{NormalizeSwordName(row.Item.ItemName)} is not currently allowed for this character.\n\n{restrictionReason}",
                "Equipment Restriction",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _libraryById.TryGetValue(EquipmentLibraryService.CanonicalizeId(row.Item.ItemId), out var detailForCost);
        int itemCostCopper = addAsFreeFind
            ? 0
            : (detailForCost is not null
                ? CharacterWealthService.ToCopper(detailForCost.CostGold, detailForCost.CostSilver, detailForCost.CostCopper)
                : ParseCostTextCopper(row.Item.CostText));

        if (!addAsFreeFind && !CanAffordAdditionalCopper(itemCostCopper))
        {
            int remaining;
            if (_app.CharGen.IsLevelUpMode
                && _app.CharGen.LevelUpCharacterIndex >= 0
                && _app.CharGen.LevelUpCharacterIndex < _app.Characters.Count)
            {
                var c = _app.Characters[_app.CharGen.LevelUpCharacterIndex];
                remaining = CharacterWealthService.GetTotalCopper(c);
            }
            else
            {
                int starting = GetStartingFundsCopper();
                int spent = GetSelectedEquipmentCopper();
                remaining = Math.Max(0, starting - spent);
            }

            MessageBox.Show(
                $"Not enough funds for {row.Item.ItemName}.\n\n" +
                $"Cost: {CharacterWealthService.FormatCoins(itemCostCopper)}\n" +
                $"Remaining: {CharacterWealthService.FormatCoins(remaining)}",
                "Equipment Budget",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TryApplyLevelUpEquipmentDeltaCopper(itemCostCopper))
            return;

        var existing = _app.CharGen.SelectedEquipment
            .FirstOrDefault(x => string.Equals(
                EquipmentLibraryService.CanonicalizeId(x.ItemId),
                EquipmentLibraryService.CanonicalizeId(row.Item.ItemId),
                StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            _libraryById.TryGetValue(EquipmentLibraryService.CanonicalizeId(row.Item.ItemId), out var detail);
            _app.CharGen.SelectedEquipment.Add(new EquipmentSelection
            {
                ItemId = row.Item.ItemId,
                Category = EquipmentLibraryService.NormalizeCategoryLabel(row.Item.Category),
                ItemName = NormalizeSwordName(row.Item.ItemName),
                CostText = addAsFreeFind ? "FREE" : row.Item.CostText,
                Quantity = 1,
                IsArmor = detail?.IsArmor == true,
                ArmorClassValue = detail?.ArmorClassValue ?? 10,
                RogueArmorProfile = detail?.RogueArmorProfile ?? "no_armor",
                IsShield = detail?.IsShield == true,
                IsWeapon = detail?.IsWeapon == true,
                WeaponSpeed = detail?.WeaponSpeed ?? 0,
                WeaponDamageSmallMedium = detail?.WeaponDamageSmallMedium ?? string.Empty,
                WeaponDamageLarge = detail?.WeaponDamageLarge ?? string.Empty,
                WeaponType = detail?.WeaponType ?? string.Empty,
                WeaponSize = detail?.WeaponSize ?? string.Empty,
                WeaponAttackBonus = detail?.WeaponAttackBonus ?? 0,
                WeaponDamageBonus = detail?.WeaponDamageBonus ?? 0,
                WeaponThac0Modifier = detail?.WeaponThac0Modifier ?? 0,
                CostGoldEach = addAsFreeFind ? 0 : (detail?.CostGold ?? 0),
                CostSilverEach = addAsFreeFind ? 0 : (detail?.CostSilver ?? 0),
                CostCopperEach = addAsFreeFind ? 0 : (detail?.CostCopper ?? ParseCostTextCopper(row.Item.CostText)),
                SizeClassEach = detail?.SizeClass ?? "Medium",
                WeightEach = detail?.Weight ?? 0,
            });
        }
        else
        {
            existing.Quantity = Math.Max(1, existing.Quantity + 1);
            if (addAsFreeFind)
            {
                existing.CostText = "FREE";
                existing.CostGoldEach = 0;
                existing.CostSilverEach = 0;
                existing.CostCopperEach = 0;
            }
        }

        SetTransactionStatus(
            addAsFreeFind
                ? $"Added found item {NormalizeSwordName(row.Item.ItemName)} at no coin cost."
                : $"Purchased {NormalizeSwordName(row.Item.ItemName)} for {CharacterWealthService.FormatCoins(itemCostCopper)}.",
            Brushes.LightGreen);

        RefreshLists();
    }

    private void RemoveSelected()
    {
        if (SelectedList.SelectedItem is not SelectedRow { Selection: not null } row)
            return;

        string removedName = row.Selection!.ItemName;
        int unitCostCopper = GetSelectionUnitCostCopper(row.Selection);

        if (row.Selection.Quantity > 1)
        {
            row.Selection.Quantity -= 1;
        }
        else
        {
            string removedKey = EquipmentLibraryService.CanonicalizeId(row.Selection.ItemId);
            if (string.Equals(EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedArmorId), removedKey, StringComparison.OrdinalIgnoreCase))
                _app.CharGen.EquippedArmorId = string.Empty;
            if (string.Equals(EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedShieldId), removedKey, StringComparison.OrdinalIgnoreCase))
                _app.CharGen.EquippedShieldId = string.Empty;
            if (string.Equals(EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedWeaponId), removedKey, StringComparison.OrdinalIgnoreCase))
                _app.CharGen.EquippedWeaponId = string.Empty;
            if (string.Equals(EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedMainWeaponId), removedKey, StringComparison.OrdinalIgnoreCase))
                _app.CharGen.EquippedMainWeaponId = string.Empty;
            if (string.Equals(EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedOffHandWeaponId), removedKey, StringComparison.OrdinalIgnoreCase))
                _app.CharGen.EquippedOffHandWeaponId = string.Empty;
            _app.CharGen.EquippedMagicalItemIds.RemoveAll(x => string.Equals(EquipmentLibraryService.CanonicalizeId(x), removedKey, StringComparison.OrdinalIgnoreCase));
            _app.CharGen.SelectedEquipment.Remove(row.Selection);
        }

        TryApplyLevelUpEquipmentDeltaCopper(-GetSelectionUnitCostCopper(row.Selection));
        SetTransactionStatus(
            $"Sold/removed {NormalizeSwordName(removedName)} for {CharacterWealthService.FormatCoins(unitCostCopper)}.",
            Brushes.LightSkyBlue);

        RefreshLists();
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshLists();
    private void EquipmentSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshLists();
    private void EquipmentTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshLists();
    private void SpellComponentNeedFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshLists();

    private void UseSpellComponentsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!SupportsSpellComponentsForCurrentClass())
        {
            _app.CharGen.UseSpellComponents = false;
            if (UseSpellComponentsToggle is not null)
                UseSpellComponentsToggle.IsChecked = false;
            RefreshLists();
            return;
        }

        _app.CharGen.UseSpellComponents = UseSpellComponentsToggle?.IsChecked == true;
        RefreshLists();
    }

    private void BtnRefreshEquipment_Click(object sender, RoutedEventArgs e)
    {
        RefreshLists();
        SetTransactionStatus("Equipment list refreshed from current character and spell selections.", Brushes.LightSkyBlue);
    }

    private void FreePurchaseToggle_Changed(object sender, RoutedEventArgs e)
    {
        SetTransactionStatus(
            FreePurchaseToggle?.IsChecked == true
                ? "Purchase mode: FREE FIND (adds items without coin cost)."
                : "Purchase mode: PAY NORMAL COST.",
            Brushes.LightSkyBlue);
    }

    private void AvailableList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnAdd.IsEnabled = AvailableList.SelectedItem is not null;
    }

    private void SelectedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = SelectedList.SelectedItem is SelectedRow { Selection: not null };
        BtnRemove.IsEnabled = hasSelection;
        BtnSetQuantity.IsEnabled = hasSelection;
        BtnEquipOffHand.IsEnabled = false;

        if (SelectedList.SelectedItem is SelectedRow { Selection: not null } sel)
        {
            // Resolve library data so we know what type this item is.
            string canonId = EquipmentLibraryService.CanonicalizeId(sel.Selection!.ItemId);
            _libraryById.TryGetValue(canonId, out var detail);
            bool isEquippable = detail?.IsArmor == true || detail?.IsShield == true || detail?.IsWeapon == true
                || sel.Selection.IsArmor || sel.Selection.IsShield || sel.Selection.IsWeapon
                || !string.IsNullOrWhiteSpace(ResolveMagicalSlotKey(detail, sel.Selection));
            BtnEquip.IsEnabled = isEquippable;
            bool isWeapon = detail?.IsWeapon == true || sel.Selection.IsWeapon;
            BtnEquipOffHand.IsEnabled = isWeapon;
        }
        else
        {
            BtnEquip.IsEnabled = false;
            BtnEquipOffHand.IsEnabled = false;
        }
    }

    private void BtnSetQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedList.SelectedItem is not SelectedRow { Selection: not null } row)
            return;

        if (!TryPromptQuantity(Math.Max(1, row.Selection!.Quantity), out int quantity))
            return;

        int oldQty = Math.Max(1, row.Selection.Quantity);
        int newQty = Math.Max(1, quantity);
        int deltaQty = newQty - oldQty;
        if (deltaQty > 0)
        {
            int itemCostCopper = GetSelectionUnitCostCopper(row.Selection);
            int addedCost = deltaQty * itemCostCopper;
            if (!CanAffordAdditionalCopper(addedCost))
            {
                int remaining;
                if (_app.CharGen.IsLevelUpMode
                    && _app.CharGen.LevelUpCharacterIndex >= 0
                    && _app.CharGen.LevelUpCharacterIndex < _app.Characters.Count)
                {
                    var c = _app.Characters[_app.CharGen.LevelUpCharacterIndex];
                    remaining = CharacterWealthService.GetTotalCopper(c);
                }
                else
                {
                    int starting = GetStartingFundsCopper();
                    int spent = GetSelectedEquipmentCopper();
                    remaining = Math.Max(0, starting - spent);
                }

                MessageBox.Show(
                    $"Increasing quantity exceeds available funds.\n\n" +
                    $"Additional cost: {CharacterWealthService.FormatCoins(addedCost)}\n" +
                    $"Remaining: {CharacterWealthService.FormatCoins(remaining)}",
                    "Equipment Budget",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        int deltaCopper = (newQty - oldQty) * GetSelectionUnitCostCopper(row.Selection);
        if (!TryApplyLevelUpEquipmentDeltaCopper(deltaCopper))
            return;

        row.Selection.Quantity = Math.Max(1, quantity);

        if (deltaCopper > 0)
        {
            SetTransactionStatus(
                $"Purchased {deltaQty} more {NormalizeSwordName(row.Selection.ItemName)} for {CharacterWealthService.FormatCoins(deltaCopper)}.",
                Brushes.LightGreen);
        }
        else if (deltaCopper < 0)
        {
            SetTransactionStatus(
                $"Sold {Math.Abs(deltaQty)} {NormalizeSwordName(row.Selection.ItemName)} for {CharacterWealthService.FormatCoins(Math.Abs(deltaCopper))}.",
                Brushes.LightSkyBlue);
        }
        else
        {
            SetTransactionStatus($"Quantity unchanged for {NormalizeSwordName(row.Selection.ItemName)}.", Brushes.Gray);
        }

        RefreshLists();
    }

    private int GetSelectionUnitCostCopper(EquipmentSelection selection)
        => CharacterWealthService.ToCopper(
            Math.Max(0, selection.CostGoldEach),
            Math.Max(0, selection.CostSilverEach),
            Math.Max(0, selection.CostCopperEach));

    private bool TryApplyLevelUpEquipmentDeltaCopper(int deltaCopper)
    {
        if (!_app.CharGen.IsLevelUpMode
            || _app.CharGen.LevelUpCharacterIndex < 0
            || _app.CharGen.LevelUpCharacterIndex >= _app.Characters.Count)
        {
            return true;
        }

        if (deltaCopper == 0)
        {
            SyncLevelUpEquipmentBaselineToCurrent();
            return true;
        }

        var c = _app.Characters[_app.CharGen.LevelUpCharacterIndex];
        int available = CharacterWealthService.GetTotalCopper(c);
        if (deltaCopper > 0)
        {
            if (available < deltaCopper)
            {
                MessageBox.Show(
                    $"Not enough funds. Need {CharacterWealthService.FormatCoins(deltaCopper)}, have {CharacterWealthService.FormatCoins(available)}.",
                    "Equipment Budget",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            CharacterWealthService.SetFromCopper(c, available - deltaCopper);
        }
        else
        {
            CharacterWealthService.SetFromCopper(c, available + Math.Abs(deltaCopper));
        }

        SyncLevelUpEquipmentBaselineToCurrent();
        return true;
    }

    private void SyncLevelUpEquipmentBaselineToCurrent()
    {
        _app.CharGen.BaselineEquipmentSelections = _app.CharGen.SelectedEquipment
            .Select(CloneEquipmentSelection)
            .ToList();
    }

    private static EquipmentSelection CloneEquipmentSelection(EquipmentSelection selection)
        => new()
        {
            ItemId = selection.ItemId,
            Category = selection.Category,
            ItemName = selection.ItemName,
            CostText = selection.CostText,
            Quantity = Math.Max(1, selection.Quantity),
            IsArmor = selection.IsArmor,
            ArmorClassValue = selection.ArmorClassValue,
            RogueArmorProfile = string.IsNullOrWhiteSpace(selection.RogueArmorProfile) ? "no_armor" : selection.RogueArmorProfile,
            IsShield = selection.IsShield,
            IsWeapon = selection.IsWeapon,
            WeaponSpeed = Math.Max(0, selection.WeaponSpeed),
            WeaponDamageSmallMedium = selection.WeaponDamageSmallMedium ?? string.Empty,
            WeaponDamageLarge = selection.WeaponDamageLarge ?? string.Empty,
            WeaponType = selection.WeaponType ?? string.Empty,
            WeaponSize = selection.WeaponSize ?? string.Empty,
            CostGoldEach = Math.Max(0, selection.CostGoldEach),
            CostSilverEach = Math.Max(0, selection.CostSilverEach),
            CostCopperEach = Math.Max(0, selection.CostCopperEach),
            SizeClassEach = string.IsNullOrWhiteSpace(selection.SizeClassEach) ? "Medium" : selection.SizeClassEach,
            WeightEach = Math.Max(0, selection.WeightEach),
        };

    private bool TryPromptQuantity(int currentQuantity, out int quantity)
    {
        int selectedQuantity = Math.Max(1, currentQuantity);
        quantity = selectedQuantity;

        var window = new Window
        {
            Title = "Set Quantity",
            Width = 320,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            Owner = Window.GetWindow(this),
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Enter quantity (1 or greater):",
            Margin = new Thickness(0, 0, 0, 8),
        });

        var quantityBox = new TextBox
        {
            Text = Math.Max(1, currentQuantity).ToString(CultureInfo.InvariantCulture),
            Margin = new Thickness(0, 0, 0, 10),
        };
        panel.Children.Add(quantityBox);
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var ok = new Button
        {
            Content = "OK",
            Width = 70,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 70,
            IsCancel = true,
        };

        cancel.Click += (_, _) => window.DialogResult = false;
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(quantityBox.Text.Trim(), out int parsed) || parsed < 1)
            {
                MessageBox.Show(
                    "Quantity must be 1 or greater.",
                    "Set Quantity",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                quantityBox.Focus();
                quantityBox.SelectAll();
                return;
            }

            selectedQuantity = parsed;
            window.DialogResult = true;
        };

        buttonRow.Children.Add(ok);
        buttonRow.Children.Add(cancel);
        panel.Children.Add(buttonRow);

        window.Content = panel;
        bool accepted = window.ShowDialog() == true;
        if (accepted)
            quantity = selectedQuantity;
        return accepted;
    }

    private void BtnEquip_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedList.SelectedItem is not SelectedRow { Selection: not null } row)
            return;

        if (!CanUseSelectedItem(row.Selection!, out string restrictionReason))
        {
            MessageBox.Show(
                $"{NormalizeSwordName(row.Selection!.ItemName)} cannot be equipped right now.\n\n{restrictionReason}",
                "Equipment Restriction",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string itemId = row.Selection!.ItemId;
        string canonId = EquipmentLibraryService.CanonicalizeId(itemId);
        _libraryById.TryGetValue(canonId, out var detail);

        bool isShield = detail?.IsShield == true || row.Selection.IsShield;
        bool isArmor  = !isShield && (detail?.IsArmor == true || row.Selection.IsArmor);
        bool isWeapon = detail?.IsWeapon == true || row.Selection.IsWeapon;

        if (isShield)
        {
            // Toggle off if already equipped.
            string currentKey = EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedShieldId);
            _app.CharGen.EquippedShieldId = string.Equals(currentKey, canonId, StringComparison.OrdinalIgnoreCase)
                ? ""
                : itemId;
        }
        else if (isArmor)
        {
            string currentKey = EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedArmorId);
            _app.CharGen.EquippedArmorId = string.Equals(currentKey, canonId, StringComparison.OrdinalIgnoreCase)
                ? ""
                : itemId;
        }
        else if (isWeapon)
        {
            string currentKey = EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedMainWeaponId);
            _app.CharGen.EquippedMainWeaponId = string.Equals(currentKey, canonId, StringComparison.OrdinalIgnoreCase)
                ? ""
                : itemId;
            _app.CharGen.EquippedWeaponId = _app.CharGen.EquippedMainWeaponId;
        }
        else
        {
            string magicalSlot = ResolveMagicalSlotKey(detail, row.Selection);
            if (!string.IsNullOrWhiteSpace(magicalSlot))
                ToggleMagicalEquip(itemId, magicalSlot);
        }

        RefreshLists();
    }

    private void BtnEquipOffHand_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedList.SelectedItem is not SelectedRow { Selection: not null } row)
            return;

        if (!CanUseSelectedItem(row.Selection!, out string restrictionReason))
        {
            MessageBox.Show(
                $"{NormalizeSwordName(row.Selection!.ItemName)} cannot be equipped right now.\n\n{restrictionReason}",
                "Equipment Restriction",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        string itemId = row.Selection!.ItemId;
        string canonId = EquipmentLibraryService.CanonicalizeId(itemId);
        _libraryById.TryGetValue(canonId, out var detail);
        bool isWeapon = detail?.IsWeapon == true || row.Selection.IsWeapon;
        if (!isWeapon)
            return;

        string mainKey = EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedMainWeaponId);
        string offKey = EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedOffHandWeaponId);

        if (string.Equals(offKey, canonId, StringComparison.OrdinalIgnoreCase))
        {
            _app.CharGen.EquippedOffHandWeaponId = string.Empty;
            RefreshLists();
            return;
        }

        if (string.Equals(mainKey, canonId, StringComparison.OrdinalIgnoreCase) && Math.Max(1, row.Selection.Quantity) < 2)
        {
            MessageBox.Show(
                "To use the same weapon as both main and off-hand, quantity must be at least 2.",
                "Off-Hand Assignment",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _app.CharGen.EquippedOffHandWeaponId = itemId;
        RefreshLists();
    }

    private void RefreshEquippedDisplay()
    {
        string ArmorName(string id) =>
            string.IsNullOrWhiteSpace(id) ? "—"
            : (_app.CharGen.SelectedEquipment
                .FirstOrDefault(x => string.Equals(
                    EquipmentLibraryService.CanonicalizeId(x.ItemId),
                    EquipmentLibraryService.CanonicalizeId(id),
                    StringComparison.OrdinalIgnoreCase))?.ItemName ?? id);

        if (EquippedArmorText != null)
            EquippedArmorText.Text  = $"Armor:  {ArmorName(_app.CharGen.EquippedArmorId)}";
        if (EquippedShieldText != null)
            EquippedShieldText.Text = $"Shield: {ArmorName(_app.CharGen.EquippedShieldId)}";
        if (EquippedWeaponText != null)
            EquippedWeaponText.Text = $"Main Weapon: {ArmorName(_app.CharGen.EquippedMainWeaponId)}";
        if (EquippedOffHandWeaponText != null)
            EquippedOffHandWeaponText.Text = $"Off-Hand Weapon: {ArmorName(_app.CharGen.EquippedOffHandWeaponId)}";
        var magicalBySlot = GetEquippedMagicalBySlot();
        List<string> SlotItems(string slot)
            => magicalBySlot.TryGetValue(slot, out var items) ? items : new List<string>();
        if (EquippedRing1Text != null)
            EquippedRing1Text.Text = $"Ring 1: {SlotItems("ring").ElementAtOrDefault(0) ?? "—"}";
        if (EquippedRing2Text != null)
            EquippedRing2Text.Text = $"Ring 2: {SlotItems("ring").ElementAtOrDefault(1) ?? "—"}";
        if (EquippedHelmetText != null)
            EquippedHelmetText.Text = $"Helmet: {SlotItems("helmet").FirstOrDefault() ?? "—"}";
        if (EquippedCloakText != null)
            EquippedCloakText.Text = $"Cloak: {SlotItems("cloak").FirstOrDefault() ?? "—"}";
        if (EquippedBeltText != null)
            EquippedBeltText.Text = $"Belt: {SlotItems("belt").FirstOrDefault() ?? "—"}";
        if (EquippedBootsText != null)
            EquippedBootsText.Text = $"Boots: {SlotItems("boots").FirstOrDefault() ?? "—"}";
        if (EquippedBracersText != null)
            EquippedBracersText.Text = $"Bracers: {SlotItems("bracers").FirstOrDefault() ?? "—"}";
        if (EquippedAmuletText != null)
            EquippedAmuletText.Text = $"Amulet: {SlotItems("amulet").FirstOrDefault() ?? "—"}";
        if (EquippedGlovesText != null)
            EquippedGlovesText.Text = $"Gloves: {SlotItems("gloves").FirstOrDefault() ?? "—"}";
        if (EquippedRobeText != null)
            EquippedRobeText.Text = $"Robe: {SlotItems("robe").FirstOrDefault() ?? "—"}";
        if (EquippedOtherMagicText != null)
            EquippedOtherMagicText.Text = $"Other Magic: {string.Join(", ", SlotItems("other").DefaultIfEmpty("—"))}";
    }

    private bool IsMagicalItemEquipped(string canonicalItemId)
        => _app.CharGen.EquippedMagicalItemIds.Any(x => string.Equals(EquipmentLibraryService.CanonicalizeId(x), canonicalItemId, StringComparison.OrdinalIgnoreCase));

    private string ResolveMagicalSlotKey(CustomEquipmentData? detail, EquipmentSelection selection)
    {
        if (detail?.IsArmor == true || detail?.IsShield == true || detail?.IsWeapon == true
            || selection.IsArmor || selection.IsShield || selection.IsWeapon)
        {
            return string.Empty;
        }

        bool isMagical = detail?.IsMagical == true
            || selection.Category.Contains("Magical", StringComparison.OrdinalIgnoreCase)
            || selection.ItemName.Contains("+", StringComparison.OrdinalIgnoreCase);
        if (!isMagical)
            return string.Empty;

        var terms = new List<string>();
        if (detail?.Categories is { Count: > 0 }) terms.AddRange(detail.Categories);
        if (detail?.ItemTags is { Count: > 0 }) terms.AddRange(detail.ItemTags);
        terms.Add(detail?.Name ?? selection.ItemName);
        terms.Add(selection.Category);
        terms.Add(selection.ItemName);
        string haystack = string.Join(" ", terms.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();

        if (haystack.Contains("ring")) return "ring";
        if (haystack.Contains("helmet") || haystack.Contains("helm")) return "helmet";
        if (haystack.Contains("cloak")) return "cloak";
        if (haystack.Contains("belt") || haystack.Contains("girdle")) return "belt";
        if (haystack.Contains("boot")) return "boots";
        if (haystack.Contains("bracer")) return "bracers";
        if (haystack.Contains("amulet") || haystack.Contains("necklace")) return "amulet";
        if (haystack.Contains("gauntlet") || haystack.Contains("glove")) return "gloves";
        if (haystack.Contains("robe")) return "robe";
        return "other";
    }

    private void ToggleMagicalEquip(string itemId, string slotKey)
    {
        string canonId = EquipmentLibraryService.CanonicalizeId(itemId);
        if (IsMagicalItemEquipped(canonId))
        {
            _app.CharGen.EquippedMagicalItemIds.RemoveAll(x => string.Equals(EquipmentLibraryService.CanonicalizeId(x), canonId, StringComparison.OrdinalIgnoreCase));
            return;
        }

        int limit = MagicalSlotLimits.GetValueOrDefault(slotKey, 1);
        int slotCount = _app.CharGen.EquippedMagicalItemIds.Count(id =>
        {
            string currentCanon = EquipmentLibraryService.CanonicalizeId(id);
            var currentSelection = _app.CharGen.SelectedEquipment.FirstOrDefault(x => string.Equals(EquipmentLibraryService.CanonicalizeId(x.ItemId), currentCanon, StringComparison.OrdinalIgnoreCase));
            if (currentSelection is null)
                return false;
            _libraryById.TryGetValue(currentCanon, out var currentDetail);
            return string.Equals(ResolveMagicalSlotKey(currentDetail, currentSelection), slotKey, StringComparison.OrdinalIgnoreCase);
        });

        if (slotCount >= limit)
        {
            MessageBox.Show(
                slotKey == "ring"
                    ? "Only two magical rings can be equipped at once."
                    : $"Only {limit} magical item can be equipped in the {slotKey} slot.",
                "Magical Slot Limit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _app.CharGen.EquippedMagicalItemIds.Add(itemId);
    }

    private Dictionary<string, List<string>> GetEquippedMagicalBySlot()
    {
        var bySlot = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string id in _app.CharGen.EquippedMagicalItemIds)
        {
            string canon = EquipmentLibraryService.CanonicalizeId(id);
            var selection = _app.CharGen.SelectedEquipment.FirstOrDefault(x => string.Equals(EquipmentLibraryService.CanonicalizeId(x.ItemId), canon, StringComparison.OrdinalIgnoreCase));
            if (selection is null)
                continue;

            _libraryById.TryGetValue(canon, out var detail);
            string slot = ResolveMagicalSlotKey(detail, selection);
            if (string.IsNullOrWhiteSpace(slot))
                continue;

            if (!bySlot.TryGetValue(slot, out var list))
            {
                list = new List<string>();
                bySlot[slot] = list;
            }

            list.Add(NormalizeSwordName(selection.ItemName));
        }

        return bySlot;
    }

    private bool TryUnequipMagicalSlot(string slotKey, int index = 0, bool removeAll = false)
    {
        var slotItems = new List<string>();

        foreach (string id in _app.CharGen.EquippedMagicalItemIds)
        {
            string canon = EquipmentLibraryService.CanonicalizeId(id);
            var selection = _app.CharGen.SelectedEquipment.FirstOrDefault(x => string.Equals(EquipmentLibraryService.CanonicalizeId(x.ItemId), canon, StringComparison.OrdinalIgnoreCase));
            if (selection is null)
                continue;

            _libraryById.TryGetValue(canon, out var detail);
            string currentSlot = ResolveMagicalSlotKey(detail, selection);
            if (string.Equals(currentSlot, slotKey, StringComparison.OrdinalIgnoreCase))
                slotItems.Add(id);
        }

        if (slotItems.Count == 0)
            return false;

        if (removeAll)
        {
            foreach (string id in slotItems)
                _app.CharGen.EquippedMagicalItemIds.Remove(id);
            return true;
        }

        if (index < 0 || index >= slotItems.Count)
            return false;

        _app.CharGen.EquippedMagicalItemIds.Remove(slotItems[index]);
        return true;
    }

    private void UnequipMagicalSlotAndRefresh(string slotKey, int index = 0, bool removeAll = false)
    {
        if (TryUnequipMagicalSlot(slotKey, index, removeAll))
            RefreshLists();
    }

    private void BtnUnequipRing1_Click(object sender, RoutedEventArgs e) => UnequipMagicalSlotAndRefresh("ring", 0);
    private void BtnUnequipRing2_Click(object sender, RoutedEventArgs e) => UnequipMagicalSlotAndRefresh("ring", 1);
    private void BtnUnequipHelmet_Click(object sender, RoutedEventArgs e) => UnequipMagicalSlotAndRefresh("helmet");
    private void BtnUnequipCloak_Click(object sender, RoutedEventArgs e) => UnequipMagicalSlotAndRefresh("cloak");
    private void BtnUnequipBelt_Click(object sender, RoutedEventArgs e) => UnequipMagicalSlotAndRefresh("belt");
    private void BtnUnequipBoots_Click(object sender, RoutedEventArgs e) => UnequipMagicalSlotAndRefresh("boots");
    private void BtnUnequipBracers_Click(object sender, RoutedEventArgs e) => UnequipMagicalSlotAndRefresh("bracers");
    private void BtnUnequipAmulet_Click(object sender, RoutedEventArgs e) => UnequipMagicalSlotAndRefresh("amulet");
    private void BtnUnequipGloves_Click(object sender, RoutedEventArgs e) => UnequipMagicalSlotAndRefresh("gloves");
    private void BtnUnequipRobe_Click(object sender, RoutedEventArgs e) => UnequipMagicalSlotAndRefresh("robe");
    private void BtnUnequipOtherMagic_Click(object sender, RoutedEventArgs e) => UnequipMagicalSlotAndRefresh("other", removeAll: true);

    private void BtnAdd_Click(object sender, RoutedEventArgs e) => AddSelected();
    private void BtnRemove_Click(object sender, RoutedEventArgs e) => RemoveSelected();

    private void BtnAddFunds_Click(object sender, RoutedEventArgs e)
    {
        if (!TryPromptFundsToAdd(out int pp, out int gp, out int sp, out int cp, out int gemCount, out int gemValueEachGp))
            return;

        int addCopper = CharacterWealthService.ToCopper(pp, gp, sp, cp);
        int addGemCount = Math.Max(0, gemCount);
        if (addCopper <= 0 && addGemCount <= 0)
        {
            MessageBox.Show(
                "Enter at least one non-zero coin value or gem quantity.",
                "Add Funds",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_app.CharGen.IsLevelUpMode
            && _app.CharGen.LevelUpCharacterIndex >= 0
            && _app.CharGen.LevelUpCharacterIndex < _app.Characters.Count)
        {
            var c = _app.Characters[_app.CharGen.LevelUpCharacterIndex];
            if (addCopper > 0)
                CharacterWealthService.Add(c, pp, gp, sp, cp);
            if (addGemCount > 0)
            {
                for (int i = 0; i < addGemCount; i++)
                {
                    c.Gems.Add(new GemEntry
                    {
                        Name = "Gem",
                        Quantity = 1,
                        ValueGoldPieces = Math.Max(0, gemValueEachGp),
                    });
                }
                c.GemCount = c.Gems.Sum(x => Math.Max(1, x.Quantity));
                c.GemValueGoldPieces = c.Gems.Sum(x => Math.Max(1, x.Quantity) * Math.Max(0, x.ValueGoldPieces));
            }
            c.LastModified = DateTime.Now;
            c.Revision += 1;
            _app.SaveCharacters();
        }
        else
        {
            int total = GetStartingFundsCopper() + Math.Max(0, addCopper);
            CharacterWealthService.FromCopperWithPlatinum(total, out int totalPp, out int totalGp, out int totalSp, out int totalCp);
            _app.CharGen.StartingPlatinumPieces = totalPp;
            _app.CharGen.StartingGoldPieces = totalGp;
            _app.CharGen.StartingSilverPieces = totalSp;
            _app.CharGen.StartingCopperPieces = totalCp;
            if (addGemCount > 0)
            {
                for (int i = 0; i < addGemCount; i++)
                {
                    _app.CharGen.StartingGems.Add(new GemEntry
                    {
                        Name = "Gem",
                        Quantity = 1,
                        ValueGoldPieces = Math.Max(0, gemValueEachGp),
                    });
                }
                _app.CharGen.StartingGemCount = _app.CharGen.StartingGems.Sum(x => Math.Max(1, x.Quantity));
                _app.CharGen.StartingGemValueGoldPieces = _app.CharGen.StartingGems.Sum(x => Math.Max(1, x.Quantity) * Math.Max(0, x.ValueGoldPieces));
            }
            _app.CharGen.StartingFundsAssigned = true;
        }

        RefreshLists();
    }

    private bool TryPromptFundsToAdd(out int pp, out int gp, out int sp, out int cp, out int gemCount, out int gemValueEachGp)
    {
        pp = 0;
        gp = 0;
        sp = 0;
        cp = 0;
        gemCount = 0;
        gemValueEachGp = 0;
        int selectedPp = 0;
        int selectedGp = 0;
        int selectedSp = 0;
        int selectedCp = 0;
        int selectedGemCount = 0;
        int selectedGemValueEachGp = 0;

        var dialog = new Window
        {
            Title = "Add Funds",
            Width = 460,
            Height = 320,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
        };

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Add coin denominations to current funds.",
            Margin = new Thickness(0, 0, 0, 8),
        });

        var coinGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        coinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        coinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        coinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        coinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var ppBox = new TextBox { Text = "0", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        var gpBox = new TextBox { Text = "0", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        var spBox = new TextBox { Text = "0", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        var cpBox = new TextBox { Text = "0", VerticalContentAlignment = VerticalAlignment.Center };

        var ppPanel = new StackPanel();
        ppPanel.Children.Add(new TextBlock { Text = "PP" });
        ppPanel.Children.Add(ppBox);
        Grid.SetColumn(ppPanel, 0);
        coinGrid.Children.Add(ppPanel);

        var gpPanel = new StackPanel();
        gpPanel.Children.Add(new TextBlock { Text = "GP" });
        gpPanel.Children.Add(gpBox);
        Grid.SetColumn(gpPanel, 1);
        coinGrid.Children.Add(gpPanel);

        var spPanel = new StackPanel();
        spPanel.Children.Add(new TextBlock { Text = "SP" });
        spPanel.Children.Add(spBox);
        Grid.SetColumn(spPanel, 2);
        coinGrid.Children.Add(spPanel);

        var cpPanel = new StackPanel();
        cpPanel.Children.Add(new TextBlock { Text = "CP" });
        cpPanel.Children.Add(cpBox);
        Grid.SetColumn(cpPanel, 3);
        coinGrid.Children.Add(cpPanel);

        Grid.SetRow(coinGrid, 1);
        root.Children.Add(coinGrid);

        var gemSection = new Border
        {
            BorderBrush = (Brush)FindResource("BrushBorder2"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 10),
        };
        var gemGrid = new Grid();
        gemGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gemGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        gemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var gemCountBox = new TextBox { Text = "0", VerticalContentAlignment = VerticalAlignment.Center };
        var gemValueBox = new TextBox { Text = "0", VerticalContentAlignment = VerticalAlignment.Center };

        var gemCountPanel = new StackPanel();
        gemCountPanel.Children.Add(new TextBlock { Text = "Gems (qty)" });
        gemCountPanel.Children.Add(gemCountBox);
        Grid.SetRow(gemCountPanel, 0);
        Grid.SetColumn(gemCountPanel, 0);
        gemGrid.Children.Add(gemCountPanel);

        var gemValuePanel = new StackPanel();
        gemValuePanel.Children.Add(new TextBlock { Text = "Gem value each (gp)" });
        gemValuePanel.Children.Add(gemValueBox);
        Grid.SetRow(gemValuePanel, 0);
        Grid.SetColumn(gemValuePanel, 2);
        gemGrid.Children.Add(gemValuePanel);

        var gemQuickPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
        };
        var addOneGemButton = new Button
        {
            Content = "Add 1 Gem",
            Width = 100,
            Margin = new Thickness(0, 0, 8, 0),
        };
        addOneGemButton.Click += (_, _) =>
        {
            gemCountBox.Text = "1";
            if (string.IsNullOrWhiteSpace(gemValueBox.Text) || gemValueBox.Text.Trim() == "0")
                gemValueBox.Text = string.Empty;
            gemValueBox.Focus();
            gemValueBox.SelectAll();
        };
        gemQuickPanel.Children.Add(addOneGemButton);

        var gemQuickHint = new TextBlock
        {
            Text = "Quick-fill quantity and type value.",
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
        };
        gemQuickPanel.Children.Add(gemQuickHint);

        Grid.SetColumnSpan(gemQuickPanel, 3);
        Grid.SetRow(gemQuickPanel, 1);
        gemGrid.Children.Add(gemQuickPanel);

        gemSection.Child = gemGrid;
        Grid.SetRow(gemSection, 2);
        root.Children.Add(gemSection);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Apply", Width = 88, IsDefault = true };
        ok.Click += (_, _) =>
        {
            selectedPp = ParseNonNegativeInt(ppBox.Text);
            selectedGp = ParseNonNegativeInt(gpBox.Text);
            selectedSp = ParseNonNegativeInt(spBox.Text);
            selectedCp = ParseNonNegativeInt(cpBox.Text);
            selectedGemCount = ParseNonNegativeInt(gemCountBox.Text);
            selectedGemValueEachGp = ParseNonNegativeInt(gemValueBox.Text);
            dialog.DialogResult = true;
        };
        buttonPanel.Children.Add(cancel);
        buttonPanel.Children.Add(ok);
        Grid.SetRow(buttonPanel, 3);
        root.Children.Add(buttonPanel);

        dialog.Content = root;
        bool accepted = dialog.ShowDialog() == true;
        if (!accepted)
            return false;

        pp = selectedPp;
        gp = selectedGp;
        sp = selectedSp;
        cp = selectedCp;
        gemCount = selectedGemCount;
        gemValueEachGp = selectedGemValueEachGp;
        return true;
    }

    private static int ParseNonNegativeInt(string text)
    {
        return int.TryParse((text ?? string.Empty).Trim(), out var value)
            ? Math.Max(0, value)
            : 0;
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        if (_app.CharGen.SelectedEquipment.Count == 0)
            return;

        var result = MessageBox.Show(
            "Clear all selected equipment?",
            "Equipment",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        _app.CharGen.SelectedEquipment.Clear();
        _app.CharGen.EquippedArmorId = string.Empty;
        _app.CharGen.EquippedShieldId = string.Empty;
        _app.CharGen.EquippedWeaponId = string.Empty;
        _app.CharGen.EquippedMainWeaponId = string.Empty;
        _app.CharGen.EquippedOffHandWeaponId = string.Empty;
        _app.CharGen.EquippedMagicalItemIds.Clear();
        RefreshLists();
    }

    private void AvailableList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => AddSelected();
    private void SelectedList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => RemoveSelected();

    private void AvailableList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AvailableList.SelectedItem is not AvailableRow row)
            return;

        DescriptionPopupService.Show(
            Window.GetWindow(this),
            row.Item.ItemName,
            row.Description);
    }

    private void SelectedList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedList.SelectedItem is not SelectedRow { Selection: not null } row)
            return;

        DescriptionPopupService.Show(
            Window.GetWindow(this),
            row.Selection!.ItemName,
            row.Description);
    }

    private string BuildCatalogItemDescription(EquipmentCatalogItem item)
    {
        string baseDescription;
        string id = EquipmentLibraryService.CanonicalizeId(item.ItemId);
        if (_libraryById.TryGetValue(id, out var detail))
        {
            string rich = BuildDetailDescription(detail);
            if (!string.IsNullOrWhiteSpace(rich))
            {
                baseDescription = rich;
                return AppendSpellComponentUsageIfEnabled(baseDescription, item.ItemName, item.Category);
            }
        }

        baseDescription = BuildFallbackDescription(item.ItemName, item.Category, item.CostText);
        return AppendSpellComponentUsageIfEnabled(baseDescription, item.ItemName, item.Category);
    }

    private string BuildCatalogItemTooltipDescription(EquipmentCatalogItem item)
    {
        string baseDescription;
        string id = EquipmentLibraryService.CanonicalizeId(item.ItemId);
        if (_libraryById.TryGetValue(id, out var detail))
        {
            string rich = BuildDetailDescription(detail);
            baseDescription = string.IsNullOrWhiteSpace(rich)
                ? BuildFallbackDescription(item.ItemName, item.Category, item.CostText)
                : rich;
        }
        else
        {
            baseDescription = BuildFallbackDescription(item.ItemName, item.Category, item.CostText);
        }

        return AppendSpellComponentUsageIfEnabled(baseDescription, item.ItemName, item.Category, concise: true);
    }

    private string BuildSelectedItemDescription(EquipmentSelection selection)
    {
        string baseDescription;
        string id = EquipmentLibraryService.CanonicalizeId(selection.ItemId);
        if (_libraryById.TryGetValue(id, out var detail))
        {
            string rich = BuildDetailDescription(detail);
            if (!string.IsNullOrWhiteSpace(rich))
            {
                baseDescription = rich;
                return AppendSpellComponentUsageIfEnabled(baseDescription, selection.ItemName, selection.Category);
            }
        }

        baseDescription = BuildFallbackDescription(selection.ItemName, selection.Category, selection.CostText);
        return AppendSpellComponentUsageIfEnabled(baseDescription, selection.ItemName, selection.Category);
    }

    private string BuildSelectedItemTooltipDescription(EquipmentSelection selection)
    {
        string baseDescription;
        string id = EquipmentLibraryService.CanonicalizeId(selection.ItemId);
        if (_libraryById.TryGetValue(id, out var detail))
        {
            string rich = BuildDetailDescription(detail);
            baseDescription = string.IsNullOrWhiteSpace(rich)
                ? BuildFallbackDescription(selection.ItemName, selection.Category, selection.CostText)
                : rich;
        }
        else
        {
            baseDescription = BuildFallbackDescription(selection.ItemName, selection.Category, selection.CostText);
        }

        return AppendSpellComponentUsageIfEnabled(baseDescription, selection.ItemName, selection.Category, concise: true);
    }

    private string AppendSpellComponentUsageIfEnabled(string baseDescription, string itemName, string category, bool concise = false)
    {
        bool usingSpellComponents = _app.CharGen.UseSpellComponents && SupportsSpellComponentsForCurrentClass();
        if (!usingSpellComponents || !IsSpellComponentsCategory(category))
            return baseDescription;

        string usage = BuildSpellComponentUsageDescription(itemName, concise);
        if (string.IsNullOrWhiteSpace(usage))
            return baseDescription;

        if (string.IsNullOrWhiteSpace(baseDescription))
            return usage;

        return $"{baseDescription}\n\n{usage}";
    }

    private string BuildSpellComponentUsageDescription(string itemName, bool concise = false)
    {
        string normalizedItem = NormalizeComponentName(itemName);
        if (string.IsNullOrWhiteSpace(normalizedItem))
            return string.Empty;

        var usageLookup = concise ? _componentUsageConciseByNormalized : _componentUsageFullByNormalized;
        if (usageLookup.TryGetValue(normalizedItem, out string? cached))
            return cached;

        return concise
            ? "Spell Component Usage: no current matches."
            : "Spell Component Usage: no matching spells found from current class/spell access.";
    }

    private void RebuildSpellComponentUsageCaches(bool usingSpellComponents)
    {
        _componentUsageFullByNormalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _componentUsageConciseByNormalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!usingSpellComponents)
            return;

        var spellIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in _app.CharGen.WizardSpellbookIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
                spellIds.Add(id.Trim());
        }

        foreach (var list in _app.CharGen.WizardSpellLists)
        {
            foreach (string id in list?.SpellIds ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(id))
                    spellIds.Add(id.Trim());
            }
        }

        AddClassAccessibleSpellIdsForComponents(spellIds);

        var usageByComponent = new Dictionary<string, List<(int Level, string Name, string Type)>>(StringComparer.OrdinalIgnoreCase);

        foreach (string id in spellIds)
        {
            var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
            if (spell is null)
                continue;

            int level = ParseSpellLevelSafe(spell.Level);
            string name = string.IsNullOrWhiteSpace(spell.Name) ? spell.Id : spell.Name.Trim();
            string type = IsDivineSpell(spell) ? "Divine" : "Arcane";

            var normalizedComponents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var material in ExtractMaterialComponents(spell.Materials))
            {
                string normalized = NormalizeComponentName(material);
                if (!string.IsNullOrWhiteSpace(normalized))
                    normalizedComponents.Add(normalized);
            }

            foreach (var component in ExtractComponentColumnItems(spell.Components))
            {
                string normalized = NormalizeComponentName(component);
                if (!string.IsNullOrWhiteSpace(normalized))
                    normalizedComponents.Add(normalized);
            }

            foreach (string component in normalizedComponents)
            {
                if (!usageByComponent.TryGetValue(component, out var list))
                {
                    list = new List<(int Level, string Name, string Type)>();
                    usageByComponent[component] = list;
                }

                list.Add((level, name, type));
            }
        }

        foreach (var (component, usageList) in usageByComponent)
        {
            var ordered = usageList
                .Distinct()
                .OrderBy(entry => entry.Level)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ordered.Count == 0)
                continue;

            const int fullPreviewLimit = 12;
            var fullLines = ordered
                .Take(fullPreviewLimit)
                .Select(entry => $"- L{entry.Level} {entry.Name} [{entry.Type}]")
                .ToList();

            if (ordered.Count > fullPreviewLimit)
                fullLines.Add($"- +{ordered.Count - fullPreviewLimit} more");

            _componentUsageFullByNormalized[component] =
                $"Spell Component Usage ({ordered.Count} spell{(ordered.Count == 1 ? string.Empty : "s")}):\n{string.Join("\n", fullLines)}";

            var conciseSample = ordered
                .Take(2)
                .Select(entry => $"L{entry.Level} {entry.Name}")
                .ToList();
            string conciseSummary = string.Join(", ", conciseSample);
            if (ordered.Count > 2)
                conciseSummary += $", +{ordered.Count - 2} more";

            _componentUsageConciseByNormalized[component] =
                $"Spell Component Usage: {ordered.Count} spell{(ordered.Count == 1 ? string.Empty : "s")} ({conciseSummary}). Right-click for full list.";
        }
    }

    private static string BuildDetailDescription(CustomEquipmentData detail)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(detail.Description))
            parts.Add(detail.Description.Trim());

        if (detail.IsWeapon)
        {
            parts.Add($"Weapon: damage {detail.WeaponDamageSmallMedium}/{detail.WeaponDamageLarge}, speed {detail.WeaponSpeed}, type {detail.WeaponType}, size {detail.WeaponSize}.");
        }

        if (detail.IsArmor)
        {
            parts.Add($"Armor: AC {detail.ArmorClassValue}, rogue profile {detail.RogueArmorProfile}.");
        }

        if (detail.Weight > 0)
            parts.Add($"Weight: {detail.Weight:0.##} lb.");

        if (detail.CostGold > 0 || detail.CostSilver > 0 || detail.CostCopper > 0)
            parts.Add($"Cost: {FormatCoinCost(detail.CostGold, detail.CostSilver, detail.CostCopper)}.");

        if (detail.Categories.Count > 0)
            parts.Add($"Category: {string.Join(", ", detail.Categories)}.");

        return string.Join("\n", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static string BuildFallbackDescription(string itemName, string category, string costText)
    {
        var lines = new List<string>
        {
            string.IsNullOrWhiteSpace(itemName) ? "Equipment item." : itemName,
        };

        if (!string.IsNullOrWhiteSpace(category))
            lines.Add($"Category: {category}");
        if (!string.IsNullOrWhiteSpace(costText))
            lines.Add($"Cost: {costText}");

        lines.Add("No detailed description is available in the current equipment library entry yet.");
        return string.Join("\n", lines);
    }

    private static string FormatCoinCost(int gp, int sp, int cp)
    {
        var chunks = new List<string>();
        if (gp > 0) chunks.Add($"{gp} gp");
        if (sp > 0) chunks.Add($"{sp} sp");
        if (cp > 0) chunks.Add($"{cp} cp");
        return chunks.Count == 0 ? "0" : string.Join(" ", chunks);
    }

    // Suffix → Display prefix rules for canonical "Type, Modifier" sort order.
    // Longer/more-specific entries must come before shorter overlapping ones (e.g., " crossbow" before " bow").
    private static readonly (string Suffix, string Prefix)[] SortPrefixRules =
    {
        (" sword", "Sword"),
        (" shield", "Shield"),
        (" crossbow", "Crossbow"),
        (" bow", "Bow"),
        (" axe", "Axe"),
        (" mace", "Mace"),
        (" hammer", "Hammer"),
        (" spear", "Spear"),
        (" lance", "Lance"),
        (" staff", "Staff"),
        (" pike", "Pike"),
        (" flail", "Flail"),
        (" knife", "Knife"),
        (" dagger", "Dagger"),
        (" club", "Club"),
    };

    private static string NormalizeSwordName(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return itemName;

        string trimmed = itemName.Trim();

        // Already in canonical "Type, Modifier" form — leave as-is.
        foreach (var (_, prefix) in SortPrefixRules)
        {
            if (trimmed.StartsWith(prefix + ", ", StringComparison.OrdinalIgnoreCase))
                return trimmed;
        }

        // Handle "Base - Variant" (e.g., "Bastard sword - One-handed") — normalize base only.
        int dashIdx = trimmed.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIdx > 0)
        {
            string basePart = trimmed[..dashIdx];
            string variant = trimmed[(dashIdx + 3)..];
            string normalizedBase = ApplySortPrefix(basePart);
            return $"{normalizedBase} - {variant}";
        }

        return ApplySortPrefix(trimmed);
    }

    private static string ApplySortPrefix(string name)
    {
        string lower = name.ToLowerInvariant();
        foreach (var (suffix, prefix) in SortPrefixRules)
        {
            if (lower.EndsWith(suffix))
            {
                string modifier = name[..^suffix.Length].Trim();
                if (!string.IsNullOrWhiteSpace(modifier))
                    return $"{prefix}, {modifier}";
            }
        }
        return name;
    }

    private bool IsWizardCasterInCharGen()
    {
        var classIds = _app.CharGen.SelectedClassIds.Count > 0
            ? _app.CharGen.SelectedClassIds
            : string.IsNullOrWhiteSpace(_app.CharGen.ClassId)
                ? new List<string>()
                : new List<string> { _app.CharGen.ClassId };

        return classIds.Any(id =>
            string.Equals(id, "wizard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "mage", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "illusionist", StringComparison.OrdinalIgnoreCase));
    }

    private bool CanUseCatalogItem(EquipmentCatalogItem item, out string reason)
    {
        string canonId = EquipmentLibraryService.CanonicalizeId(item.ItemId);
        _libraryById.TryGetValue(canonId, out var detail);
        return CanUseArmorOrShield(
            detail?.IsArmor == true,
            detail?.IsShield == true,
            detail?.ArmorClassValue ?? 10,
            detail?.Name ?? item.ItemName,
            out reason);
    }

    private bool CanUseSelectedItem(EquipmentSelection selection, out string reason)
        => CanUseArmorOrShield(selection.IsArmor, selection.IsShield, selection.ArmorClassValue, selection.ItemName, out reason);

    private void EnforceEquippedRestrictions()
    {
        bool changed = false;

        if (!string.IsNullOrWhiteSpace(_app.CharGen.EquippedArmorId))
        {
            var equippedArmor = _app.CharGen.SelectedEquipment.FirstOrDefault(x =>
                string.Equals(EquipmentLibraryService.CanonicalizeId(x.ItemId), EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedArmorId), StringComparison.OrdinalIgnoreCase));

            if (equippedArmor is null || !CanUseSelectedItem(equippedArmor, out _))
            {
                _app.CharGen.EquippedArmorId = string.Empty;
                changed = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(_app.CharGen.EquippedShieldId))
        {
            var equippedShield = _app.CharGen.SelectedEquipment.FirstOrDefault(x =>
                string.Equals(EquipmentLibraryService.CanonicalizeId(x.ItemId), EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedShieldId), StringComparison.OrdinalIgnoreCase));

            if (equippedShield is null || !CanUseSelectedItem(equippedShield, out _))
            {
                _app.CharGen.EquippedShieldId = string.Empty;
                changed = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(_app.CharGen.EquippedMainWeaponId))
        {
            var equippedMain = _app.CharGen.SelectedEquipment.FirstOrDefault(x =>
                string.Equals(EquipmentLibraryService.CanonicalizeId(x.ItemId), EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedMainWeaponId), StringComparison.OrdinalIgnoreCase));

            if (equippedMain is null)
            {
                _app.CharGen.EquippedMainWeaponId = string.Empty;
                _app.CharGen.EquippedWeaponId = string.Empty;
                changed = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(_app.CharGen.EquippedOffHandWeaponId))
        {
            var equippedOff = _app.CharGen.SelectedEquipment.FirstOrDefault(x =>
                string.Equals(EquipmentLibraryService.CanonicalizeId(x.ItemId), EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedOffHandWeaponId), StringComparison.OrdinalIgnoreCase));

            if (equippedOff is null)
            {
                _app.CharGen.EquippedOffHandWeaponId = string.Empty;
                changed = true;
            }
            else if (string.Equals(
                    EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedMainWeaponId),
                    EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedOffHandWeaponId),
                    StringComparison.OrdinalIgnoreCase)
                && Math.Max(1, equippedOff.Quantity) < 2)
            {
                _app.CharGen.EquippedOffHandWeaponId = string.Empty;
                changed = true;
            }
        }

        _app.CharGen.EquippedWeaponId = _app.CharGen.EquippedMainWeaponId;

        _app.CharGen.EquippedMagicalItemIds ??= new List<string>();
        int removedMagical = _app.CharGen.EquippedMagicalItemIds.RemoveAll(id =>
            !_app.CharGen.SelectedEquipment.Any(x => string.Equals(
                EquipmentLibraryService.CanonicalizeId(x.ItemId),
                EquipmentLibraryService.CanonicalizeId(id),
                StringComparison.OrdinalIgnoreCase)));
        if (removedMagical > 0)
            changed = true;

        if (changed)
            SetTransactionStatus("Equipped slots were adjusted due to inventory or class restrictions.", Brushes.Orange);
    }

    private bool CanUseArmorOrShield(bool isArmor, bool isShield, int armorClassValue, string? armorName, out string reason)
    {
        reason = string.Empty;
        if (!isArmor && !isShield)
            return true;

        ArmorUsageRules rules = GetArmorUsageRules();
        if (isShield && !rules.CanUseShield)
        {
            reason = $"Shield use is not allowed ({rules.Source}).";
            return false;
        }

        if (isArmor)
        {
            ArmorTier tier = ClassifyArmorTier(armorClassValue);
            if (tier > rules.MaxArmorTier)
            {
                reason = $"{TierLabel(tier)} armor is not allowed ({rules.Source}).";
                return false;
            }

            var selectedArmors = GetClassAbilitySelectionValues("wizard_armored_wizard");
            if (selectedArmors.Count > 0 && IsWizardOnlyCurrentCharacter() && string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
            {
                bool matchesSelectedArmor = selectedArmors.Any(choice => ArmorMatchesSelection(choice, armorName, armorClassValue));
                if (!matchesSelectedArmor)
                {
                    reason = $"Armor is not in armored wizard selection ({string.Join(", ", selectedArmors)}).";
                    return false;
                }
            }
        }

        return true;
    }

    private ArmorUsageRules GetArmorUsageRules()
    {
        var classIds = _app.CharGen.SelectedClassIds.Count > 0
            ? _app.CharGen.SelectedClassIds
            : string.IsNullOrWhiteSpace(_app.CharGen.ClassId)
                ? new List<string>()
                : new List<string> { _app.CharGen.ClassId };

        bool isPlayersOption = string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);

        bool hasWizard = classIds.Any(IsWizardClassId);
        bool hasWarrior = classIds.Any(IsWarriorClassId);
        bool hasDruid = classIds.Any(IsDruidClassId);
        var selectedWeaponOptions = _app.CharGen.SelectedWeaponProficiencies
            .Select(x => x.ProficiencyId ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedArmorRestrictionIds = GetSelectedArmorRestrictionIds();

        ArmorTier maxTier = ArmorTier.Heavy;
        bool canShield = true;
        string source = isPlayersOption ? "PO defaults" : "Core defaults";

        // Core: dedicated wizard-type characters cannot wear armor or shields.
        if (!isPlayersOption && hasWizard && !hasWarrior)
        {
            maxTier = ArmorTier.None;
            canShield = false;
            source = "Core wizard restriction";
        }

        if (isPlayersOption)
        {
            if (hasWizard && !hasWarrior)
            {
                maxTier = ArmorTier.None;
                canShield = false;
                source = "PO wizard default";

                if (selectedWeaponOptions.Contains("armor_proficiency_heavy"))
                {
                    maxTier = ArmorTier.Heavy;
                    source = "PO armor proficiency (heavy)";
                }
                else if (selectedWeaponOptions.Contains("armor_proficiency_medium"))
                {
                    maxTier = ArmorTier.Medium;
                    source = "PO armor proficiency (medium)";
                }
                else if (selectedWeaponOptions.Contains("armor_proficiency_light")
                    || HasAnyIdContaining(selectedArmorRestrictionIds, "wizard_armored_wizard"))
                {
                    maxTier = ArmorTier.Light;
                    source = "PO armor proficiency (light)";
                }

                var selectedArmors = GetClassAbilitySelectionValues("wizard_armored_wizard");
                if (selectedArmors.Count > 0)
                {
                    maxTier = selectedArmors
                        .Select(GetSelectedArmorTier)
                        .DefaultIfEmpty(ArmorTier.Light)
                        .Max();
                    source = "PO armored wizard selection";
                }

                if (selectedWeaponOptions.Contains("shield_proficiency"))
                {
                    canShield = true;
                    source = source.Contains("shield", StringComparison.OrdinalIgnoreCase)
                        ? source
                        : $"{source}; shield proficiency";
                }
            }

            // PO class tradeoffs/disadvantages can cap armor tiers.
            if (hasWarrior || hasDruid)
            {
                if (HasAnyIdContaining(selectedArmorRestrictionIds, "limited_armor_none"))
                {
                    maxTier = MinTier(maxTier, ArmorTier.None);
                    source = "PO armor restriction (none)";
                }
                else if (HasAnyIdContaining(selectedArmorRestrictionIds, "limited_armor_studded"))
                {
                    maxTier = MinTier(maxTier, ArmorTier.Light);
                    source = "PO armor restriction (studded/light)";
                }
                else if (HasAnyIdContaining(selectedArmorRestrictionIds, "limited_armor_chain"))
                {
                    maxTier = MinTier(maxTier, ArmorTier.Medium);
                    source = "PO armor restriction (chain/medium)";
                }
                else if (HasAnyIdContaining(selectedArmorRestrictionIds, "druid_restriction_armor_restriction"))
                {
                    // Legacy druid armor restriction ID maps to chain-or-lighter.
                    maxTier = MinTier(maxTier, ArmorTier.Medium);
                    source = "PO armor restriction (chain/medium)";
                }
            }
        }

        return new ArmorUsageRules(maxTier, canShield, source);
    }

    private static ArmorTier MinTier(ArmorTier a, ArmorTier b)
        => a <= b ? a : b;

    private HashSet<string> GetSelectedArmorRestrictionIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string id in _app.CharGen.SelectedClassAbilityIds)
            ids.Add(id ?? string.Empty);

        foreach (var kvp in _app.CharGen.SelectedAbilitiesByClass)
        {
            foreach (string id in kvp.Value)
                ids.Add(id ?? string.Empty);
        }

        foreach (string id in _app.CharGen.SelectedDisadvantageSeverities.Keys)
            ids.Add(id ?? string.Empty);

        return ids;
    }

    private HashSet<string> GetClassAbilitySelectionValues(params string[] abilityIds)
    {
        var wanted = new HashSet<string>(abilityIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
            return values;

        foreach (string entry in _app.CharGen.SelectedClassAbilityIds)
            TryAddSelectionValue(entry, wanted, values);

        foreach (var byClass in _app.CharGen.SelectedAbilitiesByClass.Values)
        {
            foreach (string entry in byClass)
                TryAddSelectionValue(entry, wanted, values);
        }

        return values;
    }

    private static void TryAddSelectionValue(string? selectionEntry, HashSet<string> wantedIds, HashSet<string> values)
    {
        string baseId = RulesEngine.ExtractClassAbilityBaseId(selectionEntry);
        if (!wantedIds.Contains(baseId))
            return;

        string playerText = RulesEngine.ExtractClassAbilityPlayerText(selectionEntry);
        if (string.IsNullOrWhiteSpace(playerText))
            return;

        string formatted = RulesEngine.FormatClassAbilitySelectionText(baseId, playerText);
        if (!string.IsNullOrWhiteSpace(formatted))
            values.Add(formatted);
    }

    private static ArmorTier GetSelectedArmorTier(string selection)
        => NormalizeArmorSelection(selection) switch
        {
            "leather" or "studded leather" or "padded" or "hide" => ArmorTier.Light,
            "scale" or "chain" or "elven chain" => ArmorTier.Medium,
            "splint" or "banded" or "plate" or "field plate" or "full plate" => ArmorTier.Heavy,
            _ => ArmorTier.Light,
        };

    private static bool ArmorMatchesSelection(string selection, string? armorName, int armorClassValue)
    {
        string normalizedSelection = NormalizeArmorSelection(selection);
        if (string.IsNullOrWhiteSpace(normalizedSelection))
            return false;

        string normalizedArmorName = NormalizeArmorName(armorName);
        if (string.IsNullOrWhiteSpace(normalizedArmorName))
            return ClassifyArmorTier(armorClassValue) <= GetSelectedArmorTier(selection);

        return normalizedSelection switch
        {
            "leather" => normalizedArmorName.Contains("leather", StringComparison.OrdinalIgnoreCase)
                && !normalizedArmorName.Contains("studded", StringComparison.OrdinalIgnoreCase),
            "studded leather" => normalizedArmorName.Contains("studded", StringComparison.OrdinalIgnoreCase)
                && normalizedArmorName.Contains("leather", StringComparison.OrdinalIgnoreCase),
            "padded" => normalizedArmorName.Contains("padded", StringComparison.OrdinalIgnoreCase),
            "hide" => normalizedArmorName.Contains("hide", StringComparison.OrdinalIgnoreCase),
            "scale" => normalizedArmorName.Contains("scale", StringComparison.OrdinalIgnoreCase),
            "chain" => normalizedArmorName.Contains("chain", StringComparison.OrdinalIgnoreCase)
                && !normalizedArmorName.Contains("elven", StringComparison.OrdinalIgnoreCase),
            "elven chain" => normalizedArmorName.Contains("elven", StringComparison.OrdinalIgnoreCase)
                && normalizedArmorName.Contains("chain", StringComparison.OrdinalIgnoreCase),
            "splint" => normalizedArmorName.Contains("splint", StringComparison.OrdinalIgnoreCase),
            "banded" => normalizedArmorName.Contains("banded", StringComparison.OrdinalIgnoreCase),
            "plate" => normalizedArmorName.Contains("plate", StringComparison.OrdinalIgnoreCase)
                && !normalizedArmorName.Contains("field", StringComparison.OrdinalIgnoreCase)
                && !normalizedArmorName.Contains("full", StringComparison.OrdinalIgnoreCase),
            "field plate" => normalizedArmorName.Contains("field", StringComparison.OrdinalIgnoreCase)
                && normalizedArmorName.Contains("plate", StringComparison.OrdinalIgnoreCase),
            "full plate" => normalizedArmorName.Contains("full", StringComparison.OrdinalIgnoreCase)
                && normalizedArmorName.Contains("plate", StringComparison.OrdinalIgnoreCase),
            _ => normalizedArmorName.Contains(normalizedSelection, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static string NormalizeArmorSelection(string value)
    {
        string normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        normalized = normalized
            .Replace("armor", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("mail", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return normalized.ToLowerInvariant();
    }

    private static string NormalizeArmorName(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static bool HasAnyIdContaining(IEnumerable<string> ids, string token)
        => ids.Any(x => x.Contains(token, StringComparison.OrdinalIgnoreCase));

    private bool IsWizardOnlyCurrentCharacter()
    {
        var classIds = _app.CharGen.SelectedClassIds.Count > 0
            ? _app.CharGen.SelectedClassIds
            : string.IsNullOrWhiteSpace(_app.CharGen.ClassId)
                ? new List<string>()
                : new List<string> { _app.CharGen.ClassId };

        return classIds.Any(IsWizardClassId) && !classIds.Any(IsWarriorClassId);
    }

    private static bool IsWizardClassId(string classId)
        => string.Equals(classId, "wizard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "mage", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "illusionist", StringComparison.OrdinalIgnoreCase);

    private static bool IsWarriorClassId(string classId)
        => string.Equals(classId, "fighter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "ranger", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "paladin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "barbarian", StringComparison.OrdinalIgnoreCase);

    private static bool IsDruidClassId(string classId)
        => string.Equals(classId, "druid", StringComparison.OrdinalIgnoreCase);

    private static ArmorTier ClassifyArmorTier(int armorClassValue)
    {
        // AD&D lower AC means heavier armor quality.
        if (armorClassValue <= 4)
            return ArmorTier.Heavy;
        if (armorClassValue <= 6)
            return ArmorTier.Medium;
        return ArmorTier.Light;
    }

    private static string TierLabel(ArmorTier tier)
        => tier switch
        {
            ArmorTier.None => "No",
            ArmorTier.Light => "Light",
            ArmorTier.Medium => "Medium",
            _ => "Heavy",
        };
}
