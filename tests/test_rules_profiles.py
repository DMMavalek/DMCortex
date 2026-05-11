import json
from pathlib import Path

from dmcortex.rules_profiles import resolve_rules_from_profile


def test_profile_resolution_applies_overlays(tmp_path: Path) -> None:
    rulesets_root = tmp_path / "rulesets"
    overlays = rulesets_root / "overlays"
    profiles = rulesets_root / "profiles"
    overlays.mkdir(parents=True)
    profiles.mkdir(parents=True)

    (rulesets_root / "base.json").write_text(
        json.dumps(
            {
                "ruleset_id": "base",
                "ability_generation_methods": ["standard_array"],
                "races": {"human": {"name": "Human"}},
                "classes": {"fighter": {"name": "Fighter"}},
            }
        ),
        encoding="utf-8",
    )

    (overlays / "overlay.json").write_text(
        json.dumps({"ability_generation_methods": ["point_buy_75"]}),
        encoding="utf-8",
    )

    (profiles / "core_plus.json").write_text(
        json.dumps(
            {
                "profile_id": "core_plus",
                "name": "Core Plus",
                "base_ruleset": "base.json",
                "overlays": ["overlays/overlay.json"],
            }
        ),
        encoding="utf-8",
    )

    engine = resolve_rules_from_profile(rulesets_root, profiles / "core_plus.json")

    assert engine.ruleset["ability_generation_methods"] == ["point_buy_75"]
