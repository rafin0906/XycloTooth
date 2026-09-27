package com.xyclotooth.filebridge.bluetooth

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothServerSocket
import android.bluetooth.BluetoothSocket
import android.content.Context
import android.util.Log
import com.xyclotooth.filebridge.filesystem.FileStabilityChecker
import kotlinx.coroutines.*
import org.json.JSONObject
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.IOException
import java.nio.ByteBuffer
import java.util.*

data class DeviceInfo(
    val name: String,
    val address: String,
    val isBonded: Boolean
)

class AndroidBluetoothManager(
    private val context: Context,
    private val scope: CoroutineScope,
    private val onFileReceivedFromPc: (file: File) -> Unit,
    private val onConnectionChanged: (isConnected: Boolean, deviceName: String) -> Unit
) {
    private val adapter: BluetoothAdapter? = BluetoothAdapter.getDefaultAdapter()
    private var serverSocket: BluetoothServerSocket? = null
    private var activeSocket: BluetoothSocket? = null
    private var isListening = false

    companion object {
        private const val TAG = "AndroidBluetoothManager"
        val CUSTOM_SERVICE_UUID: UUID = UUID.fromString("4a984249-1319-482d-85f3-2c16313508d7")
        val SPP_UUID: UUID = UUID.fromString("00001101-0000-1000-8000-00805F9B34FB")
        private const val CHUNK_SIZE = 4096
    }

    @SuppressLint("MissingPermission")
    fun getBondedDevices(): List<DeviceInfo> {
        val list = mutableListOf<DeviceInfo>()
        if (adapter == null || !adapter.isEnabled) return list

        try {
            val bonded = adapter.bondedDevices
            for (dev in bonded) {
                list.add(
                    DeviceInfo(
                        name = dev.name ?: "Unknown Device",
                        address = dev.address,
                        isBonded = true
                    )
                )
            }
        } catch (e: SecurityException) {
            Log.e(TAG, "Missing BLUETOOTH_CONNECT permission", e)
        }
        return list
    }

    @SuppressLint("MissingPermission")
    fun startListener(incomingDir: File) {
        if (isListening || adapter == null || !adapter.isEnabled) return
        isListening = true

        scope.launch(Dispatchers.IO) {
            try {
                serverSocket = try {
                    adapter.listenUsingRfcommWithServiceRecord("XycloTooth-Bridge", CUSTOM_SERVICE_UUID)
                } catch (e: IOException) {
                    Log.w(TAG, "Failed listening on custom UUID, falling back to SPP", e)
                    adapter.listenUsingRfcommWithServiceRecord("XycloTooth-SPP", SPP_UUID)
                }

                Log.i(TAG, "RFCOMM server socket listening...")
                onConnectionChanged(false, "Listening for PC...")

                while (isListening && serverSocket != null) {
                    try {
                        val socket = serverSocket?.accept() ?: break
                        Log.i(TAG, "Accepted Bluetooth connection from: ${socket.remoteDevice?.name}")
                        activeSocket?.close()
                        activeSocket = socket
                        onConnectionChanged(true, socket.remoteDevice?.name ?: "Windows PC")

                        handleSession(socket, incomingDir)
                    } catch (e: IOException) {
                        if (isListening) {
                            Log.w(TAG, "Accept error: ${e.message}")
                            delay(1000)
                        }
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Server socket failed", e)
            } finally {
                onConnectionChanged(false, "Disconnected")
            }
        }
    }

    fun stopListener() {
        isListening = false
        try {
            serverSocket?.close()
            activeSocket?.close()
        } catch (e: IOException) {
            Log.e(TAG, "Error closing Bluetooth sockets", e)
        }
        serverSocket = null
        activeSocket = null
        onConnectionChanged(false, "Stopped")
    }

    private fun handleSession(socket: BluetoothSocket, incomingDir: File) {
        try {
            val inStream = socket.inputStream
            val outStream = socket.outputStream

            while (socket.isConnected) {
                val frame = BluetoothProtocol.readFrame(inStream) ?: break
                when (frame.type) {
                    FrameType.HELLO -> {
                        val ack = JSONObject().apply {
                            put("clientName", "XycloTooth-Android")
                            put("version", "1.0.0")
                        }
                        BluetoothProtocol.writeJsonFrame(outStream, FrameType.HELLO_ACK, ack)
                    }
                    FrameType.FILE_START -> {
                        handleInboundFile(inStream, outStream, frame, incomingDir)
                    }
                    FrameType.PING -> {
                        BluetoothProtocol.writeFrame(outStream, FrameType.PONG, ByteArray(0))
                    }
                    else -> Log.d(TAG, "Unhandled frame: ${frame.type}")
                }
            }
        } catch (e: Exception) {
            Log.e(TAG, "Session error", e)
        } finally {
            try { socket.close() } catch (_: Exception) {}
            onConnectionChanged(false, if (isListening) "Listening for PC..." else "Disconnected")
        }
    }

    private fun handleInboundFile(
        inStream: java.io.InputStream,
        outStream: java.io.OutputStream,
        startFrame: ProtocolFrame,
        incomingDir: File
    ) {
        val meta = startFrame.asJsonObject()
        val fileId = meta.getString("fileId")
        val filename = meta.getString("filename")
        val fileSize = meta.getLong("fileSize")
        val expectedSha256 = meta.getString("sha256")

        Log.i(TAG, "Receiving file from PC: $filename ($fileSize bytes)")

        if (!incomingDir.exists()) incomingDir.mkdirs()
        val partFile = File(incomingDir, "$filename.$fileId.part")
        val finalFile = File(incomingDir, filename)

        // Send READY ACK
        val readyJson = JSONObject().apply {
            put("fileId", fileId)
            put("status", "READY")
        }
        BluetoothProtocol.writeJsonFrame(outStream, FrameType.FILE_ACK, readyJson)

        // Read chunks
        FileOutputStream(partFile).use { fos ->
            var bytesRead = 0L
            while (bytesRead < fileSize) {
                val chunkFrame = BluetoothProtocol.readFrame(inStream) ?: throw IOException("Stream broke during chunk read")
                if (chunkFrame.type != FrameType.FILE_CHUNK) throw IOException("Unexpected frame during chunks")

                // Payload: 4 bytes index + data
                val chunkData = chunkFrame.payload.copyOfRange(4, chunkFrame.payload.size)
                fos.write(chunkData)
                bytesRead += chunkData.size
            }
            fos.flush()
        }

        // Read FILE_END
        val endFrame = BluetoothProtocol.readFrame(inStream) ?: throw IOException("Missing FILE_END")
        if (endFrame.type != FrameType.FILE_END) throw IOException("Expected FILE_END")

        // Verify SHA-256
        val computedSha256 = FileStabilityChecker.computeSha256(partFile)
        if (!computedSha256.equals(expectedSha256, ignoreCase = true)) {
            partFile.delete()
            val failAck = JSONObject().apply {
                put("fileId", fileId)
                put("status", "HASH_MISMATCH")
            }
            BluetoothProtocol.writeJsonFrame(outStream, FrameType.FILE_ACK, failAck)
            Log.e(TAG, "SHA256 mismatch on received file from PC")
            return
        }

        if (finalFile.exists()) finalFile.delete()
        partFile.renameTo(finalFile)

        val okAck = JSONObject().apply {
            put("fileId", fileId)
            put("status", "OK")
            put("receivedSha256", computedSha256)
        }
        BluetoothProtocol.writeJsonFrame(outStream, FrameType.FILE_ACK, okAck)
        Log.i(TAG, "File successfully received and verified: ${finalFile.absolutePath}")

        onFileReceivedFromPc(finalFile)
    }

    /**
     * Sends a file to the paired Windows PC over Bluetooth RFCOMM.
     */
    @SuppressLint("MissingPermission")
    suspend fun sendFileToWindows(
        targetAddress: String,
        file: File,
        sha256: String
    ): Boolean = withContext(Dispatchers.IO) {
        if (adapter == null || !adapter.isEnabled) return@withContext false
        if (!file.exists()) return@withContext false

        var socket: BluetoothSocket? = null
        try {
            val device: BluetoothDevice = adapter.getRemoteDevice(targetAddress)
            Log.i(TAG, "Connecting to Windows PC ($targetAddress)...")

            socket = try {
                device.createRfcommSocketToServiceRecord(CUSTOM_SERVICE_UUID).also { it.connect() }
            } catch (e: IOException) {
                Log.w(TAG, "Custom UUID connection failed, attempting SPP fallback", e)
                device.createRfcommSocketToServiceRecord(SPP_UUID).also { it.connect() }
            }

            val activeSock = socket ?: return@withContext false
            val outStream = activeSock.outputStream
            val inStream = activeSock.inputStream

            // Send HELLO
            val hello = JSONObject().apply {
                put("clientName", "XycloTooth-Android")
                put("version", "1.0.0")
            }
            BluetoothProtocol.writeJsonFrame(outStream, FrameType.HELLO, hello)

            val helloAck = BluetoothProtocol.readFrame(inStream)
            if (helloAck == null || helloAck.type != FrameType.HELLO_ACK) {
                Log.w(TAG, "Handshake failed with Windows")
            }

            // Send FILE_START
            val fileId = UUID.randomUUID().toString()
            val fileSize = file.length()
            val totalChunks = ((fileSize + CHUNK_SIZE - 1) / CHUNK_SIZE).toInt()

            val startMeta = JSONObject().apply {
                put("fileId", fileId)
                put("filename", file.name)
                put("fileSize", fileSize)
                put("chunkSize", CHUNK_SIZE)
                put("totalChunks", totalChunks)
                put("sha256", sha256)
            }
            BluetoothProtocol.writeJsonFrame(outStream, FrameType.FILE_START, startMeta)

            // Await READY ACK
            val readyAck = BluetoothProtocol.readFrame(inStream)
            if (readyAck == null || readyAck.type != FrameType.FILE_ACK) {
                throw IOException("Windows PC did not return READY ACK")
            }

            // Stream chunks
            FileInputStream(file).use { fis ->
                val buffer = ByteArray(CHUNK_SIZE)
                var chunkIndex = 0
                var readBytes: Int

                while (fis.read(buffer).also { readBytes = it } != -1) {
                    val payload = ByteBuffer.allocate(4 + readBytes)
                    payload.putInt(chunkIndex)
                    payload.put(buffer, 0, readBytes)

                    BluetoothProtocol.writeFrame(outStream, FrameType.FILE_CHUNK, payload.array())
                    chunkIndex++
                }
            }

            // Send FILE_END
            val endMeta = JSONObject().apply {
                put("fileId", fileId)
                put("totalBytesSent", fileSize)
                put("sha256", sha256)
            }
            BluetoothProtocol.writeJsonFrame(outStream, FrameType.FILE_END, endMeta)

            // Await final ACK
            val finalAckFrame = BluetoothProtocol.readFrame(inStream)
            if (finalAckFrame == null || finalAckFrame.type != FrameType.FILE_ACK) {
                throw IOException("Windows PC did not return final FILE_ACK")
            }

            val finalAck = finalAckFrame.asJsonObject()
            val status = finalAck.optString("status")
            if (status != "OK") {
                Log.e(TAG, "Windows PC reported error: ${finalAck.optString("message")}")
                return@withContext false
            }

            Log.i(TAG, "File ${file.name} sent to Windows PC and verified successfully!")
            return@withContext true
        } catch (e: Exception) {
            Log.e(TAG, "Failed sending file to Windows PC", e)
            return@withContext false
        } finally {
            try { socket?.close() } catch (_: Exception) {}
        }
    }
}
