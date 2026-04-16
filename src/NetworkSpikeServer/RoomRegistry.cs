namespace BatteryRushArena.NetworkSpikeServer;

/// <summary>
/// Minimal in-memory room registry used by the network-session spike.
/// </summary>
public sealed class RoomRegistry
{
    private readonly Dictionary<string, SpikeRoom> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();

    public object SyncRoot { get; } = new();

    public IReadOnlyList<SpikeRoom> SnapshotRooms()
    {
        lock (SyncRoot)
        {
            return _rooms.Values.ToList();
        }
    }

    /// <summary>
    /// Creates a new room and adds the provided client as the first member.
    /// </summary>
    public SpikeRoom CreateRoom(ClientSession session)
    {
        lock (SyncRoot)
        {
            string code;
            do
            {
                code = GenerateRoomCode();
            }
            while (_rooms.ContainsKey(code));

            var room = new SpikeRoom(code);
            AddMember(room, session);
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
            string? emptyRoom = null;
            SpikeRoom? affectedRoom = null;
            foreach (var room in _rooms.Values)
            {
                if (room.Members.Remove(session))
                {
                    room.ReadyBySessionId.Remove(session.SessionId);
                    affectedRoom = room;
                    if (room.Members.Count == 0)
                    {
                        emptyRoom = room.RoomCode;
                    }
                    break;
                }
            }

            if (emptyRoom is not null)
            {
                _rooms.Remove(emptyRoom);
                return null;
            }

            return affectedRoom;
        }
    }

    private SpikeRoom? FindRoomUnsafe(ClientSession session)
    {
        return _rooms.Values.FirstOrDefault(room => room.Members.Contains(session));
    }

    private void AddMember(SpikeRoom room, ClientSession session)
    {
        room.Members.Add(session);
        room.ReadyBySessionId[session.SessionId] = false;
        session.RoomCode = room.RoomCode;
    }

    private string GenerateRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6).Select(_ => alphabet[_random.Next(alphabet.Length)]).ToArray());
    }
}
