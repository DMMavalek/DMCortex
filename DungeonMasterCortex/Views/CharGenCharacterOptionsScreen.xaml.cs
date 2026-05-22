using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CharGenCharacterOptionsScreen : UserControl, IScreen
{
    private const string FreeSpokenLanguageSource = "free_spoken";
    private const string ModernLanguagesSource = "modern_languages";
    private const string AncientLanguagesSource = "ancient_languages";
    private const string ReadingWritingSource = "reading_writing";

    private static readonly Dictionary<string, string[]> AbilityFamilyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Strength"] = new[] { "str_stamina", "str_muscle" },
        ["Dexterity"] = new[] { "dex_aim", "dex_balance" },
        ["Constitution"] = new[] { "con_health", "con_fitness" },
        ["Intelligence"] = new[] { "int_reason", "int_knowledge" },
        ["Wisdom"] = new[] { "wis_intuition", "wis_willpower", "wis_perception" },
        ["Charisma"] = new[] { "cha_leadership", "cha_appearance" },
    };

    private static readonly Dictionary<string, string> LanguageSourceLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        [FreeSpokenLanguageSource] = "Free Spoken Language",
        [ModernLanguagesSource] = "Modern Languages",
        [AncientLanguagesSource] = "Ancient Languages",
        [ReadingWritingSource] = "Reading/Writing",
    };

    private readonly MainWindow _app;
    private CharacterOptionCatalog _catalog = new(
        Array.Empty<NonweaponProficiencyDefinition>(),
        Array.Empty<TraitDefinition>(),
        Array.Empty<DisadvantageDefinition>());

    private bool IsPlayersOptionMode
        => string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);

    public UIElement View => this;

    public CharGenCharacterOptionsScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.SetBanner(_app.CharGen.IsLevelUpMode
            ? "Character Section  ›  Level Up  ›  Character Options"
            : "Character Blueprint  ›  Character Options");
        bool isPO = _app.CharGen.CharacterMode == "players_option";
        _app.SetNavBar(isPO ? 9 : 6, isPO ? 11 : 8, "Character Options",
            backAction: () => _app.GoTo(isPO ? "chargen_subabilities" : "chargen_class", -1),
            nextAction: Advance);

        EnsureSubAbilitiesSeeded();

        if (UpdateQuickJumpBar != null)
            UpdateQuickJumpBar.Visibility = Visibility.Collapsed;

        _catalog = _app.CharacterOptions.GetCatalog();
        if (!_app.CharGen.IsLevelUpMode)
            SeedKitNwps();

        if (OverallCpPanel != null)
            OverallCpPanel.Visibility = IsPlayersOptionMode ? Visibility.Visible : Visibility.Collapsed;
        if (NwpAdjustmentButtons != null)
            NwpAdjustmentButtons.Visibility = IsPlayersOptionMode ? Visibility.Visible : Visibility.Collapsed;

        RefreshAll();
    }

    private void Advance()
    {
        if (!ValidateLanguageSelections(out string message))
        {
            MessageBox.Show(message, "Languages", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _app.GoTo("chargen_weapon_prof");
    }

    private bool ConfirmSensitiveJump(string typeName)
    {
        if (!_app.CharGen.IsLevelUpMode)
            return true;

        var result = MessageBox.Show(
            $"⚠  You are opening {typeName} while updating this character.\n\nDid your DM say this was OK?",
            $"{typeName} — DM Approval Check",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    private void BtnJumpRace_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSensitiveJump("Race"))
            return;
        _app.GoTo("chargen_race", -1);
    }

    private void BtnJumpClass_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSensitiveJump("Class"))
            return;
        _app.GoTo("chargen_class", -1);
    }

    private void BtnJumpClassAbilities_Click(object sender, RoutedEventArgs e)
        => _app.GoTo("chargen_class_abilities", -1);

    private void BtnJumpOptions_Click(object sender, RoutedEventArgs e)
        => _app.GoTo("chargen_character_options");

    private void BtnJumpWeaponProf_Click(object sender, RoutedEventArgs e)
        => _app.GoTo("chargen_weapon_prof");

    private void BtnJumpEquipment_Click(object sender, RoutedEventArgs e)
        => _app.GoTo("chargen_equipment");

    private void BtnJumpReview_Click(object sender, RoutedEventArgs e)
        => _app.GoTo("chargen_review");

    private void EnsureSubAbilitiesSeeded()
    {
        if (!string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
            return;

        var defaults = new Dictionary<string, string[]>
        {
            ["str"] = new[] { "str_stamina", "str_muscle" },
            ["dex"] = new[] { "dex_aim", "dex_balance" },
            ["con"] = new[] { "con_health", "con_fitness" },
            ["int"] = new[] { "int_reason", "int_knowledge" },
            ["wis"] = new[] { "wis_intuition", "wis_willpower", "wis_perception" },
            ["cha"] = new[] { "cha_leadership", "cha_appearance" },
        };

        foreach (var (abilityKey, subKeys) in defaults)
        {
            int baseScore = _app.CharGen.ModifiedAbilities.GetValueOrDefault(
                abilityKey,
                _app.CharGen.Abilities.GetValueOrDefault(abilityKey, 10));

            foreach (string sub in subKeys)
            {
                if (!_app.CharGen.SubAbilities.ContainsKey(sub))
                    _app.CharGen.SubAbilities[sub] = baseScore;
            }
        }
    }

    private void RefreshAll()
    {
        RefreshNonweaponLists();
        RefreshTraitLists();
        RefreshDisadvantageLists();
        RefreshLanguageLists();
        RefreshOverallSummary();
    }

    private void RefreshOverallSummary()
    {
        var selectedNwps = GetSelectedNonweaponProficiencies();
        var selectedTraits = GetSelectedTraits();
        var selectedDisadvantages = GetSelectedDisadvantages();
        int selectedLanguages = _app.CharGen.SelectedLanguages.Count;
        int languageSlots = GetLanguageAllowanceBySource().Values.Sum();

        int nwpSlots = selectedNwps.Sum(GetEffectiveNwpSlotCost);
        int nwpPurchaseCp = GetSelectedNwpPurchaseCp();
        int nwpSlotBudget = GetNwpSlotBudget();
        int nwpImprovementCp = GetTotalNwpImprovementCp();
        int traitCost = selectedTraits.Sum(x => x.Cost);
        int disadvantageBonus = selectedDisadvantages.Sum(x => x.Bonus);
        bool hasOptionsCpBudget = string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);
        int cpBudget = hasOptionsCpBudget ? GetCharacterOptionsCpBudget() : 0;
        int cpSpent = traitCost + nwpPurchaseCp + nwpImprovementCp;
        int cpRemaining = hasOptionsCpBudget ? cpBudget + disadvantageBonus - cpSpent : 0;

        OverallNwpSummary.Text = hasOptionsCpBudget
            ? $"{selectedNwps.Count} selected • {nwpPurchaseCp} CP purchases • {nwpImprovementCp} CP improvements"
            : $"{selectedNwps.Count} selected • {nwpSlots}/{nwpSlotBudget} slots";
        OverallTraitSummary.Text = hasOptionsCpBudget
            ? $"{selectedTraits.Count} selected • {traitCost} CP"
            : $"{selectedTraits.Count} selected • {traitCost} CP";
        OverallDisadvantageSummary.Text = hasOptionsCpBudget
            ? $"{selectedDisadvantages.Count} selected • +{disadvantageBonus} CP • options budget {cpBudget}"
            : $"{selectedDisadvantages.Count} selected • +{disadvantageBonus} CP";
        OverallLanguageSummary.Text = $"{selectedLanguages}/{languageSlots} recorded";
        OverallCpSummary.Text = hasOptionsCpBudget ? cpRemaining.ToString() : "Core mode";
    }

    private void RefreshLanguageLists()
    {
        if (LanguageSourceCombo == null || LanguageNameTextBox == null || SpokenLanguageList == null || LiterateLanguageList == null)
            return;

        int? selectedIndex = GetSelectedLanguageIndex();
        var allowances = GetLanguageAllowanceBySource();
        var spokenItems = _app.CharGen.SelectedLanguages
            .Select((selection, index) => new { selection, index })
            .Where(x => IsSpokenLanguageSource(x.selection.SourceKey))
            .Select(x => new LanguageListItem(x.index, BuildLanguageLabel(x.selection), BuildLanguageDescription(x.selection, x.index)))
            .ToList();
        var literateItems = _app.CharGen.SelectedLanguages
            .Select((selection, index) => new { selection, index })
            .Where(x => !IsSpokenLanguageSource(x.selection.SourceKey))
            .Select(x => new LanguageListItem(x.index, BuildLanguageLabel(x.selection), BuildLanguageDescription(x.selection, x.index)))
            .ToList();

        SpokenLanguageList.ItemsSource = spokenItems;
        LiterateLanguageList.ItemsSource = literateItems;
        SelectLanguageListItem(selectedIndex);

        int used = _app.CharGen.SelectedLanguages.Count;
        int available = allowances.Values.Sum();
        int remaining = Math.Max(0, available - used);
        bool overLimit = used > available;
        LanguageSummaryText.Text = overLimit
            ? $"Recorded {used} language entries but only {available} slot{(available == 1 ? string.Empty : "s")} are available. Remove or reassign languages before continuing."
            : $"Recorded {used} language{(used == 1 ? string.Empty : "s")}. {remaining} slot{(remaining == 1 ? string.Empty : "s")} remain available.";
        LanguageSourceSummaryText.Text = string.Join("  |  ", allowances.Select(kv => $"{LanguageSourceLabels[kv.Key]}: {GetSelectedLanguageCount(kv.Key)}/{kv.Value}"));
        BtnRemoveLanguage.IsEnabled = GetSelectedLanguageIndex().HasValue;
        UpdateLanguageEditorHelp();
    }

    private void RefreshNonweaponLists()
    {
        if (NwpSearchBox == null || NwpGroupFilter == null || NwpSourceFilter == null || AvailableNwpList == null || SelectedNwpList == null)
            return;

        string? currentAvailableId = (AvailableNwpList.SelectedItem as CatalogListItem)?.Id;
        string? currentSelectedId = (SelectedNwpList.SelectedItem as CatalogListItem)?.Id;

        string search = NwpSearchBox.Text.Trim();
        string groupFilter = GetSelectedNwpGroupFilter();
        string sourceFilter = GetSelectedNwpSourceFilter();
        var selectedCounts = GetSelectedNwpCounts();
        var available = _catalog.NonweaponProficiencies
            .Where(x => x.AllowMultiple || !selectedCounts.ContainsKey(x.Id))
            .Where(x => MatchesGroupFilter(x, groupFilter))
            .Where(x => MatchesSourceFilter(x.Source, sourceFilter))
            .Where(x => MatchesSearch(x.Name, x.Description, x.Category, search))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new CatalogListItem(x.Id, BuildNonweaponListLabel(x), BuildNonweaponDetail(x)))
            .ToList();
        var selected = GetSelectedNonweaponProficiencies()
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new CatalogListItem(x.Id, BuildNonweaponListLabel(x), BuildNonweaponDetail(x)))
            .ToList();

        AvailableNwpList.ItemsSource = available;
        SelectedNwpList.ItemsSource = selected;
        if (!string.IsNullOrWhiteSpace(currentAvailableId))
            AvailableNwpList.SelectedItem = available.FirstOrDefault(x => string.Equals(x.Id, currentAvailableId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(currentSelectedId))
            SelectedNwpList.SelectedItem = selected.FirstOrDefault(x => string.Equals(x.Id, currentSelectedId, StringComparison.OrdinalIgnoreCase));

        if (SelectedNwpList.SelectedItem is null && selected.Count > 0)
            SelectedNwpList.SelectedIndex = 0;
        int totalNwpSlots = GetSelectedNonweaponProficiencies().Sum(GetEffectiveNwpSlotCost);
        int totalNwpPurchaseCp = GetSelectedNwpPurchaseCp();
        int maxNwpSlots = GetNwpSlotBudget();
        int totalImprovementCp = GetTotalNwpImprovementCp();
        bool hasOptionsCpBudget = string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);
        int cpRemaining = hasOptionsCpBudget ? GetOptionsCpRemaining() : 0;
        NwpSummaryText.Text = selected.Count == 0
            ? hasOptionsCpBudget
                ? $"No nonweapon proficiencies selected yet. Purchase cost: 0 CP. Options CP remaining: {cpRemaining}."
                : $"No nonweapon proficiencies selected yet. Slots: 0/{maxNwpSlots}."
            : hasOptionsCpBudget
                ? $"Selected {selected.Count} proficiencies costing {totalNwpPurchaseCp} CP plus {totalImprovementCp} CP in check improvements. Options CP remaining: {cpRemaining}."
                : $"Selected {selected.Count} proficiencies totalling {totalNwpSlots}/{maxNwpSlots} slots and {totalImprovementCp} CP in check improvements.";
    }

    private void RefreshTraitLists()
    {
        var selectedIds = new HashSet<string>(_app.CharGen.SelectedTraitIds, StringComparer.OrdinalIgnoreCase);
        var available = _catalog.Traits
            .Where(x => !selectedIds.Contains(x.Id))
            .Select(x => new CatalogListItem(x.Id, $"{x.Name} ({x.Cost} CP)", x.Description))
            .ToList();
        var selected = GetSelectedTraits()
            .Select(x => new CatalogListItem(x.Id, $"{x.Name} ({x.Cost} CP)", x.Description))
            .ToList();

        var currentTraitId = (AvailableTraitList.SelectedItem as CatalogListItem)?.Id;
        AvailableTraitList.ItemsSource = available;
        AvailableTraitList.SelectedItem = available.FirstOrDefault(x => x.Id == currentTraitId) ?? available.FirstOrDefault();

        SelectedTraitList.ItemsSource = selected;
        bool hasOptionsCpBudget = string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);
        int cpRemaining = hasOptionsCpBudget ? GetOptionsCpRemaining() : 0;
        TraitSummaryText.Text = selected.Count == 0
            ? hasOptionsCpBudget
                ? $"No traits selected yet. Options CP remaining: {cpRemaining}."
                : $"No traits selected yet."
            : hasOptionsCpBudget
                ? $"Selected {selected.Count} traits costing {GetSelectedTraits().Sum(x => x.Cost)} CP total. Options CP remaining: {cpRemaining}."
                : $"Selected {selected.Count} traits.";
    }

    private void RefreshDisadvantageLists()
    {
        bool hasOptionsCpBudget = IsPlayersOptionMode;
        var selectedIds = new HashSet<string>(_app.CharGen.SelectedDisadvantageSeverities.Keys, StringComparer.OrdinalIgnoreCase);
        var available = _catalog.Disadvantages
            .Where(x => !selectedIds.Contains(x.Id))
            .Select(x => new CatalogListItem(x.Id, FormatDisadvantageLabel(x), x.Description))
            .ToList();
        var selected = GetSelectedDisadvantages()
            .Select(x => new CatalogListItem(x.Id, $"{x.Name} [{x.SeverityLabel}] (+{x.Bonus} CP)", x.Description))
            .ToList();

        var currentDisadvId = (AvailableDisadvantageList.SelectedItem as CatalogListItem)?.Id;
        AvailableDisadvantageList.ItemsSource = available;
        AvailableDisadvantageList.SelectedItem = available.FirstOrDefault(x => x.Id == currentDisadvId) ?? available.FirstOrDefault();

        SelectedDisadvantageList.ItemsSource = selected;
        DisadvantageSummaryText.Text = selected.Count == 0
            ? "No disadvantages selected yet."
            : hasOptionsCpBudget
                ? $"Selected {selected.Count} disadvantages granting +{GetSelectedDisadvantages().Sum(x => x.Bonus)} CP total."
                : $"Selected {selected.Count} disadvantages.";
    }

    private static bool MatchesSearch(params string[] values)
    {
        if (values.Length == 0)
            return true;
        string search = values[^1];
        if (string.IsNullOrWhiteSpace(search))
            return true;
        return values.Take(values.Length - 1)
            .Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private List<NonweaponProficiencyDefinition> GetSelectedNonweaponProficiencies()
    {
        var lookup = _catalog.NonweaponProficiencies
            .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);

        var selected = new List<NonweaponProficiencyDefinition>();
        foreach (string id in _app.CharGen.SelectedNonweaponProficiencyIds)
        {
            if (lookup.TryGetValue(id, out var def))
                selected.Add(def);
        }
        return selected;
    }

    private Dictionary<string, int> GetSelectedNwpCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in _app.CharGen.SelectedNonweaponProficiencyIds)
            counts[id] = counts.GetValueOrDefault(id) + 1;
        return counts;
    }

    private int GetTotalNwpImprovementCp()
    {
        if (!IsPlayersOptionMode)
            return 0;

        var selectedDistinct = _app.CharGen.SelectedNonweaponProficiencyIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _app.CharGen.SelectedNonweaponProficiencyImprovements
            .Where(kv => selectedDistinct.Contains(kv.Key))
            .Sum(kv => Math.Max(0, kv.Value));
    }

    private int GetSelectedNwpPurchaseCp()
    {
        if (!string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
            return 0;

        return GetSelectedNonweaponProficiencies().Sum(GetNwpPurchaseCost);
    }

    private int GetNwpPurchaseCost(NonweaponProficiencyDefinition proficiency)
    {
        if (IsKitFreeNwp(proficiency.Id)) return 0;
        if (string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
            && proficiency.CpCost > 0)
        {
            return proficiency.CpCost + (HasNwpCrossoverPenalty(proficiency) ? 1 : 0);
        }

        return GetEffectiveNwpSlotCost(proficiency);
    }

    private int GetOptionsCpRemaining()
    {
        if (!string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
            return 0;

        int cpBudget = GetCharacterOptionsCpBudget();
        int disadvantageBonus = GetSelectedDisadvantages().Sum(x => x.Bonus);
        int traitCost = GetSelectedTraits().Sum(x => x.Cost);
        int nwpPurchaseCp = GetSelectedNwpPurchaseCp();
        int nwpImprovementCp = GetTotalNwpImprovementCp();
        // Weapons are a separate CP pool; NWP budget only tracks traits/NWPs/disadvantages.
        return cpBudget + disadvantageBonus - traitCost - nwpPurchaseCp - nwpImprovementCp;
    }

    private int GetNwpImprovementCp(string proficiencyId)
        => !IsPlayersOptionMode
            ? 0
            : _app.CharGen.SelectedNonweaponProficiencyImprovements.TryGetValue(proficiencyId, out int allocatedCp)
                ? Math.Max(0, allocatedCp)
                : 0;

    private int GetNwpImprovement(string proficiencyId)
    {
        int allocatedCp = GetNwpImprovementCp(proficiencyId);
        return allocatedCp;
    }

    private int GetCharacterOptionsCpBudget()
    {
        if (!string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
            return 0;

        // NWP stage budget = base class NWP CP (6/8 style bucket) + class-stage carryover.
        // Racial carryover remains capped at 5 earlier in subrace selection; class carryover is uncapped.
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
        if (!string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
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

    private static List<string> ExpandClassIdsForNwpEligibility(IEnumerable<string> classIds)
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in classIds)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            string id = raw.Trim();
            expanded.Add(id);

            if (string.Equals(id, "warrior", StringComparison.OrdinalIgnoreCase))
            {
                expanded.Add("fighter");
                expanded.Add("paladin");
                expanded.Add("ranger");
                continue;
            }
            if (string.Equals(id, "priest", StringComparison.OrdinalIgnoreCase))
            {
                expanded.Add("cleric");
                expanded.Add("druid");
                continue;
            }
            if (string.Equals(id, "rogue", StringComparison.OrdinalIgnoreCase))
            {
                expanded.Add("thief");
                expanded.Add("bard");
                continue;
            }
            if (string.Equals(id, "mage", StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "wizard", StringComparison.OrdinalIgnoreCase))
            {
                expanded.Add("wizard");
                expanded.Add("illusionist");
                continue;
            }
        }

        return expanded.ToList();
    }

    private int GetEffectiveNwpSlotCost(NonweaponProficiencyDefinition proficiency)
    {
        if (IsKitFreeNwp(proficiency.Id)) return 0;
        int baseCost = Math.Max(1, proficiency.Slots);
        return baseCost + (HasNwpCrossoverPenalty(proficiency) ? 1 : 0);
    }

    private bool HasNwpCrossoverPenalty(NonweaponProficiencyDefinition proficiency)
    {
        bool classRestricted = proficiency.AllowedClasses is { Count: > 0 }
            && proficiency.AllowedClasses.Any(x => !string.IsNullOrWhiteSpace(x));
        bool raceRestricted = TryGetNwpRaceRestrictions(proficiency, out var raceRestrictions);

        bool classPenalty;
        if (classRestricted)
        {
            classPenalty = !MatchesAllowedClassSelection(proficiency.AllowedClasses!);
        }
        else
        {
            var nwpGroups = GetNwpGroups(proficiency);
            if (nwpGroups.Count == 0 || nwpGroups.Contains("general"))
                classPenalty = false;
            else
            {
                var classGroups = GetSelectedClassNwpGroups();
                classPenalty = classGroups.Count > 0 && !nwpGroups.Overlaps(classGroups);
            }
        }

        bool racePenalty = raceRestricted && !GetSelectedRaceNwpTokens().Overlaps(raceRestrictions);
        return classPenalty || racePenalty;
    }

    private bool MatchesAllowedClassSelection(IReadOnlyList<string> allowedClasses)
    {
        var selectedExactClasses = GetSelectedExactClassTokens();
        var selectedClassGroups = GetSelectedClassNwpGroups();

        foreach (string rawClass in allowedClasses)
        {
            string normalizedClass = NormalizeGroupToken(rawClass);
            if (string.IsNullOrWhiteSpace(normalizedClass))
                continue;

            if (normalizedClass is "all" or "any" or "general")
                return true;

            if (selectedExactClasses.Contains(normalizedClass))
                return true;

            if (selectedClassGroups.Contains(normalizedClass))
                return true;

            if (TryGetClassFamilyToken(normalizedClass, out string familyToken)
                && selectedClassGroups.Contains(familyToken))
            {
                return true;
            }
        }

        return false;
    }

    private HashSet<string> GetSelectedExactClassTokens()
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string classId in GetEffectiveClassIds())
        {
            string normalized = NormalizeGroupToken(classId);
            if (!string.IsNullOrWhiteSpace(normalized))
                tokens.Add(normalized);
        }

        return tokens;
    }

    private HashSet<string> GetSelectedRaceNwpTokens()
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRaceToken(string? raw)
        {
            string normalized = NormalizeRaceToken(raw);
            if (!string.IsNullOrWhiteSpace(normalized))
                tokens.Add(normalized);
        }

        AddRaceToken(_app.CharGen.RaceId);
        AddRaceToken(_app.CharGen.BaseRaceId);
        if (_app.Rules.Races.TryGetValue(_app.CharGen.RaceId, out var race))
            AddRaceToken(race.BaseRaceId);

        return tokens;
    }

    private static bool TryGetNwpRaceRestrictions(NonweaponProficiencyDefinition proficiency, out HashSet<string> raceRestrictions)
    {
        raceRestrictions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string haystack = string.Join(" ",
            proficiency.Name,
            proficiency.Category,
            proficiency.ProficiencyGroup,
            proficiency.GroupsFound,
            proficiency.Description,
            proficiency.DescriptionPreview);
        string normalized = NormalizeCompact(haystack);

        AddRaceRestriction(raceRestrictions, normalized, "halfelf", "half elf", "halfelven");
        AddRaceRestriction(raceRestrictions, normalized, "halforc", "half orc");
        AddRaceRestriction(raceRestrictions, normalized, "halfling", "halflings");
        AddRaceRestriction(raceRestrictions, normalized, "dwarf", "dwarven", "dwarves");
        AddRaceRestriction(raceRestrictions, normalized, "gnome", "gnomish", "gnomes");
        AddRaceRestriction(raceRestrictions, normalized, "elf", "elves", "elven");
        AddRaceRestriction(raceRestrictions, normalized, "human", "humans");

        return raceRestrictions.Count > 0;
    }

    private static void AddRaceRestriction(HashSet<string> restrictions, string normalizedHaystack, string token, params string[] aliases)
    {
        if (normalizedHaystack.Contains(token, StringComparison.OrdinalIgnoreCase)
            || aliases.Any(alias => normalizedHaystack.Contains(NormalizeCompact(alias), StringComparison.OrdinalIgnoreCase)))
        {
            restrictions.Add(token);
        }
    }

    private HashSet<string> GetNwpGroups(NonweaponProficiencyDefinition proficiency)
    {
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddFromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            foreach (var raw in text.Split(new[] { ',', ';', '/', '|', '&' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string normalized = NormalizeGroupToken(raw);
                if (!string.IsNullOrWhiteSpace(normalized))
                    groups.Add(normalized);
            }
        }

        AddFromText(proficiency.Category);
        AddFromText(proficiency.ProficiencyGroup);
        AddFromText(proficiency.GroupsFound);
        AddFromText(proficiency.GroupFamily);

        return groups;
    }

    private static string NormalizeRaceToken(string? value)
    {
        string token = NormalizeCompact(value ?? string.Empty);
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        if (token.Contains("halfelf", StringComparison.OrdinalIgnoreCase)) return "halfelf";
        if (token.Contains("halforc", StringComparison.OrdinalIgnoreCase)) return "halforc";
        if (token.Contains("halfling", StringComparison.OrdinalIgnoreCase)) return "halfling";
        if (token.Contains("dwarf", StringComparison.OrdinalIgnoreCase)) return "dwarf";
        if (token.Contains("gnome", StringComparison.OrdinalIgnoreCase)) return "gnome";
        if (token.Contains("elf", StringComparison.OrdinalIgnoreCase)) return "elf";
        if (token.Contains("human", StringComparison.OrdinalIgnoreCase)) return "human";
        return string.Empty;
    }

    private HashSet<string> GetSelectedClassNwpGroups()
    {
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var classId in GetEffectiveClassIds())
        {
            if (string.IsNullOrWhiteSpace(classId))
                continue;

            string id = classId.Trim().ToLowerInvariant();
            groups.Add(id);

            if (id is "fighter" or "ranger" or "paladin" or "warrior")
                groups.Add("warrior");
            if (id is "cleric" or "druid" or "priest")
                groups.Add("priest");
            if (id is "thief" or "bard" or "rogue")
                groups.Add("rogue");
            if (id is "wizard" or "illusionist" or "mage")
                groups.Add("wizard");
            if (id.Contains("psionic", StringComparison.OrdinalIgnoreCase))
                groups.Add("psionicist");
        }

        return groups;
    }

    private static string NormalizeGroupToken(string raw)
    {
        string token = NormalizeCompact(raw);
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        if (token.Contains("general", StringComparison.OrdinalIgnoreCase)) return "general";
        if (token.Contains("warrior", StringComparison.OrdinalIgnoreCase)) return "warrior";
        if (token.Contains("priest", StringComparison.OrdinalIgnoreCase)) return "priest";
        if (token.Contains("rogue", StringComparison.OrdinalIgnoreCase)) return "rogue";
        if (token.Contains("wizard", StringComparison.OrdinalIgnoreCase) || token.Contains("mage", StringComparison.OrdinalIgnoreCase)) return "wizard";
        if (token.Contains("psionic", StringComparison.OrdinalIgnoreCase)) return "psionicist";

        if (token.Contains("fighter", StringComparison.OrdinalIgnoreCase)) return "fighter";
        if (token.Contains("ranger", StringComparison.OrdinalIgnoreCase)) return "ranger";
        if (token.Contains("paladin", StringComparison.OrdinalIgnoreCase)) return "paladin";
        if (token.Contains("cleric", StringComparison.OrdinalIgnoreCase)) return "cleric";
        if (token.Contains("druid", StringComparison.OrdinalIgnoreCase)) return "druid";
        if (token.Contains("thief", StringComparison.OrdinalIgnoreCase)) return "thief";
        if (token.Contains("bard", StringComparison.OrdinalIgnoreCase)) return "bard";
        if (token.Contains("illusionist", StringComparison.OrdinalIgnoreCase)) return "illusionist";

        return token;
    }

    private static bool TryGetClassFamilyToken(string token, out string family)
    {
        family = token switch
        {
            "fighter" or "paladin" or "ranger" or "warrior" => "warrior",
            "cleric" or "druid" or "priest" => "priest",
            "thief" or "bard" or "rogue" => "rogue",
            "wizard" or "mage" or "illusionist" => "wizard",
            _ => string.Empty,
        };

        return !string.IsNullOrWhiteSpace(family);
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

    private int GetNwpSlotBudget()
    {
        int classSlots = GetBaseNwpSlotsForClass();
        int intBonus = GetIntelligenceBonusNwpSlots();
        return Math.Max(0, classSlots + intBonus);
    }

    private int GetBaseNwpSlotsForClass()
    {
        string primaryClassId = _app.CharGen.SelectedClassIds.FirstOrDefault() ?? _app.CharGen.ClassId;
        if (string.IsNullOrWhiteSpace(primaryClassId))
            return 6;

        if (string.Equals(primaryClassId, "wizard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(primaryClassId, "cleric", StringComparison.OrdinalIgnoreCase)
            || string.Equals(primaryClassId, "druid", StringComparison.OrdinalIgnoreCase))
            return 8;

        if (string.Equals(primaryClassId, "fighter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(primaryClassId, "paladin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(primaryClassId, "ranger", StringComparison.OrdinalIgnoreCase)
            || string.Equals(primaryClassId, "thief", StringComparison.OrdinalIgnoreCase)
            || string.Equals(primaryClassId, "bard", StringComparison.OrdinalIgnoreCase))
            return 6;

        return 6;
    }

    private int GetIntelligenceBonusNwpSlots()
    {
        int score = GetKnowledgeScore();
        return score switch
        {
            <= 15 => 0,
            16 or 17 => 1,
            18 or 19 => 2,
            _ => 3,
        };
    }

    private int GetKnowledgeScore()
    {
        if (_app.CharGen.CharacterMode == "players_option"
            && _app.CharGen.SubAbilities.TryGetValue("int_knowledge", out int knowledge))
        {
            return knowledge;
        }

        return _app.CharGen.ModifiedAbilities.GetValueOrDefault(
            "int",
            _app.CharGen.Abilities.GetValueOrDefault("int", 10));
    }

    private string GetSelectedNwpGroupFilter()
        => (NwpGroupFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Groups";

    private string GetSelectedNwpSourceFilter()
        => (NwpSourceFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Sources";

    private static bool MatchesGroupFilter(NonweaponProficiencyDefinition proficiency, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "All Groups", StringComparison.OrdinalIgnoreCase))
            return true;

        string normalizedFilter = NormalizeGroupToken(filter);
        var filterTokens = GetNwpFilterTokens(proficiency);
        if (filterTokens.Count == 0)
            return string.Equals(normalizedFilter, "general", StringComparison.OrdinalIgnoreCase);

        return filterTokens.Contains(normalizedFilter);
    }

    private static HashSet<string> GetNwpFilterTokens(NonweaponProficiencyDefinition proficiency)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddTokens(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            foreach (string raw in text.Split(new[] { ';', ',', '/', '|', '&' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                AddFilterToken(tokens, NormalizeGroupToken(raw));
        }

        AddTokens(proficiency.Category);
        AddTokens(proficiency.ProficiencyGroup);
        AddTokens(proficiency.GroupsFound);
        AddTokens(proficiency.GroupFamily);

        if (proficiency.AllowedClasses is { Count: > 0 })
        {
            var allowedTokens = proficiency.AllowedClasses
                .Select(NormalizeGroupToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (IsUniversalAllowedClassSet(allowedTokens))
            {
                tokens.Add("general");
            }
            else
            {
                foreach (string allowedToken in allowedTokens)
                    AddFilterToken(tokens, allowedToken);
            }
        }

        return tokens;
    }

    private static bool IsUniversalAllowedClassSet(HashSet<string> allowedTokens)
    {
        if (allowedTokens.Count == 0)
            return false;

        if (allowedTokens.Contains("all") || allowedTokens.Contains("any"))
            return true;

        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string token in allowedTokens)
        {
            if (TryGetClassFamilyToken(token, out string family))
                families.Add(family);
            else if (token is "warrior" or "priest" or "rogue" or "wizard")
                families.Add(token);
        }

        return families.Contains("warrior")
            && families.Contains("priest")
            && families.Contains("rogue")
            && families.Contains("wizard");
    }

    private static void AddFilterToken(HashSet<string> tokens, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        tokens.Add(token);
        if (TryGetClassFamilyToken(token, out string family))
            tokens.Add(family);
    }

    private static bool MatchesAllowedClassFilter(IReadOnlyList<string>? allowedClasses, string filter)
    {
        if (allowedClasses is null || allowedClasses.Count == 0)
            return false;

        string normalizedFilter = NormalizeClassGroupFilter(filter);
        if (string.IsNullOrWhiteSpace(normalizedFilter))
            return false;

        foreach (string rawClass in allowedClasses)
        {
            if (string.IsNullOrWhiteSpace(rawClass))
                continue;

            string normalizedClass = NormalizeClassGroupFilter(rawClass);
            if (string.Equals(normalizedClass, "all", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedClass, "any", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedClass, normalizedFilter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeClassGroupFilter(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().ToLowerInvariant() switch
        {
            "fighter" or "paladin" or "ranger" or "warrior" => "warrior",
            "cleric" or "druid" or "priest" => "priest",
            "thief" or "bard" or "rogue" => "rogue",
            "wizard" or "mage" or "illusionist" => "mage",
            _ => value.Trim().ToLowerInvariant(),
        };
    }

    private static bool MatchesSourceFilter(string source, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || string.Equals(filter, "All Sources", StringComparison.OrdinalIgnoreCase))
            return true;
        // An NWP tagged "Core & Player's Option" appears under either individual filter.
        bool nwpIsDual = string.Equals(source, "Core & Player's Option", StringComparison.OrdinalIgnoreCase);
        if (nwpIsDual)
            return string.Equals(filter, "Core", StringComparison.OrdinalIgnoreCase)
                || string.Equals(filter, "Player's Option", StringComparison.OrdinalIgnoreCase)
                || string.Equals(filter, "Core & Player's Option", StringComparison.OrdinalIgnoreCase);
        return string.Equals(source, filter, StringComparison.OrdinalIgnoreCase);
    }

    private int GetEffectiveNwpFamilyScore(NonweaponProficiencyDefinition proficiency)
    {
        string checkAbility = GetActiveCheckAbility(proficiency);

        if (_app.CharGen.CharacterMode == "players_option"
            && _app.CharGen.SubAbilities.Count > 0
            )
        {
            // Direct sub-ability key (e.g. "str_stamina")
            if (_app.CharGen.SubAbilities.TryGetValue(checkAbility, out int directScore))
                return directScore;

            // Base-ability family: average all sub-abilities in the family
            if (AbilityFamilyMap.TryGetValue(checkAbility, out var subAbilityKeys))
            {
                var scores = subAbilityKeys
                    .Select(key => _app.CharGen.SubAbilities.TryGetValue(key, out int s) ? s : (int?)null)
                    .Where(s => s.HasValue)
                    .Select(s => s!.Value)
                    .ToList();
                if (scores.Count > 0)
                    return (int)Math.Round(scores.Average(), MidpointRounding.AwayFromZero);
            }
        }

        return checkAbility switch
        {
            "Strength" => _app.CharGen.ModifiedAbilities.GetValueOrDefault("str", _app.CharGen.Abilities.GetValueOrDefault("str", 10)),
            "Dexterity" => _app.CharGen.ModifiedAbilities.GetValueOrDefault("dex", _app.CharGen.Abilities.GetValueOrDefault("dex", 10)),
            "Constitution" => _app.CharGen.ModifiedAbilities.GetValueOrDefault("con", _app.CharGen.Abilities.GetValueOrDefault("con", 10)),
            "Intelligence" => _app.CharGen.ModifiedAbilities.GetValueOrDefault("int", _app.CharGen.Abilities.GetValueOrDefault("int", 10)),
            "Wisdom" => _app.CharGen.ModifiedAbilities.GetValueOrDefault("wis", _app.CharGen.Abilities.GetValueOrDefault("wis", 10)),
            "Charisma" => _app.CharGen.ModifiedAbilities.GetValueOrDefault("cha", _app.CharGen.Abilities.GetValueOrDefault("cha", 10)),
            _ => 10,
        };
    }

    private int GetEffectiveNwpCheckTarget(NonweaponProficiencyDefinition proficiency)
    {
        if (string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
        {
            int baseRating = Math.Max(0, proficiency.PlayersOptionBaseRating);
            int improvement = GetNwpImprovement(proficiency.Id);
            int relevantScore = GetRelevantSubAbilityScore(proficiency);
            int poTable44Bonus = GetTable44Modifier(relevantScore);
            return baseRating + poTable44Bonus + improvement;
        }

        int familyScore = GetEffectiveNwpFamilyScore(proficiency);
        int table44Bonus = 0;
        string checkAbility = GetActiveCheckAbility(proficiency);
        if (_app.CharGen.CharacterMode == "players_option" && _app.CharGen.SubAbilities.Count > 0
            && AbilityFamilyMap.TryGetValue(checkAbility, out var subAbilityKeys))
        {
            var scores = subAbilityKeys
                .Select(key => _app.CharGen.SubAbilities.TryGetValue(key, out int s) ? s : (int?)null)
                .Where(s => s.HasValue).Select(s => s!.Value).ToList();
            if (scores.Count > 0)
                table44Bonus = GetTable44Modifier((int)Math.Round(scores.Average(), MidpointRounding.AwayFromZero));
        }
        return familyScore + proficiency.CheckModifier + table44Bonus + GetNwpImprovement(proficiency.Id);
    }

    private string BuildNonweaponDetail(NonweaponProficiencyDefinition proficiency)
    {
        string checkAbility = GetActiveCheckAbility(proficiency);
        string noteSuffix = _app.CharGen.SelectedNonweaponProficiencyNotes.TryGetValue(proficiency.Id, out string? note)
            && !string.IsNullOrWhiteSpace(note)
            ? $"\nPlayer text: {note}"
            : string.Empty;

        if (string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
        {
            int baseRating = Math.Max(0, proficiency.PlayersOptionBaseRating);
            int relevantScore = GetRelevantSubAbilityScore(proficiency);
            int poTable44Bonus = GetTable44Modifier(relevantScore);
            int poImprovement = GetNwpImprovement(proficiency.Id);
            int cpSpent = GetNwpImprovementCp(proficiency.Id);
            int poTarget = baseRating + poTable44Bonus + poImprovement;

            int purchaseCp = GetNwpPurchaseCost(proficiency);
            string crossoverTag = HasNwpCrossoverPenalty(proficiency) ? " (includes crossover penalty)" : string.Empty;
            return $"Group: {proficiency.Category}  |  Source: {proficiency.Source}\n"
                 + (proficiency.CpCost > 0
                     ? $"Cost: {purchaseCp} CP{crossoverTag}  |  1 CP per improvement\n"
                     : $"Cost: {purchaseCp} CP{crossoverTag}\n")
                 + $"Initial rating: {baseRating}\n"
                 + $"Relevant ability: {checkAbility}\n"
                 + $"Check: {baseRating}{FormatSigned(poTable44Bonus)} (Table 44 from {GetSubAbilitySourceLabel(proficiency)} {relevantScore}){(poImprovement > 0 ? $" {FormatSigned(poImprovement)} improvement" : string.Empty)} = {poTarget}\n"
                 + (cpSpent > 0
                     ? $"Improvement CP spent: {cpSpent}\n\n"
                     : "\n")
                 + proficiency.Description
                 + noteSuffix;
        }

        int familyScore = GetEffectiveNwpFamilyScore(proficiency);
        int improvement = GetNwpImprovement(proficiency.Id);
        int target = GetEffectiveNwpCheckTarget(proficiency);
        string familySource = _app.CharGen.CharacterMode == "players_option" && _app.CharGen.SubAbilities.Count > 0
            ? $"{checkAbility} family"
            : checkAbility;

        bool isPO = _app.CharGen.CharacterMode == "players_option" && _app.CharGen.SubAbilities.Count > 0
            && AbilityFamilyMap.ContainsKey(checkAbility);
        int table44Bonus = target - familyScore - proficiency.CheckModifier - improvement;
        string checkLine = isPO
            ? $"Check: {familySource} {familyScore}{FormatSigned(proficiency.CheckModifier)}{(table44Bonus != 0 ? $" {FormatSigned(table44Bonus)} (ability bonus)" : string.Empty)}{(improvement > 0 ? $" {FormatSigned(improvement)} improvement" : string.Empty)} = {target}"
            : $"Check: {familySource} {familyScore}{FormatSigned(proficiency.CheckModifier)}{(improvement > 0 ? $" {FormatSigned(improvement)} improvement" : string.Empty)} = {target}";
        int slotCostCore = GetEffectiveNwpSlotCost(proficiency);
        string crossoverTagCore = HasNwpCrossoverPenalty(proficiency) ? " (includes crossover penalty)" : string.Empty;
           return $"Group: {proficiency.Category}  |  Source: {proficiency.Source}\n"
                         + $"Cost: {slotCostCore} slot{(slotCostCore == 1 ? "" : "s")}{crossoverTagCore}\n"
             + checkLine + "\n\n"
               + proficiency.Description
               + noteSuffix;
    }

    private static int GetTable44Modifier(int abilityScore) => abilityScore switch
    {
        <= 3 => -5,
        4 => -4,
        5 => -3,
        6 => -2,
        7 => -1,
        >= 8 and <= 13 => 0,
        14 => 1,
        15 => 2,
        16 => 3,
        17 => 4,
        _ => 5,
    };

    private List<TraitDefinition> GetSelectedTraits()
    {
        var selectedIds = new HashSet<string>(_app.CharGen.SelectedTraitIds, StringComparer.OrdinalIgnoreCase);
        return _catalog.Traits.Where(x => selectedIds.Contains(x.Id)).OrderBy(x => x.Name).ToList();
    }

    private List<SelectedDisadvantage> GetSelectedDisadvantages()
    {
        return _catalog.Disadvantages
            .Where(x => _app.CharGen.SelectedDisadvantageSeverities.ContainsKey(x.Id))
            .Select(x =>
            {
                string severity = _app.CharGen.SelectedDisadvantageSeverities[x.Id];
                bool isSevere = string.Equals(severity, "severe", StringComparison.OrdinalIgnoreCase);
                int bonus = isSevere && x.SevereBonus.HasValue ? x.SevereBonus.Value : x.ModerateBonus;
                return new SelectedDisadvantage(x.Id, x.Name, x.Description, isSevere ? "Severe" : "Moderate", bonus);
            })
            .OrderBy(x => x.Name)
            .ToList();
    }

    private void NwpSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshNonweaponLists();
    private void NwpGroupFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshNonweaponLists();
    private void NwpSourceFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshNonweaponLists();
    private void AvailableNwpList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void SelectedNwpList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void AvailableNwpList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnAddNwp_Click(sender, new RoutedEventArgs());

    private void SelectedNwpList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnRemoveNwp_Click(sender, new RoutedEventArgs());

    private void AvailableTraitList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private void SelectedTraitList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private void AvailableTraitList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnAddTrait_Click(sender, new RoutedEventArgs());
    private void SelectedTraitList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnRemoveTrait_Click(sender, new RoutedEventArgs());
    private void AvailableDisadvantageList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private void SelectedDisadvantageList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }
    private void AvailableDisadvantageList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnAddDisadvantage_Click(sender, new RoutedEventArgs());
    private void SelectedDisadvantageList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnRemoveDisadvantage_Click(sender, new RoutedEventArgs());
    private void SpokenLanguageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpokenLanguageList.SelectedItem is LanguageListItem item)
        {
            if (LiterateLanguageList.SelectedIndex >= 0)
                LiterateLanguageList.SelectedIndex = -1;
            LoadLanguageSelection(item.Index);
            return;
        }

        if (LiterateLanguageList.SelectedIndex < 0)
            BtnRemoveLanguage.IsEnabled = false;
    }

    private void LiterateLanguageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LiterateLanguageList.SelectedItem is LanguageListItem item)
        {
            if (SpokenLanguageList.SelectedIndex >= 0)
                SpokenLanguageList.SelectedIndex = -1;
            LoadLanguageSelection(item.Index);
            return;
        }

        if (SpokenLanguageList.SelectedIndex < 0)
            BtnRemoveLanguage.IsEnabled = false;
    }

    private void LanguageSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpokenLanguageList is null || LiterateLanguageList is null || LanguageEditorHelpText is null) return;
        UpdateLanguageEditorHelp();
    }

    private void AvailableNwpList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AvailableNwpList.SelectedItem is CatalogListItem item)
            ShowDescription(item, "Nonweapon Proficiency Description");
    }

    private void SelectedNwpList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedNwpList.SelectedItem is CatalogListItem item)
            ShowDescription(item, "Nonweapon Proficiency Description");
    }

    private void AvailableTraitList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AvailableTraitList.SelectedItem is CatalogListItem item)
            ShowDescription(item, "Trait Description");
    }

    private void SelectedTraitList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedTraitList.SelectedItem is CatalogListItem item)
            ShowDescription(item, "Trait Description");
    }

    private void AvailableDisadvantageList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AvailableDisadvantageList.SelectedItem is CatalogListItem item)
            ShowDescription(item, "Disadvantage Description");
    }

    private void SelectedDisadvantageList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedDisadvantageList.SelectedItem is CatalogListItem item)
            ShowDescription(item, "Disadvantage Description");
    }

    private void BtnAddNwp_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableNwpList.SelectedItem is not CatalogListItem item)
            return;

        var selectedNwps = GetSelectedNonweaponProficiencies();
        int usedSlots = selectedNwps.Sum(GetEffectiveNwpSlotCost);
        int maxSlots = GetNwpSlotBudget();
        var toAddCandidate = _catalog.NonweaponProficiencies.FirstOrDefault(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (toAddCandidate is null)
            return;
        var toAdd = toAddCandidate;

        if (!toAdd.AllowMultiple
            && _app.CharGen.SelectedNonweaponProficiencyIds.Any(id => string.Equals(id, toAdd.Id, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(
                $"{toAdd.Name} can only be selected once with current DM settings.",
                "Selection Restricted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int addCost = string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
            ? GetNwpPurchaseCost(toAdd)
            : GetEffectiveNwpSlotCost(toAdd);
        if (string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
        {
            int cpRemaining = GetOptionsCpRemaining();
            if (addCost > cpRemaining)
            {
                MessageBox.Show(
                    $"Adding {toAdd.Name} would exceed your options CP budget.\n\nRemaining: {cpRemaining} CP\nCost: {addCost} CP{(HasNwpCrossoverPenalty(toAdd) ? " (includes crossover penalty)" : string.Empty)}",
                    "Options CP Budget",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }
        else if (usedSlots + addCost > maxSlots)
        {
            MessageBox.Show(
                $"Adding {toAdd.Name} would exceed your NWP slot budget.\n\nUsed: {usedSlots}/{maxSlots} slots\nCost: {addCost} slot{(addCost == 1 ? string.Empty : "s")}{(HasNwpCrossoverPenalty(toAdd) ? " (includes crossover penalty)" : string.Empty)}",
                "NWP Slot Budget",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (toAdd.RequiresPlayerText)
        {
            string existing = _app.CharGen.SelectedNonweaponProficiencyNotes.GetValueOrDefault(toAdd.Id, string.Empty);
            string? note = PromptForNwpPlayerText(toAdd.Name, existing);
            if (note is null)
                return;
            _app.CharGen.SelectedNonweaponProficiencyNotes[toAdd.Id] = note;
        }

        _app.CharGen.SelectedNonweaponProficiencyIds.Add(toAdd.Id);
        _app.CharGen.SelectedNonweaponProficiencyImprovements.TryAdd(toAdd.Id, 0);
        RefreshAll();
    }

    private void BtnRemoveNwp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNwpList.SelectedItem is not CatalogListItem item)
            return;

        if (IsLockedNwp(item.Id))
        {
            MessageBox.Show(
                "This proficiency was locked from the previous level-up and cannot be removed this cycle.",
                "Locked Selection",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (IsKitGrantedNwp(item.Id))
        {
            string tag = IsKitFreeNwp(item.Id) ? "free kit bonus" : "required by kit";
            MessageBox.Show(
                $"This proficiency is a {tag} from your kit and cannot be removed.",
                "Kit Proficiency",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int removeIndex = _app.CharGen.SelectedNonweaponProficiencyIds.FindLastIndex(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (removeIndex >= 0)
            _app.CharGen.SelectedNonweaponProficiencyIds.RemoveAt(removeIndex);

        bool stillSelected = _app.CharGen.SelectedNonweaponProficiencyIds.Any(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (!stillSelected)
        {
            _app.CharGen.SelectedNonweaponProficiencyImprovements.Remove(item.Id);
            _app.CharGen.SelectedNonweaponProficiencyNotes.Remove(item.Id);
        }
        RefreshAll();
    }

    private void BtnIncreaseNwp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNwpList.SelectedItem is not CatalogListItem item)
            return;

        if (!IsPlayersOptionMode)
        {
            MessageBox.Show(
                "Core Rules does not use Character Point improvements for NWPs.",
                "Core Rules",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (IsLockedNwp(item.Id))
        {
            MessageBox.Show(
                "This proficiency was locked from the previous level-up and cannot be modified this cycle.",
                "Locked Selection",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string selectedId = item.Id;

        int cpRemaining = GetOptionsCpRemaining();
        if (cpRemaining <= 0)
        {
            MessageBox.Show(
                "No class CP remain for additional NWP improvements. Add disadvantages or remove CP costs first.",
                "Options CP Budget",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        int currentCp = GetNwpImprovementCp(item.Id);
        int currentImprovement = GetNwpImprovement(item.Id);
        int currentLevel = Math.Max(1, _app.CharGen.CharacterLevel);
        int maxImprovement = 4 + Math.Max(0, currentLevel - 1);
        int nextImprovement = currentCp + 1;
        if (currentImprovement >= maxImprovement || nextImprovement > maxImprovement)
        {
            MessageBox.Show(
                $"{item.Label}\n\nNWP checks can be raised up to {maxImprovement} times at level {currentLevel}.",
                "NWP Improvement Limit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _app.CharGen.SelectedNonweaponProficiencyImprovements[item.Id] = currentCp + 1;
        RefreshAll();

        var reselection = (SelectedNwpList.ItemsSource as List<CatalogListItem>)
            ?.FirstOrDefault(x => string.Equals(x.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        if (reselection is not null)
            SelectedNwpList.SelectedItem = reselection;
    }

    private void BtnDecreaseNwp_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNwpList.SelectedItem is not CatalogListItem item)
            return;

        if (!IsPlayersOptionMode)
        {
            MessageBox.Show(
                "Core Rules does not use Character Point improvements for NWPs.",
                "Core Rules",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (IsLockedNwp(item.Id))
        {
            MessageBox.Show(
                "This proficiency was locked from the previous level-up and cannot be modified this cycle.",
                "Locked Selection",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int currentCp = GetNwpImprovementCp(item.Id);
        if (currentCp <= 0)
            return;

        if (currentCp == 1)
            _app.CharGen.SelectedNonweaponProficiencyImprovements.Remove(item.Id);
        else
            _app.CharGen.SelectedNonweaponProficiencyImprovements[item.Id] = currentCp - 1;
        RefreshAll();
    }

    private void BtnAddTrait_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableTraitList.SelectedItem is not CatalogListItem item)
            return;

        var trait = _catalog.Traits.FirstOrDefault(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (trait is null)
            return;

        if (!string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
        {
            _app.CharGen.SelectedTraitIds.Add(item.Id);
            RefreshAll();
            return;
        }

        int cpRemaining = GetOptionsCpRemaining();

        if (trait.Cost > cpRemaining)
        {
            MessageBox.Show(
                $"Not enough options CP for {trait.Name}.\n\nCost: {trait.Cost} CP\nRemaining: {cpRemaining} CP",
                "Options CP Budget",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _app.CharGen.SelectedTraitIds.Add(item.Id);
        RefreshAll();
    }

    private void BtnRemoveTrait_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTraitList.SelectedItem is not CatalogListItem item)
            return;
        _app.CharGen.SelectedTraitIds.RemoveAll(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase));
        RefreshAll();
    }

    private void BtnAddDisadvantage_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableDisadvantageList.SelectedItem is not CatalogListItem item)
            return;

        var definition = _catalog.Disadvantages.FirstOrDefault(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
            return;

        string severity = (SeverityDropdown.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLowerInvariant() ?? "moderate";

        if (string.Equals(severity, "severe", StringComparison.OrdinalIgnoreCase) && !definition.SevereBonus.HasValue)
        {
            MessageBox.Show($"{definition.Name} only has a moderate rating in the source table.",
                "Severity Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _app.CharGen.SelectedDisadvantageSeverities[item.Id] = severity;
        RefreshAll();
    }

    private void BtnRemoveDisadvantage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDisadvantageList.SelectedItem is not CatalogListItem item)
            return;
        _app.CharGen.SelectedDisadvantageSeverities.Remove(item.Id);
        RefreshAll();
    }

    private void BtnNewLanguage_Click(object sender, RoutedEventArgs e)
    {
        string currentSource = GetSelectedLanguageSourceKey();
        ClearLanguageSelections();
        SelectLanguageSource(currentSource);
        LanguageNameTextBox.Text = string.Empty;
        BtnRemoveLanguage.IsEnabled = false;
        UpdateLanguageEditorHelp();
        LanguageNameTextBox.Focus();
    }

    private void BtnSaveLanguage_Click(object sender, RoutedEventArgs e)
    {
        string languageName = LanguageNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(languageName))
        {
            MessageBox.Show("Enter a language name first.", "Languages", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string sourceKey = GetSelectedLanguageSourceKey();
        int? editIndex = GetSelectedLanguageIndex();
        int remaining = GetRemainingLanguageSlots(sourceKey, editIndex);
        if (remaining <= 0)
        {
            MessageBox.Show(
                $"No {LanguageSourceLabels[sourceKey]} slots remain. Remove an existing entry or increase the matching language proficiency first.",
                "Languages",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var selection = new LanguageSelection { SourceKey = sourceKey, LanguageName = languageName };
        if (editIndex.HasValue)
            _app.CharGen.SelectedLanguages[editIndex.Value] = selection;
        else
            _app.CharGen.SelectedLanguages.Add(selection);

        RefreshAll();
        int targetIndex = editIndex ?? (_app.CharGen.SelectedLanguages.Count - 1);
        SelectLanguageListItem(targetIndex);
    }

    private void BtnRemoveLanguage_Click(object sender, RoutedEventArgs e)
    {
        int? idx = GetSelectedLanguageIndex();
        if (!idx.HasValue || idx.Value < 0 || idx.Value >= _app.CharGen.SelectedLanguages.Count)
            return;

        _app.CharGen.SelectedLanguages.RemoveAt(idx.Value);
        BtnNewLanguage_Click(sender, e);
        RefreshAll();
    }

    private string BuildNonweaponListLabel(NonweaponProficiencyDefinition proficiency)
    {
        string noteSuffix = _app.CharGen.SelectedNonweaponProficiencyNotes.TryGetValue(proficiency.Id, out string? note)
            && !string.IsNullOrWhiteSpace(note)
            ? $" [Text: {note}]"
            : string.Empty;

        string kitTag = IsKitFreeNwp(proficiency.Id) ? " [Kit: Free]"
            : _app.CharGen.KitRequiredNwpIds.Any(id => string.Equals(id, proficiency.Id, StringComparison.OrdinalIgnoreCase)) ? " [Kit: Required]"
            : string.Empty;

        if (string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
        {
            int baseRating = Math.Max(0, proficiency.PlayersOptionBaseRating);
            int relevantScore = GetRelevantSubAbilityScore(proficiency);
            int poTable44Bonus = GetTable44Modifier(relevantScore);
            int poImprovement = GetNwpImprovement(proficiency.Id);
            int cpSpent = GetNwpImprovementCp(proficiency.Id);
            int poTarget = baseRating + poTable44Bonus + poImprovement;
            int poTotalBonus = poTable44Bonus + poImprovement;
            string poBonusBreakdown = poImprovement > 0
                ? $"{FormatSigned(poTable44Bonus)} from {GetSubAbilitySourceLabel(proficiency)}, {FormatSigned(poImprovement)} from CP"
                : $"{FormatSigned(poTable44Bonus)} from {GetSubAbilitySourceLabel(proficiency)}";

            int purchaseCp = GetNwpPurchaseCost(proficiency);
            string crossoverTag = HasNwpCrossoverPenalty(proficiency) ? " + crossover" : string.Empty;

            string costLabel = proficiency.CpCost > 0
                ? $"{purchaseCp} CP{crossoverTag}, 1 CP/point"
                : $"{purchaseCp} CP{crossoverTag}";
            string progressLabel = cpSpent > 0
                ? $", improv {cpSpent} CP"
                : string.Empty;
            return $"{proficiency.Name} ({costLabel}), base {baseRating}, bonus {FormatSigned(poTotalBonus)} ({poBonusBreakdown}), target {poTarget}{progressLabel}{noteSuffix}{kitTag}";
        }

        int baseScore = GetEffectiveNwpFamilyScore(proficiency);
        int improvement = GetNwpImprovement(proficiency.Id);
        int target = GetEffectiveNwpCheckTarget(proficiency);

        int table44Bonus = target - baseScore - proficiency.CheckModifier - improvement;
        int totalBonus = proficiency.CheckModifier + table44Bonus + improvement;

        var bonusParts = new List<string>();
        if (proficiency.CheckModifier != 0)
            bonusParts.Add($"{FormatSigned(proficiency.CheckModifier)} check mod");
        if (table44Bonus != 0)
            bonusParts.Add($"{FormatSigned(table44Bonus)} from {GetSubAbilitySourceLabel(proficiency)}");
        if (improvement > 0)
            bonusParts.Add($"{FormatSigned(improvement)} from CP");

        string bonusBreakdown = bonusParts.Count > 0 ? string.Join(", ", bonusParts) : "no bonus";

        int coreSlotCost = GetEffectiveNwpSlotCost(proficiency);
        string coreCrossoverTag = HasNwpCrossoverPenalty(proficiency) ? " + crossover" : string.Empty;

        string coreCostLabel = $"{coreSlotCost} slot(s){coreCrossoverTag}";
        return $"{proficiency.Name} ({coreCostLabel}), base {baseScore}, bonus {FormatSigned(totalBonus)} ({bonusBreakdown}), target {target}{noteSuffix}{kitTag}";
    }

    private Dictionary<string, int> GetLanguageAllowanceBySource()
    {
        return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [FreeSpokenLanguageSource] = 1,
            [ModernLanguagesSource] = GetLanguageSlotsFromNwp(ModernLanguagesSource),
            [AncientLanguagesSource] = GetLanguageSlotsFromNwp(AncientLanguagesSource),
            [ReadingWritingSource] = GetLanguageSlotsFromNwp(ReadingWritingSource),
        };
    }

    private int GetLanguageSlotsFromNwp(string sourceKey)
    {
        var matching = GetSelectedNonweaponProficiencies()
            .Where(proficiency => IsLanguageNwpForSource(proficiency, sourceKey))
            .ToList();
        if (matching.Count == 0)
            return 0;

        int purchases = matching.Count;
        int improvements = matching
            .Select(proficiency => proficiency.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Sum(id => GetNwpImprovementCp(id));
        return purchases + improvements;
    }

    private static bool IsLanguageNwpForSource(NonweaponProficiencyDefinition proficiency, string sourceKey)
    {
        string idNorm = NormalizeCompact(proficiency.Id);
        string nameNorm = NormalizeCompact(proficiency.Name);

        static bool ContainsAny(string value, params string[] tokens)
            => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

        return sourceKey switch
        {
            ModernLanguagesSource =>
                ContainsAny(idNorm, "modernlanguage", "modernlanguages")
                || ContainsAny(nameNorm, "modernlanguage", "modernlanguages", "languagesmodern"),

            AncientLanguagesSource =>
                ContainsAny(idNorm, "ancientlanguage", "ancientlanguages")
                || ContainsAny(nameNorm, "ancientlanguage", "ancientlanguages", "languagesancient"),

            ReadingWritingSource =>
                ContainsAny(idNorm, "readingwriting", "readwrite", "literacy")
                || ContainsAny(nameNorm, "readingwriting", "readwrite", "literacy"),

            _ => false,
        };
    }

    private int GetSelectedLanguageCount(string sourceKey)
        => _app.CharGen.SelectedLanguages.Count(language => string.Equals(language.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase));

    private int GetRemainingLanguageSlots(string sourceKey, int? excludingIndex = null)
    {
        int allowance = GetLanguageAllowanceBySource().GetValueOrDefault(sourceKey);
        int used = _app.CharGen.SelectedLanguages
            .Where((language, index) => !excludingIndex.HasValue || index != excludingIndex.Value)
            .Count(language => string.Equals(language.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase));
        return allowance - used;
    }

    private bool ValidateLanguageSelections(out string message)
    {
        var allowances = GetLanguageAllowanceBySource();
        foreach (var kv in allowances)
        {
            int used = GetSelectedLanguageCount(kv.Key);
            if (used > kv.Value)
            {
                message = $"Too many languages are assigned to {LanguageSourceLabels[kv.Key]}: {used}/{kv.Value}. Remove or reassign entries in the Languages tab before continuing.";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private string BuildLanguageLabel(LanguageSelection selection)
        => $"{selection.LanguageName} [{LanguageSourceLabels.GetValueOrDefault(selection.SourceKey, selection.SourceKey)}]";

    private string BuildLanguageDescription(LanguageSelection selection, int index)
    {
        int remaining = Math.Max(0, GetRemainingLanguageSlots(selection.SourceKey, index));
        return $"Source: {LanguageSourceLabels.GetValueOrDefault(selection.SourceKey, selection.SourceKey)}\nLanguage: {selection.LanguageName}\nRemaining slots in this source: {remaining}";
    }

    private void UpdateLanguageEditorHelp()
    {
        string sourceKey = GetSelectedLanguageSourceKey();
        int remaining = GetRemainingLanguageSlots(sourceKey, GetSelectedLanguageIndex());
        string modeLabel = IsSpokenLanguageSource(sourceKey) ? "spoken" : "literate";
        LanguageEditorHelpText.Text = $"{LanguageSourceLabels[sourceKey]} has {Math.Max(0, remaining)} slot{(Math.Abs(remaining) == 1 ? string.Empty : "s")} available for this {modeLabel} entry.";
    }

    private string GetSelectedLanguageSourceKey()
        => (LanguageSourceCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? FreeSpokenLanguageSource;

    private void SelectLanguageSource(string sourceKey)
    {
        foreach (var item in LanguageSourceCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), sourceKey, StringComparison.OrdinalIgnoreCase))
            {
                LanguageSourceCombo.SelectedItem = item;
                return;
            }
        }

        LanguageSourceCombo.SelectedIndex = 0;
    }

    private void LoadLanguageSelection(int index)
    {
        if (index < 0 || index >= _app.CharGen.SelectedLanguages.Count)
        {
            BtnRemoveLanguage.IsEnabled = false;
            return;
        }

        var selection = _app.CharGen.SelectedLanguages[index];
        SelectLanguageSource(selection.SourceKey);
        LanguageNameTextBox.Text = selection.LanguageName;
        BtnRemoveLanguage.IsEnabled = true;
        UpdateLanguageEditorHelp();
    }

    private int? GetSelectedLanguageIndex()
    {
        if (SpokenLanguageList.SelectedItem is LanguageListItem spoken)
            return spoken.Index;
        if (LiterateLanguageList.SelectedItem is LanguageListItem literate)
            return literate.Index;
        return null;
    }

    private void ClearLanguageSelections()
    {
        SpokenLanguageList.SelectedIndex = -1;
        LiterateLanguageList.SelectedIndex = -1;
    }

    private void SelectLanguageListItem(int? index)
    {
        ClearLanguageSelections();
        if (!index.HasValue)
            return;

        var spokenItem = SpokenLanguageList.Items.OfType<LanguageListItem>().FirstOrDefault(item => item.Index == index.Value);
        if (spokenItem is not null)
        {
            SpokenLanguageList.SelectedItem = spokenItem;
            return;
        }

        var literateItem = LiterateLanguageList.Items.OfType<LanguageListItem>().FirstOrDefault(item => item.Index == index.Value);
        if (literateItem is not null)
            LiterateLanguageList.SelectedItem = literateItem;
    }

    private static bool IsSpokenLanguageSource(string sourceKey)
        => string.Equals(sourceKey, FreeSpokenLanguageSource, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceKey, ModernLanguagesSource, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCompact(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private string GetSubAbilitySourceLabel(NonweaponProficiencyDefinition proficiency)
    {
        string checkAbility = GetActiveCheckAbility(proficiency);
        var relevantKeys = GetRelevantSubAbilityKeys(checkAbility);
        if (relevantKeys.Count > 0)
        {
            var labels = relevantKeys.Select(PrettySubAbilityName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (labels.Count == 1)
                return labels[0];
            if (labels.Count > 1)
                return string.Join("/", labels);
        }

        if (_app.CharGen.CharacterMode != "players_option"
            || _app.CharGen.SubAbilities.Count == 0
            || !AbilityFamilyMap.TryGetValue(checkAbility, out var subAbilityKeys))
        {
            return checkAbility;
        }

        var presentKeys = subAbilityKeys
            .Where(key => _app.CharGen.SubAbilities.ContainsKey(key))
            .Select(PrettySubAbilityName)
            .ToList();

        if (presentKeys.Count == 0)
            return checkAbility;
        if (presentKeys.Count == 1)
            return presentKeys[0];
        return string.Join("/", presentKeys);
    }

    private static string PrettySubAbilityName(string key)
    {
        int idx = key.IndexOf('_');
        string raw = idx >= 0 && idx < key.Length - 1 ? key[(idx + 1)..] : key;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(raw.Replace('_', ' '));
    }

    private int GetRelevantSubAbilityScore(NonweaponProficiencyDefinition proficiency)
    {
        var keys = GetRelevantSubAbilityKeys(GetActiveCheckAbility(proficiency));
        if (keys.Count == 0)
            return GetEffectiveNwpFamilyScore(proficiency);

        var scores = keys
            .Select(key => _app.CharGen.SubAbilities.TryGetValue(key, out int score) ? (int?)score : null)
            .Where(score => score.HasValue)
            .Select(score => score!.Value)
            .ToList();

        if (scores.Count == 0)
            return GetEffectiveNwpFamilyScore(proficiency);

        return scores.Max();
    }

    private string GetActiveCheckAbility(NonweaponProficiencyDefinition proficiency)
    {
        if (string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(proficiency.PlayersOptionCheckAbility))
        {
            return proficiency.PlayersOptionCheckAbility;
        }

        return proficiency.CheckAbility;
    }

    private static List<string> GetRelevantSubAbilityKeys(string checkAbilityText)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["strength/stamina"] = "str_stamina",
            ["strength/muscle"] = "str_muscle",
            ["dexterity/aim"] = "dex_aim",
            ["dexterity/balance"] = "dex_balance",
            ["constitution/health"] = "con_health",
            ["constitution/fitness"] = "con_fitness",
            ["intelligence/reason"] = "int_reason",
            ["intelligence/knowledge"] = "int_knowledge",
            ["wisdom/intuition"] = "wis_intuition",
            ["wisdom/willpower"] = "wis_willpower",
            ["wisdom/perception"] = "wis_perception",
            ["charisma/leadership"] = "cha_leadership",
            ["charisma/appearance"] = "cha_appearance",
            ["str_stamina"] = "str_stamina",
            ["str_muscle"] = "str_muscle",
            ["dex_aim"] = "dex_aim",
            ["dex_balance"] = "dex_balance",
            ["con_health"] = "con_health",
            ["con_fitness"] = "con_fitness",
            ["int_reason"] = "int_reason",
            ["int_knowledge"] = "int_knowledge",
            ["wis_intuition"] = "wis_intuition",
            ["wis_willpower"] = "wis_willpower",
            ["wis_perception"] = "wis_perception",
            ["cha_leadership"] = "cha_leadership",
            ["cha_appearance"] = "cha_appearance",
            ["str - stamina"] = "str_stamina",
            ["str - muscle"] = "str_muscle",
            ["dex - aim"] = "dex_aim",
            ["dex - balance"] = "dex_balance",
            ["con - health"] = "con_health",
            ["con - fitness"] = "con_fitness",
            ["int - reason"] = "int_reason",
            ["int - knowledge"] = "int_knowledge",
            ["wis - intuition"] = "wis_intuition",
            ["wis - willpower"] = "wis_willpower",
            ["wis - perception"] = "wis_perception",
            ["cha - leadership"] = "cha_leadership",
            ["cha - appearance"] = "cha_appearance",
        };

        if (string.IsNullOrWhiteSpace(checkAbilityText))
            return new List<string>();

        return checkAbilityText
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim().Replace('—', '-').Replace('–', '-'))
            .Select(part => map.TryGetValue(part, out var key) ? key : null)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatDisadvantageLabel(DisadvantageDefinition disadvantage)
    {
        return disadvantage.SevereBonus.HasValue
            ? $"{disadvantage.Name} (Moderate +{disadvantage.ModerateBonus} CP, Severe +{disadvantage.SevereBonus.Value} CP)"
            : $"{disadvantage.Name} (Moderate +{disadvantage.ModerateBonus} CP)";
    }

    private static string? GetSelectedDescription(ListBox listBox)
        => (listBox.SelectedItem as CatalogListItem)?.Description;

    private void ShowDescription(CatalogListItem item, string title)
    {
        string text = string.IsNullOrWhiteSpace(item.Description) ? item.Label : item.Description;
        DescriptionPopupService.Show(Window.GetWindow(this), title, text);
    }

    private static string FormatSigned(int value)
        => value >= 0 ? $"+{value.ToString(CultureInfo.InvariantCulture)}" : value.ToString(CultureInfo.InvariantCulture);

    private bool IsLockedNwp(string proficiencyId)
        => _app.CharGen.IsLevelUpMode
            && _app.CharGen.LockedNonweaponProficiencyIds.Any(id => string.Equals(id, proficiencyId, StringComparison.OrdinalIgnoreCase));

    private bool IsKitFreeNwp(string proficiencyId)
        => _app.CharGen.KitFreeNwpIds.Any(id => string.Equals(id, proficiencyId, StringComparison.OrdinalIgnoreCase));

    private bool IsKitGrantedNwp(string proficiencyId)
        => _app.CharGen.KitFreeNwpIds.Any(id => string.Equals(id, proficiencyId, StringComparison.OrdinalIgnoreCase))
        || _app.CharGen.KitRequiredNwpIds.Any(id => string.Equals(id, proficiencyId, StringComparison.OrdinalIgnoreCase));

    private void SeedKitNwps()
    {
        if (string.IsNullOrEmpty(_app.CharGen.KitId)) return;
        var kit = _app.Rules.Kits.FirstOrDefault(k => k.Id == _app.CharGen.KitId);
        if (kit is null) return;

        var lookup = _catalog.NonweaponProficiencies
            .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);

        // Remove any previously seeded kit NWPs (in case kit changed)
        foreach (string id in _app.CharGen.KitFreeNwpIds.Concat(_app.CharGen.KitRequiredNwpIds))
        {
            _app.CharGen.SelectedNonweaponProficiencyIds.RemoveAll(sid =>
                string.Equals(sid, id, StringComparison.OrdinalIgnoreCase));
            _app.CharGen.SelectedNonweaponProficiencyImprovements.Remove(id);
        }
        _app.CharGen.KitFreeNwpIds = new();
        _app.CharGen.KitRequiredNwpIds = new();

        // Seed free NWPs (no cost)
        foreach (string id in kit.FreeNwpIds)
        {
            if (!lookup.ContainsKey(id)) continue; // NWP not in catalog, skip
            if (!_app.CharGen.SelectedNonweaponProficiencyIds
                    .Any(sid => string.Equals(sid, id, StringComparison.OrdinalIgnoreCase)))
            {
                _app.CharGen.SelectedNonweaponProficiencyIds.Add(id);
                _app.CharGen.SelectedNonweaponProficiencyImprovements.TryAdd(id, 0);
            }
            _app.CharGen.KitFreeNwpIds.Add(id);
        }

        // Seed required NWPs (pay normal cost, must take)
        foreach (string id in kit.RequiredNwpIds)
        {
            if (!lookup.ContainsKey(id)) continue;
            if (!_app.CharGen.SelectedNonweaponProficiencyIds
                    .Any(sid => string.Equals(sid, id, StringComparison.OrdinalIgnoreCase)))
            {
                _app.CharGen.SelectedNonweaponProficiencyIds.Add(id);
                _app.CharGen.SelectedNonweaponProficiencyImprovements.TryAdd(id, 0);
            }
            _app.CharGen.KitRequiredNwpIds.Add(id);
        }
    }

    private static string? PromptForNwpPlayerText(string nwpName, string existing)
    {
        var window = new Window
        {
            Title = $"{nwpName} details",
            Width = 520,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Enter the player-facing text for this proficiency:",
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

    private sealed record CatalogListItem(string Id, string Label, string Description);

    private sealed record LanguageListItem(int Index, string Label, string Description);

    private sealed record SelectedDisadvantage(string Id, string Name, string Description, string SeverityLabel, int Bonus);
}