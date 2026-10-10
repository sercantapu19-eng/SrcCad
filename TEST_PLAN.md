# SrcCad V1.2 CAD Alpha — Kabul Testi

## 0. Tek tık başlangıç

`D:\SrcCadV11\SrcCad\BASLAT.bat`

Beklenen: derleme başarılı, `SrcCad.Tests` smoke testleri PASS ve SrcCad ana penceresi açılır.

## 1. Ön Düzlem / dikdörtgen / ekstrüzyon

1. FeatureManager → Ön Düzlem.
2. Çizim.
3. Köşe Noktası ile Dikdörtgen.
4. İki köşe tıkla.
5. Çizimden Çık.
6. Ekstrüzyon.
7. Derinlik = 30.
8. ✓.

Beklenen: XY düzleminden +Z yönünde gerçek B-Rep prizma.

## 2. Üst Düzlem

Yeni Parça → Üst Düzlem → Çizim → dikdörtgen → Çizimden Çık → Ekstrüzyon.

Beklenen: profil XZ düzleminde, extrude yönü +Y. Global Z'ye yanlış çıkmamalı.

## 3. Sağ Düzlem

Yeni Parça → Sağ Düzlem → Çizim → daire → Çizimden Çık → Ekstrüzyon.

Beklenen: daire YZ düzleminde, extrude yönü +X.

## 4. Merkez ile Dikdörtgen

Yeni Parça → Ön Düzlem → Çizim → Merkez ile Dikdörtgen.
Origin'e yakın merkezi seç, köşe seç.

Beklenen: dikdörtgen ve iki construction diagonal aynı 3B sketch sahnesinde görünür.

## 5. Mouse navigasyon

- Orta tuş: döndür
- Ctrl+Orta: pan
- Wheel: zoom
- Fit / ISO / Ön / Üst / Sağ

Beklenen: çizim modunda da aynı 3B viewport döner; ayrı Canvas'a geçiş yok.

## 6. Alt-topoloji seçimi

Bir ekstrüzyon oluştur.

- Yüz → hover ve click
- Kenar → hover ve click
- Köşe → hover ve click

Beklenen: OCCT dynamic pre-highlight ve seçili alt-topoloji vurgusu.

## Sorun olursa

`COLLECT_DIAGNOSTICS.bat` çalıştır.
Proje kökünde `SrcCad_Diagnostics_YYYYMMDD-HHMMSS.zip` oluşur.

# V1.3 kabul testi

1. Ön Düzlem -> Çizim.
2. Merkez ile Dikdörtgen: merkezi Origin'e koy.
3. Akıllı Ölçülendirme -> üst kenar -> ölçü yeri -> 200 mm.
4. Aynı ölçüye çift tık -> 300 mm.
   Beklenen: dikdörtgen gerçek olarak 300 mm genişler ve Origin'deki merkez kaçmaz.
5. Bir endpoint seç -> sürükle.
   Beklenen: driving ölçü ve yatay/dikey ilişkiler korunur.
6. Ctrl ile iki çizgi seç -> Paralel veya Dik.
   Beklenen: geometri hareket eder.
7. Sağ tık.
   Beklenen: Sil / ilişkiler / Akıllı Ölçülendirme / Seçimi Temizle menüsü gelir.
8. Çizimden çık -> Ekstrüzyon -> onayla.
9. Yüz seçimi -> mouse ile yüz hover -> sol tık.
   Beklenen: hover sarı, seçili yüz turuncu kalır ve durum çubuğu 'Yüz: 1 seçili' yazar.
10. Sağ tık 3B model.
   Beklenen: Yüz/Kenar/Köşe seçimi, Seçimi Temizle, Ekrana Sığdır menüsü gelir.

# V1.4 hızlı kabul testi

1. Merkez dikdörtgen çiz.
2. Akıllı Ölçülendirme -> üst kenar.
   Beklenen: mouse hareketinde ölçü çizgisi canlı taşınır.
3. Konumu tıkla -> 250 -> Enter.
   Beklenen: düzgün extension/dimension çizgileri, uzun diagonal leader yok.
4. Dikdörtgen ortasına daire çiz.
5. Çizimden çık -> Ekstrüzyon.
   Beklenen: preview oluşur; daire delik olarak kalır.
6. Mouse tekerini iki kademe ileri.
   Beklenen: derinlik +20 mm.
7. Bir kademe geri.
   Beklenen: derinlik -10 mm.
8. Onayla.
   Beklenen: ortası delik gerçek OCCT B-Rep prizma.
