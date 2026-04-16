using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BatteryRushArena.NetworkSpike;
using UnityEditor;
using UnityEngine;

namespace BatteryRushArena.Editor
{
    /// <summary>
    /// Batch-mode smoke validation for the current NetworkSpike slice.
    /// </summary>
    public static class NetworkSpikeBatchSmoke
    {
        public static void Run()
        {
            try
            {
                var exitCode = RunAsync().GetAwaiter().GetResult();
                EditorApplication.Exit(exitCode);
            }
            catch (Exception exception)
            {
                Debug.LogError("NetworkSpikeBatchSmoke crashed: " + exception);
                EditorApplication.Exit(99);
            }
        }

        private static async Task<int> RunAsync()
        {
            var config = new NetworkSpikeClientConfig();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var observations = new SmokeObservations();

            using var clientA = new NetworkSpikeClient(config);
            using var clientB = new NetworkSpikeClient(config);
            using var badClient = new NetworkSpikeClient(config);

            AttachObservationHandlers(clientA, clientB, badClient, observations);

            var roomCode = await EstablishMatchAsync(clientA, clientB, badClient, observations, cts.Token);
            if (string.IsNullOrWhiteSpace(roomCode))
            {
                Debug.LogError("Room creation did not return a room code.");
                return 2;
            }

            var activeSnapshot = await VerifyActiveFeedsAsync(clientA, observations, cts.Token);
            if (activeSnapshot == null)
            {
                return 3;
            }

            if (!await VerifyEffectsAsync(clientA, clientB, observations, cts.Token))
            {
                return 3;
            }

            if (!await VerifyResultsFlowAsync(clientA, observations, activeSnapshot, cts.Token))
            {
                return 3;
            }

            if (!await VerifyStaleTimeoutAsync(config, observations, cts.Token))
            {
                return 3;
            }

            if (!observations.AllChecksPassed(roomCode))
            {
                Debug.LogError(observations.BuildFailureSummary(roomCode));
                return 4;
            }

            Debug.Log("Network spike smoke passed.");
            return 0;
        }

        private static void AttachObservationHandlers(
            NetworkSpikeClient clientA,
            NetworkSpikeClient clientB,
            NetworkSpikeClient badClient,
            SmokeObservations observations)
        {
            clientA.MessageReceived += msg =>
            {
                observations.ObserveRoomCode(msg);
                observations.ObserveRoomSnapshot(msg);
            };
            clientB.MessageReceived += msg =>
            {
                observations.ObserveRoomCode(msg);
                observations.ObserveStale(msg);
            };
            badClient.MessageReceived += observations.ObserveMismatch;
        }

        private static async Task<string> EstablishMatchAsync(
            NetworkSpikeClient clientA,
            NetworkSpikeClient clientB,
            NetworkSpikeClient badClient,
            SmokeObservations observations,
            CancellationToken cancellationToken)
        {
            await clientA.ConnectAndHandshakeAsync("PlayerA", cancellationToken: cancellationToken);
            await Task.Delay(200, cancellationToken);
            await clientA.CreateRoomAsync(cancellationToken);
            await Task.Delay(500, cancellationToken);

            await clientB.ConnectAndHandshakeAsync("PlayerB", cancellationToken: cancellationToken);
            await Task.Delay(200, cancellationToken);
            await clientB.JoinRoomAsync(observations.RoomCode, cancellationToken);
            await Task.Delay(500, cancellationToken);

            await badClient.ConnectAndHandshakeAsync("BadClient", "bad-version", cancellationToken);
            await Task.Delay(500, cancellationToken);

            await clientA.SetReadyAsync(true, cancellationToken);
            await clientB.SetReadyAsync(true, cancellationToken);
            return observations.RoomCode;
        }

        private static async Task<SpikeServerMessage> VerifyActiveFeedsAsync(
            NetworkSpikeClient clientA,
            SmokeObservations observations,
            CancellationToken cancellationToken)
        {
            var activeSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => msg.RoomState == "Active", cancellationToken);
            if (activeSnapshot == null || !HasPlayersInPositionFeed(activeSnapshot, "PlayerA", "PlayerB"))
            {
                Debug.LogError("Authoritative player positions were not exposed for both players at match start.");
                return null;
            }

            Debug.Log("PASS: Active-room snapshots expose authoritative player positions for both players.");

            for (var tick = 1; tick <= 3; tick++)
            {
                await clientA.SendInputFrameAsync(tick, Vector2.right, Vector2.right, false, cancellationToken);
                activeSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => msg.Detail == "movement_applied" || msg.Detail == "battery_collected", cancellationToken);
            }

            if (ExtractPlayerScore(activeSnapshot, "PlayerA") < 1)
            {
                Debug.LogError("Movement-driven battery pickup was not observed.");
                return null;
            }

            if (!HasScoreboardEntries(activeSnapshot, "PlayerA", "PlayerB"))
            {
                Debug.LogError("Scoreboard feed did not expose both players during live play.");
                return null;
            }

            Debug.Log("PASS: Live score feed exposes both players during gameplay.");
            return activeSnapshot;
        }

        private static async Task<bool> VerifyEffectsAsync(
            NetworkSpikeClient clientA,
            NetworkSpikeClient clientB,
            SmokeObservations observations,
            CancellationToken cancellationToken)
        {
            await clientA.SendInputFrameAsync(4, Vector2.up, Vector2.right, false, cancellationToken);
            var trapSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => HasEffect(msg, "PlayerA", "Trap", 0.80f), cancellationToken, 4000);
            if (trapSnapshot == null)
            {
                Debug.LogError("Trap was not observed from movement-driven overlap.");
                return false;
            }

            var playerAPosition = ExtractPosition(trapSnapshot, "PlayerA");
            var playerBPosition = ExtractPosition(trapSnapshot, "PlayerB");
            var aimAtPlayerA = (playerAPosition - playerBPosition).normalized;
            await clientB.SendInputFrameAsync(1, Vector2.zero, aimAtPlayerA, true, cancellationToken);
            var slowShotSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => HasEffect(msg, "PlayerA", "SlowShot", 0.65f), cancellationToken);
            if (slowShotSnapshot == null)
            {
                Debug.LogError("Slow shot effect was not observed from input-frame fire.");
                return false;
            }

            if (!HasEffectEntry(slowShotSnapshot, "PlayerA"))
            {
                Debug.LogError("Effect feed did not expose PlayerA's debuff payload.");
                return false;
            }

            Debug.Log("PASS: Effect feed exposes debuff state for the local player.");

            await clientA.SendInputFrameAsync(5, Vector2.up, Vector2.right, false, cancellationToken);
            var ignoredStrongSlow = await WaitForAnyMessageAsync(clientA, msg => msg.Type == "room_snapshot" && msg.Detail == "movement_applied", cancellationToken, 2000);
            if (ignoredStrongSlow == null)
            {
                Debug.LogError("Movement after strong slow did not produce a snapshot.");
                return false;
            }

            var immunitySnapshot = await WaitForRoomSnapshotAsync(clientA, msg => HasImmunity(msg, "PlayerA"), cancellationToken, 4000);
            if (immunitySnapshot == null)
            {
                Debug.LogError("Immunity window was not observed after slow expiry.");
                return false;
            }

            await clientA.SendInputFrameAsync(6, Vector2.up, Vector2.right, false, cancellationToken);
            var ignoredImmunity = await WaitForAnyMessageAsync(clientA, msg => msg.Type == "room_snapshot" && msg.Detail == "movement_applied", cancellationToken, 2000);
            if (ignoredImmunity == null)
            {
                Debug.LogError("Movement during immunity did not stay observable.");
                return false;
            }

            await Task.Delay(800, cancellationToken);
            await clientA.SendInputFrameAsync(7, Vector2.down, Vector2.right, false, cancellationToken);
            trapSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => HasEffect(msg, "PlayerA", "Trap", 0.80f), cancellationToken, 4000);
            if (trapSnapshot == null)
            {
                Debug.LogError("Trap effect was not observed after immunity expired.");
                return false;
            }

            return true;
        }

        private static async Task<bool> VerifyResultsFlowAsync(
            NetworkSpikeClient clientA,
            SmokeObservations observations,
            SpikeServerMessage activeSnapshot,
            CancellationToken cancellationToken)
        {
            var points = 0;
            while (points < 10)
            {
                activeSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => msg.RoomState == "Active" && msg.ActiveBatteryIds != null && msg.ActiveBatteryIds.Length > 0, cancellationToken);
                if (activeSnapshot == null)
                {
                    Debug.LogError("No active batteries available during scoring test.");
                    return false;
                }

                foreach (var batteryId in activeSnapshot.ActiveBatteryIds)
                {
                    var batteryPosition = LookupBatteryPosition(batteryId);
                    var currentPosition = ExtractPosition(activeSnapshot, "PlayerA");
                    var moveVector = (batteryPosition - currentPosition).normalized;
                    await clientA.SendInputFrameAsync(8 + points, moveVector, Vector2.right, false, cancellationToken);
                    var collectSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => msg.Detail == "battery_collected", cancellationToken, 4000);
                    if (collectSnapshot == null)
                    {
                        Debug.LogError("Movement-driven battery collection did not broadcast.");
                        return false;
                    }

                    activeSnapshot = collectSnapshot;
                    points = ExtractPlayerScore(collectSnapshot, "PlayerA");
                    if (points >= 10 || collectSnapshot.RoomState != "Active")
                    {
                        break;
                    }
                }

                if (points < 10)
                {
                    var respawnSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => msg.Detail == "battery_respawned", cancellationToken, 5000);
                    if (respawnSnapshot == null)
                    {
                        Debug.LogError("Battery respawn did not broadcast.");
                        return false;
                    }

                    activeSnapshot = respawnSnapshot;
                }
            }

            var savingSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => msg.RoomState == "Saving", cancellationToken, 4000);
            if (savingSnapshot == null || !HasPersistenceStatus(savingSnapshot, "Saving"))
            {
                Debug.LogError("Saving snapshot did not expose the authoritative persistence status.");
                return false;
            }

            Debug.Log("PASS: Saving snapshot exposes authoritative persistence status.");

            var resultsSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => msg.RoomState == "ResultsReady", cancellationToken, 4000);
            if (resultsSnapshot == null ||
                !HasPersistenceStatus(resultsSnapshot, "Saved") ||
                !HasScoreboardEntries(resultsSnapshot, "PlayerA", "PlayerB") ||
                ExtractPlayerScore(resultsSnapshot, "PlayerA") < 10 ||
                !string.Equals(resultsSnapshot.EndReason, "TargetScoreReached", StringComparison.Ordinal))
            {
                Debug.LogError("Results snapshot did not expose the authoritative end reason, persistence status, and final scores together.");
                return false;
            }

            Debug.Log("PASS: Results snapshot exposes end reason, final scores, and persistence status together.");
            return true;
        }

        private static async Task<bool> VerifyStaleTimeoutAsync(
            NetworkSpikeClientConfig config,
            SmokeObservations observations,
            CancellationToken cancellationToken)
        {
            using var staleClient = new NetworkSpikeClient(config);
            staleClient.MessageReceived += observations.ObserveStale;
            await staleClient.ConnectAndHandshakeAsync("IdleClient", cancellationToken: cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);
            return observations.StaleObserved;
        }

        private static async Task<SpikeServerMessage> WaitForRoomSnapshotAsync(NetworkSpikeClient client, Func<SpikeServerMessage, bool> predicate, CancellationToken cancellationToken, int timeoutMs = 8000)
        {
            SpikeServerMessage observed = null;
            void Handler(SpikeServerMessage message)
            {
                if (message.Type == "room_snapshot" && predicate(message))
                {
                    observed = message;
                }
            }

            client.MessageReceived += Handler;
            try
            {
                var started = DateTimeOffset.UtcNow;
                while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow - started < TimeSpan.FromMilliseconds(timeoutMs))
                {
                    if (observed != null)
                    {
                        return observed;
                    }

                    await Task.Delay(50, cancellationToken);
                }

                return observed;
            }
            finally
            {
                client.MessageReceived -= Handler;
            }
        }

        private static async Task<SpikeServerMessage> WaitForAnyMessageAsync(NetworkSpikeClient client, Func<SpikeServerMessage, bool> predicate, CancellationToken cancellationToken, int timeoutMs = 8000)
        {
            SpikeServerMessage observed = null;
            void Handler(SpikeServerMessage message)
            {
                if (predicate(message))
                {
                    observed = message;
                }
            }

            client.MessageReceived += Handler;
            try
            {
                var started = DateTimeOffset.UtcNow;
                while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow - started < TimeSpan.FromMilliseconds(timeoutMs))
                {
                    if (observed != null)
                    {
                        return observed;
                    }

                    await Task.Delay(50, cancellationToken);
                }

                return observed;
            }
            finally
            {
                client.MessageReceived -= Handler;
            }
        }

        private static int ExtractPlayerScore(SpikeServerMessage message, string playerName)
        {
            foreach (var entry in message.Scoreboard)
            {
                if (!entry.StartsWith(playerName + ":", StringComparison.Ordinal))
                {
                    continue;
                }

                if (int.TryParse(entry.Substring(playerName.Length + 1), out var score))
                {
                    return score;
                }
            }

            return 0;
        }

        private static Vector2 ExtractPosition(SpikeServerMessage message, string playerName)
        {
            foreach (var entry in message.PlayerPositions)
            {
                if (!entry.StartsWith(playerName + ":", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = entry.Split(':');
                if (parts.Length < 3)
                {
                    continue;
                }

                if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    return new Vector2(x, y);
                }
            }

            return Vector2.zero;
        }

        private static Vector2 LookupBatteryPosition(int batteryId) => batteryId switch
        {
            1 => new Vector2(0f, 0f),
            2 => new Vector2(0f, 2f),
            3 => new Vector2(0f, -2f),
            4 => new Vector2(2f, 0f),
            5 => new Vector2(-2f, 0f),
            6 => new Vector2(1.5f, 1.5f),
            7 => new Vector2(-1.5f, 1.5f),
            8 => new Vector2(1.5f, -1.5f),
            _ => Vector2.zero
        };

        private static bool HasPlayersInPositionFeed(SpikeServerMessage message, params string[] playerNames)
        {
            foreach (var playerName in playerNames)
            {
                if (!HasPositionEntry(message, playerName))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasPositionEntry(SpikeServerMessage message, string playerName)
        {
            foreach (var entry in message.PlayerPositions)
            {
                if (entry.StartsWith(playerName + ":", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasScoreboardEntries(SpikeServerMessage message, params string[] playerNames)
        {
            foreach (var playerName in playerNames)
            {
                if (!HasScoreboardEntry(message, playerName))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasScoreboardEntry(SpikeServerMessage message, string playerName)
        {
            foreach (var entry in message.Scoreboard)
            {
                if (entry.StartsWith(playerName + ":", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPersistenceStatus(SpikeServerMessage message, string expectedStatus) =>
            string.Equals(message.PersistenceStatus, expectedStatus, StringComparison.Ordinal);

        private static bool HasEffectEntry(SpikeServerMessage message, string playerName)
        {
            foreach (var entry in message.EffectStates)
            {
                if (entry.StartsWith(playerName + ":", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasEffect(SpikeServerMessage message, string playerName, string source, float multiplier)
        {
            foreach (var entry in message.EffectStates)
            {
                if (!entry.StartsWith(playerName + ":", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = entry.Split(':');
                if (parts.Length < 5)
                {
                    continue;
                }

                if (parts[2] == source &&
                    float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var observedMultiplier) &&
                    Math.Abs(observedMultiplier - multiplier) < 0.01f)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasImmunity(SpikeServerMessage message, string playerName)
        {
            foreach (var entry in message.EffectStates)
            {
                if (!entry.StartsWith(playerName + ":", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = entry.Split(':');
                if (parts.Length < 5)
                {
                    continue;
                }

                if (float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var immunitySeconds) &&
                    immunitySeconds > 0.0f)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class SmokeObservations
        {
            public string RoomCode { get; private set; } = string.Empty;
            public bool MismatchObserved { get; private set; }
            public bool StaleObserved { get; private set; }
            public bool CountdownObserved { get; private set; }
            public bool ActiveObserved { get; private set; }
            public bool SavingObserved { get; private set; }
            public bool ResultsObserved { get; private set; }
            public bool TargetScoreObserved { get; private set; }
            public bool MovementObserved { get; private set; }
            public bool SlowShotObserved { get; private set; }
            public bool TrapObserved { get; private set; }
            public bool ImmunityObserved { get; private set; }
            public bool StrongestSlowObserved { get; private set; }
            public bool PositionFeedObserved { get; private set; }
            public bool ScoreboardFeedObserved { get; private set; }
            public bool EffectFeedObserved { get; private set; }
            public bool SavingStatusObserved { get; private set; }
            public bool SavedStatusObserved { get; private set; }
            public bool ResultsPayloadObserved { get; private set; }

            public void ObserveRoomCode(SpikeServerMessage message)
            {
                if (message.Type == "room_joined" && string.IsNullOrEmpty(RoomCode))
                {
                    RoomCode = message.RoomCode;
                }
            }

            public void ObserveRoomSnapshot(SpikeServerMessage message)
            {
                if (message.Type != "room_snapshot")
                {
                    return;
                }

                CountdownObserved |= message.RoomState == "Countdown";
                ActiveObserved |= message.RoomState == "Active";
                SavingObserved |= message.RoomState == "Saving";
                ResultsObserved |= message.RoomState == "ResultsReady";
                TargetScoreObserved |= message.EndReason == "TargetScoreReached";
                MovementObserved |= message.Detail == "movement_applied";
                SlowShotObserved |= HasEffect(message, "PlayerA", "SlowShot", 0.65f);
                TrapObserved |= HasEffect(message, "PlayerA", "Trap", 0.80f);
                ImmunityObserved |= HasImmunity(message, "PlayerA");
                StrongestSlowObserved |= HasEffect(message, "PlayerA", "Trap", 0.80f) && !HasEffect(message, "PlayerA", "SlowShot", 0.65f);
                PositionFeedObserved |= HasPlayersInPositionFeed(message, "PlayerA", "PlayerB");
                ScoreboardFeedObserved |= HasScoreboardEntries(message, "PlayerA", "PlayerB");
                EffectFeedObserved |= HasEffectEntry(message, "PlayerA");
                SavingStatusObserved |= message.RoomState == "Saving" && HasPersistenceStatus(message, "Saving");
                SavedStatusObserved |= message.RoomState == "ResultsReady" && HasPersistenceStatus(message, "Saved");
                ResultsPayloadObserved |= message.RoomState == "ResultsReady"
                    && HasPersistenceStatus(message, "Saved")
                    && HasScoreboardEntries(message, "PlayerA", "PlayerB")
                    && message.EndReason == "TargetScoreReached";
            }

            public void ObserveMismatch(SpikeServerMessage message)
            {
                if (message.Type == "hello_rejected" && message.Error == "protocol_mismatch")
                {
                    MismatchObserved = true;
                }
            }

            public void ObserveStale(SpikeServerMessage message)
            {
                if (message.Type == "session_stale")
                {
                    StaleObserved = true;
                }
            }

            public bool AllChecksPassed(string roomCode) =>
                !string.IsNullOrWhiteSpace(roomCode)
                && MismatchObserved
                && CountdownObserved
                && ActiveObserved
                && MovementObserved
                && TargetScoreObserved
                && SavingObserved
                && ResultsObserved
                && SlowShotObserved
                && TrapObserved
                && ImmunityObserved
                && StrongestSlowObserved
                && PositionFeedObserved
                && ScoreboardFeedObserved
                && EffectFeedObserved
                && SavingStatusObserved
                && SavedStatusObserved
                && ResultsPayloadObserved
                && StaleObserved;

            public string BuildFailureSummary(string roomCode) =>
                $"Smoke failed. room={roomCode}, mismatch={MismatchObserved}, countdown={CountdownObserved}, active={ActiveObserved}, movement={MovementObserved}, targetScore={TargetScoreObserved}, saving={SavingObserved}, results={ResultsObserved}, slowShot={SlowShotObserved}, trap={TrapObserved}, immunity={ImmunityObserved}, strongest={StrongestSlowObserved}, positionFeed={PositionFeedObserved}, scoreboardFeed={ScoreboardFeedObserved}, effectFeed={EffectFeedObserved}, savingStatus={SavingStatusObserved}, savedStatus={SavedStatusObserved}, resultsPayload={ResultsPayloadObserved}, stale={StaleObserved}";
        }
    }
}
