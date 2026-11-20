# SrcCad Mobile — Device Acceptance

Target device: Samsung Galaxy S22 Ultra (arm64-v8a), Android.

This checklist is intentionally not marked complete until the APK is installed and checked with real files.

## A. Installation and launch

- [ ] Debug APK installs without package parse or signature errors.
- [ ] App opens without a native-library loading crash.
- [ ] Display stays in landscape and the 3D viewport occupies the available area.
- [ ] Airplane mode does not prevent application launch.

## B. STEP import

- [ ] Open a local .step file from Downloads.
- [ ] Open a local .stp file from device storage.
- [ ] Open a file supplied through another Android document provider.
- [ ] File name, imported face count and triangle count appear.
- [ ] Invalid/non-STEP input gives a clear error and does not show a false loaded state.
- [ ] STEP import works with networking disabled.
- [ ] Model remains visible after leaving and returning to the app.

## C. View manipulation

- [ ] One-finger drag orbits the model.
- [ ] Two-finger drag pans the model.
- [ ] Pinch gesture zooms continuously.
- [ ] Fit/Isometric returns the model to the viewport.
- [ ] No unexpected model jumps occur when switching between one- and two-finger gestures.
- [ ] Faces render without missing triangles, reversed large patches or obvious holes.

## D. Measurement

- [ ] Enable the Mesafe Ölç button.
- [ ] Tap a visible planar face to set point 1.
- [ ] Tap a second visible face to set point 2.
- [ ] Distance and ΔX/ΔY/ΔZ appear in millimetres.
- [ ] Repeat the same measurement after rotating the camera; result remains the same.
- [ ] Compare with a known dimension in the source CAD model.
- [ ] Test a STEP file authored in inches or metres and confirm the result is converted to millimetres.
- [ ] Confirm pinch/pan gestures do not create accidental measurement points.

## E. Stability and performance

- [ ] Open a small single-part STEP file.
- [ ] Open a medium multi-body STEP file.
- [ ] Open a larger assembly STEP file.
- [ ] Rotate and zoom continuously for at least one minute.
- [ ] Close/reopen several files in sequence without app crash.
- [ ] Confirm memory returns to a stable level after replacing a loaded model.

## Acceptance notes

Record the source file name, known reference dimension, measured result, Android version and any crash log for each failed item. Do not mark the measurement milestone accepted until the known-dimension tests pass.
