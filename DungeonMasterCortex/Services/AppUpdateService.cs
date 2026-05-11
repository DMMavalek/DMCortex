using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DungeonMasterCortex.Services;

public sealed class AppUpdateService
{
    private const string DmFolderUrl = "https://drive.google.com/drive/folders/1LGOrpp3zXTAiYeK6A6Ku9jQJgPGlJRD7?usp=sharing";
    private const string PlayerFolderUrl = "https://drive.google.com/drive/folders/1T2Mp_VQScbvFx4VJaWFWRBQLh03m0gLV?usp=sharing";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) DMCortexUpdater/1.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
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
            string folderUrl = edition == AppEdition.DungeonMaster ? DmFolderUrl : PlayerFolderUrl;
            string folderId = ExtractFolderId(folderUrl);
            if (string.IsNullOrWhiteSpace(folderId))
                return null;

            string html = await FetchFolderHtmlAsync(folderId).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(html))
                return null;

            var entries = ParseEntries(html);
            if (entries is null || entries.Count == 0)
                return null;

            string requiredPrefix = edition == AppEdition.DungeonMaster ? "DMCortex-Setup" : "PlayerCortex-Setup";
            var candidates = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.FileName))
                .Where(e => e.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .Where(e => e.FileName.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                .Select(e =>
                {
                    Version? parsedVersion = ExtractVersionFromFileName(e.FileName);
                    return new UpdatePackageInfo(e.FileName, e.FileId ?? string.Empty, parsedVersion);
                })
                .Where(p => p.Version is not null)
                .OrderByDescending(p => p.Version)
                .ToList();

            return candidates.FirstOrDefault();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Update package discovery failed for edition '{edition}': {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    private static async Task<string> FetchFolderHtmlAsync(string folderId)
    {
        string[] urls =
        {
            $"https://drive.google.com/embeddedfolderview?id={folderId}#list",
            $"https://drive.google.com/drive/folders/{folderId}?usp=sharing",
            $"https://drive.google.com/drive/u/0/folders/{folderId}",
        };

        var errors = new List<string>();
        foreach (string url in urls)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                string html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(html))
                    return html;

                errors.Add($"{url} returned empty content.");
            }
            catch (Exception ex)
            {
                errors.Add($"{url} failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        throw new HttpRequestException("Unable to reach Google Drive update folder. " + string.Join(" | ", errors));
    }

    public bool TryApplyUpdate(UpdatePackageInfo package, out string message)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "DMCortexUpdater");
        Directory.CreateDirectory(tempDir);
        string localInstaller = Path.Combine(tempDir, package.FileName);

        try
        {
            if (!DownloadFromGoogleDrive(package.FileId, localInstaller, out message))
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
            string cmdExe = Environment.GetEnvironmentVariable("ComSpec")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            string installerArgs = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /NOCANCEL /LOG=\"{logPath}\"";
            string delayedCommand = $"/C ping 127.0.0.1 -n 3 > nul && \"\"{localInstaller}\" {installerArgs}\"";

            Process.Start(new ProcessStartInfo
            {
                FileName = cmdExe,
                Arguments = delayedCommand,
                WorkingDirectory = tempDir,
                UseShellExecute = true,
                CreateNoWindow = true,
            });
            message = "Update downloaded and queued. The app will now close, then the installer will run in the background.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Unable to launch installer: {ex.Message}";
            return false;
        }
    }

    private static void CleanupOldShortcuts(string packageFileName)
    {
        string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        string startMenuPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        string commonStartMenuPrograms = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms));

        bool isPlayerEdition = packageFileName.StartsWith("PlayerCortex-Setup", StringComparison.OrdinalIgnoreCase);

        string[] shortcutNames;
        if (isPlayerEdition)
        {
            shortcutNames = new[]
            {
                "PlayerCortex.lnk",
            };
        }
        else
        {
            shortcutNames = new[]
            {
                "DMCortex.lnk",
                "DungeonMasterCortex.lnk",
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

    private static string ExtractFolderId(string url)
    {
        var match = Regex.Match(url, @"/folders/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private bool DownloadFromGoogleDrive(string fileId, string localInstaller, out string message)
    {
        string baseUrl = $"https://drive.google.com/uc?export=download&id={fileId}";

        using var initialResponse = Http.GetAsync(baseUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        string? mediaType = initialResponse.Content.Headers.ContentType?.MediaType;

        if (mediaType is not null && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            string html = initialResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            string? confirmedUrl = BuildConfirmDownloadUrl(html, fileId, baseUrl);
            if (string.IsNullOrWhiteSpace(confirmedUrl))
            {
                message = "Google Drive returned a web page instead of the installer. Verify the file is shared publicly and that the folder contains the .exe update package.";
                return false;
            }

            using var confirmedResponse = Http.GetAsync(confirmedUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            confirmedResponse.EnsureSuccessStatusCode();

            string? confirmedMediaType = confirmedResponse.Content.Headers.ContentType?.MediaType;
            if (confirmedMediaType is not null && confirmedMediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                message = "Google Drive still returned an HTML confirmation page instead of the installer binary. Ensure link sharing is enabled and the updater file is directly downloadable.";
                return false;
            }

            using var confirmedStream = confirmedResponse.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            using var confirmedFile = File.Create(localInstaller);
            confirmedStream.CopyTo(confirmedFile);
        }
        else
        {
            initialResponse.EnsureSuccessStatusCode();
            using var responseStream = initialResponse.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            using var file = File.Create(localInstaller);
            responseStream.CopyTo(file);
        }

        if (!IsWindowsExecutable(localInstaller))
        {
            message = "The downloaded update was not a valid Windows installer. Google Drive likely returned an HTML warning page instead of the EXE.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static string? ExtractConfirmToken(string html)
    {
        var match = Regex.Match(html, @"confirm=([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(html, @"name=""confirm""\s+value=""([^""]+)""", RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? BuildConfirmDownloadUrl(string html, string fileId, string baseUrl)
    {
        // Newer Drive pages often provide a direct confirmed URL in an anchor.
        var linkMatch = Regex.Match(html, @"href=""(?<url>[^""
>]*confirm=[^""
>]*)""", RegexOptions.IgnoreCase);
        if (linkMatch.Success)
        {
            string url = WebUtility.HtmlDecode(linkMatch.Groups["url"].Value);
            if (url.StartsWith("/", StringComparison.Ordinal))
                return "https://drive.google.com" + url;
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return url;
        }

        // Fallback to a confirmation form payload when present.
        var actionMatch = Regex.Match(html, @"<form[^>]*id=""download-form""[^>]*action=""(?<action>[^""
>]+)""", RegexOptions.IgnoreCase);
        if (actionMatch.Success)
        {
            string actionUrl = WebUtility.HtmlDecode(actionMatch.Groups["action"].Value);
            if (actionUrl.StartsWith("/", StringComparison.Ordinal))
                actionUrl = "https://drive.google.com" + actionUrl;

            var inputRegex = new Regex(@"<input[^>]*type=""hidden""[^>]*name=""(?<name>[^""
>]+)""[^>]*value=""(?<value>[^""
>]*)""", RegexOptions.IgnoreCase);
            var fields = inputRegex.Matches(html)
                .Cast<Match>()
                .Where(m => m.Success)
                .ToDictionary(
                    m => WebUtility.HtmlDecode(m.Groups["name"].Value),
                    m => WebUtility.HtmlDecode(m.Groups["value"].Value),
                    StringComparer.OrdinalIgnoreCase);

            if (!fields.ContainsKey("id"))
                fields["id"] = fileId;

            if (!fields.ContainsKey("export"))
                fields["export"] = "download";

            if (fields.Count > 0)
            {
                string query = string.Join("&", fields.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
                string separator = actionUrl.Contains("?", StringComparison.Ordinal) ? "&" : "?";
                return actionUrl + separator + query;
            }
        }

        string? confirmToken = ExtractConfirmToken(html);
        if (!string.IsNullOrWhiteSpace(confirmToken))
            return $"{baseUrl}&confirm={Uri.EscapeDataString(confirmToken)}";

        return null;
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

    private static List<DriveFolderEntry> ParseEntries(string html)
    {
        var list = new List<DriveFolderEntry>();
        var regex = new Regex(
            @"id=""entry-([A-Za-z0-9_-]+)""[\s\S]*?<div class=""flip-entry-title"">([^<]+)</div>",
            RegexOptions.IgnoreCase);

        foreach (Match match in regex.Matches(html))
        {
            if (!match.Success)
                continue;

            string id = (match.Groups[1].Value ?? string.Empty).Trim();
            string title = (System.Net.WebUtility.HtmlDecode(match.Groups[2].Value.Trim()) ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                continue;

            list.Add(new DriveFolderEntry(id, title));
        }

        // Fallback for alternate Drive embedded markup where entry id/title pairing differs.
        if (list.Count == 0)
        {
            var hrefRegex = new Regex(
                @"href=""https://drive\.google\.com/file/d/([A-Za-z0-9_-]+)[^""]*""[\s\S]*?<div class=""flip-entry-title"">([^<]+)</div>",
                RegexOptions.IgnoreCase);

            foreach (Match match in hrefRegex.Matches(html))
            {
                if (!match.Success)
                    continue;

                string id = (match.Groups[1].Value ?? string.Empty).Trim();
                string title = (System.Net.WebUtility.HtmlDecode(match.Groups[2].Value.Trim()) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                    continue;

                list.Add(new DriveFolderEntry(id, title));
            }
        }

        // Fallback for newer Drive markup where file metadata is serialized in script data.
        if (list.Count == 0)
        {
            var scriptDataRegex = new Regex(
                "\\\"id\\\":\\\"([A-Za-z0-9_-]+)\\\"[\\s\\S]*?\\\"name\\\":\\\"([^\\\"]+\\.exe)\\\"",
                RegexOptions.IgnoreCase);

            foreach (Match match in scriptDataRegex.Matches(html))
            {
                if (!match.Success)
                    continue;

                string id = (WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) ?? string.Empty).Trim();
                string title = (WebUtility.HtmlDecode(match.Groups[2].Value.Trim()) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                    continue;

                list.Add(new DriveFolderEntry(id, title));
            }
        }

        return list;
    }

    private static Version? ExtractVersionFromFileName(string name)
    {
        // Expected package examples: DMCortex-Setup-1.0.0.exe / PlayerCortex-Setup-1.0.0.exe
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

    private readonly record struct DriveFolderEntry(string FileId, string FileName);
}

public sealed record UpdatePackageInfo(string FileName, string FileId, Version? Version);
