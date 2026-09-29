using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using Microsoft.Win32;
using XycloToothBridge.Bluetooth;
using XycloToothBridge.Logging;
using XycloToothBridge.Managers;
using XycloToothBridge.Models;
using XycloToothBridge.Settings;
using XycloToothBridge.UI;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCursors = System.Windows.Input.Cursors;

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
        EnsureLabFinalDirectory();
        LoadSettingsToUI();
        RefreshPairedDevices();
        CheckAdapter();

        // Auto-start transfer manager service
        StartService();
    }

    private void EnsureLabFinalDirectory()
    {
        try
        {
            string labDir = @"C:\labfinal";
            if (!Directory.Exists(labDir))
            {
                Directory.CreateDirectory(labDir);
                AppLogger.Instance.Info($"Ensured primary responses directory exists at: {labDir}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn($"Could not create C:\\labfinal directory: {ex.Message}");
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExiting && ChkMinimizeToTray.IsChecked == true)
        {
            e.Cancel = true;
            Hide();
            _trayManager.ShowNotification("XycloTooth Running in Background", "File & Chat Bridge is active in the system tray.", System.Windows.Forms.ToolTipIcon.Info);
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
        TxtOutputDir.Text = @"C:\labfinal";
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
            IndicatorAdapter.Fill = available 
                ? new SolidColorBrush(MediaColor.FromRgb(16, 185, 129)) 
                : new SolidColorBrush(MediaColor.FromRgb(239, 68, 68));
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
            Description = "Select Staging Directory for Prompts",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TxtInputDir.Text = dialog.SelectedPath;
        }
    }

    private void BtnOpenLabFinal_Click(object sender, RoutedEventArgs e)
    {
        EnsureLabFinalDirectory();
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = @"C:\labfinal",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not open folder: {ex.Message}", "XycloTooth", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsManager.Instance.Settings;
        s.InputDirectory = TxtInputDir.Text;
        s.OutputDirectory = @"C:\labfinal";
        s.AutoTransfer = ChkAutoTransfer.IsChecked ?? true;
        s.AutoStart = ChkAutoStart.IsChecked ?? false;

        if (CmbDevices.SelectedItem is DiscoveredDevice dev)
        {
            s.SelectedDeviceAddress = dev.Address;
            s.SelectedDeviceName = dev.Name;
        }

        SettingsManager.Instance.SaveSettings(s);
        FileManager.Instance.StartMonitoring();
        System.Windows.MessageBox.Show("Configuration successfully saved! Output is configured to C:\\labfinal.", "XycloTooth", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CmbDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbDevices.SelectedItem is DiscoveredDevice dev)
        {
            var s = SettingsManager.Instance.Settings;
            if (!string.Equals(s.SelectedDeviceAddress, dev.Address, StringComparison.OrdinalIgnoreCase))
            {
                s.SelectedDeviceAddress = dev.Address;
                s.SelectedDeviceName = dev.Name;
                SettingsManager.Instance.SaveSettings(s);
                AppLogger.Instance.Info($"Target Android device switched to: {dev.Name} ({dev.Address})");
            }
        }
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

    private void RadioTab_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelChat == null || PanelLogs == null) return;

        if (RadioTabChat?.IsChecked == true)
        {
            PanelChat.Visibility = Visibility.Visible;
            PanelLogs.Visibility = Visibility.Collapsed;
            BtnClearChat.Visibility = Visibility.Visible;
            BtnClearLogs.Visibility = Visibility.Collapsed;
        }
        else if (RadioTabLogs?.IsChecked == true)
        {
            PanelChat.Visibility = Visibility.Collapsed;
            PanelLogs.Visibility = Visibility.Visible;
            BtnClearChat.Visibility = Visibility.Collapsed;
            BtnClearLogs.Visibility = Visibility.Visible;
        }
    }

    private void BtnClearChat_Click(object sender, RoutedEventArgs e)
    {
        ChatMessagesList.Children.Clear();
        ChatEmptyState.Visibility = Visibility.Visible;
        ChatMessagesList.Children.Add(ChatEmptyState);
    }

    private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
    {
        TxtLogs.Text = string.Empty;
    }

    private void TxtChatPrompt_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Enter without Shift sends prompt. Shift+Enter creates a new line.
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            SendPrompt();
        }
    }

    private void BtnSendPrompt_Click(object sender, RoutedEventArgs e)
    {
        SendPrompt();
    }

    private void SendPrompt()
    {
        string promptText = TxtChatPrompt.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(promptText))
        {
            return;
        }

        // Validate Bluetooth status
        if (!BluetoothManager.Instance.IsAdapterAvailable)
        {
            System.Windows.MessageBox.Show("Bluetooth adapter is offline. Please turn on Bluetooth.", "Bluetooth Offline", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Check selected device - always sync with current dropdown selection
        var settings = SettingsManager.Instance.Settings;
        if (CmbDevices.SelectedItem is DiscoveredDevice dev)
        {
            settings.SelectedDeviceAddress = dev.Address;
            settings.SelectedDeviceName = dev.Name;
            SettingsManager.Instance.SaveSettings(settings);
        }
        else if (string.IsNullOrWhiteSpace(settings.SelectedDeviceAddress))
        {
            System.Windows.MessageBox.Show("Please select your paired Android device from the configuration dropdown.", "No Device Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Save prompt text to a staging file
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filename = $"prompt_{timestamp}.txt";
        string inputDir = settings.InputDirectory;
        if (string.IsNullOrWhiteSpace(inputDir) || !Directory.Exists(inputDir))
        {
            inputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "XycloTooth", "Input");
            Directory.CreateDirectory(inputDir);
        }
        string filePath = Path.Combine(inputDir, filename);

        try
        {
            File.WriteAllText(filePath, promptText, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to create prompt file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Add User Bubble to Chat
        ChatEmptyState.Visibility = Visibility.Collapsed;
        var (bubble, statusLabel) = CreateUserMessageBubble(promptText, DateTime.Now);
        ChatMessagesList.Children.Add(bubble);
        ChatScrollViewer.ScrollToEnd();

        // Clear input box
        TxtChatPrompt.Text = string.Empty;

        // Monitor transfer progress via TransferManager
        void OnPromptTransferCompleted(string completedPath, bool success)
        {
            if (string.Equals(completedPath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                TransferManager.Instance.TransferCompleted -= OnPromptTransferCompleted;
                Dispatcher.Invoke(() =>
                {
                    if (success)
                    {
                        statusLabel.Text = "✅ Sent to Android | Processing AI response...";
                        statusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(52, 211, 153));
                        TxtLastSent.Text = $"{filename} ({DateTime.Now:HH:mm:ss})";
                    }
                    else
                    {
                        statusLabel.Text = "❌ Send failed. Check Android Bluetooth connection.";
                        statusLabel.Foreground = new SolidColorBrush(MediaColor.FromRgb(239, 68, 68));
                    }
                });
            }
        }
        TransferManager.Instance.TransferCompleted += OnPromptTransferCompleted;
    }

    private (Border Bubble, TextBlock StatusText) CreateUserMessageBubble(string text, DateTime time)
    {
        var container = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromRgb(37, 99, 235)), // #2563EB
            CornerRadius = new CornerRadius(12, 12, 2, 12),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(60, 4, 4, 8),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            MaxWidth = 650
        };

        var stack = new StackPanel();

        // Header: You + Time
        var header = new TextBlock
        {
            Text = $"You  •  {time:HH:mm:ss}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(191, 219, 254)), // #BFDBFE
            Margin = new Thickness(0, 0, 0, 4)
        };
        stack.Children.Add(header);

        // Prompt text
        var body = new TextBlock
        {
            Text = text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = WpfBrushes.White,
            LineHeight = 18
        };
        stack.Children.Add(body);

        // Status text
        var status = new TextBlock
        {
            Text = "📤 Sending to Android over Bluetooth...",
            FontSize = 10,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(219, 234, 254)),
            Margin = new Thickness(0, 6, 0, 0)
        };
        stack.Children.Add(status);

        container.Child = stack;
        return (container, status);
    }

    private Border CreateAiMessageBubble(string text, string filePath, string filename, DateTime time)
    {
        var container = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromRgb(30, 41, 59)), // #1E293B
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(16, 185, 129)), // Emerald accent
            BorderThickness = new Thickness(2, 0, 0, 0),
            CornerRadius = new CornerRadius(2, 12, 12, 12),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(4, 4, 60, 8),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            MaxWidth = 680
        };

        var stack = new StackPanel();

        // Header: AI Assistant + Time
        var header = new TextBlock
        {
            Text = $"🤖 AI Assistant (LLM)  •  {time:HH:mm:ss}",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(52, 211, 153)), // #34D399
            Margin = new Thickness(0, 0, 0, 6)
        };
        stack.Children.Add(header);

        // Response Body
        var body = new WpfTextBox
        {
            Text = text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(241, 245, 249)),
            Background = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            IsReadOnly = true,
            Padding = new Thickness(0)
        };
        stack.Children.Add(body);

        // Footer / Saved file badge & Action buttons
        var footer = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromRgb(15, 23, 42)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 10, 0, 0)
        };

        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var saveInfo = new TextBlock
        {
            Text = $"💾 Saved to {filePath}",
            FontSize = 11,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(saveInfo, 0);
        footerGrid.Children.Add(saveInfo);

        var actionsStack = new StackPanel { Orientation = WpfOrientation.Horizontal };

        // Copy button
        var btnCopy = new WpfButton
        {
            Content = "📋 Copy",
            Background = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85)),
            Foreground = WpfBrushes.White,
            FontSize = 10,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = WpfCursors.Hand
        };
        btnCopy.Click += (s, e) =>
        {
            System.Windows.Clipboard.SetText(text);
            btnCopy.Content = "✓ Copied!";
        };
        actionsStack.Children.Add(btnCopy);

        // Open file button
        var btnOpen = new WpfButton
        {
            Content = "📂 Open File",
            Background = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85)),
            Foreground = WpfBrushes.White,
            FontSize = 10,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = WpfCursors.Hand
        };
        btnOpen.Click += (s, e) =>
        {
            try
            {
                if (File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = @"C:\labfinal", UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not open file: {ex.Message}", "XycloTooth", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        actionsStack.Children.Add(btnOpen);

        Grid.SetColumn(actionsStack, 1);
        footerGrid.Children.Add(actionsStack);

        footer.Child = footerGrid;
        stack.Children.Add(footer);

        container.Child = stack;
        return container;
    }

    private void OnFileReceived(string filePath, string filename)
    {
        EnsureLabFinalDirectory();

        // Ensure the file is saved in C:\labfinal
        string labFinalPath = Path.Combine(@"C:\labfinal", filename);
        try
        {
            if (!filePath.Equals(labFinalPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(filePath, labFinalPath, true);
                filePath = labFinalPath;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn($"Could not sync to C:\\labfinal: {ex.Message}");
        }

        // Read response text
        string responseContent;
        try
        {
            responseContent = File.ReadAllText(filePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            responseContent = $"[File received at {filePath}, but error reading content: {ex.Message}]";
        }

        Dispatcher.InvokeAsync(() =>
        {
            TxtLastReceived.Text = $"{filename} ({DateTime.Now:HH:mm:ss})";

            // Add to Chat View
            ChatEmptyState.Visibility = Visibility.Collapsed;
            var aiBubble = CreateAiMessageBubble(responseContent, filePath, filename, DateTime.Now);
            ChatMessagesList.Children.Add(aiBubble);
            ChatScrollViewer.ScrollToEnd();

            // Switch to chat tab if on logs
            if (RadioTabLogs.IsChecked == true)
            {
                RadioTabChat.IsChecked = true;
            }

            _trayManager.ShowNotification("AI Response Received", $"New AI response saved to C:\\labfinal\\{filename}");
        });
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
            IndicatorConnection.Fill = isConnected 
                ? new SolidColorBrush(MediaColor.FromRgb(16, 185, 129)) 
                : new SolidColorBrush(MediaColor.FromRgb(245, 158, 11));
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
                    _trayManager.ShowNotification("Prompt Sent", $"Prompt {record.Filename} sent to Android.");
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
}