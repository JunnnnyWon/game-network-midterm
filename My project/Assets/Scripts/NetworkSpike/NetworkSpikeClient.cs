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

        public NetworkSpikeClient(NetworkSpikeClientConfig config, Func<DateTimeOffset> clock = null)
        {
            _config = config;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _lastGameplaySendUtc = _clock();
        }

        public event Action<SpikeServerMessage> MessageReceived;

        public event Action<string> LogEmitted;

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;

        public async Task ConnectAndHandshakeAsync(string playerName, string protocolVersionOverride = "", CancellationToken cancellationToken = default)
        {
            if (IsConnected)
            {
                return;
            }

            _tcpClient = new TcpClient();
            using (cancellationToken.Register(() =>
                   {
                       try
                       {
                           _tcpClient.Close();
                       }
                       catch
                       {
                       }
                   }))
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
            LogEmitted?.Invoke($"Connected to {_config.Host}:{_config.Port}, handshake sent.");
        }

        public Task CreateRoomAsync(CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage { Type = "create_room" }, cancellationToken);

        public Task JoinRoomAsync(string roomCode, CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage { Type = "join_room", RoomCode = roomCode }, cancellationToken);

        public Task SetReadyAsync(bool isReady, CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage { Type = "ready_state", IsReady = isReady }, cancellationToken);

        public Task CollectBatteryAsync(int batteryId, CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage { Type = "collect_battery", BatteryId = batteryId }, cancellationToken);

        public Task FireSlowShotAsync(CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage { Type = "fire_slow_shot" }, cancellationToken);

        public Task TriggerTrapAsync(int trapId, CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage { Type = "trigger_trap", TrapId = trapId }, cancellationToken);

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
            LogEmitted?.Invoke("Heartbeat sent.");
        }

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
            LogEmitted?.Invoke($"Input frame sent tick={tick} move=({move.x:F2},{move.y:F2}) fire={firePressed}.");
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
            if (_stream == null)
            {
                throw new InvalidOperationException("Client is not connected.");
            }

            await LengthPrefixedProtocol.WriteAsync(_stream, message, cancellationToken);
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                return;
            }

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await LengthPrefixedProtocol.ReadAsync<SpikeServerMessage>(_stream, cancellationToken);
                    if (message == null)
                    {
                        break;
                    }

                    MessageReceived?.Invoke(message);
                    LogEmitted?.Invoke($"Server[{message.Type}] {message.Detail} {message.Error}".Trim());
                }
            }
            catch (Exception ex) when (ex is SocketException || ex is InvalidDataException || ex is OperationCanceledException)
            {
                if (!(ex is OperationCanceledException))
                {
                    LogEmitted?.Invoke($"Read loop stopped: {ex.Message}");
                }
            }
        }
    }
}
