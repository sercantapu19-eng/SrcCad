# SolidWorks SLDPRT Android integration status

## Implemented in branch `mobile-multiformat`

- Native CMake enables C++20 and fetches the upstream MIT-licensed OpenSWX repository at configure time, then builds `openparasolid`, `openswx`, and `openswstep`.
- Android Gradle passes C++20 and enables `SRCCAD_ENABLE_OPENSWX`.
- `solidworks_jni_bridge.cpp` exposes document inspection, embedded PNG preview extraction, and explicit configuration/payload/BODY STEP export.
- `NativeCadEngine.kt` declares the matching JNI methods.
- `MainActivity.kt` routes `.SLDPRT` files through an explicit configuration, stored partition candidate, and solid BODY selection flow. The selected body is exported to STEP and passed to the existing OCCT reader.
- `.github/workflows/android-sldprt-build.yml` attempts an arm64-v8a debug APK build.

## Deliberate safety limits

OpenSWX supports modern SolidWorks chunk/stream files (2015+) and rejects legacy OLE2/CFB and ZIP/OPC variants. It reads saved Parasolid geometry but does not replay all SolidWorks feature-history/delta operations and does not prove final-body membership. The app therefore does not auto-select a configuration or body and rejects incomplete/unsupported candidates. A selected stored body may not represent the latest feature-tree state; users must verify results against the source model.

## Validation

- Code changes are committed to `mobile-multiformat`.
- The first CI attempt failed before Gradle/CMake because the Android SDK setup action requested the obsolete `tools` package. The workflow was changed to request only `platform-tools`; a subsequent attempt is pending.
- No successful Android CMake/Gradle build is recorded yet.
- Neither supplied `.SLDPRT` sample has yet passed an Android runtime test.
- No completed APK is available from this integration yet.

Do not treat this branch as a release-ready or verified SolidWorks importer until CI succeeds and both supplied sample files are tested on an Android device.
