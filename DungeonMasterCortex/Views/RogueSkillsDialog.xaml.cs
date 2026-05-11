using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class RogueSkillsDialog : Window
{
    public class ArmorOption
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public override string ToString() => Name;
    }

    public class RogueSkillRow : INotifyPropertyChanged
    {
        private string _allocatedPointsText = "0";

        public string SkillId { get; set; } = "";
        public string SkillName { get; set; } = "";
        public int BaseScore { get; set; }
        public int RacialAdjustment { get; set; }
        public int DexterityAdjustment { get; set; }
        public int ArmorAdjustment { get; set; }
        public int FinalScore { get; set; }

        public string BaseLabel => PercentValue(BaseScore, alwaysSign: false);
        public string RacialLabel => PercentValue(RacialAdjustment, alwaysSign: true);
        public string DexLabel => PercentValue(DexterityAdjustment, alwaysSign: true);
        public string ArmorLabel => PercentValue(ArmorAdjustment, alwaysSign: true);
        public string FinalLabel => $"{FinalScore}%";

        public string AllocatedPointsText
        {
            get => _allocatedPointsText;
            set
            {
                if (_allocatedPointsText == value) return;
                _allocatedPointsText = value;
                OnPropertyChanged();
                OnAllocationChanged?.Invoke();
            }
        }

        public int AllocatedPoints => int.TryParse(AllocatedPointsText, out var value) ? Math.Max(0, value) : 0;

        public Action? OnAllocationChanged { get; set; }

        private static string PercentValue(int value, bool alwaysSign)
        {
            if (value == 0) return "0%";
            if (value > 0) return alwaysSign ? $"+{value}%" : $"{value}%";
            return $"{value}%";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void NotifyComputedChanged()
        {
            OnPropertyChanged(nameof(BaseLabel));
            OnPropertyChanged(nameof(RacialLabel));
            OnPropertyChanged(nameof(DexLabel));
            OnPropertyChanged(nameof(ArmorLabel));
            OnPropertyChanged(nameof(FinalLabel));
        }
    }

    private readonly ObservableCollection<RogueSkillRow> _rows = new();
    private readonly List<ArmorOption> _armorOptions = new()
    {
        new ArmorOption { Id = "no_armor", Name = "No Armor / Bracers" },
        new ArmorOption { Id = "elven_chain", Name = "Elven Chain" },
        new ArmorOption { Id = "studded_leather", Name = "Studded, Padded, or Hide" },
        new ArmorOption { Id = "chain_or_ring_mail", Name = "Chain or Ring Mail" },
    };

    private readonly string _raceId;
    private readonly int _dexterity;
    private readonly int _level;
    private readonly int _totalPool;
    private readonly int _perSkillCap;

    private string _armorProfile;
    private Dictionary<string, int> _allocations;

    public RogueSkillsDialog(
        IEnumerable<string> selectedSkillIds,
        Dictionary<string, int> existingAllocations,
        string? armorProfile,
        string? raceId,
        int dexterity,
        int level)
    {
        InitializeComponent();

        _armorProfile = string.IsNullOrWhiteSpace(armorProfile) ? "no_armor" : armorProfile.Trim().ToLowerInvariant();
        _allocations = new Dictionary<string, int>(existingAllocations ?? new(), StringComparer.OrdinalIgnoreCase);
        _raceId = raceId ?? string.Empty;
        _dexterity = dexterity;
        _level = Math.Max(1, level);
        _totalPool = RulesEngine.GetRogueSkillPointPoolForLevel(_level);
        _perSkillCap = RulesEngine.GetRogueSkillPerSkillAllocationCap(_level);

        IntroLabel.Text = $"Pool: {_totalPool} points (60 at 1st level, +30 per additional level). Per-skill allocation cap: {_perSkillCap}.";

        ArmorProfileCombo.ItemsSource = _armorOptions;
        var selectedArmor = _armorOptions.FirstOrDefault(a => a.Id == _armorProfile) ?? _armorOptions[0];
        ArmorProfileCombo.SelectedItem = selectedArmor;

        BuildRows(selectedSkillIds);
        SkillItemsControl.ItemsSource = _rows;
        Recalculate();
    }

    private void BuildRows(IEnumerable<string> selectedSkillIds)
    {
        _rows.Clear();
        var uniqueSkillIds = (selectedSkillIds ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(RulesEngine.GetRogueSkillName)
            .ToList();

        foreach (var skillId in uniqueSkillIds)
        {
            if (!_allocations.TryGetValue(skillId, out var points))
                points = 0;

            var row = new RogueSkillRow
            {
                SkillId = skillId,
                SkillName = RulesEngine.GetRogueSkillName(skillId),
                AllocatedPointsText = points.ToString(),
                OnAllocationChanged = Recalculate,
            };
            _rows.Add(row);
        }
    }

    private void ArmorProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArmorProfileCombo.SelectedItem is ArmorOption option)
        {
            _armorProfile = option.Id;
            Recalculate();
        }
    }

    private void Recalculate()
    {
        var normalizedAllocations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            int parsed = row.AllocatedPoints;
            int clamped = Math.Clamp(parsed, 0, _perSkillCap);
            if (clamped != parsed)
                row.AllocatedPointsText = clamped.ToString();
            normalizedAllocations[row.SkillId] = clamped;
        }

        var breakdown = RulesEngine.BuildRogueSkillBreakdown(
            _rows.Select(r => r.SkillId),
            _raceId,
            _dexterity,
            _armorProfile,
            normalizedAllocations,
            _level);

        var byId = breakdown.ToDictionary(b => b.SkillId, b => b, StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows)
        {
            if (!byId.TryGetValue(row.SkillId, out var item)) continue;
            row.BaseScore = item.BaseScore;
            row.RacialAdjustment = item.RacialAdjustment;
            row.DexterityAdjustment = item.DexterityAdjustment;
            row.ArmorAdjustment = item.ArmorAdjustment;
            row.FinalScore = item.FinalScore;
            row.NotifyComputedChanged();
        }

        int spent = normalizedAllocations.Values.Sum();
        int remaining = _totalPool - spent;

        PoolSummaryLabel.Text = $"Allocated {spent} / {_totalPool} points. Remaining: {remaining}.";
        ValidationLabel.Text = remaining < 0
            ? $"You are over budget by {-remaining} points."
            : string.Empty;
        BtnOK.IsEnabled = remaining >= 0;

        _allocations = normalizedAllocations;
    }

    private void BtnOK_Click(object sender, RoutedEventArgs e)
    {
        if (_allocations.Values.Sum() > _totalPool)
        {
            MessageBox.Show("Rogue skill points exceed the available pool.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public Dictionary<string, int> GetAllocations() => new(_allocations, StringComparer.OrdinalIgnoreCase);

    public string GetArmorProfile() => _armorProfile;
}
