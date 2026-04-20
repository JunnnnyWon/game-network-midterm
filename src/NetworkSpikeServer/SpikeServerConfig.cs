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
    int ActiveTrapCount,
    float TrapMoveMultiplier,
    TimeSpan TrapDuration,
    TimeSpan TrapRetriggerCooldown,
    float TrapSpawnInset,
    float TrapSpawnMinimumSeparation,
    int TrapSpawnGenerationAttempts,
    TimeSpan PostSlowImmunity,
    float PlayerStepDistance,
    float PlayerBoundsInset,
    float BatteryPickupRadius,
    float BatterySpawnInset,
    float BatterySpawnMinimumSeparation,
    int BatterySpawnGenerationAttempts,
    float TrapTriggerRadius,
    float SlowShotRange,
    float ArenaHalfExtent,
    SpikeVec2[] PlayerSpawnPoints)
{
    /// <summary>
    /// Creates the default spike configuration aligned with ADR-0001 and ADR-0005.
    /// </summary>
    public static SpikeServerConfig CreateDefault() => new(
        Host: "127.0.0.1",
        Port: 7777,
        ProtocolVersion: "bra-spike-v1",
        HeartbeatInterval: TimeSpan.FromSeconds(2),
        StaleTimeout: TimeSpan.FromSeconds(30),
        MaxPlayersPerRoom: 2,
        MatchDuration: TimeSpan.FromSeconds(120),
        TargetScore: 10,
        ActiveBatteryCount: 3,
        BatteryRespawnDelay: TimeSpan.FromSeconds(3),
        SpawnPointCount: 8,
        SlowShotCooldown: TimeSpan.FromSeconds(4),
        SlowShotMoveMultiplier: 0.65f,
        SlowShotDuration: TimeSpan.FromSeconds(1.25),
        ActiveTrapCount: 4,
        TrapMoveMultiplier: 0.70f,
        TrapDuration: TimeSpan.FromSeconds(0.75),
        TrapRetriggerCooldown: TimeSpan.FromSeconds(1.5),
        TrapSpawnInset: 0.9f,
        TrapSpawnMinimumSeparation: 1.6f,
        TrapSpawnGenerationAttempts: 24,
        PostSlowImmunity: TimeSpan.FromSeconds(0.5),
        PlayerStepDistance: 0.75f,
        PlayerBoundsInset: 0.35f,
        BatteryPickupRadius: 0.65f,
        BatterySpawnInset: 0.9f,
        BatterySpawnMinimumSeparation: 1.6f,
        BatterySpawnGenerationAttempts: 24,
        TrapTriggerRadius: 0.65f,
        SlowShotRange: 3.5f,
        ArenaHalfExtent: 7.25f,
        PlayerSpawnPoints:
        [
            new SpikeVec2(-5.1f, 0f),
            new SpikeVec2(5.1f, 0f)
        ]);
}
