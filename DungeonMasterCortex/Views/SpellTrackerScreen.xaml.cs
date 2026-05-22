using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public class SpellTrackerScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    private int _pendingCharacterIndex = -1;

    private CharacterSheet? _selectedCharacter;
    private readonly TextBlock _characterHeader;
    private readonly TextBlock _statusText;
    private readonly TextBlock _slotSummary;
    private readonly CheckBox _priestBypass;
    private readonly ComboBox _spellTypeFilter;
    private readonly ComboBox _spellbookFilter;
    private readonly ComboBox _sortFilter;
    private readonly ListBox _knownSpells;
    private readonly ListBox _preparedSpells;
    private readonly TextBox _tagEditor;
    private readonly CheckBox _universalTagScope;
    private readonly TextBlock _selectedSpellInfo;
    private readonly Button _prepareSelectedButton;
    private readonly Button _viewSpellCardButton;
    private readonly Button _addTagButton;
    private readonly Button _removeTagButton;
    private readonly Button _openDayDetailsButton;
    private readonly Button _addToSpellbookButton;

    private readonly List<SpellRow> _knownSpellRows = new();

    public UIElement View => this;

    public SpellTrackerScreen(MainWindow app)
    {
        _app = app;

        var root = new Grid { Margin = new Thickness(28, 16, 28, 28) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "SPELL TRACKER",
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(StyleProperty, "TitleText");
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var headerButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        headerButtons.Children.Add(CreateHeaderButton("SYNC CAMPAIGN DAY", 160, BtnSyncCampaignDay_Click));
        headerButtons.Children.Add(CreateHeaderButton("NEW DAY", 100, BtnNewDay_Click));
        headerButtons.Children.Add(CreateHeaderButton("SAVE", 90, BtnSave_Click));
        headerButtons.Children.Add(CreateHeaderButton("◀ HUB", 110, BtnBack_Click, isLast: true));
        Grid.SetColumn(headerButtons, 1);
        header.Children.Add(headerButtons);

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var infoPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 12) };
        _characterHeader = new TextBlock
        {
            Text = "Character: none selected"
        };
        _characterHeader.SetResourceReference(StyleProperty, "LabelText");
        infoPanel.Children.Add(_characterHeader);

        _statusText = new TextBlock
        {
            Text = "Open Spell Tracker from Character Roster to begin.",
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        _statusText.SetResourceReference(StyleProperty, "SubtitleText");
        infoPanel.Children.Add(_statusText);

        Grid.SetRow(infoPanel, 1);
        root.Children.Add(infoPanel);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 2);

        // Left side: spell source and actions
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = "Available Spells", Margin = new Thickness(0, 0, 0, 4) });

        _spellTypeFilter = new ComboBox { Margin = new Thickness(0, 0, 0, 8), MinWidth = 220 };
        _spellTypeFilter.Items.Add("All");
        _spellTypeFilter.Items.Add("Arcane");
        _spellTypeFilter.Items.Add("Divine");
        _spellTypeFilter.SelectedIndex = 0;
        _spellTypeFilter.SelectionChanged += (_, _) => RenderKnownSpells();
        left.Children.Add(_spellTypeFilter);

        var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

        _spellbookFilter = new ComboBox { Width = 220, Margin = new Thickness(0, 0, 8, 0) };
        _spellbookFilter.Items.Add("All Spellbooks");
        _spellbookFilter.SelectedIndex = 0;
        _spellbookFilter.SelectionChanged += (_, _) => RenderKnownSpells();
        filterRow.Children.Add(_spellbookFilter);

        _sortFilter = new ComboBox { Width = 150 };
        _sortFilter.Items.Add("Sort: Level");
        _sortFilter.Items.Add("Sort: Name");
        _sortFilter.Items.Add("Sort: Tags");
        _sortFilter.SelectedIndex = 0;
        _sortFilter.SelectionChanged += (_, _) => RenderKnownSpells();
        filterRow.Children.Add(_sortFilter);
        left.Children.Add(filterRow);

        _knownSpells = new ListBox
        {
            MinHeight = 340,
            Height = 430,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _knownSpells.SelectionChanged += KnownSpells_SelectionChanged;
        _knownSpells.MouseDoubleClick += KnownSpells_MouseDoubleClick;
        left.Children.Add(_knownSpells);

        var detailsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _viewSpellCardButton = CreateHeaderButton("VIEW CARD", 110, BtnViewCard_Click);
        _viewSpellCardButton.IsEnabled = false;
        detailsRow.Children.Add(_viewSpellCardButton);
        _selectedSpellInfo = new TextBlock
        {
            Text = "Select a spell to view details and manage tags.",
            Margin = new Thickness(10, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Width = 320
        };
        _selectedSpellInfo.SetResourceReference(StyleProperty, "SubtitleText");
        detailsRow.Children.Add(_selectedSpellInfo);
        left.Children.Add(detailsRow);

        var tagRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _tagEditor = new TextBox { Width = 230, Margin = new Thickness(0, 0, 8, 0) };
        tagRow.Children.Add(_tagEditor);
        _universalTagScope = new CheckBox
        {
            Content = "Universal",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        tagRow.Children.Add(_universalTagScope);
        _addTagButton = CreateHeaderButton("ADD TAG", 90, BtnAddTag_Click);
        _addTagButton.IsEnabled = false;
        _removeTagButton = CreateHeaderButton("REMOVE TAG", 110, BtnRemoveTag_Click);
        _removeTagButton.IsEnabled = false;
        tagRow.Children.Add(_addTagButton);
        tagRow.Children.Add(_removeTagButton);
        left.Children.Add(tagRow);

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _prepareSelectedButton = CreateHeaderButton("ADD TO PREPARED", 150, BtnPrepareSelected_Click);
        _addToSpellbookButton = CreateHeaderButton("ADD TO SPELLBOOK", 150, BtnAddToSpellbook_Click);
        _addToSpellbookButton.IsEnabled = false;
        actionRow.Children.Add(_prepareSelectedButton);
        actionRow.Children.Add(_addToSpellbookButton);
        left.Children.Add(actionRow);

        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        // Right side: tracking and notes
        var right = new StackPanel();

        _priestBypass = new CheckBox
        {
            Content = "Priest bypass memorization (cast from full list)",
            Margin = new Thickness(0, 0, 0, 8)
        };
        _priestBypass.Checked += PriestBypass_Changed;
        _priestBypass.Unchecked += PriestBypass_Changed;
        right.Children.Add(_priestBypass);

        _slotSummary = new TextBlock
        {
            Text = "Slots: -",
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        _slotSummary.SetResourceReference(StyleProperty, "SubtitleText");
        right.Children.Add(_slotSummary);

        right.Children.Add(new TextBlock { Text = "Prepared Spells (today)", Margin = new Thickness(0, 0, 0, 4) });
        _preparedSpells = new ListBox { MinHeight = 340, Height = 430, Margin = new Thickness(0, 0, 0, 8) };
        right.Children.Add(_preparedSpells);

        _openDayDetailsButton = CreateHeaderButton("OPEN CAST + NOTES", 170, BtnOpenDayDetails_Click);
        right.Children.Add(_openDayDetailsButton);

        Grid.SetColumn(right, 2);
        body.Children.Add(right);

        root.Children.Add(body);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root
        };
    }

    public void OnEnter()
    {
        BindCharacter(_app.PendingSpellTrackerCharacterIndex);
    }

    public void LoadCharacter(int characterIndex)
    {
        _pendingCharacterIndex = characterIndex;
        BindCharacter(characterIndex);
    }

    private void BindCharacter(int requestedIndex)
    {
        _selectedCharacter = null;
        int index = requestedIndex;
        if (index < 0)
            index = _pendingCharacterIndex;
        if (index < 0)
            index = _app.PendingSpellTrackerCharacterIndex;
        _app.PendingSpellTrackerCharacterIndex = -1;
        _pendingCharacterIndex = index;

        if (index >= 0 && index < _app.Characters.Count)
            _selectedCharacter = _app.Characters[index];

        if (_selectedCharacter is null)
        {
            _app.SetBanner("Character Blueprint  ›  Spell Tracker");
            _characterHeader.Text = "Character: none selected";
            _statusText.Text = "Select a character in Character Roster and open Spell Tracker from there.";
            _knownSpellRows.Clear();
            RenderKnownSpells();
            _preparedSpells.Items.Clear();
            _slotSummary.Text = "Slots: -";
            _prepareSelectedButton.IsEnabled = false;
            _priestBypass.IsEnabled = false;
            _viewSpellCardButton.IsEnabled = false;
            _addTagButton.IsEnabled = false;
            _removeTagButton.IsEnabled = false;
            _openDayDetailsButton.IsEnabled = false;
            _addToSpellbookButton.IsEnabled = false;
            _selectedSpellInfo.Text = "Select a spell to view details and manage tags.";
            _spellbookFilter.Items.Clear();
            _spellbookFilter.Items.Add("All Spellbooks");
            _spellbookFilter.SelectedIndex = 0;
            return;
        }

        _app.SetBanner($"Character Blueprint  ›  Spell Tracker  ›  {_selectedCharacter.Name}");
        EnsureSpellcastingDataUpToDate(_selectedCharacter);
        EnsureSpellTrackingInitialized(_selectedCharacter);

        _characterHeader.Text = BuildCharacterSummary(_selectedCharacter);
        _statusText.Text = "Select a spell, then use ADD TO PREPARED for wizard or cleric daily prep.";

        _priestBypass.IsEnabled = IsDivineCaster(_selectedCharacter);
        _priestBypass.IsChecked = _selectedCharacter.PriestMemorizationBypass;
        _prepareSelectedButton.IsEnabled = true;
        _openDayDetailsButton.IsEnabled = true;

        RefreshSpellbookFilter();
        BuildKnownSpellRows(_selectedCharacter);
        RenderKnownSpells();
        RenderTrackingState();
    }

    private static string BuildCharacterSummary(CharacterSheet c)
    {
        string race = string.IsNullOrWhiteSpace(c.RaceName) ? c.RaceId : c.RaceName;
        string cls = string.IsNullOrWhiteSpace(c.ClassName) ? c.ClassId : c.ClassName;
        return $"{c.Name}  |  Race: {race}  |  Class: {cls}  |  HP: {c.HitPoints}  |  AC: {c.ArmorClass}  |  THAC0: {c.Thac0}";
    }

    private void EnsureSpellcastingDataUpToDate(CharacterSheet character)
    {
        bool changed = false;

        character.ArcaneSpellSlots ??= new Dictionary<int, int>();
        character.DivineSpellSlots ??= new Dictionary<int, int>();
        character.WizardSpellbooks ??= new List<WizardSpellbook>();

        var arcaneBefore = new Dictionary<int, int>(character.ArcaneSpellSlots);
        var divineBefore = new Dictionary<int, int>(character.DivineSpellSlots);

        // Re-derive slots from class/level so pre-spellbook characters get missing higher-level slots.
        CharacterProgressionService.InitializeCharacterProgression(character, seedLevelRewards: false);

        if (!arcaneBefore.OrderBy(kv => kv.Key).SequenceEqual(character.ArcaneSpellSlots.OrderBy(kv => kv.Key))
            || !divineBefore.OrderBy(kv => kv.Key).SequenceEqual(character.DivineSpellSlots.OrderBy(kv => kv.Key)))
        {
            changed = true;
        }

        if (IsWizardClass(character) && character.WizardSpellbooks.Count == 0)
        {
            character.WizardSpellbooks.Add(SpellbookUtility.CreateSpellbook(SpellbookUtility.TYPE_STANDARD));
            changed = true;
        }

        foreach (var book in character.WizardSpellbooks)
        {
            bool bookChanged = false;

            if (string.IsNullOrWhiteSpace(book.Type))
            {
                book.Type = SpellbookUtility.TYPE_STANDARD;
                bookChanged = true;
            }

            if (SpellbookUtility.TryGetSpellbookInfo(book.Type, out int pages, out double weight, out string dimensions))
            {
                if (book.CapacityPages <= 0)
                {
                    book.CapacityPages = pages;
                    bookChanged = true;
                }

                if (book.WeightLbs <= 0)
                {
                    book.WeightLbs = weight;
                    bookChanged = true;
                }

                if (string.IsNullOrWhiteSpace(book.Dimensions))
                {
                    book.Dimensions = dimensions;
                    bookChanged = true;
                }
            }

            book.SpellPages ??= new Dictionary<string, int>();
            if (bookChanged)
                changed = true;
        }

        if (SyncWizardSpellbookIdsToBooks(character))
            changed = true;

        if (changed)
        {
            TouchCharacter(character);
            _app.SaveCharacters();
        }
    }

    private bool SyncWizardSpellbookIdsToBooks(CharacterSheet character)
    {
        character.WizardSpellbookIds ??= new List<string>();
        character.WizardSpellbooks ??= new List<WizardSpellbook>();

        if (character.WizardSpellbookIds.Count == 0)
            return false;

        if (character.WizardSpellbooks.Count == 0)
            character.WizardSpellbooks.Add(SpellbookUtility.CreateSpellbook(SpellbookUtility.TYPE_STANDARD));

        bool changed = false;

        foreach (string rawId in character.WizardSpellbookIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string spellId = rawId.Trim();
            bool alreadyTracked = character.WizardSpellbooks.Any(book =>
                book.SpellPages is not null && book.SpellPages.ContainsKey(spellId));
            if (alreadyTracked)
                continue;

            var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, spellId, StringComparison.OrdinalIgnoreCase));
            int spellLevel = 1;
            if (spell is not null)
            {
                if (spell.Level.Equals("Cantrip", StringComparison.OrdinalIgnoreCase))
                    spellLevel = 0;
                else if (int.TryParse(Regex.Replace(spell.Level, @"[^\d]", string.Empty), out int parsed))
                    spellLevel = Math.Max(0, parsed);
            }

            int pages = SpellbookUtility.GetDefaultPageCount(spellLevel);

            var targetBook = character.WizardSpellbooks.FirstOrDefault(book =>
                book.SpellPages is not null && SpellbookUtility.CanAddSpellToBook(book, pages));

            if (targetBook is null)
            {
                targetBook = SpellbookUtility.CreateSpellbook(
                    SpellbookUtility.TYPE_STANDARD,
                    $"Spellbook {character.WizardSpellbooks.Count + 1}");
                character.WizardSpellbooks.Add(targetBook);
            }

            targetBook.SpellPages ??= new Dictionary<string, int>();
            targetBook.SpellPages[spellId] = pages;
            changed = true;
        }

        return changed;
    }

    private void EnsureSpellTrackingInitialized(CharacterSheet character)
    {
        if (character.SpellTracking is null)
        {
            var calendar = _app.Campaign.Calendar;
            character.SpellTracking = new CharacterSpellTracking
            {
                CharacterId = character.Name,
                CharacterName = character.Name,
                CurrentDayTracking = new DailySpellTracking
                {
                    Year = calendar.CurrentYear,
                    Era = calendar.CurrentEra,
                    Month = calendar.CurrentMonth,
                    Day = calendar.CurrentDay
                }
            };
        }

        character.SpellTracking.CharacterName = character.Name;
        character.SpellTracking.DivineConfig ??= new DivineSpellConfiguration();
        character.SpellTracking.ArcaneConfig ??= new ArcaneSpellConfiguration();
        character.SpellTracking.DivineConfig.MaxSpellSlots = new Dictionary<int, int>(GetEffectiveSlots(character, isDivine: true));
        character.SpellTracking.ArcaneConfig.MaxSpellSlots = new Dictionary<int, int>(GetEffectiveSlots(character, isDivine: false));
        character.SpellTracking.DivineConfig.SelectedSpheres = GatherCharacterSpheres(character).ToList();
        character.SpellTracking.DivineConfig.BypassMemorization = character.PriestMemorizationBypass;

        if (character.SpellTracking.CurrentDayTracking.Year <= 0)
        {
            var calendar = _app.Campaign.Calendar;
            character.SpellTracking.CurrentDayTracking.Year = calendar.CurrentYear;
            character.SpellTracking.CurrentDayTracking.Era = calendar.CurrentEra;
            character.SpellTracking.CurrentDayTracking.Month = calendar.CurrentMonth;
            character.SpellTracking.CurrentDayTracking.Day = calendar.CurrentDay;
        }
    }

    private void BuildKnownSpellRows(CharacterSheet character)
    {
        _knownSpellRows.Clear();

        foreach (var book in character.WizardSpellbooks)
        {
            string source = string.IsNullOrWhiteSpace(book.Name) ? "Spellbook" : book.Name.Trim();
            foreach (var id in book.SpellPages.Keys)
                AddSpellRow(character, id, "Arcane", source, "From spellbook");
        }

        foreach (var list in character.WizardSpellLists)
        {
            string source = string.IsNullOrWhiteSpace(list.Name) ? "Spell List" : list.Name.Trim();
            foreach (var id in list.SpellIds)
                AddSpellRow(character, id, "Arcane", source, "From wizard list");
        }

        foreach (var id in character.TrackedSpellIds)
            AddSpellRow(character, id, "Arcane", "Tracked", "Tracked spell");

        if (IsDivineCaster(character))
        {
            var spheres = GatherCharacterSpheres(character);
            foreach (var spell in _app.Rules.Spells.Where(IsDivineSpell))
            {
                if (string.IsNullOrWhiteSpace(spell.Id))
                    continue;

                if (spheres.Count > 0 && !string.IsNullOrWhiteSpace(spell.Schools))
                {
                    bool sphereMatch = spheres.Any(s => ContainsToken(spell.Schools, s));
                    if (!sphereMatch)
                        continue;
                }

                AddSpellRow(character, spell.Id, "Divine", "Divine List", spell.BriefDescription ?? string.Empty);
            }
        }

        var merged = new Dictionary<string, SpellRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _knownSpellRows)
        {
            string key = $"{row.Type}|{row.Id}";
            if (!merged.TryGetValue(key, out var existing))
            {
                merged[key] = row;
                continue;
            }

            existing.Sources.UnionWith(row.Sources);
            foreach (var tag in row.Tags)
            {
                if (!existing.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    existing.Tags.Add(tag);
            }
        }

        _knownSpellRows.Clear();
        _knownSpellRows.AddRange(merged.Values);
    }

    private void RenderKnownSpells()
    {
        IEnumerable<SpellRow> rows = _knownSpellRows;

        string typeFilter = (_spellTypeFilter.SelectedItem as string ?? "All").Trim();
        if (!string.Equals(typeFilter, "All", StringComparison.OrdinalIgnoreCase))
            rows = rows.Where(r => string.Equals(r.Type, typeFilter, StringComparison.OrdinalIgnoreCase));

        string bookFilter = (_spellbookFilter.SelectedItem as string ?? "All Spellbooks").Trim();
        if (!string.Equals(bookFilter, "All Spellbooks", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(r =>
                !string.Equals(r.Type, "Arcane", StringComparison.OrdinalIgnoreCase)
                || r.Sources.Contains(bookFilter));
        }

        string sort = (_sortFilter.SelectedItem as string ?? "Sort: Level").Trim();
        rows = sort switch
        {
            "Sort: Name" => rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Level),
            "Sort: Tags" => rows.OrderBy(r => r.Tags.Count == 0 ? "~" : string.Join(",", r.Tags), StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderBy(r => r.Level).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
        };

        var snapshot = rows.ToList();
        _knownSpells.Items.Clear();
        foreach (var row in snapshot)
            _knownSpells.Items.Add(row);

        if (snapshot.Count == 0)
        {
            _viewSpellCardButton.IsEnabled = false;
            _addTagButton.IsEnabled = false;
            _removeTagButton.IsEnabled = false;
            _selectedSpellInfo.Text = "No spells match the current filters.";
        }
    }

    private void RenderTrackingState()
    {
        if (_selectedCharacter?.SpellTracking is null)
            return;

        var tracking = _selectedCharacter.SpellTracking;
        var day = tracking.CurrentDayTracking;

        var preparedRows = day.PreparedSpells
            .OrderBy(p => p.SpellLevel)
            .ThenBy(p => p.IsCast)
            .ThenBy(p => string.IsNullOrWhiteSpace(p.SpellId) ? p.SpellName : ResolveSpellDisplayName(p.SpellId), StringComparer.OrdinalIgnoreCase)
            .ToList();

        _preparedSpells.Items.Clear();
        foreach (var prepared in preparedRows)
        {
            string displayName = string.IsNullOrWhiteSpace(prepared.SpellId)
                ? prepared.SpellName
                : ResolveSpellDisplayName(prepared.SpellId);
            string castSuffix = prepared.IsCast ? " [CAST]" : string.Empty;
            string typeLabel = IsSpellIdDivine(prepared.SpellId) ? "Divine" : "Arcane";
            _preparedSpells.Items.Add(new PreparedSpellRow(prepared.SpellId, prepared.SpellLevel, $"L{prepared.SpellLevel} {displayName} [{typeLabel}]{castSuffix}"));
        }

        var arcaneMax = GetEffectiveSlots(_selectedCharacter, isDivine: false);
        var divineMax = GetEffectiveSlots(_selectedCharacter, isDivine: true);
        var arcaneRemain = tracking.GetAvailableSpellSlots(isDivine: false);
        var divineRemain = tracking.GetAvailableSpellSlots(isDivine: true);

        string FormatSlots(string label, Dictionary<int, int> max, Dictionary<int, int> remain)
        {
            if (max.Count == 0)
                return $"{label}: -";

            var ordered = max.Keys.OrderBy(k => k)
                .Select(level => $"L{level} {Math.Max(0, remain.GetValueOrDefault(level))}/{Math.Max(0, max.GetValueOrDefault(level))}");
            return $"{label}: {string.Join(", ", ordered)}";
        }

        int arcaneCantripCap = GetCantripCapacity(_selectedCharacter, isDivine: false);
        int divineCantripCap = GetCantripCapacity(_selectedCharacter, isDivine: true);
        int arcaneCantripUsed = CountTodayCantripCasts(isDivine: false);
        int divineCantripUsed = CountTodayCantripCasts(isDivine: true);

        _slotSummary.Text = string.Join("\n", new[]
        {
            FormatSlots("Arcane slots", arcaneMax, arcaneRemain),
            $"Arcane cantrips: {arcaneCantripUsed}/{arcaneCantripCap}",
            FormatSlots("Divine slots", divineMax, divineRemain),
            $"Divine cantrips: {divineCantripUsed}/{divineCantripCap}",
            $"Tracking date: {day.Year:D4} {day.Era} M{day.Month + 1} D{day.Day}"
        });

        _openDayDetailsButton.Content = $"OPEN CAST + NOTES ({day.CastSpells.Count})";
    }

    private void BtnPrepareSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter?.SpellTracking is null)
            return;

        if (_knownSpells.SelectedItem is not SpellRow row)
        {
            MessageBox.Show("Select a spell to prepare first.", "Spell Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!string.Equals(row.Type, "Divine", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(row.Type, "Arcane", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Preparation is only supported for arcane or divine spells.", "Spell Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (row.Level == 0)
        {
            bool isDivine = string.Equals(row.Type, "Divine", StringComparison.OrdinalIgnoreCase);
            int cantripCap = GetCantripCapacity(_selectedCharacter, isDivine: isDivine);
            int preppedCantrips = CountPreparedSpellsForLevel(isDivine, 0);
            if (preppedCantrips >= cantripCap)
            {
                MessageBox.Show("Cantrip preparation capacity is full (4 cantrips per 1st-level slot).", "Spell Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        bool preparingDivine = string.Equals(row.Type, "Divine", StringComparison.OrdinalIgnoreCase);
        var maxSlots = GetEffectiveSlots(_selectedCharacter, isDivine: preparingDivine);
        int maxForLevel = 0;
        if (row.Level > 0)
        {
            if (!maxSlots.TryGetValue(row.Level, out maxForLevel) || maxForLevel <= 0)
            {
                MessageBox.Show($"This character cannot cast level {row.Level} {row.Type.ToLowerInvariant()} spells yet.", "Spell Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (row.Level > 0)
        {
            int preparedForLevel = CountPreparedSpellsForLevel(preparingDivine, row.Level);
            if (preparedForLevel >= maxForLevel)
            {
                MessageBox.Show($"Level {row.Level} preparation slots are full ({preparedForLevel}/{maxForLevel}).", "Spell Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        _selectedCharacter.SpellTracking.AddPreparedSpell(row.Id, row.Name, row.Level, row.SchoolOrSphere);
        TouchCharacter(_selectedCharacter);
        _app.SaveCharacters();
        RenderTrackingState();
    }

    private void BtnRemovePrepared_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter?.SpellTracking is null)
            return;

        if (_preparedSpells.SelectedItem is not PreparedSpellRow selected)
        {
            MessageBox.Show("Select a prepared spell to remove first.", "Spell Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int index = _selectedCharacter.SpellTracking.CurrentDayTracking.PreparedSpells.FindIndex(p =>
            !p.IsCast && p.SpellLevel == selected.SpellLevel && string.Equals(p.SpellId, selected.SpellId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            MessageBox.Show("No matching uncast prepared spell was found.", "Spell Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _selectedCharacter.SpellTracking.CurrentDayTracking.PreparedSpells.RemoveAt(index);
        TouchCharacter(_selectedCharacter);
        _app.SaveCharacters();
        RenderTrackingState();
    }

    private void BtnOpenDayDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter?.SpellTracking is null)
            return;

        var day = _selectedCharacter.SpellTracking.CurrentDayTracking;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Prepared (reference), Cast Spells, and Day Notes",
            FontSize = 16,
            FontWeight = FontWeights.Bold
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var preparedPanel = new StackPanel();
        preparedPanel.Children.Add(new TextBlock
        {
            Text = "Prepared Spells",
            Margin = new Thickness(0, 0, 0, 4)
        });
        var preparedList = new ListBox { Height = 260 };
        foreach (var prepared in day.PreparedSpells
                     .OrderBy(p => p.SpellLevel)
                     .ThenBy(p => p.IsCast)
                     .ThenBy(p => string.IsNullOrWhiteSpace(p.SpellId) ? p.SpellName : ResolveSpellDisplayName(p.SpellId), StringComparer.OrdinalIgnoreCase))
        {
            string displayName = string.IsNullOrWhiteSpace(prepared.SpellId)
                ? prepared.SpellName
                : ResolveSpellDisplayName(prepared.SpellId);
            string castSuffix = prepared.IsCast ? " [CAST]" : string.Empty;
            string typeLabel = IsSpellIdDivine(prepared.SpellId) ? "Divine" : "Arcane";
            preparedList.Items.Add(new PreparedSpellRow(prepared.SpellId, prepared.SpellLevel, $"L{prepared.SpellLevel} {displayName} [{typeLabel}]{castSuffix}"));
        }
        preparedPanel.Children.Add(preparedList);
        Grid.SetColumn(preparedPanel, 0);
        columns.Children.Add(preparedPanel);

        var castPanel = new StackPanel();
        castPanel.Children.Add(new TextBlock
        {
            Text = "Cast Spells (today)",
            Margin = new Thickness(0, 0, 0, 4)
        });
        var castList = new ListBox { Height = 260 };
        foreach (var cast in day.CastSpells)
        {
            string displayName = string.IsNullOrWhiteSpace(cast.SpellId)
                ? cast.SpellName
                : ResolveSpellDisplayName(cast.SpellId);
            castList.Items.Add($"{cast.CastTime:HH:mm}  L{cast.SpellLevel} {displayName}");
        }
        castPanel.Children.Add(castList);

        var castActionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var castSelectedButton = new Button { Content = "CAST SELECTED", Width = 130, Margin = new Thickness(0, 0, 8, 0) };
        castSelectedButton.SetResourceReference(StyleProperty, "GoldButton");
        var undoLastCastButton = new Button { Content = "UNDO LAST CAST", Width = 130 };
        undoLastCastButton.SetResourceReference(StyleProperty, "GoldButton");
        castActionRow.Children.Add(castSelectedButton);
        castActionRow.Children.Add(undoLastCastButton);
        castPanel.Children.Add(castActionRow);
        Grid.SetColumn(castPanel, 2);
        columns.Children.Add(castPanel);

        Grid.SetRow(columns, 2);
        root.Children.Add(columns);

        var notesPanel = new StackPanel();
        notesPanel.Children.Add(new TextBlock { Text = "Day Notes", Margin = new Thickness(0, 0, 0, 4) });
        var notesBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 120,
            Text = day.SessionNotes ?? string.Empty
        };
        notesPanel.Children.Add(notesBox);
        Grid.SetRow(notesPanel, 4);
        root.Children.Add(notesPanel);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var saveButton = new Button { Content = "SAVE NOTES", Width = 120, Margin = new Thickness(0, 0, 8, 0) };
        saveButton.SetResourceReference(StyleProperty, "GoldButton");
        var closeButton = new Button { Content = "CLOSE", Width = 90 };
        closeButton.SetResourceReference(StyleProperty, "GoldButton");
        buttonRow.Children.Add(saveButton);
        buttonRow.Children.Add(closeButton);
        notesPanel.Children.Add(buttonRow);

        var dialog = new Window
        {
            Title = "Spell Day Details",
            Content = root,
            Owner = Window.GetWindow(this),
            Width = 860,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (System.Windows.Media.Brush)FindResource("BrushBg")
        };

        void RenderDayLists()
        {
            day = _selectedCharacter.SpellTracking.CurrentDayTracking;

            preparedList.Items.Clear();
            foreach (var prepared in day.PreparedSpells
                         .OrderBy(p => p.SpellLevel)
                         .ThenBy(p => p.IsCast)
                         .ThenBy(p => string.IsNullOrWhiteSpace(p.SpellId) ? p.SpellName : ResolveSpellDisplayName(p.SpellId), StringComparer.OrdinalIgnoreCase))
            {
                string displayName = string.IsNullOrWhiteSpace(prepared.SpellId)
                    ? prepared.SpellName
                    : ResolveSpellDisplayName(prepared.SpellId);
                string castSuffix = prepared.IsCast ? " [CAST]" : string.Empty;
                string typeLabel = IsSpellIdDivine(prepared.SpellId) ? "Divine" : "Arcane";
                preparedList.Items.Add(new PreparedSpellRow(prepared.SpellId, prepared.SpellLevel, $"L{prepared.SpellLevel} {displayName} [{typeLabel}]{castSuffix}"));
            }

            castList.Items.Clear();
            foreach (var cast in day.CastSpells)
            {
                string displayName = string.IsNullOrWhiteSpace(cast.SpellId)
                    ? cast.SpellName
                    : ResolveSpellDisplayName(cast.SpellId);
                castList.Items.Add($"{cast.CastTime:HH:mm}  L{cast.SpellLevel} {displayName}");
            }
        }

        castSelectedButton.Click += (_, _) =>
        {
            if (_selectedCharacter?.SpellTracking is null)
                return;

            if (preparedList.SelectedItem is not PreparedSpellRow selectedPrepared)
            {
                MessageBox.Show("Select a prepared spell to cast first.", "Spell Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int preparedIndex = _selectedCharacter.SpellTracking.CurrentDayTracking.PreparedSpells.FindIndex(p =>
                !p.IsCast && p.SpellLevel == selectedPrepared.SpellLevel && string.Equals(p.SpellId, selectedPrepared.SpellId, StringComparison.OrdinalIgnoreCase));
            if (preparedIndex < 0)
            {
                MessageBox.Show("No matching uncast prepared spell was found.", "Spell Tracker", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var prepared = _selectedCharacter.SpellTracking.CurrentDayTracking.PreparedSpells[preparedIndex];
            string spellName = string.IsNullOrWhiteSpace(prepared.SpellName)
                ? ResolveSpellDisplayName(prepared.SpellId)
                : prepared.SpellName;

            _selectedCharacter.SpellTracking.CastPreparedSpell(preparedIndex);
            _selectedCharacter.SpellTracking.CastSpell(prepared.SpellId, spellName, prepared.SpellLevel);

            TouchCharacter(_selectedCharacter);
            _app.SaveCharacters();

            RenderDayLists();
            RenderTrackingState();
        };

        undoLastCastButton.Click += (_, _) =>
        {
            if (TryUndoLastCast())
            {
                RenderDayLists();
                RenderTrackingState();
            }
        };

        saveButton.Click += (_, _) => SaveDayNotes(notesBox.Text ?? string.Empty);
        closeButton.Click += (_, _) => dialog.Close();
        dialog.Closed += (_, _) =>
        {
            // Persist latest notes on close even if user skipped explicit save.
            SaveDayNotes(notesBox.Text ?? string.Empty);
            RenderTrackingState();
        };

        RenderDayLists();
        dialog.ShowDialog();
    }

    private bool TryUndoLastCast()
    {
        if (_selectedCharacter?.SpellTracking is null)
            return false;

        var casts = _selectedCharacter.SpellTracking.CurrentDayTracking.CastSpells;
        if (casts.Count == 0)
            return false;

        var last = casts[^1];
        _selectedCharacter.SpellTracking.UncastSpell(casts.Count - 1);

        // If this was a prepared divine spell, mark one matching prepared slot as unused again.
        for (int i = _selectedCharacter.SpellTracking.CurrentDayTracking.PreparedSpells.Count - 1; i >= 0; i--)
        {
            var prepared = _selectedCharacter.SpellTracking.CurrentDayTracking.PreparedSpells[i];
            if (prepared.IsCast && string.Equals(prepared.SpellId, last.SpellId, StringComparison.OrdinalIgnoreCase))
            {
                prepared.IsCast = false;
                break;
            }
        }

        TouchCharacter(_selectedCharacter);
        _app.SaveCharacters();
        RenderTrackingState();
        return true;
    }

    private void BtnAddToSpellbook_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
            return;

        if (_knownSpells.SelectedItem is not SpellRow selectedSpell)
        {
            MessageBox.Show("Select a spell first.", "Add to Spellbook", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_selectedCharacter.WizardSpellbooks?.Count == 0)
        {
            MessageBox.Show("This character has no spellbooks.", "Add to Spellbook", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Find the spell definition to get accurate level and ID
        var spellDef = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, selectedSpell.Id, StringComparison.OrdinalIgnoreCase));
        if (spellDef is null)
        {
            MessageBox.Show($"Spell '{selectedSpell.Name}' not found in rules.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Convert spell level string (e.g., "1st", "2nd", "Cantrip") to numeric level
        int spellLevel = 0;
        if (spellDef.Level.Equals("Cantrip", StringComparison.OrdinalIgnoreCase))
        {
            spellLevel = 0;
        }
        else if (int.TryParse(System.Text.RegularExpressions.Regex.Replace(spellDef.Level, @"[^\d]", ""), out int parsedLevel))
        {
            spellLevel = parsedLevel;
        }
        else
        {
            MessageBox.Show($"Could not determine spell level for '{selectedSpell.Name}'.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Open the dialog for adding to spellbook
        if (_selectedCharacter.WizardSpellbooks == null || _selectedCharacter.WizardSpellbooks.Count == 0)
        {
            MessageBox.Show("No spellbooks available. This wizard needs a spellbook.", "No Spellbooks", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new AddSpellToSpellbookDialog(
            selectedSpell.Id,
            selectedSpell.Name,
            spellLevel,
            _selectedCharacter.WizardSpellbooks);

        if (dialog.Owner is null)
            dialog.Owner = Window.GetWindow(this);

        bool? result = dialog.ShowDialog();
        if (result == true && dialog.SelectedBook is not null)
        {
            // Add the spell to the selected spellbook
            SpellbookUtility.TryAddSpellToBook(dialog.SelectedBook, selectedSpell.Id, dialog.SelectedPageCount);
            
            TouchCharacter(_selectedCharacter);
            _app.SaveCharacters();

            RefreshSpellbookFilter();
            BuildKnownSpellRows(_selectedCharacter);
            RenderKnownSpells();

            MessageBox.Show(
                $"'{selectedSpell.Name}' added to '{dialog.SelectedBook.Name}' ({dialog.SelectedPageCount} pages).",
                "Spell Added",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void BtnSyncCampaignDay_Click(object sender, RoutedEventArgs e)
    {
        SyncTrackingDateToCampaign(resetDay: false);
    }

    private void BtnNewDay_Click(object sender, RoutedEventArgs e)
    {
        SyncTrackingDateToCampaign(resetDay: true);
    }

    private void SyncTrackingDateToCampaign(bool resetDay)
    {
        if (_selectedCharacter?.SpellTracking is null)
            return;

        var calendar = _app.Campaign.Calendar;
        if (resetDay)
        {
            _selectedCharacter.SpellTracking.ResetForNewDay(
                calendar.CurrentYear,
                calendar.CurrentEra,
                calendar.CurrentMonth,
                calendar.CurrentDay);
            _statusText.Text = "Started a new spell-tracking day from campaign calendar.";
        }
        else
        {
            _selectedCharacter.SpellTracking.CurrentDayTracking.Year = calendar.CurrentYear;
            _selectedCharacter.SpellTracking.CurrentDayTracking.Era = calendar.CurrentEra;
            _selectedCharacter.SpellTracking.CurrentDayTracking.Month = calendar.CurrentMonth;
            _selectedCharacter.SpellTracking.CurrentDayTracking.Day = calendar.CurrentDay;
            _selectedCharacter.SpellTracking.LastSyncedDate = DateTime.Now;
            _statusText.Text = "Synced current tracking date to campaign calendar.";
        }

        TouchCharacter(_selectedCharacter);
        _app.SaveCharacters();
        RenderTrackingState();
    }

    private void PriestBypass_Changed(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
            return;

        bool bypass = _priestBypass.IsChecked == true;
        _selectedCharacter.PriestMemorizationBypass = bypass;
        if (_selectedCharacter.SpellTracking?.DivineConfig is not null)
            _selectedCharacter.SpellTracking.DivineConfig.BypassMemorization = bypass;

        TouchCharacter(_selectedCharacter);
        _app.SaveCharacters();
    }

    private void SaveDayNotes(string notes)
    {
        if (_selectedCharacter?.SpellTracking is null)
            return;

        _selectedCharacter.SpellTracking.CurrentDayTracking.SessionNotes = notes ?? string.Empty;
        _selectedCharacter.SpellTracking.CurrentDayTracking.LastModified = DateTime.Now;
        _selectedCharacter.SpellTracking.LastModified = DateTime.Now;

        TouchCharacter(_selectedCharacter);
        _app.SaveCharacters();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
            return;

        TouchCharacter(_selectedCharacter);
        _app.SaveCharacters();
        _statusText.Text = "Spell tracker changes saved.";
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        _app.CloseSpellTrackerAndFocusCharacters();
    }

    private static void TouchCharacter(CharacterSheet character)
    {
        character.LastModified = DateTime.Now;
        character.Revision = Math.Max(1, character.Revision + 1);
    }

    private static int ParseSpellLevel(string value)
    {
        if (int.TryParse((value ?? string.Empty).Trim(), out int exact))
            return Math.Max(0, exact);

        var match = Regex.Match(value ?? string.Empty, "\\d+");
        if (match.Success && int.TryParse(match.Value, out int parsed))
            return Math.Max(0, parsed);

        return 0;
    }

    private static Dictionary<int, int> GetEffectiveSlots(CharacterSheet character, bool isDivine)
    {
        var slots = new Dictionary<int, int>();
        var source = isDivine ? character.DivineSpellSlots : character.ArcaneSpellSlots;

        foreach (var kv in source)
        {
            if (kv.Key >= 1 && kv.Value > 0)
                slots[kv.Key] = kv.Value;
        }

        if (isDivine && IsDivineCaster(character))
        {
            // Baseline fallback: only full priest casters should default to a 1st-level slot.
            // (Paladins/rangers often have delayed spellcasting and should not get this fallback.)
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

                // AD&D rule intent: bonus spells apply only to levels the caster can already cast.
                // Exception: level 1 can be granted when we use full-priest fallback.
                bool levelIsAvailable = slots.ContainsKey(spellLevel)
                    || (spellLevel == 1 && IsFullPriestCaster(character));
                if (!levelIsAvailable)
                    continue;

                slots[spellLevel] = Math.Max(0, slots.GetValueOrDefault(spellLevel)) + bonusSlots;
            }
        }

        return slots;
    }

    private static int GetCantripCapacity(CharacterSheet character, bool isDivine)
    {
        var slots = GetEffectiveSlots(character, isDivine);
        int levelOneSlots = Math.Max(0, slots.GetValueOrDefault(1));
        return levelOneSlots * 4;
    }

    private int CountTodayCantripCasts(bool isDivine)
    {
        if (_selectedCharacter?.SpellTracking is null)
            return 0;

        int total = 0;
        foreach (var cast in _selectedCharacter.SpellTracking.CurrentDayTracking.CastSpells)
        {
            if (cast.SpellLevel != 0)
                continue;

            var row = _knownSpellRows.FirstOrDefault(r => string.Equals(r.Id, cast.SpellId, StringComparison.OrdinalIgnoreCase));
            bool castIsDivine = row is not null
                ? string.Equals(row.Type, "Divine", StringComparison.OrdinalIgnoreCase)
                : IsSpellIdDivine(cast.SpellId);

            if (castIsDivine == isDivine)
                total++;
        }

        return total;
    }

    private int CountPreparedSpellsForLevel(bool isDivine, int level)
    {
        if (_selectedCharacter?.SpellTracking is null)
            return 0;

        int total = 0;
        foreach (var prepared in _selectedCharacter.SpellTracking.CurrentDayTracking.PreparedSpells)
        {
            if (prepared.SpellLevel != level)
                continue;

            bool preparedIsDivine = IsSpellIdDivine(prepared.SpellId);
            if (preparedIsDivine == isDivine)
                total++;
        }

        return total;
    }

    private bool IsSpellIdDivine(string spellId)
    {
        var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, spellId, StringComparison.OrdinalIgnoreCase));
        return spell is not null && IsDivineSpell(spell);
    }

    private bool IsCharacterWizard()
    {
        if (_selectedCharacter is null)
            return false;

        return IsWizardClass(_selectedCharacter);
    }

    private static bool IsWizardClass(CharacterSheet character)
    {
        string combined = BuildClassText(character);
        return combined.Contains("wizard", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("mage", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("illusionist", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetWisdomScore(CharacterSheet character)
    {
        if (character.Abilities.TryGetValue("wis", out int wis))
            return wis;
        return 10;
    }

    private static Dictionary<int, int> GetWisdomBonusSpellSlots(int wisdom)
    {
        // AD&D 2e-style bonus priest spells by Wisdom score.
        // Levels are keyed as spell level -> bonus slots.
        // For scores above 18, clamp to 18 table row unless a specific expanded table is introduced.
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

    private static bool IsDivineSpell(SpellDefinition spell)
    {
        string category = (spell.Category ?? string.Empty).Trim().ToLowerInvariant();
        return category.Contains("priest") || category.Contains("divine") || category.Contains("sphere");
    }

    private void RefreshSpellbookFilter()
    {
        _spellbookFilter.Items.Clear();
        _spellbookFilter.Items.Add("All Spellbooks");

        if (_selectedCharacter is not null)
        {
            foreach (var book in _selectedCharacter.WizardSpellbooks)
            {
                string name = string.IsNullOrWhiteSpace(book.Name) ? "Spellbook" : book.Name.Trim();
                if (!_spellbookFilter.Items.Contains(name))
                    _spellbookFilter.Items.Add(name);
            }
        }

        _spellbookFilter.SelectedIndex = 0;
    }

    private string ResolveSpellDisplayName(string? spellId)
    {
        string id = (spellId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id))
            return string.Empty;

        var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        return spell is null || string.IsNullOrWhiteSpace(spell.Name)
            ? id
            : spell.Name.Trim();
    }

    private void AddSpellRow(CharacterSheet character, string spellId, string type, string source, string fallbackSummary)
    {
        if (string.IsNullOrWhiteSpace(spellId))
            return;

        string id = spellId.Trim();
        var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        string name = spell?.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            name = id;
        int level = ParseSpellLevel(spell?.Level ?? string.Empty);

        string summary = spell?.BriefDescription ?? string.Empty;
        if (string.IsNullOrWhiteSpace(summary))
            summary = fallbackSummary;
        if (string.IsNullOrWhiteSpace(summary))
            summary = "No summary available.";

        var tags = GetTagsForSpell(id);
        var row = new SpellRow(
            id,
            name,
            level,
            type,
            spell?.Schools ?? string.Empty,
            summary,
            spell?.Description ?? string.Empty,
            spell?.CastTime ?? string.Empty,
            spell?.Duration ?? string.Empty,
            spell?.Range ?? string.Empty,
            spell?.Components ?? string.Empty,
            spell?.Materials ?? string.Empty,
            spell?.Area ?? string.Empty,
            spell?.Save ?? string.Empty,
            tags);
        row.Sources.Add(source);
        _knownSpellRows.Add(row);
    }

    private List<string> GetTagsForSpell(string spellId)
    {
        if (_selectedCharacter?.SpellTracking is null)
            return new List<string>();

        var tags = new List<string>();
        if (_selectedCharacter.SpellTracking.SpellUserTags.TryGetValue(spellId, out var localTags))
            tags.AddRange(localTags);
        tags.AddRange(_app.GetGlobalSpellTags(spellId));

        return tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void KnownSpells_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_knownSpells.SelectedItem is not SpellRow row)
        {
            _viewSpellCardButton.IsEnabled = false;
            _addTagButton.IsEnabled = false;
            _removeTagButton.IsEnabled = false;
            _addToSpellbookButton.IsEnabled = false;
            _selectedSpellInfo.Text = "Select a spell to view details and manage tags.";
            return;
        }

        _viewSpellCardButton.IsEnabled = true;
        _addTagButton.IsEnabled = true;
        _removeTagButton.IsEnabled = true;
        _addToSpellbookButton.IsEnabled = IsCharacterWizard();
        string tags = row.Tags.Count == 0 ? "(none)" : string.Join(", ", row.Tags);
        string pageTracking = GetSpellbookPageTrackingText(row.Id);
        _selectedSpellInfo.Text = $"{row.Name} (L{row.Level}, {row.Type})\nTags: {tags}\n{pageTracking}";
    }

    private string GetSpellbookPageTrackingText(string spellId)
    {
        if (_selectedCharacter?.WizardSpellbooks is null || _selectedCharacter.WizardSpellbooks.Count == 0)
            return "Spellbook Pages: no spellbooks";

        var matches = new List<string>();
        int totalPages = 0;

        foreach (var book in _selectedCharacter.WizardSpellbooks)
        {
            if (book.SpellPages is null)
                continue;

            if (book.SpellPages.TryGetValue(spellId, out int pages) && pages > 0)
            {
                string name = string.IsNullOrWhiteSpace(book.Name) ? "Spellbook" : book.Name.Trim();
                matches.Add($"{name}: {pages}");
                totalPages += pages;
            }
        }

        if (matches.Count == 0)
            return "Spellbook Pages: not recorded in a spellbook";

        return $"Spellbook Pages: {string.Join(" | ", matches)} (total {totalPages})";
    }

    private void KnownSpells_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_knownSpells.SelectedItem is SpellRow)
            ShowSelectedSpellCard();
    }

    private void BtnViewCard_Click(object sender, RoutedEventArgs e) => ShowSelectedSpellCard();

    private void ShowSelectedSpellCard()
    {
        if (_knownSpells.SelectedItem is not SpellRow row)
            return;

        var visibleRows = _knownSpells.Items.OfType<SpellRow>().ToList();
        if (visibleRows.Count == 0)
            return;

        int currentIndex = visibleRows.FindIndex(r =>
            string.Equals(r.Id, row.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Type, row.Type, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            currentIndex = 0;

        var panel = new StackPanel { Margin = new Thickness(12) };

        var titleBlock = new TextBlock
        {
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(titleBlock);

        var metaBlock = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(metaBlock);

        panel.Children.Add(new TextBlock { Text = "Summary:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
        var summaryBlock = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(summaryBlock);

        panel.Children.Add(new TextBlock { Text = "Description:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
        var descriptionBox = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 300
        };
        panel.Children.Add(descriptionBox);

        var navRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var prevBtn = new Button { Content = "◀ PREV", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        prevBtn.SetResourceReference(StyleProperty, "GoldButton");
        var nextBtn = new Button { Content = "NEXT ▶", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
        nextBtn.SetResourceReference(StyleProperty, "GoldButton");
        var closeBtn = new Button { Content = "CLOSE", Width = 90 };
        closeBtn.SetResourceReference(StyleProperty, "GoldButton");
        navRow.Children.Add(prevBtn);
        navRow.Children.Add(nextBtn);
        navRow.Children.Add(closeBtn);
        panel.Children.Add(navRow);

        var win = new Window
        {
            Title = "Spell Card",
            Content = panel,
            Owner = Window.GetWindow(this),
            Width = 700,
            Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (System.Windows.Media.Brush)FindResource("BrushBg")
        };

        void RenderCard()
        {
            var current = visibleRows[currentIndex];
            titleBlock.Text = $"{current.Name} (Level {current.Level}, {current.Type})";
            metaBlock.Text =
                $"Schools/Spheres: {BlankAsDash(current.SchoolOrSphere)}\n" +
                $"Cast Time: {BlankAsDash(current.CastTime)}\n" +
                $"Duration: {BlankAsDash(current.Duration)}\n" +
                $"Range: {BlankAsDash(current.Range)}\n" +
                $"Components: {BlankAsDash(current.Components)}\n" +
                $"Material Component: {BlankAsDash(current.Materials)}\n" +
                $"Area: {BlankAsDash(current.Area)}\n" +
                $"Save: {BlankAsDash(current.Save)}\n" +
                $"Spell {currentIndex + 1} of {visibleRows.Count}";
            summaryBlock.Text = BlankAsDash(current.Summary);
            descriptionBox.Text = BuildSpellDescriptionText(current);
            prevBtn.IsEnabled = currentIndex > 0;
            nextBtn.IsEnabled = currentIndex < visibleRows.Count - 1;
        }

        prevBtn.Click += (_, _) =>
        {
            if (currentIndex <= 0)
                return;
            currentIndex--;
            RenderCard();
        };

        nextBtn.Click += (_, _) =>
        {
            if (currentIndex >= visibleRows.Count - 1)
                return;
            currentIndex++;
            RenderCard();
        };

        closeBtn.Click += (_, _) => win.Close();

        RenderCard();
        win.ShowDialog();
    }

    private void BtnAddTag_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter?.SpellTracking is null || _knownSpells.SelectedItem is not SpellRow row)
            return;

        string input = (_tagEditor.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(input))
            return;

        var additions = input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (additions.Count == 0)
            return;

        bool changed = false;
        bool useUniversalTags = _universalTagScope.IsChecked == true;
        if (useUniversalTags)
        {
            foreach (var tag in additions)
                if (_app.AddGlobalSpellTag(row.Id, tag))
                    changed = true;
        }
        else
        {
            if (!_selectedCharacter.SpellTracking.SpellUserTags.TryGetValue(row.Id, out var tags))
            {
                tags = new List<string>();
                _selectedCharacter.SpellTracking.SpellUserTags[row.Id] = tags;
            }

            foreach (var tag in additions)
            {
                if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    tags.Add(tag);
                    changed = true;
                }
            }
        }

        _tagEditor.Text = string.Empty;
        if (changed)
        {
            TouchCharacter(_selectedCharacter);
            _app.SaveCharacters();
        }
        BuildKnownSpellRows(_selectedCharacter);
        RenderKnownSpells();
    }

    private void BtnRemoveTag_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter?.SpellTracking is null || _knownSpells.SelectedItem is not SpellRow row)
            return;

        string tagToRemove = (_tagEditor.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(tagToRemove))
        {
            MessageBox.Show("Enter a tag to remove.", "Spell Tags", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool changed = false;
        bool useUniversalTags = _universalTagScope.IsChecked == true;
        if (useUniversalTags)
        {
            changed = _app.RemoveGlobalSpellTag(row.Id, tagToRemove);
        }
        else if (_selectedCharacter.SpellTracking.SpellUserTags.TryGetValue(row.Id, out var tags))
        {
            int removed = tags.RemoveAll(t => string.Equals(t, tagToRemove, StringComparison.OrdinalIgnoreCase));
            changed = removed > 0;
            if (tags.Count == 0)
                _selectedCharacter.SpellTracking.SpellUserTags.Remove(row.Id);
        }

        _tagEditor.Text = string.Empty;
        if (changed)
        {
            TouchCharacter(_selectedCharacter);
            _app.SaveCharacters();
        }
        BuildKnownSpellRows(_selectedCharacter);
        RenderKnownSpells();
    }

    private static string BlankAsDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string BuildSpellDescriptionText(SpellRow row)
    {
        string components = BlankAsDash(row.Components);
        string materials = BlankAsDash(row.Materials);
        string body = string.IsNullOrWhiteSpace(row.FullDescription) ? "-" : row.FullDescription;
        return $"Components: {components}\nMaterial Component: {materials}\n\n{body}";
    }

    private static bool ContainsToken(string haystack, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var parts = (haystack ?? string.Empty)
            .Split(new[] { ',', ';', '/', '|'}, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Any(p => string.Equals(p, token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsArcaneCaster(CharacterSheet character)
    {
        string combined = BuildClassText(character);
        return combined.Contains("wizard", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("mage", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("illusionist", StringComparison.OrdinalIgnoreCase)
            || character.ArcaneSpellSlots.Count > 0
            || character.WizardSpellbooks.Count > 0
            || character.WizardSpellLists.Count > 0;
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

    private static bool IsFullPriestCaster(CharacterSheet character)
    {
        string combined = BuildClassText(character);
        return combined.Contains("priest", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("cleric", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("druid", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildClassText(CharacterSheet character)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(character.ClassId)) parts.Add(character.ClassId);
        if (!string.IsNullOrWhiteSpace(character.ClassName)) parts.Add(character.ClassName);
        parts.AddRange(character.ClassIds.Where(c => !string.IsNullOrWhiteSpace(c)));
        return string.Join(" ", parts);
    }

    private static HashSet<string> GatherCharacterSpheres(CharacterSheet character)
    {
        var spheres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in character.SelectedSpheres)
        {
            if (!string.IsNullOrWhiteSpace(kv.Key))
                spheres.Add(kv.Key.Trim());
        }

        foreach (var byClass in character.SpheresByClass.Values)
        {
            foreach (var sphere in byClass.Keys)
                if (!string.IsNullOrWhiteSpace(sphere))
                    spheres.Add(sphere.Trim());
        }

        return spheres;
    }

    private static Button CreateHeaderButton(string content, double width, RoutedEventHandler onClick, bool isLast = false, string style = "GoldButton")
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

    private sealed class SpellRow
    {
        public SpellRow(
            string id,
            string name,
            int level,
            string type,
            string schoolOrSphere,
            string summary,
            string fullDescription,
            string castTime,
            string duration,
            string range,
            string components,
            string materials,
            string area,
            string save,
            List<string> tags)
        {
            Id = id;
            Name = name;
            Level = level;
            Type = type;
            SchoolOrSphere = schoolOrSphere ?? string.Empty;
            Summary = summary ?? string.Empty;
            FullDescription = fullDescription ?? string.Empty;
            CastTime = castTime ?? string.Empty;
            Duration = duration ?? string.Empty;
            Range = range ?? string.Empty;
            Components = components ?? string.Empty;
            Materials = materials ?? string.Empty;
            Area = area ?? string.Empty;
            Save = save ?? string.Empty;
            Tags = tags ?? new List<string>();
        }

        public string Id { get; }
        public string Name { get; }
        public int Level { get; }
        public string Type { get; }
        public string SchoolOrSphere { get; }
        public string Summary { get; }
        public string FullDescription { get; }
        public string CastTime { get; }
        public string Duration { get; }
        public string Range { get; }
        public string Components { get; }
        public string Materials { get; }
        public string Area { get; }
        public string Save { get; }
        public List<string> Tags { get; }
        public HashSet<string> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override string ToString()
        {
            string school = string.IsNullOrWhiteSpace(SchoolOrSphere) ? string.Empty : $" [{SchoolOrSphere}]";
            string components = string.IsNullOrWhiteSpace(Components) ? "" : $"  Comp: {Components}";
            string material = string.IsNullOrWhiteSpace(Materials) ? "" : $"  Mtrl: {Materials}";
            string tags = Tags.Count == 0 ? "" : $"  Tags: {string.Join(",", Tags)}";
            string summary = string.IsNullOrWhiteSpace(Summary) ? "" : $"\n   {Summary}";
            return $"{Name} (L{Level}){school}{components}{material}{tags}{summary}";
        }
    }

    private sealed class PreparedSpellRow
    {
        public PreparedSpellRow(string spellId, int spellLevel, string display)
        {
            SpellId = spellId;
            SpellLevel = spellLevel;
            Display = display;
        }

        public string SpellId { get; }
        public int SpellLevel { get; }
        public string Display { get; }

        public override string ToString() => Display;
    }
}
