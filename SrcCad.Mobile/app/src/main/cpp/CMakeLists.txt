cmake_minimum_required(VERSION 3.22.1)
project(srccad_mobile_native LANGUAGES CXX)

option(SRCCAD_ENABLE_OCCT "Build STEP import support against an Android OCCT installation" OFF)
option(SRCCAD_ENABLE_OPENSWX "Build open-source SolidWorks reader and STEP exporter" ON)
set(SRCCAD_OCCT_ROOT "" CACHE PATH "Prefix containing OCCT include/ and lib/ directories")

add_library(srccad_mobile SHARED native-lib.cpp)
target_compile_features(srccad_mobile PRIVATE cxx_std_20)
target_compile_options(srccad_mobile PRIVATE -fexceptions -frtti -Wall -Wextra)

find_library(ANDROID_LOG_LIB log REQUIRED)
find_library(ANDROID_LIB android REQUIRED)
find_library(GLES_LIB GLESv3 REQUIRED)
target_link_libraries(srccad_mobile PRIVATE ${ANDROID_LOG_LIB} ${ANDROID_LIB} ${GLES_LIB})

target_link_options(srccad_mobile PRIVATE
    "-Wl,-z,max-page-size=16384"
    "-Wl,-z,common-page-size=16384"
)

if(SRCCAD_ENABLE_OPENSWX)
    include(FetchContent)

    # NDK provides zlib; expose it as the imported target expected by OpenSWX.
    find_library(SRCCAD_ZLIB_LIBRARY z REQUIRED)
    if(NOT TARGET ZLIB::ZLIB)
        add_library(ZLIB::ZLIB UNKNOWN IMPORTED)
        set_target_properties(ZLIB::ZLIB PROPERTIES IMPORTED_LOCATION "${SRCCAD_ZLIB_LIBRARY}")
    endif()

    set(PUGIXML_BUILD_TESTS OFF CACHE BOOL "" FORCE)
    set(PUGIXML_BUILD_SHARED_AND_STATIC_LIBS OFF CACHE BOOL "" FORCE)
    FetchContent_Declare(pugixml
        GIT_REPOSITORY https://github.com/zeux/pugixml.git
        GIT_TAG v1.15
        GIT_SHALLOW TRUE)
    FetchContent_MakeAvailable(pugixml)

    # Fetch the upstream source snapshot; no binary SDK or SolidWorks runtime is needed.
    FetchContent_Declare(openswx
        GIT_REPOSITORY https://github.com/schwitters/openswx.git
        GIT_TAG main
        GIT_SHALLOW TRUE)
    FetchContent_GetProperties(openswx)
    if(NOT openswx_POPULATED)
        FetchContent_Populate(openswx)
    endif()

    set(OPENSWX_ROOT "${openswx_SOURCE_DIR}")
    foreach(module libopenparasolid libopenswx libopenswstep)
        if(NOT EXISTS "${OPENSWX_ROOT}/${module}/CMakeLists.txt")
            message(FATAL_ERROR "OpenSWX source module missing: ${OPENSWX_ROOT}/${module}")
        endif()
    endforeach()

    # Upstream currently requires a package config even when the parent already
    # created pugixml::pugixml via FetchContent. Make that lookup conditional.
    set(OPENSWX_CMAKE "${OPENSWX_ROOT}/libopenswx/CMakeLists.txt")
    file(READ "${OPENSWX_CMAKE}" OPENSWX_CMAKE_TEXT)
    string(REPLACE
        "find_package(pugixml CONFIG REQUIRED)"
        "if(NOT TARGET pugixml::pugixml)\n  find_package(pugixml CONFIG REQUIRED)\nendif()"
        OPENSWX_CMAKE_TEXT "${OPENSWX_CMAKE_TEXT}")
    file(WRITE "\${OPENSWX_CMAKE}" "\${OPENSWX_CMAKE_TEXT}")

    # Android NDK libc++ does not expose floating-point std::from_chars on all
    # supported toolchain revisions. Replace the two double parsers in OpenSWX
    # with checked strtod conversions so the native reader also builds on Android.
    set(OPENSWX_XML_READERS "\${OPENSWX_ROOT}/libopenswx/src/internal/xml_readers.cc")
    file(READ "\${OPENSWX_XML_READERS}" OPENSWX_XML_READERS_TEXT)
    set(OPENSWX_MASS_FROM_CHARS [=[
auto [ptr, ec] = std::from_chars(token.data(), token.data() + token.size(),
                                     v, std::chars_format::general);
    if (ec != std::errc{}) return std::nullopt;
]=])
    set(OPENSWX_MASS_STRTOD [=[
std::string token_storage(token);
    char* parse_end = nullptr;
    errno = 0;
    v = std::strtod(token_storage.c_str(), &parse_end);
    if (errno == ERANGE || parse_end == token_storage.c_str())
      return std::nullopt;
]=])
    set(OPENSWX_TRANSFORM_FROM_CHARS [=[
const auto [ptr, ec] = std::from_chars(
        token.data(), token.data() + token.size(), values[idx],
        std::chars_format::general);
    if (ec != std::errc{} || ptr != token.data() + token.size())
      return std::nullopt;
]=])
    set(OPENSWX_TRANSFORM_STRTOD [=[
std::string token_storage(token);
    char* parse_end = nullptr;
    errno = 0;
    values[idx] = std::strtod(token_storage.c_str(), &parse_end);
    if (errno == ERANGE || parse_end != token_storage.c_str() + token_storage.size())
      return std::nullopt;
]=])
    string(FIND "\${OPENSWX_XML_READERS_TEXT}" "\${OPENSWX_MASS_FROM_CHARS}" OPENSWX_MASS_PATTERN_INDEX)
    string(FIND "\${OPENSWX_XML_READERS_TEXT}" "\${OPENSWX_TRANSFORM_FROM_CHARS}" OPENSWX_TRANSFORM_PATTERN_INDEX)
    if(OPENSWX_MASS_PATTERN_INDEX EQUAL -1 OR OPENSWX_TRANSFORM_PATTERN_INDEX EQUAL -1)
        message(FATAL_ERROR "OpenSWX xml_readers.cc floating-point parser changed; Android compatibility patch must be reviewed")
    endif()
    string(REPLACE "\${OPENSWX_MASS_FROM_CHARS}" "\${OPENSWX_MASS_STRTOD}" OPENSWX_XML_READERS_TEXT "\${OPENSWX_XML_READERS_TEXT}")
    string(REPLACE "\${OPENSWX_TRANSFORM_FROM_CHARS}" "\${OPENSWX_TRANSFORM_STRTOD}" OPENSWX_XML_READERS_TEXT "\${OPENSWX_XML_READERS_TEXT}")
    string(REPLACE "#include <charconv>" "#include <cerrno>\n#include <cstdlib>" OPENSWX_XML_READERS_TEXT "\${OPENSWX_XML_READERS_TEXT}")
    file(WRITE "\${OPENSWX_XML_READERS}" "\${OPENSWX_XML_READERS_TEXT}")

    set(BUILD_TESTING OFF CACHE BOOL "" FORCE)
    set(OPENPARASOLID_BUILD_TOOLS OFF CACHE BOOL "" FORCE)
    add_subdirectory("${OPENSWX_ROOT}/libopenparasolid" "${CMAKE_CURRENT_BINARY_DIR}/openparasolid")
    add_subdirectory("${OPENSWX_ROOT}/libopenswx" "${CMAKE_CURRENT_BINARY_DIR}/openswx")
    add_subdirectory("${OPENSWX_ROOT}/libopenswstep" "${CMAKE_CURRENT_BINARY_DIR}/openswstep")

    target_sources(srccad_mobile PRIVATE solidworks_jni_bridge.cpp)
    target_link_libraries(srccad_mobile PRIVATE openswstep openswx openparasolid)
    target_compile_definitions(srccad_mobile PRIVATE SRCCAD_WITH_OPENSWX=1)
    message(STATUS "SrcCad Mobile: OpenSWX SolidWorks import enabled")
endif()

if(SRCCAD_ENABLE_OCCT)
    if(NOT SRCCAD_OCCT_ROOT)
        message(FATAL_ERROR "SRCCAD_OCCT_ROOT is required when SRCCAD_ENABLE_OCCT=ON")
    endif()
    set(SRCCAD_OCCT_INCLUDE_DIR "${SRCCAD_OCCT_ROOT}/include/opencascade")
    if(NOT EXISTS "${SRCCAD_OCCT_INCLUDE_DIR}/STEPControl_Reader.hxx")
        set(SRCCAD_OCCT_INCLUDE_DIR "${SRCCAD_OCCT_ROOT}/include")
    endif()
    if(NOT EXISTS "${SRCCAD_OCCT_INCLUDE_DIR}/STEPControl_Reader.hxx")
        message(FATAL_ERROR "Could not find STEPControl_Reader.hxx under ${SRCCAD_OCCT_ROOT}/include")
    endif()
    file(GLOB SRCCAD_OCCT_SHARED_LIBS CONFIGURE_DEPENDS
        "${SRCCAD_OCCT_ROOT}/lib/libTK*.so"
        "${SRCCAD_OCCT_ROOT}/lib64/libTK*.so")
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
endif()
