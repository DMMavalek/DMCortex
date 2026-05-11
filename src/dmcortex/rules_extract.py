from __future__ import annotations

import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any

RACE_KEYWORDS = {
    "human",
    "elf",
    "elves",
    "half-elf",
    "dwarf",
    "dwarves",
    "duergar",
    "gnome",
    "halfling",
    "orc",
    "goblin",
}
CLASS_KEYWORDS = {
    "fighter",
    "wizard",
    "mage",
    "cleric",
    "priest",
    "thief",
    "rogue",
    "ranger",
    "paladin",
    "druid",
    "bard",
}
PROFICIENCY_TOKENS = {
    "proficiency",
    "proficiencies",
    "weapon specialization",
    "nonweapon",
}


@dataclass(slots=True)
class ExtractedEntity:
    entity_type: str
    name: str
    source_book: str
    source_file: str
    source_href: str

    @property
    def entity_id(self) -> str:
        base = re.sub(r"[^a-z0-9]+", "_", self.name.lower()).strip("_")
        return base or "unknown"

    def to_dict(self) -> dict[str, str]:
        return {
            "id": self.entity_id,
            "name": self.name,
            "source_book": self.source_book,
            "source_file": self.source_file,
            "source_href": self.source_href,
        }


def _normalize_topic(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def _classify_topic(topic: str, in_kits_section: bool) -> str | None:
    lowered = topic.lower()

    if any(token in lowered for token in PROFICIENCY_TOKENS):
        return "proficiencies"

    if "kit" in lowered:
        return "kit_sections"

    if in_kits_section and not lowered.startswith("chapter"):
        return "kits"

    words = set(re.findall(r"[a-zA-Z\-']+", lowered))

    if words & RACE_KEYWORDS:
        return "races"
    if words & CLASS_KEYWORDS:
        return "classes"

    return None


def extract_entities_from_index(index_payload: dict[str, Any]) -> dict[str, Any]:
    buckets: dict[str, list[ExtractedEntity]] = {
        "races": [],
        "classes": [],
        "kits": [],
        "proficiencies": [],
        "kit_sections": [],
    }

    webhelp_files = index_payload.get("webhelp_files", [])

    for page in webhelp_files:
        title = page.get("title") or "Unknown Source"
        source_file = page.get("path", "")
        topics = page.get("topics", [])

        in_kits_section = False
        for topic in topics:
            name = _normalize_topic(topic.get("text", ""))
            if not name:
                continue

            if "chapter" in name.lower() and "kit" in name.lower():
                in_kits_section = True
            elif name.lower().startswith("chapter") and "kit" not in name.lower():
                in_kits_section = False

            classified = _classify_topic(name, in_kits_section)
            if not classified:
                continue

            buckets[classified].append(
                ExtractedEntity(
                    entity_type=classified,
                    name=name,
                    source_book=title,
                    source_file=source_file,
                    source_href=topic.get("href", ""),
                )
            )

    materialized: dict[str, list[dict[str, str]]] = {}
    for bucket_name, entities in buckets.items():
        dedupe: dict[str, ExtractedEntity] = {}
        for entity in entities:
            dedupe.setdefault(entity.entity_id, entity)
        materialized[bucket_name] = [item.to_dict() for item in dedupe.values()]

    return {
        "schema": "dmcortex.rules.extract.v1",
        "source_index_schema": index_payload.get("schema", "unknown"),
        "counts": {key: len(value) for key, value in materialized.items()},
        "entities": materialized,
    }


def write_structured_entities(index_file: Path, output_file: Path) -> Path:
    with index_file.open("r", encoding="utf-8") as handle:
        index_payload = json.load(handle)

    payload = extract_entities_from_index(index_payload)
    output_file.parent.mkdir(parents=True, exist_ok=True)

    with output_file.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)

    return output_file
