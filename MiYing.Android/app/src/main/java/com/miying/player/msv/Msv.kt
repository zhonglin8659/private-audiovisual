package com.miying.player.msv

import android.content.Context
import android.net.Uri
import android.os.ParcelFileDescriptor
import android.provider.OpenableColumns
import java.io.Closeable
import java.io.File
import java.io.FileInputStream
import java.io.IOException
import java.nio.ByteBuffer
import java.nio.ByteOrder
import javax.crypto.Cipher
import javax.crypto.SecretKeyFactory
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.PBEKeySpec
import javax.crypto.spec.SecretKeySpec

enum class MediaKind { Unknown, Photo, Video }

object MsvConstants {
    const val FIXED_HEADER_SIZE = 50
    const val VERSION = 1
    const val KDF_PBKDF2_SHA256 = 1
    const val CIPHER_AES_256_GCM = 1
    const val MIN_ITERATIONS = 100_000
    const val MAX_ITERATIONS = 5_000_000
    const val DEFAULT_ITERATIONS = 600_000
    const val ENHANCED_ITERATIONS = 1_200_000
    const val MIN_CHUNK_SIZE = 64 * 1024
    const val MAX_CHUNK_SIZE = 16 * 1024 * 1024
    const val DEFAULT_CHUNK_SIZE = 1024 * 1024
    const val SALT_SIZE = 16
    const val BASE_NONCE_SIZE = 8
    const val NONCE_SIZE = 12
    const val GCM_TAG_SIZE = 16
    const val NAME_MAX_BYTES = 1024
    val MAGIC = "MSV1".toByteArray(Charsets.US_ASCII)
}

open class MsvException(message: String, cause: Throwable? = null) : IOException(message, cause)
class MsvFormatException(message: String) : MsvException(message)
class MsvInvalidPasswordException(cause: Throwable? = null) :
    MsvException("密码不正确，或文件已损坏。", cause)

class MsvCorruptException(message: String, cause: Throwable? = null) : MsvException(message, cause)

data class MsvHeader(
    val version: Int,
    val kdfId: Int,
    val cipherId: Int,
    val flags: Int,
    val iterations: Int,
    val salt: ByteArray,
    val chunkSize: Int,
    val plainLength: Long,
    val baseNonce: ByteArray,
    val originalFileName: String,
    val bytes: ByteArray,
) {
    val headerSize: Int = bytes.size

    val chunkCount: Long
        get() = if (plainLength == 0L) 0L else (plainLength + chunkSize - 1) / chunkSize

    val expectedFileLength: Long
        get() = headerSize + plainLength + chunkCount * MsvConstants.GCM_TAG_SIZE

    companion object {
        fun parse(data: ByteArray, actualFileLength: Long): MsvHeader {
            if (data.size < MsvConstants.FIXED_HEADER_SIZE) {
                throw MsvFormatException("MSV 文件头不完整。")
            }

            val buffer = ByteBuffer.wrap(data).order(ByteOrder.BIG_ENDIAN)
            val magic = ByteArray(4).also { buffer.get(it) }
            if (!magic.contentEquals(MsvConstants.MAGIC)) {
                throw MsvFormatException("不是有效的 MSV 加密文件。")
            }

            val version = buffer.get().toInt() and 0xff
            val kdfId = buffer.get().toInt() and 0xff
            val cipherId = buffer.get().toInt() and 0xff
            val flags = buffer.get().toInt() and 0xff
            val iterations = buffer.int
            val salt = ByteArray(MsvConstants.SALT_SIZE).also { buffer.get(it) }
            val chunkSize = buffer.int
            val plainLength = buffer.long
            val baseNonce = ByteArray(MsvConstants.BASE_NONCE_SIZE).also { buffer.get(it) }
            val nameLength = buffer.short.toInt() and 0xffff

            if (version != MsvConstants.VERSION) throw MsvFormatException("不支持的 MSV 版本 $version。")
            if (kdfId != MsvConstants.KDF_PBKDF2_SHA256) throw MsvFormatException("不支持的 KDF。")
            if (cipherId != MsvConstants.CIPHER_AES_256_GCM) throw MsvFormatException("不支持的加密算法。")
            if (flags != 0) throw MsvFormatException("不支持的文件标志。")
            if (iterations < MsvConstants.MIN_ITERATIONS || iterations > MsvConstants.MAX_ITERATIONS) {
                throw MsvFormatException("KDF 迭代次数非法。")
            }
            if (chunkSize < MsvConstants.MIN_CHUNK_SIZE || chunkSize > MsvConstants.MAX_CHUNK_SIZE) {
                throw MsvFormatException("分块大小非法。")
            }
            if (plainLength < 0L) throw MsvFormatException("明文长度非法。")
            if (nameLength > MsvConstants.NAME_MAX_BYTES) throw MsvFormatException("原始文件名过长。")
            if (data.size != MsvConstants.FIXED_HEADER_SIZE + nameLength) {
                throw MsvFormatException("文件头不完整。")
            }

            val name = String(data, MsvConstants.FIXED_HEADER_SIZE, nameLength, Charsets.UTF_8)
            val header = MsvHeader(
                version, kdfId, cipherId, flags, iterations, salt, chunkSize,
                plainLength, baseNonce, name, data.copyOf(),
            )

            val chunkCount = header.chunkCount
            val expected = header.headerSize + plainLength + chunkCount * MsvConstants.GCM_TAG_SIZE
            if (actualFileLength < expected) throw MsvFormatException("文件不完整或已被截断。")
            if (actualFileLength > expected) throw MsvFormatException("文件尾部有多余数据。")
            return header
        }
    }
}

object MsvCrypto {
    fun deriveKey(password: String, header: MsvHeader): ByteArray {
        val spec = PBEKeySpec(password.toCharArray(), header.salt, header.iterations, 256)
        return try {
            val factory = SecretKeyFactory.getInstance("PBKDF2WithHmacSHA256")
            factory.generateSecret(spec).encoded
        } finally {
            spec.clearPassword()
        }
    }

    fun zero(value: ByteArray?) {
        value?.fill(0)
    }
}

object MsvNaming {
    private val photoExtensions = setOf(
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff",
        ".ico", ".webp", ".heic", ".heif", ".avif",
    )
    private val videoExtensions = setOf(
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".ts", ".flv", ".wmv",
        ".mpg", ".mpeg", ".3gp", ".rmvb",
    )

    fun extension(name: String?): String {
        val value = name ?: return ""
        val index = value.lastIndexOf('.')
        return if (index >= 0) value.substring(index).lowercase() else ""
    }

    fun detect(fileName: String?, encryptedName: String?): MediaKind {
        val original = extension(fileName)
        if (original in photoExtensions) return MediaKind.Photo
        if (original in videoExtensions) return MediaKind.Video

        val encrypted = extension(encryptedName)
        if (encrypted == ".msp") return MediaKind.Photo
        if (encrypted == ".msv") return MediaKind.Video

        // .msv 中未知原始扩展名交给文件签名判断，避免把照片误当视频。
        return MediaKind.Unknown
    }

    fun queryDisplayName(context: Context, uri: Uri): String {
        if (uri.scheme == "file") return uri.lastPathSegment ?: uri.toString()
        try {
            context.contentResolver.query(
                uri,
                arrayOf(OpenableColumns.DISPLAY_NAME),
                null,
                null,
                null,
            )?.use { cursor ->
                if (cursor.moveToFirst()) {
                    val index = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    if (index >= 0) return cursor.getString(index) ?: uri.lastPathSegment ?: uri.toString()
                }
            }
        } catch (_: Exception) {
        }
        return uri.lastPathSegment ?: uri.toString()
    }
}

object MediaSniffer {
    fun detect(header: ByteArray): MediaKind {
        if (header.size < 2) return MediaKind.Unknown

        if (header.size >= 3 && header[0] == 0xFF.toByte() && header[1] == 0xD8.toByte() && header[2] == 0xFF.toByte()) {
            return MediaKind.Photo
        }

        val png = byteArrayOf(0x89.toByte(), 0x50.toByte(), 0x4E.toByte(), 0x47.toByte(), 0x0D.toByte(), 0x0A.toByte(), 0x1A.toByte(), 0x0A.toByte())
        if (header.size >= png.size && header.copyOfRange(0, png.size).contentEquals(png)) {
            return MediaKind.Photo
        }

        if (header.size >= 6) {
            val gif = String(header, 0, 6, Charsets.US_ASCII)
            if (gif == "GIF87a" || gif == "GIF89a") return MediaKind.Photo
        }

        if (header[0] == 'B'.code.toByte() && header[1] == 'M'.code.toByte()) return MediaKind.Photo

        if (header.size >= 4) {
            val tiffLe = header[0] == 'I'.code.toByte() && header[1] == 'I'.code.toByte() &&
                header[2] == 0x2A.toByte() && header[3] == 0.toByte()
            val tiffBe = header[0] == 'M'.code.toByte() && header[1] == 'M'.code.toByte() &&
                header[2] == 0.toByte() && header[3] == 0x2A.toByte()
            if (tiffLe || tiffBe) return MediaKind.Photo
        }

        if (header.size >= 4 && header[0] == 0.toByte() && header[1] == 0.toByte() &&
            header[2] == 1.toByte() && header[3] == 0.toByte()
        ) {
            return MediaKind.Photo
        }

        if (header.size >= 12 && isAscii(header, 0, "RIFF") && isAscii(header, 8, "WEBP")) {
            return MediaKind.Photo
        }

        if (header.size >= 12 && isAscii(header, 4, "ftyp")) {
            val brand = String(header, 8, 4, Charsets.US_ASCII)
            return when (brand) {
                "heic", "heix", "hevc", "hevx", "heim", "heis", "hevm", "hevs",
                "mif1", "msf1", "avif", "avis" -> MediaKind.Photo
                else -> MediaKind.Video
            }
        }

        return MediaKind.Unknown
    }

    private fun isAscii(data: ByteArray, offset: Int, value: String): Boolean {
        if (data.size < offset + value.length) return false
        for (i in value.indices) {
            if (data[offset + i] != value[i].code.toByte()) return false
        }
        return true
    }
}