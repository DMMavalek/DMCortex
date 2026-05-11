from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict


class RulesEngine:
    def __init__(self, ruleset: Dict[str, Any]) -> None:
        self.ruleset = ruleset

    @classmethod
    def from_file(cls, file_path: Path) -> "RulesEngine":
        with file_path.open("r", encoding="utf-8") as handle:
            payload = json.load(handle)
        return cls(payload)

    def get_race(self, race_id: str) -> Dict[str, Any]:
        return self.ruleset["races"][race_id]

    def get_class(self, class_id: str) -> Dict[str, Any]:
        return self.ruleset["classes"][class_id]

    def list_races(self) -> Dict[str, Dict[str, Any]]:
        return self.ruleset["races"]

    def list_classes(self) -> Dict[str, Dict[str, Any]]:
        return self.ruleset["classes"]

    def get_extracted_race_constraints(self, race_id: str) -> Dict[str, Any]:
        extracted = self.ruleset.get("extracted_constraints", {})
        return extracted.get("races", {}).get(race_id, {})

    def get_extracted_kit_constraints(self, kit_id: str) -> Dict[str, Any]:
        extracted = self.ruleset.get("extracted_constraints", {})
        return extracted.get("kits", {}).get(kit_id, {})
