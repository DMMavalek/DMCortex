using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DungeonMasterCortex.Models;
using DungeonMasterCortex.Services;

namespace DungeonMasterCortex.Views;

public partial class SessionScreen : UserControl, IScreen
{
    private readonly MainWindow _app;
    private readonly DispatcherTimer _autoSyncTimer;
    private bool _autoSyncInFlight;
    private bool _reconnectInFlight;
    private string _assignedPlayerId = string.Empty;
    private string _lastSyncedContentHash = string.Empty;
    private SessionInfo? _lastConnectedSession;
    private DateTime _lastReconnectAttemptUtc = DateTime.MinValue;

    private sealed class CharacterChoice
    {
        public CharacterSheet Character { get; init; } = new();
        public string DisplayName { get; init; } = string.Empty;
    }

    public SessionScreen(MainWindow app)
    {
        _app = app;
        InitializeComponent();
        CmbTrustMode.ItemsSource = Enum.GetValues(typeof(SessionTrustMode)).Cast<SessionTrustMode>().ToList();
        CmbTrustMode.SelectedItem = SessionTrustMode.FullPlayerTrust;

        _autoSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _autoSyncTimer.Tick += AutoSyncTimer_Tick;
    }

    public UIElement View => this;

    public void OnEnter()
    {
        bool playerEdition = _app.License.Edition == AppEdition.Player;
        _app.SetBanner("Session Manager");

        HostPanel.Visibility = playerEdition ? Visibility.Collapsed : Visibility.Visible;
        JoinPanel.Visibility = Visibility.Visible;
        TxtTitle.Text = playerEdition ? "JOIN SESSION" : "SESSION MANAGER";
        TxtSubtitle.Text = playerEdition
            ? "Find a DM-hosted table session and join it"
            : "Host a local table session and review player join activity";

        if (string.IsNullOrWhiteSpace(TxtPlayerName.Text))
            TxtPlayerName.Text = Environment.UserName;

        if (string.IsNullOrWhiteSpace(TxtHostName.Text))
            TxtHostName.Text = "Dungeon Master";

        LoadCharacterChoices();
        if (_app.Sessions.ConnectedSession is not null)
            EnsureAutoSyncStarted();

        RefreshUi();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        _autoSyncTimer.Stop();
        _app.GoTo("hub", -1);
    }

    private void BtnStartHosting_Click(object sender, RoutedEventArgs e)
    {
        string sessionName = string.IsNullOrWhiteSpace(TxtSessionName.Text) ? "Saturday Table" : TxtSessionName.Text.Trim();
        string hostName = string.IsNullOrWhiteSpace(TxtHostName.Text) ? "Dungeon Master" : TxtHostName.Text.Trim();
        var trustMode = CmbTrustMode.SelectedItem is SessionTrustMode selected
            ? selected
            : SessionTrustMode.FullPlayerTrust;

        _app.Sessions.StartHosting(sessionName, _app.ActiveCampaignId, _app.Campaign.CampaignName, hostName, trustMode);
        TxtDiscoveryStatus.Text = $"Hosting '{sessionName}'. Local discovery cache updated.";
        RefreshUi();
    }

    private void BtnStopHosting_Click(object sender, RoutedEventArgs e)
    {
        _app.Sessions.StopHosting();
        TxtDiscoveryStatus.Text = "Hosting stopped.";
        RefreshUi();
    }

    private async void BtnRefreshSessions_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyActionAsync(async () =>
        {
            var sessions = (await _app.Sessions.DiscoverLanSessionsAsync()).ToList();
            LstSessions.ItemsSource = sessions;
            TxtDiscoveryStatus.Text = sessions.Count == 0
                ? "No sessions discovered on LAN."
                : $"Found {sessions.Count} session(s) on LAN.";
        });
    }

    private async void BtnJoinSession_Click(object sender, RoutedEventArgs e)
    {
        if (LstSessions.SelectedItem is not SessionInfo session)
        {
            MessageBox.Show("Select a session to join.", "Session Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var choice = CmbPlayerCharacter.SelectedItem as CharacterChoice;
        var character = choice?.Character;
        if (character is null)
        {
            MessageBox.Show("Select a character before joining a session.", "Session Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string playerName = string.IsNullOrWhiteSpace(TxtPlayerName.Text)
            ? character.PlayerName
            : TxtPlayerName.Text.Trim();

        string version = AppUpdateService.GetCurrentVersion().ToString(3);
        var request = _app.Sessions.CreateJoinRequest(session.SessionId, playerName, character, version);

        await RunBusyActionAsync(async () =>
        {
            var response = await _app.Sessions.SendJoinRequestAsync(session, request);
            if (response.Accepted)
            {
                string playerId = string.IsNullOrWhiteSpace(response.AssignedPlayerId)
                    ? request.Player.PlayerId
                    : response.AssignedPlayerId;
                _assignedPlayerId = playerId;
                var envelope = _app.Sessions.BuildCharacterSyncEnvelope(session.SessionId, playerId, character);
                bool syncOk = await _app.Sessions.SendCharacterSyncAsync(session, envelope);
                TxtDiscoveryStatus.Text = $"Joined '{session.SessionName}' as {request.Player.DisplayName}. Synced character '{request.SelectedCharacter.Name}' to host.";

                if (!syncOk)
                    TxtDiscoveryStatus.Text += " Character sync will retry on next update.";
                else
                    _lastSyncedContentHash = envelope.Summary.ContentHash;

                _lastConnectedSession = response.Session ?? session;
                _app.Sessions.ConnectToSession(_lastConnectedSession);
                EnsureAutoSyncStarted();
            }
            else
            {
                TxtDiscoveryStatus.Text = string.IsNullOrWhiteSpace(response.Message)
                    ? "Join request rejected."
                    : response.Message;
            }
        });

        RefreshUi();
    }

    private void LstSessions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnJoinSession.IsEnabled = LstSessions.SelectedItem is SessionInfo;
    }

    private async void BtnSyncNow_Click(object sender, RoutedEventArgs e)
    {
        await SyncSelectedCharacterAsync("manual", forceSync: true);
    }

    private void LoadCharacterChoices()
    {
        var choices = _app.Characters
            .Select(c => new CharacterChoice
            {
                Character = c,
                DisplayName = $"{(string.IsNullOrWhiteSpace(c.Name) ? "Unnamed Character" : c.Name)} (L{Math.Max(1, c.Level)} {(string.IsNullOrWhiteSpace(c.ClassName) ? c.ClassId : c.ClassName)})"
            })
            .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        CmbPlayerCharacter.ItemsSource = choices;
        CmbPlayerCharacter.SelectedItem = choices.FirstOrDefault();
    }

    private void RefreshUi()
    {
        var hosted = _app.Sessions.HostedSession;
        var connected = _app.Sessions.ConnectedSession;

        if (hosted is not null)
        {
            TxtSessionStatus.Text = $"Hosting '{hosted.Session.SessionName}' for campaign '{hosted.Session.CampaignName}' with trust mode {hosted.Session.TrustMode}.";
            _lastConnectedSession = hosted.Session;
        }
        else if (connected is not null)
        {
            TxtSessionStatus.Text = $"Connected session target: '{connected.SessionName}' ({connected.HostAddress}:{connected.HostPort}).";
            _lastConnectedSession = connected;
        }
        else
        {
            TxtSessionStatus.Text = "No active session.";
        }

        LstSessions.ItemsSource = _app.Sessions.DiscoveredSessions.ToList();

        if (hosted is not null)
        {
            LstParticipants.ItemsSource = hosted.Participants
                .Select(p => $"{p.Identity.DisplayName} [{p.Identity.Role}] {(p.IsConnected ? "online" : "offline")}")
                .ToList();

            LstSyncedCharacters.ItemsSource = hosted.Characters
                .Select(c => $"{c.Summary.Name} (L{c.Summary.Level} {c.Summary.ClassName}) - {c.Summary.PlayerName}")
                .ToList();

            LstAuditLog.ItemsSource = _app.Sessions.GetHostedAuditSnapshot()
                .Select(a => $"{a.TimestampUtc.ToLocalTime():HH:mm:ss} [{a.Category}] {a.Actor}: {a.Message}")
                .ToList();
        }
        else
        {
            LstParticipants.ItemsSource = null;
            LstSyncedCharacters.ItemsSource = null;
            LstAuditLog.ItemsSource = null;
        }

        BtnStopHosting.IsEnabled = hosted is not null;
        BtnJoinSession.IsEnabled = LstSessions.SelectedItem is SessionInfo;
        BtnSyncNow.IsEnabled = _app.Sessions.ConnectedSession is not null && CmbPlayerCharacter.SelectedItem is CharacterChoice;

        TxtAutoSyncStatus.Text = _app.Sessions.ConnectedSession is null
            ? "Auto-sync idle (not connected)."
            : _autoSyncTimer.IsEnabled
                ? "Auto-sync active (every 20s)."
                : "Auto-sync paused.";
    }

    private async Task RunBusyActionAsync(Func<Task> action)
    {
        try
        {
            BtnRefreshSessions.IsEnabled = false;
            BtnJoinSession.IsEnabled = false;
            BtnStartHosting.IsEnabled = false;
            BtnStopHosting.IsEnabled = false;
            await action();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Session transport error:\n\n{ex.Message}", "Session Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            RefreshUi();
            BtnStartHosting.IsEnabled = _app.Sessions.HostedSession is null;
        }
    }

    private async void AutoSyncTimer_Tick(object? sender, EventArgs e)
    {
        await SyncSelectedCharacterAsync("auto", forceSync: false);
    }

    private void EnsureAutoSyncStarted()
    {
        if (!_autoSyncTimer.IsEnabled)
            _autoSyncTimer.Start();
    }

    private async Task SyncSelectedCharacterAsync(string source, bool forceSync)
    {
        if (_autoSyncInFlight)
            return;

        var session = _app.Sessions.ConnectedSession;
        if (session is null)
        {
            await AttemptReconnectAsync("no active session");
            return;
        }

        var choice = CmbPlayerCharacter.SelectedItem as CharacterChoice;
        var character = choice?.Character;
        if (character is null)
            return;

        string playerId = string.IsNullOrWhiteSpace(_assignedPlayerId)
            ? _app.Sessions.LocalPeerId
            : _assignedPlayerId;

        var envelope = _app.Sessions.BuildCharacterSyncEnvelope(session.SessionId, playerId, character);
        if (!forceSync && string.Equals(envelope.Summary.ContentHash, _lastSyncedContentHash, StringComparison.OrdinalIgnoreCase))
        {
            TxtAutoSyncStatus.Text = $"Auto-sync checked at {DateTime.Now:HH:mm:ss}; no character changes.";
            return;
        }

        _autoSyncInFlight = true;
        try
        {
            bool ok = await _app.Sessions.SendCharacterSyncAsync(session, envelope);
            if (ok)
            {
                _lastSyncedContentHash = envelope.Summary.ContentHash;
                TxtAutoSyncStatus.Text = $"{(source == "manual" ? "Manual" : "Auto")} sync successful at {DateTime.Now:HH:mm:ss}.";
            }
            else
            {
                TxtAutoSyncStatus.Text = $"{(source == "manual" ? "Manual" : "Auto")} sync failed at {DateTime.Now:HH:mm:ss}.";
                await AttemptReconnectAsync("sync failed");
            }
        }
        catch (Exception ex)
        {
            TxtAutoSyncStatus.Text = $"Sync error at {DateTime.Now:HH:mm:ss}: {ex.Message}";
            await AttemptReconnectAsync("transport exception");
        }
        finally
        {
            _autoSyncInFlight = false;
            RefreshUi();
        }
    }

    private async Task AttemptReconnectAsync(string reason)
    {
        if (_reconnectInFlight)
            return;

        if (DateTime.UtcNow - _lastReconnectAttemptUtc < TimeSpan.FromSeconds(15))
            return;

        var chosen = CmbPlayerCharacter.SelectedItem as CharacterChoice;
        if (chosen?.Character is null)
            return;

        var baselineSession = _app.Sessions.ConnectedSession ?? _lastConnectedSession;
        if (baselineSession is null)
            return;

        _reconnectInFlight = true;
        _lastReconnectAttemptUtc = DateTime.UtcNow;
        try
        {
            TxtAutoSyncStatus.Text = $"Connection lost ({reason}). Attempting reconnect...";

            var discovered = (await _app.Sessions.DiscoverLanSessionsAsync()).ToList();
            var candidate = discovered.FirstOrDefault(s =>
                                string.Equals(s.SessionId, baselineSession.SessionId, StringComparison.OrdinalIgnoreCase))
                            ?? discovered.FirstOrDefault(s =>
                                string.Equals(s.SessionName, baselineSession.SessionName, StringComparison.OrdinalIgnoreCase)
                                && s.HostPort == baselineSession.HostPort)
                            ?? discovered.FirstOrDefault(s =>
                                string.Equals(s.HostAddress, baselineSession.HostAddress, StringComparison.OrdinalIgnoreCase)
                                && s.HostPort == baselineSession.HostPort);

            if (candidate is null)
            {
                TxtAutoSyncStatus.Text = "Reconnect failed: session not found on LAN.";
                return;
            }

            string playerName = string.IsNullOrWhiteSpace(TxtPlayerName.Text)
                ? chosen.Character.PlayerName
                : TxtPlayerName.Text.Trim();
            string version = AppUpdateService.GetCurrentVersion().ToString(3);

            var request = _app.Sessions.CreateJoinRequest(candidate.SessionId, playerName, chosen.Character, version);
            var response = await _app.Sessions.SendJoinRequestAsync(candidate, request);
            if (!response.Accepted)
            {
                TxtAutoSyncStatus.Text = $"Reconnect rejected: {(string.IsNullOrWhiteSpace(response.Message) ? "unknown reason" : response.Message)}";
                return;
            }

            _assignedPlayerId = string.IsNullOrWhiteSpace(response.AssignedPlayerId)
                ? request.Player.PlayerId
                : response.AssignedPlayerId;

            _lastConnectedSession = response.Session ?? candidate;
            _app.Sessions.ConnectToSession(_lastConnectedSession);

            var envelope = _app.Sessions.BuildCharacterSyncEnvelope(_lastConnectedSession.SessionId, _assignedPlayerId, chosen.Character);
            bool syncOk = await _app.Sessions.SendCharacterSyncAsync(_lastConnectedSession, envelope);
            if (syncOk)
            {
                _lastSyncedContentHash = envelope.Summary.ContentHash;
                TxtAutoSyncStatus.Text = $"Reconnect successful at {DateTime.Now:HH:mm:ss}.";
            }
            else
            {
                TxtAutoSyncStatus.Text = "Reconnect partial: joined, but character sync failed.";
            }
        }
        catch (Exception ex)
        {
            TxtAutoSyncStatus.Text = $"Reconnect failed: {ex.Message}";
        }
        finally
        {
            _reconnectInFlight = false;
            RefreshUi();
        }
    }
}
