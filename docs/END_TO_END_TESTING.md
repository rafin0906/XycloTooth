# End-to-End Testing & Verification Guide

This guide details the step-by-step procedures to verify the 5 core acceptance criteria and edge cases.

---

## Acceptance Test 1: Full Direction A Flow (PC $\to$ Android $\to$ Server $\to$ Android $\to$ PC)

### Objective:
Verify end-to-end automated pipeline without manual clicks after initial pairing.

### Prerequisites:
1. Windows PC and Android phone paired via Bluetooth settings.
2. Server running on reachable host (`http://<SERVER_IP>:3000`).
3. Windows Bridge application running and configured with Android device.
4. Android application service running (`FileBridgeForegroundService` active).

### Execution Steps:
1. On Windows PC, prepare test file `test_input.txt` with content:
   ```text
   Hello XycloTooth Automated Pipeline!
   Timestamp: 2026-09-26 12:00:00
   Testing automated Bluetooth to HTTPS bridge.
   ```
2. Place `test_input.txt` into the configured Windows Input Directory (e.g., `C:\XycloTooth\Test\Input\`) or click **"Send File"** in the Windows Bridge UI.
3. Observe Windows Bridge logs:
   - File chunked and sent over RFCOMM (`FILE_START` $\to$ chunks $\to$ `FILE_END`).
   - Received `FILE_ACK (OK)` from Android.
4. Observe Android UI and notification:
   - File saved to `/YourApp/input/test_input.txt`.
   - FileStabilityChecker verifies file write completion.
   - HTTPS upload begins with `X-Request-ID`.
   - Server returns response file `response_test_input.txt`.
   - Android saves response locally and queues for Bluetooth.
   - Outbound Bluetooth transfer streams response back to PC.
5. Inspect Windows Output Directory (`C:\XycloTooth\Test\Output\`):
   - `response_test_input.txt` is present and verified.
   - Content matches expected server processed output with matching `X-Request-ID`.

---

## Acceptance Test 2: Network Offline / Resilience

### Objective:
Verify uploads are persisted and automatically resume when internet is restored.

### Execution Steps:
1. On Android phone, toggle Airplane Mode ON (or disable Wi-Fi/Cellular data). Leave Bluetooth enabled.
2. Send `network_offline_test.txt` from Windows PC.
3. Observe that Android receives the file via Bluetooth and writes to storage.
4. Observe that the upload attempt fails or pauses, and transfer state changes to `FAILED` with retry backoff or remains queued in Room DB.
5. Toggle Wi-Fi back ON.
6. Observe Android Foreground Service detects network connectivity and automatically uploads the queued file.
7. Server response is received and sent back to Windows PC automatically.

---

## Acceptance Test 3: Bluetooth Disconnection During Transfer

### Objective:
Verify partial transfers are discarded and cleanly retried upon Bluetooth reconnection.

### Execution Steps:
1. Prepare a larger text file (e.g. 1 MB).
2. Begin transfer from Android to PC.
3. Disable Bluetooth on PC midway through transmission.
4. Observe:
   - Windows receiver cleans up `.part` file.
   - Android detects broken socket, marks transfer `FAILED` with retry scheduled.
5. Re-enable Bluetooth on PC.
6. The outbound queue worker re-establishes RFCOMM socket and re-transmits the file.
7. Verification completes successfully.

---

## Acceptance Test 4: Android Device Reboot Recovery

### Objective:
Verify that pending transfers survive device restart.

### Execution Steps:
1. Queue a transfer on Android while server is stopped.
2. Reboot Android phone.
3. After reboot, launch the application or allow `BOOT_COMPLETED` broadcast receiver to restart `FileBridgeForegroundService`.
4. Inspect UI: Transfer queue displays the persisted item from Room DB.
5. Start server: item immediately resumes upload and flows through to Windows PC.

---

## Acceptance Test 5: Windows Bridge PC Restart

### Objective:
Verify Windows application auto-starts and reconnects to Android device.

### Execution Steps:
1. Ensure "Start with Windows" is checked in Settings.
2. Reboot Windows PC.
3. Windows Bridge launches in system tray on user logon.
4. Bridge initializes Winsock RFCOMM listener and is immediately ready to accept incoming files from Android.
