# Issue #3 öncesi baseline doğrulama kapısı

Tarih: 2026-10-11 (Europe/Istanbul).
Test edilen kaynak commit: `b4aefc9e5c7dd2263911c6345f771dfa47362432`.
Branch: `dev/smart-dimension-core`.

**Sonuç: WINDOWS DOĞRULAMASI ENGELLİ. Doğrulanmış Windows baseline değildir.**

Bu turda üretim kodu değiştirilmedi. Issue #3'ün 29 bölümünün tamamı okundu. Kullanıcının “önce Windows kabulü, sonra Issue #3” sıralaması nedeniyle yeni geliştirme fazı başlatılmadı; CAD-001…CAD-200 matrisi henüz oluşturulmadı.

## Yeniden çalıştırılan kontroller

| Paket | PASS | FAIL | Ortam / kanıt |
|---|---:|---:|---|
| Core acceptance | 60 | 0 | Gerçek Core/Commands kaynakları, .NET 8 / Linux |
| Feature History | 12 | 0 | Managed state transition kontrolleri |
| Interaction Regression | 50 | 0 | Model koordinatları ve wheel hesapları; Windows event routing değildir |
| Source Sketch Edit | 18 | 0 | Managed transaction contract; native kernel doğrulaması değildir |
| Deployment infrastructure | 8 | 0 | PowerShell dosya fixture kontrolleri; DLL yükleme veya B-Rep testi değildir |

Managed toplam: **140 PASS / 0 FAIL**. Deployment ayrıca **8 PASS / 0 FAIL**.
Mevcut smoke paketi de PASS; yukarıdaki toplam yalnız sayılı kontrollerdir.

Kanıtlar:
- `baseline-before-issue3-managed.txt`
- `baseline-before-issue3-deployment.txt`
- `baseline-before-issue3-runner.txt`

## Windows / OCCT durumu

Erişilebilir terminal Linux x86_64'tür. Bu oturumda Windows masaüstü veya Windows terminal bağlantısı yoktur. GitHub Actions runs sorgusu sıfır koşu döndürdü; workflow listeleme connector tarafından desteklenmeyen endpoint olarak reddedildi. Bu bulgular Windows runner'ın varlığını veya çalışabilirliğini doğrulamaz.

`Run-SolidAcceptance.ps1` erişilebilir Linux PowerShell üzerinde de denendi. Exit code 1: `vcpkg_installed/x64-windows` bulunamadığından `Resolve-Path` aşamasında durdu. Derleme ve test executable'ı başlamadı. Bu bir Windows test koşusu veya CAD testinde geometri hatası değildir.

| Native paket | Bu tur çalıştırılan | Durum |
|---|---:|---|
| SM-061…072 mevcut gerçek B-Rep | 0/12 | BLOCKED; önceki 12/12 kullanıcı bildirimi tarihsel kanıttır |
| Yeni source-sketch hacim regresyonları | 0/12 | BLOCKED; hiç PASS iddiası yok |
| Windows mouse/klavye ve PropertyManager akışları | 0 | BLOCKED |

Bu tur native PASS/FAIL sayısı **0/0, 24 çalıştırılamadı**. Çalıştırılmayan testler PASS sayılmaz.

## Issue #3 rapor formatı

1. **Gerçekten tamamlanan profesyonel CAD kabiliyetleri:** Bu tur yeni kabiliyet yok; mevcut managed baseline yeniden doğrulandı.
2. **Değiştirilen dosya/mimari:** Yalnız bu doğrulama kaydı ve ham test çıktıları. Üretim mimarisi değişmedi.
3. **Yeni otomatik testler:** Yok; mevcut testler tekrar çalıştırıldı.
4. **Core toplamı:** 60/60 PASS, 0 FAIL; ayrıca 50 interaction ve 18 source-sketch kontrolü PASS.
5. **Feature History toplamı:** 12/12 PASS, 0 FAIL.
6. **Gerçek OCCT B-Rep toplamı:** Bu tur 0 çalıştırıldı; 24 BLOCKED. Önceki kullanıcı bildirimi yeni koşu yerine geçirilmedi.
7. **Kapanan manuel bug'lar:** Yok. Merkez ilişkileri, merkez/Origin/kenar ölçüleri, viewport clipping ve wheel davranışı Windows'ta doğrulanmadı.
8. **Kısmi/açık maddeler:** Önceki eksen ölçüsü ve tek ekstrüzyonun kaynak çizim düzenleme uygulamaları managed kapsamda geçiyor; Windows UI/native doğrulaması bekliyor. Issue #3 ve SM-073…120 tamamlanmadı.
9. **En büyük beş mimari açık:** (a) Ortak semantik Geometry Reference/Selection/Inference modeli, (b) genel constraint solver ve gerçek rank/DOF/conflict tanısı, (c) document transaction + çok-feature dependency/dirty/rebuild, (d) persistent topolojik referanslar ve çok seviyeli region analizi, (e) parametrik .scpart save/open round-trip ve bunların native/UI kanıtı.
10. **Sonraki en yüksek değerli grup:** Önce mevcut commit üzerinde Windows baseline kapısı. Ardından Issue #3 Faz A: reference + selection + solver + Smart Dimension; genel semantik üzerinden merkez/Origin/point-line regresyonları.

## Eksik yürütme ortamı ve devam koşulu

Windows x64, repo ile uyumlu Visual Studio C++/CLI toolset'i, .NET 8 ve OCCT kurulumu bulunan erişilebilir bir Windows yürütücüsü gerekir. Mouse/klavye kabulü ayrıca etkileşimli Windows masaüstü gerektirir; headless native test PASS sonucu bunun yerine geçmez.

Mevcut Windows ortamında Debug ve Release için:

```powershell
.\tools\Run-SolidAcceptance.ps1 -Configuration Debug
.\tools\Run-SolidAcceptance.ps1 -Configuration Release
```

OCCT yolu varsayılandan farklıysa `-OcctRoot` parametresi verilmelidir. Script logları ikinci koşuda yeniden yazar; her koşunun `logs/solid-*` dosyaları configuration ve commit bilgisiyle ayrı saklanmalıdır. Yeni native paket için `NATIVE SOURCE SKETCH REGRESSION: 12/12 PASS` satırı zorunludur.

Windows manuel akışlarının adımları `docs/SOLID_ACCEPTANCE_120.md` içindedir. Testleri uzaktan çalıştırabileceğim erişim sağlanmadığı sürece kullanıcıdan bu loglar ve manuel sonuçlar gerekir. Bu kayıt çalışma izni talebi değil, eksik yürütme kabiliyetinin açıklamasıdır.
