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

## Home：普通默认项不够

固件存在原厂桌面选择器和开机显式启动原桌面的路径。只调用普通默认桌面设置，仍可能经过厂商选择器。

通过系统 PackageManager 的持久默认接口，分别登记：

```text
MAIN + HOME
MAIN + HOME + DEFAULT
```

目标是 `com.dangbei.tvlauncher/com.dangbei.launcher.ui.main.MainActivity`。辅助代码见 [HomeConfig.java](../tools/HomeConfig.java)。没有停用原厂桌面整包。

后来又发现：Home 解析正确，也不代表开机前台一定正确。因此增加一次性的开机补充切换：如果仍在原厂桌面则打开第三方桌面；如果已经打开其他应用，则跳过。每个启动计数最多处理一次，不会在平时反复抢桌面。

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

这不等于“所有应用永不被杀”。固件还有独立黑名单，Android 也仍有正常低内存回收。手动强行停止工具仍会阻止普通开机广播，需要重新打开工具。详见[厂商限制体检](05-performance-and-restrictions.md)。
