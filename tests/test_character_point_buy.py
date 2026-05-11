from dmcortex.character_builder import CharacterBuilder
from dmcortex.rules_engine import RulesEngine


def test_point_buy_generation_works_when_enabled() -> None:
    rules = RulesEngine(
        {
            "ability_generation_methods": ["point_buy_75"],
            "point_buy": {"enabled": True, "budget": 75, "minimum": 3, "maximum": 18},
            "races": {"human": {"ability_minimums": {}, "ability_maximums": {}}},
            "classes": {"fighter": {"ability_minimums": {}, "allowed_races": ["human"]}},
        }
    )

    builder = CharacterBuilder(rules)
    abilities = builder.generate_abilities("point_buy_75")

    assert sum(abilities.values()) == 75
    assert min(abilities.values()) >= 3
    assert max(abilities.values()) <= 18
