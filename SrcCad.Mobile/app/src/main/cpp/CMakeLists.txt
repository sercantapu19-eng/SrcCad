cmake_minimum_required(VERSION 3.22.1)
project(srccad_mobile_native LANGUAGES CXX)

option(SRCCAD_ENABLE_OCCT "Build STEP import support against an Android OCCT installation" OFF)
set(SRCCAD_OCCT_ROOT "" CACHE PATH "Prefix containing OCCT include/ and lib/ directories")

add_library(srccad_mobile SHARED native-lib.cpp)
target_compile_features(srccad_mobile PRIVATE cxx_std_17)
target_compile_options(srccad_mobile PRIVATE -fexceptions -frtti -Wall -Wextra)

find_library(ANDROID_LOG_LIB log REQUIRED)
find_library(ANDROID_LIB android REQUIRED)
find_library(GLES_LIB GLESv3 REQUIRED)
target_link_libraries(srccad_mobile PRIVATE ${ANDROID_LOG_LIB} ${ANDROID_LIB} ${GLES_LIB})

# Explicit 16 KiB ELF alignment (also default in Android NDK r28+).
target_link_options(srccad_mobile PRIVATE
    "-Wl,-z,max-page-size=16384"
    "-Wl,-z,common-page-size=16384"
)

if(SRCCAD_ENABLE_OCCT)
    if(NOT SRCCAD_OCCT_ROOT)
        message(FATAL_ERROR "SRCCAD_OCCT_ROOT is required when SRCCAD_ENABLE_OCCT=ON")
    endif()

    # Use the install-tree path directly. CMake's Android toolchain applies
    # sysroot search re-rooting to find_path(); that can hide absolute host-side
    # cross-compiled SDK include directories even though the files exist.
    set(SRCCAD_OCCT_INCLUDE_DIR "${SRCCAD_OCCT_ROOT}/include/opencascade")
    if(NOT EXISTS "${SRCCAD_OCCT_INCLUDE_DIR}/STEPControl_Reader.hxx")
        set(SRCCAD_OCCT_INCLUDE_DIR "${SRCCAD_OCCT_ROOT}/include")
    endif()
    if(NOT EXISTS "${SRCCAD_OCCT_INCLUDE_DIR}/STEPControl_Reader.hxx")
        message(FATAL_ERROR
            "Could not find STEPControl_Reader.hxx in ${SRCCAD_OCCT_ROOT}/include/opencascade or /include")
    endif()

    file(GLOB SRCCAD_OCCT_SHARED_LIBS CONFIGURE_DEPENDS
        "${SRCCAD_OCCT_ROOT}/lib/libTK*.so"
        "${SRCCAD_OCCT_ROOT}/lib64/libTK*.so"
    )
    if(NOT SRCCAD_OCCT_SHARED_LIBS)
        message(FATAL_ERROR "No libTK*.so files found under ${SRCCAD_OCCT_ROOT}/lib or lib64")
    endif()

    set(SRCCAD_OCCT_IMPORTED_TARGETS "")
    foreach(OCCT_LIBRARY_PATH IN LISTS SRCCAD_OCCT_SHARED_LIBS)
        get_filename_component(OCCT_LIBRARY_NAME "${OCCT_LIBRARY_PATH}" NAME_WE)
        string(REGEX REPLACE "^lib" "" OCCT_TARGET_NAME "${OCCT_LIBRARY_NAME}")
        if(NOT TARGET ${OCCT_TARGET_NAME})
            add_library(${OCCT_TARGET_NAME} SHARED IMPORTED GLOBAL)
            set_target_properties(${OCCT_TARGET_NAME} PROPERTIES IMPORTED_LOCATION "${OCCT_LIBRARY_PATH}")
        endif()
        list(APPEND SRCCAD_OCCT_IMPORTED_TARGETS ${OCCT_TARGET_NAME})
    endforeach()

    target_include_directories(srccad_mobile PRIVATE "${SRCCAD_OCCT_INCLUDE_DIR}")
    target_compile_definitions(srccad_mobile PRIVATE SRCCAD_WITH_OCCT=1)
    target_link_libraries(srccad_mobile PRIVATE ${SRCCAD_OCCT_IMPORTED_TARGETS})
    message(STATUS "SrcCad Mobile: OCCT enabled from ${SRCCAD_OCCT_ROOT}")
else()
    message(STATUS "SrcCad Mobile: renderer shell only; STEP import unavailable until OCCT is configured")
endif()
