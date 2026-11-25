#!/usr/bin/env bash
# Builds the native libraries TMLDroid needs for Android arm64-v8a:
#   libSDL2.so, libFNA3D.so, libFAudio.so, libtheorafile.so  (what FNA/tModLoader P/Invoke)
#   libmm_gcc_s_shim.so                                       (libgcc_s stand-in for MonoMod)
#
# Versions are pinned to what tModLoader `stable` uses (see docs/RESEARCH.md).
#
# Usage: ANDROID_NDK_HOME=/path/to/ndk [ABI=arm64-v8a|x86_64] ./native/build-android-natives.sh
# Output: native/out/<abi>/*.so and native/out/java/ (SDL2's Java glue, same SDL version)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$ROOT/src"
ABI="${ABI:-arm64-v8a}"            # arm64-v8a for phones, x86_64 for the CI emulator
BUILD="$ROOT/build/$ABI"
OUT="$ROOT/out/$ABI"
case "$ABI" in
  arm64-v8a) CLANG_TRIPLE=aarch64-linux-android ;;
  x86_64)    CLANG_TRIPLE=x86_64-linux-android ;;
  *) echo "error: unsupported ABI $ABI" >&2; exit 1 ;;
esac
API="${ANDROID_API:-29}"          # Android 10, matches SupportedOSPlatformVersion
JOBS="${JOBS:-$(nproc 2>/dev/null || echo 4)}"

# --- pinned sources -------------------------------------------------------------
FNA_REPO="https://github.com/tModLoader/FNA"
FNA_COMMIT="2d16be6c66ffa9a157ff1bb50ffe215558b40394"   # submodule pin in tModLoader stable
SDL2_REPO="https://github.com/libsdl-org/SDL"
SDL2_TAG="release-2.32.10"
LLVM_REPO="https://github.com/llvm/llvm-project"
LLVM_TAG="llvmorg-18.1.8"                                # libunwind sources for the shim

# --- NDK --------------------------------------------------------------------------
NDK="${ANDROID_NDK_HOME:-${ANDROID_NDK_LATEST_HOME:-${ANDROID_NDK_ROOT:-}}}"
if [[ -z "$NDK" || ! -f "$NDK/build/cmake/android.toolchain.cmake" ]]; then
  echo "error: set ANDROID_NDK_HOME to an Android NDK (r27+ recommended)" >&2
  exit 1
fi
HOST_TAG="$(uname -s | tr '[:upper:]' '[:lower:]')-x86_64"
TOOLCHAIN="$NDK/toolchains/llvm/prebuilt/$HOST_TAG"
CLANG="$TOOLCHAIN/bin/clang"
READELF="$TOOLCHAIN/bin/llvm-readelf"
STRIP="$TOOLCHAIN/bin/llvm-strip"
echo "NDK: $NDK"

CMAKE_ANDROID=(
  -G Ninja
  -DCMAKE_TOOLCHAIN_FILE="$NDK/build/cmake/android.toolchain.cmake"
  -DANDROID_ABI="$ABI"
  -DANDROID_PLATFORM="android-$API"
  -DCMAKE_BUILD_TYPE=Release
  -DBUILD_SHARED_LIBS=ON
  -DCMAKE_PLATFORM_NO_VERSIONED_SONAME=ON   # APKs only carry lib*.so, no .so.0 symlinks
  -DCMAKE_POLICY_VERSION_MINIMUM=3.5        # FNA3D/FAudio still say cmake_minimum_required(2.8.12)
)

mkdir -p "$SRC" "$BUILD" "$OUT"

clone_at() { # repo dir ref [--recursive]
  local repo="$1" dir="$2" ref="$3" rec="${4:-}"
  if [[ ! -d "$dir/.git" ]]; then
    git init -q "$dir"
    git -C "$dir" remote add origin "$repo"
  fi
  git -C "$dir" fetch -q --depth 1 origin "$ref"
  git -C "$dir" checkout -q FETCH_HEAD
  if [[ "$rec" == "--recursive" ]]; then
    git -C "$dir" submodule update -q --init --recursive --depth 1
  fi
}

# --- 1. sources -----------------------------------------------------------------------
echo "==> fetching sources"
clone_at "$FNA_REPO" "$SRC/FNA" "$FNA_COMMIT" --recursive
clone_at "$SDL2_REPO" "$SRC/SDL2" "refs/tags/$SDL2_TAG"
if [[ ! -d "$SRC/llvm/.git" ]]; then
  git clone -q --depth 1 -b "$LLVM_TAG" --filter=blob:none --sparse "$LLVM_REPO" "$SRC/llvm"
  git -C "$SRC/llvm" sparse-checkout set libunwind/src libunwind/include
fi

# --- 2. SDL2 --------------------------------------------------------------------------
echo "==> SDL2 ($SDL2_TAG)"
SDL_PREFIX="$BUILD/sdl2-install"
cmake -S "$SRC/SDL2" -B "$BUILD/sdl2" "${CMAKE_ANDROID[@]}" \
  -DCMAKE_INSTALL_PREFIX="$SDL_PREFIX" \
  -DSDL_SHARED=ON -DSDL_STATIC=OFF -DSDL_TEST=OFF -DSDL2_DISABLE_SDL2MAIN=OFF
cmake --build "$BUILD/sdl2" -j "$JOBS"
cmake --install "$BUILD/sdl2" >/dev/null
SDL_LIB="$(find "$SDL_PREFIX/lib" -maxdepth 1 -name 'libSDL2*.so' ! -name '*main*' | head -n1)"
SDL_INC="$SDL_PREFIX/include/SDL2"
[[ -f "$SDL_LIB" ]] || { echo "error: libSDL2 not found in $SDL_PREFIX/lib" >&2; exit 1; }

SDL_VARS=(-DSDL2_INCLUDE_DIRS="$SDL_INC" -DSDL2_LIBRARIES="$SDL_LIB")

# --- 3. FNA3D (OpenGL ES 3 is auto-selected on Android) ------------------------------
echo "==> FNA3D"
cmake -S "$SRC/FNA/lib/FNA3D" -B "$BUILD/fna3d" "${CMAKE_ANDROID[@]}" "${SDL_VARS[@]}"
cmake --build "$BUILD/fna3d" -j "$JOBS"

# --- 4. FAudio ------------------------------------------------------------------------
echo "==> FAudio"
cmake -S "$SRC/FNA/lib/FAudio" -B "$BUILD/faudio" "${CMAKE_ANDROID[@]}" "${SDL_VARS[@]}"
cmake --build "$BUILD/faudio" -j "$JOBS"

# --- 5. Theorafile (video; optional for Terraria, non-fatal) --------------------------
echo "==> Theorafile"
THEORA_OK=1
mkdir -p "$BUILD/theorafile"
if ! make -C "$BUILD/theorafile" -f "$SRC/FNA/lib/Theorafile/Makefile" -j "$JOBS" \
     CC="$CLANG --target=$CLANG_TRIPLE$API" >/dev/null; then
  echo "warning: Theorafile failed to build (video playback only), continuing" >&2
  THEORA_OK=0
fi

# --- 6. libgcc_s shim for MonoMod -----------------------------------------------------
echo "==> mm_gcc_s_shim ($LLVM_TAG libunwind)"
cmake -S "$ROOT/gcc_s_shim" -B "$BUILD/gcc_s_shim" "${CMAKE_ANDROID[@]}" \
  -DLIBUNWIND_SOURCE_DIR="$SRC/llvm/libunwind"
cmake --build "$BUILD/gcc_s_shim" -j "$JOBS"

# --- 6b. libmain.so: SDL_main -> managed entry point --------------------------------
echo "==> libmain (SDL_main bridge)"
cmake -S "$ROOT/sdl_main_bridge" -B "$BUILD/sdl_main_bridge" "${CMAKE_ANDROID[@]}"
cmake --build "$BUILD/sdl_main_bridge" -j "$JOBS"

# --- 6c. SDL2 Java glue (must match the native SDL2 version exactly) -------------------
rm -rf "$ROOT/out/java"
mkdir -p "$ROOT/out/java"
cp -r "$SRC/SDL2/android-project/app/src/main/java/org" "$ROOT/out/java/"

# --- 7. collect + strip ---------------------------------------------------------------
echo "==> collecting into $OUT"
rm -f "$OUT"/*.so
cp "$SDL_LIB" "$OUT/libSDL2.so"
for so in "$SDL_PREFIX"/lib/libhidapi*.so; do [[ -f "$so" ]] && cp "$so" "$OUT/"; done
cp "$(find "$BUILD/fna3d" -maxdepth 1 -name 'libFNA3D*.so' | head -n1)" "$OUT/libFNA3D.so"
cp "$(find "$BUILD/faudio" -maxdepth 1 -name 'libFAudio*.so' | head -n1)" "$OUT/libFAudio.so"
[[ $THEORA_OK == 1 ]] && cp "$BUILD/theorafile/libtheorafile.so" "$OUT/libtheorafile.so"
cp "$BUILD/gcc_s_shim/libmm_gcc_s_shim.so" "$OUT/libmm_gcc_s_shim.so"
cp "$BUILD/sdl_main_bridge/libmain.so" "$OUT/libmain.so"
for so in "$OUT"/*.so; do "$STRIP" --strip-unneeded "$so"; done

# --- 8. verify: names, sonames and dependencies must be loadable from an APK ---------
echo "==> verifying"
ALLOWED_SYSTEM="libc.so libm.so libdl.so liblog.so libandroid.so libEGL.so libGLESv1_CM.so libGLESv2.so libGLESv3.so libOpenSLES.so libaaudio.so libvulkan.so"
OURS="$(cd "$OUT" && ls *.so | tr '\n' ' ')"
status=0
printf "%-24s %-22s %s\n" "file" "SONAME" "NEEDED"
for so in "$OUT"/*.so; do
  name="$(basename "$so")"
  soname="$("$READELF" -d "$so" | sed -n 's/.*(SONAME).*\[\(.*\)\]/\1/p')"
  needed="$("$READELF" -d "$so" | sed -n 's/.*(NEEDED).*\[\(.*\)\]/\1/p' | tr '\n' ' ')"
  printf "%-24s %-22s %s\n" "$name" "${soname:--}" "$needed"
  for dep in $needed; do
    if [[ " $ALLOWED_SYSTEM $OURS " != *" $dep "* ]]; then
      echo "  ERROR: $name needs $dep, which is neither a system lib nor shipped" >&2
      status=1
    fi
  done
  if [[ "$name" != "libmm_gcc_s_shim.so" && -n "$soname" && "$soname" != "$name" ]]; then
    echo "  ERROR: $name has SONAME $soname (must equal file name inside an APK)" >&2
    status=1
  fi
done

# shim specifics: soname + the four symbols MonoMod's helper imports
shim="$OUT/libmm_gcc_s_shim.so"
[[ "$("$READELF" -d "$shim" | sed -n 's/.*(SONAME).*\[\(.*\)\]/\1/p')" == "libgcc_s.so.1" ]] \
  || { echo "  ERROR: shim SONAME is not libgcc_s.so.1" >&2; status=1; }
for sym in _Unwind_RaiseException _Unwind_GetLanguageSpecificData _Unwind_SetIP _Unwind_SetGR __errno_location; do
  "$READELF" --dyn-syms "$shim" | grep -qE " $sym\$" \
    || { echo "  ERROR: shim does not export $sym" >&2; status=1; }
done

if [[ $status != 0 ]]; then
  echo "verification FAILED" >&2
  exit 1
fi
echo "OK ($ABI): $(ls "$OUT" | tr '\n' ' ')"
echo "Java glue: $(find "$ROOT/out/java" -name '*.java' | wc -l) files"
