using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public sealed class SpellExportSelectionDialog : Window
{
    private sealed class SpellExportRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public required SpellDefinition Spell { get; init; }
        public bool IsRequired { get; init; }
        public string Id => Spell.Id;
        public string Name => Spell.Name;
        public string Category => Spell.Category;
        public int Level => int.TryParse(Spell.Level, out int level) ? level : 0;
        public string Schools => Spell.Schools;
        public string Requirement => IsRequired ? "Required" : string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                bool next = IsRequired || value;
                if (_isSelected == next)
                    return;
                _isSelected = next;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly ObservableCollection<SpellExportRow> _rows = new();
    private readonly ICollectionView _view;
    private readonly TextBox _searchBox;
    private readonly ComboBox _categoryFilter;
    private readonly ComboBox _levelFilter;
    private readonly TextBlock _statusText;

    public SpellExportSelectionDialog(
        CharacterSheet character,
        IReadOnlyList<SpellDefinition> candidates,
        IReadOnlySet<string> requiredIds)
    {
        Title = $"Select Spells for Export - {character.Name}";
        Width = 900;
        Height = 700;
        MinWidth = 700;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        SetResourceReference(BackgroundProperty, "BrushPanel");
        SetResourceReference(ForegroundProperty, "BrushText");

        var selectedIds = new HashSet<string>(character.ExportSpellIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (SpellDefinition spell in candidates)
        {
            bool required = requiredIds.Contains(spell.Id);
            var row = new SpellExportRow { Spell = spell, IsRequired = required };
            row.IsSelected = required || selectedIds.Contains(spell.Id);
            row.PropertyChanged += (_, _) => UpdateStatus();
            _rows.Add(row);
        }

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = MatchesFilters;

        var root = new Grid { Margin = new Thickness(24, 20, 24, 20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var title = new TextBlock { Text = "SELECT SPELLS FOR EXPORT", FontSize = 18, Margin = new Thickness(0, 0, 0, 4) };
        title.SetResourceReference(StyleProperty, "TitleText");
        heading.Children.Add(title);
        var explanation = new TextBlock
        {
            Text = "Choose spell definitions to include in Codex and Foundry exports. Prepared, cast, and spellbook spells are required automatically.",
            TextWrapping = TextWrapping.Wrap,
        };
        explanation.SetResourceReference(StyleProperty, "SubtitleText");
        heading.Children.Add(explanation);
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var filters = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        _searchBox = new TextBox { Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 5, 8, 5) };
        _searchBox.ToolTip = "Search spell name, ID, school, or sphere";
        _searchBox.TextChanged += (_, _) => RefreshFilters();
        Grid.SetColumn(_searchBox, 0);
        filters.Children.Add(_searchBox);

        _categoryFilter = new ComboBox
        {
            ItemsSource = new[] { "All categories", "Arcane", "Divine", "Psionic" },
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 8, 0),
        };
        _categoryFilter.SelectionChanged += (_, _) => RefreshFilters();
        Grid.SetColumn(_categoryFilter, 1);
        filters.Children.Add(_categoryFilter);

        _levelFilter = new ComboBox
        {
            ItemsSource = new[] { "All levels", "Level 0", "Level 1", "Level 2", "Level 3", "Level 4", "Level 5", "Level 6", "Level 7", "Level 8", "Level 9" },
            SelectedIndex = 0,
        };
        _levelFilter.SelectionChanged += (_, _) => RefreshFilters();
        Grid.SetColumn(_levelFilter, 2);
        filters.Children.Add(_levelFilter);
        Grid.SetRow(filters, 1);
        root.Children.Add(filters);

        var spellGrid = new DataGrid
        {
            ItemsSource = _view,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            BorderThickness = new Thickness(1),
        };
        spellGrid.SetResourceReference(BackgroundProperty, "BrushInputBg");
        spellGrid.SetResourceReference(ForegroundProperty, "BrushText");
        spellGrid.SetResourceReference(BorderBrushProperty, "BrushBorder2");
        spellGrid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "Export",
            Width = 70,
            Binding = new Binding(nameof(SpellExportRow.IsSelected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
        });
        spellGrid.Columns.Add(new DataGridTextColumn { Header = "Spell", Width = new DataGridLength(1, DataGridLengthUnitType.Star), Binding = new Binding(nameof(SpellExportRow.Name)), IsReadOnly = true });
        spellGrid.Columns.Add(new DataGridTextColumn { Header = "Type", Width = 90, Binding = new Binding(nameof(SpellExportRow.Category)), IsReadOnly = true });
        spellGrid.Columns.Add(new DataGridTextColumn { Header = "Level", Width = 60, Binding = new Binding(nameof(SpellExportRow.Level)), IsReadOnly = true });
        spellGrid.Columns.Add(new DataGridTextColumn { Header = "School / Sphere", Width = 210, Binding = new Binding(nameof(SpellExportRow.Schools)), IsReadOnly = true });
        spellGrid.Columns.Add(new DataGridTextColumn { Header = "", Width = 75, Binding = new Binding(nameof(SpellExportRow.Requirement)), IsReadOnly = true });
        Grid.SetRow(spellGrid, 2);
        root.Children.Add(spellGrid);

        var footer = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var selectionControls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _statusText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        _statusText.SetResourceReference(StyleProperty, "SubtitleText");
        selectionControls.Children.Add(_statusText);
        selectionControls.Children.Add(CreateButton("Select visible", SelectVisible, "DarkButton"));
        selectionControls.Children.Add(CreateButton("Clear optional", ClearOptional, "DarkButton"));
        Grid.SetColumn(selectionControls, 0);
        footer.Children.Add(selectionControls);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(CreateButton("Confirm", (_, _) => DialogResult = true, "GoldButton"));
        actions.Children.Add(CreateButton("Cancel", (_, _) => DialogResult = false, "DarkButton", true));
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Content = root;
        UpdateStatus();
    }

    public List<string> GetSelectedSpellIds()
        => _rows.Where(row => row.IsSelected).Select(row => row.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private Button CreateButton(string text, RoutedEventHandler handler, string style, bool isLast = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = isLast ? new Thickness(0) : new Thickness(0, 0, 8, 0),
        };
        button.SetResourceReference(StyleProperty, style);
        button.Click += handler;
        return button;
    }

    private bool MatchesFilters(object item)
    {
        if (item is not SpellExportRow row)
            return false;
        string search = (_searchBox?.Text ?? string.Empty).Trim();
        if (search.Length > 0
            && !row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            && !row.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
            && !row.Schools.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string category = _categoryFilter?.SelectedItem as string ?? "All categories";
        if (!category.StartsWith("All", StringComparison.OrdinalIgnoreCase)
            && !row.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int selectedLevel = (_levelFilter?.SelectedIndex ?? 0) - 1;
        return selectedLevel < 0 || row.Level == selectedLevel;
    }

    private void RefreshFilters()
    {
        _view.Refresh();
        UpdateStatus();
    }

    private void SelectVisible(object sender, RoutedEventArgs e)
    {
        foreach (SpellExportRow row in _view.Cast<SpellExportRow>())
            row.IsSelected = true;
        UpdateStatus();
    }

    private void ClearOptional(object sender, RoutedEventArgs e)
    {
        foreach (SpellExportRow row in _rows.Where(row => !row.IsRequired))
            row.IsSelected = false;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_statusText is null)
            return;
        int selected = _rows.Count(row => row.IsSelected);
        int required = _rows.Count(row => row.IsRequired);
        _statusText.Text = $"{selected:N0} selected ({required:N0} required)";
    }
}
