using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;

namespace BatteryRushArena.NetworkSpikeServer;

internal static class LengthPrefixedProtocol
{
    private static readonly ConditionalWeakTable<NetworkStream, SemaphoreSlim> WriteLocks = new();

    public static async Task WriteAsync<T>(NetworkStream stream, T payload, CancellationToken cancellationToken)
    {
        var writeLock = WriteLocks.GetValue(stream, _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(payload, ProtocolJson.Options);
            var body = Encoding.UTF8.GetBytes(json);
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public static async Task<T?> ReadAsync<T>(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        var headerRead = await ReadExactAsync(stream, header, cancellationToken);
        if (!headerRead)
        {
            return default;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > 65_536)
        {
            throw new InvalidDataException($"Invalid message length: {length}");
        }

        var body = new byte[length];
        var bodyRead = await ReadExactAsync(stream, body, cancellationToken);
        if (!bodyRead)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(body, ProtocolJson.Options);
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }
}
