# SolidWorks / OpenSWX Android integration (in progress)

This branch now contains the first native integration layer:

- `app/src/main/cpp/solidworks_jni_bridge.cpp`: JNI entry points for document inspection, embedded preview extraction, and explicit configuration + payload + solid BODY STEP export.
- `NativeCadEngine.kt`: matching JNI declarations.
- `app/src/main/cpp/CMakeLists.txt`: C++20 and an opt-in `SRCCAD_ENABLE_OPENSWX` target definition.

## Important: not yet build-enabled

`SRCCAD_ENABLE_OPENSWX` intentionally defaults to OFF. The upstream OpenSWX source tree has **not yet been committed** under `app/src/main/cpp/third_party/openswx`, and the Android app import/picker flow has not yet been wired to call these methods. Turning the option on before adding those sources will fail CMake configuration.

The bridge refuses ambiguous exports: caller must pass configuration name, payload index and solid BODY id. It rejects incomplete/unselected partitions, non-solid bodies, rebuild-required configurations and unsupported delta/history state. OpenSWX's saved-geometry limitations mean this is not a guarantee that every SolidWorks file can be reconstructed as the final feature-tree state.

## Validation

No Android Gradle build, NDK/CMake build, APK, or real `.SLDPRT` import test is claimed by this commit. Next required work: vendor the OpenSWX source/dependencies, add the Kotlin selection/import flow and connect exported STEP to the existing OCCT loader; then build and test on Android.
