# 音量、Home 与后台恢复

## 音量显示不等于真实音量范围

初看音量只有十几档。进一步读取 AudioManager 后发现真实媒体音量已经是 0～100，但原遥控路径按一次会变多格，原厂音量条又把它显示成约 20 档。

只增加一层无障碍音量处理会与原系统处理叠加，出现步长仍然不对。尝试过窗口管理器资源覆盖，最后完整撤销，没有保留那条路线。

最终只改实际当贝蓝牙遥控器的专用映射：

```text
vendor 0x1d5a / product 0xc081
Linux 114 → F11 → 媒体音量 -1
Linux 115 → F12 → 媒体音量 +1
```

设备侧的持久文件为：

```text
/data/system/devices/idc/Vendor_1d5a_Product_c081.idc
/data/system/devices/keylayout/CodexDangbeiRemote.kl
```

IDC 指向自定义 layout，KL 其余按键沿用备份的原厂内容。应用只接管该厂家/产品的 F11/F12，写入真实音量并显示自己的 `音量 x / 100` 浮层。短按一次变 1；长按重复，松开停止。0 是静音。

备份原 IDC 是否存在、原 KL 和系统配置后再写入。不要只安装 APK 就期待正常，也不要只卸载 APK 而留下专用键位。

## 内置喇叭的底层增益

逐格按键修好后，用户仍听到低档偏响和调节时忽大忽小。播放时追踪 `MI_AOUT_SetVolume` 和 `MI_AOUT_GetVolumeDb`，对 5→6→5→6 分别在 0、100、500、1500、2500 ms 取样。同一档位的读数在这些时间点保持一致，6 档的增益反而低于 5 档。这台设备上，原 HAL 曲线对扩展后的档位存在反转。

先试用内置喇叭专用的单调曲线。0 继续走原静音路径，1～100 对应 −75 dB 到 0 dB，约每格 0.758 dB，经过驱动量化后仍逐格递增。遥控器在 8～21 一带往返时写入与 HAL 读回一致。用户确认“平稳了，低档也合适”后，才启用随开机恢复的正式方案。这里记录的是增益和听感，未测量声压或不同片源的 LUFS。[脱敏测试数据](../data/volume-curve-validation.json)

正式加载时先保存 Android 档位并静音，加载完成、策略恢复且确认 ready 后再恢复档位。加载失败保留静音。同一进程的失败会留状态，避免连续重复注入。正常健康检查无需反复静音。使用 `boot_id` 和音频 HAL PID 核对是否需要重载。两次完整重启分别恢复 29 和 30，恰好两次 HAL PID 相同，新启动标识仍触发了重新加载。加载临时使用的 SELinux 内存策略已恢复。

正式遥控优化最终为 **2.6.3 / versionCode 13**。最终版删除了未生效的 `ACTION_SHUTDOWN` 接收器，厂商重启流程在两次测试中绕过了该广播。已验证的保护从曲线加载开始。最后的启动重试和状态读回调整已安装核对，仍沿用两次重启验证的曲线。需要停用曲线时先按[安全恢复步骤](07-recovery.md)静音。

## Home：普通默认项不够

固件存在原厂桌面选择器和开机显式启动原桌面的路径。只调用普通默认桌面设置，仍可能经过厂商选择器。

通过系统 PackageManager 的持久默认接口，分别登记：

```text
MAIN + HOME
MAIN + HOME + DEFAULT
```

目标是 `com.dangbei.tvlauncher/com.dangbei.launcher.ui.main.MainActivity`。辅助代码见 [HomeConfig.java](../tools/HomeConfig.java)。没有停用原厂桌面整包。

Home 解析正确后，仍观察到开机显式启动原厂桌面。最初加入一次开机检查，后来扩展为开机和亮屏后的短检查窗口。只有原厂桌面在前台才切换，HDMI 播放时跳过，已打开的应用也保留。

后续只停用原厂桌面注册为 HOME/LAUNCHER 的 `com.dangbei.leard.leradlauncher.ui.splash.SplashActivity`。原厂桌面的其余组件、服务与 provider 继续启用，系统 FallbackHome 也保留。重启后读回首页组件仍停用，用户确认直接进入当贝桌面。[组件配置工具](../tools/LauncherComponents.java)

## 重启后音量为什么失效

故障时读到：

```text
local.tvkeys: stopped=true，进程不存在
enabled_accessibility_services: 空
accessibility_enabled: 0
退出记录: REMOVE TASK
专用 KL/IDC: 仍在生效
```

于是遥控器仍产生 F11/F12，却没有服务处理。旧方案太依赖无障碍设置一直保持。

2.2 开始补上：前台服务、开机接收器、界面不进入最近任务、`stopWithTask=false`、每 15 秒检查并恢复自身无障碍组件。恢复时保留其他有效服务，不覆盖整个列表。修改系统设置权限用于这项明确的自恢复；查看应用前台事件用于开机桌面判断。

移除界面任务后服务仍运行；模拟启用项丢失后约 3～11 秒恢复。实际重启、实体音量和 Home 随后均确认正常。

## 后台限制

本机 `persist.xgimi.restrictbackground.enable` 已从 true 改为 false，关闭厂商额外的短时后台强停分支，读回和跨重启均确认。

前台服务仍曾被厂商调整为 adj 900、最低调度组并等待冻结，导致积压按键在恢复时集中执行。应用开始丢弃过期的按下事件并限制失去新事件的重复。厂商包级配置只给 `local.tvkeys` 加 adj 100，其余包的调度条目保留。重启读回该服务的 adj 为 100、调度组为 2，未等待冻结。

另一个独立防护只拦截 Etna 进程对 `local.tvkeys` 和 `com.dangbei.tvlauncher` 发起的 force-stop。它保护遥控服务和第三方桌面，保留 Etna 对其他包的原行为。

独立名单及 Android 正常低内存回收仍生效。手动强行停止工具后，需要重新打开工具才能恢复正常运行。详见[厂商限制体检](05-performance-and-restrictions.md)。
