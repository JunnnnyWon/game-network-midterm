using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BatteryRushArena.NetworkSpike
{

/// <summary>
/// Configuration for the Unity-side transport spike client.
/// </summary>
[Serializable]
public sealed class NetworkSpikeClientConfig
{
    public string Host = "127.0.0.1";
    public int Port = 7777;
    public string ProtocolVersion = "bra-spike-v1";
    public float HeartbeatIntervalSeconds = 2f;
    public float TickIntervalSeconds = 0.05f;
}

/// <summary>
/// Thin Unity-side transport client used by the network-session spike.
/// </summary>
public sealed class NetworkSpikeClient : IDisposable
{
    private readonly NetworkSpikeClientConfig _config;
    private readonly Func<DateTimeOffset> _clock;
    private TcpClient _tcpClient;
    private NetworkStream _stream;
    private CancellationTokenSource _readerCts;
    private Task _readerTask;
    private DateTimeOffset _lastGameplaySendUtc;

    /// <summary>
    /// Initializes a spike client with the provided configuration.
    /// </summary>
    public NetworkSpikeClient(NetworkSpikeClientConfig config, Func<DateTimeOffset> clock = null)
    {
        _config = config;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _lastGameplaySendUtc = _clock();
    }

    /// <summary>
    /// Raised when a server message arrives.
    /// </summary>
    public event Action<SpikeServerMessage> MessageReceived;

    /// <summary>
    /// Raised when the client wants to append a visible local log line.
    /// </summary>
    public event Action<string> LogEmitted;

    /// <summary>
    /// Gets whether the underlying TCP connection is currently open.
    /// </summary>
    public bool IsConnected => _tcpClient != null && _tcpClient.Connected;

    /// <summary>
    /// Connects to the configured server and performs the protocol handshake.
    /// </summary>
    public async Task ConnectAndHandshakeAsync(string playerName, string protocolVersionOverride = "", CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        _tcpClient = new TcpClient();
        using (cancellationToken.Register(() => { try { _tcpClient.Close(); } catch { } }))
        {
            await _tcpClient.ConnectAsync(_config.Host, _config.Port);
        }
        _stream = _tcpClient.GetStream();
        _readerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readerTask = Task.Run(() => ReadLoopAsync(_readerCts.Token), _readerCts.Token);

        await SendAsync(new SpikeClientMessage
        {
            Type = "hello",
            ProtocolVersion = string.IsNullOrWhiteSpace(protocolVersionOverride) ? _config.ProtocolVersion : protocolVersionOverride,
            PlayerName = playerName
        }, cancellationToken);
        if (LogEmitted != null) LogEmitted($"Connected to {_config.Host}:{_config.Port}, handshake sent.");
    }

    /// <summary>
    /// Requests room creation from the server.
    /// </summary>
    public Task CreateRoomAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new SpikeClientMessage { Type = "create_room" }, cancellationToken);

    /// <summary>
    /// Requests room join from the server.
    /// </summary>
    public Task JoinRoomAsync(string roomCode, CancellationToken cancellationToken = default) =>
        SendAsync(new SpikeClientMessage { Type = "join_room", RoomCode = roomCode }, cancellationToken);

    /// <summary>
    /// Sends authoritative ready-state intent.
    /// </summary>
    public Task SetReadyAsync(bool isReady, CancellationToken cancellationToken = default) =>
        SendAsync(new SpikeClientMessage { Type = "ready_state", IsReady = isReady }, cancellationToken);

    /// <summary>
    /// Sends a heartbeat when no gameplay input has been emitted recently.
    /// </summary>
    public async Task MaybeSendHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        if ((_clock() - _lastGameplaySendUtc).TotalSeconds < _config.HeartbeatIntervalSeconds)
        {
            return;
        }

        await SendAsync(new SpikeClientMessage { Type = "heartbeat" }, cancellationToken);
        if (LogEmitted != null) LogEmitted("Heartbeat sent.");
    }

    /// <summary>
    /// Sends one tick-aligned input frame.
    /// </summary>
    public async Task SendInputFrameAsync(int tick, Vector2 move, Vector2 aim, bool firePressed, CancellationToken cancellationToken = default)
    {
        await SendAsync(new SpikeClientMessage
        {
            Type = "input_frame",
            Tick = tick,
            MoveX = move.x,
            MoveY = move.y,
            AimX = aim.x,
            AimY = aim.y,
            FirePressed = firePressed
        }, cancellationToken);
        _lastGameplaySendUtc = _clock();
        if (LogEmitted != null) LogEmitted($"Input frame sent tick={tick} move=({move.x:F2},{move.y:F2}) fire={firePressed}.");
    }

    public void Dispose()
    {
        _readerCts?.Cancel();
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _readerCts?.Dispose();
    }

    private async Task SendAsync(SpikeClientMessage message, CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Client is not connected.");
        }

        await LengthPrefixedProtocol.WriteAsync(_stream, message, cancellationToken);
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await LengthPrefixedProtocol.ReadAsync<SpikeServerMessage>(_stream, cancellationToken);
                if (message is null)
                {
                    break;
                }

                if (MessageReceived != null) MessageReceived(message);
                if (LogEmitted != null) LogEmitted($"Server[{message.Type}] {message.Detail} {message.Error}".Trim());
            }
        }
        catch (Exception ex) when (ex is SocketException or InvalidDataException or OperationCanceledException)
        {
            if (ex is not OperationCanceledException)
            {
                if (LogEmitted != null) LogEmitted($"Read loop stopped: {ex.Message}");
            }
        }
    }
}
}
