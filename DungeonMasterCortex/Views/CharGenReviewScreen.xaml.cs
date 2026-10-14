using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CharGenReviewScreen : UserControl, IScreen
{
    private readonly MainWindow _app;

    private static bool HasSelectedClassAbility(CharGenState cg, string abilityId)
    {
        if (string.IsNullOrWhiteSpace(abilityId))
            return false;

        if (cg.SelectedClassAbilityIds
            .Select(RulesEngine.ExtractClassAbilityBaseId)
            .Any(id => string.Equals(id, abilityId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var byClass in cg.SelectedAbilitiesByClass.Values)
        {
            if (byClass
                .Select(RulesEngine.ExtractClassAbilityBaseId)
                .Any(id => string.Equals(id, abilityId, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private int GetEffectiveWeaponCpUsedForState(CharGenState cg, IEnumerable<string> classIds, IEnumerable<WeaponProficiencySelection> selections)
    {
        bool hasProficiencyEase = HasSelectedClassAbility(cg, "fighter_proficiency_ease");

        int ApplyEase(int value)
            => !hasProficiencyEase || value <= 0
                ? value
                : Math.Max(1, (value + 1) / 2);

        var selList = (selections ?? Array.Empty<WeaponProficiencySelection>()).ToList();
        int total = 0;
        foreach (var selection in selList)
        {
            bool covered = string.Equals(selection.ProficiencyType, "individual", StringComparison.OrdinalIgnoreCase)
                && _app.Rules.IsWeaponCoveredByGroupSelections(selection.ProficiencyId, selList);

            int cost;
            if (covered)
            {
                cost = selection.Specialized ? _app.Rules.GetWeaponSpecializationOnlyCpCost(classIds) : 0;
            }
            else
            {
                cost = _app.Rules.GetWeaponProficiencyCpCost(classIds, selection.ProficiencyId, selection.ProficiencyType, selection.Specialized);
                if (cost < 0)
                    cost = RulesEngine.GetWeaponProficiencySlotCost(selection.ProficiencyType, selection.ProficiencyId) + (selection.Specialized ? 1 : 0);
            }

            cost = ApplyEase(cost);

            if (selection.WeaponOfChoice)
            {
                int choiceCost = _app.Rules.GetWeaponProficiencyCpCost(classIds, "weapon_of_choice", "combat_option", specialized: false);
                if (choiceCost > 0)
                    cost += ApplyEase(choiceCost);
            }

            if (selection.WeaponExpertise)
            {
                int expertiseCost = _app.Rules.GetWeaponProficiencyCpCost(classIds, "weapon_expertise", "combat_option", specialized: false);
                if (expertiseCost > 0)
                    cost += ApplyEase(expertiseCost);
            }

            if (selection.WeaponMastery)
            {
                var effectiveClasses = classIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                bool singleFighter = effectiveClasses.Count == 1 && effectiveClasses.Any(id => string.Equals(id, "fighter", StringComparison.OrdinalIgnoreCase));
                int masteryCost = singleFighter ? 2 : 8;
                cost += ApplyEase(masteryCost);
            }

            if (selection.HighMastery)
            {
                var effectiveClasses = classIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                bool singleFighter = effectiveClasses.Count == 1 && effectiveClasses.Any(id => string.Equals(id, "fighter", StringComparison.OrdinalIgnoreCase));
                int highMasteryCost = singleFighter ? 2 : 8;
                cost += ApplyEase(highMasteryCost);
            }

            if (selection.GrandMastery)
            {
                var effectiveClasses = classIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                bool singleFighter = effectiveClasses.Count == 1 && effectiveClasses.Any(id => string.Equals(id, "fighter", StringComparison.OrdinalIgnoreCase));
                int grandMasteryCost = singleFighter ? 2 : 8;
                cost += ApplyEase(grandMasteryCost);
            }

            total += Math.Max(0, cost);
        }

        return total;
    }
    private bool _suppressCoreStatsRefresh;
    public UIElement View => this;

    private static readonly string[] AbilityOrder  = { "str", "dex", "con", "int", "wis", "cha" };
    private static readonly string[] AbilityAbbrev = { "STR", "DEX", "CON", "INT", "WIS", "CHA" };
    private static readonly (string Key, string Label, string AbilityKey)[] SubAbilityOrder =
    {
        ("str_stamina", "STR Stamina", "str"),
        ("str_muscle", "STR Muscle", "str"),
        ("dex_aim", "DEX Aim", "dex"),
        ("dex_balance", "DEX Balance", "dex"),
        ("con_health", "CON Health", "con"),
        ("con_fitness", "CON Fitness", "con"),
        ("int_reason", "INT Reason", "int"),
        ("int_knowledge", "INT Knowledge", "int"),
        ("wis_intuition", "WIS Intuition", "wis"),
        ("wis_willpower", "WIS Willpower", "wis"),
        ("wis_perception", "WIS Perception", "wis"),
        ("cha_leadership", "CHA Leadership", "cha"),
        ("cha_appearance", "CHA Appearance", "cha"),
    };

    private static readonly Brush StrColor = BrushFromHex("#D6A15E");
    private static readonly Brush DexColor = BrushFromHex("#6AB0A4");
    private static readonly Brush ConColor = BrushFromHex("#8FB070");
    private static readonly Brush IntColor = BrushFromHex("#6F97C9");
    private static readonly Brush WisColor = BrushFromHex("#9E8CB8");
    private static readonly Brush ChaColor = BrushFromHex("#C78B6D");
    private static readonly Brush AccentGold = BrushFromHex("#E8C050");
    private static readonly Brush AccentNeutral = BrushFromHex("#C4A468");
    private static readonly Regex CoinRangeRegex = new(
        @"(?<low>\d[\d,]*)\s*(?:-|–|to)\s*(?<high>\d[\d,]*)\s*(?<coin>pp|gp|ep|sp|cp)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CoinValueRegex = new(
        @"(?<value>\d[\d,]*)\s*(?<coin>pp|gp|ep|sp|cp)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BareRangeRegex = new(
        @"(?<low>\d[\d,]*)\s*(?:-|–|to)\s*(?<high>\d[\d,]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BareNumberRegex = new(
        @"\d[\d,]*",
        RegexOptions.Compiled);
    private static readonly Dictionary<string, string[]> NwpAbilityFamilyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Strength"] = new[] { "str_stamina", "str_muscle" },
        ["Dexterity"] = new[] { "dex_aim", "dex_balance" },
        ["Constitution"] = new[] { "con_health", "con_fitness" },
        ["Intelligence"] = new[] { "int_reason", "int_knowledge" },
        ["Wisdom"] = new[] { "wis_intuition", "wis_willpower", "wis_perception" },
        ["Charisma"] = new[] { "cha_leadership", "cha_appearance" },
    };

    public CharGenReviewScreen(MainWindow app)
    {
        _app = app;
        _suppressCoreStatsRefresh = true;
        InitializeComponent();
        _suppressCoreStatsRefresh = false;
    }

    public void OnEnter()
    {
        _app.SetBanner(_app.CharGen.IsLevelUpMode
            ? "Character Section  ›  Level Up  ›  Review"
            : "Character Blueprint  ›  Review");
        bool isPO_r = _app.CharGen.CharacterMode == "players_option";
        bool isWizardPO_r = isPO_r && string.Equals(_app.CharGen.ClassId, "wizard", StringComparison.OrdinalIgnoreCase);
        bool hasWizardSpecs_r = _app.Rules.Classes.TryGetValue("wizard", out var _wc_r) && _wc_r.Specializations is { Count: > 0 };
        int baseReviewStep = isPO_r ? (isWizardPO_r && hasWizardSpecs_r ? 13 : 12) : 9;
        bool hasWizardSpellStep = IsWizardCasterInCharGen();
        int reviewStep = hasWizardSpellStep ? baseReviewStep + 1 : baseReviewStep;
        _app.SetNavBar(reviewStep, reviewStep, "Review",
            backAction: () => _app.GoTo(hasWizardSpellStep ? "chargen_wizard_spells" : "chargen_equipment", -1),
            nextAction: SafeFinalise,
            nextLabel: "FINISH  ✔");

        var cg = _app.CharGen;
        NormalizeCoreClassAbilitySelections(cg);
        _app.Rules.Races.TryGetValue(cg.RaceId,  out var race);
        _app.Rules.Classes.TryGetValue(cg.ClassId, out var cls);
        var effectiveClassIds = GetEffectiveClassIds(cg);

        SumName.Text  = cg.Name;
        SumPlayerName.Text = cg.PlayerName;
        SelectGenderComboValue(cg.Gender);
        SumRace.Text  = race?.Name  ?? cg.RaceId;
        SumClass.Text = BuildClassDisplayName(effectiveClassIds, cls?.Name ?? cg.ClassId);
        SumAlignment.Text = string.IsNullOrWhiteSpace(cg.Alignment) ? "Not selected" : cg.Alignment;
        SumDeity.Text = !_app.Rules.DeityRulesEnabled
            ? "Deity rules disabled"
            : _app.Rules.Deities.TryGetValue(cg.DeityId, out var deity) ? deity.Name : "None";
        _suppressCoreStatsRefresh = true;

        if (cg.IsLevelUpMode)
        {
            // Level-up mode: lock the level/HP inputs, show progression summary
            LevelInput.Visibility          = Visibility.Collapsed;
            MaxHpAtLevelOneCheck.Visibility = Visibility.Collapsed;
            CpPerLevelRow.Visibility       = Visibility.Collapsed;
            LevelUpSummary.Visibility      = Visibility.Visible;

            var existing = (cg.LevelUpCharacterIndex >= 0 && cg.LevelUpCharacterIndex < _app.Characters.Count)
                ? _app.Characters[cg.LevelUpCharacterIndex]
                : null;

            int currentLevel = existing?.Level ?? cg.CharacterLevel;
            int currentXp    = existing?.ExperiencePoints ?? 0;
            int pendingXp    = Math.Max(0, cg.LevelUpPendingExperienceGain);
            int pendingHp    = Math.Max(0, cg.LevelUpPendingHitPointGain);
            int pendingCp    = Math.Max(0, cg.LevelUpPendingCharacterPointGain);
            int newXp        = currentXp + pendingXp;
            int projectedLevel = CharacterProgressionService.GetLevelForExperience(cg.ClassId, newXp);

            string levelLine = projectedLevel != currentLevel
                ? $"Level {currentLevel} → {projectedLevel}  (XP: {currentXp:N0} + {pendingXp:N0} = {newXp:N0})"
                : $"Level {currentLevel}  (XP: {currentXp:N0} + {pendingXp:N0} = {newXp:N0})";
            string hpLine = projectedLevel > currentLevel
                ? $"HP: {(existing?.HitPoints ?? 0)} + {pendingHp} pending if level-up = {(existing?.HitPoints ?? 0) + pendingHp}"
                : $"HP: {(existing?.HitPoints ?? 0)} (no level-up projected, pending HP ignored)";
            string cpLine = pendingCp > 0 ? $"  |  +{pendingCp} CP" : string.Empty;
            string hpAuditLine = existing?.HitPointGainByLevel.Count > 0
                ? "HP audit: " + string.Join(", ", existing.HitPointGainByLevel
                    .OrderBy(kv => kv.Key)
                    .Select(kv => $"L{kv.Key}:+{kv.Value}"))
                : "HP audit: no per-level HP history recorded yet.";
            LevelUpSummary.Text = $"{levelLine}\n{hpLine}{cpLine}\n{hpAuditLine}";

            // Set review level to the projected value so stat preview works
            cg.CharacterLevel = projectedLevel;
            LevelInput.Text = projectedLevel.ToString();
            MaxHpAtLevelOneCheck.IsChecked = false;
        }
        else
        {
            LevelInput.Visibility          = Visibility.Visible;
            MaxHpAtLevelOneCheck.Visibility = Visibility.Visible;
            CpPerLevelRow.Visibility       = string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
            LevelUpSummary.Visibility      = Visibility.Collapsed;

            if (cg.IsExistingCharacterMode)
            {
                int enteredXp = Math.Max(0, cg.ExistingStartingExperience);
                int effectiveXp = GetExistingExperienceForLeveling(cg);
                int classCount = GetEffectiveClassIds(cg)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                int derivedLevel = CharacterProgressionService.GetLevelForExperience(cg.ClassId, effectiveXp);
                cg.CharacterLevel = derivedLevel;
                LevelInput.Text = derivedLevel.ToString();
                if (string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
                {
                    int classLevelAdvances = cg.GetExistingClassLevelAdvances();
                    int levelBasedCp = Math.Max(0, cg.ExistingCpPerLevel) * classLevelAdvances;
                    int startingPoolCp = Math.Max(0, cg.ExistingStartingUnspentCharacterPoints);
                    LevelUpSummary.Text = classCount > 1 && cg.ExistingExperienceIsGrandTotalForMulticlass
                        ? $"Existing character mode: Total XP {enteredXp:N0} split across {classCount} classes -> {effectiveXp:N0} per class -> Level {derivedLevel}.\nCP award: {levelBasedCp:N0} from {classLevelAdvances} class level(s) + {startingPoolCp:N0} starting pool = {levelBasedCp + startingPoolCp:N0}."
                        : $"Existing character mode: XP {effectiveXp:N0} -> Level {derivedLevel}.\nCP award: {levelBasedCp:N0} from {classLevelAdvances} class level(s) + {startingPoolCp:N0} starting pool = {levelBasedCp + startingPoolCp:N0}.";
                }
                else
                {
                    LevelUpSummary.Text = classCount > 1 && cg.ExistingExperienceIsGrandTotalForMulticlass
                        ? $"Existing character mode: Total XP {enteredXp:N0} split across {classCount} classes -> {effectiveXp:N0} per class -> Level {derivedLevel}."
                        : $"Existing character mode: XP {effectiveXp:N0} -> Level {derivedLevel}.";
                }
                if (cg.UsesSeparateExistingClassExperience)
                {
                    string classSummary = string.Join("\n", GetEffectiveClassIds(cg).Select(id =>
                    {
                        string className = _app.Rules.Classes.TryGetValue(id, out var definition) ? definition.Name : id;
                        int xp = cg.GetExistingExperienceForClass(id);
                        return $"{className}: {xp:N0} XP → Level {CharacterProgressionService.GetLevelForExperience(id, xp)}";
                    }));
                    LevelUpSummary.Text = "Existing character — separate starting XP (bonuses already included):\n" + classSummary;
                    if (string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
                    {
                        int advances = cg.GetExistingClassLevelAdvances();
                        LevelUpSummary.Text += $"\nCP award: {Math.Max(0, cg.ExistingCpPerLevel) * advances:N0} from {advances} class level(s) + {Math.Max(0, cg.ExistingStartingUnspentCharacterPoints):N0} starting pool = {cg.GetExistingCharacterPointPool():N0}.";
                    }
                }
                LevelUpSummary.Visibility = Visibility.Visible;
            }
            else
            {
                LevelInput.Text = Math.Max(1, cg.CharacterLevel).ToString();
                LevelUpSummary.Text = "";
            }

            if (cg.ExistingCpPerLevel > 0)
                CpPerLevelInput.Text = cg.ExistingCpPerLevel.ToString();
            if (!string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
                CpPerLevelInput.Text = "0";

            MaxHpAtLevelOneCheck.IsChecked = true;
        }

        _suppressCoreStatsRefresh = false;

        var selectedPackage = _app.Rules.BuildRacialAbilityPackage(cg.RaceId, cg.SelectedRacialAbilityIds);
        var classPackage = _app.Rules.BuildClassAbilityPackage(
            cg.ClassId,
            cg.SelectedClassAbilityIds,
            cg.RacialCarryoverToClassPoints,
            cg.WizardSpecializationId,
            Math.Max(1, cg.CharacterLevel),
            cg.RaceId);
        SumRacialAbilities.ItemsSource = selectedPackage.selectedAbilities.Count > 0
            ? selectedPackage.selectedAbilities.Select(a => a.Description).ToList()
            : new List<string> { "(No racial abilities selected)" };
        SumClassAbilities.ItemsSource = classPackage.selectedAbilities.Count > 0
            ? classPackage.selectedAbilities.Select(a => a.Description).ToList()
            : new List<string> { "(No class abilities selected)" };

        var optionCatalog = _app.CharacterOptions.GetCatalog();
        var optionLines = new List<string>();
        var nwpLookup = optionCatalog.NonweaponProficiencies
            .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);
        var selectedNwps = cg.SelectedNonweaponProficiencyIds
            .Select(id => nwpLookup.TryGetValue(id, out var proficiency) ? proficiency : null)
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderBy(x => x.Name)
            .ToList();
        var selectedTraits = optionCatalog.Traits
            .Where(x => cg.SelectedTraitIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => x.Name)
            .ToList();
        var selectedDisadvantages = optionCatalog.Disadvantages
            .Where(x => cg.SelectedDisadvantageSeverities.ContainsKey(x.Id))
            .OrderBy(x => x.Name)
            .Select(x =>
            {
                string severity = cg.SelectedDisadvantageSeverities[x.Id];
                bool isSevere = string.Equals(severity, "severe", StringComparison.OrdinalIgnoreCase) && x.SevereBonus.HasValue;
                int bonus = isSevere ? x.SevereBonus!.Value : x.ModerateBonus;
                return $"{x.Name} [{(isSevere ? "Severe" : "Moderate")}] (+{bonus} CP)";
            })
            .ToList();

        if (selectedNwps.Count > 0)
        {
            int improvementCp = NwpGrantRules.GetImprovementCost(
                selectedNwps.Select(x => x.Id), cg.SelectedNonweaponProficiencyImprovements);
            if (string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
            {
                int purchaseCp = selectedNwps.Sum(x => NwpGrantRules.GetAcquisitionCost(cg, x));
                optionLines.Add($"Nonweapon proficiencies ({purchaseCp} CP purchases, {improvementCp} CP improvements): {string.Join(", ", selectedNwps.Select(x => FormatNwpSummary(cg, x)))}");
            }
            else
            {
                optionLines.Add($"Nonweapon proficiencies ({selectedNwps.Sum(x => x.Slots)} slots, {improvementCp} CP improvements): {string.Join(", ", selectedNwps.Select(x => FormatNwpSummary(cg, x)))}");
            }
        }
        if (selectedTraits.Count > 0)
            optionLines.Add($"Traits ({selectedTraits.Sum(x => x.Cost)} CP): {string.Join(", ", selectedTraits.Select(x => x.Name))}");
        if (selectedDisadvantages.Count > 0)
            optionLines.Add($"Disadvantages: {string.Join(", ", selectedDisadvantages)}");
        if (cg.SelectedLanguages.Count > 0)
            optionLines.Add($"Languages: {string.Join(", ", cg.SelectedLanguages.Select(FormatLanguageSummary))}");

        // Weapon proficiencies summary
        if (cg.SelectedWeaponProficiencies.Count > 0)
        {
            var classIds = cg.SelectedClassIds.Count > 0
                ? cg.SelectedClassIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : string.IsNullOrWhiteSpace(cg.ClassId)
                    ? new List<string>()
                    : new List<string> { cg.ClassId };
            int slotsUsed = string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
                ? GetEffectiveWeaponCpUsedForState(cg, classIds, cg.SelectedWeaponProficiencies)
                : _app.Rules.GetTotalWeaponProficiencySlotsUsed(cg.SelectedWeaponProficiencies);
            var wpNames = cg.SelectedWeaponProficiencies
                .Select(wp => wp.GrandMastery ? $"{wp.DisplayName} ★★★ [Grand Master]"
                    : wp.HighMastery ? $"{wp.DisplayName} ★★ [High Master]"
                    : wp.WeaponMastery ? $"{wp.DisplayName} ★ [Master]"
                    : wp.Specialized ? $"{wp.DisplayName} ★ [Specialist]"
                    : wp.DisplayName)
                .ToList();
            optionLines.Add(string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
                ? $"Weapon proficiencies ({slotsUsed} CP): {string.Join(", ", wpNames)}"
                : $"Weapon proficiencies ({slotsUsed} slots): {string.Join(", ", wpNames)}");
        }

        if (cg.SelectedEquipment.Count > 0)
        {
            string equipmentText = string.Join(", ",
                cg.SelectedEquipment
                    .OrderBy(x => x.Category)
                    .ThenBy(x => x.ItemName)
                    .Select(x => x.Quantity > 1 ? $"{x.ItemName} x{x.Quantity}" : x.ItemName));
            optionLines.Add($"Equipment ({cg.SelectedEquipment.Count} line items): {equipmentText}");
        }

        SumCharacterOptions.ItemsSource = optionLines.Count > 0
            ? optionLines
            : new List<string> { "(No proficiencies, traits, disadvantages, or languages selected)" };

        var spellcastingLines = new List<string>();
        if (RulesEngine.IsPriestClassId(cg.ClassId)
            && cg.SelectedSpheres.Count > 0)
        {
            var normalizedSpheres = CharGenClassScreen.NormalizeSphereSelections(cg.SelectedSpheres);
            var sphereText = string.Join(", ",
                normalizedSpheres
                    .OrderBy(kv => kv.Key)
                    .Select(kv => $"{kv.Key} ({FormatSelectionLevel(kv.Value)})"));
            spellcastingLines.Add($"Cleric spheres: {sphereText}");
        }

        if (string.Equals(cg.ClassId, "wizard", StringComparison.OrdinalIgnoreCase)
            && (!string.IsNullOrEmpty(cg.WizardSpecializationId) || cg.SelectedWizardSchools.Count > 0))
        {
            var available = CharGenClassAbilitiesScreen.NormalizeWizardSchoolSelections(cg.SelectedWizardSchools)
                .Where(kv => kv.Value)
                .Select(kv => kv.Key)
                .OrderBy(x => x)
                .ToList();
            var opposed = _app.Rules.Classes.TryGetValue("wizard", out var wizardClass)
                ? wizardClass.Specializations?
                    .FirstOrDefault(s => string.Equals(s.Id, cg.WizardSpecializationId, StringComparison.OrdinalIgnoreCase))?
                    .OppositionSchools
                    .OrderBy(x => x)
                    .ToList() ?? new List<string>()
                : new List<string>();

            if (available.Count > 0)
                spellcastingLines.Add($"Wizard schools available: {string.Join(", ", available)}");
            if (opposed.Count > 0)
                spellcastingLines.Add($"Wizard schools opposed: {string.Join(", ", opposed)}");
        }

        bool isPlayersOptionMode = string.Equals(
            CharGenState.NormalizeCharacterModeToken(cg.CharacterMode),
            "players_option",
            StringComparison.OrdinalIgnoreCase);
        var selectedAbilityEntries = cg.SelectedClassAbilityIds
            .Concat(cg.SelectedAbilitiesByClass.Values.SelectMany(entries => entries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selectedSkillIds = isPlayersOptionMode
            ? RulesEngine.GetRogueSkillIdsForClassProfile(cg.ClassId, selectedAbilityEntries)
            : RulesEngine.GetStandardCoreRogueSkillIdsForClassProfile(cg.ClassId);
        if (selectedSkillIds.Count > 0)
        {
            int level = Math.Max(1, cg.CharacterLevel);
            int dex = cg.ModifiedAbilities.TryGetValue("dex", out var modifiedDex)
                ? modifiedDex
                : cg.Abilities.GetValueOrDefault("dex", 10);
            string raceId = string.IsNullOrWhiteSpace(cg.BaseRaceId) ? cg.RaceId : cg.BaseRaceId;
            bool useAverageProgression = isPlayersOptionMode
                && RulesEngine.UsesAutomaticRogueSkillProgression(cg.ClassId);

            var breakdown = RulesEngine.BuildRogueSkillBreakdown(
                selectedSkillIds,
                raceId,
                dex,
                cg.RogueSkillArmorProfile,
                cg.SelectedRogueSkillPoints,
                level,
                useAverageProgression);

            var finalSkills = string.Join(", ", breakdown.Select(x => $"{x.SkillName} {x.FinalScore}%"));

            if (useAverageProgression)
            {
                spellcastingLines.Add("Rogue skill progression: Automatic DMG average table");
            }
            else
            {
                int pool = RulesEngine.GetRogueSkillPointPoolForLevel(level, cg.ClassId);
                int spent = breakdown.Sum(x => x.AllocatedPoints);
                int remaining = pool - spent;
                spellcastingLines.Add($"Rogue skill points: {spent}/{pool} (remaining {remaining})");
            }
            spellcastingLines.Add($"Rogue skills: {finalSkills}");
        }

        if (spellcastingLines.Count > 0)
        {
            SpellcastingSummaryHeader.Visibility = Visibility.Visible;
            SpellcastingSummary.Visibility = Visibility.Visible;
            SpellcastingSummary.Text = string.Join("\n", spellcastingLines);
        }
        else
        {
            SpellcastingSummaryHeader.Visibility = Visibility.Collapsed;
            SpellcastingSummary.Visibility = Visibility.Collapsed;
            SpellcastingSummary.Text = "";
        }

        if (selectedPackage.budget > 0)
        {
            CpSummary.Text = $"Remaining CP: {selectedPackage.remaining}  (Spent {selectedPackage.spent} / {selectedPackage.budget})";
            CpSummary.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(selectedPackage.remaining < 0 ? "#C02828" : selectedPackage.remaining == 0 ? "#E8C050" : "#C4A468"));
        }
        else
        {
            CpSummary.Text = "";
        }

        if (cg.IsLevelUpMode || cg.IsExistingCharacterMode)
        {
            int startingPoolCp = cg.IsLevelUpMode
                && cg.LevelUpCharacterIndex >= 0
                && cg.LevelUpCharacterIndex < _app.Characters.Count
                    ? Math.Max(0, _app.Characters[cg.LevelUpCharacterIndex].UnspentCharacterPoints)
                    : cg.GetExistingCharacterPointPool();
            int pendingCp = Math.Max(0, cg.LevelUpPendingCharacterPointGain);
            int availablePoolCp = Math.Max(0, startingPoolCp + pendingCp);

            var nwpDefinitions = _app.CharacterOptions.GetCatalog().NonweaponProficiencies.ToList();
            int baselineNwpCp = CalculateTotalNwpCp(cg.BaselineNonweaponProficiencyIds, cg.BaselineNonweaponProficiencyImprovements, nwpDefinitions, cg);
            int currentNwpCp = CalculateTotalNwpCp(cg, nwpDefinitions);
            int baselineWeaponCp = GetEffectiveWeaponCpUsedForState(cg, effectiveClassIds, cg.BaselineWeaponProficiencies);
            int currentWeaponCp = GetEffectiveWeaponCpUsedForState(cg, effectiveClassIds, cg.SelectedWeaponProficiencies);
            int cpSpentThisCycle = Math.Max(0, currentNwpCp - baselineNwpCp) + Math.Max(0, currentWeaponCp - baselineWeaponCp);
            int remainingGlobalCp = Math.Max(0, availablePoolCp - cpSpentThisCycle);

            ClassCpSummary.Text = $"Global CP Pool: {remainingGlobalCp}  (Spent this update {cpSpentThisCycle} / Available {availablePoolCp})";
            ClassCpSummary.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(remainingGlobalCp == 0 ? "#E8C050" : "#C4A468"));
        }
        else if (classPackage.budget > 0)
        {
            ClassCpSummary.Text = $"Class CP: Remaining {classPackage.remaining}  (Spent {classPackage.spent} / {classPackage.budget})";
            ClassCpSummary.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(classPackage.remaining < 0 ? "#C02828" : classPackage.remaining == 0 ? "#E8C050" : "#C4A468"));
        }
        else
        {
            ClassCpSummary.Text = "";
        }

        var effectiveAbilities = GetEffectiveReviewAbilities(cg);
        var items = new List<AbilityViewModel>();
        for (int i = 0; i < AbilityOrder.Length; i++)
        {
            int score = effectiveAbilities.GetValueOrDefault(AbilityOrder[i], cg.Abilities.GetValueOrDefault(AbilityOrder[i], 0));
            var color = score >= 15 ? "#E8C050" : score >= 9 ? "#C4A468" : "#C02828";
            items.Add(new AbilityViewModel
            {
                Score      = score.ToString(),
                Abbrev     = AbilityAbbrev[i],
                ScoreColor = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(color)),
            });
        }
        SumAbilities.ItemsSource = items;

        if (cg.CharacterMode == "players_option")
        {
            var effectiveSubAbilities = BuildReviewSubAbilities(cg, effectiveAbilities);
            var subLines = new List<MechanicsLineViewModel>();
            foreach (var sub in SubAbilityOrder)
            {
                int score = effectiveSubAbilities.GetValueOrDefault(sub.Key,
                    effectiveAbilities.GetValueOrDefault(sub.AbilityKey, cg.Abilities.GetValueOrDefault(sub.AbilityKey, 10)));
                string effect = SubAbilityTables.GetEffect(sub.Key, score, cg.ExceptionalStrength);
                subLines.Add(new MechanicsLineViewModel
                {
                    Text = $"{sub.Label} {score}: {effect}",
                    LineColor = AbilityColor(sub.AbilityKey)
                });
            }

            if (cg.ExceptionalStrength > 0)
            {
                subLines.Insert(0, new MechanicsLineViewModel
                {
                    Text = $"Exceptional Strength: 18/{(cg.ExceptionalStrength == 100 ? "00" : cg.ExceptionalStrength.ToString("D2"))}",
                    LineColor = AccentGold
                });
            }

            var totals = SubAbilityTables.CalculateTotals(effectiveSubAbilities, cg.ExceptionalStrength, GetEffectiveClassIdForConBonus(cg));
            SumSubAbilityBreakdown.ItemsSource = subLines;

            var notes = totals.ToNotes();
            var derivedLines = new List<MechanicsLineViewModel>();
            for (int i = 0; i < notes.Count; i++)
            {
                derivedLines.Add(new MechanicsLineViewModel
                {
                    Text = notes[i],
                    LineColor = DerivedLineColor(i)
                });
            }
            SumSubDerivedTotals.ItemsSource = derivedLines;
        }
        else
        {
            SumSubAbilityBreakdown.ItemsSource = new List<MechanicsLineViewModel>
            {
                new()
                {
                    Text = "Core Rules mode: Sub-ability mechanics not active.",
                    LineColor = AccentNeutral
                }
            };
            SumSubDerivedTotals.ItemsSource = new List<MechanicsLineViewModel>();
        }

        RefreshCoreStatsSummary();
    }

    private void LevelInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressCoreStatsRefresh)
            return;
        RefreshCoreStatsSummary();
    }

    private void MaxHpAtLevelOneCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressCoreStatsRefresh)
            return;
        RefreshCoreStatsSummary();
    }

    private void RefreshCoreStatsSummary()
    {
        if (LevelInput == null || MaxHpAtLevelOneCheck == null || CoreStatsSummary == null)
            return;

        int level = GetConfiguredLevel();
        bool maxHpAtLevelOne = MaxHpAtLevelOneCheck.IsChecked != false;
        var cg = _app.CharGen;
        var effectiveAbilities = GetEffectiveReviewAbilities(cg);
        cg.CharacterLevel = level;

        Models.CharacterSheet? previewSheet;
        try
        {
            previewSheet = _app.Rules.BuildCharacter(
                cg.Name,
                cg.RaceId,
                cg.ClassId,
                effectiveAbilities,
                cg.SelectedRacialAbilityIds,
                cg.SelectedClassAbilityIds,
                cg.RacialCarryoverToClassPoints,
                cg.WizardSpecializationId,
                cg.SubAbilities,
                cg.ExceptionalStrength,
                cg.RogueSkillArmorProfile,
                1,
                cg.CharacterMode);
        }
        catch (Exception ex)
        {
            CoreStatsSummary.Text = "Core stats preview unavailable until ability budgets are in range."
                + $"\nReason: {ex.Message}";
            return;
        }

        int con = cg.ModifiedAbilities.GetValueOrDefault("con", cg.Abilities.GetValueOrDefault("con", 10));
        int hp = CalculateLevelScaledHitPoints(cg.ClassId, level, con, maxHpAtLevelOne, cg.CharacterMode, previewSheet.Bonuses);
        int conHpBonus = CharacterProgressionService.GetConHitPointBonus(con, cg.ClassId);
        int totalHpPerLevelBonus = conHpBonus + previewSheet.Bonuses.HpPerLevel;
        int ac = previewSheet.ArmorClass;
        previewSheet.ClassMode = cg.ClassMode;
        previewSheet.ClassIds = GetEffectiveClassIds(cg);
        previewSheet.Level = level;
        previewSheet.ClassLevels = previewSheet.ClassIds.ToDictionary(id => id,
            id => cg.IsExistingCharacterMode && !cg.IsLevelUpMode
                ? CharacterProgressionService.GetLevelForExperience(id, cg.GetExistingExperienceForClass(id))
                : cg.IsLevelUpMode && cg.LevelUpCharacterIndex >= 0 && cg.LevelUpCharacterIndex < _app.Characters.Count
                    ? Math.Max(_app.Characters[cg.LevelUpCharacterIndex].ClassLevels.GetValueOrDefault(id, 1),
                        CharacterProgressionService.GetLevelForExperience(id,
                            (int)Math.Clamp((long)CharacterProgressionService.GetExperienceForClass(_app.Characters[cg.LevelUpCharacterIndex], id)
                                + Math.Max(0, cg.LevelUpPendingExperienceGain), 0L, int.MaxValue)))
                    : level,
            StringComparer.OrdinalIgnoreCase);
        int baseThac0 = CharacterProgressionService.GetBaseThac0(previewSheet);
        int effectiveThac0 = baseThac0;

        CoreStatsSummary.Text = $"Level {level}  |  HP {hp}  |  AC {ac}  |  THAC0 {effectiveThac0}"
            + $"\nBase THAC0 {baseThac0}"
            + $"\nClass hit die d{GetClassHitDie(cg.ClassId)}, HP bonus/level {FormatSigned(totalHpPerLevelBonus)} ({FormatSigned(conHpBonus)} CON{(previewSheet.Bonuses.HpPerLevel != 0 ? $", {FormatSigned(previewSheet.Bonuses.HpPerLevel)} racial/class" : string.Empty)}), level-1 HP mode: {(maxHpAtLevelOne ? "max" : "average")}";
    }

    private int GetConfiguredLevel()
    {
        if (int.TryParse(LevelInput.Text, out int level))
            return Math.Clamp(level, 1, 30);
        return 1;
    }

    private int GetConfiguredCpPerLevel()
    {
        if (!string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (int.TryParse(CpPerLevelInput?.Text, out int cp))
            return Math.Max(0, cp);
        return 0;
    }

    private static int GetClassHitDie(string classId) => (classId ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "fighter" or "ranger" or "paladin" => 10,
        "cleric" or "druid" or "thief" => 8,
        "wizard" => 4,
        "bard" or "psionicist" => 6,
        _ when RulesEngine.IsPriestClassId(classId) => 8,
        _ => 6,
    };

    private static int GetBaseThac0(string classId, int level)
    {
        level = Math.Max(1, level);
        return (classId ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "fighter" or "paladin" or "ranger" => 20 - (level - 1),
            "cleric" or "druid" or "thief" or "bard" => 20 - ((level - 1) / 2),
            "wizard" => 20 - ((level - 1) / 3),
            _ => 20 - ((level - 1) / 2),
        };
    }

    private static int CalculateLevelScaledHitPoints(
        string classId,
        int level,
        int con,
        bool maxHpAtLevelOne,
        string characterMode,
        AbilityBonuses bonuses)
    {
        int hitDie = GetClassHitDie(classId);
        int averageDie = (hitDie + 1) / 2;
        int firstLevelDie = maxHpAtLevelOne ? hitDie : averageDie;
        int conMod = CharacterProgressionService.GetConHitPointBonus(con, classId);
        bool isPlayersOption = string.Equals(characterMode, "players_option", StringComparison.OrdinalIgnoreCase);
        int levelBonus = isPlayersOption ? bonuses.HpPerLevel : 0;
        int flatBonus = isPlayersOption ? bonuses.HpFlatBonus : 0;

        int firstLevelHp = firstLevelDie + conMod + levelBonus + flatBonus;
        int additionalLevelHp = averageDie + conMod + levelBonus;
        int total = firstLevelHp + Math.Max(0, level - 1) * additionalLevelHp;
        return Math.Max(1, total);
    }

    private Dictionary<string, int> GetEffectiveReviewAbilities(CharGenState cg)
    {
        if (cg.ModifiedAbilities is { Count: > 0 })
            return new Dictionary<string, int>(cg.ModifiedAbilities, StringComparer.OrdinalIgnoreCase);

        if (cg.Abilities.Count > 0 && !string.IsNullOrWhiteSpace(cg.RaceId))
            return _app.Rules.ApplyRacialModifiers(cg.RaceId, cg.Abilities);

        return new Dictionary<string, int>(cg.Abilities, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, int> BuildReviewSubAbilities(CharGenState cg, IReadOnlyDictionary<string, int> effectiveAbilities)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in SubAbilityOrder)
        {
            int baseAbility = cg.Abilities.GetValueOrDefault(sub.AbilityKey, 10);
            int modifiedAbility = (cg.ModifiedAbilities is { Count: > 0 })
                ? cg.ModifiedAbilities.GetValueOrDefault(sub.AbilityKey, baseAbility)
                : baseAbility;
            int effectiveAbility = effectiveAbilities.GetValueOrDefault(sub.AbilityKey, modifiedAbility);
            int abilityDelta = effectiveAbility - modifiedAbility;

            int currentValue = cg.SubAbilities.GetValueOrDefault(sub.Key, modifiedAbility);
            result[sub.Key] = ClampSubAbilityScore(sub.Key, currentValue + abilityDelta);
        }
        return result;
    }

    private static int ClampSubAbilityScore(string subAbilityKey, int value)
    {
        if (string.IsNullOrWhiteSpace(subAbilityKey))
            return Math.Clamp(value, 1, 18);

        bool isStrengthSubAbility = subAbilityKey.StartsWith("str_", StringComparison.OrdinalIgnoreCase);
        int max = isStrengthSubAbility ? 20 : 18;
        return Math.Clamp(value, 1, max);
    }

    private int GetExistingExperienceForLeveling(CharGenState cg)
    {
        return cg.GetExistingExperienceForClass(cg.ClassId);
    }

    private void RebuildSheetAbilityStateForSelectedClasses(CharacterSheet sheet, CharGenState cg)
    {
        var classIds = GetEffectiveClassIds(cg);
        if (classIds.Count == 0 && !string.IsNullOrWhiteSpace(cg.ClassId))
            classIds.Add(cg.ClassId);

        if (cg.SelectedAbilitiesByClass.Count == 0
            && !string.IsNullOrWhiteSpace(cg.ClassId)
            && cg.SelectedClassAbilityIds.Count > 0)
        {
            cg.SelectedAbilitiesByClass[cg.ClassId] = new List<string>(cg.SelectedClassAbilityIds);
        }

        var effectiveAbilities = GetEffectiveReviewAbilities(cg);
        var racialPackage = _app.Rules.BuildRacialAbilityPackage(cg.RaceId, cg.SelectedRacialAbilityIds);
        var allStructured = new List<AbilityDefinition>(racialPackage.selectedAbilities);

        int classBudget = 0;
        int classSpent = 0;
        int classRemaining = 0;

        foreach (string classId in classIds)
        {
            var selectedAbilityIds = cg.SelectedAbilitiesByClass.TryGetValue(classId, out var ids) && ids.Count > 0
                ? ids
                : string.Equals(classId, cg.ClassId, StringComparison.OrdinalIgnoreCase)
                    ? cg.SelectedClassAbilityIds
                    : new List<string>();

            var specializationId = cg.WizardSpecializationById.TryGetValue(classId, out var spec) && !string.IsNullOrWhiteSpace(spec)
                ? spec
                : string.Equals(classId, cg.ClassId, StringComparison.OrdinalIgnoreCase)
                    ? cg.WizardSpecializationId
                    : string.Empty;

            var classPackage = _app.Rules.BuildClassAbilityPackage(
                classId,
                selectedAbilityIds,
                cg.RacialCarryoverToClassPoints,
                specializationId,
                cg.UsesSeparateExistingClassExperience ? cg.GetConfiguredClassLevel(classId) : Math.Max(1, sheet.Level),
                cg.RaceId);

            var spheres = cg.SpheresByClass.TryGetValue(classId, out var classSpheres)
                ? classSpheres
                : string.Equals(classId, cg.ClassId, StringComparison.OrdinalIgnoreCase)
                    ? cg.SelectedSpheres
                    : new Dictionary<string, string>();
            var schools = cg.SchoolsByClass.TryGetValue(classId, out var classSchools)
                ? classSchools
                : string.Equals(classId, cg.ClassId, StringComparison.OrdinalIgnoreCase)
                    ? cg.SelectedWizardSchools
                    : new Dictionary<string, bool>();
            int configurationCost = _app.Rules.Classes.TryGetValue(classId, out var classDefinition)
                ? CharGenClassAbilitiesScreen.CalculateConfigurationCpCost(classDefinition, spheres, schools, specializationId)
                : 0;

            classBudget += classPackage.budget;
            classSpent += classPackage.spent + configurationCost;
            classRemaining += classPackage.remaining - configurationCost;
            allStructured.AddRange(classPackage.selectedAbilities);
        }

        if (_app.Rules.DeityRulesEnabled
            && !string.IsNullOrWhiteSpace(cg.DeityId)
            && _app.Rules.Deities.TryGetValue(cg.DeityId, out var deity)
            && deity.FollowerAbility is not null
            && allStructured.All(ability => !string.Equals(ability.Id, deity.FollowerAbility.Id, StringComparison.OrdinalIgnoreCase)))
        {
            allStructured.Add(deity.FollowerAbility);
        }

        string normalizedArmorProfile = string.IsNullOrWhiteSpace(cg.RogueSkillArmorProfile)
            ? "no_armor"
            : cg.RogueSkillArmorProfile.Trim().ToLowerInvariant();
        bool isUnarmored = normalizedArmorProfile == "no_armor";
        var bonuses = RulesEngine.AggregateEffects(allStructured, isUnarmored, Math.Max(1, cg.CharacterLevel));

        var effectiveSubAbilities = BuildReviewSubAbilities(cg, effectiveAbilities);
        foreach (var (key, mod) in bonuses.SubAbilityBonuses)
        {
            if (string.IsNullOrWhiteSpace(key) || mod == 0)
                continue;

            if (effectiveSubAbilities.ContainsKey(key))
            {
                effectiveSubAbilities[key] += mod;
                continue;
            }

            var matchKey = effectiveSubAbilities.Keys
                .FirstOrDefault(k => k.EndsWith($"_{key}", StringComparison.OrdinalIgnoreCase));
            if (matchKey is not null)
                effectiveSubAbilities[matchKey] += mod;
        }

        foreach (var key in effectiveSubAbilities.Keys.ToList())
            effectiveSubAbilities[key] = ClampSubAbilityScore(key, effectiveSubAbilities[key]);

        var subTotals = SubAbilityTables.CalculateTotals(effectiveSubAbilities, cg.ExceptionalStrength, GetEffectiveClassIdForConBonus(cg));
        bonuses.AttackBonus += subTotals.MeleeAttackBonus;
        bonuses.DamageBonus += subTotals.MeleeDamageBonus;
        bonuses.AcBonus += subTotals.ArmorClassAdjustment;
        bonuses.SurpriseBonus += subTotals.SurpriseAdjustment;
        bonuses.ReactionBonus += subTotals.ReactionAdjustment;
        bonuses.HpPerLevel += subTotals.HpPerLevel;
        bonuses.NwpSlotBonus += subTotals.BonusNwpSlots;
        bonuses.NwpCheckBonus += subTotals.PickPocketsAdjustment;
        bonuses.NwpCheckBonus += subTotals.OpenLocksAdjustment;
        bonuses.NwpCheckBonus += subTotals.MoveSilentlyAdjustment;
        bonuses.NwpCheckBonus += subTotals.ClimbWallsAdjustment;

        if (subTotals.PoisonSaveAdjustment != 0)
            bonuses.SaveBonuses["poison"] = bonuses.SaveBonuses.GetValueOrDefault("poison") + subTotals.PoisonSaveAdjustment;

        if (subTotals.MissileAttackBonus != 0)
        {
            bonuses.WeaponAttackBonuses["missile"] = bonuses.WeaponAttackBonuses.GetValueOrDefault("missile") + subTotals.MissileAttackBonus;
            bonuses.WeaponAttackBonuses["thrown"] = bonuses.WeaponAttackBonuses.GetValueOrDefault("thrown") + subTotals.MissileAttackBonus;
            bonuses.WeaponAttackBonuses["slings"] = bonuses.WeaponAttackBonuses.GetValueOrDefault("slings") + subTotals.MissileAttackBonus;
        }

        if (subTotals.MagicDefenseAdjustment != 0)
            bonuses.SaveBonuses["magic"] = bonuses.SaveBonuses.GetValueOrDefault("magic") + subTotals.MagicDefenseAdjustment;

        bonuses.SubAbilityBonuses["pick_pockets"] = bonuses.SubAbilityBonuses.GetValueOrDefault("pick_pockets") + subTotals.PickPocketsAdjustment;
        bonuses.SubAbilityBonuses["open_locks"] = bonuses.SubAbilityBonuses.GetValueOrDefault("open_locks") + subTotals.OpenLocksAdjustment;
        bonuses.SubAbilityBonuses["move_silently"] = bonuses.SubAbilityBonuses.GetValueOrDefault("move_silently") + subTotals.MoveSilentlyAdjustment;
        bonuses.SubAbilityBonuses["climb_walls"] = bonuses.SubAbilityBonuses.GetValueOrDefault("climb_walls") + subTotals.ClimbWallsAdjustment;

        bool isPlayersOption = string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);
        if (!isPlayersOption)
        {
            bonuses.HpPerLevel = 0;
            bonuses.HpFlatBonus = 0;
        }

        sheet.Abilities = new Dictionary<string, int>(effectiveAbilities, StringComparer.OrdinalIgnoreCase);
        sheet.StructuredAbilities = allStructured;
        sheet.RacialAbilities = racialPackage.selectedAbilities.Select(a => a.Description).ToList();
        sheet.Bonuses = bonuses;
        sheet.SubAbilities = effectiveSubAbilities;
        sheet.DerivedStats = subTotals.ToDerivedStats();
        sheet.SubAbilityEffects = effectiveSubAbilities
            .ToDictionary(kv => kv.Key, kv => SubAbilityTables.GetEffect(kv.Key, kv.Value, cg.ExceptionalStrength));
        sheet.RacialPointBudget = racialPackage.budget;
        sheet.RacialPointSpent = racialPackage.spent;
        sheet.RacialPointRemaining = racialPackage.remaining;
        sheet.ClassPointBudget = classBudget;
        sheet.ClassPointSpent = classSpent;
        sheet.ClassPointRemaining = classRemaining;
        sheet.ClassAbilityCarryoverPoints = Math.Max(0, cg.RacialCarryoverToClassPoints);
        sheet.BaseArmorClass = 10;
        if (sheet.BaseMovement <= 0)
            sheet.BaseMovement = 12;
        sheet.Movement = Math.Max(1, sheet.BaseMovement + bonuses.MovementBonus);
    }

    private static string FormatSelectionLevel(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "None";
        if (string.Equals(value, "minor", StringComparison.OrdinalIgnoreCase)) return "Minor";
        if (string.Equals(value, "major", StringComparison.OrdinalIgnoreCase)) return "Major";
        if (string.Equals(value, "both", StringComparison.OrdinalIgnoreCase)) return "Both";
        return value;
    }

    private static Brush AbilityColor(string abilityKey) => abilityKey switch
    {
        "str" => StrColor,
        "dex" => DexColor,
        "con" => ConColor,
        "int" => IntColor,
        "wis" => WisColor,
        "cha" => ChaColor,
        _ => AccentNeutral,
    };

    private static Brush DerivedLineColor(int lineIndex) => lineIndex switch
    {
        0 => BrushFromHex("#B58B5B"), // strength/dexterity combat
        1 => DexColor,                // thief operations
        2 => StrColor,                // strength operations
        3 => ConColor,                // durability
        4 => IntColor,                // arcane aptitude
        5 => WisColor,                // divine aptitude
        _ => ChaColor,                // utility/social
    };

    private static Brush BrushFromHex(string hex)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private static string FormatSigned(int value)
        => value >= 0 ? $"+{value}" : value.ToString();

    private void BtnCharacterSheets_Click(object sender, RoutedEventArgs e)
        => _app.GoTo("character_sheets", 1);

    private void BtnFinalise_Click(object sender, RoutedEventArgs e)
        => SafeFinalise();

    private void SafeFinalise()
    {
        try
        {
            Finalise();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Saving failed: {ex.Message}\n\nThe character data was not fully saved.",
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void NavigateAfterFinalise(int characterIndex)
    {
        var openTemplates = MessageBox.Show(
            "Character saved. Open Character Sheet Templates now?",
            "Character Sheets",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (openTemplates == MessageBoxResult.Yes)
            _app.OpenCharacterSheets(characterIndex);
        else
            _app.GoTo("characters");
    }

    private void Finalise()
    {
        var cg = _app.CharGen;
        NormalizeCoreClassAbilitySelections(cg);

        if (!cg.IsLevelUpMode && _app.License.IsDemoMode && _app.Characters.Count >= _app.License.DemoMaxCharacters)
        {
            MessageBox.Show(
                $"Demo mode allows up to {_app.License.DemoMaxCharacters} saved characters.",
                "Demo Limit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var name = SumName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Please enter a character name.", "Name Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        cg.Name = name;
        cg.PlayerName = SumPlayerName.Text.Trim();
        cg.Gender = GetSelectedGenderValue();

        if (!cg.IsLevelUpMode
            && string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
            && !ConfirmCpPerLevelSelection())
            return;

        var effectiveClassIds = GetEffectiveClassIds(cg);
        if (!_app.Rules.IsClassCombinationAlignmentAllowed(effectiveClassIds, cg.Alignment))
        {
            MessageBox.Show(
                "The selected alignment is not allowed by this class combination.",
                "Alignment Not Allowed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var issues = _app.Rules.Validate(
            cg.RaceId,
            cg.ClassId,
            GetEffectiveReviewAbilities(cg),
            cg.Alignment,
            ignoreAlignment: effectiveClassIds.Count > 1);
        if (issues.Count > 0)
        {
            var proceed = MessageBox.Show(
                $"{issues[0]}\n\nFinish anyway?",
                "Validation Warning",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (proceed != MessageBoxResult.Yes) return;
        }

        if (cg.IsLevelUpMode)
        {
            if (cg.LevelUpCharacterIndex < 0 || cg.LevelUpCharacterIndex >= _app.Characters.Count)
            {
                MessageBox.Show("The selected character for level-up could not be found.", "Level Up", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existing = _app.Characters[cg.LevelUpCharacterIndex];
            string wealthAdjustmentMessage = string.Empty;

            if (_app.License.IsDemoMode)
            {
                int projectedXp = Math.Max(0, existing.ExperiencePoints) + Math.Max(0, cg.LevelUpPendingExperienceGain);
                int projectedLevel = CharacterProgressionService.GetLevelForExperience(existing.ClassId, projectedXp);
                if (projectedLevel > _app.License.DemoMaxLevel)
                {
                    MessageBox.Show(
                        $"Demo mode allows level-ups only up to level {_app.License.DemoMaxLevel}.",
                        "Demo Limit",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
            }

            var baselineEquipment = (cg.BaselineEquipmentSelections?.Count ?? 0) > 0
                ? cg.BaselineEquipmentSelections
                : existing.EquipmentSelections;
            int baselineEquipmentCopper = SumEquipmentCostCopper(baselineEquipment);
            int updatedEquipmentCopper = SumEquipmentCostCopper(cg.SelectedEquipment);
            int equipmentDeltaCopper = updatedEquipmentCopper - baselineEquipmentCopper;
            if (equipmentDeltaCopper > 0)
            {
                int availableCopper = CharacterWealthService.GetTotalCopper(existing);
                if (availableCopper < equipmentDeltaCopper)
                {
                    MessageBox.Show(
                        $"Not enough funds for equipment changes.\n\nNeed: {CharacterWealthService.FormatCoins(equipmentDeltaCopper)}\nAvailable: {CharacterWealthService.FormatCoins(availableCopper)}",
                        "Insufficient Funds",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                CharacterWealthService.SetFromCopper(existing, availableCopper - equipmentDeltaCopper);
                wealthAdjustmentMessage = $"Spent for equipment changes: {CharacterWealthService.FormatCoins(equipmentDeltaCopper)}.";
            }
            else if (equipmentDeltaCopper < 0)
            {
                int refundCopper = Math.Abs(equipmentDeltaCopper);
                int availableCopper = CharacterWealthService.GetTotalCopper(existing);
                CharacterWealthService.SetFromCopper(existing, availableCopper + refundCopper);
                wealthAdjustmentMessage = $"Funds returned from sold/removed equipment: {CharacterWealthService.FormatCoins(refundCopper)}.";
            }

            var currentClassIds = cg.SelectedClassIds.Count > 0
                ? cg.SelectedClassIds.Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
                : string.IsNullOrWhiteSpace(cg.ClassId) ? new List<string>() : new List<string> { cg.ClassId };

            bool isPlayersOptionCharacter = string.Equals(existing.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);

            int pendingHpGain = Math.Max(0, cg.LevelUpPendingHitPointGain);
            existing.ExperiencePoints = Math.Max(0, existing.ExperiencePoints) + Math.Max(0, cg.LevelUpPendingExperienceGain);
            if (isPlayersOptionCharacter)
                existing.UnspentCharacterPoints = Math.Max(0, existing.UnspentCharacterPoints) + Math.Max(0, cg.LevelUpPendingCharacterPointGain);

            var catalog = _app.CharacterOptions.GetCatalog();
            var nwpDefinitions = catalog.NonweaponProficiencies.ToList();
            if (isPlayersOptionCharacter)
            {
                int currentNwpCp = CalculateTotalNwpCp(cg, nwpDefinitions);
                int baselineNwpCp = CalculateTotalNwpCp(cg.BaselineNonweaponProficiencyIds, cg.BaselineNonweaponProficiencyImprovements, nwpDefinitions, cg);

                var classIdsForWeaponCp = currentClassIds.Count > 0 ? currentClassIds : new List<string> { cg.ClassId };
                int currentWeaponCp = GetEffectiveWeaponCpUsedForState(cg, classIdsForWeaponCp, cg.SelectedWeaponProficiencies);
                int baselineWeaponCp = GetEffectiveWeaponCpUsedForState(cg, classIdsForWeaponCp, cg.BaselineWeaponProficiencies);

                int nwpSpentThisCycle = Math.Max(0, currentNwpCp - baselineNwpCp);
                int weaponSpentThisCycle = Math.Max(0, currentWeaponCp - baselineWeaponCp);
                int cpSpentThisCycle = nwpSpentThisCycle + weaponSpentThisCycle;
                if (cpSpentThisCycle > existing.UnspentCharacterPoints)
                {
                    MessageBox.Show(
                        $"Not enough Character Points for this level-up.\n\nSpent this cycle: {cpSpentThisCycle}\nAvailable: {existing.UnspentCharacterPoints}",
                        "Character Points",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                existing.UnspentCharacterPoints -= cpSpentThisCycle;
                existing.SpentNwpCharacterPoints += nwpSpentThisCycle;
                existing.SpentWeaponCharacterPoints += weaponSpentThisCycle;
            }

            var advancement = CharacterProgressionService.ApplyLevelAdvancementFromExperience(existing, applyHitPointProgression: false);
            bool hpIgnored = false;
            if (advancement.LeveledUp)
            {
                if (pendingHpGain > 0)
                {
                    existing.HitPoints = Math.Max(1, existing.HitPoints + pendingHpGain);
                    CharacterProgressionService.RecordHitPointGainAcrossLevels(
                        existing,
                        advancement.OldLevel,
                        advancement.NewLevel,
                        pendingHpGain);
                }
            }
            else if (pendingHpGain > 0)
            {
                hpIgnored = true;
            }

            existing.ClassMode = cg.ClassMode;
            existing.PlayerName = cg.PlayerName;
            existing.Gender = cg.Gender;
            existing.Alignment = cg.Alignment;
            existing.DeityId = cg.DeityId;
            existing.ClassIds = currentClassIds;
            existing.ClassId = currentClassIds.FirstOrDefault() ?? existing.ClassId;
            existing.ClassName = BuildClassDisplayName(currentClassIds, existing.ClassName);
            CharacterProgressionService.ApplyExperienceAdjustment(existing, 0);
            if (!string.IsNullOrWhiteSpace(cg.ClassId)
                && !string.Equals(cg.ClassMode, "multiclass", StringComparison.OrdinalIgnoreCase))
            {
                cg.SelectedAbilitiesByClass[cg.ClassId] = new List<string>(cg.SelectedClassAbilityIds);
            }
            existing.SelectedClassAbilityIds = new List<string>(cg.SelectedClassAbilityIds);
            if (cg.SelectedAbilitiesByClass.Count == 0
                && !string.IsNullOrWhiteSpace(cg.ClassId)
                && cg.SelectedClassAbilityIds.Count > 0)
            {
                cg.SelectedAbilitiesByClass[cg.ClassId] = new List<string>(cg.SelectedClassAbilityIds);
            }
            existing.SelectedRacialAbilityIds = new List<string>(cg.SelectedRacialAbilityIds);
            existing.WizardSpecializationId = cg.WizardSpecializationId;
            existing.NonweaponProficiencyIds = new List<string>(cg.SelectedNonweaponProficiencyIds);
            existing.WeaponProficiencies = cg.SelectedWeaponProficiencies
                .Select(x => new WeaponProficiencySelection
                {
                    ProficiencyId = x.ProficiencyId,
                    ProficiencyType = x.ProficiencyType,
                    DisplayName = x.DisplayName,
                    Specialized = x.Specialized,
                    WeaponOfChoice = x.WeaponOfChoice,
                    WeaponExpertise = x.WeaponExpertise,
                    WeaponMastery = x.WeaponMastery,
                    HighMastery = x.HighMastery,
                    GrandMastery = x.GrandMastery,
                })
                .ToList();
            existing.EquipmentSelections = cg.SelectedEquipment
                .Select(x => new EquipmentSelection
                {
                    ItemId = x.ItemId,
                    Category = x.Category,
                    ItemName = x.ItemName,
                    CostText = x.CostText,
                    Quantity = Math.Max(1, x.Quantity),
                    IsArmor = x.IsArmor,
                    ArmorClassValue = x.ArmorClassValue,
                    RogueArmorProfile = x.RogueArmorProfile,
                    IsShield = x.IsShield,
                    IsWeapon = x.IsWeapon,
                    WeaponSpeed = x.WeaponSpeed,
                    WeaponDamageSmallMedium = x.WeaponDamageSmallMedium,
                    WeaponDamageLarge = x.WeaponDamageLarge,
                    WeaponType = x.WeaponType,
                    WeaponSize = x.WeaponSize,
                    WeaponAttackBonus = x.WeaponAttackBonus,
                    WeaponDamageBonus = x.WeaponDamageBonus,
                    WeaponThac0Modifier = x.WeaponThac0Modifier,
                    CostGoldEach = x.CostGoldEach,
                    CostSilverEach = x.CostSilverEach,
                    CostCopperEach = x.CostCopperEach,
                    SizeClassEach = x.SizeClassEach,
                    WeightEach = x.WeightEach,
                    UseAsOffHand = x.UseAsOffHand,
                })
                .ToList();
            existing.UseSpellComponents = cg.UseSpellComponents;
            // Copy equipped slot selections.
            existing.EquippedArmorId  = cg.EquippedArmorId;
            existing.EquippedShieldId = cg.EquippedShieldId;
            existing.EquippedMainWeaponId = string.IsNullOrWhiteSpace(cg.EquippedMainWeaponId)
                ? cg.EquippedWeaponId
                : cg.EquippedMainWeaponId;
            existing.EquippedOffHandWeaponId = cg.EquippedOffHandWeaponId;
            existing.EquippedWeaponId = existing.EquippedMainWeaponId;
            existing.EquippedMagicalItemIds = cg.EquippedMagicalItemIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
            // Copy kit selection.
            if (!string.IsNullOrEmpty(cg.KitId))
            {
                var kit = _app.Rules.Kits.FirstOrDefault(k => k.Id == cg.KitId);
                existing.KitId   = cg.KitId;
                existing.KitName = kit?.Name ?? "";
            }
            existing.KitFreeNwpIds = new List<string>(cg.KitFreeNwpIds);
            existing.KitRequiredNwpIds = new List<string>(cg.KitRequiredNwpIds);

            existing.LockedLastLevelUpNonweaponIds = GetAddedIds(cg.SelectedNonweaponProficiencyIds, cg.BaselineNonweaponProficiencyIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            existing.LockedLastLevelUpWeaponIds = GetAddedIds(
                    cg.SelectedWeaponProficiencies.Select(x => x.ProficiencyId).ToList(),
                    cg.BaselineWeaponProficiencies.Select(x => x.ProficiencyId).ToList())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Persist full chargen selection state for next level-up.
            existing.SelectedSpheres = new Dictionary<string, string>(cg.SelectedSpheres, StringComparer.OrdinalIgnoreCase);
            existing.SelectedWizardSchools = new Dictionary<string, bool>(cg.SelectedWizardSchools, StringComparer.OrdinalIgnoreCase);
            existing.SelectedAbilitiesByClass = cg.SelectedAbilitiesByClass
                .ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value), StringComparer.OrdinalIgnoreCase);
            existing.SelectedTraitIds = new List<string>(cg.SelectedTraitIds);
            existing.SelectedDisadvantageSeverities = new Dictionary<string, string>(cg.SelectedDisadvantageSeverities, StringComparer.OrdinalIgnoreCase);
            existing.SelectedLanguages = cg.SelectedLanguages
                .Select(l => new LanguageSelection { SourceKey = l.SourceKey, LanguageName = l.LanguageName })
                .ToList();
            existing.SelectedNonweaponProficiencyImprovements = new Dictionary<string, int>(cg.SelectedNonweaponProficiencyImprovements, StringComparer.OrdinalIgnoreCase);
            existing.RogueSkillArmorProfile = cg.RogueSkillArmorProfile;
            existing.SelectedRogueSkillPoints = new Dictionary<string, int>(cg.SelectedRogueSkillPoints, StringComparer.OrdinalIgnoreCase);
            existing.SpheresByClass = cg.SpheresByClass
                .ToDictionary(kv => kv.Key, kv => new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            existing.SchoolsByClass = cg.SchoolsByClass
                .ToDictionary(kv => kv.Key, kv => new Dictionary<string, bool>(kv.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
            existing.WizardSpellbookIds = cg.WizardSpellbookIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            
            // DEBUG: Log spell data before filtering
            System.Diagnostics.Debug.WriteLine($"[LevelUp-CharGen] cg.WizardSpellbookIds count: {cg.WizardSpellbookIds?.Count ?? 0}");
            System.Diagnostics.Debug.WriteLine($"[LevelUp-CharGen] cg.WizardSpellLists count: {cg.WizardSpellLists?.Count ?? 0}");
            foreach (var list in cg.WizardSpellLists ?? new List<NamedSpellList>())
            {
                System.Diagnostics.Debug.WriteLine($"  List '{list.Name}' has {list.SpellIds?.Count ?? 0} spells");
            }
            
            existing.WizardSpellLists = BuildSafeWizardSpellLists(cg.WizardSpellLists, existing.WizardSpellbookIds);

            RebuildSheetAbilityStateForSelectedClasses(existing, cg);
            existing.Thac0 = CharacterProgressionService.GetBaseThac0(existing);
            
            // DEBUG: Log spell data after filtering
            System.Diagnostics.Debug.WriteLine($"[LevelUp-Saved] existing.WizardSpellbookIds count: {existing.WizardSpellbookIds?.Count ?? 0}");
            System.Diagnostics.Debug.WriteLine($"[LevelUp-Saved] existing.WizardSpellLists count: {existing.WizardSpellLists?.Count ?? 0}");
            foreach (var list in existing.WizardSpellLists ?? new List<NamedSpellList>())
            {
                System.Diagnostics.Debug.WriteLine($"  List '{list.Name}' has {list.SpellIds?.Count ?? 0} spells");
            }

            existing.LastModified = DateTime.Now;
            existing.Revision = Math.Max(1, existing.Revision + 1);

            existing.NonweaponProficiencies = catalog.NonweaponProficiencies
                .Where(x => cg.SelectedNonweaponProficiencyIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase))
                .Select(x => FormatNwpSummary(cg, x))
                .OrderBy(x => x)
                .ToList();
            existing.Languages = cg.SelectedLanguages
                .Select(FormatLanguageSummary)
                .OrderBy(x => x)
                .ToList();
            existing.Equipment = existing.EquipmentSelections
                .OrderBy(x => x.Category)
                .ThenBy(x => x.ItemName)
                .Select(x => x.Quantity > 1 ? $"{x.ItemName} x{x.Quantity}" : x.ItemName)
                .ToList();
            existing.Traits = catalog.Traits
                .Where(x => cg.SelectedTraitIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase))
                .Select(x => x.Name)
                .OrderBy(x => x)
                .ToList();
            existing.Disadvantages = catalog.Disadvantages
                .Where(x => cg.SelectedDisadvantageSeverities.ContainsKey(x.Id))
                .OrderBy(x => x.Name)
                .Select(x =>
                {
                    string severity = cg.SelectedDisadvantageSeverities[x.Id];
                    bool isSevere = string.Equals(severity, "severe", StringComparison.OrdinalIgnoreCase) && x.SevereBonus.HasValue;
                    return $"{x.Name} [{(isSevere ? "Severe" : "Moderate")}]";
                })
                .ToList();
            CharacterArmorService.RecalculateArmorForCharacter(existing, new EquipmentLibraryService().GetEquipmentLibrary());

            // Ensure wizards have at least one spellbook
            EnsureWizardHasDefaultSpellbook(existing);

            _app.SaveCharacters();
            string completion = advancement.LeveledUp
                ? $"{existing.Name} advanced from level {advancement.OldLevel} to {advancement.NewLevel}."
                : $"{existing.Name} was updated (no level change).";
            if (hpIgnored)
                completion += "\n\nHP gain was not applied because no level was gained.";
            if (!string.IsNullOrWhiteSpace(wealthAdjustmentMessage))
                completion += $"\n\n{wealthAdjustmentMessage}";

            MessageBox.Show(
                completion,
                "Level Up Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            int updatedCharacterIndex = cg.LevelUpCharacterIndex;
            cg.Clear();
            NavigateAfterFinalise(updatedCharacterIndex);
            return;
        }

        var effectiveAbilities = GetEffectiveReviewAbilities(cg);

        var sheet = _app.Rules.BuildCharacter(
            cg.Name,
            cg.RaceId,
            cg.ClassId,
            effectiveAbilities,
            cg.SelectedRacialAbilityIds,
            cg.SelectedClassAbilityIds,
            cg.RacialCarryoverToClassPoints,
            cg.WizardSpecializationId,
            cg.SubAbilities,
            cg.ExceptionalStrength,
            cg.RogueSkillArmorProfile,
            Math.Max(1, cg.CharacterLevel),
            cg.CharacterMode);

        int configuredLevel = GetConfiguredLevel();
        if (cg.IsExistingCharacterMode)
        {
            int enteredXp = Math.Max(0, cg.ExistingStartingExperience);
            int effectiveXp = GetExistingExperienceForLeveling(cg);
            int classCount = GetEffectiveClassIds(cg)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            sheet.ExperiencePoints = effectiveXp;
            configuredLevel = CharacterProgressionService.GetLevelForExperience(cg.ClassId, effectiveXp);
            if (classCount > 1 && cg.ExistingExperienceIsGrandTotalForMulticlass && !cg.UsesSeparateExistingClassExperience)
            {
                sheet.Notes.Add($"Existing character XP entered as total {enteredXp:N0}; stored as {effectiveXp:N0} per class for multiclass progression.");
            }
        }

        if (_app.License.IsDemoMode && (configuredLevel > _app.License.DemoMaxLevel
            || (cg.UsesSeparateExistingClassExperience && effectiveClassIds.Any(id => cg.GetConfiguredClassLevel(id) > _app.License.DemoMaxLevel))))
        {
            MessageBox.Show(
                $"Demo mode allows a maximum level of {_app.License.DemoMaxLevel}.",
                "Demo Limit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        bool maxHpAtLevelOne = MaxHpAtLevelOneCheck.IsChecked != false;
        int con = cg.ModifiedAbilities.GetValueOrDefault("con", cg.Abilities.GetValueOrDefault("con", 10));
        sheet.Level = configuredLevel;
        sheet.PlayerName = cg.PlayerName;
        sheet.Gender = cg.Gender;
        sheet.Alignment = cg.Alignment;
        sheet.DeityId = cg.DeityId;
        RebuildSheetAbilityStateForSelectedClasses(sheet, cg);
        sheet.HitPoints = CalculateLevelScaledHitPoints(cg.ClassId, configuredLevel, con, maxHpAtLevelOne, cg.CharacterMode, sheet.Bonuses);
        sheet.BaseHitPoints = CalculateLevelScaledHitPoints(cg.ClassId, 1, con, maxHpAtLevelOne, cg.CharacterMode, sheet.Bonuses);

        bool isCoreRulesCharacter = string.Equals(cg.CharacterMode, "core_rules", StringComparison.OrdinalIgnoreCase);
        if (isCoreRulesCharacter && configuredLevel == 1)
        {
            int coreLevelOneHpCap = Math.Max(1, GetClassHitDie(cg.ClassId) + CharacterProgressionService.GetConHitPointBonus(con, cg.ClassId));
            sheet.HitPoints = Math.Min(sheet.HitPoints, coreLevelOneHpCap);
            sheet.BaseHitPoints = Math.Min(sheet.BaseHitPoints, coreLevelOneHpCap);
        }

        sheet.HitPointGainByLevel.Clear();
        sheet.HitPointGainByLevel[1] = Math.Max(1, sheet.BaseHitPoints);
        if (configuredLevel > 1)
        {
            int levelTwoHp = CalculateLevelScaledHitPoints(cg.ClassId, 2, con, maxHpAtLevelOne, cg.CharacterMode, sheet.Bonuses);
            int perLevelGain = Math.Max(1, levelTwoHp - sheet.BaseHitPoints);
            for (int level = 2; level <= configuredLevel; level++)
                sheet.HitPointGainByLevel[level] = perLevelGain;
        }
        sheet.Thac0 = GetBaseThac0(cg.ClassId, configuredLevel);
        CharacterProgressionService.InitializeCharacterProgression(sheet, seedLevelRewards: true);

        if (cg.IsExistingCharacterMode && cg.ExistingStartingHitPoints > 0)
        {
            sheet.HitPoints = Math.Max(1, cg.ExistingStartingHitPoints);
            sheet.CurrentHitPoints = sheet.HitPoints;
        }

        bool assignedStartingFundsFromCharGen = !cg.IsLevelUpMode
            && cg.StartingFundsAssigned
            && !cg.IsExistingCharacterMode;
        if (assignedStartingFundsFromCharGen)
        {
            sheet.PlatinumPieces = Math.Max(0, cg.StartingPlatinumPieces);
            sheet.GoldPieces = Math.Max(0, cg.StartingGoldPieces);
            sheet.SilverPieces = Math.Max(0, cg.StartingSilverPieces);
            sheet.CopperPieces = Math.Max(0, cg.StartingCopperPieces);
            sheet.GemCount = Math.Max(0, cg.StartingGemCount);
            sheet.GemValueGoldPieces = Math.Max(0, cg.StartingGemValueGoldPieces);
            sheet.Gems = cg.StartingGems
                .Select(x => new GemEntry
                {
                    Name = x.Name,
                    Quantity = Math.Max(1, x.Quantity),
                    ValueGoldPieces = Math.Max(0, x.ValueGoldPieces),
                })
                .ToList();
            sheet.StartingFundsAssigned = true;
            sheet.Notes.Add($"Starting funds: {CharacterWealthService.FormatCoins(sheet)}");
            if (!string.IsNullOrWhiteSpace(cg.StartingFundsRollSummary))
                sheet.Notes.Add($"Starting funds roll: {cg.StartingFundsRollSummary}");
        }
        else if (CharacterWealthService.EnsureStartingFunds(sheet))
        {
            sheet.Notes.Add($"Starting funds: {CharacterWealthService.FormatCoins(sheet)}");
        }
        sheet.Notes.Add($"Core stats: Level {sheet.Level}, HP {sheet.HitPoints}, AC {sheet.ArmorClass}, THAC0 {sheet.Thac0}");
        sheet.ClassMode = cg.ClassMode;
        sheet.ClassIds = cg.SelectedClassIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sheet.ClassIds.Count > 0)
            sheet.ClassId = sheet.ClassIds[0];
        sheet.ClassName = BuildClassDisplayName(sheet.ClassIds, sheet.ClassName);
        if (cg.IsExistingCharacterMode)
        {
            if (cg.UsesSeparateExistingClassExperience)
            {
                sheet.ClassExperiencePoints = sheet.ClassIds.ToDictionary(
                    id => id,
                    id => cg.GetExistingExperienceForClass(id),
                    StringComparer.OrdinalIgnoreCase);
                sheet.ExperiencePoints = cg.GetExistingExperienceForClass(sheet.ClassId);
                sheet.Notes.Add("Existing character starting XP by class (includes prior bonuses): "
                    + string.Join(", ", sheet.ClassIds.Select(id => $"{id}: {sheet.ClassExperiencePoints[id]:N0}")));
            }
            CharacterProgressionService.ApplyExperienceAdjustment(sheet, 0);
        }
        else
        {
            sheet.ClassLevels = sheet.ClassIds.ToDictionary(
                id => id,
                _ => Math.Max(1, sheet.Level),
                StringComparer.OrdinalIgnoreCase);
        }
        sheet.Thac0 = CharacterProgressionService.GetBaseThac0(sheet);
        if (cg.SelectedAbilitiesByClass.Count == 0
            && !string.IsNullOrWhiteSpace(cg.ClassId)
            && cg.SelectedClassAbilityIds.Count > 0)
        {
            cg.SelectedAbilitiesByClass[cg.ClassId] = new List<string>(cg.SelectedClassAbilityIds);
        }
        sheet.WizardSpecializationId = cg.WizardSpecializationId;
        sheet.NonweaponProficiencyIds = new List<string>(cg.SelectedNonweaponProficiencyIds);
        sheet.WeaponProficiencies = cg.SelectedWeaponProficiencies
            .Select(x => new WeaponProficiencySelection
            {
                ProficiencyId = x.ProficiencyId,
                ProficiencyType = x.ProficiencyType,
                DisplayName = x.DisplayName,
                Specialized = x.Specialized,
                WeaponOfChoice = x.WeaponOfChoice,
                WeaponExpertise = x.WeaponExpertise,
                WeaponMastery = x.WeaponMastery,
                HighMastery = x.HighMastery,
                GrandMastery = x.GrandMastery,
            })
            .ToList();
        sheet.EquipmentSelections = cg.SelectedEquipment
            .Select(x => new EquipmentSelection
            {
                ItemId = x.ItemId,
                Category = x.Category,
                ItemName = x.ItemName,
                CostText = x.CostText,
                Quantity = Math.Max(1, x.Quantity),
                IsArmor = x.IsArmor,
                ArmorClassValue = x.ArmorClassValue,
                RogueArmorProfile = x.RogueArmorProfile,
                IsShield = x.IsShield,
                IsWeapon = x.IsWeapon,
                WeaponSpeed = x.WeaponSpeed,
                WeaponDamageSmallMedium = x.WeaponDamageSmallMedium,
                WeaponDamageLarge = x.WeaponDamageLarge,
                WeaponType = x.WeaponType,
                WeaponSize = x.WeaponSize,
                WeaponAttackBonus = x.WeaponAttackBonus,
                WeaponDamageBonus = x.WeaponDamageBonus,
                WeaponThac0Modifier = x.WeaponThac0Modifier,
                CostGoldEach = x.CostGoldEach,
                CostSilverEach = x.CostSilverEach,
                CostCopperEach = x.CostCopperEach,
                SizeClassEach = x.SizeClassEach,
                WeightEach = x.WeightEach,
            })
            .ToList();
        sheet.UseSpellComponents = cg.UseSpellComponents;
        // Copy equipped slot selections.
        sheet.EquippedArmorId  = cg.EquippedArmorId;
        sheet.EquippedShieldId = cg.EquippedShieldId;
        sheet.EquippedMainWeaponId = string.IsNullOrWhiteSpace(cg.EquippedMainWeaponId)
            ? cg.EquippedWeaponId
            : cg.EquippedMainWeaponId;
        sheet.EquippedOffHandWeaponId = cg.EquippedOffHandWeaponId;
        sheet.EquippedWeaponId = sheet.EquippedMainWeaponId;
        sheet.EquippedMagicalItemIds = cg.EquippedMagicalItemIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        // Copy kit selection.
        if (!string.IsNullOrEmpty(cg.KitId))
        {
            var kit = _app.Rules.Kits.FirstOrDefault(k => k.Id == cg.KitId);
            sheet.KitId   = cg.KitId;
            sheet.KitName = kit?.Name ?? "";
        }
        else
        {
            sheet.KitId   = "";
            sheet.KitName = "";
        }
        sheet.KitFreeNwpIds = new List<string>(cg.KitFreeNwpIds);
        sheet.KitRequiredNwpIds = new List<string>(cg.KitRequiredNwpIds);

        var optionCatalog = _app.CharacterOptions.GetCatalog();
        sheet.NonweaponProficiencies = optionCatalog.NonweaponProficiencies
            .Where(x => cg.SelectedNonweaponProficiencyIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase))
            .Select(x => FormatNwpSummary(cg, x))
            .OrderBy(x => x)
            .ToList();
        sheet.Languages = cg.SelectedLanguages
            .Select(FormatLanguageSummary)
            .OrderBy(x => x)
            .ToList();
        sheet.Equipment = sheet.EquipmentSelections
            .OrderBy(x => x.Category)
            .ThenBy(x => x.ItemName)
            .Select(x => x.Quantity > 1 ? $"{x.ItemName} x{x.Quantity}" : x.ItemName)
            .ToList();
        CharacterArmorService.RecalculateArmorForCharacter(sheet, new EquipmentLibraryService().GetEquipmentLibrary());
        sheet.Traits = optionCatalog.Traits
            .Where(x => cg.SelectedTraitIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase))
            .Select(x => x.Name)
            .OrderBy(x => x)
            .ToList();
        sheet.Disadvantages = optionCatalog.Disadvantages
            .Where(x => cg.SelectedDisadvantageSeverities.ContainsKey(x.Id))
            .OrderBy(x => x.Name)
            .Select(x =>
            {
                string severity = cg.SelectedDisadvantageSeverities[x.Id];
                bool isSevere = string.Equals(severity, "severe", StringComparison.OrdinalIgnoreCase) && x.SevereBonus.HasValue;
                return $"{x.Name} [{(isSevere ? "Severe" : "Moderate")}]";
            })
            .ToList();

        if (string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
        {
            int startingPoolCp = (cg.IsLevelUpMode || cg.IsExistingCharacterMode)
                ? (cg.IsExistingCharacterMode
                    ? cg.GetExistingCharacterPointPool()
                    : Math.Max(0, cg.ExistingStartingUnspentCharacterPoints))
                : 0;
            int classCarryover = GetClassCarryoverCp(cg);
            int nwpBaseBudget = GetBaseNwpClassBudget(cg);
            int traitCost = GetSelectedTraitCost(cg, optionCatalog);
            int disadvantageBonus = GetSelectedDisadvantageBonus(cg, optionCatalog);
            int nwpSpent = CalculateTotalNwpCp(cg, optionCatalog.NonweaponProficiencies.ToList());

            var classIdsForWeaponCp = cg.SelectedClassIds.Count > 0
                ? cg.SelectedClassIds
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : string.IsNullOrWhiteSpace(cg.ClassId)
                    ? new List<string>()
                    : new List<string> { cg.ClassId };
            int intScore = cg.ModifiedAbilities.GetValueOrDefault("int", cg.Abilities.GetValueOrDefault("int", 10));
            int weaponBaseBudget = _app.Rules.GetWeaponCpBudget(classIdsForWeaponCp, intScore);
            int weaponSpent = GetEffectiveWeaponCpUsedForState(cg, classIdsForWeaponCp, cg.SelectedWeaponProficiencies);

            sheet.SpentNwpCharacterPoints = Math.Max(0, nwpSpent);
            sheet.SpentWeaponCharacterPoints = Math.Max(0, weaponSpent);
            sheet.UnspentCharacterPoints = Math.Max(
                0,
                startingPoolCp + weaponBaseBudget + nwpBaseBudget + classCarryover
                    + disadvantageBonus - traitCost - nwpSpent - weaponSpent);
        }
        else
        {
            sheet.SpentNwpCharacterPoints = 0;
            sheet.SpentWeaponCharacterPoints = 0;
            sheet.UnspentCharacterPoints = 0;
        }

        if (sheet.NonweaponProficiencies.Count > 0)
            sheet.Notes.Add("Nonweapon proficiencies: " + string.Join(", ", sheet.NonweaponProficiencies));
        if (sheet.Languages.Count > 0)
            sheet.Notes.Add("Languages: " + string.Join(", ", sheet.Languages));
        if (sheet.Equipment.Count > 0)
            sheet.Notes.Add("Equipment: " + string.Join(", ", sheet.Equipment));
        if (sheet.Traits.Count > 0)
            sheet.Notes.Add("Traits: " + string.Join(", ", sheet.Traits));
        if (sheet.Disadvantages.Count > 0)
            sheet.Notes.Add("Disadvantages: " + string.Join(", ", sheet.Disadvantages));

        // Persist full chargen selection state for future level-up flows.
        sheet.CpPerLevel = GetConfiguredCpPerLevel();
        sheet.SelectedSpheres = new Dictionary<string, string>(cg.SelectedSpheres, StringComparer.OrdinalIgnoreCase);
        sheet.SelectedWizardSchools = new Dictionary<string, bool>(cg.SelectedWizardSchools, StringComparer.OrdinalIgnoreCase);
        sheet.SelectedAbilitiesByClass = cg.SelectedAbilitiesByClass
            .ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value), StringComparer.OrdinalIgnoreCase);
        sheet.SelectedTraitIds = new List<string>(cg.SelectedTraitIds);
        sheet.SelectedDisadvantageSeverities = new Dictionary<string, string>(cg.SelectedDisadvantageSeverities, StringComparer.OrdinalIgnoreCase);
        sheet.SelectedLanguages = cg.SelectedLanguages
            .Select(l => new LanguageSelection { SourceKey = l.SourceKey, LanguageName = l.LanguageName })
            .ToList();
        sheet.SelectedNonweaponProficiencyImprovements = new Dictionary<string, int>(cg.SelectedNonweaponProficiencyImprovements, StringComparer.OrdinalIgnoreCase);
        sheet.RogueSkillArmorProfile = cg.RogueSkillArmorProfile;
        sheet.SelectedRogueSkillPoints = new Dictionary<string, int>(cg.SelectedRogueSkillPoints, StringComparer.OrdinalIgnoreCase);
        sheet.SpheresByClass = cg.SpheresByClass
            .ToDictionary(kv => kv.Key, kv => new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        sheet.SchoolsByClass = cg.SchoolsByClass
            .ToDictionary(kv => kv.Key, kv => new Dictionary<string, bool>(kv.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        sheet.WizardSpellbookIds = cg.WizardSpellbookIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        
        // DEBUG: Log spell data before filtering
        System.Diagnostics.Debug.WriteLine($"[CharGen] cg.WizardSpellbookIds count: {cg.WizardSpellbookIds?.Count ?? 0}");
        System.Diagnostics.Debug.WriteLine($"[CharGen] cg.WizardSpellLists count: {cg.WizardSpellLists?.Count ?? 0}");
        foreach (var list in cg.WizardSpellLists ?? new List<NamedSpellList>())
        {
            System.Diagnostics.Debug.WriteLine($"  List '{list.Name}' has {list.SpellIds?.Count ?? 0} spells");
        }
        
        sheet.WizardSpellLists = BuildSafeWizardSpellLists(cg.WizardSpellLists, sheet.WizardSpellbookIds);
        
        // DEBUG: Log spell data after filtering
        System.Diagnostics.Debug.WriteLine($"[SavedSheet] sheet.WizardSpellbookIds count: {sheet.WizardSpellbookIds?.Count ?? 0}");
        System.Diagnostics.Debug.WriteLine($"[SavedSheet] sheet.WizardSpellLists count: {sheet.WizardSpellLists?.Count ?? 0}");
        foreach (var list in sheet.WizardSpellLists ?? new List<NamedSpellList>())
        {
            System.Diagnostics.Debug.WriteLine($"  List '{list.Name}' has {list.SpellIds?.Count ?? 0} spells");
        }

        // Ensure wizards have at least one spellbook
        EnsureWizardHasDefaultSpellbook(sheet);

        _app.Characters.Add(sheet);
        _app.SaveCharacters();

        MessageBox.Show(
            $"{sheet.Name} the {sheet.RaceName} {sheet.ClassName} is ready for adventure!",
            "Character Created",
            MessageBoxButton.OK, MessageBoxImage.Information);

        int newCharacterIndex = _app.Characters.Count - 1;
        cg.Clear();
        NavigateAfterFinalise(newCharacterIndex);
    }

    private static List<NamedSpellList> BuildSafeWizardSpellLists(
        IEnumerable<NamedSpellList>? sourceLists,
        IReadOnlyCollection<string> validSpellbookIds)
    {
        if (sourceLists is null)
            return new List<NamedSpellList>();

        return sourceLists
            .Where(list => list is not null)
            .Select(list => new
            {
                Name = (list.Name ?? string.Empty).Trim(),
                SpellIds = list.SpellIds ?? new List<string>()
            })
            .Where(list => !string.IsNullOrWhiteSpace(list.Name))
            .Select(list => new NamedSpellList
            {
                Name = list.Name,
                SpellIds = list.SpellIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Where(id => validSpellbookIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                    .ToList()
            })
            .ToList();
    }

    private List<string> GetEffectiveClassIds(CharGenState cg)
    {
        if (cg.SelectedClassIds.Count > 0)
        {
            return cg.SelectedClassIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return string.IsNullOrWhiteSpace(cg.ClassId)
            ? new List<string>()
            : new List<string> { cg.ClassId };
    }

    private void NormalizeCoreClassAbilitySelections(CharGenState cg)
    {
        if (string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
            return;

        var classIds = GetEffectiveClassIds(cg);
        var byClass = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var classId in classIds)
        {
            if (!_app.Rules.Classes.TryGetValue(classId, out var cls))
                continue;

            byClass[classId] = cls.StructuredAbilities
                .Where(a => a.AutoGranted)
                .Select(a => a.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        cg.SelectedAbilitiesByClass = byClass;

        string primaryClassId = !string.IsNullOrWhiteSpace(cg.ClassId)
            ? cg.ClassId
            : classIds.FirstOrDefault() ?? string.Empty;
        cg.SelectedClassAbilityIds = byClass.TryGetValue(primaryClassId, out var primaryIds)
            ? new List<string>(primaryIds)
            : new List<string>();
    }

    private string BuildClassDisplayName(IEnumerable<string> classIds, string fallback)
    {
        var names = (classIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => _app.Rules.Classes.TryGetValue(id, out var cls) ? cls.Name : id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names.Count > 0 ? string.Join(" / ", names) : fallback;
    }

    private bool ConfirmCpPerLevelSelection()
    {
        if (!string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
            return true;

        int configuredCp = GetConfiguredCpPerLevel();
        if (configuredCp > 0)
            return true;

        var result = MessageBox.Show(
            "CP / Level is still set to 0, which means the game will ask you for Character Points at each level-up.\n\nWould you like to set a default number now?",
            "CP / Level Reminder",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
            return false;

        if (result == MessageBoxResult.Yes)
        {
            CpPerLevelInput.Focus();
            CpPerLevelInput.SelectAll();
            MessageBox.Show(
                "Enter the CP / Level value, then click Finalise again.",
                "CP / Level",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        return true;
    }

    private static string FormatNwpSummary(CharGenState cg, NonweaponProficiencyDefinition proficiency)
    {
        bool isPo = string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);
        int cpSpent = isPo && cg.SelectedNonweaponProficiencyImprovements.TryGetValue(proficiency.Id, out int cp)
            ? Math.Max(0, cp)
            : 0;
        // Player's Option improvements are 1 CP per +1, regardless of the initial purchase cost.
        // This formatter is also used when saving new and updated character summaries.
        int improvement = cpSpent;
        int familyScore = GetReviewNwpFamilyScore(cg, proficiency);
        int rangerTrackingBonus = RangerTrackingRules.GetBonus(cg, proficiency);
        int synergyBonus = isPo ? NwpSynergyRules.GetPermanentBonus(cg, proficiency) : 0;
        int target = isPo
            ? Math.Max(0, proficiency.PlayersOptionBaseRating) + GetReviewTable44Modifier(familyScore) + improvement + rangerTrackingBonus + synergyBonus
            : familyScore + proficiency.CheckModifier + improvement + rangerTrackingBonus;
        int purchaseCp = NwpGrantRules.GetAcquisitionCost(cg, proficiency);
        int totalCp = purchaseCp + cpSpent;
        string cpSummary = purchaseCp == 0
            ? $"{totalCp} CP improvement; granted by {(cg.KitFreeNwpIds.Contains(proficiency.Id, StringComparer.OrdinalIgnoreCase) ? "kit" : "class")}"
            : cpSpent > 0
                ? $"{totalCp} CP: {purchaseCp} purchase + {cpSpent} improvement"
                : $"{totalCp} CP purchase";
        string summary = isPo
            ? $"{proficiency.Name} {target} ({cpSummary})"
            : $"{proficiency.Name} {target}";
        if (RangerTrackingRules.IsTracking(proficiency.Id, proficiency.Name))
            summary += RangerTrackingRules.GetConditionalTargetSummary(cg, proficiency, target);
        else if (isPo)
        {
            string conditional = NwpSynergyRules.GetConditionalSummary(cg, proficiency, target);
            if (!string.IsNullOrEmpty(conditional))
                summary += "; conditional alternatives: " + conditional.Replace(Environment.NewLine, "; ");
        }

        if (cg.SelectedNonweaponProficiencyNotes.TryGetValue(proficiency.Id, out var note)
            && !string.IsNullOrWhiteSpace(note))
        {
            summary += $" [Text: {note}]";
        }

        return summary;
    }

    private bool IsWizardCasterInCharGen()
    {
        var classIds = _app.CharGen.SelectedClassIds.Count > 0
            ? _app.CharGen.SelectedClassIds
            : string.IsNullOrWhiteSpace(_app.CharGen.ClassId)
                ? new List<string>()
                : new List<string> { _app.CharGen.ClassId };

        return classIds.Any(IsWizardClassId);
    }

    private static bool IsWizardClassId(string classId)
    {
        return string.Equals(classId, "wizard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "mage", StringComparison.OrdinalIgnoreCase)
            || string.Equals(classId, "illusionist", StringComparison.OrdinalIgnoreCase);
    }

    private string GetSelectedGenderValue()
    {
        if (SumGender.SelectedItem is ComboBoxItem item)
            return NormalizeGenderToken(item.Tag as string ?? item.Content?.ToString());

        return string.Empty;
    }

    private void SelectGenderComboValue(string? raw)
    {
        string target = NormalizeGenderToken(raw);
        if (string.IsNullOrWhiteSpace(target))
        {
            SumGender.SelectedIndex = -1;
            return;
        }

        for (int i = 0; i < SumGender.Items.Count; i++)
        {
            if (SumGender.Items[i] is not ComboBoxItem item)
                continue;

            string itemValue = NormalizeGenderToken(item.Tag as string ?? item.Content?.ToString());
            if (string.Equals(itemValue, target, StringComparison.OrdinalIgnoreCase))
            {
                SumGender.SelectedIndex = i;
                return;
            }
        }

        SumGender.SelectedIndex = -1;
    }

    private static string NormalizeGenderToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string value = raw.Trim().ToLowerInvariant();
        return value switch
        {
            "male" or "m" => "male",
            "female" or "f" => "female",
            "other" or "o" or "nonbinary" or "non-binary" => "other",
            _ => string.Empty,
        };
    }

    private static string FormatLanguageSummary(LanguageSelection language)
    {
        string source = language.SourceKey switch
        {
            "free_spoken" => "Free Spoken Language",
            "modern_languages" => "Modern Languages",
            "ancient_languages" => "Ancient Languages",
            "animal_languages" => "Creature Languages (Druid)",
            "reading_writing" => "Reading/Writing",
            _ => language.SourceKey,
        };

        return $"{language.LanguageName} [{source}]";
    }

    private static int GetReviewNwpFamilyScore(CharGenState cg, NonweaponProficiencyDefinition proficiency)
    {
        string checkAbility = GetReviewActiveCheckAbility(cg, proficiency);

        if (cg.CharacterMode == "players_option"
            && cg.SubAbilities.Count > 0
            )
        {
            if (cg.SubAbilities.TryGetValue(checkAbility, out int directScore))
                return directScore;

            if (NwpAbilityFamilyMap.TryGetValue(checkAbility, out var subAbilityKeys))
            {
                var scores = subAbilityKeys
                    .Select(key => cg.SubAbilities.TryGetValue(key, out int score) ? score : (int?)null)
                    .Where(score => score.HasValue)
                    .Select(score => score!.Value)
                    .ToList();
                if (scores.Count > 0)
                    return (int)Math.Round(scores.Average(), MidpointRounding.AwayFromZero);
            }
        }

        return checkAbility switch
        {
            "Strength" => cg.ModifiedAbilities.GetValueOrDefault("str", cg.Abilities.GetValueOrDefault("str", 10)),
            "Dexterity" => cg.ModifiedAbilities.GetValueOrDefault("dex", cg.Abilities.GetValueOrDefault("dex", 10)),
            "Constitution" => cg.ModifiedAbilities.GetValueOrDefault("con", cg.Abilities.GetValueOrDefault("con", 10)),
            "Intelligence" => cg.ModifiedAbilities.GetValueOrDefault("int", cg.Abilities.GetValueOrDefault("int", 10)),
            "Wisdom" => cg.ModifiedAbilities.GetValueOrDefault("wis", cg.Abilities.GetValueOrDefault("wis", 10)),
            "Charisma" => cg.ModifiedAbilities.GetValueOrDefault("cha", cg.Abilities.GetValueOrDefault("cha", 10)),
            _ => 10,
        };
    }

    private static string GetReviewActiveCheckAbility(CharGenState cg, NonweaponProficiencyDefinition proficiency)
    {
        if (string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(proficiency.PlayersOptionCheckAbility))
        {
            return proficiency.PlayersOptionCheckAbility;
        }

        return proficiency.CheckAbility;
    }

    private static int GetReviewTable44Modifier(int abilityScore) => abilityScore switch
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

    private int GetClassCarryoverCp(CharGenState cg)
    {
        if (!string.Equals(cg.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
            return 0;

        var classIds = cg.SelectedClassIds.Count > 0
            ? cg.SelectedClassIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : string.IsNullOrWhiteSpace(cg.ClassId)
                ? new List<string>()
                : new List<string> { cg.ClassId };

        int totalRemaining = 0;
        foreach (var classId in classIds)
        {
            var selectedAbilityIds = cg.SelectedAbilitiesByClass.TryGetValue(classId, out var ids) && ids.Count > 0
                ? ids
                : string.Equals(classId, cg.ClassId, StringComparison.OrdinalIgnoreCase)
                    ? cg.SelectedClassAbilityIds
                    : new List<string>();
            var specializationId = cg.WizardSpecializationById.TryGetValue(classId, out var spec) && !string.IsNullOrWhiteSpace(spec)
                ? spec
                : string.Equals(classId, cg.ClassId, StringComparison.OrdinalIgnoreCase)
                    ? cg.WizardSpecializationId
                    : string.Empty;

            var classPackage = _app.Rules.BuildClassAbilityPackage(
                classId,
                selectedAbilityIds,
                cg.RacialCarryoverToClassPoints,
                specializationId,
                Math.Max(1, cg.CharacterLevel),
                cg.RaceId);

            var spheres = cg.SpheresByClass.TryGetValue(classId, out var sp) && sp.Count > 0
                ? sp
                : string.Equals(classId, cg.ClassId, StringComparison.OrdinalIgnoreCase)
                    ? cg.SelectedSpheres
                    : new Dictionary<string, string>();
            var schools = cg.SchoolsByClass.TryGetValue(classId, out var sc) && sc.Count > 0
                ? sc
                : string.Equals(classId, cg.ClassId, StringComparison.OrdinalIgnoreCase)
                    ? cg.SelectedWizardSchools
                    : new Dictionary<string, bool>();

            int extraCost = _app.Rules.Classes.TryGetValue(classId, out var classDefinition)
                ? CharGenClassAbilitiesScreen.CalculateConfigurationCpCost(classDefinition, spheres, schools, specializationId)
                : 0;

            totalRemaining += Math.Max(0, classPackage.remaining - extraCost);
        }

        return Math.Max(0, totalRemaining);
    }

    private int GetBaseNwpClassBudget(CharGenState cg)
    {
        var classIds = cg.SelectedClassIds.Count > 0
            ? cg.SelectedClassIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : string.IsNullOrWhiteSpace(cg.ClassId)
                ? new List<string>()
                : new List<string> { cg.ClassId };
        int intScore = cg.ModifiedAbilities.GetValueOrDefault("int", cg.Abilities.GetValueOrDefault("int", 10));
        int fullBudget = _app.Rules.GetNwpCpBudget(classIds, intScore);
        int intBonus = RulesEngine.GetIntBonusCps(intScore);
        return Math.Max(0, fullBudget - intBonus);
    }

    private static int GetSelectedTraitCost(CharGenState cg, CharacterOptionCatalog catalog)
    {
        var selectedIds = cg.SelectedTraitIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return catalog.Traits
            .Where(x => selectedIds.Contains(x.Id))
            .Sum(x => x.Cost);
    }

    private static int GetSelectedDisadvantageBonus(CharGenState cg, CharacterOptionCatalog catalog)
    {
        return catalog.Disadvantages
            .Where(x => cg.SelectedDisadvantageSeverities.ContainsKey(x.Id))
            .Sum(x =>
            {
                string severity = cg.SelectedDisadvantageSeverities[x.Id];
                bool isSevere = string.Equals(severity, "severe", StringComparison.OrdinalIgnoreCase);
                return isSevere && x.SevereBonus.HasValue ? x.SevereBonus.Value : x.ModerateBonus;
            });
    }

    private static int CalculateTotalNwpCp(CharGenState cg, List<NonweaponProficiencyDefinition> definitions)
    {
        return CalculateTotalNwpCp(cg.SelectedNonweaponProficiencyIds,
            cg.SelectedNonweaponProficiencyImprovements, definitions, cg);
    }

    private static int CalculateTotalNwpCp(
        List<string> selectedIds,
        Dictionary<string, int> improvements,
        List<NonweaponProficiencyDefinition> definitions,
        CharGenState cg)
    {
        return NwpGrantRules.GetTotalBaseCost(selectedIds, improvements, definitions, cg);
    }

    private static List<string> GetAddedIds(List<string> current, List<string> baseline)
    {
        var baselineCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in baseline)
            baselineCounts[id] = baselineCounts.GetValueOrDefault(id) + 1;

        var added = new List<string>();
        foreach (string id in current)
        {
            int remaining = baselineCounts.GetValueOrDefault(id);
            if (remaining > 0)
            {
                baselineCounts[id] = remaining - 1;
                continue;
            }

            added.Add(id);
        }

        return added;
    }

    private static int SumEquipmentCostCopper(IEnumerable<EquipmentSelection>? selections)
    {
        if (selections is null)
            return 0;

        long total = 0;
        foreach (var selection in selections)
        {
            int quantity = Math.Max(1, selection?.Quantity ?? 1);
            int unitCostCopper = ResolveEquipmentUnitCostCopper(selection);
            total += (long)quantity * unitCostCopper;
            if (total > int.MaxValue)
                return int.MaxValue;
        }

        return (int)Math.Max(0, total);
    }

    private static int ResolveEquipmentUnitCostCopper(EquipmentSelection? selection)
    {
        if (selection is null)
            return 0;

        int explicitCostCopper = CharacterWealthService.ToCopper(
            0,
            Math.Max(0, selection.CostGoldEach),
            Math.Max(0, selection.CostSilverEach),
            Math.Max(0, selection.CostCopperEach));
        if (explicitCostCopper > 0)
            return explicitCostCopper;

        return TryParseCostTextToCopper(selection.CostText, out int parsedCopper)
            ? parsedCopper
            : 0;
    }

    private static bool TryParseCostTextToCopper(string? text, out int copper)
    {
        copper = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string normalized = CoinRangeRegex.Replace(text, m =>
        {
            string high = m.Groups["high"].Value;
            string coin = m.Groups["coin"].Value;
            return $"{high}{coin}";
        });

        long total = 0;
        bool foundCoinValue = false;
        foreach (Match match in CoinValueRegex.Matches(normalized))
        {
            if (!TryParsePositiveInt(match.Groups["value"].Value, out int value))
                continue;

            string coin = match.Groups["coin"].Value;
            int multiplier = coin.Equals("pp", StringComparison.OrdinalIgnoreCase) ? 500
                : coin.Equals("gp", StringComparison.OrdinalIgnoreCase) ? 100
                : coin.Equals("ep", StringComparison.OrdinalIgnoreCase) ? 50
                : coin.Equals("sp", StringComparison.OrdinalIgnoreCase) ? 10
                : 1;
            total += (long)value * multiplier;
            foundCoinValue = true;
        }

        if (foundCoinValue)
        {
            copper = total > int.MaxValue ? int.MaxValue : (int)Math.Max(0, total);
            return copper > 0;
        }

        var bareRange = BareRangeRegex.Match(text);
        if (bareRange.Success
            && TryParsePositiveInt(bareRange.Groups["high"].Value, out int rangeHighGp)
            && rangeHighGp > 0)
        {
            copper = rangeHighGp > (int.MaxValue / 100) ? int.MaxValue : rangeHighGp * 100;
            return true;
        }

        var bareNumber = BareNumberRegex.Match(text);
        if (bareNumber.Success
            && TryParsePositiveInt(bareNumber.Value, out int gp)
            && gp > 0)
        {
            copper = gp > (int.MaxValue / 100) ? int.MaxValue : gp * 100;
            return true;
        }

        return false;
    }

    private static bool TryParsePositiveInt(string value, out int parsed)
    {
        parsed = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Replace(",", string.Empty).Trim();
        return int.TryParse(normalized, out parsed) && parsed >= 0;
    }

    /// <summary>
    /// Ensures a wizard character has at least one default spellbook.
    /// If the character is a wizard and has no spellbooks, creates a Standard Spellbook.
    /// </summary>
    private void EnsureWizardHasDefaultSpellbook(CharacterSheet sheet)
    {
        if (sheet == null)
            return;

        bool isWizard = IsWizardClassId(sheet.ClassId)
            || (sheet.ClassIds?.Any(IsWizardClassId) ?? false);

        if (!isWizard)
            return;

        if (sheet.WizardSpellbooks == null)
            sheet.WizardSpellbooks = new List<Models.WizardSpellbook>();
        if (sheet.WizardSpellbookIds == null)
            sheet.WizardSpellbookIds = new List<string>();

        if (sheet.WizardSpellbooks.Count == 0)
            sheet.WizardSpellbooks.Add(SpellbookUtility.CreateSpellbook(SpellbookUtility.TYPE_STANDARD, "Spellbook 1"));

        foreach (var book in sheet.WizardSpellbooks)
        {
            book.SpellPages ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(book.Type))
                book.Type = SpellbookUtility.TYPE_STANDARD;

            if (SpellbookUtility.TryGetSpellbookInfo(book.Type, out int pages, out double weight, out string dimensions))
            {
                if (book.CapacityPages <= 0)
                    book.CapacityPages = pages;
                if (book.WeightLbs <= 0)
                    book.WeightLbs = weight;
                if (string.IsNullOrWhiteSpace(book.Dimensions))
                    book.Dimensions = dimensions;
            }
        }

        foreach (string spellId in sheet.WizardSpellbookIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            bool alreadyTracked = sheet.WizardSpellbooks.Any(book => book.SpellPages.ContainsKey(spellId));
            if (alreadyTracked)
                continue;

            var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, spellId, StringComparison.OrdinalIgnoreCase));
            int spellLevel = ParseSpellLevelLoose(spell?.Level);
            int pages = SpellbookUtility.GetDefaultPageCount(spellLevel);

            var targetBook = sheet.WizardSpellbooks.FirstOrDefault(book => SpellbookUtility.CanAddSpellToBook(book, pages));
            if (targetBook is null)
            {
                targetBook = SpellbookUtility.CreateSpellbook(
                    SpellbookUtility.TYPE_STANDARD,
                    $"Spellbook {sheet.WizardSpellbooks.Count + 1}");
                sheet.WizardSpellbooks.Add(targetBook);
            }

            targetBook.SpellPages[spellId] = pages;
        }
    }

    private static int ParseSpellLevelLoose(string? levelText)
    {
        if (string.IsNullOrWhiteSpace(levelText))
            return 1;

        if (levelText.Equals("Cantrip", StringComparison.OrdinalIgnoreCase))
            return 0;

        var digits = new string(levelText.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out int parsed) ? Math.Max(0, parsed) : 1;
    }

    // Returns "cleric_warrior" when the cleric has Warrior Priests, so CalculateTotals
    // applies the warrior CON HP bonus (+3 at CON 17, +4 at CON 18+).
    private static string GetEffectiveClassIdForConBonus(CharGenState cg)
    {
        if (string.Equals(cg.ClassId, "cleric", StringComparison.OrdinalIgnoreCase))
        {
            bool hasWarriorPriests = cg.SelectedClassAbilityIds.Any(entry =>
                string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry),
                    "cleric_warrior_priests", StringComparison.OrdinalIgnoreCase));
            if (hasWarriorPriests)
                return "cleric_warrior";
        }
        return cg.ClassId;
    }
}


public class MechanicsLineViewModel
{
    public string Text { get; set; } = "";
    public Brush  LineColor { get; set; } = Brushes.White;
}
