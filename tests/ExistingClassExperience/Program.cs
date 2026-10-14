using System.Reflection;
using System.Text.Json;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;
using DungeonMasterCortex.Views;

int checks = 0;
void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    checks++;
}

var state = new CharGenState
{
    IsExistingCharacterMode = true,
    ClassMode = "multiclass",
    ClassId = "fighter",
    SelectedClassIds = new() { "fighter", "wizard" },
    ExistingStartingExperience = 900000,
    ExistingExperienceIsGrandTotalForMulticlass = true,
    ExistingUsesSeparateClassExperience = true,
    ExistingClassExperiencePoints = new(StringComparer.OrdinalIgnoreCase) { ["fighter"] = 11000, ["wizard"] = 10000 },
    ExistingCpPerLevel = 3,
    ExistingStartingUnspentCharacterPoints = 7,
};
Equal(11000, state.GetExistingExperienceForClass("FIGHTER"), "Exact fighter XP, no split or bonus reapplied");
Equal(10000, state.GetExistingExperienceForClass("wizard"), "Exact wizard XP");
state.RecalculateLevelFromExistingExperience();
Equal(4, state.CharacterLevel, "Fighter level");
Equal(6, state.GetExistingClassLevelAdvances(), "Both class histories count for CP");
Equal(25, state.GetExistingCharacterPointPool(), "Historical CP plus starting pool");
state.ClassId = "wizard";
state.ExistingClassExperiencePoints["wizard"] = 9000;
state.RecalculateLevelFromExistingExperience();
Equal(3, state.CharacterLevel, "Wizard's own unequal XP determines its level");
Equal(5, state.GetExistingClassLevelAdvances(), "Unequal levels determine historical CP");
state.SelectedClassIds.Add("thief");
state.ExistingClassExperiencePoints["thief"] = 2500;
Equal(7, state.GetExistingClassLevelAdvances(), "Three class histories");
state.SelectedClassIds.Remove("thief");
Equal(5, state.GetExistingClassLevelAdvances(), "Deselected class ignored");
Equal(4, state.GetConfiguredClassLevel("fighter"), "Primary tab uses its own level");
Equal(3, state.GetConfiguredClassLevel("wizard"), "Secondary tab uses its own level");
state.SelectedClassIds.Add("thief");
state.ExistingClassExperiencePoints["thief"] = 5000;
Equal(4, state.GetConfiguredClassLevel("thief"), "Secondary rogue allocation uses own level");
state.SelectedClassIds.Remove("thief");

var sheet = new CharacterSheet
{
    ClassMode = "multiclass",
    ClassId = "fighter",
    ClassIds = new() { "fighter", "wizard" },
    ExperiencePoints = 11000,
    ClassExperiencePoints = new(StringComparer.OrdinalIgnoreCase) { ["fighter"] = 11000, ["wizard"] = 9000 },
    Level = 4,
};
CharacterProgressionService.ApplyExperienceAdjustment(sheet, 0);
Equal(4, sheet.ClassLevels["fighter"], "Saved fighter level");
Equal(3, sheet.ClassLevels["wizard"], "Saved wizard level");
Equal(3, sheet.Level, "Legacy overall level is lower class level");
Equal(11000, sheet.ExperiencePoints, "Legacy XP remains primary class balance");
Equal(17, sheet.Thac0, "Primary combat progression uses primary level");

var copy = JsonSerializer.Deserialize<CharacterSheet>(JsonSerializer.Serialize(sheet))!;
Equal(9000, copy.ClassExperiencePoints["wizard"], "JSON save/load preserves unequal balances");
CharacterProgressionService.ApplyExperienceAdjustment(copy, 0);
Equal(9000, copy.ClassExperiencePoints["wizard"], "Zero recalculation preserves unequal balances");

string Display(Type view, string method)
    => (string)view.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { copy })!;
Equal("4/3", Display(typeof(CharacterRosterScreen), "FormatLevelDisplay"), "Roster independent levels");
Equal("11,000/9,000", Display(typeof(CharacterRosterScreen), "FormatExperienceDisplay"), "Roster independent XP");
Equal("Fighter 4 / Wizard 3", Display(typeof(CharacterSheetsScreen), "FormatLevelDisplay"), "Sheet independent levels");
Equal("Fighter: 11,000 XP / Wizard: 9,000 XP", Display(typeof(CharacterSheetsScreen), "FormatExperienceDisplay"), "Sheet independent XP");
Equal("Fighter: 5,000 XP / Wizard: 1,000 XP", Display(typeof(CharacterSheetsScreen), "FormatExperienceNeededDisplay"), "XP needed uses independent balances");

// Existing shared awards must not erase balances entered through Add Existing.
CharacterProgressionService.ApplyExperienceAdjustment(copy, 1000);
Equal(12000, copy.ClassExperiencePoints["fighter"], "Existing shared award to primary");
Equal(10000, copy.ClassExperiencePoints["wizard"], "Existing shared award preserves secondary offset");
Equal(4, copy.ClassLevels["wizard"], "Secondary class crosses its own threshold");
copy.ExperiencePoints += 500;
CharacterProgressionService.ApplyExperienceAdjustment(copy, 0);
Equal(10500, copy.ClassExperiencePoints["wizard"], "Direct legacy XP update preserves offset");
CharacterProgressionService.ApplyExperienceAdjustment(copy, -int.MaxValue);
Equal(0, copy.ClassExperiencePoints["fighter"], "Primary XP cannot go negative");
Equal(0, copy.ClassExperiencePoints["wizard"], "Secondary XP cannot go negative");

var legacy = JsonSerializer.Deserialize<CharacterSheet>("{\"ClassMode\":\"multiclass\",\"ClassId\":\"fighter\",\"ClassIds\":[\"fighter\",\"wizard\"],\"ExperiencePoints\":11000}")!;
CharacterProgressionService.ApplyExperienceAdjustment(legacy, 0);
Equal(0, legacy.ClassExperiencePoints.Count, "Legacy characters not opted into separate XP");
Equal(4, legacy.ClassLevels["wizard"], "Legacy shared XP unchanged");
Equal(11000, CharacterProgressionService.GetExperienceForClass(legacy, "wizard"), "Legacy balance fallback");

state.ExistingUsesSeparateClassExperience = false;
state.ExistingStartingExperience = 20000;
Equal(10000, state.GetExistingExperienceForClass("wizard"), "Existing total XP split unchanged");
state.ExistingExperienceIsGrandTotalForMulticlass = false;
Equal(20000, state.GetExistingExperienceForClass("wizard"), "Existing same-XP-per-class mode unchanged");
state.ClassMode = "";
state.SelectedClassIds = new() { "fighter" };
state.ExistingUsesSeparateClassExperience = true;
Equal(20000, state.GetExistingExperienceForClass("fighter"), "Single class ignores multiclass option");
state.IsExistingCharacterMode = false;
Equal(false, state.UsesSeparateExistingClassExperience, "New character flow not opted in");
state.IsExistingCharacterMode = true;
state.IsLevelUpMode = true;
Equal(false, state.UsesSeparateExistingClassExperience, "Level-up flow not opted in");
state.Clear();
Equal(false, state.ExistingUsesSeparateClassExperience, "Clear resets new option");
Equal(0, state.ExistingClassExperiencePoints.Count, "Clear resets per-class values");
Console.WriteLine($"PASS: {checks} existing-character class XP regression checks.");