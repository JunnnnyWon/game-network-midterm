using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    private Rect _window = new(20, 20, 520, 520);

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
        if (_client is null || !_client.IsConnected)
        {
            return;
        }

        if (_autoHeartbeat)
        {
            _ = _client.MaybeSendHeartbeatAsync(_lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
        }

        var move = ReadMoveVector();
        if (move.sqrMagnitude > 0.0001f)
        {
            _tick += 1;
            _ = _client.SendInputFrameAsync(_tick, move, Vector2.right, false, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
        }

        if (Mouse.current?.leftButton.wasPressedThisFrame == true)
        {
            _tick += 1;
            _ = _client.SendInputFrameAsync(_tick, ReadMoveVector(), Vector2.right, true, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
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
            if (_client != null) _ = _client.ConnectAndHandshakeAsync(_playerName, _protocolVersionOverride, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
        }

        if (GUILayout.Button("Create Room"))
        {
            if (_client != null) _ = _client.CreateRoomAsync(_lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
        }

        if (GUILayout.Button("Join Room"))
        {
            if (_client != null) _ = _client.JoinRoomAsync(_roomCode, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
        }
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Send Input Frame"))
        {
            _tick += 1;
            if (_client != null) _ = _client.SendInputFrameAsync(_tick, ReadMoveVector(), Vector2.right, Mouse.current != null && Mouse.current.leftButton.isPressed, _lifetimeCts != null ? _lifetimeCts.Token : CancellationToken.None);
        }

        _autoHeartbeat = GUILayout.Toggle(_autoHeartbeat, "Auto heartbeat when idle");
        GUILayout.Label($"Connected: {(_client != null && _client.IsConnected)}");
        GUILayout.Label($"Last Tick Sent: {_tick}");
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
        if (!string.IsNullOrWhiteSpace(message.RoomCode))
        {
            _roomCode = message.RoomCode;
        }
    }

    private static Vector2 ReadMoveVector()
    {
        var move = Vector2.zero;
        if (Keyboard.current?.wKey.isPressed == true) move.y += 1f;
        if (Keyboard.current?.sKey.isPressed == true) move.y -= 1f;
        if (Keyboard.current?.aKey.isPressed == true) move.x -= 1f;
        if (Keyboard.current?.dKey.isPressed == true) move.x += 1f;
        return move.sqrMagnitude > 1f ? move.normalized : move;
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
