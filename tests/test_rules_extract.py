from dmcortex.rules_extract import extract_entities_from_index


def test_extract_entities_from_index_classifies_topics() -> None:
    payload = {
        "schema": "dmcortex.assets.index.v1",
        "webhelp_files": [
            {
                "path": "Assets/Core Rules/WEBHELP/CBD/DD00001.HTM",
                "title": "Complete Book of Dwarves",
                "topics": [
                    {"href": "A.htm", "text": "Chapter Ten: Character Creation and Kits"},
                    {"href": "B.htm", "text": "Hill Dwarves"},
                    {"href": "C.htm", "text": "Animal Master"},
                    {"href": "D.htm", "text": "Nonweapon Proficiencies"},
                    {"href": "E.htm", "text": "Fighter"},
                ],
            }
        ],
    }

    extracted = extract_entities_from_index(payload)

    assert extracted["counts"]["races"] >= 1
    assert extracted["counts"]["kits"] >= 1
    assert extracted["counts"]["proficiencies"] >= 1
    assert extracted["counts"]["classes"] >= 1
