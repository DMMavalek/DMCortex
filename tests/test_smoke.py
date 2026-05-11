from pathlib import Path

from dmcortex.character_builder import CharacterBuilder
from dmcortex.rules_engine import RulesEngine


def test_can_build_valid_character() -> None:
    root = Path(__file__).resolve().parents[1]
    rules = RulesEngine.from_file(root / "data" / "rulesets" / "core_2e.json")
    builder = CharacterBuilder(rules)

    abilities = {
        "str": 15,
        "dex": 14,
        "con": 13,
        "int": 12,
        "wis": 10,
        "cha": 8,
    }

    character = builder.build_character("Test", "human", "fighter", abilities)

    assert character.class_id == "fighter"
