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
        throw new InvalidOperationException($"{message}: expected {expected}; actual {actual}");
}

var formatter = typeof(CharGenReviewScreen).GetMethod("FormatNwpSummary", BindingFlags.Static | BindingFlags.NonPublic)!;
string Summary(CharGenState state, NonweaponProficiencyDefinition proficiency)
    => (string)formatter.Invoke(null, new object[] { state, proficiency })!;

var state = new CharGenState
{
    CharacterMode = "players_option",
    ClassId = "druid",
    ClassMode = "multiclass",
    SelectedClassIds = new() { "druid", "ranger" },
    IsExistingCharacterMode = true,
    ExistingUsesSeparateClassExperience = true,
    ExistingClassExperiencePoints = new() { ["druid"] = 125000, ["ranger"] = 150000 },
    ExistingCpPerLevel = 4,
    Abilities = new() { ["int"] = 10, ["wis"] = 14, ["dex"] = 16 },
    SubAbilities = new() { ["int_knowledge"] = 10, ["wis_intuition"] = 16, ["wis_willpower"] = 12 },
};
state.ModifiedAbilities = new(state.Abilities);
state.RecalculateLevelFromExistingExperience();
Equal(64, state.GetExistingCharacterPointPool(), "Lys advancement pool unchanged");

// Self-contained definitions capture the audited inputs, not user-local catalog overrides.
var cases = new[]
{
    (new NonweaponProficiencyDefinition("nwp_animal_lore", "Animal Lore", "Warrior", 1, "Intelligence", 0, "fixture", CpCost: 3, PlayersOptionBaseRating: 7, PlayersOptionCheckAbility: "int_knowledge"), 5, 12),
    (new NonweaponProficiencyDefinition("nwp_healing", "Healing", "Priest", 2, "Wisdom", 0, "fixture", CpCost: 4, PlayersOptionBaseRating: 5, PlayersOptionCheckAbility: "wis_intuition"), 8, 16),
    (new NonweaponProficiencyDefinition("nwp_herbalism", "Herbalism", "Priest", 2, "Intelligence", 0, "fixture", CpCost: 3, PlayersOptionBaseRating: 6, PlayersOptionCheckAbility: "int_knowledge"), 13, 19),
    (new NonweaponProficiencyDefinition("nwp_riding_land", "Riding, Land", "General", 1, "Wisdom", 0, "fixture", CpCost: 2, PlayersOptionBaseRating: 8, PlayersOptionCheckAbility: "wis_willpower"), 13, 21),
};
foreach (var (definition, cp, target) in cases)
{
    state.SelectedNonweaponProficiencyIds = new() { definition.Id };
    state.SelectedNonweaponProficiencyImprovements[definition.Id] = cp;
    string expected = $"{definition.Name} {target} ({definition.CpCost + cp} CP: {definition.CpCost} purchase + {cp} improvement)";
    Equal(expected, Summary(state, definition), $"Lys {definition.Name} target and cost");
    state.IsExistingCharacterMode = false;
    state.IsLevelUpMode = true;
    Equal(expected, Summary(state, definition), "Update/save uses same summary formula");
    state.IsLevelUpMode = false;
    Equal(expected, Summary(state, definition), "New character uses same summary formula");
    state.IsExistingCharacterMode = true;
    Equal(cp, state.SelectedNonweaponProficiencyImprovements[definition.Id], "Formatting never changes spent CP");
}

var generic = cases[1].Item1 with { Id = "nwp_fixture", Name = "Fixture" };
foreach (int purchaseCost in new[] { 0, 1, 2, 3, 4, 8 })
{
    var definition = generic with { CpCost = purchaseCost };
    state.SelectedNonweaponProficiencyImprovements[definition.Id] = 3;
    string summary = Summary(state, definition);
    Equal(true, summary.StartsWith("Fixture 11 ("), $"Three CP grants +3 regardless of purchase cost {purchaseCost}");
    Equal(true, summary.Contains("3 improvement"), "Fractional purchase multiple is not rounded away");
}

// Review, native structured saving and exported structured data agree for ordinary definitions.
foreach (int rating in new[] { 1, 3, 5, 8 })
{
    var definition = generic with { PlayersOptionBaseRating = rating };
    state.SelectedNonweaponProficiencyImprovements[definition.Id] = 3;
    Equal(true, Summary(state, definition).StartsWith($"Fixture {rating + 3 + 3} ("), "Low base rating matches NWP page, no artificial floor of five");
    var sheet = new CharacterSheet
    {
        CharacterMode = "players_option", ClassId = "druid",
        Abilities = new(state.Abilities), SubAbilities = new(state.SubAbilities),
        NonweaponProficiencyIds = new() { definition.Id },
        SelectedNonweaponProficiencyImprovements = new() { [definition.Id] = 3 },
        SpentNwpCharacterPoints = 7, UnspentCharacterPoints = 9,
    };
    var catalog = new CharacterOptionCatalog(new[] { definition }, Array.Empty<TraitDefinition>(), Array.Empty<DisadvantageDefinition>());
    FoundryCharacterExchangeService.SynchronizeCharacterData(sheet, catalog: catalog);
    var record = sheet.StructuredNonweaponProficiencies.Single();
    Equal(rating + 6, record.TargetNumber, "Structured target uses full improvement CP");
    Equal(3, record.ImprovementCharacterPoints, "Structured improvement history unchanged");
    Equal(7, record.TotalCharacterPoints, "Purchase remains separate from improvement rate");
    var reloaded = JsonSerializer.Deserialize<CharacterSheet>(JsonSerializer.Serialize(sheet))!;
    FoundryCharacterExchangeService.SynchronizeCharacterData(reloaded, catalog: catalog);
    Equal(record.TargetNumber, reloaded.StructuredNonweaponProficiencies.Single().TargetNumber, "JSON save/reload preserves target");
    Equal(7, reloaded.SpentNwpCharacterPoints, "No CP refund or extra charge");
    Equal(9, reloaded.UnspentCharacterPoints, "Unspent CP unchanged");
}

state.SelectedNonweaponProficiencyImprovements[generic.Id] = -3;
Equal(true, Summary(state, generic).StartsWith("Fixture 8 ("), "Negative improvement history is ignored");
state.SelectedNonweaponProficiencyImprovements.Remove(generic.Id);
Equal(true, Summary(state, generic).StartsWith("Fixture 8 ("), "Missing improvement history means zero");
state.CharacterMode = "core_rules";
state.SelectedNonweaponProficiencyImprovements[generic.Id] = 8;
Equal("Fixture 14", Summary(state, generic), "Core review unchanged and ignores PO improvement CP");

state.CharacterMode = "players_option";
state.SelectedAbilitiesByClass["ranger"] = new() { RangerTrackingRules.TrackingAbilityId };
var tracking = generic with { Id = "nwp_tracking", Name = "Tracking", CpCost = 4, PlayersOptionBaseRating = 7 };
state.SelectedNonweaponProficiencyImprovements[tracking.Id] = 2;
Equal("Tracking 19 (2 CP improvement; granted by class)", Summary(state, tracking), "Free Tracking +7 Ranger bonus +3 Wisdom +2 paid improvements");
Equal(64, state.GetExistingCharacterPointPool(), "Targets do not change advancement pool");
Console.WriteLine($"PASS: {checks} NWP improvement regression checks.");