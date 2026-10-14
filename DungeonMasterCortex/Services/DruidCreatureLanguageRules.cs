using System;
using System.Collections.Generic;
using System.Linq;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public static class DruidCreatureLanguageRules
{
    public const string AbilityId = "druid_communicate_with_creatures";

    public static int GetAllowance(CharGenState state, CharacterSheet? updatingCharacter = null)
    {
        var classIds = state.SelectedClassIds.Count > 0 ? state.SelectedClassIds : new List<string> { state.ClassId };
        if (!classIds.Contains("druid", StringComparer.OrdinalIgnoreCase))
            return 0;

        var selections = state.SelectedAbilitiesByClass.FirstOrDefault(pair =>
            string.Equals(pair.Key, "druid", StringComparison.OrdinalIgnoreCase)).Value;
        IEnumerable<string> abilityIds = selections ?? (string.Equals(state.ClassId, "druid", StringComparison.OrdinalIgnoreCase)
            ? state.SelectedClassAbilityIds : Enumerable.Empty<string>());
        if (!abilityIds.Select(RulesEngine.ExtractClassAbilityBaseId).Contains(AbilityId, StringComparer.OrdinalIgnoreCase))
            return 0;

        if (state.IsLevelUpMode && updatingCharacter is not null)
        {
            int savedLevel = updatingCharacter.ClassLevels.FirstOrDefault(pair =>
                string.Equals(pair.Key, "druid", StringComparison.OrdinalIgnoreCase)).Value;
            if (state.LevelUpPendingExperienceGain <= 0 && savedLevel > 0)
                return savedLevel;
            int xp = (int)Math.Clamp((long)CharacterProgressionService.GetExperienceForClass(updatingCharacter, "druid")
                + Math.Max(0, state.LevelUpPendingExperienceGain), 0L, int.MaxValue);
            return CharacterProgressionService.GetLevelForExperience("druid", xp);
        }

        if (state.IsExistingCharacterMode && !state.IsLevelUpMode)
            return CharacterProgressionService.GetLevelForExperience("druid", state.GetExistingExperienceForClass("druid"));

        return Math.Max(1, state.CharacterLevel);
    }
}