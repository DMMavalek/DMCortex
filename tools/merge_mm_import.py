"""
Strict-filter merge of MM PDF import staging data into monsters.json.

Applies:
  - Name validation (no garbled chars, no field-label prefixes, reasonable length)
  - HD validation (clean format, no merged stats)
  - Known OCR name corrections
  - Deduplication (skips dragons already covered with age_stages)
  - Keeps narrative text (Combat, Habitat/Society, Ecology) even when stats are partial

Usage:
  python tools/merge_mm_import.py [--dry-run]
"""
import json
import re
import sys
from pathlib import Path

WORKSPACE = Path(__file__).parent.parent
IMPORT_PATH = WORKSPACE / "data/import/mm_pdf_import.json"
MONSTERS_PATH = WORKSPACE / "data/rulesets/monsters.json"

DRY_RUN = '--dry-run' in sys.argv

# ── OCR name corrections ─────────────────────────────────────────────────────
NAME_CORRECTIONS = {
    "Dopplegnnger":              "Doppelganger",
    "Homonculous":               "Homunculus",
    "Elenhant":                  "Elephant",
    "Griff on":                  "Griffon",
    "IxitxacT tl":               "Ixitxachitl",
    "Mind Flnyer (Illithid)":    "Mind Flayer (Illithid)",
    "Githvnnki":                 "Githyanki",
    "lycanthrope, Wereraven":    "Lycanthrope, Wereraven",
    "lycanthrope, Werebear":     "Lycanthrope, Werebear",
    "Grip p li":                 "Grippli",
    "Annis":                     "Hag, Annis",
    "rhought-Eater":             "Thought-Eater",
    "Toad, Gianr":               "Toad, Giant",
    "Ooze/Slime/lellv":          "Ooze/Slime/Jelly",
    "Ooze/Slime/Jell y":         "Ooze/Slime/Jelly",
    "Dopplegnnger":              "Doppelganger",
    "IxitxacT tl":               "Ixitxachitl",
    "Gremlin, Jermlaine":        "Gremlin, Jermlaine",   # keep as-is (correct name)
    "Tasloi":                    "SKIP",      # HD garbled
    "Leech I":                   "SKIP",      # garbled suffix
    "Unicorn I":                 "SKIP",      # garbled suffix
    "hP Quasit":                 "SKIP",      # garbled prefix
    "Feyr Great Feyr":           "SKIP",      # multi-species
    "Lamia Lamia Noble":         "SKIP",      # multi-species (Lamia + Lamia Noble)
    "Rakshasa Rakshasa, Greater":"SKIP",      # doubled name / multi-species
    "Balor Madlith":             "SKIP",      # garbled multi (Tanar'ri)
    "Beholder and Beholder-kin": "SKIP",      # multi-species umbrella
    "Lesser Greater":            "SKIP",      # multi-species
    "Common Killer":             "SKIP",      # garbled whale page
    "Guardian Spirit Water":     "SKIP",      # multi-type Naga page
    "Guardian Spirit Water":     "SKIP",      # multi-type Naga page
    "Black White":               "SKIP",      # garbled (Pudding types)
    "Gargantua":                 "SKIP",      # umbrella entry (many gargantua types)
    "Beetle, Giant":             "SKIP",      # umbrella (Bombardier/Fire/etc.)
    "Baatezu":                   "SKIP",      # umbrella for all devil types
    "Giant":                     "SKIP",      # too generic
    "Mammal, Herd":              "SKIP",      # too generic umbrella
    "Grell":                     "SKIP",      # HD has 3 variants merged
    "Heucuva":                   "SKIP",      # HD has 2 merged values
    "Lammasu":                   "SKIP",      # HD has 2 variants merged
    "Brownie":                   "SKIP",      # HD has 2 identical values (OCR column artifact)
    "Crocodile":                 "SKIP",      # HD has 2 sizes merged
    "Crustacean, Giant":         "SKIP",      # HD has 2 sizes merged
    "Elemental, Water Kin":      "SKIP",      # HD has 2 values merged
    "Fungus":                    "SKIP",      # HD has 2 types merged
    "Broken One":                "SKIP",      # HD has 2 values merged
    "Dwarf":                     "SKIP",      # multiple dwarf types merged; add individually
}

# Field label prefixes that indicate a garbled monster name
FIELD_LABEL_PREFIXES = (
    'CLIMAT', 'CUMATE', 'CWMATE', 'CLMATEFTERRAIN', 'CLMT', 'CLIWE',
    'CLlMATE', 'CUMA', 'CLlMATF', 'CUMATER', 'CLIMATER', 'CWMATEFTERRAIN',
    'FREQUENCY:', 'SPECIAL', 'TERRAIN:', 'ARMOR', 'NO. OF', 'NO.OF',
    'THAC', 'DIET:', 'TREASURE:', 'MORALE:', 'INTEL',
    'CLIMUETERBWN', 'CLIMATVIZRRAIN', 'CLIHU', 'CLIHUTE', 'CLIMA',
    'OTYWH', 'CLIME',
)

# Monsters that should be skipped — dragons already have full age_stages data,
# and entries that are known multi-species garbage.
SKIP_IDS = {
    # All dragon entries: already in dataset with full age stage tables
    'mon_dragon_chromatic_p1wk_dragon', 'mon_dragon_chromatic_green_dragon',
    'mon_red_dragon_dragon_chromatic', 'mon_amethyst_dragon_dragon_gem',
    '1 dragon_gem_crystal_dragon', 'mon_dragon_gem_crystal_dragon',
    'mon_emerald_dragon_dragon_gem', 'mon_dragon_metallic',
    'mon_cumatmterrain_tropical_subtropical_and_temperate',
    'mon_brown_dragon', 'mon_cloud_dragon', 'mon_deep_dragon',
    'mon_mercury_dragon', 'mon_mist_dragon', 'mon_shadow_dragon',
    'mon_steel_dragon', 'mon_cumatfjterrain_desert',
    'mon_clmtefterrain_subtropical_and_temperate',
    'mon_climatvterrain_temperate_tropical_and_subtropical',
    'mon_dragonet_fi', 'mon_dragonet_pseudodragon', 'mon_dragonne',
    # Known multi-species / garbled
    'mon_lesser', 'mon_beai', 'mon_i',
}

SKIP_NAME_SUBSTRINGS = ('dragon',)   # lowercase; skip any residual dragon entries


def is_garbled_name(name: str) -> bool:
    if not name or len(name) < 3 or len(name) > 55:
        return True
    upper = name.upper().strip()
    for prefix in FIELD_LABEL_PREFIXES:
        if upper.startswith(prefix.upper()):
            return True
    # Contains ': ' suggesting embedded field label
    if ': ' in name and not name.endswith(':'):
        return True
    # Garbage chars
    if any(c in name for c in ('~', '`', '@', '#', '^', '*', '{', '}', '|', '<', '>', '!')):
        return True
    # Leading punctuation / dash / digit / quote
    if name[0] in ('-', "'", '.', ',', '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', ';', '?', 'm', 'h'):
        return True
    # Starts with lowercase (OCR junk prefix)
    if name[0].islower():
        return True
    # Multiple words all-caps suggests merged field labels
    words = name.split()
    if len(words) >= 3 and all(w.isupper() for w in words):
        return True
    # Roman numerals / single chars
    if re.match(r'^[IVXivx\d\s]+$', name.strip()):
        return True
    # Trailing standalone Roman numeral ( I, II)
    if re.search(r'\s+[IVX]+$', name) and name[-1].isupper():
        return True
    # Contains brackets which suggest merged stat data
    if '(' in name and ')' in name and re.search(r'\(\d+', name):
        return True
    # Duplicate word check — catches 'Foo Foo', 'Rakshasa Rakshasa, Greater'
    words_lower = [w.lower().strip('.,') for w in words if len(w) > 3]
    if len(words_lower) >= 2 and len(words_lower) != len(set(words_lower)):
        return True
    return False


def is_garbled_hd(hd: str) -> bool:
    if not hd:
        return False  # empty HD is valid (Varies/unknown)
    tokens = hd.split()
    # Max 2 tokens: e.g. "4" or "7+7" or "1/2" or "8 +3"
    # Three tokens only valid when third is "(base)" for dragons — but dragons are skipped
    if len(tokens) > 2:
        return True
    upper = hd.upper()
    for kw in ('THAC', 'DAMAGE', 'ATTACK', 'SPECIAL', 'DEFENS', 'PER', 'SEE'):
        if kw in upper:
            return True
    # Garbage chars
    if any(c in hd for c in ('~', '&', '@', '#', '!', "'", ';')):
        return True
    return False


def is_duplicate_name_in_name(name: str) -> bool:
    """Detect 'Foo Foo' or 'Foo Bar Foo' — double-named pages."""
    words = name.split()
    if len(words) >= 4:
        half = len(words) // 2
        if words[:half] == words[half:half*2]:
            return True
    # "Name1 Name2" where Name2 is a known species that appears redundantly
    return False


def is_multi_creature_name(name: str) -> bool:
    """Names with multiple creatures jammed together."""
    # e.g. "Wild Dog War Dog BUnk Dog", "Amphis Constrictor Constrictor Poison Po"
    words = name.split()
    if len(words) >= 5:
        return True
    # Contains 'and' which suggests combined entries
    if ' and ' in name.lower() and len(name) > 25:
        return True
    return False


def clean_hd(hd: str) -> str:
    """Normalize HD string."""
    if not hd:
        return ''
    # Fix 'ld' → '1d'
    hd = re.sub(r'\bl([d])', r'1\1', hd)
    # Remove trailing garbage
    hd = re.sub(r'[\s\-]+$', '', hd).strip()
    return hd


def clean_size(size_str: str) -> str:
    """Extract single size letter."""
    s = (size_str or 'M').strip().upper()
    for letter in ('T', 'S', 'M', 'L', 'H', 'G'):
        if s.startswith(letter):
            return letter
    return 'M'


def make_id(name: str) -> str:
    s = name.lower().strip()
    s = re.sub(r"[',\.\(\)]", '', s)
    s = re.sub(r'\s+', '_', s)
    s = re.sub(r'[^a-z0-9_]', '', s)
    s = re.sub(r'_+', '_', s).strip('_')
    return f'mon_{s}'


def normalize_xp(xp_raw) -> int:
    if isinstance(xp_raw, int):
        return xp_raw
    s = str(xp_raw).replace(',', '').replace('.', '').strip()
    m = re.search(r'(\d+)', s)
    return int(m.group(1)) if m else 0


def main():
    # Load staging data
    with open(IMPORT_PATH, encoding='utf-8') as f:
        staging = json.load(f)

    # Load existing monsters
    with open(MONSTERS_PATH, encoding='utf-8') as f:
        existing_data = json.load(f)

    existing = existing_data.get('monsters', [])
    existing_ids = {m['id'] for m in existing}
    existing_names_lower = {m['name'].lower().strip() for m in existing}

    accepted = []
    rejected = []

    for m in staging.get('monsters', []):
        raw_name = m.get('name', '').strip()
        name = NAME_CORRECTIONS.get(raw_name, raw_name)
        hd = clean_hd(m.get('hit_dice', ''))

        # Reject: explicitly skipped entries
        if name == 'SKIP':
            rejected.append((raw_name, 'explicitly_skipped'))
            continue

        # Reject: garbled name
        if is_garbled_name(name):
            rejected.append((raw_name, 'garbled_name'))
            continue

        # Reject: AC outside plausible range
        ac = int(m.get('armor_class', 10))
        if ac < -10 or ac > 25:
            rejected.append((raw_name, f'ac_out_of_range({ac})'))
            continue

        # Reject: multi-creature name
        if is_multi_creature_name(name):
            rejected.append((raw_name, 'multi_creature'))
            continue

        # Reject: garbled HD
        if is_garbled_hd(hd):
            rejected.append((raw_name, 'garbled_hd'))
            continue

        # Skip: dragon entries (already in dataset with age stages)
        if any(sub in name.lower() for sub in SKIP_NAME_SUBSTRINGS):
            rejected.append((raw_name, 'dragon_already_covered'))
            continue

        # Skip: already in dataset
        if name.lower().strip() in existing_names_lower:
            rejected.append((raw_name, 'already_in_dataset'))
            continue

        # Build clean monster entry
        monster_id = make_id(name)
        # Resolve ID collision
        base_id = monster_id
        suffix = 2
        while monster_id in existing_ids:
            monster_id = f"{base_id}_{suffix}"
            suffix += 1

        entry = {
            "id": monster_id,
            "name": name,
            "monster_type": m.get('monster_type', 'Standard'),
            "source": "Monstrous Manual",
            "hit_dice": hd,
            "armor_class": int(m.get('armor_class', 10)),
            "movement": m.get('movement', ''),
            "thac0": int(m.get('thac0', 20)),
            "attacks": int(m.get('attacks', 1)),
            "damage": m.get('damage', ''),
            "special_attacks": m.get('special_attacks', 'Nil'),
            "special_defenses": m.get('special_defenses', 'Nil'),
            "magic_resistance": m.get('magic_resistance', 'Nil'),
            "size": clean_size(m.get('size', 'M')),
            "morale": m.get('morale', ''),
            "xp_value": normalize_xp(m.get('xp_value', 0)),
            "number_appearing": m.get('number_appearing', ''),
            "frequency": m.get('frequency', ''),
            "intelligence": m.get('intelligence', ''),
            "alignment": m.get('alignment', ''),
            "treasure_type": m.get('treasure_type', 'Nil'),
            "description": m.get('description', ''),
            "combat": m.get('combat', ''),
            "habitat_society": m.get('habitat_society', ''),
            "ecology": m.get('ecology', ''),
        }

        # Propagate thac0_text if present
        if m.get('thac0_text'):
            entry['thac0_text'] = m['thac0_text']

        # Propagate HD range helper fields
        if m.get('hit_dice_min') is not None:
            entry['hit_dice_min'] = m['hit_dice_min']
            entry['hit_dice_max'] = m['hit_dice_max']

        existing_ids.add(monster_id)
        accepted.append(entry)

    # Deduplicate by name: keep only the first entry per name
    seen_names: set[str] = set()
    deduped = []
    deduped_dropped = []
    for e in accepted:
        key = e['name'].lower().strip()
        if key in seen_names:
            deduped_dropped.append(e['name'])
        else:
            seen_names.add(key)
            deduped.append(e)
    if deduped_dropped:
        print(f"\nDeduplication dropped {len(deduped_dropped)} duplicate names:")
        for n in deduped_dropped:
            print(f"  - {n}")
    accepted = deduped

    # Summary
    print(f"Accepted: {len(accepted)}")
    print(f"Rejected: {len(rejected)}")
    print()
    print("ACCEPTED:")
    for m in accepted:
        print(f"  {m['name']:<45}  HD={m['hit_dice']:<10} AC={m['armor_class']:3d}  XP={m['xp_value']:6d}")

    print("\nREJECTED breakdown:")
    by_reason = {}
    for name, reason in rejected:
        by_reason.setdefault(reason, []).append(name)
    for reason, names in sorted(by_reason.items()):
        print(f"  {reason}: {len(names)}")
        for n in names[:5]:
            print(f"    - {n[:50]}")
        if len(names) > 5:
            print(f"    ... and {len(names)-5} more")

    if DRY_RUN:
        print("\n[DRY RUN] No changes written.")
        return

    if not accepted:
        print("\nNothing to merge.")
        return

    # Merge: add new monsters and re-sort by name
    all_monsters = existing + accepted
    all_monsters.sort(key=lambda m: m['name'].lower())

    existing_data['monsters'] = all_monsters
    with open(MONSTERS_PATH, 'w', encoding='utf-8') as f:
        json.dump(existing_data, f, indent=2, ensure_ascii=False)

    print(f"\nMerged {len(accepted)} new monsters into monsters.json")
    print(f"Total monsters now: {len(all_monsters)}")


if __name__ == '__main__':
    main()
