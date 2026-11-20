# SrcCad Mobile Viewer

Android companion viewer for SrcCad. This project is isolated under \`SrcCad.Mobile/\`; the existing Windows WPF/C++/CLI CAD application is not modified.

## Implemented in source

- Android Storage Access Framework file picker for local STEP/STP files
- Copy selected files into app cache before native import
- JNI document handles; B-rep topology remains on the C++ side
- Optional OCCT 8.0.0 STEP reader and B-rep tessellation path
- Bounding-box extraction and triangle/normal buffer transfer
- OpenGL ES 3.0 shaded mesh renderer
- One-finger orbit, two-finger pan/pinch zoom, and fit-to-view control
- Screen-ray selection against the mesh with the selected point projected back to the exact B-rep face
- Two-point distance and ΔX/ΔY/ΔZ measurement in millimetres
- Windows PowerShell script to cross-compile OCCT for Android arm64-v8a
- Gradle 8.13 wrapper

## Important build status

The OCCT importer and exact-point projection are behind the \`SRCCAD_ENABLE_OCCT\` CMake option. Without an Android-built OCCT installation, the app shell can be built but STEP import and point picking return explicit unavailable/error states.

The project source has **not yet been built into an APK or tested on a phone**. Follow [docs/BUILD_ANDROID.md](docs/BUILD_ANDROID.md) to build OCCT and the Android app.

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
