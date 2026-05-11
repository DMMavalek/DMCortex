from __future__ import annotations

import argparse
from pathlib import Path

from dmcortex.assets_ingest import write_assets_index
from dmcortex.character_builder import CharacterBuilder
from dmcortex.campaign import CampaignManager
from dmcortex.combat import CombatTracker
from dmcortex.models import CampaignEntry, Combatant
from dmcortex.rules_extract import write_structured_entities
from dmcortex.rules_engine import RulesEngine
from dmcortex.rules_numeric_extract import write_numeric_rules
from dmcortex.rules_profiles import resolve_rules_from_profile


def _project_root() -> Path:
    return Path(__file__).resolve().parents[2]


def _run_preview() -> None:
    rules_path = _project_root() / "data" / "rulesets" / "core_2e.json"
    rules = RulesEngine.from_file(rules_path)

    builder = CharacterBuilder(rules)
    abilities = builder.generate_abilities("standard_array")
    character = builder.build_character(
        name="New Hero",
        race_id="human",
        class_id="fighter",
        abilities=abilities,
    )

    print("== Character Preview ==")
    print(character)

    tracker = CombatTracker("Goblin Ambush")
    tracker.add_combatant(Combatant(name="New Hero", initiative=5, hp_current=10, hp_max=10))
    tracker.add_combatant(Combatant(name="Goblin", initiative=3, hp_current=6, hp_max=6))
    tracker.apply_damage("Goblin", 2)

    print("\\n== Combat Preview ==")
    print(tracker.encounter)

    campaign = CampaignManager()
    campaign.add_entry(
        CampaignEntry(
            session_id="S001",
            title="Campaign Created",
            body="Party assembled in the border town.",
            tags=["setup", "party"],
        )
    )

    print("\\n== Campaign Preview ==")
    print(campaign.export())


def _run_assets_index(assets: str, output: str) -> None:
    assets_root = Path(assets).resolve()
    output_file = Path(output).resolve()

    generated = write_assets_index(assets_root=assets_root, output_file=output_file)
    print("== Assets Index Generated ==")
    print(generated)


def _run_extract_rules(assets: str, index_file: str, output_file: str) -> None:
    assets_root = Path(assets).resolve()
    index_path = Path(index_file).resolve()
    structured_path = Path(output_file).resolve()

    if not index_path.exists():
        write_assets_index(assets_root=assets_root, output_file=index_path)

    generated = write_structured_entities(index_file=index_path, output_file=structured_path)
    print("== Structured Rules Extracted ==")
    print(generated)


def _run_extract_numeric_rules(assets: str, output_file: str) -> None:
    books_root = Path(assets).resolve() / "Core Rules" / "BOOKS"
    generated = write_numeric_rules(books_root=books_root, output_file=Path(output_file).resolve())

    print("== Numeric Rules Extracted ==")
    print(generated)


def _run_profile_preview(profile: str, character_overlays: list[str], constraints_file: str) -> None:
    root = _project_root()
    rulesets_root = root / "data" / "rulesets"
    profile_path = rulesets_root / "profiles" / profile
    character_overlay_paths = [rulesets_root / overlay for overlay in character_overlays]
    constraints_path = Path(constraints_file).resolve()

    rules = resolve_rules_from_profile(
        rulesets_root=rulesets_root,
        profile_path=profile_path,
        character_overlay_paths=character_overlay_paths,
        extracted_constraints_path=constraints_path,
    )

    campaign = CampaignManager()
    campaign.set_campaign_profile(profile_path.stem)
    campaign.set_character_overlays("New Hero", [str(path) for path in character_overlay_paths])

    builder = CharacterBuilder(rules)
    method = "point_buy_75" if "point_buy_75" in rules.ruleset.get("ability_generation_methods", []) else "standard_array"
    abilities = builder.generate_abilities(method)
    character = builder.build_character("New Hero", "human", "fighter", abilities)

    print("== Profile Preview ==")
    print(f"Campaign profile: {campaign.rules_config.profile_id}")
    print(f"Character overlays: {campaign.overlays_for_character('New Hero')}")
    print(character)


def main() -> None:
    root = _project_root()
    parser = argparse.ArgumentParser(description="Dungeon Master Cortex tools")
    subparsers = parser.add_subparsers(dest="command")

    subparsers.add_parser("gui", help="Launch the graphical interface")

    subparsers.add_parser("preview", help="Run starter preview")

    index_parser = subparsers.add_parser("index-assets", help="Build index from Assets folder")
    index_parser.add_argument(
        "--assets",
        default=str(root / "Assets"),
        help="Path to Assets directory",
    )
    index_parser.add_argument(
        "--out",
        default=str(root / "data" / "import" / "adnd2e_asset_index.json"),
        help="Path to output JSON index",
    )

    extract_parser = subparsers.add_parser("extract-rules", help="Build structured entities from local assets")
    extract_parser.add_argument(
        "--assets",
        default=str(root / "Assets"),
        help="Path to Assets directory",
    )
    extract_parser.add_argument(
        "--index",
        default=str(root / "data" / "import" / "adnd2e_asset_index.json"),
        help="Path to input/output asset index JSON",
    )
    extract_parser.add_argument(
        "--out",
        default=str(root / "data" / "import" / "adnd2e_structured_entities.json"),
        help="Path to structured entities JSON",
    )

    numeric_parser = subparsers.add_parser("extract-numeric-rules", help="Extract numeric constraints from BOOKS RTF files")
    numeric_parser.add_argument(
        "--assets",
        default=str(root / "Assets"),
        help="Path to Assets directory",
    )
    numeric_parser.add_argument(
        "--out",
        default=str(root / "data" / "import" / "adnd2e_numeric_rules.json"),
        help="Path to numeric rules JSON",
    )

    profile_parser = subparsers.add_parser("preview-profile", help="Preview rules using campaign profile + overlays")
    profile_parser.add_argument(
        "--profile",
        default="core_only.json",
        help="Profile file in data/rulesets/profiles",
    )
    profile_parser.add_argument(
        "--character-overlay",
        action="append",
        default=[],
        help="Relative overlay file in data/rulesets to apply at character build time",
    )
    profile_parser.add_argument(
        "--constraints",
        default=str(root / "data" / "import" / "adnd2e_numeric_rules.json"),
        help="Path to extracted numeric constraints JSON",
    )

    args = parser.parse_args()

    if args.command == "gui":
        from dmcortex.ui import launch
        launch()
        return

    if args.command in (None, "preview"):
        _run_preview()
        return

    if args.command == "index-assets":
        _run_assets_index(args.assets, args.out)
        return

    if args.command == "extract-rules":
        _run_extract_rules(args.assets, args.index, args.out)
        return

    if args.command == "extract-numeric-rules":
        _run_extract_numeric_rules(args.assets, args.out)
        return

    if args.command == "preview-profile":
        _run_profile_preview(args.profile, args.character_overlay, args.constraints)
        return

    raise ValueError(f"Unknown command: {args.command}")


if __name__ == "__main__":
    main()
