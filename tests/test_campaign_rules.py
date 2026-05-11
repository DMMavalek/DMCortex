from dmcortex.campaign import CampaignManager


def test_campaign_and_character_rule_toggles() -> None:
    manager = CampaignManager()

    manager.set_campaign_profile("core_plus_players_option")
    manager.set_character_overlays("Aelar", ["overlays/players_option.json"])

    assert manager.rules_config.profile_id == "core_plus_players_option"
    assert manager.overlays_for_character("Aelar") == ["overlays/players_option.json"]
