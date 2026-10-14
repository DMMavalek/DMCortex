using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CharacterSheetsScreen : UserControl, IScreen
{
    private const string SectionDragDataFormat = "dmcortex.character-sheet.section";
    private const int LayoutLaneCount = 6;
    private const int DefaultSectionSpan = 2; // 1/3 width in a 6-lane layout
    private readonly MainWindow _app;
    private readonly Random _rng = new();
    private CharacterSheet? _selectedCharacter;
    private string _selectedCharacterSheetText = string.Empty;
    private readonly List<SectionCard> _sectionCards = new();
    private readonly Dictionary<string, SectionPlacement> _sectionLayoutByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenSectionKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expandedSectionKeys = new(StringComparer.OrdinalIgnoreCase);
    private Point _dragStartPoint;
    private string? _dragSectionKey;
    private bool _dragDebugEnabled = true;
    private static readonly Dictionary<string, PrintLayoutOptions> PrintLayoutPresetsByProfile = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> TextSectionBySectionKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["character_identity"] = "CHARACTER SHEET",
        ["ability_scores"] = "ABILITIES",
        ["combat_statistics"] = "CORE COMBAT",
        ["weapons"] = "WEAPONS",
        ["saving_throws"] = "SAVING THROWS",
        ["weapon_proficiencies"] = "WEAPON PROFICIENCIES",
        ["turning_undead"] = "TURNING UNDEAD",
        ["unarmed_combat"] = "UNARMED COMBAT",
        ["nonweapon_proficiencies"] = "NONWEAPON PROFICIENCIES",
        ["rogue_abilities"] = "ROGUE ABILITIES",
        ["languages"] = "LANGUAGES",
        ["equipment"] = "EQUIPMENT",
        ["wealth"] = "WEALTH",
        ["traits_disadvantages"] = "TRAITS / DISADVANTAGES",
        ["abilities_special_powers"] = "ABILITIES & SPECIAL POWERS",
        ["notes"] = "NOTES",
        ["spells"] = "SPELLS",
    };
    private static readonly Dictionary<string, string> TextSectionByVisualTitle = new(StringComparer.OrdinalIgnoreCase)
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
        ["ROGUE ABILITIES"] = "ROGUE ABILITIES",
        ["LANGUAGES"] = "LANGUAGES",
        ["EQUIPMENT"] = "EQUIPMENT",
        ["WEALTH"] = "WEALTH",
        ["TRAITS & DISADVANTAGES"] = "TRAITS / DISADVANTAGES",
        ["ABILITIES & SPECIAL POWERS"] = "ABILITIES & SPECIAL POWERS",
        ["NOTES"] = "NOTES",
        ["SPELLS"] = "SPELLS",
    };

    private sealed class SectionCard
    {
        public required string Key { get; init; }
        public required string Title { get; init; }
        public required Expander Expander { get; init; }
    }

    private sealed class HtmlSectionLayout
    {
        public required string Key { get; init; }
        public required string Title { get; init; }
        public required string SourceHeading { get; init; }
        public int Row { get; init; }
        public int Lane { get; init; }
        public int Span { get; init; }
    }

    private sealed class HtmlWeaponRow
    {
        public required string Role { get; init; }
        public required string Weapon { get; init; }
        public required string Thac0 { get; init; }
        public required string Sf { get; init; }
        public required string Apr { get; init; }
        public required string DmgSm { get; init; }
        public required string DmgL { get; init; }
        public required string Type { get; init; }
        public required string Size { get; init; }
        public required string Prof { get; init; }
        public bool IsProficient { get; init; }
    }

    private sealed class SectionPlacement
    {
        public int Lane { get; set; }
        public int Order { get; set; }
        public int Span { get; set; } = DefaultSectionSpan;
    }

    private static readonly (string Label, int Span)[] WidthPresets =
    {
        ("1/3 Row", 2),
        ("1/2 Row", 3),
        ("2/3 Row", 4),
        ("Full Row", 6),
    };

    private sealed class RowLaneTarget
    {
        public int Row { get; init; }
        public int Lane { get; init; }
    }

    private sealed class VisualThemeRestoreState
    {
        public List<(TextBlock element, Brush? foreground, Brush? background)> TextBlocks { get; } = new();
        public List<(Border element, Brush? borderBrush, Brush? background)> Borders { get; } = new();
        public List<(Control element, Brush? foreground, Brush? background, Brush? borderBrush)> Controls { get; } = new();
        public List<(Panel element, Brush? background)> Panels { get; } = new();
        public List<(Shape element, Brush? fill, Brush? stroke)> Shapes { get; } = new();
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
            BtnExportHtml.IsEnabled = false;
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
        BtnExportHtml.IsEnabled = true;
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
                         + $"Level: {levelText}    XP: {FormatExperienceDisplay(c)}";
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

    private static string FormatClassLabel(string classId)
    {
        string normalized = (classId ?? string.Empty).Trim().Replace('_', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized);
    }

    private static string FormatLevelDisplay(CharacterSheet character)
    {
        int level = Math.Max(1, character.Level);
        var classIds = GetDisplayedClassIds(character);
        if (classIds.Count <= 1)
            return level.ToString();

        return string.Join(" / ", classIds.Select(classId =>
            $"{FormatClassLabel(classId)} {CharacterProgressionService.GetLevelForExperience(classId, CharacterProgressionService.GetExperienceForClass(character, classId))}"));
    }

    private static string FormatExperienceDisplay(CharacterSheet character)
    {
        int experience = Math.Max(0, character.ExperiencePoints);
        var classIds = GetDisplayedClassIds(character);
        if (classIds.Count <= 1)
            return $"{experience:n0} XP";

        return string.Join(" / ", classIds.Select(classId =>
            $"{FormatClassLabel(classId)}: {CharacterProgressionService.GetExperienceForClass(character, classId):n0} XP"));
    }

    private static string FormatExperienceNeededDisplay(CharacterSheet character)
    {
        int experience = Math.Max(0, character.ExperiencePoints);
        var classIds = GetDisplayedClassIds(character);
        if (classIds.Count > 1)
        {
            return string.Join(" / ", classIds.Select(classId =>
            {
                int classExperience = CharacterProgressionService.GetExperienceForClass(character, classId);
                int classLevel = CharacterProgressionService.GetLevelForExperience(classId, classExperience);
                if (classLevel >= 20)
                    return $"{FormatClassLabel(classId)}: 0 XP (Max level)";

                int nextLevelXp = CharacterProgressionService.GetMinimumExperienceForLevel(classId, classLevel + 1);
                return $"{FormatClassLabel(classId)}: {Math.Max(0, nextLevelXp - classExperience):n0} XP";
            }));
        }

        int currentLevel = Math.Clamp(character.Level, 1, 20);
        if (currentLevel >= 20)
            return "0 XP (Max level)";

        string classId = classIds.FirstOrDefault() ?? character.ClassId;
        int nextLevelXp = CharacterProgressionService.GetMinimumExperienceForLevel(classId, currentLevel + 1);
        int needed = Math.Max(0, nextLevelXp - experience);
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
            || normalized.Equals("Creature Languages (Druid)", StringComparison.OrdinalIgnoreCase)
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

    private static bool IsRogueClassCharacter(CharacterSheet character)
    {
        static bool IsRogueToken(string token)
        {
            string normalized = (token ?? string.Empty).Trim().ToLowerInvariant();
            return normalized is "thief" or "bard" or "rogue";
        }

        if (character.ClassIds is { Count: > 0 } && character.ClassIds.Any(IsRogueToken))
            return true;

        if (!string.IsNullOrWhiteSpace(character.ClassId))
        {
            var parts = character.ClassId
                .Split(new[] { '/', '+', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Any(IsRogueToken))
                return true;
        }

        return false;
    }

    private static List<string> BuildRogueAbilityLines(CharacterSheet character)
    {
        if (IsPlayersOptionMode(character))
        {
            // In Player's Option mode, rogue abilities are opt-in from selected class abilities.
            var selectedAbilityEntries = character.SelectedClassAbilityIds
                .Concat(character.SelectedAbilitiesByClass.Values.SelectMany(entries => entries))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var selectedSkillIds = RulesEngine.GetRogueSkillIdsForClassProfile(
                character.ClassId,
                selectedAbilityEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selectedSkillIds.Count == 0)
                return new List<string>();

            var selectedAllocations = character.SelectedRogueSkillPoints
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            int dexterity = ResolveAbilityScore(character, "dex");
            var breakdown = RulesEngine.BuildRogueSkillBreakdown(
                selectedSkillIds,
                character.RaceId,
                dexterity,
                character.RogueSkillArmorProfile,
                selectedAllocations,
                Math.Max(1, character.Level),
                RulesEngine.UsesAutomaticRogueSkillProgression(character.ClassId));

            return breakdown
                .OrderBy(entry => entry.SkillName, StringComparer.OrdinalIgnoreCase)
                .Select(FormatRogueSkillBreakdownLine)
                .ToList();
        }

        // In Core mode, thieves and bards still use the base rogue skill list,
        // but the final scores come from allocated points plus race/Dex/armor.
        var coreSkillIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> classTokens = character.ClassIds is { Count: > 0 }
            ? character.ClassIds
            : (character.ClassId ?? string.Empty)
                .Split(new[] { '/', '+', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string rawToken in classTokens)
        {
            string token = (rawToken ?? string.Empty).Trim().ToLowerInvariant();
            foreach (string skillId in RulesEngine.GetStandardCoreRogueSkillIdsForClassProfile(token))
            {
                coreSkillIds.Add(skillId);
            }
        }

        if (coreSkillIds.Count == 0)
            return new List<string>();

        int coreDexterity = ResolveAbilityScore(character, "dex");
        var coreBreakdown = RulesEngine.BuildRogueSkillBreakdown(
            coreSkillIds,
            character.RaceId,
            coreDexterity,
            character.RogueSkillArmorProfile,
            character.SelectedRogueSkillPoints,
            Math.Max(1, character.Level));

        return coreBreakdown
            .OrderBy(entry => entry.SkillName, StringComparer.OrdinalIgnoreCase)
            .Select(FormatRogueSkillBreakdownLine)
            .ToList();
    }

    private static string FormatRogueSkillBreakdownLine(RogueSkillBreakdown entry)
    {
        string racePart = entry.RacialAdjustment == 0 ? "Race 0%" : $"Race {FormatSigned(entry.RacialAdjustment)}%";
        string dexPart = entry.DexterityAdjustment == 0 ? "Dex 0%" : $"Dex {FormatSigned(entry.DexterityAdjustment)}%";
        string armorPart = entry.ArmorAdjustment == 0 ? "Armor 0%" : $"Armor {FormatSigned(entry.ArmorAdjustment)}%";
        string addedPart = entry.AllocatedPoints == 0 ? "Added 0%" : $"Added +{entry.AllocatedPoints}%";
        return $"{entry.SkillName}: Base {entry.BaseScore}% | {racePart} | {dexPart} | {armorPart} | {addedPart} | Total {entry.FinalScore}%";
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
            $"Hit Points: {c.HitPoints}",
            $"Armor Class: {c.ArmorClass}",
            $"Armor Profile: {c.ArmorProfile}    Rogue Armor Profile: {c.RogueSkillArmorProfile}",
            $"Movement: {c.Movement} (base {c.BaseMovement})",
            $"THAC0: {CharacterProgressionService.GetCombatBaseThac0(c)}",
            $"Unspent Proficiency Choices: {c.UnspentProficiencyChoices}    Unspent Rogue Skill Points: {c.UnspentRogueSkillPoints}",
            string.Empty,
            "=== ABILITIES ===",
        };

        if (string.Equals(c.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
        {
            string hpRerollText = c.SpentHpRerollCharacterPoints > 0 ? $"    Spent HP Reroll CP: {c.SpentHpRerollCharacterPoints}" : string.Empty;
            lines.Insert(8, $"Unspent CP: {c.UnspentCharacterPoints}    Spent NWP CP: {c.SpentNwpCharacterPoints}    Spent Weapon CP: {c.SpentWeaponCharacterPoints}{hpRerollText}");
        }

        bool isPlayersOption = IsPlayersOptionMode(c);
        foreach (string abilityKey in AbilityKeys)
        {
            int abilityScore = ResolveAbilityScore(c, abilityKey);
            lines.Add($"{abilityKey.ToUpperInvariant()}: {abilityScore}");

            if (!isPlayersOption)
            {
                lines.Add($"  - {BuildCoreAbilityEffectSummary(c, abilityKey, abilityScore)}");
                continue;
            }

            foreach (string subKey in GetSubAbilityKeys(abilityKey))
            {
                int subScore = ResolveSubAbilityScore(c, abilityKey, subKey, abilityScore);
                string subScoreDisplay = FormatSubAbilityScore(c, abilityKey, subKey, subScore);
                string effect = ResolveSubAbilityEffect(c, subKey, subScore);
                lines.Add($"  - {GetSubAbilityDisplayLabel(c, subKey)}: {subScoreDisplay} ({effect})");
            }
        }

        lines.Add(string.Empty);
        lines.Add("=== WEAPONS ===");
        var carriedWeapons = c.EquipmentSelections
            .Where(e => e.IsWeapon)
            .OrderBy(e => e.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (carriedWeapons.Count > 0)
        {
            foreach (var weapon in carriedWeapons)
            {
                int quantity = Math.Max(1, weapon.Quantity);
                string qty = quantity > 1 ? $" x{quantity}" : string.Empty;
                string stats = $" [SF {weapon.WeaponSpeed}; Dmg {weapon.WeaponDamageSmallMedium}/{weapon.WeaponDamageLarge}; {weapon.WeaponType}; {weapon.WeaponSize}]";
                lines.Add($"{weapon.ItemName}{qty}{stats}");
            }
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
                string mastery = wp.GrandMastery
                    ? " [Grand Mastery: +1 apr; +3 attack/+3 damage; upgraded damage dice; speed -3; crit on 16+]"
                    : wp.HighMastery
                    ? " [High Mastery: +3 attack/+3 damage; speed factor -3; crit on 16+; extreme missile range]"
                    : wp.WeaponMastery
                    ? " [Mastery: melee +3 attack/+3 damage; missile +2 attack beyond point blank, +3 attack/+3 damage at point blank]"
                    : string.Empty;
                string typeLabel = FormatProficiencyTypePlain(wp);
                lines.Add($"{wp.DisplayName} ({typeLabel}){spec}{mastery}");
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
                lines.Add(AppendNwpConditionalModifiers(c, nwp));
        }
        else
        {
            lines.Add("(none)");
        }

        lines.Add(string.Empty);
        lines.Add("=== ROGUE ABILITIES ===");
        var rogueAbilityLines = BuildRogueAbilityLines(c);
        if (rogueAbilityLines.Count > 0)
        {
            foreach (string line in rogueAbilityLines)
                lines.Add(line);
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
                int quantity = Math.Max(1, eq.Quantity);
                string qty = quantity > 1 ? $" x{quantity}" : string.Empty;
                lines.Add($"{eq.ItemName}{qty}");
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
        lines.Add("=== WEALTH ===");
        int totalGemValue = GetTotalGemValueGoldPieces(c);
        int totalGemCount = GetTotalGemCount(c);
        lines.Add($"Platinum Pieces: {c.PlatinumPieces}");
        lines.Add($"Gold Pieces: {c.GoldPieces}");
        lines.Add($"Silver Pieces: {c.SilverPieces}");
        lines.Add($"Copper Pieces: {c.CopperPieces}");
        lines.Add($"Gem Count: {totalGemCount}");
        lines.Add($"Gem Value: {totalGemValue:n0} gp");

        lines.Add(string.Empty);
        lines.Add("=== TRAITS / DISADVANTAGES ===");
        lines.Add(traitNames.Count > 0 ? "Traits: " + string.Join(", ", traitNames) : "Traits: (none)");
        lines.Add(disadvantageNames.Count > 0 ? "Disadvantages: " + string.Join(", ", disadvantageNames) : "Disadvantages: (none)");

        lines.Add(string.Empty);
        lines.Add("=== ABILITIES & SPECIAL POWERS ===");
        var unlockedStructuredAbilities = c.StructuredAbilities
            .Where(ab => RulesEngine.AbilityUnlockLevel(ab) <= Math.Max(1, c.Level))
            .ToList();
        if (unlockedStructuredAbilities.Count > 0)
        {
            foreach (var ab in unlockedStructuredAbilities)
            {
                string name = ab.Id.Replace("_", " ");
                string desc = string.IsNullOrWhiteSpace(ab.Description) ? string.Empty : $": {ab.Description}";
                lines.Add($"{name}{desc}");
            }
        }
        else if (c.RacialAbilities.Count > 0)
        {
            foreach (string ab in c.RacialAbilities)
                lines.Add(ab);
        }
        else
        {
            lines.Add("(none)");
        }

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
            var spellbookByLevel = GetAccessibleWizardSpellDefinitions(c)
                .GroupBy(s => GetDisplayedWizardSpellLevelForReview(c, s))
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

    private void BtnResetLayout_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
            return;

        string profileKey = BuildSheetLayoutProfileKey(_selectedCharacter);
        _selectedCharacter.SheetLayoutByProfile.Remove(profileKey);
        _selectedCharacter.HiddenSectionKeysByProfile.Remove(profileKey);
        _hiddenSectionKeys.Clear();

        LoadSavedSectionLayoutPreference(_selectedCharacter);
        ApplyHiddenSectionVisibility();
        RenderSectionCards();
        _app.SaveCharacters();

        BtnToggleExpandCollapse.Content = "EXPAND ALL";
        StatusText.Text = BuildStatusText("Layout reset to default profile.");
    }

    private void BtnToggleExpandCollapse_Click(object sender, RoutedEventArgs e)
    {
        var expanders = GetLiveSectionExpanders()
            .Where(x => x.Visibility == Visibility.Visible)
            .ToList();
        if (expanders.Count == 0)
            return;

        bool allExpanded = expanders.All(x => x.IsExpanded);
        bool shouldExpand = !allExpanded;

        foreach (var expander in expanders)
            expander.IsExpanded = shouldExpand;

        BtnToggleExpandCollapse.Content = shouldExpand ? "COLLAPSE ALL" : "EXPAND ALL";
        StatusText.Text = BuildStatusText(shouldExpand ? "Expanded all visible sections." : "Collapsed all visible sections.");
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

            var printOptions = ShowPrintSetupDialog(_selectedCharacter, sectionTitles);
            if (printOptions is null)
                return;

            var (previewWidth, previewHeight) = GetDefaultLetterPreviewArea();
            var previewDocument = BuildPrintableDocument(previewWidth, previewHeight, printOptions);
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

            var printable = BuildPrintableDocument(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight, printOptions);
            if (printable is null)
            {
                MessageBox.Show("Could not generate a printable document from the current sheet layout.", "Character Sheets", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            dlg.PrintDocument(printable.DocumentPaginator, $"Character Sheet - {_selectedCharacter.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not print character sheet.\n\n{ex.Message}", "Character Sheets", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private IDocumentPaginatorSource? BuildPrintableDocument(double printableWidth, double printableHeight, PrintLayoutOptions options)
    {
        if (_selectedCharacter is null)
            return null;

        var visualDocument = BuildPrintableVisualDocument(LaneSurface, printableWidth, printableHeight, options);
        if (visualDocument is not null)
            return visualDocument;

        if (options.Theme == PrintDocumentTheme.BlackAndWhite)
        {
            return BuildPrintableTextDocument(
                _selectedCharacter,
                _selectedCharacterSheetText,
                options,
                printableWidth,
                printableHeight);
        }

        return null;
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
            return new PrintLayoutOptions(new List<PrintLayoutEntry>(), PrintDocumentTheme.Color);

        string profileKey = BuildPrintProfileKey(character);
        var selectedEntries = initialOptions is not null
            ? ClonePrintLayoutEntries(initialOptions.CloneEntries())
            : PrintLayoutPresetsByProfile.TryGetValue(profileKey, out var saved)
                ? saved.CloneEntriesFor(sectionTitles)
                : BuildDefaultPrintLayoutEntries(sectionTitles);
        var selectedTheme = initialOptions?.Theme
            ?? (PrintLayoutPresetsByProfile.TryGetValue(profileKey, out var presetTheme) ? presetTheme.Theme : PrintDocumentTheme.Color);
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
            PrintLayoutPresetsByProfile[profileKey] = new PrintLayoutOptions(ClonePrintLayoutEntries(selectedEntries), GetSelectedPrintTheme());
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

        return new PrintLayoutOptions(ClonePrintLayoutEntries(selectedEntries), GetSelectedPrintTheme());

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

        public PrintLayoutOptions(List<PrintLayoutEntry> selectedEntries, PrintDocumentTheme theme)
        {
            SelectedEntries = selectedEntries;
            Theme = theme;
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
            Text = "Preview reflects the current Character Sheet layout. Use Print to open printer dialog.",
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

        FrameworkElement previewHost;
        if (document is FlowDocument flowDocument)
        {
            previewHost = new FlowDocumentScrollViewer
            {
                Document = flowDocument,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                IsToolBarVisible = true,
            };
        }
        else
        {
            previewHost = new DocumentViewer
            {
                Document = document,
                Background = Brushes.White,
                Foreground = Brushes.Black,
            };
        }

        Grid.SetRow(previewHost, 1);
        Grid.SetColumn(previewHost, 0);
        root.Children.Add(previewHost);

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

    private void BtnExportBlankPdf_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export Mapped Fillable Character Sheet",
                FileName = "blank-mapped-character-sheet",
                Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                DefaultExt = ".pdf",
                AddExtension = true,
                OverwritePrompt = true,
            };

            if (dlg.ShowDialog() != true)
                return;

            BlankCharacterSheetPdfService.Create(dlg.FileName);
            StatusText.Text = BuildStatusText($"Exported blank fillable PDF to {System.IO.Path.GetFileName(dlg.FileName)}.");
            MessageBox.Show(
                "Blank fillable character sheet exported as PDF.",
                "Character Sheets",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not export blank fillable PDF.\n\n{ex.Message}",
                "Character Sheets",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BtnExportHtml_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
            return;

        try
        {
            var sectionLayout = GetVisibleSectionLayoutForHtmlExport();
            if (sectionLayout.Count == 0)
            {
                MessageBox.Show("No visible sections are available to export.", "Character Sheets", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string defaultFileName = string.IsNullOrWhiteSpace(_selectedCharacter.Name)
                ? "character-sheet"
                : _selectedCharacter.Name;

            var dlg = new SaveFileDialog
            {
                Title = "Export Character Sheet (HTML)",
                FileName = defaultFileName,
                Filter = "HTML files (*.html)|*.html|All files (*.*)|*.*",
                DefaultExt = ".html",
                AddExtension = true,
                OverwritePrompt = true,
            };

            if (dlg.ShowDialog() != true)
                return;

            string html = BuildColorSheetHtml(_selectedCharacter, sectionLayout, _selectedCharacterSheetText);
            File.WriteAllText(dlg.FileName, html, Encoding.UTF8);
            StatusText.Text = BuildStatusText($"Exported HTML sheet to {System.IO.Path.GetFileName(dlg.FileName)}.");
            MessageBox.Show("Character sheet exported as HTML.", "Character Sheets", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export character sheet HTML.\n\n{ex.Message}", "Character Sheets", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private List<HtmlSectionLayout> GetVisibleSectionLayoutForHtmlExport()
    {
        return _sectionCards
            .Where(card => card.Expander.Visibility == Visibility.Visible)
            .Where(card => !string.IsNullOrWhiteSpace(card.Title))
            .Select(card =>
            {
                _sectionLayoutByKey.TryGetValue(card.Key, out var placement);
                int span = placement is null ? DefaultSectionSpan : Math.Clamp(placement.Span, 1, LayoutLaneCount);
                int lane = placement is null ? 0 : Math.Clamp(placement.Lane, 0, LayoutLaneCount - span);

                return new HtmlSectionLayout
                {
                    Key = card.Key,
                    Title = card.Title,
                    SourceHeading = ResolveTextSectionHeading(card.Key, card.Title),
                    Row = placement?.Order ?? int.MaxValue,
                    Lane = lane,
                    Span = span,
                };
            })
            .OrderBy(item => item.Row)
            .ThenBy(item => item.Lane)
            .ToList();
    }

    private static string ResolveTextSectionHeading(string sectionKey, string sectionTitle)
    {
        if (TextSectionBySectionKey.TryGetValue(sectionKey, out string? heading)
            && !string.IsNullOrWhiteSpace(heading))
        {
            return heading;
        }

        if (TextSectionByVisualTitle.TryGetValue(sectionTitle, out string? fallback)
            && !string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return sectionTitle;
    }

    private string BuildColorSheetHtml(CharacterSheet character, IReadOnlyList<HtmlSectionLayout> sectionLayout, string fullSheetText)
    {
        var blocks = ParseTextSections(fullSheetText)
            .Where(block => !string.IsNullOrWhiteSpace(block.heading))
            .GroupBy(block => block.heading, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var sections = new List<(string key, string title, string sourceHeading, int row, int lane, int span, List<string> lines)>();
        foreach (HtmlSectionLayout layout in sectionLayout)
        {
            string sourceHeading = layout.SourceHeading;

            if (!blocks.TryGetValue(sourceHeading, out var block))
                continue;

            var lines = block.lines
                .Where(line => !line.StartsWith("=== ", StringComparison.Ordinal))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            sections.Add((layout.Key, layout.Title, sourceHeading, layout.Row, layout.Lane, layout.Span, lines));
        }

        var html = new StringBuilder(32_768);
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\"> ");
        html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"> ");
        html.AppendLine($"  <title>Character Sheet - {EscapeHtml(character.Name)}</title>");
        html.AppendLine("  <style>");
        html.AppendLine("    :root { --parchment:#fffaf1; --panel:#fcf8f0; --ink:#1f1811; --title:#4c3112; --accent:#825e25; --accent-soft:#b69158; --border:#d8c49c; }");
        html.AppendLine("    * { box-sizing: border-box; }");
        html.AppendLine("    body { margin: 0; font-family: Segoe UI, Tahoma, sans-serif; color: var(--ink); background: linear-gradient(180deg, #f0e5d2 0%, #e7d7be 100%); }");
        html.AppendLine("    .wrap { max-width: 1080px; margin: 24px auto; padding: 18px; }");
        html.AppendLine("    .header { background: linear-gradient(145deg, #f8f0e1 0%, #f2e4cb 100%); border: 1px solid var(--border); border-radius: 8px; padding: 16px; box-shadow: 0 8px 22px rgba(76, 49, 18, .14); }");
        html.AppendLine("    .title { margin: 0; font-size: 30px; color: var(--title); letter-spacing: .02em; }");
        html.AppendLine("    .subtitle { margin: 6px 0 0; color: #5f4730; font-size: 14px; }");
        html.AppendLine("    .grid { margin-top: 16px; display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 12px; grid-auto-flow: row; align-items: stretch; }");
        html.AppendLine("    .section { background: var(--panel); border: 1px solid var(--border); border-radius: 6px; overflow: hidden; box-shadow: 0 4px 12px rgba(76, 49, 18, .08); display: flex; flex-direction: column; min-height: 100%; }");
        html.AppendLine("    .section h2 { margin: 0; padding: 10px 12px; font-size: 15px; font-weight: 700; color: var(--title); border-bottom: 1px solid var(--accent-soft); background: linear-gradient(180deg, #fdf7eb 0%, #f6ecd8 100%); }");
        html.AppendLine("    .section ul { list-style: none; margin: 0; padding: 10px 12px 12px; }");
        html.AppendLine("    .section li { font-size: 13px; line-height: 1.45; margin: 0 0 5px; white-space: pre-wrap; } ");
        html.AppendLine("    .section li:last-child { margin-bottom: 0; }");
        html.AppendLine("    .identity-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 12px 18px; padding: 10px 12px 12px; }");
        html.AppendLine("    .identity-col { display: flex; flex-direction: column; gap: 6px; }");
        html.AppendLine("    .identity-row { font-size: 13px; line-height: 1.35; } ");
        html.AppendLine("    .identity-key { color: #5f4730; font-weight: 700; margin-right: 6px; } ");
        html.AppendLine("    .identity-val { color: var(--ink); } ");
        html.AppendLine("    .weapon-table-wrap { padding: 8px 10px 12px; overflow-x: auto; }");
        html.AppendLine("    .weapon-table { width: 100%; border-collapse: collapse; min-width: 760px; font-size: 12px; } ");
        html.AppendLine("    .weapon-table thead th { text-align: left; font-size: 10px; letter-spacing: .03em; color: #5f4730; border-bottom: 1px solid var(--accent-soft); padding: 0 4px 6px; white-space: nowrap; }");
        html.AppendLine("    .weapon-table tbody td { padding: 4px 4px; border-bottom: 1px solid rgba(130,94,37,.12); white-space: nowrap; vertical-align: middle; }");
        html.AppendLine("    .weapon-table tbody tr:nth-child(even) { background: rgba(255,255,255,.38); }");
        html.AppendLine("    .weapon-name { font-weight: 600; }");
        html.AppendLine("    .prof-badge { display: inline-block; padding: 1px 6px; border-radius: 3px; font-size: 10px; font-weight: 700; color: #fff; } ");
        html.AppendLine("    .prof-badge.prof { background: #2d7a4b; } .prof-badge.spec { background: var(--title); } .prof-badge.np { background: #b13a2a; }");
        html.AppendLine("    .thac0-np { color: #b13a2a; font-weight: 600; }");
        html.AppendLine("    .empty { color: #6e5740; font-style: italic; }");
        html.AppendLine("    @media (max-width: 980px) { .grid { grid-template-columns: repeat(2, minmax(0, 1fr)); } .section { grid-column: auto !important; grid-row: auto !important; } }");
        html.AppendLine("    @media (max-width: 640px) { .grid { grid-template-columns: 1fr; } }");
        html.AppendLine("    @media print { body { background: #fff; } .wrap { margin: 0; max-width: none; } .header, .section { box-shadow: none; } }");
        html.AppendLine("  </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("  <div class=\"wrap\">");
        html.AppendLine("    <header class=\"header\">");
        html.AppendLine($"      <h1 class=\"title\">{EscapeHtml(character.Name)}</h1>");
        html.AppendLine($"      <p class=\"subtitle\">Player: {EscapeHtml(string.IsNullOrWhiteSpace(character.PlayerName) ? "-" : character.PlayerName)} | Last Updated: {EscapeHtml(character.LastModifiedDisplay)}</p>");
        html.AppendLine("    </header>");
        html.AppendLine("    <main class=\"grid\">");

        foreach (var section in sections)
        {
            int gridColumn = Math.Clamp(section.lane + 1, 1, LayoutLaneCount);
            int gridSpan = Math.Clamp(section.span, 1, LayoutLaneCount - section.lane);
            int gridRow = Math.Max(1, section.row + 1);

            html.AppendLine($"      <section class=\"section\" style=\"grid-column: {gridColumn} / span {gridSpan}; grid-row: {gridRow};\">");
            html.AppendLine($"        <h2>{EscapeHtml(section.title)}</h2>");
            if (string.Equals(section.key, "character_identity", StringComparison.OrdinalIgnoreCase))
            {
                string race = string.IsNullOrWhiteSpace(character.RaceName) ? character.RaceId : character.RaceName;
                string cls = string.IsNullOrWhiteSpace(character.ClassName) ? character.ClassId : character.ClassName;
                string ruleset = string.Equals(character.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
                    ? "Player's Option"
                    : "Core Rules";
                string cpsAvailable = string.Equals(character.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase)
                    ? character.UnspentCharacterPoints.ToString("N0", CultureInfo.InvariantCulture)
                    : "-";

                var left = new List<(string label, string value)>
                {
                    ("Name", string.IsNullOrWhiteSpace(character.Name) ? "-" : character.Name),
                    ("Player", string.IsNullOrWhiteSpace(character.PlayerName) ? "-" : character.PlayerName),
                    ("Gender", string.IsNullOrWhiteSpace(character.Gender) ? "-" : character.Gender),
                    ("Party", string.IsNullOrWhiteSpace(character.Party) ? "-" : character.Party),
                    ("Ruleset", ruleset),
                    ("CPs Available", cpsAvailable),
                };

                var right = new List<(string label, string value)>
                {
                    ("Race", string.IsNullOrWhiteSpace(race) ? "-" : race),
                    ("Class", string.IsNullOrWhiteSpace(cls) ? "-" : cls),
                    ("Level", FormatLevelDisplay(character)),
                    ("Experience", FormatExperienceDisplay(character)),
                    ("Experience Needed", FormatExperienceNeededDisplay(character)),
                };

                bool hasIdentityData = left.Any(pair => !string.IsNullOrWhiteSpace(pair.value) && pair.value != "-")
                    || right.Any(pair => !string.IsNullOrWhiteSpace(pair.value) && pair.value != "-");

                if (!hasIdentityData)
                {
                    html.AppendLine("        <ul>");
                    html.AppendLine("          <li class=\"empty\">(no entries)</li>");
                    html.AppendLine("        </ul>");
                }
                else
                {
                    html.AppendLine("        <div class=\"identity-grid\">");
                    html.AppendLine("          <div class=\"identity-col\">");
                    foreach (var pair in left)
                    {
                        string value = string.IsNullOrWhiteSpace(pair.value) ? "-" : pair.value;
                        html.AppendLine($"            <div class=\"identity-row\"><span class=\"identity-key\">{EscapeHtml(pair.label)}:</span><span class=\"identity-val\">{EscapeHtml(value)}</span></div>");
                    }
                    html.AppendLine("          </div>");
                    html.AppendLine("          <div class=\"identity-col\">");
                    foreach (var pair in right)
                    {
                        string value = string.IsNullOrWhiteSpace(pair.value) ? "-" : pair.value;
                        html.AppendLine($"            <div class=\"identity-row\"><span class=\"identity-key\">{EscapeHtml(pair.label)}:</span><span class=\"identity-val\">{EscapeHtml(value)}</span></div>");
                    }
                    html.AppendLine("          </div>");
                    html.AppendLine("        </div>");
                }
            }
            else if (string.Equals(section.key, "weapons", StringComparison.OrdinalIgnoreCase))
            {
                var weaponRows = BuildHtmlWeaponRows(character);
                if (weaponRows.Count == 0)
                {
                    html.AppendLine("        <ul>");
                    html.AppendLine("          <li class=\"empty\">(no weapons carried)</li>");
                    html.AppendLine("        </ul>");
                }
                else
                {
                    html.AppendLine("        <div class=\"weapon-table-wrap\">");
                    html.AppendLine("          <table class=\"weapon-table\">");
                    html.AppendLine("            <thead><tr><th>ROLE</th><th>WEAPON</th><th>THAC0</th><th>SF</th><th>APR</th><th>DMG S/M</th><th>DMG L</th><th>TYPE</th><th>SZ</th><th>PROF</th></tr></thead>");
                    html.AppendLine("            <tbody>");
                    foreach (var row in weaponRows)
                    {
                        string badgeClass = row.Prof.Equals("SPEC", StringComparison.OrdinalIgnoreCase)
                            ? "spec"
                            : row.Prof.Equals("PROF", StringComparison.OrdinalIgnoreCase)
                                ? "prof"
                                : "np";
                        string thac0Class = row.IsProficient ? string.Empty : " class=\"thac0-np\"";
                        html.AppendLine("              <tr>");
                        html.AppendLine($"                <td>{EscapeHtml(row.Role)}</td>");
                        html.AppendLine($"                <td class=\"weapon-name\">{EscapeHtml(row.Weapon)}</td>");
                        html.AppendLine($"                <td{thac0Class}>{EscapeHtml(row.Thac0)}</td>");
                        html.AppendLine($"                <td>{EscapeHtml(row.Sf)}</td>");
                        html.AppendLine($"                <td>{EscapeHtml(row.Apr)}</td>");
                        html.AppendLine($"                <td>{EscapeHtml(row.DmgSm)}</td>");
                        html.AppendLine($"                <td>{EscapeHtml(row.DmgL)}</td>");
                        html.AppendLine($"                <td>{EscapeHtml(row.Type)}</td>");
                        html.AppendLine($"                <td>{EscapeHtml(row.Size)}</td>");
                        html.AppendLine($"                <td><span class=\"prof-badge {badgeClass}\">{EscapeHtml(row.Prof)}</span></td>");
                        html.AppendLine("              </tr>");
                    }
                    html.AppendLine("            </tbody>");
                    html.AppendLine("          </table>");
                    html.AppendLine("        </div>");
                }
            }
            else
            {
                html.AppendLine("        <ul>");
                if (section.lines.Count == 0)
                {
                    html.AppendLine("          <li class=\"empty\">(no entries)</li>");
                }
                else
                {
                    foreach (string line in section.lines)
                        html.AppendLine($"          <li>{EscapeHtml(line)}</li>");
                }
                html.AppendLine("        </ul>");
            }
            html.AppendLine("      </section>");
        }

        html.AppendLine("    </main>");
        html.AppendLine("  </div>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");

        return html.ToString();
    }

    private List<HtmlWeaponRow> BuildHtmlWeaponRows(CharacterSheet c)
    {
        var carriedWeapons = c.EquipmentSelections.Where(e => e.IsWeapon).ToList();
        if (carriedWeapons.Count == 0)
            return new List<HtmlWeaponRow>();

        string equippedMainKey = EquipmentLibraryService.CanonicalizeId(c.EquippedMainWeaponId);
        if (string.IsNullOrWhiteSpace(equippedMainKey))
            equippedMainKey = EquipmentLibraryService.CanonicalizeId(c.EquippedWeaponId);
        string equippedOffKey = EquipmentLibraryService.CanonicalizeId(c.EquippedOffHandWeaponId);
        var orderedWeapons = new List<EquipmentSelection>();
        var remainingWeapons = new List<EquipmentSelection>(carriedWeapons);

        void PullFirstByKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            var match = remainingWeapons.FirstOrDefault(w =>
                string.Equals(EquipmentLibraryService.CanonicalizeId(w.ItemId), key, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return;

            orderedWeapons.Add(match);
            remainingWeapons.Remove(match);
        }

        PullFirstByKey(equippedMainKey);
        PullFirstByKey(equippedOffKey);
        orderedWeapons.AddRange(remainingWeapons);

        bool isPlayersOption = string.Equals(c.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);
        var combatOptions = c.WeaponProficiencies
            .Where(x => string.Equals(x.ProficiencyType, "combat_option", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.ProficiencyId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int neutralWeaponIndex = 1;
        var rows = new List<HtmlWeaponRow>();

        foreach (var weapon in orderedWeapons)
        {
            string weaponKey = EquipmentLibraryService.CanonicalizeId(weapon.ItemId);
            string role;
            if (!string.IsNullOrWhiteSpace(equippedMainKey)
                && string.Equals(weaponKey, equippedMainKey, StringComparison.OrdinalIgnoreCase))
            {
                role = "PRIMARY";
            }
            else if (!string.IsNullOrWhiteSpace(equippedOffKey)
                && string.Equals(weaponKey, equippedOffKey, StringComparison.OrdinalIgnoreCase))
            {
                role = "OFF-HAND";
            }
            else
            {
                role = $"WEAPON {neutralWeaponIndex++}";
            }

            static HashSet<string> WordSet(string s) =>
                new(s.ToLowerInvariant().Split(new[] { ' ', ',', '-', '\'', '/' }, StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.OrdinalIgnoreCase);
            static bool TokenNameMatch(string a, string b)
            {
                if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (AreWeaponsEquivalent(a, b))
                    return true;

                var aWords = WordSet(a);
                var bWords = WordSet(b);
                if (aWords.Count == 0 || bWords.Count == 0)
                    return false;

                return aWords.SetEquals(bWords) || aWords.IsSubsetOf(bWords) || bWords.IsSubsetOf(aWords);
            }

            var itemWords = WordSet(weapon.ItemName);
            var weaponDef = _app.Rules.Weapons.FirstOrDefault(wd =>
                wd.Name.Equals(weapon.ItemName, StringComparison.OrdinalIgnoreCase)
                || WordSet(wd.Name).SetEquals(itemWords)
                || AreWeaponsEquivalent(wd.Id, weapon.ItemId)
                || AreWeaponsEquivalent(wd.Name, weapon.ItemName));

            bool proficient = false;
            bool specialized = false;
            string profNote = "NP";
            WeaponProficiencySelection? matchedSelection = null;

            foreach (var wp in c.WeaponProficiencies)
            {
                if (string.Equals(wp.ProficiencyType, "combat_option", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool match = false;
                if (wp.ProficiencyType == "individual")
                {
                    match = wp.DisplayName.Equals(weapon.ItemName, StringComparison.OrdinalIgnoreCase)
                        || (weaponDef != null && wp.ProficiencyId.Equals(weaponDef.Id, StringComparison.OrdinalIgnoreCase))
                        || AreWeaponsEquivalent(wp.ProficiencyId, weapon.ItemId)
                        || AreWeaponsEquivalent(wp.DisplayName, weapon.ItemName)
                        || (weaponDef != null && AreWeaponsEquivalent(wp.ProficiencyId, weaponDef.Id));
                }
                else if (wp.ProficiencyType == "tight_group" && weaponDef != null)
                {
                    match = wp.ProficiencyId.Equals(weaponDef.TightGroupId, StringComparison.OrdinalIgnoreCase);
                    if (!match && _app.Rules.TightGroups.TryGetValue(wp.ProficiencyId, out var tg))
                        match = tg.WeaponIds.Contains(weaponDef.Id, StringComparer.OrdinalIgnoreCase);
                }
                else if (wp.ProficiencyType == "broad_group" && weaponDef != null)
                {
                    match = wp.ProficiencyId.Equals(weaponDef.GroupId, StringComparison.OrdinalIgnoreCase);
                    if (!match)
                    {
                        var bg = _app.Rules.WeaponGroups.FirstOrDefault(g =>
                            g.Id.Equals(wp.ProficiencyId, StringComparison.OrdinalIgnoreCase));
                        if (bg != null)
                            match = bg.TightGroups.Any(tg => tg.WeaponIds.Contains(weaponDef.Id, StringComparer.OrdinalIgnoreCase));
                    }
                }

                if (!match)
                {
                    match = wp.DisplayName.Equals(weapon.ItemName, StringComparison.OrdinalIgnoreCase)
                        || weapon.ItemName.Contains(wp.DisplayName, StringComparison.OrdinalIgnoreCase)
                        || wp.DisplayName.Contains(weapon.ItemName, StringComparison.OrdinalIgnoreCase)
                        || TokenNameMatch(wp.DisplayName, weapon.ItemName);
                }

                if (match)
                {
                    proficient = true;
                    specialized = wp.Specialized;
                    profNote = specialized ? "SPEC" : "PROF";
                    matchedSelection = wp;
                    break;
                }
            }

            int trainingAtkBonus = 0;
            int trainingDmgBonus = 0;
            if (isPlayersOption && matchedSelection?.WeaponOfChoice == true) trainingAtkBonus += 1;
            if (isPlayersOption && matchedSelection?.WeaponExpertise == true) { trainingAtkBonus += 1; trainingDmgBonus += 1; }
            if (isPlayersOption && combatOptions.Contains("fighting_style_single") && role.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)) trainingAtkBonus += 1;
            if (isPlayersOption && combatOptions.Contains("fighting_style_two_weapon") && role.Equals("OFF-HAND", StringComparison.OrdinalIgnoreCase)) trainingAtkBonus += 1;
            if (isPlayersOption && combatOptions.Contains("fighting_style_two_handed") && string.Equals(weapon.WeaponSize, "L", StringComparison.OrdinalIgnoreCase)) trainingDmgBonus += 1;
            if (isPlayersOption && combatOptions.Contains("fighting_style_missile")
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

            int enemyAttackBonus = CombatModifierService.GetEnemyAttackBonus(c.Bonuses, string.Empty);
            int enemyDamageBonus = CombatModifierService.GetEnemyDamageBonus(c.Bonuses, string.Empty);

            int meleeAtkBonus = 0;
            int dmgBonus = 0;
            if (c.Abilities.TryGetValue("str", out int strScore))
            {
                int muscle = isPlayersOption && c.SubAbilities.TryGetValue("str_muscle", out int m) ? m : strScore;
                (meleeAtkBonus, dmgBonus) = GetStrMeleeBonuses(muscle, c.ExceptionalStrength);
            }

            bool isMissileWeapon;
            if (weaponDef is not null)
            {
                bool isBow = string.Equals(weaponDef.GroupId, "bows", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(weaponDef.TightGroupId, "bows", StringComparison.OrdinalIgnoreCase);
                bool isCrossbow = string.Equals(weaponDef.GroupId, "crossbows", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(weaponDef.TightGroupId, "crossbows", StringComparison.OrdinalIgnoreCase);
                bool isSling = string.Equals(weaponDef.GroupId, "slings", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(weaponDef.TightGroupId, "slings", StringComparison.OrdinalIgnoreCase);
                bool isThrown = string.Equals(weaponDef.GroupId, "thrown", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(weaponDef.TightGroupId, "thrown", StringComparison.OrdinalIgnoreCase)
                    || weaponDef.GroupId.Contains("thrown", StringComparison.OrdinalIgnoreCase)
                    || weaponDef.TightGroupId.Contains("thrown", StringComparison.OrdinalIgnoreCase);
                isMissileWeapon = isBow || isCrossbow || isSling || isThrown;
            }
            else
            {
                isMissileWeapon = weapon.Category.Contains("missile", StringComparison.OrdinalIgnoreCase)
                    || weapon.Category.Contains("ranged", StringComparison.OrdinalIgnoreCase);
            }

            int globalAttackBonus = c.Bonuses?.AttackBonus ?? 0;
            int miscAttackBonus = globalAttackBonus - meleeAtkBonus;
            int statAttackBonus = isMissileWeapon ? 0 : meleeAtkBonus;
            int intrinsicAttackBonus = weapon.WeaponAttackBonus - weapon.WeaponThac0Modifier;
            int intrinsicDamageBonus = weapon.WeaponDamageBonus;
            int nonProfPenalty = proficient ? 0 : RulesEngine.GetNonProficiencyPenalty(c.ClassId ?? string.Empty);
            int offHandPenalty = role.Equals("OFF-HAND", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
            bool removesOffHandPenalty = (isPlayersOption && (combatOptions.Contains("fighting_style_two_weapon") || combatOptions.Contains("talent_ambidexterity") || combatOptions.Contains("ambidexterity")))
                || HasClassAbility(c, "ranger_two_weapon_style");
            if (offHandPenalty > 0 && removesOffHandPenalty)
                offHandPenalty = 0;

            bool isGrandMaster = matchedSelection?.GrandMastery == true;
            bool isHighMaster = isGrandMaster || matchedSelection?.HighMastery == true;
            bool hasMastery = isHighMaster || matchedSelection?.WeaponMastery == true || combatOptions.Contains("weapon_mastery");
            int specializationAttackBonus = hasMastery ? (isMissileWeapon ? 2 : 3) : specialized ? 1 : 0;
            int specializationDamageBonus = hasMastery ? (isMissileWeapon ? 0 : 3) : specialized ? 2 : 0;
            int totalAttackBonus = statAttackBonus + miscAttackBonus + intrinsicAttackBonus + trainingAtkBonus + specializationAttackBonus + enemyAttackBonus;
            int effectiveAtk = CharacterProgressionService.GetCombatBaseThac0(c) - totalAttackBonus + nonProfPenalty + offHandPenalty;
            int totalDamageBonus = dmgBonus + intrinsicDamageBonus + trainingDmgBonus + specializationDamageBonus + enemyDamageBonus;

            if (isGrandMaster)
                profNote = "G-MSTR";
            else if (isHighMaster)
                profNote = "H-MSTR";
            else if (hasMastery)
                profNote = "MSTR";

            var thac0Notes = new List<string>();
            if (!proficient)
                thac0Notes.Add($"NP {FormatSigned(nonProfPenalty)}");
            if (offHandPenalty > 0)
                thac0Notes.Add($"off-hand {FormatSigned(offHandPenalty)}");
            if (intrinsicAttackBonus != 0)
                thac0Notes.Add($"weapon {FormatSigned(-intrinsicAttackBonus)}");
            if (enemyAttackBonus != 0)
                thac0Notes.Add($"enemy {FormatSigned(enemyAttackBonus)}");

            string thac0Display = thac0Notes.Count > 0
                ? $"{effectiveAtk} ({string.Join(", ", thac0Notes)})"
                : effectiveAtk.ToString(CultureInfo.InvariantCulture);

            string rawDmgSm = isGrandMaster ? UpgradeDamageDie(weapon.WeaponDamageSmallMedium) : weapon.WeaponDamageSmallMedium;
            string rawDmgL = isGrandMaster ? UpgradeDamageDie(weapon.WeaponDamageLarge) : weapon.WeaponDamageLarge;

            string dmgSuffix = totalDamageBonus > 0 ? $"+{totalDamageBonus}" : totalDamageBonus < 0 ? $"{totalDamageBonus}" : string.Empty;
            string dmgSm = string.IsNullOrWhiteSpace(rawDmgSm) ? "-"
                : totalDamageBonus != 0 ? $"{rawDmgSm}{dmgSuffix}" : rawDmgSm;
            string dmgL = string.IsNullOrWhiteSpace(rawDmgL) ? "-"
                : totalDamageBonus != 0 ? $"{rawDmgL}{dmgSuffix}" : rawDmgL;

            int effectiveSf = isHighMaster && weapon.WeaponSpeed > 0 ? Math.Max(1, weapon.WeaponSpeed - 3) : weapon.WeaponSpeed;
            string sfDisplay = effectiveSf > 0 ? effectiveSf.ToString(CultureInfo.InvariantCulture) : "-";

            rows.Add(new HtmlWeaponRow
            {
                Role = role,
                Weapon = weapon.ItemName,
                Thac0 = thac0Display,
                Sf = sfDisplay,
                Apr = CalculateWeaponApr(c, weaponDef, specialized, isGrandMaster),
                DmgSm = dmgSm,
                DmgL = dmgL,
                Type = string.IsNullOrWhiteSpace(weapon.WeaponType) ? "-" : weapon.WeaponType.ToUpperInvariant(),
                Size = string.IsNullOrWhiteSpace(weapon.WeaponSize) ? "-" : weapon.WeaponSize.ToUpperInvariant(),
                Prof = profNote,
                IsProficient = proficient,
            });
        }

        return rows;
    }

    private static string EscapeHtml(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
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

    private static IDocumentPaginatorSource? BuildPrintableVisualDocument(FrameworkElement source, double printableWidth, double printableHeight, PrintLayoutOptions options)
    {
        source.UpdateLayout();

        if (printableWidth <= 0 || printableHeight <= 0)
            return null;

        const double pageInset = 16;
        double contentWidth = Math.Max(1, printableWidth - (pageInset * 2));
        double contentHeight = Math.Max(1, printableHeight - (pageInset * 2));

        var panel = source as Panel;
        var sections = GetDescendantExpanders(source).ToList();
        bool canReorderInPanel = panel is not null
            && sections.Count > 0
            && panel.Children.OfType<Expander>().Count() == sections.Count;
        var originalExpansion = new List<bool>();
        var originalVisibility = new List<Visibility>();
        List<UIElement>? originalChildOrder = null;

        if (sections.Count > 0)
        {
            originalExpansion = sections.Select(section => section.IsExpanded).ToList();
            originalVisibility = sections.Select(section => section.Visibility).ToList();

            foreach (var section in sections)
            {
                string title = (section.Header as TextBlock)?.Text?.Trim() ?? string.Empty;
                var sectionOptions = options.GetOptionsFor(title);

                section.Visibility = sectionOptions.Include ? Visibility.Visible : Visibility.Collapsed;
                section.IsExpanded = true;
            }

            if (canReorderInPanel && panel is not null)
                originalChildOrder = ApplySectionOrder(panel, sections, options);

            source.UpdateLayout();
        }

        VisualThemeRestoreState? bwThemeState = null;

        try
        {
            double sourceWidth = source.ActualWidth;
            double sourceHeight = source.ActualHeight;
            if (sourceWidth <= 0 || sourceHeight <= 0)
                return null;
            RenderTargetBitmap? monochromeSnapshot = null;
            if (options.Theme == PrintDocumentTheme.BlackAndWhite)
            {
                bwThemeState = CaptureAndApplyBlackAndWhiteVisualTheme(source);
                source.UpdateLayout();

                int pixelWidth = Math.Max(1, (int)Math.Ceiling(sourceWidth));
                int pixelHeight = Math.Max(1, (int)Math.Ceiling(sourceHeight));
                monochromeSnapshot = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
                monochromeSnapshot.Render(source);
            }

            double scale = contentWidth / sourceWidth;
            if (scale <= 0)
                return null;

            double sourceHeightPerPage = contentHeight / scale;
            if (sourceHeightPerPage <= 0)
                return null;
            sourceHeightPerPage = Math.Max(1, Math.Floor(sourceHeightPerPage));

            var printSections = BuildPrintSections(source, sections, sourceHeight, options);
            var pageSlices = BuildLogicalPageSlices(printSections, sourceHeightPerPage);
            if (pageSlices.Count == 0)
                return null;

            var document = new FixedDocument();
            foreach (var (sliceStart, sliceHeight) in pageSlices)
            {
                double snappedStart = Math.Max(0, Math.Round(sliceStart));
                double snappedHeight = Math.Max(1, Math.Ceiling(sliceHeight));

                var page = new FixedPage
                {
                    Width = printableWidth,
                    Height = printableHeight,
                    Background = Brushes.White,
                };

                Brush brush = options.Theme == PrintDocumentTheme.BlackAndWhite && monochromeSnapshot is not null
                    ? new ImageBrush(monochromeSnapshot)
                    {
                        Viewbox = new Rect(0, snappedStart, sourceWidth, snappedHeight),
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewport = new Rect(0, 0, contentWidth, Math.Min(contentHeight, snappedHeight * scale)),
                        ViewportUnits = BrushMappingMode.Absolute,
                        Stretch = Stretch.Fill,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top,
                    }
                    : new VisualBrush(source)
                    {
                        Viewbox = new Rect(0, snappedStart, sourceWidth, snappedHeight),
                        ViewboxUnits = BrushMappingMode.Absolute,
                        Viewport = new Rect(0, 0, contentWidth, Math.Min(contentHeight, snappedHeight * scale)),
                        ViewportUnits = BrushMappingMode.Absolute,
                        Stretch = Stretch.Fill,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top,
                    };

                var content = new Rectangle
                {
                    Width = contentWidth,
                    Height = Math.Min(contentHeight, Math.Max(1, snappedHeight * scale)),
                    Fill = brush,
                    SnapsToDevicePixels = true,
                };

                content.Clip = new RectangleGeometry(new Rect(0, 0, content.Width, content.Height));

                FixedPage.SetLeft(content, pageInset);
                FixedPage.SetTop(content, pageInset);

                page.ClipToBounds = true;
                page.Children.Add(content);

                var pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(page);
                document.Pages.Add(pageContent);
            }

            return document;
        }
        finally
        {
            if (bwThemeState is not null)
            {
                RestoreVisualTheme(bwThemeState);
                source.UpdateLayout();
            }

            if (sections.Count > 0
                && originalExpansion.Count == sections.Count
                && originalVisibility.Count == sections.Count)
            {
                if (canReorderInPanel && panel is not null && originalChildOrder is not null)
                    RestorePanelChildren(panel, originalChildOrder);

                for (int i = 0; i < sections.Count; i++)
                {
                    sections[i].IsExpanded = originalExpansion[i];
                    sections[i].Visibility = originalVisibility[i];
                }

                source.UpdateLayout();
            }
        }
    }

    private static VisualThemeRestoreState CaptureAndApplyBlackAndWhiteVisualTheme(DependencyObject root)
    {
        var state = new VisualThemeRestoreState();
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (current is TextBlock textBlock)
            {
                state.TextBlocks.Add((textBlock, textBlock.Foreground, textBlock.Background));
                textBlock.Foreground = Brushes.Black;
                if (textBlock.Background is Brush textBackground)
                    textBlock.Background = ConvertBrushToMonochrome(textBackground, preserveContrast: true);
            }
            else if (current is Border border)
            {
                state.Borders.Add((border, border.BorderBrush, border.Background));
                if (border.BorderBrush is Brush borderBrush)
                    border.BorderBrush = ConvertBrushToMonochrome(borderBrush, preserveContrast: true);
                if (border.Background is Brush borderBackground)
                    border.Background = ConvertBrushToMonochrome(borderBackground, preserveContrast: false);
            }
            else if (current is Control control)
            {
                state.Controls.Add((control, control.Foreground, control.Background, control.BorderBrush));
                control.Foreground = Brushes.Black;
                if (control.Background is Brush controlBackground)
                    control.Background = ConvertBrushToMonochrome(controlBackground, preserveContrast: false);
                if (control.BorderBrush is Brush controlBorder)
                    control.BorderBrush = ConvertBrushToMonochrome(controlBorder, preserveContrast: true);
            }
            else if (current is Panel panel)
            {
                state.Panels.Add((panel, panel.Background));
                if (panel.Background is Brush panelBackground)
                    panel.Background = ConvertBrushToMonochrome(panelBackground, preserveContrast: false);
            }
            else if (current is Shape shape)
            {
                state.Shapes.Add((shape, shape.Fill, shape.Stroke));
                if (shape.Fill is Brush fillBrush)
                    shape.Fill = ConvertBrushToMonochrome(fillBrush, preserveContrast: false);
                if (shape.Stroke is Brush strokeBrush)
                    shape.Stroke = ConvertBrushToMonochrome(strokeBrush, preserveContrast: true);
            }

            int childCount = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < childCount; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }

        return state;
    }

    private static void RestoreVisualTheme(VisualThemeRestoreState state)
    {
        foreach (var (element, foreground, background) in state.TextBlocks)
        {
            element.Foreground = foreground;
            element.Background = background;
        }

        foreach (var (element, borderBrush, background) in state.Borders)
        {
            element.BorderBrush = borderBrush;
            element.Background = background;
        }

        foreach (var (element, foreground, background, borderBrush) in state.Controls)
        {
            element.Foreground = foreground;
            element.Background = background;
            element.BorderBrush = borderBrush;
        }

        foreach (var (element, background) in state.Panels)
            element.Background = background;

        foreach (var (element, fill, stroke) in state.Shapes)
        {
            element.Fill = fill;
            element.Stroke = stroke;
        }
    }

    private static Brush ConvertBrushToMonochrome(Brush brush, bool preserveContrast)
    {
        if (brush is not SolidColorBrush solid)
            return preserveContrast ? Brushes.Black : Brushes.White;

        if (solid.Color.A == 0)
            return brush;

        byte luminance = (byte)Math.Clamp(
            (int)Math.Round((solid.Color.R * 0.299) + (solid.Color.G * 0.587) + (solid.Color.B * 0.114)),
            0,
            255);

        if (preserveContrast)
        {
            byte contrast = luminance < 160 ? (byte)0 : (byte)96;
            return new SolidColorBrush(Color.FromArgb(solid.Color.A, contrast, contrast, contrast));
        }

        byte background = (byte)Math.Clamp(235 + ((luminance - 128) / 6), 225, 248);
        return new SolidColorBrush(Color.FromArgb(solid.Color.A, background, background, background));
    }

    private static List<string> BuildLivePageMap(FrameworkElement source, double printableWidth, double printableHeight, PrintLayoutOptions options)
    {
        source.UpdateLayout();
        if (printableWidth <= 0 || printableHeight <= 0)
            return new List<string> { "Unable to compute page map." };

        const double pageInset = 16;
        double contentWidth = Math.Max(1, printableWidth - (pageInset * 2));
        double contentHeight = Math.Max(1, printableHeight - (pageInset * 2));

        var panel = source as Panel;
        var sections = GetDescendantExpanders(source).ToList();
        bool canReorderInPanel = panel is not null
            && sections.Count > 0
            && panel.Children.OfType<Expander>().Count() == sections.Count;
        var originalExpansion = sections.Select(section => section.IsExpanded).ToList();
        var originalVisibility = sections.Select(section => section.Visibility).ToList();
        List<UIElement>? originalChildOrder = null;

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

            source.UpdateLayout();

            double sourceWidth = source.ActualWidth;
            double sourceHeight = source.ActualHeight;
            if (sourceWidth <= 0 || sourceHeight <= 0)
                return new List<string> { "Unable to compute page map." };

            double scale = contentWidth / sourceWidth;
            if (scale <= 0)
                return new List<string> { "Unable to compute page map." };

            double sourceHeightPerPage = Math.Max(1, Math.Floor(contentHeight / scale));
            var printSections = BuildPrintSections(source, sections, sourceHeight, options);
            var pageSlices = BuildLogicalPageSlices(printSections, sourceHeightPerPage);
            if (pageSlices.Count == 0)
                return new List<string> { "No printable sections selected." };

            var lines = new List<string>();
            for (int i = 0; i < pageSlices.Count; i++)
            {
                var (start, height) = pageSlices[i];
                double end = start + height;
                var titles = printSections
                    .Where(section => section.top < end && section.top + section.height > start)
                    .Select(section => section.title)
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

            for (int i = 0; i < sections.Count && i < originalExpansion.Count && i < originalVisibility.Count; i++)
            {
                sections[i].IsExpanded = originalExpansion[i];
                sections[i].Visibility = originalVisibility[i];
            }

            source.UpdateLayout();
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

    private static List<(string title, double top, double height, bool forceNewPageBefore, bool forceNewPageAfter)> BuildPrintSections(
        FrameworkElement source,
        List<Expander> sections,
        double sourceHeight,
        PrintLayoutOptions options)
    {
        var result = new List<(string title, double top, double height, bool forceNewPageBefore, bool forceNewPageAfter)>();

        var sectionByTitle = sections
            .Select(section => new
            {
                Title = (section.Header as TextBlock)?.Text?.Trim() ?? string.Empty,
                Section = section,
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .ToDictionary(item => item.Title, item => item.Section, StringComparer.OrdinalIgnoreCase);

        foreach (var planItem in options.GetSectionPlan())
        {
            if (!sectionByTitle.TryGetValue(planItem.Title, out var section))
                continue;

            if (!section.IsVisible || section.ActualHeight <= 0)
                continue;

            string title = planItem.Title;

            bool forceBreakBefore = planItem.BreakBefore
                || (result.Count > 0 && string.Equals(title, "SPELLS", StringComparison.OrdinalIgnoreCase));
            bool forceBreakAfter = false;

            if (string.Equals(title, "SPELLS", StringComparison.OrdinalIgnoreCase)
                && section.Content is StackPanel spellPanel)
            {
                var spellGroups = spellPanel.Children
                    .OfType<WrapPanel>()
                    .SelectMany(panel => panel.Children.OfType<FrameworkElement>())
                    .Where(child => child.IsVisible && child.ActualHeight > 0)
                    .ToList();

                if (spellGroups.Count > 0)
                {
                    double sectionTop = Math.Max(0, section.TransformToAncestor(source).Transform(new Point(0, 0)).Y);
                    bool firstCard = true;
                    foreach (var group in spellGroups)
                    {
                        var groupOrigin = group.TransformToAncestor(source).Transform(new Point(0, 0));
                        double groupTop = Math.Max(0, groupOrigin.Y);
                        double groupHeight = Math.Min(group.ActualHeight, Math.Max(0, sourceHeight - groupTop));
                        if (groupHeight <= 0)
                            continue;

                        double cardTop = firstCard ? Math.Min(sectionTop, groupTop) : groupTop;
                        double cardBottom = Math.Min(sourceHeight, groupTop + groupHeight);

                        // For spell cards, break on row boundaries when possible so rows are
                        // not split across pages. Keep card headers attached to the first row.
                        if (group is Border { Child: StackPanel cardStack })
                        {
                            var cardParts = cardStack.Children
                                .OfType<FrameworkElement>()
                                .Where(child => child.IsVisible && child.ActualHeight > 0)
                                .ToList();

                            if (cardParts.Count > 0)
                            {
                                int firstDataIndex = Math.Min(3, cardParts.Count);
                                var firstDataPart = cardParts.Skip(firstDataIndex).FirstOrDefault();

                                if (firstDataPart is not null)
                                {
                                    var firstDataOrigin = firstDataPart.TransformToAncestor(source).Transform(new Point(0, 0));
                                    double firstDataTop = Math.Max(cardTop, firstDataOrigin.Y);
                                    double firstDataBottom = Math.Min(sourceHeight, firstDataTop + firstDataPart.ActualHeight);

                                    double headerWithFirstRowHeight = Math.Max(1, firstDataBottom - cardTop);
                                    result.Add((title, cardTop, headerWithFirstRowHeight, firstCard && forceBreakBefore, false));

                                    foreach (var part in cardParts.Skip(firstDataIndex + 1))
                                    {
                                        var partOrigin = part.TransformToAncestor(source).Transform(new Point(0, 0));
                                        double partTop = Math.Max(0, partOrigin.Y);
                                        double partHeight = Math.Min(part.ActualHeight, Math.Max(0, sourceHeight - partTop));
                                        if (partHeight <= 0)
                                            continue;

                                        result.Add((title, partTop, Math.Max(1, partHeight), false, false));
                                    }
                                }
                                else
                                {
                                    result.Add((title, cardTop, Math.Max(1, cardBottom - cardTop), firstCard && forceBreakBefore, false));
                                }
                            }
                            else
                            {
                                result.Add((title, cardTop, Math.Max(1, cardBottom - cardTop), firstCard && forceBreakBefore, false));
                            }
                        }
                        else
                        {
                            result.Add((title, cardTop, Math.Max(1, cardBottom - cardTop), firstCard && forceBreakBefore, false));
                        }

                        firstCard = false;
                    }

                    if (result.Count > 0)
                    {
                        var last = result[^1];
                        result[^1] = (last.title, last.top, last.height, last.forceNewPageBefore, forceBreakAfter);
                    }

                    continue;
                }
            }

            var origin = section.TransformToAncestor(source).Transform(new Point(0, 0));
            double top = Math.Max(0, origin.Y);
            double height = Math.Min(section.ActualHeight, Math.Max(0, sourceHeight - top));
            if (height <= 0)
                continue;

            result.Add((title, top, height, forceBreakBefore, forceBreakAfter));
        }

        return result;
    }

    private static List<(double start, double height)> BuildLogicalPageSlices(
        List<(string title, double top, double height, bool forceNewPageBefore, bool forceNewPageAfter)> sections,
        double pageHeight)
    {
        var slices = new List<(double start, double height)>();
        if (sections.Count == 0 || pageHeight <= 0)
            return slices;

        // Split any oversized section into page-sized chunks first.
        // This keeps text at normal size and lets content flow across pages
        // instead of shrinking an entire large section to fit one page.
        var normalizedSections = new List<(string title, double top, double height, bool forceNewPageBefore, bool forceNewPageAfter)>();
        foreach (var section in sections)
        {
            double sectionTop = Math.Max(0, section.top);
            double remaining = Math.Max(1, section.height);
            bool firstChunk = true;

            while (remaining > 0)
            {
                double chunkHeight = Math.Min(pageHeight, remaining);
                bool forceBefore = firstChunk && section.forceNewPageBefore;
                bool isLastChunk = remaining <= pageHeight;
                bool forceAfter = isLastChunk && section.forceNewPageAfter;

                normalizedSections.Add((section.title, sectionTop, chunkHeight, forceBefore, forceAfter));

                sectionTop += chunkHeight;
                remaining -= chunkHeight;
                firstChunk = false;
            }
        }

        // Build pages from true Y extents so each slice maps to a single, non-overlapping
        // visual region of the live panel.
        var orderedSections = normalizedSections
            .OrderBy(section => section.top)
            .ToList();

        double currentStart = 0;
        double currentEnd = 0;
        bool hasContent = false;

        void Flush()
        {
            if (!hasContent)
                return;

            double height = Math.Max(1, currentEnd - currentStart);
            slices.Add((currentStart, height));
            hasContent = false;
            currentStart = 0;
            currentEnd = 0;
        }

        foreach (var section in orderedSections)
        {
            double sectionTop = Math.Max(0, section.top);
            double sectionEnd = Math.Max(sectionTop + 1, section.top + Math.Max(1, section.height));

            if (section.forceNewPageBefore && hasContent)
                Flush();

            if (!hasContent)
            {
                currentStart = sectionTop;
                currentEnd = sectionEnd;
                hasContent = true;
            }
            else
            {
                double proposedEnd = Math.Max(currentEnd, sectionEnd);
                if (proposedEnd - currentStart > pageHeight)
                {
                    Flush();
                    currentStart = sectionTop;
                    currentEnd = sectionEnd;
                    hasContent = true;
                }
                else
                {
                    currentEnd = proposedEnd;
                }
            }

            if (section.forceNewPageAfter && hasContent)
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

            if (!string.IsNullOrWhiteSpace(section.heading))
            {
                sectionStack.Children.Add(new Border
                {
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    BorderBrush = headerBrush,
                    Padding = new Thickness(0, 0, 0, 3),
                    Margin = new Thickness(0, 0, 0, 5),
                    Child = new TextBlock
                    {
                        Text = section.heading,
                        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        Foreground = titleBrush,
                        TextWrapping = TextWrapping.Wrap,
                    },
                });
            }

            foreach (string line in section.lines)
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

        return doc;
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

            string? matchingVisualTitle = TextSectionByVisualTitle
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
                _sectionLayoutByKey[card.Key].Lane = Math.Clamp(_sectionLayoutByKey[card.Key].Lane, 0, LayoutLaneCount - 1);
                _sectionLayoutByKey[card.Key].Span = Math.Clamp(_sectionLayoutByKey[card.Key].Span, 1, LayoutLaneCount);
                continue;
            }

            _sectionLayoutByKey[card.Key] = new SectionPlacement
            {
                Order = nextRow++,
                Lane = 0,
                Span = DefaultSectionSpan,
            };
        }

        NormalizeLayoutRows();
    }

    private void NormalizeLayoutRows()
    {
        var rowSlots = new SortedDictionary<int, string?[]>();
        foreach (var kv in _sectionLayoutByKey
            .OrderBy(kv => Math.Max(0, kv.Value.Order))
            .ThenBy(kv => Math.Clamp(kv.Value.Lane, 0, LayoutLaneCount - 1)))
        {
            int targetRow = Math.Max(0, kv.Value.Order);
            int span = Math.Clamp(kv.Value.Span, 1, LayoutLaneCount);
            int targetLane = Math.Clamp(kv.Value.Lane, 0, LayoutLaneCount - span);
            PlaceWithSpill(rowSlots, targetRow, targetLane, span, kv.Key);
        }

        int normalizedRow = 0;
        foreach (var slots in rowSlots.Values.Where(values => values.Any(value => !string.IsNullOrWhiteSpace(value))))
        {
            var handledKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int lane = 0; lane < LayoutLaneCount; lane++)
            {
                string? key = slots[lane];
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                if (!_sectionLayoutByKey.ContainsKey(key) || handledKeys.Contains(key))
                    continue;

                int span = 1;
                for (int cursor = lane + 1; cursor < LayoutLaneCount; cursor++)
                {
                    if (!string.Equals(slots[cursor], key, StringComparison.OrdinalIgnoreCase))
                        break;
                    span++;
                }

                _sectionLayoutByKey[key].Order = normalizedRow;
                _sectionLayoutByKey[key].Lane = lane;
                _sectionLayoutByKey[key].Span = Math.Clamp(span, 1, LayoutLaneCount);
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

            for (int lane = 0; lane < LayoutLaneCount; lane++)
            {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (lane < LayoutLaneCount - 1)
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            }

            for (int lane = 0; lane < LayoutLaneCount; lane++)
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
                int lane = Math.Clamp(_sectionLayoutByKey[rowCards[i].Key].Lane, 0, LayoutLaneCount - 1);
                int span = Math.Clamp(_sectionLayoutByKey[rowCards[i].Key].Span, 1, LayoutLaneCount);
                Grid.SetColumn(rowCards[i].Expander, lane * 2);
                Grid.SetColumnSpan(rowCards[i].Expander, Math.Min((LayoutLaneCount * 2) - 1 - (lane * 2), (span * 2) - 1));
                Panel.SetZIndex(rowCards[i].Expander, 1);
                rowGrid.Children.Add(rowCards[i].Expander);
            }

            LaneSurface.Children.Add(rowGrid);
            LaneSurface.Children.Add(BuildInsertionTarget(rowIndex + 1));
        }
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
        int lane = (int)(dropXInLaneSurface / (width / LayoutLaneCount));
        return Math.Clamp(lane, 0, LayoutLaneCount - span);
    }

    private bool CanFitSectionAtRowLane(string sectionKey, int row, int lane)
    {
        var slots = BuildRowSlotsExcluding(sectionKey);
        int span = GetSectionSpan(sectionKey);
        int clampedLane = Math.Clamp(lane, 0, LayoutLaneCount - span);

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
            movingPlacement.Span = Math.Clamp(movingPlacement.Span, 1, LayoutLaneCount);
            movingPlacement.Lane = Math.Clamp(movingPlacement.Lane, 0, LayoutLaneCount - 1);
            movingPlacement.Order = Math.Max(0, movingPlacement.Order);
        }

        MoveSectionCard(sectionKey, targetRow, targetLane);
    }

    private int ResolveDropLane(Grid rowGrid, int targetRow, string movingKey, Point dropPointInRow)
    {
        _ = targetRow;
        int span = GetSectionSpan(movingKey);
        double width = Math.Max(1, rowGrid.ActualWidth);
        int lane = (int)(dropPointInRow.X / (width / LayoutLaneCount));
        return Math.Clamp(lane, 0, LayoutLaneCount - span);
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
        int clampedLane = Math.Clamp(targetLane, 0, LayoutLaneCount - span);

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
        int clampedLane = Math.Clamp(targetLane, 0, LayoutLaneCount - span);

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
            .ThenBy(kv => Math.Clamp(kv.Value.Lane, 0, LayoutLaneCount - 1)))
        {
            int row = Math.Max(0, kv.Value.Order);
            int lane = Math.Clamp(kv.Value.Lane, 0, LayoutLaneCount - 1);

            if (!rows.TryGetValue(row, out var slots))
            {
                slots = new string?[LayoutLaneCount];
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
                    probeSlots = new string?[LayoutLaneCount];
                    rows[probeRow] = probeSlots;
                }

                for (int probeLane = 0; probeLane < LayoutLaneCount; probeLane++)
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
        int clampedLane = Math.Clamp(lane, 0, LayoutLaneCount - 1);

        if (!rows.TryGetValue(clampedRow, out var slots))
        {
            slots = new string?[LayoutLaneCount];
            rows[clampedRow] = slots;
        }

        if (!string.IsNullOrWhiteSpace(slots[clampedLane]))
            return false;

        slots[clampedLane] = sectionKey;
        return true;
    }

    private static bool TryFindOpenLaneInRow(SortedDictionary<int, string?[]> rows, int row, int preferredLane, out int openLane)
    {
        openLane = Math.Clamp(preferredLane, 0, LayoutLaneCount - 1);
        if (!rows.TryGetValue(Math.Max(0, row), out var slots))
            return true;

        if (string.IsNullOrWhiteSpace(slots[openLane]))
            return true;

        for (int offset = 1; offset < LayoutLaneCount; offset++)
        {
            int right = openLane + offset;
            if (right <= LayoutLaneCount - 1 && string.IsNullOrWhiteSpace(slots[right]))
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
            for (int lane = 0; lane < LayoutLaneCount; lane++)
            {
                string? key = slots[lane];
                if (string.IsNullOrWhiteSpace(key) || !_sectionLayoutByKey.ContainsKey(key))
                    continue;

                _sectionLayoutByKey[key].Order = normalizedRow;
                _sectionLayoutByKey[key].Lane = lane;
                _sectionLayoutByKey[key].Span = Math.Clamp(_sectionLayoutByKey[key].Span, 1, LayoutLaneCount);
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
            _sectionLayoutByKey[rowKeys[i]].Lane = Math.Clamp(i * DefaultSectionSpan, 0, LayoutLaneCount - 1);
            _sectionLayoutByKey[rowKeys[i]].Span = DefaultSectionSpan;
        }

        NormalizeLayoutRows();
    }

    private void MoveSectionAboveSection(string movingSectionKey, string targetSectionKey)
    {
        if (!_sectionLayoutByKey.TryGetValue(movingSectionKey, out var movingPlacement))
            return;
        if (!_sectionLayoutByKey.TryGetValue(targetSectionKey, out var targetPlacement))
            return;

        movingPlacement.Span = Math.Clamp(movingPlacement.Span, 1, LayoutLaneCount);

        int insertionRow = Math.Max(0, targetPlacement.Order);
        int targetLane = Math.Clamp(targetPlacement.Lane, 0, LayoutLaneCount - 1);

        var rowSlots = BuildRowSlotsExcluding(movingSectionKey);
        var shifted = new SortedDictionary<int, string?[]>();
        foreach (var kv in rowSlots)
        {
            int row = kv.Key >= insertionRow ? kv.Key + 1 : kv.Key;
            shifted[row] = kv.Value;
        }

        int movingSpan = Math.Clamp(movingPlacement.Span, 1, LayoutLaneCount);
        int lane = Math.Clamp(targetLane, 0, LayoutLaneCount - movingSpan);
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
            .ThenBy(kv => Math.Clamp(kv.Value.Lane, 0, LayoutLaneCount - 1)))
        {
            int row = Math.Max(0, kv.Value.Order);
            int span = Math.Clamp(kv.Value.Span, 1, LayoutLaneCount);
            int lane = Math.Clamp(kv.Value.Lane, 0, LayoutLaneCount - span);
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
            for (int lane = 0; lane < LayoutLaneCount; lane++)
            {
                string? key = slots[lane];
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                if (!_sectionLayoutByKey.ContainsKey(key) || handledKeys.Contains(key))
                    continue;

                int span = 1;
                for (int cursor = lane + 1; cursor < LayoutLaneCount; cursor++)
                {
                    if (!string.Equals(slots[cursor], key, StringComparison.OrdinalIgnoreCase))
                        break;
                    span++;
                }

                _sectionLayoutByKey[key].Order = normalizedRow;
                _sectionLayoutByKey[key].Lane = lane;
                _sectionLayoutByKey[key].Span = Math.Clamp(span, 1, LayoutLaneCount);
                handledKeys.Add(key);
            }
            normalizedRow++;
        }
    }

    private int GetSectionSpan(string sectionKey)
    {
        if (_sectionLayoutByKey.TryGetValue(sectionKey, out var placement))
            return Math.Clamp(placement.Span, 1, LayoutLaneCount);
        return DefaultSectionSpan;
    }

    private void PlaceWithSpill(SortedDictionary<int, string?[]> rowSlots, int startRow, int startLane, int span, string sectionKey)
    {
        int row = Math.Max(0, startRow);
        int clampedSpan = Math.Clamp(span, 1, LayoutLaneCount);
        int lane = Math.Clamp(startLane, 0, LayoutLaneCount - clampedSpan);

        if (!rowSlots.TryGetValue(row, out var slots))
        {
            slots = new string?[LayoutLaneCount];
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

        for (int i = 0; i < LayoutLaneCount; i++)
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
        if (nextLane >= LayoutLaneCount)
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
            if (displacedLane + displacedSpan - 1 >= LayoutLaneCount)
            {
                displacedRow++;
                displacedLane = 0;
            }

            displacedLane = Math.Clamp(displacedLane, 0, LayoutLaneCount - displacedSpan);
            PlaceWithSpill(rowSlots, displacedRow, displacedLane, displacedSpan, displacedKey);

            nextRow = displacedRow;
            nextLane = displacedLane + displacedSpan;
            if (nextLane >= LayoutLaneCount)
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

        string profileKey = BuildSheetLayoutProfileKey(character);
        if (!character.SheetLayoutByProfile.TryGetValue(profileKey, out var saved)
            || saved is null)
        {
            return;
        }

        bool looksLegacyThreeLane = saved.Placements.Count > 0
            && saved.Placements.All(p => (p.Span <= 0 ? 1 : p.Span) <= 3 && Math.Max(p.Column, p.Lane) <= 2);

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

            int span = Math.Clamp(placement.Span <= 0 ? 1 : placement.Span, 1, LayoutLaneCount);

            if (looksLegacyThreeLane)
            {
                lane *= 2;
                span *= 2;
            }

            _sectionLayoutByKey[placement.SectionKey] = new SectionPlacement
            {
                Lane = Math.Clamp(lane, 0, LayoutLaneCount - 1),
                Order = Math.Max(0, row),
                Span = Math.Clamp(span, 1, LayoutLaneCount),
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
                    Span = Math.Clamp(kv.Value.Span, 1, LayoutLaneCount),
                })
                .ToList(),
        };

        _selectedCharacter.SheetLayoutByProfile[profileKey] = preference;
        _app.SaveCharacters();
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

    private void LoadExpandedSectionPreference(CharacterSheet character)
    {
        _expandedSectionKeys.Clear();

        string profileKey = BuildSheetLayoutProfileKey(character);
        if (!character.ExpandedSectionKeysByProfile.TryGetValue(profileKey, out var expandedKeys)
            || expandedKeys is null)
        {
            return;
        }

        foreach (string key in expandedKeys.Where(k => !string.IsNullOrWhiteSpace(k)))
            _expandedSectionKeys.Add(key);
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

    private void SaveCurrentExpandedSectionPreference()
    {
        if (_selectedCharacter is null)
            return;

        string profileKey = BuildSheetLayoutProfileKey(_selectedCharacter);
        _selectedCharacter.ExpandedSectionKeysByProfile[profileKey] = _expandedSectionKeys
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
        LoadExpandedSectionPreference(c);

        void AddSection(string key, string title, bool expanded, Action<StackPanel> populate)
        {
            bool isExpanded = _expandedSectionKeys.Count > 0
                ? _expandedSectionKeys.Contains(key)
                : expanded;

            _sectionCards.Add(new SectionCard
            {
                Key = key,
                Title = title,
                Expander = BuildSection(key, title, isExpanded, populate),
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
            AddKV(identityLeft, "Gender",     string.IsNullOrWhiteSpace(c.Gender) ? "—" : c.Gender);
            AddKV(identityLeft, "Party",      string.IsNullOrWhiteSpace(c.Party)      ? "—" : c.Party);
            AddKV(identityLeft, "Ruleset",    ruleset);
            if (string.Equals(c.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase))
                AddKV(identityLeft, "CPs Available", c.UnspentCharacterPoints.ToString("N0", CultureInfo.InvariantCulture));

            AddKV(identityRight, "Race",       race);
            AddKV(identityRight, "Class",      cls);
            AddKV(identityRight, "Level",      FormatLevelDisplay(c));
            AddKV(identityRight, "Experience", FormatExperienceDisplay(c));
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
            AddKV(p, "Hit Points",    c.HitPoints.ToString(CultureInfo.InvariantCulture));
            string acDisplay = c.MissileArmorClass != c.ArmorClass
                ? $"{c.ArmorClass}  (Missile AC: {c.MissileArmorClass})"
                : c.ArmorClass.ToString(CultureInfo.InvariantCulture);
            AddKV(p, "Armor Class",   acDisplay);
            AddKV(p, "AC Breakdown",  BuildArmorClassBreakdown(c));
            AddKV(p, "Armor Profile", c.ArmorProfile.Replace("_", " "));
            AddKV(p, "THAC0",         CharacterProgressionService.GetCombatBaseThac0(c).ToString());
            AddKV(p, "THAC0 Breakdown", BuildThac0Breakdown(c));
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

            // Preserve equipped intent: render main-hand first, off-hand second,
            // then any remaining carried weapons.
            string equippedMainKey = EquipmentLibraryService.CanonicalizeId(c.EquippedMainWeaponId);
            if (string.IsNullOrWhiteSpace(equippedMainKey))
                equippedMainKey = EquipmentLibraryService.CanonicalizeId(c.EquippedWeaponId);
            string equippedOffKey = EquipmentLibraryService.CanonicalizeId(c.EquippedOffHandWeaponId);
            var orderedWeapons = new List<EquipmentSelection>();
            var remainingWeapons = new List<EquipmentSelection>(carriedWeapons);

            void PullFirstByKey(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return;

                var match = remainingWeapons.FirstOrDefault(w =>
                    string.Equals(EquipmentLibraryService.CanonicalizeId(w.ItemId), key, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return;

                orderedWeapons.Add(match);
                remainingWeapons.Remove(match);
            }

            PullFirstByKey(equippedMainKey);
            PullFirstByKey(equippedOffKey);
            orderedWeapons.AddRange(remainingWeapons);

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
            int neutralWeaponIndex = 1;
            for (int wi = 0; wi < orderedWeapons.Count; wi++)
            {
                string weaponKey = EquipmentLibraryService.CanonicalizeId(orderedWeapons[wi].ItemId);
                string role;
                if (!string.IsNullOrWhiteSpace(equippedMainKey)
                    && string.Equals(weaponKey, equippedMainKey, StringComparison.OrdinalIgnoreCase))
                {
                    role = "PRIMARY";
                }
                else if (!string.IsNullOrWhiteSpace(equippedOffKey)
                    && string.Equals(weaponKey, equippedOffKey, StringComparison.OrdinalIgnoreCase))
                {
                    role = "OFF-HAND";
                }
                else
                {
                    role = $"WEAPON {neutralWeaponIndex++}";
                }

                tbl.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                AddWeaponTableRow(tbl, wi + 2, c, orderedWeapons[wi], role, string.Empty, ColCount);
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
                string mastery = wp.GrandMastery
                    ? "  [Grand Mastery: +1 apr; +3 attack/+3 damage; upgraded damage dice; speed -3; crit on 16+]"
                    : wp.HighMastery
                    ? "  [High Mastery: +3 attack/+3 damage; speed factor -3; crit on 16+; extreme missile range]"
                    : wp.WeaponMastery
                    ? "  [Mastery: melee +3 attack/+3 damage; missile +2 attack beyond point blank, +3 attack/+3 damage at point blank]"
                    : string.Empty;
                string type = FormatProficiencyTypeBadge(wp);
                profLines.Add($"{wp.DisplayName}{type}{spec}{mastery}");
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

            AddTwoColumnItems(p, profLines);
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

            if (HasClassAbility(c, "ranger_tracking_proficiency"))
            {
                int trackingBonus = RangerTrackingRules.GetBonus(c);
                int plainTrackingIndex = nwp.FindIndex(x =>
                    string.Equals(x, "Tracking", StringComparison.OrdinalIgnoreCase));
                if (plainTrackingIndex >= 0)
                    nwp[plainTrackingIndex] = $"Tracking (+{trackingBonus})";
                else if (!nwp.Any(x => x.StartsWith("Tracking ", StringComparison.OrdinalIgnoreCase)))
                    nwp.Add($"Tracking (+{trackingBonus})");
            }

            if (nwp.Count == 0) { AddItem(p, "(none)"); return; }
            var profLines = nwp
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (string line in profLines)
                AddItem(p, AppendNwpConditionalModifiers(c, line));
        });

        // 7b. ROGUE ABILITIES
        var rogueAbilityLines = BuildRogueAbilityLines(c);
        bool showRogueSection = rogueAbilityLines.Count > 0 || IsRogueClassCharacter(c);
        AddSection("rogue_abilities", "ROGUE ABILITIES", false, p =>
        {
            if (rogueAbilityLines.Count == 0)
            {
                AddItem(p, "(none)");
                return;
            }

            foreach (string line in rogueAbilityLines)
                AddItem(p, line);
        });
        if (!showRogueSection)
            _hiddenSectionKeys.Add("rogue_abilities");

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
            int totalGemValue = GetTotalGemValueGoldPieces(c);
            int totalGemCount = GetTotalGemCount(c);
            AddKV(p, "Platinum Pieces", c.PlatinumPieces.ToString());
            AddKV(p, "Gold Pieces",   c.GoldPieces.ToString());
            AddKV(p, "Silver Pieces", c.SilverPieces.ToString());
            AddKV(p, "Copper Pieces", c.CopperPieces.ToString());
            AddKV(p, "Gem Count", totalGemCount.ToString());
            AddKV(p, "Gem Value", $"{totalGemValue:n0} gp");
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
        var unlockedStructuredAbilities = c.StructuredAbilities
            .Where(ab => RulesEngine.AbilityUnlockLevel(ab) <= Math.Max(1, c.Level))
            .ToList();
        bool hasAbilities = unlockedStructuredAbilities.Count > 0 || c.RacialAbilities.Count > 0;
        if (hasAbilities)
        {
            AddSection("abilities_special_powers", "ABILITIES & SPECIAL POWERS", false, p =>
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

    private static string BuildArmorClassBreakdown(CharacterSheet character)
    {
        int baseAc = character.BaseArmorClass;
        int finalAc = character.ArmorClass;

        int totalBonusAdjustment = character.Bonuses?.AcBonus ?? 0;
        var subTotals = SubAbilityTables.CalculateTotals(character.SubAbilities, character.ExceptionalStrength, character.ClassId);
        int dexAndSubAdjustment = subTotals.ArmorClassAdjustment;
        int abilityAdjustment = totalBonusAdjustment - dexAndSubAdjustment;
        int equipmentAndOtherAdjustment = finalAc - baseAc - totalBonusAdjustment;

        string dexPart = $"DEX/sub {FormatSigned(dexAndSubAdjustment)}";
        string abilityPart = $"abilities {FormatSigned(abilityAdjustment)}";
        string equipmentPart = $"equipment {FormatSigned(equipmentAndOtherAdjustment)}";

        string missilePart = character.MissileArmorClass != character.ArmorClass
            ? $" (Missile AC: {character.MissileArmorClass})"
            : string.Empty;

        return $"{baseAc} + ({dexPart}) + ({abilityPart}) + ({equipmentPart}) = {finalAc}{missilePart}";
    }

    private static string BuildThac0Breakdown(CharacterSheet character)
    {
        int baseThac0 = CharacterProgressionService.GetCombatBaseThac0(character);
        bool isPlayersOption = string.Equals(character.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);

        int meleeAttackBonus = 0;
        if (character.Abilities.TryGetValue("str", out int strength))
        {
            int muscle = isPlayersOption && character.SubAbilities.TryGetValue("str_muscle", out int muscleScore)
                ? muscleScore
                : strength;
            (meleeAttackBonus, _) = GetStrMeleeBonuses(muscle, character.ExceptionalStrength);
        }

        int globalAttackBonus = character.Bonuses?.AttackBonus ?? 0;
        int effectiveMeleeBeforeWeapon = baseThac0 - meleeAttackBonus - globalAttackBonus;

        string strPart = $"STR {FormatSigned(-meleeAttackBonus)}";
        string abilityPart = $"abilities {FormatSigned(-globalAttackBonus)}";

        return $"{baseThac0} + ({strPart}) + ({abilityPart}) = {effectiveMeleeBeforeWeapon} melee; weapon rows add training/spec and NP/off-hand/enemy mods.";
    }

    // ── Weapon card ───────────────────────────────────────────────────────────

    // ── Weapon card ───────────────────────────────────────────────────────────

    private void AddWeaponTableRow(Grid tbl, int rowIndex, CharacterSheet c, EquipmentSelection w, string role, string enemyContext, int colCount)
    {
        bool isPlayersOption = string.Equals(c.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);

        // Proficiency status — resolve via weapon catalog for group coverage
        bool proficient  = false;
        bool specialized = false;
        string profNote  = "NP";
        WeaponProficiencySelection? matchedSelection = null;

        // Find the weapon definition by matching ItemName to catalog.
        // Equipment names may differ in word order (e.g. "Short sword" vs "Sword, Short"),
        // so also compare by word-set (sorted lowercased tokens).
        static HashSet<string> WordSet(string s) =>
            new(s.ToLowerInvariant().Split(new[] { ' ', ',', '-', '\'', '/' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        static bool TokenNameMatch(string a, string b)
        {
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;

            var aWords = WordSet(a);
            var bWords = WordSet(b);
            if (aWords.Count == 0 || bWords.Count == 0)
                return false;

            if (aWords.SetEquals(bWords))
                return true;

            if (AreWeaponsEquivalent(a, b))
                return true;

            // Allow aliases with extra qualifiers, e.g. "Axe, Hand/Throwing" vs "Axe, Throwing".
            return aWords.IsSubsetOf(bWords) || bWords.IsSubsetOf(aWords);
        }
        var itemWords = WordSet(w.ItemName);
        var weaponDef = _app.Rules.Weapons.FirstOrDefault(wd =>
            wd.Name.Equals(w.ItemName, StringComparison.OrdinalIgnoreCase)
            || WordSet(wd.Name).SetEquals(itemWords)
            || AreWeaponsEquivalent(wd.Id, w.ItemId)
            || AreWeaponsEquivalent(wd.Name, w.ItemName));

        foreach (var wp in c.WeaponProficiencies)
        {
            if (string.Equals(wp.ProficiencyType, "combat_option", StringComparison.OrdinalIgnoreCase))
                continue;

            bool match = false;
            if (wp.ProficiencyType == "individual")
            {
                // Match by display name (covers direct name pick)
                 match = wp.DisplayName.Equals(w.ItemName, StringComparison.OrdinalIgnoreCase)
                     || (weaponDef != null && wp.ProficiencyId.Equals(weaponDef.Id, StringComparison.OrdinalIgnoreCase))
                     || AreWeaponsEquivalent(wp.ProficiencyId, w.ItemId)
                     || AreWeaponsEquivalent(wp.DisplayName, w.ItemName)
                     || (weaponDef != null && AreWeaponsEquivalent(wp.ProficiencyId, weaponDef.Id));
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
                     || wp.DisplayName.Contains(w.ItemName, StringComparison.OrdinalIgnoreCase)
                     || TokenNameMatch(wp.DisplayName, w.ItemName);
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

        if (isPlayersOption && matchedSelection?.WeaponOfChoice == true) trainingAtkBonus += 1;
        if (isPlayersOption && matchedSelection?.WeaponExpertise == true) { trainingAtkBonus += 1; trainingDmgBonus += 1; }

        if (isPlayersOption && combatOptions.Contains("fighting_style_single") && role.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase))
            trainingAtkBonus += 1;
        if (isPlayersOption && combatOptions.Contains("fighting_style_two_weapon") && role.Equals("OFF-HAND", StringComparison.OrdinalIgnoreCase))
            trainingAtkBonus += 1;
        if (isPlayersOption && combatOptions.Contains("fighting_style_two_handed") && string.Equals(w.WeaponSize, "L", StringComparison.OrdinalIgnoreCase))
            trainingDmgBonus += 1;
        if (isPlayersOption && combatOptions.Contains("fighting_style_missile")
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

        // STR bonuses (melee-only baseline used to separate misc/global attack modifiers)
        int meleeAtkBonus = 0, dmgBonus = 0;
        if (c.Abilities.TryGetValue("str", out int strScore))
        {
            int muscle = isPlayersOption && c.SubAbilities.TryGetValue("str_muscle", out int m) ? m : strScore;
            (meleeAtkBonus, dmgBonus) = GetStrMeleeBonuses(muscle, c.ExceptionalStrength);
        }

        bool isMissileWeapon = false;
        if (weaponDef is not null)
        {
            bool isBow = string.Equals(weaponDef.GroupId, "bows", StringComparison.OrdinalIgnoreCase)
                || string.Equals(weaponDef.TightGroupId, "bows", StringComparison.OrdinalIgnoreCase);
            bool isCrossbow = string.Equals(weaponDef.GroupId, "crossbows", StringComparison.OrdinalIgnoreCase)
                || string.Equals(weaponDef.TightGroupId, "crossbows", StringComparison.OrdinalIgnoreCase);
            bool isSling = string.Equals(weaponDef.GroupId, "slings", StringComparison.OrdinalIgnoreCase)
                || string.Equals(weaponDef.TightGroupId, "slings", StringComparison.OrdinalIgnoreCase);
            bool isThrown = string.Equals(weaponDef.GroupId, "thrown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(weaponDef.TightGroupId, "thrown", StringComparison.OrdinalIgnoreCase)
                || weaponDef.GroupId.Contains("thrown", StringComparison.OrdinalIgnoreCase)
                || weaponDef.TightGroupId.Contains("thrown", StringComparison.OrdinalIgnoreCase);
            isMissileWeapon = isBow || isCrossbow || isSling || isThrown;
        }
        else
        {
            isMissileWeapon = w.Category.Contains("missile", StringComparison.OrdinalIgnoreCase)
                || w.Category.Contains("ranged", StringComparison.OrdinalIgnoreCase);
        }

        int globalAttackBonus = c.Bonuses?.AttackBonus ?? 0;
        int miscAttackBonus = globalAttackBonus - meleeAtkBonus;
        int statAttackBonus = isMissileWeapon ? 0 : meleeAtkBonus;
        int intrinsicAttackBonus = w.WeaponAttackBonus - w.WeaponThac0Modifier;
        int intrinsicDamageBonus = w.WeaponDamageBonus;
        // Class-appropriate non-proficiency penalty (PHB p.52)
        int nonProfPenalty = proficient ? 0 : RulesEngine.GetNonProficiencyPenalty(c.ClassId ?? string.Empty);
        int offHandPenalty = role.Equals("OFF-HAND", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
        bool removesOffHandPenalty = (isPlayersOption && (combatOptions.Contains("fighting_style_two_weapon") || combatOptions.Contains("talent_ambidexterity") || combatOptions.Contains("ambidexterity")))
            || HasClassAbility(c, "ranger_two_weapon_style");
        if (offHandPenalty > 0 && removesOffHandPenalty)
            offHandPenalty = 0;
        bool isGrandMaster = matchedSelection?.GrandMastery == true;
        bool isHighMaster = isGrandMaster || matchedSelection?.HighMastery == true;
        bool hasMastery = isHighMaster || matchedSelection?.WeaponMastery == true || combatOptions.Contains("weapon_mastery");
        int specializationAttackBonus = hasMastery ? (isMissileWeapon ? 2 : 3) : specialized ? 1 : 0;
        int specializationDamageBonus = hasMastery ? (isMissileWeapon ? 0 : 3) : specialized ? 2 : 0;
        int totalAttackBonus = statAttackBonus + miscAttackBonus + intrinsicAttackBonus + trainingAtkBonus + specializationAttackBonus + enemyAttackBonus;
        int effectiveAtk   = CharacterProgressionService.GetCombatBaseThac0(c) - totalAttackBonus + nonProfPenalty + offHandPenalty;
        int totalDamageBonus = dmgBonus + intrinsicDamageBonus + trainingDmgBonus + specializationDamageBonus + enemyDamageBonus;

        if (isGrandMaster)
            profNote = "G-MSTR";
        else if (isHighMaster)
            profNote = "H-MSTR";
        else if (hasMastery)
            profNote = "MSTR";

        // Build THAC0 display: show base + modifiers breakdown when non-proficient
        var thac0Notes = new List<string>();
        if (!proficient)
            thac0Notes.Add($"NP {FormatSigned(nonProfPenalty)}");
        if (offHandPenalty > 0)
            thac0Notes.Add($"off-hand {FormatSigned(offHandPenalty)}");
        if (intrinsicAttackBonus != 0)
            thac0Notes.Add($"weapon {FormatSigned(-intrinsicAttackBonus)}");
        if (enemyAttackBonus != 0)
            thac0Notes.Add($"enemy {FormatSigned(enemyAttackBonus)}");
        string thac0Display = thac0Notes.Count > 0
            ? $"{effectiveAtk} ({string.Join(", ", thac0Notes)})"
            : effectiveAtk.ToString();

        string rawDmgSm = isGrandMaster ? UpgradeDamageDie(w.WeaponDamageSmallMedium) : w.WeaponDamageSmallMedium;
        string rawDmgL = isGrandMaster ? UpgradeDamageDie(w.WeaponDamageLarge) : w.WeaponDamageLarge;

        string dmgSuffix = totalDamageBonus > 0 ? $"+{totalDamageBonus}" : totalDamageBonus < 0 ? $"{totalDamageBonus}" : string.Empty;
        string dmgSM = string.IsNullOrWhiteSpace(rawDmgSm) ? "—"
                 : totalDamageBonus != 0 ? $"{rawDmgSm}{dmgSuffix}" : rawDmgSm;
        string dmgL  = string.IsNullOrWhiteSpace(rawDmgL) ? "—"
                 : totalDamageBonus != 0 ? $"{rawDmgL}{dmgSuffix}" : rawDmgL;

        int effectiveSf = isHighMaster && w.WeaponSpeed > 0 ? Math.Max(1, w.WeaponSpeed - 3) : w.WeaponSpeed;
        string sf   = effectiveSf > 0 ? effectiveSf.ToString() : "—";
        string apr  = CalculateWeaponApr(c, weaponDef, specialized, isGrandMaster);
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
            // Weapon type "P" means piercing damage, not "projectile/thrown".
            // Using Type here incorrectly applied thrown/missile bonuses to melee weapons
            // like daggers and short swords, producing overly low THAC0 values.
            bool isThrown = string.Equals(weaponDef.GroupId, "thrown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(weaponDef.TightGroupId, "thrown", StringComparison.OrdinalIgnoreCase)
                || weaponDef.GroupId.Contains("thrown", StringComparison.OrdinalIgnoreCase)
                || weaponDef.TightGroupId.Contains("thrown", StringComparison.OrdinalIgnoreCase);
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
        /// - Grand Mastery (+1 attack per round above specialist rate for warriors)
        /// Returns a display string like "1", "3/2", "2", "5/2", "3", "7/2".
        /// </summary>
        private static string CalculateWeaponApr(CharacterSheet c, WeaponDefinition? weaponDef, bool specialized, bool isGrandMaster = false)
        {
            bool isWarrior = c.ClassId is "fighter" or "paladin" or "ranger" || c.ClassIds.Any(id => id is "fighter" or "paladin" or "ranger");

            // Base APR in half-attacks (×2 denominator)
            int baseHalves;
            if (isWarrior)
            {
                // Derive from the warrior class's own level, not a stale primary-class snapshot.
                baseHalves = CharacterProgressionService.GetBaseAttackRate(c) switch
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

            // Grand Mastery grants +1 additional attack per round above specialist rate (warriors only)
            if (isGrandMaster && isWarrior)
                baseHalves += 2;

            return baseHalves switch
            {
                1 => "1/2",
                2 => "1",
                3 => "3/2",
                4 => "2",
                5 => "5/2",
                6 => "3",
                7 => "7/2",
                8 => "4",
                _ => $"{baseHalves}/2",
            };
        }

        private static string UpgradeDamageDie(string damageExpr)
        {
            if (string.IsNullOrWhiteSpace(damageExpr) || damageExpr == "-" || damageExpr == "—")
                return damageExpr;

            // Upgrades each die expression according to C&T Grand Mastery rules (p. 75):
            // 1d2 -> 1d3, 1d3 -> 1d4, 1d4 -> 1d6, 1d6 -> 1d8, 1d8 -> 1d10, 1d10 -> 1d12, 1d12 -> 1d20, 2d4 -> 2d6, 3d6 -> 3d8
            return Regex.Replace(damageExpr, @"(?<count>\d*)d(?<sides>\d+)", m =>
            {
                string count = m.Groups["count"].Value;
                if (!int.TryParse(m.Groups["sides"].Value, out int sides))
                    return m.Value;

                int newSides = sides switch
                {
                    2 => 3,
                    3 => 4,
                    4 => 6,
                    6 => 8,
                    8 => 10,
                    10 => 12,
                    12 => string.IsNullOrEmpty(count) || count == "1" ? 20 : 16,
                    _ => sides + 2,
                };

                return $"{count}d{newSides}";
            });
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

    private static string FormatSubAbilityScore(CharacterSheet character, string abilityKey, string subKey, int subScore)
    {
        if (string.Equals(abilityKey, "str", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(subKey, "str_muscle", StringComparison.OrdinalIgnoreCase)
                || string.Equals(subKey, "str_stamina", StringComparison.OrdinalIgnoreCase))
            && subScore == 18
            && character.ExceptionalStrength > 0)
        {
            return $"{subScore} ({character.ExceptionalStrength})";
        }

        return subScore.ToString(CultureInfo.InvariantCulture);
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

    private static string BuildCoreAbilityEffectSummary(CharacterSheet character, string abilityKey, int abilityScore)
    {
        var effects = new List<string>();

        foreach (string subKey in GetSubAbilityKeys(abilityKey))
        {
            int subScore = ResolveSubAbilityScore(character, abilityKey, subKey, abilityScore);
            string effect = ResolveSubAbilityEffect(character, subKey, subScore);
            if (!string.IsNullOrWhiteSpace(effect))
                effects.Add(effect);
        }

        return effects.Count == 0 ? "No derived effects" : string.Join(" | ", effects);
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

        if (!IsPlayersOptionMode(c))
        {
            bodyPanel.Children.Add(new TextBlock
            {
                Text = BuildCoreAbilityEffectSummary(c, abilityKey, score),
                FontFamily = (FontFamily)FindResource("FontBody"),
                FontSize = 12,
                Foreground = (Brush)FindResource("BrushText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });

            var coreOuter = new StackPanel();
            coreOuter.Children.Add(headerBorder);
            coreOuter.Children.Add(bodyPanel);

            parent.Children.Add(new Border
            {
                BorderBrush     = (Brush)FindResource("BrushBorder2"),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(3),
                Child           = coreOuter,
            });
            return;
        }

        foreach (string subKey in GetSubAbilityKeys(abilityKey))
        {
            int subScore = ResolveSubAbilityScore(c, abilityKey, subKey, score);
            string effect = ResolveSubAbilityEffect(c, subKey, subScore);
            string subScoreDisplay = FormatSubAbilityScore(c, abilityKey, subKey, subScore);
            bodyPanel.Children.Add(new TextBlock
            {
                Text = $"{GetSubAbilityDisplayLabel(c, subKey)}: {subScoreDisplay} - {effect}",
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

        foreach (var preset in WidthPresets)
        {
            int targetSpan = preset.Span;
            var widthItem = new MenuItem
            {
                Header = $"Set Width: {preset.Label}",
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

        expander.ContextMenu = contextMenu;
        expander.Expanded += (_, _) =>
        {
            _expandedSectionKeys.Add(key);
            SaveCurrentExpandedSectionPreference();
        };
        expander.Collapsed += (_, _) =>
        {
            _expandedSectionKeys.Remove(key);
            SaveCurrentExpandedSectionPreference();
        };
        expander.PreviewMouseLeftButtonDown += SectionCard_PreviewMouseLeftButtonDown;
        expander.PreviewMouseMove += SectionCard_PreviewMouseMove;
        return expander;
    }

    private static int GetTotalGemCount(CharacterSheet character)
    {
        if (character.Gems is { Count: > 0 })
            return character.Gems.Sum(gem => Math.Max(1, gem.Quantity));

        return Math.Max(0, character.GemCount);
    }

    private static int GetTotalGemValueGoldPieces(CharacterSheet character)
    {
        if (character.Gems is { Count: > 0 })
        {
            return character.Gems.Sum(gem =>
            {
                int quantity = Math.Max(1, gem.Quantity);
                int valueEach = Math.Max(0, gem.ValueGoldPieces);
                return quantity * valueEach;
            });
        }

        return Math.Max(0, character.GemValueGoldPieces);
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

        int newSpan = Math.Clamp(span, 1, LayoutLaneCount);
        int row = Math.Max(0, placement.Order);
        int lane = Math.Clamp(placement.Lane, 0, LayoutLaneCount - newSpan);

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
            for (int candidate = rightStart; candidate < LayoutLaneCount; candidate++)
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
        string widthLabel = WidthPresets.FirstOrDefault(p => p.Span == placement.Span).Label;
        if (string.IsNullOrWhiteSpace(widthLabel))
            widthLabel = $"{placement.Span}/{LayoutLaneCount} Row";
        StatusText.Text = BuildStatusText($"Set section width to {widthLabel}.");
    }

    private void BuildSpellsAtEndColumns(StackPanel panel, CharacterSheet character)
    {
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

            columnHost.Children.Add(BuildSpellLevelColumnCard($"Priest L{level}", slotText, spells, character));
            hasAnyColumns = true;
        }

        if (IsWizardCaster(character))
        {
            var wizardSpellsByLevel = GetAccessibleWizardSpellDefinitions(character)
                .GroupBy(s => TryParseSpellLevel(s.Level, out int level) ? level : int.MaxValue)
                .Where(g => g.Key != int.MaxValue)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList());

            foreach (int level in wizardSpellsByLevel.Keys.OrderBy(x => x))
            {
                int slots = character.ArcaneSpellSlots.TryGetValue(level, out int value) ? value : 0;
                columnHost.Children.Add(BuildSpellLevelColumnCard($"Wizard L{level}", $"Slots: {slots}", wizardSpellsByLevel[level], character));
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
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("BrushTitle"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontFamily = (FontFamily)FindResource("FontBody"),
            FontSize = 13,
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
                FontSize = 12,
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
                FontSize = 13,
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
                FontSize = 14,
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
            FontSize = 13,
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
            int trackingBonus = RangerTrackingRules.GetBonus(character);
            string conditionalModifiers = RangerTrackingRules.GetConditionalModifierSummary(character);
            entries.Add(new ClassBonusSpellLikeEntry(
                "Tracking",
                "Passive",
                $"NWP granted; +{trackingBonus} total Ranger bonus (+5 class, +1 per 3 levels){conditionalModifiers}"));
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

    private string AppendNwpConditionalModifiers(CharacterSheet character, string proficiencyText)
    {
        if (!string.Equals(CharGenState.NormalizeCharacterModeToken(character.CharacterMode),
            "players_option", StringComparison.OrdinalIgnoreCase))
            return proficiencyText;

        // Match the whole catalog name, not a substring in notes or another proficiency name.
        var proficiency = _app.CharacterOptions.GetCatalog().NonweaponProficiencies
            .OrderByDescending(definition => definition.Name.Length)
            .FirstOrDefault(definition => Regex.IsMatch(proficiencyText,
                @"^\s*" + Regex.Escape(definition.Name) + @"(?=\s+[+-]?\d+(?:\s|$|[,;])|\s*\(|\s*$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        if (proficiency is null)
            return proficiencyText;

        if (RangerTrackingRules.IsTracking(proficiency.Id, proficiency.Name))
        {
            if (proficiencyText.Contains("animal target", StringComparison.OrdinalIgnoreCase)
                || proficiencyText.Contains("Animal Lore", StringComparison.OrdinalIgnoreCase)
                || proficiencyText.Contains("Animal Empathy", StringComparison.OrdinalIgnoreCase)
                || proficiencyText.Contains("animal_lore_tracking", StringComparison.OrdinalIgnoreCase)
                || proficiencyText.Contains("animal_empathy_tracking", StringComparison.OrdinalIgnoreCase))
                return proficiencyText;

            return proficiencyText + RangerTrackingRules.GetConditionalModifierSummary(character);
        }

        // Use only the saved target. Never recalculate permanent bonuses on legacy strings.
        var targetMatch = Regex.Match(proficiencyText,
            @"^\s*" + Regex.Escape(proficiency.Name) + @"\s+(?<target>[+-]?\d+)(?=\s|$|[,;])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        bool hasTarget = int.TryParse(targetMatch.Groups["target"].Value,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int savedTarget);
        string conditional = NwpSynergyRules.GetConditionalSummary(character, proficiency, savedTarget);
        var additions = conditional.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
            .Where(line =>
            {
                string condition = line.Split(':', 2)[0];
                int sourceIndex = line.IndexOf("; source: ", StringComparison.Ordinal);
                string source = sourceIndex >= 0 ? line[(sourceIndex + 10)..] : string.Empty;
                var ruleId = Regex.Match(line, @"\((?<id>[^)]+)\)");
                return !proficiencyText.Contains(ruleId.Value, StringComparison.OrdinalIgnoreCase)
                    && !(proficiencyText.Contains(condition + ":", StringComparison.OrdinalIgnoreCase)
                        && source.Length > 0 && proficiencyText.Contains(source, StringComparison.OrdinalIgnoreCase));
            })
            // No saved numeric target (e.g. a name-only import): show modifiers, not an invented target.
            .Select(line => hasTarget ? line : Regex.Replace(line, @", target [+-]?\d+", string.Empty))
            .ToList();
        return additions.Count == 0
            ? proficiencyText
            : proficiencyText + "; conditional alternatives: " + string.Join("; ", additions);
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

        var idSet = new HashSet<string>(abilityIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        if (idSet.Count == 0)
            return false;

        var selectedIds = character.SelectedClassAbilityIds
            .Select(RulesEngine.ExtractClassAbilityBaseId)
            .Where(id => !string.IsNullOrWhiteSpace(id));

        if (selectedIds.Any(idSet.Contains))
            return true;

        foreach (var (_, entries) in character.SelectedAbilitiesByClass)
        {
            if (entries is null)
                continue;

            if (entries
                .Select(RulesEngine.ExtractClassAbilityBaseId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Any(idSet.Contains))
            {
                return true;
            }
        }

        int level = Math.Max(1, character.Level);
        return character.StructuredAbilities
            .Where(ab => RulesEngine.AbilityUnlockLevel(ab) <= level)
            .Any(ab => idSet.Contains(ab.Id));
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

        var wanted = new HashSet<string>(abilityIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
            yield break;

        foreach (var selectionEntry in character.SelectedClassAbilityIds)
        {
            string baseId = RulesEngine.ExtractClassAbilityBaseId(selectionEntry);
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
        return CharacterSavingThrowService.Calculate(character)
            .Select(result => new SaveRow(
                result.Label,
                result.CategoryKey,
                result.BaseTarget,
                result.Bonus,
                result.FinalTarget))
            .ToList();
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

    private static string FormatProficiencyTypePlain(WeaponProficiencySelection wp)
    {
        if (wp.ProficiencyType == "individual") return "Individual";
        if (wp.ProficiencyType == "tight_group") return "Tight Group";
        if (wp.ProficiencyType == "broad_group") return "Broad Group";
        if (string.Equals(wp.ProficiencyType, "combat_option", StringComparison.OrdinalIgnoreCase))
        {
            string id = (wp.ProficiencyId ?? string.Empty).ToLowerInvariant();
            if (id.StartsWith("armor_proficiency_")) return "Armor Proficiency";
            if (id == "shield_proficiency") return "Shield Proficiency";
            if (id.StartsWith("fighting_style_")) return "Fighting Style";
            if (id.StartsWith("talent_") || IsSpecialTalentId(id)) return "Special Talent";
            return "Combat Option";
        }
        return wp.ProficiencyType.Replace("_", " ");
    }

    private static string FormatProficiencyTypeBadge(WeaponProficiencySelection wp)
    {
        if (wp.ProficiencyType == "individual") return string.Empty;
        return $"  ({FormatProficiencyTypePlain(wp)})";
    }

    private static bool IsSpecialTalentId(string id)
    {
        string key = (id ?? string.Empty).Trim().ToLowerInvariant();
        if (key.StartsWith("talent_", StringComparison.OrdinalIgnoreCase))
            key = key["talent_".Length..];

        return key switch
        {
            "alertness" or "ambidexterity" or "ambush" or "camouflage" or "dirty_fighting"
            or "endurance" or "fine_balance" or "iron_will" or "leadership" or "quickness"
            or "steady_hand" or "trouble_sense" => true,
            _ => false,
        };
    }

    private static bool AreWeaponsEquivalent(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            return true;

        static string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");
        string na = Norm(a);
        string nb = Norm(b);
        if (na == nb) return true;

        if ((na.Contains("footmanspick") || na.Contains("pickfootman"))
            && (nb.Contains("footmanspick") || nb.Contains("pickfootman")))
            return true;

        if ((na.Contains("horsemanspick") || na.Contains("pickhorseman"))
            && (nb.Contains("horsemanspick") || nb.Contains("pickhorseman")))
            return true;

        return false;
    }

    private int GetDisplayedWizardSpellLevelForReview(CharacterSheet character, SpellDefinition spell)
    {
        if (!TryParseSpellLevel(spell.Level, out int baseLevel))
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
            || token.Equals("priest", StringComparison.OrdinalIgnoreCase)
            || RulesEngine.IsPriestClassId(token);
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
        if (character.ExpandedSectionKeysByProfile is null) { character.ExpandedSectionKeysByProfile = new(); changed = true; }

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
