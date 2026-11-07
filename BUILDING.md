# 源码与构建

这是实际使用代码的整理归档。构建成功只说明代码能编译，不表示另一台投影仪已经完成适配。没有自动安装、自动 root 或自动改显示设置的构建步骤。

## Windows 屏幕联动

Windows 上使用系统自带的 .NET Framework 4.x C# 编译器：

```powershell
powershell -File tools/build-windows.ps1
```

输出到忽略目录 `artifacts/windows/Projector-Display-Router.exe`。程序启动后会修改显示布局；请先阅读[屏幕联动](docs/04-display-router.md)。构建过程不会运行它。

把 `examples/router-settings.example.json` 复制到输出程序旁并改名为 `router-settings.json`。示例中的密钥和设备编号是占位符，需要自行生成并与 Android 端私有 `display-pair.json` 一致；显示器标记、信号源编号要在自己的设备上校准。示例 `computerSource=-999` 故意不能匹配电脑输入，防止未经校准就切屏。源码保留了原工作区回退路径，公开使用应始终提供程序同目录配置。

## Android 遥控优化

源码在 `sources/android-tv-remote/`，包名 `local.tvkeys`、版本 2.3。原构建链为 JDK 21、Android API 28 平台 jar、SDK Build Tools 的 aapt / D8 / zipalign / apksigner，Java 源码级别 8、DEX 最低 API 23。设备实际运行 Android 13。

手动构建顺序：

1. 用 `javac -encoding UTF-8 -source 8 -target 8` 编译 `src`，classpath 指向自己的 `android.jar`。
2. 用 D8 将 class 转为 `classes.dex`。
3. 用 aapt 将 manifest 和 `res` 打成未签名 APK，把 `classes.dex` 放入 APK 根目录。
4. zipalign 后用自己的密钥签名，并用 apksigner 验证。

这里不附原签名私钥。换密钥不能直接覆盖更新已有安装；**不要为了安装新签名版本直接卸载旧程序**，先按[恢复说明](docs/07-recovery.md)恢复遥控键位。无障碍授权、开机允许、HOME 规则、专用键位文件、语音载入所需文件和局域网配对都属于设备配置，编译 APK 不会自动完成这些事情。

`tools/HomeConfig.java` 是需要 root 启动的单独辅助类，不编入 APK；调用系统隐藏接口，必须核对固件接口签名。

## 语音增强

可独立测试语义解析：

```text
node sources/voice-enhancement/test-commands.js
```

运行时补丁另依赖 Frida 17.26.0 android-arm、frida-java-bridge 7.0.13 和 esbuild 0.28.2。`patch-body.js` 是补丁主体，载入前需要引入 bridge 并绑定 `Java`，再打包到单一脚本；`reapply.sh` 则依赖本机 `/data/local/codex-voice` 下的外部工具与策略备份。依赖和二进制未放进仓库。ARMv7 兼容处理见[语音记录](docs/03-voice.md)，因此仅安装 npm 原版依赖不保证能在这台设备运行。

语音切信号源属于 v5 新增代码，解析测试通过不等于实体语音验收通过。亮度、打开应用、退出面板的已有实机结论单独列在验证表里。

## 本次公开前的检查

发布时运行语义解析用例、编译 Android Java 源码和 Windows C# 源码，并检查文档内部链接与公开文件清单。没有为了发布再次改动投影仪，也没有把这些静态检查写成一次新的整机重启验收。
