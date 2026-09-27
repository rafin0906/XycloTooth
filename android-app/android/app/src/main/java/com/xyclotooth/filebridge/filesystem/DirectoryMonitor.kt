package com.xyclotooth.filebridge.filesystem

import android.os.Build
import android.os.FileObserver
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import java.io.File
import java.util.concurrent.ConcurrentHashMap

class DirectoryMonitor(
    private val scope: CoroutineScope,
    private val onFileStable: (file: File) -> Unit
) {
    private var observer: FileObserver? = null
    private val processingFiles = ConcurrentHashMap<String, Long>()

    companion object {
        private const val TAG = "DirectoryMonitor"
        private const val WATCH_FLAGS = FileObserver.CREATE or
                FileObserver.MODIFY or
                FileObserver.MOVED_TO or
                FileObserver.CLOSE_WRITE
    }

    fun startMonitoring(path: String) {
        stopMonitoring()
        val dir = File(path)
        if (!dir.exists()) dir.mkdirs()

        Log.i(TAG, "Starting FileObserver on: $path")

        observer = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            object : FileObserver(dir, WATCH_FLAGS) {
                override fun onEvent(event: Int, name: String?) {
                    handleEvent(event, dir, name)
                }
            }
        } else {
            @Suppress("DEPRECATION")
            object : FileObserver(dir.absolutePath, WATCH_FLAGS) {
                override fun onEvent(event: Int, name: String?) {
                    handleEvent(event, dir, name)
                }
            }
        }

        observer?.startWatching()
    }

    fun stopMonitoring() {
        observer?.stopWatching()
        observer = null
        Log.i(TAG, "Stopped FileObserver.")
    }

    private fun handleEvent(event: Int, parentDir: File, name: String?) {
        if (name == null) return
        if (!name.endsWith(".txt", ignoreCase = true)) return
        if (name.endsWith(".part", ignoreCase = true)) return

        val file = File(parentDir, name)
        val path = file.absolutePath

        // Deduplicate rapid consecutive event bursts within 2 seconds
        val lastEventTime = processingFiles[path] ?: 0L
        val now = System.currentTimeMillis()
        if (now - lastEventTime < 2000L) {
            return
        }
        processingFiles[path] = now

        scope.launch(Dispatchers.IO) {
            Log.d(TAG, "File event (0x${Integer.toHexString(event)}) detected on: $name. Awaiting stability...")
            val isStable = FileStabilityChecker.waitForStability(file)
            if (isStable) {
                Log.i(TAG, "File confirmed complete and stable: $name (${file.length()} bytes)")
                onFileStable(file)
            } else {
                Log.w(TAG, "File stability verification failed or timed out: $name")
            }
            processingFiles.remove(path)
        }
    }
}
