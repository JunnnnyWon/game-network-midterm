namespace BatteryRushArena.NetworkSpikeServer;

/// <summary>
/// Minimal authoritative room state used by the room-state flow slice.
/// </summary>
public sealed class SpikeRoom
{
    public SpikeRoom(string roomCode)
    {
        RoomCode = roomCode;
        State = SpikeRoomState.Lobby;
        StateEnteredUtc = DateTimeOffset.UtcNow;
    }

    public string RoomCode { get; }
    public List<ClientSession> Members { get; } = new();
    public Dictionary<string, bool> ReadyBySessionId { get; } = new(StringComparer.Ordinal);
    public SpikeRoomState State { get; set; }
    public DateTimeOffset StateEnteredUtc { get; set; }
    public DateTimeOffset CountdownEndsUtc { get; set; }
    public DateTimeOffset ActiveEndsUtc { get; set; }
    public string EndReason { get; set; } = string.Empty;

}

public enum SpikeRoomState
{
    Lobby,
    Countdown,
    Active,
    Ended,
    Saving,
    ResultsReady
}
