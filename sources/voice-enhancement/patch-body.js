Java.perform(function(){
  var SystemSettings=Java.use('android.provider.Settings$System');
  var App=Java.use('com.dangbei.util.ApplicationUtil');
  var Rocky=Java.use('com.dangbei.rocky.library.core.RockyManager');
  var Intent=Java.use('android.content.Intent');
  var Parcel=Java.use('android.os.Parcel');
  var ServiceManager=Java.use('android.os.ServiceManager');
  var JProcess=Java.use('android.os.Process');
  var ctx=App.getInstance().getContext();
  var resolver=ctx.getContentResolver();
  var session=0,lastSession=-1,lastText='',lastAt=0;
  var pendingClose=-1,closeTimer=null;
  var descriptor='xgimi.hardware.gmpf.IDisplayManager';
  function status(value){SystemSettings.putString(resolver,'codex_voice_status',String(value));console.log('CODEX_VOICE '+value);}
  function active(){return String(SystemSettings.getString(resolver,'codex_voice_enabled'))==='1';}
  function lightCall(code,value){
    var binder=ServiceManager.getService(descriptor+'/default');
    if(binder===null)throw Error('光源服务不可用');
    var request=Parcel.obtain(),reply=Parcel.obtain();
    try{
      request.writeInterfaceToken(descriptor);
      if(value!==undefined) request.writeByte(value);
      if(!binder.transact(code,request,reply,0)) throw Error('光源服务未响应');
      reply.readException();
      return code===7?reply.readByte():null;
    }finally{request.recycle();reply.recycle();}
  }
  function closePanel(expectedSession){
    if(expectedSession!==session || pendingClose!==expectedSession)return;
    pendingClose=-1;
    if(closeTimer!==null){clearTimeout(closeTimer);closeTimer=null;}
    Java.scheduleOnMainThread(function(){
      try{
        var manager=Rocky.getInstance();
        var panel=Java.use('com.dangbei.rocky_ui.RockyViewBar').getInstance();
        panel.isForceExit.value=true;
        manager.onRequestHideUI(0);
        manager.voiceBack();
        panel.isForceExit.value=true;
        panel.removeView(0);
        SystemSettings.putString(resolver,'codex_voice_panel','closing');
        setTimeout(function(){Java.scheduleOnMainThread(function(){
          SystemSettings.putString(resolver,'codex_voice_panel',panel.isShow.value?'still-visible':'closed');
        });},700);
      }catch(e){console.log('CODEX_CLOSE_ERROR '+e);}
    });
  }
  function feedback(message){
    var expectedSession=session;
    pendingClose=expectedSession;
    if(closeTimer!==null)clearTimeout(closeTimer);
    closeTimer=setTimeout(function(){closePanel(expectedSession);},4000);
    Java.scheduleOnMainThread(function(){
      try{
        var toast=Java.use('android.widget.Toast');
        toast.makeText(ctx,Java.use('java.lang.String').$new(message),0).show();
        Rocky.getInstance().playTTS.overload('java.lang.String','boolean').call(Rocky.getInstance(),message,false);
      }catch(e){console.log('CODEX_FEEDBACK_ERROR '+e);}
    });
  }
  function normalize(value){return String(value).toLowerCase().replace(/[\s·:：_\-]/g,'');}
  function switchSource(command){
    var target=command.target,port=command.port;
    var manager=Java.cast(ctx.getSystemService('tv_input'),Java.use('android.media.tv.TvInputManager'));
    var list=manager.getTvInputList(),inputs={};
    for(var i=0;i<list.size();i++){
      var info=Java.cast(list.get(i),Java.use('android.media.tv.TvInputInfo'));
      var id=String(info.getId());
      var match=id.match(/^com\.xgimi\.tvinput\/\.inputservice\.Hdmi([12])TvInputService\/HW[12]$/);
      if(match)inputs[Number(match[1])]={id:id,connected:manager.getInputState(id)===0};
    }
    if(target==='connected'){
      var ports=Object.keys(inputs).filter(function(key){return inputs[key].connected;});
      if(ports.length!==1){status('source:needs-port');feedback(ports.length===0?'没有检测到已连接的HDMI信号，请检查电脑，或直接指定HDMI一、HDMI二':'检测到多个HDMI信号，请说HDMI一或者HDMI二');return;}
      port=Number(ports[0]);target='hdmi';
    }
    if(target==='hdmi' && !inputs[port]){status('source:not-supported');feedback('没有找到这个HDMI接口');return;}
    Java.scheduleOnMainThread(function(){
      try{
        var intent;
        if(target==='system'){
          intent=Intent.$new('android.intent.action.MAIN');
          intent.addCategory('android.intent.category.HOME');
          intent.setClassName('com.dangbei.tvlauncher','com.dangbei.launcher.ui.main.MainActivity');
        }else{
          var uri=Java.use('android.media.tv.TvContract').buildChannelUriForPassthroughInput(inputs[port].id);
          intent=Intent.$new('com.xgimi.action.hdmiPlayer');
          intent.setClassName('com.xgimi.xhplayer','com.xgimi.xhplayer.InputActivity');
          intent.setDataAndType(uri,'vnd.android.cursor.item/channel');
        }
        intent.addFlags(0x10000000);
        ctx.startActivity(intent);
        status('source:requested:'+(target==='system'?'system':'hdmi'+port));
        feedback(target==='system'?'正在返回系统桌面':'正在切换到HDMI'+(port===1?'一':'二'));
      }catch(e){status('source:failed');feedback('信号源切换失败，请重试');console.log('CODEX_SOURCE_ERROR '+e);}
    });
  }
  function listApps(){
    var pm=ctx.getPackageManager(),byPackage={};
    ['android.intent.category.LEANBACK_LAUNCHER','android.intent.category.LAUNCHER'].forEach(function(category){
      var query=Intent.$new('android.intent.action.MAIN');query.addCategory(category);
      var list=pm.queryIntentActivities(query,0);
      for(var i=0;i<list.size();i++){
        var item=Java.cast(list.get(i),Java.use('android.content.pm.ResolveInfo'));
        var info=item.activityInfo.value;
        var pkg=String(info.packageName.value);
        if(byPackage[pkg])continue;
        byPackage[pkg]={pkg:pkg,activity:String(info.name.value),label:String(item.loadLabel(pm))};
      }
    });
    return Object.keys(byPackage).map(function(k){return byPackage[k];});
  }
  function launchApp(name){
    var key=normalize(name),apps=listApps();
    var aliases={appletv:'com.apple.atve.androidtv.appletv','苹果tv':'com.apple.atve.androidtv.appletv','苹果电视':'com.apple.atve.androidtv.appletv','谷歌商店':'com.android.vending','googleplay':'com.android.vending','谷歌play商店':'com.android.vending','设置':'com.android.newsettings','系统设置':'com.android.newsettings','当贝桌面':'com.dangbei.tvlauncher','桌面':'com.dangbei.tvlauncher'};
    var target=aliases[key],matches=apps.filter(function(a){return target?a.pkg===target:normalize(a.label)===key;});
    if(!target && matches.length===0 && key.length>=2)matches=apps.filter(function(a){return normalize(a.label).indexOf(key)>=0;});
    if(matches.length===0){feedback('没有找到这个已安装应用，请说应用的完整名称');status('app:not-found');return;}
    if(matches.length>1){feedback('找到多个应用，请说更完整的名称');status('app:ambiguous');return;}
    var chosen=matches[0];
    Java.scheduleOnMainThread(function(){
      try{
        var intent=Intent.$new('android.intent.action.MAIN');
        intent.setClassName(chosen.pkg,chosen.activity);intent.addFlags(0x10000000);
        try{Java.use('com.dangbei.rocky_ui.RockyViewBar').getInstance().removeView(0);}catch(ignore){}
        ctx.startActivity(intent);status('app:opened:'+chosen.pkg);feedback('已打开'+chosen.label);
      }catch(e){status('app:failed');feedback('应用打开失败');console.log('CODEX_APP_ERROR '+e);}
    });
  }
  function handle(text,end){
    var command=parseVoiceCommand(text);
    if(command===null)return false;
    if(!end)return active();
    if(!active()){
      if(String(SystemSettings.getString(resolver,'codex_voice_observe'))==='1')status('heard:'+command.kind+(command.value!==undefined?':'+command.value:''));
      return false;
    }
    var now=Date.now();
    if(lastSession===session && lastText===String(text) && now-lastAt<2500)return true;
    lastSession=session;lastText=String(text);lastAt=now;
    try{
      if(command.kind==='error'){feedback(command.message);status('command:invalid');return true;}
      if(command.kind==='source'){switchSource(command);return true;}
      if(command.kind==='app'){launchApp(command.name);return true;}
      var current=lightCall(7);
      var desired=command.kind==='relative'?Math.max(1,Math.min(10,current+command.delta)):command.value;
      if(desired<1||desired>10)throw Error('档位不合法');
      lightCall(6,desired);
      var actual=lightCall(7);
      if(actual!==desired)throw Error('读回不一致');
      status('light:ok:'+actual);feedback('光源亮度已调到'+actual+'档');
    }catch(e){status('command:failed');feedback('操作没有成功，请重试');console.log('CODEX_COMMAND_ERROR '+e);}
    return true;
  }
  var start=Rocky.onVoiceStart.overload();
  start.implementation=function(){session++;pendingClose=-1;return start.call(this);};
  var ttsStatus=Rocky.onTTSPlayStatus.overload('java.lang.String');
  ttsStatus.implementation=function(state){
    var result=ttsStatus.call(this,state);
    if(String(state)==='end' && pendingClose===session){
      var expectedSession=session;
      setTimeout(function(){closePanel(expectedSession);},150);
    }
    return result;
  };
  var iat=Rocky.isOutputIatMode.overload('java.lang.String','boolean');
  iat.implementation=function(text,end){if(handle(text,end))return true;return iat.call(this,text,end);};
  var input=Rocky.startAiInput.overload('java.lang.String','java.lang.String','java.lang.String');
  input.implementation=function(text,sid,data){if(handle(text,true))return true;return input.call(this,text,sid,data);};
  SystemSettings.putString(resolver,'codex_voice_patch_pid',String(JProcess.myPid()));
  SystemSettings.putString(resolver,'codex_voice_patch_version','5');
  status('ready:v5:pid='+JProcess.myPid());
});
