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

        using var clientA = new NetworkSpikeClient(config);
        using var clientB = new NetworkSpikeClient(config);
        using var badClient = new NetworkSpikeClient(config);

        clientA.MessageReceived += msg =>
        {
            if (msg.Type == "room_joined" && string.IsNullOrEmpty(roomCode)) roomCode = msg.RoomCode;
            if (msg.Type == "input_frame_ack") inputAckObserved = true;
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

        await clientA.SendInputFrameAsync(1, new Vector2(1f, 0f), Vector2.right, false, cts.Token);
        await Task.Delay(500, cts.Token);

        await Task.Delay(TimeSpan.FromSeconds(6), cts.Token);

        var success = !string.IsNullOrWhiteSpace(roomCode) && mismatchObserved && inputAckObserved && staleObserved;
        if (!success)
        {
            Debug.LogError($"Smoke failed. room={roomCode}, mismatch={mismatchObserved}, inputAck={inputAckObserved}, stale={staleObserved}");
            return 3;
        }

        Debug.Log("Network spike smoke passed.");
        return 0;
    }
}
}
