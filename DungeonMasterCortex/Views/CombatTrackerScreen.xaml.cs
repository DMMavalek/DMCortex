using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;
using DungeonMasterCortex.Utilities;

namespace DungeonMasterCortex.Views;

public partial class CombatTrackerScreen : UserControl, IScreen
{
    private enum SpellSaveMode
    {
        HdEstimate,
        ManualTarget,
        Add2eCategory
    }

    private sealed class AttackResolution
    {
        public bool IsValid { get; init; }
        public bool IsHit { get; init; }
        public int Roll { get; init; }
        public int Thac0 { get; init; }
        public int TargetAc { get; init; }
        public int ManualAttackModifier { get; init; }
        public int EnemyAttackBonus { get; init; }
        public int TotalAttackModifier { get; init; }
        public int EffectiveThac0 { get; init; }
        public int NeededRoll { get; init; }
        public int HitAc { get; init; }
        public int AscendingAttackTotal { get; init; }
        public bool IsNat1 { get; init; }
        public bool IsNat20 { get; init; }
        public bool UseAscendingAc { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    private readonly MainWindow _app;
    private CharacterSheet? _enemyModCharacter;
    private Combatant? _attackAttacker;
    private CharacterSheet? _attackTargetCharacter;
    private string _enemyContext = string.Empty;
    private Point _dragStartPoint;
    private Combatant? _dragSourceCombatant;
    private Combatant? _selectedActionAttacker;
    private Combatant? _selectedActionTarget;
    private Combatant? _spellCaster;
    private Combatant? _spellTarget;
    private List<Combatant> _selectedAoeTargets = new();
    private CharacterSheet? _spellcasterCharacter;
    private List<string> _availableSpells = new();
    private HashSet<string> _castSpells = new();
    private bool _spellCastAsHealing;
    private List<Combatant> _spellCasterOptions = new();
    private List<Combatant> _spellTargetOptions = new();
    private TextBox? _spellCasterSearchBox;
    private TextBox? _spellTargetSearchBox;
    private bool _suppressSpellCasterFilter;
    private bool _suppressSpellTargetFilter;
    private SpellDefinition? _selectedSpellDefinition;
    private SpellSaveMode _spellSaveMode = SpellSaveMode.HdEstimate;
    private int _manualMonsterSaveTarget = 14;
    private CharacterSheet? _selectedCharacterToAdd;
    private MonsterDefinition? _selectedMonster;
    private string _selectedPartyName = string.Empty;
    private bool _isUpdatingRowTargetPicker;
    private bool _isCombatStarted;
    private string _activeCombatantId = string.Empty;
    private bool _isRoundCompletePending;
    private int _pendingCompletedRound;
    public UIElement View => this;

    public CombatTrackerScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.SetBanner("DM Tools  ›  Combat Tracker");
        _isCombatStarted = _app.Combat.Current?.Combatants.Any(c => c.IsActiveTurn) == true;
        _activeCombatantId = _app.Combat.Current?.Combatants.FirstOrDefault(c => c.IsActiveTurn)?.CombatantId ?? string.Empty;
        _isRoundCompletePending = false;
        _pendingCompletedRound = 0;
        UpdateRoundCompleteBanner();
        RefreshEncounterBuilders();
        RefreshEnemyModifierTools();
        ApplyPendingMonsterSelection();
        RefreshList();
    }

    private void ApplyPendingMonsterSelection()
    {
        string pendingMonsterId = (_app.PendingCombatMonsterId ?? string.Empty).Trim();
        int pendingQty = Math.Max(1, _app.PendingCombatMonsterQuantity);
        _app.PendingCombatMonsterId = string.Empty;
        _app.PendingCombatMonsterQuantity = 1;

        if (string.IsNullOrWhiteSpace(pendingMonsterId))
            return;

        var pendingMonster = _app.Rules.Monsters.FirstOrDefault(m =>
            string.Equals(m.Id, pendingMonsterId, StringComparison.OrdinalIgnoreCase));
        if (pendingMonster is null)
            return;

        _selectedMonster = pendingMonster;
        MonsterPicker.SelectedItem = pendingMonster;
        MonsterQtyBox.Text = pendingQty.ToString();
        MonsterAttackPatternBox.Text = pendingMonster.Attacks <= 0 ? "1" : pendingMonster.Attacks.ToString();

        if (_app.Combat.Current is null)
            _app.Combat.NewEncounter("Encounter");

        int added = 0;
        for (int i = 1; i <= pendingQty; i++)
        {
            var combatant = CreateMonsterCombatant(pendingMonster, i, MonsterAttackPatternBox.Text);
            if (AddCombatantIfMissing(combatant))
                added++;
        }

        if (added > 0)
            StatusBar.Text = $"Added {added} {pendingMonster.Name.ToLowerInvariant()}{(added == 1 ? string.Empty : "s")} from Monsters tab.";

        RefreshEncounterBuilders();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) =>
        _app.GoTo("dm_tools", -1);

    private void BtnNewEncounter_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtEncName.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = "Encounter";
        _app.Combat.NewEncounter(name);
        _isRoundCompletePending = false;
        _pendingCompletedRound = 0;
        UpdateRoundCompleteBanner();
        StatusBar.Text = $"Encounter '{name}' started.";
        ResetRoundAttackCounts();
        RefreshEncounterBuilders();
        RefreshEnemyModifierTools();
        RefreshList();
    }

    private void RefreshEncounterBuilders()
    {
        var parties = _app.Characters
            .Select(c => (c.Party ?? string.Empty).Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PartyPicker.ItemsSource = parties;
        if (!string.IsNullOrWhiteSpace(_selectedPartyName) && parties.Any(p => string.Equals(p, _selectedPartyName, StringComparison.OrdinalIgnoreCase)))
            PartyPicker.SelectedItem = parties.First(p => string.Equals(p, _selectedPartyName, StringComparison.OrdinalIgnoreCase));
        else if (PartyPicker.SelectedItem is null && parties.Count > 0)
            PartyPicker.SelectedIndex = 0;

        var characters = _app.Characters.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        CharacterPicker.ItemsSource = characters;
        CharacterPicker.DisplayMemberPath = "Name";
        if (_selectedCharacterToAdd is not null)
            CharacterPicker.SelectedItem = characters.FirstOrDefault(c => string.Equals(c.Name, _selectedCharacterToAdd.Name, StringComparison.OrdinalIgnoreCase));
        else if (CharacterPicker.SelectedItem is null && characters.Count > 0)
            CharacterPicker.SelectedIndex = 0;

        var monsters = _app.Rules.Monsters
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        MonsterPicker.ItemsSource = monsters;
        MonsterPicker.DisplayMemberPath = "Name";
        if (_selectedMonster is not null)
            MonsterPicker.SelectedItem = monsters.FirstOrDefault(m => string.Equals(m.Name, _selectedMonster.Name, StringComparison.OrdinalIgnoreCase));
        else if (MonsterPicker.SelectedItem is null && monsters.Count > 0)
            MonsterPicker.SelectedIndex = 0;

        RefreshCombatantTargetPickers();
    }

    private void RefreshCombatantTargetPickers()
    {
        var combatants = _app.Combat.Current?.Combatants ?? new List<Combatant>();
        var ordered = combatants.OrderBy(c => c.Initiative).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var actingCombatants = ordered.Where(c => c.IsAbleToAct).ToList();

        CombatActionAttackerPicker.ItemsSource = actingCombatants;
        CombatActionAttackerPicker.DisplayMemberPath = "DisplayName";
        if (_selectedActionAttacker is not null)
            CombatActionAttackerPicker.SelectedItem = actingCombatants.FirstOrDefault(c => string.Equals(c.CombatantId, _selectedActionAttacker.CombatantId, StringComparison.OrdinalIgnoreCase));
        else if (CombatActionAttackerPicker.SelectedItem is null && actingCombatants.Count > 0)
            CombatActionAttackerPicker.SelectedIndex = 0;

        var validTargets = _selectedActionAttacker is null
            ? actingCombatants
            : ordered.Where(c => CanTarget(_selectedActionAttacker, c)).ToList();

        CombatActionTargetPicker.ItemsSource = validTargets;
        CombatActionTargetPicker.DisplayMemberPath = "DisplayName";
        if (_selectedActionTarget is not null)
            CombatActionTargetPicker.SelectedItem = validTargets.FirstOrDefault(c => string.Equals(c.CombatantId, _selectedActionTarget.CombatantId, StringComparison.OrdinalIgnoreCase));
        else if (CombatActionTargetPicker.SelectedItem is null && validTargets.Count > 0)
            CombatActionTargetPicker.SelectedIndex = 0;

        UpdateCombatActionInfo();
        RefreshSpellTools();
    }

    private void RefreshSpellTools()
    {
        var ordered = GetOrderedCombatants();
        _spellCasterOptions = ordered.ToList();
        _spellTargetOptions = ordered.ToList();

        if (SpellSaveModePicker is not null && SpellSaveModePicker.Items.Count == 0)
        {
            SpellSaveModePicker.ItemsSource = new List<string>
            {
                "Save: HD Estimate",
                "Save: Manual Target",
                "Save: AD&D 2e Category"
            };
            SpellSaveModePicker.SelectedIndex = (int)_spellSaveMode;
        }

        if (TxtManualMonsterSave is not null)
            TxtManualMonsterSave.Text = _manualMonsterSaveTarget.ToString();

        var casterItems = FilterCombatantsByText(_spellCasterOptions, _spellCasterSearchBox?.Text);
        SpellCasterPicker.ItemsSource = casterItems;
        SpellCasterPicker.DisplayMemberPath = "DisplayName";
        if (_spellCaster is not null)
            SpellCasterPicker.SelectedItem = casterItems.FirstOrDefault(c => string.Equals(c.CombatantId, _spellCaster.CombatantId, StringComparison.OrdinalIgnoreCase));
        else if (SpellCasterPicker.SelectedItem is null && casterItems.Count > 0)
            SpellCasterPicker.SelectedIndex = 0;

        var targetItems = FilterCombatantsByText(_spellTargetOptions, _spellTargetSearchBox?.Text);
        SpellTargetPicker.ItemsSource = targetItems;
        SpellTargetPicker.DisplayMemberPath = "DisplayName";
        if (_spellTarget is not null)
            SpellTargetPicker.SelectedItem = targetItems.FirstOrDefault(c => string.Equals(c.CombatantId, _spellTarget.CombatantId, StringComparison.OrdinalIgnoreCase));
        else if (SpellTargetPicker.SelectedItem is null && targetItems.Count > 0)
            SpellTargetPicker.SelectedIndex = 0;

        UpdateSpellActionInfo();
    }

    private static List<Combatant> FilterCombatantsByText(IEnumerable<Combatant> source, string? filter)
    {
        string text = (filter ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return source.ToList();

        return source
            .Where(c => (c.DisplayName ?? string.Empty).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
    }

    private void SpellSaveModePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpellSaveModePicker is null)
            return;

        _spellSaveMode = SpellSaveModePicker.SelectedIndex switch
        {
            1 => SpellSaveMode.ManualTarget,
            2 => SpellSaveMode.Add2eCategory,
            _ => SpellSaveMode.HdEstimate
        };

        UpdateSpellActionInfo();
    }

    private void TxtManualMonsterSave_LostFocus(object sender, RoutedEventArgs e)
    {
        if (TxtManualMonsterSave is null)
            return;

        if (!int.TryParse((TxtManualMonsterSave.Text ?? string.Empty).Trim(), out int parsed))
            parsed = _manualMonsterSaveTarget;

        _manualMonsterSaveTarget = Math.Clamp(parsed, 2, 20);
        TxtManualMonsterSave.Text = _manualMonsterSaveTarget.ToString();
    }

    private void LoadSpellsFromCharacter(CharacterSheet? character)
    {
        _spellcasterCharacter = character;
        _availableSpells.Clear();
        _castSpells.Clear();
        _selectedSpellDefinition = null;

        if (character is null)
            return;

        var spellNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Include daily spell tracking data (Spell Tracker integration)
        var dailyTracking = character.SpellTracking?.CurrentDayTracking;
        if (dailyTracking is not null)
        {
            foreach (var prepared in dailyTracking.PreparedSpells)
            {
                string name = string.IsNullOrWhiteSpace(prepared.SpellId)
                    ? (prepared.SpellName ?? string.Empty).Trim()
                    : ResolveSpellDisplayName(prepared.SpellId);
                if (!string.IsNullOrWhiteSpace(name))
                    spellNames.Add(name);
            }
        }

        // 2) Include legacy wizard named spell lists.
        foreach (var list in character.WizardSpellLists)
        {
            foreach (var spellId in list.SpellIds)
            {
                string name = ResolveSpellDisplayName(spellId);
                if (!string.IsNullOrWhiteSpace(name))
                    spellNames.Add(name);
            }
        }

        // 3) Include wizard spellbooks.
        foreach (var spellbook in character.WizardSpellbooks)
        {
            foreach (var spellId in spellbook.SpellPages.Keys)
            {
                string name = ResolveSpellDisplayName(spellId);
                if (!string.IsNullOrWhiteSpace(name))
                    spellNames.Add(name);
            }
        }

        // 4) Include tracked spell IDs.
        foreach (var spellId in character.TrackedSpellIds)
        {
            string name = ResolveSpellDisplayName(spellId);
            if (!string.IsNullOrWhiteSpace(name))
                spellNames.Add(name);
        }

        _availableSpells = spellNames
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        UpdateSpellActionInfo();
    }

    private static bool IsArcaneCaster(CharacterSheet character)
    {
        string combined = BuildClassText(character);
        return combined.Contains("wizard", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("mage", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("illusionist", StringComparison.OrdinalIgnoreCase)
            || character.ArcaneSpellSlots.Count > 0
            || character.WizardSpellbooks.Count > 0
            || character.WizardSpellLists.Count > 0;
    }

    private static bool IsDivineCaster(CharacterSheet character)
    {
        string combined = BuildClassText(character);
        return combined.Contains("priest", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("cleric", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("druid", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("paladin", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("ranger", StringComparison.OrdinalIgnoreCase)
            || character.DivineSpellSlots.Count > 0;
    }

    private static string BuildClassText(CharacterSheet character)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(character.ClassId)) parts.Add(character.ClassId);
        if (!string.IsNullOrWhiteSpace(character.ClassName)) parts.Add(character.ClassName);
        parts.AddRange(character.ClassIds.Where(c => !string.IsNullOrWhiteSpace(c)));
        return string.Join(" ", parts);
    }

    private string ResolveSpellDisplayName(string? spellId)
    {
        string id = (spellId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id))
            return string.Empty;

        var spell = _app.Rules.Spells.FirstOrDefault(s =>
            string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        return spell is null || string.IsNullOrWhiteSpace(spell.Name)
            ? id
            : spell.Name.Trim();
    }

    private bool IsSpellIdDivine(string? spellId)
    {
        string id = (spellId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var spell = _app.Rules.Spells.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (spell is null)
            return false;

        string category = (spell.Category ?? string.Empty).Trim();
        return category.Contains("divine", StringComparison.OrdinalIgnoreCase)
            || category.Contains("priest", StringComparison.OrdinalIgnoreCase)
            || category.Contains("cleric", StringComparison.OrdinalIgnoreCase)
            || category.Contains("druid", StringComparison.OrdinalIgnoreCase);
    }

    private void MarkSpellAsCast(string spellName)
    {
        if (string.IsNullOrWhiteSpace(spellName))
            return;

        _castSpells.Add(spellName.ToLowerInvariant());
        _availableSpells.RemoveAll(s => string.Equals(s, spellName, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateCombatActionInfo(string? message = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            CombatActionInfo.Text = message;
            return;
        }

        if (_selectedActionAttacker is null)
        {
            CombatActionInfo.Text = "Select an attacker, assign a target by drag-and-drop or the target picker, then click HIT or MISS.";
            return;
        }

        string attacker = _selectedActionAttacker.DisplayName;
        string target = _selectedActionTarget?.DisplayName ?? _selectedActionAttacker.AssignedTargetName;
        string attacks = _selectedActionAttacker.AttacksRemainingThisRound > 0
            ? $"{_selectedActionAttacker.AttacksRemainingThisRound} attacks left this round"
            : "attack count will reset next round";
        CombatActionInfo.Text = string.IsNullOrWhiteSpace(target)
            ? $"{attacker}: {attacks}. Assign a target by drag-and-drop or the target picker."
            : $"{attacker} -> {target}: {attacks}. Click HIT or MISS to advance the sequence.";
    }

    private void CharacterPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedCharacterToAdd = CharacterPicker.SelectedItem as CharacterSheet;
    }

    private void MonsterPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedMonster = MonsterPicker.SelectedItem as MonsterDefinition;
        if (_selectedMonster is not null)
        {
            MonsterAttackPatternBox.Text = _selectedMonster.Attacks <= 0 ? "1" : _selectedMonster.Attacks.ToString();
        }
    }

    private void CombatActionAttackerPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedActionAttacker = CombatActionAttackerPicker.SelectedItem as Combatant;
        if (_selectedActionAttacker is not null && _selectedActionTarget is not null && !CanTarget(_selectedActionAttacker, _selectedActionTarget))
            _selectedActionTarget = null;

        RefreshCombatantTargetPickers();
        UpdateCombatActionInfo();
    }

    private void CombatActionTargetPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedActionTarget = CombatActionTargetPicker.SelectedItem as Combatant;
        AssignTargetFromPicker();
        UpdateCombatActionInfo();
    }

    private void BtnAddParty_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null)
        {
            if (MessageBox.Show("No encounter active. Create a new encounter?", "No Encounter", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                string name = TxtEncName.Text.Trim();
                if (string.IsNullOrEmpty(name)) name = "Encounter";
                _app.Combat.NewEncounter(name);
                RefreshEncounterBuilders();
                BtnAddParty_Click(sender, e);
            }
            return;
        }

        string partyName = (PartyPicker.SelectedItem as string ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(partyName))
        {
            MessageBox.Show("Select a party first.", "Party", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _selectedPartyName = partyName;
        int added = 0;
        foreach (var character in _app.Characters.Where(c => string.Equals((c.Party ?? string.Empty).Trim(), partyName, StringComparison.OrdinalIgnoreCase)))
        {
            if (AddCombatantIfMissing(CreateCharacterCombatant(character)))
                added++;
        }

        StatusBar.Text = added > 0
            ? $"Added {added} party member{(added == 1 ? string.Empty : "s")} from '{partyName}'."
            : $"No new members from '{partyName}' were added.";
        RefreshEncounterBuilders();
        RefreshList();
    }

    private void BtnAddCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null)
        {
            if (MessageBox.Show("No encounter active. Create a new encounter?", "No Encounter", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                string name = TxtEncName.Text.Trim();
                if (string.IsNullOrEmpty(name)) name = "Encounter";
                _app.Combat.NewEncounter(name);
                RefreshEncounterBuilders();
                BtnAddCharacter_Click(sender, e);
            }
            return;
        }

        var character = _selectedCharacterToAdd ?? CharacterPicker.SelectedItem as CharacterSheet;
        if (character is null)
        {
            MessageBox.Show("Select a character first.", "Character", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (AddCombatantIfMissing(CreateCharacterCombatant(character)))
            StatusBar.Text = $"Added {character.Name} to the encounter.";
        else
            StatusBar.Text = $"{character.Name} is already in the encounter.";

        RefreshEncounterBuilders();
        RefreshList();
    }

    private void BtnAddMonsterGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null)
        {
            if (MessageBox.Show("No encounter active. Create a new encounter?", "No Encounter", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                string name = TxtEncName.Text.Trim();
                if (string.IsNullOrEmpty(name)) name = "Encounter";
                _app.Combat.NewEncounter(name);
                RefreshEncounterBuilders();
                BtnAddMonsterGroup_Click(sender, e);
            }
            return;
        }

        var monster = _selectedMonster ?? MonsterPicker.SelectedItem as MonsterDefinition;
        if (monster is null)
        {
            MessageBox.Show("Select a monster first.", "Monster", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(MonsterQtyBox.Text.Trim(), out int qty) || qty < 1)
        {
            MessageBox.Show("Quantity must be a positive number.", "Monster", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string attackPattern = MonsterAttackPatternBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(attackPattern))
            attackPattern = monster.Attacks > 0 ? monster.Attacks.ToString() : "1";

        int added = 0;
        for (int i = 1; i <= qty; i++)
        {
            var combatant = CreateMonsterCombatant(monster, i, attackPattern);
            if (AddCombatantIfMissing(combatant))
                added++;
        }

        StatusBar.Text = added > 0
            ? $"Added {added} {monster.Name.ToLowerInvariant()}{(added == 1 ? string.Empty : "s")} to the encounter."
            : $"No new {monster.Name.ToLowerInvariant()} entries were added.";
        RefreshEncounterBuilders();
        RefreshList();
    }

    private void BtnHit_Click(object sender, RoutedEventArgs e)
    {
        ResolveSelectedCombatantAttack(isHit: true);
    }

    private void BtnMiss_Click(object sender, RoutedEventArgs e)
    {
        ResolveSelectedCombatantAttack(isHit: false);
    }

    private void ResolveSelectedCombatantAttack(bool isHit)
    {
        if (!_isCombatStarted)
        {
            MessageBox.Show("Click START COMBAT first.", "Combat", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var attacker = _selectedActionAttacker ?? CombatActionAttackerPicker.SelectedItem as Combatant;
        if (attacker is null)
        {
            MessageBox.Show("Select an attacker first.", "Combat", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!attacker.IsAbleToAct)
        {
            StatusBar.Text = $"{attacker.DisplayName} cannot attack while {attacker.ConditionDisplay.ToLowerInvariant()}.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(_activeCombatantId)
            && !string.Equals(attacker.CombatantId, _activeCombatantId, StringComparison.OrdinalIgnoreCase))
        {
            var active = _app.Combat.Current?.Combatants.FirstOrDefault(c => string.Equals(c.CombatantId, _activeCombatantId, StringComparison.OrdinalIgnoreCase));
            if (active is not null)
            {
                StatusBar.Text = $"It is {active.DisplayName}'s turn.";
                _selectedActionAttacker = active;
                CombatActionAttackerPicker.SelectedItem = active;
                return;
            }
        }

        var target = _selectedActionTarget ?? CombatActionTargetPicker.SelectedItem as Combatant;
        if (target is null && !string.IsNullOrWhiteSpace(attacker.AssignedTargetId))
            target = _app.Combat.Current?.Combatants.FirstOrDefault(c => string.Equals(c.CombatantId, attacker.AssignedTargetId, StringComparison.OrdinalIgnoreCase));

        if (!isHit)
        {
            AdvanceAttackState(attacker, null);
            StatusBar.Text = $"MISS: {attacker.DisplayName} attack advanced.";
            RefreshList();
            return;
        }

        if (target is null)
        {
            MessageBox.Show("Assign a target to this combatant first.", "Combat", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int damage = PromptForDamage(attacker, target);
        if (damage < 0)
            return;

        _app.Combat.ApplyDamage(target, damage);
        ProcessPostDamageStateChanges(target);
        AdvanceAttackState(attacker, target);
        StatusBar.Text = $"HIT: {attacker.DisplayName} dealt {damage} to {target.DisplayName}. HP now {target.HpCurrent}/{target.HpMax}.";
        RefreshList();
        CheckCombatEndConditions();
    }

    private void AdvanceAttackState(Combatant attacker, Combatant? target)
    {
        if (_app.Combat.Current is null)
            return;

        attacker.AttacksRemainingThisRound = Math.Max(0, attacker.AttacksRemainingThisRound - 1);
        if (target is not null)
        {
            attacker.AssignedTargetId = target.CombatantId;
            attacker.AssignedTargetName = target.DisplayName;
        }

        if (attacker.AttacksRemainingThisRound <= 0)
        {
            attacker.AttackCursor++;
            AdvanceToNextActiveCombatant(attacker);
        }
    }

    private void AdvanceToNextActiveCombatant(Combatant currentAttacker)
    {
        if (_app.Combat.Current is null)
            return;

        MoveCombatantToBottom(currentAttacker);

        var ordered = GetOrderedCombatants();
        if (ordered.Count == 0)
            return;

        for (int offset = 0; offset < ordered.Count; offset++)
        {
            var candidate = ordered[offset];
            if (!candidate.IsAbleToAct || candidate.AttacksRemainingThisRound <= 0)
                continue;

            _activeCombatantId = candidate.CombatantId;
            _selectedActionAttacker = candidate;
            CombatActionAttackerPicker.SelectedItem = candidate;
            return;
        }

        if (_app.Combat.Current.Combatants.Any(c => c.IsAbleToAct))
        {
            TryAdvanceToNextRoundWithReview(showEndRoundNotification: true);
            return;
        }

        _activeCombatantId = string.Empty;
        StatusBar.Text = "No conscious combatants remain.";
    }

    private void ConsumeTurnAndAdvance(Combatant actor)
    {
        actor.AttacksRemainingThisRound = 0;
        actor.AttackCursor++;
        AdvanceToNextActiveCombatant(actor);
    }

    private void MoveCombatantToBottom(Combatant combatant)
    {
        if (_app.Combat.Current is null)
            return;

        var list = _app.Combat.Current.Combatants;
        int index = list.FindIndex(c => string.Equals(c.CombatantId, combatant.CombatantId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;

        var item = list[index];
        list.RemoveAt(index);
        list.Add(item);
    }

    private int PromptForDamage(Combatant attacker, Combatant target)
    {
        var window = new Window
        {
            Title = $"Damage - {attacker.DisplayName} → {target.DisplayName}",
            Width = 360,
            Height = 190,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(attacker.DamageProfile)
                ? "Enter damage to apply:"
                : $"Enter damage to apply (monster profile: {attacker.DamageProfile}):",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var damageBox = new TextBox
        {
            Text = "",
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 10),
        };
        panel.Children.Add(damageBox);

        // Focus and select all text (empty, so just focus for typing)
        damageBox.Loaded += (_, _) => damageBox.Focus();

        int selectedDamage = -1;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(cancel);

        var apply = new Button { Content = "Apply", Width = 80, IsDefault = true };
        apply.Click += (_, _) =>
        {
            if (!int.TryParse(damageBox.Text.Trim(), out selectedDamage) || selectedDamage < 0)
            {
                MessageBox.Show("Enter a non-negative whole number for damage.", "Damage", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            window.DialogResult = true;
        };
        buttons.Children.Add(apply);

        panel.Children.Add(buttons);
        window.Content = panel;

        if (window.ShowDialog() != true)
            return -1;

        return selectedDamage;
    }

    private static Combatant CreateCharacterCombatant(CharacterSheet character)
    {
        var pattern = NormalizeAttackPattern(character.AttackRate);
        return new Combatant
        {
            CombatantId = $"pc:{character.Name}".ToLowerInvariant(),
            Kind = "PC",
            SourceName = character.Name,
            PartyName = character.Party,
            Name = character.Name,
            Initiative = 0,
            HpCurrent = character.CurrentHitPoints,
            HpMax = character.HitPoints,
            ArmorClass = character.ArmorClass,
            Thac0 = character.Thac0,
            Thac0Text = character.Thac0.ToString(),
            AttackPatternText = pattern,
            DamageProfile = string.Empty,
            AttacksRemainingThisRound = GetAttacksForPattern(pattern, 1),
            AssignedTargetName = string.Empty,
            SpeedFactor = 0,
            DamageRollExpression = string.Empty,
        };
    }

    private static Combatant CreateMonsterCombatant(MonsterDefinition monster, int number, string attackPattern)
    {
        string normalizedAttackPattern = NormalizeAttackPattern(attackPattern);
        int hp = RollMonsterHitPoints(monster.HitDice);
        int speedFactor = ExtractSpeedFactor(monster.Movement);
        string defaultDamageRoll = DetermineDefaultDamageExpression(monster.Damage);
        return new Combatant
        {
            CombatantId = $"monster:{monster.Id}:{number}".ToLowerInvariant(),
            Kind = "Monster",
            SourceName = monster.Name,
            MonsterBaseName = monster.Name,
            MonsterNumber = number,
            Name = $"{monster.Name} {number}",
            Initiative = 0,
            HpCurrent = hp,
            HpMax = hp,
            ArmorClass = monster.ArmorClass,
            Thac0 = monster.Thac0,
            Thac0Text = monster.EffectiveThac0Text,
            AttackPatternText = normalizedAttackPattern,
            DamageProfile = monster.Damage,
            AttacksRemainingThisRound = GetAttacksForPattern(normalizedAttackPattern, 1),
            AssignedTargetName = string.Empty,
            SpeedFactor = speedFactor,
            DamageRollExpression = defaultDamageRoll,
        };
    }

    private static string DetermineDefaultDamageExpression(string? profile)
    {
        string text = (profile ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        if (text.Contains("by weapon", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var diceMatch = Regex.Match(text, @"(\d+d\d+(?:[+-]\d+)?)", RegexOptions.IgnoreCase);
        if (diceMatch.Success)
            return diceMatch.Groups[1].Value.ToLowerInvariant();

        var rangeMatch = Regex.Match(text, @"(\d+)\s*-\s*(\d+)");
        if (rangeMatch.Success)
            return $"{rangeMatch.Groups[1].Value}-{rangeMatch.Groups[2].Value}";

        return string.Empty;
    }

    private static bool TryRollDamageValue(string? expression, out int damage)
    {
        damage = 0;
        string text = (expression ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (int.TryParse(text, out int flat))
        {
            damage = Math.Max(0, flat);
            return true;
        }

        var rangeMatch = Regex.Match(text, @"^(\d+)\s*-\s*(\d+)$");
        if (rangeMatch.Success)
        {
            int min = int.Parse(rangeMatch.Groups[1].Value);
            int max = int.Parse(rangeMatch.Groups[2].Value);
            if (max < min)
                (min, max) = (max, min);
            damage = Random.Shared.Next(min, max + 1);
            return true;
        }

        if (Regex.IsMatch(text, @"^\d+d\d+([+-]\d+)?$", RegexOptions.IgnoreCase))
        {
            damage = Math.Max(0, DiceRoller.Roll(text));
            return true;
        }

        return false;
    }

    private static string NormalizeDamageExpression(string? expression)
    {
        string text = (expression ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (Regex.IsMatch(text, @"^\d+$"))
            return text;

        var rangeMatch = Regex.Match(text, @"^(\d+)\s*-\s*(\d+)$");
        if (rangeMatch.Success)
            return $"{rangeMatch.Groups[1].Value}-{rangeMatch.Groups[2].Value}";

        var diceMatch = Regex.Match(text, @"^(\d+d\d+(?:[+-]\d+)?)$", RegexOptions.IgnoreCase);
        if (diceMatch.Success)
            return diceMatch.Groups[1].Value.ToLowerInvariant();

        return text;
    }

    private bool AddCombatantIfMissing(Combatant combatant)
    {
        if (_app.Combat.Current is null)
            return false;

        combatant.AttacksRemainingThisRound = GetAttacksForPattern(combatant.AttackPatternText, _app.Combat.Current.RoundNumber);

        if (_app.Combat.Current.Combatants.Any(existing =>
                string.Equals(existing.CombatantId, combatant.CombatantId, StringComparison.OrdinalIgnoreCase)
                || (existing.IsMonster && combatant.IsMonster && string.Equals(existing.Name, combatant.Name, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        _app.Combat.AddCombatant(combatant);
        return true;
    }

    private static string NormalizeAttackPattern(string? pattern)
    {
        var cleaned = (pattern ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
            return "1";

        if (cleaned.Contains("/round", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned.Replace("/round", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

        return cleaned;
    }

    private static int GetAttacksForPattern(string? pattern, int roundNumber)
    {
        var cleaned = NormalizeAttackPattern(pattern);
        if (int.TryParse(cleaned, out int fixedCount))
            return Math.Max(1, fixedCount);

        // Handle fractional patterns like "1/2" → use first value only
        if (cleaned.Contains('/'))
        {
            var parts = cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0 && int.TryParse(parts[0], out int firstValue))
                return Math.Max(1, firstValue);
            return 1;
        }

        // Handle decimal patterns like "1.2"
        if (cleaned.Contains('.'))
        {
            var parts = cleaned.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => int.TryParse(p, out int value) ? Math.Max(1, value) : 1)
                .ToList();
            if (parts.Count == 0)
                return 1;
            int index = Math.Max(0, (roundNumber - 1) % parts.Count);
            return parts[index];
        }

        return 1;
    }

    private static int RollMonsterHitPoints(string hitDice)
    {
        var text = (hitDice ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text))
            return 1;

        text = text.Replace("hd", string.Empty).Replace("hp", string.Empty).Trim();

        if (text is "1/2" or "½")
            return DiceRoller.Roll("1d4");

        if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+d\d+([+-]\d+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return Math.Max(1, DiceRoller.Roll(text));

        var plusMatch = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+)\s*\+\s*(\d+)$");
        if (plusMatch.Success)
        {
            int dice = int.Parse(plusMatch.Groups[1].Value);
            int bonus = int.Parse(plusMatch.Groups[2].Value);
            return Math.Max(1, DiceRoller.Roll($"{Math.Max(1, dice)}d8+{bonus}"));
        }

        var minusMatch = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+)\s*-\s*(\d+)$");
        if (minusMatch.Success)
        {
            int left = int.Parse(minusMatch.Groups[1].Value);
            int right = int.Parse(minusMatch.Groups[2].Value);
            if (left <= right)
            {
                int rolledDice = Random.Shared.Next(left, right + 1);
                return Math.Max(1, DiceRoller.Roll($"{Math.Max(1, rolledDice)}d8"));
            }

            return Math.Max(1, DiceRoller.Roll($"{Math.Max(1, left)}d8-{right}"));
        }

        if (int.TryParse(text, out int plainDice))
            return Math.Max(1, DiceRoller.Roll($"{Math.Max(1, plainDice)}d8"));

        return 1;
    }

    private static int ExtractSpeedFactor(string? movement)
    {
        if (string.IsNullOrWhiteSpace(movement))
            return 0;

        var text = movement.Trim().ToLowerInvariant();
        var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int speed))
            return speed;

        return 0;
    }

    private void ResetRoundAttackCounts()
    {
        if (_app.Combat.Current is null)
            return;

        foreach (var combatant in _app.Combat.Current.Combatants)
        {
            int baseAttacks = GetAttacksForPattern(combatant.AttackPatternText, _app.Combat.Current.RoundNumber);
            int hasteBonus = GetActiveHasteBonus(combatant);
            combatant.AttacksRemainingThisRound = baseAttacks + hasteBonus;
        }
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null)
        {
            MessageBox.Show("Create an encounter first.", "No Encounter",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var name = TxtCombName.Text.Trim();
        if (string.IsNullOrEmpty(name)) { MessageBox.Show("Enter a combatant name."); return; }
        if (!int.TryParse(TxtInit.Text, out int init)) { MessageBox.Show("Initiative must be a number."); return; }
        if (!int.TryParse(TxtHp.Text,   out int hp))   { MessageBox.Show("HP must be a number."); return; }

        _app.Combat.AddCombatant(new Combatant
        {
            Name = name, Initiative = init, HpCurrent = hp, HpMax = hp
        });
        TxtCombName.Text = TxtInit.Text = TxtHp.Text = "";
        StatusBar.Text = $"{name} added (Initiative {init}, HP {hp}).";
        RefreshEnemyModifierTools();
        RefreshList();
    }

    private void BtnAddNpc_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null)
        {
            if (MessageBox.Show("No encounter active. Create a new encounter?", "No Encounter", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                string name = TxtEncName.Text.Trim();
                if (string.IsNullOrEmpty(name)) name = "Encounter";
                _app.Combat.NewEncounter(name);
                RefreshEncounterBuilders();
                BtnAddNpc_Click(sender, e);
            }
            return;
        }

        string npcName = NpcNameBox.Text.Trim();
        if (string.IsNullOrEmpty(npcName)) { MessageBox.Show("Enter an NPC name."); return; }
        if (!int.TryParse(NpcHpBox.Text, out int hp)) { MessageBox.Show("HP must be a number."); return; }
        if (!int.TryParse(NpcAcBox.Text, out int ac)) { MessageBox.Show("AC must be a number."); return; }
        if (!int.TryParse(NpcThac0Box.Text, out int thac0)) { MessageBox.Show("THAC0 must be a number."); return; }

        var npc = new Combatant
        {
            CombatantId = $"npc:{npcName}".ToLowerInvariant(),
            Kind = "NPC",
            Name = npcName,
            Initiative = 0,
            HpCurrent = hp,
            HpMax = hp,
            ArmorClass = ac,
            Thac0 = thac0,
            Thac0Text = thac0.ToString(),
            AttackPatternText = "1/round",
            AttacksRemainingThisRound = 1,
            AssignedTargetName = string.Empty,
            SpeedFactor = 0,
            DamageRollExpression = string.Empty,
        };

        _app.Combat.AddCombatant(npc);
        NpcNameBox.Text = "NPC";
        NpcHpBox.Text = "10";
        NpcAcBox.Text = "10";
        NpcThac0Box.Text = "20";
        StatusBar.Text = $"NPC '{npcName}' added (HP {hp}, AC {ac}, THAC0 {thac0}).";
        RefreshEnemyModifierTools();
        RefreshList();
    }

    private void BtnDamage_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null) return;
        int idx = CombatantList.SelectedIndex;
        if (idx < 0)
        {
            MessageBox.Show("Select a combatant in the list first.", "No Target",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!int.TryParse(TxtDmg.Text, out int dmg))
        {
            MessageBox.Show("Enter a numeric damage value."); return;
        }
        int appliedDamage = dmg;
        int enemyDamageBonus = 0;
        int enemyAttackBonus = 0;

        if (ApplyEnemyDamageBonusCheck.IsChecked == true && _enemyModCharacter is not null && !string.IsNullOrWhiteSpace(_enemyContext))
        {
            enemyDamageBonus = CombatModifierService.GetEnemyDamageBonus(_enemyModCharacter.Bonuses, _enemyContext);
            enemyAttackBonus = CombatModifierService.GetEnemyAttackBonus(_enemyModCharacter.Bonuses, _enemyContext);
            appliedDamage = Math.Max(0, dmg + enemyDamageBonus);
        }

        if (CombatantList.SelectedItem is not Combatant selectedTarget)
        {
            MessageBox.Show("Select a combatant in the list first.", "No Target",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var target = _app.Combat.Current.Combatants.FirstOrDefault(c => string.Equals(c.CombatantId, selectedTarget.CombatantId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
            return;

        _app.Combat.ApplyDamage(target, appliedDamage);
        ProcessPostDamageStateChanges(target);
        TxtDmg.Text = "";
        StatusBar.Text = enemyDamageBonus != 0
            ? $"{target.Name} took {appliedDamage} damage (base {dmg}, enemy bonus {FormatSigned(enemyDamageBonus)}). HP now {target.HpCurrent}/{target.HpMax}."
            : $"{target.Name} took {appliedDamage} damage. HP now {target.HpCurrent}/{target.HpMax}.";

        if (_enemyModCharacter is not null && !string.IsNullOrWhiteSpace(_enemyContext))
        {
            EnemyModifierInfo.Text = $"{_enemyModCharacter.Name}: attack {FormatSigned(enemyAttackBonus)}, damage {FormatSigned(enemyDamageBonus)} vs {_enemyContext}.";
        }

        RefreshList();
        CheckCombatEndConditions();
        // Reselect same row
        if (idx < CombatantList.Items.Count) CombatantList.SelectedIndex = idx;
    }

    private void BtnNextRound_Click(object sender, RoutedEventArgs e)
    {
        TryAdvanceToNextRoundWithReview(showEndRoundNotification: true);
    }

    private void BtnEndCombat_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null)
        {
            MessageBox.Show("No active encounter.", "End Combat", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"End combat encounter '{_app.Combat.Current.Name}'?\n\nThis cannot be undone.",
            "End Combat",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            EndCombatEncounter();
        }
    }

    private void EndCombatEncounter()
    {
        if (_app.Combat.Current is null)
            return;

        // Save combatant HP back to characters before clearing
        foreach (var combatant in _app.Combat.Current.Combatants.Where(c => c.IsPc))
        {
            var character = _app.Characters.FirstOrDefault(ch => string.Equals(ch.Name, combatant.SourceName, StringComparison.OrdinalIgnoreCase));
            if (character is not null)
            {
                character.CurrentHitPoints = Math.Max(0, combatant.HpCurrent);
            }
        }

        // Clear the encounter combatants and reset state
        _app.Combat.Current.Combatants.Clear();
        _activeCombatantId = string.Empty;
        _isRoundCompletePending = false;
        _pendingCompletedRound = 0;
        _isCombatStarted = false;
        ResetRoundAttackCounts();
        StatusBar.Text = "Combat ended.";
        RefreshEncounterBuilders();
        RefreshEnemyModifierTools();
        RefreshList();
    }

    private void CheckCombatEndConditions()
    {
        if (_app.Combat.Current is null || !_isCombatStarted)
            return;

        var combatants = _app.Combat.Current.Combatants;
        var aliveMonsters = combatants.Where(c => c.IsMonster && c.IsAbleToAct).ToList();
        var alivePCs = combatants.Where(c => c.IsPc && c.IsAbleToAct).ToList();

        string? outcome = null;
        if (aliveMonsters.Count == 0)
            outcome = "All monsters have been defeated!";
        else if (alivePCs.Count == 0)
            outcome = "All player characters have been defeated!";

        if (outcome is not null)
        {
            StatusBar.Text = outcome;
            var result = MessageBox.Show(
                $"{outcome}\n\nEnd combat?",
                "Combat Resolution",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                EndCombatEncounter();
            }
        }
    }

    private bool TryAdvanceToNextRoundWithReview(bool showEndRoundNotification)
    {
        if (_app.Combat.Current is null)
            return false;

        if (!_app.Combat.Current.Combatants.Any(c => c.IsAbleToAct))
        {
            _activeCombatantId = string.Empty;
            _isRoundCompletePending = false;
            _pendingCompletedRound = 0;
            UpdateRoundCompleteBanner();
            StatusBar.Text = "No conscious combatants remain.";
            RefreshList();
            return false;
        }

        int completedRound = _app.Combat.Current.RoundNumber;
        bool reviewInitiative = false;

        if (showEndRoundNotification)
        {
            var choice = MessageBox.Show(
                $"Round {completedRound} complete.\n\nChoose next action:\nYes: Review/change initiatives\nNo: Start next round now\nCancel: Stay on this round",
                "End of Round",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Information);

            if (choice == MessageBoxResult.Cancel)
            {
                _activeCombatantId = string.Empty;
                _selectedActionAttacker = null;
                _isRoundCompletePending = true;
                _pendingCompletedRound = completedRound;
                UpdateRoundCompleteBanner();
                ApplyActiveTurnMarker();
                StatusBar.Text = $"Round {completedRound} complete. Start the next round when ready.";
                RefreshList();
                return false;
            }

            reviewInitiative = choice == MessageBoxResult.Yes;
        }

        _app.Combat.AdvanceRound();
        AdvanceTimedEffects();
        ResetRoundAttackCounts();
        _isRoundCompletePending = false;
        _pendingCompletedRound = 0;
        UpdateRoundCompleteBanner();

        if (reviewInitiative)
        {
            var dialog = new RoundReviewDialog(_app.Combat.Current.RoundNumber, _app.Combat.Current.Combatants)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }

        // Always re-sort before the new round starts so updated initiatives apply.
        SortCombatantsByInitiative();

        if (_isCombatStarted)
            SetFirstActiveCombatant();

        StatusBar.Text = $"Round {_app.Combat.Current.RoundNumber} begins.";
        RefreshList();
        return true;
    }

    private void BtnRollInitiative_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null || _app.Combat.Current.Combatants.Count == 0)
        {
            MessageBox.Show("Add at least one combatant first.", "Initiative", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        foreach (var combatant in _app.Combat.Current.Combatants)
            combatant.Initiative = Random.Shared.Next(1, 11) + combatant.SpeedFactor;

        if (_isCombatStarted)
            SortCombatantsByInitiative();

        StatusBar.Text = _isCombatStarted
            ? "Initiative rolled (1d10 + Speed Factor) for all combatants. Lowest goes first. You can still edit values manually."
            : "Initiative rolled (1d10 + Speed Factor). Order will apply when combat starts.";
        RefreshList();
    }

    private void BtnStartCombat_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null || _app.Combat.Current.Combatants.Count == 0)
        {
            MessageBox.Show("Create an encounter and add combatants first.", "Start Combat", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Log combat to campaign calendar
        _app.Campaign.LogCombatEncounter(_app.Combat.Current.Name, _app.Combat.Current.Combatants.Count);

        _isCombatStarted = true;
        _isRoundCompletePending = false;
        _pendingCompletedRound = 0;
        UpdateRoundCompleteBanner();
        ProcessPostDamageStateChanges(null);
        ResetRoundAttackCounts();
        SortCombatantsByInitiative();
        SetFirstActiveCombatant();
        StatusBar.Text = string.IsNullOrWhiteSpace(_activeCombatantId)
            ? "Combat started, but no conscious combatants remain."
            : "Combat started. Active turn is highlighted.";
        RefreshList();
    }

    private void RefreshList()
    {
        CombatantList.Items.Clear();
        if (_app.Combat.Current is null)
        {
            RoundLabel.Text = "No encounter active";
            _isCombatStarted = false;
            _activeCombatantId = string.Empty;
            _isRoundCompletePending = false;
            _pendingCompletedRound = 0;
            UpdateRoundCompleteBanner();
            RefreshCombatantTargetPickers();
            return;
        }

        ProcessPostDamageStateChanges(null);
        ApplyActiveTurnMarker();
        UpdateRoundCompleteBanner();

        RoundLabel.Text = $"Round {_app.Combat.Current.RoundNumber}  —  {_app.Combat.Current.Name}";
        foreach (var c in GetOrderedCombatants())
        {
            CombatantList.Items.Add(c);
        }

        RefreshCombatantTargetPickers();
    }

    private void UpdateRoundCompleteBanner()
    {
        if (RoundCompleteBanner is null || RoundCompleteBannerText is null || RoundEndingSoonBanner is null || RoundEndingSoonBannerText is null)
            return;

        if (_isRoundCompletePending)
        {
            RoundCompleteBannerText.Text = $"Round {_pendingCompletedRound} complete. Click NEXT ROUND when ready.";
            RoundCompleteBanner.Visibility = Visibility.Visible;
            RoundEndingSoonBanner.Visibility = Visibility.Collapsed;
            return;
        }

        RoundCompleteBanner.Visibility = Visibility.Collapsed;

        if (!_isCombatStarted || _app.Combat.Current is null || string.IsNullOrWhiteSpace(_activeCombatantId))
        {
            RoundEndingSoonBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var actionable = _app.Combat.Current.Combatants
            .Where(c => c.IsAbleToAct && c.AttacksRemainingThisRound > 0)
            .ToList();

        if (actionable.Count != 1 || !string.Equals(actionable[0].CombatantId, _activeCombatantId, StringComparison.OrdinalIgnoreCase))
        {
            RoundEndingSoonBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var active = actionable[0];
        RoundEndingSoonBannerText.Text = $"Round ending soon: {active.DisplayName} has {active.AttacksRemainingThisRound} attack(s) left.";
        RoundEndingSoonBanner.Visibility = Visibility.Visible;
    }

    private void CombatantList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = CombatantList.SelectedItem as Combatant;
        if (selected is null)
            return;

        if (_isCombatStarted && !string.IsNullOrWhiteSpace(_activeCombatantId))
        {
            var active = _app.Combat.Current?.Combatants.FirstOrDefault(c => string.Equals(c.CombatantId, _activeCombatantId, StringComparison.OrdinalIgnoreCase));
            if (active is not null)
            {
                _selectedActionAttacker = active;
                CombatActionAttackerPicker.SelectedItem = active;
            }
        }
        else
        {
            _selectedActionAttacker = selected;
            CombatActionAttackerPicker.SelectedItem = selected;
        }

        if (!string.IsNullOrWhiteSpace(selected.AssignedTargetId) && _app.Combat.Current is not null)
        {
            var target = _app.Combat.Current.Combatants.FirstOrDefault(c => string.Equals(c.CombatantId, selected.AssignedTargetId, StringComparison.OrdinalIgnoreCase));
            if (target is not null)
            {
                _selectedActionTarget = target;
                CombatActionTargetPicker.SelectedItem = target;
            }
        }

        UpdateCombatActionInfo();
    }

    private void RowHit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Combatant combatant)
            return;

        _selectedActionAttacker = combatant;
        CombatActionAttackerPicker.SelectedItem = combatant;
        ResolveSelectedCombatantAttack(isHit: true);
    }

    private void RowMiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Combatant combatant)
            return;

        _selectedActionAttacker = combatant;
        CombatActionAttackerPicker.SelectedItem = combatant;
        ResolveSelectedCombatantAttack(isHit: false);
    }

    private void RowRoll_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Combatant monster)
            return;

        if (!monster.IsAbleToAct)
        {
            StatusBar.Text = $"{monster.DisplayName} cannot attack while {monster.ConditionDisplay.ToLowerInvariant()}.";
            return;
        }

        if (_app.Combat.Current is null)
            return;

        var target = _app.Combat.Current.Combatants.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(monster.AssignedTargetId)
            && string.Equals(c.CombatantId, monster.AssignedTargetId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            MessageBox.Show("Assign a target to this monster first.", "Combat", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int roll = Random.Shared.Next(1, 21);
        bool isNat20 = roll == 20;
        bool isNat1 = roll == 1;
        int effectModifier = GetActiveEffectModifier(monster);
        int effectiveThac0 = monster.Thac0 - effectModifier;
        int requiredRoll = effectiveThac0 - target.ArmorClass;
        bool isHit = isNat20 || (!isNat1 && roll >= requiredRoll);

        string natText = isNat20 ? " (natural 20!)" : isNat1 ? " (natural 1)" : string.Empty;
        string outcomeText = isHit ? "HIT" : "MISS";
        string effectModifierText = effectModifier == 0 ? string.Empty : $" (THAC0 {FormatSigned(effectModifier)} from effects)";

        if (!isHit)
        {
            // Log miss
            LogAttack(monster, target, roll, target.ArmorClass, requiredRoll, false);
            AdvanceAttackState(monster, null);
            StatusBar.Text = $"ROLL: {monster.DisplayName} rolled {roll}{natText} vs AC {target.ArmorClass} (need {requiredRoll}) — {outcomeText}. Attack advanced.{effectModifierText}";
            RefreshList();
            return;
        }

        monster.DamageRollExpression = NormalizeDamageExpression(monster.DamageRollExpression);
        if (!TryRollDamageValue(monster.DamageRollExpression, out int damage))
        {
            int manualDamage = PromptForDamage(monster, target);
            if (manualDamage < 0)
                return;
            damage = manualDamage;
        }

        int hpBefore = target.HpCurrent;
        _app.Combat.ApplyDamage(target, damage);
        int hpAfter = target.HpCurrent;
        
        // Log hit and damage
        LogAttack(monster, target, roll, target.ArmorClass, requiredRoll, true, damage);
        LogDamage(target, damage, hpBefore, hpAfter, $"from {monster.DisplayName} attack");
        
        ProcessPostDamageStateChanges(target);
        AdvanceAttackState(monster, target);
        string damageSource = string.IsNullOrWhiteSpace(monster.DamageRollExpression) ? "manual" : monster.DamageRollExpression;
        StatusBar.Text = $"ROLL: {monster.DisplayName} rolled {roll}{natText} vs AC {target.ArmorClass} (need {requiredRoll}) — {outcomeText}. Dealt {damage} ({damageSource}) to {target.DisplayName} (HP now {target.HpCurrent}/{target.HpMax}).{effectModifierText}";
        RefreshList();
        CheckCombatEndConditions();
    }

    private void RowDamageExpression_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not Combatant combatant)
            return;

        combatant.DamageRollExpression = NormalizeDamageExpression(box.Text);
        box.Text = combatant.DamageRollExpression;
    }

    private void SpellCasterPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSpellCasterFilter)
            return;

        _spellCaster = SpellCasterPicker.SelectedItem as Combatant;
        _spellcasterCharacter = ResolveCasterCharacter(_spellCaster);

        if (_selectedSpellDefinition is not null
            && TryGetScaledSpellDamageExpression(_selectedSpellDefinition, _spellcasterCharacter, out string scaledDamageExpr))
        {
            TxtSpellDamage.Text = scaledDamageExpr;
        }

        UpdateSpellActionInfo();
    }

    private void SpellTargetPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSpellTargetFilter)
            return;

        _spellTarget = SpellTargetPicker.SelectedItem as Combatant;
        UpdateSpellActionInfo();
    }

    private void SpellCasterPicker_Loaded(object sender, RoutedEventArgs e)
    {
        if (_spellCasterSearchBox is not null)
            return;

        if (SpellCasterPicker.Template.FindName("PART_EditableTextBox", SpellCasterPicker) is not TextBox tb)
            return;

        _spellCasterSearchBox = tb;
        _spellCasterSearchBox.TextChanged += SpellCasterSearchBox_TextChanged;
    }

    private void SpellTargetPicker_Loaded(object sender, RoutedEventArgs e)
    {
        if (_spellTargetSearchBox is not null)
            return;

        if (SpellTargetPicker.Template.FindName("PART_EditableTextBox", SpellTargetPicker) is not TextBox tb)
            return;

        _spellTargetSearchBox = tb;
        _spellTargetSearchBox.TextChanged += SpellTargetSearchBox_TextChanged;
    }

    private void SpellCasterSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSpellCasterFilter)
            return;

        string filter = _spellCasterSearchBox?.Text ?? string.Empty;
        string? selectedId = (_spellCaster ?? SpellCasterPicker.SelectedItem as Combatant)?.CombatantId;

        var filtered = FilterCombatantsByText(_spellCasterOptions, filter);

        _suppressSpellCasterFilter = true;
        SpellCasterPicker.ItemsSource = filtered;
        SpellCasterPicker.DisplayMemberPath = "DisplayName";
        if (!string.IsNullOrWhiteSpace(selectedId))
            SpellCasterPicker.SelectedItem = filtered.FirstOrDefault(c => string.Equals(c.CombatantId, selectedId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter))
            SpellCasterPicker.IsDropDownOpen = true;
        _suppressSpellCasterFilter = false;
    }

    private void SpellTargetSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSpellTargetFilter)
            return;

        string filter = _spellTargetSearchBox?.Text ?? string.Empty;
        string? selectedId = (_spellTarget ?? SpellTargetPicker.SelectedItem as Combatant)?.CombatantId;

        var filtered = FilterCombatantsByText(_spellTargetOptions, filter);

        _suppressSpellTargetFilter = true;
        SpellTargetPicker.ItemsSource = filtered;
        SpellTargetPicker.DisplayMemberPath = "DisplayName";
        if (!string.IsNullOrWhiteSpace(selectedId))
            SpellTargetPicker.SelectedItem = filtered.FirstOrDefault(c => string.Equals(c.CombatantId, selectedId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(filter))
            SpellTargetPicker.IsDropDownOpen = true;
        _suppressSpellTargetFilter = false;
    }

    private void BtnLoadSpells_Click(object sender, RoutedEventArgs e)
    {
        var caster = _spellCaster ?? SpellCasterPicker.SelectedItem as Combatant;
        if (caster is null)
        {
            MessageBox.Show("Select a caster first.", "Spells", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var character = ResolveCasterCharacter(caster);

        if (character is null)
        {
            MessageBox.Show($"No character found for {caster.DisplayName}.", "Spells", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool hasLegacyWizardData = character.WizardSpellLists.Count > 0 || character.WizardSpellbooks.Count > 0;
        bool hasTrackedPreparedData = character.SpellTracking?.CurrentDayTracking?.PreparedSpells?.Count > 0;

        if (!hasLegacyWizardData && !hasTrackedPreparedData)
        {
            MessageBox.Show($"{character.Name} has no spells loaded.", "Spells", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LoadSpellsFromCharacter(character);
        RefreshAvailableSpellsList();
        StatusBar.Text = $"Loaded {_availableSpells.Count} spell(s) for {character.Name} (using today's tracked spells when available).";
    }

    private void BtnQuickCastActive_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null)
        {
            MessageBox.Show("Create an encounter first.", "Quick Cast", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Combatant? active = null;
        if (!string.IsNullOrWhiteSpace(_activeCombatantId))
        {
            active = _app.Combat.Current.Combatants.FirstOrDefault(c =>
                string.Equals(c.CombatantId, _activeCombatantId, StringComparison.OrdinalIgnoreCase));
        }

        active ??= _app.Combat.Current.Combatants.FirstOrDefault(c => c.IsActiveTurn)
            ?? _selectedActionAttacker
            ?? SpellCasterPicker.SelectedItem as Combatant;

        if (active is null)
        {
            MessageBox.Show("No active combatant found.", "Quick Cast", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var character = ResolveCasterCharacter(active);
        if (character is null)
        {
            MessageBox.Show($"{active.DisplayName} is not a character with a spell list.", "Quick Cast", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _spellCaster = active;
        SpellCasterPicker.SelectedItem = active;

        Combatant? target = null;
        if (!string.IsNullOrWhiteSpace(active.AssignedTargetId))
        {
            target = _app.Combat.Current.Combatants.FirstOrDefault(c =>
                string.Equals(c.CombatantId, active.AssignedTargetId, StringComparison.OrdinalIgnoreCase));
        }

        target ??= _selectedActionTarget
            ?? CombatActionTargetPicker.SelectedItem as Combatant
            ?? SpellTargetPicker.SelectedItem as Combatant;

        // If no reliable target is set, prompt with a quick dropdown so combat can continue.
        if (target is null || !CanTarget(active, target))
            target = PromptQuickCastTargetSelection(active);

        if (target is not null)
        {
            _spellTarget = target;
            SpellTargetPicker.SelectedItem = target;
        }

        LoadSpellsFromCharacter(character);
        RefreshAvailableSpellsList();
        UpdateSpellActionInfo();

        string targetText = target is null ? "no target assigned" : $"targeting {target.DisplayName}";
        StatusBar.Text = $"Quick Cast ready: {character.Name} loaded with {_availableSpells.Count} spell(s), {targetText}.";
    }

    private void BtnCastSpellQuick_Click(object sender, RoutedEventArgs e)
    {
        _spellCastAsHealing = false;
        BtnQuickCastActive_Click(sender, e);

        if (_spellCaster is null)
            return;

        if (_availableSpells.Count == 0)
        {
            MessageBox.Show("No available spells were found for the active caster.", "Cast Spell", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        List<Combatant> BuildQuickCastTargets(bool healing)
        {
            return (_app.Combat.Current?.Combatants ?? new List<Combatant>())
                .Where(c => healing ? CanHealTarget(_spellCaster, c) : CanTarget(_spellCaster, c))
                .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var validTargets = BuildQuickCastTargets(healing: false);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Cast as {_spellCaster.DisplayName}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var healModeCheck = new CheckBox
        {
            Content = "Apply as healing (instead of damage)",
            Margin = new Thickness(0, 0, 0, 8),
            IsChecked = false
        };
        panel.Children.Add(healModeCheck);

        panel.Children.Add(new TextBlock { Text = "Spell", Margin = new Thickness(0, 0, 0, 4) });
        var allSpellNames = _availableSpells.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        var spellPicker = new ComboBox
        {
            ItemsSource = allSpellNames,
            SelectedItem = allSpellNames.FirstOrDefault(),
            IsEditable = true,
            IsTextSearchEnabled = false,
            StaysOpenOnEdit = true,
            MinWidth = 320,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(spellPicker);

        spellPicker.Loaded += (_, _) =>
        {
            if (spellPicker.Template.FindName("PART_EditableTextBox", spellPicker) is not TextBox editable)
                return;

            bool isUpdating = false;
            editable.TextChanged += (_, _) =>
            {
                if (isUpdating)
                    return;

                string filter = editable.Text ?? string.Empty;
                string? priorSelection = spellPicker.SelectedItem as string;

                var filtered = string.IsNullOrWhiteSpace(filter)
                    ? allSpellNames
                    : allSpellNames
                        .Where(s => s.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList();

                isUpdating = true;
                spellPicker.ItemsSource = filtered;
                spellPicker.IsDropDownOpen = true;

                if (!string.IsNullOrWhiteSpace(priorSelection)
                    && filtered.Any(s => string.Equals(s, priorSelection, StringComparison.OrdinalIgnoreCase)))
                {
                    spellPicker.SelectedItem = priorSelection;
                }
                else
                {
                    spellPicker.SelectedItem = filtered.FirstOrDefault();
                }

                editable.Text = filter;
                editable.CaretIndex = editable.Text.Length;
                isUpdating = false;
            };
        };

        var aoeCheck = new CheckBox
        {
            Content = "AOE mode (select multiple targets)",
            IsChecked = false,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(aoeCheck);

        panel.Children.Add(new TextBlock { Text = "Single target", Margin = new Thickness(0, 0, 0, 4) });
        var targetPicker = new ComboBox
        {
            ItemsSource = validTargets,
            DisplayMemberPath = "DisplayName",
            SelectedItem = validTargets.FirstOrDefault(c => _spellTarget is not null && string.Equals(c.CombatantId, _spellTarget.CombatantId, StringComparison.OrdinalIgnoreCase))
                           ?? validTargets.FirstOrDefault(),
            MinWidth = 320,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(targetPicker);

        healModeCheck.Checked += (_, _) =>
        {
            var items = BuildQuickCastTargets(healing: true);
            targetPicker.ItemsSource = items;
            targetPicker.SelectedItem = items.FirstOrDefault(c => _spellTarget is not null && string.Equals(c.CombatantId, _spellTarget.CombatantId, StringComparison.OrdinalIgnoreCase))
                                     ?? items.FirstOrDefault();
        };

        healModeCheck.Unchecked += (_, _) =>
        {
            var items = BuildQuickCastTargets(healing: false);
            targetPicker.ItemsSource = items;
            targetPicker.SelectedItem = items.FirstOrDefault(c => _spellTarget is not null && string.Equals(c.CombatantId, _spellTarget.CombatantId, StringComparison.OrdinalIgnoreCase))
                                     ?? items.FirstOrDefault();
        };

        panel.Children.Add(new TextBlock { Text = "Damage", Margin = new Thickness(0, 0, 0, 4) });
        var damageBox = new TextBox
        {
            Text = TxtSpellDamage.Text,
            MinWidth = 140,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(damageBox);

        panel.Children.Add(new TextBlock { Text = "Effect duration (rounds)", Margin = new Thickness(0, 0, 0, 4) });
        var roundsBox = new TextBox
        {
            Text = TxtSpellRounds.Text,
            MinWidth = 90,
            Margin = new Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(roundsBox);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var castDamageBtn = new Button { Content = "CAST DAMAGE", Width = 120, Margin = new Thickness(0, 0, 8, 0) };
        castDamageBtn.SetResourceReference(StyleProperty, "GoldButton");
        var applyEffectBtn = new Button { Content = "APPLY EFFECT", Width = 120, Margin = new Thickness(0, 0, 8, 0) };
        applyEffectBtn.SetResourceReference(StyleProperty, "GoldButton");
        var cancelBtn = new Button { Content = "CANCEL", Width = 90, IsCancel = true };
        cancelBtn.SetResourceReference(StyleProperty, "GoldButton");
        btnRow.Children.Add(castDamageBtn);
        btnRow.Children.Add(applyEffectBtn);
        btnRow.Children.Add(cancelBtn);
        panel.Children.Add(btnRow);

        var dialog = new Window
        {
            Title = "Quick Cast",
            Content = panel,
            Width = 420,
            Height = 460,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Background = (System.Windows.Media.Brush)FindResource("BrushBg")
        };

        void ApplyDialogSelections()
        {
            _spellCaster = _spellCaster ?? SpellCasterPicker.SelectedItem as Combatant;
            if (_spellCaster is not null)
                SpellCasterPicker.SelectedItem = _spellCaster;

            _spellCastAsHealing = healModeCheck.IsChecked == true;
            ChkAoeMode.IsChecked = aoeCheck.IsChecked == true;
            if (aoeCheck.IsChecked != true && targetPicker.SelectedItem is Combatant target)
            {
                _spellTarget = target;
                SpellTargetPicker.SelectedItem = target;
            }

            string spellName = (spellPicker.SelectedItem as string ?? string.Empty).Trim();
            TxtSpellName.Text = spellName;
            _selectedSpellDefinition = ResolveSpellDefinition(spellName);

            TxtSpellDamage.Text = (damageBox.Text ?? string.Empty).Trim();
            TxtSpellRounds.Text = (roundsBox.Text ?? string.Empty).Trim();
        }

        castDamageBtn.Click += (_, _) =>
        {
            ApplyDialogSelections();
            BtnCastSpellDamage_Click(sender, e);
            dialog.Close();
        };

        applyEffectBtn.Click += (_, _) =>
        {
            ApplyDialogSelections();
            BtnApplySpellEffect_Click(sender, e);
            dialog.Close();
        };

        cancelBtn.Click += (_, _) => dialog.Close();

        dialog.ShowDialog();
    }

    private Combatant? PromptQuickCastTargetSelection(Combatant caster)
    {
        if (_app.Combat.Current is null)
            return null;

        var validTargets = _app.Combat.Current.Combatants
            .Where(c => CanTarget(caster, c))
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validTargets.Count == 0)
            return null;

        if (validTargets.Count == 1)
            return validTargets[0];

        var picker = new ComboBox
        {
            MinWidth = 280,
            ItemsSource = validTargets,
            DisplayMemberPath = "DisplayName",
            SelectedIndex = 0,
            Margin = new Thickness(0, 6, 0, 10)
        };

        var okButton = new Button { Content = "OK", Width = 86, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        okButton.SetResourceReference(StyleProperty, "GoldButton");
        var cancelButton = new Button { Content = "Cancel", Width = 86, IsCancel = true };
        cancelButton.SetResourceReference(StyleProperty, "GoldButton");

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Choose target for {caster.DisplayName}:",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(picker);
        panel.Children.Add(buttonRow);

        var dialog = new Window
        {
            Title = "Quick Cast Target",
            Content = panel,
            Width = 390,
            Height = 180,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ShowInTaskbar = false,
            Background = (System.Windows.Media.Brush)FindResource("BrushBg")
        };

        okButton.Click += (_, _) => dialog.DialogResult = true;
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        bool? result = dialog.ShowDialog();
        if (result != true)
            return null;

        return picker.SelectedItem as Combatant;
    }

    private void AvailableSpellsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AvailableSpellsList.SelectedItem is not string selectedSpell)
            return;

        TxtSpellName.Text = selectedSpell;
        _selectedSpellDefinition = ResolveSpellDefinition(selectedSpell);

        if (_selectedSpellDefinition is not null
            && TryGetScaledSpellDamageExpression(_selectedSpellDefinition, _spellcasterCharacter ?? ResolveCasterCharacter(_spellCaster), out string damageExpr))
        {
            TxtSpellDamage.Text = damageExpr;
        }
    }

    private void RefreshAvailableSpellsList()
    {
        if (AvailableSpellsList is null)
            return;

        var displayList = new List<string>();
        
        // Add available spells in normal color
        foreach (var spell in _availableSpells)
            displayList.Add(spell);
        
        AvailableSpellsList.ItemsSource = displayList;
    }

    private void BtnCastSpellDamage_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null)
            return;

        var caster = _spellCaster ?? SpellCasterPicker.SelectedItem as Combatant;
        if (caster is null)
        {
            MessageBox.Show("Select a caster.", "Spell", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_isCombatStarted && !string.IsNullOrWhiteSpace(_activeCombatantId)
            && !string.Equals(caster.CombatantId, _activeCombatantId, StringComparison.OrdinalIgnoreCase))
        {
            var active = _app.Combat.Current.Combatants.FirstOrDefault(c => string.Equals(c.CombatantId, _activeCombatantId, StringComparison.OrdinalIgnoreCase));
            string activeName = active?.DisplayName ?? "active combatant";
            MessageBox.Show($"It is currently {activeName}'s turn.", "Spell", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string expr = NormalizeDamageExpression(TxtSpellDamage.Text);
        TxtSpellDamage.Text = expr;
        if (!TryRollDamageValue(expr, out int spellDamage))
        {
            MessageBox.Show("Enter spell damage as dice, range, or flat value (e.g., 6d6, 2-8, 5).", "Spell", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targets = new List<Combatant>();
        if (ChkAoeMode.IsChecked == true)
        {
            targets = SelectAoeTargets();
            if (targets.Count == 0)
                return;
        }
        else
        {
            var target = _spellTarget ?? SpellTargetPicker.SelectedItem as Combatant;
            if (target is null)
            {
                MessageBox.Show("Select a target.", "Spell", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            targets.Add(target);
        }

        string spellName = string.IsNullOrWhiteSpace(TxtSpellName.Text) ? "Spell" : TxtSpellName.Text.Trim();
        var spellDef = _selectedSpellDefinition ?? ResolveSpellDefinition(spellName);
        string saveText = spellDef?.Save ?? string.Empty;
        bool hasSave = SpellAllowsSave(saveText);
        int totalDamage = 0;
        int totalHealing = 0;
        var saveNotes = new List<string>();
        foreach (var target in targets)
        {
            if (_spellCastAsHealing)
            {
                if (!CanHealTarget(caster, target))
                    continue;

                int healHpBefore = target.HpCurrent;
                int healed = Math.Max(0, Math.Min(spellDamage, target.HpMax - target.HpCurrent));
                target.HpCurrent = Math.Min(target.HpMax, target.HpCurrent + healed);
                int healHpAfter = target.HpCurrent;

                if (healed > 0)
                {
                    LogStateChange(target, $"Healed +{healed} HP ({healHpBefore} -> {healHpAfter})");
                    totalHealing += healed;
                }

                ProcessPostDamageStateChanges(target);
                continue;
            }

            int damageToApply = spellDamage;

            if (target.IsMonster)
            {
                if (TryGetMonsterMagicResistancePercent(target, out int mrPercent) && mrPercent > 0)
                {
                    int mrRoll = Random.Shared.Next(1, 101);
                    if (mrRoll <= mrPercent)
                    {
                        damageToApply = 0;
                        saveNotes.Add($"{target.DisplayName} resisted via MR {mrPercent}% (rolled {mrRoll}).");
                    }
                }

                if (damageToApply > 0 && hasSave)
                {
                    int saveTarget = GetMonsterSaveTargetForSpell(target, saveText);
                    int saveRoll = Random.Shared.Next(1, 21);
                    bool saveSuccess = saveRoll >= saveTarget;
                    string categoryLabel = GetSaveCategoryLabel(saveText);

                    if (saveSuccess)
                    {
                        damageToApply = AdjustDamageForSuccessfulSave(spellDamage, saveText);
                        saveNotes.Add($"{target.DisplayName} save success ({saveRoll} vs {saveTarget}, {categoryLabel}) -> {damageToApply} dmg.");
                    }
                    else
                    {
                        saveNotes.Add($"{target.DisplayName} save fail ({saveRoll} vs {saveTarget}, {categoryLabel}) -> {damageToApply} dmg.");
                    }
                }
            }

            int hpBefore = target.HpCurrent;
            _app.Combat.ApplyDamage(target, damageToApply);
            int hpAfter = target.HpCurrent;
            
            // Log damage
            if (damageToApply > 0)
            {
                LogDamage(target, damageToApply, hpBefore, hpAfter, $"from {spellName} cast by {caster.DisplayName}");
            }
            
            ProcessPostDamageStateChanges(target);
            totalDamage += damageToApply;
        }

        // Log the spell cast
        LogSpellCast(caster, targets, spellName, _spellCastAsHealing ? 0 : totalDamage);
        RecordCombatSpellCastForTracker(caster, spellName);

        if (_spellCastAsHealing)
        {
            StatusBar.Text = targets.Count == 1
                ? $"{caster.DisplayName} cast {spellName} on {targets[0].DisplayName} and restored up to {spellDamage} HP ({expr}). HP now {targets[0].HpCurrent}/{targets[0].HpMax}."
                : $"{caster.DisplayName} cast {spellName} on {targets.Count} targets as healing ({expr}). Total healing: {totalHealing}.";
        }
        else
        {
            StatusBar.Text = targets.Count == 1
                ? $"{caster.DisplayName} cast {spellName} on {targets[0].DisplayName} for {spellDamage} damage ({expr}). HP now {targets[0].HpCurrent}/{targets[0].HpMax}."
                : $"{caster.DisplayName} cast {spellName} on {targets.Count} targets for {spellDamage} damage each ({expr}). Total: {totalDamage}";
        }
        if (!_isCombatStarted)
            StatusBar.Text += " (Pre-combat cast: no turn consumed.)";
        SpellActionInfo.Text = StatusBar.Text;
        
        // Track spell as cast (if loaded from character)
        if (_spellcasterCharacter is not null && _availableSpells.Count > 0)
        {
            MarkSpellAsCast(spellName);
            RefreshAvailableSpellsList();
        }

        if (saveNotes.Count > 0)
        {
            string notes = string.Join(" ", saveNotes);
            SpellActionInfo.Text = string.IsNullOrWhiteSpace(SpellActionInfo.Text)
                ? notes
                : $"{SpellActionInfo.Text} {notes}";
        }

        if (_isCombatStarted)
            ConsumeTurnAndAdvance(caster);
        
        RefreshList();
        CheckCombatEndConditions();
    }

    private void BtnApplySpellEffect_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null)
            return;

        var caster = _spellCaster ?? SpellCasterPicker.SelectedItem as Combatant;
        if (caster is null)
        {
            MessageBox.Show("Select a caster.", "Spell", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_isCombatStarted && !string.IsNullOrWhiteSpace(_activeCombatantId)
            && !string.Equals(caster.CombatantId, _activeCombatantId, StringComparison.OrdinalIgnoreCase))
        {
            var active = _app.Combat.Current.Combatants.FirstOrDefault(c => string.Equals(c.CombatantId, _activeCombatantId, StringComparison.OrdinalIgnoreCase));
            string activeName = active?.DisplayName ?? "active combatant";
            MessageBox.Show($"It is currently {activeName}'s turn.", "Spell", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string effectName = (TxtSpellName.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(effectName))
        {
            MessageBox.Show("Enter a spell/effect name (e.g., Bless, Curse).", "Spell", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int rounds = 1;
        if (int.TryParse(TxtSpellRounds.Text.Trim(), out int parsedRounds))
            rounds = Math.Max(1, parsedRounds);

        var targets = new List<Combatant>();
        if (ChkAoeMode.IsChecked == true)
        {
            targets = SelectAoeTargets();
            if (targets.Count == 0)
                return;
        }
        else
        {
            var target = _spellTarget ?? SpellTargetPicker.SelectedItem as Combatant;
            if (target is null)
            {
                MessageBox.Show("Select a target.", "Spell", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            targets.Add(target);
        }

        foreach (var target in targets)
            UpsertTimedStatus(target, effectName, rounds);

        // Log the spell effect
        LogSpellEffect(caster, targets, effectName, rounds);
        RecordCombatSpellCastForTracker(caster, effectName);

        SpellActionInfo.Text = targets.Count == 1
            ? $"{caster.DisplayName} applied {effectName} to {targets[0].DisplayName} for {rounds} round(s)."
            : $"{caster.DisplayName} applied {effectName} to {targets.Count} targets for {rounds} round(s).";
        if (!_isCombatStarted)
            SpellActionInfo.Text += " (Pre-combat cast: no turn consumed.)";
        StatusBar.Text = SpellActionInfo.Text;
        
        // Track spell as cast (if loaded from character)
        if (_spellcasterCharacter is not null && _availableSpells.Count > 0)
        {
            MarkSpellAsCast(effectName);
            RefreshAvailableSpellsList();
        }

        if (_isCombatStarted)
            ConsumeTurnAndAdvance(caster);
        
        RefreshList();
    }

    private SpellDefinition? ResolveSpellDefinition(string? nameOrId)
    {
        string token = (nameOrId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return _app.Rules.Spells.FirstOrDefault(s =>
                   string.Equals(s.Name, token, StringComparison.OrdinalIgnoreCase))
               ?? _app.Rules.Spells.FirstOrDefault(s =>
                   string.Equals(s.Id, token, StringComparison.OrdinalIgnoreCase));
    }

    private void RecordCombatSpellCastForTracker(Combatant caster, string spellName)
    {
        if (string.IsNullOrWhiteSpace(spellName))
            return;

        var character = ResolveCasterCharacter(caster);
        if (character is null)
            return;

        EnsureCombatSpellTrackingInitialized(character);
        if (character.SpellTracking is null)
            return;

        var spellDef = ResolveSpellDefinition(spellName);
        string spellId = spellDef?.Id ?? string.Empty;
        int spellLevel = ParseCombatSpellLevel(spellDef);

        character.SpellTracking.CastSpell(spellId, spellName, spellLevel, "Cast from Combat Tracker");

        // If this matches a prepared slot, mark one uncast prepared copy as spent.
        var prepared = character.SpellTracking.CurrentDayTracking.PreparedSpells;
        int preparedIndex = prepared.FindIndex(p =>
            !p.IsCast
            && (string.Equals(p.SpellId, spellId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.SpellName, spellName, StringComparison.OrdinalIgnoreCase)));
        if (preparedIndex >= 0)
            character.SpellTracking.CastPreparedSpell(preparedIndex);

        _app.SaveCharacters();
    }

    private void EnsureCombatSpellTrackingInitialized(CharacterSheet character)
    {
        if (character.SpellTracking is null)
        {
            var calendar = _app.Campaign.Calendar;
            character.SpellTracking = new CharacterSpellTracking
            {
                CharacterId = character.Name,
                CharacterName = character.Name,
                CurrentDayTracking = new DailySpellTracking
                {
                    Year = calendar.CurrentYear,
                    Era = calendar.CurrentEra,
                    Month = calendar.CurrentMonth,
                    Day = calendar.CurrentDay
                }
            };
        }

        if (character.SpellTracking.CurrentDayTracking.Year <= 0)
        {
            var calendar = _app.Campaign.Calendar;
            character.SpellTracking.CurrentDayTracking.Year = calendar.CurrentYear;
            character.SpellTracking.CurrentDayTracking.Era = calendar.CurrentEra;
            character.SpellTracking.CurrentDayTracking.Month = calendar.CurrentMonth;
            character.SpellTracking.CurrentDayTracking.Day = calendar.CurrentDay;
        }
    }

    private static int ParseCombatSpellLevel(SpellDefinition? spellDef)
    {
        if (spellDef is null)
            return 1;

        string levelText = (spellDef.Level ?? string.Empty).Trim();
        if (string.Equals(levelText, "Cantrip", StringComparison.OrdinalIgnoreCase))
            return 0;

        var digits = Regex.Replace(levelText, @"[^\d]", string.Empty);
        if (int.TryParse(digits, out int parsed))
            return Math.Max(0, parsed);

        return 1;
    }

    private CharacterSheet? ResolveCasterCharacter(Combatant? caster)
    {
        if (caster is null)
            return null;

        return _app.Characters.FirstOrDefault(c =>
                   string.Equals(c.Name, caster.SourceName, StringComparison.OrdinalIgnoreCase))
               ?? _app.Characters.FirstOrDefault(c =>
                   string.Equals(c.Name, caster.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetScaledSpellDamageExpression(SpellDefinition spell, CharacterSheet? caster, out string expression)
    {
        expression = string.Empty;
        if (!TryExtractDamageExpression(spell.Damage, out string baseExpr))
            return false;

        int casterLevel = Math.Max(1, caster?.Level ?? 1);
        if (!TryParseDiceExpression(baseExpr, out int baseCount, out int baseSides, out int baseModifier))
        {
            expression = baseExpr;
            return true;
        }

        int totalCount = baseCount;
        int totalModifier = baseModifier;

        if (TryExtractDamageExpression(spell.DamageStep, out string stepExpr)
            && TryParseDiceExpression(stepExpr, out int stepCount, out int stepSides, out int stepModifier)
            && stepSides == baseSides)
        {
            int startLevel = TryParsePositiveInt(spell.DamageScaleStartLevel, 2);
            int everyLevels = TryParsePositiveInt(spell.DamageScaleEveryLevels, 1);

            int increments = casterLevel < startLevel
                ? 0
                : 1 + ((casterLevel - startLevel) / everyLevels);

            totalCount += stepCount * Math.Max(0, increments);
            totalModifier += stepModifier * Math.Max(0, increments);
        }

        if (TryExtractDamageExpression(spell.DamageMax, out string maxExpr)
            && TryParseDiceExpression(maxExpr, out int maxCount, out int maxSides, out _)
            && maxSides == baseSides)
        {
            totalCount = Math.Min(totalCount, maxCount);
        }

        expression = FormatDiceExpression(totalCount, baseSides, totalModifier);
        return true;
    }

    private static bool TryParseDiceExpression(string expr, out int count, out int sides, out int modifier)
    {
        count = 0;
        sides = 0;
        modifier = 0;

        var match = Regex.Match((expr ?? string.Empty).Trim(), @"^(\d+)d(\d+)([+-]\d+)?$", RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups[1].Value, out count)
            || !int.TryParse(match.Groups[2].Value, out sides))
        {
            return false;
        }

        if (match.Groups[3].Success && !int.TryParse(match.Groups[3].Value, out modifier))
            modifier = 0;

        count = Math.Max(1, count);
        sides = Math.Max(2, sides);
        return true;
    }

    private static string FormatDiceExpression(int count, int sides, int modifier)
    {
        count = Math.Max(1, count);
        sides = Math.Max(2, sides);

        return modifier switch
        {
            > 0 => $"{count}d{sides}+{modifier}",
            < 0 => $"{count}d{sides}{modifier}",
            _ => $"{count}d{sides}"
        };
    }

    private static int TryParsePositiveInt(string? value, int fallback)
    {
        var match = Regex.Match((value ?? string.Empty).Trim(), @"(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int parsed) && parsed > 0)
            return parsed;

        return fallback;
    }

    private static bool TryExtractDamageExpression(string? damageText, out string expression)
    {
        expression = string.Empty;
        string text = (damageText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var dice = Regex.Match(text, @"(\d+d\d+(?:[+-]\d+)?)", RegexOptions.IgnoreCase);
        if (dice.Success)
        {
            expression = dice.Groups[1].Value.ToLowerInvariant();
            return true;
        }

        var range = Regex.Match(text, @"(\d+)\s*-\s*(\d+)");
        if (range.Success)
        {
            expression = $"{range.Groups[1].Value}-{range.Groups[2].Value}";
            return true;
        }

        var number = Regex.Match(text, @"\b(\d+)\b");
        if (number.Success)
        {
            expression = number.Groups[1].Value;
            return true;
        }

        return false;
    }

    private static bool SpellAllowsSave(string? saveText)
    {
        string text = (saveText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return !(text.Equals("none", StringComparison.OrdinalIgnoreCase)
                 || text.Equals("no", StringComparison.OrdinalIgnoreCase)
                 || text.Equals("nil", StringComparison.OrdinalIgnoreCase)
                 || text.Equals("-", StringComparison.OrdinalIgnoreCase));
    }

    private static int AdjustDamageForSuccessfulSave(int baseDamage, string? saveText)
    {
        string text = (saveText ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Contains("1/2") || text.Contains("half"))
            return Math.Max(0, baseDamage / 2);
        if (text.Contains("neg"))
            return 0;

        // Default behavior when save text is present but not explicit
        return Math.Max(0, baseDamage / 2);
    }

    private bool TryGetMonsterMagicResistancePercent(Combatant target, out int percent)
    {
        percent = 0;
        var monster = ResolveMonsterDefinition(target);
        if (monster is null)
            return false;

        var match = Regex.Match(monster.MagicResistance ?? string.Empty, @"(\d+)");
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups[1].Value, out int parsed))
            return false;

        percent = Math.Clamp(parsed, 0, 100);
        return true;
    }

    private int EstimateMonsterSaveTarget(Combatant target)
    {
        var monster = ResolveMonsterDefinition(target);
        if (monster is null)
            return 14;

        int hitDice = 1;
        string hd = (monster.HitDice ?? string.Empty).Trim().ToLowerInvariant();
        if (hd is "1/2" or "½")
            hitDice = 1;
        else
        {
            var match = Regex.Match(hd, @"(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int parsed))
                hitDice = Math.Max(1, parsed);
        }

        int targetNumber = 16 - ((hitDice - 1) / 2);
        return Math.Clamp(targetNumber, 6, 20);
    }

    private int GetMonsterSaveTargetForSpell(Combatant target, string? spellSaveText)
    {
        return _spellSaveMode switch
        {
            SpellSaveMode.ManualTarget => Math.Clamp(_manualMonsterSaveTarget, 2, 20),
            SpellSaveMode.Add2eCategory => GetAdd2eCategorySaveTarget(target, spellSaveText),
            _ => EstimateMonsterSaveTarget(target)
        };
    }

    private int GetAdd2eCategorySaveTarget(Combatant target, string? spellSaveText)
    {
        var monster = ResolveMonsterDefinition(target);
        if (monster is null)
            return 14;

        int hd = GetMonsterHitDiceForSaves(monster);
        string category = InferSaveCategory(spellSaveText);

        // AD&D 2e warrior-style save progression by effective level/HD.
        int levelBand = hd switch
        {
            <= 2 => 1,
            <= 4 => 3,
            <= 6 => 5,
            <= 8 => 7,
            <= 10 => 9,
            <= 12 => 11,
            <= 14 => 13,
            <= 16 => 15,
            _ => 17
        };

        return category switch
        {
            "poison_death" => GetWarriorSaveTarget(levelBand, 14, 13, 11, 10, 8, 7, 5, 4, 3),
            "rod_staff_wand" => GetWarriorSaveTarget(levelBand, 16, 15, 13, 12, 10, 9, 7, 6, 5),
            "petrification_polymorph" => GetWarriorSaveTarget(levelBand, 15, 14, 12, 11, 9, 8, 6, 5, 4),
            "breath_weapon" => GetWarriorSaveTarget(levelBand, 17, 16, 14, 13, 11, 10, 8, 7, 6),
            _ => GetWarriorSaveTarget(levelBand, 17, 16, 14, 13, 11, 10, 8, 7, 6),
        };
    }

    private static int GetWarriorSaveTarget(
        int levelBand,
        int l1_2,
        int l3_4,
        int l5_6,
        int l7_8,
        int l9_10,
        int l11_12,
        int l13_14,
        int l15_16,
        int l17Plus)
    {
        return levelBand switch
        {
            <= 2 => l1_2,
            <= 4 => l3_4,
            <= 6 => l5_6,
            <= 8 => l7_8,
            <= 10 => l9_10,
            <= 12 => l11_12,
            <= 14 => l13_14,
            <= 16 => l15_16,
            _ => l17Plus
        };
    }

    private static int GetMonsterHitDiceForSaves(MonsterDefinition monster)
    {
        string hdText = (monster.HitDice ?? string.Empty).Trim().ToLowerInvariant();
        if (hdText is "1/2" or "½")
            return 1;

        var match = Regex.Match(hdText, @"(\d+)");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int hd))
            return 1;

        return Math.Max(1, hd);
    }

    private static string InferSaveCategory(string? spellSaveText)
    {
        string text = (spellSaveText ?? string.Empty).Trim().ToLowerInvariant();

        if (text.Contains("poison") || text.Contains("death") || text.Contains("paraly"))
            return "poison_death";
        if (text.Contains("rod") || text.Contains("wand") || text.Contains("staff"))
            return "rod_staff_wand";
        if (text.Contains("petr") || text.Contains("poly"))
            return "petrification_polymorph";
        if (text.Contains("breath"))
            return "breath_weapon";

        return "spell";
    }

    private string GetSaveCategoryLabel(string? spellSaveText)
    {
        if (_spellSaveMode != SpellSaveMode.Add2eCategory)
            return "category: n/a";

        return InferSaveCategory(spellSaveText) switch
        {
            "poison_death" => "category: poison/death",
            "rod_staff_wand" => "category: rod/staff/wand",
            "petrification_polymorph" => "category: petrification/polymorph",
            "breath_weapon" => "category: breath weapon",
            _ => "category: spell"
        };
    }

    private MonsterDefinition? ResolveMonsterDefinition(Combatant target)
    {
        if (!target.IsMonster)
            return null;

        string combatantId = target.CombatantId ?? string.Empty;
        var idMatch = Regex.Match(combatantId, @"^monster:([^:]+):", RegexOptions.IgnoreCase);
        if (idMatch.Success)
        {
            string monsterId = idMatch.Groups[1].Value;
            var byId = _app.Rules.Monsters.FirstOrDefault(m =>
                string.Equals(m.Id, monsterId, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;
        }

        return _app.Rules.Monsters.FirstOrDefault(m =>
            string.Equals(m.Name, target.MonsterBaseName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.Name, target.SourceName, StringComparison.OrdinalIgnoreCase));
    }

    private void RowInitiative_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitRowInitiative(sender as TextBox);
    }

    private void RowInitiative_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        CommitRowInitiative(sender as TextBox);
        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void CommitRowInitiative(TextBox? box)
    {
        if (box?.Tag is not Combatant combatant)
            return;

        if (!int.TryParse((box.Text ?? string.Empty).Trim(), out int initiative))
        {
            box.Text = combatant.Initiative.ToString();
            StatusBar.Text = "Initiative must be a number.";
            return;
        }

        combatant.Initiative = initiative;
        if (_isCombatStarted)
            SortCombatantsByInitiative();

        StatusBar.Text = _isCombatStarted
            ? $"Initiative updated for {combatant.DisplayName}: {initiative}."
            : $"Initiative set for {combatant.DisplayName}: {initiative}. Order will apply when combat starts.";
        RefreshList();
        SelectCombatantInList(combatant);
    }

    private void RowTargetPicker_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox picker || picker.Tag is not Combatant attacker)
            return;

        PopulateRowTargetPicker(picker, attacker);
    }

    private void RowTargetPicker_DropDownOpened(object sender, EventArgs e)
    {
        if (sender is not ComboBox picker || picker.Tag is not Combatant attacker)
            return;

        PopulateRowTargetPicker(picker, attacker);
    }

    private void PopulateRowTargetPicker(ComboBox picker, Combatant attacker)
    {
        if (_app.Combat.Current is null)
            return;

        var validTargets = _app.Combat.Current.Combatants
            .Where(c => CanTarget(attacker, c))
            .OrderBy(c => c.Initiative)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _isUpdatingRowTargetPicker = true;
        picker.ItemsSource = validTargets;
        picker.DisplayMemberPath = "DisplayName";
        picker.SelectedItem = validTargets.FirstOrDefault(c => string.Equals(c.CombatantId, attacker.AssignedTargetId, StringComparison.OrdinalIgnoreCase));
        _isUpdatingRowTargetPicker = false;
    }

    private void RowTargetPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingRowTargetPicker)
            return;

        if (sender is not ComboBox picker || picker.Tag is not Combatant attacker || picker.SelectedItem is not Combatant target)
            return;

        if (!CanTarget(attacker, target))
        {
            StatusBar.Text = "Invalid target selection.";
            return;
        }

        attacker.AssignedTargetId = target.CombatantId;
        attacker.AssignedTargetName = target.DisplayName;

        _selectedActionAttacker = attacker;
        _selectedActionTarget = target;
        CombatActionAttackerPicker.SelectedItem = attacker;
        CombatActionTargetPicker.SelectedItem = target;

        StatusBar.Text = $"Assigned {attacker.DisplayName} to attack {target.DisplayName}.";
        UpdateCombatActionInfo();
        RefreshList();
    }

    private void RowRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not Combatant combatant)
            return;

        if (_app.Combat.RemoveCombatant(combatant.CombatantId))
        {
            StatusBar.Text = $"Removed {combatant.DisplayName} from the encounter.";
            if (_selectedActionAttacker is not null && string.Equals(_selectedActionAttacker.CombatantId, combatant.CombatantId, StringComparison.OrdinalIgnoreCase))
                _selectedActionAttacker = null;
            if (_selectedActionTarget is not null && string.Equals(_selectedActionTarget.CombatantId, combatant.CombatantId, StringComparison.OrdinalIgnoreCase))
                _selectedActionTarget = null;
            RefreshList();
        }
    }

    private void CombatantItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        if (sender is ListBoxItem item && item.DataContext is Combatant combatant)
            _dragSourceCombatant = combatant;
    }

    private void CombatantItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSourceCombatant is null)
            return;

        Point currentPosition = e.GetPosition(null);
        if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(CombatantList, _dragSourceCombatant, DragDropEffects.Move);
    }

    private void CombatantItem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(Combatant)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void CombatantItem_Drop(object sender, DragEventArgs e)
    {
        if (sender is not ListBoxItem item || item.DataContext is not Combatant target || !e.Data.GetDataPresent(typeof(Combatant)))
            return;

        var source = e.Data.GetData(typeof(Combatant)) as Combatant;
        if (source is null || string.Equals(source.CombatantId, target.CombatantId, StringComparison.OrdinalIgnoreCase))
            return;

        if (!CanTarget(source, target))
        {
            StatusBar.Text = "Invalid target assignment.";
            return;
        }

        source.AssignedTargetId = target.CombatantId;
        source.AssignedTargetName = target.DisplayName;
        _selectedActionAttacker = source;
        _selectedActionTarget = target;
        CombatActionAttackerPicker.SelectedItem = source;
        CombatActionTargetPicker.SelectedItem = target;
        StatusBar.Text = $"Assigned {source.DisplayName} to attack {target.DisplayName}.";
        UpdateCombatActionInfo();
        RefreshList();
    }

    private bool CanTarget(Combatant attacker, Combatant target)
    {
        if (string.Equals(attacker.CombatantId, target.CombatantId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!attacker.IsAbleToAct)
            return false;
        if (!target.IsAbleToAct)
            return false;
        if (attacker.IsPlayerCharacter && target.IsPlayerCharacter)
            return false;
        if (!attacker.IsPlayerCharacter && !target.IsPlayerCharacter && !attacker.IsCharmed)
            return false;
        return true;
    }

    private static bool CanHealTarget(Combatant healer, Combatant target)
    {
        if (target.IsDead)
            return false;

        // Allow self-healing.
        if (string.Equals(healer.CombatantId, target.CombatantId, StringComparison.OrdinalIgnoreCase))
            return true;

        // Healing is intended for allies.
        if (healer.IsPlayerCharacter != target.IsPlayerCharacter)
            return false;

        return true;
    }

    private void AssignTargetFromPicker()
    {
        if (_selectedActionAttacker is null || _selectedActionTarget is null)
            return;

        if (!CanTarget(_selectedActionAttacker, _selectedActionTarget))
        {
            _selectedActionTarget = null;
            CombatActionTargetPicker.SelectedItem = null;
            StatusBar.Text = "Invalid target selection.";
            return;
        }

        _selectedActionAttacker.AssignedTargetId = _selectedActionTarget.CombatantId;
        _selectedActionAttacker.AssignedTargetName = _selectedActionTarget.DisplayName;
        UpdateCombatActionInfo();
        RefreshList();
    }

    private void RefreshEnemyModifierTools()
    {
        var characters = _app.Characters
            .OrderBy(c => c.Name, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        EnemyModCharacterPicker.ItemsSource = characters;
        EnemyModCharacterPicker.DisplayMemberPath = "Name";

        if (_enemyModCharacter is not null)
        {
            _enemyModCharacter = characters.FirstOrDefault(c =>
                string.Equals(c.Name, _enemyModCharacter.Name, System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.ClassId, _enemyModCharacter.ClassId, System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.RaceId, _enemyModCharacter.RaceId, System.StringComparison.OrdinalIgnoreCase));
        }

        if (_enemyModCharacter is null && characters.Count > 0)
            _enemyModCharacter = characters[0];

        EnemyModCharacterPicker.SelectedItem = _enemyModCharacter;

        var enemyOptions = _app.Rules.Monsters
            .SelectMany(m => new[] { m.Name, m.MonsterType })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase)
            .ToList();

        EnemyContextPicker.ItemsSource = enemyOptions;
        EnemyContextPicker.Text = _enemyContext;

        var combatants = _app.Combat.Current?.Combatants ?? new List<Combatant>();
        AttackAttackerPicker.ItemsSource = combatants;
        AttackAttackerPicker.DisplayMemberPath = "Name";
        if (_attackAttacker is not null)
        {
            _attackAttacker = combatants.FirstOrDefault(c =>
                string.Equals(c.Name, _attackAttacker.Name, System.StringComparison.OrdinalIgnoreCase)
                && c.Initiative == _attackAttacker.Initiative
                && c.HpMax == _attackAttacker.HpMax);
        }
        if (_attackAttacker is null && combatants.Count > 0)
            _attackAttacker = combatants[0];
        AttackAttackerPicker.SelectedItem = _attackAttacker;

        AttackTargetCharacterPicker.ItemsSource = characters;
        AttackTargetCharacterPicker.DisplayMemberPath = "Name";
        if (_attackTargetCharacter is not null)
        {
            _attackTargetCharacter = characters.FirstOrDefault(c =>
                string.Equals(c.Name, _attackTargetCharacter.Name, System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.ClassId, _attackTargetCharacter.ClassId, System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.RaceId, _attackTargetCharacter.RaceId, System.StringComparison.OrdinalIgnoreCase));
        }
        if (_attackTargetCharacter is null && characters.Count > 0)
            _attackTargetCharacter = characters[0];
        AttackTargetCharacterPicker.SelectedItem = _attackTargetCharacter;

        if (_attackTargetCharacter is not null)
            TxtTargetAc.Text = _attackTargetCharacter.ArmorClass.ToString();

        TryAutoPopulateAttackerThac0();

        UpdateEnemyModifierInfo();
    }

    private void AttackAttackerPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _attackAttacker = AttackAttackerPicker.SelectedItem as Combatant;
        TryAutoPopulateAttackerThac0();
    }

    private void AttackTargetCharacterPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _attackTargetCharacter = AttackTargetCharacterPicker.SelectedItem as CharacterSheet;
        if (_attackTargetCharacter is not null)
            TxtTargetAc.Text = _attackTargetCharacter.ArmorClass.ToString();
    }

    private void TryAutoPopulateAttackerThac0()
    {
        if (_attackAttacker is null)
            return;

        var monster = _app.Rules.Monsters.FirstOrDefault(m =>
            string.Equals(m.Name, _attackAttacker.Name, System.StringComparison.OrdinalIgnoreCase));
        if (monster is null)
            return;

        if (int.TryParse(monster.EffectiveThac0Text, out int parsedThac0))
            TxtAttackThac0.Text = parsedThac0.ToString();
    }

    private void BtnResolveAttack_Click(object sender, RoutedEventArgs e)
    {
        var resolution = ResolveAttack();
        AttackResolverInfo.Text = resolution.Message;
    }

    private void BtnResolveAndApplyDamage_Click(object sender, RoutedEventArgs e)
    {
        var resolution = ResolveAttack();
        AttackResolverInfo.Text = resolution.Message;
        if (!resolution.IsValid)
            return;

        if (!resolution.IsHit)
        {
            StatusBar.Text = "Attack missed. No damage applied.";
            return;
        }

        if (_app.Combat.Current is null)
        {
            MessageBox.Show("Create an encounter first.", "No Encounter",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!int.TryParse(TxtDmg.Text?.Trim(), out int baseDamage))
        {
            MessageBox.Show("Enter numeric damage in Apply Damage first.", "Invalid Damage",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Combatant? targetCombatant = GetResolvedTargetCombatant();
        if (targetCombatant is null)
        {
            MessageBox.Show("Select a target combatant in the list, or add a combatant matching the target character name.",
                            "No Target Combatant", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int damageBonus = 0;
        int appliedDamage = Math.Max(0, baseDamage);
        if (ApplyEnemyDamageBonusCheck.IsChecked == true && _enemyModCharacter is not null && !string.IsNullOrWhiteSpace(_enemyContext))
        {
            damageBonus = CombatModifierService.GetEnemyDamageBonus(_enemyModCharacter.Bonuses, _enemyContext);
            appliedDamage = Math.Max(0, baseDamage + damageBonus);
        }

        _app.Combat.ApplyDamage(targetCombatant, appliedDamage);
        ProcessPostDamageStateChanges(targetCombatant);
        TxtDmg.Text = string.Empty;

        string bonusText = damageBonus != 0 ? $" (base {baseDamage}, enemy damage bonus {FormatSigned(damageBonus)})" : string.Empty;
        StatusBar.Text = $"HIT confirmed. {targetCombatant.Name} took {appliedDamage} damage{bonusText}. HP now {targetCombatant.HpCurrent}/{targetCombatant.HpMax}.";

        RefreshList();
        CheckCombatEndConditions();
        SelectCombatantInList(targetCombatant);
    }

    private AttackResolution ResolveAttack()
    {
        if (!int.TryParse(TxtAttackRoll.Text?.Trim(), out int roll))
        {
            return InvalidResolution("Roll must be a number from 1 to 20.");
        }

        if (roll < 1 || roll > 20)
        {
            return InvalidResolution("Roll must be between 1 and 20.");
        }

        if (!int.TryParse(TxtAttackThac0.Text?.Trim(), out int thac0))
        {
            return InvalidResolution("THAC0 must be numeric.");
        }

        if (!int.TryParse(TxtAttackModifier.Text?.Trim(), out int manualAttackMod))
        {
            return InvalidResolution("Attack modifier must be numeric.");
        }

        if (!int.TryParse(TxtTargetAc.Text?.Trim(), out int targetAc))
        {
            return InvalidResolution("Target AC must be numeric.");
        }

        bool useAscendingAc = AttackUseAscendingAcCheck.IsChecked == true;
        int enemyAttackBonus = 0;
        if (AttackIncludeEnemyBonusCheck.IsChecked == true)
            enemyAttackBonus = GetResolverEnemyAttackBonus();

        int effectSpellModifier = GetActiveEffectModifier(_attackAttacker);
        int totalAttackMod = manualAttackMod + enemyAttackBonus + effectSpellModifier;
        int effectiveThac0 = thac0 - totalAttackMod;
        int requiredRoll = effectiveThac0 - targetAc;
        bool isNat1 = roll == 1;
        bool isNat20 = roll == 20;
        int attackTotalAscending = roll + totalAttackMod;

        bool hit;
        if (isNat20)
            hit = true;
        else if (isNat1)
            hit = false;
        else if (useAscendingAc)
            hit = attackTotalAscending >= targetAc;
        else
            hit = roll >= requiredRoll;

        int hitAc = effectiveThac0 - roll;

        string attackerName = _attackAttacker?.Name ?? "Attacker";
        string targetName = _attackTargetCharacter?.Name ?? "Target";
        string outcome = hit ? "HIT" : "MISS";
        string natText = isNat20 ? " (natural 20)" : isNat1 ? " (natural 1)" : string.Empty;

        string enemyBonusText = enemyAttackBonus == 0 ? string.Empty : $", enemy bonus {FormatSigned(enemyAttackBonus)}";
        string effectBonusText = effectSpellModifier == 0 ? string.Empty : $", spell effects {FormatSigned(effectSpellModifier)}";
        string message = useAscendingAc
            ? $"{attackerName} -> {targetName}: {outcome}{natText}. Roll {roll}, total mod {FormatSigned(totalAttackMod)} (manual {FormatSigned(manualAttackMod)}{enemyBonusText}{effectBonusText}), attack total {attackTotalAscending} vs AC {targetAc}."
            : $"{attackerName} -> {targetName}: {outcome}{natText}. Roll {roll}, THAC0 {thac0}, total mod {FormatSigned(totalAttackMod)} (manual {FormatSigned(manualAttackMod)}{enemyBonusText}{effectBonusText}), effective THAC0 {effectiveThac0}, target AC {targetAc}, needed {requiredRoll}, hits AC {hitAc}.";

        return new AttackResolution
        {
            IsValid = true,
            IsHit = hit,
            Roll = roll,
            Thac0 = thac0,
            TargetAc = targetAc,
            ManualAttackModifier = manualAttackMod,
            EnemyAttackBonus = enemyAttackBonus,
            TotalAttackModifier = totalAttackMod,
            EffectiveThac0 = effectiveThac0,
            NeededRoll = requiredRoll,
            HitAc = hitAc,
            AscendingAttackTotal = attackTotalAscending,
            IsNat1 = isNat1,
            IsNat20 = isNat20,
            UseAscendingAc = useAscendingAc,
            Message = message
        };
    }

    private int GetResolverEnemyAttackBonus()
    {
        if (string.IsNullOrWhiteSpace(_enemyContext))
            return 0;

        CharacterSheet? source = _enemyModCharacter;
        if (_attackAttacker is not null)
        {
            source = _app.Characters.FirstOrDefault(c =>
                string.Equals(c.Name, _attackAttacker.Name, System.StringComparison.OrdinalIgnoreCase)) ?? source;
        }

        if (source is null)
            return 0;

        return CombatModifierService.GetEnemyAttackBonus(source.Bonuses, _enemyContext);
    }

    private Combatant? GetResolvedTargetCombatant()
    {
        if (_app.Combat.Current is null)
            return null;

        if (CombatantList.SelectedItem is Combatant selectedItem)
            return _app.Combat.Current.Combatants.FirstOrDefault(c => string.Equals(c.CombatantId, selectedItem.CombatantId, StringComparison.OrdinalIgnoreCase));

        if (_attackTargetCharacter is null)
            return null;

        return _app.Combat.Current.Combatants.FirstOrDefault(c =>
            string.Equals(c.Name, _attackTargetCharacter.Name, System.StringComparison.OrdinalIgnoreCase));
    }

    private void SelectCombatantInList(Combatant combatant)
    {
        for (int i = 0; i < CombatantList.Items.Count; i++)
        {
            if (CombatantList.Items[i] is Combatant candidate
                && string.Equals(candidate.CombatantId, combatant.CombatantId, StringComparison.OrdinalIgnoreCase))
            {
                CombatantList.SelectedIndex = i;
                return;
            }
        }
    }

    private void SortCombatantsByInitiative()
    {
        if (_app.Combat.Current is null)
            return;

        _app.Combat.Current.Combatants = _app.Combat.Current.Combatants
            .OrderBy(c => c.Initiative)
            .ThenBy(c => c.SpeedFactor)
            .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<Combatant> GetOrderedCombatants()
    {
        if (_app.Combat.Current is null)
            return new List<Combatant>();

        // Preserve current encounter order, but sort defeated to the bottom
        var alive = _app.Combat.Current.Combatants.Where(c => !c.IsDead && !c.IsUnconscious).ToList();
        var defeated = _app.Combat.Current.Combatants.Where(c => c.IsDead || c.IsUnconscious).ToList();
        alive.AddRange(defeated);
        return alive;
    }

    private void SetFirstActiveCombatant()
    {
        if (_app.Combat.Current is null)
        {
            _activeCombatantId = string.Empty;
            return;
        }

        var first = GetOrderedCombatants().FirstOrDefault(c => c.IsAbleToAct && c.AttacksRemainingThisRound > 0);
        _activeCombatantId = first?.CombatantId ?? string.Empty;
        _selectedActionAttacker = first;
        CombatActionAttackerPicker.SelectedItem = first;
    }

    private void ApplyActiveTurnMarker()
    {
        if (_app.Combat.Current is null)
            return;

        foreach (var combatant in _app.Combat.Current.Combatants)
            combatant.IsActiveTurn = _isCombatStarted
                && !string.IsNullOrWhiteSpace(_activeCombatantId)
                && string.Equals(combatant.CombatantId, _activeCombatantId, StringComparison.OrdinalIgnoreCase);
    }

    private void ProcessPostDamageStateChanges(Combatant? updatedTarget)
    {
        if (_app.Combat.Current is null)
            return;

        foreach (var combatant in _app.Combat.Current.Combatants)
        {
            combatant.Statuses.RemoveAll(s => string.Equals(s, "Unconscious", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Dead", StringComparison.OrdinalIgnoreCase));
            if (combatant.IsDead)
                combatant.Statuses.Add("Dead");
            else if (combatant.IsUnconscious)
                combatant.Statuses.Add("Unconscious");

            if (!combatant.IsAbleToAct)
            {
                combatant.AttacksRemainingThisRound = 0;
                combatant.AssignedTargetId = string.Empty;
                combatant.AssignedTargetName = string.Empty;
            }
        }

        var invalidTargets = _app.Combat.Current.Combatants
            .Where(c => !c.IsAbleToAct)
            .Select(c => c.CombatantId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var attacker in _app.Combat.Current.Combatants.Where(c => c.IsAbleToAct))
        {
            if (!string.IsNullOrWhiteSpace(attacker.AssignedTargetId) && invalidTargets.Contains(attacker.AssignedTargetId))
            {
                attacker.AssignedTargetId = string.Empty;
                attacker.AssignedTargetName = string.Empty;
            }
        }

        if (_selectedActionTarget is not null && !_selectedActionTarget.IsAbleToAct)
            _selectedActionTarget = null;

        if (_selectedActionAttacker is not null && !_selectedActionAttacker.IsAbleToAct)
            _selectedActionAttacker = GetOrderedCombatants().FirstOrDefault(c => c.IsAbleToAct);

        if (_isCombatStarted)
        {
            bool activeInvalid = string.IsNullOrWhiteSpace(_activeCombatantId)
                || !_app.Combat.Current.Combatants.Any(c => string.Equals(c.CombatantId, _activeCombatantId, StringComparison.OrdinalIgnoreCase) && c.IsAbleToAct);
            if (activeInvalid)
                SetFirstActiveCombatant();
        }

        if (updatedTarget is not null)
        {
            if (updatedTarget.IsDead)
                StatusBar.Text = $"{updatedTarget.DisplayName} is DEAD at {updatedTarget.HpCurrent} HP.";
            else if (updatedTarget.IsUnconscious)
                StatusBar.Text = $"{updatedTarget.DisplayName} is UNCONSCIOUS at {updatedTarget.HpCurrent} HP and can no longer attack.";
        }
    }

    private static void UpsertTimedStatus(Combatant target, string effectName, int rounds)
    {
        // Scale spell rounds to combat rounds: 1 spell round = 3 combat rounds
        int combatRounds = Math.Max(1, rounds) * 3;
        
        target.Statuses.RemoveAll(s =>
        {
            var match = Regex.Match(s ?? string.Empty, @"^(.*)\((\d+)r\)$");
            return match.Success && string.Equals(match.Groups[1].Value.Trim(), effectName.Trim(), StringComparison.OrdinalIgnoreCase);
        });

        target.Statuses.Add($"{effectName} ({combatRounds}r)");
    }

    private static int GetActiveEffectModifier(Combatant? combatant)
    {
        if (combatant is null)
            return 0;
            
        int modifier = 0;
        foreach (var status in combatant.Statuses)
        {
            var match = Regex.Match(status ?? string.Empty, @"^(.*)\((\d+)r\)$");
            if (!match.Success)
                continue;

            string effectName = match.Groups[1].Value.Trim();
            if (string.Equals(effectName, "Bless", StringComparison.OrdinalIgnoreCase))
                modifier += 1;
            else if (string.Equals(effectName, "Curse", StringComparison.OrdinalIgnoreCase))
                modifier -= 1;
        }

        return modifier;
    }

    private static int GetActiveHasteBonus(Combatant? combatant)
    {
        if (combatant is null)
            return 0;
            
        foreach (var status in combatant.Statuses)
        {
            var match = Regex.Match(status ?? string.Empty, @"^(.*)\((\d+)r\)$");
            if (!match.Success)
                continue;

            string effectName = match.Groups[1].Value.Trim();
            if (string.Equals(effectName, "Haste", StringComparison.OrdinalIgnoreCase))
                return 1; // +1 extra attack per round
        }

        return 0;
    }

    private void AdvanceTimedEffects()
    {
        if (_app.Combat.Current is null)
            return;

        foreach (var combatant in _app.Combat.Current.Combatants)
        {
            var updated = new List<string>();
            foreach (var status in combatant.Statuses)
            {
                if (string.Equals(status, "Unconscious", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, "Dead", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(status))
                        updated.Add(status);
                    continue;
                }

                var match = Regex.Match(status ?? string.Empty, @"^(.*)\((\d+)r\)$");
                if (!match.Success)
                {
                    if (!string.IsNullOrWhiteSpace(status))
                        updated.Add(status);
                    continue;
                }

                string effectName = match.Groups[1].Value.Trim();
                int rounds = int.Parse(match.Groups[2].Value);
                int nextRounds = rounds - 1;
                if (nextRounds > 0)
                    updated.Add($"{effectName} ({nextRounds}r)");
            }

            combatant.Statuses = updated;
        }
    }

    private void UpdateSpellActionInfo()
    {
        if (SpellActionInfo is null)
            return;

        string casterName = _spellCaster?.DisplayName ?? "Caster";
        string targetName = _spellTarget?.DisplayName ?? "Target";
        string aoeText = ChkAoeMode.IsChecked == true ? " (AOE mode - check target popup)" : string.Empty;
        string saveModeText = _spellSaveMode switch
        {
            SpellSaveMode.ManualTarget => $"Manual save target {_manualMonsterSaveTarget}",
            SpellSaveMode.Add2eCategory => "AD&D 2e category saves",
            _ => "HD-estimated saves"
        };
        string timingText = _isCombatStarted
            ? "In combat: casting uses the active combatant's turn."
            : "Pre-combat: any combatant can cast; effects are tracked and timers begin once rounds advance.";
        SpellActionInfo.Text = $"Spell actions: {casterName} -> {targetName}. {saveModeText}. {timingText} Use damage for direct spells, or apply timed effects.{aoeText}";
    }

    private List<Combatant> SelectAoeTargets()
    {
        if (_app.Combat.Current is null)
            return new List<Combatant>();

        var validTargets = _app.Combat.Current.Combatants
            .Where(c => c.IsAbleToAct)
            .OrderBy(c => c.Initiative)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dialog = new AoeTargetSelectorDialog(validTargets)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
            return dialog.SelectedTargets;

        return new List<Combatant>();
    }

    private static AttackResolution InvalidResolution(string message)
        => new() { IsValid = false, Message = message };

    private void EnemyModCharacterPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _enemyModCharacter = EnemyModCharacterPicker.SelectedItem as CharacterSheet;
        UpdateEnemyModifierInfo();
    }

    private void EnemyContextPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _enemyContext = (EnemyContextPicker.Text ?? string.Empty).Trim();
        UpdateEnemyModifierInfo();
    }

    private void UpdateEnemyModifierInfo()
    {
        if (_enemyModCharacter is null)
        {
            EnemyModifierInfo.Text = "Select a character to preview enemy-context bonuses.";
            return;
        }

        _enemyContext = (EnemyContextPicker.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(_enemyContext))
        {
            EnemyModifierInfo.Text = $"{_enemyModCharacter.Name}: no enemy context selected.";
            return;
        }

        int attackBonus = CombatModifierService.GetEnemyAttackBonus(_enemyModCharacter.Bonuses, _enemyContext);
        int damageBonus = CombatModifierService.GetEnemyDamageBonus(_enemyModCharacter.Bonuses, _enemyContext);
        EnemyModifierInfo.Text = $"{_enemyModCharacter.Name}: attack {FormatSigned(attackBonus)}, damage {FormatSigned(damageBonus)} vs {_enemyContext}.";
    }

    private static string FormatSigned(int value)
        => value > 0 ? $"+{value}" : value.ToString();

    // ── Combat Logging ────────────────────────────────────────────────────────

    private void AppendEncounterEvent(
        GameEventCategory category,
        string eventType,
        string summary,
        Combatant? actor = null,
        Combatant? target = null,
        Dictionary<string, string>? metadata = null,
        string details = "")
    {
        if (_app.Combat.Current is null)
            return;

        _app.Combat.Current.Timeline.Add(new GameEventEntry
        {
            RoundNumber = _app.Combat.Current.RoundNumber,
            Category = category,
            EventType = eventType,
            ActorName = actor?.DisplayName ?? string.Empty,
            TargetName = target?.DisplayName ?? string.Empty,
            Summary = summary,
            Details = details,
            Metadata = metadata ?? new Dictionary<string, string>()
        });
    }

    public void AddCustomGameEvent(string eventType, string summary, string details = "", Dictionary<string, string>? metadata = null)
    {
        AppendEncounterEvent(
            GameEventCategory.Custom,
            eventType,
            summary,
            metadata: metadata,
            details: details);
    }

    private void LogAttack(Combatant attacker, Combatant? target, int roll, int targetAc, int requiredRoll, bool isHit, int damage = 0)
    {
        if (_app.Combat.Current is null)
            return;

        var entry = new CombatLogEntry
        {
            Round = _app.Combat.Current.RoundNumber,
            ActionType = attacker.IsMonster ? CombatActionType.MonsterAttack : CombatActionType.PlayerAttack,
            ActorName = attacker.DisplayName,
            TargetName = target?.DisplayName ?? "unknown",
            RollValue = roll,
            TargetValue = targetAc,
            Success = isHit,
            Damage = damage,
            Details = $"AC {targetAc}, needed {requiredRoll}"
        };

        _app.Combat.Current.CombatLog.Add(entry);
        AppendEncounterEvent(
            GameEventCategory.Combat,
            attacker.IsMonster ? "MonsterAttack" : "PlayerAttack",
            $"{attacker.DisplayName} {(isHit ? "hit" : "missed")} {(target?.DisplayName ?? "unknown target")}",
            attacker,
            target,
            new Dictionary<string, string>
            {
                ["roll"] = roll.ToString(),
                ["targetAc"] = targetAc.ToString(),
                ["requiredRoll"] = requiredRoll.ToString(),
                ["isHit"] = isHit.ToString(),
                ["damage"] = damage.ToString()
            },
            entry.Details);
    }

    private void LogSpellCast(Combatant caster, List<Combatant> targets, string spellName, int totalDamage = 0)
    {
        if (_app.Combat.Current is null)
            return;

        var entry = new CombatLogEntry
        {
            Round = _app.Combat.Current.RoundNumber,
            ActionType = CombatActionType.SpellCast,
            ActorName = caster.DisplayName,
            TargetName = string.Join(", ", targets.Select(t => t.DisplayName)),
            Details = spellName,
            Damage = totalDamage
        };

        _app.Combat.Current.CombatLog.Add(entry);
        AppendEncounterEvent(
            GameEventCategory.Combat,
            "SpellCast",
            $"{caster.DisplayName} cast {spellName}",
            caster,
            null,
            new Dictionary<string, string>
            {
                ["targets"] = entry.TargetName,
                ["totalDamage"] = totalDamage.ToString()
            },
            spellName);
    }

    private void LogSpellEffect(Combatant caster, List<Combatant> targets, string effectName, int rounds)
    {
        if (_app.Combat.Current is null)
            return;

        var entry = new CombatLogEntry
        {
            Round = _app.Combat.Current.RoundNumber,
            ActionType = CombatActionType.SpellEffect,
            ActorName = caster.DisplayName,
            TargetName = string.Join(", ", targets.Select(t => t.DisplayName)),
            Details = $"{effectName} for {rounds} round(s)"
        };

        _app.Combat.Current.CombatLog.Add(entry);
        AppendEncounterEvent(
            GameEventCategory.Combat,
            "SpellEffect",
            $"{effectName} applied by {caster.DisplayName}",
            caster,
            null,
            new Dictionary<string, string>
            {
                ["targets"] = entry.TargetName,
                ["rounds"] = rounds.ToString()
            },
            entry.Details);
    }

    private void LogDamage(Combatant target, int damage, int hpBefore, int hpAfter, string? reason = null)
    {
        if (_app.Combat.Current is null)
            return;

        var entry = new CombatLogEntry
        {
            Round = _app.Combat.Current.RoundNumber,
            ActionType = CombatActionType.Damage,
            TargetName = target.DisplayName,
            Damage = damage,
            HpBefore = hpBefore,
            HpAfter = hpAfter,
            Details = reason ?? string.Empty
        };

        _app.Combat.Current.CombatLog.Add(entry);
        AppendEncounterEvent(
            GameEventCategory.Combat,
            "Damage",
            $"{target.DisplayName} took {damage} damage",
            null,
            target,
            new Dictionary<string, string>
            {
                ["hpBefore"] = hpBefore.ToString(),
                ["hpAfter"] = hpAfter.ToString(),
                ["damage"] = damage.ToString()
            },
            entry.Details);
    }

    private void LogStateChange(Combatant target, string newState)
    {
        if (_app.Combat.Current is null)
            return;

        var entry = new CombatLogEntry
        {
            Round = _app.Combat.Current.RoundNumber,
            ActionType = CombatActionType.StateChange,
            TargetName = target.DisplayName,
            Details = newState,
            HpAfter = target.HpCurrent
        };

        _app.Combat.Current.CombatLog.Add(entry);
        AppendEncounterEvent(
            GameEventCategory.Combat,
            "StateChange",
            $"{target.DisplayName} changed state to {newState}",
            null,
            target,
            details: newState);
    }

    private void LogInitiativeRolled()
    {
        if (_app.Combat.Current is null)
            return;

        var summary = string.Join("; ", _app.Combat.Current.Combatants
            .OrderBy(c => c.Initiative)
            .Select(c => $"{c.DisplayName} {c.Initiative}"));

        var entry = new CombatLogEntry
        {
            Round = _app.Combat.Current.RoundNumber,
            ActionType = CombatActionType.Initiative,
            Details = summary
        };

        _app.Combat.Current.CombatLog.Add(entry);
        AppendEncounterEvent(
            GameEventCategory.Combat,
            "Initiative",
            "Initiative rolled for encounter",
            metadata: new Dictionary<string, string> { ["order"] = summary },
            details: summary);
    }

    private void LogRoundStart()
    {
        if (_app.Combat.Current is null)
            return;

        var entry = new CombatLogEntry
        {
            Round = _app.Combat.Current.RoundNumber,
            ActionType = CombatActionType.RoundStart,
            Details = $"Round {_app.Combat.Current.RoundNumber} started"
        };

        _app.Combat.Current.CombatLog.Add(entry);
        AppendEncounterEvent(
            GameEventCategory.Combat,
            "RoundStart",
            $"Round {_app.Combat.Current.RoundNumber} started");
    }

    public List<string> GetCombatLog()
    {
        if (_app.Combat.Current is null)
            return new();

        return _app.Combat.Current.CombatLog
            .Select(entry => entry.ToString())
            .ToList();
    }

    public string GetCombatLogSummary()
    {
        var log = GetCombatLog();
        if (log.Count == 0)
            return "No combat actions logged yet.";

        return string.Join("\n", log.Take(100));  // Show last 100 entries
    }

        // ── Combat Log & Treasure UI ──────────────────────────────────────────

        public void ShowCombatLog()
        {
            if (_app.Combat.Current is null)
            {
                MessageBox.Show("No active combat.", "Combat Log", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new Window
            {
                Title = "Combat Log",
                Width = 700,
                Height = 500,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
            };

            var panel = new StackPanel { Margin = new Thickness(14) };
        
            var title = new TextBlock
            {
                Text = $"Combat Log - {_app.Combat.Current.Name} (Round {_app.Combat.Current.RoundNumber})",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10),
            };
            panel.Children.Add(title);

            var logText = new TextBox
            {
                Text = GetCombatLogSummary(),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 10),
                Height = 400,
            };
            panel.Children.Add(logText);

            var closeBtn = new Button
            {
                Content = "Close",
                Width = 100,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            closeBtn.Click += (_, _) => window.Close();
            panel.Children.Add(closeBtn);

            window.Content = panel;
            window.ShowDialog();
        }

        public void ShowTreasureGeneration()
        {
            if (_app.Combat.Current is null)
            {
                MessageBox.Show("No active combat.", "Treasure", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var window = new Window
            {
                Title = "Treasure Generation",
                Width = 600,
                Height = 400,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
            };

            var panel = new StackPanel { Margin = new Thickness(14) };

            var title = new TextBlock
            {
                Text = "Generate Treasure from Defeated Monsters",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10),
            };
            panel.Children.Add(title);

            var infoText = new TextBlock
            {
                Text = "Select treasure type or enter a letter (A-Z) to generate treasure from DMG tables:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            };
            panel.Children.Add(infoText);

            var inputPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var label = new TextBlock { Text = "Treasure Type: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var typeBox = new TextBox { Width = 100, Padding = new Thickness(4) };
            inputPanel.Children.Add(label);
            inputPanel.Children.Add(typeBox);
            panel.Children.Add(inputPanel);

            var treasureResultBox = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 10),
                Height = 200,
                Text = "Enter treasure type and click Generate to see results."
            };
            panel.Children.Add(treasureResultBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        
            var generateBtn = new Button { Content = "Generate", Width = 100, Margin = new Thickness(0, 0, 8, 0) };
            generateBtn.Click += (_, _) =>
            {
                string treasureType = (typeBox.Text ?? "").Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(treasureType))
                {
                    treasureResultBox.Text = "Please enter a treasure type (A-Z).";
                    return;
                }

                string treasureTable = DmgTreasureTable.GetTreasureTable(treasureType);
                treasureResultBox.Text = $"Treasure Type {treasureType}:\n\n{treasureTable}";

                AppendEncounterEvent(
                    GameEventCategory.Treasure,
                    "TreasureTypeLookup",
                    $"Treasure type {treasureType} generated",
                    metadata: new Dictionary<string, string>
                    {
                        ["treasureType"] = treasureType,
                        ["table"] = treasureTable
                    },
                    details: treasureTable);
            };
            buttonPanel.Children.Add(generateBtn);

            var rollDefeatedBtn = new Button { Content = "Roll Defeated", Width = 120, Margin = new Thickness(0, 0, 8, 0) };
            rollDefeatedBtn.Click += (_, _) =>
            {
                string report = RollTreasureFromDefeatedMonsters();
                treasureResultBox.Text = report;

                AppendEncounterEvent(
                    GameEventCategory.Treasure,
                    "TreasureRolledAfterCombat",
                    "Rolled treasure from defeated monsters",
                    details: report);
            };
            buttonPanel.Children.Add(rollDefeatedBtn);

            var editBtn = new Button { Content = "Edit Tables", Width = 100, Margin = new Thickness(0, 0, 8, 0) };
            editBtn.Click += (_, _) => ShowTreasureTableEditor();
            buttonPanel.Children.Add(editBtn);

            var awardBtn = new Button { Content = "🏆 Award To Party", Width = 130, Margin = new Thickness(0, 0, 8, 0) };
            awardBtn.Click += (_, _) =>
            {
                string treasureText = treasureResultBox.Text;
                if (string.IsNullOrWhiteSpace(treasureText) || treasureText.Contains("Enter treasure type"))
                {
                    MessageBox.Show("Generate treasure first before awarding.", "No Treasure", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                AwardTreasureToParty(treasureText);
                MessageBox.Show("Treasure awarded to party and logged to calendar.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            buttonPanel.Children.Add(awardBtn);

            var closeBtn = new Button { Content = "Close", Width = 100 };
            closeBtn.Click += (_, _) => window.Close();
            buttonPanel.Children.Add(closeBtn);

            panel.Children.Add(buttonPanel);

            window.Content = panel;
            window.ShowDialog();
        }

        private void AwardTreasureToParty(string treasureText)
        {
            string source = _app.Combat.Current?.Name ?? "Combat encounter";
            _app.Campaign.AwardTreasure("Treasure Hoard", treasureText, source);
        }

        private string RollTreasureFromDefeatedMonsters()
        {
            if (_app.Combat.Current is null)
                return "No active combat.";

            var defeatedMonsters = _app.Combat.Current.Combatants
                .Where(c => c.IsMonster && (c.IsDead || c.IsUnconscious))
                .ToList();

            if (defeatedMonsters.Count == 0)
                return "No defeated monsters found. Defeated = dead or unconscious.";

            var sb = new StringBuilder();
            sb.AppendLine("Treasure Roll From Defeated Monsters");
            sb.AppendLine();

            int processed = 0;
            foreach (var combatant in defeatedMonsters)
            {
                var monster = ResolveMonsterDefinition(combatant);
                if (monster is null)
                    continue;

                var types = ParseTreasureTypes(monster.TreasureType);
                if (types.Count == 0)
                {
                    sb.AppendLine($"{combatant.DisplayName}: no treasure type ({monster.TreasureType}).");
                    sb.AppendLine();
                    continue;
                }

                processed++;
                sb.AppendLine($"{combatant.DisplayName} ({monster.Name}) - Treasure {monster.TreasureType}");
                foreach (string type in types)
                {
                    var (summary, checks) = RollTreasureType(type);
                    sb.AppendLine($"  Type {type}: {summary}");
                    foreach (var check in checks)
                        sb.AppendLine($"    {check}");
                }
                sb.AppendLine();
            }

            if (processed == 0)
                return "Defeated monsters found, but no linked treasure table entries were resolved.";

            return sb.ToString().TrimEnd();
        }

        private static List<string> ParseTreasureTypes(string? treasureTypeText)
        {
            string text = (treasureTypeText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text)
                || text.Equals("nil", StringComparison.OrdinalIgnoreCase)
                || text.Equals("none", StringComparison.OrdinalIgnoreCase)
                || text.Equals("-", StringComparison.OrdinalIgnoreCase))
            {
                return new List<string>();
            }

            var matches = Regex.Matches(text.ToUpperInvariant(), @"\b([A-Z])\b");
            return matches
                .Select(m => m.Groups[1].Value)
                .Where(t => DmgTreasureTable.Tables.ContainsKey(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static (string Summary, List<string> Checks) RollTreasureType(string treasureType)
        {
            if (!DmgTreasureTable.Tables.TryGetValue(treasureType, out var roll))
                return ("Unknown treasure type", new List<string>());

            var checks = new List<string>();
            var awarded = new List<string>();

            TryRollComponent("CP", roll.Copper, roll.CopperChance, checks, awarded);
            TryRollComponent("SP", roll.Silver, roll.SilverChance, checks, awarded);
            TryRollComponent("EP", roll.Electrum, roll.ElectrumChance, checks, awarded);
            TryRollComponent("GP", roll.Gold, roll.GoldChance, checks, awarded);
            TryRollComponent("PP", roll.Platinum, roll.PlatinumChance, checks, awarded);
            TryRollComponent("Gems", roll.Gems, roll.GemsChance, checks, awarded);
            TryRollComponent("Art", roll.Jewelry, roll.JewelryChance, checks, awarded);
            TryRollComponent("Magic", roll.MagicItems, roll.MagicItemsChance, checks, awarded);

            string summary = awarded.Count == 0
                ? "No treasure awarded"
                : string.Join(" | ", awarded);

            return (summary, checks);
        }

        private static void TryRollComponent(string label, string amountExpr, string chanceExpr, List<string> checks, List<string> awarded)
        {
            string amount = (amountExpr ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(amount) || amount == "--")
                return;

            int? chancePercent = ParseChancePercent(chanceExpr);
            bool include;
            if (chancePercent.HasValue)
            {
                int d100 = Random.Shared.Next(1, 101);
                include = d100 <= chancePercent.Value;
                checks.Add($"{label}: d100 {d100} vs {chancePercent.Value}% => {(include ? "yes" : "no")}");
            }
            else
            {
                include = true;
                checks.Add($"{label}: auto");
            }

            if (!include)
                return;

            string rolledAmount = RollAmountExpression(amount);
            awarded.Add($"{label} {rolledAmount}");
        }

        private static int? ParseChancePercent(string? chanceExpr)
        {
            string text = (chanceExpr ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text) || text == "--")
                return null;

            var match = Regex.Match(text, @"(\d+)");
            if (!match.Success)
                return null;

            if (!int.TryParse(match.Groups[1].Value, out int chance))
                return null;

            return Math.Clamp(chance, 0, 100);
        }

        private static string RollAmountExpression(string expr)
        {
            string text = (expr ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Numeric range with optional suffix, e.g. "1,000-6,000" or "1-8 potions"
            var rangeMatch = Regex.Match(text, @"^(\d[\d,]*)\s*-\s*(\d[\d,]*)(.*)$");
            if (rangeMatch.Success
                && int.TryParse(rangeMatch.Groups[1].Value.Replace(",", string.Empty), out int min)
                && int.TryParse(rangeMatch.Groups[2].Value.Replace(",", string.Empty), out int max))
            {
                if (max < min)
                    (min, max) = (max, min);

                int rolled = Random.Shared.Next(min, max + 1);
                string suffix = (rangeMatch.Groups[3].Value ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(suffix)
                    ? rolled.ToString()
                    : $"{rolled} {suffix}";
            }

            // "Any N" style values remain deterministic labels for later item-table rolling.
            return text;
        }

        public void ShowTreasureTableEditor()
        {
            var allTables = DmgTreasureTable.GetAllTables();

            var window = new Window
            {
                Title = "Treasure Table Editor",
                Width = 1320,
                Height = 860,
                MinWidth = 1120,
                MinHeight = 680,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
            };

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var help = new TextBlock
            {
                Text = "Select a treasure type (A-Z), edit amounts and their percentile availability chances, then click Save.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(help);
            Grid.SetRow(help, 0);

            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = false,
                ItemsSource = allTables
                    .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kvp => new TreasureTableEditorRow
                    {
                        Type = kvp.Key,
                        Copper = kvp.Value.Copper,
                        CopperChance = kvp.Value.CopperChance,
                        Silver = kvp.Value.Silver,
                        SilverChance = kvp.Value.SilverChance,
                        Electrum = kvp.Value.Electrum,
                        ElectrumChance = kvp.Value.ElectrumChance,
                        Gold = kvp.Value.Gold,
                        GoldChance = kvp.Value.GoldChance,
                        Platinum = kvp.Value.Platinum,
                        PlatinumChance = kvp.Value.PlatinumChance,
                        Gems = kvp.Value.Gems,
                        GemsChance = kvp.Value.GemsChance,
                        Jewelry = kvp.Value.Jewelry,
                        JewelryChance = kvp.Value.JewelryChance,
                        MagicItems = kvp.Value.MagicItems
                        ,MagicItemsChance = kvp.Value.MagicItemsChance
                    })
                    .ToList()
            };

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Type",
                Binding = new System.Windows.Data.Binding("Type"),
                IsReadOnly = true,
                Width = new DataGridLength(70)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "CP",
                Binding = new System.Windows.Data.Binding("Copper"),
                Width = new DataGridLength(90)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "CP %",
                Binding = new System.Windows.Data.Binding("CopperChance"),
                Width = new DataGridLength(70)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "SP",
                Binding = new System.Windows.Data.Binding("Silver"),
                Width = new DataGridLength(90)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "SP %",
                Binding = new System.Windows.Data.Binding("SilverChance"),
                Width = new DataGridLength(70)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "EP",
                Binding = new System.Windows.Data.Binding("Electrum"),
                Width = new DataGridLength(90)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "EP %",
                Binding = new System.Windows.Data.Binding("ElectrumChance"),
                Width = new DataGridLength(70)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "GP",
                Binding = new System.Windows.Data.Binding("Gold"),
                Width = new DataGridLength(90)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "GP %",
                Binding = new System.Windows.Data.Binding("GoldChance"),
                Width = new DataGridLength(70)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "PP",
                Binding = new System.Windows.Data.Binding("Platinum"),
                Width = new DataGridLength(90)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "PP %",
                Binding = new System.Windows.Data.Binding("PlatinumChance"),
                Width = new DataGridLength(70)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Gems",
                Binding = new System.Windows.Data.Binding("Gems"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Gems %",
                Binding = new System.Windows.Data.Binding("GemsChance"),
                Width = new DataGridLength(70)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Jewelry",
                Binding = new System.Windows.Data.Binding("Jewelry"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Art %",
                Binding = new System.Windows.Data.Binding("JewelryChance"),
                Width = new DataGridLength(70)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Magic Items",
                Binding = new System.Windows.Data.Binding("MagicItems"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Magic %",
                Binding = new System.Windows.Data.Binding("MagicItemsChance"),
                Width = new DataGridLength(80)
            });

            root.Children.Add(grid);
            Grid.SetRow(grid, 1);

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var saveBtn = new Button { Content = "Save", Width = 110, Margin = new Thickness(0, 0, 8, 0) };
            saveBtn.Click += (_, _) =>
            {
                if (grid.ItemsSource is not List<TreasureTableEditorRow> rows)
                    return;

                int updatedCount = 0;
                foreach (var row in rows)
                {
                    if (DmgTreasureTable.TryUpdateTreasureTable(
                        row.Type,
                        new DmgTreasureRoll
                        {
                            Copper = row.Copper,
                            CopperChance = row.CopperChance,
                            Silver = row.Silver,
                            SilverChance = row.SilverChance,
                            Electrum = row.Electrum,
                            ElectrumChance = row.ElectrumChance,
                            Gold = row.Gold,
                            GoldChance = row.GoldChance,
                            Platinum = row.Platinum,
                            PlatinumChance = row.PlatinumChance,
                            Gems = row.Gems,
                            GemsChance = row.GemsChance,
                            Jewelry = row.Jewelry,
                            JewelryChance = row.JewelryChance,
                            MagicItems = row.MagicItems,
                            MagicItemsChance = row.MagicItemsChance,
                        }))
                        updatedCount++;
                }

                AppendEncounterEvent(
                    GameEventCategory.Treasure,
                    "TreasureTableEdited",
                    $"Updated {updatedCount} treasure table entr{(updatedCount == 1 ? "y" : "ies")}",
                    metadata: new Dictionary<string, string> { ["updatedCount"] = updatedCount.ToString() },
                    details: "Treasure table editor save");

                MessageBox.Show($"Saved {updatedCount} treasure table entries for this session.", "Treasure Table", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            footer.Children.Add(saveBtn);

            var closeBtn = new Button { Content = "Close", Width = 110 };
            closeBtn.Click += (_, _) => window.Close();
            footer.Children.Add(closeBtn);

            root.Children.Add(footer);
            Grid.SetRow(footer, 2);

            window.Content = root;
            window.ShowDialog();
        }

        private sealed class TreasureTableEditorRow
        {
            public string Type { get; set; } = string.Empty;
            public string Copper { get; set; } = string.Empty;
            public string CopperChance { get; set; } = string.Empty;
            public string Silver { get; set; } = string.Empty;
            public string SilverChance { get; set; } = string.Empty;
            public string Electrum { get; set; } = string.Empty;
            public string ElectrumChance { get; set; } = string.Empty;
            public string Gold { get; set; } = string.Empty;
            public string GoldChance { get; set; } = string.Empty;
            public string Platinum { get; set; } = string.Empty;
            public string PlatinumChance { get; set; } = string.Empty;
            public string Gems { get; set; } = string.Empty;
            public string GemsChance { get; set; } = string.Empty;
            public string Jewelry { get; set; } = string.Empty;
            public string JewelryChance { get; set; } = string.Empty;
            public string MagicItems { get; set; } = string.Empty;
            public string MagicItemsChance { get; set; } = string.Empty;
        }
}
