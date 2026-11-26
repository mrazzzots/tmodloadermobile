package io.github.tmldroid;

import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;

import java.util.concurrent.ConcurrentLinkedQueue;

import org.libsdl.app.SDLActivity;

/**
 * Hosts tModLoader. SDL loads libSDL2.so + libmain.so and calls SDL_main on its thread,
 * which jumps to TMLDroid.GameEntry.RunGame (published by TMLDroid.App).
 * On top of SDL's surface sits {@link TouchControls} (joystick, buttons, keyboard toggle).
 */
public class GameActivity extends SDLActivity {
    private static native int nativeToggleKeyboard();

    private static TouchControls controls;
    private static final ConcurrentLinkedQueue<Integer> commands = new ConcurrentLinkedQueue<>();
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable hideBars = this::hideSystemBars;

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
        }, commands::add);
        mLayout.addView(controls, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        hideSystemBars();
        getWindow().getDecorView().setOnSystemUiVisibilityChangeListener(v -> {
            if ((v & View.SYSTEM_UI_FLAG_FULLSCREEN) == 0) {
                handler.removeCallbacks(hideBars);
                handler.postDelayed(hideBars, 2000);
            }
        });
        if (mSurface != null) {
            // keep SDL's own touch handling, but remember the last position for the right-click button
            mSurface.setOnTouchListener((v, e) -> {
                if (controls != null) controls.onGameTouch(e);
                return mSurface.onTouch(v, e);
            });
        }
    }

    // ---- immersive full screen: SDL shows the system bars for a non-fullscreen window ----

    @SuppressWarnings("deprecation")
    private void hideSystemBars() {
        if (Build.VERSION.SDK_INT >= 28) {
            WindowManager.LayoutParams a = getWindow().getAttributes();
            a.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
            getWindow().setAttributes(a);
        }
        View decor = getWindow().getDecorView();
        decor.setSystemUiVisibility(View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY | View.SYSTEM_UI_FLAG_FULLSCREEN
                | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION | View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
        if (Build.VERSION.SDK_INT >= 30) {
            getWindow().setDecorFitsSystemWindows(false);
            WindowInsetsController c = getWindow().getInsetsController();
            if (c != null) {
                c.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
                c.hide(WindowInsets.Type.statusBars() | WindowInsets.Type.navigationBars());
            }
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        handler.postDelayed(hideBars, 300);
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) {
            hideSystemBars();
            handler.postDelayed(hideBars, 1000); // SDL may re-show them while creating its window
        }
    }

    @Override
    protected void onDestroy() {
        controls = null;
        boolean finishing = isFinishing();
        super.onDestroy();
        // tModLoader cannot start twice in one process (static state, native resolvers, hooks).
        // Once the game has ended, end the process too: Android restarts the launcher screen in a
        // fresh process, and the next "Play" gets a clean runtime.
        if (finishing) {
            android.util.Log.i("TMLDroid", "game finished: ending process for a clean next launch");
            new Handler(Looper.getMainLooper()).postDelayed(
                () -> android.os.Process.killProcess(android.os.Process.myPid()), 300);
        }
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

    /** Polled by managed code (TMLDroid.GameState): next overlay command or 0. */
    public static int pollCommand() {
        Integer c = commands.poll();
        return c == null ? 0 : c;
    }

    /** Called from managed code (TMLDroid.GameState) when Terraria enters or leaves a world. */
    public static void setInGame(final boolean inGame) {
        final SDLActivity activity = mSingleton;
        final TouchControls c = controls;
        if (activity == null || c == null) return;
        activity.runOnUiThread(() -> c.setInGame(inGame));
    }
}
