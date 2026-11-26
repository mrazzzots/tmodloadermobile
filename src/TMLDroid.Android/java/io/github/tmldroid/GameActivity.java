package io.github.tmldroid;

import android.os.Bundle;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.ViewGroup;

import org.libsdl.app.SDLActivity;

/**
 * Hosts tModLoader. SDL loads libSDL2.so + libmain.so and calls SDL_main on its thread,
 * which jumps to TMLDroid.GameEntry.RunGame (published by TMLDroid.App).
 * On top of SDL's surface sits {@link TouchControls} (joystick, buttons, keyboard toggle).
 */
public class GameActivity extends SDLActivity {
    private static native int nativeToggleKeyboard();

    private static TouchControls controls;

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

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        if (mLayout == null) return;
        controls = new TouchControls(this, () -> {
            try { nativeToggleKeyboard(); } catch (Throwable ignored) { }
        });
        mLayout.addView(controls, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        if (mSurface != null) {
            // keep SDL's own touch handling, but remember the last position for the right-click button
            mSurface.setOnTouchListener((v, e) -> {
                if (controls != null) controls.onGameTouch(e);
                return mSurface.onTouch(v, e);
            });
        }
    }

    @Override
    protected void onDestroy() {
        controls = null;
        super.onDestroy();
    }

    /** Back button = Escape (close menus / inventory), as Terraria knows no "back" key. */
    @Override
    public boolean dispatchKeyEvent(KeyEvent event) {
        if (event.getKeyCode() == KeyEvent.KEYCODE_BACK) {
            if (event.getAction() == KeyEvent.ACTION_DOWN && event.getRepeatCount() == 0)
                SDLActivity.onNativeKeyDown(KeyEvent.KEYCODE_ESCAPE);
            else if (event.getAction() == KeyEvent.ACTION_UP)
                SDLActivity.onNativeKeyUp(KeyEvent.KEYCODE_ESCAPE);
            return true;
        }
        return super.dispatchKeyEvent(event);
    }

    /** Called from managed code (TMLDroid.GameState) when Terraria enters or leaves a world. */
    public static void setInGame(final boolean inGame) {
        final SDLActivity activity = mSingleton;
        final TouchControls c = controls;
        if (activity == null || c == null) return;
        activity.runOnUiThread(() -> c.setInGame(inGame));
    }
}
