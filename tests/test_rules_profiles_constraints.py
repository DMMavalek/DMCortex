import json
from pathlib import Path

from dmcortex.rules_profiles import resolve_rules_from_profile


def test_profile_resolution_includes_extracted_constraints(tmp_path: Path) -> None:
    rulesets_root = tmp_path / "rulesets"
    profiles = rulesets_root / "profiles"
    rulesets_root.mkdir(parents=True)
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

    (profiles / "core.json").write_text(
        json.dumps(
            {
                "profile_id": "core",
                "name": "Core",
                "base_ruleset": "base.json",
                "overlays": [],
            }
        ),
        encoding="utf-8",
    )

    constraints = tmp_path / "constraints.json"
    constraints.write_text(json.dumps({"races": {"human": {"ability_minimums": {"wis": 12}}}}), encoding="utf-8")

    engine = resolve_rules_from_profile(
        rulesets_root=rulesets_root,
        profile_path=profiles / "core.json",
        extracted_constraints_path=constraints,
    )

    assert engine.ruleset["extracted_constraints"]["races"]["human"]["ability_minimums"]["wis"] == 12
