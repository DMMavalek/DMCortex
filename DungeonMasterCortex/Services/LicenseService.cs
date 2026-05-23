using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace DungeonMasterCortex.Services;

public enum AppEdition
{
    DungeonMaster,
    Player,
}

public sealed class LicenseService
{
    private const string ActivationResponsePrefix = "DMCACT1";
    private const string ActivationRequestPrefix = "DMCREQ1";

    // Public key matching the private key in DungeonMasterCortex.Tools.
    // Replace with your production key material before release.
    private const string ActivationPublicModulusBase64 = "zdlVZ9Z0fGgBWb02/cx/q52CjZIpfmbp7GJI5jdVubX1u+MpP0ZyiktWzr/aWGCpi9EUCowMmUf7fnqpHjWM6RSzYTJUtWkpvr2wpydPMEVrTDgmm3HDx1RYbv1tY7oWPaOxKYFuNVf0HeNjHmsDd9SSrszGbmI0PRkuSUSDy4a3TcNfhXKNLiupUERWSEYCqTZF5iAnYi+uhtl5atP2JaFsfn52woDi5WhCozFTmp4hNLp81mdfQM8g2lIRPDIQRpD10dRTYWf8+qOTM8Px+GuQy2xuSS3WLV3YyoBoqIV5dWMmIrXY+pRp9ovlE4MWizF6aP87Zf+J9x1yz18AbQ==";
    private const string ActivationPublicExponentBase64 = "AQAB";

    private sealed class LicenseState
    {
        public bool IsActivated { get; set; }
        public string ActivationCode { get; set; } = string.Empty;
        public DateTime ActivatedAtUtc { get; set; } = DateTime.MinValue;
    }

    private sealed class ActivationRequestPayload
    {
        public int Version { get; set; } = 1;
        public string Fingerprint { get; set; } = string.Empty;
        public string Edition { get; set; } = string.Empty;
        public DateTime RequestedAtUtc { get; set; }
    }

    public sealed class ActivationSmtpSettings
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public bool UseSsl { get; set; } = true;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string ToEmail { get; set; } = string.Empty;
    }

    private sealed class ActivationResponsePayload
    {
        public int Version { get; set; } = 1;
        public string Fingerprint { get; set; } = string.Empty;
        public string Edition { get; set; } = string.Empty;
        public DateTime IssuedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public string LicenseId { get; set; } = string.Empty;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly string LicenseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DungeonMasterCortex");

    private static readonly string SmtpSettingsPath = Path.Combine(LicenseDirectory, "smtp_activation.json");
    private static readonly string LegacyLicensePath = Path.Combine(LicenseDirectory, "license.json");

    private readonly AppEdition _edition;
    private readonly string _licensePath;
    private LicenseState _state = new();

    public LicenseService(AppEdition edition)
    {
        _edition = edition;
        _licensePath = Path.Combine(LicenseDirectory, $"license.{GetEditionToken(edition)}.json");
        Load();
    }

    public AppEdition Edition => _edition;
    public bool IsDemoMode => !_state.IsActivated;
    public int DemoMaxCharacters => 2;
    public int DemoMaxLevel => 4;
    public int DemoMaxCampaignEntries => 4;

    public string EditionLabel => _edition == AppEdition.DungeonMaster ? "DM Edition" : "Player Edition";

    public string ActivationStatusLabel => IsDemoMode
        ? $"Demo mode active ({EditionLabel})"
        : $"Activated ({EditionLabel})";

    public string ActivationHelpLabel => _edition == AppEdition.DungeonMaster
        ? "Send your request code by email, then paste the signed response code here."
        : "Send your request code by email, then paste the signed response code here.";

    public string GetActivationRequestCode()
    {
        var payload = new ActivationRequestPayload
        {
            Fingerprint = GetMachineFingerprint(),
            Edition = GetEditionToken(_edition),
            RequestedAtUtc = DateTime.UtcNow,
        };

        var json = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        // Use a short hash for the user-facing code
        string shortHash = ToShortCode(SHA256.HashData(payloadBytes));
        // The backend code is still the full payload, but user only sees the short code
        // The full payload is included in the email body for backend use
        return $"{ActivationRequestPrefix}-{shortHash}";
    }

    // Used for backend/SMTP: returns the full payload for the activation tool
    public string GetActivationRequestPayload()
    {
        var payload = new ActivationRequestPayload
        {
            Fingerprint = GetMachineFingerprint(),
            Edition = GetEditionToken(_edition),
            RequestedAtUtc = DateTime.UtcNow,
        };
        var json = JsonSerializer.Serialize(payload);
        return $"{ActivationRequestPrefix}.{ToBase64Url(Encoding.UTF8.GetBytes(json))}";
    }

    private static string ToShortCode(byte[] hash)
    {
        // Take first 8 bytes, encode as 16 hex digits, group as XXXX-XXXX-XXXX-XXXX
        var hex = BitConverter.ToString(hash, 0, 8).Replace("-", "");
        return string.Join("-", Enumerable.Range(0, 4).Select(i => hex.Substring(i * 4, 4)));
    }

    public bool TryActivate(string activationCode, out string message)
    {
        var code = NormalizeCode(activationCode);
        if (string.IsNullOrWhiteSpace(code))
        {
            message = "Enter an activation code.";
            return false;
        }

        if (!TryValidateSignedActivationCode(code, out var validationMessage))
        {
            message = validationMessage;
            return false;
        }

        _state.IsActivated = true;
        _state.ActivationCode = code;
        _state.ActivatedAtUtc = DateTime.UtcNow;
        Save();

        message = "Activation successful.";
        return true;
    }

    public bool ResetToDemo(out string message)
    {
        _state = new LicenseState();
        Save();
        message = "Activation cleared. Demo mode is now active.";
        return true;
    }

    public ActivationSmtpSettings GetSmtpSettings()
    {
        try
        {
            if (!File.Exists(SmtpSettingsPath))
                return new ActivationSmtpSettings();

            var json = File.ReadAllText(SmtpSettingsPath);
            return JsonSerializer.Deserialize<ActivationSmtpSettings>(json) ?? new ActivationSmtpSettings();
        }
        catch
        {
            return new ActivationSmtpSettings();
        }
    }

    public bool SaveSmtpSettings(ActivationSmtpSettings settings, out string message)
    {
        if (settings is null)
        {
            message = "SMTP settings were missing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.Host)
            || settings.Port <= 0
            || string.IsNullOrWhiteSpace(settings.FromEmail)
            || string.IsNullOrWhiteSpace(settings.ToEmail))
        {
            message = "SMTP Host, Port, From Email, and To Email are required.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(LicenseDirectory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SmtpSettingsPath, json);
            message = "SMTP settings saved.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Failed to save SMTP settings: {ex.Message}";
            return false;
        }
    }

    public bool TrySendActivationRequestBySmtp(string requestCode, out string message)
    {
        var settings = GetSmtpSettings();
        if (string.IsNullOrWhiteSpace(settings.Host)
            || settings.Port <= 0
            || string.IsNullOrWhiteSpace(settings.FromEmail)
            || string.IsNullOrWhiteSpace(settings.ToEmail))
        {
            message = "SMTP settings are incomplete. Open SMTP Settings first.";
            return false;
        }

        string shortCode = GetActivationRequestCode();
        string subject = $"Dungeon Master Codex Activation Request ({EditionLabel})";
        string body = "Activation request code generated by Dungeon Master Codex.\n\n"
            + $"Edition: {EditionLabel}\n"
            + $"Machine Fingerprint: {GetMachineFingerprint()}\n"
            + $"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n\n"
            + $"Request Code: {shortCode}\n\n"
            + "Full Payload (for backend):\n"
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

            using var mail = new MailMessage(settings.FromEmail, settings.ToEmail, subject, body);
            client.Send(mail);
            message = "Activation request email sent.";
            return true;
        }
        catch (SmtpException ex)
        {
            message = BuildSmtpErrorMessage(ex);
            return false;
        }
        catch (Exception ex)
        {
            message = $"SMTP send failed: {ex.Message}";
            return false;
        }
    }

    public bool TrySendActivationRequestBySmtp(string requestCode, out string message, string name, string email, string phone)
    {
        var settings = GetSmtpSettings();
        if (string.IsNullOrWhiteSpace(settings.Host)
            || settings.Port <= 0
            || string.IsNullOrWhiteSpace(settings.FromEmail)
            || string.IsNullOrWhiteSpace(settings.ToEmail))
        {
            message = "SMTP settings are incomplete. Open SMTP Settings first.";
            return false;
        }

        string shortCode = GetActivationRequestCode();
        string subject = $"Dungeon Master Codex Activation Request ({EditionLabel})";
        string body = "Activation request code generated by Dungeon Master Codex.\n\n"
            + $"Edition: {EditionLabel}\n"
            + $"Machine Fingerprint: {GetMachineFingerprint()}\n"
            + $"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n\n"
            + $"Name: {name}\n"
            + $"Email: {email}\n"
            + $"Phone: {phone}\n\n"
            + $"Request Code: {shortCode}\n\n"
            + "Full Payload (for backend):\n"
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

            using var mail = new MailMessage(settings.FromEmail, settings.ToEmail, subject, body);
            client.Send(mail);
            message = "Activation request email sent.";
            return true;
        }
        catch (SmtpException ex)
        {
            message = BuildSmtpErrorMessage(ex);
            return false;
        }
        catch (Exception ex)
        {
            message = $"SMTP send failed: {ex.Message}";
            return false;
        }
    }

    private static string BuildSmtpErrorMessage(SmtpException ex)
    {
        var detail = $"SMTP send failed ({ex.StatusCode}): {ex.Message}";
        var hint = "Check SMTP Host/Port/SSL, credentials, and app-password requirements.";
        return $"{detail}\n{hint}";
    }

    private bool TryValidateSignedActivationCode(string code, out string message)
    {
        message = "Invalid activation code format.";

        var parts = code.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;

        if (!string.Equals(parts[0], ActivationResponsePrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        byte[] payloadBytes;
        byte[] signatureBytes;
        try
        {
            payloadBytes = FromBase64Url(parts[1]);
            signatureBytes = FromBase64Url(parts[2]);
        }
        catch
        {
            message = "Activation code is corrupted.";
            return false;
        }

        if (!VerifySignature(payloadBytes, signatureBytes))
        {
            message = "Activation signature is not valid.";
            return false;
        }

        ActivationResponsePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ActivationResponsePayload>(payloadBytes);
        }
        catch
        {
            message = "Activation payload could not be read.";
            return false;
        }

        if (payload is null || payload.Version != 1)
        {
            message = "Activation payload version is unsupported.";
            return false;
        }

        var expectedFingerprint = GetMachineFingerprint();
        if (!string.Equals(payload.Fingerprint, expectedFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            message = "This activation code was generated for a different PC.";
            return false;
        }

        if (!IsEditionTokenAllowedForCurrentEdition(payload.Edition, _edition))
        {
            message = "This activation code is for a different edition.";
            return false;
        }

        if (payload.ExpiresAtUtc.HasValue && payload.ExpiresAtUtc.Value < DateTime.UtcNow)
        {
            message = "This activation code has expired.";
            return false;
        }

        message = "OK";
        return true;
    }

    private static bool VerifySignature(byte[] payloadBytes, byte[] signatureBytes)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters
            {
                Modulus = Convert.FromBase64String(ActivationPublicModulusBase64),
                Exponent = Convert.FromBase64String(ActivationPublicExponentBase64),
            });
            return rsa.VerifyData(payloadBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    private static string GetMachineFingerprint()
    {
        string machineGuid = string.Empty;
        try
        {
            machineGuid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", string.Empty)?.ToString() ?? string.Empty;
        }
        catch
        {
            machineGuid = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(machineGuid))
            machineGuid = Environment.MachineName;

        string raw = $"{machineGuid}|{Environment.MachineName}|{Environment.OSVersion.VersionString}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    private static string GetEditionToken(AppEdition edition)
    {
        return edition switch
        {
            AppEdition.DungeonMaster => "dm",
            AppEdition.Player => "player",
            _ => "player",
        };
    }

    private static bool IsEditionTokenAllowedForCurrentEdition(string token, AppEdition edition)
    {
        if (string.Equals(token, "any", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(token, GetEditionToken(edition), StringComparison.OrdinalIgnoreCase);
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] FromBase64Url(string value)
    {
        string normalized = value
            .Replace('-', '+')
            .Replace('_', '/');

        int mod4 = normalized.Length % 4;
        if (mod4 > 0)
            normalized = normalized.PadRight(normalized.Length + (4 - mod4), '=');

        return Convert.FromBase64String(normalized);
    }

    private static string NormalizeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value.Where(c => !char.IsWhiteSpace(c)).ToArray();
        return new string(chars);
    }

    private void Load()
    {
        try
        {
            string pathToLoad = ResolveLicensePathForLoad();
            if (!File.Exists(pathToLoad))
            {
                _state = new LicenseState();
                return;
            }

            var json = File.ReadAllText(pathToLoad);
            var loaded = JsonSerializer.Deserialize<LicenseState>(json);
            _state = loaded ?? new LicenseState();

            if (!IsPersistedStateValid(_state))
            {
                _state = new LicenseState();
                Save();
                return;
            }

            if (!string.Equals(pathToLoad, _licensePath, StringComparison.OrdinalIgnoreCase))
                Save();
        }
        catch
        {
            _state = new LicenseState();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(LicenseDirectory);
        var json = JsonSerializer.Serialize(_state, JsonOptions);
        File.WriteAllText(_licensePath, json);
    }

    private string ResolveLicensePathForLoad()
    {
        if (File.Exists(_licensePath))
            return _licensePath;

        if (File.Exists(LegacyLicensePath))
            return LegacyLicensePath;

        return _licensePath;
    }

    private bool IsPersistedStateValid(LicenseState state)
    {
        if (!state.IsActivated)
            return true;

        if (string.IsNullOrWhiteSpace(state.ActivationCode))
            return false;

        return TryValidateSignedActivationCode(state.ActivationCode, out _);
    }
}
