# Android build guide

## One-click Windows build

After installing the prerequisites below, open this folder in Windows Explorer and run `BUILD_ANDROID.bat`. It builds OCCT for Android ARM64 and then assembles the debug APK. You can still run the OCCT and Gradle steps separately when troubleshooting.

## Target

The first device target is Android arm64-v8a. The app uses:
- Kotlin + Jetpack Compose for the shell and Android document picker
- OpenGL ES 3.0 for mobile rendering
- Open CASCADE 8.0.0 for STEP B-rep import and tessellation
- JNI handles; B-rep objects remain native-side

The source includes a STEP importer, mesh renderer, and a first two-point measurement path. It has not yet been built or validated on a physical Android device.

## 1. Install tools (Windows)

In Android Studio > SDK Manager:
- Android SDK Platform 36
- Android SDK Build-Tools
- Android NDK (Side by side) 28.2.13676358
- CMake
- Android SDK Command-line Tools

Use JDK 17. Git, CMake and Ninja must be available on PATH (or install CMake/Ninja from Android Studio SDK Manager).

## 2. Build OCCT for Android

Open PowerShell in the repository and run:

\`\`\`powershell
cd SrcCad.Mobile
.\scripts\build_occt_android.ps1
\`\`\`

The script checks out upstream OCCT tag V8_0_0, configures an arm64-v8a cross-build and installs the selected STEP, mesh and topological-algorithm toolkits and their dependencies under:

\`SrcCad.Mobile/third_party/occt-android/arm64-v8a\`

The source, build and install directories are ignored by Git. Do not commit generated OCCT libraries.

The script validates that \`STEPControl_Reader.hxx\` and \`libTKDESTEP.so\` were installed. If configuration fails because an OCCT CMake option/toolkit changed, keep the failure log; do not silently disable STEP support.

## 3. Build the Android app

Open \`SrcCad.Mobile\` as the Android Studio project and run Gradle sync. The project includes a Gradle 8.13 wrapper; on Windows you can also build from PowerShell:

\`\`\`powershell
.\gradlew.bat :app:assembleDebug
\`\`\`

The app uses Android Gradle Plugin 8.11.1 with Kotlin 2.2.20 and Gradle 8.13. The app uses Android Gradle Plugin 8.11.1 with Kotlin 2.2.20 and Gradle 8.13. The app module pins Android NDK r28c. When the OCCT install path is the default location above, Gradle detects it automatically. To use a different location, set:

\`\`\`powershell
$env:SRCCAD_OCCT_ROOT = "D:\path\to\occt-android\arm64-v8a"
.\gradlew.bat :app:assembleDebug
\`\`\`

Debug APK output, after a successful build:

\`app/build/outputs/apk/debug/app-debug.apk\`

## 4. Current functional boundary

Implemented in source:
- SAF selection and cache copy for local .step/.stp files
- Native STEPControl_Reader import path when OCCT is built and linked
- B-rep tessellation and bounding-box extraction
- OpenGL ES 3.0 shaded triangle renderer
- One-finger orbit, two-finger pan/pinch and fit view
- Screen-ray picking against the mesh followed by B-rep point projection to the hit face
- Two-point Euclidean distance and ΔX/ΔY/ΔZ calculation in OCCT millimetres

Not implemented or not yet validated:
- Vertex/edge snapping and diameter/radius/angle measurements
- IGES, SolidWorks, DWG/DXF readers
- Assembly hierarchy and section analysis
- Performance on large STEP assemblies
- APK build/install and real-device acceptance

The screen ray identifies the hit face using its triangulation, then the picked point is projected to the exact trimmed B-rep face with OCCT. The distance is calculated from those B-rep points, not from screen pixels. Accuracy and picking behavior still require validation with known-size test files.

No APK has been built or device-tested in this development environment. Run the build and device acceptance checks before treating this as a working release.
