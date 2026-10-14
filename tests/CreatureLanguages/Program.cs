using System.Reflection;
using System.Text.Json;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;
using DungeonMasterCortex.Views;

int checks = 0;
void Equal<T>(T expected, T actual, string message)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}
var state = new CharGenState { ClassId = "druid", CharacterLevel = 1 };
Equal(0, DruidCreatureLanguageRules.GetAllowance(state), "No ability means no creature language slots");
state.SelectedClassAbilityIds.Add(DruidCreatureLanguageRules.AbilityId);
Equal(1, DruidCreatureLanguageRules.GetAllowance(state), "One language at first level");
state.CharacterLevel = 10;
Equal(10, DruidCreatureLanguageRules.GetAllowance(state), "One free language for each level");
state.ClassMode = "multiclass";
state.ClassId = "ranger";
state.SelectedClassIds = new() { "ranger", "druid" };
state.SelectedClassAbilityIds.Clear();
state.SelectedAbilitiesByClass["druid"] = new() { DruidCreatureLanguageRules.AbilityId };
state.CharacterLevel = 8;
state.IsExistingCharacterMode = true;
state.ExistingUsesSeparateClassExperience = true;
state.ExistingClassExperiencePoints = new() { ["druid"] = 125000, ["ranger"] = 150000 };
Equal(10, DruidCreatureLanguageRules.GetAllowance(state), "Lys Ranger 8 / Druid 10 gets ten choices");
state.ExistingUsesSeparateClassExperience = false;
state.ExistingStartingExperience = 125000;
Equal(10, DruidCreatureLanguageRules.GetAllowance(state), "Legacy same-XP mode uses Druid table");
state.ExistingStartingExperience = 250000;
state.ExistingExperienceIsGrandTotalForMulticlass = true;
Equal(10, DruidCreatureLanguageRules.GetAllowance(state), "Grand-total entry splits before deriving Druid level");

var sheet = new CharacterSheet
{
    ClassMode = "multiclass", ClassId = "ranger", ClassIds = new() { "ranger", "druid" },
    Level = 8, ExperiencePoints = 150000,
    ClassLevels = new() { ["druid"] = 10, ["ranger"] = 8 },
    ClassExperiencePoints = new() { ["druid"] = 125000, ["ranger"] = 150000 },
};
state.IsLevelUpMode = true;
Equal(10, DruidCreatureLanguageRules.GetAllowance(state, sheet), "Update uses saved Druid level");
state.LevelUpPendingExperienceGain = 75000;
Equal(11, DruidCreatureLanguageRules.GetAllowance(state, sheet), "Druid advances and earns one more choice");
state.LevelUpPendingExperienceGain = 1;
Equal(10, DruidCreatureLanguageRules.GetAllowance(state, sheet), "XP below threshold grants no extra choice");
state.SelectedAbilitiesByClass["druid"].Clear();
Equal(0, DruidCreatureLanguageRules.GetAllowance(state, sheet), "Removing ability removes allowance, not saved names");
state.SelectedAbilitiesByClass["druid"].Add(DruidCreatureLanguageRules.AbilityId);
state.SelectedClassIds.Remove("druid");
Equal(0, DruidCreatureLanguageRules.GetAllowance(state, sheet), "Stale ability cannot grant non-Druid allowance");

sheet.SelectedLanguages = new()
{
    new LanguageSelection { SourceKey = "animal_languages", LanguageName = "Dryads" },
    new LanguageSelection { SourceKey = "animal_languages", LanguageName = "Treants" },
};
var copy = JsonSerializer.Deserialize<CharacterSheet>(JsonSerializer.Serialize(sheet))!;
Equal(2, copy.SelectedLanguages.Count, "Creature choices survive save/reload");
Equal("Dryads", copy.SelectedLanguages[0].LanguageName, "Creature name preserved");
Equal("animal_languages", copy.SelectedLanguages[0].SourceKey, "Existing source ID remains compatible");
var spoken = typeof(CharacterSheetsScreen).GetMethod("IsSpokenLanguageSource", BindingFlags.NonPublic | BindingFlags.Static)!;
foreach (string source in new[] { "animal_languages", "Animal Languages", "Creature Languages (Druid)" })
    Equal(true, (bool)spoken.Invoke(null, new object[] { source })!, "Old and new creature labels display as spoken");
Console.WriteLine($"PASS: {checks} creature-language regression checks.");