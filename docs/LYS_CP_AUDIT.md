# Lys CP reconciliation — 2026-10-14

Read-only audit of the earlier PDF found in the user's Downloads, prior JSON exports, the recovered roster, and current code. No character data or production formulas were changed.

## Fix applied after the audit

The original audit below records the pre-fix findings. The confirmed review/new-and-update saved-summary defect is now corrected: each improvement CP grants +1, independent of purchase cost. Review also uses the same nonnegative base-rating floor as the selection page rather than imposing a minimum base of five.

The read-only Lys audit was rerun against the corrected formatter:

| NWP | Same improvement CP | Corrected review target |
| --- | ---: | ---: |
| Animal Lore | 5 | 12 |
| Healing | 8 | 16 |
| Herbalism | 13 | 19 |
| Riding, Land | 13 | 21 |
| Set Snares | 5 | 14 |

No saved roster, spending history, CP budget, or user-local proficiency definitions were altered. Existing summary strings are regenerated when the character is next finalized through the normal update flow; do not spend additional CP simply to compensate for the former review error. There is no automatic refund because the incorrect targets alone do not establish an overpayment.

Validation: Release build with no warnings/errors; 62 self-contained NWP improvement checks, 100 Tracking checks, and 40 existing-character XP checks passed. The new regression project is `tests/NwpImprovements/NwpImprovements.csproj`.

Remaining scope: shared Intelligence CP allocation, differing PDF/export targets, ability-source/catalog overrides, and other proficiency-bonus differences are not resolved by this arithmetic fix. In particular, structured exchange has a separate Animal Lore base/ability special case; the regression checks for generic structured records do not certify that special case against the selection page.

## Reference identity

- PDF: Lyssa Ravencry, Ranger 8 / Druid 10, XP 150,000 / 125,000, INT 10 (Knowledge/Reason 10).
- Advancement under the user's chosen full 4 CP per class advance: `(8 - 1 + 10 - 1) * 4 = 64 CP`.
- Base allocations: race 25, Druid 100, Ranger 60, NWP 8, weapon 8, shared Intelligence allowance 2 = **203 CP**, not the earlier stated 205. With advancement: **267 CP**, before restrictions, disadvantages, or spending. These are restricted/category allocations, not an unrestricted NWP balance.
- Bundled S&P Intelligence rule: `Assets/Core Rules/WEBHELP/SP/DD03005.HTM`, paragraphs describing Intelligence CP. The bonus can buy languages/Intelligence-relevant NWPs; warriors may instead use the same allowance on weapons. It is not awarded twice. Current UI puts it in the warrior weapon-stage budget and does not allow spending it at the earlier NWP stage.

## Confirmed calculation defect

`CharGenCharacterOptionsScreen.GetNwpImprovement` returns allocated improvement CP directly: **1 CP = +1**.
`CharGenReviewScreen.FormatNwpSummary` divides those same CP by the proficiency's initial purchase cost before computing the target. Initial purchase cost must not be reused as the per-point improvement rate.

Actual current review formatter invoked against PDF ability/subability scores and the recovered improvement CP:

| NWP | Improvement CP | Options arithmetic target | Actual review target | PDF target |
| --- | ---: | ---: | ---: | ---: |
| Animal Lore | 5 | 12 | 8 | 12 |
| Healing | 8 | 16 | 10 | 16 |
| Herbalism | 13 | 19 | 10 | 18 |
| Riding, Land | 13 | 21 | 14 | 21 |
| Set Snares | 5 | 14 | 10 | 15 |

The options arithmetic here includes catalog base, Table 44 modifier, and improvement CP. It is not an assertion that every possible class/item bonus has been reconciled. Herbalism differs between the PDF (18) and older Foundry JSON (19). Set Snares requires further ability-source/bonus reconciliation. Increasing CP to match the incorrect review targets would overcharge the character.

## Other evidence and limits

- The recovered roster records **Druid 11 / Ranger 8**, not the PDF's 10/8; it is a historical record, not proof of the current UI state.
- That record has NWP spent 107, weapon spent 14, unspent 4; those counters alone do not prove legitimate spending because historical reconstruction/import differs.
- Current live catalog resolves Tracking to `nwp_warrior_tracking`, base 7, PO check ability `Strength`. Bundled master/PO rules describe Wisdom/Intuition. Local/custom catalog overrides must be checked before changing bundled definitions or claiming an exact Tracking CP reconstruction.
- Ranger Tracking bonus for the reference Ranger 8 is +7 total (+5 base, +2 from levels); its initial NWP purchase is free when class-granted. Paid improvements remain chargeable.
- This audit does not certify that all old sheet targets are attainable from the same budget/rules. Full comparison still needs matching the PDF's selected class abilities, sphere costs, free grants, trait, restrictions, and all proficiency definitions.

## Reproduction

The read-only console audit is in `artifacts/lys-cp-audit/`. It requires the earlier PDF/JSON files in the user's Downloads and the recovered roster in this workspace. It reads but does not overwrite them.