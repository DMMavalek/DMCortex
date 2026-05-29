using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CharGenClassAbilitiesScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    private List<ClassDefinition> _classes = new();          // all selected classes (1 or more)
    private ClassDefinition? _activeClass;
    private List<AbilityDefinition> _optionalAbilities = new();
    private List<AbilityDefinition> _autoAssignedAbilities = new();
    private readonly List<string> _selectedAbilityEntries = new();
    private List<AbilityListItem> _availableItems = new();
    private List<AbilityListItem> _selectedItems = new();
    private bool _showDisadvantagesTab;

    private bool IsPlayersOptionMode
        => string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);

    // Forwarded from CharGenClassScreen (static so the two screens share it)
    internal static readonly Dictionary<string, (int minor, int major)> SphereCosts =
        CharGenClassScreen.SphereCosts;
    internal static int CalculateSphereCpCost(Dictionary<string, string> spheres)
        => CharGenClassScreen.CalculateSphereCpCost(spheres);
    internal static int CalculateWizardSchoolCpCost(Dictionary<string, bool> schools)
        => NormalizeWizardSchoolSelections(schools).Count(kv => kv.Value) * 5;

    private static int CalculateConfigurationCpCost(
        ClassDefinition cls,
        Dictionary<string, string> spheres,
        Dictionary<string, bool> schools)
    {
        if (string.Equals(cls.Id, "cleric", StringComparison.OrdinalIgnoreCase))
            return CalculateSphereCpCost(spheres);

        if (string.Equals(cls.Id, "wizard", StringComparison.OrdinalIgnoreCase))
            return CalculateWizardSchoolCpCost(schools);

        if (string.Equals(cls.Id, "druid", StringComparison.OrdinalIgnoreCase))
        {
            var fixedSphereAbility = cls.StructuredAbilities.FirstOrDefault(a =>
                string.Equals(a.Id, "druid_access_to_spheres", StringComparison.OrdinalIgnoreCase));
            return Math.Max(0, fixedSphereAbility?.PointCost ?? 60);
        }

        if (string.Equals(cls.Id, "paladin", StringComparison.OrdinalIgnoreCase))
            return CalculateSphereCpCost(spheres);

        if (string.Equals(cls.Id, "ranger", StringComparison.OrdinalIgnoreCase))
            return 0;

        return 0;
    }

    internal static Dictionary<string, bool> NormalizeWizardSchoolSelections(Dictionary<string, bool>? schools)
    {
        var normalized = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        if (schools is null)
            return normalized;

        foreach (var (school, selected) in schools)
        {
            var key = string.Equals(school, "Greater Divination", System.StringComparison.OrdinalIgnoreCase)
                ? "Divination"
                : school;

            normalized[key] = normalized.TryGetValue(key, out var existing)
                ? existing || selected
                : selected;
        }

        return normalized;
    }

    public CharGenClassAbilitiesScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        try
        {
            _app.CharGen.RecalculateLevelFromExistingExperience();
            _app.SetBanner("Character Blueprint  ›  Class Abilities");
            bool isPO = IsPlayersOptionMode;
            int totalSteps = isPO ? 9 : 8;
            _app.SetNavBar(6, totalSteps, "Class Abilities",
                backAction: () => _app.GoTo("chargen_class"),
                nextAction: Advance);

            _classes = _app.CharGen.SelectedClassIds
                .Select(id => _app.Rules.Classes.TryGetValue(id, out var c) ? c : null)
                .Where(c => c != null)
                .Select(c => c!)
                .ToList();

            if (_classes.Count == 0)
            {
                MessageBox.Show("Error: No class selected. Please go back.", "Invalid State",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _app.GoTo("chargen_class");
                return;
            }

            bool isMulti = _app.CharGen.ClassMode == "multiclass" && _classes.Count > 1;
            MultiClassTabPanel.Visibility = isMulti ? Visibility.Visible : Visibility.Collapsed;

            SubtitleLabel.Text = isPO
                ? (isMulti
                    ? $"Step 6  ·  Allocate class ability CP - {_classes.Count} classes"
                    : "Step 6  ·  Allocate class ability CP")
                : (isMulti
                    ? $"Step 6  ·  Core class package - {_classes.Count} classes"
                    : "Step 6  ·  Core class package");

            if (isMulti)
                BuildTabButtons();

            // Load the first (or only) class
            var startClass = _activeClass != null && _classes.Any(c => c.Id == _activeClass.Id)
                ? _activeClass
                : _classes[0];
            if (isMulti) LoadMultiClassState(startClass);
            _showDisadvantagesTab = false;
            SelectClass(startClass);
            ClassBudgetCard.Visibility = isPO ? Visibility.Visible : Visibility.Collapsed;

            if (!isPO)
            {
                BtnAbilitiesTab.Visibility = Visibility.Collapsed;
                BtnDisadvantagesTab.Visibility = Visibility.Collapsed;
                AvailableListTitle.Text = "Fixed Core Package";
                SelectedListTitle.Text = "Granted Core Package";
                AvailableClassAbilitySummary.Text = "Core Rules has no optional class ability purchasing.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error in CharGenClassAbilitiesScreen.OnEnter:\n\n{ex.GetType().Name}: {ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                "Debug Info", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    // â”€â”€ Tab buttons (multi-class) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void BuildTabButtons()
    {
        MultiClassClassTabs.Children.Clear();
        foreach (var cls in _classes)
        {
            bool isActive = _activeClass?.Id == cls.Id;
            var btn = new Button
            {
                Content = cls.Name,
                Tag = cls,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(16, 7, 16, 7),
                Background = isActive
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C4A468"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#221A0E")),
                Foreground = isActive
                    ? new SolidColorBrush(Colors.Black)
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C4A468")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C4A468")),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Georgia"),
                FontSize = 13,
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            var captured = cls;
            btn.Click += (_, __) => SwitchToClass(captured);
            MultiClassClassTabs.Children.Add(btn);
        }
    }

    private void SwitchToClass(ClassDefinition cls)
    {
        SaveCurrentMultiClassState();
        LoadMultiClassState(cls);
        SelectClass(cls);
    }

    private void SelectClass(ClassDefinition cls)
    {
        _activeClass = cls;
        BuildAbilityChoices(cls);
        RefreshBudgetSummary(cls);
        if (_app.CharGen.ClassMode == "multiclass") BuildTabButtons();
    }

    // â”€â”€ Per-class state save/load â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void SaveCurrentMultiClassState()
    {
        if (_activeClass == null || _app.CharGen.ClassMode != "multiclass") return;
        _app.CharGen.SelectedAbilitiesByClass[_activeClass.Id] = _selectedAbilityEntries.ToList();
        _app.CharGen.WizardSpecializationById[_activeClass.Id] = _app.CharGen.WizardSpecializationId ?? "";
        _app.CharGen.SpheresByClass[_activeClass.Id] = new(_app.CharGen.SelectedSpheres);
        _app.CharGen.SchoolsByClass[_activeClass.Id] = new(_app.CharGen.SelectedWizardSchools);
        _app.CharGen.RogueSkillPointsByClass[_activeClass.Id] = new(_app.CharGen.SelectedRogueSkillPoints);
        _app.CharGen.RogueSkillArmorByClass[_activeClass.Id] = _app.CharGen.RogueSkillArmorProfile ?? "no_armor";
    }

    private void LoadMultiClassState(ClassDefinition cls)
    {
        if (_app.CharGen.ClassMode != "multiclass") return;
        _app.CharGen.WizardSpecializationId = _app.CharGen.WizardSpecializationById.TryGetValue(cls.Id, out var spec) ? spec : "";
        _app.CharGen.SelectedSpheres = _app.CharGen.SpheresByClass.TryGetValue(cls.Id, out var spheres)
            ? new(spheres) : new();
        _app.CharGen.SelectedWizardSchools = _app.CharGen.SchoolsByClass.TryGetValue(cls.Id, out var schools)
            ? NormalizeWizardSchoolSelections(schools) : new();
        _app.CharGen.SelectedRogueSkillPoints = _app.CharGen.RogueSkillPointsByClass.TryGetValue(cls.Id, out var roguePoints)
            ? new(roguePoints)
            : new();
        _app.CharGen.RogueSkillArmorProfile = _app.CharGen.RogueSkillArmorByClass.TryGetValue(cls.Id, out var armor)
            ? armor
            : "no_armor";
    }

    // â”€â”€ Ability choices â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void BuildAbilityChoices(ClassDefinition cls)
    {
        List<AbilityDefinition> allOptionalAbilities;
        if (IsPlayersOptionMode)
        {
            _autoAssignedAbilities = cls.StructuredAbilities
                .Where(a => a.AutoGranted)
                .OrderBy(a => a.PointCost).ThenBy(a => a.Description)
                .ToList();

            var hiddenSpecializationAbilityIds = GetHiddenWizardSpecializationAbilityIds(cls);
            allOptionalAbilities = cls.StructuredAbilities
                .Where(a => !a.AutoGranted
                    && !IsSelectorManagedAbility(a)
                    && !hiddenSpecializationAbilityIds.Contains(a.Id))
                .OrderBy(a => a.PointCost).ThenBy(a => a.Description)
                .ToList();

            bool creationStage = Math.Max(1, _app.CharGen.CharacterLevel) <= 1;
            _optionalAbilities = allOptionalAbilities
                .Where(a => creationStage || a.AllowPurchaseAfterLevelOne)
                .ToList();
        }
        else
        {
            // Core Rules: class package is fixed; grant every class ability by default.
            _autoAssignedAbilities = cls.StructuredAbilities
                .OrderBy(a => a.PointCost)
                .ThenBy(a => a.Description)
                .ToList();
            allOptionalAbilities = new List<AbilityDefinition>();
            _optionalAbilities = new List<AbilityDefinition>();
        }

        _selectedAbilityEntries.Clear();
        var optionalIds = new HashSet<string>(allOptionalAbilities.Select(a => a.Id), System.StringComparer.OrdinalIgnoreCase);
        if (!IsPlayersOptionMode)
        {
            PersistCurrentSelections();
        }
        else if (_app.CharGen.ClassMode == "multiclass")
        {
            if (_app.CharGen.SelectedAbilitiesByClass.TryGetValue(cls.Id, out var saved))
            {
                foreach (var entry in saved)
                {
                    string baseId = RulesEngine.ExtractClassAbilityBaseId(entry);
                    if (optionalIds.Contains(baseId))
                        _selectedAbilityEntries.Add(entry);
                }
            }
        }
        else
        {
            foreach (var entry in _app.CharGen.SelectedClassAbilityIds)
            {
                string baseId = RulesEngine.ExtractClassAbilityBaseId(entry);
                if (optionalIds.Contains(baseId))
                    _selectedAbilityEntries.Add(entry);
            }
        }

        UpdateWizardSpecialtyUI(cls);
        UpdateSphereSelectionUI(cls);
        RefreshAbilityLists();
        RefreshSphereSchoolSummary(cls);

        ClassTitle.Text = cls.Name;
        ClassInfo.Text = cls.AbilityMinimums.Count > 0
            ? "Requirements: " + string.Join(", ", cls.AbilityMinimums.Select(kv => $"{RulesEngine.AbilityLabel(kv.Key)} {kv.Value}"))
            : "";
        ClassAbilityHint.Text = !IsPlayersOptionMode
            ? "Core Rules mode: all class abilities are granted automatically."
            : _optionalAbilities.Count == 0
                ? (Math.Max(1, _app.CharGen.CharacterLevel) > 1
                    ? "No optional abilities can be purchased at this level. Some abilities are creation-only."
                    : "This class has no optional abilities to configure.")
                : "Choose optional abilities on the left and move them into your package on the right.";
    }

    internal static bool IsSelectorManagedAbility(AbilityDefinition ability)
    {
        string abilityId = ability.Id ?? string.Empty;
        if (abilityId.StartsWith("cleric_sphere_minor_", StringComparison.OrdinalIgnoreCase)
            || abilityId.StartsWith("cleric_sphere_major_", StringComparison.OrdinalIgnoreCase)
            || abilityId.StartsWith("druid_sphere_minor_", StringComparison.OrdinalIgnoreCase)
            || abilityId.StartsWith("druid_sphere_major_", StringComparison.OrdinalIgnoreCase)
            || abilityId.StartsWith("ranger_sphere_minor_", StringComparison.OrdinalIgnoreCase)
            || abilityId.StartsWith("ranger_sphere_major_", StringComparison.OrdinalIgnoreCase)
            || abilityId.StartsWith("paladin_sphere_minor_", StringComparison.OrdinalIgnoreCase)
            || abilityId.StartsWith("paladin_sphere_major_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "cleric_sphere_access_minor",
            "cleric_sphere_access_major",
            "druid_access_to_spheres",
            "wizard_access_to_schools",
            "wizard_priestly_wizard",
            "druid_sphere_access_minor",
            "druid_sphere_access_major",
            "ranger_sphere_access_minor",
            "ranger_sphere_access_major",
            "paladin_sphere_access_minor",
            "paladin_sphere_access_major",
            "ranger_alternate_sphere_access",
            "paladin_alternate_sphere_access",
            "wizard_school_abjuration",
            "wizard_school_alteration",
            "wizard_school_conjuration_summoning",
            "wizard_school_divination",
            "wizard_school_enchantment_charm",
            "wizard_school_illusion",
            "wizard_school_invocation_evocation",
            "wizard_school_necromancy",
            "wizard_school_alchemy",
            "wizard_school_artifice",
            "wizard_school_dimensional",
            "wizard_school_force",
            "wizard_school_geometry",
            "wizard_school_shadow",
            "wizard_school_song",
            "wizard_school_wild",
            "wizard_school_elemental_air",
            "wizard_school_elemental_earth",
            "wizard_school_elemental_fire",
            "wizard_school_elemental_water",
        };

        if (ids.Contains(abilityId))
            return true;

        string category = ability.Category ?? string.Empty;
        if (string.Equals(category, "Arcane School", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "Thaumaturgy School", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "Sphere - Minor", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(category, "Sphere - Major", System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var description = ability.Description ?? string.Empty;
        return description.Contains("Access to schools", System.StringComparison.OrdinalIgnoreCase)
            || description.Contains("Access to spheres", System.StringComparison.OrdinalIgnoreCase)
            || description.Contains("Alternate sphere access", System.StringComparison.OrdinalIgnoreCase)
            || description.Contains("Gain one wizard school", System.StringComparison.OrdinalIgnoreCase)
            || description.Contains("priest sphere access", System.StringComparison.OrdinalIgnoreCase);
    }

    private HashSet<string> GetHiddenWizardSpecializationAbilityIds(ClassDefinition cls)
    {
        var hidden = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(cls.Id, "wizard", System.StringComparison.OrdinalIgnoreCase)
            || cls.Specializations == null
            || cls.Specializations.Count == 0)
        {
            return hidden;
        }

        foreach (var id in cls.Specializations.SelectMany(s => s.AutoSelectAbilityIds ?? Enumerable.Empty<string>()))
            hidden.Add(id);

        return hidden;
    }

    private void UpdateWizardSpecialtyUI(ClassDefinition cls)
    {
        if (cls.Id == "wizard")
        {
            ClassConfigTitle.Text = "Wizard Configuration";
            ClassConfigHint.Text = "Choose a specialty and configure school access.";
            BtnSelectWizardSchools.Visibility = Visibility.Visible;
        }
        else
        {
            BtnSelectWizardSchools.Visibility = Visibility.Collapsed;
        }
        BtnAllocateRogueSkills.Visibility = ClassSupportsRogueSkillConfiguration(cls) ? Visibility.Visible : Visibility.Collapsed;
        RefreshClassConfigurationCard(cls);
    }

    private void UpdateSphereSelectionUI(ClassDefinition cls)
    {
        bool canConfigurePaladinSpheres = ShouldConfigurePaladinSpheres(cls);
        bool canConfigureRangerSpheres = ShouldConfigureRangerSpheres(cls);
        BtnSelectSpheres.Visibility = cls.Id == "cleric" || canConfigurePaladinSpheres || canConfigureRangerSpheres
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (cls.Id == "cleric")
        {
            ClassConfigTitle.Text = "Priest Configuration";
            ClassConfigHint.Text = "Choose major and minor sphere access, then review the CP total below.";
        }
        if (cls.Id == "druid" && _app.CharGen.SelectedSpheres.Count == 0)
        {
            AutoFillDruidSpheres();
            RefreshAbilityLists();
            RefreshBudgetSummary(cls);
        }
        if (canConfigureRangerSpheres && _app.CharGen.SelectedSpheres.Count == 0)
        {
            AutoFillRangerDefaultSpheres();
        }
        if (cls.Id != "cleric" && cls.Id != "druid" && !canConfigurePaladinSpheres && !canConfigureRangerSpheres)
            _app.CharGen.SelectedSpheres.Clear();
        if (cls.Id == "druid")
        {
            ClassConfigTitle.Text = "Priest Configuration";
            ClassConfigHint.Text = "Druid sphere access is fixed and summarized below.";
        }
        if (canConfigurePaladinSpheres)
        {
            ClassConfigTitle.Text = "Priest Configuration";
            ClassConfigHint.Text = "Paladin alternate sphere access: choose minor and/or major sphere access.";
        }
        if (canConfigureRangerSpheres)
        {
            ClassConfigTitle.Text = "Priest Configuration";
            ClassConfigHint.Text = "Ranger alternate sphere access: swap one default minor sphere for another.";
        }
        if (ClassSupportsRogueSkillConfiguration(cls))
        {
            ClassConfigTitle.Text = "Rogue Skill Configuration";
            ClassConfigHint.Text = "Allocate rogue skill points using race, Dexterity, and armor adjustments.";
            ClassConfigSpecLabel.Visibility = Visibility.Collapsed;
            ClassConfigSpecLabel.Text = string.Empty;
        }
        RefreshClassConfigurationCard(cls);
    }

    private void RefreshClassConfigurationCard(ClassDefinition cls)
    {
        bool show = cls.Id == "wizard"
            || cls.Id == "cleric"
            || cls.Id == "druid"
            || ShouldConfigurePaladinSpheres(cls)
            || ShouldConfigureRangerSpheres(cls)
            || ClassSupportsRogueSkillConfiguration(cls);
        ClassConfigCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) ClassConfigHint.Text = string.Empty;
    }

    private void RefreshAbilityLists()
    {
        bool showDisadvantages = _showDisadvantagesTab;

        var selectedBaseIds = _selectedAbilityEntries
            .Select(RulesEngine.ExtractClassAbilityBaseId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        var selectedCountById = selectedBaseIds
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        bool showCpLabels = IsPlayersOptionMode;
        _availableItems = _optionalAbilities
            .Where(a => a.AllowMultiple || !selectedCountById.ContainsKey(a.Id))
            .Where(a => IsDisadvantageAbility(a) == showDisadvantages)
            .Select(a => ToAbilityItem(a, showCpLabels))
            .OrderBy(a => a.Label)
            .ToList();

        var hiddenSpecializationAbilityIds = _activeClass is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : GetHiddenWizardSpecializationAbilityIds(_activeClass);
        var optionalById = (_activeClass?.StructuredAbilities ?? new List<AbilityDefinition>())
            .Where(a => !a.AutoGranted
                && !IsSelectorManagedAbility(a)
                && !hiddenSpecializationAbilityIds.Contains(a.Id))
            .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var selectedOptional = _selectedAbilityEntries
            .Select(entry =>
            {
                string baseId = RulesEngine.ExtractClassAbilityBaseId(entry);
                return optionalById.TryGetValue(baseId, out var ability)
                    ? ToAbilityItem(ability, entry, showCpLabels)
                    : null;
            })
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(item => item.IsDisadvantage == showDisadvantages);

        if (showDisadvantages)
        {
            _selectedItems = selectedOptional
                .OrderBy(a => a.Label)
                .ToList();
        }
        else
        {
            _selectedItems = _autoAssignedAbilities.Select(a => ToAbilityItem(a, showCpLabels))
                .Concat(selectedOptional)
                .OrderBy(a => a.Label)
                .ToList();
        }

        AvailableClassAbilityList.ItemsSource = _availableItems;
        SelectedClassAbilityList.ItemsSource = _selectedItems;

        ApplyAbilityTabState();

        int auto = _autoAssignedAbilities.Count;
        int opt = _selectedItems.Count - auto;
        int availableDisadvantages = _availableItems.Count(a => a.IsDisadvantage);
        if (showDisadvantages)
        {
            AvailableClassAbilitySummary.Text = _availableItems.Count == 0
                ? "No disadvantages remain for this class."
                : $"{_availableItems.Count} disadvantage{(_availableItems.Count == 1 ? string.Empty : "s")} available.";
            SelectedClassAbilitySummary.Text = _selectedItems.Count == 0
                ? "No disadvantages selected."
                : $"{_selectedItems.Count} disadvantage{(_selectedItems.Count == 1 ? string.Empty : "s")} selected.";
        }
        else
        {
            AvailableClassAbilitySummary.Text = _availableItems.Count == 0
                ? "No optional abilities remain for this class."
                : $"{_availableItems.Count} optional abilit{(_availableItems.Count == 1 ? "y" : "ies")} available ({availableDisadvantages} disadvantages are on the Disadvantages tab).";
            SelectedClassAbilitySummary.Text = auto > 0
                ? $"{_selectedItems.Count} total: {auto} auto-granted, {opt} optional."
                : $"{_selectedItems.Count} optional abilit{(_selectedItems.Count == 1 ? "y" : "ies")} selected.";
        }
    }

    private void ApplyAbilityTabState()
    {
        AvailableListTitle.Text = _showDisadvantagesTab ? "Available Disadvantages" : "Available Abilities";
        SelectedListTitle.Text = _showDisadvantagesTab ? "Selected Disadvantages" : "Current Class Package";

        BtnAbilitiesTab.Style = (Style)FindResource(_showDisadvantagesTab ? "DarkButton" : "GoldButton");
        BtnDisadvantagesTab.Style = (Style)FindResource(_showDisadvantagesTab ? "GoldButton" : "DarkButton");
    }

    private void BtnAbilitiesTab_Click(object sender, RoutedEventArgs e)
    {
        _showDisadvantagesTab = false;
        RefreshAbilityLists();
    }

    private void BtnDisadvantagesTab_Click(object sender, RoutedEventArgs e)
    {
        _showDisadvantagesTab = true;
        RefreshAbilityLists();
    }

    private void RefreshBudgetSummary(ClassDefinition cls)
    {
        if (!IsPlayersOptionMode)
        {
            ClassBudgetSummary.Text = "Core";
            ClassBudgetDetail.Text = "Core Rules: fixed class package (no class CP budget).";
            ClassBudgetSummary.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#C4A468"));
            return;
        }

        var wizSpec = _app.CharGen.ClassMode == "multiclass"
            ? (_app.CharGen.WizardSpecializationById.TryGetValue(cls.Id, out var ws) ? ws : "")
            : _app.CharGen.WizardSpecializationId;

        var package = _app.Rules.BuildClassAbilityPackage(
            cls.Id, _selectedAbilityEntries.ToList(),
            _app.CharGen.RacialCarryoverToClassPoints, wizSpec);

        int configurationCost = CalculateConfigurationCpCost(
            cls,
            _app.CharGen.SelectedSpheres,
            _app.CharGen.SelectedWizardSchools);
        int totalSpent = package.spent + configurationCost;
        int remaining = package.budget - totalSpent;

        ClassBudgetSummary.Text = package.budget > 0 ? remaining.ToString() : "-";
        ClassBudgetDetail.Text = package.budget > 0
            ? $"Spent {totalSpent} / {package.budget} CP"
            : "No class CP budget for this class";
        ClassBudgetSummary.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(remaining < 0 ? "#C02828" : remaining == 0 ? "#E8C050" : "#C4A468"));
    }

    private void RefreshSphereSchoolSummary(ClassDefinition cls)
    {
        if (!IsPlayersOptionMode)
        {
            SphereSchoolSection.Visibility = Visibility.Collapsed;
            return;
        }

        if (cls.Id == "cleric")
        {
            SphereSchoolSection.Visibility = Visibility.Visible;
            SphereSchoolSectionLabel.Text = "PRIEST SPHERE ACCESS";
            var spheres = CharGenClassScreen.NormalizeSphereSelections(_app.CharGen.SelectedSpheres);
            _app.CharGen.SelectedSpheres = new(spheres);
            string sphereSummary = spheres.Count == 0
                ? "No spheres configured â€” click the button to select minor and/or major access."
                : $"{spheres.Count} sphere(s) â€” {CalculateSphereCpCost(spheres)} CP  Â·  {string.Join(", ", spheres.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} ({kv.Value})"))}";
            var selectedSkillIds = GetSelectedRogueSkillIds();
            if (selectedSkillIds.Count == 0)
            {
                SphereSchoolSectionSummary.Text = sphereSummary;
            }
            else
            {
                SphereSchoolSectionSummary.Text = $"{sphereSummary}\n{BuildRogueSkillSummary(selectedSkillIds)}";
            }
        }
        else if (cls.Id == "druid")
        {
            SphereSchoolSection.Visibility = Visibility.Visible;
            SphereSchoolSectionLabel.Text = "PRIEST SPHERE ACCESS  (Fixed â€” Druid)";
            int fixedCost = CalculateConfigurationCpCost(cls, _app.CharGen.SelectedSpheres, _app.CharGen.SelectedWizardSchools);
            SphereSchoolSectionSummary.Text = $"Auto-assigned: All, Elemental, Healing, Plant, Weather (minor access)  Â·  {fixedCost} CP";
        }
        else if (ShouldConfigurePaladinSpheres(cls))
        {
            SphereSchoolSection.Visibility = Visibility.Visible;
            SphereSchoolSectionLabel.Text = "PRIEST SPHERE ACCESS  (Paladin Alternate)";
            var spheres = CharGenClassScreen.NormalizeSphereSelections(_app.CharGen.SelectedSpheres);
            _app.CharGen.SelectedSpheres = new(spheres);
            int cost = CalculateSphereCpCost(spheres);
            SphereSchoolSectionSummary.Text = spheres.Count == 0
                ? "No alternate spheres configured â€” click the button to select minor and/or major access."
                : $"{spheres.Count} sphere(s) â€” {cost} CP  Â·  {string.Join(", ", spheres.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} ({kv.Value})"))}";
        }
        else if (ShouldConfigureRangerSpheres(cls))
        {
            SphereSchoolSection.Visibility = Visibility.Visible;
            SphereSchoolSectionLabel.Text = "PRIEST SPHERE ACCESS  (Ranger Alternate)";
            var spheres = NormalizeRangerAlternateSpheres(_app.CharGen.SelectedSpheres);
            _app.CharGen.SelectedSpheres = new(spheres);
            var defaults = GetRangerDefaultSpheres();
            string details = string.Join(", ", spheres.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} ({kv.Value})"));
            SphereSchoolSectionSummary.Text = !IsValidRangerSphereSwap(spheres)
                ? "Select exactly two minor spheres with exactly one default (Animal or Plant) and one replacement sphere."
                : $"Configured: {details}  Â·  default minor spheres are Animal + Plant (swap one).";
        }
        else if (cls.Id == "wizard")
        {
            SphereSchoolSection.Visibility = Visibility.Visible;
            SphereSchoolSectionLabel.Text = "WIZARD SCHOOLS OF MAGIC";
            _app.CharGen.SelectedWizardSchools = NormalizeWizardSchoolSelections(_app.CharGen.SelectedWizardSchools);
            var selected = _app.CharGen.SelectedWizardSchools
                .Where(kv => kv.Value)
                .Select(kv => kv.Key)
                .OrderBy(x => x)
                .ToList();
            int schoolCost = CalculateWizardSchoolCpCost(_app.CharGen.SelectedWizardSchools);

            if (string.IsNullOrEmpty(_app.CharGen.WizardSpecializationId))
            {
                SphereSchoolSectionSummary.Text = selected.Count == 0
                    ? "No schools configured — click the button to select school access."
                    : $"General Wizard — {selected.Count} school(s) selected — {schoolCost} CP  Â·  {string.Join(", ", selected)}";
            }
            else
            {
                var clsDef = _app.Rules.Classes.TryGetValue(cls.Id, out var cd) ? cd : null;
                var spec = clsDef?.Specializations?
                    .FirstOrDefault(s => string.Equals(s.Id, _app.CharGen.WizardSpecializationId, System.StringComparison.OrdinalIgnoreCase));
                var specName = spec?.Name ?? _app.CharGen.WizardSpecializationId;
                var opposed = spec?.OppositionSchools?.OrderBy(x => x).ToList() ?? new List<string>();
                var selectedText = selected.Count == 0 ? "none" : string.Join(", ", selected);
                var opposedText = opposed.Count == 0 ? "none" : string.Join(", ", opposed);
                SphereSchoolSectionSummary.Text = $"Specialist: {specName}  Â·  {selected.Count} school(s) selected — {schoolCost} CP  Â·  Purchased: {selectedText}  Â·  Opposed: {opposedText}";
            }
        }
        else if (ClassSupportsRogueSkillConfiguration(cls))
        {
            SphereSchoolSection.Visibility = Visibility.Visible;
            SphereSchoolSectionLabel.Text = "ROGUE SKILL PROFILE";

            var selectedSkillIds = GetSelectedRogueSkillIds();
            if (selectedSkillIds.Count == 0)
            {
                SphereSchoolSectionSummary.Text = "No scored rogue skills selected in class abilities yet.";
                return;
            }

            SphereSchoolSectionSummary.Text = BuildRogueSkillSummary(selectedSkillIds);
        }
        else
        {
            SphereSchoolSection.Visibility = Visibility.Collapsed;
        }
    }

    private string BuildRogueSkillSummary(List<string> selectedSkillIds)
    {
        int level = Math.Max(1, _app.CharGen.CharacterLevel);
        int dex = GetCurrentDexterityScore();
        string raceId = GetCurrentBaseRaceId();
        var breakdown = RulesEngine.BuildRogueSkillBreakdown(
            selectedSkillIds,
            raceId,
            dex,
            _app.CharGen.RogueSkillArmorProfile,
            _app.CharGen.SelectedRogueSkillPoints,
            level);

        int pool = RulesEngine.GetRogueSkillPointPoolForLevel(level);
        int spent = breakdown.Sum(x => x.AllocatedPoints);
        int remaining = pool - spent;
        string armorLabel = GetArmorProfileLabel(_app.CharGen.RogueSkillArmorProfile);
        string skillText = string.Join(", ", breakdown.Select(x => $"{x.SkillName} {x.FinalScore}%"));
        return $"Rogue skills â€” Armor: {armorLabel}  |  Points: {spent}/{pool} (remaining {remaining})  |  {skillText}";
    }

    private List<string> GetSelectedRogueSkillIds()
    {
        var selectedIds = _selectedAbilityEntries
            .Concat(_autoAssignedAbilities.Select(a => a.Id))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        return RulesEngine.GetRogueSkillIdsForAbilitySelection(selectedIds);
    }

    private int GetCurrentDexterityScore()
    {
        if (_app.CharGen.ModifiedAbilities.TryGetValue("dex", out var modifiedDex))
            return modifiedDex;
        if (_app.CharGen.Abilities.TryGetValue("dex", out var dex))
            return dex;
        return 10;
    }

    private string GetCurrentBaseRaceId()
    {
        if (!string.IsNullOrWhiteSpace(_app.CharGen.BaseRaceId))
            return _app.CharGen.BaseRaceId;
        return _app.CharGen.RaceId;
    }

    private static bool IsRogueClass(string? classId)
        => string.Equals(classId, "thief", StringComparison.OrdinalIgnoreCase)
           || string.Equals(classId, "bard", StringComparison.OrdinalIgnoreCase);

    private bool ClassSupportsRogueSkillConfiguration(ClassDefinition cls)
    {
        if (IsRogueClass(cls.Id))
            return true;

        return cls.StructuredAbilities.Any(a => string.Equals(a.Id, "cleric_thief_ability", StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.Id, "wizard_thief_ability", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetArmorProfileLabel(string? armorProfile)
    {
        return (armorProfile ?? "no_armor").Trim().ToLowerInvariant() switch
        {
            "elven_chain" => "Elven Chain",
            "studded_leather" => "Studded/Padded/Hide",
            "chain_or_ring_mail" => "Chain or Ring Mail",
            _ => "No Armor/Bracers",
        };
    }

    private void AutoFillDruidSpheres()
    {
        _app.CharGen.SelectedSpheres.Clear();
        _app.CharGen.SelectedSpheres["All"] = "minor";
        _app.CharGen.SelectedSpheres["Elemental"] = "minor";
        _app.CharGen.SelectedSpheres["Healing"] = "minor";
        _app.CharGen.SelectedSpheres["Plant"] = "minor";
        _app.CharGen.SelectedSpheres["Weather"] = "minor";
        RemoveSphereAccessAbilitiesFromSelection();
    }

    private void RemoveSphereAccessAbilitiesFromSelection()
    {
        foreach (var id in new[] { "cleric_sphere_access_minor", "cleric_sphere_access_major",
                                   "druid_sphere_access_minor", "druid_sphere_access_major",
                                   "ranger_sphere_access_minor", "ranger_sphere_access_major",
                                   "paladin_sphere_access_minor", "paladin_sphere_access_major" })
            _selectedAbilityEntries.RemoveAll(entry => string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry), id, StringComparison.OrdinalIgnoreCase));
    }

    private bool ShouldConfigurePaladinSpheres(ClassDefinition cls)
    {
        if (!string.Equals(cls.Id, "paladin", StringComparison.OrdinalIgnoreCase))
            return false;

        return _selectedAbilityEntries.Any(entry =>
            string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry), "paladin_alternate_sphere_access", StringComparison.OrdinalIgnoreCase));
    }

    private bool ShouldConfigureRangerSpheres(ClassDefinition cls)
    {
        if (!string.Equals(cls.Id, "ranger", StringComparison.OrdinalIgnoreCase))
            return false;

        return _selectedAbilityEntries.Any(entry =>
            string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry), "ranger_alternate_sphere_access", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> GetRangerDefaultSpheres()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Animal"] = "minor",
            ["Plant"] = "minor",
        };

    private static Dictionary<string, string> NormalizeRangerAlternateSpheres(Dictionary<string, string>? spheres)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (spheres is null)
            return normalized;

        foreach (var (sphere, access) in spheres)
        {
            if (string.IsNullOrWhiteSpace(sphere))
                continue;

            string tier = (access ?? string.Empty).Trim();
            if (!string.Equals(tier, "minor", StringComparison.OrdinalIgnoreCase))
                continue;

            normalized[sphere.Trim()] = "minor";
        }

        return normalized;
    }

    private static bool IsValidRangerSphereSwap(Dictionary<string, string> spheres)
    {
        if (spheres.Count != 2)
            return false;

        if (spheres.Values.Any(v => !string.Equals(v, "minor", StringComparison.OrdinalIgnoreCase)))
            return false;

        var defaults = GetRangerDefaultSpheres().Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int defaultCount = spheres.Keys.Count(k => defaults.Contains(k));
        return defaultCount == 1;
    }

    private void AutoFillRangerDefaultSpheres()
    {
        _app.CharGen.SelectedSpheres.Clear();
        foreach (var kv in GetRangerDefaultSpheres())
            _app.CharGen.SelectedSpheres[kv.Key] = kv.Value;
        RemoveSphereAccessAbilitiesFromSelection();
    }

    private static AbilityListItem ToAbilityItem(AbilityDefinition a, bool showCp = true)
    {
        var shortName = ToShortAbilityName(a.Description);
        bool isDisadvantage = IsDisadvantageAbility(a);
        string label = showCp
            ? (isDisadvantage
                ? $"{shortName} ({a.PointCost} CP)  (disadvantage)"
                : $"{shortName} ({a.PointCost} CP)")
            : shortName;
        return new AbilityListItem
        {
            Id = a.Id,
            BaseAbilityId = a.Id,
            SelectionEntry = a.Id,
            Label = label,
            Description = a.Description,
            IsAutoAssigned = a.AutoGranted,
            IsDisadvantage = isDisadvantage,
        };
    }

    private static AbilityListItem ToAbilityItem(AbilityDefinition a, string selectionEntry, bool showCp = true)
    {
        var effectiveAbility = new AbilityDefinition
        {
            Id = a.Id,
            Description = a.Description,
            Category = a.Category,
            PointCost = RulesEngine.GetConfiguredClassAbilityPointCost(a, selectionEntry),
            AutoGranted = a.AutoGranted,
            AllowMultiple = a.AllowMultiple,
            RequiresPlayerText = a.RequiresPlayerText,
            AllowPurchaseAfterLevelOne = a.AllowPurchaseAfterLevelOne,
            Effect = a.Effect,
        };
        var item = ToAbilityItem(effectiveAbility, showCp);
        string playerText = RulesEngine.FormatClassAbilitySelectionText(a.Id, RulesEngine.ExtractClassAbilityPlayerText(selectionEntry));
        if (!string.IsNullOrWhiteSpace(playerText))
            item.Label += $" [Selection: {playerText}]";
        item.SelectionEntry = selectionEntry;
        return item;
    }

    private static bool IsDisadvantageAbility(AbilityDefinition ability)
    {
        if (ability.PointCost < 0)
            return true;

        string id = ability.Id ?? string.Empty;
        string description = ability.Description ?? string.Empty;
        string category = ability.Category ?? string.Empty;

        return id.Contains("restriction", StringComparison.OrdinalIgnoreCase)
            || description.Contains("restriction", StringComparison.OrdinalIgnoreCase)
            || category.Contains("restriction", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToShortAbilityName(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "Ability";
        var name = description.Split(':', 2)[0].Trim();
        name = Regex.Replace(name, @"\s*\(\s*-?\d+\s*\)\s*$", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(name) ? description.Trim() : name;
    }

    private static readonly Dictionary<string, IReadOnlyList<string>> SelectionOptionsByAbilityId =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["cleric_proficiency_crossovers"] = new[]
            {
                "Warrior",
                "Mage",
                "Rogue",
            },
            ["cleric_restriction_limited_magical_item_use"] = new[]
            {
                "Potions/Oils/Scrolls",
                "Rings",
                "Rods/Staves/Wands",
                "Miscellaneous Magic",
                "Weapons and Armor",
            },
            ["druid_restriction_limited_magical_item_use"] = new[]
            {
                "Potions/Oils/Scrolls",
                "Rings",
                "Rods/Staves/Wands",
                "Miscellaneous Magic",
                "Weapons and Armor",
            },
            ["paladin_restriction_limited_magical_item_use"] = new[]
            {
                "Potions/Oils/Scrolls",
                "Rings",
                "Rods/Staves/Wands",
                "Miscellaneous Magic",
                "Weapons and Armor",
            },
            ["ranger_restriction_limited_magical_item_use"] = new[]
            {
                "Potions/Oils/Scrolls",
                "Rings",
                "Rods/Staves/Wands",
                "Miscellaneous Magic",
                "Weapons and Armor",
            },
            ["fighter_restriction_limited_magical_item_use"] = new[]
            {
                "Potions/Oils/Scrolls",
                "Rings",
                "Rods/Staves/Wands",
                "Miscellaneous Magic",
                "Weapons and Armor",
            },
            ["cleric_sphere_focus_bonus"] = SphereCosts.Keys.OrderBy(x => x).ToArray(),
            ["cleric_thief_ability"] = RulesEngine.ClericThiefAbilitySelectionToSkillId.Keys.ToArray(),
            ["cleric_wizardly_priests"] = new[]
            {
                "Abjuration",
                "Conjuration",
                "Divination",
                "Enchantment",
                "Illusion",
                "Invocation",
                "Necromancy",
                "Transmutation",
            },
            ["bard_school_specialization"] = new[]
            {
                "Enchantment/Charm",
                "Illusion",
                "Song",
            },
            ["bard_restriction_opposition_school"] = new[]
            {
                "Abjuration",
                "Alteration",
                "Conjuration/Summoning",
                "Divination",
                "Enchantment/Charm",
                "Illusion",
                "Invocation/Evocation",
                "Necromancy",
                "Song",
            },
            ["psionicist_discipline_mastery"] = new[]
            {
                "Clairsentience",
                "Psychokinesis",
                "Psychometabolism",
                "Psychoportation",
                "Telepathy",
                "Metapsionics",
            },
            ["psionicist_restriction_one_discipline"] = new[]
            {
                "Clairsentience",
                "Psychokinesis",
                "Psychometabolism",
                "Psychoportation",
                "Telepathy",
                "Metapsionics",
            },
            ["wizard_dispel_cp10"] = new[]
            {
                "Abjuration",
                "Alteration",
                "Conjuration/Summoning",
                "Divination",
                "Enchantment/Charm",
                "Illusion",
                "Invocation/Evocation",
                "Necromancy",
            },
            ["wizard_dispel_cp15"] = new[]
            {
                "Abjuration",
                "Alteration",
                "Conjuration/Summoning",
                "Divination",
                "Enchantment/Charm",
                "Illusion",
                "Invocation/Evocation",
                "Necromancy",
            },
            ["wizard_enhanced_casting_level"] = new[]
            {
                "Abjuration",
                "Alteration",
                "Conjuration/Summoning",
                "Divination",
                "Enchantment/Charm",
                "Illusion",
                "Invocation/Evocation",
                "Necromancy",
            },
            ["wizard_immunity"] = new[] { "Use spell picker" },
            ["wizard_learning_bonus_cp5"] = new[]
            {
                "Abjuration",
                "Alteration",
                "Conjuration/Summoning",
                "Divination",
                "Enchantment/Charm",
                "Illusion",
                "Invocation/Evocation",
                "Necromancy",
            },
            ["wizard_learning_bonus_cp7"] = new[]
            {
                "Abjuration",
                "Alteration",
                "Conjuration/Summoning",
                "Divination",
                "Enchantment/Charm",
                "Illusion",
                "Invocation/Evocation",
                "Necromancy",
            },
            ["wizard_restriction_learning_penalty"] = new[]
            {
                "Abjuration",
                "Alteration",
                "Conjuration/Summoning",
                "Divination",
                "Enchantment/Charm",
                "Illusion",
                "Invocation/Evocation",
                "Necromancy",
            },
            ["wizard_restriction_limited_magical_item_use"] = new[]
            {
                "Potions/Oils/Scrolls",
                "Rings",
                "Rods/Staves/Wands",
                "Miscellaneous Magic",
                "Weapons and Armor",
            },
            ["wizard_no_components_cp5"] = new[] { "Use spell picker" },
            ["wizard_no_components_cp8"] = new[] { "Use spell picker" },
            ["wizard_persistent_spell_effect"] = new[] { "Use spell picker" },
            ["wizard_priestly_wizard_cp10"] = SphereCosts.Keys.OrderBy(x => x).ToArray(),
            ["wizard_priestly_wizard_cp15"] = SphereCosts.Keys.OrderBy(x => x).ToArray(),
            ["wizard_proficiency_crossovers"] = new[]
            {
                "Warrior",
                "Priest",
                "Rogue",
            },
            ["wizard_range_increase_cp5"] = new[] { "25%" },
            ["wizard_range_increase_cp7"] = new[] { "50%" },
            ["wizard_research_bonus"] = new[]
            {
                "Abjuration",
                "Alteration",
                "Conjuration/Summoning",
                "Divination",
                "Enchantment/Charm",
                "Illusion",
                "Invocation/Evocation",
                "Necromancy",
            },
            ["wizard_spell_focus"] = new[]
            {
                "Abjuration",
                "Alteration",
                "Conjuration/Summoning",
                "Divination",
                "Enchantment/Charm",
                "Illusion",
                "Invocation/Evocation",
                "Necromancy",
                "Alchemy",
                "Artifice",
                "Dimensional",
                "Force",
                "Geometry",
                "Shadow",
                "Song",
                "Wild Magic",
                "Elemental (Air)",
                "Elemental (Earth)",
                "Elemental (Fire)",
                "Elemental (Water)",
            },
            ["wizard_thief_ability"] = RulesEngine.ClericThiefAbilitySelectionToSkillId.Keys.ToArray(),
        };

    private static readonly HashSet<string> UniqueSelectionTextAbilityIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "cleric_proficiency_crossovers",
            "cleric_restriction_limited_magical_item_use",
            "druid_restriction_limited_magical_item_use",
            "paladin_restriction_limited_magical_item_use",
            "ranger_restriction_limited_magical_item_use",
            "fighter_restriction_limited_magical_item_use",
            "cleric_sphere_focus_bonus",
            "cleric_thief_ability",
            "cleric_wizardly_priests",
            "bard_school_specialization",
            "bard_restriction_opposition_school",
            "psionicist_discipline_mastery",
            "psionicist_restriction_one_discipline",
            "wizard_dispel_cp10",
            "wizard_dispel_cp15",
            "wizard_enhanced_casting_level",
            "wizard_immunity",
            "wizard_learning_bonus_cp5",
            "wizard_learning_bonus_cp7",
            "wizard_restriction_learning_penalty",
            "wizard_restriction_limited_magical_item_use",
            "wizard_no_components_cp5",
            "wizard_no_components_cp8",
            "wizard_persistent_spell_effect",
            "wizard_priestly_wizard_cp10",
            "wizard_priestly_wizard_cp15",
            "wizard_proficiency_crossovers",
            "wizard_range_increase_cp5",
            "wizard_range_increase_cp7",
            "wizard_research_bonus",
            "wizard_spell_focus",
            "wizard_signature_spell_levels_1_3",
            "wizard_signature_spell_levels_4_6",
            "wizard_signature_spell_levels_7_9",
            "wizard_thief_ability",
            "wizard_restriction_awkward_casting_method",
            "wizard_restriction_behavior_taboo",
            "wizard_restriction_difficult_memorization",
            "wizard_restriction_environmental_condition",
            "wizard_restriction_supernatural_constraint",
            "wizard_restriction_talisman",
            "wizard_restriction_weapons_restriction_cp3",
        };

    // â”€â”€ Button handlers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void BtnAddClassAbility_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPlayersOptionMode)
            return;

        if (_activeClass is null || AvailableClassAbilityList.SelectedItem is not AbilityListItem item) return;
        var toAdd = _optionalAbilities.FirstOrDefault(a => string.Equals(a.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (toAdd is null)
            return;

        if (Math.Max(1, _app.CharGen.CharacterLevel) > 1 && !toAdd.AllowPurchaseAfterLevelOne)
        {
            MessageBox.Show(
                $"{toAdd.Description} can only be purchased during character creation.",
                "Selection Restricted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        bool alreadySelected = _selectedAbilityEntries.Any(entry =>
            string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry), toAdd.Id, StringComparison.OrdinalIgnoreCase));
        if (!toAdd.AllowMultiple && alreadySelected)
        {
            MessageBox.Show(
                $"{toAdd.Description} can only be selected once.",
                "Selection Restricted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (string.Equals(toAdd.Id, "wizard_no_components_cp8", StringComparison.OrdinalIgnoreCase))
        {
            int selectedCount = _selectedAbilityEntries.Count(entry =>
                string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry), toAdd.Id, StringComparison.OrdinalIgnoreCase));
            if (selectedCount >= 2)
            {
                MessageBox.Show(
                    "No components (8) allows exactly two selected spells.",
                    "Selection Restricted",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        // Turning Mastery: warn if Turn Undead not already selected
        if (string.Equals(toAdd.Id, "cleric_turning_mastery", StringComparison.OrdinalIgnoreCase))
        {
            bool hasTurnUndead = _selectedAbilityEntries.Any(entry =>
                string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry), "cleric_turn_undead", StringComparison.OrdinalIgnoreCase));
            if (!hasTurnUndead)
            {
                var warnResult = MessageBox.Show(
                    "Turn Undead is not selected. Turning Mastery requires Turn Undead to function properly. Proceed anyway?",
                    "Dependency Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (warnResult == MessageBoxResult.No)
                    return;
            }
        }

        string playerText = string.Empty;
        if (toAdd.RequiresPlayerText)
        {
            string? entered = PromptForClassAbilitySelectionText(toAdd.Id, toAdd.Description, string.Empty);
            if (entered is null)
                return;
            playerText = entered;

            if (UniqueSelectionTextAbilityIds.Contains(toAdd.Id))
            {
                bool duplicateSelection = _selectedAbilityEntries.Any(entry =>
                    string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry), toAdd.Id, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(RulesEngine.ExtractClassAbilityPlayerText(entry), playerText, StringComparison.OrdinalIgnoreCase));
                if (duplicateSelection)
                {
                    MessageBox.Show(
                        $"{ToShortAbilityName(toAdd.Description)} already includes '{playerText}'. Choose a different option.",
                        "Selection Restricted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
            }

            if (!ValidateWizardSignatureSelection(toAdd.Id, playerText))
                return;
        }

        string selectionEntry = RulesEngine.BuildClassAbilitySelectionEntry(toAdd.Id, playerText);
        if (string.IsNullOrWhiteSpace(selectionEntry))
            return;

        _selectedAbilityEntries.Add(selectionEntry);
        PersistCurrentSelections();
        RefreshAbilityLists();
        RefreshBudgetSummary(_activeClass);
        RefreshSphereSchoolSummary(_activeClass!);
    }

    private void BtnRemoveClassAbility_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPlayersOptionMode)
            return;

        if (_activeClass is null || SelectedClassAbilityList.SelectedItem is not AbilityListItem item) return;
        if (item.IsAutoAssigned) return;

        if (!string.IsNullOrWhiteSpace(item.SelectionEntry))
            _selectedAbilityEntries.Remove(item.SelectionEntry);
        else
        {
            int idx = _selectedAbilityEntries.FindLastIndex(entry =>
                string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry), item.BaseAbilityId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                _selectedAbilityEntries.RemoveAt(idx);
        }

        PersistCurrentSelections();
        RefreshAbilityLists();
        RefreshBudgetSummary(_activeClass);
        RefreshSphereSchoolSummary(_activeClass!);
    }

    private void PersistCurrentSelections()
    {
        if (_activeClass == null) return;
        if (_app.CharGen.ClassMode == "multiclass")
            _app.CharGen.SelectedAbilitiesByClass[_activeClass.Id] = _selectedAbilityEntries.ToList();
        else
            _app.CharGen.SelectedClassAbilityIds = _selectedAbilityEntries.ToList();
    }

    private void AvailableClassAbilityList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnAddClassAbility_Click(sender, new RoutedEventArgs());

    private void SelectedClassAbilityList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnRemoveClassAbility_Click(sender, new RoutedEventArgs());

    private void AvailableClassAbilityList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var listItem = ItemsControl.ContainerFromElement(AvailableClassAbilityList, dep) as ListBoxItem;
            if (listItem?.DataContext is AbilityListItem item)
            {
                ShowAbilityDescription(item);
                e.Handled = true;
            }
        }
    }

    private void SelectedClassAbilityList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var listItem = ItemsControl.ContainerFromElement(SelectedClassAbilityList, dep) as ListBoxItem;
            if (listItem?.DataContext is AbilityListItem item)
            {
                ShowAbilityDescription(item);
                e.Handled = true;
            }
        }
    }

    private void ShowAbilityDescription(AbilityListItem item)
    {
        var text = string.IsNullOrWhiteSpace(item.Description) ? item.Label : item.Description;
        DescriptionPopupService.Show(Window.GetWindow(this), "Class Ability Description", text);
    }

    private string? PromptForClassAbilitySelectionText(string abilityId, string abilityName, string existing)
    {
        if (string.Equals(abilityId, "cleric_spell_like_granted_power", StringComparison.OrdinalIgnoreCase))
            return PromptForSpellLikeGrantedPowerSelection(abilityName, existing);

        if (string.Equals(abilityId, "wizard_signature_spell_levels_1_3", StringComparison.OrdinalIgnoreCase))
            return PromptForWizardSignatureSpellSelection(abilityName, existing, 1, 3);

        if (string.Equals(abilityId, "wizard_signature_spell_levels_4_6", StringComparison.OrdinalIgnoreCase))
            return PromptForWizardSignatureSpellSelection(abilityName, existing, 4, 6);

        if (string.Equals(abilityId, "wizard_signature_spell_levels_7_9", StringComparison.OrdinalIgnoreCase))
            return PromptForWizardSignatureSpellSelection(abilityName, existing, 7, 9);

        if (string.Equals(abilityId, "wizard_immunity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(abilityId, "wizard_no_components_cp5", StringComparison.OrdinalIgnoreCase)
            || string.Equals(abilityId, "wizard_no_components_cp8", StringComparison.OrdinalIgnoreCase))
        {
            return PromptForSpellSelection(
                abilityName,
                existing,
                _app.Rules.Spells
                    .Where(spell => string.Equals(spell.Category, "arcane", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }

        if (string.Equals(abilityId, "wizard_persistent_spell_effect", StringComparison.OrdinalIgnoreCase))
        {
            return PromptForSpellSelection(
                abilityName,
                existing,
                _app.Rules.Spells
                    .Where(spell => string.Equals(spell.Category, "arcane", StringComparison.OrdinalIgnoreCase))
                    .Where(spell => !string.IsNullOrWhiteSpace(spell.Duration)
                        && !spell.Duration.Contains("instant", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }

        if (SelectionOptionsByAbilityId.TryGetValue(abilityId ?? string.Empty, out var options)
            && options.Count > 0)
        {
            if (options.Count == 1 && string.Equals(options[0], "Use spell picker", StringComparison.OrdinalIgnoreCase))
            {
                return PromptForSpellSelection(abilityName, existing, _app.Rules.Spells.OrderBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase).ToList());
            }

            return PromptForClassAbilitySelectionOption(abilityName, options, existing);
        }

        var window = new Window
        {
            Title = $"{abilityName} selection",
            Width = 560,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Enter selection text (for example the exact spell):",
            Margin = new Thickness(0, 0, 0, 8),
        });

        var input = new TextBox
        {
            Text = existing ?? string.Empty,
            AcceptsReturn = false,
            Height = 30,
        };
        panel.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => window.DialogResult = true;
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        window.Content = panel;
        bool? result = window.ShowDialog();
        if (result != true)
            return null;

        return input.Text.Trim();
    }

    private string? PromptForWizardSignatureSpellSelection(string abilityName, string existing, int minLevel, int maxLevel)
    {
        var eligibleSpells = _app.Rules.Spells
            .Where(spell => string.Equals(spell.Category, "arcane", StringComparison.OrdinalIgnoreCase))
            .Where(spell => int.TryParse(spell.Level, out int parsedLevel) && parsedLevel >= minLevel && parsedLevel <= maxLevel)
            .OrderBy(spell => ParseSpellLevelOrZero(spell.Level))
            .ThenBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? picked = PromptForSpellSelection(abilityName, existing, eligibleSpells);
        if (picked is null)
            return null;

        if (!TryGetArcaneSpellLevelByName(picked, out int spellLevel)
            || spellLevel < minLevel
            || spellLevel > maxLevel)
        {
            MessageBox.Show(
                $"Select a valid arcane spell from levels {minLevel}-{maxLevel}.",
                "Selection Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }

        return GetCanonicalArcaneSpellName(picked);
    }

    private bool ValidateWizardSignatureSelection(string abilityId, string selectedSpellName)
    {
        if (!TryGetSignatureLevelRange(abilityId, out int minLevel, out int maxLevel))
            return true;

        if (!TryGetArcaneSpellLevelByName(selectedSpellName, out int selectedLevel)
            || selectedLevel < minLevel
            || selectedLevel > maxLevel)
        {
            MessageBox.Show(
                $"Signature spell selection must be an arcane spell from levels {minLevel}-{maxLevel}.",
                "Selection Restricted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        bool alreadyUsedLevel = _selectedAbilityEntries
            .Where(entry => string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry), abilityId, StringComparison.OrdinalIgnoreCase))
            .Select(entry => RulesEngine.ExtractClassAbilityPlayerText(entry))
            .Select(name => TryGetArcaneSpellLevelByName(name, out int level) ? level : 0)
            .Any(level => level == selectedLevel);

        if (alreadyUsedLevel)
        {
            MessageBox.Show(
                $"A signature spell for level {selectedLevel} is already selected. Choose a spell from a different level.",
                "Selection Restricted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        return true;
    }

    private bool TryGetSignatureLevelRange(string abilityId, out int minLevel, out int maxLevel)
    {
        minLevel = 0;
        maxLevel = 0;

        if (string.Equals(abilityId, "wizard_signature_spell_levels_1_3", StringComparison.OrdinalIgnoreCase))
        {
            minLevel = 1;
            maxLevel = 3;
            return true;
        }

        if (string.Equals(abilityId, "wizard_signature_spell_levels_4_6", StringComparison.OrdinalIgnoreCase))
        {
            minLevel = 4;
            maxLevel = 6;
            return true;
        }

        if (string.Equals(abilityId, "wizard_signature_spell_levels_7_9", StringComparison.OrdinalIgnoreCase))
        {
            minLevel = 7;
            maxLevel = 9;
            return true;
        }

        return false;
    }

    private bool TryGetArcaneSpellLevelByName(string spellName, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(spellName))
            return false;

        var spell = _app.Rules.Spells.FirstOrDefault(s =>
            string.Equals(s.Category, "arcane", StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Name, spellName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (spell is null)
            return false;

        return int.TryParse(spell.Level, out level);
    }

    private string GetCanonicalArcaneSpellName(string spellName)
    {
        var spell = _app.Rules.Spells.FirstOrDefault(s =>
            string.Equals(s.Category, "arcane", StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Name, spellName?.Trim(), StringComparison.OrdinalIgnoreCase));
        return spell?.Name ?? spellName.Trim();
    }

    private static int ParseSpellLevelOrZero(string level)
        => int.TryParse(level, out int parsed) ? parsed : 0;

    private static string? PromptForSpellSelection(string abilityName, string existing, IReadOnlyList<SpellDefinition> spells)
    {
        var options = spells
            .Select(spell => spell.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.Count == 0)
        {
            MessageBox.Show("No eligible spells were found for this selection.", "Selection Unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var window = new Window
        {
            Title = $"{abilityName} selection",
            Width = 640,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Select one spell:",
            Margin = new Thickness(0, 0, 0, 8),
        });

        var picker = new ComboBox
        {
            Height = 30,
            MinWidth = 420,
            ItemsSource = options,
            IsEditable = true,
        };

        string existingValue = (existing ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(existingValue))
            picker.Text = existingValue;
        else
            picker.SelectedIndex = 0;

        panel.Children.Add(picker);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => window.DialogResult = true;
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        window.Content = panel;
        bool? result = window.ShowDialog();
        if (result != true)
            return null;

        return (picker.Text ?? string.Empty).Trim();
    }

    private static string? PromptForSpellLikeGrantedPowerSelection(string abilityName, string existing)
    {
        RulesEngine.TryParseSpellLikeGrantedPowerSelection(existing, out var current);

        var window = new Window
        {
            Title = $"{abilityName} selection",
            Width = 640,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Configure the spell-like granted power.",
            Margin = new Thickness(0, 0, 0, 12),
        });

        panel.Children.Add(new TextBlock { Text = "Spell type", Margin = new Thickness(0, 0, 0, 4) });
        var spellTypePicker = new ComboBox
        {
            Height = 30,
            ItemsSource = new[] { "Priest", "Wizard" },
            SelectedItem = string.Equals(current.SpellType, "wizard", StringComparison.OrdinalIgnoreCase) ? "Wizard" : "Priest",
        };
        panel.Children.Add(spellTypePicker);

        panel.Children.Add(new TextBlock { Text = "Spell name", Margin = new Thickness(0, 10, 0, 4) });
        var spellNameInput = new TextBox
        {
            Text = current.SpellName ?? string.Empty,
            Height = 30,
        };
        panel.Children.Add(spellNameInput);

        panel.Children.Add(new TextBlock { Text = "Spell level", Margin = new Thickness(0, 10, 0, 4) });
        var spellLevelPicker = new ComboBox { Height = 30 };

        void RefreshSpellLevels()
        {
            string type = string.Equals(spellTypePicker.SelectedItem?.ToString(), "Wizard", StringComparison.OrdinalIgnoreCase)
                ? "wizard"
                : "priest";
            int maxLevel = string.Equals(type, "wizard", StringComparison.OrdinalIgnoreCase) ? 9 : 7;
            var levels = Enumerable.Range(1, maxLevel).Select(i => i.ToString()).ToList();
            spellLevelPicker.ItemsSource = levels;

            string preferred = current.SpellLevel > 0 && current.SpellLevel <= maxLevel
                ? current.SpellLevel.ToString()
                : "1";
            spellLevelPicker.SelectedItem = preferred;
        }

        RefreshSpellLevels();
        spellTypePicker.SelectionChanged += (_, _) => RefreshSpellLevels();
        panel.Children.Add(spellLevelPicker);

        panel.Children.Add(new TextBlock { Text = "Usage", Margin = new Thickness(0, 10, 0, 4) });
        var usagePicker = new ComboBox
        {
            Height = 30,
            ItemsSource = new[] { "Once per week", "Per day" },
            SelectedItem = current.IsDaily ? "Per day" : "Once per week",
        };
        panel.Children.Add(usagePicker);

        panel.Children.Add(new TextBlock { Text = "Uses per day", Margin = new Thickness(0, 10, 0, 4) });
        var usesPicker = new ComboBox
        {
            Height = 30,
            ItemsSource = Enumerable.Range(1, 9).Select(i => i.ToString()).ToList(),
            SelectedItem = Math.Max(1, current.UsesPerDay).ToString(),
            IsEnabled = current.IsDaily,
        };
        usagePicker.SelectionChanged += (_, _) =>
        {
            bool isDaily = string.Equals(usagePicker.SelectedItem?.ToString(), "Per day", StringComparison.OrdinalIgnoreCase);
            usesPicker.IsEnabled = isDaily;
            if (!isDaily)
                usesPicker.SelectedItem = "1";
        };
        panel.Children.Add(usesPicker);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => window.DialogResult = true;
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        window.Content = panel;
        if (window.ShowDialog() != true)
            return null;

        string spellName = spellNameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(spellName))
        {
            MessageBox.Show("Enter the exact spell name for this granted power.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        bool isWizard = string.Equals(spellTypePicker.SelectedItem?.ToString(), "Wizard", StringComparison.OrdinalIgnoreCase);
        bool isDaily = string.Equals(usagePicker.SelectedItem?.ToString(), "Per day", StringComparison.OrdinalIgnoreCase);
        int spellLevel = int.TryParse(spellLevelPicker.SelectedItem?.ToString(), out int parsedLevel) ? parsedLevel : 1;
        int usesPerDay = isDaily && int.TryParse(usesPicker.SelectedItem?.ToString(), out int parsedUses) ? parsedUses : 1;

        return RulesEngine.BuildSpellLikeGrantedPowerSelectionText(
            isWizard ? "wizard" : "priest",
            spellLevel,
            isDaily,
            usesPerDay,
            spellName);
    }

    private static string? PromptForClassAbilitySelectionOption(
        string abilityName,
        IReadOnlyList<string> options,
        string existing)
    {
        var window = new Window
        {
            Title = $"{abilityName} selection",
            Width = 560,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Select one option:",
            Margin = new Thickness(0, 0, 0, 8),
        });

        var picker = new ComboBox
        {
            Height = 30,
            MinWidth = 340,
            ItemsSource = options,
        };

        string existingValue = (existing ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(existingValue))
            picker.SelectedItem = options.FirstOrDefault(o => string.Equals(o, existingValue, StringComparison.OrdinalIgnoreCase));
        if (picker.SelectedItem is null && options.Count > 0)
            picker.SelectedIndex = 0;

        panel.Children.Add(picker);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => window.DialogResult = true;
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        window.Content = panel;
        bool? result = window.ShowDialog();
        if (result != true)
            return null;

        return (picker.SelectedItem?.ToString() ?? string.Empty).Trim();
    }

    private void BtnSelectSpheres_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPlayersOptionMode)
            return;

        if (_activeClass is null)
            return;

        bool isCleric = string.Equals(_activeClass.Id, "cleric", StringComparison.OrdinalIgnoreCase);
        bool isPaladinAlternate = ShouldConfigurePaladinSpheres(_activeClass);
        bool isRangerAlternate = ShouldConfigureRangerSpheres(_activeClass);
        if (!isCleric && !isPaladinAlternate && !isRangerAlternate)
            return;

        var dialog = new SphereSelectionDialog(CharGenClassScreen.NormalizeSphereSelections(_app.CharGen.SelectedSpheres));
        if (dialog.ShowDialog() == true)
        {
            var selections = dialog.GetSelections();
            if (isRangerAlternate)
            {
                selections = NormalizeRangerAlternateSpheres(selections);
                if (!IsValidRangerSphereSwap(selections))
                {
                    MessageBox.Show(
                        "Ranger alternate sphere access must keep exactly one default minor sphere (Animal or Plant) and swap the other for one different minor sphere.",
                        "Invalid Sphere Selection",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
            }

            _app.CharGen.SelectedSpheres = selections;
            if (_app.CharGen.SelectedSpheres.Count > 0)
            {
                RemoveSphereAccessAbilitiesFromSelection();
                PersistCurrentSelections();
                RefreshAbilityLists();
            }
        }
        RefreshBudgetSummary(_activeClass);
        RefreshSphereSchoolSummary(_activeClass!);
    }

    private void BtnSelectWizardSchools_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPlayersOptionMode)
            return;

        if (_activeClass?.Id != "wizard") return;
        var dialog = new WizardSchoolsDialog(
            _app.CharGen.SelectedWizardSchools,
            _activeClass.Specializations,
            _app.CharGen.WizardSpecializationId);

        if (dialog.ShowDialog() == true)
        {
            var newSpecId = dialog.GetSpecializationId();
            if (!string.Equals(newSpecId, _app.CharGen.WizardSpecializationId, System.StringComparison.OrdinalIgnoreCase))
            {
                var allSpecIds = _activeClass.Specializations?.SelectMany(s => s.AutoSelectAbilityIds).ToHashSet() ?? new HashSet<string>();
                _selectedAbilityEntries.RemoveAll(entry => allSpecIds.Contains(RulesEngine.ExtractClassAbilityBaseId(entry)));
                _app.CharGen.WizardSpecializationId = newSpecId;
                if (!string.IsNullOrEmpty(newSpecId))
                {
                    var spec = _activeClass.Specializations?.FirstOrDefault(s => string.Equals(s.Id, newSpecId, System.StringComparison.OrdinalIgnoreCase));
                    if (spec != null) foreach (var id in spec.AutoSelectAbilityIds) _selectedAbilityEntries.Add(id);
                }
            }
            _app.CharGen.SelectedWizardSchools = NormalizeWizardSchoolSelections(dialog.GetSelections());
            PersistCurrentSelections();
            RefreshAbilityLists();
            RefreshBudgetSummary(_activeClass);
        }
        RefreshSphereSchoolSummary(_activeClass!);
    }

    private void BtnAllocateRogueSkills_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPlayersOptionMode)
            return;

        if (_activeClass is null || !ClassSupportsRogueSkillConfiguration(_activeClass)) return;

        var selectedSkillIds = GetSelectedRogueSkillIds();
        if (selectedSkillIds.Count == 0)
        {
            MessageBox.Show(
                "Select one or more scored rogue abilities first (for example Pick Pockets or Open Locks).",
                "No Rogue Skills Selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int dex = GetCurrentDexterityScore();
        string raceId = GetCurrentBaseRaceId();
        int level = Math.Max(1, _app.CharGen.CharacterLevel);

        var dialog = new RogueSkillsDialog(
            selectedSkillIds,
            _app.CharGen.SelectedRogueSkillPoints,
            _app.CharGen.RogueSkillArmorProfile,
            raceId,
            dex,
            level)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true) return;

        _app.CharGen.SelectedRogueSkillPoints = dialog.GetAllocations();
        _app.CharGen.RogueSkillArmorProfile = dialog.GetArmorProfile();

        if (_app.CharGen.ClassMode == "multiclass" && _activeClass != null)
        {
            _app.CharGen.RogueSkillPointsByClass[_activeClass.Id] = new(_app.CharGen.SelectedRogueSkillPoints);
            _app.CharGen.RogueSkillArmorByClass[_activeClass.Id] = _app.CharGen.RogueSkillArmorProfile;
        }

        RefreshSphereSchoolSummary(_activeClass!);
    }

    // â”€â”€ Advance â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void Advance()
    {
        if (_activeClass != null) SaveCurrentMultiClassState();

        if (!IsPlayersOptionMode)
        {
            foreach (var cls in _classes)
            {
                var nonAutoIds = cls.StructuredAbilities
                    .Where(a => !a.AutoGranted)
                    .Select(a => a.Id)
                    .Distinct(System.StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (_app.CharGen.ClassMode == "multiclass")
                    _app.CharGen.SelectedAbilitiesByClass[cls.Id] = nonAutoIds;

                if (string.Equals(cls.Id, _app.CharGen.ClassId, System.StringComparison.OrdinalIgnoreCase))
                    _app.CharGen.SelectedClassAbilityIds = nonAutoIds;
            }

            if (_app.CharGen.ClassMode == "multiclass")
                _app.CharGen.SelectedClassAbilityIds = _app.CharGen.SelectedAbilitiesByClass
                    .SelectMany(kvp => kvp.Value)
                    .Distinct(System.StringComparer.OrdinalIgnoreCase)
                    .ToList();

            _app.GoTo("chargen_character_options");
            return;
        }

        var allIssues = new List<string>();
        var configWarnings = new List<string>();
        foreach (var cls in _classes)
        {
            var classAbilityIds = _app.CharGen.ClassMode == "multiclass"
                ? (_app.CharGen.SelectedAbilitiesByClass.TryGetValue(cls.Id, out var selectedForClass) ? selectedForClass : new List<string>())
                : _app.CharGen.SelectedClassAbilityIds;

            var classSpheres = _app.CharGen.ClassMode == "multiclass"
                ? (_app.CharGen.SpheresByClass.TryGetValue(cls.Id, out var spheresForClass) ? spheresForClass : new Dictionary<string, string>())
                : _app.CharGen.SelectedSpheres;

            var classSchools = _app.CharGen.ClassMode == "multiclass"
                ? (_app.CharGen.SchoolsByClass.TryGetValue(cls.Id, out var schoolsForClass) ? schoolsForClass : new Dictionary<string, bool>())
                : _app.CharGen.SelectedWizardSchools;

            if (string.Equals(cls.Id, "cleric", System.StringComparison.OrdinalIgnoreCase)
                && classSpheres.Count == 0)
            {
                configWarnings.Add($"{cls.Name}: no priest spheres selected.");
            }

            bool hasPaladinAlternateSpheres = classAbilityIds.Any(entry =>
                string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry), "paladin_alternate_sphere_access", StringComparison.OrdinalIgnoreCase));
            if (string.Equals(cls.Id, "paladin", System.StringComparison.OrdinalIgnoreCase)
                && hasPaladinAlternateSpheres
                && classSpheres.Count == 0)
            {
                configWarnings.Add($"{cls.Name}: alternate sphere access selected but no spheres configured.");
            }

            bool hasRangerAlternateSpheres = classAbilityIds.Any(entry =>
                string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry), "ranger_alternate_sphere_access", StringComparison.OrdinalIgnoreCase));
            if (string.Equals(cls.Id, "ranger", System.StringComparison.OrdinalIgnoreCase)
                && hasRangerAlternateSpheres
                && !IsValidRangerSphereSwap(NormalizeRangerAlternateSpheres(classSpheres)))
            {
                configWarnings.Add($"{cls.Name}: alternate sphere access must swap exactly one default minor sphere (Animal or Plant).");
            }

            if (string.Equals(cls.Id, "wizard", System.StringComparison.OrdinalIgnoreCase))
            {
                var normalizedSchools = NormalizeWizardSchoolSelections(classSchools);
                if (!normalizedSchools.Any(kv => kv.Value))
                    configWarnings.Add($"{cls.Name}: no wizard schools selected.");
            }

            if (IsRogueClass(cls.Id))
            {
                var autoGranted = cls.StructuredAbilities
                    .Where(a => a.AutoGranted)
                    .Select(a => a.Id);
                var abilityIds = classAbilityIds
                    .Concat(autoGranted)
                    .Distinct(System.StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var rogueSkills = RulesEngine.GetRogueSkillIdsForAbilitySelection(abilityIds);
                if (rogueSkills.Count == 0)
                    configWarnings.Add($"{cls.Name}: no rogue skill abilities selected.");
            }

            if (_app.CharGen.CharacterMode == "players_option")
            {
                var abils = _app.CharGen.ClassMode == "multiclass"
                    ? (_app.CharGen.SelectedAbilitiesByClass.TryGetValue(cls.Id, out var sa) ? sa : new())
                    : _app.CharGen.SelectedClassAbilityIds;
                var wizSpec = _app.CharGen.ClassMode == "multiclass"
                    ? (_app.CharGen.WizardSpecializationById.TryGetValue(cls.Id, out var ws) ? ws : "")
                    : _app.CharGen.WizardSpecializationId;
                var pkg = _app.Rules.BuildClassAbilityPackage(cls.Id, abils, _app.CharGen.RacialCarryoverToClassPoints, wizSpec);
                var spheres = _app.CharGen.ClassMode == "multiclass"
                    ? (_app.CharGen.SpheresByClass.TryGetValue(cls.Id, out var sp) ? sp : new())
                    : _app.CharGen.SelectedSpheres;
                var schools = _app.CharGen.ClassMode == "multiclass"
                    ? (_app.CharGen.SchoolsByClass.TryGetValue(cls.Id, out var sc) ? sc : new())
                    : _app.CharGen.SelectedWizardSchools;
                int configurationCost = CalculateConfigurationCpCost(cls, spheres, schools);
                if (pkg.remaining - configurationCost < 0)
                    allIssues.Add($"{cls.Name}: abilities exceed budget by {-(pkg.remaining - configurationCost)} CP.");
            }
        }

        if (configWarnings.Count > 0)
        {
            var proceed = MessageBox.Show(
                string.Join("\n", configWarnings) + "\n\nProceed anyway?",
                "Missing Class Configuration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (proceed != MessageBoxResult.Yes)
                return;
        }

        if (allIssues.Count > 0)
        {
            var proceed = MessageBox.Show(
                string.Join("\n", allIssues) + "\n\nProceed anyway?",
                "Class Point Budget", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (proceed != MessageBoxResult.Yes) return;
        }

        // Flatten per-class selections into legacy field for subsequent screens
        if (_app.CharGen.ClassMode == "multiclass")
            _app.CharGen.SelectedClassAbilityIds = _app.CharGen.SelectedAbilitiesByClass
                .SelectMany(kvp => kvp.Value).Distinct().ToList();

        if (_app.CharGen.CharacterMode == "players_option")
            _app.GoTo("chargen_subabilities");
        else
            _app.GoTo("chargen_character_options");
    }
}
