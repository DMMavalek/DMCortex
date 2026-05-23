from __future__ import annotations

import json
from dataclasses import asdict
from pathlib import Path
from typing import Any, List

from dmcortex.models import CharacterSheet

UNASSIGNED_PARTY = "Unassigned"


def save_characters(file_path: Path, characters: List[CharacterSheet]) -> None:
    file_path.parent.mkdir(parents=True, exist_ok=True)
    payload = [asdict(character) for character in characters]
    with file_path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)


def load_characters(file_path: Path) -> List[CharacterSheet]:
    if not file_path.exists():
        return []

    with file_path.open("r", encoding="utf-8") as handle:
        payload: Any = json.load(handle)

    if isinstance(payload, dict):
        payload = payload.get("characters", [])

    if not isinstance(payload, list):
        return []

    loaded: List[CharacterSheet] = []
    for item in payload:
        if not isinstance(item, dict):
            continue

        abilities = item.get("abilities", {})
        if not isinstance(abilities, dict):
            abilities = {}

        notes = item.get("notes", [])
        if not isinstance(notes, list):
            notes = []

        name = str(item.get("name", "")).strip()
        race_id = str(item.get("race_id", "")).strip()
        class_id = str(item.get("class_id", "")).strip()
        if not name or not race_id or not class_id:
            continue

        loaded.append(
            CharacterSheet(
                name=name,
                race_id=race_id,
                class_id=class_id,
                abilities={str(k): int(v) for k, v in abilities.items() if isinstance(v, int)},
                player_name=str(item.get("player_name", "")).strip(),
                party_name=str(item.get("party_name", UNASSIGNED_PARTY)).strip() or UNASSIGNED_PARTY,
                notes=[str(note) for note in notes],
            )
        )

    return loaded


def save_state(file_path: Path, characters: List[CharacterSheet], parties: List[str]) -> None:
    file_path.parent.mkdir(parents=True, exist_ok=True)

    normalized_parties: List[str] = []
    seen = set()
    for party in parties:
        party_name = str(party).strip()
        if not party_name:
            continue
        lowered = party_name.lower()
        if lowered in seen:
            continue
        seen.add(lowered)
        normalized_parties.append(party_name)

    if UNASSIGNED_PARTY.lower() not in seen:
        normalized_parties.insert(0, UNASSIGNED_PARTY)

    payload = {
        "characters": [asdict(character) for character in characters],
        "parties": normalized_parties,
    }
    with file_path.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)


def load_state(file_path: Path) -> tuple[List[CharacterSheet], List[str]]:
    characters = load_characters(file_path)
    parties = sorted(
        {
            (character.party_name or UNASSIGNED_PARTY).strip() or UNASSIGNED_PARTY
            for character in characters
        },
        key=str.lower,
    )

    if not file_path.exists():
        if UNASSIGNED_PARTY not in parties:
            parties.insert(0, UNASSIGNED_PARTY)
        return characters, parties

    try:
        with file_path.open("r", encoding="utf-8") as handle:
            payload: Any = json.load(handle)
    except Exception:
        if UNASSIGNED_PARTY not in parties:
            parties.insert(0, UNASSIGNED_PARTY)
        return characters, parties

    if isinstance(payload, dict):
        persisted = payload.get("parties", [])
        if isinstance(persisted, list):
            for party in persisted:
                party_name = str(party).strip()
                if party_name and party_name.lower() not in {p.lower() for p in parties}:
                    parties.append(party_name)

    if UNASSIGNED_PARTY.lower() not in {p.lower() for p in parties}:
        parties.insert(0, UNASSIGNED_PARTY)

    parties.sort(key=str.lower)
    return characters, parties
