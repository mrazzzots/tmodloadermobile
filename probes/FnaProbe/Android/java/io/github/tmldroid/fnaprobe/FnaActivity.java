package io.github.tmldroid.fnaprobe;

import org.libsdl.app.SDLActivity;

/**
 * SDL's Android activity. SDL loads libSDL2.so and libmain.so, then runs SDL_main()
 * (native/sdl_main_bridge) on its own thread, which jumps into managed code (Entry.RunGame).
 */
public class FnaActivity extends SDLActivity {
    @Override
    protected String[] getLibraries() {
        return new String[] { "SDL2", "main" };
    }

    // Default is nativeLibraryDir + "/libmain.so", which does not exist when native libs
    // stay inside the APK. A bare name resolves to the copy loaded by getLibraries().
    @Override
    protected String getMainSharedObject() {
        return "libmain.so";
    }

    @Override
    protected String[] getArguments() {
        boolean autorun = getIntent() != null && getIntent().getBooleanExtra("autorun", false);
        return autorun ? new String[] { "--autorun" } : new String[0];
    }
}
