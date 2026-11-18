package com.miying.player.ui

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.RectF
import android.util.AttributeSet
import android.view.GestureDetector
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import android.view.View
import kotlin.math.max
import kotlin.math.min

class PhotoZoomView @JvmOverloads constructor(
    context: Context,
    attrs: AttributeSet? = null,
) : View(context, attrs) {
    private var image: Bitmap? = null
    private val matrix = Matrix()
    private val paint = Paint(Paint.FILTER_BITMAP_FLAG or Paint.ANTI_ALIAS_FLAG)
    private var initialized = false
    private var maximumScale = 8f

    var onScaleChanged: ((Float) -> Unit)? = null

    private val gestureDetector = GestureDetector(context, object : GestureDetector.SimpleOnGestureListener() {
        override fun onDown(event: MotionEvent): Boolean = true

        override fun onScroll(
            downEvent: MotionEvent?,
            event: MotionEvent,
            distanceX: Float,
            distanceY: Float,
        ): Boolean {
            matrix.postTranslate(-distanceX, -distanceY)
            constrain()
            invalidate()
            return true
        }

        override fun onDoubleTap(event: MotionEvent): Boolean {
            if (scale > 1.02f) fit() else show100()
            return true
        }
    })

    private val scaleDetector = ScaleGestureDetector(context, object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
        override fun onScale(detector: ScaleGestureDetector): Boolean {
            applyScale(detector.scaleFactor, detector.focusX, detector.focusY)
            return true
        }
    })

    val scale: Float
        get() {
            val values = FloatArray(9)
            matrix.getValues(values)
            return values[Matrix.MSCALE_X]
        }

    fun setImageBitmap(bitmap: Bitmap?) {
        image = bitmap
        initialized = bitmap != null
        if (bitmap != null) fit() else invalidate()
    }

    override fun onSizeChanged(width: Int, height: Int, oldWidth: Int, oldHeight: Int) {
        super.onSizeChanged(width, height, oldWidth, oldHeight)
        if (image != null && (width != oldWidth || height != oldHeight)) fit()
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        val bitmap = image ?: return
        canvas.concat(matrix)
        canvas.drawBitmap(bitmap, 0f, 0f, paint)
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        val handledScale = scaleDetector.onTouchEvent(event)
        val handledGesture = gestureDetector.onTouchEvent(event)
        if (event.actionMasked == MotionEvent.ACTION_UP ||
            event.actionMasked == MotionEvent.ACTION_CANCEL
        ) {
            constrain()
            invalidate()
        }
        return handledScale || handledGesture || super.onTouchEvent(event)
    }

    fun fit() {
        val bitmap = image ?: return
        if (width <= 0 || height <= 0) return
        matrix.reset()
        val horizontal = width.toFloat() / bitmap.width
        val vertical = height.toFloat() / bitmap.height
        val value = min(horizontal, vertical)
        matrix.postScale(value, value)
        center()
        initialized = true
        notifyScale()
        invalidate()
    }

    fun show100() {
        val bitmap = image ?: return
        if (width <= 0 || height <= 0) return
        matrix.reset()
        matrix.postScale(1f, 1f)
        center()
        initialized = true
        notifyScale()
        invalidate()
    }

    fun zoomIn() = applyScale(1.25f, width / 2f, height / 2f)
    fun zoomOut() = applyScale(0.8f, width / 2f, height / 2f)

    private fun applyScale(factor: Float, focusX: Float, focusY: Float) {
        if (image == null || !initialized) return
        matrix.postScale(factor, factor, focusX, focusY)
        constrain()
        notifyScale()
        invalidate()
    }

    private fun center() {
        val bitmap = image ?: return
        val rect = RectF(0f, 0f, bitmap.width.toFloat(), bitmap.height.toFloat())
        matrix.mapRect(rect)
        matrix.postTranslate(
            width / 2f - rect.width() / 2f - rect.left,
            height / 2f - rect.height() / 2f - rect.top,
        )
        constrain()
    }

    private fun constrain() {
        val bitmap = image ?: return
        val values = FloatArray(9)
        matrix.getValues(values)
        val scaleValue = values[Matrix.MSCALE_X]
        val clampedScale = max(0.05f, min(scaleValue, maximumScale))
        if (clampedScale != scaleValue) {
            val focusX = width / 2f
            val focusY = height / 2f
            matrix.postScale(clampedScale / scaleValue, clampedScale / scaleValue, focusX, focusY)
        }

        val rect = RectF(0f, 0f, bitmap.width.toFloat(), bitmap.height.toFloat())
        matrix.mapRect(rect)
        var dx = 0f
        var dy = 0f
        if (rect.width() <= width) dx = width / 2f - rect.centerX()
        else if (rect.left > 0f) dx = -rect.left
        else if (rect.right < width) dx = width - rect.right

        if (rect.height() <= height) dy = height / 2f - rect.centerY()
        else if (rect.top > 0f) dy = -rect.top
        else if (rect.bottom < height) dy = height - rect.bottom

        if (dx != 0f || dy != 0f) matrix.postTranslate(dx, dy)
    }

    private fun notifyScale() {
        onScaleChanged?.invoke(scale)
    }
}