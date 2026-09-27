package com.xyclotooth.filebridge.network

import android.util.Log
import com.xyclotooth.filebridge.filesystem.FileStabilityChecker
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.asRequestBody
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.util.concurrent.TimeUnit

data class UploadResult(
    val success: Boolean,
    val responseFile: File? = null,
    val responseFilename: String? = null,
    val responseSha256: String? = null,
    val error: String? = null
)

class ApiClient {
    private val client = OkHttpClient.Builder()
        .connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .retryOnConnectionFailure(true)
        .build()

    companion object {
        private const val TAG = "ApiClient"
    }

    /**
     * Uploads the given text file to the configured server using HTTPS POST.
     * Correlates with requestId. Saves server response to responseDir.
     */
    suspend fun uploadFile(
        serverBaseUrl: String,
        apiToken: String,
        file: File,
        requestId: String,
        responseDir: File
    ): UploadResult = withContext(Dispatchers.IO) {
        val uploadUrl = "${serverBaseUrl.trimEnd('/')}/api/upload"
        Log.i(TAG, "Uploading ${file.name} to $uploadUrl (RequestId: $requestId)")

        val requestBody = MultipartBody.Builder()
            .setType(MultipartBody.FORM)
            .addFormDataPart(
                "file",
                file.name,
                file.asRequestBody("text/plain".toMediaType())
            )
            .build()

        val requestBuilder = Request.Builder()
            .url(uploadUrl)
            .addHeader("X-Request-ID", requestId)
            .post(requestBody)

        if (apiToken.isNotBlank()) {
            requestBuilder.addHeader("Authorization", "Bearer $apiToken")
        }

        try {
            val response = client.newCall(requestBuilder.build()).execute()
            response.use { resp ->
                if (!resp.isSuccessful) {
                    val errMsg = "Server responded with HTTP ${resp.code}: ${resp.message}"
                    Log.e(TAG, errMsg)
                    return@withContext UploadResult(success = false, error = errMsg)
                }

                val body = resp.body ?: return@withContext UploadResult(success = false, error = "Empty server response body")
                val contentType = resp.header("Content-Type") ?: ""

                // Check if server returned JSON with download URL
                if (contentType.contains("application/json")) {
                    val jsonStr = body.string()
                    val json = JSONObject(jsonStr)
                    if (json.has("downloadUrl")) {
                        val downloadUrl = json.getString("downloadUrl")
                        val fullDownloadUrl = if (downloadUrl.startsWith("http")) downloadUrl else "${serverBaseUrl.trimEnd('/')}${downloadUrl}"
                        val filename = json.optString("filename", "response_${file.name}")
                        return@withContext downloadResponseFile(fullDownloadUrl, apiToken, requestId, filename, responseDir)
                    }
                }

                // Direct file response (text/plain or attachment)
                val contentDisposition = resp.header("Content-Disposition") ?: ""
                var responseFilename = "response_${file.name}"
                if (contentDisposition.contains("filename=")) {
                    val extracted = contentDisposition.substringAfter("filename=").replace("\"", "").trim()
                    if (extracted.isNotBlank()) responseFilename = extracted
                }

                if (!responseDir.exists()) responseDir.mkdirs()
                val partFile = File(responseDir, "$responseFilename.$requestId.part")
                val finalFile = File(responseDir, responseFilename)

                FileOutputStream(partFile).use { fos ->
                    body.byteStream().copyTo(fos)
                    fos.flush()
                }

                val computedSha256 = FileStabilityChecker.computeSha256(partFile)
                if (finalFile.exists()) finalFile.delete()
                partFile.renameTo(finalFile)

                Log.i(TAG, "Server response successfully received: ${finalFile.name} (SHA-256: $computedSha256)")
                return@withContext UploadResult(
                    success = true,
                    responseFile = finalFile,
                    responseFilename = finalFile.name,
                    responseSha256 = computedSha256
                )
            }
        } catch (e: IOException) {
            Log.e(TAG, "Network I/O error during upload", e)
            return@withContext UploadResult(success = false, error = "Network error: ${e.message}")
        } catch (e: Exception) {
            Log.e(TAG, "Unexpected upload exception", e)
            return@withContext UploadResult(success = false, error = e.message)
        }
    }

    private fun downloadResponseFile(
        url: String,
        apiToken: String,
        requestId: String,
        filename: String,
        responseDir: File
    ): UploadResult {
        val requestBuilder = Request.Builder().url(url).addHeader("X-Request-ID", requestId)
        if (apiToken.isNotBlank()) {
            requestBuilder.addHeader("Authorization", "Bearer $apiToken")
        }

        val resp = client.newCall(requestBuilder.build()).execute()
        resp.use { response ->
            if (!response.isSuccessful) {
                return UploadResult(success = false, error = "Download failed: HTTP ${response.code}")
            }
            val body = response.body ?: return UploadResult(success = false, error = "Empty download body")
            if (!responseDir.exists()) responseDir.mkdirs()

            val partFile = File(responseDir, "$filename.$requestId.part")
            val finalFile = File(responseDir, filename)

            FileOutputStream(partFile).use { fos ->
                body.byteStream().copyTo(fos)
                fos.flush()
            }

            val sha256 = FileStabilityChecker.computeSha256(partFile)
            if (finalFile.exists()) finalFile.delete()
            partFile.renameTo(finalFile)

            return UploadResult(
                success = true,
                responseFile = finalFile,
                responseFilename = finalFile.name,
                responseSha256 = sha256
            )
        }
    }
}
