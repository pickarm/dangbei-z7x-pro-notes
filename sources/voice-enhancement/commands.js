'use strict';
function parseVoiceCommand(input) {
  var text=String(input||'').toLowerCase().replace(/[\s，。？！,.?!、]/g,'');
  if(!text || /不要|别给|别调|别打开|取消|停止调整|不需要/.test(text)) return null;
  text=text.replace(/^(?:请|麻烦|你|给我|帮我)+/,'');
  if(/^(?:返回|回到|切回|切到|切换到|切换至|进入|打开)(?:投影仪系统|投影系统|安卓系统|系统桌面|系统|当贝桌面|桌面)(?:吧|一下|谢谢)?$/.test(text))
    return {kind:'source',target:'system'};
  var port=text.match(/^(?:(?:把|信号源|切换|切回|切|换|打开|进入|选择|到|至|为))*hdmi([0-9零一二两三四五六七八九十]+)(?:号|接口|输入|信号源|口|一下|吧|谢谢)*$/);
  if(port){
    var number={'一':1,'二':2,'两':2}[port[1]]||Number(port[1]);
    return number===1||number===2?{kind:'source',target:'hdmi',port:number}:
      {kind:'error',message:'这台投影仪支持HDMI一和HDMI二，请指定其中一个'};
  }
  if(/^(?:切换到|切到|切换至|切回|进入|打开)(?:电脑|笔记本|电脑信号|电脑接口)(?:吧|一下|谢谢)?$/.test(text))
    return {kind:'source',target:'connected'};
  if(/^(?:切换|选择|打开)信号源(?:吧|一下)?$/.test(text))
    return {kind:'error',message:'请说切换到HDMI一、HDMI二，或者返回系统'};
  var open=text.match(/^(?:打开|启动|运行)(.+?)(?:一下|吧|谢谢)?$/);
  if(open) return {kind:'app',name:open[1]};
  if(/画面亮度|图像亮度|对比度|色温/.test(text)) return null;
  var light=/亮度|光源|调亮|调暗|太亮了|太暗了/.test(text);
  if(!light) return null;
  if(/百分|%/.test(text)) return {kind:'error',message:'请用一到十档设置光源亮度'};
  if(/最大|最高|最亮/.test(text)) return {kind:'light',value:10};
  if(/最小|最低|最暗/.test(text)) return {kind:'light',value:1};
  if(/调亮|亮一点|亮一些|提高|增加|太暗了/.test(text)) return {kind:'relative',delta:1};
  if(/调暗|暗一点|暗一些|降低|减小|减少|太亮了/.test(text)) return {kind:'relative',delta:-1};
  var m=text.match(/([0-9零〇一二两三四五六七八九十百]+)(?:档|级|吧|一下|谢谢)*$/);
  if(!m) return {kind:'error',message:'请说光源亮度调到几档，一到十档'};
  var digits={'零':0,'〇':0,'一':1,'二':2,'两':2,'三':3,'四':4,'五':5,'六':6,'七':7,'八':8,'九':9,'十':10};
  var n=/^\d+$/.test(m[1])?Number(m[1]):digits[m[1]];
  if(!Number.isInteger(n)||n<1||n>10) return {kind:'error',message:'光源亮度只能设置一到十档'};
  return {kind:'light',value:n};
}
if(typeof module!=='undefined') module.exports=parseVoiceCommand;
