using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class BugReportDialog : Window
{
    private readonly MainWindow _app;
    private readonly LicenseService _license;

    public BugReportDialog(MainWindow app, LicenseService license)
    {
        _app = app;
        _license = license;
        InitializeComponent();
        TxtDiagnostics.Text = BuildDiagnosticsText();
    }

    private void BtnSendReport_Click(object sender, RoutedEventArgs e)
    {
        string summary = TxtSummary.Text.Trim();
        string steps = TxtSteps.Text.Trim();
        string expected = TxtExpected.Text.Trim();
        string actual = TxtActual.Text.Trim();
        string reporterEmail = TxtReporterEmail.Text.Trim();
        string severity = (SeverityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim() ?? "Medium";

        if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(actual))
        {
            TxtStatus.Text = "Status: Summary and Actual Result are required.";
            MessageBox.Show("Please provide at least Summary and Actual Result.", "Bug Report", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(reporterEmail)
            && (!reporterEmail.Contains("@", StringComparison.Ordinal) || !reporterEmail.Contains('.', StringComparison.Ordinal)))
        {
            TxtStatus.Text = "Status: Enter a valid email address or leave it blank.";
            MessageBox.Show("Enter a valid email address or leave the field blank.", "Bug Report", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string reportBody = BuildReportBody(summary, severity, steps, expected, actual, reporterEmail);
        string subjectSuffix = TruncateForSubject(summary, 80);

        if (_license.TrySendBugReportBySmtp(subjectSuffix, reportBody, out string message, reporterEmail))
        {
            TxtStatus.Text = "Status: Bug report sent successfully.";
            MessageBox.Show(message, "Bug Report", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
            return;
        }

        Clipboard.SetText(reportBody);
        var smtpSettings = _license.GetSmtpSettings();
        string targetEmail = string.IsNullOrWhiteSpace(smtpSettings.ToEmail)
            ? "your support email"
            : smtpSettings.ToEmail.Trim();

        TxtStatus.Text = "Status: SMTP send failed. Full bug report copied to clipboard for manual email.";
        MessageBox.Show(
            $"{message}\n\nThe full bug report has been copied to your clipboard. Send it manually to {targetEmail}.",
            "Bug Report",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private string BuildReportBody(string summary, string severity, string steps, string expected, string actual, string reporterEmail)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Bug Report");
        sb.AppendLine("==========");
        sb.AppendLine($"Summary: {summary}");
        sb.AppendLine($"Severity: {severity}");
        sb.AppendLine($"Reported UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Reporter Email: {(string.IsNullOrWhiteSpace(reporterEmail) ? "(not provided)" : reporterEmail)}");
        sb.AppendLine();
        sb.AppendLine("Steps to Reproduce:");
        sb.AppendLine(string.IsNullOrWhiteSpace(steps) ? "(not provided)" : steps);
        sb.AppendLine();
        sb.AppendLine("Expected Result:");
        sb.AppendLine(string.IsNullOrWhiteSpace(expected) ? "(not provided)" : expected);
        sb.AppendLine();
        sb.AppendLine("Actual Result:");
        sb.AppendLine(actual);
        sb.AppendLine();
        sb.AppendLine("Diagnostics:");
        sb.AppendLine(TxtDiagnostics.Text);
        return sb.ToString();
    }

    private string BuildDiagnosticsText()
    {
        string edition = _license.EditionLabel;
        string activation = _license.IsDemoMode ? "Demo mode" : "Activated";
        string version = AppUpdateService.GetCurrentDisplayVersion();

        var sb = new StringBuilder();
        sb.AppendLine($"App Version: {version}");
        sb.AppendLine($"Edition: {edition}");
        sb.AppendLine($"Activation Status: {activation}");
        sb.AppendLine($"OS: {Environment.OSVersion.VersionString}");
        sb.AppendLine($"Machine: {Environment.MachineName}");
        sb.AppendLine($"Character Count Loaded: {_app.Characters.Count}");
        return sb.ToString();
    }

    private static string TruncateForSubject(string value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "General";

        string trimmed = value.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed[..maxLen];
    }
}
