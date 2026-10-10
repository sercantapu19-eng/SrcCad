# SrcCad V1.2 — Kurulum

Ön koşullar zaten V1.1 Foundation testinde doğrulandı:

- Visual Studio 2026 Community
- .NET masaüstü geliştirme
- C++ masaüstü geliştirme
- C++/CLI
- x64 MSVC
- OCCT / vcpkg: `D:\SrcCadV11\vcpkg_installed`

## Yapılacak

1. Eski `D:\SrcCadV11\SrcCad` klasörünü istersen yedekle.
2. Bu paketin içeriğini `D:\SrcCadV11\SrcCad` altına çıkar.
3. `BASLAT.bat` çift tıkla.

Visual Studio ile elle açmak istersen:

- `SrcCad.sln`
- `Debug | x64`
- Ctrl+Shift+B
- başlangıç projesi: `SrcCad.App`

## Hata raporu

PowerShell ekranı ve tek tek screenshot yerine:

`COLLECT_DIAGNOSTICS.bat`

çalıştırıp oluşan ZIP'i gönder.
