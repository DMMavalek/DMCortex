using System;
using System.IO;
using System.Text;
using System.Windows;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class ReleaseNotesDialog : Window
{
    private readonly Version _currentVersion;

    public bool DoNotShowAgain => ChkDoNotShowAgain.IsChecked == true;

    public ReleaseNotesDialog(Version? currentVersion = null)
    {
        _currentVersion = currentVersion ?? AppUpdateService.GetCurrentVersion();
        InitializeComponent();
        TxtDialogTitle.Text = $"Dungeon Master Cortex v{_currentVersion:0.0.00} - Changelog";
        TxtChangelog.Text = LoadChangelogText();
        TxtChangelog.CaretIndex = 0;
        TxtChangelog.ScrollToHome();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private static string LoadChangelogText()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.txt");
            if (File.Exists(path))
                return FilterChangelogForDisplay(File.ReadAllText(path));
        }
        catch
        {
            // fall through to fallback text
        }

        return "CHANGELOG file not found.\n\nAdd CHANGELOG.txt next to the executable to display the running update history.";
    }

    private static string FilterChangelogForDisplay(string source)
    {
        // Supported hide markers in CHANGELOG.txt:
        // 1) Entire block: [HIDE-START] ... [HIDE-END]
        // 2) Single line: prefix line with [HIDE]
        var builder = new StringBuilder(source.Length);
        bool inHiddenBlock = false;

        using var reader = new StringReader(source);
        while (reader.ReadLine() is string line)
        {
            string trimmed = line.Trim();

            if (string.Equals(trimmed, "[HIDE-START]", StringComparison.OrdinalIgnoreCase))
            {
                inHiddenBlock = true;
                continue;
            }

            if (string.Equals(trimmed, "[HIDE-END]", StringComparison.OrdinalIgnoreCase))
            {
                inHiddenBlock = false;
                continue;
            }

            if (inHiddenBlock)
                continue;

            if (trimmed.StartsWith("[HIDE]", StringComparison.OrdinalIgnoreCase))
                continue;

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }
}
