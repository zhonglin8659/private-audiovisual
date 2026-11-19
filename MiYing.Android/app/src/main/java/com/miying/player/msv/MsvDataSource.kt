@file:androidx.annotation.OptIn(androidx.media3.common.util.UnstableApi::class)

package com.miying.player.msv

import android.net.Uri
import androidx.media3.common.C
import androidx.media3.datasource.DataSpec
import androidx.media3.datasource.BaseDataSource

class MsvDataSource(
    private val media: MsvMedia,
) : BaseDataSource(true) {
    private var reader: MsvReader? = null
    private var position = 0L
    private var remaining = C.LENGTH_UNSET.toLong()

    override fun open(dataSpec: DataSpec): Long {
        if (reader == null) {
            reader = media.openReader()
        }
        val length = reader?.plainLength ?: 0L
        if (dataSpec.position < 0 || dataSpec.position > length) {
            throw java.io.IOException("读取位置超出文件范围。")
        }
        position = dataSpec.position
        remaining = if (dataSpec.length != C.LENGTH_UNSET.toLong()) dataSpec.length else C.LENGTH_UNSET.toLong()
        transferStarted(dataSpec)
        return if (remaining == C.LENGTH_UNSET.toLong()) length - position else remaining
    }

    override fun read(target: ByteArray, offset: Int, length: Int): Int {
        if (length == 0) return 0
        if (remaining == 0L) return C.RESULT_END_OF_INPUT
        val plainLength = reader?.plainLength ?: return C.RESULT_END_OF_INPUT
        val available = plainLength - position
        if (available <= 0) return C.RESULT_END_OF_INPUT

        var wanted = minOf(length.toLong(), available).toInt()
        if (remaining != C.LENGTH_UNSET.toLong()) {
            wanted = minOf(wanted.toLong(), remaining).toInt()
        }
        if (wanted == 0) return C.RESULT_END_OF_INPUT

        val read = reader?.readAt(position, target, offset, wanted) ?: return C.RESULT_END_OF_INPUT
        if (read <= 0) return C.RESULT_END_OF_INPUT
        position += read
        bytesTransferred(read)
        if (remaining != C.LENGTH_UNSET.toLong()) remaining -= read
        return read
    }

    override fun getUri(): Uri? = media.uri

    override fun close() {
        reader?.close()
        reader = null
    }

    class Factory(
        private val media: MsvMedia,
    ) : androidx.media3.datasource.DataSource.Factory {
        override fun createDataSource(): MsvDataSource = MsvDataSource(media)
    }
}