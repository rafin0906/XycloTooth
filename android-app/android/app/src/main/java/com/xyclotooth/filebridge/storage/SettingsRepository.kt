package com.xyclotooth.filebridge.storage

import android.content.Context
import android.content.SharedPreferences
import java.io.File

class SettingsRepository(context: Context) {
    private val prefs: SharedPreferences = context.getSharedPreferences("xyclotooth_settings", Context.MODE_PRIVATE)

    companion object {
        private const val KEY_SERVER_URL = "server_url"
        private const val KEY_API_TOKEN = "api_token"
        private const val KEY_DEVICE_ADDRESS = "device_address"
        private const val KEY_DEVICE_NAME = "device_name"
        private const val KEY_INCOMING_DIR = "incoming_dir"
        private const val KEY_RESPONSE_DIR = "response_dir"
        private const val KEY_AUTO_UPLOAD = "auto_upload"
        private const val KEY_AUTO_SEND_RESPONSE = "auto_send_response"
        private const val KEY_RETRY_COUNT = "retry_count"
        private const val KEY_RETRY_DELAY = "retry_delay"
    }

    var serverUrl: String
        get() = prefs.getString(KEY_SERVER_URL, "http://10.0.2.2:3000") ?: "http://10.0.2.2:3000"
        set(value) = prefs.edit().putString(KEY_SERVER_URL, value).apply()

    var apiToken: String
        get() = prefs.getString(KEY_API_TOKEN, "xyclo-secret-token-2026") ?: "xyclo-secret-token-2026"
        set(value) = prefs.edit().putString(KEY_API_TOKEN, value).apply()

    var pairedDeviceAddress: String
        get() = prefs.getString(KEY_DEVICE_ADDRESS, "") ?: ""
        set(value) = prefs.edit().putString(KEY_DEVICE_ADDRESS, value).apply()

    var pairedDeviceName: String
        get() = prefs.getString(KEY_DEVICE_NAME, "") ?: ""
        set(value) = prefs.edit().putString(KEY_DEVICE_NAME, value).apply()

    var incomingDirectory: String
        get() {
            val def = File(context.getExternalFilesDir(null), "input").absolutePath
            return prefs.getString(KEY_INCOMING_DIR, def) ?: def
        }
        set(value) = prefs.edit().putString(KEY_INCOMING_DIR, value).apply()

    var responseDirectory: String
        get() {
            val def = File(context.getExternalFilesDir(null), "responses").absolutePath
            return prefs.getString(KEY_RESPONSE_DIR, def) ?: def
        }
        set(value) = prefs.edit().putString(KEY_RESPONSE_DIR, value).apply()

    var autoUpload: Boolean
        get() = prefs.getBoolean(KEY_AUTO_UPLOAD, true)
        set(value) = prefs.edit().putBoolean(KEY_AUTO_UPLOAD, value).apply()

    var autoSendResponse: Boolean
        get() = prefs.getBoolean(KEY_AUTO_SEND_RESPONSE, true)
        set(value) = prefs.edit().putBoolean(KEY_AUTO_SEND_RESPONSE, value).apply()

    var retryCount: Int
        get() = prefs.getInt(KEY_RETRY_COUNT, 3)
        set(value) = prefs.edit().putInt(KEY_RETRY_COUNT, value).apply()

    var retryDelaySeconds: Int
        get() = prefs.getInt(KEY_RETRY_DELAY, 3)
        set(value) = prefs.edit().putInt(KEY_RETRY_DELAY, value).apply()

    private val context: Context = context.applicationContext

    init {
        // Ensure default directories exist
        File(incomingDirectory).mkdirs()
        File(responseDirectory).mkdirs()
    }
}
