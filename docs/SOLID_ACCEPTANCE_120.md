# Katı Model kabul kapısı — 120 senaryo

**Durum: TAMAMLANMADI.** Kaynak incelemesi veya arayüz görüntüsü çalışma kanıtı sayılmaz.

Tarih: 2026-10-10. İlk kaynak tabanı: `98646e1`; çalışma sırasında eklenen `7ebba8c` gereksinim belgesi korunmuştur.

- PASS: gerçek Core/Commands C# kodu .NET 8 Roslyn ile derlenip çalıştırıldı.
- BLOCKED: Windows/WPF/C++/CLI/OCCT ortamı yok; test çalıştırılmadı.
- FAIL-SOURCE: kaynak incelemesinde gereken uygulama yok veya açıkça yetersiz; runtime PASS değildir.

## SM-001–060: çalıştırılan çekirdek testleri

Kod: `src/SrcCad.Tests/AcceptanceTests.cs`. Her test model geometrisini, reddedilen girdiyi veya snapshot durumunu doğrular. Mevcut smoke testleri ayrıca korunur.

| ID | Kontrol / beklenen sonuç | Durum |
|---|---|---|
| SM-001 | Ön Düzlem düzlem dönüşümü | PASS |
| SM-002 | Ön Düzlem sağ elli birim eksenler | PASS |
| SM-003 | Üst Düzlem düzlem dönüşümü | PASS |
| SM-004 | Üst Düzlem sağ elli birim eksenler | PASS |
| SM-005 | Sağ Düzlem düzlem dönüşümü | PASS |
| SM-006 | Sağ Düzlem sağ elli birim eksenler | PASS |
| SM-007 | NaN nokta reddi | PASS |
| SM-008 | Sonsuz nokta reddi | PASS |
| SM-009 | Aynı koordinatta sıfır çizgi reddi | PASS |
| SM-010 | Eksik noktalı çizgi reddi | PASS |
| SM-011 | NaN yarıçap reddi | PASS |
| SM-012 | Eksik merkezli daire reddi | PASS |
| SM-013 | Horizontal gerçek geometri | PASS |
| SM-014 | Vertical gerçek geometri | PASS |
| SM-015 | Çakışık iki nokta | PASS |
| SM-016 | Parallel iki çizgi | PASS |
| SM-017 | Perpendicular iki çizgi | PASS |
| SM-018 | Equal iki çizgi | PASS |
| SM-019 | Collinear iki çizgi | PASS |
| SM-020 | Eşmerkezli iki daire | PASS |
| SM-021 | Sabitle ve sürüklemeyi engelle | PASS |
| SM-022 | Parallel daire seçimi reddi | PASS |
| SM-023 | Perpendicular daire seçimi reddi | PASS |
| SM-024 | Collinear daire seçimi reddi | PASS |
| SM-025 | Eşmerkezli çizgi seçimi reddi | PASS |
| SM-026 | Eşit karma geometri reddi | PASS |
| SM-027 | Aynı objeye ilişki reddi | PASS |
| SM-028 | Eksik noktaya sabitle reddi | PASS |
| SM-029 | LineLength sürücü ölçü | PASS |
| SM-030 | PointDistance sürücü ölçü | PASS |
| SM-031 | HorizontalDistance sürücü ölçü | PASS |
| SM-032 | VerticalDistance sürücü ölçü | PASS |
| SM-033 | Çap sürücü ölçüsü | PASS |
| SM-034 | Ters yönlü ikinci çizgi açısı | PASS |
| SM-035 | Geçersiz ölçü reddi NaN | PASS |
| SM-036 | Geçersiz ölçü reddi Infinity | PASS |
| SM-037 | Geçersiz ölçü reddi -1 | PASS |
| SM-038 | Geçersiz ölçü reddi 0 | PASS |
| SM-039 | Çakışan ölçü atomik geri alma | PASS |
| SM-040 | 180 üstü açı reddi | PASS |
| SM-041 | Snapshot tam geri yükleme | PASS |
| SM-042 | Snapshot yineleme | PASS |
| SM-043 | Silinen nokta ölçüsünü temizle | PASS |
| SM-044 | Origin silinemez | PASS |
| SM-045 | Ölçü etiketini taşımak geometriyi korur | PASS |
| SM-046 | Tek dikdörtgen konturu | PASS |
| SM-047 | Üç dairesel delik | PASS |
| SM-048 | Poligon delik | PASS |
| SM-049 | Kendini kesen kontur reddi | PASS |
| SM-050 | Sıfır alan konturu reddi | PASS |
| SM-051 | Çakışan poligon delikler reddi | PASS |
| SM-052 | Daire poligon deliğini kesemez | PASS |
| SM-053 | Dış sınıra teğet delik reddi | PASS |
| SM-054 | Açık zincir reddi | PASS |
| SM-055 | Kontur yönlerini normalize et | PASS |
| SM-056 | Çizgi komutu geometri üretir | PASS |
| SM-057 | Dikdörtgen iptali geometri üretmez | PASS |
| SM-058 | Daire komutu geometri üretir | PASS |
| SM-059 | Merkez dikdörtgen komutu | PASS |
| SM-060 | Önizleme modeli değiştirmez | PASS |

## SM-061–072: gerçek OCCT B-Rep testleri

Kod: `NativeAcceptanceTests.cs`; üretimdeki aynı prism builder çağrılır. Sonucun `TopAbs_SOLID` ve `BRepCheck_Analyzer.IsValid` olması, BRepGProp hacminin analitik hacme 1e-4 mm³ içinde uyması gerekir. Mesh/ekran görüntüsü kullanılmaz.

| ID | Geometri | Beklenen | Durum |
|---|---|---|---|
| SM-061 | Ön: 250×200×20 plaka | 1.000.000 mm³ | PASS-WINDOWS-REPORTED |
| SM-062 | Ön: 250×200×20, 3×Ø15 delik | (50.000 − 3π×7,5²) × 20 mm³ | PASS-WINDOWS-REPORTED |
| SM-063 | Ön: 40×20 poligon delikli plaka | 984.000 mm³ | PASS-WINDOWS-REPORTED |
| SM-064 | Ön: −20 mm ters ekstrüzyon | 1.000.000 mm³; yön ters | PASS-WINDOWS-REPORTED |
| SM-065 | Üst: 250×200×20 plaka | 1.000.000 mm³ | PASS-WINDOWS-REPORTED |
| SM-066 | Üst: 250×200×20, 3×Ø15 delik | (50.000 − 3π×7,5²) × 20 mm³ | PASS-WINDOWS-REPORTED |
| SM-067 | Üst: 40×20 poligon delikli plaka | 984.000 mm³ | PASS-WINDOWS-REPORTED |
| SM-068 | Üst: −20 mm ters ekstrüzyon | 1.000.000 mm³; yön ters | PASS-WINDOWS-REPORTED |
| SM-069 | Sağ: 250×200×20 plaka | 1.000.000 mm³ | PASS-WINDOWS-REPORTED |
| SM-070 | Sağ: 250×200×20, 3×Ø15 delik | (50.000 − 3π×7,5²) × 20 mm³ | PASS-WINDOWS-REPORTED |
| SM-071 | Sağ: 40×20 poligon delikli plaka | 984.000 mm³ | PASS-WINDOWS-REPORTED |
| SM-072 | Sağ: −20 mm ters ekstrüzyon | 1.000.000 mm³; yön ters | PASS-WINDOWS-REPORTED |

## SM-073–120: arayüz, solver ve feature kabulü

| ID | Senaryo | Uygulama ve beklenen sonuç | Durum |
|---|---|---|---|
| SM-073 | Düzlem seçimi ve sketch başlangıcı | Ön, Üst ve Sağ düzlemi sırayla seçip çizgi çiz; çizgi seçilen düzlemin dünya koordinatlarında kalmalı. | BLOCKED |
| SM-074 | Tek tık seçim | Çizgi, daire, nokta ve ölçüye tıkla; yalnız hedef seçilmeli. | BLOCKED |
| SM-075 | Ctrl ile seçim ekle/kaldır | Üç objeyi Ctrl ile seç, birini tekrar tıkla; yalnız o obje seçimden çıkmalı. | BLOCKED |
| SM-076 | Boş alana tıklama | Seçim varken boş alana tıkla; seçim temizlenmeli. | BLOCKED |
| SM-077 | Sağ tık mevcut seçim | İki seçili çizginin üstünde sağ tık; seçim korunmalı, uygun ilişkiler uygulanabilmeli. | BLOCKED |
| SM-078 | Hover ile seçim ayrımı | Model yüzünde hover, sonra seçim; hover ve kalıcı seçim ayrı görünmeli. | BLOCKED |
| SM-079 | Delete ve geri alma | Çizgi ve ona bağlı ölçüyü sil; Ctrl+Z her ikisini, Ctrl+Y silinmiş durumu getirmeli. | BLOCKED |
| SM-080 | Esc çizgi zinciri | Zincir çizerken Esc; bekleyen adım iptal, tamamlanmış çizgiler korunmalı. | BLOCKED |
| SM-081 | Esc ölçü yerleştirme | İkinci çizgi seçildikten sonra Esc; yeni ölçü ve geometri değişikliği oluşmamalı. | BLOCKED |
| SM-082 | MMB döndürme | Katı ve sketch modunda orta tuşla döndür; model değişmemeli. | BLOCKED |
| SM-083 | Ctrl+MMB pan | Görünümü kaydır; kamera yönü ve model geometrisi korunmalı. | BLOCKED |
| SM-084 | Tekerlek zoom | İmleç model üzerindeyken zoom; geometri boyutu değişmemeli. | BLOCKED |
| SM-085 | Fit ve standart görünümler | Uzak geometri oluştur; F/ISO/Ön/Üst/Sağ bütün modeli doğru eksenlerle göstermeli. | BLOCKED |
| SM-086 | Native viewport Ctrl+Z/Y | WinForms viewport odaklıyken çizim ekle, geri al ve yinele; kısayollar WPF odağı gerektirmemeli. | BLOCKED |
| SM-087 | Metin kutusunda kısayollar | Derinlik kutusunda Delete ve Ctrl+Z; yalnız metin düzenlenmeli, model silinmemeli. | BLOCKED |
| SM-088 | İki çizgi açı ölçüsü | Ctrl kullanmadan iki çizgi ve konum seç; 30 derece gir; ölçülen gerçek açı 30 olmalı. | BLOCKED |
| SM-089 | Ölçü düzenleme ve iptal | Ölçüye çift tık; önce Esc ile iptal, sonra değer değiştir; iptal kayıpsız, onay undo edilebilir olmalı. | BLOCKED |
| SM-090 | Ölçü etiketi sürükleme undo | Etiketi taşı; Ctrl+Z yalnız etiket konumunu geri getirmeli. | BLOCKED |
| SM-091 | Sabit nokta sürükleme | Sabit nokta taşınamamalı; merkeze göre dikdörtgen köşesinde de sabitle ihlal edilmemeli. | BLOCKED |
| SM-092 | Gerçek solver DOF hesabı | Yinelenen/bağımlı ilişkiler serbestliği yanlış azaltmamalı; Jacobian rank veya eşdeğer doğrulanmış yöntem kullanılmalı. | BLOCKED |
| SM-093 | Solver çakışma raporu | Çözülemeyen ilişki kümesi ilgili ilişkileri göstermeli; geometri ve önceki değerler atomik korunmalı. | BLOCKED |
| SM-094 | İlişki zinciri çözümü | En az 20 ilişkili çizgi ve üç sürücü ölçü düzenlenince tüm kısıtlar tolerans içinde sağlanmalı. | BLOCKED |
| SM-095 | Teğet ilişkisi | Daire-çizgi ve daire-daire teğetlik gerçek geometrik kısıt olarak çözülmeli. | BLOCKED |
| SM-096 | Kontur adası | Dış profil, delik ve deliğin içindeki ada; malzeme derinlik paritesine göre üretilmeli. | BLOCKED |
| SM-097 | Eşmerkezli halkadan katı | İki eşmerkezli daire ekstrüzyonu ortası boş boru üretmeli. | BLOCKED |
| SM-098 | Birden fazla dış bölge | İki ayrık kapalı profil seçilince iki geçerli solid üretilmeli; bölgeler kaybolmamalı. | BLOCKED |
| SM-099 | Concave kontur sınırı | İç poligonun köşeleri içeride fakat kenarı dışarıda olan profil reddedilmeli. | BLOCKED |
| SM-100 | Native geçersiz katı reddi | Kendini kesen profil native katmana verilince başarısız sonuç alınmalı; geçersiz B-Rep commit edilmemeli. | BLOCKED |
| SM-101 | Geçersiz derinlik stale preview | Geçerli preview sonrası 0, NaN, Infinity ve metin gir; onay eski preview katısını kaydetmemeli. | BLOCKED |
| SM-102 | Feature düzenleme iptali | Ekstrüzyon1 20 mm iken 30 mm preview yapıp Esc; gerçek hacim 20 mm değerine dönmeli. | BLOCKED |
| SM-103 | Feature derinlik undo/redo | 20 → 30 → 40 mm değiştir; iki undo ve iki redo sonrası gerçek hacimler sırasıyla doğrulanmalı. | BLOCKED |
| SM-104 | Feature oluşturma undo | İlk ekstrüzyonu oluşturup Ctrl+Z; feature ve solid kaldırılmalı, Ctrl+Y geri getirmeli. | BLOCKED |
| SM-105 | Sketch düzenle ve downstream rebuild | Ekstrüzyondan sonra sketch genişliğini değiştir; tüm bağımlı katılar yeniden üretilmeli. | BLOCKED |
| SM-106 | FeatureManager bağımlılık ağacı | Birden fazla sketch ve feature ekle; düzenleme ve hata durumu doğru node ile ilişkilendirilmeli. | BLOCKED |
| SM-107 | Feature silme ve undo | Feature silindiğinde bağımlı feature davranışı belirli olmalı; undo tüm bağımlılıkları geri getirmeli. | BLOCKED |
| SM-108 | Parametrik kesme ekstrüzyonu | Katı üzerine sketch ile kes; hacim gerçek olarak azalmalı ve derinlik değişiminde rebuild olmalı. | BLOCKED |
| SM-109 | Parametrik döndürme | Kapalı profili eksen etrafında döndür; açı düzenleme ve undo gerçek B-Rep değiştirmeli. | BLOCKED |
| SM-110 | Parametrik fillet/chamfer | Seçilen kenarlara yarıçap/pah uygula; hatalı değer katıyı bozmamalı. | BLOCKED |
| SM-111 | Dosya kaydet/aç | scpart kaydet, kapat ve aç; sketch, ölçü, ilişki ve feature parametreleri kayıpsız kalmalı. | BLOCKED |
| SM-112 | Belgeye göre history izolasyonu | Yeni Parça sonrası eski undo/redo ve seçim hedefleri kullanılmamalı. | BLOCKED |
| SM-113 | Başarısız işlem history | Geçersiz ölçü veya rectangle işlemi redo zincirini silmemeli; boş history adımı oluşturmamalı. | BLOCKED |
| SM-114 | Aktif araç sırasında undo | Çizgi zincirinde undo sonrası mouse hareketi/sonraki tık silinmiş nokta ID hatası üretmemeli. | BLOCKED |
| SM-115 | Pencere boyutu ve DPI | %100/%150/%200 DPI ile resize; toolbar, ölçü düzenleme ve seçim konumu erişilebilir kalmalı. | BLOCKED |
| SM-116 | Gerçek yüz seçimi | Katının ön, yan ve delik yüzlerini seç; AIS gerçek TopoDS_Face hedeflerini vermeli. | BLOCKED |
| SM-117 | Gerçek kenar/köşe seçimi | Kenar ve köşe modunu değiştir; uygun alt-topoloji seçilmeli. | BLOCKED |
| SM-118 | Önizleme başarısızken committed solid | Düzenleme sırasında geçersiz profil/değer; iptal eski solid ve seçim modunu korumalı. | BLOCKED |
| SM-119 | Tam Windows derleme | VS C++/CLI + OCCT ile Debug ve Release derlemeleri; hiçbir eksik alan, ABI veya DLL hatası olmamalı. | BLOCKED |
| SM-120 | Mevcut özellik regresyon turu | BASLAT ile aç; önceki TEST_PLAN.md akışlarının tamamını uygula; aktif her komutta onay/iptal/hata senaryoları geçmeli. | BLOCKED |

## Sonuç ve yeniden çalıştırma

120 senaryonun **72 PASS (12 Windows kullanıcı bildirimi), 14 FAIL-SOURCE, 34 BLOCKED** sonucu var. Modül kabul edilmedi.

İlk 60 senaryoda düzeltme öncesi 36 PASS / 24 FAIL; düzeltme sonrası 60 PASS. Ham çıktılar `docs/validation/core-before.log` ve `core-after.log`.

Standart .NET 8 ortamında: `dotnet run --project src/SrcCad.Tests -c Debug -p:Platform=x64`.

MSBuild process introspection kullanılamayan Linux ortamında: `python tools/run-core-tests.py /path/to/dotnet-sdk-root`. Bu sadece managed testleri derler ve çalıştırır; Windows uygulama derlemesi değildir.

Windows/OCCT ortamında: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Run-SolidAcceptance.ps1`. Mevcut OCCT yolu farklıysa `-OcctRoot` verin. Çıktılar `logs/solid-*.log` altında. Bu komut dosyası burada Windows üzerinde çalıştırılmadı.

72 otomatik test geçse bile SM-073–120 tamamlanmadan Katı Model tamamlandı denemez. Sac Levha, Profil, Montaj ve Teknik Resim geliştirmesine geçilmez.

## 2026-10-11 ilerlemesi

- Windows native deployment düzeltildi: köprü, ijwhost ve OCCT DLL’leri ayrı native test çıktısına açıkça dağıtılır. 8 dosya dağıtım kontrolü geçti. Bunlar SM-061–072 native B-Rep testleri değildir.
- Feature oluşturma, derinlik düzenleme ve tek ekstrüzyonun silinmesi artık sketch snapshot + feature durumu ile history kaydeder. Hatalı rebuild history yığınlarını ilerletmez.
- 12 ek managed history kontrolü geçti; `docs/validation/feature-history-tests.log`. Bu ek kontroller 120 maddelik kabul kapsamının sayısını veya UI PASS sayısını artırmaz.
- SM-104 artık uygulanmış fakat Windows/UI doğrulaması bekliyor. SM-107 için tek feature silme/undo geliştirildi; bağımlılık ağacı olmadığı için tam kriter hâlâ FAIL-SOURCE.
- Genel solver, bağımlı feature rebuild ve çok seviyeli konturlar açık. Kullanıcı güncel branch üzerinde 60/60 core, 12/12 history ve 12/12 gerçek OCCT PASS bildirdi. Modül tamamlanmadı.

## Windows geri bildirimi ve etkileşim regresyonları

Kullanıcı 2026-10-11 tarihinde güncel branch için native deployment sorununun çözüldüğünü ve 12 gerçek B-Rep testinin geçtiğini bildirdi. SM-061–072 bu bildirime dayanır; bu Linux oturumunda native test çalıştırılmadı. 72 kabul maddesine ek 12 history kontrolü vardır.

`InteractionRegressionTests` 50 ek managed kontrol içerir: üç düzlemde her iki seçim sırasıyla iki daire merkezi ve Origin–dikdörtgen yatay/dikey sürücü ölçüleri, koordinat/radyüs/kapalı profil doğrulaması, 24 ilişkilik eksen zinciri, sabit uçta atomik ret ve wheel detent birikimi. İlk Origin–dikdörtgen testi başarısızdı; bağlı eksen yayılımı düzeltildikten sonra geçti. Bunlar tam genel solver kanıtı değildir; SM-094 karma ilişkiler için açık kalır.

Windows tekrar turu (SM-084, SM-088, SM-120 alt senaryoları; henüz PASS değil):
1. Ekstrüzyon PropertyManager üzerinde ve viewport üzerinde tekerleği çevir: her kademe ±10 mm; metin ve gerçek önizleme birlikte değişmeli. 0 mm önizlemeyi geçersiz kılmalı; negatif derinlik ters yönde oluşmalı. Onay, iptal, undo/redo dene.
2. Çapraz yerleşmiş iki dairenin merkezlerini seç. Etiketi üst/alta taşı: yatay; sağ/sola taşı: dikey; iki noktanın sınırlayıcı kutusu içinde: hizalı mesafe. Önizleme ve oluşturulan tür aynı olmalı. 100 ve 120 mm değerleri merkezleri hareket ettirmeli, çapları korumalı.
3. Origin ile dikdörtgen köşesini iki farklı seçim sırasıyla ölçülendir. X=40, Y=50 ver; Origin sabit, köşe (40,50), profil kapalı kalmalı. Çift tıkla düzenle, Ctrl+Z/Y ve Esc ile iptali kontrol et.

Tam Windows/WPF olay yönlendirmesi doğrulanmadan bu üç kullanıcı hatası kapatılmış kabul edilmeyecek.

## Kaynak çizim düzenleme ilerlemesi

SM-105 için tek mevcut Ekstrüzyon1'in kaynak çizimi artık Çizimi Düzenle düğmesi veya FeatureManager Çizim1 çift tıklamasıyla açılır. Çizimden çıkmak OCCT rebuild yapar; onay tek feature history adımıdır. Geçersiz profil düzenleme oturumunda kalır, history ilerlemez. Düzenlemeyi İptal Et veya seçim modunda Esc eski çizimi ve katıyı geri getirir. Yeni yüz sketch'i veya genel çok-feature bağımlılık grafiği henüz uygulanmadı.

18 managed işlem kontrolü geçti (`docs/validation/source-sketch-tests.log`). Native suite'e üç düzlemde önce/düzenle/geri yükle/yeniden uygula için 12 gerçek hacim kontrolü eklendi; **bu yeni native kontroller henüz Windows'ta çalıştırılmadı**. Önceki 12 PASS bildirimi yeni testlere aktarılmaz.

Windows'ta: 100×60×20 katı oluştur; Çizim1'i düzenleyip genişliği150 yap, çıkışta katı150×60×20 olmalı; Ctrl+Z/Y ile iki geometriye dön. Tek kenarı silip çıkmayı dene: başarısız rebuild sonrası düzeltme veya iptal mümkün olmalı. İptal eski100/150 genişliği korumalı ve redo geçmişini silmemeli. SM-105 ve tam SM-073–120 kabulü bu akış doğrulanana kadar açık.


## 2026-10-13 kaynak tamamlama güncellemesi

Aşağıdaki maddelerde önceki **FAIL-SOURCE** gerekçeleri artık geçerli değildir; kaynak ve otomatik test altyapısı eklenmiştir. Windows/WPF/C++/CLI/OCCT gerçek kullanım turu bu oturumda çalıştırılmadığı için durumları yalnızca **BLOCKED** olarak güncellendi, PASS verilmedi.

- SM-092: Jacobian rank tabanlı DOF ve redundant denklem sayımı mevcut.
- SM-093: çakışma durumunda sorunlu ilişki/ölçü kimlikleri raporlanır; başarısız çözüm snapshot'a atomik döner.
- SM-094–095: karma ilişki zincirleri ve çizgi-daire / daire-daire teğetlik solver/test kapsamındadır.
- SM-096–098: parity tabanlı çok-bölgeli sketch çözümü; delik içindeki ada, eşmerkezli halka ve ayrık dış bölgeler gerçek extrusion region olarak çözülür. Native evaluator ayrık bölgeleri compound solid olarak üretir.
- SM-105–107: feature dependency graph, incremental downstream rebuild, suppress/block, cascade delete ve Undo/Redo transaction katmanı mevcuttur.
- SM-108–110: parametrik Cut, Revolve/RevolveCut, Fillet ve Chamfer gerçek OCCT evaluator zincirindedir.
- SM-111: schema-2 .scpart save/open feature kimliği, parametreler, suppression ve semantik topoloji referanslarıyla round-trip test edilir.
- Kalıcı E/F/C topoloji imzaları artık Core save validation aşamasında doğrulanır; bozuk/tekrarlı referans dosyaya yazılmaz.
- Teknik resim bağımlı görünüş/annotation pafta bütünlüğü ve L/D/R/A ilişkisel ölçü payload'ları save aşamasında doğrulanır.
- Montaj F/C yüz referansları ve mate türü/referans uyumluluğu save ve UI seçim aşamasında doğrulanır.

Yeni/yenilenen testler Windows native acceptance paketine bağlıdır; gerçek PASS için tools/Run-SolidAcceptance.ps1 ile Windows x64 OCCT turu gerekir.

## Issue #3 Faz A bağlantısı

Kullanıcı Windows'ta Core60 + History12 + Interaction50 + SourceSketch18 + Native12 + NativeSource12, FAIL0 bildirdi; baseline kapısı geçildi. Buna rağmen merkez ilişkileri, Origin ölçü akışı, Fully Defined ve Centerline manuel olarak başarısız bildirildi. Bu akışlar eski otomatik PASS ile kapatılmadı.

Yeni ortak reference/selection/solver çalışması ve kanıtlar: `validation/ISSUE_3_PHASE_A.md`. Uzun vadeli200 madde: `CAD_ACCEPTANCE_200.md`. Mevcut SM matrisi bunların yerine kaldırılmadı; Windows manuel maddeler hâlâ doğrulama bekliyor.
