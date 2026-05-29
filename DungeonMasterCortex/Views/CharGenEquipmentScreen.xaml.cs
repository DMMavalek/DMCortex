using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
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

    private readonly MainWindow _app;
    private EquipmentCatalog _catalog = new(Array.Empty<EquipmentCategory>(), "");
    private Dictionary<string, CustomEquipmentData> _libraryById = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<string, bool>> ExpansionStateByContext = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressExpansionStateEvents;

    private enum ArmorTier
    {
        None = 0,
        Light = 1,
        Medium = 2,
        Heavy = 3,
    }

    private sealed record ArmorUsageRules(ArmorTier MaxArmorTier, bool CanUseShield, string Source);

    private record AvailableRow(EquipmentCatalogItem Item, string Label, string CostText, string Description);
    private record SelectedRow(EquipmentSelection? Selection, string Label, string QuantityText, string CostText, string Description, string SlotIcon, bool IsGem, string GroupName);

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
        _libraryById = new EquipmentLibraryService().GetEquipmentLibrary()
            .GroupBy(x => EquipmentLibraryService.CanonicalizeId(x.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        EnsureStartingFundsForCharGen();
        PopulateCategoryFilter();
        RefreshLists();
        SetTransactionStatus("Last transaction: none yet.", Brushes.Gray);

        ImportStatusText.Text = string.IsNullOrWhiteSpace(_catalog.LoadMessage)
            ? "Tables XXXI (Weapons) and XXXII (Armor) are intentionally excluded for dedicated mechanics."
            : _catalog.LoadMessage;
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
        categories.AddRange(_catalog.Categories
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(GetCategoryDisplayName));

        CategoryFilter.ItemsSource = categories;
        CategoryFilter.SelectedIndex = 0;
    }

    private void RefreshLists()
    {
        EnforceEquippedRestrictions();

        string category = CategoryFilter.SelectedItem as string ?? AllCategories;
        string search = EquipmentSearchBox?.Text.Trim() ?? string.Empty;
        var selectedById = _app.CharGen.SelectedEquipment
            .GroupBy(x => EquipmentLibraryService.CanonicalizeId(x.ItemId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(s => Math.Max(1, s.Quantity)), StringComparer.OrdinalIgnoreCase);

        var available = _catalog.Categories
            .Where(c => category == AllCategories || category.Equals(GetCategoryDisplayName(c), StringComparison.OrdinalIgnoreCase))
            .SelectMany(c => c.Items)
            .Where(i => string.IsNullOrWhiteSpace(search)
                || i.ItemName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || i.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
                || i.CostText.Contains(search, StringComparison.OrdinalIgnoreCase)
                || BuildCatalogItemDescription(i).Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(i => CanUseCatalogItem(i, out _))
            .OrderBy(i => i.Category)
            .ThenBy(i => NormalizeSwordName(i.ItemName), StringComparer.OrdinalIgnoreCase)
            .Select(i =>
            {
                string qtySuffix = selectedById.TryGetValue(EquipmentLibraryService.CanonicalizeId(i.ItemId), out int qty) ? $"  (owned x{qty})" : string.Empty;
                return new AvailableRow(i, $"{NormalizeSwordName(i.ItemName)}{qtySuffix}", i.CostText, BuildCatalogItemDescription(i));
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
                else if (string.Equals(EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedWeaponId), canonId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_app.CharGen.EquippedWeaponId))
                    slotIcon = "[W]";
                return new SelectedRow(
                    x,
                    NormalizeSwordName(x.ItemName),
                    $"x{Math.Max(1, x.Quantity)}",
                    x.CostText,
                    BuildSelectedItemDescription(x),
                    slotIcon,
                    false,
                    string.IsNullOrWhiteSpace(x.Category) ? "Other" : x.Category);
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
        int itemCostCopper = detailForCost is not null
            ? CharacterWealthService.ToCopper(detailForCost.CostGold, detailForCost.CostSilver, detailForCost.CostCopper)
            : ParseCostTextCopper(row.Item.CostText);

        if (!CanAffordAdditionalCopper(itemCostCopper))
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
                Category = row.Item.Category,
                ItemName = NormalizeSwordName(row.Item.ItemName),
                CostText = row.Item.CostText,
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
                CostGoldEach = detail?.CostGold ?? 0,
                CostSilverEach = detail?.CostSilver ?? 0,
                CostCopperEach = detail?.CostCopper ?? ParseCostTextCopper(row.Item.CostText),
                SizeClassEach = detail?.SizeClass ?? "Medium",
                WeightEach = detail?.Weight ?? 0,
            });
        }
        else
        {
            existing.Quantity = Math.Max(1, existing.Quantity + 1);
        }

        SetTransactionStatus(
            $"Purchased {NormalizeSwordName(row.Item.ItemName)} for {CharacterWealthService.FormatCoins(itemCostCopper)}.",
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

    private void AvailableList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnAdd.IsEnabled = AvailableList.SelectedItem is not null;
    }

    private void SelectedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = SelectedList.SelectedItem is SelectedRow { Selection: not null };
        BtnRemove.IsEnabled = hasSelection;
        BtnSetQuantity.IsEnabled = hasSelection;

        if (SelectedList.SelectedItem is SelectedRow { Selection: not null } sel)
        {
            // Resolve library data so we know what type this item is.
            string canonId = EquipmentLibraryService.CanonicalizeId(sel.Selection!.ItemId);
            _libraryById.TryGetValue(canonId, out var detail);
            bool isEquippable = detail?.IsArmor == true || detail?.IsShield == true || detail?.IsWeapon == true
                || sel.Selection.IsArmor || sel.Selection.IsShield || sel.Selection.IsWeapon;
            BtnEquip.IsEnabled = isEquippable;
        }
        else
        {
            BtnEquip.IsEnabled = false;
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
            string currentKey = EquipmentLibraryService.CanonicalizeId(_app.CharGen.EquippedWeaponId);
            _app.CharGen.EquippedWeaponId = string.Equals(currentKey, canonId, StringComparison.OrdinalIgnoreCase)
                ? ""
                : itemId;
        }

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
            EquippedWeaponText.Text = $"Weapon: {ArmorName(_app.CharGen.EquippedWeaponId)}";
    }

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
        string id = EquipmentLibraryService.CanonicalizeId(item.ItemId);
        if (_libraryById.TryGetValue(id, out var detail))
        {
            string rich = BuildDetailDescription(detail);
            if (!string.IsNullOrWhiteSpace(rich))
                return rich;
        }

        return BuildFallbackDescription(item.ItemName, item.Category, item.CostText);
    }

    private string BuildSelectedItemDescription(EquipmentSelection selection)
    {
        string id = EquipmentLibraryService.CanonicalizeId(selection.ItemId);
        if (_libraryById.TryGetValue(id, out var detail))
        {
            string rich = BuildDetailDescription(detail);
            if (!string.IsNullOrWhiteSpace(rich))
                return rich;
        }

        return BuildFallbackDescription(selection.ItemName, selection.Category, selection.CostText);
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
        return CanUseArmorOrShield(detail?.IsArmor == true, detail?.IsShield == true, detail?.ArmorClassValue ?? 10, out reason);
    }

    private bool CanUseSelectedItem(EquipmentSelection selection, out string reason)
        => CanUseArmorOrShield(selection.IsArmor, selection.IsShield, selection.ArmorClassValue, out reason);

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

        if (changed)
            SetTransactionStatus("Equipped armor/shield was cleared due to current class restrictions.", Brushes.Orange);
    }

    private bool CanUseArmorOrShield(bool isArmor, bool isShield, int armorClassValue, out string reason)
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

    private static bool HasAnyIdContaining(IEnumerable<string> ids, string token)
        => ids.Any(x => x.Contains(token, StringComparison.OrdinalIgnoreCase));

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
