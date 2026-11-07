package local.tvkeys;

import android.content.Context;
import android.media.AudioManager;

final class VolumeControl {
    static boolean enabled(Context c) { return c.getSharedPreferences("settings", 0).getBoolean("fine_volume", true); }
    static void enabled(Context c, boolean value) { c.getSharedPreferences("settings", 0).edit().putBoolean("fine_volume", value).apply(); }
    static AudioManager audio(Context c) { return (AudioManager)c.getSystemService(Context.AUDIO_SERVICE); }
    static int change(Context c, int delta) {
        AudioManager a = audio(c);
        int next = Math.max(0, Math.min(a.getStreamMaxVolume(AudioManager.STREAM_MUSIC), a.getStreamVolume(AudioManager.STREAM_MUSIC) + delta));
        a.setStreamVolume(AudioManager.STREAM_MUSIC, next, 0);
        return a.getStreamVolume(AudioManager.STREAM_MUSIC);
    }
}
