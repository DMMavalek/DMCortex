using System.Windows;
using System.Windows.Controls;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class HubScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    public HubScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        bool playerEdition = _app.License.Edition == AppEdition.Player;

        _app.SetBanner(playerEdition ? "Player Codex" : "Dungeon Master Codex");
        TxtHubTitle.Text = playerEdition ? "PLAYER CODEX" : "DUNGEON MASTER CODEX";
        TxtActivationStatus.Text = _app.License.ActivationStatusLabel;
        TxtAppVersion.Text = $"Version {AppUpdateService.GetCurrentDisplayVersion()}";

        CardSessions.Visibility = Visibility.Collapsed;
        CardDmTools.Visibility = Visibility.Visible;
        CardCampaign.Visibility = playerEdition ? Visibility.Collapsed : Visibility.Visible;
        CardCampaignTracker.Visibility = playerEdition ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnCharGen_Click(object sender, RoutedEventArgs e) => _app.GoTo("characters");
    private void BtnSessions_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Session Manager is hidden in this release.",
            "Feature Hidden",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
    private void BtnDmTools_Click(object sender, RoutedEventArgs e) => _app.GoTo("dm_tools");
    private void BtnCampaign_Click(object sender, RoutedEventArgs e) => _app.GoTo("campaign");
    private void BtnOptions_Click(object sender, RoutedEventArgs e) => _app.GoTo("options");
}
