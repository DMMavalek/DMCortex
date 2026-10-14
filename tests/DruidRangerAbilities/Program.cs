using System.Reflection;
using System.Text.Json;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
bool Contains(string text, string token) => text.Contains(token, StringComparison.OrdinalIgnoreCase);
// Only the normal engine loaders access disk; all fixtures and serialization stay in memory.
var rules = new RulesEngine(); // Includes workbook loading followed by post-processing.
const string identifyId = "druid_identify", enemyId = "ranger_special_enemy";
var druid = rules.Classes["druid"];
Check(druid.StructuredAbilities.Count(a => a.Id == identifyId) == 1, "Identify exists exactly once");
var identify = druid.StructuredAbilities.Single(a => a.Id == identifyId);
Check(!identify.AutoGranted && identify.PointCost == 5, "Identify is an optional 5 CP purchase");
foreach (string token in new[] { "plants", "animals", "clean water", "3rd level" })
    Check(Contains(identify.Description, token), $"Identify description includes {token}");
var emptyDruid = rules.BuildClassAbilityPackage("druid", characterLevel: 1);
var chosenDruid = rules.BuildClassAbilityPackage("druid", new[] { identifyId }, characterLevel: 1);
Check(emptyDruid.selectedAbilities.All(a => a.Id != identifyId), "Identify is not automatically granted");
Check(chosenDruid.selectedAbilities.Count(a => a.Id == identifyId) == 1, "Identify selectable at creation");
Check(chosenDruid.spent - emptyDruid.spent == 5, "Creation charges 5 CP before the later benefit");
Check(chosenDruid.selectedAbilities.Single(a => a.Id == identifyId).Description == identify.Description,
    "Creation retains the later-benefit description");
var postProcess = typeof(RulesEngine).GetMethod("PostProcessRules", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new InvalidOperationException("PostProcessRules not found");
postProcess.Invoke(rules, null);
Check(rules.Classes["druid"].StructuredAbilities.Count(a => a.Id == identifyId) == 1,
    "Repeated post-processing does not duplicate Identify");

var ranger = rules.Classes["ranger"];
var enemy = ranger.StructuredAbilities.Single(a => a.Id == enemyId);
Check(enemy.RequiresPlayerText, "Runtime special enemy requires player text after workbook loading");
rules.ClassAbilityLibrary.TryGetValue(enemyId, out var libraryEnemy);
if (libraryEnemy is not null) Check(libraryEnemy.RequiresPlayerText, "Library special enemy requires player text");
string runtimeBefore = JsonSerializer.Serialize(enemy), libraryBefore = JsonSerializer.Serialize(libraryEnemy);
string orcsEntry = RulesEngine.BuildClassAbilitySelectionEntry(enemyId, "Orcs");
Check(RulesEngine.ExtractClassAbilityBaseId(orcsEntry) == enemyId, "Selection base ID roundtrips");
Check(RulesEngine.ExtractClassAbilityPlayerText(orcsEntry) == "Orcs", "Selection text roundtrips");
var bare = rules.BuildClassAbilityPackage("ranger", new[] { enemyId }, characterLevel: 1);
var orcs = rules.BuildClassAbilityPackage("ranger", new[] { orcsEntry }, characterLevel: 1);
var orcsAbility = orcs.selectedAbilities.Single(a => a.Id == enemyId);
Check(Contains(orcsAbility.Description, "Selection: Orcs"), "Selected package names Orcs");
Check(orcs.spent == bare.spent && orcs.budget == bare.budget && orcs.remaining == bare.remaining,
    "Text selection preserves package CP accounting");
Check(orcsAbility.PointCost == enemy.PointCost, "Text selection preserves ability CP cost");
Check(enemy.Effect.EnemyAttackBonuses.TryGetValue("special_enemy", out int bonus), "Source has generic enemy bonus");
Check(orcsAbility.Effect.EnemyAttackBonuses.GetValueOrDefault("Orcs") == bonus, "Attack bonus targets Orcs");
Check(!orcsAbility.Effect.EnemyAttackBonuses.ContainsKey("special_enemy"), "Selected bonus is not generic");
string giantsEntry = RulesEngine.BuildClassAbilitySelectionEntry(enemyId, "Giants");
var giants = rules.BuildClassAbilityPackage("ranger", new[] { giantsEntry }, characterLevel: 1);
var giantsAbility = giants.selectedAbilities.Single(a => a.Id == enemyId);
Check(Contains(giantsAbility.Description, "Selection: Giants"), "Independent package names Giants");
Check(giantsAbility.Effect.EnemyAttackBonuses.GetValueOrDefault("Giants") == bonus, "Independent Giants bonus");
Check(!giantsAbility.Effect.EnemyAttackBonuses.ContainsKey("Orcs") &&
    !giantsAbility.Effect.EnemyAttackBonuses.ContainsKey("special_enemy"), "Giants package has no stale targets");
Check(orcsAbility.Effect.EnemyAttackBonuses.GetValueOrDefault("Orcs") == bonus &&
    !orcsAbility.Effect.EnemyAttackBonuses.ContainsKey("Giants"), "Later choice leaves Orcs independent");
Check(JsonSerializer.Serialize(enemy) == runtimeBefore, "Generic runtime effects are unchanged");
Check(JsonSerializer.Serialize(libraryEnemy) == libraryBefore, "Generic library effects are unchanged");

// Deep-clone the ability and replace only the in-memory class record to test unrelated bonuses.
var fixture = JsonSerializer.Deserialize<AbilityDefinition>(runtimeBefore)!;
fixture.Effect.EnemyAttackBonuses["Undead"] = 2;
fixture.Effect.EnemyDamageBonuses["Dragons"] = 3;
rules.Classes["ranger"] = ranger with
{
    StructuredAbilities = ranger.StructuredAbilities.Select(a => a.Id == enemyId ? fixture : a).ToList()
};
try
{
    var selected = rules.BuildClassAbilityPackage("ranger", new[] { orcsEntry }, characterLevel: 1)
        .selectedAbilities.Single(a => a.Id == enemyId);
    Check(selected.Effect.EnemyAttackBonuses.GetValueOrDefault("Undead") == 2, "Other enemy attack bonus preserved");
    Check(selected.Effect.EnemyDamageBonuses.GetValueOrDefault("Dragons") == 3, "Other enemy damage bonus preserved");
    Check(selected.Effect.EnemyAttackBonuses.GetValueOrDefault("Orcs") == bonus &&
        !selected.Effect.EnemyAttackBonuses.ContainsKey("special_enemy"), "Fixture still remaps only generic attack");
    Check(fixture.Effect.EnemyAttackBonuses.ContainsKey("special_enemy") &&
        !fixture.Effect.EnemyAttackBonuses.ContainsKey("Orcs"), "Fixture source is not mutated");
}
finally { rules.Classes["ranger"] = ranger; }
var sheet = new CharacterSheet { SelectedAbilitiesByClass = new() { ["ranger"] = new() { orcsEntry } } };
var restored = JsonSerializer.Deserialize<CharacterSheet>(JsonSerializer.Serialize(sheet))!;
Check(restored.SelectedAbilitiesByClass["ranger"].SequenceEqual(new[] { orcsEntry }), "Sheet preserves exact selection entry");
Check(RulesEngine.ExtractClassAbilityPlayerText(restored.SelectedAbilitiesByClass["ranger"].Single()) == "Orcs",
    "Sheet roundtrip preserves enemy choice");
var sphereFormatter = typeof(DungeonMasterCortex.Views.CharGenClassAbilitiesScreen).GetMethod(
    "FormatSphereAccessSummary", BindingFlags.Static | BindingFlags.NonPublic)!;
string FormatSpheres(Dictionary<string, string> spheres)
    => (string)sphereFormatter.Invoke(null, new object[] { spheres })!;
Check(FormatSpheres(new()) == "Major: None\nMinor: None", "Empty sphere lists are explicit");
Check(FormatSpheres(new() { ["Plant"] = "minor", ["All"] = "major", ["Animal"] = "MAJOR" })
    == "Major: All, Animal\nMinor: Plant", "Sphere summary groups and sorts actual access");
Check(FormatSpheres(new() { ["Healing"] = "both" }) == "Major: Healing\nMinor: Healing", "Both access remains visible in both lists");
Check(FormatSpheres(new() { ["All"] = "minor", ["Weather"] = "minor" })
    == "Major: None\nMinor: All, Weather", "Druid summary does not invent major access");
var druidDefaults = typeof(DungeonMasterCortex.Views.CharGenClassAbilitiesScreen).GetMethod(
    "GetDruidDefaultSpheres", BindingFlags.Static | BindingFlags.NonPublic)!;
var defaultSpheres = (Dictionary<string, string>)druidDefaults.Invoke(null, null)!;
Check(defaultSpheres.Count == 7, "Druid package has six major spheres and one minor");
Check(FormatSpheres(defaultSpheres) == "Major: All, Animal, Elemental, Healing, Plant, Weather\nMinor: Divination",
    "Druid fixed package grants major access and minor Divination");
defaultSpheres["All"] = "minor";
Check(((Dictionary<string, string>)druidDefaults.Invoke(null, null)!)["All"] == "major", "Default package is independently allocated");
Console.WriteLine($"PASS: {checks} druid/ranger ability regression checks.");