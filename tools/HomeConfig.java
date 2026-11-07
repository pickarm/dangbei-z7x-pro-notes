import android.content.ComponentName;
import android.content.Intent;
import android.content.IntentFilter;
import android.system.Os;
import java.lang.reflect.Method;

public class HomeConfig {
    public static void main(String[] args) throws Exception {
        if (Os.getuid() != 0) throw new SecurityException("Root launcher required");
        Os.setgid(1000);
        Os.setuid(1000);
        Object pm = Class.forName("android.app.AppGlobals").getMethod("getPackageManager").invoke(null);
        Class<?> api = Class.forName("android.content.pm.IPackageManager");
        System.out.println("Binder caller uid=" + Os.getuid());
        if (args.length == 0 || args[0].equals("inspect")) {
            for (Method m : api.getMethods())
                if (m.getName().contains("PersistentPreferred")) System.out.println(m);
            return;
        }
        if (args[0].equals("undo")) {
            api.getMethod("clearPackagePersistentPreferredActivities", String.class, int.class)
                .invoke(pm, "com.dangbei.tvlauncher", 0);
            System.out.println("Removed persistent home preference for Dangbei Launcher");
            return;
        }
        if (!args[0].equals("apply")) throw new IllegalArgumentException("Unknown command");
        Method add = api.getMethod("addPersistentPreferredActivity", IntentFilter.class, ComponentName.class, int.class);
        ComponentName target = new ComponentName("com.dangbei.tvlauncher", "com.dangbei.launcher.ui.main.MainActivity");
        for (boolean includeDefault : new boolean[] {false, true}) {
            IntentFilter f = new IntentFilter(Intent.ACTION_MAIN);
            f.addCategory(Intent.CATEGORY_HOME);
            if (includeDefault) f.addCategory(Intent.CATEGORY_DEFAULT);
            add.invoke(pm, f, target, 0);
            System.out.println("Saved persistent HOME, DEFAULT=" + includeDefault + " -> " + target.flattenToString());
        }
    }
}
