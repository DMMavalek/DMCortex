# Product Vision

## Goals

Build a complete AD&D 2e suite with two major systems:
1. Character Generator for Core rules and Player's Option variants
2. Dungeon Master Workspace for combat and campaign management

## Guiding Principles

- Rules as data: game rules should be editable without code changes where practical.
- Auditability: show why a character option is valid or invalid.
- Modularity: keep character generation, combat, and campaign features independent but connected.
- Offline-first: run locally without internet dependency.

## Primary User Flows

1. Create Character
- Select rules profile (Core, Core + PO, custom)
- Roll or assign abilities
- Select race, class, kit, proficiencies, and spells
- Validate prerequisites and restrictions
- Export character sheet

2. Run Combat
- Add PCs and NPCs/monsters
- Track initiative rounds, actions, HP, and status effects
- Record notes and events by turn

3. Manage Campaign
- Track NPCs, quests, locations, loot, and timeline
- Link encounters and sessions to campaign entities

## MVP Scope

- JSON-driven race/class/ability prerequisites
- Character validation pipeline
- Basic combat round tracker
- Campaign log entries tied to sessions

## Future Scope

- Full Player's Option point-buy and customization menus
- Printable and digital character sheets
- Save/import for multiple campaign worlds
- Optional web or desktop GUI
