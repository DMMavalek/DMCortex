# Asset Ingestion

This project now includes a local asset indexer that reads your existing AD&D 2e Core Rules files and produces a machine-readable index.

## Supported Sources

- `Assets/Core Rules/HELP/*.CNT`
- `Assets/Core Rules/WEBHELP/**/*.HTM`

The index captures:
- Help book metadata (`:Base`, `:Title`, `:Index`, `:include`)
- WebHelp page title
- WebHelp bold headings (best-effort chapter/topic labels)
- WebHelp link targets and link text (topic graph)

## Generate Index

From the project root:

```powershell
dmcortex index-assets
```

Optional custom paths:

```powershell
dmcortex index-assets --assets "Assets" --out "data/import/adnd2e_asset_index.json"
```

## Output

- `data/import/adnd2e_asset_index.json`

## Structured Extraction (Second Pass)

Generate normalized entities for character generation and rules tooling:

```powershell
dmcortex extract-rules
```

This command reads the asset index and emits:

- `data/import/adnd2e_structured_entities.json`

Entity buckets currently include:
- races
- classes
- kits
- proficiencies

## Numeric Extraction (Third Pass)

Generate numeric rule constraints from BOOKS RTF files:

```powershell
dmcortex extract-numeric-rules
```

This emits:

- `data/import/adnd2e_numeric_rules.json`

Current coverage:
- race ability score min/max constraints (where parseable)
- kit ability minimum requirements
- proficiency slot progression table entries (where parseable)

## Notes

- Parsing is deliberately lightweight and dependency-free.
- This first pass focuses on discoverability and topic mapping.
- Next pass should add structured extraction for character-generation rules (ability bounds, class/race requirements, kits, proficiencies).
