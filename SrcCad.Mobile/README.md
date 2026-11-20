# SrcCad Mobile Viewer

Android companion viewer for SrcCad. This project is isolated under `SrcCad.Mobile/`; the existing Windows WPF/C++/CLI CAD application is not modified.

## Implemented in source

- Android Storage Access Framework file picker for local STEP/STP files
- Copy selected files into app cache before native import
- JNI document handles; B-rep topology remains on the C++ side
- Optional OCCT 8.0.0 STEP reader and B-rep tessellation path
- Bounding-box extraction and triangle/normal buffer transfer
- OpenGL ES 3.0 shaded mesh renderer using GPU vertex/index buffers
- Built-in 100 × 50 × 20 mm demo box to exercise the renderer without STEP support
- One-finger orbit, two-finger pan/pinch zoom, and fit-to-view control
- Screen-ray selection against the mesh with the selected point projected back to the exact B-rep face
- Two-point distance and ΔX/ΔY/ΔZ measurement in millimetres on imported STEP B-rep models
- Demo geometry is display-only; measurement stays disabled for the demo
- Windows PowerShell script to cross-compile OCCT for Android arm64-v8a
- Gradle 8.13 wrapper and one-click Windows build script

## Build status

The source implementation is in place, but **the APK has not yet been built or tested on a phone**. The OCCT importer and exact-point projection are enabled only after an Android-built OCCT installation is supplied.

Install the prerequisites in [docs/BUILD_ANDROID.md](docs/BUILD_ANDROID.md), then run `BUILD_ANDROID.bat` from this folder. The script builds OCCT and then the debug APK. A successful build should create:

`app/build/outputs/apk/debug/app-debug.apk`

## First acceptance target

1. Open a local .step/.stp file.
2. Import B-rep geometry and generate a tessellated mesh.
3. Render the model on the OpenGL ES surface.
4. Orbit, pan, zoom, and fit the model.
5. Select two visible surface points and measure their B-rep point-to-point distance.
6. Validate against known dimensions and multiple real STEP files.

## Next work

- Vertex/edge snapping and diameter/radius/angle measurements
- Model units and measurement precision display
- IGES, assembly structure, section analysis
- Evaluate licensed readers for SolidWorks native files and DWG/DXF

Native SolidWorks and DWG support are not promised until a compatible reader is licensed and tested with real files.
