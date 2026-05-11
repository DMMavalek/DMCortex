using System.Windows;
using System.Windows.Controls;

namespace DungeonMasterCortex.Views;

public partial class DmToolsScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    public UIElement View => this;

    public DmToolsScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    public void OnEnter()
    {
        _app.SetBanner("DM Tools");
        if (_app.License.IsDemoMode)
            BtnEdit.Content = "OPEN (VIEW ONLY)";
        else
            BtnEdit.Content = "OPEN";
    }

    private void BtnHub_Click(object sender, RoutedEventArgs e)    => _app.GoTo("hub", -1);
    private void BtnCombat_Click(object sender, RoutedEventArgs e) => _app.GoTo("combat_tracker");
    private void BtnEdit_Click(object sender, RoutedEventArgs e)   => _app.GoTo("edit_info");
}
