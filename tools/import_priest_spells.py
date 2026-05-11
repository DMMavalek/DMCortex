"""
One-time importer: AD&D Priest Spells xlsx → spells.json
Appends divine spells to the existing spells.json.
"""

import json
import re
import openpyxl
from pathlib import Path

XLSX_PATH = Path(r"C:\Users\kelava\Documents\Projects\Dungeon Master Cortex\Assets\Spells\Priest Spells\AD_and_D_Priest_Spells_Final.xlsx")
JSON_PATH = Path(r"C:\Users\kelava\Documents\Projects\Dungeon Master Cortex\data\rulesets\spells.json")

# Column indices (0-based after reading row values)
COL_NAME        = 0
COL_LEVEL       = 1
COL_CAST_TIME   = 2
COL_RANGE       = 3
COL_DURATION    = 4
COL_AREA        = 5
COL_COMPONENTS  = 6
COL_DESCRIPTION = 7
COL_DOMAINS     = 8
COL_SPHERES     = 9


def clean(val) -> str:
    if val is None:
        return ""
    return str(val).strip()


def make_brief(description: str) -> str:
    """First sentence, capped at 200 chars."""
    if not description:
        return ""
    # try to split on first period followed by space or end
    m = re.search(r'\.[\s]', description)
    if m and m.start() <= 200:
        return description[: m.start() + 1].strip()
    return description[:200].strip()


def slugify(name: str) -> str:
    s = name.lower()
    s = re.sub(r"[^a-z0-9]+", "_", s)
    return s.strip("_")[:40]


def main():
    # Load existing spells
    with open(JSON_PATH, encoding="utf-8") as f:
        data = json.load(f)

    existing_ids = {s["id"] for s in data["spells"]}

    # Determine next numeric suffix for PRI IDs
    pri_nums = []
    for sid in existing_ids:
        m = re.match(r"^PRI(\d+)$", sid, re.IGNORECASE)
        if m:
            pri_nums.append(int(m.group(1)))
    next_num = (max(pri_nums) + 1) if pri_nums else 1

    wb = openpyxl.load_workbook(XLSX_PATH)
    ws = wb.active

    new_spells = []
    skipped = 0

    for row in ws.iter_rows(min_row=2, values_only=True):
        name = clean(row[COL_NAME])
        if not name:
            skipped += 1
            continue

        level       = clean(row[COL_LEVEL])
        cast_time   = clean(row[COL_CAST_TIME])
        range_      = clean(row[COL_RANGE])
        duration    = clean(row[COL_DURATION])
        area        = clean(row[COL_AREA])
        components  = clean(row[COL_COMPONENTS])
        description = clean(row[COL_DESCRIPTION])
        domains     = clean(row[COL_DOMAINS])
        spheres     = clean(row[COL_SPHERES])

        brief = make_brief(description)

        # Generate a unique ID
        base_id = f"PRI{next_num:06d}"
        while base_id in existing_ids:
            next_num += 1
            base_id = f"PRI{next_num:06d}"

        existing_ids.add(base_id)
        next_num += 1

        spell = {
            "id":                           base_id,
            "level":                        level,
            "name":                         name,
            "reversal":                     "",
            "schools":                      spheres,
            "range":                        range_,
            "components":                   components,
            "materials":                    "",
            "cast_time":                    cast_time,
            "duration":                     duration,
            "area":                         area,
            "save":                         "",
            "frequency":                    "",
            "volume":                       "",
            "page":                         domains,
            "brief_description":            brief,
            "description":                  description,
            "category":                     "divine",
            "damage":                       "",
            "damage_step":                  "",
            "damage_scale_start_level":     "",
            "damage_scale_every_levels":    "",
            "damage_max_at_level":          "",
            "damage_max":                   "",
        }
        new_spells.append(spell)

    data["spells"].extend(new_spells)

    with open(JSON_PATH, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

    print(f"Done. Imported {len(new_spells)} divine spells. Skipped {skipped} empty-name rows.")
    print(f"Total spells now: {len(data['spells'])}")


if __name__ == "__main__":
    main()
