# Core Rules Rescan Playbook

## Goal
Keep Core Rules character generation data complete and authoritative (races, class eligibility, multiclass combos, kits, proficiencies, spells, monsters) without relying on Player's Option-only defaults.

## When to Run
- After importing new rulebook assets.
- After changing parsing logic in tools.
- Before any public release candidate.

## Inputs
- Assets under `Assets/Core Rules/` (HELP, WEBHELP, BOOKS).
- Ruleset files under `data/rulesets/`.

## Pipeline
1. Build/update the asset index.
2. Re-extract structured entities.
3. Re-extract numeric constraints.
4. Reconcile extracted output into base ruleset JSON files.
5. Validate Core Rules char-gen behavior in app.
6. Build installers.

## Commands
Run from repo root.

```powershell
dmcortex index-assets
dmcortex extract-rules
dmcortex extract-numeric-rules
```

## Reconciliation Checklist
Apply extracted data into these source-of-truth files:
- `data/rulesets/core_2e.json`
- `data/rulesets/multiclass_combos.json`
- `data/rulesets/kits.json`
- `data/rulesets/weapons.json`
- `data/rulesets/spells.json`
- `data/rulesets/monsters.json`

Prioritize Core Rules coverage first:
- Race ability mins/maxes and restrictions.
- Class ability minimums and race eligibility.
- Canonical multiclass combinations by race.
- Core kits and proficiency progression.

## Validation Checklist (Core Rules Mode)
1. New character defaults to `core_rules`.
2. Race selection reflects Core Rules-only options.
3. Class eligibility honors race limits.
4. Multiclass list includes expected combos by race.
5. No Player's Option CP flow appears unless mode is `players_option`.
6. Review screen labels ruleset as Core Rules.

## Release Gate
Before publishing installers:
1. Complete validation checklist above.
2. Ensure update folders contain only setup EXEs + backup folder.
3. Confirm setup filenames use current version in both DM and Player outputs.

## Notes
- Keep Core Rules and Player's Option logic separate; do not patch Core gaps by changing PO-only behavior.
- If a rule appears in both sources with conflict, record decision in release notes before shipping.
