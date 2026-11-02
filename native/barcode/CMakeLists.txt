cmake_minimum_required(VERSION 3.24)
project(RitsukageBarcode LANGUAGES C CXX)
include(FetchContent)
set(BUILD_SHARED_LIBS ON CACHE BOOL "" FORCE)
set(ZXING_READERS ON CACHE BOOL "" FORCE)
set(ZXING_WRITERS OFF CACHE STRING "" FORCE)
set(ZXING_C_API ON CACHE BOOL "" FORCE)
set(ZXING_EXAMPLES OFF CACHE BOOL "" FORCE)
set(ZXING_UNIT_TESTS OFF CACHE BOOL "" FORCE)
set(ZXING_BLACKBOX_TESTS OFF CACHE BOOL "" FORCE)
set(ZXING_LINK_CPP_STATICALLY ON CACHE BOOL "" FORCE)
set(CMAKE_INTERPROCEDURAL_OPTIMIZATION ON)
FetchContent_Declare(zxing
    URL https://codeload.github.com/zxing-cpp/zxing-cpp/zip/refs/tags/v3.1.1
    URL_HASH SHA256=9d9884d560baf745f4930307adc07555104ea96f2c66b42bae754de799649980
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
FetchContent_MakeAvailable(zxing)
include("${CMAKE_CURRENT_SOURCE_DIR}/ApplyPatches.cmake")
set_target_properties(ZXing PROPERTIES
    OUTPUT_NAME RitsukageBarcode
    VERSION ""
    SOVERSION ""
    WINDOWS_EXPORT_ALL_SYMBOLS OFF
    RUNTIME_OUTPUT_DIRECTORY "$<1:${CMAKE_BINARY_DIR}/bin>"
    LIBRARY_OUTPUT_DIRECTORY "$<1:${CMAKE_BINARY_DIR}/bin>")
if(WIN32)
    target_sources(ZXing PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/reader.exports.def")
elseif(APPLE)
    target_link_options(ZXing PRIVATE "LINKER:-exported_symbols_list,${CMAKE_CURRENT_SOURCE_DIR}/reader.exports.macos")
else()
    target_link_options(ZXing PRIVATE "LINKER:--version-script=${CMAKE_CURRENT_SOURCE_DIR}/reader.exports.map")
endif()
