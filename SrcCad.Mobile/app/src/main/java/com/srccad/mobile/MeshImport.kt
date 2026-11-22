package com.srccad.mobile

import android.util.Xml
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.zip.ZipFile
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sqrt

/** Render-ready triangle soup: 6 floats per vertex (xyz + flat normal), 3 vertices per triangle. */
class MeshData(val vertices: FloatArray, val indices: IntArray, val bounds: DoubleArray, val triangles: Int)

/**
 * Dependency-free readers for STL, OBJ and 3MF.
 * Triangle formats do not retain B-Rep face identity, so precise topology measurement is unavailable.
 */
object MeshFileReaders {
    const val MAX_TRIANGLES = 2_000_000
    private const val MAX_FILE_BYTES = 400L * 1024L * 1024L

    private class Builder {
        private var data = FloatArray(1 shl 16)
        private var floatCount = 0
        var triangles = 0
            private set
        private val min = doubleArrayOf(Double.MAX_VALUE, Double.MAX_VALUE, Double.MAX_VALUE)
        private val max = doubleArrayOf(-Double.MAX_VALUE, -Double.MAX_VALUE, -Double.MAX_VALUE)

        fun tri(ax: Double, ay: Double, az: Double, bx: Double, by: Double, bz: Double, cx: Double, cy: Double, cz: Double) {
            val ux = bx - ax; val uy = by - ay; val uz = bz - az
            val vx = cx - ax; val vy = cy - ay; val vz = cz - az
            var nx = uy * vz - uz * vy
            var ny = uz * vx - ux * vz
            var nz = ux * vy - uy * vx
            val len = sqrt(nx * nx + ny * ny + nz * nz)
            if (!len.isFinite() || len <= 1.0e-18) return
            nx /= len; ny /= len; nz /= len
            if (triangles >= MAX_TRIANGLES) {
                throw IllegalStateException("Model çok büyük: $MAX_TRIANGLES üçgen sınırı aşıldı.")
            }
            if (floatCount + 18 > data.size) data = data.copyOf(max(data.size * 2, floatCount + 18))
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
            if (triangles == 0) throw IllegalArgumentException("$format dosyasında çizilebilir üçgen bulunamadı.")
            return MeshData(
                data.copyOf(floatCount),
                IntArray(triangles * 3) { it },
                doubleArrayOf(min[0], min[1], min[2], max[0], max[1], max[2]),
                triangles
            )
        }
    }

    fun readStl(file: File): MeshData {
        requireReadable(file, "STL")
        val bytes = file.readBytes()
        if (bytes.size < 15) throw IllegalArgumentException("STL dosyası çok kısa veya bozuk.")
        val buffer = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        val declared = if (bytes.size >= 84) buffer.getInt(80).toLong() and 0xFFFFFFFFL else -1L
        val builder = Builder()
        if (declared >= 0 && 84L + declared * 50L == bytes.size.toLong()) {
            var offset = 84
            repeat(declared.toInt()) {
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
            if (!text.contains("facet", true) || !text.contains("vertex", true)) {
                throw IllegalArgumentException("STL biçimi tanınamadı (ne ikili ne ASCII STL).")
            }
            val vertices = ArrayList<DoubleArray>(3)
            for (raw in text.lineSequence()) {
                val line = raw.trim()
                if (!line.startsWith("vertex", true)) continue
                val parts = line.split(Regex("\\s+"))
                if (parts.size < 4) continue
                val point = doubleArrayOf(parts[1].toDouble(), parts[2].toDouble(), parts[3].toDouble())
                if (point.all { it.isFinite() }) vertices.add(point)
                if (vertices.size == 3) {
                    builder.tri(
                        vertices[0][0], vertices[0][1], vertices[0][2],
                        vertices[1][0], vertices[1][1], vertices[1][2],
                        vertices[2][0], vertices[2][1], vertices[2][2]
                    )
                    vertices.clear()
                }
            }
        }
        return builder.finish("STL")
    }

    fun readObj(file: File): MeshData {
        requireReadable(file, "OBJ")
        val vertices = ArrayList<DoubleArray>()
        val builder = Builder()
        file.bufferedReader(Charsets.UTF_8).useLines { lines ->
            lines.forEach { raw ->
                val line = raw.substringBefore('#').trim()
                if (line.isEmpty()) return@forEach
                val parts = line.split(Regex("\\s+"))
                when (parts[0]) {
                    "v" -> {
                        if (parts.size < 4) return@forEach
                        val point = doubleArrayOf(parts[1].toDouble(), parts[2].toDouble(), parts[3].toDouble())
                        if (!point.all { it.isFinite() }) throw IllegalArgumentException("OBJ dosyasında geçersiz vertex koordinatı.")
                        vertices.add(point)
                        if (vertices.size > 10_000_000) throw IllegalArgumentException("OBJ dosyasında çok fazla vertex var.")
                    }
                    "f" -> {
                        if (parts.size < 4) return@forEach
                        val face = parts.drop(1).map { token ->
                            val rawIndex = token.substringBefore('/').toInt()
                            val index = if (rawIndex > 0) rawIndex - 1 else vertices.size + rawIndex
                            if (index !in vertices.indices) throw IllegalArgumentException("OBJ yüzü geçersiz vertex indeksine başvuruyor.")
                            index
                        }
                        for (i in 1 until face.size - 1) {
                            val a = vertices[face[0]]; val b = vertices[face[i]]; val c = vertices[face[i + 1]]
                            builder.tri(a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2])
                        }
                    }
                }
            }
        }
        return builder.finish("OBJ")
    }

    fun read3mf(file: File): MeshData {
        requireReadable(file, "3MF")
        val builder = Builder()
        ZipFile(file).use { zip ->
            val entry = zip.getEntry("3D/3dmodel.model")
                ?: zip.entries().asSequence().firstOrNull { it.name.endsWith(".model", true) }
                ?: throw IllegalArgumentException("3MF arşivinde 3D model dosyası bulunamadı.")
            zip.getInputStream(entry).use { input ->
                val parser = Xml.newPullParser()
                parser.setFeature(org.xmlpull.v1.XmlPullParser.FEATURE_PROCESS_NAMESPACES, false)
                parser.setInput(input, "UTF-8")
                var unitScale = 1.0
                var vertices = ArrayList<DoubleArray>()
                var triangles = ArrayList<IntArray>()
                var inMesh = false
                var event = parser.eventType
                while (event != org.xmlpull.v1.XmlPullParser.END_DOCUMENT) {
                    when (event) {
                        org.xmlpull.v1.XmlPullParser.START_TAG -> when (parser.name.substringAfter(':')) {
                            "model" -> {
                                unitScale = when (parser.getAttributeValue(null, "unit")?.lowercase()) {
                                    "micron" -> 0.001
                                    "millimeter", null -> 1.0
                                    "centimeter" -> 10.0
                                    "inch" -> 25.4
                                    "foot" -> 304.8
                                    "meter" -> 1000.0
                                    else -> 1.0
                                }
                            }
                            "mesh" -> {
                                inMesh = true
                                vertices = ArrayList()
                                triangles = ArrayList()
                            }
                            "vertex" -> if (inMesh) {
                                val x = parser.getAttributeValue(null, "x")?.toDoubleOrNull()
                                val y = parser.getAttributeValue(null, "y")?.toDoubleOrNull()
                                val z = parser.getAttributeValue(null, "z")?.toDoubleOrNull()
                                if (x == null || y == null || z == null || !x.isFinite() || !y.isFinite() || !z.isFinite()) {
                                    throw IllegalArgumentException("3MF dosyasında geçersiz vertex koordinatı.")
                                }
                                vertices.add(doubleArrayOf(x * unitScale, y * unitScale, z * unitScale))
                            }
                            "triangle" -> if (inMesh) {
                                val v1 = parser.getAttributeValue(null, "v1")?.toIntOrNull()
                                val v2 = parser.getAttributeValue(null, "v2")?.toIntOrNull()
                                val v3 = parser.getAttributeValue(null, "v3")?.toIntOrNull()
                                if (v1 == null || v2 == null || v3 == null) {
                                    throw IllegalArgumentException("3MF dosyasında geçersiz triangle indeksi.")
                                }
                                triangles.add(intArrayOf(v1, v2, v3))
                            }
                        }
                        org.xmlpull.v1.XmlPullParser.END_TAG -> if (parser.name.substringAfter(':') == "mesh" && inMesh) {
                            for (triangle in triangles) {
                                val a = vertices.getOrNull(triangle[0]) ?: throw IllegalArgumentException("3MF triangle vertex indeksi aralık dışında.")
                                val b = vertices.getOrNull(triangle[1]) ?: throw IllegalArgumentException("3MF triangle vertex indeksi aralık dışında.")
                                val c = vertices.getOrNull(triangle[2]) ?: throw IllegalArgumentException("3MF triangle vertex indeksi aralık dışında.")
                                builder.tri(a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2])
                            }
                            inMesh = false
                        }
                    }
                    event = parser.next()
                }
            }
        }
        return builder.finish("3MF")
    }

    private fun requireReadable(file: File, format: String) {
        if (!file.isFile || !file.canRead()) throw IllegalArgumentException("$format dosyası okunamıyor.")
        if (file.length() <= 0L) throw IllegalArgumentException("$format dosyası boş.")
        if (file.length() > MAX_FILE_BYTES) throw IllegalArgumentException("$format dosyası 400 MB üstünde; bu cihaz için çok büyük.")
    }
}
