using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Views;

public partial class CharGenWizardSpecScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    // "Mage (Generalist)" sentinel + loaded specializations
    private record SpecEntry(string Id, string DisplayName, WizardSpecialization? Spec);
    private List<SpecEntry> _entries = new();

    public CharGenWizardSpecScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.SetBanner("Character Blueprint  ›  Wizard Specialization");
        _app.SetNavBar(9, 12, "Wizard Specialization",
            backAction: () => _app.GoTo("chargen_subabilities", -1),
            nextAction: Advance);

        _entries.Clear();
        _entries.Add(new SpecEntry("", "Mage (Generalist)", null));

        if (_app.Rules.Classes.TryGetValue("wizard", out var wizardClass)
            && wizardClass.Specializations is { Count: > 0 } specs)
        {
            foreach (var s in specs)
                _entries.Add(new SpecEntry(s.Id, s.Name, s));
        }

        SpecList.Items.Clear();
        foreach (var e in _entries) SpecList.Items.Add(e.DisplayName);

        // Restore previous selection
        var savedId = _app.CharGen.WizardSpecializationId ?? "";
        var savedIdx = _entries.FindIndex(e =>
            string.Equals(e.Id, savedId, StringComparison.OrdinalIgnoreCase));
        SpecList.SelectedIndex = savedIdx >= 0 ? savedIdx : 0;
    }

    private void SpecList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = SpecList.SelectedIndex;
        if (idx < 0 || idx >= _entries.Count) return;
        ShowSpecInfo(_entries[idx]);
        RefreshBudget(_entries[idx]);
        RefreshAutoGrantedList(_entries[idx]);
    }

    private void ShowSpecInfo(SpecEntry entry)
    {
        if (entry.Spec is null)
        {
            SpecTitle.Text = "Mage (Generalist)";
            SpecReqs.Text  = "No special requirements.";
            SpecOpp.Text   = "Access to all 8 schools of wizard magic. Full 40 CP budget.";
            return;
        }
        var s = entry.Spec;
        SpecTitle.Text = s.Name;
        SpecReqs.Text = s.AbilityMinimums.Count > 0
            ? "Requirements: " + string.Join(", ",
                s.AbilityMinimums.Select(kv => $"{UpperFirst(kv.Key)} {kv.Value}"))
            : "No extra ability requirements.";
        SpecOpp.Text = s.OppositionSchools.Count > 0
            ? $"Opposition schools (forbidden): {string.Join(", ", s.OppositionSchools)}\n\n{s.Description}"
            : s.Description;
    }

    private void RefreshBudget(SpecEntry entry)
    {
        // Use the specialization budget if specialist, otherwise wizard default 40
        int baseBudget = entry.Spec?.ClassPointBudget ?? 40;
        var carryover = Math.Clamp(_app.CharGen.RacialCarryoverToClassPoints, 0, 5);
        int total = baseBudget + carryover;

        // Account for already-selected class abilities cost
        var specId = entry.Id;
        var package = _app.Rules.BuildClassAbilityPackage(
            "wizard",
            _app.CharGen.SelectedClassAbilityIds,
            _app.CharGen.RacialCarryoverToClassPoints,
            specId);

        BudgetSummary.Text = total > 0 ? package.remaining.ToString() : "-";
        BudgetDetail.Text  = total > 0
            ? $"Spent {package.spent} / {package.budget} CP (incl. {carryover} racial carryover)"
            : "No CP budget";
        BudgetSummary.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(
                package.remaining < 0 ? "#C02828" : package.remaining == 0 ? "#E8C050" : "#C4A468"));
    }

    private void RefreshAutoGrantedList(SpecEntry entry)
    {
        if (entry.Spec is null)
        {
            AutoGrantedList.ItemsSource = new List<AutoItem>
            {
                new() { Label = "(Generalist mage — no specialist abilities)" }
            };
            return;
        }

        // Look up the auto-select ability definitions from the wizard class
        _app.Rules.Classes.TryGetValue("wizard", out var wizardClass);
        var autoItems = new List<AutoItem>();
        foreach (var id in entry.Spec.AutoSelectAbilityIds)
        {
            var def = wizardClass?.StructuredAbilities
                .FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            autoItems.Add(new AutoItem
            {
                Label = def is not null
                    ? $"[AUTO] ({def.PointCost} CP) {def.Description}"
                    : $"[AUTO] {id}"
            });
        }
        if (autoItems.Count == 0)
            autoItems.Add(new AutoItem { Label = "(No auto-granted abilities for this specialization)" });

        AutoGrantedList.ItemsSource = autoItems;
    }

    private void Advance()
    {
        var idx = SpecList.SelectedIndex;
        if (idx < 0 || idx >= _entries.Count)
        {
            MessageBox.Show("Please choose a specialization.", "Selection Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var entry = _entries[idx];

        // Validate ability minimums for specialist (use ModifiedAbilities which includes racial modifiers)
        if (entry.Spec?.AbilityMinimums.Count > 0)
        {
            var issues = new List<string>();
            foreach (var kv in entry.Spec.AbilityMinimums)
            {
                // Use ModifiedAbilities if available (with racial modifiers), otherwise use base Abilities
                int score = _app.CharGen.ModifiedAbilities.Count > 0
                    ? _app.CharGen.ModifiedAbilities.GetValueOrDefault(kv.Key, _app.CharGen.Abilities.GetValueOrDefault(kv.Key, 0))
                    : _app.CharGen.Abilities.GetValueOrDefault(kv.Key, 0);
                if (score < kv.Value)
                    issues.Add($"{UpperFirst(kv.Key)} {score} (need {kv.Value})");
            }
            if (issues.Count > 0)
            {
                var proceed = MessageBox.Show(
                    $"Ability score requirement not met: {string.Join(", ", issues)}\n\nProceed anyway?",
                    "Specialist Requirement",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (proceed != MessageBoxResult.Yes) return;
            }
        }

        _app.CharGen.WizardSpecializationId = entry.Id;
        _app.GoTo("chargen_review");
    }

    private static string UpperFirst(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    private class AutoItem
    {
        public string Label { get; set; } = string.Empty;
    }
}
