Java.perform(function () {
  const App = Java.use('android.app.ActivityThread').currentApplication();
  const Settings = Java.use('android.provider.Settings$System');
  const resolver = App.getContentResolver();
  const Manager = Java.use('android.app.ActivityManager');
  const stop = Manager.forceStopPackage.overload('java.lang.String');
  let count = 0;
  stop.implementation = function (name) {
    const pkg = String(name);
    if (Settings.getInt(resolver, 'codex_home_guard_enabled', 1) === 1 &&
        (pkg === 'local.tvkeys' || pkg === 'com.dangbei.tvlauncher')) {
      Settings.putString(resolver, 'codex_home_guard_last_blocked', pkg);
      Settings.putInt(resolver, 'codex_home_guard_block_count', ++count);
      return;
    }
    return stop.call(this, name);
  };
  Settings.putString(resolver, 'codex_home_guard_pid', String(Java.use('android.os.Process').myPid()));
  Settings.putInt(resolver, 'codex_home_guard_version', 1);
  Settings.putString(resolver, 'codex_home_guard_status', 'healthy');
});
