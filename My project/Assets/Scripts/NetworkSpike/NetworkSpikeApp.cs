using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

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
        private NetworkSpikeClient _client;
        private CancellationTokenSource _lifetimeCts;
        private string _playerName = "PlayerA";
        private string _roomCode = string.Empty;
        private string _protocolVersionOverride = string.Empty;
        private int _tick;
        private bool _autoHeartbeat = true;
        private bool _readyRequested;
        private Rect _window = new(20, 20, 860, 880);
        private SpikeServerMessage _lastServerMessage = new SpikeServerMessage();
        private int[] _activeBatteryIds = Array.Empty<int>();
        private GUIStyle _overlayTitleStyle;
        private GUIStyle _overlaySubStyle;
        private GUIStyle _pillLabelStyle;

        private void Awake()
        {
            _lifetimeCts = new CancellationTokenSource();
            _client = new NetworkSpikeClient(_config);
            _client.LogEmitted += AppendLog;
            _client.MessageReceived += OnMessageReceived;
            AppendLog("Network spike bootstrap ready.");
        }

        private void Update()
        {
            if (_client == null || !_client.IsConnected)
            {
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
        }

        private void OnGUI()
        {
            _window = GUI.Window(4815, _window, DrawWindow, "Network Session Spike");
        }

        private void OnDestroy()
        {
            if (_lifetimeCts != null) _lifetimeCts.Cancel();
            if (_client != null) _client.Dispose();
            if (_lifetimeCts != null) _lifetimeCts.Dispose();
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label("Use two local players and one local server process for the spike.");
            GUILayout.Space(6);
            GUILayout.Label($"Host: {_config.Host}:{_config.Port}");
            GUILayout.Label("Player Name");
            _playerName = GUILayout.TextField(_playerName);
            GUILayout.Label("Protocol Override (optional mismatch test)");
            _protocolVersionOverride = GUILayout.TextField(_protocolVersionOverride);
            GUILayout.Label("Room Code");
            _roomCode = GUILayout.TextField(_roomCode);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Connect"))
            {
                if (_client != null)
                {
                    _ = _client.ConnectAndHandshakeAsync(_playerName, _protocolVersionOverride, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
                }
            }

            if (GUILayout.Button("Create Room"))
            {
                _readyRequested = false;
                if (_client != null)
                {
                    _ = _client.CreateRoomAsync(_lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
                }
            }

            if (GUILayout.Button("Join Room"))
            {
                _readyRequested = false;
                if (_client != null)
                {
                    _ = _client.JoinRoomAsync(_roomCode, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_readyRequested ? "Unset Ready" : "Set Ready"))
            {
                _readyRequested = !_readyRequested;
                if (_client != null)
                {
                    _ = _client.SetReadyAsync(_readyRequested, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
                }
            }
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Send Input Frame") && IsRoomState("Active"))
            {
                _tick += 1;
                if (_client != null)
                {
                    _ = _client.SendInputFrameAsync(_tick, ReadMoveVector(), ReadAimVector(), Mouse.current != null && Mouse.current.leftButton.isPressed, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
                }
            }

            _autoHeartbeat = GUILayout.Toggle(_autoHeartbeat, "Auto heartbeat when idle");
            GUILayout.Label($"Connected: {(_client != null && _client.IsConnected)}");
            GUILayout.Label($"Last Tick Sent: {_tick}");
            GUILayout.Space(8);
            GUILayout.Label($"Authoritative Room State: {_lastServerMessage.RoomState}");
            GUILayout.Label($"Player Count: {_lastServerMessage.PlayerCount} / Ready: {_lastServerMessage.ReadyPlayers}");
            GUILayout.Label($"Countdown Remaining: {_lastServerMessage.CountdownRemainingSeconds:F2}");
            GUILayout.Label($"Match Time Remaining: {_lastServerMessage.MatchTimeRemainingSeconds:F2}");
            GUILayout.Label($"End Reason: {_lastServerMessage.EndReason}");
            GUILayout.Label($"Persistence Status: {_lastServerMessage.PersistenceStatus}");
            GUILayout.Label($"Members: {string.Join(", ", _lastServerMessage.Members ?? Array.Empty<string>())}");
            GUILayout.Label($"Scoreboard: {string.Join(" | ", _lastServerMessage.Scoreboard ?? Array.Empty<string>())}");
            GUILayout.Label($"Active Batteries: {string.Join(", ", _lastServerMessage.ActiveBatteryIds ?? Array.Empty<int>())}");
            GUILayout.Label($"Effects: {string.Join(" | ", _lastServerMessage.EffectStates ?? Array.Empty<string>())}");
            GUILayout.Label($"Player Positions: {string.Join(" | ", _lastServerMessage.PlayerPositions ?? Array.Empty<string>())}");
            if (IsRoomState("Active"))
            {
                GUILayout.Label("Move with WASD, aim with the mouse, and left click to drive authoritative pickup/trap/slow checks.");
            }

            GUILayout.Space(10);
            GUILayout.Label("Authoritative Arena Preview");
            var arenaRect = GUILayoutUtility.GetRect(780f, 340f, GUILayout.ExpandWidth(true));
            DrawArenaPreview(arenaRect);

            GUILayout.Space(8);
            GUILayout.Label("Logs:");
            var startIndex = Mathf.Max(0, _logs.Count - 16);
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
