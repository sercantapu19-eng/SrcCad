package com.srccad.mobile

import android.content.Context
import android.opengl.GLES30
import android.opengl.GLSurfaceView
import android.opengl.Matrix
import android.view.MotionEvent
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.FloatBuffer
import java.nio.IntBuffer
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sqrt
import kotlin.math.tan
import javax.microedition.khronos.egl.EGLConfig
import javax.microedition.khronos.opengles.GL10

/** OpenGL ES 3.0 mesh viewer: one-finger orbit, two-finger pan/pinch. */
class CadGlSurfaceView(context: Context) : GLSurfaceView(context) {
    private val cadRenderer = CadGlRenderer(context.resources.displayMetrics.density)
    private val tapSlopPx = 18f * context.resources.displayMetrics.density
    private var lastX = 0f
    private var lastY = 0f
    private var lastSpan = 0f
    private var multiTouchGesture = false
    private var downX = 0f
    private var downY = 0f
    @Volatile var measurementMode: Boolean = false
    var onRayRequested: ((FloatArray?) -> Unit)? = null

    init {
        setEGLContextClientVersion(3)
        setPreserveEGLContextOnPause(true)
        setRenderer(cadRenderer)
        renderMode = RENDERMODE_WHEN_DIRTY
    }

    fun submitMesh(vertices: FloatArray, indices: IntArray, bounds: DoubleArray, triangleFaceIds: IntArray) {
        queueEvent { cadRenderer.setMesh(vertices, indices, bounds, triangleFaceIds); requestRender() }
    }

    fun highlightFaces(faceIds: IntArray) {
        queueEvent { cadRenderer.setHighlightedFaces(faceIds); requestRender() }
    }

    fun highlightEdges(vararg picks: DoubleArray) {
        queueEvent { cadRenderer.setHighlightedEdges(picks.toList()); requestRender() }
    }

    fun clearMesh() {
        queueEvent { cadRenderer.clearMesh(); requestRender() }
    }

    fun fitModel() {
        queueEvent { cadRenderer.resetCamera(); requestRender() }
    }

    fun setStandardView(view: String) {
        queueEvent { cadRenderer.setStandardView(view); requestRender() }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                lastX = event.x
                lastY = event.y
                downX = event.x
                downY = event.y
                lastSpan = 0f
                multiTouchGesture = false
                return true
            }
            MotionEvent.ACTION_POINTER_DOWN -> {
                multiTouchGesture = true
                lastSpan = pointerSpan(event)
                lastX = (event.getX(0) + event.getX(1)) * 0.5f
                lastY = (event.getY(0) + event.getY(1)) * 0.5f
                return true
            }
            MotionEvent.ACTION_MOVE -> {
                if (event.pointerCount == 1) {
                    val dx = event.x - lastX
                    val dy = event.y - lastY
                    // Drag always orbits, including while measurement mode is active.
                    // A short stationary tap is handled on ACTION_UP below.
                    queueEvent { cadRenderer.orbit(dx * 0.55f, dy * 0.55f); requestRender() }
                    lastX = event.x
                    lastY = event.y
                } else if (event.pointerCount >= 2) {
                    val x = (event.getX(0) + event.getX(1)) * 0.5f
                    val y = (event.getY(0) + event.getY(1)) * 0.5f
                    val span = pointerSpan(event)
                    val zoomRatio = if (lastSpan > 0f && span > 0f) span / lastSpan else 1f
                    val dx = x - lastX
                    val dy = y - lastY
                    queueEvent {
                        cadRenderer.zoom(zoomRatio)
                        cadRenderer.pan(dx / max(1, width).toFloat(), -dy / max(1, height).toFloat())
                        requestRender()
                    }
                    lastSpan = span
                    lastX = x
                    lastY = y
                }
                return true
            }
            MotionEvent.ACTION_POINTER_UP -> {
                // Keep the surviving finger's coordinates so dropping from a pinch
                // to a one-finger orbit does not cause a camera jump.
                val remainingIndex = if (event.actionIndex == 0) 1 else 0
                if (remainingIndex < event.pointerCount) {
                    lastX = event.getX(remainingIndex)
                    lastY = event.getY(remainingIndex)
                }
                lastSpan = 0f
                return true
            }
            MotionEvent.ACTION_UP -> {
                if (measurementMode && !multiTouchGesture) {
                    val dx = event.x - downX
                    val dy = event.y - downY
                    if (sqrt(dx * dx + dy * dy) <= tapSlopPx) requestRay(event.x, event.y)
                }
                lastSpan = 0f
                multiTouchGesture = false
                return true
            }
            MotionEvent.ACTION_CANCEL -> {
                lastSpan = 0f
                return true
            }
        }
        return true
    }

    private fun requestRay(x: Float, y: Float) {
        queueEvent {
            val ray = cadRenderer.rayForScreen(x, y, width, height)
            post { onRayRequested?.invoke(ray) }
        }
    }

    private fun pointerSpan(event: MotionEvent): Float {
        if (event.pointerCount < 2) return 0f
        val dx = event.getX(0) - event.getX(1)
        val dy = event.getY(0) - event.getY(1)
        return sqrt(dx * dx + dy * dy)
    }
}

private class CadGlRenderer(private val density: Float) : GLSurfaceView.Renderer {
    private var program = 0
    private var vertexBuffer: FloatBuffer? = null
    private var indexBuffer: IntBuffer? = null
    private var indexCount = 0
    private var triangleFaceIds = IntArray(0)
    private var selectedFaceIds = IntArray(0)
    private var highlightIndexBufferId = 0
    private var highlightIndexCount = 0
    private var edgeVertexBufferId = 0
    private var edgeVertexCount = 0
    private var edgeRanges = emptyList<Pair<Int, Int>>()
    private var vertexBufferId = 0
    private var indexBufferId = 0
    private var meshUploaded = false
    private var surfaceWidth = 1
    private var surfaceHeight = 1
    private var centerX = 0f
    private var centerY = 0f
    private var centerZ = 0f
    private var modelScale = 1f
    private var rotationX = -20f
    private var rotationY = 35f
    private var panX = 0f
    private var panY = 0f
    private var zoomFactor = 1f

    private val projection = FloatArray(16)
    private val view = FloatArray(16)
    private val model = FloatArray(16)
    private val viewModel = FloatArray(16)
    private val mvp = FloatArray(16)
    @Volatile private var inverseMvpSnapshot: FloatArray? = null

    override fun onSurfaceCreated(gl: GL10?, config: EGLConfig?) {
        GLES30.glClearColor(0.075f, 0.09f, 0.12f, 1f)
        GLES30.glEnable(GLES30.GL_DEPTH_TEST)
        GLES30.glDisable(GLES30.GL_CULL_FACE)
        program = createProgram(VERTEX_SHADER, FRAGMENT_SHADER)
        val bufferIds = IntArray(2)
        GLES30.glGenBuffers(2, bufferIds, 0)
        val highlightIds = IntArray(2)
        GLES30.glGenBuffers(2, highlightIds, 0)
        highlightIndexBufferId = highlightIds[0]
        edgeVertexBufferId = highlightIds[1]
        vertexBufferId = bufferIds[0]
        indexBufferId = bufferIds[1]
        meshUploaded = false
    }

    override fun onSurfaceChanged(gl: GL10?, width: Int, height: Int) {
        surfaceWidth = max(1, width)
        surfaceHeight = max(1, height)
        GLES30.glViewport(0, 0, surfaceWidth, surfaceHeight)
        Matrix.perspectiveM(projection, 0, 42f, surfaceWidth.toFloat() / surfaceHeight, 0.05f, 100f)
    }

    override fun onDrawFrame(gl: GL10?) {
        GLES30.glClear(GLES30.GL_COLOR_BUFFER_BIT or GLES30.GL_DEPTH_BUFFER_BIT)
        val vertices = vertexBuffer ?: return
        val indices = indexBuffer ?: return
        if (indexCount == 0 || program == 0) return

        Matrix.setLookAtM(view, 0, 0f, 0f, 4.2f / zoomFactor, 0f, 0f, 0f, 0f, 1f, 0f)
        Matrix.setIdentityM(model, 0)
        Matrix.translateM(model, 0, panX, panY, 0f)
        Matrix.rotateM(model, 0, rotationX, 1f, 0f, 0f)
        Matrix.rotateM(model, 0, rotationY, 0f, 1f, 0f)
        Matrix.scaleM(model, 0, modelScale, modelScale, modelScale)
        Matrix.translateM(model, 0, -centerX, -centerY, -centerZ)
        Matrix.multiplyMM(viewModel, 0, view, 0, model, 0)
        Matrix.multiplyMM(mvp, 0, projection, 0, viewModel, 0)
        val inverse = FloatArray(16)
        inverseMvpSnapshot = if (Matrix.invertM(inverse, 0, mvp, 0)) inverse else null

        GLES30.glUseProgram(program)
        GLES30.glUniformMatrix4fv(GLES30.glGetUniformLocation(program, "uMvp"), 1, false, mvp, 0)
        GLES30.glUniformMatrix4fv(GLES30.glGetUniformLocation(program, "uModel"), 1, false, model, 0)
        GLES30.glUniform1i(GLES30.glGetUniformLocation(program, "uHighlight"), 0)

        // GLES 3 requires buffer objects for vertex/index data; client-side arrays are not valid.
        if (!meshUploaded) {
            vertices.position(0)
            GLES30.glBindBuffer(GLES30.GL_ARRAY_BUFFER, vertexBufferId)
            GLES30.glBufferData(GLES30.GL_ARRAY_BUFFER, vertices.capacity() * 4, vertices, GLES30.GL_STATIC_DRAW)
            indices.position(0)
            GLES30.glBindBuffer(GLES30.GL_ELEMENT_ARRAY_BUFFER, indexBufferId)
            GLES30.glBufferData(GLES30.GL_ELEMENT_ARRAY_BUFFER, indices.capacity() * 4, indices, GLES30.GL_STATIC_DRAW)
            meshUploaded = true
        }

        GLES30.glBindBuffer(GLES30.GL_ARRAY_BUFFER, vertexBufferId)
        GLES30.glEnableVertexAttribArray(0)
        GLES30.glVertexAttribPointer(0, 3, GLES30.GL_FLOAT, false, 24, 0)
        GLES30.glEnableVertexAttribArray(1)
        GLES30.glVertexAttribPointer(1, 3, GLES30.GL_FLOAT, false, 24, 12)
        GLES30.glBindBuffer(GLES30.GL_ELEMENT_ARRAY_BUFFER, indexBufferId)
        GLES30.glDrawElements(GLES30.GL_TRIANGLES, indexCount, GLES30.GL_UNSIGNED_INT, 0)
        if (highlightIndexCount > 0) {
            GLES30.glUniform1i(GLES30.glGetUniformLocation(program, "uEdgeHighlight"), 0)
            GLES30.glUniform1i(GLES30.glGetUniformLocation(program, "uHighlight"), 1)
            GLES30.glBindBuffer(GLES30.GL_ELEMENT_ARRAY_BUFFER, highlightIndexBufferId)
            GLES30.glDrawElements(GLES30.GL_TRIANGLES, highlightIndexCount, GLES30.GL_UNSIGNED_INT, 0)
            GLES30.glUniform1i(GLES30.glGetUniformLocation(program, "uHighlight"), 0)
        }
        if (edgeVertexCount > 0) {
            GLES30.glUniform1i(GLES30.glGetUniformLocation(program, "uEdgeHighlight"), 1)
            GLES30.glUniform1i(GLES30.glGetUniformLocation(program, "uHighlight"), 1)
            GLES30.glLineWidth(4f)
            GLES30.glBindBuffer(GLES30.GL_ARRAY_BUFFER, edgeVertexBufferId)
            GLES30.glEnableVertexAttribArray(0)
            GLES30.glVertexAttribPointer(0, 3, GLES30.GL_FLOAT, false, 24, 0)
            GLES30.glEnableVertexAttribArray(1)
            GLES30.glVertexAttribPointer(1, 3, GLES30.GL_FLOAT, false, 24, 12)
            for ((first, count) in edgeRanges) {
                GLES30.glDrawArrays(GLES30.GL_LINE_STRIP, first, count)
            }
            GLES30.glUniform1i(GLES30.glGetUniformLocation(program, "uHighlight"), 0)
            GLES30.glUniform1i(GLES30.glGetUniformLocation(program, "uEdgeHighlight"), 0)
        }
        GLES30.glDisableVertexAttribArray(0)
        GLES30.glDisableVertexAttribArray(1)
        GLES30.glBindBuffer(GLES30.GL_ARRAY_BUFFER, 0)
        GLES30.glBindBuffer(GLES30.GL_ELEMENT_ARRAY_BUFFER, 0)
    }

    fun rayForScreen(x: Float, y: Float, viewWidth: Int, viewHeight: Int): FloatArray? {
        val inverse = inverseMvpSnapshot ?: return null
        if (viewWidth <= 0 || viewHeight <= 0) return null
        val ndcX = 2f * x / viewWidth.toFloat() - 1f
        val ndcY = 1f - 2f * y / viewHeight.toFloat()
        val nearClip = floatArrayOf(ndcX, ndcY, -1f, 1f)
        val farClip = floatArrayOf(ndcX, ndcY, 1f, 1f)
        val nearWorld = FloatArray(4)
        val farWorld = FloatArray(4)
        Matrix.multiplyMV(nearWorld, 0, inverse, 0, nearClip, 0)
        Matrix.multiplyMV(farWorld, 0, inverse, 0, farClip, 0)
        if (kotlin.math.abs(nearWorld[3]) < 1e-8f || kotlin.math.abs(farWorld[3]) < 1e-8f) return null
        val ox = nearWorld[0] / nearWorld[3]
        val oy = nearWorld[1] / nearWorld[3]
        val oz = nearWorld[2] / nearWorld[3]
        val fx = farWorld[0] / farWorld[3]
        val fy = farWorld[1] / farWorld[3]
        val fz = farWorld[2] / farWorld[3]
        val dx = fx - ox
        val dy = fy - oy
        val dz = fz - oz
        val length = sqrt(dx * dx + dy * dy + dz * dz)
        if (length <= 1e-8f) return null

        // Convert a 12 dp finger-edge hit target to model-space units.
        // Picking tolerance follows camera zoom and the current viewport height.
        val distanceToTarget = 4.2f / zoomFactor
        val halfFovTangent = tan(Math.toRadians(21.0)).toFloat()
        val worldUnitsPerPixel = (2f * distanceToTarget * halfFovTangent) /
            (max(1, viewHeight).toFloat() * max(modelScale, 1.0e-8f))
        val edgeTolerance = worldUnitsPerPixel * 12f * density

        return floatArrayOf(ox, oy, oz, dx / length, dy / length, dz / length, edgeTolerance)
    }

    fun setMesh(vertices: FloatArray, indices: IntArray, bounds: DoubleArray, faceIds: IntArray) {
        if (vertices.size < 18 || vertices.size % 6 != 0 || indices.isEmpty() || bounds.size != 6) {
            clearMesh()
            return
        }
        vertexBuffer = ByteBuffer.allocateDirect(vertices.size * 4)
            .order(ByteOrder.nativeOrder()).asFloatBuffer().apply { put(vertices); position(0) }
        indexBuffer = ByteBuffer.allocateDirect(indices.size * 4)
            .order(ByteOrder.nativeOrder()).asIntBuffer().apply { put(indices); position(0) }
        indexCount = indices.size
        triangleFaceIds = faceIds.copyOf()
        selectedFaceIds = IntArray(0)
        highlightIndexCount = 0
        edgeVertexCount = 0
        edgeRanges = emptyList()
        meshUploaded = false
        centerX = ((bounds[0] + bounds[3]) * 0.5).toFloat()
        centerY = ((bounds[1] + bounds[4]) * 0.5).toFloat()
        centerZ = ((bounds[2] + bounds[5]) * 0.5).toFloat()
        val extent = max(
            (bounds[3] - bounds[0]).toFloat(),
            max((bounds[4] - bounds[1]).toFloat(), (bounds[5] - bounds[2]).toFloat())
        )
        modelScale = if (extent > 1e-9f) 2.2f / extent else 1f
        resetCamera()
    }

    fun setHighlightedFaces(faceIds: IntArray) {
        selectedFaceIds = faceIds.distinct().filter { it >= 0 }.toIntArray()
        val source = indexBuffer ?: return
        val selected = ArrayList<Int>()
        for (triangle in triangleFaceIds.indices) {
            if (selectedFaceIds.contains(triangleFaceIds[triangle])) {
                val offset = triangle * 3
                selected.add(source.get(offset)); selected.add(source.get(offset + 1)); selected.add(source.get(offset + 2))
            }
        }
        val data = selected.toIntArray()
        highlightIndexCount = data.size
        if (data.isNotEmpty()) {
            val buffer = ByteBuffer.allocateDirect(data.size * 4).order(ByteOrder.nativeOrder()).asIntBuffer()
            buffer.put(data).position(0)
            GLES30.glBindBuffer(GLES30.GL_ELEMENT_ARRAY_BUFFER, highlightIndexBufferId)
            GLES30.glBufferData(GLES30.GL_ELEMENT_ARRAY_BUFFER, data.size * 4, buffer, GLES30.GL_DYNAMIC_DRAW)
            GLES30.glBindBuffer(GLES30.GL_ELEMENT_ARRAY_BUFFER, 0)
        }
    }

    fun setHighlightedEdges(picks: List<DoubleArray>) {
        val vertices = ArrayList<Float>()
        val ranges = ArrayList<Pair<Int, Int>>()
        for (pick in picks) {
            if (pick.size < 12) continue
            val pointCount = pick[11].toInt().coerceIn(0, 512)
            if (pointCount < 2 || pick.size < 12 + pointCount * 3) continue
            val first = vertices.size / 6
            for (i in 0 until pointCount) {
                val offset = 12 + i * 3
                vertices.add(pick[offset].toFloat())
                vertices.add(pick[offset + 1].toFloat())
                vertices.add(pick[offset + 2].toFloat())
                vertices.add(0f); vertices.add(0f); vertices.add(1f)
            }
            ranges.add(first to pointCount)
        }
        edgeRanges = ranges
        edgeVertexCount = vertices.size / 6
        if (edgeVertexCount == 0) return
        val data = FloatArray(vertices.size) { vertices[it] }
        val buffer = ByteBuffer.allocateDirect(data.size * 4)
            .order(ByteOrder.nativeOrder()).asFloatBuffer()
        buffer.put(data).position(0)
        GLES30.glBindBuffer(GLES30.GL_ARRAY_BUFFER, edgeVertexBufferId)
        GLES30.glBufferData(GLES30.GL_ARRAY_BUFFER, data.size * 4, buffer, GLES30.GL_DYNAMIC_DRAW)
        GLES30.glBindBuffer(GLES30.GL_ARRAY_BUFFER, 0)
    }

    fun clearMesh() {
        triangleFaceIds = IntArray(0)
        selectedFaceIds = IntArray(0)
        highlightIndexCount = 0
        edgeVertexCount = 0
        edgeRanges = emptyList()
        vertexBuffer = null
        indexBuffer = null
        indexCount = 0
        meshUploaded = false
        resetCamera()
    }

    fun resetCamera() {
        rotationX = -20f
        rotationY = 35f
        panX = 0f
        panY = 0f
        zoomFactor = 1f
    }

    fun setStandardView(view: String) {
        when (view) {
            "front" -> { rotationX = 0f; rotationY = 0f }
            "back" -> { rotationX = 0f; rotationY = 180f }
            "right" -> { rotationX = 0f; rotationY = 90f }
            "left" -> { rotationX = 0f; rotationY = -90f }
            "top" -> { rotationX = -89f; rotationY = 0f }
            "bottom" -> { rotationX = 89f; rotationY = 0f }
            else -> { rotationX = -20f; rotationY = 35f }
        }
        panX = 0f
        panY = 0f
        zoomFactor = 1f
    }

    fun orbit(dx: Float, dy: Float) {
        rotationY = (rotationY + dx) % 360f
        rotationX = min(89f, max(-89f, rotationX + dy))
    }

    fun pan(dx: Float, dy: Float) {
        panX += dx * 2f
        panY += dy * 2f
    }

    fun zoom(factor: Float) {
        if (factor.isFinite() && factor > 0f) zoomFactor = min(8f, max(0.25f, zoomFactor * factor))
    }

    private fun createProgram(vertexSource: String, fragmentSource: String): Int {
        val vertex = compileShader(GLES30.GL_VERTEX_SHADER, vertexSource)
        val fragment = compileShader(GLES30.GL_FRAGMENT_SHADER, fragmentSource)
        val result = GLES30.glCreateProgram()
        GLES30.glAttachShader(result, vertex)
        GLES30.glAttachShader(result, fragment)
        GLES30.glBindAttribLocation(result, 0, "aPosition")
        GLES30.glBindAttribLocation(result, 1, "aNormal")
        GLES30.glLinkProgram(result)
        val status = IntArray(1)
        GLES30.glGetProgramiv(result, GLES30.GL_LINK_STATUS, status, 0)
        if (status[0] == 0) {
            val message = GLES30.glGetProgramInfoLog(result)
            GLES30.glDeleteProgram(result)
            throw IllegalStateException("OpenGL program link failed: $message")
        }
        GLES30.glDeleteShader(vertex)
        GLES30.glDeleteShader(fragment)
        return result
    }

    private fun compileShader(type: Int, source: String): Int {
        val shader = GLES30.glCreateShader(type)
        GLES30.glShaderSource(shader, source)
        GLES30.glCompileShader(shader)
        val status = IntArray(1)
        GLES30.glGetShaderiv(shader, GLES30.GL_COMPILE_STATUS, status, 0)
        if (status[0] == 0) {
            val message = GLES30.glGetShaderInfoLog(shader)
            GLES30.glDeleteShader(shader)
            throw IllegalStateException("OpenGL shader compile failed: $message")
        }
        return shader
    }

    companion object {
        private const val VERTEX_SHADER = """
            #version 300 es
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec3 aNormal;
            uniform mat4 uMvp;
            uniform mat4 uModel;
            out vec3 vNormal;
            void main() {
                gl_Position = uMvp * vec4(aPosition, 1.0);
                vNormal = mat3(uModel) * aNormal;
            }
        """
        private const val FRAGMENT_SHADER = """
            #version 300 es
            precision highp float;
            in vec3 vNormal;
            uniform int uHighlight;
            uniform int uEdgeHighlight;
            out vec4 outColor;
            void main() {
                vec3 n = normalize(vNormal);
                vec3 lightDirection = normalize(vec3(-0.45, 0.75, 0.55));
                float diffuse = max(dot(n, lightDirection), 0.0);
                float light = 0.28 + diffuse * 0.72;
                vec3 baseColor = (uEdgeHighlight == 1) ? vec3(1.0, 0.82, 0.05) :
                    ((uHighlight == 1) ? vec3(1.0, 0.12, 0.035) : vec3(0.55, 0.68, 0.82));
                outColor = vec4(baseColor * (uHighlight == 1 ? 1.0 : light), 1.0);
            }
        """
    }
}
