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

public partial class CharGenWizardSpellsScreen : UserControl, IScreen
{
    private const string AllLevels = "(All Levels)";
    private static SpellDetailPopup? _activeSpellPopup;

    private static void CloseSpellPopupSafely(SpellDetailPopup? popup)
    {
        if (popup is null)
            return;

        try
        {
            if (popup.IsLoaded)
                popup.Close();
        }
        catch
        {
            // Ignore close races from rapid repeated right-clicks.
        }
    }

    private void KnownSpellList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var item = ItemsControl.ContainerFromElement(KnownSpellList, dep) as ListBoxItem;
            if (item?.DataContext is SpellRow row)
            {
                ShowSpellDetailDialog(row.Spell);
                e.Handled = true;
            }
        }
    }

    private void ListSpellsList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var item = ItemsControl.ContainerFromElement(ListSpellsList, dep) as ListBoxItem;
            if (item?.DataContext is SpellRow row)
            {
                ShowSpellDetailDialog(row.Spell);
                e.Handled = true;
            }
        }
    }
    private const string AllSchools = "(All Schools)";

    private void AvailableSpellList_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var item = ItemsControl.ContainerFromElement(AvailableSpellList, dep) as ListBoxItem;
            if (item?.DataContext is SpellRow row)
            {
                ShowSpellDetailDialog(row.Spell);
                e.Handled = true;
            }
        }
    }

    private void ShowSpellDetailDialog(SpellDefinition spell)
    {
        if (_activeSpellPopup is not null)
            CloseSpellPopupSafely(_activeSpellPopup);

        string detail = BuildFullSpellDetail(spell);
        var popup = new SpellDetailPopup(detail);
        _activeSpellPopup = popup;
        var owner = Window.GetWindow(this);
        popup.Owner = owner;
        popup.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        MouseButtonEventHandler? ownerClickCloser = null;
        if (owner is not null)
        {
            ownerClickCloser = (_, me) =>
            {
                if (!popup.IsVisible)
                    return;

                // Keep right-click available to immediately open another spell description.
                if (me.ChangedButton == MouseButton.Left)
                    me.Handled = true;

                CloseSpellPopupSafely(popup);
            };

            owner.PreviewMouseDown += ownerClickCloser;
        }

        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeSpellPopup, popup))
                _activeSpellPopup = null;

            if (owner is null)
                return;

            if (ownerClickCloser is not null)
                owner.PreviewMouseDown -= ownerClickCloser;

            owner.Dispatcher.BeginInvoke(() =>
            {
                if (!owner.IsVisible)
                    return;

                owner.Activate();
                if (owner.Content is IInputElement ownerContent)
                    Keyboard.Focus(ownerContent);
            });
        };

        try
        {
            popup.Show();
        }
        catch
        {
            if (ReferenceEquals(_activeSpellPopup, popup))
                _activeSpellPopup = null;
        }
    }


    private string BuildFullSpellDetail(SpellDefinition spell)
    {
        // Compose a readable full spell detail string with improved formatting
        string damageHeal = $"Damage/Heal: {SpellDamageService.BuildDamageDisplay(spell, Math.Max(1, _app.CharGen.CharacterLevel))}";
        string brief = $"Brief: {spell.BriefDescription}";
        string description = spell.Description ?? "(no description)";
        // Normalize line endings
        string normalized = description.Replace("\r\n", "\n").Replace("\r", "\n");
        // Replace double line breaks (paragraphs) with triple for extra space
        string withExtraSpace = Regex.Replace(normalized, "\n{2,}", "\n\n\n");
        // If only single line breaks, treat them as paragraph breaks (add an extra)
        if (!withExtraSpace.Contains("\n\n\n"))
            withExtraSpace = withExtraSpace.Replace("\n", "\n\n");
        string formattedDescription = withExtraSpace.Trim();

        var lines = new List<string>
        {
            $"Name: {spell.Name}",
            $"Level: {spell.Level}",
            $"School(s): {spell.Schools}",
            $"Range: {spell.Range}",
            $"Duration: {spell.Duration}",
            $"Area: {spell.Area}",
            $"Components: {spell.Components}",
            $"Cast Time: {spell.CastTime}",
            $"Save: {spell.Save}",
            damageHeal,
            "", // Space after Damage/Heal
            brief,
            "", // Space after Brief
            formattedDescription
        };
        return string.Join("\n", lines.Where(l => l != null));
    }

    private static readonly Dictionary<int, int[]> WizardSpellLimitsByCharacterLevel = new()
    {
        [1] = new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0 },
        [2] = new[] { 2, 0, 0, 0, 0, 0, 0, 0, 0 },
        [3] = new[] { 2, 1, 0, 0, 0, 0, 0, 0, 0 },
        [4] = new[] { 3, 2, 0, 0, 0, 0, 0, 0, 0 },
        [5] = new[] { 4, 2, 1, 0, 0, 0, 0, 0, 0 },
        [6] = new[] { 4, 2, 2, 0, 0, 0, 0, 0, 0 },
        [7] = new[] { 4, 3, 2, 1, 0, 0, 0, 0, 0 },
        [8] = new[] { 4, 3, 3, 2, 0, 0, 0, 0, 0 },
        [9] = new[] { 4, 3, 3, 2, 1, 0, 0, 0, 0 },
        [10] = new[] { 4, 4, 3, 2, 2, 0, 0, 0, 0 },
        [11] = new[] { 4, 4, 4, 3, 3, 0, 0, 0, 0 },
        [12] = new[] { 4, 4, 4, 4, 4, 1, 0, 0, 0 },
        [13] = new[] { 5, 5, 5, 4, 4, 2, 0, 0, 0 },
        [14] = new[] { 5, 5, 5, 4, 4, 2, 1, 0, 0 },
        [15] = new[] { 5, 5, 5, 5, 5, 2, 1, 0, 0 },
        [16] = new[] { 5, 5, 5, 5, 5, 3, 2, 1, 0 },
        [17] = new[] { 5, 5, 5, 5, 5, 3, 3, 2, 0 },
        [18] = new[] { 5, 5, 5, 5, 5, 3, 3, 2, 1 },
        [19] = new[] { 5, 5, 5, 5, 5, 3, 3, 3, 1 },
        [20] = new[] { 5, 5, 5, 5, 5, 4, 3, 3, 2 },
    };

    private readonly MainWindow _app;
    private readonly Random _rng = new();

    private sealed class SpellRow
    {
        public string SpellId { get; init; } = string.Empty;
        public string LevelText { get; init; } = string.Empty;
        public string DisplayText { get; init; } = string.Empty;
        public string Schools { get; init; } = string.Empty;
        public SpellDefinition Spell { get; init; } = null!;
        public string SpellName => Spell.Name;
        public bool ShowLevelDivider { get; init; }
    }

    private sealed class PhysicalBookSpellItem
    {
        public string SpellName { get; init; } = string.Empty;
        public string PagesText { get; init; } = string.Empty;
    }

    private sealed class PhysicalBookItem
    {
        public WizardSpellbook Book { get; init; } = null!;
        public string Display { get; init; } = string.Empty;
        public override string ToString() => Display;
    }

    private List<SpellRow> _availableRows = new();
    private List<SpellRow> _knownRows = new();
    private List<SpellRow> _allAccessibleRows = new();
    private Dictionary<string, SpellDefinition> _spellsById = new(StringComparer.OrdinalIgnoreCase);
    private string _activeListName = string.Empty;

    public UIElement View => this;

    public CharGenWizardSpellsScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.CharGen.RecalculateLevelFromExistingExperience();

        if (!IsWizardCasterInCharGen())
        {
            _app.GoTo("chargen_review");
            return;
        }

        _app.SetBanner(_app.CharGen.IsLevelUpMode
            ? "Character Section  ›  Level Up  ›  Wizard Spells"
            : "Character Blueprint  ›  Wizard Spells");

        bool isPO = _app.CharGen.CharacterMode == "players_option";
        bool isWizardPO = isPO && string.Equals(_app.CharGen.ClassId, "wizard", StringComparison.OrdinalIgnoreCase);
        bool hasWizardSpecs = _app.Rules.Classes.TryGetValue("wizard", out var wc) && wc.Specializations is { Count: > 0 };
        int baseStepTotal = isPO ? (isWizardPO && hasWizardSpecs ? 13 : 12) : 10;
        int totalSteps = baseStepTotal + 1;
        int currentStep = totalSteps - 1;

        _app.SetNavBar(currentStep, totalSteps, "Wizard Spells",
            backAction: () => _app.GoTo("chargen_equipment", -1),
            nextAction: () => _app.GoTo("chargen_review"));

        SortByPicker.ItemsSource = new[] { "Level", "School", "Name" };
        SortByPicker.SelectedIndex = 0;

        _app.CharGen.WizardSpellbookIds ??= new List<string>();
        _app.CharGen.WizardSpellLists ??= new List<NamedSpellList>();
        _app.CharGen.WizardSpellLists = _app.CharGen.WizardSpellLists
            .Where(list => list is not null)
            .Select(list => new NamedSpellList
            {
                Name = list.Name ?? string.Empty,
                SpellIds = list.SpellIds ?? new List<string>()
            })
            .ToList();

        LearnRollResult.Text = string.Empty;
        UpdateLearnChanceText();
        RefreshAll();
    }

    private void RefreshAll()
    {
        BuildRows();
        RefreshDailySlotSummary();
        RefreshFilterPickers();
        RefreshAvailableList();
        RefreshKnownList();
        RefreshSpellLists();
        RefreshPhysicalBookSelector();
    }

    private void BuildRows()
    {
        _spellsById = _app.Rules.Spells
            .Where(spell => !string.IsNullOrWhiteSpace(spell.Id))
            .GroupBy(spell => spell.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var knownSet = _app.CharGen.WizardSpellbookIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allowedSchools = GetSelectedWizardSchools();
        var blockedSchools = GetWizardOppositionSchools();

        var wizardSpells = _app.Rules.Spells
            .Where(spell => string.Equals(spell.Category, "arcane", StringComparison.OrdinalIgnoreCase))
            .Where(spell => IsSpellLevelCastableNow(spell.Level))
            .Where(spell => IsWizardSpellAllowedForCharacter(spell, allowedSchools, blockedSchools))
            .Select(spell => new SpellRow
            {
                SpellId = spell.Id,
                LevelText = FormatSpellLevel(spell.Level),
                DisplayText = BuildSpellDisplay(spell),
                Schools = spell.Schools,
                Spell = spell,
            })
            .ToList();

        _allAccessibleRows = wizardSpells;

        _knownRows = wizardSpells
            .Where(row => knownSet.Contains(row.SpellId))
            .ToList();

        _availableRows = wizardSpells
            .Where(row => !knownSet.Contains(row.SpellId))
            .ToList();
    }

    private void RefreshFilterPickers()
    {
        string levelSelection = FilterLevelPicker.SelectedItem as string ?? AllLevels;
        string schoolSelection = FilterSchoolPicker.SelectedItem as string ?? AllSchools;

        var levelOptions = new List<string> { AllLevels };
        levelOptions.AddRange(_availableRows
            .Select(row => row.LevelText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        FilterLevelPicker.ItemsSource = levelOptions;
        FilterLevelPicker.SelectedItem = levelOptions.Contains(levelSelection, StringComparer.OrdinalIgnoreCase)
            ? levelSelection
            : AllLevels;

        var schoolOptions = new List<string> { AllSchools };
        schoolOptions.AddRange(BuildSchoolFilterOptions());
        FilterSchoolPicker.ItemsSource = schoolOptions;
        FilterSchoolPicker.SelectedItem = schoolOptions.Contains(schoolSelection, StringComparer.OrdinalIgnoreCase)
            ? schoolSelection
            : AllSchools;
    }

    private List<string> BuildSchoolFilterOptions()
    {
        // Primary source: schools explicitly selected for this wizard in character setup.
        var schools = GetSelectedWizardSchools();

        // Include schools present in already-known spells to support rare post-creation additions.
        foreach (var school in _knownRows.SelectMany(row => ParseSpellSchoolTokens(row.Schools)))
            schools.Add(school);

        // Fallback for legacy/incomplete state where selected schools were never captured.
        if (schools.Count == 0)
        {
            foreach (var school in _allAccessibleRows.SelectMany(row => ParseSpellSchoolTokens(row.Schools)))
                schools.Add(school);
        }

        return schools
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void RefreshAvailableList()
    {
        string search = SearchBox.Text?.Trim() ?? string.Empty;
        string sortBy = SortByPicker.SelectedItem as string ?? "Level";
        string selectedLevel = FilterLevelPicker.SelectedItem as string ?? AllLevels;
        string selectedSchool = FilterSchoolPicker.SelectedItem as string ?? AllSchools;

        IEnumerable<SpellRow> query = _availableRows;

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(row =>
                row.Spell.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Spell.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Spell.Schools.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(selectedLevel, AllLevels, StringComparison.OrdinalIgnoreCase))
            query = query.Where(row => string.Equals(row.LevelText, selectedLevel, StringComparison.OrdinalIgnoreCase));

        if (!string.Equals(selectedSchool, AllSchools, StringComparison.OrdinalIgnoreCase))
            query = query.Where(row => ParseSpellSchoolTokens(row.Schools).Contains(selectedSchool));

        query = sortBy switch
        {
            "Name" => query.OrderBy(row => row.Spell.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => ParseSpellLevel(row.Spell.Level))
                .ThenBy(row => row.Spell.Schools, StringComparer.OrdinalIgnoreCase),
            "School" => query.OrderBy(row => FirstSchoolToken(row.Spell.Schools), StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => ParseSpellLevel(row.Spell.Level))
                .ThenBy(row => row.Spell.Name, StringComparer.OrdinalIgnoreCase),
            _ => query.OrderBy(row => ParseSpellLevel(row.Spell.Level))
                .ThenBy(row => FirstSchoolToken(row.Spell.Schools), StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Spell.Name, StringComparer.OrdinalIgnoreCase),
        };

        var rows = query.ToList();
        AvailableSpellList.ItemsSource = ApplyLevelDividers(rows);
        AvailableSummary.Text = $"Showing {rows.Count} available arcane spells";
        BtnLearnSpell.IsEnabled = AvailableSpellList.SelectedItem is SpellRow;
    }

    private void RefreshKnownList()
    {
        var known = _knownRows
            .OrderBy(row => ParseSpellLevel(row.Spell.Level))
            .ThenBy(row => row.Spell.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        KnownSpellList.ItemsSource = ApplyLevelDividers(known);
        KnownSummary.Text = $"{known.Count} learned spell(s)";
        BtnUnlearnSpell.IsEnabled = KnownSpellList.SelectedItem is SpellRow;
    }

    private void RefreshSpellLists()
    {
        var names = _app.CharGen.WizardSpellLists
            .Where(list => list is not null)
            .Where(list => !string.IsNullOrWhiteSpace(list.Name))
            .Select(list => list.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(_activeListName)
            && !names.Contains(_activeListName, StringComparer.OrdinalIgnoreCase))
        {
            _activeListName = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(_activeListName) && names.Count > 0)
            _activeListName = names[0];

        SpellListsList.ItemsSource = names;
        SpellListsList.SelectedItem = string.IsNullOrWhiteSpace(_activeListName) ? null : _activeListName;
        RefreshSelectedListSpells();
        BtnAddToList.IsEnabled = !string.IsNullOrWhiteSpace(_activeListName) && KnownSpellList.SelectedItem is SpellRow;
        BtnRemoveFromList.IsEnabled = !string.IsNullOrWhiteSpace(_activeListName) && ListSpellsList.SelectedItem is SpellRow;
    }

    private void RefreshSelectedListSpells()
    {
        if (string.IsNullOrWhiteSpace(_activeListName))
        {
            ListSpellsList.ItemsSource = null;
            return;
        }

        var list = _app.CharGen.WizardSpellLists.FirstOrDefault(l =>
            string.Equals(l.Name, _activeListName, StringComparison.OrdinalIgnoreCase));

        if (list is null)
        {
            ListSpellsList.ItemsSource = null;
            return;
        }

        var rows = (list.SpellIds ?? new List<string>())
            .Select(id => _knownRows.FirstOrDefault(row => string.Equals(row.SpellId, id, StringComparison.OrdinalIgnoreCase)))
            .Where(row => row is not null)
            .Cast<SpellRow>()
            .ToList();

        ListSpellsList.ItemsSource = ApplyLevelDividers(rows);
    }

    private void BtnLearnSpell_Click(object sender, RoutedEventArgs e)
    {
        if (AvailableSpellList.SelectedItem is not SpellRow row)
            return;

        if (!IsSpellLevelCastableNow(row.Spell.Level))
        {
            int maxLevel = GetHighestCastableSpellLevel();
            LearnRollResult.Text = maxLevel <= 0
                ? $"Cannot learn {row.Spell.Name}: this character cannot cast wizard spells yet."
                : $"Cannot learn {row.Spell.Name}: only up to level {maxLevel} wizard spells are currently castable.";
            return;
        }

        int learnChance = GetLearnSpellChancePercent();
        int roll = ParseManualRoll(out bool usedManual);
        bool success = roll <= learnChance;

        if (!success && OverrideFailureCheck.IsChecked == true)
            success = true;

        string source = usedManual ? "manual" : "auto";
        if (!success)
        {
            LearnRollResult.Text = $"Failed to learn {row.Spell.Name}: roll {roll} ({source}) vs {learnChance}% chance.";
            return;
        }

        if (!_app.CharGen.WizardSpellbookIds.Any(id => string.Equals(id, row.SpellId, StringComparison.OrdinalIgnoreCase)))
            _app.CharGen.WizardSpellbookIds.Add(row.SpellId);

        LearnRollResult.Text = success && OverrideFailureCheck.IsChecked == true && roll > learnChance
            ? $"Override applied: {row.Spell.Name} learned (roll {roll} vs {learnChance}%)."
            : $"Learned {row.Spell.Name}: roll {roll} ({source}) vs {learnChance}% chance.";

        // Offer to add the spell to a physical spellbook (with page tracking).
        TryAddLearnedSpellToSpellbook(row.SpellId, row.Spell.Name, ParseSpellLevel(row.Spell.Level));

        RefreshAll();
    }

    private void BtnUnlearnSpell_Click(object sender, RoutedEventArgs e)
    {
        if (KnownSpellList.SelectedItem is not SpellRow row)
            return;

        _app.CharGen.WizardSpellbookIds = _app.CharGen.WizardSpellbookIds
            .Where(id => !string.Equals(id, row.SpellId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var list in _app.CharGen.WizardSpellLists)
        {
            list.SpellIds = (list.SpellIds ?? new List<string>())
                .Where(id => !string.Equals(id, row.SpellId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        LearnRollResult.Text = $"Removed {row.Spell.Name} from spellbook and named lists.";
        RefreshAll();
    }

    private CharacterSheet? GetCharacterForPhysicalBooks()
    {
        if (!_app.CharGen.IsLevelUpMode) return null;
        int idx = _app.CharGen.LevelUpCharacterIndex;
        if (idx < 0 || idx >= _app.Characters.Count) return null;
        return _app.Characters[idx];
    }

    private void RefreshPhysicalBookSelector()
    {
        var character = GetCharacterForPhysicalBooks();
        if (character is null)
        {
            PhysicalSpellbooksSection.Visibility = Visibility.Collapsed;
            return;
        }

        // Sync spellbook inventory items → WizardSpellbooks before showing the dropdown.
        if (SpellbookUtility.SyncSpellbooksFromInventory(character))
            _app.SaveCharacters();

        PhysicalSpellbooksSection.Visibility = character.WizardSpellbooks.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (character.WizardSpellbooks.Count == 0)
            return;

        string? prevName = (CmbPhysicalBook.SelectedItem as PhysicalBookItem)?.Book.Name;

        var items = character.WizardSpellbooks
            .Select(book =>
            {
                int used = book.SpellPages?.Values.Sum() ?? 0;
                int avail = book.GetAvailablePages();
                return new PhysicalBookItem
                {
                    Book = book,
                    Display = $"{book.Name} ({book.Type}) — {used}/{book.CapacityPages} pages used"
                };
            })
            .ToList();

        CmbPhysicalBook.ItemsSource = items;

        var restore = items.FirstOrDefault(i =>
            string.Equals(i.Book.Name, prevName, StringComparison.OrdinalIgnoreCase));
        CmbPhysicalBook.SelectedItem = restore ?? items.FirstOrDefault();
    }

    private void RefreshPhysicalBookSpells()
    {
        if (CmbPhysicalBook.SelectedItem is not PhysicalBookItem item)
        {
            PhysicalBookStats.Text = string.Empty;
            PhysicalBookSpellList.ItemsSource = null;
            BtnCopyToBook.IsEnabled = false;
            return;
        }

        var book = item.Book;
        book.SpellPages ??= new Dictionary<string, int>();

        int usedPages = book.SpellPages.Values.Sum();
        int availPages = book.GetAvailablePages();
        PhysicalBookStats.Text = $"{usedPages} pages used  ·  {availPages} pages remaining of {book.CapacityPages} total";

        var entries = book.SpellPages
            .Select(kv =>
            {
                string name = _spellsById.TryGetValue(kv.Key, out var s) ? s.Name : kv.Key;
                return new PhysicalBookSpellItem
                {
                    SpellName = name,
                    PagesText = $"{kv.Value}p"
                };
            })
            .OrderBy(e => e.SpellName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PhysicalBookSpellList.ItemsSource = entries;
        BtnCopyToBook.IsEnabled = KnownSpellList.SelectedItem is SpellRow;
    }

    private void CmbPhysicalBook_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshPhysicalBookSpells();
    }

    private void BtnCopyToBook_Click(object sender, RoutedEventArgs e)
    {
        if (KnownSpellList.SelectedItem is not SpellRow row)
            return;

        var character = GetCharacterForPhysicalBooks();
        if (character is null)
            return;

        character.WizardSpellbooks ??= new List<WizardSpellbook>();
        if (character.WizardSpellbooks.Count == 0)
        {
            MessageBox.Show("This character has no physical spellbooks.", "Copy to Spellbook",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool alreadyIn = character.WizardSpellbooks.Any(b =>
            b.SpellPages is not null && b.SpellPages.ContainsKey(row.SpellId));
        if (alreadyIn)
        {
            MessageBox.Show($"'{row.Spell.Name}' is already recorded in one of this character's spellbooks.",
                "Already Recorded", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int spellLevel = ParseSpellLevel(row.Spell.Level);
        var dialog = new AddSpellToSpellbookDialog(row.SpellId, row.Spell.Name, spellLevel, character.WizardSpellbooks);
        dialog.Owner = Window.GetWindow(this);
        bool? result = dialog.ShowDialog();

        if (result == true && dialog.SelectedBook is not null)
        {
            SpellbookUtility.TryAddSpellToBook(dialog.SelectedBook, row.SpellId, dialog.SelectedPageCount);
            _app.SaveCharacters();
            RefreshPhysicalBookSelector();
            RefreshPhysicalBookSpells();
        }
    }

    private void TryAddLearnedSpellToSpellbook(string spellId, string spellName, int spellLevel)    {
        if (!_app.CharGen.IsLevelUpMode)
            return;

        int idx = _app.CharGen.LevelUpCharacterIndex;
        if (idx < 0 || idx >= _app.Characters.Count)
            return;

        var character = _app.Characters[idx];
        character.WizardSpellbooks ??= new List<WizardSpellbook>();

        if (character.WizardSpellbooks.Count == 0)
        {
            var result = MessageBox.Show(
                $"'{spellName}' was learned but no spellbooks exist. Create a default Standard spellbook?",
                "Add to Spellbook",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                character.WizardSpellbooks.Add(SpellbookUtility.CreateSpellbook(SpellbookUtility.TYPE_STANDARD));
            else
                return;
        }

        // Check if already in a spellbook
        bool alreadyInBook = character.WizardSpellbooks.Any(b =>
            b.SpellPages is not null && b.SpellPages.ContainsKey(spellId));
        if (alreadyInBook)
            return;

        var dialog = new AddSpellToSpellbookDialog(spellId, spellName, spellLevel, character.WizardSpellbooks);
        dialog.Owner = Window.GetWindow(this);
        bool? dialogResult = dialog.ShowDialog();

        if (dialogResult == true && dialog.SelectedBook is not null)
        {
            SpellbookUtility.TryAddSpellToBook(dialog.SelectedBook, spellId, dialog.SelectedPageCount);
            _app.SaveCharacters();
        }
    }

    private void BtnCreateList_Click(object sender, RoutedEventArgs e)
    {
        string name = (NewListNameBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter a list name first.", "Spell Lists", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_app.CharGen.WizardSpellLists.Any(list => string.Equals(list?.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show("That spell list name already exists.", "Spell Lists", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _app.CharGen.WizardSpellLists.Add(new NamedSpellList { Name = name, SpellIds = new List<string>() });
        _activeListName = name;
        NewListNameBox.Text = string.Empty;
        RefreshSpellLists();
    }

    private void BtnRemoveList_Click(object sender, RoutedEventArgs e)
    {
        if (SpellListsList.SelectedItem is not string selectedName)
            return;

        _app.CharGen.WizardSpellLists = _app.CharGen.WizardSpellLists
            .Where(list => !string.Equals(list.Name, selectedName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _activeListName = string.Empty;
        RefreshSpellLists();
    }

    private void BtnAddToList_Click(object sender, RoutedEventArgs e)
    {
        if (KnownSpellList.SelectedItem is not SpellRow known || string.IsNullOrWhiteSpace(_activeListName))
            return;

        var list = _app.CharGen.WizardSpellLists.FirstOrDefault(l =>
            string.Equals(l.Name, _activeListName, StringComparison.OrdinalIgnoreCase));
        if (list is null)
            return;

        list.SpellIds ??= new List<string>();

        int spellLevel = ParseSpellLevel(known.Spell.Level);
        var slotsByLevel = GetDailyWizardSlotsByLevel();
        int levelSlots = slotsByLevel.TryGetValue(spellLevel, out int slots) ? slots : 0;
        if (levelSlots <= 0)
        {
            MessageBox.Show(
                $"You currently have 0 daily slots for level {spellLevel} wizard spells.",
                "Spell Lists",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int currentAtLevel = list.SpellIds
            .Select(id => _spellsById.TryGetValue(id, out var spell) ? spell : null)
            .Where(spell => spell is not null)
            .Cast<SpellDefinition>()
            .Count(spell => ParseSpellLevel(spell.Level) == spellLevel);

        if (currentAtLevel >= levelSlots)
        {
            MessageBox.Show(
                $"{list.Name} already has the maximum number of level {spellLevel} spells ({currentAtLevel}/{levelSlots}).",
                "Spell Lists",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        list.SpellIds.Add(known.SpellId);

        RefreshSelectedListSpells();
    }

    private void BtnRemoveFromList_Click(object sender, RoutedEventArgs e)
    {
        if (ListSpellsList.SelectedItem is not SpellRow selected || string.IsNullOrWhiteSpace(_activeListName))
            return;

        var list = _app.CharGen.WizardSpellLists.FirstOrDefault(l =>
            string.Equals(l.Name, _activeListName, StringComparison.OrdinalIgnoreCase));
        if (list is null)
            return;

        var ids = list.SpellIds ?? new List<string>();
        int removeIndex = ids.FindIndex(id => string.Equals(id, selected.SpellId, StringComparison.OrdinalIgnoreCase));
        if (removeIndex >= 0)
            ids.RemoveAt(removeIndex);
        list.SpellIds = ids;

        RefreshSelectedListSpells();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshAvailableList();
    private void SortByPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshAvailableList();
    private void FilterLevelPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshAvailableList();
    private void FilterSchoolPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshAvailableList();

    private void AvailableSpellList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnLearnSpell.IsEnabled = AvailableSpellList.SelectedItem is SpellRow;
    }

    private void KnownSpellList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnUnlearnSpell.IsEnabled = KnownSpellList.SelectedItem is SpellRow;
        BtnAddToList.IsEnabled = !string.IsNullOrWhiteSpace(_activeListName) && KnownSpellList.SelectedItem is SpellRow;
        BtnCopyToBook.IsEnabled = KnownSpellList.SelectedItem is SpellRow
                                  && CmbPhysicalBook.SelectedItem is PhysicalBookItem;
    }

    private void AvailableSpellList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AvailableSpellList.SelectedItem is SpellRow)
            BtnLearnSpell_Click(sender, e);
    }

    private void SpellListsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _activeListName = SpellListsList.SelectedItem as string ?? string.Empty;
        RefreshSelectedListSpells();
        BtnAddToList.IsEnabled = !string.IsNullOrWhiteSpace(_activeListName) && KnownSpellList.SelectedItem is SpellRow;
        BtnRemoveFromList.IsEnabled = !string.IsNullOrWhiteSpace(_activeListName) && ListSpellsList.SelectedItem is SpellRow;
    }

    private void ListSpellsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnRemoveFromList.IsEnabled = !string.IsNullOrWhiteSpace(_activeListName) && ListSpellsList.SelectedItem is SpellRow;
    }

    private bool IsWizardCasterInCharGen()
    {
        return GetWizardClassIdsInCharGen().Any();
    }

    private IEnumerable<string> GetWizardClassIdsInCharGen()
    {
        var classIds = _app.CharGen.SelectedClassIds.Count > 0
            ? _app.CharGen.SelectedClassIds
            : string.IsNullOrWhiteSpace(_app.CharGen.ClassId)
                ? new List<string>()
                : new List<string> { _app.CharGen.ClassId };

        return classIds
            .Where(IsWizardClassId)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsWizardClassId(string classId)
    {
        string token = (classId ?? string.Empty).Trim();
        return token.Equals("wizard", StringComparison.OrdinalIgnoreCase)
            || token.Equals("mage", StringComparison.OrdinalIgnoreCase)
            || token.Equals("illusionist", StringComparison.OrdinalIgnoreCase);
    }

    private HashSet<string> GetSelectedWizardSchools()
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (school, isSelected) in _app.CharGen.SelectedWizardSchools)
        {
            if (!isSelected)
                continue;

            string normalized = NormalizeWizardSchoolName(school);
            if (!string.IsNullOrWhiteSpace(normalized))
                selected.Add(normalized);
        }

        foreach (string classId in GetWizardClassIdsInCharGen())
        {
            if (!_app.CharGen.SchoolsByClass.TryGetValue(classId, out var classSchools))
                continue;

            foreach (var (school, isSelected) in classSchools)
            {
                if (!isSelected)
                    continue;

                string normalized = NormalizeWizardSchoolName(school);
                if (!string.IsNullOrWhiteSpace(normalized))
                    selected.Add(normalized);
            }
        }

        return selected;
    }

    private HashSet<string> GetWizardOppositionSchools()
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!_app.Rules.Classes.TryGetValue("wizard", out var wizardClass) || wizardClass.Specializations is null)
            return blocked;

        var specializationIds = new List<string>();
        if (!string.IsNullOrWhiteSpace(_app.CharGen.WizardSpecializationId))
            specializationIds.Add(_app.CharGen.WizardSpecializationId);

        foreach (string classId in GetWizardClassIdsInCharGen())
        {
            if (!_app.CharGen.WizardSpecializationById.TryGetValue(classId, out var id)
                || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(id))
                specializationIds.Add(id);
        }

        foreach (string specId in specializationIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var spec = wizardClass.Specializations.FirstOrDefault(s =>
                string.Equals(s.Id, specId, StringComparison.OrdinalIgnoreCase));
            if (spec is null)
                continue;

            foreach (string school in spec.OppositionSchools)
            {
                string normalized = NormalizeWizardSchoolName(school);
                if (!string.IsNullOrWhiteSpace(normalized))
                    blocked.Add(normalized);
            }
        }

        return blocked;
    }

    private static bool IsWizardSpellAllowedForCharacter(
        SpellDefinition spell,
        HashSet<string> allowedSchools,
        HashSet<string> blockedSchools)
    {
        var spellSchools = ParseSpellSchoolTokens(spell.Schools);
        if (spellSchools.Count == 0)
            return true;

        var permittedSchools = spellSchools
            .Where(school => !blockedSchools.Contains(school))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (permittedSchools.Count == 0)
            return false;

        if (allowedSchools.Count == 0)
            return true;

        return permittedSchools.Any(allowedSchools.Contains);
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

            string normalized = NormalizeWizardSchoolName(raw);
            if (!string.IsNullOrWhiteSpace(normalized))
                tokens.Add(normalized);
        }

        return tokens;
    }

    private static string NormalizeWizardSchoolName(string school)
    {
        string token = (school ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        if (token.Equals("Greater Divination", StringComparison.OrdinalIgnoreCase))
            return "Divination";

        return token;
    }

    private static string BuildSpellDisplay(SpellDefinition spell)
    {
        string schools = string.IsNullOrWhiteSpace(spell.Schools) ? "No school" : spell.Schools;
        return $"{spell.Name}  [{schools}]";
    }

    private static string FormatSpellLevel(string level)
        => int.TryParse(level, out int parsed) ? $"L{parsed}" : level;

    private static int ParseSpellLevel(string level)
        => int.TryParse(level, out int parsed) ? parsed : int.MaxValue;

    private static string FirstSchoolToken(string schools)
    {
        var tokens = ParseSpellSchoolTokens(schools);
        return tokens.Count == 0 ? "~" : tokens.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First();
    }

    private List<SpellRow> ApplyLevelDividers(IEnumerable<SpellRow> rows)
    {
        int? previousLevel = null;
        var output = new List<SpellRow>();
        foreach (var row in rows)
        {
            int currentLevel = ParseSpellLevel(row.Spell.Level);
            bool showDivider = !previousLevel.HasValue || previousLevel.Value != currentLevel;
            output.Add(new SpellRow
            {
                SpellId = row.SpellId,
                LevelText = row.LevelText,
                DisplayText = row.DisplayText,
                Schools = row.Schools,
                Spell = row.Spell,
                ShowLevelDivider = showDivider,
            });
            previousLevel = currentLevel;
        }

        return output;
    }

    private int GetSpellbookLimitForLevel(int spellLevel)
    {
        if (spellLevel < 1 || spellLevel > 9)
            return 0;

        int characterLevel = Math.Clamp(_app.CharGen.CharacterLevel, 1, 20);
        if (!WizardSpellLimitsByCharacterLevel.TryGetValue(characterLevel, out var limits)
            || limits.Length < spellLevel)
        {
            return 0;
        }

        return Math.Max(0, limits[spellLevel - 1]);
    }

    private int CountKnownArcaneSpellsAtLevel(int spellLevel)
    {
        if (spellLevel < 1 || spellLevel > 9)
            return 0;

        int count = 0;
        foreach (string spellId in _app.CharGen.WizardSpellbookIds)
        {
            if (string.IsNullOrWhiteSpace(spellId)
                || !_spellsById.TryGetValue(spellId, out var spell)
                || !string.Equals(spell.Category, "arcane", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ParseSpellLevel(spell.Level) == spellLevel)
                count++;
        }

        return count;
    }

    private int GetLearnSpellChancePercent()
    {
        var totals = SubAbilityTables.CalculateTotals(_app.CharGen.SubAbilities, _app.CharGen.ExceptionalStrength, _app.CharGen.ClassId);
        if (totals.LearnSpellsPercent > 0)
            return Math.Clamp(totals.LearnSpellsPercent, 1, 100);

        int intelligence = _app.CharGen.ModifiedAbilities.GetValueOrDefault("int", _app.CharGen.Abilities.GetValueOrDefault("int", 10));
        return GetLearnChanceFromIntelligence(intelligence);
    }

    private void RefreshDailySlotSummary()
    {
        var slots = GetDailyWizardSlotsByLevel();
        if (slots.Count == 0)
        {
            DailySlotsSummary.Text = "Daily wizard slots: none available at current level.";
            return;
        }

        string summary = string.Join(", ",
            slots.OrderBy(kv => kv.Key).Select(kv => $"L{kv.Key}: {kv.Value}"));
        DailySlotsSummary.Text = $"Daily wizard slots: {summary}";
    }

    private Dictionary<int, int> GetDailyWizardSlotsByLevel()
    {
        int characterLevel = Math.Clamp(_app.CharGen.CharacterLevel, 1, 20);
        if (!WizardSpellLimitsByCharacterLevel.TryGetValue(characterLevel, out var slots))
            return new Dictionary<int, int>();

        var result = new Dictionary<int, int>();
        for (int i = 0; i < slots.Length; i++)
        {
            int value = Math.Max(0, slots[i]);
            if (value > 0)
                result[i + 1] = value;
        }

        return result;
    }

    private int GetHighestCastableSpellLevel()
    {
        var slots = GetDailyWizardSlotsByLevel();
        return slots.Keys.DefaultIfEmpty(0).Max();
    }

    private bool IsSpellLevelCastableNow(string levelText)
    {
        int spellLevel = ParseSpellLevel(levelText);
        if (spellLevel <= 0 || spellLevel == int.MaxValue)
            return false;

        return spellLevel <= GetHighestCastableSpellLevel();
    }

    private static int GetLearnChanceFromIntelligence(int intelligence)
    {
        if (intelligence <= 8) return 0;
        if (intelligence == 9) return 35;
        if (intelligence == 10) return 40;
        if (intelligence == 11) return 45;
        if (intelligence == 12) return 50;
        if (intelligence == 13) return 55;
        if (intelligence == 14) return 60;
        if (intelligence == 15) return 65;
        if (intelligence == 16) return 70;
        if (intelligence == 17) return 75;
        if (intelligence == 18) return 85;
        if (intelligence == 19) return 95;
        if (intelligence == 20) return 96;
        if (intelligence == 21) return 97;
        if (intelligence == 22) return 98;
        if (intelligence == 23) return 99;
        return 100;
    }

    private void UpdateLearnChanceText()
    {
        int chance = GetLearnSpellChancePercent();
        LearnChanceText.Text = chance <= 0
            ? "Learn chance: 0% (no automatic success)."
            : $"Learn chance: {chance}%";
    }

    private int ParseManualRoll(out bool usedManual)
    {
        if (int.TryParse(ManualRollBox.Text?.Trim(), out int manualRoll))
        {
            usedManual = true;
            return Math.Clamp(manualRoll, 1, 100);
        }

        usedManual = false;
        return _rng.Next(1, 101);
    }
}
