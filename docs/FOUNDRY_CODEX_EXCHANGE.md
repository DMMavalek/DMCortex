# Foundry / Codex Character Exchange

## Contract

Codex writes a versioned JSON envelope when **Foundry exchange (*.foundry.json)** is selected in the character export dialog.

```json
{
  "Schema": "dungeon-master-cortex.foundry-character-exchange",
  "SchemaVersion": 1,
  "ExportedAtUtc": "2026-09-21T00:00:00Z",
  "Codex": {},
  "Foundry": {}
}
```

- `Codex` is the complete native `CharacterSheet`. Foundry must retain it unchanged except for deliberate shared-field updates.
- `Foundry` is the complete Foundry export payload. Codex retains it as `FoundrySourceData` and returns it without removing unknown properties.
- `Foundry` is `null` when a character has never been imported from Foundry.
- Importers must reject schema versions newer than they support rather than silently dropping fields.

The Codex payload now includes structured `ArcaneSpells`, `DivineSpells`, `PsionicPowers`, `CastingPools`, `RemainingArcaneSpellSlots`, and `RemainingDivineSpellSlots`. `NonweaponProficiencies` is a structured record array in exchange files; the former rendered strings remain available as `LegacyNonweaponProficiencies`. `EquipmentSelections` contains complete structured inventory records.

Each structured spell has matching stable `Id` and `SpellId` values, its complete catalog definition, casting-pool identity, and preparation state. `PreparedCount` is the number of prepared copies, while `RemainingUses` and `RemainingPreparedCount` are the uncast copies. `Prepared` remains true when at least one copy was prepared, even after every copy has been cast.

Codex synchronizes these records before normal native saves and exchange exports. Discovery includes tracked IDs, legacy and structured wizard spellbooks, named spell lists, current and historical prepared/cast tracking, and existing structured records. Returned Foundry spell edits merge into the Codex collections by stable spell ID; unrecognized spell properties remain attached to the merged record.

For divine casters, current slot levels and selected sphere access define the candidates shown in the export-selection dialog; sphere access alone does not make every available spell a character spell. `ExportSpellIds` stores the user's optional selections, and `HasExplicitSpellExportSelection` distinguishes an intentional empty selection from an older character that has not configured export choices. Prepared, cast, and spellbook spells are always included. Plain Codex JSON export uses the same synchronization path as Foundry exchange export. Legacy visible equipment names are resolved into `EquipmentSelections` before load migration or export, while `Equipment` remains populated for compatibility. Structured abilities are exported with nonblank `Name` and `Category` metadata resolved from the active race/class catalogs.

`Foundry` remains `null` for a character that has never supplied a Foundry payload. Codex does not invent a Foundry-only object; once one is imported, it is retained and returned losslessly.

Spell, equipment, NWP, casting-pool, slot-state, spell-tracking, and character records retain unrecognized JSON properties. Stable IDs remain independent of display names.

`SavingThrows` contains Cortex's five final current AD&D targets: `paralyzationPoisonDeath`, `rodStaffWand`, `petrificationPolymorph`, `breathWeapon`, and `spell`. Cortex calculates multiclass saves from each class's own level, selects the most advantageous class target, and applies permanent displayed bonuses. Unknown saving-throw properties are retained.

Each `NonweaponProficiencies` record contains its own `Id` and `ProficiencyId`, final `TargetNumber`, normalized ability key, purchase CP, improvement CP, total CP, class-grant state, notes, and extension data. `NonweaponProficiencyIds` remains present in the same order for compatibility, but identity never depends on array position.

## Foundry Module Requirements

On import:

1. Detect the exact `Schema` value and require `SchemaVersion <= 1`.
2. Store the complete `Codex` object in an actor flag, for example `flags.<module-id>.codexPayload`.
3. If `Foundry` is present, restore its Foundry-only actor data before applying current values from `Codex`.
4. Map shared fields from `Codex` into the actor. The incoming Codex payload is authoritative for shared fields.
5. Never discard unknown properties from either payload.

On export:

1. Produce the same envelope, not only the legacy flattened Foundry object.
2. Start `Codex` from the retained actor flag and update only mapped shared fields from the current actor.
3. Put the module's complete current flattened export in `Foundry`.
4. Preserve stable Codex IDs. Display names are not substitutes for `RaceId`, `ClassIds`, proficiency IDs, ability IDs, spell IDs, or equipment IDs.

## Shared Field Rules

- Multiclass XP is stored per class in Codex. Use `ClassLevels` and per-class progression; do not put the sum of all class XP into `ExperiencePoints`.
- Use lowercase Codex class IDs such as `druid` and `ranger`; set `ClassMode` to `multiclass` when more than one class exists.
- Use `half_elf_po` for the Player's Option Half-Elf race.
- Preserve both maximum and current HP.
- Preserve proficiency IDs and improvement CP separately from rendered names and target numbers.
- Preserve spell IDs, spell definitions, slot maxima, remaining slots, casting pools, and tracking state.
- Preserve structured equipment and equipped-slot IDs in addition to Foundry combat summaries.
- Preserve notes, selected class abilities, sphere access, CP ledgers, and character-sheet layout data even when Foundry does not display them.

## Legacy Foundry Files

Codex also detects the existing flattened Foundry format by its `FoundryExtensions` object. It maps reliable shared fields, derives multiclass levels from `FoundryExtensions.ClassProgression`, treats progression XP as per-class XP, and stores the complete source object for later exchange export.

The regression fixture is `artifacts/foundry-exchange-test`. It uses `Downloads/Foundry Lys.json` and `Downloads/Codex Lys.json` and verifies semantic preservation of the full Foundry payload.