using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using XycloToothBridge.Bluetooth;
using XycloToothBridge.Logging;
using XycloToothBridge.Models;

namespace XycloToothBridge.Managers;

public class TransferManager
{
    private static readonly Lazy<TransferManager> _instance = new(() => new TransferManager());
    public static TransferManager Instance => _instance.Value;

    private readonly ConcurrentQueue<string> _sendQueue = new();
    private readonly SemaphoreSlim _queueSignal = new(0);
    private CancellationTokenSource? _workerCts;
    private bool _isRunning;

    public event Action<string, bool>? TransferCompleted;

    private TransferManager()
    {
        FileManager.Instance.FileReadyToSend += EnqueueSend;
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _workerCts = new CancellationTokenSource();
        Task.Run(() => WorkerLoopAsync(_workerCts.Token));
        FileManager.Instance.StartMonitoring();
        BluetoothManager.Instance.StartListener();
        AppLogger.Instance.Info("TransferManager service started.");
    }

    public void Stop()
    {
        _isRunning = false;
        _workerCts?.Cancel();
        FileManager.Instance.StopMonitoring();
        BluetoothManager.Instance.StopListener();
        AppLogger.Instance.Info("TransferManager service stopped.");
    }

    public void EnqueueSend(string filePath)
    {
        _sendQueue.Enqueue(filePath);
        _queueSignal.Release();
        AppLogger.Instance.Info($"Enqueued file for transfer: {System.IO.Path.GetFileName(filePath)}");
    }

    private async Task WorkerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _queueSignal.WaitAsync(ct);
                if (_sendQueue.TryDequeue(out var filePath))
                {
                    bool success = false;
                    int maxRetries = 3;
                    int delayMs = 2000;

                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        AppLogger.Instance.Info($"Transfer attempt {attempt}/{maxRetries} for {System.IO.Path.GetFileName(filePath)}");
                        success = await BluetoothManager.Instance.SendFileToAndroidAsync(filePath, ct);

                        if (success) break;

                        if (attempt < maxRetries)
                        {
                            AppLogger.Instance.Warn($"Transfer failed, retrying in {delayMs / 1000}s...");
                            await Task.Delay(delayMs, ct);
                            delayMs *= 2; // exponential backoff
                        }
                    }

                    TransferCompleted?.Invoke(filePath, success);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error("Unexpected error in transfer queue worker", ex);
                await Task.Delay(2000, ct);
            }
        }
    }
}
