package com.srccad.mobile

/** Kotlin/JNI boundary: CAD topology stays in native C++; Kotlin receives render arrays. */
object NativeCadEngine {
    init { System.loadLibrary("srccad_mobile") }
    external fun nativeCreate(): Long
    external fun nativeDestroy(handle: Long)
    external fun nativeEngineStatus(): String
    /** JSON: { ok, message, faces, triangles }. */
    external fun nativeLoadStep(handle: Long, absolutePath: String): String
    /** Interleaved XYZ + normal data (6 floats per vertex). */
    external fun nativeGetVertices(handle: Long): FloatArray
    external fun nativeGetIndices(handle: Long): IntArray
    /** xmin, ymin, zmin, xmax, ymax, zmax. */
    external fun nativeGetBounds(handle: Long): DoubleArray
}
