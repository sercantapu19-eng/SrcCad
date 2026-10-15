# SrcCad — Kalan Modüller Ana Spesifikasyonu

> Katı Model kabul testleri tamamlanmadan aşağıdaki modüller aktif geliştirmeye alınmaz. Çalışmayan komut UI'da aktif gösterilmez.

## Sac Levha
- Base-Flange / Tab: herhangi bir geçerli kapalı konturdan gerçek sac gövde.
- Edge Flange: grafik alanından kenar seçimi; uzunluk, açı, konum, büküm yarıçapı, canlı önizleme; detached plaka değil gerçek büküm.
- Miter Flange, Hem, Jog, Unfold, Fold, Flatten / Flat Pattern, Normal Cut.
- Kalınlık, ters yön, K-factor / bend allowance, relief.
- FeatureManager'da parametrik sac feature'ları; değişiklikte Flat Pattern yeniden hesaplanır.
- Gerçek açınımdan DXF/DWG çıktısı.

## Profiller / Weldments
- Structural Member, Trim/Extend, End Cap, Gusset, Weld Bead, Cut List, Structure System.
- Kullanıcının sağlayacağı profil kütüphanesi entegre edilebilir olmalı.
- Kesit, yön, döndürme ve köşe birleşimleri parametrik.
- Cut List gerçek gövde boyu/adedi üzerinden.

## Montaj
- .scpart ve alt montaj ekleme; .scasm belge modeli.
- Hiyerarşik FeatureManager.
- Sabit/serbest bileşen.
- Mate: Coincident, Concentric, Parallel, Perpendicular, Distance, Angle ve temel mekanik eşleşmeler.
- Yüz/kenar/eksen seçerek mate; taşıma/döndürme ve mate solver güncellemesi.
- Hide/Show, Suppress/Unsuppress, Isolate.
- Interference/collision kontrolü.
- Taşınabilir harici referanslar.

## Teknik Resim
- .scdrw, sheet/template.
- Ön, üst, sağ/sol, izometrik; projected, section, detail view.
- Sac Flat Pattern görünüşü.
- Teknik resim ölçüleri ve model ölçülerinin aktarımı.
- Center mark/centerline ve delik gösterimleri.
- Model değişince görünüş güncelleme.
- Antet/özel özellik bağlantıları.
- PDF ve DWG/DXF çıktı.
- Montaj BOM ve balonlar.

## Ölçüm / İnceleme
- Mesafe, açı, yarıçap/çap, koordinat.
- Gerçek B-Rep yüz/kenar/köşe sorguları.
- Kütle, hacim, yüzey alanı, ağırlık merkezi.
- Kesit görünümü ve interference/collision.

## Dosya ve belge modeli
Planlanan yerel formatlar: .scpart, .scasm, .scdrw.
- Feature/sketch/constraint/property verisi kaybolmadan kaydet/aç.
- Sürüm ve migration stratejisi.
- Güvenli/atomik kayıt.
- STEP/IGES ve uygun olduğunda DXF/DWG I/O ayrı katmanda.

## Özel Özellikler
- Çizen / Drawn By
- Onaylayan / Approved By
- Parça-Montaj Adı / Part-Assembly Name
- Renk Kodu / Color Code
- Kaynak İşlemi / Welding Process
- Malzeme / Material
- Kütle / Mass
- İşlem Tipi

İşlem Tipi: LAZER, KAYNAK, TESTERE, TALAŞLI İMALAT, MONTAJ, HAZIR MALZEME.
RAL kodları arbitrary custom property değil appearance sisteminin parçasıdır.

## Ortak UX kabul kuralı
Ana ilke: **“SOLIDWORKS kullanan el, SrcCad'de yabancılık çekmesin.”**
- CommandManager üstte; FeatureManager/PropertyManager solda; 3B viewport merkezde.
- MMB rotate, Ctrl+MMB pan, wheel zoom; sağ tık, Delete, Ctrl+Z/Y, Esc.
- Feature'a çift tıklama ile parametrik düzenleme.
- Shaded, Shaded with Edges, Wireframe, HLR, HLV.
- Fit/ISO/Front/Top/Right gerçek model bounds üzerinden.
- Her aktif komutta başarı + iptal + Undo/Redo + hatalı geometri kabul testleri.

## Geliştirme sırası
1. Katı Model
2. Sac Levha
3. Profiller / Weldments
4. Montaj
5. Teknik Resim
6. Ölçüm / İnceleme

Kullanıcı açıkça değiştirmedikçe bu sıra korunur.
