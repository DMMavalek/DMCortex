using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex;

public partial class App : Application
{
	private MainWindow? _mainWindow;
	private static Mutex? _singleInstanceMutex;

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		WriteStartupDiagnostics();

		bool isPrimaryInstance;
		_singleInstanceMutex = new Mutex(true, "Global\\DungeonMasterCortex.SingleInstance", out isPrimaryInstance);
		if (!isPrimaryInstance)
		{
			MessageBox.Show("DungeonMaster Cortex is already running.", "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
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

	private async void ShowSplashAndLaunchMain()
	{
		var splash = BuildSplashWindow();
		splash.Show();

		await Task.Delay(TimeSpan.FromSeconds(5));

		_mainWindow = new MainWindow();
		MainWindow = _mainWindow;
		splash.Close();
		_mainWindow.Show();
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
					? "DMCortex-Setup-x.y.z.exe"
					: "PlayerCortex-Setup-x.y.z.exe";
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
					$"You are already on the latest version.\n\nInstalled: {current.ToString(3)}\nLatest: {latest.ToString(3)}",
					"Check for Updates",
					MessageBoxButton.OK,
					MessageBoxImage.Information);
				return;
			}

			var result = ShowMessage(owner,
				$"Version {latest.ToString(3)} is available.\n\nInstalled: {current.ToString(3)}\n\nInstall now?",
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
			string version = AppUpdateService.GetCurrentVersion().ToString(3);
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

