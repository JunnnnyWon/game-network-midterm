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
    int MaxPlayersPerRoom,
    TimeSpan MatchDuration,
    int TargetScore,
    int ActiveBatteryCount,
    TimeSpan BatteryRespawnDelay,
    int SpawnPointCount,
    TimeSpan SlowShotCooldown,
    float SlowShotMoveMultiplier,
    TimeSpan SlowShotDuration,
    float TrapMoveMultiplier,
    TimeSpan TrapDuration,
    TimeSpan TrapRetriggerCooldown,
    TimeSpan PostSlowImmunity)
{
    /// <summary>
    /// Creates the default spike configuration aligned with ADR-0001 and ADR-0005.
    /// </summary>
    public static SpikeServerConfig CreateDefault() => new(
        Host: "127.0.0.1",
        Port: 7777,
        ProtocolVersion: "bra-spike-v1",
        HeartbeatInterval: TimeSpan.FromSeconds(2),
        StaleTimeout: TimeSpan.FromSeconds(5),
        MaxPlayersPerRoom: 2,
        MatchDuration: TimeSpan.FromSeconds(120),
        TargetScore: 10,
        ActiveBatteryCount: 3,
        BatteryRespawnDelay: TimeSpan.FromSeconds(3),
        SpawnPointCount: 8,
        SlowShotCooldown: TimeSpan.FromSeconds(4),
        SlowShotMoveMultiplier: 0.65f,
        SlowShotDuration: TimeSpan.FromSeconds(1.25),
        TrapMoveMultiplier: 0.80f,
        TrapDuration: TimeSpan.FromSeconds(0.75),
        TrapRetriggerCooldown: TimeSpan.FromSeconds(1.5),
        PostSlowImmunity: TimeSpan.FromSeconds(0.5));
}
