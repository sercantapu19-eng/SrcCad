package com.srccad.mobile

import android.graphics.Color
import java.io.File
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.atan
import kotlin.math.atan2
import kotlin.math.ceil
import kotlin.math.cos
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sin

/** Line segments (x1,y1,x2,y2 ...) of one display colour. */
class DxfColorGroup(val argb: Int, val points: FloatArray)

class DxfDrawing(
    val groups: List<DxfColorGroup>,
    val bounds: DoubleArray, // minX, minY, maxX, maxY
    val entityCount: Int,
    val layerCount: Int,
    val skipped: Map<String, Int>
) {
    fun skippedSummary(): String =
        if (skipped.isEmpty()) "" else skipped.entries.joinToString(", ") { it.key + " ×" + it.value }
}

/**
 * Free ASCII-DXF reader written for SrcCad (no third-party/GPL code).
 * Supported: LINE, CIRCLE, ARC, LWPOLYLINE/POLYLINE (with bulges), ELLIPSE, SPLINE, INSERT (+blocks, arrays),
 * DIMENSION (its generated block geometry), layers (colour / off / frozen).
 * NOT drawn: TEXT/MTEXT, HATCH, SOLID, 3DFACE, IMAGE, LEADER, POINT, OCS extrusion mirroring. Binary DXF unsupported.
 */
object DxfReader {
    private const val MAX_SEGMENTS = 4_000_000
    private val DEFAULT_COLOR = Color.rgb(235, 240, 245)

    private class Ent(val type: String) {
        val codes = ArrayList<Int>()
        val values = ArrayList<String>()
        fun add(code: Int, value: String) { codes.add(code); values.add(value) }
        fun str(code: Int): String? {
            for (i in codes.indices) if (codes[i] == code) return values[i]
            return null
        }
        fun num(code: Int, def: Double = 0.0): Double = str(code)?.trim()?.toDoubleOrNull() ?: def
        fun list(code: Int): DoubleArray {
            val out = ArrayList<Double>()
            for (i in codes.indices) if (codes[i] == code) out.add(values[i].trim().toDoubleOrNull() ?: 0.0)
            return out.toDoubleArray()
        }
    }

    private class Layer(val argb: Int, val hidden: Boolean)
    private class Block(val baseX: Double, val baseY: Double) { val entities = ArrayList<Ent>() }

    private class Mat(val a: Double, val b: Double, val c: Double, val d: Double, val tx: Double, val ty: Double) {
        fun x(px: Double, py: Double) = a * px + c * py + tx
        fun y(px: Double, py: Double) = b * px + d * py + ty
        /** Applies [o] first, then this. */
        fun times(o: Mat) = Mat(
            a * o.a + c * o.b, b * o.a + d * o.b, a * o.c + c * o.d, b * o.c + d * o.d,
            a * o.tx + c * o.ty + tx, b * o.tx + d * o.ty + ty
        )
        companion object { val I = Mat(1.0, 0.0, 0.0, 1.0, 0.0, 0.0) }
    }

    private class FloatBuf {
        var data = FloatArray(1 shl 12)
        var size = 0
        fun add4(a: Float, b: Float, c: Float, d: Float) {
            if (size + 4 > data.size) data = data.copyOf(data.size * 2)
            data[size++] = a; data[size++] = b; data[size++] = c; data[size++] = d
        }
    }

    private class Ctx(val layers: Map<String, Layer>, val blocks: Map<String, Block>) {
        val buffers = HashMap<Int, FloatBuf>()
        val skipped = LinkedHashMap<String, Int>()
        var segments = 0
        var entities = 0
        var minX = Double.MAX_VALUE; var minY = Double.MAX_VALUE
        var maxX = -Double.MAX_VALUE; var maxY = -Double.MAX_VALUE
        fun skip(type: String) { skipped[type] = (skipped[type] ?: 0) + 1 }
    }

    fun read(file: File): DxfDrawing {
        if (file.length() > 150L * 1024 * 1024) throw IllegalArgumentException("DXF dosyası 150 MB üstünde; bu cihaz için çok büyük.")
        val head = file.inputStream().use { s ->
            val buf = ByteArray(32)
            val n = s.read(buf)
            String(buf, 0, max(n, 0), Charsets.ISO_8859_1)
        }
        if (head.startsWith("AutoCAD Binary DXF")) {
            throw IllegalArgumentException("İkili (binary) DXF desteklenmiyor. Dosyayı ASCII DXF olarak kaydedin.")
        }
        if (head.startsWith("AC10")) {
            throw IllegalArgumentException("Bu dosya DWG biçiminde (DXF değil). DWG kapalı bir formattır; önce DXF'e dönüştürün.")
        }

        val layers = HashMap<String, Layer>()
        val blocks = HashMap<String, Block>()
        val entities = ArrayList<Ent>()
        var section: String? = null
        var currentBlock: Block? = null
        var inBlockName: String? = null

        fun handle(e: Ent) {
            when (e.type) {
                "SECTION" -> section = e.str(2)
                "ENDSEC" -> section = null
                "BLOCK" -> {
                    currentBlock = Block(e.num(10), e.num(20))
                    inBlockName = e.str(2) ?: ""
                    blocks[inBlockName!!] = currentBlock!!
                }
                "ENDBLK" -> { currentBlock = null; inBlockName = null }
                "LAYER" -> if (section == "TABLES") {
                    val name = e.str(2) ?: return
                    val color = e.str(62)?.trim()?.toIntOrNull() ?: 7
                    val frozen = (e.str(70)?.trim()?.toIntOrNull() ?: 0) and 1 != 0
                    layers[name] = Layer(aci(abs(color)), color < 0 || frozen)
                }
                else -> when (section) {
                    "ENTITIES" -> entities.add(e)
                    "BLOCKS" -> currentBlock?.entities?.add(e)
                }
            }
        }

        var current: Ent? = null
        file.bufferedReader(Charsets.UTF_8).use { reader ->
            while (true) {
                val codeLine = reader.readLine() ?: break
                val valueLine = reader.readLine() ?: break
                val code = codeLine.trim().toIntOrNull() ?: continue
                if (code == 0) {
                    current?.let { handle(it) }
                    current = Ent(valueLine.trim())
                } else {
                    current?.add(code, valueLine)
                }
            }
            current?.let { handle(it) }
        }

        val ctx = Ctx(layers, blocks)
        process(ctx, entities, Mat.I, 0, null)
        if (ctx.segments == 0) {
            val note = if (ctx.skipped.isEmpty()) "" else " Atlanan nesneler: " + ctx.skipped.entries.joinToString { it.key + " ×" + it.value }
            throw IllegalStateException("DXF içinde çizilebilir 2B geometri bulunamadı.$note")
        }
        val groups = ctx.buffers.map { (argb, buf) -> DxfColorGroup(argb, buf.data.copyOf(buf.size)) }
        return DxfDrawing(
            groups, doubleArrayOf(ctx.minX, ctx.minY, ctx.maxX, ctx.maxY),
            ctx.entities, layers.size, ctx.skipped
        )
    }

    private fun aci(index: Int): Int = when (index) {
        1 -> Color.rgb(255, 60, 60)
        2 -> Color.rgb(255, 235, 60)
        3 -> Color.rgb(60, 230, 90)
        4 -> Color.rgb(60, 230, 235)
        5 -> Color.rgb(90, 130, 255)
        6 -> Color.rgb(240, 90, 240)
        7 -> DEFAULT_COLOR
        8 -> Color.rgb(128, 128, 128)
        9 -> Color.rgb(192, 192, 192)
        in 250..255 -> { val g = 51 + (index - 250) * 41; Color.rgb(g, g, g) }
        in 10..249 -> {
            val hue = ((index - 10) / 10) * 15f
            val sat = if ((index - 10) % 2 == 0) 1f else 0.5f
            val value = 1f - (((index - 10) % 10) / 2) * 0.12f
            Color.HSVToColor(floatArrayOf(hue, sat, value))
        }
        else -> DEFAULT_COLOR
    }

    private fun colorOf(ctx: Ctx, e: Ent, inherit: Int?): Int? {
        val layer = ctx.layers[e.str(8) ?: "0"]
        if (layer != null && layer.hidden) return null
        val c = e.str(62)?.trim()?.toIntOrNull()
        return when {
            c == null || c == 256 -> layer?.argb ?: DEFAULT_COLOR
            c == 0 -> inherit ?: layer?.argb ?: DEFAULT_COLOR
            c < 0 -> null
            else -> aci(c)
        }
    }

    private fun seg(ctx: Ctx, m: Mat, x0: Double, y0: Double, x1: Double, y1: Double, argb: Int) {
        if (ctx.segments >= MAX_SEGMENTS) throw IllegalStateException("Çizim çok büyük: $MAX_SEGMENTS çizgi sınırı aşıldı (bellek koruması).")
        val ax = m.x(x0, y0); val ay = m.y(x0, y0); val bx = m.x(x1, y1); val by = m.y(x1, y1)
        if (ax.isNaN() || ay.isNaN() || bx.isNaN() || by.isNaN()) return
        ctx.buffers.getOrPut(argb) { FloatBuf() }.add4(ax.toFloat(), ay.toFloat(), bx.toFloat(), by.toFloat())
        ctx.segments++
        ctx.minX = min(ctx.minX, min(ax, bx)); ctx.maxX = max(ctx.maxX, max(ax, bx))
        ctx.minY = min(ctx.minY, min(ay, by)); ctx.maxY = max(ctx.maxY, max(ay, by))
    }

    private fun arc(ctx: Ctx, m: Mat, cx: Double, cy: Double, r: Double, a0: Double, sweep: Double, argb: Int) {
        val n = max(4, ceil(abs(sweep) / (PI / 48)).toInt())
        var px = cx + r * cos(a0); var py = cy + r * sin(a0)
        for (i in 1..n) {
            val a = a0 + sweep * i / n
            val qx = cx + r * cos(a); val qy = cy + r * sin(a)
            seg(ctx, m, px, py, qx, qy, argb)
            px = qx; py = qy
        }
    }

    private fun bulge(ctx: Ctx, m: Mat, x0: Double, y0: Double, x1: Double, y1: Double, b: Double, argb: Int) {
        val chord = hypot(x1 - x0, y1 - y0)
        if (chord < 1.0e-12) return
        if (abs(b) < 1.0e-9) { seg(ctx, m, x0, y0, x1, y1, argb); return }
        val sweep = 4.0 * atan(b)
        val offset = chord / 2.0 * (1.0 - b * b) / (2.0 * b)
        val cx = (x0 + x1) / 2.0 + (-(y1 - y0) / chord) * offset
        val cy = (y0 + y1) / 2.0 + ((x1 - x0) / chord) * offset
        val r = hypot(x0 - cx, y0 - cy)
        arc(ctx, m, cx, cy, r, atan2(y0 - cy, x0 - cx), sweep, argb)
    }

    private fun polyline(ctx: Ctx, m: Mat, xs: List<Double>, ys: List<Double>, bl: List<Double>, closed: Boolean, argb: Int) {
        val n = xs.size
        if (n < 2) return
        val last = if (closed) n else n - 1
        for (i in 0 until last) {
            val j = (i + 1) % n
            bulge(ctx, m, xs[i], ys[i], xs[j], ys[j], bl[i], argb)
        }
    }

    private fun basis(span: Int, u: Double, p: Int, knots: DoubleArray): DoubleArray {
        val n = DoubleArray(p + 1); val left = DoubleArray(p + 1); val right = DoubleArray(p + 1)
        n[0] = 1.0
        for (j in 1..p) {
            left[j] = u - knots[span + 1 - j]; right[j] = knots[span + j] - u
            var saved = 0.0
            for (r in 0 until j) {
                val den = right[r + 1] + left[j - r]
                val temp = if (den == 0.0) 0.0 else n[r] / den
                n[r] = saved + right[r + 1] * temp
                saved = left[j - r] * temp
            }
            n[j] = saved
        }
        return n
    }

    private fun spline(ctx: Ctx, m: Mat, e: Ent, argb: Int) {
        val degree = e.num(71, 3.0).toInt()
        val knots = e.list(40)
        val cx = ArrayList<Double>(); val cy = ArrayList<Double>()
        val fx = ArrayList<Double>(); val fy = ArrayList<Double>()
        for (i in e.codes.indices) {
            val v = e.values[i].trim().toDoubleOrNull() ?: 0.0
            when (e.codes[i]) {
                10 -> { cx.add(v); cy.add(0.0) }
                20 -> if (cy.isNotEmpty()) cy[cy.size - 1] = v
                11 -> { fx.add(v); fy.add(0.0) }
                21 -> if (fy.isNotEmpty()) fy[fy.size - 1] = v
            }
        }
        val weights = e.list(41)
        val n = cx.size
        if (degree >= 1 && n > degree && knots.size == n + degree + 1) {
            val steps = max(48, n * 20)
            val u0 = knots[degree]; val u1 = knots[n]
            var px = 0.0; var py = 0.0
            for (s in 0..steps) {
                val u = if (s == steps) u1 else u0 + (u1 - u0) * s / steps
                var span = degree
                while (span < n - 1 && u >= knots[span + 1]) span++
                val nb = basis(span, u, degree, knots)
                var x = 0.0; var y = 0.0; var w = 0.0
                for (j in 0..degree) {
                    val idx = span - degree + j
                    val wt = if (weights.size == n) weights[idx] else 1.0
                    x += nb[j] * cx[idx] * wt; y += nb[j] * cy[idx] * wt; w += nb[j] * wt
                }
                if (w != 0.0) { x /= w; y /= w }
                if (s > 0) seg(ctx, m, px, py, x, y, argb)
                px = x; py = y
            }
        } else if (fx.size >= 2) {
            for (i in 0 until fx.size - 1) seg(ctx, m, fx[i], fy[i], fx[i + 1], fy[i + 1], argb)
        } else if (n >= 2) {
            // Degenerate definition: fall back to the control polygon.
            for (i in 0 until n - 1) seg(ctx, m, cx[i], cy[i], cx[i + 1], cy[i + 1], argb)
        } else ctx.skip("SPLINE(geçersiz)")
    }

    private fun process(ctx: Ctx, list: List<Ent>, m: Mat, depth: Int, inherit: Int?) {
        var i = 0
        while (i < list.size) {
            val e = list[i]
            i++
            when (e.type) {
                "LINE" -> colorOf(ctx, e, inherit)?.let {
                    seg(ctx, m, e.num(10), e.num(20), e.num(11), e.num(21), it); ctx.entities++
                }
                "CIRCLE" -> colorOf(ctx, e, inherit)?.let {
                    arc(ctx, m, e.num(10), e.num(20), e.num(40), 0.0, 2 * PI, it); ctx.entities++
                }
                "ARC" -> colorOf(ctx, e, inherit)?.let {
                    val a0 = Math.toRadians(e.num(50)); var a1 = Math.toRadians(e.num(51))
                    while (a1 <= a0) a1 += 2 * PI
                    arc(ctx, m, e.num(10), e.num(20), e.num(40), a0, a1 - a0, it); ctx.entities++
                }
                "ELLIPSE" -> colorOf(ctx, e, inherit)?.let {
                    val cx = e.num(10); val cy = e.num(20)
                    val mx = e.num(11); val my = e.num(21)
                    val ratio = e.num(40, 1.0)
                    val t0 = e.num(41, 0.0); var t1 = e.num(42, 2 * PI)
                    while (t1 <= t0) t1 += 2 * PI
                    val nx = -my * ratio; val ny = mx * ratio
                    val n = max(8, ceil((t1 - t0) / (PI / 48)).toInt())
                    var px = cx + cos(t0) * mx + sin(t0) * nx; var py = cy + cos(t0) * my + sin(t0) * ny
                    for (k in 1..n) {
                        val t = t0 + (t1 - t0) * k / n
                        val qx = cx + cos(t) * mx + sin(t) * nx; val qy = cy + cos(t) * my + sin(t) * ny
                        seg(ctx, m, px, py, qx, qy, it); px = qx; py = qy
                    }
                    ctx.entities++
                }
                "LWPOLYLINE" -> colorOf(ctx, e, inherit)?.let {
                    val xs = ArrayList<Double>(); val ys = ArrayList<Double>(); val bl = ArrayList<Double>()
                    for (k in e.codes.indices) {
                        val v = e.values[k].trim().toDoubleOrNull() ?: 0.0
                        when (e.codes[k]) {
                            10 -> { xs.add(v); ys.add(0.0); bl.add(0.0) }
                            20 -> if (ys.isNotEmpty()) ys[ys.size - 1] = v
                            42 -> if (bl.isNotEmpty()) bl[bl.size - 1] = v
                        }
                    }
                    polyline(ctx, m, xs, ys, bl, (e.num(70).toInt() and 1) != 0, it); ctx.entities++
                }
                "POLYLINE" -> {
                    val xs = ArrayList<Double>(); val ys = ArrayList<Double>(); val bl = ArrayList<Double>()
                    while (i < list.size && list[i].type != "SEQEND") {
                        val v = list[i]
                        if (v.type == "VERTEX" && (v.num(70).toInt() and 16) == 0) {
                            xs.add(v.num(10)); ys.add(v.num(20)); bl.add(v.num(42))
                        }
                        i++
                    }
                    if (i < list.size) i++ // SEQEND
                    if ((e.num(70).toInt() and (16 or 64)) != 0) ctx.skip("POLYLINE(3B ağ)")
                    else colorOf(ctx, e, inherit)?.let {
                        polyline(ctx, m, xs, ys, bl, (e.num(70).toInt() and 1) != 0, it); ctx.entities++
                    }
                }
                "SPLINE" -> colorOf(ctx, e, inherit)?.let { spline(ctx, m, e, it); ctx.entities++ }
                "INSERT" -> {
                    val block = ctx.blocks[e.str(2) ?: ""]
                    val color = colorOf(ctx, e, inherit)
                    if (block == null) ctx.skip("INSERT(blok yok)")
                    else if (color != null && depth < 8) {
                        val sx = e.num(41, 1.0); val sy = e.num(42, 1.0)
                        val rot = Math.toRadians(e.num(50))
                        val cols = e.num(70, 1.0).toInt().coerceIn(1, 1000)
                        val rows = e.num(71, 1.0).toInt().coerceIn(1, 1000)
                        val dx = e.num(44); val dy = e.num(45)
                        val cs = cos(rot); val sn = sin(rot)
                        for (r in 0 until rows) for (c in 0 until cols) {
                            val ox = c * dx * cs - r * dy * sn
                            val oy = c * dx * sn + r * dy * cs
                            val place = Mat(cs * sx, sn * sx, -sn * sy, cs * sy, e.num(10) + ox, e.num(20) + oy)
                            val total = m.times(place).times(Mat(1.0, 0.0, 0.0, 1.0, -block.baseX, -block.baseY))
                            process(ctx, block.entities, total, depth + 1, color)
                        }
                        ctx.entities++
                    }
                }
                "DIMENSION" -> {
                    // The dimension's visible geometry (lines, arrowheads) lives in an anonymous block.
                    val block = ctx.blocks[e.str(2) ?: ""]
                    val color = colorOf(ctx, e, inherit)
                    if (block != null && color != null && depth < 8) {
                        process(ctx, block.entities, m, depth + 1, color); ctx.entities++
                    } else ctx.skip("DIMENSION")
                }
                "VERTEX", "SEQEND", "ENDBLK", "BLOCK" -> {}
                else -> ctx.skip(e.type)
            }
        }
    }
}
