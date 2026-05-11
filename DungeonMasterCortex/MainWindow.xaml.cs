using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;
using DungeonMasterCortex.Views;

namespace DungeonMasterCortex;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions CharacterSaveJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string CharacterSaveDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DungeonMasterCortex");

    private static readonly string CharacterSavePath = System.IO.Path.Combine(CharacterSaveDirectory, "characters.json");

    // ── Shared services ───────────────────────────────────────────────────────
    public readonly RulesEngine                  Rules            = new();
    public readonly CharacterOptionCatalogService CharacterOptions = new();
    public readonly CombatService                Combat           = new();
    public readonly CampaignService              Campaign         = new();
    public readonly LicenseService               License          = new(GetBuildEdition());

    public CharGenState CharGen    { get; } = new();
    public List<Models.CharacterSheet> Characters { get; } = new();

    // ── Screen registry ───────────────────────────────────────────────────────
    private readonly Dictionary<string, Lazy<IScreen>> _screens;
    private string  _currentScreen = "";
    private Action? _backAction;
    private Action? _nextAction;

    // Optional requested initial tab when navigating into Edit Information.
    public string PendingEditInfoTab { get; set; } = "";
    public int PendingEditCharacterIndex { get; set; } = -1;
    public bool PendingCharacterLevelUpMode { get; set; } = false;
    public int PendingCharacterSheetIndex { get; set; } = -1;

    private static readonly string[] CharGenOrder =
    {
        "hub", "chargen_name", "chargen_abilities",
        "chargen_race", "chargen_subrace", "chargen_class",
        "chargen_class_abilities",
        "chargen_subabilities",
        "chargen_character_options", "chargen_weapon_prof", "chargen_equipment", "chargen_wizard_spells", "chargen_review"
    };

    public MainWindow()
    {
        InitializeComponent();
        Title = License.Edition == AppEdition.Player ? "Player Codex" : "Dungeon Master Codex";
        BannerVersion.Text = $"v{AppUpdateService.GetCurrentVersion().ToString(3)}";

        _screens = new()
        {
            ["dice_roller"]       = new Lazy<IScreen>(() => new DiceRollerScreen(this)),
            ["hub"]               = new Lazy<IScreen>(() => new HubScreen(this)),
            ["characters"]        = new Lazy<IScreen>(() => new CharacterRosterScreen(this)),
            ["chargen_name"]      = new Lazy<IScreen>(() => new CharGenNameScreen(this)),
            ["chargen_abilities"] = new Lazy<IScreen>(() => new CharGenAbilitiesScreen(this)),
            ["chargen_subabilities"] = new Lazy<IScreen>(() => new CharGenSubAbilitiesScreen(this)),
            ["chargen_race"]      = new Lazy<IScreen>(() => new CharGenRaceScreen(this)),
            ["chargen_subrace"]   = new Lazy<IScreen>(() => new CharGenSubraceScreen(this)),
            ["chargen_class"]     = new Lazy<IScreen>(() => new CharGenClassScreen(this)),
            ["chargen_character_options"] = new Lazy<IScreen>(() => new CharGenCharacterOptionsScreen(this)),
            ["chargen_weapon_prof"]      = new Lazy<IScreen>(() => new CharGenWeaponProficienciesScreen(this)),
            ["chargen_equipment"]      = new Lazy<IScreen>(() => new CharGenEquipmentScreen(this)),
            ["chargen_wizard_spells"] = new Lazy<IScreen>(() => new CharGenWizardSpellsScreen(this)),
            ["chargen_class_abilities"] = new Lazy<IScreen>(() => new CharGenClassAbilitiesScreen(this)),
            ["chargen_wizard_spec"] = new Lazy<IScreen>(() => new CharGenWizardSpecScreen(this)),
            ["chargen_review"]    = new Lazy<IScreen>(() => new CharGenReviewScreen(this)),
            ["dm_tools"]          = new Lazy<IScreen>(() => new DmToolsScreen(this)),
            ["campaign"]          = new Lazy<IScreen>(() => new CampaignScreen(this)),
            ["character_sheets"]  = new Lazy<IScreen>(() => new CharacterSheetsScreen(this)),
            ["combat_tracker"]    = new Lazy<IScreen>(() => new CombatTrackerScreen(this)),
            ["edit_info"]         = new Lazy<IScreen>(() => new EditInfoScreen(this)),
        };

        LoadCharacters();
        Closing += (_, _) => SaveCharacters();

        GoTo("hub");
    }

    private static AppEdition GetBuildEdition()
    {
        return AppUpdateService.GetCurrentEdition();
    }

    public void SaveCharacters()
    {
        try
        {
            Directory.CreateDirectory(CharacterSaveDirectory);
            var json = JsonSerializer.Serialize(Characters, CharacterSaveJsonOptions);
            
            // DEBUG: Log what we're saving
            foreach (var character in Characters)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveJSON] {character.Name}: WizardSpellbookIds={character.WizardSpellbookIds?.Count ?? 0}, WizardSpellLists={character.WizardSpellLists?.Count ?? 0}");
                foreach (var list in character.WizardSpellLists ?? new List<NamedSpellList>())
                {
                    System.Diagnostics.Debug.WriteLine($"  [SaveJSON] List '{list.Name}': {list.SpellIds?.Count ?? 0} spells");
                }
            }
            
            File.WriteAllText(CharacterSavePath, json);
            System.Diagnostics.Debug.WriteLine($"[SaveJSON] Saved to: {CharacterSavePath}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to save characters to disk.\n\n{ex.Message}",
                "Save Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void LoadCharacters()
    {
        try
        {
            if (!File.Exists(CharacterSavePath))
                return;

            var json = File.ReadAllText(CharacterSavePath);
            var loaded = JsonSerializer.Deserialize<List<Models.CharacterSheet>>(json);
            if (loaded is null)
                return;

            // DEBUG: Log what we're loading
            System.Diagnostics.Debug.WriteLine($"[LoadJSON] Loading {loaded.Count} characters from {CharacterSavePath}");
            foreach (var character in loaded)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadJSON] {character.Name}: WizardSpellbookIds={character.WizardSpellbookIds?.Count ?? 0}, WizardSpellLists={character.WizardSpellLists?.Count ?? 0}");
                foreach (var list in character.WizardSpellLists ?? new List<NamedSpellList>())
                {
                    System.Diagnostics.Debug.WriteLine($"  [LoadJSON] List '{list.Name}': {list.SpellIds?.Count ?? 0} spells");
                }
            }

            bool migrated = NormalizeLoadedCharacterEquipmentIds(loaded);

            Characters.Clear();
            Characters.AddRange(loaded);

            if (migrated)
                SaveCharacters();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to load saved characters. Starting with an empty roster.\n\n{ex.Message}",
                "Load Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool NormalizeLoadedCharacterEquipmentIds(List<Models.CharacterSheet> characters)
    {
        bool changedAny = false;
        var library = new EquipmentLibraryService().GetEquipmentLibrary();
        var libraryById = library
            .GroupBy(x => EquipmentLibraryService.CanonicalizeId(x.Id), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (var character in characters)
        {
            character.EquipmentSelections ??= new List<EquipmentSelection>();

            var mergedById = new Dictionary<string, EquipmentSelection>(StringComparer.OrdinalIgnoreCase);
            int sourceCount = character.EquipmentSelections.Count;

            foreach (var selection in character.EquipmentSelections)
            {
                string canonicalId = string.IsNullOrWhiteSpace(selection.ItemId)
                    ? EquipmentLibraryService.BuildId(selection.ItemName)
                    : EquipmentLibraryService.CanonicalizeId(selection.ItemId);

                if (!string.Equals((selection.ItemId ?? string.Empty).Trim(), canonicalId, StringComparison.Ordinal))
                    changedAny = true;

                if (!mergedById.TryGetValue(canonicalId, out var existing))
                {
                    libraryById.TryGetValue(canonicalId, out var libraryItem);

                    mergedById[canonicalId] = new EquipmentSelection
                    {
                        ItemId = canonicalId,
                        Category = string.IsNullOrWhiteSpace(selection.Category) ? (libraryItem?.Categories.FirstOrDefault() ?? string.Empty) : selection.Category,
                        ItemName = string.IsNullOrWhiteSpace(selection.ItemName) ? (libraryItem?.Name ?? string.Empty) : selection.ItemName,
                        CostText = selection.CostText,
                        Quantity = Math.Max(1, selection.Quantity),
                        IsArmor = selection.IsArmor || (libraryItem?.IsArmor == true),
                        ArmorClassValue = selection.ArmorClassValue < 10 ? selection.ArmorClassValue : (libraryItem?.ArmorClassValue ?? selection.ArmorClassValue),
                        RogueArmorProfile = string.IsNullOrWhiteSpace(selection.RogueArmorProfile)
                            ? (libraryItem?.RogueArmorProfile ?? "no_armor")
                            : selection.RogueArmorProfile,
                        IsShield = selection.IsShield || (libraryItem?.IsShield == true),
                        IsWeapon = selection.IsWeapon || (libraryItem?.IsWeapon == true),
                        WeaponSpeed = Math.Max(0, selection.WeaponSpeed > 0 ? selection.WeaponSpeed : (libraryItem?.WeaponSpeed ?? 0)),
                        WeaponDamageSmallMedium = string.IsNullOrWhiteSpace(selection.WeaponDamageSmallMedium)
                            ? (libraryItem?.WeaponDamageSmallMedium ?? string.Empty)
                            : selection.WeaponDamageSmallMedium,
                        WeaponDamageLarge = string.IsNullOrWhiteSpace(selection.WeaponDamageLarge)
                            ? (libraryItem?.WeaponDamageLarge ?? string.Empty)
                            : selection.WeaponDamageLarge,
                        WeaponType = string.IsNullOrWhiteSpace(selection.WeaponType)
                            ? (libraryItem?.WeaponType ?? string.Empty)
                            : selection.WeaponType,
                        WeaponSize = string.IsNullOrWhiteSpace(selection.WeaponSize)
                            ? (libraryItem?.WeaponSize ?? string.Empty)
                            : selection.WeaponSize,
                        CostGoldEach = Math.Max(0, selection.CostGoldEach > 0 ? selection.CostGoldEach : (libraryItem?.CostGold ?? 0)),
                        CostSilverEach = Math.Max(0, selection.CostSilverEach > 0 ? selection.CostSilverEach : (libraryItem?.CostSilver ?? 0)),
                        CostCopperEach = Math.Max(0, selection.CostCopperEach > 0 ? selection.CostCopperEach : (libraryItem?.CostCopper ?? 0)),
                        SizeClassEach = string.IsNullOrWhiteSpace(selection.SizeClassEach)
                            ? (libraryItem?.SizeClass ?? "Medium")
                            : selection.SizeClassEach,
                        WeightEach = Math.Max(0, selection.WeightEach > 0 ? selection.WeightEach : (libraryItem?.Weight ?? 0)),
                    };
                }
                else
                {
                    existing.Quantity += Math.Max(1, selection.Quantity);
                    changedAny = true;

                    if (string.IsNullOrWhiteSpace(existing.ItemName) && !string.IsNullOrWhiteSpace(selection.ItemName))
                        existing.ItemName = selection.ItemName;
                    if (string.IsNullOrWhiteSpace(existing.Category) && !string.IsNullOrWhiteSpace(selection.Category))
                        existing.Category = selection.Category;
                    if (string.IsNullOrWhiteSpace(existing.CostText) && !string.IsNullOrWhiteSpace(selection.CostText))
                        existing.CostText = selection.CostText;
                    if (!existing.IsArmor && selection.IsArmor)
                        existing.IsArmor = true;
                    if (!existing.IsShield && selection.IsShield)
                        existing.IsShield = true;
                    if (existing.ArmorClassValue == 10 && selection.ArmorClassValue < 10)
                        existing.ArmorClassValue = selection.ArmorClassValue;
                    if (string.IsNullOrWhiteSpace(existing.RogueArmorProfile) || string.Equals(existing.RogueArmorProfile, "no_armor", StringComparison.OrdinalIgnoreCase))
                        existing.RogueArmorProfile = string.IsNullOrWhiteSpace(selection.RogueArmorProfile) ? "no_armor" : selection.RogueArmorProfile;
                    if (existing.WeightEach <= 0 && selection.WeightEach > 0)
                        existing.WeightEach = selection.WeightEach;
                    if (!existing.IsWeapon && selection.IsWeapon)
                        existing.IsWeapon = true;
                    if (existing.WeaponSpeed <= 0 && selection.WeaponSpeed > 0)
                        existing.WeaponSpeed = selection.WeaponSpeed;
                    if (string.IsNullOrWhiteSpace(existing.WeaponDamageSmallMedium) && !string.IsNullOrWhiteSpace(selection.WeaponDamageSmallMedium))
                        existing.WeaponDamageSmallMedium = selection.WeaponDamageSmallMedium;
                    if (string.IsNullOrWhiteSpace(existing.WeaponDamageLarge) && !string.IsNullOrWhiteSpace(selection.WeaponDamageLarge))
                        existing.WeaponDamageLarge = selection.WeaponDamageLarge;
                    if (string.IsNullOrWhiteSpace(existing.WeaponType) && !string.IsNullOrWhiteSpace(selection.WeaponType))
                        existing.WeaponType = selection.WeaponType;
                    if (string.IsNullOrWhiteSpace(existing.WeaponSize) && !string.IsNullOrWhiteSpace(selection.WeaponSize))
                        existing.WeaponSize = selection.WeaponSize;
                    if (existing.CostGoldEach <= 0 && selection.CostGoldEach > 0)
                        existing.CostGoldEach = selection.CostGoldEach;
                    if (existing.CostSilverEach <= 0 && selection.CostSilverEach > 0)
                        existing.CostSilverEach = selection.CostSilverEach;
                    if (existing.CostCopperEach <= 0 && selection.CostCopperEach > 0)
                        existing.CostCopperEach = selection.CostCopperEach;
                    if (string.IsNullOrWhiteSpace(existing.SizeClassEach) && !string.IsNullOrWhiteSpace(selection.SizeClassEach))
                        existing.SizeClassEach = selection.SizeClassEach;
                }
            }

            if (mergedById.Count != sourceCount)
                changedAny = true;

            character.EquipmentSelections = mergedById.Values
                .OrderBy(x => x.Category)
                .ThenBy(x => x.ItemName)
                .ToList();

            character.Equipment ??= new List<string>();
            var rebuiltEquipment = character.EquipmentSelections
                .OrderBy(x => x.Category)
                .ThenBy(x => x.ItemName)
                .Select(x => x.Quantity > 1 ? $"{x.ItemName} x{x.Quantity}" : x.ItemName)
                .ToList();

            if (!character.Equipment.SequenceEqual(rebuiltEquipment, StringComparer.Ordinal))
            {
                character.Equipment = rebuiltEquipment;
                changedAny = true;
            }

            // Ensure equipped slots reference items still present after merge/normalization.
            string equippedArmorKey = EquipmentLibraryService.CanonicalizeId(character.EquippedArmorId);
            string equippedShieldKey = EquipmentLibraryService.CanonicalizeId(character.EquippedShieldId);
            string equippedWeaponKey = EquipmentLibraryService.CanonicalizeId(character.EquippedWeaponId);

            if (!string.IsNullOrWhiteSpace(equippedArmorKey) && !mergedById.ContainsKey(equippedArmorKey))
            {
                character.EquippedArmorId = string.Empty;
                changedAny = true;
            }
            if (!string.IsNullOrWhiteSpace(equippedShieldKey) && !mergedById.ContainsKey(equippedShieldKey))
            {
                character.EquippedShieldId = string.Empty;
                changedAny = true;
            }
            if (!string.IsNullOrWhiteSpace(equippedWeaponKey) && !mergedById.ContainsKey(equippedWeaponKey))
            {
                character.EquippedWeaponId = string.Empty;
                changedAny = true;
            }

            int priorAc = character.ArmorClass;
            string priorProfile = character.RogueSkillArmorProfile ?? "no_armor";
            CharacterArmorService.RecalculateArmorForCharacter(character, library);
            if (character.ArmorClass != priorAc || !string.Equals(character.RogueSkillArmorProfile, priorProfile, StringComparison.OrdinalIgnoreCase))
                changedAny = true;
        }

        return changedAny;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    public void GoTo(string name, int direction = 1)
    {
        if (!IsScreenAllowedForEdition(name))
        {
            MessageBox.Show(
                "This feature is not available in the Player edition.",
                "Edition Restricted",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Array.IndexOf(CharGenOrder, _currentScreen) >= 0 &&
            Array.IndexOf(CharGenOrder, name) >= 0)
        {
            int ci = Array.IndexOf(CharGenOrder, _currentScreen);
            int ni = Array.IndexOf(CharGenOrder, name);
            direction = ni >= ci ? 1 : -1;
        }

        if (!_screens.TryGetValue(name, out var lazyScreen))
        {
            ShowNavigationError(name, new InvalidOperationException($"Unknown screen: {name}"));
            return;
        }

        try
        {
            var screen = lazyScreen.Value;
            PageHost.Content = screen.View;
            screen.OnEnter();
        }
        catch (Exception ex)
        {
            ShowNavigationError(name, ex);
            return;
        }

        if (_currentScreen != "")
        {
            var sb = (Storyboard)Resources[direction >= 0 ? "SlideInRight" : "SlideInLeft"];
            sb.Begin(this);
        }
        _currentScreen = name;

        bool isCharGen = name != "hub" && Array.IndexOf(CharGenOrder, name) > 0;
        NavBar.Visibility = isCharGen ? Visibility.Visible : Visibility.Collapsed;
        UpdateLevelUpQuickJumpBar(name, isCharGen);
    }

    private bool IsScreenAllowedForEdition(string screenName)
    {
        if (License.Edition != AppEdition.Player)
            return true;

        return !string.Equals(screenName, "dm_tools", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(screenName, "campaign", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(screenName, "combat_tracker", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(screenName, "edit_info", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateLevelUpQuickJumpBar(string currentScreen, bool isCharGen)
    {
        bool showQuickJumps = isCharGen && CharGen.IsLevelUpMode;
        LevelUpQuickJumpBar.Visibility = showQuickJumps ? Visibility.Visible : Visibility.Collapsed;
        if (!showQuickJumps)
            return;

        bool hasWizardSpells = IsWizardCasterInCharGen();
        BtnJumpSpells.Visibility = hasWizardSpells ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsWizardCasterInCharGen()
    {
        var classIds = CharGen.SelectedClassIds.Count > 0
            ? CharGen.SelectedClassIds
            : string.IsNullOrWhiteSpace(CharGen.ClassId)
                ? new List<string>()
                : new List<string> { CharGen.ClassId };

        return classIds.Any(id =>
            string.Equals(id, "wizard", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "mage", StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, "illusionist", StringComparison.OrdinalIgnoreCase));
    }

    private bool ConfirmSensitiveLevelUpJump(string typeName)
    {
        if (!CharGen.IsLevelUpMode)
            return true;

        var result = MessageBox.Show(
            $"⚠  You are opening {typeName} while updating this character.\n\nDid your DM say this was OK?",
            $"{typeName} — DM Approval Check",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private void BtnJumpRace_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSensitiveLevelUpJump("Race"))
            return;
        GoTo("chargen_race");
    }

    private void BtnJumpClass_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmSensitiveLevelUpJump("Class"))
            return;
        GoTo("chargen_class");
    }

    private void BtnJumpClassAbilities_Click(object sender, RoutedEventArgs e) => GoTo("chargen_class_abilities");
    private void BtnJumpOptions_Click(object sender, RoutedEventArgs e) => GoTo("chargen_character_options");
    private void BtnJumpWeaponProf_Click(object sender, RoutedEventArgs e) => GoTo("chargen_weapon_prof");
    private void BtnJumpEquipment_Click(object sender, RoutedEventArgs e) => GoTo("chargen_equipment");
    private void BtnJumpSpells_Click(object sender, RoutedEventArgs e)
    {
        if (BtnJumpSpells.Visibility == Visibility.Visible)
            GoTo("chargen_wizard_spells");
    }
    private void BtnJumpReview_Click(object sender, RoutedEventArgs e) => GoTo("chargen_review");

    private void ShowNavigationError(string screenName, Exception exception)
    {
        string errorText = BuildNavigationErrorText(screenName, exception);

        try
        {
            string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DMC_Error.txt");
            System.IO.File.WriteAllText(tempFile, errorText);
            errorText = $"{errorText}\n\nSaved to: {tempFile}";
        }
        catch
        {
            // Keep the original error text if writing the temp file fails.
        }

        var dialog = new ErrorDialog(errorText)
        {
            Owner = this,
            Title = "Navigation Error",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        dialog.ShowDialog();
    }

    private static string BuildNavigationErrorText(string screenName, Exception exception)
    {
        var lines = new List<string>
        {
            $"Screen: {screenName}",
            $"Error: {exception.GetType().FullName}",
            $"Message: {exception.Message}"
        };

        if (exception.InnerException is not null)
        {
            lines.Add(string.Empty);
            lines.Add($"Inner Error: {exception.InnerException.GetType().FullName}");
            lines.Add($"Inner Message: {exception.InnerException.Message}");
        }

        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            lines.Add(string.Empty);
            lines.Add("Stack:");
            lines.Add(exception.StackTrace);
        }

        return string.Join(Environment.NewLine, lines);
    }

    public void SetBanner(string subtitle) => BannerSubtitle.Text = subtitle;

    public void SetNavBar(int step, int total, string label,
                          Action backAction, Action nextAction,
                          string nextLabel = "NEXT  ▶")
    {
        _backAction = backAction;
        _nextAction = nextAction;
        BtnNext.Content = nextLabel;
        StepLabel.Text  = $"Step {step} of {total}  —  {label}";

        StepDots.Children.Clear();
        for (int i = 1; i <= total; i++)
        {
            StepDots.Children.Add(new Ellipse
            {
                Width  = i == step ? 12 : 9,
                Height = i == step ? 12 : 9,
                Fill   = i == step
                    ? (System.Windows.Media.Brush)FindResource("BrushTitle")
                    : (System.Windows.Media.Brush)FindResource("BrushBorder2"),
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) => _backAction?.Invoke();
    private void BtnNext_Click(object sender, RoutedEventArgs e) => _nextAction?.Invoke();

    public void OpenCharacterSheets(int characterIndex = -1)
    {
        PendingCharacterSheetIndex = characterIndex;
        GoTo("character_sheets");
    }

    public void StartPlayerLevelUp(int characterIndex, int xpGain, int hpGain, int cpGain)
    {
        if (characterIndex < 0 || characterIndex >= Characters.Count)
            return;

        var character = Characters[characterIndex];

        CharGen.Clear();
        CharGen.IsLevelUpMode = true;
        CharGen.LevelUpCharacterIndex = characterIndex;
        CharGen.LevelUpPendingExperienceGain = Math.Max(0, xpGain);
        CharGen.LevelUpPendingHitPointGain = Math.Max(0, hpGain);
        CharGen.LevelUpPendingCharacterPointGain = Math.Max(0, cpGain);

        CharGen.Name = character.Name;
        CharGen.CharacterMode = string.IsNullOrWhiteSpace(character.CharacterMode) ? "players_option" : character.CharacterMode;
        CharGen.RaceId = character.RaceId;
        CharGen.BaseRaceId = character.RaceId;
        CharGen.ClassId = character.ClassId;
        CharGen.ClassMode = character.ClassMode;
        int projectedXp = Math.Max(0, character.ExperiencePoints) + Math.Max(0, xpGain);
        int projectedLevel = CharacterProgressionService.GetLevelForExperience(CharGen.ClassId, projectedXp);
        // Wizard spell selection happens before the Review screen, so set projected level now.
        CharGen.CharacterLevel = Math.Max(1, projectedLevel);

        CharGen.Abilities = character.Abilities.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        CharGen.ModifiedAbilities = character.Abilities.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        if (character.ClassIds.Count > 0)
        {
            CharGen.SelectedClassIds = character.ClassIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else if (!string.IsNullOrWhiteSpace(character.ClassId))
        {
            CharGen.SelectedClassIds = new List<string> { character.ClassId };
        }

        CharGen.SelectedNonweaponProficiencyIds = character.NonweaponProficiencyIds.Count > 0
            ? new List<string>(character.NonweaponProficiencyIds)
            : new List<string>();
        CharGen.SelectedWeaponProficiencies = character.WeaponProficiencies
            .Select(CloneWeaponSelection)
            .ToList();
        CharGen.SelectedEquipment = character.EquipmentSelections
            .Select(CloneEquipmentSelection)
            .ToList();
        CharGen.WizardSpellbookIds = new List<string>(character.WizardSpellbookIds);
        CharGen.WizardSpellLists = character.WizardSpellLists
            .Select(list => new NamedSpellList
            {
                Name = list.Name,
                SpellIds = new List<string>(list.SpellIds)
            })
            .ToList();
        CharGen.EquippedArmorId = character.EquippedArmorId ?? string.Empty;
        CharGen.EquippedShieldId = character.EquippedShieldId ?? string.Empty;
        CharGen.EquippedWeaponId = character.EquippedWeaponId ?? string.Empty;
        CharGen.KitId = character.KitId ?? string.Empty;
        CharGen.KitFreeNwpIds = character.KitFreeNwpIds ?? new List<string>();
        CharGen.KitRequiredNwpIds = character.KitRequiredNwpIds ?? new List<string>();
        if (CharGen.SelectedEquipment.Count == 0 && character.Equipment.Count > 0)
        {
            CharGen.SelectedEquipment = character.Equipment
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => new EquipmentSelection
                {
                    ItemId = $"LEGACY|{x}",
                    Category = "Legacy",
                    ItemName = x,
                    Quantity = 1,
                    SizeClassEach = "Medium",
                })
                .ToList();
        }

        CharGen.LockedNonweaponProficiencyIds = new List<string>(character.LockedLastLevelUpNonweaponIds);
        CharGen.LockedWeaponProficiencyIds = new List<string>(character.LockedLastLevelUpWeaponIds);

        // Restore full chargen selection state so all screens show prior choices.
        CharGen.SelectedSpheres = new Dictionary<string, string>(character.SelectedSpheres, StringComparer.OrdinalIgnoreCase);
        CharGen.SelectedWizardSchools = new Dictionary<string, bool>(character.SelectedWizardSchools, StringComparer.OrdinalIgnoreCase);
        CharGen.SelectedAbilitiesByClass = character.SelectedAbilitiesByClass
            .ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value), StringComparer.OrdinalIgnoreCase);
        if (CharGen.SelectedAbilitiesByClass.Count == 0
            && character.SelectedClassAbilityIds.Count > 0
            && !string.IsNullOrWhiteSpace(CharGen.ClassId))
        {
            CharGen.SelectedAbilitiesByClass[CharGen.ClassId] = new List<string>(character.SelectedClassAbilityIds);
        }
        CharGen.SelectedTraitIds = new List<string>(character.SelectedTraitIds);
        CharGen.SelectedDisadvantageSeverities = new Dictionary<string, string>(character.SelectedDisadvantageSeverities, StringComparer.OrdinalIgnoreCase);
        CharGen.SelectedLanguages = character.SelectedLanguages
            .Select(l => new LanguageSelection { SourceKey = l.SourceKey, LanguageName = l.LanguageName })
            .ToList();
        CharGen.SelectedNonweaponProficiencyImprovements = new Dictionary<string, int>(character.SelectedNonweaponProficiencyImprovements, StringComparer.OrdinalIgnoreCase);
        CharGen.RogueSkillArmorProfile = character.RogueSkillArmorProfile;
        CharGen.SelectedRogueSkillPoints = new Dictionary<string, int>(character.SelectedRogueSkillPoints, StringComparer.OrdinalIgnoreCase);
        CharGen.SpheresByClass = character.SpheresByClass
            .ToDictionary(kv => kv.Key, kv => new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        CharGen.SchoolsByClass = character.SchoolsByClass
            .ToDictionary(kv => kv.Key, kv => new Dictionary<string, bool>(kv.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        CharGen.BaselineNonweaponProficiencyIds = new List<string>(CharGen.SelectedNonweaponProficiencyIds);
        CharGen.BaselineNonweaponProficiencyImprovements = new Dictionary<string, int>(
            CharGen.SelectedNonweaponProficiencyImprovements,
            StringComparer.OrdinalIgnoreCase);
        CharGen.BaselineWeaponProficiencyIds = CharGen.SelectedWeaponProficiencies
            .Select(x => x.ProficiencyId)
            .ToList();
        CharGen.BaselineWeaponProficiencies = CharGen.SelectedWeaponProficiencies
            .Select(CloneWeaponSelection)
            .ToList();

        CharGen.SyncLegacyFieldsFromNew();
    }

    private static WeaponProficiencySelection CloneWeaponSelection(WeaponProficiencySelection selection)
        => new()
        {
            ProficiencyId = selection.ProficiencyId,
            ProficiencyType = selection.ProficiencyType,
            DisplayName = selection.DisplayName,
            Specialized = selection.Specialized,
            WeaponOfChoice = selection.WeaponOfChoice,
            WeaponExpertise = selection.WeaponExpertise,
        };

    private static EquipmentSelection CloneEquipmentSelection(EquipmentSelection selection)
        => new()
        {
            ItemId = selection.ItemId,
            Category = selection.Category,
            ItemName = selection.ItemName,
            CostText = selection.CostText,
            Quantity = Math.Max(1, selection.Quantity),
            IsArmor = selection.IsArmor,
            ArmorClassValue = selection.ArmorClassValue,
            RogueArmorProfile = string.IsNullOrWhiteSpace(selection.RogueArmorProfile) ? "no_armor" : selection.RogueArmorProfile,
            IsShield = selection.IsShield,
            IsWeapon = selection.IsWeapon,
            WeaponSpeed = Math.Max(0, selection.WeaponSpeed),
            WeaponDamageSmallMedium = selection.WeaponDamageSmallMedium ?? string.Empty,
            WeaponDamageLarge = selection.WeaponDamageLarge ?? string.Empty,
            WeaponType = selection.WeaponType ?? string.Empty,
            WeaponSize = selection.WeaponSize ?? string.Empty,
            CostGoldEach = Math.Max(0, selection.CostGoldEach),
            CostSilverEach = Math.Max(0, selection.CostSilverEach),
            CostCopperEach = Math.Max(0, selection.CostCopperEach),
            SizeClassEach = string.IsNullOrWhiteSpace(selection.SizeClassEach) ? "Medium" : selection.SizeClassEach,
            WeightEach = Math.Max(0, selection.WeightEach),
        };
}

// ── Shared chargen state ──────────────────────────────────────────────────────
public class CharGenState
{
    public string Name         { get; set; } = "";
    public string CharacterMode { get; set; } = "players_option";
    public string Method       { get; set; } = "method_v_4d6_drop_lowest";
    public Dictionary<string, int> Abilities    { get; set; } = new();
    // Modified abilities: rolled scores + racial modifiers applied
    public Dictionary<string, int> ModifiedAbilities { get; set; } = new();
    // Racial ability modifiers (delta) applied during race selection: e.g. {str: 0, dex: 0, con: +1, int: 0, wis: 0, cha: -1}
    public Dictionary<string, int> RacialAbilityModifiers { get; set; } = new();
    // PO sub-abilities: keys e.g. "str_muscle", "str_stamina", "dex_aim" …
    public Dictionary<string, int> SubAbilities { get; set; } = new();
    // Exceptional strength percentile (warriors with STR 18): 0=none, 1-100
    public int ExceptionalStrength { get; set; } = 0;
    public bool ExceptionalStrengthLocked { get; set; } = false;
    public string BaseRaceId { get; set; } = "";
    public string RaceId  { get; set; } = "";

    // Player level-up mode context.
    public bool IsLevelUpMode { get; set; } = false;
    public int LevelUpCharacterIndex { get; set; } = -1;
    public int LevelUpPendingExperienceGain { get; set; } = 0;
    public int LevelUpPendingHitPointGain { get; set; } = 0;
    public int LevelUpPendingCharacterPointGain { get; set; } = 0;
    public List<string> LockedNonweaponProficiencyIds { get; set; } = new();
    public List<string> LockedWeaponProficiencyIds { get; set; } = new();
    public List<string> BaselineNonweaponProficiencyIds { get; set; } = new();
    public Dictionary<string, int> BaselineNonweaponProficiencyImprovements { get; set; } = new();
    public List<string> BaselineWeaponProficiencyIds { get; set; } = new();
    public List<WeaponProficiencySelection> BaselineWeaponProficiencies { get; set; } = new();

    // Existing-character onboarding mode context (roster -> add existing).
    public bool IsExistingCharacterMode { get; set; } = false;
    public int ExistingStartingExperience { get; set; } = 0;
    public bool ExistingExperienceIsGrandTotalForMulticlass { get; set; } = true;
    public int ExistingStartingUnspentCharacterPoints { get; set; } = 0;
    public int ExistingCpPerLevel { get; set; } = 0;
    
    // ── Class Selection (Multi/Dual-class support) ──
    public string ClassId { get; set; } = "";  // For single-class / legacy; derived from SelectedClassIds[0]
    public List<string> SelectedClassIds { get; set; } = new();  // Primary: all selected class IDs
    public string ClassMode { get; set; } = "";  // "" = single-class, "multiclass", "dualclass"
    public int DualClassSwitchLevel { get; set; } = 0;  // For dual-class mode: level when switching
    // Kit selection from Complete Books of Races
    public string KitId { get; set; } = "";
    public List<string> KitFreeNwpIds { get; set; } = new();
    public List<string> KitRequiredNwpIds { get; set; } = new();
    
    public int RacialCarryoverToClassPoints { get; set; } = 0;
    public List<string> SelectedRacialAbilityIds { get; set; } = new();
    
    // ── Per-class selections (new) ──
    public Dictionary<string, List<string>> SelectedAbilitiesByClass { get; set; } = new();
    public Dictionary<string, string> WizardSpecializationById { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> SpheresByClass { get; set; } = new();
    public Dictionary<string, Dictionary<string, bool>> SchoolsByClass { get; set; } = new();
    public Dictionary<string, Dictionary<string, int>> RogueSkillPointsByClass { get; set; } = new();
    public Dictionary<string, string> RogueSkillArmorByClass { get; set; } = new();
    
    // ── Legacy single-class selections (for backwards compat) ──
    public List<string> SelectedClassAbilityIds { get; set; } = new();
    public string WizardSpecializationId { get; set; } = "";
    // Sphere selections: key=sphere name (e.g. "All", "Healing"), value="minor"/"major"/"both"
    public Dictionary<string, string> SelectedSpheres { get; set; } = new();
    // Wizard school selections: key=school name (e.g. "Abjuration"), value=true if selected
    public Dictionary<string, bool> SelectedWizardSchools { get; set; } = new();
    // Rogue skill allocations: key=skill id (e.g. "pick_pockets"), value=allocated discretionary points
    public Dictionary<string, int> SelectedRogueSkillPoints { get; set; } = new();
    // Rogue armor profile for thieving skill adjustments
    public string RogueSkillArmorProfile { get; set; } = "no_armor";
    public List<string> SelectedNonweaponProficiencyIds { get; set; } = new();
    public Dictionary<string, int> SelectedNonweaponProficiencyImprovements { get; set; } = new();
    public Dictionary<string, string> SelectedNonweaponProficiencyNotes { get; set; } = new();
    public List<LanguageSelection> SelectedLanguages { get; set; } = new();
    public List<string> SelectedTraitIds { get; set; } = new();
    public Dictionary<string, string> SelectedDisadvantageSeverities { get; set; } = new();
    // Chargen level baseline for per-level point calculations (defaults to 1st level)
    public int CharacterLevel { get; set; } = 1;
    // Weapon proficiency selections
    public List<WeaponProficiencySelection> SelectedWeaponProficiencies { get; set; } = new();
    public List<EquipmentSelection> SelectedEquipment { get; set; } = new();
    public List<string> WizardSpellbookIds { get; set; } = new();
    public List<NamedSpellList> WizardSpellLists { get; set; } = new();
    // Explicitly equipped slots (set on the equipment screen).
    public string EquippedArmorId  { get; set; } = "";
    public string EquippedShieldId { get; set; } = "";
    public string EquippedWeaponId { get; set; } = "";

    public void Clear()
    {
        Name = ""; CharacterMode = "players_option"; Method = "method_v_4d6_drop_lowest";
        Abilities = new(); ModifiedAbilities = new(); RacialAbilityModifiers = new(); SubAbilities = new(); ExceptionalStrength = 0; ExceptionalStrengthLocked = false;
        BaseRaceId = ""; RaceId = ""; ClassId = ""; ClassMode = ""; KitId = ""; KitFreeNwpIds = new(); KitRequiredNwpIds = new();
        IsLevelUpMode = false;
        LevelUpCharacterIndex = -1;
        LevelUpPendingExperienceGain = 0;
        LevelUpPendingHitPointGain = 0;
        LevelUpPendingCharacterPointGain = 0;
        LockedNonweaponProficiencyIds = new();
        LockedWeaponProficiencyIds = new();
        BaselineNonweaponProficiencyIds = new();
        BaselineNonweaponProficiencyImprovements = new();
        BaselineWeaponProficiencyIds = new();
        BaselineWeaponProficiencies = new();
        IsExistingCharacterMode = false;
        ExistingStartingExperience = 0;
        ExistingExperienceIsGrandTotalForMulticlass = true;
        ExistingStartingUnspentCharacterPoints = 0;
        ExistingCpPerLevel = 0;
        SelectedClassIds = new();
        DualClassSwitchLevel = 0;
        RacialCarryoverToClassPoints = 0;
        SelectedRacialAbilityIds = new();
        SelectedAbilitiesByClass = new();
        WizardSpecializationById = new();
        SpheresByClass = new();
        SchoolsByClass = new();
        RogueSkillPointsByClass = new();
        RogueSkillArmorByClass = new();
        SelectedClassAbilityIds = new();
        WizardSpecializationId = "";
        SelectedSpheres = new();
        SelectedWizardSchools = new();
        SelectedRogueSkillPoints = new();
        RogueSkillArmorProfile = "no_armor";
        SelectedNonweaponProficiencyIds = new();
        SelectedNonweaponProficiencyImprovements = new();
        SelectedNonweaponProficiencyNotes = new();
        SelectedLanguages = new();
        SelectedTraitIds = new();
        SelectedDisadvantageSeverities = new();
        CharacterLevel = 1;
        SelectedWeaponProficiencies = new();
        SelectedEquipment = new();
        WizardSpellbookIds = new();
        WizardSpellLists = new();
        EquippedArmorId = "";
        EquippedShieldId = "";
        EquippedWeaponId = "";
    }

    /// <summary>
    /// Sync legacy single-class fields from the new multi-class structures.
    /// Used when transitioning from new multi-class UI back to legacy screens.
    /// </summary>
    public void SyncLegacyFieldsFromNew()
    {
        if (SelectedClassIds.Count == 0)
        {
            ClassId = "";
            SelectedClassAbilityIds = new();
            WizardSpecializationId = "";
            SelectedSpheres = new();
            SelectedWizardSchools = new();
        }
        else
        {
            string primaryClassId = SelectedClassIds[0];
            ClassId = primaryClassId;
            SelectedClassAbilityIds = SelectedAbilitiesByClass.TryGetValue(primaryClassId, out var abilities) 
                ? new List<string>(abilities) 
                : new();
            WizardSpecializationId = WizardSpecializationById.TryGetValue(primaryClassId, out var spec)
                ? spec
                : "";
            SelectedSpheres = SpheresByClass.TryGetValue(primaryClassId, out var spheres)
                ? new Dictionary<string, string>(spheres)
                : new();
            SelectedWizardSchools = SchoolsByClass.TryGetValue(primaryClassId, out var schools)
                ? new Dictionary<string, bool>(schools)
                : new();
            SelectedRogueSkillPoints = RogueSkillPointsByClass.TryGetValue(primaryClassId, out var roguePoints)
                ? new Dictionary<string, int>(roguePoints)
                : new();
            RogueSkillArmorProfile = RogueSkillArmorByClass.TryGetValue(primaryClassId, out var armor)
                ? armor
                : "no_armor";
        }
    }

    /// <summary>
    /// Sync new multi-class fields from legacy single-class structures.
    /// Used during initial conversion or when working in single-class mode.
    /// </summary>
    public void SyncNewFieldsFromLegacy()
    {
        if (!string.IsNullOrEmpty(ClassId))
        {
            SelectedClassIds = new List<string> { ClassId };
            SelectedAbilitiesByClass[ClassId] = new List<string>(SelectedClassAbilityIds);
            if (!string.IsNullOrEmpty(WizardSpecializationId))
                WizardSpecializationById[ClassId] = WizardSpecializationId;
            if (SelectedSpheres.Count > 0)
                SpheresByClass[ClassId] = new Dictionary<string, string>(SelectedSpheres);
            if (SelectedWizardSchools.Count > 0)
                SchoolsByClass[ClassId] = new Dictionary<string, bool>(SelectedWizardSchools);
            if (SelectedRogueSkillPoints.Count > 0)
                RogueSkillPointsByClass[ClassId] = new Dictionary<string, int>(SelectedRogueSkillPoints);
            if (!string.IsNullOrWhiteSpace(RogueSkillArmorProfile))
                RogueSkillArmorByClass[ClassId] = RogueSkillArmorProfile;
        }
    }

    /// <summary>
    /// Recompute CharacterLevel from existing-character XP context.
    /// For multiclass with total-XP mode, XP is split evenly per class.
    /// </summary>
    public void RecalculateLevelFromExistingExperience()
    {
        if (!IsExistingCharacterMode)
        {
            CharacterLevel = Math.Max(1, CharacterLevel);
            return;
        }

        var classIds = SelectedClassIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (classIds.Count == 0 && !string.IsNullOrWhiteSpace(ClassId))
            classIds.Add(ClassId);

        int enteredXp = Math.Max(0, ExistingStartingExperience);
        int effectiveXp = enteredXp;
        if (classIds.Count > 1 && ExistingExperienceIsGrandTotalForMulticlass)
            effectiveXp = Math.Max(0, enteredXp / classIds.Count);

        string classIdForProgression = !string.IsNullOrWhiteSpace(ClassId)
            ? ClassId
            : classIds.FirstOrDefault() ?? string.Empty;

        int derived = CharacterProgressionService.GetLevelForExperience(classIdForProgression, effectiveXp);
        CharacterLevel = Math.Max(1, derived);
    }
}