from dmcortex.character_builder import CharacterBuilder
from dmcortex.rules_engine import RulesEngine


def test_validate_choice_uses_extracted_race_and_kit_constraints() -> None:
    rules = RulesEngine(
        {
            "races": {
                "human": {
                    "name": "Human",
                    "ability_minimums": {},
                    "ability_maximums": {},
                }
            },
            "classes": {
                "fighter": {
                    "name": "Fighter",
                    "ability_minimums": {},
                    "allowed_races": ["human"],
                }
            },
            "extracted_constraints": {
                "races": {
                    "human": {
                        "ability_minimums": {"wis": 12},
                    }
                },
                "kits": {
                    "animal_master": {
                        "ability_minimums": {"wis": 12},
                    }
                },
            },
        }
    )

    builder = CharacterBuilder(rules)

    issues = builder.validate_choice(
        race_id="human",
        class_id="fighter",
        abilities={"str": 15, "dex": 10, "con": 10, "int": 10, "wis": 11, "cha": 10},
        kit_id="animal_master",
    )

    assert any("Race minimum failed: wis" in issue for issue in issues)
    assert any("Kit minimum failed (animal_master): wis" in issue for issue in issues)
