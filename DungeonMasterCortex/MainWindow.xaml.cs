using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;
using DungeonMasterCortex.Views;

namespace DungeonMasterCortex;

public partial class MainWindow : Window
{
    private const double DefaultUiFontSize = 14;
    private const double DefaultWindowWidth = 1160;
    private const double DefaultWindowHeight = 780;
    private bool _autoResizeWindow = true;
    private bool _checkForUpdatesOnStartup = true;
    private bool _characterLoadFailed;

    public bool AutoResizeWindow
    {
        get => _autoResizeWindow;
        set => _autoResizeWindow = value;
    }

    public bool CheckForUpdatesOnStartup
    {
        get => _checkForUpdatesOnStartup;
        set => _checkForUpdatesOnStartup = value;
    }

    public string AppearanceMode { get; private set; } = "parchment";

    public bool SetAppearanceMode(string? mode)
    {
        string normalized = NormalizeAppearanceMode(mode);
        bool changed = !string.Equals(AppearanceMode, normalized, StringComparison.OrdinalIgnoreCase);
        AppearanceMode = normalized;
        return changed;
    }

    public static string GetSavedAppearanceMode() =>
        NormalizeAppearanceMode(ReadVersionSettings().AppearanceMode);

    private static string NormalizeAppearanceMode(string? mode) =>
        string.Equals(mode, "high_contrast_dark", StringComparison.OrdinalIgnoreCase)
            ? "high_contrast_dark"
            : "parchment";

    // Set global font size for the application
    public void SetGlobalFontSize(double fontSize)
    {
        FontSize = fontSize;

        if (Content is FrameworkElement root)
        {
            double scale = Math.Clamp(fontSize / DefaultUiFontSize, 0.75, 2.0);
            root.LayoutTransform = new ScaleTransform(scale, scale);
            if (_autoResizeWindow && WindowState != WindowState.Maximized)
            {
                Width = Math.Max(MinWidth, DefaultWindowWidth * scale);
                Height = Math.Max(MinHeight, DefaultWindowHeight * scale);
            }
        }

        foreach (Window window in Application.Current.Windows)
        {
            window.FontSize = fontSize;
        }
    }
    private static readonly JsonSerializerOptions CharacterSaveJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string CharacterSaveDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DungeonMasterCortex");

    private static readonly string CharacterSavePath = System.IO.Path.Combine(CharacterSaveDirectory, "characters.json");
    private static readonly string CharacterLoadErrorPath = System.IO.Path.Combine(CharacterSaveDirectory, "character-load-error.log");
    private static readonly string EmptyRosterConfirmationPath = System.IO.Path.Combine(CharacterSaveDirectory, "characters-empty-confirmed.marker");
    private static readonly string VersionSettingsPath = System.IO.Path.Combine(CharacterSaveDirectory, "version.json");

    private sealed class VersionSettingsData
    {
        public string LastSeenVersion { get; set; } = "0.0.0";
        public List<string> DoNotShowReleaseNotesForVersions { get; set; } = new();
        public bool UseFullscreen { get; set; } = false;
        public double PreferredFontSize { get; set; } = 14;
        public bool AutoResizeWindow { get; set; } = true;
        public bool CheckForUpdatesOnStartup { get; set; } = true;
        public string AppearanceMode { get; set; } = "parchment";
        public string CharacterRosterRulesetFilter { get; set; } = "both";
        public Dictionary<string, List<string>> GlobalSpellTags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // ── Shared services ───────────────────────────────────────────────────────
    public readonly RulesEngine                  Rules            = new();
    public readonly CharacterOptionCatalogService CharacterOptions = new();
    public readonly CombatService                Combat           = new();
    public readonly SessionService               Sessions         = new(GetBuildEdition());
    public readonly LicenseService               License          = new(GetBuildEdition());

    private readonly Dictionary<string, CampaignService> _campaigns = new(StringComparer.OrdinalIgnoreCase);
    public string ActiveCampaignId { get; private set; } = "default";
    public CampaignService Campaign
    {
        get
        {
            var campaign = _campaigns[ActiveCampaignId];
            SyncLinkedCampaignCalendar(campaign);
            return campaign;
        }
    }

    public CharGenState CharGen    { get; } = new();
    public List<Models.CharacterSheet> Characters { get; } = new();

    // ── Screen registry ───────────────────────────────────────────────────────
    private readonly Dictionary<string, Lazy<IScreen>> _screens;
    private string  _currentScreen = "";
    private Action? _backAction;
    private Action? _nextAction;
    private Window? _spellTrackerWindow;
    private Views.SpellTrackerScreen? _spellTrackerScreen;
    private bool _suppressSpellTrackerCloseNavigation;
    private string _spellTrackerReturnScreen = "characters";
    private int _spellTrackerReturnCharacterIndex = -1;
    private readonly Dictionary<string, List<string>> _globalSpellTags = new(StringComparer.OrdinalIgnoreCase);

    // Optional requested initial tab when navigating into Edit Information.
    public string PendingEditInfoTab { get; set; } = "";
    public int PendingEditCharacterIndex { get; set; } = -1;
    public bool PendingCharacterLevelUpMode { get; set; } = false;
    public int PendingCharacterSheetIndex { get; set; } = -1;
    public int PendingInGameCharacterIndex { get; set; } = -1;
    public int PendingSpellTrackerCharacterIndex { get; set; } = -1;
    public string PendingCombatMonsterId { get; set; } = "";
    public int PendingCombatMonsterQuantity { get; set; } = 1;

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
        BannerVersion.Text = $"v{AppUpdateService.GetCurrentDisplayVersion()}";
        ApplyUiPreferencesFromSettings();
        LoadGlobalSpellTagsFromSettings();

        InitializeCampaigns();

        _screens = new()
        {
            ["dice_roller"]       = new Lazy<IScreen>(() => new DiceRollerScreen(this)),
            ["hub"]               = new Lazy<IScreen>(() => new HubScreen(this)),
            ["characters"]        = new Lazy<IScreen>(() => new CharacterRosterScreen(this)),
            ["party_management"]  = new Lazy<IScreen>(() => new PartyManagementScreen(this)),
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
            ["sessions"]          = new Lazy<IScreen>(() => new SessionScreen(this)),
            ["campaign"]          = new Lazy<IScreen>(() => new CampaignScreen(this)),
            ["character_sheets"]  = new Lazy<IScreen>(() => new CharacterSheetsScreen(this)),
            ["in_game_character"] = new Lazy<IScreen>(() => new InGameCharacterScreen(this)),
            ["spell_tracker"]     = new Lazy<IScreen>(() => new SpellTrackerScreen(this)),
            ["combat_tracker"]    = new Lazy<IScreen>(() => new CombatTrackerScreen(this)),
            ["edit_info"]         = new Lazy<IScreen>(() => new EditInfoScreen(this)),
            ["options"]           = new Lazy<IScreen>(() => new OptionsScreen(this)),
        };

        LoadCharacters();
        Closing += (_, _) => SaveCharacters();
        Loaded += MainWindow_Loaded;

        GoTo("hub");
    }

    public void SaveUiPreferences()
    {
        try
        {
            if (!Directory.Exists(CharacterSaveDirectory))
                Directory.CreateDirectory(CharacterSaveDirectory);

            var settings = ReadVersionSettings();
            settings.UseFullscreen = WindowState == WindowState.Maximized;
            settings.PreferredFontSize = FontSize;
            settings.AutoResizeWindow = _autoResizeWindow;
            settings.CheckForUpdatesOnStartup = _checkForUpdatesOnStartup;
            settings.AppearanceMode = NormalizeAppearanceMode(AppearanceMode);
            WriteVersionSettings(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving UI preferences: {ex.Message}");
        }
    }

    public string GetCharacterRosterRulesetFilterPreference()
    {
        try
        {
            var settings = ReadVersionSettings();
            return NormalizeCharacterRosterRulesetFilter(settings.CharacterRosterRulesetFilter);
        }
        catch
        {
            return "both";
        }
    }

    public void SaveCharacterRosterRulesetFilterPreference(string filter)
    {
        try
        {
            if (!Directory.Exists(CharacterSaveDirectory))
                Directory.CreateDirectory(CharacterSaveDirectory);

            var settings = ReadVersionSettings();
            settings.CharacterRosterRulesetFilter = NormalizeCharacterRosterRulesetFilter(filter);
            WriteVersionSettings(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving roster ruleset filter preference: {ex.Message}");
        }
    }

    private static string NormalizeCharacterRosterRulesetFilter(string? filter)
    {
        return (filter ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "po" => "po",
            "c" => "c",
            _ => "both",
        };
    }

    private void ApplyUiPreferencesFromSettings()
    {
        try
        {
            var settings = ReadVersionSettings();

            double fontSize = settings.PreferredFontSize;
            if (fontSize < 10 || fontSize > 40)
                fontSize = 14;

            _autoResizeWindow = settings.AutoResizeWindow;
            _checkForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
            AppearanceMode = NormalizeAppearanceMode(settings.AppearanceMode);
            SetGlobalFontSize(fontSize);
            WindowState = settings.UseFullscreen ? WindowState.Maximized : WindowState.Normal;

            if (_autoResizeWindow && WindowState != WindowState.Maximized)
            {
                double scale = Math.Clamp(fontSize / DefaultUiFontSize, 0.75, 2.0);
                Width = Math.Max(MinWidth, DefaultWindowWidth * scale);
                Height = Math.Max(MinHeight, DefaultWindowHeight * scale);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error applying UI preferences: {ex.Message}");
        }
    }

    private void InitializeCampaigns()
    {
        _campaigns.Clear();
        var defaultCampaign = new CampaignService
        {
            CampaignId = "default",
            CampaignName = "Default Campaign"
        };
        _campaigns[defaultCampaign.CampaignId] = defaultCampaign;
        ActiveCampaignId = defaultCampaign.CampaignId;
    }

    public IReadOnlyList<CampaignService> GetCampaigns()
    {
        return _campaigns.Values
            .OrderBy(c => c.CampaignName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public CampaignService? GetCampaignById(string campaignId)
    {
        if (string.IsNullOrWhiteSpace(campaignId))
            return null;
        return _campaigns.TryGetValue(campaignId, out var campaign) ? campaign : null;
    }

    private void SyncLinkedCampaignCalendar(CampaignService campaign)
    {
        string sourceId = (campaign.SharedCalendarSourceCampaignId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sourceId))
            return;
        if (string.Equals(sourceId, campaign.CampaignId, StringComparison.OrdinalIgnoreCase))
            return;
        if (!_campaigns.TryGetValue(sourceId, out var source))
            return;

        var targetCal = campaign.Calendar;
        var sourceCal = source.Calendar;

        targetCal.Config.MonthCount = sourceCal.Config.MonthCount;
        targetCal.Config.DaysPerWeek = sourceCal.Config.DaysPerWeek;
        targetCal.Config.HoursPerDay = sourceCal.Config.HoursPerDay;
        targetCal.Config.MonthNames = new List<string>(sourceCal.Config.MonthNames);
        targetCal.Config.DayNames = new List<string>(sourceCal.Config.DayNames);
        targetCal.Config.DaysPerMonth = new List<int>(sourceCal.Config.DaysPerMonth);
        targetCal.Config.Moons = sourceCal.Config.Moons
            .Select(m => new MoonDefinition
            {
                Name = m.Name,
                CycleLengthDays = m.CycleLengthDays,
                DayInCycle = m.DayInCycle,
            })
            .ToList();

        targetCal.CurrentYear = sourceCal.CurrentYear;
        targetCal.CurrentEra = sourceCal.CurrentEra;
        targetCal.CurrentMonth = sourceCal.CurrentMonth;
        targetCal.CurrentDay = sourceCal.CurrentDay;
        targetCal.CurrentHour = sourceCal.CurrentHour;
        targetCal.CurrentMinute = sourceCal.CurrentMinute;
        targetCal.TotalDaysElapsed = sourceCal.TotalDaysElapsed;
        targetCal.TotalMinutesElapsed = sourceCal.TotalMinutesElapsed;
    }

    public bool CreateCampaign(string name, out string message)
    {
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            message = "Campaign name is required.";
            return false;
        }

        string baseId = BuildCampaignId(name);
        string id = baseId;
        int suffix = 2;
        while (_campaigns.ContainsKey(id))
        {
            id = $"{baseId}-{suffix}";
            suffix++;
        }

        _campaigns[id] = new CampaignService
        {
            CampaignId = id,
            CampaignName = name
        };

        ActiveCampaignId = id;
        message = "Campaign created.";
        return true;
    }

    public bool SwitchCampaign(string campaignId)
    {
        if (!_campaigns.ContainsKey(campaignId))
            return false;
        ActiveCampaignId = campaignId;
        return true;
    }

    public bool DeleteCampaign(string campaignId, out string message)
    {
        if (!_campaigns.ContainsKey(campaignId))
        {
            message = "Campaign not found.";
            return false;
        }

        if (_campaigns.Count <= 1)
        {
            message = "At least one campaign must exist.";
            return false;
        }

        _campaigns.Remove(campaignId);

        foreach (var campaign in _campaigns.Values)
        {
            if (string.Equals(campaign.SharedCalendarSourceCampaignId, campaignId, StringComparison.OrdinalIgnoreCase))
                campaign.SharedCalendarSourceCampaignId = string.Empty;
        }

        if (string.Equals(ActiveCampaignId, campaignId, StringComparison.OrdinalIgnoreCase))
        {
            ActiveCampaignId = _campaigns.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).First();
        }

        message = "Campaign deleted.";
        return true;
    }

    private static string BuildCampaignId(string name)
    {
        var chars = name.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        string raw = new string(chars);
        while (raw.Contains("--", StringComparison.Ordinal))
            raw = raw.Replace("--", "-", StringComparison.Ordinal);
        raw = raw.Trim('-');
        return string.IsNullOrWhiteSpace(raw) ? "campaign" : raw;
    }

    private static AppEdition GetBuildEdition()
    {
        return AppUpdateService.GetCurrentEdition();
    }

    public void SaveCharacters(bool allowEmptyRoster = false)
    {
        if (!allowEmptyRoster && Characters.Count == 0 && File.Exists(CharacterSavePath))
        {
            try
            {
                var existingCharacters = JsonSerializer.Deserialize<List<Models.CharacterSheet>>(
                    File.ReadAllText(CharacterSavePath));
                if (existingCharacters is null)
                {
                    System.Diagnostics.Debug.WriteLine("[SaveJSON] Skipped empty save because the existing roster was JSON null.");
                    return;
                }

                if (existingCharacters is { Count: > 0 })
                {
                    System.Diagnostics.Debug.WriteLine("[SaveJSON] Skipped empty save because the existing roster contains characters.");
                    return;
                }
            }
            catch
            {
                // A failed load must never be followed by an empty overwrite.
                System.Diagnostics.Debug.WriteLine("[SaveJSON] Skipped empty save because the existing roster could not be read.");
                return;
            }
        }

        if (!allowEmptyRoster && _characterLoadFailed && Characters.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[SaveJSON] Skipped empty save because the existing roster failed to load.");
            return;
        }

        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(CharacterSaveDirectory);
            CharacterOptionCatalog catalog = CharacterOptions.GetCatalog();
            foreach (CharacterSheet character in Characters)
                FoundryCharacterExchangeService.SynchronizeCharacterData(character, Rules, catalog);
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
            
            temporaryPath = System.IO.Path.Combine(CharacterSaveDirectory, $"characters.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, json);

            if (File.Exists(CharacterSavePath))
            {
                string existingJson = File.ReadAllText(CharacterSavePath);
                if (!string.Equals(existingJson, json, StringComparison.Ordinal))
                {
                    string backupPath = System.IO.Path.Combine(
                        CharacterSaveDirectory,
                        $"characters-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json.bak");
                    File.Copy(CharacterSavePath, backupPath, overwrite: false);
                }

                File.Replace(temporaryPath, CharacterSavePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, CharacterSavePath);
            }

            temporaryPath = null;
            _characterLoadFailed = false;
            try
            {
                if (allowEmptyRoster && Characters.Count == 0)
                    File.WriteAllText(EmptyRosterConfirmationPath, DateTimeOffset.UtcNow.ToString("O"));
                else if (Characters.Count > 0 && File.Exists(EmptyRosterConfirmationPath))
                    File.Delete(EmptyRosterConfirmationPath);
            }
            catch (Exception markerException)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveJSON] Could not update empty-roster confirmation marker: {markerException.Message}");
            }

            System.Diagnostics.Debug.WriteLine($"[SaveJSON] Saved to: {CharacterSavePath}");
        }
        catch (Exception ex)
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch { /* Preserve the original save even if temporary-file cleanup fails. */ }
            }

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
                throw new JsonException("The character roster contained JSON null instead of a character list.");

            bool migrated = false;
            bool protectEmptyRoster = false;
            if (loaded.Count == 0 && !IsEmptyRosterExplicitlyConfirmed())
            {
                List<Models.CharacterSheet>? backupCharacters = FindLatestNonEmptyCharacterBackup(out string? backupPath);
                if (backupCharacters is not null && backupPath is not null)
                {
                    MessageBoxResult restoreBackup = MessageBox.Show(
                        $"The shared roster is empty, but a backup contains {backupCharacters.Count} character(s). Restore that backup?\n\n{backupPath}",
                        "Empty Roster Backup Found",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (restoreBackup == MessageBoxResult.Yes)
                    {
                        loaded = backupCharacters;
                        migrated = true;
                    }
                    else
                    {
                        protectEmptyRoster = true;
                    }
                }
            }

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

            CharacterOptionCatalog catalog = CharacterOptions.GetCatalog();
            foreach (CharacterSheet character in loaded)
            {
                bool neededHydration = (character.EquipmentSelections.Count == 0 && character.Equipment.Count > 0)
                    || (character.ArcaneSpells.Count == 0 && character.DivineSpells.Count == 0
                        && character.PsionicPowers.Count == 0
                        && (character.ArcaneSpellSlots.Any(slot => slot.Value > 0)
                            || character.DivineSpellSlots.Any(slot => slot.Value > 0)))
                    || character.StructuredAbilities.Any(ability =>
                        string.IsNullOrWhiteSpace(ability.Name) || string.IsNullOrWhiteSpace(ability.Category));
                FoundryCharacterExchangeService.SynchronizeCharacterData(character, Rules, catalog);
                migrated |= neededHydration;
            }

            migrated = NormalizeLoadedCharacterEquipmentIds(loaded) || migrated;
            migrated = NormalizeLoadedCharacterModes(loaded) || migrated;

            Characters.Clear();
            Characters.AddRange(loaded);
            _characterLoadFailed = protectEmptyRoster;

            if (migrated)
                SaveCharacters();
        }
        catch (Exception ex)
        {
            _characterLoadFailed = true;
            try
            {
                Directory.CreateDirectory(CharacterSaveDirectory);
                File.AppendAllText(
                    CharacterLoadErrorPath,
                    $"{DateTimeOffset.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { /* The load warning below still reports the original failure. */ }

            MessageBox.Show(
                $"Unable to load saved characters. The existing roster will not be overwritten by an empty save.\n\n{ex.Message}\n\nDetails were also written to:\n{CharacterLoadErrorPath}",
                "Load Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static List<Models.CharacterSheet>? FindLatestNonEmptyCharacterBackup(out string? backupPath)
    {
        backupPath = null;
        if (!Directory.Exists(CharacterSaveDirectory))
            return null;

        foreach (string candidatePath in Directory
            .EnumerateFiles(CharacterSaveDirectory, "characters-*.json.bak")
            .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                List<Models.CharacterSheet>? characters = JsonSerializer.Deserialize<List<Models.CharacterSheet>>(
                    File.ReadAllText(candidatePath));
                if (characters is { Count: > 0 })
                {
                    backupPath = candidatePath;
                    return characters;
                }
            }
            catch
            {
                // Ignore an unusable backup and continue to older snapshots.
            }
        }

        return null;
    }

    private static bool IsEmptyRosterExplicitlyConfirmed()
    {
        try
        {
            return File.Exists(EmptyRosterConfirmationPath)
                && File.Exists(CharacterSavePath)
                && File.GetLastWriteTimeUtc(EmptyRosterConfirmationPath) >= File.GetLastWriteTimeUtc(CharacterSavePath);
        }
        catch
        {
            return false;
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

                string normalizedCategory = EquipmentLibraryService.NormalizeCategoryLabel(selection.Category);
                if (!string.Equals(selection.Category ?? string.Empty, normalizedCategory, StringComparison.Ordinal))
                    changedAny = true;

                if (!string.Equals((selection.ItemId ?? string.Empty).Trim(), canonicalId, StringComparison.Ordinal))
                    changedAny = true;

                if (!mergedById.TryGetValue(canonicalId, out var existing))
                {
                    libraryById.TryGetValue(canonicalId, out var libraryItem);

                    mergedById[canonicalId] = new EquipmentSelection
                    {
                        ItemId = canonicalId,
                        Category = EquipmentLibraryService.NormalizeCategoryLabel(
                            string.IsNullOrWhiteSpace(selection.Category)
                                ? (libraryItem?.Categories.FirstOrDefault() ?? string.Empty)
                                : normalizedCategory),
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
                        WeaponAttackBonus = selection.WeaponAttackBonus != 0
                            ? selection.WeaponAttackBonus
                            : (libraryItem?.WeaponAttackBonus ?? 0),
                        WeaponDamageBonus = selection.WeaponDamageBonus != 0
                            ? selection.WeaponDamageBonus
                            : (libraryItem?.WeaponDamageBonus ?? 0),
                        WeaponThac0Modifier = selection.WeaponThac0Modifier != 0
                            ? selection.WeaponThac0Modifier
                            : (libraryItem?.WeaponThac0Modifier ?? 0),
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
                    if (string.IsNullOrWhiteSpace(existing.Category) && !string.IsNullOrWhiteSpace(normalizedCategory))
                        existing.Category = normalizedCategory;
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
                    if (existing.WeaponAttackBonus == 0 && selection.WeaponAttackBonus != 0)
                        existing.WeaponAttackBonus = selection.WeaponAttackBonus;
                    if (existing.WeaponDamageBonus == 0 && selection.WeaponDamageBonus != 0)
                        existing.WeaponDamageBonus = selection.WeaponDamageBonus;
                    if (existing.WeaponThac0Modifier == 0 && selection.WeaponThac0Modifier != 0)
                        existing.WeaponThac0Modifier = selection.WeaponThac0Modifier;
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
            string equippedMainWeaponKey = EquipmentLibraryService.CanonicalizeId(character.EquippedMainWeaponId);
            string equippedOffHandWeaponKey = EquipmentLibraryService.CanonicalizeId(character.EquippedOffHandWeaponId);

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
            if (!string.IsNullOrWhiteSpace(equippedMainWeaponKey) && !mergedById.ContainsKey(equippedMainWeaponKey))
            {
                character.EquippedMainWeaponId = string.Empty;
                changedAny = true;
            }
            if (!string.IsNullOrWhiteSpace(equippedOffHandWeaponKey) && !mergedById.ContainsKey(equippedOffHandWeaponKey))
            {
                character.EquippedOffHandWeaponId = string.Empty;
                changedAny = true;
            }

            character.EquippedMagicalItemIds ??= new List<string>();
            var normalizedMagicalIds = character.EquippedMagicalItemIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(EquipmentLibraryService.CanonicalizeId)
                .Where(mergedById.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => mergedById[id].ItemId)
                .ToList();
            if (!character.EquippedMagicalItemIds.SequenceEqual(normalizedMagicalIds, StringComparer.OrdinalIgnoreCase))
            {
                character.EquippedMagicalItemIds = normalizedMagicalIds;
                changedAny = true;
            }

            // Keep legacy single weapon slot aligned with main weapon.
            string canonicalMain = EquipmentLibraryService.CanonicalizeId(character.EquippedMainWeaponId);
            string canonicalLegacy = EquipmentLibraryService.CanonicalizeId(character.EquippedWeaponId);
            if (!string.Equals(canonicalMain, canonicalLegacy, StringComparison.OrdinalIgnoreCase))
            {
                character.EquippedWeaponId = character.EquippedMainWeaponId;
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

    private static bool NormalizeLoadedCharacterModes(List<Models.CharacterSheet> characters)
    {
        bool changedAny = false;

        foreach (var character in characters)
        {
            string normalizedMode = InferCharacterModeFromSheet(character);
            if (!string.Equals(character.CharacterMode, normalizedMode, StringComparison.OrdinalIgnoreCase))
            {
                character.CharacterMode = normalizedMode;
                changedAny = true;
            }

            if (string.Equals(normalizedMode, "core_rules", StringComparison.OrdinalIgnoreCase)
                && NormalizeCoreCharacterPointFields(character))
            {
                changedAny = true;
            }
        }

        return changedAny;
    }

    private static bool NormalizeCoreCharacterPointFields(Models.CharacterSheet character)
    {
        bool changed = false;

        if (character.UnspentCharacterPoints != 0)
        {
            character.UnspentCharacterPoints = 0;
            changed = true;
        }
        if (character.SpentNwpCharacterPoints != 0)
        {
            character.SpentNwpCharacterPoints = 0;
            changed = true;
        }
        if (character.SpentWeaponCharacterPoints != 0)
        {
            character.SpentWeaponCharacterPoints = 0;
            changed = true;
        }
        if (character.CpPerLevel != 0)
        {
            character.CpPerLevel = 0;
            changed = true;
        }

        if (character.RacialPointBudget != 0)
        {
            character.RacialPointBudget = 0;
            changed = true;
        }
        if (character.RacialPointSpent != 0)
        {
            character.RacialPointSpent = 0;
            changed = true;
        }
        if (character.RacialPointRemaining != 0)
        {
            character.RacialPointRemaining = 0;
            changed = true;
        }
        if (character.ClassPointBudget != 0)
        {
            character.ClassPointBudget = 0;
            changed = true;
        }
        if (character.ClassPointSpent != 0)
        {
            character.ClassPointSpent = 0;
            changed = true;
        }
        if (character.ClassPointRemaining != 0)
        {
            character.ClassPointRemaining = 0;
            changed = true;
        }
        if (character.ClassAbilityCarryoverPoints != 0)
        {
            character.ClassAbilityCarryoverPoints = 0;
            changed = true;
        }

        if (NormalizeCoreLevelOneHitPoints(character))
            changed = true;

        return changed;
    }

    private static bool NormalizeCoreLevelOneHitPoints(Models.CharacterSheet character)
    {
        if (character.Level != 1)
            return false;

        int con = character.Abilities.GetValueOrDefault("con", 10);
        int classHitDie = (character.ClassId ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "fighter" or "ranger" or "paladin" => 10,
            "cleric" or "druid" or "thief" => 8,
            "wizard" => 4,
            "bard" or "psionicist" => 6,
            _ when Services.RulesEngine.IsPriestClassId(character.ClassId) => 8,
            _ => 6,
        };

        int cap = Math.Max(1, classHitDie + CharacterProgressionService.GetConHitPointBonus(con, character.ClassId ?? string.Empty));
        bool changed = false;

        if (character.HitPoints > cap)
        {
            character.HitPoints = cap;
            changed = true;
        }
        if (character.BaseHitPoints > cap)
        {
            character.BaseHitPoints = cap;
            changed = true;
        }
        if (character.CurrentHitPoints > character.HitPoints)
        {
            character.CurrentHitPoints = character.HitPoints;
            changed = true;
        }

        if (character.HitPointGainByLevel.TryGetValue(1, out int levelOneGain) && levelOneGain > cap)
        {
            character.HitPointGainByLevel[1] = cap;
            changed = true;
        }

        return changed;
    }

    private static string InferCharacterModeFromSheet(Models.CharacterSheet character)
    {
        string storedModeToken = CharGenState.NormalizeCharacterModeToken(character.CharacterMode);
        string classModeToken = (character.ClassMode ?? string.Empty).Trim();
        bool raceLooksPo = !string.IsNullOrWhiteSpace(character.RaceId)
            && character.RaceId.Contains("_po", StringComparison.OrdinalIgnoreCase);
        bool classModeLooksPo = string.Equals(classModeToken, "players_option", StringComparison.OrdinalIgnoreCase);
        bool classModeLooksCore = string.Equals(classModeToken, "core_rules", StringComparison.OrdinalIgnoreCase);
        bool hasSelectedClassAbilityChoices = character.SelectedClassAbilityIds.Count > 0
            || character.SelectedAbilitiesByClass.Values.Any(ids => ids is { Count: > 0 });
        bool hasPoProgressSignals = character.CpPerLevel > 0
            || character.UnspentCharacterPoints > 0
            || character.SpentNwpCharacterPoints > 0
            || character.SpentWeaponCharacterPoints > 0
            || hasSelectedClassAbilityChoices
            || character.ClassAbilityCarryoverPoints > 0
            || character.ClassPointSpent > 0;
        bool hasPoBudgetOnlySignals = character.RacialPointBudget > 0
            || character.ClassPointBudget > 0
            || character.RacialPointRemaining > 0
            || character.ClassPointRemaining > 0
            || character.RacialPointSpent > 0;

        // Keep an explicit saved mode stable unless there is a clear contradiction.
        if (string.Equals(storedModeToken, "players_option", StringComparison.OrdinalIgnoreCase))
        {
            bool hasStrongPoSignals = raceLooksPo || classModeLooksPo || hasPoProgressSignals;
            if (classModeLooksCore && !hasStrongPoSignals)
                return "core_rules";
            if (!hasStrongPoSignals)
                return "core_rules";
            return "players_option";
        }

        if (string.Equals(storedModeToken, "core_rules", StringComparison.OrdinalIgnoreCase))
        {
            if (raceLooksPo || classModeLooksPo || hasPoProgressSignals)
                return "players_option";
            return "core_rules";
        }

        if (raceLooksPo || classModeLooksPo)
            return "players_option";

        if (classModeLooksCore)
            return "core_rules";

        if (hasPoProgressSignals || hasPoBudgetOnlySignals)
            return "players_option";

        return "core_rules";
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    public void GoTo(string name, int direction = 1)
    {
        if (Array.IndexOf(CharGenOrder, name) > 0)
            CharGen.CharacterMode = ResolveCharGenCharacterMode();

        if (string.Equals(name, "hub", StringComparison.OrdinalIgnoreCase))
            CloseSpellTrackerWindow();

        if (string.Equals(name, "sessions", StringComparison.OrdinalIgnoreCase)
            && !SessionService.NetworkingEnabled)
        {
            MessageBox.Show(
                "Networking is disabled in this build.",
                "Feature Disabled",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

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

    private string ResolveCharGenCharacterMode()
    {
        string normalized = CharGenState.NormalizeCharacterModeToken(CharGen.CharacterMode);

        // In new-character flow, keep the user-selected rules mode stable.
        // Automatic inference from in-progress selections can incorrectly flip
        // a Core Rules character to Player's Option mid-wizard.
        if (!CharGen.IsLevelUpMode && !CharGen.IsExistingCharacterMode)
            return normalized;

        if (string.Equals(normalized, "players_option", StringComparison.OrdinalIgnoreCase))
            return "players_option";

        bool raceLooksPo = (!string.IsNullOrWhiteSpace(CharGen.RaceId) && CharGen.RaceId.Contains("_po", StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(CharGen.BaseRaceId) && CharGen.BaseRaceId.Contains("_po", StringComparison.OrdinalIgnoreCase));

        bool raceModePo = (!string.IsNullOrWhiteSpace(CharGen.RaceId)
            && Rules.Races.TryGetValue(CharGen.RaceId, out var race)
            && string.Equals(race.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase));

        bool hasPoSignals = CharGen.RacialCarryoverToClassPoints > 0;

        return (raceLooksPo || raceModePo || hasPoSignals) ? "players_option" : "core_rules";
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

    private void BtnJumpClassAbilities_Click(object sender, RoutedEventArgs e)
    {
        bool isPlayersOption = string.Equals(CharGen.CharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);
        if (!isPlayersOption)
        {
            bool canAdjustCore = CharGen.IsExistingCharacterMode || CharGen.IsLevelUpMode;
            if (!canAdjustCore)
            {
                MessageBox.Show(
                    "Class abilities/disadvantages are only available here for existing or update characters.",
                    "Class Abilities Unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var proceed = MessageBox.Show(
                "Core Rules usually auto-grants class abilities. Open Class Abilities anyway?\n\nOnly proceed with DM approval.",
                "Class Abilities",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (proceed != MessageBoxResult.Yes)
                return;
        }

        GoTo("chargen_class_abilities");
    }
    private void BtnJumpOptions_Click(object sender, RoutedEventArgs e) => GoTo("chargen_character_options");
    private void BtnJumpWeaponProf_Click(object sender, RoutedEventArgs e) => GoTo("chargen_weapon_prof");
    private void BtnJumpEquipment_Click(object sender, RoutedEventArgs e) => GoTo("chargen_equipment");
    private void BtnJumpSpells_Click(object sender, RoutedEventArgs e)
    {
        if (BtnJumpSpells.Visibility == Visibility.Visible)
            GoTo("chargen_wizard_spells");
    }
    private void BtnJumpReview_Click(object sender, RoutedEventArgs e) => GoTo("chargen_review");

    private void BtnReportBugCharGen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new BugReportDialog(this, License)
        {
            Owner = this,
        };

        dialog.ShowDialog();
    }

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
    private void BtnHub_Click(object sender, RoutedEventArgs e)
    {
        bool isCharGenFlow = Array.IndexOf(CharGenOrder, _currentScreen) > 0;
        if (isCharGenFlow)
        {
            var result = MessageBox.Show(
                "Return to Character Creation? Unsaved character-creation progress may be lost.",
                "Leave Character Creation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;
        }

        GoTo("characters", -1);
    }

    public void OpenCharacterSheets(int characterIndex = -1)
    {
        PendingCharacterSheetIndex = characterIndex;
        GoTo("character_sheets");
    }

    public void OpenInGameCharacter(int characterIndex)
    {
        PendingInGameCharacterIndex = characterIndex;
        GoTo("in_game_character");
    }

    public void OpenSpellTracker(int characterIndex, string returnScreen = "characters")
    {
        _spellTrackerReturnScreen = string.IsNullOrWhiteSpace(returnScreen) ? "characters" : returnScreen.Trim();
        _spellTrackerReturnCharacterIndex = characterIndex;

        if (_spellTrackerWindow is null || _spellTrackerScreen is null)
        {
            _spellTrackerScreen = new Views.SpellTrackerScreen(this);
            _spellTrackerWindow = new Window
            {
                Title = "Spell Tracker",
                Content = _spellTrackerScreen,
                Owner = this,
                Width = 1180,
                Height = 760,
                MinWidth = 980,
                MinHeight = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = (Brush)FindResource("BrushBg")
            };

            _spellTrackerWindow.Closed += (_, _) =>
            {
                bool shouldNavigateAfterClose = !_suppressSpellTrackerCloseNavigation;
                _suppressSpellTrackerCloseNavigation = false;
                _spellTrackerWindow = null;
                _spellTrackerScreen = null;

                if (shouldNavigateAfterClose)
                    NavigateAfterSpellTrackerClose();
            };
        }

        _spellTrackerScreen.LoadCharacter(characterIndex);
        _spellTrackerWindow.Show();
        if (_spellTrackerWindow.WindowState == WindowState.Minimized)
            _spellTrackerWindow.WindowState = WindowState.Normal;
        _spellTrackerWindow.Activate();
    }

    private void CloseSpellTrackerWindow()
    {
        if (_spellTrackerWindow is null)
            return;

        _suppressSpellTrackerCloseNavigation = true;
        _spellTrackerWindow.Close();
    }

    public void CloseSpellTrackerAndFocusCharacters()
    {
        if (_spellTrackerWindow is not null)
        {
            _suppressSpellTrackerCloseNavigation = false;
            _spellTrackerWindow.Close();
            return;
        }

        NavigateAfterSpellTrackerClose();
    }

    private void NavigateAfterSpellTrackerClose()
    {
        if (string.Equals(_spellTrackerReturnScreen, "in_game_character", StringComparison.OrdinalIgnoreCase)
            && _spellTrackerReturnCharacterIndex >= 0
            && _spellTrackerReturnCharacterIndex < Characters.Count)
        {
            PendingInGameCharacterIndex = _spellTrackerReturnCharacterIndex;
            GoTo("in_game_character", -1);
        }
        else
        {
            GoTo("characters", -1);
        }

        Activate();
        Focus();
    }

    public void StartPlayerLevelUp(int characterIndex, int xpGain, int hpGain, int cpGain)
    {
        if (characterIndex < 0 || characterIndex >= Characters.Count)
            return;

        var character = Characters[characterIndex];

        CharGen.Clear();
        CharGen.IsLevelUpMode = true;
        CharGen.LevelUpCharacterIndex = characterIndex;
        CharGen.ExistingStartingExperience = Math.Max(0, character.ExperiencePoints);
        CharGen.ExistingStartingUnspentCharacterPoints = Math.Max(0, character.UnspentCharacterPoints);
        CharGen.LevelUpPendingExperienceGain = Math.Max(0, xpGain);
        CharGen.LevelUpPendingHitPointGain = Math.Max(0, hpGain);
        string normalizedCharacterMode = InferCharacterModeFromSheet(character);
        if (!string.Equals(character.CharacterMode, normalizedCharacterMode, StringComparison.OrdinalIgnoreCase))
            character.CharacterMode = normalizedCharacterMode;
        bool isPlayersOption = string.Equals(normalizedCharacterMode, "players_option", StringComparison.OrdinalIgnoreCase);
        CharGen.LevelUpPendingCharacterPointGain = isPlayersOption ? Math.Max(0, cpGain) : 0;

        CharGen.Name = character.Name;
        CharGen.PlayerName = character.PlayerName;
        CharGen.Gender = character.Gender;
        CharGen.Alignment = character.Alignment;
        CharGen.DeityId = character.DeityId;
        CharGen.CharacterMode = normalizedCharacterMode;
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
        CharGen.SubAbilities = character.SubAbilities is { Count: > 0 }
            ? new Dictionary<string, int>(character.SubAbilities, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        CharGen.ExceptionalStrength = Math.Clamp(character.ExceptionalStrength, 0, 100);
        CharGen.ExceptionalStrengthLocked = false;

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
        CharGen.UseSpellComponents = character.UseSpellComponents;
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
        CharGen.EquippedMainWeaponId = !string.IsNullOrWhiteSpace(character.EquippedMainWeaponId)
            ? character.EquippedMainWeaponId
            : (character.EquippedWeaponId ?? string.Empty);
        CharGen.EquippedWeaponId = CharGen.EquippedMainWeaponId;
        CharGen.EquippedOffHandWeaponId = character.EquippedOffHandWeaponId ?? string.Empty;
        CharGen.EquippedMagicalItemIds = character.EquippedMagicalItemIds?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
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
        CharGen.SelectedClassAbilityIds = CharGen.SelectedAbilitiesByClass.TryGetValue(CharGen.ClassId, out var activeClassAbilities)
            ? new List<string>(activeClassAbilities)
            : new List<string>(character.SelectedClassAbilityIds);
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
        CharGen.BaselineTraitIds = new List<string>(CharGen.SelectedTraitIds);
        CharGen.BaselineDisadvantageSeverities = new Dictionary<string, string>(
            CharGen.SelectedDisadvantageSeverities,
            StringComparer.OrdinalIgnoreCase);
        CharGen.BaselineWeaponProficiencyIds = CharGen.SelectedWeaponProficiencies
            .Select(x => x.ProficiencyId)
            .ToList();
        CharGen.BaselineWeaponProficiencies = CharGen.SelectedWeaponProficiencies
            .Select(CloneWeaponSelection)
            .ToList();
        CharGen.BaselineEquipmentSelections = CharGen.SelectedEquipment
            .Select(CloneEquipmentSelection)
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
            WeaponMastery = selection.WeaponMastery,
            HighMastery = selection.HighMastery,
            GrandMastery = selection.GrandMastery,
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
            WeaponAttackBonus = selection.WeaponAttackBonus,
            WeaponDamageBonus = selection.WeaponDamageBonus,
            WeaponThac0Modifier = selection.WeaponThac0Modifier,
            CostGoldEach = Math.Max(0, selection.CostGoldEach),
            CostSilverEach = Math.Max(0, selection.CostSilverEach),
            CostCopperEach = Math.Max(0, selection.CostCopperEach),
            SizeClassEach = string.IsNullOrWhiteSpace(selection.SizeClassEach) ? "Medium" : selection.SizeClassEach,
            WeightEach = Math.Max(0, selection.WeightEach),
            UseAsOffHand = selection.UseAsOffHand,
        };

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        CheckAndShowReleaseNotes();
    }

    private void CheckAndShowReleaseNotes()
    {
        try
        {
            var currentVersion = AppUpdateService.GetCurrentVersion();
            var storedVersion = GetStoredVersion();

            // If current version is newer, check if user opted out
            if (currentVersion > storedVersion)
            {
                if (HasUserOptedOutOfReleaseNotes(currentVersion))
                {
                    SaveStoredVersion(currentVersion);
                    return;
                }

                var dialog = new ReleaseNotesDialog(currentVersion)
                {
                    Owner = this
                };
                dialog.ShowDialog();

                SaveStoredVersion(currentVersion, dialog.DoNotShowAgain);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking release notes: {ex.Message}");
        }
    }

    private Version GetStoredVersion()
    {
        try
        {
            var settings = ReadVersionSettings();
            if (Version.TryParse(settings.LastSeenVersion, out var parsedVersion))
                return parsedVersion;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading stored version: {ex.Message}");
        }

        return new Version(0, 0, 0, 0);
    }

    private void SaveStoredVersion(Version version, bool doNotShowAgain = false)
    {
        try
        {
            if (!Directory.Exists(CharacterSaveDirectory))
                Directory.CreateDirectory(CharacterSaveDirectory);

            var settings = ReadVersionSettings();
            settings.LastSeenVersion = AppUpdateService.ToDisplayVersionString(version);
            settings.DoNotShowReleaseNotesForVersions = GetDoNotShowVersions(version, doNotShowAgain);
            WriteVersionSettings(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving version: {ex.Message}");
        }
    }

    private bool HasUserOptedOutOfReleaseNotes(Version version)
    {
        try
        {
            var settings = ReadVersionSettings();
            foreach (var versionStr in settings.DoNotShowReleaseNotesForVersions)
            {
                if (string.Equals(versionStr, AppUpdateService.ToDisplayVersionString(version), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error checking release notes opt-out: {ex.Message}");
        }

        return false;
    }

    private List<string> GetDoNotShowVersions(Version currentVersion, bool addCurrentVersion)
    {
        var versions = new List<string>();

        try
        {
            versions = ReadVersionSettings()
                .DoNotShowReleaseNotesForVersions
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { }

        if (addCurrentVersion)
        {
            string currentVersionStr = AppUpdateService.ToDisplayVersionString(currentVersion);
            if (!versions.Contains(currentVersionStr))
                versions.Add(currentVersionStr);
        }

        return versions;
    }

    private static VersionSettingsData ReadVersionSettings()
    {
        try
        {
            if (!Directory.Exists(CharacterSaveDirectory) || !File.Exists(VersionSettingsPath))
                return new VersionSettingsData();

            var json = File.ReadAllText(VersionSettingsPath);
            var data = JsonSerializer.Deserialize<VersionSettingsData>(json);
            return data ?? new VersionSettingsData();
        }
        catch
        {
            return new VersionSettingsData();
        }
    }

    private static void WriteVersionSettings(VersionSettingsData data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(VersionSettingsPath, json);
    }

    private void LoadGlobalSpellTagsFromSettings()
    {
        _globalSpellTags.Clear();

        try
        {
            var settings = ReadVersionSettings();
            if (settings.GlobalSpellTags is null)
                return;

            foreach (var kv in settings.GlobalSpellTags)
            {
                string spellId = (kv.Key ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(spellId))
                    continue;

                var tags = (kv.Value ?? new List<string>())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (tags.Count > 0)
                    _globalSpellTags[spellId] = tags;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading global spell tags: {ex.Message}");
        }
    }

    private void SaveGlobalSpellTagsToSettings()
    {
        var settings = ReadVersionSettings();
        settings.GlobalSpellTags = new Dictionary<string, List<string>>(_globalSpellTags, StringComparer.OrdinalIgnoreCase);
        WriteVersionSettings(settings);
    }

    public List<string> GetGlobalSpellTags(string spellId)
    {
        string id = (spellId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id))
            return new List<string>();

        if (!_globalSpellTags.TryGetValue(id, out var tags))
            return new List<string>();

        return tags.ToList();
    }

    public bool AddGlobalSpellTag(string spellId, string tag)
    {
        string id = (spellId ?? string.Empty).Trim();
        string cleanedTag = (tag ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(cleanedTag))
            return false;

        if (!_globalSpellTags.TryGetValue(id, out var tags))
        {
            tags = new List<string>();
            _globalSpellTags[id] = tags;
        }

        if (tags.Contains(cleanedTag, StringComparer.OrdinalIgnoreCase))
            return false;

        tags.Add(cleanedTag);
        tags.Sort(StringComparer.OrdinalIgnoreCase);
        SaveGlobalSpellTagsToSettings();
        return true;
    }

    public bool RemoveGlobalSpellTag(string spellId, string tag)
    {
        string id = (spellId ?? string.Empty).Trim();
        string cleanedTag = (tag ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(cleanedTag))
            return false;

        if (!_globalSpellTags.TryGetValue(id, out var tags))
            return false;

        int removed = tags.RemoveAll(t => string.Equals(t, cleanedTag, StringComparison.OrdinalIgnoreCase));
        if (removed <= 0)
            return false;

        if (tags.Count == 0)
            _globalSpellTags.Remove(id);

        SaveGlobalSpellTagsToSettings();
        return true;
    }

    // Navigate back to the previous screen
    public void GoToPreviousScreen()
    {
        if (_backAction != null)
        {
            _backAction.Invoke();
        }
        else
        {
            MessageBox.Show("No previous screen to navigate to.", "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


    public void SetBackAction(Action backAction)
    {
        _backAction = backAction;
    }
}