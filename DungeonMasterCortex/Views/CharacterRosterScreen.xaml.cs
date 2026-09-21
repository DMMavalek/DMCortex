using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
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
    private const string AllPartiesFilterLabel = "All Parties";
    private const string UnassignedPartyFilterLabel = "Unassigned";

    private readonly MainWindow _app;
    private readonly DataGrid CharacterGrid;
    private readonly TextBlock RosterStatus;
    private readonly ComboBox PartyFilterCombo;
    private string _activePartyFilter = string.Empty;
    private bool _isRefreshingPartyControls;

    public UIElement View => this;

    public CharacterRosterScreen(MainWindow app)
    {
        _app = app;
        (CharacterGrid, RosterStatus, PartyFilterCombo) = BuildUi();
    }

    private static List<string> GetDisplayedClassIds(CharacterSheet character)
    {
        if (character.ClassIds is { Count: > 0 })
        {
            return character.ClassIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(character.ClassId))
        {
            return character.ClassId
                .Split(new[] { '/', '+', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        return new List<string>();
    }

    private static (int HitDie, int HitDieLevels, int PostCapGain) GetHitPointProfileForClass(string classId)
    {
        return (classId ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "fighter" or "paladin" or "ranger" => (10, 9, 3),
            "cleric" or "druid" => (8, 9, 2),
            "wizard" or "mage" or "illusionist" => (4, 10, 1),
            "thief" or "bard" or "rogue" => (6, 10, 2),
            _ when RulesEngine.IsPriestClassId(classId) => (8, 9, 2),
            _ => (6, 10, 2),
        };
    }

    private static string FormatLevelDisplay(CharacterSheet character)
    {
        var classIds = GetDisplayedClassIds(character);
        if (classIds.Count <= 1)
            return Math.Max(1, character.Level).ToString();

        return string.Join("/", classIds.Select(classId =>
            CharacterProgressionService.GetLevelForExperience(classId, character.ExperiencePoints)));
    }

    private static string FormatExperienceDisplay(CharacterSheet character)
    {
        string experience = Math.Max(0, character.ExperiencePoints).ToString("N0");
        int classCount = Math.Max(1, GetDisplayedClassIds(character).Count);
        return classCount == 1
            ? experience
            : string.Join("/", Enumerable.Repeat(experience, classCount));
    }

    private static int GetCalculatedOverallLevel(CharacterSheet character, int experiencePoints)
    {
        var classIds = GetDisplayedClassIds(character);
        if (classIds.Count == 0)
            classIds.Add(character.ClassId);
        return classIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => CharacterProgressionService.GetLevelForExperience(id, experiencePoints))
            .DefaultIfEmpty(1)
            .Min();
    }

    private static int GetCalculatedClassLevelsGained(CharacterSheet character, int experiencePoints)
    {
        var classIds = GetDisplayedClassIds(character);
        if (classIds.Count == 0)
            classIds.Add(character.ClassId);

        return classIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Sum(id =>
            {
                int currentLevel = character.ClassLevels.TryGetValue(id, out int savedLevel)
                    ? savedLevel
                    : CharacterProgressionService.GetLevelForExperience(id, character.ExperiencePoints);
                int projectedLevel = CharacterProgressionService.GetLevelForExperience(id, experiencePoints);
                return Math.Max(0, projectedLevel - Math.Max(1, currentLevel));
            });
    }

    private static string FormatRulesetDisplay(CharacterSheet character)
    {
        string mode = (character.CharacterMode ?? string.Empty).Trim();
        if (string.Equals(mode, "players_option", StringComparison.OrdinalIgnoreCase))
            return "PO";

        if (string.IsNullOrWhiteSpace(mode) || string.Equals(mode, "core_rules", StringComparison.OrdinalIgnoreCase))
            return "C";

        return mode;
    }

    private static bool IsPlayersOptionCharacter(CharacterSheet character)
        => string.Equals((character.CharacterMode ?? string.Empty).Trim(), "players_option", StringComparison.OrdinalIgnoreCase);

    public void OnEnter()
    {
        _app.SetBanner("Character Blueprint  ›  Character Roster");
        RefreshGrid();
    }

    private (DataGrid Grid, TextBlock Status, ComboBox FilterCombo) BuildUi()
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
        const double actionButtonWidth = 132;

        var actionsTop = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actionsTop.Children.Add(CreateHeaderButton("+ NEW CHARACTER", actionButtonWidth, BtnNewCharacter_Click));
        actionsTop.Children.Add(CreateHeaderButton("+ ADD EXISTING", actionButtonWidth, BtnAddExistingCharacter_Click));
        actionsTop.Children.Add(CreateHeaderButton("IMPORT CUSTOM", actionButtonWidth, BtnImportCustomPack_Click));
        actionsTop.Children.Add(CreateHeaderButton("REFRESH", actionButtonWidth, BtnRefresh_Click));
        actionsTop.Children.Add(CreateHeaderButton("CHAR SHEETS", actionButtonWidth, BtnCharacterSheets_Click));
        actionsTop.Children.Add(CreateHeaderButton("PARTIES", actionButtonWidth, BtnPartyManagement_Click, isLast: true));
        Grid.SetRow(actionsTop, 0);

        var actionsBottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };
        actionsBottom.Children.Add(CreateHeaderButton("UPDATE", actionButtonWidth, BtnLevelUp_Click));
        actionsBottom.Children.Add(CreateHeaderButton("IN GAME", actionButtonWidth, BtnInGame_Click));
        actionsBottom.Children.Add(CreateHeaderButton("SPELL TRACKER", actionButtonWidth, BtnSpellTracker_Click));
        actionsBottom.Children.Add(CreateHeaderButton("± CP ADJUST", actionButtonWidth, BtnAwardBonusCp_Click));
        actionsBottom.Children.Add(CreateHeaderButton("◀ HUB", actionButtonWidth, BtnHub_Click, isLast: true));
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
        grid.SelectionChanged += CharacterGrid_SelectionChanged;
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
        AddTextColumn(grid, "Edition", "Ruleset", 70);
        AddTextColumn(grid, "Race", "Race", 70);
        AddTextColumn(grid, "Class", "Class", 85);
        AddTextColumn(grid, "Lvl", "Level", 45);
        AddTextColumn(grid, "XP", "Experience", 110);
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
        bottomActions.Children.Add(CreateHeaderButton("🗑  DELETE", 110, BtnDelete_Click, isLast: false, style: "DangerButton"));

        // Party filter inline with bottom actions
        bottomActions.Children.Add(new TextBlock
        {
            Text = "Party Filter:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 6, 0)
        });
        var filterCombo = new ComboBox { MinWidth = 180, VerticalAlignment = VerticalAlignment.Center };
        filterCombo.SelectionChanged += PartyFilterCombo_SelectionChanged;
        bottomActions.Children.Add(filterCombo);

        Grid.SetRow(bottomActions, 2);
        content.Children.Add(bottomActions);

        root.Children.Add(content);
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root
        };
        return (grid, status, filterCombo);
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

    private void BtnInGame_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterGrid.SelectedItem is not CharacterRow selected
            || selected.SourceIndex < 0
            || selected.SourceIndex >= _app.Characters.Count)
        {
            MessageBox.Show("Select a character in the roster first.", "In-Game Character",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _app.OpenInGameCharacter(selected.SourceIndex);
    }

    private void BtnSpellTracker_Click(object sender, RoutedEventArgs e)
    {
        if (CharacterGrid.SelectedItem is not CharacterRow selected
            || selected.SourceIndex < 0
            || selected.SourceIndex >= _app.Characters.Count)
        {
            MessageBox.Show("Select a character in the roster first.", "Spell Tracker",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _app.OpenSpellTracker(selected.SourceIndex);
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
            Filter         = "Codex character (*.json)|*.json|Foundry exchange (*.foundry.json)|*.foundry.json|All files (*.*)|*.*",
            FilterIndex    = 1,
            DefaultExt     = ".json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            IReadOnlyList<SpellDefinition> candidates = FoundryCharacterExchangeService.GetExportSpellCandidates(c, _app.Rules);
            if (candidates.Count > 0)
            {
                IReadOnlySet<string> requiredIds = FoundryCharacterExchangeService.GetRequiredSpellIds(c);
                var spellDialog = new SpellExportSelectionDialog(c, candidates, requiredIds)
                {
                    Owner = Window.GetWindow(this),
                };
                if (spellDialog.ShowDialog() != true)
                    return;

                c.ExportSpellIds = spellDialog.GetSelectedSpellIds();
                c.HasExplicitSpellExportSelection = true;
                c.LastModified = DateTime.Now;
                c.Revision = Math.Max(1, c.Revision + 1);
                _app.SaveCharacters();
            }

            var exportOptions = new JsonSerializerOptions { WriteIndented = true };
            var json = dlg.FilterIndex == 2
                ? FoundryCharacterExchangeService.Export(c, exportOptions, _app.Rules, _app.CharacterOptions.GetCatalog())
                : FoundryCharacterExchangeService.ExportCodex(c, exportOptions, _app.Rules, _app.CharacterOptions.GetCatalog());
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
            Filter     = "Character files (*.json;*.pdf)|*.json;*.pdf|JSON files (*.json)|*.json|Mapped PDF sheets (*.pdf)|*.pdf|All files (*.*)|*.*",
            DefaultExt = ".json",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        int imported = 0;
        var errors   = new List<string>();
        var warnings = new List<string>();
        var opts     = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var pdfImporter = new MappedCharacterSheetPdfImportService();

        foreach (var file in dlg.FileNames)
        {
            try
            {
                if (string.Equals(System.IO.Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    MappedCharacterSheetPdfImportService.ImportResult result = pdfImporter.Import(
                        file,
                        _app.Rules,
                        _app.CharacterOptions.GetCatalog());
                    _app.Characters.Add(result.Character);
                    imported++;
                    warnings.AddRange(result.Warnings.Select(warning => $"{System.IO.Path.GetFileName(file)}: {warning}"));
                    continue;
                }

                var text = File.ReadAllText(file);
                if (FoundryCharacterExchangeService.TryImport(
                    text,
                    opts,
                    out CharacterSheet? exchanged,
                    out string exchangeWarning,
                    _app.Rules,
                    _app.CharacterOptions.GetCatalog()))
                {
                    _app.Characters.Add(exchanged!);
                    imported++;
                    if (!string.IsNullOrWhiteSpace(exchangeWarning))
                        warnings.Add($"{System.IO.Path.GetFileName(file)}: {exchangeWarning}");
                    continue;
                }

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
        if (warnings.Count > 0)
        {
            const int visibleWarningLimit = 12;
            msg += "\n\nImport notes:\n" + string.Join("\n", warnings.Take(visibleWarningLimit));
            if (warnings.Count > visibleWarningLimit)
                msg += $"\n...and {warnings.Count - visibleWarningLimit} more note(s).";
        }
        MessageBox.Show(msg, "Import", MessageBoxButton.OK,
                        errors.Count > 0 || warnings.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
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

    private void CharacterGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // selection tracking used by double-click / level-up; no party panel to update
    }

    private void BtnPartyManagement_Click(object sender, RoutedEventArgs e)
        => _app.GoTo("party_management");

    private void PartyFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingPartyControls)
            return;

        if (PartyFilterCombo.SelectedItem is not string selected)
            return;

        if (string.Equals(selected, AllPartiesFilterLabel, StringComparison.OrdinalIgnoreCase))
            _activePartyFilter = string.Empty;
        else if (string.Equals(selected, UnassignedPartyFilterLabel, StringComparison.OrdinalIgnoreCase))
            _activePartyFilter = UnassignedPartyFilterLabel;
        else
            _activePartyFilter = selected;

        RefreshGrid();
    }

    private void CharacterGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => DoLevelUp();

    private void BtnLevelUp_Click(object sender, RoutedEventArgs e)
        => DoLevelUp();

    private void BtnImportCustomPack_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Custom Classes and Races",
            Filter = "Dungeon Master Cortex custom packs (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var (racesImported, classesImported) = _app.Rules.ImportCustomRacesAndClasses(dialog.FileName);
            MessageBox.Show(
                $"Custom content is ready to use.\n\nClasses imported: {classesImported}\nRaces imported: {racesImported}",
                "Import Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"The custom pack could not be imported.\n\n{ex.Message}",
                "Import Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

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

        if (!PromptForLevelUpGains(character, out int xpGain, out int hpGain, out int cpGain,
                out int levelsDrained, out int drainHpLoss))
            return;

        if (levelsDrained > 0)
        {
            var drain = CharacterProgressionService.ApplyLevelDrain(character, levelsDrained, drainHpLoss);
            character.Notes.Add(
                $"Level drain: level {drain.OldLevel} to {drain.NewLevel}; " +
                $"XP {drain.OldExperience:N0} to {drain.NewExperience:N0}; HP lost {drain.HitPointsLost}.");
            character.LastModified = DateTime.Now;
            character.Revision = Math.Max(1, character.Revision + 1);
            _app.SaveCharacters();
            RefreshGrid();
            MessageBox.Show(
                $"{character.Name} was drained from level {drain.OldLevel} to {drain.NewLevel}.\n\n" +
                $"XP: {drain.OldExperience:N0} -> {drain.NewExperience:N0}\n" +
                $"HP lost: {drain.HitPointsLost}",
                "Level Drain Applied",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int adjustedExperience = (int)Math.Clamp((long)Math.Max(0, character.ExperiencePoints) + xpGain, 0L, int.MaxValue);
        int projectedOverallLevel = GetCalculatedOverallLevel(character, adjustedExperience);
        if (projectedOverallLevel <= Math.Max(1, character.Level))
        {
            var adjustment = CharacterProgressionService.ApplyExperienceAdjustment(character, xpGain);
            if (hpGain != 0)
            {
                character.HitPoints = Math.Max(1, character.HitPoints + hpGain);
                character.CurrentHitPoints = Math.Clamp(character.CurrentHitPoints + hpGain, 0, character.HitPoints);
            }
            if (cpGain > 0)
                character.UnspentCharacterPoints += cpGain;

            character.Notes.Add(
                $"Manual update: XP {adjustment.OldExperience:N0} to {adjustment.NewExperience:N0}; " +
                $"level {adjustment.OldLevel} to {adjustment.NewLevel}; HP adjustment {hpGain:+#;-#;0}.");
            character.LastModified = DateTime.Now;
            character.Revision = Math.Max(1, character.Revision + 1);
            _app.SaveCharacters();
            RefreshGrid();
            _app.StartPlayerLevelUp(selected.SourceIndex, 0, 0, 0);
            _app.GoTo("chargen_character_options");
            return;
        }

        if (_app.License.IsDemoMode && character.Level >= _app.License.DemoMaxLevel)
        {
            MessageBox.Show(
                $"Demo mode allows level-ups only up to level {_app.License.DemoMaxLevel}.",
                "Demo Limit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

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
        if (!IsPlayersOptionCharacter(character))
        {
            MessageBox.Show(
                "CP adjustments are only available for Player's Option characters.",
                "Core Rules",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

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

    private static bool PromptForLevelUpGains(CharacterSheet character, out int xpGain, out int hpGain, out int cpGain,
        out int levelsDrained, out int drainHpLoss)
    {
        xpGain = 0;
        hpGain = 0;
        cpGain = 0;
        levelsDrained = 0;
        drainHpLoss = 0;
        int selectedXp = 0;
        int selectedHp = 0;
        int selectedCp = 0;
        int selectedLevelsDrained = 0;
        int selectedDrainHpLoss = 0;

        bool isPlayersOptionMode = IsPlayersOptionCharacter(character);
        bool hasCpPerLevel = isPlayersOptionMode && character.CpPerLevel > 0;
        var (hitDie, _) = CharacterProgressionService.GetHitDieInfo(character, character.Level + 1);
        int hpBonus = CharacterProgressionService.GetConAndClassHpBonus(character);
        string bonusLabel = hpBonus >= 0 ? $"+{hpBonus}" : $"{hpBonus}";
        int primeXpBonus = CharacterProgressionService.GetPrimeRequisiteBonusPercent(character);
        int abilityXpBonus = character.Bonuses?.XpModifierPercent ?? 0;
        int totalXpBonus = primeXpBonus + abilityXpBonus;
        bool hpWasRolled = false;
        bool willLevelUp = false;
        int projectedLevel = Math.Max(1, character.Level);
        int projectedLevelsGained = 0;
        int projectedClassLevelsGained = 0;
        TextBlock? cpAutoText = null;

        var window = new Window
        {
            Title = $"Update — {character.Name}",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            MaxHeight = Math.Max(500, SystemParameters.WorkArea.Height - 80),
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
        panel.Children.Add(new TextBlock { Text = "Experience Adjustment (+/-):", Margin = new Thickness(0, 6, 0, 2) });
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
            int entered = int.TryParse(xpBox.Text.Trim(), out int parsed) ? parsed : 0;
            if (entered <= 0 || totalXpBonus == 0 || xpAlreadyIncludesBonus.IsChecked == true)
                return entered;
            return CharacterProgressionService.ApplyExperienceBonus(entered, totalXpBonus);
        }

        // ── HP row ────────────────────────────────────────────────────────────
        panel.Children.Add(new TextBlock
        {
            Text = $"HP Adjustment (+/-; roll d{hitDie}, bonuses {bonusLabel} on level gain):",
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

        var hpBreakdownText = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0xA4, 0x68)),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        panel.Children.Add(hpBreakdownText);

        void RefreshLevelState()
        {
            int enteredXp = int.TryParse(xpBox.Text.Trim(), out int parsed) ? parsed : 0;
            int pendingXp = GetEffectiveXpGain();
            int projectedExperience = (int)Math.Clamp((long)Math.Max(0, character.ExperiencePoints) + pendingXp, 0L, int.MaxValue);
            projectedLevel = GetCalculatedOverallLevel(character, projectedExperience);
            willLevelUp = projectedLevel > Math.Max(1, character.Level);
            projectedLevelsGained = Math.Max(0, projectedLevel - Math.Max(1, character.Level));
            projectedClassLevelsGained = GetCalculatedClassLevelsGained(character, projectedExperience);

            if (totalXpBonus != 0)
            {
                if (xpAlreadyIncludesBonus.IsChecked == true)
                    xpBonusInfo.Text = $"XP bonus not applied by system. Effective XP gain: {enteredXp}.";
                else
                    xpBonusInfo.Text = $"XP bonus applied by system ({(totalXpBonus >= 0 ? "+" : string.Empty)}{totalXpBonus}%). Effective XP gain: {pendingXp}.";
            }

            if (willLevelUp)
            {
                string levelText = projectedLevelsGained == 1 ? "1 level" : $"{projectedLevelsGained} levels";
                levelStateText.Text = $"Level increase detected: {character.Level} -> {projectedLevel} ({levelText}). Roll applies one HP gain per level.";
                levelStateText.Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC0, 0x50));
                hpRow.Visibility = Visibility.Visible;
                rollBtn.IsEnabled = true;
                if (cpAutoText is not null)
                    cpAutoText.Text = $"CP auto-award for this update: {character.CpPerLevel} x {projectedClassLevelsGained} class level(s) = {Math.Max(0, character.CpPerLevel) * projectedClassLevelsGained}.";
            }
            else
            {
                levelStateText.Text = projectedLevel < Math.Max(1, character.Level)
                    ? $"Level reduction detected: {character.Level} -> {projectedLevel}. HP changes remain manual."
                    : "No level increase. XP and HP adjustments will be applied directly.";
                levelStateText.Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0xA4, 0x68));
                hpRow.Visibility = Visibility.Visible;
                rollBtn.IsEnabled = false;
                hpWasRolled = false;
                hpBreakdownText.Text = string.Empty;
                hpBreakdownText.Visibility = Visibility.Collapsed;
                if (cpAutoText is not null)
                    cpAutoText.Text = projectedClassLevelsGained > 0
                        ? $"CP auto-award for this update: {character.CpPerLevel} x {projectedClassLevelsGained} class level(s) = {Math.Max(0, character.CpPerLevel) * projectedClassLevelsGained}."
                        : "CP auto-award for this update: 0 (no class level gained).";
            }
        }

        xpBox.TextChanged += (_, _) => RefreshLevelState();
    xpAlreadyIncludesBonus.Checked += (_, _) => RefreshLevelState();
    xpAlreadyIncludesBonus.Unchecked += (_, _) => RefreshLevelState();
        RefreshLevelState();

        rollBtn.Click += (_, _) =>
        {
            int startLevel = Math.Max(1, character.Level);
            int totalRolled = 0;
            int con = character.Abilities.GetValueOrDefault("con", 10);
            var classIds = character.ClassMode == "multiclass" && character.ClassIds.Count > 0
                ? character.ClassIds
                    .Select(id => id ?? string.Empty)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToList()
                : new List<string> { character.ClassId ?? string.Empty };

            var breakdownLines = new List<string>();

            for (int level = startLevel + 1; level <= projectedLevel; level++)
            {
                int levelTotal = 0;
                var classParts = new List<string>();
                foreach (string classId in classIds)
                {
                    string classToken = classId ?? string.Empty;
                    var profile = GetHitPointProfileForClass(classToken);
                    int baseRoll = level <= profile.HitDieLevels
                        ? Random.Shared.Next(1, profile.HitDie + 1)
                        : profile.PostCapGain;
                    int classBonus = CharacterProgressionService.GetConHitPointBonus(con, classToken) + (character.Bonuses?.HpPerLevel ?? 0);
                    int classTotal = Math.Max(1, baseRoll + classBonus);
                    levelTotal += classTotal;

                    string className = classToken;
                    classParts.Add($"{className}: {baseRoll} + {classBonus} = {classTotal}");
                }

                totalRolled += levelTotal;
                breakdownLines.Add($"L{level}: {string.Join(" | ", classParts)} => +{levelTotal} HP");
            }

            hpBox.Text = totalRolled.ToString();
            hpWasRolled = true;
            hpBreakdownText.Text = "HP roll breakdown:\n" + string.Join("\n", breakdownLines);
            hpBreakdownText.Visibility = Visibility.Visible;
        };
        // Reset flag if user edits manually after rolling
        hpBox.TextChanged += (_, _) => { if (!rollBtn.IsKeyboardFocusWithin) hpWasRolled = false; };

        // ── CP row (Player's Option only) ────────────────────────────────────
        TextBox? cpBox = null;
        CheckBox? saveCpPerLevelCheck = null;
        if (isPlayersOptionMode && hasCpPerLevel)
        {
            cpAutoText = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC0, 0x50)),
                TextWrapping = TextWrapping.Wrap,
            };
            panel.Children.Add(cpAutoText);

            panel.Children.Add(new TextBlock
            {
                Text = $"Character Points: {character.CpPerLevel} / level  (from character sheet)",
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC0, 0x50)),
            });
        }
        else if (isPlayersOptionMode)
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

        panel.Children.Add(new Separator { Margin = new Thickness(0, 14, 0, 10) });
        panel.Children.Add(new TextBlock
        {
            Text = "Level Drain",
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xC0, 0x50)),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Use this instead of gains when energy drain or another effect removes levels.",
            Margin = new Thickness(0, 2, 0, 8),
            Style = (Style)Application.Current.FindResource("SubtitleText"),
            TextWrapping = TextWrapping.Wrap,
        });

        var drainGrid = new Grid();
        drainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        drainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        drainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var drainLevelPanel = new StackPanel();
        drainLevelPanel.Children.Add(new TextBlock { Text = "Levels Drained:", Margin = new Thickness(0, 0, 0, 2) });
        var drainLevelsBox = new TextBox { Text = "0", Padding = new Thickness(6, 3, 6, 3) };
        drainLevelPanel.Children.Add(drainLevelsBox);
        Grid.SetColumn(drainLevelPanel, 0);
        drainGrid.Children.Add(drainLevelPanel);

        var drainHpPanel = new StackPanel();
        drainHpPanel.Children.Add(new TextBlock { Text = "HP Lost (0 = recorded):", Margin = new Thickness(0, 0, 0, 2) });
        var drainHpBox = new TextBox { Text = "0", Padding = new Thickness(6, 3, 6, 3) };
        drainHpPanel.Children.Add(drainHpBox);
        Grid.SetColumn(drainHpPanel, 2);
        drainGrid.Children.Add(drainHpPanel);
        panel.Children.Add(drainGrid);

        RefreshLevelState();

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
            if (!int.TryParse(drainLevelsBox.Text.Trim(), out int parsedLevelsDrained) || parsedLevelsDrained < 0)
            {
                MessageBox.Show("Enter a non-negative whole number for levels drained.", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(drainHpBox.Text.Trim(), out int parsedDrainHpLoss) || parsedDrainHpLoss < 0)
            {
                MessageBox.Show("Enter a non-negative whole number for HP lost.", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (parsedLevelsDrained > 0)
            {
                int actualLevelsDrained = Math.Min(parsedLevelsDrained, Math.Max(0, character.Level - 1));
                if (actualLevelsDrained <= 0)
                {
                    MessageBox.Show("A level 1 character cannot lose another level.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int targetLevel = character.Level - actualLevelsDrained;
                bool hasRecordedHp = character.HitPointGainByLevel.Any(entry =>
                    entry.Key > targetLevel && entry.Key <= character.Level && entry.Value > 0);
                if (parsedDrainHpLoss <= 0 && !hasRecordedHp)
                {
                    MessageBox.Show(
                        "No HP history exists for the drained level. Enter the HP lost before applying the drain.",
                        "Update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var confirmation = MessageBox.Show(
                    $"Apply {actualLevelsDrained} level drain to {character.Name}?\n\n" +
                    $"Level: {character.Level} -> {targetLevel}\n" +
                    "XP will move to the midpoint of the new level's progression band. " +
                    (parsedDrainHpLoss > 0 ? $"HP lost: {parsedDrainHpLoss}." : "Recorded HP from the lost level(s) will be removed."),
                    "Confirm Level Drain",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirmation != MessageBoxResult.Yes)
                    return;

                selectedLevelsDrained = actualLevelsDrained;
                selectedDrainHpLoss = parsedDrainHpLoss;
                window.DialogResult = true;
                return;
            }

            if (!int.TryParse(xpBox.Text.Trim(), out int enteredXp))
            {
                MessageBox.Show("Enter a whole number for XP adjustment.", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int parsedXp = GetEffectiveXpGain();
            int projectedExperience = (int)Math.Clamp((long)Math.Max(0, character.ExperiencePoints) + parsedXp, 0L, int.MaxValue);
            int projectedLevel = GetCalculatedOverallLevel(character, projectedExperience);
            int projectedClassLevelsGainedNow = GetCalculatedClassLevelsGained(character, projectedExperience);
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

            if (!int.TryParse(hpBox.Text.Trim(), out int parsedHp))
            {
                MessageBox.Show("Enter a whole number for HP adjustment.", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (willLevelUpNow)
            {
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

            int parsedCp = 0;
            if (isPlayersOptionMode && hasCpPerLevel)
                parsedCp = character.CpPerLevel;
            else if (isPlayersOptionMode && (!int.TryParse(cpBox!.Text.Trim(), out parsedCp) || parsedCp < 0))
            {
                MessageBox.Show("Enter a non-negative whole number for CP.", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            selectedXp = parsedXp;
            selectedHp = parsedHp;
            if (isPlayersOptionMode && hasCpPerLevel)
                selectedCp = Math.Max(0, character.CpPerLevel) * projectedClassLevelsGainedNow;
            else if (isPlayersOptionMode)
                selectedCp = parsedCp;
            if (!hasCpPerLevel && isPlayersOptionMode && saveCpPerLevelCheck?.IsChecked == true)
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
        levelsDrained = selectedLevelsDrained;
        drainHpLoss = selectedDrainHpLoss;
        return true;
    }

    private void RefreshGrid()
    {
        var rows = new List<CharacterRow>();
        int sourceIndex = 0;
        foreach (var c in _app.Characters)
        {
            if (!MatchesActivePartyFilter(c.Party))
            {
                sourceIndex++;
                continue;
            }

            rows.Add(new CharacterRow
            {
                SourceIndex = sourceIndex,
                Name = c.Name,
                PlayerName = c.PlayerName,
                Party = c.Party,
                Ruleset = FormatRulesetDisplay(c),
                Race = c.RaceName,
                Class = c.ClassName,
                Level = FormatLevelDisplay(c),
                Experience = FormatExperienceDisplay(c),
                HitPoints = c.HitPoints,
                ArmorClass = c.ArmorClass,
                Thac0 = c.Thac0,
                Revision = c.Revision,
                LastModified = c.LastModifiedDisplay,
            });

            sourceIndex++;
        }

        CharacterGrid.ItemsSource = rows;
        RosterStatus.Text = rows.Count == 0
            ? "No characters match the current filter."
            : $"{rows.Count} character{(rows.Count == 1 ? "" : "s")} shown.";

        RefreshPartyControls();
    }

    private bool MatchesActivePartyFilter(string? partyName)
    {
        string normalizedParty = (partyName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(_activePartyFilter))
            return true;

        if (string.Equals(_activePartyFilter, UnassignedPartyFilterLabel, StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(normalizedParty);

        return string.Equals(normalizedParty, _activePartyFilter, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshPartyControls()
    {
        _isRefreshingPartyControls = true;

        var knownParties = _app.Characters
            .Select(x => (x.Party ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string currentFilterLabel = string.IsNullOrWhiteSpace(_activePartyFilter) ? AllPartiesFilterLabel : _activePartyFilter;
        var filterItems = new List<string> { AllPartiesFilterLabel, UnassignedPartyFilterLabel };
        filterItems.AddRange(knownParties);

        PartyFilterCombo.ItemsSource = filterItems;
        if (filterItems.Contains(currentFilterLabel, StringComparer.OrdinalIgnoreCase))
            PartyFilterCombo.SelectedItem = filterItems.First(x => string.Equals(x, currentFilterLabel, StringComparison.OrdinalIgnoreCase));
        else
            PartyFilterCombo.SelectedItem = AllPartiesFilterLabel;

        _isRefreshingPartyControls = false;
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
        public string Ruleset { get; set; } = "";
        public string Race { get; set; } = "";
        public string Class { get; set; } = "";
        public string Level { get; set; } = "";
        public string Experience { get; set; } = string.Empty;
        public int HitPoints { get; set; }
        public int ArmorClass { get; set; }
        public int Thac0 { get; set; }
        public int Revision { get; set; }
        public string LastModified { get; set; } = "";
    }
}
