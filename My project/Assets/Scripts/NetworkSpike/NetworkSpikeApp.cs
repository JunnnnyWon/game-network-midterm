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
        private const float ArenaWorldHalfExtent = 3.5f;
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
        private static readonly Vector2[] BatterySpawnPreview =
        {
            new(0f, 0f),
            new(0f, 2f),
            new(0f, -2f),
            new(2f, 0f),
            new(-2f, 0f),
            new(1.5f, 1.5f),
            new(-1.5f, 1.5f),
            new(1.5f, -1.5f)
        };

        private static readonly Vector2[] TrapPreviewPositions =
        {
            new(0f, 1f),
            new(0f, -1f)
        };

        private readonly List<string> _logs = new();
        private readonly List<PlayerVisualState> _playerVisuals = new();
        private readonly NetworkSpikeClientConfig _config = new();
        private readonly Dictionary<string, SpriteRenderer> _scenePlayerRenderers = new(StringComparer.Ordinal);
        private NetworkSpikeClient _client;
        private CancellationTokenSource _lifetimeCts;
        private string _playerName = "PlayerA";
        private string _roomCode = string.Empty;
        private string _protocolVersionOverride = string.Empty;
        private int _tick;
        private bool _autoHeartbeat = true;
        private bool _readyRequested;
        private bool _showDiagnosticsWindow;
        private Rect _window = new(0f, 0f, 320f, 220f);
        private SpikeServerMessage _lastServerMessage = new SpikeServerMessage();
        private int[] _activeBatteryIds = Array.Empty<int>();
        private GUIStyle _overlayTitleStyle;
        private GUIStyle _overlaySubStyle;
        private GUIStyle _pillLabelStyle;
        private Transform _sceneRoot;
        private Transform _uiOverlayRoot;
        private SpriteRenderer _arenaSurfaceRenderer;
        private SpriteRenderer[] _batterySceneRenderers = Array.Empty<SpriteRenderer>();
        private SpriteRenderer[] _trapSceneRenderers = Array.Empty<SpriteRenderer>();
        private UIDocument _uiDocument;
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
        private Label _toolkitCountdownLabel;
        private Label _toolkitTopLabel;
        private Label _toolkitScoreLabel;
        private Label _toolkitCooldownLabel;
        private Label _toolkitEffectLabel;
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
            _client = new NetworkSpikeClient(_config);
            _client.LogEmitted += AppendLog;
            _client.MessageReceived += OnMessageReceived;
            EnsureScenePresentation();
            EnsureUiToolkitOverlay();
            ConfigureCamera();
            AppendLog("Network spike bootstrap ready.");
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.backquoteKey.wasPressedThisFrame)
            {
                _showDiagnosticsWindow = !_showDiagnosticsWindow;
                AppendLog(_showDiagnosticsWindow ? "Diagnostics window shown." : "Diagnostics window hidden.");
            }

            if (_client == null || !_client.IsConnected)
            {
                EnsureUiToolkitOverlay();
                return;
            }

            if (_autoHeartbeat)
            {
                _ = _client.MaybeSendHeartbeatAsync(_lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
            }

            if (IsRoomState("Active"))
            {
                var move = ReadMoveVector();
                if (move.sqrMagnitude > 0.0001f)
                {
                    _tick += 1;
                    _ = _client.SendInputFrameAsync(_tick, move, ReadAimVector(), false, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
                }

                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                {
                    _tick += 1;
                    _ = _client.SendInputFrameAsync(_tick, ReadMoveVector(), ReadAimVector(), true, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
                }
            }

            UpdateCameraFollow();
            EnsureUiToolkitOverlay();
        }

        private void OnGUI()
        {
            if (!_showDiagnosticsWindow)
            {
                return;
            }

            _window.x = Screen.width - _window.width - 16f;
            _window.y = Screen.height - _window.height - 16f;
            _window = GUI.Window(4815, _window, DrawWindow, "Network Session Spike");
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

        private void DrawWindow(int id)
        {
            GUILayout.Label("Diagnostics only — canonical flow is UI Toolkit.");
            GUILayout.Space(6);
            GUILayout.Label($"Host: {_config.Host}:{_config.Port}");
            GUILayout.Label("Protocol Override (optional mismatch test)");
            _protocolVersionOverride = GUILayout.TextField(_protocolVersionOverride);
            _autoHeartbeat = GUILayout.Toggle(_autoHeartbeat, "Auto heartbeat when idle");
            GUILayout.Label($"Connected: {(_client != null && _client.IsConnected)}");
            GUILayout.Label($"Room: {FormatValue(_roomCode)}");
            GUILayout.Label($"State: {_lastServerMessage.RoomState}");
            GUILayout.Label($"Players: {_lastServerMessage.PlayerCount} / Ready {_lastServerMessage.ReadyPlayers}");
            GUILayout.Label($"Timer: {_lastServerMessage.MatchTimeRemainingSeconds:F1}s · Countdown {_lastServerMessage.CountdownRemainingSeconds:F1}s");
            GUILayout.Label($"Toolkit assets loaded: {_toolkitUsesAuthoredAssets}");

            GUILayout.Space(8);
            GUILayout.Label("Logs:");
            var startIndex = Mathf.Max(0, _logs.Count - 8);
            for (var index = startIndex; index < _logs.Count; index++)
            {
                GUILayout.Label(_logs[index]);
            }
            GUI.DragWindow();
        }

        private void OnMessageReceived(SpikeServerMessage message)
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
            foreach (var trapPosition in TrapPreviewPositions)
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
                var batteryIndex = batteryId - 1;
                if (batteryIndex < 0 || batteryIndex >= BatterySpawnPreview.Length)
                {
                    continue;
                }

                var markerRect = BuildMarkerRect(rect, BatterySpawnPreview[batteryIndex], 16f);
                DrawFilledRect(markerRect, new Color(1f, 0.83f, 0.18f, 0.95f));
                DrawOutline(markerRect, new Color(1f, 0.95f, 0.62f, 1f), 2f);
            }
        }

        private void DrawPlayerPreview(Rect rect)
        {
            for (var index = 0; index < _playerVisuals.Count; index++)
            {
                var player = _playerVisuals[index];
                var isLocalPlayer = string.Equals(player.Name, _playerName, StringComparison.Ordinal);
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
            var scoresByName = ParseScores(message.Scoreboard);
            var positionsByName = ParsePositions(message.PlayerPositions);
            var effectsByName = ParseEffects(message.EffectStates);
            _playerVisuals.Clear();
            _playerVisuals.AddRange(BuildPlayerVisuals(message.Members, scoresByName, positionsByName, effectsByName));
        }

        public void ApplyAuthoritativeSnapshotForTesting(SpikeServerMessage message)
        {
            _lastServerMessage = message;
            RefreshPresentationSnapshot(message);
            SyncScenePresentation();
            RefreshUiToolkitOverlay();
        }

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

        public bool UsesDiagnosticsOnlyImguiForTesting => !_showDiagnosticsWindow;

        public string ToolkitPreMatchTitleForTesting => _toolkitPreMatchTitleLabel?.text ?? string.Empty;

        public string ToolkitPreMatchMembersForTesting => _toolkitPreMatchMembersLabel?.text ?? string.Empty;

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

            return "Score: " + string.Join(" · ", _playerVisuals.Select(player => $"{player.Name} {player.Score}"));
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
            _playerVisuals.FirstOrDefault(player => string.Equals(player.Name, _playerName, StringComparison.Ordinal));

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

            _sceneRoot = new GameObject("NetworkSpikeScenePresentation").transform;
            _arenaSurfaceRenderer = CreateSceneSprite(
                "ArenaSurface",
                _sceneRoot,
                Vector2.zero,
                new Vector3((ArenaWorldHalfExtent * 2f) + 0.8f, (ArenaWorldHalfExtent * 2f) + 0.8f, 1f),
                new Color(0.06f, 0.08f, 0.12f, 0.92f),
                -10);

            _batterySceneRenderers = BatterySpawnPreview
                .Select((position, index) => CreateSceneSprite(
                    $"Battery-{index + 1}",
                    _sceneRoot,
                    position,
                    new Vector3(0.35f, 0.35f, 1f),
                    new Color(1f, 0.83f, 0.18f, 1f),
                    20))
                .ToArray();

            _trapSceneRenderers = TrapPreviewPositions
                .Select((position, index) => CreateSceneSprite(
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

                _batterySceneRenderers[index].gameObject.SetActive(activeIds.Contains(index + 1));
            }
        }

        private void SyncTrapSceneActors()
        {
            foreach (var renderer in _trapSceneRenderers)
            {
                if (renderer != null)
                {
                    renderer.gameObject.SetActive(true);
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
                    renderer = CreateSceneSprite(
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
            camera.orthographicSize = 4.4f;
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
            if (_uiRoot != null)
            {
                return;
            }

            if (_uiOverlayRoot == null)
            {
                _uiOverlayRoot = new GameObject("NetworkSpikeUiOverlay").transform;
                _uiOverlayRoot.SetParent(transform, false);
            }

            if (_panelSettings == null)
            {
                _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                _panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                _panelSettings.referenceResolution = new Vector2Int(1920, 1080);
                _panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                _panelSettings.match = 0.5f;
                _panelSettings.sortingOrder = 100;
                _panelSettings.themeStyleSheet = LoadToolkitThemeStyleSheet();
            }

            if (_uiDocument == null)
            {
                _uiDocument = _uiOverlayRoot.gameObject.AddComponent<UIDocument>();
                _uiDocument.panelSettings = _panelSettings;
                _uiDocument.sortingOrder = 100;
            }

            _uiRoot = _uiDocument.rootVisualElement;
            if (_uiRoot == null)
            {
                return;
            }

            BuildUiToolkitOverlay();
            RefreshUiToolkitOverlay();
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
            _toolkitReadyButton = _uiRoot.Q<Button>("ready-button");
            _toolkitCountdownLabel = _uiRoot.Q<Label>("countdown-label");
            _toolkitTopLabel = _uiRoot.Q<Label>("toolkit-top-label");
            _toolkitScoreLabel = _uiRoot.Q<Label>("toolkit-score-label");
            _toolkitCooldownLabel = _uiRoot.Q<Label>("toolkit-cooldown-label");
            _toolkitEffectLabel = _uiRoot.Q<Label>("toolkit-effect-label");
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
            _uiRoot.Clear();
            _uiRoot.style.flexGrow = 1f;
            _uiRoot.pickingMode = PickingMode.Position;

            _preMatchOverlay = new VisualElement();
            _preMatchOverlay.style.position = Position.Absolute;
            _preMatchOverlay.style.left = ToolkitMargin;
            _preMatchOverlay.style.top = ToolkitMargin;
            _preMatchOverlay.style.width = new Length(32f, LengthUnit.Percent);
            _preMatchOverlay.style.minWidth = 280f;
            _preMatchOverlay.style.maxWidth = 420f;
            _preMatchOverlay.style.paddingLeft = 16f;
            _preMatchOverlay.style.paddingRight = 16f;
            _preMatchOverlay.style.paddingTop = 16f;
            _preMatchOverlay.style.paddingBottom = 16f;
            _preMatchOverlay.style.backgroundColor = new Color(0.08f, 0.11f, 0.17f, 0.92f);
            _preMatchOverlay.style.borderLeftWidth = 2f;
            _preMatchOverlay.style.borderTopWidth = 2f;
            _preMatchOverlay.style.borderRightWidth = 2f;
            _preMatchOverlay.style.borderBottomWidth = 2f;
            _preMatchOverlay.style.borderLeftColor = new Color(0.39f, 0.55f, 0.75f, 1f);
            _preMatchOverlay.style.borderTopColor = new Color(0.39f, 0.55f, 0.75f, 1f);
            _preMatchOverlay.style.borderRightColor = new Color(0.39f, 0.55f, 0.75f, 1f);
            _preMatchOverlay.style.borderBottomColor = new Color(0.39f, 0.55f, 0.75f, 1f);
            _preMatchOverlay.pickingMode = PickingMode.Position;

            _toolkitPreMatchTitleLabel = new Label();
            _toolkitPreMatchTitleLabel.style.fontSize = 20f;
            _toolkitPreMatchTitleLabel.style.color = Color.white;
            _toolkitPreMatchTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            _toolkitPreMatchSummaryLabel = new Label();
            _toolkitPreMatchSummaryLabel.style.marginTop = 6f;
            _toolkitPreMatchSummaryLabel.style.color = new Color(0.88f, 0.92f, 1f, 1f);

            _toolkitPlayerNameField = new TextField("Player");
            _toolkitPlayerNameField.RegisterValueChangedCallback(evt =>
            {
                if (!_suppressToolkitFieldCallbacks)
                {
                    _playerName = evt.newValue;
                }
            });

            _toolkitRoomCodeField = new TextField("Room Code");
            _toolkitRoomCodeField.RegisterValueChangedCallback(evt =>
            {
                if (!_suppressToolkitFieldCallbacks)
                {
                    _roomCode = evt.newValue;
                }
            });

            var actionRow = new VisualElement();
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.flexWrap = Wrap.Wrap;
            actionRow.style.marginTop = 10f;

            _toolkitConnectButton = new Button(() => _ = ConnectFromUiAsync())
            { text = "Connect" };
            _toolkitCreateButton = new Button(() => _ = CreateRoomFromUiAsync())
            { text = "Create" };
            _toolkitJoinButton = new Button(() => _ = JoinRoomFromUiAsync())
            { text = "Join" };

            _toolkitConnectButton.style.minWidth = 96f;
            _toolkitConnectButton.style.flexGrow = 1f;
            _toolkitCreateButton.style.minWidth = 96f;
            _toolkitCreateButton.style.flexGrow = 1f;
            _toolkitJoinButton.style.minWidth = 96f;
            _toolkitJoinButton.style.flexGrow = 1f;

            actionRow.Add(_toolkitConnectButton);
            actionRow.Add(_toolkitCreateButton);
            actionRow.Add(_toolkitJoinButton);

            _toolkitPreMatchMembersLabel = new Label();
            _toolkitPreMatchMembersLabel.style.marginTop = 10f;
            _toolkitPreMatchMembersLabel.style.whiteSpace = WhiteSpace.Normal;
            _toolkitPreMatchMembersLabel.style.color = new Color(0.95f, 0.97f, 1f, 1f);

            _toolkitReadyButton = new Button(() =>
            {
                _readyRequested = !_readyRequested;
                if (_client != null)
                {
                    _ = _client.SetReadyAsync(_readyRequested, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
                }
            })
            { text = "Set Ready" };
            _toolkitReadyButton.style.marginTop = 10f;

            _toolkitCountdownLabel = new Label();
            _toolkitCountdownLabel.style.marginTop = 10f;
            _toolkitCountdownLabel.style.fontSize = 18f;
            _toolkitCountdownLabel.style.color = new Color(1f, 0.93f, 0.5f, 1f);
            _toolkitCountdownLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            _preMatchOverlay.Add(_toolkitPreMatchTitleLabel);
            _preMatchOverlay.Add(_toolkitPreMatchSummaryLabel);
            _preMatchOverlay.Add(_toolkitPlayerNameField);
            _preMatchOverlay.Add(_toolkitRoomCodeField);
            _preMatchOverlay.Add(actionRow);
            _preMatchOverlay.Add(_toolkitPreMatchMembersLabel);
            _preMatchOverlay.Add(_toolkitReadyButton);
            _preMatchOverlay.Add(_toolkitCountdownLabel);
            _uiRoot.Add(_preMatchOverlay);

            _activeHudOverlay = new VisualElement();
            _activeHudOverlay.style.position = Position.Absolute;
            _activeHudOverlay.style.left = ToolkitMargin;
            _activeHudOverlay.style.top = ToolkitMargin;
            _activeHudOverlay.style.right = ToolkitMargin;
            _activeHudOverlay.style.bottom = ToolkitMargin;
            _activeHudOverlay.pickingMode = PickingMode.Ignore;

            var statusCard = CreateToolkitCard(ToolkitMargin, ToolkitMargin, ToolkitCardWidth, ToolkitCardHeight, "MATCH");
            _toolkitTopLabel = CreateToolkitValueLabel();
            statusCard.Add(_toolkitTopLabel);

            var scoreCard = CreateToolkitCard(ToolkitMargin, ToolkitMargin + ToolkitCardHeight + 8f, ToolkitCardWidth, ToolkitCardHeight, "SCORE");
            _toolkitScoreLabel = CreateToolkitValueLabel();
            scoreCard.Add(_toolkitScoreLabel);

            var cooldownCard = CreateToolkitCard(ToolkitMargin + ToolkitCardWidth + 8f, ToolkitMargin, ToolkitCardWidth, ToolkitCardHeight, "COOLDOWN");
            _toolkitCooldownLabel = CreateToolkitValueLabel();
            cooldownCard.Add(_toolkitCooldownLabel);

            var effectCard = CreateToolkitCard(ToolkitMargin + ToolkitCardWidth + 8f, ToolkitMargin + ToolkitCardHeight + 8f, ToolkitCardWidth, ToolkitCardHeight, "STATUS");
            _toolkitEffectLabel = CreateToolkitValueLabel();
            effectCard.Add(_toolkitEffectLabel);

            _activeHudOverlay.Add(statusCard);
            _activeHudOverlay.Add(scoreCard);
            _activeHudOverlay.Add(cooldownCard);
            _activeHudOverlay.Add(effectCard);
            _uiRoot.Add(_activeHudOverlay);

            _resultsOverlay = new VisualElement();
            _resultsOverlay.style.position = Position.Absolute;
            _resultsOverlay.style.left = 0f;
            _resultsOverlay.style.right = 0f;
            _resultsOverlay.style.top = 0f;
            _resultsOverlay.style.bottom = 0f;
            _resultsOverlay.style.justifyContent = Justify.Center;
            _resultsOverlay.style.alignItems = Align.Center;
            _resultsOverlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.42f);

            var resultsCard = new VisualElement();
            resultsCard.style.width = 360f;
            resultsCard.style.minWidth = 280f;
            resultsCard.style.maxWidth = 360f;
            resultsCard.style.paddingLeft = 18f;
            resultsCard.style.paddingRight = 18f;
            resultsCard.style.paddingTop = 18f;
            resultsCard.style.paddingBottom = 18f;
            resultsCard.style.backgroundColor = new Color(0.08f, 0.11f, 0.17f, 0.94f);
            resultsCard.style.borderLeftWidth = 2f;
            resultsCard.style.borderRightWidth = 2f;
            resultsCard.style.borderTopWidth = 2f;
            resultsCard.style.borderBottomWidth = 2f;
            resultsCard.style.borderLeftColor = new Color(0.39f, 0.55f, 0.75f, 1f);
            resultsCard.style.borderRightColor = new Color(0.39f, 0.55f, 0.75f, 1f);
            resultsCard.style.borderTopColor = new Color(0.39f, 0.55f, 0.75f, 1f);
            resultsCard.style.borderBottomColor = new Color(0.39f, 0.55f, 0.75f, 1f);

            _toolkitResultsTitleLabel = new Label();
            _toolkitResultsTitleLabel.style.fontSize = 20f;
            _toolkitResultsTitleLabel.style.color = Color.white;
            _toolkitResultsTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            _toolkitResultsScoreLabel = new Label();
            _toolkitResultsScoreLabel.style.marginTop = 8f;
            _toolkitResultsScoreLabel.style.color = new Color(0.95f, 0.97f, 1f, 1f);
            _toolkitResultsScoreLabel.style.unityFontStyleAndWeight = FontStyle.Bold;

            _toolkitResultsDetailLabel = new Label();
            _toolkitResultsDetailLabel.style.marginTop = 8f;
            _toolkitResultsDetailLabel.style.whiteSpace = WhiteSpace.Normal;
            _toolkitResultsDetailLabel.style.color = new Color(0.88f, 0.92f, 1f, 1f);

            resultsCard.Add(_toolkitResultsTitleLabel);
            resultsCard.Add(_toolkitResultsScoreLabel);
            resultsCard.Add(_toolkitResultsDetailLabel);
            _resultsOverlay.Add(resultsCard);
            _uiRoot.Add(_resultsOverlay);
            ApplyToolkitFontDefaults();
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
            var opponent = _playerVisuals.FirstOrDefault(player => !string.Equals(player.Name, _playerName, StringComparison.Ordinal));
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
            _toolkitReadyButton.text = _readyRequested ? "Unset Ready" : "Set Ready";
            var isConnected = _client != null && _client.IsConnected;
            var normalizedRoomCode = NormalizeRoomCode(_roomCode);
            var canUsePreMatchActions = !IsRoomState("Countdown") && !IsAnyRoomState("Active", "Ended", "Saving", "ResultsReady");
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
                _toolkitResultsDetailLabel.text = $"State: {FormatValue(_lastServerMessage.RoomState)}\nReason: {FormatValue(_lastServerMessage.EndReason)}\nPersist: {FormatValue(_lastServerMessage.PersistenceStatus)}";
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
                return "Countdown";
            }

            if (IsRoomState("Lobby"))
            {
                return "Lobby";
            }

            return "Main Menu";
        }

        private string BuildPreMatchSummary()
        {
            if (IsRoomState("Countdown"))
            {
                return $"Room {FormatValue(_roomCode)} · Ready {_lastServerMessage.ReadyPlayers}/{Mathf.Max(1, _lastServerMessage.PlayerCount)}";
            }

            if (IsRoomState("Lobby"))
            {
                return $"Room {FormatValue(_roomCode)} · Players {_lastServerMessage.PlayerCount} · Ready {_lastServerMessage.ReadyPlayers}";
            }

            return $"Host {_config.Host}:{_config.Port} · Create/Join auto-connects";
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

        private async Task CreateRoomFromUiAsync()
        {
            if (_client == null)
            {
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
            if (_client == null || _client.IsConnected)
            {
                return;
            }

            await _client.ConnectAndHandshakeAsync(_playerName, _protocolVersionOverride, GetLifetimeToken());
        }

        private CancellationToken GetLifetimeToken() =>
            _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None;

        private static string NormalizeRoomCode(string roomCode) =>
            string.IsNullOrWhiteSpace(roomCode) ? string.Empty : roomCode.Trim().ToUpperInvariant();

        private string BuildLobbyMembersSummary()
        {
            if (_lastServerMessage.Members == null || _lastServerMessage.Members.Length == 0)
            {
                return "Members: waiting for room";
            }

            return "Members: " + string.Join(", ", _lastServerMessage.Members);
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
            if (_overlayTitleStyle != null && _overlaySubStyle != null && _pillLabelStyle != null)
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
}
