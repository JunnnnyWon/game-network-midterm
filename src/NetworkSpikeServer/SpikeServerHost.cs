using System.Net;
using System.Net.Sockets;

namespace BatteryRushArena.NetworkSpikeServer;

/// <summary>
/// Hosts the network-session spike server and minimal room lifecycle.
/// </summary>
public sealed class SpikeServerHost
{
    private const float VisualPickupHalfExtent = 0.72f;
    private readonly SpikeServerConfig _config;
    private readonly RoomRegistry _roomRegistry;
    private readonly MySqlPersistenceService _persistenceService;
    private readonly List<ClientSession> _sessions = new();
    private readonly object _sync = new();
    private readonly TcpListener _listener;

    public SpikeServerHost(SpikeServerConfig config, RoomRegistry roomRegistry, MySqlPersistenceService persistenceService)
    {
        _config = config;
        _roomRegistry = roomRegistry;
        _persistenceService = persistenceService;
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
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _listener.Stop();
            try
            {
                await Task.WhenAll(staleMonitor, roomMonitor);
            }
            catch (OperationCanceledException)
            {
            }
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
                    case "start_match":
                        await HandleStartMatchAsync(session, cancellationToken);
                        break;
                    case "heartbeat":
                        await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
                        {
                            Type = "heartbeat_ack",
                            SessionId = session.SessionId,
                            Tick = session.LastProcessedTick,
                            Detail = "alive",
                            RoomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom),
                            ClientSentAtUnixMs = message.ClientSentAtUnixMs,
                            ServerSentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            LastProcessedClientTick = session.LastProcessedTick,
                            HeartbeatAgeSeconds = 0f
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
            var roomBeforeRemoval = _roomRegistry.FindRoom(session);
            if (roomBeforeRemoval is not null)
            {
                lock (_roomRegistry.SyncRoot)
                {
                    if (roomBeforeRemoval.State == SpikeRoomState.Active)
                    {
                        MarkRoomEnded(roomBeforeRemoval, "DisconnectForfeit", session.PlayerName);
                    }
                }
            }

            var affectedRoom = _roomRegistry.Remove(session);
            if (affectedRoom is not null)
            {
                ApplyDisconnectStateTransition(affectedRoom);
                await BroadcastRoomAsync(affectedRoom, "member_removed", cancellationToken);
            }

            await BroadcastRoomListingsAsync(cancellationToken);

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

        if (string.IsNullOrWhiteSpace(message.PlayerName))
        {
            await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
            {
                Type = "hello_rejected",
                Error = "invalid_player_name",
                Detail = "player name is required"
            }, cancellationToken);
            session.TcpClient.Close();
            return;
        }

        session.PlayerName = ResolveUniquePlayerName(message.PlayerName, session.SessionId);
        await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
        {
            Type = "hello_accepted",
            SessionId = session.SessionId,
            Detail = session.PlayerName,
            RoomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom),
            ServerSentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ClientSentAtUnixMs = message.ClientSentAtUnixMs
        }, cancellationToken);
    }

    private string ResolveUniquePlayerName(string requestedName, string sessionId)
    {
        var baseName = requestedName.Trim();
        lock (_sync)
        {
            var existingNames = _sessions
                .Where(existing => !string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal))
                .Select(existing => existing.PlayerName)
                .Where(existing => !string.IsNullOrWhiteSpace(existing))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existingNames.Contains(baseName))
            {
                return baseName;
            }

            var suffix = 2;
            while (true)
            {
                var candidate = $"{baseName}#{suffix}";
                if (!existingNames.Contains(candidate))
                {
                    return candidate;
                }

                suffix += 1;
            }
        }
    }

    private async Task HandleCreateRoomAsync(ClientSession session, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.PlayerName))
        {
            await SendErrorAsync(session, "handshake_required", cancellationToken);
            return;
        }

        var existingRoom = _roomRegistry.FindRoom(session);
        if (existingRoom is not null)
        {
            await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
            {
                Type = "room_joined",
                SessionId = session.SessionId,
                RoomCode = existingRoom.RoomCode,
                Detail = "already_joined",
                RoomState = existingRoom.State.ToString(),
                HostSessionId = existingRoom.HostSessionId,
                HostPlayerName = existingRoom.Members.FirstOrDefault(member => string.Equals(member.SessionId, existingRoom.HostSessionId, StringComparison.Ordinal))?.PlayerName ?? string.Empty,
                PlayerCount = existingRoom.Members.Count,
                ReadyPlayers = existingRoom.ReadyBySessionId.Values.Count(v => v),
                Members = existingRoom.Members.Select(member => member.PlayerName).ToArray(),
                ReadyMembers = existingRoom.Members
                    .Where(member => existingRoom.ReadyBySessionId.GetValueOrDefault(member.SessionId))
                    .Select(member => member.PlayerName)
                    .ToArray(),
                RoomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom)
            }, cancellationToken);
            await BroadcastRoomAsync(existingRoom, "room_joined", cancellationToken);
            return;
        }

        _roomRegistry.Remove(session);
        var room = _roomRegistry.CreateRoom(session);
        await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
        {
            Type = "room_joined",
            SessionId = session.SessionId,
            RoomCode = room.RoomCode,
            Detail = "created",
            RoomState = room.State.ToString(),
            HostSessionId = room.HostSessionId,
            HostPlayerName = session.PlayerName,
            PlayerCount = room.Members.Count,
            ReadyPlayers = room.ReadyBySessionId.Values.Count(v => v),
            Members = room.Members.Select(member => member.PlayerName).ToArray(),
            ReadyMembers = room.Members
                .Where(member => room.ReadyBySessionId.GetValueOrDefault(member.SessionId))
                .Select(member => member.PlayerName)
                .ToArray(),
            RoomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom),
            ServerSentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            LastProcessedClientTick = session.LastProcessedTick
        }, cancellationToken);
        await BroadcastRoomAsync(room, "room_joined", cancellationToken);
        await BroadcastRoomListingsAsync(cancellationToken);
        Console.WriteLine($"[server] room created {room.RoomCode} by {session.PlayerName}");
    }

    private async Task HandleJoinRoomAsync(ClientSession session, ClientMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.PlayerName))
        {
            await SendErrorAsync(session, "handshake_required", cancellationToken);
            return;
        }

        var normalizedRoomCode = message.RoomCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedRoomCode))
        {
            await SendErrorAsync(session, "invalid_room_code", cancellationToken);
            return;
        }

        var existingRoom = _roomRegistry.FindRoom(session);
        if (existingRoom is not null)
        {
            if (string.Equals(existingRoom.RoomCode, normalizedRoomCode, StringComparison.OrdinalIgnoreCase))
            {
                await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
                {
                    Type = "room_joined",
                    SessionId = session.SessionId,
                    RoomCode = existingRoom.RoomCode,
                    Detail = "already_joined",
                    RoomState = existingRoom.State.ToString(),
                    HostSessionId = existingRoom.HostSessionId,
                    HostPlayerName = existingRoom.Members.FirstOrDefault(member => string.Equals(member.SessionId, existingRoom.HostSessionId, StringComparison.Ordinal))?.PlayerName ?? string.Empty,
                    PlayerCount = existingRoom.Members.Count,
                    ReadyPlayers = existingRoom.ReadyBySessionId.Values.Count(v => v),
                    Members = existingRoom.Members.Select(member => member.PlayerName).ToArray(),
                    ReadyMembers = existingRoom.Members
                        .Where(member => existingRoom.ReadyBySessionId.GetValueOrDefault(member.SessionId))
                        .Select(member => member.PlayerName)
                        .ToArray(),
                    RoomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom)
                }, cancellationToken);
                await BroadcastRoomAsync(existingRoom, "room_joined", cancellationToken);
                return;
            }

            await SendErrorAsync(session, "already_in_room", cancellationToken);
            return;
        }

        _roomRegistry.Remove(session);
        if (!_roomRegistry.TryJoinRoom(normalizedRoomCode, session, _config.MaxPlayersPerRoom, out var room, out var error))
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
            Detail = "joined",
            RoomState = joinedRoom.State.ToString(),
            HostSessionId = joinedRoom.HostSessionId,
            HostPlayerName = joinedRoom.Members.FirstOrDefault(member => string.Equals(member.SessionId, joinedRoom.HostSessionId, StringComparison.Ordinal))?.PlayerName ?? string.Empty,
            PlayerCount = joinedRoom.Members.Count,
            ReadyPlayers = joinedRoom.ReadyBySessionId.Values.Count(v => v),
            Members = joinedRoom.Members.Select(member => member.PlayerName).ToArray(),
            ReadyMembers = joinedRoom.Members
                .Where(member => joinedRoom.ReadyBySessionId.GetValueOrDefault(member.SessionId))
                .Select(member => member.PlayerName)
                .ToArray(),
            RoomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom),
            ServerSentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            LastProcessedClientTick = session.LastProcessedTick
        }, cancellationToken);
        await BroadcastRoomAsync(joinedRoom, "room_joined", cancellationToken);
        await BroadcastRoomListingsAsync(cancellationToken);
        Console.WriteLine($"[server] room joined {joinedRoom.RoomCode} by {session.PlayerName}");
    }

    private async Task HandleReadyStateAsync(ClientSession session, ClientMessage message, CancellationToken cancellationToken)
    {
        if (!_roomRegistry.TrySetReady(session, message.IsReady, out var room))
        {
            await SendErrorAsync(session, "not_in_room", cancellationToken);
            return;
        }

        lock (_roomRegistry.SyncRoot)
        {
            if (room!.State == SpikeRoomState.Countdown && !message.IsReady)
            {
                room.State = SpikeRoomState.Lobby;
                room.StateEnteredUtc = DateTimeOffset.UtcNow;
                room.CountdownEndsUtc = DateTimeOffset.MinValue;
                room.EndReason = string.Empty;
            }
        }

        await BroadcastRoomAsync(room!, "ready_state_changed", cancellationToken);
        await BroadcastRoomListingsAsync(cancellationToken);
    }

    private async Task HandleStartMatchAsync(ClientSession session, CancellationToken cancellationToken)
    {
        var room = _roomRegistry.FindRoom(session);
        if (room is null)
        {
            await SendErrorAsync(session, "not_in_room", cancellationToken);
            return;
        }

        string? error = null;
        lock (_roomRegistry.SyncRoot)
        {
            if (room.State != SpikeRoomState.Lobby)
            {
                error = "room_not_startable";
            }
            else if (!string.Equals(room.HostSessionId, session.SessionId, StringComparison.Ordinal))
            {
                error = "host_only_start";
            }
            else if (room.Members.Count < _config.MaxPlayersPerRoom)
            {
                error = "waiting_for_players";
            }
            else if (room.ReadyBySessionId.Values.Count(v => v) < room.Members.Count)
            {
                error = "not_all_ready";
            }
            else
            {
                room.State = SpikeRoomState.Countdown;
                room.StateEnteredUtc = DateTimeOffset.UtcNow;
                room.CountdownEndsUtc = DateTimeOffset.UtcNow.AddSeconds(3);
                room.EndReason = string.Empty;
            }
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            await SendErrorAsync(session, error, cancellationToken);
            return;
        }

        await BroadcastRoomAsync(room, "room_state_changed", cancellationToken);
        await BroadcastRoomListingsAsync(cancellationToken);
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
        var movementApplied = false;
        var slowShotApplied = false;
        var trapApplied = false;
        var batteryCollected = false;
        lock (_roomRegistry.SyncRoot)
        {
            movementApplied = ProcessMovementFrame(room, session.SessionId, new SpikeVec2(message.MoveX, message.MoveY), out trapApplied, out batteryCollected);
            if (message.FirePressed)
            {
                slowShotApplied = TryApplySlowShot(room, session.SessionId, new SpikeVec2(message.AimX, message.AimY));
            }
        }

        await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
        {
            Type = "input_frame_ack",
            SessionId = session.SessionId,
            Tick = message.Tick,
            Detail = $"mx={message.MoveX:F2},my={message.MoveY:F2},fire={message.FirePressed}",
            ServerSentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ClientSentAtUnixMs = message.ClientSentAtUnixMs,
            LastProcessedClientTick = session.LastProcessedTick
        }, cancellationToken);
        Console.WriteLine($"[server] input ack session={session.SessionId} tick={message.Tick}");

        if (movementApplied || slowShotApplied || trapApplied || batteryCollected)
        {
            await BroadcastRoomAsync(room, DescribeMovementDetail(batteryCollected, slowShotApplied, trapApplied, movementApplied), cancellationToken);
        }
    }

    private async Task MonitorStaleSessionsAsync(CancellationToken cancellationToken)
    {
        try
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
        catch (OperationCanceledException)
        {
        }
    }

    private async Task MonitorRoomsAsync(CancellationToken cancellationToken)
    {
        try
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
                                    room.ActiveEndsUtc = DateTimeOffset.UtcNow.Add(_config.MatchDuration);
                                    room.EndReason = string.Empty;
                                    InitializeActiveMatch(room);
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
                                var batteriesRespawned = ProcessBatteryRespawns(room);
                                var effectsChanged = ProcessEffectExpirations(room);
                                if (DateTimeOffset.UtcNow >= room.ActiveEndsUtc)
                                {
                                    MarkRoomEnded(room, string.IsNullOrWhiteSpace(room.EndReason) ? "TimeExpired" : room.EndReason);
                                    shouldBroadcast = true;
                                    messageType = "room_state_changed";
                                }
                                else if (batteriesRespawned)
                                {
                                    shouldBroadcast = true;
                                    messageType = "battery_respawned";
                                }
                                else if (effectsChanged)
                                {
                                    shouldBroadcast = true;
                                    messageType = "effect_state_changed";
                                }
                                else
                                {
                                    shouldBroadcast = true;
                                    messageType = "active_tick";
                                    includeDetail = false;
                                }
                                break;
                            case SpikeRoomState.Ended:
                                if (room.PendingMatchResult is null)
                                {
                                    room.PendingMatchResult = BuildMatchResultPayload(room);
                                }

                                if (room.PersistenceTask is null && room.PendingMatchResult is not null)
                                {
                                    room.State = SpikeRoomState.Saving;
                                    room.StateEnteredUtc = DateTimeOffset.UtcNow;
                                    room.PersistenceStatus = "Saving";
                                    room.PersistenceDetail = "Writing match results to MySQL";
                                    room.PersistenceTask = _persistenceService.PersistMatchResultAsync(room.PendingMatchResult, cancellationToken);
                                    shouldBroadcast = true;
                                    messageType = "room_state_changed";
                                }
                                break;
                            case SpikeRoomState.Saving:
                                if (room.PersistenceTask is not null && room.PersistenceTask.IsCompleted)
                                {
                                    room.State = SpikeRoomState.ResultsReady;
                                    room.StateEnteredUtc = DateTimeOffset.UtcNow;
                                    if (room.PersistenceTask.IsFaulted)
                                    {
                                        room.PersistenceStatus = "Failed";
                                        room.PersistenceDetail = room.PersistenceTask.Exception?.GetBaseException().Message ?? "Unknown persistence error";
                                        room.LeaderboardRows = [];
                                    }
                                    else
                                    {
                                        var persistenceResult = room.PersistenceTask.GetAwaiter().GetResult();
                                        room.PersistenceStatus = persistenceResult.Status;
                                        room.PersistenceDetail = persistenceResult.Detail;
                                        room.LeaderboardRows = persistenceResult.LeaderboardRows
                                            .Select(row => $"{row.PlayerName} · W{row.Wins} D{row.Draws} L{row.Losses} · Best {row.BestScore}")
                                            .ToArray();
                                    }
                                    shouldBroadcast = true;
                                    messageType = "room_state_changed";
                                }
                                break;
                        }
                    }

                    if (shouldBroadcast)
                    {
                        await BroadcastRoomAsync(room, messageType, cancellationToken, includeDetail);
                        if (messageType == "room_state_changed")
                        {
                            await BroadcastRoomListingsAsync(cancellationToken);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task BroadcastRoomListingsAsync(CancellationToken cancellationToken)
    {
        var roomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom);
        var nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ClientSession[] targets;
        lock (_sync)
        {
            targets = _sessions
                .Where(session => !string.IsNullOrWhiteSpace(session.PlayerName))
                .ToArray();
        }

        foreach (var session in targets)
        {
            try
            {
                await LengthPrefixedProtocol.WriteAsync(session.Stream, new ServerMessage
                {
                    Type = "room_listings_updated",
                    SessionId = session.SessionId,
                    RoomCode = session.RoomCode,
                    RoomListings = roomListings,
                    ServerSentAtUnixMs = nowUnixMs,
                    LastProcessedClientTick = session.LastProcessedTick,
                    HeartbeatAgeSeconds = Math.Max(0f, (float)(DateTimeOffset.UtcNow - session.LastSeenUtc).TotalSeconds)
                }, cancellationToken);
            }
            catch
            {
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
        string hostSessionId;
        string hostPlayerName;
        int playerCount;
        int readyPlayers;
        float countdownRemaining;
        string endReason;
        string persistenceStatus;
        string[] members;
        string[] readyMembers;
        string[] roomListings;
        int[] activeBatteryIds;
        string[] batteryPositions;
        string[] trapPositions;
        string[] scoreboard;
        string[] effectStates;
        string[] playerPositions;
        float matchTimeRemaining;
        int snapshotSequence;
        ClientSession[] targets;

        lock (_roomRegistry.SyncRoot)
        {
            room.SnapshotSequence += 1;
            snapshotSequence = room.SnapshotSequence;
            roomCode = room.RoomCode;
            roomState = room.State.ToString();
            hostSessionId = room.HostSessionId;
            hostPlayerName = room.Members.FirstOrDefault(member => string.Equals(member.SessionId, room.HostSessionId, StringComparison.Ordinal))?.PlayerName ?? string.Empty;
            playerCount = room.Members.Count;
            readyPlayers = room.ReadyBySessionId.Values.Count(v => v);
            countdownRemaining = room.State == SpikeRoomState.Countdown
                ? Math.Max(0f, (float)(room.CountdownEndsUtc - DateTimeOffset.UtcNow).TotalSeconds)
                : 0f;
            matchTimeRemaining = room.State == SpikeRoomState.Active
                ? Math.Max(0f, (float)(room.ActiveEndsUtc - DateTimeOffset.UtcNow).TotalSeconds)
                : 0f;
            endReason = room.EndReason;
            persistenceStatus = room.PersistenceStatus;
            members = room.Members.Select(member => member.PlayerName).ToArray();
            readyMembers = room.Members
                .Where(member => room.ReadyBySessionId.GetValueOrDefault(member.SessionId))
                .Select(member => member.PlayerName)
                .ToArray();
            roomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom);
            activeBatteryIds = room.ActiveBatteryIds.ToArray();
            batteryPositions = room.ActiveBatteryIds
                .Select(id =>
                {
                    var position = room.BatteryPositionsById.GetValueOrDefault(id);
                    return FormattableString.Invariant($"{id}:{position.X:0.00}:{position.Y:0.00}");
                })
                .ToArray();
            trapPositions = room.TrapPositionsById
                .OrderBy(pair => pair.Key)
                .Select(pair => FormattableString.Invariant($"{pair.Key}:{pair.Value.X:0.00}:{pair.Value.Y:0.00}"))
                .ToArray();
            scoreboard = room.Members
                .Select(member => $"{member.PlayerName}:{room.ScoreBySessionId.GetValueOrDefault(member.SessionId, 0)}")
                .ToArray();
            effectStates = room.Members
                .Select(member =>
                {
                    var effect = room.EffectsBySessionId.GetValueOrDefault(member.SessionId) ?? new PlayerEffectState();
                    return $"{member.PlayerName}:{effect.MoveMultiplier:0.00}:{effect.Source}:{Math.Max(0f, (float)(effect.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds):0.00}:{Math.Max(0f, (float)(effect.ImmuneUntilUtc - DateTimeOffset.UtcNow).TotalSeconds):0.00}";
                })
                .ToArray();
            playerPositions = room.Members
                .Select(member =>
                {
                    var position = room.PlayerPositionsBySessionId.GetValueOrDefault(member.SessionId);
                    return FormattableString.Invariant($"{member.PlayerName}:{position.X:0.00}:{position.Y:0.00}");
                })
                .ToArray();
            targets = room.Members.ToArray();
        }

        foreach (var member in targets)
        {
            try
            {
                var cooldownRemaining = 0f;
                var readyAt = DateTimeOffset.MinValue;
                var nowUtc = DateTimeOffset.UtcNow;
                lock (_roomRegistry.SyncRoot)
                {
                    readyAt = room.SlowShotReadyAtBySessionId.GetValueOrDefault(member.SessionId);
                    if (readyAt > nowUtc)
                    {
                        cooldownRemaining = Math.Max(0f, (float)(readyAt - nowUtc).TotalSeconds);
                    }
                }

                var message = new ServerMessage
                {
                    Type = "room_snapshot",
                    RoomCode = roomCode,
                    SessionId = member.SessionId,
                    Tick = snapshotSequence,
                    Detail = includeDetail ? messageType : string.Empty,
                    RoomState = roomState,
                    HostSessionId = hostSessionId,
                    HostPlayerName = hostPlayerName,
                    PlayerCount = playerCount,
                    ReadyPlayers = readyPlayers,
                    CountdownRemainingSeconds = countdownRemaining,
                    EndReason = endReason,
                    PersistenceStatus = persistenceStatus,
                    PersistenceDetail = room.PersistenceDetail,
                    Members = members,
                    ReadyMembers = readyMembers,
                    RoomListings = roomListings,
                    ActiveBatteryIds = activeBatteryIds,
                    BatteryPositions = batteryPositions,
                    TrapPositions = trapPositions,
                    Scoreboard = scoreboard,
                    LeaderboardRows = room.LeaderboardRows,
                    EffectStates = effectStates,
                    PlayerPositions = playerPositions,
                    MatchTimeRemainingSeconds = matchTimeRemaining,
                    ServerSentAtUnixMs = nowUtc.ToUnixTimeMilliseconds(),
                    SnapshotSequence = snapshotSequence,
                    LastProcessedClientTick = member.LastProcessedTick,
                    HeartbeatAgeSeconds = Math.Max(0f, (float)(nowUtc - member.LastSeenUtc).TotalSeconds),
                    SlowShotReady = cooldownRemaining <= 0.01f,
                    SlowShotCooldownRemainingSeconds = cooldownRemaining
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

    private void InitializeActiveMatch(SpikeRoom room)
    {
        room.ActiveBatteryIds.Clear();
        room.BatteryPositionsById.Clear();
        room.TrapPositionsById.Clear();
        room.PendingRespawns.Clear();
        room.RecentSpawnHistory.Clear();
        room.TrapRetriggerReadyAtBySessionTrapKey.Clear();
        room.PendingMatchResult = null;
        room.PersistenceTask = null;
        room.PersistenceStatus = string.Empty;
        room.PersistenceDetail = string.Empty;
        room.LeaderboardRows = [];
        room.ForfeitingPlayerName = string.Empty;
        for (var index = 0; index < room.Members.Count; index++)
        {
            var member = room.Members[index];
            room.ScoreBySessionId[member.SessionId] = 0;
            room.EffectsBySessionId[member.SessionId] = new PlayerEffectState();
            room.SlowShotReadyAtBySessionId[member.SessionId] = DateTimeOffset.MinValue;
            room.PlayerPositionsBySessionId[member.SessionId] = _config.PlayerSpawnPoints[index % _config.PlayerSpawnPoints.Length];
        }

        for (var trapId = 1; trapId <= _config.ActiveTrapCount; trapId++)
        {
            room.TrapPositionsById[trapId] = GenerateTrapSpawnPosition(room);
        }

        while (room.ActiveBatteryIds.Count < _config.ActiveBatteryCount)
        {
            var nextBattery = SelectNextBatteryId(room);
            if (nextBattery < 0)
            {
                break;
            }

            ActivateBattery(room, nextBattery);
        }
    }

    private bool ProcessEffectExpirations(SpikeRoom room)
    {
        var changed = false;
        foreach (var effect in room.EffectsBySessionId.Values)
        {
            if (effect.MoveMultiplier < 1f && DateTimeOffset.UtcNow >= effect.ExpiresAtUtc)
            {
                effect.MoveMultiplier = 1f;
                effect.Source = string.Empty;
                effect.ImmuneUntilUtc = DateTimeOffset.UtcNow.Add(_config.PostSlowImmunity);
                changed = true;
            }
        }

        return changed;
    }

    private bool ProcessBatteryRespawns(SpikeRoom room)
    {
        if (room.PendingRespawns.Count == 0)
        {
            return false;
        }

        var due = room.PendingRespawns
            .Where(pair => pair.Value <= DateTimeOffset.UtcNow)
            .Select(pair => pair.Key)
            .ToList();
        var changed = false;
        foreach (var batteryId in due)
        {
            room.PendingRespawns.Remove(batteryId);
            if (!room.ActiveBatteryIds.Contains(batteryId))
            {
                ActivateBattery(room, batteryId);
                changed = true;
            }
        }

        return changed;
    }

    private int SelectNextBatteryId(SpikeRoom room)
    {
        var available = Enumerable.Range(1, _config.SpawnPointCount)
            .Where(id => !room.ActiveBatteryIds.Contains(id) && !room.PendingRespawns.ContainsKey(id))
            .ToList();
        if (available.Count == 0)
        {
            return -1;
        }

        var filtered = available.Where(id => !room.RecentSpawnHistory.Contains(id)).ToList();
        if (filtered.Count == 0)
        {
            filtered = available;
        }

        return filtered[Random.Shared.Next(filtered.Count)];
    }

    private void ActivateBattery(SpikeRoom room, int batteryId)
    {
        room.ActiveBatteryIds.Add(batteryId);
        room.BatteryPositionsById[batteryId] = GenerateBatterySpawnPosition(room);
        room.RecentSpawnHistory.Enqueue(batteryId);
        while (room.RecentSpawnHistory.Count > 2)
        {
            room.RecentSpawnHistory.Dequeue();
        }
    }

    private bool ApplySlow(SpikeRoom room, string sessionId, float multiplier, TimeSpan duration, string source)
    {
        var effect = room.EffectsBySessionId.GetValueOrDefault(sessionId);
        if (effect is null)
        {
            effect = new PlayerEffectState();
            room.EffectsBySessionId[sessionId] = effect;
        }

        if (effect.ImmuneUntilUtc > DateTimeOffset.UtcNow)
        {
            return false;
        }

        if (effect.MoveMultiplier < 1f && multiplier >= effect.MoveMultiplier)
        {
            return false;
        }

        effect.MoveMultiplier = multiplier;
        effect.Source = source;
        effect.ExpiresAtUtc = DateTimeOffset.UtcNow.Add(duration);
        return true;
    }

    private bool ProcessMovementFrame(SpikeRoom room, string sessionId, SpikeVec2 requestedMove, out bool trapApplied, out bool batteryCollected)
    {
        trapApplied = false;
        batteryCollected = false;
        if (requestedMove.LengthSquared() <= 0f)
        {
            return false;
        }

        var move = requestedMove.Normalized();
        var current = room.PlayerPositionsBySessionId.GetValueOrDefault(sessionId);
        var multiplier = (room.EffectsBySessionId.GetValueOrDefault(sessionId) ?? new PlayerEffectState()).MoveMultiplier;
        var proposed = ClampToArena(current.Add(move.Scale(_config.PlayerStepDistance * multiplier)));

        var trapId = LookupTrapAtPosition(room, proposed);
        if (trapId is not null && TryApplyTrap(room, sessionId, trapId.Value))
        {
            trapApplied = true;
            multiplier = (room.EffectsBySessionId.GetValueOrDefault(sessionId) ?? new PlayerEffectState()).MoveMultiplier;
            proposed = ClampToArena(current.Add(move.Scale(_config.PlayerStepDistance * multiplier)));
        }

        room.PlayerPositionsBySessionId[sessionId] = proposed;
        batteryCollected = TryProcessBatteryPickup(room, sessionId, current, proposed);
        return true;
    }

    private bool TryApplySlowShot(SpikeRoom room, string sourceSessionId, SpikeVec2 aimVector)
    {
        if (room.State != SpikeRoomState.Active)
        {
            return false;
        }

        if (room.SlowShotReadyAtBySessionId.GetValueOrDefault(sourceSessionId) > DateTimeOffset.UtcNow)
        {
            return false;
        }

        if (!room.PlayerPositionsBySessionId.TryGetValue(sourceSessionId, out var sourcePosition))
        {
            return false;
        }

        var normalizedAim = aimVector.LengthSquared() > 0f ? aimVector.Normalized() : new SpikeVec2(0f, 0f);
        var target = room.Members
            .Where(member => member.SessionId != sourceSessionId)
            .Select(member => new
            {
                Member = member,
                Position = room.PlayerPositionsBySessionId.GetValueOrDefault(member.SessionId)
            })
            .Select(candidate => new
            {
                candidate.Member,
                Delta = new SpikeVec2(candidate.Position.X - sourcePosition.X, candidate.Position.Y - sourcePosition.Y)
            })
            .Select(candidate => new
            {
                candidate.Member,
                candidate.Delta,
                DistanceSquared = candidate.Delta.LengthSquared(),
                AimAlignment = normalizedAim.LengthSquared() > 0f ? candidate.Delta.Normalized().Dot(normalizedAim) : 1f
            })
            .Where(candidate => candidate.DistanceSquared <= _config.SlowShotRange * _config.SlowShotRange)
            .Where(candidate => candidate.AimAlignment >= 0.25f)
            .OrderBy(candidate => candidate.DistanceSquared)
            .FirstOrDefault();

        if (target is null)
        {
            return false;
        }

        var applied = ApplySlow(room, target.Member.SessionId, _config.SlowShotMoveMultiplier, _config.SlowShotDuration, "SlowShot");
        if (applied)
        {
            room.SlowShotReadyAtBySessionId[sourceSessionId] = DateTimeOffset.UtcNow.Add(_config.SlowShotCooldown);
        }

        return applied;
    }

    private bool TryApplyTrap(SpikeRoom room, string sessionId, int trapId)
    {
        if (room.State != SpikeRoomState.Active)
        {
            return false;
        }

        var effect = room.EffectsBySessionId.GetValueOrDefault(sessionId);
        if (effect is not null && effect.ImmuneUntilUtc > DateTimeOffset.UtcNow)
        {
            return false;
        }

        var key = $"{sessionId}:{trapId}";
        if (room.TrapRetriggerReadyAtBySessionTrapKey.GetValueOrDefault(key) > DateTimeOffset.UtcNow)
        {
            return false;
        }

        var applied = ApplySlow(room, sessionId, _config.TrapMoveMultiplier, _config.TrapDuration, "Trap");
        if (!applied)
        {
            return false;
        }

        room.TrapRetriggerReadyAtBySessionTrapKey[key] = DateTimeOffset.UtcNow.Add(_config.TrapRetriggerCooldown);
        return true;
    }

    private bool TryProcessBatteryPickup(SpikeRoom room, string sessionId, SpikeVec2 previousPosition, SpikeVec2 currentPosition)
    {
        if (room.State != SpikeRoomState.Active)
        {
            return false;
        }

        var pickupRadiusSquared = _config.BatteryPickupRadius * _config.BatteryPickupRadius;
        int? selectedBatteryId = null;
        var selectedDistanceSquared = float.MaxValue;

        foreach (var batteryId in room.ActiveBatteryIds)
        {
            if (!room.BatteryPositionsById.TryGetValue(batteryId, out var batteryCenter))
            {
                continue;
            }

            var distanceSquared = DistanceToSegmentSquared(previousPosition, currentPosition, batteryCenter);
            if (distanceSquared > pickupRadiusSquared &&
                !OverlapsVisualPickupZone(previousPosition, currentPosition, batteryCenter) &&
                !OverlapsVisualPickupPoint(previousPosition, batteryCenter) &&
                !OverlapsVisualPickupPoint(currentPosition, batteryCenter))
            {
                continue;
            }

            if (distanceSquared < selectedDistanceSquared ||
                (Math.Abs(distanceSquared - selectedDistanceSquared) < 0.0001f && (selectedBatteryId is null || batteryId < selectedBatteryId.Value)))
            {
                selectedDistanceSquared = distanceSquared;
                selectedBatteryId = batteryId;
            }
        }

        if (selectedBatteryId is null)
        {
            return false;
        }

        room.ActiveBatteryIds.Remove(selectedBatteryId.Value);
        room.BatteryPositionsById.Remove(selectedBatteryId.Value);
        room.PendingRespawns[selectedBatteryId.Value] = DateTimeOffset.UtcNow.Add(_config.BatteryRespawnDelay);
        room.ScoreBySessionId[sessionId] = room.ScoreBySessionId.GetValueOrDefault(sessionId, 0) + 1;

        if (room.ScoreBySessionId[sessionId] >= _config.TargetScore)
        {
            MarkRoomEnded(room, "TargetScoreReached");
        }

        return true;
    }

    private SpikeVec2 GenerateBatterySpawnPosition(SpikeRoom room)
    {
        return GenerateSpawnPosition(
            room,
            room.BatteryPositionsById.Values.Concat(room.TrapPositionsById.Values),
            _config.BatterySpawnInset,
            _config.BatterySpawnMinimumSeparation,
            _config.BatterySpawnGenerationAttempts);
    }

    private SpikeVec2 GenerateTrapSpawnPosition(SpikeRoom room)
    {
        return GenerateSpawnPosition(
            room,
            room.TrapPositionsById.Values.Concat(room.BatteryPositionsById.Values),
            _config.TrapSpawnInset,
            _config.TrapSpawnMinimumSeparation,
            _config.TrapSpawnGenerationAttempts);
    }

    private SpikeVec2 GenerateSpawnPosition(
        SpikeRoom room,
        IEnumerable<SpikeVec2> occupiedPositions,
        float inset,
        float minimumSeparation,
        int generationAttempts)
    {
        var min = -_config.ArenaHalfExtent + inset;
        var max = _config.ArenaHalfExtent - inset;
        var minSeparationSquared = minimumSeparation * minimumSeparation;
        var attempts = Math.Max(1, generationAttempts);
        var fallback = new SpikeVec2(0f, 0f);
        var occupied = occupiedPositions.ToArray();

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var candidate = new SpikeVec2(
                Random.Shared.NextSingle() * (max - min) + min,
                Random.Shared.NextSingle() * (max - min) + min);
            fallback = candidate;

            if (occupied.Any(position => position.DistanceSquared(candidate) < minSeparationSquared))
            {
                continue;
            }

            if (room.PlayerPositionsBySessionId.Values.Any(position => position.DistanceSquared(candidate) < minSeparationSquared))
            {
                continue;
            }

            return candidate;
        }

        return fallback;
    }

    private int? LookupTrapAtPosition(SpikeRoom room, SpikeVec2 position)
    {
        var triggerRadiusSquared = _config.TrapTriggerRadius * _config.TrapTriggerRadius;
        foreach (var pair in room.TrapPositionsById)
        {
            if (position.DistanceSquared(pair.Value) <= triggerRadiusSquared)
            {
                return pair.Key;
            }
        }

        return null;
    }

    private SpikeVec2 ClampToArena(SpikeVec2 position)
    {
        var playableHalfExtent = Math.Max(0f, _config.ArenaHalfExtent - _config.PlayerBoundsInset);
        var min = -playableHalfExtent;
        var max = playableHalfExtent;
        return new SpikeVec2(
            Math.Clamp(position.X, min, max),
            Math.Clamp(position.Y, min, max));
    }

    private static float DistanceToSegmentSquared(SpikeVec2 segmentStart, SpikeVec2 segmentEnd, SpikeVec2 point)
    {
        var segment = new SpikeVec2(segmentEnd.X - segmentStart.X, segmentEnd.Y - segmentStart.Y);
        var lengthSquared = segment.LengthSquared();
        if (lengthSquared <= 0.0001f)
        {
            return segmentStart.DistanceSquared(point);
        }

        var toPoint = new SpikeVec2(point.X - segmentStart.X, point.Y - segmentStart.Y);
        var t = Math.Clamp(toPoint.Dot(segment) / lengthSquared, 0f, 1f);
        var closest = new SpikeVec2(
            segmentStart.X + (segment.X * t),
            segmentStart.Y + (segment.Y * t));
        return closest.DistanceSquared(point);
    }

    private static bool OverlapsVisualPickupZone(SpikeVec2 segmentStart, SpikeVec2 segmentEnd, SpikeVec2 point)
    {
        var segment = new SpikeVec2(segmentEnd.X - segmentStart.X, segmentEnd.Y - segmentStart.Y);
        var lengthSquared = segment.LengthSquared();
        SpikeVec2 closest;
        if (lengthSquared <= 0.0001f)
        {
            closest = segmentStart;
        }
        else
        {
            var toPoint = new SpikeVec2(point.X - segmentStart.X, point.Y - segmentStart.Y);
            var t = Math.Clamp(toPoint.Dot(segment) / lengthSquared, 0f, 1f);
            closest = new SpikeVec2(
                segmentStart.X + (segment.X * t),
                segmentStart.Y + (segment.Y * t));
        }

        return Math.Abs(closest.X - point.X) <= VisualPickupHalfExtent &&
               Math.Abs(closest.Y - point.Y) <= VisualPickupHalfExtent;
    }

    private static bool OverlapsVisualPickupPoint(SpikeVec2 position, SpikeVec2 point)
    {
        return Math.Abs(position.X - point.X) <= VisualPickupHalfExtent &&
               Math.Abs(position.Y - point.Y) <= VisualPickupHalfExtent;
    }

    private static string DescribeMovementDetail(bool batteryCollected, bool slowShotApplied, bool trapApplied, bool movementApplied)
    {
        if (batteryCollected)
        {
            return "battery_collected";
        }

        if (slowShotApplied)
        {
            return "slow_shot_applied";
        }

        if (trapApplied)
        {
            return "trap_applied";
        }

        return movementApplied ? "movement_applied" : "input_observed";
    }

    private void MarkRoomEnded(SpikeRoom room, string endReason, string? forfeitingPlayerName = null)
    {
        if (room.State == SpikeRoomState.Ended || room.State == SpikeRoomState.Saving || room.State == SpikeRoomState.ResultsReady)
        {
            return;
        }

        room.State = SpikeRoomState.Ended;
        room.StateEnteredUtc = DateTimeOffset.UtcNow;
        room.EndReason = endReason;
        room.ForfeitingPlayerName = forfeitingPlayerName ?? string.Empty;
        room.PendingMatchResult = BuildMatchResultPayload(room);
    }

    private MatchResultPayload BuildMatchResultPayload(SpikeRoom room)
    {
        var players = room.Members
            .Select(member => (
                PlayerName: member.PlayerName,
                Score: room.ScoreBySessionId.GetValueOrDefault(member.SessionId, 0)))
            .OrderByDescending(player => player.Score)
            .ThenBy(player => player.PlayerName, StringComparer.Ordinal)
            .ToArray();

        var winnerName = ResolveWinnerName(room.EndReason, room.ForfeitingPlayerName, players);
        var playerResults = players
            .Select(player => new MatchPlayerResult(
                player.PlayerName,
                player.Score,
                ResolveOutcome(room.EndReason, room.ForfeitingPlayerName, winnerName, player.PlayerName)))
            .ToArray();

        return new MatchResultPayload(
            Guid.NewGuid().ToString("N"),
            room.RoomCode,
            room.EndReason,
            winnerName,
            DateTimeOffset.UtcNow,
            playerResults);
    }

    private static string? ResolveWinnerName(string endReason, string forfeitingPlayerName, IReadOnlyList<(string PlayerName, int Score)> players)
    {
        if (players.Count == 0)
        {
            return null;
        }

        if (string.Equals(endReason, "Draw", StringComparison.Ordinal) ||
            string.Equals(endReason, "ServerAbort", StringComparison.Ordinal))
        {
            return null;
        }

        if (string.Equals(endReason, "DisconnectForfeit", StringComparison.Ordinal))
        {
            var survivor = players.FirstOrDefault(player => !string.Equals(player.PlayerName, forfeitingPlayerName, StringComparison.Ordinal));
            return string.IsNullOrWhiteSpace(survivor.PlayerName) ? null : survivor.PlayerName;
        }

        if (players.Count == 1)
        {
            return players[0].PlayerName;
        }

        return players[0].Score == players[1].Score ? null : players[0].PlayerName;
    }

    private static string ResolveOutcome(string endReason, string forfeitingPlayerName, string? winnerName, string playerName)
    {
        if (string.Equals(endReason, "Draw", StringComparison.Ordinal))
        {
            return "Draw";
        }

        if (string.Equals(endReason, "ServerAbort", StringComparison.Ordinal))
        {
            return "ServerAbort";
        }

        if (string.Equals(endReason, "DisconnectForfeit", StringComparison.Ordinal))
        {
            return string.Equals(playerName, forfeitingPlayerName, StringComparison.Ordinal) ? "Loss" : "Win";
        }

        if (winnerName is null)
        {
            return "Draw";
        }

        if (string.Equals(winnerName, playerName, StringComparison.Ordinal))
        {
            return "Win";
        }

        return "Loss";
    }
}
