# 源码与构建

这是实际使用代码的整理归档。构建成功只说明代码能编译，不表示另一台投影仪已经完成适配。没有自动安装、自动 root 或自动改显示设置的构建步骤。

## Windows 屏幕联动

在 Windows 的 PowerShell 中从仓库根目录运行构建脚本，脚本使用系统自带的 .NET Framework 4.x C# 编译器。

```powershell
& ./tools/build-windows.ps1
```

输出到忽略目录 `artifacts/windows/Projector-Display-Router.exe`。程序启动后会修改显示布局；请先阅读[屏幕联动](docs/04-display-router.md)。构建过程不会运行它。

把 `examples/router-settings.example.json` 复制到输出程序旁并改名为 `router-settings.json`。示例中的密钥和设备编号是占位符，需要自行生成并与 Android 端私有 `display-pair.json` 一致。显示器标记、具体 HDMI 端口的信号源编号要在自己的设备上校准。示例 `computerSource=-999` 表示未校准，保留显示器输出。部署使用程序同目录的配置。

本机计划任务从 `C:\ProgramData\CodexProjectorDisplayRouter` 启动。确认配置并保存显示器基线后，可用[任务安装脚本](tools/Install-Scheduled-Router.ps1)注册和启动，用[任务停用脚本](tools/Disable-Display-Router.ps1)停止。启动条件和恢复方式见[屏幕联动](docs/04-display-router.md)。

程序同目录的 `display-mode-profiles.json` 保存完整设备路径和可信单屏模式，属于本机私有配置，提交时应排除。每个设备只在没有记录时保存模式，本次进程自动回退选出的模式不会写入可信缓存。为新设备建立缓存时，先在 Windows 设好单屏及所需分辨率和刷新率，再启动或重启联动后台。要更换已有模式，先停联动、备份并移走旧缓存，再按相同步骤建立新记录。

从仓库根目录运行离线测试脚本。脚本用 .NET Framework 编译并运行[显示模式测试](tests/windows-display/DisplayModeProfiles.Tests.cs)和[路由策略测试](tests/windows-display/RouterPolicy.Tests.cs)，覆盖模式数据、路径重绑、应用旗标、缓存重载及信号源判定，输出到 `artifacts/tests`。

```powershell
& ./tools/test-windows.ps1
```

## Android 遥控优化

源码在 `sources/android-tv-remote/`，包名 `local.tvkeys`、版本 2.6.3 / versionCode 13。原构建链为 JDK 21、Android API 28 平台 jar、SDK Build Tools 的 aapt / D8 / zipalign / apksigner，Java 源码级别 8、DEX 最低 API 23。设备实际运行 Android 13。

手动构建顺序：

1. 用 `javac -encoding UTF-8 -source 8 -target 8` 编译 `src`，classpath 指向自己的 `android.jar`。
2. 用 D8 将 class 转为 `classes.dex`。
3. 用 aapt 将 manifest 和 `res` 打成未签名 APK，把 `classes.dex` 放入 APK 根目录。
4. zipalign 后用自己的密钥签名，并用 apksigner 验证。

这里不附原签名私钥。换密钥不能直接覆盖更新已有安装；**不要为了安装新签名版本直接卸载旧程序**，先按[恢复说明](docs/07-recovery.md)恢复遥控键位。无障碍授权、开机允许、HOME 规则、专用键位文件、语音载入所需文件和局域网配对都属于设备配置，编译 APK 不会自动完成这些事情。

[HomeConfig.java](tools/HomeConfig.java) 和 [LauncherComponents.java](tools/LauncherComponents.java) 是分别用于持久 HOME 选择和原厂首页组件设置的工具，通过 root 启动。两者调用系统隐藏接口，编译和运行前按固件核对签名。

## 内置喇叭音量曲线

曲线由遥控优化的后台加载器检查。归档包含[原生曲线模板](sources/volume-curve/volume-curve.js.in)、[重新加载脚本](sources/volume-curve/reapply.sh)和[安全停用脚本](sources/volume-curve/disable.sh)。这些文件使用独立的本机配置、外部 Frida 工具和音频 HAL 接口。APK 编译完成后仍需核对固件接口及音频输出目标。正式加载和安全停用见[音量记录](docs/02-remote-home-background.md)及[恢复方法](docs/07-recovery.md)。

## Etna 定向防护

[防护脚本](sources/home-guard/guard-body.js)由加载器在需要时加载至 Etna 进程。[重新加载脚本](sources/home-guard/reapply.sh)依赖本机配备 Frida 和策略备份。这里记录与部署一致的拦截范围，具体处理见[桌面和后台记录](docs/02-remote-home-background.md)。

## 语音增强

可独立测试语义解析：

```text
node sources/voice-enhancement/test-commands.js
```

运行时补丁另依赖 Frida 17.26.0 android-arm、frida-java-bridge 7.0.13 和 esbuild 0.28.2。`patch-body.js` 是补丁主体，载入前需要引入 bridge 并绑定 `Java`，再打包到单一脚本；`reapply.sh` 则依赖本机 `/data/local/codex-voice` 下的外部工具与策略备份。依赖和二进制未放进仓库。ARMv7 兼容处理见[语音记录](docs/03-voice.md)，因此仅安装 npm 原版依赖不保证能在这台设备运行。

语音切信号源属于 v5 新增代码，解析测试通过不等于实体语音验收通过。亮度、打开应用、退出面板的已有实机结论单独列在验证表里。

## 发布验证

发布检查包括语义解析用例、Android Java 与 Windows C# 编译、文档内部链接及公开文件清单。实体操作信号源、喇叭听感和整机重启结果另见[验证状态](docs/00-status.md)。
