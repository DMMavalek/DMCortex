using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Views;

public partial class CharGenSubraceScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    private List<RaceDefinition> _subraces = new();
    private RaceDefinition? _activeSubrace;
    private List<AbilityDefinition> _optionalAbilities = new();
    private List<AbilityDefinition> _autoAssignedAbilities = new();
    private readonly HashSet<string> _selectedAbilityIds = new(StringComparer.OrdinalIgnoreCase);
    private List<AbilityListItem> _availableItems = new();
    private List<AbilityListItem> _selectedItems = new();

    private bool IsPlayersOptionMode
        => string.Equals(_app.CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);

    public CharGenSubraceScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.SetBanner("Character Blueprint  ›  Subrace");
        bool isPO_s = IsPlayersOptionMode;
        _app.SetNavBar(4, isPO_s ? 7 : 5, "Subrace",
            backAction: () => _app.GoTo("chargen_race"),
            nextAction: Advance);

        // Core Rules: hide the racial ability selection panel — the subrace is fixed and prebuilt.
        if (RacialAbilityPanel != null)
            RacialAbilityPanel.Visibility = isPO_s ? Visibility.Visible : Visibility.Collapsed;

        var baseRaceId = ResolveBaseRaceId();
        if (string.IsNullOrWhiteSpace(baseRaceId))
        {
            MessageBox.Show("Please choose a race first.", "Selection Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _app.GoTo("chargen_race", -1);
            return;
        }

        _subraces = _app.Rules.SubracesForBase(_app.CharGen.CharacterMode, baseRaceId)
            .OrderByDescending(IsBasicSubrace)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        SubraceList.Items.Clear();
        foreach (var race in _subraces) SubraceList.Items.Add(race.Name);

        var selectedRaceId = _app.CharGen.RaceId;
        var idx = _subraces.FindIndex(r => string.Equals(r.Id, selectedRaceId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            SubraceList.SelectedIndex = idx;
        }
        else if (_subraces.Count > 0)
        {
            int basicIdx = _subraces.FindIndex(IsBasicSubrace);
            SubraceList.SelectedIndex = basicIdx >= 0 ? basicIdx : 0;
        }

        if (_subraces.Count == 0)
        {
            SubraceTitle.Text = "No subraces found";
            SubraceInfo.Text = "This race currently has no subrace definitions for the selected character mode.";
            BudgetSummary.Text = string.Empty;
            BudgetDetail.Text = string.Empty;
            AvailableAbilityList.ItemsSource = null;
            SelectedAbilityList.ItemsSource = null;
        }
    }

    private string ResolveBaseRaceId()
    {
        if (!string.IsNullOrWhiteSpace(_app.CharGen.BaseRaceId))
            return _app.CharGen.BaseRaceId;

        if (!string.IsNullOrWhiteSpace(_app.CharGen.RaceId) &&
            _app.Rules.Races.TryGetValue(_app.CharGen.RaceId, out var existingRace))
            return string.IsNullOrWhiteSpace(existingRace.BaseRaceId) ? existingRace.Id : existingRace.BaseRaceId;

        return string.Empty;
    }

    private void SubraceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = SubraceList.SelectedIndex;
        if (idx < 0 || idx >= _subraces.Count) return;

        var race = _subraces[idx];
        _activeSubrace = race;
        ShowSubraceInfo(race);
        SubraceList.ToolTip = SubraceInfo.Text;
        BuildAbilityChoices(race);
        RefreshBudgetSummary(race);
    }

    private void SubraceList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var idx = SubraceList.SelectedIndex;
        if (idx < 0 || idx >= _subraces.Count) return;

        var race = _subraces[idx];
        ShowSubraceInfo(race);
        MessageBox.Show(SubraceInfo.Text, $"{race.Name} Description",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowSubraceInfo(RaceDefinition race)
    {
        SubraceTitle.Text = race.Name;
        var autoCount = race.StructuredAbilities.Count(x => x.AutoGranted);
        var optionCount = race.StructuredAbilities.Count(x => !x.AutoGranted);
        
        var sb = new StringBuilder();
        sb.AppendLine(autoCount > 0
            ? $"{autoCount} default abilities, {optionCount} optional choices"
            : optionCount > 0
                ? $"No defaults, {optionCount} optional choices"
                : "No racial ability entries");
        
        // Display racial ability modifiers if any
        var raceModifiers = _app.Rules.GetEffectiveAbilityModifiers(race.Id);
        if (raceModifiers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ABILITY MODIFIERS:");
            var abilityLabels = new[] { "STR", "DEX", "CON", "INT", "WIS", "CHA" };
            var abilityKeys = new[] { "str", "dex", "con", "int", "wis", "cha" };
            
            for (int i = 0; i < abilityKeys.Length; i++)
            {
                if (raceModifiers.TryGetValue(abilityKeys[i], out var modifier) && modifier != 0)
                {
                    int baseScore = _app.CharGen.Abilities.GetValueOrDefault(abilityKeys[i], 10);
                    int modifiedScore = _app.Rules.ApplyRacialModifiers(race.Id, _app.CharGen.Abilities)
                        .GetValueOrDefault(abilityKeys[i], 10);
                    
                    var modStr = modifier > 0 ? $"+{modifier}" : modifier.ToString();
                    sb.AppendLine($"  {abilityLabels[i]}: {baseScore} → {modifiedScore}  ({modStr})");
                }
            }
        }
        
        SubraceInfo.Text = sb.ToString().Trim();
    }

    private void BuildAbilityChoices(RaceDefinition race)
    {
        if (IsPlayersOptionMode)
        {
            _autoAssignedAbilities = race.StructuredAbilities
                .Where(a => a.AutoGranted)
                .OrderBy(a => a.PointCost)
                .ThenBy(a => a.Description)
                .ToList();

            _optionalAbilities = race.StructuredAbilities
                .Where(a => !a.AutoGranted)
                .OrderBy(a => a.PointCost)
                .ThenBy(a => a.Description)
                .ToList();
        }
        else
        {
            // Core Rules: racial package is fixed and fully granted.
            _autoAssignedAbilities = race.StructuredAbilities
                .OrderBy(a => a.PointCost)
                .ThenBy(a => a.Description)
                .ToList();
            _optionalAbilities = new List<AbilityDefinition>();
        }

        _selectedAbilityIds.Clear();
        if (IsPlayersOptionMode && IsBasicSubrace(race))
        {
            var optionalIds = new HashSet<string>(_optionalAbilities.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var id in _app.CharGen.SelectedRacialAbilityIds.Where(optionalIds.Contains))
                _selectedAbilityIds.Add(id);
        }

        RefreshAbilityLists();
    }

    private void RefreshAbilityLists()
    {
        _availableItems = _optionalAbilities
            .Where(a => !_selectedAbilityIds.Contains(a.Id))
            .Select(ToAbilityItem)
            .OrderBy(a => a.Label)
            .ToList();

        _selectedItems = _autoAssignedAbilities
            .Select(ToAbilityItem)
            .Concat(_optionalAbilities
                .Where(a => _selectedAbilityIds.Contains(a.Id))
                .Select(ToAbilityItem))
            .OrderBy(a => a.Label)
            .ToList();

        AvailableAbilityList.ItemsSource = _availableItems;
        SelectedAbilityList.ItemsSource = _selectedItems;
    }

    private AbilityListItem ToAbilityItem(AbilityDefinition a)
    {
        var shortName = ToShortAbilityName(a.Description);
        string label = IsPlayersOptionMode
            ? $"{shortName} ({a.PointCost} CP)"
            : shortName;
        return new AbilityListItem
        {
            Id = a.Id,
            Label = label,
            Description = a.Description,
            IsAutoAssigned = a.AutoGranted,
        };
    }

    private static string ToShortAbilityName(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "Ability";

        var name = description.Split(':', 2)[0].Trim();
        name = Regex.Replace(name, @"\s*\(\s*-?\d+\s*\)\s*$", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(name) ? description.Trim() : name;
    }

    private void BtnAddAbility_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPlayersOptionMode)
            return;

        if (_activeSubrace is null) return;
        if (AvailableAbilityList.SelectedItem is not AbilityListItem item) return;
        if (EnsureEditableSubrace()) return;
        _selectedAbilityIds.Add(item.Id);
        RefreshAbilityLists();
        RefreshBudgetSummary(_activeSubrace);
    }

    private void BtnRemoveAbility_Click(object sender, RoutedEventArgs e)
    {
        if (!IsPlayersOptionMode)
            return;

        if (_activeSubrace is null) return;
        if (SelectedAbilityList.SelectedItem is not AbilityListItem item) return;
        if (EnsureEditableSubrace()) return;
        if (item.IsAutoAssigned) return;
        _selectedAbilityIds.Remove(item.Id);
        RefreshAbilityLists();
        RefreshBudgetSummary(_activeSubrace);
    }

    private bool EnsureEditableSubrace()
    {
        if (_activeSubrace is null || IsBasicSubrace(_activeSubrace)) return false;

        var basic = FindBasicSubrace(_activeSubrace.BaseRaceId);
        if (basic is null) return false;

        _app.CharGen.SelectedRacialAbilityIds.Clear();
        _selectedAbilityIds.Clear();

        var basicIndex = _subraces.FindIndex(r => string.Equals(r.Id, basic.Id, StringComparison.OrdinalIgnoreCase));
        if (basicIndex >= 0)
        {
            SubraceList.SelectedIndex = basicIndex;
            return true;
        }

        return false;
    }

    private RaceDefinition? FindBasicSubrace(string? baseRaceId)
    {
        return _subraces.FirstOrDefault(r =>
            string.Equals(r.BaseRaceId, baseRaceId, StringComparison.OrdinalIgnoreCase) &&
            IsBasicSubrace(r));
    }

    private static bool IsBasicSubrace(RaceDefinition race)
    {
        return race.Name.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase);
    }

    private void AvailableAbilityList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        BtnAddAbility_Click(sender, new RoutedEventArgs());
    }

    private void SelectedAbilityList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        BtnRemoveAbility_Click(sender, new RoutedEventArgs());
    }

    private void AvailableAbilityList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AvailableAbilityList.SelectedItem is not AbilityListItem item) return;
        ShowAbilityDescription(item);
    }

    private void SelectedAbilityList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedAbilityList.SelectedItem is not AbilityListItem item) return;
        ShowAbilityDescription(item);
    }

    private void ShowAbilityDescription(AbilityListItem item)
    {
        var text = string.IsNullOrWhiteSpace(item.Description) ? item.Label : item.Description;
        DescriptionPopupService.Show(Window.GetWindow(this), "Racial Ability Description", text);
    }

    private void RefreshBudgetSummary(RaceDefinition race)
    {
        if (!IsPlayersOptionMode)
        {
            BudgetSummary.Text = "Core";
            BudgetDetail.Text = "Core Rules: default racial abilities are auto-granted.";
            BudgetSummary.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#C4A468"));
            return;
        }

        var selectedIds = _selectedAbilityIds.ToList();
        var package = _app.Rules.BuildRacialAbilityPackage(race.Id, selectedIds);

        BudgetSummary.Text = package.budget > 0 ? package.remaining.ToString() : "-";
        BudgetDetail.Text = package.budget > 0
            ? $"Spent {package.spent} / {package.budget} CP"
            : "No CP budget for this subrace";
        BudgetSummary.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(package.remaining < 0 ? "#C02828" : package.remaining == 0 ? "#E8C050" : "#C4A468"));
    }

    private void Advance()
    {
        var idx = SubraceList.SelectedIndex;
        if (idx < 0 || idx >= _subraces.Count)
        {
            MessageBox.Show("Please choose a subrace.", "Selection Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedRace = _subraces[idx];
        var selectedIds = IsPlayersOptionMode
            ? _selectedAbilityIds.ToList()
            : selectedRace.StructuredAbilities
                .Where(a => !a.AutoGranted)
                .Select(a => a.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        var package = _app.Rules.BuildRacialAbilityPackage(selectedRace.Id, selectedIds);
        if (IsPlayersOptionMode)
        {
            if (package.remaining < 0)
            {
                MessageBox.Show(
                    $"Selected racial abilities exceed budget by {-package.remaining} points.",
                    "Racial Point Budget",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            if (package.remaining > 5)
            {
                MessageBox.Show(
                    $"You can carry over at most 5 racial CP into class abilities.\n\n" +
                    $"Current unspent racial CP: {package.remaining}.\n" +
                    "Spend more racial points before continuing.",
                    "Racial CP Carryover Limit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        var previousModified = _app.CharGen.ModifiedAbilities != null && _app.CharGen.ModifiedAbilities.Count > 0
            ? new Dictionary<string, int>(_app.CharGen.ModifiedAbilities)
            : new Dictionary<string, int>(_app.CharGen.Abilities);

        _app.CharGen.RaceId = selectedRace.Id;
        _app.CharGen.SelectedRacialAbilityIds = selectedIds;
        _app.CharGen.RacialCarryoverToClassPoints = IsPlayersOptionMode ? package.remaining : 0;
        
        // Apply racial ability modifiers to base ability scores
        _app.CharGen.RacialAbilityModifiers = selectedRace.AbilityModifiers ?? new();
        
        // Ensure abilities are populated
        if (_app.CharGen.Abilities == null || _app.CharGen.Abilities.Count == 0)
        {
            _app.CharGen.ModifiedAbilities = new();
        }
        else
        {
            _app.CharGen.ModifiedAbilities = _app.Rules.ApplyRacialModifiers(
                selectedRace.Id,
                _app.CharGen.Abilities);
        }

        RebaseSubAbilities(previousModified, _app.CharGen.ModifiedAbilities);
        
        _app.GoTo("chargen_class");
    }

    private void RebaseSubAbilities(Dictionary<string, int> previousModified, Dictionary<string, int> newModified)
    {
        if (_app.CharGen.SubAbilities.Count == 0) return;

        var keys = _app.CharGen.SubAbilities.Keys.ToList();
        foreach (var subKey in keys)
        {
            int separator = subKey.IndexOf('_');
            if (separator <= 0) continue;

            string abilityKey = subKey[..separator];
            int oldBase = previousModified.GetValueOrDefault(abilityKey, _app.CharGen.Abilities.GetValueOrDefault(abilityKey, 10));
            int newBase = newModified.GetValueOrDefault(abilityKey, _app.CharGen.Abilities.GetValueOrDefault(abilityKey, 10));

            int current = _app.CharGen.SubAbilities.GetValueOrDefault(subKey, oldBase);
            int relativeDelta = current - oldBase;

            int min = Math.Max(3, newBase - 4);
            int hardCap = string.Equals(abilityKey, "str", StringComparison.OrdinalIgnoreCase) ? 20 : 18;
            int max = Math.Min(hardCap, newBase + 4);

            int rebased = Math.Clamp(newBase + relativeDelta, min, max);
            _app.CharGen.SubAbilities[subKey] = rebased;
        }
    }
}

public class AbilityListItem
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsAutoAssigned { get; set; }
    public bool IsDisadvantage { get; set; }
}
