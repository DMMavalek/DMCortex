using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

int checks = 0;
var failures = new List<string>();
void Equal<T>(T expected, T actual, string message)
{
    checks++;
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        failures.Add($"{message}: expected {expected}, actual {actual}");
}

var tracking = new NonweaponProficiencyDefinition(
    "nwp_tracking", "Tracking", "General", 2, "wis", -6, "Tracking regression fixture",
    CpCost: 3, PlayersOptionBaseRating: 5, PlayersOptionCheckAbility: "wis");
var other = tracking with { Id = "nwp_other", Name = "Other" };
var definitions = new[] { tracking, other };
var catalog = new CharacterOptionCatalog(definitions, Array.Empty<TraitDefinition>(), Array.Empty<DisadvantageDefinition>());
string grant = RangerTrackingRules.TrackingAbilityId;
string encodedGrant = RulesEngine.BuildClassAbilitySelectionEntry(grant, "Forest trail");
string caseVariantGrant = RulesEngine.BuildClassAbilitySelectionEntry(grant.ToUpperInvariant(), "Forest trail");

CharGenState RangerState(int level = 1, string mode = "players_option") => new()
{
    CharacterMode = mode,
    ClassId = "ranger",
    CharacterLevel = level,
    SelectedClassAbilityIds = new() { grant },
};

CharacterSheet RangerSheet(int level = 1, string mode = "players_option") => new()
{
    CharacterMode = mode,
    ClassId = "ranger",
    Level = level,
    ClassLevels = new(StringComparer.OrdinalIgnoreCase) { ["ranger"] = level },
    SelectedClassAbilityIds = new() { grant },
    Abilities = new() { ["wis"] = 10 },
};

// Exercise the actual ::text:: selection encoding, not an invented suffix.
Equal($"{grant}::text::Forest trail", encodedGrant, "Actual ability selection format");
Equal(grant, RulesEngine.ExtractClassAbilityBaseId(encodedGrant), "Encoded selection base ID");
var state = RangerState();
var sheet = RangerSheet();
Equal(true, RangerTrackingRules.HasTrackingGrant(state), "Primary state grant with class-list fallback");
Equal(true, RangerTrackingRules.HasTrackingGrant(sheet), "Primary sheet grant with class-list fallback");
Equal(5, RangerTrackingRules.GetBonus(state, tracking), "PO state base Ranger bonus");
Equal(5, RangerTrackingRules.GetBonus(sheet), "PO sheet base Ranger bonus");
Equal(0, RangerTrackingRules.GetBonus(state, other), "Unrelated proficiency gets no tracking bonus");
RangerTrackingRules.EnsureGrantedTracking(state, definitions);
Equal(tracking.Id, state.SelectedNonweaponProficiencyIds.Single(), "Grant adds Tracking only");
Equal(0, state.SelectedNonweaponProficiencyImprovements[tracking.Id], "Grant initializes improvement history");

// Boundaries immediately before, at and after each third-level improvement.
foreach (var (level, expected) in new[] { (2, 5), (3, 6), (4, 6), (5, 6), (6, 7), (7, 7), (8, 7), (9, 8), (10, 8) })
{
    Equal(expected, RangerTrackingRules.GetBonus(RangerState(level), tracking), $"PO state level {level}");
    Equal(expected, RangerTrackingRules.GetBonus(RangerSheet(level)), $"PO sheet level {level}");
}

state = RangerState(9);
sheet = RangerSheet(9);
state.SelectedClassAbilityIds.Clear();
sheet.SelectedClassAbilityIds.Clear();
Equal(false, RangerTrackingRules.HasTrackingGrant(state), "State grant absent");
Equal(false, RangerTrackingRules.HasTrackingGrant(sheet), "Sheet grant absent");
RangerTrackingRules.EnsureGrantedTracking(state, definitions);
Equal(0, state.SelectedNonweaponProficiencyIds.Count, "Missing grant must not autoadd Tracking");
Equal(0, state.SelectedNonweaponProficiencyImprovements.Count, "Missing grant must not add history");
Equal(5, RangerTrackingRules.GetBonus(state, tracking), "Ungrant state retains base only, no level improvements");
Equal(5, RangerTrackingRules.GetBonus(sheet), "Ungrant sheet retains base only, no level improvements");

// Both primary selections and per-class selections normalize encoded IDs/casing.
foreach (bool byClass in new[] { false, true })
{
    state = RangerState(6, "Player's Option");
    sheet = RangerSheet(6, "PO");
    state.ClassId = sheet.ClassId = byClass ? "druid" : "RANGER";
    state.SelectedClassIds = new() { "druid", "RANGER" };
    sheet.ClassIds = new() { "druid", "RANGER" };
    state.SelectedClassAbilityIds = new() { caseVariantGrant };
    sheet.SelectedClassAbilityIds = new() { caseVariantGrant };
    if (byClass)
    {
        state.SelectedClassAbilityIds.Clear();
        sheet.SelectedClassAbilityIds.Clear();
        state.SelectedAbilitiesByClass["RaNgEr"] = new() { caseVariantGrant };
        sheet.SelectedAbilitiesByClass["RaNgEr"] = new() { caseVariantGrant };
    }
    Equal(true, RangerTrackingRules.HasTrackingGrant(state), $"Normalized state grant, byClass={byClass}");
    Equal(true, RangerTrackingRules.HasTrackingGrant(sheet), $"Normalized sheet grant, byClass={byClass}");
    Equal(7, RangerTrackingRules.GetBonus(state, tracking), $"Normalized state bonus, byClass={byClass}");
    Equal(7, RangerTrackingRules.GetBonus(sheet), $"Normalized sheet bonus, byClass={byClass}");
    RangerTrackingRules.EnsureGrantedTracking(state, definitions);
    Equal(1, state.SelectedNonweaponProficiencyIds.Count, $"Normalized state auto-grant, byClass={byClass}");
}

// An ability on another class, or a stale Ranger selection without Ranger class membership, is not a grant.
state = RangerState(9);
sheet = RangerSheet(9);
state.ClassId = sheet.ClassId = "druid";
state.SelectedClassIds = new() { "druid", "ranger" };
sheet.ClassIds = new() { "druid", "ranger" };
Equal(false, RangerTrackingRules.HasTrackingGrant(state), "Primary Druid ability cannot grant Ranger tracking to state");
Equal(false, RangerTrackingRules.HasTrackingGrant(sheet), "Primary Druid ability cannot grant Ranger tracking to sheet");
state.SelectedAbilitiesByClass["ranger"] = new() { grant };
sheet.SelectedAbilitiesByClass["ranger"] = new() { grant };
state.SelectedClassIds.Remove("ranger");
sheet.ClassIds.Remove("ranger");
Equal(false, RangerTrackingRules.HasTrackingGrant(state), "State requires Ranger class membership");
Equal(false, RangerTrackingRules.HasTrackingGrant(sheet), "Sheet requires Ranger class membership");
Equal(0, RangerTrackingRules.GetBonus(state, tracking), "Non-Ranger state gets no bonus");
Equal(0, RangerTrackingRules.GetBonus(sheet), "Non-Ranger sheet gets no bonus");

state = RangerState();
RangerTrackingRules.EnsureGrantedTracking(state, new[] { other });
Equal(0, state.SelectedNonweaponProficiencyIds.Count, "Missing catalog definition adds nothing");
Equal(0, state.SelectedNonweaponProficiencyImprovements.Count, "Missing definition adds no history");
RangerTrackingRules.EnsureGrantedTracking(state, definitions);
state.SelectedNonweaponProficiencyImprovements[tracking.Id] = 4;
for (int visit = 0; visit < 3; visit++)
    RangerTrackingRules.EnsureGrantedTracking(state, definitions);
Equal(1, state.SelectedNonweaponProficiencyIds.Count, "Reentry never duplicates Tracking");
Equal(4, state.SelectedNonweaponProficiencyImprovements[tracking.Id], "Reentry preserves purchased improvements");
state.SelectedNonweaponProficiencyIds[0] = tracking.Id.ToUpperInvariant();
RangerTrackingRules.EnsureGrantedTracking(state, definitions);
Equal(1, state.SelectedNonweaponProficiencyIds.Count, "Existing case-variant selection is not duplicated");
Equal(4, state.SelectedNonweaponProficiencyImprovements[tracking.Id], "Case-variant reentry retains history");

// Regression: the Ranger level, not the primary Druid/overall level, determines the improvement.
// New-character mode ignores stale Add Existing values and retains its configured level.
foreach (bool existing in new[] { false, true })
{
    state = new CharGenState
    {
        CharacterMode = "players_option",
        ClassMode = "multiclass",
        ClassId = "druid",
        SelectedClassIds = new() { "druid", "ranger" },
        CharacterLevel = 10,
        IsExistingCharacterMode = existing,
        ExistingUsesSeparateClassExperience = true,
        ExistingStartingExperience = 125000,
        ExistingClassExperiencePoints = new(StringComparer.OrdinalIgnoreCase) { ["druid"] = 125000, ["ranger"] = 150000 },
        SelectedAbilitiesByClass = new() { ["ranger"] = new() { encodedGrant } },
    };
    if (existing)
        state.RecalculateLevelFromExistingExperience();
    Equal(10, CharacterProgressionService.GetLevelForExperience("druid", 125000), $"Druid XP fixture, existing={existing}");
    Equal(8, CharacterProgressionService.GetLevelForExperience("ranger", 150000), $"Ranger XP fixture, existing={existing}");
    Equal(10, state.CharacterLevel, $"Primary level stays 10, existing={existing}");
    Equal(true, RangerTrackingRules.HasTrackingGrant(state), $"Multiclass state grant, existing={existing}");
    RangerTrackingRules.EnsureGrantedTracking(state, definitions);
    Equal(1, state.SelectedNonweaponProficiencyIds.Count, $"Multiclass state grant adds once, existing={existing}");
    Equal(existing ? 7 : 8, RangerTrackingRules.GetBonus(state, tracking), $"Ranger state bonus honors creation mode, existing={existing}");

    sheet = new CharacterSheet
    {
        CharacterMode = "players_option",
        ClassMode = "multiclass",
        ClassId = "druid",
        ClassIds = new() { "druid", "ranger" },
        Level = 10,
        ExperiencePoints = 125000,
        ClassExperiencePoints = new(StringComparer.OrdinalIgnoreCase) { ["druid"] = 125000, ["ranger"] = 150000 },
        ClassLevels = new(StringComparer.OrdinalIgnoreCase) { ["druid"] = 10, ["ranger"] = 8 },
        SelectedAbilitiesByClass = new() { ["ranger"] = new() { encodedGrant } },
    };
    Equal(true, RangerTrackingRules.HasTrackingGrant(sheet), $"Multiclass sheet grant, existing={existing}");
    Equal(7, RangerTrackingRules.GetBonus(sheet), $"Druid 10/Ranger 8 sheet bonus, existing={existing}");
}

sheet = RangerSheet(6);
sheet.ClassLevels.Clear();
Equal(7, RangerTrackingRules.GetBonus(sheet), "Legacy sheet falls back to overall level");
sheet.ClassLevels["RANGER"] = 3;
Equal(6, RangerTrackingRules.GetBonus(sheet), "Sheet class-level lookup is case-insensitive");

// Public Foundry sync verifies free initial purchase, not free improvement CP.
// Core checks exercise cancellation of Tracking's -6 and zero structured slot cost;
// no WPF view construction/reflection or UI slot-budget assertion is needed here.
foreach (string mode in new[] { "players_option", "core_rules" })
{
    state = RangerState(9, mode);
    sheet = RangerSheet(9, mode);
    int expectedBonus = mode == "core_rules" ? 6 : 8;
    Equal(expectedBonus, RangerTrackingRules.GetBonus(state, tracking), $"{mode} state bonus");
    Equal(expectedBonus, RangerTrackingRules.GetBonus(sheet), $"{mode} sheet bonus");
    RangerTrackingRules.EnsureGrantedTracking(state, definitions);
    Equal(1, state.SelectedNonweaponProficiencyIds.Count, $"{mode} grant present");
    sheet.NonweaponProficiencyIds = new() { tracking.Id };
    sheet.SelectedNonweaponProficiencyImprovements[tracking.Id] = 4;
    FoundryCharacterExchangeService.SynchronizeCharacterData(sheet, catalog: catalog);
    var record = sheet.StructuredNonweaponProficiencies.Single();
    Equal(tracking.Id, record.ProficiencyId, $"{mode} structured Tracking ID");
    Equal(0, record.InitialCost, $"{mode} class grant has no initial CP/slot cost");
    Equal(true, record.GrantedByClass, $"{mode} structured class grant marker");
    Equal(4, record.ImprovementCharacterPoints, $"{mode} paid improvements retained");
    Equal(4, record.TotalCharacterPoints, $"{mode} total excludes initial purchase only");
    if (mode == "core_rules")
        Equal(12, record.TargetNumber, "Core target = Wisdom 10 - 6 + Ranger 6 + two paid slot improvements");
    FoundryCharacterExchangeService.SynchronizeCharacterData(sheet, catalog: catalog);
    Equal(1, sheet.StructuredNonweaponProficiencies.Count, $"{mode} repeated sync does not duplicate records");
    Equal(4, sheet.StructuredNonweaponProficiencies.Single().ImprovementCharacterPoints, $"{mode} repeated sync retains improvements");
    sheet.SelectedClassAbilityIds.Clear();
    FoundryCharacterExchangeService.SynchronizeCharacterData(sheet, catalog: catalog);
    record = sheet.StructuredNonweaponProficiencies.Single();
    Equal(tracking.CpCost, record.InitialCost, $"{mode} nongranted purchase is not free");
    Equal(false, record.GrantedByClass, $"{mode} nongranted purchase has no class grant marker");
    if (mode == "core_rules")
    {
        Equal(6, RangerTrackingRules.GetBonus(state, tracking), "Core six-point compensation is level-independent");
        sheet.ClassId = "druid";
        Equal(0, RangerTrackingRules.GetBonus(sheet), "Core compensation requires Ranger class");
    }
}

foreach (string failure in failures)
    Console.Error.WriteLine($"FAIL: {failure}");
Console.WriteLine($"{(failures.Count == 0 ? "PASS" : "FAIL")}: {checks} Ranger Tracking regression checks; {failures.Count} failures.");
return failures.Count == 0 ? 0 : 1;