import android.content.ComponentName;
import android.content.pm.ActivityInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.system.Os;

public final class LauncherComponents {
    public static void main(String[] args) throws Exception {
        if(Os.getuid()!=0)throw new SecurityException("Root launch required");
        Os.setgid(1000);Os.setuid(1000);
        Object pm=Class.forName("android.app.AppGlobals").getMethod("getPackageManager").invoke(null);
        Class<?> api=Class.forName("android.content.pm.IPackageManager");
        String pkg="com.dangbei.leard.leradlauncher";
        if(args.length==0||"inspect".equals(args[0])){
            PackageInfo info=(PackageInfo)api.getMethod("getPackageInfo",String.class,long.class,int.class).invoke(pm,pkg,(long)(PackageManager.GET_ACTIVITIES|PackageManager.MATCH_DISABLED_COMPONENTS),0);
            for(ActivityInfo item:info.activities){
                if(item.name.contains("splash")||item.name.contains(".main.")||item.name.endsWith("MainActivity")){
                    ComponentName component=new ComponentName(pkg,item.name);
                    System.out.println(component.flattenToString()+" state="+api.getMethod("getComponentEnabledSetting",ComponentName.class,int.class).invoke(pm,component,0));
                }
            }
            return;
        }
        if(args.length<3)throw new IllegalArgumentException("set <component> <state>");
        ComponentName component=ComponentName.unflattenFromString(args[1]);
        if(component==null||!pkg.equals(component.getPackageName()))throw new IllegalArgumentException("Unexpected package");
        int state=Integer.parseInt(args[2]);
        if(state!=0&&state!=2)throw new IllegalArgumentException("Only default/disabled states supported");
        api.getMethod("setComponentEnabledSetting",ComponentName.class,int.class,int.class,int.class).invoke(pm,component,state,PackageManager.DONT_KILL_APP,0);
        int actual=(Integer)api.getMethod("getComponentEnabledSetting",ComponentName.class,int.class).invoke(pm,component,0);
        if(actual!=state)throw new IllegalStateException("Readback mismatch");
        System.out.println(component.flattenToString()+" state="+actual+" DONT_KILL_APP");
    }
}
