using System.Text.Json;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

int checks = 0;
void Equal<T>(T expected, T actual, string message)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
}
var lys = new CharacterSheet
{
    ClassMode = "multiclass", ClassId = "druid", ClassIds = new() { "druid", "ranger" },
    ClassLevels = new() { ["druid"] = 10, ["ranger"] = 8 },
    ExperiencePoints = 125000,
    ClassExperiencePoints = new() { ["druid"] = 125000, ["ranger"] = 150000 },
    Level = 8, Thac0 = 17, HitPoints = 96, UnspentCharacterPoints = 6,
    Bonuses = new() { AttackBonus = 3, DamageBonus = 2 },
};
string before = JsonSerializer.Serialize(lys);
Equal(13, CharacterProgressionService.GetBaseThac0(lys), "Druid10/Ranger8 uses Ranger13");
Equal(13, CharacterProgressionService.GetCombatBaseThac0(lys), "Display ignores stale primary-class THAC0");
Equal(before, JsonSerializer.Serialize(lys), "Display lookup does not rewrite character");
lys.ClassId = "ranger";
lys.ClassIds.Reverse();
Equal(13, CharacterProgressionService.GetBaseThac0(lys), "Primary and class order do not affect THAC0");
lys.ClassId = "druid";
CharacterProgressionService.ApplyExperienceAdjustment(lys, 0);
Equal(13, lys.Thac0, "Progression stores best class THAC0");
Equal(8, lys.ClassLevels["ranger"], "Ranger uses own XP");
Equal(10, lys.ClassLevels["druid"], "Druid uses own XP");
Equal(6, lys.UnspentCharacterPoints, "Combat correction grants no CP");
Equal(96, lys.HitPoints, "Combat correction does not change HP");
Equal(3, lys.Bonuses.AttackBonus, "Attack bonuses remain separate");
CharacterProgressionService.InitializeCharacterProgression(lys, false);
Equal(13, lys.Thac0, "Initialization does not overwrite best progression");
var copy = JsonSerializer.Deserialize<CharacterSheet>(JsonSerializer.Serialize(lys))!;
Equal(13, CharacterProgressionService.GetCombatBaseThac0(copy), "Save/reload retains best THAC0");

var priest = new CharacterSheet { ClassId = "druid", Level = 10, Thac0 = 12 };
Equal(16, CharacterProgressionService.GetBaseThac0(priest), "Single priest formula unchanged");
Equal(12, CharacterProgressionService.GetCombatBaseThac0(priest), "Single-class manual display value preserved");
foreach (string warrior in new[] { "fighter", "paladin", "ranger" })
{
    var c = new CharacterSheet { ClassId = warrior, Level = 8 };
    Equal(13, CharacterProgressionService.GetBaseThac0(c), warrior + " level8 baseline");
}
var highPriest = new CharacterSheet
{
    ClassMode = "multiclass", ClassId = "ranger", ClassIds = new() { "ranger", "druid" },
    ClassLevels = new() { ["ranger"] = 2, ["druid"] = 20 },
};
Equal(11, CharacterProgressionService.GetBaseThac0(highPriest), "Best progression is not always warrior");
var missing = new CharacterSheet
{
    ClassMode = "multiclass", ClassId = "druid", ClassIds = new() { "druid", "ranger" },
    ExperiencePoints = 125000,
    ClassExperiencePoints = new() { ["druid"] = 125000, ["ranger"] = 150000 },
};
Equal(13, CharacterProgressionService.GetBaseThac0(missing), "Missing class levels derived from each XP balance");
Equal(0, missing.ClassLevels.Count, "Fallback does not mutate saved levels");
Equal("3/2 rounds", CharacterProgressionService.GetBaseAttackRate(lys), "Ranger8 base APR despite Druid primary");
var aprMethod = typeof(DungeonMasterCortex.Views.CharacterSheetsScreen).GetMethod("CalculateWeaponApr",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
string Apr(CharacterSheet c, bool specialized, bool grandMaster = false)
    => (string)aprMethod.Invoke(null, new object?[] { c, null, specialized, grandMaster })!;
lys.AttackRate = "1/round";
Equal("2", Apr(lys, true), "Lys specialization gives two attacks despite stale stored APR");
Equal("3/2", Apr(lys, false), "Unspecialized weapon keeps Ranger8 base");
Equal("3", Apr(lys, true, true), "Grand mastery adds once above specialist rate");
lys.ClassLevels["ranger"] = 6;
Equal("3/2", Apr(lys, true), "Level6 specialist attacks three per two rounds");
lys.ClassLevels["ranger"] = 7;
Equal("2", Apr(lys, true), "Level7 specialist attacks twice");
lys.ClassLevels["ranger"] = 13;
Equal("5/2", Apr(lys, true), "Level13 specialist progression");
lys.ClassLevels["ranger"] = 8;
CharacterProgressionService.InitializeCharacterProgression(lys, false);
Equal("3/2 rounds", lys.AttackRate, "Stored base rate remains unspecialized");
Equal("2", Apr(lys, true), "Specialization is not added twice after initialization");
Console.WriteLine($"PASS: {checks} multiclass combat regression checks.");