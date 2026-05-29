using System;
using System.Collections.Generic;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Models
{
    public class CharGenState
    {
        public int ExistingStartingUnspentCharacterPoints { get; set; } = 0;
        public Dictionary<string, string> SelectedNonweaponProficiencyNotes { get; set; } = new();
        public Dictionary<string, string> WizardSpecializationById { get; set; } = new();
        // Additional properties for review and compatibility
        public List<string> SelectedClassAbilityIds { get; set; } = new();
        public List<string> SelectedRacialAbilityIds { get; set; } = new();
        public int RacialCarryoverToClassPoints { get; set; } = 0;
        public string WizardSpecializationId { get; set; } = "";
        public bool IsExistingCharacterMode { get; set; } = false;
        public int ExistingStartingExperience { get; set; } = 0;
        public bool ExistingExperienceIsGrandTotalForMulticlass { get; set; } = false;
        public int ExistingCpPerLevel { get; set; } = 0;
        public string Name         { get; set; } = "";
        public string CharacterMode { get; set; } = "core_rules";
        public string Method       { get; set; } = "method_v_4d6_drop_lowest";
        public Dictionary<string, int> Abilities    { get; set; } = new();
        public Dictionary<string, int> ModifiedAbilities { get; set; } = new();
        public Dictionary<string, int> RacialAbilityModifiers { get; set; } = new();
        public Dictionary<string, int> SubAbilities { get; set; } = new();
        public int ExceptionalStrength { get; set; } = 0;
        public bool ExceptionalStrengthLocked { get; set; } = false;
        public string BaseRaceId { get; set; } = "";
        public string RaceId  { get; set; } = "";
        public string ClassId { get; set; } = "";
        public string ClassMode { get; set; } = "";
        public int CharacterLevel { get; set; } = 1;
        public List<string> SelectedClassIds { get; set; } = new();
        public List<string> SelectedNonweaponProficiencyIds { get; set; } = new();
        public List<WeaponProficiencySelection> SelectedWeaponProficiencies { get; set; } = new();
        public List<EquipmentSelection> SelectedEquipment { get; set; } = new();
        public List<GemEntry> StartingGems { get; set; } = new();
        public int StartingPlatinumPieces { get; set; } = 0;
        public int StartingGoldPieces { get; set; } = 0;
        public int StartingSilverPieces { get; set; } = 0;
        public int StartingCopperPieces { get; set; } = 0;
        public int StartingGemCount { get; set; } = 0;
        public int StartingGemValueGoldPieces { get; set; } = 0;
        public bool StartingFundsAssigned { get; set; } = false;
        public string StartingFundsRollSummary { get; set; } = "";
        public List<string> WizardSpellbookIds { get; set; } = new();
        public List<NamedSpellList> WizardSpellLists { get; set; } = new();
        public string EquippedArmorId { get; set; } = "";
        public string EquippedShieldId { get; set; } = "";
        public string EquippedWeaponId { get; set; } = "";
        public string KitId { get; set; } = "";
        public List<string> KitFreeNwpIds { get; set; } = new();
        public List<string> KitRequiredNwpIds { get; set; } = new();
        public List<string> LockedNonweaponProficiencyIds { get; set; } = new();
        public List<string> LockedWeaponProficiencyIds { get; set; } = new();
        public bool IsLevelUpMode { get; set; } = false;
        public int LevelUpCharacterIndex { get; set; } = -1;
        public int LevelUpPendingExperienceGain { get; set; } = 0;
        public int LevelUpPendingHitPointGain { get; set; } = 0;
        public int LevelUpPendingCharacterPointGain { get; set; } = 0;
        public Dictionary<string, string> SelectedSpheres { get; set; } = new();
        public Dictionary<string, bool> SelectedWizardSchools { get; set; } = new();
        public Dictionary<string, List<string>> SelectedAbilitiesByClass { get; set; } = new();
        public List<string> SelectedTraitIds { get; set; } = new();
        public Dictionary<string, string> SelectedDisadvantageSeverities { get; set; } = new();
        public List<LanguageSelection> SelectedLanguages { get; set; } = new();
        public Dictionary<string, int> SelectedNonweaponProficiencyImprovements { get; set; } = new();
        public string RogueSkillArmorProfile { get; set; } = "no_armor";
        public Dictionary<string, int> SelectedRogueSkillPoints { get; set; } = new();
        public Dictionary<string, Dictionary<string, int>> RogueSkillPointsByClass { get; set; } = new();
        public Dictionary<string, string> RogueSkillArmorByClass { get; set; } = new();
        public Dictionary<string, Dictionary<string, string>> SpheresByClass { get; set; } = new();
        public Dictionary<string, Dictionary<string, bool>> SchoolsByClass { get; set; } = new();
        public List<string> BaselineNonweaponProficiencyIds { get; set; } = new();
        public Dictionary<string, int> BaselineNonweaponProficiencyImprovements { get; set; } = new();
        public List<string> BaselineWeaponProficiencyIds { get; set; } = new();
        public List<WeaponProficiencySelection> BaselineWeaponProficiencies { get; set; } = new();
        public List<EquipmentSelection> BaselineEquipmentSelections { get; set; } = new();

        // Methods
        public void Clear()
        {
            Name = "";
            CharacterMode = "core_rules";
            Method = "method_v_4d6_drop_lowest";
            Abilities.Clear();
            ModifiedAbilities.Clear();
            RacialAbilityModifiers.Clear();
            SubAbilities.Clear();
            ExceptionalStrength = 0;
            ExceptionalStrengthLocked = false;
            BaseRaceId = "";
            RaceId = "";
            ClassId = "";
            ClassMode = "";
            CharacterLevel = 1;
            SelectedClassIds.Clear();
            SelectedNonweaponProficiencyIds.Clear();
            SelectedWeaponProficiencies.Clear();
            SelectedEquipment.Clear();
            StartingGems.Clear();
            StartingPlatinumPieces = 0;
            StartingGoldPieces = 0;
            StartingSilverPieces = 0;
            StartingCopperPieces = 0;
            StartingGemCount = 0;
            StartingGemValueGoldPieces = 0;
            StartingFundsAssigned = false;
            StartingFundsRollSummary = "";
            WizardSpellbookIds.Clear();
            WizardSpellLists.Clear();
            EquippedArmorId = "";
            EquippedShieldId = "";
            EquippedWeaponId = "";
            KitId = "";
            KitFreeNwpIds.Clear();
            KitRequiredNwpIds.Clear();
            LockedNonweaponProficiencyIds.Clear();
            LockedWeaponProficiencyIds.Clear();
            IsLevelUpMode = false;
            LevelUpCharacterIndex = -1;
            LevelUpPendingExperienceGain = 0;
            LevelUpPendingHitPointGain = 0;
            LevelUpPendingCharacterPointGain = 0;
            SelectedSpheres.Clear();
            SelectedWizardSchools.Clear();
            SelectedAbilitiesByClass.Clear();
            SelectedTraitIds.Clear();
            SelectedDisadvantageSeverities.Clear();
            SelectedLanguages.Clear();
            SelectedNonweaponProficiencyImprovements.Clear();
            RogueSkillArmorProfile = "no_armor";
            SelectedRogueSkillPoints.Clear();
            RogueSkillPointsByClass.Clear();
            RogueSkillArmorByClass.Clear();
            SpheresByClass.Clear();
            SchoolsByClass.Clear();
            BaselineNonweaponProficiencyIds.Clear();
            BaselineNonweaponProficiencyImprovements.Clear();
            BaselineWeaponProficiencyIds.Clear();
            BaselineWeaponProficiencies.Clear();
            BaselineEquipmentSelections.Clear();
        }

        public void RecalculateLevelFromExistingExperience()
        {
            if (IsLevelUpMode)
            {
                int experience = Math.Max(0, ExistingStartingExperience) + Math.Max(0, LevelUpPendingExperienceGain);
                CharacterLevel = Math.Max(1, CharacterProgressionService.GetLevelForExperience(ClassId, experience));
                return;
            }

            if (IsExistingCharacterMode)
            {
                int experience = Math.Max(0, ExistingStartingExperience);
                CharacterLevel = Math.Max(1, CharacterProgressionService.GetLevelForExperience(ClassId, experience));
            }
        }

        public void SyncLegacyFieldsFromNew() { /* Implement as needed */ }
    }
}