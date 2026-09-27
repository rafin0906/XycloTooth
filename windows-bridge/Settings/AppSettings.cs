using System;
using System.IO;

namespace XycloToothBridge.Settings;

public class AppSettings
{
    public string SelectedDeviceAddress { get; set; } = string.Empty;
    public string SelectedDeviceName { get; set; } = string.Empty;
    public string InputDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public bool AutoStart { get; set; } = false;
    public bool AutoTransfer { get; set; } = true;
    public string LogLevel { get; set; } = "INFO";
    public string ServiceUuid { get; set; } = "4a984249-1319-482d-85f3-2c16313508d7";

    public AppSettings()
    {
        string defaultRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "XycloTooth");
        InputDirectory = Path.Combine(defaultRoot, "Input");
        OutputDirectory = Path.Combine(defaultRoot, "Output");
    }
}
