using System.Text.Json;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

int checks = 0;
void Equal<T>(T expected, T actual, string message)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{message}: expected {expected}; actual {actual}");
}
void Contains(string text, string part, string message) => Equal(true, text.Contains(part, StringComparison.Ordinal), message);
NonweaponProficiencyDefinition Def(string name, string? id = null)
    => new(id ?? "nwp_" + name.ToLowerInvariant().Replace(",", "").Replace(" ", "_"),
        name, "General", 1, "Wisdom", 0, "test", CpCost: 3, PlayersOptionBaseRating: 7);
CharacterSheet Sheet(CharGenState state) => new()
{
    CharacterMode = state.CharacterMode, RaceId = state.RaceId, ClassId = state.ClassId,
    ClassIds = new(state.SelectedClassIds), NonweaponProficiencyIds = new(state.SelectedNonweaponProficiencyIds),
    SelectedTraitIds = new(state.SelectedTraitIds), KitFreeNwpIds = new(state.KitFreeNwpIds),
    SelectedNonweaponProficiencyImprovements = new(state.SelectedNonweaponProficiencyImprovements),
    UnspentCharacterPoints = 9, SpentNwpCharacterPoints = 85, SpentWeaponCharacterPoints = 14,
    CpPerLevel = 4, RacialPointBudget = 25, ClassPointBudget = 160
};
void Bonus(CharGenState state, string target, int expected)
{
    Equal(expected, NwpSynergyRules.GetPermanentBonus(state, Def(target)), target + " state permanent");
    Equal(expected, NwpSynergyRules.GetPermanentBonus(Sheet(state), Def(target)), target + " sheet permanent");
}

// Load for the FIRST time with a different working directory: the service must use only the resource.
string originalDirectory = Environment.CurrentDirectory;
try
{
    Environment.CurrentDirectory = Path.GetTempPath();
    Equal(37, NwpSynergyRules.Rules.Count, "Embedded JSON loads without runtime paths");
}
finally { Environment.CurrentDirectory = originalDirectory; }

// 1. Tracking has three contexts, but only TWO source grants: neither is permanent.
var animals = new CharGenState
{
    CharacterMode = "players_option", SelectedNonweaponProficiencyIds = new() { "nwp_warrior_animal_lore" },
    SelectedTraitIds = new() { "trait_animal_empathy" }, ClassId = "ranger", CharacterLevel = 8
};
var tracking = Def("Tracking");
Bonus(animals, "Tracking", 0);
foreach (var (context, expected) in new[] { ("people", 0), ("animals", 2), ("wild", 4) })
{
    Equal(expected, NwpSynergyRules.EvaluateConditionalBonus(animals, tracking, new[] { context }), "Tracking " + context);
    Equal(expected, NwpSynergyRules.EvaluateConditionalBonus(Sheet(animals), tracking, new[] { context }), "Sheet Tracking " + context);
}
Equal(4, NwpSynergyRules.EvaluateConditionalBonus(animals, tracking, new[] { "WILD", "wild", "animals", "non-domesticated animals" }), "Wild implies animals once");
Equal(2, NwpSynergyRules.EvaluateConditionalBonus(animals, tracking, new[] { "domesticated animals" }), "Domestic animals do not activate Empathy");
Equal(0, NwpSynergyRules.EvaluateConditionalBonus(animals, tracking, new[] { "wild_people", "unknown" }), "Unknown contexts do not suffix-match");
Equal(2, NwpSynergyRules.GetContributions(animals, tracking).Count, "No third/invented Tracking grant or Ranger double count");
string trackingSummary = NwpSynergyRules.GetConditionalSummary(animals, tracking, 23);
Contains(trackingSummary, "animals: +2 (animal_lore_tracking), target 25", "Separate Lore condition");
Contains(trackingSummary, "wild: +2 (animal_empathy_tracking), target 25", "Separate Empathy condition, not blanket +4");
Equal(2, trackingSummary.Split(Environment.NewLine).Length, "Every Tracking condition separate");

// 2. Set Snares gets broad permanent Lore, and Empathy only for the eight-hour small-animal check.
Bonus(animals, "Set Snares", 2);
var snares = Def("Set Snares");
Equal(0, NwpSynergyRules.EvaluateConditionalBonus(animals, snares, new[] { "pit_creation" }), "Pit creation does not get Empathy");
Equal(2, NwpSynergyRules.EvaluateConditionalBonus(animals, snares, new[] { "small_animal_snare" }), "Small-animal check gets conditional Empathy");
Contains(NwpSynergyRules.GetConditionalSummary(animals, snares, 18), "target 20", "Already-permanent target is not given Lore twice");
Equal(2, NwpSynergyRules.GetPermanentBonus(animals, Def("", "nwp_rogue_set_snares")), "ID-only grouped target alias");
Equal(2, NwpSynergyRules.GetPermanentBonus(animals, Def("Set Snares", "custom_target")), "Named custom target alias");

// 3. Riding grants are cumulative but repeat ownership/group variants never multiply them.
var riding = new CharGenState
{
    CharacterMode = "players_option",
    SelectedNonweaponProficiencyIds = new() { "nwp_general_animal_handling", "animal_handling", "nwp_animal_training", "nwp_warrior_animal_training" },
    SelectedTraitIds = new() { "trait_animal_empathy", "AnimalEmpathy", "TRAIT_ANIMAL_EMPATHY" }
};
Bonus(riding, "Riding, Land", 4);
Bonus(riding, "Riding, Airborne", 4);
Equal(4, NwpSynergyRules.GetPermanentBonus(riding, Def("", "nwp_general_land_based_riding")), "Land-based ID alias");
Equal(4, NwpSynergyRules.GetPermanentBonus(riding, Def("", "nwp_air_based_riding")), "Air-based ID alias");
Equal(3, NwpSynergyRules.GetContributions(riding, Def("Riding, Land")).Count, "Three named source IDs survive dedup");

// 4. Empathy is NOT Animal Empathy, even in compact, camel-case, or prefixed IDs.
Bonus(riding, "Astrology", 0);
Bonus(riding, "Etiquette", 0);
Bonus(riding, "Reading Lips", 0);
riding.SelectedTraitIds.Add("trait_Empathy");
Bonus(riding, "Astrology", 1);
Bonus(riding, "Etiquette", 2);
Bonus(riding, "Reading Lips", 2);
var impostor = new CharGenState { CharacterMode = "players_option", SelectedTraitIds = new() { "trait_unknown_empathy" }, SelectedNonweaponProficiencyIds = new() { "nwp_unknown_animal_lore" } };
Bonus(impostor, "Etiquette", 0);
Bonus(impostor, "Set Snares", 0);
Equal(0, NwpSynergyRules.GetPermanentBonus(animals, Def("", "nwp_unknown_set_snares")), "Unknown target prefixes do not suffix-match");

// 5. Knowledge/Memory affect only verified targets, not universal history or literacy.
var memory = new CharGenState { CharacterMode = "players_option", SelectedTraitIds = new() { "trait_obscure_knowledge", "trait_precise_memory" } };
Bonus(memory, "Ancient History", 3);
Bonus(memory, "Local History", 3);
Bonus(memory, "Heraldry", 2);
Bonus(memory, "Ancient Languages", 2);
Bonus(memory, "Reading/Writing", 0);
memory.SelectedTraitIds.Remove("trait_obscure_knowledge");
Bonus(memory, "Ancient History", 0);
Bonus(memory, "Heraldry", 0);

// 6. Other confirmed traits; unsupported craft/music/HP/qualitative relations remain zero.
var crafts = new CharGenState
{
    CharacterMode = "players_option",
    SelectedTraitIds = new() { "Impersonation", "Artistic Ability", "Music/Singing", "Music/Instrument", "Internal Compass", "Keen Smell" },
    SelectedNonweaponProficiencyIds = new() { "Herbalism", "Carpentry", "Blacksmithing", "Acting" }
};
foreach (var (name, value) in new[] { ("Disguise", 2), ("Painting", 2), ("Pottery", 2), ("Singing", 2), ("Navigation", 1), ("Hunting", 2),
    ("Sculpting", 0), ("Weaving", 0), ("Musical Instrument", 0), ("Healing", 0), ("Engineering", 0), ("Bowyer/Fletcher", 0) })
    Bonus(crafts, name, value);
Equal(0, NwpSynergyRules.EvaluateConditionalBonus(crafts, Def("Healing"), new[] { "herbalism_dose" }), "HP healing is never a rating");

// 7. Racial/subrace prefixes are bounded to race ownership, not a universal NWP increase.
var race = new CharGenState { CharacterMode = "players_option", RaceId = "race_dwarf_mountain" };
Bonus(race, "Stonemasonry", 2);
Bonus(race, "Weaponsmithing", 1);
Bonus(race, "Tailoring", 0);
race.RaceId = "halfling_stout";
Bonus(race, "Tailoring", 1);
Bonus(race, "Weaving", 1);
Bonus(race, "Weaponsmithing", 0);
race.BaseRaceId = "dwarf";
Equal(2, NwpSynergyRules.GetPermanentBonus(race, Def("Stonemasonry")), "Chargen base race takes precedence");
race.BaseRaceId = "";
var namedRace = new CharacterSheet { CharacterMode = "players_option", RaceName = "Dwarf" };
Equal(2, NwpSynergyRules.GetPermanentBonus(namedRace, Def("Stonemasonry")), "Sheet race name fallback");

// 8. Navigation reports both star grants, does not silently stack +5, keeps Compass permanent.
var stars = new CharGenState
{
    CharacterMode = "players_option",
    SelectedNonweaponProficiencyIds = new() { "Astrology", "Astronomy" }, SelectedTraitIds = new() { "Internal Compass" }
};
Bonus(stars, "Navigation", 1);
Equal(0, NwpSynergyRules.EvaluateConditionalBonus(stars, Def("Navigation"), Array.Empty<string>()), "No visible stars, no conditional bonus");
Equal(3, NwpSynergyRules.EvaluateConditionalBonus(stars, Def("Navigation"), new[] { "stars_visible", "stars_visible" }), "Highest grouped star grant only");
Equal(3, NwpSynergyRules.EvaluateConditionalBonus(Sheet(stars), Def("Navigation"), new[] { "stars_visible" }), "Sheet same stacking");
string starSummary = NwpSynergyRules.GetConditionalSummary(stars, Def("Navigation"), 11);
Contains(starSummary, "astrology_navigation), target 13", "Astrology reported independently");
Contains(starSummary, "astronomy_navigation), target 14", "Astronomy reported independently");
Contains(starSummary, "stacking unresolved", "Stacking uncertainty explicit");
stars.SelectedNonweaponProficiencyIds.Remove("Astronomy");
Equal(2, NwpSynergyRules.EvaluateConditionalBonus(stars, Def("Navigation"), new[] { "stars_visible" }), "Astrology alone retains +2");

// 9. Relevant task assertions must be explicit; never universal Agriculture/Rope/Spellcraft/Tightrope.
var tasks = new CharGenState
{
    CharacterMode = "players_option",
    ClassId = "wizard", SelectedTraitIds = new() { "Animal Empathy", "Climate Sense" },
    SelectedNonweaponProficiencyIds = new() { "Rope Use", "Spellcraft", "Tightrope Walking" }
};
foreach (string name in new[] { "Agriculture", "Mountaineering", "Spellcraft", "Tightrope Walking" })
{
    Bonus(tasks, name, 0);
    Equal(0, NwpSynergyRules.EvaluateConditionalBonus(tasks, Def(name), Array.Empty<string>()), "Task conditions absent: " + name);
}
Equal(2, NwpSynergyRules.EvaluateConditionalBonus(tasks, Def("Agriculture"), new[] { "animal_empathy_relevant" }), "Agriculture relevant animal task only");
Equal(2, NwpSynergyRules.EvaluateConditionalBonus(tasks, Def("Agriculture"), new[] { "climate_sense_relevant" }), "Agriculture relevant climate task only");
Equal(0, NwpSynergyRules.EvaluateConditionalBonus(tasks, Def("Agriculture"), new[] { "irrigation" }), "No guessed agriculture relevance");
Equal(2, NwpSynergyRules.EvaluateConditionalBonus(tasks, Def("Mountaineering"), new[] { "rope_involved" }), "Rope d20 bonus only");
Equal(0, NwpSynergyRules.EvaluateConditionalBonus(tasks, Def("Mountaineering"), new[] { "rope_climbing_percentage" }), "No percentage bonus in d20 API");
Equal(2, NwpSynergyRules.EvaluateConditionalBonus(tasks, Def("Spellcraft"), new[] { "see_and_hear_casting" }), "Both see and hear");
Equal(0, NwpSynergyRules.EvaluateConditionalBonus(tasks, Def("Spellcraft"), new[] { "see_casting" }), "Seeing alone insufficient");
Equal(6, NwpSynergyRules.EvaluateConditionalBonus(tasks, Def("Spellcraft"), new[] { "see_and_hear_casting", "see_components", "own_specialty_or_school" }), "Explicit Spellcraft conditions stack");
tasks.ClassId = "cleric";
Equal(0, NwpSynergyRules.EvaluateConditionalBonus(tasks, Def("Spellcraft"), new[] { "own_specialty_or_school" }), "School bonus requires wizard ownership");
Equal(5, NwpSynergyRules.EvaluateConditionalBonus(tasks, Def("Tightrope Walking"), new[] { "flat_surface_over_one_inch", "balance_pole" }), "Explicit tightrope surface and equipment stack");

// 10. Read-only/idempotent state/sheet APIs, legacy names, serialization, free CP, and every rule/alias.
animals.KitFreeNwpIds.Add("Animal Lore");
animals.SelectedNonweaponProficiencyImprovements["nwp_tracking"] = 4;
animals.SelectedNonweaponProficiencyImprovements["nwp_set_snares"] = 5;
animals.ExistingStartingUnspentCharacterPoints = 9;
animals.ExistingCpPerLevel = 4;
animals.RacialCarryoverToClassPoints = 3;
string stateBefore = JsonSerializer.Serialize(animals);
var sheet = Sheet(animals);
sheet.NonweaponProficiencies.Add("Animal Lore 12 (8 CP: 3 purchase + 5 improvement)");
sheet.Traits.Add("Animal Empathy (4 CP)");
sheet.StructuredNonweaponProficiencies.Add(new() { ProficiencyId = "nwp_warrior_animal_lore", DisplayName = "Animal Lore", TargetNumber = 12, ImprovementCharacterPoints = 5, TotalCharacterPoints = 8 });
string sheetBefore = JsonSerializer.Serialize(sheet);
for (int i = 0; i < 3; i++)
{
    Equal(2, NwpSynergyRules.GetContributions(sheet, tracking).Count, "Repeated sheet representations deduplicate");
    Equal(2, NwpSynergyRules.GetPermanentBonus(sheet, snares), "Repeated Lore once");
    Equal(4, NwpSynergyRules.EvaluateConditionalBonus(sheet, tracking, new[] { "wild", "wild" }), "Repeated evaluation idempotent");
    Equal(trackingSummary, NwpSynergyRules.GetConditionalSummary(sheet, tracking, 23), "State/sheet summaries agree");
    NwpSynergyRules.GetConditionalSummary(animals, snares, 18);
    NwpSynergyRules.GetPermanentBonus(animals, snares);
    NwpSynergyRules.EvaluateConditionalBonus(animals, tracking, new[] { "wild" });
}
Equal(stateBefore, JsonSerializer.Serialize(animals), "All chargen fields/CP/allocations unchanged");
Equal(sheetBefore, JsonSerializer.Serialize(sheet), "All sheet fields/CP/targets unchanged");
var reloadedState = JsonSerializer.Deserialize<CharGenState>(stateBefore)!;
var reloadedSheet = JsonSerializer.Deserialize<CharacterSheet>(sheetBefore)!;
Equal(4, NwpSynergyRules.EvaluateConditionalBonus(reloadedState, tracking, new[] { "wild" }), "State native JSON round trip");
Equal(4, NwpSynergyRules.EvaluateConditionalBonus(reloadedSheet, tracking, new[] { "wild" }), "Sheet native JSON round trip");
Equal(85, reloadedSheet.SpentNwpCharacterPoints, "No extra synergy CP");
Equal(9, reloadedSheet.UnspentCharacterPoints, "No silent refund");
var legacy = new CharacterSheet { CharacterMode = "players_option", NonweaponProficiencies = new() { "Animal Lore 12 (8 CP)" }, Traits = new() { "Animal Empathy (4 CP)" } };
Equal(4, NwpSynergyRules.EvaluateConditionalBonus(legacy, tracking, new[] { "wild" }), "Legacy named source fallback");
Equal(0, NwpSynergyRules.GetPermanentBonus(legacy, Def("Etiquette")), "Legacy Animal Empathy not Empathy");

// 11. Owning the prerequisites in core_rules never activates SP synergies.
var core = new CharGenState
{
    CharacterMode = "core_rules", RaceId = "dwarf", ClassId = "wizard",
    SelectedNonweaponProficiencyIds = new() { "Animal Lore", "Astrology", "Astronomy" },
    SelectedTraitIds = new() { "Animal Empathy", "Empathy", "Internal Compass" }
};
var coreSheet = Sheet(core);
foreach (var definition in new[] { snares, tracking, Def("Navigation"), Def("Etiquette"), Def("Stonemasonry"), Def("Spellcraft") })
{
    Bonus(core, definition.Name, 0);
    Equal(0, NwpSynergyRules.GetContributions(core, definition).Count, "Core state has no contributions: " + definition.Name);
    Equal(0, NwpSynergyRules.GetContributions(coreSheet, definition).Count, "Core sheet has no contributions: " + definition.Name);
    string[] contexts = { "small_animal_snare", "wild", "stars_visible", "own_specialty_or_school" };
    Equal(0, NwpSynergyRules.EvaluateConditionalBonus(core, definition, contexts), "Core state has no conditional synergy: " + definition.Name);
    Equal(0, NwpSynergyRules.EvaluateConditionalBonus(coreSheet, definition, contexts), "Core sheet has no conditional synergy: " + definition.Name);
    Equal("", NwpSynergyRules.GetConditionalSummary(core, definition, 10), "Core state has no summary: " + definition.Name);
    Equal("", NwpSynergyRules.GetConditionalSummary(coreSheet, definition, 10), "Core sheet has no summary: " + definition.Name);
}

// 12. Exercise the actual exchange hydration, not just the read-only synergy queries.
var exchangeSnares = new NonweaponProficiencyDefinition(
    "nwp_set_snares", "Set Snares", "Warrior", 1, "Intelligence", 0, "test",
    CpCost: 4, PlayersOptionBaseRating: 8);
var exchangeTracking = Def("Tracking");
var catalog = new CharacterOptionCatalog(
    new[] { exchangeSnares, exchangeTracking, Def("Animal Lore") },
    Array.Empty<TraitDefinition>(), Array.Empty<DisadvantageDefinition>());
CharacterSheet SnaresSheet() => new()
{
    CharacterMode = "players_option",
    Abilities = new() { ["int"] = 10 },
    NonweaponProficiencyIds = new() { exchangeSnares.Id, "nwp_animal_lore" },
    SelectedTraitIds = new() { "trait_animal_empathy" },
    SelectedNonweaponProficiencyImprovements = new() { [exchangeSnares.Id] = 6 },
    UnspentCharacterPoints = 9, SpentNwpCharacterPoints = 10
};
void CheckContributions(CharacterNonweaponProficiencyRecord record, int expectedCount)
{
    Equal(expectedCount, record.BonusContributions.Count, record.DisplayName + " recorded contribution count");
    Equal(expectedCount, record.BonusContributions.Select(entry => entry.RuleId).Distinct().Count(), record.DisplayName + " no duplicate contributions");
    foreach (var entry in record.BonusContributions)
    {
        var source = NwpSynergyRules.Rules.Single(rule => rule.Id == entry.RuleId);
        Equal(source.Modifier, entry.Modifier, entry.RuleId + " recorded modifier");
        Equal(source.Situational, entry.Situational, entry.RuleId + " recorded permanence");
        Equal(source.Condition, entry.Condition, entry.RuleId + " recorded condition");
        Equal(source.SourceReference, entry.SourceReference, entry.RuleId + " exact source attribution");
        Equal(source.StackingGroup, entry.StackingGroup, entry.RuleId + " recorded stacking group");
        Equal(0, entry.CharacterPointCost, entry.RuleId + " contribution costs no CP");
    }
}
var synchronized = SnaresSheet();
for (int i = 0; i < 3; i++)
{
    FoundryCharacterExchangeService.SynchronizeCharacterData(synchronized, catalog: catalog);
    var record = synchronized.StructuredNonweaponProficiencies.Single(item => item.ProficiencyId == exchangeSnares.Id);
    Equal(16, record.TargetNumber, "Set Snares permanent target 8 + Int10 modifier0 + improvement6 + Lore2");
    Equal(2, record.BonusContributions.Where(entry => !entry.Situational).Sum(entry => entry.Modifier), "Only Lore is permanent");
    var empathy = record.BonusContributions.Single(entry => entry.RuleId == "animal_empathy_set_snares");
    Equal(true, empathy.Situational, "Small-animal Empathy is not permanent");
    Equal("small_animal_snare", empathy.Condition, "Empathy requires small-animal check");
    Equal(2, empathy.Modifier, "Small-animal recorded modifier");
    Equal(2, NwpSynergyRules.EvaluateConditionalBonus(synchronized, exchangeSnares, new[] { "small_animal_snare" }), "Small-animal conditional +2 only");
    Contains(NwpSynergyRules.GetConditionalSummary(synchronized, exchangeSnares, record.TargetNumber), "target 18", "Conditional target does not reapply permanent Lore");
    CheckContributions(record, 2);
    Equal(1, synchronized.StructuredNonweaponProficiencies.Count(item => item.ProficiencyId == exchangeSnares.Id), "Sync has one Set Snares record");
    Equal(4, record.InitialCost, "Set Snares acquisition stays 4 CP");
    Equal(6, record.ImprovementCharacterPoints, "Set Snares paid improvement stays 6 CP");
    Equal(10, record.TotalCharacterPoints, "Set Snares total stays 10 CP");
    Equal(6, synchronized.SelectedNonweaponProficiencyImprovements[exchangeSnares.Id], "Improvement history unchanged");
    Equal(9, synchronized.UnspentCharacterPoints, "Sync does not consume or refund CP");
    Equal(10, synchronized.SpentNwpCharacterPoints, "Sync does not charge synergy CP");
    synchronized = JsonSerializer.Deserialize<CharacterSheet>(JsonSerializer.Serialize(synchronized))!;
    Equal(16, synchronized.StructuredNonweaponProficiencies.Single(item => item.ProficiencyId == exchangeSnares.Id).TargetNumber, "Save/reload retains permanent target");
}
var imported = SnaresSheet();
imported.SelectedNonweaponProficiencyImprovements.Clear();
imported.StructuredNonweaponProficiencies.Add(new()
{
    Id = exchangeSnares.Id, ProficiencyId = exchangeSnares.Id,
    DisplayName = exchangeSnares.Name, TargetNumber = 77
});
for (int i = 0; i < 3; i++)
{
    imported = JsonSerializer.Deserialize<CharacterSheet>(JsonSerializer.Serialize(imported))!;
    FoundryCharacterExchangeService.SynchronizeCharacterData(imported, catalog: catalog);
    var record = imported.StructuredNonweaponProficiencies.Single(item => item.ProficiencyId == exchangeSnares.Id);
    Equal(77, record.TargetNumber, "Imported target without improvement history is retained, not reconstructed");
    Equal(0, imported.SelectedNonweaponProficiencyImprovements.Count, "Sync never invents imported improvement history");
    Equal(9, imported.UnspentCharacterPoints, "Imported sync leaves unspent CP unchanged");
    CheckContributions(record, 2);
}

// 13. Ranger's existing class bonus stays separate from all four animal-source combinations.
foreach (var (name, lore, empathy) in new[]
{
    ("none", false, false), ("lore", true, false), ("empathy", false, true), ("both", true, true)
})
{
    var ranger = new CharGenState
    {
        CharacterMode = "players_option", ClassId = "ranger", CharacterLevel = 8,
        SelectedClassAbilityIds = new() { RangerTrackingRules.TrackingAbilityId }
    };
    if (lore) ranger.SelectedNonweaponProficiencyIds.Add("nwp_animal_lore");
    if (empathy) ranger.SelectedTraitIds.Add("trait_animal_empathy");
    Equal(true, RangerTrackingRules.HasTrackingGrant(ranger), name + " Ranger class grant owned");
    Equal(false, ranger.SelectedNonweaponProficiencyIds.Contains(exchangeTracking.Id), name + " Tracking initially unacquired");
    RangerTrackingRules.EnsureGrantedTracking(ranger, catalog.NonweaponProficiencies);
    RangerTrackingRules.EnsureGrantedTracking(ranger, catalog.NonweaponProficiencies);
    Equal(1, ranger.SelectedNonweaponProficiencyIds.Count(id => id == exchangeTracking.Id), name + " class grant automatically acquires Tracking once");
    Equal(0, ranger.SelectedNonweaponProficiencyImprovements[exchangeTracking.Id], name + " grant has no paid improvement");
    Equal(true, NwpGrantRules.IsFree(ranger, exchangeTracking), name + " Tracking free acquisition");
    Equal(0, NwpGrantRules.GetAcquisitionCost(ranger, exchangeTracking), name + " Tracking acquisition CP0");
    Equal(7, RangerTrackingRules.GetBonus(ranger, exchangeTracking), name + " level8 Ranger permanent class bonus 5 + 8/3");
    Bonus(ranger, "Tracking", 0);
    int animalBonus = lore ? 2 : 0;
    int wildBonus = animalBonus + (empathy ? 2 : 0);
    Equal(animalBonus, NwpSynergyRules.EvaluateConditionalBonus(ranger, exchangeTracking, new[] { "animals" }), name + " Ranger state animal context");
    Equal(wildBonus, NwpSynergyRules.EvaluateConditionalBonus(ranger, exchangeTracking, new[] { "wild" }), name + " Ranger state wild context");
    string expectedSummary = lore && empathy ? ", animal target 16, non-domesticated animal target 18"
        : lore ? ", animal target 16" : empathy ? ", non-domesticated animal target 16" : "";
    Equal(expectedSummary, RangerTrackingRules.GetConditionalTargetSummary(ranger, exchangeTracking, 14), name + " existing Ranger conditional summary");
    var rangerSheet = Sheet(ranger);
    rangerSheet.Level = 8;
    rangerSheet.ClassLevels["ranger"] = 8;
    rangerSheet.SelectedClassAbilityIds = new(ranger.SelectedClassAbilityIds);
    rangerSheet.Abilities["wis"] = 10;
    for (int i = 0; i < 2; i++)
    {
        FoundryCharacterExchangeService.SynchronizeCharacterData(rangerSheet, catalog: catalog);
        var record = rangerSheet.StructuredNonweaponProficiencies.Single(item => item.ProficiencyId == exchangeTracking.Id);
        Equal(true, RangerTrackingRules.HasTrackingGrant(rangerSheet), name + " sheet retains class grant");
        Equal(7, RangerTrackingRules.GetBonus(rangerSheet), name + " sheet Ranger bonus remains7");
        Equal(14, record.TargetNumber, name + " Tracking base7 + ability0 + class7, no conditional leakage");
        Equal(true, record.GrantedByClass, name + " Tracking recorded class grant");
        Equal(0, record.InitialCost, name + " Tracking recorded free acquisition");
        Equal(0, record.ImprovementCharacterPoints, name + " Tracking no paid improvement");
        Equal(0, record.TotalCharacterPoints, name + " Tracking total CP0");
        Equal(0, NwpSynergyRules.GetPermanentBonus(rangerSheet, exchangeTracking), name + " no permanent animal synergy");
        Equal(animalBonus, NwpSynergyRules.EvaluateConditionalBonus(rangerSheet, exchangeTracking, new[] { "animals" }), name + " sheet animal context");
        Equal(wildBonus, NwpSynergyRules.EvaluateConditionalBonus(rangerSheet, exchangeTracking, new[] { "wild" }), name + " sheet wild context");
        Equal(9, rangerSheet.UnspentCharacterPoints, name + " free grant does not consume unspent CP");
        CheckContributions(record, (lore ? 1 : 0) + (empathy ? 1 : 0));
        rangerSheet = JsonSerializer.Deserialize<CharacterSheet>(JsonSerializer.Serialize(rangerSheet))!;
    }
}

// 14. A positive imported class grant is authoritative without an improvement-history key.
var retainedTracking = new CharacterSheet
{
    Name = "Retained Tracking", CharacterMode = "players_option", ClassId = "ranger", Level = 8,
    ClassLevels = new() { ["ranger"] = 8 },
    SelectedClassAbilityIds = new() { RangerTrackingRules.TrackingAbilityId },
    NonweaponProficiencyIds = new() { exchangeTracking.Id, "nwp_animal_lore" },
    SelectedTraitIds = new() { "trait_animal_empathy" },
    Abilities = new() { ["str"] = 10, ["dex"] = 10, ["con"] = 10, ["int"] = 10, ["wis"] = 10, ["cha"] = 10 },
    StructuredNonweaponProficiencies = new()
    {
        new() { Id = exchangeTracking.Id, ProficiencyId = exchangeTracking.Id, DisplayName = "Tracking", TargetNumber = 25 }
    },
    UnspentCharacterPoints = 9, SpentNwpCharacterPoints = 3
};
for (int i = 0; i < 3; i++)
{
    FoundryCharacterExchangeService.SynchronizeCharacterData(retainedTracking, catalog: catalog);
    var record = retainedTracking.StructuredNonweaponProficiencies.Single(item => item.ProficiencyId == exchangeTracking.Id);
    Equal(25, record.TargetNumber, "Positive imported Tracking grant remains25 on repeated sync");
    Equal(false, retainedTracking.SelectedNonweaponProficiencyImprovements.ContainsKey(exchangeTracking.Id), "Class grant sync never invents a Tracking improvement key");
    Equal<bool?>(null, record.SynergiesIncludedInTarget, "Imported Tracking target composition stays unknown");
    Equal(true, record.GrantedByClass, "Retained Tracking is still a class grant");
    Equal(0, record.InitialCost, "Retained Tracking class acquisition is free");
    Equal(0, record.ImprovementCharacterPoints, "Retained Tracking has no paid improvements");
    Equal(0, record.TotalCharacterPoints, "Retained Tracking total paid CP remains zero");
    Equal(4, NwpSynergyRules.EvaluateConditionalBonus(retainedTracking, exchangeTracking, new[] { "wild" }), "Retained Tracking animal contributions remain conditional");
    CheckContributions(record, 2);
}

// 15–16. Zero is a real catalog base; explicit zero history must not compound synergy.
var zeroSnares = exchangeSnares with { PlayersOptionBaseRating = 0 };
var blindfighting = new NonweaponProficiencyDefinition(
    "nwp_blind_fighting", "Blind-fighting", "Warrior", 2, "Wisdom", 0, "test",
    CpCost: 4, PlayersOptionBaseRating: 0);
var catalogLore = new NonweaponProficiencyDefinition(
    "nwp_animal_lore", "Animal Lore", "Warrior", 1, "Intelligence", 0, "test",
    CpCost: 3, PlayersOptionBaseRating: 7, PlayersOptionCheckAbility: "int_knowledge");
var safetyCatalog = new CharacterOptionCatalog(
    new[] { zeroSnares, blindfighting, catalogLore, exchangeTracking },
    Array.Empty<TraitDefinition>(), Array.Empty<DisadvantageDefinition>());
CharacterSheet SafetySheet(string name, string id, int improvementCp) => new()
{
    Name = name, CharacterMode = "players_option",
    Abilities = new() { ["str"] = 10, ["dex"] = 10, ["con"] = 10, ["int"] = 10, ["wis"] = 10, ["cha"] = 10 },
    SubAbilities = new()
    {
        ["str_stamina"] = 10, ["str_muscle"] = 10, ["dex_aim"] = 10, ["dex_balance"] = 10,
        ["con_health"] = 10, ["con_fitness"] = 10, ["int_reason"] = 10, ["int_knowledge"] = 10,
        ["wis_intuition"] = 10, ["wis_willpower"] = 10, ["wis_perception"] = 10,
        ["cha_leadership"] = 10, ["cha_appearance"] = 10
    },
    NonweaponProficiencyIds = new() { id, catalogLore.Id },
    SelectedTraitIds = new() { "trait_animal_empathy" },
    SelectedNonweaponProficiencyImprovements = new() { [id] = improvementCp, [catalogLore.Id] = 0 },
    UnspentCharacterPoints = 9, SpentNwpCharacterPoints = 7 + improvementCp
};
var zeroBaseSnares = SafetySheet("Zero-base Set Snares", zeroSnares.Id, 0);
var zeroBaseBlind = SafetySheet("Zero-base Blind-fighting", blindfighting.Id, 0);
foreach (var (character, id, target, contributionCount) in new[]
{
    (zeroBaseSnares, zeroSnares.Id, 2, 2), (zeroBaseBlind, blindfighting.Id, 0, 0)
})
{
    for (int i = 0; i < 3; i++)
    {
        FoundryCharacterExchangeService.SynchronizeCharacterData(character, catalog: safetyCatalog);
        var record = character.StructuredNonweaponProficiencies.Single(item => item.ProficiencyId == id);
        Equal(target, record.TargetNumber, character.Name + " uses zero catalog base on every sync");
        Equal<bool?>(true, record.SynergiesIncludedInTarget, character.Name + " recalculated composition is known");
        Equal(true, character.SelectedNonweaponProficiencyImprovements.ContainsKey(id), character.Name + " explicit zero history retained");
        Equal(0, character.SelectedNonweaponProficiencyImprovements[id], character.Name + " zero history unchanged");
        Equal(0, record.ImprovementCharacterPoints, character.Name + " no paid improvement invented");
        Equal(4, record.InitialCost, character.Name + " acquisition unchanged");
        Equal(4, record.TotalCharacterPoints, character.Name + " synergy adds no CP");
        Equal(9, character.UnspentCharacterPoints, character.Name + " no silent CP refund");
        Equal(7, character.SpentNwpCharacterPoints, character.Name + " paid total unchanged");
        CheckContributions(record, contributionCount);
    }
}

// 17. A kit waives acquisition, never the paid improvements or their target increase.
var kitFree = SafetySheet("Kit-free paid Set Snares", zeroSnares.Id, 5);
kitFree.KitFreeNwpIds.Add(zeroSnares.Id);
kitFree.SpentNwpCharacterPoints = 8; // Lore purchase3 + paid Snares improvements5.
for (int i = 0; i < 3; i++)
{
    FoundryCharacterExchangeService.SynchronizeCharacterData(kitFree, catalog: safetyCatalog);
    var record = kitFree.StructuredNonweaponProficiencies.Single(item => item.ProficiencyId == zeroSnares.Id);
    Equal(7, record.TargetNumber, "Kit-free target is base0 + ability0 + paid5 + Lore2");
    Equal(0, record.InitialCost, "Kit-free acquisition costs0");
    Equal(true, record.GrantedByKit, "Kit-free grant is recorded");
    Equal(false, record.GrantedByClass, "Kit grant is not misclassified as class grant");
    Equal(5, record.ImprovementCharacterPoints, "Kit-free paid improvements retained");
    Equal(5, record.TotalCharacterPoints, "Kit-free total includes paid improvements only");
    Equal(5, kitFree.SelectedNonweaponProficiencyImprovements[zeroSnares.Id], "Kit-free improvement history unchanged");
    Equal(8, kitFree.SpentNwpCharacterPoints, "Kit-free paid sheet total unchanged");
    Equal(9, kitFree.UnspentCharacterPoints, "Kit-free sync does not refund improvements");
    Equal<bool?>(true, record.SynergiesIncludedInTarget, "Kit-free recalculated composition is known");
    CheckContributions(record, 2);
}

// 18. Known Lore history uses catalog base7 and Knowledge, not legacy base8/Intuition.
var knownLore = SafetySheet("Known Animal Lore history", zeroSnares.Id, 0);
knownLore.SelectedNonweaponProficiencyImprovements[catalogLore.Id] = 5;
knownLore.SubAbilities["int_knowledge"] = 16;
knownLore.SubAbilities["wis_intuition"] = 3;
knownLore.SpentNwpCharacterPoints = 12;
knownLore.StructuredNonweaponProficiencies.Add(new()
{
    Id = catalogLore.Id, ProficiencyId = catalogLore.Id, DisplayName = catalogLore.Name,
    Ability = "wis_intuition", TargetNumber = 77
});
for (int i = 0; i < 3; i++)
{
    FoundryCharacterExchangeService.SynchronizeCharacterData(knownLore, catalog: safetyCatalog);
    var record = knownLore.StructuredNonweaponProficiencies.Single(item => item.ProficiencyId == catalogLore.Id);
    Equal("int_knowledge", record.Ability, "Known Lore history uses catalog Knowledge ability");
    Equal(15, record.TargetNumber, "Known Lore target is catalog7 + Knowledge16 modifier3 + paid5");
    Equal<bool?>(true, record.SynergiesIncludedInTarget, "Known Lore target is recalculated");
    Equal(3, record.InitialCost, "Known Lore acquisition stays3");
    Equal(5, record.ImprovementCharacterPoints, "Known Lore paid improvement stays5");
    Equal(8, record.TotalCharacterPoints, "Known Lore total stays8");
    Equal(5, knownLore.SelectedNonweaponProficiencyImprovements[catalogLore.Id], "Known Lore history unchanged");
    CheckContributions(record, 0);
}

// 19. Core exchange hydration has no permanent or conditional SP contributions.
var coreExchange = SafetySheet("Core exchange Set Snares", zeroSnares.Id, 0);
coreExchange.CharacterMode = "core_rules";
for (int i = 0; i < 3; i++)
{
    FoundryCharacterExchangeService.SynchronizeCharacterData(coreExchange, catalog: safetyCatalog);
    var record = coreExchange.StructuredNonweaponProficiencies.Single(item => item.ProficiencyId == zeroSnares.Id);
    Equal(10, record.TargetNumber, "Core exchange target uses ability10 without Lore synergy");
    Equal<bool?>(null, record.SynergiesIncludedInTarget, "Core target has no SP composition flag");
    CheckContributions(record, 0);
    Equal(0, NwpSynergyRules.GetPermanentBonus(coreExchange, zeroSnares), "Core exchange permanent synergy absent");
    Equal(0, NwpSynergyRules.EvaluateConditionalBonus(coreExchange, zeroSnares, new[] { "small_animal_snare" }), "Core exchange conditional synergy absent");
    Equal(7, coreExchange.SpentNwpCharacterPoints, "Core exchange paid total unchanged");
}

// 20. Native JSON, Codex export, and Foundry exchange retain targets, attribution, and paid counts.
foreach (var (character, exchangeCatalog) in new[]
{
    (retainedTracking, catalog), (zeroBaseSnares, safetyCatalog), (zeroBaseBlind, safetyCatalog),
    (kitFree, safetyCatalog), (knownLore, safetyCatalog), (coreExchange, safetyCatalog)
})
{
    string recordsBefore = JsonSerializer.Serialize(character.StructuredNonweaponProficiencies);
    string historyBefore = JsonSerializer.Serialize(character.SelectedNonweaponProficiencyImprovements);
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    string nativeJson = JsonSerializer.Serialize(character, options);
    string codexJson = FoundryCharacterExchangeService.ExportCodex(character, options, catalog: exchangeCatalog);
    string exchangeJson = FoundryCharacterExchangeService.Export(character, options, catalog: exchangeCatalog);
    Equal(true, FoundryCharacterExchangeService.TryImport(exchangeJson, options, out var exchangeReloaded, out string warning, catalog: exchangeCatalog), character.Name + " exchange import succeeds");
    Equal("", warning, character.Name + " current exchange imports without legacy warning");
    foreach (var reloaded in new[]
    {
        JsonSerializer.Deserialize<CharacterSheet>(nativeJson, options)!,
        JsonSerializer.Deserialize<CharacterSheet>(codexJson, options)!, exchangeReloaded!
    })
    {
        Equal(recordsBefore, JsonSerializer.Serialize(reloaded.StructuredNonweaponProficiencies), character.Name + " roundtrip retains target/composition/contributions/paid record counts");
        Equal(historyBefore, JsonSerializer.Serialize(reloaded.SelectedNonweaponProficiencyImprovements), character.Name + " roundtrip retains exact paid history keys and counts");
        Equal(character.SpentNwpCharacterPoints, reloaded.SpentNwpCharacterPoints, character.Name + " roundtrip retains spent CP");
        Equal(character.UnspentCharacterPoints, reloaded.UnspentCharacterPoints, character.Name + " roundtrip retains unspent CP");
        Equal(string.Join("|", character.KitFreeNwpIds), string.Join("|", reloaded.KitFreeNwpIds), character.Name + " roundtrip retains kit grants");
        FoundryCharacterExchangeService.SynchronizeCharacterData(reloaded, catalog: exchangeCatalog);
        Equal(recordsBefore, JsonSerializer.Serialize(reloaded.StructuredNonweaponProficiencies), character.Name + " roundtrip resync stays idempotent");
        Equal(historyBefore, JsonSerializer.Serialize(reloaded.SelectedNonweaponProficiencyImprovements), character.Name + " roundtrip resync never invents history");
    }
}

// Independently enumerated audit manifest: coverage cannot pass just by iterating an incomplete JSON file.
string[] expectedIds = ("animal_lore_set_snares animal_empathy_handling animal_empathy_training animal_empathy_charioteering "
    + "animal_handling_riding animal_training_riding animal_empathy_riding empathy_astrology empathy_etiquette empathy_reading_lips "
    + "obscure_knowledge_ancient_history obscure_knowledge_heraldry obscure_knowledge_local_history precise_memory_ancient_languages "
    + "impersonation_disguise artistic_ability_painting artistic_ability_pottery music_singing_singing internal_compass_navigation keen_smell_hunting "
    + "dwarf_stonemasonry dwarf_weaponsmithing halfling_tailoring halfling_weaving animal_lore_tracking animal_empathy_tracking "
    + "animal_empathy_set_snares animal_empathy_agriculture climate_sense_agriculture astrology_navigation astronomy_navigation "
    + "rope_use_mountaineering spellcraft_see_and_hear spellcraft_components wizard_spellcraft_school tightrope_flat_surface tightrope_balance_pole").Split(' ');
Equal(string.Join("|", expectedIds.Order()), string.Join("|", NwpSynergyRules.Rules.Select(rule => rule.Id).Order()), "All audited numeric rules included, no invented rules");
Equal(24, NwpSynergyRules.Rules.Count(rule => !rule.Situational), "Permanent inventory count");
Equal(13, NwpSynergyRules.Rules.Count(rule => rule.Situational), "Conditional inventory count");

string FindRoot()
{
    for (var directory = new DirectoryInfo(originalDirectory); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "docs", "NWP_SYNERGY_AUDIT.md"))) return directory.FullName;
    throw new InvalidOperationException("Run these source-provenance tests from the workspace.");
}
string root = FindRoot();
foreach (var rule in NwpSynergyRules.Rules)
{
    Equal(0, rule.CpCost, rule.Id + " zero CP");
    Equal(true, File.Exists(Path.Combine(root, rule.SourceReference)), rule.Id + " exact bundled source path");
    Equal(true, rule.SourceReference.StartsWith("Assets/Core Rules/WEBHELP/SP/", StringComparison.Ordinal), "No invented page references");
    foreach (string source in rule.Source)
    foreach (string target in rule.Target)
    {
        var owner = new CharGenState { CharacterMode = "players_option" };
        switch (rule.SourceKind)
        {
            case "proficiency": owner.SelectedNonweaponProficiencyIds.Add(source); break;
            case "trait": owner.SelectedTraitIds.Add(source); break;
            case "race": owner.RaceId = source; break;
            case "class": owner.ClassId = source; break;
        }
        var definition = Def(target, "custom_target");
        Equal(1, NwpSynergyRules.GetContributions(owner, definition).Count(entry => entry.Id == rule.Id), rule.Id + " source/target name alias");
        Equal(1, NwpSynergyRules.GetContributions(Sheet(owner), definition).Count(entry => entry.Id == rule.Id), rule.Id + " sheet alias");
        Equal(rule.Situational ? 0 : rule.Modifier, NwpSynergyRules.GetPermanentBonus(owner, definition), rule.Id + " permanence");
        Equal(rule.Situational ? rule.Modifier : 0,
            NwpSynergyRules.EvaluateConditionalBonus(owner, definition, new[] { rule.Condition, rule.Condition }), rule.Id + " matching condition once");
        Equal(0, NwpSynergyRules.GetContributions(new CharGenState(), definition).Count, rule.Id + " missing prerequisite");
        if (rule.Situational)
            Contains(NwpSynergyRules.GetConditionalSummary(owner, definition, 10), rule.Id, rule.Id + " separate summary");

        // Every configured source/target alias also works as an ID, not just as a display name.
        var prefixedOwner = new CharGenState { CharacterMode = "players_option" };
        switch (rule.SourceKind)
        {
            case "proficiency": prefixedOwner.SelectedNonweaponProficiencyIds.Add("nwp_general_" + source); break;
            case "trait": prefixedOwner.SelectedTraitIds.Add("trait_" + source); break;
            case "race": prefixedOwner.RaceId = "race_" + source; break;
            case "class": prefixedOwner.ClassId = "class_" + source; break;
        }
        var idDefinition = Def("", "nwp_warrior_" + target);
        Equal(1, NwpSynergyRules.GetContributions(prefixedOwner, idDefinition).Count(entry => entry.Id == rule.Id), rule.Id + " prefixed ID aliases");
        Equal(1, NwpSynergyRules.GetContributions(Sheet(prefixedOwner), idDefinition).Count(entry => entry.Id == rule.Id), rule.Id + " sheet prefixed ID aliases");
    }
}
Equal(true, ((IList<NwpSynergyRule>)NwpSynergyRules.Rules).IsReadOnly, "Rule collection immutable");
Equal(true, ((IList<string>)NwpSynergyRules.Rules[0].Source).IsReadOnly, "Source alias collection immutable");
Equal(true, ((IList<string>)NwpSynergyRules.Rules[0].Target).IsReadOnly, "Target alias collection immutable");
Console.WriteLine($"PASS: {checks} NWP synergy checks across 20 scenarios (including 4 Ranger cases) and all {NwpSynergyRules.Rules.Count} bundled rules.");