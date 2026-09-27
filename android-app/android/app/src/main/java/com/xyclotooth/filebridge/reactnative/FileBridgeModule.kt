package com.xyclotooth.filebridge.reactnative

import android.bluetooth.BluetoothAdapter
import android.content.Intent
import android.os.Build
import com.facebook.react.bridge.*
import com.facebook.react.modules.core.DeviceEventManagerModule
import com.xyclotooth.filebridge.bluetooth.AndroidBluetoothManager
import com.xyclotooth.filebridge.service.FileBridgeForegroundService
import com.xyclotooth.filebridge.storage.AppDatabase
import com.xyclotooth.filebridge.storage.SettingsRepository
import com.xyclotooth.filebridge.storage.TransferState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File

class FileBridgeModule(private val reactContext: ReactApplicationContext) :
    ReactContextBaseJavaModule(reactContext) {

    private val moduleScope = CoroutineScope(Dispatchers.Main)
    private val db = AppDatabase.getInstance(reactContext)
    private val settings = SettingsRepository(reactContext)

    override fun getName(): String = "FileBridgeModule"

    private fun sendEvent(eventName: String, params: WritableMap?) {
        if (reactContext.hasActiveReactInstance()) {
            reactContext
                .getJSModule(DeviceEventManagerModule.RCTDeviceEventEmitter::class.java)
                .emit(eventName, params)
        }
    }

    @ReactMethod
    fun startService(promise: Promise) {
        try {
            val intent = Intent(reactContext, FileBridgeForegroundService::class.java).apply {
                action = FileBridgeForegroundService.ACTION_START
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                reactContext.startForegroundService(intent)
            } else {
                reactContext.startService(intent)
            }
            promise.resolve(true)
        } catch (e: Exception) {
            promise.reject("START_SERVICE_ERROR", e.message, e)
        }
    }

    @ReactMethod
    fun stopService(promise: Promise) {
        try {
            val intent = Intent(reactContext, FileBridgeForegroundService::class.java).apply {
                action = FileBridgeForegroundService.ACTION_STOP
            }
            reactContext.startService(intent)
            promise.resolve(true)
        } catch (e: Exception) {
            promise.reject("STOP_SERVICE_ERROR", e.message, e)
        }
    }

    @ReactMethod
    fun getServiceStatus(promise: Promise) {
        val map = Arguments.createMap()
        map.putBoolean("isRunning", FileBridgeForegroundService.isRunning)
        map.putString("selectedDeviceAddress", settings.pairedDeviceAddress)
        map.putString("selectedDeviceName", settings.pairedDeviceName)
        map.putString("serverUrl", settings.serverUrl)
        map.putString("incomingDirectory", settings.incomingDirectory)
        promise.resolve(map)
    }

    @ReactMethod
    fun getPairedDevices(promise: Promise) {
        try {
            val adapter = BluetoothAdapter.getDefaultAdapter()
            val array = Arguments.createArray()
            if (adapter != null && adapter.isEnabled) {
                for (dev in adapter.bondedDevices) {
                    val map = Arguments.createMap()
                    map.putString("name", dev.name ?: "Unknown Device")
                    map.putString("address", dev.address)
                    array.pushMap(map)
                }
            }
            promise.resolve(array)
        } catch (e: SecurityException) {
            promise.reject("PERMISSION_DENIED", "Bluetooth permission not granted", e)
        } catch (e: Exception) {
            promise.reject("GET_DEVICES_ERROR", e.message, e)
        }
    }

    @ReactMethod
    fun selectBluetoothDevice(address: String, name: String, promise: Promise) {
        settings.pairedDeviceAddress = address
        settings.pairedDeviceName = name
        promise.resolve(true)
    }

    @ReactMethod
    fun getSettings(promise: Promise) {
        val map = Arguments.createMap().apply {
            putString("serverUrl", settings.serverUrl)
            putString("apiToken", settings.apiToken)
            putString("pairedDeviceAddress", settings.pairedDeviceAddress)
            putString("pairedDeviceName", settings.pairedDeviceName)
            putString("incomingDirectory", settings.incomingDirectory)
            putString("responseDirectory", settings.responseDirectory)
            putBoolean("autoUpload", settings.autoUpload)
            putBoolean("autoSendResponse", settings.autoSendResponse)
            putInt("retryCount", settings.retryCount)
            putInt("retryDelaySeconds", settings.retryDelaySeconds)
        }
        promise.resolve(map)
    }

    @ReactMethod
    fun saveSettings(map: ReadableMap, promise: Promise) {
        try {
            if (map.hasKey("serverUrl")) settings.serverUrl = map.getString("serverUrl") ?: settings.serverUrl
            if (map.hasKey("apiToken")) settings.apiToken = map.getString("apiToken") ?: settings.apiToken
            if (map.hasKey("incomingDirectory")) settings.incomingDirectory = map.getString("incomingDirectory") ?: settings.incomingDirectory
            if (map.hasKey("responseDirectory")) settings.responseDirectory = map.getString("responseDirectory") ?: settings.responseDirectory
            if (map.hasKey("autoUpload")) settings.autoUpload = map.getBoolean("autoUpload")
            if (map.hasKey("autoSendResponse")) settings.autoSendResponse = map.getBoolean("autoSendResponse")
            if (map.hasKey("retryCount")) settings.retryCount = map.getInt("retryCount")
            if (map.hasKey("retryDelaySeconds")) settings.retryDelaySeconds = map.getInt("retryDelaySeconds")
            promise.resolve(true)
        } catch (e: Exception) {
            promise.reject("SAVE_SETTINGS_ERROR", e.message, e)
        }
    }

    @ReactMethod
    fun getRecentTransfers(limit: Int, promise: Promise) {
        moduleScope.launch(Dispatchers.IO) {
            try {
                val list = db.transferDao().getRecentTransfers(if (limit <= 0) 50 else limit)
                val array = Arguments.createArray()
                for (item in list) {
                    val map = Arguments.createMap().apply {
                        putString("id", item.id)
                        putString("filename", item.filename)
                        putDouble("fileSize", item.fileSize.toDouble())
                        putString("sha256", item.sha256Hash)
                        putString("state", item.state.name)
                        putString("direction", item.direction)
                        putString("requestId", item.requestId)
                        putString("responseFilename", item.responseFilename)
                        putInt("retryCount", item.retryCount)
                        putString("lastError", item.lastError)
                        putDouble("createdAt", item.createdAt.toDouble())
                    }
                    array.pushMap(map)
                }
                withContext(Dispatchers.Main) {
                    promise.resolve(array)
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) {
                    promise.reject("GET_TRANSFERS_ERROR", e.message, e)
                }
            }
        }
    }

    @ReactMethod
    fun retryTransfer(id: String, promise: Promise) {
        moduleScope.launch(Dispatchers.IO) {
            try {
                val item = db.transferDao().getById(id)
                if (item != null) {
                    val nextState = if (item.responseFilePath != null) TransferState.BLUETOOTH_SEND_PENDING else TransferState.READY
                    db.transferDao().updateState(id, nextState, null)
                    withContext(Dispatchers.Main) { promise.resolve(true) }
                } else {
                    withContext(Dispatchers.Main) { promise.reject("NOT_FOUND", "Transfer not found") }
                }
            } catch (e: Exception) {
                withContext(Dispatchers.Main) { promise.reject("RETRY_ERROR", e.message, e) }
            }
        }
    }

    // React Native NativeEventEmitter compatibility
    @ReactMethod
    fun addListener(eventName: String) {}

    @ReactMethod
    fun removeListeners(count: Int) {}
}
