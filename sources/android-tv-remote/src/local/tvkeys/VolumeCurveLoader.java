package local.tvkeys;

import android.content.Context;
import android.os.Handler;
import android.os.SystemClock;
import android.provider.Settings;
import java.io.BufferedReader;
import java.io.File;
import java.io.InputStreamReader;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

final class VolumeCurveLoader {
    private static final String APPLY="/data/local/codex-volume/reapply.sh";
    private final Context context;
    private final Handler handler=new Handler();
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private volatile boolean running;
    private final long retryDeadline=SystemClock.elapsedRealtime()+90000;
    private final Runnable tick=new Runnable(){public void run(){
        check();
        String status=Settings.System.getString(context.getContentResolver(),"codex_volume_curve_status");
        boolean retry=SystemClock.elapsedRealtime()<retryDeadline && !ready(context) && !"disabled".equals(status) && !"failed-for-current-process".equals(status);
        handler.postDelayed(this,retry?2500:30000);
    }};
    VolumeCurveLoader(Context c){context=c.getApplicationContext();}
    void start(){handler.postDelayed(tick,6000);}
    void stop(){handler.removeCallbacksAndMessages(null);worker.shutdownNow();}
    private static boolean ready(Context c){
        return "healthy".equals(Settings.System.getString(c.getContentResolver(),"codex_volume_curve_status")) &&
                Settings.System.getInt(c.getContentResolver(),"codex_volume_curve_boot_count",-2)==Settings.Global.getInt(c.getContentResolver(),"boot_count",-1);
    }
    static String status(Context c){
        String value=Settings.System.getString(c.getContentResolver(),"codex_volume_curve_status");
        if("healthy".equals(value)&&ready(c))return "平滑细调";
        if("disabled".equals(value))return "已关闭";
        if("failed-for-current-process".equals(value))return "加载失败，请检查";
        return "正在准备";
    }
    private void check(){
        if(running || !new File(APPLY).isFile())return;
        boolean enabled=Settings.System.getInt(context.getContentResolver(),"codex_volume_curve_enabled",0)==1;
        String status=Settings.System.getString(context.getContentResolver(),"codex_volume_curve_status");
        if(!enabled && (status==null || "disabled".equals(status)))return;
        running=true;
        worker.execute(()->{
            try{
                String command=exec("/system/bin/getprop","vendor.xgimi.system.debug.cmd");
                String state=exec("/system/bin/getprop","init.svc.debugserver");
                if("running".equals(state)||(!command.isEmpty()&&!APPLY.equals(command)))return;
                exec("/system/bin/setprop","vendor.xgimi.system.debugserver","0");
                exec("/system/bin/setprop","vendor.xgimi.system.debug.cmd",APPLY);
                exec("/system/bin/setprop","vendor.xgimi.system.debugserver","1");
            }catch(Exception e){Settings.System.putString(context.getContentResolver(),"codex_volume_curve_loader_error",e.getClass().getSimpleName());}
            finally{running=false;}
        });
    }
    private static String exec(String... args)throws Exception{
        Process p=new ProcessBuilder(args).redirectErrorStream(true).start();
        StringBuilder text=new StringBuilder();
        try(BufferedReader reader=new BufferedReader(new InputStreamReader(p.getInputStream(),"UTF-8"))){String line;while((line=reader.readLine())!=null)text.append(line);}
        if(p.waitFor()!=0)throw new IllegalStateException(text.toString());
        return text.toString().trim();
    }
}
