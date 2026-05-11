from __future__ import annotations

from dataclasses import asdict
from typing import Dict, List

from dmcortex.models import CampaignEntry, CampaignRulesConfig, CharacterRulesConfig


class CampaignManager:
    def __init__(self) -> None:
        self.entries: List[CampaignEntry] = []
        self.rules_config = CampaignRulesConfig()
        self.character_rules: Dict[str, CharacterRulesConfig] = {}

    def add_entry(self, entry: CampaignEntry) -> None:
        self.entries.append(entry)

    def by_session(self, session_id: str) -> List[CampaignEntry]:
        return [entry for entry in self.entries if entry.session_id == session_id]

    def set_campaign_profile(self, profile_id: str) -> None:
        self.rules_config.profile_id = profile_id

    def set_character_overlays(self, character_name: str, overlay_files: List[str]) -> None:
        self.character_rules[character_name] = CharacterRulesConfig(
            character_name=character_name,
            overlay_files=list(overlay_files),
        )

    def overlays_for_character(self, character_name: str) -> List[str]:
        config = self.character_rules.get(character_name)
        return list(config.overlay_files) if config else []

    def export(self) -> List[Dict[str, object]]:
        return [asdict(entry) for entry in self.entries]
