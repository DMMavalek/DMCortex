using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CharacterSheetsScreen : UserControl, IScreen
{
    private const string SectionDragDataFormat = "dmcortex.character-sheet.section";
    private readonly MainWindow _app;
    private readonly Random _rng = new();
    private CharacterSheet? _selectedCharacter;
    private string _selectedCharacterSheetText = string.Empty;
    private readonly List<SectionCard> _sectionCards = new();
    private readonly Dictionary<string, SectionPlacement> _sectionLayoutByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedSectionKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasSavedExpansionState;
    private bool _suspendExpansionStateSave;
    private readonly HashSet<string> _hiddenSectionKeys = new(StringComparer.OrdinalIgnoreCase);
    private Point _dragStartPoint;
    private string? _dragSectionKey;
    private bool _dragDebugEnabled = true;
    private static readonly Dictionary<string, PrintLayoutOptions> PrintLayoutPresetsByProfile = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, bool> SpellPrintGroupingByProfile = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PrintDocumentTheme> PrintThemeByProfile = new(StringComparer.OrdinalIgnoreCase);

    private sealed class SectionCard
    {
        public required string Key { get; init; }
        public required string Title { get; init; }
        public required Expander Expander { get; init; }
    }

    private sealed class SectionPlacement
    {
        public int Lane { get; set; }
        public int Order { get; set; }
        public int Span { get; set; } = 1;
    }

    private sealed class RowLaneTarget
    {
        public int Row { get; init; }
        public int Lane { get; init; }
    }

    public UIElement View => this;
    private static readonly Dictionary<string, string> WizardSchoolAccessByAbilityId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wizard_school_abjuration"] = "Abjuration",
        ["wizard_school_alchemy"] = "Alchemy",
        ["wizard_school_alteration"] = "Alteration",
        ["wizard_school_artifice"] = "Artifice",
        ["wizard_school_conjuration_summoning"] = "Conjuration/Summoning",
        ["wizard_school_dimensional"] = "Dimensional",
        ["wizard_school_divination"] = "Divination",
        ["wizard_school_elemental_air"] = "Elemental (Air)",
        ["wizard_school_elemental_earth"] = "Elemental (Earth)",
        ["wizard_school_elemental_fire"] = "Elemental (Fire)",
        ["wizard_school_elemental_water"] = "Elemental (Water)",
        ["wizard_school_enchantment_charm"] = "Enchantment/Charm",
        ["wizard_school_force"] = "Force",
        ["wizard_school_geometry"] = "Geometry",
        ["wizard_school_illusion"] = "Illusion",
        ["wizard_school_invocation_evocation"] = "Invocation/Evocation",
        ["wizard_school_necromancy"] = "Necromancy",
        ["wizard_school_shadow"] = "Shadow",
        ["wizard_school_song"] = "Song",
        ["wizard_school_wild"] = "Wild Magic",
    };
    private static readonly Dictionary<string, string> PrimarySchoolBySpecializationId = new(StringComparer.OrdinalIgnoreCase)
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

        ClearLayoutSurface();

        if (_selectedCharacter is null)
        {
            LaneSurface.Children.Add(new TextBlock
            {
                Text = "Select a character from the Character Blueprint roster, then open Character Sheets to generate a player-ready sheet.",
                Style = (Style)FindResource("BodyText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 12, 4, 12),
            });
            _selectedCharacterSheetText = string.Empty;
            BtnPrintSummary.IsEnabled = false;
            BtnCopySummary.IsEnabled = false;
            BtnToggleExpandCollapse.IsEnabled = false;
            BtnToggleExpandCollapse.Content = "EXPAND ALL";
            return;
        }

        EnsureCharacterCollectionsInitialized(_selectedCharacter);
        RepairMissingDivineSpellSlots(_selectedCharacter);
        EnsureSpellbooksInitialized(_selectedCharacter);
    NormalizeWizardSpellbookIds(_selectedCharacter);
    NormalizeTrackedSpellIds(_selectedCharacter);
        _selectedCharacterSheetText = BuildCharacterSheetText(_selectedCharacter);
        PopulateSheetSections(_selectedCharacter);
        BtnPrintSummary.IsEnabled = true;
        BtnCopySummary.IsEnabled = true;
        BtnToggleExpandCollapse.IsEnabled = true;
        UpdateExpandCollapseToggleButton();
    StatusText.Text = BuildStatusText($"Sheet generated for {_selectedCharacter.Name}.");
    }

    private IEnumerable<Expander> GetVisibleSectionExpanders()
        => _sectionCards
            .Select(card => card.Expander)
            .Where(expander => expander.Visibility == Visibility.Visible);

    private void UpdateExpandCollapseToggleButton()
    {
        var visible = GetVisibleSectionExpanders().ToList();
        if (visible.Count == 0)
        {
            BtnToggleExpandCollapse.Content = "EXPAND ALL";
            BtnToggleExpandCollapse.IsEnabled = false;
            return;
        }

        BtnToggleExpandCollapse.IsEnabled = _selectedCharacter is not null;
        bool allExpanded = visible.All(expander => expander.IsExpanded);
        BtnToggleExpandCollapse.Content = allExpanded ? "COLLAPSE ALL" : "EXPAND ALL";
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
               + $"Coins: {c.PlatinumPieces}pp {c.GoldPieces}gp {c.SilverPieces}sp {c.CopperPieces}cp\n"
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

    private static string FormatExperienceNeededDisplay(CharacterSheet character)
    {
        int currentLevel = Math.Clamp(character.Level, 1, 20);
        if (currentLevel >= 20)
            return "0 XP (Max level)";

        int nextLevelXp = CharacterProgressionService.GetMinimumExperienceForLevel(character.ClassId, currentLevel + 1);
        int needed = Math.Max(0, nextLevelXp - Math.Max(0, character.ExperiencePoints));
        return $"{needed:n0} XP";
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

    private (List<string> spoken, List<string> written) BuildLanguageBuckets(CharacterSheet character)
    {
        var spoken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (character.SelectedLanguages.Count > 0)
        {
            foreach (var language in character.SelectedLanguages)
            {
                string name = (language.LanguageName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (IsSpokenLanguageSource(language.SourceKey))
                    spoken.Add(name);
                else
                    written.Add(name);
            }
        }
        else
        {
            foreach (string entry in character.Languages)
            {
                var (name, source) = ParseLegacyLanguageEntry(entry);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (IsSpokenLanguageSource(source))
                    spoken.Add(name);
                else
                    written.Add(name);
            }
        }

        if (HasClassAbility(character, "thief_thieves_cant"))
            spoken.Add("Thieves' Cant");

        if (HasClassAbility(character, "ranger_speak_with_animals"))
            spoken.Add("Animal Speech");

        return (
            spoken.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            written.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static (string Name, string SourceKey) ParseLegacyLanguageEntry(string entry)
    {
        string value = (entry ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            return (string.Empty, string.Empty);

        var match = Regex.Match(value, @"^(?<name>.*?)\s*(?:\[(?<source>.+)\])?$");
        if (!match.Success)
            return (value, string.Empty);

        string name = (match.Groups["name"].Value ?? string.Empty).Trim();
        string source = (match.Groups["source"].Value ?? string.Empty).Trim();
        return (name, source);
    }

    private static bool IsSpokenLanguageSource(string sourceKey)
    {
        string normalized = (sourceKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return true;

        if (normalized.Equals("free_spoken", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("modern_languages", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("animal_languages", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("priest_secret_language", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Free Spoken Language", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Modern Languages", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Animal Languages", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Priest Secret Language", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Equals("ancient_languages", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("reading_writing", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Ancient Languages", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Reading/Writing", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.Contains("ancient", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("reading", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("writing", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("written", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
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

        foreach (string abilityKey in AbilityKeys)
        {
            int abilityScore = ResolveAbilityScore(c, abilityKey);
            lines.Add($"{abilityKey.ToUpperInvariant()}: {abilityScore}");
            foreach (string subKey in GetSubAbilityKeys(abilityKey))
            {
                int subScore = ResolveSubAbilityScore(c, abilityKey, subKey, abilityScore);
                string effect = ResolveSubAbilityEffect(c, subKey, subScore);
                lines.Add($"  - {GetSubAbilityDisplayLabel(c, subKey)}: {subScore} ({effect})");
            }
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
        var (spokenLanguages, writtenLanguages) = BuildLanguageBuckets(c);
        lines.Add("Spoken:");
        if (spokenLanguages.Count == 0)
            lines.Add("  (none)");
        else
            foreach (string lang in spokenLanguages)
                lines.Add($"  {lang}");

        lines.Add("Written:");
        if (writtenLanguages.Count == 0)
            lines.Add("  (none)");
        else
            foreach (string lang in writtenLanguages)
                lines.Add($"  {lang}");

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
        lines.Add("=== GEMS ===");
        var gemLines = BuildGemLines(c);
        if (gemLines.Count > 0)
        {
            foreach (var gemLine in gemLines)
                lines.Add(gemLine);
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

    private static List<string> BuildGemLines(CharacterSheet c)
    {
        var lines = new List<string>();
        if (c.Gems.Count > 0)
        {
            foreach (var gem in c.Gems
                .OrderByDescending(x => Math.Max(0, x.ValueGoldPieces))
                .ThenByDescending(x => Math.Max(1, x.Quantity))
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                int quantity = Math.Max(1, gem.Quantity);
                int valueEach = Math.Max(0, gem.ValueGoldPieces);
                int totalValue = quantity * valueEach;
                lines.Add($"{gem.Name} x{quantity} — {valueEach} gp each ({totalValue} gp total)");
            }
            return lines;
        }

        int gemCount = Math.Max(0, c.GemCount);
        int gemValue = Math.Max(0, c.GemValueGoldPieces);
        if (gemCount > 0)
            lines.Add($"Gem total: {gemCount} gem{(gemCount == 1 ? string.Empty : "s")} ({gemValue} gp value)");

        return lines;
    }

    private void AppendSpellsAtEndText(List<string> lines, CharacterSheet c)
    {
        lines.Add(string.Empty);
        lines.Add("=== SPELLS ===");

        var classBonusEntries = BuildClassBonusSpellLikeEntries(c);
        if (classBonusEntries.Count > 0)
        {
            lines.Add("Class bonus spells/powers:");
            foreach (var entry in classBonusEntries)
                lines.Add($"  - {entry.Name}: {entry.Usage} ({entry.Details})");
        }

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
                        lines.Add("    - " + BuildSpellAtGlanceTextLine(spell, c));
                }
            }
        }

        if (IsWizardCaster(c))
        {
            int maxArcaneLevel = GetHighestSpellLevel(c.ArcaneSpellSlots);
            var spellbookByLevel = GetAccessibleWizardSpellDefinitions(c)
                .GroupBy(s => GetDisplayedWizardSpellLevelForReview(c, s, maxArcaneLevel))
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
                        lines.Add("    - " + BuildSpellAtGlanceTextLine(spell, c));
                }
            }
        }

    }

    private string BuildSpellAtGlanceTextLine(SpellDefinition spell, CharacterSheet character)
    {
        string effect = BuildSpellEffectAtGlance(spell, character);
        string cast = GetDisplayedCastTime(character, spell);
        string duration = BuildSpellDurationAtGlance(spell, character);
        string range = GetDisplayedRange(character, spell);
        string save = string.IsNullOrWhiteSpace(spell.Save) ? "-" : spell.Save.Trim();
        string brief = string.IsNullOrWhiteSpace(spell.BriefDescription) ? "(no brief description)" : spell.BriefDescription.Trim();
        return $"{spell.Name} | Dmg/Heal: {effect} | Cast: {cast} | Dur: {duration} | Range: {range} | Save: {save} | {brief}";
    }

    private string BuildSpellEffectAtGlance(SpellDefinition spell, CharacterSheet character)
    {
        int casterLevel = GetEffectiveCasterLevelForSpell(character, spell);
        string effect = SpellDamageService.BuildDamageDisplay(spell, Math.Max(1, casterLevel));
        return string.IsNullOrWhiteSpace(effect) ? "-" : effect;
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        _app.GoTo("characters", -1);
    }

    private void BtnToggleExpandCollapse_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
            return;

        var visible = GetVisibleSectionExpanders().ToList();
        if (visible.Count == 0)
            return;

        bool allExpanded = visible.All(expander => expander.IsExpanded);
        bool targetExpanded = !allExpanded;

        _suspendExpansionStateSave = true;
        try
        {
            foreach (var expander in visible)
            {
                expander.IsExpanded = targetExpanded;
                if (expander.Tag is not string key || string.IsNullOrWhiteSpace(key))
                    continue;

                if (targetExpanded)
                    _expandedSectionKeys.Add(key);
                else
                    _expandedSectionKeys.Remove(key);
            }
        }
        finally
        {
            _suspendExpansionStateSave = false;
        }

        _hasSavedExpansionState = true;
        SaveCurrentSectionLayoutPreference();
        UpdateExpandCollapseToggleButton();
        StatusText.Text = BuildStatusText(targetExpanded ? "Expanded all visible sections." : "Collapsed all visible sections.");
    }

    private void BtnPrintSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
            return;

        try
        {
            var sectionTitles = GetPrintableSectionTitlesInLayoutOrder();

            if (sectionTitles.Count == 0)
            {
                MessageBox.Show("No visible sections are available to print.", "Character Sheets", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var printOptions = new PrintLayoutOptions(
                BuildDefaultPrintLayoutEntries(sectionTitles),
                GetPreferredPrintTheme(_selectedCharacter),
                GetKeepSpellsGroupedByLevelPreference(_selectedCharacter));

            var (previewWidth, previewHeight) = GetDefaultLetterPreviewArea();
            var previewDocument = BuildPrintableVisualDocument(LaneSurface, previewWidth, previewHeight, printOptions);
            if (previewDocument is null)
            {
                MessageBox.Show("Could not generate print preview from the current sheet layout.", "Character Sheets", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var previewMap = BuildLivePageMap(LaneSurface, previewWidth, previewHeight, printOptions);

            var previewChoice = ShowPrintPreviewDialog(previewDocument, previewMap);
            if (previewChoice != PrintPreviewChoice.Print)
                return;

            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true)
                return;

            var printable = BuildPrintableVisualDocument(LaneSurface, dlg.PrintableAreaWidth, dlg.PrintableAreaHeight, printOptions);
            if (printable is null)
            {
                MessageBox.Show("Could not generate a printable document from the current sheet layout.", "Character Sheets", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            dlg.PrintDocument(((IDocumentPaginatorSource)printable).DocumentPaginator, $"Character Sheet - {_selectedCharacter.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not print character sheet.\n\n{ex.Message}", "Character Sheets", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private List<string> GetPrintableSectionTitlesInLayoutOrder()
    {
        return _sectionCards
            .Where(card => card.Expander.Visibility == Visibility.Visible && card.Expander.IsExpanded)
            .OrderBy(card => _sectionLayoutByKey.TryGetValue(card.Key, out var placement) ? placement.Order : int.MaxValue)
            .ThenBy(card => _sectionLayoutByKey.TryGetValue(card.Key, out var placement) ? placement.Lane : int.MaxValue)
            .Select(card => card.Title)
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .ToList();
    }

    private PrintLayoutOptions? ShowPrintSetupDialog(CharacterSheet character, List<string> sectionTitles, PrintLayoutOptions? initialOptions = null)
    {
        if (sectionTitles.Count == 0)
            return new PrintLayoutOptions(
                new List<PrintLayoutEntry>(),
                PrintDocumentTheme.Color,
                GetKeepSpellsGroupedByLevelPreference(character));

        string profileKey = BuildPrintProfileKey(character);
        var selectedEntries = initialOptions is not null
            ? ClonePrintLayoutEntries(initialOptions.CloneEntries())
            : PrintLayoutPresetsByProfile.TryGetValue(profileKey, out var saved)
                ? saved.CloneEntriesFor(sectionTitles)
                : BuildDefaultPrintLayoutEntries(sectionTitles);
        var selectedTheme = initialOptions?.Theme
            ?? (PrintLayoutPresetsByProfile.TryGetValue(profileKey, out var presetTheme) ? presetTheme.Theme : PrintDocumentTheme.Color);
        bool keepSpellsGroupedByLevel = initialOptions?.KeepSpellsGroupedByLevel
            ?? (PrintLayoutPresetsByProfile.TryGetValue(profileKey, out var presetGrouping) ? presetGrouping.KeepSpellsGroupedByLevel : GetKeepSpellsGroupedByLevelPreference(character));
        var availableTitles = BuildAvailablePrintSectionTitles(sectionTitles, selectedEntries);

        var window = new Window
        {
            Title = "Print Setup",
            Width = 880,
            Height = 660,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.CanResize,
            MinWidth = 760,
            MinHeight = 540,
            Background = (Brush)FindResource("BrushPanel"),
            Foreground = (Brush)FindResource("BrushText"),
        };

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var intro = new TextBlock
        {
            Text = "Available sections are on the left. Selected print order is on the right. Use arrows or double-click to move items, and insert page-break markers between selected sections.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(intro, 0);
        root.Children.Add(intro);

        var appearancePanel = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        appearancePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        appearancePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        appearancePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        appearancePanel.Children.Add(new TextBlock
        {
            Text = "Print style:",
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BrushTitle"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });

        var styleCombo = new ComboBox
        {
            Width = 160,
            ItemsSource = new[] { "Color (gold/brown)", "Black & White" },
            SelectedIndex = selectedTheme == PrintDocumentTheme.Color ? 0 : 1,
            Margin = new Thickness(0, 0, 0, 0),
        };
        Grid.SetColumn(styleCombo, 1);
        appearancePanel.Children.Add(styleCombo);
        Grid.SetRow(appearancePanel, 0);
        root.Children.Add(appearancePanel);

        var editorGrid = new Grid();
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        editorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(editorGrid, 1);

        var selectedHost = new Grid();
        selectedHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        selectedHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        selectedHost.Children.Add(new TextBlock
        {
            Text = "Selected (Print Order)",
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BrushTitle"),
            Margin = new Thickness(0, 0, 0, 8),
        });

        var selectedList = new ListBox
        {
            MinHeight = 280,
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("BrushBorder2"),
            Background = (Brush)FindResource("BrushCard"),
            Foreground = (Brush)FindResource("BrushText"),
        };
        Grid.SetRow(selectedList, 1);
        selectedHost.Children.Add(selectedList);
        Grid.SetColumn(selectedHost, 0);
        editorGrid.Children.Add(selectedHost);

        var middleButtons = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var addButton = new Button
        {
            Content = "Add >>",
            Width = 130,
            Margin = new Thickness(0, 0, 0, 8),
            Style = (Style)FindResource("DarkButton"),
        };
        var removeButton = new Button
        {
            Content = "<< Remove",
            Width = 130,
            Margin = new Thickness(0, 0, 0, 14),
            Style = (Style)FindResource("DarkButton"),
        };
        var pageBreakButton = new Button
        {
            Content = "Insert Page Break",
            Width = 130,
            Margin = new Thickness(0, 0, 0, 14),
            Style = (Style)FindResource("DarkButton"),
        };
        var moveUpButton = new Button
        {
            Content = "Move Up",
            Width = 130,
            Margin = new Thickness(0, 0, 0, 8),
            Style = (Style)FindResource("DarkButton"),
        };
        var moveDownButton = new Button
        {
            Content = "Move Down",
            Width = 130,
            Style = (Style)FindResource("DarkButton"),
        };

        middleButtons.Children.Add(addButton);
        middleButtons.Children.Add(removeButton);
        middleButtons.Children.Add(pageBreakButton);
        middleButtons.Children.Add(moveUpButton);
        middleButtons.Children.Add(moveDownButton);
        Grid.SetColumn(middleButtons, 1);
        editorGrid.Children.Add(middleButtons);

        var availableHost = new Grid();
        availableHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        availableHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        availableHost.Children.Add(new TextBlock
        {
            Text = "Available Sections",
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BrushTitle"),
            Margin = new Thickness(0, 0, 0, 8),
        });

        var availableList = new ListBox
        {
            MinHeight = 280,
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("BrushBorder2"),
            Background = (Brush)FindResource("BrushCard"),
            Foreground = (Brush)FindResource("BrushText"),
        };
        Grid.SetRow(availableList, 1);
        availableHost.Children.Add(availableList);
        Grid.SetColumn(availableHost, 0);
        editorGrid.Children.Add(availableHost);

        Grid.SetColumn(selectedHost, 2);
        editorGrid.Children.Remove(selectedHost);
        editorGrid.Children.Add(selectedHost);

        root.Children.Add(editorGrid);

        void RefreshLists(int? selectedIndex = null)
        {
            availableTitles = BuildAvailablePrintSectionTitles(sectionTitles, selectedEntries);
            var selectedLabels = selectedEntries.Select(FormatPrintLayoutEntryLabel).ToList();
            selectedList.ItemsSource = selectedLabels;
            availableList.ItemsSource = availableTitles;

            if (selectedLabels.Count == 0)
            {
                selectedList.SelectedIndex = -1;
                return;
            }

            if (selectedIndex is null)
                return;

            int clamped = Math.Max(0, Math.Min(selectedLabels.Count - 1, selectedIndex.Value));
            selectedList.SelectedIndex = clamped;
        }

        void AddFromAvailableSelection()
        {
            if (availableList.SelectedItem is not string title)
                return;

            int insertAt = selectedList.SelectedIndex >= 0 ? selectedList.SelectedIndex + 1 : selectedEntries.Count;
            selectedEntries.Insert(insertAt, PrintLayoutEntry.Section(title));
            RefreshLists(insertAt);
        }

        void RemoveFromSelectedSelection()
        {
            int selectedIndex = selectedList.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= selectedEntries.Count)
                return;

            selectedEntries.RemoveAt(selectedIndex);

            SanitizePageBreakMarkersInPlace(selectedEntries);
            RefreshLists(Math.Max(0, selectedIndex - 1));
        }

        void InsertPageBreakMarker()
        {
            if (selectedEntries.Count == 0)
                return;

            int insertAt = selectedList.SelectedIndex >= 0 ? selectedList.SelectedIndex + 1 : selectedEntries.Count;
            selectedEntries.Insert(insertAt, PrintLayoutEntry.PageBreak());
            SanitizePageBreakMarkersInPlace(selectedEntries);
            RefreshLists(insertAt);
        }

        void MoveSelected(int delta)
        {
            int index = selectedList.SelectedIndex;
            if (index < 0 || index >= selectedEntries.Count)
                return;

            int target = index + delta;
            if (target < 0 || target >= selectedEntries.Count)
                return;

            var item = selectedEntries[index];
            selectedEntries.RemoveAt(index);
            selectedEntries.Insert(target, item);
            SanitizePageBreakMarkersInPlace(selectedEntries);
            RefreshLists(target);
        }

        addButton.Click += (_, _) => AddFromAvailableSelection();
        removeButton.Click += (_, _) => RemoveFromSelectedSelection();
        pageBreakButton.Click += (_, _) => InsertPageBreakMarker();
        moveUpButton.Click += (_, _) => MoveSelected(-1);
        moveDownButton.Click += (_, _) => MoveSelected(1);

        availableList.MouseDoubleClick += (_, _) => AddFromAvailableSelection();
        selectedList.MouseDoubleClick += (_, _) => RemoveFromSelectedSelection();

        RefreshLists();

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        Grid.SetRow(buttonRow, 2);

        var savePresetButton = new Button
        {
            Content = "Save Preset",
            Width = 110,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("DarkButton"),
        };
        var loadPresetButton = new Button
        {
            Content = "Load Preset",
            Width = 110,
            Margin = new Thickness(0, 0, 16, 0),
            Style = (Style)FindResource("DarkButton"),
        };

        savePresetButton.Click += (_, _) =>
        {
            PrintLayoutPresetsByProfile[profileKey] = new PrintLayoutOptions(
                ClonePrintLayoutEntries(selectedEntries),
                GetSelectedPrintTheme(),
                keepSpellsGroupedByLevel);
            MessageBox.Show("Preset saved for this character/class profile.", "Print Setup", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        loadPresetButton.Click += (_, _) =>
        {
            if (!PrintLayoutPresetsByProfile.TryGetValue(profileKey, out var preset))
            {
                MessageBox.Show("No saved preset found for this character/class profile.", "Print Setup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            selectedEntries = preset.CloneEntriesFor(sectionTitles);
            styleCombo.SelectedIndex = preset.Theme == PrintDocumentTheme.Color ? 0 : 1;
            keepSpellsGroupedByLevel = preset.KeepSpellsGroupedByLevel;
            RefreshLists();
        };

        buttonRow.Children.Add(savePresetButton);
        buttonRow.Children.Add(loadPresetButton);

        var printButton = new Button
        {
            Content = "Print",
            Width = 100,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("GoldButton"),
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 100,
            IsCancel = true,
            Style = (Style)FindResource("DarkButton"),
        };

        printButton.Click += (_, _) => window.DialogResult = true;
        cancelButton.Click += (_, _) => window.DialogResult = false;
        buttonRow.Children.Add(printButton);
        buttonRow.Children.Add(cancelButton);
        root.Children.Add(buttonRow);

        window.Content = root;
        bool? accepted = window.ShowDialog();
        if (accepted != true)
            return null;

        SanitizePageBreakMarkersInPlace(selectedEntries);

        if (!selectedEntries.Any(entry => entry.Kind == PrintLayoutEntryKind.Section))
        {
            MessageBox.Show(
                "Select at least one section to print.",
                "Print Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }

        return new PrintLayoutOptions(
            ClonePrintLayoutEntries(selectedEntries),
            GetSelectedPrintTheme(),
            keepSpellsGroupedByLevel);

        PrintDocumentTheme GetSelectedPrintTheme()
        {
            return styleCombo.SelectedIndex == 0 ? PrintDocumentTheme.Color : PrintDocumentTheme.BlackAndWhite;
        }
    }

    private static string BuildPrintProfileKey(CharacterSheet character)
    {
        string classKey = character.ClassIds is { Count: > 0 }
            ? string.Join("+", character.ClassIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            : character.ClassId ?? string.Empty;

        return $"{character.Name}|{classKey}|{character.CharacterMode}";
    }

    private static bool GetKeepSpellsGroupedByLevelPreference(CharacterSheet character)
    {
        string profileKey = BuildPrintProfileKey(character);
        return SpellPrintGroupingByProfile.TryGetValue(profileKey, out bool keepGrouped)
            ? keepGrouped
            : true;
    }

    private static PrintDocumentTheme GetPreferredPrintTheme(CharacterSheet character)
    {
        string profileKey = BuildPrintProfileKey(character);
        if (PrintThemeByProfile.TryGetValue(profileKey, out var explicitTheme))
            return explicitTheme;

        return PrintLayoutPresetsByProfile.TryGetValue(profileKey, out var preset)
            ? preset.Theme
            : PrintDocumentTheme.BlackAndWhite;
    }

    private void SetPreferredPrintTheme(CharacterSheet character, PrintDocumentTheme theme)
    {
        string profileKey = BuildPrintProfileKey(character);
        PrintThemeByProfile[profileKey] = theme;

        if (PrintLayoutPresetsByProfile.TryGetValue(profileKey, out var existingPreset))
        {
            PrintLayoutPresetsByProfile[profileKey] = new PrintLayoutOptions(
                existingPreset.CloneEntries(),
                theme,
                existingPreset.KeepSpellsGroupedByLevel);
        }
    }

    private void SetKeepSpellsGroupedByLevelPreference(CharacterSheet character, bool keepGrouped)
    {
        string profileKey = BuildPrintProfileKey(character);
        SpellPrintGroupingByProfile[profileKey] = keepGrouped;

        if (PrintLayoutPresetsByProfile.TryGetValue(profileKey, out var existingPreset))
        {
            PrintLayoutPresetsByProfile[profileKey] = new PrintLayoutOptions(
                existingPreset.CloneEntries(),
                existingPreset.Theme,
                keepGrouped);
        }
    }

    private static List<PrintLayoutEntry> BuildDefaultPrintLayoutEntries(IEnumerable<string> titles)
    {
        var entries = new List<PrintLayoutEntry>();
        int index = 0;
        foreach (string title in titles)
        {
            if (index > 0 && string.Equals(title, "SPELLS", StringComparison.OrdinalIgnoreCase))
                entries.Add(PrintLayoutEntry.PageBreak());

            entries.Add(PrintLayoutEntry.Section(title));
            index++;
        }

        return entries;
    }

    private static List<PrintLayoutEntry> ClonePrintLayoutEntries(IEnumerable<PrintLayoutEntry> entries)
    {
        return entries.Select(entry => new PrintLayoutEntry
        {
            Kind = entry.Kind,
            Title = entry.Title,
        }).ToList();
    }

    private static List<string> BuildAvailablePrintSectionTitles(List<string> allTitles, List<PrintLayoutEntry> selectedEntries)
    {
        var selected = selectedEntries
            .Where(entry => entry.Kind == PrintLayoutEntryKind.Section)
            .Select(entry => entry.Title)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return allTitles
            .Where(title => !selected.Contains(title))
            .ToList();
    }

    private static string FormatPrintLayoutEntryLabel(PrintLayoutEntry entry)
    {
        return entry.Kind == PrintLayoutEntryKind.PageBreak
            ? "--- Page Break ---"
            : entry.Title;
    }

    private static void SanitizePageBreakMarkersInPlace(List<PrintLayoutEntry> entries)
    {
        // Remove duplicate adjacent page breaks first.
        for (int i = entries.Count - 1; i > 0; i--)
        {
            if (entries[i].Kind == PrintLayoutEntryKind.PageBreak && entries[i - 1].Kind == PrintLayoutEntryKind.PageBreak)
                entries.RemoveAt(i);
        }

        // Trim leading/trailing page breaks so markers stay between sections.
        while (entries.Count > 0 && entries[0].Kind == PrintLayoutEntryKind.PageBreak)
            entries.RemoveAt(0);

        while (entries.Count > 0 && entries[^1].Kind == PrintLayoutEntryKind.PageBreak)
            entries.RemoveAt(entries.Count - 1);

        // If sections were removed, collapse any newly adjacent marker pairs.
        for (int i = entries.Count - 1; i > 0; i--)
        {
            if (entries[i].Kind == PrintLayoutEntryKind.PageBreak && entries[i - 1].Kind == PrintLayoutEntryKind.PageBreak)
                entries.RemoveAt(i);
        }
    }

    private sealed class PrintSectionPlanItem
    {
        public string Title { get; init; } = string.Empty;
        public bool BreakBefore { get; init; }
    }

    private sealed class PrintLayoutEntry
    {
        public PrintLayoutEntryKind Kind { get; init; }
        public string Title { get; init; } = string.Empty;

        public static PrintLayoutEntry Section(string title) => new()
        {
            Kind = PrintLayoutEntryKind.Section,
            Title = title,
        };

        public static PrintLayoutEntry PageBreak() => new()
        {
            Kind = PrintLayoutEntryKind.PageBreak,
            Title = string.Empty,
        };
    }

    private enum PrintLayoutEntryKind
    {
        Section,
        PageBreak,
    }

    private sealed class PrintSectionOptionState
    {
        public bool Include { get; init; }
    }

    private enum PrintDocumentTheme
    {
        Color,
        BlackAndWhite,
    }

    private sealed class PrintLayoutOptions
    {
        private List<PrintLayoutEntry> SelectedEntries { get; }
        private HashSet<string> IncludedSectionTitles { get; }
        public PrintDocumentTheme Theme { get; }
        public bool KeepSpellsGroupedByLevel { get; }

        public PrintLayoutOptions(List<PrintLayoutEntry> selectedEntries, PrintDocumentTheme theme, bool keepSpellsGroupedByLevel = true)
        {
            SelectedEntries = selectedEntries;
            Theme = theme;
            KeepSpellsGroupedByLevel = keepSpellsGroupedByLevel;
            IncludedSectionTitles = selectedEntries
                .Where(entry => entry.Kind == PrintLayoutEntryKind.Section)
                .Select(entry => entry.Title)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public List<PrintLayoutEntry> CloneEntriesFor(IEnumerable<string> sectionTitles)
        {
            var validTitles = sectionTitles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var clone = new List<PrintLayoutEntry>();
            var usedSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in SelectedEntries)
            {
                if (entry.Kind == PrintLayoutEntryKind.PageBreak)
                {
                    clone.Add(PrintLayoutEntry.PageBreak());
                    continue;
                }

                if (!validTitles.Contains(entry.Title))
                    continue;
                if (!usedSections.Add(entry.Title))
                    continue;

                clone.Add(PrintLayoutEntry.Section(entry.Title));
            }

            SanitizePageBreakMarkersInPlace(clone);
            return clone;
        }

        public List<PrintLayoutEntry> CloneEntries()
        {
            return ClonePrintLayoutEntries(SelectedEntries);
        }

        public IReadOnlyList<string> GetOrderedSectionTitles()
        {
            return SelectedEntries
                .Where(entry => entry.Kind == PrintLayoutEntryKind.Section)
                .Select(entry => entry.Title)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public IReadOnlyList<PrintSectionPlanItem> GetSectionPlan()
        {
            var plan = new List<PrintSectionPlanItem>();
            bool breakBeforeNext = false;

            foreach (var entry in SelectedEntries)
            {
                if (entry.Kind == PrintLayoutEntryKind.PageBreak)
                {
                    breakBeforeNext = true;
                    continue;
                }

                plan.Add(new PrintSectionPlanItem
                {
                    Title = entry.Title,
                    BreakBefore = breakBeforeNext,
                });

                breakBeforeNext = false;
            }

            return plan;
        }

        public PrintSectionOptionState GetOptionsFor(string sectionTitle)
        {
            return new PrintSectionOptionState { Include = IncludedSectionTitles.Contains(sectionTitle) };
        }
    }

    private enum PrintPreviewChoice
    {
        Print,
        GoBack,
        Cancel,
    }

    private PrintPreviewChoice ShowPrintPreviewDialog(IDocumentPaginatorSource document, List<string> pageMapLines)
    {
        var window = new Window
        {
            Title = "Print Preview",
            Width = 1160,
            Height = 760,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            MinWidth = 980,
            MinHeight = 620,
            Background = (Brush)FindResource("BrushPanel"),
            Foreground = (Brush)FindResource("BrushText"),
        };

        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var hint = new TextBlock
        {
            Text = "Preview mirrors the current sheet layout. Use Print to open printer dialog.",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Margin = new Thickness(0, 0, 12, 0),
        };
        DockPanel.SetDock(hint, Dock.Left);
        toolbar.Children.Add(hint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var printButton = new Button
        {
            Content = "Print...",
            Width = 100,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Style = (Style)FindResource("GoldButton"),
        };
        var goBackButton = new Button
        {
            Content = "Go Back",
            Width = 100,
            Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)FindResource("DarkButton"),
        };
        var closeButton = new Button
        {
            Content = "Close",
            Width = 100,
            IsCancel = true,
            Style = (Style)FindResource("DarkButton"),
        };

        printButton.Click += (_, _) => window.DialogResult = true;
        goBackButton.Click += (_, _) =>
        {
            window.Tag = PrintPreviewChoice.GoBack;
            window.Close();
        };
        closeButton.Click += (_, _) =>
        {
            window.Tag = PrintPreviewChoice.Cancel;
            window.Close();
        };
        buttons.Children.Add(printButton);
        buttons.Children.Add(goBackButton);
        buttons.Children.Add(closeButton);
        DockPanel.SetDock(buttons, Dock.Right);
        toolbar.Children.Add(buttons);

        Grid.SetRow(toolbar, 0);
        Grid.SetColumnSpan(toolbar, 2);
        root.Children.Add(toolbar);

        var viewer = new DocumentViewer
        {
            Document = document,
            Background = Brushes.White,
            Foreground = Brushes.Black,
        };
        Grid.SetRow(viewer, 1);
        Grid.SetColumn(viewer, 0);
        root.Children.Add(viewer);

        var pageMapHost = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("BrushBorder2"),
            Background = (Brush)FindResource("BrushCard"),
            Padding = new Thickness(10),
            Margin = new Thickness(10, 0, 0, 0),
        };

        var pageMapPanel = new StackPanel();
        pageMapPanel.Children.Add(new TextBlock
        {
            Text = "Page Map",
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BrushTitle"),
            Margin = new Thickness(0, 0, 0, 8),
        });

        var pageMapList = new ListBox
        {
            ItemsSource = pageMapLines,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("BrushText"),
        };
        pageMapPanel.Children.Add(pageMapList);
        pageMapHost.Child = pageMapPanel;

        Grid.SetRow(pageMapHost, 1);
        Grid.SetColumn(pageMapHost, 1);
        root.Children.Add(pageMapHost);

        window.Content = root;
        bool? accepted = window.ShowDialog();
        if (accepted == true)
            return PrintPreviewChoice.Print;

        if (window.Tag is PrintPreviewChoice choice)
            return choice;

        return PrintPreviewChoice.Cancel;
    }

    private static (double width, double height) GetDefaultLetterPreviewArea()
    {
        // 8.5 x 11 at 96 DPI = 816 x 1056 DIPs; reserve half-inch margins.
        return (720, 960);
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

    private static IEnumerable<Expander> GetDescendantExpanders(DependencyObject root)
    {
        var result = new List<Expander>();
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is Expander expander)
                result.Add(expander);

            int childCount = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < childCount; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }

        return result;
    }

    private static ScrollViewer? GetAncestorScrollViewer(DependencyObject element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer)
                return scrollViewer;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static FrameworkElement? CreateDetachedPrintSurface(FrameworkElement source)
    {
        try
        {
            string xaml = XamlWriter.Save(source);
            return XamlReader.Parse(xaml) as FrameworkElement;
        }
        catch
        {
            return null;
        }
    }

    private sealed class VisualPrintSection
    {
        public string Title { get; init; } = string.Empty;
        public double Top { get; init; }
        public double Height { get; init; }
        public bool ForceNewPageBefore { get; init; }
        public bool ForceNewPageAfter { get; init; }
    }

    private static IDocumentPaginatorSource? BuildPrintableVisualDocument(FrameworkElement source, double printableWidth, double printableHeight, PrintLayoutOptions options)
    {
        FrameworkElement printSource = CreateDetachedPrintSurface(source) ?? source;

        if (options.Theme == PrintDocumentTheme.BlackAndWhite)
            ApplyMonochromePrintTheme(printSource);

        printSource.UpdateLayout();

        if (printableWidth <= 0 || printableHeight <= 0)
            return null;

        const double pageInset = 16;
        double contentWidth = Math.Max(1, printableWidth - (pageInset * 2));
        double contentHeight = Math.Max(1, printableHeight - (pageInset * 2));

        var panel = printSource as Panel;
        var sections = GetDescendantExpanders(printSource).ToList();
        bool canReorderInPanel = panel is not null
            && sections.Count > 0
            && panel.Children.OfType<Expander>().Count() == sections.Count;

        var originalExpansion = sections.Select(section => section.IsExpanded).ToList();
        var originalVisibility = sections.Select(section => section.Visibility).ToList();
        List<UIElement>? originalChildOrder = null;
        var spellWrapSwaps = new List<(StackPanel Parent, int Index, WrapPanel Original, StackPanel Replacement)>();

        try
        {
            foreach (var section in sections)
            {
                string title = (section.Header as TextBlock)?.Text?.Trim() ?? string.Empty;
                var sectionOptions = options.GetOptionsFor(title);
                section.Visibility = sectionOptions.Include ? Visibility.Visible : Visibility.Collapsed;
                section.IsExpanded = true;
            }

            if (canReorderInPanel && panel is not null)
                originalChildOrder = ApplySectionOrder(panel, sections, options);

            printSource.UpdateLayout();

            // Print spells in a single vertical column so rows don't get squeezed by WrapPanel.
            spellWrapSwaps = SwapSpellWrapPanelsToStackPanels(sections);
            if (spellWrapSwaps.Count > 0)
                printSource.UpdateLayout();

            double sourceWidth = printSource.ActualWidth;
            double sourceHeight = printSource.ActualHeight;
            if (sourceWidth <= 0 || sourceHeight <= 0)
                return null;

            double scale = contentWidth / sourceWidth;
            if (scale <= 0)
                return null;

            double sourceHeightPerPage = Math.Max(1, Math.Floor(contentHeight / scale));
            var printSections = BuildPrintSections(printSource, sections, sourceHeight, options);
            var pageSlices = BuildLogicalPageSlices(printSections, sourceHeightPerPage);
            if (pageSlices.Count == 0)
                return null;

            var document = new FixedDocument();
            foreach (var (sliceStart, sliceHeight) in pageSlices)
            {
                double snappedStart = Math.Max(0, Math.Round(sliceStart));
                double snappedHeight = Math.Max(1, Math.Ceiling(sliceHeight));
                double drawHeight = Math.Min(contentHeight, Math.Max(1, snappedHeight * scale));

                var page = new FixedPage
                {
                    Width = printableWidth,
                    Height = printableHeight,
                    Background = Brushes.White,
                    ClipToBounds = true,
                };

                var brush = new VisualBrush(printSource)
                {
                    Viewbox = new Rect(0, snappedStart, sourceWidth, snappedHeight),
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, contentWidth, drawHeight),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Stretch = Stretch.Fill,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                };

                var content = new Rectangle
                {
                    Width = contentWidth,
                    Height = drawHeight,
                    Fill = brush,
                    SnapsToDevicePixels = true,
                    Clip = new RectangleGeometry(new Rect(0, 0, contentWidth, drawHeight)),
                };

                FixedPage.SetLeft(content, pageInset);
                FixedPage.SetTop(content, pageInset);
                page.Children.Add(content);

                var pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(page);
                document.Pages.Add(pageContent);
            }

            return document;
        }
        finally
        {
            if (canReorderInPanel && panel is not null && originalChildOrder is not null)
                RestorePanelChildren(panel, originalChildOrder);

            RestoreSpellWrapPanels(spellWrapSwaps);

            for (int i = 0; i < sections.Count && i < originalExpansion.Count && i < originalVisibility.Count; i++)
            {
                sections[i].IsExpanded = originalExpansion[i];
                sections[i].Visibility = originalVisibility[i];
            }

            printSource.UpdateLayout();
        }
    }

    private static List<string> BuildLivePageMap(FrameworkElement source, double printableWidth, double printableHeight, PrintLayoutOptions options)
    {
        FrameworkElement printSource = CreateDetachedPrintSurface(source) ?? source;

        if (options.Theme == PrintDocumentTheme.BlackAndWhite)
            ApplyMonochromePrintTheme(printSource);

        printSource.UpdateLayout();

        if (printableWidth <= 0 || printableHeight <= 0)
            return new List<string> { "Unable to compute page map." };

        const double pageInset = 16;
        double contentWidth = Math.Max(1, printableWidth - (pageInset * 2));
        double contentHeight = Math.Max(1, printableHeight - (pageInset * 2));

        var panel = printSource as Panel;
        var sections = GetDescendantExpanders(printSource).ToList();
        bool canReorderInPanel = panel is not null
            && sections.Count > 0
            && panel.Children.OfType<Expander>().Count() == sections.Count;
        var originalExpansion = sections.Select(section => section.IsExpanded).ToList();
        var originalVisibility = sections.Select(section => section.Visibility).ToList();
        List<UIElement>? originalChildOrder = null;
        var spellWrapSwaps = new List<(StackPanel Parent, int Index, WrapPanel Original, StackPanel Replacement)>();

        try
        {
            foreach (var section in sections)
            {
                string title = (section.Header as TextBlock)?.Text?.Trim() ?? string.Empty;
                var sectionOptions = options.GetOptionsFor(title);
                section.Visibility = sectionOptions.Include ? Visibility.Visible : Visibility.Collapsed;
                section.IsExpanded = true;
            }

            if (canReorderInPanel && panel is not null)
                originalChildOrder = ApplySectionOrder(panel, sections, options);

            printSource.UpdateLayout();

            spellWrapSwaps = SwapSpellWrapPanelsToStackPanels(sections);
            if (spellWrapSwaps.Count > 0)
                printSource.UpdateLayout();

            double sourceWidth = printSource.ActualWidth;
            double sourceHeight = printSource.ActualHeight;
            if (sourceWidth <= 0 || sourceHeight <= 0)
                return new List<string> { "Unable to compute page map." };

            double scale = contentWidth / sourceWidth;
            if (scale <= 0)
                return new List<string> { "Unable to compute page map." };

            double sourceHeightPerPage = Math.Max(1, Math.Floor(contentHeight / scale));
            var printSections = BuildPrintSections(printSource, sections, sourceHeight, options);
            var pageSlices = BuildLogicalPageSlices(printSections, sourceHeightPerPage);
            if (pageSlices.Count == 0)
                return new List<string> { "No printable sections selected." };

            var lines = new List<string>();
            for (int i = 0; i < pageSlices.Count; i++)
            {
                var (start, height) = pageSlices[i];
                double end = start + height;
                var titles = printSections
                    .Where(section => section.Top < end && section.Top + section.Height > start)
                    .Select(section => section.Title)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string text = titles.Count == 0 ? "(blank)" : string.Join("  |  ", titles);
                lines.Add($"Page {i + 1}");
                lines.Add($"  Start: {start:0.#}");
                lines.Add($"  End:   {end:0.#}");
                lines.Add($"  Sections: {text}");

                if (i < pageSlices.Count - 1)
                    lines.Add("  --- page break ---");
            }

            return lines;
        }
        finally
        {
            if (canReorderInPanel && panel is not null && originalChildOrder is not null)
                RestorePanelChildren(panel, originalChildOrder);

            RestoreSpellWrapPanels(spellWrapSwaps);

            for (int i = 0; i < sections.Count && i < originalExpansion.Count && i < originalVisibility.Count; i++)
            {
                sections[i].IsExpanded = originalExpansion[i];
                sections[i].Visibility = originalVisibility[i];
            }

            printSource.UpdateLayout();
        }
    }

    private static List<UIElement>? ApplySectionOrder(Panel panel, List<Expander> sections, PrintLayoutOptions options)
    {
        if (sections.Count == 0)
            return null;

        var sectionByTitle = sections
            .Select(section => new
            {
                Title = (section.Header as TextBlock)?.Text?.Trim() ?? string.Empty,
                Section = section,
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToDictionary(item => item.Title, item => item.Section, StringComparer.OrdinalIgnoreCase);

        var ordered = new List<UIElement>();
        foreach (var title in options.GetOrderedSectionTitles())
        {
            if (sectionByTitle.TryGetValue(title, out var section))
                ordered.Add(section);
        }

        foreach (var section in sections)
        {
            if (!ordered.Contains(section))
                ordered.Add(section);
        }

        var original = panel.Children.Cast<UIElement>().ToList();
        panel.Children.Clear();
        foreach (var child in ordered)
            panel.Children.Add(child);

        return original;
    }

    private static void RestorePanelChildren(Panel panel, List<UIElement> originalChildren)
    {
        panel.Children.Clear();
        foreach (var child in originalChildren)
            panel.Children.Add(child);
    }

    private static List<(StackPanel Parent, int Index, WrapPanel Original, StackPanel Replacement)> SwapSpellWrapPanelsToStackPanels(List<Expander> sections)
    {
        var swaps = new List<(StackPanel Parent, int Index, WrapPanel Original, StackPanel Replacement)>();
        foreach (var section in sections)
        {
            if (!string.Equals((section.Header as TextBlock)?.Text?.Trim(), "SPELLS", StringComparison.OrdinalIgnoreCase))
                continue;
            if (section.Content is not StackPanel spellContent)
                continue;

            for (int i = spellContent.Children.Count - 1; i >= 0; i--)
            {
                if (spellContent.Children[i] is not WrapPanel wrapPanel)
                    continue;

                var singleColumn = new StackPanel { Margin = wrapPanel.Margin };
                var wrappedChildren = wrapPanel.Children.Cast<UIElement>().ToList();
                wrapPanel.Children.Clear();
                foreach (var child in wrappedChildren)
                    singleColumn.Children.Add(child);

                spellContent.Children.RemoveAt(i);
                spellContent.Children.Insert(i, singleColumn);
                swaps.Add((spellContent, i, wrapPanel, singleColumn));
            }
        }

        return swaps;
    }

    private static void RestoreSpellWrapPanels(List<(StackPanel Parent, int Index, WrapPanel Original, StackPanel Replacement)> swaps)
    {
        for (int i = swaps.Count - 1; i >= 0; i--)
        {
            var (parent, index, original, replacement) = swaps[i];
            var children = replacement.Children.Cast<UIElement>().ToList();
            replacement.Children.Clear();
            foreach (var child in children)
                original.Children.Add(child);

            int replacementIndex = parent.Children.IndexOf(replacement);
            if (replacementIndex >= 0)
                parent.Children.RemoveAt(replacementIndex);

            int insertIndex = Math.Clamp(index, 0, parent.Children.Count);
            parent.Children.Insert(insertIndex, original);
        }
    }

    private static List<VisualPrintSection> BuildPrintSections(FrameworkElement source, List<Expander> sections, double sourceHeight, PrintLayoutOptions options)
    {
        var sectionByTitle = sections
            .Select(section => new
            {
                Title = (section.Header as TextBlock)?.Text?.Trim() ?? string.Empty,
                Section = section,
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToDictionary(item => item.Title, item => item.Section, StringComparer.OrdinalIgnoreCase);

        var regions = new List<VisualPrintSection>();

        foreach (var planItem in options.GetSectionPlan())
        {
            if (!sectionByTitle.TryGetValue(planItem.Title, out var expander))
                continue;
            if (expander.Visibility != Visibility.Visible)
                continue;

            if (string.Equals(planItem.Title, "SPELLS", StringComparison.OrdinalIgnoreCase)
                && options.KeepSpellsGroupedByLevel
                && expander.Content is StackPanel spellContent)
            {
                var spellCards = new List<Border>();

                foreach (var child in spellContent.Children)
                {
                    if (child is WrapPanel wrap)
                    {
                        spellCards.AddRange(wrap.Children.OfType<Border>());
                        continue;
                    }

                    if (child is StackPanel stack)
                    {
                        spellCards.AddRange(stack.Children.OfType<Border>());
                        continue;
                    }

                    if (child is Border border)
                        spellCards.Add(border);
                }

                if (spellCards.Count > 0)
                {
                    int targetBreakCardIndex = 0;
                    if (planItem.BreakBefore)
                    {
                        int firstNonBonus = spellCards.FindIndex(card => !IsClassBonusSpellCard(card));
                        targetBreakCardIndex = firstNonBonus >= 0 ? firstNonBonus : 0;
                    }

                    bool addedAnyCardRegions = false;
                    double? pendingBonusTop = null;
                    double pendingBonusBottom = 0;
                    bool pendingBonusBreak = false;

                    for (int cardIndex = 0; cardIndex < spellCards.Count; cardIndex++)
                    {
                        var spellCard = spellCards[cardIndex];
                        if (spellCard.Visibility != Visibility.Visible)
                            continue;

                        bool forceBreakBeforeCard = planItem.BreakBefore && cardIndex == targetBreakCardIndex;

                        if (IsClassBonusSpellCard(spellCard))
                        {
                            if (TryGetElementBounds(source, spellCard, sourceHeight, out double bonusTop, out double bonusHeight))
                            {
                                if (!pendingBonusTop.HasValue)
                                    pendingBonusTop = bonusTop;

                                pendingBonusBottom = Math.Max(pendingBonusBottom, bonusTop + bonusHeight);
                                pendingBonusBreak = pendingBonusBreak || forceBreakBeforeCard;
                            }

                            continue;
                        }

                        bool appended = AddSpellCardRegions(
                            regions,
                            source,
                            sourceHeight,
                            spellCard,
                            planItem.Title,
                            forceBreakBeforeCard || pendingBonusBreak,
                            pendingBonusTop,
                            pendingBonusBottom);
                        addedAnyCardRegions = addedAnyCardRegions || appended;

                        if (appended)
                        {
                            pendingBonusTop = null;
                            pendingBonusBottom = 0;
                            pendingBonusBreak = false;
                        }
                    }

                    if (pendingBonusTop.HasValue)
                    {
                        regions.Add(new VisualPrintSection
                        {
                            Title = planItem.Title,
                            Top = pendingBonusTop.Value,
                            Height = Math.Max(1, pendingBonusBottom - pendingBonusTop.Value),
                            ForceNewPageBefore = pendingBonusBreak,
                            ForceNewPageAfter = false,
                        });
                        addedAnyCardRegions = true;
                    }

                    if (addedAnyCardRegions)
                        continue;
                }
            }

            if (TryGetElementBounds(source, expander, sourceHeight, out double top, out double height))
            {
                regions.Add(new VisualPrintSection
                {
                    Title = planItem.Title,
                    Top = top,
                    Height = height,
                    ForceNewPageBefore = planItem.BreakBefore,
                    ForceNewPageAfter = false,
                });
            }
        }

        return regions;
    }

    private static bool AddSpellCardRegions(
        List<VisualPrintSection> regions,
        FrameworkElement source,
        double sourceHeight,
        Border spellCard,
        string title,
        bool forceNewPageBeforeCard,
        double? mergeTop,
        double mergeBottom)
    {
        if (!TryGetElementBounds(source, spellCard, sourceHeight, out double cardTop, out double cardHeight))
            return false;

        double regionTop = mergeTop.HasValue ? Math.Min(mergeTop.Value, cardTop) : cardTop;
        double regionBottom = mergeTop.HasValue ? Math.Max(mergeBottom, cardTop + cardHeight) : cardTop + cardHeight;

        if (spellCard.Child is not StackPanel cardStack)
        {
            regions.Add(new VisualPrintSection
            {
                Title = title,
                Top = regionTop,
                Height = Math.Max(1, regionBottom - regionTop),
                ForceNewPageBefore = forceNewPageBeforeCard,
                ForceNewPageAfter = false,
            });
            return true;
        }

        var rowBorders = cardStack.Children.OfType<Border>()
            .Where(border => border.Visibility == Visibility.Visible)
            .ToList();

        if (rowBorders.Count == 0)
        {
            regions.Add(new VisualPrintSection
            {
                Title = title,
                Top = regionTop,
                Height = Math.Max(1, regionBottom - regionTop),
                ForceNewPageBefore = forceNewPageBeforeCard,
                ForceNewPageAfter = false,
            });
            return true;
        }

        if (!TryGetElementBounds(source, rowBorders[0], sourceHeight, out double firstRowTop, out double firstRowHeight))
        {
            regions.Add(new VisualPrintSection
            {
                Title = title,
                Top = regionTop,
                Height = Math.Max(1, regionBottom - regionTop),
                ForceNewPageBefore = forceNewPageBeforeCard,
                ForceNewPageAfter = false,
            });
            return true;
        }

        double firstSegmentBottom = Math.Max(firstRowTop + firstRowHeight, mergeTop.HasValue ? mergeBottom : firstRowTop + firstRowHeight);
        regions.Add(new VisualPrintSection
        {
            Title = title,
            Top = regionTop,
            Height = Math.Max(1, firstSegmentBottom - regionTop),
            ForceNewPageBefore = forceNewPageBeforeCard,
            ForceNewPageAfter = false,
        });

        for (int i = 1; i < rowBorders.Count; i++)
        {
            if (!TryGetElementBounds(source, rowBorders[i], sourceHeight, out double rowTop, out double rowHeight))
                continue;

            regions.Add(new VisualPrintSection
            {
                Title = title,
                Top = rowTop,
                Height = Math.Max(1, rowHeight),
                ForceNewPageBefore = false,
                ForceNewPageAfter = false,
            });
        }

        return true;
    }

    private static bool IsClassBonusSpellCard(Border card)
    {
        if (card.Child is not StackPanel stack)
            return false;

        var heading = stack.Children.OfType<TextBlock>().FirstOrDefault()?.Text?.Trim();
        return string.Equals(heading, "CLASS BONUS SPELLS / POWERS", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyMonochromePrintTheme(FrameworkElement root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            DependencyObject current = queue.Dequeue();

            if (current is TextBlock textBlock)
            {
                textBlock.Foreground = Brushes.Black;
                textBlock.Background = Brushes.Transparent;
            }

            if (current is Border border)
            {
                border.BorderBrush = Brushes.Black;
                border.Background = Brushes.White;
            }

            if (current is Panel panel)
            {
                panel.Background = Brushes.White;
            }

            if (current is Control control)
            {
                control.Foreground = Brushes.Black;
                control.Background = Brushes.White;
                control.BorderBrush = Brushes.Black;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < childCount; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }
    }

    private static bool TryGetElementBounds(FrameworkElement source, FrameworkElement element, double sourceHeight, out double top, out double height)
    {
        top = 0;
        height = 0;

        double elementHeight = element.ActualHeight;
        if (elementHeight <= 0)
            elementHeight = element.RenderSize.Height;
        if (elementHeight <= 0)
            return false;

        try
        {
            Point offset = element.TransformToAncestor(source).Transform(new Point(0, 0));
            top = Math.Max(0, offset.Y);
            height = Math.Max(1, Math.Min(elementHeight, Math.Max(1, sourceHeight - top)));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<(double start, double height)> BuildLogicalPageSlices(IReadOnlyList<VisualPrintSection> orderedSections, double pageHeight)
    {
        var slices = new List<(double start, double height)>();
        if (orderedSections.Count == 0)
            return slices;

        pageHeight = Math.Max(1, pageHeight);
        bool hasContent = false;
        double currentStart = 0;
        double currentEnd = 0;

        void Flush()
        {
            if (!hasContent)
                return;

            double sliceHeight = Math.Max(1, currentEnd - currentStart);
            slices.Add((currentStart, sliceHeight));
            hasContent = false;
            currentStart = 0;
            currentEnd = 0;
        }

        foreach (var section in orderedSections)
        {
            double sectionTop = Math.Max(0, section.Top);
            double sectionEnd = Math.Max(sectionTop + 1, sectionTop + Math.Max(1, section.Height));
            double sectionHeight = Math.Max(1, sectionEnd - sectionTop);

            if (section.ForceNewPageBefore && hasContent)
                Flush();

            // Never allow a single oversized section region to be squeezed into one printed page.
            // If the region exceeds one page in source-space height, split it into page-sized chunks.
            if (sectionHeight > pageHeight)
            {
                if (hasContent)
                    Flush();

                double chunkStart = sectionTop;
                while (chunkStart < sectionEnd)
                {
                    double chunkEnd = Math.Min(sectionEnd, chunkStart + pageHeight);
                    double chunkHeight = Math.Max(1, chunkEnd - chunkStart);
                    slices.Add((chunkStart, chunkHeight));
                    chunkStart = chunkEnd;
                }

                if (section.ForceNewPageAfter && hasContent)
                    Flush();

                continue;
            }

            if (!hasContent)
            {
                currentStart = sectionTop;
                currentEnd = sectionEnd;
                hasContent = true;
            }
            else
            {
                // Section bounds can occasionally arrive slightly out-of-order; expand both sides.
                double proposedStart = Math.Min(currentStart, sectionTop);
                double proposedEnd = Math.Max(currentEnd, sectionEnd);

                if (proposedEnd - proposedStart > pageHeight)
                {
                    Flush();
                    currentStart = sectionTop;
                    currentEnd = sectionEnd;
                    hasContent = true;
                }
                else
                {
                    currentStart = proposedStart;
                    currentEnd = proposedEnd;
                }
            }

            if (section.ForceNewPageAfter && hasContent)
                Flush();
        }

        Flush();
        return slices;
    }

    private static FlowDocument BuildPrintableTextDocument(CharacterSheet c, string fullSheetText, PrintLayoutOptions options, double printableWidth, double printableHeight)
    {
        string filteredSheetText = FilterSheetTextForPrint(fullSheetText, options);
        var (titleBrush, bodyBrush, accentBrush, borderBrush, panelBrush, headerBrush) = GetPrintPalette(options.Theme);

        var doc = new FlowDocument
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 10,
            PagePadding = new Thickness(22),
            PageWidth = printableWidth,
            PageHeight = printableHeight,
            ColumnWidth = printableWidth,
            Foreground = bodyBrush,
            Background = Brushes.White,
        };

        doc.Blocks.Add(new Paragraph(new Run($"Character Sheet - {c.Name}"))
        {
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = titleBrush,
            Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var section in ParseTextSections(filteredSheetText))
        {
            if (string.Equals(section.heading, "SPELLS", StringComparison.OrdinalIgnoreCase))
            {
                AddSpellTextBlocks(doc, section.lines, titleBrush, bodyBrush, borderBrush, panelBrush, headerBrush, options.KeepSpellsGroupedByLevel);
                continue;
            }

            AddSimpleTextSection(doc, section.heading, section.lines, titleBrush, bodyBrush, borderBrush, panelBrush, headerBrush);
        }

        return doc;
    }

    private static void AddSimpleTextSection(FlowDocument doc, string heading, IReadOnlyList<string> lines, Brush titleBrush, Brush bodyBrush, Brush borderBrush, Brush panelBrush, Brush headerBrush)
    {
        var sectionContainer = new BlockUIContainer
        {
            Margin = new Thickness(0, 0, 0, 8),
        };

        var box = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = borderBrush,
            Background = panelBrush,
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(2),
        };

        var sectionStack = new StackPanel();

        if (!string.IsNullOrWhiteSpace(heading))
        {
            sectionStack.Children.Add(new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = headerBrush,
                Padding = new Thickness(0, 0, 0, 3),
                Margin = new Thickness(0, 0, 0, 5),
                Child = new TextBlock
                {
                    Text = heading,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = titleBrush,
                    TextWrapping = TextWrapping.Wrap,
                },
            });
        }

        foreach (string line in lines)
        {
            sectionStack.Children.Add(new TextBlock
            {
                Text = line,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 10,
                Foreground = bodyBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 1),
            });
        }

        box.Child = sectionStack;
        sectionContainer.Child = box;
        doc.Blocks.Add(sectionContainer);
    }

    private static void AddSpellTextBlocks(FlowDocument doc, IReadOnlyList<string> lines, Brush titleBrush, Brush bodyBrush, Brush borderBrush, Brush panelBrush, Brush headerBrush, bool keepGroupedByLevel)
    {
        doc.Blocks.Add(new Paragraph(new Run("SPELLS"))
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = titleBrush,
            Margin = new Thickness(0, 0, 0, 6),
        });

        BlockUIContainer? currentLevelContainer = null;
        StackPanel? currentLevelPanel = null;
        bool firstLevelSection = true;

        void FlushLevelSection()
        {
            if (currentLevelContainer is null)
                return;

            doc.Blocks.Add(currentLevelContainer);
            currentLevelContainer = null;
            currentLevelPanel = null;
        }

        void StartLevelSection(string heading)
        {
            FlushLevelSection();

            currentLevelContainer = new BlockUIContainer
            {
                Margin = new Thickness(0, 0, 0, 8),
                BreakPageBefore = keepGroupedByLevel && !firstLevelSection,
            };
            firstLevelSection = false;

            var levelBox = new Border
            {
                Margin = new Thickness(0, 0, 0, 2),
                BorderThickness = new Thickness(1),
                BorderBrush = borderBrush,
                Background = panelBrush,
                Padding = new Thickness(8, 6, 8, 6),
                CornerRadius = new CornerRadius(2),
            };

            currentLevelPanel = new StackPanel();
            currentLevelPanel.Children.Add(new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = headerBrush,
                Padding = new Thickness(0, 0, 0, 3),
                Margin = new Thickness(0, 0, 0, 5),
                Child = new TextBlock
                {
                    Text = heading,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = titleBrush,
                    TextWrapping = TextWrapping.Wrap,
                },
            });

            levelBox.Child = currentLevelPanel;
            currentLevelContainer.Child = levelBox;
        }

        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (trimmed.Equals("Class bonus spells/powers:", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Priest slots at current level:", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Wizard spellbook:", StringComparison.OrdinalIgnoreCase))
            {
                FlushLevelSection();
                doc.Blocks.Add(new Paragraph(new Run(trimmed))
                {
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = titleBrush,
                    Margin = new Thickness(0, 2, 0, 2),
                });
                continue;
            }

            bool isLevelHeading = trimmed.StartsWith("Level ", StringComparison.OrdinalIgnoreCase)
                && (trimmed.EndsWith(" priest spells:", StringComparison.OrdinalIgnoreCase)
                    || trimmed.EndsWith(" wizard spells:", StringComparison.OrdinalIgnoreCase));

            if (isLevelHeading)
            {
                StartLevelSection(trimmed);
                continue;
            }

            if (currentLevelPanel is not null)
            {
                currentLevelPanel.Children.Add(new TextBlock
                {
                    Text = trimmed,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    FontSize = 10,
                    Foreground = bodyBrush,
                    Margin = new Thickness(0, 0, 0, 1),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
            else
            {
                doc.Blocks.Add(new Paragraph(new Run(trimmed))
                {
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    FontSize = 10,
                    Foreground = bodyBrush,
                    Margin = new Thickness(0, 0, 0, 1),
                });
            }
        }

        FlushLevelSection();
    }

    private static (Brush titleBrush, Brush bodyBrush, Brush accentBrush, Brush borderBrush, Brush panelBrush, Brush headerBrush) GetPrintPalette(PrintDocumentTheme theme)
    {
        if (theme == PrintDocumentTheme.BlackAndWhite)
        {
            return (Brushes.Black, Brushes.Black, Brushes.Black, Brushes.Black, Brushes.White, Brushes.Black);
        }

        return (
            new SolidColorBrush(Color.FromRgb(76, 49, 18)),
            Brushes.Black,
            new SolidColorBrush(Color.FromRgb(130, 94, 37)),
            new SolidColorBrush(Color.FromRgb(130, 94, 37)),
            new SolidColorBrush(Color.FromRgb(252, 248, 240)),
            new SolidColorBrush(Color.FromRgb(130, 94, 37)));
    }

    private static string FilterSheetTextForPrint(string fullSheetText, PrintLayoutOptions options)
    {
        var selectedTitles = options.GetOrderedSectionTitles().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var textSectionByVisualTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CHARACTER IDENTITY"] = "CHARACTER SHEET",
            ["ABILITY SCORES"] = "ABILITIES",
            ["COMBAT STATISTICS"] = "CORE COMBAT",
            ["WEAPONS"] = "WEAPONS",
            ["SAVING THROWS"] = "SAVING THROWS",
            ["WEAPON PROFICIENCIES"] = "WEAPON PROFICIENCIES",
            ["TURNING UNDEAD"] = "TURNING UNDEAD",
            ["UNARMED COMBAT"] = "UNARMED COMBAT",
            ["NONWEAPON PROFICIENCIES"] = "NONWEAPON PROFICIENCIES",
            ["LANGUAGES"] = "LANGUAGES",
            ["EQUIPMENT"] = "EQUIPMENT",
            ["WEALTH"] = "EQUIPMENT",
            ["TRAITS & DISADVANTAGES"] = "TRAITS / DISADVANTAGES",
            ["NOTES"] = "NOTES",
            ["SPELLS"] = "SPELLS",
        };

        var blocks = ParseTextSections(fullSheetText);
        var output = new List<string>();

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.heading))
            {
                output.AddRange(block.lines);
                continue;
            }

            if (string.Equals(block.heading, "CHARACTER SHEET", StringComparison.OrdinalIgnoreCase))
            {
                output.AddRange(block.lines);
                continue;
            }

            string? matchingVisualTitle = textSectionByVisualTitle
                .FirstOrDefault(pair => string.Equals(pair.Value, block.heading, StringComparison.OrdinalIgnoreCase))
                .Key;

            if (matchingVisualTitle is null)
            {
                output.AddRange(block.lines);
                continue;
            }

            if (!selectedTitles.Contains(matchingVisualTitle))
                continue;

            output.AddRange(block.lines);
        }

        return string.Join(Environment.NewLine, output);
    }

    private static List<(string heading, List<string> lines)> ParseTextSections(string fullSheetText)
    {
        var sections = new List<(string heading, List<string> lines)>();
        string[] rawLines = fullSheetText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        string currentHeading = string.Empty;
        var currentLines = new List<string>();

        void Flush()
        {
            if (currentLines.Count == 0 && string.IsNullOrWhiteSpace(currentHeading))
                return;

            sections.Add((currentHeading, new List<string>(currentLines)));
            currentLines.Clear();
        }

        foreach (string line in rawLines)
        {
            if (line.StartsWith("=== ") && line.EndsWith(" ===", StringComparison.Ordinal))
            {
                Flush();
                currentHeading = line.Substring(4, line.Length - 8).Trim();
                currentLines.Add(line);
                continue;
            }

            currentLines.Add(line);
        }

        Flush();
        return sections;
    }

    private bool IsLayoutUiReady() => LaneSurface is not null;

    private void ClearLayoutSurface() => LaneSurface.Children.Clear();

    private IEnumerable<Expander> GetLiveSectionExpanders()
    {
        if (!IsLayoutUiReady())
            return Enumerable.Empty<Expander>();

        return GetDescendantExpanders(LaneSurface);
    }

    private void EnsureLayoutForCards()
    {
        var keys = _sectionCards.Select(card => card.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string staleKey in _sectionLayoutByKey.Keys.Where(key => !keys.Contains(key)).ToList())
            _sectionLayoutByKey.Remove(staleKey);

        int nextRow = _sectionLayoutByKey.Values.Count == 0 ? 0 : _sectionLayoutByKey.Values.Max(v => v.Order) + 1;
        foreach (var card in _sectionCards)
        {
            if (_sectionLayoutByKey.ContainsKey(card.Key))
            {
                _sectionLayoutByKey[card.Key].Span = Math.Clamp(_sectionLayoutByKey[card.Key].Span, 1, 3);
                continue;
            }

            _sectionLayoutByKey[card.Key] = new SectionPlacement
            {
                Order = nextRow++,
                Lane = 0,
                Span = 1,
            };
        }

        NormalizeLayoutRows();
    }

    private void NormalizeLayoutRows()
    {
        var rowSlots = new SortedDictionary<int, string?[]>();
        foreach (var kv in _sectionLayoutByKey
            .OrderBy(kv => Math.Max(0, kv.Value.Order))
            .ThenBy(kv => Math.Clamp(kv.Value.Lane, 0, 2)))
        {
            int targetRow = Math.Max(0, kv.Value.Order);
            int span = Math.Clamp(kv.Value.Span, 1, 3);
            int targetLane = Math.Clamp(kv.Value.Lane, 0, 3 - span);
            PlaceWithSpill(rowSlots, targetRow, targetLane, span, kv.Key);
        }

        int normalizedRow = 0;
        foreach (var slots in rowSlots.Values.Where(values => values.Any(value => !string.IsNullOrWhiteSpace(value))))
        {
            var handledKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int lane = 0; lane < 3; lane++)
            {
                string? key = slots[lane];
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                if (!_sectionLayoutByKey.ContainsKey(key) || handledKeys.Contains(key))
                    continue;

                int span = 1;
                for (int cursor = lane + 1; cursor < 3; cursor++)
                {
                    if (!string.Equals(slots[cursor], key, StringComparison.OrdinalIgnoreCase))
                        break;
                    span++;
                }

                _sectionLayoutByKey[key].Order = normalizedRow;
                _sectionLayoutByKey[key].Lane = lane;
                _sectionLayoutByKey[key].Span = Math.Clamp(span, 1, 3);
                handledKeys.Add(key);
            }
            normalizedRow++;
        }
    }

    private void RenderSectionCards()
    {
        if (!IsLayoutUiReady())
            return;

        ClearLayoutSurface();
        if (_sectionCards.Count == 0)
            return;

        EnsureLayoutForCards();

        var rowIndexes = _sectionLayoutByKey.Values
            .Select(v => v.Order)
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        LaneSurface.Children.Add(BuildInsertionTarget(0));

        foreach (int rowIndex in rowIndexes)
        {
            var rowCards = _sectionCards
                .Where(card => card.Expander.Visibility == Visibility.Visible
                    && _sectionLayoutByKey.TryGetValue(card.Key, out var p)
                    && p.Order == rowIndex)
                .OrderBy(card => _sectionLayoutByKey[card.Key].Lane)
                .ToList();
            if (rowCards.Count == 0)
                continue;

            var rowGrid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8),
                Tag = rowIndex,
                AllowDrop = true,
                Background = Brushes.Transparent,
            };
            rowGrid.DragOver += RowGrid_DragOver;
            rowGrid.Drop += RowGrid_Drop;

            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int lane = 0; lane < 3; lane++)
            {
                var laneTarget = new Border
                {
                    Background = Brushes.Transparent,
                    AllowDrop = true,
                    Tag = new RowLaneTarget { Row = rowIndex, Lane = lane },
                };
                laneTarget.DragOver += LaneTarget_DragOver;
                laneTarget.DragLeave += LaneTarget_DragLeave;
                laneTarget.Drop += LaneTarget_Drop;
                Grid.SetColumn(laneTarget, lane * 2);
                Panel.SetZIndex(laneTarget, 0);
                rowGrid.Children.Add(laneTarget);
            }

            for (int i = 0; i < rowCards.Count; i++)
            {
                DetachFromParent(rowCards[i].Expander);
                int lane = Math.Clamp(_sectionLayoutByKey[rowCards[i].Key].Lane, 0, 2);
                int span = Math.Clamp(_sectionLayoutByKey[rowCards[i].Key].Span, 1, 3);
                Grid.SetColumn(rowCards[i].Expander, lane * 2);
                Grid.SetColumnSpan(rowCards[i].Expander, Math.Min(5 - (lane * 2), (span * 2) - 1));
                Panel.SetZIndex(rowCards[i].Expander, 1);
                rowGrid.Children.Add(rowCards[i].Expander);
            }

            LaneSurface.Children.Add(rowGrid);
            LaneSurface.Children.Add(BuildInsertionTarget(rowIndex + 1));
        }

        UpdateExpandCollapseToggleButton();
    }

    private Border BuildInsertionTarget(int insertionRow)
    {
        var line = new Border
        {
            Height = 2,
            Background = (Brush)FindResource("BrushBorder2"),
            Opacity = 0.35,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(1),
            IsHitTestVisible = false,
        };

        var host = new Border
        {
            Height = 12,
            Margin = new Thickness(0, 0, 0, 8),
            Background = Brushes.Transparent,
            Tag = insertionRow,
            AllowDrop = true,
            Child = line,
        };

        host.DragOver += InsertionTarget_DragOver;
        host.DragLeave += InsertionTarget_DragLeave;
        host.Drop += InsertionTarget_Drop;
        return host;
    }

    private static void DetachFromParent(UIElement element)
    {
        switch (VisualTreeHelper.GetParent(element))
        {
            case Panel panel:
                panel.Children.Remove(element);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
                break;
        }
    }

    private void SectionCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Expander expander)
            return;
        if (expander.Header is not TextBlock header || !header.IsMouseOver)
            return;

        if (expander.Tag is not string sectionKey || string.IsNullOrWhiteSpace(sectionKey))
            return;

        _dragStartPoint = e.GetPosition(this);
        _dragSectionKey = sectionKey;
    }

    private void SectionCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || string.IsNullOrWhiteSpace(_dragSectionKey))
            return;
        if (sender is not UIElement dragSource)
            return;
        if (!_sectionLayoutByKey.ContainsKey(_dragSectionKey))
        {
            _dragSectionKey = null;
            return;
        }

        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        try
        {
            var data = new DataObject(SectionDragDataFormat, _dragSectionKey);
            DragDrop.DoDragDrop(dragSource, data, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            StatusText.Text = BuildStatusText($"Could not drag section: {ex.Message}");
        }
        finally
        {
            _dragSectionKey = null;
        }
    }

    private void RowGrid_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(SectionDragDataFormat) ? DragDropEffects.Move : DragDropEffects.None;

        if (e.Effects == DragDropEffects.Move
            && sender is Grid rowGrid
            && rowGrid.Tag is int targetRow
            && TryGetDraggingSectionKey(e, out string movingKey))
        {
            int lane = ResolveDropLane(rowGrid, targetRow, movingKey, e.GetPosition(rowGrid));
            UpdateDragDebugStatus("row", movingKey, targetRow, lane);
        }

        e.Handled = true;
    }

    private void RowGrid_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (sender is not Grid rowGrid)
                return;
            if (!e.Data.GetDataPresent(SectionDragDataFormat))
                return;

            string? key = e.Data.GetData(SectionDragDataFormat) as string;
            if (string.IsNullOrWhiteSpace(key) || !_sectionLayoutByKey.ContainsKey(key))
                return;

            if (rowGrid.Tag is not int targetRow)
                return;

            int targetLane = ResolveDropLane(rowGrid, targetRow, key, e.GetPosition(rowGrid));
            PlaceSectionDeterministically(key, targetRow, targetLane);

            e.Handled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = BuildStatusText($"Could not place section: {ex.Message}");
        }
    }

    private void LaneTarget_DragOver(object sender, DragEventArgs e)
    {
        bool canDrop = e.Data.GetDataPresent(SectionDragDataFormat);
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;

        if (sender is Border target)
            target.Background = canDrop ? new SolidColorBrush(Color.FromArgb(28, 255, 215, 0)) : Brushes.Transparent;

        if (canDrop
            && sender is Border laneTarget
            && laneTarget.Tag is RowLaneTarget info
            && TryGetDraggingSectionKey(e, out string movingKey))
        {
            UpdateDragDebugStatus("lane", movingKey, info.Row, info.Lane);
        }

        e.Handled = true;
    }

    private void LaneTarget_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border target)
            target.Background = Brushes.Transparent;
    }

    private void LaneTarget_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (sender is not Border target || target.Tag is not RowLaneTarget info)
                return;
            if (!e.Data.GetDataPresent(SectionDragDataFormat))
                return;

            string? key = e.Data.GetData(SectionDragDataFormat) as string;
            if (string.IsNullOrWhiteSpace(key) || !_sectionLayoutByKey.ContainsKey(key))
                return;

            PlaceSectionDeterministically(key, info.Row, info.Lane);

            target.Background = Brushes.Transparent;
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = BuildStatusText($"Could not place section: {ex.Message}");
        }
    }

    private void InsertionTarget_DragOver(object sender, DragEventArgs e)
    {
        bool canDrop = e.Data.GetDataPresent(SectionDragDataFormat);
        e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        if (sender is Border host && host.Child is Border line)
        {
            line.Opacity = canDrop ? 1.0 : 0.35;
            line.Background = canDrop ? (Brush)FindResource("BrushTitle") : (Brush)FindResource("BrushBorder2");
        }

        if (canDrop
            && sender is Border insertionHost
            && insertionHost.Tag is int insertionRow
            && TryGetDraggingSectionKey(e, out string movingKey))
        {
            int lane = ResolveSurfaceDropLane(e.GetPosition(LaneSurface).X, movingKey);
            UpdateDragDebugStatus("insert", movingKey, insertionRow, lane);
        }

        e.Handled = true;
    }

    private void InsertionTarget_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border host && host.Child is Border line)
        {
            line.Opacity = 0.35;
            line.Background = (Brush)FindResource("BrushBorder2");
        }
    }

    private void InsertionTarget_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (sender is not Border host || host.Tag is not int insertionRow)
                return;
            if (!e.Data.GetDataPresent(SectionDragDataFormat))
                return;

            string? key = e.Data.GetData(SectionDragDataFormat) as string;
            if (string.IsNullOrWhiteSpace(key) || !_sectionLayoutByKey.ContainsKey(key))
                return;

            Point lanePoint = e.GetPosition(LaneSurface);
            int lane = ResolveSurfaceDropLane(lanePoint.X, key);
            MoveSectionCardToInsertedRow(key, insertionRow, lane);

            if (host.Child is Border line)
            {
                line.Opacity = 0.35;
                line.Background = (Brush)FindResource("BrushBorder2");
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = BuildStatusText($"Could not insert section: {ex.Message}");
        }
    }

    private void LaneSurface_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(SectionDragDataFormat) ? DragDropEffects.Move : DragDropEffects.None;

        if (e.Effects == DragDropEffects.Move && TryGetDraggingSectionKey(e, out string movingKey))
        {
            Point dropPoint = e.GetPosition(LaneSurface);
            DependencyObject? hit = GetHitVisualAtPoint(LaneSurface, dropPoint);

            RowLaneTarget? hitLaneTarget = GetRowLaneTargetFromDropSender(hit);
            if (hitLaneTarget is not null)
            {
                UpdateDragDebugStatus("lane", movingKey, hitLaneTarget.Row, hitLaneTarget.Lane);
            }
            else
            {
                int? insertionRow = GetInsertionRowFromDropSender(hit);
                if (insertionRow.HasValue)
                {
                    int lane = ResolveSurfaceDropLane(dropPoint.X, movingKey);
                    UpdateDragDebugStatus("insert", movingKey, insertionRow.Value, lane);
                }
                else if (TryResolveDropTarget(dropPoint, out Grid? targetRowGrid, out int targetRow) && targetRowGrid is not null)
                {
                    int lane = ResolveDropLane(targetRowGrid, targetRow, movingKey, e.GetPosition(targetRowGrid));
                    UpdateDragDebugStatus("surface", movingKey, targetRow, lane);
                }
            }
        }

        e.Handled = true;
    }

    private void LaneSurface_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.Data.GetDataPresent(SectionDragDataFormat))
                return;

            string? key = e.Data.GetData(SectionDragDataFormat) as string;
            if (string.IsNullOrWhiteSpace(key) || !_sectionLayoutByKey.ContainsKey(key))
                return;

            Point dropPoint = e.GetPosition(LaneSurface);
            DependencyObject? hit = GetHitVisualAtPoint(LaneSurface, dropPoint);

            // Highest priority: explicit row/lane drop targets (highlight boxes).
            RowLaneTarget? hitLaneTarget = GetRowLaneTargetFromDropSender(hit);
            if (hitLaneTarget is not null)
            {
                PlaceSectionDeterministically(key, hitLaneTarget.Row, hitLaneTarget.Lane);

                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            // Next priority: explicit insertion-line targets.
            int? insertionRow = GetInsertionRowFromDropSender(hit);
            if (insertionRow.HasValue)
            {
                int lane = ResolveSurfaceDropLane(dropPoint.X, key);
                MoveSectionCardToInsertedRow(key, insertionRow.Value, lane);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            // First choice: if the pointer is over a specific section, swap the two.
            string? targetSectionKey = GetSectionKeyFromDropSender(hit);
            if (!string.IsNullOrWhiteSpace(targetSectionKey)
                && !string.Equals(targetSectionKey, key, StringComparison.OrdinalIgnoreCase)
                && _sectionLayoutByKey.ContainsKey(targetSectionKey))
            {
                SwapSections(key, targetSectionKey);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            StatusText.Text = BuildStatusText("Drop on a highlighted lane box, insertion line, or section row.");
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        catch (Exception ex)
        {
            StatusText.Text = BuildStatusText($"Could not create new row: {ex.Message}");
        }
    }

    private static DependencyObject? GetHitVisualAtPoint(Visual root, Point point)
    {
        HitTestResult? hit = VisualTreeHelper.HitTest(root, point);
        return hit?.VisualHit as DependencyObject;
    }

    private bool TryResolveDropRow(Point dropPointInLaneSurface, out Grid targetRow)
    {
        targetRow = null!;

        var rowGrids = LaneSurface.Children.OfType<Grid>().ToList();
        if (rowGrids.Count == 0)
            return false;

        foreach (Grid rowGrid in rowGrids)
        {
            Point rowTopLeft = rowGrid.TranslatePoint(new Point(0, 0), LaneSurface);
            double rowHeight = rowGrid.ActualHeight;
            if (dropPointInLaneSurface.Y >= rowTopLeft.Y && dropPointInLaneSurface.Y <= rowTopLeft.Y + rowHeight)
            {
                targetRow = rowGrid;
                return true;
            }
        }

        // If dropped between rows, pick the closest row center.
        targetRow = rowGrids
            .OrderBy(grid =>
            {
                Point topLeft = grid.TranslatePoint(new Point(0, 0), LaneSurface);
                double centerY = topLeft.Y + (grid.ActualHeight / 2.0);
                return Math.Abs(dropPointInLaneSurface.Y - centerY);
            })
            .First();
        return true;
    }

    private bool TryResolveDropTarget(Point dropPointInLaneSurface, out Grid? targetRowGrid, out int insertionRow)
    {
        targetRowGrid = null;
        insertionRow = 0;

        var rowGrids = LaneSurface.Children
            .OfType<Grid>()
            .OrderBy(grid => grid.Tag is int idx ? idx : int.MaxValue)
            .ToList();

        if (rowGrids.Count == 0)
            return false;

        var rowBounds = rowGrids
            .Select(grid =>
            {
                Point topLeft = grid.TranslatePoint(new Point(0, 0), LaneSurface);
                double top = topLeft.Y;
                double bottom = top + grid.ActualHeight;
                int rowIndex = grid.Tag is int idx ? idx : 0;
                return (grid, rowIndex, top, bottom);
            })
            .OrderBy(x => x.rowIndex)
            .ToList();

        // Above the first row: insert at top.
        if (dropPointInLaneSurface.Y < rowBounds[0].top)
        {
            targetRowGrid = rowBounds[0].grid;
            insertionRow = rowBounds[0].rowIndex;
            return true;
        }

        for (int i = 0; i < rowBounds.Count; i++)
        {
            var current = rowBounds[i];

            // Inside a row's vertical bounds: treat as drop into that row.
            if (dropPointInLaneSurface.Y >= current.top && dropPointInLaneSurface.Y <= current.bottom)
            {
                targetRowGrid = current.grid;
                insertionRow = current.rowIndex;
                return true;
            }

            // Between this row and the next row: insert at the next row index.
            if (i < rowBounds.Count - 1)
            {
                var next = rowBounds[i + 1];
                if (dropPointInLaneSurface.Y > current.bottom && dropPointInLaneSurface.Y < next.top)
                {
                    insertionRow = next.rowIndex;
                    return true;
                }
            }
        }

        // Below the last row: snap to the last row context instead of auto-appending.
        targetRowGrid = rowBounds[^1].grid;
        insertionRow = rowBounds[^1].rowIndex;
        return true;
    }

    private int ResolveSurfaceDropLane(double dropXInLaneSurface, string movingKey)
    {
        int span = GetSectionSpan(movingKey);
        double width = Math.Max(1, LaneSurface.ActualWidth);
        int lane = (int)(dropXInLaneSurface / (width / 3.0));
        return Math.Clamp(lane, 0, 3 - span);
    }

    private bool CanFitSectionAtRowLane(string sectionKey, int row, int lane)
    {
        var slots = BuildRowSlotsExcluding(sectionKey);
        int span = GetSectionSpan(sectionKey);
        int clampedLane = Math.Clamp(lane, 0, 3 - span);

        if (!slots.TryGetValue(Math.Max(0, row), out var rowSlots))
            return true;

        for (int i = 0; i < span; i++)
        {
            if (!string.IsNullOrWhiteSpace(rowSlots[clampedLane + i]))
                return false;
        }

        return true;
    }

    private void PlaceSectionDeterministically(string sectionKey, int targetRow, int targetLane)
    {
        if (_sectionLayoutByKey.TryGetValue(sectionKey, out var movingPlacement))
        {
            // Keep the section's configured width while dragging.
            movingPlacement.Span = Math.Clamp(movingPlacement.Span, 1, 3);
            movingPlacement.Lane = Math.Clamp(movingPlacement.Lane, 0, 2);
            movingPlacement.Order = Math.Max(0, movingPlacement.Order);
        }

        MoveSectionCard(sectionKey, targetRow, targetLane);
    }

    private int ResolveDropLane(Grid rowGrid, int targetRow, string movingKey, Point dropPointInRow)
    {
        _ = targetRow;
        _ = movingKey;
        double width = Math.Max(1, rowGrid.ActualWidth);
        int lane = (int)(dropPointInRow.X / (width / 3.0));
        return Math.Clamp(lane, 0, 2);
    }

    private static Grid? FindOwningRowGrid(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is Grid grid && grid.Tag is int)
                return grid;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static RowLaneTarget? GetRowLaneTargetFromDropSender(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is Border border && border.Tag is RowLaneTarget laneTarget)
                return laneTarget;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static int? GetInsertionRowFromDropSender(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is Border border && border.Tag is int row)
                return row;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void SectionCard_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(SectionDragDataFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        Grid? rowGrid = FindOwningRowGrid(sender as DependencyObject);
        if (rowGrid is null)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        RowGrid_DragOver(rowGrid, e);
    }

    private void SectionCard_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(SectionDragDataFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        string? movingKey = e.Data.GetData(SectionDragDataFormat) as string;
        if (string.IsNullOrWhiteSpace(movingKey) || !_sectionLayoutByKey.ContainsKey(movingKey))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        Grid? rowGrid = FindOwningRowGrid(sender as DependencyObject);
        if (rowGrid is null || rowGrid.Tag is not int rowIndex)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        // If the pointer landed on a specific section card, swap the two.
        string? targetSectionKey = GetSectionKeyFromDropSender(sender as DependencyObject);
        if (!string.IsNullOrWhiteSpace(targetSectionKey)
            && !string.Equals(targetSectionKey, movingKey, StringComparison.OrdinalIgnoreCase)
            && _sectionLayoutByKey.ContainsKey(targetSectionKey))
        {
            SwapSections(movingKey, targetSectionKey);
        }
        else
        {
            int lane = ResolveDropLane(rowGrid, rowIndex, movingKey, e.GetPosition(rowGrid));
            PlaceSectionDeterministically(movingKey, rowIndex, lane);
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static string? GetSectionKeyFromDropSender(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is Expander expander && expander.Tag is string key && !string.IsNullOrWhiteSpace(key))
                return key;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private bool TryGetDraggingSectionKey(DragEventArgs e, out string sectionKey)
    {
        sectionKey = string.Empty;
        if (!e.Data.GetDataPresent(SectionDragDataFormat))
            return false;

        if (e.Data.GetData(SectionDragDataFormat) is not string key || string.IsNullOrWhiteSpace(key))
            return false;

        if (!_sectionLayoutByKey.ContainsKey(key))
            return false;

        sectionKey = key;
        return true;
    }

    private void UpdateDragDebugStatus(string zone, string movingKey, int targetRow, int targetLane)
    {
        if (!_dragDebugEnabled)
            return;

        string movingTitle = _sectionCards
            .FirstOrDefault(card => string.Equals(card.Key, movingKey, StringComparison.OrdinalIgnoreCase))
            ?.Title ?? movingKey;

        StatusText.Text = BuildStatusText(
            $"Debug drag: {movingTitle} -> {zone} row {targetRow + 1}, column {targetLane + 1}");
    }

    private void SwapSections(string movingKey, string targetKey)
    {
        if (!_sectionLayoutByKey.TryGetValue(movingKey, out var movingPlacement)
            || !_sectionLayoutByKey.TryGetValue(targetKey, out var targetPlacement))
            return;

        (movingPlacement.Order, targetPlacement.Order) = (targetPlacement.Order, movingPlacement.Order);
        (movingPlacement.Lane, targetPlacement.Lane) = (targetPlacement.Lane, movingPlacement.Lane);
        (movingPlacement.Span, targetPlacement.Span) = (targetPlacement.Span, movingPlacement.Span);

        NormalizeLayoutRows();
        RenderSectionCards();
        SaveCurrentSectionLayoutPreference();
    }

    private void MoveSectionCard(string sectionKey, int targetRow, int targetLane, bool normalizeTargetRow = true)
    {
        if (!_sectionLayoutByKey.ContainsKey(sectionKey))
            return;

        int span = GetSectionSpan(sectionKey);
        int clampedRow = Math.Max(0, targetRow);
        int clampedLane = Math.Clamp(targetLane, 0, 3 - span);

        // Check if any target slot is occupied by a different section.
        bool slotOccupied = _sectionLayoutByKey.Any(kv =>
            !kv.Key.Equals(sectionKey, StringComparison.OrdinalIgnoreCase) &&
            kv.Value.Order == clampedRow &&
            kv.Value.Lane >= clampedLane && kv.Value.Lane < clampedLane + span);

        if (slotOccupied)
        {
            // Shift all other sections at or after the target row down by one.
            // This opens a clean slot without triggering cascade displacements.
            foreach (var kv in _sectionLayoutByKey)
            {
                if (!kv.Key.Equals(sectionKey, StringComparison.OrdinalIgnoreCase) &&
                    kv.Value.Order >= clampedRow)
                {
                    kv.Value.Order++;
                }
            }
        }

        _sectionLayoutByKey[sectionKey].Order = clampedRow;
        _sectionLayoutByKey[sectionKey].Lane = clampedLane;

        NormalizeLayoutRows();
        _ = normalizeTargetRow;

        RenderSectionCards();
        SaveCurrentSectionLayoutPreference();
    }

    private void MoveSectionCardToInsertedRow(string sectionKey, int insertionRow, int targetLane)
    {
        if (!_sectionLayoutByKey.ContainsKey(sectionKey))
            return;

        int span = GetSectionSpan(sectionKey);
        int clampedRow = Math.Max(0, insertionRow);
        int clampedLane = Math.Clamp(targetLane, 0, 3 - span);

        // Always insert a new row by shifting all other sections at or after
        // the insertion point down by one — no cascade displacement.
        foreach (var kv in _sectionLayoutByKey)
        {
            if (!kv.Key.Equals(sectionKey, StringComparison.OrdinalIgnoreCase) &&
                kv.Value.Order >= clampedRow)
            {
                kv.Value.Order++;
            }
        }

        _sectionLayoutByKey[sectionKey].Order = clampedRow;
        _sectionLayoutByKey[sectionKey].Lane = clampedLane;

        NormalizeLayoutRows();
        RenderSectionCards();
        SaveCurrentSectionLayoutPreference();
    }

    private SortedDictionary<int, string?[]> BuildSimpleRowSlotsExcluding(string excludedSectionKey)
    {
        var rows = new SortedDictionary<int, string?[]>();

        foreach (var kv in _sectionLayoutByKey
            .Where(kv => !string.Equals(kv.Key, excludedSectionKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => Math.Max(0, kv.Value.Order))
            .ThenBy(kv => Math.Clamp(kv.Value.Lane, 0, 2)))
        {
            int row = Math.Max(0, kv.Value.Order);
            int lane = Math.Clamp(kv.Value.Lane, 0, 2);

            if (!rows.TryGetValue(row, out var slots))
            {
                slots = new string?[3];
                rows[row] = slots;
            }

            if (string.IsNullOrWhiteSpace(slots[lane]))
            {
                slots[lane] = kv.Key;
                continue;
            }

            // Resolve collisions deterministically by placing in the next available slot.
            int probeRow = row;
            bool placed = false;
            while (!placed)
            {
                if (!rows.TryGetValue(probeRow, out var probeSlots))
                {
                    probeSlots = new string?[3];
                    rows[probeRow] = probeSlots;
                }

                for (int probeLane = 0; probeLane < 3; probeLane++)
                {
                    if (!string.IsNullOrWhiteSpace(probeSlots[probeLane]))
                        continue;

                    probeSlots[probeLane] = kv.Key;
                    placed = true;
                    break;
                }

                if (!placed)
                    probeRow++;
            }
        }

        return rows;
    }

    private static bool TryPlaceSimpleInRow(SortedDictionary<int, string?[]> rows, int row, int lane, string sectionKey)
    {
        int clampedRow = Math.Max(0, row);
        int clampedLane = Math.Clamp(lane, 0, 2);

        if (!rows.TryGetValue(clampedRow, out var slots))
        {
            slots = new string?[3];
            rows[clampedRow] = slots;
        }

        if (!string.IsNullOrWhiteSpace(slots[clampedLane]))
            return false;

        slots[clampedLane] = sectionKey;
        return true;
    }

    private static bool TryFindOpenLaneInRow(SortedDictionary<int, string?[]> rows, int row, int preferredLane, out int openLane)
    {
        openLane = Math.Clamp(preferredLane, 0, 2);
        if (!rows.TryGetValue(Math.Max(0, row), out var slots))
            return true;

        if (string.IsNullOrWhiteSpace(slots[openLane]))
            return true;

        for (int offset = 1; offset < 3; offset++)
        {
            int right = openLane + offset;
            if (right <= 2 && string.IsNullOrWhiteSpace(slots[right]))
            {
                openLane = right;
                return true;
            }

            int left = openLane - offset;
            if (left >= 0 && string.IsNullOrWhiteSpace(slots[left]))
            {
                openLane = left;
                return true;
            }
        }

        return false;
    }

    private static void InsertSimpleRow(SortedDictionary<int, string?[]> rows, int insertionRow)
    {
        int rowIndex = Math.Max(0, insertionRow);
        var shifted = rows
            .OrderBy(kv => kv.Key)
            .ToDictionary(
                kv => kv.Key >= rowIndex ? kv.Key + 1 : kv.Key,
                kv => kv.Value);

        rows.Clear();
        foreach (var kv in shifted.OrderBy(kv => kv.Key))
            rows[kv.Key] = kv.Value;
    }

    private void ApplySimpleRowLayout(SortedDictionary<int, string?[]> rows)
    {
        int normalizedRow = 0;
        foreach (var slots in rows.Values.Where(s => s.Any(v => !string.IsNullOrWhiteSpace(v))))
        {
            for (int lane = 0; lane < 3; lane++)
            {
                string? key = slots[lane];
                if (string.IsNullOrWhiteSpace(key) || !_sectionLayoutByKey.ContainsKey(key))
                    continue;

                _sectionLayoutByKey[key].Order = normalizedRow;
                _sectionLayoutByKey[key].Lane = lane;
                _sectionLayoutByKey[key].Span = Math.Clamp(_sectionLayoutByKey[key].Span, 1, 3);
            }

            normalizedRow++;
        }
    }

    private void ForceRowToSingleColumn(int rowIndex)
    {
        int targetRow = Math.Max(0, rowIndex);
        var rowKeys = _sectionLayoutByKey
            .Where(kv => kv.Value.Order == targetRow)
            .OrderBy(kv => kv.Value.Lane)
            .Select(kv => kv.Key)
            .ToList();

        for (int i = 0; i < rowKeys.Count; i++)
        {
            _sectionLayoutByKey[rowKeys[i]].Lane = Math.Clamp(i, 0, 2);
            _sectionLayoutByKey[rowKeys[i]].Span = 1;
        }

        NormalizeLayoutRows();
    }

    private void MoveSectionAboveSection(string movingSectionKey, string targetSectionKey)
    {
        if (!_sectionLayoutByKey.TryGetValue(movingSectionKey, out var movingPlacement))
            return;
        if (!_sectionLayoutByKey.TryGetValue(targetSectionKey, out var targetPlacement))
            return;

        movingPlacement.Span = Math.Clamp(movingPlacement.Span, 1, 3);

        int insertionRow = Math.Max(0, targetPlacement.Order);
        int targetLane = Math.Clamp(targetPlacement.Lane, 0, 2);

        var rowSlots = BuildRowSlotsExcluding(movingSectionKey);
        var shifted = new SortedDictionary<int, string?[]>();
        foreach (var kv in rowSlots)
        {
            int row = kv.Key >= insertionRow ? kv.Key + 1 : kv.Key;
            shifted[row] = kv.Value;
        }

        int movingSpan = Math.Clamp(movingPlacement.Span, 1, 3);
        int lane = Math.Clamp(targetLane, 0, 3 - movingSpan);
        PlaceWithSpill(shifted, insertionRow, lane, movingSpan, movingSectionKey);

        ApplyRowSlotsLayout(shifted);
        RenderSectionCards();
        SaveCurrentSectionLayoutPreference();
        StatusText.Text = BuildStatusText("Moved section above target section.");
    }

    private SortedDictionary<int, string?[]> BuildRowSlotsExcluding(string? excludedSectionKey = null)
    {
        var rowSlots = new SortedDictionary<int, string?[]>();
        foreach (var kv in _sectionLayoutByKey
            .Where(kv => string.IsNullOrWhiteSpace(excludedSectionKey)
                || !string.Equals(kv.Key, excludedSectionKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => Math.Max(0, kv.Value.Order))
            .ThenBy(kv => Math.Clamp(kv.Value.Lane, 0, 2)))
        {
            int row = Math.Max(0, kv.Value.Order);
            int span = Math.Clamp(kv.Value.Span, 1, 3);
            int lane = Math.Clamp(kv.Value.Lane, 0, 3 - span);
            PlaceWithSpill(rowSlots, row, lane, span, kv.Key);
        }

        return rowSlots;
    }

    private void ApplyRowSlotsLayout(SortedDictionary<int, string?[]> rowSlots)
    {
        int normalizedRow = 0;
        foreach (var slots in rowSlots.Values.Where(values => values.Any(value => !string.IsNullOrWhiteSpace(value))))
        {
            var handledKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int lane = 0; lane < 3; lane++)
            {
                string? key = slots[lane];
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                if (!_sectionLayoutByKey.ContainsKey(key) || handledKeys.Contains(key))
                    continue;

                int span = 1;
                for (int cursor = lane + 1; cursor < 3; cursor++)
                {
                    if (!string.Equals(slots[cursor], key, StringComparison.OrdinalIgnoreCase))
                        break;
                    span++;
                }

                _sectionLayoutByKey[key].Order = normalizedRow;
                _sectionLayoutByKey[key].Lane = lane;
                _sectionLayoutByKey[key].Span = Math.Clamp(span, 1, 3);
                handledKeys.Add(key);
            }
            normalizedRow++;
        }
    }

    private int GetSectionSpan(string sectionKey)
    {
        if (_sectionLayoutByKey.TryGetValue(sectionKey, out var placement))
            return Math.Clamp(placement.Span, 1, 3);
        return 1;
    }

    private void PlaceWithSpill(SortedDictionary<int, string?[]> rowSlots, int startRow, int startLane, int span, string sectionKey)
    {
        int row = Math.Max(0, startRow);
        int clampedSpan = Math.Clamp(span, 1, 3);
        int lane = Math.Clamp(startLane, 0, 3 - clampedSpan);

        if (!rowSlots.TryGetValue(row, out var slots))
        {
            slots = new string?[3];
            rowSlots[row] = slots;
        }

        var displacedKeys = new List<string>();
        for (int i = 0; i < clampedSpan; i++)
        {
            string? existing = slots[lane + i];
            if (!string.IsNullOrWhiteSpace(existing)
                && !string.Equals(existing, sectionKey, StringComparison.OrdinalIgnoreCase)
                && !displacedKeys.Contains(existing, StringComparer.OrdinalIgnoreCase))
            {
                displacedKeys.Add(existing);
            }
        }

        for (int i = 0; i < 3; i++)
        {
            string candidate = slots[i] ?? string.Empty;
            if (string.Equals(candidate, sectionKey, StringComparison.OrdinalIgnoreCase)
                || displacedKeys.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                slots[i] = null;
            }
        }

        for (int i = 0; i < clampedSpan; i++)
            slots[lane + i] = sectionKey;

        int nextRow = row;
        int nextLane = lane + clampedSpan;
        if (nextLane > 2)
        {
            nextRow++;
            nextLane = 0;
        }

        foreach (string displacedKey in displacedKeys)
        {
            int displacedSpan = GetSectionSpan(displacedKey);
            int displacedRow = nextRow;
            int displacedLane = nextLane;

            // If the displaced section cannot fit in the remaining slots of this row,
            // start it on the next row so it never overwrites the section just placed.
            if (displacedLane + displacedSpan - 1 > 2)
            {
                displacedRow++;
                displacedLane = 0;
            }

            displacedLane = Math.Clamp(displacedLane, 0, 3 - displacedSpan);
            PlaceWithSpill(rowSlots, displacedRow, displacedLane, displacedSpan, displacedKey);

            nextRow = displacedRow;
            nextLane = displacedLane + displacedSpan;
            if (nextLane > 2)
            {
                nextRow++;
                nextLane = 0;
            }
        }
    }

    private static string BuildSheetLayoutProfileKey(CharacterSheet character)
    {
        string classKey = character.ClassIds is { Count: > 0 }
            ? string.Join("+", character.ClassIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
            : character.ClassId ?? string.Empty;
        return $"{classKey}|{character.CharacterMode}|{character.ClassMode}";
    }

    private void LoadSavedSectionLayoutPreference(CharacterSheet character)
    {
        _sectionLayoutByKey.Clear();
        _expandedSectionKeys.Clear();
        _hasSavedExpansionState = false;

        string profileKey = BuildSheetLayoutProfileKey(character);
        if (!character.SheetLayoutByProfile.TryGetValue(profileKey, out var saved)
            || saved is null)
        {
            return;
        }

        if (saved.HasExpansionState)
        {
            _hasSavedExpansionState = true;
            foreach (string key in saved.ExpandedSectionKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
                _expandedSectionKeys.Add(key);
        }

        foreach (var placement in saved.Placements)
        {
            if (string.IsNullOrWhiteSpace(placement.SectionKey))
                continue;

            int row = placement.Row;
            int lane = placement.Column;
            if (row == 0 && lane == 0 && (placement.Order != 0 || placement.Lane != 0))
            {
                row = placement.Order;
                lane = placement.Lane;
            }

            _sectionLayoutByKey[placement.SectionKey] = new SectionPlacement
            {
                Lane = Math.Clamp(lane, 0, 2),
                Order = Math.Max(0, row),
                Span = Math.Clamp(placement.Span <= 0 ? 1 : placement.Span, 1, 3),
            };
        }
    }

    private void SaveCurrentSectionLayoutPreference()
    {
        if (_selectedCharacter is null)
            return;

        string profileKey = BuildSheetLayoutProfileKey(_selectedCharacter);
        var preference = new CharacterSheetLayoutPreference
        {
            HasExpansionState = true,
            ExpandedSectionKeys = _sectionCards
                .Where(card => card.Expander.IsExpanded)
                .Select(card => card.Key)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Placements = _sectionLayoutByKey
                .OrderBy(kv => kv.Value.Order)
                .ThenBy(kv => kv.Value.Lane)
                .Select(kv => new CharacterSheetLayoutPlacement
                {
                    SectionKey = kv.Key,
                    Lane = kv.Value.Lane,
                    Order = kv.Value.Order,
                    Row = kv.Value.Order,
                    Column = kv.Value.Lane,
                    Span = Math.Clamp(kv.Value.Span, 1, 3),
                })
                .ToList(),
        };

        _selectedCharacter.SheetLayoutByProfile[profileKey] = preference;
        _app.SaveCharacters();
    }

    private bool ResolveSectionExpandedState(string key, bool defaultExpanded)
    {
        if (!_hasSavedExpansionState)
            return defaultExpanded;

        return _expandedSectionKeys.Contains(key);
    }

    private void LoadHiddenSectionPreference(CharacterSheet character)
    {
        _hiddenSectionKeys.Clear();

        string profileKey = BuildSheetLayoutProfileKey(character);
        if (!character.HiddenSectionKeysByProfile.TryGetValue(profileKey, out var hiddenKeys)
            || hiddenKeys is null)
        {
            return;
        }

        foreach (string key in hiddenKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
            _hiddenSectionKeys.Add(key);
    }

    private void SaveCurrentHiddenSectionPreference()
    {
        if (_selectedCharacter is null)
            return;

        string profileKey = BuildSheetLayoutProfileKey(_selectedCharacter);
        _selectedCharacter.HiddenSectionKeysByProfile[profileKey] = _hiddenSectionKeys
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _app.SaveCharacters();
    }

    private void ApplyHiddenSectionVisibility()
    {
        foreach (var card in _sectionCards)
            card.Expander.Visibility = _hiddenSectionKeys.Contains(card.Key) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HideSection(string sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
            return;

        _hiddenSectionKeys.Add(sectionKey);
        ApplyHiddenSectionVisibility();
        SaveCurrentHiddenSectionPreference();
        RenderSectionCards();

        string title = _sectionCards.FirstOrDefault(card => string.Equals(card.Key, sectionKey, StringComparison.OrdinalIgnoreCase))?.Title ?? sectionKey;
        StatusText.Text = BuildStatusText($"Hidden section: {title}.");
    }

    private void ShowSection(string sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
            return;

        if (!_hiddenSectionKeys.Remove(sectionKey))
            return;

        ApplyHiddenSectionVisibility();
        SaveCurrentHiddenSectionPreference();
        RenderSectionCards();

        string title = _sectionCards.FirstOrDefault(card => string.Equals(card.Key, sectionKey, StringComparison.OrdinalIgnoreCase))?.Title ?? sectionKey;
        StatusText.Text = BuildStatusText($"Restored section: {title}.");
    }

    private void ResetSectionVisibility()
    {
        if (_hiddenSectionKeys.Count == 0)
        {
            StatusText.Text = BuildStatusText("No hidden sections to restore.");
            return;
        }

        _hiddenSectionKeys.Clear();
        ApplyHiddenSectionVisibility();
        SaveCurrentHiddenSectionPreference();
        RenderSectionCards();
        StatusText.Text = BuildStatusText("Restored all hidden sections.");
    }

    // ── Section building ──────────────────────────────────────────────────────

    private void PopulateSheetSections(CharacterSheet c)
    {
        _sectionCards.Clear();
        LoadSavedSectionLayoutPreference(c);
        LoadHiddenSectionPreference(c);

        void AddSection(string key, string title, bool expanded, Action<StackPanel> populate)
        {
            _sectionCards.Add(new SectionCard
            {
                Key = key,
                Title = title,
                Expander = BuildSection(key, title, ResolveSectionExpandedState(key, expanded), populate),
            });
        }

        string race     = string.IsNullOrWhiteSpace(c.RaceName)  ? c.RaceId  : c.RaceName;
        string cls      = string.IsNullOrWhiteSpace(c.ClassName) ? c.ClassId : c.ClassName;
        string ruleset  = c.CharacterMode == "players_option" ? "Player's Option" : "Core Rules";

        // 1. CHARACTER IDENTITY
        AddSection("character_identity", "CHARACTER IDENTITY", true, p =>
        {
            var (identityColumns, identityLeft, identityRight) = CreateTwoColumnPanels();
            AddKV(identityLeft, "Name",       c.Name);
            AddKV(identityLeft, "Player",     string.IsNullOrWhiteSpace(c.PlayerName) ? "—" : c.PlayerName);
            AddKV(identityLeft, "Party",      string.IsNullOrWhiteSpace(c.Party)      ? "—" : c.Party);
            AddKV(identityLeft, "Ruleset",    ruleset);

            AddKV(identityRight, "Race",       race);
            AddKV(identityRight, "Class",      cls);
            AddKV(identityRight, "Level",      FormatLevelDisplay(c));
            AddKV(identityRight, "Experience", $"{c.ExperiencePoints:n0} XP");
            AddKV(identityRight, "Experience Needed", FormatExperienceNeededDisplay(c));

            p.Children.Add(identityColumns);
        });

        // 2. ABILITY SCORES
        AddSection("ability_scores", "ABILITY SCORES", true, p =>
        {
            bool first = true;
            foreach (string abilityKey in AbilityKeys)
            {
                int score = ResolveAbilityScore(c, abilityKey);
                if (!first) p.Children.Add(new Border { Height = 6 });
                first = false;
                AddAbilityBlock(p, c, abilityKey, score);
            }
        });

        // 3. COMBAT STATISTICS
        AddSection("combat_statistics", "COMBAT STATISTICS", true, p =>
        {
            AddKV(p, "Hit Points",    $"{c.HitPoints}  (base {c.BaseHitPoints})");
            AddKV(p, "Armor Class",   $"{c.ArmorClass}  (base {c.BaseArmorClass})");
            AddKV(p, "Armor Profile", c.ArmorProfile.Replace("_", " "));
            AddKV(p, "THAC0",         c.Thac0.ToString());
            AddKV(p, "Attack Rate",   c.AttackRate);
            AddKV(p, "Movement",      $"{c.Movement}  (base {c.BaseMovement})");
            if (!string.IsNullOrWhiteSpace(c.RogueSkillArmorProfile) && c.RogueSkillArmorProfile != "no_armor")
                AddKV(p, "Rogue Armor", c.RogueSkillArmorProfile.Replace("_", " "));
            if (HasClassAbility(c, "cleric_warrior_priests"))
                AddKV(p, "Warrior Bonus", "Active — Exceptional STR eligible; CON 17 = +3 HP/level, CON 18+ = +4 HP/level");
        });

        // 4. WEAPONS — carried weapons as a table grid
        var carriedWeapons = c.EquipmentSelections.Where(e => e.IsWeapon).ToList();
        AddSection("weapons", "WEAPONS", carriedWeapons.Count > 0, p =>
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
        });

        // 5. SAVING THROWS (class table target adjusted by bonuses)
        var saves = BuildSavingThrowRows(c);
        if (saves.Count > 0)
        {
            AddSection("saving_throws", "SAVING THROWS", true, p =>
            {
                foreach (var save in saves)
                {
                    string value = $"Target: {save.FinalTarget}   (base {save.BaseTarget}{(save.Bonus != 0 ? $", bonus {FormatSigned(save.Bonus)}" : string.Empty)})";
                    AddKV(p, save.Label, value);
                }
            });
        }

        // 6. WEAPON PROFICIENCIES
        AddSection("weapon_proficiencies", "WEAPON PROFICIENCIES", true, p =>
        {
            if (c.WeaponProficiencies.Count == 0 && !HasClassAbility(c, "cleric_weapon_allowance", "cleric_weapon_specialization"))
            { AddItem(p, "(none)"); return; }

            var profLines = new List<string>();
            foreach (var wp in c.WeaponProficiencies.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                string spec = wp.Specialized ? "  [Specialized]" : string.Empty;
                string type = wp.ProficiencyType == "individual"
                    ? string.Empty
                    : $"  ({wp.ProficiencyType.Replace("_", " ")})";
                profLines.Add($"{wp.DisplayName}{type}{spec}");
            }
            foreach (string entry in c.SelectedClassAbilityIds.Where(e =>
                string.Equals(RulesEngine.ExtractClassAbilityBaseId(e), "cleric_weapon_allowance", StringComparison.OrdinalIgnoreCase)))
            {
                string w = RulesEngine.ExtractClassAbilityPlayerText(entry);
                profLines.Add($"  ✦ Additional Allowed: {(string.IsNullOrWhiteSpace(w) ? "(not configured)" : w)}");
            }
            foreach (string entry in c.SelectedClassAbilityIds.Where(e =>
                string.Equals(RulesEngine.ExtractClassAbilityBaseId(e), "cleric_weapon_specialization", StringComparison.OrdinalIgnoreCase)))
            {
                string w = RulesEngine.ExtractClassAbilityPlayerText(entry);
                profLines.Add($"  ★ Specialization: {(string.IsNullOrWhiteSpace(w) ? "(not configured)" : w)}");
            }

            foreach (string line in profLines)
                AddItem(p, line);
        });

        // 6b. TURNING UNDEAD (cleric + mastery, or paladin turn undead at level 3+)
        bool showTurningUndead = HasClassAbility(c, "cleric_turn_undead", "cleric_turning_mastery")
            || (HasClassAbility(c, "paladin_turn_undead") && Math.Max(1, c.Level) >= 3);
        if (showTurningUndead)
        {
            AddSection("turning_undead", "TURNING UNDEAD", true, p =>
                BuildTurningUndeadTable(p, c));
        }

        // 6c. UNARMED COMBAT (if cleric has unarmed combat skills)
        if (HasClassAbility(c, "cleric_unarmed_combat_skills"))
        {
            AddSection("unarmed_combat", "UNARMED COMBAT", true, p =>
                BuildUnarmedCombatTable(p, c));
        }

        // 7. NONWEAPON PROFICIENCIES
        AddSection("nonweapon_proficiencies", "NONWEAPON PROFICIENCIES", true, p =>
        {
            var nwp = c.NonweaponProficiencies.ToList();
            if (HasClassAbility(c, "thief_tunneling")
                && !nwp.Any(x => x.Contains("tunnel", StringComparison.OrdinalIgnoreCase)))
            {
                nwp.Add("Tunneling");
            }

            if (HasClassAbility(c, "ranger_tracking_proficiency")
                && !nwp.Any(x => string.Equals(x, "Tracking", StringComparison.OrdinalIgnoreCase)))
            {
                int trackingBonus = GetRangerTrackingBonus(Math.Max(1, c.Level));
                nwp.Add($"Tracking (+{trackingBonus})");
            }

            if (nwp.Count == 0) { AddItem(p, "(none)"); return; }
            var profLines = nwp
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (string line in profLines)
                AddItem(p, line);
        });

        // 8. LANGUAGES
        AddSection("languages", "LANGUAGES", false, p =>
        {
            var (spokenLanguages, writtenLanguages) = BuildLanguageBuckets(c);
            if (spokenLanguages.Count == 0 && writtenLanguages.Count == 0)
            {
                AddItem(p, "(none)");
                return;
            }

            var columns = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var spokenPanel = new StackPanel();
            AddDivider(spokenPanel, "Spoken Languages");
            if (spokenLanguages.Count == 0)
                AddItem(spokenPanel, "(none)");
            else
                foreach (string language in spokenLanguages)
                    AddItem(spokenPanel, language);

            var writtenPanel = new StackPanel();
            AddDivider(writtenPanel, "Written Languages");
            if (writtenLanguages.Count == 0)
                AddItem(writtenPanel, "(none)");
            else
                foreach (string language in writtenLanguages)
                    AddItem(writtenPanel, language);

            Grid.SetColumn(spokenPanel, 0);
            Grid.SetColumn(writtenPanel, 2);
            columns.Children.Add(spokenPanel);
            columns.Children.Add(writtenPanel);
            p.Children.Add(columns);
        });

        // 9. EQUIPMENT
        AddSection("equipment", "EQUIPMENT", true, p =>
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
        });

        // 10. WEALTH
        AddSection("wealth", "WEALTH", false, p =>
        {
            AddKV(p, "Gold Pieces",   c.GoldPieces.ToString());
            AddKV(p, "Silver Pieces", c.SilverPieces.ToString());
            AddKV(p, "Copper Pieces", c.CopperPieces.ToString());
        });

        // 11. TRAITS & DISADVANTAGES
        var traitNames = ResolveTraitNames(c);
        var disadvantageNames = ResolveDisadvantageNames(c);
        bool hasTraitData = traitNames.Count > 0 || disadvantageNames.Count > 0;
        AddSection("traits_disadvantages", "TRAITS & DISADVANTAGES", hasTraitData, p =>
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
        });

        // 12. RACIAL & CLASS ABILITIES
        var displayedAbilityLines = GetDisplayedAbilityLines(c);
        bool hasAbilities = displayedAbilityLines.Count > 0 || c.RacialAbilities.Count > 0;
        if (hasAbilities)
        {
            AddSection("abilities_special_powers", "ABILITIES & SPECIAL POWERS", false, p =>
            {
                if (displayedAbilityLines.Count > 0)
                {
                    foreach (string line in displayedAbilityLines)
                        AddItem(p, line);
                }
                else
                {
                    foreach (string ab in c.RacialAbilities)
                        AddItem(p, ab);
                }
            });
        }

        // 13. NOTES
        AddSection("notes", "NOTES", c.Notes.Count > 0, p =>
        {
            if (c.Notes.Count == 0) { AddItem(p, "(none)"); return; }
            foreach (string note in c.Notes)
                AddItem(p, $"• {note}");
        });

        // 14. SPELLS (at end)
        AddSection("spells", "SPELLS", true, p =>
        {
            BuildSpellsAtEndColumns(p, c);
        });

        EnsureLayoutForCards();
        ApplyHiddenSectionVisibility();
        RenderSectionCards();
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
        int offHandPenalty = role.Equals("OFF-HAND", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        bool removesOffHandPenalty = combatOptions.Contains("fighting_style_two_weapon")
            || HasClassAbility(c, "ranger_two_weapon_style");
        if (offHandPenalty > 0 && removesOffHandPenalty)
            offHandPenalty = 0;
        int totalAttackBonus = atkBonus + trainingAtkBonus + (specialized ? 1 : 0) + enemyAttackBonus;
        int effectiveAtk   = c.Thac0 - totalAttackBonus + nonProfPenalty + offHandPenalty;
        int totalDamageBonus = dmgBonus + trainingDmgBonus + (specialized ? 2 : 0) + enemyDamageBonus;

        // Build THAC0 display: show base + modifiers breakdown when non-proficient
        var thac0Notes = new List<string>();
        if (!proficient)
            thac0Notes.Add($"NP {FormatSigned(nonProfPenalty)}");
        if (offHandPenalty > 0)
            thac0Notes.Add($"off-hand {FormatSigned(offHandPenalty)}");
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

    private static string CoreSubAbilityLabel(string subKey) => subKey switch
    {
        "str_muscle"     => "Melee Attack and Damage",
        "str_stamina"    => "Carrying Capacity",
        "dex_aim"        => "Missile and Rogue Precision",
        "dex_balance"    => "Reaction and Defense",
        "con_health"     => "System Shock and Poison",
        "con_fitness"    => "Hit Points and Resurrection",
        "int_reason"     => "Spell Limits and Immunity",
        "int_knowledge"  => "Languages and Learning",
        "wis_intuition"  => "Bonus Priest Spells",
        "wis_willpower"  => "Magic Defense",
        "wis_perception" => "Surprise and Detection",
        "cha_leadership" => "Leadership and Loyalty",
        "cha_appearance" => "Reaction",
        _ => subKey,
    };

    private static string GetSubAbilityDisplayLabel(CharacterSheet character, string subKey)
        => IsPlayersOptionMode(character) ? SubAbilityLabel(subKey) : CoreSubAbilityLabel(subKey);

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

    private static bool IsPlayersOptionMode(CharacterSheet character)
        => string.Equals(character.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);

    private static int ResolveAbilityScore(CharacterSheet character, string abilityKey)
    {
        if (character.Abilities.TryGetValue(abilityKey, out int directScore))
            return directScore;

        var familyScores = GetSubAbilityKeys(abilityKey)
            .Select(subKey => character.SubAbilities.TryGetValue(subKey, out int score) ? (int?)score : null)
            .Where(score => score.HasValue)
            .Select(score => score!.Value)
            .ToList();

        if (familyScores.Count > 0)
            return (int)Math.Round(familyScores.Average(), MidpointRounding.AwayFromZero);

        // Neutral fallback keeps sheet rendering stable for legacy or partial records.
        return 10;
    }

    private static int ResolveSubAbilityScore(CharacterSheet character, string abilityKey, string subKey, int baseScore)
    {
        if (character.SubAbilities.TryGetValue(subKey, out int score))
            return score;

        if (character.Abilities.TryGetValue(abilityKey, out int abilityScore))
            return abilityScore;

        return baseScore;
    }

    private static string ResolveSubAbilityEffect(CharacterSheet character, string subKey, int subScore)
    {
        string baseEffect;
        if (character.SubAbilityEffects.TryGetValue(subKey, out string? effect)
            && !string.IsNullOrWhiteSpace(effect))
        {
            baseEffect = effect.Trim();
        }
        else
        {
            baseEffect = SubAbilityTables.GetEffect(subKey, subScore, character.ExceptionalStrength);
        }

        if (string.Equals(subKey, "str_muscle", StringComparison.OrdinalIgnoreCase)
            && HasClassAbility(character, "fighter_concentrated_strength"))
        {
            var bendBarsMatch = Regex.Match(baseEffect, @"Bend bars:\s*(?<pct>\d+)%", RegexOptions.IgnoreCase);
            if (bendBarsMatch.Success && int.TryParse(bendBarsMatch.Groups["pct"].Value, out int percent))
            {
                int doubled = Math.Min(100, Math.Max(0, percent * 2));
                baseEffect = baseEffect.Remove(bendBarsMatch.Groups["pct"].Index, bendBarsMatch.Groups["pct"].Length)
                    .Insert(bendBarsMatch.Groups["pct"].Index, doubled.ToString(CultureInfo.InvariantCulture));
                if (!baseEffect.Contains("Concentrated Strength", StringComparison.OrdinalIgnoreCase))
                    baseEffect += " (Concentrated Strength applied)";
            }
        }

        return baseEffect;
    }

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

        foreach (string subKey in GetSubAbilityKeys(abilityKey))
        {
            int subScore = ResolveSubAbilityScore(c, abilityKey, subKey, score);
            string effect = ResolveSubAbilityEffect(c, subKey, subScore);
            bodyPanel.Children.Add(new TextBlock
            {
                Text = $"{GetSubAbilityDisplayLabel(c, subKey)}: {subScore} - {effect}",
                FontFamily = (FontFamily)FindResource("FontBody"),
                FontSize = 12,
                Foreground = (Brush)FindResource("BrushText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

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

    private Expander BuildSection(string key, string title, bool expanded, Action<StackPanel> populate)
    {
        var header = new TextBlock
        {
            Text = title,
            FontFamily  = (FontFamily)FindResource("FontBody"),
            FontSize    = 12,
            FontWeight  = FontWeights.Bold,
            Foreground  = (Brush)FindResource("BrushTitle"),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.SizeAll,
            ToolTip = "Drag to move this section",
        };

        var panel = new StackPanel { Margin = new Thickness(10, 6, 10, 8) };
        populate(panel);

        var expander = new Expander
        {
            Tag = key,
            Header     = header,
            IsExpanded = expanded,
            Content    = panel,
            Style      = (Style)FindResource("SheetExpander"),
            Margin     = new Thickness(0, 0, 0, 4),
            AllowDrop  = true,
        };

        header.AllowDrop = true;
        header.DragOver += SectionCard_DragOver;
        header.Drop += SectionCard_Drop;
        expander.DragOver += SectionCard_DragOver;
        expander.Drop += SectionCard_Drop;

        var contextMenu = new ContextMenu();
        for (int col = 1; col <= 3; col++)
        {
            int targetLane = col - 1;
            var item = new MenuItem
            {
                Header = $"Move to Column {col}",
            };
            item.Click += (_, _) => MoveSectionToColumn(key, targetLane);
            contextMenu.Items.Add(item);
        }

        contextMenu.Items.Add(new Separator());

        var hideSectionItem = new MenuItem
        {
            Header = "Hide This Section",
        };
        hideSectionItem.Click += (_, _) => HideSection(key);
        contextMenu.Items.Add(hideSectionItem);

        var showHiddenMenu = new MenuItem
        {
            Header = "Show Hidden Sections",
        };
        contextMenu.Opened += (_, _) =>
        {
            showHiddenMenu.Items.Clear();

            var hiddenCards = _sectionCards
                .Where(card => _hiddenSectionKeys.Contains(card.Key))
                .OrderBy(card => card.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (hiddenCards.Count == 0)
            {
                showHiddenMenu.Items.Add(new MenuItem
                {
                    Header = "(none)",
                    IsEnabled = false,
                });
                return;
            }

            foreach (var hiddenCard in hiddenCards)
            {
                string hiddenKey = hiddenCard.Key;
                var showItem = new MenuItem
                {
                    Header = hiddenCard.Title,
                };
                showItem.Click += (_, _) => ShowSection(hiddenKey);
                showHiddenMenu.Items.Add(showItem);
            }
        };
        contextMenu.Items.Add(showHiddenMenu);

        var resetVisibilityItem = new MenuItem
        {
            Header = "Reset Section Visibility",
        };
        resetVisibilityItem.Click += (_, _) => ResetSectionVisibility();
        contextMenu.Items.Add(resetVisibilityItem);

        contextMenu.Items.Add(new Separator());

        var debugToggleItem = new MenuItem
        {
            Header = "Show Drag Debug",
            IsCheckable = true,
            IsChecked = _dragDebugEnabled,
        };
        debugToggleItem.Click += (_, _) =>
        {
            _dragDebugEnabled = debugToggleItem.IsChecked;
            StatusText.Text = BuildStatusText(_dragDebugEnabled
                ? "Drag debug enabled."
                : "Drag debug disabled.");
        };
        contextMenu.Opened += (_, _) => debugToggleItem.IsChecked = _dragDebugEnabled;
        contextMenu.Items.Add(debugToggleItem);

        contextMenu.Items.Add(new Separator());

        for (int width = 1; width <= 3; width++)
        {
            int targetSpan = width;
            var widthItem = new MenuItem
            {
                Header = width == 1 ? "Set Width: 1 Column" : $"Set Width: {width} Columns",
            };
            widthItem.Click += (_, _) => SetSectionWidth(key, targetSpan);
            contextMenu.Items.Add(widthItem);
        }

        contextMenu.Items.Add(new Separator());

        var moveToNewRowBelow = new MenuItem
        {
            Header = "Move to New Row Below",
        };
        moveToNewRowBelow.Click += (_, _) => MoveSectionToNewRowBelowCurrent(key);
        contextMenu.Items.Add(moveToNewRowBelow);

        var moveToNewRowBottom = new MenuItem
        {
            Header = "Move to New Row Bottom",
        };
        moveToNewRowBottom.Click += (_, _) => MoveSectionToNewBottomRow(key);
        contextMenu.Items.Add(moveToNewRowBottom);

        if (string.Equals(key, "spells", StringComparison.OrdinalIgnoreCase))
        {
            contextMenu.Items.Add(new Separator());

            var spellGroupingItem = new MenuItem
            {
                Header = "Print: Keep Spells Grouped by Level",
                IsCheckable = true,
                IsChecked = _selectedCharacter is not null && GetKeepSpellsGroupedByLevelPreference(_selectedCharacter),
            };

            var blackWhiteThemeItem = new MenuItem
            {
                Header = "Print: Black & White Default",
                IsCheckable = true,
                IsChecked = _selectedCharacter is not null && GetPreferredPrintTheme(_selectedCharacter) == PrintDocumentTheme.BlackAndWhite,
            };

            spellGroupingItem.Click += (_, _) =>
            {
                if (_selectedCharacter is null)
                    return;

                bool keepGrouped = spellGroupingItem.IsChecked;
                SetKeepSpellsGroupedByLevelPreference(_selectedCharacter, keepGrouped);
                StatusText.Text = BuildStatusText(keepGrouped
                    ? "Spell print grouping: keep levels together."
                    : "Spell print grouping: allow freer page breaks.");
            };

            blackWhiteThemeItem.Click += (_, _) =>
            {
                if (_selectedCharacter is null)
                    return;

                var theme = blackWhiteThemeItem.IsChecked ? PrintDocumentTheme.BlackAndWhite : PrintDocumentTheme.Color;
                SetPreferredPrintTheme(_selectedCharacter, theme);
                StatusText.Text = BuildStatusText(blackWhiteThemeItem.IsChecked
                    ? "Print theme default: black & white."
                    : "Print theme default: color.");
            };

            contextMenu.Opened += (_, _) =>
            {
                if (_selectedCharacter is null)
                    return;
                spellGroupingItem.IsChecked = GetKeepSpellsGroupedByLevelPreference(_selectedCharacter);
                blackWhiteThemeItem.IsChecked = GetPreferredPrintTheme(_selectedCharacter) == PrintDocumentTheme.BlackAndWhite;
            };

            contextMenu.Items.Add(spellGroupingItem);
            contextMenu.Items.Add(blackWhiteThemeItem);
        }

        expander.ContextMenu = contextMenu;
        expander.PreviewMouseLeftButtonDown += SectionCard_PreviewMouseLeftButtonDown;
        expander.PreviewMouseMove += SectionCard_PreviewMouseMove;
        expander.Expanded += SectionExpander_ExpandedChanged;
        expander.Collapsed += SectionExpander_ExpandedChanged;
        return expander;
    }

    private void SectionExpander_ExpandedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander)
            return;
        if (expander.Tag is not string key || string.IsNullOrWhiteSpace(key))
            return;

        if (expander.IsExpanded)
            _expandedSectionKeys.Add(key);
        else
            _expandedSectionKeys.Remove(key);

        _hasSavedExpansionState = true;
        if (!_suspendExpansionStateSave)
            SaveCurrentSectionLayoutPreference();

        UpdateExpandCollapseToggleButton();
    }

    private void MoveSectionToColumn(string sectionKey, int targetLane)
    {
        if (!_sectionLayoutByKey.TryGetValue(sectionKey, out var placement))
            return;

        int targetRow = Math.Max(0, placement.Order);
        // Manual column moves should preserve explicit lane choice.
        MoveSectionCard(sectionKey, targetRow, targetLane, normalizeTargetRow: false);
        StatusText.Text = BuildStatusText($"Moved section to column {targetLane + 1}.");
    }

    private void MoveSectionToNewBottomRow(string sectionKey)
    {
        if (!_sectionLayoutByKey.ContainsKey(sectionKey))
            return;

        var rowSlots = BuildRowSlotsExcluding(sectionKey);
        int targetRow = rowSlots.Count == 0 ? 0 : rowSlots.Keys.Max() + 1;
        int span = GetSectionSpan(sectionKey);
        PlaceWithSpill(rowSlots, targetRow, 0, span, sectionKey);
        ApplyRowSlotsLayout(rowSlots);
        RenderSectionCards();
        SaveCurrentSectionLayoutPreference();
        StatusText.Text = BuildStatusText("Moved section to a new bottom row.");
    }

    private void MoveSectionToNewRowBelowCurrent(string sectionKey)
    {
        if (!_sectionLayoutByKey.TryGetValue(sectionKey, out var placement))
            return;

        int currentRow = Math.Max(0, placement.Order);
        var rowSlots = BuildRowSlotsExcluding(sectionKey);

        var shifted = new SortedDictionary<int, string?[]>();
        foreach (var kv in rowSlots)
        {
            int row = kv.Key >= currentRow + 1 ? kv.Key + 1 : kv.Key;
            shifted[row] = kv.Value;
        }

        int span = GetSectionSpan(sectionKey);
        PlaceWithSpill(shifted, currentRow + 1, 0, span, sectionKey);
        ApplyRowSlotsLayout(shifted);
        RenderSectionCards();
        SaveCurrentSectionLayoutPreference();
        StatusText.Text = BuildStatusText("Moved section to a new row below.");
    }

    private void SetSectionWidth(string sectionKey, int span)
    {
        if (!_sectionLayoutByKey.TryGetValue(sectionKey, out var placement))
            return;

        int newSpan = Math.Clamp(span, 1, 3);
        int row = Math.Max(0, placement.Order);
        int lane = Math.Clamp(placement.Lane, 0, 3 - newSpan);

        // Before committing the wider span, try to push any displaced occupants
        // rightward within the same row rather than letting PlaceWithSpill bump
        // them to the next row.
        for (int claimedLane = lane + 1; claimedLane < lane + newSpan; claimedLane++)
        {
            string? occupantKey = _sectionLayoutByKey
                .Where(kv => !string.Equals(kv.Key, sectionKey, StringComparison.OrdinalIgnoreCase)
                          && kv.Value.Order == row
                          && kv.Value.Lane == claimedLane)
                .Select(kv => (string?)kv.Key)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(occupantKey))
                continue;

            // Find the first free lane to the right of the expanding section.
            int rightStart = lane + newSpan;
            for (int candidate = rightStart; candidate < 3; candidate++)
            {
                bool candidateFree = !_sectionLayoutByKey.Any(kv =>
                    !string.Equals(kv.Key, sectionKey, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(kv.Key, occupantKey, StringComparison.OrdinalIgnoreCase)
                    && kv.Value.Order == row
                    && kv.Value.Lane == candidate);

                if (candidateFree)
                {
                    _sectionLayoutByKey[occupantKey].Lane = candidate;
                    _sectionLayoutByKey[occupantKey].Span = 1;
                    break;
                }
            }
        }

        placement.Span = newSpan;
        placement.Order = row;
        placement.Lane = lane;

        // Width changes should use span-aware normalization, not drag placement,
        // so 2/3-column selections persist.
        NormalizeLayoutRows();
        RenderSectionCards();
        SaveCurrentSectionLayoutPreference();
        StatusText.Text = BuildStatusText($"Set section width to {placement.Span} column{(placement.Span == 1 ? string.Empty : "s")}.");
    }

    private void BuildSpellsAtEndColumns(StackPanel panel, CharacterSheet character)
    {
        var slotSummaryLines = BuildSpellSlotSummaryLines(character);
        if (slotSummaryLines.Count > 0)
        {
            foreach (string summaryLine in slotSummaryLines)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = summaryLine,
                    FontFamily = (FontFamily)FindResource("FontBody"),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("BrushTitle"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4),
                });
            }

            panel.Children.Add(new Border { Height = 4 });
        }

        var columnHost = new WrapPanel
        {
            Margin = new Thickness(0, 2, 0, 0),
            Orientation = Orientation.Horizontal,
        };

        bool hasAnyColumns = false;

        var classBonusEntries = BuildClassBonusSpellLikeEntries(character);
        if (classBonusEntries.Count > 0)
        {
            columnHost.Children.Add(BuildClassBonusCard(classBonusEntries));
            hasAnyColumns = true;
        }

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

            columnHost.Children.Add(BuildSpellLevelColumnCard($"Priest Level {level}", slotText, spells, character));
            hasAnyColumns = true;
        }

        if (IsWizardCaster(character))
        {
            int maxArcaneLevel = GetHighestSpellLevel(character.ArcaneSpellSlots);
            var wizardSpellsByLevel = GetAccessibleWizardSpellDefinitions(character)
                .Where(s => TryParseSpellLevel(s.Level, out int level) && level <= maxArcaneLevel)
                .GroupBy(s => TryParseSpellLevel(s.Level, out int level) ? level : int.MaxValue)
                .Where(g => g.Key != int.MaxValue)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList());

            foreach (int level in wizardSpellsByLevel.Keys.Union(character.ArcaneSpellSlots.Where(kv => kv.Value > 0).Select(kv => kv.Key)).OrderBy(x => x))
            {
                int slots = character.ArcaneSpellSlots.TryGetValue(level, out int value) ? value : 0;
                wizardSpellsByLevel.TryGetValue(level, out var spells);
                spells ??= new List<SpellDefinition>();
                columnHost.Children.Add(BuildSpellLevelColumnCard($"Wizard Level {level}", $"Slots: {slots}", spells, character));
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

    private List<string> BuildSpellSlotSummaryLines(CharacterSheet character)
    {
        var lines = new List<string>();

        var priestSlots = BuildPriestSlotTotalsByLevel(character);
        if (priestSlots.Count > 0)
        {
            string priestSummary = string.Join(", ",
                priestSlots.OrderBy(kv => kv.Key)
                    .Select(kv => $"Level {kv.Key}-{kv.Value.Total}"));
            lines.Add($"Priest spell slots to cast: {priestSummary}");
        }

        if (IsWizardCaster(character))
        {
            var wizardSlots = character.ArcaneSpellSlots
                .Where(kv => kv.Value > 0)
                .OrderBy(kv => kv.Key)
                .ToList();

            if (wizardSlots.Count > 0)
            {
                string wizardSummary = string.Join(", ",
                    wizardSlots.Select(kv => $"Level {kv.Key}-{kv.Value}"));
                lines.Add($"Wizard spell slots to cast: {wizardSummary}");
            }
        }

        return lines;
    }

    private Border BuildSpellLevelColumnCard(string title, string subtitle, List<SpellDefinition> spells, CharacterSheet character)
    {
        var card = new Border
        {
            Width = double.NaN,
            MinWidth = 620,
            MaxWidth = 920,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("BrushBorder2"),
            Background = (Brush)FindResource("BrushPanel"),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
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

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
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
                Margin = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
            };

            var cell = new Border
            {
                BorderBrush = (Brush)FindResource("BrushBorder2"),
                BorderThickness = i == headers.Length - 1 ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 1),
                Padding = new Thickness(4, 2, 4, 2),
                Child = text,
            };

            Grid.SetColumn(cell, i);
            headerGrid.Children.Add(cell);
        }

        stack.Children.Add(headerGrid);

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
                stack.Children.Add(BuildSpellAtGlanceRow(spell, character));
            }
        }

        card.Child = stack;
        return card;
    }

    private UIElement BuildSpellAtGlanceRow(SpellDefinition spell, CharacterSheet character)
    {
        var content = new StackPanel();

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
            BuildSpellEffectAtGlance(spell, character),
            GetDisplayedCastTime(character, spell),
            BuildSpellDurationAtGlance(spell, character),
            GetDisplayedRange(character, spell),
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
                Margin = new Thickness(0),
            };

            var cell = new Border
            {
                BorderBrush = (Brush)FindResource("BrushBorder2"),
                BorderThickness = i == values.Length - 1 ? new Thickness(0, 0, 0, 0) : new Thickness(0, 0, 1, 0),
                Padding = new Thickness(4, 0, 4, 0),
                Child = text,
            };

            Grid.SetColumn(cell, i);
            row.Children.Add(cell);
        }

        content.Children.Add(row);

        var brief = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(spell.BriefDescription) ? "Brief: (no brief description)" : $"Brief: {spell.BriefDescription.Trim()}",
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize = 11,
            Foreground = (Brush)FindResource("BrushDim"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        };
        content.Children.Add(brief);

        return new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource("BrushBorder2"),
            Background = (Brush)FindResource("BrushPanel"),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(6, 5, 6, 5),
            Child = content,
        };
    }

    private sealed record ClassBonusSpellLikeEntry(string Name, string Usage, string Details);

    private List<ClassBonusSpellLikeEntry> BuildClassBonusSpellLikeEntries(CharacterSheet character)
    {
        var entries = new List<ClassBonusSpellLikeEntry>();
        int level = Math.Max(1, character.Level);

        if (HasClassAbility(character, "fighter_1d12_hit_points", "fighter_1d12-for-hit-points"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("1d12 for Hit Points", "Passive", "Fighter hit die becomes d12 instead of d10"));
        }

        if (HasClassAbility(character, "fighter_defense_bonus"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Defense Bonus", "Passive", "+2 AC while unarmored and unencumbered"));
        }

        if (HasClassAbility(character, "fighter_poison_resistance"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Poison Resistance", "Passive", "+1 to saving throws vs poison"));
        }

        if (HasClassAbility(character, "fighter_spell_resistance"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Spell Resistance", "Passive", "+1 to saving throws vs spells"));
        }

        if (HasClassAbility(character, "fighter_weapon_specialization"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Weapon Specialization", "Passive", "May specialize in a selected weapon (specialization CP still applies)"));
        }

        if (HasClassAbility(character, "fighter_multiple_specialization"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Multiple Specialization", "Passive", "May specialize in multiple weapons"));
        }

        if (HasClassAbility(character, "fighter_restriction_limited_armor_chain"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Limited Armor", "Restriction", "Chain mail or lighter only"));
        }

        if (HasClassAbility(character, "fighter_restriction_limited_armor_studded"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Limited Armor", "Restriction", "Studded leather or lighter only"));
        }

        if (HasClassAbility(character, "fighter_restriction_limited_armor_none"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: No Armor", "Restriction", "No armor may be worn"));
        }

        if (HasClassAbility(character, "fighter_restriction_limited_weapon_selection"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Limited Weapon Selection", "Restriction", "Weapon choices are restricted by selected package"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "fighter_restriction_limited_magical_item_use"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Limited Magical Item Use", "Restriction", $"Barred category: {selection}"));
        }

        if (HasClassAbility(character, "fighter_restriction_limited_magical_item_use")
            && !GetClassAbilitySelections(character, "fighter_restriction_limited_magical_item_use").Any())
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Limited Magical Item Use", "Restriction", "One magical item category is barred"));
        }

        if (HasClassAbility(character, "cleric_detect_evil"))
            entries.Add(new ClassBonusSpellLikeEntry("Detect Evil", "1/day", "Bonus spell"));

        if (HasClassAbility(character, "druid_bonus_spell"))
            entries.Add(new ClassBonusSpellLikeEntry("Animal Friendship", "1/day", "Bonus spell"));

        if (HasClassAbility(character, "cleric_identify_plants_animals_cp8") && level >= 1)
            entries.Add(new ClassBonusSpellLikeEntry("Identify Plants and Animals", "Available", "Granted at level 1 (8 CP option)"));
        if (HasClassAbility(character, "cleric_identify_plants_animals_cp5") && level >= 3)
            entries.Add(new ClassBonusSpellLikeEntry("Identify Plants and Animals", "Available", "Granted at level 3 (5 CP option)"));

        if (HasClassAbility(character, "cleric_pass_without_trace_cp7") && level >= 1)
            entries.Add(new ClassBonusSpellLikeEntry("Pass without Trace", "Available", "Granted at level 1 (7 CP option)"));
        else if (HasClassAbility(character, "cleric_pass_without_trace_cp5") && level >= 3)
            entries.Add(new ClassBonusSpellLikeEntry("Pass without Trace", "Available", "Granted at level 3 (5 CP option)"));

        if (HasClassAbility(character, "cleric_purify_water") && level >= 1)
            entries.Add(new ClassBonusSpellLikeEntry("Purify Water", "1/day", "Bonus spell at level 1"));

        if (HasClassAbility(character, "cleric_know_alignment_cp10", "cleric_know_alignment_cp15"))
        {
            int castsPerDay = Math.Max(1, (level + 1) / 2);
            entries.Add(new ClassBonusSpellLikeEntry("Know Alignment", $"{castsPerDay}/day", "Bonus spell scales by level"));
        }

        if (HasClassAbility(character, "cleric_lay_on_hands"))
        {
            int pool = level * 2;
            entries.Add(new ClassBonusSpellLikeEntry("Lay on Hands", $"{pool} HP/day", "Healing pool = 2 HP per level"));
        }

        if (HasClassAbility(character, "paladin_healing"))
        {
            int pool = level * 2;
            entries.Add(new ClassBonusSpellLikeEntry("Lay on Hands", $"{pool} HP/day", "Paladin healing pool = 2 HP per level"));
        }

        if (HasClassAbility(character, "paladin_curative"))
        {
            int usesPerWeek = Math.Max(1, (level + 4) / 5);
            entries.Add(new ClassBonusSpellLikeEntry("Cure Disease", $"{usesPerWeek}/week", "Uses scale by level: ceil(level / 5)"));
        }

        if (HasClassAbility(character, "ranger_climbing"))
        {
            int climbChance = GetRangerClimbChance(level);
            entries.Add(new ClassBonusSpellLikeEntry("Climbing", "Passive", $"{climbChance}% chance on natural formations"));
        }

        if (HasClassAbility(character, "ranger_detect_noise"))
        {
            int chance = GetRogueDefaultChartChance("detect_noise", level);
            entries.Add(new ClassBonusSpellLikeEntry("Detect Noise", "Passive", $"{chance}% chance (rogue default chart, same-level)"));
        }

        if (HasClassAbility(character, "ranger_find_remove_wilderness_traps"))
        {
            int chance = GetRogueDefaultChartChance("find_remove_traps", level);
            entries.Add(new ClassBonusSpellLikeEntry("Find/Remove Wilderness Traps", "Passive", $"{chance}% chance (rogue default chart, wilderness only)"));
        }

        if (HasClassAbility(character, "ranger_pass_without_trace"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Pass without Trace", "1/day", "Ranger granted ability"));
        }

        if (HasClassAbility(character, "ranger_speak_with_animals"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Speak with Animals", "1/day", "Ranger granted ability"));
        }

        if (HasClassAbility(character, "ranger_tracking_proficiency"))
        {
            int trackingBonus = GetRangerTrackingBonus(level);
            entries.Add(new ClassBonusSpellLikeEntry("Tracking", "Passive", $"NWP granted; +{trackingBonus} tracking bonus"));
        }

        if (HasClassAbility(character, "ranger_two_weapon_style"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Two-Weapon Style", "Passive", "Off-hand attack penalties removed"));
        }

        if (HasClassAbility(character, "ranger_weapon_specialization"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Weapon Specialization", "Passive", "Ranger may specialize in weapons (per proficiency rules)"));
        }

        if (HasClassAbility(character, "ranger_sneak_attack"))
        {
            int multiplier = GetBackstabMultiplier(level);
            entries.Add(new ClassBonusSpellLikeEntry("Sneak Attack", "Passive", $"Backstab-style damage multiplier x{multiplier}"));
        }

        if (HasClassAbility(character, "thief_backstab"))
        {
            int multiplier = GetBackstabMultiplier(level);
            entries.Add(new ClassBonusSpellLikeEntry("Backstab", "Passive", $"Damage multiplier x{multiplier}"));
        }

        if (HasClassAbility(character, "thief_thieves_cant"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Thieves' Cant", "Passive", "Can communicate using coded underworld slang"));
        }

        if (HasClassAbility(character, "thief_tunneling"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Tunneling", "Passive", "Can excavate tunnels with terrain-dependent checks"));
        }

        if (HasClassAbility(character, "thief_weapon_specialization"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Weapon Specialization", "Passive", "Thief may specialize in one weapon (proficiency cost applies)"));
        }

        if (CharacterHasClass(character, "psionicist"))
        {
            if (HasClassAbility(character, "psionicist_extra_devotion"))
            {
                entries.Add(new ClassBonusSpellLikeEntry("Extra Devotion", "Passive", "+1 devotion known at character creation"));
            }

            if (HasClassAbility(character, "psionicist_extra_science"))
            {
                entries.Add(new ClassBonusSpellLikeEntry("Extra Science", "Passive", "+1 science known at character creation"));
            }

            if (HasClassAbility(character, "psionicist_extra_discipline"))
            {
                entries.Add(new ClassBonusSpellLikeEntry("Extra Discipline", "Passive", "+1 discipline access at character creation"));
            }

            if (HasClassAbility(character, "psionicist_defense_mode_bonus"))
            {
                entries.Add(new ClassBonusSpellLikeEntry("Defense Mode Bonus", "Passive", "Know one additional defense mode at level 1"));
            }

            int ppModifierPerLevel = GetPsionicPowerPointModifierPerLevel(character);
            if (ppModifierPerLevel != 0)
            {
                int totalModifier = ppModifierPerLevel * level;
                string sign = ppModifierPerLevel >= 0 ? "+" : string.Empty;
                string totalSign = totalModifier >= 0 ? "+" : string.Empty;
                entries.Add(new ClassBonusSpellLikeEntry(
                    "Power Point Progression",
                    "Progression",
                    $"{sign}{ppModifierPerLevel} PP/level ({totalSign}{totalModifier} at level {level})"));
            }

            if (HasClassAbility(character, "psionicist_power_point_recovery"))
            {
                entries.Add(new ClassBonusSpellLikeEntry("Rapid Recovery", "Passive", "Power points recover at x2 normal rest rate"));
            }

            foreach (string selection in GetClassAbilitySelections(character, "psionicist_discipline_mastery"))
            {
                entries.Add(new ClassBonusSpellLikeEntry("Discipline Mastery", "Passive", $"Primary discipline: {selection}; related devotion costs reduced by 1 PP (minimum 1)"));
            }

            if (HasClassAbility(character, "psionicist_discipline_mastery")
                && !GetClassAbilitySelections(character, "psionicist_discipline_mastery").Any())
            {
                entries.Add(new ClassBonusSpellLikeEntry("Discipline Mastery", "Passive", "Choose one primary discipline; related devotion costs reduced by 1 PP (minimum 1)"));
            }

            foreach (string selection in GetClassAbilitySelections(character, "psionicist_restriction_one_discipline"))
            {
                entries.Add(new ClassBonusSpellLikeEntry("Restriction: Single Discipline", "Restriction", $"Locked to {selection} until higher-level unlock"));
            }

            if (HasClassAbility(character, "psionicist_restriction_one_discipline")
                && !GetClassAbilitySelections(character, "psionicist_restriction_one_discipline").Any())
            {
                entries.Add(new ClassBonusSpellLikeEntry("Restriction: Single Discipline", "Restriction", "Locked to one primary discipline until higher-level unlock"));
            }

            if (HasClassAbility(character, "psionicist_restriction_no_combat_modes"))
            {
                entries.Add(new ClassBonusSpellLikeEntry("Restriction: No Combat Modes", "Restriction", "Psionic attack/combat modes disabled; contact-based use only"));
            }
        }

        if (HasClassAbility(character, "wizard_access_to_schools"))
        {
            var schools = GetSelectedWizardSchools(character).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            string details = schools.Count > 0
                ? $"Access selected: {string.Join(", ", schools)}"
                : "Access purchased per selected school";
            entries.Add(new ClassBonusSpellLikeEntry("Wizard School Access", "Passive", details));
        }

        if (TryGetWizardSpecialization(character, out var spec))
        {
            string mainSchool = PrimarySchoolBySpecializationId.TryGetValue(spec.Id, out string? school)
                && !string.IsNullOrWhiteSpace(school)
                ? school
                : spec.Name;
            string opposed = spec.OppositionSchools.Count > 0
                ? string.Join(", ", spec.OppositionSchools)
                : "none";
            entries.Add(new ClassBonusSpellLikeEntry("Wizard Specialist", "Passive", $"{spec.Name}: primary school {mainSchool}; opposition schools {opposed}"));
            entries.Add(new ClassBonusSpellLikeEntry("Specialist Bonus Spell", "Progression", $"+1 arcane slot per spell level (specialist school use)"));
        }

        foreach (string schoolAccessAbilityId in WizardSchoolAccessByAbilityId.Keys)
        {
            foreach (string school in GetClassAbilitySelections(character, schoolAccessAbilityId))
            {
                if (string.IsNullOrWhiteSpace(school))
                    continue;

                entries.Add(new ClassBonusSpellLikeEntry("Wizard School Access", "Passive", $"Access to {school}"));
            }

            if (!HasClassAbility(character, schoolAccessAbilityId))
                continue;

            string mappedSchool = WizardSchoolAccessByAbilityId[schoolAccessAbilityId];
            entries.Add(new ClassBonusSpellLikeEntry("Wizard School Access", "Passive", $"Access to {mappedSchool}"));
        }

        if (HasClassAbility(character, "wizard_armored_wizard"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Armored Wizard", "Passive", "Wizard can cast in selected armor profile"));
        }

        if (HasClassAbility(character, "wizard_automatic_spells"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Automatic Spells", "Progression", "Gain one automatic spell when each new spell level unlocks"));
        }

        if (HasClassAbility(character, "wizard_bonus_spells_cp10", "wizard_bonus_spells_cp15"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Bonus Spells", "Passive", "+1 memorization slot for each spell level with base wizard slots"));
        }

        if (HasClassAbility(character, "wizard_casting_reduction"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Casting Reduction", "Passive", "Arcane casting times reduced by 1 (minimum 1)"));
        }

        if (HasClassAbility(character, "wizard_combat_bonus"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Combat Bonus", "Passive", "Uses rogue THAC0 progression"));
        }

        if (HasClassAbility(character, "wizard_constitution_adjustment", "wizard_warrior_hit_point_bonus"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Constitution Adjustment", "Passive", "Uses warrior Constitution hit point bonus progression"));
        }

        if (HasClassAbility(character, "wizard_detect_magic"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Detect Magic", $"{level}/day", "Line-of-sight magical radiation detection"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_dispel_cp10"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Dispel", "1/day", $"Chosen school/category: {selection}"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_dispel_cp15"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Dispel", "2/day", $"Chosen school/category: {selection}"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_enhanced_casting_level"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Enhanced Casting Level", "1/day", $"Chosen school: {selection}; cast as +1d4 effective levels"));
        }

        if (HasClassAbility(character, "wizard_extend_duration"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Extend Duration", "Passive", $"Non-instantaneous arcane spell durations gain +{level}"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_immunity"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Immunity", "Passive", $"Complete immunity to {selection}"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_learning_bonus_cp5"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Learning Bonus", "Passive", $"{selection} spells are learned at +15%"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_learning_bonus_cp7"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Learning Bonus", "Passive", $"{selection} spells are learned at +25%"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_no_components_cp5", "wizard_no_components_cp8"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("No Components", "Passive", $"{selection} requires no material components"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_persistent_spell_effect"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Persistent Spell Effect", "Passive", $"{selection} can be maintained persistently while concentrating"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_priestly_wizard_cp10"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Priestly Wizard", "Passive", $"Minor access to {selection}; common and uncommon spells only, cast with wizard slots"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_priestly_wizard_cp15"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Priestly Wizard", "Passive", $"Major access to {selection}; common and uncommon spells only, cast with wizard slots"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_proficiency_crossovers"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Proficiency Crossover", "Passive", $"{selection} nonweapon proficiencies ignore crossover penalty"));
        }

        if (HasClassAbility(character, "wizard_range_increase_cp7"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Range Increase", "Passive", "Arcane spell ranges increased by 50% (numeric ranges only)"));
        }
        else if (HasClassAbility(character, "wizard_range_increase_cp5"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Range Increase", "Passive", "Arcane spell ranges increased by 25% (numeric ranges only)"));
        }

        if (HasClassAbility(character, "wizard_read_magic"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Read Magic", $"{level}/day", "Extra daily uses scale by level"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_research_bonus"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Research Bonus", "Passive", $"{selection} spells count as one level lower for research"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_spell_focus"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Spell Focus", "Passive", $"{selection} spells display at +1 level in spell review"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_thief_ability"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Wizard Thief Ability", "Passive", $"Rogue base-chart ability: {selection}"));
        }

        if (HasClassAbility(character, "wizard_weapon_selection_cp10"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Weapon Selection (CP10)", "Passive", "Wizard may select rogue-tier weapons"));
        }

        if (HasClassAbility(character, "wizard_weapon_selection_cp15"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Weapon Selection (CP15)", "Passive", "Wizard may select cleric weapons"));
        }

        if (HasClassAbility(character, "wizard_weapon_specialization"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Weapon Specialization", "Passive", "Wizard may specialize in one weapon"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_restriction_awkward_casting_method"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Awkward Casting", "Restriction", selection));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_signature_spell_levels_1_3", "wizard_signature_spell_levels_4_6", "wizard_signature_spell_levels_7_9"))
        {
            string detail = TryGetArcaneSpellLevelByName(selection, out int signatureLevel)
                ? $"Level {signatureLevel}: {selection}"
                : selection;
            entries.Add(new ClassBonusSpellLikeEntry("Signature Spell", "Passive", detail));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_restriction_behavior_taboo"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Behavior/Taboo", "Restriction", selection));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_restriction_difficult_memorization"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Difficult Memorization", "Restriction", selection));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_restriction_environmental_condition"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Environmental Condition", "Restriction", selection));
        }

        if (HasClassAbility(character, "wizard_restriction_hazardous_spells"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Hazardous Spells", "Restriction", "On cast risk: save vs breath or suffer backlash/instability"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_restriction_learning_penalty"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Learning Penalty", "Restriction", $"Favored school: {selection}; other schools suffer learn chance penalty"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_restriction_limited_magical_item_use"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Limited Magical Item Use", "Restriction", $"Barred category: {selection}"));
        }

        if (HasClassAbility(character, "wizard_restriction_reduced_spell_knowledge"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Reduced Spell Knowledge", "Restriction", "Maximum known spells per level is halved"));
        }

        if (HasClassAbility(character, "wizard_restriction_reduced_spell_progression"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Reduced Spell Progression", "Restriction", "Arcane slots reduced by 1 at each spell level where slots exist"));
        }

        if (HasClassAbility(character, "wizard_restriction_slower_casting_time_cp2"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Slower Casting (CP2)", "Restriction", "Arcane casts under 1 round: +3; 1 round or more: doubled"));
        }

        if (HasClassAbility(character, "wizard_restriction_slower_casting_time_cp5"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Slower Casting (CP5)", "Restriction", "Arcane casts under 1 round become 1 round; rounds escalate to turns"));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_restriction_supernatural_constraint"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Supernatural Constraint", "Restriction", selection));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_restriction_talisman"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Talisman", "Restriction", selection));
        }

        foreach (string selection in GetClassAbilitySelections(character, "wizard_restriction_weapons_restriction_cp3"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Weapons (CP3)", "Restriction", $"Allowed weapons only: {selection}"));
        }

        if (HasClassAbility(character, "wizard_restriction_weapons_restriction_cp5"))
        {
            entries.Add(new ClassBonusSpellLikeEntry("Restriction: Weapons (CP5)", "Restriction", "No weapon proficiencies allowed"));
        }

        if (HasClassAbility(character, "fighter_mythic_lore"))
        {
            int chance = Math.Max(5, level * 5);
            entries.Add(new ClassBonusSpellLikeEntry("Mythic Lore", "Passive", $"{chance}% chance to know item history"));
        }

        foreach (var selectionEntry in character.SelectedClassAbilityIds
            .Where(entry => string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry), "cleric_spell_like_granted_power", StringComparison.OrdinalIgnoreCase)))
        {
            if (!RulesEngine.TryParseSpellLikeGrantedPowerSelection(RulesEngine.ExtractClassAbilityPlayerText(selectionEntry), out var selection))
                continue;

            string usage = selection.IsDaily
                ? $"{selection.UsesPerDay}/day"
                : "1/week";
            string details = $"{(string.Equals(selection.SpellType, "wizard", StringComparison.OrdinalIgnoreCase) ? "Wizard" : "Priest")} {selection.SpellLevel}";
            entries.Add(new ClassBonusSpellLikeEntry(selection.SpellName, usage, details));
        }

        return entries;
    }

    private static int GetRangerClimbChance(int level)
    {
        int clampedLevel = Math.Max(1, level);
        // Ranger climbing progression: starts competent and scales steadily by level.
        return Math.Min(95, 40 + ((clampedLevel - 1) * 5));
    }

    private static int GetRogueDefaultChartChance(string skillId, int level)
    {
        int clampedLevel = Math.Max(1, level);
        int baseScore = RulesEngine.GetRogueSkillBaseScore(skillId);
        // Use a deterministic same-level rogue default progression for passive ranger skill grants.
        return Math.Clamp(baseScore + ((clampedLevel - 1) * 5), 0, 95);
    }

    private static int GetBackstabMultiplier(int level)
    {
        int clampedLevel = Math.Max(1, level);
        if (clampedLevel <= 4) return 2;
        if (clampedLevel <= 8) return 3;
        if (clampedLevel <= 12) return 4;
        if (clampedLevel <= 16) return 5;
        return 6;
    }

    private static int GetRangerTrackingBonus(int level)
    {
        return Math.Max(1, level);
    }

    private static int GetPsionicPowerPointModifierPerLevel(CharacterSheet character)
    {
        int modifier = 0;

        if (HasClassAbility(character, "psionicist_extra_power_points_5"))
            modifier += 5;

        if (HasClassAbility(character, "psionicist_extra_power_points_10"))
            modifier += 10;

        if (HasClassAbility(character, "psionicist_restriction_reduced_pp"))
            modifier -= 3;

        return modifier;
    }

    private static bool HasClassAbility(CharacterSheet character, params string[] abilityIds)
    {
        if (abilityIds is null || abilityIds.Length == 0)
            return false;

        var idSet = new HashSet<string>(abilityIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(NormalizeClassAbilityId), StringComparer.OrdinalIgnoreCase);
        if (idSet.Count == 0)
            return false;

        var selectedIds = EnumerateSelectedClassAbilityEntries(character)
            .Select(RulesEngine.ExtractClassAbilityBaseId)
            .Select(NormalizeClassAbilityId)
            .Where(id => !string.IsNullOrWhiteSpace(id));

        if (selectedIds.Any(idSet.Contains))
            return true;

        int level = Math.Max(1, character.Level);
        return character.StructuredAbilities
            .Where(ab => RulesEngine.AbilityUnlockLevel(ab) <= level)
            .Any(ab => idSet.Contains(NormalizeClassAbilityId(ab.Id)));
    }

    private List<string> GetDisplayedAbilityLines(CharacterSheet character)
    {
        int level = Math.Max(1, character.Level);
        var lines = character.StructuredAbilities
            .Where(ab => RulesEngine.AbilityUnlockLevel(ab) <= level)
            .Select(FormatAbilityLine)
            .ToList();

        var seenBaseIds = new HashSet<string>(character.StructuredAbilities
            .Where(ab => RulesEngine.AbilityUnlockLevel(ab) <= level)
            .Select(ab => NormalizeClassAbilityId(ab.Id)), StringComparer.OrdinalIgnoreCase);
        var seenSelectionEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string selectionEntry in EnumerateSelectedClassAbilityEntries(character))
        {
            if (!seenSelectionEntries.Add(selectionEntry))
                continue;

            string baseId = NormalizeClassAbilityId(RulesEngine.ExtractClassAbilityBaseId(selectionEntry));
            if (string.IsNullOrWhiteSpace(baseId) || seenBaseIds.Contains(baseId))
                continue;

            string playerText = RulesEngine.ExtractClassAbilityPlayerText(selectionEntry);
            string? description = null;

            if (_app.Rules.ClassAbilityLibrary.TryGetValue(baseId, out var abilityDef)
                && !string.IsNullOrWhiteSpace(abilityDef.Description))
            {
                description = abilityDef.Description;
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                string displayName = HumanizeAbilityId(baseId);
                description = string.IsNullOrWhiteSpace(playerText)
                    ? displayName
                    : $"{displayName}: {playerText}";
            }
            else if (!string.IsNullOrWhiteSpace(playerText))
            {
                description = $"{description} Selection: {playerText}";
            }

            lines.Add(description);
            seenBaseIds.Add(baseId);
        }

        return lines;
    }

    private static IEnumerable<string> EnumerateSelectedClassAbilityEntries(CharacterSheet character)
    {
        foreach (string selectionEntry in character.SelectedClassAbilityIds)
        {
            if (!string.IsNullOrWhiteSpace(selectionEntry))
                yield return selectionEntry;
        }

        foreach (var (_, entries) in character.SelectedAbilitiesByClass)
        {
            if (entries is null)
                continue;

            foreach (string selectionEntry in entries)
            {
                if (!string.IsNullOrWhiteSpace(selectionEntry))
                    yield return selectionEntry;
            }
        }
    }

    private static string FormatAbilityLine(AbilityDefinition ability)
    {
        string name = ability.Id.Replace("_", " ");
        string desc = string.IsNullOrWhiteSpace(ability.Description) ? string.Empty : $": {ability.Description}";
        return $"{name}{desc}";
    }

    private static string HumanizeAbilityId(string abilityId)
    {
        if (string.IsNullOrWhiteSpace(abilityId))
            return string.Empty;

        var parts = abilityId
            .Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (parts.Count > 1 && IsKnownClassToken(parts[0]))
            parts.RemoveAt(0);

        if (parts.Count == 0)
            return abilityId;

        return string.Join(" ", parts);
    }

    private static string NormalizeClassAbilityId(string abilityId)
    {
        string normalized = (abilityId ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_');
        if (normalized == "fighter_1d12_for_hit_points")
            return "fighter_1d12_hit_points";
        return normalized;
    }

    private static bool IsKnownClassToken(string token)
    {
        return token.Equals("fighter", StringComparison.OrdinalIgnoreCase)
            || token.Equals("paladin", StringComparison.OrdinalIgnoreCase)
            || token.Equals("ranger", StringComparison.OrdinalIgnoreCase)
            || token.Equals("cleric", StringComparison.OrdinalIgnoreCase)
            || token.Equals("druid", StringComparison.OrdinalIgnoreCase)
            || token.Equals("wizard", StringComparison.OrdinalIgnoreCase)
            || token.Equals("mage", StringComparison.OrdinalIgnoreCase)
            || token.Equals("illusionist", StringComparison.OrdinalIgnoreCase)
            || token.Equals("thief", StringComparison.OrdinalIgnoreCase)
            || token.Equals("bard", StringComparison.OrdinalIgnoreCase);
    }

    private Border BuildClassBonusCard(List<ClassBonusSpellLikeEntry> entries)
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
            Text = "CLASS BONUS SPELLS / POWERS",
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BrushTitle"),
        });

        foreach (var entry in entries)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"- {entry.Name}: {entry.Usage} ({entry.Details})",
                FontFamily = (FontFamily)FindResource("FontBody"),
                FontSize = 12,
                Foreground = (Brush)FindResource("BrushText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0),
            });
        }

        card.Child = stack;
        return card;
    }

    private string BuildSpellDurationAtGlance(SpellDefinition spell, CharacterSheet character)
    {
        string raw = string.IsNullOrWhiteSpace(spell.Duration) ? "-" : spell.Duration.Trim();
        if (raw == "-")
            return raw;

        if (IsInstantaneousDuration(raw))
            return raw;

        double multiplier = GetExtendedDurationMultiplier(character);
        int baseCasterLevel = Math.Max(1, character.Level);
        int casterLevel = GetEffectiveCasterLevelForSpell(character, spell);
        int durationLevelBonus = (!IsDivineSpell(spell) && HasClassAbility(character, "wizard_extend_duration"))
            ? Math.Max(1, character.Level)
            : 0;
        var perLevelRegex = new Regex(@"(?<value>\d+(?:\.\d+)?)\s*(?<unit>rounds?|rds?\.?|rd\.?|turns?|turn\.?|hours?|hrs?\.?|hr\.?|days?)\s*/\s*level", RegexOptions.IgnoreCase);
        var match = perLevelRegex.Match(raw);
        string multLabel = multiplier >= 2.0 ? "x2" : multiplier > 1.0 ? "x1.5" : string.Empty;

        if (match.Success && double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double amountPerLevel))
        {
            double scaled = (amountPerLevel * casterLevel * multiplier) + durationLevelBonus;
            string normalizedUnit = NormalizeDurationUnit(match.Groups["unit"].Value, scaled);
            string scaledText = Math.Abs(scaled - Math.Round(scaled)) < 0.001
                ? ((int)Math.Round(scaled)).ToString(CultureInfo.InvariantCulture)
                : scaled.ToString("0.##", CultureInfo.InvariantCulture);
            var modifiers = new List<string>();
            if (casterLevel > baseCasterLevel)
                modifiers.Add($"focus L{casterLevel}");
            if (!string.IsNullOrWhiteSpace(multLabel))
                modifiers.Add(multLabel);
            if (durationLevelBonus > 0)
                modifiers.Add($"+{durationLevelBonus} dur");
            string modifierText = modifiers.Count == 0 ? string.Empty : $" ({string.Join(", ", modifiers)})";
            return $"{raw} => {scaledText} {normalizedUnit} at L{casterLevel}{modifierText}";
        }

        if (multiplier <= 1.0 && durationLevelBonus <= 0)
            return raw;

        var suffixes = new List<string>();
        if (!string.IsNullOrWhiteSpace(multLabel))
            suffixes.Add($"{multLabel} extended");
        if (durationLevelBonus > 0)
            suffixes.Add($"+{durationLevelBonus} duration");
        return $"{raw} ({string.Join(", ", suffixes)})";
    }

    private int GetEffectiveCasterLevelForSpell(CharacterSheet character, SpellDefinition spell)
    {
        int casterLevel = Math.Max(1, character.Level);
        if (!IsDivineSpell(spell))
            return casterLevel;

        if (HasClassAbility(character, "ranger_increased_spell_power")
            && CharacterHasClass(character, "ranger"))
        {
            casterLevel = Math.Max(1, character.Level - 4);
        }

        if (HasClassAbility(character, "druid_elemental_spell_bonus")
            && IsElementalSphereSpell(spell))
        {
            casterLevel += 1;
        }

        string focusedSphere = GetFocusedPriestSphere(character);
        if (string.IsNullOrWhiteSpace(focusedSphere))
            return casterLevel;

        var spellSpheres = ParseSpellSchoolTokens(spell.Schools);
        if (spellSpheres.Contains(focusedSphere))
            return casterLevel + 1;

        return casterLevel;
    }

    private static bool IsElementalSphereSpell(SpellDefinition spell)
    {
        if (spell is null || string.IsNullOrWhiteSpace(spell.Schools))
            return false;

        foreach (string sphere in ParseSpellSchoolTokens(spell.Schools))
        {
            if (sphere.Contains("Elemental", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string GetFocusedPriestSphere(CharacterSheet character)
    {
        foreach (var selectionEntry in character.SelectedClassAbilityIds)
        {
            if (!string.Equals(RulesEngine.ExtractClassAbilityBaseId(selectionEntry), "cleric_sphere_focus_bonus", StringComparison.OrdinalIgnoreCase))
                continue;

            string playerText = RulesEngine.FormatClassAbilitySelectionText(
                "cleric_sphere_focus_bonus",
                RulesEngine.ExtractClassAbilityPlayerText(selectionEntry));
            if (!string.IsNullOrWhiteSpace(playerText))
                return playerText;
        }

        return string.Empty;
    }

    private static IEnumerable<string> GetClassAbilitySelections(CharacterSheet character, params string[] abilityIds)
    {
        if (abilityIds is null || abilityIds.Length == 0)
            yield break;

        var wanted = new HashSet<string>(abilityIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(NormalizeClassAbilityId), StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
            yield break;

        foreach (var selectionEntry in EnumerateSelectedClassAbilityEntries(character))
        {
            string baseId = NormalizeClassAbilityId(RulesEngine.ExtractClassAbilityBaseId(selectionEntry));
            if (!wanted.Contains(baseId))
                continue;

            string value = RulesEngine.FormatClassAbilitySelectionText(baseId, RulesEngine.ExtractClassAbilityPlayerText(selectionEntry));
            if (string.IsNullOrWhiteSpace(value))
                continue;

            yield return value;
        }
    }

    private static string GetDisplayedCastTime(CharacterSheet character, SpellDefinition spell)
    {
        string raw = string.IsNullOrWhiteSpace(spell.CastTime) ? "-" : spell.CastTime.Trim();
        if (raw == "-")
            return raw;

        if (IsDivineSpell(spell)
            && HasClassAbility(character, "cleric_restriction_slower_casting_times", "druid_restriction_slower_casting_times"))
            return RulesEngine.AdjustSpellCastTimeForSlowerCasting(raw);

        if (!IsDivineSpell(spell)
            && HasClassAbility(character, "wizard_restriction_slower_casting_time_cp5"))
        {
            return RulesEngine.AdjustSpellCastTimeToNextTimeUnit(raw);
        }

        if (!IsDivineSpell(spell)
            && HasClassAbility(character, "wizard_restriction_slower_casting_time_cp2"))
        {
            return RulesEngine.AdjustSpellCastTimeForSlowerCasting(raw);
        }

        if (!IsDivineSpell(spell)
            && HasClassAbility(character, "wizard_casting_reduction"))
        {
            return AdjustSpellCastTimeForReduction(raw);
        }

        return raw;
    }

    private static string AdjustSpellCastTimeForReduction(string rawCastTime)
    {
        string castTime = (rawCastTime ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(castTime))
            return string.Empty;

        var match = Regex.Match(castTime, @"\d+");
        if (match.Success && int.TryParse(match.Value, out int value))
        {
            int adjusted = Math.Max(1, value - 1);
            return castTime.Remove(match.Index, match.Length)
                .Insert(match.Index, adjusted.ToString(CultureInfo.InvariantCulture));
        }

        return castTime;
    }

    private static string GetDisplayedRange(CharacterSheet character, SpellDefinition spell)
    {
        string raw = string.IsNullOrWhiteSpace(spell.Range) ? "-" : spell.Range.Trim();
        if (raw == "-" || IsDivineSpell(spell))
            return raw;

        double multiplier = GetWizardRangeMultiplier(character);
        if (multiplier <= 1.0)
            return raw;

        var match = Regex.Match(raw, @"(?<value>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double rangeValue))
            return raw;

        double scaled = rangeValue * multiplier;
        string scaledText = Math.Abs(scaled - Math.Round(scaled)) < 0.001
            ? ((int)Math.Round(scaled)).ToString(CultureInfo.InvariantCulture)
            : scaled.ToString("0.##", CultureInfo.InvariantCulture);

        string multiplierLabel = multiplier >= 1.5 ? "x1.5" : "x1.25";
        return raw.Remove(match.Index, match.Length).Insert(match.Index, scaledText) + $" ({multiplierLabel} range)";
    }

    private static double GetWizardRangeMultiplier(CharacterSheet character)
    {
        if (HasClassAbility(character, "wizard_range_increase_cp7"))
            return 1.5;
        if (HasClassAbility(character, "wizard_range_increase_cp5"))
            return 1.25;
        return 1.0;
    }

    private static bool IsDivineSpell(SpellDefinition spell)
        => string.Equals(spell.Category, "divine", StringComparison.OrdinalIgnoreCase)
           || string.Equals(spell.Category, "priest", StringComparison.OrdinalIgnoreCase);

    private static bool IsInstantaneousDuration(string duration)
        => duration.Contains("instant", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDurationUnit(string unitToken, double value)
    {
        string unit = unitToken.Trim().TrimEnd('.').ToLowerInvariant();
        bool plural = Math.Abs(value - 1.0) > 0.001;

        if (unit is "rd" or "rds" or "round" or "rounds")
            return plural ? "rounds" : "round";
        if (unit is "turn" or "turns")
            return plural ? "turns" : "turn";
        if (unit is "hr" or "hrs" or "hour" or "hours")
            return plural ? "hours" : "hour";
        if (unit is "day" or "days")
            return plural ? "days" : "day";
        return plural ? unit + "s" : unit;
    }

    private static double GetExtendedDurationMultiplier(CharacterSheet character)
    {
        if (HasClassAbility(character, "cleric_extended_spell_duration_cp15"))
            return 2.0;
        if (HasClassAbility(character, "cleric_extended_spell_duration_cp10"))
            return 1.5;
        return 1.0;
    }

    private Dictionary<int, (int Base, int WisdomBonus, int Total)> BuildPriestSlotTotalsByLevel(CharacterSheet character)
    {
        var totals = new Dictionary<int, (int Base, int WisdomBonus, int Total)>();
        bool hasReducedProgression = HasClassAbility(character, "cleric_restriction_reduced_spell_progression")
            || HasClassAbility(character, "druid_restriction_reduced_spell_progression");
        var wisBonusSlots = GetPriestWisdomBonusSlots(character.Abilities.GetValueOrDefault("wis", 10));

        foreach (var level in character.DivineSpellSlots.Keys.OrderBy(x => x))
        {
            int baseSlots = character.DivineSpellSlots.TryGetValue(level, out int baseValue) ? baseValue : 0;
            if (hasReducedProgression)
                baseSlots = Math.Max(0, baseSlots - 1);

            int bonusSlots = 0;
            if (baseSlots > 0)
                bonusSlots = wisBonusSlots.TryGetValue(level, out int bonusValue) ? bonusValue : 0;

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

    private (Grid host, StackPanel left, StackPanel right) CreateTwoColumnPanels()
    {
        var host = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel();
        var right = new StackPanel();
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 2);
        host.Children.Add(left);
        host.Children.Add(right);

        return (host, left, right);
    }

    private void AddTwoColumnItems(StackPanel parent, List<string> items)
    {
        if (items.Count <= 1)
        {
            foreach (string item in items)
                AddItem(parent, item);
            return;
        }

        var (host, left, right) = CreateTwoColumnPanels();
        int splitIndex = (items.Count + 1) / 2;

        for (int i = 0; i < items.Count; i++)
        {
            AddItem(i < splitIndex ? left : right, items[i]);
        }

        parent.Children.Add(host);
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

    private int GetDisplayedWizardSpellLevelForReview(CharacterSheet character, SpellDefinition spell, int maxArcaneLevel)
    {
        if (!TryParseSpellLevel(spell.Level, out int baseLevel) || baseLevel > maxArcaneLevel)
            return int.MaxValue;

        string focusSchool = GetWizardSpellFocusSchool(character);
        if (string.IsNullOrWhiteSpace(focusSchool))
            return baseLevel;

        string normalizedFocus = NormalizeWizardSchoolName(focusSchool);
        if (string.IsNullOrWhiteSpace(normalizedFocus))
            return baseLevel;

        var spellSchools = ParseSpellSchoolTokens(spell.Schools);
        if (spellSchools.Contains(normalizedFocus))
            return Math.Min(9, baseLevel + 1);

        return baseLevel;
    }

    private static string GetWizardSpellFocusSchool(CharacterSheet character)
    {
        foreach (string selection in GetClassAbilitySelections(character, "wizard_spell_focus"))
        {
            if (!string.IsNullOrWhiteSpace(selection))
                return selection;
        }

        return string.Empty;
    }

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
        var selectedPriestlyWizardSpheres = GetSelectedPriestlyWizardSpheres(character);

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
                && !IsPriestSpellAllowedForCharacter(spell, selectedPriestSpheres)
                && !IsPriestSpellAllowedForCharacter(spell, selectedPriestlyWizardSpheres))
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

    private List<SpellDefinition> GetAccessibleWizardSpellDefinitions(CharacterSheet character)
    {
        var spells = GetWizardSpellbookDefinitions(character);
        int maxArcaneLevel = GetHighestSpellLevel(character.ArcaneSpellSlots);
        var priestlyWizardSpheres = GetSelectedPriestlyWizardSpheres(character);

        if (maxArcaneLevel > 0 && priestlyWizardSpheres.Count > 0)
        {
            spells.AddRange(_app.Rules.Spells
                .Where(spell => string.Equals(spell.Category, "divine", StringComparison.OrdinalIgnoreCase))
                .Where(spell => TryParseSpellLevel(spell.Level, out int spellLevel) && spellLevel <= maxArcaneLevel)
                .Where(spell => IsPriestSpellAllowedForCharacter(spell, priestlyWizardSpheres))
                .Where(IsCommonOrUncommonSpell));
        }

        return spells
            .GroupBy(spell => spell.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(spell => TryParseSpellLevel(spell.Level, out int level) ? level : int.MaxValue)
            .ThenBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<SpellDefinition> GetAccessiblePriestSpellDefinitions(CharacterSheet character)
    {
        int maxDivineLevel = GetHighestSpellLevel(character.DivineSpellSlots);
        if (maxDivineLevel <= 0)
            return new List<SpellDefinition>();

        var selectedPriestSpheres = GetSelectedPriestSpheres(character);

        var divineSpells = _app.Rules.Spells
            .Where(spell => string.Equals(spell.Category, "divine", StringComparison.OrdinalIgnoreCase))
            .Where(spell => TryParseSpellLevel(spell.Level, out int spellLevel) && spellLevel <= maxDivineLevel)
            .Where(spell => IsPriestSpellAllowedForCharacter(spell, selectedPriestSpheres));

        // Wizardly Priests: also include common/uncommon arcane spells from the selected wizard school
        var wizardlySchools = GetWizardlyPriestSchools(character);
        IEnumerable<SpellDefinition> wizardlySpells = Enumerable.Empty<SpellDefinition>();
        if (wizardlySchools.Count > 0)
        {
            wizardlySpells = _app.Rules.Spells
                .Where(spell => string.Equals(spell.Category, "arcane", StringComparison.OrdinalIgnoreCase))
                .Where(spell => TryParseSpellLevel(spell.Level, out int spellLevel) && spellLevel <= maxDivineLevel)
                .Where(spell => IsSpellInAnySchool(spell, wizardlySchools))
                .Where(IsCommonOrUncommonSpell);
        }

        return divineSpells.Concat(wizardlySpells)
            .OrderBy(spell => TryParseSpellLevel(spell.Level, out int level) ? level : int.MaxValue)
            .ThenBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> GetWizardlyPriestSchools(CharacterSheet character)
    {
        return character.SelectedClassAbilityIds
            .Where(entry => string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry),
                "cleric_wizardly_priests", StringComparison.OrdinalIgnoreCase))
            .Select(RulesEngine.ExtractClassAbilityPlayerText)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static bool IsSpellInAnySchool(SpellDefinition spell, List<string> schools)
    {
        var spellSchools = ParseSpellSchoolTokens(spell.Schools);
        return schools.Any(school =>
            spellSchools.Contains(NormalizeWizardSchoolName(school), StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsCommonOrUncommonSpell(SpellDefinition spell)
    {
        string freq = (spell.Frequency ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(freq)) return true;
        return !freq.StartsWith("Rare", StringComparison.OrdinalIgnoreCase)
            && !freq.StartsWith("Very Rare", StringComparison.OrdinalIgnoreCase);
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

        AddWizardSchoolAccessFromAbilitySelections(character, schools);

        return schools;
    }

    private static void AddWizardSchoolAccessFromAbilitySelections(CharacterSheet character, HashSet<string> schools)
    {
        foreach (string selectionEntry in character.SelectedClassAbilityIds)
        {
            string baseId = RulesEngine.ExtractClassAbilityBaseId(selectionEntry);
            if (!WizardSchoolAccessByAbilityId.TryGetValue(baseId, out string? mappedSchool)
                || string.IsNullOrWhiteSpace(mappedSchool))
                continue;

            string normalized = NormalizeWizardSchoolName(mappedSchool);
            if (!string.IsNullOrWhiteSpace(normalized))
                schools.Add(normalized);
        }
    }

    private bool TryGetArcaneSpellLevelByName(string spellName, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(spellName))
            return false;

        var spell = _app.Rules.Spells.FirstOrDefault(s =>
            string.Equals(s.Category, "arcane", StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.Name, spellName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (spell is null)
            return false;

        return int.TryParse(spell.Level, out level);
    }

    private bool TryGetWizardSpecialization(CharacterSheet character, out WizardSpecialization specialization)
    {
        specialization = default!;
        if (string.IsNullOrWhiteSpace(character.WizardSpecializationId)
            || !_app.Rules.Classes.TryGetValue("wizard", out var wizardClass)
            || wizardClass.Specializations is null)
        {
            return false;
        }

        var match = wizardClass.Specializations.FirstOrDefault(spec =>
            string.Equals(spec.Id, character.WizardSpecializationId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return false;

        specialization = match;
        return true;
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

        foreach (string raw in Regex.Split(schoolsText, @"\s*(,|;|\||&|\band\b)\s*", RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(raw)
                || raw == ","
                || raw == ";"
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
        string token = Regex.Replace((school ?? string.Empty).Trim(), @"\s+", " ");
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        if (token.Equals("Greater Divination", StringComparison.OrdinalIgnoreCase))
            return "Divination";

        if (token.Equals("Invocation", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Evocation", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Invocation/Evocation", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Evocation/Invocation", StringComparison.OrdinalIgnoreCase))
        {
            return "Invocation/Evocation";
        }

        if (token.Equals("Conjuration", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Summon", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Summoning", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Conjuration/Summon", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Summon/Conjuration", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Conjuration/Summoning", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Summoning/Conjuration", StringComparison.OrdinalIgnoreCase))
        {
            return "Conjuration/Summoning";
        }

        return token;
    }

    private static int ParseSpellLevel(string value)
    {
        if (int.TryParse((value ?? string.Empty).Trim(), out int exact))
            return Math.Max(0, exact);

        var match = Regex.Match(value ?? string.Empty, "\\d+");
        if (match.Success && int.TryParse(match.Value, out int parsed))
            return Math.Max(0, parsed);

        return 0;
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

        bool rangerHasConfiguredClassSpheres = character.SpheresByClass.TryGetValue("ranger", out var rangerSpheres)
            && rangerSpheres.Count > 0;

        if (CharacterHasClass(character, "ranger")
            && !rangerHasConfiguredClassSpheres
            && HasClassAbility(character, "ranger_priest_spells", "ranger_increased_spell_progression_cp7", "ranger_increased_spell_progression_cp12"))
        {
            if (!selected.ContainsKey("Animal"))
                selected["Animal"] = "minor";
            if (!selected.ContainsKey("Plant"))
                selected["Plant"] = "minor";
        }

        bool paladinHasConfiguredClassSpheres = character.SpheresByClass.TryGetValue("paladin", out var paladinSpheres)
            && paladinSpheres.Count > 0;

        if (CharacterHasClass(character, "paladin")
            && !paladinHasConfiguredClassSpheres
            && HasClassAbility(character, "paladin_priest_spells"))
        {
            if (!selected.ContainsKey("Combat"))
                selected["Combat"] = "minor";
            if (!selected.ContainsKey("Divination"))
                selected["Divination"] = "minor";
            if (!selected.ContainsKey("Healing"))
                selected["Healing"] = "minor";
            if (!selected.ContainsKey("Protection"))
                selected["Protection"] = "minor";
        }

        return selected;
    }

    private Dictionary<string, string> GetSelectedPriestlyWizardSpheres(CharacterSheet character)
    {
        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string selectionEntry in character.SelectedClassAbilityIds)
        {
            string baseId = RulesEngine.ExtractClassAbilityBaseId(selectionEntry);
            if (!string.Equals(baseId, "wizard_priestly_wizard_cp10", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(baseId, "wizard_priestly_wizard_cp15", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string sphere = RulesEngine.FormatClassAbilitySelectionText(baseId, RulesEngine.ExtractClassAbilityPlayerText(selectionEntry));
            if (string.IsNullOrWhiteSpace(sphere))
                continue;

            string access = string.Equals(baseId, "wizard_priestly_wizard_cp15", StringComparison.OrdinalIgnoreCase)
                ? "major"
                : "minor";

            if (selected.TryGetValue(sphere, out string? existingAccess)
                && string.Equals(existingAccess, "major", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            selected[sphere] = access;
        }

        return selected;
    }

    private static bool CharacterHasClass(CharacterSheet character, string classId)
    {
        return GetCharacterClassIds(character)
            .Any(id => string.Equals(id, classId, StringComparison.OrdinalIgnoreCase));
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

        int spellLevel = ParseSpellLevel(spell.Level);
        string requiredAccess = spellLevel <= 3 ? "minor" : "major";
        return spellSpheres.Any(sphere => RulesEngine.PriestHasSphereAccess(sphere, selectedSpheres, requiredAccess));
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
        if (character.HiddenSectionKeysByProfile is null) { character.HiddenSectionKeysByProfile = new(); changed = true; }

        if (changed)
            _app.SaveCharacters();
    }

    private void RepairMissingDivineSpellSlots(CharacterSheet character)
    {
        if (character.DivineSpellSlots.Any(kv => kv.Value > 0))
            return;

        int level = Math.Max(1, character.Level);
        bool needsPaladinRepair = CharacterHasClass(character, "paladin")
            && level >= 4
            && HasClassAbility(character, "paladin_priest_spells", "paladin_increased_spell_progression_cp10", "paladin_increased_spell_progression_cp15");

        bool needsRangerRepair = CharacterHasClass(character, "ranger")
            && level >= 8
            && HasClassAbility(character, "ranger_priest_spells", "ranger_increased_spell_progression_cp7", "ranger_increased_spell_progression_cp12");

        if (!needsPaladinRepair && !needsRangerRepair)
            return;

        var before = new Dictionary<int, int>(character.DivineSpellSlots);
        CharacterProgressionService.InitializeCharacterProgression(character, seedLevelRewards: false);
        bool changed = !before.OrderBy(kv => kv.Key).SequenceEqual(character.DivineSpellSlots.OrderBy(kv => kv.Key));
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

    // AD&D 2E Table 61 — Turning Undead
    // Rows = cleric level (index 0 = level 1, index 12 = level 13+)
    // Columns = undead types: Skel, Zomb, Ghoul, Shad, Wight, Ghast, Wraith, Mummy, Spectr, Vamp, Ghost, Lich, Spec
    private static readonly string[] TurnUndeadHeaders =
        { "Skel", "Zomb", "Ghoul", "Shad", "Wight", "Ghst", "Wrth", "Mmy", "Spct", "Vamp", "Gst", "Lich", "Spcl" };

    private static readonly string[][] TurnUndeadTable =
    {
        new[] { "10", "13", "16", "19", "20", "—",  "—",  "—",  "—",  "—",  "—",  "—",  "—"  }, // L1
        new[] { "7",  "10", "13", "16", "19", "20", "—",  "—",  "—",  "—",  "—",  "—",  "—"  }, // L2
        new[] { "4",  "7",  "10", "13", "16", "19", "20", "—",  "—",  "—",  "—",  "—",  "—"  }, // L3
        new[] { "T",  "T",  "4",  "7",  "10", "13", "16", "19", "20", "—",  "—",  "—",  "—"  }, // L4
        new[] { "T",  "T",  "T",  "4",  "7",  "10", "13", "16", "19", "20", "—",  "—",  "—"  }, // L5
        new[] { "D",  "D",  "T",  "T",  "4",  "7",  "10", "13", "16", "19", "20", "—",  "—"  }, // L6
        new[] { "D",  "D",  "D",  "T",  "T",  "4",  "7",  "10", "13", "16", "19", "20", "—"  }, // L7
        new[] { "D",  "D",  "D",  "D",  "T",  "T",  "4",  "7",  "10", "13", "16", "19", "20" }, // L8
        new[] { "D",  "D",  "D",  "D",  "D",  "T",  "T",  "4",  "7",  "10", "13", "16", "19" }, // L9
        new[] { "D",  "D",  "D",  "D",  "D",  "D",  "T",  "T",  "4",  "7",  "10", "13", "16" }, // L10
        new[] { "D",  "D",  "D",  "D",  "D",  "D",  "D",  "T",  "T",  "4",  "7",  "10", "13" }, // L11
        new[] { "D",  "D",  "D",  "D",  "D",  "D",  "D",  "D",  "T",  "T",  "4",  "7",  "10" }, // L12
        new[] { "D",  "D",  "D",  "D",  "D",  "D",  "D",  "D",  "D",  "T",  "T",  "4",  "7"  }, // L13+
    };

    private void BuildTurningUndeadTable(StackPanel panel, CharacterSheet character)
    {
        bool hasPaladinTurning = HasClassAbility(character, "paladin_turn_undead");
        bool hasMastery = HasClassAbility(character, "cleric_turning_mastery") && !hasPaladinTurning;
        int level = Math.Max(1, character.Level);
        int effectiveTurnLevel = hasPaladinTurning ? Math.Max(1, level - 2) : level;
        int effectiveRow = Math.Min(effectiveTurnLevel, 13) - 1; // row for effective turning level (0-based)
        int masteryRow  = Math.Min(level + 1, 13) - 1;      // row for turning mastery (1 level higher)

        // Legend
        var legend = new TextBlock
        {
            Text = "T = Turn automatically   D = Destroy automatically   — = Cannot turn",
            FontSize = 10,
            Foreground = (Brush)FindResource("BrushDim"),
            Margin = new Thickness(0, 0, 0, 4),
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(legend);

        if (hasPaladinTurning)
        {
            var paladinNote = new TextBlock
            {
                Text = "Paladin turn undead: use cleric turning at effective level = current level - 2.",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("BrushGreenLt"),
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(paladinNote);
        }

        if (hasMastery && level < 13)
        {
            var note = new TextBlock
            {
                Text = "Turning Mastery: effective level is current level +1 (bold row).",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("BrushGreenLt"),
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(note);
        }

        // Wrap table in horizontal scroll
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var grid = new Grid();
        int colCount = 1 + TurnUndeadHeaders.Length;  // Level label + 13 undead types
        for (int ci = 0; ci < colCount; ci++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = ci == 0 ? new GridLength(42) : new GridLength(38) });

        int rowCount = 1 + TurnUndeadTable.Length;  // header row + 13 data rows
        for (int ri = 0; ri < rowCount; ri++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header row
        var lvlHdr = MakeTurnCell("Lvl", 0, 0, isHeader: true, highlight: false, mastery: false);
        grid.Children.Add(lvlHdr);
        for (int ci = 0; ci < TurnUndeadHeaders.Length; ci++)
        {
            var hdr = MakeTurnCell(TurnUndeadHeaders[ci], 0, ci + 1, isHeader: true, highlight: false, mastery: false);
            grid.Children.Add(hdr);
        }

        // Data rows
        for (int ri = 0; ri < TurnUndeadTable.Length; ri++)
        {
            bool isHighlighted = ri == effectiveRow;
            bool isMasteryRow  = hasMastery && ri == masteryRow && masteryRow != effectiveRow;
            string levelLabel  = ri < 12 ? (ri + 1).ToString() : "13+";

            var lvlCell = MakeTurnCell(levelLabel, ri + 1, 0,
                isHeader: false, highlight: isHighlighted, mastery: isMasteryRow);
            grid.Children.Add(lvlCell);

            for (int ci = 0; ci < TurnUndeadTable[ri].Length; ci++)
            {
                var cell = MakeTurnCell(TurnUndeadTable[ri][ci], ri + 1, ci + 1,
                    isHeader: false, highlight: isHighlighted, mastery: isMasteryRow);
                grid.Children.Add(cell);
            }
        }

        scroll.Content = grid;
        panel.Children.Add(scroll);
    }

    private Border MakeTurnCell(string text, int row, int col, bool isHeader, bool highlight, bool mastery)
    {
        var tb = new TextBlock
        {
            Text       = text,
            FontSize   = 11,
            FontWeight = (isHeader || highlight || mastery) ? FontWeights.Bold : FontWeights.Normal,
            Foreground = highlight ? (Brush)FindResource("BrushTitle")
                       : mastery  ? (Brush)FindResource("BrushGreenLt")
                       : isHeader ? (Brush)FindResource("BrushDim")
                       :            (Brush)FindResource("BrushText"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Padding             = new Thickness(2)
        };

        var border = new Border
        {
            BorderBrush     = (Brush)FindResource("BrushBorder2"),
            BorderThickness = new Thickness(0.5),
            Background      = highlight ? new SolidColorBrush(Color.FromArgb(40, 212, 175, 55))
                            : mastery   ? new SolidColorBrush(Color.FromArgb(25, 180, 220, 180))
                            : Brushes.Transparent,
            Padding         = new Thickness(3, 2, 3, 2),
            Child           = tb
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        return border;
    }

    private void BuildUnarmedCombatTable(StackPanel panel, CharacterSheet character)
    {
        int level = Math.Max(1, character.Level);
        int currentTier = level switch
        {
            <= 4  => 0,  // Specialist
            <= 8  => 1,  // Master
            <= 12 => 2,  // High Master
            _     => 3   // Grand Master
        };

        // Headers + data rows
        string[] stageNames = { "Specialist (L1–4)", "Master (L5–8)", "High Master (L9–12)", "Grand Master (L13+)" };
        string[] aprs       = { "3/2",   "2",    "5/2",   "3"   };
        string[] dmgs       = { "1d6",   "1d8",  "1d10",  "2d6" };
        string[] speeds     = { "4",     "3",    "2",     "1"   };
        string[] thac0s     = { "+0",    "+1",   "+2",    "+3"  };

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var grid = new Grid();
        string[] colHeaders = { "Stage", "APR", "Damage", "Speed", "THAC0 Adj" };
        int[] colWidths     = {  150,     44,    54,       44,      74          };
        foreach (int w in colWidths)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

        int totalRows = 1 + stageNames.Length;
        for (int ri = 0; ri < totalRows; ri++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header row
        for (int ci = 0; ci < colHeaders.Length; ci++)
            grid.Children.Add(MakeTurnCell(colHeaders[ci], 0, ci, isHeader: true, highlight: false, mastery: false));

        // Data rows
        for (int ri = 0; ri < stageNames.Length; ri++)
        {
            bool active = ri == currentTier;
            grid.Children.Add(MakeTurnCell(stageNames[ri], ri + 1, 0, isHeader: false, highlight: active, mastery: false));
            grid.Children.Add(MakeTurnCell(aprs[ri],        ri + 1, 1, isHeader: false, highlight: active, mastery: false));
            grid.Children.Add(MakeTurnCell(dmgs[ri],        ri + 1, 2, isHeader: false, highlight: active, mastery: false));
            grid.Children.Add(MakeTurnCell(speeds[ri],      ri + 1, 3, isHeader: false, highlight: active, mastery: false));
            grid.Children.Add(MakeTurnCell(thac0s[ri],      ri + 1, 4, isHeader: false, highlight: active, mastery: false));
        }

        scroll.Content = grid;
        panel.Children.Add(scroll);

        var note = new TextBlock
        {
            Text = $"Current tier: {stageNames[currentTier]}",
            FontSize = 10,
            FontStyle = FontStyles.Italic,
            Foreground = (Brush)FindResource("BrushDim"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        panel.Children.Add(note);
    }
}
