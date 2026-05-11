from __future__ import annotations

from pathlib import Path
from pypdf import PdfReader

PDFS = [
    Path(r"C:/Users/kelava/Documents/Projects/Dungeon Master Cortex/Assets/Spells/Priest Spells/Priest Spell Compendium Volume 1.pdf"),
    Path(r"C:/Users/kelava/Documents/Projects/Dungeon Master Cortex/Assets/Spells/Priest Spells/Priest Spell Compendium Volume 2.pdf"),
    Path(r"C:/Users/kelava/Documents/Projects/Dungeon Master Cortex/Assets/Spells/Priest Spells/Priest Spell Compendium Volume 3.pdf"),
]

# Wider net for PRI001069 clues.
NEEDLES = [
    "bronzewood",
    "oaken cudgel",
    "nondamaging touch",
    "shamans",
    "power to charm",
    "creature that is hit",
]


def clip(text: str, center: int, span: int = 700) -> str:
    lo = max(0, center - span)
    hi = min(len(text), center + span)
    return text[lo:hi]


def main() -> int:
    for pdf_path in PDFS:
        print(f"\n=== {pdf_path.name} ===")
        reader = PdfReader(str(pdf_path))
        hits = 0

        for page_index, page in enumerate(reader.pages, start=1):
            try:
                text = page.extract_text() or ""
            except Exception:
                continue

            low = text.lower()
            for needle in NEEDLES:
                pos = low.find(needle.lower())
                if pos >= 0:
                    hits += 1
                    print(f"\n[HIT] page {page_index} needle='{needle}'")
                    print(clip(text, pos).replace("\n", " "))

        print(f"Total hits: {hits}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
