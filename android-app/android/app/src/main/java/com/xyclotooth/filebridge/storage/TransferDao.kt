package com.xyclotooth.filebridge.storage

import androidx.room.*

@Dao
interface TransferDao {
    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(transfer: TransferEntity)

    @Update
    suspend fun update(transfer: TransferEntity)

    @Query("SELECT * FROM transfers WHERE id = :id LIMIT 1")
    suspend fun getById(id: String): TransferEntity?

    @Query("SELECT * FROM transfers WHERE sha256Hash = :hash AND state != 'FAILED' AND createdAt > :sinceTimestamp LIMIT 1")
    suspend fun getRecentByHash(hash: String, sinceTimestamp: Long): TransferEntity?

    @Query("SELECT * FROM transfers WHERE state IN ('READY', 'UPLOADING') ORDER BY createdAt ASC")
    suspend fun getPendingUploads(): List<TransferEntity>

    @Query("SELECT * FROM transfers WHERE state IN ('RESPONSE_RECEIVED', 'BLUETOOTH_SEND_PENDING') ORDER BY createdAt ASC")
    suspend fun getPendingBluetoothSends(): List<TransferEntity>

    @Query("SELECT * FROM transfers ORDER BY createdAt DESC LIMIT :limit")
    suspend fun getRecentTransfers(limit: Int = 50): List<TransferEntity>

    @Query("UPDATE transfers SET state = :newState, lastError = :error, updatedAt = :updatedAt WHERE id = :id")
    suspend fun updateState(id: String, newState: TransferState, error: String? = null, updatedAt: Long = System.currentTimeMillis())

    @Query("UPDATE transfers SET retryCount = retryCount + 1, updatedAt = :updatedAt WHERE id = :id")
    suspend fun incrementRetry(id: String, updatedAt: Long = System.currentTimeMillis())

    @Query("DELETE FROM transfers WHERE createdAt < :beforeTimestamp")
    suspend fun deleteOldTransfers(beforeTimestamp: Long)
}
