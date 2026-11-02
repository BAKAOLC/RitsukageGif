cmake_minimum_required(VERSION 3.25)
project(RitsukageGifExplorerShell LANGUAGES CXX)

add_library(RitsukageGifExplorerShell SHARED ExplorerCommand.cpp ExplorerShell.def)
set_target_properties(RitsukageGifExplorerShell PROPERTIES
    OUTPUT_NAME "RitsukageGif.ExplorerShell"
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
target_compile_features(RitsukageGifExplorerShell PRIVATE cxx_std_20)
target_compile_definitions(RitsukageGifExplorerShell PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(RitsukageGifExplorerShell PRIVATE /W4 /WX /permissive- /EHsc /guard:cf)
target_link_options(RitsukageGifExplorerShell PRIVATE /DYNAMICBASE /NXCOMPAT /guard:cf)
target_link_libraries(RitsukageGifExplorerShell PRIVATE ole32 shell32 shlwapi advapi32)
