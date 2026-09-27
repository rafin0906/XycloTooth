using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using XycloToothBridge.Logging;

namespace XycloToothBridge.Settings;

public class SettingsManager
{
    private static readonly Lazy<SettingsManager> _instance = new(() => new SettingsManager());
    public static SettingsManager Instance => _instance.Value;

    private readonly string _settingsPath;
    private const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "XycloToothBridge";

    public AppSettings Settings { get; private set; }

    private SettingsManager()
    {
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XycloTooth");
        Directory.CreateDirectory(appData);
        _settingsPath = Path.Combine(appData, "settings.json");
        Settings = LoadSettings();
        EnsureDirectoriesExist();
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Failed to load settings file, using defaults", ex);
        }

        var defaultSettings = new AppSettings();
        SaveSettings(defaultSettings);
        return defaultSettings;
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            Settings = settings;
            EnsureDirectoriesExist();
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);

            UpdateStartupRegistry(settings.AutoStart);
            AppLogger.Instance.Info("Application settings saved successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Failed to save settings", ex);
        }
    }

    public void EnsureDirectoriesExist()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Settings.InputDirectory) && !Directory.Exists(Settings.InputDirectory))
            {
                Directory.CreateDirectory(Settings.InputDirectory);
                AppLogger.Instance.Info($"Created input directory: {Settings.InputDirectory}");
            }

            if (!string.IsNullOrWhiteSpace(Settings.OutputDirectory) && !Directory.Exists(Settings.OutputDirectory))
            {
                Directory.CreateDirectory(Settings.OutputDirectory);
                AppLogger.Instance.Info($"Created output directory: {Settings.OutputDirectory}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Failed to ensure directories exist", ex);
        }
    }

    private void UpdateStartupRegistry(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
            if (key == null) return;

            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(exePath)) return;

            if (enable)
            {
                key.SetValue(AppName, $"\"{exePath}\" --minimized");
            }
            else
            {
                if (key.GetValue(AppName) != null)
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Failed to update Windows startup registry key", ex);
        }
    }
}
