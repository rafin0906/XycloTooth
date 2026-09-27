package com.xyclotooth.filebridge.storage

import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

enum class TransferState {
    DETECTED,
    WAITING_FOR_STABILITY,
    READY,
    UPLOADING,
    UPLOADED,
    WAITING_FOR_RESPONSE,
    RESPONSE_RECEIVED,
    BLUETOOTH_SEND_PENDING,
    BLUETOOTH_SENDING,
    SENT_TO_PC,
    FAILED
}

@Entity(
    tableName = "transfers",
    indices = [
        Index("state"),
        Index("sha256Hash"),
        Index("requestId")
    ]
)
data class TransferEntity(
    @PrimaryKey
    val id: String, // UUID
    val filename: String,
    val localFilePath: String,
    val fileSize: Long,
    val sha256Hash: String,
    val direction: String, // 'INBOUND_PC' or 'OUTBOUND_PC'
    val state: TransferState,
    val requestId: String,
    val responseFilename: String? = null,
    val responseFilePath: String? = null,
    val responseSha256: String? = null,
    val retryCount: Int = 0,
    val lastError: String? = null,
    val createdAt: Long = System.currentTimeMillis(),
    val updatedAt: Long = System.currentTimeMillis()
)
