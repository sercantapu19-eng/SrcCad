#include <jni.h>
#include <algorithm>
#include <cstdint>
#include <cmath>
#include <exception>
#include <new>
#include <sstream>
#include <string>
#include <vector>
#include <limits>

#if defined(SRCCAD_WITH_OCCT)
#include <BRepBndLib.hxx>
#include <BRepAdaptor_Curve.hxx>
#include <BRepAdaptor_Surface.hxx>
#include <BRepBuilderAPI_MakeVertex.hxx>
#include <BRepExtrema_DistShapeShape.hxx>
#include <BRepGProp.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRep_Tool.hxx>
#include <Bnd_Box.hxx>
#include <GProp_GProps.hxx>
#include <GeomAbs_CurveType.hxx>
#include <GeomAbs_SurfaceType.hxx>
#include <IFSelect_ReturnStatus.hxx>
#include <Interface_Static.hxx>
#include <Poly_Triangle.hxx>
#include <Poly_Triangulation.hxx>
#include <STEPControl_Reader.hxx>
#include <TopAbs_Orientation.hxx>
#include <TopAbs_ShapeEnum.hxx>
#include <TopExp_Explorer.hxx>
#include <TopLoc_Location.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Vertex.hxx>
#include <gp_Ax1.hxx>
#include <gp_Cylinder.hxx>
#include <gp_Pln.hxx>
#include <gp_Pnt.hxx>
#include <gp_Vec.hxx>
#include <TopoDS_Edge.hxx>
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
    STEPControl_Reader reader; // initializes the STEP controller and its Interface_Static parameters
    // Normalize every STEP source unit to millimetres for predictable CAD measurement.
    if (!Interface_Static::SetCVal("xstep.cascade.unit", "MM")) {
        return loadError("OCCT STEP unit parameter xstep.cascade.unit is unavailable.");
    }
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
    const double deflection = std::max(0.001, diagonal * 0.0005);
    BRepMesh_IncrementalMesh mesher(shape, deflection, false, 0.22, true);
    if (!mesher.IsDone()) return loadError("Model üçgen ağına dönüştürülemedi.");

    for (TopExp_Explorer explorer(shape, TopAbs_FACE); explorer.More(); explorer.Next()) {
        const TopoDS_Face face = TopoDS::Face(explorer.Current());
        TopLoc_Location location;
        const auto triangulation = BRep_Tool::Triangulation(face, location);
        if (triangulation.IsNull()) continue;
        ++document.faceCount;
        const std::int32_t faceIndex = static_cast<std::int32_t>(document.faces.size());
        document.faces.push_back(face);
        for (int index = 1; index <= triangulation->NbTriangles(); ++index) {
            int n1 = 0, n2 = 0, n3 = 0;
            triangulation->Triangle(index).Get(n1, n2, n3);
            if (face.Orientation() == TopAbs_REVERSED) std::swap(n2, n3);

            const gp_Pnt p1 = triangulation->Node(n1).Transformed(location.Transformation());
            const gp_Pnt p2 = triangulation->Node(n2).Transformed(location.Transformation());
            const gp_Pnt p3 = triangulation->Node(n3).Transformed(location.Transformation());
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
#endif // SRCCAD_WITH_OCCT: STEP import implementation

void appendDemoTriangle(
    CadDocument& document, const double* p1, const double* p2, const double* p3, int faceIndex) {
    const double e1[3] = {p2[0] - p1[0], p2[1] - p1[1], p2[2] - p1[2]};
    const double e2[3] = {p3[0] - p1[0], p3[1] - p1[1], p3[2] - p1[2]};
    double normal[3] = {
        e1[1] * e2[2] - e1[2] * e2[1],
        e1[2] * e2[0] - e1[0] * e2[2],
        e1[0] * e2[1] - e1[1] * e2[0]
    };
    const double length = std::sqrt(
        normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]);
    if (length <= 1.0e-12) return;
    normal[0] /= length;
    normal[1] /= length;
    normal[2] /= length;

    const std::int32_t base = static_cast<std::int32_t>(document.vertices.size() / 6);
    const double points[3][3] = {
        {p1[0], p1[1], p1[2]},
        {p2[0], p2[1], p2[2]},
        {p3[0], p3[1], p3[2]}
    };
    for (const auto& point : points) {
        document.vertices.push_back(static_cast<float>(point[0]));
        document.vertices.push_back(static_cast<float>(point[1]));
        document.vertices.push_back(static_cast<float>(point[2]));
        document.vertices.push_back(static_cast<float>(normal[0]));
        document.vertices.push_back(static_cast<float>(normal[1]));
        document.vertices.push_back(static_cast<float>(normal[2]));
    }
    document.indices.push_back(base);
    document.indices.push_back(base + 1);
    document.indices.push_back(base + 2);
    document.triangleFaceIndices.push_back(faceIndex);
    ++document.triangleCount;
}

std::string loadDemoBox(CadDocument& document) {
    document.clear();

    // Six outward-facing quads for a 100 x 50 x 20 mm demonstration solid.
    const double faces[6][4][3] = {
        {{0, 0, 0},   {0, 50, 0},   {100, 50, 0}, {100, 0, 0}},   // -Z
        {{0, 0, 20},  {100, 0, 20}, {100, 50, 20}, {0, 50, 20}}, // +Z
        {{0, 0, 0},   {0, 0, 20},  {0, 50, 20},  {0, 50, 0}},   // -X
        {{100, 0, 0}, {100, 50, 0},{100, 50, 20},{100, 0, 20}}, // +X
        {{0, 0, 0},   {100, 0, 0}, {100, 0, 20}, {0, 0, 20}},   // -Y
        {{0, 50, 0},  {0, 50, 20}, {100, 50, 20},{100, 50, 0}}  // +Y
    };

    for (int face = 0; face < 6; ++face) {
        appendDemoTriangle(document, faces[face][0], faces[face][1], faces[face][2], face);
        appendDemoTriangle(document, faces[face][0], faces[face][2], faces[face][3], face);
    }

    document.bounds[0] = 0.0;
    document.bounds[1] = 0.0;
    document.bounds[2] = 0.0;
    document.bounds[3] = 100.0;
    document.bounds[4] = 50.0;
    document.bounds[5] = 20.0;
    document.faceCount = 6;
    document.loaded = document.triangleCount == 12;

    if (!document.loaded) {
        document.clear();
        return loadError("Demo geometri ağı oluşturulamadı.");
    }

    return R"({"ok":true,"message":"100 x 50 x 20 mm demo kutu yüklendi","faces":6,"triangles":12})";
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

// Result layout:
// [kind, X, Y, Z, primary, secondary, directionX, directionY, directionZ, hasDirection]
// kind: 1 line edge, 2 circular edge, 3 other edge, 4 planar face, 5 cylindrical face, 6 other face.
std::vector<double> analyzeGeometryPick(
    CadDocument& document,
    const double origin[3],
    const double direction[3],
    const double edgeTolerance) {
    std::vector<double> result;
    if (!document.loaded || document.indices.size() < 3) return result;

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
        double rayDistance = 0.0;
        if (intersectRayTriangle(origin, direction, a, b, c, rayDistance) && rayDistance < nearest) {
            nearest = rayDistance;
            nearestFace = document.triangleFaceIndices[triangle];
        }
    }
    if (nearestFace < 0 || static_cast<std::size_t>(nearestFace) >= document.faces.size()) return result;

    const gp_Pnt approximate(
        origin[0] + direction[0] * nearest,
        origin[1] + direction[1] * nearest,
        origin[2] + direction[2] * nearest);
    const TopoDS_Vertex pickedVertex = BRepBuilderAPI_MakeVertex(approximate).Vertex();

    const TopoDS_Face& face = document.faces[static_cast<std::size_t>(nearestFace)];
    BRepExtrema_DistShapeShape faceProjection(pickedVertex, face);
    faceProjection.Perform();
    if (!faceProjection.IsDone() || faceProjection.NbSolution() < 1) return result;
    const gp_Pnt pickedPoint = faceProjection.PointOnShape2(1);

    int kind = 6;
    double primary = 0.0;
    double secondary = 0.0;
    gp_Dir directionVector(0.0, 0.0, 1.0);
    bool hasDirection = false;

    // Prefer a topological edge only when the finger landed close enough to it.
    // This avoids turning a tap in the middle of a face into a random edge selection.
    TopoDS_Edge closestEdge;
    double closestEdgeDistance = std::numeric_limits<double>::max();
    const TopoDS_Vertex exactPickVertex = BRepBuilderAPI_MakeVertex(pickedPoint).Vertex();
    for (TopExp_Explorer edgeExplorer(face, TopAbs_EDGE); edgeExplorer.More(); edgeExplorer.Next()) {
        const TopoDS_Edge edge = TopoDS::Edge(edgeExplorer.Current());
        BRepExtrema_DistShapeShape edgeDistance(exactPickVertex, edge);
        edgeDistance.Perform();
        if (edgeDistance.IsDone() && edgeDistance.NbSolution() > 0 &&
            edgeDistance.Value() < closestEdgeDistance) {
            closestEdgeDistance = edgeDistance.Value();
            closestEdge = edge;
        }
    }

    if (!closestEdge.IsNull() && closestEdgeDistance <= std::max(edgeTolerance, 0.001)) {
        BRepAdaptor_Curve curve(closestEdge);
        GProp_GProps linearProperties;
        BRepGProp::LinearProperties(closestEdge, linearProperties);
        primary = linearProperties.Mass();

        if (curve.GetType() == GeomAbs_Line) {
            kind = 1;
            directionVector = curve.Line().Direction();
            hasDirection = true;
        } else if (curve.GetType() == GeomAbs_Circle) {
            kind = 2;
            const double radius = curve.Circle().Radius();
            primary = radius * 2.0;
            secondary = radius;
        } else {
            kind = 3;
        }
    } else {
        BRepAdaptor_Surface surface(face, true);
        GProp_GProps surfaceProperties;
        BRepGProp::SurfaceProperties(face, surfaceProperties);
        primary = surfaceProperties.Mass();

        if (surface.GetType() == GeomAbs_Plane) {
            kind = 4;
            directionVector = surface.Plane().Axis().Direction();
            hasDirection = true;
        } else if (surface.GetType() == GeomAbs_Cylinder) {
            kind = 5;
            const gp_Cylinder cylinder = surface.Cylinder();
            const gp_Ax1 axis = cylinder.Axis();
            primary = cylinder.Radius() * 2.0;
            double minAxial = std::numeric_limits<double>::max();
            double maxAxial = -std::numeric_limits<double>::max();
            for (TopExp_Explorer vertexExplorer(face, TopAbs_VERTEX);
                 vertexExplorer.More(); vertexExplorer.Next()) {
                const TopoDS_Vertex vertex = TopoDS::Vertex(vertexExplorer.Current());
                const gp_Pnt point = BRep_Tool::Pnt(vertex);
                const double axial = gp_Vec(axis.Location(), point).Dot(gp_Vec(axis.Direction()));
                minAxial = std::min(minAxial, axial);
                maxAxial = std::max(maxAxial, axial);
            }
            secondary = (maxAxial >= minAxial) ? (maxAxial - minAxial) : 0.0;
            directionVector = axis.Direction();
            hasDirection = true;
        } else {
            kind = 6;
        }
    }

    result = {
        static_cast<double>(kind),
        pickedPoint.X(), pickedPoint.Y(), pickedPoint.Z(),
        primary, secondary,
        hasDirection ? directionVector.X() : 0.0,
        hasDirection ? directionVector.Y() : 0.0,
        hasDirection ? directionVector.Z() : 0.0,
        hasDirection ? 1.0 : 0.0,
        static_cast<double>(nearestFace)
    };
    return result;
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
extern "C" JNIEXPORT jstring JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeLoadDemoBox(JNIEnv* env, jobject, jlong handle) {
    CadDocument* document = fromHandle(handle);
    if (document == nullptr) {
        return toJavaString(env, loadError("Geçersiz native model tutamacı."));
    }
    try {
        return toJavaString(env, loadDemoBox(*document));
    } catch (...) {
        document->clear();
        return toJavaString(env, loadError("Demo geometri hazırlanırken hata oluştu."));
    }
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

extern "C" JNIEXPORT jintArray JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeGetTriangleFaceIndices(JNIEnv* env, jobject, jlong handle) {
    CadDocument* document = fromHandle(handle);
    if (document == nullptr || document->triangleFaceIndices.empty()) return env->NewIntArray(0);
    jintArray result = env->NewIntArray(static_cast<jsize>(document->triangleFaceIndices.size()));
    if (result != nullptr) env->SetIntArrayRegion(result, 0,
        static_cast<jsize>(document->triangleFaceIndices.size()),
        reinterpret_cast<const jint*>(document->triangleFaceIndices.data()));
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
Java_com_srccad_mobile_NativeCadEngine_nativePickGeometry(
    JNIEnv* env, jobject, jlong handle,
    jdoubleArray javaOrigin, jdoubleArray javaDirection, jdouble tolerance) {
    CadDocument* document = fromHandle(handle);
    if (document == nullptr || javaOrigin == nullptr || javaDirection == nullptr ||
        env->GetArrayLength(javaOrigin) < 3 || env->GetArrayLength(javaDirection) < 3) {
        return env->NewDoubleArray(0);
    }

    double origin[3] = {0.0, 0.0, 0.0};
    double direction[3] = {0.0, 0.0, 0.0};
    env->GetDoubleArrayRegion(javaOrigin, 0, 3, origin);
    env->GetDoubleArrayRegion(javaDirection, 0, 3, direction);
    const double length = std::sqrt(
        direction[0] * direction[0] + direction[1] * direction[1] + direction[2] * direction[2]);
    if (length <= 1.0e-12) return env->NewDoubleArray(0);
    direction[0] /= length;
    direction[1] /= length;
    direction[2] /= length;

#if defined(SRCCAD_WITH_OCCT)
    try {
        const std::vector<double> values =
            analyzeGeometryPick(*document, origin, direction, std::max(0.0, static_cast<double>(tolerance)));
        if (values.empty()) return env->NewDoubleArray(0);
        jdoubleArray result = env->NewDoubleArray(static_cast<jsize>(values.size()));
        if (result != nullptr) {
            env->SetDoubleArrayRegion(result, 0, static_cast<jsize>(values.size()), values.data());
        }
        return result;
    } catch (...) {
        return env->NewDoubleArray(0);
    }
#else
    (void)tolerance;
    return env->NewDoubleArray(0);
#endif
}

extern "C" JNIEXPORT jdoubleArray JNICALL
Java_com_srccad_mobile_NativeCadEngine_nativeMeasurePoints(
    JNIEnv* env, jobject, jlong handle, jdoubleArray javaA, jdoubleArray javaB) {
    CadDocument* document = fromHandle(handle);
    if (document == nullptr || !document->loaded || javaA == nullptr || javaB == nullptr ||
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
