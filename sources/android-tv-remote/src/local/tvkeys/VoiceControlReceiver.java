package local.tvkeys;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
public final class VoiceControlReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context, Intent intent) {
        if("local.tvkeys.SET_VOICE_ENABLED".equals(intent.getAction())) {
            VoiceLoader.enabled(context,intent.getBooleanExtra("enabled",false));
        }
    }
}
