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
    private const string WeaponMasteryId = "weapon_mastery";

    public record CombatOptionDefinition(
        string Id,
        string Name,
        string Category, // "Armor", "Shield", "Style", "Talent"
        int SlotCost,
        int CpCost,
        string Description,
        string RelevantAbility = "",
        int InitialRating = 0,
        string AllowedGroups = "All");

    private static readonly CombatOptionDefinition[] CombatOptions =
    {
        // ── Armor & Shields ──────────────────────────────────────────────────
        new("armor_proficiency_light", "Armor Proficiency (Light)", "Armor", 1, 2,
            "A character who spends a proficiency slot on light armor learns to move efficiently in it, suffering only one-half the normal encumbrance of light armor. Under Player's Option rules, this also allows non-armor classes (such as wizards) to wear light armor without penalty.",
            AllowedGroups: "All"),
        new("armor_proficiency_medium", "Armor Proficiency (Medium)", "Armor", 1, 2,
            "A character who spends a proficiency slot on medium armor learns to move efficiently in it, suffering only one-half the normal encumbrance of medium armor. Under Player's Option rules, this also allows non-armor classes (such as wizards) to wear medium armor without penalty.",
            AllowedGroups: "All"),
        new("armor_proficiency_heavy", "Armor Proficiency (Heavy)", "Armor", 1, 2,
            "A character who spends a proficiency slot on heavy armor learns to move efficiently in it, suffering only one-half the normal encumbrance of heavy armor. Under Player's Option rules, this also allows non-armor classes (such as wizards) to wear heavy armor without penalty.",
            AllowedGroups: "All"),
        new("shield_proficiency", "Shield Proficiency", "Shield", 1, 2,
            "Increases the protective value of shields when held: Buckler +1 AC (1 attacker), Small Shield +2 AC (2 attackers), Medium Shield +3 AC (3 attackers), Body Shield +3 AC (+4 vs missiles, 4 attackers). Also allows shield use for classes normally restricted.",
            AllowedGroups: "All"),

        // ── Fighting Styles ──────────────────────────────────────────────────
        new("fighting_style_single", "Single-Weapon Style", "Style", 1, 2,
            "When fighting with a one-handed weapon and an empty off-hand, gain a +1 bonus to Armor Class (+2 if a second slot is invested). Off-hand remains free to punch, grab, or switch to two-handed if the weapon allows.",
            AllowedGroups: "All"),
        new("fighting_style_two_weapon", "Two-Weapon Style", "Style", 1, 2,
            "Reduces penalties for fighting with a weapon in each hand from -2/-4 down to 0/-2 (or 0/0 for ambidextrous characters). Rangers receive the first slot free. A second slot allows using two weapons of equal size.",
            AllowedGroups: "Warrior, Rogue"),
        new("fighting_style_two_handed", "Two-Handed Style", "Style", 1, 2,
            "Increases the speed of two-handed weapons by one category (slow to average, average to fast; or -3 speed factor). Also grants +1 damage when wielding versatile one-handed weapons in two hands.",
            AllowedGroups: "Warrior, Priest, Rogue"),
        new("fighting_style_missile", "Missile Style", "Style", 1, 2,
            "Move up to half movement with full rate of fire, or full move at half rate of fire. Grants -1 AC bonus against enemy missile fire while attacking with ranged weapons.",
            AllowedGroups: "All"),
        new("fighting_style_sword_shield", "Sword and Shield Style", "Style", 1, 2,
            "Allows making a secondary shield attack (rush, punch, block, trap) every round without forfeiting the shield's Armor Class protection.",
            AllowedGroups: "Warrior, Priest, Rogue"),

        // ── Special Talents (Player's Option: Combat & Tactics, pp. 80–82) ──
        new("talent_alertness", "Alertness", "Talent", 1, 6,
            "Unnaturally alert and instinctively notes signs of trouble that others miss. Reduces chance of being surprised by 1 in 10 on a successful proficiency check. In situations where surprise is automatic, a successful check allows being surprised only normally.",
            RelevantAbility: "Wisdom / Intuition (+1)", AllowedGroups: "All"),
        new("talent_ambidexterity", "Ambidexterity", "Talent", 1, 4,
            "Equally skilled with either hand; neither right-handed nor left-handed. When fighting in two-weapon style, has two primary hands and suffers only -2 to hit with either weapon (-2/-2 instead of -2/-4), and no penalty at all (0/0) if specialized in two-weapon style.",
            RelevantAbility: "Dexterity / Aim", AllowedGroups: "Warrior, Rogue"),
        new("talent_ambush", "Ambush", "Talent", 1, 4,
            "Skilled at laying ambushes and setting up surprise attacks even where terrain does not normally favor it. Unless victims spot the ambushers beforehand, surprise is guaranteed.",
            RelevantAbility: "Intelligence / Reason", InitialRating: 5, AllowedGroups: "Warrior, Rogue"),
        new("talent_camouflage", "Camouflage", "Talent", 1, 4,
            "Stay out of sight in natural surroundings using cover and preparation. On a successful check, considered effectively invisible as long as stationary (-1 bonus to surprise enemies). Grants Rangers and Thieves +40% to Hide in Shadows when combined.",
            RelevantAbility: "Intelligence / Knowledge", InitialRating: 5, AllowedGroups: "Warrior, Rogue"),
        new("talent_dirty_fighting", "Dirty Fighting", "Talent", 1, 3,
            "Repertoire of feints, ruses, and unsportsmanlike tactics. Can attempt once per fight; on a success, gains +1 bonus (+2 if enemy believes character fights honorably) to the next attack roll.",
            RelevantAbility: "Intelligence / Knowledge", InitialRating: 5, AllowedGroups: "Warrior, Rogue"),
        new("talent_endurance", "Endurance", "Talent", 2, 4,
            "Perform strenuous physical activity twice as long before fatigue sets in. If fatigue rules are in play, increases fatigue points by 50%.",
            RelevantAbility: "Constitution / Fitness", InitialRating: 3, AllowedGroups: "Warrior"),
        new("talent_fine_balance", "Fine Balance", "Talent", 2, 5,
            "Innate sense of balance and uncanny knack for staying on feet. With a successful check, gains +2 on climbing checks, saving throws, or ability checks to avoid slipping or falling, and reduces off-balance fighting penalties by 2 points (+2 on d20 / +10% on d100 for balance-related feats).",
            RelevantAbility: "Dexterity / Balance", InitialRating: 7, AllowedGroups: "Warrior, Rogue"),
        new("talent_iron_will", "Iron Will", "Talent", 2, 6,
            "Amazing drive to push through injury and exhaustion. Grants +1 bonus to saving throws vs mind-affecting spells and effects (charms, holds, hypnotism, suggestion). Also allows rolling a save vs death each round to remain conscious and fighting at negative HP.",
            RelevantAbility: "Wisdom / Willpower (-2)", InitialRating: 3, AllowedGroups: "Warrior, Priest"),
        new("talent_leadership", "Leadership", "Talent", 1, 3,
            "Motivate troops and inspire men. Military units led by the hero gain +2 to morale checks. In mass combat, commands troops as if 3 levels higher.",
            RelevantAbility: "Charisma / Leadership (-1)", InitialRating: 5, AllowedGroups: "Warrior"),
        new("talent_quickness", "Quickness", "Talent", 2, 6,
            "Unusually fast hand-eye coordination. In combat, gains a -2 bonus to initiative rolls on a successful proficiency check when moving or attacking with weapons of average speed or faster. Does not apply to slow weapons, guarding, or parrying.",
            RelevantAbility: "Dexterity / Aim", InitialRating: 3, AllowedGroups: "Warrior, Rogue"),
        new("talent_steady_hand", "Steady Hand", "Talent", 1, 5,
            "Excellent distance judgment and smooth aim with bows and crossbows. Taking a full round to aim eliminates the medium-range penalty and reduces the long-range penalty to -2 (forfeits multiple attacks to make one careful shot).",
            RelevantAbility: "Dexterity / Aim", AllowedGroups: "Warrior, Rogue"),
        new("talent_trouble_sense", "Trouble Sense", "Talent", 1, 4,
            "Instinctive danger sense for detecting unnoticed threats. On a successful secret DM check, only surprised on a 1 by sneak attacks, and treats rear attacks as flank attacks instead.",
            RelevantAbility: "Wisdom / Intuition", InitialRating: 3, AllowedGroups: "All")
    };

    private static CombatOptionDefinition? FindCombatOption(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var direct = CombatOptions.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (direct is not null) return direct;

        string alt = id.StartsWith("talent_", StringComparison.OrdinalIgnoreCase)
            ? id["talent_".Length..]
            : $"talent_{id}";

        return CombatOptions.FirstOrDefault(x => string.Equals(x.Id, alt, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly HashSet<string> ClericRestrictedWeaponIds =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "quarterstaff",
            "club",
            "warhammer",
            "horsemans_mace",
            "horsemans_flail",
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
            : "Character Blueprint  ›  Weapon Proficiencies");
        _catalog = new CharacterOptionCatalogService().GetCatalog();
        bool isPO = _app.CharGen.CharacterMode == "players_option";

        // Step numbering: equipment now sits between weapon proficiencies and review.
        // Core: step 7 of 9; PO no-wizard: step 10 of 12; PO wizard: step 11 of 13
        bool isWizardPO = isPO && string.Equals(_app.CharGen.ClassId, "wizard", StringComparison.OrdinalIgnoreCase);
        bool hasWizardSpecs = _app.Rules.Classes.TryGetValue("wizard", out var wc) && wc.Specializations is { Count: > 0 };
        int stepTotal = isPO
            ? (isWizardPO && hasWizardSpecs ? 13 : 12)
            : 9;
        int stepCurrent = stepTotal - 2;

        _app.SetNavBar(stepCurrent, stepTotal, "Weapon Proficiencies",
            backAction: () => _app.GoTo("chargen_character_options", -1),
            nextAction: Advance);

        EnforceClericWeaponRestrictionsOnExistingSelections();

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
            return _app.CharGen.IsLevelUpMode
                ? GetCarryoverFromNwpStage()
                : GetWeaponCpBudget() + GetCarryoverFromNwpStage();

        var classIds = GetEffectiveClassIds();
        int level = Math.Max(1, _app.CharGen.CharacterLevel);

        if (classIds.Count == 0)
            return _app.Rules.GetWeaponProficiencySlots("fighter", level);

        return classIds
            .Select(id => _app.Rules.GetWeaponProficiencySlots(id, level))
            .Max();
    }

    private int GetSlotsUsed()
    {
        int currentCost = _app.CharGen.SelectedWeaponProficiencies.Sum(GetSelectionTotalCost);

        if (_app.CharGen.IsLevelUpMode)
        {
            int baselineCost = _app.CharGen.BaselineWeaponProficiencies.Sum(GetSelectionTotalCost);
            return Math.Max(0, currentCost - baselineCost);
        }

        return currentCost;
    }

    private int GetCharacterLevel() => Math.Max(1, _app.CharGen.CharacterLevel);

    private int GetProficiencyCost(string profType, string proficiencyId, bool specialized = false)
    {
        int baseCost = IsPlayersOptionMode()
            ? _app.Rules.GetWeaponProficiencyCpCost(GetEffectiveClassIds(), proficiencyId, profType, specialized, GetCharacterLevel())
            : RulesEngine.GetWeaponProficiencySlotCost(profType) + (specialized ? 1 : 0);

        if (baseCost <= 0
            && IsPlayersOptionMode()
            && specialized
            && string.Equals(profType, "individual", StringComparison.OrdinalIgnoreCase)
            && IsWizardOnlyCharacter()
            && HasSelectedClassAbility("wizard_weapon_specialization"))
        {
            int nonSpecializedCost = _app.Rules.GetWeaponProficiencyCpCost(
                GetEffectiveClassIds(),
                proficiencyId,
                profType,
                specialized: false,
                GetCharacterLevel());
            if (nonSpecializedCost > 0)
            {
                // Table 53 wizard specialization surcharge.
                baseCost = nonSpecializedCost + 10;
            }
        }

        if (baseCost <= 0)
            return baseCost;

        if (HasFighterProficiencyEase())
            return Math.Max(1, (baseCost + 1) / 2);

        return baseCost;
    }

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

    private HashSet<string> GetSelectedClassAbilityBaseIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string entry in _app.CharGen.SelectedClassAbilityIds)
        {
            string id = RulesEngine.ExtractClassAbilityBaseId(entry);
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id);
        }

        foreach (var byClass in _app.CharGen.SelectedAbilitiesByClass.Values)
        {
            foreach (string entry in byClass)
            {
                string id = RulesEngine.ExtractClassAbilityBaseId(entry);
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id);
            }
        }

        return ids;
    }

    private IEnumerable<string> GetSelectedClassAbilityEntries()
    {
        foreach (string entry in _app.CharGen.SelectedClassAbilityIds)
            yield return entry;

        foreach (var byClass in _app.CharGen.SelectedAbilitiesByClass.Values)
        {
            foreach (string entry in byClass)
                yield return entry;
        }
    }

    private bool HasSelectedClassAbility(params string[] abilityIds)
    {
        if (abilityIds is null || abilityIds.Length == 0)
            return false;

        var wanted = new HashSet<string>(abilityIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
            return false;

        return GetSelectedClassAbilityEntries()
            .Select(RulesEngine.ExtractClassAbilityBaseId)
            .Any(wanted.Contains);
    }

    private HashSet<string> GetWizardListedWeaponRestrictionIds()
    {
        var allowedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string entry in GetSelectedClassAbilityEntries())
        {
            string baseId = RulesEngine.ExtractClassAbilityBaseId(entry);
            if (!string.Equals(baseId, "wizard_restriction_weapons_restriction_cp3", StringComparison.OrdinalIgnoreCase))
                continue;

            string playerText = RulesEngine.ExtractClassAbilityPlayerText(entry);
            if (string.IsNullOrWhiteSpace(playerText))
                continue;

            foreach (string rawToken in playerText.Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string token = rawToken.Trim();
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                var byId = _app.Rules.WeaponById.Keys.FirstOrDefault(id => string.Equals(id, token, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(byId))
                {
                    allowedIds.Add(byId);
                    continue;
                }

                var byName = _app.Rules.WeaponById.Values
                    .FirstOrDefault(w => string.Equals(w.Name, token, StringComparison.OrdinalIgnoreCase));
                if (byName is not null)
                    allowedIds.Add(byName.Id);
            }
        }

        return allowedIds;
    }

    private bool HasNoWeaponProficiencyRestriction()
        => GetSelectedClassAbilityBaseIds().Contains("cleric_restriction_weapon_restriction_cp15")
            || GetSelectedClassAbilityBaseIds().Contains("druid_restriction_weapon_restriction_cp15")
            || HasSelectedClassAbility("wizard_restriction_weapons_restriction_cp5");

    private bool HasLimitedPriestWeaponRestriction()
        => GetSelectedClassAbilityBaseIds().Contains("cleric_restriction_weapon_restriction_cp5")
            || GetSelectedClassAbilityBaseIds().Contains("druid_restriction_weapon_restriction_cp5")
            || GetSelectedClassAbilityBaseIds().Contains("paladin_restriction_limited_weapon_selection")
            || GetSelectedClassAbilityBaseIds().Contains("ranger_restriction_limited_weapon_selection");

    private bool HasWizardListedWeaponRestriction()
        => HasSelectedClassAbility("wizard_restriction_weapons_restriction_cp3");

    private bool HasFighterProficiencyEase()
        => GetSelectedClassAbilityBaseIds().Contains("fighter_proficiency_ease");

    private static bool IsWizardWeaponClassId(string classId)
    {
        string key = (classId ?? string.Empty).Trim();
        return string.Equals(key, "wizard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "mage", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "illusionist", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsWizardOnlyCharacter()
    {
        var classes = GetEffectiveClassIds();
        return classes.Count > 0 && classes.All(IsWizardWeaponClassId);
    }

    private bool HasWizardRogueWeaponSelection()
        => HasSelectedClassAbility("wizard_weapon_selection_cp10");

    private bool HasWizardClericWeaponSelection()
        => HasSelectedClassAbility("wizard_weapon_selection_cp15");

    private bool CanWizardSelectWeaponByAbility(string? weaponId)
    {
        if (!IsPlayersOptionMode() || !IsWizardOnlyCharacter())
            return true;

        if (string.IsNullOrWhiteSpace(weaponId))
            return false;

        int wizardCost = _app.Rules.GetWeaponProficiencyCpCost(
            new[] { "wizard" },
            weaponId,
            "individual",
            specialized: false,
            GetCharacterLevel());

        if (wizardCost <= 0)
            return false;

        if (wizardCost == 3)
            return true;

        bool isClericWeapon = ClericRestrictedWeaponIds.Contains(weaponId);
        if (isClericWeapon)
            return HasWizardClericWeaponSelection();

        if (wizardCost == 5)
            return HasWizardRogueWeaponSelection();

        return false;
    }

    private bool IsWeaponAllowedForClassRestriction(string weaponId)
    {
        if (HasNoWeaponProficiencyRestriction())
            return false;

        if (HasWizardListedWeaponRestriction())
        {
            var allowed = GetWizardListedWeaponRestrictionIds();
            return allowed.Contains(weaponId ?? string.Empty);
        }

        if (!HasLimitedPriestWeaponRestriction())
            return CanWizardSelectWeaponByAbility(weaponId);

        return ClericRestrictedWeaponIds.Contains(weaponId ?? string.Empty)
            && CanWizardSelectWeaponByAbility(weaponId);
    }

    private void EnforceClericWeaponRestrictionsOnExistingSelections()
    {
        if (_app.CharGen.SelectedWeaponProficiencies.Count == 0)
            return;

        bool noWeapons = HasNoWeaponProficiencyRestriction();
        bool limitedWeapons = HasLimitedPriestWeaponRestriction();
        bool wizardListedWeapons = HasWizardListedWeaponRestriction();
        bool wizardSelectionGated = IsPlayersOptionMode() && IsWizardOnlyCharacter();
        var wizardAllowed = GetWizardListedWeaponRestrictionIds();
        if (!noWeapons && !limitedWeapons && !wizardListedWeapons && !wizardSelectionGated)
            return;

        int removed = _app.CharGen.SelectedWeaponProficiencies.RemoveAll(sel =>
        {
            if (noWeapons)
                return true;

            if (wizardListedWeapons)
            {
                return !string.Equals(sel.ProficiencyType, "individual", StringComparison.OrdinalIgnoreCase)
                    || !wizardAllowed.Contains(sel.ProficiencyId);
            }

            if (!limitedWeapons)
            {
                if (wizardSelectionGated)
                {
                    return !string.Equals(sel.ProficiencyType, "individual", StringComparison.OrdinalIgnoreCase)
                        || !CanWizardSelectWeaponByAbility(sel.ProficiencyId);
                }

                return false;
            }

            return !string.Equals(sel.ProficiencyType, "individual", StringComparison.OrdinalIgnoreCase)
                || !ClericRestrictedWeaponIds.Contains(sel.ProficiencyId);
        });

        if (removed <= 0)
            return;

        string message = noWeapons
            ? "Weapon restriction (15 CP) is selected. All weapon proficiencies were removed."
            : wizardSelectionGated
                ? "Wizard weapon-selection limits are active. Disallowed proficiencies were removed."
                : "Weapon restriction is selected. Only staff, club, war hammer, horseman's mace, and horseman's flail may remain.";

        MessageBox.Show(message, "Weapon Restrictions Applied", MessageBoxButton.OK, MessageBoxImage.Information);
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
                specializationId,
                Math.Max(1, _app.CharGen.CharacterLevel),
                _app.CharGen.RaceId);
            int extraCost = _app.Rules.Classes.TryGetValue(classId, out var classDefinition)
                ? CharGenClassAbilitiesScreen.CalculateConfigurationCpCost(
                    classDefinition,
                    GetSpheresForClass(classId),
                    GetWizardSchoolsForClass(classId),
                    specializationId)
                : 0;

            totalRemaining += Math.Max(0, classPackage.remaining - extraCost);
        }

        return Math.Max(0, totalRemaining);
    }

    private int GetCarryoverFromNwpStage()
    {
        if (!IsPlayersOptionMode())
            return 0;

        if (_app.CharGen.IsLevelUpMode || _app.CharGen.IsExistingCharacterMode)
        {
            int startingPoolCp = _app.CharGen.IsLevelUpMode
                && _app.CharGen.LevelUpCharacterIndex >= 0
                && _app.CharGen.LevelUpCharacterIndex < _app.Characters.Count
                    ? Math.Max(0, _app.Characters[_app.CharGen.LevelUpCharacterIndex].UnspentCharacterPoints)
                    : _app.CharGen.GetExistingCharacterPointPool();
            int pendingGain = Math.Max(0, _app.CharGen.LevelUpPendingCharacterPointGain);
            int availablePool = Math.Max(0, startingPoolCp + pendingGain);

            int baselineTraitCost = GetTraitCost(_app.CharGen.BaselineTraitIds);
            int currentTraitCost = GetSelectedTraitCost();
            int deltaTraitCost = currentTraitCost - baselineTraitCost;

            int baselineDisadvantageBonus = GetDisadvantageBonus(_app.CharGen.BaselineDisadvantageSeverities);
            int currentDisadvantageBonus = GetSelectedDisadvantageBonus();
            int deltaDisadvantageBonus = currentDisadvantageBonus - baselineDisadvantageBonus;

            int baselineNwpPurchaseCp = GetNwpPurchaseCpForIds(_app.CharGen.BaselineNonweaponProficiencyIds);
            int currentNwpPurchaseCp = GetSelectedNwpPurchaseCp();
            int deltaNwpPurchaseCp = currentNwpPurchaseCp - baselineNwpPurchaseCp;

            int baselineNwpImprovementCp = GetNwpImprovementCpForState(
                _app.CharGen.BaselineNonweaponProficiencyIds,
                _app.CharGen.BaselineNonweaponProficiencyImprovements);
            int currentNwpImprovementCp = GetTotalNwpImprovementCp();
            int deltaNwpImprovementCp = currentNwpImprovementCp - baselineNwpImprovementCp;

            int spentThisUpdate = deltaTraitCost + deltaNwpPurchaseCp + deltaNwpImprovementCp - deltaDisadvantageBonus;
            return Math.Max(0, availablePool - spentThisUpdate);
        }

        int nwpBudget = GetCharacterOptionsCpBudget();
        int cpPool = GetCarryoverCharacterPointPool();
        int disadvantageBonus = GetSelectedDisadvantageBonus();
        int traitCost = GetSelectedTraitCost();
        int nwpPurchaseCp = GetSelectedNwpPurchaseCp();
        int nwpImprovementCp = GetTotalNwpImprovementCp();

        int remaining = nwpBudget + disadvantageBonus + cpPool - traitCost - nwpPurchaseCp - nwpImprovementCp;
        return Math.Max(0, remaining);
    }

    private int GetTraitCost(IEnumerable<string> traitIds)
    {
        var selectedIds = traitIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _catalog.Traits
            .Where(x => selectedIds.Contains(x.Id))
            .Sum(x => x.Cost);
    }

    private int GetDisadvantageBonus(IReadOnlyDictionary<string, string> severities)
    {
        if (severities == null || severities.Count == 0)
            return 0;

        return _catalog.Disadvantages
            .Where(x => severities.ContainsKey(x.Id))
            .Sum(x =>
            {
                string severity = severities[x.Id];
                bool isSevere = string.Equals(severity, "severe", StringComparison.OrdinalIgnoreCase);
                return isSevere && x.SevereBonus.HasValue ? x.SevereBonus.Value : x.ModerateBonus;
            });
    }

    private int GetNwpPurchaseCpForIds(IEnumerable<string> proficiencyIds)
    {
        if (!IsPlayersOptionMode())
            return 0;

        var proficiencyById = _catalog.NonweaponProficiencies
            .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        int total = 0;
        foreach (string id in proficiencyIds)
        {
            if (!proficiencyById.TryGetValue(id, out var proficiency))
                continue;
            total += proficiency.CpCost > 0 ? proficiency.CpCost : Math.Max(1, proficiency.Slots);
        }

        return total;
    }

    private static int GetNwpImprovementCpForState(IEnumerable<string> proficiencyIds, IReadOnlyDictionary<string, int> improvementById)
    {
        var selectedDistinct = proficiencyIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return improvementById
            .Where(kv => selectedDistinct.Contains(kv.Key))
            .Sum(kv => Math.Max(0, kv.Value));
    }

    private int GetCarryoverCharacterPointPool()
    {
        if (!IsPlayersOptionMode())
            return 0;

        int existingPool = 0;
        if (_app.CharGen.IsLevelUpMode
            && _app.CharGen.LevelUpCharacterIndex >= 0
            && _app.CharGen.LevelUpCharacterIndex < _app.Characters.Count)
        {
            existingPool = Math.Max(0, _app.Characters[_app.CharGen.LevelUpCharacterIndex].UnspentCharacterPoints);
        }
        else if (_app.CharGen.IsExistingCharacterMode)
        {
            existingPool = _app.CharGen.GetExistingCharacterPointPool();
        }

        int pendingGain = Math.Max(0, _app.CharGen.LevelUpPendingCharacterPointGain);
        return Math.Max(0, existingPool + pendingGain);
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
        {
            bool wizardAbilityUnlock = IsWizardOnlyCharacter() && HasSelectedClassAbility("wizard_weapon_specialization");
            return HasPurchasedWeaponSpecializationAbility()
                && (_app.Rules.CanSpecializeAtLevel(GetEffectiveClassIds(), GetCharacterLevel()) || wizardAbilityUnlock);
        }
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

    private bool HasMultipleWeaponSpecializationAbility()
    {
        if (!IsPlayersOptionMode())
            return false;

        foreach (var classId in GetEffectiveClassIds())
        {
            var selected = GetSelectedAbilityIdsForClass(classId);
            if (selected.Any(id => RulesEngine.ExtractClassAbilityBaseId(id).EndsWith("_multiple_specialization", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private bool HasPaladinSingleSpecializationCap()
    {
        if (!IsPlayersOptionMode())
            return false;

        bool hasPaladinClass = GetEffectiveClassIds().Any(id => string.Equals(id, "paladin", StringComparison.OrdinalIgnoreCase));
        if (!hasPaladinClass)
            return false;

        var selected = GetSelectedAbilityIdsForClass("paladin");
        bool hasSingleSpecAbility = selected.Any(id => string.Equals(RulesEngine.ExtractClassAbilityBaseId(id), "paladin_weapon_specialization", StringComparison.OrdinalIgnoreCase));
        bool hasMultipleSpecAbility = selected.Any(id => RulesEngine.ExtractClassAbilityBaseId(id).EndsWith("_multiple_specialization", StringComparison.OrdinalIgnoreCase));
        return hasSingleSpecAbility && !hasMultipleSpecAbility;
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
        RemainingBudgetHeaderText.Text = isPO ? "GLOBAL CP REMAINING" : "SLOTS REMAINING";

        BudgetUsedSummaryText.Text = $"{used} / {budget}";
        BudgetUsedSummaryText.Foreground = over
            ? System.Windows.Media.Brushes.OrangeRed
            : (System.Windows.Media.Brush)FindResource("BrushText");

        RemainingBudgetText.Text = over ? $"Over by {used - budget}" : remaining.ToString();
        RemainingBudgetText.Foreground = over
            ? System.Windows.Media.Brushes.OrangeRed
            : (System.Windows.Media.Brush)FindResource("BrushText");

        CostInfoText.Text = isPO
            ? "Weapon: 2-6 CP by class | Styles & Armor: 2 CP | Talents: 3-6 CP | Tight: 4 CP | Broad: 6 CP"
            : "Weapon: 1 slot | Tight: 1 slot | Broad: 2 slots | Styles & Armor: 1 slot | Talents: 1-2 slots";

        SelectedCountText.Text = $"{_app.CharGen.SelectedWeaponProficiencies.Count} proficiencies";

        if (isPO)
        {
            int cpPool = GetCarryoverCharacterPointPool();
            SelectedCountText.Text = $"{_app.CharGen.SelectedWeaponProficiencies.Count} proficiencies • Global CP pool {cpPool}";
        }

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
        bool showArmor      = showType == "all" || showType == "armor";
        bool showStyles     = showType == "all" || showType == "styles";
        bool showTalents    = showType == "all" || showType == "talents";
        bool noWeapons      = HasNoWeaponProficiencyRestriction();
        bool limitedWeapons = HasLimitedPriestWeaponRestriction();
        bool canBuyGroups   = !IsPlayersOptionMode() || _app.Rules.CanBuyWeaponGroups(GetEffectiveClassIds());

        // Broad groups
        if (showBroad && canBuyGroups && !noWeapons && !limitedWeapons)
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
        if (showTight && canBuyGroups && !noWeapons && !limitedWeapons)
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
        if (showIndividual && !noWeapons)
        {
            foreach (var w in _app.Rules.Weapons
                .OrderBy(w => GetWeaponSortKey(w.Name), StringComparer.OrdinalIgnoreCase))
            {
                if (!IsWeaponAllowedForClassRestriction(w.Id))
                    continue;

                if (!string.Equals(groupFilter, AllGroups, StringComparison.OrdinalIgnoreCase))
                {
                    var grp = _app.Rules.WeaponGroups
                        .FirstOrDefault(g => string.Equals(g.Id, w.GroupId, StringComparison.OrdinalIgnoreCase));
                    if (grp is null || !string.Equals(grp.Name, groupFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                if (!MatchesSearch(w.Name, search)) continue;
                if (selectedIds.Contains(w.Id)) continue;

                bool isCoveredByGroup = _app.Rules.IsWeaponCoveredByGroupSelections(w.Id, _app.CharGen.SelectedWeaponProficiencies);

                // If covered by a group but character cannot specialize, skip redundant listing
                if (isCoveredByGroup && !CanSpecialize())
                    continue;

                int cost;
                string badge;
                string label;

                if (isCoveredByGroup)
                {
                    cost = IsPlayersOptionMode()
                        ? _app.Rules.GetWeaponSpecializationOnlyCpCost(GetEffectiveClassIds())
                        : 1;

                    if (HasFighterProficiencyEase())
                        cost = Math.Max(1, (cost + 1) / 2);

                    badge = $"{FormatBudgetUnits(cost)} (Spec)";
                    label = $"    {w.Name}  ★ [In Group]";
                }
                else
                {
                    cost = GetProficiencyCost("individual", w.Id);
                    if (cost < 0) continue;
                    badge = FormatBudgetUnits(cost);
                    label = $"    {w.Name}";
                }

                items.Add(new AvailableItem(w.Id, "individual",
                    label, badge, w, null, null));
            }
        }

        // Armor & Shield Proficiencies
        if (showArmor && !noWeapons)
        {
            foreach (var option in CombatOptions.Where(o => o.Category == "Armor" || o.Category == "Shield").OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!MatchesSearch(option.Name, search)) continue;
                if (selectedIds.Contains(option.Id)) continue;

                int cost = GetProficiencyCost(CombatOptionType, option.Id);
                if (cost < 0) continue;

                string prefix = showType == "all" ? $"[{option.Category}] " : "    ";
                items.Add(new AvailableItem(option.Id, CombatOptionType,
                    $"{prefix}{option.Name}", FormatBudgetUnits(cost), null, null, null));
            }
        }

        // Fighting Styles
        if (showStyles && !noWeapons)
        {
            foreach (var option in CombatOptions.Where(o => o.Category == "Style").OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!MatchesSearch(option.Name, search)) continue;
                if (selectedIds.Contains(option.Id)) continue;

                int cost = GetProficiencyCost(CombatOptionType, option.Id);
                if (cost < 0) continue;

                string prefix = showType == "all" ? "[Style] " : "    ";
                items.Add(new AvailableItem(option.Id, CombatOptionType,
                    $"{prefix}{option.Name}", FormatBudgetUnits(cost), null, null, null));
            }
        }

        // Special Talents (Player's Option: Combat & Tactics)
        if (showTalents && !noWeapons)
        {
            foreach (var option in CombatOptions.Where(o => o.Category == "Talent").OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!MatchesSearch(option.Name, search)) continue;
                if (selectedIds.Contains(option.Id)) continue;

                int cost = GetProficiencyCost(CombatOptionType, option.Id);
                if (cost < 0) continue;

                string prefix = showType == "all" ? "[Talent] " : "    ";
                items.Add(new AvailableItem(option.Id, CombatOptionType,
                    $"{prefix}{option.Name}", FormatBudgetUnits(cost), null, null, null));
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
                int cost = GetSelectionTotalCost(sel);
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
        return _app.Rules.IsWeaponCoveredByGroupSelections(weapon.Id, _app.CharGen.SelectedWeaponProficiencies);
    }

    private static bool MatchesSearch(string name, string search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        return name.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private string GetShowType()
    {
        if (ShowArmor?.IsChecked      == true) return "armor";
        if (ShowStyles?.IsChecked     == true) return "styles";
        if (ShowTalents?.IsChecked    == true) return "talents";
        if (ShowIndividual?.IsChecked == true) return "individual";
        if (ShowTight?.IsChecked      == true) return "tight";
        if (ShowBroad?.IsChecked      == true) return "broad";
        return "all";
    }

    private static string BuildSelectionBadge(WeaponProficiencySelection selection)
    {
        var parts = new List<string>();
        if (selection.GrandMastery) parts.Add("G-MASTER");
        else if (selection.HighMastery) parts.Add("H-MASTER");
        else if (selection.WeaponMastery) parts.Add("MASTER");
        else if (selection.Specialized) parts.Add("SPEC");

        if (selection.WeaponOfChoice) parts.Add("CHOICE");
        if (selection.WeaponExpertise) parts.Add("EXPERT");
        return parts.Count > 0 ? $"★ {string.Join("/", parts)}" : string.Empty;
    }

    private int GetPerWeaponOptionCost(string optionId)
        => Math.Max(0, GetProficiencyCost(CombatOptionType, optionId));

    private int GetSelectionTotalCost(WeaponProficiencySelection selection)
    {
        bool covered = string.Equals(selection.ProficiencyType, "individual", StringComparison.OrdinalIgnoreCase)
            && _app.Rules.IsWeaponCoveredByGroupSelections(selection.ProficiencyId, _app.CharGen.SelectedWeaponProficiencies);

        int cost;
        if (covered)
        {
            if (IsPlayersOptionMode())
            {
                cost = selection.Specialized ? _app.Rules.GetWeaponSpecializationOnlyCpCost(GetEffectiveClassIds()) : 0;
                if (HasFighterProficiencyEase()) cost = Math.Max(1, (cost + 1) / 2);
            }
            else
            {
                cost = selection.Specialized ? 1 : 0;
            }
        }
        else
        {
            cost = GetProficiencyCost(selection.ProficiencyType, selection.ProficiencyId, selection.Specialized);
            if (cost < 0)
                cost = RulesEngine.GetWeaponProficiencySlotCost(selection.ProficiencyType, selection.ProficiencyId) + (selection.Specialized ? 1 : 0);
        }

        if (selection.WeaponOfChoice)
            cost += GetPerWeaponOptionCost(WeaponOfChoiceId);
        if (selection.WeaponExpertise)
            cost += GetPerWeaponOptionCost(WeaponExpertiseId);
        if (selection.WeaponMastery)
            cost += IsPlayersOptionMode() ? Math.Max(0, GetWeaponMasteryCost()) : 1;
        if (selection.HighMastery)
            cost += IsPlayersOptionMode() ? Math.Max(0, GetHighMasteryCost()) : 1;
        if (selection.GrandMastery)
            cost += IsPlayersOptionMode() ? Math.Max(0, GetGrandMasteryCost()) : 1;

        return Math.Max(0, cost);
    }

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

        bool inGroup = _app.Rules.IsWeaponCoveredByGroupSelections(w.Id, _app.CharGen.SelectedWeaponProficiencies);
        if (inGroup)
        {
            int specCost = IsPlayersOptionMode()
                ? _app.Rules.GetWeaponSpecializationOnlyCpCost(GetEffectiveClassIds())
                : 1;
            if (HasFighterProficiencyEase()) specCost = Math.Max(1, (specCost + 1) / 2);
            string groupNote = $"\n\n★ Group Covered: Base proficiency is already granted by your group proficiency. Adding this weapon purchases Specialization directly for {FormatBudgetUnits(specCost)}.";
            DetailNotes.Text = (w.Notes ?? string.Empty) + groupNote;
        }
        else
        {
            DetailNotes.Text = w.Notes;
        }

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
        var option = FindCombatOption(optionId);
        if (option is null)
        {
            DetailTitle.Text = optionId;
            DetailType.Text = $"Combat Option  ({FormatBudgetUnits(GetProficiencyCost(CombatOptionType, optionId))})";
            DetailNotes.Text = "Additional combat training option.";
        }
        else
        {
            DetailTitle.Text = option.Name;
            string categoryLabel = option.Category switch
            {
                "Armor" => "Armor Proficiency",
                "Shield" => "Shield Proficiency",
                "Style" => "Fighting Style",
                "Talent" => "Special Talent",
                _ => "Combat Option"
            };
            int cost = GetProficiencyCost(CombatOptionType, option.Id);
            DetailType.Text = $"{categoryLabel}  ({FormatBudgetUnits(cost)})";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(option.Description);
            sb.AppendLine();
            sb.AppendLine($"• Category: {categoryLabel}");
            sb.AppendLine($"• Cost: {option.SlotCost} {(option.SlotCost == 1 ? "slot" : "slots")} (Core) / {option.CpCost} CP (Player's Option)");
            if (!string.IsNullOrWhiteSpace(option.AllowedGroups))
                sb.AppendLine($"• Allowed Groups: {option.AllowedGroups}");
            if (!string.IsNullOrWhiteSpace(option.RelevantAbility))
                sb.AppendLine($"• Relevant Ability: {option.RelevantAbility}");
            if (option.InitialRating > 0)
                sb.AppendLine($"• Initial Rating: {option.InitialRating}");

            DetailNotes.Text = sb.ToString().TrimEnd();
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

        if (HasNoWeaponProficiencyRestriction())
        {
            MessageBox.Show(
                "This character has the 15 CP weapon restriction and cannot gain weapon proficiencies.",
                "Weapon Restriction",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (HasLimitedPriestWeaponRestriction())
        {
            bool allowed = string.Equals(item.ProfType, "individual", StringComparison.OrdinalIgnoreCase)
                && ClericRestrictedWeaponIds.Contains(item.Id);
            if (!allowed)
            {
                MessageBox.Show(
                    "This character is limited to staff, club, war hammer, horseman's mace, and horseman's flail.",
                    "Weapon Restriction",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        if (HasWizardListedWeaponRestriction())
        {
            var allowedIds = GetWizardListedWeaponRestrictionIds();
            bool allowed = string.Equals(item.ProfType, "individual", StringComparison.OrdinalIgnoreCase)
                && allowedIds.Contains(item.Id);
            if (!allowed)
            {
                string allowedText = allowedIds.Count == 0
                    ? "No allowed weapon list is configured. Set it in Class Abilities for Weapons Restriction (3)."
                    : "Allowed list only: " + string.Join(", ", allowedIds
                        .Select(id => _app.Rules.WeaponById.TryGetValue(id, out var w) ? w.Name : id)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                MessageBox.Show(
                    allowedText,
                    "Weapon Restriction",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        var existing = _app.CharGen.SelectedWeaponProficiencies
            .FirstOrDefault(s => string.Equals(s.ProficiencyId, item.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return; // already added

        bool isCovered = string.Equals(item.ProfType, "individual", StringComparison.OrdinalIgnoreCase)
            && _app.Rules.IsWeaponCoveredByGroupSelections(item.Id, _app.CharGen.SelectedWeaponProficiencies);

        if (isCovered)
        {
            if (!CanSpecialize())
            {
                MessageBox.Show(
                    $"{BuildDisplayName(item)} is already covered by your selected group proficiency.",
                    "Weapon Proficiency",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (_app.CharGen.SelectedWeaponProficiencies.Any(s => s.Specialized)
                && !HasMultipleWeaponSpecializationAbility())
            {
                MessageBox.Show(
                    "Weapon Specialization allows only one specialized weapon unless Multiple Specializations was selected during character creation.",
                    "Weapon Specialization",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (HasPaladinSingleSpecializationCap() && _app.CharGen.SelectedWeaponProficiencies.Count(s => s.Specialized) >= 1)
            {
                MessageBox.Show(
                    "Paladin weapon specialization allows specialization in only one weapon.",
                    "Weapon Specialization",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        int addCost = isCovered
            ? (IsPlayersOptionMode() ? _app.Rules.GetWeaponSpecializationOnlyCpCost(GetEffectiveClassIds()) : 1)
            : GetProficiencyCost(item.ProfType, item.Id);

        if (isCovered && HasFighterProficiencyEase())
            addCost = Math.Max(1, (addCost + 1) / 2);

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
            Specialized     = isCovered && CanSpecialize(),
        };
        _app.CharGen.SelectedWeaponProficiencies.Add(sel);
        Refresh();

        // Select the newly added item in the right list
        var addedItem = SelectedList.Items.Cast<SelectedItem>()
            .FirstOrDefault(x => x.Id == item.Id);
        if (addedItem is not null) SelectedList.SelectedItem = addedItem;
    }

    private void PromptSpecializeInGroupWeapon(SelectedItem groupItem)
    {
        if (!CanSpecialize())
        {
            MessageBox.Show(
                "Your character's class is not eligible for weapon specialization.",
                "Weapon Specialization",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_app.CharGen.SelectedWeaponProficiencies.Any(s => s.Specialized) && !HasMultipleWeaponSpecializationAbility())
        {
            MessageBox.Show(
                "Weapon Specialization allows only one specialized weapon unless Multiple Specializations was selected during character creation.",
                "Weapon Specialization",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (HasPaladinSingleSpecializationCap() && _app.CharGen.SelectedWeaponProficiencies.Count(s => s.Specialized) >= 1)
        {
            MessageBox.Show(
                "Paladin weapon specialization allows specialization in only one weapon.",
                "Weapon Specialization",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var candidateWeapons = new List<WeaponDefinition>();
        if (string.Equals(groupItem.ProfType, "tight_group", StringComparison.OrdinalIgnoreCase))
        {
            if (_app.Rules.TightGroups.TryGetValue(groupItem.Id, out var tg))
            {
                candidateWeapons = tg.WeaponIds
                    .Select(id => _app.Rules.WeaponById.TryGetValue(id, out var w) ? w : null)
                    .Where(w => w is not null && IsWeaponAllowedForClassRestriction(w.Id))
                    .Select(w => w!)
                    .ToList();
            }
        }
        else if (string.Equals(groupItem.ProfType, "broad_group", StringComparison.OrdinalIgnoreCase))
        {
            var bg = _app.Rules.WeaponGroups
                .FirstOrDefault(g => string.Equals(g.Id, groupItem.Id, StringComparison.OrdinalIgnoreCase));
            if (bg is not null)
            {
                var ids = bg.TightGroups.SelectMany(t => t.WeaponIds)
                    .Concat(_app.Rules.Weapons.Where(w => string.Equals(w.GroupId, bg.Id, StringComparison.OrdinalIgnoreCase)).Select(w => w.Id))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                candidateWeapons = ids
                    .Select(id => _app.Rules.WeaponById.TryGetValue(id, out var w) ? w : null)
                    .Where(w => w is not null && IsWeaponAllowedForClassRestriction(w.Id))
                    .Select(w => w!)
                    .ToList();
            }
        }

        candidateWeapons = candidateWeapons
            .OrderBy(w => GetWeaponSortKey(w.Name), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidateWeapons.Count == 0)
        {
            MessageBox.Show(
                "No eligible weapons were found in this weapon group.",
                "Weapon Specialization",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int addCost = IsPlayersOptionMode()
            ? _app.Rules.GetWeaponSpecializationOnlyCpCost(GetEffectiveClassIds())
            : 1;
        if (HasFighterProficiencyEase())
            addCost = Math.Max(1, (addCost + 1) / 2);

        int budget = GetSlotBudget();
        int used = GetSlotsUsed();
        if (used + addCost > budget)
        {
            string budgetType = IsPlayersOptionMode() ? "Weapon Proficiency CP" : "Weapon Proficiency Slots";
            MessageBox.Show(
                $"Specializing in a weapon from this group requires {FormatBudgetUnits(addCost)}, which would exceed your {budgetType.ToLowerInvariant()} budget.\n\nUsed: {used}/{budget}",
                budgetType,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var dialog = new Window
        {
            Title = $"Specialize in Weapon — {groupItem.Label.Trim()}",
            Width = 440,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("BrushCard"),
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new StackPanel { Margin = new Thickness(18) };

        root.Children.Add(new TextBlock
        {
            Text = $"Select a weapon from {groupItem.Label.Trim()} to specialize in:",
            Style = (Style)Application.Current.FindResource("TitleText"),
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        });

        root.Children.Add(new TextBlock
        {
            Text = $"Group proficiency covers basic training. Specialization cost: {FormatBudgetUnits(addCost)}.",
            Style = (Style)Application.Current.FindResource("SubtitleText"),
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap,
        });

        var combo = new ComboBox
        {
            Style = (Style)Application.Current.FindResource("DarkComboBox"),
            Height = 32,
            Margin = new Thickness(0, 0, 0, 18),
            ItemsSource = candidateWeapons.Select(w => $"{w.Name}  (Spd {w.Speed}, {w.DamageSm}/{w.DamageL}, {ExpandTypeCode(w.Type)})").ToList(),
            SelectedIndex = 0,
        };
        root.Children.Add(combo);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var okBtn = new Button
        {
            Content = "★  SPECIALIZE",
            Style = (Style)Application.Current.FindResource("GoldButton"),
            Width = 130,
            Height = 32,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var cancelBtn = new Button
        {
            Content = "CANCEL",
            Style = (Style)Application.Current.FindResource("DarkButton"),
            Width = 90,
            Height = 32,
        };

        okBtn.Click += (_, _) =>
        {
            int idx = combo.SelectedIndex;
            if (idx >= 0 && idx < candidateWeapons.Count)
            {
                var chosenWeapon = candidateWeapons[idx];
                var existingSel = _app.CharGen.SelectedWeaponProficiencies
                    .FirstOrDefault(s => string.Equals(s.ProficiencyId, chosenWeapon.Id, StringComparison.OrdinalIgnoreCase));

                if (existingSel is not null)
                {
                    existingSel.Specialized = true;
                }
                else
                {
                    _app.CharGen.SelectedWeaponProficiencies.Add(new WeaponProficiencySelection
                    {
                        ProficiencyId = chosenWeapon.Id,
                        ProficiencyType = "individual",
                        DisplayName = GetWeaponSortKey(chosenWeapon.Name),
                        Specialized = true,
                    });
                }

                Refresh();

                var addedItem = SelectedList.Items.Cast<SelectedItem>()
                    .FirstOrDefault(x => x.Id == chosenWeapon.Id);
                if (addedItem is not null) SelectedList.SelectedItem = addedItem;
            }
            dialog.Close();
        };

        cancelBtn.Click += (_, _) => dialog.Close();

        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        root.Children.Add(btnRow);

        dialog.Content = root;
        dialog.ShowDialog();
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

        if (string.Equals(selItem.ProfType, "broad_group", StringComparison.OrdinalIgnoreCase)
            || string.Equals(selItem.ProfType, "tight_group", StringComparison.OrdinalIgnoreCase))
        {
            PromptSpecializeInGroupWeapon(selItem);
            return;
        }

        if (selItem.ProfType != "individual")
        {
            MessageBox.Show(
                "Only individual weapon proficiencies or weapon groups can be specialized.",
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
            if (!match.Specialized && _app.CharGen.SelectedWeaponProficiencies.Any(s => s.Specialized)
                && !HasMultipleWeaponSpecializationAbility())
            {
                MessageBox.Show(
                    "Weapon Specialization allows only one specialized weapon unless Multiple Specializations was selected during character creation.",
                    "Weapon Specialization",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!match.Specialized && HasPaladinSingleSpecializationCap())
            {
                int specializedCount = _app.CharGen.SelectedWeaponProficiencies.Count(s => s.Specialized);
                if (specializedCount >= 1)
                {
                    MessageBox.Show(
                        "Paladin weapon specialization allows specialization in only one weapon.",
                        "Weapon Specialization",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
            }

            int budget = GetSlotBudget();
            int used = GetSlotsUsed();
            bool isCovered = _app.Rules.IsWeaponCoveredByGroupSelections(match.ProficiencyId, _app.CharGen.SelectedWeaponProficiencies);
            int additionalCost;

            if (isCovered)
            {
                int specCost = IsPlayersOptionMode()
                    ? _app.Rules.GetWeaponSpecializationOnlyCpCost(GetEffectiveClassIds())
                    : 1;
                if (HasFighterProficiencyEase()) specCost = Math.Max(1, (specCost + 1) / 2);
                additionalCost = specCost;
            }
            else
            {
                int specializedCost = GetProficiencyCost(match.ProficiencyType, match.ProficiencyId, specialized: true);
                int currentCost = GetProficiencyCost(match.ProficiencyType, match.ProficiencyId, specialized: false);
                additionalCost = specializedCost - currentCost;
                if (specializedCost < 0)
                {
                    MessageBox.Show(
                        "Specialization is not available for this class or weapon under Players Option rules.",
                        "Weapon Specialization",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
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
            if (!match.Specialized)
            {
                match.WeaponMastery = false;
                match.HighMastery = false;
                match.GrandMastery = false;
            }
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
        CombatOptionType => FindCombatOption(item.Id)?.Name ?? item.Id,
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

    private int GetWeaponMasteryMinLevel(out string classLabel)
    {
        var classIds = GetEffectiveClassIds();
        bool hasFighter = classIds.Any(id => string.Equals(id, "fighter", StringComparison.OrdinalIgnoreCase));
        bool hasRangerOrPaladin = classIds.Any(id =>
            string.Equals(id, "ranger", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "paladin", StringComparison.OrdinalIgnoreCase));

        if (hasFighter && classIds.Count == 1)
        {
            classLabel = "Fighter";
            return 5;
        }
        if (hasFighter)
        {
            classLabel = "Multi-class Fighter";
            return 6;
        }
        if (hasRangerOrPaladin)
        {
            classLabel = "Ranger/Paladin";
            return 7;
        }

        classLabel = "Warrior";
        return -1;
    }

    private int GetHighMasteryMinLevel(out string classLabel)
    {
        var classIds = GetEffectiveClassIds();
        bool hasFighter = classIds.Any(id => string.Equals(id, "fighter", StringComparison.OrdinalIgnoreCase));
        bool hasRangerOrPaladin = classIds.Any(id =>
            string.Equals(id, "ranger", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "paladin", StringComparison.OrdinalIgnoreCase));

        if (hasFighter && classIds.Count == 1)
        {
            classLabel = "Fighter";
            return 6;
        }
        if (hasFighter)
        {
            classLabel = "Multi-class Fighter";
            return 8;
        }
        if (hasRangerOrPaladin)
        {
            classLabel = "Ranger/Paladin";
            return 9;
        }

        classLabel = "Warrior";
        return -1;
    }

    private int GetGrandMasteryMinLevel(out string classLabel)
    {
        var classIds = GetEffectiveClassIds();
        bool hasFighter = classIds.Any(id => string.Equals(id, "fighter", StringComparison.OrdinalIgnoreCase));
        if (hasFighter && classIds.Count == 1)
        {
            classLabel = "Single-class Fighter";
            return 9;
        }

        classLabel = "Single-class Fighter only";
        return -1;
    }

    private int GetWeaponMasteryCost()
    {
        int minLevel = GetWeaponMasteryMinLevel(out _);
        if (minLevel < 0 || GetCharacterLevel() < minLevel)
            return -1;

        if (!IsPlayersOptionMode())
            return 1;

        var classIds = GetEffectiveClassIds();
        bool isSingleFighter = classIds.Count == 1 && classIds.Any(id => string.Equals(id, "fighter", StringComparison.OrdinalIgnoreCase));
        return isSingleFighter ? 2 : 8;
    }

    private int GetHighMasteryCost()
    {
        int minLevel = GetHighMasteryMinLevel(out _);
        if (minLevel < 0 || GetCharacterLevel() < minLevel)
            return -1;

        if (!IsPlayersOptionMode())
            return 1;

        var classIds = GetEffectiveClassIds();
        bool isSingleFighter = classIds.Count == 1 && classIds.Any(id => string.Equals(id, "fighter", StringComparison.OrdinalIgnoreCase));
        return isSingleFighter ? 2 : 8;
    }

    private int GetGrandMasteryCost()
    {
        int minLevel = GetGrandMasteryMinLevel(out _);
        if (minLevel < 0 || GetCharacterLevel() < minLevel)
            return -1;

        if (!IsPlayersOptionMode())
            return 1;

        return 2;
    }

    private void BtnWeaponMastery_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedIndividualWeapon(out var selection))
            return;
        if (!selection.Specialized)
        {
            MessageBox.Show("Weapon Mastery requires specialization in the selected weapon.", "Weapon Mastery", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int minLevel = GetWeaponMasteryMinLevel(out string classLabel);
        if (minLevel < 0 || GetCharacterLevel() < minLevel)
        {
            MessageBox.Show($"Weapon Mastery requires {classLabel} Level {minLevel}+. Current character level is {GetCharacterLevel()}.", "Weapon Mastery", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int cost = GetWeaponMasteryCost();
        if (selection.WeaponMastery)
        {
            selection.WeaponMastery = false;
            selection.HighMastery = false;
            selection.GrandMastery = false;
        }
        else
        {
            if (GetSlotsUsed() + cost > GetSlotBudget())
            {
                string unit = IsPlayersOptionMode() ? "CP" : "slot(s)";
                MessageBox.Show($"Weapon Mastery requires {cost} additional {unit}.", "Weapon Mastery", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            selection.WeaponMastery = true;
        }
        Refresh();
    }

    private void BtnHighMastery_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedIndividualWeapon(out var selection))
            return;
        if (!selection.Specialized)
        {
            MessageBox.Show("High Mastery requires specialization and Weapon Mastery first.", "High Mastery", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int minLevel = GetHighMasteryMinLevel(out string classLabel);
        if (minLevel < 0 || GetCharacterLevel() < minLevel)
        {
            MessageBox.Show($"High Mastery requires {classLabel} Level {minLevel}+. Current character level is {GetCharacterLevel()}.", "High Mastery", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int highCost = GetHighMasteryCost();
        int baseMasteryCost = selection.WeaponMastery ? 0 : GetWeaponMasteryCost();

        if (selection.HighMastery)
        {
            selection.HighMastery = false;
            selection.GrandMastery = false;
        }
        else
        {
            int totalNeeded = highCost + baseMasteryCost;
            if (GetSlotsUsed() + totalNeeded > GetSlotBudget())
            {
                string unit = IsPlayersOptionMode() ? "CP" : "slot(s)";
                MessageBox.Show($"High Mastery requires {totalNeeded} additional {unit}.", "High Mastery", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            selection.WeaponMastery = true;
            selection.HighMastery = true;
        }
        Refresh();
    }

    private void BtnGrandMastery_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedIndividualWeapon(out var selection))
            return;
        if (!selection.Specialized)
        {
            MessageBox.Show("Grand Mastery requires specialization, Weapon Mastery, and High Mastery first.", "Grand Mastery", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int minLevel = GetGrandMasteryMinLevel(out string classLabel);
        if (minLevel < 0 || GetCharacterLevel() < minLevel)
        {
            MessageBox.Show($"Grand Mastery requires {classLabel} Level {minLevel}+. Current character level is {GetCharacterLevel()}.", "Grand Mastery", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int grandCost = GetGrandMasteryCost();
        int highCost = selection.HighMastery ? 0 : GetHighMasteryCost();
        int baseMasteryCost = selection.WeaponMastery ? 0 : GetWeaponMasteryCost();

        if (selection.GrandMastery)
        {
            selection.GrandMastery = false;
        }
        else
        {
            int totalNeeded = grandCost + highCost + baseMasteryCost;
            if (GetSlotsUsed() + totalNeeded > GetSlotBudget())
            {
                string unit = IsPlayersOptionMode() ? "CP" : "slot(s)";
                MessageBox.Show($"Grand Mastery requires {totalNeeded} additional {unit}.", "Grand Mastery", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            selection.WeaponMastery = true;
            selection.HighMastery = true;
            selection.GrandMastery = true;
        }
        Refresh();
    }

    private void WeaponOptionButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button)
            return;

        string title = button == BtnWeaponChoice
            ? "Weapon of Choice"
            : button == BtnWeaponExpertise
                ? "Weapon Expertise"
                : button == BtnWeaponMastery
                    ? "Weapon Mastery"
                : button == BtnHighMastery
                    ? "High Mastery"
                : button == BtnGrandMastery
                    ? "Grand Mastery"
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
        BtnWeaponMastery.IsEnabled = false;
        BtnHighMastery.IsEnabled = false;
        BtnGrandMastery.IsEnabled = false;
    }

    private void SelectedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = SelectedList.SelectedItem as SelectedItem;
        BtnRemove.IsEnabled = item is not null;

        if (item is not null)
        {
            UpdateDetailFromSelected(item);
            bool isIndividual = item.ProfType == "individual";
            bool isGroup = item.ProfType is "broad_group" or "tight_group";
            bool hasWeapon = TryGetSelectedIndividualWeapon(out var selectedWeapon);
            bool isSpec = hasWeapon && selectedWeapon.Specialized;
            bool isMaster = hasWeapon && selectedWeapon.WeaponMastery;
            bool isHighMaster = hasWeapon && selectedWeapon.HighMastery;
            bool isGrandMaster = hasWeapon && selectedWeapon.GrandMastery;

            BtnSpecialize.IsEnabled = CanSpecialize() && (isIndividual || isGroup);
            BtnWeaponChoice.IsEnabled = IsPlayersOptionMode() && isIndividual;
            BtnWeaponExpertise.IsEnabled = IsPlayersOptionMode() && isIndividual;

            int masteryCost = GetWeaponMasteryCost();
            int highMasteryCost = GetHighMasteryCost();
            int grandMasteryCost = GetGrandMasteryCost();

            BtnWeaponMastery.IsEnabled = isIndividual && isSpec && masteryCost >= 0;
            BtnHighMastery.IsEnabled = isIndividual && isSpec && isMaster && highMasteryCost >= 0;
            BtnGrandMastery.IsEnabled = isIndividual && isSpec && isHighMaster && grandMasteryCost >= 0;

            BtnSpecialize.Content = isGroup
                ? "★ SPEC IN GROUP..."
                : (isSpec ? "★ SPECIALIZED" : "★ SPECIALIZE");
            BtnWeaponMastery.Content = isMaster ? "★ MASTER" : "WEAPON MASTERY";
            BtnHighMastery.Content = isHighMaster ? "★★ HIGH MASTER" : "HIGH MASTERY";
            BtnGrandMastery.Content = isGrandMaster ? "★★★ GRAND MASTER" : "GRAND MASTERY";
        }
        else
        {
            BtnSpecialize.IsEnabled = false;
            BtnWeaponChoice.IsEnabled = false;
            BtnWeaponExpertise.IsEnabled = false;
            BtnWeaponMastery.IsEnabled = false;
            BtnHighMastery.IsEnabled = false;
            BtnGrandMastery.IsEnabled = false;

            BtnSpecialize.Content = "★ SPECIALIZE";
            BtnWeaponMastery.Content = "WEAPON MASTERY";
            BtnHighMastery.Content = "HIGH MASTERY";
            BtnGrandMastery.Content = "GRAND MASTERY";
        }
    }

    private void AvailableList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnAdd_Click(sender, new RoutedEventArgs());

    private void SelectedList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnRemove_Click(sender, new RoutedEventArgs());
}
