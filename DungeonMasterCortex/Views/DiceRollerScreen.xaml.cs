using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DungeonMasterCortex.Views;

public partial class DiceRollerScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    private static readonly string[] AbilityOrder = { "str", "dex", "con", "int", "wis", "cha" };
    private const string DragPayloadType = "dmcortex-score-token";

    private readonly Dictionary<string, List<ScoreToken>> _assigned = new()
    {
        ["str"] = new(),
        ["dex"] = new(),
        ["con"] = new(),
        ["int"] = new(),
        ["wis"] = new(),
        ["cha"] = new(),
    };

    private readonly List<ScoreToken> _pool = new();
    private Point _dragStart;
    private ScoreToken? _dragToken;
    private string _dragSource = "";

    private readonly List<RollMethodOption> _methods =
    [
        new("method_i_3d6_in_order", "I: 3d6 in order", "PHB Method I. Roll 3d6 for each ability in listed order."),
        new("method_ii_3d6_twice_keep_best", "II: 3d6 twice", "PHB Method II. Roll each ability twice and keep the preferred total."),
        new("method_iii_3d6_arrange", "III: 3d6 assign", "PHB Method III. Roll six totals, then assign them where you want."),
        new("method_iv_3d6_12_choose_6", "IV: 12 rolls, keep 6", "PHB Method IV. Roll 12 totals, keep the best six, then assign."),
        new("method_v_4d6_drop_lowest", "V: 4d6 drop low", "PHB Method V. Roll 4d6, drop the lowest die, repeat six times."),
        new("method_vi_8_plus_7d6", "VI: base 8 plus 7d6", "PHB Method VI. Start at 8 in each ability, then distribute seven d6 rolls."),
        new("homebrew_4d6_reroll_1s", "HB: 4d6 reroll 1s", "Homebrew. Reroll any 1s, then keep the best 3 of 4 dice."),
        new("manual_entry", "Manual entry", "Type scores directly into the six ability boxes."),
    ];

    public DiceRollerScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
        CmbMethod.ItemsSource = _methods;
        CmbMethod.DisplayMemberPath = "Name";
        CmbMethod.SelectedValuePath = "Id";
    }

    public void OnEnter()
    {
        _app.SetBanner("Character Blueprint  ›  Dice Roller");

        if (string.IsNullOrWhiteSpace(_app.CharGen.Method))
            _app.CharGen.Method = "method_v_4d6_drop_lowest";

        CmbMethod.SelectedValue = _app.CharGen.Method;
        if (CmbMethod.SelectedIndex < 0) CmbMethod.SelectedIndex = 0;
        MethodDesc.Text = ((RollMethodOption)CmbMethod.SelectedItem).Description;
        MethodDesc.ToolTip = GetMethodTooltip(CmbMethod.SelectedValue?.ToString() ?? string.Empty);
        RbCoreRules.IsChecked = _app.CharGen.CharacterMode != "players_option";
        RbPlayersOption.IsChecked = _app.CharGen.CharacterMode == "players_option";
        UpdateManualEntryMode();

        if (_app.CharGen.Abilities.Count > 0)
            LoadAssignedFromAbilities(_app.CharGen.Abilities);
        else
            ResetAssignmentState();

        RefreshBoard();
    }

    private void BtnToolkit_Click(object sender, RoutedEventArgs e) => _app.GoTo("hub");

    private void BtnCharacters_Click(object sender, RoutedEventArgs e) => _app.GoTo("characters");

    private void CmbMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbMethod.SelectedItem is not RollMethodOption option) return;
        MethodDesc.Text = option.Description;
        MethodDesc.ToolTip = GetMethodTooltip(option.Id);
        _app.CharGen.Method = option.Id;
        UpdateManualEntryMode();
        StatusText.Text = option.Id == "manual_entry"
            ? "Manual entry selected. Type scores beside each ability, then click USE SCORES."
            : $"{DungeonMasterCortex.Services.RulesEngine.MethodDisplay(option.Id)} selected.";
    }

    private void BtnRoll_Click(object sender, RoutedEventArgs e)
    {
        if (CmbMethod.SelectedItem is not RollMethodOption option) return;
        if (option.Id == "manual_entry")
        {
            StatusText.Text = "Manual entry selected. Type scores beside each ability and click USE SCORES.";
            return;
        }

        var scores = _app.Rules.GenerateDicePool(option.Id);

        _app.CharGen.Method = option.Id;
        _app.CharGen.Abilities = new Dictionary<string, int>();

        if (IsStrictInOrder())
        {
            LoadAssignedInOrder(scores);
            RefreshBoard();
            StatusText.Text = "Method I rolled and assigned automatically in order.";
            return;
        }

        LoadPool(scores);
        RefreshBoard();
        StatusText.Text = option.Id == "method_vi_8_plus_7d6"
            ? "Rolled Method VI. Each ability starts at 8; drag all seven d6 onto abilities."
            : $"Rolled with {DungeonMasterCortex.Services.RulesEngine.MethodDisplay(option.Id)}. Drag dice to abilities.";
    }

    private void BtnUse_Click(object sender, RoutedEventArgs e)
    {
        bool isManual = CmbMethod.SelectedValue?.ToString() == "manual_entry";
        bool ok = isManual
            ? TryParseManualAbilityBoxes(out var abilities, out var error)
            : TryBuildAssignedAbilities(out abilities, out error);

        if (!ok)
        {
            MessageBox.Show(error, "Invalid Ability Scores",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CmbMethod.SelectedItem is not RollMethodOption option) return;
        _app.CharGen.CharacterMode = RbPlayersOption.IsChecked == true ? "players_option" : "core_rules";
        _app.CharGen.Method = option.Id;
        _app.CharGen.Abilities = abilities;
        _app.GoTo("chargen_race");
    }

    private void RulesetRadio_Checked(object sender, RoutedEventArgs e)
    {
        _app.CharGen.CharacterMode = RbPlayersOption.IsChecked == true ? "players_option" : "core_rules";
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        ResetAssignmentState();
        SetManualBoxTexts(new Dictionary<string, int>());
        RefreshBoard();
        StatusText.Text = "Assignments cleared.";
    }

    private void Token_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsStrictInOrder()) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not ScoreToken token) return;
        _dragStart = e.GetPosition(this);
        _dragToken = token;
        _dragSource = token.AssignedAbility ?? "pool";
    }

    private void Token_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        TryBeginDrag(e);
    }

    private void TryBeginDrag(MouseEventArgs e)
    {
        if (IsStrictInOrder()) return;
        if (e.LeftButton != MouseButtonState.Pressed || _dragToken is null || string.IsNullOrEmpty(_dragSource))
            return;

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var payload = new DragPayload(_dragToken.Id, _dragSource);
        var data = new DataObject(DragPayloadType, payload);
        DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
    }

    private void DropTarget_DragOver(object sender, DragEventArgs e)
    {
        if (IsStrictInOrder())
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = e.Data.GetDataPresent(DragPayloadType) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void AbilityDrop_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border slot || slot.Tag is not string targetAbility) return;
        if (!TryGetPayload(e, out var payload)) return;
        if (!TryGetToken(payload.TokenId, out var token)) return;

        if (payload.Source == targetAbility) return;

        if (IsMethodVi() && GetAssignedBonus(targetAbility) + token.Value > 10)
        {
            StatusText.Text = "Method VI cannot raise an ability above 18.";
            return;
        }

        RemoveFromCurrentLocation(payload.Source, token.Id);

        if (!IsMethodVi() && _assigned[targetAbility].Count > 0)
        {
            foreach (var existing in _assigned[targetAbility])
            {
                existing.AssignedAbility = null;
                _pool.Add(existing);
            }
            _assigned[targetAbility].Clear();
        }

        token.AssignedAbility = targetAbility;
        _assigned[targetAbility].Add(token);
        RefreshBoard();
        StatusText.Text = IsMethodVi()
            ? "Die assigned. Distribute all seven d6 without pushing any score above 18."
            : "Die assigned. Continue until all six abilities are filled.";
    }

    private void PoolDrop_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetPayload(e, out var payload)) return;
        if (payload.Source == "pool") return;
        if (!TryGetToken(payload.TokenId, out var token)) return;

        RemoveFromCurrentLocation(payload.Source, token.Id);
        token.AssignedAbility = null;
        if (_pool.All(t => t.Id != token.Id)) _pool.Add(token);
        RefreshBoard();
        StatusText.Text = "Die returned to pool.";
    }

    private bool TryGetPayload(DragEventArgs e, out DragPayload payload)
    {
        payload = new DragPayload(Guid.Empty, "");
        if (!e.Data.GetDataPresent(DragPayloadType)) return false;
        if (e.Data.GetData(DragPayloadType) is not DragPayload p) return false;
        payload = p;
        return true;
    }

    private bool TryGetToken(Guid tokenId, out ScoreToken token)
    {
        var found = _pool.FirstOrDefault(t => t.Id == tokenId) ??
                    _assigned.Values.SelectMany(tokens => tokens).FirstOrDefault(t => t.Id == tokenId);
        if (found is null)
        {
            token = null!;
            return false;
        }

        token = found;
        return true;
    }

    private void RemoveFromCurrentLocation(string source, Guid tokenId)
    {
        if (source == "pool")
        {
            _pool.RemoveAll(t => t.Id == tokenId);
            return;
        }

        if (_assigned.TryGetValue(source, out var tokens))
            tokens.RemoveAll(t => t.Id == tokenId);
    }

    private void LoadPool(IEnumerable<DungeonMasterCortex.Services.RulesEngine.DiceRollResult> scores)
    {
        _pool.Clear();
        foreach (var key in AbilityOrder) _assigned[key].Clear();

        foreach (var score in scores)
            _pool.Add(new ScoreToken(score.Total) { DetailText = score.DetailText });
    }

    private void LoadAssignedInOrder(IEnumerable<DungeonMasterCortex.Services.RulesEngine.DiceRollResult> scores)
    {
        _pool.Clear();
        foreach (var key in AbilityOrder) _assigned[key].Clear();

        int index = 0;
        foreach (var score in scores)
        {
            if (index >= AbilityOrder.Length) break;
            var ability = AbilityOrder[index++];
            var token = new ScoreToken(score.Total) { AssignedAbility = ability, DetailText = score.DetailText };
            _assigned[ability].Add(token);
        }
    }

    private void ResetAssignmentState()
    {
        _pool.Clear();
        foreach (var key in AbilityOrder) _assigned[key].Clear();
    }

    private void LoadAssignedFromAbilities(Dictionary<string, int> abilities)
    {
        _pool.Clear();
        foreach (var key in AbilityOrder)
        {
            _assigned[key].Clear();
            var value = abilities.GetValueOrDefault(key, 10);
            var token = new ScoreToken(value) { AssignedAbility = key, DetailText = $"{key.ToUpperInvariant()}: {value}" };
            _assigned[key].Add(token);
        }

        SetManualBoxTexts(abilities);
    }

    private void RefreshBoard()
    {
        PoolItems.ItemsSource = null;
        PoolItems.ItemsSource = _pool.OrderByDescending(t => t.Value).ToList();

        SetSlotVisual("str", ItemsStr, ValStr, SlotStr);
        SetSlotVisual("dex", ItemsDex, ValDex, SlotDex);
        SetSlotVisual("con", ItemsCon, ValCon, SlotCon);
        SetSlotVisual("int", ItemsInt, ValInt, SlotInt);
        SetSlotVisual("wis", ItemsWis, ValWis, SlotWis);
        SetSlotVisual("cha", ItemsCha, ValCha, SlotCha);
        SyncManualBoxesFromAssigned();
    }

    private void SetSlotVisual(string ability, ItemsControl itemsControl, TextBlock valueText, Border slot)
    {
        bool isManual = CmbMethod.SelectedValue?.ToString() == "manual_entry";
        var tokens = _assigned[ability];
        itemsControl.ItemsSource = null;
        itemsControl.ItemsSource = tokens.ToList();

        if (tokens.Count == 0)
        {
            itemsControl.Visibility = Visibility.Collapsed;
            valueText.Text       = isManual ? "" : "Drop die";
            valueText.Visibility = isManual ? Visibility.Collapsed : Visibility.Visible;
            valueText.Margin     = new Thickness(8, 0, 0, 0);
            valueText.Foreground = (Brush)FindResource("BrushDim");
            slot.Background      = (Brush)FindResource("BrushInputBg");
            return;
        }

        itemsControl.Visibility = Visibility.Visible;
        valueText.Visibility    = Visibility.Visible;
        valueText.Margin        = new Thickness(8, 0, 0, 0);
        valueText.Text = IsMethodVi()
            ? $"Total  {8 + tokens.Sum(t => t.Value)}"
            : $"Total  {tokens.Sum(t => t.Value)}";
        valueText.Foreground = (Brush)FindResource("BrushTitle");
        slot.Background      = (Brush)FindResource("BrushBtn");
    }

    private bool TryBuildAssignedAbilities(out Dictionary<string, int> abilities, out string error)
    {
        abilities = new Dictionary<string, int>();
        error = "";

        foreach (var ability in AbilityOrder)
        {
            if (IsMethodVi())
            {
                abilities[ability] = 8 + _assigned[ability].Sum(t => t.Value);
                continue;
            }

            if (_assigned[ability].Count != 1)
            {
                error = "All six abilities must have an assigned die before continuing.";
                return false;
            }

            abilities[ability] = _assigned[ability][0].Value;
        }

        if (IsMethodVi() && _pool.Count > 0)
        {
            error = "Assign all seven d6 before continuing with Method VI.";
            return false;
        }

        return true;
    }

    private static bool TryParseManualScores(string input, out List<int> scores, out string error)
    {
        scores = new List<int>();
        error = "";

        var tokens = input.Split(new[] { ',', ' ', ';', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length != 6)
        {
            error = "Enter exactly 6 scores (example: 16, 14, 13, 12, 11, 9).";
            return false;
        }

        foreach (var token in tokens)
        {
            if (!int.TryParse(token, out int value))
            {
                error = $"'{token}' is not a valid number.";
                return false;
            }

            if (value < 3 || value > 18)
            {
                error = "Each score must be between 3 and 18.";
                return false;
            }

            scores.Add(value);
        }

        return true;
    }

    private bool TryParseManualAbilityBoxes(out Dictionary<string, int> abilities, out string error)
    {
        abilities = new Dictionary<string, int>();
        error = "";

        if (!TryParseAbilityBox(TxtStrManual.Text, "Strength", out int str, out error)) return false;
        if (!TryParseAbilityBox(TxtDexManual.Text, "Dexterity", out int dex, out error)) return false;
        if (!TryParseAbilityBox(TxtConManual.Text, "Constitution", out int con, out error)) return false;
        if (!TryParseAbilityBox(TxtIntManual.Text, "Intelligence", out int intel, out error)) return false;
        if (!TryParseAbilityBox(TxtWisManual.Text, "Wisdom", out int wis, out error)) return false;
        if (!TryParseAbilityBox(TxtChaManual.Text, "Charisma", out int cha, out error)) return false;

        abilities["str"] = str;
        abilities["dex"] = dex;
        abilities["con"] = con;
        abilities["int"] = intel;
        abilities["wis"] = wis;
        abilities["cha"] = cha;
        return true;
    }

    private static bool TryParseAbilityBox(string text, string label, out int value, out string error)
    {
        error = "";
        if (!int.TryParse(text.Trim(), out value))
        {
            error = $"{label} must be a number.";
            return false;
        }

        if (value < 3 || value > 18)
        {
            error = $"{label} must be between 3 and 18.";
            return false;
        }

        return true;
    }

    private void SyncManualBoxesFromAssigned()
    {
        if (CmbMethod.SelectedValue?.ToString() == "manual_entry") return;

        TxtStrManual.Text = GetDisplayedAssignedValue("str");
        TxtDexManual.Text = GetDisplayedAssignedValue("dex");
        TxtConManual.Text = GetDisplayedAssignedValue("con");
        TxtIntManual.Text = GetDisplayedAssignedValue("int");
        TxtWisManual.Text = GetDisplayedAssignedValue("wis");
        TxtChaManual.Text = GetDisplayedAssignedValue("cha");
    }

    private void SetManualBoxTexts(Dictionary<string, int> abilities)
    {
        TxtStrManual.Text = abilities.TryGetValue("str", out int str) ? str.ToString(CultureInfo.InvariantCulture) : "";
        TxtDexManual.Text = abilities.TryGetValue("dex", out int dex) ? dex.ToString(CultureInfo.InvariantCulture) : "";
        TxtConManual.Text = abilities.TryGetValue("con", out int con) ? con.ToString(CultureInfo.InvariantCulture) : "";
        TxtIntManual.Text = abilities.TryGetValue("int", out int intel) ? intel.ToString(CultureInfo.InvariantCulture) : "";
        TxtWisManual.Text = abilities.TryGetValue("wis", out int wis) ? wis.ToString(CultureInfo.InvariantCulture) : "";
        TxtChaManual.Text = abilities.TryGetValue("cha", out int cha) ? cha.ToString(CultureInfo.InvariantCulture) : "";
    }

    private void UpdateManualEntryMode()
    {
        bool isManual = CmbMethod.SelectedValue?.ToString() == "manual_entry";
        var manualVisibility = isManual ? Visibility.Visible : Visibility.Collapsed;
        var slotVisibility = isManual ? Visibility.Collapsed : Visibility.Visible;

        SlotStr.Visibility = slotVisibility;
        SlotDex.Visibility = slotVisibility;
        SlotCon.Visibility = slotVisibility;
        SlotInt.Visibility = slotVisibility;
        SlotWis.Visibility = slotVisibility;
        SlotCha.Visibility = slotVisibility;

        TxtStrManual.Visibility = manualVisibility;
        TxtDexManual.Visibility = manualVisibility;
        TxtConManual.Visibility = manualVisibility;
        TxtIntManual.Visibility = manualVisibility;
        TxtWisManual.Visibility = manualVisibility;
        TxtChaManual.Visibility = manualVisibility;

        TxtStrManual.IsReadOnly = !isManual;
        TxtDexManual.IsReadOnly = !isManual;
        TxtConManual.IsReadOnly = !isManual;
        TxtIntManual.IsReadOnly = !isManual;
        TxtWisManual.IsReadOnly = !isManual;
        TxtChaManual.IsReadOnly = !isManual;

        SlotColumn.Width = isManual
            ? new GridLength(0)
            : new GridLength(136);

        ManualEntryColumn.Width = isManual
            ? new GridLength(92)
            : new GridLength(0);

        RefreshBoard();
    }

    private bool IsMethodVi() => CmbMethod.SelectedValue?.ToString() == "method_vi_8_plus_7d6";

    private bool IsStrictInOrder() => CmbMethod.SelectedValue?.ToString() == "method_i_3d6_in_order";

    private static string GetMethodTooltip(string methodId)
    {
        return methodId switch
        {
            "method_v_4d6_drop_lowest" =>
                "Method V uses 4d6, drops the lowest die, and keeps the best 3. An 18 is rare by design: 1.62% per roll, or about 8.7% chance of seeing at least one 18 across six abilities.",
            "homebrew_4d6_reroll_1s" =>
                "This homebrew rerolls any 1s before dropping the lowest die, which raises the average score a little compared with standard 4d6 drop-lowest.",
            "method_vi_8_plus_7d6" =>
                "Method VI starts every ability at 8 and then distributes seven d6 rolls. It is not a standard 4d6-drop-lowest method.",
            _ =>
                "Hover here for method guidance. The 4d6-drop-lowest method is the standard 'roll 4d6, drop the lowest' approach, where high scores are uncommon but possible.",
        };
    }

    private int GetAssignedBonus(string ability) => _assigned[ability].Sum(t => t.Value);

    private string GetDisplayedAssignedValue(string ability)
    {
        if (_assigned[ability].Count == 0) return "";
        int total = _assigned[ability].Sum(t => t.Value);
        return IsMethodVi()
            ? (8 + total).ToString(CultureInfo.InvariantCulture)
            : total.ToString(CultureInfo.InvariantCulture);
    }

    private sealed record RollMethodOption(string Id, string Name, string Description);
    private sealed record DragPayload(Guid TokenId, string Source);

    private sealed class ScoreToken
    {
        public Guid Id { get; }
        public int Value { get; }
        public string? AssignedAbility { get; set; }
        public string DetailText { get; set; } = string.Empty;

        public ScoreToken(int value)
        {
            Id = Guid.NewGuid();
            Value = value;
        }
    }
}
