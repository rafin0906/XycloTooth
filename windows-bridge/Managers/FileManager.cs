using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using XycloToothBridge.Logging;
using XycloToothBridge.Settings;

namespace XycloToothBridge.Managers;

public class FileManager
{
    private static readonly Lazy<FileManager> _instance = new(() => new FileManager());
    public static FileManager Instance => _instance.Value;

    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, DateTime> _recentlySentFiles = new();
    private readonly ConcurrentDictionary<string, byte> _processingFiles = new();

    public event Action<string>? FileReadyToSend;

    private FileManager()
    {
    }

    public static async Task<string> ComputeSha256Async(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        byte[] hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void StartMonitoring()
    {
        StopMonitoring();

        var settings = SettingsManager.Instance.Settings;
        if (!settings.AutoTransfer || string.IsNullOrWhiteSpace(settings.InputDirectory))
        {
            return;
        }

        try
        {
            if (!Directory.Exists(settings.InputDirectory))
            {
                Directory.CreateDirectory(settings.InputDirectory);
            }

            _watcher = new FileSystemWatcher(settings.InputDirectory)
            {
                Filter = "*.txt",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
            _watcher.Renamed += (s, e) => HandleCandidateFile(e.FullPath);

            AppLogger.Instance.Info($"Input folder monitoring active: {settings.InputDirectory}");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Failed to start directory monitor", ex);
        }
    }

    public void StopMonitoring()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
            AppLogger.Instance.Info("Input folder monitoring stopped.");
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        HandleCandidateFile(e.FullPath);
    }

    private void HandleCandidateFile(string fullPath)
    {
        if (!fullPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return;
        if (fullPath.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) return;

        // Deduplication & concurrency guard
        if (!_processingFiles.TryAdd(fullPath, 0)) return;

        Task.Run(async () =>
        {
            try
            {
                // Wait for file write completion / stability
                bool isStable = await WaitForFileStabilityAsync(fullPath, TimeSpan.FromSeconds(5));
                if (!isStable)
                {
                    AppLogger.Instance.Warn($"File stability timeout: {fullPath}");
                    return;
                }

                // Check recent transfer cache (within 10 seconds)
                string hash = await ComputeSha256Async(fullPath);
                if (_recentlySentFiles.TryGetValue(hash, out var sentTime) && (DateTime.Now - sentTime).TotalSeconds < 10)
                {
                    AppLogger.Instance.Debug($"Skipping recently transferred file with hash {hash}");
                    return;
                }

                _recentlySentFiles[hash] = DateTime.Now;
                AppLogger.Instance.Info($"New stable .txt file detected in input directory: {Path.GetFileName(fullPath)}");
                FileReadyToSend?.Invoke(fullPath);
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error($"Error processing input file: {fullPath}", ex);
            }
            finally
            {
                _processingFiles.TryRemove(fullPath, out _);
            }
        });
    }

    /// <summary>
    /// Verifies that the file size remains constant and file can be opened for reading.
    /// </summary>
    public static async Task<bool> WaitForFileStabilityAsync(string path, TimeSpan timeout)
    {
        var start = DateTime.Now;
        long lastSize = -1;

        while (DateTime.Now - start < timeout)
        {
            try
            {
                if (!File.Exists(path)) return false;

                var fi = new FileInfo(path);
                long currentSize = fi.Length;

                if (currentSize > 0 && currentSize == lastSize)
                {
                    // Attempt to open with exclusive read to verify write lock is released
                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return true;
                }

                lastSize = currentSize;
            }
            catch (IOException)
            {
                // File still locked by writing process
            }

            await Task.Delay(500);
        }

        return false;
    }
}
