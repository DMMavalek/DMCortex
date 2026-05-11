from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any

ABILITY_MAP = {
    "strength": "str",
    "dexterity": "dex",
    "constitution": "con",
    "intelligence": "int",
    "wisdom": "wis",
    "charisma": "cha",
}

RACE_SECTION_MAP = {
    "Hill Dwarf": "hill_dwarf",
    "Mountain Dwarf": "mountain_dwarf",
    "Deep Dwarf": "deep_dwarf",
    "Duergar": "duergar",
}


def _slugify(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", value.lower()).strip("_")


def _rtf_to_text(raw: str) -> str:
    text = raw.replace("\\par", "\n").replace("\\tab", "\t")
    text = re.sub(r"\\'[0-9a-fA-F]{2}", "", text)
    text = re.sub(r"\\[a-zA-Z]+-?\d*\s?", "", text)
    text = text.replace("{", "").replace("}", "")
    text = re.sub(r"\n{2,}", "\n", text)
    return text


def _extract_race_ability_constraints(text: str, source_file: str) -> dict[str, dict[str, Any]]:
    races: dict[str, dict[str, Any]] = {}

    for race_name, race_id in RACE_SECTION_MAP.items():
        marker = f"{race_name} Ability Scores"
        start = text.find(marker)
        if start == -1:
            continue

        end = text.find("Languages:", start)
        block = text[start:end] if end != -1 else text[start : start + 1500]

        minimums: dict[str, int] = {}
        maximums: dict[str, int] = {}

        for line in block.splitlines():
            line_clean = line.strip()
            for ability_name, ability_id in ABILITY_MAP.items():
                if not line_clean.lower().startswith(ability_name):
                    continue
                numbers = re.findall(r"\d+", line_clean)
                if len(numbers) < 2:
                    continue
                minimums[ability_id] = int(numbers[0])
                maximums[ability_id] = int(numbers[1])

        if minimums or maximums:
            races[race_id] = {
                "name": race_name,
                "ability_minimums": minimums,
                "ability_maximums": maximums,
                "source": source_file,
            }

    return races


def _extract_kit_requirements(text: str, source_file: str) -> dict[str, dict[str, Any]]:
    kits: dict[str, dict[str, Any]] = {}

    simple_pattern = re.compile(
        r"An?\s+([A-Za-z][A-Za-z\s\-/']+?)\s+must have a\s+([A-Za-z]+)\s+of\s+(\d+)\s+or more",
        flags=re.IGNORECASE,
    )
    multi_pattern = re.compile(
        r"An?\s+([A-Za-z][A-Za-z\s\-/']+?)\s+must have minimum scores of\s+(\d+)\s+in\s+([A-Za-z]+)\s+and\s+([A-Za-z]+)",
        flags=re.IGNORECASE,
    )

    for match in simple_pattern.finditer(text):
        kit_name = re.sub(r"\s+", " ", match.group(1)).strip()
        ability_name = match.group(2).lower()
        score = int(match.group(3))
        ability_id = ABILITY_MAP.get(ability_name)
        if not ability_id:
            continue

        kit_id = _slugify(kit_name)
        kits[kit_id] = {
            "name": kit_name,
            "ability_minimums": {ability_id: score},
            "source": source_file,
        }

    for match in multi_pattern.finditer(text):
        kit_name = re.sub(r"\s+", " ", match.group(1)).strip()
        score = int(match.group(2))
        ability_a = ABILITY_MAP.get(match.group(3).lower())
        ability_b = ABILITY_MAP.get(match.group(4).lower())
        if not ability_a or not ability_b:
            continue

        kit_id = _slugify(kit_name)
        kits[kit_id] = {
            "name": kit_name,
            "ability_minimums": {ability_a: score, ability_b: score},
            "source": source_file,
        }

    return kits


def _extract_proficiency_slots(text: str, source_file: str) -> dict[str, dict[str, Any]]:
    proficiencies: dict[str, dict[str, Any]] = {}

    patterns = {
        "warrior": r"Warrior\s+(\d+)\s+(\d+)\s+(-?\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)",
        "priest": r"Priest\s+(\d+)\s+(\d+)\s+(-?\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)",
        "thief": r"Thief\s+(\d+)\s+(\d+)\s+(-?\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)",
        "warrior_priest": r"Warrior/Priest\s+(\d+)\s+(\d+)\s+(-?\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)",
        "warrior_thief": r"Warrior/Thief\s+(\d+)\s+(\d+)\s+(-?\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)",
    }

    for role, pattern in patterns.items():
        match = re.search(pattern, text, flags=re.IGNORECASE)
        if not match:
            continue
        values = [int(group) for group in match.groups()]
        proficiencies[role] = {
            "weapon_initial": values[0],
            "weapon_new_every_levels": values[1],
            "weapon_nonproficiency_penalty": values[2],
            "nonweapon_initial": values[3],
            "nonweapon_new_every_levels": values[4],
            "detection_initial": values[5],
            "detection_new_every_levels": values[6],
            "source": source_file,
        }

    return proficiencies


def extract_numeric_rules_from_books(books_root: Path) -> dict[str, Any]:
    races: dict[str, dict[str, Any]] = {}
    kits: dict[str, dict[str, Any]] = {}
    proficiencies: dict[str, dict[str, Any]] = {}

    for book_file in sorted(books_root.glob("*.RTF")):
        with book_file.open("r", encoding="cp1252", errors="ignore") as handle:
            raw = handle.read()

        plain_text = _rtf_to_text(raw)
        source = str(book_file)

        races.update(_extract_race_ability_constraints(plain_text, source))
        kits.update(_extract_kit_requirements(plain_text, source))

        extracted_prof = _extract_proficiency_slots(plain_text, source)
        for key, value in extracted_prof.items():
            proficiencies.setdefault(key, value)

    return {
        "schema": "dmcortex.rules.numeric.v1",
        "counts": {
            "races": len(races),
            "kits": len(kits),
            "proficiencies": len(proficiencies),
        },
        "races": races,
        "kits": kits,
        "proficiencies": proficiencies,
    }


def write_numeric_rules(books_root: Path, output_file: Path) -> Path:
    payload = extract_numeric_rules_from_books(books_root)
    output_file.parent.mkdir(parents=True, exist_ok=True)

    with output_file.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)

    return output_file
