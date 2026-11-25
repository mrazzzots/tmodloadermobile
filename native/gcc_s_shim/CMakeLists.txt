# libmm_gcc_s_shim.so — a stand-in for libgcc_s.so.1 on Android.
#
# MonoMod.Core's arm64 Linux helper (exhelper_linux_arm64.so) has DT_NEEDED libgcc_s.so.1 and
# imports _Unwind_RaiseException / _Unwind_GetLanguageSpecificData / _Unwind_SetIP / _Unwind_SetGR.
# Android has no libgcc_s. We build LLVM libunwind from source with default visibility, give the
# library SONAME libgcc_s.so.1 and preload it, so bionic resolves the dependency by soname.
cmake_minimum_required(VERSION 3.16)
project(mm_gcc_s_shim C CXX ASM)

if(NOT LIBUNWIND_SOURCE_DIR)
  message(FATAL_ERROR "Pass -DLIBUNWIND_SOURCE_DIR=<llvm-project>/libunwind")
endif()

file(GLOB UNWIND_SOURCES
  "${LIBUNWIND_SOURCE_DIR}/src/*.c"
  "${LIBUNWIND_SOURCE_DIR}/src/*.cpp"
  "${LIBUNWIND_SOURCE_DIR}/src/*.S")

add_library(mm_gcc_s_shim SHARED shim.c ${UNWIND_SOURCES})

target_include_directories(mm_gcc_s_shim PRIVATE "${LIBUNWIND_SOURCE_DIR}/include")
target_compile_definitions(mm_gcc_s_shim PRIVATE _LIBUNWIND_IS_NATIVE_ONLY)
target_compile_options(mm_gcc_s_shim PRIVATE
  -fPIC -funwind-tables -fstrict-aliasing
  $<$<COMPILE_LANGUAGE:CXX>:-std=c++17 -fno-exceptions -fno-rtti -nostdinc++>
  $<$<COMPILE_LANGUAGE:C>:-std=c99>)

set(EXPORTS_MAP "${CMAKE_CURRENT_SOURCE_DIR}/exports.map")
target_link_options(mm_gcc_s_shim PRIVATE
  "-Wl,-soname,libgcc_s.so.1"
  "-Wl,--version-script=${EXPORTS_MAP}"
  "-Wl,-z,noexecstack"
  -nostdlib++)

# Do not let the toolchain add its own (hidden) libunwind on top of ours.
include(CheckCXXCompilerFlag)
check_cxx_compiler_flag("--unwindlib=none" HAS_UNWINDLIB_NONE)
if(HAS_UNWINDLIB_NONE AND ANDROID)
  target_link_options(mm_gcc_s_shim PRIVATE "--unwindlib=none")
endif()

find_library(DL_LIB dl)
if(DL_LIB)
  target_link_libraries(mm_gcc_s_shim PRIVATE ${DL_LIB})
endif()

# NO_SONAME: otherwise CMake appends its own -soname after ours.
# LINKER_LANGUAGE CXX + -nostdlib++: link with the C++ driver but without any C++ runtime.
set_target_properties(mm_gcc_s_shim PROPERTIES
  NO_SONAME ON
  LINKER_LANGUAGE CXX
  LINK_DEPENDS "${EXPORTS_MAP}")
