using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace BatteryRushArena.NetworkSpike
{
    /// <summary>
    /// Simple runtime driver and observable debug UI for the network-session spike.
    /// </summary>
    public sealed class NetworkSpikeApp : MonoBehaviour
    {
        private const float ArenaWorldHalfExtent = 7.25f;
        private const float HudCardWidth = 168f;
        private const float HudCardHeight = 48f;
        private const float HudCardGap = 12f;
        private const float AbilityPanelWidth = 212f;
        private const float AbilityPanelHeight = 64f;
        private const float AbilityPillWidth = 94f;
        private const float AbilityPillHeight = 22f;
        private const float CameraFollowSpeed = 6f;
        private const float ToolkitMargin = 16f;
        private const float ToolkitCardWidth = 220f;
        private const float ToolkitCardHeight = 72f;
        private const string ToolkitOverlayResourcePath = "NetworkSpikeUI/NetworkSpikeOverlay";
        private const string ToolkitThemeResourcePath = "NetworkSpikeUI/UnityDefaultRuntimeTheme";
        private static readonly Vector2[] TrapPreviewPositions =
        {
            new(-4.6f, 2.8f),
            new(-1.9f, -4.1f),
            new(2.2f, 4.3f),
            new(4.7f, -2.7f)
        };

        private readonly List<string> _logs = new();
        private readonly List<PlayerVisualState> _playerVisuals = new();
        private readonly Dictionary<int, Vector2> _batteryPositionsById = new();
        private readonly Dictionary<int, Vector2> _trapPositionsById = new();
        private readonly NetworkSpikeClientConfig _config = new();
        private readonly Dictionary<string, SpriteRenderer> _scenePlayerRenderers = new(StringComparer.Ordinal);
        private NetworkSpikeClient _client;
        private CancellationTokenSource _lifetimeCts;
        private string _playerName = "PlayerA";
        private string _roomCode = string.Empty;
        private string _protocolVersionOverride = string.Empty;
        private int _tick;
        private float _inputTickAccumulator;
        private bool _fireBuffered;
        private bool _autoHeartbeat = true;
        private bool _readyRequested;
        private bool _autoConnectAttempted;
        private SpikeServerMessage _lastServerMessage = new SpikeServerMessage();
        private int[] _activeBatteryIds = Array.Empty<int>();
        private GUIStyle _overlayTitleStyle;
        private GUIStyle _overlaySubStyle;
        private GUIStyle _pillLabelStyle;
        private GUIStyle _fallbackPanelTitleStyle;
        private GUIStyle _fallbackPanelBodyStyle;
        private GUIStyle _fallbackPanelValueStyle;
        private GUIStyle _fallbackButtonStyle;
        private GUIStyle _fallbackFieldStyle;
        private GUIStyle _fallbackFieldInputStyle;
        private GUIStyle _fallbackSectionLabelStyle;
        private Transform _sceneRoot;
        private Transform _uiOverlayRoot;
        private SpriteRenderer _arenaSurfaceRenderer;
        private SpriteRenderer[] _batterySceneRenderers = Array.Empty<SpriteRenderer>();
        private SpriteRenderer[] _trapSceneRenderers = Array.Empty<SpriteRenderer>();
        private PanelSettings _panelSettings;
        private VisualTreeAsset _toolkitOverlayAsset;
        private StyleSheet _toolkitOverlayStyleSheet;
        private ThemeStyleSheet _toolkitThemeStyleSheet;
        private VisualElement _uiRoot;
        private VisualElement _preMatchOverlay;
        private VisualElement _activeHudOverlay;
        private VisualElement _resultsOverlay;
        private Label _toolkitPreMatchTitleLabel;
        private Label _toolkitPreMatchSummaryLabel;
        private Label _toolkitPreMatchMembersLabel;
        private Label _toolkitPreMatchRoomsLabel;
        private Label _toolkitCountdownLabel;
        private Label _toolkitTopLabel;
        private Label _toolkitScoreLabel;
        private Label _toolkitCooldownLabel;
        private Label _toolkitEffectLabel;
        private VisualElement _toolkitNetworkPanel;
        private Label _toolkitNetworkTelemetryLabel;
        private Label _toolkitNetworkEventsLabel;
        private Label _toolkitResultsTitleLabel;
        private Label _toolkitResultsScoreLabel;
        private Label _toolkitResultsDetailLabel;
        private TextField _toolkitPlayerNameField;
        private TextField _toolkitRoomCodeField;
        private Button _toolkitConnectButton;
        private Button _toolkitCreateButton;
        private Button _toolkitJoinButton;
        private Button _toolkitReadyButton;
        private bool _suppressToolkitFieldCallbacks;
        private bool _toolkitUsesAuthoredAssets;
        private static Sprite _solidSprite;
        private Font _fallbackToolkitFont;

        private void Awake()
        {
            _lifetimeCts = new CancellationTokenSource();
            EnsureDefaultPlayerName();
            _client = new NetworkSpikeClient(_config);
            _client.LogEmitted += AppendLog;
            _client.MessageReceived += OnMessageReceived;
            EnsureScenePresentation();
            EnsureUiToolkitOverlay();
            ConfigureCamera();
            AppendLog("Network spike bootstrap ready.");
        }

        private void Start()
        {
            BeginInitialConnectionAttempt();
        }

        private void Update()
        {
            if (_client == null || !_client.IsConnected)
            {
                EnsureUiToolkitOverlay();
                return;
            }

            if (_autoHeartbeat)
            {
                _ = PumpHeartbeatAsync();
            }

            if (IsRoomState("Active"))
            {
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                {
                    _fireBuffered = true;
                }

                PumpInputFrames();
            }
            else
            {
                _inputTickAccumulator = 0f;
                _fireBuffered = false;
            }

            UpdateCameraFollow();
            EnsureUiToolkitOverlay();
        }

        private void PumpInputFrames()
        {
            var tickInterval = Mathf.Max(0.01f, _config.TickIntervalSeconds);
            _inputTickAccumulator += Time.unscaledDeltaTime;

            while (_inputTickAccumulator >= tickInterval)
            {
                _inputTickAccumulator -= tickInterval;

                var move = ReadMoveVector();
                var firePressed = _fireBuffered;
                if (move.sqrMagnitude <= 0.0001f && !firePressed)
                {
                    continue;
                }

                _tick += 1;
                _ = _client.SendInputFrameAsync(
                    _tick,
                    move,
                    ReadAimVector(),
                    firePressed,
                    _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);

                _fireBuffered = false;
            }
        }

        private void OnGUI()
        {
            DrawModernFallbackOverlay();
        }

        private void OnDestroy()
        {
            if (_lifetimeCts != null) _lifetimeCts.Cancel();
            if (_client != null) _client.Dispose();
            if (_lifetimeCts != null) _lifetimeCts.Dispose();
            if (_sceneRoot != null)
            {
                Destroy(_sceneRoot.gameObject);
            }
            if (_uiOverlayRoot != null)
            {
                Destroy(_uiOverlayRoot.gameObject);
            }
            if (_panelSettings != null)
            {
                Destroy(_panelSettings);
            }
        }

        private void DrawModernFallbackOverlay()
        {
            EnsureOverlayStyles();

            if (IsAnyRoomState("Ended", "Saving", "ResultsReady"))
            {
                DrawFallbackResultsPanel();
            }
            else if (!IsRoomState("Active"))
            {
                DrawFallbackPrematchPanel();
                DrawFallbackNetworkPanel();
            }
            else
            {
                NetworkSpikeActiveHudRenderer.Draw(new Rect(0f, 0f, Screen.width, Screen.height), BuildActiveHudSnapshot());
            }
        }

        private void DrawFallbackPrematchPanel()
        {
            var panelRect = new Rect(24f, 24f, Mathf.Min(Screen.width * 0.42f, 560f), Mathf.Min(Screen.height * 0.80f, 620f));
            DrawPanelChrome(panelRect, new Color(0.05f, 0.09f, 0.16f, 0.94f), new Color(0.34f, 0.82f, 0.96f, 1f));

            GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 22f, panelRect.width - 48f, 36f), BuildPreMatchTitle(), _fallbackPanelTitleStyle);
            GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 62f, panelRect.width - 48f, 28f), BuildPreMatchSummary(), _fallbackPanelBodyStyle);

            if (IsRoomState("Countdown"))
            {
                GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 116f, panelRect.width - 48f, 32f), "SQUAD STATUS", _fallbackSectionLabelStyle);
                GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 148f, panelRect.width - 48f, 108f), BuildLobbyMembersSummary(), _fallbackPanelBodyStyle);
                GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 292f, panelRect.width - 48f, 52f), $"MATCH STARTS IN {Mathf.CeilToInt(Mathf.Max(0.1f, _lastServerMessage.CountdownRemainingSeconds))}", _fallbackPanelValueStyle);
                GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 348f, panelRect.width - 48f, 52f), "Authoritative countdown from the host.", _fallbackPanelBodyStyle);
                return;
            }

            GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 104f, 140f, 24f), "PLAYER", _fallbackSectionLabelStyle);
            _playerName = DrawFallbackTextField(new Rect(panelRect.x + 24f, panelRect.y + 132f, panelRect.width - 48f, 42f), _playerName);

            GUI.Label(new Rect(panelRect.x + 24f, panelRect.y + 188f, 180f, 24f), "ROOM CODE", _fallbackSectionLabelStyle);
            _roomCode = DrawFallbackTextField(new Rect(panelRect.x + 24f, panelRect.y + 216f, panelRect.width - 48f, 42f), _roomCode);

            var buttonWidth = (panelRect.width - 64f) / 3f;
            if (DrawFallbackButton(new Rect(panelRect.x + 24f, panelRect.y + 278f, buttonWidth, 46f), "SYNC", false))
            {
                _ = ConnectFromUiAsync();
            }

            if (DrawFallbackButton(new Rect(panelRect.x + 32f + buttonWidth, panelRect.y + 278f, buttonWidth, 46f), "HOST MATCH", true))
            {
                _ = CreateRoomFromUiAsync();
            }

            if (DrawFallbackButton(new Rect(panelRect.x + 40f + (buttonWidth * 2f), panelRect.y + 278f, buttonWidth, 46f), "ENTER ROOM", false))
            {
                _ = JoinRoomFromUiAsync();
            }

            var footerButtonHeight = 46f;
            var footerGap = 10f;
            var readyButtonY = panelRect.yMax - footerButtonHeight - 20f;
            var startButtonY = readyButtonY - footerButtonHeight - footerGap;
            var hasStartButton = IsLocalPlayerHost() && IsRoomState("Lobby");

            var membersTop = panelRect.y + 340f;
            var membersHeight = 68f;
            var roomsTop = membersTop + membersHeight + 12f;
            var roomsBottom = hasStartButton ? startButtonY - 12f : readyButtonY - 12f;
            var roomsHeight = Mathf.Max(60f, roomsBottom - roomsTop);

            GUI.Label(new Rect(panelRect.x + 24f, membersTop, panelRect.width - 48f, membersHeight), BuildLobbyMembersSummary(), _fallbackPanelBodyStyle);
            GUI.Label(new Rect(panelRect.x + 24f, roomsTop, panelRect.width - 48f, roomsHeight), BuildRoomListingsSummary(), _fallbackPanelBodyStyle);

            var readyLabel = _readyRequested ? "Unset Ready" : "Set Ready";
            if (DrawFallbackButton(new Rect(panelRect.x + 24f, readyButtonY, panelRect.width - 48f, footerButtonHeight), readyLabel, true))
            {
                _readyRequested = !_readyRequested;
                if (_client != null)
                {
                    _ = _client.SetReadyAsync(_readyRequested, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
                }
            }

            if (hasStartButton)
            {
                if (DrawFallbackButton(new Rect(panelRect.x + 24f, startButtonY, panelRect.width - 48f, footerButtonHeight), "START MATCH", false))
                {
                    _ = StartMatchFromUiAsync();
                }
            }
        }

        private void DrawFallbackHudStrips()
        {
            var topCardWidth = Mathf.Min(Screen.width * 0.34f, 430f);
            var scoreCardWidth = Mathf.Min(Screen.width * 0.34f, 480f);
            var topLeft = new Rect(24f, 24f, topCardWidth, 122f);
            var topRight = new Rect(Screen.width - scoreCardWidth - 24f, 24f, scoreCardWidth, 122f);
            var bottomLeft = new Rect(24f, Screen.height - 132f, Mathf.Min(Screen.width * 0.34f, 440f), 102f);
            var bottomRight = new Rect(Screen.width - Mathf.Min(Screen.width * 0.28f, 320f) - 24f, Screen.height - 132f, Mathf.Min(Screen.width * 0.28f, 320f), 102f);

            DrawHudStrip(topLeft, "MATCH", BuildNetworkAwareMatchLabel());
            DrawHudStrip(topRight, "SCORE", BuildResultsScoreLine());
            DrawHudStrip(bottomLeft, "STATUS", GetLocalPlayer() is { } local ? BuildEffectLabel(local) : "Awaiting feed");
            DrawHudStrip(bottomRight, "COOLDOWN", BuildCooldownCardLabel());
        }

        private void DrawFallbackResultsPanel()
        {
            var width = Mathf.Min(Screen.width * 0.58f, 820f);
            var height = Mathf.Min(Screen.height * 0.62f, 560f);
            var panelRect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            DrawFilledRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.48f));
            DrawPanelChrome(panelRect, new Color(0.05f, 0.09f, 0.16f, 0.96f), new Color(0.34f, 0.82f, 0.96f, 1f));

            GUI.Label(new Rect(panelRect.x + 28f, panelRect.y + 24f, panelRect.width - 56f, 36f), BuildWinnerSummary(), _fallbackPanelTitleStyle);
            GUI.Label(new Rect(panelRect.x + 28f, panelRect.y + 70f, panelRect.width - 56f, 24f), "FINAL SCORE", _fallbackSectionLabelStyle);
            GUI.Label(new Rect(panelRect.x + 28f, panelRect.y + 98f, panelRect.width - 56f, 74f), BuildResultsScoreLine(), _fallbackPanelValueStyle);

            var detailTop = panelRect.y + 188f;
            var buttonHeight = 46f;
            var detailHeight = panelRect.height - (detailTop - panelRect.y) - buttonHeight - 34f;
            GUI.Label(
                new Rect(panelRect.x + 28f, detailTop, panelRect.width - 56f, detailHeight),
                $"State: {FormatValue(_lastServerMessage.RoomState)}\nReason: {FormatValue(_lastServerMessage.EndReason)}\nPersist: {FormatValue(_lastServerMessage.PersistenceStatus)}\n{FormatValue(_lastServerMessage.PersistenceDetail)}\n{BuildLeaderboardSummary()}",
                _fallbackPanelBodyStyle);

            if (DrawFallbackButton(new Rect(panelRect.x + 28f, panelRect.yMax - buttonHeight - 20f, panelRect.width - 56f, buttonHeight), "RETURN TO LOBBY", true))
            {
                _ = ReturnToLobbyFromUiAsync();
            }
        }

        private void DrawFallbackNetworkPanel()
        {
            var width = Mathf.Min(Screen.width * 0.34f, 480f);
            var height = Mathf.Min(Screen.height * 0.30f, 250f);
            var panelRect = IsRoomState("Active")
                ? new Rect(Screen.width - width - 24f, 158f, width, height)
                : new Rect(Screen.width - width - 24f, Screen.height - height - 24f, width, height);
            DrawPanelChrome(panelRect, new Color(0.04f, 0.07f, 0.12f, 0.82f), new Color(0.30f, 0.56f, 0.78f, 0.9f));
            GUI.Label(new Rect(panelRect.x + 18f, panelRect.y + 16f, panelRect.width - 36f, 24f), "NETWORK TELEMETRY", _fallbackSectionLabelStyle);
            GUI.Label(new Rect(panelRect.x + 18f, panelRect.y + 46f, panelRect.width - 36f, 96f), BuildNetworkTelemetrySummary(), _fallbackPanelBodyStyle);
            GUI.Label(new Rect(panelRect.x + 18f, panelRect.y + 150f, panelRect.width - 36f, panelRect.height - 166f), BuildRecentEventsSummary(), _fallbackPanelBodyStyle);
        }

        private NetworkSpikeActiveHudSnapshot BuildActiveHudSnapshot()
        {
            var localPlayer = GetLocalPlayer();
            return new NetworkSpikeActiveHudSnapshot(
                BuildNetworkAwareMatchLabel(),
                BuildResultsScoreLine(),
                localPlayer is null ? "Awaiting feed" : BuildEffectLabel(localPlayer),
                BuildCooldownCardLabel(),
                BuildNetworkTelemetrySummary(),
                BuildRecentEventsSummary());
        }

        private string BuildNetworkAwareMatchLabel() =>
            $"Room {FormatValue(_roomCode)}\nState {FormatValue(_lastServerMessage.RoomState)}\nTime {Mathf.Max(0f, _lastServerMessage.MatchTimeRemainingSeconds):0.0}s";

        private void OnMessageReceived(SpikeServerMessage message)
        {
            if (string.Equals(message.Type, "hello_accepted", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(message.Detail))
            {
                _playerName = message.Detail;
            }

            _lastServerMessage = MergeServerMessage(_lastServerMessage, message);
            if (!string.IsNullOrWhiteSpace(message.RoomCode))
            {
                _roomCode = message.RoomCode;
            }

            RefreshPresentationSnapshot(_lastServerMessage);
            SyncScenePresentation();
            RefreshUiToolkitOverlay();
        }

        private static SpikeServerMessage MergeServerMessage(SpikeServerMessage current, SpikeServerMessage incoming)
        {
            if (incoming == null)
            {
                return current ?? new SpikeServerMessage();
            }

            if (current == null)
            {
                return incoming;
            }

            var authoritativeRoomSnapshot = string.Equals(incoming.Type, "room_snapshot", StringComparison.OrdinalIgnoreCase);
            var carriesRoomListings = authoritativeRoomSnapshot || MessageCarriesRoomListings(incoming.Type);

            return new SpikeServerMessage
            {
                Type = string.IsNullOrWhiteSpace(incoming.Type) ? current.Type : incoming.Type,
                RoomCode = string.IsNullOrWhiteSpace(incoming.RoomCode) ? current.RoomCode : incoming.RoomCode,
                SessionId = string.IsNullOrWhiteSpace(incoming.SessionId) ? current.SessionId : incoming.SessionId,
                Error = string.IsNullOrWhiteSpace(incoming.Error) ? current.Error : incoming.Error,
                Tick = incoming.Tick != 0 ? incoming.Tick : current.Tick,
                Detail = string.IsNullOrWhiteSpace(incoming.Detail) ? current.Detail : incoming.Detail,
                RoomState = string.IsNullOrWhiteSpace(incoming.RoomState) ? current.RoomState : incoming.RoomState,
                HostSessionId = string.IsNullOrWhiteSpace(incoming.HostSessionId) ? current.HostSessionId : incoming.HostSessionId,
                HostPlayerName = string.IsNullOrWhiteSpace(incoming.HostPlayerName) ? current.HostPlayerName : incoming.HostPlayerName,
                PlayerCount = authoritativeRoomSnapshot ? incoming.PlayerCount : (incoming.PlayerCount != 0 ? incoming.PlayerCount : current.PlayerCount),
                ReadyPlayers = authoritativeRoomSnapshot ? incoming.ReadyPlayers : (incoming.ReadyPlayers != 0 ? incoming.ReadyPlayers : current.ReadyPlayers),
                CountdownRemainingSeconds = authoritativeRoomSnapshot ? incoming.CountdownRemainingSeconds : (incoming.CountdownRemainingSeconds > 0f ? incoming.CountdownRemainingSeconds : current.CountdownRemainingSeconds),
                EndReason = string.IsNullOrWhiteSpace(incoming.EndReason) ? current.EndReason : incoming.EndReason,
                PersistenceStatus = string.IsNullOrWhiteSpace(incoming.PersistenceStatus) ? current.PersistenceStatus : incoming.PersistenceStatus,
                PersistenceDetail = string.IsNullOrWhiteSpace(incoming.PersistenceDetail) ? current.PersistenceDetail : incoming.PersistenceDetail,
                Members = authoritativeRoomSnapshot ? (incoming.Members ?? Array.Empty<string>()) : (incoming.Members != null && incoming.Members.Length > 0 ? incoming.Members : current.Members),
                ReadyMembers = authoritativeRoomSnapshot ? (incoming.ReadyMembers ?? Array.Empty<string>()) : (incoming.ReadyMembers != null && incoming.ReadyMembers.Length > 0 ? incoming.ReadyMembers : current.ReadyMembers),
                RoomListings = carriesRoomListings ? (incoming.RoomListings ?? Array.Empty<string>()) : current.RoomListings,
                ActiveBatteryIds = authoritativeRoomSnapshot ? (incoming.ActiveBatteryIds ?? Array.Empty<int>()) : (incoming.ActiveBatteryIds != null && incoming.ActiveBatteryIds.Length > 0 ? incoming.ActiveBatteryIds : current.ActiveBatteryIds),
                BatteryPositions = authoritativeRoomSnapshot ? (incoming.BatteryPositions ?? Array.Empty<string>()) : (incoming.BatteryPositions != null && incoming.BatteryPositions.Length > 0 ? incoming.BatteryPositions : current.BatteryPositions),
                TrapPositions = authoritativeRoomSnapshot ? (incoming.TrapPositions ?? Array.Empty<string>()) : (incoming.TrapPositions != null && incoming.TrapPositions.Length > 0 ? incoming.TrapPositions : current.TrapPositions),
                Scoreboard = authoritativeRoomSnapshot ? (incoming.Scoreboard ?? Array.Empty<string>()) : (incoming.Scoreboard != null && incoming.Scoreboard.Length > 0 ? incoming.Scoreboard : current.Scoreboard),
                LeaderboardRows = authoritativeRoomSnapshot ? (incoming.LeaderboardRows ?? Array.Empty<string>()) : (incoming.LeaderboardRows != null && incoming.LeaderboardRows.Length > 0 ? incoming.LeaderboardRows : current.LeaderboardRows),
                MatchTimeRemainingSeconds = authoritativeRoomSnapshot ? incoming.MatchTimeRemainingSeconds : (incoming.MatchTimeRemainingSeconds > 0f ? incoming.MatchTimeRemainingSeconds : current.MatchTimeRemainingSeconds),
                SlowShotCooldownRemainingSeconds = authoritativeRoomSnapshot ? incoming.SlowShotCooldownRemainingSeconds : (incoming.SlowShotCooldownRemainingSeconds > 0f ? incoming.SlowShotCooldownRemainingSeconds : current.SlowShotCooldownRemainingSeconds),
                EffectStates = authoritativeRoomSnapshot ? (incoming.EffectStates ?? Array.Empty<string>()) : (incoming.EffectStates != null && incoming.EffectStates.Length > 0 ? incoming.EffectStates : current.EffectStates),
                PlayerPositions = authoritativeRoomSnapshot ? (incoming.PlayerPositions ?? Array.Empty<string>()) : (incoming.PlayerPositions != null && incoming.PlayerPositions.Length > 0 ? incoming.PlayerPositions : current.PlayerPositions),
                SlowShotReady = authoritativeRoomSnapshot ? incoming.SlowShotReady : (incoming.SlowShotReady || current.SlowShotReady)
            };
        }

        private async Task PumpHeartbeatAsync()
        {
            try
            {
                if (_client != null)
                {
                    await _client.MaybeSendHeartbeatAsync(GetLifetimeToken());
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Heartbeat failed: {ex.Message}");
            }
        }

        private static Vector2 ReadMoveVector()
        {
            var move = Vector2.zero;
            if (Keyboard.current != null && Keyboard.current.wKey.isPressed) move.y += 1f;
            if (Keyboard.current != null && Keyboard.current.sKey.isPressed) move.y -= 1f;
            if (Keyboard.current != null && Keyboard.current.aKey.isPressed) move.x -= 1f;
            if (Keyboard.current != null && Keyboard.current.dKey.isPressed) move.x += 1f;
            return move.sqrMagnitude > 1f ? move.normalized : move;
        }

        private static Vector2 ReadAimVector()
        {
            if (Mouse.current == null)
            {
                return Vector2.right;
            }

            var mousePosition = Mouse.current.position.ReadValue();
            var anchor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var aim = mousePosition - anchor;
            return aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector2.right;
        }

        private void AppendLog(string message)
        {
            _logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (_logs.Count > 64)
            {
                _logs.RemoveAt(0);
            }

            Debug.Log(message);
        }

        private void DrawArenaPreview(Rect rect)
        {
            DrawFilledRect(rect, new Color(0.07f, 0.09f, 0.14f, 1f));
            DrawArenaGrid(rect);
            DrawTrapPreview(rect);
            DrawBatteryPreview(rect);
            DrawPlayerPreview(rect);
            DrawStateOverlay(rect);
            DrawStatusWidgets(rect);
            DrawHudSummary(rect);
            DrawAbilityHud(rect);
        }

        private void DrawArenaGrid(Rect rect)
        {
            DrawOutline(rect, new Color(0.26f, 0.33f, 0.45f, 1f), 2f);
            var midX = rect.x + (rect.width * 0.5f);
            var midY = rect.y + (rect.height * 0.5f);
            DrawFilledRect(new Rect(midX - 1f, rect.y, 2f, rect.height), new Color(0.18f, 0.22f, 0.3f, 1f));
            DrawFilledRect(new Rect(rect.x, midY - 1f, rect.width, 2f), new Color(0.18f, 0.22f, 0.3f, 1f));

            for (var index = 1; index <= 2; index++)
            {
                var offset = rect.width * 0.5f * (index / ArenaWorldHalfExtent);
                DrawFilledRect(new Rect(midX - offset, rect.y, 1f, rect.height), new Color(0.12f, 0.15f, 0.22f, 1f));
                DrawFilledRect(new Rect(midX + offset, rect.y, 1f, rect.height), new Color(0.12f, 0.15f, 0.22f, 1f));
            }
        }

        private void DrawTrapPreview(Rect rect)
        {
            var trapPositions = _trapPositionsById.Count > 0
                ? _trapPositionsById.OrderBy(pair => pair.Key).Select(pair => pair.Value)
                : TrapPreviewPositions;
            foreach (var trapPosition in trapPositions)
            {
                var markerRect = BuildMarkerRect(rect, trapPosition, 22f);
                DrawFilledRect(markerRect, new Color(0.78f, 0.21f, 0.28f, 0.45f));
                DrawOutline(markerRect, new Color(1f, 0.45f, 0.5f, 0.95f), 2f);
                GUI.Label(new Rect(markerRect.xMax + 4f, markerRect.y - 1f, 70f, 18f), "Trap");
            }
        }

        private void DrawBatteryPreview(Rect rect)
        {
            foreach (var batteryId in _activeBatteryIds)
            {
                if (!_batteryPositionsById.TryGetValue(batteryId, out var batteryPosition))
                {
                    continue;
                }

                var markerRect = BuildMarkerRect(rect, batteryPosition, 16f);
                DrawFilledRect(markerRect, new Color(1f, 0.83f, 0.18f, 0.95f));
                DrawOutline(markerRect, new Color(1f, 0.95f, 0.62f, 1f), 2f);
            }
        }

        private void DrawPlayerPreview(Rect rect)
        {
            for (var index = 0; index < _playerVisuals.Count; index++)
            {
                var player = _playerVisuals[index];
                var isLocalPlayer = string.Equals(player.Name, GetEffectiveLocalPlayerName(), StringComparison.Ordinal);
                var bodyColor = isLocalPlayer ? new Color(0.33f, 0.9f, 0.53f, 1f) : new Color(0.33f, 0.7f, 1f, 1f);
                var bodyRect = BuildMarkerRect(rect, player.Position, 18f);

                DrawFilledRect(bodyRect, bodyColor);
                DrawOutline(bodyRect, Color.white, 2f);

                if (player.IsDebuffed)
                {
                    var debuffRect = bodyRect;
                    debuffRect.x -= 4f;
                    debuffRect.y -= 4f;
                    debuffRect.width += 8f;
                    debuffRect.height += 8f;
                    DrawOutline(debuffRect, new Color(1f, 0.46f, 0.2f, 0.95f), 2f);
                }

                if (player.ImmunityRemainingSeconds > 0.05f)
                {
                    var immuneRect = bodyRect;
                    immuneRect.x -= 8f;
                    immuneRect.y -= 8f;
                    immuneRect.width += 16f;
                    immuneRect.height += 16f;
                    DrawOutline(immuneRect, new Color(0.97f, 0.96f, 0.45f, 0.95f), 1.5f);
                }

                var labelRect = new Rect(bodyRect.x - 20f, bodyRect.yMax + 2f, 90f, 36f);
                GUI.Label(labelRect, $"{player.Name}\nScore {player.Score}");
            }
        }

        private void DrawStateOverlay(Rect rect)
        {
            EnsureOverlayStyles();

            if (IsRoomState("Countdown"))
            {
                var countdownText = Mathf.CeilToInt(Mathf.Max(0.1f, _lastServerMessage.CountdownRemainingSeconds)).ToString(CultureInfo.InvariantCulture);
                GUI.Label(new Rect(rect.x, rect.y + 12f, rect.width, 30f), $"Match starts in {countdownText}", _overlayTitleStyle);
                GUI.Label(new Rect(rect.x, rect.y + 40f, rect.width, 20f), "Authoritative countdown from the server", _overlaySubStyle);
            }
            else if (IsRoomState("Lobby"))
            {
                GUI.Label(new Rect(rect.x, rect.y + 12f, rect.width, 24f), "Lobby preview", _overlayTitleStyle);
                GUI.Label(new Rect(rect.x, rect.y + 36f, rect.width, 18f), "Connect both players, then set Ready to begin.", _overlaySubStyle);
            }

            if (IsAnyRoomState("Ended", "Saving", "ResultsReady"))
            {
                var winnerLine = BuildWinnerSummary();
                var resultRect = new Rect(rect.x + rect.width - 238f, rect.y + 14f, 224f, 104f);
                DrawFilledRect(resultRect, new Color(0.12f, 0.16f, 0.23f, 0.94f));
                DrawOutline(resultRect, new Color(0.52f, 0.67f, 0.87f, 1f), 2f);
                GUI.Label(new Rect(resultRect.x + 10f, resultRect.y + 10f, resultRect.width - 20f, 22f), winnerLine, _overlayTitleStyle);
                GUI.Label(new Rect(resultRect.x + 10f, resultRect.y + 40f, resultRect.width - 20f, 18f), $"Reason: {FormatValue(_lastServerMessage.EndReason)}", _overlaySubStyle);
                GUI.Label(new Rect(resultRect.x + 10f, resultRect.y + 60f, resultRect.width - 20f, 18f), $"Persist: {FormatValue(_lastServerMessage.PersistenceStatus)}", _overlaySubStyle);
                GUI.Label(new Rect(resultRect.x + 10f, resultRect.y + 78f, resultRect.width - 20f, 18f), $"State: {FormatValue(_lastServerMessage.RoomState)}", _overlaySubStyle);
            }

            if (IsRoomState("Active") && _playerVisuals.Count > 0)
            {
                var leader = _playerVisuals.OrderByDescending(player => player.Score).ThenBy(player => player.Name, StringComparer.Ordinal).First();
                if (leader.Score >= 9)
                {
                    var bannerRect = new Rect(rect.x + (rect.width * 0.5f) - 140f, rect.y + 12f, 280f, 28f);
                    DrawFilledRect(bannerRect, new Color(0.58f, 0.18f, 0.21f, 0.85f));
                    DrawOutline(bannerRect, new Color(1f, 0.72f, 0.72f, 1f), 1.5f);
                    GUI.Label(bannerRect, $"{leader.Name} is on match point!", _overlayTitleStyle);
                }
            }
        }

        private void DrawHudSummary(Rect rect)
        {
            var summaryRect = new Rect(rect.x + 12f, rect.yMax - 80f, rect.width - 24f, 68f);
            DrawFilledRect(summaryRect, new Color(0.09f, 0.12f, 0.18f, 0.82f));
            DrawOutline(summaryRect, new Color(0.24f, 0.32f, 0.45f, 1f), 1f);

            var topRow = new Rect(summaryRect.x + 12f, summaryRect.y + 8f, summaryRect.width - 24f, 22f);
            GUI.Label(topRow, $"Room {FormatValue(_roomCode)} · State {FormatValue(_lastServerMessage.RoomState)} · Timer {_lastServerMessage.MatchTimeRemainingSeconds:F1}s");

            var localPlayer = GetLocalPlayer();
            var opponent = _playerVisuals.FirstOrDefault(player => !string.Equals(player.Name, _playerName, StringComparison.Ordinal))
                           ?? (_playerVisuals.Count > 1 ? _playerVisuals[1] : null);

            var bottomText = localPlayer is null
                ? "Waiting for authoritative player snapshots."
                : $"Local {localPlayer.Name}: score {localPlayer.Score}, status {BuildPlayerStatus(localPlayer)}"
                  + $" · Slow shot {BuildCooldownLabel()}"
                  + (opponent is not null ? $" · Rival {opponent.Name}: score {opponent.Score}, status {BuildPlayerStatus(opponent)}" : string.Empty);
            GUI.Label(new Rect(summaryRect.x + 12f, summaryRect.y + 32f, summaryRect.width - 24f, 28f), bottomText);
        }

        private void DrawStatusWidgets(Rect rect)
        {
            var localPlayer = GetLocalPlayer();
            var cooldownRect = new Rect(rect.x + 12f, rect.y + 12f, HudCardWidth, HudCardHeight);
            DrawHudCard(
                cooldownRect,
                "Cooldown",
                BuildCooldownCardLabel(),
                GetCooldownAccentColor());

            if (localPlayer is null)
            {
                return;
            }

            var effectRect = new Rect(cooldownRect.xMax + HudCardGap, rect.y + 12f, HudCardWidth + 20f, HudCardHeight);
            DrawHudCard(
                effectRect,
                "Effect",
                BuildEffectLabel(localPlayer),
                GetEffectAccentColor(localPlayer));

            var fill = GetEffectFill(localPlayer);
            if (fill <= 0f)
            {
                return;
            }

            var barRect = new Rect(effectRect.x + 12f, effectRect.yMax - 12f, effectRect.width - 24f, 6f);
            DrawFilledRect(barRect, new Color(1f, 1f, 1f, 0.1f));
            DrawFilledRect(new Rect(barRect.x, barRect.y, barRect.width * fill, barRect.height),
                GetEffectBarColor(localPlayer));
        }

        private void DrawAbilityHud(Rect rect)
        {
            var localPlayer = GetLocalPlayer();
            var panelRect = new Rect(rect.x + rect.width - (AbilityPanelWidth + HudCardGap), rect.yMax - 154f, AbilityPanelWidth, AbilityPanelHeight);
            DrawFilledRect(panelRect, new Color(0.07f, 0.1f, 0.16f, 0.9f));
            DrawOutline(panelRect, new Color(0.31f, 0.42f, 0.58f, 1f), 1.5f);

            GUI.Label(new Rect(panelRect.x + 10f, panelRect.y + 8f, panelRect.width - 20f, 18f), "Local Ability HUD");
            DrawStatusPill(
                new Rect(panelRect.x + 10f, panelRect.y + 30f, AbilityPillWidth, AbilityPillHeight),
                $"Slow {BuildCooldownLabel()}",
                GetCooldownAccentColor(0.95f));

            DrawStatusPill(
                new Rect(panelRect.x + 10f + AbilityPillWidth + 4f, panelRect.y + 30f, AbilityPillWidth, AbilityPillHeight),
                localPlayer is null ? "Awaiting feed" : BuildEffectLabel(localPlayer),
                localPlayer is null ? new Color(0.24f, 0.4f, 0.66f, 0.95f) : GetEffectAccentColor(localPlayer, 0.95f));
        }

        private void RefreshPresentationSnapshot(SpikeServerMessage message)
        {
            _activeBatteryIds = message.ActiveBatteryIds != null ? (int[])message.ActiveBatteryIds.Clone() : Array.Empty<int>();
            _batteryPositionsById.Clear();
            foreach (var pair in ParseBatteryPositions(message.BatteryPositions))
            {
                _batteryPositionsById[pair.Key] = pair.Value;
            }
            _trapPositionsById.Clear();
            foreach (var pair in ParseTrapPositions(message.TrapPositions))
            {
                _trapPositionsById[pair.Key] = pair.Value;
            }
            var scoresByName = ParseScores(message.Scoreboard);
            var positionsByName = ParsePositions(message.PlayerPositions);
            var effectsByName = ParseEffects(message.EffectStates);
            var visibleMembers = IsRoomState("Active")
                ? message.Members
                : (message.ReadyMembers != null && message.ReadyMembers.Length > 0 ? message.ReadyMembers : Array.Empty<string>());
            _playerVisuals.Clear();
            _playerVisuals.AddRange(BuildPlayerVisuals(visibleMembers, scoresByName, positionsByName, effectsByName));
        }

        public void ApplyAuthoritativeSnapshotForTesting(SpikeServerMessage message)
        {
            _lastServerMessage = message;
            if (!string.IsNullOrWhiteSpace(message.RoomCode))
            {
                _roomCode = message.RoomCode;
            }
            RefreshPresentationSnapshot(message);
            SyncScenePresentation();
            RefreshUiToolkitOverlay();
        }

        public void ReceiveServerMessageForTesting(SpikeServerMessage message) => OnMessageReceived(message);

        public int ScenePlayerActorCountForTesting => _scenePlayerRenderers.Count(pair => pair.Value != null && pair.Value.gameObject.activeSelf);

        public int SceneBatteryActorCountForTesting => _batterySceneRenderers.Count(renderer => renderer != null && renderer.gameObject.activeSelf);

        public int SceneTrapActorCountForTesting => _trapSceneRenderers.Count(renderer => renderer != null && renderer.gameObject.activeSelf);

        public Vector3 GetPlayerScenePositionForTesting(string playerName) =>
            _scenePlayerRenderers.TryGetValue(playerName, out var renderer) && renderer != null
                ? renderer.transform.position
                : Vector3.zero;

        public bool ToolkitHudVisibleForTesting => _activeHudOverlay != null && _activeHudOverlay.style.display == DisplayStyle.Flex;

        public bool ToolkitResultsVisibleForTesting => _resultsOverlay != null && _resultsOverlay.style.display == DisplayStyle.Flex;

        public bool ToolkitPreMatchVisibleForTesting => _preMatchOverlay != null && _preMatchOverlay.style.display == DisplayStyle.Flex;

        public bool ToolkitCountdownVisibleForTesting => _toolkitCountdownLabel != null && _toolkitCountdownLabel.style.display == DisplayStyle.Flex;

        public bool ToolkitOverlayBuiltForTesting => _uiRoot != null;

        public bool ToolkitUsesAuthoredAssetsForTesting => _toolkitUsesAuthoredAssets;

        public string ToolkitPreMatchTitleForTesting => _toolkitPreMatchTitleLabel?.text ?? string.Empty;

        public string ToolkitPreMatchMembersForTesting => _toolkitPreMatchMembersLabel?.text ?? string.Empty;

        public string ToolkitPreMatchRoomsForTesting => _toolkitPreMatchRoomsLabel?.text ?? string.Empty;

        public string ToolkitNetworkTelemetryForTesting => _toolkitNetworkTelemetryLabel?.text ?? BuildNetworkTelemetrySummary();

        public string ToolkitNetworkEventsForTesting => _toolkitNetworkEventsLabel?.text ?? BuildRecentEventsSummary();

        private string BuildWinnerSummary()
        {
            if (_playerVisuals.Count == 0)
            {
                return "Waiting for results";
            }

            if (_playerVisuals.Count == 1)
            {
                return $"{_playerVisuals[0].Name} wins";
            }

            var orderedPlayers = _playerVisuals
                .OrderByDescending(player => player.Score)
                .ThenBy(player => player.Name, StringComparer.Ordinal)
                .ToArray();

            return orderedPlayers[0].Score == orderedPlayers[1].Score
                ? "Draw"
                : $"{orderedPlayers[0].Name} wins";
        }

        private static string BuildPlayerStatus(PlayerVisualState player)
        {
            if (player.IsDebuffed)
            {
                return $"{player.EffectSource} ×{player.MoveMultiplier:0.00} ({player.EffectRemainingSeconds:0.0}s)";
            }

            if (player.ImmunityRemainingSeconds > 0.05f)
            {
                return $"Immune ({player.ImmunityRemainingSeconds:0.0}s)";
            }

            return "Clear";
        }

        private string BuildCooldownLabel() =>
            _lastServerMessage.SlowShotReady
                ? "READY"
                : $"{Mathf.Max(0f, _lastServerMessage.SlowShotCooldownRemainingSeconds):0.0}s";

        private string BuildCooldownCardLabel() =>
            _lastServerMessage.SlowShotReady
                ? "Slow shot ready"
                : $"Slow shot cooldown {BuildCooldownLabel()}";

        private string BuildResultsScoreLine()
        {
            if (_playerVisuals.Count == 0)
            {
                return "Score: awaiting data";
            }

            return string.Join("\n", _playerVisuals.Select(player => $"{AbbreviateValue(player.Name, 14)}  {player.Score}"));
        }

        private string BuildLeaderboardSummary()
        {
            if (_lastServerMessage.LeaderboardRows == null || _lastServerMessage.LeaderboardRows.Length == 0)
            {
                return "Leaderboard: waiting for database results";
            }

            return "Leaderboard:\n- " + string.Join("\n- ", _lastServerMessage.LeaderboardRows);
        }

        private static string BuildEffectLabel(PlayerVisualState player)
        {
            if (player.IsDebuffed)
            {
                return $"{player.EffectSource} {player.EffectRemainingSeconds:0.0}s";
            }

            if (player.ImmunityRemainingSeconds > 0.05f)
            {
                return $"Immune {player.ImmunityRemainingSeconds:0.0}s";
            }

            return "Clear";
        }

        private PlayerVisualState GetLocalPlayer() =>
            _playerVisuals.FirstOrDefault(player => string.Equals(player.Name, GetEffectiveLocalPlayerName(), StringComparison.Ordinal));

        private string GetEffectiveLocalPlayerName()
        {
            if (_client != null && _client.IsConnected && !string.IsNullOrWhiteSpace(_client.ConnectedPlayerName))
            {
                return _client.ConnectedPlayerName;
            }

            return _playerName;
        }

        private Color GetCooldownAccentColor(float alpha = 1f) =>
            _lastServerMessage.SlowShotReady ? new Color(0.24f, 0.62f, 0.34f, alpha) : new Color(0.8f, 0.39f, 0.14f, alpha);

        private static Color GetEffectAccentColor(PlayerVisualState player, float alpha = 1f)
        {
            if (player.IsDebuffed)
            {
                return new Color(0.84f, 0.33f, 0.22f, alpha);
            }

            return player.ImmunityRemainingSeconds > 0.05f
                ? new Color(0.82f, 0.72f, 0.2f, alpha)
                : new Color(0.25f, 0.43f, 0.66f, alpha);
        }

        private static Color GetEffectBarColor(PlayerVisualState player) =>
            player.IsDebuffed ? new Color(1f, 0.52f, 0.27f, 1f) : new Color(1f, 0.93f, 0.42f, 1f);

        private static float GetEffectFill(PlayerVisualState player) =>
            player.IsDebuffed
                ? Mathf.Clamp01(player.EffectRemainingSeconds / 1.25f)
                : Mathf.Clamp01(player.ImmunityRemainingSeconds / 0.5f);

        private static string FormatValue(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

        private static Dictionary<string, int> ParseScores(string[] scoreboardEntries)
        {
            var scoresByName = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in scoreboardEntries ?? Array.Empty<string>())
            {
                var separator = entry.IndexOf(':');
                if (separator <= 0 || separator >= entry.Length - 1)
                {
                    continue;
                }

                if (int.TryParse(entry[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var score))
                {
                    scoresByName[entry[..separator]] = score;
                }
            }

            return scoresByName;
        }

        private static Dictionary<string, Vector2> ParsePositions(string[] positionEntries)
        {
            var positionsByName = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            foreach (var entry in positionEntries ?? Array.Empty<string>())
            {
                var parts = entry.Split(':');
                if (parts.Length < 3)
                {
                    continue;
                }

                if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    positionsByName[parts[0]] = new Vector2(x, y);
                }
            }

            return positionsByName;
        }

        private static Dictionary<int, Vector2> ParseBatteryPositions(string[] positionEntries)
        {
            return ParseIndexedPositions(positionEntries);
        }

        private static Dictionary<int, Vector2> ParseTrapPositions(string[] positionEntries)
        {
            return ParseIndexedPositions(positionEntries);
        }

        private static Dictionary<int, Vector2> ParseIndexedPositions(string[] positionEntries)
        {
            var positionsById = new Dictionary<int, Vector2>();
            foreach (var entry in positionEntries ?? Array.Empty<string>())
            {
                var parts = entry.Split(':');
                if (parts.Length < 3)
                {
                    continue;
                }

                if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    positionsById[id] = new Vector2(x, y);
                }
            }

            return positionsById;
        }

        private static Dictionary<string, EffectVisualState> ParseEffects(string[] effectEntries)
        {
            var effectsByName = new Dictionary<string, EffectVisualState>(StringComparer.Ordinal);
            foreach (var entry in effectEntries ?? Array.Empty<string>())
            {
                var parts = entry.Split(':');
                if (parts.Length < 5)
                {
                    continue;
                }

                effectsByName[parts[0]] = new EffectVisualState
                {
                    MoveMultiplier = ParseFloat(parts[1], 1f),
                    Source = parts[2],
                    RemainingSeconds = ParseFloat(parts[3], 0f),
                    ImmunityRemainingSeconds = ParseFloat(parts[4], 0f)
                };
            }

            return effectsByName;
        }

        private static IEnumerable<PlayerVisualState> BuildPlayerVisuals(
            string[] members,
            IReadOnlyDictionary<string, int> scoresByName,
            IReadOnlyDictionary<string, Vector2> positionsByName,
            IReadOnlyDictionary<string, EffectVisualState> effectsByName)
        {
            foreach (var member in members ?? Array.Empty<string>())
            {
                effectsByName.TryGetValue(member, out var effectState);
                positionsByName.TryGetValue(member, out var position);
                scoresByName.TryGetValue(member, out var score);
                yield return new PlayerVisualState
                {
                    Name = member,
                    Position = position,
                    Score = score,
                    MoveMultiplier = effectState?.MoveMultiplier ?? 1f,
                    EffectSource = effectState?.Source ?? string.Empty,
                    EffectRemainingSeconds = effectState?.RemainingSeconds ?? 0f,
                    ImmunityRemainingSeconds = effectState?.ImmunityRemainingSeconds ?? 0f
                };
            }
        }

        private static float ParseFloat(string text, float fallback) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

        private void EnsureScenePresentation()
        {
            if (_sceneRoot != null)
            {
                return;
            }

            _sceneRoot = GameObject.Find("NetworkSpikeScenePresentation")?.transform;
            if (_sceneRoot == null)
            {
                _sceneRoot = new GameObject("NetworkSpikeScenePresentation").transform;
            }

            _arenaSurfaceRenderer = EnsureSceneSprite(
                "ArenaSurface",
                _sceneRoot,
                Vector2.zero,
                new Vector3((ArenaWorldHalfExtent * 2f) + 0.8f, (ArenaWorldHalfExtent * 2f) + 0.8f, 1f),
                new Color(0.06f, 0.08f, 0.12f, 0.92f),
                -10);

            _batterySceneRenderers = Enumerable.Range(0, 8)
                .Select(index => EnsureSceneSprite(
                    $"Battery-{index + 1}",
                    _sceneRoot,
                    Vector2.zero,
                    new Vector3(0.35f, 0.35f, 1f),
                    new Color(1f, 0.83f, 0.18f, 1f),
                    20))
                .ToArray();

            _trapSceneRenderers = TrapPreviewPositions
                .Select((position, index) => EnsureSceneSprite(
                    $"Trap-{index + 1}",
                    _sceneRoot,
                    position,
                    new Vector3(0.9f, 0.9f, 1f),
                    new Color(0.8f, 0.24f, 0.28f, 0.35f),
                    5))
                .ToArray();
        }

        private void SyncScenePresentation()
        {
            EnsureScenePresentation();
            SyncBatterySceneActors();
            SyncTrapSceneActors();
            SyncPlayerSceneActors();
        }

        private void SyncBatterySceneActors()
        {
            var activeIds = new HashSet<int>(_activeBatteryIds);
            for (var index = 0; index < _batterySceneRenderers.Length; index++)
            {
                if (_batterySceneRenderers[index] == null)
                {
                    continue;
                }

                var batteryId = index + 1;
                var hasPosition = _batteryPositionsById.TryGetValue(batteryId, out var batteryPosition);
                var isActive = activeIds.Contains(batteryId) && hasPosition;
                _batterySceneRenderers[index].gameObject.SetActive(isActive);
                if (isActive)
                {
                    _batterySceneRenderers[index].transform.position = new Vector3(batteryPosition.x, batteryPosition.y, 0f);
                }
            }
        }

        private void SyncTrapSceneActors()
        {
            for (var index = 0; index < _trapSceneRenderers.Length; index++)
            {
                var renderer = _trapSceneRenderers[index];
                if (renderer != null)
                {
                    var trapId = index + 1;
                    var isActive = _trapPositionsById.TryGetValue(trapId, out var trapPosition);
                    renderer.gameObject.SetActive(isActive);
                    if (isActive)
                    {
                        renderer.transform.position = new Vector3(trapPosition.x, trapPosition.y, 0f);
                    }
                }
            }
        }

        private void SyncPlayerSceneActors()
        {
            var activePlayers = new HashSet<string>(_playerVisuals.Select(player => player.Name), StringComparer.Ordinal);
            foreach (var stale in _scenePlayerRenderers.Keys.Where(name => !activePlayers.Contains(name)).ToArray())
            {
                if (_scenePlayerRenderers[stale] != null)
                {
                    _scenePlayerRenderers[stale].gameObject.SetActive(false);
                }
            }

            foreach (var player in _playerVisuals)
            {
                if (!_scenePlayerRenderers.TryGetValue(player.Name, out var renderer) || renderer == null)
                {
                    renderer = EnsureSceneSprite(
                        $"{player.Name}-Actor",
                        _sceneRoot,
                        player.Position,
                        new Vector3(0.55f, 0.55f, 1f),
                        Color.white,
                        30);
                    _scenePlayerRenderers[player.Name] = renderer;
                }

                renderer.gameObject.SetActive(true);
                renderer.transform.position = new Vector3(player.Position.x, player.Position.y, 0f);
                renderer.color = BuildPlayerSceneColor(player);
            }
        }

        private void ConfigureCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            camera.orthographic = true;
            camera.orthographicSize = ArenaWorldHalfExtent + 0.7f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.backgroundColor = new Color(0.03f, 0.05f, 0.08f, 1f);
        }

        private void UpdateCameraFollow()
        {
            var camera = Camera.main;
            var localPlayer = GetLocalPlayer();
            if (camera == null || localPlayer == null)
            {
                return;
            }

            var target = new Vector3(localPlayer.Position.x, localPlayer.Position.y, -10f);
            camera.transform.position = Vector3.Lerp(camera.transform.position, target, Time.deltaTime * CameraFollowSpeed);
        }

        private void EnsureUiToolkitOverlay()
        {
            if (_uiOverlayRoot != null)
            {
                Destroy(_uiOverlayRoot.gameObject);
                _uiOverlayRoot = null;
            }

            if (_panelSettings != null)
            {
                Destroy(_panelSettings);
                _panelSettings = null;
            }

            _uiRoot = null;
            _preMatchOverlay = null;
            _activeHudOverlay = null;
            _resultsOverlay = null;
            _toolkitPreMatchTitleLabel = null;
            _toolkitPreMatchSummaryLabel = null;
            _toolkitPlayerNameField = null;
            _toolkitRoomCodeField = null;
            _toolkitConnectButton = null;
            _toolkitCreateButton = null;
            _toolkitJoinButton = null;
            _toolkitPreMatchMembersLabel = null;
            _toolkitPreMatchRoomsLabel = null;
            _toolkitReadyButton = null;
            _toolkitCountdownLabel = null;
            _toolkitTopLabel = null;
            _toolkitScoreLabel = null;
            _toolkitCooldownLabel = null;
            _toolkitEffectLabel = null;
            _toolkitNetworkPanel = null;
            _toolkitNetworkTelemetryLabel = null;
            _toolkitNetworkEventsLabel = null;
            _toolkitResultsTitleLabel = null;
            _toolkitResultsScoreLabel = null;
            _toolkitResultsDetailLabel = null;
            _toolkitUsesAuthoredAssets = false;
        }

        private void BuildUiToolkitOverlay()
        {
            _toolkitOverlayAsset ??= Resources.Load<VisualTreeAsset>(ToolkitOverlayResourcePath);
            _toolkitOverlayStyleSheet ??= Resources.Load<StyleSheet>(ToolkitOverlayResourcePath);
            _toolkitUsesAuthoredAssets = _toolkitOverlayAsset != null;

            if (_toolkitUsesAuthoredAssets)
            {
                BuildAuthoredUiToolkitOverlay();
                return;
            }

            BuildProceduralUiToolkitOverlay();
        }

        private void BuildAuthoredUiToolkitOverlay()
        {
            _uiRoot.Clear();
            _uiRoot.style.flexGrow = 1f;
            _uiRoot.pickingMode = PickingMode.Position;
            if (_toolkitOverlayStyleSheet != null && !_uiRoot.styleSheets.Contains(_toolkitOverlayStyleSheet))
            {
                _uiRoot.styleSheets.Add(_toolkitOverlayStyleSheet);
            }

            _toolkitOverlayAsset.CloneTree(_uiRoot);
            ApplyToolkitFontDefaults();
            _preMatchOverlay = _uiRoot.Q<VisualElement>("prematch-overlay");
            _activeHudOverlay = _uiRoot.Q<VisualElement>("active-hud-overlay");
            _resultsOverlay = _uiRoot.Q<VisualElement>("results-overlay");
            _toolkitPreMatchTitleLabel = _uiRoot.Q<Label>("prematch-title");
            _toolkitPreMatchSummaryLabel = _uiRoot.Q<Label>("prematch-summary");
            _toolkitPlayerNameField = _uiRoot.Q<TextField>("player-name-field");
            _toolkitRoomCodeField = _uiRoot.Q<TextField>("room-code-field");
            _toolkitConnectButton = _uiRoot.Q<Button>("connect-button");
            _toolkitCreateButton = _uiRoot.Q<Button>("create-button");
            _toolkitJoinButton = _uiRoot.Q<Button>("join-button");
            _toolkitPreMatchMembersLabel = _uiRoot.Q<Label>("prematch-members");
            _toolkitPreMatchRoomsLabel = _uiRoot.Q<Label>("prematch-rooms");
            _toolkitReadyButton = _uiRoot.Q<Button>("ready-button");
            _toolkitCountdownLabel = _uiRoot.Q<Label>("countdown-label");
            _toolkitTopLabel = _uiRoot.Q<Label>("toolkit-top-label");
            _toolkitScoreLabel = _uiRoot.Q<Label>("toolkit-score-label");
            _toolkitCooldownLabel = _uiRoot.Q<Label>("toolkit-cooldown-label");
            _toolkitEffectLabel = _uiRoot.Q<Label>("toolkit-effect-label");
            _toolkitNetworkPanel = _uiRoot.Q<VisualElement>("network-panel");
            _toolkitNetworkTelemetryLabel = _uiRoot.Q<Label>("network-telemetry-label");
            _toolkitNetworkEventsLabel = _uiRoot.Q<Label>("network-events-label");
            _toolkitResultsTitleLabel = _uiRoot.Q<Label>("toolkit-results-title-label");
            _toolkitResultsScoreLabel = _uiRoot.Q<Label>("toolkit-results-score-label");
            _toolkitResultsDetailLabel = _uiRoot.Q<Label>("toolkit-results-detail-label");

            _toolkitPlayerNameField?.RegisterValueChangedCallback(evt =>
            {
                if (!_suppressToolkitFieldCallbacks)
                {
                    _playerName = evt.newValue;
                }
            });
            _toolkitRoomCodeField?.RegisterValueChangedCallback(evt =>
            {
                if (!_suppressToolkitFieldCallbacks)
                {
                    _roomCode = evt.newValue;
                }
            });
            if (_toolkitConnectButton != null)
            {
                _toolkitConnectButton.clicked += () =>
                {
                    _ = ConnectFromUiAsync();
                };
            }
            if (_toolkitCreateButton != null)
            {
                _toolkitCreateButton.clicked += () =>
                {
                    _ = CreateRoomFromUiAsync();
                };
            }
            if (_toolkitJoinButton != null)
            {
                _toolkitJoinButton.clicked += () =>
                {
                    _ = JoinRoomFromUiAsync();
                };
            }
            if (_toolkitReadyButton != null)
            {
                _toolkitReadyButton.clicked += () =>
                {
                    _readyRequested = !_readyRequested;
                    if (_client != null)
                    {
                        _ = _client.SetReadyAsync(_readyRequested, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
                    }
                };
            }
        }

        private void BuildProceduralUiToolkitOverlay()
        {
            _toolkitUsesAuthoredAssets = false;
        }

        private void ApplyToolkitFontDefaults()
        {
            if (_uiRoot == null)
            {
                return;
            }

            _fallbackToolkitFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _fallbackToolkitFont ??= Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_fallbackToolkitFont == null)
            {
                return;
            }

            var fontDefinition = FontDefinition.FromFont(_fallbackToolkitFont);
            _uiRoot.style.unityFont = _fallbackToolkitFont;
            _uiRoot.style.unityFontDefinition = fontDefinition;

            foreach (var textElement in _uiRoot.Query<TextElement>().ToList())
            {
                textElement.style.unityFont = _fallbackToolkitFont;
                textElement.style.unityFontDefinition = fontDefinition;
            }
        }

        private ThemeStyleSheet LoadToolkitThemeStyleSheet()
        {
            _toolkitThemeStyleSheet ??= Resources.Load<ThemeStyleSheet>(ToolkitThemeResourcePath);
            return _toolkitThemeStyleSheet;
        }

        private void RefreshUiToolkitOverlay()
        {
            if (_uiRoot == null)
            {
                return;
            }

            var localPlayer = GetLocalPlayer();
            var opponent = _playerVisuals.FirstOrDefault(player => !string.Equals(player.Name, GetEffectiveLocalPlayerName(), StringComparison.Ordinal));
            var preMatchVisible = !IsAnyRoomState("Active", "Ended", "Saving", "ResultsReady");

            _preMatchOverlay.style.display = preMatchVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _activeHudOverlay.style.display = IsRoomState("Active") ? DisplayStyle.Flex : DisplayStyle.None;
            _resultsOverlay.style.display = IsAnyRoomState("Ended", "Saving", "ResultsReady") ? DisplayStyle.Flex : DisplayStyle.None;

            _suppressToolkitFieldCallbacks = true;
            _toolkitPlayerNameField.value = _playerName;
            _toolkitRoomCodeField.value = NormalizeRoomCode(_roomCode);
            _suppressToolkitFieldCallbacks = false;

            _toolkitPreMatchTitleLabel.text = BuildPreMatchTitle();
            _toolkitPreMatchSummaryLabel.text = BuildPreMatchSummary();
            _toolkitPreMatchMembersLabel.text = BuildLobbyMembersSummary();
            if (_toolkitPreMatchRoomsLabel != null)
            {
                _toolkitPreMatchRoomsLabel.text = BuildRoomListingsSummary();
            }
            _toolkitReadyButton.text = _readyRequested ? "Unset Ready" : "Set Ready";
            var isConnected = _client != null && _client.IsConnected;
            var normalizedRoomCode = NormalizeRoomCode(_roomCode);
            var hasJoinedRoomSession = isConnected && IsAnyRoomState("Lobby", "Countdown", "Active", "Ended", "Saving", "ResultsReady");
            var canUsePreMatchActions = !hasJoinedRoomSession && !IsRoomState("Countdown") && !IsAnyRoomState("Active", "Ended", "Saving", "ResultsReady");
            _toolkitPlayerNameField?.SetEnabled(!hasJoinedRoomSession);
            _toolkitRoomCodeField?.SetEnabled(canUsePreMatchActions);
            _toolkitReadyButton.SetEnabled(isConnected && IsAnyRoomState("Lobby", "Countdown"));
            _toolkitConnectButton.SetEnabled(!isConnected);
            _toolkitCreateButton.SetEnabled(canUsePreMatchActions);
            _toolkitJoinButton.SetEnabled(canUsePreMatchActions && !string.IsNullOrWhiteSpace(normalizedRoomCode));
            _toolkitCountdownLabel.style.display = IsRoomState("Countdown") ? DisplayStyle.Flex : DisplayStyle.None;
            _toolkitCountdownLabel.text = IsRoomState("Countdown")
                ? $"Match starts in {Mathf.CeilToInt(Mathf.Max(0.1f, _lastServerMessage.CountdownRemainingSeconds))}"
                : string.Empty;

            if (_toolkitTopLabel != null)
            {
                _toolkitTopLabel.text = $"Room {FormatValue(_roomCode)}\nTime {_lastServerMessage.MatchTimeRemainingSeconds:0.0}s";
            }

            if (_toolkitScoreLabel != null)
            {
                _toolkitScoreLabel.text = localPlayer is null
                    ? "Waiting for players"
                    : $"{localPlayer.Name} {localPlayer.Score} : {(opponent?.Score ?? 0)} {opponent?.Name ?? "—"}";
            }

            if (_toolkitCooldownLabel != null)
            {
                _toolkitCooldownLabel.text = BuildCooldownCardLabel();
            }

            if (_toolkitEffectLabel != null)
            {
                _toolkitEffectLabel.text = localPlayer is null ? "Awaiting feed" : BuildEffectLabel(localPlayer);
            }

            if (_toolkitNetworkPanel != null)
            {
                _toolkitNetworkPanel.style.display = DisplayStyle.Flex;
            }

            if (_toolkitNetworkTelemetryLabel != null)
            {
                _toolkitNetworkTelemetryLabel.text = BuildNetworkTelemetrySummary();
            }

            if (_toolkitNetworkEventsLabel != null)
            {
                _toolkitNetworkEventsLabel.text = BuildRecentEventsSummary();
            }

            if (_toolkitResultsTitleLabel != null)
            {
                _toolkitResultsTitleLabel.text = BuildWinnerSummary();
            }

            if (_toolkitResultsScoreLabel != null)
            {
                _toolkitResultsScoreLabel.text = BuildResultsScoreLine();
            }

            if (_toolkitResultsDetailLabel != null)
            {
                _toolkitResultsDetailLabel.text =
                    $"State: {FormatValue(_lastServerMessage.RoomState)}\n" +
                    $"Reason: {FormatValue(_lastServerMessage.EndReason)}\n" +
                    $"Persist: {FormatValue(_lastServerMessage.PersistenceStatus)}\n" +
                    $"{FormatValue(_lastServerMessage.PersistenceDetail)}\n" +
                    $"{BuildLeaderboardSummary()}";
            }
        }

        private static VisualElement CreateToolkitCard(float left, float top, float width, float height, string title)
        {
            var card = new VisualElement();
            card.style.position = Position.Absolute;
            card.style.left = left;
            card.style.top = top;
            card.style.width = width;
            card.style.height = height;
            card.style.paddingLeft = 12f;
            card.style.paddingRight = 12f;
            card.style.paddingTop = 8f;
            card.style.paddingBottom = 8f;
            card.style.backgroundColor = new Color(0.07f, 0.1f, 0.16f, 0.9f);
            card.style.borderLeftWidth = 2f;
            card.style.borderTopWidth = 2f;
            card.style.borderRightWidth = 2f;
            card.style.borderBottomWidth = 2f;
            card.style.borderLeftColor = new Color(0.31f, 0.42f, 0.58f, 1f);
            card.style.borderTopColor = new Color(0.31f, 0.42f, 0.58f, 1f);
            card.style.borderRightColor = new Color(0.31f, 0.42f, 0.58f, 1f);
            card.style.borderBottomColor = new Color(0.31f, 0.42f, 0.58f, 1f);

            var titleLabel = new Label(title);
            titleLabel.style.fontSize = 11f;
            titleLabel.style.color = new Color(0.72f, 0.82f, 0.95f, 1f);
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(titleLabel);
            return card;
        }

        private static Label CreateToolkitValueLabel()
        {
            var label = new Label();
            label.style.marginTop = 6f;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = Color.white;
            label.style.fontSize = 14f;
            return label;
        }

        private string BuildPreMatchTitle()
        {
            if (IsRoomState("Countdown"))
            {
                return "DEPLOY COUNTDOWN";
            }

            if (IsRoomState("Lobby"))
            {
                return "TACTICAL LOBBY";
            }

            return "ARENA UPLINK";
        }

        private string BuildPreMatchSummary()
        {
            if (IsRoomState("Countdown"))
            {
                return $"Room {FormatValue(_roomCode)} · Host {FormatValue(_lastServerMessage.HostPlayerName)}";
            }

            if (IsRoomState("Lobby"))
            {
                return $"Room {FormatValue(_roomCode)} · Host {FormatValue(_lastServerMessage.HostPlayerName)} · Ready {_lastServerMessage.ReadyPlayers}/{Mathf.Max(1, _lastServerMessage.PlayerCount)}";
            }

            return $"Host {_config.Host}:{_config.Port} · Create/Join auto-connects";
        }

        private string BuildNetworkTelemetrySummary()
        {
            var sessionShort = AbbreviateValue(_lastServerMessage.SessionId, 8);
            var connectedPlayer = _client != null && _client.IsConnected ? _client.ConnectedPlayerName : _playerName;
            var rttText = _client != null && _client.LastHeartbeatRttMs >= 0 ? $"{_client.LastHeartbeatRttMs}ms" : "n/a";
            var heartbeatAgeText = $"{Mathf.Max(0f, _lastServerMessage.HeartbeatAgeSeconds):0.00}s";
            var snapshotSequence = _lastServerMessage.SnapshotSequence > 0 ? _lastServerMessage.SnapshotSequence : (_client != null ? _client.LastSnapshotSequence : 0);
            var ackedTick = _lastServerMessage.LastProcessedClientTick > 0 ? _lastServerMessage.LastProcessedClientTick : (_client != null ? _client.LastAckedClientTick : 0);
            var messageAgeText = "n/a";
            if (_client != null && _client.LastMessageReceivedUtc != DateTimeOffset.MinValue)
            {
                messageAgeText = $"{Math.Max(0d, (DateTimeOffset.UtcNow - _client.LastMessageReceivedUtc).TotalSeconds):0.00}s";
            }

            return string.Join("\n", new[]
            {
                $"Player {AbbreviateValue(connectedPlayer, 12)} · Room {FormatValue(_roomCode)}",
                $"Session {FormatValue(sessionShort)} · Msg {(_client != null ? _client.MessagesReceivedCount : 0)}",
                $"Tick {_tick} · Snap {snapshotSequence} · Ack {ackedTick}",
                $"RTT {rttText} · Beat {heartbeatAgeText} · MsgAge {messageAgeText}"
            });
        }

        private string BuildRecentEventsSummary()
        {
            if (_logs.Count == 0)
            {
                return "Recent events:\n- waiting for network activity";
            }

            var entries = _logs.Skip(Mathf.Max(0, _logs.Count - 2))
                .Select(entry => AbbreviateValue(entry, 36))
                .ToArray();
            return "Recent events:\n- " + string.Join("\n- ", entries);
        }

        private async Task ConnectFromUiAsync()
        {
            if (_client == null)
            {
                return;
            }

            try
            {
                await EnsureConnectedFromUiAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"Connect failed: {ex.Message}");
            }
        }

        private void BeginInitialConnectionAttempt()
        {
            if (_autoConnectAttempted || _client == null)
            {
                return;
            }

            _autoConnectAttempted = true;
            _ = AutoConnectSilentlyAsync();
        }

        private async Task AutoConnectSilentlyAsync()
        {
            try
            {
                await EnsureConnectedFromUiAsync();
                AppendLog($"Auto-connected as {GetEffectiveLocalPlayerName()}.");
            }
            catch (Exception ex)
            {
                AppendLog($"Auto-connect skipped: {ex.Message}");
            }
        }

        private async Task CreateRoomFromUiAsync()
        {
            if (_client == null)
            {
                return;
            }

            if (_client.IsConnected && IsAnyRoomState("Lobby", "Countdown", "Active", "Ended", "Saving", "ResultsReady"))
            {
                AppendLog($"Already in room {FormatValue(_roomCode)}. Leave or reconnect before creating another room.");
                return;
            }

            try
            {
                _readyRequested = false;
                await EnsureConnectedFromUiAsync();
                await _client.CreateRoomAsync(GetLifetimeToken());
            }
            catch (Exception ex)
            {
                AppendLog($"Create room failed: {ex.Message}");
            }
        }

        private async Task JoinRoomFromUiAsync()
        {
            if (_client == null)
            {
                return;
            }

            var normalizedRoomCode = NormalizeRoomCode(_roomCode);
            if (string.IsNullOrWhiteSpace(normalizedRoomCode))
            {
                AppendLog("Join room skipped: enter a room code first.");
                return;
            }

            if (_client.IsConnected && IsAnyRoomState("Lobby", "Countdown", "Active", "Ended", "Saving", "ResultsReady"))
            {
                if (string.Equals(normalizedRoomCode, NormalizeRoomCode(_lastServerMessage.RoomCode), StringComparison.Ordinal))
                {
                    AppendLog($"Already joined room {normalizedRoomCode}.");
                    return;
                }

                AppendLog($"Already in room {FormatValue(_roomCode)}. Leave or reconnect before joining another room.");
                return;
            }

            try
            {
                _readyRequested = false;
                _roomCode = normalizedRoomCode;
                await EnsureConnectedFromUiAsync();
                await _client.JoinRoomAsync(normalizedRoomCode, GetLifetimeToken());
            }
            catch (Exception ex)
            {
                AppendLog($"Join room failed: {ex.Message}");
            }
        }

        private async Task EnsureConnectedFromUiAsync()
        {
            if (_client == null)
            {
                return;
            }

            _playerName = NormalizePlayerName(_playerName);
            if (string.IsNullOrWhiteSpace(_playerName))
            {
                throw new InvalidOperationException("Player name is required.");
            }

            if (_client.IsConnected)
            {
                if (string.Equals(_client.ConnectedPlayerName, _playerName, StringComparison.Ordinal))
                {
                    return;
                }

                AppendLog($"Reconnect required to apply player name change: {_client.ConnectedPlayerName} -> {_playerName}");
                _client.Disconnect();
                ResetTransientSessionView();
            }

            await _client.ConnectAndHandshakeAsync(_playerName, _protocolVersionOverride, GetLifetimeToken());
        }

        private async Task StartMatchFromUiAsync()
        {
            if (_client == null)
            {
                return;
            }

            try
            {
                await _client.StartMatchAsync(GetLifetimeToken());
            }
            catch (Exception ex)
            {
                AppendLog($"Start match failed: {ex.Message}");
            }
        }

        private async Task ReturnToLobbyFromUiAsync()
        {
            if (_client != null && _client.IsConnected)
            {
                _client.Disconnect();
            }

            ResetTransientSessionView();

            try
            {
                await EnsureConnectedFromUiAsync();
                AppendLog("Returned to lobby.");
            }
            catch (Exception ex)
            {
                AppendLog($"Return to lobby failed: {ex.Message}");
            }
        }

        private CancellationToken GetLifetimeToken() =>
            _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None;

        private void ResetTransientSessionView()
        {
            _roomCode = string.Empty;
            _readyRequested = false;
            _lastServerMessage = new SpikeServerMessage();
            _activeBatteryIds = Array.Empty<int>();
            _batteryPositionsById.Clear();
            _trapPositionsById.Clear();
            _playerVisuals.Clear();
            SyncScenePresentation();
            RefreshUiToolkitOverlay();
        }

        private static bool MessageCarriesRoomListings(string messageType) =>
            string.Equals(messageType, "hello_accepted", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(messageType, "room_joined", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(messageType, "heartbeat_ack", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(messageType, "room_listings_updated", StringComparison.OrdinalIgnoreCase);

        private static string NormalizePlayerName(string playerName) =>
            string.IsNullOrWhiteSpace(playerName) ? string.Empty : playerName.Trim();

        private static string AbbreviateValue(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim();
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
        }

        private void EnsureDefaultPlayerName()
        {
            if (!string.Equals(_playerName, "PlayerA", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(_playerName))
            {
                return;
            }

            try
            {
                _playerName = FormattableString.Invariant($"Player{System.Diagnostics.Process.GetCurrentProcess().Id % 10000:0000}");
            }
            catch
            {
                _playerName = FormattableString.Invariant($"Player{UnityEngine.Random.Range(1000, 9999)}");
            }
        }

        private static string NormalizeRoomCode(string roomCode) =>
            string.IsNullOrWhiteSpace(roomCode) ? string.Empty : roomCode.Trim().ToUpperInvariant();

        private bool IsLocalPlayerHost() =>
            (!string.IsNullOrWhiteSpace(_lastServerMessage.HostSessionId) &&
             string.Equals(_lastServerMessage.HostSessionId, _lastServerMessage.SessionId, StringComparison.Ordinal)) ||
            (!string.IsNullOrWhiteSpace(_lastServerMessage.HostPlayerName) &&
             string.Equals(_lastServerMessage.HostPlayerName, GetEffectiveLocalPlayerName(), StringComparison.Ordinal)) ||
            (IsRoomState("Lobby") &&
             _lastServerMessage.PlayerCount <= 1 &&
             (_lastServerMessage.Members?.Length ?? 0) == 1 &&
             string.Equals(_lastServerMessage.Members[0], GetEffectiveLocalPlayerName(), StringComparison.Ordinal));

        private string BuildLobbyMembersSummary()
        {
            if (_lastServerMessage.Members == null || _lastServerMessage.Members.Length == 0)
            {
                return "Members: waiting for room";
            }

            var readySet = new HashSet<string>(_lastServerMessage.ReadyMembers ?? Array.Empty<string>(), StringComparer.Ordinal);
            return "Players in room:\n- " + string.Join("\n- ", _lastServerMessage.Members.Select(member =>
                $"{member}{(string.Equals(member, _lastServerMessage.HostPlayerName, StringComparison.Ordinal) ? " (Host)" : string.Empty)}{(readySet.Contains(member) ? " (Ready)" : " (Waiting)")}"));
        }

        private string BuildRoomListingsSummary()
        {
            if (_lastServerMessage.RoomListings == null || _lastServerMessage.RoomListings.Length == 0)
            {
                return "Open rooms: none yet";
            }

            return "Open rooms:\n- " + string.Join("\n- ", _lastServerMessage.RoomListings);
        }

        private static Color BuildPlayerSceneColor(PlayerVisualState player)
        {
            if (player.IsDebuffed)
            {
                return new Color(1f, 0.47f, 0.28f, 1f);
            }

            if (player.ImmunityRemainingSeconds > 0.05f)
            {
                return new Color(0.95f, 0.89f, 0.42f, 1f);
            }

            return new Color(0.33f, 0.9f, 0.53f, 1f);
        }

        private static SpriteRenderer EnsureSceneSprite(
            string name,
            Transform parent,
            Vector2 position,
            Vector3 scale,
            Color color,
            int sortingOrder)
        {
            var existingChild = parent.Find(name);
            if (existingChild == null)
            {
                return CreateSceneSprite(name, parent, position, scale, color, sortingOrder);
            }

            existingChild.localPosition = new Vector3(position.x, position.y, 0f);
            existingChild.localScale = scale;

            var renderer = existingChild.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                renderer = existingChild.gameObject.AddComponent<SpriteRenderer>();
            }

            renderer.sprite = GetSolidSprite();
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static SpriteRenderer CreateSceneSprite(
            string name,
            Transform parent,
            Vector2 position,
            Vector3 scale,
            Color color,
            int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(position.x, position.y, 0f);
            go.transform.localScale = scale;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = GetSolidSprite();
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private static Sprite GetSolidSprite()
        {
            if (_solidSprite != null)
            {
                return _solidSprite;
            }

            _solidSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            return _solidSprite;
        }

        private bool IsRoomState(string state) =>
            string.Equals(_lastServerMessage.RoomState, state, StringComparison.OrdinalIgnoreCase);

        private bool IsAnyRoomState(params string[] states) => states.Any(IsRoomState);

        private void EnsureOverlayStyles()
        {
            if (_overlayTitleStyle != null &&
                _overlaySubStyle != null &&
                _pillLabelStyle != null &&
                _fallbackPanelTitleStyle != null &&
                _fallbackPanelBodyStyle != null &&
                _fallbackPanelValueStyle != null &&
                _fallbackButtonStyle != null &&
                _fallbackFieldStyle != null &&
                _fallbackFieldInputStyle != null &&
                _fallbackSectionLabelStyle != null)
            {
                return;
            }

            _overlayTitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _overlaySubStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.88f, 0.92f, 1f, 1f) }
            };
            _pillLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white }
            };

            _fallbackPanelTitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _fallbackPanelValueStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(0.96f, 0.98f, 1f, 1f) }
            };

            _fallbackPanelBodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 15,
                wordWrap = true,
                normal = { textColor = new Color(0.88f, 0.93f, 1f, 1f) }
            };

            _fallbackSectionLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.60f, 0.84f, 0.96f, 1f) }
            };

            _fallbackButtonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                fixedHeight = 46f,
                normal = { textColor = Color.white }
            };
            _fallbackButtonStyle.hover.textColor = Color.white;
            _fallbackButtonStyle.active.textColor = Color.white;

            _fallbackFieldStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };

            _fallbackFieldInputStyle = new GUIStyle(GUI.skin.textField)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 18,
                padding = new RectOffset(12, 12, 8, 8),
                normal = { textColor = Color.white, background = null },
                focused = { textColor = Color.white, background = null },
                active = { textColor = Color.white, background = null },
                hover = { textColor = Color.white, background = null },
                border = new RectOffset(0, 0, 0, 0)
            };
        }

        private void DrawStatusPill(Rect rect, string text, Color fill)
        {
            DrawFilledRect(rect, fill);
            DrawOutline(rect, new Color(1f, 1f, 1f, 0.2f), 1f);
            GUI.Label(rect, text, _pillLabelStyle);
        }

        private static Rect BuildMarkerRect(Rect arenaRect, Vector2 worldPosition, float size)
        {
            var normalizedX = Mathf.InverseLerp(-ArenaWorldHalfExtent, ArenaWorldHalfExtent, worldPosition.x);
            var normalizedY = Mathf.InverseLerp(ArenaWorldHalfExtent, -ArenaWorldHalfExtent, worldPosition.y);
            var centerX = arenaRect.x + (arenaRect.width * normalizedX);
            var centerY = arenaRect.y + (arenaRect.height * normalizedY);
            return new Rect(centerX - (size * 0.5f), centerY - (size * 0.5f), size, size);
        }

        private static void DrawFilledRect(Rect rect, Color color)
        {
            var previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            DrawFilledRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawFilledRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawFilledRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawFilledRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static void DrawHudCard(Rect rect, string label, string value, Color accentColor)
        {
            DrawFilledRect(rect, new Color(0.08f, 0.1f, 0.16f, 0.92f));
            DrawFilledRect(new Rect(rect.x, rect.y, 6f, rect.height), accentColor);
            DrawOutline(rect, new Color(0.29f, 0.36f, 0.48f, 1f), 1f);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 6f, rect.width - 18f, 16f), label);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 20f, rect.width - 18f, 16f), value);
        }

        private static void DrawPanelChrome(Rect rect, Color fill, Color border)
        {
            DrawFilledRect(rect, fill);
            DrawOutline(rect, border, 2f);
        }

        private void DrawHudStrip(Rect rect, string label, string value)
        {
            DrawPanelChrome(rect, new Color(0.05f, 0.09f, 0.16f, 0.92f), new Color(0.32f, 0.78f, 0.95f, 0.92f));
            GUI.Label(new Rect(rect.x + 18f, rect.y + 10f, rect.width - 36f, 20f), label, _fallbackSectionLabelStyle);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 34f, rect.width - 36f, rect.height - 42f), value, _fallbackPanelValueStyle);
        }

        private bool DrawFallbackButton(Rect rect, string text, bool primary)
        {
            var hover = rect.Contains(Event.current.mousePosition);
            var fill = primary
                ? (hover ? new Color(0.20f, 0.46f, 0.72f, 1f) : new Color(0.14f, 0.34f, 0.56f, 1f))
                : (hover ? new Color(0.16f, 0.24f, 0.38f, 1f) : new Color(0.10f, 0.17f, 0.28f, 1f));
            DrawPanelChrome(rect, fill, new Color(0.48f, 0.74f, 0.94f, 0.9f));
            GUI.Label(rect, text, _fallbackButtonStyle);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private string DrawFallbackTextField(Rect rect, string value)
        {
            DrawPanelChrome(rect, new Color(0.08f, 0.12f, 0.20f, 0.96f), new Color(0.42f, 0.70f, 0.94f, 0.75f));
            return GUI.TextField(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), value, _fallbackFieldInputStyle);
        }

        private sealed class PlayerVisualState
        {
            public string Name = string.Empty;
            public int Score;
            public Vector2 Position;
            public float MoveMultiplier = 1f;
            public string EffectSource = string.Empty;
            public float EffectRemainingSeconds;
            public float ImmunityRemainingSeconds;

            public bool IsDebuffed => MoveMultiplier < 0.99f && EffectRemainingSeconds > 0.01f;
        }

        private sealed class EffectVisualState
        {
            public float MoveMultiplier = 1f;
            public string Source = string.Empty;
            public float RemainingSeconds;
            public float ImmunityRemainingSeconds;
        }
    }

    public readonly struct NetworkSpikeActiveHudSnapshot
    {
        public NetworkSpikeActiveHudSnapshot(
            string matchLabel,
            string scoreLabel,
            string statusLabel,
            string cooldownLabel,
            string networkTelemetry,
            string recentEvents)
        {
            MatchLabel = matchLabel;
            ScoreLabel = scoreLabel;
            StatusLabel = statusLabel;
            CooldownLabel = cooldownLabel;
            NetworkTelemetry = networkTelemetry;
            RecentEvents = recentEvents;
        }

        public string MatchLabel { get; }
        public string ScoreLabel { get; }
        public string StatusLabel { get; }
        public string CooldownLabel { get; }
        public string NetworkTelemetry { get; }
        public string RecentEvents { get; }
    }

    public static class NetworkSpikeActiveHudRenderer
    {
        private static GUIStyle panelValueStyle;
        private static GUIStyle panelBodyStyle;
        private static GUIStyle sectionLabelStyle;

        public static void Draw(Rect viewportRect, NetworkSpikeActiveHudSnapshot snapshot)
        {
            EnsureStyles();

            var topCardWidth = Mathf.Min(viewportRect.width * 0.34f, 430f);
            var scoreCardWidth = Mathf.Min(viewportRect.width * 0.34f, 480f);
            var topLeft = new Rect(viewportRect.x + 24f, viewportRect.y + 24f, topCardWidth, 122f);
            var topRight = new Rect(viewportRect.x + viewportRect.width - scoreCardWidth - 24f, viewportRect.y + 24f, scoreCardWidth, 122f);
            var bottomLeft = new Rect(viewportRect.x + 24f, viewportRect.y + viewportRect.height - 132f, Mathf.Min(viewportRect.width * 0.34f, 440f), 102f);
            var bottomRight = new Rect(viewportRect.x + viewportRect.width - Mathf.Min(viewportRect.width * 0.28f, 320f) - 24f, viewportRect.y + viewportRect.height - 132f, Mathf.Min(viewportRect.width * 0.28f, 320f), 102f);

            DrawHudStrip(topLeft, "MATCH", snapshot.MatchLabel);
            DrawHudStrip(topRight, "SCORE", snapshot.ScoreLabel);
            DrawHudStrip(bottomLeft, "STATUS", snapshot.StatusLabel);
            DrawHudStrip(bottomRight, "COOLDOWN", snapshot.CooldownLabel);

            var width = Mathf.Min(viewportRect.width * 0.34f, 480f);
            var height = Mathf.Min(viewportRect.height * 0.30f, 250f);
            var networkRect = new Rect(viewportRect.x + viewportRect.width - width - 24f, viewportRect.y + 158f, width, height);
            DrawPanelChrome(networkRect, new Color(0.04f, 0.07f, 0.12f, 0.82f), new Color(0.30f, 0.56f, 0.78f, 0.9f));
            GUI.Label(new Rect(networkRect.x + 18f, networkRect.y + 16f, networkRect.width - 36f, 24f), "NETWORK TELEMETRY", sectionLabelStyle);
            GUI.Label(new Rect(networkRect.x + 18f, networkRect.y + 46f, networkRect.width - 36f, 96f), snapshot.NetworkTelemetry, panelBodyStyle);
            GUI.Label(new Rect(networkRect.x + 18f, networkRect.y + 150f, networkRect.width - 36f, networkRect.height - 166f), snapshot.RecentEvents, panelBodyStyle);
        }

        private static void DrawHudStrip(Rect rect, string label, string value)
        {
            DrawPanelChrome(rect, new Color(0.05f, 0.09f, 0.16f, 0.92f), new Color(0.32f, 0.78f, 0.95f, 0.92f));
            GUI.Label(new Rect(rect.x + 18f, rect.y + 10f, rect.width - 36f, 20f), label, sectionLabelStyle);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 34f, rect.width - 36f, rect.height - 42f), value, panelValueStyle);
        }

        private static void EnsureStyles()
        {
            if (panelValueStyle != null)
            {
                return;
            }

            panelValueStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                normal = { textColor = new Color(0.96f, 0.98f, 1f, 1f) }
            };

            panelBodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 15,
                wordWrap = true,
                normal = { textColor = new Color(0.88f, 0.93f, 1f, 1f) }
            };

            sectionLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.60f, 0.84f, 0.96f, 1f) }
            };
        }

        private static void DrawPanelChrome(Rect rect, Color fill, Color border)
        {
            DrawFilledRect(rect, fill);
            DrawOutline(rect, border, 2f);
        }

        private static void DrawFilledRect(Rect rect, Color color)
        {
            var previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            DrawFilledRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            DrawFilledRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            DrawFilledRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            DrawFilledRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }
}
