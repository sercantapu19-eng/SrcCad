> **Geliştirme durumu (2026-10-10): Katı Model tamamlanmadı.** Güncel doğrulama kaydı:
> [120 senaryolu kabul kapısı](docs/SOLID_ACCEPTANCE_120.md).
> 60 managed test çalıştırıldı; Windows/WPF ve gerçek OCCT doğrulaması bekliyor.
> Aşağıdaki eski sürüm notları güncel kabul sonucu olarak değerlendirilmemelidir.

# SrcCad V1.2 CAD Alpha

Bu paket, SrcCad V1.1 Foundation testinin üzerine kurulan **ilk çizim yapılabilir OCCT sürümüdür**.
Eski `WpfPreviewKernel` kullanılmaz. Katı model ve seçim Open CASCADE (OCCT) üzerinden yürür.

## Bu sürümde çalışan hedef akış

1. `Ön / Üst / Sağ Düzlem` seç.
2. `Çizim` komutuna bas.
3. Aynı gerçek 3B viewport içinde:
   - Çizgi
   - Köşe Noktası ile Dikdörtgen
   - Merkez ile Dikdörtgen
   - Daire
   çiz.
4. Origin ve mevcut sketch endpoint'lerine 10 px civarında snap uygulanır.
5. `Çizimden Çık`.
6. Kapalı tek profil varsa `Ekstrüzyon` aktif olur.
7. Derinliği PropertyManager'dan değiştir; yeşil ✓ ile onayla.
8. Sonuç gerçek OCCT `TopoDS_Shape` / B-Rep katıdır.
9. Yüz / Kenar / Köşe seçim modları OCCT AIS seçimi ile çalışır ve mouse hover'da pre-highlight verir.

## Mouse

- Orta tuş sürükle: döndür
- Ctrl + Orta tuş sürükle: pan
- Wheel: zoom
- F: Fit
- Esc: aktif sketch adımını / feature önizlemesini iptal et
- Ctrl+N: yeni parça

## Merkez ile Dikdörtgen

Merkez dikdörtgeni yalnız dört bağımsız çizgi olarak kaydedilmez. Modelde merkez noktası,
dört köşe ve merkez-dikdörtgen tanımı tutulur; iki construction diagonal da gösterilir.
Bu temel, V1.3 solver/Smart Dimension aşamasında merkez sabit kalacak driving dimension davranışı için hazırlanmıştır.

## Bilerek henüz yok

- Smart Dimension / driving dimension
- DOF solver ve sketch relation çözümü
- Cut / Revolve / Fillet vb.
- Sac Levha
- Profiller
- Montaj
- Teknik resim

Çalışmayan komutlar aktif gösterilmez.

## Kurulum

OCCT kurulumu şu konumda olmalıdır:

`D:\SrcCadV11\vcpkg_installed`

Bu paketin **içeriğini** şu konuma koyun:

`D:\SrcCadV11\SrcCad`

Sonra yalnızca:

`BASLAT.bat`

çalıştırın. Batch dosyası `Debug|x64` derler, çekirdek/komut smoke testlerini otomatik çalıştırır ve yalnız testler geçerse programı açar.
Derleme veya smoke test başarısız olursa `COLLECT_DIAGNOSTICS.bat` tek ZIP üretir.

## V1.2.1 Build Fix

Bu paket V1.2 CAD Alpha'nın Windows SDK / C++/CLI derleme düzeltmesidir.

Düzeltilen iki kök sorun:

1. `IServiceProvider` adı, C++/CLI `System` namespace'i ile Windows SDK `servprov.h`
   arasında çakışıyordu. Managed namespace artık native Windows/OCCT header'larından
   önce global olarak açılmıyor.
2. `windows.h` içindeki `min/max` makroları OCCT'nin `std::max` / `std::min`
   kullanımlarını bozuyordu. `NOMINMAX` etkinleştirildi.

Mevcut `D:\SrcCadV11\vcpkg_installed` klasörüne dokunmayın.

## V1.2.2 Build Fix

Diagnostics paketindeki son iki hata giderildi:

- `Application`: `System.Windows.Forms.Application` / `System.Windows.Application`
- `KeyEventArgs`: `System.Windows.Forms.KeyEventArgs` / `System.Windows.Input.KeyEventArgs`

Kök sebep `UseWPF + UseWindowsForms + ImplicitUsings` birlikteliğinin global namespace
çakışması üretmesiydi. `SrcCad.App` için implicit usings kapatıldı ve WPF tipleri açıkça
belirtildi.

## V1.2.3 Restore Fix

Diagnostics paketindeki son hata C++/CLI veya OCCT değildi. C++/CLI köprüsü başarıyla
derlendi; C# projelerinde `obj/project.assets.json` dosyaları olmadığı için
`NETSDK1004` oluştu.

`BASLAT.bat` artık derlemeden önce aynı MSBuild çağrısında `/restore` çalıştırır.
Bu nedenle `bin/obj/x64` klasörleri silinmiş olsa bile gerekli assets dosyaları
otomatik yeniden oluşturulur.

# V1.3 Parametric Sketch Alpha

Bu sürüm V1.2'nin yalnızca ekstrüzyon testinden sonra gelen kullanım davranışı milestone'udur.

## Eklenenler

- Gerçek OCCT yüz / kenar / köşe seçimi için kalıcı seçili vurgu
- Model ve sketch için sağ tık menüsü
- Sketch obje / endpoint / ölçü hit-test ve seçim
- Delete ile sketch öğesi silme
- Yatay, Dikey, Çakışık, Paralel, Dik, Eşit ve Sabitle ilişkileri
- Otomatik yatay/dikey çıkarımı
- Akıllı Ölçülendirme:
  - çizgi uzunluğu
  - daire çapı
  - iki nokta mesafesi
  - iki çizgi açısı
- Ölçü değerlerinin OCCT viewport içinde gösterilmesi
- Ölçüye çift tıklama / PropertyManager'dan düzenleme
- Sürücü ölçü değişince gerçek geometrinin değişmesi
- Merkez dikdörtgende genişlik/yükseklik değişirken merkezin korunması
- Eksik tanımlı nokta sürükleme; ilişkiler ve driving ölçüler korunur
- Sketch durumunda yaklaşık DOF ve mavi/siyah geometri durumu
- Otomatik smoke testte "label değil geometri değişiyor" doğrulaması

## Bilinen sınır

V1.3'te DOF hesabı konservatif cebirsel sayımdır; Jacobian-rank tabanlı nihai solver
bir sonraki solver milestone'unda gelecektir. Buna rağmen bu sürümdeki driving dimension
ve ilişkiler gerçekten geometriyi değiştirir; yalnızca ekrandaki metni değiştirmez.

# V1.4 Usable Part Alpha

Bu milestone tek tek mikro yamalar yerine günlük parça modelleme akışını toparlar.

## Smart Dimension
Akış:
1. Geometriyi seç (istersen Smart Dimension'dan önce de seçebilirsin).
2. Akıllı Ölçülendirme.
3. Ölçü mouse ile canlı önizlenir.
4. Konumu tıkla.
5. Değeri gir.
6. Driving dimension gerçek geometriyi sürer.

Tek çizgide normal ikinci tık artık açı seçmeye çalışmaz; ölçüyü yerleştirir.
Açı için iki çizgi Ctrl ile seçilir.

## Delikli ekstrüzyon
Kapalı çizgi dış konturu + içerideki daireler tek profil kabul edilir.
Daireler OCCT face üzerinde inner wire/delik olarak oluşturulur ve prizma delikli çıkar.

## Ekstrüzyon derinliği
Ekstrüzyon önizlemesi açıkken mouse tekeri:
- ileri: +10 mm
- geri: -10 mm
- minimum: 10 mm

Normal modelleme modunda mouse tekeri yine zoom yapar.

