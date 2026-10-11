# Issue #3 — Faz A, çalışan checkpoint

2026-10-11. Başlangıç: `2078819`, `dev/smart-dimension-core`.

## Baseline ve manuel kabul girdisi

Kullanıcı Windows'ta Core60, History12, Interaction50, SourceSketch18, NativeAcceptance12 ve NativeSourceSketch12 PASS / 0 FAIL bildirdi. Windows baseline kapısı geçildi. Bu rapor kullanıcı bildirimi olarak kaydedilir; Linux oturumunda native test çalıştırıldığı anlamına gelmez.

Otomatik baseline geçmesine rağmen kullanıcı şu gerçek akışları başarısız bildirdi: iki merkez Horizontal/Vertical ilişki, merkezleri parametrik hizalama, Origin–dikdörtgen ölçüsü, Fix olmadan Fully Defined, Centerline aracı. Bunlar yeni regresyon girdileridir; otomatik test sayısı manuel sorunları kapatmaz.

## Uygulanan ortak altyapı

- `GeometryReference`: Origin, Point, Endpoint, CenterPoint, Midpoint, Line, ConstructionLine, Circle; ekran koordinatı yerine model kimliği/rolü.
- `SmartDimensionSession`: desktop ve testlerin kullandığı ortak seçim/yerleştirme durum makinesi. Point-like çiftler, tek gövde, point-like/line her iki seçim sırası.
- Reference tabanlı Horizontal/Vertical/Coincident ilişkiler; tüm merkezler/noktalar aynı residual üzerinden çözülür. Point-line ölçüsü sonsuz taşıyıcı çizgiye dik mesafedir; yatay/dikey kenarda uygun eksen mesafesine eşittir.
- Damped least-squares residual/Jacobian çözümü. Sabit noktalar değişken sisteminden çıkarılır. Başarısız referans ilişkisi/dimension edit snapshot'a döner. Signed çözüm dalı referans ölçüsünde saklanır.
- DOF: parametre sayısı eksi Jacobian rank. Eski yaklaşık constraint sayımı kaldırıldı. Düzenli çözüm noktasında yerel DOF'dur; global uniqueness/singularity sertifikası değildir.
- Centerline komutu construction semantiğiyle çizgi üretir; seçim/ölçü/ilişkilerde yer alır ve ekstrüzyon sınırına girmez.
- UI ortak motoru kullanır; preview ve commit farklı ölçü çıkarım kodları içermez.

## Kanıt ve açık sınırlar

`SketchFoundationTests` üç düzlemde 102 kontrol: merkez/gövde ayrımı, Origin seçim→ölçü akışı, point-line her iki seçim sırası, gerçek koordinatlar, referans hizalamanın sonraki ölçüde korunması, Fix olmadan dikdörtgen+iki daire 0 DOF, ölçü kaldırılınca 1 DOF, çakışmada rollback, construction profile dışlama ve midpoint çözümleme.

Mevcut 140 managed kontrol korunur. Yeni Windows/native koşu PENDING. Kullanıcı raporundaki 24 native PASS bu yeni koda aktarılmaz.

Bu checkpoint **Issue #3 tamamlandı değildir**. Genel referans migration devam ediyor: legacy ilişkilerde eski başlangıç çözümü korunuyor ve residual solver fallback kullanılıyor. Tangent, Symmetric, tüm point-line ilişkileri, rank redundancy/conflict provenance, sparse/component solving, kapsamlı inference/cycling, arc/curve geometrileri açık. Midpoint referansı API'de var; viewport'ta midpoint seçim/işaretleme tam değil. .scpart round-trip, revision/dirty graph, çok-feature rebuild, persistent topology ve nested regions sonraki fazlardır. UI ve native için manuel PASS iddiası yok.
