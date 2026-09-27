using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using XycloToothBridge.Logging;
using XycloToothBridge.Settings;

namespace XycloToothBridge.UI;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Action _showMainWindowAction;
    private readonly Action _exitAppAction;

    public TrayIconManager(Action showMainWindowAction, Action exitAppAction)
    {
        _showMainWindowAction = showMainWindowAction;
        _exitAppAction = exitAppAction;

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "XycloTooth File Bridge",
            Visible = true
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open Dashboard", null, (s, e) => _showMainWindowAction());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Open Input Folder", null, (s, e) => OpenFolder(SettingsManager.Instance.Settings.InputDirectory));
        contextMenu.Items.Add("Open Output Folder", null, (s, e) => OpenFolder(SettingsManager.Instance.Settings.OutputDirectory));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (s, e) => _exitAppAction());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => _showMainWindowAction();
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, icon);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Failed to show balloon tip", ex);
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Failed to open folder: {path}", ex);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
