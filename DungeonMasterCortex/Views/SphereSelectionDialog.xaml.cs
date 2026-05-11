using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace DungeonMasterCortex.Views;

public partial class SphereSelectionDialog : Window
{
    public class SphereItem : INotifyPropertyChanged
    {
        private bool _isMinor;
        private bool _isMajor;

        public string SphereName  { get; set; } = "";
        public string SphereLabel { get; set; } = "";
        public int    MinorCost   { get; set; }
        public int    MajorCost   { get; set; }

        public string MinorLabel    => $"Minor  ({MinorCost} CP)";
        public string MajorLabel    => $"Major  ({MajorCost} CP)";
        public string MinorCostStr  => $"({MinorCost} CP)";
        public string MajorCostStr  => $"({MajorCost} CP)";

        public bool IsMinor
        {
            get => _isMinor;
            set { _isMinor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectionNote)); }
        }

        public bool IsMajor
        {
            get => _isMajor;
            set { _isMajor = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectionNote)); }
        }

        public string SelectionNote => (IsMinor, IsMajor) switch
        {
            (true,  true)  => "Both selected",
            (true,  false) => "Minor selected",
            (false, true)  => "Major selected",
            _              => ""
        };

        public string CurrentSelection => (IsMinor, IsMajor) switch
        {
            (true,  true)  => "both",
            (true,  false) => "minor",
            (false, true)  => "major",
            _              => "none"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private readonly ObservableCollection<SphereItem> _items = new();
    private Dictionary<string, string> _selections = new();

    // Standard priest spheres in AD&D 2e
    private static readonly string[] StandardSpheres = new[]
    {
        "All", "Animal", "Astral", "Chaos", "Charm", "Combat", "Creation", "Divination",
        "Elemental", "Elemental Air", "Elemental Earth", "Elemental Fire", "Elemental Water",
        "Evil", "Good", "Guardian", "Healing", "Knowledge", "Law",
        "Life", "Magic", "Necromantic", "Numbers", "Plant", "Protection", "Spells", "Summoning", "Sun",
        "Thought", "Time", "Travelers", "War", "Wards", "Weather"
    };

    public SphereSelectionDialog(Dictionary<string, string> currentSelections)
    {
        InitializeComponent();
        _selections = CharGenClassScreen.NormalizeSphereSelections(currentSelections ?? new());
        InitializeSpheres();
    }

    private void InitializeSpheres()
    {
        _items.Clear();
        foreach (var sphere in StandardSpheres)
        {
            var selection = _selections.TryGetValue(sphere, out var val) ? val : "none";
            var (minor, major) = CharGenClassScreen.SphereCosts.TryGetValue(sphere, out var costs) ? costs : (5, 10);
            var item = new SphereItem
            {
                SphereName  = sphere,
                SphereLabel = sphere,
                MinorCost   = minor,
                MajorCost   = major,
                IsMinor     = selection == "minor" || selection == "both",
                IsMajor     = selection == "major" || selection == "both",
            };
            item.PropertyChanged += (_, _) => UpdateTotalCost();
            _items.Add(item);
        }
        SphereItemsControl.ItemsSource = _items;
        UpdateTotalCost();
    }

    private void UpdateTotalCost()
    {
        int total = _items.Sum(item =>
        {
            int cost = 0;
            if (item.IsMinor) cost += item.MinorCost;
            if (item.IsMajor) cost += item.MajorCost;
            return cost;
        });
        TotalCostLabel.Text = $"Total CP Cost:  {total}";
    }


    private void BtnOK_Click(object sender, RoutedEventArgs e)
    {
        _selections.Clear();
        foreach (var item in _items)
        {
            if (item.CurrentSelection != "none")
                _selections[item.SphereName] = item.CurrentSelection;
        }
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    public Dictionary<string, string> GetSelections()
    {
        return _selections;
    }
}
