using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DungeonMasterCortex.Views;

public partial class CharGenAbilitiesScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    private static readonly string[] AbilityOrder  = { "str", "dex", "con", "int", "wis", "cha" };
    private static readonly string[] AbilityAbbrev = { "STR", "DEX", "CON", "INT", "WIS", "CHA" };
    private static readonly string[] AbilityLabel  =
        { "Strength","Dexterity","Constitution","Intelligence","Wisdom","Charisma" };

    public CharGenAbilitiesScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.SetBanner("Character Generator  ›  Ability Scores");
        bool isPO   = _app.CharGen.CharacterMode == "players_option";
        int  total  = isPO ? 7 : 6;
        _app.SetNavBar(2, total, "Ability Scores",
            backAction: () => _app.GoTo("chargen_name"),
            nextAction: () => _app.GoTo("chargen_race"));

        BtnReroll.IsEnabled = DungeonMasterCortex.Services.RulesEngine.CanAutoRoll(_app.CharGen.Method);
        MethodLabel.Text = $"Method: {DungeonMasterCortex.Services.RulesEngine.MethodDisplay(_app.CharGen.Method)}";

        RefreshScores();
    }

    private void BtnReroll_Click(object sender, RoutedEventArgs e)
    {
        _app.CharGen.Abilities = _app.Rules.GenerateAbilities(_app.CharGen.Method);
        RefreshScores();
    }

    private void RefreshScores()
    {
        var items = new List<AbilityViewModel>();
        for (int i = 0; i < AbilityOrder.Length; i++)
        {
            int score = _app.CharGen.Abilities.GetValueOrDefault(AbilityOrder[i], 0);
            var color = score >= 15 ? "#E8C050"
                      : score >= 9  ? "#C4A468"
                      : "#C02828";
            items.Add(new AbilityViewModel
            {
                Score      = score.ToString(),
                Abbrev     = AbilityAbbrev[i],
                Label      = AbilityLabel[i],
                ScoreColor = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(color)),
            });
        }
        AbilityItems.ItemsSource = items;
    }
}

public class AbilityViewModel
{
    public string Score      { get; set; } = "";
    public string Abbrev     { get; set; } = "";
    public string Label      { get; set; } = "";
    public Brush  ScoreColor { get; set; } = Brushes.White;
}
