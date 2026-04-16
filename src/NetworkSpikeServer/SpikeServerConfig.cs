namespace BatteryRushArena.NetworkSpikeServer;

/// <summary>
/// Immutable runtime configuration for the network-session spike server.
/// </summary>
public sealed record SpikeServerConfig(
    string Host,
    int Port,
    string ProtocolVersion,
    TimeSpan HeartbeatInterval,
    TimeSpan StaleTimeout,
    int MaxPlayersPerRoom)
{
    /// <summary>
    /// Creates the default spike configuration aligned with ADR-0001.
    /// </summary>
    public static SpikeServerConfig CreateDefault() => new(
        Host: "127.0.0.1",
        Port: 7777,
        ProtocolVersion: "bra-spike-v1",
        HeartbeatInterval: TimeSpan.FromSeconds(2),
        StaleTimeout: TimeSpan.FromSeconds(5),
        MaxPlayersPerRoom: 2);
}
