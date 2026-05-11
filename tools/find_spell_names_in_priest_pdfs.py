from __future__ import annotations

from pathlib import Path
from pypdf import PdfReader

PDFS = [
    Path(r"C:/Users/kelava/Documents/Projects/Dungeon Master Cortex/Assets/Spells/Priest Spells/Priest Spell Compendium Volume 1.pdf"),
    Path(r"C:/Users/kelava/Documents/Projects/Dungeon Master Cortex/Assets/Spells/Priest Spells/Priest Spell Compendium Volume 2.pdf"),
    Path(r"C:/Users/kelava/Documents/Projects/Dungeon Master Cortex/Assets/Spells/Priest Spells/Priest Spell Compendium Volume 3.pdf"),
]

TARGETS = [
    # PRI001069 clues
    "bronzewood or oaken cudgel",
    "nondamaging touch",
    # PRI001065 clues
    "Azrai",
    "Belinik",
]


def clip(text: str, center: int, span: int = 500) -> str:
    lo = max(0, center - span)
    hi = min(len(text), center + span)
    return text[lo:hi]


def main() -> int:
    for pdf_path in PDFS:
        if not pdf_path.exists():
            print(f"MISSING: {pdf_path}")
            continue

        print(f"\n=== Scanning: {pdf_path.name} ===")
        reader = PdfReader(str(pdf_path))
        hit_count = 0

        for page_index, page in enumerate(reader.pages, start=1):
            try:
                text = page.extract_text() or ""
            except Exception:
                continue

            text_l = text.lower()
            for needle in TARGETS:
                pos = text_l.find(needle.lower())
                if pos >= 0:
                    hit_count += 1
                    print(f"\n[HIT] page {page_index} | needle: {needle}")
                    print(clip(text, pos).replace("\n", " "))

        print(f"\nTotal hits in {pdf_path.name}: {hit_count}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
