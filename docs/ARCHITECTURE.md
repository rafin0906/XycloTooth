# System Architecture: Bluetooth ↔ Android ↔ Server Automated Text File Bridge

## 1. System Overview

The system provides an automated, zero-touch, bidirectional pipeline for `.txt` files across three distinct layers:
1. **Windows PC Agent (`windows-bridge`)**: Standalone native desktop application with system-tray presence, listening on Bluetooth Classic RFCOMM.
2. **Android Mobile App (`android-app`)**: React Native UI + native Kotlin Foreground Service managing Bluetooth RFCOMM, Room persistence, FileObserver directory monitoring, and HTTPS communications.
3. **External Server (`server`)**: HTTP/HTTPS server receiving multipart/form-data text file uploads, processing them, and returning response text files with request correlation.

---

## 2. End-to-End Pipeline

```
[Windows PC]
   │
   │ 1. User drops file in Input Dir (or clicks Send)
   │ 2. Windows Bridge chunks & streams over RFCOMM
   ▼
[Bluetooth RFCOMM Channel (SPP / Custom UUID)]
   │
   │ 3. Android Kotlin BluetoothManager receives chunks into .part file
   │ 4. Verifies SHA-256 hash & moves to /YourApp/input/
   ▼
[Android Incoming Directory / Storage]
   │
   │ 5. FileObserver & FileStabilityChecker confirms file write complete
   │ 6. Adds to persistent Room TransferQueue (State: READY)
   ▼
[Android Foreground Service & UploadManager]
   │
   │ 7. Uploads via HTTPS POST /api/upload (multipart, Bearer token, X-Request-ID)
   ▼
[External Remote Server API]
   │
   │ 8. Validates Bearer token & file integrity
   │ 9. Processes file & generates response.txt
   │ 10. Returns response.txt (or JSON with download URL)
   ▼
[Android DownloadManager]
   │
   │ 11. Saves response to /YourApp/responses/response.txt.part
   │ 12. Atomically commits response.txt & updates Room state (BLUETOOTH_SEND_PENDING)
   ▼
[Android Outbound Bluetooth Queue]
   │
   │ 13. BluetoothTransferManager streams response.txt to Windows PC
   ▼
[Bluetooth RFCOMM Channel]
   │
   │ 14. Windows Bridge receives into response.txt.part
   │ 15. Verifies SHA-256 matches header
   │ 16. Atomically commits to Windows Output Directory
   ▼
[Windows Output Directory]
   │
   └── User receives response.txt without any manual intervention!
```

---

## 3. Component Details

### A. Windows Desktop Application (`windows-bridge`)
- **Technology:** Native .NET 8 / C# compiled as a **Self-Contained Single-File Executable (`-p:PublishSingleFile=true`)**.
  - Includes all runtime dependencies internally.
  - Zero external prerequisites (no .NET runtime, Python, Java, or Node required on end-user PC).
- **Bluetooth Stack:** Uses Windows Winsock `AF_BTH` (Bluetooth Sockets) with RFCOMM (`BTHPROTO_RFCOMM`), binding to Service Class UUID `4a984249-1319-482d-85f3-2c16313508d7` or standard SerialPortProfile (SPP).
- **Core Modules:**
  - `BluetoothManager`: Manages listening server socket & outgoing client connections.
  - `ProtocolEngine`: Length-prefixed binary framing, CRC32 verification, chunk assembly.
  - `FileManager`: Monitors input folder, performs atomic file writes (`.part` $\to$ target), calculates SHA-256.
  - `SettingsManager`: Configuration persisted in local app data JSON (directories, device address, auto-start).
  - `TrayIconManager`: Minimizes to notification area, balloon tooltips for completed transfers.

### B. Android Native Layer (`android-app/android`)
- **Foreground Service (`FileBridgeForegroundService`):**
  - Keeps connection alive and queues active even when the React Native UI is closed, minimized, or screen locked.
  - Displays persistent notification with real-time bridge status.
- **Directory Monitoring (`DirectoryMonitor` & `FileStabilityChecker`):**
  - Monitors the input directory using Android `FileObserver`.
  - Multi-stage stability verification: checks size and modification time over consecutive intervals to ensure full file write before initiating upload.
- **Local Persistence (`AppDatabase` via Room):**
  - `TransferEntity` records all transfers, lifecycle states, retry counts, request IDs, and file hashes.
- **Network Layer (`ApiClient`):**
  - OkHttp client with TLS 1.3 support, Bearer token authentication, multipart file upload, and exponential backoff.
- **React Native Bridge (`FileBridgeModule`):**
  - Exposes control methods (`startService`, `stopService`, `getPairedDevices`, `retryTransfer`) and emits lifecycle events to JavaScript.

### C. React Native UI (`android-app/src`)
- Real-time dashboard showing status cards:
  - Bluetooth connection state.
  - Monitoring status and target folders.
  - Server connection and pending upload/download queues.
  - Live activity log feed.
- Settings modal for paired PC selection, folder paths, server URL, Bearer token, and auto-sync toggles.

### D. External Server API (`server`)
- Reference Express.js server providing:
  - `POST /api/upload`: Handles multipart uploads, extracts `X-Request-ID`, generates correlated text response.
  - `GET /api/download/:id`: Secondary download endpoint for URL-based response workflows.
  - `GET /api/health`: Health probe.
