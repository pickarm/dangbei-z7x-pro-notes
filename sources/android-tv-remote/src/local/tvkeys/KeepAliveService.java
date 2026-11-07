package local.tvkeys;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.app.usage.UsageEvents;
import android.app.usage.UsageStatsManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.Intent;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.SystemClock;
import android.provider.Settings;
import java.io.FileDescriptor;
import java.io.PrintWriter;
import java.util.LinkedHashSet;

public final class KeepAliveService extends Service {
    private static final String CHANNEL = "remote_service";
    private static final String STOCK = "com.dangbei.leard.leradlauncher";
    private static final String HOME = "com.dangbei.tvlauncher";
    private final Handler handler = new Handler();
    private String repairStatus = "starting", homeStatus = "not-requested";
    private int repairs;
    private boolean homeScheduled;
    private DisplayLink displayLink;
    private final Runnable maintenance = new Runnable() { public void run() {
        repairAccessibility();
        handler.postDelayed(this, 15000);
    }};

    static void start(Context context, boolean boot) {
        Intent intent = new Intent(context, KeepAliveService.class).putExtra("boot", boot);
        if (Build.VERSION.SDK_INT >= 26) context.startForegroundService(intent);
        else context.startService(intent);
    }

    @Override public void onCreate() {
        super.onCreate();
        NotificationManager manager = (NotificationManager)getSystemService(NOTIFICATION_SERVICE);
        if (Build.VERSION.SDK_INT >= 26) manager.createNotificationChannel(
                new NotificationChannel(CHANNEL, "遥控优化后台服务", NotificationManager.IMPORTANCE_LOW));
        PendingIntent open = PendingIntent.getActivity(this, 0, new Intent(this, MainActivity.class),
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        Notification.Builder notification = Build.VERSION.SDK_INT >= 26 ?
                new Notification.Builder(this, CHANNEL) : new Notification.Builder(this);
        startForeground(101, notification.setSmallIcon(android.R.drawable.ic_media_play)
                .setContentTitle("遥控优化正在运行").setContentText("保持音量细调和语音增强服务")
                .setContentIntent(open).setOngoing(true).setShowWhen(false).build());
        handler.post(maintenance);
        displayLink=new DisplayLink(this);displayLink.start();
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        repairAccessibility();
        // The boot count survives this service restarting. Never pull the user home later.
        boolean afterBoot = (intent != null && intent.getBooleanExtra("boot", false)) ||
                SystemClock.elapsedRealtime() < 180000;
        if (afterBoot && !homeScheduled &&
                Settings.System.getInt(getContentResolver(), "codex_boot_home_enabled", 1) == 1) {
            final int boot = Settings.Global.getInt(getContentResolver(), "boot_count", -1);
            if (boot >= 0 && getSharedPreferences("settings", 0).getInt("home_boot_done", -2) != boot) {
                homeScheduled = true;
                homeStatus = "scheduled";
                handler.postDelayed(() -> handOffHome(boot), 8000);
            }
        }
        return START_STICKY;
    }

    private void repairAccessibility() {
        try {
            String own = new ComponentName(this, FineVolumeService.class).flattenToString();
            String value = Settings.Secure.getString(getContentResolver(), Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES);
            LinkedHashSet<String> services = new LinkedHashSet<>();
            boolean contains = false;
            if (value != null && !value.isEmpty()) for (String item : value.split(":")) {
                ComponentName component = ComponentName.unflattenFromString(item);
                if (component == null) continue;
                services.add(item);
                if (component.equals(new ComponentName(this, FineVolumeService.class))) contains = true;
            }
            if (!contains) {
                services.add(own);
                if (!Settings.Secure.putString(getContentResolver(), Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES,
                        String.join(":", services))) throw new IllegalStateException("accessibility write failed");
                repairs++;
            }
            if (Settings.Secure.getInt(getContentResolver(), Settings.Secure.ACCESSIBILITY_ENABLED, 0) != 1)
                if (!Settings.Secure.putInt(getContentResolver(), Settings.Secure.ACCESSIBILITY_ENABLED, 1))
                    throw new IllegalStateException("accessibility enable failed");
            repairStatus = "enabled";
        } catch (RuntimeException error) { repairStatus = error.getClass().getSimpleName(); }
    }

    private void handOffHome(int boot) {
        try {
            UsageStatsManager manager = (UsageStatsManager)getSystemService(USAGE_STATS_SERVICE);
            long now = System.currentTimeMillis();
            UsageEvents events = manager.queryEvents(now - Math.min(SystemClock.elapsedRealtime(), 120000), now);
            UsageEvents.Event event = new UsageEvents.Event();
            String foreground = null;
            while (events.hasNextEvent()) {
                events.getNextEvent(event);
                if (event.getEventType() == UsageEvents.Event.MOVE_TO_FOREGROUND) foreground = event.getPackageName();
            }
            if (STOCK.equals(foreground)) {
                Intent launch = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME)
                        .setComponent(new ComponentName(HOME, "com.dangbei.launcher.ui.main.MainActivity"))
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_RESET_TASK_IF_NEEDED);
                startActivity(launch);
                homeStatus = "switched-to-dangbei";
            } else homeStatus = HOME.equals(foreground) ? "already-dangbei" : "skipped-active-app";
            if (foreground != null) getSharedPreferences("settings", 0).edit().putInt("home_boot_done", boot).apply();
            else homeStatus = "usage-access-unavailable";
        } catch (RuntimeException error) { homeStatus = error.getClass().getSimpleName(); }
    }

    @Override public void onTaskRemoved(Intent intent) { repairAccessibility(); super.onTaskRemoved(intent); }
    @Override public void onDestroy() { if(displayLink!=null)displayLink.stop();handler.removeCallbacksAndMessages(null); super.onDestroy(); }
    @Override public IBinder onBind(Intent intent) { return null; }
    @Override protected void dump(FileDescriptor fd, PrintWriter out, String[] args) {
        out.println("KeepAliveService foreground=true accessibility=" + repairStatus + " repairs=" + repairs);
        out.println("bootHome=" + homeStatus);
        if(displayLink!=null)out.println(displayLink.diagnostic());
    }
}
