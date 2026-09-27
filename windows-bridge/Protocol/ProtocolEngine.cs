using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XycloToothBridge.Logging;
using XycloToothBridge.Models;

namespace XycloToothBridge.Protocol;

public class ProtocolFrame
{
    public FrameType Type { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public string AsUtf8String() => Encoding.UTF8.GetString(Payload);

    public T? AsJson<T>()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(Payload);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Failed to deserialize frame payload to {typeof(T).Name}", ex);
            return default;
        }
    }
}

public class ProtocolEngine
{
    public const byte Magic0 = 0x58; // 'X'
    public const byte Magic1 = 0x54; // 'T'
    public const byte ProtocolVersion = 0x01;
    public const int HeaderSize = 8; // Magic(2) + Version(1) + Type(1) + Length(4)
    public const int ChecksumSize = 4; // CRC32(4)

    /// <summary>
    /// Reads a complete validated frame from the stream asynchronously.
    /// </summary>
    public static async Task<ProtocolFrame?> ReadFrameAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[HeaderSize];
        int bytesRead = 0;

        // Read header
        while (bytesRead < HeaderSize)
        {
            int n = await stream.ReadAsync(header.AsMemory(bytesRead, HeaderSize - bytesRead), ct);
            if (n <= 0) return null; // Stream closed
            bytesRead += n;
        }

        // Validate magic & version
        if (header[0] != Magic0 || header[1] != Magic1)
        {
            AppLogger.Instance.Warn($"Protocol framing error: Invalid magic bytes [0x{header[0]:X2}, 0x{header[1]:X2}]");
            return null;
        }

        if (header[2] != ProtocolVersion)
        {
            AppLogger.Instance.Warn($"Protocol framing error: Unsupported version {header[2]}");
            return null;
        }

        FrameType frameType = (FrameType)header[3];
        uint payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4));

        if (payloadLength > 10 * 1024 * 1024) // 10MB sanity safety limit per frame
        {
            AppLogger.Instance.Error($"Payload length {payloadLength} exceeds maximum allowed frame limit.");
            return null;
        }

        // Read payload
        byte[] payload = new byte[payloadLength];
        int payloadRead = 0;
        while (payloadRead < payloadLength)
        {
            int n = await stream.ReadAsync(payload.AsMemory(payloadRead, (int)payloadLength - payloadRead), ct);
            if (n <= 0) return null;
            payloadRead += n;
        }

        // Read CRC32
        byte[] checksumBytes = new byte[ChecksumSize];
        int crcRead = 0;
        while (crcRead < ChecksumSize)
        {
            int n = await stream.ReadAsync(checksumBytes.AsMemory(crcRead, ChecksumSize - crcRead), ct);
            if (n <= 0) return null;
            crcRead += n;
        }

        uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(checksumBytes);

        // Verify CRC-32 over header + payload
        byte[] fullFrame = new byte[HeaderSize + payloadLength];
        Buffer.BlockCopy(header, 0, fullFrame, 0, HeaderSize);
        Buffer.BlockCopy(payload, 0, fullFrame, HeaderSize, (int)payloadLength);

        uint actualCrc = Crc32.Compute(fullFrame);
        if (actualCrc != expectedCrc)
        {
            AppLogger.Instance.Error($"CRC32 Checksum mismatch! Expected: 0x{expectedCrc:X8}, Computed: 0x{actualCrc:X8}");
            return null;
        }

        return new ProtocolFrame
        {
            Type = frameType,
            Payload = payload
        };
    }

    /// <summary>
    /// Writes a complete frame with header and CRC32 checksum to the stream.
    /// </summary>
    public static async Task WriteFrameAsync(Stream stream, FrameType type, byte[] payload, CancellationToken ct = default)
    {
        uint payloadLength = (uint)payload.Length;
        byte[] frameData = new byte[HeaderSize + payloadLength + ChecksumSize];

        // Header
        frameData[0] = Magic0;
        frameData[1] = Magic1;
        frameData[2] = ProtocolVersion;
        frameData[3] = (byte)type;
        BinaryPrimitives.WriteUInt32BigEndian(frameData.AsSpan(4, 4), payloadLength);

        // Payload
        if (payloadLength > 0)
        {
            Buffer.BlockCopy(payload, 0, frameData, HeaderSize, (int)payloadLength);
        }

        // Calculate CRC32 over Header + Payload
        uint crc = Crc32.Compute(frameData.AsSpan(0, HeaderSize + (int)payloadLength));
        BinaryPrimitives.WriteUInt32BigEndian(frameData.AsSpan(HeaderSize + (int)payloadLength, ChecksumSize), crc);

        // Send all bytes
        await stream.WriteAsync(frameData.AsMemory(), ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>
    /// Helper to send JSON payload
    /// </summary>
    public static Task WriteJsonFrameAsync<T>(Stream stream, FrameType type, T data, CancellationToken ct = default)
    {
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(data);
        return WriteFrameAsync(stream, type, jsonBytes, ct);
    }
}
