package com.xyclotooth.filebridge.filesystem

import kotlinx.coroutines.delay
import java.io.File
import java.io.FileInputStream
import java.io.IOException
import java.security.MessageDigest

object FileStabilityChecker {

    /**
     * Checks if a file exists, is non-empty, has stopped growing across check intervals,
     * and can be opened for reading.
     */
    suspend fun waitForStability(
        file: File,
        timeoutMs: Long = 6000L,
        checkIntervalMs: Long = 800L
    ): Boolean {
        val startTime = System.currentTimeMillis()
        var lastSize = -1L

        while (System.currentTimeMillis() - startTime < timeoutMs) {
            if (!file.exists()) return false

            val currentLength = file.length()
            if (currentLength > 0 && currentLength == lastSize) {
                // Verify file lock has been released and file is readable
                if (canReadFile(file)) {
                    return true
                }
            }

            lastSize = currentLength
            delay(checkIntervalMs)
        }

        return false
    }

    private fun canReadFile(file: File): Boolean {
        return try {
            FileInputStream(file).use { stream ->
                val buffer = ByteArray(1024)
                stream.read(buffer)
            }
            true
        } catch (e: IOException) {
            false
        }
    }

    /**
     * Computes the SHA-256 hash of a file.
     */
    fun computeSha256(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        FileInputStream(file).use { fis ->
            val buffer = ByteArray(8192)
            var bytesRead: Int
            while (fis.read(buffer).also { bytesRead = it } != -1) {
                digest.update(buffer, 0, bytesRead)
            }
        }
        val hashBytes = digest.digest()
        return hashBytes.joinToString("") { "%02x".format(it) }
    }
}
