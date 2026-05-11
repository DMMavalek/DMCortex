using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CharGenWeaponProficienciesScreen : UserControl, IScreen
{
    private const string AllGroups = "(All Groups)";
    private const string CombatOptionType = "combat_option";
    private const string WeaponOfChoiceId = "weapon_of_choice";
    private const string WeaponExpertiseId = "weapon_expertise";

    private static readonly (string Id, string Name, string Description)[] CombatOptions =
    {
        ("weapon_mastery", "Weapon Mastery", "Advanced training beyond specialization. This option is intended for high-level campaigns and should be DM-approved."),
        ("armor_proficiency_light", "Armor Proficiency (Light)", "Use light armor without proficiency penalties."),
        ("armor_proficiency_medium", "Armor Proficiency (Medium)", "Use medium armor without proficiency penalties."),
        ("armor_proficiency_heavy", "Armor Proficiency (Heavy)", "Use heavy armor without proficiency penalties."),
        ("shield_proficiency", "Shield Proficiency", "Use shields without proficiency penalties."),
        ("fighting_style_single", "Single-Weapon Style", "When fighting with one one-handed weapon and an empty off-hand, gain style benefits focused on precision and defense."),
        ("fighting_style_two_weapon", "Two-Weapon Style", "Improves dual-wield fighting by reducing off-hand penalties and improving attack flow."),
        ("fighting_style_two_handed", "Two-Handed Style", "Focuses on power strikes with two-handed weapons, trading flexibility for harder hits."),
        ("fighting_style_missile", "Missile Style", "Specialized ranged training that improves accuracy with bows, crossbows, slings, and similar missile weapons."),
        ("fighting_style_sword_shield", "Sword and Shield Style", "Defensive style built around shield use and close-in melee control.")
    };

    private readonly MainWindow _app;
    private CharacterOptionCatalog _catalog = new(
        Array.Empty<NonweaponProficiencyDefinition>(),
        Array.Empty<TraitDefinition>(),
        Array.Empty<DisadvantageDefinition>());

    // View model records for the list boxes
    private record AvailableItem(
        string Id,
        string ProfType,   // "individual", "tight_group", "broad_group"
        string Label,
        string Badge,      // slot cost badge e.g. "1 slot"
        WeaponDefinition? Weapon,
        TightGroupDefinition? TightGroup,
        WeaponGroupDefinition? BroadGroup);

    private record SelectedItem(
        string Id,
        string ProfType,
        string Label,
        string SpecBadge,  // "★ SPEC" if specialized
        string SlotCost);  // e.g. "1 slot" or "3 slots"

    public UIElement View => this;

    public CharGenWeaponProficienciesScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.SetBanner(_app.CharGen.IsLevelUpMode
            ? "Character Section  ›  Level Up  ›  Weapon Proficiencies"
            : "Character Generator  ›  Weapon Proficiencies");
        _catalog = new CharacterOptionCatalogService().GetCatalog();
        bool isPO = _app.CharGen.CharacterMode == "players_option";

        // Step numbering: equipment now sits between weapon proficiencies and review.
        // Core: step 8 of 10; PO no-wizard: step 10 of 12; PO wizard: step 11 of 13
        bool isWizardPO = isPO && string.Equals(_app.CharGen.ClassId, "wizard", StringComparison.OrdinalIgnoreCase);
        bool hasWizardSpecs = _app.Rules.Classes.TryGetValue("wizard", out var wc) && wc.Specializations is { Count: > 0 };
        int stepTotal = isPO
            ? (isWizardPO && hasWizardSpecs ? 13 : 12)
            : 10;
        int stepCurrent = stepTotal - 2;

        _app.SetNavBar(stepCurrent, stepTotal, "Weapon Proficiencies",
            backAction: () => _app.GoTo("chargen_character_options", -1),
            nextAction: Advance);

        PopulateGroupFilter();
        Refresh();
        UpdateDetail(null);
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void Advance()
    {
        _app.GoTo("chargen_equipment");
    }

    // ── Budget helpers ────────────────────────────────────────────────────────

    private bool IsPlayersOptionMode()
        => string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);

    private int GetSlotBudget()
    {
        if (IsPlayersOptionMode())
            return GetWeaponCpBudget() + GetCarryoverFromNwpStage();

        var classIds = GetEffectiveClassIds();
        int level = Math.Max(1, _app.CharGen.CharacterLevel);

        if (classIds.Count == 0)
            return _app.Rules.GetWeaponProficiencySlots("fighter", level);

        return classIds
            .Select(id => _app.Rules.GetWeaponProficiencySlots(id, level))
            .Max();
    }

    private int GetSlotsUsed()
        => IsPlayersOptionMode()
            ? _app.Rules.GetTotalWeaponProficiencyCpUsed(GetEffectiveClassIds(), _app.CharGen.SelectedWeaponProficiencies)
            : RulesEngine.GetTotalWeaponProficiencySlotsUsed(_app.CharGen.SelectedWeaponProficiencies);

    private int GetCharacterLevel() => Math.Max(1, _app.CharGen.CharacterLevel);

    private int GetProficiencyCost(string profType, string proficiencyId, bool specialized = false)
        => IsPlayersOptionMode()
            ? _app.Rules.GetWeaponProficiencyCpCost(GetEffectiveClassIds(), proficiencyId, profType, specialized, GetCharacterLevel())
            : RulesEngine.GetWeaponProficiencySlotCost(profType) + (specialized ? 1 : 0);

    private string FormatBudgetUnits(int amount)
    {
        bool isPO = IsPlayersOptionMode();
        string singular = isPO ? "CP" : "slot";
        string plural = isPO ? "CP" : "slots";
        return amount == 1 ? $"1 {singular}" : $"{amount} {plural}";
    }

    private List<string> GetEffectiveClassIds()
    {
        if (_app.CharGen.SelectedClassIds.Count > 0)
            return _app.CharGen.SelectedClassIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        if (!string.IsNullOrWhiteSpace(_app.CharGen.ClassId))
            return new List<string> { _app.CharGen.ClassId };
        return new List<string>();
    }

    private int GetWeaponCpBudget()
    {
        if (!IsPlayersOptionMode())
            return 0;

        // S&P weapon allotment (warriors=8, priests=8, rogues=6, wizards=3)
        // plus INT bonus for warriors (can also spend INT bonus on weapons).
        var classIds = GetEffectiveClassIds();
        int intScore = _app.CharGen.ModifiedAbilities.TryGetValue("int", out int modInt)
            ? modInt
            : _app.CharGen.Abilities.GetValueOrDefault("int", 10);
        return _app.Rules.GetWeaponCpBudget(classIds, intScore);
    }

    private int GetTotalNwpImprovementCp()
    {
        var selectedDistinct = _app.CharGen.SelectedNonweaponProficiencyIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _app.CharGen.SelectedNonweaponProficiencyImprovements
            .Where(kv => selectedDistinct.Contains(kv.Key))
            .Sum(kv => Math.Max(0, kv.Value));
    }

    private int GetSelectedNwpPurchaseCp()
    {
        if (!IsPlayersOptionMode())
            return 0;

        var proficiencyById = _catalog.NonweaponProficiencies
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int total = 0;
        foreach (string id in _app.CharGen.SelectedNonweaponProficiencyIds)
        {
            if (!proficiencyById.TryGetValue(id, out var proficiency))
                continue;
            total += proficiency.CpCost > 0 ? proficiency.CpCost : Math.Max(1, proficiency.Slots);
        }

        return total;
    }

    private int GetSelectedTraitCost()
    {
        var selectedIds = _app.CharGen.SelectedTraitIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _catalog.Traits
            .Where(x => selectedIds.Contains(x.Id))
            .Sum(x => x.Cost);
    }

    private int GetSelectedDisadvantageBonus()
    {
        return _catalog.Disadvantages
            .Where(x => _app.CharGen.SelectedDisadvantageSeverities.ContainsKey(x.Id))
            .Sum(x =>
            {
                string severity = _app.CharGen.SelectedDisadvantageSeverities[x.Id];
                bool isSevere = string.Equals(severity, "severe", StringComparison.OrdinalIgnoreCase);
                return isSevere && x.SevereBonus.HasValue ? x.SevereBonus.Value : x.ModerateBonus;
            });
    }

    private int GetCharacterOptionsCpBudget()
    {
        if (!IsPlayersOptionMode())
            return 0;

        int baseClassBudget = GetBaseNwpClassBudget();
        int classCarryover = GetClassCarryoverCp();
        return Math.Max(0, baseClassBudget + classCarryover);
    }

    private int GetBaseNwpClassBudget()
    {
        var classIds = GetEffectiveClassIds();
        int intScore = _app.CharGen.ModifiedAbilities.TryGetValue("int", out int modInt)
            ? modInt
            : _app.CharGen.Abilities.GetValueOrDefault("int", 10);

        int fullBudget = _app.Rules.GetNwpCpBudget(classIds, intScore);
        int intBonus = RulesEngine.GetIntBonusCps(intScore);
        return Math.Max(0, fullBudget - intBonus);
    }

    private int GetClassCarryoverCp()
    {
        if (!IsPlayersOptionMode())
            return 0;

        var classIds = GetEffectiveClassIds();
        int totalRemaining = 0;

        foreach (var classId in classIds)
        {
            var selectedAbilityIds = GetSelectedAbilityIdsForClass(classId);
            var specializationId = GetSpecializationIdForClass(classId);
            var classPackage = _app.Rules.BuildClassAbilityPackage(
                classId,
                selectedAbilityIds,
                _app.CharGen.RacialCarryoverToClassPoints,
                specializationId);

            int extraCost = 0;
            if (string.Equals(classId, "cleric", StringComparison.OrdinalIgnoreCase)
                || string.Equals(classId, "druid", StringComparison.OrdinalIgnoreCase))
            {
                extraCost += CharGenClassAbilitiesScreen.CalculateSphereCpCost(GetSpheresForClass(classId));
            }
            if (string.Equals(classId, "wizard", StringComparison.OrdinalIgnoreCase))
            {
                extraCost += CharGenClassAbilitiesScreen.CalculateWizardSchoolCpCost(GetWizardSchoolsForClass(classId));
            }

            totalRemaining += Math.Max(0, classPackage.remaining - extraCost);
        }

        return Math.Max(0, totalRemaining);
    }

    private int GetCarryoverFromNwpStage()
    {
        if (!IsPlayersOptionMode())
            return 0;

        int nwpBudget = GetCharacterOptionsCpBudget();
        int disadvantageBonus = GetSelectedDisadvantageBonus();
        int traitCost = GetSelectedTraitCost();
        int nwpPurchaseCp = GetSelectedNwpPurchaseCp();
        int nwpImprovementCp = GetTotalNwpImprovementCp();

        int remaining = nwpBudget + disadvantageBonus - traitCost - nwpPurchaseCp - nwpImprovementCp;
        return Math.Max(0, remaining);
    }

    private IEnumerable<string> GetSelectedAbilityIdsForClass(string classId)
    {
        if (_app.CharGen.SelectedAbilitiesByClass.TryGetValue(classId, out var ids) && ids.Count > 0)
            return ids;
        return string.Equals(classId, _app.CharGen.ClassId, StringComparison.OrdinalIgnoreCase)
            ? _app.CharGen.SelectedClassAbilityIds
            : Enumerable.Empty<string>();
    }

    private string GetSpecializationIdForClass(string classId)
    {
        if (_app.CharGen.WizardSpecializationById.TryGetValue(classId, out var id) && !string.IsNullOrWhiteSpace(id))
            return id;
        return string.Equals(classId, _app.CharGen.ClassId, StringComparison.OrdinalIgnoreCase)
            ? _app.CharGen.WizardSpecializationId
            : string.Empty;
    }

    private Dictionary<string, string> GetSpheresForClass(string classId)
    {
        if (_app.CharGen.SpheresByClass.TryGetValue(classId, out var spheres) && spheres.Count > 0)
            return new Dictionary<string, string>(spheres);
        return string.Equals(classId, _app.CharGen.ClassId, StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string>(_app.CharGen.SelectedSpheres)
            : new Dictionary<string, string>();
    }

    private Dictionary<string, bool> GetWizardSchoolsForClass(string classId)
    {
        if (_app.CharGen.SchoolsByClass.TryGetValue(classId, out var schools) && schools.Count > 0)
            return new Dictionary<string, bool>(schools);
        return string.Equals(classId, _app.CharGen.ClassId, StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, bool>(_app.CharGen.SelectedWizardSchools)
            : new Dictionary<string, bool>();
    }

    private bool CanSpecialize()
    {
        bool isPO = string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);
        if (isPO)
            return HasPurchasedWeaponSpecializationAbility()
                && _app.Rules.CanSpecializeAtLevel(GetEffectiveClassIds(), GetCharacterLevel());
        return GetEffectiveClassIds()
            .Any(id => _app.Rules.CanSpecializeInWeapons(id, isPO));
    }

    private bool HasPurchasedWeaponSpecializationAbility()
    {
        if (!IsPlayersOptionMode())
            return true;

        foreach (var classId in GetEffectiveClassIds())
        {
            var selected = GetSelectedAbilityIdsForClass(classId);
            if (selected.Any(id => id.EndsWith("_weapon_specialization", StringComparison.OrdinalIgnoreCase)
                                || id.EndsWith("_multiple_specialization", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    // ── UI population ─────────────────────────────────────────────────────────

    private void PopulateGroupFilter()
    {
        var groups = _app.Rules.WeaponGroups.Select(g => g.Name).OrderBy(n => n).ToList();
        groups.Insert(0, AllGroups);
        GroupFilter.ItemsSource = groups;
        GroupFilter.SelectedIndex = 0;
    }

    private void Refresh()
    {
        RefreshAvailable();
        RefreshSelected();
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        bool isPO = IsPlayersOptionMode();
        int used   = GetSlotsUsed();
        int budget = GetSlotBudget();
        bool over  = used > budget;
        int remaining = budget - used;

        BudgetUsedHeaderText.Text = isPO ? "CP USED" : "SLOTS USED";
        RemainingBudgetHeaderText.Text = isPO ? "REMAINING CP" : "SLOTS REMAINING";

        BudgetUsedSummaryText.Text = $"{used} / {budget}";
        BudgetUsedSummaryText.Foreground = over
            ? System.Windows.Media.Brushes.OrangeRed
            : (System.Windows.Media.Brush)FindResource("BrushText");

        RemainingBudgetText.Text = over ? $"Over by {used - budget}" : remaining.ToString();
        RemainingBudgetText.Foreground = over
            ? System.Windows.Media.Brushes.OrangeRed
            : (System.Windows.Media.Brush)FindResource("BrushText");

        CostInfoText.Text = isPO
            ? "Individual weapon: 2/3/4/5/6 CP by class and crossover  |  Tight: 4 CP warriors only  |  Broad: 6 CP warriors only  |  Fighting styles/combat options: 2 CP"
            : "Individual weapon: 1 slot  |  Tight group: 1 slot  |  Broad group: 2 slots";

        SelectedCountText.Text = $"{_app.CharGen.SelectedWeaponProficiencies.Count} proficiencies";

        bool canSpec = CanSpecialize();
        SpecializationInfoText.Text = canSpec
            ? (isPO ? "Extra CP cost varies by class (Table 53)  |  +1 to hit, +2 dmg  |  Extra attacks" : "+1 slot cost  |  +1 to hit, +2 dmg  |  Extra attacks")
            : (isPO && GetEffectiveClassIds().Any(id => _app.Rules.CanSpecializeInWeapons(id, true))
                ? $"Requires higher level to specialize  (min level varies by class)"
                : "Not available for this class");

        BtnSpecialize.IsEnabled = canSpec;
    }

    private void RefreshAvailable()
    {
        // Guard against calls fired during InitializeComponent() before all named controls exist
        if (AvailableList is null) return;

        string search      = WeaponSearchBox?.Text?.Trim() ?? "";
        string groupFilter = GroupFilter?.SelectedItem as string ?? AllGroups;
        string showType    = GetShowType();

        var selectedIds = _app.CharGen.SelectedWeaponProficiencies
            .Select(s => s.ProficiencyId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = new List<AvailableItem>();

        bool showIndividual = showType == "all" || showType == "individual";
        bool showTight      = showType == "all" || showType == "tight";
        bool showBroad      = showType == "all" || showType == "broad";
        bool showTraining   = showType == "all" || showType == "training";
        bool canBuyGroups   = !IsPlayersOptionMode() || _app.Rules.CanBuyWeaponGroups(GetEffectiveClassIds());

        // Broad groups
        if (showBroad && canBuyGroups)
        {
            foreach (var g in _app.Rules.WeaponGroups)
            {
                if (!string.Equals(groupFilter, AllGroups, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(g.Name, groupFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!MatchesSearch(g.Name, search)) continue;
                if (selectedIds.Contains(g.Id)) continue;

                int cost = GetProficiencyCost("broad_group", g.Id);
                if (cost < 0) continue;

                items.Add(new AvailableItem(g.Id, "broad_group",
                    $"[BROAD] {g.Name}", FormatBudgetUnits(cost), null, null, g));
            }
        }

        // Tight groups
        if (showTight && canBuyGroups)
        {
            foreach (var g in _app.Rules.WeaponGroups)
            {
                if (!string.Equals(groupFilter, AllGroups, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(g.Name, groupFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var tg in g.TightGroups)
                {
                    if (!MatchesSearch(tg.Name, search)) continue;
                    if (selectedIds.Contains(tg.Id)) continue;
                    // Skip if the broad group for this tight group is already selected
                    if (selectedIds.Contains(g.Id)) continue;

                    int cost = GetProficiencyCost("tight_group", tg.Id);
                    if (cost < 0) continue;

                    items.Add(new AvailableItem(tg.Id, "tight_group",
                        $"  {tg.Name}", FormatBudgetUnits(cost), null, tg, g));
                }
            }
        }

        // Individual weapons
        if (showIndividual)
        {
            foreach (var w in _app.Rules.Weapons
                .OrderBy(w => GetWeaponSortKey(w.Name), StringComparer.OrdinalIgnoreCase))
            {
                if (!string.Equals(groupFilter, AllGroups, StringComparison.OrdinalIgnoreCase))
                {
                    var grp = _app.Rules.WeaponGroups
                        .FirstOrDefault(g => string.Equals(g.Id, w.GroupId, StringComparison.OrdinalIgnoreCase));
                    if (grp is null || !string.Equals(grp.Name, groupFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                if (!MatchesSearch(w.Name, search)) continue;
                if (selectedIds.Contains(w.Id)) continue;
                // Skip if a tight group or broad group already covers this weapon
                if (IsCoveredBySelection(w, selectedIds)) continue;

                int cost = GetProficiencyCost("individual", w.Id);
                if (cost < 0) continue;

                items.Add(new AvailableItem(w.Id, "individual",
                    $"    {w.Name}", FormatBudgetUnits(cost), w, null, null));
            }
        }

        // Additional combat training picks that share the weapon proficiency budget.
        if (showTraining)
        {
            foreach (var option in CombatOptions.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!MatchesSearch(option.Name, search)) continue;
                if (selectedIds.Contains(option.Id)) continue;

                int cost = GetProficiencyCost(CombatOptionType, option.Id);
                if (cost < 0) continue;

                items.Add(new AvailableItem(option.Id, CombatOptionType,
                    $"    {option.Name}", FormatBudgetUnits(cost), null, null, null));
            }
        }

        var currentId = (AvailableList.SelectedItem as AvailableItem)?.Id;
        AvailableList.ItemsSource = items;
        if (!string.IsNullOrWhiteSpace(currentId))
            AvailableList.SelectedItem = items.FirstOrDefault(x => x.Id == currentId);
        if (AvailableList.SelectedItem is null && items.Count > 0)
            AvailableList.SelectedIndex = 0;
    }

    private void RefreshSelected()
    {
        var items = _app.CharGen.SelectedWeaponProficiencies
            .Select(sel =>
            {
                int cost = GetProficiencyCost(sel.ProficiencyType, sel.ProficiencyId, sel.Specialized);
                string costLabel = FormatBudgetUnits(cost);
                string specBadge = BuildSelectionBadge(sel);
                return new SelectedItem(sel.ProficiencyId, sel.ProficiencyType,
                    sel.DisplayName, specBadge, costLabel);
            })
            .OrderBy(x => GetWeaponSortKey(x.Label), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var currentId = (SelectedList.SelectedItem as SelectedItem)?.Id;
        SelectedList.ItemsSource = items;
        if (!string.IsNullOrWhiteSpace(currentId))
            SelectedList.SelectedItem = items.FirstOrDefault(x => x.Id == currentId);
        if (SelectedList.SelectedItem is null && items.Count > 0)
            SelectedList.SelectedIndex = 0;
    }

    private bool IsCoveredBySelection(WeaponDefinition weapon, HashSet<string> selectedIds)
    {
        // Covered by broad group?
        if (!string.IsNullOrWhiteSpace(weapon.GroupId) && selectedIds.Contains(weapon.GroupId))
            return true;
        // Covered by tight group?
        if (!string.IsNullOrWhiteSpace(weapon.TightGroupId) && selectedIds.Contains(weapon.TightGroupId))
            return true;
        return false;
    }

    private static bool MatchesSearch(string name, string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        return name.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private string GetShowType()
    {
        if (ShowTraining?.IsChecked  == true) return "training";
        if (ShowIndividual?.IsChecked == true) return "individual";
        if (ShowTight?.IsChecked      == true) return "tight";
        if (ShowBroad?.IsChecked      == true) return "broad";
        return "all";
    }

    private static string BuildSelectionBadge(WeaponProficiencySelection selection)
    {
        var parts = new List<string>();
        if (selection.Specialized) parts.Add("SPEC");
        if (selection.WeaponOfChoice) parts.Add("CHOICE");
        if (selection.WeaponExpertise) parts.Add("EXPERT");
        return parts.Count > 0 ? $"★ {string.Join("/", parts)}" : string.Empty;
    }

    private int GetPerWeaponOptionCost(string optionId)
        => Math.Max(0, GetProficiencyCost(CombatOptionType, optionId));

    private bool TryGetSelectedIndividualWeapon(out WeaponProficiencySelection selection)
    {
        selection = null!;
        if (SelectedList.SelectedItem is not SelectedItem selectedItem)
            return false;

        selection = _app.CharGen.SelectedWeaponProficiencies
            .FirstOrDefault(s => string.Equals(s.ProficiencyId, selectedItem.Id, StringComparison.OrdinalIgnoreCase))!;

        return selection is not null && string.Equals(selection.ProficiencyType, "individual", StringComparison.OrdinalIgnoreCase);
    }

    private void TogglePerWeaponOption(string optionId, Func<WeaponProficiencySelection, bool> getFlag, Action<WeaponProficiencySelection, bool> setFlag, string title)
    {
        if (!IsPlayersOptionMode())
        {
            MessageBox.Show(
                $"{title} is available in Players Option mode.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!TryGetSelectedIndividualWeapon(out var selection))
        {
            MessageBox.Show(
                "Select an individual weapon in the Selected Proficiencies list first.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (IsLockedWeapon(selection.ProficiencyId))
        {
            MessageBox.Show(
                "This weapon proficiency was locked from the previous level-up and cannot be changed this cycle.",
                "Locked Selection",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        bool currentlyEnabled = getFlag(selection);
        if (!currentlyEnabled)
        {
            int budget = GetSlotBudget();
            int used = GetSlotsUsed();
            int addCost = GetPerWeaponOptionCost(optionId);
            if (used + addCost > budget)
            {
                string budgetType = IsPlayersOptionMode() ? "Weapon Proficiency CP" : "Weapon Proficiency Slots";
                MessageBox.Show(
                    $"Adding {title} to {selection.DisplayName} would exceed your {budgetType.ToLowerInvariant()} budget.\n\nUsed: {used}/{budget}\nAdditional cost: {FormatBudgetUnits(addCost)}",
                    budgetType,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        setFlag(selection, !currentlyEnabled);
        Refresh();
    }

    // ── Detail panel ──────────────────────────────────────────────────────────

    private void UpdateDetail(AvailableItem? item)
    {
        if (item is null)
        {
            DetailTitle.Text = "Select a weapon or group";
            DetailType.Text  = "";
            DetailNotes.Text = "";
            WeaponStatsPanel.Visibility   = Visibility.Collapsed;
            GroupMembersPanel.Visibility  = Visibility.Collapsed;
            SpecializationStatsPanel.Visibility = CanSpecialize()
                ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        switch (item.ProfType)
        {
            case CombatOptionType:
                ShowCombatOptionDetail(item.Id);
                break;
            case "individual" when item.Weapon is not null:
                ShowWeaponDetail(item.Weapon);
                break;
            case "tight_group" when item.TightGroup is not null:
                ShowTightGroupDetail(item.TightGroup, item.BroadGroup);
                break;
            case "broad_group" when item.BroadGroup is not null:
                ShowBroadGroupDetail(item.BroadGroup);
                break;
            default:
                DetailTitle.Text = item.Label.Trim();
                DetailType.Text  = item.ProfType;
                WeaponStatsPanel.Visibility  = Visibility.Collapsed;
                GroupMembersPanel.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void UpdateDetailFromSelected(SelectedItem? item)
    {
        if (item is null) return;

        switch (item.ProfType)
        {
            case CombatOptionType:
                ShowCombatOptionDetail(item.Id);
                break;
            case "individual":
                if (_app.Rules.WeaponById.TryGetValue(item.Id, out var w))
                    ShowWeaponDetail(w);
                break;
            case "tight_group":
                if (_app.Rules.TightGroups.TryGetValue(item.Id, out var tg))
                {
                    var bg = _app.Rules.WeaponGroups
                        .FirstOrDefault(g => g.TightGroups.Any(t => t.Id == item.Id));
                    ShowTightGroupDetail(tg, bg);
                }
                break;
            case "broad_group":
                var broadGroup = _app.Rules.WeaponGroups
                    .FirstOrDefault(g => string.Equals(g.Id, item.Id, StringComparison.OrdinalIgnoreCase));
                if (broadGroup is not null)
                    ShowBroadGroupDetail(broadGroup);
                break;
        }
    }

    private void ShowWeaponDetail(WeaponDefinition w)
    {
        DetailTitle.Text = w.Name;
        var grp = _app.Rules.WeaponGroups
            .FirstOrDefault(g => string.Equals(g.Id, w.GroupId, StringComparison.OrdinalIgnoreCase));
        var tg  = _app.Rules.TightGroups.TryGetValue(w.TightGroupId, out var t) ? t : null;

        DetailType.Text = grp is null
            ? "Individual Weapon"
            : tg is null
                ? $"Individual Weapon  |  Group: {grp.Name}"
                : $"Individual Weapon  |  {grp.Name} › {tg.Name}";

        WeaponStatsPanel.Visibility = Visibility.Visible;
        StatDamageSm.Text = w.DamageSm;
        StatDamageL.Text  = w.DamageL;
        StatSpeed.Text    = w.Speed.ToString();
        StatType.Text     = ExpandTypeCode(w.Type);
        StatSize.Text     = ExpandSizeCode(w.Size);
        StatApr.Text      = FormatApr(w.AttacksPerRound);

        DetailNotes.Text = w.Notes;
        GroupMembersPanel.Visibility = Visibility.Collapsed;
        SpecializationStatsPanel.Visibility = CanSpecialize() ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowTightGroupDetail(TightGroupDefinition tg, WeaponGroupDefinition? bg)
    {
        DetailTitle.Text = $"Tight Group: {tg.Name}";
        string unit = FormatBudgetUnits(GetProficiencyCost("tight_group", tg.Id));
        DetailType.Text  = bg is null ? $"Tight Weapon Group  ({unit})" : $"Tight Group in {bg.Name}  ({unit})";

        WeaponStatsPanel.Visibility = Visibility.Collapsed;
        SpecializationStatsPanel.Visibility = Visibility.Collapsed;
        DetailNotes.Text = $"Covers {tg.WeaponIds.Count} weapons. Proficiency grants familiarity with each weapon listed.";

        var members = tg.WeaponIds
            .Select(id => _app.Rules.WeaponById.TryGetValue(id, out var w)
                ? $"{w.Name}  (Spd {w.Speed}, {w.DamageSm}/{w.DamageL}, {ExpandTypeCode(w.Type)})"
                : id)
            .ToList();
        GroupMembersList.ItemsSource = members;
        GroupMembersPanel.Visibility = Visibility.Visible;
    }

    private void ShowBroadGroupDetail(WeaponGroupDefinition g)
    {
        DetailTitle.Text = $"Broad Group: {g.Name}";
        DetailType.Text  = $"Broad Weapon Group  ({FormatBudgetUnits(GetProficiencyCost("broad_group", g.Id))})";

        WeaponStatsPanel.Visibility = Visibility.Collapsed;
        SpecializationStatsPanel.Visibility = Visibility.Collapsed;

        int totalWeapons = g.TightGroups.SelectMany(tg => tg.WeaponIds).Distinct().Count()
                           + _app.Rules.Weapons.Count(w => string.Equals(w.GroupId, g.Id, StringComparison.OrdinalIgnoreCase)
                               && string.IsNullOrWhiteSpace(w.TightGroupId));
        DetailNotes.Text = $"{g.Description}\n\nCovers {g.TightGroups.Count} tight groups and all individual weapons in the family.";

        var members = g.TightGroups.Select(tg => $"• {tg.Name}  ({tg.WeaponIds.Count} weapons)").ToList();
        GroupMembersList.ItemsSource = members;
        GroupMembersPanel.Visibility = Visibility.Visible;
    }

    private void ShowCombatOptionDetail(string optionId)
    {
        var option = CombatOptions.FirstOrDefault(x => string.Equals(x.Id, optionId, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(option.Id))
        {
            DetailTitle.Text = optionId;
            DetailType.Text = $"Combat Option  ({FormatBudgetUnits(GetProficiencyCost(CombatOptionType, optionId))})";
            DetailNotes.Text = "Additional combat training option.";
        }
        else
        {
            DetailTitle.Text = option.Name;
            DetailType.Text = $"Combat Option  ({FormatBudgetUnits(GetProficiencyCost(CombatOptionType, option.Id))})";
            DetailNotes.Text = option.Description;
        }

        WeaponStatsPanel.Visibility = Visibility.Collapsed;
        GroupMembersPanel.Visibility = Visibility.Collapsed;
        SpecializationStatsPanel.Visibility = Visibility.Collapsed;
    }

    private static string ExpandTypeCode(string code) => code switch
    {
        "S"   => "S (Slashing)",
        "P"   => "P (Piercing)",
        "B"   => "B (Bludgeoning)",
        "S/P" => "S/P",
        "P/B" => "P/B",
        "B/P" => "B/P",
        _     => code,
    };

    private static string ExpandSizeCode(string code) => code switch
    {
        "S" => "S (Small)",
        "M" => "M (Medium)",
        "L" => "L (Large)",
        _   => code,
    };

    private static string FormatApr(string apr) => apr switch
    {
        "1/2" => "1/2  (1 per 2 rounds)",
        "3/2" => "3/2  (3 per 2 rounds)",
        "2"   => "2 per round",
        "3"   => "3 per round",
        _     => $"{apr} per round",
    };

    // ── Add / Remove / Specialize ─────────────────────────────────────────────

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableList.SelectedItem is not AvailableItem item) return;

        var existing = _app.CharGen.SelectedWeaponProficiencies
            .FirstOrDefault(s => string.Equals(s.ProficiencyId, item.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return; // already added

        int addCost = GetProficiencyCost(item.ProfType, item.Id);
        if (addCost < 0)
        {
            MessageBox.Show(
                "This proficiency is not available to the selected class under Players Option weapon rules.",
                "Weapon Proficiency",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        int budget = GetSlotBudget();
        int used = GetSlotsUsed();
        if (used + addCost > budget)
        {
            string budgetType = IsPlayersOptionMode() ? "Weapon Proficiency CP" : "Weapon Proficiency Slots";
            MessageBox.Show(
                $"Adding {BuildDisplayName(item)} would exceed your {budgetType.ToLowerInvariant()} budget.\n\nUsed: {used}/{budget}\nCost: {FormatBudgetUnits(addCost)}",
                budgetType,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var sel = new WeaponProficiencySelection
        {
            ProficiencyId   = item.Id,
            ProficiencyType = item.ProfType,
            DisplayName     = BuildDisplayName(item),
            Specialized     = false,
        };
        _app.CharGen.SelectedWeaponProficiencies.Add(sel);
        Refresh();

        // Select the newly added item in the right list
        var addedItem = SelectedList.Items.Cast<SelectedItem>()
            .FirstOrDefault(x => x.Id == item.Id);
        if (addedItem is not null) SelectedList.SelectedItem = addedItem;
    }

    private void BtnSpecialize_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedList.SelectedItem is not SelectedItem selItem)
        {
            MessageBox.Show(
                "Select an item in the Selected Proficiencies list, then click Specialize.",
                "Weapon Specialization",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (selItem.ProfType != "individual")
        {
            MessageBox.Show(
                "Only individual weapon proficiencies can be specialized.",
                "Weapon Specialization",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (IsLockedWeapon(selItem.Id))
        {
            MessageBox.Show(
                "This weapon proficiency was locked from the previous level-up and cannot be changed this cycle.",
                "Locked Selection",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var match = _app.CharGen.SelectedWeaponProficiencies
            .FirstOrDefault(s => string.Equals(s.ProficiencyId, selItem.Id, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            int budget = GetSlotBudget();
            int used = GetSlotsUsed();
            int specializedCost = GetProficiencyCost(match.ProficiencyType, match.ProficiencyId, specialized: true);
            int currentCost = GetProficiencyCost(match.ProficiencyType, match.ProficiencyId, specialized: false);
            int additionalCost = specializedCost - currentCost;
            if (specializedCost < 0)
            {
                MessageBox.Show(
                    "Specialization is not available for this class or weapon under Players Option rules.",
                    "Weapon Specialization",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!match.Specialized && used + additionalCost > budget)
            {
                string budgetType = IsPlayersOptionMode() ? "Weapon Proficiency CP" : "Weapon Proficiency Slots";
                MessageBox.Show(
                    $"Adding specialization to {match.DisplayName} would exceed your {budgetType.ToLowerInvariant()} budget.\n\nUsed: {used}/{budget}\nAdditional cost: {FormatBudgetUnits(additionalCost)}",
                    budgetType,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            match.Specialized = !match.Specialized;
            Refresh();
        }
    }

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedList.SelectedItem is not SelectedItem selItem) return;

        if (IsLockedWeapon(selItem.Id))
        {
            MessageBox.Show(
                "This weapon proficiency was locked from the previous level-up and cannot be removed this cycle.",
                "Locked Selection",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var match = _app.CharGen.SelectedWeaponProficiencies
            .FirstOrDefault(s => string.Equals(s.ProficiencyId, selItem.Id, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            _app.CharGen.SelectedWeaponProficiencies.Remove(match);
            Refresh();
        }
    }

    private static string BuildDisplayName(AvailableItem item) => item.ProfType switch
    {
        "broad_group" => $"[Broad] {item.BroadGroup?.Name ?? item.Id}",
        "tight_group" => $"[Tight] {item.TightGroup?.Name ?? item.Id}",
        CombatOptionType => CombatOptions.FirstOrDefault(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase)).Name is string n && !string.IsNullOrWhiteSpace(n)
            ? n
            : item.Id,
        _             => GetWeaponSortKey(item.Weapon?.Name ?? item.Id),
    };

    private static string GetWeaponSortKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        string trimmed = name.Trim();
        if (trimmed.StartsWith("Sword, ", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        string lower = trimmed.ToLowerInvariant();
        if (lower.EndsWith(" sword"))
        {
            string prefix = trimmed[..^6].Trim();
            if (!string.IsNullOrWhiteSpace(prefix))
                return $"Sword, {prefix}";
        }
        return trimmed;
    }

    private bool IsLockedWeapon(string proficiencyId)
        => _app.CharGen.IsLevelUpMode
            && _app.CharGen.LockedWeaponProficiencyIds.Any(id => string.Equals(id, proficiencyId, StringComparison.OrdinalIgnoreCase));

    private void BtnWeaponChoice_Click(object sender, RoutedEventArgs e)
    {
        TogglePerWeaponOption(
            WeaponOfChoiceId,
            s => s.WeaponOfChoice,
            (s, enabled) => s.WeaponOfChoice = enabled,
            "Weapon of Choice");
    }

    private void BtnWeaponExpertise_Click(object sender, RoutedEventArgs e)
    {
        TogglePerWeaponOption(
            WeaponExpertiseId,
            s => s.WeaponExpertise,
            (s, enabled) => s.WeaponExpertise = enabled,
            "Weapon Expertise");
    }

    private void WeaponOptionButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button)
            return;

        string title = button == BtnWeaponChoice
            ? "Weapon of Choice"
            : button == BtnWeaponExpertise
                ? "Weapon Expertise"
                : "Weapon Option";
        string description = button.ToolTip as string ?? string.Empty;
        if (string.IsNullOrWhiteSpace(description))
            return;

        DescriptionPopupService.Show(Window.GetWindow(this), title, description);
        e.Handled = true;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void WeaponSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshAvailable();

    private void GroupFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RefreshAvailable();

    private void ShowType_Changed(object sender, RoutedEventArgs e)
        => RefreshAvailable();

    private void AvailableList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = AvailableList.SelectedItem as AvailableItem;
        UpdateDetail(item);

        bool isIndividual = item?.ProfType == "individual";
        BtnAdd.IsEnabled        = item is not null;
        BtnSpecialize.IsEnabled = CanSpecialize() && isIndividual;
        BtnWeaponChoice.IsEnabled = false;
        BtnWeaponExpertise.IsEnabled = false;
    }

    private void SelectedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = SelectedList.SelectedItem as SelectedItem;
        BtnRemove.IsEnabled = item is not null;

        if (item is not null)
        {
            UpdateDetailFromSelected(item);
            bool isIndividual = item.ProfType == "individual";
            BtnSpecialize.IsEnabled = CanSpecialize() && isIndividual;
            BtnWeaponChoice.IsEnabled = IsPlayersOptionMode() && isIndividual;
            BtnWeaponExpertise.IsEnabled = IsPlayersOptionMode() && isIndividual;
        }
        else
        {
            BtnWeaponChoice.IsEnabled = false;
            BtnWeaponExpertise.IsEnabled = false;
        }
    }

    private void AvailableList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnAdd_Click(sender, new RoutedEventArgs());

    private void SelectedList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnRemove_Click(sender, new RoutedEventArgs());
}
