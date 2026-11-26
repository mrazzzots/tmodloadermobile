package io.github.tmldroid;

import android.annotation.SuppressLint;
import android.content.Context;
import android.content.SharedPreferences;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.drawable.GradientDrawable;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import org.libsdl.app.SDLActivity;

/**
 * On-screen controls for Terraria. Everything is sent to SDL as ordinary key / mouse events,
 * so the game sees a keyboard and mouse with its default bindings (WASD, Space, Esc, E, R, H, J, B, M...).
 * Taps on the game itself stay touches (SDL turns them into left-mouse: use item / aim).
 */
@SuppressLint("ViewConstructor")
public class TouchControls extends FrameLayout {
    // Android MotionEvent constants understood by SDLActivity.onNativeMouse
    private static final int MOUSE_ACTION_DOWN = 0, MOUSE_ACTION_UP = 1, MOUSE_ACTION_SCROLL = 8;
    private static final int MOUSE_BUTTON_RIGHT = 2;

    private final FrameLayout gameControls;   // only while a world is loaded
    private final SharedPreferences prefs;
    private boolean inGame;
    private boolean hidden;
    private float lastTouchX = -1, lastTouchY = -1;

    public TouchControls(Context ctx, Runnable toggleKeyboard) {
        super(ctx);
        prefs = ctx.getSharedPreferences("controls", Context.MODE_PRIVATE);
        hidden = prefs.getBoolean("hidden", false);

        gameControls = new FrameLayout(ctx);
        addView(gameControls, new LayoutParams(LayoutParams.MATCH_PARENT, LayoutParams.MATCH_PARENT));

        // --- left: movement joystick (A/D/W/S) ---
        Joystick stick = new Joystick(ctx);
        LayoutParams stickLp = new LayoutParams(dp(170), dp(170), Gravity.BOTTOM | Gravity.START);
        stickLp.setMargins(dp(28), 0, 0, dp(20));
        gameControls.addView(stick, stickLp);

        // --- right bottom: jump + right click ---
        View jump = keyButton(ctx, "Прыжок", KeyEvent.KEYCODE_SPACE, 86, 15);
        LayoutParams jumpLp = new LayoutParams(dp(86), dp(86), Gravity.BOTTOM | Gravity.END);
        jumpLp.setMargins(0, 0, dp(28), dp(28));
        gameControls.addView(jump, jumpLp);

        View rmb = mouseRightButton(ctx);
        LayoutParams rmbLp = new LayoutParams(dp(64), dp(64), Gravity.BOTTOM | Gravity.END);
        rmbLp.setMargins(0, 0, dp(126), dp(20));
        gameControls.addView(rmb, rmbLp);

        // --- right: row of quick actions above jump ---
        LinearLayout quick = new LinearLayout(ctx);
        quick.setOrientation(LinearLayout.HORIZONTAL);
        quick.addView(keyButton(ctx, "Крюк", KeyEvent.KEYCODE_E, 52, 11), square(52));
        quick.addView(keyButton(ctx, "Маунт", KeyEvent.KEYCODE_R, 52, 11), square(52));
        quick.addView(keyButton(ctx, "Лечение", KeyEvent.KEYCODE_H, 52, 10), square(52));
        quick.addView(keyButton(ctx, "Мана", KeyEvent.KEYCODE_J, 52, 11), square(52));
        quick.addView(keyButton(ctx, "Баффы", KeyEvent.KEYCODE_B, 52, 11), square(52));
        LayoutParams quickLp = new LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT, Gravity.BOTTOM | Gravity.END);
        quickLp.setMargins(0, 0, dp(20), dp(124));
        gameControls.addView(quick, quickLp);

        // --- right edge, middle: inventory, hotbar scroll, map, smart cursor, throw ---
        LinearLayout side = new LinearLayout(ctx);
        side.setOrientation(LinearLayout.VERTICAL);
        side.addView(keyButton(ctx, "Инвент.", KeyEvent.KEYCODE_ESCAPE, 56, 10), square(56));
        LinearLayout scroll = new LinearLayout(ctx);
        scroll.addView(wheelButton(ctx, "◀", 1), square(44));
        scroll.addView(wheelButton(ctx, "▶", -1), square(44));
        side.addView(scroll);
        side.addView(keyButton(ctx, "Карта", KeyEvent.KEYCODE_M, 56, 11), square(56));
        side.addView(keyButton(ctx, "Умный\nкурсор", KeyEvent.KEYCODE_CTRL_LEFT, 56, 9), square(56));
        side.addView(keyButton(ctx, "Выбросить", KeyEvent.KEYCODE_T, 56, 8), square(56));
        LayoutParams sideLp = new LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT, Gravity.TOP | Gravity.END);
        sideLp.setMargins(0, dp(150), dp(8), 0);
        gameControls.addView(side, sideLp);

        // --- always visible: keyboard + show/hide controls (top centre, out of Terraria's UI) ---
        LinearLayout always = new LinearLayout(ctx);
        TextView kb = label(ctx, "⌨", 18);
        kb.setOnClickListener(v -> toggleKeyboard.run());
        always.addView(kb, square(44));
        TextView eye = label(ctx, "🎮", 16);
        eye.setOnClickListener(v -> {
            hidden = !hidden;
            prefs.edit().putBoolean("hidden", hidden).apply();
            update();
        });
        always.addView(eye, square(44));
        LayoutParams alwaysLp = new LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT, Gravity.TOP | Gravity.CENTER_HORIZONTAL);
        alwaysLp.setMargins(0, dp(4), 0, 0);
        addView(always, alwaysLp);

        update();
    }

    /** Called from managed code (via GameActivity) when Terraria enters / leaves a world. */
    public void setInGame(boolean value) {
        inGame = value;
        update();
    }

    /** Remember where the player last touched the game, for the right-click button. */
    public void onGameTouch(MotionEvent e) {
        int i = e.getActionIndex();
        lastTouchX = e.getX(i);
        lastTouchY = e.getY(i);
    }

    private void update() {
        gameControls.setVisibility(inGame && !hidden ? VISIBLE : GONE);
    }

    // ------------------------------------------------------------------ input

    private static void keyDown(int keycode) { SDLActivity.onNativeKeyDown(keycode); }
    private static void keyUp(int keycode) { SDLActivity.onNativeKeyUp(keycode); }

    private View keyButton(Context ctx, String text, int keycode, int sizeDp, int textSp) {
        TextView b = label(ctx, text, textSp);
        b.setOnTouchListener((v, e) -> {
            switch (e.getActionMasked()) {
                case MotionEvent.ACTION_DOWN: keyDown(keycode); v.setAlpha(1f); return true;
                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL: keyUp(keycode); v.setAlpha(0.75f); return true;
            }
            return true;
        });
        return b;
    }

    private View wheelButton(Context ctx, String text, int direction) {
        TextView b = label(ctx, text, 14);
        b.setOnClickListener(v -> SDLActivity.onNativeMouse(0, MOUSE_ACTION_SCROLL, 0, direction, false));
        return b;
    }

    private View mouseRightButton(Context ctx) {
        TextView b = label(ctx, "ПКМ", 13);
        b.setOnTouchListener((v, e) -> {
            float x = lastTouchX >= 0 ? lastTouchX : getWidth() / 2f;
            float y = lastTouchY >= 0 ? lastTouchY : getHeight() / 2f;
            switch (e.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    SDLActivity.onNativeMouse(MOUSE_BUTTON_RIGHT, MOUSE_ACTION_DOWN, x, y, false);
                    v.setAlpha(1f);
                    return true;
                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL:
                    SDLActivity.onNativeMouse(0, MOUSE_ACTION_UP, x, y, false);
                    v.setAlpha(0.75f);
                    return true;
            }
            return true;
        });
        return b;
    }

    // ------------------------------------------------------------------ views

    private TextView label(Context ctx, String text, int textSp) {
        TextView t = new TextView(ctx);
        t.setText(text);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, textSp);
        t.setTextColor(Color.WHITE);
        t.setGravity(Gravity.CENTER);
        t.setIncludeFontPadding(false);
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(0x70000000);
        bg.setStroke(dp(1), 0x90FFFFFF);
        bg.setCornerRadius(dp(14));
        t.setBackground(bg);
        t.setAlpha(0.75f);
        return t;
    }

    private LinearLayout.LayoutParams square(int sizeDp) {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(dp(sizeDp), dp(sizeDp));
        lp.setMargins(dp(3), dp(3), dp(3), dp(3));
        return lp;
    }

    private int dp(float v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }

    /** Virtual stick: holds A/D for left/right, W/S for up/down (ropes, platforms). */
    private final class Joystick extends View {
        private final Paint base = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint knob = new Paint(Paint.ANTI_ALIAS_FLAG);
        private float kx, ky;          // knob offset from centre, in px
        private int pointer = -1;
        private boolean left, right, up, down;

        Joystick(Context ctx) {
            super(ctx);
            base.setColor(0x50000000);
            knob.setColor(0xA0FFFFFF);
        }

        @Override
        protected void onDraw(Canvas c) {
            float cx = getWidth() / 2f, cy = getHeight() / 2f, r = Math.min(cx, cy);
            base.setStyle(Paint.Style.FILL);
            c.drawCircle(cx, cy, r, base);
            base.setStyle(Paint.Style.STROKE);
            c.drawCircle(cx, cy, r - 1, base);
            c.drawCircle(cx + kx, cy + ky, r * 0.38f, knob);
        }

        @SuppressLint("ClickableViewAccessibility")
        @Override
        public boolean onTouchEvent(MotionEvent e) {
            switch (e.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                case MotionEvent.ACTION_POINTER_DOWN:
                    if (pointer == -1) {
                        pointer = e.getPointerId(e.getActionIndex());
                        move(e.getX(e.getActionIndex()), e.getY(e.getActionIndex()));
                    }
                    return true;
                case MotionEvent.ACTION_MOVE: {
                    int i = e.findPointerIndex(pointer);
                    if (i >= 0) move(e.getX(i), e.getY(i));
                    return true;
                }
                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_POINTER_UP:
                case MotionEvent.ACTION_CANCEL:
                    if (e.getActionMasked() == MotionEvent.ACTION_CANCEL || e.getPointerId(e.getActionIndex()) == pointer) {
                        pointer = -1;
                        kx = ky = 0;
                        apply(false, false, false, false);
                        invalidate();
                    }
                    return true;
            }
            return true;
        }

        private void move(float x, float y) {
            float cx = getWidth() / 2f, cy = getHeight() / 2f, r = Math.min(cx, cy);
            float dx = x - cx, dy = y - cy;
            float len = (float) Math.hypot(dx, dy);
            if (len > r) { dx = dx / len * r; dy = dy / len * r; }
            kx = dx; ky = dy;
            float nx = dx / r, ny = dy / r, dead = 0.3f;
            // vertical needs a firmer push, so walking never drops through platforms by accident
            apply(nx < -dead, nx > dead, ny < -0.6f, ny > 0.6f);
            invalidate();
        }

        private void apply(boolean l, boolean r, boolean u, boolean d) {
            left = set(left, l, KeyEvent.KEYCODE_A);
            right = set(right, r, KeyEvent.KEYCODE_D);
            up = set(up, u, KeyEvent.KEYCODE_W);
            down = set(down, d, KeyEvent.KEYCODE_S);
        }

        private boolean set(boolean was, boolean now, int keycode) {
            if (now && !was) keyDown(keycode);
            if (!now && was) keyUp(keycode);
            return now;
        }
    }
}
