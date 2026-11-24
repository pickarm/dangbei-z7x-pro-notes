package local.tvkeys;

import android.content.Context;
import android.os.Handler;
import android.provider.Settings;
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

final class HomeGuardLoader {
    private static final String APPLY="/data/local/codex-home-guard/reapply.sh";
    private final Context context;
    private final Handler handler=new Handler();
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private volatile boolean running;
    private final Runnable tick=new Runnable(){public void run(){check();handler.postDelayed(this,60000);}};
    HomeGuardLoader(Context c){context=c.getApplicationContext();}
    void start(){handler.postDelayed(tick,3000);}
    void stop(){handler.removeCallbacksAndMessages(null);worker.shutdownNow();}
    private void check(){
        if(running||Settings.System.getInt(context.getContentResolver(),"codex_home_guard_enabled",1)!=1)return;
        running=true;
        worker.execute(()->{
            try{
                String command=exec("/system/bin/getprop","vendor.xgimi.system.debug.cmd");
                String state=exec("/system/bin/getprop","init.svc.debugserver");
                if("running".equals(state)||(!command.isEmpty()&&!APPLY.equals(command)))return;
                exec("/system/bin/setprop","vendor.xgimi.system.debugserver","0");
                exec("/system/bin/setprop","vendor.xgimi.system.debug.cmd",APPLY);
                exec("/system/bin/setprop","vendor.xgimi.system.debugserver","1");
            }catch(Exception e){Settings.System.putString(context.getContentResolver(),"codex_home_guard_loader_error",e.getClass().getSimpleName());}
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
