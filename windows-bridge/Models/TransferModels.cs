using System;
using System.Text.Json.Serialization;

namespace XycloToothBridge.Models;

public enum FrameType : byte
{
    Hello = 0x01,
    HelloAck = 0x02,
    FileStart = 0x10,
    FileChunk = 0x11,
    FileEnd = 0x12,
    FileAck = 0x13,
    Ping = 0x20,
    Pong = 0x21,
    Error = 0xFF
}

public enum TransferStatus
{
    Idle,
    Connecting,
    Transferring,
    Verifying,
    Completed,
    Failed
}

public enum TransferDirection
{
    WindowsToAndroid,
    AndroidToWindows
}

public class FileStartMetadata
{
    [JsonPropertyName("fileId")]
    public string FileId { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("chunkSize")]
    public int ChunkSize { get; set; } = 4096;

    [JsonPropertyName("totalChunks")]
    public int TotalChunks { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

public class FileEndMetadata
{
    [JsonPropertyName("fileId")]
    public string FileId { get; set; } = string.Empty;

    [JsonPropertyName("totalBytesSent")]
    public long TotalBytesSent { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

public class FileAckMetadata
{
    [JsonPropertyName("fileId")]
    public string FileId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "OK"; // "OK", "HASH_MISMATCH", "ERROR"

    [JsonPropertyName("receivedSha256")]
    public string? ReceivedSha256 { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class HelloMetadata
{
    [JsonPropertyName("clientName")]
    public string ClientName { get; set; } = "XycloTooth-WindowsBridge";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = Environment.MachineName;

    [JsonPropertyName("maxChunkSize")]
    public int MaxChunkSize { get; set; } = 4096;
}

public class TransferRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Filename { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public TransferDirection Direction { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Idle;
    public int ProgressPercent { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
}
