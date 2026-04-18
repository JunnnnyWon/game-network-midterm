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
                            Detail = "alive",
                            RoomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom)
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
            Detail = session.PlayerName,
            RoomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom)
        }, cancellationToken);
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
            RoomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom)
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
            RoomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom)
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
            Detail = $"mx={message.MoveX:F2},my={message.MoveY:F2},fire={message.FirePressed}"
        }, cancellationToken);
        Console.WriteLine($"[server] input ack session={session.SessionId} tick={message.Tick}");

        if (movementApplied || slowShotApplied || trapApplied || batteryCollected)
        {
            await BroadcastRoomAsync(room, DescribeMovementDetail(batteryCollected, slowShotApplied, trapApplied, movementApplied), cancellationToken);
        }
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
                            room.State = SpikeRoomState.Ended;
                            room.StateEnteredUtc = DateTimeOffset.UtcNow;
                            room.EndReason = string.IsNullOrWhiteSpace(room.EndReason) ? "TimeExpired" : room.EndReason;
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
        string[] readyMembers;
        string[] roomListings;
        int[] activeBatteryIds;
        string[] scoreboard;
        string[] effectStates;
        string[] playerPositions;
        float matchTimeRemaining;
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
            matchTimeRemaining = room.State == SpikeRoomState.Active
                ? Math.Max(0f, (float)(room.ActiveEndsUtc - DateTimeOffset.UtcNow).TotalSeconds)
                : 0f;
            endReason = room.EndReason;
            persistenceStatus = room.State switch
            {
                SpikeRoomState.Saving => "Saving",
                SpikeRoomState.ResultsReady => "Saved",
                _ => string.Empty
            };
            members = room.Members.Select(member => member.PlayerName).ToArray();
            readyMembers = room.Members
                .Where(member => room.ReadyBySessionId.GetValueOrDefault(member.SessionId))
                .Select(member => member.PlayerName)
                .ToArray();
            roomListings = _roomRegistry.SnapshotRoomListings(_config.MaxPlayersPerRoom);
            activeBatteryIds = room.ActiveBatteryIds.ToArray();
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
                lock (_roomRegistry.SyncRoot)
                {
                    readyAt = room.SlowShotReadyAtBySessionId.GetValueOrDefault(member.SessionId);
                    if (readyAt > DateTimeOffset.UtcNow)
                    {
                        cooldownRemaining = Math.Max(0f, (float)(readyAt - DateTimeOffset.UtcNow).TotalSeconds);
                    }
                }

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
                    Members = members,
                    ReadyMembers = readyMembers,
                    RoomListings = roomListings,
                    ActiveBatteryIds = activeBatteryIds,
                    Scoreboard = scoreboard,
                    EffectStates = effectStates,
                    PlayerPositions = playerPositions,
                    MatchTimeRemainingSeconds = matchTimeRemaining,
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
        room.PendingRespawns.Clear();
        room.RecentSpawnHistory.Clear();
        for (var index = 0; index < room.Members.Count; index++)
        {
            var member = room.Members[index];
            room.ScoreBySessionId[member.SessionId] = 0;
            room.EffectsBySessionId[member.SessionId] = new PlayerEffectState();
            room.SlowShotReadyAtBySessionId[member.SessionId] = DateTimeOffset.MinValue;
            room.PlayerPositionsBySessionId[member.SessionId] = _config.PlayerSpawnPoints[index % _config.PlayerSpawnPoints.Length];
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

        return filtered.Min();
    }

    private void ActivateBattery(SpikeRoom room, int batteryId)
    {
        room.ActiveBatteryIds.Add(batteryId);
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
        var proposed = current.Add(move.Scale(_config.PlayerStepDistance * multiplier));

        var trapId = LookupTrapAtPosition(proposed);
        if (trapId is not null && TryApplyTrap(room, sessionId, trapId.Value))
        {
            trapApplied = true;
            multiplier = (room.EffectsBySessionId.GetValueOrDefault(sessionId) ?? new PlayerEffectState()).MoveMultiplier;
            proposed = current.Add(move.Scale(_config.PlayerStepDistance * multiplier));
        }

        room.PlayerPositionsBySessionId[sessionId] = proposed;
        batteryCollected = TryProcessBatteryPickup(room);
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

    private bool TryProcessBatteryPickup(SpikeRoom room)
    {
        if (room.State != SpikeRoomState.Active)
        {
            return false;
        }

        var pickupRadiusSquared = _config.BatteryPickupRadius * _config.BatteryPickupRadius;
        var changed = false;
        foreach (var batteryId in room.ActiveBatteryIds.ToArray())
        {
            var batteryCenter = LookupBatteryPosition(batteryId);
            var eligible = room.Members
                .Select((member, joinIndex) => new
                {
                    Member = member,
                    JoinIndex = joinIndex,
                    Position = room.PlayerPositionsBySessionId.GetValueOrDefault(member.SessionId)
                })
                .Select(candidate => new
                {
                    candidate.Member,
                    candidate.JoinIndex,
                    DistanceSquared = candidate.Position.DistanceSquared(batteryCenter)
                })
                .Where(candidate => candidate.DistanceSquared <= pickupRadiusSquared)
                .OrderBy(candidate => candidate.DistanceSquared)
                .ThenBy(candidate => candidate.JoinIndex)
                .FirstOrDefault();

            if (eligible is null)
            {
                continue;
            }

            room.ActiveBatteryIds.Remove(batteryId);
            room.PendingRespawns[batteryId] = DateTimeOffset.UtcNow.Add(_config.BatteryRespawnDelay);
            room.ScoreBySessionId[eligible.Member.SessionId] = room.ScoreBySessionId.GetValueOrDefault(eligible.Member.SessionId, 0) + 1;
            changed = true;

            if (room.ScoreBySessionId[eligible.Member.SessionId] >= _config.TargetScore)
            {
                room.State = SpikeRoomState.Ended;
                room.StateEnteredUtc = DateTimeOffset.UtcNow;
                room.EndReason = "TargetScoreReached";
            }
        }

        return changed;
    }

    private SpikeVec2 LookupBatteryPosition(int batteryId)
    {
        var index = Math.Clamp(batteryId - 1, 0, _config.BatterySpawnPoints.Length - 1);
        return _config.BatterySpawnPoints[index];
    }

    private int? LookupTrapAtPosition(SpikeVec2 position)
    {
        var triggerRadiusSquared = _config.TrapTriggerRadius * _config.TrapTriggerRadius;
        for (var index = 0; index < _config.TrapCenters.Length; index++)
        {
            if (position.DistanceSquared(_config.TrapCenters[index]) <= triggerRadiusSquared)
            {
                return index + 1;
            }
        }

        return null;
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
}
