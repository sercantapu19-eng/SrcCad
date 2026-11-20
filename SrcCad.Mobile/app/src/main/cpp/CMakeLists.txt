cmake_minimum_required(VERSION 3.22.1)
project(srccad_mobile_native LANGUAGES CXX)

add_library(srccad_mobile SHARED native-lib.cpp)
target_compile_features(srccad_mobile PRIVATE cxx_std_17)

# OCCT Android libraries will be added here after building OCCT for arm64-v8a.
# Keep the native target buildable as a stub until those binaries are supplied.
