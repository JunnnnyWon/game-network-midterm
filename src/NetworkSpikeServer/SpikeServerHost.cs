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
        var roomMonitor = MonitorRoomsAsync(cancellationToken);
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
            await Task.WhenAll(staleMonitor, roomMonitor);
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
                        await HandleCreateRoomAsync(session, cancellationToken);
                        break;
                    case "join_room":
                        await HandleJoinRoomAsync(session, message, cancellationToken);
                        break;
                    case "ready_state":
                        await HandleReadyStateAsync(session, message, cancellationToken);
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
            var affectedRoom = _roomRegistry.Remove(session);
            if (affectedRoom is not null)
            {
                ApplyDisconnectStateTransition(affectedRoom);
                await BroadcastRoomAsync(affectedRoom, "member_removed", cancellationToken);
            }

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

    private async Task HandleCreateRoomAsync(ClientSession session, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.PlayerName))
        {
            await SendErrorAsync(session, "handshake_required", cancellationToken);
            return;
        }

        _roomRegistry.Remove(session);
        var room = _roomRegistry.CreateRoom(session);
        await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
        {
            Type = "room_joined",
            SessionId = session.SessionId,
            RoomCode = room.RoomCode,
            Detail = "created"
        }, cancellationToken);
        await BroadcastRoomAsync(room, "room_joined", cancellationToken);
        Console.WriteLine($"[server] room created {room.RoomCode} by {session.PlayerName}");
    }

    private async Task HandleJoinRoomAsync(ClientSession session, ClientMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.PlayerName))
        {
            await SendErrorAsync(session, "handshake_required", cancellationToken);
            return;
        }

        _roomRegistry.Remove(session);
        if (!_roomRegistry.TryJoinRoom(message.RoomCode, session, _config.MaxPlayersPerRoom, out var room, out var error))
        {
            await SendErrorAsync(session, error, cancellationToken);
            return;
        }

        var joinedRoom = room!;
        await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
        {
            Type = "room_joined",
            SessionId = session.SessionId,
            RoomCode = joinedRoom.RoomCode,
            Detail = "joined"
        }, cancellationToken);
        await BroadcastRoomAsync(joinedRoom, "room_joined", cancellationToken);
        Console.WriteLine($"[server] room joined {joinedRoom.RoomCode} by {session.PlayerName}");
    }

    private async Task HandleReadyStateAsync(ClientSession session, ClientMessage message, CancellationToken cancellationToken)
    {
        if (!_roomRegistry.TrySetReady(session, message.IsReady, out var room))
        {
            await SendErrorAsync(session, "not_in_room", cancellationToken);
            return;
        }

        await BroadcastRoomAsync(room!, "ready_state_changed", cancellationToken);
    }

    private async Task HandleInputFrameAsync(ClientSession session, ClientMessage message, CancellationToken cancellationToken)
    {
        var room = _roomRegistry.FindRoom(session);
        if (room is null)
        {
            await SendErrorAsync(session, "not_in_room", cancellationToken);
            return;
        }

        if (room.State != SpikeRoomState.Active)
        {
            await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
            {
                Type = "input_frame_ignored",
                SessionId = session.SessionId,
                Tick = message.Tick,
                Error = "room_not_active"
            }, cancellationToken);
            return;
        }

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
                }

                session.TcpClient.Close();
            }
        }
    }

    private async Task MonitorRoomsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(250, cancellationToken);
            foreach (var room in _roomRegistry.SnapshotRooms())
            {
                var shouldBroadcast = false;
                var includeDetail = true;
                var messageType = "room_state_changed";

                lock (_roomRegistry.SyncRoot)
                {
                    switch (room.State)
                    {
                        case SpikeRoomState.Lobby:
                            if (room.Members.Count == _config.MaxPlayersPerRoom && room.ReadyBySessionId.Values.Count(v => v) == _config.MaxPlayersPerRoom)
                            {
                                room.State = SpikeRoomState.Countdown;
                                room.StateEnteredUtc = DateTimeOffset.UtcNow;
                                room.CountdownEndsUtc = DateTimeOffset.UtcNow.AddSeconds(3);
                                shouldBroadcast = true;
                                messageType = "room_state_changed";
                            }
                            break;
                        case SpikeRoomState.Countdown:
                            if (room.Members.Count < _config.MaxPlayersPerRoom || room.ReadyBySessionId.Values.Count(v => v) < _config.MaxPlayersPerRoom)
                            {
                                room.State = SpikeRoomState.Lobby;
                                room.StateEnteredUtc = DateTimeOffset.UtcNow;
                                room.EndReason = string.Empty;
                                shouldBroadcast = true;
                                messageType = "room_state_changed";
                            }
                            else if (DateTimeOffset.UtcNow >= room.CountdownEndsUtc)
                            {
                                room.State = SpikeRoomState.Active;
                                room.StateEnteredUtc = DateTimeOffset.UtcNow;
                                room.ActiveEndsUtc = DateTimeOffset.UtcNow.AddSeconds(5);
                                room.EndReason = string.Empty;
                                shouldBroadcast = true;
                                messageType = "room_state_changed";
                            }
                            else
                            {
                                shouldBroadcast = true;
                                messageType = "countdown_tick";
                                includeDetail = false;
                            }
                            break;
                        case SpikeRoomState.Active:
                            if (DateTimeOffset.UtcNow >= room.ActiveEndsUtc)
                            {
                                room.State = SpikeRoomState.Ended;
                                room.StateEnteredUtc = DateTimeOffset.UtcNow;
                                room.EndReason = string.IsNullOrWhiteSpace(room.EndReason) ? "timebox_complete" : room.EndReason;
                                shouldBroadcast = true;
                                messageType = "room_state_changed";
                            }
                            break;
                        case SpikeRoomState.Ended:
                            if (DateTimeOffset.UtcNow - room.StateEnteredUtc >= TimeSpan.FromSeconds(0.5))
                            {
                                room.State = SpikeRoomState.Saving;
                                room.StateEnteredUtc = DateTimeOffset.UtcNow;
                                shouldBroadcast = true;
                                messageType = "room_state_changed";
                            }
                            break;
                        case SpikeRoomState.Saving:
                            if (DateTimeOffset.UtcNow - room.StateEnteredUtc >= TimeSpan.FromSeconds(0.5))
                            {
                                room.State = SpikeRoomState.ResultsReady;
                                room.StateEnteredUtc = DateTimeOffset.UtcNow;
                                shouldBroadcast = true;
                                messageType = "room_state_changed";
                            }
                            break;
                    }
                }

                if (shouldBroadcast)
                {
                    await BroadcastRoomAsync(room, messageType, cancellationToken, includeDetail);
                }
            }
        }
    }

    private void ApplyDisconnectStateTransition(SpikeRoom room)
    {
        lock (_roomRegistry.SyncRoot)
        {
            if (room.State == SpikeRoomState.Countdown)
        {
            room.State = SpikeRoomState.Lobby;
            room.StateEnteredUtc = DateTimeOffset.UtcNow;
            room.EndReason = string.Empty;
            return;
        }

        if (room.State == SpikeRoomState.Active && room.Members.Count == 1)
        {
            room.State = SpikeRoomState.Ended;
            room.StateEnteredUtc = DateTimeOffset.UtcNow;
            room.EndReason = "DisconnectForfeit";
        }
        }
    }

    private async Task BroadcastRoomAsync(SpikeRoom room, string messageType, CancellationToken cancellationToken, bool includeDetail = true)
    {
        string roomCode;
        string roomState;
        int playerCount;
        int readyPlayers;
        float countdownRemaining;
        string endReason;
        string persistenceStatus;
        string[] members;
        ClientSession[] targets;

        lock (_roomRegistry.SyncRoot)
        {
            roomCode = room.RoomCode;
            roomState = room.State.ToString();
            playerCount = room.Members.Count;
            readyPlayers = room.ReadyBySessionId.Values.Count(v => v);
            countdownRemaining = room.State == SpikeRoomState.Countdown
                ? Math.Max(0f, (float)(room.CountdownEndsUtc - DateTimeOffset.UtcNow).TotalSeconds)
                : 0f;
            endReason = room.EndReason;
            persistenceStatus = room.State switch
            {
                SpikeRoomState.Saving => "Saving",
                SpikeRoomState.ResultsReady => "Saved",
                _ => string.Empty
            };
            members = room.Members.Select(member => member.PlayerName).ToArray();
            targets = room.Members.ToArray();
        }

        foreach (var member in targets)
        {
            try
            {
                var message = new ServerMessage
                {
                    Type = "room_snapshot",
                    RoomCode = roomCode,
                    SessionId = member.SessionId,
                    Detail = includeDetail ? messageType : string.Empty,
                    RoomState = roomState,
                    PlayerCount = playerCount,
                    ReadyPlayers = readyPlayers,
                    CountdownRemainingSeconds = countdownRemaining,
                    EndReason = endReason,
                    PersistenceStatus = persistenceStatus,
                    Members = members
                };
                await LengthPrefixedProtocol.WriteAsync(member.Stream, message, cancellationToken);
            }
            catch
            {
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
