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
import javax.microedition.khronos.egl.EGLConfig
import javax.microedition.khronos.opengles.GL10

/** OpenGL ES 3.0 mesh viewer: one-finger orbit, two-finger pan/pinch. */
class CadGlSurfaceView(context: Context) : GLSurfaceView(context) {
    private val cadRenderer = CadGlRenderer()
    private var lastX = 0f
    private var lastY = 0f
    private var lastSpan = 0f

    init {
        setEGLContextClientVersion(3)
        setPreserveEGLContextOnPause(true)
        setRenderer(cadRenderer)
        renderMode = RENDERMODE_WHEN_DIRTY
    }

    fun submitMesh(vertices: FloatArray, indices: IntArray, bounds: DoubleArray) {
        queueEvent { cadRenderer.setMesh(vertices, indices, bounds); requestRender() }
    }

    fun clearMesh() {
        queueEvent { cadRenderer.clearMesh(); requestRender() }
    }

    fun fitModel() {
        queueEvent { cadRenderer.resetCamera(); requestRender() }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                lastX = event.x
                lastY = event.y
                lastSpan = 0f
                return true
            }
            MotionEvent.ACTION_POINTER_DOWN -> {
                lastSpan = pointerSpan(event)
                lastX = (event.getX(0) + event.getX(1)) * 0.5f
                lastY = (event.getY(0) + event.getY(1)) * 0.5f
                return true
            }
            MotionEvent.ACTION_MOVE -> {
                if (event.pointerCount == 1) {
                    val dx = event.x - lastX
                    val dy = event.y - lastY
                    queueEvent { cadRenderer.orbit(dx * 0.55f, dy * 0.55f); requestRender() }
                    lastX = event.x
                    lastY = event.y
                } else if (event.pointerCount >= 2) {
                    val x = (event.getX(0) + event.getX(1)) * 0.5f
                    val y = (event.getY(0) + event.getY(1)) * 0.5f
                    val span = pointerSpan(event)
                    val dx = x - lastX
                    val dy = y - lastY
                    queueEvent {
                        if (lastSpan > 0f && span > 0f) cadRenderer.zoom(span / lastSpan)
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
                lastSpan = 0f
                return true
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                lastSpan = 0f
                return true
            }
        }
        return true
    }

    private fun pointerSpan(event: MotionEvent): Float {
        if (event.pointerCount < 2) return 0f
        val dx = event.getX(0) - event.getX(1)
        val dy = event.getY(0) - event.getY(1)
        return sqrt(dx * dx + dy * dy)
    }
}

private class CadGlRenderer : GLSurfaceView.Renderer {
    private var program = 0
    private var vertexBuffer: FloatBuffer? = null
    private var indexBuffer: IntBuffer? = null
    private var indexCount = 0
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

    override fun onSurfaceCreated(gl: GL10?, config: EGLConfig?) {
        GLES30.glClearColor(0.075f, 0.09f, 0.12f, 1f)
        GLES30.glEnable(GLES30.GL_DEPTH_TEST)
        GLES30.glDisable(GLES30.GL_CULL_FACE)
        program = createProgram(VERTEX_SHADER, FRAGMENT_SHADER)
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

        GLES30.glUseProgram(program)
        GLES30.glUniformMatrix4fv(GLES30.glGetUniformLocation(program, "uMvp"), 1, false, mvp, 0)
        GLES30.glUniformMatrix4fv(GLES30.glGetUniformLocation(program, "uModel"), 1, false, model, 0)

        vertices.position(0)
        GLES30.glEnableVertexAttribArray(0)
        GLES30.glVertexAttribPointer(0, 3, GLES30.GL_FLOAT, false, 24, vertices)
        vertices.position(3)
        GLES30.glEnableVertexAttribArray(1)
        GLES30.glVertexAttribPointer(1, 3, GLES30.GL_FLOAT, false, 24, vertices)
        indices.position(0)
        GLES30.glDrawElements(GLES30.GL_TRIANGLES, indexCount, GLES30.GL_UNSIGNED_INT, indices)
        GLES30.glDisableVertexAttribArray(0)
        GLES30.glDisableVertexAttribArray(1)
    }

    fun setMesh(vertices: FloatArray, indices: IntArray, bounds: DoubleArray) {
        if (vertices.size < 18 || vertices.size % 6 != 0 || indices.isEmpty() || bounds.size != 6) {
            clearMesh()
            return
        }
        vertexBuffer = ByteBuffer.allocateDirect(vertices.size * 4)
            .order(ByteOrder.nativeOrder()).asFloatBuffer().apply { put(vertices); position(0) }
        indexBuffer = ByteBuffer.allocateDirect(indices.size * 4)
            .order(ByteOrder.nativeOrder()).asIntBuffer().apply { put(indices); position(0) }
        indexCount = indices.size
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

    fun clearMesh() {
        vertexBuffer = null
        indexBuffer = null
        indexCount = 0
        resetCamera()
    }

    fun resetCamera() {
        rotationX = -20f
        rotationY = 35f
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
            out vec4 outColor;
            void main() {
                vec3 n = normalize(vNormal);
                vec3 lightDirection = normalize(vec3(-0.45, 0.75, 0.55));
                float diffuse = max(dot(n, lightDirection), 0.0);
                float light = 0.28 + diffuse * 0.72;
                outColor = vec4(vec3(0.55, 0.68, 0.82) * light, 1.0);
            }
        """
    }
}
