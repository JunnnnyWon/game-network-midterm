using System;
using System.Threading;
using System.Threading.Tasks;
using BatteryRushArena.NetworkSpike;
using UnityEditor;
using UnityEngine;

namespace BatteryRushArena.Editor
{

/// <summary>
/// Batch-mode smoke validation for story-001 using the actual Unity-side transport client.
/// </summary>
public static class NetworkSpikeBatchSmoke
{
    /// <summary>
    /// Runs the Unity-side smoke test against a local spike server.
    /// </summary>
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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var roomCode = string.Empty;
        var mismatchObserved = false;
        var staleObserved = false;
        var inputAckObserved = false;
        var countdownObserved = false;
        var activeObserved = false;
        var savingObserved = false;
        var resultsObserved = false;
        var disconnectForfeitObserved = false;

        using var clientA = new NetworkSpikeClient(config);
        using var clientB = new NetworkSpikeClient(config);
        using var badClient = new NetworkSpikeClient(config);

        clientA.MessageReceived += msg =>
        {
            if (msg.Type == "room_joined" && string.IsNullOrEmpty(roomCode)) roomCode = msg.RoomCode;
            if (msg.Type == "input_frame_ack") inputAckObserved = true;
            if (msg.Type == "room_snapshot" && msg.RoomState == "Countdown") countdownObserved = true;
            if (msg.Type == "room_snapshot" && msg.RoomState == "Active") activeObserved = true;
            if (msg.Type == "room_snapshot" && msg.RoomState == "Saving") savingObserved = true;
            if (msg.Type == "room_snapshot" && msg.RoomState == "ResultsReady") resultsObserved = true;
            if (msg.Type == "room_snapshot" && msg.EndReason == "DisconnectForfeit") disconnectForfeitObserved = true;
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
        await Task.Delay(TimeSpan.FromSeconds(4), cts.Token);

        await clientA.SendInputFrameAsync(1, new Vector2(1f, 0f), Vector2.right, false, cts.Token);
        await Task.Delay(500, cts.Token);

        // Disconnect client B during active play; A should observe DisconnectForfeit and the Saving -> ResultsReady flow.
        clientB.Dispose();
        await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);

        // Open a third valid client that idles to prove stale timeout without joining a room.
        using var staleClient = new NetworkSpikeClient(config);
        staleClient.MessageReceived += msg =>
        {
            if (msg.Type == "session_stale") staleObserved = true;
        };
        await staleClient.ConnectAndHandshakeAsync("IdleClient", cancellationToken: cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(6), cts.Token);

        var success = !string.IsNullOrWhiteSpace(roomCode) && mismatchObserved && countdownObserved && activeObserved && inputAckObserved && disconnectForfeitObserved && savingObserved && resultsObserved && staleObserved;
        if (!success)
        {
            Debug.LogError($"Smoke failed. room={roomCode}, mismatch={mismatchObserved}, countdown={countdownObserved}, active={activeObserved}, inputAck={inputAckObserved}, disconnectForfeit={disconnectForfeitObserved}, saving={savingObserved}, results={resultsObserved}, stale={staleObserved}");
            return 3;
        }

        Debug.Log("Network spike smoke passed.");
        return 0;
    }
}
}
