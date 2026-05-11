from pathlib import Path

from dmcortex.rules_numeric_extract import extract_numeric_rules_from_books


def test_extract_numeric_rules_from_books(tmp_path: Path) -> None:
    books = tmp_path / "BOOKS"
    books.mkdir()
    sample = books / "DWARFBK.RTF"
    sample.write_text(
        "Hill Dwarf Ability Scores\\par"
        "Strength\\tab 8\\tab 18\\par"
        "Dexterity\\tab 3\\tab 17\\par"
        "Constitution\\tab 11\\tab 18\\par"
        "Languages:\\par"
        "An Animal Master must have a Wisdom of 12 or more.\\par"
        "A Vermin Slayer must have minimum scores of 14 in Strength and Dexterity.\\par"
        "Warrior 4 3 -2 3 3 5 4\\par",
        encoding="cp1252",
    )

    payload = extract_numeric_rules_from_books(books)

    assert payload["races"]["hill_dwarf"]["ability_minimums"]["str"] == 8
    assert payload["kits"]["animal_master"]["ability_minimums"]["wis"] == 12
    assert payload["kits"]["vermin_slayer"]["ability_minimums"]["str"] == 14
    assert payload["proficiencies"]["warrior"]["weapon_initial"] == 4
