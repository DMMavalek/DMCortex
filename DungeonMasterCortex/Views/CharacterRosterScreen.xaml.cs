using System.Collections.Generic;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public class CharacterRosterScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    private readonly DataGrid CharacterGrid;
    private readonly TextBlock RosterStatus;

    public UIElement View => this;

    public CharacterRosterScreen(MainWindow app)
    {
        _app = app;
        (CharacterGrid, RosterStatus) = BuildUi();
    }

    private static int GetDisplayedClassCount(CharacterSheet character)
    {
        if (character.ClassIds is { Count: > 0 })
            return character.ClassIds.Count;

        if (!string.IsNullOrWhiteSpace(character.ClassId))
        {
            var parts = character.ClassId
                .Split(new[] { '/', '+', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1)
                return parts.Length;
        }

        return 1;
    }

    private static string FormatLevelDisplay(CharacterSheet character)
    {
        int level = Math.Max(1, character.Level);
        int classCount = Math.Max(1, GetDisplayedClassCount(character));
        if (classCount <= 1)
            return level.ToString();

        var parts = new List<string>(classCount);
        for (int i = 0; i < classCount; i++)
            parts.Add(level.ToString());
        return string.Join("/", parts);
    }

    public void OnEnter()
    {
        _app.SetBanner("Character Generator  ›  Character Roster");
        RefreshGrid();
    }

    private (DataGrid Grid, TextBlock Status) BuildUi()
    {
        var root = new Grid { Margin = new Thickness(28, 16, 28, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(header, 0);
        var title = new TextBlock
        {
            Text = "CHARACTERS",
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.SetResourceReference(StyleProperty, "TitleText");
        Grid.SetColumn(title, 0);
        header.Children.Add(title);

        var actions = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var actionsTop = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actionsTop.Children.Add(CreateHeaderButton("+ NEW CHARACTER", 170, BtnNewCharacter_Click));
        actionsTop.Children.Add(CreateHeaderButton("+ ADD EXISTING", 170, BtnAddExistingCharacter_Click));
        actionsTop.Children.Add(CreateHeaderButton("REFRESH", 100, BtnRefresh_Click));
        actionsTop.Children.Add(CreateHeaderButton("CHAR SHEETS", 130, BtnCharacterSheets_Click, isLast: true));
        Grid.SetRow(actionsTop, 0);

        var actionsBottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };
        actionsBottom.Children.Add(CreateHeaderButton("UPDATE", 100, BtnLevelUp_Click));
        actionsBottom.Children.Add(CreateHeaderButton("± CP ADJUST", 120, BtnAwardBonusCp_Click));
        actionsBottom.Children.Add(CreateHeaderButton("◀ HUB", 100, BtnHub_Click, isLast: true));
        Grid.SetRow(actionsBottom, 1);

        actions.Children.Add(actionsTop);
        actions.Children.Add(actionsBottom);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        root.Children.Add(header);

        var divider = new Rectangle { Height = 1, Margin = new Thickness(0, 10, 0, 12) };
        divider.SetResourceReference(Shape.FillProperty, "BrushBorder");
        Grid.SetRow(divider, 1);
        root.Children.Add(divider);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(content, 2);

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            AlternatingRowBackground = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1)
        };
        grid.SetResourceReference(BackgroundProperty, "BrushInputBg");
        grid.SetResourceReference(ForegroundProperty, "BrushText");
        grid.SetResourceReference(BorderBrushProperty, "BrushBorder2");
        grid.MouseDoubleClick += CharacterGrid_MouseDoubleClick;
        Grid.SetRow(grid, 0);

        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(BackgroundProperty, TryFindResource("BrushBtn")));
        headerStyle.Setters.Add(new Setter(ForegroundProperty, TryFindResource("BrushBtnTxt")));
        headerStyle.Setters.Add(new Setter(BorderBrushProperty, TryFindResource("BrushBorder")));
        headerStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        headerStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(8, 6, 8, 6)));
        headerStyle.Setters.Add(new Setter(FontWeightProperty, FontWeights.Bold));
        grid.ColumnHeaderStyle = headerStyle;

        AddTextColumn(grid, "Name", "Name", 145);
        AddTextColumn(grid, "Player", "PlayerName", 90);
        AddTextColumn(grid, "Party", "Party", 75);
        AddTextColumn(grid, "Race", "Race", 70);
        AddTextColumn(grid, "Class", "Class", 85);
        AddTextColumn(grid, "Lvl", "Level", 45);
        AddTextColumn(grid, "XP", "ExperiencePoints", 70);
        AddTextColumn(grid, "HP", "HitPoints", 45);
        AddTextColumn(grid, "AC", "ArmorClass", 40);
        AddTextColumn(grid, "THAC0", "Thac0", 50);
        AddTextColumn(grid, "Rev", "Revision", 45);
        AddTextColumn(grid, "Updated", "LastModified", 80);
        content.Children.Add(grid);

        var status = new TextBlock
        {
            Margin = new Thickness(2, 10, 0, 0),
            Text = "No characters yet. Click NEW CHARACTER or ADD EXISTING to start."
        };
        status.SetResourceReference(StyleProperty, "SubtitleText");
        Grid.SetRow(status, 1);
        content.Children.Add(status);

        var bottomActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0)
        };
        bottomActions.Children.Add(CreateHeaderButton("⬆  EXPORT", 110, BtnExport_Click));
        bottomActions.Children.Add(CreateHeaderButton("⬇  IMPORT", 110, BtnImport_Click));
        bottomActions.Children.Add(CreateHeaderButton("🗑  DELETE", 110, BtnDelete_Click, isLast: true, style: "DangerButton"));
        Grid.SetRow(bottomActions, 2);
        content.Children.Add(bottomActions);

        root.Children.Add(content);
        Content = root;
        return (grid, status);
    }

    private Button CreateHeaderButton(string content, double width, RoutedEventHandler onClick, bool isLast = false, string style = "GoldButton")
    {
        var button = new Button
        {
            Content = content,
            Width = width,
            Margin = isLast ? new Thickness(0) : new Thickness(0, 0, 8, 0)
        };
        button.SetResourceReference(StyleProperty, style);
        button.Click += onClick;
        return button;
    }

    private static void AddTextColumn(DataGrid grid, string header, string bindingPath, double width)
    {
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Width = width,
            Binding = new Binding(bindingPath)
        });
    }

    private void BtnHub_Click(object sender, RoutedEventArgs e) =>
        _app.GoTo("hub", -1);

    private void BtnCharacterSheets_Click(object sender, RoutedEventArgs e)
    {
        int selectedIndex = -1;
        if (CharacterGrid.SelectedItem is CharacterRow selected && selected.SourceIndex >= 0 && selected.SourceIndex < _app.Characters.Count)
            selectedIndex = selected.SourceIndex;

        _app.OpenCharacterSheets(selectedIndex);
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterGrid.SelectedItem is not CharacterRow selected)
        {
            MessageBox.Show("Select a character in the roster to export.", "Export",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (selected.SourceIndex < 0 || selected.SourceIndex >= _app.Characters.Count) return;

        var c = _app.Characters[selected.SourceIndex];
        var dlg = new SaveFileDialog
        {
            Title          = "Export Character",
            FileName       = $"{c.Name}.json",
            Filter         = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt     = ".json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            MessageBox.Show($"{c.Name} exported successfully.", "Export",
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed.\n\n{ex.Message}", "Export Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title      = "Import Character(s)",
            Filter     = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        int imported = 0;
        var errors   = new List<string>();
        var opts     = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var file in dlg.FileNames)
        {
            try
            {
                var text = File.ReadAllText(file);
                // Try single character first, then array
                CharacterSheet? single = null;
                List<CharacterSheet>? many = null;
                try   { single = JsonSerializer.Deserialize<CharacterSheet>(text, opts); }
                catch { many   = JsonSerializer.Deserialize<List<CharacterSheet>>(text, opts); }

                if (single is not null && !string.IsNullOrWhiteSpace(single.Name))
                {
                    _app.Characters.Add(single);
                    imported++;
                }
                else if (many is not null)
                {
                    foreach (var ch in many)
                        if (ch is not null && !string.IsNullOrWhiteSpace(ch.Name))
                        { _app.Characters.Add(ch); imported++; }
                }
                else
                {
                    errors.Add(System.IO.Path.GetFileName(file) + ": unrecognised format");
                }
            }
            catch (Exception ex)
            {
                errors.Add(System.IO.Path.GetFileName(file) + ": " + ex.Message);
            }
        }

        if (imported > 0) _app.SaveCharacters();
        RefreshGrid();

        var msg = $"{imported} character{(imported == 1 ? "" : "s")} imported.";
        if (errors.Count > 0)
            msg += "\n\nErrors:\n" + string.Join("\n", errors);
        MessageBox.Show(msg, "Import", MessageBoxButton.OK,
                        errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterGrid.SelectedItem is not CharacterRow selected)
        {
            MessageBox.Show("Select a character in the roster to delete.", "Delete",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (selected.SourceIndex < 0 || selected.SourceIndex >= _app.Characters.Count) return;

        var c = _app.Characters[selected.SourceIndex];
        var result = MessageBox.Show(
            $"Permanently delete '{c.Name}'? This cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        _app.Characters.RemoveAt(selected.SourceIndex);
        _app.SaveCharacters();
        RefreshGrid();
    }

    private void BtnNewCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCreateAnotherCharacterInCurrentLicense())
            return;

        _app.CharGen.Clear();
        _app.GoTo("dice_roller");
    }

    private void BtnAddExistingCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCreateAnotherCharacterInCurrentLicense())
            return;

        if (!PromptForExistingCharacterSetup(
                out int startingExperience,
                out int cpPerLevel,
                out int startingUnspentCp,
                out bool experienceIsGrandTotalForMulticlass))
            return;

        _app.CharGen.Clear();
        _app.CharGen.IsExistingCharacterMode = true;
        _app.CharGen.ExistingStartingExperience = startingExperience;
        _app.CharGen.ExistingExperienceIsGrandTotalForMulticlass = experienceIsGrandTotalForMulticlass;
        _app.CharGen.ExistingCpPerLevel = cpPerLevel;
        _app.CharGen.ExistingStartingUnspentCharacterPoints = startingUnspentCp;
        _app.CharGen.Method = "manual_entry";
        _app.GoTo("dice_roller");
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshGrid();

    private void CharacterGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => DoLevelUp();

    private void BtnLevelUp_Click(object sender, RoutedEventArgs e)
        => DoLevelUp();

    private void DoLevelUp()
    {
        if (CharacterGrid.SelectedItem is not CharacterRow selected)
        {
            MessageBox.Show("Select a character in the roster first.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.SourceIndex < 0 || selected.SourceIndex >= _app.Characters.Count)
            return;

        var character = _app.Characters[selected.SourceIndex];

        if (_app.License.IsDemoMode && character.Level >= _app.License.DemoMaxLevel)
        {
            MessageBox.Show(
                $"Demo mode allows level-ups only up to level {_app.License.DemoMaxLevel}.",
                "Demo Limit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!PromptForLevelUpGains(character, out int xpGain, out int hpGain, out int cpGain))
            return;

        _app.StartPlayerLevelUp(selected.SourceIndex, xpGain, hpGain, cpGain);
        _app.GoTo("chargen_character_options");
    }

    private bool CanCreateAnotherCharacterInCurrentLicense()
    {
        if (!_app.License.IsDemoMode)
            return true;

        if (_app.Characters.Count < _app.License.DemoMaxCharacters)
            return true;

        MessageBox.Show(
            $"Demo mode allows up to {_app.License.DemoMaxCharacters} saved characters.",
            "Demo Limit",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private void BtnAwardBonusCp_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterGrid.SelectedItem is not CharacterRow selected)
        {
            MessageBox.Show("Select a character in the roster first.", "CP Adjustment", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (selected.SourceIndex < 0 || selected.SourceIndex >= _app.Characters.Count)
            return;

        var character = _app.Characters[selected.SourceIndex];
        int awardedCp = 0;
        var window = new Window
        {
            Title = $"CP Adjustment — {character.Name}",
            Width = 340,
            Height = 210,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
        };
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Current unspent CP: {character.UnspentCharacterPoints}\n\nEnter CP adjustment (+ to award, − to spend):",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });
        var cpBox = new TextBox { Text = "0", Padding = new Thickness(6, 3, 6, 3) };
        panel.Children.Add(cpBox);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => window.DialogResult = false;
        var ok = new Button { Content = "Apply", Width = 80 };
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(cpBox.Text.Trim(), out int cp) || cp == 0)
            {
                MessageBox.Show("Enter a non-zero whole number (positive to award, negative to spend).", "CP Adjustment", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            awardedCp = cp;
            window.DialogResult = true;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(ok);
        panel.Children.Add(actions);
        window.Content = panel;

        if (window.ShowDialog() != true)
            return;

        character.UnspentCharacterPoints = Math.Max(0, character.UnspentCharacterPoints + awardedCp);
        character.LastModified = DateTime.Now;
        character.Revision = Math.Max(1, character.Revision + 1);
        _app.SaveCharacters();
        RefreshGrid();
        string adjDesc = awardedCp > 0 ? $"Awarded +{awardedCp} CP" : $"Spent {Math.Abs(awardedCp)} CP";
        MessageBox.Show(
            $"{adjDesc} for {character.Name}. Total unspent: {character.UnspentCharacterPoints}.",
            "CP Adjusted",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static bool PromptForLevelUpGains(CharacterSheet character, out int xpGain, out int hpGain, out int cpGain)
    {
        xpGain = 0;
        hpGain = 0;
        cpGain = 0;
        int selectedXp = 0;
        int selectedHp = 0;
        int selectedCp = 0;

        bool hasCpPerLevel = character.CpPerLevel > 0;
        var (hitDie, _) = CharacterProgressionService.GetHitDieInfo(character, character.Level + 1);
        int hpBonus = CharacterProgressionService.GetConAndClassHpBonus(character);
        string bonusLabel = hpBonus >= 0 ? $"+{hpBonus}" : $"{hpBonus}";
        int primeXpBonus = CharacterProgressionService.GetPrimeRequisiteBonusPercent(character);
        int abilityXpBonus = character.Bonuses?.XpModifierPercent ?? 0;
        int totalXpBonus = primeXpBonus + abilityXpBonus;
        bool hpWasRolled = false;
        bool willLevelUp = false;

        var window = new Window
        {
            Title = $"Update — {character.Name}",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Enter gains for this update:",
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.Bold,
        });

        var levelStateText = new TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            Style = (Style)Application.Current.FindResource("SubtitleText"),
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(levelStateText);

        // ── XP row ────────────────────────────────────────────────────────────
        panel.Children.Add(new TextBlock { Text = "Experience Gain:", Margin = new Thickness(0, 6, 0, 2) });
        var xpBox = new TextBox { Text = "0", Padding = new Thickness(6, 3, 6, 3) };
        panel.Children.Add(xpBox);

        var xpBonusInfo = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 2),
            Style = (Style)Application.Current.FindResource("SubtitleText"),
            TextWrapping = TextWrapping.Wrap,
        };
        var xpAlreadyIncludesBonus = new CheckBox
        {
            Content = "Entered XP already includes bonus",
            Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0xA4, 0x68)),
            Margin = new Thickness(0, 2, 0, 2),
            IsChecked = false,
        };

        if (totalXpBonus != 0)
        {
            string signPrime = primeXpBonus >= 0 ? $"+{primeXpBonus}" : $"{primeXpBonus}";
            string signAbility = abilityXpBonus >= 0 ? $"+{abilityXpBonus}" : $"{abilityXpBonus}";
            string signTotal = totalXpBonus >= 0 ? $"+{totalXpBonus}" : $"{totalXpBonus}";
            xpBonusInfo.Text = $"XP bonus: Prime Requisite {signPrime}% + racial/class {signAbility}% = {signTotal}%";
            panel.Children.Add(xpBonusInfo);
            panel.Children.Add(xpAlreadyIncludesBonus);
        }

        int GetEffectiveXpGain()
        {
            int entered = int.TryParse(xpBox.Text.Trim(), out int parsed) ? Math.Max(0, parsed) : 0;
            if (entered == 0 || totalXpBonus == 0 || xpAlreadyIncludesBonus.IsChecked == true)
                return entered;
            return CharacterProgressionService.ApplyExperienceBonus(entered, totalXpBonus);
        }

        // ── HP row ────────────────────────────────────────────────────────────
        panel.Children.Add(new TextBlock
        {
            Text = $"HP Gain  (d{hitDie}, bonuses {bonusLabel}):",
            Margin = new Thickness(0, 10, 0, 2)
        });
        var hpRow = new Grid();
        hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hpRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        var hpBox = new TextBox { Text = "0", Padding = new Thickness(6, 3, 6, 3) };
        Grid.SetColumn(hpBox, 0);
        var rollBtn = new Button
        {
            Content = "Roll",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(6, 3, 6, 3),
            ToolTip = $"Roll 1d{hitDie} and apply CON/class bonuses ({bonusLabel})"
        };
        Grid.SetColumn(rollBtn, 1);
        hpRow.Children.Add(hpBox);
        hpRow.Children.Add(rollBtn);
        panel.Children.Add(hpRow);

        void RefreshLevelState()
        {
            int enteredXp = int.TryParse(xpBox.Text.Trim(), out int parsed) ? Math.Max(0, parsed) : 0;
            int pendingXp = GetEffectiveXpGain();
            int projectedLevel = CharacterProgressionService.GetLevelForExperience(
                character.ClassId,
                Math.Max(0, character.ExperiencePoints) + pendingXp);
            willLevelUp = projectedLevel > Math.Max(1, character.Level);

            if (totalXpBonus != 0)
            {
                if (xpAlreadyIncludesBonus.IsChecked == true)
                    xpBonusInfo.Text = $"XP bonus not applied by system. Effective XP gain: {enteredXp}.";
                else
                    xpBonusInfo.Text = $"XP bonus applied by system ({(totalXpBonus >= 0 ? "+" : string.Empty)}{totalXpBonus}%). Effective XP gain: {pendingXp}.";
            }

            if (willLevelUp)
            {
                levelStateText.Text = $"Level increase detected: {character.Level} -> {projectedLevel}. Enter or roll HP gain.";
                levelStateText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC0, 0x50));
                hpRow.Visibility = Visibility.Visible;
                rollBtn.IsEnabled = true;
            }
            else
            {
                levelStateText.Text = "No level increase from current XP gain. HP gain is disabled for this update.";
                levelStateText.Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0xA4, 0x68));
                hpRow.Visibility = Visibility.Collapsed;
                rollBtn.IsEnabled = false;
                hpBox.Text = "0";
                hpWasRolled = false;
            }
        }

        xpBox.TextChanged += (_, _) => RefreshLevelState();
    xpAlreadyIncludesBonus.Checked += (_, _) => RefreshLevelState();
    xpAlreadyIncludesBonus.Unchecked += (_, _) => RefreshLevelState();
        RefreshLevelState();

        rollBtn.Click += (_, _) =>
        {
            int rolled = CharacterProgressionService.RollHpForLevel(character, character.Level + 1);
            hpBox.Text = rolled.ToString();
            hpWasRolled = true;
        };
        // Reset flag if user edits manually after rolling
        hpBox.TextChanged += (_, _) => { if (!rollBtn.IsKeyboardFocusWithin) hpWasRolled = false; };

        // ── CP row ────────────────────────────────────────────────────────────
        TextBox? cpBox = null;
        CheckBox? saveCpPerLevelCheck = null;
        if (hasCpPerLevel)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Character Points: {character.CpPerLevel} / level  (from character sheet)",
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC0, 0x50)),
            });
        }
        else
        {
            panel.Children.Add(new TextBlock { Text = "Character Point Gain:", Margin = new Thickness(0, 10, 0, 2) });
            cpBox = new TextBox { Text = "0", Padding = new Thickness(6, 3, 6, 3) };
            panel.Children.Add(cpBox);
            saveCpPerLevelCheck = new CheckBox
            {
                Content = "Use this CP gain as the default CP / level for future level-ups",
                Margin = new Thickness(0, 6, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC0, 0x50)),
            };
            panel.Children.Add(saveCpPerLevelCheck);
        }

        // ── OK / Cancel ───────────────────────────────────────────────────────
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => window.DialogResult = false;

        var ok = new Button { Content = "Start", Width = 80, IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(xpBox.Text.Trim(), out int enteredXp) || enteredXp < 0)
            {
                MessageBox.Show("Enter a non-negative whole number for XP.", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int parsedXp = GetEffectiveXpGain();
            int projectedLevel = CharacterProgressionService.GetLevelForExperience(
                character.ClassId,
                Math.Max(0, character.ExperiencePoints) + parsedXp);
            bool willLevelUpNow = projectedLevel > Math.Max(1, character.Level);

            if (willLevelUpNow && !willLevelUp)
            {
                MessageBox.Show(
                    "Applying XP bonus now causes a level increase. Enter or roll HP gain and click Start again.",
                    "Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                RefreshLevelState();
                return;
            }

            int parsedHp = 0;
            if (willLevelUpNow)
            {
                if (!int.TryParse(hpBox.Text.Trim(), out parsedHp) || parsedHp < 0)
                {
                    MessageBox.Show("Enter a non-negative whole number for HP.", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // If HP was typed manually, ask about bonuses
                if (!hpWasRolled && parsedHp > 0)
                {
                    int bonus = CharacterProgressionService.GetConAndClassHpBonus(character);
                    if (bonus != 0)
                    {
                        string sign = bonus > 0 ? $"+{bonus}" : $"{bonus}";
                        var ans = MessageBox.Show(
                            $"You entered {parsedHp} HP manually.\n\n" +
                            $"Add CON & class bonuses ({sign}) to make it {parsedHp + bonus}?",
                            "Apply HP Bonuses?",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        if (ans == MessageBoxResult.Yes)
                            parsedHp = Math.Max(1, parsedHp + bonus);
                    }
                }
            }

            int parsedCp = hasCpPerLevel ? character.CpPerLevel : 0;
            if (!hasCpPerLevel && (!int.TryParse(cpBox!.Text.Trim(), out parsedCp) || parsedCp < 0))
            {
                MessageBox.Show("Enter a non-negative whole number for CP.", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            selectedXp = parsedXp;
            selectedHp = parsedHp;
            selectedCp = parsedCp;
            if (!hasCpPerLevel && saveCpPerLevelCheck?.IsChecked == true)
                character.CpPerLevel = parsedCp;
            window.DialogResult = true;
        };

        actions.Children.Add(cancel);
        actions.Children.Add(ok);
        panel.Children.Add(actions);
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        if (window.ShowDialog() != true)
            return false;

        xpGain = selectedXp;
        hpGain = selectedHp;
        cpGain = selectedCp;
        return true;
    }

    private void RefreshGrid()
    {
        var rows = new List<CharacterRow>();
        foreach (var c in _app.Characters)
        {
            rows.Add(new CharacterRow
            {
                SourceIndex = rows.Count,
                Name = c.Name,
                PlayerName = c.PlayerName,
                Party = c.Party,
                Race = c.RaceName,
                Class = c.ClassName,
                Level = FormatLevelDisplay(c),
                ExperiencePoints = c.ExperiencePoints,
                HitPoints = c.HitPoints,
                ArmorClass = c.ArmorClass,
                Thac0 = c.Thac0,
                Revision = c.Revision,
                LastModified = c.LastModifiedDisplay,
            });
        }

        CharacterGrid.ItemsSource = rows;
        RosterStatus.Text = rows.Count == 0
            ? "No characters yet. Click NEW CHARACTER or ADD EXISTING to start."
            : $"{rows.Count} character{(rows.Count == 1 ? "" : "s")} loaded.";
    }

    private static bool PromptForExistingCharacterSetup(
        out int startingExperience,
        out int cpPerLevel,
        out int startingUnspentCp,
        out bool experienceIsGrandTotalForMulticlass)
    {
        startingExperience = 0;
        cpPerLevel = 0;
        startingUnspentCp = 0;
        experienceIsGrandTotalForMulticlass = true;

        int selectedExperience = 0;
        int selectedCpPerLevel = 0;
        int selectedUnspentCp = 0;
        bool selectedExperienceIsGrandTotalForMulticlass = true;
        var window = new Window
        {
            Title = "Add Existing Character",
            Width = 420,
            Height = 430,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ShowInTaskbar = false,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Enter setup values for an existing character.",
            Margin = new Thickness(0, 0, 0, 8),
            FontWeight = FontWeights.Bold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Level will be derived from XP once class selection is complete.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        });

        panel.Children.Add(new TextBlock { Text = "Starting Experience:", Margin = new Thickness(0, 0, 0, 2) });
        var xpBox = new TextBox
        {
            Text = "0",
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(xpBox);

        panel.Children.Add(new TextBlock
        {
            Text = "XP entry mode for multiclass:",
            Margin = new Thickness(0, 0, 0, 2),
        });
        var multiclassXpMode = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            SelectedIndex = 0,
        };
        multiclassXpMode.Items.Add(new ComboBoxItem
        {
            Content = "Grand total XP entered (auto-split across selected classes)",
            Tag = true,
        });
        multiclassXpMode.Items.Add(new ComboBoxItem
        {
            Content = "Per-class XP entered (no split)",
            Tag = false,
        });
        panel.Children.Add(multiclassXpMode);

        panel.Children.Add(new TextBlock
        {
            Text = "Single-class characters ignore this setting.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0xA4, 0x68)),
        });

        panel.Children.Add(new TextBlock { Text = "CP / Level:", Margin = new Thickness(0, 0, 0, 2) });
        var cpPerLevelBox = new TextBox
        {
            Text = "0",
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(cpPerLevelBox);

        panel.Children.Add(new TextBlock { Text = "Starting Unspent CP (optional):", Margin = new Thickness(0, 0, 0, 2) });
        var unspentCpBox = new TextBox
        {
            Text = "0",
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(unspentCpBox);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => window.DialogResult = false;

        var start = new Button { Content = "Start", Width = 80, IsDefault = true };
        start.Click += (_, _) =>
        {
            if (!int.TryParse(xpBox.Text.Trim(), out int parsedXp) || parsedXp < 0)
            {
                MessageBox.Show("Enter a non-negative whole number for starting experience.", "Invalid Experience", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(cpPerLevelBox.Text.Trim(), out int parsedCpPerLevel) || parsedCpPerLevel < 0)
            {
                MessageBox.Show("Enter a non-negative whole number for CP / Level.", "Invalid CP / Level", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(unspentCpBox.Text.Trim(), out int parsedUnspentCp) || parsedUnspentCp < 0)
            {
                MessageBox.Show("Enter a non-negative whole number for starting unspent CP.", "Invalid Starting CP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            selectedExperience = parsedXp;
            selectedExperienceIsGrandTotalForMulticlass =
                (multiclassXpMode.SelectedItem as ComboBoxItem)?.Tag as bool? ?? true;
            selectedCpPerLevel = parsedCpPerLevel;
            selectedUnspentCp = parsedUnspentCp;
            window.DialogResult = true;
        };

        actions.Children.Add(cancel);
        actions.Children.Add(start);
        panel.Children.Add(actions);
        window.Content = panel;

        if (window.ShowDialog() != true)
            return false;

        startingExperience = selectedExperience;
        experienceIsGrandTotalForMulticlass = selectedExperienceIsGrandTotalForMulticlass;
        cpPerLevel = selectedCpPerLevel;
        startingUnspentCp = selectedUnspentCp;
        return true;
    }

    private class CharacterRow
    {
        public int SourceIndex { get; set; }
        public string Name { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public string Party { get; set; } = "";
        public string Race { get; set; } = "";
        public string Class { get; set; } = "";
        public string Level { get; set; } = "";
        public int ExperiencePoints { get; set; }
        public int HitPoints { get; set; }
        public int ArmorClass { get; set; }
        public int Thac0 { get; set; }
        public int Revision { get; set; }
        public string LastModified { get; set; } = "";
    }
}
