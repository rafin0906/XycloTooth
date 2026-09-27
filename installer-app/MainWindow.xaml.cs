using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace XycloToothSetup;

public partial class MainWindow : Window
{
    private readonly string _defaultInstallDir;

    public MainWindow()
    {
        InitializeComponent();
        _defaultInstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "XycloTooth");
        TxtInstallDir.Text = _defaultInstallDir;
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select Installation Directory",
            UseDescriptionForTitle = true,
            SelectedPath = TxtInstallDir.Text
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TxtInstallDir.Text = dialog.SelectedPath;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        if (BtnInstall.Content.ToString() == "Finish")
        {
            if (ChkLaunchApp.IsChecked == true)
            {
                string exePath = Path.Combine(TxtInstallDir.Text, "XycloToothBridge.exe");
                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                }
            }
            Close();
            return;
        }

        BtnInstall.IsEnabled = false;
        BtnBrowse.IsEnabled = false;
        TxtInstallDir.IsEnabled = false;
        BtnCancel.IsEnabled = false;

        string targetDir = TxtInstallDir.Text.Trim();
        bool createDesktop = ChkDesktopShortcut.IsChecked == true;
        bool createStartMenu = ChkStartMenuShortcut.IsChecked == true;
        bool createDirs = ChkCreateDirs.IsChecked == true;

        await Task.Run(() =>
        {
            UpdateProgress(10, "Preparing target directory...");
            Directory.CreateDirectory(targetDir);

            UpdateProgress(30, "Extracting XycloTooth application files...");
            string targetExe = Path.Combine(targetDir, "XycloToothBridge.exe");

            var assembly = Assembly.GetExecutingAssembly();
            // Find embedded resource name
            string? resourceName = null;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith("XycloToothBridge.exe", StringComparison.OrdinalIgnoreCase))
                {
                    resourceName = name;
                    break;
                }
            }

            if (resourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var fileStream = new FileStream(targetExe, FileMode.Create, FileAccess.Write, FileShare.None);
                    stream.CopyTo(fileStream);
                }
            }

            UpdateProgress(60, "Configuring shortcuts and directories...");
            if (createDirs)
            {
                string userDocs = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Directory.CreateDirectory(Path.Combine(userDocs, "XycloTooth", "Input"));
                Directory.CreateDirectory(Path.Combine(userDocs, "XycloTooth", "Output"));
            }

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    if (createDesktop)
                    {
                        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        dynamic shortcut = shell.CreateShortcut(Path.Combine(desktopPath, "XycloTooth File Bridge.lnk"));
                        shortcut.TargetPath = targetExe;
                        shortcut.WorkingDirectory = targetDir;
                        shortcut.Description = "Automated Bluetooth Text File Bridge";
                        shortcut.Save();
                    }

                    if (createStartMenu)
                    {
                        string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "XycloTooth");
                        Directory.CreateDirectory(startMenu);
                        dynamic shortcut = shell.CreateShortcut(Path.Combine(startMenu, "XycloTooth File Bridge.lnk"));
                        shortcut.TargetPath = targetExe;
                        shortcut.WorkingDirectory = targetDir;
                        shortcut.Description = "Automated Bluetooth Text File Bridge";
                        shortcut.Save();
                    }
                }
            }

            UpdateProgress(90, "Creating uninstaller...");
            string uninstallerScript = Path.Combine(targetDir, "uninstall.bat");
            File.WriteAllText(uninstallerScript, "@echo off\r\necho Uninstalling XycloTooth...\r\ntaskkill /F /IM XycloToothBridge.exe >nul 2>&1\r\ndel /Q \"%USERPROFILE%\\Desktop\\XycloTooth File Bridge.lnk\" >nul 2>&1\r\nrmdir /S /Q \"%APPDATA%\\Microsoft\\Windows\\Start Menu\\Programs\\XycloTooth\" >nul 2>&1\r\ncd ..\r\nrmdir /S /Q \"" + targetDir + "\"\r\necho Uninstallation complete.\r\npause\r\n");

            UpdateProgress(100, "Installation complete!");
        });

        TxtStatus.Text = "Installation completed successfully!";
        TxtStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
        BtnInstall.Content = "Finish";
        BtnInstall.IsEnabled = true;
        BtnCancel.Visibility = Visibility.Collapsed;
    }

    private void UpdateProgress(int percent, string message)
    {
        Dispatcher.Invoke(() =>
        {
            PbInstall.Value = percent;
            TxtStatus.Text = message;
        });
    }
}
