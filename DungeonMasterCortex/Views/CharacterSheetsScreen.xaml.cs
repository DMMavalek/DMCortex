using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CharacterSheetsScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    private readonly Random _rng = new();
    private CharacterSheet? _selectedCharacter;
    private string _selectedCharacterSheetText = string.Empty;

    public UIElement View => this;

    public CharacterSheetsScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.SetBanner("Character Sheet");
        try
        {
            BindSelectedCharacter();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open character sheet.\n\n{ex.Message}",
                "Character Sheet",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _app.GoTo("characters");
        }
    }

    private void BindSelectedCharacter()
    {
        _selectedCharacter = null;
        int index = _app.PendingCharacterSheetIndex;
        _app.PendingCharacterSheetIndex = -1;

        if (index >= 0 && index < _app.Characters.Count)
            _selectedCharacter = _app.Characters[index];

        SheetPanel.Children.Clear();

        if (_selectedCharacter is null)
        {
            SheetPanel.Children.Add(new TextBlock
            {
                Text = "Select a character from the Character Blueprint roster, then open Character Sheets to generate a player-ready sheet.",
                Style = (Style)FindResource("BodyText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 12, 4, 12),
            });
            _selectedCharacterSheetText = string.Empty;
            BtnPrintSummary.IsEnabled = false;
            BtnCopySummary.IsEnabled = false;
            return;
        }

        EnsureCharacterCollectionsInitialized(_selectedCharacter);
        EnsureSpellbooksInitialized(_selectedCharacter);
    NormalizeWizardSpellbookIds(_selectedCharacter);
    NormalizeTrackedSpellIds(_selectedCharacter);
        _selectedCharacterSheetText = BuildCharacterSheetText(_selectedCharacter);
        PopulateSheetSections(_selectedCharacter);
        BtnPrintSummary.IsEnabled = true;
        BtnCopySummary.IsEnabled = true;
    StatusText.Text = BuildStatusText($"Sheet generated for {_selectedCharacter.Name}.");
    }

    private static string BuildCharacterSummary(CharacterSheet c)
    {
        string race = string.IsNullOrWhiteSpace(c.RaceName) ? c.RaceId : c.RaceName;
        string cls = string.IsNullOrWhiteSpace(c.ClassName) ? c.ClassId : c.ClassName;
        string levelText = FormatLevelDisplay(c);
        string party = string.IsNullOrWhiteSpace(c.Party) ? "-" : c.Party;
        string player = string.IsNullOrWhiteSpace(c.PlayerName) ? "-" : c.PlayerName;

        return $"Name: {c.Name}\n"
             + $"Player: {player}\n"
             + $"Party: {party}\n"
             + $"Race: {race}\n"
             + $"Class: {cls}\n"
             + $"Level: {levelText}    XP: {c.ExperiencePoints:n0}\n"
             + $"HP: {c.HitPoints}    AC: {c.ArmorClass}    THAC0: {c.Thac0}\n"
             + $"Coins: {c.GoldPieces}gp {c.SilverPieces}sp {c.CopperPieces}cp\n"
             + $"Equipment lines: {(c.EquipmentSelections?.Count ?? 0)}";
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
        return string.Join("/", Enumerable.Repeat(level.ToString(), classCount));
    }

    private List<string> ResolveTraitNames(CharacterSheet c)
    {
        if (c.Traits.Count > 0)
        {
            return c.Traits
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (c.SelectedTraitIds.Count == 0)
            return new List<string>();

        var traitById = _app.CharacterOptions.GetCatalog().Traits
            .ToDictionary(x => x.Id, x => x.Name, StringComparer.OrdinalIgnoreCase);

        return c.SelectedTraitIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => traitById.TryGetValue(id, out var name) ? name : id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> ResolveDisadvantageNames(CharacterSheet c)
    {
        if (c.Disadvantages.Count > 0)
        {
            return c.Disadvantages
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (c.SelectedDisadvantageSeverities.Count == 0)
            return new List<string>();

        var disadvantagesById = _app.CharacterOptions.GetCatalog().Disadvantages
            .ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase);

        return c.SelectedDisadvantageSeverities
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv =>
            {
                if (!disadvantagesById.TryGetValue(kv.Key, out var disadvantage))
                    return kv.Key;

                bool isSevere = string.Equals(kv.Value, "severe", StringComparison.OrdinalIgnoreCase)
                    && disadvantage.SevereBonus.HasValue;
                return $"{disadvantage.Name} [{(isSevere ? "Severe" : "Moderate")}]";
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string BuildCharacterSheetText(CharacterSheet c)
    {
        var traitNames = ResolveTraitNames(c);
        var disadvantageNames = ResolveDisadvantageNames(c);
        var saves = BuildSavingThrowRows(c);

        var lines = new List<string>
        {
            "=== CHARACTER SHEET ===",
            BuildCharacterSummary(c),
            string.Empty,
            "=== CORE COMBAT ===",
            $"Attack Rate: {c.AttackRate}",
            $"Base HP: {c.BaseHitPoints}    Base AC: {c.BaseArmorClass}",
            $"Armor Profile: {c.ArmorProfile}    Rogue Armor Profile: {c.RogueSkillArmorProfile}",
            $"Movement: {c.Movement} (base {c.BaseMovement})",
            $"THAC0: {c.Thac0}",
            $"Unspent Proficiency Choices: {c.UnspentProficiencyChoices}    Unspent Rogue Skill Points: {c.UnspentRogueSkillPoints}",
            string.Empty,
            "=== ABILITIES ===",
        };

        if (string.Equals(c.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
        {
            lines.Insert(8, $"Unspent CP: {c.UnspentCharacterPoints}    Spent NWP CP: {c.SpentNwpCharacterPoints}    Spent Weapon CP: {c.SpentWeaponCharacterPoints}");
        }

        if (c.Abilities.Count > 0)
        {
            foreach (var kv in c.Abilities.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                lines.Add($"{kv.Key.ToUpperInvariant()}: {kv.Value}");
        }
        else
        {
            lines.Add("(none)");
        }

        lines.Add(string.Empty);
        lines.Add("=== SAVING THROWS ===");
        if (saves.Count > 0)
        {
            foreach (var save in saves)
                lines.Add($"{save.Label}: {save.FinalTarget} (base {save.BaseTarget}{(save.Bonus != 0 ? $", bonus {FormatSigned(save.Bonus)}" : string.Empty)})");
        }
        else
        {
            lines.Add("(none)");
        }

        lines.Add(string.Empty);
        lines.Add("=== WEAPON PROFICIENCIES ===");
        if (c.WeaponProficiencies.Count > 0)
        {
            foreach (var wp in c.WeaponProficiencies.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                string spec = wp.Specialized ? " [Specialized]" : string.Empty;
                lines.Add($"{wp.DisplayName} ({wp.ProficiencyType}){spec}");
            }
        }
        else
        {
            lines.Add("(none)");
        }

        lines.Add(string.Empty);
        lines.Add("=== NONWEAPON PROFICIENCIES ===");
        if (c.NonweaponProficiencies.Count > 0)
        {
            foreach (string nwp in c.NonweaponProficiencies.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                lines.Add(nwp);
        }
        else
        {
            lines.Add("(none)");
        }

        lines.Add(string.Empty);
        lines.Add("=== LANGUAGES ===");
        if (c.Languages.Count > 0)
        {
            foreach (string lang in c.Languages.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                lines.Add(lang);
        }
        else
        {
            lines.Add("(none)");
        }

        lines.Add(string.Empty);
        lines.Add("=== EQUIPMENT ===");
        if (c.EquipmentSelections.Count > 0)
        {
            foreach (var eq in c.EquipmentSelections.OrderBy(x => x.Category).ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase))
            {
                string qty = $"x{Math.Max(1, eq.Quantity)}";
                string weapon = eq.IsWeapon
                    ? $" | Wpn SF {eq.WeaponSpeed}, Dmg {eq.WeaponDamageSmallMedium}/{eq.WeaponDamageLarge}, Type {eq.WeaponType}, Size {eq.WeaponSize}"
                    : string.Empty;
                string armor = eq.IsArmor
                    ? $" | Armor AC {eq.ArmorClassValue} ({eq.RogueArmorProfile})"
                    : string.Empty;
                lines.Add($"{eq.ItemName} {qty} [{eq.Category}]{weapon}{armor}");
            }
        }
        else if (c.Equipment.Count > 0)
        {
            foreach (string eq in c.Equipment)
                lines.Add(eq);
        }
        else
        {
            lines.Add("(none)");
        }

        lines.Add(string.Empty);
        lines.Add("=== TRAITS / DISADVANTAGES ===");
        lines.Add(traitNames.Count > 0 ? "Traits: " + string.Join(", ", traitNames) : "Traits: (none)");
        lines.Add(disadvantageNames.Count > 0 ? "Disadvantages: " + string.Join(", ", disadvantageNames) : "Disadvantages: (none)");

        lines.Add(string.Empty);
        lines.Add("=== NOTES ===");
        if (c.Notes.Count > 0)
        {
            foreach (string note in c.Notes)
                lines.Add($"- {note}");
        }
        else
        {
            lines.Add("(none)");
        }

        AppendSpellsAtEndText(lines, c);

        return string.Join(Environment.NewLine, lines);
    }

    private void AppendSpellsAtEndText(List<string> lines, CharacterSheet c)
    {
        lines.Add(string.Empty);
        lines.Add("=== SPELLS ===");

        var priestSlots = BuildPriestSlotTotalsByLevel(c);
        if (priestSlots.Count > 0)
        {
            lines.Add("Priest slots at current level:");
            foreach (var kv in priestSlots.OrderBy(kv => kv.Key))
            {
                var slot = kv.Value;
                lines.Add($"  L{kv.Key}: {slot.Total} total ({slot.Base} base + {slot.WisdomBonus} Wis bonus)");
            }

            var priestSpellsByLevel = GetAccessiblePriestSpellDefinitions(c)
                .GroupBy(s => TryParseSpellLevel(s.Level, out int level) ? level : int.MaxValue)
                .Where(g => g.Key != int.MaxValue)
                .OrderBy(g => g.Key)
                .ToList();

            if (priestSpellsByLevel.Count > 0)
            {
                foreach (var levelGroup in priestSpellsByLevel)
                {
                    lines.Add($"  Level {levelGroup.Key} priest spells:");
                    foreach (var spell in levelGroup.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                        lines.Add("    - " + BuildSpellAtGlanceTextLine(spell, c.Level));
                }
            }
        }

        if (IsWizardCaster(c))
        {
            int maxArcaneLevel = GetHighestSpellLevel(c.ArcaneSpellSlots);
            var spellbookByLevel = GetWizardSpellbookDefinitions(c)
                .Where(s => TryParseSpellLevel(s.Level, out int level) && level <= maxArcaneLevel)
                .GroupBy(s => TryParseSpellLevel(s.Level, out int level) ? level : int.MaxValue)
                .Where(g => g.Key != int.MaxValue)
                .OrderBy(g => g.Key)
                .ToList();

            lines.Add("Wizard spellbook:");
            if (spellbookByLevel.Count == 0)
            {
                lines.Add("  (none)");
            }
            else
            {
                foreach (var levelGroup in spellbookByLevel)
                {
                    lines.Add($"  Level {levelGroup.Key} wizard spells:");
                    foreach (var spell in levelGroup.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                        lines.Add("    - " + BuildSpellAtGlanceTextLine(spell, c.Level));
                }
            }
        }

    }

    private static string BuildSpellAtGlanceTextLine(SpellDefinition spell, int casterLevel)
    {
        string effect = BuildSpellEffectAtGlance(spell, casterLevel);
        string cast = string.IsNullOrWhiteSpace(spell.CastTime) ? "-" : spell.CastTime.Trim();
        string duration = string.IsNullOrWhiteSpace(spell.Duration) ? "-" : spell.Duration.Trim();
        string range = string.IsNullOrWhiteSpace(spell.Range) ? "-" : spell.Range.Trim();
        string save = string.IsNullOrWhiteSpace(spell.Save) ? "-" : spell.Save.Trim();
        string brief = string.IsNullOrWhiteSpace(spell.BriefDescription) ? "(no brief description)" : spell.BriefDescription.Trim();
        return $"{spell.Name} | Dmg/Heal: {effect} | Cast: {cast} | Dur: {duration} | Range: {range} | Save: {save} | {brief}";
    }

    private static string BuildSpellEffectAtGlance(SpellDefinition spell, int casterLevel)
    {
        string effect = SpellDamageService.BuildDamageDisplay(spell, Math.Max(1, casterLevel));
        return string.IsNullOrWhiteSpace(effect) ? "-" : effect;
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        _app.GoTo("characters", -1);
    }

    private void BtnPrintSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
            return;

        try
        {
            var doc = BuildPrintableDocument(_selectedCharacter, _selectedCharacterSheetText);
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true)
                return;

            doc.PageHeight = dlg.PrintableAreaHeight;
            doc.PageWidth = dlg.PrintableAreaWidth;
            doc.ColumnWidth = dlg.PrintableAreaWidth;
            dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"Character Sheet - {_selectedCharacter.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not print character sheet.\n\n{ex.Message}", "Character Sheets", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnCopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedCharacterSheetText))
            return;

        try
        {
            Clipboard.SetText(_selectedCharacterSheetText);
            StatusText.Text = "Character sheet copied to clipboard.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not copy character sheet.\n\n{ex.Message}", "Character Sheets", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static FlowDocument BuildPrintableDocument(CharacterSheet c, string fullSheetText)
    {
        var doc = new FlowDocument
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 11,
            PagePadding = new Thickness(32),
        };

        doc.Blocks.Add(new Paragraph(new Run($"Character Sheet - {c.Name}"))
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        foreach (string line in fullSheetText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            doc.Blocks.Add(new Paragraph(new Run(line))
            {
                Margin = new Thickness(0, 0, 0, 2)
            });
        }

        return doc;
    }

    // ── Section building ──────────────────────────────────────────────────────

    private void PopulateSheetSections(CharacterSheet c)
    {
        SheetPanel.Children.Clear();

        string race     = string.IsNullOrWhiteSpace(c.RaceName)  ? c.RaceId  : c.RaceName;
        string cls      = string.IsNullOrWhiteSpace(c.ClassName) ? c.ClassId : c.ClassName;
        string ruleset  = c.CharacterMode == "players_option" ? "Player's Option" : "Core Rules";

        // 1. CHARACTER IDENTITY
        SheetPanel.Children.Add(BuildSection("CHARACTER IDENTITY", true, p =>
        {
            AddKV(p, "Name",       c.Name);
            AddKV(p, "Player",     string.IsNullOrWhiteSpace(c.PlayerName) ? "—" : c.PlayerName);
            AddKV(p, "Party",      string.IsNullOrWhiteSpace(c.Party)      ? "—" : c.Party);
            AddKV(p, "Race",       race);
            AddKV(p, "Class",      cls);
            AddKV(p, "Level",      FormatLevelDisplay(c));
            AddKV(p, "Experience", $"{c.ExperiencePoints:n0} XP");
            AddKV(p, "Ruleset",    ruleset);
        }));

        // 2. ABILITY SCORES
        SheetPanel.Children.Add(BuildSection("ABILITY SCORES", true, p =>
        {
            bool first = true;
            foreach (string abilityKey in new[] { "str", "dex", "con", "int", "wis", "cha" })
            {
                if (!c.Abilities.TryGetValue(abilityKey, out int score)) continue;
                if (!first) p.Children.Add(new Border { Height = 6 });
                first = false;
                AddAbilityBlock(p, c, abilityKey, score);
            }
        }));

        // 3. COMBAT STATISTICS
        SheetPanel.Children.Add(BuildSection("COMBAT STATISTICS", true, p =>
        {
            AddKV(p, "Hit Points",    $"{c.HitPoints}  (base {c.BaseHitPoints})");
            AddKV(p, "Armor Class",   $"{c.ArmorClass}  (base {c.BaseArmorClass})");
            AddKV(p, "Armor Profile", c.ArmorProfile.Replace("_", " "));
            AddKV(p, "THAC0",         c.Thac0.ToString());
            AddKV(p, "Attack Rate",   c.AttackRate);
            AddKV(p, "Movement",      $"{c.Movement}  (base {c.BaseMovement})");
            if (!string.IsNullOrWhiteSpace(c.RogueSkillArmorProfile) && c.RogueSkillArmorProfile != "no_armor")
                AddKV(p, "Rogue Armor", c.RogueSkillArmorProfile.Replace("_", " "));
        }));

        // 4. WEAPONS — carried weapons as a table grid
        var carriedWeapons = c.EquipmentSelections.Where(e => e.IsWeapon).ToList();
        SheetPanel.Children.Add(BuildSection("WEAPONS", carriedWeapons.Count > 0, p =>
        {
            if (carriedWeapons.Count == 0) { AddItem(p, "(no weapons carried)"); return; }

            // Columns: Role | Name | THAC0 | SF | APR | Dmg S/M | Dmg L | Type | Sz | Prof
            var tbl = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            tbl.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            const int ColCount = 10;

            // Header row
            tbl.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] headers = { "ROLE", "WEAPON", "THAC0", "SF", "APR", "DMG S/M", "DMG L", "TYPE", "SZ", "PROF" };
            for (int ci = 0; ci < headers.Length; ci++)
            {
                var hdr = new TextBlock
                {
                    Text       = headers[ci],
                    FontFamily = (FontFamily)FindResource("FontBody"),
                    FontSize   = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("BrushDim"),
                    Padding    = new Thickness(ci == 0 ? 0 : 4, 0, 4, 4),
                };
                Grid.SetRow(hdr, 0);
                Grid.SetColumn(hdr, ci);
                tbl.Children.Add(hdr);
            }

            // Separator under header
            tbl.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var hdrSep = new Border
            {
                Height     = 1,
                Background = (Brush)FindResource("BrushBorder2"),
                Margin     = new Thickness(0, 0, 0, 4),
            };
            Grid.SetRow(hdrSep, 1);
            Grid.SetColumnSpan(hdrSep, ColCount);
            tbl.Children.Add(hdrSep);

            // Weapon rows
            for (int wi = 0; wi < carriedWeapons.Count; wi++)
            {
                string role = wi == 0 ? "PRIMARY" : wi == 1 ? "OFF-HAND" : $"WEAPON {wi + 1}";
                tbl.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddWeaponTableRow(tbl, wi + 2, c, carriedWeapons[wi], role, string.Empty, ColCount);
            }

            p.Children.Add(tbl);
        }));

        // 5. SAVING THROWS (class table target adjusted by bonuses)
        var saves = BuildSavingThrowRows(c);
        if (saves.Count > 0)
        {
            SheetPanel.Children.Add(BuildSection("SAVING THROWS", true, p =>
            {
                foreach (var save in saves)
                {
                    string value = $"Target: {save.FinalTarget}   (base {save.BaseTarget}{(save.Bonus != 0 ? $", bonus {FormatSigned(save.Bonus)}" : string.Empty)})";
                    AddKV(p, save.Label, value);
                }
            }));
        }

        // 6. WEAPON PROFICIENCIES
        SheetPanel.Children.Add(BuildSection("WEAPON PROFICIENCIES", true, p =>
        {
            if (c.WeaponProficiencies.Count == 0) { AddItem(p, "(none)"); return; }
            foreach (var wp in c.WeaponProficiencies.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                string spec = wp.Specialized ? "  [Specialized]" : string.Empty;
                string type = wp.ProficiencyType == "individual"
                    ? string.Empty
                    : $"  ({wp.ProficiencyType.Replace("_", " ")})";
                AddItem(p, $"{wp.DisplayName}{type}{spec}");
            }
        }));

        // 7. NONWEAPON PROFICIENCIES
        SheetPanel.Children.Add(BuildSection("NONWEAPON PROFICIENCIES", true, p =>
        {
            if (c.NonweaponProficiencies.Count == 0) { AddItem(p, "(none)"); return; }
            foreach (string nwp in c.NonweaponProficiencies.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                AddItem(p, nwp);
        }));

        // 8. LANGUAGES
        SheetPanel.Children.Add(BuildSection("LANGUAGES", false, p =>
        {
            if (c.Languages.Count == 0) { AddItem(p, "(none)"); return; }
            foreach (string lang in c.Languages.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                AddItem(p, lang);
        }));

        // 9. EQUIPMENT
        SheetPanel.Children.Add(BuildSection("EQUIPMENT", true, p =>
        {
            if (c.EquipmentSelections.Count == 0 && c.Equipment.Count == 0)
            {
                AddItem(p, "(none)"); return;
            }
            if (c.EquipmentSelections.Count > 0)
            {
                string? lastCat = null;
                foreach (var eq in c.EquipmentSelections
                    .OrderBy(x => x.Category)
                    .ThenBy(x => x.ItemName, StringComparer.OrdinalIgnoreCase))
                {
                    if (eq.Category != lastCat)
                    {
                        if (lastCat != null) p.Children.Add(new Border { Height = 4 });
                        AddDivider(p, eq.Category);
                        lastCat = eq.Category;
                    }
                    string qty   = eq.Quantity > 1 ? $"  ×{eq.Quantity}" : string.Empty;
                    string stats = eq.IsWeapon
                        ? $"  [SF {eq.WeaponSpeed}  Dmg {eq.WeaponDamageSmallMedium}/{eq.WeaponDamageLarge}  {eq.WeaponType}  {eq.WeaponSize}]"
                        : eq.IsArmor ? $"  [AC {eq.ArmorClassValue}]" : string.Empty;
                    double wt    = eq.WeightEach * Math.Max(1, eq.Quantity);
                    string wtStr = wt > 0 ? $"  {wt:0.#} lb" : string.Empty;
                    AddItem(p, $"{eq.ItemName}{qty}{wtStr}{stats}");
                }
                double total = c.EquipmentSelections.Sum(e => e.WeightEach * Math.Max(1, e.Quantity));
                if (total > 0)
                {
                    p.Children.Add(new Border { Height = 4 });
                    AddKV(p, "Total Weight", $"{total:0.#} lb");
                }
            }
            else
            {
                foreach (string eq in c.Equipment)
                    AddItem(p, eq);
            }
        }));

        // 10. WEALTH
        SheetPanel.Children.Add(BuildSection("WEALTH", false, p =>
        {
            AddKV(p, "Gold Pieces",   c.GoldPieces.ToString());
            AddKV(p, "Silver Pieces", c.SilverPieces.ToString());
            AddKV(p, "Copper Pieces", c.CopperPieces.ToString());
        }));

        // 11. TRAITS & DISADVANTAGES
        var traitNames = ResolveTraitNames(c);
        var disadvantageNames = ResolveDisadvantageNames(c);
        bool hasTraitData = traitNames.Count > 0 || disadvantageNames.Count > 0;
        SheetPanel.Children.Add(BuildSection("TRAITS & DISADVANTAGES", hasTraitData, p =>
        {
            if (!hasTraitData) { AddItem(p, "(none)"); return; }
            if (traitNames.Count > 0)
            {
                AddDivider(p, "Traits");
                foreach (string t in traitNames)
                    AddItem(p, t);
            }
            if (disadvantageNames.Count > 0)
            {
                if (traitNames.Count > 0) p.Children.Add(new Border { Height = 4 });
                AddDivider(p, "Disadvantages");
                foreach (string d in disadvantageNames)
                    AddItem(p, d);
            }
        }));

        // 12. RACIAL & CLASS ABILITIES
        var unlockedStructuredAbilities = c.StructuredAbilities
            .Where(ab => RulesEngine.AbilityUnlockLevel(ab) <= Math.Max(1, c.Level))
            .ToList();
        bool hasAbilities = unlockedStructuredAbilities.Count > 0 || c.RacialAbilities.Count > 0;
        if (hasAbilities)
        {
            SheetPanel.Children.Add(BuildSection("ABILITIES & SPECIAL POWERS", false, p =>
            {
                if (unlockedStructuredAbilities.Count > 0)
                {
                    foreach (var ab in unlockedStructuredAbilities)
                    {
                        string name = ab.Id.Replace("_", " ");
                        string desc = string.IsNullOrWhiteSpace(ab.Description) ? string.Empty : $": {ab.Description}";
                        AddItem(p, $"{name}{desc}");
                    }
                }
                else
                {
                    foreach (string ab in c.RacialAbilities)
                        AddItem(p, ab);
                }
            }));
        }

        // 13. NOTES
        SheetPanel.Children.Add(BuildSection("NOTES", c.Notes.Count > 0, p =>
        {
            if (c.Notes.Count == 0) { AddItem(p, "(none)"); return; }
            foreach (string note in c.Notes)
                AddItem(p, $"• {note}");
        }));

        // 14. SPELLS (at end)
        SheetPanel.Children.Add(BuildSection("SPELLS", true, p =>
        {
            BuildSpellsAtEndColumns(p, c);
        }));
    }

    // ── Weapon card ───────────────────────────────────────────────────────────

    // ── Weapon card ───────────────────────────────────────────────────────────

    private void AddWeaponTableRow(Grid tbl, int rowIndex, CharacterSheet c, EquipmentSelection w, string role, string enemyContext, int colCount)
    {
        // Proficiency status — resolve via weapon catalog for group coverage
        bool proficient  = false;
        bool specialized = false;
        string profNote  = "NP";
        WeaponProficiencySelection? matchedSelection = null;

        // Find the weapon definition by matching ItemName to catalog.
        // Equipment names may differ in word order (e.g. "Short sword" vs "Sword, Short"),
        // so also compare by word-set (sorted lowercased tokens).
        static HashSet<string> WordSet(string s) =>
            new(s.ToLowerInvariant().Split(new[] { ' ', ',', '-', '\'' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        var itemWords = WordSet(w.ItemName);
        var weaponDef = _app.Rules.Weapons.FirstOrDefault(wd =>
            wd.Name.Equals(w.ItemName, StringComparison.OrdinalIgnoreCase)
            || WordSet(wd.Name).SetEquals(itemWords));

        foreach (var wp in c.WeaponProficiencies)
        {
            if (string.Equals(wp.ProficiencyType, "combat_option", StringComparison.OrdinalIgnoreCase))
                continue;

            bool match = false;
            if (wp.ProficiencyType == "individual")
            {
                // Match by display name (covers direct name pick)
                match = wp.DisplayName.Equals(w.ItemName, StringComparison.OrdinalIgnoreCase)
                     || (weaponDef != null && wp.ProficiencyId.Equals(weaponDef.Id, StringComparison.OrdinalIgnoreCase));
            }
            else if (wp.ProficiencyType == "tight_group" && weaponDef != null)
            {
                // Check if this weapon belongs to the proficiency's tight group
                match = wp.ProficiencyId.Equals(weaponDef.TightGroupId, StringComparison.OrdinalIgnoreCase);
                if (!match && _app.Rules.TightGroups.TryGetValue(wp.ProficiencyId, out var tg))
                    match = tg.WeaponIds.Contains(weaponDef.Id, StringComparer.OrdinalIgnoreCase);
            }
            else if (wp.ProficiencyType == "broad_group" && weaponDef != null)
            {
                // Check if this weapon's group matches the broad group
                match = wp.ProficiencyId.Equals(weaponDef.GroupId, StringComparison.OrdinalIgnoreCase);
                if (!match)
                {
                    var bg = _app.Rules.WeaponGroups.FirstOrDefault(g =>
                        g.Id.Equals(wp.ProficiencyId, StringComparison.OrdinalIgnoreCase));
                    if (bg != null)
                        match = bg.TightGroups.Any(tg => tg.WeaponIds.Contains(weaponDef.Id, StringComparer.OrdinalIgnoreCase));
                }
            }
            // Fallback: loose name substring match (handles hand-typed entries)
            if (!match)
                match = wp.DisplayName.Equals(w.ItemName, StringComparison.OrdinalIgnoreCase)
                     || w.ItemName.Contains(wp.DisplayName, StringComparison.OrdinalIgnoreCase)
                     || wp.DisplayName.Contains(w.ItemName, StringComparison.OrdinalIgnoreCase);
            if (match)
            {
                proficient  = true;
                specialized = wp.Specialized;
                profNote    = specialized ? "SPEC" : "PROF";
                matchedSelection = wp;
                break;
            }
        }

        var combatOptions = c.WeaponProficiencies
            .Where(x => string.Equals(x.ProficiencyType, "combat_option", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.ProficiencyId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int trainingAtkBonus = 0;
        int trainingDmgBonus = 0;

        if (matchedSelection?.WeaponOfChoice == true) trainingAtkBonus += 1;
        if (matchedSelection?.WeaponExpertise == true) { trainingAtkBonus += 1; trainingDmgBonus += 1; }
        if (combatOptions.Contains("weapon_mastery")) { trainingAtkBonus += 3; trainingDmgBonus += 3; }

        if (combatOptions.Contains("fighting_style_single") && role.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase))
            trainingAtkBonus += 1;
        if (combatOptions.Contains("fighting_style_two_weapon") && role.Equals("OFF-HAND", StringComparison.OrdinalIgnoreCase))
            trainingAtkBonus += 1;
        if (combatOptions.Contains("fighting_style_two_handed") && string.Equals(w.WeaponSize, "L", StringComparison.OrdinalIgnoreCase))
            trainingDmgBonus += 1;
        if (combatOptions.Contains("fighting_style_missile")
            && weaponDef is not null
            && (weaponDef.GroupId.Equals("bows", StringComparison.OrdinalIgnoreCase)
                || weaponDef.GroupId.Equals("crossbows", StringComparison.OrdinalIgnoreCase)
                || weaponDef.GroupId.Equals("slings", StringComparison.OrdinalIgnoreCase)
                || weaponDef.Type.Contains("P", StringComparison.OrdinalIgnoreCase)))
        {
            trainingAtkBonus += 1;
        }

        trainingAtkBonus += GetWeaponSpecificBonus(c.Bonuses?.WeaponAttackBonuses, weaponDef, matchedSelection);
        trainingDmgBonus += GetWeaponSpecificBonus(c.Bonuses?.WeaponDamageBonuses, weaponDef, matchedSelection);
        int enemyAttackBonus = CombatModifierService.GetEnemyAttackBonus(c.Bonuses, enemyContext);
        int enemyDamageBonus = CombatModifierService.GetEnemyDamageBonus(c.Bonuses, enemyContext);

        // STR bonuses
        int atkBonus = 0, dmgBonus = 0;
        if (c.Abilities.TryGetValue("str", out int strScore))
        {
            int muscle = c.SubAbilities.TryGetValue("str_muscle", out int m) ? m : strScore;
            (atkBonus, dmgBonus) = GetStrMeleeBonuses(muscle, c.ExceptionalStrength);
        }
        // Class-appropriate non-proficiency penalty (PHB p.52)
        int nonProfPenalty = proficient ? 0 : RulesEngine.GetNonProficiencyPenalty(c.ClassId ?? string.Empty);
        int totalAttackBonus = atkBonus + trainingAtkBonus + (specialized ? 1 : 0) + enemyAttackBonus;
        int effectiveAtk   = c.Thac0 - totalAttackBonus + nonProfPenalty;
        int totalDamageBonus = dmgBonus + trainingDmgBonus + (specialized ? 2 : 0) + enemyDamageBonus;

        // Build THAC0 display: show base + modifiers breakdown when non-proficient
        var thac0Notes = new List<string>();
        if (!proficient)
            thac0Notes.Add($"NP {FormatSigned(nonProfPenalty)}");
        if (enemyAttackBonus != 0)
            thac0Notes.Add($"enemy {FormatSigned(enemyAttackBonus)}");
        string thac0Display = thac0Notes.Count > 0
            ? $"{effectiveAtk} ({string.Join(", ", thac0Notes)})"
            : effectiveAtk.ToString();

        string dmgSuffix = totalDamageBonus > 0 ? $"+{totalDamageBonus}" : totalDamageBonus < 0 ? $"{totalDamageBonus}" : string.Empty;
        string dmgSM = string.IsNullOrWhiteSpace(w.WeaponDamageSmallMedium) ? "—"
                 : totalDamageBonus != 0 ? $"{w.WeaponDamageSmallMedium}{dmgSuffix}" : w.WeaponDamageSmallMedium;
        string dmgL  = string.IsNullOrWhiteSpace(w.WeaponDamageLarge) ? "—"
                 : totalDamageBonus != 0 ? $"{w.WeaponDamageLarge}{dmgSuffix}" : w.WeaponDamageLarge;

        string sf   = w.WeaponSpeed > 0 ? w.WeaponSpeed.ToString() : "—";
        string apr  = CalculateWeaponApr(c, weaponDef, specialized);
        string type = string.IsNullOrWhiteSpace(w.WeaponType) ? "—" : w.WeaponType.ToUpperInvariant();
        string size = string.IsNullOrWhiteSpace(w.WeaponSize)  ? "—" : w.WeaponSize.ToUpperInvariant();

        // Alternating row shading
        if (rowIndex % 2 == 0)
        {
            var rowBg = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(18, 255, 255, 255)),
            };
            Grid.SetRow(rowBg, rowIndex);
            Grid.SetColumnSpan(rowBg, colCount);
            tbl.Children.Insert(0, rowBg); // insert behind cells
        }

        // Cell helper — column 0 has no left padding
        void Cell(int col, string text, bool bold = false, Brush? fg = null)
        {
            var tb = new TextBlock
            {
                Text              = text,
                FontFamily        = (FontFamily)FindResource("FontBody"),
                FontSize          = bold ? 13 : 12,
                FontWeight        = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground        = fg ?? (Brush)FindResource("BrushText"),
                Padding           = new Thickness(col == 0 ? 0 : 4, 3, 4, 3),
                TextWrapping      = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(tb, rowIndex);
            Grid.SetColumn(tb, col);
            tbl.Children.Add(tb);
        }

        Cell(0, role,          false, (Brush)FindResource("BrushDim"));
        Cell(1, w.ItemName,    true);
        Cell(2, thac0Display,  false, proficient ? null : (Brush)FindResource("BrushRed"));
        Cell(3, sf);
        Cell(4, apr);
        Cell(5, dmgSM);
        Cell(6, dmgL);
        Cell(7, type);
        Cell(8, size);

        // Prof badge
        var badgeBrush = specialized ? (Brush)FindResource("BrushTitle")
                       : proficient  ? (Brush)FindResource("BrushGreenLt")
                                     : (Brush)FindResource("BrushRed");
        var badge = new Border
        {
            Background          = badgeBrush,
            CornerRadius        = new CornerRadius(3),
            Padding             = new Thickness(5, 1, 5, 1),
            Margin              = new Thickness(4, 3, 0, 3),
            VerticalAlignment   = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text       = profNote,
                FontFamily = (FontFamily)FindResource("FontBody"),
                FontSize   = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
            },
        };
        Grid.SetRow(badge, rowIndex);
        Grid.SetColumn(badge, 9);
        tbl.Children.Add(badge);
    }

    private static int GetWeaponSpecificBonus(
        Dictionary<string, int>? bonuses,
        WeaponDefinition? weaponDef,
        WeaponProficiencySelection? matchedSelection)
    {
        if (bonuses is null || bonuses.Count == 0)
            return 0;

        int total = 0;
        foreach (var (key, value) in bonuses)
        {
            if (value == 0 || string.IsNullOrWhiteSpace(key))
                continue;

            if (key.Equals("chosen_weapon", StringComparison.OrdinalIgnoreCase) && matchedSelection?.WeaponOfChoice == true)
            {
                total += value;
                continue;
            }

            if (weaponDef is null)
                continue;

            if (key.Equals(weaponDef.Id, StringComparison.OrdinalIgnoreCase)
                || key.Equals(weaponDef.GroupId, StringComparison.OrdinalIgnoreCase)
                || key.Equals(weaponDef.TightGroupId, StringComparison.OrdinalIgnoreCase))
            {
                total += value;
                continue;
            }

            // Support generic ability keys from class/race mechanics.
            string normalized = key.Trim().ToLowerInvariant();
            bool isBow = string.Equals(weaponDef.GroupId, "bows", StringComparison.OrdinalIgnoreCase)
                || string.Equals(weaponDef.TightGroupId, "bows", StringComparison.OrdinalIgnoreCase);
            bool isCrossbow = string.Equals(weaponDef.GroupId, "crossbows", StringComparison.OrdinalIgnoreCase)
                || string.Equals(weaponDef.TightGroupId, "crossbows", StringComparison.OrdinalIgnoreCase);
            bool isSling = string.Equals(weaponDef.GroupId, "slings", StringComparison.OrdinalIgnoreCase)
                || string.Equals(weaponDef.TightGroupId, "slings", StringComparison.OrdinalIgnoreCase);
            bool isThrown = weaponDef.Type?.Contains("P", StringComparison.OrdinalIgnoreCase) == true;
            bool isMissile = isBow || isCrossbow || isSling || isThrown;

            if ((normalized == "bow" || normalized == "bows") && isBow)
                total += value;
            else if ((normalized == "crossbow" || normalized == "crossbows") && isCrossbow)
                total += value;
            else if ((normalized == "sling" || normalized == "slings") && isSling)
                total += value;
            else if ((normalized == "thrown" || normalized == "throwing") && isThrown)
                total += value;
            else if ((normalized == "missile" || normalized == "ranged") && isMissile)
                total += value;
            else if ((normalized == "melee" || normalized == "hand_to_hand") && !isMissile)
                total += value;
        }

        return total;
    }

        /// <summary>
        /// Computes effective attacks per round for a weapon, taking into account:
        /// - Warrior class level progression (stored in c.AttackRate)
        /// - The weapon's own base APR from the catalog (for non-warriors or slow weapons)
        /// - Weapon specialization (+½ attack per round for warriors only)
        /// Returns a display string like "1", "3/2", "2", "5/2".
        /// </summary>
        private static string CalculateWeaponApr(CharacterSheet c, WeaponDefinition? weaponDef, bool specialized)
        {
            bool isWarrior = c.ClassId is "fighter" or "paladin" or "ranger";

            // Base APR in half-attacks (×2 denominator)
            int baseHalves;
            if (isWarrior)
            {
                // Warrior level progression already computed and stored in AttackRate
                baseHalves = c.AttackRate switch
                {
                    "2/round"    => 4,
                    "3/2 rounds" => 3,
                    _            => 2,   // "1/round"
                };
            }
            else
            {
                // Non-warrior: use the weapon's own APR from catalog
                string weaponApr = weaponDef?.AttacksPerRound ?? "1";
                baseHalves = weaponApr switch
                {
                    "1/2" => 1,
                    "1"   => 2,
                    "3/2" => 3,
                    "2"   => 4,
                    "3"   => 6,
                    _     => 2,
                };
            }

            // Specialization grants +½ attack per round (warriors only)
            if (specialized && isWarrior)
                baseHalves += 1;

            return baseHalves switch
            {
                1 => "1/2",
                2 => "1",
                3 => "3/2",
                4 => "2",
                5 => "5/2",
                6 => "3",
                _ => $"{baseHalves}/2",
            };
        }

        private static (int atk, int dmg) GetStrMeleeBonuses(int muscle, int exStr)
    {
        if (muscle == 18 && exStr > 0)
        {
            return exStr switch
            {
                <= 50 => (1, 3),
                <= 75 => (2, 3),
                <= 90 => (2, 4),
                <= 99 => (2, 5),
                _     => (3, 6),
            };
        }
        return muscle switch
        {
            1        => (-5, -4),
            2        => (-3, -2),
            3        => (-3, -1),
            4 or 5   => (-2, -1),
            6 or 7   => (-1,  0),
            >= 8 and <= 15 => (0, 0),
            16       => (0,  1),
            17       => (1,  1),
            18       => (1,  2),
            19       => (3,  7),
            20       => (3,  8),
            _        => (0,  0),
        };
    }

    // ── Ability block ─────────────────────────────────────────────────────────

    private static readonly string[] AbilityNames = { "Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma" };
    private static readonly string[] AbilityKeys  = { "str",      "dex",       "con",           "int",          "wis",    "cha"      };

    private static string[] GetSubAbilityKeys(string ability) => ability.ToLowerInvariant() switch
    {
        "str" => new[] { "str_muscle",   "str_stamina" },
        "dex" => new[] { "dex_aim",      "dex_balance" },
        "con" => new[] { "con_health",   "con_fitness" },
        "int" => new[] { "int_reason",   "int_knowledge" },
        "wis" => new[] { "wis_intuition","wis_willpower","wis_perception" },
        "cha" => new[] { "cha_leadership","cha_appearance" },
        _     => Array.Empty<string>(),
    };

    private static string SubAbilityLabel(string subKey) => subKey switch
    {
        "str_muscle"     => "Muscle",
        "str_stamina"    => "Stamina",
        "dex_aim"        => "Aim",
        "dex_balance"    => "Balance",
        "con_health"     => "Health",
        "con_fitness"    => "Fitness",
        "int_reason"     => "Reason",
        "int_knowledge"  => "Knowledge",
        "wis_intuition"  => "Intuition",
        "wis_willpower"  => "Willpower",
        "wis_perception" => "Perception",
        "cha_leadership" => "Leadership",
        "cha_appearance" => "Appearance",
        _ => subKey,
    };

    private static string AbilityFullName(string key) => key.ToLowerInvariant() switch
    {
        "str" => "Strength",
        "dex" => "Dexterity",
        "con" => "Constitution",
        "int" => "Intelligence",
        "wis" => "Wisdom",
        "cha" => "Charisma",
        _ => key.ToUpperInvariant(),
    };

    private void AddAbilityBlock(StackPanel parent, CharacterSheet c, string abilityKey, int score)
    {
        string scoreDisplay = score.ToString();
        if (abilityKey == "str" && c.ExceptionalStrength > 0)
            scoreDisplay = $"{score}/{c.ExceptionalStrength:D2}";

        // ── Header bar ────────────────────────────────────────────────────────
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameText = new TextBlock
        {
            Text       = AbilityFullName(abilityKey).ToUpperInvariant(),
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize   = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BrushTitle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var scoreText = new TextBlock
        {
            Text       = scoreDisplay,
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize   = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BrushTitle"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        Grid.SetColumn(nameText,  0);
        Grid.SetColumn(scoreText, 1);
        headerGrid.Children.Add(nameText);
        headerGrid.Children.Add(scoreText);

        var headerBorder = new Border
        {
            Background      = (Brush)FindResource("BrushBtn"),
            BorderBrush     = (Brush)FindResource("BrushBorder2"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius    = new CornerRadius(3, 3, 0, 0),
            Padding         = new Thickness(10, 5, 10, 5),
            Child           = headerGrid,
        };

        var bodyPanel = new StackPanel { Margin = new Thickness(10, 6, 10, 8) };
        bodyPanel.Children.Add(new TextBlock
        {
            Text = $"{AbilityFullName(abilityKey)}: {scoreDisplay}",
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("BrushText"),
            TextWrapping = TextWrapping.Wrap,
        });

        // ── Outer card ────────────────────────────────────────────────────────
        var outer = new StackPanel();
        outer.Children.Add(headerBorder);
        outer.Children.Add(bodyPanel);

        parent.Children.Add(new Border
        {
            BorderBrush     = (Brush)FindResource("BrushBorder2"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(3),
            Child           = outer,
        });
    }

    private Expander BuildSection(string title, bool expanded, Action<StackPanel> populate)
    {
        var header = new TextBlock
        {
            Text = title,
            FontFamily  = (FontFamily)FindResource("FontBody"),
            FontSize    = 12,
            FontWeight  = FontWeights.Bold,
            Foreground  = (Brush)FindResource("BrushTitle"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var panel = new StackPanel { Margin = new Thickness(10, 6, 10, 8) };
        populate(panel);

        return new Expander
        {
            Header     = header,
            IsExpanded = expanded,
            Content    = panel,
            Style      = (Style)FindResource("SheetExpander"),
            Margin     = new Thickness(0, 0, 0, 4),
        };
    }

    private void BuildSpellsAtEndColumns(StackPanel panel, CharacterSheet character)
    {
        var columnHost = new WrapPanel
        {
            Margin = new Thickness(0, 2, 0, 0),
            Orientation = Orientation.Horizontal,
        };

        bool hasAnyColumns = false;

        var priestSlots = BuildPriestSlotTotalsByLevel(character);
        var priestSpellsByLevel = GetAccessiblePriestSpellDefinitions(character)
            .GroupBy(s => TryParseSpellLevel(s.Level, out int level) ? level : int.MaxValue)
            .Where(g => g.Key != int.MaxValue)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList());

        foreach (int level in priestSpellsByLevel.Keys.Union(priestSlots.Keys).OrderBy(x => x))
        {
            priestSlots.TryGetValue(level, out var slotInfo);
            priestSpellsByLevel.TryGetValue(level, out var spells);
            spells ??= new List<SpellDefinition>();

            string slotText = slotInfo.Total > 0
                ? $"Slots: {slotInfo.Total} ({slotInfo.Base} base + {slotInfo.WisdomBonus} Wis)"
                : "Slots: 0";

            columnHost.Children.Add(BuildSpellLevelColumnCard($"Priest L{level}", slotText, spells));
            hasAnyColumns = true;
        }

        if (IsWizardCaster(character))
        {
            int maxArcaneLevel = GetHighestSpellLevel(character.ArcaneSpellSlots);
            var wizardSpellsByLevel = GetWizardSpellbookDefinitions(character)
                .Where(s => TryParseSpellLevel(s.Level, out int level) && level <= maxArcaneLevel)
                .GroupBy(s => TryParseSpellLevel(s.Level, out int level) ? level : int.MaxValue)
                .Where(g => g.Key != int.MaxValue)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList());

            foreach (int level in wizardSpellsByLevel.Keys.OrderBy(x => x))
            {
                int slots = character.ArcaneSpellSlots.TryGetValue(level, out int value) ? value : 0;
                columnHost.Children.Add(BuildSpellLevelColumnCard($"Wizard L{level}", $"Slots: {slots}", wizardSpellsByLevel[level]));
                hasAnyColumns = true;
            }
        }

        if (!hasAnyColumns)
        {
            AddItem(panel, "(no spell access at current level)");
            return;
        }

        panel.Children.Add(columnHost);
    }

    private Border BuildSpellLevelColumnCard(string title, string subtitle, List<SpellDefinition> spells)
    {
        var card = new Border
        {
            Width = 920,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("BrushBorder2"),
            Background = (Brush)FindResource("BrushPanel"),
            CornerRadius = new CornerRadius(2),
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BrushTitle"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize = 11,
            Foreground = (Brush)FindResource("BrushDim"),
            Margin = new Thickness(0, 2, 0, 6),
        });

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });

        string[] headers = { "Spell", "Damage/Healing", "Cast Time", "Duration", "Range", "Save" };
        for (int i = 0; i < headers.Length; i++)
        {
            var text = new TextBlock
            {
                Text = headers[i],
                FontFamily = (FontFamily)FindResource("FontBody"),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("BrushDim"),
                Margin = new Thickness(0, 0, 6, 0),
            };
            Grid.SetColumn(text, i);
            headerGrid.Children.Add(text);
        }

        stack.Children.Add(headerGrid);
        stack.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)FindResource("BrushBorder2"),
            Margin = new Thickness(0, 0, 0, 6),
        });

        if (spells.Count == 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "(no available spells)",
                FontFamily = (FontFamily)FindResource("FontBody"),
                FontSize = 12,
                Foreground = (Brush)FindResource("BrushDim"),
            });
        }
        else
        {
            foreach (var spell in spells)
            {
                stack.Children.Add(BuildSpellAtGlanceRow(spell));
            }
        }

        card.Child = stack;
        return card;
    }

    private UIElement BuildSpellAtGlanceRow(SpellDefinition spell)
    {
        var container = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });

        var values = new[]
        {
            spell.Name,
            BuildSpellEffectAtGlance(spell, _selectedCharacter?.Level ?? 1),
            string.IsNullOrWhiteSpace(spell.CastTime) ? "-" : spell.CastTime.Trim(),
            string.IsNullOrWhiteSpace(spell.Duration) ? "-" : spell.Duration.Trim(),
            string.IsNullOrWhiteSpace(spell.Range) ? "-" : spell.Range.Trim(),
            string.IsNullOrWhiteSpace(spell.Save) ? "-" : spell.Save.Trim(),
        };

        for (int i = 0; i < values.Length; i++)
        {
            var text = new TextBlock
            {
                Text = values[i],
                FontFamily = (FontFamily)FindResource("FontBody"),
                FontSize = 12,
                Foreground = (Brush)FindResource("BrushText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 6, 0),
            };
            Grid.SetColumn(text, i);
            row.Children.Add(text);
        }

        container.Children.Add(row);

        var brief = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(spell.BriefDescription) ? "Brief: (no brief description)" : $"Brief: {spell.BriefDescription.Trim()}",
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize = 11,
            Foreground = (Brush)FindResource("BrushDim"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        };
        container.Children.Add(brief);

        container.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)FindResource("BrushBorder2"),
            Margin = new Thickness(0, 5, 0, 0),
        });

        return container;
    }

    private Dictionary<int, (int Base, int WisdomBonus, int Total)> BuildPriestSlotTotalsByLevel(CharacterSheet character)
    {
        var totals = new Dictionary<int, (int Base, int WisdomBonus, int Total)>();
        var wisBonusSlots = GetPriestWisdomBonusSlots(character.Abilities.GetValueOrDefault("wis", 10));

        foreach (var level in character.DivineSpellSlots.Keys.OrderBy(x => x))
        {
            int baseSlots = character.DivineSpellSlots.TryGetValue(level, out int baseValue) ? baseValue : 0;
            int bonusSlots = wisBonusSlots.TryGetValue(level, out int bonusValue) ? bonusValue : 0;
            int totalSlots = baseSlots + bonusSlots;
            if (totalSlots > 0)
                totals[level] = (baseSlots, bonusSlots, totalSlots);
        }

        return totals;
    }

    private static Dictionary<int, int> GetPriestWisdomBonusSlots(int wisdom)
    {
        var bonus = new Dictionary<int, int>();

        if (wisdom >= 13) bonus[1] = 1;
        if (wisdom >= 14) bonus[1] = 2;
        if (wisdom >= 15) bonus[2] = 1;
        if (wisdom >= 16) bonus[2] = 2;
        if (wisdom >= 17) bonus[3] = 1;
        if (wisdom >= 18) bonus[4] = 1;

        return bonus;
    }

    private void AddKV(StackPanel panel, string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lbl = new TextBlock
        {
            Text       = label,
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize   = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("BrushDim"),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var val = new TextBlock
        {
            Text         = value,
            FontFamily   = (FontFamily)FindResource("FontBody"),
            FontSize     = 13,
            Foreground   = (Brush)FindResource("BrushText"),
            TextWrapping = TextWrapping.Wrap,
        };

        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(val, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(val);
        panel.Children.Add(grid);
    }

    private void AddItem(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text         = text,
            FontFamily   = (FontFamily)FindResource("FontBody"),
            FontSize     = 13,
            Foreground   = (Brush)FindResource("BrushText"),
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 2, 0, 0),
        });
    }

    private void AddDivider(StackPanel panel, string label)
    {
        panel.Children.Add(new TextBlock
        {
            Text       = label.ToUpperInvariant(),
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize   = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BrushDim"),
            Margin     = new Thickness(0, 5, 0, 1),
        });
        panel.Children.Add(new Border
        {
            Height              = 1,
            Background          = (Brush)FindResource("BrushBorder2"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin              = new Thickness(0, 0, 0, 3),
        });
    }

    private static string FormatSavingThrowKey(string key)
    {
        // e.g. "save_poison" → "vs. Poison"
        string raw = key.StartsWith("save_", StringComparison.OrdinalIgnoreCase) ? key[5..] : key;
        return "vs. " + char.ToUpper(raw[0]) + raw[1..].Replace("_", " ");
    }

    private static string BuildStatusText(string prefix) => prefix;

    private sealed record SaveRow(string Label, string CategoryKey, int BaseTarget, int Bonus, int FinalTarget);

    private sealed record SavingThrowCategory(string Key, string Label, string[] BonusKeys);

    private static readonly SavingThrowCategory[] StandardSavingThrowCategories =
    {
        new("death_poison", "Paralyzation / Poison / Death Magic", new[] { "death", "poison" }),
        new("rod_staff_wand", "Rod / Staff / Wand", new[] { "rod_staff_wand" }),
        new("petrification_polymorph", "Petrification / Polymorph", new[] { "petrification_polymorph" }),
        new("breath_weapon", "Breath Weapon", new[] { "breath_weapon" }),
        new("spell", "Spell", new[] { "spell", "magic" }),
    };

    private List<SaveRow> BuildSavingThrowRows(CharacterSheet character)
    {
        var result = new List<SaveRow>();

        int level = Math.Max(1, character.Level);
        var classIds = (character.ClassIds is { Count: > 0 }
                ? character.ClassIds
                : string.IsNullOrWhiteSpace(character.ClassId)
                    ? new List<string>()
                    : new List<string> { character.ClassId })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (classIds.Count == 0)
            classIds.Add("fighter");

        foreach (var category in StandardSavingThrowCategories)
        {
            int baseTarget = classIds
                .Select(classId => GetBaseSavingThrowTarget(classId, level, category.Key))
                .Min();

            int bonus = GetBestApplicableSaveBonus(character.Bonuses, category.BonusKeys);
            int finalTarget = Math.Clamp(baseTarget - bonus, 1, 30);
            result.Add(new SaveRow(category.Label, category.Key, baseTarget, bonus, finalTarget));
        }

        return result;
    }

    private static int GetBaseSavingThrowTarget(string classId, int level, string categoryKey)
    {
        string token = ResolveClassSaveGroup(classId);
        int band = SaveLevelBand(level, token);

        return token switch
        {
            "warrior" => categoryKey switch
            {
                "death_poison" => new[] { 14, 13, 11, 10, 8, 7, 5, 4, 3, 2 }[band],
                "rod_staff_wand" => new[] { 16, 15, 13, 12, 10, 9, 7, 6, 5, 4 }[band],
                "petrification_polymorph" => new[] { 15, 14, 12, 11, 9, 8, 6, 5, 4, 3 }[band],
                "breath_weapon" => new[] { 17, 16, 14, 13, 11, 10, 8, 7, 6, 5 }[band],
                _ => new[] { 17, 16, 14, 13, 11, 10, 8, 7, 6, 5 }[band],
            },
            "priest" => categoryKey switch
            {
                "death_poison" => new[] { 10, 9, 7, 6, 5, 4, 3 }[band],
                "rod_staff_wand" => new[] { 14, 13, 11, 10, 9, 8, 7 }[band],
                "petrification_polymorph" => new[] { 13, 12, 10, 9, 8, 7, 6 }[band],
                "breath_weapon" => new[] { 16, 15, 13, 12, 11, 10, 9 }[band],
                _ => new[] { 15, 14, 12, 11, 10, 9, 8 }[band],
            },
            "rogue" => categoryKey switch
            {
                "death_poison" => new[] { 13, 12, 11, 10, 9 }[band],
                "rod_staff_wand" => new[] { 14, 12, 10, 8, 6 }[band],
                "petrification_polymorph" => new[] { 12, 11, 10, 9, 8 }[band],
                "breath_weapon" => new[] { 16, 15, 14, 13, 12 }[band],
                _ => new[] { 15, 13, 11, 9, 7 }[band],
            },
            _ => categoryKey switch
            {
                "death_poison" => new[] { 14, 13, 11, 10 }[band],
                "rod_staff_wand" => new[] { 11, 9, 7, 5 }[band],
                "petrification_polymorph" => new[] { 13, 11, 9, 7 }[band],
                "breath_weapon" => new[] { 15, 13, 11, 9 }[band],
                _ => new[] { 12, 10, 8, 6 }[band],
            },
        };
    }

    private static int SaveLevelBand(int level, string group)
    {
        int clamped = Math.Clamp(level, 1, 20);
        return group switch
        {
            "warrior" => (clamped - 1) / 2,
            "priest" => (clamped - 1) / 3,
            "rogue" => (clamped - 1) / 4,
            _ => (clamped - 1) / 5,
        };
    }

    private static string ResolveClassSaveGroup(string classId)
    {
        string token = (classId ?? string.Empty).Trim().ToLowerInvariant();
        return token switch
        {
            "fighter" or "paladin" or "ranger" => "warrior",
            "cleric" or "druid" => "priest",
            "thief" or "bard" or "rogue" => "rogue",
            _ => "wizard",
        };
    }

    private static int GetBestApplicableSaveBonus(AbilityBonuses bonuses, IEnumerable<string> bonusKeys)
    {
        int allBonus = CombatModifierService.GetSavingThrowBonus(bonuses, "all");
        int best = int.MinValue;

        foreach (var key in bonusKeys)
        {
            int withKey = CombatModifierService.GetSavingThrowBonus(bonuses, key);
            if (withKey > best)
                best = withKey;
        }

        if (best == int.MinValue)
            return allBonus;

        // Each keyed query already includes any "all" entry; keep the strongest applicable bonus path.
        return Math.Max(allBonus, best);
    }

    private static string FormatSigned(int value)
        => value > 0 ? $"+{value}" : value.ToString();

    private void NormalizeWizardSpellbookIds(CharacterSheet character)
    {
        var normalizedBooks = new List<WizardSpellbook>();
        bool changed = false;
        var allowedSchools = GetSelectedWizardSchools(character);
        var blockedSchools = GetWizardOppositionSchools(character);

        foreach (var book in character.WizardSpellbooks)
        {
            if (book is null)
            {
                changed = true;
                continue;
            }

            book.SpellPages ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            book.CapacityPages = book.CapacityPages <= 0 ? 100 : book.CapacityPages;
            var validPages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in book.SpellPages)
            {
                string spellId = kv.Key;
                int pages = Math.Max(1, kv.Value);
                var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, spellId, StringComparison.OrdinalIgnoreCase));
                if (spell is null || !string.Equals(spell.Category, "arcane", StringComparison.OrdinalIgnoreCase))
                {
                    changed = true;
                    continue;
                }

                if (!IsWizardSpellAllowedForCharacter(spell, allowedSchools, blockedSchools))
                {
                    changed = true;
                    continue;
                }

                validPages[spell.Id] = pages;
            }

            book.SpellPages = validPages;
            normalizedBooks.Add(book);
        }

        character.WizardSpellbooks = normalizedBooks;
        UpdateLegacyWizardSpellbookIds(character);

        if (changed)
            _app.SaveCharacters();
    }

    private void NormalizeTrackedSpellIds(CharacterSheet character)
    {
        if (character.TrackedSpellIds.Count == 0)
            return;

        var normalized = new List<string>();
        bool changed = false;
        bool isWizardCaster = IsWizardCaster(character);
        var selectedPriestSpheres = GetSelectedPriestSpheres(character);

        foreach (string spellId in character.TrackedSpellIds)
        {
            var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, spellId, StringComparison.OrdinalIgnoreCase));
            if (spell is null)
            {
                changed = true;
                continue;
            }

            if (normalized.Any(id => string.Equals(id, spell.Id, StringComparison.OrdinalIgnoreCase)))
            {
                changed = true;
                continue;
            }

            if (isWizardCaster
                && string.Equals(spell.Category, "arcane", StringComparison.OrdinalIgnoreCase)
                && !character.WizardSpellbookIds.Any(id => string.Equals(id, spell.Id, StringComparison.OrdinalIgnoreCase)))
            {
                changed = true;
                continue;
            }

            if (string.Equals(spell.Category, "divine", StringComparison.OrdinalIgnoreCase)
                && !IsPriestSpellAllowedForCharacter(spell, selectedPriestSpheres))
            {
                changed = true;
                continue;
            }

            normalized.Add(spell.Id);
        }

        if (!changed)
            return;

        character.TrackedSpellIds = normalized;
        _app.SaveCharacters();
    }

    private List<SpellDefinition> GetWizardSpellbookDefinitions(CharacterSheet character)
    {
        return character.WizardSpellbookIds
            .Select(id => _app.Rules.Spells.FirstOrDefault(spell => string.Equals(spell.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(spell => spell is not null)
            .Cast<SpellDefinition>()
            .ToList();
    }

    private List<SpellDefinition> GetAccessiblePriestSpellDefinitions(CharacterSheet character)
    {
        int maxDivineLevel = GetHighestSpellLevel(character.DivineSpellSlots);
        if (maxDivineLevel <= 0)
            return new List<SpellDefinition>();

        var selectedPriestSpheres = GetSelectedPriestSpheres(character);

        return _app.Rules.Spells
            .Where(spell => string.Equals(spell.Category, "divine", StringComparison.OrdinalIgnoreCase))
            .Where(spell => TryParseSpellLevel(spell.Level, out int spellLevel) && spellLevel <= maxDivineLevel)
            .Where(spell => IsPriestSpellAllowedForCharacter(spell, selectedPriestSpheres))
            .OrderBy(spell => TryParseSpellLevel(spell.Level, out int level) ? level : int.MaxValue)
            .ThenBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool IsWizardCaster(CharacterSheet character)
    {
        if (!character.ArcaneSpellSlots.Any(kv => kv.Value > 0))
            return false;

        return GetCharacterClassIds(character).Any(IsWizardClassId);
    }

    private static IEnumerable<string> GetCharacterClassIds(CharacterSheet character)
    {
        if (character.ClassIds.Count > 0)
            return character.ClassIds;

        return string.IsNullOrWhiteSpace(character.ClassId)
            ? Enumerable.Empty<string>()
            : new[] { character.ClassId };
    }

    private static bool IsWizardClassId(string classId)
    {
        string token = (classId ?? string.Empty).Trim();
        return token.Equals("wizard", StringComparison.OrdinalIgnoreCase)
            || token.Equals("mage", StringComparison.OrdinalIgnoreCase)
            || token.Equals("illusionist", StringComparison.OrdinalIgnoreCase);
    }

    private HashSet<string> GetSelectedWizardSchools(CharacterSheet character)
    {
        var schools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (school, selected) in character.SelectedWizardSchools)
        {
            if (!selected)
                continue;

            string normalized = NormalizeWizardSchoolName(school);
            if (!string.IsNullOrWhiteSpace(normalized))
                schools.Add(normalized);
        }

        foreach (string classId in GetCharacterClassIds(character).Where(IsWizardClassId))
        {
            if (!character.SchoolsByClass.TryGetValue(classId, out var byClassSchools))
                continue;

            foreach (var (school, selected) in byClassSchools)
            {
                if (!selected)
                    continue;

                string normalized = NormalizeWizardSchoolName(school);
                if (!string.IsNullOrWhiteSpace(normalized))
                    schools.Add(normalized);
            }
        }

        return schools;
    }

    private HashSet<string> GetWizardOppositionSchools(CharacterSheet character)
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(character.WizardSpecializationId)
            || !_app.Rules.Classes.TryGetValue("wizard", out var wizardClass)
            || wizardClass.Specializations is null)
        {
            return blocked;
        }

        var spec = wizardClass.Specializations.FirstOrDefault(s =>
            string.Equals(s.Id, character.WizardSpecializationId, StringComparison.OrdinalIgnoreCase));
        if (spec is null)
            return blocked;

        foreach (string school in spec.OppositionSchools)
        {
            string normalized = NormalizeWizardSchoolName(school);
            if (!string.IsNullOrWhiteSpace(normalized))
                blocked.Add(normalized);
        }

        return blocked;
    }

    private static bool IsWizardSpellAllowedForCharacter(
        SpellDefinition spell,
        HashSet<string> allowedSchools,
        HashSet<string> blockedSchools)
    {
        var spellSchools = ParseSpellSchoolTokens(spell.Schools);
        if (spellSchools.Count == 0)
            return true;

        var permittedSchools = spellSchools
            .Where(school => !blockedSchools.Contains(school))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (permittedSchools.Count == 0)
            return false;

        if (allowedSchools.Count == 0)
            return true;

        return permittedSchools.Any(allowedSchools.Contains);
    }

    private static HashSet<string> ParseSpellSchoolTokens(string schoolsText)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(schoolsText))
            return tokens;

        foreach (string raw in Regex.Split(schoolsText, @"\s*(,|;|/|\||&|\band\b)\s*", RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(raw)
                || raw == ","
                || raw == ";"
                || raw == "/"
                || raw == "|"
                || raw == "&"
                || string.Equals(raw, "and", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string normalized = NormalizeWizardSchoolName(raw);
            if (!string.IsNullOrWhiteSpace(normalized))
                tokens.Add(normalized);
        }

        return tokens;
    }

    private static string NormalizeWizardSchoolName(string school)
    {
        string token = (school ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        if (token.Equals("Greater Divination", StringComparison.OrdinalIgnoreCase))
            return "Divination";

        return token;
    }

    private Dictionary<string, string> GetSelectedPriestSpheres(CharacterSheet character)
    {
        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sphere, access) in character.SelectedSpheres)
        {
            if (!string.IsNullOrWhiteSpace(sphere) && !string.IsNullOrWhiteSpace(access))
                selected[sphere.Trim()] = access.Trim();
        }

        foreach (string classId in GetCharacterClassIds(character).Where(IsPriestClassId))
        {
            if (!character.SpheresByClass.TryGetValue(classId, out var byClassSpheres))
                continue;

            foreach (var (sphere, access) in byClassSpheres)
            {
                if (!string.IsNullOrWhiteSpace(sphere) && !string.IsNullOrWhiteSpace(access))
                    selected[sphere.Trim()] = access.Trim();
            }
        }

        return selected;
    }

    private static bool IsPriestClassId(string classId)
    {
        string token = (classId ?? string.Empty).Trim();
        return token.Equals("cleric", StringComparison.OrdinalIgnoreCase)
            || token.Equals("druid", StringComparison.OrdinalIgnoreCase)
            || token.Equals("ranger", StringComparison.OrdinalIgnoreCase)
            || token.Equals("paladin", StringComparison.OrdinalIgnoreCase)
            || token.Equals("priest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPriestSpellAllowedForCharacter(SpellDefinition spell, Dictionary<string, string> selectedSpheres)
    {
        var spellSpheres = ParseSpellSchoolTokens(spell.Schools);
        if (spellSpheres.Count == 0)
            return true;

        if (selectedSpheres.Count == 0)
            return true;

        return spellSpheres.Any(sphere => RulesEngine.PriestHasSphereAccess(sphere, selectedSpheres, "minor"));
    }

    private static int GetHighestSpellLevel(Dictionary<int, int> slots)
    {
        return slots
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key)
            .DefaultIfEmpty(0)
            .Max();
    }


    private void EnsureCharacterCollectionsInitialized(CharacterSheet character)
    {
        bool changed = false;

        if (character.Abilities is null) { character.Abilities = new(); changed = true; }
        if (character.SubAbilities is null) { character.SubAbilities = new(); changed = true; }
        if (character.WeaponProficiencies is null) { character.WeaponProficiencies = new(); changed = true; }
        if (character.NonweaponProficiencies is null) { character.NonweaponProficiencies = new(); changed = true; }
        if (character.Languages is null) { character.Languages = new(); changed = true; }
        if (character.EquipmentSelections is null) { character.EquipmentSelections = new(); changed = true; }
        if (character.Equipment is null) { character.Equipment = new(); changed = true; }
        if (character.Traits is null) { character.Traits = new(); changed = true; }
        if (character.Disadvantages is null) { character.Disadvantages = new(); changed = true; }
        if (character.Notes is null) { character.Notes = new(); changed = true; }
        if (character.ArcaneSpellSlots is null) { character.ArcaneSpellSlots = new(); changed = true; }
        if (character.DivineSpellSlots is null) { character.DivineSpellSlots = new(); changed = true; }
        if (character.WizardSpellbookIds is null) { character.WizardSpellbookIds = new(); changed = true; }
        if (character.WizardSpellbooks is null) { character.WizardSpellbooks = new(); changed = true; }
        if (character.TrackedSpellIds is null) { character.TrackedSpellIds = new(); changed = true; }
        if (character.SelectedSpheres is null) { character.SelectedSpheres = new(); changed = true; }
        if (character.SpheresByClass is null) { character.SpheresByClass = new(); changed = true; }
        if (character.SelectedWizardSchools is null) { character.SelectedWizardSchools = new(); changed = true; }
        if (character.SchoolsByClass is null) { character.SchoolsByClass = new(); changed = true; }
        if (character.ClassIds is null) { character.ClassIds = new(); changed = true; }

        if (changed)
            _app.SaveCharacters();
    }

    private void EnsureSpellbooksInitialized(CharacterSheet character)
    {
        bool changed = false;
        character.WizardSpellbooks ??= new List<WizardSpellbook>();

        // Sync any spellbook items from the character's inventory first.
        if (SpellbookUtility.SyncSpellbooksFromInventory(character))
            changed = true;

        // Only create a generic fallback if inventory sync did not produce any books.
        if (character.WizardSpellbooks.Count == 0)
        {
            var defaultBook = SpellbookUtility.CreateSpellbook(SpellbookUtility.TYPE_STANDARD, "Spellbook 1");
            character.WizardSpellbooks.Add(defaultBook);
            changed = true;
        }

        foreach (var book in character.WizardSpellbooks)
        {
            if (string.IsNullOrWhiteSpace(book.Id))
                book.Id = Guid.NewGuid().ToString("N");

            if (string.IsNullOrWhiteSpace(book.Type))
                book.Type = SpellbookUtility.TYPE_STANDARD;

            if (string.IsNullOrWhiteSpace(book.Name))
                book.Name = "Spellbook";

            if (SpellbookUtility.TryGetSpellbookInfo(book.Type, out int pages, out double weight, out string dimensions))
            {
                if (book.CapacityPages <= 0)
                    book.CapacityPages = pages;
                if (book.WeightLbs <= 0)
                    book.WeightLbs = weight;
                if (string.IsNullOrWhiteSpace(book.Dimensions))
                    book.Dimensions = dimensions;
            }

            book.SpellPages ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        character.WizardSpellbookIds ??= new List<string>();
        foreach (string spellId in character.WizardSpellbookIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            bool alreadyTracked = character.WizardSpellbooks.Any(book => book.SpellPages.ContainsKey(spellId));
            if (alreadyTracked)
                continue;

            var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, spellId, StringComparison.OrdinalIgnoreCase));
            int level = spell is null || !TryParseSpellLevel(spell.Level, out int parsed) ? 1 : parsed;
            int pages = RollSpellPages(level);

            var targetBook = character.WizardSpellbooks.FirstOrDefault(book => SpellbookUtility.CanAddSpellToBook(book, pages));
            if (targetBook is null)
            {
                targetBook = SpellbookUtility.CreateSpellbook(SpellbookUtility.TYPE_STANDARD, $"Spellbook {character.WizardSpellbooks.Count + 1}");
                character.WizardSpellbooks.Add(targetBook);
            }

            targetBook.SpellPages[spellId] = pages;
            changed = true;
        }

        UpdateLegacyWizardSpellbookIds(character);

        if (changed)
            _app.SaveCharacters();
    }

    private void UpdateLegacyWizardSpellbookIds(CharacterSheet character)
    {
        character.WizardSpellbookIds ??= new List<string>();

        // Keep explicit spellbook IDs as the source of truth from char-gen/level-up.
        // Only backfill from legacy WizardSpellbooks when IDs are missing.
        if (character.WizardSpellbookIds.Count > 0)
            return;

        character.WizardSpellbookIds = character.WizardSpellbooks
            .SelectMany(book => book.SpellPages.Keys)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private int RollSpellPages(int spellLevel)
    {
        int level = Math.Clamp(spellLevel, 0, 9);
        var range = SpellbookUtility.GetPageRange(level);
        return _rng.Next(range.Min, range.Max + 1);
    }

    private static bool TryParseSpellLevel(string? levelText, out int level)
    {
        string text = (levelText ?? string.Empty).Trim();
        if (text.Equals("cantrip", StringComparison.OrdinalIgnoreCase))
        {
            level = 0;
            return true;
        }

        var match = Regex.Match(text, "\\d+");
        if (match.Success && int.TryParse(match.Value, out level))
            return level >= 0;

        level = 0;
        return false;
    }

    private static string FormatSpellCategory(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "arcane" => "Arcane",
            "divine" => "Divine",
            "psionic" => "Psionic",
            _ => string.IsNullOrWhiteSpace(category) ? "Spell" : category,
        };
    }
}
