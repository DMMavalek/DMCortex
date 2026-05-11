import csv
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MM_INDEX = ROOT / "Assets" / "Core Rules" / "WEBHELP" / "MM" / "DD03789.HTM"
MONSTERS_JSON = ROOT / "data" / "rulesets" / "monsters.json"
OUT_DIR = ROOT / "data" / "audit"
OUT_DIR.mkdir(parents=True, exist_ok=True)

OUT_CSV = OUT_DIR / "mm_missing_candidates.csv"
OUT_MD = OUT_DIR / "mm_coverage_report.md"


def norm_name(s: str) -> str:
    s = s.lower().strip()
    s = s.replace("\n", " ")
    s = re.sub(r"\([^\)]*\)", "", s)
    s = s.replace("dragon, ", "")
    s = s.replace("cat, ", "cat ")
    s = s.replace("lion, ", "lion ")
    s = s.replace("tiger, ", "tiger ")
    s = s.replace(",", " ")
    s = re.sub(r"\b(the|a|an)\b", " ", s)
    s = re.sub(r"\s+", " ", s).strip()
    return s


def load_mm_names() -> list[str]:
    html = MM_INDEX.read_text(encoding="latin-1", errors="ignore")
    links = re.findall(r'<A HREF="(DD\d+\.htm[^\"]*)"[^>]*>([^<]+)</A>', html, flags=re.I)
    names = sorted({re.sub(r"\s+", " ", n.strip()) for _, n in links if n.strip()})
    return names


def load_json_names() -> list[str]:
    data = json.loads(MONSTERS_JSON.read_text(encoding="utf-8"))
    return [m.get("name", "").strip() for m in data.get("monsters", [])]


def main() -> None:
    mm_names = load_mm_names()
    json_names = load_json_names()

    json_norm = {norm_name(n): n for n in json_names if n}

    missing = []
    for name in mm_names:
        n = norm_name(name)
        if n and n not in json_norm:
            missing.append((name, n))

    missing.sort(key=lambda x: x[0].lower())

    with OUT_CSV.open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["mm_name", "normalized_name"])
        w.writerows(missing)

    cat_keys = ("cat", "lion", "tiger", "jaguar", "leopard", "lynx", "smilodon", "cheetah")
    cat_missing = [row for row in missing if any(k in row[0].lower() for k in cat_keys)]

    md = []
    md.append("# MM Coverage Audit\n")
    md.append(f"- MM index linked names: {len(mm_names)}")
    md.append(f"- Monsters in dataset: {len(json_names)}")
    md.append(f"- Missing candidates (normalized heuristic): {len(missing)}")
    md.append("")
    md.append("## Notes")
    md.append("- MM index includes variants, grouped sub-entries, and appendix headings; this report is a candidate list, not a strict one-to-one deficit count.")
    md.append("- Use this file to drive incremental imports and alias mapping.")
    md.append("")
    md.append("## Cat-Related Missing Candidates")
    if not cat_missing:
        md.append("- None")
    else:
        for mm_name, _ in cat_missing[:100]:
            md.append(f"- {mm_name}")
    md.append("")
    md.append("## Output Files")
    md.append(f"- {OUT_CSV.relative_to(ROOT).as_posix()}")
    md.append(f"- {OUT_MD.relative_to(ROOT).as_posix()}")

    OUT_MD.write_text("\n".join(md) + "\n", encoding="utf-8")

    print(f"Wrote: {OUT_CSV}")
    print(f"Wrote: {OUT_MD}")


if __name__ == "__main__":
    main()
