using System.Text.Json;
using MySqlConnector;

namespace BatteryRushArena.NetworkSpikeServer;

public sealed class MySqlPersistenceService
{
    private readonly string _connectionString;

    public MySqlPersistenceService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<PersistenceAttemptResult> PersistMatchResultAsync(MatchResultPayload payload, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

                var insertMatch = connection.CreateCommand();
                insertMatch.Transaction = transaction;
                insertMatch.CommandText = """
                    INSERT INTO match_results
                    (match_id, room_id, ended_at_utc, end_reason, winner_player_name, player_count,
                     player_a_name, player_a_score, player_b_name, player_b_score, raw_payload_json)
                    VALUES
                    (@matchId, @roomId, @endedAtUtc, @endReason, @winnerPlayerName, @playerCount,
                     @playerAName, @playerAScore, @playerBName, @playerBScore, @rawPayloadJson)
                    ON DUPLICATE KEY UPDATE
                      room_id = room_id
                    """;
                insertMatch.Parameters.AddWithValue("@matchId", payload.MatchId);
                insertMatch.Parameters.AddWithValue("@roomId", payload.RoomId);
                insertMatch.Parameters.AddWithValue("@endedAtUtc", payload.EndedAtUtc.UtcDateTime);
                insertMatch.Parameters.AddWithValue("@endReason", payload.EndReason);
                insertMatch.Parameters.AddWithValue("@winnerPlayerName", (object?)payload.WinnerPlayerName ?? DBNull.Value);
                insertMatch.Parameters.AddWithValue("@playerCount", payload.Players.Count);
                insertMatch.Parameters.AddWithValue("@playerAName", payload.Players[0].PlayerName);
                insertMatch.Parameters.AddWithValue("@playerAScore", payload.Players[0].Score);
                insertMatch.Parameters.AddWithValue("@playerBName", payload.Players.Count > 1 ? payload.Players[1].PlayerName : DBNull.Value);
                insertMatch.Parameters.AddWithValue("@playerBScore", payload.Players.Count > 1 ? payload.Players[1].Score : DBNull.Value);
                insertMatch.Parameters.AddWithValue("@rawPayloadJson", JsonSerializer.Serialize(payload, ProtocolJson.Options));
                await insertMatch.ExecuteNonQueryAsync(cancellationToken);

                if (!string.Equals(payload.EndReason, "ServerAbort", StringComparison.Ordinal))
                {
                    foreach (var player in payload.Players)
                    {
                        var updateStats = connection.CreateCommand();
                        updateStats.Transaction = transaction;
                        updateStats.CommandText = """
                            INSERT INTO player_stats
                            (player_name, wins, draws, losses, best_score, total_matches, last_played_at)
                            VALUES
                            (@playerName, @wins, @draws, @losses, @bestScore, @totalMatches, @lastPlayedAt)
                            ON DUPLICATE KEY UPDATE
                              wins = wins + @wins,
                              draws = draws + @draws,
                              losses = losses + @losses,
                              best_score = GREATEST(best_score, @bestScore),
                              total_matches = total_matches + @totalMatches,
                              last_played_at = GREATEST(last_played_at, @lastPlayedAt)
                            """;
                        updateStats.Parameters.AddWithValue("@playerName", player.PlayerName);
                        updateStats.Parameters.AddWithValue("@wins", string.Equals(player.Outcome, "Win", StringComparison.Ordinal) ? 1 : 0);
                        updateStats.Parameters.AddWithValue("@draws", string.Equals(player.Outcome, "Draw", StringComparison.Ordinal) ? 1 : 0);
                        updateStats.Parameters.AddWithValue("@losses", string.Equals(player.Outcome, "Loss", StringComparison.Ordinal) ? 1 : 0);
                        updateStats.Parameters.AddWithValue("@bestScore", player.Score);
                        updateStats.Parameters.AddWithValue("@totalMatches", 1);
                        updateStats.Parameters.AddWithValue("@lastPlayedAt", payload.EndedAtUtc.UtcDateTime);
                        await updateStats.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                await transaction.CommitAsync(cancellationToken);
                var leaderboard = await QueryTopAsync(10, cancellationToken);
                return new PersistenceAttemptResult(true, "Saved", "MySQL write committed", leaderboard);
            }
            catch (Exception) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                if (attempt == maxAttempts - 1)
                {
                    // fall through to final attempt result
                }
            }
            catch (Exception ex)
            {
                return new PersistenceAttemptResult(false, "Failed", ex.Message, Array.Empty<LeaderboardRow>());
            }
        }

        return new PersistenceAttemptResult(false, "Failed", "Unknown persistence failure", Array.Empty<LeaderboardRow>());
    }

    public async Task<IReadOnlyList<LeaderboardRow>> QueryTopAsync(int limit, CancellationToken cancellationToken)
    {
        var rows = new List<LeaderboardRow>();
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT player_name, wins, draws, losses, best_score, total_matches
            FROM player_stats
            ORDER BY wins DESC, best_score DESC, total_matches ASC, player_name ASC
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LeaderboardRow(
                reader.GetString("player_name"),
                reader.GetInt32("wins"),
                reader.GetInt32("draws"),
                reader.GetInt32("losses"),
                reader.GetInt32("best_score"),
                reader.GetInt32("total_matches")));
        }

        return rows;
    }

    public static string BuildConnectionStringFromEnvironment()
    {
        var host = Environment.GetEnvironmentVariable("MYSQL_HOST") ?? "127.0.0.1";
        var port = Environment.GetEnvironmentVariable("MYSQL_PORT") ?? "3306";
        var database = Environment.GetEnvironmentVariable("MYSQL_DATABASE") ?? "ckgame";
        var user = Environment.GetEnvironmentVariable("MYSQL_USER") ?? "ckgame_user";
        var password = Environment.GetEnvironmentVariable("MYSQL_PASSWORD") ?? "ckgame_pass";
        return $"Server={host};Port={port};Database={database};User ID={user};Password={password};SslMode=None;AllowPublicKeyRetrieval=True;";
    }
}
