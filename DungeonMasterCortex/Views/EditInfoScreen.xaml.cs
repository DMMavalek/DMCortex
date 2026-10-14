using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

/// <summary>Display item for the NWP editor list. IsMissingPo drives the orange highlight.</summary>
internal record NwpListItem(string Display, bool IsMissingPo);

public partial class EditInfoScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    private static readonly string[] AbilityOrder  = { "str", "dex", "con", "int", "wis", "cha" };
    private static readonly string[] AbilityAbbrev = { "STR", "DEX", "CON", "INT", "WIS", "CHA" };
    private static readonly Dictionary<string, string[]> SubAbilitiesByAbility = new(StringComparer.OrdinalIgnoreCase)
    {
        ["str"] = new[] { "str_stamina", "str_muscle" },
        ["dex"] = new[] { "dex_aim", "dex_balance" },
        ["con"] = new[] { "con_health", "con_fitness" },
        ["int"] = new[] { "int_reason", "int_knowledge" },
        ["wis"] = new[] { "wis_intuition", "wis_willpower", "wis_perception" },
        ["cha"] = new[] { "cha_leadership", "cha_appearance" },
    };

    private static readonly Dictionary<string, string> CoreAbilityDisplayToKey = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Strength"] = "Strength",
        ["Dexterity"] = "Dexterity",
        ["Constitution"] = "Constitution",
        ["Intelligence"] = "Intelligence",
        ["Wisdom"] = "Wisdom",
        ["Charisma"] = "Charisma",
        ["None"] = "",
    };

    // Mapping between ComboBox display labels and internal CheckAbility keys for PO base abilities/sub-abilities.
    private static readonly Dictionary<string, string> PoAbilityDisplayToKey = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Strength"]       = "Strength",
        ["Str — Stamina"]  = "str_stamina",
        ["Str — Muscle"]   = "str_muscle",
        ["Dexterity"]      = "Dexterity",
        ["Dex — Aim"]      = "dex_aim",
        ["Dex — Balance"]  = "dex_balance",
        ["Constitution"]   = "Constitution",
        ["Con — Health"]   = "con_health",
        ["Con — Fitness"]  = "con_fitness",
        ["Intelligence"]   = "Intelligence",
        ["Int — Reason"]   = "int_reason",
        ["Int — Knowledge"]= "int_knowledge",
        ["Wisdom"]         = "Wisdom",
        ["Wis — Intuition"]= "wis_intuition",
        ["Wis — Willpower"]= "wis_willpower",
        ["Wis — Perception"]= "wis_perception",
        ["Charisma"]       = "Charisma",
        ["Cha — Leadership"]= "cha_leadership",
        ["Cha — Appearance"]= "cha_appearance",
        ["None"]           = "",
    };
    private static readonly Dictionary<string, string> CoreAbilityKeyToDisplay =
        CoreAbilityDisplayToKey.ToDictionary(kv => kv.Value, kv => kv.Key, System.StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> PoAbilityKeyToDisplay =
        PoAbilityDisplayToKey.ToDictionary(kv => kv.Value, kv => kv.Key, System.StringComparer.OrdinalIgnoreCase);

    private string _selectedRaceId   = "";
    private string _selectedNwpId    = "";
    private string _selectedTraitId  = "";
    private string _selectedDisadvantageId = "";
    private string _selectedKitId = "";
    private string _selectedClassId = "";
    private string _selectedDeityId = "";
    private string _selectedRaceAbilityId = "";
    private string _selectedMonsterId = "";
    private string _selectedSpellId = "";
    private string _activeSpellCategory = "arcane";
    private List<MonsterDefinition> _monsterItems = new();
    private List<SpellDefinition> _spellItems = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<DragonAgeStageVm> _ageStageRows = new();
    private bool _updatingNwpUi;
    private bool _updatingCharacterFaithUi;
    private bool _creatingNewNwp;
    private List<AbilityDefinition> _workingRacialAbilities = new();
    private int _selectedRacialAbilityIndex = -1;
    private List<AbilityDefinition> _workingClassAbilities = new();
    private int _selectedClassAbilityIndex = -1;
    private List<ClassDefinition> _classCopyTargets = new();
    private List<RaceDefinition> _raceCopyTargets = new();
    private readonly Dictionary<string, NonweaponProficiencySetting> _nwpSettings
        = new(System.StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, CustomNwpData> _customNwpIds = new(System.StringComparer.OrdinalIgnoreCase);
    private List<NonweaponProficiencyDefinition> _nwpItems = new();
    private List<TraitDefinition> _traitItems = new();
    private List<DisadvantageDefinition> _disadvantageItems = new();
    private List<KitDefinition> _kitItems = new();
    private List<CustomEquipmentData> _equipmentItems = new();
    private string _selectedEquipmentId = "";
    private const string EquipmentAllCategories = "(All Categories)";
    private const string CharacterInventoryAllCategories = "(All Categories)";
    private static readonly string[] CharacterInventorySortOptions =
    {
        "Name (A-Z)",
        "Name (Z-A)",
        "Quantity (High-Low)",
        "Cost (High-Low)",
    };
    private static readonly Regex CoinValueRegex = new(
        @"(?<amount>\d[\d,]*)\s*(?<coin>pp|gp|ep|sp|cp)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BareValueRegex = new(
        @"(?<low>\d[\d,]*)(?:\s*[-–]\s*(?<high>\d[\d,]*))?",
        RegexOptions.Compiled);
    private bool _showMagicalEquipment = false;
    private bool _attemptedAutoMagicalImport;
    private bool _demoViewOnlyNoticeShown;
    private string _activeCharacterEditorSubTab = "update";
    private string _editorRulesModeFilter = "core_rules";
    private readonly List<int> _visibleRacialAbilityIndices = new();
    private readonly List<int> _visibleClassAbilityIndices = new();
    private List<SnippetTargetOption> _raceSnippetTargets = new();
    private static readonly (string Key, string Default)[] GroupedMetadataFields =
    {
        ("ChargedItem", "No"),
        ("MaxCharges", "0"),
        ("ChargeCost", "0"),
        ("ChargeType", string.Empty),
        ("RechargeMethod", string.Empty),
        ("ActivationType", string.Empty),
        ("Duration", string.Empty),
        ("DurationType", string.Empty),
        ("DurationValue", string.Empty),
        ("Save_ParalyzationMod", "0"),
        ("Save_RSWMod", "0"),
        ("Save_PetrificationMod", "0"),
        ("Save_BreathMod", "0"),
        ("Save_SpellMod", "0"),
        ("Save_AllMod", "0"),
        ("PickPocketsMod", "0"),
        ("OpenLocksMod", "0"),
        ("FindRemoveTrapsMod", "0"),
        ("MoveSilentlyMod", "0"),
        ("HideInShadowsMod", "0"),
        ("DetectNoiseMod", "0"),
        ("ClimbWallsMod", "0"),
        ("ReadLanguagesMod", "0"),
        ("STR_Mod", "0"),
        ("DEX_Mod", "0"),
        ("CON_Mod", "0"),
        ("INT_Mod", "0"),
        ("WIS_Mod", "0"),
        ("CHA_Mod", "0"),
        ("STR_Set", string.Empty),
        ("STR_Exceptional_Set", string.Empty),
        ("DEX_Set", string.Empty),
        ("CON_Set", string.Empty),
        ("INT_Set", string.Empty),
        ("WIS_Set", string.Empty),
        ("CHA_Set", string.Empty),
    };
    private static readonly HashSet<string> GroupedNumericMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "MaxCharges", "ChargeCost",
        "Save_ParalyzationMod", "Save_RSWMod", "Save_PetrificationMod", "Save_BreathMod", "Save_SpellMod", "Save_AllMod",
        "PickPocketsMod", "OpenLocksMod", "FindRemoveTrapsMod", "MoveSilentlyMod", "HideInShadowsMod", "DetectNoiseMod", "ClimbWallsMod", "ReadLanguagesMod",
        "STR_Mod", "DEX_Mod", "CON_Mod", "INT_Mod", "WIS_Mod", "CHA_Mod",
        "EnhancementBonus", "AttackBonus", "DamageBonus", "THAC0Mod", "ACMod",
    };

    private sealed class HealingSpellOption
    {
        public SpellDefinition Spell { get; }
        public int SpellLevel { get; }
        public string Display { get; }

        public HealingSpellOption(SpellDefinition spell, int spellLevel, string display)
        {
            Spell = spell;
            SpellLevel = spellLevel;
            Display = display;
        }

        public override string ToString() => Display;
    }

    private sealed class HealingItemOption
    {
        public string ItemId { get; }
        public string Display { get; }

        public HealingItemOption(string itemId, string display)
        {
            ItemId = itemId;
            Display = display;
        }

        public override string ToString() => Display;
    }

    private sealed record ClassCopyTargetOption(string Id, string Name)
    {
        public override string ToString() => $"{Name} [{Id}]";
    }

    private sealed record RaceCopyTargetOption(string Id, string Name)
    {
        public override string ToString() => $"{Name} [{Id}]";
    }

    private sealed record SnippetTargetOption(string Key, string Label)
    {
        public override string ToString() => $"{Label} [{Key}]";
    }

    public EditInfoScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(HandleDemoViewOnlyButtonGuard), true);
        ShowTab("chars");
    }

    public void OnEnter()
    {
        _app.SetBanner(_app.License.IsDemoMode
            ? "DM Tools  ›  Edit Information (View Only Demo)"
            : "DM Tools  ›  Edit Information");

        if (_app.License.IsDemoMode && !_demoViewOnlyNoticeShown)
        {
            _demoViewOnlyNoticeShown = true;
            MessageBox.Show(
                "Demo mode is view only in Edit Information. You can browse data, but add/save/delete actions are locked.",
                "Demo Mode",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        var requestedTab = _app.PendingEditInfoTab;
        _app.PendingEditInfoTab = "";
        int pendingCharacterIndex = _app.PendingEditCharacterIndex;
        bool pendingLevelUpMode = _app.PendingCharacterLevelUpMode;
        _app.PendingEditCharacterIndex = -1;
        _app.PendingCharacterLevelUpMode = false;

        if (pendingCharacterIndex >= 0 && pendingCharacterIndex < _app.Characters.Count)
        {
            ShowTab("chars");
            CharList.SelectedIndex = pendingCharacterIndex;
            CharList.ScrollIntoView(CharList.Items[pendingCharacterIndex]);
            if (pendingLevelUpMode)
            {
                CharLevelupInfo.Text = "Opened from roster UPDATE. Add XP/HP/CP, then apply update.";
                CharXpGainInput.Focus();
            }
            return;
        }

        if (requestedTab is "chars" or "nwps" or "traits" or "disadvantages" or "equipment" or "equipmnet" or "races" or "racial_abilities" or "classes" or "class_abilities" or "deities" or "spells" or "spells_arcane" or "spells_divine" or "spells_psionic")
            ShowTab(requestedTab);
        else
            ShowTab("chars");
    }

    private void HandleDemoViewOnlyButtonGuard(object sender, RoutedEventArgs e)
    {
        if (!_app.License.IsDemoMode)
            return;

        if (e.OriginalSource is not Button button)
            return;

        var name = button.Name ?? string.Empty;
        if (name.StartsWith("Tab", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "BtnOpenRacialAbilityEditor", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "BtnHub", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Handled = true;
        MessageBox.Show(
            "This action is locked in demo mode. Activate to enable editing.",
            "Demo Mode",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void TabChars_Click(object sender, RoutedEventArgs e)     => ShowTab("chars");
    private void TabOptions_Click(object sender, RoutedEventArgs e)   => ShowTab("nwps");
    private void TabRaces_Click(object sender, RoutedEventArgs e)     => ShowTab("races");
    private void TabNwps_Click(object sender, RoutedEventArgs e)      => ShowTab("nwps");
    private void TabTraits_Click(object sender, RoutedEventArgs e)    => ShowTab("traits");
    private void TabDisadvantages_Click(object sender, RoutedEventArgs e) => ShowTab("disadvantages");
    private void TabBtnKits_Click(object sender, RoutedEventArgs e)       => ShowTab("kits");
    private void TabEquipmnet_Click(object sender, RoutedEventArgs e)  => ShowTab("equipmnet");
    private void TabBtnMultiClass_Click(object sender, RoutedEventArgs e) => ShowTab("multiclass");
    private void TabClasses_Click(object sender, RoutedEventArgs e)   => ShowTab("classes");
    private void TabDeities_Click(object sender, RoutedEventArgs e)  => ShowTab("deities");
    private void TabClassAbilities_Click(object sender, RoutedEventArgs e) => ShowTab("class_abilities");
    private void TabRacialAbilities_Click(object sender, RoutedEventArgs e) => ShowTab("racial_abilities");
    private void TabBtnSpells_Click(object sender, RoutedEventArgs e) => ShowTab("spells_arcane");
    private void TabSpellsArcane_Click(object sender, RoutedEventArgs e) => ShowTab("spells_arcane");
    private void TabSpellsDivine_Click(object sender, RoutedEventArgs e) => ShowTab("spells_divine");
    private void TabSpellsPsionic_Click(object sender, RoutedEventArgs e) => ShowTab("spells_psionic");
    private void TabBtnCharHealing_Click(object sender, RoutedEventArgs e) => ShowCharacterEditorSubTab("healing");
    private void TabBtnCharUpdate_Click(object sender, RoutedEventArgs e) => ShowCharacterEditorSubTab("update");

    private void RefreshDeityEditor()
    {
        DeityRulesEnabledCheck.IsChecked = _app.Rules.DeityRulesEnabled;
        var deities = _app.Rules.Deities.Values.OrderBy(deity => deity.Name).ToList();
        DeityEditorList.ItemsSource = deities;
        if (!string.IsNullOrWhiteSpace(_selectedDeityId))
        {
            int index = deities.FindIndex(deity => string.Equals(deity.Id, _selectedDeityId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                DeityEditorList.SelectedIndex = index;
        }
        if (DeityEditorList.SelectedIndex < 0 && deities.Count > 0)
            DeityEditorList.SelectedIndex = 0;
    }

    private void DeityEditorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeityEditorList.SelectedItem is not DeityDefinition deity)
            return;

        _selectedDeityId = deity.Id;
        DeityEditId.Text = deity.Id;
        DeityEditId.IsReadOnly = true;
        DeityEditName.Text = deity.Name;
        DeityEditAlignment.Text = deity.Alignment;
        DeityEditPortfolio.Text = deity.Portfolio;
        DeityEditFollowerAlignments.Text = string.Join(", ", deity.FollowerAlignments);
        DeityEditFollowerBenefit.Text = deity.FollowerAbility?.Description ?? string.Empty;
        DeityEditorInfo.Text = string.Empty;
    }

    private void BtnNewDeity_Click(object sender, RoutedEventArgs e)
    {
        _selectedDeityId = string.Empty;
        DeityEditorList.SelectedIndex = -1;
        DeityEditId.IsReadOnly = false;
        DeityEditId.Text = string.Empty;
        DeityEditName.Text = string.Empty;
        DeityEditAlignment.Text = string.Empty;
        DeityEditPortfolio.Text = string.Empty;
        DeityEditFollowerAlignments.Text = string.Empty;
        DeityEditFollowerBenefit.Text = string.Empty;
        DeityEditorInfo.Text = "Enter a stable lowercase ID, such as my_deity.";
        DeityEditId.Focus();
    }

    private void BtnSaveDeitySetting_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = DeityRulesEnabledCheck.IsChecked == true;
        _app.Rules.SetDeityRulesEnabled(enabled);
        DeityEditorInfo.Text = enabled
            ? "Custom deity rules and specialty-priest classes are enabled."
            : "Custom deity rules and specialty-priest classes are disabled for character creation.";
    }

    private void BtnSaveDeity_Click(object sender, RoutedEventArgs e)
    {
        string id = DeityEditId.Text.Trim().ToLowerInvariant();
        string name = DeityEditName.Text.Trim();
        string alignment = DeityEditAlignment.Text.Trim().ToUpperInvariant();
        string[] validAlignments = { "LG", "NG", "CG", "LN", "N", "CN", "LE", "NE", "CE" };
        if (!Regex.IsMatch(id, "^[a-z0-9]+(?:_[a-z0-9]+)*$") || string.IsNullOrWhiteSpace(name))
        {
            DeityEditorInfo.Text = "A name and lowercase ID containing letters, numbers, or underscores are required.";
            return;
        }
        if (!validAlignments.Contains(alignment, StringComparer.OrdinalIgnoreCase))
        {
            DeityEditorInfo.Text = "Deity alignment must be one of: LG, NG, CG, LN, N, CN, LE, NE, CE.";
            return;
        }

        var followerAlignments = DeityEditFollowerAlignments.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (followerAlignments.Count == 0 || followerAlignments.Any(value => !validAlignments.Contains(value, StringComparer.OrdinalIgnoreCase)))
        {
            DeityEditorInfo.Text = "Enter one or more valid follower alignments separated by commas.";
            return;
        }

        string benefit = DeityEditFollowerBenefit.Text.Trim();
        AbilityDefinition? followerAbility = string.IsNullOrWhiteSpace(benefit)
            ? null
            : new AbilityDefinition
            {
                Id = $"deity_{id}_follower",
                Description = benefit,
                AutoGranted = true,
            };
        _app.Rules.SaveDeity(new DeityDefinition(
            id,
            name,
            alignment,
            DeityEditPortfolio.Text.Trim(),
            followerAlignments,
            followerAbility));
        _selectedDeityId = id;
        RefreshDeityEditor();
        DeityEditorInfo.Text = $"Saved {name} as a local deity override.";
    }

    private void BtnOpenRacialAbilityEditor_Click(object sender, RoutedEventArgs e)
    {
        string raceId = RaceId.Text.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(raceId))
            _selectedRaceAbilityId = raceId;
        ShowTab("racial_abilities");
    }

    private void BtnInsertRaceAbilitySnippet_Click(object sender, RoutedEventArgs e)
    {
        if (RaceAbilityDefsSnippet.SelectedItem is not ComboBoxItem selected)
            return;

        string snippet = (selected.Tag?.ToString() ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(snippet))
            return;

        InsertRaceAbilitySnippetAtCursor(snippet);
    }

    private void RaceSnippetType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshRaceSnippetBuilderTargets();
    }

    private void RaceSnippetTargetSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyRaceSnippetTargetFilter();
    }

    private void BtnClearRaceSnippetTargetSearch_Click(object sender, RoutedEventArgs e)
    {
        if (RaceSnippetTargetSearch is null)
            return;

        RaceSnippetTargetSearch.Text = string.Empty;
        RaceSnippetTargetSearch.Focus();
    }

    private void RaceSnippetTargetSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        BtnBuildRaceAbilitySnippet_Click(sender, new RoutedEventArgs());
    }

    private void RaceSnippetValue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        BtnBuildRaceAbilitySnippet_Click(sender, new RoutedEventArgs());
    }

    private void BtnBuildRaceAbilitySnippet_Click(object sender, RoutedEventArgs e)
    {
        if (RaceSnippetTarget.SelectedItem is not SnippetTargetOption target)
        {
            MessageBox.Show("Select a target first.", "Guided Snippet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse((RaceSnippetValue.Text ?? string.Empty).Trim(), out int value))
        {
            MessageBox.Show("Value must be a whole number.", "Guided Snippet", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string type = GetSelectedRaceSnippetType();
        string snippet = type switch
        {
            "weapon_attack" => $"weapon_attack.{target.Key}={value}",
            "attack_scaling" => $"attack_per_levels={value}",
            "weapon_damage" => $"weapon_damage.{target.Key}={value}",
            "nwp_check_targeted" => $"nwp_check.{target.Key}={value}",
            "save_bonus" => $"save.{target.Key}={value}",
            "enemy_attack" => $"enemy_attack.{target.Key}={value}",
            "enemy_damage" => $"enemy_damage.{target.Key}={value}",
            _ => string.Empty,
        };

        if (string.IsNullOrWhiteSpace(snippet))
            return;

        InsertRaceAbilitySnippetAtCursor(snippet);
    }

    private string GetSelectedRaceSnippetType()
    {
        return (RaceSnippetType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "weapon_attack";
    }

    private void RefreshRaceSnippetBuilderTargets()
    {
        if (RaceSnippetTarget is null || RaceSnippetType is null)
            return;

        string snippetType = GetSelectedRaceSnippetType();
        _raceSnippetTargets = BuildRaceSnippetTargets(snippetType);
        ApplyRaceSnippetTargetFilter();
    }

    private void ApplyRaceSnippetTargetFilter()
    {
        if (RaceSnippetTarget is null)
            return;

        string? selectedKey = (RaceSnippetTarget.SelectedItem as SnippetTargetOption)?.Key;
        string filter = (RaceSnippetTargetSearch?.Text ?? string.Empty).Trim();

        var filtered = string.IsNullOrWhiteSpace(filter)
            ? _raceSnippetTargets
            : _raceSnippetTargets
                .Where(x => x.Key.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || x.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        RaceSnippetTarget.ItemsSource = filtered;

        if (!string.IsNullOrWhiteSpace(selectedKey))
        {
            int priorIdx = filtered.FindIndex(x => string.Equals(x.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
            if (priorIdx >= 0)
            {
                RaceSnippetTarget.SelectedIndex = priorIdx;
                return;
            }
        }

        RaceSnippetTarget.SelectedIndex = filtered.Count > 0 ? 0 : -1;
    }

    private List<SnippetTargetOption> BuildRaceSnippetTargets(string snippetType)
    {
        switch ((snippetType ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "weapon_attack":
            case "weapon_damage":
            {
                var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var g in _app.Rules.WeaponGroups.OrderBy(x => x.Name))
                {
                    if (!string.IsNullOrWhiteSpace(g.Id) && !byId.ContainsKey(g.Id))
                        byId[g.Id] = $"Group: {g.Name}";

                    foreach (var tg in g.TightGroups.OrderBy(x => x.Name))
                    {
                        if (!string.IsNullOrWhiteSpace(tg.Id) && !byId.ContainsKey(tg.Id))
                            byId[tg.Id] = $"Tight Group: {tg.Name}";
                    }
                }

                foreach (var w in _app.Rules.Weapons.OrderBy(x => x.Name))
                {
                    if (!string.IsNullOrWhiteSpace(w.Id) && !byId.ContainsKey(w.Id))
                        byId[w.Id] = $"Weapon: {w.Name}";
                }

                return byId
                    .OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new SnippetTargetOption(kv.Key, kv.Value))
                    .ToList();
            }
            case "attack_scaling":
                return new List<SnippetTargetOption> { new("attack_per_levels", "Attack bonus every X levels") };
            case "nwp_check_targeted":
            {
                var nwps = _app.CharacterOptions.GetCatalog().NonweaponProficiencies
                    .OrderBy(n => n.Name)
                    .Select(n => new SnippetTargetOption(n.Id, $"NWP: {n.Name}"))
                    .ToList();
                return nwps;
            }
            case "save_bonus":
                return new List<SnippetTargetOption>
                {
                    new("all", "Save: All"),
                    new("magic", "Save: Magic"),
                    new("poison", "Save: Poison"),
                    new("spell", "Save: Spell"),
                    new("death", "Save: Death"),
                    new("breath", "Save: Breath"),
                    new("polymorph", "Save: Polymorph"),
                    new("petrification", "Save: Petrification"),
                };
            case "enemy_attack":
            case "enemy_damage":
                return new List<SnippetTargetOption>
                {
                    new("goblinoids", "Enemy: Goblinoids"),
                    new("undead", "Enemy: Undead"),
                    new("giants", "Enemy: Giants"),
                    new("orcs", "Enemy: Orcs"),
                    new("drow", "Enemy: Drow"),
                    new("troglodytes", "Enemy: Troglodytes"),
                    new("dragons", "Enemy: Dragons"),
                    new("humanoids", "Enemy: Humanoids"),
                };
            default:
                return new List<SnippetTargetOption>();
        }
    }

    private void InsertRaceAbilitySnippetAtCursor(string snippet)
    {
        string text = RaceAbilityDefs.Text ?? string.Empty;
        int caret = Math.Clamp(RaceAbilityDefs.CaretIndex, 0, text.Length);
        bool isFullLine = snippet.Contains('|');

        string insertion = snippet;
        if (isFullLine)
        {
            bool needsLeadingNewline = caret > 0
                && text[caret - 1] != '\n'
                && text[caret - 1] != '\r';
            bool needsTrailingNewline = caret < text.Length
                && text[caret] != '\n'
                && text[caret] != '\r';

            insertion = (needsLeadingNewline ? "\n" : string.Empty)
                + snippet
                + (needsTrailingNewline ? "\n" : string.Empty);
        }
        else
        {
            bool addSeparator = caret > 0
                && !char.IsWhiteSpace(text[caret - 1])
                && text[caret - 1] != ';'
                && text[caret - 1] != ','
                && text[caret - 1] != '|';

            insertion = (addSeparator ? ";" : string.Empty) + snippet;
        }

        RaceAbilityDefs.Text = text.Insert(caret, insertion);
        RaceAbilityDefs.CaretIndex = caret + insertion.Length;
        RaceAbilityDefs.Focus();
    }

    private void ShowTab(string tab)
    {
        if (tab == "equipment")
            tab = "equipmnet";
        if (tab == "spells")
            tab = "spells_arcane";

        bool inOptions = tab is "nwps" or "traits" or "disadvantages" or "kits" or "multiclass";
        if (inOptions && IsCoreFilter() && (tab == "traits" || tab == "disadvantages"))
            tab = "nwps";
        bool inRaces = tab is "races" or "racial_abilities";
        bool inClasses = tab is "classes" or "class_abilities";
        bool inSpells = tab is "spells_arcane" or "spells_divine" or "spells_psionic";

        if (inSpells)
        {
            string nextSpellCategory = tab switch
            {
                "spells_divine" => "divine",
                "spells_psionic" => "psionic",
                _ => "arcane",
            };

            if (!string.Equals(_activeSpellCategory, nextSpellCategory, System.StringComparison.OrdinalIgnoreCase))
            {
                _activeSpellCategory = nextSpellCategory;
                var selectedSpell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, _selectedSpellId, System.StringComparison.OrdinalIgnoreCase));
                if (selectedSpell is null || !string.Equals(selectedSpell.Category, _activeSpellCategory, System.StringComparison.OrdinalIgnoreCase))
                {
                    _selectedSpellId = string.Empty;
                    ClearSpellEditor();
                }
            }
        }

        TabChars.Visibility     = tab == "chars"     ? Visibility.Visible : Visibility.Collapsed;
        TabRaces.Visibility     = tab == "races"     ? Visibility.Visible : Visibility.Collapsed;
        TabNwps.Visibility      = tab == "nwps"      ? Visibility.Visible : Visibility.Collapsed;
        TabTraits.Visibility    = tab == "traits"    ? Visibility.Visible : Visibility.Collapsed;
        TabDisadvantages.Visibility = tab == "disadvantages" ? Visibility.Visible : Visibility.Collapsed;
        TabKits.Visibility          = tab == "kits"          ? Visibility.Visible : Visibility.Collapsed;
        TabEquipment.Visibility = tab == "equipmnet" ? Visibility.Visible : Visibility.Collapsed;
        TabMultiClass.Visibility    = tab == "multiclass"     ? Visibility.Visible : Visibility.Collapsed;
        TabClasses.Visibility = tab == "classes" ? Visibility.Visible : Visibility.Collapsed;
        TabDeities.Visibility = tab == "deities" ? Visibility.Visible : Visibility.Collapsed;
        TabClassAbilities.Visibility = tab == "class_abilities" ? Visibility.Visible : Visibility.Collapsed;
        TabRacialAbilities.Visibility = tab == "racial_abilities" ? Visibility.Visible : Visibility.Collapsed;
        TabMonsters.Visibility = tab == "monsters" ? Visibility.Visible : Visibility.Collapsed;
        TabSpells.Visibility = inSpells ? Visibility.Visible : Visibility.Collapsed;

        OptionsSubTabStrip.Visibility = inOptions ? Visibility.Visible : Visibility.Collapsed;
        RacesSubTabStrip.Visibility = inRaces ? Visibility.Visible : Visibility.Collapsed;
        ClassesSubTabStrip.Visibility = inClasses ? Visibility.Visible : Visibility.Collapsed;
        RulesModeFilterStrip.Visibility = (inRaces || inClasses || inOptions) ? Visibility.Visible : Visibility.Collapsed;
        SpellsSubTabStrip.Visibility = inSpells ? Visibility.Visible : Visibility.Collapsed;
        UpdateRulesModeToggleUi();

        bool poOptionsEnabled = !IsCoreFilter();
        TabBtnTraits.Visibility = poOptionsEnabled ? Visibility.Visible : Visibility.Collapsed;
        TabBtnDisadvantages.Visibility = poOptionsEnabled ? Visibility.Visible : Visibility.Collapsed;

        var active   = (System.Windows.Media.Brush)FindResource("BrushBtnAct");
        var inactive = (System.Windows.Media.Brush)FindResource("BrushBtn");
        TabBtnChars.Background     = tab == "chars" ? active : inactive;
        TabBtnOptions.Background   = inOptions ? active : inactive;
        TabBtnEquipmnet.Background = tab == "equipmnet" ? active : inactive;
        TabBtnRaces.Background     = inRaces ? active : inactive;
        TabBtnClasses.Background   = inClasses ? active : inactive;
        TabBtnDeities.Background   = tab == "deities" ? active : inactive;
        TabBtnMonsters.Background  = tab == "monsters" ? active : inactive;
        TabBtnSpells.Background    = inSpells ? active : inactive;
        TabBtnNwps.Background      = tab == "nwps" ? active : inactive;
        TabBtnTraits.Background    = tab == "traits" ? active : inactive;
        TabBtnDisadvantages.Background = tab == "disadvantages" ? active : inactive;
        TabBtnKits.Background          = tab == "kits"          ? active : inactive;
        TabSubBtnRaces.Background  = tab == "races" ? active : inactive;
        TabBtnMultiClass.Background    = tab == "multiclass"     ? active : inactive;
        TabBtnRacialAbilities.Background = tab == "racial_abilities" ? active : inactive;
        TabSubBtnClasses.Background = tab == "classes" ? active : inactive;
        TabBtnClassAbilities.Background = tab == "class_abilities" ? active : inactive;
        TabBtnSpellsArcane.Background = tab == "spells_arcane" ? active : inactive;
        TabBtnSpellsDivine.Background = tab == "spells_divine" ? active : inactive;
        TabBtnSpellsPsionic.Background = tab == "spells_psionic" ? active : inactive;

        if (tab == "chars")     RefreshCharList();
        if (tab == "chars")     ShowCharacterEditorSubTab(_activeCharacterEditorSubTab);
        if (tab == "races")     RefreshRaceList();
        if (tab == "races")     RefreshRaceSnippetBuilderTargets();
        if (tab == "nwps")      RefreshNwpList();
        if (tab == "traits")    RefreshTraitEditorList();
        if (tab == "disadvantages") RefreshDisadvantageEditorList();
        if (tab == "kits")          RefreshKitEditorList();
        if (tab == "equipmnet") RefreshEquipmentEditor();
        if (tab == "multiclass")    RefreshMultiClassEditor();
        if (tab == "classes") RefreshClassEditorList();
        if (tab == "deities") RefreshDeityEditor();
        if (tab == "class_abilities") RefreshClassAbilityEditorList();
        if (tab == "racial_abilities") RefreshRacialAbilityEditorList();
        if (tab == "monsters")
        {
            // Always open Monsters tab in an unfiltered state to avoid hidden subsets.
            if (!string.IsNullOrEmpty(MonsterSearchBox.Text))
                MonsterSearchBox.Text = string.Empty;

            if (MonsterTypeFilter.Items.Count > 0)
            {
                int allTypesIndex = MonsterTypeFilter.Items.IndexOf("(All Types)");
                MonsterTypeFilter.SelectedIndex = allTypesIndex >= 0 ? allTypesIndex : 0;
            }

            RefreshMonsterEditor();
        }
        if (inSpells)
            RefreshSpellEditor();
    }

    private void RulesModeToggle_Changed(object sender, RoutedEventArgs e)
    {
        _editorRulesModeFilter = RulesModeToggle.IsChecked == true ? "players_option" : "core_rules";
        UpdateRulesModeToggleUi();
        RefreshModeFilteredEditors();
    }

    private void UpdateRulesModeToggleUi()
    {
        if (RulesModeToggle == null) return;
        RulesModeToggle.Content = string.Equals(_editorRulesModeFilter, "players_option", System.StringComparison.OrdinalIgnoreCase)
            ? "Showing Player's Option"
            : "Showing Core Rules";
    }

    private void RefreshModeFilteredEditors()
    {
        if (TabNwps.Visibility == Visibility.Visible)
            RefreshNwpList();
        if (TabKits.Visibility == Visibility.Visible)
            RefreshKitEditorList();
        if (TabTraits.Visibility == Visibility.Visible && IsCoreFilter())
            ShowTab("nwps");
        if (TabDisadvantages.Visibility == Visibility.Visible && IsCoreFilter())
            ShowTab("nwps");
        if (TabRaces.Visibility == Visibility.Visible)
            RefreshRaceList();
        if (TabRacialAbilities.Visibility == Visibility.Visible)
            RefreshRacialAbilityEditorList();
        if (TabClasses.Visibility == Visibility.Visible)
            RefreshClassEditorList();
        if (TabClassAbilities.Visibility == Visibility.Visible)
            RefreshClassAbilityEditorList();
    }

    private bool IsCoreFilter()
        => !string.Equals(_editorRulesModeFilter, "players_option", System.StringComparison.OrdinalIgnoreCase);

    private bool RaceMatchesEditorFilter(RaceDefinition race)
    {
        var mode = (race.CharacterMode ?? string.Empty).Trim().ToLowerInvariant();
        if (mode == "all") return true;

        return IsCoreFilter()
            ? mode == "core_rules"
            : mode == "players_option";
    }

    private List<RaceDefinition> GetOrderedRacesForEditor()
        => _app.Rules.Races.Values
            .Where(RaceMatchesEditorFilter)
            .OrderBy(r => r.Name)
            .ToList();

    private static bool IsCoreAbilityEntry(AbilityDefinition a)
        => a.AutoGranted && a.PointCost <= 0;

    private static bool IsPlayersOptionAbilityEntry(AbilityDefinition a)
        => !a.AutoGranted || a.PointCost > 0;

    private string ResolveClassRulesModeForEditor(ClassDefinition cls)
    {
        var mode = (cls.RulesMode ?? string.Empty).Trim().ToLowerInvariant();
        if (mode == "core_rules" || mode == "players_option" || mode == "all")
            return mode;

        bool hasCore = cls.StructuredAbilities.Any(IsCoreAbilityEntry);
        bool hasPlayersOption = cls.ClassPointBudget > 0 || cls.StructuredAbilities.Any(IsPlayersOptionAbilityEntry);
        if (hasCore && !hasPlayersOption) return "core_rules";
        if (!hasCore && hasPlayersOption) return "players_option";
        return "all";
    }

    private static string StripLeadingLevelPrefix(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        return System.Text.RegularExpressions.Regex
            .Replace(description.Trim(), @"^\s*level\s+\d+\s*:\s*", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private bool ClassMatchesEditorFilter(ClassDefinition cls)
    {
        var mode = ResolveClassRulesModeForEditor(cls);
        if (mode == "all") return true;
        return IsCoreFilter() ? mode == "core_rules" : mode == "players_option";
    }

    private bool NwpMatchesRulesModeFilter(NonweaponProficiencyDefinition nwp)
    {
        var source = (nwp.Source ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(source)) return true;

        bool hasCore = string.Equals(source, "Core", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "Core & Player's Option", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "Custom", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "Supplement", System.StringComparison.OrdinalIgnoreCase);

        bool hasPlayersOption = string.Equals(source, "Player's Option", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "Core & Player's Option", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "Custom", System.StringComparison.OrdinalIgnoreCase);

        return IsCoreFilter() ? hasCore : hasPlayersOption;
    }

    private bool KitMatchesRulesModeFilter(KitDefinition kit)
    {
        var mode = (kit.RulesMode ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(mode)) mode = "all";

        if (mode == "all") return true;
        if (IsCoreFilter()) return mode == "core_rules";
        return mode == "players_option";
    }

    private static void SelectKitRulesMode(ComboBox box, string mode)
    {
        string normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized != "core_rules" && normalized != "players_option" && normalized != "all")
            normalized = "all";

        foreach (var item in box.Items)
        {
            if (item is ComboBoxItem combo && string.Equals(combo.Tag as string, normalized, System.StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = combo;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    private static string SelectedKitRulesMode(ComboBox box)
    {
        if (box.SelectedItem is ComboBoxItem combo && combo.Tag is string tag)
            return tag;
        return "all";
    }

    private static string KitRulesModeLabel(string mode) => (mode ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "core_rules" => "Core",
        "players_option" => "PO",
        _ => "All",
    };

    private static bool IsPlayersOptionCharacter(CharacterSheet c)
        => string.Equals((c.CharacterMode ?? string.Empty).Trim(), "players_option", StringComparison.OrdinalIgnoreCase);

    private void ShowCharacterEditorSubTab(string subTab)
    {
        _activeCharacterEditorSubTab = string.Equals(subTab, "healing", System.StringComparison.OrdinalIgnoreCase)
            ? "healing"
            : "update";

        bool showHealing = _activeCharacterEditorSubTab == "healing";
        CharHealingPanel.Visibility = showHealing ? Visibility.Visible : Visibility.Collapsed;
        CharUpdatePanel.Visibility = showHealing ? Visibility.Collapsed : Visibility.Visible;
        CharNotesPanel.Visibility = showHealing ? Visibility.Collapsed : Visibility.Visible;
        CharNotesActionsPanel.Visibility = showHealing ? Visibility.Collapsed : Visibility.Visible;

        var active = (System.Windows.Media.Brush)FindResource("BrushBtnAct");
        var inactive = (System.Windows.Media.Brush)FindResource("BrushBtn");
        TabBtnCharHealing.Background = showHealing ? active : inactive;
        TabBtnCharUpdate.Background = showHealing ? inactive : active;
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) =>
        _app.GoTo("dm_tools", -1);

    private void BtnReportBug_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var dialog = new BugReportDialog(_app, _app.License)
        {
            Owner = owner,
        };

        dialog.ShowDialog();
    }

    // ── Equipment tab ────────────────────────────────────────────────────────

    private sealed record EquipmentListItem(CustomEquipmentData Item)
    {
        public override string ToString()
        {
            string cost = FormatCost(Item);
            return string.IsNullOrWhiteSpace(cost)
                ? Item.Name
                : $"{Item.Name} ({cost})";
        }

        private static string FormatCost(CustomEquipmentData item)
        {
            var parts = new List<string>();
            if (item.CostGold > 0) parts.Add($"{item.CostGold}gp");
            if (item.CostSilver > 0) parts.Add($"{item.CostSilver}sp");
            if (item.CostCopper > 0) parts.Add($"{item.CostCopper}cp");
            return string.Join(" ", parts);
        }
    }

    private void RefreshEquipmentEditor()
    {
        _equipmentItems = LoadEquipmentItemsWithMagicalFallback(out string? magicalImportNote);
        EquipmentEditorInfo.Text = string.IsNullOrWhiteSpace(magicalImportNote)
            ? "Edit equipment definitions here. Categories and tags are separate from item names."
            : $"Edit equipment definitions here. Categories and tags are separate from item names. {magicalImportNote}";
        ApplyEquipmentTypeTabStyles();
        PopulateEquipmentCategoryFilter();
        RefreshEquipmentList();
        if (EquipmentListEditor.SelectedItem is not EquipmentListItem && _equipmentItems.Count > 0)
            EquipmentListEditor.SelectedIndex = 0;
        if (_equipmentItems.Count == 0)
            NewEquipmentEditorEntry();
    }

    private List<CustomEquipmentData> LoadEquipmentItemsWithMagicalFallback(out string? magicalImportNote)
    {
        magicalImportNote = null;
        var libraryService = new EquipmentLibraryService();
        var library = libraryService.GetEquipmentLibrary().ToList();

        if (library.Any(x => x.IsMagical) || _attemptedAutoMagicalImport)
            return library;

        _attemptedAutoMagicalImport = true;
        string? workbookPath = FindMagicItemsWorkbookPath();
        if (string.IsNullOrWhiteSpace(workbookPath))
        {
            magicalImportNote = "Magical workbook not found for auto-import.";
            return library;
        }

        try
        {
            var result = new MagicalItemImportService().ImportFromWorkbook(workbookPath);
            magicalImportNote = $"Auto-imported magical items from {Path.GetFileName(workbookPath)} (imported {result.ImportedRows}, created {result.CreatedRows}, updated {result.UpdatedRows}).";
            return libraryService.GetEquipmentLibrary().ToList();
        }
        catch (Exception ex)
        {
            magicalImportNote = $"Magical auto-import failed: {ex.Message}";
            return library;
        }
    }

    private static string? FindMagicItemsWorkbookPath()
    {
        foreach (var startPath in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
                     .Where(p => !string.IsNullOrWhiteSpace(p))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dir = new DirectoryInfo(startPath);
            while (dir is not null)
            {
                string candidate = Path.Combine(dir.FullName, "Assets", "Magical Items", "Magic Items.xlsx");
                if (File.Exists(candidate))
                    return candidate;

                dir = dir.Parent;
            }
        }

        return null;
    }

    private void PopulateEquipmentCategoryFilter()
    {
        var categories = new List<string> { EquipmentAllCategories };
        categories.AddRange(_equipmentItems
            .Where(x => x.IsMagical == _showMagicalEquipment)
            .SelectMany(x => x.Categories)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(x.ToLowerInvariant()))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase));

        string previous = EquipmentCategoryFilter.SelectedItem as string ?? EquipmentAllCategories;
        EquipmentCategoryFilter.ItemsSource = categories;
        EquipmentCategoryFilter.SelectedItem = categories.Contains(previous) ? previous : EquipmentAllCategories;
    }

    private void RefreshEquipmentList()
    {
        string search = (EquipmentSearchBox?.Text ?? string.Empty).Trim();
        string category = EquipmentCategoryFilter.SelectedItem as string ?? EquipmentAllCategories;

        var filtered = _equipmentItems
            .Where(item => item.IsMagical == _showMagicalEquipment)
            .Where(item => category == EquipmentAllCategories
                || item.Categories.Contains(category, System.StringComparer.OrdinalIgnoreCase))
            .Where(item => string.IsNullOrWhiteSpace(search)
                || item.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                || item.Categories.Any(c => c.Contains(search, System.StringComparison.OrdinalIgnoreCase))
                || item.ItemTags.Any(t => t.Contains(search, System.StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Name, System.StringComparer.OrdinalIgnoreCase)
            .Select(item => new EquipmentListItem(item))
            .ToList();

        EquipmentListEditor.ItemsSource = filtered;

        if (!string.IsNullOrWhiteSpace(_selectedEquipmentId))
        {
            var selected = filtered.FirstOrDefault(x => string.Equals(x.Item.Id, _selectedEquipmentId, System.StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
                EquipmentListEditor.SelectedItem = selected;
        }
    }

    private void EquipmentListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EquipmentListEditor.SelectedItem is not EquipmentListItem selected)
        {
            BtnDeleteEquipment.IsEnabled = false;
            return;
        }

        BtnDeleteEquipment.IsEnabled = true;
        _selectedEquipmentId = selected.Item.Id;
        LoadEquipmentEditor(selected.Item);
    }

    private void LoadEquipmentEditor(CustomEquipmentData item)
    {
        EquipmentEditorTitle.Text = $"Equipment Editor  —  {item.Name}";
        EquipmentEditId.Text = item.Id;
        EquipmentEditName.Text = item.Name;
        EquipmentEditCategories.Text = string.Join(", ", item.Categories);
        EquipmentEditTags.Text = string.Join(", ", item.ItemTags);
        EquipmentIsMagical.IsChecked = item.IsMagical;
        EquipmentEditDescription.Text = item.Description;
        EquipmentEditWeight.Text = item.Weight.ToString(CultureInfo.InvariantCulture);
        EquipmentEditCostGold.Text = item.CostGold.ToString(CultureInfo.InvariantCulture);
        EquipmentEditCostSilver.Text = item.CostSilver.ToString(CultureInfo.InvariantCulture);
        EquipmentEditCostCopper.Text = item.CostCopper.ToString(CultureInfo.InvariantCulture);
        EquipmentIsArmor.IsChecked = item.IsArmor;
        EquipmentArmorClassValue.Text = item.ArmorClassValue.ToString(CultureInfo.InvariantCulture);
        SelectComboBoxItemByTag(EquipmentRogueArmorProfile, item.RogueArmorProfile);
        EquipmentIsWeapon.IsChecked = item.IsWeapon;
        EquipmentWeaponSpeed.Text = item.WeaponSpeed.ToString(CultureInfo.InvariantCulture);
        EquipmentWeaponDamageSmMd.Text = item.WeaponDamageSmallMedium;
        EquipmentWeaponDamageLarge.Text = item.WeaponDamageLarge;
        SelectComboBoxItemByTag(EquipmentWeaponType, item.WeaponType);
        SelectComboBoxItemByTag(EquipmentWeaponSize, item.WeaponSize);
        EquipmentMagicEnhancementBonus.Text = item.EnchantmentBonus.ToString(CultureInfo.InvariantCulture);
        EquipmentMagicAttackBonus.Text = item.WeaponAttackBonus.ToString(CultureInfo.InvariantCulture);
        EquipmentMagicDamageBonus.Text = item.WeaponDamageBonus.ToString(CultureInfo.InvariantCulture);
        EquipmentMagicThac0Modifier.Text = item.WeaponThac0Modifier.ToString(CultureInfo.InvariantCulture);
        EquipmentMagicArmorClassModifier.Text = item.ArmorClassModifier.ToString(CultureInfo.InvariantCulture);
        var editableMetadata = BuildEditableMetadata(item);
        PopulateGroupedMetadataEditors(editableMetadata);
        EquipmentMagicMetadataText.Text = FormatMetadataLines(editableMetadata);

        SelectComboBoxItemByText(EquipmentEditSize, item.SizeClass);

        EquipmentIsContainer.IsChecked = item.IsContainer;
        EquipmentContainerMaxItems.Text = item.ContainerMaxItems.ToString(CultureInfo.InvariantCulture);
        EquipmentContainerMaxWeight.Text = item.ContainerMaxWeight.ToString(CultureInfo.InvariantCulture);
        EquipmentAllowedTags.Text = string.Join(", ", item.AllowedContentTags);
        UpdateArmorEditorEnabledState();
        UpdateWeaponEditorEnabledState();
        UpdateContainerEditorEnabledState();
    }

    private void NewEquipmentEditorEntry()
    {
        _selectedEquipmentId = "";
        BtnDeleteEquipment.IsEnabled = false;
        EquipmentEditorTitle.Text = "Equipment Editor  —  New Item";
        EquipmentEditId.Text = "(new)";
        EquipmentEditName.Text = "";
        EquipmentEditCategories.Text = "Adventuring Gear";
        EquipmentEditTags.Text = "";
        EquipmentIsMagical.IsChecked = _showMagicalEquipment;
        EquipmentEditDescription.Text = "";
        EquipmentEditWeight.Text = "0";
        EquipmentEditCostGold.Text = "0";
        EquipmentEditCostSilver.Text = "0";
        EquipmentEditCostCopper.Text = "0";
        EquipmentIsArmor.IsChecked = false;
        EquipmentArmorClassValue.Text = "10";
        SelectComboBoxItemByTag(EquipmentRogueArmorProfile, "no_armor");
        EquipmentIsWeapon.IsChecked = false;
        EquipmentWeaponSpeed.Text = "0";
        EquipmentWeaponDamageSmMd.Text = "";
        EquipmentWeaponDamageLarge.Text = "";
        SelectComboBoxItemByTag(EquipmentWeaponType, "");
        SelectComboBoxItemByTag(EquipmentWeaponSize, "");
        SelectComboBoxItemByText(EquipmentEditSize, "Medium");
        EquipmentIsContainer.IsChecked = false;
        EquipmentContainerMaxItems.Text = "0";
        EquipmentContainerMaxWeight.Text = "0";
        EquipmentAllowedTags.Text = "";
        EquipmentMagicEnhancementBonus.Text = "0";
        EquipmentMagicAttackBonus.Text = "0";
        EquipmentMagicDamageBonus.Text = "0";
        EquipmentMagicThac0Modifier.Text = "0";
        EquipmentMagicArmorClassModifier.Text = "0";
        ResetGroupedMetadataEditors();
        EquipmentMagicMetadataText.Text = string.Empty;
        UpdateArmorEditorEnabledState();
        UpdateWeaponEditorEnabledState();
        UpdateContainerEditorEnabledState();
    }

    private void EquipmentCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RefreshEquipmentList();

    private void EquipmentRegularTabBtn_Click(object sender, RoutedEventArgs e)
    {
        _showMagicalEquipment = false;
        ApplyEquipmentTypeTabStyles();
        PopulateEquipmentCategoryFilter();
        RefreshEquipmentList();
        NewEquipmentEditorEntry();
    }

    private void EquipmentMagicalTabBtn_Click(object sender, RoutedEventArgs e)
    {
        _showMagicalEquipment = true;
        ApplyEquipmentTypeTabStyles();
        PopulateEquipmentCategoryFilter();
        RefreshEquipmentList();
        NewEquipmentEditorEntry();
    }

    private void EquipmentTreasureTabBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowTreasureTableEditor();
    }

    private void ApplyEquipmentTypeTabStyles()
    {
        var active = (System.Windows.Media.Brush)FindResource("BrushBtnAct");
        var inactive = (System.Windows.Media.Brush)FindResource("BrushBtn");
        EquipmentRegularTabBtn.Background = _showMagicalEquipment ? inactive : active;
        EquipmentMagicalTabBtn.Background = _showMagicalEquipment ? active : inactive;
        EquipmentTreasureTabBtn.Background = inactive;
    }

    private sealed class TreasureTableEditorRow
    {
        public string Type { get; set; } = string.Empty;
        public string Copper { get; set; } = string.Empty;
        public string CopperChance { get; set; } = string.Empty;
        public string Silver { get; set; } = string.Empty;
        public string SilverChance { get; set; } = string.Empty;
        public string Electrum { get; set; } = string.Empty;
        public string ElectrumChance { get; set; } = string.Empty;
        public string Gold { get; set; } = string.Empty;
        public string GoldChance { get; set; } = string.Empty;
        public string Platinum { get; set; } = string.Empty;
        public string PlatinumChance { get; set; } = string.Empty;
        public string Gems { get; set; } = string.Empty;
        public string GemsChance { get; set; } = string.Empty;
        public string Jewelry { get; set; } = string.Empty;
        public string JewelryChance { get; set; } = string.Empty;
        public string MagicItems { get; set; } = string.Empty;
        public string MagicItemsChance { get; set; } = string.Empty;
    }

    private void ShowTreasureTableEditor()
    {
        var allTables = DmgTreasureTable.GetAllTables();

        var window = new Window
        {
            Title = "Treasure Table Editor",
            Width = 1320,
            Height = 860,
            MinWidth = 1120,
            MinHeight = 680,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            Owner = Window.GetWindow(this),
        };

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var help = new TextBlock
        {
            Text = "Edit DMG treasure table entries (A-Z): amounts and percentile availability chances.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(help);
        Grid.SetRow(help, 0);

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = false,
            ItemsSource = allTables
                .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kvp => new TreasureTableEditorRow
                {
                    Type = kvp.Key,
                    Copper = kvp.Value.Copper,
                    CopperChance = kvp.Value.CopperChance,
                    Silver = kvp.Value.Silver,
                    SilverChance = kvp.Value.SilverChance,
                    Electrum = kvp.Value.Electrum,
                    ElectrumChance = kvp.Value.ElectrumChance,
                    Gold = kvp.Value.Gold,
                    GoldChance = kvp.Value.GoldChance,
                    Platinum = kvp.Value.Platinum,
                    PlatinumChance = kvp.Value.PlatinumChance,
                    Gems = kvp.Value.Gems,
                    GemsChance = kvp.Value.GemsChance,
                    Jewelry = kvp.Value.Jewelry,
                    JewelryChance = kvp.Value.JewelryChance,
                    MagicItems = kvp.Value.MagicItems
                    ,MagicItemsChance = kvp.Value.MagicItemsChance
                })
                .ToList()
        };

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Type",
            Binding = new System.Windows.Data.Binding("Type"),
            IsReadOnly = true,
            Width = new DataGridLength(70)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "CP",
            Binding = new System.Windows.Data.Binding("Copper"),
            Width = new DataGridLength(90)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "CP %",
            Binding = new System.Windows.Data.Binding("CopperChance"),
            Width = new DataGridLength(70)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "SP",
            Binding = new System.Windows.Data.Binding("Silver"),
            Width = new DataGridLength(90)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "SP %",
            Binding = new System.Windows.Data.Binding("SilverChance"),
            Width = new DataGridLength(70)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "EP",
            Binding = new System.Windows.Data.Binding("Electrum"),
            Width = new DataGridLength(90)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "EP %",
            Binding = new System.Windows.Data.Binding("ElectrumChance"),
            Width = new DataGridLength(70)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "GP",
            Binding = new System.Windows.Data.Binding("Gold"),
            Width = new DataGridLength(90)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "GP %",
            Binding = new System.Windows.Data.Binding("GoldChance"),
            Width = new DataGridLength(70)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "PP",
            Binding = new System.Windows.Data.Binding("Platinum"),
            Width = new DataGridLength(90)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "PP %",
            Binding = new System.Windows.Data.Binding("PlatinumChance"),
            Width = new DataGridLength(70)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Gems",
            Binding = new System.Windows.Data.Binding("Gems"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Gems %",
            Binding = new System.Windows.Data.Binding("GemsChance"),
            Width = new DataGridLength(70)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Jewelry",
            Binding = new System.Windows.Data.Binding("Jewelry"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Art %",
            Binding = new System.Windows.Data.Binding("JewelryChance"),
            Width = new DataGridLength(70)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Magic Items",
            Binding = new System.Windows.Data.Binding("MagicItems"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Magic %",
            Binding = new System.Windows.Data.Binding("MagicItemsChance"),
            Width = new DataGridLength(80)
        });

        root.Children.Add(grid);
        Grid.SetRow(grid, 1);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var saveBtn = new Button { Content = "Save", Width = 110, Margin = new Thickness(0, 0, 8, 0) };
        saveBtn.Click += (_, _) =>
        {
            if (grid.ItemsSource is not List<TreasureTableEditorRow> rows)
                return;

            int updatedCount = 0;
            foreach (var row in rows)
            {
                if (DmgTreasureTable.TryUpdateTreasureTable(
                    row.Type,
                    new DmgTreasureRoll
                    {
                        Copper = row.Copper,
                        CopperChance = row.CopperChance,
                        Silver = row.Silver,
                        SilverChance = row.SilverChance,
                        Electrum = row.Electrum,
                        ElectrumChance = row.ElectrumChance,
                        Gold = row.Gold,
                        GoldChance = row.GoldChance,
                        Platinum = row.Platinum,
                        PlatinumChance = row.PlatinumChance,
                        Gems = row.Gems,
                        GemsChance = row.GemsChance,
                        Jewelry = row.Jewelry,
                        JewelryChance = row.JewelryChance,
                        MagicItems = row.MagicItems,
                        MagicItemsChance = row.MagicItemsChance,
                    }))
                    updatedCount++;
            }

            EquipmentEditorInfo.Text = $"Saved {updatedCount} treasure table entries for this session.";
            MessageBox.Show(
                $"Saved {updatedCount} treasure table entries for this session.",
                "Treasure Table",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        };
        footer.Children.Add(saveBtn);

        var closeBtn = new Button { Content = "Close", Width = 110 };
        closeBtn.Click += (_, _) => window.Close();
        footer.Children.Add(closeBtn);

        root.Children.Add(footer);
        Grid.SetRow(footer, 2);

        window.Content = root;
        window.ShowDialog();
    }

    private void EquipmentSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshEquipmentList();

    private void BtnNewEquipment_Click(object sender, RoutedEventArgs e)
    {
        EquipmentListEditor.SelectedIndex = -1;
        NewEquipmentEditorEntry();
    }

    private void BtnDeleteEquipment_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedEquipmentId))
            return;

        var existing = _equipmentItems.FirstOrDefault(x => string.Equals(x.Id, _selectedEquipmentId, System.StringComparison.OrdinalIgnoreCase));
        if (existing is null)
            return;

        var result = MessageBox.Show(
            $"Delete equipment item '{existing.Name}'?",
            "Equipment",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        _equipmentItems.Remove(existing);
        new EquipmentLibraryService().SaveEquipmentLibrary(_equipmentItems);
        RefreshEquipmentEditor();
        EquipmentEditorInfo.Text = $"Deleted '{existing.Name}'.";
    }

    private void BtnCleanupEquipment_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Run cleanup on all equipment items now?\n\nThis will normalize spacing and remove category prefixes from names.",
            "Cleanup Equipment Library",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        var cleanup = new EquipmentLibraryService().CleanupEquipmentLibrary();
        RefreshEquipmentEditor();
        EquipmentEditorInfo.Text = $"Cleanup complete: {cleanup.ChangedItems} updated out of {cleanup.TotalItems} item(s).";
    }

    private void BtnRefreshEquipment_Click(object sender, RoutedEventArgs e)
    {
        RefreshEquipmentEditor();
        EquipmentEditorInfo.Text = "Equipment library reloaded.";
    }

    private void BtnImportMagicalItems_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Magical Items from Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            CheckFileExists = true,
            Multiselect = false,
            FileName = "Magic Items.xlsx",
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var result = new MagicalItemImportService().ImportFromWorkbook(dlg.FileName);
            RefreshEquipmentEditor();
            EquipmentEditorInfo.Text = $"Imported magical items. Imported: {result.ImportedRows}, Updated: {result.UpdatedRows}, Created: {result.CreatedRows}, Skipped: {result.SkippedRows}.";

            if (result.Warnings.Count > 0)
            {
                MessageBox.Show(
                    $"Import complete with warnings.\n\n{string.Join("\n", result.Warnings.Take(15))}",
                    "Magical Items Import",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Import failed: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EquipmentIsContainer_Changed(object sender, RoutedEventArgs e)
        => UpdateContainerEditorEnabledState();

    private void EquipmentIsArmor_Changed(object sender, RoutedEventArgs e)
        => UpdateArmorEditorEnabledState();

    private void EquipmentIsWeapon_Changed(object sender, RoutedEventArgs e)
        => UpdateWeaponEditorEnabledState();

    private void UpdateArmorEditorEnabledState()
    {
        bool enabled = EquipmentIsArmor.IsChecked == true;
        EquipmentArmorClassValue.IsEnabled = enabled;
        EquipmentRogueArmorProfile.IsEnabled = enabled;
    }

    private void UpdateWeaponEditorEnabledState()
    {
        bool enabled = EquipmentIsWeapon.IsChecked == true;
        EquipmentWeaponSpeed.IsEnabled = enabled;
        EquipmentWeaponDamageSmMd.IsEnabled = enabled;
        EquipmentWeaponDamageLarge.IsEnabled = enabled;
        EquipmentWeaponType.IsEnabled = enabled;
        EquipmentWeaponSize.IsEnabled = enabled;
    }

    private void UpdateContainerEditorEnabledState()
    {
        bool enabled = EquipmentIsContainer.IsChecked == true;
        EquipmentContainerMaxItems.IsEnabled = enabled;
        EquipmentContainerMaxWeight.IsEnabled = enabled;
        EquipmentAllowedTags.IsEnabled = enabled;
    }

    private void BtnSaveEquipment_Click(object sender, RoutedEventArgs e)
    {
        string name = (EquipmentEditName.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            EquipmentEditorInfo.Text = "Name is required.";
            return;
        }

        var categories = ParseTokenList(EquipmentEditCategories.Text);
        if (categories.Count == 0)
            categories.Add("Uncategorized");

        var tags = ParseTokenList(EquipmentEditTags.Text)
            .Select(x => x.ToLowerInvariant())
            .ToList();

        bool isContainer = EquipmentIsContainer.IsChecked == true;
        var allowedTags = ParseTokenList(EquipmentAllowedTags.Text)
            .Select(x => x.ToLowerInvariant())
            .ToList();

        string existingId = string.IsNullOrWhiteSpace(_selectedEquipmentId) ? "" : _selectedEquipmentId;
        var metadata = ParseMetadataLines(EquipmentMagicMetadataText.Text);
        string candidateId = string.IsNullOrWhiteSpace(existingId)
            ? EquipmentLibraryService.BuildId(name)
            : existingId;
        string finalId = EnsureUniqueEquipmentId(candidateId, existingId);

        int enhancementBonus = ParseIntAllowNegative(EquipmentMagicEnhancementBonus.Text);
        int weaponAttackBonus = ParseIntAllowNegative(EquipmentMagicAttackBonus.Text);
        int weaponDamageBonus = ParseIntAllowNegative(EquipmentMagicDamageBonus.Text);
        int weaponThac0Modifier = ParseIntAllowNegative(EquipmentMagicThac0Modifier.Text);
        int armorClassModifier = ParseIntAllowNegative(EquipmentMagicArmorClassModifier.Text);

        metadata["ItemID"] = finalId;
        metadata["ItemName"] = name;
        metadata["EnhancementBonus"] = enhancementBonus.ToString(CultureInfo.InvariantCulture);
        metadata["AttackBonus"] = weaponAttackBonus.ToString(CultureInfo.InvariantCulture);
        metadata["DamageBonus"] = weaponDamageBonus.ToString(CultureInfo.InvariantCulture);
        metadata["THAC0Mod"] = weaponThac0Modifier.ToString(CultureInfo.InvariantCulture);
        metadata["ACMod"] = armorClassModifier.ToString(CultureInfo.InvariantCulture);
        ApplyGroupedEditorsToMetadata(metadata);

        var updated = new CustomEquipmentData
        {
            Id = finalId,
            Name = name,
            Description = (EquipmentEditDescription.Text ?? string.Empty).Trim(),
            IsMagical = EquipmentIsMagical.IsChecked == true,
            Categories = categories,
            ItemTags = tags,
            SizeClass = GetComboBoxText(EquipmentEditSize, "Medium"),
            Weight = ParseNonNegativeDouble(EquipmentEditWeight.Text),
            IsArmor = EquipmentIsArmor.IsChecked == true,
            ArmorClassValue = EquipmentIsArmor.IsChecked == true
                ? Math.Clamp(ParseIntAllowNegative(EquipmentArmorClassValue.Text), -10, 10)
                : 10,
            RogueArmorProfile = EquipmentIsArmor.IsChecked == true
                ? GetComboBoxTag(EquipmentRogueArmorProfile, "no_armor")
                : "no_armor",
            IsWeapon = EquipmentIsWeapon.IsChecked == true,
            WeaponSpeed = EquipmentIsWeapon.IsChecked == true
                ? ParseNonNegativeInt(EquipmentWeaponSpeed.Text)
                : 0,
            WeaponDamageSmallMedium = EquipmentIsWeapon.IsChecked == true
                ? (EquipmentWeaponDamageSmMd.Text ?? string.Empty).Trim()
                : string.Empty,
            WeaponDamageLarge = EquipmentIsWeapon.IsChecked == true
                ? (EquipmentWeaponDamageLarge.Text ?? string.Empty).Trim()
                : string.Empty,
            WeaponType = EquipmentIsWeapon.IsChecked == true
                ? GetComboBoxTag(EquipmentWeaponType, string.Empty)
                : string.Empty,
            WeaponSize = EquipmentIsWeapon.IsChecked == true
                ? GetComboBoxTag(EquipmentWeaponSize, string.Empty)
                : string.Empty,
            EnchantmentBonus = enhancementBonus,
            WeaponAttackBonus = weaponAttackBonus,
            WeaponDamageBonus = weaponDamageBonus,
            WeaponThac0Modifier = weaponThac0Modifier,
            ArmorClassModifier = armorClassModifier,
            CostGold = ParseNonNegativeInt(EquipmentEditCostGold.Text),
            CostSilver = ParseNonNegativeInt(EquipmentEditCostSilver.Text),
            CostCopper = ParseNonNegativeInt(EquipmentEditCostCopper.Text),
            IsContainer = isContainer,
            ContainerMaxItems = isContainer ? ParseNonNegativeInt(EquipmentContainerMaxItems.Text) : 0,
            ContainerMaxWeight = isContainer ? ParseNonNegativeDouble(EquipmentContainerMaxWeight.Text) : 0,
            AllowedContentTags = isContainer ? allowedTags : new List<string>(),
            MagicalItemMetadata = metadata,
        };

        int existingIndex = _equipmentItems.FindIndex(x => string.Equals(x.Id, existingId, System.StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            _equipmentItems[existingIndex] = updated;
        else
            _equipmentItems.Add(updated);

        new EquipmentLibraryService().SaveEquipmentLibrary(_equipmentItems);

        _selectedEquipmentId = updated.Id;
        RefreshEquipmentEditor();
        EquipmentEditorInfo.Text = $"Saved '{updated.Name}'.";
    }

    private string EnsureUniqueEquipmentId(string candidateId, string currentId)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
            candidateId = "equipment_item";

        if (_equipmentItems.All(x => string.Equals(x.Id, candidateId, System.StringComparison.OrdinalIgnoreCase)
            ? string.Equals(x.Id, currentId, System.StringComparison.OrdinalIgnoreCase)
            : true))
        {
            return candidateId;
        }

        int suffix = 2;
        while (true)
        {
            string test = $"{candidateId}_{suffix}";
            bool exists = _equipmentItems.Any(x => string.Equals(x.Id, test, System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(x.Id, currentId, System.StringComparison.OrdinalIgnoreCase));
            if (!exists)
                return test;
            suffix++;
        }
    }

    private static List<string> ParseTokenList(string? text)
    {
        return (text ?? string.Empty)
            .Split(',')
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> BuildEditableMetadata(CustomEquipmentData item)
    {
        var metadata = new Dictionary<string, string>(item.MagicalItemMetadata ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

        if (!metadata.ContainsKey("ItemID"))
            metadata["ItemID"] = item.Id;
        if (!metadata.ContainsKey("ItemName"))
            metadata["ItemName"] = item.Name;
        if (!metadata.ContainsKey("EnhancementBonus"))
            metadata["EnhancementBonus"] = item.EnchantmentBonus.ToString(CultureInfo.InvariantCulture);
        if (!metadata.ContainsKey("AttackBonus"))
            metadata["AttackBonus"] = item.WeaponAttackBonus.ToString(CultureInfo.InvariantCulture);
        if (!metadata.ContainsKey("DamageBonus"))
            metadata["DamageBonus"] = item.WeaponDamageBonus.ToString(CultureInfo.InvariantCulture);
        if (!metadata.ContainsKey("THAC0Mod"))
            metadata["THAC0Mod"] = item.WeaponThac0Modifier.ToString(CultureInfo.InvariantCulture);
        if (!metadata.ContainsKey("ACMod"))
            metadata["ACMod"] = item.ArmorClassModifier.ToString(CultureInfo.InvariantCulture);

        return metadata;
    }

    private static string FormatMetadataLines(Dictionary<string, string> metadata)
    {
        return string.Join(
            Environment.NewLine,
            metadata
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private static Dictionary<string, string> ParseMetadataLines(string? text)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in (text ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            string key = line.Substring(0, equalsIndex).Trim();
            string value = line.Substring(equalsIndex + 1).Trim();
            if (key.Length == 0)
                continue;

            metadata[key] = value;
        }

        return metadata;
    }

    private void BtnApplyGroupedMetadataToRaw_Click(object sender, RoutedEventArgs e)
    {
        var metadata = ParseMetadataLines(EquipmentMagicMetadataText.Text);

        metadata["ItemID"] = string.IsNullOrWhiteSpace(_selectedEquipmentId)
            ? (EquipmentEditId.Text == "(new)" ? string.Empty : EquipmentEditId.Text)
            : _selectedEquipmentId;
        metadata["ItemName"] = EquipmentEditName.Text?.Trim() ?? string.Empty;
        metadata["EnhancementBonus"] = ParseIntAllowNegative(EquipmentMagicEnhancementBonus.Text).ToString(CultureInfo.InvariantCulture);
        metadata["AttackBonus"] = ParseIntAllowNegative(EquipmentMagicAttackBonus.Text).ToString(CultureInfo.InvariantCulture);
        metadata["DamageBonus"] = ParseIntAllowNegative(EquipmentMagicDamageBonus.Text).ToString(CultureInfo.InvariantCulture);
        metadata["THAC0Mod"] = ParseIntAllowNegative(EquipmentMagicThac0Modifier.Text).ToString(CultureInfo.InvariantCulture);
        metadata["ACMod"] = ParseIntAllowNegative(EquipmentMagicArmorClassModifier.Text).ToString(CultureInfo.InvariantCulture);
        ApplyGroupedEditorsToMetadata(metadata);

        EquipmentMagicMetadataText.Text = FormatMetadataLines(metadata);
        EquipmentEditorInfo.Text = "Grouped metadata applied to raw metadata text.";
    }

    private void BtnApplyRawMetadataToGrouped_Click(object sender, RoutedEventArgs e)
    {
        var metadata = ParseMetadataLines(EquipmentMagicMetadataText.Text);

        EquipmentMagicEnhancementBonus.Text = GetMetadataInt(metadata, "EnhancementBonus", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMagicAttackBonus.Text = GetMetadataInt(metadata, "AttackBonus", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMagicDamageBonus.Text = GetMetadataInt(metadata, "DamageBonus", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMagicThac0Modifier.Text = GetMetadataInt(metadata, "THAC0Mod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMagicArmorClassModifier.Text = GetMetadataInt(metadata, "ACMod", 0).ToString(CultureInfo.InvariantCulture);

        PopulateGroupedMetadataEditors(metadata);
        EquipmentEditorInfo.Text = "Raw metadata applied to grouped editors.";
    }

    private void BtnValidateEquipmentMetadata_Click(object sender, RoutedEventArgs e)
    {
        var (metadata, invalidLines, duplicateKeys) = ParseMetadataLinesDetailed(EquipmentMagicMetadataText.Text);

        var numericErrors = new List<string>();
        foreach (string key in GroupedNumericMetadataKeys)
        {
            if (!metadata.TryGetValue(key, out string? value))
                continue;
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (!int.TryParse(value, out _))
                numericErrors.Add($"{key}={value}");
        }

        if (invalidLines.Count == 0 && duplicateKeys.Count == 0 && numericErrors.Count == 0)
        {
            EquipmentEditorInfo.Text = $"Metadata validation passed ({metadata.Count} key(s)).";
            MessageBox.Show(
                $"Metadata validation passed.\n\nKey count: {metadata.Count}",
                "Metadata Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var problems = new List<string>();
        if (invalidLines.Count > 0)
            problems.Add($"Invalid line(s): {string.Join(", ", invalidLines)}");
        if (duplicateKeys.Count > 0)
            problems.Add($"Duplicate key(s): {string.Join(", ", duplicateKeys)}");
        if (numericErrors.Count > 0)
            problems.Add($"Numeric value issue(s): {string.Join("; ", numericErrors.Take(12))}");

        EquipmentEditorInfo.Text = "Metadata validation found issues.";
        MessageBox.Show(
            string.Join("\n\n", problems),
            "Metadata Validation",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void PopulateGroupedMetadataEditors(Dictionary<string, string> metadata)
    {
        EquipmentMetaChargedItem.IsChecked = GetMetadataBool(metadata, "ChargedItem", false);
        EquipmentMetaMaxCharges.Text = GetMetadataInt(metadata, "MaxCharges", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaChargeCost.Text = GetMetadataInt(metadata, "ChargeCost", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaChargeType.Text = GetMetadataString(metadata, "ChargeType");
        EquipmentMetaRechargeMethod.Text = GetMetadataString(metadata, "RechargeMethod");
        EquipmentMetaActivationType.Text = GetMetadataString(metadata, "ActivationType");
        EquipmentMetaDuration.Text = GetMetadataString(metadata, "Duration");
        EquipmentMetaDurationType.Text = GetMetadataString(metadata, "DurationType");
        EquipmentMetaDurationValue.Text = GetMetadataString(metadata, "DurationValue");

        EquipmentMetaSaveParalyzationMod.Text = GetMetadataInt(metadata, "Save_ParalyzationMod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaSaveRswMod.Text = GetMetadataInt(metadata, "Save_RSWMod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaSavePetrificationMod.Text = GetMetadataInt(metadata, "Save_PetrificationMod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaSaveBreathMod.Text = GetMetadataInt(metadata, "Save_BreathMod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaSaveSpellMod.Text = GetMetadataInt(metadata, "Save_SpellMod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaSaveAllMod.Text = GetMetadataInt(metadata, "Save_AllMod", 0).ToString(CultureInfo.InvariantCulture);

        EquipmentMetaPickPocketsMod.Text = GetMetadataInt(metadata, "PickPocketsMod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaOpenLocksMod.Text = GetMetadataInt(metadata, "OpenLocksMod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaFindRemoveTrapsMod.Text = GetMetadataInt(metadata, "FindRemoveTrapsMod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaMoveSilentlyMod.Text = GetMetadataInt(metadata, "MoveSilentlyMod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaHideInShadowsMod.Text = GetMetadataInt(metadata, "HideInShadowsMod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaDetectNoiseMod.Text = GetMetadataInt(metadata, "DetectNoiseMod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaClimbWallsMod.Text = GetMetadataInt(metadata, "ClimbWallsMod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaReadLanguagesMod.Text = GetMetadataInt(metadata, "ReadLanguagesMod", 0).ToString(CultureInfo.InvariantCulture);

        EquipmentMetaStrMod.Text = GetMetadataInt(metadata, "STR_Mod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaDexMod.Text = GetMetadataInt(metadata, "DEX_Mod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaConMod.Text = GetMetadataInt(metadata, "CON_Mod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaIntMod.Text = GetMetadataInt(metadata, "INT_Mod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaWisMod.Text = GetMetadataInt(metadata, "WIS_Mod", 0).ToString(CultureInfo.InvariantCulture);
        EquipmentMetaChaMod.Text = GetMetadataInt(metadata, "CHA_Mod", 0).ToString(CultureInfo.InvariantCulture);

        EquipmentMetaStrSet.Text = GetMetadataString(metadata, "STR_Set");
        EquipmentMetaStrExceptionalSet.Text = GetMetadataString(metadata, "STR_Exceptional_Set");
        EquipmentMetaDexSet.Text = GetMetadataString(metadata, "DEX_Set");
        EquipmentMetaConSet.Text = GetMetadataString(metadata, "CON_Set");
        EquipmentMetaIntSet.Text = GetMetadataString(metadata, "INT_Set");
        EquipmentMetaWisSet.Text = GetMetadataString(metadata, "WIS_Set");
        EquipmentMetaChaSet.Text = GetMetadataString(metadata, "CHA_Set");
    }

    private void ResetGroupedMetadataEditors()
    {
        EquipmentMetaChargedItem.IsChecked = false;
        EquipmentMetaMaxCharges.Text = "0";
        EquipmentMetaChargeCost.Text = "0";
        EquipmentMetaChargeType.Text = string.Empty;
        EquipmentMetaRechargeMethod.Text = string.Empty;
        EquipmentMetaActivationType.Text = string.Empty;
        EquipmentMetaDuration.Text = string.Empty;
        EquipmentMetaDurationType.Text = string.Empty;
        EquipmentMetaDurationValue.Text = string.Empty;

        EquipmentMetaSaveParalyzationMod.Text = "0";
        EquipmentMetaSaveRswMod.Text = "0";
        EquipmentMetaSavePetrificationMod.Text = "0";
        EquipmentMetaSaveBreathMod.Text = "0";
        EquipmentMetaSaveSpellMod.Text = "0";
        EquipmentMetaSaveAllMod.Text = "0";

        EquipmentMetaPickPocketsMod.Text = "0";
        EquipmentMetaOpenLocksMod.Text = "0";
        EquipmentMetaFindRemoveTrapsMod.Text = "0";
        EquipmentMetaMoveSilentlyMod.Text = "0";
        EquipmentMetaHideInShadowsMod.Text = "0";
        EquipmentMetaDetectNoiseMod.Text = "0";
        EquipmentMetaClimbWallsMod.Text = "0";
        EquipmentMetaReadLanguagesMod.Text = "0";

        EquipmentMetaStrMod.Text = "0";
        EquipmentMetaDexMod.Text = "0";
        EquipmentMetaConMod.Text = "0";
        EquipmentMetaIntMod.Text = "0";
        EquipmentMetaWisMod.Text = "0";
        EquipmentMetaChaMod.Text = "0";

        EquipmentMetaStrSet.Text = string.Empty;
        EquipmentMetaStrExceptionalSet.Text = string.Empty;
        EquipmentMetaDexSet.Text = string.Empty;
        EquipmentMetaConSet.Text = string.Empty;
        EquipmentMetaIntSet.Text = string.Empty;
        EquipmentMetaWisSet.Text = string.Empty;
        EquipmentMetaChaSet.Text = string.Empty;
    }

    private void ApplyGroupedEditorsToMetadata(Dictionary<string, string> metadata)
    {
        metadata["ChargedItem"] = EquipmentMetaChargedItem.IsChecked == true ? "Yes" : "No";
        metadata["MaxCharges"] = ParseIntAllowNegative(EquipmentMetaMaxCharges.Text).ToString(CultureInfo.InvariantCulture);
        metadata["ChargeCost"] = ParseIntAllowNegative(EquipmentMetaChargeCost.Text).ToString(CultureInfo.InvariantCulture);
        metadata["ChargeType"] = (EquipmentMetaChargeType.Text ?? string.Empty).Trim();
        metadata["RechargeMethod"] = (EquipmentMetaRechargeMethod.Text ?? string.Empty).Trim();
        metadata["ActivationType"] = (EquipmentMetaActivationType.Text ?? string.Empty).Trim();
        metadata["Duration"] = (EquipmentMetaDuration.Text ?? string.Empty).Trim();
        metadata["DurationType"] = (EquipmentMetaDurationType.Text ?? string.Empty).Trim();
        metadata["DurationValue"] = (EquipmentMetaDurationValue.Text ?? string.Empty).Trim();

        metadata["Save_ParalyzationMod"] = ParseIntAllowNegative(EquipmentMetaSaveParalyzationMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["Save_RSWMod"] = ParseIntAllowNegative(EquipmentMetaSaveRswMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["Save_PetrificationMod"] = ParseIntAllowNegative(EquipmentMetaSavePetrificationMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["Save_BreathMod"] = ParseIntAllowNegative(EquipmentMetaSaveBreathMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["Save_SpellMod"] = ParseIntAllowNegative(EquipmentMetaSaveSpellMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["Save_AllMod"] = ParseIntAllowNegative(EquipmentMetaSaveAllMod.Text).ToString(CultureInfo.InvariantCulture);

        metadata["PickPocketsMod"] = ParseIntAllowNegative(EquipmentMetaPickPocketsMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["OpenLocksMod"] = ParseIntAllowNegative(EquipmentMetaOpenLocksMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["FindRemoveTrapsMod"] = ParseIntAllowNegative(EquipmentMetaFindRemoveTrapsMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["MoveSilentlyMod"] = ParseIntAllowNegative(EquipmentMetaMoveSilentlyMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["HideInShadowsMod"] = ParseIntAllowNegative(EquipmentMetaHideInShadowsMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["DetectNoiseMod"] = ParseIntAllowNegative(EquipmentMetaDetectNoiseMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["ClimbWallsMod"] = ParseIntAllowNegative(EquipmentMetaClimbWallsMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["ReadLanguagesMod"] = ParseIntAllowNegative(EquipmentMetaReadLanguagesMod.Text).ToString(CultureInfo.InvariantCulture);

        metadata["STR_Mod"] = ParseIntAllowNegative(EquipmentMetaStrMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["DEX_Mod"] = ParseIntAllowNegative(EquipmentMetaDexMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["CON_Mod"] = ParseIntAllowNegative(EquipmentMetaConMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["INT_Mod"] = ParseIntAllowNegative(EquipmentMetaIntMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["WIS_Mod"] = ParseIntAllowNegative(EquipmentMetaWisMod.Text).ToString(CultureInfo.InvariantCulture);
        metadata["CHA_Mod"] = ParseIntAllowNegative(EquipmentMetaChaMod.Text).ToString(CultureInfo.InvariantCulture);

        metadata["STR_Set"] = (EquipmentMetaStrSet.Text ?? string.Empty).Trim();
        metadata["STR_Exceptional_Set"] = (EquipmentMetaStrExceptionalSet.Text ?? string.Empty).Trim();
        metadata["DEX_Set"] = (EquipmentMetaDexSet.Text ?? string.Empty).Trim();
        metadata["CON_Set"] = (EquipmentMetaConSet.Text ?? string.Empty).Trim();
        metadata["INT_Set"] = (EquipmentMetaIntSet.Text ?? string.Empty).Trim();
        metadata["WIS_Set"] = (EquipmentMetaWisSet.Text ?? string.Empty).Trim();
        metadata["CHA_Set"] = (EquipmentMetaChaSet.Text ?? string.Empty).Trim();

        foreach (var (key, defaultValue) in GroupedMetadataFields)
        {
            if (!metadata.TryGetValue(key, out string? value))
                continue;

            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, defaultValue, StringComparison.OrdinalIgnoreCase))
                metadata.Remove(key);
        }
    }

    private static int GetMetadataInt(Dictionary<string, string> metadata, string key, int fallback)
    {
        if (!metadata.TryGetValue(key, out string? value))
            return fallback;
        return int.TryParse((value ?? string.Empty).Trim(), out int parsed) ? parsed : fallback;
    }

    private static bool GetMetadataBool(Dictionary<string, string> metadata, string key, bool fallback)
    {
        if (!metadata.TryGetValue(key, out string? value))
            return fallback;

        string normalized = (value ?? string.Empty).Trim();
        if (bool.TryParse(normalized, out bool parsed))
            return parsed;

        return string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMetadataString(Dictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key, out string? value)
            ? (value ?? string.Empty)
            : string.Empty;
    }

    private static (Dictionary<string, string> Metadata, List<int> InvalidLines, List<string> DuplicateKeys) ParseMetadataLinesDetailed(string? text)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var invalidLines = new List<int>();
        var duplicateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int lineNumber = 0;
        foreach (var rawLine in (text ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            lineNumber++;
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
            {
                invalidLines.Add(lineNumber);
                continue;
            }

            string key = line.Substring(0, equalsIndex).Trim();
            string value = line.Substring(equalsIndex + 1).Trim();
            if (key.Length == 0)
            {
                invalidLines.Add(lineNumber);
                continue;
            }

            if (metadata.ContainsKey(key))
                duplicateKeys.Add(key);
            metadata[key] = value;
        }

        return (metadata, invalidLines, duplicateKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static double ParseNonNegativeDouble(string? text)
    {
        if (double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return System.Math.Max(0, value);
        return 0;
    }

    private static void SelectComboBoxItemByText(ComboBox combo, string text)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi
                && string.Equals(cbi.Content?.ToString(), text, System.StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = cbi;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static string GetComboBoxText(ComboBox combo, string fallback)
    {
        if (combo.SelectedItem is ComboBoxItem cbi)
            return cbi.Content?.ToString() ?? fallback;
        return fallback;
    }

    private static void SelectComboBoxItemByTag(ComboBox combo, string tag)
    {
        foreach (var item in combo.Items)
        {
            if (item is ComboBoxItem cbi
                && string.Equals(cbi.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = cbi;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private static string GetComboBoxTag(ComboBox combo, string fallback)
    {
        if (combo.SelectedItem is ComboBoxItem cbi)
            return cbi.Tag?.ToString() ?? fallback;
        return fallback;
    }

    private static int ParseIntAllowNegative(string? text)
    {
        return int.TryParse((text ?? string.Empty).Trim(), out int value)
            ? value
            : 10;
    }

    // ── Characters tab ────────────────────────────────────────────────────────

    private void RefreshCharList()
    {
        CharList.Items.Clear();
        foreach (var c in _app.Characters)
            CharList.Items.Add($"{c.Name}  ({c.RaceName} {c.ClassName})");
    }

    private void BtnRefreshChars_Click(object sender, RoutedEventArgs e) =>
        RefreshCharList();

    private void CharList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count) return;
        var c = _app.Characters[idx];

        int priorAc = c.ArmorClass;
        string priorArmorProfile = c.RogueSkillArmorProfile ?? "no_armor";
        CharacterArmorService.RecalculateArmorForCharacter(c, new EquipmentLibraryService().GetEquipmentLibrary());
        bool armorChanged = c.ArmorClass != priorAc
            || !string.Equals(c.RogueSkillArmorProfile, priorArmorProfile, StringComparison.OrdinalIgnoreCase);

        bool wealthInitialized = CharacterWealthService.EnsureStartingFunds(c);
        if (wealthInitialized)
        {
            c.Notes.Add($"Starting funds assigned by class: {CharacterWealthService.FormatCoins(c)}");
            CharLevelupInfo.Text = $"Assigned starting funds: {CharacterWealthService.FormatCoins(c)}.";
        }

        if (wealthInitialized || armorChanged)
        {
            c.LastModified = System.DateTime.Now;
            c.Revision += 1;
            _app.SaveCharacters();
        }

        CharDetailTitle.Text = $"{c.Name}  —  {c.RaceName} {c.ClassName}";
        CharNotes.Text       = string.Join("\n", c.Notes);
        CharRacialAbilities.ItemsSource = c.RacialAbilities.Count > 0
            ? c.RacialAbilities
            : c.Notes.Where(n => n.StartsWith("Racial Ability: ")).Select(n => n[16..]).ToList();

        // Display current HP and max HP
        CharCurrentHp.Text = $"{c.CurrentHitPoints}";
        CharMaxHp.Text = $"{c.HitPoints}";
        CharSetCurrentHpInput.Text = $"{c.CurrentHitPoints}";
        CharSetMaxHpInput.Text = $"{c.HitPoints}";
        CharHealInput.Text = "0";
        CharSpellHealAmount.Text = "0";
        CharItemHealAmount.Text = "0";
        RefreshCharacterHealingSources(c);

        CharXpGainInput.Text = "0";
        CharHpGainInput.Text = "0";
        CharCpGainInput.Text = "0";
        CharLevelsDrainedInput.Text = "0";
        CharDrainHpInput.Text = "0";
        CharSpendNwpCpInput.Text = "0";
        CharSpendWeaponCpInput.Text = "0";
        PopulateCharacterFaithEditor(c);

        bool showCpControls = IsPlayersOptionCharacter(c);
        CharUpdateHeader.Text = showCpControls ? "Level-Up & CP Spending" : "Level-Up Progress";
        CharAddCpPanel.Visibility = showCpControls ? Visibility.Visible : Visibility.Collapsed;
        CharSpendCpGrid.Visibility = showCpControls ? Visibility.Visible : Visibility.Collapsed;
        CharSpendCpButton.Visibility = showCpControls ? Visibility.Visible : Visibility.Collapsed;

        if (!wealthInitialized)
            CharLevelupInfo.Text = "";
        RefreshCharacterProgressSummary(c);

        var items = new List<AbilityViewModel>();
        for (int i = 0; i < AbilityOrder.Length; i++)
        {
            int score = c.Abilities.GetValueOrDefault(AbilityOrder[i], 0);
            var color = score >= 15 ? "#E8C050" : score >= 9 ? "#C4A468" : "#C02828";
            items.Add(new AbilityViewModel
            {
                Score      = score.ToString(),
                Abbrev     = AbilityAbbrev[i],
                ScoreColor = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(color)),
            });
        }
        CharAbilityDisplay.ItemsSource = items;
        PopulateCharacterBaseAbilityInputs(c);
        PopulateCharacterSubAbilityInputs(c);
    }

    private static List<string> GetCharacterClassIdsForFaith(CharacterSheet character)
    {
        var classIds = character.ClassIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (classIds.Count == 0)
        {
            classIds = (character.ClassId ?? string.Empty)
                .Split(new[] { '/', '+', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        return classIds;
    }

    private void PopulateCharacterFaithEditor(CharacterSheet character)
    {
        _updatingCharacterFaithUi = true;
        try
        {
            CharAlignmentCombo.SelectedItem = CharAlignmentCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), character.Alignment, StringComparison.OrdinalIgnoreCase));
            PopulateCharacterDeityChoices(character, character.Alignment, character.DeityId);
        }
        finally
        {
            _updatingCharacterFaithUi = false;
        }
    }

    private void PopulateCharacterDeityChoices(CharacterSheet character, string alignment, string selectedDeityId)
    {
        if (!_app.Rules.DeityRulesEnabled)
        {
            CharDeityCombo.ItemsSource = Array.Empty<DeityDefinition>();
            CharDeityCombo.IsEnabled = false;
            CharFaithRequirementText.Text = "Custom deity rules are disabled in DM Toolkit > Deities. Alignment can still be updated.";
            return;
        }

        var classIds = GetCharacterClassIdsForFaith(character);
        var specialtyClass = classIds
            .Select(id => _app.Rules.Classes.GetValueOrDefault(id))
            .FirstOrDefault(definition => definition?.IsSpecialtyPriest == true);
        bool isDruid = classIds.Any(id => string.Equals(id, "druid", StringComparison.OrdinalIgnoreCase));
        bool isPaladin = classIds.Any(id => string.Equals(id, "paladin", StringComparison.OrdinalIgnoreCase));
        bool deityRequired = classIds.Any(id =>
            string.Equals(id, "cleric", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "ranger", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "paladin", StringComparison.OrdinalIgnoreCase));
        string lockedDeityId = specialtyClass?.DeityId ?? (isDruid ? "una" : string.Empty);
        deityRequired |= !string.IsNullOrWhiteSpace(lockedDeityId);

        IEnumerable<DeityDefinition> choices = _app.Rules.Deities.Values;
        if (!string.IsNullOrWhiteSpace(lockedDeityId))
        {
            choices = choices.Where(deity => string.Equals(deity.Id, lockedDeityId, StringComparison.OrdinalIgnoreCase));
            selectedDeityId = lockedDeityId;
            CharDeityCombo.IsEnabled = false;
            CharFaithRequirementText.Text = specialtyClass is not null
                ? "This specialty priest's deity is fixed by class."
                : "All Druids follow Una.";
        }
        else
        {
            CharDeityCombo.IsEnabled = true;
            choices = isPaladin
                ? choices.Where(deity => string.Equals(deity.Alignment, "LG", StringComparison.OrdinalIgnoreCase))
                : choices.Where(deity => deity.FollowerAlignments.Contains(alignment, StringComparer.OrdinalIgnoreCase));
            CharFaithRequirementText.Text = deityRequired
                ? "A deity compatible with this character's alignment is required."
                : "Deity selection is optional; choices shown accept this alignment.";
        }

        var items = choices.OrderBy(deity => deity.Name).ToList();
        if (!deityRequired)
            items.Insert(0, new DeityDefinition("", "No deity", "", "", new List<string>(), null));
        CharDeityCombo.ItemsSource = items;
        CharDeityCombo.SelectedItem = items.FirstOrDefault(deity =>
            string.Equals(deity.Id, selectedDeityId, StringComparison.OrdinalIgnoreCase));
    }

    private void CharAlignmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingCharacterFaithUi)
            return;
        int index = CharList.SelectedIndex;
        if (index < 0 || index >= _app.Characters.Count)
            return;

        string alignment = CharAlignmentCombo.SelectedItem is ComboBoxItem item
            ? item.Tag?.ToString() ?? string.Empty
            : string.Empty;
        _updatingCharacterFaithUi = true;
        try
        {
            PopulateCharacterDeityChoices(_app.Characters[index], alignment, _app.Characters[index].DeityId);
        }
        finally
        {
            _updatingCharacterFaithUi = false;
        }
    }

    private void BtnSaveCharacterFaith_Click(object sender, RoutedEventArgs e)
    {
        int index = CharList.SelectedIndex;
        if (index < 0 || index >= _app.Characters.Count)
            return;
        if (CharAlignmentCombo.SelectedItem is not ComboBoxItem alignmentItem)
        {
            MessageBox.Show("Select an alignment.", "Alignment Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var character = _app.Characters[index];
        string alignment = alignmentItem.Tag?.ToString() ?? string.Empty;
        var characterClassIds = GetCharacterClassIdsForFaith(character);
        if (!_app.Rules.IsClassCombinationAlignmentAllowed(characterClassIds, alignment))
        {
            MessageBox.Show(
                "The selected alignment is not allowed by this class combination.",
                "Alignment Not Allowed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string deityId = character.DeityId;
        if (_app.Rules.DeityRulesEnabled)
        {
            deityId = CharDeityCombo.SelectedItem is DeityDefinition deity ? deity.Id : string.Empty;
            bool deityRequired = !CharDeityCombo.IsEnabled
                || GetCharacterClassIdsForFaith(character).Any(id =>
                    string.Equals(id, "cleric", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, "ranger", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(id, "paladin", StringComparison.OrdinalIgnoreCase));
            if (deityRequired && string.IsNullOrWhiteSpace(deityId))
            {
                MessageBox.Show("Select a deity compatible with this character.", "Deity Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        character.StructuredAbilities.RemoveAll(ability =>
            ability.Id.StartsWith("deity_", StringComparison.OrdinalIgnoreCase)
            && ability.Id.EndsWith("_follower", StringComparison.OrdinalIgnoreCase));
        if (_app.Rules.DeityRulesEnabled
            && _app.Rules.Deities.TryGetValue(deityId, out var selectedDeity)
            && selectedDeity.FollowerAbility is not null)
        {
            character.StructuredAbilities.Add(CloneAbilityDefinition(selectedDeity.FollowerAbility));
        }

        character.Alignment = alignment;
        character.DeityId = deityId;
        character.Notes.Add($"Alignment and faith updated by DM: {alignment}, {(_app.Rules.Deities.TryGetValue(deityId, out var savedDeity) ? savedDeity.Name : "no deity")}.");
        RecalculateCharacterDerivedStateFromCurrentAbilities(character, new EquipmentLibraryService().GetEquipmentLibrary());
        _app.SaveCharacters();
        CharNotes.Text = string.Join("\n", character.Notes);
        CharFaithRequirementText.Text = "Alignment and faith saved.";
    }

    private void PopulateCharacterBaseAbilityInputs(CharacterSheet c)
    {
        var baseAbilities = DeriveBaseAbilitiesForDmAdjustment(c);
        CharBaseStrInput.Text = baseAbilities.GetValueOrDefault("str", 10).ToString(CultureInfo.InvariantCulture);
        CharBaseDexInput.Text = baseAbilities.GetValueOrDefault("dex", 10).ToString(CultureInfo.InvariantCulture);
        CharBaseConInput.Text = baseAbilities.GetValueOrDefault("con", 10).ToString(CultureInfo.InvariantCulture);
        CharBaseIntInput.Text = baseAbilities.GetValueOrDefault("int", 10).ToString(CultureInfo.InvariantCulture);
        CharBaseWisInput.Text = baseAbilities.GetValueOrDefault("wis", 10).ToString(CultureInfo.InvariantCulture);
        CharBaseChaInput.Text = baseAbilities.GetValueOrDefault("cha", 10).ToString(CultureInfo.InvariantCulture);
    }

    private void PopulateCharacterSubAbilityInputs(CharacterSheet c)
    {
        var currentSubAbilities = BuildCurrentSubAbilityStateForAdjustment(c.Abilities, c.SubAbilities);

        CharSubStrStaminaInput.Text = currentSubAbilities.GetValueOrDefault("str_stamina", 10).ToString(CultureInfo.InvariantCulture);
        CharSubStrMuscleInput.Text = currentSubAbilities.GetValueOrDefault("str_muscle", 10).ToString(CultureInfo.InvariantCulture);
        CharSubDexAimInput.Text = currentSubAbilities.GetValueOrDefault("dex_aim", 10).ToString(CultureInfo.InvariantCulture);
        CharSubDexBalanceInput.Text = currentSubAbilities.GetValueOrDefault("dex_balance", 10).ToString(CultureInfo.InvariantCulture);
        CharSubConHealthInput.Text = currentSubAbilities.GetValueOrDefault("con_health", 10).ToString(CultureInfo.InvariantCulture);
        CharSubConFitnessInput.Text = currentSubAbilities.GetValueOrDefault("con_fitness", 10).ToString(CultureInfo.InvariantCulture);
        CharSubIntReasonInput.Text = currentSubAbilities.GetValueOrDefault("int_reason", 10).ToString(CultureInfo.InvariantCulture);
        CharSubIntKnowledgeInput.Text = currentSubAbilities.GetValueOrDefault("int_knowledge", 10).ToString(CultureInfo.InvariantCulture);
        CharSubWisIntuitionInput.Text = currentSubAbilities.GetValueOrDefault("wis_intuition", 10).ToString(CultureInfo.InvariantCulture);
        CharSubWisWillpowerInput.Text = currentSubAbilities.GetValueOrDefault("wis_willpower", 10).ToString(CultureInfo.InvariantCulture);
        CharSubWisPerceptionInput.Text = currentSubAbilities.GetValueOrDefault("wis_perception", 10).ToString(CultureInfo.InvariantCulture);
        CharSubChaLeadershipInput.Text = currentSubAbilities.GetValueOrDefault("cha_leadership", 10).ToString(CultureInfo.InvariantCulture);
        CharSubChaAppearanceInput.Text = currentSubAbilities.GetValueOrDefault("cha_appearance", 10).ToString(CultureInfo.InvariantCulture);
        CharExceptionalStrengthInput.Text = c.ExceptionalStrength == 100
            ? "00"
            : c.ExceptionalStrength.ToString(CultureInfo.InvariantCulture);
    }

    private void BtnApplyBaseAbilityAdjustments_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        if (!TryReadBaseAbilityInputs(out var requestedBaseAbilities, out string parseError))
        {
            MessageBox.Show(parseError, "Base Stat Adjustment", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var currentAbilities = new Dictionary<string, int>(c.Abilities, StringComparer.OrdinalIgnoreCase);
        var currentBaseAbilities = DeriveBaseAbilitiesForDmAdjustment(c);
        var racialModifiers = GetCharacterRacialAbilityModifiers(c);
        var requestedEffectiveAbilities = ApplyAbilityModifiers(requestedBaseAbilities, racialModifiers);

        var changedKeys = AbilityOrder
            .Where(key => currentAbilities.GetValueOrDefault(key, 10) != requestedEffectiveAbilities.GetValueOrDefault(key, 10))
            .ToList();

        if (changedKeys.Count == 0)
        {
            CharLevelupInfo.Text = "No base stat changes were entered.";
            return;
        }

        string changeSummary = string.Join(", ",
            changedKeys.Select(key =>
            {
                string label = key.ToUpperInvariant();
                int oldBaseValue = currentBaseAbilities.GetValueOrDefault(key, currentAbilities.GetValueOrDefault(key, 10));
                int newBaseValue = requestedBaseAbilities.GetValueOrDefault(key, oldBaseValue);
                int oldValue = currentAbilities.GetValueOrDefault(key, 10);
                int newValue = requestedEffectiveAbilities.GetValueOrDefault(key, oldValue);
                return $"{label} base {oldBaseValue}->{newBaseValue} (effective {oldValue}->{newValue})";
            }));

        var confirm = MessageBox.Show(
            "DM Warning: This will permanently change base ability scores, then automatically shift linked sub-abilities by the same delta.\n\n"
            + $"Apply these changes?\n{changeSummary}",
            "Apply Base Stat Changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        var updatedSubAbilities = BuildCurrentSubAbilityStateForAdjustment(currentAbilities, c.SubAbilities);
        foreach (string abilityKey in changedKeys)
        {
            int oldAbilityScore = currentAbilities.GetValueOrDefault(abilityKey, 10);
            int newAbilityScore = requestedEffectiveAbilities.GetValueOrDefault(abilityKey, oldAbilityScore);
            int delta = newAbilityScore - oldAbilityScore;
            if (delta == 0)
                continue;

            if (!SubAbilitiesByAbility.TryGetValue(abilityKey, out var subKeys))
                continue;

            foreach (string subKey in subKeys)
            {
                int prior = updatedSubAbilities.GetValueOrDefault(subKey, oldAbilityScore);
                updatedSubAbilities[subKey] = Math.Clamp(prior + delta, 1, 20);
            }
        }

        int oldCon = currentAbilities.GetValueOrDefault("con", 10);
        int newCon = requestedEffectiveAbilities.GetValueOrDefault("con", oldCon);
        int hpDeltaPerLevel = CharacterProgressionService.GetConHitPointBonus(newCon, c.ClassId)
            - CharacterProgressionService.GetConHitPointBonus(oldCon, c.ClassId);

        c.Abilities = requestedEffectiveAbilities;
        c.SubAbilities = updatedSubAbilities;
        RecalculateCharacterDerivedStateFromCurrentAbilities(c, new EquipmentLibraryService().GetEquipmentLibrary());

        if (hpDeltaPerLevel != 0)
        {
            int levelCount = Math.Max(1, c.Level);
            int totalHpDelta = hpDeltaPerLevel * levelCount;
            c.BaseHitPoints = Math.Max(1, c.BaseHitPoints + hpDeltaPerLevel);
            c.HitPoints = Math.Max(1, c.HitPoints + totalHpDelta);
            c.CurrentHitPoints = Math.Clamp(c.CurrentHitPoints + totalHpDelta, 0, c.HitPoints);

            foreach (var level in c.HitPointGainByLevel.Keys.ToList())
                c.HitPointGainByLevel[level] = Math.Max(1, c.HitPointGainByLevel[level] + hpDeltaPerLevel);
        }

        c.Notes.Add($"DM adjusted base stats on {DateTime.Now:yyyy-MM-dd HH:mm}: {changeSummary}.");
        _app.SaveCharacters();

        RefreshCharList();
        CharList.SelectedIndex = idx;
        CharLevelupInfo.Text = $"Applied base stat changes: {changeSummary}.";
    }

    private void BtnApplySubAbilityAdjustments_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        if (!TryReadSubAbilityInputs(out var requestedSubAbilities, out int exceptionalStrength, out string parseError))
        {
            MessageBox.Show(parseError, "Sub-Ability Adjustment", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        c.SubAbilities = requestedSubAbilities;
        c.ExceptionalStrength = exceptionalStrength;

        RecalculateCharacterDerivedStateFromCurrentAbilities(c, new EquipmentLibraryService().GetEquipmentLibrary());
        c.Notes.Add($"DM adjusted sub-abilities on {DateTime.Now:yyyy-MM-dd HH:mm}.");
        _app.SaveCharacters();

        RefreshCharList();
        CharList.SelectedIndex = idx;
        CharLevelupInfo.Text = "Applied sub-ability and exceptional strength changes.";
    }

    private Dictionary<string, int> DeriveBaseAbilitiesForDmAdjustment(CharacterSheet character)
    {
        var baseAbilities = new Dictionary<string, int>(character.Abilities, StringComparer.OrdinalIgnoreCase);
        var modifiers = GetCharacterRacialAbilityModifiers(character);
        if (modifiers.Count == 0)
            return baseAbilities;

        foreach (var (abilityKey, modifier) in modifiers)
        {
            if (modifier == 0)
                continue;

            if (!baseAbilities.TryGetValue(abilityKey, out int currentValue))
                continue;

            baseAbilities[abilityKey] = currentValue - modifier;
        }

        return baseAbilities;
    }

    private Dictionary<string, int> GetCharacterRacialAbilityModifiers(CharacterSheet character)
    {
        var modifiers = _app.Rules.GetEffectiveAbilityModifiers(character.RaceId);
        if (modifiers.Count > 0)
            return modifiers;

        string raceToken = (character.RaceName ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "-");
        if (string.IsNullOrWhiteSpace(raceToken))
            return modifiers;

        if (raceToken.Contains("halfling"))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["dex"] = 1,
                ["str"] = -1,
            };
        }

        if (raceToken.Contains("dwarf"))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["con"] = 1,
                ["cha"] = -1,
            };
        }

        if (raceToken.Contains("elf"))
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["dex"] = 1,
                ["con"] = -1,
            };
        }

        return modifiers;
    }

    private static Dictionary<string, int> ApplyAbilityModifiers(
        IReadOnlyDictionary<string, int> baseAbilities,
        IReadOnlyDictionary<string, int> modifiers)
    {
        var result = new Dictionary<string, int>(baseAbilities, StringComparer.OrdinalIgnoreCase);
        foreach (var (abilityKey, modifier) in modifiers)
        {
            if (modifier == 0)
                continue;

            if (!result.TryGetValue(abilityKey, out int currentValue))
                continue;

            result[abilityKey] = currentValue + modifier;
        }

        return result;
    }

    private static Dictionary<string, int> BuildCurrentSubAbilityStateForAdjustment(
        IReadOnlyDictionary<string, int> currentAbilities,
        IReadOnlyDictionary<string, int> storedSubAbilities)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["str_stamina"] = currentAbilities.GetValueOrDefault("str", 10),
            ["str_muscle"] = currentAbilities.GetValueOrDefault("str", 10),
            ["dex_aim"] = currentAbilities.GetValueOrDefault("dex", 10),
            ["dex_balance"] = currentAbilities.GetValueOrDefault("dex", 10),
            ["con_health"] = currentAbilities.GetValueOrDefault("con", 10),
            ["con_fitness"] = currentAbilities.GetValueOrDefault("con", 10),
            ["int_reason"] = currentAbilities.GetValueOrDefault("int", 10),
            ["int_knowledge"] = currentAbilities.GetValueOrDefault("int", 10),
            ["wis_intuition"] = currentAbilities.GetValueOrDefault("wis", 10),
            ["wis_willpower"] = currentAbilities.GetValueOrDefault("wis", 10),
            ["wis_perception"] = currentAbilities.GetValueOrDefault("wis", 10),
            ["cha_leadership"] = currentAbilities.GetValueOrDefault("cha", 10),
            ["cha_appearance"] = currentAbilities.GetValueOrDefault("cha", 10),
        };

        foreach (var (key, value) in storedSubAbilities)
        {
            if (string.IsNullOrWhiteSpace(key) || !result.ContainsKey(key))
                continue;

            result[key] = value;
        }

        return result;
    }

    private void RecalculateCharacterDerivedStateFromCurrentAbilities(CharacterSheet character, IReadOnlyList<CustomEquipmentData> equipmentLibrary)
    {
        var unlockedStructuredAbilities = character.StructuredAbilities
            .Where(a => RulesEngine.AbilityUnlockLevel(a) <= Math.Max(1, character.Level))
            .ToList();

        bool isUnarmored = string.Equals(
            string.IsNullOrWhiteSpace(character.RogueSkillArmorProfile) ? "no_armor" : character.RogueSkillArmorProfile,
            "no_armor",
            StringComparison.OrdinalIgnoreCase);

        var bonuses = unlockedStructuredAbilities.Count > 0
            ? RulesEngine.AggregateEffects(unlockedStructuredAbilities, isUnarmored, Math.Max(1, character.Level))
            : new AbilityBonuses();

        var subTotals = SubAbilityTables.CalculateTotals(character.SubAbilities, character.ExceptionalStrength, character.ClassId);
        bonuses.AttackBonus += subTotals.MeleeAttackBonus;
        bonuses.DamageBonus += subTotals.MeleeDamageBonus;
        bonuses.AcBonus += subTotals.ArmorClassAdjustment;
        bonuses.SurpriseBonus += subTotals.SurpriseAdjustment;
        bonuses.ReactionBonus += subTotals.ReactionAdjustment;
        bonuses.HpPerLevel += subTotals.HpPerLevel;
        bonuses.NwpSlotBonus += subTotals.BonusNwpSlots;
        bonuses.NwpCheckBonus += subTotals.PickPocketsAdjustment;
        bonuses.NwpCheckBonus += subTotals.OpenLocksAdjustment;
        bonuses.NwpCheckBonus += subTotals.MoveSilentlyAdjustment;
        bonuses.NwpCheckBonus += subTotals.ClimbWallsAdjustment;

        if (subTotals.PoisonSaveAdjustment != 0)
            bonuses.SaveBonuses["poison"] = bonuses.SaveBonuses.GetValueOrDefault("poison") + subTotals.PoisonSaveAdjustment;

        if (subTotals.MissileAttackBonus != 0)
        {
            bonuses.WeaponAttackBonuses["missile"] = bonuses.WeaponAttackBonuses.GetValueOrDefault("missile") + subTotals.MissileAttackBonus;
            bonuses.WeaponAttackBonuses["thrown"] = bonuses.WeaponAttackBonuses.GetValueOrDefault("thrown") + subTotals.MissileAttackBonus;
            bonuses.WeaponAttackBonuses["slings"] = bonuses.WeaponAttackBonuses.GetValueOrDefault("slings") + subTotals.MissileAttackBonus;
        }

        if (subTotals.MagicDefenseAdjustment != 0)
            bonuses.SaveBonuses["magic"] = bonuses.SaveBonuses.GetValueOrDefault("magic") + subTotals.MagicDefenseAdjustment;

        bonuses.SubAbilityBonuses["pick_pockets"] = bonuses.SubAbilityBonuses.GetValueOrDefault("pick_pockets") + subTotals.PickPocketsAdjustment;
        bonuses.SubAbilityBonuses["open_locks"] = bonuses.SubAbilityBonuses.GetValueOrDefault("open_locks") + subTotals.OpenLocksAdjustment;
        bonuses.SubAbilityBonuses["move_silently"] = bonuses.SubAbilityBonuses.GetValueOrDefault("move_silently") + subTotals.MoveSilentlyAdjustment;
        bonuses.SubAbilityBonuses["climb_walls"] = bonuses.SubAbilityBonuses.GetValueOrDefault("climb_walls") + subTotals.ClimbWallsAdjustment;

        bool isPlayersOption = string.Equals(character.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);
        if (!isPlayersOption)
        {
            bonuses.HpPerLevel = 0;
            bonuses.HpFlatBonus = 0;
        }

        character.Bonuses = bonuses;
        character.DerivedStats = subTotals.ToDerivedStats();
        character.SubAbilityEffects = character.SubAbilities.ToDictionary(
            kv => kv.Key,
            kv => SubAbilityTables.GetEffect(kv.Key, kv.Value, character.ExceptionalStrength),
            StringComparer.OrdinalIgnoreCase);
        character.Movement = Math.Max(1, character.BaseMovement + bonuses.MovementBonus);

        CharacterProgressionService.InitializeCharacterProgression(character, seedLevelRewards: false);
        CharacterArmorService.RecalculateArmorForCharacter(character, equipmentLibrary);
        character.LastModified = DateTime.Now;
        character.Revision += 1;
    }

    private bool TryReadBaseAbilityInputs(out Dictionary<string, int> abilities, out string error)
    {
        abilities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["str"] = 10,
            ["dex"] = 10,
            ["con"] = 10,
            ["int"] = 10,
            ["wis"] = 10,
            ["cha"] = 10,
        };
        error = string.Empty;

        if (!TryParseAbilityScore(CharBaseStrInput.Text, out int str))
        {
            error = "Enter STR as a whole number from 1 to 25.";
            return false;
        }
        if (!TryParseAbilityScore(CharBaseDexInput.Text, out int dex))
        {
            error = "Enter DEX as a whole number from 1 to 25.";
            return false;
        }
        if (!TryParseAbilityScore(CharBaseConInput.Text, out int con))
        {
            error = "Enter CON as a whole number from 1 to 25.";
            return false;
        }
        if (!TryParseAbilityScore(CharBaseIntInput.Text, out int intel))
        {
            error = "Enter INT as a whole number from 1 to 25.";
            return false;
        }
        if (!TryParseAbilityScore(CharBaseWisInput.Text, out int wis))
        {
            error = "Enter WIS as a whole number from 1 to 25.";
            return false;
        }
        if (!TryParseAbilityScore(CharBaseChaInput.Text, out int cha))
        {
            error = "Enter CHA as a whole number from 1 to 25.";
            return false;
        }

        abilities["str"] = str;
        abilities["dex"] = dex;
        abilities["con"] = con;
        abilities["int"] = intel;
        abilities["wis"] = wis;
        abilities["cha"] = cha;
        return true;
    }

    private bool TryReadSubAbilityInputs(out Dictionary<string, int> subAbilities, out int exceptionalStrength, out string error)
    {
        subAbilities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        exceptionalStrength = 0;
        error = string.Empty;

        if (!TryParseSubAbilityScore(CharSubStrStaminaInput.Text, 1, 20, out int strStamina))
        {
            error = "Enter STR Stamina as a whole number from 1 to 20.";
            return false;
        }
        if (!TryParseSubAbilityScore(CharSubStrMuscleInput.Text, 1, 20, out int strMuscle))
        {
            error = "Enter STR Muscle as a whole number from 1 to 20.";
            return false;
        }
        if (!TryParseSubAbilityScore(CharSubDexAimInput.Text, 1, 18, out int dexAim))
        {
            error = "Enter DEX Aim as a whole number from 1 to 18.";
            return false;
        }
        if (!TryParseSubAbilityScore(CharSubDexBalanceInput.Text, 1, 18, out int dexBalance))
        {
            error = "Enter DEX Balance as a whole number from 1 to 18.";
            return false;
        }
        if (!TryParseSubAbilityScore(CharSubConHealthInput.Text, 1, 18, out int conHealth))
        {
            error = "Enter CON Health as a whole number from 1 to 18.";
            return false;
        }
        if (!TryParseSubAbilityScore(CharSubConFitnessInput.Text, 1, 18, out int conFitness))
        {
            error = "Enter CON Fitness as a whole number from 1 to 18.";
            return false;
        }
        if (!TryParseSubAbilityScore(CharSubIntReasonInput.Text, 1, 18, out int intReason))
        {
            error = "Enter INT Reason as a whole number from 1 to 18.";
            return false;
        }
        if (!TryParseSubAbilityScore(CharSubIntKnowledgeInput.Text, 1, 18, out int intKnowledge))
        {
            error = "Enter INT Knowledge as a whole number from 1 to 18.";
            return false;
        }
        if (!TryParseSubAbilityScore(CharSubWisIntuitionInput.Text, 1, 18, out int wisIntuition))
        {
            error = "Enter WIS Intuition as a whole number from 1 to 18.";
            return false;
        }
        if (!TryParseSubAbilityScore(CharSubWisWillpowerInput.Text, 1, 18, out int wisWillpower))
        {
            error = "Enter WIS Willpower as a whole number from 1 to 18.";
            return false;
        }
        if (!TryParseSubAbilityScore(CharSubWisPerceptionInput.Text, 1, 18, out int wisPerception))
        {
            error = "Enter WIS Perception as a whole number from 1 to 18.";
            return false;
        }
        if (!TryParseSubAbilityScore(CharSubChaLeadershipInput.Text, 1, 18, out int chaLeadership))
        {
            error = "Enter CHA Leadership as a whole number from 1 to 18.";
            return false;
        }
        if (!TryParseSubAbilityScore(CharSubChaAppearanceInput.Text, 1, 18, out int chaAppearance))
        {
            error = "Enter CHA Appearance as a whole number from 1 to 18.";
            return false;
        }

        if (!TryParseExceptionalStrengthForEditor(CharExceptionalStrengthInput.Text, out exceptionalStrength))
        {
            error = "Enter Exceptional Strength as 0, 1-100, or 00 for 100.";
            return false;
        }

        subAbilities["str_stamina"] = strStamina;
        subAbilities["str_muscle"] = strMuscle;
        subAbilities["dex_aim"] = dexAim;
        subAbilities["dex_balance"] = dexBalance;
        subAbilities["con_health"] = conHealth;
        subAbilities["con_fitness"] = conFitness;
        subAbilities["int_reason"] = intReason;
        subAbilities["int_knowledge"] = intKnowledge;
        subAbilities["wis_intuition"] = wisIntuition;
        subAbilities["wis_willpower"] = wisWillpower;
        subAbilities["wis_perception"] = wisPerception;
        subAbilities["cha_leadership"] = chaLeadership;
        subAbilities["cha_appearance"] = chaAppearance;
        return true;
    }

    private static bool TryParseSubAbilityScore(string? text, int min, int max, out int value)
    {
        value = 0;
        return int.TryParse((text ?? string.Empty).Trim(), out value) && value >= min && value <= max;
    }

    private static bool TryParseExceptionalStrengthForEditor(string? text, out int value)
    {
        value = 0;
        string raw = (text ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
            return true;

        if (string.Equals(raw, "00", StringComparison.OrdinalIgnoreCase))
        {
            value = 100;
            return true;
        }

        if (!int.TryParse(raw, out int parsed))
            return false;

        if (parsed < 0 || parsed > 100)
            return false;

        value = parsed;
        return true;
    }

    private static bool TryParseAbilityScore(string? text, out int value)
    {
        value = 0;
        return int.TryParse((text ?? string.Empty).Trim(), out value) && value >= 1 && value <= 25;
    }

    private void BtnHealCharacter_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        int healAmount = ParseNonNegativeInt(CharHealInput.Text);
        
        if (healAmount <= 0)
        {
            MessageBox.Show("Enter a positive healing amount.", "Heal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int oldHp = c.CurrentHitPoints;
        c.CurrentHitPoints = Math.Min(c.HitPoints, c.CurrentHitPoints + healAmount);
        
        c.Revision += 1;
        c.LastModified = System.DateTime.Now;
        _app.SaveCharacters();

        CharCurrentHp.Text = $"{c.CurrentHitPoints}";
        CharHealInput.Text = "0";
        
        MessageBox.Show(
            $"{c.Name} healed from {oldHp} to {c.CurrentHitPoints} HP.",
            "Healing",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnApplyHpOverride_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        if (!int.TryParse(CharSetMaxHpInput.Text.Trim(), out int newMaxHp) || newMaxHp < 1)
        {
            MessageBox.Show("Enter a whole number of 1 or greater for max HP.", "HP Override", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(CharSetCurrentHpInput.Text.Trim(), out int newCurrentHp) || newCurrentHp < 0)
        {
            MessageBox.Show("Enter a whole number of 0 or greater for current HP.", "HP Override", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (newCurrentHp > newMaxHp)
        {
            MessageBox.Show("Current HP cannot be greater than max HP.", "HP Override", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int oldMaxHp = c.HitPoints;
        int oldCurrentHp = c.CurrentHitPoints;

        c.HitPoints = newMaxHp;
        c.CurrentHitPoints = newCurrentHp;
        c.Notes.Add($"HP manually adjusted: current {oldCurrentHp} -> {newCurrentHp}, max {oldMaxHp} -> {newMaxHp}.");
        c.Revision += 1;
        c.LastModified = System.DateTime.Now;
        _app.SaveCharacters();

        CharCurrentHp.Text = $"{c.CurrentHitPoints}";
        CharMaxHp.Text = $"{c.HitPoints}";
        CharSetCurrentHpInput.Text = $"{c.CurrentHitPoints}";
        CharSetMaxHpInput.Text = $"{c.HitPoints}";
        CharNotes.Text = string.Join("\n", c.Notes);
        RefreshCharacterProgressSummary(c);
        CharLevelupInfo.Text = $"HP manually updated: current {oldCurrentHp} -> {c.CurrentHitPoints}, max {oldMaxHp} -> {c.HitPoints}.";
    }

    private void BtnHealFromSpell_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        if (CharHealingSpellPicker.SelectedItem is not HealingSpellOption selected)
        {
            MessageBox.Show("Select a healing spell first.", "Healing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int healAmount = ParseNonNegativeInt(CharSpellHealAmount.Text);
        if (healAmount <= 0)
        {
            MessageBox.Show("Enter a positive healing amount.", "Healing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (c.CurrentHitPoints >= c.HitPoints)
        {
            MessageBox.Show($"{c.Name} is already at full HP.", "Healing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (c.SpellTracking is not null)
        {
            bool isDivine = string.Equals(selected.Spell.Category, "divine", System.StringComparison.OrdinalIgnoreCase);
            var availableSlots = c.SpellTracking.GetAvailableSpellSlots(isDivine);
            if (selected.SpellLevel > 0
                && (!availableSlots.TryGetValue(selected.SpellLevel, out int slotsRemaining) || slotsRemaining <= 0))
            {
                MessageBox.Show(
                    $"No level {selected.SpellLevel} {selected.Spell.Category.ToLowerInvariant()} spell slots remain.",
                    "Healing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            int preparedIndex = c.SpellTracking.CurrentDayTracking.PreparedSpells.FindIndex(p =>
                !p.IsCast && string.Equals(p.SpellId, selected.Spell.Id, System.StringComparison.OrdinalIgnoreCase));
            if (preparedIndex >= 0)
                c.SpellTracking.CastPreparedSpell(preparedIndex);

            c.SpellTracking.CastSpell(selected.Spell.Id, selected.Spell.Name, selected.SpellLevel, $"Healing applied to {c.Name}");
        }

        int oldHp = c.CurrentHitPoints;
        c.CurrentHitPoints = System.Math.Min(c.HitPoints, c.CurrentHitPoints + healAmount);

        c.Notes.Add($"Cast {selected.Spell.Name} for healing: +{healAmount} HP ({oldHp} -> {c.CurrentHitPoints}).");
        c.Revision += 1;
        c.LastModified = System.DateTime.Now;
        _app.SaveCharacters();

        CharCurrentHp.Text = $"{c.CurrentHitPoints}";
        CharSpellHealAmount.Text = "0";
        CharNotes.Text = string.Join("\n", c.Notes);
        RefreshCharacterProgressSummary(c);

        MessageBox.Show(
            $"{selected.Spell.Name} healed {c.Name} from {oldHp} to {c.CurrentHitPoints} HP.",
            "Healing",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnHealFromItem_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        if (CharHealingItemPicker.SelectedItem is not HealingItemOption selected)
        {
            MessageBox.Show("Select a healing item first.", "Healing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int healAmount = ParseNonNegativeInt(CharItemHealAmount.Text);
        if (healAmount <= 0)
        {
            MessageBox.Show("Enter a positive healing amount.", "Healing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (c.CurrentHitPoints >= c.HitPoints)
        {
            MessageBox.Show($"{c.Name} is already at full HP.", "Healing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string targetId = EquipmentLibraryService.CanonicalizeId(selected.ItemId);
        var existing = c.EquipmentSelections.FirstOrDefault(x =>
            string.Equals(EquipmentLibraryService.CanonicalizeId(x.ItemId), targetId, System.StringComparison.OrdinalIgnoreCase));

        if (existing is null || existing.Quantity <= 0)
        {
            MessageBox.Show("That healing item is no longer available in inventory.", "Healing", MessageBoxButton.OK, MessageBoxImage.Warning);
            RefreshCharacterHealingSources(c);
            return;
        }

        int oldHp = c.CurrentHitPoints;
        c.CurrentHitPoints = System.Math.Min(c.HitPoints, c.CurrentHitPoints + healAmount);

        existing.Quantity = System.Math.Max(0, existing.Quantity - 1);
        if (existing.Quantity == 0)
            c.EquipmentSelections.Remove(existing);

        c.Equipment = c.EquipmentSelections
            .OrderBy(x => x.Category)
            .ThenBy(x => x.ItemName)
            .Select(x => x.Quantity > 1 ? $"{x.ItemName} x{x.Quantity}" : x.ItemName)
            .ToList();

        c.Notes.Add($"Used {existing.ItemName} for healing: +{healAmount} HP ({oldHp} -> {c.CurrentHitPoints}).");
        c.Revision += 1;
        c.LastModified = System.DateTime.Now;
        _app.SaveCharacters();

        CharCurrentHp.Text = $"{c.CurrentHitPoints}";
        CharItemHealAmount.Text = "0";
        CharNotes.Text = string.Join("\n", c.Notes);
        RefreshCharacterProgressSummary(c);
        RefreshCharacterHealingSources(c);

        MessageBox.Show(
            $"{existing.ItemName} used. {c.Name} healed from {oldHp} to {c.CurrentHitPoints} HP.",
            "Healing",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RefreshCharacterHealingSources(CharacterSheet character)
    {
        CharHealingSpellPicker.Items.Clear();
        foreach (var spell in GetCharacterHealingSpells(character))
        {
            int level = ParseSpellLevelOrDefault(spell.Level);
            CharHealingSpellPicker.Items.Add(new HealingSpellOption(
                spell,
                level,
                $"{spell.Name} (L{level}, {spell.Category})"));
        }

        CharHealingSpellPicker.SelectedIndex = CharHealingSpellPicker.Items.Count > 0 ? 0 : -1;

        CharHealingItemPicker.Items.Clear();
        foreach (var item in character.EquipmentSelections
                     .Where(x => x.Quantity > 0 && IsLikelyHealingItem(x.ItemName))
                     .OrderBy(x => x.ItemName, System.StringComparer.OrdinalIgnoreCase))
        {
            CharHealingItemPicker.Items.Add(new HealingItemOption(
                item.ItemId,
                item.Quantity > 1 ? $"{item.ItemName} x{item.Quantity}" : item.ItemName));
        }

        CharHealingItemPicker.SelectedIndex = CharHealingItemPicker.Items.Count > 0 ? 0 : -1;
    }

    private List<SpellDefinition> GetCharacterHealingSpells(CharacterSheet character)
    {
        var knownIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (string id in character.TrackedSpellIds)
            if (!string.IsNullOrWhiteSpace(id))
                knownIds.Add(id);

        foreach (string id in character.WizardSpellbookIds)
            if (!string.IsNullOrWhiteSpace(id))
                knownIds.Add(id);

        if (character.SpellTracking is not null)
        {
            foreach (var prepared in character.SpellTracking.CurrentDayTracking.PreparedSpells)
                if (!string.IsNullOrWhiteSpace(prepared.SpellId))
                    knownIds.Add(prepared.SpellId);
            foreach (var cast in character.SpellTracking.CurrentDayTracking.CastSpells)
                if (!string.IsNullOrWhiteSpace(cast.SpellId))
                    knownIds.Add(cast.SpellId);
        }

        return _app.Rules.Spells
            .Where(s => s.IsHealing)
            .Where(s => knownIds.Contains(s.Id))
            .OrderBy(s => ParseSpellLevelOrDefault(s.Level))
            .ThenBy(s => s.Name, System.StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ParseSpellLevelOrDefault(string? levelText)
    {
        if (string.IsNullOrWhiteSpace(levelText))
            return 1;

        string trimmed = levelText.Trim();
        int start = -1;
        int end = -1;
        for (int i = 0; i < trimmed.Length; i++)
        {
            if (char.IsDigit(trimmed[i]))
            {
                if (start < 0)
                    start = i;
                end = i;
            }
            else if (start >= 0)
            {
                break;
            }
        }

        if (start >= 0 && end >= start)
        {
            string numeric = trimmed[start..(end + 1)];
            if (int.TryParse(numeric, out int parsed) && parsed >= 0)
                return parsed;
        }

        return 1;
    }

    private static bool IsLikelyHealingItem(string? itemName)
    {
        string text = (itemName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("heal", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("healing", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("cure", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("potion", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("elixir", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("balm", System.StringComparison.OrdinalIgnoreCase);
    }

    private void BtnApplyLevelUpdate_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        if (!int.TryParse(CharXpGainInput.Text.Trim(), out int xpGain))
        {
            CharLevelupInfo.Text = "Enter a whole number for XP adjustment.";
            return;
        }
        if (!int.TryParse(CharHpGainInput.Text.Trim(), out int hpGain))
        {
            CharLevelupInfo.Text = "Enter a whole number for HP adjustment.";
            return;
        }
        int cpGain = IsPlayersOptionCharacter(c) ? ParseNonNegativeInt(CharCpGainInput.Text) : 0;

        int primeXpBonus = CharacterProgressionService.GetPrimeRequisiteBonusPercent(c);
        int abilityXpBonus = c.Bonuses?.XpModifierPercent ?? 0;
        int totalXpBonus = primeXpBonus + abilityXpBonus;
        if (xpGain > 0 && totalXpBonus != 0)
        {
            int adjusted = CharacterProgressionService.ApplyExperienceBonus(xpGain, totalXpBonus);
            int delta = adjusted - xpGain;
            string signPrime = primeXpBonus >= 0 ? $"+{primeXpBonus}" : $"{primeXpBonus}";
            string signAbility = abilityXpBonus >= 0 ? $"+{abilityXpBonus}" : $"{abilityXpBonus}";
            string signTotal = totalXpBonus >= 0 ? $"+{totalXpBonus}" : $"{totalXpBonus}";

            var decision = MessageBox.Show(
                $"XP entered: {xpGain}\n\n" +
                $"Prime Requisite bonus: {signPrime}%\n" +
                $"Racial/Class bonus: {signAbility}%\n" +
                $"Total bonus: {signTotal}%\n\n" +
                $"Apply bonus automatically?\n" +
                $"Yes = apply (effective XP gain {adjusted}, change {delta:+#;-#;0})\n" +
                $"No = keep entered XP as-is",
                "XP Bonus",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (decision == MessageBoxResult.Cancel)
                return;
            if (decision == MessageBoxResult.Yes)
                xpGain = adjusted;
        }

        int oldLevel = c.Level;
        int oldHp = c.HitPoints;
        int oldXp = c.ExperiencePoints;

        c.ExperiencePoints = (int)Math.Clamp((long)Math.Max(0, c.ExperiencePoints) + xpGain, 0L, int.MaxValue);
        c.UnspentCharacterPoints += cpGain;

        var advancement = xpGain > 0
            ? CharacterProgressionService.ApplyLevelAdvancementFromExperience(c, applyHitPointProgression: false)
            : new LevelAdvancementResult { OldLevel = oldLevel, NewLevel = oldLevel };
        if (xpGain <= 0)
            CharacterProgressionService.ApplyExperienceAdjustment(c, 0);

        if (hpGain != 0)
        {
            c.HitPoints = Math.Max(1, c.HitPoints + hpGain);
            c.CurrentHitPoints = Math.Clamp(c.CurrentHitPoints + hpGain, 0, c.HitPoints);
            if (advancement.LeveledUp && hpGain > 0)
                CharacterProgressionService.RecordHitPointGainAcrossLevels(
                    c,
                    advancement.OldLevel,
                    advancement.NewLevel,
                    hpGain);
        }

        CharacterProgressionService.ApplyExperienceAdjustment(c, 0);

        c.Revision += 1;
        c.LastModified = System.DateTime.Now;
        _app.SaveCharacters();

        RefreshCharacterProgressSummary(c);
        CharLevelupInfo.Text =
            $"Updated. Level {oldLevel} -> {c.Level}, XP {oldXp:n0} -> {c.ExperiencePoints:n0}, " +
            $"HP {oldHp} -> {c.HitPoints}.";

        CharXpGainInput.Text = "0";
        CharHpGainInput.Text = "0";
        CharCpGainInput.Text = "0";
    }

    private void BtnApplyLevelDrain_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var character = _app.Characters[idx];
        int requestedLevels = ParseNonNegativeInt(CharLevelsDrainedInput.Text);
        int manualHitPointLoss = ParseNonNegativeInt(CharDrainHpInput.Text);
        if (requestedLevels <= 0)
        {
            CharLevelupInfo.Text = "Enter the number of levels drained.";
            return;
        }

        int actualLevels = Math.Min(requestedLevels, Math.Max(0, character.Level - 1));
        if (actualLevels <= 0)
        {
            CharLevelupInfo.Text = "A level 1 character cannot lose another level.";
            return;
        }

        int targetLevel = character.Level - actualLevels;
        bool hasRecordedHp = character.HitPointGainByLevel.Any(entry =>
            entry.Key > targetLevel && entry.Key <= character.Level && entry.Value > 0);
        if (manualHitPointLoss <= 0 && !hasRecordedHp)
        {
            CharLevelupInfo.Text = "No HP history exists for the drained level. Enter the HP lost before applying the drain.";
            return;
        }

        var confirmation = MessageBox.Show(
            $"Apply {actualLevels} level drain to {character.Name}?\n\n" +
            $"Level: {character.Level} -> {targetLevel}\n" +
            "XP will move to the midpoint of the new level's progression band. " +
            (manualHitPointLoss > 0 ? $"HP lost: {manualHitPointLoss}." : "Recorded HP from the lost level(s) will be removed."),
            "Confirm Level Drain",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        var drain = CharacterProgressionService.ApplyLevelDrain(character, actualLevels, manualHitPointLoss);
        character.Notes.Add(
            $"Level drain: level {drain.OldLevel} to {drain.NewLevel}; " +
            $"XP {drain.OldExperience:N0} to {drain.NewExperience:N0}; HP lost {drain.HitPointsLost}.");
        character.Revision += 1;
        character.LastModified = DateTime.Now;
        _app.SaveCharacters();

        RefreshCharacterProgressSummary(character);
        CharNotes.Text = string.Join("\n", character.Notes);
        CharLevelupInfo.Text =
            $"Level drained: {drain.OldLevel} -> {drain.NewLevel}. " +
            $"XP {drain.OldExperience:N0} -> {drain.NewExperience:N0}, HP lost {drain.HitPointsLost}.";
        CharLevelsDrainedInput.Text = "0";
        CharDrainHpInput.Text = "0";
    }

    private void BtnSpendCharacterCp_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        if (!IsPlayersOptionCharacter(c))
        {
            CharLevelupInfo.Text = "Core Rules characters do not use Character Point spending.";
            return;
        }

        int nwpSpend = ParseNonNegativeInt(CharSpendNwpCpInput.Text);
        int weaponSpend = ParseNonNegativeInt(CharSpendWeaponCpInput.Text);
        int hpRerollSpend = ParseNonNegativeInt(CharSpendHpRerollCpInput.Text);
        int totalSpend = nwpSpend + weaponSpend + hpRerollSpend;

        if (totalSpend == 0)
        {
            CharLevelupInfo.Text = "Enter CP to spend on NWPs, weapon proficiencies, or HP rerolls.";
            return;
        }

        if (totalSpend > c.UnspentCharacterPoints)
        {
            CharLevelupInfo.Text = $"Not enough unspent CP. Requested {totalSpend}, available {c.UnspentCharacterPoints}.";
            return;
        }

        c.UnspentCharacterPoints -= totalSpend;
        c.SpentNwpCharacterPoints += nwpSpend;
        c.SpentWeaponCharacterPoints += weaponSpend;
        c.SpentHpRerollCharacterPoints += hpRerollSpend;
        if (hpRerollSpend > 0)
        {
            int currentLevel = Math.Max(1, c.Level);
            c.HpRerollsByLevel[currentLevel] = c.HpRerollsByLevel.GetValueOrDefault(currentLevel, 0) + hpRerollSpend;
        }
        c.Revision += 1;
        c.LastModified = System.DateTime.Now;
        _app.SaveCharacters();

        RefreshCharacterProgressSummary(c);
        CharLevelupInfo.Text = $"Spent {nwpSpend} CP on NWPs, {weaponSpend} CP on weapons, and {hpRerollSpend} CP on HP rerolls.";

        CharSpendNwpCpInput.Text = "0";
        CharSpendWeaponCpInput.Text = "0";
        CharSpendHpRerollCpInput.Text = "0";
    }

    private void BtnBuyItem_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        var library = new EquipmentLibraryService().GetEquipmentLibrary()
            .OrderBy(x => x.Name, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (library.Count == 0)
        {
            MessageBox.Show("No equipment is available in the library.", "Buy Item",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryShowBuyItemDialog(library, out var item, out int quantity, out bool useOverrideCost, out int overrideGp, out int overrideSp, out int overrideCp))
            return;

        ResolvePurchaseUnitCost(c, item, useOverrideCost, overrideGp, overrideSp, overrideCp, out int unitGp, out int unitSp, out int unitCp);

        int totalGp = unitGp * quantity;
        int totalSp = unitSp * quantity;
        int totalCp = unitCp * quantity;
        int totalCostCopper = CharacterWealthService.ToCopper(totalGp, totalSp, totalCp);
        int copperBeforePurchase = CharacterWealthService.GetTotalCopper(c);

        if (totalCostCopper <= 0)
        {
            var freeDecision = MessageBox.Show(
                $"{item.Name} has no cost recorded. Add it to the character as a free item?",
                "No Cost Recorded",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (freeDecision != MessageBoxResult.Yes)
                return;
        }
        else if (!CharacterWealthService.TrySpend(c, totalGp, totalSp, totalCp, out string reason))
        {
            CharLevelupInfo.Text = reason;
            RefreshCharacterProgressSummary(c);
            return;
        }

        var existing = c.EquipmentSelections.FirstOrDefault(x =>
            string.Equals(
                EquipmentLibraryService.CanonicalizeId(x.ItemId),
                EquipmentLibraryService.CanonicalizeId(item.Id),
                System.StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            c.EquipmentSelections.Add(new EquipmentSelection
            {
                ItemId = item.Id,
                Category = EquipmentLibraryService.NormalizeCategoryLabel(item.Categories.FirstOrDefault() ?? "Equipment"),
                ItemName = item.Name,
                CostText = useOverrideCost
                    ? BuildCostText(unitGp, unitSp, unitCp)
                    : BuildCostText(item),
                Quantity = quantity,
                IsArmor = item.IsArmor,
                ArmorClassValue = item.ArmorClassValue,
                RogueArmorProfile = item.RogueArmorProfile,
                IsWeapon = item.IsWeapon,
                WeaponSpeed = item.WeaponSpeed,
                WeaponDamageSmallMedium = item.WeaponDamageSmallMedium,
                WeaponDamageLarge = item.WeaponDamageLarge,
                WeaponType = item.WeaponType,
                WeaponSize = item.WeaponSize,
                WeaponAttackBonus = item.WeaponAttackBonus,
                WeaponDamageBonus = item.WeaponDamageBonus,
                WeaponThac0Modifier = item.WeaponThac0Modifier,
                CostGoldEach = Math.Max(0, unitGp),
                CostSilverEach = Math.Max(0, unitSp),
                CostCopperEach = Math.Max(0, unitCp),
                SizeClassEach = item.SizeClass,
                WeightEach = item.Weight,
            });
        }
        else
        {
            existing.Quantity = System.Math.Max(1, existing.Quantity + quantity);
            if (!existing.IsArmor && item.IsArmor)
                existing.IsArmor = true;
            if (existing.ArmorClassValue == 10 && item.ArmorClassValue < 10)
                existing.ArmorClassValue = item.ArmorClassValue;
            if (string.IsNullOrWhiteSpace(existing.RogueArmorProfile) || string.Equals(existing.RogueArmorProfile, "no_armor", StringComparison.OrdinalIgnoreCase))
                existing.RogueArmorProfile = item.RogueArmorProfile;
            if (!existing.IsWeapon && item.IsWeapon)
                existing.IsWeapon = true;
            if (existing.WeaponSpeed <= 0 && item.WeaponSpeed > 0)
                existing.WeaponSpeed = item.WeaponSpeed;
            if (string.IsNullOrWhiteSpace(existing.WeaponDamageSmallMedium))
                existing.WeaponDamageSmallMedium = item.WeaponDamageSmallMedium;
            if (string.IsNullOrWhiteSpace(existing.WeaponDamageLarge))
                existing.WeaponDamageLarge = item.WeaponDamageLarge;
            if (string.IsNullOrWhiteSpace(existing.WeaponType))
                existing.WeaponType = item.WeaponType;
            if (string.IsNullOrWhiteSpace(existing.WeaponSize))
                existing.WeaponSize = item.WeaponSize;
            if (existing.WeaponAttackBonus == 0)
                existing.WeaponAttackBonus = item.WeaponAttackBonus;
            if (existing.WeaponDamageBonus == 0)
                existing.WeaponDamageBonus = item.WeaponDamageBonus;
            if (existing.WeaponThac0Modifier == 0)
                existing.WeaponThac0Modifier = item.WeaponThac0Modifier;
            if (existing.CostGoldEach <= 0)
                existing.CostGoldEach = Math.Max(0, unitGp);
            if (existing.CostSilverEach <= 0)
                existing.CostSilverEach = Math.Max(0, unitSp);
            if (existing.CostCopperEach <= 0)
                existing.CostCopperEach = Math.Max(0, unitCp);
            if (string.IsNullOrWhiteSpace(existing.SizeClassEach))
                existing.SizeClassEach = item.SizeClass;
            if (existing.WeightEach <= 0)
                existing.WeightEach = item.Weight;
        }

        c.Equipment = c.EquipmentSelections
            .OrderBy(x => x.Category)
            .ThenBy(x => x.ItemName)
            .Select(x => x.Quantity > 1 ? $"{x.ItemName} x{x.Quantity}" : x.ItemName)
            .ToList();

        CharacterArmorService.RecalculateArmorForCharacter(c, library);

        c.Notes.Add($"Purchased {item.Name} x{quantity} for {CharacterWealthService.FormatCoins(totalCostCopper)}.");
        c.LastModified = System.DateTime.Now;
        c.Revision += 1;
        _app.SaveCharacters();

        RefreshCharacterProgressSummary(c);
        CharNotes.Text = string.Join("\n", c.Notes);
        int copperAfterPurchase = CharacterWealthService.GetTotalCopper(c);
        CharLevelupInfo.Text = totalCostCopper > 0
            ? $"Purchased {item.Name} x{quantity}. Deducted {CharacterWealthService.FormatCoins(totalCostCopper)}. Funds: {CharacterWealthService.FormatCoins(copperBeforePurchase)} -> {CharacterWealthService.FormatCoins(copperAfterPurchase)}."
            : $"Added {item.Name} x{quantity} (no cost recorded).";
    }

    private void BtnSellItem_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var c = _app.Characters[idx];
        if (!TryShowSellDialog(
            c,
            out string removeItemId,
            out int removeQuantity,
            out int pp,
            out int gp,
            out int sp,
            out int cp,
            out int gems,
            out int gemValueEachGp,
            out string note))
            return;

        bool removedItem = false;
        string removedItemName = string.Empty;
        if (!string.IsNullOrWhiteSpace(removeItemId) && removeQuantity > 0)
        {
            string targetId = EquipmentLibraryService.CanonicalizeId(removeItemId);
            var existing = c.EquipmentSelections.FirstOrDefault(x =>
                string.Equals(EquipmentLibraryService.CanonicalizeId(x.ItemId), targetId, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                CharLevelupInfo.Text = "Selected equipment could not be found on this character.";
                return;
            }

            removedItemName = existing.ItemName;
            existing.Quantity = Math.Max(0, existing.Quantity - removeQuantity);
            if (existing.Quantity == 0)
                c.EquipmentSelections.Remove(existing);

            c.Equipment = c.EquipmentSelections
                .OrderBy(x => x.Category)
                .ThenBy(x => x.ItemName)
                .Select(x => x.Quantity > 1 ? $"{x.ItemName} x{x.Quantity}" : x.ItemName)
                .ToList();

            CharacterArmorService.RecalculateArmorForCharacter(c, new EquipmentLibraryService().GetEquipmentLibrary());
            removedItem = true;
        }

        int addedCopper = CharacterWealthService.ToCopper(pp, gp, sp, cp);
        int gemCountToAdd = Math.Max(0, gems);
        int gemEachValueGp = Math.Max(0, gemValueEachGp);
        int gemValueToAddGp = gemCountToAdd * gemEachValueGp;
        bool addedGemOnly = gemCountToAdd > 0;

        if (addedCopper <= 0 && !addedGemOnly && !removedItem)
        {
            CharLevelupInfo.Text = "No changes requested. Set an item quantity to remove and/or add funds.";
            return;
        }

        if (addedCopper > 0)
            CharacterWealthService.Add(c, pp, gp, sp, cp);
        if (addedGemOnly)
        {
            c.Gems ??= new List<GemEntry>();
            c.Gems.Add(new GemEntry
            {
                Name = "Gem",
                Quantity = gemCountToAdd,
                ValueGoldPieces = gemEachValueGp,
            });
            c.GemCount = c.Gems.Sum(x => Math.Max(1, x.Quantity));
            c.GemValueGoldPieces = c.Gems.Sum(x => Math.Max(1, x.Quantity) * Math.Max(0, x.ValueGoldPieces));
        }

        string addedFundsText = FormatAddedFunds(pp, gp, sp, cp, gemCountToAdd, gemEachValueGp);

        string reason = string.IsNullOrWhiteSpace(note) ? "sale/funds added" : note.Trim();
        if (removedItem && (addedCopper > 0 || addedGemOnly))
            c.Notes.Add($"Sold {removedItemName} x{removeQuantity} ({reason}); added {addedFundsText}.");
        else if (removedItem)
            c.Notes.Add($"Removed {removedItemName} x{removeQuantity} from equipment ({reason}).");
        else
            c.Notes.Add($"Funds added ({reason}): {addedFundsText}.");

        c.LastModified = System.DateTime.Now;
        c.Revision += 1;
        _app.SaveCharacters();

        RefreshCharacterProgressSummary(c);
        CharNotes.Text = string.Join("\n", c.Notes);
        if (removedItem && (addedCopper > 0 || addedGemOnly))
            CharLevelupInfo.Text = $"Removed {removedItemName} x{removeQuantity} and added {addedFundsText}.";
        else if (removedItem)
            CharLevelupInfo.Text = $"Removed {removedItemName} x{removeQuantity}.";
        else
            CharLevelupInfo.Text = $"Added {addedFundsText} to {c.Name}.";
    }

    private void RefreshCharacterProgressSummary(CharacterSheet c)
    {
        var classIds = c.ClassIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        string levelDisplay = classIds.Count > 1
            ? string.Join("/", classIds.Select(id => CharacterProgressionService.GetLevelForExperience(id, CharacterProgressionService.GetExperienceForClass(c, id))))
            : Math.Max(1, c.Level).ToString();
        string xpDisplay = classIds.Count > 1
            ? string.Join("/", classIds.Select(id => CharacterProgressionService.GetExperienceForClass(c, id).ToString("N0")))
            : Math.Max(0, c.ExperiencePoints).ToString("N0");

        string hpAudit = c.HitPointGainByLevel.Count > 0
            ? string.Join(", ", c.HitPointGainByLevel
                .OrderBy(kv => kv.Key)
                .Select(kv => $"L{kv.Key}:+{kv.Value}"))
            : "No per-level HP history recorded yet.";

        CharCoinSummary.Text = CharacterWealthService.FormatCoins(c);
        EnsureCharacterInventorySortOptions();
        PopulateCharacterInventoryCategoryFilter(c);
        RefreshGemInventoryList(c);
        RefreshCharacterInventorySections(c);

        bool showCpSummary = IsPlayersOptionCharacter(c);
        string cpSummary = showCpSummary
            ? $"CP Unspent: {c.UnspentCharacterPoints}    CP Spent (NWP): {c.SpentNwpCharacterPoints}    CP Spent (Weapon): {c.SpentWeaponCharacterPoints}    CP Spent (HP Rerolls): {c.SpentHpRerollCharacterPoints}\n"
            : string.Empty;

        CharProgressSummary.Text =
            $"Level: {levelDisplay}    XP: {xpDisplay}    HP: {c.HitPoints}\n"
            + $"Armor Class: {c.ArmorClass}    Rogue Armor Profile: {c.RogueSkillArmorProfile}\n"
            + cpSummary
            + $"Unspent proficiency choices: {c.UnspentProficiencyChoices}"
            + $"\nHP by level: {hpAudit}"
            + (c.UnspentRogueSkillPoints > 0 ? $"    Unspent rogue skill points: {c.UnspentRogueSkillPoints}" : string.Empty);
    }

    private void RefreshGemInventoryList(CharacterSheet c)
    {
        if (CharGemList is null)
            return;

        CharGemList.Items.Clear();

        var gems = (c.Gems ?? new List<GemEntry>())
            .Where(x => Math.Max(1, x.Quantity) > 0)
            .OrderByDescending(x => Math.Max(0, x.ValueGoldPieces))
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (gems.Count == 0)
        {
            int gemCount = Math.Max(0, c.GemCount);
            int totalValue = Math.Max(0, c.GemValueGoldPieces);
            if (gemCount > 0)
                CharGemList.Items.Add($"Gems x{gemCount} ({totalValue} gp total)");
            else
                CharGemList.Items.Add("(none)");
            return;
        }

        foreach (var gem in gems)
        {
            int qty = Math.Max(1, gem.Quantity);
            int valueEach = Math.Max(0, gem.ValueGoldPieces);
            int valueTotal = qty * valueEach;
            CharGemList.Items.Add($"{gem.Name} x{qty} - {valueEach} gp each ({valueTotal} gp total)");
        }
    }

    private void RefreshCharacterInventorySections(CharacterSheet c)
    {
        if (CharInventorySectionsPanel is null)
            return;

        CharInventorySectionsPanel.Children.Clear();

        string search = (CharInventorySearchBox?.Text ?? string.Empty).Trim();
        string selectedCategory = CharInventoryCategoryFilter?.SelectedItem as string ?? CharacterInventoryAllCategories;
        string sortMode = CharInventorySort?.SelectedItem as string ?? CharacterInventorySortOptions[0];

        var inventoryRows = new List<(string Category, string Name, int Quantity, int UnitCopper, string DisplayText)>();

        foreach (var item in (c.EquipmentSelections ?? new List<EquipmentSelection>()).Where(x => x.Quantity > 0))
        {
            string category = string.IsNullOrWhiteSpace(item.Category)
                ? "Equipment"
                : EquipmentLibraryService.NormalizeCategoryLabel(item.Category);
            int unitCopper = CharacterWealthService.ToCopper(item.CostGoldEach, item.CostSilverEach, item.CostCopperEach);
            string display = unitCopper > 0
                ? $"{item.ItemName} x{item.Quantity} - {CharacterWealthService.FormatCoins(unitCopper)} each"
                : $"{item.ItemName} x{item.Quantity}";
            inventoryRows.Add((category, item.ItemName, item.Quantity, unitCopper, display));
        }

        foreach (var gem in (c.Gems ?? new List<GemEntry>()).Where(x => Math.Max(1, x.Quantity) > 0))
        {
            int quantity = Math.Max(1, gem.Quantity);
            int valueEachGp = Math.Max(0, gem.ValueGoldPieces);
            int unitCopper = CharacterWealthService.ToCopper(valueEachGp, 0, 0);
            string gemName = string.IsNullOrWhiteSpace(gem.Name) ? "Gem" : gem.Name.Trim();
            string display = $"{gemName} x{quantity} - {valueEachGp} gp each ({valueEachGp * quantity} gp total)";
            inventoryRows.Add(("Gems", gemName, quantity, unitCopper, display));
        }

        if (!inventoryRows.Any(x => string.Equals(x.Category, "Gems", StringComparison.OrdinalIgnoreCase)) && c.GemCount > 0)
        {
            int qty = Math.Max(0, c.GemCount);
            int totalValueGp = Math.Max(0, c.GemValueGoldPieces);
            int eachGp = qty > 0 ? Math.Max(0, totalValueGp / qty) : 0;
            int unitCopper = CharacterWealthService.ToCopper(eachGp, 0, 0);
            inventoryRows.Add(("Gems", "Gem", qty, unitCopper, $"Gem x{qty} - {totalValueGp} gp total"));
        }

        var filtered = inventoryRows
            .Where(x => selectedCategory == CharacterInventoryAllCategories
                || string.Equals(x.Category, selectedCategory, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(search)
                || x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.DisplayText.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == 0)
        {
            CharInventorySectionsPanel.Children.Add(new TextBlock
            {
                Text = "No inventory items.",
                Style = (Style)FindResource("SubtitleText"),
                Margin = new Thickness(6, 2, 0, 2),
            });
            return;
        }

        var grouped = filtered
            .GroupBy(x => x.Category)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in grouped)
        {
            var list = new ListBox
            {
                Style = (Style)FindResource("DarkListBox"),
                ItemContainerStyle = (Style)FindResource("DarkListBoxItem"),
                MinHeight = 48,
                MaxHeight = 170,
            };

            IEnumerable<(string Category, string Name, int Quantity, int UnitCopper, string DisplayText)> sortedGroup = sortMode switch
            {
                "Name (Z-A)" => group.OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase),
                "Quantity (High-Low)" => group.OrderByDescending(x => x.Quantity).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
                "Cost (High-Low)" => group.OrderByDescending(x => x.UnitCopper).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
                _ => group.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            };

            foreach (var item in sortedGroup)
                list.Items.Add(item.DisplayText);

            CharInventorySectionsPanel.Children.Add(new Expander
            {
                Header = $"{group.Key} ({group.Count()})",
                IsExpanded = true,
                Margin = new Thickness(0, 0, 0, 6),
                Content = list,
            });
        }
    }

    private void EnsureCharacterInventorySortOptions()
    {
        if (CharInventorySort is null)
            return;

        if (CharInventorySort.Items.Count == 0)
        {
            foreach (var option in CharacterInventorySortOptions)
                CharInventorySort.Items.Add(option);
            CharInventorySort.SelectedIndex = 0;
        }
    }

    private void PopulateCharacterInventoryCategoryFilter(CharacterSheet c)
    {
        if (CharInventoryCategoryFilter is null)
            return;

        var categories = new List<string> { CharacterInventoryAllCategories };
        categories.AddRange((c.EquipmentSelections ?? new List<EquipmentSelection>())
            .Where(x => x.Quantity > 0)
            .Select(x => string.IsNullOrWhiteSpace(x.Category)
                ? "Equipment"
                : EquipmentLibraryService.NormalizeCategoryLabel(x.Category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        bool hasGemEntries = (c.Gems?.Count ?? 0) > 0 || c.GemCount > 0;
        if (hasGemEntries && !categories.Contains("Gems", StringComparer.OrdinalIgnoreCase))
            categories.Add("Gems");

        string previous = CharInventoryCategoryFilter.SelectedItem as string ?? CharacterInventoryAllCategories;
        CharInventoryCategoryFilter.ItemsSource = categories;
        CharInventoryCategoryFilter.SelectedItem = categories.Contains(previous, StringComparer.OrdinalIgnoreCase)
            ? categories.First(x => string.Equals(x, previous, StringComparison.OrdinalIgnoreCase))
            : CharacterInventoryAllCategories;
    }

    private void CharInventorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshSelectedCharacterInventorySections();

    private void CharInventoryCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RefreshSelectedCharacterInventorySections();

    private void CharInventorySort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RefreshSelectedCharacterInventorySections();

    private void RefreshSelectedCharacterInventorySections()
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        RefreshCharacterInventorySections(_app.Characters[idx]);
    }

    private static void ResolvePurchaseUnitCost(
        CharacterSheet character,
        CustomEquipmentData item,
        bool useOverride,
        int overrideGp,
        int overrideSp,
        int overrideCp,
        out int unitGp,
        out int unitSp,
        out int unitCp)
    {
        if (useOverride)
        {
            unitGp = Math.Max(0, overrideGp);
            unitSp = Math.Max(0, overrideSp);
            unitCp = Math.Max(0, overrideCp);
            return;
        }

        ResolveUnitCostFromItemData(item, out unitGp, out unitSp, out unitCp);

        if (unitGp > 0 || unitSp > 0 || unitCp > 0)
            return;

        string canonicalItemId = EquipmentLibraryService.CanonicalizeId(item.Id);
        var existing = (character.EquipmentSelections ?? new List<EquipmentSelection>())
            .FirstOrDefault(x => string.Equals(EquipmentLibraryService.CanonicalizeId(x.ItemId), canonicalItemId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && (existing.CostGoldEach > 0 || existing.CostSilverEach > 0 || existing.CostCopperEach > 0))
        {
            unitGp = Math.Max(0, existing.CostGoldEach);
            unitSp = Math.Max(0, existing.CostSilverEach);
            unitCp = Math.Max(0, existing.CostCopperEach);
            return;
        }

        ResolveUnitCostFromItemData(item, out unitGp, out unitSp, out unitCp);
    }

    private static void ResolveUnitCostFromItemData(CustomEquipmentData item, out int gp, out int sp, out int cp)
    {
        gp = Math.Max(0, item.CostGold);
        sp = Math.Max(0, item.CostSilver);
        cp = Math.Max(0, item.CostCopper);

        if (gp > 0 || sp > 0 || cp > 0)
            return;

        if (TryExtractCoinTotals(item.Name, out int parsedGp, out int parsedSp, out int parsedCp)
            || TryExtractCoinTotals(item.Description, out parsedGp, out parsedSp, out parsedCp))
        {
            gp = parsedGp;
            sp = parsedSp;
            cp = parsedCp;
            return;
        }

        if (TryExtractBareGoldValue(item.Name, out int bareNameGp)
            || TryExtractBareGoldValue(item.Description, out bareNameGp))
        {
            gp = bareNameGp;
            sp = 0;
            cp = 0;
        }
    }

    private static bool TryExtractCoinTotals(string? text, out int gp, out int sp, out int cp)
    {
        gp = 0;
        sp = 0;
        cp = 0;

        string value = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        int totalCopper = 0;
        foreach (Match match in CoinValueRegex.Matches(value))
        {
            if (!int.TryParse(match.Groups["amount"].Value.Replace(",", string.Empty), out int amount))
                continue;

            string coin = match.Groups["coin"].Value.ToLowerInvariant();
            totalCopper += coin switch
            {
                "pp" => amount * 500,
                "gp" => amount * 100,
                "ep" => amount * 50,
                "sp" => amount * 10,
                "cp" => amount,
                _ => 0,
            };
        }

        if (totalCopper <= 0)
            return false;

        CharacterWealthService.FromCopper(totalCopper, out gp, out sp, out cp);
        return true;
    }

    private static bool TryExtractBareGoldValue(string? text, out int gp)
    {
        gp = 0;
        string value = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        int best = 0;
        foreach (Match match in BareValueRegex.Matches(value))
        {
            string lowText = match.Groups["low"].Value.Replace(",", string.Empty);
            if (!int.TryParse(lowText, out int low))
                continue;

            int candidate = low;
            string highText = match.Groups["high"].Value.Replace(",", string.Empty);
            if (match.Groups["high"].Success && int.TryParse(highText, out int high))
                candidate = Math.Max(low, high);

            if (candidate > best)
                best = candidate;
        }

        if (best <= 0)
            return false;

        gp = best;
        return true;
    }

    private static int ParseNonNegativeInt(string text)
    {
        return int.TryParse((text ?? string.Empty).Trim(), out var value)
            ? System.Math.Max(0, value)
            : 0;
    }

    private static string BuildCostText(CustomEquipmentData item)
    {
        var parts = new List<string>();
        if (item.CostGold > 0) parts.Add($"{item.CostGold}gp");
        if (item.CostSilver > 0) parts.Add($"{item.CostSilver}sp");
        if (item.CostCopper > 0) parts.Add($"{item.CostCopper}cp");
        return string.Join(" ", parts);
    }

    private static string BuildCostText(int gp, int sp, int cp)
    {
        var parts = new List<string>();
        if (gp > 0) parts.Add($"{gp}gp");
        if (sp > 0) parts.Add($"{sp}sp");
        if (cp > 0) parts.Add($"{cp}cp");
        return string.Join(" ", parts);
    }

    private static string FormatAddedFunds(int pp, int gp, int sp, int cp, int gems, int gemValueEachGp)
    {
        var parts = new List<string>();
        if (pp > 0) parts.Add($"{pp} pp");
        if (gp > 0) parts.Add($"{gp} gp");
        if (sp > 0) parts.Add($"{sp} sp");
        if (cp > 0) parts.Add($"{cp} cp");
        if (gems > 0)
        {
            int totalGp = Math.Max(0, gems * Math.Max(0, gemValueEachGp));
            parts.Add(totalGp > 0
                ? $"{gems} gem{(gems == 1 ? string.Empty : "s")} ({totalGp} gp value)"
                : $"{gems} gem{(gems == 1 ? string.Empty : "s")}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "0 cp";
    }

    private bool TryShowBuyItemDialog(
        IReadOnlyList<CustomEquipmentData> items,
        out CustomEquipmentData item,
        out int quantity,
        out bool useOverrideCost,
        out int overrideGp,
        out int overrideSp,
        out int overrideCp)
    {
        item = items[0];
        quantity = 1;
        useOverrideCost = false;
        overrideGp = 0;
        overrideSp = 0;
        overrideCp = 0;

        bool selectedOverride = false;
        int selectedOverrideGp = 0;
        int selectedOverrideSp = 0;
        int selectedOverrideCp = 0;

        var dialog = new Window
        {
            Title = "Buy Item",
            Width = 520,
            Height = 350,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Content = BuildBuyDialogContent(items, out ComboBox itemCombo, out TextBox qtyBox, out CheckBox overrideCheck, out TextBox gpBox, out TextBox spBox, out TextBox cpBox),
        };

        if (dialog.ShowDialog() != true)
            return false;

        if (itemCombo.SelectedItem is not CustomEquipmentData selected)
            return false;

        int parsedQty = ParseNonNegativeInt(qtyBox.Text);
        if (parsedQty <= 0)
        {
            MessageBox.Show("Quantity must be at least 1.", "Buy Item", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        selectedOverride = overrideCheck.IsChecked == true;
        if (selectedOverride)
        {
            selectedOverrideGp = ParseNonNegativeInt(gpBox.Text);
            selectedOverrideSp = ParseNonNegativeInt(spBox.Text);
            selectedOverrideCp = ParseNonNegativeInt(cpBox.Text);
        }

        item = selected;
        quantity = parsedQty;
        useOverrideCost = selectedOverride;
        overrideGp = selectedOverrideGp;
        overrideSp = selectedOverrideSp;
        overrideCp = selectedOverrideCp;
        return true;
    }

    private static UIElement BuildBuyDialogContent(
        IReadOnlyList<CustomEquipmentData> items,
        out ComboBox itemCombo,
        out TextBox qtyBox,
        out CheckBox overrideCheck,
        out TextBox gpBox,
        out TextBox spBox,
        out TextBox cpBox)
    {
        ComboBox itemComboLocal;
        TextBox qtyBoxLocal;
        CheckBox overrideCheckLocal;
        TextBox gpBoxLocal;
        TextBox spBoxLocal;
        TextBox cpBoxLocal;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock { Text = "Item", Margin = new Thickness(0, 0, 0, 4) });
        itemComboLocal = new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = 0,
            DisplayMemberPath = nameof(CustomEquipmentData.Name),
            Margin = new Thickness(0, 0, 0, 10),
            MinHeight = 26,
        };
        Grid.SetRow(itemComboLocal, 1);
        root.Children.Add(itemComboLocal);

        var qtyPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        qtyPanel.Children.Add(new TextBlock { Text = "Quantity", Width = 90, VerticalAlignment = VerticalAlignment.Center });
        qtyBoxLocal = new TextBox { Text = "1", Width = 90, VerticalContentAlignment = VerticalAlignment.Center };
        qtyPanel.Children.Add(qtyBoxLocal);
        Grid.SetRow(qtyPanel, 2);
        root.Children.Add(qtyPanel);

        overrideCheckLocal = new CheckBox
        {
            Content = "Override unit cost",
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(overrideCheckLocal, 3);
        root.Children.Add(overrideCheckLocal);

        var overrideGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        overrideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        overrideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        overrideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        overrideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        overrideGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        gpBoxLocal = new TextBox { Text = "0", VerticalContentAlignment = VerticalAlignment.Center };
        spBoxLocal = new TextBox { Text = "0", VerticalContentAlignment = VerticalAlignment.Center };
        cpBoxLocal = new TextBox { Text = "0", VerticalContentAlignment = VerticalAlignment.Center };

        var gpPanel = new StackPanel();
        gpPanel.Children.Add(new TextBlock { Text = "GP" });
        gpPanel.Children.Add(gpBoxLocal);
        Grid.SetColumn(gpPanel, 0);
        overrideGrid.Children.Add(gpPanel);

        var spPanel = new StackPanel();
        spPanel.Children.Add(new TextBlock { Text = "SP" });
        spPanel.Children.Add(spBoxLocal);
        Grid.SetColumn(spPanel, 2);
        overrideGrid.Children.Add(spPanel);

        var cpPanel = new StackPanel();
        cpPanel.Children.Add(new TextBlock { Text = "CP" });
        cpPanel.Children.Add(cpBoxLocal);
        Grid.SetColumn(cpPanel, 4);
        overrideGrid.Children.Add(cpPanel);

        Grid.SetRow(overrideGrid, 4);
        root.Children.Add(overrideGrid);

        var totalPreview = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(totalPreview, 5);
        root.Children.Add(totalPreview);

        void ToggleOverrideFields()
        {
            bool enabled = overrideCheckLocal.IsChecked == true;
            gpBoxLocal.IsEnabled = enabled;
            spBoxLocal.IsEnabled = enabled;
            cpBoxLocal.IsEnabled = enabled;
            UpdateTotalPreview();
        }

        void UpdateTotalPreview()
        {
            if (itemComboLocal.SelectedItem is not CustomEquipmentData selected)
            {
                totalPreview.Text = "Total: 0 cp";
                return;
            }

            int quantity = Math.Max(1, ParseNonNegativeInt(qtyBoxLocal.Text));
            bool useOverride = overrideCheckLocal.IsChecked == true;

            int unitGp;
            int unitSp;
            int unitCp;
            if (useOverride)
            {
                unitGp = ParseNonNegativeInt(gpBoxLocal.Text);
                unitSp = ParseNonNegativeInt(spBoxLocal.Text);
                unitCp = ParseNonNegativeInt(cpBoxLocal.Text);
            }
            else
            {
                ResolveUnitCostFromItemData(selected, out unitGp, out unitSp, out unitCp);
            }

            int totalGp = unitGp * quantity;
            int totalSp = unitSp * quantity;
            int totalCp = unitCp * quantity;
            int totalCopper = CharacterWealthService.ToCopper(totalGp, totalSp, totalCp);

            string unitText = BuildCostText(unitGp, unitSp, unitCp);
            string totalText = CharacterWealthService.FormatCoins(totalCopper);
            totalPreview.Text = string.IsNullOrWhiteSpace(unitText)
                ? $"Total: {totalText} ({quantity} item{(quantity == 1 ? string.Empty : "s")}, no unit cost recorded)"
                : $"Total: {totalText} ({quantity} item{(quantity == 1 ? string.Empty : "s")} @ {unitText})";
        }

        overrideCheckLocal.Checked += (_, _) => ToggleOverrideFields();
        overrideCheckLocal.Unchecked += (_, _) => ToggleOverrideFields();
        itemComboLocal.SelectionChanged += (_, _) => UpdateTotalPreview();
        qtyBoxLocal.TextChanged += (_, _) => UpdateTotalPreview();
        gpBoxLocal.TextChanged += (_, _) => UpdateTotalPreview();
        spBoxLocal.TextChanged += (_, _) => UpdateTotalPreview();
        cpBoxLocal.TextChanged += (_, _) => UpdateTotalPreview();
        ToggleOverrideFields();
        UpdateTotalPreview();

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Buy", Width = 88, IsDefault = true };
        ok.Click += (_, _) => Window.GetWindow(ok)!.DialogResult = true;
        buttonPanel.Children.Add(cancel);
        buttonPanel.Children.Add(ok);
        Grid.SetRow(buttonPanel, 6);
        root.Children.Add(buttonPanel);

        itemCombo = itemComboLocal;
        qtyBox = qtyBoxLocal;
        overrideCheck = overrideCheckLocal;
        gpBox = gpBoxLocal;
        spBox = spBoxLocal;
        cpBox = cpBoxLocal;

        return root;
    }

    private bool TryShowSellDialog(
        CharacterSheet character,
        out string removeItemId,
        out int removeQuantity,
        out int pp,
        out int gp,
        out int sp,
        out int cp,
        out int gems,
        out int gemValueEachGp,
        out string note)
    {
        removeItemId = string.Empty;
        removeQuantity = 0;
        pp = 0;
        gp = 0;
        sp = 0;
        cp = 0;
        gems = 0;
        gemValueEachGp = 0;
        note = string.Empty;

        var removableItems = (character.EquipmentSelections ?? new List<EquipmentSelection>())
            .Where(x => x.Quantity > 0)
            .OrderBy(x => x.ItemName)
            .ToList();

        var dialog = new Window
        {
            Title = "Sell / Unequip / Add Funds",
            Width = 520,
            Height = 430,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Content = BuildSellDialogContent(
                removableItems,
                out ComboBox itemCombo,
                out TextBox qtyBox,
                out TextBox ppBox,
                out TextBox gpBox,
                out TextBox spBox,
                out TextBox cpBox,
                out TextBox gemsBox,
                out TextBox gemValueBox,
                out TextBox noteBox),
        };

        if (dialog.ShowDialog() != true)
            return false;

        if (itemCombo.SelectedItem is EquipmentSelection selected)
        {
            int parsedQuantity = ParseNonNegativeInt(qtyBox.Text);
            if (parsedQuantity > 0)
            {
                removeItemId = selected.ItemId;
                removeQuantity = parsedQuantity;
            }
        }

        pp = ParseNonNegativeInt(ppBox.Text);
        gp = ParseNonNegativeInt(gpBox.Text);
        sp = ParseNonNegativeInt(spBox.Text);
        cp = ParseNonNegativeInt(cpBox.Text);
        gems = ParseNonNegativeInt(gemsBox.Text);
        gemValueEachGp = ParseNonNegativeInt(gemValueBox.Text);
        note = noteBox.Text;
        return true;
    }

    private static UIElement BuildSellDialogContent(
        IReadOnlyList<EquipmentSelection> removableItems,
        out ComboBox itemCombo,
        out TextBox qtyBox,
        out TextBox ppBox,
        out TextBox gpBox,
        out TextBox spBox,
        out TextBox cpBox,
        out TextBox gemsBox,
        out TextBox gemValueBox,
        out TextBox noteBox)
    {
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock { Text = "Optional: remove equipped item quantity", Margin = new Thickness(0, 0, 0, 6) });

        itemCombo = new ComboBox
        {
            ItemsSource = removableItems,
            DisplayMemberPath = nameof(EquipmentSelection.ItemName),
            SelectedIndex = removableItems.Count > 0 ? 0 : -1,
            IsEnabled = removableItems.Count > 0,
            MinHeight = 26,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(itemCombo, 1);
        root.Children.Add(itemCombo);

        var qtyPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        qtyPanel.Children.Add(new TextBlock { Text = "Qty to remove", Width = 120, VerticalAlignment = VerticalAlignment.Center });
        qtyBox = new TextBox { Text = "0", Width = 90, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = removableItems.Count > 0 };
        qtyPanel.Children.Add(qtyBox);
        qtyPanel.Children.Add(new TextBlock
        {
            Text = removableItems.Count > 0 ? "(0 = keep all items)" : "(no equipped items)",
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.8,
        });
        Grid.SetRow(qtyPanel, 2);
        root.Children.Add(qtyPanel);

        root.Children.Add(new TextBlock { Text = "Optional: add sale funds", Margin = new Thickness(0, 0, 0, 8) });
        Grid.SetRow(root.Children[^1], 3);

        var coinGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        coinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        coinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        coinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        coinGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        ppBox = new TextBox { Text = "0", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        gpBox = new TextBox { Text = "0", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        spBox = new TextBox { Text = "0", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        cpBox = new TextBox { Text = "0", VerticalContentAlignment = VerticalAlignment.Center };

        var ppPanel = new StackPanel();
        ppPanel.Children.Add(new TextBlock { Text = "PP" });
        ppPanel.Children.Add(ppBox);
        Grid.SetColumn(ppPanel, 0);
        coinGrid.Children.Add(ppPanel);

        var gpPanel = new StackPanel();
        gpPanel.Children.Add(new TextBlock { Text = "GP" });
        gpPanel.Children.Add(gpBox);
        Grid.SetColumn(gpPanel, 1);
        coinGrid.Children.Add(gpPanel);

        var spPanel = new StackPanel();
        spPanel.Children.Add(new TextBlock { Text = "SP" });
        spPanel.Children.Add(spBox);
        Grid.SetColumn(spPanel, 2);
        coinGrid.Children.Add(spPanel);

        var cpPanel = new StackPanel();
        cpPanel.Children.Add(new TextBlock { Text = "CP" });
        cpPanel.Children.Add(cpBox);
        Grid.SetColumn(cpPanel, 3);
        coinGrid.Children.Add(cpPanel);

        Grid.SetRow(coinGrid, 4);
        root.Children.Add(coinGrid);

        var gemGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        gemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        gemsBox = new TextBox { Text = "0", Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        gemValueBox = new TextBox { Text = "0", VerticalContentAlignment = VerticalAlignment.Center };

        var gemsPanel = new StackPanel();
        gemsPanel.Children.Add(new TextBlock { Text = "Gems Qty" });
        gemsPanel.Children.Add(gemsBox);
        Grid.SetColumn(gemsPanel, 0);
        gemGrid.Children.Add(gemsPanel);

        var gemValuePanel = new StackPanel();
        gemValuePanel.Children.Add(new TextBlock { Text = "Gem Value Each (GP)" });
        gemValuePanel.Children.Add(gemValueBox);
        Grid.SetColumn(gemValuePanel, 1);
        gemGrid.Children.Add(gemValuePanel);

        Grid.SetRow(gemGrid, 5);
        root.Children.Add(gemGrid);

        root.Children.Add(new TextBlock { Text = "Note (optional)", Margin = new Thickness(0, 0, 0, 4) });
        Grid.SetRow(root.Children[^1], 6);

        noteBox = new TextBox { Margin = new Thickness(0, 0, 0, 8), MinHeight = 26 };
        Grid.SetRow(noteBox, 7);
        root.Children.Add(noteBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Apply", Width = 88, IsDefault = true };
        ok.Click += (_, _) => Window.GetWindow(ok)!.DialogResult = true;
        buttonPanel.Children.Add(cancel);
        buttonPanel.Children.Add(ok);
        Grid.SetRow(buttonPanel, 8);
        root.Children.Add(buttonPanel);

        return root;
    }

    private void BtnSaveNotes_Click(object sender, RoutedEventArgs e)
    {
        int idx = CharList.SelectedIndex;
        if (idx < 0 || idx >= _app.Characters.Count) return;
        var c = _app.Characters[idx];
        c.Notes.Clear();
        foreach (var line in CharNotes.Text.Split('\n'))
        {
            var t = line.Trim();
            if (!string.IsNullOrEmpty(t)) c.Notes.Add(t);
        }
        c.LastModified = System.DateTime.Now;
        c.Revision += 1;
        _app.SaveCharacters();
        MessageBox.Show("Notes saved.", "Saved",
                        MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Races tab ───────────────────────────────────────────────────────────

    private void RefreshRaceList()
    {
        RaceListEditor.Items.Clear();
        var ordered = GetOrderedRacesForEditor();
        foreach (var race in ordered)
            RaceListEditor.Items.Add(FormatRaceEditorLabel(race));

        if (!string.IsNullOrEmpty(_selectedRaceId) && _app.Rules.Races.ContainsKey(_selectedRaceId))
        {
            RaceListEditor.SelectedIndex = ordered.FindIndex(r => r.Id == _selectedRaceId);
        }
        else
        {
            _selectedRaceId = "";
            ClearRaceEditor();
        }
    }

    private void RaceListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = RaceListEditor.SelectedIndex;
        if (idx < 0) return;

        var ordered = GetOrderedRacesForEditor();
        if (idx >= ordered.Count) return;
        LoadRaceEditor(ordered[idx]);
    }

    private void LoadRaceEditor(RaceDefinition race)
    {
        _selectedRaceId = race.Id;
        RaceId.Text = race.Id;
        RaceName.Text = race.Name;
        RaceBaseId.Text = race.BaseRaceId;
        SelectMode(race.CharacterMode);

        SetAbilityBox(RaceStrMin, race.AbilityMinimums, "str");
        SetAbilityBox(RaceDexMin, race.AbilityMinimums, "dex");
        SetAbilityBox(RaceConMin, race.AbilityMinimums, "con");
        SetAbilityBox(RaceIntMin, race.AbilityMinimums, "int");
        SetAbilityBox(RaceWisMin, race.AbilityMinimums, "wis");
        SetAbilityBox(RaceChaMin, race.AbilityMinimums, "cha");

        SetAbilityBox(RaceStrMax, race.AbilityMaximums, "str");
        SetAbilityBox(RaceDexMax, race.AbilityMaximums, "dex");
        SetAbilityBox(RaceConMax, race.AbilityMaximums, "con");
        SetAbilityBox(RaceIntMax, race.AbilityMaximums, "int");
        SetAbilityBox(RaceWisMax, race.AbilityMaximums, "wis");
        SetAbilityBox(RaceChaMax, race.AbilityMaximums, "cha");

        RacePointBudget.Text = race.RacialPointBudget.ToString();
        var legacyLines = race.RacialAbilities.Count > 0
            ? race.RacialAbilities
            : race.StructuredAbilities.Select(a => a.Description).Where(d => !string.IsNullOrWhiteSpace(d)).ToList();
        RaceAbilities.Text = string.Join("\n", legacyLines);
        RaceAbilityDefs.Text = string.Join("\n", race.StructuredAbilities.Select(FormatAbilityDefLine));
    }

    private void BtnNewRace_Click(object sender, RoutedEventArgs e)
    {
        _selectedRaceId = "";
        RaceListEditor.SelectedIndex = -1;
        ClearRaceEditor();
        SelectMode("players_option");
    }

    private void BtnSaveRace_Click(object sender, RoutedEventArgs e)
    {
        var raceId = RaceId.Text.Trim().ToLowerInvariant();
        var raceName = RaceName.Text.Trim();
        if (string.IsNullOrWhiteSpace(raceId) || string.IsNullOrWhiteSpace(raceName))
        {
            MessageBox.Show("Race Id and Display Name are required.", "Missing Information",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var baseRaceId = string.IsNullOrWhiteSpace(RaceBaseId.Text)
                ? raceId
                : RaceBaseId.Text.Trim().ToLowerInvariant();

            _app.Rules.Races.TryGetValue(raceId, out var existingRace);

            var existingEffects = existingRace is not null
                ? existingRace.StructuredAbilities.ToDictionary(a => a.Id, a => a.Effect, System.StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, AbilityEffect>(System.StringComparer.OrdinalIgnoreCase);

            var legacyAbilityLines = ReadRaceAbilityTextLines();
            var structuredAbilities = ParseAbilityDefs(RaceAbilityDefs.Text, existingEffects);
            if (structuredAbilities.Count == 0 && legacyAbilityLines.Count > 0)
                structuredAbilities = BuildFallbackStructuredAbilitiesFromLegacyLines(legacyAbilityLines, existingEffects);

            var displayAbilityLines = legacyAbilityLines.Count > 0
                ? legacyAbilityLines
                : structuredAbilities.Select(a => a.Description).Where(d => !string.IsNullOrWhiteSpace(d)).ToList();

            var race = new RaceDefinition(
                raceId,
                raceName,
                SelectedMode(),
                baseRaceId,
                ReadAbilityMap(isMinimum: true),
                ReadAbilityMap(isMinimum: false),
                new(),  // AbilityModifiers - empty for now, can be edited via UI later
                displayAbilityLines,
                structuredAbilities,
                ParseIntOrDefault(RacePointBudget.Text, 0),
                existingRace?.Source ?? "custom");

            var autoAssignedSpent = race.StructuredAbilities
                .Where(a => a.AutoGranted)
                .Sum(a => a.PointCost);
            if (race.RacialPointBudget > 0 && autoAssignedSpent > race.RacialPointBudget)
            {
                MessageBox.Show(
                    $"Cannot save race '{race.Name}'. Auto-assigned abilities spend {autoAssignedSpent} CP but budget is {race.RacialPointBudget} CP.\n\n" +
                    "Adjust point costs, mark some entries as optional, or increase the race budget.",
                    "Invalid Racial CP Budget",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _app.Rules.SaveRace(race);
            _selectedRaceId = race.Id;
            RefreshRaceList();
            MessageBox.Show("Race saved.", "Saved",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Save Race",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnDeleteRace_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedRaceId)) return;

        var result = MessageBox.Show("Delete this race from the ruleset?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            _app.Rules.DeleteRace(_selectedRaceId);
            _selectedRaceId = "";
            RefreshRaceList();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Delete Race",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Dictionary<string, int> ReadAbilityMap(bool isMinimum)
    {
        var map = new Dictionary<string, int>();
        ReadAbilityValue(map, "str", isMinimum ? RaceStrMin : RaceStrMax);
        ReadAbilityValue(map, "dex", isMinimum ? RaceDexMin : RaceDexMax);
        ReadAbilityValue(map, "con", isMinimum ? RaceConMin : RaceConMax);
        ReadAbilityValue(map, "int", isMinimum ? RaceIntMin : RaceIntMax);
        ReadAbilityValue(map, "wis", isMinimum ? RaceWisMin : RaceWisMax);
        ReadAbilityValue(map, "cha", isMinimum ? RaceChaMin : RaceChaMax);
        return map;
    }

    private static void ReadAbilityValue(Dictionary<string, int> map, string key, TextBox box)
    {
        var text = box.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (!int.TryParse(text, out var value))
            throw new System.InvalidOperationException($"{key.ToUpperInvariant()} must be a number.");
        map[key] = value;
    }

    private static void SetAbilityBox(TextBox box, Dictionary<string, int> map, string key)
    {
        box.Text = map.TryGetValue(key, out var value) ? value.ToString() : "";
    }

    private void ClearRaceEditor()
    {
        RaceId.Text = "";
        RaceName.Text = "";
        RaceBaseId.Text = "";
        SelectMode("all");
        foreach (var box in new[]
                 { RaceStrMin, RaceDexMin, RaceConMin, RaceIntMin, RaceWisMin, RaceChaMin,
                   RaceStrMax, RaceDexMax, RaceConMax, RaceIntMax, RaceWisMax, RaceChaMax })
            box.Text = "";
        RacePointBudget.Text = "0";
        RaceAbilities.Text = "";
        RaceAbilityDefs.Text = "";
    }

    private List<string> ReadRaceAbilityTextLines()
    {
        return RaceAbilities.Text
            .Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .ToList();
    }

    private static List<AbilityDefinition> BuildFallbackStructuredAbilitiesFromLegacyLines(
        IReadOnlyList<string> lines,
        IReadOnlyDictionary<string, AbilityEffect> existingEffects)
    {
        var usedIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var result = new List<AbilityDefinition>();

        foreach (var line in lines)
        {
            string baseId = SlugifySimple(line);
            if (string.IsNullOrWhiteSpace(baseId))
                baseId = "race_ability";

            string id = baseId;
            int suffix = 2;
            while (!usedIds.Add(id))
            {
                id = $"{baseId}_{suffix}";
                suffix += 1;
            }

            var effect = existingEffects.TryGetValue(id, out var existingFx)
                ? existingFx
                : new AbilityEffect();

            result.Add(new AbilityDefinition
            {
                Id = id,
                Description = line,
                PointCost = 0,
                AutoGranted = true,
                Effect = effect,
            });
        }

        return result;
    }

    private static int ParseIntOrDefault(string text, int fallback)
    {
        return int.TryParse(text.Trim(), out var v) ? v : fallback;
    }

    private static string FormatAbilityDefLine(AbilityDefinition def)
    {
        var safeDesc = def.Description.Replace("|", "/");
        var mechanicsJson = SerializeEffect(def.Effect);
        return $"{def.Id}|{def.PointCost}|{def.AutoGranted.ToString().ToLowerInvariant()}|{safeDesc}|{mechanicsJson}";
    }

    private static List<AbilityDefinition> ParseAbilityDefs(string raw, Dictionary<string, AbilityEffect> existingEffects)
    {
        var result = new List<AbilityDefinition>();
        foreach (var line in raw.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var parts = trimmed.Split('|');
            if (parts.Length < 4)
            {
                // Fallback: treat as plain description
                result.Add(new AbilityDefinition
                {
                    Id = trimmed.ToLowerInvariant().Replace(' ', '_'),
                    Description = trimmed,
                    PointCost = 0,
                    AutoGranted = true,
                    Effect = existingEffects.TryGetValue(trimmed, out var fx0) ? fx0 : new AbilityEffect(),
                });
                continue;
            }

            var id = parts[0].Trim();
            var cost = int.TryParse(parts[1].Trim(), out var c) ? c : 0;
            var auto = bool.TryParse(parts[2].Trim(), out var a) ? a : true;
            var hasMechanicsJson = parts.Length >= 5;
            var desc = hasMechanicsJson
                ? string.Join("|", parts.Skip(3).Take(parts.Length - 4)).Trim()
                : string.Join("|", parts.Skip(3)).Trim();

            var keyId = string.IsNullOrWhiteSpace(id) ? desc.ToLowerInvariant().Replace(' ', '_') : id;
            var effect = existingEffects.TryGetValue(keyId, out var existingFx) ? existingFx : new AbilityEffect();

            if (hasMechanicsJson)
            {
                var rawJson = parts[^1].Trim();
                var parsed = ParseEffect(rawJson);
                if (parsed is not null) effect = parsed;
            }

            result.Add(new AbilityDefinition
            {
                Id = keyId,
                Description = desc,
                PointCost = cost,
                AutoGranted = auto,
                Effect = effect,
            });
        }
        return result;
    }

    private static string SerializeEffect(AbilityEffect effect)
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        var json = JsonSerializer.Serialize(effect, options);
        // Keep compact lines in the DM editor.
        return string.IsNullOrWhiteSpace(json) ? "{}" : json;
    }

    private static AbilityEffect? ParseEffect(string rawJson)
    {
        var text = (rawJson ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || text == "{}") return new AbilityEffect();

        // Accept easier key=value shorthand (semicolon/comma separated) for non-JSON users.
        var shorthand = ParseEffectShorthand(text);
        if (shorthand is not null)
            return shorthand;

        try
        {
            return JsonSerializer.Deserialize<AbilityEffect>(text) ?? new AbilityEffect();
        }
        catch
        {
            return null;
        }
    }

    private static AbilityEffect? ParseEffectShorthand(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new AbilityEffect();

        // If the entry looks like JSON, let JSON parsing handle it.
        if (text.StartsWith("{", StringComparison.Ordinal) || text.StartsWith("[", StringComparison.Ordinal))
            return null;

        var effect = new AbilityEffect();
        int applied = 0;

        var tokens = text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var tokenRaw in tokens)
        {
            var token = tokenRaw.Trim();
            if (token.Length == 0)
                continue;

            string key;
            string value;
            int equalsIdx = token.IndexOf('=');
            if (equalsIdx >= 0)
            {
                key = token[..equalsIdx].Trim();
                value = token[(equalsIdx + 1)..].Trim();
            }
            else
            {
                key = token;
                value = "true";
            }

            if (TryApplyShorthandValue(effect, key, value))
                applied += 1;
        }

        return applied > 0 ? effect : null;
    }

    private static bool TryApplyShorthandValue(AbilityEffect effect, string keyRaw, string valueRaw)
    {
        var key = (keyRaw ?? string.Empty).Trim().ToLowerInvariant();
        var value = (valueRaw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (TryParsePrefixedMapInt(key, value, "save.", effect.SaveBonuses)) return true;
        if (TryParsePrefixedMapInt(key, value, "sub.", effect.SubAbilityBonuses)) return true;
        if (TryParsePrefixedMapInt(key, value, "enemy_attack.", effect.EnemyAttackBonuses)) return true;
        if (TryParsePrefixedMapInt(key, value, "enemy_damage.", effect.EnemyDamageBonuses)) return true;
        if (TryParsePrefixedMapInt(key, value, "weapon_attack.", effect.WeaponAttackBonuses)) return true;
        if (TryParsePrefixedMapInt(key, value, "weapon_damage.", effect.WeaponDamageBonuses)) return true;
        if (TryParsePrefixedMapInt(key, value, "nwp_check.", effect.NwpCheckBonusesById)) return true;

        switch (key)
        {
            case "ac":
            case "ac_bonus":
                if (!TryParseIntInto(value, out int acBonus)) return false;
                effect.AcBonus = acBonus;
                return true;
            case "attack":
            case "attack_bonus":
                if (!TryParseIntInto(value, out int attackBonus)) return false;
                effect.AttackBonus = attackBonus;
                return true;
            case "attack_per_levels":
            case "attack_bonus_per_levels":
                if (!TryParseIntInto(value, out int attackPerLevels)) return false;
                effect.AttackBonusPerLevels = Math.Max(0, attackPerLevels);
                return true;
            case "damage":
            case "damage_bonus":
                if (!TryParseIntInto(value, out int damageBonus)) return false;
                effect.DamageBonus = damageBonus;
                return true;
            case "movement":
            case "movement_bonus":
                if (!TryParseIntInto(value, out int movementBonus)) return false;
                effect.MovementBonus = movementBonus;
                return true;
            case "hp_per_level":
                if (!TryParseIntInto(value, out int hpPerLevel)) return false;
                effect.HpPerLevel = hpPerLevel;
                return true;
            case "hp_flat":
            case "hp_flat_bonus":
                if (!TryParseIntInto(value, out int hpFlatBonus)) return false;
                effect.HpFlatBonus = hpFlatBonus;
                return true;
            case "xp_mod":
            case "xp_modifier":
            case "xp_modifier_percent":
                if (!TryParseIntInto(value, out int xpModifierPercent)) return false;
                effect.XpModifierPercent = xpModifierPercent;
                return true;
            case "nwp_slots":
                if (!TryParseIntInto(value, out int nwpSlotBonus)) return false;
                effect.NwpSlotBonus = nwpSlotBonus;
                return true;
            case "nwp_cost_reduction":
            case "nwp_cost_red":
                if (!TryParseIntInto(value, out int nwpCostReduction)) return false;
                effect.NwpCostReduction = nwpCostReduction;
                return true;
            case "nwp_check":
                if (!TryParseIntInto(value, out int nwpCheckBonus)) return false;
                effect.NwpCheckBonus = nwpCheckBonus;
                return true;
            case "surprise":
            case "surprise_bonus":
                if (!TryParseIntInto(value, out int surpriseBonus)) return false;
                effect.SurpriseBonus = surpriseBonus;
                return true;
            case "reaction":
            case "reaction_bonus":
                if (!TryParseIntInto(value, out int reactionBonus)) return false;
                effect.ReactionBonus = reactionBonus;
                return true;
            case "infravision":
            case "infravision_feet":
                if (!TryParseIntInto(value, out int infravisionFeet)) return false;
                effect.InfravisionFeet = infravisionFeet;
                return true;
            case "magic_resist":
            case "magic_resist_percent":
            case "magic_resistance":
                if (!TryParseIntInto(value, out int magicResistPercent)) return false;
                effect.MagicResistPercent = magicResistPercent;
                return true;
            case "hp_dice":
            case "hp_dice_expr":
            case "hp_dice_expression":
                effect.HpDiceExpression = string.IsNullOrWhiteSpace(value) ? null : value;
                return true;
            case "no_armor_ac":
            case "ac_requires_no_armor":
                if (!TryParseBoolInto(value, out bool acRequiresNoArmor)) return false;
                effect.AcBonusRequiresNoArmor = acRequiresNoArmor;
                return true;
            case "stealth":
                if (!TryParseBoolInto(value, out bool grantsStealth)) return false;
                effect.GrantsStealth = grantsStealth;
                return true;
            case "detect_secret_doors":
                if (!TryParseBoolInto(value, out bool detectSecretDoors)) return false;
                effect.DetectSecretDoors = detectSecretDoors;
                return true;
            case "detect_stonework":
                if (!TryParseBoolInto(value, out bool detectStonework)) return false;
                effect.DetectStonework = detectStonework;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParsePrefixedMapInt(string key, string value, string prefix, Dictionary<string, int> target)
    {
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string mapKey = key[prefix.Length..].Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(mapKey) || !int.TryParse(value, out int mapValue))
            return false;

        target[mapKey] = mapValue;
        return true;
    }

    private static bool TryParseIntInto(string raw, out int value)
    {
        if (int.TryParse(raw, out value))
            return true;

        value = 0;
        return false;
    }

    private static bool TryParseBoolInto(string raw, out bool value)
    {
        var text = (raw ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Length == 0)
        {
            value = true;
            return true;
        }

        if (bool.TryParse(text, out value))
            return true;

        if (text is "1" or "yes" or "y" or "on")
        {
            value = true;
            return true;
        }

        if (text is "0" or "no" or "n" or "off")
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private string SelectedMode()
    {
        if (RaceMode.SelectedItem is ComboBoxItem item && item.Tag is string tag) return tag;
        return "all";
    }

    private string SelectedClassRulesMode()
    {
        if (ClassDefRulesMode.SelectedItem is ComboBoxItem item && item.Tag is string tag) return tag;
        return "all";
    }

    private void SelectMode(string mode)
    {
        foreach (var item in RaceMode.Items)
        {
            if (item is ComboBoxItem combo && (combo.Tag as string) == mode)
            {
                RaceMode.SelectedItem = combo;
                return;
            }
        }
        RaceMode.SelectedIndex = 0;
    }

    private void SelectClassRulesMode(string mode)
    {
        foreach (var item in ClassDefRulesMode.Items)
        {
            if (item is ComboBoxItem combo && (combo.Tag as string) == mode)
            {
                ClassDefRulesMode.SelectedItem = combo;
                return;
            }
        }
        ClassDefRulesMode.SelectedIndex = 0;
    }

    private static string ModeLabel(string mode) => mode switch
    {
        "core_rules" => "Core Rules",
        "players_option" => "Player's Option",
        _ => "All Characters",
    };

    private static string SourceLabel(string source)
        => string.Equals(source, "custom", System.StringComparison.OrdinalIgnoreCase)
            ? "CUSTOM"
            : "CORE";

    private static string FormatRaceEditorLabel(RaceDefinition race)
        => $"{race.Name}  [{ModeLabel(race.CharacterMode)}]  [{SourceLabel(race.Source)}]";

    private static string FormatClassEditorLabel(ClassDefinition cls)
    {
        string modeLabel = (cls.RulesMode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "core_rules" => "Core",
            "players_option" => "PO",
            _ => "All",
        };
        return $"{cls.Name} [{cls.Id}] [{modeLabel}] [{SourceLabel(cls.Source)}]";
    }

    private void BtnExportCustomPack_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export Custom Races and Classes",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "dmcortex_custom_pack.json",
            AddExtension = true,
            DefaultExt = ".json",
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            _app.Rules.ExportCustomRacesAndClasses(dlg.FileName);
            MessageBox.Show($"Custom pack exported to:\n{dlg.FileName}",
                "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}",
                "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnImportCustomPack_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Custom Races and Classes",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var (racesImported, classesImported) = _app.Rules.ImportCustomRacesAndClasses(dlg.FileName);
            RefreshRaceList();
            RefreshClassEditorList();
            RefreshClassAbilityEditorList();
            RefreshRacialAbilityEditorList();

            MessageBox.Show(
                $"Import complete.\n\nRaces imported: {racesImported}\nClasses imported: {classesImported}",
                "Import Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Import failed: {ex.Message}",
                "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Traits tab ───────────────────────────────────────────────────────────

    private void RefreshTraitEditorList()
    {
        string search = TraitSearch.Text.Trim();
        _traitItems = _app.CharacterOptions.GetCatalog().Traits
            .Where(t => string.IsNullOrWhiteSpace(search)
                || t.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                || t.Description.Contains(search, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name)
            .ToList();

        var customIds = _app.CharacterOptions.GetCustomTraits();
        TraitListEditor.ItemsSource = _traitItems
            .Select(t => (customIds.ContainsKey(t.Id) ? "★ " : string.Empty) + $"{t.Name} ({t.Cost} CP)")
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedTraitId))
        {
            int idx = _traitItems.FindIndex(x => string.Equals(x.Id, _selectedTraitId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) TraitListEditor.SelectedIndex = idx;
        }
        if (TraitListEditor.SelectedIndex < 0 && _traitItems.Count > 0)
            TraitListEditor.SelectedIndex = 0;
    }

    private void TraitSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TabTraits.Visibility == Visibility.Visible)
            RefreshTraitEditorList();
    }

    private void TraitListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = TraitListEditor.SelectedIndex;
        if (idx < 0 || idx >= _traitItems.Count) return;
        var trait = _traitItems[idx];
        _selectedTraitId = trait.Id;
        TraitName.Text = trait.Name;
        TraitCost.Text = trait.Cost.ToString();
        TraitDescription.Text = trait.Description;
        bool isCustom = _app.CharacterOptions.GetCustomTraits().ContainsKey(trait.Id);
        BtnDeleteTrait.IsEnabled = isCustom;
        TraitEditorInfo.Text = isCustom ? "★ Custom / overridden trait" : string.Empty;
    }

    private void BtnNewTrait_Click(object sender, RoutedEventArgs e)
    {
        _selectedTraitId = string.Empty;
        TraitListEditor.SelectedIndex = -1;
        TraitName.Text = string.Empty;
        TraitCost.Text = "0";
        TraitDescription.Text = string.Empty;
        BtnDeleteTrait.IsEnabled = false;
        TraitEditorInfo.Text = "Fill in the fields and click SAVE TRAIT.";
    }

    private void BtnSaveTrait_Click(object sender, RoutedEventArgs e)
    {
        string name = TraitName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Trait name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(TraitCost.Text.Trim(), out int cost)) cost = 0;

        string id = string.IsNullOrWhiteSpace(_selectedTraitId)
            ? $"trait_custom_{SlugifySimple(name)}"
            : _selectedTraitId;

        _app.CharacterOptions.SaveCustomTrait(new CustomTraitData
        {
            Id = id,
            Name = name,
            Cost = cost,
            Description = TraitDescription.Text.Trim(),
        });
        _app.CharacterOptions.InvalidateCache();
        _selectedTraitId = id;
        TraitEditorInfo.Text = $"★ Saved: {name}";
        RefreshTraitEditorList();
    }

    private void BtnDeleteTrait_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedTraitId)) return;
        if (MessageBox.Show("Delete this custom trait?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _app.CharacterOptions.DeleteCustomTrait(_selectedTraitId);
        _app.CharacterOptions.InvalidateCache();
        _selectedTraitId = string.Empty;
        RefreshTraitEditorList();
    }

    // ── Disadvantages tab ────────────────────────────────────────────────────

    private void RefreshDisadvantageEditorList()
    {
        string search = DisadvantageSearch.Text.Trim();
        _disadvantageItems = _app.CharacterOptions.GetCatalog().Disadvantages
            .Where(d => string.IsNullOrWhiteSpace(search)
                || d.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                || d.Description.Contains(search, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Name)
            .ToList();

        var customIds = _app.CharacterOptions.GetCustomDisadvantages();
        DisadvantageListEditor.ItemsSource = _disadvantageItems
            .Select(d => (customIds.ContainsKey(d.Id) ? "★ " : string.Empty) +
                $"{d.Name} (+{d.ModerateBonus}{(d.SevereBonus.HasValue ? $" / +{d.SevereBonus.Value}" : string.Empty)} CP)")
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedDisadvantageId))
        {
            int idx = _disadvantageItems.FindIndex(x => string.Equals(x.Id, _selectedDisadvantageId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) DisadvantageListEditor.SelectedIndex = idx;
        }
        if (DisadvantageListEditor.SelectedIndex < 0 && _disadvantageItems.Count > 0)
            DisadvantageListEditor.SelectedIndex = 0;
    }

    private void DisadvantageSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TabDisadvantages.Visibility == Visibility.Visible)
            RefreshDisadvantageEditorList();
    }

    private void DisadvantageListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = DisadvantageListEditor.SelectedIndex;
        if (idx < 0 || idx >= _disadvantageItems.Count) return;
        var dis = _disadvantageItems[idx];
        _selectedDisadvantageId = dis.Id;
        DisadvantageName.Text = dis.Name;
        DisadvantageModerate.Text = dis.ModerateBonus.ToString();
        DisadvantageSevere.Text = dis.SevereBonus?.ToString() ?? string.Empty;
        DisadvantageDescription.Text = dis.Description;
        bool isCustom = _app.CharacterOptions.GetCustomDisadvantages().ContainsKey(dis.Id);
        BtnDeleteDisadvantage.IsEnabled = isCustom;
        DisadvantageEditorInfo.Text = isCustom ? "★ Custom / overridden disadvantage" : string.Empty;
    }

    private void BtnNewDisadvantage_Click(object sender, RoutedEventArgs e)
    {
        _selectedDisadvantageId = string.Empty;
        DisadvantageListEditor.SelectedIndex = -1;
        DisadvantageName.Text = string.Empty;
        DisadvantageModerate.Text = "1";
        DisadvantageSevere.Text = string.Empty;
        DisadvantageDescription.Text = string.Empty;
        BtnDeleteDisadvantage.IsEnabled = false;
        DisadvantageEditorInfo.Text = "Fill in the fields and click SAVE DISADVANTAGE.";
    }

    private void BtnSaveDisadvantage_Click(object sender, RoutedEventArgs e)
    {
        string name = DisadvantageName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Disadvantage name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(DisadvantageModerate.Text.Trim(), out int moderate)) moderate = 1;
        int? severe = null;
        if (int.TryParse(DisadvantageSevere.Text.Trim(), out int severeValue)) severe = severeValue;

        string id = string.IsNullOrWhiteSpace(_selectedDisadvantageId)
            ? $"disadv_custom_{SlugifySimple(name)}"
            : _selectedDisadvantageId;

        _app.CharacterOptions.SaveCustomDisadvantage(new CustomDisadvantageData
        {
            Id = id,
            Name = name,
            ModerateBonus = moderate,
            SevereBonus = severe,
            Description = DisadvantageDescription.Text.Trim(),
        });
        _app.CharacterOptions.InvalidateCache();
        _selectedDisadvantageId = id;
        DisadvantageEditorInfo.Text = $"★ Saved: {name}";
        RefreshDisadvantageEditorList();
    }

    private void BtnDeleteDisadvantage_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedDisadvantageId)) return;
        if (MessageBox.Show("Delete this custom disadvantage?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _app.CharacterOptions.DeleteCustomDisadvantage(_selectedDisadvantageId);
        _app.CharacterOptions.InvalidateCache();
        _selectedDisadvantageId = string.Empty;
        RefreshDisadvantageEditorList();
    }

    // ── Kits tab ─────────────────────────────────────────────────────────────

    private void RefreshKitEditorList()
    {
        string search = KitEditorSearch.Text.Trim();
        string sourceFilter = (KitSourceFilter.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "";
        bool allSources = sourceFilter == "All Sources" || string.IsNullOrEmpty(sourceFilter);

        _kitItems = _app.Rules.Kits
            .Where(k =>
                (string.IsNullOrWhiteSpace(search)
                    || k.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || k.Description.Contains(search, System.StringComparison.OrdinalIgnoreCase))
                && KitMatchesRulesModeFilter(k)
                && (allSources || string.Equals(k.Source, sourceFilter, System.StringComparison.OrdinalIgnoreCase)))
            .OrderBy(k => k.Source)
            .ThenBy(k => k.Name)
            .ToList();

        KitListEditor.ItemsSource = _kitItems
            .Select(k => $"{k.Name}  [{KitRulesModeLabel(k.RulesMode)}] [{k.Source}]")
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedKitId))
        {
            int idx = _kitItems.FindIndex(x => string.Equals(x.Id, _selectedKitId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) KitListEditor.SelectedIndex = idx;
        }
        if (KitListEditor.SelectedIndex < 0 && _kitItems.Count > 0)
            KitListEditor.SelectedIndex = 0;
    }

    private void KitEditorSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TabKits.Visibility == Visibility.Visible)
            RefreshKitEditorList();
    }

    private void KitSourceFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TabKits.Visibility == Visibility.Visible)
            RefreshKitEditorList();
    }

    private void KitListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = KitListEditor.SelectedIndex;
        if (idx < 0 || idx >= _kitItems.Count) return;
        var kit = _kitItems[idx];
        _selectedKitId = kit.Id;
        KitEditName.Text = kit.Name;
        KitEditSource.Text = kit.Source;
        SelectKitRulesMode(KitEditRulesMode, kit.RulesMode);
        KitEditAllowedRaces.Text = string.Join(", ", kit.AllowedRaces);
        KitEditAllowedClasses.Text = string.Join(", ", kit.AllowedClasses);
        KitEditDescription.Text = kit.Description;
        bool isCustom = kit.Source.Equals("Custom", System.StringComparison.OrdinalIgnoreCase)
                     || !_app.Rules.Kits.Any(k => k.Id == kit.Id && k.Source != "Custom");
        BtnDeleteKit.IsEnabled = true; // all kits can be deleted/replaced via the DM toolkit
        KitEditorInfo.Text = string.Empty;
    }

    private void BtnNewKit_Click(object sender, RoutedEventArgs e)
    {
        _selectedKitId = string.Empty;
        KitListEditor.SelectedIndex = -1;
        KitEditName.Text = string.Empty;
        KitEditSource.Text = "Custom";
        SelectKitRulesMode(KitEditRulesMode, IsCoreFilter() ? "core_rules" : "players_option");
        KitEditAllowedRaces.Text = string.Empty;
        KitEditAllowedClasses.Text = string.Empty;
        KitEditDescription.Text = string.Empty;
        BtnDeleteKit.IsEnabled = false;
        KitEditorInfo.Text = "Fill in the fields and click SAVE KIT.";
    }

    private void BtnSaveKit_Click(object sender, RoutedEventArgs e)
    {
        string name = KitEditName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Kit name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string id = string.IsNullOrWhiteSpace(_selectedKitId)
            ? $"kit_custom_{SlugifySimple(name)}"
            : _selectedKitId;

        var allowedRaces = KitEditAllowedRaces.Text
            .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .ToList();
        var allowedClasses = KitEditAllowedClasses.Text
            .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .ToList();

        // Preserve existing NWP grants when saving from the DM editor
        var existingKit = _app.Rules.Kits.FirstOrDefault(k => k.Id == id);
        var updated = new KitDefinition(
            id,
            name,
            KitEditDescription.Text.Trim(),
            KitEditSource.Text.Trim(),
            allowedRaces,
            allowedClasses,
            existingKit?.FreeNwpIds ?? new List<string>(),
            existingKit?.RequiredNwpIds ?? new List<string>(),
            SelectedKitRulesMode(KitEditRulesMode),
            existingKit?.StartingFundsRollOverride ?? string.Empty,
            existingKit?.StartingFundsGoldBonus ?? 0,
            existingKit?.StartingFundsMultiplierPercent ?? 100);

        var allKits = _app.Rules.Kits.Where(k => k.Id != id).Append(updated);
        _app.Rules.SaveKitDefinitions(allKits);
        _selectedKitId = id;
        KitEditorInfo.Text = $"★ Saved: {name}";
        RefreshKitEditorList();
    }

    private void BtnDeleteKit_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedKitId)) return;
        string displayName = _kitItems.FirstOrDefault(k => k.Id == _selectedKitId)?.Name ?? _selectedKitId;
        if (MessageBox.Show($"Delete kit \"{displayName}\"?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        var remaining = _app.Rules.Kits.Where(k => k.Id != _selectedKitId);
        _app.Rules.SaveKitDefinitions(remaining);
        _selectedKitId = string.Empty;
        KitEditName.Text = string.Empty;
        KitEditSource.Text = string.Empty;
        SelectKitRulesMode(KitEditRulesMode, "all");
        KitEditAllowedRaces.Text = string.Empty;
        KitEditAllowedClasses.Text = string.Empty;
        KitEditDescription.Text = string.Empty;
        BtnDeleteKit.IsEnabled = false;
        KitEditorInfo.Text = string.Empty;
        RefreshKitEditorList();
    }

    private void BtnMigrateKitRulesModes_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                "This will write explicit rules_mode values into kits.json for kits that do not have one yet. Continue?",
                "Migrate Kit Rules Modes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var (updated, total) = _app.Rules.MigrateKitRulesModesInFile();
            RefreshKitEditorList();
            KitEditorInfo.Text = $"Migration complete: {updated} updated out of {total} kits.";
            MessageBox.Show(
                $"Kit rules-mode migration complete.\n\nUpdated: {updated}\nTotal kits scanned: {total}",
                "Migration Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Migration failed: {ex.Message}", "Migration Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Classes tab ──────────────────────────────────────────────────────────

    // ── Multi-Class Combos tab ────────────────────────────────────────────────

    private string _selectedMultiClassRaceGroup = "";
    // Working copy of combos for the currently-edited group (mutable list)
    private List<List<string>> _editingCombos = new();

    private void RefreshMultiClassEditor()
    {
        MultiClassRaceList.SelectionChanged -= MultiClassRaceList_SelectionChanged;
        MultiClassRaceList.ItemsSource = _app.Rules.MultiClassCombos
            .Select(g => $"{g.RaceGroup}  ({g.Combos.Count} combos)")
            .ToList();
        MultiClassRaceList.SelectionChanged += MultiClassRaceList_SelectionChanged;

        if (!string.IsNullOrEmpty(_selectedMultiClassRaceGroup))
        {
            int idx = _app.Rules.MultiClassCombos
                .FindIndex(g => string.Equals(g.RaceGroup, _selectedMultiClassRaceGroup, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                MultiClassRaceList.SelectedIndex = idx;
                return;
            }
        }

        if (_app.Rules.MultiClassCombos.Count > 0)
            MultiClassRaceList.SelectedIndex = 0;
        else
            ClearMultiClassEditor();
    }

    private void MultiClassRaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = MultiClassRaceList.SelectedIndex;
        if (idx < 0 || idx >= _app.Rules.MultiClassCombos.Count) { ClearMultiClassEditor(); return; }
        var g = _app.Rules.MultiClassCombos[idx];
        _selectedMultiClassRaceGroup = g.RaceGroup;
        MultiClassEditRaceGroup.Text = g.RaceGroup;
        MultiClassEditSource.Text    = g.Source;
        _editingCombos = g.Combos.Select(c => new List<string>(c)).ToList();
        RefreshMultiClassComboList();
        BtnDeleteMultiClassGroup.IsEnabled = true;
        MultiClassEditorInfo.Text = "";
    }

    private void ClearMultiClassEditor()
    {
        _selectedMultiClassRaceGroup = "";
        _editingCombos = new();
        MultiClassEditRaceGroup.Text = "";
        MultiClassEditSource.Text    = "";
        MultiClassComboList.ItemsSource = null;
        BtnDeleteMultiClassGroup.IsEnabled = false;
        BtnRemoveMultiClassCombo.IsEnabled = false;
        MultiClassEditorInfo.Text = "";
    }

    private void RefreshMultiClassComboList()
    {
        MultiClassComboList.SelectionChanged -= MultiClassComboList_SelectionChanged;
        MultiClassComboList.ItemsSource = _editingCombos
            .Select(c => string.Join(", ", c))
            .ToList();
        MultiClassComboList.SelectionChanged += MultiClassComboList_SelectionChanged;
        BtnRemoveMultiClassCombo.IsEnabled = false;
    }

    private void MultiClassComboList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnRemoveMultiClassCombo.IsEnabled = MultiClassComboList.SelectedIndex >= 0;
    }

    private void BtnNewMultiClassGroup_Click(object sender, RoutedEventArgs e)
    {
        _selectedMultiClassRaceGroup = "";
        _editingCombos = new();
        MultiClassEditRaceGroup.Text = "";
        MultiClassEditSource.Text    = "PHB 2e";
        MultiClassComboList.ItemsSource = null;
        BtnDeleteMultiClassGroup.IsEnabled = false;
        BtnRemoveMultiClassCombo.IsEnabled = false;
        MultiClassEditorInfo.Text = "Enter a race group key and add combos, then click SAVE GROUP.";
        MultiClassRaceList.SelectedIndex = -1;
    }

    private void BtnAddMultiClassCombo_Click(object sender, RoutedEventArgs e)
    {
        string raw = MultiClassNewComboEntry.Text.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return;

        var classes = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Select(s => s.ToLowerInvariant())
                         .Distinct()
                         .ToList();
        if (classes.Count < 2) { MultiClassEditorInfo.Text = "A combo needs at least 2 class IDs."; return; }

        // Prevent duplicate combos (order-insensitive)
        bool dup = _editingCombos.Any(c =>
            c.Count == classes.Count &&
            new HashSet<string>(c, StringComparer.OrdinalIgnoreCase).SetEquals(classes));
        if (dup) { MultiClassEditorInfo.Text = "That combo already exists."; return; }

        _editingCombos.Add(classes);
        MultiClassNewComboEntry.Text = "";
        RefreshMultiClassComboList();
        MultiClassEditorInfo.Text = $"Added: {string.Join("/", classes)}. Remember to SAVE GROUP.";
    }

    private void BtnRemoveMultiClassCombo_Click(object sender, RoutedEventArgs e)
    {
        int idx = MultiClassComboList.SelectedIndex;
        if (idx < 0 || idx >= _editingCombos.Count) return;
        string label = string.Join("/", _editingCombos[idx]);
        _editingCombos.RemoveAt(idx);
        RefreshMultiClassComboList();
        MultiClassEditorInfo.Text = $"Removed: {label}. Remember to SAVE GROUP.";
    }

    private void BtnSaveMultiClassGroup_Click(object sender, RoutedEventArgs e)
    {
        string raceGroup = MultiClassEditRaceGroup.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(raceGroup)) { MultiClassEditorInfo.Text = "Race group key is required."; return; }

        string source = MultiClassEditSource.Text.Trim();
        var updated = new MultiClassComboGroup(raceGroup, source, _editingCombos.Select(c => new List<string>(c)).ToList());

        var all = _app.Rules.MultiClassCombos
            .Where(g => !string.Equals(g.RaceGroup, raceGroup, StringComparison.OrdinalIgnoreCase))
            .Append(updated)
            .OrderBy(g => g.RaceGroup)
            .ToList();

        _app.Rules.SaveMultiClassCombos(all);
        _selectedMultiClassRaceGroup = raceGroup;
        MultiClassEditorInfo.Text = $"★ Saved: {raceGroup}  ({_editingCombos.Count} combos)";
        RefreshMultiClassEditor();
    }

    private void BtnDeleteMultiClassGroup_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedMultiClassRaceGroup)) return;
        if (MessageBox.Show($"Delete all multi-class combos for \"{_selectedMultiClassRaceGroup}\"?",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var remaining = _app.Rules.MultiClassCombos
            .Where(g => !string.Equals(g.RaceGroup, _selectedMultiClassRaceGroup, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _app.Rules.SaveMultiClassCombos(remaining);
        ClearMultiClassEditor();
        RefreshMultiClassEditor();
    }


    private List<ClassDefinition> GetOrderedClasses()
        => _app.Rules.Classes.Values
            .Where(ClassMatchesEditorFilter)
            .OrderBy(c => c.Name)
            .ToList();

    private void RefreshClassEditorList()
    {
        ClassDefListEditor.ItemsSource = GetOrderedClasses()
            .Select(FormatClassEditorLabel)
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedClassId))
        {
            var ordered = GetOrderedClasses();
            int idx = ordered.FindIndex(c => string.Equals(c.Id, _selectedClassId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                ClassDefListEditor.SelectedIndex = idx;
        }

        if (ClassDefListEditor.SelectedIndex < 0 && _app.Rules.Classes.Count > 0)
            ClassDefListEditor.SelectedIndex = 0;
    }

    private void ClassDefListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = ClassDefListEditor.SelectedIndex;
        var ordered = GetOrderedClasses();
        if (idx < 0 || idx >= ordered.Count) return;
        LoadClassDefinitionEditor(ordered[idx]);
    }

    private void LoadClassDefinitionEditor(ClassDefinition cls)
    {
        _selectedClassId = cls.Id;
        ClassDefEditorTitle.Text = $"Class Editor - {cls.Name}";
        ClassDefIdEditor.Text = cls.Id;
        ClassDefNameEditor.Text = cls.Name;
        SelectClassRulesMode(ResolveClassRulesModeForEditor(cls));
        ClassDefBudgetEditor.Text = cls.ClassPointBudget.ToString();
        ClassDefAllowedRacesEditor.Text = string.Join(", ", cls.AllowedRaces);
        ClassDefAllowedAlignmentsEditor.Text = string.Join(", ", cls.AllowedAlignments ?? new List<string>());
        ClassDefMinsEditor.Text = JsonSerializer.Serialize(cls.AbilityMinimums);
        BtnDeleteClassDefinition.IsEnabled = true;
    }

    private void BtnNewClassDefinition_Click(object sender, RoutedEventArgs e)
    {
        _selectedClassId = string.Empty;
        ClassDefListEditor.SelectedIndex = -1;
        ClassDefEditorTitle.Text = "Class Editor - New Class";
        ClassDefIdEditor.Text = string.Empty;
        ClassDefNameEditor.Text = string.Empty;
        SelectClassRulesMode(IsCoreFilter() ? "core_rules" : "players_option");
        ClassDefBudgetEditor.Text = "0";
        ClassDefAllowedRacesEditor.Text = string.Empty;
        ClassDefAllowedAlignmentsEditor.Text = string.Empty;
        ClassDefMinsEditor.Text = "{}";
        BtnDeleteClassDefinition.IsEnabled = false;
        ClassDefEditorInfo.Text = "Fill in the fields and click SAVE CLASS.";
    }

    private void BtnSaveClassDefinition_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string id = ClassDefIdEditor.Text.Trim().ToLowerInvariant();
            string name = ClassDefNameEditor.Text.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Class Id and Name are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existingClass = _app.Rules.Classes.TryGetValue(id, out var existing)
                ? existing
                : null;
            var mins = string.IsNullOrWhiteSpace(ClassDefMinsEditor.Text)
                ? new Dictionary<string, int>()
                : JsonSerializer.Deserialize<Dictionary<string, int>>(ClassDefMinsEditor.Text.Trim())
                    ?? new Dictionary<string, int>();
            var allowed = ClassDefAllowedRacesEditor.Text
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
            string[] validAlignments = { "LG", "NG", "CG", "LN", "N", "CN", "LE", "NE", "CE" };
            var allowedAlignments = ClassDefAllowedAlignmentsEditor.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => value.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (allowedAlignments.Any(value => !validAlignments.Contains(value, StringComparer.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Allowed Alignments contains an invalid alignment code.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            int budget = ParseIntOrDefault(ClassDefBudgetEditor.Text, 0);

            _app.Rules.SaveClass(new ClassDefinition(
                id,
                name,
                mins,
                allowed,
                existingClass?.StructuredAbilities ?? new List<AbilityDefinition>(),
                budget,
                existingClass?.Specializations,
                existingClass?.Source ?? "custom",
                SelectedClassRulesMode(),
                existingClass?.StartingFundsRoll ?? "",
                existingClass?.FixedSpheres,
                existingClass?.DeityId ?? "",
                allowedAlignments));
            _selectedClassId = id;
            ClassDefEditorInfo.Text = $"Saved class: {name}";
            RefreshClassEditorList();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Save Class", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnDeleteClassDefinition_Click(object sender, RoutedEventArgs e)
    {
        string id = ClassDefIdEditor.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(id)) return;
        if (MessageBox.Show("Delete this class from ruleset files?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        _app.Rules.DeleteClass(id);
        _selectedClassId = string.Empty;
        BtnNewClassDefinition_Click(sender, e);
        RefreshClassEditorList();
    }

    // ── Class Abilities tab ──────────────────────────────────────────────────

    private void RefreshClassAbilityEditorList()
    {
        ClassListEditor.ItemsSource = GetOrderedClasses()
            .Select(FormatClassEditorLabel)
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedClassId))
        {
            var ordered = GetOrderedClasses();
            int idx = ordered.FindIndex(c => string.Equals(c.Id, _selectedClassId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) ClassListEditor.SelectedIndex = idx;
        }
        if (ClassListEditor.SelectedIndex < 0 && _app.Rules.Classes.Count > 0)
            ClassListEditor.SelectedIndex = 0;

        RefreshClassCopyTargets();
    }

    private void ClassListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = ClassListEditor.SelectedIndex;
        var ordered = GetOrderedClasses();
        if (idx < 0 || idx >= ordered.Count) return;
        LoadClassAbilityEditor(ordered[idx]);
    }

    private void LoadClassAbilityEditor(ClassDefinition cls)
    {
        _selectedClassId = cls.Id;
        ClassEditorTitle.Text = $"Class Ability Editor - {cls.Name}";
        ClassIdEditor.Text = cls.Id;
        ClassNameEditor.Text = cls.Name;
        _workingClassAbilities = cls.StructuredAbilities
            .Select(CloneAbilityDefinition)
            .ToList();
        _selectedClassAbilityIndex = -1;
        RefreshClassAbilityItemsList();
        ClearClassAbilityDetailForm();
        RefreshClassCopyTargets();
    }

    private void BtnNewClassAbilityClass_Click(object sender, RoutedEventArgs e)
    {
        _selectedClassId = string.Empty;
        ClassListEditor.SelectedIndex = -1;
        ClassEditorTitle.Text = "Class Ability Editor - New Class";
        ClassIdEditor.Text = string.Empty;
        ClassNameEditor.Text = string.Empty;
        _workingClassAbilities = new List<AbilityDefinition>();
        _selectedClassAbilityIndex = -1;
        RefreshClassAbilityItemsList();
        ClearClassAbilityDetailForm();
        RefreshClassCopyTargets();
        ClassEditorInfo.Text = "Enter a class id, name, and abilities, then click SAVE CLASS ABILITIES.";
    }

    private void BtnSaveClass_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string id = ClassIdEditor.Text.Trim().ToLowerInvariant();
            string name = ClassNameEditor.Text.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Class Id and Name are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existingClass = _app.Rules.Classes.TryGetValue(id, out var existing)
                ? existing
                : null;
            _app.Rules.SaveClass(new ClassDefinition(
                id,
                name,
                existingClass?.AbilityMinimums ?? new Dictionary<string, int>(),
                existingClass?.AllowedRaces ?? new List<string>(),
                _workingClassAbilities,
                existingClass?.ClassPointBudget ?? 0,
                existingClass?.Specializations,
                existingClass?.Source ?? "custom",
                existingClass?.RulesMode ?? (IsCoreFilter() ? "core_rules" : "players_option"),
                existingClass?.StartingFundsRoll ?? "",
                existingClass?.FixedSpheres,
                existingClass?.DeityId ?? "",
                existingClass?.AllowedAlignments));

            int updatedCharacters = ReapplyAbilityMechanicsToMatchingCharacters(classId: id, raceId: null);
            _selectedClassId = id;
            ClassEditorInfo.Text = $"Saved class abilities: {name}. Reapplied mechanics to {updatedCharacters} character(s).";
            RefreshClassAbilityEditorList();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Save Class", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnDeleteClass_Click(object sender, RoutedEventArgs e)
    {
        BtnDeleteClassDefinition_Click(sender, e);
        RefreshClassAbilityEditorList();
    }

    private void RefreshClassCopyTargets()
    {
        _classCopyTargets = GetOrderedClasses()
            .Where(c => !string.Equals(c.Id, _selectedClassId, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        ClassCopyTarget.ItemsSource = _classCopyTargets
            .Select(c => new ClassCopyTargetOption(c.Id, c.Name))
            .ToList();

        if (ClassCopyTarget.Items.Count > 0)
            ClassCopyTarget.SelectedIndex = 0;
    }

    private void BtnCopyClassAbilityToClass_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClassAbilityIndex < 0 || _selectedClassAbilityIndex >= _workingClassAbilities.Count)
        {
            ClassEditorInfo.Text = "Select an ability to copy first.";
            return;
        }

        if (ClassCopyTarget.SelectedItem is not ClassCopyTargetOption targetOption)
        {
            ClassEditorInfo.Text = "Select a target class first.";
            return;
        }

        if (!_app.Rules.Classes.TryGetValue(targetOption.Id, out var targetClass))
        {
            ClassEditorInfo.Text = "Could not find the target class in rules.";
            return;
        }

        if (_selectedClassAbilityIndex == CaAbilityList.SelectedIndex)
        {
            _workingClassAbilities[_selectedClassAbilityIndex] = ReadClassAbilityDetailForm();
            RefreshClassAbilityItemsList();
        }

        var sourceAbility = _workingClassAbilities[_selectedClassAbilityIndex];
        var clonedAbility = CloneAbilityDefinition(sourceAbility);
        var result = CopyClassAbilityToTargets(clonedAbility, new[] { targetClass });
        ClassEditorInfo.Text = result.updated > 0
            ? $"Updated '{clonedAbility.Description}' on {targetClass.Name}. Reapplied mechanics to {result.charactersUpdated} character(s)."
            : $"Copied '{clonedAbility.Description}' to {targetClass.Name}. Reapplied mechanics to {result.charactersUpdated} character(s).";
    }

    private void BtnCopyClassAbilityToAllClasses_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClassAbilityIndex < 0 || _selectedClassAbilityIndex >= _workingClassAbilities.Count)
        {
            ClassEditorInfo.Text = "Select an ability to copy first.";
            return;
        }

        if (_classCopyTargets.Count == 0)
        {
            ClassEditorInfo.Text = "No other classes available in the current filter.";
            return;
        }

        if (_selectedClassAbilityIndex == CaAbilityList.SelectedIndex)
        {
            _workingClassAbilities[_selectedClassAbilityIndex] = ReadClassAbilityDetailForm();
            RefreshClassAbilityItemsList();
        }

        var sourceAbility = _workingClassAbilities[_selectedClassAbilityIndex];
        var clonedAbility = CloneAbilityDefinition(sourceAbility);
        var result = CopyClassAbilityToTargets(clonedAbility, _classCopyTargets);

        ClassEditorInfo.Text = $"Applied '{clonedAbility.Description}' to {_classCopyTargets.Count} class(es): {result.added} added, {result.updated} updated. Reapplied mechanics to {result.charactersUpdated} character(s).";
    }

    private (int added, int updated, int charactersUpdated) CopyClassAbilityToTargets(AbilityDefinition ability, IEnumerable<ClassDefinition> targets)
    {
        int added = 0;
        int updated = 0;
        int charactersUpdated = 0;

        foreach (var targetClass in targets)
        {
            var targetAbilities = targetClass.StructuredAbilities
                .Select(CloneAbilityDefinition)
                .ToList();

            int existingIdx = targetAbilities.FindIndex(a =>
                string.Equals(a.Id, ability.Id, System.StringComparison.OrdinalIgnoreCase));

            if (existingIdx >= 0)
            {
                targetAbilities[existingIdx] = CloneAbilityDefinition(ability);
                updated += 1;
            }
            else
            {
                targetAbilities.Add(CloneAbilityDefinition(ability));
                added += 1;
            }

            _app.Rules.SaveClass(new ClassDefinition(
                targetClass.Id,
                targetClass.Name,
                targetClass.AbilityMinimums,
                targetClass.AllowedRaces,
                targetAbilities,
                targetClass.ClassPointBudget,
                targetClass.Specializations,
                targetClass.Source,
                targetClass.RulesMode,
                targetClass.StartingFundsRoll,
                targetClass.FixedSpheres,
                targetClass.DeityId,
                targetClass.AllowedAlignments));

            charactersUpdated += ReapplyAbilityMechanicsToMatchingCharacters(classId: targetClass.Id, raceId: null);
        }

        return (added, updated, charactersUpdated);
    }

    // ── Racial Abilities tab ─────────────────────────────────────────────────

    private void RefreshRacialAbilityEditorList()
    {
        var ordered = GetOrderedRacesForEditor();
        RaceAbilityListEditor.ItemsSource = ordered
            .Select(r => $"{r.Name} [{r.Id}] [{ModeLabel(r.CharacterMode)}] [{SourceLabel(r.Source)}]")
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedRaceAbilityId))
        {
            int idx = ordered.FindIndex(r => string.Equals(r.Id, _selectedRaceAbilityId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) RaceAbilityListEditor.SelectedIndex = idx;
        }
        if (RaceAbilityListEditor.SelectedIndex < 0 && ordered.Count > 0)
            RaceAbilityListEditor.SelectedIndex = 0;

        RefreshRaceCopyTargets();
    }

    private void RaceAbilityListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = RaceAbilityListEditor.SelectedIndex;
        var ordered = GetOrderedRacesForEditor();
        if (idx < 0 || idx >= ordered.Count) return;
        var race = ordered[idx];
        _selectedRaceAbilityId = race.Id;
        RacialAbilityEditorTitle.Text = $"Racial Ability Editor - {race.Name}";
        RaceAbilityRaceId.Text = race.Id;
        RaceAbilityRaceName.Text = race.Name;
        _workingRacialAbilities = race.StructuredAbilities
            .Select(CloneAbilityDefinition)
            .ToList();
        _selectedRacialAbilityIndex = -1;
        RefreshRacialAbilityItemsList();
        ClearRacialAbilityDetailForm();
        RefreshRaceCopyTargets();
    }

    private void BtnNewRacialAbilityRace_Click(object sender, RoutedEventArgs e)
    {
        _selectedRaceAbilityId = string.Empty;
        RaceAbilityListEditor.SelectedIndex = -1;
        RacialAbilityEditorTitle.Text = "Racial Ability Editor - New Race";
        RaceAbilityRaceId.Text = string.Empty;
        RaceAbilityRaceName.Text = string.Empty;
        _workingRacialAbilities = new List<AbilityDefinition>();
        _selectedRacialAbilityIndex = -1;
        RefreshRacialAbilityItemsList();
        ClearRacialAbilityDetailForm();
        RefreshRaceCopyTargets();
        RacialAbilityEditorInfo.Text = "Enter a race id, name, and abilities, then click SAVE RACIAL ABILITIES.";
    }

    private void BtnSaveRacialAbilities_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string raceId = RaceAbilityRaceId.Text.Trim().ToLowerInvariant();
            string raceName = RaceAbilityRaceName.Text.Trim();
            if (string.IsNullOrWhiteSpace(raceId) || string.IsNullOrWhiteSpace(raceName))
            {
                MessageBox.Show("Race Id and Race Name are required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _app.Rules.Races.TryGetValue(raceId, out var race);
            var defs = _workingRacialAbilities;
            var updated = race is not null
                ? race with
                {
                    Name = raceName,
                    StructuredAbilities = defs,
                    RacialAbilities = defs.Select(d => d.Description).ToList(),
                }
                : new RaceDefinition(
                    raceId,
                    raceName,
                    "all",
                    raceId,
                    new Dictionary<string, int>(),
                    new Dictionary<string, int>(),
                    new Dictionary<string, int>(),
                    defs.Select(d => d.Description).ToList(),
                    defs,
                    0,
                    race?.Source ?? "custom");

            _app.Rules.SaveRace(updated);
            int updatedCharacters = ReapplyAbilityMechanicsToMatchingCharacters(classId: null, raceId: updated.Id);
            _selectedRaceAbilityId = updated.Id;
            RacialAbilityEditorInfo.Text = $"Saved racial abilities for {updated.Name}. Reapplied mechanics to {updatedCharacters} character(s).";
            RefreshRacialAbilityEditorList();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Save Racial Abilities", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshRaceCopyTargets()
    {
        _raceCopyTargets = _app.Rules.Races.Values
            .OrderBy(r => r.Name)
            .Where(r => !string.Equals(r.Id, _selectedRaceAbilityId, System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        var options = _raceCopyTargets
            .Select(r => new RaceCopyTargetOption(r.Id, r.Name))
            .ToList();

        RaceCopyTarget.ItemsSource = options;
        RaceCopyTargetList.ItemsSource = options
            .Select(x => new RaceCopyTargetOption(x.Id, x.Name))
            .ToList();

        if (RaceCopyTarget.Items.Count > 0)
            RaceCopyTarget.SelectedIndex = 0;

        if (string.IsNullOrWhiteSpace(RaceCopyFamilyKey.Text)
            && _app.Rules.Races.TryGetValue(_selectedRaceAbilityId, out var sourceRace))
        {
            var seed = string.IsNullOrWhiteSpace(sourceRace.BaseRaceId)
                ? sourceRace.Name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? sourceRace.Id
                : sourceRace.BaseRaceId;
            RaceCopyFamilyKey.Text = seed;
        }
    }

    private void BtnCopyRacialAbilityToRace_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetRacialAbilityForCopy(out var sourceAbility)) return;

        if (RaceCopyTarget.SelectedItem is not RaceCopyTargetOption targetOption)
        {
            RacialAbilityEditorInfo.Text = "Select a target race first.";
            return;
        }

        if (!_app.Rules.Races.TryGetValue(targetOption.Id, out var targetRace))
        {
            RacialAbilityEditorInfo.Text = "Could not find the target race in rules.";
            return;
        }

        SaveAbilityToRace(targetRace, sourceAbility, out bool replaced);

        RacialAbilityEditorInfo.Text = replaced
            ? $"Updated '{sourceAbility.Description}' on {targetRace.Name}."
            : $"Copied '{sourceAbility.Description}' to {targetRace.Name}.";
    }

    private void BtnCopyRacialAbilityToSelectedRaces_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetRacialAbilityForCopy(out var sourceAbility)) return;

        var selectedTargets = RaceCopyTargetList.SelectedItems
            .OfType<RaceCopyTargetOption>()
            .Select(x => x.Id)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selectedTargets.Count == 0)
        {
            RacialAbilityEditorInfo.Text = "Select one or more target races first.";
            return;
        }

        int updated = 0;
        int added = 0;
        foreach (var targetId in selectedTargets)
        {
            if (!_app.Rules.Races.TryGetValue(targetId, out var targetRace))
                continue;

            SaveAbilityToRace(targetRace, sourceAbility, out bool replaced);
            if (replaced) updated += 1;
            else added += 1;
        }

        RacialAbilityEditorInfo.Text = $"Copied '{sourceAbility.Description}' to {updated + added} race(s): {added} added, {updated} updated.";
        RefreshRaceCopyTargets();
    }

    private void BtnCopyRacialAbilityToFamily_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetRacialAbilityForCopy(out var sourceAbility)) return;

        string token = RaceCopyFamilyKey.Text.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            RacialAbilityEditorInfo.Text = "Enter a family key (for example: kobold).";
            return;
        }

        var matches = _app.Rules.Races.Values
            .Where(r => !string.Equals(r.Id, _selectedRaceAbilityId, System.StringComparison.OrdinalIgnoreCase)
                && (r.Id.Contains(token, System.StringComparison.OrdinalIgnoreCase)
                    || r.Name.Contains(token, System.StringComparison.OrdinalIgnoreCase)
                    || r.BaseRaceId.Contains(token, System.StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => r.Name)
            .ToList();

        if (matches.Count == 0)
        {
            RacialAbilityEditorInfo.Text = $"No races matched '{token}'.";
            return;
        }

        int updated = 0;
        int added = 0;
        foreach (var targetRace in matches)
        {
            SaveAbilityToRace(targetRace, sourceAbility, out bool replaced);
            if (replaced) updated += 1;
            else added += 1;
        }

        RacialAbilityEditorInfo.Text = $"Copied '{sourceAbility.Description}' to {matches.Count} matching race(s): {added} added, {updated} updated.";
        RefreshRaceCopyTargets();
    }

    private bool TryGetRacialAbilityForCopy(out AbilityDefinition sourceAbility)
    {
        sourceAbility = new AbilityDefinition();
        if (_selectedRacialAbilityIndex < 0 || _selectedRacialAbilityIndex >= _workingRacialAbilities.Count)
        {
            RacialAbilityEditorInfo.Text = "Select an ability to copy first.";
            return false;
        }

        if (_selectedRacialAbilityIndex == RaAbilityList.SelectedIndex)
        {
            _workingRacialAbilities[_selectedRacialAbilityIndex] = ReadRacialAbilityDetailForm();
            RefreshRacialAbilityItemsList();
        }

        sourceAbility = CloneAbilityDefinition(_workingRacialAbilities[_selectedRacialAbilityIndex]);
        return true;
    }

    private void SaveAbilityToRace(RaceDefinition targetRace, AbilityDefinition sourceAbility, out bool replaced)
    {
        var targetAbilities = targetRace.StructuredAbilities
            .Select(CloneAbilityDefinition)
            .ToList();

        int existingIdx = targetAbilities.FindIndex(a =>
            string.Equals(a.Id, sourceAbility.Id, System.StringComparison.OrdinalIgnoreCase));

        replaced = existingIdx >= 0;
        if (replaced)
            targetAbilities[existingIdx] = CloneAbilityDefinition(sourceAbility);
        else
            targetAbilities.Add(CloneAbilityDefinition(sourceAbility));

        var updatedTargetRace = targetRace with
        {
            StructuredAbilities = targetAbilities,
            RacialAbilities = targetAbilities.Select(a => a.Description).ToList(),
        };

        _app.Rules.SaveRace(updatedTargetRace);
        ReapplyAbilityMechanicsToMatchingCharacters(classId: null, raceId: updatedTargetRace.Id);
    }

    private int ReapplyAbilityMechanicsToMatchingCharacters(string? classId, string? raceId)
    {
        bool hasClass = !string.IsNullOrWhiteSpace(classId);
        bool hasRace = !string.IsNullOrWhiteSpace(raceId);
        if (!hasClass && !hasRace)
            return 0;

        int updated = 0;
        var library = new EquipmentLibraryService().GetEquipmentLibrary();

        foreach (var c in _app.Characters)
        {
            bool classMatch = hasClass && CharacterUsesClass(c, classId!);
            bool raceMatch = hasRace && string.Equals(c.RaceId, raceId, System.StringComparison.OrdinalIgnoreCase);
            if (!classMatch && !raceMatch)
                continue;

            RebuildCharacterAbilityStateFromRules(c, library);
            updated += 1;
        }

        if (updated > 0)
            _app.SaveCharacters();

        return updated;
    }

    private void RebuildCharacterAbilityStateFromRules(CharacterSheet character, IReadOnlyList<CustomEquipmentData> equipmentLibrary)
    {
        var rebuilt = _app.Rules.BuildCharacter(
            character.Name,
            character.RaceId,
            character.ClassId,
            character.Abilities,
            character.SelectedRacialAbilityIds,
            character.SelectedClassAbilityIds,
            character.ClassAbilityCarryoverPoints,
            character.WizardSpecializationId,
            character.SubAbilities,
            character.ExceptionalStrength,
            character.RogueSkillArmorProfile,
            Math.Max(1, character.Level),
            character.CharacterMode);

        character.StructuredAbilities = rebuilt.StructuredAbilities
            .Select(CloneAbilityDefinition)
            .ToList();
        character.RacialAbilities = rebuilt.RacialAbilities.ToList();
        character.Bonuses = rebuilt.Bonuses;
        character.BaseMovement = rebuilt.BaseMovement;
        character.Movement = rebuilt.Movement;
        character.RacialPointBudget = rebuilt.RacialPointBudget;
        character.RacialPointSpent = rebuilt.RacialPointSpent;
        character.RacialPointRemaining = rebuilt.RacialPointRemaining;
        character.ClassPointBudget = rebuilt.ClassPointBudget;
        character.ClassPointSpent = rebuilt.ClassPointSpent;
        character.ClassPointRemaining = rebuilt.ClassPointRemaining;
        character.DerivedStats = new Dictionary<string, int>(rebuilt.DerivedStats, System.StringComparer.OrdinalIgnoreCase);
        character.SubAbilityEffects = new Dictionary<string, string>(rebuilt.SubAbilityEffects, System.StringComparer.OrdinalIgnoreCase);

        CharacterProgressionService.InitializeCharacterProgression(character, seedLevelRewards: false);
        CharacterArmorService.RecalculateArmorForCharacter(character, equipmentLibrary);
        character.LastModified = System.DateTime.Now;
        character.Revision += 1;
    }

    private static bool CharacterUsesClass(CharacterSheet character, string classId)
    {
        if (string.Equals(character.ClassId, classId, System.StringComparison.OrdinalIgnoreCase))
            return true;

        if (character.ClassIds.Any(c => string.Equals(c, classId, System.StringComparison.OrdinalIgnoreCase)))
            return true;

        var parts = character.ClassId
            .Split(new[] { '/', '+', ',', ';', '|' }, System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        return parts.Any(part => string.Equals(part, classId, System.StringComparison.OrdinalIgnoreCase));
    }

    // ── Class Ability item list handlers ────────────────────────────────────

    private void RefreshClassAbilityItemsList()
    {
        _visibleClassAbilityIndices.Clear();
        _visibleClassAbilityIndices.AddRange(
            Enumerable.Range(0, _workingClassAbilities.Count)
                .OrderBy(i => _workingClassAbilities[i].Description ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => _workingClassAbilities[i].Id ?? string.Empty, StringComparer.OrdinalIgnoreCase));

        CaAbilityList.ItemsSource = null;
        CaAbilityList.ItemsSource = _visibleClassAbilityIndices
            .Select(i => _workingClassAbilities[i])
            .Select(a =>
            {
                int unlockLevel = DungeonMasterCortex.Services.RulesEngine.AbilityUnlockLevel(a);
                string levelBadge = unlockLevel > 1 ? $", unlock L{unlockLevel}" : string.Empty;
                string multiBadge = a.AllowMultiple ? ", repeatable" : string.Empty;
                string textBadge = a.RequiresPlayerText ? ", text" : string.Empty;
                string postLevelBadge = a.AllowPurchaseAfterLevelOne ? string.Empty : ", creation-only";
                return $"{a.Description}  [{(a.AutoGranted ? "auto" : "optional")}, {a.PointCost} pts{levelBadge}{multiBadge}{textBadge}{postLevelBadge}]";
            })
            .ToList();

        if (_selectedClassAbilityIndex >= 0 && _selectedClassAbilityIndex < _workingClassAbilities.Count)
        {
            int visibleIdx = _visibleClassAbilityIndices.IndexOf(_selectedClassAbilityIndex);
            CaAbilityList.SelectedIndex = visibleIdx;
            if (visibleIdx < 0)
                ClearClassAbilityDetailForm();
        }
    }

    private void CaAbilityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = CaAbilityList.SelectedIndex;
        if (idx < 0 || idx >= _visibleClassAbilityIndices.Count) return;
        _selectedClassAbilityIndex = _visibleClassAbilityIndices[idx];
        LoadClassAbilityDetailForm(_workingClassAbilities[_selectedClassAbilityIndex]);
    }

    private void BtnAddClassAbility_Click(object sender, RoutedEventArgs e)
    {
        _workingClassAbilities.Add(new AbilityDefinition { Description = "New Ability", AutoGranted = true, Id = "new_ability" });
        _selectedClassAbilityIndex = _workingClassAbilities.Count - 1;
        RefreshClassAbilityItemsList();
        LoadClassAbilityDetailForm(_workingClassAbilities[_selectedClassAbilityIndex]);
    }

    private void BtnRemoveClassAbility_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClassAbilityIndex < 0 || _selectedClassAbilityIndex >= _workingClassAbilities.Count) return;
        _workingClassAbilities.RemoveAt(_selectedClassAbilityIndex);
        _selectedClassAbilityIndex = Math.Min(_selectedClassAbilityIndex, _workingClassAbilities.Count - 1);
        RefreshClassAbilityItemsList();
        if (_selectedClassAbilityIndex >= 0)
            LoadClassAbilityDetailForm(_workingClassAbilities[_selectedClassAbilityIndex]);
        else
            ClearClassAbilityDetailForm();
    }

    private void BtnApplyClassAbility_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedClassAbilityIndex < 0 || _selectedClassAbilityIndex >= _workingClassAbilities.Count)
        {
            CaApplyInfo.Text = "Select an ability from the list first.";
            return;
        }
        _workingClassAbilities[_selectedClassAbilityIndex] = ReadClassAbilityDetailForm();
        RefreshClassAbilityItemsList();
        CaApplyInfo.Text = "Applied. Remember to click SAVE CLASS ABILITIES to persist.";
    }

    private void LoadClassAbilityDetailForm(AbilityDefinition a)
    {
        CaDesc.Text = StripLeadingLevelPrefix(a.Description);
        CaCost.Text = a.PointCost != 0 ? a.PointCost.ToString() : "";
        CaUnlockLevel.Text = DungeonMasterCortex.Services.RulesEngine.AbilityUnlockLevel(a).ToString();
        CaAuto.IsChecked = a.AutoGranted;
        CaAllowMultiple.IsChecked = a.AllowMultiple;
        CaRequiresText.IsChecked = a.RequiresPlayerText;
        CaAllowPostLevelOne.IsChecked = a.AllowPurchaseAfterLevelOne;
        var fx = a.Effect;
        CaAcBonus.Text      = fx.AcBonus != 0 ? fx.AcBonus.ToString() : "";
        CaNoArmorForAc.IsChecked = fx.AcBonusRequiresNoArmor;
        CaAttackBonus.Text  = fx.AttackBonus != 0 ? fx.AttackBonus.ToString() : "";
        CaAttackPerLevels.Text = fx.AttackBonusPerLevels > 0 ? fx.AttackBonusPerLevels.ToString() : "";
        CaDamageBonus.Text  = fx.DamageBonus != 0 ? fx.DamageBonus.ToString() : "";
        CaMovementBonus.Text = fx.MovementBonus != 0 ? fx.MovementBonus.ToString() : "";
        CaHpDice.Text       = fx.HpDiceExpression ?? "";
        CaHpPerLevel.Text   = fx.HpPerLevel != 0 ? fx.HpPerLevel.ToString() : "";
        CaHpFlat.Text       = fx.HpFlatBonus != 0 ? fx.HpFlatBonus.ToString() : "";
        CaXpMod.Text        = fx.XpModifierPercent != 0 ? fx.XpModifierPercent.ToString() : "";
        CaNwpSlots.Text     = fx.NwpSlotBonus != 0 ? fx.NwpSlotBonus.ToString() : "";
        CaNwpCostRed.Text   = fx.NwpCostReduction != 0 ? fx.NwpCostReduction.ToString() : "";
        CaNwpCheck.Text     = fx.NwpCheckBonus != 0 ? fx.NwpCheckBonus.ToString() : "";
        CaSurprise.Text     = fx.SurpriseBonus != 0 ? fx.SurpriseBonus.ToString() : "";
        CaReaction.Text     = fx.ReactionBonus != 0 ? fx.ReactionBonus.ToString() : "";
        CaInfravision.Text  = fx.InfravisionFeet != 0 ? fx.InfravisionFeet.ToString() : "";
        CaMagicResist.Text  = fx.MagicResistPercent != 0 ? fx.MagicResistPercent.ToString() : "";
        CaStealth.IsChecked     = fx.GrantsStealth;
        CaDetectDoors.IsChecked = fx.DetectSecretDoors;
        CaDetectStone.IsChecked = fx.DetectStonework;
        CaSaveBonuses.Text   = FormatKeyValuePairs(fx.SaveBonuses);
        CaSubAbility.Text    = FormatKeyValuePairs(fx.SubAbilityBonuses);
        CaEnemyAttack.Text   = FormatKeyValuePairs(fx.EnemyAttackBonuses);
        CaEnemyDamage.Text   = FormatKeyValuePairs(fx.EnemyDamageBonuses);
        CaWeaponAttack.Text  = FormatKeyValuePairs(fx.WeaponAttackBonuses);
        CaWeaponDamage.Text  = FormatKeyValuePairs(fx.WeaponDamageBonuses);
        CaApplyInfo.Text = "";
    }

    private void ClearClassAbilityDetailForm()
    {
        CaDesc.Text = ""; CaCost.Text = ""; CaUnlockLevel.Text = "1"; CaAuto.IsChecked = true;
        CaAllowMultiple.IsChecked = false;
        CaRequiresText.IsChecked = false;
        CaAllowPostLevelOne.IsChecked = false;
        CaAcBonus.Text = ""; CaAttackBonus.Text = ""; CaAttackPerLevels.Text = ""; CaDamageBonus.Text = "";
        CaNoArmorForAc.IsChecked = false;
        CaMovementBonus.Text = "";
        CaHpDice.Text = "";
        CaHpPerLevel.Text = ""; CaHpFlat.Text = ""; CaXpMod.Text = "";
        CaNwpSlots.Text = ""; CaNwpCostRed.Text = ""; CaNwpCheck.Text = "";
        CaSurprise.Text = ""; CaReaction.Text = ""; CaInfravision.Text = ""; CaMagicResist.Text = "";
        CaStealth.IsChecked = false; CaDetectDoors.IsChecked = false; CaDetectStone.IsChecked = false;
        CaSaveBonuses.Text = ""; CaSubAbility.Text = "";
        CaEnemyAttack.Text = ""; CaEnemyDamage.Text = "";
        CaWeaponAttack.Text = ""; CaWeaponDamage.Text = "";
        CaApplyInfo.Text = "";
    }

    private AbilityDefinition ReadClassAbilityDetailForm()
    {
        var baseDesc = StripLeadingLevelPrefix(CaDesc.Text);
        int unlockLevel = ParseIntOrDefault(CaUnlockLevel.Text, 1);
        if (unlockLevel < 1) unlockLevel = 1;
        var desc = unlockLevel > 1 ? $"Level {unlockLevel}: {baseDesc}" : baseDesc;
        var id = unlockLevel > 1
            ? $"{SlugifySimple(baseDesc)}_lvl{unlockLevel}"
            : SlugifySimple(baseDesc);

        return new AbilityDefinition
        {
            Id          = id,
            Description = desc,
            PointCost   = ParseIntOrDefault(CaCost.Text, 0),
            AutoGranted = CaAuto.IsChecked == true,
            AllowMultiple = CaAllowMultiple.IsChecked == true,
            RequiresPlayerText = CaRequiresText.IsChecked == true,
            AllowPurchaseAfterLevelOne = CaAllowPostLevelOne.IsChecked == true,
            Effect = new AbilityEffect
            {
                AcBonus            = ParseIntOrDefault(CaAcBonus.Text, 0),
                AcBonusRequiresNoArmor = CaNoArmorForAc.IsChecked == true,
                AttackBonus        = ParseIntOrDefault(CaAttackBonus.Text, 0),
                AttackBonusPerLevels = ParseIntOrDefault(CaAttackPerLevels.Text, 0),
                DamageBonus        = ParseIntOrDefault(CaDamageBonus.Text, 0),
                MovementBonus      = ParseIntOrDefault(CaMovementBonus.Text, 0),
                HpDiceExpression   = string.IsNullOrWhiteSpace(CaHpDice.Text) ? null : CaHpDice.Text.Trim(),
                HpPerLevel         = ParseIntOrDefault(CaHpPerLevel.Text, 0),
                HpFlatBonus        = ParseIntOrDefault(CaHpFlat.Text, 0),
                XpModifierPercent  = ParseIntOrDefault(CaXpMod.Text, 0),
                NwpSlotBonus       = ParseIntOrDefault(CaNwpSlots.Text, 0),
                NwpCostReduction   = ParseIntOrDefault(CaNwpCostRed.Text, 0),
                NwpCheckBonus      = ParseIntOrDefault(CaNwpCheck.Text, 0),
                SurpriseBonus      = ParseIntOrDefault(CaSurprise.Text, 0),
                ReactionBonus      = ParseIntOrDefault(CaReaction.Text, 0),
                InfravisionFeet    = ParseIntOrDefault(CaInfravision.Text, 0),
                MagicResistPercent = ParseIntOrDefault(CaMagicResist.Text, 0),
                GrantsStealth      = CaStealth.IsChecked == true,
                DetectSecretDoors  = CaDetectDoors.IsChecked == true,
                DetectStonework    = CaDetectStone.IsChecked == true,
                SaveBonuses        = ParseKeyValuePairs(CaSaveBonuses.Text),
                SubAbilityBonuses  = ParseKeyValuePairs(CaSubAbility.Text),
                EnemyAttackBonuses = ParseKeyValuePairs(CaEnemyAttack.Text),
                EnemyDamageBonuses = ParseKeyValuePairs(CaEnemyDamage.Text),
                WeaponAttackBonuses = ParseKeyValuePairs(CaWeaponAttack.Text),
                WeaponDamageBonuses = ParseKeyValuePairs(CaWeaponDamage.Text),
            },
        };
    }

    private static AbilityDefinition CloneAbilityDefinition(AbilityDefinition source)
    {
        return new AbilityDefinition
        {
            Id = source.Id,
            Description = source.Description,
            Category = source.Category,
            PointCost = source.PointCost,
            AutoGranted = source.AutoGranted,
            AllowMultiple = source.AllowMultiple,
            RequiresPlayerText = source.RequiresPlayerText,
            AllowPurchaseAfterLevelOne = source.AllowPurchaseAfterLevelOne,
            Effect = CloneAbilityEffect(source.Effect),
        };
    }

    private static AbilityEffect CloneAbilityEffect(AbilityEffect source)
    {
        return new AbilityEffect
        {
            AcBonus = source.AcBonus,
            AcBonusRequiresNoArmor = source.AcBonusRequiresNoArmor,
            AttackBonus = source.AttackBonus,
            AttackBonusPerLevels = source.AttackBonusPerLevels,
            DamageBonus = source.DamageBonus,
            MovementBonus = source.MovementBonus,
            SaveBonuses = new Dictionary<string, int>(source.SaveBonuses, System.StringComparer.OrdinalIgnoreCase),
            XpModifierPercent = source.XpModifierPercent,
            HpDiceExpression = source.HpDiceExpression,
            HpPerLevel = source.HpPerLevel,
            HpFlatBonus = source.HpFlatBonus,
            NwpSlotBonus = source.NwpSlotBonus,
            NwpCostReduction = source.NwpCostReduction,
            NwpCheckBonus = source.NwpCheckBonus,
            NwpCheckBonusesById = new Dictionary<string, int>(source.NwpCheckBonusesById, System.StringComparer.OrdinalIgnoreCase),
            SurpriseBonus = source.SurpriseBonus,
            SubAbilityBonuses = new Dictionary<string, int>(source.SubAbilityBonuses, System.StringComparer.OrdinalIgnoreCase),
            InfravisionFeet = source.InfravisionFeet,
            MagicResistPercent = source.MagicResistPercent,
            EnemyAttackBonuses = new Dictionary<string, int>(source.EnemyAttackBonuses, System.StringComparer.OrdinalIgnoreCase),
            EnemyDamageBonuses = new Dictionary<string, int>(source.EnemyDamageBonuses, System.StringComparer.OrdinalIgnoreCase),
            WeaponAttackBonuses = new Dictionary<string, int>(source.WeaponAttackBonuses, System.StringComparer.OrdinalIgnoreCase),
            WeaponDamageBonuses = new Dictionary<string, int>(source.WeaponDamageBonuses, System.StringComparer.OrdinalIgnoreCase),
            ReactionBonus = source.ReactionBonus,
            GrantsStealth = source.GrantsStealth,
            DetectSecretDoors = source.DetectSecretDoors,
            DetectStonework = source.DetectStonework,
        };
    }

    // ── Racial Ability item list handlers ────────────────────────────────────

    private void RefreshRacialAbilityItemsList()
    {
        _visibleRacialAbilityIndices.Clear();
        for (int i = 0; i < _workingRacialAbilities.Count; i++)
        {
            var ability = _workingRacialAbilities[i];
            bool include = IsCoreFilter()
                ? IsCoreAbilityEntry(ability)
                : IsPlayersOptionAbilityEntry(ability);

            if (include)
                _visibleRacialAbilityIndices.Add(i);
        }

        RaAbilityList.ItemsSource = null;
        RaAbilityList.ItemsSource = _visibleRacialAbilityIndices
            .Select(i => _workingRacialAbilities[i])
            .Select(a => $"{a.Description}  [{(a.AutoGranted ? "auto" : "optional")}, {a.PointCost} pts]")
            .ToList();

        if (_selectedRacialAbilityIndex >= 0 && _selectedRacialAbilityIndex < _workingRacialAbilities.Count)
        {
            int visibleIdx = _visibleRacialAbilityIndices.IndexOf(_selectedRacialAbilityIndex);
            RaAbilityList.SelectedIndex = visibleIdx;
            if (visibleIdx < 0)
                ClearRacialAbilityDetailForm();
        }
    }

    private void RaAbilityList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = RaAbilityList.SelectedIndex;
        if (idx < 0 || idx >= _visibleRacialAbilityIndices.Count) return;
        _selectedRacialAbilityIndex = _visibleRacialAbilityIndices[idx];
        LoadRacialAbilityDetailForm(_workingRacialAbilities[_selectedRacialAbilityIndex]);
    }

    private void BtnAddRacialAbility_Click(object sender, RoutedEventArgs e)
    {
        _workingRacialAbilities.Add(new AbilityDefinition { Description = "New Ability", AutoGranted = true, Id = "new_ability" });
        _selectedRacialAbilityIndex = _workingRacialAbilities.Count - 1;
        RefreshRacialAbilityItemsList();
        LoadRacialAbilityDetailForm(_workingRacialAbilities[_selectedRacialAbilityIndex]);
    }

    private void BtnInsertRacialTemplate_Click(object sender, RoutedEventArgs e)
    {
        string templateKey = (RaTemplatePreset.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "blank";
        if (!TryBuildRacialAbilityFromTemplateWizard(templateKey, out var template, out var wizardNote))
            return;

        _workingRacialAbilities.Add(template);
        _selectedRacialAbilityIndex = _workingRacialAbilities.Count - 1;
        RefreshRacialAbilityItemsList();
        LoadRacialAbilityDetailForm(_workingRacialAbilities[_selectedRacialAbilityIndex]);

        string label = (RaTemplatePreset.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "template";
        RaApplyInfo.Text = $"Inserted {label} template. {wizardNote}";
    }

    private void BtnRemoveRacialAbility_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRacialAbilityIndex < 0 || _selectedRacialAbilityIndex >= _workingRacialAbilities.Count) return;
        _workingRacialAbilities.RemoveAt(_selectedRacialAbilityIndex);
        _selectedRacialAbilityIndex = Math.Min(_selectedRacialAbilityIndex, _workingRacialAbilities.Count - 1);
        RefreshRacialAbilityItemsList();
        if (_selectedRacialAbilityIndex >= 0)
            LoadRacialAbilityDetailForm(_workingRacialAbilities[_selectedRacialAbilityIndex]);
        else
            ClearRacialAbilityDetailForm();
    }

    private void BtnApplyRacialAbility_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRacialAbilityIndex < 0 || _selectedRacialAbilityIndex >= _workingRacialAbilities.Count)
        {
            RaApplyInfo.Text = "Select an ability from the list first.";
            return;
        }
        _workingRacialAbilities[_selectedRacialAbilityIndex] = ReadRacialAbilityDetailForm();
        RefreshRacialAbilityItemsList();
        RaApplyInfo.Text = "Applied. Remember to click SAVE RACIAL ABILITIES to persist.";
    }

    private void LoadRacialAbilityDetailForm(AbilityDefinition a)
    {
        RaDesc.Text = a.Description;
        RaCost.Text = a.PointCost != 0 ? a.PointCost.ToString() : "";
        RaAuto.IsChecked = a.AutoGranted;
        var fx = a.Effect;
        RaAcBonus.Text      = fx.AcBonus != 0 ? fx.AcBonus.ToString() : "";
        RaNoArmorForAc.IsChecked = fx.AcBonusRequiresNoArmor;
        RaAttackBonus.Text  = fx.AttackBonus != 0 ? fx.AttackBonus.ToString() : "";
        RaAttackPerLevels.Text = fx.AttackBonusPerLevels > 0 ? fx.AttackBonusPerLevels.ToString() : "";
        RaDamageBonus.Text  = fx.DamageBonus != 0 ? fx.DamageBonus.ToString() : "";
        RaMovementBonus.Text = fx.MovementBonus != 0 ? fx.MovementBonus.ToString() : "";
        RaHpDice.Text       = fx.HpDiceExpression ?? "";
        RaHpPerLevel.Text   = fx.HpPerLevel != 0 ? fx.HpPerLevel.ToString() : "";
        RaHpFlat.Text       = fx.HpFlatBonus != 0 ? fx.HpFlatBonus.ToString() : "";
        RaXpMod.Text        = fx.XpModifierPercent != 0 ? fx.XpModifierPercent.ToString() : "";
        RaNwpSlots.Text     = fx.NwpSlotBonus != 0 ? fx.NwpSlotBonus.ToString() : "";
        RaNwpCostRed.Text   = fx.NwpCostReduction != 0 ? fx.NwpCostReduction.ToString() : "";
        RaNwpCheck.Text     = fx.NwpCheckBonus != 0 ? fx.NwpCheckBonus.ToString() : "";
        RaSurprise.Text     = fx.SurpriseBonus != 0 ? fx.SurpriseBonus.ToString() : "";
        RaReaction.Text     = fx.ReactionBonus != 0 ? fx.ReactionBonus.ToString() : "";
        RaInfravision.Text  = fx.InfravisionFeet != 0 ? fx.InfravisionFeet.ToString() : "";
        RaMagicResist.Text  = fx.MagicResistPercent != 0 ? fx.MagicResistPercent.ToString() : "";
        RaStealth.IsChecked     = fx.GrantsStealth;
        RaDetectDoors.IsChecked = fx.DetectSecretDoors;
        RaDetectStone.IsChecked = fx.DetectStonework;
        RaSaveBonuses.Text   = FormatKeyValuePairs(fx.SaveBonuses);
        RaSubAbility.Text    = FormatKeyValuePairs(fx.SubAbilityBonuses);
        RaEnemyAttack.Text   = FormatKeyValuePairs(fx.EnemyAttackBonuses);
        RaEnemyDamage.Text   = FormatKeyValuePairs(fx.EnemyDamageBonuses);
        RaWeaponAttack.Text  = FormatKeyValuePairs(fx.WeaponAttackBonuses);
        RaWeaponDamage.Text  = FormatKeyValuePairs(fx.WeaponDamageBonuses);
        RaApplyInfo.Text = "";
    }

    private void ClearRacialAbilityDetailForm()
    {
        RaDesc.Text = ""; RaCost.Text = ""; RaAuto.IsChecked = true;
        RaAcBonus.Text = ""; RaAttackBonus.Text = ""; RaAttackPerLevels.Text = ""; RaDamageBonus.Text = "";
        RaNoArmorForAc.IsChecked = false;
        RaMovementBonus.Text = "";
        RaHpDice.Text = "";
        RaHpPerLevel.Text = ""; RaHpFlat.Text = ""; RaXpMod.Text = "";
        RaNwpSlots.Text = ""; RaNwpCostRed.Text = ""; RaNwpCheck.Text = "";
        RaSurprise.Text = ""; RaReaction.Text = ""; RaInfravision.Text = ""; RaMagicResist.Text = "";
        RaStealth.IsChecked = false; RaDetectDoors.IsChecked = false; RaDetectStone.IsChecked = false;
        RaSaveBonuses.Text = ""; RaSubAbility.Text = "";
        RaEnemyAttack.Text = ""; RaEnemyDamage.Text = "";
        RaWeaponAttack.Text = ""; RaWeaponDamage.Text = "";
        RaApplyInfo.Text = "";
    }

    private AbilityDefinition ReadRacialAbilityDetailForm()
    {
        var desc = RaDesc.Text.Trim();
        return new AbilityDefinition
        {
            Id          = SlugifySimple(desc),
            Description = desc,
            PointCost   = ParseIntOrDefault(RaCost.Text, 0),
            AutoGranted = RaAuto.IsChecked == true,
            Effect = new AbilityEffect
            {
                AcBonus            = ParseIntOrDefault(RaAcBonus.Text, 0),
                AcBonusRequiresNoArmor = RaNoArmorForAc.IsChecked == true,
                AttackBonus        = ParseIntOrDefault(RaAttackBonus.Text, 0),
                AttackBonusPerLevels = ParseIntOrDefault(RaAttackPerLevels.Text, 0),
                DamageBonus        = ParseIntOrDefault(RaDamageBonus.Text, 0),
                MovementBonus      = ParseIntOrDefault(RaMovementBonus.Text, 0),
                HpDiceExpression   = string.IsNullOrWhiteSpace(RaHpDice.Text) ? null : RaHpDice.Text.Trim(),
                HpPerLevel         = ParseIntOrDefault(RaHpPerLevel.Text, 0),
                HpFlatBonus        = ParseIntOrDefault(RaHpFlat.Text, 0),
                XpModifierPercent  = ParseIntOrDefault(RaXpMod.Text, 0),
                NwpSlotBonus       = ParseIntOrDefault(RaNwpSlots.Text, 0),
                NwpCostReduction   = ParseIntOrDefault(RaNwpCostRed.Text, 0),
                NwpCheckBonus      = ParseIntOrDefault(RaNwpCheck.Text, 0),
                SurpriseBonus      = ParseIntOrDefault(RaSurprise.Text, 0),
                ReactionBonus      = ParseIntOrDefault(RaReaction.Text, 0),
                InfravisionFeet    = ParseIntOrDefault(RaInfravision.Text, 0),
                MagicResistPercent = ParseIntOrDefault(RaMagicResist.Text, 0),
                GrantsStealth      = RaStealth.IsChecked == true,
                DetectSecretDoors  = RaDetectDoors.IsChecked == true,
                DetectStonework    = RaDetectStone.IsChecked == true,
                SaveBonuses        = ParseKeyValuePairs(RaSaveBonuses.Text),
                SubAbilityBonuses  = ParseKeyValuePairs(RaSubAbility.Text),
                EnemyAttackBonuses = ParseKeyValuePairs(RaEnemyAttack.Text),
                EnemyDamageBonuses = ParseKeyValuePairs(RaEnemyDamage.Text),
                WeaponAttackBonuses = ParseKeyValuePairs(RaWeaponAttack.Text),
                WeaponDamageBonuses = ParseKeyValuePairs(RaWeaponDamage.Text),
            },
        };
    }

    private static AbilityDefinition BuildRacialAbilityTemplate(string templateKey)
    {
        var ability = new AbilityDefinition
        {
            Description = "New Ability",
            PointCost = 0,
            AutoGranted = true,
            Effect = new AbilityEffect(),
        };

        switch ((templateKey ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "darkvision":
                ability.Description = "Darkvision (set range)";
                ability.Effect.InfravisionFeet = 60;
                break;
            case "save_bonus":
                ability.Description = "Save Bonus (set type/value)";
                ability.AutoGranted = false;
                ability.Effect.SaveBonuses ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ability.Effect.SaveBonuses["all"] = 1;
                break;
            case "weapon_attack":
                ability.Description = "Weapon Attack Bonus (set group/value)";
                ability.AutoGranted = false;
                ability.Effect.WeaponAttackBonuses ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ability.Effect.WeaponAttackBonuses["bows"] = 1;
                break;
            case "scaling_attack":
                ability.Description = "Scaling Attack Bonus (set interval)";
                ability.AutoGranted = false;
                ability.Effect.AttackBonusPerLevels = 3;
                break;
            case "weapon_damage":
                ability.Description = "Weapon Damage Bonus (set group/value)";
                ability.AutoGranted = false;
                ability.Effect.WeaponDamageBonuses ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ability.Effect.WeaponDamageBonuses["swords"] = 1;
                break;
            case "enemy_attack":
                ability.Description = "Enemy Attack Bonus (set enemy/value)";
                ability.AutoGranted = false;
                ability.Effect.EnemyAttackBonuses ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ability.Effect.EnemyAttackBonuses["goblinoids"] = 1;
                break;
            case "enemy_damage":
                ability.Description = "Enemy Damage Bonus (set enemy/value)";
                ability.AutoGranted = false;
                ability.Effect.EnemyDamageBonuses ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ability.Effect.EnemyDamageBonuses["goblinoids"] = 1;
                break;
            case "stealth":
                ability.Description = "Stealth";
                ability.Effect.GrantsStealth = true;
                break;
            case "detect_secret_doors":
                ability.Description = "Detect Secret Doors";
                ability.Effect.DetectSecretDoors = true;
                break;
            case "detect_stonework":
                ability.Description = "Detect Stonework";
                ability.Effect.DetectStonework = true;
                break;
            case "nwp_aptitude":
                ability.Description = "NWP Aptitude (set check/slot bonuses)";
                ability.AutoGranted = false;
                ability.Effect.NwpCheckBonus = 1;
                break;
            case "hp_bonus":
                ability.Description = "HP Bonus (set per-level/flat)";
                ability.AutoGranted = false;
                ability.Effect.HpPerLevel = 1;
                break;
            case "magic_resistance":
                ability.Description = "Magic Resistance";
                ability.AutoGranted = false;
                ability.Effect.MagicResistPercent = 5;
                break;
            default:
                ability.Description = "New Ability";
                break;
        }

        ability.Id = SlugifySimple(ability.Description);
        return ability;
    }

    private bool TryBuildRacialAbilityFromTemplateWizard(string templateKey, out AbilityDefinition ability, out string note)
    {
        note = "Adjust fields, click APPLY CHANGES TO LIST, then SAVE RACIAL ABILITIES.";
        ability = BuildRacialAbilityTemplate(templateKey);

        var key = (templateKey ?? string.Empty).Trim().ToLowerInvariant();
        if (key == "blank")
            return true;

        if (!TryShowRacialTemplateDialog(
                title: "Racial Ability Template",
                templateLabel: key,
                defaultAutoGranted: ability.AutoGranted,
                fields: GetTemplateFields(key),
                out var values,
                out bool autoGranted))
        {
            return false;
        }

        ability = BuildAbilityFromTemplateInput(key, values, autoGranted);
        note = "Template values applied. You can fine-tune any field below, then apply and save.";
        return true;
    }

    private static List<(string Key, string Label, string DefaultValue, string Hint)> GetTemplateFields(string templateKey)
    {
        return templateKey switch
        {
            "darkvision" => new List<(string, string, string, string)>
            {
                ("description", "Description", "Darkvision", "Short player-facing text shown in ability lists."),
                ("infravision", "Infravision (ft)", "60", "Common values are 30, 60, 90, or 120 feet."),
                ("cost", "Point Cost", "0", "Use 0 for free racial traits; increase for optional purchasable traits."),
            },
            "save_bonus" => new List<(string, string, string, string)>
            {
                ("description", "Description", "Save Bonus", "Example: Hardy Constitution."),
                ("save_key", "Save Type", "all", "Common keys: all, magic, poison, spell, death, breath, polymorph, petrification."),
                ("save_value", "Save Bonus", "1", "Positive numbers improve saves (for example 1 or 2)."),
                ("cost", "Point Cost", "0", "Set cost for optional purchase; keep 0 for auto traits."),
            },
            "weapon_attack" => new List<(string, string, string, string)>
            {
                ("description", "Description", "Weapon Attack Bonus", "Example: Bow Accuracy."),
                ("weapon_group", "Weapon Group", "bows", "Common groups: bows, swords, axes, hammers, slings, thrown, missile."),
                ("attack_value", "Attack Bonus", "1", "Usually 1 for minor racial bonuses."),
                ("cost", "Point Cost", "0", "Set >0 if this is an optional PO purchase."),
            },
            "scaling_attack" => new List<(string, string, string, string)>
            {
                ("description", "Description", "Scaling Attack Bonus", "Example: Veteran Edge."),
                ("attack_per_levels", "Attack Every X Levels", "3", "Enter 3 for +1 attack at levels 3, 6, 9, and so on."),
                ("cost", "Point Cost", "0", "Set >0 if this is an optional PO purchase."),
            },
            "weapon_damage" => new List<(string, string, string, string)>
            {
                ("description", "Description", "Weapon Damage Bonus", "Example: Axe Damage Focus."),
                ("weapon_group", "Weapon Group", "swords", "Use the same group key naming style as weapon attack bonuses."),
                ("damage_value", "Damage Bonus", "1", "Usually 1 for racial damage perks."),
                ("cost", "Point Cost", "0", "Set >0 if optional; 0 if always granted."),
            },
            "enemy_attack" => new List<(string, string, string, string)>
            {
                ("description", "Description", "Enemy Attack Bonus", "Example: Hatred of Goblinoids."),
                ("enemy_key", "Enemy Group", "goblinoids", "Free-text grouping key. Keep naming consistent between abilities."),
                ("attack_value", "Attack Bonus", "1", "Common values are 1 or 2."),
                ("cost", "Point Cost", "0", "Optional abilities should usually have a cost."),
            },
            "enemy_damage" => new List<(string, string, string, string)>
            {
                ("description", "Description", "Enemy Damage Bonus", "Example: Giant Slayer."),
                ("enemy_key", "Enemy Group", "goblinoids", "Match this key to your enemy attack bonus key when relevant."),
                ("damage_value", "Damage Bonus", "1", "Common values are 1 or 2."),
                ("cost", "Point Cost", "0", "Optional abilities should usually have a cost."),
            },
            "nwp_aptitude" => new List<(string, string, string, string)>
            {
                ("description", "Description", "NWP Aptitude", "Example: Craftsman Training."),
                ("nwp_check", "NWP Check Bonus", "1", "Applies to NWP checks. Typical values: 1 or 2."),
                ("nwp_slots", "NWP Slot Bonus", "0", "Adds extra proficiency slots at character creation."),
                ("nwp_cost_reduction", "NWP Cost Reduction", "0", "Reduces CP cost for NWPs in PO mode."),
                ("cost", "Point Cost", "0", "Optional abilities should usually have a cost."),
            },
            "hp_bonus" => new List<(string, string, string, string)>
            {
                ("description", "Description", "HP Bonus", "Example: Hardy Folk."),
                ("hp_per_level", "HP Per Level", "1", "Per-level bonus applies each level where ability is active."),
                ("hp_flat", "HP Flat Bonus", "0", "One-time flat bonus applied on top of normal HP."),
                ("cost", "Point Cost", "0", "Optional abilities should usually have a cost."),
            },
            "magic_resistance" => new List<(string, string, string, string)>
            {
                ("description", "Description", "Magic Resistance", "Example: Innate Resistance."),
                ("mr_percent", "Magic Resist %", "5", "Percent chance to resist magical effects. Common values: 5-20."),
                ("cost", "Point Cost", "0", "Optional abilities should usually have a cost."),
            },
            "stealth" => new List<(string, string, string, string)>
            {
                ("description", "Description", "Stealth", "Simple on/off trait that grants stealth bonuses."),
                ("cost", "Point Cost", "0", "Optional abilities should usually have a cost."),
            },
            "detect_secret_doors" => new List<(string, string, string, string)>
            {
                ("description", "Description", "Detect Secret Doors", "Simple on/off trait used by races like elves."),
                ("cost", "Point Cost", "0", "Optional abilities should usually have a cost."),
            },
            "detect_stonework" => new List<(string, string, string, string)>
            {
                ("description", "Description", "Detect Stonework", "Simple on/off trait common to dwarven style abilities."),
                ("cost", "Point Cost", "0", "Optional abilities should usually have a cost."),
            },
            _ => new List<(string, string, string, string)>
            {
                ("description", "Description", "New Ability", "Give this ability a clear player-facing name."),
                ("cost", "Point Cost", "0", "Use 0 for free traits; set >0 for optional traits."),
            },
        };
    }

    private bool TryShowRacialTemplateDialog(
        string title,
        string templateLabel,
        bool defaultAutoGranted,
        IReadOnlyList<(string Key, string Label, string DefaultValue, string Hint)> fields,
        out Dictionary<string, string> values,
        out bool autoGranted)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        autoGranted = defaultAutoGranted;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var prompt = new TextBlock
        {
            Text = $"Template: {templateLabel.Replace('_', ' ')}. Fill key values, then click Insert.",
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        };
        root.Children.Add(prompt);

        var formGrid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var boxes = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);

        int rowCursor = 0;
        for (int i = 0; i < fields.Count; i++)
        {
            formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var field = fields[i];

            var label = new TextBlock
            {
                Text = field.Label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 4, 8, 4),
            };
            Grid.SetRow(label, rowCursor);
            Grid.SetColumn(label, 0);
            formGrid.Children.Add(label);

            var box = new TextBox
            {
                Text = field.DefaultValue,
                Margin = new Thickness(0, 2, 0, 2),
                MinHeight = 26,
                ToolTip = string.IsNullOrWhiteSpace(field.Hint) ? null : field.Hint,
            };
            Grid.SetRow(box, rowCursor);
            Grid.SetColumn(box, 1);
            formGrid.Children.Add(box);
            boxes[field.Key] = box;
            rowCursor += 1;

            if (!string.IsNullOrWhiteSpace(field.Hint))
            {
                formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var hint = new TextBlock
                {
                    Text = field.Hint,
                    Margin = new Thickness(0, 0, 0, 6),
                    Opacity = 0.8,
                    TextWrapping = TextWrapping.Wrap,
                };
                Grid.SetRow(hint, rowCursor);
                Grid.SetColumn(hint, 1);
                formGrid.Children.Add(hint);
                rowCursor += 1;
            }
        }

        var formScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = formGrid,
            Margin = new Thickness(0, 0, 0, 10),
            MaxHeight = 360,
        };

        Grid.SetRow(formScroll, 1);
        root.Children.Add(formScroll);

        var autoCheck = new CheckBox
        {
            Content = "Auto Granted",
            IsChecked = defaultAutoGranted,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(autoCheck, 2);
        root.Children.Add(autoCheck);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = new Button { Content = "Cancel", Width = 88, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = "Insert", Width = 88, IsDefault = true };
        ok.Click += (_, _) => Window.GetWindow(ok)!.DialogResult = true;
        buttonPanel.Children.Add(cancel);
        buttonPanel.Children.Add(ok);
        Grid.SetRow(buttonPanel, 3);
        root.Children.Add(buttonPanel);

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = Math.Min(700, 280 + (fields.Count * 64)),
            MinHeight = 360,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Content = root,
        };

        if (dialog.ShowDialog() != true)
            return false;

        foreach (var (key, box) in boxes)
            values[key] = (box.Text ?? string.Empty).Trim();

        autoGranted = autoCheck.IsChecked == true;
        return true;
    }

    private static AbilityDefinition BuildAbilityFromTemplateInput(
        string templateKey,
        IReadOnlyDictionary<string, string> values,
        bool autoGranted)
    {
        string description = GetTemplateText(values, "description", "New Ability");
        int pointCost = ParseTemplateInt(values, "cost", 0);

        var ability = new AbilityDefinition
        {
            Id = SlugifySimple(description),
            Description = description,
            PointCost = pointCost,
            AutoGranted = autoGranted,
            Effect = new AbilityEffect(),
        };

        string key = (templateKey ?? string.Empty).Trim().ToLowerInvariant();
        switch (key)
        {
            case "darkvision":
                ability.Effect.InfravisionFeet = ParseTemplateInt(values, "infravision", 60);
                break;
            case "save_bonus":
            {
                string saveKey = GetTemplateText(values, "save_key", "all").ToLowerInvariant();
                int value = ParseTemplateInt(values, "save_value", 1);
                ability.Effect.SaveBonuses ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ability.Effect.SaveBonuses[saveKey] = value;
                break;
            }
            case "weapon_attack":
            {
                string weaponKey = GetTemplateText(values, "weapon_group", "bows").ToLowerInvariant();
                int value = ParseTemplateInt(values, "attack_value", 1);
                ability.Effect.WeaponAttackBonuses ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ability.Effect.WeaponAttackBonuses[weaponKey] = value;
                break;
            }
            case "scaling_attack":
                ability.Effect.AttackBonusPerLevels = ParseTemplateInt(values, "attack_per_levels", 3);
                break;
            case "weapon_damage":
            {
                string weaponKey = GetTemplateText(values, "weapon_group", "swords").ToLowerInvariant();
                int value = ParseTemplateInt(values, "damage_value", 1);
                ability.Effect.WeaponDamageBonuses ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ability.Effect.WeaponDamageBonuses[weaponKey] = value;
                break;
            }
            case "enemy_attack":
            {
                string enemyKey = GetTemplateText(values, "enemy_key", "goblinoids").ToLowerInvariant();
                int value = ParseTemplateInt(values, "attack_value", 1);
                ability.Effect.EnemyAttackBonuses ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ability.Effect.EnemyAttackBonuses[enemyKey] = value;
                break;
            }
            case "enemy_damage":
            {
                string enemyKey = GetTemplateText(values, "enemy_key", "goblinoids").ToLowerInvariant();
                int value = ParseTemplateInt(values, "damage_value", 1);
                ability.Effect.EnemyDamageBonuses ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                ability.Effect.EnemyDamageBonuses[enemyKey] = value;
                break;
            }
            case "stealth":
                ability.Effect.GrantsStealth = true;
                break;
            case "detect_secret_doors":
                ability.Effect.DetectSecretDoors = true;
                break;
            case "detect_stonework":
                ability.Effect.DetectStonework = true;
                break;
            case "nwp_aptitude":
                ability.Effect.NwpCheckBonus = ParseTemplateInt(values, "nwp_check", 1);
                ability.Effect.NwpSlotBonus = ParseTemplateInt(values, "nwp_slots", 0);
                ability.Effect.NwpCostReduction = ParseTemplateInt(values, "nwp_cost_reduction", 0);
                break;
            case "hp_bonus":
                ability.Effect.HpPerLevel = ParseTemplateInt(values, "hp_per_level", 1);
                ability.Effect.HpFlatBonus = ParseTemplateInt(values, "hp_flat", 0);
                break;
            case "magic_resistance":
                ability.Effect.MagicResistPercent = ParseTemplateInt(values, "mr_percent", 5);
                break;
        }

        return ability;
    }

    private static int ParseTemplateInt(IReadOnlyDictionary<string, string> values, string key, int fallback)
    {
        if (!values.TryGetValue(key, out var raw))
            return fallback;

        return int.TryParse((raw ?? string.Empty).Trim(), out int parsed) ? parsed : fallback;
    }

    private static string GetTemplateText(IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        if (!values.TryGetValue(key, out var raw))
            return fallback;

        var trimmed = (raw ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    // ── Key:value dictionary helpers ─────────────────────────────────────────

    private static Dictionary<string, int> ParseKeyValuePairs(string text)
    {
        var result = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var part in text.Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Trim().Split(':');
            if (kv.Length == 2 && int.TryParse(kv[1].Trim(), out var val))
                result[kv[0].Trim().ToLowerInvariant()] = val;
        }
        return result;
    }

    private static string FormatKeyValuePairs(Dictionary<string, int> dict)
    {
        if (dict == null || dict.Count == 0) return "";
        return string.Join(", ", dict.Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    private static string SlugifySimple(string value)
    {
        string slug = System.Text.RegularExpressions.Regex
            .Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "_")
            .Trim('_');
        return string.IsNullOrWhiteSpace(slug) ? "item" : slug;
    }

    // ── NWPs tab ──────────────────────────────────────────────────────────────

    private void RefreshNwpList()
    {
        _nwpSettings.Clear();
        foreach (var kv in _app.CharacterOptions.GetNonweaponProficiencySettings())
            _nwpSettings[kv.Key] = kv.Value;

        _customNwpIds = new Dictionary<string, CustomNwpData>(
            _app.CharacterOptions.GetCustomNwps(), System.StringComparer.OrdinalIgnoreCase);

        string search        = NwpEditorSearch.Text.Trim();
        string categoryFilter = (NwpCategoryFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        string sourceFilter   = (NwpSourceFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

        _nwpItems = _app.CharacterOptions.GetCatalog().NonweaponProficiencies
            .Where(nwp =>
                (string.IsNullOrWhiteSpace(search)
                    || nwp.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || nwp.Category.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || nwp.Description.Contains(search, System.StringComparison.OrdinalIgnoreCase))
                && NwpMatchesRulesModeFilter(nwp)
                && (categoryFilter is "" or "All Categories"
                    || NwpMatchesCategoryFilter(nwp, categoryFilter))
                && (sourceFilter is "" or "All Sources"
                    || string.Equals(nwp.Source, sourceFilter, System.StringComparison.OrdinalIgnoreCase)
                    // "Core & Player's Option" entries show up under either individual source filter
                    || (string.Equals(nwp.Source, "Core & Player's Option", System.StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(sourceFilter, "Core", System.StringComparison.OrdinalIgnoreCase)
                            || string.Equals(sourceFilter, "Player's Option", System.StringComparison.OrdinalIgnoreCase)))))
            .OrderBy(nwp => nwp.Category)
            .ThenBy(nwp => nwp.Name)
            .ToList();

        NwpEditorList.ItemsSource = _nwpItems
            .Select(nwp =>
            {
                string star = _customNwpIds.ContainsKey(nwp.Id) ? "★ " : string.Empty;
                bool missingPo = nwp.CpCost == 0
                    && nwp.PlayersOptionBaseRating == 0
                    && !string.Equals(nwp.Source, "Player's Option", System.StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(nwp.Source, "Core & Player's Option", System.StringComparison.OrdinalIgnoreCase);
                string display = $"{star}{nwp.Name} [{nwp.Category}]  —  {nwp.Source}";
                return new NwpListItem(display, missingPo);
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(_selectedNwpId))
        {
            int idx = _nwpItems.FindIndex(x => string.Equals(x.Id, _selectedNwpId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                NwpEditorList.SelectedIndex = idx;
        }

        if (NwpEditorList.SelectedIndex < 0 && _nwpItems.Count > 0)
            NwpEditorList.SelectedIndex = 0;

        // Source diagnostics — count by source in the FULL (unfiltered) catalog.
        var all = _app.CharacterOptions.GetCatalog().NonweaponProficiencies;
        int coreOnly    = all.Count(n => string.Equals(n.Source, "Core", System.StringComparison.OrdinalIgnoreCase));
        int poOnly      = all.Count(n => string.Equals(n.Source, "Player's Option", System.StringComparison.OrdinalIgnoreCase));
        int corePo      = all.Count(n => string.Equals(n.Source, "Core & Player's Option", System.StringComparison.OrdinalIgnoreCase));
        int supp        = all.Count(n => string.Equals(n.Source, "Supplement", System.StringComparison.OrdinalIgnoreCase));
        int customCount = all.Count(n => _customNwpIds.ContainsKey(n.Id));
        int warriorEligible = all.Count(n => n.AllowedClasses is { Count: > 0 }
            && (n.AllowedClasses.Contains("fighter", System.StringComparer.OrdinalIgnoreCase)
                || n.AllowedClasses.Contains("paladin", System.StringComparer.OrdinalIgnoreCase)
                || n.AllowedClasses.Contains("ranger", System.StringComparer.OrdinalIgnoreCase)));
        NwpSourceDiagnosticsText.Text =
            $"Core: {coreOnly}  |  PO: {poOnly}  |  Core+PO: {corePo}  |  Supplement: {supp}  |  ★ Custom: {customCount}  |  Warrior-eligible: {warriorEligible}  |  Total: {all.Count}";
    }

    private void NwpEditorSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TabNwps.Visibility == Visibility.Visible)
            RefreshNwpList();
    }

    private void NwpFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabNwps.Visibility == Visibility.Visible)
            RefreshNwpList();
    }

    private void NwpEditorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = NwpEditorList.SelectedIndex;
        if (idx < 0 || idx >= _nwpItems.Count)
            return;

        _creatingNewNwp = false;
        var nwp = _nwpItems[idx];
        _selectedNwpId = nwp.Id;

        _updatingNwpUi = true;

        NwpEditorTitle.Text = "Edit Proficiency";
        NwpEditName.Text = nwp.Name;
        NwpEditorDescription.Text = nwp.Description;

        SelectComboItem(NwpEditCategory, nwp.Category);
        SelectComboItem(NwpEditSource, nwp.Source);
        string coreAbilityDisplay = string.IsNullOrWhiteSpace(nwp.CheckAbility)
            ? "None"
            : CoreAbilityKeyToDisplay.GetValueOrDefault(NormalizeCoreAbilityKey(nwp.CheckAbility), NormalizeCoreAbilityKey(nwp.CheckAbility));
        SelectComboItem(NwpEditCoreAbility, coreAbilityDisplay);
        string poAbilityKey = ResolvePoAbilityKey(nwp);
        string poAbilityDisplay = string.IsNullOrWhiteSpace(poAbilityKey)
            ? "None"
            : PoAbilityKeyToDisplay.GetValueOrDefault(poAbilityKey, poAbilityKey);
        SelectComboItem(NwpEditPoAbility, poAbilityDisplay);
        NwpEditSlots.Text   = nwp.Slots.ToString();
        NwpEditCpCost.Text  = nwp.CpCost.ToString();
        NwpEditCoreModifier.Text = nwp.CheckModifier.ToString();
        NwpEditPoModifier.Text   = nwp.PlayersOptionBaseRating.ToString();
        NwpEditAllowedClasses.Text = nwp.AllowedClasses is { Count: > 0 }
            ? string.Join(", ", nwp.AllowedClasses)
            : string.Empty;
        NwpImportReference.Text = BuildNwpImportReference(nwp);

        bool isCustom = _customNwpIds.ContainsKey(nwp.Id);
        BtnDeleteNwp.IsEnabled = isCustom;
        NwpEditorInfo.Text = isCustom ? "★ Custom / overridden definition" : string.Empty;

        if (!_nwpSettings.TryGetValue(nwp.Id, out var setting))
        {
            setting = new NonweaponProficiencySetting
            {
                Id = nwp.Id,
                AllowMultiple = nwp.AllowMultiple,
                RequiresPlayerText = nwp.RequiresPlayerText,
            };
            _nwpSettings[nwp.Id] = setting;
        }

        NwpAllowMultipleCheck.IsChecked = setting.AllowMultiple;
        NwpRequireTextCheck.IsChecked   = setting.RequiresPlayerText;

        _updatingNwpUi = false;
    }

    private void NwpSettingCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingNwpUi || string.IsNullOrWhiteSpace(_selectedNwpId))
            return;

        if (!_nwpSettings.TryGetValue(_selectedNwpId, out var setting))
        {
            setting = new NonweaponProficiencySetting { Id = _selectedNwpId };
            _nwpSettings[_selectedNwpId] = setting;
        }

        setting.AllowMultiple      = NwpAllowMultipleCheck.IsChecked == true;
        setting.RequiresPlayerText = NwpRequireTextCheck.IsChecked   == true;
    }

    private void BtnNewNwp_Click(object sender, RoutedEventArgs e)
    {
        _creatingNewNwp = true;
        _selectedNwpId  = string.Empty;
        _updatingNwpUi  = true;

        NwpEditorTitle.Text  = "New Proficiency";
        NwpEditName.Text     = string.Empty;
        NwpEditorDescription.Text = string.Empty;
        NwpEditSlots.Text    = "1";
        NwpEditCpCost.Text   = "0";
        NwpEditCoreModifier.Text = "0";
        NwpEditPoModifier.Text   = "0";
        SelectComboItem(NwpEditCategory, "General");
        SelectComboItem(NwpEditSource,   "Custom");
        SelectComboItem(NwpEditCoreAbility,  "Intelligence");
        SelectComboItem(NwpEditPoAbility,  "Intelligence");
        NwpAllowMultipleCheck.IsChecked = false;
        NwpRequireTextCheck.IsChecked   = false;
        BtnDeleteNwp.IsEnabled = false;
        NwpEditorInfo.Text = "Fill in the fields and click SAVE PROFICIENCY DEFINITION.";
        NwpEditAllowedClasses.Text = string.Empty;
        NwpImportReference.Text = "New custom NWP. Import metadata will be blank unless this overrides an imported entry.";
        NwpEditorList.SelectedIndex = -1;

        _updatingNwpUi = false;
        NwpEditName.Focus();
    }

    private void BtnDeleteNwp_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedNwpId))
            return;

        string label = NwpEditName.Text.Trim();
        var answer = MessageBox.Show(
            $"Delete the custom definition for \"{label}\"?\n\nParsed data (if any) will be restored on next load.",
            "Delete Custom NWP",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        _app.CharacterOptions.DeleteCustomNwp(_selectedNwpId);
        _app.CharacterOptions.InvalidateCache();
        _selectedNwpId = string.Empty;
        NwpEditorInfo.Text = "Custom definition deleted.";
        RefreshNwpList();
    }

    private void BtnSaveNwpDefinition_Click(object sender, RoutedEventArgs e)
    {
        string name = NwpEditName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(NwpEditSlots.Text.Trim(), out int slots) || slots < 1)
            slots = 1;
        if (!int.TryParse(NwpEditCoreModifier.Text.Trim(), out int coreModifier))
            coreModifier = 0;
        if (!int.TryParse(NwpEditPoModifier.Text.Trim(), out int poBaseRating) || poBaseRating < 0)
            poBaseRating = 0;
        if (!int.TryParse(NwpEditCpCost.Text.Trim(), out int cpCost) || cpCost < 0)
            cpCost = 0;

        string category = (NwpEditCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "General";
        string source   = (NwpEditSource.SelectedItem   as ComboBoxItem)?.Content?.ToString() ?? "Custom";
        string coreAbilityDisplay = (NwpEditCoreAbility.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Intelligence";
        string coreAbility = CoreAbilityDisplayToKey.GetValueOrDefault(coreAbilityDisplay, coreAbilityDisplay);
        string poAbilityDisplay = (NwpEditPoAbility.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Intelligence";
        string poAbility = PoAbilityDisplayToKey.GetValueOrDefault(poAbilityDisplay, poAbilityDisplay);

        string id = _creatingNewNwp || string.IsNullOrWhiteSpace(_selectedNwpId)
            ? GenerateNwpId(name)
            : _selectedNwpId;

        var existingImport = _app.CharacterOptions.GetCatalog().NonweaponProficiencies
            .FirstOrDefault(x => string.Equals(x.Id, id, System.StringComparison.OrdinalIgnoreCase));

        var data = new CustomNwpData
        {
            Id            = id,
            Name          = name,
            Category      = category,
            Source        = source,
            Slots         = slots,
            CheckAbility  = coreAbility,
            CheckModifier = coreModifier,
            Description   = NwpEditorDescription.Text.Trim(),
            CpCost        = cpCost,
            PlayersOptionBaseRating = poBaseRating,
            PlayersOptionCheckAbility = poAbility,
            AllowedClasses = NwpEditAllowedClasses.Text
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList(),
            GroupFamily = existingImport?.GroupFamily ?? string.Empty,
            ProficiencyGroup = existingImport?.ProficiencyGroup ?? string.Empty,
            IsMultiGroup = existingImport?.IsMultiGroup ?? false,
            GroupsFound = existingImport?.GroupsFound ?? string.Empty,
            PlayersOptionRaw = existingImport?.PlayersOptionRaw ?? string.Empty,
            SourceBook = existingImport?.SourceBook ?? string.Empty,
            SourceTag = existingImport?.SourceTag ?? string.Empty,
            SettingName = existingImport?.SettingName ?? string.Empty,
            Origin = existingImport?.Origin ?? string.Empty,
            DescriptionPreview = existingImport?.DescriptionPreview ?? string.Empty,
            HasDescription = existingImport?.HasDescription ?? !string.IsNullOrWhiteSpace(NwpEditorDescription.Text),
        };

        // Also persist the DM settings (allow-multiple, require-text) for this entry.
        if (!_nwpSettings.TryGetValue(id, out var setting))
        {
            setting = new NonweaponProficiencySetting { Id = id };
            _nwpSettings[id] = setting;
        }
        setting.AllowMultiple      = NwpAllowMultipleCheck.IsChecked == true;
        setting.RequiresPlayerText = NwpRequireTextCheck.IsChecked   == true;

        try
        {
            _app.CharacterOptions.SaveCustomNwp(data);
            _app.CharacterOptions.SaveNonweaponProficiencySettings(_nwpSettings.Values);
            _app.CharacterOptions.InvalidateCache();
            _selectedNwpId  = id;
            _creatingNewNwp = false;
            BtnDeleteNwp.IsEnabled = true;
            NwpEditorInfo.Text = $"★ Saved: {name}";
            RefreshNwpList();

            // If filters hide the saved entry, reset to all so the user can immediately verify the change.
            if (!_nwpItems.Any(x => string.Equals(x.Id, id, System.StringComparison.OrdinalIgnoreCase)))
            {
                SelectComboItem(NwpCategoryFilter, "All Categories");
                SelectComboItem(NwpSourceFilter, "All Sources");
                RefreshNwpList();
                NwpEditorInfo.Text = $"★ Saved: {name} (filters reset to show saved item)";
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnSaveNwpSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _app.CharacterOptions.SaveNonweaponProficiencySettings(_nwpSettings.Values);
            _app.CharacterOptions.InvalidateCache();
            MessageBox.Show("NWP settings saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshNwpList();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Unable to Save NWP Settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCrossoverReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var candidates = _app.CharacterOptions.GetCrossoverCandidates();

            if (candidates.Count == 0)
            {
                MessageBox.Show(
                    "No unmatched crossover candidates found.\nAll Player's Option entries already have exact name matches in the DOCX source.",
                    "Crossover Report",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Unmatched Crossover Candidates ({candidates.Count})");
            sb.AppendLine("These PO entries were auto-merged via normalized-name fallback.");
            sb.AppendLine("Consider aligning names in the DM editor for cleaner data.");
            sb.AppendLine(new string('─', 60));
            sb.AppendLine();
            foreach (var (docxName, docxCat, poName, poCat) in candidates)
            {
                sb.AppendLine($"  DOCX: {docxName}  [{docxCat}]");
                sb.AppendLine($"  PO:   {poName}  [{poCat}]");
                sb.AppendLine();
            }

            var win = new Window
            {
                Title = "Crossover Report",
                Width  = 520,
                Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = System.Windows.Media.Brushes.Black,
            };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            scroll.Content = new TextBox
            {
                Text = sb.ToString(),
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                Background = System.Windows.Media.Brushes.Black,
                Foreground = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10),
            };
            win.Content = scroll;
            win.ShowDialog();
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(ex.Message, "Crossover Report Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string GenerateNwpId(string name)
    {
        string slug = System.Text.RegularExpressions.Regex
            .Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_")
            .Trim('_');
        return $"nwp_custom_{slug}";
    }

    private static string BuildNwpImportReference(NonweaponProficiencyDefinition nwp)
    {
        string allowed = nwp.AllowedClasses is { Count: > 0 }
            ? string.Join(", ", nwp.AllowedClasses)
            : "(all classes)";

        return string.Join(System.Environment.NewLine, new[]
        {
            $"Group Family: {nwp.GroupFamily}",
            $"Proficiency Group: {nwp.ProficiencyGroup}",
            $"Multi-Group?: {(nwp.IsMultiGroup ? "Y" : "N")}",
            $"Groups Found: {nwp.GroupsFound}",
            $"Allowed Classes: {allowed}",
            $"PO Check Ability/Subability: {ResolvePoAbilityKey(nwp)}",
            $"Source Book: {nwp.SourceBook}",
            $"Source Tag: {nwp.SourceTag}",
            $"Setting: {nwp.SettingName}",
            $"Origin: {nwp.Origin}",
            $"PO Raw: {nwp.PlayersOptionRaw}",
            $"Has Description: {(nwp.HasDescription ? "Y" : "N")}",
            $"Description Preview: {nwp.DescriptionPreview}",
        });
    }

    private static bool NwpMatchesCategoryFilter(NonweaponProficiencyDefinition nwp, string filter)
    {
        if (string.Equals(nwp.Category, filter, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(nwp.ProficiencyGroup, filter, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(nwp.GroupsFound))
            return false;

        return nwp.GroupsFound
            .Split(';', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries)
            .Any(group => string.Equals(group, filter, System.StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePoAbilityKey(NonweaponProficiencyDefinition nwp)
    {
        if (!string.IsNullOrWhiteSpace(nwp.PlayersOptionCheckAbility))
            return nwp.PlayersOptionCheckAbility;
        return nwp.CheckAbility;
    }

    private static string NormalizeCoreAbilityKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        if (string.Equals(key, "str_stamina", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "str_muscle", System.StringComparison.OrdinalIgnoreCase))
            return "Strength";
        if (string.Equals(key, "dex_aim", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "dex_balance", System.StringComparison.OrdinalIgnoreCase))
            return "Dexterity";
        if (string.Equals(key, "con_health", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "con_fitness", System.StringComparison.OrdinalIgnoreCase))
            return "Constitution";
        if (string.Equals(key, "int_reason", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "int_knowledge", System.StringComparison.OrdinalIgnoreCase))
            return "Intelligence";
        if (string.Equals(key, "wis_intuition", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "wis_willpower", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "wis_perception", System.StringComparison.OrdinalIgnoreCase))
            return "Wisdom";
        if (string.Equals(key, "cha_leadership", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "cha_appearance", System.StringComparison.OrdinalIgnoreCase))
            return "Charisma";

        return key;
    }

    private static void SelectComboItem(ComboBox combo, string value)
    {
        foreach (object raw in combo.Items)
        {
            if (raw is not ComboBoxItem item)
                continue;

            if (string.Equals(item.Content?.ToString(), value, System.StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    // ── Spells tab ───────────────────────────────────────────────────────────

    private void RefreshSpellEditor()
    {
        string search = SpellSearchBox.Text?.Trim() ?? "";
        var totalCategoryCount = _app.Rules.Spells.Count(s => string.Equals(s.Category, _activeSpellCategory, System.StringComparison.OrdinalIgnoreCase));

        _spellItems = _app.Rules.Spells
            .Where(s => string.Equals(s.Category, _activeSpellCategory, System.StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(search)
                    || s.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || s.Id.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || s.Schools.Contains(search, System.StringComparison.OrdinalIgnoreCase)
                    || s.BriefDescription.Contains(search, System.StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => SpellLevelSortKey(s.Level))
            .ThenBy(s => s.Name)
            .ToList();

        SpellListEditor.ItemsSource = _spellItems;
        SpellCountText.Text = $"Showing {_spellItems.Count} of {totalCategoryCount} {SpellCategoryDisplayName(_activeSpellCategory)} spells";

        if (!string.IsNullOrWhiteSpace(_selectedSpellId))
        {
            int idx = _spellItems.FindIndex(s => string.Equals(s.Id, _selectedSpellId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                SpellListEditor.SelectedIndex = idx;
        }
    }

    private void SpellSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TabSpells.Visibility == Visibility.Visible)
            RefreshSpellEditor();
    }

    private void SpellListEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpellListEditor.SelectedItem is not SpellDefinition spell)
            return;

        _selectedSpellId = spell.Id;
        SpellId.Text = spell.Id;
        SpellLevel.Text = spell.Level;
        SpellName.Text = spell.Name;
        SpellReversal.Text = spell.Reversal;
        SpellSchools.Text = spell.Schools;
        SpellRange.Text = spell.Range;
        SpellComponents.Text = spell.Components;
        SpellMaterials.Text = spell.Materials;
        SpellCastTime.Text = spell.CastTime;
        SpellDuration.Text = spell.Duration;
        SpellArea.Text = spell.Area;
        SpellSave.Text = spell.Save;
        SpellFrequency.Text = spell.Frequency;
        SpellVolume.Text = spell.Volume;
        SpellPage.Text = spell.Page;
        SpellIsHealing.IsChecked = spell.IsHealing;
        SpellDamage.Text = spell.Damage;
        SpellDamageStep.Text = spell.DamageStep;
        SpellDamageScaleStartLevel.Text = spell.DamageScaleStartLevel;
        SpellDamageScaleEveryLevels.Text = spell.DamageScaleEveryLevels;
        SpellDamageMaxAtLevel.Text = spell.DamageMaxAtLevel;
        SpellDamageMax.Text = spell.DamageMax;
        UpdateSpellDamagePreview();
        SpellBriefDescription.Text = spell.BriefDescription;
        SpellDescription.Text = spell.Description;
    }

    private void BtnNewSpell_Click(object sender, RoutedEventArgs e)
    {
        _selectedSpellId = string.Empty;
        SpellListEditor.SelectedItem = null;
        ClearSpellEditor();
    }

    private void ClearSpellEditor()
    {
        SpellId.Text = GetNextAvailableSpellId(_activeSpellCategory, _app.Rules.Spells);
        SpellLevel.Text = "1";
        SpellName.Text = string.Empty;
        SpellReversal.Text = string.Empty;
        SpellSchools.Text = string.Empty;
        SpellRange.Text = string.Empty;
        SpellComponents.Text = string.Empty;
        SpellMaterials.Text = string.Empty;
        SpellCastTime.Text = string.Empty;
        SpellDuration.Text = string.Empty;
        SpellArea.Text = string.Empty;
        SpellSave.Text = string.Empty;
        SpellFrequency.Text = string.Empty;
        SpellVolume.Text = string.Empty;
        SpellPage.Text = string.Empty;
        SpellIsHealing.IsChecked = false;
        SpellDamage.Text = string.Empty;
        SpellDamageStep.Text = string.Empty;
        SpellDamageScaleStartLevel.Text = string.Empty;
        SpellDamageScaleEveryLevels.Text = string.Empty;
        SpellDamageMaxAtLevel.Text = string.Empty;
        SpellDamageMax.Text = string.Empty;
        UpdateSpellDamagePreview();
        SpellBriefDescription.Text = string.Empty;
        SpellDescription.Text = string.Empty;
    }

    private void SpellDamageFields_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSpellDamagePreview();
    }

    private void SpellIsHealing_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSpellDamagePreview();
    }

    private void UpdateSpellDamagePreview()
    {
        SpellDamagePreview.Text = BuildSpellDamageSummary(
            SpellDamage.Text,
            SpellDamageStep.Text,
            SpellDamageScaleStartLevel.Text,
            SpellDamageScaleEveryLevels.Text,
            SpellDamageMaxAtLevel.Text,
            SpellDamageMax.Text,
            SpellIsHealing.IsChecked == true);
    }

    private void BtnSaveSpell_Click(object sender, RoutedEventArgs e)
    {
        string name = SpellName.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Spell Name is required.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryValidateSpellDamageFields(out string damageValidationMessage))
        {
            MessageBox.Show(damageValidationMessage, "Invalid Damage Scaling", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string originalId = _selectedSpellId;
        string enteredId = SpellId.Text?.Trim() ?? string.Empty;
        string finalId;

        if (string.IsNullOrWhiteSpace(enteredId))
        {
            finalId = GetNextAvailableSpellId(_activeSpellCategory, _app.Rules.Spells);
        }
        else
        {
            bool duplicateExists = _app.Rules.Spells.Any(s =>
                !string.Equals(s.Id, originalId, System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(s.Id, enteredId, System.StringComparison.OrdinalIgnoreCase));
            if (duplicateExists)
            {
                MessageBox.Show("SpellID already exists. Enter a unique SpellID.", "Duplicate SpellID", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            finalId = enteredId;
        }

        var updated = new SpellDefinition(
            finalId,
            _activeSpellCategory,
            SpellLevel.Text?.Trim() ?? string.Empty,
            name,
            SpellReversal.Text?.Trim() ?? string.Empty,
            SpellSchools.Text?.Trim() ?? string.Empty,
            SpellRange.Text?.Trim() ?? string.Empty,
            SpellComponents.Text?.Trim() ?? string.Empty,
            SpellMaterials.Text?.Trim() ?? string.Empty,
            SpellCastTime.Text?.Trim() ?? string.Empty,
            SpellDuration.Text?.Trim() ?? string.Empty,
            SpellArea.Text?.Trim() ?? string.Empty,
            SpellSave.Text?.Trim() ?? string.Empty,
            SpellFrequency.Text?.Trim() ?? string.Empty,
            SpellVolume.Text?.Trim() ?? string.Empty,
            SpellPage.Text?.Trim() ?? string.Empty,
            SpellIsHealing.IsChecked == true,
            SpellDamage.Text?.Trim() ?? string.Empty,
            SpellDamageStep.Text?.Trim() ?? string.Empty,
            SpellDamageScaleStartLevel.Text?.Trim() ?? string.Empty,
            SpellDamageScaleEveryLevels.Text?.Trim() ?? string.Empty,
            SpellDamageMaxAtLevel.Text?.Trim() ?? string.Empty,
            SpellDamageMax.Text?.Trim() ?? string.Empty,
            SpellBriefDescription.Text?.Trim() ?? string.Empty,
            SpellDescription.Text?.Trim() ?? string.Empty);

        var allSpells = _app.Rules.Spells
            .Where(s => !string.Equals(s.Id, originalId, System.StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { updated })
            .OrderBy(s => SpellCategorySortKey(s.Category))
            .ThenBy(s => SpellLevelSortKey(s.Level))
            .ThenBy(s => s.Name);

        _app.Rules.SaveSpellDefinitions(allSpells);
        _selectedSpellId = finalId;
        SpellId.Text = finalId;
        RefreshSpellEditor();
        FlashSpellSavedIndicator();
    }

    private void BtnDeleteSpell_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedSpellId))
            return;

        string displayName = _spellItems.FirstOrDefault(s => string.Equals(s.Id, _selectedSpellId, System.StringComparison.OrdinalIgnoreCase))?.Name ?? _selectedSpellId;
        var result = MessageBox.Show(
            $"Delete spell '{displayName}'? This cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        var remaining = _app.Rules.Spells.Where(s => !string.Equals(s.Id, _selectedSpellId, System.StringComparison.OrdinalIgnoreCase));
        _app.Rules.SaveSpellDefinitions(remaining);
        _selectedSpellId = string.Empty;
        SpellListEditor.SelectedItem = null;
        ClearSpellEditor();
        RefreshSpellEditor();
    }

    private void BtnExportSpells_Click(object sender, RoutedEventArgs e)
    {
        string categoryLabel = SpellCategoryDisplayName(_activeSpellCategory);
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = $"Export {categoryLabel} Spells",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"{categoryLabel.ToLowerInvariant()}_spells_export.xlsx",
        };
        if (dlg.ShowDialog() != true)
            return;

        var spells = _app.Rules.Spells
            .Where(s => string.Equals(s.Category, _activeSpellCategory, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => SpellLevelSortKey(s.Level))
            .ThenBy(s => s.Name)
            .ToList();

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.AddWorksheet("Spells");

        // Header row
        string[] headers =
        [
            "ID", "Category", "Level", "Name", "Reversal", "Schools",
            "Range", "Components", "Materials", "Cast Time", "Duration", "Area",
            "Save", "Frequency", "Volume", "Page",
            "Healing",
            "Damage", "Damage Step", "Start Level", "Every Levels", "Max At Level", "Max Damage",
            "Brief Description", "Description"
        ];
        for (int col = 0; col < headers.Length; col++)
        {
            var cell = ws.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.DarkBlue;
            cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
        }

        // Data rows
        for (int row = 0; row < spells.Count; row++)
        {
            var s = spells[row];
            int r = row + 2;
            ws.Cell(r, 1).Value = s.Id;
            ws.Cell(r, 2).Value = s.Category;
            ws.Cell(r, 3).Value = s.Level;
            ws.Cell(r, 4).Value = s.Name;
            ws.Cell(r, 5).Value = s.Reversal;
            ws.Cell(r, 6).Value = s.Schools;
            ws.Cell(r, 7).Value = s.Range;
            ws.Cell(r, 8).Value = s.Components;
            ws.Cell(r, 9).Value = s.Materials;
            ws.Cell(r, 10).Value = s.CastTime;
            ws.Cell(r, 11).Value = s.Duration;
            ws.Cell(r, 12).Value = s.Area;
            ws.Cell(r, 13).Value = s.Save;
            ws.Cell(r, 14).Value = s.Frequency;
            ws.Cell(r, 15).Value = s.Volume;
            ws.Cell(r, 16).Value = s.Page;
            ws.Cell(r, 17).Value = s.IsHealing;
            ws.Cell(r, 18).Value = s.Damage;
            ws.Cell(r, 19).Value = s.DamageStep;
            ws.Cell(r, 20).Value = s.DamageScaleStartLevel;
            ws.Cell(r, 21).Value = s.DamageScaleEveryLevels;
            ws.Cell(r, 22).Value = s.DamageMaxAtLevel;
            ws.Cell(r, 23).Value = s.DamageMax;
            ws.Cell(r, 24).Value = s.BriefDescription;
            ws.Cell(r, 25).Value = s.Description;

            // Zebra stripe
            if (row % 2 == 1)
            {
                var rowRange = ws.Range(r, 1, r, headers.Length);
                rowRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1A1A2E");
                rowRange.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            }
        }

        // Auto-fit columns, but cap description columns to avoid massive widths
        ws.Columns(1, 23).AdjustToContents();
        ws.Column(24).Width = 50;
        ws.Column(25).Width = 80;
        ws.Column(24).Style.Alignment.WrapText = true;
        ws.Column(25).Style.Alignment.WrapText = true;

        // Freeze header row
        ws.SheetView.FreezeRows(1);

        // Auto-filter on headers
        ws.RangeUsed()!.SetAutoFilter();

        try
        {
            wb.SaveAs(dlg.FileName);
            MessageBox.Show($"Exported {spells.Count} {categoryLabel} spells to:\n{dlg.FileName}",
                "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnImportSpells_Click(object sender, RoutedEventArgs e)
    {
        string categoryLabel = SpellCategoryDisplayName(_activeSpellCategory);
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Import {categoryLabel} Spells from Excel",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
        };
        if (dlg.ShowDialog() != true)
            return;

        int updated = 0;
        int skipped = 0;
        var errors = new System.Text.StringBuilder();

        try
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(dlg.FileName);
            var ws = wb.Worksheets.First();

            // Build header → column index map from row 1
            var headerMap = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int col = 1; col <= lastCol; col++)
            {
                string header = ws.Cell(1, col).GetValue<string>().Trim();
                if (!string.IsNullOrEmpty(header))
                    headerMap[header] = col;
            }

            if (!headerMap.ContainsKey("ID"))
            {
                MessageBox.Show("The file must have an 'ID' column in row 1 to match spells.",
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string Get(ClosedXML.Excel.IXLRow row, string col) =>
                headerMap.TryGetValue(col, out int c) ? row.Cell(c).GetValue<string>().Trim() : string.Empty;

            static bool ParseBool(string text)
            {
                string value = (text ?? string.Empty).Trim();
                if (bool.TryParse(value, out bool parsed))
                    return parsed;

                return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "y", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }

            // Build a mutable lookup of all spells by ID
            var spellDict = _app.Rules.Spells.ToDictionary(
                s => s.Id, s => s,
                System.StringComparer.OrdinalIgnoreCase);

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                string id = Get(row, "ID");
                if (string.IsNullOrWhiteSpace(id))
                {
                    skipped++;
                    continue;
                }

                if (!spellDict.TryGetValue(id, out var existing))
                {
                    errors.AppendLine($"Row {r}: ID '{id}' not found — skipped.");
                    skipped++;
                    continue;
                }

                // Replace with values from xlsx; blank cells clear the existing value
                var updated_spell = existing with
                {
                    Level                    = Get(row, "Level"),
                    Name                     = string.IsNullOrWhiteSpace(Get(row, "Name")) ? existing.Name : Get(row, "Name"),
                    Reversal                 = Get(row, "Reversal"),
                    Schools                  = Get(row, "Schools"),
                    Range                    = Get(row, "Range"),
                    Components               = Get(row, "Components"),
                    Materials                = Get(row, "Materials"),
                    CastTime                 = Get(row, "Cast Time"),
                    Duration                 = Get(row, "Duration"),
                    Area                     = Get(row, "Area"),
                    Save                     = Get(row, "Save"),
                    Frequency                = Get(row, "Frequency"),
                    Volume                   = Get(row, "Volume"),
                    Page                     = Get(row, "Page"),
                    IsHealing                = ParseBool(Get(row, "Healing")),
                    Damage                   = Get(row, "Damage"),
                    DamageStep               = Get(row, "Damage Step"),
                    DamageScaleStartLevel    = Get(row, "Start Level"),
                    DamageScaleEveryLevels   = Get(row, "Every Levels"),
                    DamageMaxAtLevel         = Get(row, "Max At Level"),
                    DamageMax                = Get(row, "Max Damage"),
                    BriefDescription         = Get(row, "Brief Description"),
                    Description              = Get(row, "Description"),
                };

                spellDict[id] = updated_spell;
                updated++;
            }

            // Persist — keep all spells, replace updated ones
            var finalList = _app.Rules.Spells.Select(s =>
                spellDict.TryGetValue(s.Id, out var updated_s) ? updated_s : s);
            _app.Rules.SaveSpellDefinitions(finalList);

            RefreshSpellEditor();

            string summary = $"Import complete.\nUpdated: {updated}  |  Skipped: {skipped}";
            if (errors.Length > 0)
                summary += $"\n\nWarnings:\n{errors}";
            MessageBox.Show(summary, "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed: {ex.Message}", "Import Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void FlashSpellSavedIndicator()
    {
        SpellSavedIndicator.Visibility = Visibility.Visible;
        await System.Threading.Tasks.Task.Delay(2000);
        SpellSavedIndicator.Visibility = Visibility.Collapsed;
    }

    private void TabSpells_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.S &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            BtnSaveSpell_Click(sender, e);
            e.Handled = true;
        }
    }

    private static string SpellCategoryDisplayName(string category) => category switch
    {
        "divine" => "Divine",
        "psionic" => "Psionic",
        _ => "Arcane",
    };

    private static int SpellCategorySortKey(string category) => category switch
    {
        "arcane" => 0,
        "divine" => 1,
        "psionic" => 2,
        _ => 9,
    };

    private static int SpellLevelSortKey(string level)
    {
        return int.TryParse(level, out int parsed) ? parsed : 99;
    }

    private bool TryValidateSpellDamageFields(out string message)
    {
        string damage = SpellDamage.Text?.Trim() ?? string.Empty;
        string step = SpellDamageStep.Text?.Trim() ?? string.Empty;
        string startLevelText = SpellDamageScaleStartLevel.Text?.Trim() ?? string.Empty;
        string everyLevelsText = SpellDamageScaleEveryLevels.Text?.Trim() ?? string.Empty;
        string maxAtLevelText = SpellDamageMaxAtLevel.Text?.Trim() ?? string.Empty;
        string maxDamage = SpellDamageMax.Text?.Trim() ?? string.Empty;

        bool hasScaling = !string.IsNullOrWhiteSpace(step)
            || !string.IsNullOrWhiteSpace(startLevelText)
            || !string.IsNullOrWhiteSpace(everyLevelsText)
            || !string.IsNullOrWhiteSpace(maxAtLevelText)
            || !string.IsNullOrWhiteSpace(maxDamage);

        if (hasScaling && string.IsNullOrWhiteSpace(damage))
        {
            message = "Base Damage is required when using damage scaling fields.";
            return false;
        }

        if (!TryParsePositiveIntField(startLevelText, "Start At Level", out int? startLevel, out message))
            return false;
        if (!TryParsePositiveIntField(everyLevelsText, "Increase Every", out int? everyLevels, out message))
            return false;
        if (!TryParsePositiveIntField(maxAtLevelText, "Max At Level", out int? maxAtLevel, out message))
            return false;

        if (!string.IsNullOrWhiteSpace(step) && !everyLevels.HasValue)
        {
            message = "Increase Every is required when Increase By is set.";
            return false;
        }

        if (everyLevels.HasValue && !startLevel.HasValue)
        {
            message = "Start At Level is required when Increase Every is set.";
            return false;
        }

        if (maxAtLevel.HasValue && !startLevel.HasValue)
        {
            message = "Start At Level is required when Max At Level is set.";
            return false;
        }

        if (maxAtLevel.HasValue && startLevel.HasValue && maxAtLevel.Value < startLevel.Value)
        {
            message = "Max At Level cannot be lower than Start At Level.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(maxDamage) && !maxAtLevel.HasValue)
        {
            message = "Max At Level is required when Max Damage is set.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool TryParsePositiveIntField(string text, string fieldName, out int? value, out string message)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            message = string.Empty;
            return true;
        }

        if (!int.TryParse(text, out int parsed) || parsed <= 0)
        {
            message = $"{fieldName} must be a positive whole number.";
            return false;
        }

        value = parsed;
        message = string.Empty;
        return true;
    }

    private static string BuildSpellDamageSummary(
        string? damage,
        string? damageStep,
        string? startLevel,
        string? everyLevels,
        string? maxAtLevel,
        string? maxDamage,
        bool isHealing)
    {
        string baseDamage = (damage ?? string.Empty).Trim();
        string step = (damageStep ?? string.Empty).Trim();
        string start = (startLevel ?? string.Empty).Trim();
        string every = (everyLevels ?? string.Empty).Trim();
        string maxLevel = (maxAtLevel ?? string.Empty).Trim();
        string capDamage = (maxDamage ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(baseDamage)
            && string.IsNullOrWhiteSpace(step)
            && string.IsNullOrWhiteSpace(start)
            && string.IsNullOrWhiteSpace(every)
            && string.IsNullOrWhiteSpace(maxLevel)
            && string.IsNullOrWhiteSpace(capDamage))
        {
            return isHealing
                ? "No healing scaling configured."
                : "No damage scaling configured.";
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(baseDamage))
            parts.Add($"Base: {baseDamage}");
        if (!string.IsNullOrWhiteSpace(step))
            parts.Add($"+{step}");
        if (!string.IsNullOrWhiteSpace(every))
        {
            string cadence = $"every {every} level";
            if (every != "1")
                cadence += "s";

            if (!string.IsNullOrWhiteSpace(start))
                parts.Add($"from level {start}, {cadence}");
            else
                parts.Add(cadence);
        }
        else if (!string.IsNullOrWhiteSpace(start))
        {
            parts.Add($"starts at level {start}");
        }

        if (!string.IsNullOrWhiteSpace(maxLevel) && !string.IsNullOrWhiteSpace(capDamage))
            parts.Add($"caps at level {maxLevel} ({capDamage})");
        else if (!string.IsNullOrWhiteSpace(maxLevel))
            parts.Add($"caps at level {maxLevel}");
        else if (!string.IsNullOrWhiteSpace(capDamage))
            parts.Add($"max damage {capDamage}");

        string summary = string.Join("; ", parts);
        return isHealing ? $"Heals: {summary}" : summary;
    }

    internal static string GetNextAvailableSpellId(
        string category,
        IEnumerable<SpellDefinition> spells)
    {
        string prefix = category switch
        {
            "divine" => "PRI",
            "psionic" => "PSI",
            _ => "WIZ",
        };

        int highestId = spells
            .Where(spell => string.Equals(spell.Category, category, System.StringComparison.OrdinalIgnoreCase))
            .Select(spell => Regex.Match(spell.Id ?? string.Empty, $@"^{prefix}(\d+)$", RegexOptions.IgnoreCase))
            .Where(match => match.Success && int.TryParse(match.Groups[1].Value, out _))
            .Select(match => int.Parse(match.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{highestId + 1:D6}";
    }

    // ── Monsters tab ─────────────────────────────────────────────────────────

    private void TabBtnMonsters_Click(object sender, RoutedEventArgs e) => ShowTab("monsters");

    private void RefreshMonsterEditor()
    {
        string search = MonsterSearchBox.Text?.Trim() ?? "";
        string typeFilter = MonsterTypeFilter.SelectedItem is string s && s != "(All Types)" ? s : "";

        // Rebuild type filter ComboBox if empty
        if (MonsterTypeFilter.Items.Count <= 1)
        {
            MonsterTypeFilter.Items.Clear();
            MonsterTypeFilter.Items.Add("(All Types)");
            foreach (var t in _app.Rules.Monsters.Select(m => m.MonsterType).Distinct().OrderBy(x => x))
                MonsterTypeFilter.Items.Add(t);
            MonsterTypeFilter.SelectedIndex = 0;
        }

        _monsterItems = _app.Rules.Monsters
            .Where(m => (string.IsNullOrEmpty(search) ||
                         m.Name.Contains(search, System.StringComparison.OrdinalIgnoreCase) ||
                         m.MonsterType.Contains(search, System.StringComparison.OrdinalIgnoreCase)) &&
                        (string.IsNullOrEmpty(typeFilter) || m.MonsterType == typeFilter))
            .OrderBy(m => m.Name)
            .ToList();

        MonsterListEditor.ItemsSource = _monsterItems;
        MonsterCountText.Text = $"Showing {_monsterItems.Count} of {_app.Rules.Monsters.Count} monsters";

        if (!string.IsNullOrEmpty(_selectedMonsterId))
        {
            int idx = _monsterItems.FindIndex(m => string.Equals(m.Id, _selectedMonsterId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) MonsterListEditor.SelectedIndex = idx;
        }
    }

    private void RefreshMonsterEditorList() => RefreshMonsterEditor();

    private void MonsterSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (TabMonsters.Visibility == Visibility.Visible) RefreshMonsterEditor();
    }

    private void MonsterTypeFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TabMonsters.Visibility == Visibility.Visible) RefreshMonsterEditor();
    }

    private void MonsterListEditor_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MonsterListEditor.SelectedItem is not MonsterDefinition m) return;
        _selectedMonsterId = m.Id;
        MonsterName.Text           = m.Name;
        MonsterType.Text           = m.MonsterType;
        MonsterHitDice.Text        = m.HitDice;
        MonsterAC.Text             = m.ArmorClass.ToString();
        MonsterThac0.Text          = m.EffectiveThac0Text;
        MonsterMovement.Text       = m.Movement;
        MonsterAttacks.Text        = m.Attacks.ToString();
        MonsterDamage.Text         = m.Damage;
        MonsterSize.Text           = m.Size;
        MonsterIntelligence.Text   = m.Intelligence;
        MonsterAlignment.Text      = m.Alignment;
        MonsterMorale.Text         = m.Morale;
        MonsterXP.Text             = m.XpValue.ToString();
        MonsterNumAppearing.Text   = m.NumberAppearing;
        MonsterFrequency.Text      = m.Frequency;
        MonsterTreasure.Text       = m.TreasureType;
        MonsterMagicRes.Text       = m.MagicResistance;
        MonsterSpecialAttacks.Text = m.SpecialAttacks;
        MonsterSpecialDefenses.Text = m.SpecialDefenses;
        MonsterSpecialAbilities.Text = m.SpecialAbilities;
        MonsterCombat.Text         = m.Combat;
        MonsterHabitatSociety.Text = m.HabitatSociety;
        MonsterEcology.Text        = m.Ecology;
        MonsterSource.Text         = m.Source;
        MonsterDescription.Text    = m.Description;
        LoadAgeStages(m.AgeStages, IsDragonLike(m.MonsterType, m.Name));
    }

    private void BtnNewMonster_Click(object sender, RoutedEventArgs e)
    {
        _selectedMonsterId = string.Empty;
        MonsterName.Text = "New Monster";
        MonsterType.Text = "";
        MonsterHitDice.Text = "1";
        MonsterAC.Text = "10";
        MonsterThac0.Text = "20";
        MonsterMovement.Text = "12";
        MonsterAttacks.Text = "1";
        MonsterDamage.Text = "1d6";
        MonsterSize.Text = "M";
        MonsterIntelligence.Text = "Average";
        MonsterAlignment.Text = "Neutral";
        MonsterMorale.Text = "Average (9)";
        MonsterXP.Text = "0";
        MonsterNumAppearing.Text = "1";
        MonsterFrequency.Text = "Uncommon";
        MonsterTreasure.Text = "Nil";
        MonsterMagicRes.Text = "Nil";
        MonsterSpecialAttacks.Text = "None";
        MonsterSpecialDefenses.Text = "None";
        MonsterSpecialAbilities.Text = "None";
        MonsterCombat.Text = "";
        MonsterHabitatSociety.Text = "";
        MonsterEcology.Text = "";
        MonsterSource.Text = "Custom";
        MonsterDescription.Text = "";
        LoadAgeStages(null, false);
        MonsterListEditor.SelectedItem = null;
    }

    private void BtnSaveMonster_Click(object sender, RoutedEventArgs e)
    {
        string name = MonsterName.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name)) return;

        int.TryParse(MonsterAC.Text,      out int ac);
        var thac0Input = MonsterThac0.Text?.Trim() ?? "";
        int.TryParse(thac0Input, out int thac0);
        int.TryParse(MonsterAttacks.Text, out int attacks);
        int.TryParse(MonsterXP.Text,      out int xp);

        string id = string.IsNullOrWhiteSpace(_selectedMonsterId)
            ? "mon_" + name.ToLowerInvariant().Replace(" ", "_").Replace(",", "").Replace("'", "")
            : _selectedMonsterId;

        // collect age stages from the editable grid
        List<DragonAgeStage>? ageStages = _ageStageRows.Count > 0
            ? _ageStageRows.Select(vm => new DragonAgeStage(
                vm.StageNumber, vm.Category, vm.HitDice, vm.ArmorClass,
                vm.Thac0, vm.BreathWeapon, vm.SpellLevel, vm.MagicResistance, vm.SpecialAbilities))
              .ToList()
            : null;

        var updated = new MonsterDefinition(
            id, name,
            MonsterType.Text?.Trim() ?? "",
            MonsterSource.Text?.Trim() ?? "",
            MonsterHitDice.Text?.Trim() ?? "",
            ac,
            MonsterMovement.Text?.Trim() ?? "",
            thac0,
            attacks <= 0 ? 1 : attacks,
            MonsterDamage.Text?.Trim() ?? "",
            MonsterSpecialAttacks.Text?.Trim() ?? "",
            MonsterSpecialDefenses.Text?.Trim() ?? "",
            MonsterMagicRes.Text?.Trim() ?? "",
            MonsterSize.Text?.Trim() ?? "",
            MonsterMorale.Text?.Trim() ?? "",
            xp,
            MonsterNumAppearing.Text?.Trim() ?? "",
            MonsterFrequency.Text?.Trim() ?? "",
            MonsterIntelligence.Text?.Trim() ?? "",
            MonsterAlignment.Text?.Trim() ?? "",
            MonsterTreasure.Text?.Trim() ?? "",
            MonsterDescription.Text?.Trim() ?? "",
            ageStages,
            MonsterSpecialAbilities.Text?.Trim() ?? "",
            MonsterCombat.Text?.Trim() ?? "",
            MonsterHabitatSociety.Text?.Trim() ?? "",
            MonsterEcology.Text?.Trim() ?? ""
        )
        {
            Thac0Text = thac0Input
        };

        var allMonsters = _app.Rules.Monsters
            .Where(m => !string.Equals(m.Id, id, System.StringComparison.OrdinalIgnoreCase))
            .Concat(new[] { updated })
            .OrderBy(m => m.Name);

        _app.Rules.SaveMonsterDefinitions(allMonsters);
        _selectedMonsterId = id;
        RefreshMonsterEditorList();
        FlashMonsterSavedIndicator();
    }

    private async void FlashMonsterSavedIndicator()
    {
        MonsterSavedIndicator.Visibility = Visibility.Visible;
        await System.Threading.Tasks.Task.Delay(2000);
        MonsterSavedIndicator.Visibility = Visibility.Collapsed;
    }

    private void TabMonsters_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.S &&
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            BtnSaveMonster_Click(sender, e);
            e.Handled = true;
        }
    }

    private void BtnAddMonsterToCombatTracker_Click(object sender, RoutedEventArgs e)
    {
        var selectedMonster = MonsterListEditor.SelectedItem as MonsterDefinition
            ?? _monsterItems.FirstOrDefault(m => string.Equals(m.Id, _selectedMonsterId, System.StringComparison.OrdinalIgnoreCase));

        if (selectedMonster is null)
        {
            MessageBox.Show(
                "Select a monster first.",
                "Monsters",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int quantity = 1;
        var quantityWindow = new Window
        {
            Title = "Add Monster To Combat Tracker",
            Width = 360,
            Height = 190,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = $"How many '{selectedMonster.Name}' should be added?",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var quantityBox = new TextBox
        {
            Text = "1",
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 10),
        };
        panel.Children.Add(quantityBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => quantityWindow.DialogResult = false;
        buttons.Children.Add(cancel);

        var add = new Button { Content = "Add", Width = 80, IsDefault = true };
        add.Click += (_, _) =>
        {
            if (!int.TryParse(quantityBox.Text.Trim(), out quantity) || quantity < 1)
            {
                MessageBox.Show("Enter a quantity of 1 or greater.", "Monsters", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            quantityWindow.DialogResult = true;
        };
        buttons.Children.Add(add);

        panel.Children.Add(buttons);
        quantityWindow.Content = panel;

        if (quantityWindow.ShowDialog() != true)
            return;

        _app.PendingCombatMonsterId = selectedMonster.Id;
        _app.PendingCombatMonsterQuantity = quantity;
        _app.GoTo("combat_tracker");
    }

    private void BtnDeleteMonster_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedMonsterId)) return;
        string displayName = _monsterItems.FirstOrDefault(m => m.Id == _selectedMonsterId)?.Name ?? _selectedMonsterId;
        var result = System.Windows.MessageBox.Show(
            $"Delete monster '{displayName}'? This cannot be undone.",
            "Confirm Delete", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        var remaining = _app.Rules.Monsters.Where(m => m.Id != _selectedMonsterId);
        _app.Rules.SaveMonsterDefinitions(remaining);
        _selectedMonsterId = string.Empty;
        MonsterTypeFilter.Items.Clear();
        RefreshMonsterEditorList();
    }

    private static bool IsDragonLike(string? monsterType, string? monsterName)
    {
        if (!string.IsNullOrWhiteSpace(monsterType) &&
            monsterType.Contains("dragon", System.StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(monsterName) &&
            monsterName.Contains("dragon", System.StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string GetDragonAgeCategory(int stage) => stage switch
    {
        1 => "Hatchling",
        2 => "Very Young",
        3 => "Young",
        4 => "Juvenile",
        5 => "Young Adult",
        6 => "Adult",
        7 => "Mature Adult",
        8 => "Old",
        9 => "Very Old",
        10 => "Venerable",
        11 => "Wyrm",
        _ => "Great Wyrm",
    };

    private static string GetDragonMagicResistanceByStage(int stage) => stage switch
    {
        <= 2 => "Nil",
        3 => "5%",
        4 => "10%",
        5 => "15%",
        6 => "20%",
        7 => "25%",
        8 => "30%",
        9 => "35%",
        10 => "40%",
        11 => "45%",
        _ => "50%",
    };

    private static string GetDragonSpellLevelByStage(int stage) => stage switch
    {
        <= 2 => "None",
        3 => "1st",
        4 => "2nd",
        5 => "3rd",
        6 => "4th",
        7 => "5th",
        8 => "6th",
        9 => "7th",
        10 => "8th",
        _ => "9th",
    };

    private static int ParseLeadingInt(string? text, int fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        int i = 0;
        while (i < text.Length && !char.IsDigit(text[i]) && text[i] != '-') i++;
        int start = i;
        if (i < text.Length && text[i] == '-') i++;
        while (i < text.Length && char.IsDigit(text[i])) i++;
        return i > start && int.TryParse(text[start..i], out int parsed) ? parsed : fallback;
    }

    // AD&D 2e Monstrous Manual dragon table: HD modifiers by age category 1..12.
    // Base species stats are treated as Juvenile (category 4), where modifier is 0.
    private static readonly int[] DragonHdModifiersByStage = { -6, -4, -2, 0, 1, 2, 3, 4, 5, 6, 7, 8 };

    private static string ComputeDragonBreathByAge(int derivedHd)
    {
        // Canonical rule: breath weapon damage equals the dragon's current hit points.
        // We display an HD-linked approximation for convenience.
        return $"Current HP (about {System.Math.Max(1, derivedHd)}d8)";
    }

    private void GetDragonBaseStats(out int baseHd, out int baseAc, out int baseThac0)
    {
        baseHd = ParseLeadingInt(MonsterHitDice.Text, 6);

        int.TryParse(MonsterAC.Text, out baseAc);
        if (baseAc == 0 && string.IsNullOrWhiteSpace(MonsterAC.Text)) baseAc = 10;

        baseThac0 = ParseLeadingInt(MonsterThac0.Text, 20);
    }

    private static void ComputeDerivedDragonCombat(int stage, int baseHd, int baseAc, int baseThac0,
        out int derivedHd, out int derivedAc, out int derivedThac0, out string derivedBreath)
    {
        int stageIndex = System.Math.Clamp(stage, 1, 12) - 1;
        int hdMod = DragonHdModifiersByStage[stageIndex];

        // HD uses the official age modifier table.
        derivedHd = System.Math.Max(1, baseHd + hdMod);

        // AC progression is 1 point per age category relative to Juvenile (category 4).
        derivedAc = baseAc + (4 - stage);

        // THAC0 tracks age/HD progression from the species base THAC0 (juvenile baseline).
        derivedThac0 = baseThac0 - hdMod;

        derivedBreath = ComputeDragonBreathByAge(derivedHd);
    }

    private void InitializeDragonStageTemplateRows(bool includeDerivedDefaults)
    {
        _ageStageRows.Clear();

        GetDragonBaseStats(out int baseHd, out int baseAc, out int baseThac0);

        for (int stage = 1; stage <= 12; stage++)
        {
            ComputeDerivedDragonCombat(stage, baseHd, baseAc, baseThac0,
                out int derivedHd, out int derivedAc, out int derivedThac0, out string derivedBreath);

            _ageStageRows.Add(new DragonAgeStageVm
            {
                StageNumber      = stage,
                Category         = GetDragonAgeCategory(stage),
                HitDice          = includeDerivedDefaults ? derivedHd.ToString() : "",
                ArmorClass       = includeDerivedDefaults ? derivedAc : 0,
                Thac0            = includeDerivedDefaults ? derivedThac0 : 20,
                BreathWeapon     = includeDerivedDefaults ? derivedBreath : "",
                SpellLevel       = includeDerivedDefaults ? GetDragonSpellLevelByStage(stage) : "None",
                MagicResistance  = includeDerivedDefaults ? GetDragonMagicResistanceByStage(stage) : "Nil",
                SpecialAbilities = includeDerivedDefaults
                    ? string.Join("; ", new[]
                    {
                        MonsterSpecialAttacks.Text?.Trim(),
                        MonsterSpecialDefenses.Text?.Trim(),
                        MonsterSpecialAbilities.Text?.Trim()
                    }.Where(s => !string.IsNullOrWhiteSpace(s)))
                    : "",
            });
        }
    }

    private void LoadAgeStages(List<DragonAgeStage>? stages, bool isDragon)
    {
        _ageStageRows.Clear();

        GetDragonBaseStats(out int baseHd, out int baseAc, out int baseThac0);

        if (stages is { Count: > 0 })
        {
            foreach (var s in stages)
            {
                int stage = s.StageNumber > 0 ? s.StageNumber : (_ageStageRows.Count + 1);
                _ageStageRows.Add(new DragonAgeStageVm
                {
                    StageNumber      = stage,
                    Category         = isDragon ? GetDragonAgeCategory(stage) : s.Category,
                       HitDice          = s.HitDice,
                       ArmorClass       = s.ArmorClass,
                       Thac0            = s.Thac0,
                       BreathWeapon     = s.BreathWeapon ?? "",
                    SpellLevel       = string.IsNullOrWhiteSpace(s.SpellLevel) ? GetDragonSpellLevelByStage(stage) : s.SpellLevel,
                    MagicResistance  = string.IsNullOrWhiteSpace(s.MagicResistance) ? GetDragonMagicResistanceByStage(stage) : s.MagicResistance,
                    SpecialAbilities = s.SpecialAbilities,
                });
            }
            AgeStagesSection.Visibility = Visibility.Visible;
        }
        else if (isDragon)
        {
            // Restore the old dragon chart behavior: show a full 12-stage table even when data is missing.
            InitializeDragonStageTemplateRows(includeDerivedDefaults: true);
            AgeStagesSection.Visibility = Visibility.Visible;
        }
        else
        {
            AgeStagesSection.Visibility = Visibility.Collapsed;
        }
        AgeStagesGrid.ItemsSource = _ageStageRows;
    }

    private void BtnInitDragonStages_Click(object sender, RoutedEventArgs e)
    {
        if (_ageStageRows.Count > 0)
        {
            var confirm = System.Windows.MessageBox.Show(
                "Age stages already exist. Replace them with a blank 12-stage template?",
                "Initialize Stages", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
        }

        InitializeDragonStageTemplateRows(includeDerivedDefaults: true);

        AgeStagesGrid.ItemsSource = _ageStageRows;
        AgeStagesSection.Visibility = Visibility.Visible;
    }

    private void BtnClearDragonStages_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "Remove all age stages from this monster?",
            "Confirm Clear", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;
        _ageStageRows.Clear();
        AgeStagesSection.Visibility = Visibility.Collapsed;
    }
}

// ── Dragon age stage view-model (mutable; required for DataGrid two-way binding) ──
public class DragonAgeStageVm
{
    public int    StageNumber      { get; set; }
    public string Category         { get; set; } = "";
    public string HitDice          { get; set; } = "";
    public int    ArmorClass       { get; set; }
    public int    Thac0            { get; set; }
    public string BreathWeapon     { get; set; } = "";
    public string SpellLevel       { get; set; } = "";
    public string MagicResistance  { get; set; } = "";
    public string SpecialAbilities { get; set; } = "";
}
