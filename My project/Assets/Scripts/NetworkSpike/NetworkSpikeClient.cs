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
        private readonly SynchronizationContext _mainThreadContext;
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private CancellationTokenSource _readerCts;
        private Task _readerTask;
        private DateTimeOffset _lastTransportSendUtc;
        private bool _sessionEstablished;

        public NetworkSpikeClient(NetworkSpikeClientConfig config, Func<DateTimeOffset> clock = null)
        {
            _config = config;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _mainThreadContext = SynchronizationContext.Current;
            _lastTransportSendUtc = _clock();
        }

        public event Action<SpikeServerMessage> MessageReceived;

        public event Action<string> LogEmitted;

        public bool IsConnected => _sessionEstablished;

        public async Task ConnectAndHandshakeAsync(string playerName, string protocolVersionOverride = "", CancellationToken cancellationToken = default)
        {
            if (_tcpClient != null && !IsConnected)
            {
                ResetConnectionState();
            }

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

            await SendAsync(new SpikeClientMessage
            {
                Type = "hello",
                ProtocolVersion = string.IsNullOrWhiteSpace(protocolVersionOverride) ? _config.ProtocolVersion : protocolVersionOverride,
                PlayerName = playerName
            }, cancellationToken);

            var helloResponse = await LengthPrefixedProtocol.ReadAsync<SpikeServerMessage>(_stream, cancellationToken);
            if (helloResponse == null)
            {
                ResetConnectionState();
                throw new IOException("Handshake response was not received.");
            }

            DispatchToMainThread(() => MessageReceived?.Invoke(helloResponse));
            DispatchToMainThread(() => LogEmitted?.Invoke($"Server[{helloResponse.Type}] {helloResponse.Detail} {helloResponse.Error}".Trim()));

            if (string.Equals(helloResponse.Type, "hello_rejected", StringComparison.Ordinal))
            {
                ResetConnectionState();
                throw new InvalidOperationException($"Handshake rejected: {helloResponse.Error} {helloResponse.Detail}".Trim());
            }

            _sessionEstablished = true;
            _readerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readerTask = Task.Run(() => ReadLoopAsync(_readerCts.Token), _readerCts.Token);
            LogEmitted?.Invoke($"Connected to {_config.Host}:{_config.Port}, handshake sent.");
        }

        public Task CreateRoomAsync(CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage { Type = "create_room" }, cancellationToken);

        public Task JoinRoomAsync(string roomCode, CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage { Type = "join_room", RoomCode = roomCode }, cancellationToken);

        public Task SetReadyAsync(bool isReady, CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage { Type = "ready_state", IsReady = isReady }, cancellationToken);

        public async Task MaybeSendHeartbeatAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                return;
            }

            if ((_clock() - _lastTransportSendUtc).TotalSeconds < _config.HeartbeatIntervalSeconds)
            {
                return;
            }

            await SendAsync(new SpikeClientMessage { Type = "heartbeat" }, cancellationToken);
            _lastTransportSendUtc = _clock();
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
            _lastTransportSendUtc = _clock();
            LogEmitted?.Invoke($"Input frame sent tick={tick} move=({move.x:F2},{move.y:F2}) fire={firePressed}.");
        }

        public void Dispose()
        {
            ResetConnectionState();
        }

        private async Task SendAsync(SpikeClientMessage message, CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException("Client is not connected.");
            }

            try
            {
                await LengthPrefixedProtocol.WriteAsync(_stream, message, cancellationToken);
                _lastTransportSendUtc = _clock();
            }
            catch
            {
                ResetConnectionState();
                throw;
            }
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

                    DispatchToMainThread(() => MessageReceived?.Invoke(message));
                    DispatchToMainThread(() => LogEmitted?.Invoke($"Server[{message.Type}] {message.Detail} {message.Error}".Trim()));
                }
            }
            catch (Exception ex) when (ex is SocketException || ex is InvalidDataException || ex is OperationCanceledException)
            {
                if (!(ex is OperationCanceledException))
                {
                    DispatchToMainThread(() => LogEmitted?.Invoke($"Read loop stopped: {ex.Message}"));
                }
            }
            catch (Exception ex)
            {
                DispatchToMainThread(() => LogEmitted?.Invoke($"Read loop crashed: {ex.GetType().Name}: {ex.Message}"));
            }
            finally
            {
                ResetConnectionState();
            }
        }

        private void DispatchToMainThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            if (_mainThreadContext == null || SynchronizationContext.Current == _mainThreadContext)
            {
                action();
                return;
            }

            _mainThreadContext.Post(_ => action(), null);
        }

        private void ResetConnectionState()
        {
            try
            {
                _readerCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                _stream?.Dispose();
            }
            catch
            {
            }

            try
            {
                _tcpClient?.Dispose();
            }
            catch
            {
            }

            try
            {
                _readerCts?.Dispose();
            }
            catch
            {
            }

            _stream = null;
            _tcpClient = null;
            _readerTask = null;
            _readerCts = null;
            _sessionEstablished = false;
        }
    }
}
