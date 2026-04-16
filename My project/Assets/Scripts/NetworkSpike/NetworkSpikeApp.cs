using System;
using System.Collections.Generic;
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
        private readonly List<string> _logs = new();
        private readonly NetworkSpikeClientConfig _config = new();
        private NetworkSpikeClient _client;
        private CancellationTokenSource _lifetimeCts;
        private string _playerName = "PlayerA";
        private string _roomCode = string.Empty;
        private string _protocolVersionOverride = string.Empty;
        private int _tick;
        private bool _autoHeartbeat = true;
        private bool _readyRequested;
        private Rect _window = new(20, 20, 620, 680);
        private SpikeServerMessage _lastServerMessage = new SpikeServerMessage();

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

            if (string.Equals(_lastServerMessage.RoomState, "Active", StringComparison.OrdinalIgnoreCase))
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

            if (GUILayout.Button("Send Input Frame") && string.Equals(_lastServerMessage.RoomState, "Active", StringComparison.OrdinalIgnoreCase))
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
            if (string.Equals(_lastServerMessage.RoomState, "Active", StringComparison.OrdinalIgnoreCase))
            {
                GUILayout.Label("Move with WASD, aim with the mouse, and left click to drive authoritative pickup/trap/slow checks.");
            }

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
    }
}
