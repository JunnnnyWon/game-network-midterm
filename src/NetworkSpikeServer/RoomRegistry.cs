namespace BatteryRushArena.NetworkSpikeServer;

/// <summary>
/// Minimal in-memory room registry used by the network-session spike.
/// </summary>
public sealed class RoomRegistry
{
    private readonly Dictionary<string, List<ClientSession>> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new();

    /// <summary>
    /// Creates a new room and adds the provided client as the first member.
    /// </summary>
    public string CreateRoom(ClientSession session)
    {
        string code;
        do
        {
            code = GenerateRoomCode();
        }
        while (_rooms.ContainsKey(code));

        _rooms[code] = new List<ClientSession> { session };
        return code;
    }

    /// <summary>
    /// Attempts to join an existing room.
    /// </summary>
    public bool TryJoinRoom(string roomCode, ClientSession session, int maxPlayers, out string error)
    {
        if (!_rooms.TryGetValue(roomCode, out var members))
        {
            error = "invalid_room_code";
            return false;
        }

        if (members.Count >= maxPlayers)
        {
            error = "room_full";
            return false;
        }

        members.Add(session);
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Removes the client from any room membership.
    /// </summary>
    public void Remove(ClientSession session)
    {
        string? emptyRoom = null;
        foreach (var pair in _rooms)
        {
            if (pair.Value.Remove(session) && pair.Value.Count == 0)
            {
                emptyRoom = pair.Key;
            }
        }

        if (emptyRoom is not null)
        {
            _rooms.Remove(emptyRoom);
        }
    }

    private string GenerateRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 6).Select(_ => alphabet[_random.Next(alphabet.Length)]).ToArray());
    }
}
