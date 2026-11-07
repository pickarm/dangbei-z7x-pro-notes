const assert=require('node:assert/strict');
const parse=require('./commands.js');
for(const [text,want] of [
 ['你给我把光源亮度调到三',{kind:'light',value:3}],
 ['亮度调到10',{kind:'light',value:10}],
 ['亮度设为两档',{kind:'light',value:2}],
 ['调暗一点',{kind:'relative',delta:-1}],
 ['太暗了',{kind:'relative',delta:1}],
 ['光源最高亮度',{kind:'light',value:10}],
 ['请打开 Apple TV',{kind:'app',name:'appletv'}],
 ['打开设置吧',{kind:'app',name:'设置'}],
 ['不要打开AppleTV',null], ['别调亮度',null],
 ['图像亮度调到三',null], ['今天的天气',null]
 ,['切换到 HDMI 1',{kind:'source',target:'hdmi',port:1}]
 ,['打开HDMI二',{kind:'source',target:'hdmi',port:2}]
 ,['HDMI一',{kind:'source',target:'hdmi',port:1}]
 ,['把信号源切换到HDMI二号接口',{kind:'source',target:'hdmi',port:2}]
 ,['切换到信号源HDMI1',{kind:'source',target:'hdmi',port:1}]
 ,['请返回系统',{kind:'source',target:'system'}]
 ,['切回系统桌面',{kind:'source',target:'system'}]
 ,['切到电脑',{kind:'source',target:'connected'}]
 ,['进入笔记本',{kind:'source',target:'connected'}]
 ,['打开系统设置',{kind:'app',name:'系统设置'}]
 ,['不要切换到HDMI1',null]
 ,['HDMI是什么',null]
 ,['切到HDMI1然后打开AppleTV',null]
]) assert.deepEqual(parse(text),want,text);
for(const text of ['亮度调到0','亮度调到11','亮度调到一百','光源调到百分之三十']) assert.equal(parse(text).kind,'error',text);
for(const text of ['打开HDMI3','切到HDMI零','切换到HDMI10','切换信号源']) assert.equal(parse(text).kind,'error',text);
console.log('Command parsing: 33 meaningful cases passed');
