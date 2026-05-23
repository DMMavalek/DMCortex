using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClosedXML.Excel;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

if (args.Length == 0)
{
    int exitCode = RunActivationToolUi();
    Environment.ExitCode = exitCode;
    return;
}

if (args.Length > 0 && string.Equals(args[0], "generate-activation-response", StringComparison.OrdinalIgnoreCase))
{
    int exitCode = RunActivationResponseGenerator(args);
    Environment.ExitCode = exitCode;
    return;
}

if (args.Length > 0 && string.Equals(args[0], "import-wizard-spells", StringComparison.OrdinalIgnoreCase))
{
    string workbookPath = args.Length > 1
        ? args[1]
        : Path.Combine("Assets", "Spells", "Wizard Spells", "Wizard_Spells_Tactical_Summaries_Standardized.xlsx");

    ImportWizardSpells(workbookPath);
    return;
}

RunEquipmentDescriptionImport();

static int RunActivationToolUi()
{
    int exitCode = 0;

    var thread = new Thread(() =>
    {
        try
        {
            exitCode = RunActivationToolUiSta();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Activation tool failed to start: {ex.Message}", "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Error);
            exitCode = 1;
        }
    });

    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    return exitCode;
}

static int RunActivationToolUiSta()
{
    var app = new Application
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown,
    };

    var splashWindow = BuildSplashWindow();
    var mainWindow = BuildActivationWindow();
    app.MainWindow = mainWindow;

    var timer = new DispatcherTimer
    {
        Interval = TimeSpan.FromSeconds(5),
    };

    timer.Tick += (_, _) =>
    {
        timer.Stop();
        splashWindow.Close();
        app.ShutdownMode = ShutdownMode.OnMainWindowClose;
        mainWindow.Show();
    };

    splashWindow.Show();
    timer.Start();
    app.Run();
    return 0;
}

static Window BuildSplashWindow()
{
    var panel = new StackPanel
    {
        Margin = new Thickness(24),
        VerticalAlignment = VerticalAlignment.Center,
    };

    try
    {
        var image = new Image
        {
            Width = 128,
            Height = 128,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14),
            Source = new BitmapImage(new Uri("pack://application:,,,/Resources/LicenseActivationIcon.png", UriKind.Absolute)),
        };
        panel.Children.Add(image);
    }
    catch
    {
        // If resource loading fails, keep text-only splash instead of crashing startup.
    }

    panel.Children.Add(new TextBlock
    {
        Text = "Dungeon Master Codex",
        FontSize = 24,
        FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
    });

    panel.Children.Add(new TextBlock
    {
        Text = "License Activation Tool",
        Margin = new Thickness(0, 6, 0, 0),
        FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Center,
    });

    return new Window
    {
        Title = "Loading...",
        Width = 440,
        Height = 310,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        ResizeMode = ResizeMode.NoResize,
        Background = Brushes.White,
        ShowInTaskbar = true,
        Content = panel,
    };
}

static Window BuildActivationWindow()
{
    string toolVersion = GetToolVersion();

    var window = new Window
    {
        Title = $"Dungeon Master Codex Activation Tool v{toolVersion}",
        Width = 760,
        Height = 560,
        MinWidth = 680,
        MinHeight = 500,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
    };

    var tabControl = new TabControl
    {
        Margin = new Thickness(10),
    };

    var activateTab = new TabItem { Header = "Generate Activation" };
    var historyTab = new TabItem { Header = "Activated Users" };

    var activationPanel = new StackPanel { Margin = new Thickness(10) };

    activationPanel.Children.Add(new TextBlock
    {
        Text = "Name (required):",
        Margin = new Thickness(0, 0, 0, 4),
    });
    var nameBox = new TextBox
    {
        Height = 30,
        Margin = new Thickness(0, 0, 0, 8),
    };
    activationPanel.Children.Add(nameBox);

    activationPanel.Children.Add(new TextBlock
    {
        Text = "Email (required):",
        Margin = new Thickness(0, 0, 0, 4),
    });
    var emailBox = new TextBox
    {
        Height = 30,
        Margin = new Thickness(0, 0, 0, 8),
    };
    activationPanel.Children.Add(emailBox);

    activationPanel.Children.Add(new TextBlock
    {
        Text = "Phone Number:",
        Margin = new Thickness(0, 0, 0, 4),
    });
    var phoneBox = new TextBox
    {
        Height = 30,
        Margin = new Thickness(0, 0, 0, 8),
    };
    activationPanel.Children.Add(phoneBox);

    activationPanel.Children.Add(new TextBlock
    {
        Text = "Request Code Received:",
        Margin = new Thickness(0, 0, 0, 4),
        FontWeight = FontWeights.SemiBold,
    });

    var requestBox = new TextBox
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 64,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Margin = new Thickness(0, 0, 0, 10),
    };
    activationPanel.Children.Add(requestBox);

    var options = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, 0, 0, 10),
    };

    options.Children.Add(new TextBlock
    {
        Text = "Days Valid:",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
    });

    var daysBox = new TextBox
    {
        Width = 72,
        Text = "0",
        Margin = new Thickness(0, 0, 16, 0),
    };
    options.Children.Add(daysBox);

    options.Children.Add(new TextBlock
    {
        Text = "Edition:",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
    });

    var editionBox = new ComboBox
    {
        Width = 180,
    };
    editionBox.Items.Add(new ComboBoxItem { Content = "Use Request Edition", Tag = string.Empty });
    editionBox.Items.Add(new ComboBoxItem { Content = "DM", Tag = "dm" });
    editionBox.Items.Add(new ComboBoxItem { Content = "Player", Tag = "player" });
    editionBox.Items.Add(new ComboBoxItem { Content = "Any", Tag = "any" });
    editionBox.SelectedIndex = 0;
    options.Children.Add(editionBox);

    activationPanel.Children.Add(options);

    var generateButton = new Button
    {
        Content = "Generate Response Code",
        Width = 220,
        Height = 34,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 0, 0, 10),
    };
    activationPanel.Children.Add(generateButton);

    activationPanel.Children.Add(new TextBlock
    {
        Text = "Response Code (clean display):",
        Margin = new Thickness(0, 0, 0, 4),
        FontWeight = FontWeights.SemiBold,
    });

    var cleanResponseBox = new TextBox
    {
        Height = 30,
        IsReadOnly = true,
        Margin = new Thickness(0, 0, 0, 8),
        FontWeight = FontWeights.Bold,
        FontSize = 14,
    };
    activationPanel.Children.Add(cleanResponseBox);

    activationPanel.Children.Add(new TextBlock
    {
        Text = "Activation Code Sent:",
        Margin = new Thickness(0, 0, 0, 4),
        FontWeight = FontWeights.SemiBold,
    });

    var responseBox = new TextBox
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 128,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        IsReadOnly = true,
        Margin = new Thickness(0, 0, 0, 10),
    };
    activationPanel.Children.Add(responseBox);

    var copyButton = new Button
    {
        Content = "Copy Response Code",
        Width = 160,
        Height = 30,
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    copyButton.Click += (_, _) =>
    {
        if (!string.IsNullOrWhiteSpace(responseBox.Text))
        {
            Clipboard.SetText(responseBox.Text.Trim());
            MessageBox.Show("Response code copied.", "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    };
    activationPanel.Children.Add(copyButton);

    activationPanel.Children.Add(new TextBlock
    {
        Text = $"Activation Tool Version {toolVersion}",
        Margin = new Thickness(0, 10, 0, 0),
        FontSize = 12,
        Foreground = Brushes.DimGray,
    });

    var sendEmailButton = new Button
    {
        Content = "Send Response To Email",
        Width = 180,
        Height = 30,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 8, 0, 0),
    };
    activationPanel.Children.Add(sendEmailButton);

    var smtpSettingsButton = new Button
    {
        Content = "SMTP Settings",
        Width = 180,
        Height = 30,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 8, 0, 0),
    };
    activationPanel.Children.Add(smtpSettingsButton);

    var historyGrid = BuildActivationHistoryGrid();
    var historyEntries = LoadActivationLogEntries();
    historyGrid.ItemsSource = historyEntries;

    var historyPanel = new DockPanel { Margin = new Thickness(10) };
    var searchPanel = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, 0, 0, 8),
    };
    searchPanel.Children.Add(new TextBlock
    {
        Text = "Search Email:",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 8, 0),
    });
    var emailSearchBox = new TextBox
    {
        Width = 260,
        Height = 28,
        Margin = new Thickness(0, 0, 8, 0),
    };
    searchPanel.Children.Add(emailSearchBox);
    var clearSearchButton = new Button
    {
        Content = "Clear",
        Width = 80,
        Height = 28,
    };
    searchPanel.Children.Add(clearSearchButton);

    var refreshButton = new Button
    {
        Content = "Refresh",
        Width = 100,
        Height = 30,
        HorizontalAlignment = HorizontalAlignment.Left,
        Margin = new Thickness(0, 0, 0, 8),
    };
    void ApplyHistoryFilter()
    {
        string emailFilter = emailSearchBox.Text.Trim();
        historyGrid.ItemsSource = string.IsNullOrWhiteSpace(emailFilter)
            ? historyEntries
            : historyEntries
                .Where(entry => entry.Email.Contains(emailFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    refreshButton.Click += (_, _) =>
    {
        historyEntries = LoadActivationLogEntries();
        ApplyHistoryFilter();
    };
    emailSearchBox.TextChanged += (_, _) => ApplyHistoryFilter();
    clearSearchButton.Click += (_, _) =>
    {
        emailSearchBox.Text = string.Empty;
        ApplyHistoryFilter();
    };

    DockPanel.SetDock(searchPanel, Dock.Top);
    historyPanel.Children.Add(searchPanel);
    DockPanel.SetDock(refreshButton, Dock.Top);
    historyPanel.Children.Add(refreshButton);
    historyPanel.Children.Add(historyGrid);

    generateButton.Click += (_, _) =>
    {
        string name = nameBox.Text.Trim();
        string email = emailBox.Text.Trim();
        string phone = phoneBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            MessageBox.Show("Name and Email are required.", "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string requestCode = NormalizeCode(requestBox.Text);
        if (string.IsNullOrWhiteSpace(requestCode))
        {
            MessageBox.Show("Enter the full request payload.", "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(daysBox.Text.Trim(), out int validDays) || validDays < 0)
        {
            MessageBox.Show("Days Valid must be a non-negative integer.", "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string? editionOverride = (editionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(editionOverride))
            editionOverride = null;

        if (!TryGenerateActivationResponse(requestCode, validDays, editionOverride, out string responseCode, out string error))
        {
            MessageBox.Show(error, "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string extractedRequestCode = ExtractFullRequestPayload(requestBox.Text);
        string cleanResponseCode = ToCleanDisplayCode(responseCode);
        string resolvedEdition = editionOverride ?? (TryExtractEditionFromRequest(extractedRequestCode) ?? string.Empty);

        responseBox.Text = responseCode;
        cleanResponseBox.Text = cleanResponseCode;

        var logEntry = new ActivationLogEntry
        {
            ActivatedAtUtc = DateTime.UtcNow,
            Name = name,
            Email = email,
            Phone = phone,
            Edition = ToEditionDisplay(resolvedEdition),
            RequestCodeReceived = extractedRequestCode,
            ActivationCodeSent = responseCode,
        };

        if (!TryAppendActivationLogEntry(logEntry, out string saveMessage))
        {
            MessageBox.Show($"Response generated, but history save failed: {saveMessage}", "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        historyEntries = LoadActivationLogEntries();
        ApplyHistoryFilter();
    };

    sendEmailButton.Click += (_, _) =>
    {
        string name = nameBox.Text.Trim();
        string email = emailBox.Text.Trim();
        string phone = phoneBox.Text.Trim();
        string requestCode = ExtractFullRequestPayload(requestBox.Text);
        string fullResponseCode = responseBox.Text.Trim();
        string cleanResponseCode = cleanResponseBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            MessageBox.Show("Name and Email are required.", "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!IsValidEmailAddress(email))
        {
            MessageBox.Show("Email format is invalid.", "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(fullResponseCode))
        {
            MessageBox.Show("Generate a response code first.", "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (TrySendActivationResponseBySmtp(name, email, phone, requestCode, fullResponseCode, cleanResponseCode, out string sendMessage))
            MessageBox.Show(sendMessage, "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(sendMessage, "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
    };

    smtpSettingsButton.Click += (_, _) =>
    {
        ShowSmtpSettingsDialog(window);
    };

    activateTab.Content = new ScrollViewer
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Content = activationPanel,
    };
    historyTab.Content = historyPanel;
    tabControl.Items.Add(activateTab);
    tabControl.Items.Add(historyTab);

    window.Content = tabControl;
    return window;
}

static DataGrid BuildActivationHistoryGrid()
{
    var grid = new DataGrid
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserAddRows = false,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        Margin = new Thickness(0),
        MinHeight = 380,
    };

    grid.Columns.Add(new DataGridTextColumn
    {
        Header = "Activated UTC",
        Binding = new Binding(nameof(ActivationLogEntry.ActivatedAtUtc)) { StringFormat = "yyyy-MM-dd HH:mm:ss" },
        Width = new DataGridLength(160),
    });
    grid.Columns.Add(new DataGridTextColumn
    {
        Header = "Name",
        Binding = new Binding(nameof(ActivationLogEntry.Name)),
        Width = new DataGridLength(140),
    });
    grid.Columns.Add(new DataGridTextColumn
    {
        Header = "Email",
        Binding = new Binding(nameof(ActivationLogEntry.Email)),
        Width = new DataGridLength(180),
    });
    grid.Columns.Add(new DataGridTextColumn
    {
        Header = "Edition",
        Binding = new Binding(nameof(ActivationLogEntry.Edition)),
        Width = new DataGridLength(110),
    });
    grid.Columns.Add(new DataGridTextColumn
    {
        Header = "Phone",
        Binding = new Binding(nameof(ActivationLogEntry.Phone)),
        Width = new DataGridLength(110),
    });
    grid.Columns.Add(new DataGridTextColumn
    {
        Header = "Activation Code Received",
        Binding = new Binding(nameof(ActivationLogEntry.RequestCodeReceived)),
        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
    });
    grid.Columns.Add(new DataGridTextColumn
    {
        Header = "Activation Code Sent",
        Binding = new Binding(nameof(ActivationLogEntry.ActivationCodeSent)),
        Width = new DataGridLength(1, DataGridLengthUnitType.Star),
    });

    return grid;
}

static string GetActivationLogPath()
{
    string logDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DungeonMasterCortex");

    return Path.Combine(logDirectory, "activation_tool_history.json");
}

static List<ActivationLogEntry> LoadActivationLogEntries()
{
    try
    {
        string path = GetActivationLogPath();
        if (!File.Exists(path))
            return new List<ActivationLogEntry>();

        string json = File.ReadAllText(path);
        var items = JsonSerializer.Deserialize<List<ActivationLogEntry>>(json);
        return (items ?? new List<ActivationLogEntry>())
            .Select(NormalizeActivationLogEntry)
            .OrderByDescending(item => item.ActivatedAtUtc)
            .ToList();
    }
    catch
    {
        return new List<ActivationLogEntry>();
    }
}

static bool TryAppendActivationLogEntry(ActivationLogEntry entry, out string message)
{
    try
    {
        string path = GetActivationLogPath();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var items = LoadActivationLogEntries();
        items.Insert(0, NormalizeActivationLogEntry(entry));

        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        File.WriteAllText(path, json);
        message = "OK";
        return true;
    }
    catch (Exception ex)
    {
        message = ex.Message;
        return false;
    }
}

static string ToCleanDisplayCode(string responseCode)
{
    if (string.IsNullOrWhiteSpace(responseCode))
        return string.Empty;

    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(responseCode));
    const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    var chars = new char[15];

    for (int i = 0; i < chars.Length; i++)
        chars[i] = alphabet[hash[i] % alphabet.Length];

    return string.Create(17, chars, static (span, source) =>
    {
        source.AsSpan(0, 5).CopyTo(span);
        span[5] = '-';
        source.AsSpan(5, 5).CopyTo(span[6..]);
        span[11] = '-';
        source.AsSpan(10, 5).CopyTo(span[12..]);
    });
}

static ActivationLogEntry NormalizeActivationLogEntry(ActivationLogEntry entry)
{
    if (entry is null)
        return new ActivationLogEntry();

    if (string.IsNullOrWhiteSpace(entry.Edition))
    {
        string editionToken = TryExtractEditionFromActivationResponse(entry.ActivationCodeSent)
            ?? TryExtractEditionFromRequest(entry.RequestCodeReceived)
            ?? string.Empty;

        entry.Edition = ToEditionDisplay(editionToken);
    }

    return entry;
}

static string? TryExtractEditionFromRequest(string requestCode)
{
    if (!TryParseActivationRequest(requestCode, out var request, out _))
        return null;

    return request.Edition;
}

static string? TryExtractEditionFromActivationResponse(string responseCode)
{
    string normalized = NormalizeCode(responseCode);
    if (string.IsNullOrWhiteSpace(normalized))
        return null;

    string[] parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 3 || !string.Equals(parts[0], "DMCACT1", StringComparison.OrdinalIgnoreCase))
        return null;

    try
    {
        byte[] payloadBytes = FromBase64Url(parts[1]);
        var payload = JsonSerializer.Deserialize<ActivationResponsePayload>(payloadBytes);
        return payload?.Edition;
    }
    catch
    {
        return null;
    }
}

static string ToEditionDisplay(string? edition)
{
    return (edition ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "dm" => "DM",
        "player" => "Player",
        "any" => "Any",
        _ => string.IsNullOrWhiteSpace(edition) ? "Unknown" : edition.Trim(),
    };
}

static string GetSmtpSettingsPath()
{
    string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DungeonMasterCortex");
    return Path.Combine(root, "smtp_activation.json");
}

static string GetPackagedSmtpSettingsPath()
{
    string baseDirectory = AppContext.BaseDirectory;
    return Path.Combine(baseDirectory, "smtp_activation.json");
}

static string GetPreferredWritableSmtpSettingsPath()
{
    string packagedPath = GetPackagedSmtpSettingsPath();
    if (File.Exists(packagedPath))
        return packagedPath;

    return GetSmtpSettingsPath();
}

static bool IsValidEmailAddress(string value)
{
    try
    {
        _ = new MailAddress(value);
        return true;
    }
    catch
    {
        return false;
    }
}

static void EnsureSmtpSettingsTemplateFile()
{
    string settingsPath = GetPreferredWritableSmtpSettingsPath();
    if (File.Exists(settingsPath))
        return;

    string? directory = Path.GetDirectoryName(settingsPath);
    if (!string.IsNullOrWhiteSpace(directory))
        Directory.CreateDirectory(directory);

    var template = new ToolSmtpSettings();
    File.WriteAllText(settingsPath, JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true }));
}

static bool TryLoadToolSmtpSettings(out ToolSmtpSettings settings, out string message)
{
    settings = new ToolSmtpSettings();
    message = string.Empty;

    try
    {
        string packagedPath = GetPackagedSmtpSettingsPath();
        string appDataPath = GetSmtpSettingsPath();
        string? settingsPath = null;

        if (File.Exists(packagedPath))
            settingsPath = packagedPath;
        else if (File.Exists(appDataPath))
            settingsPath = appDataPath;

        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            EnsureSmtpSettingsTemplateFile();
            message = $"SMTP settings not found. A template was created at: {GetPreferredWritableSmtpSettingsPath()}";
            return false;
        }

        string json = File.ReadAllText(settingsPath);
        settings = JsonSerializer.Deserialize<ToolSmtpSettings>(json) ?? new ToolSmtpSettings();
        return true;
    }
    catch (Exception ex)
    {
        message = $"Failed to read SMTP settings: {ex.Message}";
        return false;
    }
}

static bool TrySaveToolSmtpSettings(ToolSmtpSettings settings, out string message)
{
    message = string.Empty;

    if (string.IsNullOrWhiteSpace(settings.Host)
        || settings.Port <= 0
        || string.IsNullOrWhiteSpace(settings.FromEmail))
    {
        message = "SMTP Host, Port, and From Email are required.";
        return false;
    }

    if (!IsValidEmailAddress(settings.FromEmail))
    {
        message = "From Email format is invalid.";
        return false;
    }

    if (!string.IsNullOrWhiteSpace(settings.ToEmail) && !IsValidEmailAddress(settings.ToEmail))
    {
        message = "Default To Email format is invalid.";
        return false;
    }

    string settingsPath = GetPreferredWritableSmtpSettingsPath();
    try
    {
        string? directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(settingsPath, json);
        message = "SMTP settings saved.";
        return true;
    }
    catch (Exception ex)
    {
        message = $"Failed to save SMTP settings: {ex.Message}";
        return false;
    }
}

static void OpenSmtpSettingsInExplorer()
{
    string settingsPath = GetPreferredWritableSmtpSettingsPath();
    EnsureSmtpSettingsTemplateFile();

    var info = new ProcessStartInfo
    {
        FileName = "explorer.exe",
        Arguments = $"/select,\"{settingsPath}\"",
        UseShellExecute = true,
    };

    Process.Start(info);
}

static void ShowSmtpSettingsDialog(Window owner)
{
    TryLoadToolSmtpSettings(out var loaded, out _);

    var panel = new StackPanel { Margin = new Thickness(12) };

    panel.Children.Add(new TextBlock { Text = "SMTP Host:", Margin = new Thickness(0, 0, 0, 4) });
    var hostBox = new TextBox { Text = loaded.Host, Height = 30, Margin = new Thickness(0, 0, 0, 8) };
    panel.Children.Add(hostBox);

    panel.Children.Add(new TextBlock { Text = "SMTP Port:", Margin = new Thickness(0, 0, 0, 4) });
    var portBox = new TextBox { Text = loaded.Port.ToString(CultureInfo.InvariantCulture), Height = 30, Margin = new Thickness(0, 0, 0, 8) };
    panel.Children.Add(portBox);

    var sslCheck = new CheckBox
    {
        Content = "Use SSL",
        IsChecked = loaded.UseSsl,
        Margin = new Thickness(0, 0, 0, 8),
    };
    panel.Children.Add(sslCheck);

    panel.Children.Add(new TextBlock { Text = "SMTP Username (optional):", Margin = new Thickness(0, 0, 0, 4) });
    var usernameBox = new TextBox { Text = loaded.Username, Height = 30, Margin = new Thickness(0, 0, 0, 8) };
    panel.Children.Add(usernameBox);

    panel.Children.Add(new TextBlock { Text = "SMTP Password (optional):", Margin = new Thickness(0, 0, 0, 4) });
    var passwordBox = new PasswordBox { Password = loaded.Password, Height = 30, Margin = new Thickness(0, 0, 0, 8) };
    panel.Children.Add(passwordBox);

    panel.Children.Add(new TextBlock { Text = "From Email:", Margin = new Thickness(0, 0, 0, 4) });
    var fromEmailBox = new TextBox { Text = loaded.FromEmail, Height = 30, Margin = new Thickness(0, 0, 0, 8) };
    panel.Children.Add(fromEmailBox);

    panel.Children.Add(new TextBlock { Text = "Default To Email (optional):", Margin = new Thickness(0, 0, 0, 4) });
    var toEmailBox = new TextBox { Text = loaded.ToEmail, Height = 30, Margin = new Thickness(0, 0, 0, 12) };
    panel.Children.Add(toEmailBox);

    var buttonRow = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    var saveButton = new Button
    {
        Content = "Save",
        Width = 96,
        Height = 30,
        Margin = new Thickness(0, 0, 8, 0),
    };

    var openButton = new Button
    {
        Content = "Open File",
        Width = 110,
        Height = 30,
        Margin = new Thickness(0, 0, 8, 0),
    };

    var closeButton = new Button
    {
        Content = "Close",
        Width = 96,
        Height = 30,
    };

    buttonRow.Children.Add(saveButton);
    buttonRow.Children.Add(openButton);
    buttonRow.Children.Add(closeButton);
    panel.Children.Add(buttonRow);

    var dialog = new Window
    {
        Title = "SMTP Settings",
        Width = 470,
        Height = 560,
        MinWidth = 430,
        MinHeight = 520,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        Owner = owner,
        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel,
        },
    };

    saveButton.Click += (_, _) =>
    {
        if (!int.TryParse(portBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port <= 0)
        {
            MessageBox.Show("SMTP Port must be a positive integer.", "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = new ToolSmtpSettings
        {
            Host = hostBox.Text.Trim(),
            Port = port,
            UseSsl = sslCheck.IsChecked == true,
            Username = usernameBox.Text.Trim(),
            Password = passwordBox.Password,
            FromEmail = fromEmailBox.Text.Trim(),
            ToEmail = toEmailBox.Text.Trim(),
        };

        if (TrySaveToolSmtpSettings(settings, out string message))
            MessageBox.Show(message, "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(message, "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
    };

    openButton.Click += (_, _) =>
    {
        try
        {
            OpenSmtpSettingsInExplorer();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open SMTP settings file: {ex.Message}", "Activation Tool", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    };

    closeButton.Click += (_, _) => dialog.Close();
    dialog.ShowDialog();
}

static bool TrySendActivationResponseBySmtp(
    string name,
    string recipientEmail,
    string phone,
    string requestCode,
    string fullResponseCode,
    string cleanResponseCode,
    out string message)
{
    ToolSmtpSettings settings;
    if (!TryLoadToolSmtpSettings(out settings, out string loadMessage))
    {
        message = loadMessage;
        return false;
    }

    if (string.IsNullOrWhiteSpace(settings.Host)
        || settings.Port <= 0
        || string.IsNullOrWhiteSpace(settings.FromEmail))
    {
        message = "SMTP settings are incomplete. Open SMTP Settings in the activation tool and fill Host/Port/From Email.";
        return false;
    }

    string subject = "Dungeon Master Codex Activation Response";
    string body = "Your activation response has been generated.\n\n"
        + $"Name: {name}\n"
        + $"Email: {recipientEmail}\n"
        + $"Phone: {phone}\n\n"
        + $"Display Code: {cleanResponseCode}\n\n"
        + "Activation Code (paste this into the app):\n"
        + fullResponseCode
        + "\n\n"
        + "Original Request Payload:\n"
        + requestCode
        + "\n";

    try
    {
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.UseSsl,
            UseDefaultCredentials = false,
            Timeout = 20000,
        };

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            client.Credentials = new NetworkCredential(settings.Username, settings.Password ?? string.Empty);
        }

        MailAddress fromAddress = string.IsNullOrWhiteSpace(settings.DisplayName)
            ? new MailAddress(settings.FromEmail)
            : new MailAddress(settings.FromEmail, settings.DisplayName);

        using var mail = new MailMessage(fromAddress, new MailAddress(recipientEmail))
        {
            Subject = subject,
            Body = body,
        };
        client.Send(mail);
        message = "Activation response email sent.";
        return true;
    }
    catch (SmtpException ex)
    {
        message = $"SMTP send failed ({ex.StatusCode}): {ex.Message}\n"
            + "Check SMTP Host/Port/SSL, credentials, and app-password requirements.";
        return false;
    }
    catch (Exception ex)
    {
        message = $"SMTP send failed: {ex.Message}";
        return false;
    }
}

static string GetToolVersion()
{
    try
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(processPath);
            string? version = info.FileVersion;
            if (!string.IsNullOrWhiteSpace(version))
            {
                string[] parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3)
                    return string.Join('.', parts.Take(3));

                return version;
            }
        }
    }
    catch
    {
        // Fall back to a generic version label.
    }

    return "unknown";
}

static bool TryGenerateActivationResponse(string requestCode, int validDays, string? editionOverride, out string responseCode, out string error)
{
    responseCode = string.Empty;
    error = string.Empty;

    if (!TryParseActivationRequest(requestCode, out var request, out var parseError))
    {
        error = parseError;
        return false;
    }

    string finalEdition = string.IsNullOrWhiteSpace(editionOverride)
        ? request.Edition
        : editionOverride;

    var responsePayload = new ActivationResponsePayload
    {
        Version = 1,
        Fingerprint = request.Fingerprint,
        Edition = finalEdition,
        IssuedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = validDays > 0 ? DateTime.UtcNow.AddDays(validDays) : null,
        LicenseId = "LIC-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
    };

    var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responsePayload));
    var signatureBytes = SignPayload(payloadBytes);
    responseCode = "DMCACT1" + "." + ToBase64Url(payloadBytes) + "." + ToBase64Url(signatureBytes);
    return true;
}

static void RunEquipmentDescriptionImport()
{
    var service = new EquipmentLibraryService();

    // Ensure library migrations run before importing descriptions.
    var library = service.GetEquipmentLibrary();
    Console.WriteLine($"Library items (post-migration): {library.Count}");

    var result = service.ImportMissingDescriptionsFromWebHelp();

    Console.WriteLine($"Total items: {result.TotalItems}");
    Console.WriteLine($"Missing before: {result.MissingDescriptionsBefore}");
    Console.WriteLine($"Filled: {result.DescriptionsFilled}");
    Console.WriteLine($"Missing after: {result.MissingDescriptionsAfter}");
    Console.WriteLine($"Report path: {result.ReportPath}");

    if (result.UnresolvedItemNames.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Unresolved sample:");
        foreach (string name in result.UnresolvedItemNames)
            Console.WriteLine($" - {name}");
    }
}

static void ImportWizardSpells(string workbookPath)
{
    if (!File.Exists(workbookPath))
    {
        Console.Error.WriteLine($"Workbook not found: {workbookPath}");
        Environment.ExitCode = 2;
        return;
    }

    using var wb = new XLWorkbook(workbookPath);
    var ws = wb.Worksheets.First();
    var header = BuildHeaderMap(ws);

    string[] required = { "Name", "Level" };
    foreach (string column in required)
    {
        if (!header.ContainsKey(column))
        {
            Console.Error.WriteLine($"Missing required column '{column}' in row 1.");
            Environment.ExitCode = 3;
            return;
        }
    }

    string Get(IXLRow row, string column)
        => header.TryGetValue(column, out int c) ? row.Cell(c).GetValue<string>().Trim() : string.Empty;

    static bool ParseBool(string text)
    {
        string value = (text ?? string.Empty).Trim();
        return bool.TryParse(value, out bool parsed) && parsed
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    var imported = new List<SpellDefinition>();
    int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
    int generatedIdCounter = 1;

    for (int r = 2; r <= lastRow; r++)
    {
        var row = ws.Row(r);
        string name = Get(row, "Name");
        string level = Get(row, "Level");
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(level))
            continue;

        string id = Get(row, "ID");
        if (string.IsNullOrWhiteSpace(id))
        {
            id = $"WIZIMPORT{generatedIdCounter.ToString("D6", CultureInfo.InvariantCulture)}";
            generatedIdCounter++;
        }

        imported.Add(new SpellDefinition(
            id,
            "arcane",
            level,
            name,
            Get(row, "Reversal"),
            Get(row, "Schools"),
            Get(row, "Range"),
            Get(row, "Components"),
            Get(row, "Materials"),
            Get(row, "Cast Time"),
            Get(row, "Duration"),
            Get(row, "Area"),
            Get(row, "Save"),
            Get(row, "Frequency"),
            Get(row, "Volume"),
            Get(row, "Page"),
            ParseBool(Get(row, "Healing")),
            Get(row, "Damage"),
            Get(row, "Damage Step"),
            Get(row, "Start Level"),
            Get(row, "Every Levels"),
            Get(row, "Max At Level"),
            Get(row, "Max Damage"),
            Get(row, "Brief Description"),
            Get(row, "Description")));
    }

    imported = imported
        .Where(spell => !string.IsNullOrWhiteSpace(spell.Name))
        .GroupBy(spell => spell.Id, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Last())
        .OrderBy(spell => int.TryParse(spell.Level, out int level) ? level : int.MaxValue)
        .ThenBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (imported.Count == 0)
    {
        Console.Error.WriteLine("No wizard spells found in workbook.");
        Environment.ExitCode = 4;
        return;
    }

    var rules = new RulesEngine();
    var nonArcane = rules.Spells
        .Where(spell => !string.Equals(spell.Category, "arcane", StringComparison.OrdinalIgnoreCase));

    var merged = nonArcane
        .Concat(imported)
        .OrderBy(spell => SpellCategorySortKey(spell.Category))
        .ThenBy(spell => int.TryParse(spell.Level, out int level) ? level : int.MaxValue)
        .ThenBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    rules.SaveSpellDefinitions(merged);

    Console.WriteLine($"Imported {imported.Count} wizard spells from '{workbookPath}'.");
    Console.WriteLine($"Preserved {nonArcane.Count()} non-arcane spells.");
    Console.WriteLine($"Total saved spells: {merged.Count}.");
}

static Dictionary<string, int> BuildHeaderMap(IXLWorksheet ws)
{
    var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
    for (int col = 1; col <= lastCol; col++)
    {
        string text = ws.Cell(1, col).GetValue<string>().Trim();
        if (!string.IsNullOrWhiteSpace(text))
            map[text] = col;
    }

    return map;
}

static int SpellCategorySortKey(string category)
{
    return (category ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "arcane" => 0,
        "divine" => 1,
        "psionic" => 2,
        _ => 9,
    };
}

static int RunActivationResponseGenerator(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: generate-activation-response <requestCode> [--days <n>] [--edition <dm|player|any>]");
        return 2;
    }

    string requestCode = NormalizeCode(args[1]);
    int validDays = 0;
    string? editionOverride = null;

    for (int i = 2; i < args.Length; i++)
    {
        if (string.Equals(args[i], "--days", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            if (!int.TryParse(args[i + 1], out validDays) || validDays < 0)
            {
                Console.Error.WriteLine("Invalid value for --days. Use a non-negative integer.");
                return 2;
            }

            i++;
            continue;
        }

        if (string.Equals(args[i], "--edition", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
        {
            editionOverride = args[i + 1].Trim().ToLowerInvariant();
            if (editionOverride is not ("dm" or "player" or "any"))
            {
                Console.Error.WriteLine("Invalid value for --edition. Use dm, player, or any.");
                return 2;
            }

            i++;
            continue;
        }
    }

    if (!TryParseActivationRequest(requestCode, out var request, out var error))
    {
        Console.Error.WriteLine(error);
        return 3;
    }

    string finalEdition = string.IsNullOrWhiteSpace(editionOverride)
        ? request.Edition
        : editionOverride;

    var responsePayload = new ActivationResponsePayload
    {
        Version = 1,
        Fingerprint = request.Fingerprint,
        Edition = finalEdition,
        IssuedAtUtc = DateTime.UtcNow,
        ExpiresAtUtc = validDays > 0 ? DateTime.UtcNow.AddDays(validDays) : null,
        LicenseId = "LIC-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
    };

    var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responsePayload));
    var signatureBytes = SignPayload(payloadBytes);
    string responseCode = "DMCACT1" + "." + ToBase64Url(payloadBytes) + "." + ToBase64Url(signatureBytes);

    Console.WriteLine("Activation response generated.");
    Console.WriteLine();
    Console.WriteLine("Request fingerprint:");
    Console.WriteLine(request.Fingerprint);
    Console.WriteLine();
    Console.WriteLine("Response code (send back by email):");
    Console.WriteLine(responseCode);

    return 0;
}

static bool TryParseActivationRequest(string requestCode, out ActivationRequestPayload request, out string error)
{
    request = new ActivationRequestPayload();
    error = "Invalid request code.";

    string extracted = ExtractFullRequestPayload(requestCode);
    if (string.IsNullOrWhiteSpace(extracted))
    {
        if (ContainsShortDisplayCode(requestCode))
        {
            error = "The short display request code cannot be used here. Paste the full payload from the email section 'Full Payload (for backend)'.";
            return false;
        }

        return false;
    }

    requestCode = extracted;

    string[] parts = requestCode.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 2)
        return false;

    if (!string.Equals(parts[0], "DMCREQ1", StringComparison.OrdinalIgnoreCase))
        return false;

    try
    {
        byte[] payloadBytes = FromBase64Url(parts[1]);
        request = JsonSerializer.Deserialize<ActivationRequestPayload>(payloadBytes) ?? new ActivationRequestPayload();
    }
    catch
    {
        error = "Request code could not be decoded.";
        return false;
    }

    if (request.Version != 1 || string.IsNullOrWhiteSpace(request.Fingerprint) || string.IsNullOrWhiteSpace(request.Edition))
    {
        error = "Request code payload is incomplete.";
        return false;
    }

    return true;
}

static string ExtractFullRequestPayload(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        return string.Empty;

    // Accept pasting entire email/body and extract the actual payload token.
    Match match = Regex.Match(
        value,
        @"DMCREQ1\.[A-Za-z0-9_-]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    if (match.Success)
        return match.Value;

    // Fallback: input may already be mostly normalized payload.
    return NormalizeCode(value);
}

static bool ContainsShortDisplayCode(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        return false;

    return Regex.IsMatch(
        value,
        @"DMCREQ1-[A-Fa-f0-9]{4}(?:-[A-Fa-f0-9]{4}){3}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

static byte[] SignPayload(byte[] payloadBytes)
{
    using var rsa = RSA.Create();
    rsa.ImportParameters(new RSAParameters
    {
        Modulus = Convert.FromBase64String("zdlVZ9Z0fGgBWb02/cx/q52CjZIpfmbp7GJI5jdVubX1u+MpP0ZyiktWzr/aWGCpi9EUCowMmUf7fnqpHjWM6RSzYTJUtWkpvr2wpydPMEVrTDgmm3HDx1RYbv1tY7oWPaOxKYFuNVf0HeNjHmsDd9SSrszGbmI0PRkuSUSDy4a3TcNfhXKNLiupUERWSEYCqTZF5iAnYi+uhtl5atP2JaFsfn52woDi5WhCozFTmp4hNLp81mdfQM8g2lIRPDIQRpD10dRTYWf8+qOTM8Px+GuQy2xuSS3WLV3YyoBoqIV5dWMmIrXY+pRp9ovlE4MWizF6aP87Zf+J9x1yz18AbQ=="),
        Exponent = Convert.FromBase64String("AQAB"),
        D = Convert.FromBase64String("K8aJLBDmKsKvbtcXR7fieqt/ZP3tRw05t+Ra3mJsH5c7j95KGkOv/grxhfw0wdCknbAz095em4Y8THRnXJ5EvhiB4Syj6QRZNU//rjxk0b4hiE70nt/9o3kjaU8JoUikjC0wcsQsnLl8l5KQtJpLXYNeQkAX/sdxloCxYDFq2bAFqZFLD/2DBmqgJF7V5Vb7BwBHZrbgKVcjo4O5D4Mwo/HFu23tHN5TRGtJU4LLW5oiohHExFHOQ2Wm9vDxIsZyRLONCkvYbeUKWHqw5n987u2qhRiQ06nvLv38MM3+9bBvur21yhZPii6LILtAC3pFWWZJ9fEvoqwHmaF+5LEDfQ=="),
        P = Convert.FromBase64String("8lb2Hy0eD4DCz6d30QpJJ93wj8wXf1JcdOWW9pmI5HUbilLCgWl8Knl++Wdf5289XhcPLT4YmsRA6QjaYF3roQq+yXNma8OyjPG9S20tx+JxTA9Old0dlIhfAijZhsb4v6bUx0jh0Cj8HAY0JJ9OYyhjrsQTjQbC9DjKi0xxpVc="),
        Q = Convert.FromBase64String("2XPMojTeWGQSMkS6elP+y4MK0KAw+jOCYuRC0A1fH+LFcaktl/LFpBL2IVdeT3PQU8y/xrsIIWPaBIXYv3nvt3SVI9STqCPFVxluLlUB/0enLhv/dyz9q6GhhhJWoqBMSCiSSqY/fwwBPL2xuWMZXyxLD6h2iRGGtvWvMh7Nids="),
        DP = Convert.FromBase64String("IPY3D9KBLjajSL9MisBNZwDHAagO4iB/tt6rg+sqNXjAQDY1goiofNZ9sMqgvsfgnvWf+NVjX1mmQowTt9vOet8NSDVMDwhVNtqClsnI2lEwe9nxJG0o4tURpyeLPsu9dcPpWRnOrROGBwHJAdoxPUd3F4RP7HSo+7LlycCiDI0="),
        DQ = Convert.FromBase64String("C4iMzf2n3WBRZsEmct6JoRmuNSqJ7ntU6xHYSVisNvC8MC8c7/Y8bVtkGpibs/MclZVChrPc1oiJQ7wlpuI8yKoyTtgzjLN5AAmlQmfX10Zho5xwjE2ilrvX6ViHp9CAu0MLn1H6BC8K0cHt7ztGWTnsMURqJRL85i9Zv3rKxAk="),
        InverseQ = Convert.FromBase64String("yBvIA2/GImi5T8tJo4nQFldjGWUa4xxBh+bPTOkQDGEe9+eTMImEr5962h/hbhqnZ5Xw+p2vJGL1JgxmfCIj9NJihnZrjcDivoJQqaD/ICWnjj9v0VwLRzWkKxzz2+jDVDu9vEuXM1aoQvbXVs6113W96/vO1kR0Qs3wQNW9W7s="),
    });

    return rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
}

static string ToBase64Url(byte[] bytes)
{
    return Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

static byte[] FromBase64Url(string value)
{
    string normalized = value
        .Replace('-', '+')
        .Replace('_', '/');

    int mod4 = normalized.Length % 4;
    if (mod4 > 0)
        normalized = normalized.PadRight(normalized.Length + (4 - mod4), '=');

    return Convert.FromBase64String(normalized);
}

static string NormalizeCode(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        return string.Empty;

    return new string(value.Where(c => !char.IsWhiteSpace(c)).ToArray());
}

file sealed class ActivationRequestPayload
{
    public int Version { get; set; } = 1;
    public string Fingerprint { get; set; } = string.Empty;
    public string Edition { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
}

file sealed class ActivationResponsePayload
{
    public int Version { get; set; } = 1;
    public string Fingerprint { get; set; } = string.Empty;
    public string Edition { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string LicenseId { get; set; } = string.Empty;
}

file sealed class ActivationLogEntry
{
    public DateTime ActivatedAtUtc { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Edition { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string RequestCodeReceived { get; set; } = string.Empty;
    public string ActivationCodeSent { get; set; } = string.Empty;
}

file sealed class ToolSmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string ToEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
