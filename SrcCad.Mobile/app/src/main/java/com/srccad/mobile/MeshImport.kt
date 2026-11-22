package com.srccad.mobile

import android.util.Xml
import org.xmlpull.v1.XmlPullParser
import java.io.File
import java.io.InputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.zip.ZipFile
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sqrt

/** Render-ready triangle soup: 6 floats per vertex (xyz + flat normal), 3 vertices per triangle. */
class MeshData(val vertices: FloatArray, val indices: IntArray, val bounds: DoubleArray, val triangles: Int)

/**
 * Free, dependency-free readers for mesh formats (STL binary/ASCII, OBJ, 3MF).
 * These formats carry triangles only, so there is NO B-Rep face identity: face selection and
 * precise measurement are intentionally unavailable for them.
 */
object MeshFileReaders {
    const val MAX_TRIANGLES = 2_000_000

    private class Builder {
        var data = FloatArray(1 shl 16)
        var floatCount = 0
        var triangles = 0
        val min = doubleArrayOf(Double.MAX_VALUE, Double.MAX_VALUE, Double.MAX_VALUE)
        val max = doubleArrayOf(-Double.MAX_VALUE, -Double.MAX_VALUE, -Double.MAX_VALUE)

        fun tri(ax: Double, ay: Double, az: Double, bx: Double, by: Double, bz: Double, cx: Double, cy: Double, cz: Double) {
            val ux = bx - ax; val uy = by - ay; val uz = bz - az
            val vx = cx - ax; val vy = cy - ay; val vz = cz - az
            var nx = uy * vz - uz * vy
            var ny = uz * vx - ux * vz
            var nz = ux * vy - uy * vx
            val len = sqrt(nx * nx + ny * ny + nz * nz)
            if (len <= 1.0e-18 || len.isNaN()) return
            nx /= len; ny /= len; nz /= len
            if (triangles >= MAX_TRIANGLES) {
                throw IllegalStateException(
                    "Model çok büyük: $MAX_TRIANGLES üçgen sınırı aşıldı (bellek koruması)."
                )
            }
            if (floatCount + 18 > data.size) data = data.copyOf(data.size * 2)
            put(ax, ay, az, nx, ny, nz); put(bx, by, bz, nx, ny, nz); put(cx, cy, cz, nx, ny, nz)
            triangles++
        }

        private fun put(x: Double, y: Double, z: Double, nx: Double, ny: Double, nz: Double) {
            data[floatCount++] = x.toFloat(); data[floatCount++] = y.toFloat(); data[floatCount++] = z.toFloat()
            data[floatCount++] = nx.toFloat(); data[floatCount++] = ny.toFloat(); data[floatCount++] = nz.toFloat()
            min[0] = min(min[0], x); min[1] = min(min[1], y); min[2] = min(min[2], z)
            max[0] = max(max[0], x); max[1] = max(max[1], y); max[2] = max(max[2], z)
        }

        fun finish(format: String): MeshData {
            if (triangles == 0) throw IllegalStateException("$format dosyasında çizilebilir üçgen bulunamadı.")
            return MeshData(
                data.copyOf(floatCount),
                IntArray(triangles * 3) { it },
                doubleArrayOf(min[0], min[1], min[2], max[0], max[1], max[2]),
                triangles
            )
        }
    }

    fun readStl(file: File): MeshData {
        val length = file.length()
        if (length < 15) throw IllegalArgumentException("STL dosyası çok kısa veya bozuk.")
        if (length > 400L * 1024 * 1024) throw IllegalArgumentException("STL dosyası 400 MB üstünde; bu cihaz için çok büyük.")
        val bytes = file.readBytes()
        val buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        val declared = if (bytes.size >= 84) buffer.getInt(80).toLong() and 0xFFFFFFFFL else -1L
        val builder = Builder()
        if (declared >= 0 && 84L + declared * 50L == bytes.size.toLong()) {
            var offset = 84
            for (i in 0 until declared.toInt()) {
                val b = offset + 12
                builder.tri(
                    buffer.getFloat(b).toDouble(), buffer.getFloat(b + 4).toDouble(), buffer.getFloat(b + 8).toDouble(),
                    buffer.getFloat(b + 12).toDouble(), buffer.getFloat(b + 16).toDouble(), buffer.getFloat(b + 20).toDouble(),
                    buffer.getFloat(b + 24).toDouble(), buffer.getFloat(b + 28).toDouble(), buffer.getFloat(b + 32).toDouble()
                )
                offset += 50
            }
        } else {
            val text = String(bytes, Charsets.ISO_8859_1)
            if (!text.contains("facet", ignoreCase = true) || !text.contains("vertex", ignoreCase = true)) {
                throw IllegalArgumentException("STL biçimi tanınamadı (ne ikili ne ASCII STL).")
            }
            val v = DoubleArray(9)
            var count = 0
            for (raw in text.lineSequence()) {
                val line = raw.trim()
                if (!line.startsWith("vertex", ignoreCase = true)) continue
                val parts = line.split(Regex("\\s+"))
                if (parts.size < 4) continue
                v[count * 3] = parts[1].toDouble(); v[count * 3 + 1] = parts[2].toDouble(); v[count * 3 + 2] = parts[3].toDouble()
                count++
                if (count == 3) {
                    builder.tri(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8])
                    count = 0
                }
            }
        }
        return builder.finish("STL")
    }

    fun readObj(file: File): MeshData {
        if (file.length() > 400L * 1024 * 1024) throw IllegalArgumentException("OBJ dosyası 400 MB üstünde; bu cihaz için çok büyük.")
        var xyz = FloatArray(3 * 4096)
