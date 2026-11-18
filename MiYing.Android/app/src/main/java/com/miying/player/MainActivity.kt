package com.miying.player

import android.annotation.SuppressLint
import android.app.Activity
import android.app.AlertDialog
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.media.ExifInterface
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.TextUtils
import android.view.GestureDetector
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.WindowInsets
import android.view.WindowInsetsController
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ProgressBar
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.TextView
import android.widget.Toast
import androidx.media3.common.AudioAttributes
import androidx.media3.common.MediaItem
import androidx.media3.common.PlaybackParameters
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.ProgressiveMediaSource
import androidx.media3.ui.PlayerView
import com.miying.player.msv.MediaKind
import com.miying.player.msv.MsvDataSource
import com.miying.player.msv.MsvMedia
import com.miying.player.msv.MsvNaming
import com.miying.player.msv.RecentEntry
import com.miying.player.msv.RecentStore
import com.miying.player.ui.PhotoZoomView
import java.io.ByteArrayInputStream
import java.io.File
import java.io.FileNotFoundException
import java.util.concurrent.Executors

@UnstableApi
class MainActivity : Activity() {
    private lateinit var screenHome: ScrollView
    private lateinit var screenUnlock: ViewGroup
    private lateinit var screenVideo: ViewGroup
    private lateinit var screenPhoto: ViewGroup
    private lateinit var recentList: LinearLayout
    private lateinit var textNoRecent: TextView
    private lateinit var btnClearRecent: TextView
    private lateinit var textUnlockFile: TextView
    private lateinit var inputPassword: EditText
    private lateinit var checkRemember: CheckBox
    private lateinit var btnUnlock: Button
    private lateinit var textUnlockError: TextView
    private lateinit var progressUnlock: ProgressBar
    private lateinit var playerView: PlayerView
    private lateinit var videoName: TextView
    private lateinit var videoTop: View
    private lateinit var videoControls: View
    private lateinit var videoTouchLayer: View
    private lateinit var seekVideo: SeekBar
    private lateinit var textVideoTime: TextView
    private lateinit var btnPlay: Button
    private lateinit var btnMute: Button
    private lateinit var btnSpeed: Button
    private lateinit var photoView: PhotoZoomView
    private lateinit var photoName: TextView
    private lateinit var photoInfo: TextView
    private lateinit var photoTop: View
    private lateinit var photoControls: View

    private val recentStore by lazy { RecentStore(this) }
    private val background = Executors.newSingleThreadExecutor()
    private val mainHandler = Handler(Looper.getMainLooper())
    private var controlsVisible = false
    private val hideControlsRunnable = Runnable {
        if (screenVideo.visibility == View.VISIBLE && player?.isPlaying == true) {
            hideVideoControls()
        }
    }
    private var selectedMedia: MsvMedia? = null
    private var selectedPhoto: Bitmap? = null
    private var pendingUri: Uri? = null
    private var pendingName: String? = null
    private var rememberedPassword: CharArray? = null
    private var pendingRememberPassword = false
    private var player: ExoPlayer? = null
    private var progressRunnable: Runnable? = null
    private var isVideoFullScreen = false
    private var openPhotoAfterUnlock = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        bindViews()
        setupListeners()
        refreshRecent()
        showHomeScreen()
        handleViewIntent(intent)
    }

    private fun handleViewIntent(intent: Intent?) {
        val uri = intent?.data ?: return
        if (intent.action == Intent.ACTION_VIEW) {
            takePersistable(uri)
            showUnlockScreen(uri, MsvNaming.queryDisplayName(this, uri))
        }
    }

    private fun bindViews() {
        screenHome = findViewById(R.id.screen_home)
        screenUnlock = findViewById(R.id.screen_unlock)
        screenVideo = findViewById(R.id.screen_video)
        screenPhoto = findViewById(R.id.screen_photo)
        recentList = findViewById(R.id.list_recent)
        textNoRecent = findViewById(R.id.text_no_recent)
        btnClearRecent = findViewById(R.id.btn_clear_recent)
        textUnlockFile = findViewById(R.id.text_unlock_file)
        inputPassword = findViewById(R.id.input_password)
        checkRemember = findViewById(R.id.check_remember)
        btnUnlock = findViewById(R.id.btn_unlock)
        textUnlockError = findViewById(R.id.text_unlock_error)
        progressUnlock = findViewById(R.id.progress_unlock)
        playerView = findViewById(R.id.player_view)
        videoName = findViewById(R.id.text_video_name)
        videoTop = findViewById(R.id.video_top)
        videoControls = findViewById(R.id.video_controls)
        videoTouchLayer = findViewById(R.id.video_touch_layer)
        seekVideo = findViewById(R.id.seek_video)
        textVideoTime = findViewById(R.id.text_video_time)
        btnPlay = findViewById(R.id.btn_play)
        btnMute = findViewById(R.id.btn_mute)
        btnSpeed = findViewById(R.id.btn_speed)
        photoView = findViewById(R.id.photo_view)
        photoName = findViewById(R.id.text_photo_name)
        photoInfo = findViewById(R.id.text_photo_info)
        photoTop = findViewById(R.id.photo_top)
        photoControls = findViewById(R.id.photo_controls)
    }

    private fun setupListeners() {
        findViewById<Button>(R.id.btn_open_video).setOnClickListener { openEncryptedMedia(MediaKind.Video) }
        findViewById<Button>(R.id.btn_open_photo).setOnClickListener { openEncryptedMedia(MediaKind.Photo) }
        findViewById<View>(R.id.btn_unlock_back).setOnClickListener { showHomeScreen() }
        findViewById<TextView>(R.id.btn_video_back).setOnClickListener { showHomeScreen() }
        findViewById<TextView>(R.id.btn_photo_back).setOnClickListener { showHomeScreen() }
        findViewById<Button>(R.id.btn_unlock).setOnClickListener { unlockSelectedMedia() }
        btnClearRecent.setOnClickListener {
            recentStore.clear()
            refreshRecent()
            Toast.makeText(this, "最近打开记录已清空", Toast.LENGTH_SHORT).show()
        }

        val gestureDetector = GestureDetector(this, object : GestureDetector.SimpleOnGestureListener() {
            override fun onDown(e: MotionEvent): Boolean = true

            override fun onSingleTapConfirmed(e: MotionEvent): Boolean {
                toggleVideoControls()
                return true
            }

            override fun onDoubleTap(e: MotionEvent): Boolean {
                toggleVideoFullscreen()
                return true
            }
        })
        videoTouchLayer.setOnTouchListener { _, event -> gestureDetector.onTouchEvent(event) }

        boundControl(R.id.btn_back10) {
            player?.let { current -> current.seekTo(maxOf(0L, current.currentPosition - 10_000L)) }
        }
        boundControl(R.id.btn_forward10) {
            player?.let { current -> current.seekTo(current.currentPosition + 10_000L) }
        }
        boundControl(R.id.btn_play) {
            player?.let { current ->
                if (current.isPlaying) current.pause() else current.play()
                updatePlayButton()
            }
        }
        boundControl(R.id.btn_mute) {
            player?.let { current ->
                current.volume = if (current.volume > 0f) 0f else 1f
                btnMute.setText(if (current.volume > 0f) R.string.video_mute else R.string.video_unmute)
            }
        }
        boundControl(R.id.btn_speed) { showSpeedDialog() }
        boundControl(R.id.btn_video_fullscreen) { toggleVideoFullscreen() }

        seekVideo.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(seekBar: SeekBar?, progress: Int, fromUser: Boolean) {
                if (fromUser) {
                    val current = player ?: return
                    textVideoTime.text = formatTime(progress.toLong()) + " / " + formatTime(current.duration.takeIf { it > 0 } ?: 0L)
                }
            }

            override fun onStartTrackingTouch(seekBar: SeekBar?) {
                cancelHideControls()
                player?.pause()
            }

            override fun onStopTrackingTouch(seekBar: SeekBar?) {
                val current = player ?: return
                current.seekTo(seekBar?.progress?.toLong() ?: 0L)
                current.play()
                showVideoControls()
            }
        })

        findViewById<Button>(R.id.btn_photo_fit).setOnClickListener { photoView.fit() }
        findViewById<Button>(R.id.btn_photo_100).setOnClickListener { photoView.show100() }
        findViewById<Button>(R.id.btn_photo_zoom_in).setOnClickListener { photoView.zoomIn() }
        findViewById<Button>(R.id.btn_photo_zoom_out).setOnClickListener { photoView.zoomOut() }
        findViewById<Button>(R.id.btn_photo_rotate).setOnClickListener { rotatePhoto() }
        photoView.onScaleChanged = { value -> photoInfo.text = buildPhotoInfo(value) }
    }

    private fun boundControl(viewId: Int, action: () -> Unit) {
        findViewById<View>(viewId).setOnClickListener {
            if (screenVideo.visibility == View.VISIBLE) {
                showVideoControls()
            }
            action()
        }
    }

    private fun openEncryptedMedia(kind: MediaKind) {
        pendingUri = null
        openPhotoAfterUnlock = kind == MediaKind.Photo
        pendingName = null
        val intent = Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE)
            type = "*/*"
        }
        startActivityForResult(intent, REQUEST_OPEN_MEDIA)
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        val uri = data?.data ?: return
        if (requestCode == REQUEST_OPEN_MEDIA && resultCode == RESULT_OK) {
            takePersistable(uri)
            showUnlockScreen(uri, MsvNaming.queryDisplayName(this, uri))
        }
    }

    private fun takePersistable(uri: Uri) {
        try {
            contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION)
        } catch (_: Exception) {
        }
    }

    private fun showHomeScreen() {
        cancelHideControls()
        applyImmersiveMode(false)
        isVideoFullScreen = false
        saveCurrentProgress()
        closeCurrentMedia()
        screenHome.visibility = View.VISIBLE
        screenUnlock.visibility = View.GONE
        screenVideo.visibility = View.GONE
        screenPhoto.visibility = View.GONE
        refreshRecent()
    }

    private fun showUnlockScreen(uri: Uri, name: String) {
        cancelHideControls()
        applyImmersiveMode(false)
        isVideoFullScreen = false
        closeCurrentMedia()
        pendingUri = uri
        pendingName = name
        screenHome.visibility = View.GONE
        screenUnlock.visibility = View.VISIBLE
        screenVideo.visibility = View.GONE
        screenPhoto.visibility = View.GONE
        textUnlockFile.text = name
        textUnlockError.visibility = View.GONE
        progressUnlock.visibility = View.GONE
        inputPassword.setText("")
        checkRemember.isChecked = false
        rememberedPassword?.let {
            inputPassword.setText(String(it))
            checkRemember.isChecked = true
            unlockSelectedMedia()
        }
    }

    @SuppressLint("SetTextI18n")
    private fun unlockSelectedMedia() {
        val uri = pendingUri ?: return
        val password = inputPassword.text?.toString() ?: ""
        if (password.isEmpty()) {
            textUnlockError.text = "请输入密码。"
            textUnlockError.visibility = View.VISIBLE
            return
        }
        pendingRememberPassword = checkRemember.isChecked
        textUnlockError.visibility = View.GONE
        progressUnlock.visibility = View.VISIBLE
        btnUnlock.isEnabled = false
        background.execute {
            try {
                val media = MsvMedia.open(applicationContext, uri, password)
                val bitmap = if (media.kind == MediaKind.Photo) {
                    decodeBitmap(media.readAllPlainText())
                } else {
                    null
                }
                runOnUiThread {
                    progressUnlock.visibility = View.GONE
                    btnUnlock.isEnabled = true
                    if (pendingRememberPassword) rememberedPassword = password.toCharArray()
                    onMediaUnlocked(media, bitmap)
                }
            } catch (error: Throwable) {
                runOnUiThread {
                    progressUnlock.visibility = View.GONE
                    btnUnlock.isEnabled = true
                    if (isMissingFileError(error)) {
                        recentStore.remove(uri.toString())
                        refreshRecent()
                        Toast.makeText(this, "文件已不存在，已从最近打开移除", Toast.LENGTH_SHORT).show()
                        showHomeScreen()
                    } else {
                        textUnlockError.text = error.message ?: "无法打开文件。"
                        textUnlockError.visibility = View.VISIBLE
                    }
                }
            }
        }
    }

    private fun onMediaUnlocked(media: MsvMedia, bitmap: Bitmap?) {
        closeCurrentMedia()
        selectedMedia = media
        if (media.kind == MediaKind.Photo && bitmap != null) {
            selectedPhoto = bitmap
            showPhotoScreen(media, bitmap)
            recentStore.save(media.uri.toString(), media.displayName, "photo")
        } else if (media.kind != MediaKind.Photo) {
            showVideoScreen(media)
            recentStore.save(media.uri.toString(), media.displayName, "video")
        } else {
            Toast.makeText(this, "无法解码照片。", Toast.LENGTH_SHORT).show()
            showHomeScreen()
        }
    }

    private fun showVideoScreen(media: MsvMedia) {
        screenHome.visibility = View.GONE
        screenUnlock.visibility = View.GONE
        screenVideo.visibility = View.VISIBLE
        screenPhoto.visibility = View.GONE
        videoName.text = media.displayName
        isVideoFullScreen = false
        applyImmersiveMode(false)
        initializePlayer(media)
        showVideoControls()
    }

    private fun initializePlayer(media: MsvMedia) {
        val existing = player
        if (existing == null) {
            player = ExoPlayer.Builder(this).build().also { created ->
                playerView.player = created
                created.setAudioAttributes(AudioAttributes.DEFAULT, true)
                created.addListener(object : Player.Listener {
                    override fun onPlaybackStateChanged(playbackState: Int) {
                        updatePlayButton()
                        updateVideoProgress()
                        if (playbackState == Player.STATE_READY && created.isPlaying) {
                            scheduleHideControls()
                        }
                    }

                    override fun onIsPlayingChanged(isPlaying: Boolean) {
                        updatePlayButton()
                        if (isPlaying) {
                            scheduleHideControls()
                        } else {
                            cancelHideControls()
                            showVideoControls(autoHide = false)
                        }
                    }
                })
            }
        }
        val factory = MsvDataSource.Factory(media)
        val progressiveFactory = ProgressiveMediaSource.Factory(factory)
        val mainSource = progressiveFactory.createMediaSource(MediaItem.fromUri(media.uri))
        val startPosition = currentRecentPosition(media.uri.toString())
        player?.setMediaSource(mainSource, startPosition)
        player?.prepare()
        player?.playWhenReady = true
        startProgressUpdates()
        updateVideoProgress()
        updatePlayButton()
    }

    private fun startProgressUpdates() {
        progressRunnable?.let { playerView.removeCallbacks(it) }
        val runnable = object : Runnable {
            override fun run() {
                updateVideoProgress()
                playerView.postDelayed(this, 200L)
            }
        }
        progressRunnable = runnable
        playerView.post(runnable)
    }

    private fun stopProgressUpdates() {
        progressRunnable?.let { playerView.removeCallbacks(it) }
        progressRunnable = null
    }

    private fun updateVideoProgress() {
        val current = player ?: return
        val duration = if (current.duration > 0) current.duration else 0L
        val position = current.currentPosition.coerceIn(0L, maxOf(duration, 0L))
        seekVideo.max = duration.coerceAtLeast(0L).toInt()
        seekVideo.progress = position.toInt()
        textVideoTime.text = formatTime(position) + " / " + formatTime(duration)
    }

    private fun updatePlayButton() {
        val playing = player?.isPlaying == true
        btnPlay.text = if (playing) "❚❚" else "▶"
        btnPlay.contentDescription = getString(if (playing) R.string.video_pause else R.string.video_play)
    }

    private fun showSpeedDialog() {
        val speeds = arrayOf("0.5x", "0.75x", "1x", "1.25x", "1.5x", "2x")
        val values = floatArrayOf(0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f)
        AlertDialog.Builder(this)
            .setTitle(R.string.video_speed)
            .setItems(speeds) { _, which ->
                player?.playbackParameters = PlaybackParameters(values[which])
                btnSpeed.text = speeds[which]
            }
            .show()
    }

    private fun toggleVideoFullscreen() {
        if (screenVideo.visibility != View.VISIBLE) {
            return
        }

        isVideoFullScreen = !isVideoFullScreen
        applyImmersiveMode(isVideoFullScreen)
        showVideoControls()
    }

    private fun showVideoControls(autoHide: Boolean = true) {
        if (screenVideo.visibility != View.VISIBLE) {
            return
        }

        videoTop.visibility = View.VISIBLE
        videoControls.visibility = View.VISIBLE
        controlsVisible = true
        if (autoHide) {
            scheduleHideControls()
        } else {
            cancelHideControls()
        }
    }

    private fun hideVideoControls() {
        videoTop.visibility = View.GONE
        videoControls.visibility = View.GONE
        controlsVisible = false
        cancelHideControls()
    }

    private fun toggleVideoControls() {
        if (screenVideo.visibility != View.VISIBLE) {
            return
        }

        if (controlsVisible) {
            hideVideoControls()
        } else {
            showVideoControls()
        }
    }

    private fun scheduleHideControls() {
        cancelHideControls()
        if (screenVideo.visibility == View.VISIBLE && player?.isPlaying == true) {
            mainHandler.postDelayed(hideControlsRunnable, 3_500L)
        }
    }

    private fun cancelHideControls() {
        mainHandler.removeCallbacks(hideControlsRunnable)
    }

    @Suppress("DEPRECATION")
    private fun applyImmersiveMode(enabled: Boolean) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            val controller = window.insetsController ?: return
            if (enabled) {
                controller.hide(WindowInsets.Type.systemBars())
                controller.systemBarsBehavior =
                    WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
            } else {
                controller.show(WindowInsets.Type.systemBars())
            }
        } else {
            window.decorView.systemUiVisibility = if (enabled) {
                View.SYSTEM_UI_FLAG_FULLSCREEN or
                    View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or
                    View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY or
                    View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or
                    View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION or
                    View.SYSTEM_UI_FLAG_LAYOUT_STABLE
            } else {
                View.SYSTEM_UI_FLAG_VISIBLE
            }
        }
    }

    private fun showPhotoScreen(media: MsvMedia, bitmap: Bitmap) {
        screenHome.visibility = View.GONE
        screenUnlock.visibility = View.GONE
        screenVideo.visibility = View.GONE
        screenPhoto.visibility = View.VISIBLE
        photoName.text = media.displayName
        photoTop.visibility = View.VISIBLE
        photoControls.visibility = View.VISIBLE
        photoView.setImageBitmap(bitmap)
        photoView.post { photoView.fit() }
        photoInfo.text = buildPhotoInfo(photoView.scale)
    }

    private fun rotatePhoto() {
        val original = selectedPhoto ?: return
        val rotation = Matrix().apply { postRotate(90f) }
        val rotated = Bitmap.createBitmap(original, 0, 0, original.width, original.height, rotation, true)
        selectedPhoto = rotated
        photoView.setImageBitmap(rotated)
        photoView.post { photoView.fit() }
    }

    private fun buildPhotoInfo(scale: Float): String {
        val bitmap = selectedPhoto
        val dimension = if (bitmap != null) "${bitmap.width}×${bitmap.height}" else ""
        return dimension + "  " + String.format("%.0f%%", scale * 100f)
    }

    private fun decodeBitmap(data: ByteArray): Bitmap? {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(data, 0, data.size, bounds)
        val options = BitmapFactory.Options().apply {
            inSampleSize = calculateSampleSize(bounds.outWidth, bounds.outHeight)
        }
        val decoded = BitmapFactory.decodeByteArray(data, 0, data.size, options)
        return applyExifRotation(data, decoded)
    }

    private fun calculateSampleSize(width: Int, height: Int): Int {
        val maxDimension = 4096
        var sampleSize = 1
        if (width > maxDimension || height > maxDimension) {
            val halfWidth = width / 2
            val halfHeight = height / 2
            while ((halfWidth / sampleSize) >= maxDimension && (halfHeight / sampleSize) >= maxDimension) {
                sampleSize *= 2
            }
        }
        return sampleSize
    }

    private fun applyExifRotation(data: ByteArray, bitmap: Bitmap?): Bitmap? {
        if (bitmap == null) return null
        val orientation = try {
            val exif = ExifInterface(ByteArrayInputStream(data))
            exif.getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL)
        } catch (_: Exception) {
            ExifInterface.ORIENTATION_NORMAL
        }
        val degrees = when (orientation) {
            ExifInterface.ORIENTATION_ROTATE_90 -> 90f
            ExifInterface.ORIENTATION_ROTATE_180 -> 180f
            ExifInterface.ORIENTATION_ROTATE_270 -> 270f
            else -> 0f
        }
        if (degrees == 0f) return bitmap
        val matrix = Matrix().apply { postRotate(degrees) }
        val rotated = Bitmap.createBitmap(bitmap, 0, 0, bitmap.width, bitmap.height, matrix, true)
        if (rotated != bitmap) bitmap.recycle()
        return rotated
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density).toInt()

    private fun formatTime(value: Long): String {
        val totalSeconds = (value / 1000L).coerceAtLeast(0L)
        val hours = totalSeconds / 3600L
        val minutes = (totalSeconds % 3600L) / 60L
        val seconds = totalSeconds % 60L
        return if (hours > 0L) {
            String.format("%02d:%02d:%02d", hours, minutes, seconds)
        } else {
            String.format("%02d:%02d", minutes, seconds)
        }
    }

    private fun currentRecentPosition(uri: String): Long {
        return recentStore.load().firstOrNull { it.uri == uri }?.position ?: 0L
    }

    private fun saveCurrentProgress() {
        val media = selectedMedia ?: return
        val current = player ?: return
        if (current.duration > 0) {
            recentStore.save(media.uri.toString(), media.displayName, "video", current.currentPosition, current.duration)
        }
    }

    private fun refreshRecent() {
        recentList.removeAllViews()
        val entries = recentStore.load()
        textNoRecent.visibility = if (entries.isEmpty()) View.VISIBLE else View.GONE
        btnClearRecent.visibility = if (entries.isEmpty()) View.GONE else View.VISIBLE

        entries.forEach { entry ->
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = android.view.Gravity.CENTER_VERTICAL
                setPadding(dp(16), dp(12), dp(8), dp(12))
                background = getDrawable(R.drawable.card_surface)
                layoutParams = LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.WRAP_CONTENT,
                ).apply { bottomMargin = dp(8) }
                setOnClickListener { openRecent(entry) }
            }

            val info = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = android.view.Gravity.CENTER_VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            }
            val name = TextView(this).apply {
                text = entry.name
                setTextColor(getColor(R.color.text))
                textSize = 15f
                maxLines = 1
                ellipsize = TextUtils.TruncateAt.END
                layoutParams = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            }
            val type = TextView(this).apply {
                text = if (entry.kind == "photo") "照片" else "视频"
                setTextColor(getColor(R.color.text_sub))
                textSize = 12f
                setPadding(dp(10), 0, dp(6), 0)
            }
            info.addView(name)
            info.addView(type)

            val remove = TextView(this).apply {
                text = "移除"
                setTextColor(getColor(R.color.danger))
                textSize = 12f
                setPadding(dp(10), dp(6), dp(10), dp(6))
                background = getDrawable(R.drawable.bg_control_chip)
                contentDescription = "删除最近记录"
                setOnClickListener {
                    recentStore.remove(entry.uri)
                    refreshRecent()
                    Toast.makeText(this@MainActivity, "已移除最近记录", Toast.LENGTH_SHORT).show()
                }
            }

            row.addView(info)
            row.addView(remove)
            recentList.addView(row)
        }
    }

    private fun openRecent(entry: RecentEntry) {
        val uri = Uri.parse(entry.uri)
        if (!isRecentUriAccessible(uri)) {
            recentStore.remove(entry.uri)
            refreshRecent()
            Toast.makeText(this, "文件已不存在，已从最近打开移除", Toast.LENGTH_SHORT).show()
            return
        }

        takePersistable(uri)
        showUnlockScreen(uri, entry.name)
    }

    private fun isRecentUriAccessible(uri: Uri): Boolean {
        if (uri.scheme == "file") {
            return uri.path?.let { File(it).exists() } == true
        }

        return try {
            contentResolver.openFileDescriptor(uri, "r")?.use { true } ?: false
        } catch (_: Exception) {
            false
        }
    }

    private fun isMissingFileError(error: Throwable): Boolean {
        var cause: Throwable? = error
        while (cause != null) {
            if (cause is FileNotFoundException) {
                return true
            }

            val message = cause.message?.lowercase().orEmpty()
            if ("file not found" in message || "enoent" in message || "no such file" in message) {
                return true
            }
            cause = cause.cause
        }
        return false
    }

    private fun closeCurrentMedia() {
        stopProgressUpdates()
        saveCurrentProgress()
        player?.release()
        player = null
        playerView.player = null
        selectedPhoto?.recycle()
        selectedPhoto = null
        selectedMedia?.close()
        selectedMedia = null
        photoView.setImageBitmap(null)
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (screenUnlock.visibility == View.VISIBLE ||
            screenVideo.visibility == View.VISIBLE ||
            screenPhoto.visibility == View.VISIBLE
        ) {
            showHomeScreen()
        } else {
            super.onBackPressed()
        }
    }

    override fun onPause() {
        super.onPause()
        saveCurrentProgress()
    }

    override fun onDestroy() {
        super.onDestroy()
        cancelHideControls()
        closeCurrentMedia()
        background.shutdown()
        rememberedPassword?.fill(' ')
        rememberedPassword = null
    }

    companion object {
        private const val REQUEST_OPEN_MEDIA = 1001
    }
}