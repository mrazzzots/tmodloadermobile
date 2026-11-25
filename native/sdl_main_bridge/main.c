/*
 * libmain.so — SDL's Android entry point, forwarded to managed code.
 *
 * SDLActivity (Java) starts the SDL thread and calls SDL_main() from libmain.so.
 * The .NET side publishes the address of an [UnmanagedCallersOnly] method in the
 * environment variable TMLDROID_MANAGED_MAIN (hex) before the activity starts.
 * Using the environment avoids depending on both loaders (Java and .NET) sharing
 * one copy of this library's globals.
 */
#include <stdint.h>
#include <stdlib.h>
#include <android/log.h>

#define TAG "TMLDroid"
typedef int (*managed_main_t)(int argc, char **argv);

__attribute__((visibility("default"))) int SDL_main(int argc, char **argv)
{
    const char *value = getenv("TMLDROID_MANAGED_MAIN");
    if (value == NULL || value[0] == '\0') {
        __android_log_print(ANDROID_LOG_ERROR, TAG, "SDL_main: TMLDROID_MANAGED_MAIN is not set");
        return 1;
    }
    managed_main_t entry = (managed_main_t)(uintptr_t)strtoull(value, NULL, 16);
    if (entry == NULL) {
        __android_log_print(ANDROID_LOG_ERROR, TAG, "SDL_main: bad TMLDROID_MANAGED_MAIN=%s", value);
        return 1;
    }
    __android_log_print(ANDROID_LOG_INFO, TAG, "SDL_main: entering managed code at %p", (void *)entry);
    int rc = entry(argc, argv);
    __android_log_print(ANDROID_LOG_INFO, TAG, "SDL_main: managed code returned %d", rc);
    return rc;
}
