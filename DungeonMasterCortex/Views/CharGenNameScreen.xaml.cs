using System.Windows;
using System.Windows.Controls;

namespace DungeonMasterCortex.Views;

public partial class CharGenNameScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    public CharGenNameScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.SetBanner("Character Blueprint  ›  Name");
        bool isPO_ = _app.CharGen.CharacterMode == "players_option";
        _app.SetNavBar(1, isPO_ ? 7 : 6, "Name",
            backAction: () => _app.GoTo("dice_roller", -1),
            nextAction: Advance);
        TxtName.Text = _app.CharGen.Name;
        MethodSummary.Text = $"Rolling Method: {DungeonMasterCortex.Services.RulesEngine.MethodDisplay(_app.CharGen.Method)}";
    }

    private void Advance()
    {
        var name = TxtName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Please enter a character name.", "Name Required",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _app.CharGen.Name   = name;
        if (_app.CharGen.Abilities.Count == 0)
            _app.CharGen.Abilities = _app.Rules.GenerateAbilities(_app.CharGen.Method);
        _app.GoTo("chargen_abilities");
    }
}
