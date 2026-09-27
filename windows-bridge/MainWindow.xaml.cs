using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using Microsoft.Win32;
using XycloToothBridge.Bluetooth;
using XycloToothBridge.Logging;
using XycloToothBridge.Managers;
using XycloToothBridge.Models;
using XycloToothBridge.Settings;
using XycloToothBridge.UI;

namespace XycloToothBridge;

public partial class MainWindow : Window
{
    private readonly TrayIconManager _trayManager;
    private bool _isServiceRunning;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        _trayManager = new TrayIconManager(
            showMainWindowAction: () =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            },
            exitAppAction: () =>
            {
                _isExiting = true;
                _trayManager.Dispose();
                TransferManager.Instance.Stop();
                System.Windows.Application.Current?.Shutdown();
            }
        );

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;

        // Subscribe to Logger
        AppLogger.Instance.LogReceived += OnLogReceived;

        // Subscribe to Bluetooth and Transfer events
        BluetoothManager.Instance.AdapterStatusChanged += OnAdapterStatusChanged;
        BluetoothManager.Instance.ConnectionStatusChanged += OnConnectionStatusChanged;
        BluetoothManager.Instance.TransferProgress += OnTransferProgress;
        BluetoothManager.Instance.FileReceived += OnFileReceived;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadSettingsToUI();
        RefreshPairedDevices();
        CheckAdapter();

        // Auto-start transfer manager service
        StartService();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExiting && ChkMinimizeToTray.IsChecked == true)
        {
            e.Cancel = true;
            Hide();
            _trayManager.ShowNotification("XycloTooth Running in Background", "File Bridge is active in the system tray.", System.Windows.Forms.ToolTipIcon.Info);
        }
        else
        {
            _trayManager.Dispose();
            TransferManager.Instance.Stop();
        }
    }

    private void LoadSettingsToUI()
    {
        var s = SettingsManager.Instance.Settings;
        TxtInputDir.Text = s.InputDirectory;
        TxtOutputDir.Text = s.OutputDirectory;
        ChkAutoTransfer.IsChecked = s.AutoTransfer;
        ChkAutoStart.IsChecked = s.AutoStart;
    }

    private void CheckAdapter()
    {
        bool available = BluetoothManager.Instance.CheckAdapterStatus();
        UpdateAdapterUI(available);
    }

    private void UpdateAdapterUI(bool available)
    {
        Dispatcher.InvokeAsync(() =>
        {
            IndicatorAdapter.Fill = available ? new SolidColorBrush(MediaColor.FromRgb(16, 185, 129)) : new SolidColorBrush(MediaColor.FromRgb(239, 68, 68));
            TxtAdapterStatus.Text = available ? "Online" : "Offline";
        });
    }

    private void RefreshPairedDevices()
    {
        CmbDevices.Items.Clear();
        var devices = BluetoothManager.Instance.GetPairedDevices();
        string currentSelection = SettingsManager.Instance.Settings.SelectedDeviceAddress;

        int selectedIndex = -1;
        for (int i = 0; i < devices.Count; i++)
        {
            CmbDevices.Items.Add(devices[i]);
            if (!string.IsNullOrEmpty(currentSelection) && devices[i].Address.Equals(currentSelection, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
            }
        }

        if (selectedIndex >= 0)
        {
            CmbDevices.SelectedIndex = selectedIndex;
        }
        else if (devices.Count > 0)
        {
            CmbDevices.SelectedIndex = 0;
        }
    }

    private void StartService()
    {
        TransferManager.Instance.Start();
        _isServiceRunning = true;
        BtnToggleService.Content = "⏹ Stop Service";
        BtnToggleService.Background = new SolidColorBrush(MediaColor.FromRgb(239, 68, 68));
    }

    private void StopService()
    {
        TransferManager.Instance.Stop();
        _isServiceRunning = false;
        BtnToggleService.Content = "▶ Start Service";
        BtnToggleService.Background = new SolidColorBrush(MediaColor.FromRgb(16, 185, 129));
    }

    private void BtnToggleService_Click(object sender, RoutedEventArgs e)
    {
        if (_isServiceRunning)
        {
            StopService();
        }
        else
        {
            StartService();
        }
    }

    private void BtnRefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        RefreshPairedDevices();
    }

    private void BtnBrowseInput_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Input Directory for PC -> Android files",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TxtInputDir.Text = dialog.SelectedPath;
        }
    }

    private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Output Directory for Android -> PC files",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TxtOutputDir.Text = dialog.SelectedPath;
        }
    }

    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsManager.Instance.Settings;
        s.InputDirectory = TxtInputDir.Text;
        s.OutputDirectory = TxtOutputDir.Text;
        s.AutoTransfer = ChkAutoTransfer.IsChecked ?? true;
        s.AutoStart = ChkAutoStart.IsChecked ?? false;

        if (CmbDevices.SelectedItem is DiscoveredDevice dev)
        {
            s.SelectedDeviceAddress = dev.Address;
            s.SelectedDeviceName = dev.Name;
        }

        SettingsManager.Instance.SaveSettings(s);
        FileManager.Instance.StartMonitoring(); // Refresh watcher with new path
        System.Windows.MessageBox.Show("Configuration successfully saved!", "XycloTooth", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSendFile_Click(object sender, RoutedEventArgs e)
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            Title = "Select .txt file to send to Android"
        };

        if (openDialog.ShowDialog() == true)
        {
            TransferManager.Instance.EnqueueSend(openDialog.FileName);
        }
    }

    private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
    {
        TxtLogs.Text = string.Empty;
    }

    private void OnLogReceived(LogEntry entry)
    {
        Dispatcher.InvokeAsync(() =>
        {
            TxtLogs.AppendText(entry.ToString() + Environment.NewLine);
            TxtLogs.ScrollToEnd();
        });
    }

    private void OnAdapterStatusChanged(bool available)
    {
        UpdateAdapterUI(available);
    }

    private void OnConnectionStatusChanged(bool isConnected, string statusText)
    {
        Dispatcher.InvokeAsync(() =>
        {
            IndicatorConnection.Fill = isConnected ? new SolidColorBrush(MediaColor.FromRgb(16, 185, 129)) : new SolidColorBrush(MediaColor.FromRgb(245, 158, 11));
            TxtConnectionStatus.Text = statusText;
        });
    }

    private void OnTransferProgress(TransferRecord record)
    {
        Dispatcher.InvokeAsync(() =>
        {
            TxtCurrentTransfer.Text = $"{record.Filename} ({record.Status} - {record.ProgressPercent}%)";
            PbTransfer.Value = record.ProgressPercent;

            if (record.Status == TransferStatus.Completed)
            {
                if (record.Direction == TransferDirection.WindowsToAndroid)
                {
                    TxtLastSent.Text = $"{record.Filename} ({DateTime.Now:HH:mm:ss})";
                    _trayManager.ShowNotification("File Sent", $"Successfully transferred {record.Filename} to Android.");
                }
                else
                {
                    TxtLastReceived.Text = $"{record.Filename} ({DateTime.Now:HH:mm:ss})";
                }
            }
            else if (record.Status == TransferStatus.Failed)
            {
                TxtCurrentTransfer.Text = $"Failed: {record.ErrorMessage}";
            }
        });
    }

    private void OnFileReceived(string filePath, string filename)
    {
        Dispatcher.InvokeAsync(() =>
        {
            TxtLastReceived.Text = $"{filename} ({DateTime.Now:HH:mm:ss})";
            _trayManager.ShowNotification("File Received", $"Received {filename} from Android device.");
        });
    }
}