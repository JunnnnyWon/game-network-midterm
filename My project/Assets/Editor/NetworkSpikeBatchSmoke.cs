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
            var roomCode = string.Empty;
            var mismatchObserved = false;
            var staleObserved = false;
            var countdownObserved = false;
            var activeObserved = false;
            var savingObserved = false;
            var resultsObserved = false;
            var targetScoreObserved = false;
            var slowShotObserved = false;
            var trapObserved = false;
            var immunityObserved = false;
            var strongestSlowObserved = false;

            using var clientA = new NetworkSpikeClient(config);
            using var clientB = new NetworkSpikeClient(config);
            using var badClient = new NetworkSpikeClient(config);

            clientA.MessageReceived += msg =>
            {
                if (msg.Type == "room_joined" && string.IsNullOrEmpty(roomCode)) roomCode = msg.RoomCode;
                if (msg.Type == "room_snapshot" && msg.RoomState == "Countdown") countdownObserved = true;
                if (msg.Type == "room_snapshot" && msg.RoomState == "Active") activeObserved = true;
                if (msg.Type == "room_snapshot" && msg.RoomState == "Saving") savingObserved = true;
                if (msg.Type == "room_snapshot" && msg.RoomState == "ResultsReady") resultsObserved = true;
                if (msg.Type == "room_snapshot" && msg.EndReason == "TargetScoreReached") targetScoreObserved = true;
                if (msg.Type == "room_snapshot" && HasEffect(msg, "PlayerA", "SlowShot", 0.65f)) slowShotObserved = true;
                if (msg.Type == "room_snapshot" && HasEffect(msg, "PlayerA", "Trap", 0.80f)) trapObserved = true;
                if (msg.Type == "room_snapshot" && HasImmunity(msg, "PlayerA")) immunityObserved = true;
                if (msg.Type == "room_snapshot" && HasEffect(msg, "PlayerA", "Trap", 0.80f) && !HasEffect(msg, "PlayerA", "SlowShot", 0.65f)) strongestSlowObserved = true;
            };
            clientB.MessageReceived += msg =>
            {
                if (msg.Type == "room_joined" && string.IsNullOrEmpty(roomCode)) roomCode = msg.RoomCode;
                if (msg.Type == "session_stale") staleObserved = true;
            };
            badClient.MessageReceived += msg =>
            {
                if (msg.Type == "hello_rejected" && msg.Error == "protocol_mismatch") mismatchObserved = true;
            };

            await clientA.ConnectAndHandshakeAsync("PlayerA", cancellationToken: cts.Token);
            await Task.Delay(200, cts.Token);
            await clientA.CreateRoomAsync(cts.Token);
            await Task.Delay(500, cts.Token);
            if (string.IsNullOrWhiteSpace(roomCode))
            {
                Debug.LogError("Room creation did not return a room code.");
                return 2;
            }

            await clientB.ConnectAndHandshakeAsync("PlayerB", cancellationToken: cts.Token);
            await Task.Delay(200, cts.Token);
            await clientB.JoinRoomAsync(roomCode, cts.Token);
            await Task.Delay(500, cts.Token);

            await badClient.ConnectAndHandshakeAsync("BadClient", "bad-version", cts.Token);
            await Task.Delay(500, cts.Token);

            await clientA.SetReadyAsync(true, cts.Token);
            await clientB.SetReadyAsync(true, cts.Token);
            var activeSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => msg.RoomState == "Active", cts.Token);

            await clientB.FireSlowShotAsync(cts.Token);
            var slowShotSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => HasEffect(msg, "PlayerA", "SlowShot", 0.65f), cts.Token);
            if (slowShotSnapshot == null)
            {
                Debug.LogError("Slow shot effect was not observed.");
                return 3;
            }

            await clientA.TriggerTrapAsync(1, cts.Token);
            var ignoredStrongSlow = await WaitForAnyMessageAsync(clientA, msg => msg.Type == "trigger_trap_ignored", cts.Token, 2000);
            if (ignoredStrongSlow == null)
            {
                Debug.LogError("Trap was not ignored while stronger slow was active.");
                return 3;
            }

            var immunitySnapshot = await WaitForRoomSnapshotAsync(clientA, msg => HasImmunity(msg, "PlayerA"), cts.Token, 4000);
            if (immunitySnapshot == null)
            {
                Debug.LogError("Immunity window was not observed after slow expiry.");
                return 3;
            }

            await clientA.TriggerTrapAsync(1, cts.Token);
            var ignoredImmunity = await WaitForAnyMessageAsync(clientA, msg => msg.Type == "trigger_trap_ignored", cts.Token, 2000);
            if (ignoredImmunity == null)
            {
                Debug.LogError("Trap was not ignored during immunity window.");
                return 3;
            }

            await Task.Delay(800, cts.Token);
            await clientA.TriggerTrapAsync(1, cts.Token);
            var trapSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => HasEffect(msg, "PlayerA", "Trap", 0.80f), cts.Token, 4000);
            if (trapSnapshot == null)
            {
                Debug.LogError("Trap effect was not observed after immunity expired.");
                return 3;
            }

            var points = 0;
            while (points < 10)
            {
                activeSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => msg.RoomState == "Active" && msg.ActiveBatteryIds != null && msg.ActiveBatteryIds.Length > 0, cts.Token);
                if (activeSnapshot == null)
                {
                    Debug.LogError("No active batteries available during scoring test.");
                    return 3;
                }

                foreach (var batteryId in activeSnapshot.ActiveBatteryIds)
                {
                    await clientA.CollectBatteryAsync(batteryId, cts.Token);
                    var collectSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => msg.Detail == "battery_collected", cts.Token);
                    if (collectSnapshot == null)
                    {
                        Debug.LogError("Battery collection did not broadcast.");
                        return 3;
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
                    var respawnSnapshot = await WaitForRoomSnapshotAsync(clientA, msg => msg.Detail == "battery_respawned", cts.Token, 5000);
                    if (respawnSnapshot == null)
                    {
                        Debug.LogError("Battery respawn did not broadcast.");
                        return 3;
                    }

                    activeSnapshot = respawnSnapshot;
                }
            }

            using var staleClient = new NetworkSpikeClient(config);
            staleClient.MessageReceived += msg =>
            {
                if (msg.Type == "session_stale") staleObserved = true;
            };
            await staleClient.ConnectAndHandshakeAsync("IdleClient", cancellationToken: cts.Token);
            await Task.Delay(TimeSpan.FromSeconds(6), cts.Token);

            var success = !string.IsNullOrWhiteSpace(roomCode)
                          && mismatchObserved
                          && countdownObserved
                          && activeObserved
                          && targetScoreObserved
                          && savingObserved
                          && resultsObserved
                          && slowShotObserved
                          && trapObserved
                          && immunityObserved
                          && strongestSlowObserved
                          && staleObserved;

            if (!success)
            {
                Debug.LogError(
                    $"Smoke failed. room={roomCode}, mismatch={mismatchObserved}, countdown={countdownObserved}, active={activeObserved}, targetScore={targetScoreObserved}, saving={savingObserved}, results={resultsObserved}, slowShot={slowShotObserved}, trap={trapObserved}, immunity={immunityObserved}, strongest={strongestSlowObserved}, stale={staleObserved}");
                return 4;
            }

            Debug.Log("Network spike smoke passed.");
            return 0;
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
    }
}
