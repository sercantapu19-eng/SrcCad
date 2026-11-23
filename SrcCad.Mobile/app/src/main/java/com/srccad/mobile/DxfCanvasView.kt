package com.srccad.mobile

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectTransformGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.drawscope.drawIntoCanvas
import androidx.compose.ui.graphics.nativeCanvas
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.unit.IntSize
import kotlin.math.max
import kotlin.math.min

/** DXF 2D viewer with pan/zoom and two-point measurement in drawing units. */
@Composable
fun DxfCanvasView(
    drawing: DxfDrawing,
    fitToken: Int,
    modifier: Modifier = Modifier,
    measurementMode: Boolean = false,
    measurementPoints: List<Pair<Double, Double>> = emptyList(),
    onMeasurePoint: (Double, Double) -> Unit = { _, _ -> }
) {
    var size by remember { mutableStateOf(IntSize.Zero) }
    var scale by remember { mutableStateOf(1f) }
    var ox by remember { mutableStateOf(0f) }
    var oy by remember { mutableStateOf(0f) }
    val paint = remember {
        android.graphics.Paint().apply {
            isAntiAlias = true
            style = android.graphics.Paint.Style.STROKE
            strokeCap = android.graphics.Paint.Cap.ROUND
        }
    }

    LaunchedEffect(drawing, fitToken, size) {
        if (size.width > 0 && size.height > 0) {
            val dw = max(drawing.bounds[2] - drawing.bounds[0], 1.0e-9)
            val dh = max(drawing.bounds[3] - drawing.bounds[1], 1.0e-9)
            val margin = 32f
            val s = min((size.width - 2 * margin) / dw, (size.height - 2 * margin) / dh).toFloat()
            val cx = (drawing.bounds[0] + drawing.bounds[2]) / 2.0
            val cy = (drawing.bounds[1] + drawing.bounds[3]) / 2.0
            scale = s
            ox = size.width / 2f - (cx * s).toFloat()
            oy = size.height / 2f + (cy * s).toFloat()
        }
    }

    Canvas(
        modifier = modifier
            .onSizeChanged { size = it }
            .pointerInput(drawing) {
                detectTransformGestures { centroid, pan, zoom, _ ->
                    val next = (scale * zoom).coerceIn(1.0e-6f, 1.0e9f)
                    val k = next / scale
                    ox = centroid.x - (centroid.x - ox) * k + pan.x
                    oy = centroid.y - (centroid.y - oy) * k + pan.y
                    scale = next
                }
            }
            .pointerInput(drawing, measurementMode, scale, ox, oy) {
                detectTapGestures { tap ->
                    if (measurementMode && scale > 0f) {
                        val worldX = ((tap.x - ox) / scale).toDouble()
                        val worldY = ((oy - tap.y) / scale).toDouble()
                        onMeasurePoint(worldX, worldY)
                    }
                }
            }
    ) {
        drawRect(Color(0xFF202833))
        val s = scale; val x0 = ox; val y0 = oy
        drawIntoCanvas { canvas ->
            val nc = canvas.nativeCanvas
            nc.save()
            nc.translate(x0, y0)
            nc.scale(s, -s)
            paint.strokeWidth = 1.6f / s
            for (group in drawing.groups) {
                paint.color = group.argb
                nc.drawLines(group.points, paint)
            }
            if (measurementPoints.isNotEmpty()) {
                paint.color = android.graphics.Color.YELLOW
                paint.strokeWidth = 2.5f / s
                val first = measurementPoints.first()
                nc.drawCircle(first.first.toFloat(), first.second.toFloat(), 6f / s, paint)
                if (measurementPoints.size >= 2) {
                    val second = measurementPoints[1]
                    nc.drawCircle(second.first.toFloat(), second.second.toFloat(), 6f / s, paint)
                    nc.drawLine(first.first.toFloat(), first.second.toFloat(), second.first.toFloat(), second.second.toFloat(), paint)
                }
            }
            nc.restore()
        }
    }
}
