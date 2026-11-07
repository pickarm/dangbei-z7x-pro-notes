package local.tvkeys;

import android.accessibilityservice.AccessibilityService;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Handler;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.InputDevice;
import android.view.WindowManager;
import android.view.accessibility.AccessibilityEvent;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import java.io.FileDescriptor;
import java.io.PrintWriter;

public final class FineVolumeService extends AccessibilityService {
    static volatile FineVolumeService active;
    private VoiceLoader voiceLoader;
    private final Handler handler = new Handler();
    private WindowManager wm;
    private LinearLayout overlay;
    private TextView label;
    private ProgressBar bar;
    private int heldKey;
    private int handledDowns;
    private int lastVolume = -1;
    private boolean upCaptured, downCaptured;
    private final Runnable hide = new Runnable() { public void run() { removeOverlay(); } };
    private final Runnable repeat = new Runnable() { public void run() {
        if (heldKey == 0) return;
        changeForKey(heldKey);
        handler.postDelayed(this, 120);
    }};

    @Override protected void onServiceConnected() { active = this; KeepAliveService.start(this, false); wm = (WindowManager)getSystemService(WINDOW_SERVICE); voiceLoader=new VoiceLoader(this);voiceLoader.start(); }
    @Override public void onAccessibilityEvent(AccessibilityEvent event) { }
    @Override public void onInterrupt() { }
    @Override public void onDestroy() { active = null; if(voiceLoader!=null)voiceLoader.stop();handler.removeCallbacksAndMessages(null); removeOverlay(); super.onDestroy(); }
    void refreshVoice(){if(voiceLoader!=null)voiceLoader.check();}
    @Override protected boolean onKeyEvent(KeyEvent event) {
        int key = event.getKeyCode();
        InputDevice device = event.getDevice();
        if (device == null || device.getVendorId() != 0x1d5a || device.getProductId() != 0xc081) return false;
        if (key != KeyEvent.KEYCODE_F12 && key != KeyEvent.KEYCODE_F11) return false;
        boolean isUp = key == KeyEvent.KEYCODE_F12;
        boolean captured = isUp ? upCaptured : downCaptured;
        if (event.getAction() == KeyEvent.ACTION_UP) {
            if (isUp) upCaptured = false; else downCaptured = false;
            if (heldKey == key) { heldKey = 0; handler.removeCallbacks(repeat); }
            return captured;
        }
        if (event.getAction() != KeyEvent.ACTION_DOWN) return captured;
        if (event.getRepeatCount() == 0) {
            captured = true;
            if (isUp) upCaptured = captured; else downCaptured = captured;
        }
        if (!captured) return false;
        if (event.getRepeatCount() == 0) {
            heldKey = key;
            handler.removeCallbacks(repeat);
            changeForKey(key);
            handler.postDelayed(repeat, 350);
        }
        return true;
    }
    private void changeForKey(int key) {
        handledDowns++;
        int step = VolumeControl.enabled(this) ? 1 : 5;
        lastVolume = VolumeControl.change(this, key == KeyEvent.KEYCODE_F12 ? step : -step);
        showVolume(lastVolume);
    }

    private int dp(int n) { return Math.round(n * getResources().getDisplayMetrics().density); }
    private void showVolume(int value) {
        try {
            if (overlay == null) {
                overlay = new LinearLayout(this);
                overlay.setOrientation(LinearLayout.VERTICAL);
                overlay.setPadding(dp(24), dp(12), dp(24), dp(18));
                GradientDrawable bg = new GradientDrawable();
                bg.setColor(Color.argb(240, 15, 23, 42)); bg.setCornerRadius(dp(16));
                overlay.setBackground(bg);
                label = new TextView(this); label.setTextSize(25); label.setTextColor(Color.WHITE);
                label.setGravity(Gravity.CENTER); label.setTypeface(null, Typeface.BOLD);
                overlay.addView(label, new LinearLayout.LayoutParams(-1, dp(46)));
                bar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
                bar.setMax(VolumeControl.audio(this).getStreamMaxVolume(3));
                overlay.addView(bar, new LinearLayout.LayoutParams(-1, dp(10)));
                WindowManager.LayoutParams p = new WindowManager.LayoutParams(dp(420), -2,
                    WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
                    WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE,
                    PixelFormat.TRANSLUCENT);
                p.gravity = Gravity.BOTTOM | Gravity.CENTER_HORIZONTAL; p.y = dp(42);
                wm.addView(overlay, p);
            }
            label.setText(value == 0 ? "静音   0 / 100" : "音量   " + value + " / 100");
            bar.setProgress(value);
            handler.removeCallbacks(hide); handler.postDelayed(hide, 1600);
        } catch (RuntimeException error) { removeOverlay(); }
    }
    private void removeOverlay() {
        if (overlay != null && wm != null) { try { wm.removeView(overlay); } catch (RuntimeException ignored) {} }
        overlay = null;
    }
    @Override protected void dump(FileDescriptor fd, PrintWriter out, String[] args) {
        out.println("FineVolumeService connected=" + (active == this));
        out.println("enabled=" + VolumeControl.enabled(this));
        out.println("handledDowns=" + handledDowns + " lastVolume=" + lastVolume);
        out.println("current=" + VolumeControl.audio(this).getStreamVolume(3) + " max=" + VolumeControl.audio(this).getStreamMaxVolume(3));
    }
}
