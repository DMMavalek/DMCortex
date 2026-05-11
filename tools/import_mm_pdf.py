"""
Import monster stat blocks from the AD&D 2e Monstrous Manual PDF.

Extraction strategy:
- pypdf text extraction (sequential, left-to-right per page)
- Regex-based stat block field parsing
- Known OCR artifact normalization
- THAC0 computed from HD using 2e table when not found
- Narrative sections (Combat, Habitat/Society, Ecology) extracted from body text
- Multi-species pages flagged and skipped (already handled in existing dataset)

Output:
  data/import/mm_pdf_import.json       — draft monster entries for review
  data/import/mm_pdf_skipped.csv       — pages skipped (multi-species or no stats)
  data/import/mm_pdf_import_report.md  — summary report
"""

import json
import re
import sys
import os
import csv
from pathlib import Path

import pypdf

PDF_PATH = r"C:\Users\kelava\Documents\Projects\Dungeon Master Cortex\Assets\Monsters\Monstrous Manual.pdf"
WORKSPACE = Path(__file__).parent.parent
DATA_DIR = WORKSPACE / "data"
IMPORT_DIR = DATA_DIR / "import"
IMPORT_DIR.mkdir(parents=True, exist_ok=True)

# ────────────────────────────────────────────────────────────────────────────
# OCR normalization for field names
# ────────────────────────────────────────────────────────────────────────────
FIELD_NORM = {
    # CLIMATE/TERRAIN variants
    'CLIMATUTERRAIN': 'CLIMATE/TERRAIN',
    'CLIMATVTERRAIN': 'CLIMATE/TERRAIN',
    'CLIMATETERRAIN': 'CLIMATE/TERRAIN',
    'CLIMATE/TERRAIN': 'CLIMATE/TERRAIN',
    'CLIMATEKERRAIN': 'CLIMATE/TERRAIN',
    # FREQUENCY variants
    'FREOUENCY': 'FREQUENCY',
    'FREQENCY': 'FREQUENCY',
    'FREQUENCY': 'FREQUENCY',
    # ORGANIZATION variants
    "OR~ANUATION": 'ORGANIZATION',
    "OR~ANUATRION": 'ORGANIZATION',
    "ORGANlZATION": 'ORGANIZATION',
    "ORGANZATION": 'ORGANIZATION',
    "ORGANIZATION": 'ORGANIZATION',
    # ACTIVITY CYCLE
    'ACTIVIY CYCLE': 'ACTIVITY CYCLE',
    'ACTIVVY CYCLE': 'ACTIVITY CYCLE',
    'ACTNITY CYCLE': 'ACTIVITY CYCLE',
    'ACTIVITY CYCLE': 'ACTIVITY CYCLE',
    # INTELLIGENCE
    'INTELLIGENCE': 'INTELLIGENCE',
    # TREASURE
    'TREASURE': 'TREASURE',
    # ALIGNMENT
    'ALIGNMENT': 'ALIGNMENT',
    # NO. APPEARING
    'NO. APPEARING': 'NO. APPEARING',
    'NO APPEARING': 'NO. APPEARING',
    # ARMOR CLASS
    'ARMOR CLASS': 'ARMOR CLASS',
    # MOVEMENT
    'MOVEMENT': 'MOVEMENT',
    # HIT DICE
    'HIT DICE': 'HIT DICE',
    # THAC0
    'THAC0': 'THAC0',
    'THACO': 'THAC0',
    'THAC': 'THAC0',
    # NO. OF ATTACKS
    'NO. OF ATTACKS': 'NO. OF ATTACKS',
    'NO OF ATTACKS': 'NO. OF ATTACKS',
    # DAMAGE/ATTACK
    'DAMAGE/ATTACK': 'DAMAGE/ATTACK',
    'DAMAGE/AUACK': 'DAMAGE/ATTACK',
    # SPECIAL ATTACKS
    'SPECIAL ATTACKS': 'SPECIAL ATTACKS',
    'SPEClAL ATTACKS': 'SPECIAL ATTACKS',
    # SPECIAL DEFENSES
    'SPECIAL DEFENSES': 'SPECIAL DEFENSES',
    'SPECIAL DWENSES': 'SPECIAL DEFENSES',
    'SPECIAL DEFENSES': 'SPECIAL DEFENSES',
    'SPEClAL DEFENSES': 'SPECIAL DEFENSES',
    # MAGIC RESISTANCE
    'MAGIC RESISTANCE': 'MAGIC RESISTANCE',
    'MAGIC RES': 'MAGIC RESISTANCE',
    # SIZE
    'SIZE': 'SIZE',
    'SUE': 'SIZE',
    'SlZE': 'SIZE',
    # MORALE
    'MORALE': 'MORALE',
    # XP VALUE
    'XP VALUE': 'XP VALUE',
    'XPVALUE': 'XP VALUE',
}

# Ordered list of canonical stat field names (the order they appear in the book)
STAT_FIELDS_ORDERED = [
    'CLIMATE/TERRAIN', 'FREQUENCY', 'ORGANIZATION', 'ACTIVITY CYCLE', 'DIET',
    'INTELLIGENCE', 'TREASURE', 'ALIGNMENT',
    'NO. APPEARING', 'ARMOR CLASS', 'MOVEMENT', 'HIT DICE',
    'THAC0', 'NO. OF ATTACKS', 'DAMAGE/ATTACK', 'SPECIAL ATTACKS',
    'SPECIAL DEFENSES', 'MAGIC RESISTANCE', 'SIZE', 'MORALE', 'XP VALUE',
]

# Pattern that matches any known (possibly OCR'd) field label
_FIELD_PAT = '|'.join(
    re.escape(k) for k in sorted(FIELD_NORM.keys(), key=len, reverse=True)
)
FIELD_LABEL_RE = re.compile(
    r'(?:^|\n)\s*(' + _FIELD_PAT + r')\s*:',
    re.IGNORECASE | re.MULTILINE
)

# ────────────────────────────────────────────────────────────────────────────
# 2e THAC0 table (HD → THAC0)
# ────────────────────────────────────────────────────────────────────────────
THAC0_TABLE = {
    0: 20, 1: 19, 2: 19, 3: 17, 4: 17,
    5: 15, 6: 15, 7: 13, 8: 13, 9: 11,
    10: 11, 11: 9, 12: 9, 13: 7, 14: 7,
    15: 5, 16: 5, 17: 3, 18: 3, 19: 1, 20: 1,
}

def thac0_from_hd(hd_str: str) -> int:
    """Compute THAC0 from Hit Dice string using 2e lookup table."""
    s = (hd_str or '').strip().lower()
    s = s.replace('hp', '').strip()
    if s in ('varies', 'variable', 'n/a', 'nil', ''):
        return 20
    if s in ('1/2', '½', '0.5'):
        return 20
    # Handle "Xd8" format (take X as HD)
    m = re.match(r'(\d+)d\d+', s)
    if m:
        hd = int(m.group(1))
        return THAC0_TABLE.get(min(hd, 20), 1)
    # Handle "X+Y" format
    m = re.match(r'(\d+)\s*\+\s*\d+', s)
    if m:
        hd = int(m.group(1))
        return THAC0_TABLE.get(min(hd, 20), 1)
    # Handle "X-Y" range (take average)
    m = re.match(r'(\d+)\s*[-to]+\s*(\d+)', s)
    if m:
        hd = (int(m.group(1)) + int(m.group(2))) // 2
        return THAC0_TABLE.get(min(hd, 20), 1)
    # Plain integer
    try:
        hd = int(float(s))
        return THAC0_TABLE.get(min(hd, 20), 1)
    except ValueError:
        return 20

def normalize_value(raw: str) -> str:
    """Clean up OCR artifacts in field values."""
    if not raw:
        return ''
    s = raw.strip()
    # Fix common OCR mistakes in numeric values
    s = s.replace('5.000', '5,000').replace('2.000', '2,000').replace(
        '1.000', '1,000').replace('10.000', '10,000').replace('3.000', '3,000').replace(
        '4.000', '4,000').replace('6.000', '6,000').replace('7.000', '7,000').replace(
        '8.000', '8,000').replace('9.000', '9,000').replace('15.000', '15,000').replace(
        '20.000', '20,000').replace('25.000', '25,000').replace('50.000', '50,000')
    # Fix 'ld' → '1d' (OCR confuses lowercase L with 1)
    s = re.sub(r'\bl([d])', r'1\1', s)
    # Collapse internal newlines to spaces (multi-line values)
    s = re.sub(r'\s*\n\s*', ' ', s).strip()
    return s

def normalize_xp(xp_str: str) -> int:
    """Parse XP value string to integer."""
    s = normalize_value(xp_str or '')
    s = s.replace(',', '').replace('.', '')
    m = re.search(r'(\d+)', s)
    if m:
        return int(m.group(1))
    return 0

def normalize_ac(ac_str: str) -> int:
    """Parse AC to integer."""
    m = re.search(r'-?\d+', (ac_str or ''))
    if m:
        return int(m.group())
    return 10

def normalize_attacks(attacks_str: str) -> int:
    """Parse No. of Attacks to integer."""
    s = (attacks_str or '').strip()
    m = re.search(r'(\d+)', s)
    if m:
        return int(m.group(1))
    return 1

def make_id(name: str) -> str:
    """Generate mon_<slug> ID from monster name."""
    s = name.lower().strip()
    s = re.sub(r"[',\.]", '', s)
    s = re.sub(r'\s+', '_', s)
    s = re.sub(r'[^a-z0-9_]', '', s)
    s = re.sub(r'_+', '_', s).strip('_')
    return f'mon_{s}'

# ────────────────────────────────────────────────────────────────────────────
# Detect multi-species pages
# ────────────────────────────────────────────────────────────────────────────
# If the stat block has more than 2 repetitions of a key field, it's multi-column
def is_multi_species(text: str) -> bool:
    """Return True if this page has multiple side-by-side stat blocks."""
    # Count how many times ARMOR CLASS appears — multiple means multi-species
    ac_count = len(re.findall(r'ARMOR\s+CLASS', text, re.IGNORECASE))
    if ac_count >= 3:
        return True
    # Also check for multi-column by detecting multiple FREQUENCY/MORALE occurrences
    freq_count = len(re.findall(r'FREOU?E[NQ]CY', text, re.IGNORECASE))
    if freq_count >= 2:
        return True
    return False

# ────────────────────────────────────────────────────────────────────────────
# Extract stat block fields from page text
# ────────────────────────────────────────────────────────────────────────────
def extract_stat_block(text: str) -> dict[str, str]:
    """Parse stat block field labels and their values from page text."""
    fields: dict[str, str] = {}
    
    # Find all field label positions
    matches = list(FIELD_LABEL_RE.finditer(text))
    if not matches:
        return fields
    
    for i, match in enumerate(matches):
        raw_label = match.group(1).strip().upper()
        # Normalize the label
        canonical = FIELD_NORM.get(raw_label, raw_label)
        # Value is text from end of match to start of next match
        val_start = match.end()
        val_end = matches[i + 1].start() if i + 1 < len(matches) else val_start + 200
        raw_val = text[val_start:val_end]
        # Clean up
        val = normalize_value(raw_val)
        # Truncate very long values (stat block values should be short)
        if len(val) > 120:
            val = val[:120].rsplit(' ', 1)[0] + '…'
        fields[canonical] = val
    
    return fields

# ────────────────────────────────────────────────────────────────────────────
# Extract narrative sections from page text
# ────────────────────────────────────────────────────────────────────────────
SECTION_RE = re.compile(
    r'\b(Combat|Habitat\s*/\s*Society|Habitat/Society|HabitatSociety|Ecology)\s*:?\s+',
    re.IGNORECASE
)

def extract_narrative(text: str) -> dict[str, str]:
    """Extract Combat, Habitat/Society, Ecology sections from narrative text.
    
    Pypdf mixes the two narrative columns but section headers are still detectable.
    """
    sections: dict[str, str] = {'Combat': '', 'HabitatSociety': '', 'Ecology': ''}
    
    matches = list(SECTION_RE.finditer(text))
    if not matches:
        return sections
    
    for i, match in enumerate(matches):
        raw_sec = match.group(1).strip().lower()
        if 'combat' in raw_sec:
            key = 'Combat'
        elif 'habitat' in raw_sec or 'society' in raw_sec:
            key = 'HabitatSociety'
        elif 'ecology' in raw_sec:
            key = 'Ecology'
        else:
            continue
        
        sec_start = match.end()
        sec_end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        raw = text[sec_start:sec_end].strip()
        # Collapse internal newlines to spaces
        cleaned = re.sub(r'\s*\n\s*', ' ', raw).strip()
        # Limit to 1500 chars per section
        if len(cleaned) > 1500:
            cleaned = cleaned[:1500].rsplit(' ', 1)[0] + '…'
        sections[key] = cleaned
    
    return sections

# ────────────────────────────────────────────────────────────────────────────
# Monster type detection from context text
# ────────────────────────────────────────────────────────────────────────────
def detect_monster_type(name: str, terrain: str, intelligence: str, text: str) -> str:
    """Guess monster type from available info."""
    name_l = name.lower()
    text_l = (text or '').lower()
    if any(w in name_l for w in ['dragon', 'dragonet', 'wyvern']):
        return 'Dragon'
    if any(w in name_l for w in ['undead', 'zombie', 'skeleton', 'vampire', 'ghost', 'ghoul', 'lich', 'wight', 'wraith', 'specter', 'banshee', 'mummy']):
        return 'Undead'
    if any(w in name_l for w in ['demon', 'devil', 'tanar', 'baatezu', 'yugoloth', 'tanar\'ri']):
        return 'Fiend'
    if any(w in name_l for w in ['golem', 'construct', 'animated']):
        return 'Construct'
    if any(w in name_l for w in ['elemental']):
        return 'Elemental'
    # Default
    return 'Standard'

# ────────────────────────────────────────────────────────────────────────────
# Main extraction loop
# ────────────────────────────────────────────────────────────────────────────
def extract_all():
    reader = pypdf.PdfReader(PDF_PATH)
    total_pages = len(reader.pages)
    print(f"PDF: {total_pages} pages")
    
    # Load existing monsters.json to avoid duplicates
    monsters_path = DATA_DIR / "rulesets" / "monsters.json"
    with open(monsters_path, encoding='utf-8') as f:
        existing_data = json.load(f)
    existing_ids = {m['id'] for m in existing_data.get('monsters', [])}
    existing_names = {m['name'].lower().strip() for m in existing_data.get('monsters', [])}
    print(f"Existing monsters: {len(existing_ids)}")
    
    STAT_KEYWORDS = ["CLIMATE", "FREQUENCY", "ARMOR CLASS", "HIT DICE", "THAC0", "MORALE", "XP VALUE"]
    
    imported = []
    skipped = []
    
    for page_idx in range(total_pages):
        page = reader.pages[page_idx]
        text = page.extract_text() or ''
        
        # Skip pages with too few stat block keywords
        hits = sum(1 for kw in STAT_KEYWORDS if kw in text.upper())
        if hits < 3:
            continue
        
        # Get monster name: first non-empty line
        lines = [l.strip() for l in text.split('\n') if l.strip()]
        if not lines:
            continue
        name = lines[0]
        
        # Skip obvious non-monster pages (intro, appendix, etc.)
        name_lower = name.lower()
        skip_prefixes = ('flying', 'table', 'appendix', 'introduction', 'page', 'chapter',
                        'the following', 'all monsters', 'common monster')
        if any(name_lower.startswith(p) for p in skip_prefixes) or len(name) > 60:
            skipped.append({'page': page_idx + 1, 'reason': 'non-monster page', 'name': name[:50]})
            continue
        
        # Skip multi-species pages
        if is_multi_species(text):
            skipped.append({'page': page_idx + 1, 'reason': 'multi-species', 'name': name[:50]})
            continue
        
        # Skip if already in dataset (by name match)
        if name.lower().strip() in existing_names:
            skipped.append({'page': page_idx + 1, 'reason': 'already_in_dataset', 'name': name[:50]})
            continue
        
        # Extract stat block
        fields = extract_stat_block(text)
        if not fields:
            skipped.append({'page': page_idx + 1, 'reason': 'no_stat_block_parsed', 'name': name[:50]})
            continue
        
        # Must have at least AC or HD to be useful
        if 'ARMOR CLASS' not in fields and 'HIT DICE' not in fields:
            skipped.append({'page': page_idx + 1, 'reason': 'missing_ac_and_hd', 'name': name[:50]})
            continue
        
        # Extract narrative sections
        narrative = extract_narrative(text)
        
        # Build monster entry
        hd_raw = fields.get('HIT DICE', '')
        ac_raw = fields.get('ARMOR CLASS', '10')
        thac0_raw = fields.get('THAC0', '')
        
        # Compute THAC0 if not found
        thac0_computed = thac0_from_hd(hd_raw)
        thac0_text_flag = ''
        if thac0_raw.strip().lower() in ('varies', 'variable', 'n/a', 'nil'):
            thac0_text_flag = thac0_raw.strip()
            thac0_final = thac0_computed
        elif thac0_raw:
            try:
                thac0_final = int(thac0_raw.split('/')[0].strip().split()[0])
            except ValueError:
                thac0_final = thac0_computed
                thac0_text_flag = thac0_raw.strip()
        else:
            thac0_final = thac0_computed
            thac0_text_flag = ''  # computed, no note needed
        
        monster_id = make_id(name)
        # Avoid ID collisions
        base_id = monster_id
        suffix = 2
        while monster_id in existing_ids:
            monster_id = f"{base_id}_{suffix}"
            suffix += 1
        
        # Build the entry
        entry = {
            "id": monster_id,
            "name": name,
            "monster_type": detect_monster_type(name, fields.get('CLIMATE/TERRAIN', ''),
                                                  fields.get('INTELLIGENCE', ''), text),
            "source": "Monstrous Manual",
            "hit_dice": normalize_value(hd_raw),
            "armor_class": normalize_ac(ac_raw),
            "movement": normalize_value(fields.get('MOVEMENT', '')),
            "thac0": thac0_final,
            "attacks": normalize_attacks(fields.get('NO. OF ATTACKS', '')),
            "damage": normalize_value(fields.get('DAMAGE/ATTACK', '')),
            "special_attacks": normalize_value(fields.get('SPECIAL ATTACKS', 'Nil')),
            "special_defenses": normalize_value(fields.get('SPECIAL DEFENSES', 'Nil')),
            "magic_resistance": normalize_value(fields.get('MAGIC RESISTANCE', 'Nil')),
            "size": normalize_value(fields.get('SIZE', 'M')).split()[0] if fields.get('SIZE') else 'M',
            "morale": normalize_value(fields.get('MORALE', '')),
            "xp_value": normalize_xp(fields.get('XP VALUE', '0')),
            "number_appearing": normalize_value(fields.get('NO. APPEARING', '')),
            "frequency": normalize_value(fields.get('FREQUENCY', '')),
            "intelligence": normalize_value(fields.get('INTELLIGENCE', '')),
            "alignment": normalize_value(fields.get('ALIGNMENT', '')),
            "treasure_type": normalize_value(fields.get('TREASURE', 'Nil')),
            "description": normalize_value(fields.get('CLIMATE/TERRAIN', '')),
            "combat": narrative.get('Combat', ''),
            "habitat_society": narrative.get('HabitatSociety', ''),
            "ecology": narrative.get('Ecology', ''),
            # Meta fields for review
            "_pdf_page": page_idx + 1,
            "_thac0_source": "computed_from_hd" if not thac0_raw.strip() else "extracted",
        }
        
        if thac0_text_flag:
            entry['thac0_text'] = thac0_text_flag
        
        # Also add computed helper fields
        hd_min, hd_max = None, None
        s = normalize_value(hd_raw).lower().strip().replace('hp', '').strip()
        if s and s not in ('varies', 'variable', 'n/a', 'nil'):
            if s in ('1/2', '½'):
                hd_min, hd_max = 0, 1
            else:
                hm = re.match(r'^(\d+)\s*\+\s*(\d+)$', s)
                if hm:
                    b = int(hm.group(1))
                    hd_min, hd_max = b, b + int(hm.group(2))
                else:
                    hm = re.match(r'^(\d+)\s*[-to]+\s*(\d+)$', s)
                    if hm:
                        a, b = int(hm.group(1)), int(hm.group(2))
                        hd_min, hd_max = min(a, b), max(a, b)
                    elif re.match(r'^\d+$', s):
                        hd_min = hd_max = int(s)
        if hd_min is not None:
            entry['hit_dice_min'] = hd_min
            entry['hit_dice_max'] = hd_max
        
        imported.append(entry)
        print(f"  p{page_idx+1:3d}  {name[:40]:<40}  HD={entry['hit_dice']:<6} AC={entry['armor_class']:3d}  THAC0={entry['thac0']:2d}")
    
    # ── Write outputs ────────────────────────────────────────────────────────
    out_path = IMPORT_DIR / "mm_pdf_import.json"
    with open(out_path, 'w', encoding='utf-8') as f:
        json.dump({"monsters": imported}, f, indent=2, ensure_ascii=False)
    print(f"\nWrote {len(imported)} monsters → {out_path}")
    
    skipped_path = IMPORT_DIR / "mm_pdf_skipped.csv"
    with open(skipped_path, 'w', newline='', encoding='utf-8') as f:
        w = csv.DictWriter(f, fieldnames=['page', 'reason', 'name'])
        w.writeheader()
        w.writerows(skipped)
    print(f"Wrote {len(skipped)} skipped pages → {skipped_path}")
    
    # ── Write report ─────────────────────────────────────────────────────────
    report_path = IMPORT_DIR / "mm_pdf_import_report.md"
    by_reason = {}
    for s in skipped:
        by_reason.setdefault(s['reason'], []).append(s)
    
    with open(report_path, 'w', encoding='utf-8') as f:
        f.write(f"# MM PDF Import Report\n\n")
        f.write(f"- **PDF pages:** {total_pages}\n")
        f.write(f"- **Existing monsters:** {len(existing_ids)}\n")
        f.write(f"- **New monsters extracted:** {len(imported)}\n")
        f.write(f"- **Pages skipped:** {len(skipped)}\n\n")
        f.write("## Skipped Breakdown\n\n")
        for reason, items in sorted(by_reason.items()):
            f.write(f"### {reason} ({len(items)})\n")
            for item in items[:20]:
                f.write(f"- p{item['page']}: {item['name']}\n")
            if len(items) > 20:
                f.write(f"- _...and {len(items)-20} more_\n")
            f.write("\n")
        f.write("## Imported Monsters (first 50)\n\n")
        f.write("| Name | HD | AC | THAC0 src | XP |\n")
        f.write("|------|----|----|-----------|----|\n")
        for m in imported[:50]:
            src = m.get('_thac0_source', '')
            f.write(f"| {m['name']} | {m['hit_dice']} | {m['armor_class']} | {src} | {m['xp_value']} |\n")
    print(f"Report → {report_path}")
    
    return imported, skipped

if __name__ == '__main__':
    imported, skipped = extract_all()
    print(f"\nDone. {len(imported)} new monsters ready for review in data/import/mm_pdf_import.json")
    print("Run tools/merge_mm_import.py to merge into monsters.json after review.")
