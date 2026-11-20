# SrcCad Mobile Viewer

Android companion viewer for SrcCad. This project is isolated under `SrcCad.Mobile/`; the existing Windows WPF/C++/CLI CAD application is not modified.

## Implemented in source

- Android Storage Access Framework file picker for local STEP/STP files
- Copy selected files into app cache before native import
- JNI document handles; B-rep topology remains on the C++ side
- Optional OCCT 8.0.0 STEP reader and B-rep tessellation path
- Bounding-box extraction and triangle/normal buffer transfer
- OpenGL ES 3.0 shaded mesh renderer
- One-finger orbit, two-finger pan/pinch zoom, and fit-to-view control
- Windows PowerShell script for cross-compiling OCCT for Android arm64-v8a

## Important build status

The OCCT importer is behind the `SRCCAD_ENABLE_OCCT` CMake option. Without an Android-built OCCT installation the Android shell can still be built, but STEP import returns an explicit "reader not linked" message. The project has **not yet been built into an APK or tested on a phone**.

Follow [docs/BUILD_ANDROID.md](docs/BUILD_ANDROID.md) to build OCCT and the Android app.

## First acceptance target

1. Open a local .step/.stp file.
2. Import B-rep geometry and generate a tessellated mesh.
3. Render the model on the OpenGL ES surface.
4. Orbit, pan, zoom, and fit the model.
5. Validate with real STEP test files before marking this milestone complete.

## Next work

- Geometric picking and exact B-rep measurements
- Model units and circular edge/face measurements
- IGES, assembly structure, section analysis
- Evaluate licensed readers for SolidWorks native files and DWG/DXF

Native SolidWorks and DWG support are not promised until a compatible reader is licensed and tested with real files.
