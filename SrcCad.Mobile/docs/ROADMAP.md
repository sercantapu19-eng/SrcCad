# SrcCad Mobile Viewer — Roadmap

## M0 — Android shell
- [x] Isolated Android application module
- [x] Landscape-first viewer shell
- [x] Android document picker entry point
- [x] JNI/CMake native boundary
- [ ] Verify Gradle build in Android Studio
- [ ] Install debug APK on Samsung Galaxy S22 Ultra

## M1 — STEP viewing (first functional milestone)
- [ ] Build Open CASCADE for Android arm64-v8a with the Android NDK
- [ ] Integrate STEPCAFControl / XDE reader for STEP AP203, AP214 and AP242
- [ ] Preserve assembly hierarchy, names and colors where present
- [ ] Add an Android OpenGL ES rendering surface backed by OCCT
- [ ] Fit-all, orbit, pan, zoom and standard views
- [ ] Validate with small, medium and large real STEP files

Acceptance:
1. User selects a local .step/.stp file.
2. File imports without network access.
3. Model is visible and can be orbited, panned and zoomed.
4. Fit-all and isometric view frame the complete model.
5. Import errors are reported clearly; no false success state.

## M2 — Geometric measurement
- [ ] Native point/vertex/edge/face picking
- [ ] Two-point distance in model coordinates
- [ ] Diameter and radius measurement for circular geometry
- [ ] Angle measurement between supported geometric entities
- [ ] Display units and precision from model/import settings

Acceptance:
- Distances are computed from exact model geometry, not screen pixels.
- Results are repeatable across camera zoom levels.
- Measurement is tested against known dimensions in reference STEP files.

## M3 — IGES and section inspection
- [ ] IGES import
- [ ] Section plane controls and clipping
- [ ] Section result validation against reference models

## M4 — Assembly and additional formats
- [ ] Assembly tree, hide/show and isolate
- [ ] STL/OBJ mesh import
- [ ] Evaluate licensed readers for SolidWorks native files and DWG/DXF as needed
- [ ] Do not advertise a format as supported until real-file tests pass

## Engineering constraints
- Keep all Android-specific code inside `SrcCad.Mobile/`.
- Do not alter the existing Windows WPF/C++/CLI viewer or OCCT bridge.
- Do not vendor proprietary CAD readers or binaries without checking redistribution terms.
- Do not publish a release APK until the relevant acceptance checks pass.
