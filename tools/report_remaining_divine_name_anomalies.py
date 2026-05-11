from __future__ import annotations

from pathlib import Path
import json
import re

JSON_PATH = Path(r"C:/Users/kelava/Documents/Projects/Dungeon Master Cortex/data/rulesets/spells.json")
OUT_TXT = Path(r"C:/Users/kelava/Documents/Projects/Dungeon Master Cortex/Assets/Spells/Priest Spells/divine_name_anomalies_review.txt")


def t(v: object) -> str:
    return "" if v is None else str(v).strip()


def main() -> int:
    data = json.load(JSON_PATH.open("r", encoding="utf-8"))
    spells = [s for s in data.get("spells", []) if t(s.get("category", "")).lower() == "divine"]

    lines = []
    lines.append("Remaining divine name anomalies for manual review")
    lines.append("")

    count = 0
    for s in spells:
        sid = t(s.get("id", ""))
        name = t(s.get("name", ""))
        brief = t(s.get("brief_description", ""))
        desc = t(s.get("description", ""))

        suspicious = False
        if name.startswith("Notes:"):
            suspicious = True
        if len(name) > 45:
            suspicious = True
        if re.search(r"\bleast\b|\bthis case\b|10-foot|thick-", name, re.IGNORECASE):
            suspicious = True
        if not suspicious:
            continue

        count += 1
        lines.append(f"ID: {sid}")
        lines.append(f"Name: {name}")
        if brief:
            lines.append(f"Brief: {brief[:220]}")
        if desc:
            lines.append(f"Desc: {desc[:220]}")
        lines.append("")

    OUT_TXT.write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote review file: {OUT_TXT}")
    print(f"Anomaly count: {count}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
