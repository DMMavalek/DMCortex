using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Views;

public class PartyManagementScreen : UserControl, IScreen
{
    private const string AllPartiesFilterLabel = "All Parties";
    private const string UnassignedPartyFilterLabel = "Unassigned";

    private readonly MainWindow _app;
    private readonly DataGrid _characterGrid;
    private readonly ComboBox _partyFilterCombo;
    private readonly ListBox _partyList;
    private readonly TextBlock _selectedCharacterText;
    private readonly TextBlock _selectedCharacterDetails;
    private readonly TextBox _playerNameEditor;
    private readonly TextBox _partyNameEditor;
    private readonly TextBlock _partyStatus;

    private string _activePartyFilter = string.Empty;
    private bool _isRefreshing;

    public UIElement View => this;

    public PartyManagementScreen(MainWindow app)
    {
        _app = app;
        (_characterGrid, _partyFilterCombo, _partyList,
         _selectedCharacterText, _selectedCharacterDetails,
         _playerNameEditor, _partyNameEditor, _partyStatus) = BuildUi();
    }

    public void OnEnter()
    {
        _app.SetBanner("Character Blueprint  ›  Party Management");
        RefreshAll();
    }

    // ── UI construction ──────────────────────────────────────────────────────

    private (DataGrid grid, ComboBox filterCombo, ListBox partyList,
             TextBlock selectedText, TextBlock selectedDetails,
             TextBox playerEditor, TextBox partyEditor, TextBlock status)
        BuildUi()
    {
        var root = new Grid { Margin = new Thickness(28, 16, 28, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // divider
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // content

        // ── Header ───────────────────────────────────────────────────────────
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);

        var title = new TextBlock
        {
            Text = "PARTY MANAGEMENT",
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(StyleProperty, "TitleText");
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        headerActions.Children.Add(MakeButton("◀ CHARACTERS", 140, BtnBack_Click, isLast: true));
        Grid.SetColumn(headerActions, 1);
        header.Children.Add(headerActions);
        root.Children.Add(header);

        var divider = new Rectangle { Height = 1, Margin = new Thickness(0, 10, 0, 12) };
        divider.SetResourceReference(Shape.FillProperty, "BrushBorder");
        Grid.SetRow(divider, 1);
        root.Children.Add(divider);

        // ── Content area ─────────────────────────────────────────────────────
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(200) }); // character grid
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });     // grid status + filter row
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });  // spacer
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // management panel
        Grid.SetRow(content, 2);

        // Character grid
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            AlternationCount = 2,
            BorderThickness = new Thickness(1)
        };
        grid.RowStyle = (Style)FindResource("AlternatingDataGridRow");
        grid.SetResourceReference(BackgroundProperty, "BrushInputBg");
        grid.SetResourceReference(ForegroundProperty, "BrushText");
        grid.SetResourceReference(BorderBrushProperty, "BrushBorder2");
        grid.SelectionChanged += CharacterGrid_SelectionChanged;

        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(BackgroundProperty, TryFindResource("BrushBtn")));
        headerStyle.Setters.Add(new Setter(ForegroundProperty, TryFindResource("BrushBtnTxt")));
        headerStyle.Setters.Add(new Setter(BorderBrushProperty, TryFindResource("BrushBorder")));
        headerStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        headerStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(8, 6, 8, 6)));
        headerStyle.Setters.Add(new Setter(FontWeightProperty, FontWeights.Bold));
        grid.ColumnHeaderStyle = headerStyle;

        AddCol(grid, "Name", "Name", 145);
        AddCol(grid, "Player", "PlayerName", 110);
        AddCol(grid, "Party", "Party", 100);
        AddCol(grid, "Race", "Race", 80);
        AddCol(grid, "Class", "Class", 90);
        AddCol(grid, "Lvl", "Level", 45);
        Grid.SetRow(grid, 0);
        content.Children.Add(grid);

        // Filter row below the grid
        var filterRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0)
        };
        filterRow.Children.Add(new TextBlock
        {
            Text = "Filter by Party:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var filterCombo = new ComboBox { MinWidth = 180, Margin = new Thickness(0, 0, 0, 0) };
        filterCombo.SelectionChanged += PartyFilterCombo_SelectionChanged;
        filterRow.Children.Add(filterCombo);
        Grid.SetRow(filterRow, 1);
        content.Children.Add(filterRow);

        // Management panel
        var mgmtBorder = new Border
        {
            Padding = new Thickness(10),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6)
        };
        mgmtBorder.SetResourceReference(BorderBrushProperty, "BrushBorder");
        mgmtBorder.SetResourceReference(BackgroundProperty, "BrushInputBg");
        Grid.SetRow(mgmtBorder, 3);

        var mgmt = new Grid();
        mgmt.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        mgmt.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        mgmt.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mgmt.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mgmt.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var mgmtTitle = new TextBlock
        {
            Text = "ASSIGN CHARACTERS",
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 6)
        };
        mgmtTitle.SetResourceReference(StyleProperty, "TitleText");
        Grid.SetColumnSpan(mgmtTitle, 3);
        Grid.SetRow(mgmtTitle, 0);
        mgmt.Children.Add(mgmtTitle);

        // Left: Known parties list
        var leftPanel = new StackPanel { Orientation = Orientation.Vertical };
        Grid.SetColumn(leftPanel, 0);
        Grid.SetRow(leftPanel, 1);
        leftPanel.Children.Add(new TextBlock { Text = "Known Parties", Margin = new Thickness(0, 0, 0, 2) });
        var partyList = new ListBox { MinHeight = 80, MaxHeight = 120, Margin = new Thickness(0, 0, 0, 4) };
        partyList.SelectionChanged += PartyList_SelectionChanged;
        leftPanel.Children.Add(partyList);
        mgmt.Children.Add(leftPanel);

        // Right: Selected character + editors + buttons
        var rightPanel = new StackPanel { Orientation = Orientation.Vertical };
        Grid.SetColumn(rightPanel, 2);
        Grid.SetRow(rightPanel, 1);

        var selectedText = new TextBlock
        {
            Text = "Selected character: none",
            Margin = new Thickness(0, 0, 0, 4),
            TextWrapping = TextWrapping.Wrap
        };
        selectedText.SetResourceReference(StyleProperty, "SubtitleText");
        rightPanel.Children.Add(selectedText);

        var selectedDetails = new TextBlock
        {
            Text = "Level: -   Race: -   Class: -",
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };
        selectedDetails.SetResourceReference(StyleProperty, "SubtitleText");
        rightPanel.Children.Add(selectedDetails);

        rightPanel.Children.Add(new TextBlock { Text = "Player Name", Margin = new Thickness(0, 0, 0, 2) });
        var playerEditor = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(6, 3, 6, 3)
        };
        rightPanel.Children.Add(playerEditor);

        rightPanel.Children.Add(new TextBlock { Text = "Party Name", Margin = new Thickness(0, 0, 0, 2) });
        var partyEditor = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(6, 3, 6, 3)
        };
        rightPanel.Children.Add(partyEditor);

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        btnRow.Children.Add(MakeButton("ASSIGN TO PARTY", 120, BtnAssignToSelectedParty_Click));
        btnRow.Children.Add(MakeButton("APPLY", 80, BtnApplyToCharacter_Click));
        btnRow.Children.Add(MakeButton("CLEAR", 80, BtnClearCharacterParty_Click));
        btnRow.Children.Add(MakeButton("CREATE", 80, BtnCreateParty_Click));
        btnRow.Children.Add(MakeButton("RENAME", 90, BtnRenameSelectedParty_Click));
        btnRow.Children.Add(MakeButton("DELETE", 88, BtnDeleteSelectedParty_Click, isLast: true, style: "DangerButton"));
        rightPanel.Children.Add(btnRow);

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        status.SetResourceReference(StyleProperty, "SubtitleText");
        rightPanel.Children.Add(status);

        mgmt.Children.Add(rightPanel);
        mgmtBorder.Child = mgmt;
        content.Children.Add(mgmtBorder);

        root.Children.Add(content);
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root
        };

        return (grid, filterCombo, partyList, selectedText, selectedDetails, playerEditor, partyEditor, status);
    }

    private Button MakeButton(string label, double width, RoutedEventHandler onClick,
                              bool isLast = false, string style = "GoldButton")
    {
        var btn = new Button
        {
            Content = label,
            Width = width,
            Margin = isLast ? new Thickness(0) : new Thickness(0, 0, 8, 0)
        };
        btn.SetResourceReference(StyleProperty, style);
        btn.Click += onClick;
        return btn;
    }

    private static void AddCol(DataGrid grid, string header, string binding, double width)
    {
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Width = width,
            Binding = new Binding(binding)
        });
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void BtnBack_Click(object sender, RoutedEventArgs e)
        => _app.GoTo("characters", -1);

    private void CharacterGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_characterGrid.SelectedItem is CharacterRow row
            && row.SourceIndex >= 0 && row.SourceIndex < _app.Characters.Count)
        {
            var c = _app.Characters[row.SourceIndex];
            _selectedCharacterText.Text = $"Selected character: {c.Name}";
            _selectedCharacterDetails.Text =
                $"Level: {Math.Max(1, c.Level)}   HP: {c.HitPoints}   Race: {(string.IsNullOrWhiteSpace(c.RaceName) ? c.RaceId : c.RaceName)}   Class: {(string.IsNullOrWhiteSpace(c.ClassName) ? c.ClassId : c.ClassName)}";
            _playerNameEditor.Text = c.PlayerName ?? string.Empty;
            _partyNameEditor.Text = c.Party ?? string.Empty;
            return;
        }
        _selectedCharacterText.Text = "Selected character: none";
        _selectedCharacterDetails.Text = "Level: -   Race: -   Class: -";
        _playerNameEditor.Text = string.Empty;
        _partyNameEditor.Text = string.Empty;
    }

    private void PartyFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;
        if (_partyFilterCombo.SelectedItem is not string selected) return;

        _activePartyFilter = string.Equals(selected, AllPartiesFilterLabel, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : string.Equals(selected, UnassignedPartyFilterLabel, StringComparison.OrdinalIgnoreCase)
                ? UnassignedPartyFilterLabel
                : selected;

        RefreshAll();
    }

    private void PartyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_partyList.SelectedItem is string selected)
            _partyNameEditor.Text = selected;
    }

    private void BtnAssignToSelectedParty_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedCharacter(out var character)) return;
        if (_partyList.SelectedItem is not string selectedParty || string.IsNullOrWhiteSpace(selectedParty))
        {
            MessageBox.Show("Select a party from Known Parties first.", "Party Management",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        character!.Party = selectedParty;
        character.LastModified = DateTime.Now;
        character.Revision = Math.Max(1, character.Revision + 1);
        _app.SaveCharacters();
        RefreshAll();
        _partyStatus.Text = $"Assigned {character.Name} to party '{selectedParty}'.";
    }

    private void BtnApplyToCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedCharacter(out var character)) return;
        string newPlayerName = (_playerNameEditor.Text ?? string.Empty).Trim();
        string newPartyName = (_partyNameEditor.Text ?? string.Empty).Trim();
        character!.PlayerName = newPlayerName;
        character.Party = newPartyName;
        character.LastModified = DateTime.Now;
        character.Revision = Math.Max(1, character.Revision + 1);
        _app.SaveCharacters();
        RefreshAll();
        _partyStatus.Text = string.IsNullOrWhiteSpace(newPartyName)
            ? $"Updated {character.Name}. Character is now unassigned."
            : $"Updated {character.Name}. Assigned to party '{newPartyName}'.";
    }

    private void BtnClearCharacterParty_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedCharacter(out var character)) return;
        character!.Party = string.Empty;
        character.LastModified = DateTime.Now;
        character.Revision = Math.Max(1, character.Revision + 1);
        _app.SaveCharacters();
        RefreshAll();
        _partyStatus.Text = $"Cleared party assignment for {character.Name}.";
    }

    private void BtnCreateParty_Click(object sender, RoutedEventArgs e)
    {
        string newPartyName = (_partyNameEditor.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newPartyName))
        {
            MessageBox.Show("Enter a party name first.", "Party Management",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!TryGetSelectedCharacter(out var character)) return;
        character!.Party = newPartyName;
        character.LastModified = DateTime.Now;
        character.Revision = Math.Max(1, character.Revision + 1);
        _app.SaveCharacters();
        _activePartyFilter = newPartyName;
        RefreshAll();
        _partyStatus.Text = $"Created/updated party '{newPartyName}' with {character.Name}.";
    }

    private void BtnRenameSelectedParty_Click(object sender, RoutedEventArgs e)
    {
        if (_partyList.SelectedItem is not string selectedParty || string.IsNullOrWhiteSpace(selectedParty))
        {
            MessageBox.Show("Select a party from Known Parties first.", "Party Management",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string newPartyName = (_partyNameEditor.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(newPartyName))
        {
            MessageBox.Show("Enter a new party name.", "Party Management",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.Equals(selectedParty, newPartyName, StringComparison.OrdinalIgnoreCase))
        {
            _partyStatus.Text = "Party name is unchanged.";
            return;
        }
        int updatedCount = 0;
        foreach (var c in _app.Characters)
        {
            if (!string.Equals((c.Party ?? string.Empty).Trim(), selectedParty, StringComparison.OrdinalIgnoreCase))
                continue;
            c.Party = newPartyName;
            c.LastModified = DateTime.Now;
            c.Revision = Math.Max(1, c.Revision + 1);
            updatedCount++;
        }
        if (updatedCount == 0)
        {
            _partyStatus.Text = "No members were found for the selected party.";
            return;
        }
        _app.SaveCharacters();
        _activePartyFilter = newPartyName;
        RefreshAll();
        _partyStatus.Text = $"Renamed party '{selectedParty}' to '{newPartyName}' for {updatedCount} character{(updatedCount == 1 ? "" : "s")}.";
    }

    private void BtnDeleteSelectedParty_Click(object sender, RoutedEventArgs e)
    {
        if (_partyList.SelectedItem is not string selectedParty || string.IsNullOrWhiteSpace(selectedParty))
        {
            MessageBox.Show("Select a party from Known Parties first.", "Party Management",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var confirm = MessageBox.Show(
            $"Remove party '{selectedParty}' from all of its members?",
            "Delete Party", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        int updatedCount = 0;
        foreach (var c in _app.Characters)
        {
            if (!string.Equals((c.Party ?? string.Empty).Trim(), selectedParty, StringComparison.OrdinalIgnoreCase))
                continue;
            c.Party = string.Empty;
            c.LastModified = DateTime.Now;
            c.Revision = Math.Max(1, c.Revision + 1);
            updatedCount++;
        }
        _app.SaveCharacters();
        _activePartyFilter = string.Empty;
        RefreshAll();
        _partyStatus.Text = $"Deleted party '{selectedParty}'. Cleared assignment for {updatedCount} character{(updatedCount == 1 ? "" : "s")}.";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool TryGetSelectedCharacter(out CharacterSheet? character)
    {
        character = null;
        if (_characterGrid.SelectedItem is not CharacterRow row
            || row.SourceIndex < 0 || row.SourceIndex >= _app.Characters.Count)
        {
            MessageBox.Show("Select a character in the list first.", "Party Management",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        character = _app.Characters[row.SourceIndex];
        return true;
    }

    private void RefreshAll()
    {
        _isRefreshing = true;

        var knownParties = _app.Characters
            .Select(x => (x.Party ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Update party list
        string savedParty = _partyList.SelectedItem as string ?? string.Empty;
        _partyList.ItemsSource = knownParties;
        if (!string.IsNullOrWhiteSpace(savedParty)
            && knownParties.Any(x => string.Equals(x, savedParty, StringComparison.OrdinalIgnoreCase)))
            _partyList.SelectedItem = knownParties.First(x => string.Equals(x, savedParty, StringComparison.OrdinalIgnoreCase));

        // Update filter combo
        string currentLabel = string.IsNullOrWhiteSpace(_activePartyFilter)
            ? AllPartiesFilterLabel
            : _activePartyFilter;
        var filterItems = new List<string> { AllPartiesFilterLabel, UnassignedPartyFilterLabel };
        filterItems.AddRange(knownParties);
        _partyFilterCombo.ItemsSource = filterItems;
        _partyFilterCombo.SelectedItem = filterItems.Contains(currentLabel, StringComparer.OrdinalIgnoreCase)
            ? filterItems.First(x => string.Equals(x, currentLabel, StringComparison.OrdinalIgnoreCase))
            : AllPartiesFilterLabel;

        _isRefreshing = false;

        // Rebuild character grid rows
        var rows = new List<CharacterRow>();
        int idx = 0;
        foreach (var c in _app.Characters)
        {
            if (MatchesFilter(c.Party))
            {
                rows.Add(new CharacterRow
                {
                    SourceIndex = idx,
                    Name = c.Name,
                    PlayerName = c.PlayerName,
                    Party = c.Party,
                    Race = c.RaceName,
                    Class = c.ClassName,
                    Level = Math.Max(1, c.Level).ToString()
                });
            }
            idx++;
        }
        _characterGrid.ItemsSource = rows;

        int unassigned = _app.Characters.Count(x => string.IsNullOrWhiteSpace((x.Party ?? string.Empty).Trim()));
        _partyStatus.Text = $"{knownParties.Count} part{(knownParties.Count == 1 ? "y" : "ies")} tracked, {unassigned} unassigned character{(unassigned == 1 ? "" : "s")}.";
    }

    private bool MatchesFilter(string? party)
    {
        string normalized = (party ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(_activePartyFilter)) return true;
        if (string.Equals(_activePartyFilter, UnassignedPartyFilterLabel, StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(normalized);
        return string.Equals(normalized, _activePartyFilter, StringComparison.OrdinalIgnoreCase);
    }

    // ── Row DTO ──────────────────────────────────────────────────────────────

    private class CharacterRow
    {
        public int SourceIndex { get; set; }
        public string Name { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public string Party { get; set; } = "";
        public string Race { get; set; } = "";
        public string Class { get; set; } = "";
        public string Level { get; set; } = "";
    }
}
