from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List

AbilityScores = Dict[str, int]


@dataclass(slots=True)
class CharacterSheet:
    name: str
    race_id: str
    class_id: str
    abilities: AbilityScores
    notes: List[str] = field(default_factory=list)


@dataclass(slots=True)
class Combatant:
    name: str
    initiative: int
    hp_current: int
    hp_max: int
    statuses: List[str] = field(default_factory=list)


@dataclass(slots=True)
class Encounter:
    name: str
    round_number: int = 1
    combatants: List[Combatant] = field(default_factory=list)


@dataclass(slots=True)
class CampaignEntry:
    session_id: str
    title: str
    body: str
    tags: List[str] = field(default_factory=list)


@dataclass(slots=True)
class CampaignRulesConfig:
    profile_id: str = "core_only"


@dataclass(slots=True)
class CharacterRulesConfig:
    character_name: str
    overlay_files: List[str] = field(default_factory=list)
