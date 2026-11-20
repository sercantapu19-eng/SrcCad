#include <jni.h>
#include <algorithm>
#include <cstdint>
#include <cmath>
#include <new>
#include <sstream>
#include <string>
#include <vector>
#include <limits>

#if defined(SRCCAD_WITH_OCCT)
#include <BRepBndLib.hxx>
#include <BRepBuilderAPI_MakeVertex.hxx>
#include <BRepExtrema_DistShapeShape.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRep_Tool.hxx>
#include <Bnd_Box.hxx>
#include <IFSelect_ReturnStatus.hxx>
#include <Interface_Static.hxx>
#include <Poly_Triangulation.hxx>
#include <STEPControl_Reader.hxx>
#include <TopAbs_Orientation.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <TopExp_Explorer.hxx>
#include <TopLoc_Location.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Shape.hxx>
#include <gp_Pnt.hxx>
#include <gp_Vec.hxx>
#endif

namespace {
struct CadDocument {
    std::vector<float> vertices;       // x,y,z,nx,ny,nz
    std::vector<std::int32_t> indices;
    double bounds[6] = {0, 0, 0, 0, 0, 0};
    int faceCount = 0;
    int triangleCount = 0;
    std::vector<std::int32_t> triangleFaceIndices;
    bool loaded = false;
#if defined(SRCCAD_WITH_OCCT)
    TopoDS_Shape shape;
    std::vector<TopoDS_Face> faces;
#endif
    void clear() {
        vertices.clear(); indices.clear();
        faceCount = 0; triangleCount = 0; loaded = false;
        triangleFaceIndices.clear();
#if defined(SRCCAD_WITH_OCCT)
        faces.clear();
#endif
        for (double& value : bounds) value = 0.0;
#if defined(SRCCAD_WITH_OCCT)
        shape.Nullify();
#endif
    }
};

CadDocument* fromHandle(jlong handle) {
    return reinterpret_cast<CadDocument*>(static_cast<std::uintptr_t>(handle));
}
std::string jsonEscape(const std::string& value) {
    std::ostringstream output;
    for (unsigned char ch : value) {
        switch (ch) {
            case '"': output << "\\\""; break;
            case '\\': output << "\\\\"; break;
            case '\n': output << "\\n"; break;
            case '\r': output << "\\r"; break;
            case '\t': output << "\\t"; break;
            default: if (ch < 0x20) output << ' '; else output << static_cast<char>(ch);
        }
    }
    return output.str();
}
jstring toJavaString(JNIEnv* env, const std::string& value) {
    return env->NewStringUTF(value.c_str());
}
std::string loadError(const std::string& message) {
    return std::string(R"({"ok":false,"message":")") + jsonEscape(message) + R"("})";
}

#if defined(SRCCAD_WITH_OCCT)
std::string loadStep(CadDocument& document, const std::string& path) {
    document.clear();
    // Normalize every STEP source unit to millimetres for predictable CAD measurement.
    Interface_Static::SetCVal("xstep.cascade.unit", "MM");
    STEPControl_Reader reader;
    if (reader.ReadFile(path.c_str()) != IFSelect_RetDone)
        return loadError("OCCT STEP okuyucusu dosyayı açamadı (ReadFile).");
    if (reader.TransferRoots() <= 0)
        return loadError("STEP dosyasında aktarılabilir geometrik kök bulunamadı.");

    TopoDS_Shape shape = reader.OneShape();
    if (shape.IsNull()) return loadError("STEP dosyasından geçerli B-rep şekli üretilemedi.");

    Bnd_Box box;
    BRepBndLib::Add(shape, box);
    if (box.IsVoid()) return loadError("Modelin sınır kutusu hesaplanamadı.");
    box.Get(document.bounds[0], document.bounds[1], document.bounds[2],
            document.bounds[3], document.bounds[4], document.bounds[5]);

    const double dx = document.bounds[3] - document.bounds[0];
    const double dy = document.bounds[4] - document.bounds[1];
    const double dz = document.bounds[5] - document.bounds[2];
    const double diagonal = std::sqrt(dx * dx + dy * dy + dz * dz);
    const double deflection = std::max(0.01, diagonal * 0.001);
    BRepMesh_IncrementalMesh mesher(shape, deflection, false, 0.35, true);
    if (!mesher.IsDone()) return loadError("Model üçgen ağına dönüştürülemedi.");

    for (TopExp_Explorer explorer(shape, TopAbs_FACE); explorer.More(); explorer.Next()) {
        const TopoDS_Face face = TopoDS::Face(explorer.Current());
        TopLoc_Location location;
        const auto triangulation = BRep_Tool::Triangulation(face, location);
        if (triangulation.IsNull()) continue;
        ++document.faceCount;
        const std::int32_t faceIndex = static_cast<std::int32_t>(document.faces.size());
        document.faces.push_back(face);
        const auto& nodes = triangulation->Nodes();
        const auto& triangles = triangulation->Triangles();

        for (int index = triangles.Lower(); index <= triangles.Upper(); ++index) {
            int n1 = 0, n2 = 0, n3 = 0;
            triangles(index).Get(n1, n2, n3);
            if (face.Orientation() == TopAbs_REVERSED) std::swap(n2, n3);

            const gp_Pnt p1 = nodes(n1).Transformed(location.Transformation());
            const gp_Pnt p2 = nodes(n2).Transformed(location.Transformation());
            const gp_Pnt p3 = nodes(n3).Transformed(location.Transformation());
            gp_Vec normal = gp_Vec(p1, p2).Crossed(gp_Vec(p1, p3));
            if (normal.SquareMagnitude() <= 1.0e-24) continue;
            normal.Normalize();

            const std::int32_t base = static_cast<std::int32_t>(document.vertices.size() / 6);
            const double points[3][3] = {
                {p1.X(), p1.Y(), p1.Z()}, {p2.X(), p2.Y(), p2.Z()}, {p3.X(), p3.Y(), p3.Z()}
            };
            for (const auto& point : points) {
                document.vertices.push_back(static_cast<float>(point[0]));
                document.vertices.push_back(static_cast<float>(point[1]));
                document.vertices.push_back(static_cast<float>(point[2]));
                document.vertices.push_back(static_cast<float>(normal.X()));
                document.vertices.push_back(static_cast<float>(normal.Y()));
                document.vertices.push_back(static_cast<float>(normal.Z()));
            }
            document.indices.push_back(base);
            document.indices.push_back(base + 1);
            document.indices.push_back(base + 2);
            document.triangleFaceIndices.push_back(faceIndex);
            ++document.triangleCount;
        }
    }
    if (document.triangleCount == 0) {
        document.clear();
        return loadError("STEP modeli üçgenlenebilir yüzey üretmedi.");
    }
    document.shape = shape;
    document.loaded = true;
    std::ostringstream result;
    result << R"({"ok":true,"message":"STEP modeli yüklendi","faces":)" << document.faceCount
           << R"(,"triangles":)" << document.triangleCount << "}";
    return result.str();
}

bool intersectRayTriangle(
    const double origin[3], const double direction[3],
    const float* a, const float* b, const float* c,
    double& distance) {
    const double e1[3] = {b[0] - a[0], b[1] - a[1], b[2] - a[2]};
    const double e2[3] = {c[0] - a[0], c[1] - a[1], c[2] - a[2]};
    const double p[3] = {
        direction[1] * e2[2] - direction[2] * e2[1],
        direction[2] * e2[0] - direction[0] * e2[2],
        direction[0] * e2[1] - direction[1] * e2[0]
    };
    const double determinant = e1[0] * p[0] + e1[1] * p[1] + e1[2] * p[2];
    if (std::abs(determinant) < 1.0e-12) return false;
    const double inverse = 1.0 / determinant;
    const double tvec[3] = {origin[0] - a[0], origin[1] - a[1], origin[2] - a[2]};
    const double u = (tvec[0] * p[0] + tvec[1] * p[1] + tvec[2] * p[2]) * inverse;
    if (u < 0.0 || u > 1.0) return false;
    const double q[3] = {
        tvec[1] * e1[2] - tvec[2] * e1[1],
        tvec[2] * e1[0] - tvec[0] * e1[2],
        tvec[0] * e1[1] - tvec[1] * e1[0]
    };
    const double v = (direction[0] * q[0] + direction[1] * q[1] + direction[2] * q[2]) * inverse;
    if (v < 0.0 || u + v > 1.0) return false;
    distance = (e2[0] * q[0] + e2[1] * q[1] + e2[2] * q[2]) * inverse;
    return distance > 1.0e-9;
}

#if defined(SRCCAD_WITH_OCCT)
bool pickExactBrepPoint(
    CadDocument& document, const double origin[3], const double direction[3], gp_Pnt& exactPoint) {
    if (!document.loaded || document.indices.size() < 3) return false;
    double nearest = std::numeric_limits<double>::max();
    std::int32_t nearestFace = -1;

    for (std::size_t triangle = 0; triangle < document.triangleFaceIndices.size(); ++triangle) {
        const std::size_t offset = triangle * 3;
        const std::int32_t ia = document.indices[offset];
        const std::int32_t ib = document.indices[offset + 1];
        const std::int32_t ic = document.indices[offset + 2];
        const float* a = document.vertices.data() + static_cast<std::size_t>(ia) * 6;
        const float* b = document.vertices.data() + static_cast<std::size_t>(ib) * 6;
        const float* c = document.vertices.data() + static_cast<std::size_t>(ic) * 6;
        double distance = 0.0;
        if (intersectRayTriangle(origin, direction, a, b, c, distance) && distance < nearest) {
            nearest = distance;
            nearestFace = document.triangleFaceIndices[triangle];
        }
    }
    if (nearestFace < 0 || static_cast<std::size_t>(nearestFace) >= document.faces.size()) return false;

    const gp_Pnt approximate(
        origin[0] + direction[0] * nearest,
        origin[1] + direction[1] * nearest,
        origin[2] + direction[2] * nearest);
    const TopoDS_Vertex vertex = BRepBuilderAPI_MakeVertex(approximate).Vertex();
    BRepExtrema_DistShapeShape projection(vertex, document.faces[static_cast<std::size_t>(nearestFace)]);
    projection.Perform();
    if (!projection.IsDone() || projection.NbSolution() < 1) return false;
    exactPoint = projection.PointOnShape2(1);
    return true;
}
#endif
} // namespace

extern "C" JNIEXPORT jlong JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeCreate(JNIEnv*, jobject) {
    auto* document = new (std::nothrow) CadDocument();
    return static_cast<jlong>(reinterpret_cast<std::uintptr_t>(document));
}
extern "C" JNIEXPORT void JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeDestroy(JNIEnv*, jobject, jlong handle) {
    delete fromHandle(handle);
}
extern "C" JNIEXPORT jstring JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeEngineStatus(JNIEnv* env, jobject) {
#if defined(SRCCAD_WITH_OCCT)
    return toJavaString(env, "OCCT STEP çekirdeği bağlı; OpenGL ES 3.0 renderer hazır.");
#else
    return toJavaString(env, "OpenGL ES 3.0 renderer hazır. OCCT STEP çekirdeği henüz bağlanmadı.");
#endif
}
extern "C" JNIEXPORT jstring JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeLoadStep(JNIEnv* env, jobject, jlong handle, jstring javaPath) {
    CadDocument* document = fromHandle(handle);
    if (document == nullptr || javaPath == nullptr)
        return toJavaString(env, loadError("Geçersiz native model tutamacı veya dosya yolu."));
    const char* pathChars = env->GetStringUTFChars(javaPath, nullptr);
    if (pathChars == nullptr) return toJavaString(env, loadError("Dosya yolu belleğe alınamadı."));
    const std::string path(pathChars);
    env->ReleaseStringUTFChars(javaPath, pathChars);
#if defined(SRCCAD_WITH_OCCT)
    try {
        return toJavaString(env, loadStep(*document, path));
    } catch (const std::exception& error) {
        document->clear();
        return toJavaString(env, loadError(error.what()));
    } catch (...) {
        document->clear();
        return toJavaString(env, loadError("OCCT içe aktarma sırasında bilinmeyen hata oluştu."));
    }
#else
    (void)path;
    document->clear();
    return toJavaString(env, loadError(
        "STEP okuyucu henüz derlenmedi. scripts/build_occt_android.ps1 ile OCCT Android "
        "kütüphanelerini üretin ve uygulamayı SRCCAD_OCCT_ROOT ile derleyin."));
#endif
}
extern "C" JNIEXPORT jfloatArray JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeGetVertices(JNIEnv* env, jobject, jlong handle) {
    CadDocument* document = fromHandle(handle);
    if (document == nullptr || document->vertices.empty()) return env->NewFloatArray(0);
    jfloatArray result = env->NewFloatArray(static_cast<jsize>(document->vertices.size()));
    if (result != nullptr)
        env->SetFloatArrayRegion(result, 0, static_cast<jsize>(document->vertices.size()), document->vertices.data());
    return result;
}
extern "C" JNIEXPORT jintArray JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeGetIndices(JNIEnv* env, jobject, jlong handle) {
    CadDocument* document = fromHandle(handle);
    if (document == nullptr || document->indices.empty()) return env->NewIntArray(0);
    jintArray result = env->NewIntArray(static_cast<jsize>(document->indices.size()));
    if (result != nullptr)
        env->SetIntArrayRegion(result, 0, static_cast<jsize>(document->indices.size()),
                               reinterpret_cast<const jint*>(document->indices.data()));
    return result;
}

extern "C" JNIEXPORT jdoubleArray JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativePickRay(
    JNIEnv* env, jobject, jlong handle, jdoubleArray javaOrigin, jdoubleArray javaDirection) {
    CadDocument* document = fromHandle(handle);
    if (document == nullptr || javaOrigin == nullptr || javaDirection == nullptr) return env->NewDoubleArray(0);
    if (env->GetArrayLength(javaOrigin) < 3 || env->GetArrayLength(javaDirection) < 3) return env->NewDoubleArray(0);
    double origin[3] = {0, 0, 0};
    double direction[3] = {0, 0, 0};
    env->GetDoubleArrayRegion(javaOrigin, 0, 3, origin);
    env->GetDoubleArrayRegion(javaDirection, 0, 3, direction);
    const double length = std::sqrt(direction[0] * direction[0] + direction[1] * direction[1] + direction[2] * direction[2]);
    if (length <= 1.0e-12) return env->NewDoubleArray(0);
    direction[0] /= length; direction[1] /= length; direction[2] /= length;
#if defined(SRCCAD_WITH_OCCT)
    try {
        gp_Pnt point;
        if (!pickExactBrepPoint(*document, origin, direction, point)) return env->NewDoubleArray(0);
        const jdouble coordinates[3] = {point.X(), point.Y(), point.Z()};
        jdoubleArray result = env->NewDoubleArray(3);
        if (result != nullptr) env->SetDoubleArrayRegion(result, 0, 3, coordinates);
        return result;
    } catch (...) { return env->NewDoubleArray(0); }
#else
    return env->NewDoubleArray(0);
#endif
}

extern "C" JNIEXPORT jdoubleArray JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeMeasurePoints(
    JNIEnv* env, jobject, jlong handle, jdoubleArray javaA, jdoubleArray javaB) {
    if (fromHandle(handle) == nullptr || javaA == nullptr || javaB == nullptr ||
        env->GetArrayLength(javaA) < 3 || env->GetArrayLength(javaB) < 3) return env->NewDoubleArray(0);
    jdouble a[3] = {0, 0, 0};
    jdouble b[3] = {0, 0, 0};
    env->GetDoubleArrayRegion(javaA, 0, 3, a);
    env->GetDoubleArrayRegion(javaB, 0, 3, b);
    const double dx = b[0] - a[0];
    const double dy = b[1] - a[1];
    const double dz = b[2] - a[2];
    const jdouble values[4] = {std::sqrt(dx * dx + dy * dy + dz * dz), dx, dy, dz};
    jdoubleArray result = env->NewDoubleArray(4);
    if (result != nullptr) env->SetDoubleArrayRegion(result, 0, 4, values);
    return result;
}

extern "C" JNIEXPORT jdoubleArray JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeGetBounds(JNIEnv* env, jobject, jlong handle) {
    CadDocument* document = fromHandle(handle);
    jdoubleArray result = env->NewDoubleArray(6);
    if (result != nullptr && document != nullptr && document->loaded)
        env->SetDoubleArrayRegion(result, 0, 6, document->bounds);
    return result;
}
