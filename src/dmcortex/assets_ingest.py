from __future__ import annotations

import json
import re
from dataclasses import asdict, dataclass
from html.parser import HTMLParser
from pathlib import Path
from typing import Iterable, List


@dataclass(slots=True)
class CntIndexEntry:
    label: str
    target: str


@dataclass(slots=True)
class CntFileSummary:
    path: str
    base: str | None
    title: str | None
    indexes: list[CntIndexEntry]
    includes: list[str]


@dataclass(slots=True)
class HtmlTopic:
    href: str
    text: str


@dataclass(slots=True)
class WebHelpSummary:
    path: str
    title: str | None
    headings: list[str]
    topics: list[HtmlTopic]


class _AnchorParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__()
        self.in_title = False
        self._captured_title: list[str] = []
        self._active_href: str | None = None
        self._anchor_buffer: list[str] = []
        self.topics: list[HtmlTopic] = []

    @property
    def title(self) -> str | None:
        title = _clean_text("".join(self._captured_title))
        return title or None

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag.lower() == "title":
            self.in_title = True
            return
        if tag.lower() != "a":
            return

        href = None
        for key, value in attrs:
            if key.lower() == "href":
                href = value
                break

        self._active_href = href
        self._anchor_buffer = []

    def handle_endtag(self, tag: str) -> None:
        if tag.lower() == "title":
            self.in_title = False
            return
        if tag.lower() != "a":
            return

        if self._active_href:
            text = _clean_text("".join(self._anchor_buffer))
            if text:
                self.topics.append(HtmlTopic(href=self._active_href, text=text))

        self._active_href = None
        self._anchor_buffer = []

    def handle_data(self, data: str) -> None:
        if self.in_title:
            self._captured_title.append(data)
        if self._active_href is not None:
            self._anchor_buffer.append(data)


def _clean_text(value: str) -> str:
    compact = re.sub(r"\s+", " ", value)
    return compact.strip()


def parse_cnt_file(file_path: Path) -> CntFileSummary:
    base: str | None = None
    title: str | None = None
    indexes: list[CntIndexEntry] = []
    includes: list[str] = []

    with file_path.open("r", encoding="cp1252", errors="ignore") as handle:
        for raw_line in handle:
            line = raw_line.strip()
            if line.startswith(":Base "):
                base = line[6:].strip()
                continue
            if line.startswith(":Title "):
                title = line[7:].strip()
                continue
            if line.startswith(":Index "):
                index_body = line[7:].strip()
                if "=" in index_body:
                    label, target = index_body.split("=", maxsplit=1)
                    indexes.append(CntIndexEntry(label=label.strip(), target=target.strip()))
                continue
            if line.startswith(":include "):
                includes.append(line[9:].strip())

    return CntFileSummary(
        path=str(file_path),
        base=base,
        title=title,
        indexes=indexes,
        includes=includes,
    )


def _iter_headings(html_text: str) -> Iterable[str]:
    # These legacy files rely on styled FONT tags; this lightweight extraction
    # keeps chapter/topic labels without requiring third-party parsers.
    for match in re.finditer(r"<B>([^<>]{3,120})</B>", html_text, flags=re.IGNORECASE):
        text = _clean_text(match.group(1))
        if text:
            yield text


def parse_webhelp_html(file_path: Path) -> WebHelpSummary:
    parser = _AnchorParser()

    with file_path.open("r", encoding="cp1252", errors="ignore") as handle:
        payload = handle.read()

    parser.feed(payload)
    headings = list(dict.fromkeys(_iter_headings(payload)))

    return WebHelpSummary(
        path=str(file_path),
        title=parser.title,
        headings=headings[:40],
        topics=parser.topics[:200],
    )


def build_assets_index(assets_root: Path) -> dict[str, object]:
    core_rules = assets_root / "Core Rules"
    help_dir = core_rules / "HELP"
    webhelp_dir = core_rules / "WEBHELP"

    cnt_summaries = []
    if help_dir.exists():
        for cnt_path in sorted(help_dir.glob("*.CNT")):
            cnt_summaries.append(asdict(parse_cnt_file(cnt_path)))

    webhelp_summaries = []
    if webhelp_dir.exists():
        for html_path in sorted(webhelp_dir.rglob("*.HTM")):
            webhelp_summaries.append(asdict(parse_webhelp_html(html_path)))

    return {
        "schema": "dmcortex.assets.index.v1",
        "assets_root": str(assets_root),
        "core_rules_path": str(core_rules),
        "cnt_files": cnt_summaries,
        "webhelp_files": webhelp_summaries,
        "counts": {
            "cnt_files": len(cnt_summaries),
            "webhelp_files": len(webhelp_summaries),
        },
    }


def write_assets_index(assets_root: Path, output_file: Path) -> Path:
    payload = build_assets_index(assets_root)
    output_file.parent.mkdir(parents=True, exist_ok=True)

    with output_file.open("w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2)

    return output_file
