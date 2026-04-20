namespace BatteryRushArena.NetworkSpikeServer;

/// <summary>
/// Minimal in-memory room registry used by the network-session spike.
/// </summary>
public sealed class RoomRegistry
{
    private readonly Dictionary<string, SpikeRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private int _nextRoomNumber = 1;

    public object SyncRoot { get; } = new();

    public IReadOnlyList<SpikeRoom> SnapshotRooms()
    {
        lock (SyncRoot)
        {
            return _rooms.Values.ToList();
        }
    }

    public string[] SnapshotRoomListings(int maxPlayersPerRoom)
    {
        lock (SyncRoot)
        {
            return _rooms.Values
                .OrderBy(room => room.RoomCode, StringComparer.Ordinal)
                .Select(room =>
                {
                    EnsureHostAssignedUnsafe(room);
                    var ready = room.ReadyBySessionId.Values.Count(value => value);
                    return $"{room.RoomCode} · {room.Members.Count}/{maxPlayersPerRoom} · {room.State} · Ready {ready}";
                })
                .ToArray();
        }
    }

    /// <summary>
    /// Creates a new room and adds the provided client as the first member.
    /// </summary>
    public SpikeRoom CreateRoom(ClientSession session)
    {
        lock (SyncRoot)
        {
            var code = GenerateNextRoomCode();

            var room = new SpikeRoom(code);
            AddMember(room, session);
            room.HostSessionId = session.SessionId;
            _rooms[code] = room;
            return room;
        }
    }

    /// <summary>
    /// Attempts to join an existing room.
    /// </summary>
    public bool TryJoinRoom(string roomCode, ClientSession session, int maxPlayers, out SpikeRoom? room, out string error)
    {
        lock (SyncRoot)
        {
            if (!_rooms.TryGetValue(roomCode, out room))
            {
                error = "invalid_room_code";
                return false;
            }

            if (room.Members.Count >= maxPlayers)
            {
                error = "room_full";
                return false;
            }

            AddMember(room, session);
            EnsureHostAssignedUnsafe(room);
            error = string.Empty;
            return true;
        }
    }

    public bool TrySetReady(ClientSession session, bool isReady, out SpikeRoom? room)
    {
        lock (SyncRoot)
        {
            var foundRoom = FindRoomUnsafe(session);
            if (foundRoom is null)
            {
                room = null;
                return false;
            }

            EnsureHostAssignedUnsafe(foundRoom);
            foundRoom.ReadyBySessionId[session.SessionId] = isReady;
            room = foundRoom;
            return true;
        }
    }

    public SpikeRoom? FindRoom(ClientSession session)
    {
        lock (SyncRoot)
        {
            return FindRoomUnsafe(session);
        }
    }

    /// <summary>
    /// Removes the client from any room membership and returns the affected room if it still exists.
    /// </summary>
    public SpikeRoom? Remove(ClientSession session)
    {
        lock (SyncRoot)
        {
            SpikeRoom? affectedRoom = null;
            foreach (var room in _rooms.Values)
            {
                if (room.Members.Remove(session))
                {
                    room.ReadyBySessionId.Remove(session.SessionId);
                    room.ScoreBySessionId.Remove(session.SessionId);
                    room.PlayerPositionsBySessionId.Remove(session.SessionId);
                    room.EffectsBySessionId.Remove(session.SessionId);
                    room.SlowShotReadyAtBySessionId.Remove(session.SessionId);
                    foreach (var trapKey in room.TrapRetriggerReadyAtBySessionTrapKey.Keys
                                 .Where(key => key.StartsWith(session.SessionId + ":", StringComparison.Ordinal))
                                 .ToArray())
                    {
                        room.TrapRetriggerReadyAtBySessionTrapKey.Remove(trapKey);
                    }

                    affectedRoom = room;
                    if (room.Members.Count == 0)
                    {
                        ResetEmptyRoomUnsafe(room);
                    }
                    else if (string.Equals(room.HostSessionId, session.SessionId, StringComparison.Ordinal))
                    {
                        room.HostSessionId = room.Members[0].SessionId;
                    }
                    break;
                }
            }

            session.RoomCode = string.Empty;
            return affectedRoom;
        }
    }

    private static void ResetEmptyRoomUnsafe(SpikeRoom room)
    {
        room.State = SpikeRoomState.Lobby;
        room.StateEnteredUtc = DateTimeOffset.UtcNow;
        room.CountdownEndsUtc = DateTimeOffset.MinValue;
        room.ActiveEndsUtc = DateTimeOffset.MinValue;
        room.SnapshotSequence = 0;
        room.EndReason = string.Empty;
        room.ForfeitingPlayerName = string.Empty;
        room.PersistenceStatus = string.Empty;
        room.PersistenceDetail = string.Empty;
        room.PendingMatchResult = null;
        room.PersistenceTask = null;
        room.LeaderboardRows = [];
        room.HostSessionId = string.Empty;
        room.ActiveBatteryIds.Clear();
        room.BatteryPositionsById.Clear();
        room.PendingRespawns.Clear();
        room.RecentSpawnHistory.Clear();
    }

    private SpikeRoom? FindRoomUnsafe(ClientSession session)
    {
        var room = _rooms.Values.FirstOrDefault(candidate => candidate.Members.Contains(session));
        if (room is not null)
        {
            EnsureHostAssignedUnsafe(room);
        }

        return room;
    }

    private void AddMember(SpikeRoom room, ClientSession session)
    {
        room.Members.Add(session);
        if (string.IsNullOrWhiteSpace(room.HostSessionId))
        {
            room.HostSessionId = session.SessionId;
        }
        room.ReadyBySessionId[session.SessionId] = false;
        room.ScoreBySessionId[session.SessionId] = 0;
        room.EffectsBySessionId[session.SessionId] = new PlayerEffectState();
        room.SlowShotReadyAtBySessionId[session.SessionId] = DateTimeOffset.MinValue;
        room.PlayerPositionsBySessionId[session.SessionId] = new SpikeVec2(0f, 0f);
        session.RoomCode = room.RoomCode;
    }

    private static void EnsureHostAssignedUnsafe(SpikeRoom room)
    {
        if (room.Members.Count == 0)
        {
            room.HostSessionId = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(room.HostSessionId) ||
            room.Members.All(member => !string.Equals(member.SessionId, room.HostSessionId, StringComparison.Ordinal)))
        {
            room.HostSessionId = room.Members[0].SessionId;
        }
    }

    private string GenerateNextRoomCode()
    {
        string code;
        do
        {
            code = FormattableString.Invariant($"ROOM{_nextRoomNumber:00}");
            _nextRoomNumber += 1;
        }
        while (_rooms.ContainsKey(code));

        return code;
    }
}
