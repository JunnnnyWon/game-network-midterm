using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryRushArena.NetworkSpikeServer;

/// <summary>
/// Client-to-server protocol envelope for the network spike.
/// </summary>
public sealed class ClientMessage
{
    public string Type { get; set; } = string.Empty;
    public string ProtocolVersion { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string RoomCode { get; set; } = string.Empty;
    public int Tick { get; set; }
    public float MoveX { get; set; }
    public float MoveY { get; set; }
    public float AimX { get; set; }
    public float AimY { get; set; }
    public bool FirePressed { get; set; }
}

/// <summary>
/// Server-to-client protocol envelope for the network spike.
/// </summary>
public sealed class ServerMessage
{
    public string Type { get; set; } = string.Empty;
    public string RoomCode { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public int Tick { get; set; }
    public string Detail { get; set; } = string.Empty;
}

/// <summary>
/// Json serializer configuration shared by the spike server and clients.
/// </summary>
public static class ProtocolJson
{
    /// <summary>
    /// Shared serializer options for compact, case-insensitive protocol payloads.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };
}
