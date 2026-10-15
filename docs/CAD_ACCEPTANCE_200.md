# CAD-001…CAD-200 — Profesyonel parametrik CAD kabul matrisi

2026-10-11. Issue #3 ve kullanıcı ana geliştirme metni kapsamı. **Tamamlanmadı.**

`PASS-MANAGED`: belirtilen model alt gereksinimi gerçek managed kodla çalıştırıldı; Windows/native veya bütün özellik DoD onayı değildir. `PARTIAL`: uygulama/alt test var, kapsam eksik. `PENDING-WINDOWS`: gerçek native test eklendi, bu kaynak üzerinde henüz çalıştırılmadı. `TODO`: uygulanacak/testlenecek. `—` mevcut test veya uygulama kanıtı yoktur.

Test sınıfları `src/SrcCad.Tests/`; model dosyaları `src/SrcCad.Core/Sketch/`, codec `src/SrcCad.Core/Persistence/`, history `src/SrcCad.Core/History/`, UI `src/SrcCad.App/`, native `src/SrcCad.Kernel.Occt/`. Test adı sütunundaki sınıf `.Run()` paketidir; her satır ayrı test sayısı olarak sayılmaz.

| ID | Category | Requirement | Automated/manual | Test name | Current status | Implementation reference | Failure notes |
|---|---|---|---|---|---|---|---|
| CAD-001 | SKETCH | Line endpoints model coordinates | Automated | AcceptanceTests | PASS-MANAGED | SketchModel.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-002 | SKETCH | Corner and rotated three-point rectangles | Automated | AcceptanceTests / ThreePointToolTests | PASS-MANAGED | SketchModel.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-003 | SKETCH | Center rectangle symmetry | Automated | AcceptanceTests | PASS-MANAGED | SketchModel.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-004 | SKETCH | Circle center/radius and three-point creation | Automated | AcceptanceTests / ThreePointToolTests | PASS-MANAGED | SketchModel.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-005 | SKETCH | Construction line semantic flag | Automated | SketchFoundationTests | PASS-MANAGED | SketchCommands.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-006 | SKETCH | Construction excluded from region | Automated | SketchFoundationTests | PASS-MANAGED | SketchModel.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-007 | SKETCH | Derived midpoint reference | Automated | SketchFoundationTests | PASS-MANAGED | GeometryReference.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-008 | SKETCH | Independent sketch point | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-009 | SKETCH | Centerpoint arc | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-010 | SKETCH | Three-point arc | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-011 | SKETCH | Closed straight slot | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-012 | SKETCH | Regular polygon | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-013 | SKETCH | Ellipse geometry | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-014 | SKETCH | Trim intersection segment | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-015 | CONSTRAINT | Point-like Horizontal | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-016 | CONSTRAINT | Point-like Vertical | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-017 | CONSTRAINT | Point-like Coincident | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-018 | CONSTRAINT | Origin to endpoint Coincident | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-019 | CONSTRAINT | Line Horizontal | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-020 | CONSTRAINT | Line Vertical | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-021 | CONSTRAINT | Line Parallel | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-022 | CONSTRAINT | Line Perpendicular | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-023 | CONSTRAINT | Line Equal | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-024 | CONSTRAINT | Circle Equal | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-025 | CONSTRAINT | Circle Concentric | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-026 | CONSTRAINT | Line Collinear | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-027 | CONSTRAINT | Point-line Coincident | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-028 | CONSTRAINT | Point-line Midpoint | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-029 | CONSTRAINT | Line-circle Tangent | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-030 | CONSTRAINT | External circle Tangent | Automated + manual | SketchFoundationTests / GeneralConstraintTests / AcceptanceTests | PARTIAL | SketchModel.cs; SketchConstraintSystem.cs | Model alt kapsamı testli; tüm UI kombinasyonları henüz doğrulanmadı. |
| CAD-031 | CONSTRAINT | Internal circle Tangent | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-032 | CONSTRAINT | Symmetric about axis | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-033 | DIMENSION | Line length driving | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-034 | DIMENSION | Circle diameter driving | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-035 | DIMENSION | Circle radius driving | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-036 | DIMENSION | Point-point aligned distance | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-037 | DIMENSION | Point-point horizontal distance | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-038 | DIMENSION | Point-point vertical distance | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-039 | DIMENSION | Origin to corner X | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-040 | DIMENSION | Origin to corner Y | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-041 | DIMENSION | Origin to center X | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-042 | DIMENSION | Origin to center Y | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-043 | DIMENSION | Center-center X | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-044 | DIMENSION | Center-center Y | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-045 | DIMENSION | Center-vertical edge distance | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-046 | DIMENSION | Center-horizontal edge distance | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-047 | DIMENSION | Point-oblique line distance | Automated | GeneralConstraintTests | PASS-MANAGED | GeometryReference.cs | GeneralConstraintTests: üç düzlemde gerçek dik mesafe doğrulandı; Windows UI PENDING. |
| CAD-048 | DIMENSION | Parallel line distance | Automated + manual | ParallelDimensionTests; NativeParallelDimensionTests (PENDING) | PARTIAL | GeometryReference.cs; SketchConstraintSystem.cs | 205 managed assertions; yatay/dikey/eğik, ters sıra, construction, zero, save/open, rollback ve alan. Windows UI/native bekliyor. |
| CAD-049 | DIMENSION | Two-line angle | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-050 | DIMENSION | Reference dimension remains read-only | Automated + manual | SketchFoundationTests / AcceptanceTests | PARTIAL | GeometryReference.cs; SmartDimensionSession.cs | Managed alt kapsam mevcut; Windows kullanıcı akışı tekrar gerekli. |
| CAD-051 | SOLVER | Rectangle and two circles zero DOF without Fix | Automated | SketchFoundationTests | PASS-MANAGED | SketchConstraintSystem.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-052 | SOLVER | Removing one driving dimension releases DOF | Automated | SketchFoundationTests | PASS-MANAGED | SketchConstraintSystem.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-053 | SOLVER | Rank handles redundant equations | Automated | GeneralConstraintTests | PASS-MANAGED | SketchConstraintSystem.cs | GeneralConstraintTests: redundant equation rank; constraint provenance ayrı kapsamdır. |
| CAD-054 | SOLVER | Conflict rejection restores geometry | Automated | SketchFoundationTests | PASS-MANAGED | SketchConstraintSystem.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-055 | SOLVER | Fixed Origin eliminated from unknowns | Automated | SketchFoundationTests | PASS-MANAGED | SketchConstraintSystem.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-056 | SOLVER | Constraint survives dimension edit | Automated | SketchFoundationTests; GeneralConstraintTests | PASS-MANAGED | SketchConstraintSystem.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-057 | SOLVER | Mixed 20-constraint chain | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-058 | SOLVER | Singular configuration diagnostics | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-059 | SOLVER | Constraint-level conflict provenance | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-060 | SOLVER | Disconnected component solving | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-061 | SELECTION | Circle body versus center | Automated | SketchFoundationTests | PASS-MANAGED | GeometryReference.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-062 | SELECTION | Origin semantic identity | Automated | SketchFoundationTests | PASS-MANAGED | GeometryReference.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-063 | SELECTION | Endpoint semantic identity | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-064 | SELECTION | Midpoint hit and glyph | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-065 | SELECTION | Construction body selection | Automated | SketchFoundationTests | PASS-MANAGED | GeometryReference.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-066 | SELECTION | Overlapping geometry cycling | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-067 | SELECTION | Screen-space tolerance across zoom | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-068 | SELECTION | Ctrl toggles references | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-069 | SELECTION | Empty click clears selection | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-070 | SELECTION | Dimension label hit priority | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-071 | INFERENCE | Endpoint snap | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-072 | INFERENCE | Origin snap | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-073 | INFERENCE | Circle center snap | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-074 | INFERENCE | Midpoint snap | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-075 | INFERENCE | Intersection snap | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-076 | INFERENCE | Horizontal alignment guide | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-077 | INFERENCE | Vertical alignment guide | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-078 | INFERENCE | Tangent candidate guide | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-079 | HISTORY | Sketch entity transaction | Automated + manual | FeatureHistoryTests / SourceSketchEditTests | PARTIAL | MainWindow.xaml.cs; TransactionalHistory.cs | Tek sketch/extrusion kapsamı; genel document transaction değil. |
| CAD-080 | HISTORY | Constraint transaction | Automated + manual | FeatureHistoryTests / SourceSketchEditTests | PARTIAL | MainWindow.xaml.cs; TransactionalHistory.cs | Tek sketch/extrusion kapsamı; genel document transaction değil. |
| CAD-081 | HISTORY | Dimension transaction | Automated + manual | FeatureHistoryTests / SourceSketchEditTests | PARTIAL | MainWindow.xaml.cs; TransactionalHistory.cs | Tek sketch/extrusion kapsamı; genel document transaction değil. |
| CAD-082 | HISTORY | Feature create transaction | Automated + manual | FeatureHistoryTests / SourceSketchEditTests | PARTIAL | MainWindow.xaml.cs; TransactionalHistory.cs | Tek sketch/extrusion kapsamı; genel document transaction değil. |
| CAD-083 | HISTORY | Feature edit transaction | Automated + manual | FeatureHistoryTests / SourceSketchEditTests | PARTIAL | MainWindow.xaml.cs; TransactionalHistory.cs | Tek sketch/extrusion kapsamı; genel document transaction değil. |
| CAD-084 | HISTORY | Feature delete transaction | Automated + manual | FeatureHistoryTests / SourceSketchEditTests | PARTIAL | MainWindow.xaml.cs; TransactionalHistory.cs | Tek sketch/extrusion kapsamı; genel document transaction değil. |
| CAD-085 | HISTORY | Preview excluded from history | Automated + manual | FeatureHistoryTests / SourceSketchEditTests | PARTIAL | MainWindow.xaml.cs; TransactionalHistory.cs | Tek sketch/extrusion kapsamı; genel document transaction değil. |
| CAD-086 | HISTORY | Failed apply retains history | Automated | FeatureHistoryTests; SourceSketchEditTests | PASS-MANAGED | TransactionalHistory.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-087 | DEPENDENCY | Explicit sketch revision | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-088 | DEPENDENCY | Feature input dependency identity | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-089 | DEPENDENCY | Transitive dirty propagation | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-090 | DEPENDENCY | Deterministic topological rebuild | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-091 | DEPENDENCY | Dependency cycle rejection | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-092 | DEPENDENCY | Missing parent rejection | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-093 | DEPENDENCY | Sketch1 to Extrude1 rebuild | Automated + manual | NativeAcceptanceTests / SourceSketchEditTests | PARTIAL | MainWindow.xaml.cs | Tek ekstrüzyon rebuild; genel graph henüz yok. |
| CAD-094 | DEPENDENCY | Sketch2 on face to Cut rebuild | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-095 | DEPENDENCY | Cut to Fillet rebuild | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-096 | DEPENDENCY | Incremental rebuild affected nodes | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-097 | DEPENDENCY | Failed feature blocks downstream | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-098 | DEPENDENCY | Recovery after upstream repair | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-099 | DEPENDENCY | Suppression propagation | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-100 | DEPENDENCY | Rollback point traversal | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-101 | TOPOLOGY | Extrude face provenance | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-102 | TOPOLOGY | Extrude edge provenance | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-103 | TOPOLOGY | Vertex stable reference | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-104 | TOPOLOGY | Top face survives depth edit | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-105 | TOPOLOGY | Side face survives profile edit | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-106 | TOPOLOGY | Ambiguous match diagnosed | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-107 | TOPOLOGY | Deleted reference diagnosed | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-108 | TOPOLOGY | Split face mapping | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-109 | TOPOLOGY | Merged face mapping | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-110 | TOPOLOGY | Persistent reference round-trip | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-111 | BREP | Front plane prism validity and volume | Automated native | NativeAcceptanceTests | PENDING-WINDOWS | OcctViewportBridge.cpp | Yeni branch koşusu bekliyor; önceki24 PASS kullanıcı bildirimi tarihsel baseline. |
| CAD-112 | BREP | Top plane prism validity and volume | Automated native | NativeAcceptanceTests | PENDING-WINDOWS | OcctViewportBridge.cpp | Yeni branch koşusu bekliyor; önceki24 PASS kullanıcı bildirimi tarihsel baseline. |
| CAD-113 | BREP | Right plane prism validity and volume | Automated native | NativeAcceptanceTests | PENDING-WINDOWS | OcctViewportBridge.cpp | Yeni branch koşusu bekliyor; önceki24 PASS kullanıcı bildirimi tarihsel baseline. |
| CAD-114 | BREP | Three circular holes volume | Automated native | NativeAcceptanceTests | PENDING-WINDOWS | OcctViewportBridge.cpp | Yeni branch koşusu bekliyor; önceki24 PASS kullanıcı bildirimi tarihsel baseline. |
| CAD-115 | BREP | Polygon hole volume | Automated native | NativeAcceptanceTests | PENDING-WINDOWS | OcctViewportBridge.cpp | Yeni branch koşusu bekliyor; önceki24 PASS kullanıcı bildirimi tarihsel baseline. |
| CAD-116 | BREP | Negative depth volume | Automated native | NativeAcceptanceTests | PENDING-WINDOWS | OcctViewportBridge.cpp | Yeni branch koşusu bekliyor; önceki24 PASS kullanıcı bildirimi tarihsel baseline. |
| CAD-117 | BREP | Source width edit volume | Automated native | NativeAcceptanceTests | PENDING-WINDOWS | OcctViewportBridge.cpp | Yeni branch koşusu bekliyor; önceki24 PASS kullanıcı bildirimi tarihsel baseline. |
| CAD-118 | BREP | Source snapshot restore volume | Automated native | NativeAcceptanceTests | PENDING-WINDOWS | OcctViewportBridge.cpp | Yeni branch koşusu bekliyor; önceki24 PASS kullanıcı bildirimi tarihsel baseline. |
| CAD-119 | BREP | Post-open driving edit volume | Automated native | NativeReferenceRoundtripTests | PENDING-WINDOWS | OcctViewportBridge.cpp | Yeni branch koşusu bekliyor; önceki24 PASS kullanıcı bildirimi tarihsel baseline. |
| CAD-120 | BREP | Nested island topology and volume | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-121 | BREP | Concentric ring topology and volume | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-122 | BREP | Disjoint regions solid count | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-123 | FEATURE | Blind extrusion creation | Automated + manual | NativeAcceptanceTests / FeatureHistoryTests | PARTIAL | OcctViewportBridge.cpp; MainWindow.xaml.cs | Önceki Windows baseline kullanıcı PASS; yeni kaynak için yeniden koşu gerekli. |
| CAD-124 | FEATURE | Extrusion depth edit | Automated + manual | NativeAcceptanceTests / FeatureHistoryTests | PARTIAL | OcctViewportBridge.cpp; MainWindow.xaml.cs | Önceki Windows baseline kullanıcı PASS; yeni kaynak için yeniden koşu gerekli. |
| CAD-125 | FEATURE | Extrusion direction reversal | Automated + manual | NativeAcceptanceTests / FeatureHistoryTests | PARTIAL | OcctViewportBridge.cpp; MainWindow.xaml.cs | Önceki Windows baseline kullanıcı PASS; yeni kaynak için yeniden koşu gerekli. |
| CAD-126 | FEATURE | Mid-plane extrusion | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-127 | FEATURE | Through-all cut | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-128 | FEATURE | Blind boolean cut | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-129 | FEATURE | Up-to-face termination | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-130 | FEATURE | Two-direction extrusion | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-131 | FEATURE | Revolve closed region | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-132 | FEATURE | Edge fillet | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-133 | FEATURE | Edge chamfer | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-134 | FEATURE | Hole feature | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-135 | FEATURE | Feature linear pattern | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-136 | FEATURE | Feature mirror | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-137 | SAVE/OPEN | Sketch entity round-trip | Automated | PartFileTests | PASS-MANAGED | PartFile.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-138 | SAVE/OPEN | Geometry reference round-trip | Automated | PartFileTests | PASS-MANAGED | PartFile.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-139 | SAVE/OPEN | Constraint round-trip | Automated | PartFileTests | PASS-MANAGED | PartFile.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-140 | SAVE/OPEN | Driving dimension round-trip | Automated + manual | PartFileTests | PARTIAL | PartFile.cs | Reference eksen ölçüleri testli; tüm dimension türleri round-trip testi değil. |
| CAD-141 | SAVE/OPEN | Construction round-trip | Automated | PartFileTests | PASS-MANAGED | PartFile.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-142 | SAVE/OPEN | Plane and mm unit round-trip | Automated | PartFileTests | PASS-MANAGED | PartFile.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-143 | SAVE/OPEN | Extrusion parameter round-trip | Automated | PartFileTests | PASS-MANAGED | PartFile.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-144 | SAVE/OPEN | Post-open dimension still drives | Automated | PartFileTests | PASS-MANAGED | PartFile.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-145 | SAVE/OPEN | Unknown schema rejected | Automated | PartFileTests | PASS-MANAGED | PartFile.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-146 | SAVE/OPEN | Invalid reference rejected | Automated + manual | PartFileTests | PARTIAL | PartFile.cs | Validasyon var; tüm bozuk referans kombinasyonları testli değil. |
| CAD-147 | SAVE/OPEN | Atomic file replacement | Automated | PartFileTests | PASS-MANAGED | PartFile.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-148 | SAVE/OPEN | Multi-feature history and dependencies round-trip | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-149 | UNDO/REDO | Sketch create undo redo | Automated + manual | FeatureHistoryTests / SourceSketchEditTests / AcceptanceTests | PARTIAL | MainWindow.xaml.cs | Managed snapshot/transaction kanıtı var; Windows akışı PENDING. |
| CAD-150 | UNDO/REDO | Constraint undo redo | Automated + manual | FeatureHistoryTests / SourceSketchEditTests / AcceptanceTests | PARTIAL | MainWindow.xaml.cs | Managed snapshot/transaction kanıtı var; Windows akışı PENDING. |
| CAD-151 | UNDO/REDO | Dimension value undo redo | Automated + manual | FeatureHistoryTests / SourceSketchEditTests / AcceptanceTests | PARTIAL | MainWindow.xaml.cs | Managed snapshot/transaction kanıtı var; Windows akışı PENDING. |
| CAD-152 | UNDO/REDO | Dimension label undo redo | Automated + manual | FeatureHistoryTests / SourceSketchEditTests / AcceptanceTests | PARTIAL | MainWindow.xaml.cs | Managed snapshot/transaction kanıtı var; Windows akışı PENDING. |
| CAD-153 | UNDO/REDO | Feature create undo redo | Automated + manual | FeatureHistoryTests / SourceSketchEditTests / AcceptanceTests | PARTIAL | MainWindow.xaml.cs | Managed snapshot/transaction kanıtı var; Windows akışı PENDING. |
| CAD-154 | UNDO/REDO | Feature depth undo redo | Automated + manual | FeatureHistoryTests / SourceSketchEditTests / AcceptanceTests | PARTIAL | MainWindow.xaml.cs | Managed snapshot/transaction kanıtı var; Windows akışı PENDING. |
| CAD-155 | UNDO/REDO | Source sketch edit undo redo | Automated + manual | FeatureHistoryTests / SourceSketchEditTests / AcceptanceTests | PARTIAL | MainWindow.xaml.cs | Managed snapshot/transaction kanıtı var; Windows akışı PENDING. |
| CAD-156 | UNDO/REDO | Failed commit keeps redo | Automated | SourceSketchEditTests | PASS-MANAGED | TransactionalHistory.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-157 | UNDO/REDO | New document clears history | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-158 | UNDO/REDO | Selection safe after undo | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-159 | VIEWPORT | Circle fully inside viewport | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-160 | VIEWPORT | Circle center outside visible arc remains | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-161 | VIEWPORT | Circle partially clipped | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-162 | VIEWPORT | Circle fully outside culled | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-163 | VIEWPORT | Extreme zoom circle visibility | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-164 | VIEWPORT | Line partial clipping | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-165 | VIEWPORT | Arc partial clipping | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-166 | VIEWPORT | Dimension extension line clipping | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-167 | VIEWPORT | Center highlight visibility | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-168 | VIEWPORT | Middle drag rotate | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-169 | VIEWPORT | Ctrl middle drag pan | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-170 | VIEWPORT | Wheel zoom outside command | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-171 | ROBUSTNESS | Reject zero-length line | Automated | AcceptanceTests | PASS-MANAGED | SketchModel.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-172 | ROBUSTNESS | Reject zero radius | Automated | AcceptanceTests | PASS-MANAGED | SketchModel.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-173 | ROBUSTNESS | Reject NaN coordinates | Automated | AcceptanceTests | PASS-MANAGED | SketchModel.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-174 | ROBUSTNESS | Reject infinite dimension | Automated | AcceptanceTests | PASS-MANAGED | SketchModel.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-175 | ROBUSTNESS | Reject conflicting dimensions | Automated | SketchFoundationTests | PASS-MANAGED | SketchConstraintSystem.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-176 | ROBUSTNESS | Duplicate constraint does not accumulate | Automated | GeneralConstraintTests | PASS-MANAGED | GeometryReference.cs | Managed model kanıtı; Windows UI için genellenmez. |
| CAD-177 | ROBUSTNESS | Near-parallel constraint stability | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-178 | ROBUSTNESS | Near-perpendicular constraint stability | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-179 | ROBUSTNESS | Tiny geometry tolerance | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-180 | ROBUSTNESS | Large-coordinate solver accuracy | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-181 | ROBUSTNESS | Repeated rebuild determinism | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-182 | ROBUSTNESS | Negative solution branch preservation | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-183 | PERFORMANCE | 100-entity hit-test profile | Automated | PerformanceSmokeTests | PASS-MANAGED | GeometryReference.cs | Host ölçümü; Windows UI latency veya solver scaling kabulü değildir. |
| CAD-184 | PERFORMANCE | 500-entity hit-test profile | Automated | PerformanceSmokeTests | PASS-MANAGED | GeometryReference.cs | Host ölçümü; Windows UI latency veya solver scaling kabulü değildir. |
| CAD-185 | PERFORMANCE | 100-entity solver profile | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-186 | PERFORMANCE | 500-entity solver profile | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-187 | PERFORMANCE | Constraint-heavy rank profile | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-188 | PERFORMANCE | 50-feature rebuild profile | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-189 | PERFORMANCE | Repeated open/edit/rebuild profile | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-190 | PERFORMANCE | Contour extraction scaling | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-191 | UI/COMMANDMANAGER | Original vector CAD icons | Automated + manual | Windows manual / InteractionRegressionTests | PARTIAL | MainWindow.xaml; App.xaml; MainWindow.xaml.cs | Uygulandı; Windows görsel/olay kabulü PENDING. |
| CAD-192 | UI/COMMANDMANAGER | Line and centerline command family | Automated + manual | Windows manual / InteractionRegressionTests | PARTIAL | MainWindow.xaml; App.xaml; MainWindow.xaml.cs | Uygulandı; Windows görsel/olay kabulü PENDING. |
| CAD-193 | UI/COMMANDMANAGER | Rectangle command family | Automated + manual | Windows manual / InteractionRegressionTests | PARTIAL | MainWindow.xaml; App.xaml; MainWindow.xaml.cs | Uygulandı; Windows görsel/olay kabulü PENDING. |
| CAD-194 | UI/COMMANDMANAGER | Circle and arc command families | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-195 | UI/COMMANDMANAGER | Slot command family | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-196 | UI/COMMANDMANAGER | Sketch edit tools command families | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-197 | UI/COMMANDMANAGER | PropertyManager accept cancel | Automated + manual | Windows manual / InteractionRegressionTests | PARTIAL | MainWindow.xaml; App.xaml; MainWindow.xaml.cs | Uygulandı; Windows görsel/olay kabulü PENDING. |
| CAD-198 | UI/COMMANDMANAGER | Extrude wheel parameter preview | Automated + manual | Windows manual / InteractionRegressionTests | PARTIAL | MainWindow.xaml; App.xaml; MainWindow.xaml.cs | Uygulandı; Windows görsel/olay kabulü PENDING. |
| CAD-199 | UI/COMMANDMANAGER | Feature tree context edit | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |
| CAD-200 | UI/COMMANDMANAGER | DPI and keyboard navigation | Automated + manual | — | TODO | — | Kabul kanıtı yok; PASS sayılmaz. |

## Durum sayımı

36 PASS-MANAGED, 98 TODO, 57 PARTIAL, 9 PENDING-WINDOWS. Toplam200. Bu sayılar test/assertion toplamı değildir.

Mevcut managed test toplamı572 PASS / 0 FAIL. Native suite54 kontrol: mevcut24 baseline kullanıcı Windows PASS bildirimli; yeni30 ve değişen kaynağın tamamı bu tur PENDING. Windows manuel sorunları kullanıcı yeniden doğrulamadan kapatılmaz.
