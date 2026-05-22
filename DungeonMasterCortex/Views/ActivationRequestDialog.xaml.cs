using System;
using System.Windows;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class ActivationRequestDialog : Window
{
    private readonly LicenseService _license;

    public ActivationRequestDialog(LicenseService license)
    {
        _license = license;
        InitializeComponent();
    }

    private void BtnSendRequest_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtName.Text.Trim();
        string email = TxtEmail.Text.Trim();
        string phone = TxtPhone.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(phone))
        {
            TxtStatus.Text = "Status: Name, email, and phone are required.";
            MessageBox.Show("Name, email, and phone are required.", "Activation Request", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!email.Contains("@", StringComparison.Ordinal) || !email.Contains('.', StringComparison.Ordinal))
        {
            TxtStatus.Text = "Status: Enter a valid email address.";
            MessageBox.Show("Enter a valid email address.", "Activation Request", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string requestPayload = _license.GetActivationRequestPayload();
        if (_license.TrySendActivationRequestBySmtp(requestPayload, out string message, name, email, phone))
        {
            TxtStatus.Text = "Status: Activation request sent successfully.";
            MessageBox.Show(message, "Activation Request", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
            return;
        }

        TxtStatus.Text = $"Status: {message}";
        MessageBox.Show(message, "Activation Request", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
