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

/*
 * On-screen keyboard toggle for the overlay button in GameActivity.
 * Android SDL keeps text input off until SDL_StartTextInput(), and Terraria (written for
 * desktops, where text input is always on) never asks for it. SDL is looked up at run time
 * so libmain.so does not need SDL at link time.
 */
#include <jni.h>
#include <dlfcn.h>

typedef int (*sdl_bool_fn)(void);
typedef void (*sdl_void_fn)(void);
typedef unsigned int (*sdl_wasinit_fn)(unsigned int);

static void *sdl_handle(void)
{
    static void *handle;
    if (handle == NULL) handle = dlopen("libSDL2.so", RTLD_NOW | RTLD_NOLOAD);
    if (handle == NULL) handle = dlopen("libSDL2.so", RTLD_NOW);
    return handle;
}

JNIEXPORT jint JNICALL Java_io_github_tmldroid_GameActivity_nativeToggleKeyboard(JNIEnv *env, jclass cls)
{
    (void)env; (void)cls;
    void *h = sdl_handle();
    if (h == NULL) return -1;
    sdl_wasinit_fn was_init = (sdl_wasinit_fn)dlsym(h, "SDL_WasInit");
    sdl_bool_fn is_active = (sdl_bool_fn)dlsym(h, "SDL_IsTextInputActive");
    sdl_void_fn start = (sdl_void_fn)dlsym(h, "SDL_StartTextInput");
    sdl_void_fn stop = (sdl_void_fn)dlsym(h, "SDL_StopTextInput");
    if (!was_init || !is_active || !start || !stop) return -1;
    if (was_init(0x00000020u /* SDL_INIT_VIDEO */) == 0) return -1;
    if (is_active()) { stop(); return 0; }
    start();
    return 1;
}
