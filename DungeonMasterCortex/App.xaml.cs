using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex;

public partial class App : Application
{
	private MainWindow? _mainWindow;
	private static Mutex? _singleInstanceMutex;
	private static bool _textBoxHandlersRegistered;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		RegisterGlobalTextBoxFocusBehavior();
		WriteStartupDiagnostics();

		if (TryRedirectFromStagingCopy())
			return;

		bool isPrimaryInstance;
		try
		{
			// Use a unique mutex name to avoid conflicts
			string mutexName = $"Global\\DungeonMasterCortex.SingleInstance.{Environment.UserName}";
			_singleInstanceMutex = new Mutex(true, mutexName, out isPrimaryInstance);
			if (!isPrimaryInstance)
			{
				MessageBox.Show("Dungeon Master Codex is already running.", "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
				Current.Shutdown();
				return;
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show($"An error occurred while checking for existing instances: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			Current.Shutdown();
			return;
		}

		string? processPath = Environment.ProcessPath;
		if (!string.IsNullOrWhiteSpace(processPath)
			&& processPath.IndexOf("DMC Updates", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			MessageBox.Show(
				$"This copy is launching from a staging folder, not the installed app path:\n\n{processPath}\n\nPlease launch from the Start Menu shortcut after install/update.",
				"Stale Launch Path Detected",
				MessageBoxButton.OK,
				MessageBoxImage.Warning);
		}

		DispatcherUnhandledException += OnDispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
		TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;

		ShowSplashAndLaunchMain();
	}

	private static void RegisterGlobalTextBoxFocusBehavior()
	{
		if (_textBoxHandlersRegistered)
			return;

		EventManager.RegisterClassHandler(
			typeof(TextBox),
			UIElement.GotKeyboardFocusEvent,
			new KeyboardFocusChangedEventHandler(OnTextBoxGotKeyboardFocus));

		EventManager.RegisterClassHandler(
			typeof(TextBox),
			UIElement.PreviewMouseLeftButtonDownEvent,
			new MouseButtonEventHandler(OnTextBoxPreviewMouseLeftButtonDown),
			true);

		_textBoxHandlersRegistered = true;
	}

	private static void OnTextBoxGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (sender is TextBox textBox && textBox.IsEnabled && !textBox.IsReadOnly)
		{
			textBox.SelectAll();
		}
	}

	private static void OnTextBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (sender is not TextBox textBox || !textBox.IsEnabled || textBox.IsReadOnly)
			return;

		if (!textBox.IsKeyboardFocusWithin)
		{
			e.Handled = true;
			textBox.Focus();
		}
	}

	private static bool TryRedirectFromStagingCopy()
	{
		try
		{
			string? processPath = Environment.ProcessPath;
			if (string.IsNullOrWhiteSpace(processPath)
				|| processPath.IndexOf("DMC Updates", StringComparison.OrdinalIgnoreCase) < 0)
			{
				return false;
			}

			bool isPlayer = AppUpdateService.GetCurrentEdition() == AppEdition.Player;
			string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			string candidate = isPlayer
				? Path.Combine(programFiles, "PlayerCodex", "PlayerCortex.exe")
				: Path.Combine(programFiles, "DMCodex", "DungeonMasterCortex.exe");

			if (!File.Exists(candidate))
				return false;

			if (string.Equals(candidate, processPath, StringComparison.OrdinalIgnoreCase))
				return false;

			Process.Start(new ProcessStartInfo
			{
				FileName = candidate,
				WorkingDirectory = Path.GetDirectoryName(candidate) ?? programFiles,
				UseShellExecute = true,
			});

			MessageBox.Show(
				$"A staging copy was launched. Opening installed copy instead:\n\n{candidate}",
				"Redirecting To Installed App",
				MessageBoxButton.OK,
				MessageBoxImage.Information);

			Current?.Shutdown();
			return true;
		}
		catch
		{
			return false;
		}
	}

	private async void ShowSplashAndLaunchMain()
	{
		var splash = BuildSplashWindow();
		splash.Show();

		await Task.Delay(TimeSpan.FromSeconds(5));

		_mainWindow = new MainWindow();
		MainWindow = _mainWindow;
		splash.Close();
		_mainWindow.Show();
		_ = CheckForUpdatesOnStartupAsync(_mainWindow);
	}

	private static async Task CheckForUpdatesOnStartupAsync(Window? owner)
	{
		try
		{
			if (owner is MainWindow mainWindow && !mainWindow.CheckForUpdatesOnStartup)
				return;

			await Task.Delay(TimeSpan.FromSeconds(5));

			if (Current is null || owner is null || !owner.IsVisible)
				return;

			var updateService = new AppUpdateService();
			var edition = AppUpdateService.GetCurrentEdition();
			var remote = await updateService.GetLatestPackageAsync(edition).ConfigureAwait(true);
			if (remote is null || remote.Version is null)
				return;

			Version current = AppUpdateService.NormalizeVersion(AppUpdateService.GetCurrentVersion());
			Version latest = AppUpdateService.NormalizeVersion(remote.Version);
			if (latest <= current)
				return;

			var result = ShowMessage(owner,
				$"Version {AppUpdateService.ToDisplayVersionString(latest)} is available.\n\nInstalled: {AppUpdateService.ToDisplayVersionString(current)}\n\nInstall now?",
				"Update Available",
				MessageBoxButton.YesNo,
				MessageBoxImage.Information);

			if (result != MessageBoxResult.Yes)
				return;

			if (updateService.TryApplyUpdate(remote, out string message))
			{
				ShowMessage(owner, message, "Updater", MessageBoxButton.OK, MessageBoxImage.Information);

				try
				{
					owner.Close();
				}
				catch
				{
					// Ignore close errors during updater handoff.
				}

				try
				{
					Current?.Shutdown();
				}
				catch
				{
					// Ignore shutdown errors during updater handoff.
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"Startup update check skipped: {ex.Message}");
		}
	}

	private static Window BuildSplashWindow()
	{
		string uri = IsPlayerEdition()
			? "pack://application:,,,/Resources/PlayerSplash.png"
			: "pack://application:,,,/Resources/DmSplash.png";
		bool playerEdition = IsPlayerEdition();

		var image = new Image
		{
			Stretch = Stretch.Uniform,
			Source = new BitmapImage(new Uri(uri, UriKind.Absolute)),
		};

		return new Window
		{
			Title = "Loading...",
			Width = playerEdition ? 620 : 720,
			Height = playerEdition ? 360 : 420,
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
			ResizeMode = ResizeMode.NoResize,
			WindowStyle = WindowStyle.None,
			ShowInTaskbar = true,
			Background = Brushes.Black,
			Content = image,
		};
	}

	internal static async Task CheckForUpdatesAsync(Window? owner)
	{
		try
		{
			var updateService = new AppUpdateService();
			var edition = AppUpdateService.GetCurrentEdition();
			var remote = await updateService.GetLatestPackageAsync(edition);
			if (remote is null || remote.Version is null)
			{
				string expected = edition == AppEdition.DungeonMaster
					? "DMCodex-Setup-x.y.z.exe"
					: "PlayerCodex-Setup-x.y.z.exe";
				ShowMessage(owner,
					$"No valid update package was found.\n\nExpected file name format:\n{expected}",
					"Check for Updates",
					MessageBoxButton.OK,
					MessageBoxImage.Information);
				return;
			}

			Version current = AppUpdateService.NormalizeVersion(AppUpdateService.GetCurrentVersion());
			Version latest = AppUpdateService.NormalizeVersion(remote.Version);
			if (latest <= current)
			{
				ShowMessage(owner,
					$"You are already on the latest version.\n\nInstalled: {AppUpdateService.ToDisplayVersionString(current)}\nLatest: {AppUpdateService.ToDisplayVersionString(latest)}",
					"Check for Updates",
					MessageBoxButton.OK,
					MessageBoxImage.Information);
				return;
			}

			var result = ShowMessage(owner,
				$"Version {AppUpdateService.ToDisplayVersionString(latest)} is available.\n\nInstalled: {AppUpdateService.ToDisplayVersionString(current)}\n\nInstall now?",
				"Update Available",
				MessageBoxButton.YesNo,
				MessageBoxImage.Information);

			if (result != MessageBoxResult.Yes)
				return;

			if (updateService.TryApplyUpdate(remote, out string message))
			{
				ShowMessage(owner, message, "Updater", MessageBoxButton.OK, MessageBoxImage.Information);

				// Installer launch already succeeded; shutdown errors should never be surfaced as update failures.
				try
				{
					owner?.Close();
				}
				catch
				{
					// Non-fatal: app can still continue running until user closes it.
				}

				try
				{
					Current?.Shutdown();
				}
				catch
				{
					// Non-fatal: avoid masking successful installer launch with a false update error.
				}

				return;
			}
			else
			{
				ShowMessage(owner, message, "Updater", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}
		   catch (Exception ex)
		   {
		   ShowMessage(owner, $"Update check failed.\n\n{ex}", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
		   }
	}

	private static MessageBoxResult ShowMessage(
		Window? owner,
		string message,
		string caption,
		MessageBoxButton buttons,
		MessageBoxImage icon)
	{
		return owner is null
			? MessageBox.Show(message, caption, buttons, icon)
			: MessageBox.Show(owner, message, caption, buttons, icon);
	}

	private static bool IsPlayerEdition()
	{
		return AppUpdateService.GetCurrentEdition() == AppEdition.Player;
	}

	private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		ShowFatalError("UI", e.Exception);
		e.Handled = true;
	}

	private static void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		if (e.ExceptionObject is Exception ex)
			ShowFatalError("Domain", ex);
		else
			MessageBox.Show("An unknown fatal error occurred.", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
	}

	private static void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		if (!IsBenignTaskException(e.Exception))
			ShowFatalError("Task", e.Exception);

		e.SetObserved();
	}

	private static void ShowFatalError(string source, Exception ex)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"Unhandled {source} exception:");
		sb.AppendLine(ex.GetType().FullName ?? "Exception");
		sb.AppendLine(ex.Message);
		sb.AppendLine();
		sb.AppendLine(ex.StackTrace ?? "(no stack trace)");

		MessageBox.Show(sb.ToString(), "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
	}

	private static bool IsBenignTaskException(Exception ex)
	{
		if (ex is AggregateException aggregate)
		{
			var flattened = aggregate.Flatten();
			return flattened.InnerExceptions.All(IsBenignTaskException);
		}

		return ex is OperationCanceledException
			|| ex is ObjectDisposedException
			|| ex is System.Net.Sockets.SocketException;
	}

	protected override void OnExit(ExitEventArgs e)
	{
		try
		{
			_singleInstanceMutex?.ReleaseMutex();
		}
		catch
		{
			// Ignore release failures during shutdown.
		}

		_singleInstanceMutex?.Dispose();
		_singleInstanceMutex = null;
		base.OnExit(e);
	}

	private static void WriteStartupDiagnostics()
	{
		try
		{
			string? processPath = Environment.ProcessPath;
			string version = AppUpdateService.GetCurrentDisplayVersion();
			string fileVersion = "(unknown)";
			if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
			{
				fileVersion = FileVersionInfo.GetVersionInfo(processPath).FileVersion ?? "(unknown)";
			}

			string logPath = Path.Combine(Path.GetTempPath(), "DMC_Startup.log");
			File.AppendAllText(logPath,
				$"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ProcessPath={processPath} | Version={version} | FileVersion={fileVersion}{Environment.NewLine}");
		}
		catch
		{
			// Non-fatal diagnostics.
		}
	}
}

