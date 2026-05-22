using System;
using System.Windows;
using System.Windows.Controls;

namespace DungeonMasterCortex.Views;

public partial class OptionsScreen : UserControl, IScreen
{
    public void OnEnter()
    {
        _app.SetBanner("Options");
        FullScreenCheckBox.IsChecked = _app.WindowState == WindowState.Maximized;
        AutoResizeCheckBox.IsChecked = _app.AutoResizeWindow;
        StartupUpdateCheckBox.IsChecked = _app.CheckForUpdatesOnStartup;

        string label = _app.FontSize switch
        {
            <= 12.5 => "Small",
            <= 15.5 => "Normal",
            <= 19.5 => "Large",
            _ => "Extra Large"
        };

        foreach (var item in TextSizeComboBox.Items)
        {
            if (item is ComboBoxItem combo && string.Equals(combo.Content?.ToString(), label, StringComparison.OrdinalIgnoreCase))
            {
                TextSizeComboBox.SelectedItem = combo;
                break;
            }
        }

        RefreshActivationUi();
    }

    private void RefreshActivationUi()
    {
        ActivationStatusText.Text = _app.License.ActivationStatusLabel;
        ActivationHelpText.Text = _app.License.ActivationHelpLabel;
    }

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await App.CheckForUpdatesAsync(Window.GetWindow(this));
    }

    private void BtnRequestActivation_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var dialog = new ActivationRequestDialog(_app.License)
        {
            Owner = owner,
        };

        dialog.ShowDialog();
    }

    private void BtnApplyActivationCode_Click(object sender, RoutedEventArgs e)
    {
        string code = ActivationCodeTextBox.Text?.Trim() ?? string.Empty;
        if (!_app.License.TryActivate(code, out string message))
        {
            MessageBox.Show(message, "Activation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshActivationUi();
        ActivationCodeTextBox.Clear();
        MessageBox.Show(message, "Activation", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private readonly MainWindow _app;
    public UIElement View => this;

    public OptionsScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        // Full screen toggle
        if (FullScreenCheckBox.IsChecked == true)
            _app.WindowState = WindowState.Maximized;
        else
            _app.WindowState = WindowState.Normal;

        // Text size
        var selected = (TextSizeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        double fontSize = selected switch
        {
            "Small" => 12,
            "Normal" => 14,
            "Large" => 18,
            "Extra Large" => 22,
            _ => 14
        };
        _app.AutoResizeWindow = AutoResizeCheckBox.IsChecked == true;
        _app.CheckForUpdatesOnStartup = StartupUpdateCheckBox.IsChecked == true;
        _app.SetGlobalFontSize(fontSize);
        _app.SaveUiPreferences();

        // Navigate back to the previous screen
        _app.GoToPreviousScreen();
    }

    private void BtnBackToHub_Click(object sender, RoutedEventArgs e)
    {
        _app.GoTo("hub");
    }
}
