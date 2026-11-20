# Android build guide

## Target

The first device target is Android arm64-v8a. The app uses:
- Kotlin + Jetpack Compose for the shell and Android document picker
- OpenGL ES 3.0 for mobile rendering
- Open CASCADE 8.0.0 for STEP B-rep import and tessellation
- JNI handles; B-rep objects remain native-side

The native rendering/import pipeline is scaffolded in source. STEP import is compiled only when the Android OCCT installation is supplied.

## 1. Install tools (Windows)

In Android Studio > SDK Manager:
- Android SDK Platform 36
- Android SDK Build-Tools
- Android NDK (Side by side) 28.2.13676358
- CMake
- Android SDK Command-line Tools

Also install Git, CMake and Ninja on PATH (or install CMake/Ninja from Android Studio SDK Manager). Use JDK 17.

## 2. Build OCCT for Android

Open PowerShell in the repository and run:

\`\`\`powershell
cd SrcCad.Mobile
.\scripts\build_occt_android.ps1
\`\`\`

The script checks out upstream OCCT tag V8_0_0, configures an arm64-v8a cross-build, builds only the STEP reader and triangulation toolkits plus dependencies, and installs them under \`SrcCad.Mobile/third_party/occt-android/arm64-v8a\`.

The folder is ignored by Git. Do not commit generated OCCT libraries.

## 3. Build the app

Set the install prefix and open \`SrcCad.Mobile\` as an Android Studio project:

\`\`\`powershell
$env:SRCCAD_OCCT_ROOT = (Resolve-Path .\third_party\occt-android\arm64-v8a).Path
\`\`\`

Then Sync Project with Gradle Files and run the \`:app:assembleDebug\` task. The app module pins NDK r28c to produce 16 KB-aligned native libraries.

If OCCT is not installed, the native shell can still compile with STEP import disabled; the app will report that the STEP reader is unavailable rather than pretending that a model was imported.

## Current functional boundary

Implemented in source:
- SAF file selection and cache copy for local .step/.stp files
- JNI document handle and error/status response
- OCCT STEP reader + B-rep tessellation path behind \`SRCCAD_ENABLE_OCCT\`
- Bounding-box extraction and triangle buffer transfer
- OpenGL ES 3.0 shaded mesh renderer
- One-finger orbit, two-finger pan/pinch and fit view

Not implemented yet:
- Geometry picking and exact B-rep measurements
- IGES, SolidWorks, DWG/DXF readers
- Assembly tree, section view and exploded view

No APK has been built or device-tested in this environment. Run the Android build and device acceptance checks before treating this as a working release.
