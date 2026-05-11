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
    private readonly HashSet<string> _selectedAbilityIds = new(System.StringComparer.OrdinalIgnoreCase);
    private List<AbilityListItem> _availableItems = new();
    private List<AbilityListItem> _selectedItems = new();
    private bool _showDisadvantagesTab;

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
            _app.SetBanner("Character Generator  â€º  Class Abilities");
            bool isPO = _app.CharGen.CharacterMode == "players_option";
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

            SubtitleLabel.Text = isMulti
                ? $"Step 6  Â·  Allocate class ability CP â€” {_classes.Count} classes"
                : "Step 6  Â·  Allocate class ability CP";

            if (isMulti)
                BuildTabButtons();

            // Load the first (or only) class
            var startClass = _activeClass != null && _classes.Any(c => c.Id == _activeClass.Id)
                ? _activeClass
                : _classes[0];
            if (isMulti) LoadMultiClassState(startClass);
            _showDisadvantagesTab = false;
            SelectClass(startClass);
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
        _app.CharGen.SelectedAbilitiesByClass[_activeClass.Id] = _selectedAbilityIds.ToList();
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
        _autoAssignedAbilities = cls.StructuredAbilities
            .Where(a => a.AutoGranted)
            .OrderBy(a => a.PointCost).ThenBy(a => a.Description)
            .ToList();

        var hiddenSpecializationAbilityIds = GetHiddenWizardSpecializationAbilityIds(cls);
        _optionalAbilities = cls.StructuredAbilities
            .Where(a => !a.AutoGranted
                && !IsSelectorManagedAbility(a)
                && !hiddenSpecializationAbilityIds.Contains(a.Id))
            .OrderBy(a => a.PointCost).ThenBy(a => a.Description)
            .ToList();

        _selectedAbilityIds.Clear();
        var optionalIds = new HashSet<string>(_optionalAbilities.Select(a => a.Id), System.StringComparer.OrdinalIgnoreCase);
        if (_app.CharGen.ClassMode == "multiclass")
        {
            if (_app.CharGen.SelectedAbilitiesByClass.TryGetValue(cls.Id, out var saved))
                foreach (var id in saved.Where(optionalIds.Contains)) _selectedAbilityIds.Add(id);
        }
        else
        {
            foreach (var id in _app.CharGen.SelectedClassAbilityIds.Where(optionalIds.Contains))
                _selectedAbilityIds.Add(id);
        }

        UpdateWizardSpecialtyUI(cls);
        UpdateSphereSelectionUI(cls);
        RefreshAbilityLists();
        RefreshSphereSchoolSummary(cls);

        ClassTitle.Text = cls.Name;
        ClassInfo.Text = cls.AbilityMinimums.Count > 0
            ? "Requirements: " + string.Join(", ", cls.AbilityMinimums.Select(kv => $"{RulesEngine.AbilityLabel(kv.Key)} {kv.Value}"))
            : "";
        ClassAbilityHint.Text = _optionalAbilities.Count == 0
            ? "This class has no optional abilities to configure."
            : "Choose optional abilities on the left and move them into your package on the right.";
    }

    internal static bool IsSelectorManagedAbility(AbilityDefinition ability)
    {
        var ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "cleric_sphere_access_minor",
            "cleric_sphere_access_major",
            "cleric_wizardly_priests",
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
        };

        if (ids.Contains(ability.Id))
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
        BtnAllocateRogueSkills.Visibility = IsRogueClass(cls.Id) ? Visibility.Visible : Visibility.Collapsed;
        RefreshClassConfigurationCard(cls);
    }

    private void UpdateSphereSelectionUI(ClassDefinition cls)
    {
        BtnSelectSpheres.Visibility = cls.Id == "cleric" ? Visibility.Visible : Visibility.Collapsed;

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
        if (cls.Id != "cleric" && cls.Id != "druid")
            _app.CharGen.SelectedSpheres.Clear();
        if (cls.Id == "druid")
        {
            ClassConfigTitle.Text = "Priest Configuration";
            ClassConfigHint.Text = "Druid sphere access is fixed and summarized below.";
        }
        if (IsRogueClass(cls.Id))
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
        bool show = cls.Id == "wizard" || cls.Id == "cleric" || cls.Id == "druid" || IsRogueClass(cls.Id);
        ClassConfigCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) ClassConfigHint.Text = string.Empty;
    }

    private void RefreshAbilityLists()
    {
        bool showDisadvantages = _showDisadvantagesTab;

        _availableItems = _optionalAbilities
            .Where(a => !_selectedAbilityIds.Contains(a.Id))
            .Where(a => IsDisadvantageAbility(a) == showDisadvantages)
            .Select(ToAbilityItem)
            .OrderBy(a => a.Label)
            .ToList();

        var selectedOptional = _optionalAbilities
            .Where(a => _selectedAbilityIds.Contains(a.Id))
            .Where(a => IsDisadvantageAbility(a) == showDisadvantages)
            .Select(ToAbilityItem);

        if (showDisadvantages)
        {
            _selectedItems = selectedOptional
                .OrderBy(a => a.Label)
                .ToList();
        }
        else
        {
            _selectedItems = _autoAssignedAbilities.Select(ToAbilityItem)
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
        var wizSpec = _app.CharGen.ClassMode == "multiclass"
            ? (_app.CharGen.WizardSpecializationById.TryGetValue(cls.Id, out var ws) ? ws : "")
            : _app.CharGen.WizardSpecializationId;

        var package = _app.Rules.BuildClassAbilityPackage(
            cls.Id, _selectedAbilityIds.ToList(),
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
        if (cls.Id == "cleric")
        {
            SphereSchoolSection.Visibility = Visibility.Visible;
            SphereSchoolSectionLabel.Text = "PRIEST SPHERE ACCESS";
            var spheres = CharGenClassScreen.NormalizeSphereSelections(_app.CharGen.SelectedSpheres);
            _app.CharGen.SelectedSpheres = new(spheres);
            SphereSchoolSectionSummary.Text = spheres.Count == 0
                ? "No spheres configured â€” click the button to select minor and/or major access."
                : $"{spheres.Count} sphere(s) â€” {CalculateSphereCpCost(spheres)} CP  Â·  {string.Join(", ", spheres.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} ({kv.Value})"))}";
        }
        else if (cls.Id == "druid")
        {
            SphereSchoolSection.Visibility = Visibility.Visible;
            SphereSchoolSectionLabel.Text = "PRIEST SPHERE ACCESS  (Fixed â€” Druid)";
            int fixedCost = CalculateConfigurationCpCost(cls, _app.CharGen.SelectedSpheres, _app.CharGen.SelectedWizardSchools);
            SphereSchoolSectionSummary.Text = $"Auto-assigned: All, Elemental, Healing, Plant, Weather (minor access)  Â·  {fixedCost} CP";
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
        else if (IsRogueClass(cls.Id))
        {
            SphereSchoolSection.Visibility = Visibility.Visible;
            SphereSchoolSectionLabel.Text = "ROGUE SKILL PROFILE";

            var selectedSkillIds = GetSelectedRogueSkillIds();
            if (selectedSkillIds.Count == 0)
            {
                SphereSchoolSectionSummary.Text = "No scored rogue skills selected in class abilities yet.";
                return;
            }

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
            SphereSchoolSectionSummary.Text = $"Armor: {armorLabel}  |  Points: {spent}/{pool} (remaining {remaining})  |  {skillText}";
        }
        else
        {
            SphereSchoolSection.Visibility = Visibility.Collapsed;
        }
    }

    private List<string> GetSelectedRogueSkillIds()
    {
        var selectedIds = _selectedAbilityIds
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
            _selectedAbilityIds.Remove(id);
    }

    private static AbilityListItem ToAbilityItem(AbilityDefinition a)
    {
        var shortName = ToShortAbilityName(a.Description);
        bool isDisadvantage = IsDisadvantageAbility(a);
        string label = isDisadvantage
            ? $"{shortName} ({a.PointCost} CP)  (disadvantage)"
            : $"{shortName} ({a.PointCost} CP)";
        return new AbilityListItem { Id = a.Id, Label = label, Description = a.Description, IsAutoAssigned = a.AutoGranted, IsDisadvantage = isDisadvantage };
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

    // â”€â”€ Button handlers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void BtnAddClassAbility_Click(object sender, RoutedEventArgs e)
    {
        if (_activeClass is null || AvailableClassAbilityList.SelectedItem is not AbilityListItem item) return;
        _selectedAbilityIds.Add(item.Id);
        PersistCurrentSelections();
        RefreshAbilityLists();
        RefreshBudgetSummary(_activeClass);
        RefreshSphereSchoolSummary(_activeClass!);
    }

    private void BtnRemoveClassAbility_Click(object sender, RoutedEventArgs e)
    {
        if (_activeClass is null || SelectedClassAbilityList.SelectedItem is not AbilityListItem item) return;
        if (item.IsAutoAssigned) return;
        _selectedAbilityIds.Remove(item.Id);
        PersistCurrentSelections();
        RefreshAbilityLists();
        RefreshBudgetSummary(_activeClass);
        RefreshSphereSchoolSummary(_activeClass!);
    }

    private void PersistCurrentSelections()
    {
        if (_activeClass == null) return;
        if (_app.CharGen.ClassMode == "multiclass")
            _app.CharGen.SelectedAbilitiesByClass[_activeClass.Id] = _selectedAbilityIds.ToList();
        else
            _app.CharGen.SelectedClassAbilityIds = _selectedAbilityIds.ToList();
    }

    private void AvailableClassAbilityList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnAddClassAbility_Click(sender, new RoutedEventArgs());

    private void SelectedClassAbilityList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnRemoveClassAbility_Click(sender, new RoutedEventArgs());

    private void AvailableClassAbilityList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AvailableClassAbilityList.SelectedItem is AbilityListItem item) ShowAbilityDescription(item);
    }

    private void SelectedClassAbilityList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedClassAbilityList.SelectedItem is AbilityListItem item) ShowAbilityDescription(item);
    }

    private void ShowAbilityDescription(AbilityListItem item)
    {
        var text = string.IsNullOrWhiteSpace(item.Description) ? item.Label : item.Description;
        DescriptionPopupService.Show(Window.GetWindow(this), "Class Ability Description", text);
    }

    private void BtnSelectSpheres_Click(object sender, RoutedEventArgs e)
    {
        if (_activeClass?.Id != "cleric") return;
        var dialog = new SphereSelectionDialog(CharGenClassScreen.NormalizeSphereSelections(_app.CharGen.SelectedSpheres));
        if (dialog.ShowDialog() == true)
        {
            _app.CharGen.SelectedSpheres = dialog.GetSelections();
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
                foreach (var id in allSpecIds) _selectedAbilityIds.Remove(id);
                _app.CharGen.WizardSpecializationId = newSpecId;
                if (!string.IsNullOrEmpty(newSpecId))
                {
                    var spec = _activeClass.Specializations?.FirstOrDefault(s => string.Equals(s.Id, newSpecId, System.StringComparison.OrdinalIgnoreCase));
                    if (spec != null) foreach (var id in spec.AutoSelectAbilityIds) _selectedAbilityIds.Add(id);
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
        if (!IsRogueClass(_activeClass?.Id)) return;

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
