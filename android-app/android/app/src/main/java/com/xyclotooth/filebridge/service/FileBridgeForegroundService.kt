package com.xyclotooth.filebridge.service

import android.app.*
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.util.Log
import androidx.core.app.NotificationCompat
import com.xyclotooth.filebridge.bluetooth.AndroidBluetoothManager
import com.xyclotooth.filebridge.filesystem.DirectoryMonitor
import com.xyclotooth.filebridge.filesystem.FileStabilityChecker
import com.xyclotooth.filebridge.network.ApiClient
import com.xyclotooth.filebridge.storage.*
import kotlinx.coroutines.*
import java.io.File
import java.util.UUID

class FileBridgeForegroundService : Service() {

    private val serviceScope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private lateinit var db: AppDatabase
    private lateinit var settings: SettingsRepository
    private lateinit var apiClient: ApiClient
    private lateinit var directoryMonitor: DirectoryMonitor
    private lateinit var bluetoothManager: AndroidBluetoothManager

    private var isServiceActive = false

    companion object {
        private const val TAG = "FileBridgeService"
        const val NOTIFICATION_CHANNEL_ID = "xyclotooth_bridge_channel"
        const val NOTIFICATION_ID = 1001

        const val ACTION_START = "ACTION_START"
        const val ACTION_STOP = "ACTION_STOP"

        var isRunning = false
            private set
    }

    override fun onCreate() {
        super.onCreate()
        Log.i(TAG, "Initializing FileBridgeForegroundService...")

        db = AppDatabase.getInstance(this)
        settings = SettingsRepository(this)
        apiClient = ApiClient()

        createNotificationChannel()

        bluetoothManager = AndroidBluetoothManager(
            context = this,
            scope = serviceScope,
            onFileReceivedFromPc = { file -> handleFileReceivedFromPc(file) },
            onConnectionChanged = { isConnected, name ->
                updateNotification("Bluetooth: ${if (isConnected) "Connected to $name" else name}")
            }
        )

        directoryMonitor = DirectoryMonitor(serviceScope) { stableFile ->
            handleStableInputFile(stableFile)
        }
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val action = intent?.action ?: ACTION_START
        if (action == ACTION_STOP) {
            stopBridge()
            stopSelf()
            return START_NOT_STICKY
        }

        startBridge()
        return START_STICKY
    }

    private fun startBridge() {
        if (isServiceActive) return
        isServiceActive = true
        isRunning = true

        val notification = buildNotification("Monitoring active | Bluetooth listening...")
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val type = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE or ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC
            } else {
                ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE
            }
            startForeground(NOTIFICATION_ID, notification, type)
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }

        // Start Bluetooth Listener
        bluetoothManager.startListener(File(settings.incomingDirectory))

        // Start Directory Monitoring
        directoryMonitor.startMonitoring(settings.incomingDirectory)

        // Launch Queue Workers
        serviceScope.launch { uploadWorkerLoop() }
        serviceScope.launch { bluetoothOutboundWorkerLoop() }

        Log.i(TAG, "FileBridge Foreground Service successfully running.")
    }

    private fun stopBridge() {
        isServiceActive = false
        isRunning = false
        directoryMonitor.stopMonitoring()
        bluetoothManager.stopListener()
        serviceScope.cancel()
        Log.i(TAG, "FileBridge Foreground Service stopped.")
    }

    private fun handleFileReceivedFromPc(file: File) {
        // Automatically handled by DirectoryMonitor when written into incomingDirectory
        Log.i(TAG, "File arrived from PC: ${file.name}")
    }

    private fun handleStableInputFile(file: File) {
        serviceScope.launch {
            try {
                val sha256 = FileStabilityChecker.computeSha256(file)

                // Duplicate Protection: check recent successful upload within 10 minutes
                val tenMinutesAgo = System.currentTimeMillis() - 10 * 60 * 1000L
                val recent = db.transferDao().getRecentByHash(sha256, tenMinutesAgo)
                if (recent != null) {
                    Log.i(TAG, "Skipping duplicate file with identical SHA-256 within deduplication window: ${file.name}")
                    return@launch
                }

                val transferId = UUID.randomUUID().toString()
                val requestId = UUID.randomUUID().toString()

                val entity = TransferEntity(
                    id = transferId,
                    filename = file.name,
                    localFilePath = file.absolutePath,
                    fileSize = file.length(),
                    sha256Hash = sha256,
                    direction = "INBOUND_PC",
                    state = TransferState.READY,
                    requestId = requestId
                )

                db.transferDao().insert(entity)
                Log.i(TAG, "Enqueued file for upload: ${file.name} (ID: $transferId)")
                updateNotification("Queued for upload: ${file.name}")
            } catch (e: Exception) {
                Log.e(TAG, "Error handling stable input file", e)
            }
        }
    }

    /**
     * Upload worker loop: processes files waiting for HTTPS upload to remote server.
     */
    private suspend fun uploadWorkerLoop() {
        while (isServiceActive) {
            try {
                val pending = db.transferDao().getPendingUploads()
                for (item in pending) {
                    if (!isServiceActive) break
                    processUploadItem(item)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error in upload worker loop", e)
            }
            delay(2000)
        }
    }

    private suspend fun processUploadItem(item: TransferEntity) {
        db.transferDao().updateState(item.id, TransferState.UPLOADING)
        updateNotification("Uploading: ${item.filename}")

        val file = File(item.localFilePath)
        if (!file.exists()) {
            db.transferDao().updateState(item.id, TransferState.FAILED, "File not found on device")
            return
        }

        val result = apiClient.uploadFile(
            serverBaseUrl = settings.serverUrl,
            apiToken = settings.apiToken,
            file = file,
            requestId = item.requestId,
            responseDir = File(settings.responseDirectory)
        )

        if (result.success && result.responseFile != null) {
            val updated = item.copy(
                state = TransferState.RESPONSE_RECEIVED,
                responseFilename = result.responseFilename,
                responseFilePath = result.responseFile.absolutePath,
                responseSha256 = result.responseSha256,
                updatedAt = System.currentTimeMillis()
            )
            db.transferDao().update(updated)
            Log.i(TAG, "Upload complete. Response saved: ${result.responseFilename}")

            if (settings.autoSendResponse) {
                db.transferDao().updateState(item.id, TransferState.BLUETOOTH_SEND_PENDING)
            }
        } else {
            val retryLimit = settings.retryCount
            if (item.retryCount < retryLimit) {
                db.transferDao().incrementRetry(item.id)
                db.transferDao().updateState(item.id, TransferState.READY, result.error)
                Log.w(TAG, "Upload failed for ${item.filename}, will retry (${item.retryCount + 1}/$retryLimit)")
            } else {
                db.transferDao().updateState(item.id, TransferState.FAILED, result.error ?: "Upload failed")
                Log.e(TAG, "Upload permanently failed for ${item.filename}: ${result.error}")
            }
        }
    }

    /**
     * Bluetooth Outbound worker loop: transfers response files back to Windows PC.
     */
    private suspend fun bluetoothOutboundWorkerLoop() {
        while (isServiceActive) {
            try {
                val pending = db.transferDao().getPendingBluetoothSends()
                for (item in pending) {
                    if (!isServiceActive) break
                    processBluetoothSendItem(item)
                }
            } catch (e: Exception) {
                Log.e(TAG, "Error in bluetooth outbound worker loop", e)
            }
            delay(3000)
        }
    }

    private suspend fun processBluetoothSendItem(item: TransferEntity) {
        val targetPc = settings.pairedDeviceAddress
        if (targetPc.isBlank()) {
            Log.w(TAG, "Cannot send response to PC: No Windows PC selected in settings")
            return
        }

        val responsePath = item.responseFilePath
        if (responsePath == null || !File(responsePath).exists()) {
            db.transferDao().updateState(item.id, TransferState.FAILED, "Response file missing")
            return
        }

        db.transferDao().updateState(item.id, TransferState.BLUETOOTH_SENDING)
        updateNotification("Sending response to PC: ${item.responseFilename}")

        val respFile = File(responsePath)
        val success = bluetoothManager.sendFileToWindows(
            targetAddress = targetPc,
            file = respFile,
            sha256 = item.responseSha256 ?: FileStabilityChecker.computeSha256(respFile)
        )

        if (success) {
            db.transferDao().updateState(item.id, TransferState.SENT_TO_PC)
            updateNotification("Sent to PC: ${item.responseFilename}")
            Log.i(TAG, "Response ${item.responseFilename} sent to Windows PC successfully!")
        } else {
            val retryLimit = settings.retryCount
            if (item.retryCount < retryLimit) {
                db.transferDao().incrementRetry(item.id)
                db.transferDao().updateState(item.id, TransferState.BLUETOOTH_SEND_PENDING, "Bluetooth transfer failed, retrying...")
            } else {
                db.transferDao().updateState(item.id, TransferState.FAILED, "Bluetooth transfer failed after max retries")
            }
        }
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                NOTIFICATION_CHANNEL_ID,
                "XycloTooth File Bridge Service",
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "Automated Bluetooth and Server Text File Pipeline"
            }
            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(channel)
        }
    }

    private fun buildNotification(status: String): Notification {
        return NotificationCompat.Builder(this, NOTIFICATION_CHANNEL_ID)
            .setContentTitle("XycloTooth File Bridge Active")
            .setContentText(status)
            .setSmallIcon(android.R.drawable.stat_notify_sync)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
    }

    private fun updateNotification(status: String) {
        val manager = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        manager.notify(NOTIFICATION_ID, buildNotification(status))
    }

    override fun onDestroy() {
        stopBridge()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null
}
