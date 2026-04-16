using System.Net;
using System.Net.Sockets;

namespace BatteryRushArena.NetworkSpikeServer;

/// <summary>
/// Hosts the network-session spike server and minimal room lifecycle.
/// </summary>
public sealed class SpikeServerHost
{
    private readonly SpikeServerConfig _config;
    private readonly RoomRegistry _roomRegistry;
    private readonly List<ClientSession> _sessions = new();
    private readonly object _sync = new();
    private readonly TcpListener _listener;

    public SpikeServerHost(SpikeServerConfig config, RoomRegistry roomRegistry)
    {
        _config = config;
        _roomRegistry = roomRegistry;
        _listener = new TcpListener(IPAddress.Parse(config.Host), config.Port);
    }

    /// <summary>
    /// Starts the accept loop and stale-session monitor.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        Console.WriteLine($"[server] listening on {_config.Host}:{_config.Port}");

        var staleMonitor = MonitorStaleSessionsAsync(cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(() => HandleClientAsync(new ClientSession(client), cancellationToken), cancellationToken);
            }
        }
        finally
        {
            _listener.Stop();
            await staleMonitor;
        }
    }

    private async Task HandleClientAsync(ClientSession session, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _sessions.Add(session);
        }

        try
        {
            Console.WriteLine($"[server] client connected {session.SessionId}");
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await LengthPrefixedProtocol.ReadAsync<ClientMessage>(session.Stream, cancellationToken);
                if (message is null)
                {
                    break;
                }

                session.LastSeenUtc = DateTimeOffset.UtcNow;
                switch (message.Type)
                {
                    case "hello":
                        await HandleHelloAsync(session, message, cancellationToken);
                        break;
                    case "create_room":
                        await HandleCreateRoomAsync(session, message, cancellationToken);
                        break;
                    case "join_room":
                        await HandleJoinRoomAsync(session, message, cancellationToken);
                        break;
                    case "heartbeat":
                        await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
                        {
                            Type = "heartbeat_ack",
                            SessionId = session.SessionId,
                            Detail = "alive"
                        }, cancellationToken);
                        break;
                    case "input_frame":
                        await HandleInputFrameAsync(session, message, cancellationToken);
                        break;
                    default:
                        await SendErrorAsync(session, "unsupported_message_type", cancellationToken);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException)
        {
            Console.WriteLine($"[server] session {session.SessionId} ended with error: {ex.Message}");
        }
        finally
        {
            _roomRegistry.Remove(session);
            lock (_sync)
            {
                _sessions.Remove(session);
            }

            session.TcpClient.Dispose();
            Console.WriteLine($"[server] client disconnected {session.SessionId}");
        }
    }

    private async Task HandleHelloAsync(ClientSession session, ClientMessage message, CancellationToken cancellationToken)
    {
        if (!string.Equals(message.ProtocolVersion, _config.ProtocolVersion, StringComparison.Ordinal))
        {
            await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
            {
                Type = "hello_rejected",
                Error = "protocol_mismatch",
                Detail = $"expected {_config.ProtocolVersion}, got {message.ProtocolVersion}"
            }, cancellationToken);
            session.TcpClient.Close();
            return;
        }

        session.PlayerName = message.PlayerName;
        await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
        {
            Type = "hello_accepted",
            SessionId = session.SessionId,
            Detail = session.PlayerName
        }, cancellationToken);
    }

    private async Task HandleCreateRoomAsync(ClientSession session, ClientMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.PlayerName))
        {
            await SendErrorAsync(session, "handshake_required", cancellationToken);
            return;
        }

        _roomRegistry.Remove(session);
        session.RoomCode = _roomRegistry.CreateRoom(session);
        await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
        {
            Type = "room_joined",
            SessionId = session.SessionId,
            RoomCode = session.RoomCode,
            Detail = "created"
        }, cancellationToken);
        Console.WriteLine($"[server] room created {session.RoomCode} by {session.PlayerName}");
    }

    private async Task HandleJoinRoomAsync(ClientSession session, ClientMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.PlayerName))
        {
            await SendErrorAsync(session, "handshake_required", cancellationToken);
            return;
        }

        _roomRegistry.Remove(session);
        if (!_roomRegistry.TryJoinRoom(message.RoomCode, session, _config.MaxPlayersPerRoom, out var error))
        {
            await SendErrorAsync(session, error, cancellationToken);
            return;
        }

        session.RoomCode = message.RoomCode;
        await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
        {
            Type = "room_joined",
            SessionId = session.SessionId,
            RoomCode = session.RoomCode,
            Detail = "joined"
        }, cancellationToken);
        Console.WriteLine($"[server] room joined {session.RoomCode} by {session.PlayerName}");
    }

    private async Task HandleInputFrameAsync(ClientSession session, ClientMessage message, CancellationToken cancellationToken)
    {
        if (message.Tick <= session.LastProcessedTick)
        {
            await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
            {
                Type = "input_frame_ignored",
                SessionId = session.SessionId,
                Tick = message.Tick,
                Error = "stale_or_duplicate_tick"
            }, cancellationToken);
            return;
        }

        session.LastProcessedTick = message.Tick;
        await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
        {
            Type = "input_frame_ack",
            SessionId = session.SessionId,
            Tick = message.Tick,
            Detail = $"mx={message.MoveX:F2},my={message.MoveY:F2},fire={message.FirePressed}"
        }, cancellationToken);
        Console.WriteLine($"[server] input ack session={session.SessionId} tick={message.Tick}");
    }

    private async Task MonitorStaleSessionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            List<ClientSession> staleSessions;
            lock (_sync)
            {
                staleSessions = _sessions.Where(session => DateTimeOffset.UtcNow - session.LastSeenUtc >= _config.StaleTimeout).ToList();
            }

            foreach (var session in staleSessions)
            {
                try
                {
                    await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
                    {
                        Type = "session_stale",
                        SessionId = session.SessionId,
                        Error = "heartbeat_timeout",
                        Detail = $"stale after {_config.StaleTimeout.TotalSeconds:F0}s"
                    }, cancellationToken);
                }
                catch
                {
                    // ignore best-effort notification failure
                }

                session.TcpClient.Close();
            }
        }
    }

    private Task SendErrorAsync(ClientSession session, string error, CancellationToken cancellationToken) =>
        LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
        {
            Type = "error",
            SessionId = session.SessionId,
            Error = error
        }, cancellationToken);
}
