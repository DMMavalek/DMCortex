from pathlib import Path

from dmcortex.assets_ingest import parse_cnt_file, parse_webhelp_html


def test_parse_cnt_file_extracts_indexes_and_includes(tmp_path: Path) -> None:
    sample = tmp_path / "sample.CNT"
    sample.write_text(
        ":Base ADandD.hlp\n"
        ":Title Advanced Dungeons and Dragons\n"
        ":Index Player's Handbook =players.hlp\n"
        ":include Players.CNT\n",
        encoding="cp1252",
    )

    parsed = parse_cnt_file(sample)

    assert parsed.base == "ADandD.hlp"
    assert parsed.title == "Advanced Dungeons and Dragons"
    assert parsed.indexes[0].label == "Player's Handbook"
    assert parsed.indexes[0].target == "players.hlp"
    assert parsed.includes == ["Players.CNT"]


def test_parse_webhelp_html_extracts_title_links_and_headings(tmp_path: Path) -> None:
    sample = tmp_path / "sample.HTM"
    sample.write_text(
        "<html><head><title>Book Name</title></head><body>"
        "<b>Table of Contents</b>"
        "<a href='DD00001.htm'>Chapter One</a>"
        "</body></html>",
        encoding="cp1252",
    )

    parsed = parse_webhelp_html(sample)

    assert parsed.title == "Book Name"
    assert parsed.topics[0].href == "DD00001.htm"
    assert parsed.topics[0].text == "Chapter One"
    assert "Table of Contents" in parsed.headings
