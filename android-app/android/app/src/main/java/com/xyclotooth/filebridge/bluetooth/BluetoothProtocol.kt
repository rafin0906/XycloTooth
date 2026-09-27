package com.xyclotooth.filebridge.bluetooth

import org.json.JSONObject
import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.zip.CRC32

enum class FrameType(val code: Byte) {
    HELLO(0x01),
    HELLO_ACK(0x02),
    FILE_START(0x10),
    FILE_CHUNK(0x11),
    FILE_END(0x12),
    FILE_ACK(0x13),
    PING(0x20),
    PONG(0x21),
    ERROR(0xFF.toByte());

    companion object {
        fun fromCode(code: Byte): FrameType? = entries.firstOrNull { it.code == code }
    }
}

data class ProtocolFrame(
    val type: FrameType,
    val payload: ByteArray
) {
    fun asUtf8(): String = String(payload, Charsets.UTF_8)
    fun asJsonObject(): JSONObject = JSONObject(asUtf8())

    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (javaClass != other?.javaClass) return false
        other as ProtocolFrame
        return type == other.type && payload.contentEquals(other.payload)
    }

    override fun hashCode(): Int {
        var result = type.hashCode()
        result = 31 * result + payload.contentHashCode()
        return result
    }
}

object BluetoothProtocol {
    const val MAGIC_0: Byte = 0x58 // 'X'
    const val MAGIC_1: Byte = 0x54 // 'T'
    const val PROTOCOL_VERSION: Byte = 0x01
    const val HEADER_SIZE = 8
    const val CHECKSUM_SIZE = 4

    /**
     * Reads a full validated frame from InputStream.
     * Returns null if stream closed or CRC32/framing validation failed.
     */
    fun readFrame(inputStream: InputStream): ProtocolFrame? {
        val header = ByteArray(HEADER_SIZE)
        if (!readExact(inputStream, header, HEADER_SIZE)) return null

        if (header[0] != MAGIC_0 || header[1] != MAGIC_1) {
            return null // Framing error
        }

        if (header[2] != PROTOCOL_VERSION) {
            return null // Version mismatch
        }

        val frameType = FrameType.fromCode(header[3]) ?: return null
        val payloadLength = ByteBuffer.wrap(header, 4, 4).order(ByteOrder.BIG_ENDIAN).int

        if (payloadLength < 0 || payloadLength > 10 * 1024 * 1024) {
            return null // Exceeds safety threshold
        }

        val payload = ByteArray(payloadLength)
        if (!readExact(inputStream, payload, payloadLength)) return null

        val checksumBytes = ByteArray(CHECKSUM_SIZE)
        if (!readExact(inputStream, checksumBytes, CHECKSUM_SIZE)) return null

        val expectedCrc = ByteBuffer.wrap(checksumBytes).order(ByteOrder.BIG_ENDIAN).int.toLong() and 0xFFFFFFFFL

        val crc = CRC32()
        crc.update(header)
        crc.update(payload)

        if (crc.value != expectedCrc) {
            return null // CRC mismatch
        }

        return ProtocolFrame(frameType, payload)
    }

    /**
     * Writes a structured frame with length prefix and CRC32 to OutputStream.
     */
    fun writeFrame(outputStream: OutputStream, type: FrameType, payload: ByteArray) {
        val payloadLength = payload.size
        val totalLength = HEADER_SIZE + payloadLength + CHECKSUM_SIZE
        val buffer = ByteBuffer.allocate(totalLength).order(ByteOrder.BIG_ENDIAN)

        // Header
        buffer.put(MAGIC_0)
        buffer.put(MAGIC_1)
        buffer.put(PROTOCOL_VERSION)
        buffer.put(type.code)
        buffer.putInt(payloadLength)

        // Payload
        if (payloadLength > 0) {
            buffer.put(payload)
        }

        // Calculate CRC32 over header + payload
        val crc = CRC32()
        crc.update(buffer.array(), 0, HEADER_SIZE + payloadLength)
        buffer.putInt((crc.value and 0xFFFFFFFFL).toInt())

        outputStream.write(buffer.array())
        outputStream.flush()
    }

    fun writeJsonFrame(outputStream: OutputStream, type: FrameType, json: JSONObject) {
        val bytes = json.toString().toByteArray(Charsets.UTF_8)
        writeFrame(outputStream, type, bytes)
    }

    private fun readExact(inputStream: InputStream, buffer: ByteArray, length: Int): Boolean {
        var offset = 0
        while (offset < length) {
            val count = inputStream.read(buffer, offset, length - offset)
            if (count < 0) return false // End of stream
            offset += count
        }
        return true
    }
}
