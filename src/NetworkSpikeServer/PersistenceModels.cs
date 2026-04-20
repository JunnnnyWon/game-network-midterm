namespace BatteryRushArena.NetworkSpikeServer;

public sealed record MatchPlayerResult(
    string PlayerName,
    int Score,
    string Outcome);

public sealed record MatchResultPayload(
    string MatchId,
    string RoomId,
    string EndReason,
    string? WinnerPlayerName,
    DateTimeOffset EndedAtUtc,
    IReadOnlyList<MatchPlayerResult> Players);

public sealed record LeaderboardRow(
    string PlayerName,
    int Wins,
    int Draws,
    int Losses,
    int BestScore,
    int TotalMatches);

public sealed record PersistenceAttemptResult(
    bool Success,
    string Status,
    string Detail,
    IReadOnlyList<LeaderboardRow> LeaderboardRows);
