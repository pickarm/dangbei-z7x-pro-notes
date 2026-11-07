package local.tvkeys;

import android.content.Context;
import android.os.IBinder;
import android.os.Parcel;
import android.util.Base64;
import org.json.JSONObject;
import java.io.File;
import java.io.FileInputStream;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.SocketTimeoutException;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/** Answers authenticated status requests only; never changes a signal source. */
final class DisplayLink implements Runnable {
    static volatile DisplayLink active;
    private final Context context;
    private volatile boolean stopped;
    private volatile DatagramSocket socket;
    private volatile String status="准备中", lastSource="unknown";
    private volatile int requests;
    private Thread thread;
    private long configTime=-1;
    private byte[] key;
    private String device;
    private int port;

    DisplayLink(Context c){context=c.getApplicationContext();}
    static boolean enabled(Context c){return c.getSharedPreferences("settings",0).getBoolean("display_link_enabled",true);}
    static void enabled(Context c,boolean value){c.getSharedPreferences("settings",0).edit().putBoolean("display_link_enabled",value).apply();}
    static String status(Context c){if(!enabled(c))return "已关闭";return active==null?"等待后台服务":active.status;}
    void start(){active=this;thread=new Thread(this,"DisplayLink");thread.setDaemon(true);thread.start();}
    void stop(){stopped=true;DatagramSocket current=socket;if(current!=null)current.close();if(thread!=null)thread.interrupt();active=null;}
    String diagnostic(){return "DisplayLink status="+status+" requests="+requests+" source="+lastSource;}

    private boolean load() throws Exception {
        File file=new File(context.getFilesDir(),"display-pair.json");
        if(!file.isFile()){status="未配对";return false;}
        if(file.lastModified()==configTime && key!=null)return true;
        if(file.length()>2048)throw new IllegalArgumentException("Pair file too large");
        byte[] bytes=new byte[(int)file.length()];
        try(FileInputStream in=new FileInputStream(file)){
            int n=0,count;while(n<bytes.length&&(count=in.read(bytes,n,bytes.length-n))>0)n+=count;
            if(n!=bytes.length)throw new IllegalArgumentException("Incomplete pair file");
        }
        JSONObject config=new JSONObject(new String(bytes,StandardCharsets.UTF_8));
        byte[] newKey=Base64.decode(config.getString("key"),Base64.NO_WRAP);
        if(newKey.length!=32)throw new IllegalArgumentException("Invalid pair key");
        device=config.getString("device");port=config.getInt("port");
        if(!device.matches("[a-f0-9]{32}")||port<1024||port>65535)throw new IllegalArgumentException("Invalid pair config");
        key=newKey;configTime=file.lastModified();return true;
    }
    private String mac(String value)throws Exception{
        Mac mac=Mac.getInstance("HmacSHA256");mac.init(new SecretKeySpec(key,"HmacSHA256"));
        StringBuilder text=new StringBuilder();for(byte b:mac.doFinal(value.getBytes(StandardCharsets.UTF_8)))
            text.append(String.format(java.util.Locale.ROOT,"%02x",b&255));return text.toString();
    }
    private String playing() {
        try{return String.valueOf(Class.forName("android.os.SystemProperties").getMethod("get",String.class,String.class)
                .invoke(null,"vendor.xgimi.hdmi.playing.state","unknown"));}
        catch(Exception e){return "unknown";}
    }
    private int nativeSource() {
        String descriptor="xgimi.hardware.gmpf.IGmTvManager";
        Parcel request=Parcel.obtain(),reply=Parcel.obtain();
        try{
            IBinder binder=(IBinder)Class.forName("android.os.ServiceManager").getMethod("getService",String.class)
                    .invoke(null,descriptor+"/default");
            if(binder==null)return -1;
            request.writeInterfaceToken(descriptor);
            if(!binder.transact(4,request,reply,0))return -1;
            reply.readException();return reply.readInt();
        }catch(Exception e){return -1;}
        finally{request.recycle();reply.recycle();}
    }
    @Override public void run(){
        while(!stopped){
            try{
                if(!load()){
                    DatagramSocket current=socket;if(current!=null){current.close();socket=null;}
                    Thread.sleep(1000);continue;
                }
                if(socket==null){socket=new DatagramSocket(port);socket.setSoTimeout(1500);status="等待电脑";}
                byte[] buffer=new byte[2048];DatagramPacket packet=new DatagramPacket(buffer,buffer.length);
                try{socket.receive(packet);}catch(SocketTimeoutException e){continue;}
                if(!packet.getAddress().isSiteLocalAddress()&&!packet.getAddress().isLoopbackAddress())continue;
                if(packet.getLength()>=buffer.length)continue;
                try{
                    JSONObject query=new JSONObject(new String(packet.getData(),0,packet.getLength(),StandardCharsets.UTF_8));
                    String nonce=query.optString("nonce"), supplied=query.optString("mac");
                    if(query.optInt("v")!=1||!nonce.matches("[a-f0-9]{32}"))continue;
                    if(!MessageDigest.isEqual(mac("query|"+nonce).getBytes(StandardCharsets.US_ASCII),
                            supplied.getBytes(StandardCharsets.US_ASCII)))continue;
                    boolean on=enabled(context);
                    String playing=on?playing():"disabled";int source=on?nativeSource():-1;
                    JSONObject response=new JSONObject();response.put("v",1);response.put("nonce",nonce);
                    response.put("device",device);response.put("playing",playing);response.put("source",source);
                    response.put("mac",mac("state|"+nonce+"|"+device+"|"+playing+"|"+source));
                    byte[] encoded=response.toString().getBytes(StandardCharsets.UTF_8);
                    socket.send(new DatagramPacket(encoded,encoded.length,packet.getAddress(),packet.getPort()));
                    requests++;lastSource=playing+":"+source;status=on?"正常":"已关闭";
                }catch(Exception ignored){/* Invalid network messages do not affect the service. */}
            }catch(Exception error){
                if(stopped)break;
                status="连接待恢复";
                DatagramSocket current=socket;if(current!=null)current.close();socket=null;
                try{Thread.sleep(2000);}catch(InterruptedException ignored){}
            }
        }
    }
}
