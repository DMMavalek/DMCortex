using System;
using System.Collections.Generic;

namespace DungeonMasterCortex.Models;

public enum SessionTrustMode
{
    FullPlayerTrust = 0,
    SemiTrusted = 1,
    StrictAuthority = 2,
}

public enum SessionRuntimeState
{
    Idle = 0,
    Hosting = 1,
    Connected = 2,
}

public enum SessionParticipantRole
{
    DungeonMaster = 0,
    Player = 1,
}

public class SessionInfo
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionName { get; set; } = "New Session";
    public string CampaignId { get; set; } = "default";
    public string CampaignName { get; set; } = "Default Campaign";
    public string HostDisplayName { get; set; } = "DM";
    public string HostAddress { get; set; } = "127.0.0.1";
    public int HostPort { get; set; } = 45731;
    public SessionTrustMode TrustMode { get; set; } = SessionTrustMode.FullPlayerTrust;
    public bool IsLanVisible { get; set; } = true;
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public string HostEdition { get; set; } = string.Empty;
}

public class PlayerIdentity
{
    public string PlayerId { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = string.Empty;
    public SessionParticipantRole Role { get; set; } = SessionParticipantRole.Player;
    public string Edition { get; set; } = string.Empty;
}

public class CharacterSummary
{
    public string CharacterKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public int Revision { get; set; } = 1;
    public string ContentHash { get; set; } = string.Empty;
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
}

public class CharacterSyncEnvelope
{
    public string SessionId { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public CharacterSummary Summary { get; set; } = new();
    public CharacterSheet Character { get; set; } = new();
    public DateTime SyncedUtc { get; set; } = DateTime.UtcNow;
    public bool PlayerAuthorityWins { get; set; } = true;
}

public class JoinRequest
{
    public string SessionId { get; set; } = string.Empty;
    public PlayerIdentity Player { get; set; } = new();
    public CharacterSummary SelectedCharacter { get; set; } = new();
    public string ClientVersion { get; set; } = string.Empty;
    public DateTime RequestedUtc { get; set; } = DateTime.UtcNow;
}

public class JoinResponse
{
    public bool Accepted { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string AssignedPlayerId { get; set; } = string.Empty;
    public SessionInfo? Session { get; set; }
}

public class SessionParticipant
{
    public PlayerIdentity Identity { get; set; } = new();
    public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;
    public CharacterSummary? SelectedCharacter { get; set; }
    public bool IsConnected { get; set; } = true;
}

public class SessionCharacterRecord
{
    public string CharacterKey { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public CharacterSummary Summary { get; set; } = new();
    public CharacterSheet Character { get; set; } = new();
    public DateTime LastSyncedUtc { get; set; } = DateTime.UtcNow;
}

public class SessionAuditEntry
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Category { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class HostedSessionState
{
    public SessionInfo Session { get; set; } = new();
    public List<SessionParticipant> Participants { get; set; } = new();
    public List<SessionCharacterRecord> Characters { get; set; } = new();
    public List<JoinRequest> PendingJoinRequests { get; set; } = new();
    public List<SessionAuditEntry> AuditLog { get; set; } = new();
}
