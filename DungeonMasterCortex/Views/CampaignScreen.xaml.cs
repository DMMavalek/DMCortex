using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Linq;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CampaignScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    private int _selectedNpcIdx      = -1;
    private int _selectedLocationIdx = -1;
    private int _selectedLootIdx     = -1;
    private int _selectedMoonIdx     = -1;
    private string _selectedParty    = "";
    private bool _isUpdatingCampaignSelector;
    private bool _isUpdatingCalendarSourceSelector;
    private string _activeTab = "log";
    private readonly Dictionary<string, Brush> _campaignColorBrushes = new(StringComparer.OrdinalIgnoreCase);

    private sealed class CharacterPickerOption
    {
        public CharacterSheet Character { get; }
        public string Display { get; }

        public CharacterPickerOption(CharacterSheet character, string display)
        {
            Character = character;
            Display = display;
        }

        public override string ToString() => Display;
    }

    private sealed class SpellCastOption
    {
        public SpellDefinition Spell { get; }
        public int SpellLevel { get; }
        public string Display { get; }

        public SpellCastOption(SpellDefinition spell, int spellLevel, string display)
        {
            Spell = spell;
            SpellLevel = spellLevel;
            Display = display;
        }

        public override string ToString() => Display;
    }

    private sealed class PartyMemberItem
    {
        public CharacterSheet Character { get; }
        public string Display { get; }

        public PartyMemberItem(CharacterSheet character, string display)
        {
            Character = character;
            Display = display;
        }

        public override string ToString() => Display;
    }

    public CampaignScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
        ShowCampaignTab("log");
    }

    public void OnEnter()
    {
        _app.SetBanner("Campaign  ›  Session Log");
        RefreshCampaignSelector();
        ShowCampaignTab("log");
        PopulateEntryDateInputsFromCalendar();
    }

    private void BtnHub_Click(object sender, RoutedEventArgs e) =>
        _app.GoTo("hub", -1);

    // ── Tab switching ─────────────────────────────────────────────────────────


    private void CampTabLog_Click(object sender, RoutedEventArgs e)       => ShowCampaignTab("log");
    private void CampTabNpcs_Click(object sender, RoutedEventArgs e)      => ShowCampaignTab("npcs");
    private void CampTabLocations_Click(object sender, RoutedEventArgs e) => ShowCampaignTab("locations");
    private void CampTabLoot_Click(object sender, RoutedEventArgs e)      => ShowCampaignTab("loot");
    private void CampTabCalendar_Click(object sender, RoutedEventArgs e)  => ShowCampaignTab("calendar");

    private void ShowCampaignTab(string tab)
    {
        _activeTab = tab;
        CampTabLog.Visibility       = tab == "log"       ? Visibility.Visible : Visibility.Collapsed;
        CampTabNpcs.Visibility      = tab == "npcs"      ? Visibility.Visible : Visibility.Collapsed;
        CampTabLocations.Visibility = tab == "locations" ? Visibility.Visible : Visibility.Collapsed;
        CampTabLoot.Visibility      = tab == "loot"      ? Visibility.Visible : Visibility.Collapsed;
        CampTabParty.Visibility     = tab == "party"     ? Visibility.Visible : Visibility.Collapsed;
        CampTabCalendar.Visibility  = tab == "calendar"  ? Visibility.Visible : Visibility.Collapsed;

        var active   = (System.Windows.Media.Brush)FindResource("BrushBtnAct");
        var inactive = (System.Windows.Media.Brush)FindResource("BrushBtn");
        CampTabBtnLog.Background       = tab == "log"       ? active : inactive;
        CampTabBtnNpcs.Background      = tab == "npcs"      ? active : inactive;
        CampTabBtnLocations.Background = tab == "locations" ? active : inactive;
        CampTabBtnLoot.Background      = tab == "loot"      ? active : inactive;
        CampTabBtnParty.Background     = tab == "party"     ? active : inactive;
        CampTabBtnCalendar.Background  = tab == "calendar"  ? active : inactive;

        if (tab == "log")
        {
            RefreshList();
            PopulateEntryDateInputsFromCalendar();
        }
        if (tab == "npcs")      RefreshNpcList();
        if (tab == "locations") RefreshLocationList();
        if (tab == "loot")      RefreshLootList();
        if (tab == "party")     { RefreshPartySelector(); RefreshPartyMemberList(); }
        if (tab == "calendar")  RefreshCalendarView();
    }

    // ── Party Loot tab ─────────────────────────────────────────────────────
    private void RefreshLootList()
    {
        LootList.Items.Clear();
        foreach (var loot in _app.Campaign.PartyLoot)
            LootList.Items.Add(loot.ListDisplay);
        _selectedLootIdx = -1;
        ClearLootEditor();
    }

    private void LootList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = LootList.SelectedIndex;
        if (idx < 0 || idx >= _app.Campaign.PartyLoot.Count) return;
        _selectedLootIdx = idx;
        var loot = _app.Campaign.PartyLoot[idx];
        LootName.Text        = loot.Name;
        LootDescription.Text = loot.Description;
        LootQuantity.Text    = loot.Quantity.ToString();
        LootSource.Text      = loot.Source;
        LootSessionId.Text   = loot.SessionId;
        LootNotes.Text       = loot.Notes;
    }

    private void BtnAddLoot_Click(object sender, RoutedEventArgs e)
    {
        _app.Campaign.PartyLoot.Add(new PartyLootEntry { Name = "New Loot" });
        RefreshLootList();
        LootList.SelectedIndex = _app.Campaign.PartyLoot.Count - 1;
    }

    private void BtnDeleteLoot_Click(object sender, RoutedEventArgs e)
    {
        int idx = LootList.SelectedIndex;
        if (idx < 0) return;
        var result = MessageBox.Show("Delete this loot item?", "Confirm",
                                     MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        _app.Campaign.PartyLoot.RemoveAt(idx);
        RefreshLootList();
    }

    private void BtnSaveLoot_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLootIdx < 0 || _selectedLootIdx >= _app.Campaign.PartyLoot.Count) return;
        var loot = _app.Campaign.PartyLoot[_selectedLootIdx];
        loot.Name        = LootName.Text.Trim();
        loot.Description = LootDescription.Text.Trim();
        loot.Source      = LootSource.Text.Trim();
        loot.SessionId   = LootSessionId.Text.Trim();
        loot.Notes       = LootNotes.Text.Trim();
        if (int.TryParse(LootQuantity.Text.Trim(), out int qty))
            loot.Quantity = qty;
        else
            loot.Quantity = 1;
        RefreshLootList();
        LootList.SelectedIndex = _selectedLootIdx;
    }

    private void ClearLootEditor()
    {
        LootName.Text = LootDescription.Text = LootQuantity.Text = LootSource.Text = LootSessionId.Text = LootNotes.Text = "";
    }

    private void RefreshCampaignSelector()
    {
        _isUpdatingCampaignSelector = true;
        CmbCampaignSelect.Items.Clear();

        foreach (var campaign in _app.GetCampaigns())
            CmbCampaignSelect.Items.Add(new CampaignSelectorItem(campaign));

        int idx = -1;
        for (int i = 0; i < CmbCampaignSelect.Items.Count; i++)
        {
            if (CmbCampaignSelect.Items[i] is CampaignSelectorItem item
                && string.Equals(item.CampaignId, _app.ActiveCampaignId, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }

        CmbCampaignSelect.SelectedIndex = idx;
        _isUpdatingCampaignSelector = false;
    }

    private void CmbCampaignSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingCampaignSelector) return;
        if (CmbCampaignSelect.SelectedItem is not CampaignSelectorItem selected) return;
        if (!_app.SwitchCampaign(selected.CampaignId)) return;

        _app.SetBanner($"Campaign  ›  {_activeTab.ToUpperInvariant()}  ({_app.Campaign.CampaignName})");
        PopulateEntryDateInputsFromCalendar();
        ShowCampaignTab(_activeTab);
    }

    private void CampTabParty_Click(object sender, RoutedEventArgs e) => ShowCampaignTab("party");

    private void BtnCreateCampaign_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtNewCampaignName.Text.Trim();
        if (!_app.CreateCampaign(name, out string message))
        {
            MessageBox.Show(message, "Create Campaign", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TxtNewCampaignName.Text = "";
        RefreshCampaignSelector();
        PopulateEntryDateInputsFromCalendar();
        ShowCampaignTab(_activeTab);
    }

    private void BtnDeleteCampaign_Click(object sender, RoutedEventArgs e)
    {
        if (CmbCampaignSelect.SelectedItem is not CampaignSelectorItem selected)
            return;

        var result = MessageBox.Show(
            $"Delete campaign '{selected.CampaignName}'?",
            "Delete Campaign",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        if (!_app.DeleteCampaign(selected.CampaignId, out string message))
        {
            MessageBox.Show(message, "Delete Campaign", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshCampaignSelector();
        PopulateEntryDateInputsFromCalendar();
        ShowCampaignTab(_activeTab);
    }

    private sealed class CampaignSelectorItem
    {
        public string CampaignId { get; }
        public string CampaignName { get; }

        public CampaignSelectorItem(CampaignService campaign)
        {
            CampaignId = campaign.CampaignId;
            CampaignName = campaign.CampaignName;
        }

        public override string ToString() => CampaignName;
    }

    private List<SpellDefinition> GetKnownSpellsForCharacter(CharacterSheet character)
    {
        bool isWizard = IsWizardCaster(character);
        bool isDivine = IsDivineCaster(character);
        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (isWizard)
        {
            // Wizards: load from spellbook
            foreach (string id in character.WizardSpellbookIds)
                if (!string.IsNullOrWhiteSpace(id))
                    knownIds.Add(id);
        }
        else if (isDivine)
        {
            // Clerics/Druids: load from prepared spells or accessible divine spells
            if (character.SpellTracking is not null)
            {
                foreach (var prepared in character.SpellTracking.CurrentDayTracking.PreparedSpells)
                    if (!string.IsNullOrWhiteSpace(prepared.SpellId))
                        knownIds.Add(prepared.SpellId);
            }
            else
            {
                // Fallback: load all accessible divine spells if no tracking
                foreach (var spell in _app.Rules.Spells
                    .Where(s => string.Equals(s.Category, "divine", System.StringComparison.OrdinalIgnoreCase)))
                    knownIds.Add(spell.Id);
            }
        }
        else
        {
            // Generic: load from tracked spells
            foreach (string id in character.TrackedSpellIds)
                if (!string.IsNullOrWhiteSpace(id))
                    knownIds.Add(id);
        }

        return _app.Rules.Spells
            .Where(s => knownIds.Contains(s.Id))
            .ToList();
    }

    private static int ParseSpellLevelOrDefault(string? levelText)
    {
        string text = (levelText ?? string.Empty).Trim();
        if (text.Length == 0)
            return 1;

        int start = -1;
        int end = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsDigit(text[i]))
            {
                if (start < 0) start = i;
                end = i;
            }
            else if (start >= 0)
            {
                break;
            }
        }

        if (start >= 0 && end >= start)
        {
            string token = text[start..(end + 1)];
            if (int.TryParse(token, out int level) && level >= 0)
                return level;
        }

        return 1;
    }

    private bool IsWizardCaster(CharacterSheet character)
    {
        return string.Equals(character.ClassId, "wizard", System.StringComparison.OrdinalIgnoreCase)
            || character.ClassIds.Any(id => string.Equals(id, "wizard", System.StringComparison.OrdinalIgnoreCase));
    }

    private bool IsDivineCaster(CharacterSheet character)
    {
        string classId = (character.ClassId ?? string.Empty).ToLowerInvariant();
        return classId is "priest" or "cleric" or "druid" or "ranger" or "paladin"
            || character.ClassIds.Any(id => (id ?? string.Empty).ToLowerInvariant() is "priest" or "cleric" or "druid" or "ranger" or "paladin");
    }

    private void RefreshPartyMemberList()
    {
        PartyMemberList.Items.Clear();
        var partyMembers = _app.Characters
            .Where(c => string.IsNullOrEmpty(_selectedParty) || string.Equals(c.Party, _selectedParty, System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, System.StringComparer.OrdinalIgnoreCase);
        
        foreach (var character in partyMembers)
        {
            string display = $"{character.Name}  ({character.CurrentHitPoints}/{character.HitPoints} HP)";
            PartyMemberList.Items.Add(new PartyMemberItem(character, display));
        }
    }

    private void BtnHealPartyMember_Click(object sender, RoutedEventArgs e)
    {
        if (PartyMemberList.SelectedItem is not PartyMemberItem item)
        {
            MessageBox.Show("Select a party member first.", "Healing", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var character = item.Character;
        if (!int.TryParse((PartyHealInput.Text ?? string.Empty).Trim(), out int healAmount) || healAmount <= 0)
        {
            MessageBox.Show("Enter a positive healing amount.", "Healing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int oldHp = character.CurrentHitPoints;
        character.CurrentHitPoints = System.Math.Min(character.HitPoints, character.CurrentHitPoints + healAmount);
        character.Revision += 1;
        character.LastModified = System.DateTime.Now;
        _app.SaveCharacters();

        PartyCharHp.Text = character.CurrentHitPoints.ToString();
        PartyHealInput.Text = "0";
        RefreshPartyMemberList();

        MessageBox.Show(
            $"{character.Name} healed from {oldHp} to {character.CurrentHitPoints} HP.",
            "Healing",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RefreshPartySelector()
    {
        CmbPartySelector.Items.Clear();
        var uniqueParties = _app.Characters
            .Select(c => string.IsNullOrEmpty(c.Party) ? "(No Party)" : c.Party)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();

        CmbPartySelector.Items.Add("(All Characters)");
        foreach (var party in uniqueParties)
        {
            CmbPartySelector.Items.Add(party);
        }

        CmbPartySelector.SelectedIndex = 0;
        _selectedParty = "";
    }

    private void CmbPartySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbPartySelector.SelectedItem is not string selected)
            return;

        _selectedParty = selected == "(All Characters)" ? "" : selected;
        RefreshPartyMemberList();
    }

    private void PartyMemberList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PartyMemberList.SelectedItem is not PartyMemberItem item)
            return;

        var character = item.Character;
        PartyMemberDetail.Text = $"{character.Name}  ({character.RaceName} {character.ClassName})";
        PartyCharHp.Text = character.CurrentHitPoints.ToString();
        PartyCharMaxHp.Text = character.HitPoints.ToString();
        PartyHealInput.Text = "0";
        PartyCharNotes.Text = string.Join("\n", character.Notes);
        
        // Populate spell list for this character
        RefreshSpellListForCharacter(character);
        
        // Populate target list
        CmbSpellTarget.Items.Clear();
        foreach (var c in _app.Characters.OrderBy(x => x.Name))
        {
            CmbSpellTarget.Items.Add(new CharacterPickerOption(c, c.Name));
        }
    }

    private void ChkCastAnySpell_Changed(object sender, RoutedEventArgs e)
    {
        if (PartyMemberList.SelectedItem is not PartyMemberItem item)
            return;

        var character = item.Character;
        if (ChkCastAnySpell.IsChecked == true)
        {
            // Populate ALL spells for this character
            RefreshSpellListForCharacter(character, showAll: true);
            ChkHealingOnly.IsEnabled = false;
            ChkBuffsOnly.IsEnabled = false;
        }
        else
        {
            ChkHealingOnly.IsEnabled = true;
            ChkBuffsOnly.IsEnabled = true;
            RefreshSpellListForCharacter(character);
        }
    }

    private void ChkSpellFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (PartyMemberList.SelectedItem is not PartyMemberItem item)
            return;

        var character = item.Character;
        RefreshSpellListForCharacter(character);
    }

    private void RefreshSpellListForCharacter(CharacterSheet character, bool showAll = false)
    {
        CmbSpellToCast.Items.Clear();

        var spells = GetKnownSpellsForCharacter(character);

        // Apply filters
        if (!showAll && ChkCastAnySpell.IsChecked != true)
        {
            if (ChkHealingOnly.IsChecked == true)
                spells = spells.Where(s => s.IsHealing).ToList();
            else if (ChkBuffsOnly.IsChecked == true)
                spells = spells.Where(s => !s.IsHealing && (s.Name.Contains("Buff", System.StringComparison.OrdinalIgnoreCase) || 
                         s.Name.Contains("Bless", System.StringComparison.OrdinalIgnoreCase) ||
                         s.Name.Contains("Protect", System.StringComparison.OrdinalIgnoreCase) ||
                         s.Name.Contains("Enhance", System.StringComparison.OrdinalIgnoreCase))).ToList();
        }

        // Sort by level
        spells = spells.OrderBy(s => ParseSpellLevelOrDefault(s.Level)).ThenBy(s => s.Name).ToList();

        foreach (var spell in spells)
        {
            int level = ParseSpellLevelOrDefault(spell.Level);
            string display = $"{spell.Name}  (Level {level})";
            CmbSpellToCast.Items.Add(new SpellCastOption(spell, level, display));
        }
    }

    private void BtnCastPartySpell_Click(object sender, RoutedEventArgs e)
    {
        if (PartyMemberList.SelectedItem is not PartyMemberItem casterItem)
        {
            MessageBox.Show("Select a caster.", "Spell Casting", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (CmbSpellTarget.SelectedItem is not CharacterPickerOption targetOption)
        {
            MessageBox.Show("Select a target.", "Spell Casting", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (CmbSpellToCast.SelectedItem is not SpellCastOption spellOption)
        {
            MessageBox.Show("Select a spell.", "Spell Casting", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var caster = casterItem.Character;
        var target = targetOption.Character;
        var spell = spellOption.Spell;

        // Record spell cast in tracking
        if (caster.SpellTracking is not null)
        {
            var castRecord = new CastSpellRecord
            {
                SpellId = spell.Id,
                SpellName = spell.Name,
                SpellLevel = spellOption.SpellLevel,
                CastTime = System.DateTime.Now
            };
            caster.SpellTracking.CurrentDayTracking.CastSpells.Add(castRecord);
        }

        // Apply HP delta if specified
        if (int.TryParse((TxtSpellHpDelta.Text ?? "0").Trim(), out int hpDelta) && hpDelta != 0)
        {
            target.CurrentHitPoints = System.Math.Max(0, System.Math.Min(target.HitPoints, target.CurrentHitPoints + hpDelta));
        }

        caster.Revision += 1;
        caster.LastModified = System.DateTime.Now;
        target.Revision += 1;
        target.LastModified = System.DateTime.Now;
        _app.SaveCharacters();

        // Log to campaign
        var castNote = TxtSpellCastNote.Text.Trim();
        string entryText = $"{caster.Name} cast {spell.Name} on {target.Name}.";
        if (!string.IsNullOrEmpty(castNote))
            entryText += $"\n\n{castNote}";

        var cal = GetDisplayCalendar();
        string worldDate = $"{cal.GetDateDisplay(cal.CurrentYear, cal.CurrentEra, cal.CurrentMonth, cal.CurrentDay)}  {cal.CurrentHour:D2}:{cal.CurrentMinute:D2}";
        string sessionId = string.IsNullOrWhiteSpace(TxtSessionId.Text)
            ? $"SPELL-{System.DateTime.Now:yyyyMMdd-HHmmss}"
            : TxtSessionId.Text.Trim();

        _app.Campaign.AddEntry(new CampaignEntry
        {
            SessionId = sessionId,
            Title = "Spell Cast",
            Body = entryText,
            WorldDate = worldDate
        });

        _app.Campaign.Calendar.AddEvent(cal.CurrentYear, cal.CurrentEra, cal.CurrentMonth, cal.CurrentDay, new CalendarEvent
        {
            EventType = "Spell",
            Title = $"{spell.Name}: {caster.Name} -> {target.Name}",
            Description = entryText,
            RelatedId = sessionId
        });

        TxtSpellHpDelta.Text = "0";
        TxtSpellCastNote.Text = string.Empty;
        RefreshSpellListForCharacter(caster);

        MessageBox.Show(entryText, "Spell Casting", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Session Log ───────────────────────────────────────────────────────────

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCreateCampaignEntryInCurrentLicense())
            return;

        var sid   = TxtSessionId.Text.Trim();
        var title = TxtTitle.Text.Trim();
        var body  = TxtBody.Text.Trim();

        if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(title))
        {
            MessageBox.Show("Session ID and Title are required.", "Missing Fields",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryGetEntryDateParts(out int year, out int month, out int day, out int hour, out int minute, out string era))
            return;

        var displayCal = GetDisplayCalendar();
        string worldDate = $"{displayCal.GetDateDisplay(year, era, month, day)}  {hour:D2}:{minute:D2}";

        _app.Campaign.AddEntry(new CampaignEntry
        {
            SessionId = sid,
            Title = title,
            Body = body,
            WorldDate = worldDate
        });

        _app.Campaign.Calendar.AddEvent(year, era, month, day, new CalendarEvent
        {
            EventType = "Log",
            Title = title,
            Description = body,
            RelatedId = sid
        });

        TxtSessionId.Text = TxtTitle.Text = TxtBody.Text = "";
        PopulateEntryDateInputsFromCalendar();
        RefreshList();
    }

    private void EntryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = EntryList.SelectedIndex;
        if (idx < 0) return;
        var entry = _app.Campaign.Entries[idx];
        TxtDetail.Text = $"[{entry.SessionId}]  {entry.Title}\nDate: {entry.WorldDate}\n\n{entry.Body}";
    }

    private void RefreshList()
    {
        EntryList.Items.Clear();
        foreach (var entry in _app.Campaign.Entries)
            EntryList.Items.Add(entry.ListDisplay);
    }

    private void BtnUseCurrentCampaignDate_Click(object sender, RoutedEventArgs e)
    {
        PopulateEntryDateInputsFromCalendar();
    }

    private void PopulateEntryDateInputsFromCalendar()
    {
        var cal = GetDisplayCalendar();
        RefreshEntryMonthDropdown();
        if (cal.CurrentMonth >= 0 && cal.CurrentMonth < CmbEntryMonth.Items.Count)
            CmbEntryMonth.SelectedIndex = cal.CurrentMonth;
        TxtEntryDay.Text = cal.CurrentDay.ToString();
        TxtEntryYear.Text = cal.CurrentYear.ToString();
        CmbEntryEra.Text = cal.CurrentEra;
        TxtEntryHour.Text = cal.CurrentHour.ToString();
        TxtEntryMinute.Text = cal.CurrentMinute.ToString();
    }

    private void RefreshEntryMonthDropdown()
    {
        if (CmbEntryMonth is null) return;

        int currentSelection = CmbEntryMonth.SelectedIndex;
        CmbEntryMonth.Items.Clear();

        var cal = GetDisplayCalendar();
        for (int i = 0; i < cal.Config.MonthCount; i++)
        {
            string monthName = i < cal.Config.MonthNames.Count ? cal.Config.MonthNames[i] : $"Month {i + 1}";
            CmbEntryMonth.Items.Add($"{i}: {monthName}");
        }

        if (currentSelection >= 0 && currentSelection < CmbEntryMonth.Items.Count)
            CmbEntryMonth.SelectedIndex = currentSelection;
    }

    private bool TryGetEntryDateParts(out int year, out int month, out int day, out int hour, out int minute, out string era)
    {
        var cal = GetDisplayCalendar();
        year = cal.CurrentYear;
        month = cal.CurrentMonth;
        day = cal.CurrentDay;
        hour = cal.CurrentHour;
        minute = cal.CurrentMinute;
        era = (CmbEntryEra.Text ?? "").Trim();

        month = CmbEntryMonth.SelectedIndex;
        if (month < 0 || month >= cal.Config.MonthCount)
        {
            MessageBox.Show("Entry month is invalid.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(TxtEntryDay.Text, out day) || day < 1 || day > cal.Config.GetDaysInMonth(month))
        {
            MessageBox.Show("Entry day is invalid for the selected month.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(TxtEntryYear.Text, out year))
        {
            MessageBox.Show("Entry year is invalid.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(TxtEntryHour.Text, out hour) || hour < 0 || hour >= cal.Config.HoursPerDay)
        {
            MessageBox.Show($"Entry hour must be 0 to {cal.Config.HoursPerDay - 1}.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(TxtEntryMinute.Text, out minute) || minute < 0 || minute > 59)
        {
            MessageBox.Show("Entry minute must be 0 to 59.", "Invalid Date", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(era))
            era = cal.CurrentEra;

        return true;
    }

    // ── NPCs tab ──────────────────────────────────────────────────────────────

    private void RefreshNpcList()
    {
        NpcList.Items.Clear();
        foreach (var n in _app.Campaign.Npcs)
            NpcList.Items.Add(n.ListDisplay);
        _selectedNpcIdx = -1;
        ClearNpcEditor();
    }

    private void NpcList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = NpcList.SelectedIndex;
        if (idx < 0 || idx >= _app.Campaign.Npcs.Count) return;
        _selectedNpcIdx = idx;
        var n = _app.Campaign.Npcs[idx];
        NpcName.Text  = n.Name;
        NpcRole.Text  = n.Role;
        NpcNotes.Text = n.Notes;
    }

    private void BtnNewNpc_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCreateCampaignEntryInCurrentLicense())
            return;

        _app.Campaign.Npcs.Add(new NpcEntry { Name = "New NPC" });
        RefreshNpcList();
        NpcList.SelectedIndex = _app.Campaign.Npcs.Count - 1;
    }

    private void BtnDeleteNpc_Click(object sender, RoutedEventArgs e)
    {
        int idx = NpcList.SelectedIndex;
        if (idx < 0) return;
        var result = MessageBox.Show("Delete this NPC?", "Confirm",
                                     MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        _app.Campaign.Npcs.RemoveAt(idx);
        RefreshNpcList();
    }

    private void BtnSaveNpc_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNpcIdx < 0 || _selectedNpcIdx >= _app.Campaign.Npcs.Count) return;
        var n = _app.Campaign.Npcs[_selectedNpcIdx];
        n.Name  = NpcName.Text.Trim();
        n.Role  = NpcRole.Text.Trim();
        n.Notes = NpcNotes.Text.Trim();
        RefreshNpcList();
        NpcList.SelectedIndex = _selectedNpcIdx;
    }

    private void ClearNpcEditor()
    {
        NpcName.Text = NpcRole.Text = NpcNotes.Text = "";
    }

    // ── Locations tab ─────────────────────────────────────────────────────────

    private void RefreshLocationList()
    {
        LocationList.Items.Clear();
        foreach (var l in _app.Campaign.Locations)
            LocationList.Items.Add(l.ListDisplay);
        _selectedLocationIdx = -1;
        ClearLocationEditor();
    }

    private void LocationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = LocationList.SelectedIndex;
        if (idx < 0 || idx >= _app.Campaign.Locations.Count) return;
        _selectedLocationIdx = idx;
        var l = _app.Campaign.Locations[idx];
        LocName.Text  = l.Name;
        LocType.Text  = l.Type;
        LocNotes.Text = l.Notes;
    }

    private void BtnNewLocation_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCreateCampaignEntryInCurrentLicense())
            return;

        _app.Campaign.Locations.Add(new LocationEntry { Name = "New Location" });
        RefreshLocationList();
        LocationList.SelectedIndex = _app.Campaign.Locations.Count - 1;
    }

    private void BtnDeleteLocation_Click(object sender, RoutedEventArgs e)
    {
        int idx = LocationList.SelectedIndex;
        if (idx < 0) return;
        var result = MessageBox.Show("Delete this location?", "Confirm",
                                     MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        _app.Campaign.Locations.RemoveAt(idx);
        RefreshLocationList();
    }

    private void BtnSaveLocation_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLocationIdx < 0 || _selectedLocationIdx >= _app.Campaign.Locations.Count) return;
        var l = _app.Campaign.Locations[_selectedLocationIdx];
        l.Name  = LocName.Text.Trim();
        l.Type  = LocType.Text.Trim();
        l.Notes = LocNotes.Text.Trim();
        RefreshLocationList();
        LocationList.SelectedIndex = _selectedLocationIdx;
    }

    private void ClearLocationEditor()
    {
        LocName.Text = LocType.Text = LocNotes.Text = "";
    }

    private bool CanCreateCampaignEntryInCurrentLicense()
    {
        if (!_app.License.IsDemoMode)
            return true;

        int totalEntries = _app.Campaign.Entries.Count + _app.Campaign.Npcs.Count + _app.Campaign.Locations.Count;
        if (totalEntries < _app.License.DemoMaxCampaignEntries)
            return true;

        MessageBox.Show(
            $"Demo mode allows up to {_app.License.DemoMaxCampaignEntries} campaign entries in total (log + NPCs + locations).",
            "Demo Limit",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    // ── Calendar tab ──────────────────────────────────────────────────────────

    private void RefreshCalendarView()
    {
        var cal = GetDisplayCalendar();

        // Update calendar configuration from UI
        CalMonthCount.Text = cal.Config.MonthCount.ToString();
        CalDaysPerWeek.Text = cal.Config.DaysPerWeek.ToString();
        CalHoursPerDay.Text = cal.Config.HoursPerDay.ToString();
        CalCurrentYear.Text = cal.CurrentYear.ToString();
        CalYearEra.Text = cal.CurrentEra;
        RefreshCalendarSourceSelector();

        // Update moon list
        RefreshMoonList();

        // Update current date display
        CalCurrentDate.Text = cal.GetCurrentDateDisplay();

        // Update moon info
        RefreshMoonInfo();

        // Render calendar grid
        RenderCalendarGrid();

        // Refresh timeline
        RefreshTimelineView();
    }

    private void RefreshMoonList()
    {
        MoonList.Items.Clear();
        foreach (var moon in GetDisplayCalendar().Config.Moons)
        {
            MoonList.Items.Add($"{moon.Name} ({moon.GetPhaseText()})");
        }
        _selectedMoonIdx = -1;
    }

    private void RefreshMoonInfo()
    {
        MoonInfoPanel.Children.Clear();
        var header = new TextBlock { Text = "Moons", Style = (Style)FindResource("LabelText") };
        MoonInfoPanel.Children.Add(header);

        if (GetDisplayCalendar().Config.Moons.Count == 0)
        {
            var noMoons = new TextBlock { Text = "No moons configured", Foreground = (System.Windows.Media.Brush)FindResource("BrushBorder2"), Margin = new(0, 6, 0, 0) };
            MoonInfoPanel.Children.Add(noMoons);
            return;
        }

        foreach (var moon in GetDisplayCalendar().Config.Moons)
        {
            var moonBlock = new TextBlock
            {
                Text = $"{moon.Name}: {moon.GetPhaseText()} ({moon.Illumination})",
                Margin = new(0, 6, 0, 0)
            };
            MoonInfoPanel.Children.Add(moonBlock);
        }
    }

    private void RenderCalendarGrid()
    {
        var cal = GetDisplayCalendar();
        CalendarGridPanel.Children.Clear();
        CalendarGridPanel.ColumnDefinitions.Clear();
        CalendarGridPanel.RowDefinitions.Clear();

        CalMonthDisplay.Text = $"{cal.Config.MonthNames[cal.CurrentMonth]}";

        // Setup column definitions for day names
        for (int i = 0; i < cal.Config.DaysPerWeek; i++)
        {
            CalendarGridPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        }

        // Add day of week headers
        for (int dow = 0; dow < cal.Config.DaysPerWeek; dow++)
        {
            var dayHeader = new TextBlock
            {
                Text = dow < cal.Config.DayNames.Count ? cal.Config.DayNames[dow] : $"D{dow}",
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Margin = new(4)
            };
            Grid.SetColumn(dayHeader, dow);
            Grid.SetRow(dayHeader, 0);
            CalendarGridPanel.Children.Add(dayHeader);
        }

        // Header row
        CalendarGridPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // First week row
        CalendarGridPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Render days of month
        int daysInMonth = cal.Config.GetDaysInMonth(cal.CurrentMonth);
        int row = 1;
        int col = 0;

        // Find first day of week (uses DayOfWeekOffset to align calendar)
        int startDow = cal.Config.DayOfWeekOffset;
        col = startDow;

        for (int day = 1; day <= daysInMonth; day++)
        {
            if (col == 0 && day > 1)
            {
                CalendarGridPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row++;
            }

            // Gather events for this day
            var dayEvents = GetEventsForDisplayDate(cal.CurrentMonth, day, cal.CurrentYear, cal.CurrentEra);

            // Gather unique campaign IDs for this day
            var campaignIds = dayEvents.Select(e => e.CampaignId).Distinct().ToList();

            // Create a stack panel to hold the day number and campaign color markers
            var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };

            // Day number
            var dayNumText = new TextBlock
            {
                Text = day.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = day == cal.CurrentDay ? FontWeights.Bold : FontWeights.Normal,
                Foreground = day == cal.CurrentDay ? (Brush)FindResource("BrushGold") : (Brush)FindResource("BrushText")
            };
            stack.Children.Add(dayNumText);

            // Campaign color markers (as small ellipses)
            if (campaignIds.Count > 0)
            {
                var markerPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
                foreach (var campaignId in campaignIds)
                {
                    var ellipse = new System.Windows.Shapes.Ellipse
                    {
                        Width = 10,
                        Height = 10,
                        Margin = new Thickness(1, 0, 1, 0),
                        Fill = GetCampaignHighlightBrush(campaignId),
                        Stroke = (Brush)FindResource("BrushBorder2"),
                        StrokeThickness = 1,
                        ToolTip = _app.GetCampaignById(campaignId)?.CampaignName ?? campaignId
                    };
                    markerPanel.Children.Add(ellipse);
                }
                stack.Children.Add(markerPanel);
            }


            var dayButton = new Button
            {
                Content = stack,
                Style = (Style)FindResource("GoldButton"),
                ContentTemplate = null,
                Margin = new(2),
                Padding = new(8, 6, 8, 6),
                Tag = day
            };

            // Tooltip for events (must be string or null, never a UIElement)
            if (dayEvents.Count > 0)
            {
                dayButton.ToolTip = string.Join("\n", dayEvents.Select(ev => $"[{_app.GetCampaignById(ev.CampaignId)?.CampaignName ?? ev.CampaignId}] {ev.Event.Title}"));
            }
            else
            {
                dayButton.ToolTip = null;
            }

            dayButton.Click += (s, e) => DayButton_Click(day);

            Grid.SetColumn(dayButton, col);
            Grid.SetRow(dayButton, row);
            CalendarGridPanel.Children.Add(dayButton);

            col++;
            if (col >= cal.Config.DaysPerWeek)
            {
                col = 0;
            }
        }
    }

    private void DayButton_Click(int day)
    {
        var cal = GetDisplayCalendar();
        var dayEvents = GetEventsForDisplayDate(cal.CurrentMonth, day, cal.CurrentYear, cal.CurrentEra);
        
        DateEventsList.Items.Clear();
        foreach (var item in dayEvents)
        {
            DateEventsList.Items.Add(CreateHighlightedEventItem(
                campaignId: item.CampaignId,
                campaignName: item.CampaignName,
                text: $"{item.Event.EventType}: {item.Event.Title}"));
        }
    }

    private void MoonList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedMoonIdx = MoonList.SelectedIndex;
    }

    private void BtnAddMoon_Click(object sender, RoutedEventArgs e)
    {
        GetDisplayCalendar().Config.Moons.Add(new MoonDefinition { Name = "New Moon" });
        RefreshMoonList();
        RefreshMoonInfo();
    }

    private void BtnDeleteMoon_Click(object sender, RoutedEventArgs e)
    {
        var cal = GetDisplayCalendar();
        if (_selectedMoonIdx < 0 || _selectedMoonIdx >= cal.Config.Moons.Count) return;
        cal.Config.Moons.RemoveAt(_selectedMoonIdx);
        RefreshMoonList();
        RefreshMoonInfo();
    }

    private void BtnSaveCalConfig_Click(object sender, RoutedEventArgs e)
    {
        var cal = GetDisplayCalendar();
        if (!int.TryParse(CalMonthCount.Text, out int months) || months < 1 || months > 24)
        {
            MessageBox.Show("Months must be between 1 and 24.", "Invalid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(CalDaysPerWeek.Text, out int daysPerWeek) || daysPerWeek < 4 || daysPerWeek > 10)
        {
            MessageBox.Show("Days per week must be between 4 and 10.", "Invalid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(CalHoursPerDay.Text, out int hoursPerDay) || hoursPerDay < 1 || hoursPerDay > 48)
        {
            MessageBox.Show("Hours per day must be between 1 and 48.", "Invalid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(CalCurrentYear.Text, out int currentYear))
        {
            MessageBox.Show("Current year must be a valid number.", "Invalid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string era = (CalYearEra.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(era))
        {
            MessageBox.Show("Year suffix (era) is required.", "Invalid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        cal.Config.MonthCount = months;
        cal.Config.DaysPerWeek = daysPerWeek;
        cal.Config.HoursPerDay = hoursPerDay;
        cal.CurrentYear = currentYear;
        cal.CurrentEra = era;
        cal.Config.NormalizeDaysPerMonth();
        cal.InitializeDates();

        MessageBox.Show("Calendar configuration saved.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        RenderCalendarGrid();
    }

    private void BtnPrevDay_Click(object sender, RoutedEventArgs e)
    {
        var cal = GetDisplayCalendar();
        int minutesPerDay = cal.Config.HoursPerDay * 60;
        // Go back one day by setting time to start of previous day
        cal.CurrentDay--;
        if (cal.CurrentDay < 1)
        {
            cal.CurrentMonth--;
            if (cal.CurrentMonth < 0)
            {
                cal.CurrentMonth = cal.Config.MonthCount - 1;
                cal.CurrentYear--;
            }
            cal.CurrentDay = cal.Config.GetDaysInMonth(cal.CurrentMonth);
        }
        cal.TotalDaysElapsed = Math.Max(0, cal.TotalDaysElapsed - 1);
        cal.TotalMinutesElapsed = Math.Max(0, cal.TotalMinutesElapsed - minutesPerDay);
        cal.CurrentHour = 0;
        cal.CurrentMinute = 0;
        cal.UpdateMoonCycles();
        RefreshCalendarView();
    }

    private void BtnNextDay_Click(object sender, RoutedEventArgs e)
    {
        GetDisplayCalendar().AdvanceDay();
        RefreshCalendarView();
    }

    private void BtnAdvanceMinutes_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CalAdvanceAmount.Text, out int amount) || amount < 1)
        {
            MessageBox.Show("Enter a positive number in the Advance by field.", "Invalid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        GetDisplayCalendar().AdvanceTime(amount);
        RefreshCalendarView();
    }

    private void BtnAdvanceHours_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CalAdvanceAmount.Text, out int amount) || amount < 1)
        {
            MessageBox.Show("Enter a positive number in the Advance by field.", "Invalid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        GetDisplayCalendar().AdvanceTime(amount * 60);
        RefreshCalendarView();
    }

    private void BtnAdvanceDays_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(CalAdvanceAmount.Text, out int amount) || amount < 1)
        {
            MessageBox.Show("Enter a positive number in the Advance by field.", "Invalid", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var cal = GetDisplayCalendar();
        cal.AdvanceTime(amount * cal.Config.HoursPerDay * 60);
        RefreshCalendarView();
    }

    private void BtnSetDate_Click(object sender, RoutedEventArgs e)
    {
        var cal = GetDisplayCalendar();
        
        // Create a scrollable panel for the dialog
        var scrollPanel = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12) };
        var panel = new StackPanel { Margin = new Thickness(0) };
        scrollPanel.Content = panel;

        // Year picker
        var yearLabel = new TextBlock { Text = "Year:", Margin = new Thickness(0, 0, 0, 4) };
        var yearBox = new TextBox { Style = (Style)FindResource("DarkTextBox"), Text = cal.CurrentYear.ToString(), Margin = new Thickness(0, 0, 0, 8) };

        // Era picker
        var eraLabel = new TextBlock { Text = "Era (e.g., BC, PC, EDD):", Margin = new Thickness(0, 0, 0, 4) };
        var eraBox = new TextBox { Style = (Style)FindResource("DarkTextBox"), Text = cal.CurrentEra, Margin = new Thickness(0, 0, 0, 8) };

        // Month picker
        var monthLabel = new TextBlock { Text = "Month (0-based index):", Margin = new Thickness(0, 0, 0, 4) };
        var monthBox = new TextBox { Style = (Style)FindResource("DarkTextBox"), Text = cal.CurrentMonth.ToString(), Margin = new Thickness(0, 0, 0, 8) };

        // Day picker
        var dayLabel = new TextBlock { Text = "Day:", Margin = new Thickness(0, 0, 0, 4) };
        var dayBox = new TextBox { Style = (Style)FindResource("DarkTextBox"), Text = cal.CurrentDay.ToString(), Margin = new Thickness(0, 0, 0, 8) };

        // Hour picker
        var hourLabel = new TextBlock { Text = "Hour (0 = midnight):", Margin = new Thickness(0, 0, 0, 4) };
        var hourBox = new TextBox { Style = (Style)FindResource("DarkTextBox"), Text = cal.CurrentHour.ToString(), Margin = new Thickness(0, 0, 0, 8) };

        // Minute picker
        var minuteLabel = new TextBlock { Text = "Minute:", Margin = new Thickness(0, 0, 0, 4) };
        var minuteBox = new TextBox { Style = (Style)FindResource("DarkTextBox"), Text = cal.CurrentMinute.ToString(), Margin = new Thickness(0, 0, 0, 8) };

        panel.Children.Add(yearLabel);
        panel.Children.Add(yearBox);
        panel.Children.Add(eraLabel);
        panel.Children.Add(eraBox);
        panel.Children.Add(monthLabel);
        panel.Children.Add(monthBox);
        panel.Children.Add(dayLabel);
        panel.Children.Add(dayBox);
        panel.Children.Add(hourLabel);
        panel.Children.Add(hourBox);
        panel.Children.Add(minuteLabel);
        panel.Children.Add(minuteBox);

        // Separator
        var separator = new Rectangle { Height = 1, Fill = (System.Windows.Media.Brush)FindResource("BrushBorder2"), Margin = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(separator);

        // Day of Week Alignment section
        var alignLabel = new TextBlock { Text = "Align Calendar Day (Optional)", Margin = new Thickness(0, 0, 0, 8), FontWeight = FontWeights.Bold };
        panel.Children.Add(alignLabel);

        var alignMonthLabel = new TextBlock { Text = "Month for alignment:", Margin = new Thickness(0, 0, 0, 4), FontSize = 11 };
        var alignMonthBox = new TextBox { Style = (Style)FindResource("DarkTextBox"), Text = cal.CurrentMonth.ToString(), Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(alignMonthLabel);
        panel.Children.Add(alignMonthBox);

        var alignDayLabel = new TextBlock { Text = "Day for alignment:", Margin = new Thickness(0, 0, 0, 4), FontSize = 11 };
        var alignDayBox = new TextBox { Style = (Style)FindResource("DarkTextBox"), Text = "1", Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(alignDayLabel);
        panel.Children.Add(alignDayBox);

        var alignDowLabel = new TextBlock { Text = "Should be day of week:", Margin = new Thickness(0, 0, 0, 4), FontSize = 11 };
        var alignDowBox = new ComboBox { Style = (Style)FindResource("DarkComboBox"), Margin = new Thickness(0, 0, 0, 8) };
        for (int i = 0; i < cal.Config.DayNames.Count; i++)
            alignDowBox.Items.Add(cal.Config.DayNames[i]);
        alignDowBox.SelectedIndex = 0;
        panel.Children.Add(alignDowLabel);
        panel.Children.Add(alignDowBox);

        var dlgWindow = new Window
        {
            Title = "Set Date & Time",
            Owner = Window.GetWindow(this),
            Width = 300,
            Height = 500,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (System.Windows.Media.Brush)FindResource("BrushBg")
        };

        var btnPanel = new StackPanel { Margin = new Thickness(12, 12, 12, 0) };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var okBtn = new Button { Content = "Set", Style = (Style)FindResource("GoldButton"), Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancelBtn = new Button { Content = "Cancel", Style = (Style)FindResource("GoldButton"), Width = 80 };
        okBtn.Click += (_, _) => { dlgWindow.DialogResult = true; dlgWindow.Close(); };
        cancelBtn.Click += (_, _) => { dlgWindow.DialogResult = false; dlgWindow.Close(); };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);
        btnPanel.Children.Add(btnRow);
        
        var mainPanel = new StackPanel();
        mainPanel.Children.Add(scrollPanel);
        mainPanel.Children.Add(btnPanel);
        dlgWindow.Content = mainPanel;

        if (dlgWindow.ShowDialog() != true) return;

        if (!int.TryParse(monthBox.Text, out int m) || m < 0 || m >= cal.Config.MonthCount)
        { MessageBox.Show("Invalid month.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!int.TryParse(dayBox.Text, out int d) || d < 1 || d > cal.Config.GetDaysInMonth(m))
        { MessageBox.Show("Invalid day for that month.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!int.TryParse(yearBox.Text, out int y))
        { MessageBox.Show("Invalid year.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!int.TryParse(hourBox.Text, out int h) || h < 0 || h >= cal.Config.HoursPerDay)
        { MessageBox.Show($"Hour must be 0–{cal.Config.HoursPerDay - 1}.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!int.TryParse(minuteBox.Text, out int min) || min < 0 || min > 59)
        { MessageBox.Show("Minute must be 0–59.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        string era = (eraBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(era))
        { MessageBox.Show("Era is required.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        cal.CurrentYear = y;
        cal.CurrentEra = era;
        cal.CurrentMonth = m;
        cal.CurrentDay = d;
        cal.CurrentHour = h;
        cal.CurrentMinute = min;

        // Apply day-of-week alignment if specified
        if (!string.IsNullOrWhiteSpace(alignMonthBox.Text) && !string.IsNullOrWhiteSpace(alignDayBox.Text))
        {
            if (int.TryParse(alignMonthBox.Text, out int alignMonth) && int.TryParse(alignDayBox.Text, out int alignDay) && alignDowBox.SelectedIndex >= 0)
            {
                if (alignMonth >= 0 && alignMonth < cal.Config.MonthCount && alignDay >= 1 && alignDay <= cal.Config.GetDaysInMonth(alignMonth))
                {
                    int targetDow = alignDowBox.SelectedIndex;
                    // Calculate offset: if day 1 is at DOW 0, and we want day D at DOW W,
                    // then offset = (W - ((D - 1) % DaysPerWeek) + DaysPerWeek) % DaysPerWeek
                    int currentDow = (0 + (alignDay - 1)) % cal.Config.DaysPerWeek;
                    int offset = (targetDow - currentDow + cal.Config.DaysPerWeek) % cal.Config.DaysPerWeek;
                    cal.Config.DayOfWeekOffset = offset;
                }
            }
        }

        cal.UpdateMoonCycles();
        RefreshCalendarView();
        PopulateEntryDateInputsFromCalendar();
    }

    private void BtnEditMonthDays_Click(object sender, RoutedEventArgs e)
    {
        var cal = GetDisplayCalendar();
        cal.Config.NormalizeDaysPerMonth();

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = "Enter days for each month, one per line (month name shown for reference):",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var editors = new List<(string Name, TextBox Box)>();
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 320 };
        var innerPanel = new StackPanel();
        for (int i = 0; i < cal.Config.MonthCount; i++)
        {
            string name = i < cal.Config.MonthNames.Count ? cal.Config.MonthNames[i] : $"Month {i + 1}";
            innerPanel.Children.Add(new TextBlock { Text = name + ":", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 2) });
            var tb = new TextBox
            {
                Style = (Style)FindResource("DarkTextBox"),
                Text = cal.Config.DaysPerMonth[i].ToString(),
                Margin = new Thickness(0, 0, 0, 4)
            };
            innerPanel.Children.Add(tb);
            editors.Add((name, tb));
        }
        scroll.Content = innerPanel;
        panel.Children.Add(scroll);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var okBtn = new Button { Content = "Save", Style = (Style)FindResource("GoldButton"), Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancelBtn = new Button { Content = "Cancel", Style = (Style)FindResource("GoldButton"), Width = 80 };
        panel.Children.Add(btnRow);

        var dlgWindow = new Window
        {
            Title = "Edit Days Per Month",
            Content = panel,
            Owner = Window.GetWindow(this),
            Width = 300,
            Height = 460,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (System.Windows.Media.Brush)FindResource("BrushBg")
        };

        okBtn.Click += (_, _) => { dlgWindow.DialogResult = true; dlgWindow.Close(); };
        cancelBtn.Click += (_, _) => { dlgWindow.DialogResult = false; dlgWindow.Close(); };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);

        if (dlgWindow.ShowDialog() != true) return;

        bool anyError = false;
        for (int i = 0; i < editors.Count; i++)
        {
            if (!int.TryParse(editors[i].Box.Text, out int days) || days < 1 || days > 365)
            {
                MessageBox.Show($"Invalid days for {editors[i].Name}. Must be 1–365.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                anyError = true;
                break;
            }
            cal.Config.DaysPerMonth[i] = days;
        }

        if (!anyError)
        {
            cal.InitializeDates();
            MessageBox.Show("Month days updated.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            RenderCalendarGrid();
        }
    }

    private void RefreshTimelineView()
    {
        TimelineList.Items.Clear();
        var allEvents = new List<(string CampaignId, string CampaignName, int Year, string Era, int Month, int Day, string DateStr, CalendarEvent Event)>();
        var campaigns = CalShowAllCampaigns.IsChecked == true
            ? _app.GetCampaigns()
            : new List<CampaignService> { _app.Campaign };

        foreach (var campaign in campaigns)
        {
            foreach (var calDate in campaign.Calendar.GetAllDatesWithEvents())
            {
                foreach (var evt in calDate.Events)
                {
                    allEvents.Add((
                        campaign.CampaignId,
                        campaign.CampaignName,
                        calDate.Year,
                        calDate.Era,
                        calDate.Month,
                        calDate.Day,
                        campaign.Calendar.GetDateDisplay(calDate.Year, calDate.Era, calDate.Month, calDate.Day),
                        evt));
                }
            }
        }

        var sorted = allEvents
            .OrderBy(x => x.Era, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Year)
            .ThenBy(x => x.Month)
            .ThenBy(x => x.Day)
            .ToList();

        if (sorted.Count == 0)
        {
            TimelineList.Items.Add("No timeline events yet");
            return;
        }

        foreach (var item in sorted)
        {
            var entry = $"[{item.DateStr}] {item.Event.EventType}: {item.Event.Title}";
            TimelineList.Items.Add(CreateHighlightedEventItem(
                campaignId: item.CampaignId,
                campaignName: item.CampaignName,
                text: entry));
        }
    }

    private DungeonMasterCortex.Models.Calendar GetDisplayCalendar()
    {
        string sourceId = (_app.Campaign.SharedCalendarSourceCampaignId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sourceId))
            return _app.Campaign.Calendar;

        var sourceCampaign = _app.GetCampaignById(sourceId);
        return sourceCampaign?.Calendar ?? _app.Campaign.Calendar;
    }

    private List<(string CampaignId, string CampaignName, CalendarEvent Event)> GetEventsForDisplayDate(int month, int day, int year, string era)
    {
        var result = new List<(string CampaignId, string CampaignName, CalendarEvent Event)>();

        if (CalShowAllCampaigns.IsChecked == true)
        {
            foreach (var campaign in _app.GetCampaigns())
            {
                var date = campaign.Calendar.GetDate(year, era, month, day);
                foreach (var evt in date.Events)
                    result.Add((campaign.CampaignId, campaign.CampaignName, evt));
            }
            return result;
        }

        var ownDate = _app.Campaign.Calendar.GetDate(year, era, month, day);
        foreach (var evt in ownDate.Events)
            result.Add((_app.Campaign.CampaignId, _app.Campaign.CampaignName, evt));

        return result;
    }

    private object CreateHighlightedEventItem(string campaignId, string campaignName, string text)
    {
        var border = new Border
        {
            Background = GetCampaignHighlightBrush(campaignId),
            BorderBrush = (Brush)FindResource("BrushBorder2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 0, 3),
            ToolTip = campaignName,
        };

        border.Child = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("BrushText")
        };

        return border;
    }

    private Brush GetCampaignHighlightBrush(string campaignId)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
            return new SolidColorBrush(Color.FromRgb(0xE8, 0xDC, 0xC0));

        if (_campaignColorBrushes.TryGetValue(campaignId, out var existing))
            return existing;

        Color[] palette =
        {
            Color.FromRgb(0xE7, 0xD5, 0xA8),
            Color.FromRgb(0xD9, 0xD5, 0xB5),
            Color.FromRgb(0xD3, 0xE0, 0xC0),
            Color.FromRgb(0xC8, 0xDD, 0xD8),
            Color.FromRgb(0xC7, 0xD3, 0xE4),
            Color.FromRgb(0xD5, 0xC8, 0xE2),
            Color.FromRgb(0xE2, 0xC7, 0xD2),
            Color.FromRgb(0xE4, 0xCF, 0xC1)
        };

        int idx = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(campaignId)) % palette.Length;
        var brush = new SolidColorBrush(palette[idx]);
        _campaignColorBrushes[campaignId] = brush;
        return brush;
    }

    private void RefreshCalendarSourceSelector()
    {
        _isUpdatingCalendarSourceSelector = true;
        CalCalendarSource.Items.Clear();
        CalCalendarSource.Items.Add(new CalendarSourceItem(string.Empty, "Own Calendar"));

        foreach (var campaign in _app.GetCampaigns())
        {
            if (string.Equals(campaign.CampaignId, _app.Campaign.CampaignId, StringComparison.OrdinalIgnoreCase))
                continue;
            CalCalendarSource.Items.Add(new CalendarSourceItem(campaign.CampaignId, campaign.CampaignName));
        }

        string selectedId = (_app.Campaign.SharedCalendarSourceCampaignId ?? string.Empty).Trim();
        int selectedIndex = 0;
        for (int i = 0; i < CalCalendarSource.Items.Count; i++)
        {
            if (CalCalendarSource.Items[i] is CalendarSourceItem item
                && string.Equals(item.CampaignId, selectedId, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }
        CalCalendarSource.SelectedIndex = selectedIndex;
        _isUpdatingCalendarSourceSelector = false;
    }

    private void CalCalendarSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingCalendarSourceSelector)
            return;
        if (CalCalendarSource.SelectedItem is not CalendarSourceItem selected)
            return;

        _app.Campaign.SharedCalendarSourceCampaignId = selected.CampaignId;
        RefreshCalendarView();
        PopulateEntryDateInputsFromCalendar();
    }

    private void CalShowAllCampaigns_Changed(object sender, RoutedEventArgs e)
    {
        RefreshCalendarView();
    }

    private sealed class CalendarSourceItem
    {
        public string CampaignId { get; }
        public string DisplayName { get; }

        public CalendarSourceItem(string campaignId, string displayName)
        {
            CampaignId = campaignId;
            DisplayName = displayName;
        }

        public override string ToString() => DisplayName;
    }

    private void BtnEditCalendarNames_Click(object sender, RoutedEventArgs e)
    {
        var cal = GetDisplayCalendar();
        var window = new Window
        {
            Title = "Edit Calendar Names",
            Width = 400,
            Height = 500,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
        };

        var mainPanel = new StackPanel { Margin = new(12) };

        // Month names
        var monthLabel = new TextBlock { Text = "Month Names (comma-separated):", FontWeight = FontWeights.Bold, Margin = new(0, 0, 0, 4) };
        mainPanel.Children.Add(monthLabel);
        var monthBox = new TextBox
        {
            Style = (Style)FindResource("DarkTextBox"),
            Text = string.Join(", ", cal.Config.MonthNames),
            Margin = new(0, 0, 0, 12),
            Height = 60,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        mainPanel.Children.Add(monthBox);

        // Day names
        var dayLabel = new TextBlock { Text = "Day Names (comma-separated):", FontWeight = FontWeights.Bold, Margin = new(0, 0, 0, 4) };
        mainPanel.Children.Add(dayLabel);
        var dayBox = new TextBox
        {
            Style = (Style)FindResource("DarkTextBox"),
            Text = string.Join(", ", cal.Config.DayNames),
            Margin = new(0, 0, 0, 12),
            Height = 60,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        mainPanel.Children.Add(dayBox);

        // Buttons
        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 12, 0, 0) };
        var okBtn = new Button { Content = "Save", Width = 100, Margin = new(0, 0, 8, 0) };
        okBtn.Click += (_, _) =>
        {
            var months = monthBox.Text.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();
            var days = dayBox.Text.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToList();

            if (months.Count > 0)
                cal.Config.MonthNames = months;
            if (days.Count > 0)
                cal.Config.DayNames = days;

            MessageBox.Show("Calendar names updated.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            window.Close();
            RenderCalendarGrid();
        };
        buttonPanel.Children.Add(okBtn);

        var cancelBtn = new Button { Content = "Cancel", Width = 100 };
        cancelBtn.Click += (_, _) => window.Close();
        buttonPanel.Children.Add(cancelBtn);

        mainPanel.Children.Add(buttonPanel);
        window.Content = new ScrollViewer { Content = mainPanel };
        window.ShowDialog();
    }
}
