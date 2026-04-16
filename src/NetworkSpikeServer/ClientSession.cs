using System.Net.Sockets;

namespace BatteryRushArena.NetworkSpikeServer;

/// <summary>
/// Represents one connected spike client session.
/// </summary>
public sealed class ClientSession
{
    public ClientSession(TcpClient tcpClient)
    {
        TcpClient = tcpClient;
        SessionId = Guid.NewGuid().ToString("N");
        LastSeenUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Stable session identifier for the lifetime of the TCP connection.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Underlying TCP client.
    /// </summary>
    public TcpClient TcpClient { get; }

    /// <summary>
    /// Optional player display name set during handshake.
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Optional joined room code.
    /// </summary>
    public string RoomCode { get; set; } = string.Empty;

    /// <summary>
    /// Latest processed input tick, used to drop duplicate/stale frames.
    /// </summary>
    public int LastProcessedTick { get; set; }

    /// <summary>
    /// Last time the session sent heartbeat or gameplay input.
    /// </summary>
    public DateTimeOffset LastSeenUtc { get; set; }

    /// <summary>
    /// Gets the writable stream for this session.
    /// </summary>
    public NetworkStream Stream => TcpClient.GetStream();
}
