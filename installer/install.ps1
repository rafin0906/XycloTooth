<#
.SYNOPSIS
    One-Click Installer for XycloTooth Windows File Bridge
.DESCRIPTION
    Installs XycloTooth standalone executable into user's local app directory,
    creates Start Menu & Desktop shortcuts, and sets up input/output directories.
#>

$ErrorActionPreference = "Stop"

$AppName = "XycloTooth File Bridge"
$InstallDir = "$env:LOCALAPPDATA\Programs\XycloTooth"
$SourceExe = Join-Path $PSScriptRoot "..\windows-bridge\dist\XycloToothBridge.exe"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Installing $AppName...  " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

if (-not (Test-Path $SourceExe)) {
    Write-Error "Source executable not found at $SourceExe. Please build the project first."
}

# 1. Create installation directory
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Write-Host "[1/5] Target directory prepared: $InstallDir" -ForegroundColor Green

# 2. Copy Executable
$DestExe = Join-Path $InstallDir "XycloToothBridge.exe"
Copy-Item -Path $SourceExe -Destination $DestExe -Force
Write-Host "[2/5] Copied standalone executable." -ForegroundColor Green

# 3. Create default Input and Output folders
$UserDocs = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$InputDir = Join-Path $UserDocs "XycloTooth\Input"
$OutputDir = Join-Path $UserDocs "XycloTooth\Output"
New-Item -ItemType Directory -Force -Path $InputDir | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Write-Host "[3/5] Input/Output folders created in $UserDocs\XycloTooth." -ForegroundColor Green

# 4. Create Desktop & Start Menu Shortcuts
$WshShell = New-Object -ComObject WScript.Shell

$DesktopPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$DesktopShortcut = $WshShell.CreateShortcut((Join-Path $DesktopPath "$AppName.lnk"))
$DesktopShortcut.TargetPath = $DestExe
$DesktopShortcut.WorkingDirectory = $InstallDir
$DesktopShortcut.Description = "Automated Bluetooth Text File Bridge"
$DesktopShortcut.Save()

$StartMenuPrograms = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$AppStartMenuDir = Join-Path $StartMenuPrograms "XycloTooth"
New-Item -ItemType Directory -Force -Path $AppStartMenuDir | Out-Null

$StartShortcut = $WshShell.CreateShortcut((Join-Path $AppStartMenuDir "$AppName.lnk"))
$StartShortcut.TargetPath = $DestExe
$StartShortcut.WorkingDirectory = $InstallDir
$StartShortcut.Description = "Automated Bluetooth Text File Bridge"
$StartShortcut.Save()
Write-Host "[4/5] Start Menu & Desktop shortcuts created." -ForegroundColor Green

# 5. Create Uninstaller script
$UninstallScript = @"
`$ErrorActionPreference = 'SilentlyContinue'
Remove-Item -Recurse -Force '$InstallDir'
Remove-Item -Force '$DesktopPath\$AppName.lnk'
Remove-Item -Recurse -Force '$AppStartMenuDir'
Write-Host 'XycloTooth successfully uninstalled.' -ForegroundColor Green
"@
Set-Content -Path (Join-Path $InstallDir "uninstall.ps1") -Value $UninstallScript
Write-Host "[5/5] Created clean uninstaller script." -ForegroundColor Green

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Installation Complete! " -ForegroundColor Green
Write-Host " Launching $AppName..." -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

Start-Process -FilePath $DestExe
