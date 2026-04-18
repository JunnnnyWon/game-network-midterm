using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BatteryRushArena.NetworkSpike
{
    /// <summary>
    /// Shared protocol message sent from the Unity client to the external spike server.
    /// </summary>
    [Serializable]
    public sealed class SpikeClientMessage
    {
        public string Type = string.Empty;
        public string ProtocolVersion = string.Empty;
        public string PlayerName = string.Empty;
        public string RoomCode = string.Empty;
        public int Tick;
        public float MoveX;
        public float MoveY;
        public float AimX;
        public float AimY;
        public bool FirePressed;
        public bool IsReady;
        public int BatteryId;
        public int TrapId;
    }

    /// <summary>
    /// Shared protocol message sent from the spike server back to the Unity client.
    /// </summary>
    [Serializable]
    public sealed class SpikeServerMessage
    {
        public string Type = string.Empty;
        public string RoomCode = string.Empty;
        public string SessionId = string.Empty;
        public string Error = string.Empty;
        public int Tick;
        public string Detail = string.Empty;
        public string RoomState = string.Empty;
        public int PlayerCount;
        public int ReadyPlayers;
        public float CountdownRemainingSeconds;
        public string EndReason = string.Empty;
        public string PersistenceStatus = string.Empty;
        public string[] Members = Array.Empty<string>();
        public int[] ActiveBatteryIds = Array.Empty<int>();
        public string[] Scoreboard = Array.Empty<string>();
        public float MatchTimeRemainingSeconds;
        public float SlowShotCooldownRemainingSeconds;
        public string[] EffectStates = Array.Empty<string>();
        public string[] PlayerPositions = Array.Empty<string>();
        public bool SlowShotReady;
    }

    internal static class LengthPrefixedProtocol
    {
        private static readonly ConditionalWeakTable<NetworkStream, SemaphoreSlim> WriteLocks = new();

        public static async Task WriteAsync<T>(NetworkStream stream, T payload, CancellationToken cancellationToken)
        {
            var writeLock = WriteLocks.GetValue(stream, _ => new SemaphoreSlim(1, 1));
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                var json = JsonUtility.ToJson(payload);
                var body = Encoding.UTF8.GetBytes(json);
                var header = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
                await stream.WriteAsync(header, 0, header.Length, cancellationToken);
                await stream.WriteAsync(body, 0, body.Length, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            finally
            {
                writeLock.Release();
            }
        }

        public static async Task<T> ReadAsync<T>(NetworkStream stream, CancellationToken cancellationToken) where T : class
        {
            var header = new byte[4];
            if (!await ReadExactAsync(stream, header, cancellationToken))
            {
                return null;
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length <= 0 || length > 65_536)
            {
                throw new InvalidDataException("Invalid message length: " + length);
            }

            var body = new byte[length];
            if (!await ReadExactAsync(stream, body, cancellationToken))
            {
                return null;
            }

            return JsonUtility.FromJson<T>(Encoding.UTF8.GetString(body));
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, cancellationToken);
                if (read == 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }
    }
}
