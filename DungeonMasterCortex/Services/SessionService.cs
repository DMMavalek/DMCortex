using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DungeonMasterCortex.Models;

namespace DungeonMasterCortex.Services;

public class SessionService
{
    private const int DefaultHostPort = 45731;
    private const int DiscoveryPort = 45732;
    private const string DiscoveryToken = "DMC_DISCOVER_V1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private sealed class TcpRequestEnvelope
    {
        public string Type { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = string.Empty;
    }

    private sealed class TcpResponseEnvelope
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = string.Empty;
    }

    private readonly object _stateGate = new();
    private readonly List<SessionInfo> _discoveredSessions = new();
    private CancellationTokenSource? _hostingCancellation;
    private TcpListener? _tcpListener;
    private UdpClient? _udpDiscoveryServer;
    private Task? _tcpAcceptLoop;
    private Task? _udpDiscoveryLoop;

    public SessionService(AppEdition localEdition)
    {
        LocalEdition = localEdition;
    }

#if DISABLE_NETWORKING
    public static bool NetworkingEnabled => false;
#else
    public static bool NetworkingEnabled => true;
#endif

    public AppEdition LocalEdition { get; }
    public string LocalPeerId { get; } = Guid.NewGuid().ToString("N");
    public SessionRuntimeState RuntimeState { get; private set; } = SessionRuntimeState.Idle;
    public HostedSessionState? HostedSession { get; private set; }
    public SessionInfo? ConnectedSession { get; private set; }
    public IReadOnlyList<SessionInfo> DiscoveredSessions => _discoveredSessions;

    public IReadOnlyList<SessionAuditEntry> GetHostedAuditSnapshot()
    {
        lock (_stateGate)
        {
            if (HostedSession is null)
                return Array.Empty<SessionAuditEntry>();

            return HostedSession.AuditLog
                .OrderByDescending(a => a.TimestampUtc)
                .Take(60)
                .ToList();
        }
    }

    public HostedSessionState StartHosting(string sessionName, string campaignId, string campaignName, string hostDisplayName, SessionTrustMode trustMode, int port = DefaultHostPort)
    {
        StopHosting();

        string hostAddress = GetPreferredLanAddress();
        var session = new SessionInfo
        {
            SessionId = Guid.NewGuid().ToString("N"),
            SessionName = string.IsNullOrWhiteSpace(sessionName) ? "New Session" : sessionName.Trim(),
            CampaignId = string.IsNullOrWhiteSpace(campaignId) ? "default" : campaignId.Trim(),
            CampaignName = string.IsNullOrWhiteSpace(campaignName) ? "Default Campaign" : campaignName.Trim(),
            HostDisplayName = string.IsNullOrWhiteSpace(hostDisplayName) ? "DM" : hostDisplayName.Trim(),
            HostAddress = hostAddress,
            HostPort = port,
            TrustMode = trustMode,
            IsLanVisible = true,
            StartedUtc = DateTime.UtcNow,
            HostEdition = LocalEdition.ToString(),
        };

        lock (_stateGate)
        {
            HostedSession = new HostedSessionState
            {
                Session = session,
                Participants = new List<SessionParticipant>
                {
                    new()
                    {
                        Identity = new PlayerIdentity
                        {
                            PlayerId = LocalPeerId,
                            DisplayName = session.HostDisplayName,
                            Role = SessionParticipantRole.DungeonMaster,
                            Edition = LocalEdition.ToString(),
                        },
                        JoinedUtc = DateTime.UtcNow,
                        IsConnected = true,
                    }
                }
            };

            AddAuditLocked("host", session.HostDisplayName, $"Started session '{session.SessionName}' ({session.TrustMode}).");
        }

        if (NetworkingEnabled)
        {
            _hostingCancellation = new CancellationTokenSource();
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _tcpListener.Start();
            _udpDiscoveryServer = new UdpClient(DiscoveryPort);

            _tcpAcceptLoop = ObserveBackgroundTask(Task.Run(() => RunTcpAcceptLoopAsync(_hostingCancellation.Token)), "tcp-accept-loop");
            _udpDiscoveryLoop = ObserveBackgroundTask(Task.Run(() => RunUdpDiscoveryLoopAsync(_hostingCancellation.Token)), "udp-discovery-loop");
        }

        ConnectedSession = session;
        RuntimeState = SessionRuntimeState.Hosting;
        UpdateDiscoveredSessions(new[] { session });
        return HostedSession!;
    }

    public void StopHosting()
    {
        try
        {
            _hostingCancellation?.Cancel();
        }
        catch
        {
            // no-op
        }

        try
        {
            _udpDiscoveryServer?.Close();
        }
        catch
        {
            // no-op
        }

        try
        {
            _tcpListener?.Stop();
        }
        catch
        {
            // no-op
        }

        _udpDiscoveryServer = null;
        _tcpListener = null;
        _hostingCancellation?.Dispose();
        _hostingCancellation = null;
        _tcpAcceptLoop = null;
        _udpDiscoveryLoop = null;

        if (HostedSession is not null)
            _discoveredSessions.RemoveAll(s => string.Equals(s.SessionId, HostedSession.Session.SessionId, StringComparison.OrdinalIgnoreCase));

        HostedSession = null;
        ConnectedSession = null;
        RuntimeState = SessionRuntimeState.Idle;
    }

    public void ConnectToSession(SessionInfo session)
    {
        ConnectedSession = session;
        RuntimeState = SessionRuntimeState.Connected;
    }

    public void Disconnect()
    {
        if (RuntimeState != SessionRuntimeState.Hosting)
            ConnectedSession = null;

        if (RuntimeState != SessionRuntimeState.Hosting)
            RuntimeState = SessionRuntimeState.Idle;
    }

    public void UpdateDiscoveredSessions(IEnumerable<SessionInfo> sessions)
    {
        lock (_stateGate)
        {
            _discoveredSessions.Clear();
            _discoveredSessions.AddRange(sessions
                .GroupBy(s => s.SessionId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(s => s.SessionName, StringComparer.OrdinalIgnoreCase));
        }
    }

    public async Task<IReadOnlyList<SessionInfo>> DiscoverLanSessionsAsync(int timeoutMs = 1200)
    {
        var found = new List<SessionInfo>();

        if (HostedSession is not null)
            found.Add(HostedSession.Session);

        if (!NetworkingEnabled)
        {
            UpdateDiscoveredSessions(found);
            return DiscoveredSessions.ToList();
        }

        using var udpClient = new UdpClient(0);
        udpClient.EnableBroadcast = true;

        byte[] discoveryBytes = Encoding.UTF8.GetBytes(DiscoveryToken);
        await udpClient.SendAsync(discoveryBytes, discoveryBytes.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

        var deadlineUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(250, timeoutMs));
        while (DateTime.UtcNow < deadlineUtc)
        {
            TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            UdpReceiveResult result;
            try
            {
                result = await udpClient.ReceiveAsync().WaitAsync(remaining);
            }
            catch
            {
                break;
            }

            string payload = Encoding.UTF8.GetString(result.Buffer);
            var session = Deserialize<SessionInfo>(payload);
            if (session is null || string.IsNullOrWhiteSpace(session.SessionId))
                continue;

            found.Add(session);
        }

        UpdateDiscoveredSessions(found);
        return DiscoveredSessions.ToList();
    }

    public async Task<JoinResponse> SendJoinRequestAsync(SessionInfo session, JoinRequest request)
    {
        if (HostedSession is not null
            && string.Equals(HostedSession.Session.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            return RegisterJoinRequest(request);
        }

        if (!NetworkingEnabled)
        {
            return new JoinResponse
            {
                Accepted = false,
                SessionId = session.SessionId,
                Message = "Networking is disabled in this build.",
            };
        }

        return await SendTransportMessageAsync<JoinRequest, JoinResponse>(session, "join", request)
               ?? new JoinResponse
               {
                   Accepted = false,
                   SessionId = session.SessionId,
                   Message = "Join request failed.",
               };
    }

    public async Task<bool> SendCharacterSyncAsync(SessionInfo session, CharacterSyncEnvelope envelope)
    {
        if (HostedSession is not null
            && string.Equals(HostedSession.Session.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            UpsertHostedCharacter(envelope);
            return true;
        }

        if (!NetworkingEnabled)
            return false;

        var response = await SendTransportMessageAsync<CharacterSyncEnvelope, TcpResponseEnvelope>(session, "syncCharacter", envelope);
        return response?.Ok == true;
    }

    public JoinRequest CreateJoinRequest(string sessionId, string playerDisplayName, CharacterSheet selectedCharacter, string clientVersion)
    {
        return new JoinRequest
        {
            SessionId = sessionId,
            Player = new PlayerIdentity
            {
                PlayerId = LocalPeerId,
                DisplayName = string.IsNullOrWhiteSpace(playerDisplayName) ? selectedCharacter.PlayerName : playerDisplayName.Trim(),
                Role = SessionParticipantRole.Player,
                Edition = LocalEdition.ToString(),
            },
            SelectedCharacter = BuildCharacterSummary(selectedCharacter),
            ClientVersion = clientVersion,
            RequestedUtc = DateTime.UtcNow,
        };
    }

    public JoinResponse RegisterJoinRequest(JoinRequest request)
    {
        lock (_stateGate)
        {
            if (HostedSession is null)
            {
                return new JoinResponse
                {
                    Accepted = false,
                    SessionId = request.SessionId,
                    Message = "No hosted session is active.",
                };
            }

            HostedSession.PendingJoinRequests.Add(request);

            var existing = HostedSession.Participants.FirstOrDefault(p =>
                string.Equals(p.Identity.PlayerId, request.Player.PlayerId, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                HostedSession.Participants.Add(new SessionParticipant
                {
                    Identity = request.Player,
                    JoinedUtc = DateTime.UtcNow,
                    SelectedCharacter = request.SelectedCharacter,
                    IsConnected = true,
                });
            }
            else
            {
                existing.Identity = request.Player;
                existing.SelectedCharacter = request.SelectedCharacter;
                existing.IsConnected = true;
            }

            AddAuditLocked("join", request.Player.DisplayName, $"Joined session as player ({request.Player.PlayerId[..8]}). Character: {request.SelectedCharacter.Name}.");

            return new JoinResponse
            {
                Accepted = true,
                SessionId = HostedSession.Session.SessionId,
                Message = "Join request accepted.",
                AssignedPlayerId = request.Player.PlayerId,
                Session = HostedSession.Session,
            };
        }
    }

    public CharacterSyncEnvelope BuildCharacterSyncEnvelope(string sessionId, string playerId, CharacterSheet character)
    {
        return new CharacterSyncEnvelope
        {
            SessionId = sessionId,
            PlayerId = playerId,
            Summary = BuildCharacterSummary(character),
            Character = CloneCharacter(character),
            SyncedUtc = DateTime.UtcNow,
            PlayerAuthorityWins = true,
        };
    }

    public void UpsertHostedCharacter(CharacterSyncEnvelope envelope)
    {
        lock (_stateGate)
        {
            if (HostedSession is null)
                return;

            _ = TryUpsertHostedCharacterLocked(envelope, out _);
        }
    }

    private bool TryUpsertHostedCharacterLocked(CharacterSyncEnvelope envelope, out string message)
    {
        if (HostedSession is null)
        {
            message = "No hosted session available.";
            return false;
        }

        var existing = HostedSession.Characters.FirstOrDefault(c =>
            string.Equals(c.CharacterKey, envelope.Summary.CharacterKey, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            HostedSession.Characters.Add(new SessionCharacterRecord
            {
                CharacterKey = envelope.Summary.CharacterKey,
                PlayerId = envelope.PlayerId,
                Summary = envelope.Summary,
                Character = CloneCharacter(envelope.Character),
                LastSyncedUtc = envelope.SyncedUtc,
            });

            AddAuditLocked("sync", envelope.Summary.PlayerName, $"Created synced character '{envelope.Summary.Name}' (L{envelope.Summary.Level}).");
            message = "Character synchronized.";
            return true;
        }

        if (!string.Equals(existing.PlayerId, envelope.PlayerId, StringComparison.OrdinalIgnoreCase)
            && !envelope.PlayerAuthorityWins)
        {
            message = "Character ownership conflict. Sync rejected by authority rules.";
            AddAuditLocked("conflict", envelope.Summary.PlayerName, $"Rejected sync for '{envelope.Summary.Name}' due to ownership conflict.");
            return false;
        }

        bool ownerChanged = !string.Equals(existing.PlayerId, envelope.PlayerId, StringComparison.OrdinalIgnoreCase);
        existing.PlayerId = envelope.PlayerId;
        existing.Summary = envelope.Summary;
        existing.Character = CloneCharacter(envelope.Character);
        existing.LastSyncedUtc = envelope.SyncedUtc;

        if (ownerChanged)
        {
            AddAuditLocked("sync", envelope.Summary.PlayerName, $"Ownership moved for '{envelope.Summary.Name}' to player {envelope.PlayerId[..8]}.");
        }
        else
        {
            AddAuditLocked("sync", envelope.Summary.PlayerName, $"Updated synced character '{envelope.Summary.Name}' (rev {envelope.Summary.Revision}).");
        }

        message = "Character synchronized.";
        return true;
    }

    public CharacterSummary BuildCharacterSummary(CharacterSheet character)
    {
        string playerName = string.IsNullOrWhiteSpace(character.PlayerName) ? "Player" : character.PlayerName.Trim();
        string characterName = string.IsNullOrWhiteSpace(character.Name) ? "Unnamed Character" : character.Name.Trim();
        string className = !string.IsNullOrWhiteSpace(character.ClassName)
            ? character.ClassName.Trim()
            : character.ClassId.Trim();

        return new CharacterSummary
        {
            CharacterKey = BuildCharacterKey(playerName, characterName),
            Name = characterName,
            PlayerName = playerName,
            ClassName = className,
            Level = Math.Max(1, character.Level),
            Revision = Math.Max(1, character.Revision),
            ContentHash = ComputeCharacterHash(character),
            LastModifiedUtc = character.LastModified.ToUniversalTime(),
        };
    }

    private static string BuildCharacterKey(string playerName, string characterName)
    {
        string raw = $"{playerName.Trim()}::{characterName.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16];
    }

    private static string ComputeCharacterHash(CharacterSheet character)
    {
        var json = JsonSerializer.Serialize(character, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static CharacterSheet CloneCharacter(CharacterSheet character)
    {
        var json = JsonSerializer.Serialize(character, JsonOptions);
        return JsonSerializer.Deserialize<CharacterSheet>(json, JsonOptions) ?? new CharacterSheet();
    }

    private async Task RunUdpDiscoveryLoopAsync(CancellationToken cancellationToken)
    {
        if (_udpDiscoveryServer is null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult incoming = await _udpDiscoveryServer.ReceiveAsync(cancellationToken);
                string message = Encoding.UTF8.GetString(incoming.Buffer);
                if (!string.Equals(message, DiscoveryToken, StringComparison.Ordinal))
                    continue;

                var session = HostedSession?.Session;
                if (session is null)
                    continue;

                string payload = JsonSerializer.Serialize(session, JsonOptions);
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                await _udpDiscoveryServer.SendAsync(bytes, bytes.Length, incoming.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // keep listening
            }
        }
    }

    private async Task RunTcpAcceptLoopAsync(CancellationToken cancellationToken)
    {
        if (_tcpListener is null)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
                _ = ObserveBackgroundTask(HandleTcpClientAsync(client, cancellationToken), "tcp-client-handler");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                client?.Dispose();
            }
        }
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.UTF8, 4096, leaveOpen: true) { AutoFlush = true };

            string? requestLine;
            try
            {
                requestLine = await reader.ReadLineAsync(cancellationToken);
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(requestLine))
                return;

            var request = Deserialize<TcpRequestEnvelope>(requestLine);
            if (request is null)
                return;

            TcpResponseEnvelope response = request.Type switch
            {
                "join" => HandleJoinTransportRequest(request.PayloadJson),
                "syncCharacter" => HandleSyncCharacterTransportRequest(request.PayloadJson),
                "sessionInfo" => HandleSessionInfoTransportRequest(),
                _ => new TcpResponseEnvelope { Ok = false, Message = "Unknown request type." }
            };

            string responseLine = JsonSerializer.Serialize(response, JsonOptions);
            await writer.WriteLineAsync(responseLine.AsMemory(), cancellationToken);
        }
    }

    private TcpResponseEnvelope HandleJoinTransportRequest(string payloadJson)
    {
        var joinRequest = Deserialize<JoinRequest>(payloadJson);
        if (joinRequest is null)
            return new TcpResponseEnvelope { Ok = false, Message = "Invalid join payload." };

        var joinResponse = RegisterJoinRequest(joinRequest);
        return new TcpResponseEnvelope
        {
            Ok = joinResponse.Accepted,
            Message = joinResponse.Message,
            PayloadJson = JsonSerializer.Serialize(joinResponse, JsonOptions),
        };
    }

    private TcpResponseEnvelope HandleSyncCharacterTransportRequest(string payloadJson)
    {
        var envelope = Deserialize<CharacterSyncEnvelope>(payloadJson);
        if (envelope is null)
            return new TcpResponseEnvelope { Ok = false, Message = "Invalid character sync payload." };

        lock (_stateGate)
        {
            bool ok = TryUpsertHostedCharacterLocked(envelope, out string message);
            return new TcpResponseEnvelope { Ok = ok, Message = message };
        }
    }

    private TcpResponseEnvelope HandleSessionInfoTransportRequest()
    {
        if (HostedSession?.Session is null)
            return new TcpResponseEnvelope { Ok = false, Message = "No active hosted session." };

        return new TcpResponseEnvelope
        {
            Ok = true,
            Message = "Session info.",
            PayloadJson = JsonSerializer.Serialize(HostedSession.Session, JsonOptions),
        };
    }

    private async Task<TResponse?> SendTransportMessageAsync<TRequest, TResponse>(SessionInfo session, string requestType, TRequest payload)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(session.HostAddress, session.HostPort);

        using var stream = client.GetStream();
        using var writer = new StreamWriter(stream, Encoding.UTF8, 4096, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);

        var envelope = new TcpRequestEnvelope
        {
            Type = requestType,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
        };

        string line = JsonSerializer.Serialize(envelope, JsonOptions);
        await writer.WriteLineAsync(line);

        string? responseLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(responseLine))
            return default;

        var transportResponse = Deserialize<TcpResponseEnvelope>(responseLine);
        if (transportResponse is null)
            return default;

        if (typeof(TResponse) == typeof(TcpResponseEnvelope))
            return (TResponse?)(object)transportResponse;

        if (!transportResponse.Ok || string.IsNullOrWhiteSpace(transportResponse.PayloadJson))
            return default;

        return Deserialize<TResponse>(transportResponse.PayloadJson);
    }

    private static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private static Task ObserveBackgroundTask(Task task, string taskName)
    {
        task.ContinueWith(static (t, state) =>
        {
            string name = state as string ?? "background-task";
            if (t.Exception is null)
                return;

            var flat = t.Exception.Flatten();
            if (flat.InnerExceptions.All(IsBenignBackgroundException))
                return;

            Debug.WriteLine($"[SessionService:{name}] {flat}");
        }, taskName, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        return task;
    }

    private static bool IsBenignBackgroundException(Exception ex)
    {
        return ex is OperationCanceledException
               || ex is ObjectDisposedException
               || ex is SocketException;
    }

    private static string GetPreferredLanAddress()
    {
        try
        {
            foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (netInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (netInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = netInterface.GetIPProperties();
                foreach (var unicast in ipProps.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        return unicast.Address.ToString();
                }
            }
        }
        catch
        {
            // fallback below
        }

        return "127.0.0.1";
    }

    private void AddAuditLocked(string category, string actor, string message)
    {
        if (HostedSession is null)
            return;

        HostedSession.AuditLog.Add(new SessionAuditEntry
        {
            TimestampUtc = DateTime.UtcNow,
            Category = category,
            Actor = actor,
            Message = message,
        });

        if (HostedSession.AuditLog.Count > 250)
            HostedSession.AuditLog.RemoveRange(0, HostedSession.AuditLog.Count - 250);
    }
}
