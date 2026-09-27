using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using XycloToothBridge.Logging;
using XycloToothBridge.Models;
using XycloToothBridge.Protocol;
using XycloToothBridge.Settings;

namespace XycloToothBridge.Bluetooth;

public class DiscoveredDevice
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsPaired { get; set; }

    public override string ToString() => $"{Name} ({Address})";
}

public class BluetoothManager
{
    private static readonly Lazy<BluetoothManager> _instance = new(() => new BluetoothManager());
    public static BluetoothManager Instance => _instance.Value;

    private BluetoothListener? _listener;
    private CancellationTokenSource? _listenerCts;
    private BluetoothClient? _activeClient;
    private readonly object _connectionLock = new();

    public bool IsAdapterAvailable { get; private set; }
    public bool IsListening { get; private set; }
    public bool IsConnected { get; private set; }
    public string ConnectedDeviceName { get; private set; } = "None";

    public event Action<bool>? AdapterStatusChanged;
    public event Action<bool, string>? ConnectionStatusChanged;
    public event Action<TransferRecord>? TransferProgress;
    public event Action<string, string>? FileReceived; // filePath, filename

    private BluetoothManager()
    {
        CheckAdapterStatus();
    }

    public bool CheckAdapterStatus()
    {
        try
        {
            var radio = BluetoothRadio.Default;
            IsAdapterAvailable = radio != null && radio.Mode != RadioMode.PowerOff;
            AdapterStatusChanged?.Invoke(IsAdapterAvailable);
            return IsAdapterAvailable;
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Error checking Bluetooth radio status", ex);
            IsAdapterAvailable = false;
            AdapterStatusChanged?.Invoke(false);
            return false;
        }
    }

    public List<DiscoveredDevice> GetPairedDevices()
    {
        var result = new List<DiscoveredDevice>();
        try
        {
            var client = new BluetoothClient();
            var devices = client.PairedDevices;
            foreach (var dev in devices)
            {
                result.Add(new DiscoveredDevice
                {
                    Name = dev.DeviceName ?? "Unknown Device",
                    Address = dev.DeviceAddress.ToString(),
                    IsPaired = dev.Authenticated
                });
            }
            AppLogger.Instance.Info($"Discovered {result.Count} paired Bluetooth devices.");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Failed to enumerate paired devices", ex);
        }
        return result;
    }

    public void StartListener()
    {
        if (IsListening) return;

        try
        {
            var settings = SettingsManager.Instance.Settings;
            Guid serviceUuid = Guid.Parse(settings.ServiceUuid);

            _listener = new BluetoothListener(serviceUuid);
            _listener.ServiceName = "XycloTooth-FileBridge";
            _listener.Start();

            IsListening = true;
            _listenerCts = new CancellationTokenSource();
            AppLogger.Instance.Info($"Bluetooth RFCOMM server started. Listening on service UUID: {serviceUuid}");
            ConnectionStatusChanged?.Invoke(false, "Listening for connections...");

            Task.Run(() => AcceptLoopAsync(_listenerCts.Token));
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Failed to start Bluetooth listener. Trying SerialPort fallback...", ex);
            try
            {
                // Fallback to standard SerialPort profile GUID
                _listener = new BluetoothListener(BluetoothService.SerialPort);
                _listener.ServiceName = "XycloTooth-FileBridge-SPP";
                _listener.Start();

                IsListening = true;
                _listenerCts = new CancellationTokenSource();
                AppLogger.Instance.Info("Bluetooth RFCOMM server started with standard SerialPortProfile fallback.");
                ConnectionStatusChanged?.Invoke(false, "Listening on SerialPort...");
                Task.Run(() => AcceptLoopAsync(_listenerCts.Token));
            }
            catch (Exception ex2)
            {
                AppLogger.Instance.Error("Fatal: Could not start Bluetooth RFCOMM listener.", ex2);
                IsListening = false;
                ConnectionStatusChanged?.Invoke(false, "Listener Failed: " + ex2.Message);
            }
        }
    }

    public void StopListener()
    {
        try
        {
            _listenerCts?.Cancel();
            _listener?.Stop();
            _listener = null;
            IsListening = false;
            AppLogger.Instance.Info("Bluetooth listener stopped.");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Error stopping Bluetooth listener", ex);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                var client = await _listener.AcceptBluetoothClientAsync();
                AppLogger.Instance.Info($"Accepted Bluetooth connection from: {client.RemoteMachineName}");

                lock (_connectionLock)
                {
                    _activeClient?.Dispose();
                    _activeClient = client;
                    IsConnected = true;
                    ConnectedDeviceName = client.RemoteMachineName ?? "Android Device";
                }

                ConnectionStatusChanged?.Invoke(true, $"Connected to {ConnectedDeviceName}");
                _ = Task.Run(() => HandleClientSessionAsync(client, ct), ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                AppLogger.Instance.Warn($"Accept loop encountered error: {ex.Message}");
                await Task.Delay(1000, ct);
            }
        }
    }

    private async Task HandleClientSessionAsync(BluetoothClient client, CancellationToken ct)
    {
        using var stream = client.GetStream();
        try
        {
            // Initial Handshake: Send HelloAck if client sends Hello
            while (!ct.IsCancellationRequested && client.Connected)
            {
                var frame = await ProtocolEngine.ReadFrameAsync(stream, ct);
                if (frame == null) break; // Client disconnected

                switch (frame.Type)
                {
                    case FrameType.Hello:
                        AppLogger.Instance.Info($"Received HELLO from remote client: {frame.AsUtf8String()}");
                        await ProtocolEngine.WriteJsonFrameAsync(stream, FrameType.HelloAck, new HelloMetadata(), ct);
                        break;

                    case FrameType.FileStart:
                        await HandleInboundFileTransferAsync(stream, frame, ct);
                        break;

                    case FrameType.Ping:
                        await ProtocolEngine.WriteFrameAsync(stream, FrameType.Pong, Array.Empty<byte>(), ct);
                        break;

                    case FrameType.Pong:
                        AppLogger.Instance.Debug("Received PONG");
                        break;

                    default:
                        AppLogger.Instance.Warn($"Unhandled frame type: {frame.Type}");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Error in client session", ex);
        }
        finally
        {
            lock (_connectionLock)
            {
                if (_activeClient == client)
                {
                    _activeClient = null;
                    IsConnected = false;
                    ConnectedDeviceName = "None";
                }
            }
            ConnectionStatusChanged?.Invoke(false, IsListening ? "Listening for connections..." : "Disconnected");
            AppLogger.Instance.Info("Bluetooth client session closed.");
        }
    }

    private async Task HandleInboundFileTransferAsync(NetworkStream stream, ProtocolFrame startFrame, CancellationToken ct)
    {
        var meta = startFrame.AsJson<FileStartMetadata>();
        if (meta == null)
        {
            AppLogger.Instance.Error("Invalid FileStartMetadata payload");
            return;
        }

        AppLogger.Instance.Info($"Incoming file: '{meta.Filename}' ({meta.FileSize} bytes, chunks: {meta.TotalChunks}, sha256: {meta.Sha256})");

        string outputDir = SettingsManager.Instance.Settings.OutputDirectory;
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        string finalPath = Path.Combine(outputDir, meta.Filename);
        string partPath = Path.Combine(outputDir, $"{meta.Filename}.{meta.FileId}.part");

        var record = new TransferRecord
        {
            Id = meta.FileId,
            Filename = meta.Filename,
            FilePath = finalPath,
            FileSize = meta.FileSize,
            Sha256 = meta.Sha256,
            Direction = TransferDirection.AndroidToWindows,
            Status = TransferStatus.Transferring
        };
        TransferProgress?.Invoke(record);

        // Send ready ACK to start receiving chunks
        await ProtocolEngine.WriteJsonFrameAsync(stream, FrameType.FileAck, new FileAckMetadata
        {
            FileId = meta.FileId,
            Status = "READY",
            Message = "Ready for chunks"
        }, ct);

        using (var fs = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            long bytesReceived = 0;
            while (bytesReceived < meta.FileSize)
            {
                var chunkFrame = await ProtocolEngine.ReadFrameAsync(stream, ct);
                if (chunkFrame == null || chunkFrame.Type != FrameType.FileChunk)
                {
                    AppLogger.Instance.Error("Broken chunk stream during file receive.");
                    record.Status = TransferStatus.Failed;
                    record.ErrorMessage = "Connection lost during chunk reception.";
                    TransferProgress?.Invoke(record);
                    return;
                }

                // Chunk format: 4 bytes index + payload
                if (chunkFrame.Payload.Length < 4) continue;
                byte[] chunkData = chunkFrame.Payload.AsSpan(4).ToArray();

                await fs.WriteAsync(chunkData.AsMemory(), ct);
                bytesReceived += chunkData.Length;

                record.ProgressPercent = (int)((bytesReceived * 100) / Math.Max(1, meta.FileSize));
                TransferProgress?.Invoke(record);
            }

            await fs.FlushAsync(ct);
        }

        // Wait for FileEnd frame
        var endFrame = await ProtocolEngine.ReadFrameAsync(stream, ct);
        if (endFrame == null || endFrame.Type != FrameType.FileEnd)
        {
            AppLogger.Instance.Error("Missing FileEnd frame from sender");
            return;
        }

        // Verify SHA-256
        record.Status = TransferStatus.Verifying;
        TransferProgress?.Invoke(record);

        string computedHash = await Managers.FileManager.ComputeSha256Async(partPath);
        if (!string.Equals(computedHash, meta.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            AppLogger.Instance.Error($"Hash mismatch on received file! Expected: {meta.Sha256}, Got: {computedHash}");
            File.Delete(partPath);

            await ProtocolEngine.WriteJsonFrameAsync(stream, FrameType.FileAck, new FileAckMetadata
            {
                FileId = meta.FileId,
                Status = "HASH_MISMATCH",
                ReceivedSha256 = computedHash,
                Message = "File corrupted in transit."
            }, ct);

            record.Status = TransferStatus.Failed;
            record.ErrorMessage = "SHA-256 hash mismatch.";
            TransferProgress?.Invoke(record);
            return;
        }

        // Atomic move from .part to final destination
        if (File.Exists(finalPath)) File.Delete(finalPath);
        File.Move(partPath, finalPath);

        AppLogger.Instance.Info($"File successfully received, verified, and saved to: {finalPath}");

        // Send positive ACK
        await ProtocolEngine.WriteJsonFrameAsync(stream, FrameType.FileAck, new FileAckMetadata
        {
            FileId = meta.FileId,
            Status = "OK",
            ReceivedSha256 = computedHash,
            Message = "File received and verified successfully"
        }, ct);

        record.Status = TransferStatus.Completed;
        record.ProgressPercent = 100;
        record.CompletedAt = DateTime.Now;
        TransferProgress?.Invoke(record);

        FileReceived?.Invoke(finalPath, meta.Filename);
    }

    /// <summary>
    /// Sends a file to the configured Android device over Bluetooth.
    /// Connects as client if not currently connected.
    /// </summary>
    public async Task<bool> SendFileToAndroidAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            AppLogger.Instance.Error($"Send file failed: File does not exist at {filePath}");
            return false;
        }

        var settings = SettingsManager.Instance.Settings;
        if (string.IsNullOrWhiteSpace(settings.SelectedDeviceAddress))
        {
            AppLogger.Instance.Error("Send file failed: No Android device selected in settings.");
            return false;
        }

        string filename = Path.GetFileName(filePath);
        long fileSize = new FileInfo(filePath).Length;
        string sha256 = await Managers.FileManager.ComputeSha256Async(filePath);
        string fileId = Guid.NewGuid().ToString();

        var record = new TransferRecord
        {
            Id = fileId,
            Filename = filename,
            FilePath = filePath,
            FileSize = fileSize,
            Sha256 = sha256,
            Direction = TransferDirection.WindowsToAndroid,
            Status = TransferStatus.Connecting
        };
        TransferProgress?.Invoke(record);

        BluetoothClient? client = null;
        bool shouldDisposeClient = false;

        try
        {
            NetworkStream stream;
            lock (_connectionLock)
            {
                if (_activeClient != null && _activeClient.Connected)
                {
                    client = _activeClient;
                    stream = client.GetStream();
                }
            }

            if (client == null)
            {
                AppLogger.Instance.Info($"Connecting to target Android device ({settings.SelectedDeviceAddress})...");
                client = new BluetoothClient();
                shouldDisposeClient = true;

                var addr = BluetoothAddress.Parse(settings.SelectedDeviceAddress);
                Guid serviceUuid = Guid.Parse(settings.ServiceUuid);

                try
                {
                    await client.ConnectAsync(addr, serviceUuid);
                }
                catch
                {
                    AppLogger.Instance.Warn("Connection with custom UUID failed, attempting SerialPort fallback...");
                    client.Dispose();
                    client = new BluetoothClient();
                    await client.ConnectAsync(addr, BluetoothService.SerialPort);
                }

                stream = client.GetStream();
                AppLogger.Instance.Info("Connected to Android RFCOMM service.");

                // Send HELLO
                await ProtocolEngine.WriteJsonFrameAsync(stream, FrameType.Hello, new HelloMetadata(), ct);
                var helloAck = await ProtocolEngine.ReadFrameAsync(stream, ct);
                if (helloAck == null || helloAck.Type != FrameType.HelloAck)
                {
                    AppLogger.Instance.Warn("Handshake with Android device failed.");
                }
            }
            else
            {
                stream = client.GetStream();
            }

            record.Status = TransferStatus.Transferring;
            TransferProgress?.Invoke(record);

            // Send FILE_START
            int chunkSize = 4096;
            int totalChunks = (int)Math.Ceiling((double)fileSize / chunkSize);

            var meta = new FileStartMetadata
            {
                FileId = fileId,
                Filename = filename,
                FileSize = fileSize,
                ChunkSize = chunkSize,
                TotalChunks = totalChunks,
                Sha256 = sha256
            };

            await ProtocolEngine.WriteJsonFrameAsync(stream, FrameType.FileStart, meta, ct);

            // Await READY ACK
            var readyAck = await ProtocolEngine.ReadFrameAsync(stream, ct);
            if (readyAck == null || readyAck.Type != FrameType.FileAck)
            {
                throw new InvalidOperationException("Did not receive READY ACK from Android.");
            }

            // Stream Chunks
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] buffer = new byte[chunkSize];
                int chunkIndex = 0;
                long totalSent = 0;

                while (totalSent < fileSize)
                {
                    int read = await fs.ReadAsync(buffer.AsMemory(0, chunkSize), ct);
                    if (read <= 0) break;

                    // Payload: 4 bytes index + chunk bytes
                    byte[] payload = new byte[4 + read];
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), chunkIndex);
                    Buffer.BlockCopy(buffer, 0, payload, 4, read);

                    await ProtocolEngine.WriteFrameAsync(stream, FrameType.FileChunk, payload, ct);
                    totalSent += read;
                    chunkIndex++;

                    record.ProgressPercent = (int)((totalSent * 100) / Math.Max(1, fileSize));
                    TransferProgress?.Invoke(record);
                }
            }

            // Send FILE_END
            await ProtocolEngine.WriteJsonFrameAsync(stream, FrameType.FileEnd, new FileEndMetadata
            {
                FileId = fileId,
                TotalBytesSent = fileSize,
                Sha256 = sha256
            }, ct);

            record.Status = TransferStatus.Verifying;
            TransferProgress?.Invoke(record);

            // Await FINAL ACK
            var finalAckFrame = await ProtocolEngine.ReadFrameAsync(stream, ct);
            if (finalAckFrame == null || finalAckFrame.Type != FrameType.FileAck)
            {
                throw new InvalidOperationException("Did not receive final FILE_ACK from Android.");
            }

            var finalAck = finalAckFrame.AsJson<FileAckMetadata>();
            if (finalAck == null || finalAck.Status != "OK")
            {
                throw new InvalidOperationException($"Android rejected file: {finalAck?.Message ?? "Unknown error"}");
            }

            AppLogger.Instance.Info($"File '{filename}' successfully sent to Android and confirmed!");
            record.Status = TransferStatus.Completed;
            record.ProgressPercent = 100;
            record.CompletedAt = DateTime.Now;
            TransferProgress?.Invoke(record);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Send file failed for {filename}", ex);
            record.Status = TransferStatus.Failed;
            record.ErrorMessage = ex.Message;
            TransferProgress?.Invoke(record);
            return false;
        }
        finally
        {
            if (shouldDisposeClient)
            {
                client?.Dispose();
            }
        }
    }
}
