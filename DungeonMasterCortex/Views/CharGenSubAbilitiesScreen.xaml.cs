using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CharGenSubAbilitiesScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    private readonly Random _rng = new();
    public UIElement View => this;

    // ── Sub-ability definitions ──────────────────────────────────────────────
    // Each entry: (parentAbilityKey, fullName, abbrev, sub-abilities[])
    private static readonly AbilityGroup[] Groups =
    {
        new("str", "Strength", "STR", new[]
        {
            new SubDef("str_stamina", "Stamina",    "Weight allowance / carrying capacity"),
            new SubDef("str_muscle",  "Muscle",     "Melee atk, damage, open doors, bend bars, max press"),
        }),
        new("dex", "Dexterity", "DEX", new[]
        {
            new SubDef("dex_aim",     "Aim",        "Missile adj, Pick Pockets, Open Locks"),
            new SubDef("dex_balance", "Balance",    "Reaction adj, Defense adj, Move Silently, Climb Walls"),
        }),
        new("con", "Constitution", "CON", new[]
        {
            new SubDef("con_health",  "Health",     "System shock survival, poison save"),
            new SubDef("con_fitness", "Fitness",    "Hit point adjustment, resurrection chance"),
        }),
        new("int", "Intelligence", "INT", new[]
        {
            new SubDef("int_reason",    "Reason",    "Spell level, max spells, spell immunity"),
            new SubDef("int_knowledge", "Knowledge", "Bonus proficiencies (languages + NWP), learn spell %"),
        }),
        new("wis", "Wisdom", "WIS", new[]
        {
            new SubDef("wis_intuition",  "Intuition",  "Bonus priest spells, priest spell failure %"),
            new SubDef("wis_willpower",  "Willpower",  "Magic defense adjustment, spell immunity"),
            new SubDef("wis_perception", "Perception", "Surprise modifier, detect hidden, hear noise"),
        }),
        new("cha", "Charisma", "CHA", new[]
        {
            new SubDef("cha_leadership", "Leadership", "Max henchmen, loyalty base, reaction adj"),
            new SubDef("cha_appearance", "Appearance", "Initial NPC reaction adj, social encounters"),
        }),
    };

    // Warrior classes that qualify for exceptional strength
    private static readonly string[] WarriorClasses = { "fighter", "paladin", "ranger" };

    public CharGenSubAbilitiesScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.SetBanner("Character Blueprint  ›  Sub-Ability Scores");

        bool isPO   = _app.CharGen.CharacterMode == "players_option";
        bool isWizardPO = isPO && string.Equals(_app.CharGen.ClassId, "wizard",
                                                 StringComparison.OrdinalIgnoreCase);
        bool hasWizardSpecs = _app.Rules.Classes.TryGetValue("wizard", out var wc)
                              && wc.Specializations is { Count: > 0 };

        int stepTotal = (isWizardPO && hasWizardSpecs) ? 12 : 11;

        _app.SetNavBar(8, stepTotal, "Sub-Ability Scores",
            backAction: () => _app.GoTo("chargen_class_abilities", -1),
            nextAction: Advance);

        bool hadSubAbilityValues = _app.CharGen.SubAbilities.Count > 0;
        var previousModified = _app.CharGen.ModifiedAbilities is { Count: > 0 }
            ? new Dictionary<string, int>(_app.CharGen.ModifiedAbilities, StringComparer.OrdinalIgnoreCase)
            : _app.Rules.ApplyRacialModifiers(_app.CharGen.RaceId, _app.CharGen.Abilities);

        var (effectiveAbilities, bonuses) = BuildPreSplitAbilityState();
        _app.CharGen.ModifiedAbilities = effectiveAbilities;

        // Seed defaults if not yet set (default = modified base score incorporating racial modifiers)
        foreach (var g in Groups)
        {
            // Get the modified base (with racial modifiers applied)
            var mods = _app.CharGen.ModifiedAbilities;
            int modifiedBase = mods is { Count: > 0 }
                ? mods.GetValueOrDefault(g.AbilityKey, 10)
                : _app.CharGen.Abilities.GetValueOrDefault(g.AbilityKey, 10);
            int previousBase = previousModified.GetValueOrDefault(
                g.AbilityKey,
                _app.CharGen.Abilities.GetValueOrDefault(g.AbilityKey, 10));
            int baseDelta = modifiedBase - previousBase;
            
            foreach (var s in g.Subs)
            {
                if (!_app.CharGen.SubAbilities.ContainsKey(s.Key))
                {
                    _app.CharGen.SubAbilities[s.Key] = modifiedBase;
                    continue;
                }

                if (baseDelta != 0)
                {
                    int adjusted = _app.CharGen.SubAbilities.GetValueOrDefault(s.Key, modifiedBase) + baseDelta;
                    _app.CharGen.SubAbilities[s.Key] = ClampSubAbility(adjusted, s.Key);
                }
            }
        }

        // Apply race/class sub-ability bonuses before first split presentation.
        if (!hadSubAbilityValues)
            ApplySubAbilityBonusesToCurrentState(bonuses);

        SyncExceptionalStrengthEligibility();

        ExStrBanner.Text = isPO
            ? "Player's Option mode: sub-abilities default to base score; each can vary ±4."
            : "";

        Refresh();
    }

    private (Dictionary<string, int> effectiveAbilities, AbilityBonuses bonuses) BuildPreSplitAbilityState()
    {
        var effective = _app.Rules.ApplyRacialModifiers(_app.CharGen.RaceId, _app.CharGen.Abilities);
        var racialPackage = _app.Rules.BuildRacialAbilityPackage(_app.CharGen.RaceId, _app.CharGen.SelectedRacialAbilityIds);
        var allStructured = new List<AbilityDefinition>(racialPackage.selectedAbilities);
        int level = Math.Max(1, _app.CharGen.CharacterLevel);

        var classIds = _app.CharGen.SelectedClassIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (classIds.Count == 0 && !string.IsNullOrWhiteSpace(_app.CharGen.ClassId))
            classIds.Add(_app.CharGen.ClassId);

        foreach (string classId in classIds)
        {
            var selectedAbilityIds = _app.CharGen.SelectedAbilitiesByClass.TryGetValue(classId, out var ids) && ids.Count > 0
                ? ids
                : string.Equals(classId, _app.CharGen.ClassId, StringComparison.OrdinalIgnoreCase)
                    ? _app.CharGen.SelectedClassAbilityIds
                    : new List<string>();

            string? specializationId = _app.CharGen.WizardSpecializationById.TryGetValue(classId, out var spec)
                ? spec
                : string.Equals(classId, _app.CharGen.ClassId, StringComparison.OrdinalIgnoreCase)
                    ? _app.CharGen.WizardSpecializationId
                    : string.Empty;

            var classPackage = _app.Rules.BuildClassAbilityPackage(
                classId,
                selectedAbilityIds,
                _app.CharGen.RacialCarryoverToClassPoints,
                specializationId,
                level,
                _app.CharGen.RaceId);

            allStructured.AddRange(classPackage.selectedAbilities);
        }

        bool isUnarmored = string.IsNullOrWhiteSpace(_app.CharGen.RogueSkillArmorProfile)
            || string.Equals(_app.CharGen.RogueSkillArmorProfile, "no_armor", StringComparison.OrdinalIgnoreCase);
        var bonuses = RulesEngine.AggregateEffects(allStructured, isUnarmored, Math.Max(1, _app.CharGen.CharacterLevel));

        return (effective, bonuses);
    }

    private void ApplySubAbilityBonusesToCurrentState(AbilityBonuses bonuses)
    {
        foreach (var (key, mod) in bonuses.SubAbilityBonuses)
        {
            if (string.IsNullOrWhiteSpace(key) || mod == 0)
                continue;

            if (_app.CharGen.SubAbilities.ContainsKey(key))
            {
                int updated = _app.CharGen.SubAbilities[key] + mod;
                _app.CharGen.SubAbilities[key] = ClampSubAbility(updated, key);
                continue;
            }

            string? matchKey = _app.CharGen.SubAbilities.Keys
                .FirstOrDefault(k => k.EndsWith($"_{key}", StringComparison.OrdinalIgnoreCase));
            if (matchKey is null)
                continue;

            int suffixUpdated = _app.CharGen.SubAbilities[matchKey] + mod;
            _app.CharGen.SubAbilities[matchKey] = ClampSubAbility(suffixUpdated, matchKey);
        }
    }

    private static int ClampSubAbility(int value, string subKey)
    {
        int min = 1;
        int max = subKey.StartsWith("str_", StringComparison.OrdinalIgnoreCase) ? 20 : 18;
        return Math.Clamp(value, min, max);
    }

    private void Advance()
    {
        _app.GoTo("chargen_character_options");
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private void Refresh()
    {
        SyncExceptionalStrengthEligibility();
        int exStr = _app.CharGen.ExceptionalStrength;
        bool exStrEligible = ExStrPanel.Visibility == Visibility.Visible;

        // Update exceptional-strength widget
        if (exStrEligible)
        {
            ExStrValue.Text   = exStr == 100 ? "00" : $"{exStr:D2}";
            ExStrInput.Text   = exStr == 100 ? "00" : exStr.ToString();
            ExStrEffect.Text  = ExStrEffectText(exStr);
            ExStrLock.IsChecked = _app.CharGen.ExceptionalStrengthLocked;
            ApplyExStrLockState();
        }

        var rows = new List<AbilityRowVM>();
        foreach (var g in Groups)
        {
            int base_ = _app.CharGen.Abilities.GetValueOrDefault(g.AbilityKey, 10);
            int modified = _app.CharGen.ModifiedAbilities.GetValueOrDefault(g.AbilityKey, base_);
            int modifier = modified - base_;

            var subs = new List<SubAbilityVM>();
            foreach (var s in g.Subs)
            {
                int score = _app.CharGen.SubAbilities.GetValueOrDefault(s.Key, modified);
                int effEx = (s.Key == "str_muscle" && exStrEligible) ? exStr : 0;
                subs.Add(MakeSub(s, score, effEx));
            }

            string modifierLabel = modifier == 0 ? "" : modifier > 0 ? $"+{modifier} from race" : $"{modifier} from race";
            var modifierColor = modifier == 0 ? Brushes.Gray
                              : modifier > 0 ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#50C050"))
                              : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E88860"));

            rows.Add(new AbilityRowVM
            {
                Abbrev           = g.Abbrev,
                Name             = g.FullName,
                BaseScore        = base_.ToString(),
                ModifierLabel    = modifierLabel,
                ModifierColor    = modifierColor,
                Subs             = subs,
            });
        }
        SubAbilityItems.ItemsSource = rows;
    }

    private SubAbilityVM MakeSub(SubDef s, int score, int exStr)
    {
        var color = score >= 15 ? "#E8C050"
                  : score >= 9  ? "#C4A468"
                  : "#C02828";
        return new SubAbilityVM
        {
            Key        = s.Key,
            SubName    = s.Name,
            Score      = score.ToString(),
            ScoreColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
            Hint       = s.Hint,
            Effect     = SubAbilityTables.GetEffect(s.Key, score, exStr),
        };
    }

    // ── Max calculation ───────────────────────────────────────────────────────

    private int GetMax(string abilityKey, int base_)
    {
        // STR can go to 20 (exceptional-strength territory)
        int hardCap = abilityKey == "str" ? 20 : 18;

        // Apply racial maximum if set
        int racialCap = hardCap;
        if (_app.Rules.Races.TryGetValue(_app.CharGen.RaceId, out var race)
            && race.AbilityMaximums.TryGetValue(abilityKey, out int rm))
        {
            racialCap = abilityKey == "str" ? Math.Max(rm, hardCap) : rm;
        }

        return Math.Min(Math.Min(hardCap, racialCap), base_ + 4);
    }

    private int GetMin(int base_) => Math.Max(3, base_ - 4);

    // ── Adjustment handlers ───────────────────────────────────────────────────

    private void Adjust(string key, int delta)
    {
        // Derive parent ability from key prefix (e.g. "str_muscle" -> "str")
        string abilKey = key[..key.IndexOf('_')];
        int base_ = _app.CharGen.Abilities.GetValueOrDefault(abilKey, 10);
        int modifiedBase = _app.CharGen.ModifiedAbilities.GetValueOrDefault(abilKey, base_);
        int current = _app.CharGen.SubAbilities.GetValueOrDefault(key, modifiedBase);
        int desired = current + delta;

        if (desired < GetMin(modifiedBase) || desired > GetMax(abilKey, modifiedBase)) return;

        var group = Groups.FirstOrDefault(g =>
            string.Equals(g.AbilityKey, abilKey, StringComparison.OrdinalIgnoreCase));
        if (group is null)
            return;

        var siblingKeys = group.Subs
            .Select(s => s.Key)
            .Where(k => !string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // For groups with one sub-ability this is a direct adjustment.
        if (siblingKeys.Count == 0)
        {
            _app.CharGen.SubAbilities[key] = desired;
            Refresh();
            return;
        }

        // Auto-couple: raise one -> lower sibling, lower one -> raise sibling.
        // Stop once the split gap would exceed 4.
        if (delta > 0)
        {
            string siblingToLower = siblingKeys
                .OrderByDescending(k => _app.CharGen.SubAbilities.GetValueOrDefault(k, base_))
                .First();

            int sibCurrent = _app.CharGen.SubAbilities.GetValueOrDefault(siblingToLower, base_);
            int sibDesired = sibCurrent - 1;

            if (sibDesired < GetMin(base_)) return;
            if (Math.Abs(desired - sibDesired) > 4) return;

            _app.CharGen.SubAbilities[key] = desired;
            _app.CharGen.SubAbilities[siblingToLower] = sibDesired;
        }
        else
        {
            string siblingToRaise = siblingKeys
                .OrderBy(k => _app.CharGen.SubAbilities.GetValueOrDefault(k, base_))
                .First();

            int sibCurrent = _app.CharGen.SubAbilities.GetValueOrDefault(siblingToRaise, base_);
            int sibDesired = sibCurrent + 1;

            if (sibDesired > GetMax(abilKey, base_)) return;
            if (Math.Abs(desired - sibDesired) > 4) return;

            _app.CharGen.SubAbilities[key] = desired;
            _app.CharGen.SubAbilities[siblingToRaise] = sibDesired;
        }

        Refresh();
    }

    private void BtnIncrease_Click(object sender, RoutedEventArgs e)
        => Adjust(((Button)sender).Tag.ToString()!, +1);

    private void BtnDecrease_Click(object sender, RoutedEventArgs e)
        => Adjust(((Button)sender).Tag.ToString()!, -1);

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _app.CharGen.SubAbilities.Clear();
        _app.CharGen.ExceptionalStrength = 0;
        _app.CharGen.ExceptionalStrengthLocked = false;
        OnEnter();   // re-seed and re-render
    }

    // ── Exceptional Strength ──────────────────────────────────────────────────

    private void BtnExStrInc_Click(object sender, RoutedEventArgs e)
    {
        if (_app.CharGen.ExceptionalStrengthLocked) return;
        int v = Math.Clamp(_app.CharGen.ExceptionalStrength, 1, 100);
        _app.CharGen.ExceptionalStrength = Math.Min(100, v + 1);
        Refresh();
    }

    private void BtnExStrDec_Click(object sender, RoutedEventArgs e)
    {
        if (_app.CharGen.ExceptionalStrengthLocked) return;
        int v = Math.Clamp(_app.CharGen.ExceptionalStrength, 1, 100);
        _app.CharGen.ExceptionalStrength = Math.Max(1, v - 1);
        Refresh();
    }

    private void BtnExStrRoll_Click(object sender, RoutedEventArgs e)
    {
        if (_app.CharGen.ExceptionalStrengthLocked) return;
        _app.CharGen.ExceptionalStrength = _rng.Next(1, 101);
        Refresh();
    }

    private void ExStrInput_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyExceptionalStrengthInput(silentFailure: true);
    }

    private void ExStrInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        ApplyExceptionalStrengthInput(silentFailure: false);
        e.Handled = true;
    }

    private void ApplyExceptionalStrengthInput(bool silentFailure)
    {
        if (_app.CharGen.ExceptionalStrengthLocked)
        {
            ExStrInput.Text = _app.CharGen.ExceptionalStrength == 100
                ? "00"
                : _app.CharGen.ExceptionalStrength.ToString();
            return;
        }

        if (TryParseExceptionalStrength(ExStrInput.Text, out int value))
        {
            _app.CharGen.ExceptionalStrength = value;
            Refresh();
            return;
        }

        if (!silentFailure)
        {
            MessageBox.Show("Enter exceptional strength as 1-100 (you can use 00 for 100).",
                "Invalid Exceptional Strength", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        ExStrInput.Text = _app.CharGen.ExceptionalStrength == 100
            ? "00"
            : _app.CharGen.ExceptionalStrength.ToString();
    }

    private static bool TryParseExceptionalStrength(string? raw, out int value)
    {
        value = 0;
        var text = (raw ?? string.Empty).Trim();
        if (text == "00")
        {
            value = 100;
            return true;
        }

        if (!int.TryParse(text, out var parsed))
            return false;

        if (parsed is < 1 or > 100)
            return false;

        value = parsed;
        return true;
    }

    private void SyncExceptionalStrengthEligibility()
    {
        bool isWarrior = GetEffectiveClassIdsForExceptionalStrength().Any(IsWarriorClassId);

        // Warrior Priests: clerics with this ability qualify for exceptional strength
        if (!isWarrior && string.Equals(_app.CharGen.ClassId, "cleric", StringComparison.OrdinalIgnoreCase))
        {
            isWarrior = _app.CharGen.SelectedClassAbilityIds.Any(entry =>
                string.Equals(RulesEngine.ExtractClassAbilityBaseId(entry),
                    "cleric_warrior_priests", StringComparison.OrdinalIgnoreCase));
        }

        // Use ModifiedAbilities if available (with racial modifiers), otherwise fall back to base
        int strBase = _app.CharGen.ModifiedAbilities.Count > 0
            ? _app.CharGen.ModifiedAbilities.GetValueOrDefault("str", 10)
            : _app.CharGen.Abilities.GetValueOrDefault("str", 10);
        
        int muscle = _app.CharGen.SubAbilities.GetValueOrDefault("str_muscle", strBase);
        int stamina = _app.CharGen.SubAbilities.GetValueOrDefault("str_stamina", strBase);
        bool hasStr18Sub = muscle == 18 || stamina == 18;
        bool hasStr18Core = strBase == 18;
        bool isCoreRules = string.Equals(_app.CharGen.CharacterMode, "core_rules", StringComparison.OrdinalIgnoreCase);

        bool eligible = isWarrior && (isCoreRules ? hasStr18Core : hasStr18Sub);
        ExStrPanel.Visibility = eligible ? Visibility.Visible : Visibility.Collapsed;

        if (!eligible)
        {
            _app.CharGen.ExceptionalStrength = 0;
            _app.CharGen.ExceptionalStrengthLocked = false;
            return;
        }

        if (_app.CharGen.ExceptionalStrength <= 0)
            _app.CharGen.ExceptionalStrength = 50;
    }

    private IEnumerable<string> GetEffectiveClassIdsForExceptionalStrength()
    {
        if (_app.CharGen.SelectedClassIds.Count > 0)
        {
            return _app.CharGen.SelectedClassIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(_app.CharGen.ClassId))
            return new[] { _app.CharGen.ClassId };

        return Array.Empty<string>();
    }

    private static bool IsWarriorClassId(string? classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
            return false;

        string token = classId.Trim();
        if (WarriorClasses.Contains(token, StringComparer.OrdinalIgnoreCase))
            return true;

        return string.Equals(token, "warrior", StringComparison.OrdinalIgnoreCase);
    }

    private void ExStrLock_Checked(object sender, RoutedEventArgs e)
    {
        _app.CharGen.ExceptionalStrengthLocked = true;
        ApplyExStrLockState();
    }

    private void ExStrLock_Unchecked(object sender, RoutedEventArgs e)
    {
        _app.CharGen.ExceptionalStrengthLocked = false;
        ApplyExStrLockState();
    }

    private void ApplyExStrLockState()
    {
        bool locked = _app.CharGen.ExceptionalStrengthLocked;
        BtnExStrDec.IsEnabled = !locked;
        BtnExStrInc.IsEnabled = !locked;
        BtnExStrRoll.IsEnabled = !locked;
        ExStrInput.IsEnabled = !locked;
    }

    private static string ExStrEffectText(int pct)
    {
        if (pct == 0) return "";
        var (bracket, atk, dmg, press) = pct <= 50  ? ("01–50",  "+1", "+3", 280)
                                       : pct <= 75  ? ("51–75",  "+2", "+3", 305)
                                       : pct <= 90  ? ("76–90",  "+2", "+4", 330)
                                       : pct <= 99  ? ("91–99",  "+2", "+5", 380)
                                       :              ("00",     "+3", "+6", 480);
        int display = pct == 100 ? 0 : pct;
        return $"18/{display:D2} in bracket {bracket} -> Atk {atk}, Dmg {dmg}, Max press {press} lbs";
    }
}

// ── View models ───────────────────────────────────────────────────────────────

public record AbilityGroup(
    string AbilityKey,
    string FullName,
    string Abbrev,
    SubDef[] Subs);

public record SubDef(string Key, string Name, string Hint);

public class AbilityRowVM
{
    public string             Abbrev           { get; set; } = "";
    public string             Name             { get; set; } = "";
    public string             BaseScore        { get; set; } = "";
    public string             ModifierLabel    { get; set; } = "";  // e.g., "+1 from race"
    public Brush              ModifierColor    { get; set; } = Brushes.Gray;  // lighter color for modifier
    public string             RangeDisplay     { get; set; } = "";
    public List<SubAbilityVM> Subs             { get; set; } = new();
}

public class SubAbilityVM
{
    public string Key        { get; set; } = "";
    public string SubName    { get; set; } = "";
    public string Score      { get; set; } = "";
    public Brush  ScoreColor { get; set; } = Brushes.White;
    public string Hint       { get; set; } = "";
    public string Effect     { get; set; } = "";
}

