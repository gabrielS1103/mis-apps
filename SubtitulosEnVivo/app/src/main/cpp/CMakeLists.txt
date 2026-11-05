cmake_minimum_required(VERSION 3.22)
project(subtitulos_voz CXX C)

# whisper.cpp se descarga en la primera compilación (no se guarda la fuente en el repo).
# Versión fijada y verificada con su SHA-256: para actualizar, cambiar ambas líneas juntas.
include(FetchContent)
FetchContent_Declare(
    whisper
    URL      https://github.com/ggml-org/whisper.cpp/archive/refs/tags/v1.9.4.tar.gz
    URL_HASH SHA256=57e280cee375ab02425b806ad5146b99f6eb9357e3c2b31357c8a6af2e2e44ae
)

set(BUILD_SHARED_LIBS      OFF CACHE BOOL "" FORCE)  # whisper y ggml quedan dentro de libvoz.so
set(WHISPER_BUILD_TESTS    OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_SERVER   OFF CACHE BOOL "" FORCE)
set(GGML_NATIVE            OFF CACHE BOOL "" FORCE)  # se compila en la PC para otro procesador
set(GGML_OPENMP            OFF CACHE BOOL "" FORCE)  # ggml usa su propio grupo de hilos; evita empaquetar libomp
# Snapdragon 888 / Exynos 2100 (S21) y casi cualquier teléfono desde 2019: armv8.2 con dotprod y fp16.
set(GGML_CPU_ARM_ARCH "armv8.2-a+dotprod+fp16" CACHE STRING "" FORCE)

FetchContent_MakeAvailable(whisper)

add_library(voz SHARED whisper_jni.cpp)
target_link_libraries(voz PRIVATE whisper log)
target_compile_options(voz PRIVATE -O3)
target_link_options(voz PRIVATE -Wl,--gc-sections -Wl,--exclude-libs,ALL)
