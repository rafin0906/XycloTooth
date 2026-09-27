# XycloTooth Custom RFCOMM Bluetooth Protocol Specification

**Version:** 1.0.0  
**Transport:** Bluetooth Classic RFCOMM (Serial Port Profile / SPP)  
**Default Service UUID:** `4a984249-1319-482d-85f3-2c16313508d7` (SPP Fallback: `00001101-0000-1000-8000-00805F9B34FB`)  
**Target Environment:** Windows 10/11 & Android 10+ (API 29 - 36)

---

## 1. Protocol Motivation & Architecture

Standard Windows Bluetooth file reception typically relies on the OS-level "Bluetooth File Transfer Wizard" (OBEX Object Push Profile), which requires the user to manually click "Receive a file" in the system tray for every incoming file.

To achieve **100% automated, zero-click, bidirectional transfer** between Windows and Android:
1. Both Windows and Android establish direct socket communication over **Bluetooth Classic RFCOMM**.
2. Either Windows or Android can act as the RFCOMM Server (listening) or Client (connecting).
3. Transfers use length-prefixed binary frames with CRC32 frame checksums and end-to-end SHA-256 validation.
4. Transmission occurs in discrete chunks (default 4096 bytes) to prevent memory exhaustion and facilitate resumed or verified transfers.

---

## 2. Frame Structure (Binary Format)

Every transmission over the RFCOMM stream consists of one or more structured frames.

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|       Magic (0x58, 0x54)      |  Version (1)  |  Frame Type   |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                       Payload Length (N)                      |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                         Payload Bytes                         |
|                           (N bytes)                           |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                     CRC-32 Checksum (4B)                      |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

### Field Definitions:
- **Magic Bytes (2 bytes):** `0x58`, `0x54` (ASCII `"XT"` for XycloTooth). Protects against stream alignment errors.
- **Protocol Version (1 byte):** `0x01`.
- **Frame Type (1 byte):**
  - `0x01` (`HELLO`): Handshake initiation.
  - `0x02` (`HELLO_ACK`): Handshake response with agent capabilities.
  - `0x10` (`FILE_START`): Start of file metadata.
  - `0x11` (`FILE_CHUNK`): Binary chunk slice.
  - `0x12` (`FILE_END`): End of file notification with overall SHA-256.
  - `0x13` (`FILE_ACK`): Acknowledgment (OK or HASH_MISMATCH).
  - `0x20` (`PING`): Keep-alive probe.
  - `0x21` (`PONG`): Keep-alive reply.
  - `0xFF` (`ERROR`): Error notification frame.
- **Payload Length (4 bytes, Big-Endian UInt32):** Number of payload bytes $N$ ($0 \le N \le 65536$).
- **Payload (N bytes):** UTF-8 JSON or raw binary chunk data depending on `Frame Type`.
- **CRC-32 Checksum (4 bytes, Big-Endian UInt32):** Computed over Magic + Version + Frame Type + Payload Length + Payload.

---

## 3. Frame Types & Payloads

### 3.1 `HELLO` (`0x01`) & `HELLO_ACK` (`0x02`)
Payload (UTF-8 JSON):
```json
{
  "clientName": "XycloTooth-Windows",
  "version": "1.0.0",
  "deviceId": "E4:5F:01:23:45:67",
  "maxChunkSize": 4096
}
```

### 3.2 `FILE_START` (`0x10`)
Payload (UTF-8 JSON):
```json
{
  "fileId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "filename": "input.txt",
  "fileSize": 18420,
  "chunkSize": 4096,
  "totalChunks": 5,
  "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
}
```

### 3.3 `FILE_CHUNK` (`0x11`)
Binary format:
- **Chunk Index (4 bytes, Big-Endian UInt32):** 0-indexed.
- **Chunk Data (remaining bytes):** Raw bytes of the file slice.

### 3.4 `FILE_END` (`0x12`)
Payload (UTF-8 JSON):
```json
{
  "fileId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "totalBytesSent": 18420,
  "sha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
}
```

### 3.5 `FILE_ACK` (`0x13`)
Payload (UTF-8 JSON):
```json
{
  "fileId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
  "status": "OK",
  "receivedSha256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
  "message": "File written and verified successfully"
}
```
Possible status values:
- `"OK"`: File verified and atomically committed.
- `"HASH_MISMATCH"`: Received bytes do not match expected SHA-256. Sender should retry.
- `"IO_ERROR"`: Storage write failure.

---

## 4. Transfer Flow Diagrams

### Windows PC $\to$ Android
```
Windows PC                              Android Device
    |                                         |
    |---- HELLO (0x01) ---------------------->|
    |<--- HELLO_ACK (0x02) -------------------|
    |                                         |
    |---- FILE_START (fileId, size, sha256) ->| (Prepares .part file)
    |<--- FILE_ACK (status: READY) -----------|
    |                                         |
    |---- FILE_CHUNK #0 (4096B) ------------->| (Appends to .part)
    |---- FILE_CHUNK #1 (4096B) ------------->| (Appends to .part)
    |---- FILE_CHUNK #2 (remaining) --------->| (Appends to .part)
    |                                         |
    |---- FILE_END (totalBytes, sha256) ----->| (Computes received SHA256)
    |                                         | (Atomic rename: .part -> .txt)
    |<--- FILE_ACK (status: OK, sha256) ------| (Enqueues for HTTPS upload)
```

### Android $\to$ Windows PC (Response Flow)
```
Android Device                          Windows PC
    |                                         |
    |---- FILE_START (response.txt, sha256) ->| (Prepares response.txt.part)
    |<--- FILE_ACK (status: READY) -----------|
    |                                         |
    |---- FILE_CHUNK #0 --------------------->|
    |---- FILE_CHUNK #1 --------------------->|
    |---- FILE_END -------------------------->| (Verifies SHA256)
    |                                         | (Atomic rename: .part -> .txt)
    |<--- FILE_ACK (status: OK) --------------| (Notifies Tray UI)
```

---

## 5. Fault Tolerance & Idempotency Rules

1. **Temporary File Reception (`.part`):** All incoming transfers are written to a hidden `.part` file. The file is never moved to the final directory until:
   - All chunks are received.
   - The computed SHA-256 matches the expected SHA-256.
2. **Duplicate Protection:** Transfers include a unique `fileId` (UUID) and `sha256`. If the receiver already successfully completed an identical file within the deduplication window, it immediately responds with `FILE_ACK (OK)` without re-writing.
3. **Disconnection Recovery:** If the Bluetooth connection drops during chunk transmission, the `.part` file is kept with its current offset. On reconnection, the sender re-initiates `FILE_START`; the receiver responds with the next expected chunk offset or requests a fresh transfer if verification fails.
