# Issue #3 — 2026-10-11 çalışma raporu

**Issue #3 ve Katı Model tamamlanmadı.** Windows baseline kullanıcı tarafından doğrulandı; geliştirme devam etti. Yeni kaynakta Windows/native sonuçları PENDING olarak tutuldu. Sac Levha, Profil, Montaj veya Teknik Resim modülleri geliştirilmedi.

1. **Eklenen gerçek CAD davranışı:** point-like Horizontal/Vertical/Coincident; Origin/center/endpoint ölçüleri; point-line dik mesafe; Midpoint; line-circle/circle-circle Tangent; ölçü değiştikten sonra ilişkilerin korunması. Paylaşılan model ve constraint residual'ları kullanılır.
2. **Ana mimari:** GeometryReference + ortak HitReference + SmartDimensionSession; model tabanlı dimension inference; numerical residual/Jacobian solver; rank tabanlı yerel DOF; semantik referanslar snapshot/dosyada saklanır. Legacy API'ler kademeli uyumluluk katmanı olarak duruyor.
3. **Sketch araçları:** Centerline/Construction Line, 3 Nokta Dairesi, 3 Noktalı Köşe Dikdörtgen. Sonuncusu döndürülmüş gerçek kapalı profil ve paralellik/diklik ilişkileri oluşturur. Üç nokta dairesi yakalanan mevcut noktalara Coincident kısıtı kurar. Geçersiz üçüncü nokta modelde artık bırakmaz.
4. **Solver:** damped least-squares; tüm desteklenen kısıtlar residual sistemine katılır; fixed parametreler bilinmeyenlerden çıkarılır; çakışmada snapshot rollback. Tangent çözüm dalı korunur. Bu henüz tüm profesyonel constraint kombinasyonlarını veya singularity tanısını çözmüş bir solver değildir.
5. **Smart Dimension:** UI preview ve commit aynı inference motorunu kullanır. Point/center/Origin çiftleri; tek line/circle; iki sırada point-like/line; oblique line dik mesafesi. Sıfır X/Y ölçüsü sonradan pozitif değere sürülebilir. Parallel-line distance ve arc/radius kapsamı açık.
6. **Fully Defined/DOF:** Fix olmadan dikdörtgen+iki daire modeli üç düzlemde0 DOF; bir ölçü kaldırıldığında1 DOF testi geçiyor. Bağımlı denklem sayısı raporlanır. Yerel Jacobian rank, global tekil çözüm sertifikası değildir; constraint düzeyinde redundant/conflict provenance açık.
7. **History/dependency:** mevcut tek kaynak sketch→extrusion edit, snapshot ve transactional history korunuyor. Genel çok-feature graph/revision/dirty propagation bu tur uygulanmadı.
8. **OCCT:** üretim bridge builder'ına yönelik21 yeni native hacim kontrolü eklendi:12 parametrik save/open/edit/restore,9 döndürülmüş rectangle/3point circle hole. Bu tur native çalıştırılmadı. C++ bridge değiştirilmedi.
9. **CommandManager/UI:** özgün WPF vektör ikonları; Çizgi/Centerline, Dikdörtgen varyantları ve Daire varyantları açılır ailelerde. Origin/center ilişkileri ve dimension akışı ortak API'ye bağlı. Windows görsel ve olay testi PENDING; desteklenmeyen Arc/Slot/Fillet vb. aktif sahte buton eklenmedi.
10. **Yeni managed testler:**102 foundation +33 persistence +51 genel constraint +39 üç nokta araçları +2 ölçek hit-test senaryosu =227. Önceki140 korunur. Smoke paketi ayrıca PASS; numaralı toplama eklenmez.
11. **Core:**60/60 PASS,0 FAIL.
12. **Feature History:**12/12 PASS,0 FAIL.
13. **Managed regresyon:**295 PASS,0 FAIL; Core/History dahil toplam367 PASS. Deployment dosya kontrolleri ayrıca8/8 PASS. Son çıktı `validation/phase-b-tools-managed.txt`.
14. **Gerçek OCCT:** önceki24 kullanıcı Windows baseline PASS. Güncel suite45 kontrol içerir; bu değişen kaynakta45/45 PENDING,0 çalıştırıldı. Eski PASS yeni koda aktarılmaz.
15. **Windows manuel tekrar:** iki center Ctrl-seç→Yatay/Dikey→sonradan ölçü edit; Origin→corner/center X/Y (0 dahil); center→yatay/dikey/oblique line iki seçim sırası; Fix olmadan rectangle+iki circle Fully Defined; Centerline profile dışlama; Midpoint/Tangent edit;3point araçlarda preview/Esc/yanlış üçüncü nokta; `.scpart` aç→ölçü edit→Extrude rebuild; Ctrl+Z/Y; ikonlu aileler; Extrude wheel. `Run-SolidAcceptance.ps1` Debug ve Release, gerektiğinde `-OcctRoot` ile. Yeni native summary'ler zorunludur.
16. **Kısmi/açık:** manuel bug kapandı iddiası yok. Viewport kenarında circle/arc kaybolması henüz düzeltilmedi. Wheel eski uygulaması korunuyor, Windows tekrar gerekiyor. Save/open schema1 tek sketch+opsiyonel extrusion'u kapsar; çok-feature history değil. Midpoint seçim glyph'i ve kapsamlı inference/cycling açık. UI save/open komutları düzenleme kapalıyken çalışır; belge dirty/unsaved close yönetimi henüz yok.
17. **En büyük5 açık:** (a) solver singularity/ölçeklenebilirlik ve tam constraint kapsamı, (b) genel document transaction+dependency/dirty/rebuild, (c) persistent OCCT face/edge naming ve face-attached sketch, (d) nested region seçimi ve gerçek parametrik Cut/termination, (e) geniş sketch araçları, viewport clipping ve eksiksiz Windows kabulü. Bu alanlarla birlikte çok-feature .scpart şeması genişletilmeli.
18. **Branch:** `dev/smart-dimension-core`.
19. **Checkpoint'ler:** `0c7f4fd` foundation, `8d7eb9a` parametrik dosya ve ikonlar, `83a4892` genel ilişkiler/CAD matrisi. Bu raporu içeren sonraki commit üç nokta araçlarını ve son test kanıtını içerir; kesin SHA sohbet tesliminde verilir.
20. **Sonraki yüksek değerli grup:** kalan Faz A seçim/inference ve solver teşhisi; ardından ortak document revision/transaction ve çok-feature dependency rebuild; topology/region çalışmaları bunun üzerine kurulmalı. Arc/Slot/Cut gibi araçlar gerçek model desteği tamamlandıkça CommandManager'a eklenecek.

## CAD matrisi

`CAD_ACCEPTANCE_200.md`:200 benzersiz sıralı madde;36 PASS-MANAGED,56 PARTIAL,9 PENDING-WINDOWS,99 TODO. Bunlar assertion sayıları değildir. PASS-MANAGED yalnız belirtilen model alt gereksinimi içindir; tüm özellik Definition of Done onayı değildir.

## Ölçek gözlemi

Bu Linux host'unda200 body hit sorgusu100 entity için yaklaşık3 ms,500 entity için yaklaşık15 ms. Son koşunun kesin değerleri ham logdadır. Bu ölçüm solver ölçeklenebilirliği veya Windows UI latency garantisi değildir.500 entity solver ve50+ feature graph ölçümleri hâlâ açık.
