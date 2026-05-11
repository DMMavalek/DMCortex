using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CharGenRaceScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    private List<RaceDefinition> _races = new();

    public CharGenRaceScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        bool isLevelUp = _app.CharGen.IsLevelUpMode;
        _app.SetBanner(isLevelUp
            ? "Character Section  ›  Level Up  ›  Race"
            : "Character Generator  ›  Race");
        bool isPO  = _app.CharGen.CharacterMode == "players_option";
        int  total = isPO ? 8 : 7;
        _app.SetNavBar(3, total, "Race",
            backAction: () => _app.GoTo(isLevelUp ? "characters" : "chargen_abilities", -1),
            nextAction: Advance);

        _races = new List<RaceDefinition>(_app.Rules.RacesForMode(_app.CharGen.CharacterMode));
        RaceList.Items.Clear();
        foreach (var r in _races)
            RaceList.Items.Add(FormatBaseRaceName(string.IsNullOrWhiteSpace(r.BaseRaceId) ? r.Id : r.BaseRaceId));

        // Restore selection
        var restoreRaceId = !string.IsNullOrWhiteSpace(_app.CharGen.BaseRaceId)
            ? _app.CharGen.BaseRaceId
            : _app.CharGen.RaceId;

        if (!string.IsNullOrEmpty(restoreRaceId))
        {
            int idx = _races.FindIndex(r =>
                string.Equals(r.BaseRaceId, restoreRaceId, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Id, restoreRaceId, System.StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) RaceList.SelectedIndex = idx;
        }
    }

    private void RaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = RaceList.SelectedIndex;
        if (idx < 0) return;
        ShowRaceInfo(_races[idx]);
        RaceList.ToolTip = RaceInfo.Text;
    }

    private void RaceList_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        int idx = RaceList.SelectedIndex;
        if (idx < 0 || idx >= _races.Count) return;
        ShowRaceInfo(_races[idx]);
        if (!string.IsNullOrWhiteSpace(RaceInfo.Text))
        {
            MessageBox.Show(RaceInfo.Text, $"{RaceTitle.Text} Description",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ShowRaceInfo(RaceDefinition race)
    {
        var baseLabel = FormatBaseRaceName(string.IsNullOrWhiteSpace(race.BaseRaceId) ? race.Id : race.BaseRaceId);
        RaceTitle.Text = baseLabel;
        var sb = new StringBuilder();

        // Preview should use the effective subrace package for this base race.
        // This shows the actual stat deltas players will receive after subrace selection.
        var previewRace = ResolvePreviewRace(race);

        var cpRace = race;
        if (!string.IsNullOrWhiteSpace(_app.CharGen.RaceId) &&
            _app.Rules.Races.TryGetValue(_app.CharGen.RaceId, out var selectedRace) &&
            string.Equals(selectedRace.BaseRaceId, race.BaseRaceId, System.StringComparison.OrdinalIgnoreCase))
        {
            cpRace = selectedRace;
        }

        var selectedIds = string.Equals(cpRace.Id, _app.CharGen.RaceId, System.StringComparison.OrdinalIgnoreCase)
            ? _app.CharGen.SelectedRacialAbilityIds
            : Enumerable.Empty<string>();
        var cp = _app.Rules.BuildRacialAbilityPackage(cpRace.Id, selectedIds);
        if (cp.budget > 0)
        {
            CpSummary.Text = $"Remaining CP: {cp.remaining}  (Spent {cp.spent} / {cp.budget})";
            CpSummary.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(cp.remaining < 0 ? "#C02828" : cp.remaining == 0 ? "#E8C050" : "#C4A468"));
        }
        else
        {
            CpSummary.Text = "";
        }

        if (previewRace.RacialPointBudget > 0)
        {
            var autoCost = previewRace.StructuredAbilities
                .Where(a => a.AutoGranted)
                .Sum(a => a.PointCost);
            sb.AppendLine($"Racial Point Budget: {previewRace.RacialPointBudget}");
            sb.AppendLine($"Auto-assigned Package Cost: {autoCost}");
            sb.AppendLine($"Unspent Racial Points: {previewRace.RacialPointBudget - autoCost}");
            sb.AppendLine();
        }

        if (!string.Equals(previewRace.Id, race.Id, System.StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"Preview Subrace: {previewRace.Name}");
            sb.AppendLine();
        }

        if (previewRace.AbilityMinimums.Count > 0)
        {
            sb.AppendLine("Ability Minimums:");
            foreach (var (ab, v) in previewRace.AbilityMinimums)
                sb.AppendLine($"  {RulesEngine.AbilityLabel(ab)}: {v}");
        }
        if (previewRace.AbilityMaximums.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("Ability Maximums:");
            foreach (var (ab, v) in previewRace.AbilityMaximums)
                sb.AppendLine($"  {RulesEngine.AbilityLabel(ab)}: {v}");
        }
        
        // Display racial ability modifiers with preview
        var previewModifiers = _app.Rules.GetEffectiveAbilityModifiers(previewRace.Id);
        if (previewModifiers.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("Ability Modifiers:");
            var abilityLabels = new[] { "STR", "DEX", "CON", "INT", "WIS", "CHA" };
            var abilityKeys = new[] { "str", "dex", "con", "int", "wis", "cha" };
            
            for (int i = 0; i < abilityKeys.Length; i++)
            {
                if (previewModifiers.TryGetValue(abilityKeys[i], out var modifier) && modifier != 0)
                {
                    int baseScore = _app.CharGen.Abilities.GetValueOrDefault(abilityKeys[i], 10);
                    int modifiedScore = baseScore + modifier;
                    var modStr = modifier > 0 ? $"+{modifier}" : modifier.ToString();
                    sb.AppendLine($"  {abilityLabels[i]}: {baseScore} → {modifiedScore}  ({modStr})");
                }
            }
        }
        
        if (previewRace.RacialAbilities.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("Racial Abilities:");
            if (previewRace.StructuredAbilities.Count > 0)
            {
                foreach (var ability in previewRace.StructuredAbilities)
                {
                    var tag = ability.AutoGranted ? "AUTO" : "OPTION";
                    sb.AppendLine($"  • [{tag}] ({ability.PointCost}) {ability.Description}");
                }
            }
            else
            {
                foreach (var ability in previewRace.RacialAbilities)
                    sb.AppendLine($"  • {ability}");
            }
        }
        if (previewRace.AbilityMinimums.Count == 0 && previewRace.AbilityMaximums.Count == 0)
            sb.AppendLine(previewRace.RacialAbilities.Count == 0
                ? "No special ability restrictions or racial abilities."
                : "");

        RaceInfo.Text = sb.ToString().Trim();
    }

    private void Advance()
    {
        int idx = RaceList.SelectedIndex;
        if (idx < 0)
        {
            MessageBox.Show("Please choose a race.", "Selection Required",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var selected = _races[idx];
        var newBaseId = string.IsNullOrWhiteSpace(selected.BaseRaceId) ? selected.Id : selected.BaseRaceId;

        bool raceChanging = !string.Equals(_app.CharGen.BaseRaceId, newBaseId, System.StringComparison.OrdinalIgnoreCase);

        if (_app.CharGen.IsLevelUpMode && raceChanging)
        {
            var confirm = MessageBox.Show(
                "⚠  You are changing this character's race.\n\nDid your DM say this was OK?",
                "Race Change — DM Approval Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        if (raceChanging)
        {
            _app.CharGen.RaceId = "";
            _app.CharGen.SelectedRacialAbilityIds.Clear();
        }

        _app.CharGen.BaseRaceId = newBaseId;
        _app.GoTo("chargen_subrace");
    }

    private static string FormatBaseRaceName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var normalized = raw.Replace("_", "-").Trim().ToLowerInvariant();
        var parts = normalized.Split('-', System.StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private RaceDefinition ResolvePreviewRace(RaceDefinition baseRace)
    {
        var baseId = string.IsNullOrWhiteSpace(baseRace.BaseRaceId) ? baseRace.Id : baseRace.BaseRaceId;
        if (string.IsNullOrWhiteSpace(baseId))
            return baseRace;

        var subraces = _app.Rules.SubracesForBase(_app.CharGen.CharacterMode, baseId).ToList();
        if (subraces.Count == 0)
            return baseRace;

        // If a subrace is already selected for this base race, show that exact preview.
        if (!string.IsNullOrWhiteSpace(_app.CharGen.RaceId))
        {
            var selected = subraces.FirstOrDefault(r =>
                string.Equals(r.Id, _app.CharGen.RaceId, System.StringComparison.OrdinalIgnoreCase));
            if (selected != null)
                return selected;
        }

        // Otherwise default to "Basic" subrace when available, then first available option.
        return subraces.FirstOrDefault(r => r.Name.StartsWith("Basic ", System.StringComparison.OrdinalIgnoreCase))
            ?? subraces[0];
    }
}
