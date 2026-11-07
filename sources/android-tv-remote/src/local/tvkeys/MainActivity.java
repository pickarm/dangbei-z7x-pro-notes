package local.tvkeys;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

public final class MainActivity extends Activity {
    private TextView status, volume;
    private Button toggle,voiceToggle,displayToggle;
    private final Handler handler = new Handler();
    private final Runnable refresh = new Runnable() { public void run() { update(); handler.postDelayed(this, 500); } };
    private int dp(int n) { return Math.round(n * getResources().getDisplayMetrics().density); }
    private TextView text(String s, int size, int color) {
        TextView t = new TextView(this); t.setText(s); t.setTextSize(size); t.setTextColor(color); t.setPadding(0, dp(8), 0, dp(8)); return t;
    }
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        KeepAliveService.start(this, false);
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.HORIZONTAL);
        root.setGravity(Gravity.CENTER_VERTICAL); root.setPadding(dp(64), dp(36), dp(64), dp(36)); root.setBackgroundColor(Color.rgb(15,23,42));
        LinearLayout info = new LinearLayout(this); info.setOrientation(LinearLayout.VERTICAL); root.addView(info,new LinearLayout.LayoutParams(0,-2,1));
        TextView title = text("遥控优化",38,Color.WHITE); title.setTypeface(null,Typeface.BOLD); info.addView(title);
        volume = text("",34,Color.WHITE); info.addView(volume);
        status = text("",21,Color.rgb(94,234,212)); info.addView(status);
        info.addView(text("音量键每次调整 1 格，长按连续调整。\n0 为静音，100 为最大音量。\n返回其他应用后仍生效，重启自动恢复。",20,Color.rgb(203,213,225)));
        info.addView(text("Home 直接进入当贝桌面。\n语音支持光源亮度、打开已安装应用。\n卸载或停用服务前，请先恢复遥控器键位。",17,Color.rgb(148,163,184)));
        LinearLayout buttons = new LinearLayout(this); buttons.setOrientation(LinearLayout.VERTICAL);
        LinearLayout.LayoutParams right = new LinearLayout.LayoutParams(dp(270),-2); right.leftMargin=dp(38); root.addView(buttons,right);
        add(buttons,"音量 ＋1", v -> {VolumeControl.change(this,1); update();});
        add(buttons,"音量 －1", v -> {VolumeControl.change(this,-1); update();});
        toggle=add(buttons,"",v -> {VolumeControl.enabled(this,!VolumeControl.enabled(this)); update();});
        voiceToggle=add(buttons,"",v -> {VoiceLoader.enabled(this,!VoiceLoader.enabled(this));update();});
        displayToggle=add(buttons,"",v -> {DisplayLink.enabled(this,!DisplayLink.enabled(this));update();});
        add(buttons,"无障碍设置",v -> {try {startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS));} catch (RuntimeException e) {status.setText("此固件需通过 ADB 开启服务");}});
        add(buttons,"返回桌面",v -> {Intent i=new Intent(Intent.ACTION_MAIN); i.addCategory(Intent.CATEGORY_HOME); i.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK); startActivity(i);});
        setContentView(root); buttons.getChildAt(0).requestFocus(); update();
    }
    private Button add(LinearLayout p,String s,View.OnClickListener listener) {
        Button b=new Button(this); b.setText(s); b.setTextSize(22); b.setTextColor(Color.WHITE); b.setAllCaps(false); b.setFocusableInTouchMode(true); b.setOnClickListener(listener);
        b.setOnFocusChangeListener((v,focus) -> {GradientDrawable bg=new GradientDrawable(); bg.setColor(focus?Color.rgb(15,118,110):Color.rgb(51,65,85)); bg.setCornerRadius(dp(12)); bg.setStroke(dp(2),focus?Color.WHITE:Color.rgb(71,85,105)); b.setBackground(bg);});
        GradientDrawable bg=new GradientDrawable(); bg.setColor(Color.rgb(51,65,85)); bg.setCornerRadius(dp(12)); b.setBackground(bg);
        LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,dp(58)); lp.bottomMargin=dp(12); p.addView(b,lp); return b;
    }
    private void update() {
        volume.setText("当前音量   "+VolumeControl.audio(this).getStreamVolume(3)+" / "+VolumeControl.audio(this).getStreamMaxVolume(3));
        boolean enabled=VolumeControl.enabled(this);
        status.setText((FineVolumeService.active==null?"等待开启音量细调服务":enabled?"每次 1 格 · 后台服务正常":"每次 5 格 · 后台服务正常")+"\n语音增强："+VoiceLoader.status(this)+"\n电脑屏幕联动："+DisplayLink.status(this));
        toggle.setText(enabled?"切换为每次 5 格":"切换为每次 1 格");
        voiceToggle.setText(VoiceLoader.enabled(this)?"关闭语音增强":"开启语音增强");
        displayToggle.setText(DisplayLink.enabled(this)?"关闭电脑屏幕联动":"开启电脑屏幕联动");
    }
    @Override protected void onResume(){super.onResume(); handler.post(refresh);}
    @Override protected void onPause(){handler.removeCallbacks(refresh); super.onPause();}
}
