# SrcCad V1.2 CAD Alpha — Mimari

## Katmanlar

- `SrcCad.App`: WPF CommandManager / FeatureManager / PropertyManager kabuğu.
- `SrcCad.Core`: Y-up dünya, PlaneFrame, sketch veri modeli.
- `SrcCad.Commands`: komut yaşam döngüsü sözleşmeleri.
- `SrcCad.Kernel`: kernel bağımsız CAD sözleşmeleri.
- `SrcCad.Kernel.Occt`: C++/CLI → OCCT B-Rep + AIS/V3d viewer.
- `SrcCad.Viewport`: viewport/seçim sözleşmeleri.
- `SrcCad.Solver`: sonraki milestone'da gerçek constraint solver için ayrılmış katman.


## Komut mimarisi

Sketch araçları UI event kodunun içinde geometri üretmez. `SrcCad.Commands` katmanındaki
`CadCommandManager` ve `ISketchCommand` durum makineleri kullanılır. Çizgi, köşe dikdörtgeni,
merkez dikdörtgeni ve daire kendi Pick/Preview/CancelStep durumlarını yönetir.

## 3B görüntüleme

WPF `Viewport3D` kullanılmaz. WPF içindeki `WindowsFormsHost`, native HWND sağlayan bir WinForms panel taşır.
C++/CLI bridge bu HWND'yi `WNT_Window` ile OCCT `V3d_View`'a bağlar.
Kamera ortografiktir.

## Koordinat sistemi

Varsayılan Y-up:

- Ön = XY, normal +Z
- Üst = XZ, normal +Y
- Sağ = YZ, normal +X

Her sketch: `Origin + U + V + Normal` frame'inde tutulur.
Screen → plane dönüşümü `V3d_View::ConvertWithProj` ray'i ile düzlem kesişiminden hesaplanır.
Bu nedenle sketch ayrı 2B canvas değildir; gerçek 3B sahnenin bir düzlemidir.

## Seçim

Katıdan sonra OCCT AIS seçimi kullanılır:

- `MoveTo`: hover pre-highlight
- `SelectDetected`: click selection
- `AIS_Shape::SelectionMode(TopAbs_FACE/EDGE/VERTEX)`: alt-topoloji seçimi

## Sketch model

- Noktalar ID ile paylaşılır.
- Çizgiler başlangıç/bitiş point ID'lerini referans eder.
- Origin = Point ID 0.
- Endpoint snap gerçek aynı point ID'ye bağlar.
- Merkez dikdörtgen: center + 4 corner + 4 edge + 2 construction diagonal + semantic definition.

## İlk B-Rep feature

Kapalı polyline:

`BRepBuilderAPI_MakePolygon → BRepBuilderAPI_MakeFace → BRepPrimAPI_MakePrism`

Daire:

`gp_Circ → MakeEdge/MakeWire/MakeFace → MakePrism`

Feature yönü global Z değildir; sketch `PlaneFrame.Normal` kullanılır.
