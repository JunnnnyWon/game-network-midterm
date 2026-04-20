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
        private Task _heartbeatTask;
        private DateTimeOffset _lastTransportSendUtc;
        private bool _sessionEstablished;
        private string _connectedPlayerName = string.Empty;
        private DateTimeOffset _lastMessageReceivedUtc = DateTimeOffset.MinValue;
        private DateTimeOffset _lastHeartbeatAckUtc = DateTimeOffset.MinValue;
        private long _lastHeartbeatRttMs = -1;
        private int _lastSnapshotSequence;
        private int _lastServerTick;
        private int _lastAckedClientTick;
        private int _messagesReceivedCount;
        private string _lastMessageType = string.Empty;

        public NetworkSpikeClient(NetworkSpikeClientConfig config, Func<DateTimeOffset> clock = null)
        {
            _config = config;
            _clock = clock ?? (() => DateTimeOffset.UtcNow);
            _mainThreadContext = SynchronizationContext.Current;
            _lastTransportSendUtc = _clock();
        }

        public event Action<SpikeServerMessage> MessageReceived;

        public event Action<string> LogEmitted;

        public event Action ConnectionClosed;

        public bool IsConnected => _sessionEstablished;

        public string ConnectedPlayerName => _connectedPlayerName;

        public DateTimeOffset LastMessageReceivedUtc => _lastMessageReceivedUtc;

        public DateTimeOffset LastHeartbeatAckUtc => _lastHeartbeatAckUtc;

        public long LastHeartbeatRttMs => _lastHeartbeatRttMs;

        public int LastSnapshotSequence => _lastSnapshotSequence;

        public int LastServerTick => _lastServerTick;

        public int LastAckedClientTick => _lastAckedClientTick;

        public int MessagesReceivedCount => _messagesReceivedCount;

        public string LastMessageType => _lastMessageType;

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
                PlayerName = playerName,
                ClientSentAtUnixMs = _clock().ToUnixTimeMilliseconds()
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
            _connectedPlayerName = string.IsNullOrWhiteSpace(helloResponse.Detail) ? playerName : helloResponse.Detail;
            _readerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var connectionCts = _readerCts;
            var connectionClient = _tcpClient;
            var connectionStream = _stream;
            _readerTask = Task.Run(() => ReadLoopAsync(connectionCts, connectionClient, connectionStream, connectionCts.Token), connectionCts.Token);
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(connectionCts.Token), connectionCts.Token);
            LogEmitted?.Invoke($"Connected to {_config.Host}:{_config.Port}, handshake sent.");
        }

        public Task CreateRoomAsync(CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage
            {
                Type = "create_room",
                ClientSentAtUnixMs = _clock().ToUnixTimeMilliseconds()
            }, cancellationToken);

        public Task JoinRoomAsync(string roomCode, CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage
            {
                Type = "join_room",
                RoomCode = roomCode,
                ClientSentAtUnixMs = _clock().ToUnixTimeMilliseconds()
            }, cancellationToken);

        public Task SetReadyAsync(bool isReady, CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage
            {
                Type = "ready_state",
                IsReady = isReady,
                ClientSentAtUnixMs = _clock().ToUnixTimeMilliseconds()
            }, cancellationToken);

        public Task StartMatchAsync(CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage
            {
                Type = "start_match",
                StartRequested = true,
                ClientSentAtUnixMs = _clock().ToUnixTimeMilliseconds()
            }, cancellationToken);

        public Task LeaveRoomAsync(CancellationToken cancellationToken = default) =>
            SendAsync(new SpikeClientMessage
            {
                Type = "leave_room",
                ClientSentAtUnixMs = _clock().ToUnixTimeMilliseconds()
            }, cancellationToken);

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

            await SendAsync(new SpikeClientMessage
            {
                Type = "heartbeat",
                ClientSentAtUnixMs = _clock().ToUnixTimeMilliseconds()
            }, cancellationToken);
            _lastTransportSendUtc = _clock();
            LogEmitted?.Invoke("Heartbeat sent.");
        }

        private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.25f, _config.HeartbeatIntervalSeconds * 0.5f)), cancellationToken);
                    await MaybeSendHeartbeatAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                DispatchToMainThread(() => LogEmitted?.Invoke($"Heartbeat loop stopped: {ex.Message}"));
            }
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
                FirePressed = firePressed,
                ClientSentAtUnixMs = _clock().ToUnixTimeMilliseconds()
            }, cancellationToken);
            _lastTransportSendUtc = _clock();
            LogEmitted?.Invoke($"Input frame sent tick={tick} move=({move.x:F2},{move.y:F2}) fire={firePressed}.");
        }

        public void Dispose()
        {
            ResetConnectionState();
        }

        public void Disconnect()
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

        private async Task ReadLoopAsync(CancellationTokenSource ownerCts, TcpClient ownerClient, NetworkStream ownerStream, CancellationToken cancellationToken)
        {
            if (ownerStream == null)
            {
                return;
            }

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var message = await LengthPrefixedProtocol.ReadAsync<SpikeServerMessage>(ownerStream, cancellationToken);
                    if (message == null)
                    {
                        break;
                    }

                    TrackTelemetry(message);
                    DispatchToMainThread(() => MessageReceived?.Invoke(message));
                    DispatchToMainThread(() => LogEmitted?.Invoke($"Server[{message.Type}] {message.Detail} {message.Error}".Trim()));
                }
            }
            catch (Exception ex) when (ex is SocketException || ex is InvalidDataException || ex is OperationCanceledException || ex is IOException)
            {
                if (ex is OperationCanceledException || cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (ex is IOException ioException)
                {
                    var isExpectedDisconnect =
                        ioException.Message.IndexOf("interrupted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ioException.Message.IndexOf("aborted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ioException.Message.IndexOf("closed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ioException.Message.IndexOf("취소", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ioException.Message.IndexOf("스레드 종료", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isExpectedDisconnect)
                    {
                        return;
                    }
                }

                DispatchToMainThread(() => LogEmitted?.Invoke($"Read loop stopped: {ex.Message}"));
            }
            catch (Exception ex)
            {
                if (ex is IOException ioException &&
                    (ioException.Message.IndexOf("aborted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     ioException.Message.IndexOf("disposed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     ioException.Message.IndexOf("closed", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return;
                }

                DispatchToMainThread(() => LogEmitted?.Invoke($"Read loop crashed: {ex.GetType().Name}: {ex.Message}"));
            }
            finally
            {
                ResetConnectionStateIfCurrent(ownerCts, ownerClient, ownerStream);
            }
        }

        private void ResetConnectionStateIfCurrent(CancellationTokenSource ownerCts, TcpClient ownerClient, NetworkStream ownerStream)
        {
            if (!ReferenceEquals(_readerCts, ownerCts) ||
                !ReferenceEquals(_tcpClient, ownerClient) ||
                !ReferenceEquals(_stream, ownerStream))
            {
                return;
            }

            ResetConnectionState();
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

        private void TrackTelemetry(SpikeServerMessage message)
        {
            var nowUtc = _clock();
            _lastMessageReceivedUtc = nowUtc;
            _lastMessageType = message.Type ?? string.Empty;
            _messagesReceivedCount += 1;

            if (message.Tick > 0)
            {
                _lastServerTick = message.Tick;
            }

            if (message.SnapshotSequence > 0)
            {
                _lastSnapshotSequence = message.SnapshotSequence;
            }

            if (message.LastProcessedClientTick > 0)
            {
                _lastAckedClientTick = message.LastProcessedClientTick;
            }

            if (string.Equals(message.Type, "heartbeat_ack", StringComparison.OrdinalIgnoreCase))
            {
                _lastHeartbeatAckUtc = nowUtc;
                if (message.ClientSentAtUnixMs > 0)
                {
                    _lastHeartbeatRttMs = Math.Max(0, nowUtc.ToUnixTimeMilliseconds() - message.ClientSentAtUnixMs);
                }
            }
        }

        private void ResetConnectionState()
        {
            var shouldNotifyConnectionClosed = _stream != null || _tcpClient != null || _sessionEstablished;

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
            _heartbeatTask = null;
            _readerCts = null;
            _sessionEstablished = false;
            _connectedPlayerName = string.Empty;
            _lastMessageReceivedUtc = DateTimeOffset.MinValue;
            _lastHeartbeatAckUtc = DateTimeOffset.MinValue;
            _lastHeartbeatRttMs = -1;
            _lastSnapshotSequence = 0;
            _lastServerTick = 0;
            _lastAckedClientTick = 0;
            _messagesReceivedCount = 0;
            _lastMessageType = string.Empty;

            if (shouldNotifyConnectionClosed)
            {
                DispatchToMainThread(() => ConnectionClosed?.Invoke());
            }
        }
    }
}
