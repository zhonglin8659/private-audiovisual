package com.miying.player.msv

import android.content.Context
import android.net.Uri
import android.os.ParcelFileDescriptor
import java.io.Closeable
import java.io.File
import java.io.FileInputStream
import java.io.IOException
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec

class MsvFileSource : Closeable {
    lateinit var context: Context
    lateinit var uri: Uri
    private var pfd: ParcelFileDescriptor? = null
    private var stream: FileInputStream? = null
    private var channel: java.nio.channels.FileChannel? = null

    fun open(context: Context, uri: Uri) {
        this.context = context
        this.uri = uri
        if (uri.scheme == "file") {
            val file = File(uri.path ?: throw MsvFormatException("无效文件路径。"))
            val input = FileInputStream(file)
            stream = input
            channel = input.channel
        } else {
            val descriptor = context.contentResolver.openFileDescriptor(uri, "r")
                ?: throw IOException("无法打开所选文件。")
            pfd = descriptor
            val input = FileInputStream(descriptor.fileDescriptor)
            stream = input
            channel = input.channel
        }
    }

    val length: Long
        get() {
            pfd?.statSize?.takeIf { it >= 0 }?.let { return it }
            return channel?.size() ?: 0L
        }

    fun readAt(position: Long, target: ByteArray, offset: Int, length: Int): Int {
        val source = channel ?: throw IOException("文件未打开。")
        source.position(position)
        return source.read(java.nio.ByteBuffer.wrap(target, offset, length))
    }

    override fun close() {
        channel?.close()
        stream?.close()
        try { pfd?.close() } catch (_: Exception) {}
        channel = null
        stream = null
        pfd = null
    }
}

class MsvReader(
    private val source: MsvFileSource,
    private val header: MsvHeader,
    key: ByteArray,
) : Closeable {
    val uri: Uri
        get() = source.uri
    private val key: ByteArray = key.copyOf()
    private val cache = LinkedHashMap<Long, ByteArray>()
    private val cacheLimit = 8
    private var closed = false

    init {
        if (header.plainLength > 0L) {
            decryptChunk(0)
        }
    }

    val plainLength: Long get() = header.plainLength

    fun readAt(position: Long, output: ByteArray, outputOffset: Int, outputLength: Int): Int {
        check(!closed) { "Reader 已关闭。" }
        if (position < 0 || outputOffset < 0 || outputLength < 0 ||
            outputOffset + outputLength > output.size
        ) {
            throw MsvFormatException("读取参数非法。")
        }
        if (position >= header.plainLength) return -1
        val wanted = minOf(outputLength.toLong(), header.plainLength - position).toInt()
        var copied = 0
        var sourcePosition = position
        while (copied < wanted) {
            val chunkIndex = sourcePosition / header.chunkSize
            val plain = decryptChunk(chunkIndex)
            val plainOffset = (sourcePosition - chunkIndex * header.chunkSize).toInt()
            val amount = minOf(wanted - copied, plain.size - plainOffset)
            System.arraycopy(plain, plainOffset, output, outputOffset + copied, amount)
            copied += amount
            sourcePosition += amount
        }
        return wanted
    }

    fun readAll(): ByteArray {
        val output = ByteArray(header.plainLength.toInt())
        var offset = 0
        while (offset < output.size) {
            val read = readAt(offset.toLong(), output, offset, output.size - offset)
            if (read < 0) break
            offset += read
        }
        return output
    }

    private fun decryptChunk(index: Long): ByteArray {
        cache[index]?.let { return it }
        if (index < 0 || index >= header.chunkCount) {
            throw MsvCorruptException("分块索引超出文件范围。")
        }
        val cipherOffset = header.headerSize + index * (header.chunkSize + MsvConstants.GCM_TAG_SIZE)
        val plainLength = minOf(header.chunkSize.toLong(), header.plainLength - index * header.chunkSize).toInt()
        val packet = ByteArray(plainLength + MsvConstants.GCM_TAG_SIZE)
        var read = 0
        while (read < packet.size) {
            val n = source.readAt(cipherOffset + read, packet, read, packet.size - read)
            if (n <= 0) throw MsvCorruptException("加密文件提前结束。")
            read += n
        }

        val nonce = ByteArray(MsvConstants.NONCE_SIZE)
        header.baseNonce.copyInto(nonce, 0, 0, MsvConstants.BASE_NONCE_SIZE)
        nonce[MsvConstants.BASE_NONCE_SIZE] = (index ushr 24).toByte()
        nonce[MsvConstants.BASE_NONCE_SIZE + 1] = (index ushr 16).toByte()
        nonce[MsvConstants.BASE_NONCE_SIZE + 2] = (index ushr 8).toByte()
        nonce[MsvConstants.BASE_NONCE_SIZE + 3] = index.toByte()

        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, javax.crypto.spec.SecretKeySpec(key, "AES"), GCMParameterSpec(128, nonce))
        cipher.updateAAD(header.bytes)
        val plain = try {
            cipher.doFinal(packet)
        } catch (cause: Exception) {
            if (index == 0L) throw MsvInvalidPasswordException(cause)
            throw MsvCorruptException("加密分块校验失败。", cause)
        }
        java.util.Arrays.fill(packet, 0)
        cache[index] = plain
        if (cache.size > cacheLimit) {
            val oldest = cache.keys.firstOrNull()
            oldest?.let { cache.remove(it) }
        }
        return plain
    }

    override fun close() {
        if (!closed) {
            closed = true
            for (value in cache.values) value.fill(0)
            cache.clear()
            MsvCrypto.zero(key)
            source.close()
        }
    }
}

class MsvMedia private constructor(
    val source: MsvFileSource,
    val header: MsvHeader,
    private val key: ByteArray,
    val kind: MediaKind,
) : Closeable {
    val uri: Uri
        get() = source.uri
    val displayName: String = header.originalFileName

    fun openReader(): MsvReader {
        val newSource = MsvFileSource()
        return try {
            newSource.open(source.context, source.uri)
            MsvReader(newSource, header, key)
        } catch (error: Throwable) {
            newSource.close()
            throw error
        }
    }

    fun readAllPlainText(): ByteArray = openReader().use { it.readAll() }

    override fun close() {
        source.close()
        MsvCrypto.zero(key)
    }

    companion object {
        fun open(context: Context, uri: Uri, password: String): MsvMedia {
            val source = MsvFileSource()
            try {
                source.open(context, uri)
                val fileLength = source.length
                if (fileLength < MsvConstants.FIXED_HEADER_SIZE) {
                    throw MsvFormatException("MSV 文件头不完整。")
                }
                val fixed = ByteArray(MsvConstants.FIXED_HEADER_SIZE)
                var read = 0
                while (read < fixed.size) {
                    val n = source.readAt(read.toLong(), fixed, read, fixed.size - read)
                    if (n <= 0) throw MsvFormatException("MSV 文件头不完整。")
                    read += n
                }
                val buffer = java.nio.ByteBuffer.wrap(fixed).order(java.nio.ByteOrder.BIG_ENDIAN)
                buffer.position(MsvConstants.FIXED_HEADER_SIZE - 2)
                val nameLength = buffer.short.toInt() and 0xffff
                if (nameLength > MsvConstants.NAME_MAX_BYTES) throw MsvFormatException("原始文件名过长。")
                val headerData = ByteArray(MsvConstants.FIXED_HEADER_SIZE + nameLength)
                fixed.copyInto(headerData)
                read = MsvConstants.FIXED_HEADER_SIZE
                while (read < headerData.size) {
                    val n = source.readAt(read.toLong(), headerData, read, headerData.size - read)
                    if (n <= 0) throw MsvFormatException("MSV 文件头不完整。")
                    read += n
                }
                val header = MsvHeader.parse(headerData, fileLength)
                val key = MsvCrypto.deriveKey(password, header)
                val encryptedName = MsvNaming.queryDisplayName(context, uri)
                var kind = MsvNaming.detect(header.originalFileName, encryptedName)
                if (kind == MediaKind.Unknown) {
                    val probeSource = MsvFileSource()
                    val reader = try {
                        probeSource.open(context, uri)
                        MsvReader(probeSource, header, key)
                    } catch (error: Throwable) {
                        probeSource.close()
                        throw error
                    }
                    try {
                        val probeSize = minOf(32L, header.plainLength).toInt()
                        val probe = ByteArray(probeSize)
                        if (probeSize > 0) reader.readAt(0, probe, 0, probeSize)
                        kind = MediaSniffer.detect(probe)
                    } finally {
                        reader.close()
                    }
                }
                return MsvMedia(source, header, key, kind)
            } catch (error: Throwable) {
                source.close()
                throw error
            }
        }
    }
}