using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Views;

public partial class WizardSchoolsDialog : Window
{
    private const int SchoolCost = 5;

    public class SchoolItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isEnabled = true;
        private bool _isAutomatic;
        private bool _isOpposed;

        public string SchoolName  { get; set; } = "";
        public string SchoolLabel { get; set; } = "";
        public int Cost { get; set; }
        public string CostLabel => IsAutomatic ? "0 CP" : $"{Cost} CP";

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusLabel)); OnSelectionChanged?.Invoke(); }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusLabel)); }
        }

        public bool IsAutomatic
        {
            get => _isAutomatic;
            set { _isAutomatic = value; OnPropertyChanged(); OnPropertyChanged(nameof(CostLabel)); OnPropertyChanged(nameof(StatusLabel)); }
        }

        public bool IsOpposed
        {
            get => _isOpposed;
            set { _isOpposed = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusLabel)); }
        }

        public string StatusLabel => IsOpposed ? "Opposed" : IsAutomatic ? "Automatic" : !IsEnabled ? "Unavailable" : IsSelected ? "Selected" : "—";

        public Action? OnSelectionChanged { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static readonly string[] WizardSchools =
    {
        "Abjuration", "Alteration", "Conjuration/Summoning", "Divination",
        "Enchantment/Charm", "Illusion", "Invocation/Evocation", "Necromancy",
        "Alchemy", "Artifice", "Dimensional", "Force", "Geometry", "Shadow", "Song",
        "Wild Magic", "Elemental (Air)", "Elemental (Earth)", "Elemental (Fire)", "Elemental (Water)"
    };

    private static readonly HashSet<string> ClassicWizardSchools = new(WizardSchools.Take(8), System.StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> PrimarySchoolBySpecializationId = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["spec_abjurer"] = "Abjuration",
        ["spec_alchemist"] = "Alchemy",
        ["spec_transmuter"] = "Alteration",
        ["spec_conjurer"] = "Conjuration/Summoning",
        ["spec_diviner"] = "Divination",
        ["spec_enchanter"] = "Enchantment/Charm",
        ["spec_geometer"] = "Geometry",
        ["spec_illusionist"] = "Illusion",
        ["spec_invoker"] = "Invocation/Evocation",
        ["spec_necromancer"] = "Necromancy",
        ["spec_shadow"] = "Shadow",
        ["spec_song_wizard"] = "Song",
    };

    private readonly ObservableCollection<SchoolItem> _items = new();
    private readonly List<WizardSpecialization>? _specializations;
    private Dictionary<string, bool> _selections;
    private string _currentSpecId;
    private bool _suppressComboEvent;

    public WizardSchoolsDialog(
        Dictionary<string, bool> currentSelections,
        List<WizardSpecialization>? specializations,
        string? currentSpecializationId)
    {
        InitializeComponent();
        _selections    = NormalizeWizardSchoolSelections(currentSelections);
        _specializations = specializations;
        _currentSpecId = currentSpecializationId ?? "";
        InitializeSchools();
        PopulateSpecialtyCombo();
    }

    private static Dictionary<string, bool> NormalizeWizardSchoolSelections(Dictionary<string, bool>? selections)
    {
        var normalized = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
        if (selections is null)
            return normalized;

        foreach (var (school, selected) in selections)
        {
            var key = string.Equals(school, "Greater Divination", System.StringComparison.OrdinalIgnoreCase)
                ? "Divination"
                : school;

            normalized[key] = normalized.TryGetValue(key, out var existing)
                ? existing || selected
                : selected;
        }

        return normalized;
    }

    private void InitializeSchools()
    {
        _items.Clear();
        foreach (var school in WizardSchools)
        {
            _items.Add(new SchoolItem
            {
                SchoolName  = school,
                SchoolLabel = school,
                Cost        = SchoolCost,
                IsSelected  = _selections.TryGetValue(school, out var val) && val,
                IsEnabled   = true,
                OnSelectionChanged = UpdateTotalCost,
            });
        }
        SchoolItemsControl.ItemsSource = _items;
        UpdateTotalCost();
    }

    private void PopulateSpecialtyCombo()
    {
        _suppressComboEvent = true;
        SpecialtyCombo.Items.Clear();
        SpecialtyCombo.Items.Add("(None — General Wizard)");
        if (_specializations != null)
            foreach (var spec in _specializations)
                SpecialtyCombo.Items.Add(spec.Name);

        if (!string.IsNullOrEmpty(_currentSpecId) && _specializations != null)
        {
            var match = _specializations.FirstOrDefault(s =>
                string.Equals(s.Id, _currentSpecId, System.StringComparison.OrdinalIgnoreCase));
            if (match is null)
                _currentSpecId = "";
            SpecialtyCombo.SelectedItem = match?.Name ?? "(None — General Wizard)";
            if (SpecialtyCombo.SelectedItem == null) SpecialtyCombo.SelectedIndex = 0;
        }
        else
        {
            SpecialtyCombo.SelectedIndex = 0;
        }
        _suppressComboEvent = false;
        ApplySpecialtyToSchools();
    }

    private void SpecialtyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressComboEvent) return;

        if (SpecialtyCombo.SelectedIndex == 0)
        {
            _currentSpecId = "";
        }
        else if (SpecialtyCombo.SelectedItem is string name && _specializations != null)
        {
            var spec = _specializations.FirstOrDefault(s => s.Name == name);
            _currentSpecId = spec?.Id ?? "";
        }
        ApplySpecialtyToSchools();
    }

    private void ApplySpecialtyToSchools()
    {
        if (string.IsNullOrEmpty(_currentSpecId))
        {
            foreach (var item in _items)
            {
                if (item.IsAutomatic)
                    item.IsSelected = false;
                item.IsAutomatic = false;
                item.IsOpposed = false;
                item.IsEnabled = true;
            }
        }
        else
        {
            var spec = _specializations?.FirstOrDefault(s =>
                string.Equals(s.Id, _currentSpecId, System.StringComparison.OrdinalIgnoreCase));
            var opposed = spec?.OppositionSchools ?? new List<string>();
            var automaticSchools = GetAutomaticSpecialistSchools(spec);
            foreach (var item in _items)
            {
                bool isOpposed = opposed.Contains(item.SchoolName, System.StringComparer.OrdinalIgnoreCase);
                bool isAutomatic = automaticSchools.Contains(item.SchoolName);
                item.IsOpposed = isOpposed;
                item.IsAutomatic = isAutomatic;
                item.IsSelected = isAutomatic;
                item.IsEnabled = false;
            }
        }
        UpdateTotalCost();
    }

    private static HashSet<string> GetAutomaticSpecialistSchools(WizardSpecialization? specialization)
    {
        if (specialization is null)
            return new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        return ClassicWizardSchools
            .Where(school => !specialization.OppositionSchools.Contains(school, System.StringComparer.OrdinalIgnoreCase))
            .ToHashSet(System.StringComparer.OrdinalIgnoreCase);
    }

    private void UpdateTotalCost()
    {
        int total = _items.Where(item => item.IsEnabled && item.IsSelected).Sum(item => item.Cost);
        TotalCostLabel.Text = $"Total CP Cost: {total}";
    }

    private void BtnOK_Click(object sender, RoutedEventArgs e)
    {
        _selections.Clear();
        foreach (var item in _items)
        {
            if ((item.IsEnabled || item.IsAutomatic) && item.IsSelected)
                _selections[item.SchoolName] = true;
        }
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public Dictionary<string, bool> GetSelections()       => _selections;
    public string                   GetSpecializationId() => _currentSpecId;
}

