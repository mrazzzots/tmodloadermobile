package io.github.tmldroid;

import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.util.TypedValue;
import android.view.Gravity;
import android.widget.RelativeLayout;
import android.widget.TextView;

import org.libsdl.app.SDLActivity;

/**
 * Hosts tModLoader. SDL loads libSDL2.so + libmain.so and calls SDL_main on its thread,
 * which jumps to TMLDroid.GameEntry.RunGame (published by TMLDroid.App).
 * Adds a small overlay button that shows/hides the on-screen keyboard (names, chat, search).
 */
public class GameActivity extends SDLActivity {
    private static native int nativeToggleKeyboard();

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
        TextView kb = new TextView(this);
        kb.setText("⌨");
        kb.setTextSize(TypedValue.COMPLEX_UNIT_SP, 20);
        kb.setTextColor(Color.WHITE);
        kb.setGravity(Gravity.CENTER);
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(0x66000000);
        bg.setCornerRadius(dp(10));
        kb.setBackground(bg);
        kb.setAlpha(0.8f);
        kb.setOnClickListener(v -> {
            try { nativeToggleKeyboard(); } catch (Throwable ignored) { }
        });
        int size = dp(44);
        RelativeLayout.LayoutParams lp = new RelativeLayout.LayoutParams(size, size);
        lp.addRule(RelativeLayout.ALIGN_PARENT_BOTTOM);
        lp.addRule(RelativeLayout.ALIGN_PARENT_RIGHT);
        lp.setMargins(0, 0, dp(16), dp(16));
        mLayout.addView(kb, lp);
    }

    private int dp(int v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }
}
