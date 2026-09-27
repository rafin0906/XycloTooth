# Troubleshooting & Diagnostics Guide

## 1. Bluetooth Connection Issues

### Symptom: Windows Bridge shows "Adapter Offline" or "Device Not Found"
- **Cause 1:** Windows Bluetooth is disabled in Windows Settings.
  - **Resolution:** Open Windows Settings $\to$ Bluetooth & devices $\to$ Ensure Bluetooth is toggled **ON**.
- **Cause 2:** Phone and PC are not paired.
  - **Resolution:** Pair Android phone with Windows PC in Windows Bluetooth settings first. Ensure both sides accept the pairing PIN.
- **Cause 3:** Serial Port Profile (SPP) / RFCOMM service conflict.
  - **Resolution:** Restart the Windows Bridge. The app binds to RFCOMM channel using GUID `4a984249-1319-482d-85f3-2c16313508d7`. If already bound by a previous instance, check Windows Task Manager and end any lingering `XycloToothBridge.exe` processes.

### Symptom: Android shows "Bluetooth Connection Failed"
- **Cause 1:** Missing runtime Bluetooth permissions on Android 12+ (API 31+).
  - **Resolution:** Ensure `BLUETOOTH_CONNECT`, `BLUETOOTH_SCAN`, and `BLUETOOTH_ADVERTISE` are granted. The app prompts on first launch.
- **Cause 2:** Windows Bridge is not running or listening.
  - **Resolution:** Verify Windows Bridge status is green ("Listening") before initiating connection from Android.

---

## 2. Directory Monitoring Issues

### Symptom: Files dropped into Android folder are not detected
- **Cause 1:** Storage permissions not granted.
  - **Resolution:** On Android 10+ (API 29+), ensure the app has access to the configured folder (e.g. `/Android/data/com.xyclotooth.filebridge/files/input/` or scoped storage URI via Storage Access Framework).
- **Cause 2:** File is still being copied or locked by another process.
  - **Resolution:** `FileStabilityChecker` deliberately waits until file size is unchanged for at least 1500ms before queuing to avoid incomplete partial file reads. Wait 2 seconds.
- **Cause 3:** File extension is not `.txt`.
  - **Resolution:** The bridge only processes `.txt` files. Rename file to end with `.txt`.

---

## 3. Server & Network Issues

### Symptom: Android logs show "Upload Failed: 401 Unauthorized"
- **Cause:** Invalid or missing Bearer token in Android Settings.
  - **Resolution:** Open Android App $\to$ Settings $\to$ ensure **API Token** matches the server token (`xyclo-secret-token-2026`).

### Symptom: Android logs show "Connection Refused" to Server
- **Cause 1:** Server is using `localhost` on Android.
  - **Resolution:** On Android, `localhost` refers to the phone itself, NOT your PC or external server.
    - If testing against PC from Android Emulator: Use `http://10.0.2.2:3000`.
    - If testing from physical Android phone: Use your PC's local Wi-Fi IP address (e.g., `http://192.168.1.50:3000`) or a public domain/tunnel.
- **Cause 2:** Windows Firewall blocking port 3000.
  - **Resolution:** Allow inbound TCP port 3000 in Windows Defender Firewall.

---

## 4. Hash Mismatch & Data Integrity

### Symptom: Log shows "FILE_ACK (HASH_MISMATCH)"
- **Cause:** Bytes corrupted during Bluetooth transmission or premature read.
  - **Resolution:** The system automatically discards the corrupted `.part` file and triggers an automatic retry. If it recurs, check Bluetooth signal strength or interference.

---

## 5. Background Execution & Doze Mode

### Symptom: Android stops syncing when screen turns off
- **Cause:** Android Battery Optimization killed background tasks.
  - **Resolution:**
    1. Ensure `FileBridgeForegroundService` notification is visible in the notification drawer.
    2. Go to Android Settings $\to$ Apps $\to$ XycloTooth $\to$ Battery $\to$ Set to **"Unrestricted"**.
