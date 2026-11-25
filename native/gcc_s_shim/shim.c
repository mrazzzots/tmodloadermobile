/*
 * TMLDroid native compat library for MonoMod on Android.
 *
 *  - SONAME libgcc_s.so.1 + LLVM libunwind: satisfies DT_NEEDED of MonoMod's arm64 exhelper.
 *  - __errno_location: glibc name MonoMod.Core P/Invokes ("libc"); bionic calls it __errno.
 *    The host routes MonoMod's "libc" imports to this library; every other libc symbol
 *    (mmap, mprotect, ...) resolves through our DT_NEEDED libc.so.
 */
#include <errno.h>

#define EXPORT __attribute__((visibility("default")))

EXPORT const char *mm_gcc_s_shim_version(void)
{
    return "TMLDroid mm_gcc_s_shim 0.2 (LLVM libunwind, SONAME libgcc_s.so.1, __errno_location)";
}

#ifdef __ANDROID__
EXPORT int *__errno_location(void)
{
    return __errno();
}
#endif
