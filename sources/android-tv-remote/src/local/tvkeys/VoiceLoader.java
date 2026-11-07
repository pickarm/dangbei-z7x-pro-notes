package local.tvkeys;

import android.content.Context;
import android.os.Handler;
import android.provider.Settings;
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

final class VoiceLoader {
    private static final String APPLY="/data/local/codex-voice/reapply.sh";
    private static final String DISABLE="/data/local/codex-voice/disable.sh";
    private final Context context;
    private final Handler handler=new Handler();
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private volatile boolean running;
    private final Runnable tick=new Runnable(){public void run(){check(); handler.postDelayed(this,60000);}};
    VoiceLoader(Context c){context=c.getApplicationContext();}
    static boolean enabled(Context c){return c.getSharedPreferences("settings",0).getBoolean("voice_enabled",true);}
    static String status(Context c){
        if(!enabled(c))return "已关闭";
        String value=Settings.System.getString(c.getContentResolver(),"codex_voice_loader");
        if("healthy".equals(value))return "正常";
        if("failed-for-current-process".equals(value))return "加载失败，请检查";
        if("waiting-for-voice-service".equals(value))return "等待语音助手";
        return "正在准备";
    }
    static void enabled(Context c,boolean value){c.getSharedPreferences("settings",0).edit().putBoolean("voice_enabled",value).apply(); if(FineVolumeService.active!=null)FineVolumeService.active.refreshVoice();}
    void start(){handler.postDelayed(tick,10000);}
    void stop(){handler.removeCallbacksAndMessages(null);worker.shutdownNow();}
    void check(){
        if(running)return;
        running=true;
        worker.execute(()->{
            try{
                boolean on=enabled(context);
                String actual=Settings.System.getString(context.getContentResolver(),"codex_voice_enabled");
                if(!on && !"1".equals(actual))return;
                String command=exec("/system/bin/getprop","vendor.xgimi.system.debug.cmd");
                String state=exec("/system/bin/getprop","init.svc.debugserver");
                if("running".equals(state) || (!command.isEmpty()&&!APPLY.equals(command)&&!DISABLE.equals(command)))return;
                exec("/system/bin/setprop","vendor.xgimi.system.debugserver","0");
                exec("/system/bin/setprop","vendor.xgimi.system.debug.cmd",on?APPLY:DISABLE);
                exec("/system/bin/setprop","vendor.xgimi.system.debugserver","1");
            }catch(Exception e){context.getSharedPreferences("settings",0).edit().putString("voice_loader_error",e.getClass().getSimpleName()).apply();}
            finally{running=false;}
        });
    }
    private String exec(String... args)throws Exception{
        Process p=new ProcessBuilder(args).redirectErrorStream(true).start();
        StringBuilder text=new StringBuilder();
        try(BufferedReader reader=new BufferedReader(new InputStreamReader(p.getInputStream(),"UTF-8"))){String line;while((line=reader.readLine())!=null)text.append(line);}
        if(p.waitFor()!=0)throw new IllegalStateException(text.toString());
        return text.toString().trim();
    }
}
