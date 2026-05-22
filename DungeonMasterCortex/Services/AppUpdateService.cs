using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DungeonMasterCortex.Services;

public sealed class AppUpdateService
{
    private const string GitHubOwner = "DMMavalek";
    private const string GitHubRepository = "DMCortex";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepository + "/releases/latest";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "DMCodexUpdater/1.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json, application/octet-stream, */*");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    public static AppEdition GetCurrentEdition()
    {
        string exeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        if (exeName.Contains("player", StringComparison.OrdinalIgnoreCase))
            return AppEdition.Player;

        return AppEdition.DungeonMaster;
    }

    public static Version NormalizeVersion(Version version)
    {
        int major = Math.Max(0, version.Major);
        int minor = Math.Max(0, version.Minor);
        int build = version.Build >= 0 ? version.Build : 0;
        return new Version(major, minor, build, 0);
    }

    public static Version GetCurrentVersion()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            var info = FileVersionInfo.GetVersionInfo(processPath);

            if (TryParseVersionString(info.FileVersion, out Version? parsedFileVersion) && parsedFileVersion is not null)
                return NormalizeVersion(parsedFileVersion);

            if (TryParseVersionString(info.ProductVersion, out Version? parsedProductVersion) && parsedProductVersion is not null)
                return NormalizeVersion(parsedProductVersion);
        }

        var ver = Assembly.GetEntryAssembly()?.GetName().Version;
        return NormalizeVersion(ver ?? new Version(1, 0, 0, 0));
    }

    public static string ToDisplayVersionString(Version version)
    {
        var normalized = NormalizeVersion(version);
        return $"{normalized.Major}.{normalized.Minor}.{Math.Max(0, normalized.Build):00}";
    }

    public static string GetCurrentDisplayVersion()
    {
        return ToDisplayVersionString(GetCurrentVersion());
    }

    private static bool TryParseVersionString(string? input, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var match = Regex.Match(input, @"(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?");
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups[1].Value, out int major)
            || !int.TryParse(match.Groups[2].Value, out int minor)
            || !int.TryParse(match.Groups[3].Value, out int build))
            return false;

        int revision = 0;
        if (match.Groups[4].Success)
            int.TryParse(match.Groups[4].Value, out revision);

        version = new Version(major, minor, build, revision);
        return true;
    }

    public async Task<UpdatePackageInfo?> GetLatestPackageAsync(AppEdition edition)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApiUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            if (!document.RootElement.TryGetProperty("assets", out var assetsElement)
                || assetsElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string[] requiredPrefixes = edition == AppEdition.DungeonMaster
                ? new[] { "DMCodex-Setup", "DMCortex-Setup" }
                : new[] { "PlayerCodex-Setup", "PlayerCortex-Setup" };

            UpdatePackageInfo? best = null;
            foreach (var asset in assetsElement.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameElement)
                    || !asset.TryGetProperty("browser_download_url", out var downloadUrlElement))
                {
                    continue;
                }

                string fileName = nameElement.GetString() ?? string.Empty;
                string downloadUrl = downloadUrlElement.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(fileName)
                    || string.IsNullOrWhiteSpace(downloadUrl)
                    || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || !requiredPrefixes.Any(prefix => fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Version? parsedVersion = ExtractVersionFromFileName(fileName);
                if (parsedVersion is null)
                    continue;

                var candidate = new UpdatePackageInfo(fileName, downloadUrl, parsedVersion);
                if (best is null || candidate.Version > best.Version)
                    best = candidate;
            }

            return best;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Update package discovery failed for edition '{edition}': {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    public bool TryApplyUpdate(UpdatePackageInfo package, out string message)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "DMCodexUpdater");
        Directory.CreateDirectory(tempDir);
        string localInstaller = Path.Combine(tempDir, package.FileName);

        try
        {
            if (!DownloadInstaller(package.DownloadUrl, localInstaller, out message))
                return false;
        }
        catch (Exception ex)
        {
            message = $"Update download failed: {ex.Message}";
            return false;
        }

        try
        {
            CleanupOldShortcuts(package.FileName);
        }
        catch
        {
            // Non-fatal if shortcut cleanup fails.
        }

        try
        {
            string logPath = Path.Combine(tempDir, "installer.log");
            string installerArgs = $"/SILENT /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /NOCANCEL /LOG=\"{logPath}\"";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = localInstaller,
                    Arguments = installerArgs,
                    WorkingDirectory = tempDir,
                    UseShellExecute = true,
                    Verb = "runas",
                });
            }
            catch
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = localInstaller,
                    Arguments = installerArgs,
                    WorkingDirectory = tempDir,
                    UseShellExecute = true,
                });
            }

            WriteUpdaterDiagnostics(tempDir, localInstaller, installerArgs);
            message = "Update downloaded from GitHub. The app will now close, the installer will show progress, and the app will reopen automatically when finished.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Unable to launch installer: {ex.Message}";
            return false;
        }
    }

    private static void WriteUpdaterDiagnostics(string tempDir, string installerPath, string installerArgs)
    {
        try
        {
            string launchLog = Path.Combine(tempDir, "update-launch.log");
            File.AppendAllText(launchLog,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Installer={installerPath} | Args={installerArgs}{Environment.NewLine}");
        }
        catch
        {
            // Non-fatal diagnostics.
        }
    }

    private static void CleanupOldShortcuts(string packageFileName)
    {
        string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        string startMenuPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        string commonStartMenuPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));

        bool isPlayerEdition = packageFileName.StartsWith("PlayerCodex-Setup", StringComparison.OrdinalIgnoreCase)
            || packageFileName.StartsWith("PlayerCortex-Setup", StringComparison.OrdinalIgnoreCase);

        string[] shortcutNames;
        if (isPlayerEdition)
        {
            shortcutNames = new[]
            {
                "PlayerCodex.lnk",
                "PlayerCortex.lnk",
            };
        }
        else
        {
            shortcutNames = new[]
            {
                "DMCodex.lnk",
                "DungeonMasterCodex.lnk",
                "DMCortex.lnk",
                "DungeonMasterCortex.lnk",
                "DungeonMasterCodex Activation Tool.lnk",
                "DungeonMasterCortex Activation Tool.lnk",
            };
        }

        foreach (string root in new[] { userDesktop, commonDesktop, startMenuPrograms, commonStartMenuPrograms })
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            foreach (string name in shortcutNames)
            {
                string path = Path.Combine(root, name);
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }

    private bool DownloadInstaller(string downloadUrl, string localInstaller, out string message)
    {
        if (File.Exists(localInstaller) && IsWindowsExecutable(localInstaller))
        {
            message = string.Empty;
            return true;
        }

        string tempDownloadPath = localInstaller + ".download";
        if (File.Exists(tempDownloadPath))
            File.Delete(tempDownloadPath);

        using var response = Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        using var responseStream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using (var file = File.Create(tempDownloadPath))
        {
            responseStream.CopyTo(file);
            file.Flush();
        }

        if (!IsWindowsExecutable(tempDownloadPath))
        {
            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            long fileSize = 0;
            try
            {
                fileSize = new FileInfo(tempDownloadPath).Length;
            }
            catch
            {
                // Best-effort diagnostics only.
            }

            try
            {
                File.Delete(tempDownloadPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            message = string.IsNullOrWhiteSpace(mediaType)
                ? $"The downloaded update was not a valid Windows installer. File size: {fileSize} bytes."
                : $"The downloaded update was not a valid Windows installer. Content-Type: {mediaType}; File size: {fileSize} bytes.";
            return false;
        }

        if (File.Exists(localInstaller))
            File.Delete(localInstaller);

        File.Move(tempDownloadPath, localInstaller);

        message = string.Empty;
        return true;
    }

    private static bool IsWindowsExecutable(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return stream.ReadByte() == 0x4D && stream.ReadByte() == 0x5A;
        }
        catch
        {
            return false;
        }
    }

    private static Version? ExtractVersionFromFileName(string name)
    {
        var match = Regex.Match(name, @"(\d+)\.(\d+)\.(\d+)");
        if (!match.Success)
            return null;

        if (!int.TryParse(match.Groups[1].Value, out int major)
            || !int.TryParse(match.Groups[2].Value, out int minor)
            || !int.TryParse(match.Groups[3].Value, out int patch))
        {
            return null;
        }

        return new Version(major, minor, patch, 0);
    }
}

public sealed record UpdatePackageInfo(string FileName, string DownloadUrl, Version? Version);
