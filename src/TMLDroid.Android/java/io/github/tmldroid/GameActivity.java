package io.github.tmldroid;

import org.libsdl.app.SDLActivity;

/**
 * Hosts tModLoader. SDL loads libSDL2.so + libmain.so and calls SDL_main on its thread,
 * which jumps to TMLDroid.GameEntry.RunGame (published by TMLDroid.App).
 */
public class GameActivity extends SDLActivity {
    @Override
    protected String[] getLibraries() {
        return new String[] { "SDL2", "main" };
    }

    @Override
    protected String getMainSharedObject() {
        return "libmain.so";
    }

    @Override
    protected String[] getArguments() {
        return new String[0];
    }
}
