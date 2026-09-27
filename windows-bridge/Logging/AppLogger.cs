using System;
using System.Collections.Concurrent;
using System.IO;

namespace XycloToothBridge.Logging;

public enum LogLevel
{
    DEBUG,
    INFO,
    WARNING,
    ERROR
}

public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;

    public string FormattedTimestamp => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");

    public override string ToString() => $"[{FormattedTimestamp}] [{Level}] {Message}";
}

public class AppLogger
{
    private static readonly Lazy<AppLogger> _instance = new(() => new AppLogger());
    public static AppLogger Instance => _instance.Value;

    private readonly string _logFilePath;
    private readonly object _lock = new();

    public event Action<LogEntry>? LogReceived;
    public ConcurrentQueue<LogEntry> RecentLogs { get; } = new();
    private const int MaxRecentLogs = 200;

    private AppLogger()
    {
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XycloTooth", "logs");
        Directory.CreateDirectory(appData);
        _logFilePath = Path.Combine(appData, "bridge.log");
    }

    public void Log(LogLevel level, string message)
    {
        var entry = new LogEntry
        {
            Level = level,
            Message = message,
            Timestamp = DateTime.Now
        };

        RecentLogs.Enqueue(entry);
        while (RecentLogs.Count > MaxRecentLogs)
        {
            RecentLogs.TryDequeue(out _);
        }

        try
        {
            lock (_lock)
            {
                File.AppendAllText(_logFilePath, entry.ToString() + Environment.NewLine);
            }
        }
        catch
        {
            // Silently ignore disk logging errors to keep the application responsive
        }

        LogReceived?.Invoke(entry);
    }

    public void Debug(string message) => Log(LogLevel.DEBUG, message);
    public void Info(string message) => Log(LogLevel.INFO, message);
    public void Warn(string message) => Log(LogLevel.WARNING, message);
    public void Error(string message, Exception? ex = null)
    {
        string msg = ex != null ? $"{message} - Exception: {ex.Message}" : message;
        Log(LogLevel.ERROR, msg);
    }
}
