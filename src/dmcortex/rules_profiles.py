from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from dmcortex.rules_engine import RulesEngine


@dataclass(slots=True)
class RulesProfile:
    profile_id: str
    name: str
    base_ruleset: str
    overlays: list[str]


def load_profile(profile_path: Path) -> RulesProfile:
    with profile_path.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)

    return RulesProfile(
        profile_id=payload["profile_id"],
        name=payload["name"],
        base_ruleset=payload["base_ruleset"],
        overlays=list(payload.get("overlays", [])),
    )


def _read_json(file_path: Path) -> dict[str, Any]:
    with file_path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def _deep_merge(base: dict[str, Any], overlay: dict[str, Any]) -> dict[str, Any]:
    merged = dict(base)
    for key, value in overlay.items():
        if key in merged and isinstance(merged[key], dict) and isinstance(value, dict):
            merged[key] = _deep_merge(merged[key], value)
            continue
        merged[key] = value
    return merged


def resolve_rules_from_profile(
    rulesets_root: Path,
    profile_path: Path,
    character_overlay_paths: list[Path] | None = None,
    extracted_constraints_path: Path | None = None,
) -> RulesEngine:
    profile = load_profile(profile_path)

    base_payload = _read_json(rulesets_root / profile.base_ruleset)
    merged_payload = dict(base_payload)

    for overlay_rel in profile.overlays:
        overlay_payload = _read_json(rulesets_root / overlay_rel)
        merged_payload = _deep_merge(merged_payload, overlay_payload)

    for overlay_path in character_overlay_paths or []:
        merged_payload = _deep_merge(merged_payload, _read_json(overlay_path))

    if extracted_constraints_path and extracted_constraints_path.exists():
        merged_payload["extracted_constraints"] = _read_json(extracted_constraints_path)

    return RulesEngine(merged_payload)
