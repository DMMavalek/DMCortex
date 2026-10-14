using System;
using System.Collections.Generic;
using System.Linq;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public static class RangerTrackingRules
{
    public const string TrackingAbilityId = "ranger_tracking_proficiency";
    private static readonly NonweaponProficiencyDefinition TrackingDefinition = new(
        "nwp_tracking", "Tracking", "General", 2, "Wisdom", -6, string.Empty);

    public static bool HasTrackingGrant(CharGenState state)
        => GetClassIds(state.ClassId, state.SelectedClassIds)
            .Any(id => string.Equals(id, "ranger", StringComparison.OrdinalIgnoreCase))
            && GetRangerAbilityIds(state.ClassId, state.SelectedClassAbilityIds, state.SelectedAbilitiesByClass)
                .Select(RulesEngine.ExtractClassAbilityBaseId)
                .Contains(TrackingAbilityId, StringComparer.OrdinalIgnoreCase);

    public static bool HasTrackingGrant(CharacterSheet character)
        => GetClassIds(character.ClassId, character.ClassIds)
            .Any(id => string.Equals(id, "ranger", StringComparison.OrdinalIgnoreCase))
            && GetRangerAbilityIds(character.ClassId, character.SelectedClassAbilityIds, character.SelectedAbilitiesByClass)
                .Select(RulesEngine.ExtractClassAbilityBaseId)
                .Contains(TrackingAbilityId, StringComparer.OrdinalIgnoreCase);

    public static void EnsureGrantedTracking(CharGenState state, IEnumerable<NonweaponProficiencyDefinition> proficiencies)
    {
        if (!HasTrackingGrant(state))
            return;

        var tracking = proficiencies.FirstOrDefault(proficiency => IsTracking(proficiency.Id, proficiency.Name));
        if (tracking is null)
            return;

        if (!state.SelectedNonweaponProficiencyIds.Contains(tracking.Id, StringComparer.OrdinalIgnoreCase))
            state.SelectedNonweaponProficiencyIds.Add(tracking.Id);
        state.SelectedNonweaponProficiencyImprovements.TryAdd(tracking.Id, 0);
    }

    public static int GetBonus(CharGenState state, NonweaponProficiencyDefinition proficiency)
    {
        if (!IsTracking(proficiency.Id, proficiency.Name))
            return 0;

        var classIds = GetClassIds(state.ClassId, state.SelectedClassIds);
        bool hasTrackingAbility = HasTrackingGrant(state);
        int rangerLevel = state.GetConfiguredClassLevel("ranger");
        if (!state.UsesSeparateExistingClassExperience && state.IsExistingCharacterMode && !state.IsLevelUpMode)
            rangerLevel = CharacterProgressionService.GetLevelForExperience("ranger", state.GetExistingExperienceForClass("ranger"));
        else if (state.IsLevelUpMode)
            rangerLevel = CharacterProgressionService.GetLevelForExperience("ranger",
                (int)Math.Clamp((long)Math.Max(0, state.ExistingStartingExperience) + Math.Max(0, state.LevelUpPendingExperienceGain), 0L, int.MaxValue));

        return GetBonus(state.CharacterMode, classIds, hasTrackingAbility, rangerLevel);
    }

    public static int GetBonus(CharacterSheet character)
    {
        var classIds = GetClassIds(character.ClassId, character.ClassIds);
        bool hasTrackingAbility = HasTrackingGrant(character);

        int rangerLevel = character.ClassLevels
            .FirstOrDefault(entry => string.Equals(entry.Key, "ranger", StringComparison.OrdinalIgnoreCase))
            .Value;
        if (rangerLevel <= 0)
            rangerLevel = Math.Max(1, character.Level);

        return GetBonus(character.CharacterMode, classIds, hasTrackingAbility, rangerLevel);
    }

    public static bool IsTracking(string proficiencyId, string proficiencyName)
    {
        return string.Equals(proficiencyId, "nwp_tracking", StringComparison.OrdinalIgnoreCase)
            || string.Equals(proficiencyName.Trim(), "Tracking", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetConditionalTargetSummary(
        CharGenState state,
        NonweaponProficiencyDefinition proficiency,
        int baseTarget)
    {
        if (!UsesPlayersOptionRules(state.CharacterMode)
            || !IsTracking(proficiency.Id, proficiency.Name))
        {
            return string.Empty;
        }

        var contributions = NwpSynergyRules.GetContributions(state, proficiency);
        bool hasAnimals = contributions.Any(rule => rule.Situational && rule.Condition == "animals");
        bool hasWild = contributions.Any(rule => rule.Situational && rule.Condition == "wild");
        // Keep the public legacy wording. The central evaluator handles wild implying animals;
        // GetConditionalSummary reports each individual rule, not that combined task context.
        int animalBonus = NwpSynergyRules.EvaluateConditionalBonus(state, proficiency, new[] { "animals" });
        int wildBonus = NwpSynergyRules.EvaluateConditionalBonus(state, proficiency, new[] { "wild" });
        return FormatConditionalTargets(baseTarget, hasAnimals, hasWild, animalBonus, wildBonus);
    }

    public static string GetConditionalModifierSummary(CharacterSheet character)
    {
        if (!UsesPlayersOptionRules(character.CharacterMode))
            return string.Empty;

        int animalBonus = NwpSynergyRules.EvaluateConditionalBonus(character, TrackingDefinition, new[] { "animals" });
        int wildBonus = NwpSynergyRules.EvaluateConditionalBonus(character, TrackingDefinition, new[] { "wild" });
        return FormatConditionalModifiers(animalBonus, wildBonus - animalBonus);
    }

    private static int GetBonus(
        string characterMode,
        IEnumerable<string> classIds,
        bool hasTrackingAbility,
        int rangerLevel)
    {
        if (!classIds.Any(id => string.Equals(id, "ranger", StringComparison.OrdinalIgnoreCase)))
            return 0;

        if (!UsesPlayersOptionRules(characterMode))
        {
            // Core Tracking carries a -6 check modifier; Rangers ignore that penalty.
            return 6;
        }

        int levelImprovement = hasTrackingAbility ? Math.Max(1, rangerLevel) / 3 : 0;
        return 5 + levelImprovement;
    }

    private static string FormatConditionalTargets(
        int baseTarget, bool hasAnimalLore, bool hasAnimalEmpathy, int animalBonus, int wildBonus)
    {
        if (!hasAnimalLore && !hasAnimalEmpathy)
            return string.Empty;

        int animalTarget = baseTarget + animalBonus;
        int wildAnimalTarget = baseTarget + wildBonus;

        if (hasAnimalLore && hasAnimalEmpathy)
            return $", animal target {animalTarget}, non-domesticated animal target {wildAnimalTarget}";
        if (hasAnimalLore)
            return $", animal target {animalTarget}";
        return $", non-domesticated animal target {wildAnimalTarget}";
    }

    private static string FormatConditionalModifiers(int animalBonus, int wildOnlyBonus)
    {
        if (animalBonus > 0 && wildOnlyBonus > 0)
            return $"; +{animalBonus} vs animals (Animal Lore), another +{wildOnlyBonus} vs non-domesticated animals (Animal Empathy)";
        if (animalBonus > 0)
            return $"; +{animalBonus} vs animals (Animal Lore)";
        if (wildOnlyBonus > 0)
            return $"; +{wildOnlyBonus} vs non-domesticated animals (Animal Empathy)";
        return string.Empty;
    }

    private static bool UsesPlayersOptionRules(string characterMode)
    {
        return string.Equals(
            CharGenState.NormalizeCharacterModeToken(characterMode),
            "players_option",
            StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetClassIds(string primaryClassId, IEnumerable<string> selectedClassIds)
    {
        var classIds = selectedClassIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        if (classIds.Count == 0 && !string.IsNullOrWhiteSpace(primaryClassId))
            classIds.Add(primaryClassId);
        return classIds;
    }

    private static IEnumerable<string> GetRangerAbilityIds(
        string primaryClassId,
        IEnumerable<string> primaryAbilityIds,
        IReadOnlyDictionary<string, List<string>> abilitiesByClass)
    {
        var rangerEntry = abilitiesByClass.FirstOrDefault(entry =>
            string.Equals(entry.Key, "ranger", StringComparison.OrdinalIgnoreCase));
        if (rangerEntry.Value is { Count: > 0 })
            return rangerEntry.Value;

        return string.Equals(primaryClassId, "ranger", StringComparison.OrdinalIgnoreCase)
            ? primaryAbilityIds
            : Enumerable.Empty<string>();
    }
}