using System.Text.Json;
using System.Text.Json.Nodes;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
string foundryJson = File.ReadAllText(Path.Combine(downloads, "Foundry Lys.json"));
string codexJson = File.ReadAllText(Path.Combine(downloads, "Codex Lys.json"));
var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
var rules = new RulesEngine();
CharacterOptionCatalog catalog = new CharacterOptionCatalogService().GetCatalog();

Require(FoundryCharacterExchangeService.TryImport(foundryJson, options, out CharacterSheet? imported, out _, rules, catalog),
    "Foundry fixture was not detected.");
Require(imported is not null, "Foundry fixture did not produce a character.");
Require(imported!.RaceId == "half_elf_po", $"Race ID was {imported.RaceId}.");
Require(imported.ClassMode == "multiclass", $"Class mode was {imported.ClassMode}.");
Require(imported.ClassLevels.GetValueOrDefault("ranger") == 8, "Ranger level was not restored.");
Require(imported.ClassLevels.GetValueOrDefault("druid") == 11, "Druid level was not restored.");
Require(imported.ExperiencePoints == 241909, $"Per-class XP was {imported.ExperiencePoints}.");
Require(imported.FoundrySourceData.HasValue, "Original Foundry payload was not retained.");
Require(JsonEqual(foundryJson, imported.FoundrySourceData!.Value.GetRawText()),
    "Retained Foundry payload differs from its source.");

string savedCharacters = JsonSerializer.Serialize(new List<CharacterSheet> { imported }, options);
CharacterSheet savedAndReloaded = JsonSerializer.Deserialize<List<CharacterSheet>>(savedCharacters, options)?.Single()
    ?? throw new InvalidOperationException("Normal Cortex save/reload lost the imported character.");
Require(savedAndReloaded.FoundrySourceData.HasValue, "Normal Cortex save/reload lost Foundry source data.");
Require(JsonEqual(foundryJson, savedAndReloaded.FoundrySourceData!.Value.GetRawText()),
    "Normal Cortex save/reload changed Foundry source data.");
savedAndReloaded.SavingThrows.ExtensionData["customFoundrySave"] = JsonSerializer.SerializeToElement(42);

string exchangeJson = FoundryCharacterExchangeService.Export(savedAndReloaded, options, rules, catalog);
using JsonDocument exchangeDocument = JsonDocument.Parse(exchangeJson);
JsonElement exchangeRoot = exchangeDocument.RootElement;
Require(exchangeRoot.GetProperty("Schema").GetString() == FoundryCharacterExchangeService.Schema,
    "Exchange schema marker is missing.");
Require(exchangeRoot.GetProperty("SchemaVersion").GetInt32() == FoundryCharacterExchangeService.SchemaVersion,
    "Exchange schema version is incorrect.");
Require(JsonEqual(foundryJson, exchangeRoot.GetProperty("Foundry").GetRawText()),
    "Foundry payload changed during exchange export.");
JsonElement exchangeCodex = exchangeRoot.GetProperty("Codex");
JsonElement structuredNwps = exchangeCodex.GetProperty("NonweaponProficiencies");
Require(structuredNwps.GetArrayLength() == 11, $"Expected 11 structured NWPs, found {structuredNwps.GetArrayLength()}.");
Require(structuredNwps.EnumerateArray().All(record =>
        !string.IsNullOrWhiteSpace(record.GetProperty("Id").GetString())
        && record.GetProperty("Id").GetString() == record.GetProperty("ProficiencyId").GetString()),
    "One or more structured NWPs lacks a stable matching ID.");
string[] nwpIds = exchangeCodex.GetProperty("NonweaponProficiencyIds").EnumerateArray()
    .Select(value => value.GetString() ?? string.Empty).ToArray();
string[] structuredNwpIds = structuredNwps.EnumerateArray()
    .Select(record => record.GetProperty("Id").GetString() ?? string.Empty).ToArray();
Require(nwpIds.SequenceEqual(structuredNwpIds, StringComparer.OrdinalIgnoreCase),
    "Structured NWP order does not agree with NonweaponProficiencyIds.");
JsonElement animalLore = structuredNwps.EnumerateArray().Single(record =>
    record.GetProperty("Id").GetString() == "nwp_animal_lore");
Require(animalLore.GetProperty("Ability").GetString() == "wis_intuition", "Animal Lore ability was not normalized.");
Require(animalLore.GetProperty("TargetNumber").GetInt32() == 12, "Animal Lore final target is not 12.");
Require(animalLore.GetProperty("InitialCost").GetInt32() == 3, "Animal Lore purchase CP is not 3.");
JsonElement saves = exchangeCodex.GetProperty("SavingThrows");
Require(saves.GetProperty("paralyzationPoisonDeath").GetInt32() == 6, "Lys death/poison save is not 6.");
Require(saves.GetProperty("rodStaffWand").GetInt32() == 10, "Lys rod/staff/wand save is not 10.");
Require(saves.GetProperty("petrificationPolymorph").GetInt32() == 9, "Lys petrification save is not 9.");
Require(saves.GetProperty("breathWeapon").GetInt32() == 12, "Lys breath save is not 12.");
Require(saves.GetProperty("spell").GetInt32() == 11, "Lys spell save is not 11.");
Require(saves.GetProperty("customFoundrySave").GetInt32() == 42, "Unknown saving-throw property was not retained.");
Require(exchangeCodex.GetProperty("EquipmentSelections").GetArrayLength() == 2,
    "The two retained Foundry combat weapons were not exposed as structured equipment.");
JsonElement divineSpells = exchangeCodex.GetProperty("DivineSpells");
using JsonDocument foundryDocument = JsonDocument.Parse(foundryJson);
string[] retainedDivineSpellIds = foundryDocument.RootElement.GetProperty("FoundryExtensions")
    .GetProperty("Magic").GetProperty("DivineSpells").EnumerateArray()
    .Select(spell => spell.GetProperty("Id").GetString() ?? string.Empty)
    .Where(id => id.Length > 0)
    .ToArray();
string[] exportedDivineSpellIds = divineSpells.EnumerateArray()
    .Select(spell => spell.GetProperty("Id").GetString() ?? string.Empty)
    .ToArray();
Require(retainedDivineSpellIds.All(id => exportedDivineSpellIds.Contains(id, StringComparer.OrdinalIgnoreCase)),
    "One or more retained Foundry divine spell definitions was not exported in Codex.");
Require(divineSlotsArePopulated(exchangeCodex) && divineSpells.GetArrayLength() > 0,
    "Divine slot state was exported without actual native spell records.");
foreach (JsonElement spell in divineSpells.EnumerateArray())
{
    Require(!string.IsNullOrWhiteSpace(spell.GetProperty("Id").GetString())
            && spell.GetProperty("Id").GetString() == spell.GetProperty("SpellId").GetString(),
        "A divine spell lacks a stable matching Id and SpellId.");
    Require(spell.GetProperty("Level").GetInt32() > 0, "A divine spell lacks its level.");
    foreach (string property in new[] { "Components", "CastingTime", "Duration", "Range", "AreaOfEffect",
                 "SavingThrow", "Description", "Schools", "Materials", "BriefDescription", "PreparedCount",
                 "RemainingUses" })
        Require(spell.TryGetProperty(property, out _), $"Spell {spell.GetProperty("Id").GetString()} lacks {property}.");
}
Require(exchangeCodex.GetProperty("TrackedSpellIds").GetArrayLength()
        == exchangeCodex.GetProperty("ArcaneSpells").GetArrayLength()
        + divineSpells.GetArrayLength()
        + exchangeCodex.GetProperty("PsionicPowers").GetArrayLength(),
    "TrackedSpellIds was not synchronized with exported spell records.");
JsonElement divineSlots = exchangeCodex.GetProperty("DivineSpellSlots");
foreach (int level in Enumerable.Range(1, 6))
    Require(divineSlots.TryGetProperty(level.ToString(), out _), $"Divine slot maximum is missing for level {level}.");
Require(exchangeCodex.GetProperty("RemainingDivineSpellSlots").GetProperty("1").GetInt32() == 4,
    "Remaining level-one divine slots were not retained.");
Require(exchangeCodex.GetProperty("CastingPools").GetArrayLength() == 1,
    "Retained Foundry casting pool was not exported in Codex.");

Require(FoundryCharacterExchangeService.TryImport(exchangeJson, options, out CharacterSheet? roundTripped, out _, rules, catalog),
    "Exchange envelope was not detected.");
Require(roundTripped?.FoundrySourceData.HasValue == true, "Foundry payload was lost during exchange re-import.");
Require(JsonEqual(foundryJson, roundTripped!.FoundrySourceData!.Value.GetRawText()),
    "Foundry payload changed during exchange re-import.");
Require(roundTripped.DivineSpells.Any(spell => spell.ExtensionData.ContainsKey("SaveType")),
    "Unknown retained spell properties were lost from the Codex payload.");
Require(roundTripped.EquipmentSelections.First().ExtensionData.ContainsKey("DamageSM"),
    "Unknown retained equipment properties were lost from the Codex payload.");
Require(roundTripped.SavingThrows.ExtensionData.GetValueOrDefault("customFoundrySave").GetInt32() == 42,
    "Unknown saving-throw property was lost during Codex re-import.");

string[] retainedSubset = roundTripped.DivineSpells.Take(2).Select(spell => spell.Id).ToArray();
roundTripped.ExportSpellIds = retainedSubset.ToList();
roundTripped.HasExplicitSpellExportSelection = true;
string selectedRoundTripJson = FoundryCharacterExchangeService.Export(roundTripped, options, rules, catalog);
using JsonDocument selectedRoundTripDocument = JsonDocument.Parse(selectedRoundTripJson);
Require(selectedRoundTripDocument.RootElement.GetProperty("Codex").GetProperty("DivineSpells").GetArrayLength() == 2,
    "Explicit selection did not prune unselected retained Foundry spell records.");
Require(JsonEqual(foundryJson, selectedRoundTripDocument.RootElement.GetProperty("Foundry").GetRawText()),
    "Selecting a Codex spell subset changed the retained Foundry payload.");

CharacterSheet codex = JsonSerializer.Deserialize<CharacterSheet>(codexJson, options)
    ?? throw new InvalidOperationException("Codex fixture did not deserialize.");
string codexExchange = FoundryCharacterExchangeService.Export(codex, options, rules, catalog);
using JsonDocument codexDocument = JsonDocument.Parse(codexExchange);
Require(codexDocument.RootElement.GetProperty("Codex").GetProperty("Name").GetString() == "Lys",
    "Codex payload was not included in exchange export.");
JsonElement nativeAnimalLore = codexDocument.RootElement.GetProperty("Codex")
    .GetProperty("NonweaponProficiencies").EnumerateArray().Single(record =>
        record.GetProperty("Id").GetString() == "nwp_animal_lore");
Require(nativeAnimalLore.GetProperty("TargetNumber").GetInt32() == 12,
    "Native Codex Animal Lore final target is not 12.");
Require(nativeAnimalLore.GetProperty("InitialCost").GetInt32() == 3,
    "Native Codex Animal Lore purchase CP is not 3.");
Require(nativeAnimalLore.GetProperty("ImprovementCharacterPoints").GetInt32() == 5,
    "Native Codex Animal Lore improvement CP is not 5.");
Require(codexDocument.RootElement.GetProperty("Foundry").ValueKind == JsonValueKind.Null,
    "A Codex-only export unexpectedly contained Foundry source data.");

CharacterSheet visibleNative = JsonSerializer.Deserialize<CharacterSheet>(codexJson, options)
    ?? throw new InvalidOperationException("Visible native-data fixture did not deserialize.");
visibleNative.ArcaneSpells.Clear();
visibleNative.DivineSpells.Clear();
visibleNative.PsionicPowers.Clear();
visibleNative.TrackedSpellIds.Clear();
SpellDefinition[] selectedForExport = FoundryCharacterExchangeService.GetExportSpellCandidates(visibleNative, rules)
    .Where(spell => spell.Category.Equals("Divine", StringComparison.OrdinalIgnoreCase))
    .Take(2)
    .ToArray();
Require(selectedForExport.Length == 2, "Visible native fixture did not expose selectable divine spells.");
visibleNative.ExportSpellIds = selectedForExport.Select(spell => spell.Id).ToList();
visibleNative.HasExplicitSpellExportSelection = true;
visibleNative.Equipment = new List<string> { "Leather, full", "Scimitar x2" };
visibleNative.EquipmentSelections.Clear();
foreach (AbilityDefinition ability in visibleNative.StructuredAbilities)
{
    ability.Name = string.Empty;
    ability.Category = string.Empty;
}

string visibleNativeJson = FoundryCharacterExchangeService.ExportCodex(visibleNative, options, rules, catalog);
using JsonDocument visibleNativeDocument = JsonDocument.Parse(visibleNativeJson);
JsonElement visibleNativeRoot = visibleNativeDocument.RootElement;
Require(divineSlotsArePopulated(visibleNativeRoot),
    "Visible native fixture unexpectedly lacks divine spell slots.");
Require(visibleNativeRoot.GetProperty("DivineSpells").GetArrayLength() == 2,
    "Explicit divine spell export selections were not respected.");
Require(visibleNativeRoot.GetProperty("TrackedSpellIds").GetArrayLength()
        == visibleNativeRoot.GetProperty("DivineSpells").GetArrayLength(),
    "Cortex-visible divine spells were not mirrored in TrackedSpellIds.");
Require(visibleNativeRoot.GetProperty("Equipment").GetArrayLength() == 2,
    "Visible legacy equipment was removed during Codex export.");
Require(visibleNativeRoot.GetProperty("EquipmentSelections").GetArrayLength() == 2,
    "A character with Cortex-visible equipment exported an empty EquipmentSelections collection.");
Require(visibleNativeRoot.GetProperty("StructuredAbilities").EnumerateArray().All(ability =>
        !string.IsNullOrWhiteSpace(ability.GetProperty("Name").GetString())
        && !string.IsNullOrWhiteSpace(ability.GetProperty("Category").GetString())),
    "One or more structured abilities exported without Name or Category.");

visibleNative.ExportSpellIds.Clear();
string noSelectedSpellsJson = FoundryCharacterExchangeService.ExportCodex(visibleNative, options, rules, catalog);
using JsonDocument noSelectedSpellsDocument = JsonDocument.Parse(noSelectedSpellsJson);
Require(noSelectedSpellsDocument.RootElement.GetProperty("DivineSpells").GetArrayLength() == 0,
    "Sphere-accessible spells were exported despite an explicit empty selection.");

SpellDefinition nativeDefinition = rules.Spells.First(spell =>
    spell.Category.Equals("Divine", StringComparison.OrdinalIgnoreCase)
    && !string.IsNullOrWhiteSpace(spell.Description));
int nativeLevel = int.Parse(nativeDefinition.Level);
var nativePrepared = new CharacterSheet
{
    Name = "Native prepared-spell probe",
    HasExplicitSpellExportSelection = true,
    SpellTracking = new CharacterSpellTracking
    {
        DivineConfig = new DivineSpellConfiguration
        {
            MaxSpellSlots = new Dictionary<int, int> { [nativeLevel] = 2 },
        },
        CurrentDayTracking = new DailySpellTracking
        {
            PreparedSpells = new List<PreparedSpellRecord>
            {
                new() { SpellId = nativeDefinition.Id, SpellName = nativeDefinition.Name, SpellLevel = nativeLevel, IsCast = false },
                new() { SpellId = nativeDefinition.Id, SpellName = nativeDefinition.Name, SpellLevel = nativeLevel, IsCast = true },
            },
        },
    },
};
string nativePreparedExchange = FoundryCharacterExchangeService.Export(nativePrepared, options, rules, catalog);
using JsonDocument nativePreparedDocument = JsonDocument.Parse(nativePreparedExchange);
JsonElement nativeSpell = nativePreparedDocument.RootElement.GetProperty("Codex")
    .GetProperty("DivineSpells").EnumerateArray().Single();
Require(nativeSpell.GetProperty("Id").GetString() == nativeDefinition.Id
        && nativeSpell.GetProperty("SpellId").GetString() == nativeDefinition.Id,
    "Native prepared spell did not retain its stable catalog ID.");
Require(nativeSpell.GetProperty("Prepared").GetBoolean(),
    "A fully or partially cast prepared spell was incorrectly marked unprepared.");
Require(nativeSpell.GetProperty("PreparedCount").GetInt32() == 2,
    "Multiple prepared copies were not retained.");
Require(nativeSpell.GetProperty("RemainingUses").GetInt32() == 1,
    "Remaining prepared uses did not account for the cast copy.");
Require(nativeSpell.GetProperty("Description").GetString() == nativeDefinition.Description,
    "Native prepared spell was not hydrated with its complete definition.");
Require(nativePreparedDocument.RootElement.GetProperty("Foundry").ValueKind == JsonValueKind.Null,
    "Native prepared-spell hydration incorrectly requires Foundry source data.");

Console.WriteLine("PASS: Foundry legacy import maps Lys progression and preserves the complete payload.");
Console.WriteLine("PASS: Exchange import/export round-trip retains Foundry JSON semantically.");
Console.WriteLine("PASS: Lys exports 11 structured NWPs, 2 equipment records, retained and visible divine spells, and casting-pool state.");
Console.WriteLine("PASS: Lys exports final saves 6/10/9/12/11 and Animal Lore target 12 with separate CP fields.");
Console.WriteLine("PASS: Normal Cortex save/reload retains complete Foundry source data.");
Console.WriteLine("PASS: Unknown nested spell and equipment properties survive Codex re-import.");
Console.WriteLine("PASS: Explicit spell subsets prune Codex records without changing retained Foundry JSON.");
Console.WriteLine("PASS: Codex-only characters export with a complete Codex payload.");
Console.WriteLine("PASS: Native spells export stable IDs, complete definitions, and per-copy preparation state.");
Console.WriteLine("PASS: Plain Codex export respects spell selections and populates equipment and ability metadata.");

static bool divineSlotsArePopulated(JsonElement codex)
    => codex.GetProperty("DivineSpellSlots").EnumerateObject().Any(slot => slot.Value.GetInt32() > 0);

static bool JsonEqual(string left, string right)
    => JsonNode.DeepEquals(JsonNode.Parse(left), JsonNode.Parse(right));

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}