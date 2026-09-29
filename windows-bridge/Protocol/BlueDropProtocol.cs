using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XycloToothBridge.Protocol;

/// <summary>
/// Implements the exact length-prefixed JSON protocol used by receiver.py and BlueDrop AI.
/// Format: [4-byte Big-Endian Length] + [UTF-8 JSON Payload]
/// </summary>
public static class BlueDropProtocol
{
    public const int MaxFrame = 1048576; // 1 MB limit per frame
    public static readonly Guid PrimaryServiceUuid = Guid.Parse("c91c315b-8c0b-487f-a640-c073e9415d55");

    public static async Task SendJsonAsync(Stream stream, object value, CancellationToken ct = default)
    {
        string json = JsonSerializer.Serialize(value);
        byte[] raw = Encoding.UTF8.GetBytes(json);
        if (raw.Length == 0 || raw.Length > MaxFrame)
        {
            throw new InvalidOperationException($"Outgoing control frame size invalid ({raw.Length} bytes).");
        }

        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, raw.Length);

        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(raw, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<JsonElement?> ReceiveJsonAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] sizeHeader = new byte[4];
        int read = 0;
        while (read < 4)
        {
            int n = await stream.ReadAsync(sizeHeader.AsMemory(read, 4 - read), ct);
            if (n <= 0) return null; // Connection closed
            read += n;
        }

        int size = BinaryPrimitives.ReadInt32BigEndian(sizeHeader);
        if (size <= 0 || size > MaxFrame)
        {
            throw new InvalidOperationException($"Invalid incoming frame size: {size}");
        }

        byte[] payload = new byte[size];
        read = 0;
        while (read < size)
        {
            int n = await stream.ReadAsync(payload.AsMemory(read, size - read), ct);
            if (n <= 0) return null; // Connection closed prematurely
            read += n;
        }

        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.Clone();
    }
}
