# Architecture

## High-Level Modules

- `rules_engine.py`: loads and resolves ruleset definitions
- `assets_ingest.py`: indexes Core Rules HELP and WEBHELP sources
- `rules_extract.py`: second-pass extraction into structured entities
- `character_builder.py`: creates and validates character choices
- `combat.py`: encounter and initiative tracking
- `campaign.py`: campaign entities and session logs
- `rules_profiles.py`: campaign/character overlay resolution
- `app.py`: temporary CLI entry point

## Core Design

1. Rules Data Layer
- JSON files in `data/rulesets/`
- Structures for abilities, races, classes, kits, proficiencies, and configurable options
- Generated source index in `data/import/adnd2e_asset_index.json`

2. Domain Model Layer
- Typed dataclasses represent character sheets, combatants, encounters, and campaign records

3. Service Layer
- CharacterBuilder and validators
- CombatTracker and CampaignManager
- Asset index generation for discovery and extraction planning
- Structured entity extraction for race/class/kit/proficiency sets
- Rules profile resolution for campaign and per-character overlays

4. Interface Layer
- CLI now, GUI later

## Extensibility Plan

- Support multiple rulesets by ID, including custom house-rule packs
- Merge base Core rules with optional overlays (for Player's Option)
- Keep validation logic deterministic and testable

## Persistence Strategy

- Start with JSON save files for portability
- Move to SQLite once querying and historical filtering become complex

## Testing Strategy

- Unit tests for rule validators and initiative ordering
- Golden tests for known legal/illegal character builds
