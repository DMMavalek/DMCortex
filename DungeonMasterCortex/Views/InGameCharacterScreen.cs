using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public class InGameCharacterScreen : UserControl, IScreen
{
    private static readonly bool ComponentParsingAuditEnabled = false;

    private readonly MainWindow _app;
    private int _pendingCharacterIndex = -1;
    private CharacterSheet? _selectedCharacter;

    private readonly TextBlock _characterHeader;
    private readonly TextBlock _statusText;
    private readonly TextBox _notesEditor;
    private readonly TextBox _sessionNotesEditor;
    private readonly ListBox _inventoryList;
    private readonly TextBox _newItemName;
    private readonly TextBox _newItemQuantity;
    private readonly ComboBox _quickSpellCombo;
    private readonly TextBox _quickSpellNotes;
    private readonly ListBox _preparedSpellList;
    private readonly ListBox _castSpellList;
    private readonly ListBox _spellComponentList;
    private readonly ListBox _componentUsageList;
    private readonly TextBlock _spellSummary;
    private readonly TextBlock _componentSummary;
    private readonly TextBlock _componentUsageSummary;
    private readonly Button _saveNotesButton;
    private readonly Button _addItemButton;
    private readonly Button _increaseQtyButton;
    private readonly Button _decreaseQtyButton;
    private readonly Button _removeItemButton;
    private readonly Button _syncComponentsButton;
    private readonly Button _castSpellButton;
    private readonly Button _togglePreparedButton;
    private readonly Button _removeCastButton;
    private readonly Button _openSpellTrackerButton;
    private readonly Expander _abilityGroupsExpander;
    private readonly Expander _nwpExpander;
    private readonly Expander _rogueSkillsExpander;
    private readonly Expander _traitsExpander;
    private readonly Expander _classSkillsExpander;
    private readonly Expander _racialSkillsExpander;
    private readonly ListBox _nwpList;
    private readonly ListBox _rogueSkillsList;
    private readonly ListBox _traitsList;
    private readonly ListBox _classSkillsList;
    private readonly ListBox _racialSkillsList;

    private sealed class InventoryRow
    {
        public required EquipmentSelection Selection { get; init; }
        public required string DisplayName { get; init; }
        public override string ToString() => DisplayName;
    }

    private sealed class QuickSpellRow
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Level { get; init; }
        public string Source { get; set; } = string.Empty;
        public override string ToString() => $"L{Level} {Name} [{Source}]";
    }

    private sealed class PreparedSpellRow
    {
        public int Index { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public override string ToString() => DisplayName;
    }

    private sealed class CastSpellRow
    {
        public int Index { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public override string ToString() => DisplayName;
    }

    private sealed class SpellComponentRow
    {
        public string Name { get; init; } = string.Empty;
        public string SourceType { get; init; } = string.Empty;
        public int SpellCount { get; init; }
        public bool InInventory { get; init; }
        public List<string> SpellIds { get; init; } = new();
        public string SpellPreview { get; init; } = string.Empty;
        public override string ToString()
        {
            string usage = $"{SourceType}, used by {SpellCount} spell{(SpellCount == 1 ? string.Empty : "s")}";
            if (!string.IsNullOrWhiteSpace(SpellPreview))
                usage += $": {SpellPreview}";

            return $"{(InInventory ? "[OK]" : "[MISSING]")} {Name} ({usage})";
        }
    }

    public InGameCharacterScreen(MainWindow app)
    {
        _app = app;

        var root = new Grid { Margin = new Thickness(18, 12, 18, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "IN-GAME CHARACTER",
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(StyleProperty, "TitleText");
        header.Children.Add(title);

        var headerButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _openSpellTrackerButton = CreateHeaderButton("OPEN SPELL TRACKER", 170, BtnOpenSpellTracker_Click);
        headerButtons.Children.Add(_openSpellTrackerButton);
        headerButtons.Children.Add(CreateHeaderButton("CHAR SHEET", 120, BtnCharacterSheet_Click));
        headerButtons.Children.Add(CreateHeaderButton("◀ CHARACTERS", 130, BtnBack_Click, isLast: true));
        Grid.SetColumn(headerButtons, 1);
        header.Children.Add(headerButtons);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var infoPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 10) };
        _characterHeader = new TextBlock { Text = "Character: none selected" };
        _characterHeader.SetResourceReference(StyleProperty, "LabelText");
        infoPanel.Children.Add(_characterHeader);

        _statusText = new TextBlock
        {
            Text = "Open this screen from Character Roster to track play-time updates.",
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _statusText.SetResourceReference(StyleProperty, "SubtitleText");
        infoPanel.Children.Add(_statusText);
        Grid.SetRow(infoPanel, 1);
        root.Children.Add(infoPanel);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        Grid.SetRow(body, 2);

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.95, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var notesTitle = BuildSectionTitle("Notes");
        Grid.SetRow(notesTitle, 0);
        left.Children.Add(notesTitle);

        _notesEditor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 80,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(_notesEditor, 1);
        left.Children.Add(_notesEditor);

        var sessionTitle = BuildSectionTitle("Session Notes");
        Grid.SetRow(sessionTitle, 2);
        left.Children.Add(sessionTitle);

        _sessionNotesEditor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 70,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(_sessionNotesEditor, 3);
        left.Children.Add(_sessionNotesEditor);

        _saveNotesButton = CreateHeaderButton("SAVE NOTES", 120, BtnSaveNotes_Click, isLast: true);
        Grid.SetRow(_saveNotesButton, 4);
        left.Children.Add(_saveNotesButton);

        var quickTitle = BuildSectionTitle("Quick Spell Use");
        quickTitle.Margin = new Thickness(0, 6, 0, 4);
        Grid.SetRow(quickTitle, 5);
        left.Children.Add(quickTitle);

        _quickSpellCombo = new ComboBox { MinWidth = 260, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(_quickSpellCombo, 6);
        left.Children.Add(_quickSpellCombo);

        var quickActionArea = new StackPanel();
        _quickSpellNotes = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        quickActionArea.Children.Add(_quickSpellNotes);

        var quickSpellButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _castSpellButton = CreateHeaderButton("CAST SELECTED", 130, BtnCastSpell_Click);
        _togglePreparedButton = CreateHeaderButton("TOGGLE PREPARED", 145, BtnTogglePrepared_Click);
        _removeCastButton = CreateHeaderButton("REMOVE CAST", 120, BtnRemoveCast_Click, isLast: true);
        quickSpellButtons.Children.Add(_castSpellButton);
        quickSpellButtons.Children.Add(_togglePreparedButton);
        quickSpellButtons.Children.Add(_removeCastButton);
        quickActionArea.Children.Add(quickSpellButtons);

        _spellSummary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _spellSummary.SetResourceReference(StyleProperty, "SubtitleText");
        quickActionArea.Children.Add(_spellSummary);
        Grid.SetRow(quickActionArea, 7);
        left.Children.Add(quickActionArea);

        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.35, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.95, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.9, GridUnitType.Star) });

        var inventoryTitle = BuildSectionTitle("Inventory");
        Grid.SetRow(inventoryTitle, 0);
        right.Children.Add(inventoryTitle);

        _inventoryList = new ListBox { MinHeight = 110, Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(_inventoryList, 1);
        right.Children.Add(_inventoryList);

        var itemInputs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _newItemName = new TextBox { Width = 250, Margin = new Thickness(0, 0, 8, 0) };
        _newItemQuantity = new TextBox { Width = 60, Text = "1" };
        itemInputs.Children.Add(_newItemName);
        itemInputs.Children.Add(_newItemQuantity);
        Grid.SetRow(itemInputs, 2);
        right.Children.Add(itemInputs);

        var inventoryButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        _addItemButton = CreateHeaderButton("ADD ITEM", 100, BtnAddItem_Click);
        _increaseQtyButton = CreateHeaderButton("+ QTY", 85, BtnIncreaseQty_Click);
        _decreaseQtyButton = CreateHeaderButton("- QTY", 85, BtnDecreaseQty_Click);
        _removeItemButton = CreateHeaderButton("REMOVE", 95, BtnRemoveItem_Click, isLast: true, style: "DangerButton");
        inventoryButtons.Children.Add(_addItemButton);
        inventoryButtons.Children.Add(_increaseQtyButton);
        inventoryButtons.Children.Add(_decreaseQtyButton);
        inventoryButtons.Children.Add(_removeItemButton);
        Grid.SetRow(inventoryButtons, 3);
        right.Children.Add(inventoryButtons);

        var componentSection = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        componentSection.Children.Add(BuildSectionTitle("Spell Components"));
        _spellComponentList = new ListBox { MinHeight = 75, MaxHeight = 105, Margin = new Thickness(0, 0, 0, 6) };
        _spellComponentList.SelectionChanged += SpellComponentList_SelectionChanged;
        componentSection.Children.Add(_spellComponentList);
        _componentSummary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        _componentSummary.SetResourceReference(StyleProperty, "SubtitleText");
        componentSection.Children.Add(_componentSummary);
        _syncComponentsButton = CreateHeaderButton("ADD MISSING TO INVENTORY", 205, BtnSyncComponents_Click, isLast: true);
        componentSection.Children.Add(_syncComponentsButton);
        componentSection.Children.Add(BuildSectionTitle("Spells Using Selected Component"));
        _componentUsageSummary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        _componentUsageSummary.SetResourceReference(StyleProperty, "SubtitleText");
        _componentUsageSummary.Text = "Select a component to see spells that use it.";
        componentSection.Children.Add(_componentUsageSummary);
        _componentUsageList = new ListBox { MinHeight = 65, MaxHeight = 130, Margin = new Thickness(0, 0, 0, 6) };
        componentSection.Children.Add(_componentUsageList);
        Grid.SetRow(componentSection, 4);
        right.Children.Add(componentSection);

        var preparedTitle = BuildSectionTitle("Prepared Spells");
        Grid.SetRow(preparedTitle, 5);
        right.Children.Add(preparedTitle);

        _preparedSpellList = new ListBox { MinHeight = 65, Margin = new Thickness(0, 0, 0, 12) };
        Grid.SetRow(_preparedSpellList, 6);
        right.Children.Add(_preparedSpellList);

        var castTitle = BuildSectionTitle("Cast Spells");
        Grid.SetRow(castTitle, 7);
        right.Children.Add(castTitle);

        _castSpellList = new ListBox { MinHeight = 65 };
        Grid.SetRow(_castSpellList, 8);
        right.Children.Add(_castSpellList);

        Grid.SetColumn(right, 2);
        body.Children.Add(right);
        root.Children.Add(body);

        _nwpList = BuildAbilityListBox();
        _rogueSkillsList = BuildAbilityListBox();
        _traitsList = BuildAbilityListBox();
        _classSkillsList = BuildAbilityListBox();
        _racialSkillsList = BuildAbilityListBox();

        _nwpExpander = BuildAbilitySection("Nonweapon Proficiencies", _nwpList, true);
        _rogueSkillsExpander = BuildAbilitySection("Rogue Skills", _rogueSkillsList, false);
        _traitsExpander = BuildAbilitySection("Traits", _traitsList, false);
        _classSkillsExpander = BuildAbilitySection("Class Skills / Abilities", _classSkillsList, false);
        _racialSkillsExpander = BuildAbilitySection("Racial Skills / Abilities", _racialSkillsList, false);

        var abilityPanel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        abilityPanel.Children.Add(_nwpExpander);
        abilityPanel.Children.Add(_rogueSkillsExpander);
        abilityPanel.Children.Add(_traitsExpander);
        abilityPanel.Children.Add(_classSkillsExpander);
        abilityPanel.Children.Add(_racialSkillsExpander);

        _abilityGroupsExpander = new Expander
        {
            Header = "Character Abilities",
            IsExpanded = false,
            Content = abilityPanel
        };
        _abilityGroupsExpander.SetResourceReference(StyleProperty, "PanelHeaderExpander");
        Grid.SetRow(_abilityGroupsExpander, 3);
        root.Children.Add(_abilityGroupsExpander);

        Content = root;
    }

    public UIElement View => this;

    public void OnEnter()
    {
        BindCharacter(_app.PendingInGameCharacterIndex);
    }

    private void BindCharacter(int requestedIndex)
    {
        _selectedCharacter = null;
        int index = requestedIndex >= 0 ? requestedIndex : _pendingCharacterIndex;
        if (index < 0)
            index = _app.PendingInGameCharacterIndex;

        _pendingCharacterIndex = index;
        _app.PendingInGameCharacterIndex = -1;

        if (index >= 0 && index < _app.Characters.Count)
            _selectedCharacter = _app.Characters[index];

        if (_selectedCharacter is null)
        {
            _app.SetBanner("Character Blueprint  ›  In-Game Character");
            _characterHeader.Text = "Character: none selected";
            _statusText.Text = "Select a character in Character Roster and open IN GAME from there.";
            _notesEditor.Text = string.Empty;
            _sessionNotesEditor.Text = string.Empty;
            _inventoryList.ItemsSource = null;
            _preparedSpellList.ItemsSource = null;
            _castSpellList.ItemsSource = null;
            _quickSpellCombo.ItemsSource = null;
            _spellComponentList.ItemsSource = null;
            _componentUsageList.ItemsSource = null;
            _nwpList.ItemsSource = null;
            _rogueSkillsList.ItemsSource = null;
            _traitsList.ItemsSource = null;
            _classSkillsList.ItemsSource = null;
            _racialSkillsList.ItemsSource = null;
            _rogueSkillsExpander.Visibility = Visibility.Collapsed;
            _spellSummary.Text = "Spell tracking unavailable.";
            _componentSummary.Text = "Spell components unavailable.";
            _componentUsageSummary.Text = "Select a component to see spells that use it.";
            SetEnabledState(false);
            return;
        }

        _app.SetBanner($"Character Blueprint  ›  In-Game Character  ›  {_selectedCharacter.Name}");
        EnsureSpellTrackingInitialized(_selectedCharacter);
        RefreshUi();
        SetEnabledState(true);
    }

    private void RefreshUi()
    {
        if (_selectedCharacter is null)
            return;

        _characterHeader.Text = BuildCharacterSummary(_selectedCharacter);
        _notesEditor.Text = string.Join(Environment.NewLine, _selectedCharacter.Notes ?? new List<string>());
        _sessionNotesEditor.Text = _selectedCharacter.SpellTracking?.CurrentDayTracking.SessionNotes ?? string.Empty;
        _inventoryList.ItemsSource = (_selectedCharacter.EquipmentSelections ?? new List<EquipmentSelection>())
            .OrderBy(selection => selection.ItemName, StringComparer.OrdinalIgnoreCase)
            .Select(selection => new InventoryRow
            {
                Selection = selection,
                DisplayName = $"{Math.Max(1, selection.Quantity)}x {(string.IsNullOrWhiteSpace(selection.ItemName) ? selection.ItemId : selection.ItemName)}"
            })
            .ToList();

        var quickSpells = BuildQuickSpellRows(_selectedCharacter);
        _quickSpellCombo.ItemsSource = quickSpells;
        _quickSpellCombo.SelectedItem = quickSpells.FirstOrDefault();
        bool hasQuickSpells = quickSpells.Count > 0;
        _quickSpellCombo.IsEnabled = hasQuickSpells;
        _quickSpellNotes.IsEnabled = hasQuickSpells;
        _castSpellButton.IsEnabled = hasQuickSpells;

        var componentRows = BuildSpellComponentRows(_selectedCharacter);
        _spellComponentList.ItemsSource = componentRows;
        int missing = componentRows.Count(row => !row.InInventory);
        _componentSummary.Text = $"Tracked components: {componentRows.Count}. Missing in inventory: {missing}.";
        _componentUsageList.ItemsSource = null;
        _componentUsageSummary.Text = "Select a component to see spells that use it.";

        var prepared = _selectedCharacter.SpellTracking?.CurrentDayTracking.PreparedSpells ?? new List<PreparedSpellRecord>();
        _preparedSpellList.ItemsSource = prepared
            .Select((spell, index) => new PreparedSpellRow
            {
                Index = index,
                DisplayName = $"L{spell.SpellLevel} {ResolveSpellName(spell.SpellId, spell.SpellName)}{(spell.IsCast ? " [CAST]" : string.Empty)}"
            })
            .ToList();

        var casts = _selectedCharacter.SpellTracking?.CurrentDayTracking.CastSpells ?? new List<CastSpellRecord>();
        _castSpellList.ItemsSource = casts
            .Select((spell, index) => new CastSpellRow
            {
                Index = index,
                DisplayName = $"{spell.CastTime:HH:mm}  L{spell.SpellLevel} {ResolveSpellName(spell.SpellId, spell.SpellName)}{(string.IsNullOrWhiteSpace(spell.Notes) ? string.Empty : $"  -  {spell.Notes}")}"
            })
            .ToList();

        string spellSummary = BuildSpellSummary(_selectedCharacter, quickSpells.Count);
        if (!hasQuickSpells)
            spellSummary += " No memorized/prepared spells remaining.";
        _spellSummary.Text = spellSummary;
        RefreshAbilityGroups(_selectedCharacter);
    }

    private void EnsureSpellTrackingInitialized(CharacterSheet character)
    {
        character.Notes ??= new List<string>();
        character.EquipmentSelections ??= new List<EquipmentSelection>();
        character.SpellTracking ??= new CharacterSpellTracking
        {
            CharacterId = character.Name,
            CharacterName = character.Name,
            CurrentDayTracking = new DailySpellTracking
            {
                Year = _app.Campaign.Calendar.CurrentYear,
                Era = _app.Campaign.Calendar.CurrentEra,
                Month = _app.Campaign.Calendar.CurrentMonth,
                Day = _app.Campaign.Calendar.CurrentDay,
            }
        };
        character.SpellTracking.CurrentDayTracking ??= new DailySpellTracking();
        character.SpellTracking.CurrentDayTracking.PreparedSpells ??= new List<PreparedSpellRecord>();
        character.SpellTracking.CurrentDayTracking.CastSpells ??= new List<CastSpellRecord>();
    }

    private static string BuildCharacterSummary(CharacterSheet character)
    {
        string race = string.IsNullOrWhiteSpace(character.RaceName) ? character.RaceId : character.RaceName;
        string cls = string.IsNullOrWhiteSpace(character.ClassName) ? character.ClassId : character.ClassName;
        return $"{character.Name}  |  Race: {race}  |  Class: {cls}  |  Level: {Math.Max(1, character.Level)}  |  HP: {character.CurrentHitPoints}/{character.HitPoints}  |  AC: {character.ArmorClass}  |  THAC0: {character.Thac0}";
    }

    private void RefreshAbilityGroups(CharacterSheet character)
    {
        var nwp = ResolveNonweaponProficiencies(character);
        var rogue = ResolveRogueSkills(character);
        var traits = ResolveTraitNames(character);
        var classSkills = ResolveClassSkills(character);
        var racialSkills = ResolveRacialSkills(character);

        SetAbilityItems(_nwpList, nwp, "No nonweapon proficiencies found.");
        SetAbilityItems(_rogueSkillsList, rogue, "No rogue skills tracked.");
        SetAbilityItems(_traitsList, traits, "No traits selected.");
        SetAbilityItems(_classSkillsList, classSkills, "No class skills/abilities selected.");
        SetAbilityItems(_racialSkillsList, racialSkills, "No racial skills/abilities selected.");

        _rogueSkillsExpander.Visibility = rogue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetAbilityItems(ListBox listBox, List<string> items, string emptyText)
    {
        listBox.ItemsSource = items.Count == 0 ? new List<string> { emptyText } : items;
    }

    private List<string> ResolveNonweaponProficiencies(CharacterSheet character)
    {
        if (character.NonweaponProficiencies.Count > 0)
        {
            return character.NonweaponProficiencies
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (character.NonweaponProficiencyIds.Count == 0)
            return new List<string>();

        var nwpById = _app.CharacterOptions.GetCatalog().NonweaponProficiencies
            .ToDictionary(x => x.Id, x => x.Name, StringComparer.OrdinalIgnoreCase);

        return character.NonweaponProficiencyIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => nwpById.TryGetValue(id, out string? name) ? name : HumanizeToken(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> ResolveRogueSkills(CharacterSheet character)
    {
        bool isPlayersOptionMode = string.Equals(
            CharGenState.NormalizeCharacterModeToken(character.CharacterMode),
            "players_option",
            StringComparison.OrdinalIgnoreCase);

        var selectedSkillIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (isPlayersOptionMode)
        {
            var selectedAbilityEntries = character.SelectedClassAbilityIds
                .Concat(character.SelectedAbilitiesByClass.Values.SelectMany(entries => entries))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var skillId in RulesEngine.GetRogueSkillIdsForClassProfile(character.ClassId, selectedAbilityEntries))
                selectedSkillIds.Add(skillId);
        }
        else
        {
            IEnumerable<string> classTokens = character.ClassIds is { Count: > 0 }
                ? character.ClassIds
                : (character.ClassId ?? string.Empty)
                    .Split(new[] { '/', '+', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (string rawToken in classTokens)
            {
                string token = (rawToken ?? string.Empty).Trim().ToLowerInvariant();
                foreach (var skillId in RulesEngine.GetStandardCoreRogueSkillIdsForClassProfile(token))
                    selectedSkillIds.Add(skillId);
            }
        }

        if (selectedSkillIds.Count == 0)
            return new List<string>();

        int dexterity = character.Abilities.TryGetValue("dex", out var dex) ? dex : 10;
        var breakdown = RulesEngine.BuildRogueSkillBreakdown(
            selectedSkillIds,
            character.RaceId,
            dexterity,
            character.RogueSkillArmorProfile,
            character.SelectedRogueSkillPoints,
            Math.Max(1, character.Level),
            isPlayersOptionMode && RulesEngine.UsesAutomaticRogueSkillProgression(character.ClassId));

        return breakdown
            .OrderBy(kv => kv.SkillName, StringComparer.OrdinalIgnoreCase)
            .Select(FormatRogueSkillBreakdownLine)
            .ToList();
    }

    private static string FormatRogueSkillBreakdownLine(RogueSkillBreakdown entry)
    {
        string racePart = entry.RacialAdjustment == 0 ? "Race 0%" : $"Race {(entry.RacialAdjustment > 0 ? "+" : string.Empty)}{entry.RacialAdjustment}%";
        string dexPart = entry.DexterityAdjustment == 0 ? "Dex 0%" : $"Dex {(entry.DexterityAdjustment > 0 ? "+" : string.Empty)}{entry.DexterityAdjustment}%";
        string armorPart = entry.ArmorAdjustment == 0 ? "Armor 0%" : $"Armor {(entry.ArmorAdjustment > 0 ? "+" : string.Empty)}{entry.ArmorAdjustment}%";
        string addedPart = entry.AllocatedPoints == 0 ? "Added 0%" : $"Added +{entry.AllocatedPoints}%";
        return $"{entry.SkillName}: Base {entry.BaseScore}% | {racePart} | {dexPart} | {armorPart} | {addedPart} | Total {entry.FinalScore}%";
    }

    private List<string> ResolveTraitNames(CharacterSheet character)
    {
        if (character.Traits.Count > 0)
        {
            return character.Traits
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (character.SelectedTraitIds.Count == 0)
            return new List<string>();

        var traitById = _app.CharacterOptions.GetCatalog().Traits
            .ToDictionary(x => x.Id, x => x.Name, StringComparer.OrdinalIgnoreCase);

        return character.SelectedTraitIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => traitById.TryGetValue(id, out string? name) ? name : HumanizeToken(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> ResolveClassSkills(CharacterSheet character)
    {
        var abilityEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in character.SelectedClassAbilityIds.Where(x => !string.IsNullOrWhiteSpace(x)))
            abilityEntries.Add(entry);

        foreach (var (_, entries) in character.SelectedAbilitiesByClass)
        {
            foreach (string entry in entries.Where(x => !string.IsNullOrWhiteSpace(x)))
                abilityEntries.Add(entry);
        }

        if (abilityEntries.Count == 0)
        {
            return character.StructuredAbilities
                .Where(a => !string.IsNullOrWhiteSpace(a.Category)
                    && a.Category.Contains("class", StringComparison.OrdinalIgnoreCase))
                .Select(a => string.IsNullOrWhiteSpace(a.Description) ? HumanizeToken(a.Id) : a.Description)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var resolved = new List<string>();
        foreach (string entry in abilityEntries)
        {
            string baseId = RulesEngine.ExtractClassAbilityBaseId(entry);
            string playerText = RulesEngine.ExtractClassAbilityPlayerText(entry);

            string label;
            if (_app.Rules.ClassAbilityLibrary.TryGetValue(baseId, out var def))
                label = string.IsNullOrWhiteSpace(def.Description) ? HumanizeToken(baseId) : def.Description;
            else
                label = HumanizeToken(baseId);

            if (!string.IsNullOrWhiteSpace(playerText))
                label = $"{label} ({playerText})";

            resolved.Add(label);
        }

        return resolved
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> ResolveRacialSkills(CharacterSheet character)
    {
        var resolved = new List<string>();

        if (character.RacialAbilities.Count > 0)
        {
            resolved.AddRange(character.RacialAbilities
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()));
        }

        foreach (string id in character.SelectedRacialAbilityIds.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var structured = character.StructuredAbilities
                .FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            resolved.Add(structured is null || string.IsNullOrWhiteSpace(structured.Description)
                ? HumanizeToken(id)
                : structured.Description);
        }

        if (resolved.Count == 0)
        {
            resolved.AddRange(character.StructuredAbilities
                .Where(a => !string.IsNullOrWhiteSpace(a.Category)
                    && (a.Category.Contains("racial", StringComparison.OrdinalIgnoreCase)
                        || a.Category.Contains("race", StringComparison.OrdinalIgnoreCase)))
                .Select(a => string.IsNullOrWhiteSpace(a.Description) ? HumanizeToken(a.Id) : a.Description));
        }

        return resolved
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string HumanizeToken(string value)
    {
        string cleaned = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;

        cleaned = cleaned.Replace('_', ' ').Replace('-', ' ');
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    private List<QuickSpellRow> BuildQuickSpellRows(CharacterSheet character)
    {
        var rows = new Dictionary<string, QuickSpellRow>(StringComparer.OrdinalIgnoreCase);

        void AddQuickRow(string? spellId, string source)
        {
            if (string.IsNullOrWhiteSpace(spellId))
                return;

            string id = spellId.Trim();
            var spell = _app.Rules.Spells.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
            if (spell is null)
                return;

            if (rows.TryGetValue(id, out var existing))
            {
                if (!existing.Source.Contains(source, StringComparison.OrdinalIgnoreCase))
                    existing.Source = $"{existing.Source}, {source}";
                return;
            }

            rows[id] = new QuickSpellRow
            {
                Id = id,
                Name = spell.Name,
                Level = ParseSpellLevel(spell.Level),
                Source = source
            };
        }

        foreach (var prepared in character.SpellTracking?.CurrentDayTracking.PreparedSpells ?? new List<PreparedSpellRecord>())
        {
            if (prepared.IsCast)
                continue;

            AddQuickRow(prepared.SpellId, "Prepared");
        }

        return rows.Values
            .OrderBy(row => row.Level)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<SpellComponentRow> BuildSpellComponentRows(CharacterSheet character)
    {
        var spellDefinitions = GetSpellDefinitionsForComponents(character);
        var components = new Dictionary<string, (string Type, HashSet<string> SpellIds)>(StringComparer.OrdinalIgnoreCase);
        var spellById = spellDefinitions
            .Where(spell => !string.IsNullOrWhiteSpace(spell.Id))
            .ToDictionary(spell => spell.Id, spell => spell, StringComparer.OrdinalIgnoreCase);

        foreach (var spell in spellDefinitions)
        {
            foreach (var marker in ExtractComponentMarkers(spell.Components))
            {
                if (!components.TryGetValue(marker, out var entry))
                    entry = ("Casting", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                entry.SpellIds.Add(spell.Id);
                components[marker] = entry;
            }

            foreach (var material in ExtractMaterialComponents(spell.Materials))
            {
                if (!components.TryGetValue(material, out var entry))
                    entry = ("Material", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                entry.SpellIds.Add(spell.Id);
                components[material] = entry;
            }
        }

        var inventoryNames = (character.EquipmentSelections ?? new List<EquipmentSelection>())
            .Select(selection => NormalizeComponentName(selection.ItemName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return components
            .Select(kv => new SpellComponentRow
            {
                Name = kv.Key,
                SourceType = kv.Value.Type,
                SpellCount = kv.Value.SpellIds.Count,
                InInventory = kv.Value.Type == "Casting" || inventoryNames.Contains(NormalizeComponentName(kv.Key)),
                SpellIds = kv.Value.SpellIds
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                SpellPreview = BuildSpellPreview(kv.Value.SpellIds, spellById)
            })
            .OrderBy(row => row.InInventory)
            .ThenByDescending(row => row.SpellCount)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string BuildSpellPreview(IEnumerable<string> spellIds, Dictionary<string, SpellDefinition> spellById)
    {
        var preview = spellIds
            .Select(id => spellById.TryGetValue(id, out var spell) ? spell : (SpellDefinition?)null)
            .Where(spell => spell is not null)
            .Cast<SpellDefinition>()
            .Select(spell => new
            {
                Level = ParseSpellLevel(spell.Level),
                Name = string.IsNullOrWhiteSpace(spell.Name) ? spell.Id : spell.Name.Trim(),
                Type = IsDivineSpell(spell) ? "D" : "A"
            })
            .OrderBy(entry => entry.Level)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (preview.Count == 0)
            return string.Empty;

        var shown = preview
            .Take(3)
            .Select(entry => $"L{entry.Level} {entry.Name} [{entry.Type}]")
            .ToList();

        if (preview.Count > 3)
            shown.Add($"+{preview.Count - 3} more");

        return string.Join("; ", shown);
    }

    private void SpellComponentList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_selectedCharacter is null)
            return;

        if (_spellComponentList.SelectedItem is not SpellComponentRow selected)
        {
            _componentUsageList.ItemsSource = null;
            _componentUsageSummary.Text = "Select a component to see spells that use it.";
            return;
        }

        var lines = selected.SpellIds
            .Select(id => _app.Rules.Spells.FirstOrDefault(spell => string.Equals(spell.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(spell => spell is not null)
            .Cast<SpellDefinition>()
            .Select(spell => new
            {
                Level = ParseSpellLevel(spell.Level),
                Name = string.IsNullOrWhiteSpace(spell.Name) ? spell.Id : spell.Name.Trim(),
                Type = IsDivineSpell(spell) ? "Divine" : "Arcane"
            })
            .OrderBy(entry => entry.Level)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"L{entry.Level} {entry.Name} [{entry.Type}]")
            .ToList();

        _componentUsageList.ItemsSource = lines;
        _componentUsageSummary.Text = $"{selected.Name} is used by {lines.Count} spell{(lines.Count == 1 ? string.Empty : "s")}.";
    }

    private List<SpellDefinition> GetSpellDefinitionsForComponents(CharacterSheet character)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string id in character.TrackedSpellIds ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id.Trim());
        }

        foreach (string id in character.WizardSpellbookIds ?? new List<string>())
        {
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id.Trim());
        }

        foreach (var book in character.WizardSpellbooks ?? new List<WizardSpellbook>())
        {
            if (book.SpellPages is null)
                continue;

            foreach (string id in book.SpellPages.Keys)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id.Trim());
            }
        }

        foreach (var list in character.WizardSpellLists ?? new List<NamedSpellList>())
        {
            foreach (string id in list.SpellIds ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id.Trim());
            }
        }

        foreach (var prepared in character.SpellTracking?.CurrentDayTracking.PreparedSpells ?? new List<PreparedSpellRecord>())
        {
            if (!string.IsNullOrWhiteSpace(prepared.SpellId))
                ids.Add(prepared.SpellId.Trim());
        }

        foreach (var divineSpell in GetBypassAccessibleDivineSpells(character))
            ids.Add(divineSpell.Id);

        return ids
            .Select(id => _app.Rules.Spells.FirstOrDefault(spell => string.Equals(spell.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(spell => spell is not null)
            .Cast<SpellDefinition>()
            .ToList();
    }

    private List<SpellDefinition> GetBypassAccessibleDivineSpells(CharacterSheet character)
    {
        if (!IsDivineCaster(character))
            return new List<SpellDefinition>();

        if (HasPriestSpellbookRestriction(character))
        {
            return (character.WizardSpellbookIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => _app.Rules.Spells.FirstOrDefault(spell => string.Equals(spell.Id, id, StringComparison.OrdinalIgnoreCase)))
                .Where(spell => spell is not null && IsDivineSpell(spell))
                .Cast<SpellDefinition>()
                .ToList();
        }

        if (!character.PriestMemorizationBypass)
            return new List<SpellDefinition>();

        int maxDivineLevel = GetHighestSpellLevel(character.DivineSpellSlots);
        if (maxDivineLevel <= 0)
            return new List<SpellDefinition>();

        var sphereAccess = GatherCharacterSphereAccess(character);
        return _app.Rules.Spells
            .Where(IsDivineSpell)
            .Where(spell => ParseSpellLevel(spell.Level) <= maxDivineLevel)
            .Where(spell => IsDivineSpellAllowedBySphereAccess(spell, sphereAccess))
            .GroupBy(spell => spell.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private string BuildSpellSummary(CharacterSheet character, int quickSpellCount)
    {
        var day = character.SpellTracking?.CurrentDayTracking;
        if (day is null)
            return "Spell tracking unavailable.";

        int preparedCount = day.PreparedSpells.Count;
        int preparedCastCount = day.PreparedSpells.Count(spell => spell.IsCast);
        int castCount = day.CastSpells.Count;
        string bypassText = character.PriestMemorizationBypass
            ? "Priest bypass is ON."
            : "Priest bypass is OFF.";

        return $"Quick list: {quickSpellCount}. Prepared: {preparedCount} total, {preparedCastCount} marked cast. Cast log entries: {castCount}. {bypassText}";
    }

    private string ResolveSpellName(string spellId, string fallbackName)
    {
        if (!string.IsNullOrWhiteSpace(fallbackName))
            return fallbackName;

        var spell = _app.Rules.Spells.FirstOrDefault(candidate => string.Equals(candidate.Id, spellId, StringComparison.OrdinalIgnoreCase));
        return spell?.Name ?? spellId;
    }

    private static int ParseSpellLevel(string? levelText)
    {
        if (string.IsNullOrWhiteSpace(levelText))
            return 1;
        if (levelText.Equals("Cantrip", StringComparison.OrdinalIgnoreCase))
            return 0;
        string digits = Regex.Replace(levelText, @"[^\d]", string.Empty);
        return int.TryParse(digits, out int value) ? Math.Max(0, value) : 1;
    }

    private static int GetHighestSpellLevel(Dictionary<int, int> slots)
    {
        if (slots is null || slots.Count == 0)
            return 0;

        int maxLevel = 0;
        foreach (var (level, count) in slots)
        {
            if (level > 0 && count > 0)
                maxLevel = Math.Max(maxLevel, level);
        }
        return maxLevel;
    }

    private static bool IsDivineSpellAllowedBySphereAccess(SpellDefinition spell, Dictionary<string, string> sphereAccess)
    {
        if (sphereAccess.Count == 0 || string.IsNullOrWhiteSpace(spell.Schools))
            return true;

        int spellLevel = ParseSpellLevel(spell.Level);
        string requiredAccess = spellLevel <= 3 ? "minor" : "major";
        return ParseSpellSchoolTokens(spell.Schools)
            .Any(sphere => RulesEngine.PriestHasSphereAccess(sphere, sphereAccess, requiredAccess));
    }

    private static HashSet<string> ParseSpellSchoolTokens(string schoolsText)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(schoolsText))
            return tokens;

        foreach (string raw in Regex.Split(schoolsText, @"\s*(,|;|/|\||&|\band\b)\s*", RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(raw)
                || raw == ","
                || raw == ";"
                || raw == "/"
                || raw == "|"
                || raw == "&"
                || string.Equals(raw, "and", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string normalized = raw.Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
                tokens.Add(normalized);
        }

        return tokens;
    }

    private static IEnumerable<string> ExtractComponentMarkers(string? componentsText)
    {
        if (string.IsNullOrWhiteSpace(componentsText))
            yield break;

        string upper = componentsText.ToUpperInvariant();
        if (ContainsToken(upper, "V"))
            yield return "Verbal (V)";
        if (ContainsToken(upper, "S"))
            yield return "Somatic (S)";
        if (ContainsToken(upper, "M"))
            yield return "Material (M)";
        if (ContainsToken(upper, "F"))
            yield return "Focus (F)";
        if (ContainsToken(upper, "DF") || ContainsToken(upper, "D"))
            yield return "Divine Focus (DF)";
    }

    private static IEnumerable<string> ExtractMaterialComponents(string? materialsText)
    {
        if (string.IsNullOrWhiteSpace(materialsText))
            yield break;

        string cleaned = Regex.Replace(materialsText, @"\([^)]*\)", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\b(material component(?:s)?|requires?|needed|must have|must be|consumes?)\b", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bworth\s+\d+\s*(gp|sp|cp|pp)\b", string.Empty, RegexOptions.IgnoreCase);
        cleaned = cleaned.Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            yield break;

        foreach (string part in Regex.Split(cleaned, @"\s*(,|;|\bor\b|\band\b|\bplus\b|/)\s*", RegexOptions.IgnoreCase))
        {
            string token = (part ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(token)
                || token == ","
                || token == ";"
                || token == "/"
                || string.Equals(token, "or", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "and", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "plus", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? normalized = CleanMaterialToken(token);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                AuditIgnoredComponentToken("material-token", token);
                continue;
            }

            yield return normalized;
        }
    }

    private static bool ContainsToken(string haystack, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var parts = (haystack ?? string.Empty)
            .Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Any(p => string.Equals(p, token, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeComponentName(string? value)
    {
        string normalized = Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"\s+", " ");
        normalized = Regex.Replace(normalized, @"[^a-z0-9\s'-]", string.Empty).Trim();
        normalized = Regex.Replace(normalized, @"^(a|an|the)\s+", string.Empty, RegexOptions.IgnoreCase).Trim();

        if (Regex.IsMatch(normalized, @"\bholy symbol\b", RegexOptions.IgnoreCase))
            return "holy symbol";
        if (Regex.IsMatch(normalized, @"\b(prayer beads?|rosary)\b", RegexOptions.IgnoreCase))
            return "prayer beads";
        if (Regex.IsMatch(normalized, @"\bmistletoe\b", RegexOptions.IgnoreCase))
            return "mistletoe";
        if (Regex.IsMatch(normalized, @"\bpowdered silver\b|\bsilver powder\b", RegexOptions.IgnoreCase))
            return "powdered silver";
        if (Regex.IsMatch(normalized, @"\bdiamond dust\b", RegexOptions.IgnoreCase))
            return "diamond dust";

        return normalized;
    }

    private static string? CleanMaterialToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        string normalized = token.Trim(' ', '.', ':', '-');
        normalized = Regex.Replace(normalized, @"\b(the caster|caster|priest|wizard|mage)('s)?\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\bthat is consumed\b|\bwhich is consumed\b|\bif available\b", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        if (IsNoiseMaterialToken(normalized))
        {
            AuditIgnoredComponentToken("noise-raw", token);
            return null;
        }

        string compact = NormalizeComponentName(normalized);
        if (IsNoiseMaterialToken(compact))
        {
            AuditIgnoredComponentToken("noise-normalized", compact);
            return null;
        }

        return ToDisplayComponentName(compact);
    }

    private static void AuditIgnoredComponentToken(string reason, string token)
    {
        if (!ComponentParsingAuditEnabled)
            return;

        string trimmed = (token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        Debug.WriteLine($"[ComponentAudit] Ignored {reason}: {trimmed}");
    }

    private static bool IsNoiseMaterialToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return true;

        string lower = token.Trim().ToLowerInvariant();
        return lower == "none"
            || lower == "n/a"
            || lower == "na"
            || lower == "see text"
            || lower == "special"
            || lower == "varies"
            || lower == "various"
            || lower == "optional"
            || lower == "material"
            || lower == "component"
            || lower == "material component"
            || Regex.IsMatch(lower, @"^see\s")
            || Regex.IsMatch(lower, @"^as\s+")
            || Regex.IsMatch(lower, @"^at\s+least\s+\d+")
            || Regex.IsMatch(lower, @"^worth\s+\d+")
            || Regex.IsMatch(lower, @"^up\s+to\s+\d+");
    }

    private static string ToDisplayComponentName(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        return string.Join(" ", normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Length <= 2 ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        _app.GoTo("characters", -1);
    }

    private void BtnCharacterSheet_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingCharacterIndex >= 0)
            _app.OpenCharacterSheets(_pendingCharacterIndex);
    }

    private void BtnOpenSpellTracker_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingCharacterIndex >= 0)
            _app.OpenSpellTracker(_pendingCharacterIndex, "in_game_character");
    }

    private void BtnSaveNotes_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
            return;

        _selectedCharacter.Notes = (_notesEditor.Text ?? string.Empty)
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        _selectedCharacter.SpellTracking!.CurrentDayTracking.SessionNotes = (_sessionNotesEditor.Text ?? string.Empty).Trim();
        SaveCharacterChanges("Saved in-game notes.");
        RefreshUi();
    }

    private void BtnAddItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
            return;

        string name = (_newItemName.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _statusText.Text = "Enter an item name before adding it to inventory.";
            return;
        }

        int quantity = ParsePositiveInt(_newItemQuantity.Text, 1);
        _selectedCharacter.EquipmentSelections.Add(new EquipmentSelection
        {
            ItemId = BuildManualItemId(name),
            ItemName = name,
            Category = "In-Game",
            CostText = "Found in play",
            Quantity = quantity,
        });
        _newItemName.Text = string.Empty;
        _newItemQuantity.Text = "1";
        SaveCharacterChanges($"Added {quantity}x {name} to inventory.");
        RefreshUi();
    }

    private void BtnIncreaseQty_Click(object sender, RoutedEventArgs e)
    {
        if (_inventoryList.SelectedItem is not InventoryRow selected)
        {
            _statusText.Text = "Select an inventory item first.";
            return;
        }

        selected.Selection.Quantity = Math.Max(1, selected.Selection.Quantity + 1);
        SaveCharacterChanges($"Adjusted quantity for {selected.Selection.ItemName}.");
        RefreshUi();
    }

    private void BtnDecreaseQty_Click(object sender, RoutedEventArgs e)
    {
        if (_inventoryList.SelectedItem is not InventoryRow selected)
        {
            _statusText.Text = "Select an inventory item first.";
            return;
        }

        selected.Selection.Quantity = Math.Max(1, selected.Selection.Quantity - 1);
        SaveCharacterChanges($"Adjusted quantity for {selected.Selection.ItemName}.");
        RefreshUi();
    }

    private void BtnRemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null || _inventoryList.SelectedItem is not InventoryRow selected)
        {
            _statusText.Text = "Select an inventory item first.";
            return;
        }

        _selectedCharacter.EquipmentSelections.Remove(selected.Selection);
        SaveCharacterChanges($"Removed {selected.Selection.ItemName} from inventory.");
        RefreshUi();
    }

    private void BtnSyncComponents_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
            return;

        var missingMaterialRows = BuildSpellComponentRows(_selectedCharacter)
            .Where(row => !row.InInventory && string.Equals(row.SourceType, "Material", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (missingMaterialRows.Count == 0)
        {
            _statusText.Text = "No missing material components to add.";
            return;
        }

        var inventory = _selectedCharacter.EquipmentSelections ??= new List<EquipmentSelection>();
        var normalizedInventory = inventory
            .Select(i => NormalizeComponentName(i.ItemName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (var row in missingMaterialRows)
        {
            string normalized = NormalizeComponentName(row.Name);
            if (normalizedInventory.Contains(normalized))
                continue;

            inventory.Add(new EquipmentSelection
            {
                ItemId = BuildManualItemId(row.Name),
                ItemName = row.Name,
                Category = "Spell Components",
                CostText = "Auto-added from spell component requirements",
                Quantity = 1,
            });
            normalizedInventory.Add(normalized);
            added++;
        }

        if (added == 0)
        {
            _statusText.Text = "All material components are already represented in inventory.";
            return;
        }

        SaveCharacterChanges($"Added {added} missing spell component{(added == 1 ? string.Empty : "s")} to inventory.");
        RefreshUi();
    }

    private void BtnCastSpell_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter?.SpellTracking is null)
            return;

        if (_quickSpellCombo.SelectedItem is not QuickSpellRow selected)
        {
            _statusText.Text = "Select a spell first.";
            return;
        }

        if (!TryValidateSpellCast(_selectedCharacter, selected, out string failure, out int preparedIndex))
        {
            _statusText.Text = failure;
            return;
        }

        if (preparedIndex >= 0)
            _selectedCharacter.SpellTracking.CastPreparedSpell(preparedIndex);

        _selectedCharacter.SpellTracking.CastSpell(selected.Id, selected.Name, selected.Level, (_quickSpellNotes.Text ?? string.Empty).Trim());
        _quickSpellNotes.Text = string.Empty;
        SaveCharacterChanges($"Recorded cast spell: {selected.Name}.");
        RefreshUi();
    }

    private bool TryValidateSpellCast(CharacterSheet character, QuickSpellRow selected, out string failure, out int preparedIndex)
    {
        failure = string.Empty;
        preparedIndex = -1;

        var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
        bool isDivine = spell is not null && IsDivineSpell(spell);
        bool bypassAllowed = isDivine
            && character.PriestMemorizationBypass
            && selected.Source.Contains("Bypass", StringComparison.OrdinalIgnoreCase);

        preparedIndex = FindPreparedSpellIndex(character, selected.Id, selected.Level);
        if (preparedIndex < 0 && !bypassAllowed)
        {
            failure = $"{selected.Name} is not memorized/prepared and cannot be cast.";
            return false;
        }

        if (selected.Level == 0)
        {
            int cantripCap = GetCantripCapacity(character, isDivine);
            int cantripUsed = CountTodayCantripCasts(character, isDivine);
            if (cantripUsed >= cantripCap)
            {
                failure = $"{(isDivine ? "Divine" : "Arcane")} cantrip capacity reached ({cantripUsed}/{cantripCap}).";
                return false;
            }
        }
        else
        {
            var maxSlots = GetEffectiveSlots(character, isDivine);
            int allowed = Math.Max(0, maxSlots.GetValueOrDefault(selected.Level));
            if (allowed <= 0)
            {
                failure = $"Cannot cast level {selected.Level} {(isDivine ? "divine" : "arcane")} spells with current level/abilities.";
                return false;
            }

            int used = CountTodayCastsForLevel(character, isDivine, selected.Level);
            if (used >= allowed)
            {
                failure = $"No level {selected.Level} {(isDivine ? "divine" : "arcane")} casts remaining ({used}/{allowed}).";
                return false;
            }
        }

        if (!CanCastWithCurrentMaterials(character, selected.Id, out failure))
            return false;

        return true;
    }

    private static int FindPreparedSpellIndex(CharacterSheet character, string spellId, int spellLevel)
    {
        var prepared = character.SpellTracking?.CurrentDayTracking.PreparedSpells;
        if (prepared is null)
            return -1;

        for (int i = 0; i < prepared.Count; i++)
        {
            var record = prepared[i];
            if (record.IsCast)
                continue;

            if (record.SpellLevel == spellLevel
                && string.Equals(record.SpellId, spellId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private bool CanCastWithCurrentMaterials(CharacterSheet character, string spellId, out string failureMessage)
    {
        failureMessage = string.Empty;
        if (!character.UseSpellComponents)
            return true;

        var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, spellId, StringComparison.OrdinalIgnoreCase));
        if (spell is null)
            return true;

        bool hasMaterialMarker = ContainsToken(spell.Components ?? string.Empty, "M");
        bool hasMaterialText = !string.IsNullOrWhiteSpace(spell.Materials);
        if (!hasMaterialMarker && !hasMaterialText)
            return true;

        var required = ExtractMaterialComponents(spell.Materials).ToList();
        if (required.Count == 0)
            return true;

        var inventoryNames = (character.EquipmentSelections ?? new List<EquipmentSelection>())
            .Select(selection => NormalizeComponentName(selection.ItemName))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = required
            .Where(component => !inventoryNames.Contains(NormalizeComponentName(component)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missing.Count == 0)
            return true;

        string shown = string.Join(", ", missing.Take(4));
        if (missing.Count > 4)
            shown += ", ...";

        failureMessage = $"Cannot cast {spell.Name}: missing material component{(missing.Count == 1 ? string.Empty : "s")} ({shown}).";
        return false;
    }

    private int CountTodayCastsForLevel(CharacterSheet character, bool isDivine, int level)
    {
        if (character.SpellTracking is null)
            return 0;

        int total = 0;
        foreach (var cast in character.SpellTracking.CurrentDayTracking.CastSpells)
        {
            if (cast.SpellLevel != level)
                continue;

            var castSpell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, cast.SpellId, StringComparison.OrdinalIgnoreCase));
            bool castIsDivine = castSpell is not null && IsDivineSpell(castSpell);
            if (castIsDivine == isDivine)
                total++;
        }

        return total;
    }

    private int CountTodayCantripCasts(CharacterSheet character, bool isDivine)
    {
        if (character.SpellTracking is null)
            return 0;

        int total = 0;
        foreach (var cast in character.SpellTracking.CurrentDayTracking.CastSpells)
        {
            if (cast.SpellLevel != 0)
                continue;

            var castSpell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, cast.SpellId, StringComparison.OrdinalIgnoreCase));
            bool castIsDivine = castSpell is not null && IsDivineSpell(castSpell);
            if (castIsDivine == isDivine)
                total++;
        }

        return total;
    }

    private static int GetCantripCapacity(CharacterSheet character, bool isDivine)
    {
        var slots = GetEffectiveSlots(character, isDivine);
        int levelOneSlots = Math.Max(0, slots.GetValueOrDefault(1));
        return levelOneSlots * 4;
    }

    private static Dictionary<int, int> GetEffectiveSlots(CharacterSheet character, bool isDivine)
    {
        var slots = new Dictionary<int, int>();
        var source = isDivine ? character.DivineSpellSlots : character.ArcaneSpellSlots;
        bool hasReducedDivineProgression = isDivine
            && (HasClassAbility(character, "cleric_restriction_reduced_spell_progression")
                || HasClassAbility(character, "druid_restriction_reduced_spell_progression"));
        bool hasReducedArcaneProgression = !isDivine
            && HasClassAbility(character, "wizard_restriction_reduced_spell_progression");

        foreach (var kv in source)
        {
            if (kv.Key < 1)
                continue;

            int baseSlots = kv.Value;
            if (hasReducedDivineProgression)
                baseSlots = Math.Max(0, baseSlots - 1);
            if (hasReducedArcaneProgression)
                baseSlots = Math.Max(0, baseSlots - 1);

            if (baseSlots > 0)
                slots[kv.Key] = baseSlots;
        }

        if (isDivine && IsDivineCaster(character))
        {
            if (IsFullPriestCaster(character) && !slots.ContainsKey(1))
                slots[1] = 1;

            int wisdom = GetWisdomScore(character);
            var wisdomBonusSlots = GetWisdomBonusSpellSlots(wisdom);
            foreach (var kv in wisdomBonusSlots)
            {
                int spellLevel = kv.Key;
                int bonusSlots = kv.Value;
                if (bonusSlots <= 0)
                    continue;

                bool levelIsAvailable = slots.ContainsKey(spellLevel)
                    || (spellLevel == 1
                        && IsFullPriestCaster(character)
                        && !hasReducedDivineProgression);
                if (!levelIsAvailable)
                    continue;

                slots[spellLevel] = Math.Max(0, slots.GetValueOrDefault(spellLevel)) + bonusSlots;
            }
        }

        return slots;
    }

    private static bool IsFullPriestCaster(CharacterSheet character)
    {
        string combined = BuildClassText(character);
        return combined.Contains("priest", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("cleric", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("druid", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetWisdomScore(CharacterSheet character)
    {
        if (character.Abilities.TryGetValue("wis", out int wis))
            return wis;
        return 10;
    }

    private static Dictionary<int, int> GetWisdomBonusSpellSlots(int wisdom)
    {
        int wis = Math.Clamp(wisdom, 0, 18);

        if (wis >= 18)
            return new Dictionary<int, int> { [1] = 2, [2] = 2, [3] = 1, [4] = 1 };
        if (wis >= 17)
            return new Dictionary<int, int> { [1] = 2, [2] = 2, [3] = 1 };
        if (wis >= 16)
            return new Dictionary<int, int> { [1] = 2, [2] = 2 };
        if (wis >= 15)
            return new Dictionary<int, int> { [1] = 2, [2] = 1 };
        if (wis >= 14)
            return new Dictionary<int, int> { [1] = 2 };
        if (wis >= 13)
            return new Dictionary<int, int> { [1] = 1 };

        return new Dictionary<int, int>();
    }

    private void BtnTogglePrepared_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter?.SpellTracking is null)
            return;

        if (_preparedSpellList.SelectedItem is not PreparedSpellRow selected)
        {
            _statusText.Text = "Select a prepared spell first.";
            return;
        }

        var prepared = _selectedCharacter.SpellTracking.CurrentDayTracking.PreparedSpells;
        if (selected.Index < 0 || selected.Index >= prepared.Count)
            return;

        prepared[selected.Index].IsCast = !prepared[selected.Index].IsCast;
        SaveCharacterChanges($"Updated prepared spell state for {ResolveSpellName(prepared[selected.Index].SpellId, prepared[selected.Index].SpellName)}.");
        RefreshUi();
    }

    private void BtnRemoveCast_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter?.SpellTracking is null)
            return;

        if (_castSpellList.SelectedItem is not CastSpellRow selected)
        {
            _statusText.Text = "Select a cast spell entry first.";
            return;
        }

        _selectedCharacter.SpellTracking.UncastSpell(selected.Index);
        SaveCharacterChanges("Removed cast spell entry.");
        RefreshUi();
    }

    private void SaveCharacterChanges(string status)
    {
        if (_selectedCharacter is null)
            return;

        _selectedCharacter.LastModified = DateTime.Now;
        _selectedCharacter.Revision = Math.Max(1, _selectedCharacter.Revision + 1);
        _app.SaveCharacters();
        _statusText.Text = status;
    }

    private void SetEnabledState(bool isEnabled)
    {
        _notesEditor.IsEnabled = isEnabled;
        _sessionNotesEditor.IsEnabled = isEnabled;
        _inventoryList.IsEnabled = isEnabled;
        _newItemName.IsEnabled = isEnabled;
        _newItemQuantity.IsEnabled = isEnabled;
        _quickSpellCombo.IsEnabled = isEnabled;
        _quickSpellNotes.IsEnabled = isEnabled;
        _preparedSpellList.IsEnabled = isEnabled;
        _castSpellList.IsEnabled = isEnabled;
        _spellComponentList.IsEnabled = isEnabled;
        _componentUsageList.IsEnabled = isEnabled;
        _saveNotesButton.IsEnabled = isEnabled;
        _addItemButton.IsEnabled = isEnabled;
        _increaseQtyButton.IsEnabled = isEnabled;
        _decreaseQtyButton.IsEnabled = isEnabled;
        _removeItemButton.IsEnabled = isEnabled;
        _syncComponentsButton.IsEnabled = isEnabled;
        _castSpellButton.IsEnabled = isEnabled;
        _togglePreparedButton.IsEnabled = isEnabled;
        _removeCastButton.IsEnabled = isEnabled;
        _openSpellTrackerButton.IsEnabled = isEnabled;
        _abilityGroupsExpander.IsEnabled = isEnabled;
        _nwpList.IsEnabled = isEnabled;
        _rogueSkillsList.IsEnabled = isEnabled;
        _traitsList.IsEnabled = isEnabled;
        _classSkillsList.IsEnabled = isEnabled;
        _racialSkillsList.IsEnabled = isEnabled;
    }

    private static int ParsePositiveInt(string? text, int fallback)
    {
        if (!int.TryParse((text ?? string.Empty).Trim(), out int value) || value <= 0)
            return fallback;
        return value;
    }

    private static string BuildManualItemId(string name)
    {
        string slug = Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        if (string.IsNullOrWhiteSpace(slug))
            slug = "item";
        return $"manual|{slug}|{Guid.NewGuid():N}";
    }

    private static TextBlock BuildSectionTitle(string text)
    {
        var block = new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 4) };
        block.SetResourceReference(StyleProperty, "SubtitleText");
        return block;
    }

    private static ListBox BuildAbilityListBox()
    {
        return new ListBox
        {
            MinHeight = 48,
            MaxHeight = 140,
            Margin = new Thickness(0, 4, 0, 8)
        };
    }

    private static Expander BuildAbilitySection(string title, ListBox listBox, bool isExpanded)
    {
        return new Expander
        {
            Header = title,
            IsExpanded = isExpanded,
            Margin = new Thickness(0, 0, 0, 4),
            Content = listBox
        };
    }

    private Button CreateHeaderButton(string content, double width, RoutedEventHandler onClick, bool isLast = false, string style = "GoldButton")
    {
        var button = new Button
        {
            Content = content,
            Width = width,
            Margin = isLast ? new Thickness(0) : new Thickness(0, 0, 8, 0)
        };
        button.SetResourceReference(StyleProperty, style);
        button.Click += onClick;
        return button;
    }

    private static bool HasPriestSpellbookRestriction(CharacterSheet character)
        => HasClassAbility(character, "cleric_restriction_limited_spell_selection")
            || HasClassAbility(character, "druid_restriction_limited_spell_selection");

    private static bool IsDivineSpell(SpellDefinition spell)
    {
        string category = (spell.Category ?? string.Empty).Trim().ToLowerInvariant();
        return category.Contains("priest") || category.Contains("divine") || category.Contains("sphere");
    }

    private static bool IsDivineCaster(CharacterSheet character)
    {
        string combined = BuildClassText(character);
        return combined.Contains("priest", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("cleric", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("druid", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("paladin", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("ranger", StringComparison.OrdinalIgnoreCase)
            || character.DivineSpellSlots.Count > 0;
    }

    private static string BuildClassText(CharacterSheet character)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(character.ClassId)) parts.Add(character.ClassId);
        if (!string.IsNullOrWhiteSpace(character.ClassName)) parts.Add(character.ClassName);
        parts.AddRange(character.ClassIds.Where(c => !string.IsNullOrWhiteSpace(c)));
        return string.Join(" ", parts);
    }

    private static Dictionary<string, string> GatherCharacterSphereAccess(CharacterSheet character)
    {
        var access = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void MergeSphereSelections(Dictionary<string, string>? selections)
        {
            if (selections is null)
                return;

            foreach (var (sphere, tier) in selections)
            {
                if (string.IsNullOrWhiteSpace(sphere))
                    continue;

                string normalizedTier = string.Equals(tier, "major", StringComparison.OrdinalIgnoreCase)
                    ? "major"
                    : "minor";

                if (!access.TryGetValue(sphere, out var existing) || (existing != "major" && normalizedTier == "major"))
                    access[sphere] = normalizedTier;
            }
        }

        MergeSphereSelections(character.SelectedSpheres);
        foreach (var byClass in character.SpheresByClass.Values)
            MergeSphereSelections(byClass);

        return access;
    }

    private static bool HasClassAbility(CharacterSheet character, params string[] abilityIds)
    {
        if (abilityIds is null || abilityIds.Length == 0)
            return false;

        var wanted = new HashSet<string>(abilityIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);

        foreach (var raw in character.SelectedClassAbilityIds)
        {
            string baseId = RulesEngine.ExtractClassAbilityBaseId(raw);
            if (wanted.Contains(baseId))
                return true;
        }

        foreach (var (_, entries) in character.SelectedAbilitiesByClass)
        {
            if (entries is null)
                continue;

            if (entries.Select(RulesEngine.ExtractClassAbilityBaseId).Any(id => wanted.Contains(id)))
                return true;
        }

        return character.StructuredAbilities.Any(a => wanted.Contains(a.Id));
    }
}