# SolidWorks / OpenSWX Android integration checkpoint

This branch is an integration checkpoint, not a completed or tested APK.

## Changes committed
- Native CMake is switched to C++20 and has an OpenSWX/OpenParasolid/OpenSWStep target.
- Android Kotlin JNI declarations are added for document inspection, embedded preview and explicit selected-body STEP export.
- Gradle C++ compiler flag is switched to C++20.

## Required files still to be added before this can build
1. Vendor the supplied OpenSWX source archive into `SrcCad.Mobile/app/src/main/cpp/third_party/openswx/`. Expected subfolders: `libopenswx`, `libopenparasolid`, `libopenswstep`.
2. Add the JNI implementation as `SrcCad.Mobile/app/src/main/cpp/solidworks_jni_bridge.cpp`.
3. Add the Kotlin configuration / payload / BODY selection UI. The JNI API must refuse implicit first-body selection.
4. Verify OCCT Android shared libraries are present and configured for STEP loading.

## Important parser limitation
OpenSWX's saved-geometry resolver does not fully replay SolidWorks feature/delta history and cannot certify that every BODY candidate is the final displayed body. The app must show configuration, payload, BODY kind and parser limitations, and must not silently choose a candidate. Known rebuild / unsupported-delta states must be rejected.

## Validation
- No Android build was run for this branch.
- No APK was produced.
- The two supplied SLDPRT samples have not yet been runtime-tested on Android.
