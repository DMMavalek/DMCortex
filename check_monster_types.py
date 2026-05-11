import json
from pathlib import Path

p = Path("c:/Users/kelava/Documents/Projects/Dungeon Master Cortex/data/rulesets/monsters.json")
obj = json.loads(p.read_text(encoding="utf-8"))
mons = obj.get("monsters", [])

str_fields = [
    "id", "name", "monster_type", "source", "hit_dice", "movement", "damage",
    "special_attacks", "special_defenses", "magic_resistance", "size", "morale",
    "number_appearing", "frequency", "intelligence", "alignment", "treasure_type", "description"
]
int_fields = ["armor_class", "thac0", "attacks", "xp_value"]

issues = []
for i, m in enumerate(mons):
    mid = m.get("id", "<missing>")
    for k in str_fields:
        if k in m and not isinstance(m[k], str):
            issues.append((i, mid, k, type(m[k]).__name__, m[k]))
    for k in int_fields:
        if k in m and not isinstance(m[k], int):
            issues.append((i, mid, k, type(m[k]).__name__, m[k]))

print("total", len(mons))
print("issues", len(issues))
for row in issues:
    print(row)
