using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public class MultiClassItem : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsBaseEligible { get; set; } = true;
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }
    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class SingleClassItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public string AvailabilityHint { get; set; } = "";
}

public class ClassRequirementRow
{
    public string AbilityName { get; set; } = "";
    public string Required    { get; set; } = "";
    public string Actual      { get; set; } = "";
    public string StatusGlyph { get; set; } = "";
    public string StatusColor { get; set; } = "#C4A468";
}

public partial class CharGenClassScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    private List<ClassDefinition> _eligible = new();
    private List<SingleClassItem> _singleClassItems = new();

    // SphereCosts / CalculateSphereCpCost kept here because SphereSelectionDialog references them.
    internal static readonly Dictionary<string, (int minor, int major)> SphereCosts =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["All"]         = (3,  5),
            ["Animal"]      = (5,  10),
            ["Astral"]      = (3,  5),
            ["Chaos"]       = (5,  8),
            ["Charm"]       = (5,  10),
            ["Combat"]      = (5,  10),
            ["Creation"]    = (5,  10),
            ["Divination"]  = (5,  10),
            ["Elemental"]   = (8,  20),
            ["Elemental Air"] = (2, 5),
            ["Elemental Earth"] = (3, 8),
            ["Elemental Fire"] = (3, 8),
            ["Elemental Water"] = (2, 5),
            ["Evil"]        = (5,  10),
            ["Good"]        = (5,  10),
            ["Guardian"]    = (3,  5),
            ["Healing"]     = (5,  10),
            ["Knowledge"]   = (5,  10),
            ["Law"]         = (5,  8),
            ["Life"]        = (5,  10),
            ["Magic"]       = (5,  10),
            ["Necromantic"] = (5,  10),
            ["Numbers"]     = (5,  10),
            ["Plant"]       = (5,  10),
            ["Protection"]  = (5,  10),
            ["Spells"]      = (5,  10),
            ["Summoning"]   = (5,  10),
            ["Sun"]         = (3,  5),
            ["Thought"]     = (5,  10),
            ["Time"]        = (5,  10),
            ["Travelers"]   = (3,  5),
            ["War"]         = (3,  5),
            ["Wards"]       = (5,  10),
            ["Weather"]     = (5,  10),
        };

    internal static int CalculateSphereCpCost(Dictionary<string, string> spheres)
    {
        int total = 0;
        foreach (var (sphere, access) in NormalizeSphereSelections(spheres))
        {
            var (minor, major) = SphereCosts.TryGetValue(sphere, out var costs) ? costs : (5, 10);
            total += access switch { "minor" => minor, "major" => major, "both" => minor + major, _ => 0 };
        }
        return total;
    }

    internal static Dictionary<string, string> NormalizeSphereSelections(Dictionary<string, string>? spheres)
    {
        var normalized = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        if (spheres is null)
            return normalized;

        foreach (var (sphere, access) in spheres)
        {
            if (string.Equals(sphere, "Elemental", System.StringComparison.OrdinalIgnoreCase))
            {
                normalized["Elemental"] = access; // Preserve Elemental instead of expanding
                continue;
            }

            normalized[sphere] = access;
        }

        return normalized;
    }

    public CharGenClassScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        try
        {
            _app.SetBanner(_app.CharGen.IsLevelUpMode
                ? "Character Section  ›  Level Up  ›  Class"
                : "Character Blueprint  ›  Class");
            bool isPO = _app.CharGen.CharacterMode == "players_option";
            _app.SetNavBar(5, isPO ? 9 : 8, "Class",
                backAction: () => _app.GoTo("chargen_subrace", -1),
                nextAction: Advance);

            if (string.IsNullOrEmpty(_app.CharGen.RaceId))
            {
                MessageBox.Show("Error: Race not selected. Please go back and select a race.",
                    "Invalid State", MessageBoxButton.OK, MessageBoxImage.Error);
                _app.GoTo("chargen_subrace");
                return;
            }

            // Ensure ModifiedAbilities is calculated with racial modifiers
            if (_app.CharGen.ModifiedAbilities == null || _app.CharGen.ModifiedAbilities.Count == 0)
            {
                if (_app.CharGen.Abilities.Count > 0 && !string.IsNullOrEmpty(_app.CharGen.RaceId))
                {
                    _app.CharGen.ModifiedAbilities = _app.Rules.ApplyRacialModifiers(
                        _app.CharGen.RaceId,
                        _app.CharGen.Abilities);
                }
                else
                {
                    _app.CharGen.ModifiedAbilities = new Dictionary<string, int>(_app.CharGen.Abilities);
                }
            }

            _eligible = _app.Rules.Classes.Values
                .Where(ClassModeAllowed)
                .OrderBy(c => c.Name, System.StringComparer.OrdinalIgnoreCase)
                .ToList();
            _singleClassItems = BuildSingleClassItems();

            // Update race-filtered labels
            if (_app.Rules.Races.TryGetValue(_app.CharGen.RaceId, out var raceForLabel))
            {
                string rn = raceForLabel.Name;
                int enabledCount = _singleClassItems.Count(x => x.IsEnabled);
                ClassScreenSubtitle.Text = $"Step 5  \u00b7  {enabledCount} of {_singleClassItems.Count} classes currently available for {rn}";
                SingleClassLabel.Text    = $"Classes for {rn} (unavailable entries are grayed out):";
                MultiClassLabel.Text     = $"Classes for {rn} (check 2+, unavailable entries are grayed out):";
            }

            bool isHuman = _app.CharGen.RaceId == "human" ||
                           (_app.Rules.Races.TryGetValue(_app.CharGen.RaceId, out var selRace) && selRace.BaseRaceId == "human");
            RadioMultiClass.Visibility = isHuman ? Visibility.Collapsed : Visibility.Visible;
            RadioDualClass.Visibility  = Visibility.Collapsed;

            if (isHuman && _app.CharGen.ClassMode == "multiclass") _app.CharGen.ClassMode = "";
            if (_app.CharGen.ClassMode == "dualclass")             _app.CharGen.ClassMode = "";

            RadioSingleClass.IsChecked = string.IsNullOrEmpty(_app.CharGen.ClassMode);
            RadioMultiClass.IsChecked  = _app.CharGen.ClassMode == "multiclass";
            RadioDualClass.IsChecked   = false;

            RefreshUIForMode();
            ValidationLabel.Text = "";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error in CharGenClassScreen.OnEnter:\n\n{ex.GetType().Name}: {ex.Message}\n\nStack trace:\n{ex.StackTrace}",
                "Debug Info", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    // ── Mode panel visibility ────────────────────────────────────────────────

    private List<KitDefinition> _availableKits = new();

    private void RefreshUIForMode()
    {
        string mode = _app.CharGen.ClassMode ?? "";

        SingleClassPanel.Visibility = mode == "multiclass" ? Visibility.Collapsed : Visibility.Visible;
        MultiClassPanel.Visibility = mode == "multiclass" ? Visibility.Visible : Visibility.Collapsed;

        ClassList.ItemsSource = null;
        MultiClassCheckList.ItemsSource = null;
        MultiClassValidation.Text = "";

        if (mode != "multiclass")
        {
            ClassList.ItemsSource = _singleClassItems;

            if (!string.IsNullOrEmpty(_app.CharGen.ClassId))
            {
                var selectedItem = _singleClassItems.FirstOrDefault(c => string.Equals(c.Id, _app.CharGen.ClassId, StringComparison.OrdinalIgnoreCase));
                if (selectedItem is not null)
                    ClassList.SelectedItem = selectedItem;
            }

            if (ClassList.SelectedIndex < 0)
                ResetClassDisplay();
        }
        else
        {
            PopulateMultiClassList();
        }

        RefreshKitPanel();
    }

    private void RefreshKitPanel()
    {
        string raceId   = _app.CharGen.RaceId ?? "";
        string classId  = _app.CharGen.ClassId ?? "";

        _availableKits = _app.Rules.KitsFor(raceId, classId, _app.CharGen.CharacterMode);

        if (_availableKits.Count == 0)
        {
            KitPanel.Visibility = Visibility.Collapsed;
            return;
        }

        KitPanel.Visibility = Visibility.Visible;
        if (_app.Rules.Races.TryGetValue(raceId, out var race))
            KitSubtitle.Text = $"Kits available for {race.Name} — select one or leave blank for none.";

        KitList.SelectionChanged -= KitList_SelectionChanged;
        KitList.Items.Clear();
        KitList.Items.Add("(None — no kit)");
        foreach (var kit in _availableKits)
            KitList.Items.Add(kit.Name);
        KitList.SelectionChanged += KitList_SelectionChanged;

        // Restore previous selection
        if (!string.IsNullOrEmpty(_app.CharGen.KitId))
        {
            int idx = _availableKits.FindIndex(k => k.Id == _app.CharGen.KitId);
            KitList.SelectedIndex = idx >= 0 ? idx + 1 : 0;
        }
        else
        {
            KitList.SelectedIndex = 0;
        }

        UpdateKitDescription();
    }

    private void UpdateKitDescription()
    {
        int idx = KitList.SelectedIndex;
        if (idx <= 0 || idx - 1 >= _availableKits.Count)
        {
            KitDescriptionText.Text = "";
            return;
        }
        var kit = _availableKits[idx - 1];
        KitDescriptionText.Text = $"{kit.Name} ({kit.Source}): {kit.Description}";
    }

    private void PopulateMultiClassList()
    {
        var classItems = _eligible
            .Select(c =>
            {
                var issues = _app.Rules.Validate(_app.CharGen.RaceId, c.Id, _app.CharGen.Abilities);
                bool isEligible = issues.Count == 0;
                return new MultiClassItem
                {
                    Id = c.Id,
                    Name = c.Name,
                    IsSelected = _app.CharGen.SelectedClassIds.Contains(c.Id),
                    IsBaseEligible = isEligible,
                    IsEnabled = isEligible,
                };
            })
            .ToList();
        foreach (var item in classItems)
            item.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(MultiClassItem.IsSelected)) SyncMultiClassSelection(); };
        MultiClassCheckList.ItemsSource = classItems;

        // Apply combo filtering based on any restored selection
        FilterMultiClassOptions(classItems);

        var detailClass = classItems.FirstOrDefault(i => i.IsSelected)?.Id;
        var selectedClass = !string.IsNullOrEmpty(detailClass)
            ? _eligible.FirstOrDefault(c => c.Id == detailClass)
            : _eligible.FirstOrDefault();
        if (selectedClass != null)
        {
            ShowClassInfo(selectedClass);
            ValidateLive(selectedClass);
        }
        else
        {
            ResetClassDisplay();
        }
    }

    private void SyncMultiClassSelection()
    {
        if (MultiClassCheckList.ItemsSource is not List<MultiClassItem> items) return;
        _app.CharGen.SelectedClassIds = items.Where(i => i.IsSelected).Select(i => i.Id).ToList();
        FilterMultiClassOptions(items);
        int count = _app.CharGen.SelectedClassIds.Count;
        MultiClassValidation.Text = count < 2
            ? "Select at least 2 classes."
            : $"{count} classes selected.";
    }

    private void FilterMultiClassOptions(List<MultiClassItem> items)
    {
        var selected = items.Where(i => i.IsSelected)
                            .Select(i => i.Id)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selected.Count == 0)
        {
            foreach (var item in items) item.IsEnabled = item.IsBaseEligible;
            return;
        }

        var combos = _app.Rules.GetValidMultiClassCombos(_app.CharGen.RaceId);
        if (combos.Count == 0)
        {
            // Race has no defined combos — no restriction
            foreach (var item in items) item.IsEnabled = item.IsBaseEligible;
            return;
        }

        // Find all combos that still contain every already-selected class
        var validCombos = combos.Where(c => selected.IsSubsetOf(c)).ToList();

        if (validCombos.Count == 0)
        {
            // Shouldn't happen in normal flow, but keep selected enabled only
            foreach (var item in items)
                item.IsEnabled = item.IsSelected || item.IsBaseEligible;
            return;
        }

        // A class is reachable if it appears in at least one still-valid combo
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var combo in validCombos)
            foreach (var id in combo)
                reachable.Add(id);

        foreach (var item in items)
            item.IsEnabled = item.IsSelected || (item.IsBaseEligible && reachable.Contains(item.Id));
    }

    private bool ClassModeAllowed(ClassDefinition cls)
    {
        string mode = (_app.CharGen.CharacterMode ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(mode))
            mode = "core_rules";

        string classMode = (cls.RulesMode ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(classMode))
            classMode = "all";

        return classMode == "all" || classMode == mode;
    }

    private List<SingleClassItem> BuildSingleClassItems()
    {
        var items = new List<SingleClassItem>();
        foreach (var cls in _eligible)
        {
            var issues = _app.Rules.Validate(_app.CharGen.RaceId, cls.Id, _app.CharGen.Abilities);
            bool isEnabled = issues.Count == 0;
            string hint = isEnabled
                ? "Available"
                : string.Join(" | ", issues.Distinct(StringComparer.OrdinalIgnoreCase));

            items.Add(new SingleClassItem
            {
                Id = cls.Id,
                Name = cls.Name,
                IsEnabled = isEnabled,
                AvailabilityHint = hint,
            });
        }

        return items;
    }

    // ── Right-panel display ──────────────────────────────────────────────────

    private void ResetClassDisplay()
    {
        ClassTitle.Text = "\u2014";
        ClassDetailSubtitle.Text = "Select a class to see details";
        ClassCpBadge.Visibility = Visibility.Collapsed;
        ClassRequirementsPanel.ItemsSource = null;
        ClassNoRequirements.Visibility = Visibility.Collapsed;
        ClassAutoAbilitiesCard.Visibility = Visibility.Collapsed;
        ClassOptionalAbilitiesCard.Visibility = Visibility.Collapsed;
        ClassInfo.Text = "";
    }

    private void ShowClassInfo(ClassDefinition cls)
    {
        // Title
        ClassTitle.Text = cls.Name;
        ClassDetailSubtitle.Text = "Selected class details";

        // CP budget badge
        if (string.Equals(_app.CharGen.CharacterMode, "players_option", System.StringComparison.OrdinalIgnoreCase)
            && cls.ClassPointBudget > 0)
        {
            ClassCpBadgeText.Text = $"{cls.ClassPointBudget} CP";
            ClassCpBadge.Visibility = Visibility.Visible;
        }
        else
        {
            ClassCpBadge.Visibility = Visibility.Collapsed;
        }

        // Ability requirements with pass/fail
        var abilities = _app.CharGen.Abilities;
        if (cls.AbilityMinimums.Count > 0)
        {
            var rows = cls.AbilityMinimums
                .OrderBy(kv => kv.Key)
                .Select(kv =>
                {
                    int actual   = abilities.TryGetValue(kv.Key, out int v) ? v : 0;
                    bool met     = actual >= kv.Value;
                    return new ClassRequirementRow
                    {
                        AbilityName = RulesEngine.AbilityLabel(kv.Key),
                        Required    = kv.Value.ToString(),
                        Actual      = actual.ToString(),
                        StatusGlyph = met ? "\u2713" : "\u2718",
                        StatusColor = met ? "#6EC07A" : "#C05050",
                    };
                })
                .ToList();
            ClassRequirementsPanel.ItemsSource = rows;
            ClassNoRequirements.Visibility = Visibility.Collapsed;
        }
        else
        {
            ClassRequirementsPanel.ItemsSource = null;
            ClassNoRequirements.Visibility = Visibility.Visible;
        }

        // Auto-granted abilities
        var autoAbils = cls.StructuredAbilities
            .Where(a => a.AutoGranted)
            .Select(a => a.Description.Split(':')[0].Trim())
            .ToList();
        if (autoAbils.Count > 0)
        {
            ClassAutoAbilitiesList.ItemsSource = autoAbils;
            ClassAutoAbilitiesCard.Visibility  = Visibility.Visible;
        }
        else
        {
            ClassAutoAbilitiesCard.Visibility = Visibility.Collapsed;
        }

        bool isPlayersOption = string.Equals(_app.CharGen.CharacterMode, "players_option", System.StringComparison.OrdinalIgnoreCase);

        // Optional abilities are only player-selected in Player's Option mode.
        if (isPlayersOption)
        {
            int optCount = GetPreviewOptionalAbilityCount(cls);
            if (optCount > 0)
            {
                ClassOptionalAbilitiesText.Text = $"{optCount} optional abilit{(optCount == 1 ? "y" : "ies")} to choose from on the next screen.";
                ClassOptionalAbilitiesCard.Visibility = Visibility.Visible;
            }
            else
            {
                ClassOptionalAbilitiesCard.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            ClassOptionalAbilitiesCard.Visibility = Visibility.Collapsed;
        }

        // Keep hidden text field populated for right-click compat
        ClassInfo.Text = cls.Name;
    }

    private static int GetPreviewOptionalAbilityCount(ClassDefinition cls)
    {
        var hiddenWizardSpecializationAbilityIds = string.Equals(cls.Id, "wizard", System.StringComparison.OrdinalIgnoreCase)
            ? cls.Specializations?
                .SelectMany(s => s.AutoSelectAbilityIds ?? Enumerable.Empty<string>())
                .ToHashSet(System.StringComparer.OrdinalIgnoreCase)
              ?? new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        return cls.StructuredAbilities.Count(a => !a.AutoGranted
            && !CharGenClassAbilitiesScreen.IsSelectorManagedAbility(a)
            && !hiddenWizardSpecializationAbilityIds.Contains(a.Id));
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void ClassMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_eligible == null || _eligible.Count == 0) return;
        _app.CharGen.ClassMode = RadioMultiClass.IsChecked == true ? "multiclass"
                               : RadioDualClass.IsChecked   == true ? "dualclass"
                               : "";
        RefreshUIForMode();
    }

    private void MultiClassCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        SyncMultiClassSelection();

        if (sender is CheckBox { Tag: string classId })
        {
            var cls = _eligible.FirstOrDefault(c => c.Id == classId);
            if (cls != null)
            {
                ShowClassInfo(cls);
                ValidateLive(cls);
            }
        }
    }

    private void MultiClassCheckbox_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not CheckBox { Tag: string classId }) return;

        var cls = _eligible.FirstOrDefault(c => c.Id == classId);
        if (cls == null) return;

        ShowClassInfo(cls);
        ValidateLive(cls);
    }

    private void ClassList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClassList.SelectedItem is not SingleClassItem selected)
            return;

        if (!selected.IsEnabled)
        {
            ValidationLabel.Text = "\u26a0  That class is unavailable due to current race/ability restrictions.";
            ValidationLabel.Foreground = Brushes.Red;
            return;
        }

        var cls = _eligible.FirstOrDefault(x => string.Equals(x.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
        if (cls is null)
            return;

        ShowClassInfo(cls);
        ValidateLive(cls);

        // Update kit panel for newly selected class
        _app.CharGen.ClassId = cls.Id;
        _app.CharGen.KitId   = "";
        RefreshKitPanel();
    }

    private void KitList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = KitList.SelectedIndex;
        if (idx <= 0 || idx - 1 >= _availableKits.Count)
        {
            _app.CharGen.KitId = "";
        }
        else
        {
            _app.CharGen.KitId = _availableKits[idx - 1].Id;
        }
        UpdateKitDescription();
    }

    private void ClassList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ClassList.SelectedItem is not SingleClassItem selected)
            return;
        var cls = _eligible.FirstOrDefault(x => string.Equals(x.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
        if (cls is null)
            return;
        ShowClassInfo(cls);
    }

    private void ValidateLive(ClassDefinition cls)
    {
        var issues = _app.Rules.Validate(_app.CharGen.RaceId, cls.Id, _app.CharGen.Abilities);
        if (issues.Count > 0)
        {
            ValidationLabel.Text       = "\u26a0  " + issues[0];
            ValidationLabel.Foreground = Brushes.Red;
        }
        else
        {
            ValidationLabel.Text       = "\u2713  Valid combination";
            ValidationLabel.Foreground = Brushes.LightGreen;
        }
    }

    // ── Advance ──────────────────────────────────────────────────────────────

    private void Advance()
    {
        bool isMulti = RadioMultiClass.IsChecked == true;

        if (isMulti)
        {
            if (_app.CharGen.SelectedClassIds.Count < 2)
            {
                MessageBox.Show("Please select at least 2 classes for multi-class.", "Selection Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_app.CharGen.IsLevelUpMode)
            {
                var originalMode = _app.CharGen.ClassMode;
                bool wasMulticlass = string.Equals(originalMode, "multiclass", StringComparison.OrdinalIgnoreCase);
                if (wasMulticlass)
                {
                    // Multiclass characters cannot change their class list
                    var existingIds = _app.Characters[_app.CharGen.LevelUpCharacterIndex].ClassIds
                        .Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x).ToList();
                    var newIds = _app.CharGen.SelectedClassIds.OrderBy(x => x).ToList();
                    bool classChanged = !newIds.SequenceEqual(existingIds, StringComparer.OrdinalIgnoreCase);
                    if (classChanged)
                    {
                        MessageBox.Show(
                            "Multiclass characters cannot change, add, or remove classes during level-up.",
                            "Class Locked",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }
                }
                else
                {
                    var confirm = MessageBox.Show(
                        "⚠  You are changing this character's class configuration.\n\nDid your DM say this was OK?",
                        "Class Change — DM Approval Required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.Yes) return;
                }
            }

            _app.CharGen.ClassMode = "multiclass";
            _app.CharGen.ClassId   = _app.CharGen.SelectedClassIds[0];
        }
        else
        {
            if (ClassList.SelectedItem is not SingleClassItem selected)
            {
                MessageBox.Show("Please choose a class.", "Selection Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!selected.IsEnabled)
            {
                MessageBox.Show(
                    "That class is unavailable due to current race/ability restrictions.",
                    "Selection Restricted",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var cls = _eligible.FirstOrDefault(c => string.Equals(c.Id, selected.Id, StringComparison.OrdinalIgnoreCase));
            if (cls is null)
            {
                MessageBox.Show("Please choose a valid class.", "Selection Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var issues = _app.Rules.Validate(_app.CharGen.RaceId, cls.Id, _app.CharGen.Abilities);
            if (issues.Count > 0)
            {
                var proceed = MessageBox.Show($"{issues[0]}\n\nProceed anyway?",
                    "Ability Score Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (proceed != MessageBoxResult.Yes) return;
            }

            if (_app.CharGen.IsLevelUpMode)
            {
                var existingSingleId = _app.Characters[_app.CharGen.LevelUpCharacterIndex].ClassId ?? "";
                bool classChanged = !string.Equals(cls.Id, existingSingleId, StringComparison.OrdinalIgnoreCase);
                if (classChanged)
                {
                    var confirm = MessageBox.Show(
                        "⚠  You are changing this character's class.\n\nDid your DM say this was OK?",
                        "Class Change — DM Approval Required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.Yes) return;
                }
            }

            _app.CharGen.ClassId           = cls.Id;
            _app.CharGen.ClassMode         = "";
            _app.CharGen.SelectedClassIds  = new() { cls.Id };
        }

        _app.CharGen.RecalculateLevelFromExistingExperience();

        // Core Rules: skip the class abilities screen entirely — all abilities are auto-granted.
        if (!string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
        {
            _app.CharGen.SelectedAbilitiesByClass.Clear();
            foreach (var classId in _app.CharGen.SelectedClassIds)
            {
                if (_app.Rules.Classes.TryGetValue(classId, out var cls))
                {
                    _app.CharGen.SelectedAbilitiesByClass[classId] = cls.StructuredAbilities
                        .Where(a => a.AutoGranted)
                        .Select(a => a.Id)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }
            _app.CharGen.SelectedClassAbilityIds = _app.CharGen.SelectedAbilitiesByClass
                .TryGetValue(_app.CharGen.ClassId, out var classIds)
                    ? new List<string>(classIds)
                    : new List<string>();
            _app.GoTo("chargen_character_options");
            return;
        }

        _app.GoTo("chargen_class_abilities");
    }
}
