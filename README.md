# XycloTooth: Bluetooth ↔ Android ↔ Server Automated Text File Bridge

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Android-green.svg)](README.md)
[![Protocol](https://img.shields.io/badge/Bluetooth-RFCOMM%20Custom%20Protocol-orange.svg)](docs/BLUETOOTH_PROTOCOL_SPEC.md)

An end-to-end, zero-touch automated text-file pipeline between a **Windows PC**, an **Android smartphone**, and an **external remote server**.

---

## 1. System Objective & Overview

The goal of this system is to bridge files across devices without requiring manual user interaction for each file:

```
Direction A:
Windows PC (Input Folder / Send) 
   ──[ Bluetooth RFCOMM ]──► Android Phone 
   ──[ HTTPS Multipart ]──► Remote Server 
   ──[ Response File ]──► Android Phone 
   ──[ Bluetooth RFCOMM ]──► Windows PC (Output Folder)

Reverse Direction (Server Initiated):
Remote Server ──► Android Phone ──[ Bluetooth RFCOMM ]──► Windows PC
```

### Key Highlights:
- **Zero Runtime Dependencies on Windows PC:** Distributed as a **single-file, self-contained native executable (`XycloToothBridge.exe`)**. The end user does **NOT** need Python, Node.js, Java, .NET Runtime, or Visual C++ runtimes installed.
- **Direct RFCOMM Socket Communication:** Bypasses Windows's built-in "Bluetooth File Transfer Wizard" UI prompts. Windows and Android connect directly via custom RFCOMM frames.
- **Android Kotlin Foreground Service:** Keeps the pipeline active, monitoring folders, and processing transfers even when the phone screen is locked or the React Native UI is closed.
- **Robust Persistence & Idempotency:** Android Room (SQLite) database records all transfer states, correlation request IDs, retry counters, and SHA-256 hashes to prevent duplicate transmissions.
- **File Stability Verification:** Automatically waits for write completion and file stability before queuing, preventing partial or corrupted transfers.

---

## 2. Repository Architecture

```
c:/XycloTooth/
├── android-app/                # React Native + Kotlin Native Android Application
│   ├── android/                # Android Gradle Project
│   │   └── app/src/main/java/com/xyclotooth/filebridge/
│   │       ├── bluetooth/      # AndroidBluetoothManager & BluetoothProtocol
│   │       ├── filesystem/     # DirectoryMonitor & FileStabilityChecker
│   │       ├── network/        # ApiClient (OkHttp HTTPS multipart)
│   │       ├── reactnative/    # FileBridgeModule & FileBridgePackage
│   │       ├── service/        # FileBridgeForegroundService & BootReceiver
│   │       └── storage/        # AppDatabase, TransferEntity, TransferDao, Settings
│   ├── src/                    # React Native UI (Dashboard, Cards, Modals)
│   ├── App.tsx                 # Main React Native Dashboard
│   └── package.json
├── windows-bridge/             # Standalone Windows Desktop App (.NET 8 WPF)
│   ├── Bluetooth/              # Winsock RFCOMM Server & Client (InTheHand)
│   ├── Logging/                # Structured AppLogger
│   ├── Managers/               # FileManager, TransferManager
│   ├── Models/                 # TransferModels & Binary Frame Definitions
│   ├── Protocol/               # ProtocolEngine & Crc32
│   ├── Settings/               # SettingsManager & AppSettings
│   ├── UI/                     # TrayIconManager & Dark Theme MainWindow
│   ├── dist/                   # Bundled Self-Contained Single-File Executable
│   └── XycloToothBridge.csproj
├── windows-bridge.Tests/       # Unit & Integration Tests (xUnit)
├── server/                     # Reference HTTPS / HTTP External Server (Node.js)
├── installer/                  # Windows Installers
│   ├── XycloToothBridgeInstaller.iss  # Inno Setup Script
│   └── install.ps1             # One-Click PowerShell Installer
├── docs/                       # Technical Specifications & Documentation
│   ├── ARCHITECTURE.md         # Comprehensive System Architecture
│   ├── BLUETOOTH_PROTOCOL_SPEC.md # Binary Framing, CRC32 & SHA-256 Spec
│   ├── DATABASE_SCHEMA.md      # Room DB Schema & State Machine
│   ├── END_TO_END_TESTING.md   # Step-by-Step Acceptance Testing Guide
│   └── TROUBLESHOOTING.md      # Diagnostics for Bluetooth, Storage & Network
└── tests/
    └── e2e_pipeline_test.js    # Automated Pipeline Test Harness
```

---

## 3. Bluetooth Framing Protocol Specification

Transfers between Windows and Android utilize a custom length-prefixed binary framing protocol over Bluetooth Classic RFCOMM (Service Class UUID `4a984249-1319-482d-85f3-2c16313508d7` with fallback to standard SerialPortProfile):

```
+------------------------+-------------+------------+--------------------+
| Magic: "XT" (0x58, 0x54) | Version: 0x01 | Type (1B)  | Payload Length (4B)|
+------------------------+-------------+------------+--------------------+
| Payload Data (N bytes: JSON metadata or raw binary chunk)               |
+------------------------------------------------------------------------+
| CRC-32 Checksum (4 bytes Big-Endian)                                   |
+------------------------------------------------------------------------+
```

### Flow Lifecycle:
1. `HELLO (0x01)` $\to$ `HELLO_ACK (0x02)`: Handshake.
2. `FILE_START (0x10)`: Filename, size, chunk count, and sender SHA-256.
3. `FILE_ACK (0x13, status: READY)`: Receiver prepares hidden `.part` file.
4. `FILE_CHUNK (0x11)`: 4096-byte slices with 4-byte chunk index.
5. `FILE_END (0x12)`: Transfer completion signal.
6. **Integrity Validation:** Receiver computes SHA-256 of `.part` file. If matched, atomically renames to `.txt` and replies with `FILE_ACK (0x13, status: OK)`.

*For complete details, see [docs/BLUETOOTH_PROTOCOL_SPEC.md](file:///c:/XycloTooth/docs/BLUETOOTH_PROTOCOL_SPEC.md).*

---

## 4. Setup & Installation

### A. Windows Desktop Application

#### Option 1: One-Click PowerShell Install
Run in PowerShell (from the repository root):
```powershell
powershell -ExecutionPolicy Bypass -File .\installer\install.ps1
```
This installs `XycloToothBridge.exe` into `%LOCALAPPDATA%\Programs\XycloTooth`, creates Start Menu and Desktop shortcuts, and sets up your default input/output directories (`%USERPROFILE%\XycloTooth\Input` and `Output`).

#### Option 2: Inno Setup Compilation
Open `installer\XycloToothBridgeInstaller.iss` in Inno Setup Compiler and click **Compile** to generate `XycloToothBridge-Setup-v1.0.0.exe`.

#### Option 3: Manual Execution
Run the standalone executable directly:
```cmd
windows-bridge\dist\XycloToothBridge.exe
```

---

### B. External Server API

1. Navigate to the `server/` directory:
   ```cmd
   cd server
   npm install
   ```
2. Start the server:
   ```cmd
   npm start
   ```
3. Server will listen on `http://localhost:3000` (or `PORT` environment variable).
   - Health check: `http://localhost:3000/api/health`
   - Upload endpoint: `POST http://localhost:3000/api/upload`

---

### C. Android Mobile Application

1. Connect your Android device or start an emulator with Android 10+ (API 29+).
2. Navigate to `android-app/`:
   ```cmd
   cd android-app
   npm install
   ```
3. Build and launch on device:
   ```cmd
   npx react-native run-android
   ```
4. On first launch:
   - Accept Bluetooth and Notification runtime permissions.
   - Tap **"Select PC"** and choose your paired Windows PC.
   - Tap **"Settings"** and configure your server URL (e.g. `http://192.168.1.X:3000` for physical devices or `http://10.0.2.2:3000` for Android emulator).
   - Tap **"▶ Start"** to activate the `FileBridgeForegroundService`.

---

## 5. End-to-End Operation Walkthrough

1. **Pair Devices Once:**
   Pair your Android phone and Windows PC in standard Windows Bluetooth settings (`Settings -> Bluetooth & devices -> Add device`).
2. **Start the Windows Bridge:**
   Launch `XycloToothBridge.exe`. It automatically starts listening on RFCOMM and monitors the Input folder.
3. **Start the Android Bridge:**
   Open the Android app and tap **"▶ Start"**. The ongoing notification appears.
4. **Trigger Transfer:**
   Drop any `.txt` file (e.g., `input.txt`) into `C:\Users\<You>\XycloTooth\Input\` (or click **"Send .txt File"** in the Windows UI).
5. **Watch the Automation:**
   - Windows sends `input.txt` to Android via Bluetooth RFCOMM.
   - Android verifies SHA-256, writes to `/YourApp/input/input.txt`.
   - Android `FileStabilityChecker` confirms file is complete and triggers HTTPS upload.
   - Remote server processes file and returns `response_input.txt`.
   - Android saves response, updates Room DB, and streams back to Windows via Bluetooth.
   - Windows receives file into `.part`, verifies SHA-256, and atomically moves to `C:\Users\<You>\XycloTooth\Output\response_input.txt`.
   - Windows tray balloon alerts the user: *"File Received: response_input.txt"*.

---

## 6. Running Automated Tests

### C# Windows Bridge & Protocol Tests:
```cmd
dotnet test windows-bridge.Tests
```
*Validates protocol framing, CRC32 tamper detection, SHA-256 accuracy, and file stability checker.*

### End-to-End Pipeline Harness:
```cmd
node tests/e2e_pipeline_test.js
```
*Validates protocol chunking, frame round-trip, checksum resilience, and reassembly.*

---

## 7. Documentation Index

- [System Architecture](file:///c:/XycloTooth/docs/ARCHITECTURE.md)
- [Bluetooth RFCOMM Protocol Specification](file:///c:/XycloTooth/docs/BLUETOOTH_PROTOCOL_SPEC.md)
- [Database Schema & State Machine](file:///c:/XycloTooth/docs/DATABASE_SCHEMA.md)
- [Acceptance Testing Guide](file:///c:/XycloTooth/docs/END_TO_END_TESTING.md)
- [Troubleshooting & Diagnostics](file:///c:/XycloTooth/docs/TROUBLESHOOTING.md)
