using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using XycloToothBridge.Models;
using XycloToothBridge.Protocol;
using XycloToothBridge.Managers;

namespace XycloToothBridge.Tests;

public class ProtocolAndManagerTests
{
    [Fact]
    public async Task Frame_WriteAndRead_Roundtrip_Succeeds()
    {
        using var stream = new MemoryStream();

        string samplePayload = "{\"fileId\":\"test-123\",\"filename\":\"test.txt\",\"fileSize\":1024}";
        byte[] payloadBytes = Encoding.UTF8.GetBytes(samplePayload);

        await ProtocolEngine.WriteFrameAsync(stream, FrameType.FileStart, payloadBytes);

        // Reset stream position to read
        stream.Position = 0;

        var frame = await ProtocolEngine.ReadFrameAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal(FrameType.FileStart, frame.Type);
        Assert.Equal(samplePayload, frame.AsUtf8String());
    }

    [Fact]
    public async Task Frame_TamperedChecksum_IsRejected()
    {
        using var stream = new MemoryStream();

        byte[] payload = Encoding.UTF8.GetBytes("Important Data");
        await ProtocolEngine.WriteFrameAsync(stream, FrameType.FileChunk, payload);

        // Tamper with one byte in the payload
        byte[] rawBuffer = stream.ToArray();
        rawBuffer[ProtocolEngine.HeaderSize + 2] ^= 0xFF; // Flip bits

        using var tamperedStream = new MemoryStream(rawBuffer);
        var frame = await ProtocolEngine.ReadFrameAsync(tamperedStream);

        // Should return null because CRC32 checksum failed
        Assert.Null(frame);
    }

    [Fact]
    public async Task Frame_InvalidMagic_IsRejected()
    {
        using var stream = new MemoryStream();

        byte[] payload = Encoding.UTF8.GetBytes("Data");
        await ProtocolEngine.WriteFrameAsync(stream, FrameType.Ping, payload);

        byte[] rawBuffer = stream.ToArray();
        rawBuffer[0] = 0x00; // Invalidate Magic0

        using var tamperedStream = new MemoryStream(rawBuffer);
        var frame = await ProtocolEngine.ReadFrameAsync(tamperedStream);

        Assert.Null(frame);
    }

    [Fact]
    public async Task Sha256_Calculation_IsDeterministicAndAccurate()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            string content = "Hello XycloTooth Automated Pipeline 2026!";
            await File.WriteAllTextAsync(tempFile, content, Encoding.UTF8);

            string hash1 = await FileManager.ComputeSha256Async(tempFile);
            string hash2 = await FileManager.ComputeSha256Async(tempFile);

            Assert.Equal(hash1, hash2);
            Assert.Equal(64, hash1.Length); // 64 hex characters
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task FileStability_ValidatesCompletelyWrittenFile()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Stable content line 1\nStable content line 2\n");
            bool isStable = await FileManager.WaitForFileStabilityAsync(tempFile, TimeSpan.FromSeconds(2));
            Assert.True(isStable);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
