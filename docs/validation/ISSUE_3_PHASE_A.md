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


## İkinci checkpoint: parametrik dosya + CommandManager

- `.scpart` schema1: düzlem, mm birimi, sketch entity/constraint/dimension/reference snapshot ve mevcut tek ekstrüzyon parametresi. Save temp+replace kullanır; Open parametreleri yeniden kurup OCCT rebuild yapar. Bu format henüz çok-feature history/graph formatı değildir.
- 33 gerçek JSON/dosya round-trip kontrolü: referans+construction korunması, yeniden açtıktan sonra driving edit, ilişki korunması, silme/undo referans yaşamı, schema/unit/depth/ID bozuk girdi reddi.
- Üç düzlem × önce/edit/restore/reapply için 12 yeni gerçek OCCT reference round-trip hacim testi eklendi. **PENDING WINDOWS**. Toplam native suite artık36 kontroldür; önceki24 kullanıcı PASS bu yeni branch'in doğrulanması değildir.
- Özgün WPF vektör ikonları; Çizgi→Çizgi/Merkez Çizgisi ve Dikdörtgen→Köşe/Merkez aileleri. Çalışmayan Arc/Slot/Fillet vb. komutlar aktifmiş gibi eklenmedi. UI görünümü/menü klavye davranışı Windows'ta PENDING.
- Mevcut140 + Foundation102 + Roundtrip33 = **275 managed PASS**, 0 FAIL.

Windows tekrar: Origin→köşe X/Y, iki merkez Ctrl-seç→Yatay/Dikey, merkez→kenar iki seçim sırası, Fix olmadan Fully Defined örneği, merkez çizgisi profilden dışlanması, .scpart aç→ölçü edit→katı rebuild; ardından `Run-SolidAcceptance.ps1` Debug/Release. Yeni native round-trip12 sonucunu ayrıca kaydedin.


## Üçüncü checkpoint: genel ilişkiler ve kabul matrisi

Point-line Coincident/Midpoint; line-circle ve circle-circle Tangent ilişkileri ortak referans residual sistemine eklendi. Tangency internal/external dalı ve line tarafı snapshot/dosyada saklanır. Midpoint+Tangent gibi çakışmalar atomik reddedilir. Rank tanısı bağımlı denklem sayısını da raporlar; bu henüz tek tek redundant constraint teşhisi değildir.

51 yeni kontrol: üç düzlemde midpoint sonrası length edit, tangent sonrası diameter edit ve round-trip, çakışmada rollback, ters sırada duplicate relation, Origin–oblique line Coincident, oblique point-line driving distance, duplicate equation rank. Toplam **326 managed PASS / 0 FAIL**. Native36 bu yeni kaynakta PENDING.

`docs/CAD_ACCEPTANCE_200.md` tam200 benzersiz ID ve requirement/type/test/status/implementation/failure-notes alanlarını içerir. PARTIAL/PENDING/TODO maddeleri tamamlandı sayılmaz. Diğer modüllere geçilmedi.

Sıfır eksen mesafesi de desteklendi: Origin ile aynı X/Y üzerindeki noktaya 0 ölçüsü konulup sonradan15 mm yapılabilir. Uzunluk/çap/açı için pozitif değer kuralı korunur.


## Dördüncü checkpoint: gerçek araç genişlemesi

- 3 nokta dairesi: çevrel çember geometrisi, collinear ret, yakalanan mevcut noktalar için point-circle Coincident, paylaşılan solver ve gerçek rank. Sabit üç noktayla belirlenen dairede çakışan çap değişimi reddedilir.
- 3 noktalı köşe dikdörtgen: döndürülmüş kapalı profil, paralel/dik ilişkiler, sürücü kenar boyutları ve .scpart sonrası edit. Parallel başlangıç adımı mevcut ters yönü korur.
- ConstructionLine yanında Line/Axis semantik referansı ortak line dimension sistemine katılır.
- 39 yeni managed araç kontrolü;100/500 entity üzerinde200 doğru hit için2 ölçek smoke senaryosu. Host'a bağlı süreler `phase-b-tools-managed.txt`; Windows gecikme kabulü değildir.
-9 yeni gerçek native volume kontrolü: üç düzlemde döndürülmüş rectangle önce/ölçü edit ve3point circle hole. **PENDING WINDOWS**. Native suite toplam45; bu tur native çalıştırılmadı.
- Managed toplam **367 PASS / 0 FAIL**; ayrıca deployment8/8 PASS. Yeni managed227; önceki140 korunuyor.
