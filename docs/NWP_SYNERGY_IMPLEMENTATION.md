# Proficiency audit — confirmed corrections and remaining reconciliation

Date: 2026-10-14. The [pre-change findings](NWP_SYNERGY_AUDIT.md) were published before calculation edits. No character roster, user-local catalog override, historical improvement allocation, or remaining balance was rewritten.

## Implemented rules

- Central, embedded, data-driven inventory of **37 source-attributed Skills & Powers rating rules**: 24 permanent/check-wide and 13 conditional. Rule records identify prerequisites, targets/aliases, modifier, condition, stacking group, source passage, and **zero additional CP cost**.
- Animal Lore gives Tracking +2 only for animals; Animal Empathy adds +2 for wild animals. Neither enters the ordinary Tracking rating. The existing Ranger +5 and free +1 per three Ranger levels remain separate.
- Animal Lore gives Set Snares +2. Animal Empathy's additional +2 is available only for the specified small-animal snare check, not assumed for pit construction.
- Other verified bonuses include Animal Handling/Training/Empathy to Riding, empathy and knowledge traits to specified proficiencies, artistic/music traits, Internal Compass, and explicitly documented dwarf/halfling proficiency bonuses. Conditional agriculture, rope, navigation, Spellcraft and tightrope effects remain explicit alternatives.
- Where Astrology/Astronomy stacking is not established, the UI reports the uncertainty and the context evaluator uses the higher applicable grant rather than inventing +5.
- No Herbalism +1 check bonus: the verified effect is **+1 HP restored**, so it is recorded in the audit, not implemented as a proficiency rating modifier. Qualitative prerequisites and instrument/language-count benefits are likewise not fabricated as rating bonuses.
- Core mode does not receive these S&P-specific rules. Ownership of the same prerequisite through multiple IDs, names or representations does not multiply a rule.

## Calculation and accounting corrections

| Correction | Demonstrated effect | Additional CP / refund |
| --- | --- | --- |
| Free class/kit acquisition consistently honored in weapon-stage carryover and review | Ranger Tracking no longer reintroduces its catalog acquisition price in those calculations; free kit acquisitions are also excluded. Current local Tracking catalog price is 4; fixture/source prices can differ. Baseline and current calculations use the same grant policy. | Avoided erroneous acquisition charge = actual price for each affected granted acquisition; **not a fixed 6**. No automatic refund. |
| Selected improvements counted once per distinct ID | Repeated selections cannot multiply the same improvement allocation; stale allocations for unselected IDs do not reduce the current selected-proficiency pool. Raw stored maps remain untouched. | Exact avoided charge depends on actual duplicate/stale allocations. Paid selected improvements unchanged. |
| Racial ability clone preserves targeted NWP effects | `NwpCheckBonusesById` is deep-copied rather than lost when racial definitions are cloned. | Zero CP; preserves existing authored bonuses. |
| Source-attributed free synergy contributions in structured records | Export separates free benefits from initial acquisition and paid improvements; native JSON preserves those records. Conditional contributions are not inserted into permanent targets. | Zero CP. |
| Safe structured-target reconstruction | Repeated sync cannot reuse an already modified target as an initial base. Zero initial rating remains valid. Positive legacy/imported targets without improvement history are retained, including Tracking. | No spending rewrite or inferred refund. |
| Explicit composition status | `SynergiesIncludedInTarget=true` only for known recomputed targets; null means retained target composition is unknown. Kit/class grant provenance is separate. | Zero CP. |
| Animal Lore known-history exchange | Uses supplied catalog base/ability rather than forced base 8 when paid history is present. Retained legacy targets are not guessed from catalog changes. | Zero CP; target consistency correction, not a ledger transaction. |

Newly calculated options/review targets receive verified permanent synergies once. Sheets retain old saved target strings and append missing conditional information; they do not silently replace historical targets. Normal explicit character update/finalization regenerates summaries using current definitions. Generic class/racial aggregate-target parity, ability-choice normalization and custom-definition scope remain separate unresolved issues.

## Lyssa: supplied ledger arithmetic and transparent target comparison

The user's full 4 CP for **each class advance** is preserved: `(8-1)*4 + (10-1)*4 = 64`.

The stated NWP acquisitions total **28**. The eleven improvement entries total **57**, not 55. Their sum is **85**, not 83. No code correction turns a real 85 into 83 automatically.

The base-allocation comparison is 25 racial + 100 Druid + 60 Ranger + 8 NWP + 8 weapon + **one shared INT allowance of 2** + 64 advancement = **267**. The supplied 269 awards INT twice. This verified rule discrepancy was reported, but no campaign allocation or saved budget was automatically changed. The earlier 269 answer was incorrect.

Using the *reported* base/ability components and exact paid improvements, without inventing missing history:

| NWP | Base + reported ability + paid improvements + confirmed permanent synergy/class | Ordinary target candidate | Conditional / unresolved |
| --- | --- | ---: | --- |
| Animal Lore | 7 + 0 + 5 | 12 | Knowledge versus Intuition is an unresolved recorded choice; current master loses alternatives. |
| Blind Fighting | 0 + 3 + 8 | 11 | Reported components only. SP's NA/combat effects versus conditional movement-check base need separate resolution; Balance 18 would give +5, not +3. |
| Healing | 5 + 3 + 8 | 16 | Herb dose restores HP, not extra target. |
| Herbalism | 6 + 0 + 12 | 18 | Supplied allocation, not recovered 13 CP. |
| Modern Languages | 6 + 0 + 0 | 6 | Does **not** explain displayed 9. Catalog instead has base9/purchase2; reported purchase3/base6 requires source reconciliation. |
| Reading/Writing | 8 + 0 + 1 | 9 | Language ownership/literacy restrictions separate. |
| Riding, Land | 8 + 0 + 13 + 2 Animal Empathy | 23 | Reported sheet 21 omits this confirmed synergy or includes a different allocation; no refund inferred. |
| Set Snares | 8 + reported1 + 6 + 2 Animal Lore | 17 | 19 for eligible small-animal checks with Empathy. Reported +1 ability component conflicts with current legal subability choices; verify selection. |
| Tracking (reported components) | 7 + reported4 + 4 + 8 | 23 | Does not justify permanent25. Reported class8 conflicts with Ranger8's verified7. Lore and Empathy are contextual, not permanent. |
| Tracking (source-based Intuition16/Ranger8) | 7 + 3 + 4 + 7 | 21 | Animal23; wild-animal25 with Lore+Empathy. This is a **conditional route to 25**, not proof the old sheet used it. Local Strength override intentionally remains untouched. |
| Trail Marking | 7 + 1 + 0 | 8 | Does not explain14. No verified SP bonus invented for a handbook/custom definition. |
| Trail Signs | 6 + 0 + 0 | 6 | Does not explain14. Cultural/handbook rules and custom ratings remain unresolved. |

These calculations demonstrate that several supplied components do not reproduce the old totals even before software behavior is considered. They do **not** prove 6 CP were improperly deducted. Free synergies change applicable targets; they do not convert themselves into refunded purchased improvements.

**Remaining CP cannot be certified.** Subtracting NWP85 from267 is not a remaining balance: separate racial/class/sphere/trait/weapon expenditures and restricted/carryover pools must be reconciled against the same character version. The historical recovery differs in levels, weapons, improvement allocations and catalog prices. No balance was asserted from that mixed evidence.

## Files changed for this audit implementation

- [data/rulesets/nwp_synergies.json](../data/rulesets/nwp_synergies.json)
- [DungeonMasterCortex/Services/NwpSynergyRules.cs](../DungeonMasterCortex/Services/NwpSynergyRules.cs)
- [DungeonMasterCortex/Services/NwpGrantRules.cs](../DungeonMasterCortex/Services/NwpGrantRules.cs)
- [DungeonMasterCortex/DungeonMasterCortex.csproj](../DungeonMasterCortex/DungeonMasterCortex.csproj) — embedded rule resource
- [DungeonMasterCortex/Models/Models.cs](../DungeonMasterCortex/Models/Models.cs) — backward-compatible contribution/provenance fields
- [DungeonMasterCortex/Services/RulesEngine.cs](../DungeonMasterCortex/Services/RulesEngine.cs) — targeted-effect clone
- [DungeonMasterCortex/Services/RangerTrackingRules.cs](../DungeonMasterCortex/Services/RangerTrackingRules.cs) — conditional adapter
- [DungeonMasterCortex/Services/FoundryCharacterExchangeService.cs](../DungeonMasterCortex/Services/FoundryCharacterExchangeService.cs)
- [DungeonMasterCortex/Views/CharGenCharacterOptionsScreen.xaml.cs](../DungeonMasterCortex/Views/CharGenCharacterOptionsScreen.xaml.cs)
- [DungeonMasterCortex/Views/CharGenReviewScreen.xaml.cs](../DungeonMasterCortex/Views/CharGenReviewScreen.xaml.cs)
- [DungeonMasterCortex/Views/CharGenWeaponProficienciesScreen.xaml.cs](../DungeonMasterCortex/Views/CharGenWeaponProficienciesScreen.xaml.cs)
- [DungeonMasterCortex/Views/CharacterSheetsScreen.xaml.cs](../DungeonMasterCortex/Views/CharacterSheetsScreen.xaml.cs)
- [tests/NwpSynergies/Program.cs](../tests/NwpSynergies/Program.cs), [project](../tests/NwpSynergies/NwpSynergies.csproj)
- [tests/NwpAccounting/Program.cs](../tests/NwpAccounting/Program.cs), [project](../tests/NwpAccounting/NwpAccounting.csproj)
- [Pre-change audit](NWP_SYNERGY_AUDIT.md), this implementation report, and [changelog](../DungeonMasterCortex/CHANGELOG.txt)

## Tests performed

Release build: **0 warnings, 0 errors**.

| Suite | Checks |
| --- | ---: |
| NwpSynergies: 20 scenarios, four Ranger ownership combinations, all 37 rules/aliases, conditions, repeat sync, retained unknown targets, structured contribution roundtrips and free-grant/history separation | 1,501 |
| NwpAccounting: grant acquisition, kit/class duplicates, retained paid improvements, repeated purchases, stale allocations, targeted clone | 85 |
| NwpImprovements | 62 |
| RangerTracking | 100 |
| ExistingClassExperience | 40 |
| DruidRangerAbilities | 37 |
| CreatureLanguages | 17 |
| **Total** | **1,842** |

No interactive UI test or exhaustive certification of every handbook/custom proficiency was performed. All 70 SP description pages, 27 trait pages and the master rows were audited; supplemental PDF/handbook descriptions are inventoried but not all mechanically verified. The implementation deliberately omits unverified numeric relationships and never adds an unexplained +2 to Tracking.