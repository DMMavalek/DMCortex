from __future__ import annotations

from dmcortex.models import Combatant, Encounter


class CombatTracker:
    def __init__(self, encounter_name: str) -> None:
        self.encounter = Encounter(name=encounter_name)

    def add_combatant(self, combatant: Combatant) -> None:
        self.encounter.combatants.append(combatant)
        self.encounter.combatants.sort(key=lambda c: c.initiative, reverse=True)

    def next_round(self) -> int:
        self.encounter.round_number += 1
        return self.encounter.round_number

    def apply_damage(self, combatant_name: str, damage: int) -> int:
        for combatant in self.encounter.combatants:
            if combatant.name == combatant_name:
                combatant.hp_current = max(0, combatant.hp_current - damage)
                return combatant.hp_current
        raise ValueError(f"Combatant not found: {combatant_name}")
