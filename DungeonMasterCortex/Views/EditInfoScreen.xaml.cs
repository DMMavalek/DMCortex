using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

/// <summary>Display item for the NWP editor list. IsMissingPo drives the orange highlight.</summary>
internal record NwpListItem(string Display, bool IsMissingPo);

public partial class EditInfoScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    private static readonly string[] AbilityOrder  = { "str", "dex", "con", "int", "wis", "cha" };
    private static readonly string[] AbilityAbbrev = { "STR", "DEX", "CON", "INT", "WIS", "CHA" };

    private static readonly Dictionary<string, string> CoreAbilityDisplayToKey = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Strength"] = "Strength",
        ["Dexterity"] = "Dexterity",
        ["Constitution"] = "Constitution",
        ["Intelligence"] = "Intelligence",
        ["Wisdom"] = "Wisdom",
        ["Charisma"] = "Charisma",
        ["None"] = "",
    };

    // Mapping between ComboBox display labels and internal CheckAbility keys for PO base abilities/sub-abilities.
    private static readonly Dictionary<string, string> PoAbilityDisplayToKey = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Strength"]       = "Strength",
        ["Str — Stamina"]  = "str_stamina",
        ["Str — Muscle"]   = "str_muscle",
        ["Dexterity"]      = "Dexterity",
        ["Dex — Aim"]      = "dex_aim",
        ["Dex — Balance"]  = "dex_balance",
        ["Constitution"]   = "Constitution",
        ["Con — Health"]   = "con_health",
        ["Con — Fitness"]  = "con_fitness",
        ["Intelligence"]   = "Intelligence",
        ["Int — Reason"]   = "int_reason",
        ["Int — Knowledge"]= "int_knowledge",
        ["Wisdom"]         = "Wisdom",
        ["Wis — Intuition"]= "wis_intuition",
        ["Wis — Willpower"]= "wis_willpower",
        ["Wis — Perception"]= "wis_perception",
        ["Charisma"]       = "Charisma",
        ["Cha — Leadership"]= "cha_leadership",
        ["Cha — Appearance"]= "cha_appearance",
        ["None"]           = "",
    };
    private static readonly Dictionary<string, string> CoreAbilityKeyToDisplay =
        CoreAbilityDisplayToKey.ToDictionary(kv => kv.Value, kv => kv.Key, System.StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> PoAbilityKeyToDisplay =
        PoAbilityDisplayToKey.ToDictionary(kv => kv.Value, kv => kv.Key, System.StringComparer.OrdinalIgnoreCase);

    private string _selectedRaceId   = "";
    private string _selectedNwpId    = "";
    private string _selectedTraitId  = "";
    private string _selectedDisadvantageId = "";
    private string _selectedKitId = "";
    private string _selectedClassId = "";
    private string _selectedRaceAbilityId = "";
    private string _selectedMonsterId = "";
    private string _selectedSpellId = "";
    private string _activeSpellCategory = "arcane";
    private List<MonsterDefinition> _monsterItems = new();
    private List<SpellDefinition> _spellItems = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<DragonAgeStageVm> _ageStageRows = new();
    private bool _updatingNwpUi;
    private bool _creatingNewNwp;
    private List<AbilityDefinition> _workingRacialAbilities = new();
    private int _selectedRacialAbilityIndex = -1;
    private List<AbilityDefinition> _workingClassAbilities = new();
    private int _selectedClassAbilityIndex = -1;
    private List<ClassDefinition> _classCopyTargets = new();
    private List<RaceDefinition> _raceCopyTargets = new();
    private readonly Dictionary<string, NonweaponProficiencySetting> _nwpSettings
        = new(System.StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, CustomNwpData> _customNwpIds = new(System.StringComparer.OrdinalIgnoreCase);
    private List<NonweaponProficiencyDefinition> _nwpItems = new();
    private List<TraitDefinition> _traitItems = new();
    private List<DisadvantageDefinition> _disadvantageItems = new();
    private List<KitDefinition> _kitItems = new();
    private List<CustomEquipmentData> _equipmentItems = new();
    private string _selectedEquipmentId = "";
    private const string EquipmentAllCategories = "(All Categories)";
    private bool _showMagicalEquipment = false;
    private bool _demoViewOnlyNoticeShown;

    private sealed record ClassCopyTargetOption(string Id, string Name)
    {
        public override string ToString() => $"{Name} [{Id}]";
    }

    private sealed record RaceCopyTargetOption(string Id, string Name)
    {
        public override string ToString() => $"{Name} [{Id}]";
    }

    public EditInfoScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(HandleDemoViewOnlyButtonGuard), true);
        ShowTab("chars");
    }

    public void OnEnter()
    {
        _app.SetBanner(_app.License.IsDemoMode
            ? "DM Tools  ›  Edit Information (View Only Demo)"
            : "DM Tools  ›  Edit Information");

        if (_app.License.IsDemoMode && !_demoViewOnlyNoticeShown)
        {
            _demoViewOnlyNoticeShown = true;
            MessageBox.Show(
                "Demo mode is view only in Edit Information. You can browse data, but add/save/delete actions are locked.",
                "Demo Mode",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        var requestedTab = _app.PendingEditInfoTab;
        _app.PendingEditInfoTab = "";
        int pendingCharacterIndex = _app.PendingEditCharacterIndex;
        bool pendingLevelUpMode = _app.PendingCharacterLevelUpMode;
        _app.PendingEditCharacterIndex = -1;
        _app.PendingCharacterLevelUpMode = false;

        if (pendingCharacterIndex >= 0 && pendingCharacterIndex < _app.Characters.Count)
        {
            ShowTab("chars");
            CharList.SelectedIndex = pendingCharacterIndex;
            CharList.ScrollIntoView(CharList.Items[pendingCharacterIndex]);
            if (pendingLevelUpMode)
            {
                CharLevelupInfo.Text = "Opened from roster UPDATE. Add XP/HP/CP, then apply update.";
                CharXpGainInput.Focus();
            }
            return;
        }

        if (requestedTab is "chars" or "nwps" or "traits" or "disadvantages" or "equipment" or "equipmnet" or "races" or "racial_abilities" or "classes" or "class_abilities" or "spells" or "spells_arcane" or "spells_divine" or "spells_psionic")
            ShowTab(requestedTab);
        else
            ShowTab("chars");
    }

    private void HandleDemoViewOnlyButtonGuard(object sender, RoutedEventArgs e)
    {
        if (!_app.License.IsDemoMode)
            return;

        if (e.OriginalSource is not Button button)
            return;

        var name = button.Name ?? string.Empty;
        if (name.StartsWith("Tab", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "BtnHub", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Handled = true;
        MessageBox.Show(
            "This action is locked in demo mode. Activate to enable editing.",
            "Demo Mode",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void TabChars_Click(object sender, RoutedEventArgs e)     => ShowTab("chars");
    private void TabOptions_Click(object sender, RoutedEventArgs e)   => ShowTab("nwps");
    private void TabRaces_Click(object sender, RoutedEventArgs e)     => ShowTab("races");
    private void TabNwps_Click(object sender, RoutedEventArgs e)      => ShowTab("nwps");
    private void TabTraits_Click(object sender, RoutedEventArgs e)    => ShowTab("traits");
    private void TabDisadvantages_Click(object sender, RoutedEventArgs e) => ShowTab("disadvantages");
    private void TabBtnKits_Click(object sender, RoutedEventArgs e)       => ShowTab("kits");
    private void TabEquipmnet_Click(object sender, RoutedEventArgs e)  => ShowTab("equipmnet");
    private void TabBtnMultiClass_Click(object sender, RoutedEventArgs e) => ShowTab("multiclass");
    private void TabClasses_Click(object sender, RoutedEventArgs e)   => ShowTab("classes");
    private void TabClassAbilities_Click(object sender, RoutedEventArgs e) => ShowTab("class_abilities");
    private void TabRacialAbilities_Click(object sender, RoutedEventArgs e) => ShowTab("racial_abilities");
    private void TabBtnSpells_Click(object sender, RoutedEventArgs e) => ShowTab("spells_arcane");
    private void TabSpellsArcane_Click(object sender, RoutedEventArgs e) => ShowTab("spells_arcane");
    private void TabSpellsDivine_Click(object sender, RoutedEventArgs e) => ShowTab("spells_divine");
    private void TabSpellsPsionic_Click(object sender, RoutedEventArgs e) => ShowTab("spells_psionic");

    private void ShowTab(string tab)
    {
        if (tab == "equipment")
            tab = "equipmnet";
        if (tab == "spells")
            tab = "spells_arcane";

        bool inOptions = tab is "nwps" or "traits" or "disadvantages" or "kits" or "multiclass";
        bool inRaces = tab is "races" or "racial_abilities";
        bool inClasses = tab is "classes" or "class_abilities";
        bool inSpells = tab is "spells_arcane" or "spells_divine" or "spells_psionic";

        if (inSpells)
        {
            string nextSpellCategory = tab switch
            {
                "spells_divine" => "divine",
                "spells_psionic" => "psionic",
                _ => "arcane",
            };

            if (!string.Equals(_activeSpellCategory, nextSpellCategory, System.StringComparison.OrdinalIgnoreCase))
            {
                _activeSpellCategory = nextSpellCategory;
                var selectedSpell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, _selectedSpellId, System.StringComparison.OrdinalIgnoreCase));
                if (selectedSpell is null || !string.Equals(selectedSpell.Category, _activeSpellCategory, System.StringComparison.OrdinalIgnoreCase))
                {
                    _selectedSpellId = string.Empty;
                    ClearSpellEditor();
                }
            }
        }

        TabChars.Visibility     = tab == "chars"     ? Visibility.Visible : Visibility.Collapsed;
        TabRaces.Visibility     = tab == "races"     ? Visibility.Visible : Visibility.Collapsed;
        TabNwps.Visibility      = tab == "nwps"      ? Visibility.Visible : Visibility.Collapsed;
        TabTraits.Visibility    = tab == "traits"    ? Visibility.Visible : Visibility.Collapsed;
        TabDisadvantages.Visibility = tab == "disadvantages" ? Visibility.Visible : Visibility.Collapsed;
        TabKits.Visibility          = tab == "kits"          ? Visibility.Visible : Visibility.Collapsed;
        TabEquipment.Visibility = tab == "equipmnet" ? Visibility.Visible : Visibility.Collapsed;
        TabMultiClass.Visibility    = tab == "multiclass"     ? Visibility.Visible : Visibility.Collapsed;
        TabClasses.Visibility = tab == "classes" ? Visibility.Visible : Visibility.Collapsed;
        TabClassAbilities.Visibility = tab == "class_abilities" ? Visibility.Visible : Visibility.Collapsed;
        TabRacialAbilities.Visibility = tab == "racial_abilities" ? Visibility.Visible : Visibility.Collapsed;
        TabMonsters.Visibility = tab == "monsters" ? Visibility.Visible : Visibility.Collapsed;
        TabSpells.Visibility = inSpells ? Visibility.Visible : Visibility.Collapsed;

        OptionsSubTabStrip.Visibility = inOptions ? Visibility.Visible : Visibility.Collapsed;
        RacesSubTabStrip.Visibility = inRaces ? Visibility.Visible : Visibility.Collapsed;
        ClassesSubTabStrip.Visibility = inClasses ? Visibility.Visible : Visibility.Collapsed;
        SpellsSubTabStrip.Visibility = inSpells ? Visibility.Visible : Visibility.Collapsed;

        var active   = (System.Windows.Media.Brush)FindResource("BrushBtnAct");
        var inactive = (System.Windows.Media.Brush)FindResource("BrushBtn");
        TabBtnChars.Background     = tab == "chars" ? active : inactive;
        TabBtnOptions.Background   = inOptions ? active : inactive;
        TabBtnEquipmnet.Background = tab == "equipmnet" ? active : inactive;
        TabBtnRaces.Background     = inRaces ? active : inactive;
        TabBtnClasses.Background   = inClasses ? active : inactive;
        TabBtnMonsters.Background  = tab == "monsters" ? active : inactive;
        TabBtnSpells.Background    = inSpells ? active : inactive;
        TabBtnNwps.Background      = tab == "nwps" ? active : inactive;
        TabBtnTraits.Background    = tab == "traits" ? active : inactive;
        TabBtnDisadvantages.Background = tab == "disadvantages" ? active : inactive;
        TabBtnKits.Background          = tab == "kits"          ? active : inactive;
        TabSubBtnRaces.Background  = tab == "races" ? active : inactive;
        TabBtnMultiClass.Background    = tab == "multiclass"     ? active : inactive;
        TabBtnRacialAbilities.Background = tab == "racial_abilities" ? active : inactive;
        TabSubBtnClasses.Background = tab == "classes" ? active : inactive;
        TabBtnClassAbilities.Background = tab == "class_abilities" ? active : inactive;
        TabBtnSpellsArcane.Background = tab == "spells_arcane" ? active : inactive;
        TabBtnSpellsDivine.Background = tab == "spells_divine" ? active : inactive;
        TabBtnSpellsPsionic.Background = tab == "spells_psionic" ? active : inactive;

        if (tab == "chars")     RefreshCharList();
        if (tab == "races")     RefreshRaceList();
        if (tab == "nwps")      RefreshNwpList();
        if (tab == "traits")    RefreshTraitEditorList();
        if (tab == "disadvantages") RefreshDisadvantageEditorList();
        if (tab == "kits")          RefreshKitEditorList();
        if (tab == "equipmnet") RefreshEquipmentEditor();
        if (tab == "multiclass")    RefreshMultiClassEditor();
        if (tab == "classes") RefreshClassEditorList();
        if (tab == "class_abilities") RefreshClassAbilityEditorList();
        if (tab == "racial_abilities") RefreshRacialAbilityEditorList();
        if (tab == "monsters")
        {
            // Always open Monsters tab in an unfiltered state to avoid hidden subsets.
            if (!string.IsNullOrEmpty(MonsterSearchBox.Text))
                MonsterSearchBox.Text = string.Empty;

            if (MonsterTypeFilter.Items.Count > 0)
            {
                int allTypesIndex = MonsterTypeFilter.Items.IndexOf("(All Types)");
                MonsterTypeFilter.SelectedIndex = allTypesIndex >= 0 ? allTypesIndex : 0;
            }

            RefreshMonsterEditor();
        }
        if (inSpells)
            RefreshSpellEditor();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) =>
        _app.GoTo("dm_tools", -1);

    // ── Equipment tab ────────────────────────────────────────────────────────

    private sealed record EquipmentListItem(CustomEquipmentData Item)
    {
        public override string ToString()
        {
            string cost = FormatCost(Item);
            return string.IsNullOrWhiteSpace(cost)
                ? Item.Name
                : $"{Item.Name} ({cost})";
        }

        private static string FormatCost(CustomEquipmentData item)
        {
            var parts = new List<string>();
            if (item.CostGold > 0) parts.Add($"{item.CostGold}gp");
            if (item.CostSilver > 0) parts.Add($"{item.CostSilver}sp");
            if (item.CostCopper > 0) parts.Add($"{item.CostCopper}cp");
            return string.Join(" ", parts);
        }
    }

    private void RefreshEquipmentEditor()
    {
        _equipmentItems = new EquipmentLibraryService().GetEquipmentLibrary().ToList();
        EquipmentEditorInfo.Text = "Edit equipment definitions here. Categories and tags are separate from item names.";
        ApplyEquipmentTypeTabStyles();
        PopulateEquipmentCategoryFilter();
        RefreshEquipmentList();
        if (EquipmentListEditor.SelectedItem is not EquipmentListItem && _equipmentItems.Count > 0)
            EquipmentListEditor.SelectedIndex = 0;
        if (_equipmentItems.Count == 0)
            NewEquipmentEditorEntry();
    }

    private void PopulateEquipmentCategoryFilter()
    {
        var categories = new List<string> { EquipmentAllCategories };
        categories.AddRange(_equipmentItems
            .Where(x => x.IsMagical == _showMagicalEquipment)
            .SelectMany(x => x.Categories)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(x.ToLowerInvariant()))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase));

        string previous = EquipmentCategoryFilter.SelectedItem as string ?? EquipmentAllCategories;
        EquipmentCategoryFilter.ItemsSource = categories;
        EquipmentCategoryFilter.SelectedItem = categories.Contains(previous) ? previous : EquipmentAllCategories;
    }

    private void RefreshEquipmentList()
    {
        string search = (EquipmentSearchBox?.Text ?? string.Empty).Trim();
        string category = EquipmentCategoryFilter.SelectedItem as string ?? EquipmentAllCategories;

        var filtered = _equipmentItems
            .Where(item => item.IsMagical == _showMagicalEquipment)
            .Where(item => category == EquipmentAllCategories
                || item.Categories.Contains(category, System.StringComparer.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(search)
                || item.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                || item.Categories.Any(c => c.Contains(search, System.StringComparison.OrdinalIgnoreCase))
                || item.ItemTags.Any(t => t.Contains(search, System.StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Name, System.StringComparer.OrdinalIgnoreCase)
            .Select(item => new EquipmentListItem(item))
            .ToList();

        EquipmentListEditor.ItemsSource = filtered;

        if (!string.IsNullOrWhiteSpace(_selectedEquipmentId))
        {
            var selected = filtered.FirstOrDefault(x => string.Equals(x.Item.Id, _selectedEquipmentId, System.StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
                EquipmentListEditor.SelectedItem = selected;
        }
    }

    private void EquipmentListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EquipmentListEditor.SelectedItem is not EquipmentListItem selected)
        {
            BtnDeleteEquipment.IsEnabled = false;
            return;
        }

        BtnDeleteEquipment.IsEnabled = true;
        _selectedEquipmentId = selected.Item.Id;
        LoadEquipmentEditor(selected.Item);
    }

    private void LoadEquipmentEditor(CustomEquipmentData item)
    {
        EquipmentEditorTitle.Text = $"Equipment Editor  —  {item.Name}";
        EquipmentEditId.Text = item.Id;
        EquipmentEditName.Text = item.Name;
        EquipmentEditCategories.Text = string.Join(", ", item.Categories);
        EquipmentEditTags.Text = string.Join(", ", item.ItemTags);
        EquipmentIsMagical.IsChecked = item.IsMagical;
        EquipmentEditDescription.Text = item.Description;
        EquipmentEditWeight.Text = item.Weight.ToString(CultureInfo.InvariantCulture);
        EquipmentEditCostGold.Text = item.CostGold.ToString(CultureInfo.InvariantCulture);
        EquipmentEditCostSilver.Text = item.CostSilver.ToString(CultureInfo.InvariantCulture);
        EquipmentEditCostCopper.Text = item.CostCopper.ToString(CultureInfo.InvariantCulture);
        EquipmentIsArmor.IsChecked = item.IsArmor;
        EquipmentArmorClassValue.Text = item.ArmorClassValue.ToString(CultureInfo.InvariantCulture);
        SelectComboBoxItemByTag(EquipmentRogueArmorProfile, item.RogueArmorProfile);
        EquipmentIsWeapon.IsChecked = item.IsWeapon;
        EquipmentWeaponSpeed.Text = item.WeaponSpeed.ToString(CultureInfo.InvariantCulture);
        EquipmentWeaponDamageSmMd.Text = item.WeaponDamageSmallMedium;
        EquipmentWeaponDamageLarge.Text = item.WeaponDamageLarge;
        SelectComboBoxItemByTag(EquipmentWeaponType, item.WeaponType);
        SelectComboBoxItemByTag(EquipmentWeaponSize, item.WeaponSize);

        SelectComboBoxItemByText(EquipmentEditSize, item.SizeClass);

        EquipmentIsContainer.IsChecked = item.IsContainer;
        EquipmentContainerMaxItems.Text = item.ContainerMaxItems.ToString(CultureInfo.InvariantCulture);
        EquipmentContainerMaxWeight.Text = item.ContainerMaxWeight.ToString(CultureInfo.InvariantCulture);
        EquipmentAllowedTags.Text = string.Join(", ", item.AllowedContentTags);
        UpdateArmorEditorEnabledState();
        UpdateWeaponEditorEnabledState();
        UpdateContainerEditorEnabledState();
    }

    private void NewEquipmentEditorEntry()
    {
        _selectedEquipmentId = "";
        BtnDeleteEquipment.IsEnabled = false;
        EquipmentEditorTitle.Text = "Equipment Editor  —  New Item";
        EquipmentEditId.Text = "(new)";
        EquipmentEditName.Text = "";
        EquipmentEditCategories.Text = "Adventuring Gear";
        EquipmentEditTags.Text = "";
        EquipmentIsMagical.IsChecked = _showMagicalEquipment;
        EquipmentEditDescription.Text = "";
        EquipmentEditWeight.Text = "0";
        EquipmentEditCostGold.Text = "0";
        EquipmentEditCostSilver.Text = "0";
        EquipmentEditCostCopper.Text = "0";
        EquipmentIsArmor.IsChecked = false;
        EquipmentArmorClassValue.Text = "10";
        SelectComboBoxItemByTag(EquipmentRogueArmorProfile, "no_armor");
        EquipmentIsWeapon.IsChecked = false;
        EquipmentWeaponSpeed.Text = "0";
        EquipmentWeaponDamageSmMd.Text = "";
        EquipmentWeaponDamageLarge.Text = "";
        SelectComboBoxItemByTag(EquipmentWeaponType, "");
        SelectComboBoxItemByTag(EquipmentWeaponSize, "");
        SelectComboBoxItemByText(EquipmentEditSize, "Medium");
        EquipmentIsContainer.IsChecked = false;
        EquipmentContainerMaxItems.Text = "0";
        EquipmentContainerMaxWeight.Text = "0";
        EquipmentAllowedTags.Text = "";
        UpdateArmorEditorEnabledState();
        UpdateWeaponEditorEnabledState();
        UpdateContainerEditorEnabledState();
    }

    private void EquipmentCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RefreshEquipmentList();

    private void EquipmentRegularTabBtn_Click(object sender, RoutedEventArgs e)
    {
        _showMagicalEquipment = false;
        ApplyEquipmentTypeTabStyles();
        PopulateEquipmentCategoryFilter();
        RefreshEquipmentList();
        NewEquipmentEditorEntry();
    }

    private void EquipmentMagicalTabBtn_Click(object sender, RoutedEventArgs e)
    {
        _showMagicalEquipment = true;
        ApplyEquipmentTypeTabStyles();
        PopulateEquipmentCategoryFilter();
        RefreshEquipmentList();
        NewEquipmentEditorEntry();
    }

    private void ApplyEquipmentTypeTabStyles()
    {
        var active = (System.Windows.Media.Brush)FindResource("BrushBtnAct");
        var inactive = (System.Windows.Media.Brush)FindResource("BrushBtn");
        EquipmentRegularTabBtn.Background = _showMagicalEquipment ? inactive : active;
        EquipmentMagicalTabBtn.Background = _showMagicalEquipment ? active : inactive;
    }

    private void EquipmentSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshEquipmentList();

    private void BtnNewEquipment_Click(object sender, RoutedEventArgs e)
    {
        EquipmentListEditor.SelectedIndex = -1;
        NewEquipmentEditorEntry();
    }

    private void BtnDeleteEquipment_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedEquipmentId))
            return;

        var existing = _equipmentItems.FirstOrDefault(x => string.Equals(x.Id, _selectedEquipmentId, System.StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            return;

        var result = MessageBox.Show(
            $"Delete equipment item '{existing.Name}'?",
            "Equipment",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        _equipmentItems.Remove(existing);
        new EquipmentLibraryService().SaveEquipmentLibrary(_equipmentItems);
        RefreshEquipmentEditor();
        EquipmentEditorInfo.Text = $"Deleted '{existing.Name}'.";
    }

    private void BtnCleanupEquipment_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Run cleanup on all equipment items now?\n\nThis will normalize spacing and remove category prefixes from names.",
            "Cleanup Equipment Library",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        var cleanup = new EquipmentLibraryService().CleanupEquipmentLibrary();
        RefreshEquipmentEditor();
        EquipmentEditorInfo.Text = $"Cleanup complete: {cleanup.ChangedItems} updated out of {cleanup.TotalItems} item(s).";
    }

    private void BtnRefreshEquipment_Click(object sender, RoutedEventArgs e)
    {
        RefreshEquipmentEditor();
        EquipmentEditorInfo.Text = "Equipment library reloaded.";
    }

    private void EquipmentIsContainer_Changed(object sender, RoutedEventArgs e)
        => UpdateContainerEditorEnabledState();

    private void EquipmentIsArmor_Changed(object sender, RoutedEventArgs e)
        => UpdateArmorEditorEnabledState();

    private void EquipmentIsWeapon_Changed(object sender, RoutedEventArgs e)
        => UpdateWeaponEditorEnabledState();

    private void UpdateArmorEditorEnabledState()
    {
        bool enabled = EquipmentIsArmor.IsChecked == true;
        EquipmentArmorClassValue.IsEnabled = enabled;
        EquipmentRogueArmorProfile.IsEnabled = enabled;
    }

    private void UpdateWeaponEditorEnabledState()
    {
        bool enabled = EquipmentIsWeapon.IsChecked == true;
        EquipmentWeaponSpeed.IsEnabled = enabled;
        EquipmentWeaponDamageSmMd.IsEnabled = enabled;
        EquipmentWeaponDamageLarge.IsEnabled = enabled;
        EquipmentWeaponType.IsEnabled = enabled;
        EquipmentWeaponSize.IsEnabled = enabled;
    }

    private void UpdateContainerEditorEnabledState()
    {
        bool enabled = EquipmentIsContainer.IsChecked == true;
        EquipmentContainerMaxItems.IsEnabled = enabled;
        EquipmentContainerMaxWeight.IsEnabled = enabled;
        EquipmentAllowedTags.IsEnabled = enabled;
    }

    private void BtnSaveEquipment_Click(object sender, RoutedEventArgs e)
    {
        string name = (EquipmentEditName.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            EquipmentEditorInfo.Text = "Name is required.";
            return;
        }

        var categories = ParseTokenList(EquipmentEditCategories.Text);
        if (categories.Count == 0)
            categories.Add("Uncategorized");

        var tags = ParseTokenList(EquipmentEditTags.Text)
            .Select(x => x.ToLowerInvariant())
            .ToList();

        bool isContainer = EquipmentIsContainer.IsChecked == true;
        var allowedTags = ParseTokenList(EquipmentAllowedTags.Text)
            .Select(x => x.ToLowerInvariant())
            .ToList();

        string existingId = string.IsNullOrWhiteSpace(_selectedEquipmentId) ? "" : _selectedEquipmentId;
        string candidateId = string.IsNullOrWhiteSpace(existingId)
            ? EquipmentLibraryService.BuildId(name)
            : existingId;
        string finalId = EnsureUniqueEquipmentId(candidateId, existingId);

        var updated = new CustomEquipmentData
        {
            Id = finalId,
            Name = name,
            Description = (EquipmentEditDescription.Text ?? string.Empty).Trim(),
            IsMagical = EquipmentIsMagical.IsChecked == true,
            Categories = categories,
            ItemTags = tags,
            SizeClass = GetComboBoxText(EquipmentEditSize, "Medium"),
            Weight = ParseNonNegativeDouble(EquipmentEditWeight.Text),
            IsArmor = EquipmentIsArmor.IsChecked == true,
            ArmorClassValue = EquipmentIsArmor.IsChecked == true
                ? Math.Clamp(ParseIntAllowNegative(EquipmentArmorClassValue.Text), -10, 10)
                : 10,
            RogueArmorProfile = EquipmentIsArmor.IsChecked == true
                ? GetComboBoxTag(EquipmentRogueArmorProfile, "no_armor")
                : "no_armor",
            IsWeapon = EquipmentIsWeapon.IsChecked == true,
            WeaponSpeed = EquipmentIsWeapon.IsChecked == true
                ? ParseNonNegativeInt(EquipmentWeaponSpeed.Text)
                : 0,
            WeaponDamageSmallMedium = EquipmentIsWeapon.IsChecked == true
                ? (EquipmentWeaponDamageSmMd.Text ?? string.Empty).Trim()
                : string.Empty,
            WeaponDamageLarge = EquipmentIsWeapon.IsChecked == true
                ? (EquipmentWeaponDamageLarge.Text ?? string.Empty).Trim()
                : string.Empty,
            WeaponType = EquipmentIsWeapon.IsChecked == true
                ? GetComboBoxTag(EquipmentWeaponType, string.Empty)
                : string.Empty,
            WeaponSize = EquipmentIsWeapon.IsChecked == true
                ? GetComboBoxTag(EquipmentWeaponSize, string.Empty)
                : string.Empty,
            CostGold = ParseNonNegativeInt(EquipmentEditCostGold.Text),
            CostSilver = ParseNonNegativeInt(EquipmentEditCostSilver.Text),
            CostCopper = ParseNonNegativeInt(EquipmentEditCostCopper.Text),
            IsContainer = isContainer,
            ContainerMaxItems = isContainer ? ParseNonNegativeInt(EquipmentContainerMaxItems.Text) : 0,
            ContainerMaxWeight = isContainer ? ParseNonNegativeDouble(EquipmentContainerMaxWeight.Text) : 0,
            AllowedContentTags = isContainer ? allowedTags : new List<string>(),
        };

        int existingIndex = _equipmentItems.FindIndex(x => string.Equals(x.Id, existingId, System.StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            _equipmentItems[existingIndex] = updated;
        else
            _equipmentItems.Add(updated);

        new EquipmentLibraryService().SaveEquipmentLibrary(_equipmentItems);

        _selectedEquipmentId = updated.Id;
        RefreshEquipmentEditor();
        EquipmentEditorInfo.Text = $"Saved '{updated.Name}'.";
    }

    private string EnsureUniqueEquipmentId(string candidateId, string currentId)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
            candidateId = "equipment_item";

        if (_equipmentItems.All(x => string.Equals(x.Id, candidateId, System.StringComparison.OrdinalIgnoreCase)
            ? string.Equals(x.Id, currentId, System.StringComparison.OrdinalIgnoreCase)
            : true))
        {
            return candidateId;
        }

        int suffix = 2;
        while (true)
        {
            string test = $"{candidateId}_{suffix}";
            bool exists = _equipmentItems.Any(x => string.Equals(x.Id, test, System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(x.Id, currentId, System.StringComparison.OrdinalIgnoreCase));
            if (!exists)
                return test;
            suffix++;
        }
    }

    private static List<string> ParseTokenList(string? text)
    {
        return (text ?? string.Empty)
            .Split(',')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static double ParseNonNegativeDouble(string? text)
    {
        if (double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return System.Math.Max(0, value);
        return 0;
    }

    private static void SelectComboBoxItemByText(ComboBox combo, string text)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi
                && string.Equals(cbi.Content?.ToString(), text, System.StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = cbi;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static string GetComboBoxText(ComboBox combo, string fallback)
    {
        if (combo.SelectedItem is ComboBoxItem cbi)
            return cbi.Content?.ToString() ?? fallback;
        return fallback;
    }

    private static void SelectComboBoxItemByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi
                && string.Equals(cbi.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = cbi;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static string GetComboBoxTag(ComboBox combo, string fallback)
    {
        if (combo.SelectedItem is ComboBoxItem cbi)
            return cbi.Tag?.ToString() ?? fallback;
        return fallback;
    }

    private static int ParseIntAllowNegative(string? text)
    {
        return int.TryParse((text ?? string.Empty).Trim(), out int value)
            ? value
            : 10;
    }

    // ── Characters tab ────────────────────────────────────────────────────────

    private void RefreshCharList()
    {
        CharList.Items.Clear();
        foreach (var c in _app.Characters)
            CharList.Items.Add($"{c.Name}  ({c.RaceName} {c.ClassName})");
    }

    private void BtnRefreshChars_Click(object sender, RoutedEventArgs e) =>
        RefreshCharList();

    private void CharList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count) return;
        var c = _app.Characters[idx];

        int priorAc = c.ArmorClass;
        string priorArmorProfile = c.RogueSkillArmorProfile ?? "no_armor";
        CharacterArmorService.RecalculateArmorForCharacter(c, new EquipmentLibraryService().GetEquipmentLibrary());
        bool armorChanged = c.ArmorClass != priorAc
            || !string.Equals(c.RogueSkillArmorProfile, priorArmorProfile, StringComparison.OrdinalIgnoreCase);

        bool wealthInitialized = CharacterWealthService.EnsureStartingFunds(c);
        if (wealthInitialized)
        {
            c.Notes.Add($"Starting funds assigned by class: {CharacterWealthService.FormatCoins(c)}");
            CharLevelupInfo.Text = $"Assigned starting funds: {CharacterWealthService.FormatCoins(c)}.";
        }

        if (wealthInitialized || armorChanged)
        {
            c.LastModified = System.DateTime.Now;
            c.Revision += 1;
            _app.SaveCharacters();
        }

        CharDetailTitle.Text = $"{c.Name}  —  {c.RaceName} {c.ClassName}";
        CharNotes.Text       = string.Join("\n", c.Notes);
        CharRacialAbilities.ItemsSource = c.RacialAbilities.Count > 0
            ? c.RacialAbilities
            : c.Notes.Where(n => n.StartsWith("Racial Ability: ")).Select(n => n[16..]).ToList();

        CharXpGainInput.Text = "0";
        CharHpGainInput.Text = "0";
        CharCpGainInput.Text = "0";
        CharSpendNwpCpInput.Text = "0";
        CharSpendWeaponCpInput.Text = "0";
        if (!wealthInitialized)
            CharLevelupInfo.Text = "";
        RefreshCharacterProgressSummary(c);

        var items = new List<AbilityViewModel>();
        for (int i = 0; i < AbilityOrder.Length; i++)
        {
            int score = c.Abilities.GetValueOrDefault(AbilityOrder[i], 0);
            var color = score >= 15 ? "#E8C050" : score >= 9 ? "#C4A468" : "#C02828";
            items.Add(new AbilityViewModel
            {
                Score      = score.ToString(),
                Abbrev     = AbilityAbbrev[i],
                ScoreColor = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(color)),
            });
        }
        CharAbilityDisplay.ItemsSource = items;
    }

    private void BtnApplyLevelUpdate_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        int xpGain = ParseNonNegativeInt(CharXpGainInput.Text);
        int hpGain = ParseNonNegativeInt(CharHpGainInput.Text);
        int cpGain = ParseNonNegativeInt(CharCpGainInput.Text);

        int primeXpBonus = CharacterProgressionService.GetPrimeRequisiteBonusPercent(c);
        int abilityXpBonus = c.Bonuses?.XpModifierPercent ?? 0;
        int totalXpBonus = primeXpBonus + abilityXpBonus;
        if (xpGain > 0 && totalXpBonus != 0)
        {
            int adjusted = CharacterProgressionService.ApplyExperienceBonus(xpGain, totalXpBonus);
            int delta = adjusted - xpGain;
            string signPrime = primeXpBonus >= 0 ? $"+{primeXpBonus}" : $"{primeXpBonus}";
            string signAbility = abilityXpBonus >= 0 ? $"+{abilityXpBonus}" : $"{abilityXpBonus}";
            string signTotal = totalXpBonus >= 0 ? $"+{totalXpBonus}" : $"{totalXpBonus}";

            var decision = MessageBox.Show(
                $"XP entered: {xpGain}\n\n" +
                $"Prime Requisite bonus: {signPrime}%\n" +
                $"Racial/Class bonus: {signAbility}%\n" +
                $"Total bonus: {signTotal}%\n\n" +
                $"Apply bonus automatically?\n" +
                $"Yes = apply (effective XP gain {adjusted}, change {delta:+#;-#;0})\n" +
                $"No = keep entered XP as-is",
                "XP Bonus",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (decision == MessageBoxResult.Cancel)
                return;
            if (decision == MessageBoxResult.Yes)
                xpGain = adjusted;
        }

        int oldLevel = c.Level;
        int oldHp = c.HitPoints;
        int oldXp = c.ExperiencePoints;

        c.ExperiencePoints += xpGain;
        c.UnspentCharacterPoints += cpGain;

        var advancement = CharacterProgressionService.ApplyLevelAdvancementFromExperience(c, applyHitPointProgression: false);
        bool hpIgnored = false;
        if (advancement.LeveledUp)
        {
            if (hpGain > 0)
            {
                c.HitPoints = Math.Max(1, c.HitPoints + hpGain);
                CharacterProgressionService.RecordHitPointGainAcrossLevels(
                    c,
                    advancement.OldLevel,
                    advancement.NewLevel,
                    hpGain);
            }
        }
        else if (hpGain > 0)
        {
            hpIgnored = true;
        }

        c.Revision += 1;
        c.LastModified = System.DateTime.Now;
        _app.SaveCharacters();

        RefreshCharacterProgressSummary(c);
        CharLevelupInfo.Text = advancement.LeveledUp
            ? $"Level advanced: {oldLevel} -> {c.Level}. XP {oldXp:n0} -> {c.ExperiencePoints:n0}, HP {oldHp} -> {c.HitPoints}."
            : $"Updated. XP {oldXp:n0} -> {c.ExperiencePoints:n0}, HP {oldHp} -> {c.HitPoints}. No level change.";
        if (hpIgnored)
            CharLevelupInfo.Text += " HP input ignored because no level was gained.";

        CharXpGainInput.Text = "0";
        CharHpGainInput.Text = "0";
        CharCpGainInput.Text = "0";
    }

    private void BtnSpendCharacterCp_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        int nwpSpend = ParseNonNegativeInt(CharSpendNwpCpInput.Text);
        int weaponSpend = ParseNonNegativeInt(CharSpendWeaponCpInput.Text);
        int totalSpend = nwpSpend + weaponSpend;

        if (totalSpend == 0)
        {
            CharLevelupInfo.Text = "Enter CP to spend on NWPs and/or weapon proficiencies.";
            return;
        }

        if (totalSpend > c.UnspentCharacterPoints)
        {
            CharLevelupInfo.Text = $"Not enough unspent CP. Requested {totalSpend}, available {c.UnspentCharacterPoints}.";
            return;
        }

        c.UnspentCharacterPoints -= totalSpend;
        c.SpentNwpCharacterPoints += nwpSpend;
        c.SpentWeaponCharacterPoints += weaponSpend;
        c.Revision += 1;
        c.LastModified = System.DateTime.Now;
        _app.SaveCharacters();

        RefreshCharacterProgressSummary(c);
        CharLevelupInfo.Text = $"Spent {nwpSpend} CP on NWPs and {weaponSpend} CP on weapon proficiencies.";

        CharSpendNwpCpInput.Text = "0";
        CharSpendWeaponCpInput.Text = "0";
    }

    private void BtnBuyItem_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        var library = new EquipmentLibraryService().GetEquipmentLibrary()
            .OrderBy(x => x.Name, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (library.Count == 0)
        {
            MessageBox.Show("No equipment is available in the library.", "Buy Item",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryShowBuyItemDialog(library, out var item, out int quantity))
            return;

        int totalGp = item.CostGold * quantity;
        int totalSp = item.CostSilver * quantity;
        int totalCp = item.CostCopper * quantity;
        int totalCostCopper = CharacterWealthService.ToCopper(totalGp, totalSp, totalCp);

        if (totalCostCopper <= 0)
        {
            var freeDecision = MessageBox.Show(
                $"{item.Name} has no cost recorded. Add it to the character as a free item?",
                "No Cost Recorded",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (freeDecision != MessageBoxResult.Yes)
                return;
        }
        else if (!CharacterWealthService.TrySpend(c, totalGp, totalSp, totalCp, out string reason))
        {
            CharLevelupInfo.Text = reason;
            RefreshCharacterProgressSummary(c);
            return;
        }

        var existing = c.EquipmentSelections.FirstOrDefault(x =>
            string.Equals(
                EquipmentLibraryService.CanonicalizeId(x.ItemId),
                EquipmentLibraryService.CanonicalizeId(item.Id),
                System.StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            c.EquipmentSelections.Add(new EquipmentSelection
            {
                ItemId = item.Id,
                Category = item.Categories.FirstOrDefault() ?? "Equipment",
                ItemName = item.Name,
                CostText = BuildCostText(item),
                Quantity = quantity,
                IsArmor = item.IsArmor,
                ArmorClassValue = item.ArmorClassValue,
                RogueArmorProfile = item.RogueArmorProfile,
                IsWeapon = item.IsWeapon,
                WeaponSpeed = item.WeaponSpeed,
                WeaponDamageSmallMedium = item.WeaponDamageSmallMedium,
                WeaponDamageLarge = item.WeaponDamageLarge,
                WeaponType = item.WeaponType,
                WeaponSize = item.WeaponSize,
                CostGoldEach = item.CostGold,
                CostSilverEach = item.CostSilver,
                CostCopperEach = item.CostCopper,
                SizeClassEach = item.SizeClass,
                WeightEach = item.Weight,
            });
        }
        else
        {
            existing.Quantity = System.Math.Max(1, existing.Quantity + quantity);
            if (!existing.IsArmor && item.IsArmor)
                existing.IsArmor = true;
            if (existing.ArmorClassValue == 10 && item.ArmorClassValue < 10)
                existing.ArmorClassValue = item.ArmorClassValue;
            if (string.IsNullOrWhiteSpace(existing.RogueArmorProfile) || string.Equals(existing.RogueArmorProfile, "no_armor", StringComparison.OrdinalIgnoreCase))
                existing.RogueArmorProfile = item.RogueArmorProfile;
            if (!existing.IsWeapon && item.IsWeapon)
                existing.IsWeapon = true;
            if (existing.WeaponSpeed <= 0 && item.WeaponSpeed > 0)
                existing.WeaponSpeed = item.WeaponSpeed;
            if (string.IsNullOrWhiteSpace(existing.WeaponDamageSmallMedium))
                existing.WeaponDamageSmallMedium = item.WeaponDamageSmallMedium;
            if (string.IsNullOrWhiteSpace(existing.WeaponDamageLarge))
                existing.WeaponDamageLarge = item.WeaponDamageLarge;
            if (string.IsNullOrWhiteSpace(existing.WeaponType))
                existing.WeaponType = item.WeaponType;
            if (string.IsNullOrWhiteSpace(existing.WeaponSize))
                existing.WeaponSize = item.WeaponSize;
            if (existing.CostGoldEach <= 0)
                existing.CostGoldEach = item.CostGold;
            if (existing.CostSilverEach <= 0)
                existing.CostSilverEach = item.CostSilver;
            if (existing.CostCopperEach <= 0)
                existing.CostCopperEach = item.CostCopper;
            if (string.IsNullOrWhiteSpace(existing.SizeClassEach))
                existing.SizeClassEach = item.SizeClass;
            if (existing.WeightEach <= 0)
                existing.WeightEach = item.Weight;
        }

        c.Equipment = c.EquipmentSelections
            .OrderBy(x => x.Category)
            .ThenBy(x => x.ItemName)
            .Select(x => x.Quantity > 1 ? $"{x.ItemName} x{x.Quantity}" : x.ItemName)
            .ToList();

        CharacterArmorService.RecalculateArmorForCharacter(c, library);

        c.Notes.Add($"Purchased {item.Name} x{quantity} for {CharacterWealthService.FormatCoins(totalCostCopper)}.");
        c.LastModified = System.DateTime.Now;
        c.Revision += 1;
        _app.SaveCharacters();

        RefreshCharacterProgressSummary(c);
        CharNotes.Text = string.Join("\n", c.Notes);
        CharLevelupInfo.Text = totalCostCopper > 0
            ? $"Purchased {item.Name} x{quantity}. Deducted {CharacterWealthService.FormatCoins(totalCostCopper)}."
            : $"Added {item.Name} x{quantity} (no cost recorded).";
    }

    private void BtnSellItem_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        if (!TryShowSellDialog(c, out string removeItemId, out int removeQuantity, out int gp, out int sp, out int cp, out string note))
            return;

        bool removedItem = false;
        string removedItemName = string.Empty;
        if (!string.IsNullOrWhiteSpace(removeItemId) && removeQuantity > 0)
        {
            string targetId = EquipmentLibraryService.CanonicalizeId(removeItemId);
            var existing = c.EquipmentSelections.FirstOrDefault(x =>
                string.Equals(EquipmentLibraryService.CanonicalizeId(x.ItemId), targetId, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                CharLevelupInfo.Text = "Selected equipment could not be found on this character.";
                return;
            }

            removedItemName = existing.ItemName;
            existing.Quantity = Math.Max(0, existing.Quantity - removeQuantity);
            if (existing.Quantity == 0)
                c.EquipmentSelections.Remove(existing);

            c.Equipment = c.EquipmentSelections
                .OrderBy(x => x.Category)
                .ThenBy(x => x.ItemName)
                .Select(x => x.Quantity > 1 ? $"{x.ItemName} x{x.Quantity}" : x.ItemName)
                .ToList();

            CharacterArmorService.RecalculateArmorForCharacter(c, new EquipmentLibraryService().GetEquipmentLibrary());
            removedItem = true;
        }

        int addedCopper = CharacterWealthService.ToCopper(gp, sp, cp);
        if (addedCopper <= 0 && !removedItem)
        {
            CharLevelupInfo.Text = "No changes requested. Set an item quantity to remove and/or add funds.";
            return;
        }

        if (addedCopper > 0)
            CharacterWealthService.Add(c, gp, sp, cp);

        string reason = string.IsNullOrWhiteSpace(note) ? "sale/funds added" : note.Trim();
        if (removedItem && addedCopper > 0)
            c.Notes.Add($"Sold {removedItemName} x{removeQuantity} ({reason}); added {CharacterWealthService.FormatCoins(addedCopper)}.");
        else if (removedItem)
            c.Notes.Add($"Removed {removedItemName} x{removeQuantity} from equipment ({reason}).");
        else
            c.Notes.Add($"Funds added ({reason}): {CharacterWealthService.FormatCoins(addedCopper)}.");

        c.LastModified = System.DateTime.Now;
        c.Revision += 1;
        _app.SaveCharacters();

        RefreshCharacterProgressSummary(c);
        CharNotes.Text = string.Join("\n", c.Notes);
        if (removedItem && addedCopper > 0)
            CharLevelupInfo.Text = $"Removed {removedItemName} x{removeQuantity} and added {CharacterWealthService.FormatCoins(addedCopper)}.";
        else if (removedItem)
            CharLevelupInfo.Text = $"Removed {removedItemName} x{removeQuantity}.";
        else
            CharLevelupInfo.Text = $"Added {CharacterWealthService.FormatCoins(addedCopper)} to {c.Name}.";
    }

    private void RefreshCharacterProgressSummary(CharacterSheet c)
    {
        string hpAudit = c.HitPointGainByLevel.Count > 0
            ? string.Join(", ", c.HitPointGainByLevel
                .OrderBy(kv => kv.Key)
                .Select(kv => $"L{kv.Key}:+{kv.Value}"))
            : "No per-level HP history recorded yet.";

        CharCoinSummary.Text = CharacterWealthService.FormatCoins(c);

        CharProgressSummary.Text =
            $"Level: {c.Level}    XP: {c.ExperiencePoints:n0}    HP: {c.HitPoints}\n"
            + $"Armor Class: {c.ArmorClass}    Rogue Armor Profile: {c.RogueSkillArmorProfile}\n"
            + $"CP Unspent: {c.UnspentCharacterPoints}    CP Spent (NWP): {c.SpentNwpCharacterPoints}    CP Spent (Weapon): {c.SpentWeaponCharacterPoints}\n"
            + $"Unspent proficiency choices: {c.UnspentProficiencyChoices}"
            + $"\nHP by level: {hpAudit}"
            + (c.UnspentRogueSkillPoints > 0 ? $"    Unspent rogue skill points: {c.UnspentRogueSkillPoints}" : string.Empty);
    }

    private static int ParseNonNegativeInt(string text)
    {
        return int.TryParse((text ?? string.Empty).Trim(), out var value)
            ? System.Math.Max(0, value)
            : 0;
    }

    private static string BuildCostText(CustomEquipmentData item)
    {
        var parts = new List<string>();
        if (item.CostGold > 0) parts.Add($"{item.CostGold}gp");
        if (item.CostSilver > 0) parts.Add($"{item.CostSilver}sp");
        if (item.CostCopper > 0) parts.Add($"{item.CostCopper}cp");
        return string.Join(" ", parts);
    }

    private bool TryShowBuyItemDialog(IReadOnlyList<CustomEquipmentData> items, out CustomEquipmentData item, out int quantity)
    {
        item = items[0];
        quantity = 1;

        var dialog = new Window
        {
            Title = "Buy Item",
            Width = 520,
            Height = 250,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Content = BuildBuyDialogContent(items, out ComboBox itemCombo, out TextBox qtyBox),
        };

        if (dialog.ShowDialog() != true)
            return false;

        if (itemCombo.SelectedItem is not CustomEquipmentData selected)
            return false;

        int parsedQty = ParseNonNegativeInt(qtyBox.Text);
        if (parsedQty <= 0)
        {
            MessageBox.Show("Quantity must be at least 1.", "Buy Item", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        item = selected;
        quantity = parsedQty;
        return true;
    }

    private static UIElement BuildBuyDialogContent(IReadOnlyList<CustomEquipmentData> items, out ComboBox itemCombo, out TextBox qtyBox)
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock { Text = "Item", Margin = new Thickness(0, 0, 0, 4) });
        itemCombo = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = 0,
            DisplayMemberPath = nameof(CustomEquipmentData.Name),
            Margin = new Thickness(0, 0, 0, 10),
            MinHeight = 26,
        };
        Grid.SetRow(itemCombo, 1);
        root.Children.Add(itemCombo);

        var qtyPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        qtyPanel.Children.Add(new TextBlock { Text = "Quantity", Width = 90, VerticalAlignment = VerticalAlignment.Center });
        qtyBox = new TextBox { Text = "1", Width = 90, VerticalContentAlignment = VerticalAlignment.Center };
        qtyPanel.Children.Add(qtyBox);
        Grid.SetRow(qtyPanel, 2);
        root.Children.Add(qtyPanel);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Buy", Width = 88, IsDefault = true };
        ok.Click += (_, _) => Window.GetWindow(ok)!.DialogResult = true;
        buttonPanel.Children.Add(cancel);
        buttonPanel.Children.Add(ok);
        Grid.SetRow(buttonPanel, 4);
        root.Children.Add(buttonPanel);

        return root;
    }

    private bool TryShowSellDialog(CharacterSheet character, out string removeItemId, out int removeQuantity, out int gp, out int sp, out int cp, out string note)
    {
        removeItemId = string.Empty;
        removeQuantity = 0;
        gp = 0;
        sp = 0;
        cp = 0;
        note = string.Empty;

        var removableItems = (character.EquipmentSelections ?? new List<EquipmentSelection>())
            .Where(x => x.Quantity > 0)
            .OrderBy(x => x.ItemName)
            .ToList();

        var dialog = new Window
        {
            Title = "Sell / Unequip / Add Funds",
            Width = 520,
            Height = 360,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Content = BuildSellDialogContent(removableItems, out ComboBox itemCombo, out TextBox qtyBox, out TextBox gpBox, out TextBox spBox, out TextBox cpBox, out TextBox noteBox),
        };

        if (dialog.ShowDialog() != true)
            return false;

        if (itemCombo.SelectedItem is EquipmentSelection selected)
        {
            int parsedQuantity = ParseNonNegativeInt(qtyBox.Text);
            if (parsedQuantity > 0)
            {
                removeItemId = selected.ItemId;
                removeQuantity = parsedQuantity;
            }
        }

        gp = ParseNonNegativeInt(gpBox.Text);
        sp = ParseNonNegativeInt(spBox.Text);
        cp = ParseNonNegativeInt(cpBox.Text);
        note = noteBox.Text;
        return true;
    }

    private static UIElement BuildSellDialogContent(
        IReadOnlyList<EquipmentSelection> removableItems,
        out ComboBox itemCombo,
        out TextBox qtyBox,
        out TextBox gpBox,
        out TextBox spBox,
        out TextBox cpBox,
        out TextBox noteBox)
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock { Text = "Optional: remove equipped item quantity", Margin = new Thickness(0, 0, 0, 6) });

        itemCombo = new ComboBox
        {
            ItemsSource = removableItems,
            DisplayMemberPath = nameof(EquipmentSelection.ItemName),
            SelectedIndex = removableItems.Count > 0 ? 0 : -1,
            IsEnabled = removableItems.Count > 0,
            MinHeight = 26,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(itemCombo, 1);
        root.Children.Add(itemCombo);

        var qtyPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        qtyPanel.Children.Add(new TextBlock { Text = "Qty to remove", Width = 120, VerticalAlignment = VerticalAlignment.Center });
        qtyBox = new TextBox { Text = "0", Width = 90, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = removableItems.Count > 0 };
        qtyPanel.Children.Add(qtyBox);
        qtyPanel.Children.Add(new TextBlock
        {
            Text = removableItems.Count > 0 ? "(0 = keep all items)" : "(no equipped items)",
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
        });
        Grid.SetRow(qtyPanel, 2);
        root.Children.Add(qtyPanel);

        root.Children.Add(new TextBlock { Text = "Optional: add sale funds", Margin = new Thickness(0, 0, 0, 8) });
        Grid.SetRow(root.Children[^1], 3);

        var coinGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        coinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        coinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        coinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        gpBox = new TextBox { Text = "0", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        spBox = new TextBox { Text = "0", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        cpBox = new TextBox { Text = "0", VerticalContentAlignment = VerticalAlignment.Center };

        var gpPanel = new StackPanel();
        gpPanel.Children.Add(new TextBlock { Text = "GP" });
        gpPanel.Children.Add(gpBox);
        Grid.SetColumn(gpPanel, 0);
        coinGrid.Children.Add(gpPanel);

        var spPanel = new StackPanel();
        spPanel.Children.Add(new TextBlock { Text = "SP" });
        spPanel.Children.Add(spBox);
        Grid.SetColumn(spPanel, 1);
        coinGrid.Children.Add(spPanel);

        var cpPanel = new StackPanel();
        cpPanel.Children.Add(new TextBlock { Text = "CP" });
        cpPanel.Children.Add(cpBox);
        Grid.SetColumn(cpPanel, 2);
        coinGrid.Children.Add(cpPanel);

        Grid.SetRow(coinGrid, 4);
        root.Children.Add(coinGrid);

        root.Children.Add(new TextBlock { Text = "Note (optional)", Margin = new Thickness(0, 0, 0, 4) });
        Grid.SetRow(root.Children[^1], 5);

        noteBox = new TextBox { Margin = new Thickness(0, 0, 0, 8), MinHeight = 26 };
        Grid.SetRow(noteBox, 6);
        root.Children.Add(noteBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Apply", Width = 88, IsDefault = true };
        ok.Click += (_, _) => Window.GetWindow(ok)!.DialogResult = true;
        buttonPanel.Children.Add(cancel);
        buttonPanel.Children.Add(ok);
        Grid.SetRow(buttonPanel, 7);
        root.Children.Add(buttonPanel);

        return root;
    }

    private void BtnSaveNotes_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count) return;
        var c = _app.Characters[idx];
        c.Notes.Clear();
        foreach (var line in CharNotes.Text.Split('\n'))
        {
            var t = line.Trim();
            if (!string.IsNullOrEmpty(t)) c.Notes.Add(t);
        }
        c.LastModified = System.DateTime.Now;
        c.Revision += 1;
        _app.SaveCharacters();
        MessageBox.Show("Notes saved.", "Saved",
                        MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Races tab ───────────────────────────────────────────────────────────

    private void RefreshRaceList()
    {
        RaceListEditor.Items.Clear();
        foreach (var race in _app.Rules.Races.Values.OrderBy(r => r.Name))
            RaceListEditor.Items.Add($"{race.Name}  [{ModeLabel(race.CharacterMode)}]");

        if (!string.IsNullOrEmpty(_selectedRaceId) && _app.Rules.Races.ContainsKey(_selectedRaceId))
        {
            var ordered = _app.Rules.Races.Values.OrderBy(r => r.Name).ToList();
            RaceListEditor.SelectedIndex = ordered.FindIndex(r => r.Id == _selectedRaceId);
        }
        else
        {
            _selectedRaceId = "";
            ClearRaceEditor();
        }
    }

    private void RaceListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = RaceListEditor.SelectedIndex;
        if (idx < 0) return;

        var ordered = _app.Rules.Races.Values.OrderBy(r => r.Name).ToList();
        if (idx >= ordered.Count) return;
        LoadRaceEditor(ordered[idx]);
    }

    private void LoadRaceEditor(RaceDefinition race)
    {
        _selectedRaceId = race.Id;
        RaceId.Text = race.Id;
        RaceName.Text = race.Name;
        RaceBaseId.Text = race.BaseRaceId;
        SelectMode(race.CharacterMode);

        SetAbilityBox(RaceStrMin, race.AbilityMinimums, "str");
        SetAbilityBox(RaceDexMin, race.AbilityMinimums, "dex");
        SetAbilityBox(RaceConMin, race.AbilityMinimums, "con");
        SetAbilityBox(RaceIntMin, race.AbilityMinimums, "int");
        SetAbilityBox(RaceWisMin, race.AbilityMinimums, "wis");
        SetAbilityBox(RaceChaMin, race.AbilityMinimums, "cha");

        SetAbilityBox(RaceStrMax, race.AbilityMaximums, "str");
        SetAbilityBox(RaceDexMax, race.AbilityMaximums, "dex");
        SetAbilityBox(RaceConMax, race.AbilityMaximums, "con");
        SetAbilityBox(RaceIntMax, race.AbilityMaximums, "int");
        SetAbilityBox(RaceWisMax, race.AbilityMaximums, "wis");
        SetAbilityBox(RaceChaMax, race.AbilityMaximums, "cha");

        RacePointBudget.Text = race.RacialPointBudget.ToString();
        RaceAbilities.Text = string.Join("\n", race.RacialAbilities);
        RaceAbilityDefs.Text = string.Join("\n", race.StructuredAbilities.Select(FormatAbilityDefLine));
    }

    private void BtnNewRace_Click(object sender, RoutedEventArgs e)
    {
        _selectedRaceId = "";
        RaceListEditor.SelectedIndex = -1;
        ClearRaceEditor();
        SelectMode("players_option");
    }

    private void BtnSaveRace_Click(object sender, RoutedEventArgs e)
    {
        var raceId = RaceId.Text.Trim().ToLowerInvariant();
        var raceName = RaceName.Text.Trim();
        if (string.IsNullOrWhiteSpace(raceId) || string.IsNullOrWhiteSpace(raceName))
        {
            MessageBox.Show("Race Id and Display Name are required.", "Missing Information",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var baseRaceId = string.IsNullOrWhiteSpace(RaceBaseId.Text)
                ? raceId
                : RaceBaseId.Text.Trim().ToLowerInvariant();

            var existingEffects = _app.Rules.Races.TryGetValue(raceId, out var existingRace)
                ? existingRace.StructuredAbilities.ToDictionary(a => a.Id, a => a.Effect, System.StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, AbilityEffect>(System.StringComparer.OrdinalIgnoreCase);

            var race = new RaceDefinition(
                raceId,
                raceName,
                SelectedMode(),
                baseRaceId,
                ReadAbilityMap(isMinimum: true),
                ReadAbilityMap(isMinimum: false),
                new(),  // AbilityModifiers - empty for now, can be edited via UI later
                RaceAbilities.Text
                    .Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(a => a.Trim())
                    .Where(a => a.Length > 0)
                    .ToList(),
                ParseAbilityDefs(RaceAbilityDefs.Text, existingEffects),
                ParseIntOrDefault(RacePointBudget.Text, 0));

            var autoAssignedSpent = race.StructuredAbilities
                .Where(a => a.AutoGranted)
                .Sum(a => a.PointCost);
            if (race.RacialPointBudget > 0 && autoAssignedSpent > race.RacialPointBudget)
            {
                MessageBox.Show(
                    $"Cannot save race '{race.Name}'. Auto-assigned abilities spend {autoAssignedSpent} CP but budget is {race.RacialPointBudget} CP.\n\n" +
                    "Adjust point costs, mark some entries as optional, or increase the race budget.",
                    "Invalid Racial CP Budget",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _app.Rules.SaveRace(race);
            _selectedRaceId = race.Id;
            RefreshRaceList();
            MessageBox.Show("Race saved.", "Saved",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Save Race",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnDeleteRace_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedRaceId)) return;

        var result = MessageBox.Show("Delete this race from the ruleset?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            _app.Rules.DeleteRace(_selectedRaceId);
            _selectedRaceId = "";
            RefreshRaceList();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Delete Race",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Dictionary<string, int> ReadAbilityMap(bool isMinimum)
    {
        var map = new Dictionary<string, int>();
        ReadAbilityValue(map, "str", isMinimum ? RaceStrMin : RaceStrMax);
        ReadAbilityValue(map, "dex", isMinimum ? RaceDexMin : RaceDexMax);
        ReadAbilityValue(map, "con", isMinimum ? RaceConMin : RaceConMax);
        ReadAbilityValue(map, "int", isMinimum ? RaceIntMin : RaceIntMax);
        ReadAbilityValue(map, "wis", isMinimum ? RaceWisMin : RaceWisMax);
        ReadAbilityValue(map, "cha", isMinimum ? RaceChaMin : RaceChaMax);
        return map;
    }

    private static void ReadAbilityValue(Dictionary<string, int> map, string key, TextBox box)
    {
        var text = box.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (!int.TryParse(text, out var value))
            throw new System.InvalidOperationException($"{key.ToUpperInvariant()} must be a number.");
        map[key] = value;
    }

    private static void SetAbilityBox(TextBox box, Dictionary<string, int> map, string key)
    {
        box.Text = map.TryGetValue(key, out var value) ? value.ToString() : "";
    }

    private void ClearRaceEditor()
    {
        RaceId.Text = "";
        RaceName.Text = "";
        RaceBaseId.Text = "";
        SelectMode("all");
        foreach (var box in new[]
                 { RaceStrMin, RaceDexMin, RaceConMin, RaceIntMin, RaceWisMin, RaceChaMin,
                   RaceStrMax, RaceDexMax, RaceConMax, RaceIntMax, RaceWisMax, RaceChaMax })
            box.Text = "";
        RacePointBudget.Text = "0";
        RaceAbilities.Text = "";
        RaceAbilityDefs.Text = "";
    }

    private static int ParseIntOrDefault(string text, int fallback)
    {
        return int.TryParse(text.Trim(), out var v) ? v : fallback;
    }

    private static string FormatAbilityDefLine(AbilityDefinition def)
    {
        var safeDesc = def.Description.Replace("|", "/");
        var mechanicsJson = SerializeEffect(def.Effect);
        return $"{def.Id}|{def.PointCost}|{def.AutoGranted.ToString().ToLowerInvariant()}|{safeDesc}|{mechanicsJson}";
    }

    private static List<AbilityDefinition> ParseAbilityDefs(string raw, Dictionary<string, AbilityEffect> existingEffects)
    {
        var result = new List<AbilityDefinition>();
        foreach (var line in raw.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var parts = trimmed.Split('|');
            if (parts.Length < 4)
            {
                // Fallback: treat as plain description
                result.Add(new AbilityDefinition
                {
                    Id = trimmed.ToLowerInvariant().Replace(' ', '_'),
                    Description = trimmed,
                    PointCost = 0,
                    AutoGranted = true,
                    Effect = existingEffects.TryGetValue(trimmed, out var fx0) ? fx0 : new AbilityEffect(),
                });
                continue;
            }

            var id = parts[0].Trim();
            var cost = int.TryParse(parts[1].Trim(), out var c) ? c : 0;
            var auto = bool.TryParse(parts[2].Trim(), out var a) ? a : true;
            var hasMechanicsJson = parts.Length >= 5;
            var desc = hasMechanicsJson
                ? string.Join("|", parts.Skip(3).Take(parts.Length - 4)).Trim()
                : string.Join("|", parts.Skip(3)).Trim();

            var keyId = string.IsNullOrWhiteSpace(id) ? desc.ToLowerInvariant().Replace(' ', '_') : id;
            var effect = existingEffects.TryGetValue(keyId, out var existingFx) ? existingFx : new AbilityEffect();

            if (hasMechanicsJson)
            {
                var rawJson = parts[^1].Trim();
                var parsed = ParseEffect(rawJson);
                if (parsed is not null) effect = parsed;
            }

            result.Add(new AbilityDefinition
            {
                Id = keyId,
                Description = desc,
                PointCost = cost,
                AutoGranted = auto,
                Effect = effect,
            });
        }
        return result;
    }

    private static string SerializeEffect(AbilityEffect effect)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        var json = JsonSerializer.Serialize(effect, options);
        // Keep compact lines in the DM editor.
        return string.IsNullOrWhiteSpace(json) ? "{}" : json;
    }

    private static AbilityEffect? ParseEffect(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson) || rawJson == "{}") return new AbilityEffect();
        try
        {
            return JsonSerializer.Deserialize<AbilityEffect>(rawJson) ?? new AbilityEffect();
        }
        catch
        {
            return null;
        }
    }

    private string SelectedMode()
    {
        if (RaceMode.SelectedItem is ComboBoxItem item && item.Tag is string tag) return tag;
        return "all";
    }

    private void SelectMode(string mode)
    {
        foreach (var item in RaceMode.Items)
        {
            if (item is ComboBoxItem combo && (combo.Tag as string) == mode)
            {
                RaceMode.SelectedItem = combo;
                return;
            }
        }
        RaceMode.SelectedIndex = 0;
    }

    private static string ModeLabel(string mode) => mode switch
    {
        "core_rules" => "Core Rules",
        "players_option" => "Player's Option",
        _ => "All Characters",
    };

    // ── Traits tab ───────────────────────────────────────────────────────────

    private void RefreshTraitEditorList()
    {
        string search = TraitSearch.Text.Trim();
        _traitItems = _app.CharacterOptions.GetCatalog().Traits
            .Where(t => string.IsNullOrWhiteSpace(search)
                || t.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                || t.Description.Contains(search, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name)
            .ToList();

        var customIds = _app.CharacterOptions.GetCustomTraits();
        TraitListEditor.ItemsSource = _traitItems
            .Select(t => (customIds.ContainsKey(t.Id) ? "★ " : string.Empty) + $"{t.Name} ({t.Cost} CP)")
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedTraitId))
        {
            int idx = _traitItems.FindIndex(x => string.Equals(x.Id, _selectedTraitId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) TraitListEditor.SelectedIndex = idx;
        }
        if (TraitListEditor.SelectedIndex < 0 && _traitItems.Count > 0)
            TraitListEditor.SelectedIndex = 0;
    }

    private void TraitSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TabTraits.Visibility == Visibility.Visible)
            RefreshTraitEditorList();
    }

    private void TraitListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = TraitListEditor.SelectedIndex;
        if (idx < 0 || idx >= _traitItems.Count) return;
        var trait = _traitItems[idx];
        _selectedTraitId = trait.Id;
        TraitName.Text = trait.Name;
        TraitCost.Text = trait.Cost.ToString();
        TraitDescription.Text = trait.Description;
        bool isCustom = _app.CharacterOptions.GetCustomTraits().ContainsKey(trait.Id);
        BtnDeleteTrait.IsEnabled = isCustom;
        TraitEditorInfo.Text = isCustom ? "★ Custom / overridden trait" : string.Empty;
    }

    private void BtnNewTrait_Click(object sender, RoutedEventArgs e)
    {
        _selectedTraitId = string.Empty;
        TraitListEditor.SelectedIndex = -1;
        TraitName.Text = string.Empty;
        TraitCost.Text = "0";
        TraitDescription.Text = string.Empty;
        BtnDeleteTrait.IsEnabled = false;
        TraitEditorInfo.Text = "Fill in the fields and click SAVE TRAIT.";
    }

    private void BtnSaveTrait_Click(object sender, RoutedEventArgs e)
    {
        string name = TraitName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Trait name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(TraitCost.Text.Trim(), out int cost)) cost = 0;

        string id = string.IsNullOrWhiteSpace(_selectedTraitId)
            ? $"trait_custom_{SlugifySimple(name)}"
            : _selectedTraitId;

        _app.CharacterOptions.SaveCustomTrait(new CustomTraitData
        {
            Id = id,
            Name = name,
            Cost = cost,
            Description = TraitDescription.Text.Trim(),
        });
        _app.CharacterOptions.InvalidateCache();
        _selectedTraitId = id;
        TraitEditorInfo.Text = $"★ Saved: {name}";
        RefreshTraitEditorList();
    }

    private void BtnDeleteTrait_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedTraitId)) return;
        if (MessageBox.Show("Delete this custom trait?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _app.CharacterOptions.DeleteCustomTrait(_selectedTraitId);
        _app.CharacterOptions.InvalidateCache();
        _selectedTraitId = string.Empty;
        RefreshTraitEditorList();
    }

    // ── Disadvantages tab ────────────────────────────────────────────────────

    private void RefreshDisadvantageEditorList()
    {
        string search = DisadvantageSearch.Text.Trim();
        _disadvantageItems = _app.CharacterOptions.GetCatalog().Disadvantages
            .Where(d => string.IsNullOrWhiteSpace(search)
                || d.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                || d.Description.Contains(search, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Name)
            .ToList();

        var customIds = _app.CharacterOptions.GetCustomDisadvantages();
        DisadvantageListEditor.ItemsSource = _disadvantageItems
            .Select(d => (customIds.ContainsKey(d.Id) ? "★ " : string.Empty) +
                $"{d.Name} (+{d.ModerateBonus}{(d.SevereBonus.HasValue ? $" / +{d.SevereBonus.Value}" : string.Empty)} CP)")
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedDisadvantageId))
        {
            int idx = _disadvantageItems.FindIndex(x => string.Equals(x.Id, _selectedDisadvantageId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) DisadvantageListEditor.SelectedIndex = idx;
        }
        if (DisadvantageListEditor.SelectedIndex < 0 && _disadvantageItems.Count > 0)
            DisadvantageListEditor.SelectedIndex = 0;
    }

    private void DisadvantageSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TabDisadvantages.Visibility == Visibility.Visible)
            RefreshDisadvantageEditorList();
    }

    private void DisadvantageListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = DisadvantageListEditor.SelectedIndex;
        if (idx < 0 || idx >= _disadvantageItems.Count) return;
        var dis = _disadvantageItems[idx];
        _selectedDisadvantageId = dis.Id;
        DisadvantageName.Text = dis.Name;
        DisadvantageModerate.Text = dis.ModerateBonus.ToString();
        DisadvantageSevere.Text = dis.SevereBonus?.ToString() ?? string.Empty;
        DisadvantageDescription.Text = dis.Description;
        bool isCustom = _app.CharacterOptions.GetCustomDisadvantages().ContainsKey(dis.Id);
        BtnDeleteDisadvantage.IsEnabled = isCustom;
        DisadvantageEditorInfo.Text = isCustom ? "★ Custom / overridden disadvantage" : string.Empty;
    }

    private void BtnNewDisadvantage_Click(object sender, RoutedEventArgs e)
    {
        _selectedDisadvantageId = string.Empty;
        DisadvantageListEditor.SelectedIndex = -1;
        DisadvantageName.Text = string.Empty;
        DisadvantageModerate.Text = "1";
        DisadvantageSevere.Text = string.Empty;
        DisadvantageDescription.Text = string.Empty;
        BtnDeleteDisadvantage.IsEnabled = false;
        DisadvantageEditorInfo.Text = "Fill in the fields and click SAVE DISADVANTAGE.";
    }

    private void BtnSaveDisadvantage_Click(object sender, RoutedEventArgs e)
    {
        string name = DisadvantageName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Disadvantage name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(DisadvantageModerate.Text.Trim(), out int moderate)) moderate = 1;
        int? severe = null;
        if (int.TryParse(DisadvantageSevere.Text.Trim(), out int severeValue)) severe = severeValue;

        string id = string.IsNullOrWhiteSpace(_selectedDisadvantageId)
            ? $"disadv_custom_{SlugifySimple(name)}"
            : _selectedDisadvantageId;

        _app.CharacterOptions.SaveCustomDisadvantage(new CustomDisadvantageData
        {
            Id = id,
            Name = name,
            ModerateBonus = moderate,
            SevereBonus = severe,
            Description = DisadvantageDescription.Text.Trim(),
        });
        _app.CharacterOptions.InvalidateCache();
        _selectedDisadvantageId = id;
        DisadvantageEditorInfo.Text = $"★ Saved: {name}";
        RefreshDisadvantageEditorList();
    }

    private void BtnDeleteDisadvantage_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedDisadvantageId)) return;
        if (MessageBox.Show("Delete this custom disadvantage?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _app.CharacterOptions.DeleteCustomDisadvantage(_selectedDisadvantageId);
        _app.CharacterOptions.InvalidateCache();
        _selectedDisadvantageId = string.Empty;
        RefreshDisadvantageEditorList();
    }

    // ── Kits tab ─────────────────────────────────────────────────────────────

    private void RefreshKitEditorList()
    {
        string search = KitEditorSearch.Text.Trim();
        string sourceFilter = (KitSourceFilter.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "";
        bool allSources = sourceFilter == "All Sources" || string.IsNullOrEmpty(sourceFilter);

        _kitItems = _app.Rules.Kits
            .Where(k =>
                (string.IsNullOrWhiteSpace(search)
                    || k.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || k.Description.Contains(search, System.StringComparison.OrdinalIgnoreCase))
                && (allSources || string.Equals(k.Source, sourceFilter, System.StringComparison.OrdinalIgnoreCase)))
            .OrderBy(k => k.Source)
            .ThenBy(k => k.Name)
            .ToList();

        KitListEditor.ItemsSource = _kitItems
            .Select(k => $"{k.Name}  [{k.Source}]")
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedKitId))
        {
            int idx = _kitItems.FindIndex(x => string.Equals(x.Id, _selectedKitId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) KitListEditor.SelectedIndex = idx;
        }
        if (KitListEditor.SelectedIndex < 0 && _kitItems.Count > 0)
            KitListEditor.SelectedIndex = 0;
    }

    private void KitEditorSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TabKits.Visibility == Visibility.Visible)
            RefreshKitEditorList();
    }

    private void KitSourceFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TabKits.Visibility == Visibility.Visible)
            RefreshKitEditorList();
    }

    private void KitListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = KitListEditor.SelectedIndex;
        if (idx < 0 || idx >= _kitItems.Count) return;
        var kit = _kitItems[idx];
        _selectedKitId = kit.Id;
        KitEditName.Text = kit.Name;
        KitEditSource.Text = kit.Source;
        KitEditAllowedRaces.Text = string.Join(", ", kit.AllowedRaces);
        KitEditAllowedClasses.Text = string.Join(", ", kit.AllowedClasses);
        KitEditDescription.Text = kit.Description;
        bool isCustom = kit.Source.Equals("Custom", System.StringComparison.OrdinalIgnoreCase)
                     || !_app.Rules.Kits.Any(k => k.Id == kit.Id && k.Source != "Custom");
        BtnDeleteKit.IsEnabled = true; // all kits can be deleted/replaced via the DM toolkit
        KitEditorInfo.Text = string.Empty;
    }

    private void BtnNewKit_Click(object sender, RoutedEventArgs e)
    {
        _selectedKitId = string.Empty;
        KitListEditor.SelectedIndex = -1;
        KitEditName.Text = string.Empty;
        KitEditSource.Text = "Custom";
        KitEditAllowedRaces.Text = string.Empty;
        KitEditAllowedClasses.Text = string.Empty;
        KitEditDescription.Text = string.Empty;
        BtnDeleteKit.IsEnabled = false;
        KitEditorInfo.Text = "Fill in the fields and click SAVE KIT.";
    }

    private void BtnSaveKit_Click(object sender, RoutedEventArgs e)
    {
        string name = KitEditName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Kit name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string id = string.IsNullOrWhiteSpace(_selectedKitId)
            ? $"kit_custom_{SlugifySimple(name)}"
            : _selectedKitId;

        var allowedRaces = KitEditAllowedRaces.Text
            .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .ToList();
        var allowedClasses = KitEditAllowedClasses.Text
            .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .ToList();

        // Preserve existing NWP grants when saving from the DM editor
        var existingKit = _app.Rules.Kits.FirstOrDefault(k => k.Id == id);
        var updated = new KitDefinition(
            id,
            name,
            KitEditDescription.Text.Trim(),
            KitEditSource.Text.Trim(),
            allowedRaces,
            allowedClasses,
            existingKit?.FreeNwpIds ?? new List<string>(),
            existingKit?.RequiredNwpIds ?? new List<string>());

        var allKits = _app.Rules.Kits.Where(k => k.Id != id).Append(updated);
        _app.Rules.SaveKitDefinitions(allKits);
        _selectedKitId = id;
        KitEditorInfo.Text = $"★ Saved: {name}";
        RefreshKitEditorList();
    }

    private void BtnDeleteKit_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedKitId)) return;
        string displayName = _kitItems.FirstOrDefault(k => k.Id == _selectedKitId)?.Name ?? _selectedKitId;
        if (MessageBox.Show($"Delete kit \"{displayName}\"?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        var remaining = _app.Rules.Kits.Where(k => k.Id != _selectedKitId);
        _app.Rules.SaveKitDefinitions(remaining);
        _selectedKitId = string.Empty;
        KitEditName.Text = string.Empty;
        KitEditSource.Text = string.Empty;
        KitEditAllowedRaces.Text = string.Empty;
        KitEditAllowedClasses.Text = string.Empty;
        KitEditDescription.Text = string.Empty;
        BtnDeleteKit.IsEnabled = false;
        KitEditorInfo.Text = string.Empty;
        RefreshKitEditorList();
    }

    // ── Classes tab ──────────────────────────────────────────────────────────

    // ── Multi-Class Combos tab ────────────────────────────────────────────────

    private string _selectedMultiClassRaceGroup = "";
    // Working copy of combos for the currently-edited group (mutable list)
    private List<List<string>> _editingCombos = new();

    private void RefreshMultiClassEditor()
    {
        MultiClassRaceList.SelectionChanged -= MultiClassRaceList_SelectionChanged;
        MultiClassRaceList.ItemsSource = _app.Rules.MultiClassCombos
            .Select(g => $"{g.RaceGroup}  ({g.Combos.Count} combos)")
            .ToList();
        MultiClassRaceList.SelectionChanged += MultiClassRaceList_SelectionChanged;

        if (!string.IsNullOrEmpty(_selectedMultiClassRaceGroup))
        {
            int idx = _app.Rules.MultiClassCombos
                .FindIndex(g => string.Equals(g.RaceGroup, _selectedMultiClassRaceGroup, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                MultiClassRaceList.SelectedIndex = idx;
                return;
            }
        }

        if (_app.Rules.MultiClassCombos.Count > 0)
            MultiClassRaceList.SelectedIndex = 0;
        else
            ClearMultiClassEditor();
    }

    private void MultiClassRaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = MultiClassRaceList.SelectedIndex;
        if (idx < 0 || idx >= _app.Rules.MultiClassCombos.Count) { ClearMultiClassEditor(); return; }
        var g = _app.Rules.MultiClassCombos[idx];
        _selectedMultiClassRaceGroup = g.RaceGroup;
        MultiClassEditRaceGroup.Text = g.RaceGroup;
        MultiClassEditSource.Text    = g.Source;
        _editingCombos = g.Combos.Select(c => new List<string>(c)).ToList();
        RefreshMultiClassComboList();
        BtnDeleteMultiClassGroup.IsEnabled = true;
        MultiClassEditorInfo.Text = "";
    }

    private void ClearMultiClassEditor()
    {
        _selectedMultiClassRaceGroup = "";
        _editingCombos = new();
        MultiClassEditRaceGroup.Text = "";
        MultiClassEditSource.Text    = "";
        MultiClassComboList.ItemsSource = null;
        BtnDeleteMultiClassGroup.IsEnabled = false;
        BtnRemoveMultiClassCombo.IsEnabled = false;
        MultiClassEditorInfo.Text = "";
    }

    private void RefreshMultiClassComboList()
    {
        MultiClassComboList.SelectionChanged -= MultiClassComboList_SelectionChanged;
        MultiClassComboList.ItemsSource = _editingCombos
            .Select(c => string.Join(", ", c))
            .ToList();
        MultiClassComboList.SelectionChanged += MultiClassComboList_SelectionChanged;
        BtnRemoveMultiClassCombo.IsEnabled = false;
    }

    private void MultiClassComboList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnRemoveMultiClassCombo.IsEnabled = MultiClassComboList.SelectedIndex >= 0;
    }

    private void BtnNewMultiClassGroup_Click(object sender, RoutedEventArgs e)
    {
        _selectedMultiClassRaceGroup = "";
        _editingCombos = new();
        MultiClassEditRaceGroup.Text = "";
        MultiClassEditSource.Text    = "PHB 2e";
        MultiClassComboList.ItemsSource = null;
        BtnDeleteMultiClassGroup.IsEnabled = false;
        BtnRemoveMultiClassCombo.IsEnabled = false;
        MultiClassEditorInfo.Text = "Enter a race group key and add combos, then click SAVE GROUP.";
        MultiClassRaceList.SelectedIndex = -1;
    }

    private void BtnAddMultiClassCombo_Click(object sender, RoutedEventArgs e)
    {
        string raw = MultiClassNewComboEntry.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;

        var classes = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Select(s => s.ToLowerInvariant())
                         .Distinct()
                         .ToList();
        if (classes.Count < 2) { MultiClassEditorInfo.Text = "A combo needs at least 2 class IDs."; return; }

        // Prevent duplicate combos (order-insensitive)
        bool dup = _editingCombos.Any(c =>
            c.Count == classes.Count &&
            new HashSet<string>(c, StringComparer.OrdinalIgnoreCase).SetEquals(classes));
        if (dup) { MultiClassEditorInfo.Text = "That combo already exists."; return; }

        _editingCombos.Add(classes);
        MultiClassNewComboEntry.Text = "";
        RefreshMultiClassComboList();
        MultiClassEditorInfo.Text = $"Added: {string.Join("/", classes)}. Remember to SAVE GROUP.";
    }

    private void BtnRemoveMultiClassCombo_Click(object sender, RoutedEventArgs e)
    {
        int idx = MultiClassComboList.SelectedIndex;
        if (idx < 0 || idx >= _editingCombos.Count) return;
        string label = string.Join("/", _editingCombos[idx]);
        _editingCombos.RemoveAt(idx);
        RefreshMultiClassComboList();
        MultiClassEditorInfo.Text = $"Removed: {label}. Remember to SAVE GROUP.";
    }

    private void BtnSaveMultiClassGroup_Click(object sender, RoutedEventArgs e)
    {
        string raceGroup = MultiClassEditRaceGroup.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(raceGroup)) { MultiClassEditorInfo.Text = "Race group key is required."; return; }

        string source = MultiClassEditSource.Text.Trim();
        var updated = new MultiClassComboGroup(raceGroup, source, _editingCombos.Select(c => new List<string>(c)).ToList());

        var all = _app.Rules.MultiClassCombos
            .Where(g => !string.Equals(g.RaceGroup, raceGroup, StringComparison.OrdinalIgnoreCase))
            .Append(updated)
            .OrderBy(g => g.RaceGroup)
            .ToList();

        _app.Rules.SaveMultiClassCombos(all);
        _selectedMultiClassRaceGroup = raceGroup;
        MultiClassEditorInfo.Text = $"★ Saved: {raceGroup}  ({_editingCombos.Count} combos)";
        RefreshMultiClassEditor();
    }

    private void BtnDeleteMultiClassGroup_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedMultiClassRaceGroup)) return;
        if (MessageBox.Show($"Delete all multi-class combos for \"{_selectedMultiClassRaceGroup}\"?",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var remaining = _app.Rules.MultiClassCombos
            .Where(g => !string.Equals(g.RaceGroup, _selectedMultiClassRaceGroup, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _app.Rules.SaveMultiClassCombos(remaining);
        ClearMultiClassEditor();
        RefreshMultiClassEditor();
    }


    private List<ClassDefinition> GetOrderedClasses()
        => _app.Rules.Classes.Values.OrderBy(c => c.Name).ToList();

    private void RefreshClassEditorList()
    {
        ClassDefListEditor.ItemsSource = GetOrderedClasses()
            .Select(c => $"{c.Name} [{c.Id}]")
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedClassId))
        {
            var ordered = GetOrderedClasses();
            int idx = ordered.FindIndex(c => string.Equals(c.Id, _selectedClassId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                ClassDefListEditor.SelectedIndex = idx;
        }

        if (ClassDefListEditor.SelectedIndex < 0 && _app.Rules.Classes.Count > 0)
            ClassDefListEditor.SelectedIndex = 0;
    }

    private void ClassDefListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = ClassDefListEditor.SelectedIndex;
        var ordered = GetOrderedClasses();
        if (idx < 0 || idx >= ordered.Count) return;
        LoadClassDefinitionEditor(ordered[idx]);
    }

    private void LoadClassDefinitionEditor(ClassDefinition cls)
    {
        _selectedClassId = cls.Id;
        ClassDefEditorTitle.Text = $"Class Editor - {cls.Name}";
        ClassDefIdEditor.Text = cls.Id;
        ClassDefNameEditor.Text = cls.Name;
        ClassDefBudgetEditor.Text = cls.ClassPointBudget.ToString();
        ClassDefAllowedRacesEditor.Text = string.Join(", ", cls.AllowedRaces);
        ClassDefMinsEditor.Text = JsonSerializer.Serialize(cls.AbilityMinimums);
        BtnDeleteClassDefinition.IsEnabled = true;
    }

    private void BtnNewClassDefinition_Click(object sender, RoutedEventArgs e)
    {
        _selectedClassId = string.Empty;
        ClassDefListEditor.SelectedIndex = -1;
        ClassDefEditorTitle.Text = "Class Editor - New Class";
        ClassDefIdEditor.Text = string.Empty;
        ClassDefNameEditor.Text = string.Empty;
        ClassDefBudgetEditor.Text = "0";
        ClassDefAllowedRacesEditor.Text = string.Empty;
        ClassDefMinsEditor.Text = "{}";
        BtnDeleteClassDefinition.IsEnabled = false;
        ClassDefEditorInfo.Text = "Fill in the fields and click SAVE CLASS.";
    }

    private void BtnSaveClassDefinition_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string id = ClassDefIdEditor.Text.Trim().ToLowerInvariant();
            string name = ClassDefNameEditor.Text.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Class Id and Name are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existingClass = _app.Rules.Classes.TryGetValue(id, out var existing)
                ? existing
                : null;
            var mins = string.IsNullOrWhiteSpace(ClassDefMinsEditor.Text)
                ? new Dictionary<string, int>()
                : JsonSerializer.Deserialize<Dictionary<string, int>>(ClassDefMinsEditor.Text.Trim())
                    ?? new Dictionary<string, int>();
            var allowed = ClassDefAllowedRacesEditor.Text
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
            int budget = ParseIntOrDefault(ClassDefBudgetEditor.Text, 0);

            _app.Rules.SaveClass(new ClassDefinition(
                id,
                name,
                mins,
                allowed,
                existingClass?.StructuredAbilities ?? new List<AbilityDefinition>(),
                budget,
                existingClass?.Specializations));
            _selectedClassId = id;
            ClassDefEditorInfo.Text = $"Saved class: {name}";
            RefreshClassEditorList();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Save Class", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnDeleteClassDefinition_Click(object sender, RoutedEventArgs e)
    {
        string id = ClassDefIdEditor.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(id)) return;
        if (MessageBox.Show("Delete this class from ruleset files?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _app.Rules.DeleteClass(id);
        _selectedClassId = string.Empty;
        BtnNewClassDefinition_Click(sender, e);
        RefreshClassEditorList();
    }

    // ── Class Abilities tab ──────────────────────────────────────────────────

    private void RefreshClassAbilityEditorList()
    {
        ClassListEditor.ItemsSource = GetOrderedClasses()
            .Select(c => $"{c.Name} [{c.Id}]")
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedClassId))
        {
            var ordered = GetOrderedClasses();
            int idx = ordered.FindIndex(c => string.Equals(c.Id, _selectedClassId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) ClassListEditor.SelectedIndex = idx;
        }
        if (ClassListEditor.SelectedIndex < 0 && _app.Rules.Classes.Count > 0)
            ClassListEditor.SelectedIndex = 0;

        RefreshClassCopyTargets();
    }

    private void ClassListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = ClassListEditor.SelectedIndex;
        var ordered = GetOrderedClasses();
        if (idx < 0 || idx >= ordered.Count) return;
        LoadClassAbilityEditor(ordered[idx]);
    }

    private void LoadClassAbilityEditor(ClassDefinition cls)
    {
        _selectedClassId = cls.Id;
        ClassEditorTitle.Text = $"Class Ability Editor - {cls.Name}";
        ClassIdEditor.Text = cls.Id;
        ClassNameEditor.Text = cls.Name;
        _workingClassAbilities = cls.StructuredAbilities
            .Select(a => new AbilityDefinition { Id = a.Id, Description = a.Description, Category = a.Category, PointCost = a.PointCost, AutoGranted = a.AutoGranted, Effect = a.Effect })
            .ToList();
        _selectedClassAbilityIndex = -1;
        RefreshClassAbilityItemsList();
        ClearClassAbilityDetailForm();
        RefreshClassCopyTargets();
    }

    private void BtnNewClassAbilityClass_Click(object sender, RoutedEventArgs e)
    {
        _selectedClassId = string.Empty;
        ClassListEditor.SelectedIndex = -1;
        ClassEditorTitle.Text = "Class Ability Editor - New Class";
        ClassIdEditor.Text = string.Empty;
        ClassNameEditor.Text = string.Empty;
        _workingClassAbilities = new List<AbilityDefinition>();
        _selectedClassAbilityIndex = -1;
        RefreshClassAbilityItemsList();
        ClearClassAbilityDetailForm();
        RefreshClassCopyTargets();
        ClassEditorInfo.Text = "Enter a class id, name, and abilities, then click SAVE CLASS ABILITIES.";
    }

    private void BtnSaveClass_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string id = ClassIdEditor.Text.Trim().ToLowerInvariant();
            string name = ClassNameEditor.Text.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Class Id and Name are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existingClass = _app.Rules.Classes.TryGetValue(id, out var existing)
                ? existing
                : null;
            _app.Rules.SaveClass(new ClassDefinition(
                id,
                name,
                existingClass?.AbilityMinimums ?? new Dictionary<string, int>(),
                existingClass?.AllowedRaces ?? new List<string>(),
                _workingClassAbilities,
                existingClass?.ClassPointBudget ?? 0,
                existingClass?.Specializations));

            int updatedCharacters = ReapplyAbilityMechanicsToMatchingCharacters(classId: id, raceId: null);
            _selectedClassId = id;
            ClassEditorInfo.Text = $"Saved class abilities: {name}. Reapplied mechanics to {updatedCharacters} character(s).";
            RefreshClassAbilityEditorList();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Save Class", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnDeleteClass_Click(object sender, RoutedEventArgs e)
    {
        BtnDeleteClassDefinition_Click(sender, e);
        RefreshClassAbilityEditorList();
    }

    private void RefreshClassCopyTargets()
    {
        _classCopyTargets = GetOrderedClasses()
            .Where(c => !string.Equals(c.Id, _selectedClassId, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        ClassCopyTarget.ItemsSource = _classCopyTargets
            .Select(c => new ClassCopyTargetOption(c.Id, c.Name))
            .ToList();

        if (ClassCopyTarget.Items.Count > 0)
            ClassCopyTarget.SelectedIndex = 0;
    }

    private void BtnCopyClassAbilityToClass_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClassAbilityIndex < 0 || _selectedClassAbilityIndex >= _workingClassAbilities.Count)
        {
            ClassEditorInfo.Text = "Select an ability to copy first.";
            return;
        }

        if (ClassCopyTarget.SelectedItem is not ClassCopyTargetOption targetOption)
        {
            ClassEditorInfo.Text = "Select a target class first.";
            return;
        }

        if (!_app.Rules.Classes.TryGetValue(targetOption.Id, out var targetClass))
        {
            ClassEditorInfo.Text = "Could not find the target class in rules.";
            return;
        }

        if (_selectedClassAbilityIndex == CaAbilityList.SelectedIndex)
        {
            _workingClassAbilities[_selectedClassAbilityIndex] = ReadClassAbilityDetailForm();
            RefreshClassAbilityItemsList();
        }

        var sourceAbility = _workingClassAbilities[_selectedClassAbilityIndex];
        var clonedAbility = CloneAbilityDefinition(sourceAbility);
        var targetAbilities = targetClass.StructuredAbilities
            .Select(CloneAbilityDefinition)
            .ToList();

        int existingIdx = targetAbilities.FindIndex(a =>
            string.Equals(a.Id, clonedAbility.Id, System.StringComparison.OrdinalIgnoreCase));

        if (existingIdx >= 0)
            targetAbilities[existingIdx] = clonedAbility;
        else
            targetAbilities.Add(clonedAbility);

        _app.Rules.SaveClass(new ClassDefinition(
            targetClass.Id,
            targetClass.Name,
            targetClass.AbilityMinimums,
            targetClass.AllowedRaces,
            targetAbilities,
            targetClass.ClassPointBudget,
            targetClass.Specializations));

        int updatedCharacters = ReapplyAbilityMechanicsToMatchingCharacters(classId: targetClass.Id, raceId: null);

        ClassEditorInfo.Text = existingIdx >= 0
            ? $"Updated '{clonedAbility.Description}' on {targetClass.Name}. Reapplied mechanics to {updatedCharacters} character(s)."
            : $"Copied '{clonedAbility.Description}' to {targetClass.Name}. Reapplied mechanics to {updatedCharacters} character(s).";
    }

    // ── Racial Abilities tab ─────────────────────────────────────────────────

    private void RefreshRacialAbilityEditorList()
    {
        RaceAbilityListEditor.ItemsSource = _app.Rules.Races.Values
            .OrderBy(r => r.Name)
            .Select(r => $"{r.Name} [{r.Id}]")
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedRaceAbilityId))
        {
            var ordered = _app.Rules.Races.Values.OrderBy(r => r.Name).ToList();
            int idx = ordered.FindIndex(r => string.Equals(r.Id, _selectedRaceAbilityId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) RaceAbilityListEditor.SelectedIndex = idx;
        }
        if (RaceAbilityListEditor.SelectedIndex < 0 && _app.Rules.Races.Count > 0)
            RaceAbilityListEditor.SelectedIndex = 0;

        RefreshRaceCopyTargets();
    }

    private void RaceAbilityListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = RaceAbilityListEditor.SelectedIndex;
        var ordered = _app.Rules.Races.Values.OrderBy(r => r.Name).ToList();
        if (idx < 0 || idx >= ordered.Count) return;
        var race = ordered[idx];
        _selectedRaceAbilityId = race.Id;
        RacialAbilityEditorTitle.Text = $"Racial Ability Editor - {race.Name}";
        RaceAbilityRaceId.Text = race.Id;
        RaceAbilityRaceName.Text = race.Name;
        _workingRacialAbilities = race.StructuredAbilities
            .Select(a => new AbilityDefinition { Id = a.Id, Description = a.Description, Category = a.Category, PointCost = a.PointCost, AutoGranted = a.AutoGranted, Effect = a.Effect })
            .ToList();
        _selectedRacialAbilityIndex = -1;
        RefreshRacialAbilityItemsList();
        ClearRacialAbilityDetailForm();
        RefreshRaceCopyTargets();
    }

    private void BtnNewRacialAbilityRace_Click(object sender, RoutedEventArgs e)
    {
        _selectedRaceAbilityId = string.Empty;
        RaceAbilityListEditor.SelectedIndex = -1;
        RacialAbilityEditorTitle.Text = "Racial Ability Editor - New Race";
        RaceAbilityRaceId.Text = string.Empty;
        RaceAbilityRaceName.Text = string.Empty;
        _workingRacialAbilities = new List<AbilityDefinition>();
        _selectedRacialAbilityIndex = -1;
        RefreshRacialAbilityItemsList();
        ClearRacialAbilityDetailForm();
        RefreshRaceCopyTargets();
        RacialAbilityEditorInfo.Text = "Enter a race id, name, and abilities, then click SAVE RACIAL ABILITIES.";
    }

    private void BtnSaveRacialAbilities_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string raceId = RaceAbilityRaceId.Text.Trim().ToLowerInvariant();
            string raceName = RaceAbilityRaceName.Text.Trim();
            if (string.IsNullOrWhiteSpace(raceId) || string.IsNullOrWhiteSpace(raceName))
            {
                MessageBox.Show("Race Id and Race Name are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _app.Rules.Races.TryGetValue(raceId, out var race);
            var defs = _workingRacialAbilities;
            var updated = race is not null
                ? race with
                {
                    Name = raceName,
                    StructuredAbilities = defs,
                    RacialAbilities = defs.Select(d => d.Description).ToList(),
                }
                : new RaceDefinition(
                    raceId,
                    raceName,
                    "all",
                    raceId,
                    new Dictionary<string, int>(),
                    new Dictionary<string, int>(),
                    new Dictionary<string, int>(),
                    defs.Select(d => d.Description).ToList(),
                    defs,
                    0);

            _app.Rules.SaveRace(updated);
            int updatedCharacters = ReapplyAbilityMechanicsToMatchingCharacters(classId: null, raceId: updated.Id);
            _selectedRaceAbilityId = updated.Id;
            RacialAbilityEditorInfo.Text = $"Saved racial abilities for {updated.Name}. Reapplied mechanics to {updatedCharacters} character(s).";
            RefreshRacialAbilityEditorList();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Save Racial Abilities", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshRaceCopyTargets()
    {
        _raceCopyTargets = _app.Rules.Races.Values
            .OrderBy(r => r.Name)
            .Where(r => !string.Equals(r.Id, _selectedRaceAbilityId, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        var options = _raceCopyTargets
            .Select(r => new RaceCopyTargetOption(r.Id, r.Name))
            .ToList();

        RaceCopyTarget.ItemsSource = options;
        RaceCopyTargetList.ItemsSource = options
            .Select(x => new RaceCopyTargetOption(x.Id, x.Name))
            .ToList();

        if (RaceCopyTarget.Items.Count > 0)
            RaceCopyTarget.SelectedIndex = 0;

        if (string.IsNullOrWhiteSpace(RaceCopyFamilyKey.Text)
            && _app.Rules.Races.TryGetValue(_selectedRaceAbilityId, out var sourceRace))
        {
            var seed = string.IsNullOrWhiteSpace(sourceRace.BaseRaceId)
                ? sourceRace.Name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? sourceRace.Id
                : sourceRace.BaseRaceId;
            RaceCopyFamilyKey.Text = seed;
        }
    }

    private void BtnCopyRacialAbilityToRace_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetRacialAbilityForCopy(out var sourceAbility)) return;

        if (RaceCopyTarget.SelectedItem is not RaceCopyTargetOption targetOption)
        {
            RacialAbilityEditorInfo.Text = "Select a target race first.";
            return;
        }

        if (!_app.Rules.Races.TryGetValue(targetOption.Id, out var targetRace))
        {
            RacialAbilityEditorInfo.Text = "Could not find the target race in rules.";
            return;
        }

        SaveAbilityToRace(targetRace, sourceAbility, out bool replaced);

        RacialAbilityEditorInfo.Text = replaced
            ? $"Updated '{sourceAbility.Description}' on {targetRace.Name}."
            : $"Copied '{sourceAbility.Description}' to {targetRace.Name}.";
    }

    private void BtnCopyRacialAbilityToSelectedRaces_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetRacialAbilityForCopy(out var sourceAbility)) return;

        var selectedTargets = RaceCopyTargetList.SelectedItems
            .OfType<RaceCopyTargetOption>()
            .Select(x => x.Id)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selectedTargets.Count == 0)
        {
            RacialAbilityEditorInfo.Text = "Select one or more target races first.";
            return;
        }

        int updated = 0;
        int added = 0;
        foreach (var targetId in selectedTargets)
        {
            if (!_app.Rules.Races.TryGetValue(targetId, out var targetRace))
                continue;

            SaveAbilityToRace(targetRace, sourceAbility, out bool replaced);
            if (replaced) updated += 1;
            else added += 1;
        }

        RacialAbilityEditorInfo.Text = $"Copied '{sourceAbility.Description}' to {updated + added} race(s): {added} added, {updated} updated.";
        RefreshRaceCopyTargets();
    }

    private void BtnCopyRacialAbilityToFamily_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetRacialAbilityForCopy(out var sourceAbility)) return;

        string token = RaceCopyFamilyKey.Text.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            RacialAbilityEditorInfo.Text = "Enter a family key (for example: kobold).";
            return;
        }

        var matches = _app.Rules.Races.Values
            .Where(r => !string.Equals(r.Id, _selectedRaceAbilityId, System.StringComparison.OrdinalIgnoreCase)
                && (r.Id.Contains(token, System.StringComparison.OrdinalIgnoreCase)
                    || r.Name.Contains(token, System.StringComparison.OrdinalIgnoreCase)
                    || r.BaseRaceId.Contains(token, System.StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => r.Name)
            .ToList();

        if (matches.Count == 0)
        {
            RacialAbilityEditorInfo.Text = $"No races matched '{token}'.";
            return;
        }

        int updated = 0;
        int added = 0;
        foreach (var targetRace in matches)
        {
            SaveAbilityToRace(targetRace, sourceAbility, out bool replaced);
            if (replaced) updated += 1;
            else added += 1;
        }

        RacialAbilityEditorInfo.Text = $"Copied '{sourceAbility.Description}' to {matches.Count} matching race(s): {added} added, {updated} updated.";
        RefreshRaceCopyTargets();
    }

    private bool TryGetRacialAbilityForCopy(out AbilityDefinition sourceAbility)
    {
        sourceAbility = new AbilityDefinition();
        if (_selectedRacialAbilityIndex < 0 || _selectedRacialAbilityIndex >= _workingRacialAbilities.Count)
        {
            RacialAbilityEditorInfo.Text = "Select an ability to copy first.";
            return false;
        }

        if (_selectedRacialAbilityIndex == RaAbilityList.SelectedIndex)
        {
            _workingRacialAbilities[_selectedRacialAbilityIndex] = ReadRacialAbilityDetailForm();
            RefreshRacialAbilityItemsList();
        }

        sourceAbility = CloneAbilityDefinition(_workingRacialAbilities[_selectedRacialAbilityIndex]);
        return true;
    }

    private void SaveAbilityToRace(RaceDefinition targetRace, AbilityDefinition sourceAbility, out bool replaced)
    {
        var targetAbilities = targetRace.StructuredAbilities
            .Select(CloneAbilityDefinition)
            .ToList();

        int existingIdx = targetAbilities.FindIndex(a =>
            string.Equals(a.Id, sourceAbility.Id, System.StringComparison.OrdinalIgnoreCase));

        replaced = existingIdx >= 0;
        if (replaced)
            targetAbilities[existingIdx] = CloneAbilityDefinition(sourceAbility);
        else
            targetAbilities.Add(CloneAbilityDefinition(sourceAbility));

        var updatedTargetRace = targetRace with
        {
            StructuredAbilities = targetAbilities,
            RacialAbilities = targetAbilities.Select(a => a.Description).ToList(),
        };

        _app.Rules.SaveRace(updatedTargetRace);
        ReapplyAbilityMechanicsToMatchingCharacters(classId: null, raceId: updatedTargetRace.Id);
    }

    private int ReapplyAbilityMechanicsToMatchingCharacters(string? classId, string? raceId)
    {
        bool hasClass = !string.IsNullOrWhiteSpace(classId);
        bool hasRace = !string.IsNullOrWhiteSpace(raceId);
        if (!hasClass && !hasRace)
            return 0;

        int updated = 0;
        var library = new EquipmentLibraryService().GetEquipmentLibrary();

        foreach (var c in _app.Characters)
        {
            bool classMatch = hasClass && CharacterUsesClass(c, classId!);
            bool raceMatch = hasRace && string.Equals(c.RaceId, raceId, System.StringComparison.OrdinalIgnoreCase);
            if (!classMatch && !raceMatch)
                continue;

            RebuildCharacterAbilityStateFromRules(c, library);
            updated += 1;
        }

        if (updated > 0)
            _app.SaveCharacters();

        return updated;
    }

    private void RebuildCharacterAbilityStateFromRules(CharacterSheet character, IReadOnlyList<CustomEquipmentData> equipmentLibrary)
    {
        var rebuilt = _app.Rules.BuildCharacter(
            character.Name,
            character.RaceId,
            character.ClassId,
            character.Abilities,
            character.SelectedRacialAbilityIds,
            character.SelectedClassAbilityIds,
            character.ClassAbilityCarryoverPoints,
            character.WizardSpecializationId,
            character.SubAbilities,
            character.ExceptionalStrength,
            character.RogueSkillArmorProfile);

        character.StructuredAbilities = rebuilt.StructuredAbilities
            .Select(CloneAbilityDefinition)
            .ToList();
        character.RacialAbilities = rebuilt.RacialAbilities.ToList();
        character.Bonuses = rebuilt.Bonuses;
        character.BaseMovement = rebuilt.BaseMovement;
        character.Movement = rebuilt.Movement;
        character.RacialPointBudget = rebuilt.RacialPointBudget;
        character.RacialPointSpent = rebuilt.RacialPointSpent;
        character.RacialPointRemaining = rebuilt.RacialPointRemaining;
        character.ClassPointBudget = rebuilt.ClassPointBudget;
        character.ClassPointSpent = rebuilt.ClassPointSpent;
        character.ClassPointRemaining = rebuilt.ClassPointRemaining;
        character.DerivedStats = new Dictionary<string, int>(rebuilt.DerivedStats, System.StringComparer.OrdinalIgnoreCase);
        character.SubAbilityEffects = new Dictionary<string, string>(rebuilt.SubAbilityEffects, System.StringComparer.OrdinalIgnoreCase);

        CharacterProgressionService.InitializeCharacterProgression(character, seedLevelRewards: false);
        CharacterArmorService.RecalculateArmorForCharacter(character, equipmentLibrary);
        character.LastModified = System.DateTime.Now;
        character.Revision += 1;
    }

    private static bool CharacterUsesClass(CharacterSheet character, string classId)
    {
        if (string.Equals(character.ClassId, classId, System.StringComparison.OrdinalIgnoreCase))
            return true;

        if (character.ClassIds.Any(c => string.Equals(c, classId, System.StringComparison.OrdinalIgnoreCase)))
            return true;

        var parts = character.ClassId
            .Split(new[] { '/', '+', ',', ';', '|' }, System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        return parts.Any(part => string.Equals(part, classId, System.StringComparison.OrdinalIgnoreCase));
    }

    // ── Class Ability item list handlers ────────────────────────────────────

    private void RefreshClassAbilityItemsList()
    {
        CaAbilityList.ItemsSource = null;
        CaAbilityList.ItemsSource = _workingClassAbilities
            .Select(a => $"{a.Description}  [{(a.AutoGranted ? "auto" : "optional")}, {a.PointCost} pts]")
            .ToList();
        if (_selectedClassAbilityIndex >= 0 && _selectedClassAbilityIndex < _workingClassAbilities.Count)
            CaAbilityList.SelectedIndex = _selectedClassAbilityIndex;
    }

    private void CaAbilityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = CaAbilityList.SelectedIndex;
        if (idx < 0 || idx >= _workingClassAbilities.Count) return;
        _selectedClassAbilityIndex = idx;
        LoadClassAbilityDetailForm(_workingClassAbilities[idx]);
    }

    private void BtnAddClassAbility_Click(object sender, RoutedEventArgs e)
    {
        _workingClassAbilities.Add(new AbilityDefinition { Description = "New Ability", AutoGranted = true, Id = "new_ability" });
        _selectedClassAbilityIndex = _workingClassAbilities.Count - 1;
        RefreshClassAbilityItemsList();
        LoadClassAbilityDetailForm(_workingClassAbilities[_selectedClassAbilityIndex]);
    }

    private void BtnRemoveClassAbility_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClassAbilityIndex < 0 || _selectedClassAbilityIndex >= _workingClassAbilities.Count) return;
        _workingClassAbilities.RemoveAt(_selectedClassAbilityIndex);
        _selectedClassAbilityIndex = Math.Min(_selectedClassAbilityIndex, _workingClassAbilities.Count - 1);
        RefreshClassAbilityItemsList();
        if (_selectedClassAbilityIndex >= 0)
            LoadClassAbilityDetailForm(_workingClassAbilities[_selectedClassAbilityIndex]);
        else
            ClearClassAbilityDetailForm();
    }

    private void BtnApplyClassAbility_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClassAbilityIndex < 0 || _selectedClassAbilityIndex >= _workingClassAbilities.Count)
        {
            CaApplyInfo.Text = "Select an ability from the list first.";
            return;
        }
        _workingClassAbilities[_selectedClassAbilityIndex] = ReadClassAbilityDetailForm();
        RefreshClassAbilityItemsList();
        CaApplyInfo.Text = "Applied. Remember to click SAVE CLASS ABILITIES to persist.";
    }

    private void LoadClassAbilityDetailForm(AbilityDefinition a)
    {
        CaDesc.Text = a.Description;
        CaCost.Text = a.PointCost != 0 ? a.PointCost.ToString() : "";
        CaAuto.IsChecked = a.AutoGranted;
        var fx = a.Effect;
        CaAcBonus.Text      = fx.AcBonus != 0 ? fx.AcBonus.ToString() : "";
        CaNoArmorForAc.IsChecked = fx.AcBonusRequiresNoArmor;
        CaAttackBonus.Text  = fx.AttackBonus != 0 ? fx.AttackBonus.ToString() : "";
        CaDamageBonus.Text  = fx.DamageBonus != 0 ? fx.DamageBonus.ToString() : "";
        CaMovementBonus.Text = fx.MovementBonus != 0 ? fx.MovementBonus.ToString() : "";
        CaHpDice.Text       = fx.HpDiceExpression ?? "";
        CaHpPerLevel.Text   = fx.HpPerLevel != 0 ? fx.HpPerLevel.ToString() : "";
        CaHpFlat.Text       = fx.HpFlatBonus != 0 ? fx.HpFlatBonus.ToString() : "";
        CaXpMod.Text        = fx.XpModifierPercent != 0 ? fx.XpModifierPercent.ToString() : "";
        CaNwpSlots.Text     = fx.NwpSlotBonus != 0 ? fx.NwpSlotBonus.ToString() : "";
        CaNwpCostRed.Text   = fx.NwpCostReduction != 0 ? fx.NwpCostReduction.ToString() : "";
        CaNwpCheck.Text     = fx.NwpCheckBonus != 0 ? fx.NwpCheckBonus.ToString() : "";
        CaSurprise.Text     = fx.SurpriseBonus != 0 ? fx.SurpriseBonus.ToString() : "";
        CaReaction.Text     = fx.ReactionBonus != 0 ? fx.ReactionBonus.ToString() : "";
        CaInfravision.Text  = fx.InfravisionFeet != 0 ? fx.InfravisionFeet.ToString() : "";
        CaMagicResist.Text  = fx.MagicResistPercent != 0 ? fx.MagicResistPercent.ToString() : "";
        CaStealth.IsChecked     = fx.GrantsStealth;
        CaDetectDoors.IsChecked = fx.DetectSecretDoors;
        CaDetectStone.IsChecked = fx.DetectStonework;
        CaSaveBonuses.Text   = FormatKeyValuePairs(fx.SaveBonuses);
        CaSubAbility.Text    = FormatKeyValuePairs(fx.SubAbilityBonuses);
        CaEnemyAttack.Text   = FormatKeyValuePairs(fx.EnemyAttackBonuses);
        CaEnemyDamage.Text   = FormatKeyValuePairs(fx.EnemyDamageBonuses);
        CaWeaponAttack.Text  = FormatKeyValuePairs(fx.WeaponAttackBonuses);
        CaWeaponDamage.Text  = FormatKeyValuePairs(fx.WeaponDamageBonuses);
        CaApplyInfo.Text = "";
    }

    private void ClearClassAbilityDetailForm()
    {
        CaDesc.Text = ""; CaCost.Text = ""; CaAuto.IsChecked = true;
        CaAcBonus.Text = ""; CaAttackBonus.Text = ""; CaDamageBonus.Text = "";
        CaNoArmorForAc.IsChecked = false;
        CaMovementBonus.Text = "";
        CaHpDice.Text = "";
        CaHpPerLevel.Text = ""; CaHpFlat.Text = ""; CaXpMod.Text = "";
        CaNwpSlots.Text = ""; CaNwpCostRed.Text = ""; CaNwpCheck.Text = "";
        CaSurprise.Text = ""; CaReaction.Text = ""; CaInfravision.Text = ""; CaMagicResist.Text = "";
        CaStealth.IsChecked = false; CaDetectDoors.IsChecked = false; CaDetectStone.IsChecked = false;
        CaSaveBonuses.Text = ""; CaSubAbility.Text = "";
        CaEnemyAttack.Text = ""; CaEnemyDamage.Text = "";
        CaWeaponAttack.Text = ""; CaWeaponDamage.Text = "";
        CaApplyInfo.Text = "";
    }

    private AbilityDefinition ReadClassAbilityDetailForm()
    {
        var desc = CaDesc.Text.Trim();
        return new AbilityDefinition
        {
            Id          = SlugifySimple(desc),
            Description = desc,
            PointCost   = ParseIntOrDefault(CaCost.Text, 0),
            AutoGranted = CaAuto.IsChecked == true,
            Effect = new AbilityEffect
            {
                AcBonus            = ParseIntOrDefault(CaAcBonus.Text, 0),
                AcBonusRequiresNoArmor = CaNoArmorForAc.IsChecked == true,
                AttackBonus        = ParseIntOrDefault(CaAttackBonus.Text, 0),
                DamageBonus        = ParseIntOrDefault(CaDamageBonus.Text, 0),
                MovementBonus      = ParseIntOrDefault(CaMovementBonus.Text, 0),
                HpDiceExpression   = string.IsNullOrWhiteSpace(CaHpDice.Text) ? null : CaHpDice.Text.Trim(),
                HpPerLevel         = ParseIntOrDefault(CaHpPerLevel.Text, 0),
                HpFlatBonus        = ParseIntOrDefault(CaHpFlat.Text, 0),
                XpModifierPercent  = ParseIntOrDefault(CaXpMod.Text, 0),
                NwpSlotBonus       = ParseIntOrDefault(CaNwpSlots.Text, 0),
                NwpCostReduction   = ParseIntOrDefault(CaNwpCostRed.Text, 0),
                NwpCheckBonus      = ParseIntOrDefault(CaNwpCheck.Text, 0),
                SurpriseBonus      = ParseIntOrDefault(CaSurprise.Text, 0),
                ReactionBonus      = ParseIntOrDefault(CaReaction.Text, 0),
                InfravisionFeet    = ParseIntOrDefault(CaInfravision.Text, 0),
                MagicResistPercent = ParseIntOrDefault(CaMagicResist.Text, 0),
                GrantsStealth      = CaStealth.IsChecked == true,
                DetectSecretDoors  = CaDetectDoors.IsChecked == true,
                DetectStonework    = CaDetectStone.IsChecked == true,
                SaveBonuses        = ParseKeyValuePairs(CaSaveBonuses.Text),
                SubAbilityBonuses  = ParseKeyValuePairs(CaSubAbility.Text),
                EnemyAttackBonuses = ParseKeyValuePairs(CaEnemyAttack.Text),
                EnemyDamageBonuses = ParseKeyValuePairs(CaEnemyDamage.Text),
                WeaponAttackBonuses = ParseKeyValuePairs(CaWeaponAttack.Text),
                WeaponDamageBonuses = ParseKeyValuePairs(CaWeaponDamage.Text),
            },
        };
    }

    private static AbilityDefinition CloneAbilityDefinition(AbilityDefinition source)
    {
        return new AbilityDefinition
        {
            Id = source.Id,
            Description = source.Description,
            Category = source.Category,
            PointCost = source.PointCost,
            AutoGranted = source.AutoGranted,
            Effect = CloneAbilityEffect(source.Effect),
        };
    }

    private static AbilityEffect CloneAbilityEffect(AbilityEffect source)
    {
        return new AbilityEffect
        {
            AcBonus = source.AcBonus,
            AcBonusRequiresNoArmor = source.AcBonusRequiresNoArmor,
            AttackBonus = source.AttackBonus,
            DamageBonus = source.DamageBonus,
            MovementBonus = source.MovementBonus,
            SaveBonuses = new Dictionary<string, int>(source.SaveBonuses, System.StringComparer.OrdinalIgnoreCase),
            XpModifierPercent = source.XpModifierPercent,
            HpDiceExpression = source.HpDiceExpression,
            HpPerLevel = source.HpPerLevel,
            HpFlatBonus = source.HpFlatBonus,
            NwpSlotBonus = source.NwpSlotBonus,
            NwpCostReduction = source.NwpCostReduction,
            NwpCheckBonus = source.NwpCheckBonus,
            SurpriseBonus = source.SurpriseBonus,
            SubAbilityBonuses = new Dictionary<string, int>(source.SubAbilityBonuses, System.StringComparer.OrdinalIgnoreCase),
            InfravisionFeet = source.InfravisionFeet,
            MagicResistPercent = source.MagicResistPercent,
            EnemyAttackBonuses = new Dictionary<string, int>(source.EnemyAttackBonuses, System.StringComparer.OrdinalIgnoreCase),
            EnemyDamageBonuses = new Dictionary<string, int>(source.EnemyDamageBonuses, System.StringComparer.OrdinalIgnoreCase),
            WeaponAttackBonuses = new Dictionary<string, int>(source.WeaponAttackBonuses, System.StringComparer.OrdinalIgnoreCase),
            WeaponDamageBonuses = new Dictionary<string, int>(source.WeaponDamageBonuses, System.StringComparer.OrdinalIgnoreCase),
            ReactionBonus = source.ReactionBonus,
            GrantsStealth = source.GrantsStealth,
            DetectSecretDoors = source.DetectSecretDoors,
            DetectStonework = source.DetectStonework,
        };
    }

    // ── Racial Ability item list handlers ────────────────────────────────────

    private void RefreshRacialAbilityItemsList()
    {
        RaAbilityList.ItemsSource = null;
        RaAbilityList.ItemsSource = _workingRacialAbilities
            .Select(a => $"{a.Description}  [{(a.AutoGranted ? "auto" : "optional")}, {a.PointCost} pts]")
            .ToList();
        if (_selectedRacialAbilityIndex >= 0 && _selectedRacialAbilityIndex < _workingRacialAbilities.Count)
            RaAbilityList.SelectedIndex = _selectedRacialAbilityIndex;
    }

    private void RaAbilityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = RaAbilityList.SelectedIndex;
        if (idx < 0 || idx >= _workingRacialAbilities.Count) return;
        _selectedRacialAbilityIndex = idx;
        LoadRacialAbilityDetailForm(_workingRacialAbilities[idx]);
    }

    private void BtnAddRacialAbility_Click(object sender, RoutedEventArgs e)
    {
        _workingRacialAbilities.Add(new AbilityDefinition { Description = "New Ability", AutoGranted = true, Id = "new_ability" });
        _selectedRacialAbilityIndex = _workingRacialAbilities.Count - 1;
        RefreshRacialAbilityItemsList();
        LoadRacialAbilityDetailForm(_workingRacialAbilities[_selectedRacialAbilityIndex]);
    }

    private void BtnRemoveRacialAbility_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRacialAbilityIndex < 0 || _selectedRacialAbilityIndex >= _workingRacialAbilities.Count) return;
        _workingRacialAbilities.RemoveAt(_selectedRacialAbilityIndex);
        _selectedRacialAbilityIndex = Math.Min(_selectedRacialAbilityIndex, _workingRacialAbilities.Count - 1);
        RefreshRacialAbilityItemsList();
        if (_selectedRacialAbilityIndex >= 0)
            LoadRacialAbilityDetailForm(_workingRacialAbilities[_selectedRacialAbilityIndex]);
        else
            ClearRacialAbilityDetailForm();
    }

    private void BtnApplyRacialAbility_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRacialAbilityIndex < 0 || _selectedRacialAbilityIndex >= _workingRacialAbilities.Count)
        {
            RaApplyInfo.Text = "Select an ability from the list first.";
            return;
        }
        _workingRacialAbilities[_selectedRacialAbilityIndex] = ReadRacialAbilityDetailForm();
        RefreshRacialAbilityItemsList();
        RaApplyInfo.Text = "Applied. Remember to click SAVE RACIAL ABILITIES to persist.";
    }

    private void LoadRacialAbilityDetailForm(AbilityDefinition a)
    {
        RaDesc.Text = a.Description;
        RaCost.Text = a.PointCost != 0 ? a.PointCost.ToString() : "";
        RaAuto.IsChecked = a.AutoGranted;
        var fx = a.Effect;
        RaAcBonus.Text      = fx.AcBonus != 0 ? fx.AcBonus.ToString() : "";
        RaNoArmorForAc.IsChecked = fx.AcBonusRequiresNoArmor;
        RaAttackBonus.Text  = fx.AttackBonus != 0 ? fx.AttackBonus.ToString() : "";
        RaDamageBonus.Text  = fx.DamageBonus != 0 ? fx.DamageBonus.ToString() : "";
        RaMovementBonus.Text = fx.MovementBonus != 0 ? fx.MovementBonus.ToString() : "";
        RaHpDice.Text       = fx.HpDiceExpression ?? "";
        RaHpPerLevel.Text   = fx.HpPerLevel != 0 ? fx.HpPerLevel.ToString() : "";
        RaHpFlat.Text       = fx.HpFlatBonus != 0 ? fx.HpFlatBonus.ToString() : "";
        RaXpMod.Text        = fx.XpModifierPercent != 0 ? fx.XpModifierPercent.ToString() : "";
        RaNwpSlots.Text     = fx.NwpSlotBonus != 0 ? fx.NwpSlotBonus.ToString() : "";
        RaNwpCostRed.Text   = fx.NwpCostReduction != 0 ? fx.NwpCostReduction.ToString() : "";
        RaNwpCheck.Text     = fx.NwpCheckBonus != 0 ? fx.NwpCheckBonus.ToString() : "";
        RaSurprise.Text     = fx.SurpriseBonus != 0 ? fx.SurpriseBonus.ToString() : "";
        RaReaction.Text     = fx.ReactionBonus != 0 ? fx.ReactionBonus.ToString() : "";
        RaInfravision.Text  = fx.InfravisionFeet != 0 ? fx.InfravisionFeet.ToString() : "";
        RaMagicResist.Text  = fx.MagicResistPercent != 0 ? fx.MagicResistPercent.ToString() : "";
        RaStealth.IsChecked     = fx.GrantsStealth;
        RaDetectDoors.IsChecked = fx.DetectSecretDoors;
        RaDetectStone.IsChecked = fx.DetectStonework;
        RaSaveBonuses.Text   = FormatKeyValuePairs(fx.SaveBonuses);
        RaSubAbility.Text    = FormatKeyValuePairs(fx.SubAbilityBonuses);
        RaEnemyAttack.Text   = FormatKeyValuePairs(fx.EnemyAttackBonuses);
        RaEnemyDamage.Text   = FormatKeyValuePairs(fx.EnemyDamageBonuses);
        RaWeaponAttack.Text  = FormatKeyValuePairs(fx.WeaponAttackBonuses);
        RaWeaponDamage.Text  = FormatKeyValuePairs(fx.WeaponDamageBonuses);
        RaApplyInfo.Text = "";
    }

    private void ClearRacialAbilityDetailForm()
    {
        RaDesc.Text = ""; RaCost.Text = ""; RaAuto.IsChecked = true;
        RaAcBonus.Text = ""; RaAttackBonus.Text = ""; RaDamageBonus.Text = "";
        RaNoArmorForAc.IsChecked = false;
        RaMovementBonus.Text = "";
        RaHpDice.Text = "";
        RaHpPerLevel.Text = ""; RaHpFlat.Text = ""; RaXpMod.Text = "";
        RaNwpSlots.Text = ""; RaNwpCostRed.Text = ""; RaNwpCheck.Text = "";
        RaSurprise.Text = ""; RaReaction.Text = ""; RaInfravision.Text = ""; RaMagicResist.Text = "";
        RaStealth.IsChecked = false; RaDetectDoors.IsChecked = false; RaDetectStone.IsChecked = false;
        RaSaveBonuses.Text = ""; RaSubAbility.Text = "";
        RaEnemyAttack.Text = ""; RaEnemyDamage.Text = "";
        RaWeaponAttack.Text = ""; RaWeaponDamage.Text = "";
        RaApplyInfo.Text = "";
    }

    private AbilityDefinition ReadRacialAbilityDetailForm()
    {
        var desc = RaDesc.Text.Trim();
        return new AbilityDefinition
        {
            Id          = SlugifySimple(desc),
            Description = desc,
            PointCost   = ParseIntOrDefault(RaCost.Text, 0),
            AutoGranted = RaAuto.IsChecked == true,
            Effect = new AbilityEffect
            {
                AcBonus            = ParseIntOrDefault(RaAcBonus.Text, 0),
                AcBonusRequiresNoArmor = RaNoArmorForAc.IsChecked == true,
                AttackBonus        = ParseIntOrDefault(RaAttackBonus.Text, 0),
                DamageBonus        = ParseIntOrDefault(RaDamageBonus.Text, 0),
                MovementBonus      = ParseIntOrDefault(RaMovementBonus.Text, 0),
                HpDiceExpression   = string.IsNullOrWhiteSpace(RaHpDice.Text) ? null : RaHpDice.Text.Trim(),
                HpPerLevel         = ParseIntOrDefault(RaHpPerLevel.Text, 0),
                HpFlatBonus        = ParseIntOrDefault(RaHpFlat.Text, 0),
                XpModifierPercent  = ParseIntOrDefault(RaXpMod.Text, 0),
                NwpSlotBonus       = ParseIntOrDefault(RaNwpSlots.Text, 0),
                NwpCostReduction   = ParseIntOrDefault(RaNwpCostRed.Text, 0),
                NwpCheckBonus      = ParseIntOrDefault(RaNwpCheck.Text, 0),
                SurpriseBonus      = ParseIntOrDefault(RaSurprise.Text, 0),
                ReactionBonus      = ParseIntOrDefault(RaReaction.Text, 0),
                InfravisionFeet    = ParseIntOrDefault(RaInfravision.Text, 0),
                MagicResistPercent = ParseIntOrDefault(RaMagicResist.Text, 0),
                GrantsStealth      = RaStealth.IsChecked == true,
                DetectSecretDoors  = RaDetectDoors.IsChecked == true,
                DetectStonework    = RaDetectStone.IsChecked == true,
                SaveBonuses        = ParseKeyValuePairs(RaSaveBonuses.Text),
                SubAbilityBonuses  = ParseKeyValuePairs(RaSubAbility.Text),
                EnemyAttackBonuses = ParseKeyValuePairs(RaEnemyAttack.Text),
                EnemyDamageBonuses = ParseKeyValuePairs(RaEnemyDamage.Text),
                WeaponAttackBonuses = ParseKeyValuePairs(RaWeaponAttack.Text),
                WeaponDamageBonuses = ParseKeyValuePairs(RaWeaponDamage.Text),
            },
        };
    }

    // ── Key:value dictionary helpers ─────────────────────────────────────────

    private static Dictionary<string, int> ParseKeyValuePairs(string text)
    {
        var result = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var part in text.Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length == 2 && int.TryParse(kv[1].Trim(), out var val))
                result[kv[0].Trim().ToLowerInvariant()] = val;
        }
        return result;
    }

    private static string FormatKeyValuePairs(Dictionary<string, int> dict)
    {
        if (dict == null || dict.Count == 0) return "";
        return string.Join(", ", dict.Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    private static string SlugifySimple(string value)
    {
        string slug = System.Text.RegularExpressions.Regex
            .Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "_")
            .Trim('_');
        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
    }

    // ── NWPs tab ──────────────────────────────────────────────────────────────

    private void RefreshNwpList()
    {
        _nwpSettings.Clear();
        foreach (var kv in _app.CharacterOptions.GetNonweaponProficiencySettings())
            _nwpSettings[kv.Key] = kv.Value;

        _customNwpIds = new Dictionary<string, CustomNwpData>(
            _app.CharacterOptions.GetCustomNwps(), System.StringComparer.OrdinalIgnoreCase);

        string search        = NwpEditorSearch.Text.Trim();
        string categoryFilter = (NwpCategoryFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        string sourceFilter   = (NwpSourceFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

        _nwpItems = _app.CharacterOptions.GetCatalog().NonweaponProficiencies
            .Where(nwp =>
                (string.IsNullOrWhiteSpace(search)
                    || nwp.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || nwp.Category.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || nwp.Description.Contains(search, System.StringComparison.OrdinalIgnoreCase))
                && (categoryFilter is "" or "All Categories"
                    || NwpMatchesCategoryFilter(nwp, categoryFilter))
                && (sourceFilter is "" or "All Sources"
                    || string.Equals(nwp.Source, sourceFilter, System.StringComparison.OrdinalIgnoreCase)
                    // "Core & Player's Option" entries show up under either individual source filter
                    || (string.Equals(nwp.Source, "Core & Player's Option", System.StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(sourceFilter, "Core", System.StringComparison.OrdinalIgnoreCase)
                            || string.Equals(sourceFilter, "Player's Option", System.StringComparison.OrdinalIgnoreCase)))))
            .OrderBy(nwp => nwp.Category)
            .ThenBy(nwp => nwp.Name)
            .ToList();

        NwpEditorList.ItemsSource = _nwpItems
            .Select(nwp =>
            {
                string star = _customNwpIds.ContainsKey(nwp.Id) ? "★ " : string.Empty;
                bool missingPo = nwp.CpCost == 0
                    && nwp.PlayersOptionBaseRating == 0
                    && !string.Equals(nwp.Source, "Player's Option", System.StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(nwp.Source, "Core & Player's Option", System.StringComparison.OrdinalIgnoreCase);
                string display = $"{star}{nwp.Name} [{nwp.Category}]  —  {nwp.Source}";
                return new NwpListItem(display, missingPo);
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedNwpId))
        {
            int idx = _nwpItems.FindIndex(x => string.Equals(x.Id, _selectedNwpId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                NwpEditorList.SelectedIndex = idx;
        }

        if (NwpEditorList.SelectedIndex < 0 && _nwpItems.Count > 0)
            NwpEditorList.SelectedIndex = 0;

        // Source diagnostics — count by source in the FULL (unfiltered) catalog.
        var all = _app.CharacterOptions.GetCatalog().NonweaponProficiencies;
        int coreOnly    = all.Count(n => string.Equals(n.Source, "Core", System.StringComparison.OrdinalIgnoreCase));
        int poOnly      = all.Count(n => string.Equals(n.Source, "Player's Option", System.StringComparison.OrdinalIgnoreCase));
        int corePo      = all.Count(n => string.Equals(n.Source, "Core & Player's Option", System.StringComparison.OrdinalIgnoreCase));
        int supp        = all.Count(n => string.Equals(n.Source, "Supplement", System.StringComparison.OrdinalIgnoreCase));
        int customCount = all.Count(n => _customNwpIds.ContainsKey(n.Id));
        int warriorEligible = all.Count(n => n.AllowedClasses is { Count: > 0 }
            && (n.AllowedClasses.Contains("fighter", System.StringComparer.OrdinalIgnoreCase)
                || n.AllowedClasses.Contains("paladin", System.StringComparer.OrdinalIgnoreCase)
                || n.AllowedClasses.Contains("ranger", System.StringComparer.OrdinalIgnoreCase)));
        NwpSourceDiagnosticsText.Text =
            $"Core: {coreOnly}  |  PO: {poOnly}  |  Core+PO: {corePo}  |  Supplement: {supp}  |  ★ Custom: {customCount}  |  Warrior-eligible: {warriorEligible}  |  Total: {all.Count}";
    }

    private void NwpEditorSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TabNwps.Visibility == Visibility.Visible)
            RefreshNwpList();
    }

    private void NwpFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabNwps.Visibility == Visibility.Visible)
            RefreshNwpList();
    }

    private void NwpEditorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = NwpEditorList.SelectedIndex;
        if (idx < 0 || idx >= _nwpItems.Count)
            return;

        _creatingNewNwp = false;
        var nwp = _nwpItems[idx];
        _selectedNwpId = nwp.Id;

        _updatingNwpUi = true;

        NwpEditorTitle.Text = "Edit Proficiency";
        NwpEditName.Text = nwp.Name;
        NwpEditorDescription.Text = nwp.Description;

        SelectComboItem(NwpEditCategory, nwp.Category);
        SelectComboItem(NwpEditSource, nwp.Source);
        string coreAbilityDisplay = string.IsNullOrWhiteSpace(nwp.CheckAbility)
            ? "None"
            : CoreAbilityKeyToDisplay.GetValueOrDefault(NormalizeCoreAbilityKey(nwp.CheckAbility), NormalizeCoreAbilityKey(nwp.CheckAbility));
        SelectComboItem(NwpEditCoreAbility, coreAbilityDisplay);
        string poAbilityKey = ResolvePoAbilityKey(nwp);
        string poAbilityDisplay = string.IsNullOrWhiteSpace(poAbilityKey)
            ? "None"
            : PoAbilityKeyToDisplay.GetValueOrDefault(poAbilityKey, poAbilityKey);
        SelectComboItem(NwpEditPoAbility, poAbilityDisplay);
        NwpEditSlots.Text   = nwp.Slots.ToString();
        NwpEditCpCost.Text  = nwp.CpCost.ToString();
        NwpEditCoreModifier.Text = nwp.CheckModifier.ToString();
        NwpEditPoModifier.Text   = nwp.PlayersOptionBaseRating.ToString();
        NwpEditAllowedClasses.Text = nwp.AllowedClasses is { Count: > 0 }
            ? string.Join(", ", nwp.AllowedClasses)
            : string.Empty;
        NwpImportReference.Text = BuildNwpImportReference(nwp);

        bool isCustom = _customNwpIds.ContainsKey(nwp.Id);
        BtnDeleteNwp.IsEnabled = isCustom;
        NwpEditorInfo.Text = isCustom ? "★ Custom / overridden definition" : string.Empty;

        if (!_nwpSettings.TryGetValue(nwp.Id, out var setting))
        {
            setting = new NonweaponProficiencySetting
            {
                Id = nwp.Id,
                AllowMultiple = nwp.AllowMultiple,
                RequiresPlayerText = nwp.RequiresPlayerText,
            };
            _nwpSettings[nwp.Id] = setting;
        }

        NwpAllowMultipleCheck.IsChecked = setting.AllowMultiple;
        NwpRequireTextCheck.IsChecked   = setting.RequiresPlayerText;

        _updatingNwpUi = false;
    }

    private void NwpSettingCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingNwpUi || string.IsNullOrWhiteSpace(_selectedNwpId))
            return;

        if (!_nwpSettings.TryGetValue(_selectedNwpId, out var setting))
        {
            setting = new NonweaponProficiencySetting { Id = _selectedNwpId };
            _nwpSettings[_selectedNwpId] = setting;
        }

        setting.AllowMultiple      = NwpAllowMultipleCheck.IsChecked == true;
        setting.RequiresPlayerText = NwpRequireTextCheck.IsChecked   == true;
    }

    private void BtnNewNwp_Click(object sender, RoutedEventArgs e)
    {
        _creatingNewNwp = true;
        _selectedNwpId  = string.Empty;
        _updatingNwpUi  = true;

        NwpEditorTitle.Text  = "New Proficiency";
        NwpEditName.Text     = string.Empty;
        NwpEditorDescription.Text = string.Empty;
        NwpEditSlots.Text    = "1";
        NwpEditCpCost.Text   = "0";
        NwpEditCoreModifier.Text = "0";
        NwpEditPoModifier.Text   = "0";
        SelectComboItem(NwpEditCategory, "General");
        SelectComboItem(NwpEditSource,   "Custom");
        SelectComboItem(NwpEditCoreAbility,  "Intelligence");
        SelectComboItem(NwpEditPoAbility,  "Intelligence");
        NwpAllowMultipleCheck.IsChecked = false;
        NwpRequireTextCheck.IsChecked   = false;
        BtnDeleteNwp.IsEnabled = false;
        NwpEditorInfo.Text = "Fill in the fields and click SAVE PROFICIENCY DEFINITION.";
        NwpEditAllowedClasses.Text = string.Empty;
        NwpImportReference.Text = "New custom NWP. Import metadata will be blank unless this overrides an imported entry.";
        NwpEditorList.SelectedIndex = -1;

        _updatingNwpUi = false;
        NwpEditName.Focus();
    }

    private void BtnDeleteNwp_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedNwpId))
            return;

        string label = NwpEditName.Text.Trim();
        var answer = MessageBox.Show(
            $"Delete the custom definition for \"{label}\"?\n\nParsed data (if any) will be restored on next load.",
            "Delete Custom NWP",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        _app.CharacterOptions.DeleteCustomNwp(_selectedNwpId);
        _app.CharacterOptions.InvalidateCache();
        _selectedNwpId = string.Empty;
        NwpEditorInfo.Text = "Custom definition deleted.";
        RefreshNwpList();
    }

    private void BtnSaveNwpDefinition_Click(object sender, RoutedEventArgs e)
    {
        string name = NwpEditName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(NwpEditSlots.Text.Trim(), out int slots) || slots < 1)
            slots = 1;
        if (!int.TryParse(NwpEditCoreModifier.Text.Trim(), out int coreModifier))
            coreModifier = 0;
        if (!int.TryParse(NwpEditPoModifier.Text.Trim(), out int poBaseRating) || poBaseRating < 0)
            poBaseRating = 0;
        if (!int.TryParse(NwpEditCpCost.Text.Trim(), out int cpCost) || cpCost < 0)
            cpCost = 0;

        string category = (NwpEditCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "General";
        string source   = (NwpEditSource.SelectedItem   as ComboBoxItem)?.Content?.ToString() ?? "Custom";
        string coreAbilityDisplay = (NwpEditCoreAbility.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Intelligence";
        string coreAbility = CoreAbilityDisplayToKey.GetValueOrDefault(coreAbilityDisplay, coreAbilityDisplay);
        string poAbilityDisplay = (NwpEditPoAbility.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Intelligence";
        string poAbility = PoAbilityDisplayToKey.GetValueOrDefault(poAbilityDisplay, poAbilityDisplay);

        string id = _creatingNewNwp || string.IsNullOrWhiteSpace(_selectedNwpId)
            ? GenerateNwpId(name)
            : _selectedNwpId;

        var existingImport = _app.CharacterOptions.GetCatalog().NonweaponProficiencies
            .FirstOrDefault(x => string.Equals(x.Id, id, System.StringComparison.OrdinalIgnoreCase));

        var data = new CustomNwpData
        {
            Id            = id,
            Name          = name,
            Category      = category,
            Source        = source,
            Slots         = slots,
            CheckAbility  = coreAbility,
            CheckModifier = coreModifier,
            Description   = NwpEditorDescription.Text.Trim(),
            CpCost        = cpCost,
            PlayersOptionBaseRating = poBaseRating,
            PlayersOptionCheckAbility = poAbility,
            AllowedClasses = NwpEditAllowedClasses.Text
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList(),
            GroupFamily = existingImport?.GroupFamily ?? string.Empty,
            ProficiencyGroup = existingImport?.ProficiencyGroup ?? string.Empty,
            IsMultiGroup = existingImport?.IsMultiGroup ?? false,
            GroupsFound = existingImport?.GroupsFound ?? string.Empty,
            PlayersOptionRaw = existingImport?.PlayersOptionRaw ?? string.Empty,
            SourceBook = existingImport?.SourceBook ?? string.Empty,
            SourceTag = existingImport?.SourceTag ?? string.Empty,
            SettingName = existingImport?.SettingName ?? string.Empty,
            Origin = existingImport?.Origin ?? string.Empty,
            DescriptionPreview = existingImport?.DescriptionPreview ?? string.Empty,
            HasDescription = existingImport?.HasDescription ?? !string.IsNullOrWhiteSpace(NwpEditorDescription.Text),
        };

        // Also persist the DM settings (allow-multiple, require-text) for this entry.
        if (!_nwpSettings.TryGetValue(id, out var setting))
        {
            setting = new NonweaponProficiencySetting { Id = id };
            _nwpSettings[id] = setting;
        }
        setting.AllowMultiple      = NwpAllowMultipleCheck.IsChecked == true;
        setting.RequiresPlayerText = NwpRequireTextCheck.IsChecked   == true;

        try
        {
            _app.CharacterOptions.SaveCustomNwp(data);
            _app.CharacterOptions.SaveNonweaponProficiencySettings(_nwpSettings.Values);
            _app.CharacterOptions.InvalidateCache();
            _selectedNwpId  = id;
            _creatingNewNwp = false;
            BtnDeleteNwp.IsEnabled = true;
            NwpEditorInfo.Text = $"★ Saved: {name}";
            RefreshNwpList();

            // If filters hide the saved entry, reset to all so the user can immediately verify the change.
            if (!_nwpItems.Any(x => string.Equals(x.Id, id, System.StringComparison.OrdinalIgnoreCase)))
            {
                SelectComboItem(NwpCategoryFilter, "All Categories");
                SelectComboItem(NwpSourceFilter, "All Sources");
                RefreshNwpList();
                NwpEditorInfo.Text = $"★ Saved: {name} (filters reset to show saved item)";
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnSaveNwpSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _app.CharacterOptions.SaveNonweaponProficiencySettings(_nwpSettings.Values);
            _app.CharacterOptions.InvalidateCache();
            MessageBox.Show("NWP settings saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshNwpList();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Save NWP Settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCrossoverReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var candidates = _app.CharacterOptions.GetCrossoverCandidates();

            if (candidates.Count == 0)
            {
                MessageBox.Show(
                    "No unmatched crossover candidates found.\nAll Player's Option entries already have exact name matches in the DOCX source.",
                    "Crossover Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Unmatched Crossover Candidates ({candidates.Count})");
            sb.AppendLine("These PO entries were auto-merged via normalized-name fallback.");
            sb.AppendLine("Consider aligning names in the DM editor for cleaner data.");
            sb.AppendLine(new string('─', 60));
            sb.AppendLine();
            foreach (var (docxName, docxCat, poName, poCat) in candidates)
            {
                sb.AppendLine($"  DOCX: {docxName}  [{docxCat}]");
                sb.AppendLine($"  PO:   {poName}  [{poCat}]");
                sb.AppendLine();
            }

            var win = new Window
            {
                Title = "Crossover Report",
                Width  = 520,
                Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = System.Windows.Media.Brushes.Black,
            };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            scroll.Content = new TextBox
            {
                Text = sb.ToString(),
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                Background = System.Windows.Media.Brushes.Black,
                Foreground = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10),
            };
            win.Content = scroll;
            win.ShowDialog();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Crossover Report Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string GenerateNwpId(string name)
    {
        string slug = System.Text.RegularExpressions.Regex
            .Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_")
            .Trim('_');
        return $"nwp_custom_{slug}";
    }

    private static string BuildNwpImportReference(NonweaponProficiencyDefinition nwp)
    {
        string allowed = nwp.AllowedClasses is { Count: > 0 }
            ? string.Join(", ", nwp.AllowedClasses)
            : "(all classes)";

        return string.Join(System.Environment.NewLine, new[]
        {
            $"Group Family: {nwp.GroupFamily}",
            $"Proficiency Group: {nwp.ProficiencyGroup}",
            $"Multi-Group?: {(nwp.IsMultiGroup ? "Y" : "N")}",
            $"Groups Found: {nwp.GroupsFound}",
            $"Allowed Classes: {allowed}",
            $"PO Check Ability/Subability: {ResolvePoAbilityKey(nwp)}",
            $"Source Book: {nwp.SourceBook}",
            $"Source Tag: {nwp.SourceTag}",
            $"Setting: {nwp.SettingName}",
            $"Origin: {nwp.Origin}",
            $"PO Raw: {nwp.PlayersOptionRaw}",
            $"Has Description: {(nwp.HasDescription ? "Y" : "N")}",
            $"Description Preview: {nwp.DescriptionPreview}",
        });
    }

    private static bool NwpMatchesCategoryFilter(NonweaponProficiencyDefinition nwp, string filter)
    {
        if (string.Equals(nwp.Category, filter, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(nwp.ProficiencyGroup, filter, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(nwp.GroupsFound))
            return false;

        return nwp.GroupsFound
            .Split(';', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .Any(group => string.Equals(group, filter, System.StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePoAbilityKey(NonweaponProficiencyDefinition nwp)
    {
        if (!string.IsNullOrWhiteSpace(nwp.PlayersOptionCheckAbility))
            return nwp.PlayersOptionCheckAbility;
        return nwp.CheckAbility;
    }

    private static string NormalizeCoreAbilityKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        if (string.Equals(key, "str_stamina", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "str_muscle", System.StringComparison.OrdinalIgnoreCase))
            return "Strength";
        if (string.Equals(key, "dex_aim", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "dex_balance", System.StringComparison.OrdinalIgnoreCase))
            return "Dexterity";
        if (string.Equals(key, "con_health", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "con_fitness", System.StringComparison.OrdinalIgnoreCase))
            return "Constitution";
        if (string.Equals(key, "int_reason", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "int_knowledge", System.StringComparison.OrdinalIgnoreCase))
            return "Intelligence";
        if (string.Equals(key, "wis_intuition", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "wis_willpower", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "wis_perception", System.StringComparison.OrdinalIgnoreCase))
            return "Wisdom";
        if (string.Equals(key, "cha_leadership", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "cha_appearance", System.StringComparison.OrdinalIgnoreCase))
            return "Charisma";

        return key;
    }

    private static void SelectComboItem(ComboBox combo, string value)
    {
        foreach (object raw in combo.Items)
        {
            if (raw is not ComboBoxItem item)
                continue;

            if (string.Equals(item.Content?.ToString(), value, System.StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    // ── Spells tab ───────────────────────────────────────────────────────────

    private void RefreshSpellEditor()
    {
        string search = SpellSearchBox.Text?.Trim() ?? "";
        var totalCategoryCount = _app.Rules.Spells.Count(s => string.Equals(s.Category, _activeSpellCategory, System.StringComparison.OrdinalIgnoreCase));

        _spellItems = _app.Rules.Spells
            .Where(s => string.Equals(s.Category, _activeSpellCategory, System.StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(search)
                    || s.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || s.Id.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || s.Schools.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || s.BriefDescription.Contains(search, System.StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => SpellLevelSortKey(s.Level))
            .ThenBy(s => s.Name)
            .ToList();

        SpellListEditor.ItemsSource = _spellItems;
        SpellCountText.Text = $"Showing {_spellItems.Count} of {totalCategoryCount} {SpellCategoryDisplayName(_activeSpellCategory)} spells";

        if (!string.IsNullOrWhiteSpace(_selectedSpellId))
        {
            int idx = _spellItems.FindIndex(s => string.Equals(s.Id, _selectedSpellId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                SpellListEditor.SelectedIndex = idx;
        }
    }

    private void SpellSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TabSpells.Visibility == Visibility.Visible)
            RefreshSpellEditor();
    }

    private void SpellListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpellListEditor.SelectedItem is not SpellDefinition spell)
            return;

        _selectedSpellId = spell.Id;
        SpellId.Text = spell.Id;
        SpellLevel.Text = spell.Level;
        SpellName.Text = spell.Name;
        SpellReversal.Text = spell.Reversal;
        SpellSchools.Text = spell.Schools;
        SpellRange.Text = spell.Range;
        SpellComponents.Text = spell.Components;
        SpellMaterials.Text = spell.Materials;
        SpellCastTime.Text = spell.CastTime;
        SpellDuration.Text = spell.Duration;
        SpellArea.Text = spell.Area;
        SpellSave.Text = spell.Save;
        SpellFrequency.Text = spell.Frequency;
        SpellVolume.Text = spell.Volume;
        SpellPage.Text = spell.Page;
        SpellIsHealing.IsChecked = spell.IsHealing;
        SpellDamage.Text = spell.Damage;
        SpellDamageStep.Text = spell.DamageStep;
        SpellDamageScaleStartLevel.Text = spell.DamageScaleStartLevel;
        SpellDamageScaleEveryLevels.Text = spell.DamageScaleEveryLevels;
        SpellDamageMaxAtLevel.Text = spell.DamageMaxAtLevel;
        SpellDamageMax.Text = spell.DamageMax;
        UpdateSpellDamagePreview();
        SpellBriefDescription.Text = spell.BriefDescription;
        SpellDescription.Text = spell.Description;
    }

    private void BtnNewSpell_Click(object sender, RoutedEventArgs e)
    {
        _selectedSpellId = string.Empty;
        SpellListEditor.SelectedItem = null;
        ClearSpellEditor();
    }

    private void ClearSpellEditor()
    {
        SpellId.Text = string.Empty;
        SpellLevel.Text = "1";
        SpellName.Text = string.Empty;
        SpellReversal.Text = string.Empty;
        SpellSchools.Text = string.Empty;
        SpellRange.Text = string.Empty;
        SpellComponents.Text = string.Empty;
        SpellMaterials.Text = string.Empty;
        SpellCastTime.Text = string.Empty;
        SpellDuration.Text = string.Empty;
        SpellArea.Text = string.Empty;
        SpellSave.Text = string.Empty;
        SpellFrequency.Text = string.Empty;
        SpellVolume.Text = string.Empty;
        SpellPage.Text = string.Empty;
        SpellIsHealing.IsChecked = false;
        SpellDamage.Text = string.Empty;
        SpellDamageStep.Text = string.Empty;
        SpellDamageScaleStartLevel.Text = string.Empty;
        SpellDamageScaleEveryLevels.Text = string.Empty;
        SpellDamageMaxAtLevel.Text = string.Empty;
        SpellDamageMax.Text = string.Empty;
        UpdateSpellDamagePreview();
        SpellBriefDescription.Text = string.Empty;
        SpellDescription.Text = string.Empty;
    }

    private void SpellDamageFields_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSpellDamagePreview();
    }

    private void SpellIsHealing_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSpellDamagePreview();
    }

    private void UpdateSpellDamagePreview()
    {
        SpellDamagePreview.Text = BuildSpellDamageSummary(
            SpellDamage.Text,
            SpellDamageStep.Text,
            SpellDamageScaleStartLevel.Text,
            SpellDamageScaleEveryLevels.Text,
            SpellDamageMaxAtLevel.Text,
            SpellDamageMax.Text,
            SpellIsHealing.IsChecked == true);
    }

    private void BtnSaveSpell_Click(object sender, RoutedEventArgs e)
    {
        string name = SpellName.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Spell Name is required.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryValidateSpellDamageFields(out string damageValidationMessage))
        {
            MessageBox.Show(damageValidationMessage, "Invalid Damage Scaling", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string originalId = _selectedSpellId;
        string enteredId = SpellId.Text?.Trim() ?? string.Empty;
        string finalId;

        if (string.IsNullOrWhiteSpace(enteredId))
        {
            finalId = EnsureUniqueSpellId(BuildGeneratedSpellId(name), originalId);
        }
        else
        {
            bool duplicateExists = _app.Rules.Spells.Any(s =>
                !string.Equals(s.Id, originalId, System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Id, enteredId, System.StringComparison.OrdinalIgnoreCase));
            if (duplicateExists)
            {
                MessageBox.Show("SpellID already exists. Enter a unique SpellID.", "Duplicate SpellID", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            finalId = enteredId;
        }

        var updated = new SpellDefinition(
            finalId,
            _activeSpellCategory,
            SpellLevel.Text?.Trim() ?? string.Empty,
            name,
            SpellReversal.Text?.Trim() ?? string.Empty,
            SpellSchools.Text?.Trim() ?? string.Empty,
            SpellRange.Text?.Trim() ?? string.Empty,
            SpellComponents.Text?.Trim() ?? string.Empty,
            SpellMaterials.Text?.Trim() ?? string.Empty,
            SpellCastTime.Text?.Trim() ?? string.Empty,
            SpellDuration.Text?.Trim() ?? string.Empty,
            SpellArea.Text?.Trim() ?? string.Empty,
            SpellSave.Text?.Trim() ?? string.Empty,
            SpellFrequency.Text?.Trim() ?? string.Empty,
            SpellVolume.Text?.Trim() ?? string.Empty,
            SpellPage.Text?.Trim() ?? string.Empty,
            SpellIsHealing.IsChecked == true,
            SpellDamage.Text?.Trim() ?? string.Empty,
            SpellDamageStep.Text?.Trim() ?? string.Empty,
            SpellDamageScaleStartLevel.Text?.Trim() ?? string.Empty,
            SpellDamageScaleEveryLevels.Text?.Trim() ?? string.Empty,
            SpellDamageMaxAtLevel.Text?.Trim() ?? string.Empty,
            SpellDamageMax.Text?.Trim() ?? string.Empty,
            SpellBriefDescription.Text?.Trim() ?? string.Empty,
            SpellDescription.Text?.Trim() ?? string.Empty);

        var allSpells = _app.Rules.Spells
            .Where(s => !string.Equals(s.Id, originalId, System.StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { updated })
            .OrderBy(s => SpellCategorySortKey(s.Category))
            .ThenBy(s => SpellLevelSortKey(s.Level))
            .ThenBy(s => s.Name);

        _app.Rules.SaveSpellDefinitions(allSpells);
        _selectedSpellId = finalId;
        SpellId.Text = finalId;
        RefreshSpellEditor();
        FlashSpellSavedIndicator();
    }

    private void BtnDeleteSpell_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedSpellId))
            return;

        string displayName = _spellItems.FirstOrDefault(s => string.Equals(s.Id, _selectedSpellId, System.StringComparison.OrdinalIgnoreCase))?.Name ?? _selectedSpellId;
        var result = MessageBox.Show(
            $"Delete spell '{displayName}'? This cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        var remaining = _app.Rules.Spells.Where(s => !string.Equals(s.Id, _selectedSpellId, System.StringComparison.OrdinalIgnoreCase));
        _app.Rules.SaveSpellDefinitions(remaining);
        _selectedSpellId = string.Empty;
        SpellListEditor.SelectedItem = null;
        ClearSpellEditor();
        RefreshSpellEditor();
    }

    private void BtnExportSpells_Click(object sender, RoutedEventArgs e)
    {
        string categoryLabel = SpellCategoryDisplayName(_activeSpellCategory);
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Export {categoryLabel} Spells",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"{categoryLabel.ToLowerInvariant()}_spells_export.xlsx",
        };
        if (dlg.ShowDialog() != true)
            return;

        var spells = _app.Rules.Spells
            .Where(s => string.Equals(s.Category, _activeSpellCategory, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => SpellLevelSortKey(s.Level))
            .ThenBy(s => s.Name)
            .ToList();

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.AddWorksheet("Spells");

        // Header row
        string[] headers =
        [
            "ID", "Category", "Level", "Name", "Reversal", "Schools",
            "Range", "Components", "Materials", "Cast Time", "Duration", "Area",
            "Save", "Frequency", "Volume", "Page",
            "Healing",
            "Damage", "Damage Step", "Start Level", "Every Levels", "Max At Level", "Max Damage",
            "Brief Description", "Description"
        ];
        for (int col = 0; col < headers.Length; col++)
        {
            var cell = ws.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.DarkBlue;
            cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
        }

        // Data rows
        for (int row = 0; row < spells.Count; row++)
        {
            var s = spells[row];
            int r = row + 2;
            ws.Cell(r, 1).Value = s.Id;
            ws.Cell(r, 2).Value = s.Category;
            ws.Cell(r, 3).Value = s.Level;
            ws.Cell(r, 4).Value = s.Name;
            ws.Cell(r, 5).Value = s.Reversal;
            ws.Cell(r, 6).Value = s.Schools;
            ws.Cell(r, 7).Value = s.Range;
            ws.Cell(r, 8).Value = s.Components;
            ws.Cell(r, 9).Value = s.Materials;
            ws.Cell(r, 10).Value = s.CastTime;
            ws.Cell(r, 11).Value = s.Duration;
            ws.Cell(r, 12).Value = s.Area;
            ws.Cell(r, 13).Value = s.Save;
            ws.Cell(r, 14).Value = s.Frequency;
            ws.Cell(r, 15).Value = s.Volume;
            ws.Cell(r, 16).Value = s.Page;
            ws.Cell(r, 17).Value = s.IsHealing;
            ws.Cell(r, 18).Value = s.Damage;
            ws.Cell(r, 19).Value = s.DamageStep;
            ws.Cell(r, 20).Value = s.DamageScaleStartLevel;
            ws.Cell(r, 21).Value = s.DamageScaleEveryLevels;
            ws.Cell(r, 22).Value = s.DamageMaxAtLevel;
            ws.Cell(r, 23).Value = s.DamageMax;
            ws.Cell(r, 24).Value = s.BriefDescription;
            ws.Cell(r, 25).Value = s.Description;

            // Zebra stripe
            if (row % 2 == 1)
            {
                var rowRange = ws.Range(r, 1, r, headers.Length);
                rowRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1A1A2E");
                rowRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            }
        }

        // Auto-fit columns, but cap description columns to avoid massive widths
        ws.Columns(1, 23).AdjustToContents();
        ws.Column(24).Width = 50;
        ws.Column(25).Width = 80;
        ws.Column(24).Style.Alignment.WrapText = true;
        ws.Column(25).Style.Alignment.WrapText = true;

        // Freeze header row
        ws.SheetView.FreezeRows(1);

        // Auto-filter on headers
        ws.RangeUsed()!.SetAutoFilter();

        try
        {
            wb.SaveAs(dlg.FileName);
            MessageBox.Show($"Exported {spells.Count} {categoryLabel} spells to:\n{dlg.FileName}",
                "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnImportSpells_Click(object sender, RoutedEventArgs e)
    {
        string categoryLabel = SpellCategoryDisplayName(_activeSpellCategory);
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Import {categoryLabel} Spells from Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
        };
        if (dlg.ShowDialog() != true)
            return;

        int updated = 0;
        int skipped = 0;
        var errors = new System.Text.StringBuilder();

        try
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(dlg.FileName);
            var ws = wb.Worksheets.First();

            // Build header → column index map from row 1
            var headerMap = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int col = 1; col <= lastCol; col++)
            {
                string header = ws.Cell(1, col).GetValue<string>().Trim();
                if (!string.IsNullOrEmpty(header))
                    headerMap[header] = col;
            }

            if (!headerMap.ContainsKey("ID"))
            {
                MessageBox.Show("The file must have an 'ID' column in row 1 to match spells.",
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string Get(ClosedXML.Excel.IXLRow row, string col) =>
                headerMap.TryGetValue(col, out int c) ? row.Cell(c).GetValue<string>().Trim() : string.Empty;

            static bool ParseBool(string text)
            {
                string value = (text ?? string.Empty).Trim();
                if (bool.TryParse(value, out bool parsed))
                    return parsed;

                return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "y", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }

            // Build a mutable lookup of all spells by ID
            var spellDict = _app.Rules.Spells.ToDictionary(
                s => s.Id, s => s,
                System.StringComparer.OrdinalIgnoreCase);

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                string id = Get(row, "ID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    skipped++;
                    continue;
                }

                if (!spellDict.TryGetValue(id, out var existing))
                {
                    errors.AppendLine($"Row {r}: ID '{id}' not found — skipped.");
                    skipped++;
                    continue;
                }

                // Replace with values from xlsx; blank cells clear the existing value
                var updated_spell = existing with
                {
                    Level                    = Get(row, "Level"),
                    Name                     = string.IsNullOrWhiteSpace(Get(row, "Name")) ? existing.Name : Get(row, "Name"),
                    Reversal                 = Get(row, "Reversal"),
                    Schools                  = Get(row, "Schools"),
                    Range                    = Get(row, "Range"),
                    Components               = Get(row, "Components"),
                    Materials                = Get(row, "Materials"),
                    CastTime                 = Get(row, "Cast Time"),
                    Duration                 = Get(row, "Duration"),
                    Area                     = Get(row, "Area"),
                    Save                     = Get(row, "Save"),
                    Frequency                = Get(row, "Frequency"),
                    Volume                   = Get(row, "Volume"),
                    Page                     = Get(row, "Page"),
                    IsHealing                = ParseBool(Get(row, "Healing")),
                    Damage                   = Get(row, "Damage"),
                    DamageStep               = Get(row, "Damage Step"),
                    DamageScaleStartLevel    = Get(row, "Start Level"),
                    DamageScaleEveryLevels   = Get(row, "Every Levels"),
                    DamageMaxAtLevel         = Get(row, "Max At Level"),
                    DamageMax                = Get(row, "Max Damage"),
                    BriefDescription         = Get(row, "Brief Description"),
                    Description              = Get(row, "Description"),
                };

                spellDict[id] = updated_spell;
                updated++;
            }

            // Persist — keep all spells, replace updated ones
            var finalList = _app.Rules.Spells.Select(s =>
                spellDict.TryGetValue(s.Id, out var updated_s) ? updated_s : s);
            _app.Rules.SaveSpellDefinitions(finalList);

            RefreshSpellEditor();

            string summary = $"Import complete.\nUpdated: {updated}  |  Skipped: {skipped}";
            if (errors.Length > 0)
                summary += $"\n\nWarnings:\n{errors}";
            MessageBox.Show(summary, "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed: {ex.Message}", "Import Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void FlashSpellSavedIndicator()
    {
        SpellSavedIndicator.Visibility = Visibility.Visible;
        await System.Threading.Tasks.Task.Delay(2000);
        SpellSavedIndicator.Visibility = Visibility.Collapsed;
    }

    private void TabSpells_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.S &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            BtnSaveSpell_Click(sender, e);
            e.Handled = true;
        }
    }

    private static string SpellCategoryDisplayName(string category) => category switch
    {
        "divine" => "Divine",
        "psionic" => "Psionic",
        _ => "Arcane",
    };

    private static int SpellCategorySortKey(string category) => category switch
    {
        "arcane" => 0,
        "divine" => 1,
        "psionic" => 2,
        _ => 9,
    };

    private static int SpellLevelSortKey(string level)
    {
        return int.TryParse(level, out int parsed) ? parsed : 99;
    }

    private bool TryValidateSpellDamageFields(out string message)
    {
        string damage = SpellDamage.Text?.Trim() ?? string.Empty;
        string step = SpellDamageStep.Text?.Trim() ?? string.Empty;
        string startLevelText = SpellDamageScaleStartLevel.Text?.Trim() ?? string.Empty;
        string everyLevelsText = SpellDamageScaleEveryLevels.Text?.Trim() ?? string.Empty;
        string maxAtLevelText = SpellDamageMaxAtLevel.Text?.Trim() ?? string.Empty;
        string maxDamage = SpellDamageMax.Text?.Trim() ?? string.Empty;

        bool hasScaling = !string.IsNullOrWhiteSpace(step)
            || !string.IsNullOrWhiteSpace(startLevelText)
            || !string.IsNullOrWhiteSpace(everyLevelsText)
            || !string.IsNullOrWhiteSpace(maxAtLevelText)
            || !string.IsNullOrWhiteSpace(maxDamage);

        if (hasScaling && string.IsNullOrWhiteSpace(damage))
        {
            message = "Base Damage is required when using damage scaling fields.";
            return false;
        }

        if (!TryParsePositiveIntField(startLevelText, "Start At Level", out int? startLevel, out message))
            return false;
        if (!TryParsePositiveIntField(everyLevelsText, "Increase Every", out int? everyLevels, out message))
            return false;
        if (!TryParsePositiveIntField(maxAtLevelText, "Max At Level", out int? maxAtLevel, out message))
            return false;

        if (!string.IsNullOrWhiteSpace(step) && !everyLevels.HasValue)
        {
            message = "Increase Every is required when Increase By is set.";
            return false;
        }

        if (everyLevels.HasValue && !startLevel.HasValue)
        {
            message = "Start At Level is required when Increase Every is set.";
            return false;
        }

        if (maxAtLevel.HasValue && !startLevel.HasValue)
        {
            message = "Start At Level is required when Max At Level is set.";
            return false;
        }

        if (maxAtLevel.HasValue && startLevel.HasValue && maxAtLevel.Value < startLevel.Value)
        {
            message = "Max At Level cannot be lower than Start At Level.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(maxDamage) && !maxAtLevel.HasValue)
        {
            message = "Max At Level is required when Max Damage is set.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool TryParsePositiveIntField(string text, string fieldName, out int? value, out string message)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            message = string.Empty;
            return true;
        }

        if (!int.TryParse(text, out int parsed) || parsed <= 0)
        {
            message = $"{fieldName} must be a positive whole number.";
            return false;
        }

        value = parsed;
        message = string.Empty;
        return true;
    }

    private static string BuildSpellDamageSummary(
        string? damage,
        string? damageStep,
        string? startLevel,
        string? everyLevels,
        string? maxAtLevel,
        string? maxDamage,
        bool isHealing)
    {
        string baseDamage = (damage ?? string.Empty).Trim();
        string step = (damageStep ?? string.Empty).Trim();
        string start = (startLevel ?? string.Empty).Trim();
        string every = (everyLevels ?? string.Empty).Trim();
        string maxLevel = (maxAtLevel ?? string.Empty).Trim();
        string capDamage = (maxDamage ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(baseDamage)
            && string.IsNullOrWhiteSpace(step)
            && string.IsNullOrWhiteSpace(start)
            && string.IsNullOrWhiteSpace(every)
            && string.IsNullOrWhiteSpace(maxLevel)
            && string.IsNullOrWhiteSpace(capDamage))
        {
            return isHealing
                ? "No healing scaling configured."
                : "No damage scaling configured.";
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(baseDamage))
            parts.Add($"Base: {baseDamage}");
        if (!string.IsNullOrWhiteSpace(step))
            parts.Add($"+{step}");
        if (!string.IsNullOrWhiteSpace(every))
        {
            string cadence = $"every {every} level";
            if (every != "1")
                cadence += "s";

            if (!string.IsNullOrWhiteSpace(start))
                parts.Add($"from level {start}, {cadence}");
            else
                parts.Add(cadence);
        }
        else if (!string.IsNullOrWhiteSpace(start))
        {
            parts.Add($"starts at level {start}");
        }

        if (!string.IsNullOrWhiteSpace(maxLevel) && !string.IsNullOrWhiteSpace(capDamage))
            parts.Add($"caps at level {maxLevel} ({capDamage})");
        else if (!string.IsNullOrWhiteSpace(maxLevel))
            parts.Add($"caps at level {maxLevel}");
        else if (!string.IsNullOrWhiteSpace(capDamage))
            parts.Add($"max damage {capDamage}");

        string summary = string.Join("; ", parts);
        return isHealing ? $"Heals: {summary}" : summary;
    }

    private string BuildGeneratedSpellId(string name)
    {
        string prefix = _activeSpellCategory switch
        {
            "divine" => "div_custom_",
            "psionic" => "psi_custom_",
            _ => "arc_custom_",
        };
        return prefix + SlugifySimple(name);
    }

    private string EnsureUniqueSpellId(string baseId, string originalId)
    {
        string candidate = baseId;
        int suffix = 2;
        while (_app.Rules.Spells.Any(s =>
            !string.Equals(s.Id, originalId, System.StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Id, candidate, System.StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseId}_{suffix}";
            suffix++;
        }

        return candidate;
    }

    // ── Monsters tab ─────────────────────────────────────────────────────────

    private void TabBtnMonsters_Click(object sender, RoutedEventArgs e) => ShowTab("monsters");

    private void RefreshMonsterEditor()
    {
        string search = MonsterSearchBox.Text?.Trim() ?? "";
        string typeFilter = MonsterTypeFilter.SelectedItem is string s && s != "(All Types)" ? s : "";

        // Rebuild type filter ComboBox if empty
        if (MonsterTypeFilter.Items.Count <= 1)
        {
            MonsterTypeFilter.Items.Clear();
            MonsterTypeFilter.Items.Add("(All Types)");
            foreach (var t in _app.Rules.Monsters.Select(m => m.MonsterType).Distinct().OrderBy(x => x))
                MonsterTypeFilter.Items.Add(t);
            MonsterTypeFilter.SelectedIndex = 0;
        }

        _monsterItems = _app.Rules.Monsters
            .Where(m => (string.IsNullOrEmpty(search) ||
                         m.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase) ||
                         m.MonsterType.Contains(search, System.StringComparison.OrdinalIgnoreCase)) &&
                        (string.IsNullOrEmpty(typeFilter) || m.MonsterType == typeFilter))
            .OrderBy(m => m.Name)
            .ToList();

        MonsterListEditor.ItemsSource = _monsterItems;
        MonsterCountText.Text = $"Showing {_monsterItems.Count} of {_app.Rules.Monsters.Count} monsters";

        if (!string.IsNullOrEmpty(_selectedMonsterId))
        {
            int idx = _monsterItems.FindIndex(m => string.Equals(m.Id, _selectedMonsterId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) MonsterListEditor.SelectedIndex = idx;
        }
    }

    private void RefreshMonsterEditorList() => RefreshMonsterEditor();

    private void MonsterSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (TabMonsters.Visibility == Visibility.Visible) RefreshMonsterEditor();
    }

    private void MonsterTypeFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TabMonsters.Visibility == Visibility.Visible) RefreshMonsterEditor();
    }

    private void MonsterListEditor_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MonsterListEditor.SelectedItem is not MonsterDefinition m) return;
        _selectedMonsterId = m.Id;
        MonsterName.Text           = m.Name;
        MonsterType.Text           = m.MonsterType;
        MonsterHitDice.Text        = m.HitDice;
        MonsterAC.Text             = m.ArmorClass.ToString();
        MonsterThac0.Text          = m.EffectiveThac0Text;
        MonsterMovement.Text       = m.Movement;
        MonsterAttacks.Text        = m.Attacks.ToString();
        MonsterDamage.Text         = m.Damage;
        MonsterSize.Text           = m.Size;
        MonsterIntelligence.Text   = m.Intelligence;
        MonsterAlignment.Text      = m.Alignment;
        MonsterMorale.Text         = m.Morale;
        MonsterXP.Text             = m.XpValue.ToString();
        MonsterNumAppearing.Text   = m.NumberAppearing;
        MonsterFrequency.Text      = m.Frequency;
        MonsterTreasure.Text       = m.TreasureType;
        MonsterMagicRes.Text       = m.MagicResistance;
        MonsterSpecialAttacks.Text = m.SpecialAttacks;
        MonsterSpecialDefenses.Text = m.SpecialDefenses;
        MonsterSpecialAbilities.Text = m.SpecialAbilities;
        MonsterCombat.Text         = m.Combat;
        MonsterHabitatSociety.Text = m.HabitatSociety;
        MonsterEcology.Text        = m.Ecology;
        MonsterSource.Text         = m.Source;
        MonsterDescription.Text    = m.Description;
        LoadAgeStages(m.AgeStages, IsDragonLike(m.MonsterType, m.Name));
    }

    private void BtnNewMonster_Click(object sender, RoutedEventArgs e)
    {
        _selectedMonsterId = string.Empty;
        MonsterName.Text = "New Monster";
        MonsterType.Text = "";
        MonsterHitDice.Text = "1";
        MonsterAC.Text = "10";
        MonsterThac0.Text = "20";
        MonsterMovement.Text = "12";
        MonsterAttacks.Text = "1";
        MonsterDamage.Text = "1d6";
        MonsterSize.Text = "M";
        MonsterIntelligence.Text = "Average";
        MonsterAlignment.Text = "Neutral";
        MonsterMorale.Text = "Average (9)";
        MonsterXP.Text = "0";
        MonsterNumAppearing.Text = "1";
        MonsterFrequency.Text = "Uncommon";
        MonsterTreasure.Text = "Nil";
        MonsterMagicRes.Text = "Nil";
        MonsterSpecialAttacks.Text = "None";
        MonsterSpecialDefenses.Text = "None";
        MonsterSpecialAbilities.Text = "None";
        MonsterCombat.Text = "";
        MonsterHabitatSociety.Text = "";
        MonsterEcology.Text = "";
        MonsterSource.Text = "Custom";
        MonsterDescription.Text = "";
        LoadAgeStages(null, false);
        MonsterListEditor.SelectedItem = null;
    }

    private void BtnSaveMonster_Click(object sender, RoutedEventArgs e)
    {
        string name = MonsterName.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name)) return;

        int.TryParse(MonsterAC.Text,      out int ac);
        var thac0Input = MonsterThac0.Text?.Trim() ?? "";
        int.TryParse(thac0Input, out int thac0);
        int.TryParse(MonsterAttacks.Text, out int attacks);
        int.TryParse(MonsterXP.Text,      out int xp);

        string id = string.IsNullOrWhiteSpace(_selectedMonsterId)
            ? "mon_" + name.ToLowerInvariant().Replace(" ", "_").Replace(",", "").Replace("'", "")
            : _selectedMonsterId;

        // collect age stages from the editable grid
        List<DragonAgeStage>? ageStages = _ageStageRows.Count > 0
            ? _ageStageRows.Select(vm => new DragonAgeStage(
                vm.StageNumber, vm.Category, vm.HitDice, vm.ArmorClass,
                vm.Thac0, vm.BreathWeapon, vm.SpellLevel, vm.MagicResistance, vm.SpecialAbilities))
              .ToList()
            : null;

        var updated = new MonsterDefinition(
            id, name,
            MonsterType.Text?.Trim() ?? "",
            MonsterSource.Text?.Trim() ?? "",
            MonsterHitDice.Text?.Trim() ?? "",
            ac,
            MonsterMovement.Text?.Trim() ?? "",
            thac0,
            attacks <= 0 ? 1 : attacks,
            MonsterDamage.Text?.Trim() ?? "",
            MonsterSpecialAttacks.Text?.Trim() ?? "",
            MonsterSpecialDefenses.Text?.Trim() ?? "",
            MonsterMagicRes.Text?.Trim() ?? "",
            MonsterSize.Text?.Trim() ?? "",
            MonsterMorale.Text?.Trim() ?? "",
            xp,
            MonsterNumAppearing.Text?.Trim() ?? "",
            MonsterFrequency.Text?.Trim() ?? "",
            MonsterIntelligence.Text?.Trim() ?? "",
            MonsterAlignment.Text?.Trim() ?? "",
            MonsterTreasure.Text?.Trim() ?? "",
            MonsterDescription.Text?.Trim() ?? "",
            ageStages,
            MonsterSpecialAbilities.Text?.Trim() ?? "",
            MonsterCombat.Text?.Trim() ?? "",
            MonsterHabitatSociety.Text?.Trim() ?? "",
            MonsterEcology.Text?.Trim() ?? ""
        )
        {
            Thac0Text = thac0Input
        };

        var allMonsters = _app.Rules.Monsters
            .Where(m => !string.Equals(m.Id, id, System.StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { updated })
            .OrderBy(m => m.Name);

        _app.Rules.SaveMonsterDefinitions(allMonsters);
        _selectedMonsterId = id;
        RefreshMonsterEditorList();
        FlashMonsterSavedIndicator();
    }

    private async void FlashMonsterSavedIndicator()
    {
        MonsterSavedIndicator.Visibility = Visibility.Visible;
        await System.Threading.Tasks.Task.Delay(2000);
        MonsterSavedIndicator.Visibility = Visibility.Collapsed;
    }

    private void TabMonsters_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.S &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            BtnSaveMonster_Click(sender, e);
            e.Handled = true;
        }
    }

    private void BtnDeleteMonster_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedMonsterId)) return;
        string displayName = _monsterItems.FirstOrDefault(m => m.Id == _selectedMonsterId)?.Name ?? _selectedMonsterId;
        var result = System.Windows.MessageBox.Show(
            $"Delete monster '{displayName}'? This cannot be undone.",
            "Confirm Delete", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        var remaining = _app.Rules.Monsters.Where(m => m.Id != _selectedMonsterId);
        _app.Rules.SaveMonsterDefinitions(remaining);
        _selectedMonsterId = string.Empty;
        MonsterTypeFilter.Items.Clear();
        RefreshMonsterEditorList();
    }

    private static bool IsDragonLike(string? monsterType, string? monsterName)
    {
        if (!string.IsNullOrWhiteSpace(monsterType) &&
            monsterType.Contains("dragon", System.StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(monsterName) &&
            monsterName.Contains("dragon", System.StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string GetDragonAgeCategory(int stage) => stage switch
    {
        1 => "Hatchling",
        2 => "Very Young",
        3 => "Young",
        4 => "Juvenile",
        5 => "Young Adult",
        6 => "Adult",
        7 => "Mature Adult",
        8 => "Old",
        9 => "Very Old",
        10 => "Venerable",
        11 => "Wyrm",
        _ => "Great Wyrm",
    };

    private static string GetDragonMagicResistanceByStage(int stage) => stage switch
    {
        <= 2 => "Nil",
        3 => "5%",
        4 => "10%",
        5 => "15%",
        6 => "20%",
        7 => "25%",
        8 => "30%",
        9 => "35%",
        10 => "40%",
        11 => "45%",
        _ => "50%",
    };

    private static string GetDragonSpellLevelByStage(int stage) => stage switch
    {
        <= 2 => "None",
        3 => "1st",
        4 => "2nd",
        5 => "3rd",
        6 => "4th",
        7 => "5th",
        8 => "6th",
        9 => "7th",
        10 => "8th",
        _ => "9th",
    };

    private static int ParseLeadingInt(string? text, int fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        int i = 0;
        while (i < text.Length && !char.IsDigit(text[i]) && text[i] != '-') i++;
        int start = i;
        if (i < text.Length && text[i] == '-') i++;
        while (i < text.Length && char.IsDigit(text[i])) i++;
        return i > start && int.TryParse(text[start..i], out int parsed) ? parsed : fallback;
    }

    // AD&D 2e Monstrous Manual dragon table: HD modifiers by age category 1..12.
    // Base species stats are treated as Juvenile (category 4), where modifier is 0.
    private static readonly int[] DragonHdModifiersByStage = { -6, -4, -2, 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    private static string ComputeDragonBreathByAge(int derivedHd)
    {
        // Canonical rule: breath weapon damage equals the dragon's current hit points.
        // We display an HD-linked approximation for convenience.
        return $"Current HP (about {System.Math.Max(1, derivedHd)}d8)";
    }

    private void GetDragonBaseStats(out int baseHd, out int baseAc, out int baseThac0)
    {
        baseHd = ParseLeadingInt(MonsterHitDice.Text, 6);

        int.TryParse(MonsterAC.Text, out baseAc);
        if (baseAc == 0 && string.IsNullOrWhiteSpace(MonsterAC.Text)) baseAc = 10;

        baseThac0 = ParseLeadingInt(MonsterThac0.Text, 20);
    }

    private static void ComputeDerivedDragonCombat(int stage, int baseHd, int baseAc, int baseThac0,
        out int derivedHd, out int derivedAc, out int derivedThac0, out string derivedBreath)
    {
        int stageIndex = System.Math.Clamp(stage, 1, 12) - 1;
        int hdMod = DragonHdModifiersByStage[stageIndex];

        // HD uses the official age modifier table.
        derivedHd = System.Math.Max(1, baseHd + hdMod);

        // AC progression is 1 point per age category relative to Juvenile (category 4).
        derivedAc = baseAc + (4 - stage);

        // THAC0 tracks age/HD progression from the species base THAC0 (juvenile baseline).
        derivedThac0 = baseThac0 - hdMod;

        derivedBreath = ComputeDragonBreathByAge(derivedHd);
    }

    private void InitializeDragonStageTemplateRows(bool includeDerivedDefaults)
    {
        _ageStageRows.Clear();

        GetDragonBaseStats(out int baseHd, out int baseAc, out int baseThac0);

        for (int stage = 1; stage <= 12; stage++)
        {
            ComputeDerivedDragonCombat(stage, baseHd, baseAc, baseThac0,
                out int derivedHd, out int derivedAc, out int derivedThac0, out string derivedBreath);

            _ageStageRows.Add(new DragonAgeStageVm
            {
                StageNumber      = stage,
                Category         = GetDragonAgeCategory(stage),
                HitDice          = includeDerivedDefaults ? derivedHd.ToString() : "",
                ArmorClass       = includeDerivedDefaults ? derivedAc : 0,
                Thac0            = includeDerivedDefaults ? derivedThac0 : 20,
                BreathWeapon     = includeDerivedDefaults ? derivedBreath : "",
                SpellLevel       = includeDerivedDefaults ? GetDragonSpellLevelByStage(stage) : "None",
                MagicResistance  = includeDerivedDefaults ? GetDragonMagicResistanceByStage(stage) : "Nil",
                SpecialAbilities = includeDerivedDefaults
                    ? string.Join("; ", new[]
                    {
                        MonsterSpecialAttacks.Text?.Trim(),
                        MonsterSpecialDefenses.Text?.Trim(),
                        MonsterSpecialAbilities.Text?.Trim()
                    }.Where(s => !string.IsNullOrWhiteSpace(s)))
                    : "",
            });
        }
    }

    private void LoadAgeStages(List<DragonAgeStage>? stages, bool isDragon)
    {
        _ageStageRows.Clear();

        GetDragonBaseStats(out int baseHd, out int baseAc, out int baseThac0);

        if (stages is { Count: > 0 })
        {
            foreach (var s in stages)
            {
                int stage = s.StageNumber > 0 ? s.StageNumber : (_ageStageRows.Count + 1);
                _ageStageRows.Add(new DragonAgeStageVm
                {
                    StageNumber      = stage,
                    Category         = isDragon ? GetDragonAgeCategory(stage) : s.Category,
                       HitDice          = s.HitDice,
                       ArmorClass       = s.ArmorClass,
                       Thac0            = s.Thac0,
                       BreathWeapon     = s.BreathWeapon ?? "",
                    SpellLevel       = string.IsNullOrWhiteSpace(s.SpellLevel) ? GetDragonSpellLevelByStage(stage) : s.SpellLevel,
                    MagicResistance  = string.IsNullOrWhiteSpace(s.MagicResistance) ? GetDragonMagicResistanceByStage(stage) : s.MagicResistance,
                    SpecialAbilities = s.SpecialAbilities,
                });
            }
            AgeStagesSection.Visibility = Visibility.Visible;
        }
        else if (isDragon)
        {
            // Restore the old dragon chart behavior: show a full 12-stage table even when data is missing.
            InitializeDragonStageTemplateRows(includeDerivedDefaults: true);
            AgeStagesSection.Visibility = Visibility.Visible;
        }
        else
        {
            AgeStagesSection.Visibility = Visibility.Collapsed;
        }
        AgeStagesGrid.ItemsSource = _ageStageRows;
    }

    private void BtnInitDragonStages_Click(object sender, RoutedEventArgs e)
    {
        if (_ageStageRows.Count > 0)
        {
            var confirm = System.Windows.MessageBox.Show(
                "Age stages already exist. Replace them with a blank 12-stage template?",
                "Initialize Stages", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
        }

        InitializeDragonStageTemplateRows(includeDerivedDefaults: true);

        AgeStagesGrid.ItemsSource = _ageStageRows;
        AgeStagesSection.Visibility = Visibility.Visible;
    }

    private void BtnClearDragonStages_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "Remove all age stages from this monster?",
            "Confirm Clear", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;
        _ageStageRows.Clear();
        AgeStagesSection.Visibility = Visibility.Collapsed;
    }
}

// ── Dragon age stage view-model (mutable; required for DataGrid two-way binding) ──
public class DragonAgeStageVm
{
    public int    StageNumber      { get; set; }
    public string Category         { get; set; } = "";
    public string HitDice          { get; set; } = "";
    public int    ArmorClass       { get; set; }
    public int    Thac0            { get; set; }
    public string BreathWeapon     { get; set; } = "";
    public string SpellLevel       { get; set; } = "";
    public string MagicResistance  { get; set; } = "";
    public string SpecialAbilities { get; set; } = "";
}
