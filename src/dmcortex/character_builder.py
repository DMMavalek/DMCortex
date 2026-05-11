from __future__ import annotations

import random
from typing import Dict, List

from dmcortex.models import AbilityScores, CharacterSheet
from dmcortex.rules_engine import RulesEngine

ABILITIES = ("str", "dex", "con", "int", "wis", "cha")


class CharacterBuilder:
    def __init__(self, rules: RulesEngine) -> None:
        self.rules = rules

    def generate_abilities(self, method: str) -> AbilityScores:
        if method == "standard_array":
            values = list(self.rules.ruleset["standard_array"])
            return {ability: values[index] for index, ability in enumerate(ABILITIES)}
        if method == "4d6_drop_lowest":
            rolls = [self._roll_4d6_drop_lowest() for _ in ABILITIES]
            rolls.sort(reverse=True)
            return {ability: rolls[index] for index, ability in enumerate(ABILITIES)}
        if method == "point_buy_75":
            return self._point_buy_default_allocation()
        raise ValueError(f"Unsupported generation method: {method}")

    def validate_choice(
        self,
        race_id: str,
        class_id: str,
        abilities: AbilityScores,
        kit_id: str | None = None,
    ) -> List[str]:
        issues: List[str] = []
        race = self.rules.get_race(race_id)
        char_class = self.rules.get_class(class_id)
        extracted_race = self.rules.get_extracted_race_constraints(race_id)

        race_mins = self._merge_minimums(
            race.get("ability_minimums", {}),
            extracted_race.get("ability_minimums", {}),
        )
        race_maxes = self._merge_maximums(
            race.get("ability_maximums", {}),
            extracted_race.get("ability_maximums", {}),
        )

        for ability, minimum in race_mins.items():
            if abilities.get(ability, 0) < minimum:
                issues.append(f"Race minimum failed: {ability} must be at least {minimum}")

        for ability, maximum in race_maxes.items():
            if abilities.get(ability, 99) > maximum:
                issues.append(f"Race maximum failed: {ability} must be at most {maximum}")

        for ability, minimum in char_class.get("ability_minimums", {}).items():
            if abilities.get(ability, 0) < minimum:
                issues.append(f"Class minimum failed: {ability} must be at least {minimum}")

        if kit_id:
            extracted_kit = self.rules.get_extracted_kit_constraints(kit_id)
            for ability, minimum in extracted_kit.get("ability_minimums", {}).items():
                if abilities.get(ability, 0) < minimum:
                    issues.append(f"Kit minimum failed ({kit_id}): {ability} must be at least {minimum}")

        allowed_races = set(char_class.get("allowed_races", []))
        if allowed_races and race_id not in allowed_races:
            issues.append(f"Race {race_id} is not allowed for class {class_id}")

        return issues

    def build_character(self, name: str, race_id: str, class_id: str, abilities: Dict[str, int]) -> CharacterSheet:
        issues = self.validate_choice(race_id, class_id, abilities)
        if issues:
            raise ValueError("Invalid character choice: " + "; ".join(issues))
        return CharacterSheet(name=name, race_id=race_id, class_id=class_id, abilities=abilities)

    @staticmethod
    def _roll_4d6_drop_lowest() -> int:
        rolls = sorted(random.randint(1, 6) for _ in range(4))
        return sum(rolls[1:])

    def _point_buy_default_allocation(self) -> AbilityScores:
        point_buy = self.rules.ruleset.get("point_buy", {})
        if not point_buy.get("enabled", False):
            raise ValueError("Point-buy method is not enabled in this ruleset")

        budget = int(point_buy.get("budget", 75))
        minimum = int(point_buy.get("minimum", 3))
        maximum = int(point_buy.get("maximum", 18))

        base = [minimum for _ in ABILITIES]
        remaining = budget - sum(base)

        index = 0
        while remaining > 0:
            if base[index] < maximum:
                base[index] += 1
                remaining -= 1
            index = (index + 1) % len(base)

        return {ability: base[pos] for pos, ability in enumerate(ABILITIES)}

    @staticmethod
    def _merge_minimums(*sources: Dict[str, int]) -> Dict[str, int]:
        merged: Dict[str, int] = {}
        for source in sources:
            for ability, minimum in source.items():
                merged[ability] = max(merged.get(ability, minimum), minimum)
        return merged

    @staticmethod
    def _merge_maximums(*sources: Dict[str, int]) -> Dict[str, int]:
        merged: Dict[str, int] = {}
        for source in sources:
            for ability, maximum in source.items():
                if ability not in merged:
                    merged[ability] = maximum
                else:
                    merged[ability] = min(merged[ability], maximum)
        return merged
