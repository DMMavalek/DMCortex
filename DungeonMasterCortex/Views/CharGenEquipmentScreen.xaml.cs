using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CharGenEquipmentScreen : UserControl, IScreen
{
    private const string AllCategories = "(All Categories)";

    private readonly MainWindow _app;
    private EquipmentCatalog _catalog = new(Array.Empty<EquipmentCategory>(), "");
    private Dictionary<string, CustomEquipmentData> _libraryById = new(StringComparer.OrdinalIgnoreCase);

    private record AvailableRow(EquipmentCatalogItem Item, string Label, string CostText, string Description);
    private record SelectedRow(EquipmentSelection Selection, string Label, string QuantityText, string CostText, string Description, string SlotIcon);

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
            : "Character Generator  ›  Equipment");

        bool isPO = _app.CharGen.CharacterMode == "players_option";
        bool isWizardPO = isPO && string.Equals(_app.CharGen.ClassId, "wizard", StringComparison.OrdinalIgnoreCase);
        bool hasWizardSpecs = _app.Rules.Classes.TryGetValue("wizard", out var wc) && wc.Specializations is { Count: > 0 };
        int baseStepTotal = isPO
            ? (isWizardPO && hasWizardSpecs ? 13 : 12)
            : 10;
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
        PopulateCategoryFilter();
        RefreshLists();

        ImportStatusText.Text = string.IsNullOrWhiteSpace(_catalog.LoadMessage)
            ? "Tables XXXI (Weapons) and XXXII (Armor) are intentionally excluded for dedicated mechanics."
            : _catalog.LoadMessage;
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
                    slotIcon);
            })
            .ToList();

        AvailableList.ItemsSource = available;
        SelectedList.ItemsSource = selected;
        SelectedCountText.Text = $"{selected.Count} line item(s)";
        RefreshEquippedDisplay();
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
                CostCopperEach = detail?.CostCopper ?? 0,
                SizeClassEach = detail?.SizeClass ?? "Medium",
                WeightEach = detail?.Weight ?? 0,
            });
        }
        else
        {
            existing.Quantity = Math.Max(1, existing.Quantity + 1);
        }

        RefreshLists();
    }

    private void RemoveSelected()
    {
        if (SelectedList.SelectedItem is not SelectedRow row)
            return;

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
        bool hasSelection = SelectedList.SelectedItem is SelectedRow row;
        BtnRemove.IsEnabled = hasSelection;

        if (SelectedList.SelectedItem is SelectedRow sel)
        {
            // Resolve library data so we know what type this item is.
            string canonId = EquipmentLibraryService.CanonicalizeId(sel.Selection.ItemId);
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

    private void BtnEquip_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedList.SelectedItem is not SelectedRow row)
            return;

        string itemId = row.Selection.ItemId;
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
        if (SelectedList.SelectedItem is not SelectedRow row)
            return;

        DescriptionPopupService.Show(
            Window.GetWindow(this),
            row.Selection.ItemName,
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
}
