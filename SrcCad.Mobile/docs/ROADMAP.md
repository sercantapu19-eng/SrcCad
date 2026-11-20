# SrcCad Mobile Viewer — Roadmap

## M0 — Android shell and native renderer scaffold
- [x] Isolated Android application module
- [x] Landscape-first Compose shell
- [x] Android document picker and cache copy for STEP/STP
- [x] JNI native document handle API
- [x] OpenGL ES 3.0 shaded mesh renderer
- [x] Orbit, pan, pinch zoom and fit-view interaction
- [x] Optional OCCT CMake integration and 16 KB ELF linker alignment
- [x] Windows PowerShell OCCT Android build script
- [ ] Verify Gradle sync/build in Android Studio
- [ ] Produce and install debug APK on Samsung Galaxy S22 Ultra

## M1 — STEP viewing (first functional milestone)
- [x] STEPControl_Reader import path (compiled when OCCT is supplied)
- [x] B-rep tessellation and bounding-box extraction path
- [ ] Build OCCT for Android arm64-v8a and link the libraries
- [ ] Verify import with small, medium and large real STEP files
- [ ] Verify the rendered mesh and camera interaction on-device

Acceptance:
1. User selects a local .step/.stp file.
2. File imports without network access.
3. Model is visible and can be orbited, panned and zoomed.
4. Fit-view frames the complete model.
5. Import errors are reported clearly; no false success state.
6. Validate geometry against known reference dimensions.

## M2 — Geometric measurement
- [ ] Native face/edge/vertex picking
- [ ] Two-point distance in model coordinates (not screen or mesh-pixel distance)
- [ ] Circular edge/face diameter and radius measurement
- [ ] Angle measurement between supported geometric entities
- [ ] Display source length units and measurement precision

## M3 — IGES and section inspection
- [ ] IGES import
- [ ] Section plane controls and clipping
- [ ] Section result validation against reference models

## M4 — Assembly and additional formats
- [ ] STEP assembly hierarchy, names and colors through XDE
- [ ] Hide/show and isolate components
- [ ] STL/OBJ mesh import
- [ ] Evaluate licensed readers for SolidWorks native files and DWG/DXF
- [ ] Do not advertise a format as supported until real-file tests pass

## Engineering constraints
- Keep Android-specific code inside `SrcCad.Mobile/`.
- Do not alter the existing Windows WPF/C++/CLI viewer or OCCT bridge.
- Do not vendor proprietary CAD readers or binaries without checking redistribution terms.
- Do not publish a release APK until acceptance checks pass.
