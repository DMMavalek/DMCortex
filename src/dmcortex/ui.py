"""Dungeon Master Cortex – tkinter UI styled after AD&D 2e Core Rules 2.0.

Single-window application with sliding frame transitions.
All character-generation steps live in one fixed-size window;
navigation uses forward/back sliding animations via tk.place().
"""
from __future__ import annotations

import random
import tkinter as tk
from tkinter import messagebox
from pathlib import Path
from typing import Any, Dict, List, Optional

from dmcortex.character_builder import CharacterBuilder
from dmcortex.combat import CombatTracker
from dmcortex.models import CharacterSheet, Combatant, CampaignEntry
from dmcortex.campaign import CampaignManager
from dmcortex.rules_engine import RulesEngine
from dmcortex.storage import UNASSIGNED_PARTY, load_state, save_state

# ── Window constants ───────────────────────────────────────────────────────────
WIN_W = 960
WIN_H = 680
ANIM_STEPS = 12          # frames in slide animation
ANIM_MS = 12             # ms per frame  → ~144 ms total

# ── Colour palette (Core Rules 2.0 dark-parchment aesthetic) ──────────────────
C_BG        = "#0d0a06"   # near-black background
C_PANEL     = "#1a1208"   # panel / frame background
C_CARD      = "#221808"   # raised card
C_BORDER    = "#7a5a14"   # old-gold border
C_BORDER2   = "#3e2c08"   # inner / secondary border
C_TEXT      = "#c4a468"   # body parchment text
C_TITLE     = "#e8c050"   # bright gold headings
C_DIM       = "#5a4228"   # disabled / placeholder
C_BTN       = "#28190a"   # button face
C_BTN_HOV   = "#3e2a0e"   # button hover
C_BTN_TXT   = "#d0a040"   # button label
C_BTN_ACT   = "#6a4010"   # active / pressed button
C_RED       = "#7a1616"   # danger accent
C_RED_LT    = "#c02828"   # bright danger
C_GREEN     = "#1e4a12"   # success accent
C_INPUT_BG  = "#110c06"   # entry / listbox background
C_SEL       = "#5a3c0e"   # list-selection highlight
C_SEL_TXT   = "#f0d080"   # selected text colour

ABILITIES = ("str", "dex", "con", "int", "wis", "cha")
ABILITY_LABELS = {
    "str": "Strength",
    "dex": "Dexterity",
    "con": "Constitution",
    "int": "Intelligence",
    "wis": "Wisdom",
    "cha": "Charisma",
}

# ── Helpers ────────────────────────────────────────────────────────────────────

def _project_root() -> Path:
    return Path(__file__).resolve().parents[2]


def _load_rules() -> RulesEngine:
    path = _project_root() / "data" / "rulesets" / "core_2e.json"
    if path.exists():
        return RulesEngine.from_file(path)
    # Minimal fallback so the UI still launches without data files
    return RulesEngine({
        "ruleset_id": "core_2e",
        "name": "AD&D 2e Core",
        "standard_array": [15, 14, 13, 12, 10, 8],
        "ability_generation_methods": ["4d6_drop_lowest", "standard_array"],
        "races": {
            "human":  {"name": "Human",  "ability_minimums": {}, "ability_maximums": {}},
            "elf":    {"name": "Elf",    "ability_minimums": {"dex": 7}, "ability_maximums": {"con": 17}},
            "dwarf":  {"name": "Dwarf",  "ability_minimums": {"con": 11}, "ability_maximums": {"cha": 17}},
            "halfling": {"name": "Halfling", "ability_minimums": {"dex": 7, "con": 10}, "ability_maximums": {}},
            "gnome":  {"name": "Gnome",  "ability_minimums": {"int": 6, "con": 8}, "ability_maximums": {}},
            "half-elf": {"name": "Half-Elf", "ability_minimums": {}, "ability_maximums": {}},
        },
        "classes": {
            "fighter": {"name": "Fighter", "ability_minimums": {"str": 9},  "allowed_races": ["human", "elf", "dwarf", "halfling", "gnome", "half-elf"]},
            "wizard":  {"name": "Wizard",  "ability_minimums": {"int": 9},  "allowed_races": ["human", "elf", "gnome", "half-elf"]},
            "cleric":  {"name": "Cleric",  "ability_minimums": {"wis": 9},  "allowed_races": ["human", "elf", "dwarf", "halfling", "gnome", "half-elf"]},
            "thief":   {"name": "Thief",   "ability_minimums": {"dex": 9},  "allowed_races": ["human", "elf", "dwarf", "halfling", "gnome", "half-elf"]},
            "ranger":  {"name": "Ranger",  "ability_minimums": {"str": 13, "dex": 13, "con": 14, "wis": 14}, "allowed_races": ["human", "elf", "half-elf"]},
            "paladin": {"name": "Paladin", "ability_minimums": {"str": 12, "con": 9, "wis": 13, "cha": 17}, "allowed_races": ["human"]},
        },
    })


def _characters_store_path() -> Path:
    return _project_root() / "data" / "state" / "characters.json"


# ── Styled widget factories ────────────────────────────────────────────────────

def make_frame(parent: tk.Widget, **kw) -> tk.Frame:
    kw.setdefault("bg", C_PANEL)
    return tk.Frame(parent, **kw)


def make_label(parent: tk.Widget, text: str, size: int = 11,
               bold: bool = False, color: str = C_TEXT, **kw) -> tk.Label:
    weight = "bold" if bold else "normal"
    return tk.Label(
        parent, text=text,
        bg=kw.pop("bg", C_PANEL), fg=color,
        font=("Georgia", size, weight), **kw
    )


def make_title(parent: tk.Widget, text: str, size: int = 18) -> tk.Label:
    return make_label(parent, text, size=size, bold=True, color=C_TITLE)


def make_button(parent: tk.Widget, text: str, command=None,
                width: int = 18, danger: bool = False) -> tk.Button:
    face = C_RED if danger else C_BTN
    hover = C_RED_LT if danger else C_BTN_HOV
    btn = tk.Button(
        parent, text=text, command=command,
        bg=face, fg=C_BTN_TXT,
        activebackground=C_BTN_ACT, activeforeground=C_SEL_TXT,
        font=("Georgia", 11, "bold"),
        relief="flat", bd=0,
        width=width, pady=6,
        cursor="hand2",
    )
    btn.bind("<Enter>", lambda _e: btn.configure(bg=hover))
    btn.bind("<Leave>", lambda _e: btn.configure(bg=face))
    return btn


def make_entry(parent: tk.Widget, textvariable: tk.StringVar,
               width: int = 30) -> tk.Entry:
    return tk.Entry(
        parent, textvariable=textvariable,
        bg=C_INPUT_BG, fg=C_TEXT, insertbackground=C_TEXT,
        font=("Georgia", 12),
        relief="flat", bd=4,
        width=width,
        selectbackground=C_SEL, selectforeground=C_SEL_TXT,
    )


def make_listbox(parent: tk.Widget, height: int = 8,
                 width: int = 28) -> tk.Listbox:
    lb = tk.Listbox(
        parent,
        bg=C_INPUT_BG, fg=C_TEXT,
        selectbackground=C_SEL, selectforeground=C_SEL_TXT,
        font=("Georgia", 11),
        relief="flat", bd=4,
        height=height, width=width,
        activestyle="none",
        highlightthickness=0,
    )
    return lb


def make_scrollbar(parent: tk.Widget, widget: tk.Widget,
                   orient: str = "vertical") -> tk.Scrollbar:
    sb = tk.Scrollbar(parent, orient=orient, bg=C_BORDER2,
                      troughcolor=C_INPUT_BG, width=10)
    if orient == "vertical":
        widget.configure(yscrollcommand=sb.set)
        sb.configure(command=widget.yview)
    else:
        widget.configure(xscrollcommand=sb.set)
        sb.configure(command=widget.xview)
    return sb


def make_separator(parent: tk.Widget, width: int = 600) -> tk.Canvas:
    c = tk.Canvas(parent, height=2, width=width,
                  bg=C_PANEL, highlightthickness=0)
    c.create_line(0, 1, width, 1, fill=C_BORDER, width=1)
    return c


def make_ornament_bar(parent: tk.Widget, width: int = 800) -> tk.Canvas:
    """Decorative horizontal rule with diamond ornaments."""
    h = 14
    c = tk.Canvas(parent, height=h, width=width,
                  bg=C_PANEL, highlightthickness=0)
    mid = h // 2
    c.create_line(0, mid, width, mid, fill=C_BORDER2, width=1)
    for x in [width // 4, width // 2, 3 * width // 4]:
        c.create_polygon(x, mid - 5, x + 5, mid, x, mid + 5, x - 5, mid,
                         fill=C_BORDER, outline=C_TITLE)
    return c


# ── Gold-framed container ──────────────────────────────────────────────────────

class GoldFrame(tk.Frame):
    """A tk.Frame with a painted gold border on all sides."""

    def __init__(self, parent: tk.Widget, **kw):
        super().__init__(parent, bg=C_BORDER, padx=2, pady=2)
        self._inner = tk.Frame(self, bg=kw.pop("bg", C_CARD), **kw)
        self._inner.pack(fill="both", expand=True)

    @property
    def inner(self) -> tk.Frame:
        return self._inner


# ── Top banner (always visible) ────────────────────────────────────────────────

class TopBanner(tk.Frame):
    """Title banner shown at the top of every screen."""

    def __init__(self, parent: tk.Widget, app: "App"):
        super().__init__(parent, bg=C_BG, height=72)
        self.pack_propagate(False)
        self._app = app

        # Decorative left diamond strip
        strip = tk.Canvas(self, width=12, bg=C_BG, highlightthickness=0)
        strip.pack(side="left", fill="y")
        strip.bind("<Configure>", self._draw_strip)
        self._strip = strip

        # Title group
        title_frame = tk.Frame(self, bg=C_BG)
        title_frame.pack(side="left", fill="both", expand=True, padx=20)

        tk.Label(
            title_frame,
            text="DUNGEON MASTER CORTEX",
            bg=C_BG, fg=C_TITLE,
            font=("Georgia", 20, "bold"),
            anchor="w",
        ).pack(side="top", anchor="w")

        self._subtitle = tk.Label(
            title_frame, text="",
            bg=C_BG, fg=C_DIM,
            font=("Georgia", 10),
            anchor="w",
        )
        self._subtitle.pack(side="top", anchor="w")

        # Thin gold rule at bottom
        rule = tk.Canvas(self, height=3, bg=C_BORDER, highlightthickness=0)
        rule.place(x=0, rely=1.0, anchor="sw", relwidth=1.0)

    def set_subtitle(self, text: str) -> None:
        self._subtitle.configure(text=text)

    def _draw_strip(self, event=None) -> None:
        h = self._strip.winfo_height() or 72
        self._strip.delete("all")
        for y in range(6, h, 14):
            self._strip.create_polygon(
                6, y - 5, 11, y, 6, y + 5, 1, y,
                fill=C_BORDER, outline=C_BORDER2,
            )


# ── Bottom nav bar (used by char-gen wizard) ───────────────────────────────────

class BottomNav(tk.Frame):
    """Back / Next / Finish navigation bar for the character generator."""

    def __init__(self, parent: tk.Widget, app: "App"):
        super().__init__(parent, bg=C_BG, height=60)
        self.pack_propagate(False)
        self._app = app

        rule = tk.Canvas(self, height=3, bg=C_BORDER, highlightthickness=0)
        rule.place(x=0, y=0, relwidth=1.0)

        self.btn_back = make_button(self, "◀  BACK", width=14)
        self.btn_back.pack(side="left", padx=24, pady=10)

        self._step_label = tk.Label(
            self, text="",
            bg=C_BG, fg=C_DIM,
            font=("Georgia", 10),
        )
        self._step_label.pack(side="left", expand=True)

        self.btn_next = make_button(self, "NEXT  ▶", width=14)
        self.btn_next.pack(side="right", padx=24, pady=10)

    def set_step(self, current: int, total: int, label: str = "") -> None:
        self._step_label.configure(
            text=f"Step {current} of {total}  {label}"
        )

    def hide(self) -> None:
        self.pack_forget()

    def show(self) -> None:
        self.pack(side="bottom", fill="x")


# ── Slide-transition container ─────────────────────────────────────────────────

class ScreenContainer(tk.Frame):
    """
    Hosts all screens stacked at the same position.
    Transitions slide the old screen out and the new one in.
    """

    def __init__(self, parent: tk.Widget):
        super().__init__(parent, bg=C_BG)
        self._screens: Dict[str, tk.Frame] = {}
        self._current: Optional[str] = None
        self._animating = False

    def register(self, name: str, screen: tk.Frame) -> None:
        self._screens[name] = screen
        screen.place(x=WIN_W, y=0, width=WIN_W, height=WIN_H - 72 - 60)

    def show(self, name: str, direction: int = 1) -> None:
        """Show screen *name*. direction=1 → slide left (forward), -1 → right (back)."""
        if name == self._current or self._animating:
            return
        new_screen = self._screens[name]
        old_name = self._current
        self._current = name
        if old_name is None:
            new_screen.place(x=0, y=0)
            return
        old_screen = self._screens[old_name]
        self._animate(old_screen, new_screen, direction)

    def _animate(self, old: tk.Frame, new: tk.Frame, direction: int) -> None:
        self._animating = True
        step_px = WIN_W // ANIM_STEPS
        # Place new screen just off-screen in the incoming direction
        new.place(x=direction * WIN_W, y=0)
        new.lift()

        def step(iteration: int) -> None:
            if iteration >= ANIM_STEPS:
                old.place(x=-direction * WIN_W, y=0)
                new.place(x=0, y=0)
                self._animating = False
                return
            offset = iteration * step_px
            old.place(x=-direction * offset, y=0)
            new.place(x=direction * (WIN_W - offset), y=0)
            old.after(ANIM_MS, lambda: step(iteration + 1))

        step(1)

    def current(self) -> Optional[str]:
        return self._current


# ══════════════════════════════════════════════════════════════════════════════
# SCREEN CLASSES
# ══════════════════════════════════════════════════════════════════════════════

class BaseScreen(tk.Frame):
    """Common base for all screens."""

    def __init__(self, parent: tk.Widget, app: "App"):
        super().__init__(parent, bg=C_PANEL)
        self._app = app

    def on_enter(self) -> None:
        """Called each time this screen becomes visible."""


# ── Hub ────────────────────────────────────────────────────────────────────────

class HubScreen(BaseScreen):
    """Main menu / hub screen."""

    def __init__(self, parent: tk.Widget, app: "App"):
        super().__init__(parent, app)
        self._build()

    def _build(self) -> None:
        # Central content area
        content = tk.Frame(self, bg=C_PANEL)
        content.place(relx=0.5, rely=0.5, anchor="center")

        # Decorative header
        make_ornament_bar(content, width=700).pack(pady=(0, 18))

        make_title(content, "AD&D 2nd Edition Toolkit", size=22).pack(pady=(0, 4))
        make_label(content, "Select a tool to begin", color=C_DIM, bg=C_PANEL).pack(pady=(0, 20))

        make_ornament_bar(content, width=700).pack(pady=(0, 28))

        # Three big option buttons in a row
        btn_row = tk.Frame(content, bg=C_PANEL)
        btn_row.pack()

        cards = [
            ("⚔  CHARACTER\n    GENERATOR",   "chargen_name",  "Create and customise adventurers"),
            ("🗡  DM TOOLS\n   COMBAT",       "dm_tools",       "Track encounters and initiative"),
            ("📜  CAMPAIGN\n    LOG",          "campaign",       "Record session notes and events"),
        ]
        for label, target, desc in cards:
            self._make_hub_card(btn_row, label, target, desc).pack(
                side="left", padx=18, pady=8
            )

        make_ornament_bar(content, width=700).pack(pady=(28, 0))

        make_button(
            content,
            "PARTY MANAGEMENT",
            command=lambda: self._app.go("party_management"),
            width=24,
        ).pack(pady=(14, 0))

    def _make_hub_card(self, parent: tk.Widget, label: str,
                       target: str, desc: str) -> tk.Frame:
        outer = tk.Frame(parent, bg=C_BORDER, padx=2, pady=2)
        inner = tk.Frame(outer, bg=C_CARD, padx=20, pady=20, width=200, height=180)
        inner.pack_propagate(False)
        inner.pack()

        tk.Label(
            inner, text=label,
            bg=C_CARD, fg=C_TITLE,
            font=("Georgia", 13, "bold"),
            justify="center",
        ).pack(pady=(10, 8))

        tk.Label(
            inner, text=desc,
            bg=C_CARD, fg=C_DIM,
            font=("Georgia", 9),
            wraplength=160, justify="center",
        ).pack()

        make_button(inner, "OPEN", command=lambda t=target: self._app.go(t), width=12).pack(
            side="bottom", pady=(8, 4)
        )

        # Hover highlight
        def _enter(_e):
            inner.configure(bg=C_BTN_HOV)
            for w in inner.winfo_children():
                if isinstance(w, (tk.Label,)):
                    w.configure(bg=C_BTN_HOV)

        def _leave(_e):
            inner.configure(bg=C_CARD)
            for w in inner.winfo_children():
                if isinstance(w, (tk.Label,)):
                    w.configure(bg=C_CARD)

        outer.bind("<Enter>", _enter)
        inner.bind("<Enter>", _enter)
        outer.bind("<Leave>", _leave)
        inner.bind("<Leave>", _leave)

        return outer


# ── CharGen Step 1 – Name & Method ────────────────────────────────────────────

class CharGenNameScreen(BaseScreen):
    """Character generator step 1: name, method, alignment."""

    STEP = 1
    TOTAL_STEPS = 5

    def __init__(self, parent: tk.Widget, app: "App"):
        super().__init__(parent, app)
        self._name_var = tk.StringVar()
        self._player_var = tk.StringVar()
        self._method_var = tk.StringVar(value="4d6_drop_lowest")
        self._build()

    def _build(self) -> None:
        pad = tk.Frame(self, bg=C_PANEL)
        pad.place(relx=0.5, rely=0.5, anchor="center")

        make_title(pad, "CREATE YOUR CHARACTER", 20).pack(pady=(0, 4))
        make_label(pad, "Step 1  ·  Name & Ability Generation Method",
                   color=C_DIM, bg=C_PANEL).pack(pady=(0, 16))
        make_ornament_bar(pad, 560).pack(pady=(0, 22))

        form = tk.Frame(pad, bg=C_PANEL)
        form.pack()

        # Name row
        make_label(form, "Character Name:", bold=True, bg=C_PANEL).grid(
            row=0, column=0, sticky="e", padx=(0, 12), pady=8
        )
        make_entry(form, self._name_var, width=32).grid(
            row=0, column=1, sticky="w", pady=8
        )

        make_label(form, "Player Name:", bold=True, bg=C_PANEL).grid(
            row=1, column=0, sticky="e", padx=(0, 12), pady=8
        )
        make_entry(form, self._player_var, width=32).grid(
            row=1, column=1, sticky="w", pady=8
        )

        # Method
        make_label(form, "Ability Generation:", bold=True, bg=C_PANEL).grid(
            row=2, column=0, sticky="ne", padx=(0, 12), pady=8
        )
        method_frame = tk.Frame(form, bg=C_PANEL)
        method_frame.grid(row=2, column=1, sticky="w")

        methods = [
            ("4d6_drop_lowest", "Roll 4d6, drop lowest (recommended)"),
            ("standard_array",  "Standard Array  [15, 14, 13, 12, 10, 8]"),
        ]
        for val, lbl in methods:
            rb = tk.Radiobutton(
                method_frame, text=lbl,
                variable=self._method_var, value=val,
                bg=C_PANEL, fg=C_TEXT, selectcolor=C_INPUT_BG,
                activebackground=C_PANEL, activeforeground=C_TITLE,
                font=("Georgia", 11),
            )
            rb.pack(anchor="w", pady=2)

        make_ornament_bar(pad, 560).pack(pady=(22, 0))

    def on_enter(self) -> None:
        self._app.banner.set_subtitle("Character Generator  ›  Name & Method")
        self._app.nav.set_step(self.STEP, self.TOTAL_STEPS, "— Name & Method")
        self._app.nav.btn_back.configure(
            command=lambda: self._app.go("hub", direction=-1)
        )
        self._app.nav.btn_next.configure(command=self._advance)
        self._app.nav.btn_next.configure(text="NEXT  ▶")

    def _advance(self) -> None:
        name = self._name_var.get().strip()
        if not name:
            messagebox.showwarning("Name Required", "Please enter a character name.", parent=self._app.root)
            return
        self._app.chargen["name"] = name
        self._app.chargen["player_name"] = self._player_var.get().strip()
        self._app.chargen["method"] = self._method_var.get()
        # Pre-generate abilities now so the next screen can display them
        abilities = self._app.builder.generate_abilities(self._app.chargen["method"])
        self._app.chargen["abilities"] = abilities
        self._app.go("chargen_abilities")


# ── CharGen Step 2 – Ability Scores ───────────────────────────────────────────

class CharGenAbilitiesScreen(BaseScreen):
    """Character generator step 2: review / reroll ability scores."""

    STEP = 2
    TOTAL_STEPS = 5

    def __init__(self, parent: tk.Widget, app: "App"):
        super().__init__(parent, app)
        self._score_vars: Dict[str, tk.StringVar] = {
            a: tk.StringVar() for a in ABILITIES
        }
        self._build()

    def _build(self) -> None:
        pad = tk.Frame(self, bg=C_PANEL)
        pad.place(relx=0.5, rely=0.5, anchor="center")

        make_title(pad, "ABILITY SCORES", 20).pack(pady=(0, 4))
        make_label(pad, "Step 2  ·  Review or Reroll your scores",
                   color=C_DIM, bg=C_PANEL).pack(pady=(0, 12))
        make_ornament_bar(pad, 560).pack(pady=(0, 18))

        self._score_frame = tk.Frame(pad, bg=C_PANEL)
        self._score_frame.pack(pady=8)

        make_ornament_bar(pad, 560).pack(pady=(18, 8))

        make_button(pad, "↺  REROLL SCORES", command=self._reroll, width=22).pack(pady=4)
        self._info_label = make_label(pad, "", color=C_DIM, bg=C_PANEL, size=9)
        self._info_label.pack(pady=(4, 0))

    def _refresh_scores(self) -> None:
        for w in self._score_frame.winfo_children():
            w.destroy()

        abilities = self._app.chargen.get("abilities", {})
        method = self._app.chargen.get("method", "")
        self._info_label.configure(
            text=f"Method: {'Roll 4d6, drop lowest' if method == '4d6_drop_lowest' else 'Standard Array'}"
        )

        for col, ability in enumerate(ABILITIES):
            score = abilities.get(ability, 0)
            cell = tk.Frame(self._score_frame, bg=C_CARD,
                            width=80, height=100, padx=6, pady=8)
            cell.pack_propagate(False)
            cell.grid(row=0, column=col, padx=8)

            # Score value
            colour = C_TITLE if score >= 15 else (C_TEXT if score >= 9 else C_RED_LT)
            tk.Label(cell, text=str(score), bg=C_CARD, fg=colour,
                     font=("Georgia", 22, "bold")).pack(expand=True)
            tk.Label(cell, text=ABILITY_LABELS[ability][:3].upper(),
                     bg=C_CARD, fg=C_DIM,
                     font=("Georgia", 8)).pack()

    def on_enter(self) -> None:
        self._app.banner.set_subtitle("Character Generator  ›  Ability Scores")
        self._app.nav.set_step(self.STEP, self.TOTAL_STEPS, "— Ability Scores")
        self._app.nav.btn_back.configure(
            command=lambda: self._app.go("chargen_name", direction=-1)
        )
        self._app.nav.btn_next.configure(command=self._advance, text="NEXT  ▶")
        self._refresh_scores()

    def _reroll(self) -> None:
        abilities = self._app.builder.generate_abilities(self._app.chargen.get("method", "4d6_drop_lowest"))
        self._app.chargen["abilities"] = abilities
        self._refresh_scores()

    def _advance(self) -> None:
        self._app.go("chargen_race")


# ── CharGen Step 3 – Race ─────────────────────────────────────────────────────

class CharGenRaceScreen(BaseScreen):
    """Character generator step 3: choose race."""

    STEP = 3
    TOTAL_STEPS = 5

    def __init__(self, parent: tk.Widget, app: "App"):
        super().__init__(parent, app)
        self._race_var = tk.StringVar()
        self._build()

    def _build(self) -> None:
        pad = tk.Frame(self, bg=C_PANEL)
        pad.place(relx=0.5, rely=0.5, anchor="center")

        make_title(pad, "CHOOSE YOUR RACE", 20).pack(pady=(0, 4))
        make_label(pad, "Step 3  ·  Select the race for your character",
                   color=C_DIM, bg=C_PANEL).pack(pady=(0, 12))
        make_ornament_bar(pad, 620).pack(pady=(0, 18))

        columns = tk.Frame(pad, bg=C_PANEL)
        columns.pack()

        # Left: list of races
        left = tk.Frame(columns, bg=C_PANEL)
        left.pack(side="left", padx=(0, 20))

        make_label(left, "Race", bold=True, bg=C_PANEL).pack(anchor="w")
        self._lb = make_listbox(left, height=8, width=22)
        self._lb.pack()
        self._lb.bind("<<ListboxSelect>>", self._on_select)

        # Right: info panel
        right = tk.Frame(columns, bg=C_CARD, padx=16, pady=16, width=280, height=220)
        right.pack_propagate(False)
        right.pack(side="left")

        self._race_title = make_label(right, "Select a race…", bold=True,
                                       color=C_TITLE, bg=C_CARD, size=13)
        self._race_title.pack(anchor="w", pady=(0, 8))

        self._race_info = tk.Text(
            right, bg=C_CARD, fg=C_TEXT,
            font=("Georgia", 10),
            relief="flat", bd=0,
            wrap="word", state="disabled",
            width=28, height=8,
        )
        self._race_info.pack(fill="both", expand=True)

        make_ornament_bar(pad, 620).pack(pady=(18, 0))

    def _populate(self) -> None:
        self._lb.delete(0, "end")
        for rid, rdata in self._app.rules.list_races().items():
            self._lb.insert("end", rdata["name"])
        # Restore previous selection if any
        prev = self._app.chargen.get("race_id")
        if prev:
            races = list(self._app.rules.list_races().keys())
            if prev in races:
                idx = races.index(prev)
                self._lb.selection_set(idx)
                self._race_var.set(prev)
                self._show_race_info(prev)

    def _on_select(self, _event=None) -> None:
        sel = self._lb.curselection()
        if not sel:
            return
        races = list(self._app.rules.list_races().keys())
        race_id = races[sel[0]]
        self._race_var.set(race_id)
        self._show_race_info(race_id)

    def _show_race_info(self, race_id: str) -> None:
        rdata = self._app.rules.get_race(race_id)
        name = rdata.get("name", race_id)
        mins = rdata.get("ability_minimums", {})
        maxs = rdata.get("ability_maximums", {})
        lines = [f"Race: {name}\n"]
        if mins:
            lines.append("Ability Minimums:")
            for ab, v in mins.items():
                lines.append(f"  {ABILITY_LABELS.get(ab, ab)}: {v}")
        if maxs:
            lines.append("\nAbility Maximums:")
            for ab, v in maxs.items():
                lines.append(f"  {ABILITY_LABELS.get(ab, ab)}: {v}")
        if not mins and not maxs:
            lines.append("No special ability restrictions.")

        self._race_title.configure(text=name)
        self._race_info.configure(state="normal")
        self._race_info.delete("1.0", "end")
        self._race_info.insert("end", "\n".join(lines))
        self._race_info.configure(state="disabled")

    def on_enter(self) -> None:
        self._app.banner.set_subtitle("Character Generator  ›  Race")
        self._app.nav.set_step(self.STEP, self.TOTAL_STEPS, "— Race")
        self._app.nav.btn_back.configure(
            command=lambda: self._app.go("chargen_abilities", direction=-1)
        )
        self._app.nav.btn_next.configure(command=self._advance, text="NEXT  ▶")
        self._populate()

    def _advance(self) -> None:
        race_id = self._race_var.get()
        if not race_id:
            messagebox.showwarning("Selection Required", "Please choose a race.", parent=self._app.root)
            return
        self._app.chargen["race_id"] = race_id
        self._app.go("chargen_class")


# ── CharGen Step 4 – Class ────────────────────────────────────────────────────

class CharGenClassScreen(BaseScreen):
    """Character generator step 4: choose class."""

    STEP = 4
    TOTAL_STEPS = 5

    def __init__(self, parent: tk.Widget, app: "App"):
        super().__init__(parent, app)
        self._class_var = tk.StringVar()
        self._build()

    def _build(self) -> None:
        pad = tk.Frame(self, bg=C_PANEL)
        pad.place(relx=0.5, rely=0.5, anchor="center")

        make_title(pad, "CHOOSE YOUR CLASS", 20).pack(pady=(0, 4))
        make_label(pad, "Step 4  ·  Select your character class",
                   color=C_DIM, bg=C_PANEL).pack(pady=(0, 12))
        make_ornament_bar(pad, 620).pack(pady=(0, 18))

        columns = tk.Frame(pad, bg=C_PANEL)
        columns.pack()

        left = tk.Frame(columns, bg=C_PANEL)
        left.pack(side="left", padx=(0, 20))
        make_label(left, "Class", bold=True, bg=C_PANEL).pack(anchor="w")
        self._lb = make_listbox(left, height=8, width=22)
        self._lb.pack()
        self._lb.bind("<<ListboxSelect>>", self._on_select)

        right = tk.Frame(columns, bg=C_CARD, padx=16, pady=16, width=280, height=220)
        right.pack_propagate(False)
        right.pack(side="left")

        self._class_title = make_label(right, "Select a class…", bold=True,
                                        color=C_TITLE, bg=C_CARD, size=13)
        self._class_title.pack(anchor="w", pady=(0, 8))

        self._class_info = tk.Text(
            right, bg=C_CARD, fg=C_TEXT,
            font=("Georgia", 10),
            relief="flat", bd=0,
            wrap="word", state="disabled",
            width=28, height=8,
        )
        self._class_info.pack(fill="both", expand=True)

        # Validation feedback
        self._valid_label = make_label(pad, "", color=C_DIM, bg=C_PANEL, size=9)
        self._valid_label.pack(pady=(8, 0))

        make_ornament_bar(pad, 620).pack(pady=(12, 0))

    def _populate(self) -> None:
        self._lb.delete(0, "end")
        race_id = self._app.chargen.get("race_id", "")
        all_classes = self._app.rules.list_classes()
        self._eligible: List[str] = []
        for cid, cdata in all_classes.items():
            allowed = cdata.get("allowed_races", [])
            if not allowed or race_id in allowed:
                self._eligible.append(cid)
                self._lb.insert("end", cdata["name"])
        # Restore
        prev = self._app.chargen.get("class_id")
        if prev and prev in self._eligible:
            idx = self._eligible.index(prev)
            self._lb.selection_set(idx)
            self._class_var.set(prev)
            self._show_class_info(prev)

    def _on_select(self, _event=None) -> None:
        sel = self._lb.curselection()
        if not sel:
            return
        class_id = self._eligible[sel[0]]
        self._class_var.set(class_id)
        self._show_class_info(class_id)
        self._validate_live(class_id)

    def _show_class_info(self, class_id: str) -> None:
        cdata = self._app.rules.get_class(class_id)
        name = cdata.get("name", class_id)
        mins = cdata.get("ability_minimums", {})
        self._class_title.configure(text=name)
        lines = [f"Class: {name}\n"]
        if mins:
            lines.append("Ability Requirements:")
            for ab, v in mins.items():
                lines.append(f"  {ABILITY_LABELS.get(ab, ab)}: {v}")
        else:
            lines.append("No special ability requirements.")
        self._class_info.configure(state="normal")
        self._class_info.delete("1.0", "end")
        self._class_info.insert("end", "\n".join(lines))
        self._class_info.configure(state="disabled")

    def _validate_live(self, class_id: str) -> None:
        abilities = self._app.chargen.get("abilities", {})
        race_id = self._app.chargen.get("race_id", "human")
        issues = self._app.builder.validate_choice(race_id, class_id, abilities)
        if issues:
            self._valid_label.configure(
                text="⚠  " + issues[0], fg=C_RED_LT
            )
        else:
            self._valid_label.configure(
                text="✓  Valid combination", fg="#50c050"
            )

    def on_enter(self) -> None:
        self._app.banner.set_subtitle("Character Generator  ›  Class")
        self._app.nav.set_step(self.STEP, self.TOTAL_STEPS, "— Class")
        self._app.nav.btn_back.configure(
            command=lambda: self._app.go("chargen_race", direction=-1)
        )
        self._app.nav.btn_next.configure(command=self._advance, text="NEXT  ▶")
        self._populate()

    def _advance(self) -> None:
        class_id = self._class_var.get()
        if not class_id:
            messagebox.showwarning("Selection Required", "Please choose a class.", parent=self._app.root)
            return
        abilities = self._app.chargen.get("abilities", {})
        race_id = self._app.chargen.get("race_id", "human")
        issues = self._app.builder.validate_choice(race_id, class_id, abilities)
        if issues:
            confirm = messagebox.askyesno(
                "Ability Score Warning",
                f"{issues[0]}\n\nProceed anyway?",
                parent=self._app.root,
            )
            if not confirm:
                return
        self._app.chargen["class_id"] = class_id
        self._app.go("chargen_review")


# ── CharGen Step 5 – Review ───────────────────────────────────────────────────

class CharGenReviewScreen(BaseScreen):
    """Character generator step 5: final review and save."""

    STEP = 5
    TOTAL_STEPS = 5

    def __init__(self, parent: tk.Widget, app: "App"):
        super().__init__(parent, app)
        self._build()

    def _build(self) -> None:
        self._pad = tk.Frame(self, bg=C_PANEL)
        self._pad.place(relx=0.5, rely=0.5, anchor="center")

    def _refresh(self) -> None:
        for w in self._pad.winfo_children():
            w.destroy()

        cg = self._app.chargen
        name     = cg.get("name", "—")
        player   = cg.get("player_name", "") or "—"
        race_id  = cg.get("race_id", "—")
        class_id = cg.get("class_id", "—")
        abilities = cg.get("abilities", {})

        race_name  = self._app.rules.get_race(race_id).get("name", race_id) if race_id != "—" else "—"
        class_name = self._app.rules.get_class(class_id).get("name", class_id) if class_id != "—" else "—"

        make_title(self._pad, "CHARACTER SUMMARY", 20).pack(pady=(0, 4))
        make_label(self._pad, "Step 5  ·  Review and Finalise",
                   color=C_DIM, bg=C_PANEL).pack(pady=(0, 12))
        make_ornament_bar(self._pad, 600).pack(pady=(0, 18))

        card = tk.Frame(self._pad, bg=C_CARD, padx=30, pady=20)
        card.pack(padx=40)

        # Name / Race / Class
        info_rows = [
            ("Name",  name),
            ("Player", player),
            ("Race",  race_name),
            ("Class", class_name),
            ("Party", "Unassigned"),
        ]
        for lbl, val in info_rows:
            row = tk.Frame(card, bg=C_CARD)
            row.pack(fill="x", pady=3)
            make_label(row, f"{lbl}:", bold=True, bg=C_CARD, size=12).pack(side="left", padx=(0, 12))
            make_label(row, val, bg=C_CARD, size=12, color=C_TITLE).pack(side="left")

        make_separator(card, 480).pack(pady=10)

        # Ability scores
        ab_row = tk.Frame(card, bg=C_CARD)
        ab_row.pack()
        for col, ab in enumerate(ABILITIES):
            score = abilities.get(ab, 0)
            cell = tk.Frame(ab_row, bg=C_BTN, width=70, height=80, padx=4, pady=6)
            cell.pack_propagate(False)
            cell.grid(row=0, column=col, padx=5)
            colour = C_TITLE if score >= 15 else (C_TEXT if score >= 9 else C_RED_LT)
            tk.Label(cell, text=str(score), bg=C_BTN, fg=colour,
                     font=("Georgia", 20, "bold")).pack(expand=True)
            tk.Label(cell, text=ab.upper(), bg=C_BTN, fg=C_DIM,
                     font=("Georgia", 8)).pack()

        make_ornament_bar(self._pad, 600).pack(pady=(18, 8))

        make_button(self._pad, "✔  FINALISE CHARACTER",
                    command=self._finalise, width=26).pack(pady=4)

    def on_enter(self) -> None:
        self._app.banner.set_subtitle("Character Generator  ›  Review")
        self._app.nav.set_step(self.STEP, self.TOTAL_STEPS, "— Review")
        self._app.nav.btn_back.configure(
            command=lambda: self._app.go("chargen_class", direction=-1)
        )
        self._app.nav.btn_next.configure(command=self._finalise, text="FINISH  ✔")
        self._refresh()

    def _finalise(self) -> None:
        cg = self._app.chargen
        try:
            sheet = self._app.builder.build_character(
                name=cg["name"],
                race_id=cg["race_id"],
                class_id=cg["class_id"],
                abilities=cg["abilities"],
                player_name=cg.get("player_name", ""),
                party_name="Unassigned",
            )
            self._app.characters.append(sheet)
            self._app.persist_characters()
            messagebox.showinfo(
                "Character Created",
                f"{sheet.name} the {sheet.race_id.title()} {sheet.class_id.title()} is ready for adventure!",
                parent=self._app.root,
            )
            self._app.chargen.clear()
            self._app.go("hub", direction=-1)
        except ValueError as exc:
            messagebox.showerror("Invalid Character", str(exc), parent=self._app.root)


# ── DM Tools – Combat ─────────────────────────────────────────────────────────

class DMToolsScreen(BaseScreen):
    """Combat tracker."""

    def __init__(self, parent: tk.Widget, app: "App"):
        super().__init__(parent, app)
        self._tracker: Optional[CombatTracker] = None
        self._enc_name_var = tk.StringVar(value="New Encounter")
        self._comb_name_var = tk.StringVar()
        self._comb_init_var = tk.StringVar()
        self._comb_hp_var   = tk.StringVar()
        self._dmg_var       = tk.StringVar()
        self._build()

    def _build(self) -> None:
        top_strip = tk.Frame(self, bg=C_PANEL)
        top_strip.pack(fill="x", padx=20, pady=(16, 0))

        make_title(top_strip, "DM TOOLS — COMBAT TRACKER", 18).pack(side="left")
        make_button(top_strip, "◀  BACK TO HUB",
                    command=lambda: self._app.go("hub", direction=-1),
                    width=16).pack(side="right")

        make_ornament_bar(self, 880).pack(pady=(10, 14))

        content = tk.Frame(self, bg=C_PANEL)
        content.pack(fill="both", expand=True, padx=20)

        # ── Left panel: encounter creation + combatant add ──
        left = tk.Frame(content, bg=C_PANEL, width=280)
        left.pack_propagate(False)
        left.pack(side="left", fill="y", padx=(0, 16))

        make_label(left, "Encounter Name:", bold=True, bg=C_PANEL).pack(anchor="w")
        make_entry(left, self._enc_name_var, width=24).pack(anchor="w", pady=(2, 8))
        make_button(left, "⚔  NEW ENCOUNTER", command=self._new_encounter, width=22).pack(anchor="w")

        make_separator(left, 260).pack(pady=12)

        make_label(left, "Add Combatant:", bold=True, bg=C_PANEL).pack(anchor="w")
        for lbl, var in [("Name", self._comb_name_var),
                          ("Initiative", self._comb_init_var),
                          ("HP", self._comb_hp_var)]:
            make_label(left, lbl + ":", bg=C_PANEL, size=10).pack(anchor="w", pady=(4, 0))
            make_entry(left, var, width=22).pack(anchor="w")

        make_button(left, "+  ADD COMBATANT", command=self._add_combatant, width=22).pack(
            anchor="w", pady=(10, 0)
        )

        make_separator(left, 260).pack(pady=12)

        make_label(left, "Damage (selected):", bold=True, bg=C_PANEL).pack(anchor="w")
        dmg_row = tk.Frame(left, bg=C_PANEL)
        dmg_row.pack(anchor="w", fill="x", pady=(4, 0))
        make_entry(dmg_row, self._dmg_var, width=10).pack(side="left", padx=(0, 6))
        make_button(dmg_row, "APPLY", command=self._apply_damage, width=10).pack(side="left")

        make_button(left, "▶  NEXT ROUND", command=self._next_round, width=22).pack(
            anchor="w", pady=(14, 0)
        )

        # ── Right panel: combatant list ──
        right = tk.Frame(content, bg=C_PANEL)
        right.pack(side="left", fill="both", expand=True)

        make_label(right, "Initiative Order:", bold=True, bg=C_PANEL).pack(anchor="w")

        list_frame = tk.Frame(right, bg=C_PANEL)
        list_frame.pack(fill="both", expand=True, pady=(4, 0))

        self._comb_lb = make_listbox(list_frame, height=12, width=50)
        self._comb_lb.pack(side="left", fill="both", expand=True)
        make_scrollbar(list_frame, self._comb_lb).pack(side="left", fill="y")

        self._round_label = make_label(right, "Round: —", bold=True,
                                        color=C_TITLE, bg=C_PANEL)
        self._round_label.pack(anchor="w", pady=(6, 0))

    def _new_encounter(self) -> None:
        name = self._enc_name_var.get().strip() or "Encounter"
        self._tracker = CombatTracker(name)
        self._refresh_list()
        self._round_label.configure(text=f"Round: 1  —  {name}")

    def _add_combatant(self) -> None:
        if not self._tracker:
            messagebox.showinfo("No Encounter", "Create an encounter first.", parent=self._app.root)
            return
        name = self._comb_name_var.get().strip()
        try:
            init = int(self._comb_init_var.get())
            hp   = int(self._comb_hp_var.get())
        except ValueError:
            messagebox.showwarning("Invalid Input", "Initiative and HP must be numbers.", parent=self._app.root)
            return
        if not name:
            messagebox.showwarning("Name Required", "Enter a combatant name.", parent=self._app.root)
            return
        self._tracker.add_combatant(Combatant(name=name, initiative=init,
                                               hp_current=hp, hp_max=hp))
        self._comb_name_var.set("")
        self._comb_init_var.set("")
        self._comb_hp_var.set("")
        self._refresh_list()

    def _apply_damage(self) -> None:
        if not self._tracker:
            return
        sel = self._comb_lb.curselection()
        if not sel:
            messagebox.showinfo("Select Target", "Select a combatant first.", parent=self._app.root)
            return
        try:
            dmg = int(self._dmg_var.get())
        except ValueError:
            messagebox.showwarning("Invalid Damage", "Enter a numeric damage value.", parent=self._app.root)
            return
        idx = sel[0]
        combatant = self._tracker.encounter.combatants[idx]
        self._tracker.apply_damage(combatant.name, dmg)
        self._dmg_var.set("")
        self._refresh_list()

    def _next_round(self) -> None:
        if not self._tracker:
            return
        self._tracker.advance_round()
        self._refresh_list()
        self._round_label.configure(
            text=f"Round: {self._tracker.encounter.round_number}  —  {self._tracker.encounter.name}"
        )

    def _refresh_list(self) -> None:
        self._comb_lb.delete(0, "end")
        if not self._tracker:
            return
        for c in self._tracker.encounter.combatants:
            hp_str = f"HP {c.hp_current}/{c.hp_max}"
            status = " [DEAD]" if c.hp_current <= 0 else ""
            self._comb_lb.insert("end", f"  {c.initiative:>3}  {c.name:<24} {hp_str}{status}")

    def on_enter(self) -> None:
        self._app.banner.set_subtitle("DM Tools  ›  Combat Tracker")
        self._app.nav.hide()


# ── Campaign Screen ───────────────────────────────────────────────────────────

class CampaignScreen(BaseScreen):
    """Campaign log viewer / editor."""

    def __init__(self, parent: tk.Widget, app: "App"):
        super().__init__(parent, app)
        self._mgr = CampaignManager()
        self._title_var = tk.StringVar()
        self._session_var = tk.StringVar()
        self._build()

    def _build(self) -> None:
        top_strip = tk.Frame(self, bg=C_PANEL)
        top_strip.pack(fill="x", padx=20, pady=(16, 0))

        make_title(top_strip, "CAMPAIGN LOG", 18).pack(side="left")
        make_button(top_strip, "◀  BACK TO HUB",
                    command=lambda: self._app.go("hub", direction=-1),
                    width=16).pack(side="right")

        make_ornament_bar(self, 880).pack(pady=(10, 14))

        content = tk.Frame(self, bg=C_PANEL)
        content.pack(fill="both", expand=True, padx=20)

        # ── Left: add entry ──
        left = tk.Frame(content, bg=C_PANEL, width=260)
        left.pack_propagate(False)
        left.pack(side="left", fill="y", padx=(0, 16))

        make_label(left, "Session ID:", bold=True, bg=C_PANEL).pack(anchor="w")
        make_entry(left, self._session_var, width=22).pack(anchor="w", pady=(2, 8))

        make_label(left, "Title:", bold=True, bg=C_PANEL).pack(anchor="w")
        make_entry(left, self._title_var, width=22).pack(anchor="w", pady=(2, 8))

        make_label(left, "Notes:", bold=True, bg=C_PANEL).pack(anchor="w")
        self._body_text = tk.Text(
            left, bg=C_INPUT_BG, fg=C_TEXT, insertbackground=C_TEXT,
            font=("Georgia", 10), relief="flat", bd=4,
            width=24, height=8, wrap="word",
        )
        self._body_text.pack(anchor="w", pady=(2, 8))

        make_button(left, "+  ADD ENTRY", command=self._add_entry, width=22).pack(anchor="w")

        # ── Right: entry list ──
        right = tk.Frame(content, bg=C_PANEL)
        right.pack(side="left", fill="both", expand=True)

        make_label(right, "Session Entries:", bold=True, bg=C_PANEL).pack(anchor="w")

        list_frame = tk.Frame(right, bg=C_PANEL)
        list_frame.pack(fill="both", expand=True, pady=(4, 0))

        self._entry_lb = make_listbox(list_frame, height=6, width=48)
        self._entry_lb.pack(side="left", fill="x", expand=True)
        make_scrollbar(list_frame, self._entry_lb).pack(side="left", fill="y")
        self._entry_lb.bind("<<ListboxSelect>>", self._show_entry)

        make_label(right, "Entry Detail:", bold=True, bg=C_PANEL).pack(anchor="w", pady=(10, 0))
        self._detail = tk.Text(
            right, bg=C_CARD, fg=C_TEXT,
            font=("Georgia", 10), relief="flat", bd=0,
            width=50, height=8, wrap="word", state="disabled",
        )
        self._detail.pack(fill="both", expand=True, pady=(4, 0))

    def _add_entry(self) -> None:
        sid   = self._session_var.get().strip()
        title = self._title_var.get().strip()
        body  = self._body_text.get("1.0", "end-1c").strip()
        if not sid or not title:
            messagebox.showwarning("Missing Fields", "Session ID and Title are required.", parent=self._app.root)
            return
        entry = CampaignEntry(session_id=sid, title=title, body=body)
        self._mgr.add_entry(entry)
        self._session_var.set("")
        self._title_var.set("")
        self._body_text.delete("1.0", "end")
        self._refresh_list()

    def _refresh_list(self) -> None:
        self._entry_lb.delete(0, "end")
        for e in self._mgr.entries:
            self._entry_lb.insert("end", f"  [{e.session_id}]  {e.title}")

    def _show_entry(self, _event=None) -> None:
        sel = self._entry_lb.curselection()
        if not sel:
            return
        entry = self._mgr.entries[sel[0]]
        self._detail.configure(state="normal")
        self._detail.delete("1.0", "end")
        self._detail.insert("end", f"[{entry.session_id}]  {entry.title}\n\n{entry.body}")
        self._detail.configure(state="disabled")

    def on_enter(self) -> None:
        self._app.banner.set_subtitle("Campaign  ›  Session Log")
        self._app.nav.hide()


class PartyManagementScreen(BaseScreen):
    """Manage parties, player assignments, and party-based PC filtering."""

    FILTER_ALL = "All Parties"

    def __init__(self, parent: tk.Widget, app: "App"):
        super().__init__(parent, app)
        self._player_var = tk.StringVar()
        self._selected_party_var = tk.StringVar(value=UNASSIGNED_PARTY)
        self._filter_party_var = tk.StringVar(value=self.FILTER_ALL)
        self._party_name_var = tk.StringVar()
        self._selected_character: Optional[CharacterSheet] = None
        self._filtered_characters: List[CharacterSheet] = []
        self._build()

    def _build(self) -> None:
        top_strip = tk.Frame(self, bg=C_PANEL)
        top_strip.pack(fill="x", padx=20, pady=(16, 0))

        make_title(top_strip, "PARTY MANAGEMENT", 18).pack(side="left")
        make_button(top_strip, "◀  BACK TO HUB",
                    command=lambda: self._app.go("hub", direction=-1),
                    width=16).pack(side="right")

        make_ornament_bar(self, 880).pack(pady=(10, 14))

        content = tk.Frame(self, bg=C_PANEL)
        content.pack(fill="both", expand=True, padx=20)

        left = tk.Frame(content, bg=C_PANEL, width=360)
        left.pack_propagate(False)
        left.pack(side="left", fill="y", padx=(0, 16))

        make_label(left, "Filter PCs by Party:", bold=True, bg=C_PANEL).pack(anchor="w")
        self._filter_menu = tk.OptionMenu(left, self._filter_party_var, self.FILTER_ALL, command=lambda _v: self._refresh_characters())
        self._filter_menu.configure(
            bg=C_BTN, fg=C_BTN_TXT, activebackground=C_BTN_ACT, activeforeground=C_SEL_TXT,
            highlightthickness=0, relief="flat", width=24,
        )
        self._filter_menu.pack(anchor="w", pady=(2, 8))

        make_label(left, "Player Characters:", bold=True, bg=C_PANEL).pack(anchor="w")
        list_frame = tk.Frame(left, bg=C_PANEL)
        list_frame.pack(fill="both", expand=True, pady=(4, 0))

        self._chars_lb = make_listbox(list_frame, height=14, width=42)
        self._chars_lb.pack(side="left", fill="both", expand=True)
        make_scrollbar(list_frame, self._chars_lb).pack(side="left", fill="y")
        self._chars_lb.bind("<<ListboxSelect>>", self._on_select_character)

        right = tk.Frame(content, bg=C_PANEL)
        right.pack(side="left", fill="both", expand=True)

        party_card = tk.Frame(right, bg=C_CARD, padx=16, pady=12)
        party_card.pack(fill="x")

        make_label(party_card, "Parties", bold=True, bg=C_CARD, color=C_TITLE, size=13).pack(anchor="w")
        party_list_frame = tk.Frame(party_card, bg=C_CARD)
        party_list_frame.pack(fill="x", pady=(4, 8))
        self._parties_lb = make_listbox(party_list_frame, height=5, width=28)
        self._parties_lb.pack(side="left")
        make_scrollbar(party_list_frame, self._parties_lb).pack(side="left", fill="y")
        self._parties_lb.bind("<<ListboxSelect>>", self._on_select_party)

        make_label(party_card, "Party Name:", bold=True, bg=C_CARD).pack(anchor="w")
        make_entry(party_card, self._party_name_var, width=28).pack(anchor="w", pady=(2, 8))

        party_btns = tk.Frame(party_card, bg=C_CARD)
        party_btns.pack(anchor="w")
        make_button(party_btns, "CREATE", command=self._create_party, width=10).pack(side="left")
        make_button(party_btns, "RENAME", command=self._rename_party, width=10).pack(side="left", padx=(6, 0))
        make_button(party_btns, "DELETE", command=self._delete_party, width=10, danger=True).pack(side="left", padx=(6, 0))

        char_card = tk.Frame(right, bg=C_CARD, padx=16, pady=12)
        char_card.pack(fill="x", pady=(12, 0))

        self._char_title = make_label(char_card, "Select a character", bold=True,
                                      color=C_TITLE, bg=C_CARD, size=14)
        self._char_title.pack(anchor="w", pady=(0, 8))

        self._char_meta = make_label(char_card, "", bg=C_CARD, color=C_TEXT, size=10, justify="left")
        self._char_meta.pack(anchor="w", pady=(0, 10))

        make_label(char_card, "Player Name:", bold=True, bg=C_CARD).pack(anchor="w")
        make_entry(char_card, self._player_var, width=32).pack(anchor="w", pady=(2, 8))

        make_label(char_card, "Assign to Party:", bold=True, bg=C_CARD).pack(anchor="w")
        self._assign_menu = tk.OptionMenu(char_card, self._selected_party_var, UNASSIGNED_PARTY)
        self._assign_menu.configure(
            bg=C_BTN, fg=C_BTN_TXT, activebackground=C_BTN_ACT, activeforeground=C_SEL_TXT,
            highlightthickness=0, relief="flat", width=24,
        )
        self._assign_menu.pack(anchor="w", pady=(2, 10))

        char_btns = tk.Frame(char_card, bg=C_CARD)
        char_btns.pack(anchor="w")
        make_button(char_btns, "SAVE CHARACTER", command=self._save_selected, width=16).pack(side="left")
        make_button(char_btns, "SET UNASSIGNED", command=self._clear_party, width=16).pack(side="left", padx=(8, 0))

        make_label(right, "Party Rosters:", bold=True, bg=C_PANEL).pack(anchor="w", pady=(12, 4))
        self._party_detail = tk.Text(
            right, bg=C_CARD, fg=C_TEXT,
            font=("Georgia", 10), relief="flat", bd=0,
            width=52, height=10, wrap="word", state="disabled",
        )
        self._party_detail.pack(fill="both", expand=True)

    def _party_names(self) -> List[str]:
        names = [name for name in self._app.parties if name.strip()]
        if UNASSIGNED_PARTY.lower() not in {name.lower() for name in names}:
            names.insert(0, UNASSIGNED_PARTY)
        names.sort(key=str.lower)
        return names

    def _set_menu_values(self, option_menu: tk.OptionMenu, variable: tk.StringVar, values: List[str], default_value: str) -> None:
        menu = option_menu["menu"]
        menu.delete(0, "end")
        for value in values:
            menu.add_command(label=value, command=tk._setit(variable, value))

        if variable.get() not in values:
            variable.set(default_value if default_value in values else values[0])

    def _refresh_party_controls(self) -> None:
        party_names = self._party_names()

        self._parties_lb.delete(0, "end")
        for name in party_names:
            if name.lower() == UNASSIGNED_PARTY.lower():
                continue
            self._parties_lb.insert("end", name)

        filter_values = [self.FILTER_ALL] + party_names
        self._set_menu_values(self._filter_menu, self._filter_party_var, filter_values, self.FILTER_ALL)
        self._set_menu_values(self._assign_menu, self._selected_party_var, party_names, UNASSIGNED_PARTY)

    def _refresh_characters(self) -> None:
        self._chars_lb.delete(0, "end")
        selected_filter = self._filter_party_var.get()

        if selected_filter and selected_filter != self.FILTER_ALL:
            self._filtered_characters = [
                sheet for sheet in self._app.characters
                if (sheet.party_name or UNASSIGNED_PARTY).strip().lower() == selected_filter.strip().lower()
            ]
        else:
            self._filtered_characters = list(self._app.characters)

        for sheet in self._filtered_characters:
            player = sheet.player_name or "Unassigned player"
            party = sheet.party_name or UNASSIGNED_PARTY
            self._chars_lb.insert("end", f"{sheet.name}  —  {player}  [{party}]")

    def _refresh_party_roster(self) -> None:
        parties: Dict[str, List[CharacterSheet]] = {name: [] for name in self._party_names()}
        for sheet in self._app.characters:
            party_name = (sheet.party_name or UNASSIGNED_PARTY).strip() or UNASSIGNED_PARTY
            parties.setdefault(party_name, []).append(sheet)

        lines: List[str] = []
        for party_name in sorted(parties.keys(), key=str.lower):
            lines.append(f"{party_name}")
            members = parties[party_name]
            if not members:
                lines.append("  - (no members)")
            else:
                for member in members:
                    player = member.player_name or "Unassigned player"
                    lines.append(f"  - {member.name} (Player: {player})")
            lines.append("")

        self._party_detail.configure(state="normal")
        self._party_detail.delete("1.0", "end")
        self._party_detail.insert("end", "\n".join(lines).strip())
        self._party_detail.configure(state="disabled")

    def _on_select_character(self, _event=None) -> None:
        sel = self._chars_lb.curselection()
        if not sel:
            self._selected_character = None
            return

        self._selected_character = self._filtered_characters[sel[0]]
        sheet = self._selected_character
        self._player_var.set(sheet.player_name)
        self._selected_party_var.set(sheet.party_name or UNASSIGNED_PARTY)

        race = self._app.rules.get_race(sheet.race_id).get("name", sheet.race_id)
        char_class = self._app.rules.get_class(sheet.class_id).get("name", sheet.class_id)
        self._char_title.configure(text=sheet.name)
        self._char_meta.configure(text=f"Race: {race}\nClass: {char_class}")

    def _on_select_party(self, _event=None) -> None:
        sel = self._parties_lb.curselection()
        if not sel:
            return
        self._party_name_var.set(self._parties_lb.get(sel[0]))

    def _create_party(self) -> None:
        party_name = self._party_name_var.get().strip()
        if not party_name:
            messagebox.showwarning("Party Name Required", "Enter a party name to create.", parent=self._app.root)
            return
        if party_name.lower() == UNASSIGNED_PARTY.lower():
            messagebox.showwarning("Invalid Name", f"{UNASSIGNED_PARTY} is reserved.", parent=self._app.root)
            return
        if party_name.lower() in {p.lower() for p in self._app.parties}:
            messagebox.showwarning("Duplicate Party", "A party with that name already exists.", parent=self._app.root)
            return

        self._app.parties.append(party_name)
        self._app.persist_characters()
        self._refresh_party_controls()
        self._refresh_party_roster()

    def _rename_party(self) -> None:
        sel = self._parties_lb.curselection()
        if not sel:
            messagebox.showinfo("Select Party", "Choose a party to rename.", parent=self._app.root)
            return

        old_name = self._parties_lb.get(sel[0]).strip()
        new_name = self._party_name_var.get().strip()
        if not new_name:
            messagebox.showwarning("Party Name Required", "Enter the new party name.", parent=self._app.root)
            return
        if new_name.lower() == UNASSIGNED_PARTY.lower():
            messagebox.showwarning("Invalid Name", f"{UNASSIGNED_PARTY} is reserved.", parent=self._app.root)
            return
        if new_name.lower() != old_name.lower() and new_name.lower() in {p.lower() for p in self._app.parties}:
            messagebox.showwarning("Duplicate Party", "A party with that name already exists.", parent=self._app.root)
            return

        for idx, party in enumerate(self._app.parties):
            if party.lower() == old_name.lower():
                self._app.parties[idx] = new_name
                break

        for sheet in self._app.characters:
            if (sheet.party_name or "").strip().lower() == old_name.lower():
                sheet.party_name = new_name

        self._app.persist_characters()
        self._refresh_party_controls()
        self._refresh_characters()
        self._refresh_party_roster()

    def _delete_party(self) -> None:
        sel = self._parties_lb.curselection()
        if not sel:
            messagebox.showinfo("Select Party", "Choose a party to delete.", parent=self._app.root)
            return

        party_name = self._parties_lb.get(sel[0]).strip()
        confirm = messagebox.askyesno(
            "Delete Party",
            f"Delete {party_name}? Characters in that party will be moved to {UNASSIGNED_PARTY}.",
            parent=self._app.root,
        )
        if not confirm:
            return

        self._app.parties = [p for p in self._app.parties if p.lower() != party_name.lower()]
        for sheet in self._app.characters:
            if (sheet.party_name or "").strip().lower() == party_name.lower():
                sheet.party_name = UNASSIGNED_PARTY

        self._app.persist_characters()
        self._party_name_var.set("")
        self._refresh_party_controls()
        self._refresh_characters()
        self._refresh_party_roster()

    def _save_selected(self) -> None:
        if self._selected_character is None:
            messagebox.showinfo("Select Character", "Choose a character from the list first.", parent=self._app.root)
            return

        selected_party = self._selected_party_var.get().strip() or UNASSIGNED_PARTY
        if selected_party.lower() not in {p.lower() for p in self._app.parties}:
            self._app.parties.append(selected_party)

        self._selected_character.player_name = self._player_var.get().strip()
        self._selected_character.party_name = selected_party

        self._app.persist_characters()
        self._refresh_party_controls()
        self._refresh_characters()
        self._refresh_party_roster()

    def _clear_party(self) -> None:
        if self._selected_character is None:
            messagebox.showinfo("Select Character", "Choose a character from the list first.", parent=self._app.root)
            return
        self._selected_party_var.set(UNASSIGNED_PARTY)
        self._save_selected()

    def on_enter(self) -> None:
        self._app.banner.set_subtitle("Party  ›  Management")
        self._app.nav.hide()
        self._selected_character = None
        self._char_title.configure(text="Select a character")
        self._char_meta.configure(text="")
        self._player_var.set("")
        self._party_name_var.set("")
        self._filter_party_var.set(self.FILTER_ALL)
        self._selected_party_var.set(UNASSIGNED_PARTY)
        self._refresh_party_controls()
        self._refresh_characters()
        self._refresh_party_roster()


# ══════════════════════════════════════════════════════════════════════════════
# MAIN APPLICATION
# ══════════════════════════════════════════════════════════════════════════════

# Chargen screen order for direction inference
CHARGEN_ORDER = [
    "hub",
    "chargen_name",
    "chargen_abilities",
    "chargen_race",
    "chargen_class",
    "chargen_review",
]


class App:
    """Root application – owns the window and all screens."""

    def __init__(self) -> None:
        self.root = tk.Tk()
        self.root.title("Dungeon Master Cortex")
        self.root.geometry(f"{WIN_W}x{WIN_H}")
        self.root.resizable(False, False)
        self.root.configure(bg=C_BG)

        # Try to set a dark window title bar on Windows
        try:
            self.root.wm_attributes("-alpha", 1.0)
        except Exception:
            pass

        # Shared state
        self.rules    = _load_rules()
        self.builder  = CharacterBuilder(self.rules)
        self.chargen: Dict[str, Any] = {}
        self._characters_file = _characters_store_path()
        try:
            self.characters, self.parties = load_state(self._characters_file)
        except Exception:
            self.characters = []
            self.parties = [UNASSIGNED_PARTY]

        # Layout
        self.banner = TopBanner(self.root, self)
        self.banner.pack(side="top", fill="x")

        self.nav = BottomNav(self.root, self)
        self.nav.pack(side="bottom", fill="x")

        self.container = ScreenContainer(self.root)
        self.container.pack(side="top", fill="both", expand=True)

        # Register screens
        screens = {
            "hub":              HubScreen,
            "chargen_name":     CharGenNameScreen,
            "chargen_abilities": CharGenAbilitiesScreen,
            "chargen_race":     CharGenRaceScreen,
            "chargen_class":    CharGenClassScreen,
            "chargen_review":   CharGenReviewScreen,
            "dm_tools":         DMToolsScreen,
            "campaign":         CampaignScreen,
            "party_management": PartyManagementScreen,
        }
        for name, cls in screens.items():
            frame = cls(self.container, self)
            self.container.register(name, frame)

        # Show hub
        self.go("hub")

    def go(self, screen_name: str, direction: int = 1) -> None:
        """Navigate to a named screen with slide animation."""
        current = self.container.current()

        # Auto-infer direction if both screens are in the chargen flow
        if (current in CHARGEN_ORDER and screen_name in CHARGEN_ORDER):
            ci = CHARGEN_ORDER.index(current)
            ni = CHARGEN_ORDER.index(screen_name)
            direction = 1 if ni >= ci else -1

        # Show/hide nav bar for chargen steps
        chargen_screens = {"chargen_name", "chargen_abilities",
                           "chargen_race", "chargen_class", "chargen_review"}
        if screen_name in chargen_screens:
            self.nav.show()
        else:
            self.nav.hide()

        self.container.show(screen_name, direction)
        screen = self.container._screens[screen_name]
        if hasattr(screen, "on_enter"):
            screen.on_enter()

    def persist_characters(self) -> None:
        try:
            save_state(self._characters_file, self.characters, self.parties)
        except Exception as exc:
            messagebox.showerror(
                "Save Failed",
                f"Could not save characters:\n{exc}",
                parent=self.root,
            )

    def run(self) -> None:
        self.root.mainloop()


def launch() -> None:
    """Entry point called from app.py."""
    app = App()
    app.run()
