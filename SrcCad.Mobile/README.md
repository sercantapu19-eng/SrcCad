# SrcCad Mobile Viewer

Android companion viewer for SrcCad. This project is intentionally isolated under `SrcCad.Mobile/`; the existing Windows WPF/C++/CLI application is not modified.

## First milestone
1. Open a local STEP/STP file using Android's Storage Access Framework.
2. Load the model through the Android-native OCCT engine.
3. Render and manipulate the model (orbit, pan, zoom).
4. Pick two geometric points and report their model-space distance in the file's units.

## Current status
This commit is a project/bootstrap scaffold. The UI shell and JNI boundary are present; **STEP parsing, OCCT rendering, geometric picking, and measurement are not implemented yet**. No APK is claimed or attached.

## Architecture
- `app/`: Kotlin + Jetpack Compose Android application.
- `app/src/main/cpp/`: JNI boundary for the native geometry engine.
- `native/occt/`: reserved for the Android-built Open CASCADE libraries and headers (not vendored).
- `docs/`: build and acceptance notes.

## Build prerequisites
- Android Studio with Android SDK Platform 36
- Android NDK and CMake (Android arm64-v8a)
- JDK 17
- Open CASCADE built for Android arm64-v8a and linked into the native target

The Windows OCCT C++/CLI bridge cannot be reused on Android because it is tied to Windows HWND/WPF hosting. The native geometry layer must be built for Android with the NDK.

## Planned format policy
Initial implementation: STEP (AP203/AP214/AP242). IGES follows after STEP acceptance. SLDPRT/SLDASM/SLDDRW and DWG are not promised until a compatible licensed reader is selected and validated with real files.
