# Android Room Database Schema: Transfer Queue & State Persistence

## 1. Overview
The Android application persists all transfer activity, file lifecycle states, retry counts, hashes, and correlation identifiers in a local SQLite database managed by Android Room (`AppDatabase`).

Database Name: `xyclotooth_bridge.db`  
Version: `1`

---

## 2. Table: `transfers`

```sql
CREATE TABLE IF NOT EXISTS `transfers` (
    `id` TEXT NOT NULL PRIMARY KEY,            -- UUID string identifying this pipeline job
    `filename` TEXT NOT NULL,                 -- Base filename (e.g., "input.txt")
    `localFilePath` TEXT NOT NULL,            -- Absolute path on Android device
    `fileSize` INTEGER NOT NULL,              -- Size in bytes
    `sha256Hash` TEXT NOT NULL,               -- SHA-256 hex string of file contents
    `direction` TEXT NOT NULL,                -- 'INBOUND_PC' (PC -> Android -> Server) or 'OUTBOUND_PC' (Server -> Android -> PC)
    `state` TEXT NOT NULL,                    -- Current state (see State Machine below)
    `requestId` TEXT NOT NULL,                -- Unique correlation ID (X-Request-ID) sent to server
    `responseFilename` TEXT,                  -- Filename of the server response
    `responseFilePath` TEXT,                  -- Path where response is stored
    `responseSha256` TEXT,                    -- SHA-256 hex of received response
    `retryCount` INTEGER NOT NULL DEFAULT 0,  -- Number of retries attempted
    `lastError` TEXT,                         -- Descriptive message of last error encountered
    `createdAt` INTEGER NOT NULL,             -- Epoch milliseconds of creation
    `updatedAt` INTEGER NOT NULL              -- Epoch milliseconds of last state update
);

CREATE INDEX IF NOT EXISTS `index_transfers_state` ON `transfers` (`state`);
CREATE INDEX IF NOT EXISTS `index_transfers_sha256` ON `transfers` (`sha256Hash`);
CREATE INDEX IF NOT EXISTS `index_transfers_requestId` ON `transfers` (`requestId`);
```

---

## 3. State Machine Enum (`TransferState`)

| State | Description | Next Allowed States |
|---|---|---|
| `DETECTED` | File identified by FileObserver or Bluetooth receiver | `WAITING_FOR_STABILITY`, `FAILED` |
| `WAITING_FOR_STABILITY` | Verifying file size remains constant and file is closed | `READY`, `FAILED` |
| `READY` | File complete, hash calculated, ready for upload | `UPLOADING`, `FAILED` |
| `UPLOADING` | Active HTTPS multipart upload in progress | `UPLOADED`, `FAILED` |
| `UPLOADED` | Server accepted upload, awaiting response stream | `WAITING_FOR_RESPONSE`, `FAILED` |
| `WAITING_FOR_RESPONSE` | Processing server response stream or download URL | `RESPONSE_RECEIVED`, `FAILED` |
| `RESPONSE_RECEIVED` | Response file downloaded, SHA-256 verified, saved | `BLUETOOTH_SEND_PENDING`, `FAILED` |
| `BLUETOOTH_SEND_PENDING`| Enqueued in Bluetooth outbound queue for Windows PC | `BLUETOOTH_SENDING`, `FAILED` |
| `BLUETOOTH_SENDING` | Actively streaming chunks to Windows PC | `SENT_TO_PC`, `FAILED` |
| `SENT_TO_PC` | Windows acknowledged successful receipt (`FILE_ACK OK`) | *Terminal State* |
| `FAILED` | Transfer encountered error (network/Bluetooth/storage) | `READY`, `BLUETOOTH_SEND_PENDING` (on Retry) |

---

## 4. Idempotency & Deduplication Queries

### Checking for duplicate files before enqueuing:
```sql
SELECT * FROM transfers 
WHERE sha256Hash = :hash 
  AND state NOT IN ('FAILED') 
  AND createdAt > :windowLimitEpoch;
```
If a record exists within the last 10 minutes with identical SHA-256, the file is skipped with a duplicate log notice to prevent infinite loops.

### Finding pending items for resumption after restart:
```sql
-- Pending uploads to server:
SELECT * FROM transfers WHERE state IN ('READY', 'UPLOADING') ORDER BY createdAt ASC;

-- Pending transfers to Windows PC:
SELECT * FROM transfers WHERE state IN ('RESPONSE_RECEIVED', 'BLUETOOTH_SEND_PENDING', 'BLUETOOTH_SENDING') ORDER BY createdAt ASC;
```
