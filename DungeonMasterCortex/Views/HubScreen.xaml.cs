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
        TxtAppVersion.Text = $"Version {AppUpdateService.GetCurrentVersion().ToString(3)}";

        CardDmTools.Visibility = Visibility.Visible;
        CardCampaign.Visibility = playerEdition ? Visibility.Collapsed : Visibility.Visible;
        CardCampaignTracker.Visibility = playerEdition ? Visibility.Collapsed : Visibility.Visible;

        BtnActivate.Content = _app.License.IsDemoMode ? "ACTIVATE" : "ACTIVATED";
    }

    private void BtnCharGen_Click(object sender, RoutedEventArgs e) => _app.GoTo("characters");
    private void BtnDmTools_Click(object sender, RoutedEventArgs e) => _app.GoTo("dm_tools");
    private void BtnCampaign_Click(object sender, RoutedEventArgs e) => _app.GoTo("campaign");

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdates.IsEnabled = false;
        BtnCheckUpdates.Content = "CHECKING...";
        try
        {
            await App.CheckForUpdatesAsync(Window.GetWindow(this));
        }
        finally
        {
            BtnCheckUpdates.IsEnabled = true;
            BtnCheckUpdates.Content = "CHECK FOR UPDATES";
        }
    }

    private void BtnActivate_Click(object sender, RoutedEventArgs e)
    {
        if (!_app.License.IsDemoMode)
        {
            MessageBox.Show(
                _app.License.ActivationStatusLabel,
                "Activation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = BuildActivationDialog();
        if (dialog.ShowDialog() != true)
            return;

        if (_app.License.TryActivate((string)dialog.Tag, out var message))
        {
            MessageBox.Show(message, "Activation", MessageBoxButton.OK, MessageBoxImage.Information);
            OnEnter();
            return;
        }

        MessageBox.Show(message, "Activation", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private Window BuildActivationDialog()
    {
        string requestCode = _app.License.GetActivationRequestCode();
        string requestPayload = _app.License.GetActivationRequestPayload();

        var window = new Window
        {
            Title = "Activate Dungeon Master Codex",
            Width = 640,
            Height = 500,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = "Send activation code via email.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });


        // User info fields
        panel.Children.Add(new TextBlock { Text = "Name (required):", Margin = new Thickness(0, 8, 0, 2) });
        var nameBox = new TextBox { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8, 4, 8, 4), Height = 30 };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock { Text = "Email (required):", Margin = new Thickness(0, 0, 0, 2) });
        var emailBox = new TextBox { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8, 4, 8, 4), Height = 30 };
        panel.Children.Add(emailBox);

        panel.Children.Add(new TextBlock { Text = "Phone Number:", Margin = new Thickness(0, 0, 0, 2) });
        var phoneBox = new TextBox { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(8, 4, 8, 4), Height = 30 };
        panel.Children.Add(phoneBox);

        var btnSendActivation = new Button
        {
            Content = "Send Activation Code via Email",
            Width = 250,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 14,
        };
        btnSendActivation.Click += (_, _) =>
        {
            string name = nameBox.Text.Trim();
            string email = emailBox.Text.Trim();
            string phone = phoneBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
            {
                MessageBox.Show("Name and Email are required.", "Activation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_app.License.TrySendActivationRequestBySmtp(requestPayload, out var sendMessage, name, email, phone))
                MessageBox.Show(sendMessage, "Activation", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(sendMessage, "Activation", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        panel.Children.Add(btnSendActivation);

        panel.Children.Add(new TextBlock
        {
            Text = "Enter Activation Code (from email):",
            Margin = new Thickness(0, 12, 0, 4),
        });

        var box = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(8, 4, 8, 4),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Height = 54,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        panel.Children.Add(box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var btnCancel = new Button
        {
            Content = "Cancel",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
        };
        btnCancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(btnCancel);

        var btnActivate = new Button
        {
            Content = "Activate",
            Width = 90,
            IsDefault = true,
        };
        btnActivate.Click += (_, _) =>
        {
            window.Tag = box.Text.Trim();
            window.DialogResult = true;
        };
        buttons.Children.Add(btnActivate);

        panel.Children.Add(buttons);
        window.Content = panel;
        return window;
    }

    private void OpenSmtpSettingsDialog()
    {
        var settings = _app.License.GetSmtpSettings();

        var window = new Window
        {
            Title = "SMTP Settings",
            Width = 520,
            Height = 470,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock
        {
            Text = "Enter SMTP details for sending activation request emails.",
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
        });

        TextBox AddRow(string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
            var box = new TextBox
            {
                Text = value,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8, 4, 8, 4),
            };
            panel.Children.Add(box);
            return box;
        }

        var hostBox = AddRow("SMTP Host", settings.Host);
        var portBox = AddRow("SMTP Port", settings.Port.ToString());
        var usernameBox = AddRow("SMTP Username", settings.Username);
        var passwordBox = AddRow("SMTP Password", settings.Password);
        var fromBox = AddRow("From Email", settings.FromEmail);
        var toBox = AddRow("To Email", settings.ToEmail);

        var sslCheck = new CheckBox
        {
            Content = "Use SSL/TLS",
            IsChecked = settings.UseSsl,
            Margin = new Thickness(0, 0, 0, 12),
        };
        panel.Children.Add(sslCheck);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var cancel = new Button
        {
            Content = "Cancel",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
        };
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(cancel);

        var save = new Button
        {
            Content = "Save",
            Width = 90,
            IsDefault = true,
        };
        save.Click += (_, _) =>
        {
            if (!int.TryParse(portBox.Text.Trim(), out int port) || port <= 0)
            {
                MessageBox.Show("SMTP Port must be a positive number.", "SMTP Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var updated = new LicenseService.ActivationSmtpSettings
            {
                Host = hostBox.Text.Trim(),
                Port = port,
                UseSsl = sslCheck.IsChecked != false,
                Username = usernameBox.Text.Trim(),
                Password = passwordBox.Text,
                FromEmail = fromBox.Text.Trim(),
                ToEmail = toBox.Text.Trim(),
            };

            if (_app.License.SaveSmtpSettings(updated, out var saveMessage))
            {
                MessageBox.Show(saveMessage, "SMTP Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                window.DialogResult = true;
                return;
            }

            MessageBox.Show(saveMessage, "SMTP Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        buttons.Children.Add(save);

        panel.Children.Add(buttons);
        window.Content = panel;
        _ = window.ShowDialog();
    }
}
