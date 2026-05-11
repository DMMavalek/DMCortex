using System.Windows;
using System.Windows.Controls;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class CombatTrackerScreen : UserControl, IScreen
{
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
    public UIElement View => this;

    public CombatTrackerScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.SetBanner("DM Tools  ›  Combat Tracker");
        RefreshEnemyModifierTools();
        RefreshList();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) =>
        _app.GoTo("dm_tools", -1);

    private void BtnNewEncounter_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtEncName.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = "Encounter";
        _app.Combat.NewEncounter(name);
        StatusBar.Text = $"Encounter '{name}' started.";
        RefreshEnemyModifierTools();
        RefreshList();
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

        var target = _app.Combat.Current.Combatants[idx];
        _app.Combat.ApplyDamage(target, appliedDamage);
        TxtDmg.Text = "";
        StatusBar.Text = enemyDamageBonus != 0
            ? $"{target.Name} took {appliedDamage} damage (base {dmg}, enemy bonus {FormatSigned(enemyDamageBonus)}). HP now {target.HpCurrent}/{target.HpMax}."
            : $"{target.Name} took {appliedDamage} damage. HP now {target.HpCurrent}/{target.HpMax}.";

        if (_enemyModCharacter is not null && !string.IsNullOrWhiteSpace(_enemyContext))
        {
            EnemyModifierInfo.Text = $"{_enemyModCharacter.Name}: attack {FormatSigned(enemyAttackBonus)}, damage {FormatSigned(enemyDamageBonus)} vs {_enemyContext}.";
        }

        RefreshList();
        // Reselect same row
        if (idx < CombatantList.Items.Count) CombatantList.SelectedIndex = idx;
    }

    private void BtnNextRound_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Combat.Current is null) return;
        _app.Combat.AdvanceRound();
        StatusBar.Text = $"Round {_app.Combat.Current.RoundNumber} begins.";
        RefreshList();
    }

    private void RefreshList()
    {
        CombatantList.Items.Clear();
        if (_app.Combat.Current is null) { RoundLabel.Text = "No encounter active"; return; }
        RoundLabel.Text = $"Round {_app.Combat.Current.RoundNumber}  —  {_app.Combat.Current.Name}";
        foreach (var c in _app.Combat.Current.Combatants)
            CombatantList.Items.Add(c.Display);
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
        TxtDmg.Text = string.Empty;

        string bonusText = damageBonus != 0 ? $" (base {baseDamage}, enemy damage bonus {FormatSigned(damageBonus)})" : string.Empty;
        StatusBar.Text = $"HIT confirmed. {targetCombatant.Name} took {appliedDamage} damage{bonusText}. HP now {targetCombatant.HpCurrent}/{targetCombatant.HpMax}.";

        RefreshList();
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

        int totalAttackMod = manualAttackMod + enemyAttackBonus;
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
        string message = useAscendingAc
            ? $"{attackerName} -> {targetName}: {outcome}{natText}. Roll {roll}, total mod {FormatSigned(totalAttackMod)} (manual {FormatSigned(manualAttackMod)}{enemyBonusText}), attack total {attackTotalAscending} vs AC {targetAc}."
            : $"{attackerName} -> {targetName}: {outcome}{natText}. Roll {roll}, THAC0 {thac0}, total mod {FormatSigned(totalAttackMod)} (manual {FormatSigned(manualAttackMod)}{enemyBonusText}), effective THAC0 {effectiveThac0}, target AC {targetAc}, needed {requiredRoll}, hits AC {hitAc}.";

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

        int selectedIndex = CombatantList.SelectedIndex;
        if (selectedIndex >= 0 && selectedIndex < _app.Combat.Current.Combatants.Count)
            return _app.Combat.Current.Combatants[selectedIndex];

        if (_attackTargetCharacter is null)
            return null;

        return _app.Combat.Current.Combatants.FirstOrDefault(c =>
            string.Equals(c.Name, _attackTargetCharacter.Name, System.StringComparison.OrdinalIgnoreCase));
    }

    private void SelectCombatantInList(Combatant combatant)
    {
        if (_app.Combat.Current is null)
            return;

        int idx = _app.Combat.Current.Combatants.FindIndex(c => ReferenceEquals(c, combatant));
        if (idx >= 0 && idx < CombatantList.Items.Count)
            CombatantList.SelectedIndex = idx;
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
}
