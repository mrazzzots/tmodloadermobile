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

import java.util.ArrayList;
import java.util.List;
import java.util.function.IntConsumer;

/**
 * On-screen controls for Terraria. Everything is sent to SDL as ordinary key / mouse events,
 * so the game sees a keyboard and mouse with its default bindings (WASD, Space, Esc, E, R, H, J, B, M...).
 * Taps on the game itself stay touches (SDL turns them into left-mouse: use item / aim).
 *
 * Each control has a position (centre, as a fraction of the screen) and a scale; both can be
 * changed in the editor (pencil button) and are stored in SharedPreferences.
 */
@SuppressLint({"ViewConstructor", "ClickableViewAccessibility"})
public class TouchControls extends FrameLayout {
    // Android MotionEvent constants understood by SDLActivity.onNativeMouse
    private static final int MOUSE_ACTION_DOWN = 0, MOUSE_ACTION_UP = 1, MOUSE_ACTION_MOVE = 2, MOUSE_ACTION_SCROLL = 8;
    private static final int MOUSE_BUTTON_LEFT = 1, MOUSE_BUTTON_RIGHT = 2;
    /** SDL derives which button changed from the full button state, so keep it for both sticks/buttons. */
    private static int mouseState;

    private static void mouseButton(int button, boolean down, float x, float y) {
        if (down) mouseState |= button; else mouseState &= ~button;
        SDLActivity.onNativeMouse(mouseState, down ? MOUSE_ACTION_DOWN : MOUSE_ACTION_UP, x, y, false);
    }

    /** Commands for managed code (polled by TMLDroid.GameState). */
    public static final int CMD_MENU = 1, CMD_ZOOM_IN = 2, CMD_ZOOM_OUT = 3;

    private final class Ctl {
        final String id, name;
        final View view;
        final int baseDp;
        final float defX, defY;
        final boolean editorButton;
        float x, y, scale;

        Ctl(String id, String name, View view, int baseDp, float defX, float defY, boolean editorButton) {
            this.id = id; this.name = name; this.view = view; this.baseDp = baseDp;
            this.defX = defX; this.defY = defY; this.editorButton = editorButton;
            x = prefs.getFloat(id + ".x", defX);
            y = prefs.getFloat(id + ".y", defY);
            scale = prefs.getFloat(id + ".s", 1f);
        }
    }

    private final SharedPreferences prefs;
    private final List<Ctl> game = new ArrayList<>();   // shown only in a world
    private final List<Ctl> meta = new ArrayList<>();   // always shown (menu, zoom, keyboard, editor)
    private final LinearLayout editorBar;
    private final TextView editorInfo;
    private boolean inGame, hidden, editing;
    private float opacity;
    private Ctl selected;
    private float lastTouchX = -1, lastTouchY = -1;
    private float dragDx, dragDy;

    public TouchControls(Context ctx, Runnable toggleKeyboard, IntConsumer command) {
        super(ctx);
        prefs = ctx.getSharedPreferences("controls", Context.MODE_PRIVATE);
        hidden = prefs.getBoolean("hidden", false);
        opacity = prefs.getFloat("opacity", 0.7f);

        // ---- game controls: id, name, view, size dp, default centre (fraction of width, height) ----
        add(game, "stick", "Джойстик", new Joystick(ctx), 140, 0.11f, 0.72f);
        add(game, "aim", "Атака (ЛКМ)", new AimStick(ctx), 140, 0.80f, 0.70f);
        add(game, "jump", "Прыжок", keyButton("Прыжок", KeyEvent.KEYCODE_SPACE, 12), 64, 0.935f, 0.84f);
        add(game, "rmb", "ПКМ", mouseRightButton(), 50, 0.935f, 0.58f);

        // bottom row between the sticks
        add(game, "hook", "Крюк", keyButton("Крюк", KeyEvent.KEYCODE_E, 10), 44, 0.26f, 0.92f);
        add(game, "mount", "Маунт", keyButton("Маунт", KeyEvent.KEYCODE_R, 10), 44, 0.315f, 0.92f);
        add(game, "heal", "Лечение", keyButton("Лечен.", KeyEvent.KEYCODE_H, 9), 44, 0.37f, 0.92f);
        add(game, "mana", "Мана", keyButton("Мана", KeyEvent.KEYCODE_J, 10), 44, 0.425f, 0.92f);
        add(game, "buff", "Баффы", keyButton("Баффы", KeyEvent.KEYCODE_B, 9), 44, 0.48f, 0.92f);
        add(game, "inv", "Инвентарь", keyButton("Инвент.", KeyEvent.KEYCODE_ESCAPE, 9), 44, 0.535f, 0.92f);
        add(game, "map", "Карта", keyButton("Карта", KeyEvent.KEYCODE_M, 9), 44, 0.59f, 0.92f);
        add(game, "smart", "Умный курсор", keyButton("Умный\nкурсор", KeyEvent.KEYCODE_CTRL_LEFT, 8), 44, 0.645f, 0.92f);
        // second row
        add(game, "prev", "Слот ◀", wheelButton("\u25C0", 1), 40, 0.26f, 0.80f);
        add(game, "next", "Слот ▶", wheelButton("\u25B6", -1), 40, 0.315f, 0.80f);
        add(game, "throw", "Выбросить", keyButton("Выбр.", KeyEvent.KEYCODE_T, 9), 40, 0.37f, 0.80f);

        // ---- always visible, top centre ----
        add(meta, "menu", "Меню", actionButton("Меню", () -> command.accept(CMD_MENU), 10, false), 40, 0.40f, 0.06f);
        add(meta, "zoomout", "Масштаб −", actionButton("−", () -> command.accept(CMD_ZOOM_OUT), 18, false), 40, 0.44f, 0.06f);
        add(meta, "zoomin", "Масштаб +", actionButton("+", () -> command.accept(CMD_ZOOM_IN), 18, false), 40, 0.48f, 0.06f);
        add(meta, "kb", "Клавиатура", actionButton("⌨", toggleKeyboard, 16, false), 40, 0.52f, 0.06f);
        add(meta, "edit", "Редактор", actionButton("✎", () -> setEditing(!editing), 16, true), 40, 0.56f, 0.06f);
        add(meta, "hide", "Скрыть", actionButton("🎮", () -> {
            hidden = !hidden;
            prefs.edit().putBoolean("hidden", hidden).apply();
            update();
        }, 14, false), 40, 0.60f, 0.06f);

        // ---- editor bar ----
        editorBar = new LinearLayout(ctx);
        editorBar.setOrientation(LinearLayout.HORIZONTAL);
        editorBar.setGravity(Gravity.CENTER_VERTICAL);
        editorBar.setPadding(dp(8), dp(4), dp(8), dp(4));
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(0xCC202020);
        bg.setCornerRadius(dp(10));
        editorBar.setBackground(bg);
        editorInfo = text("", 12);
        editorBar.addView(editorInfo, new LinearLayout.LayoutParams(dp(180), LayoutParams.WRAP_CONTENT));
        editorBar.addView(barButton("Размер −", () -> resizeSelected(0.9f)));
        editorBar.addView(barButton("Размер +", () -> resizeSelected(1.1f)));
        editorBar.addView(barButton("Прозр. −", () -> setOpacity(opacity - 0.1f)));
        editorBar.addView(barButton("Прозр. +", () -> setOpacity(opacity + 0.1f)));
        editorBar.addView(barButton("Сброс", this::resetLayout));
        editorBar.addView(barButton("Готово", () -> setEditing(false)));
        LayoutParams barLp = new LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT, Gravity.TOP | Gravity.CENTER_HORIZONTAL);
        barLp.topMargin = dp(60);
        addView(editorBar, barLp);
        editorBar.setVisibility(GONE);

        update();
    }

    // ------------------------------------------------------------------ state

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
        boolean showGame = editing || (inGame && !hidden);
        for (Ctl c : game) {
            c.view.setVisibility(showGame ? VISIBLE : GONE);
            c.view.setAlpha(c == selected ? 1f : opacity);
        }
        for (Ctl c : meta) c.view.setAlpha(c == selected ? 1f : Math.max(opacity, 0.5f));
        editorBar.setVisibility(editing ? VISIBLE : GONE);
    }

    private void setEditing(boolean value) {
        editing = value;
        selected = null;
        editorInfo.setText("Нажмите кнопку и тащите");
        if (!value) save();
        update();
    }

    private void setOpacity(float value) {
        opacity = Math.max(0.15f, Math.min(1f, value));
        prefs.edit().putFloat("opacity", opacity).apply();
        update();
    }

    private void resizeSelected(float factor) {
        if (selected == null) { editorInfo.setText("Сначала выберите кнопку"); return; }
        selected.scale = Math.max(0.5f, Math.min(2.5f, selected.scale * factor));
        showSelected();
        layoutControls();
    }

    private void showSelected() {
        editorInfo.setText(selected.name + ": " + Math.round(selected.scale * 100) + "%");
    }

    private void resetLayout() {
        for (Ctl c : all()) { c.x = c.defX; c.y = c.defY; c.scale = 1f; }
        selected = null;
        setOpacity(0.7f);
        layoutControls();
        save();
        editorInfo.setText("Раскладка сброшена");
    }

    private void save() {
        SharedPreferences.Editor ed = prefs.edit();
        for (Ctl c : all()) ed.putFloat(c.id + ".x", c.x).putFloat(c.id + ".y", c.y).putFloat(c.id + ".s", c.scale);
        ed.apply();
    }

    private List<Ctl> all() {
        List<Ctl> l = new ArrayList<>(game);
        l.addAll(meta);
        return l;
    }

    // ------------------------------------------------------------------ layout

    private void add(List<Ctl> list, String id, String name, View v, int baseDp, float x, float y) {
        Ctl c = new Ctl(id, name, v, baseDp, x, y, "edit".equals(id));
        list.add(c);
        v.setTag(c);
        addView(v, new LayoutParams(dp(baseDp), dp(baseDp), Gravity.TOP | Gravity.START));
    }

    @Override
    protected void onSizeChanged(int w, int h, int ow, int oh) {
        super.onSizeChanged(w, h, ow, oh);
        post(this::layoutControls);
    }

    private void layoutControls() {
        int w = getWidth(), h = getHeight();
        if (w == 0 || h == 0) return;
        for (Ctl c : all()) {
            int size = dp(c.baseDp * c.scale);
            LayoutParams lp = (LayoutParams) c.view.getLayoutParams();
            lp.width = size;
            lp.height = size;
            lp.gravity = Gravity.TOP | Gravity.START;
            lp.leftMargin = clamp(Math.round(c.x * w - size / 2f), 0, w - size);
            lp.topMargin = clamp(Math.round(c.y * h - size / 2f), 0, h - size);
            c.view.setLayoutParams(lp);
        }
    }

    private static int clamp(int v, int lo, int hi) { return Math.max(lo, Math.min(hi, v)); }

    /** Editor: touching a control selects it, dragging moves it. True = consumed by the editor. */
    private boolean editTouch(View v, MotionEvent e) {
        if (!editing) return false;
        Ctl c = (Ctl) v.getTag();
        if (c.editorButton) return false;
        int w = getWidth(), h = getHeight();
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                selected = c;
                dragDx = c.x * w - e.getRawX();
                dragDy = c.y * h - e.getRawY();
                showSelected();
                update();
                break;
            case MotionEvent.ACTION_MOVE:
                c.x = Math.max(0f, Math.min(1f, (e.getRawX() + dragDx) / w));
                c.y = Math.max(0f, Math.min(1f, (e.getRawY() + dragDy) / h));
                layoutControls();
                break;
        }
        return true;
    }

    // ------------------------------------------------------------------ input

    private static void keyDown(int keycode) { SDLActivity.onNativeKeyDown(keycode); }
    private static void keyUp(int keycode) { SDLActivity.onNativeKeyUp(keycode); }

    private View keyButton(String label, int keycode, int textSp) {
        TextView b = text(label, textSp);
        styleButton(b);
        b.setOnTouchListener((v, e) -> {
            if (editTouch(v, e)) return true;
            switch (e.getActionMasked()) {
                case MotionEvent.ACTION_DOWN: keyDown(keycode); v.setAlpha(1f); break;
                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL: keyUp(keycode); v.setAlpha(opacity); break;
            }
            return true;
        });
        return b;
    }

    private View actionButton(String label, Runnable action, int textSp, boolean editorButton) {
        TextView b = text(label, textSp);
        styleButton(b);
        b.setOnTouchListener((v, e) -> {
            if (editTouch(v, e)) return true;
            if (e.getActionMasked() == MotionEvent.ACTION_UP) action.run();
            return true;
        });
        return b;
    }

    private View wheelButton(String label, int direction) {
        return actionButton(label, () -> SDLActivity.onNativeMouse(0, MOUSE_ACTION_SCROLL, 0, direction, false), 14, false);
    }

    private View mouseRightButton() {
        TextView b = text("ПКМ", 12);
        styleButton(b);
        b.setOnTouchListener((v, e) -> {
            if (editTouch(v, e)) return true;
            float x = lastTouchX >= 0 ? lastTouchX : getWidth() / 2f;
            float y = lastTouchY >= 0 ? lastTouchY : getHeight() / 2f;
            switch (e.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    mouseButton(MOUSE_BUTTON_RIGHT, true, x, y);
                    v.setAlpha(1f);
                    break;
                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL:
                    mouseButton(MOUSE_BUTTON_RIGHT, false, x, y);
                    v.setAlpha(opacity);
                    break;
            }
            return true;
        });
        return b;
    }

    // ------------------------------------------------------------------ views

    private TextView text(String s, int sp) {
        TextView t = new TextView(getContext());
        t.setText(s);
        t.setTextSize(TypedValue.COMPLEX_UNIT_SP, sp);
        t.setTextColor(Color.WHITE);
        t.setGravity(Gravity.CENTER);
        t.setIncludeFontPadding(false);
        return t;
    }

    private void styleButton(TextView t) {
        GradientDrawable bg = new GradientDrawable();
        bg.setShape(GradientDrawable.OVAL);
        bg.setColor(0x80000000);
        bg.setStroke(dp(1), 0xB0FFFFFF);
        t.setBackground(bg);
    }

    private TextView barButton(String s, Runnable action) {
        TextView t = text(s, 12);
        t.setPadding(dp(10), dp(8), dp(10), dp(8));
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(0x60FFFFFF);
        bg.setCornerRadius(dp(8));
        t.setBackground(bg);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(LayoutParams.WRAP_CONTENT, LayoutParams.WRAP_CONTENT);
        lp.setMargins(dp(4), 0, dp(4), 0);
        t.setLayoutParams(lp);
        t.setOnClickListener(v -> action.run());
        return t;
    }

    private int dp(float v) {
        return Math.round(v * getResources().getDisplayMetrics().density);
    }

    /**
     * Attack stick, like Terraria mobile: the direction puts the cursor around the player (screen
     * centre, the camera follows the player) and holding it past the dead zone holds the left mouse
     * button, so the selected item is used / swung / fired in that direction.
     */
    private final class AimStick extends View {
        private final Paint base = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint knob = new Paint(Paint.ANTI_ALIAS_FLAG);
        private float kx, ky;
        private int pointer = -1;
        private boolean pressed;
        private float dirX = 1, dirY = 0; // last aim direction (kept while the knob is in the centre)

        AimStick(Context ctx) {
            super(ctx);
            base.setStrokeWidth(dp(1));
            knob.setColor(0xB0FF7060);
        }

        @Override
        protected void onDraw(Canvas c) {
            float cx = getWidth() / 2f, cy = getHeight() / 2f, r = Math.min(cx, cy);
            base.setStyle(Paint.Style.FILL);
            base.setColor(pressed ? 0x60802020 : 0x60000000);
            c.drawCircle(cx, cy, r, base);
            base.setStyle(Paint.Style.STROKE);
            base.setColor(0xB0FFB0A0);
            c.drawCircle(cx, cy, r - dp(1), base);
            c.drawCircle(cx + kx, cy + ky, r * 0.38f, knob);
        }

        @Override
        public boolean onTouchEvent(MotionEvent e) {
            if (editTouch(this, e)) return true;
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
                        release();
                        kx = ky = 0;
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
            if (len > r) { dx = dx / len * r; dy = dy / len * r; len = r; }
            kx = dx; ky = dy;
            invalidate();
            // Holding the stick always attacks. Near the centre the last direction is kept
            // (a tiny wobble of the thumb must not flip the aim); farther out the cursor follows.
            if (len >= r * 0.15f) { dirX = dx / len; dirY = dy / len; }
            float sw = TouchControls.this.getWidth(), sh = TouchControls.this.getHeight();
            // cursor: from the screen centre (player) out to 40% of the screen height, never
            // closer than ~6% so the item is used in front of the player, not on top of him
            float reach = sh * Math.max(0.06f, 0.40f * Math.min(1f, len / r * 1.2f));
            float mx = sw / 2f + dirX * reach, my = sh / 2f + dirY * reach;
            lastTouchX = mx; lastTouchY = my; // right-click button follows the aim too
            if (!pressed) {
                pressed = true;
                mouseButton(MOUSE_BUTTON_LEFT, true, mx, my);
            } else {
                SDLActivity.onNativeMouse(mouseState, MOUSE_ACTION_MOVE, mx, my, false);
            }
        }

        private void release() {
            if (!pressed) return;
            pressed = false;
            mouseButton(MOUSE_BUTTON_LEFT, false, lastTouchX, lastTouchY);
        }
    }

    /** Virtual stick: holds A/D for left/right, W/S for up/down (ropes, platforms). */
    private final class Joystick extends View {
        private final Paint base = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint knob = new Paint(Paint.ANTI_ALIAS_FLAG);
        private float kx, ky;
        private int pointer = -1;
        private boolean left, right, up, down;

        Joystick(Context ctx) {
            super(ctx);
            base.setStrokeWidth(dp(1));
            knob.setColor(0xB0FFFFFF);
        }

        @Override
        protected void onDraw(Canvas c) {
            float cx = getWidth() / 2f, cy = getHeight() / 2f, r = Math.min(cx, cy);
            base.setStyle(Paint.Style.FILL);
            base.setColor(0x60000000);
            c.drawCircle(cx, cy, r, base);
            base.setStyle(Paint.Style.STROKE);
            base.setColor(0xB0FFFFFF);
            c.drawCircle(cx, cy, r - dp(1), base);
            c.drawCircle(cx + kx, cy + ky, r * 0.38f, knob);
        }

        @Override
        public boolean onTouchEvent(MotionEvent e) {
            if (editTouch(this, e)) return true;
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
