package com.srccad.mobile

/** Kotlin/JNI boundary: CAD topology stays in native C++; Kotlin receives render arrays. */
object NativeCadEngine {
    init { System.loadLibrary("srccad_mobile") }
    external fun nativeCreate(): Long
    external fun nativeDestroy(handle: Long)
    external fun nativeEngineStatus(): String
    /** JSON: { ok, message, faces, triangles }. */
    external fun nativeLoadStep(handle: Long, absolutePath: String): String
    /** Loads STEP, IGES, or BREP using the matching OCCT reader. */
    external fun nativeLoadCad(handle: Long, absolutePath: String, format: String): String
    /** Loads a self-contained 100 x 50 x 20 mm display-only sample box. */
    external fun nativeLoadDemoBox(handle: Long): String
    /** Interleaved XYZ + normal data (6 floats per vertex). */
    external fun nativeGetVertices(handle: Long): FloatArray
    external fun nativeGetIndices(handle: Long): IntArray
    external fun nativeGetTriangleFaceIndices(handle: Long): IntArray
    /** xmin, ymin, zmin, xmax, ymax, zmax. */
    external fun nativeGetBounds(handle: Long): DoubleArray
    /** Cast a model-space ray and classify the hit B-rep edge/face.
     * Returns kind, XYZ, primary/secondary metrics, direction XYZ and direction-valid flag. */
    external fun nativePickGeometry(
        handle: Long, origin: DoubleArray, direction: DoubleArray, edgeTolerance: Double
    ): DoubleArray
    /** Returns distance, delta-X, delta-Y, delta-Z in model units. */
    external fun nativeMeasurePoints(handle: Long, pointA: DoubleArray, pointB: DoubleArray): DoubleArray
}
