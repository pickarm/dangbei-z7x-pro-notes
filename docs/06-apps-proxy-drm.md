# 应用、电脑代理与 Apple TV

## Google 组件和桌面入口

Google 服务框架、Play 商店安装完成，但这只说明 APK 能安装、相关界面能启动，不代表投影仪获得了 Google 认证或流媒体播放认证。

Apple TV 电视版安装后，原先桌面看不到入口。之后补了可在电视桌面显示的启动桥，实机能打开 Apple TV。包名为 `com.apple.atve.androidtv.appletv`。仓库不分发第三方 APK。

## 电脑代理

自写“电脑代理”工具控制 Android 系统代理：打开应用启用、退出应用后仍保留、按钮关闭；并配置启动时默认关闭。实测过开启、关闭、HTTPS 连通和结束界面后保留，那个阶段没有再做单独的重启实测。

关键是电脑代理必须允许局域网连接，地址是**电脑的局域网地址**，不是投影仪的 `127.0.0.1`。文档用占位符，不公开实际地址、订阅和账号：

```powershell
# 仅为命令形式示意，先替换成实际电脑地址和端口
adb -s DEVICE_IP:5555 shell settings put global http_proxy PC_LAN_IP:PORT

# 关闭
adb -s DEVICE_IP:5555 shell settings put global http_proxy :0
```

这是系统 HTTP 代理，不是把所有应用都强制接管的 VPN；应用可能不遵循它。电脑要开机，代理要运行。后来停止使用投影仪端 Clash 的路线，保持代理可独立开关。

## Apple TV 为什么仍无法播放

Apple TV 电视版 17.0.0 能启动，但播放提示“无法加载视频”。临时开启代理后仍是同样结果。

诊断确认设备提供 Widevine，但读到安全级别为 L3。插件日志有：

```text
Bad Level 1 Root of Trust. Falling back to L3
Keybox error: 25. Falling back to L3.
```

独立诊断程序打开自己的 DRM 会话时得到 `ERROR_DRM_NOT_PROVISIONED`。这不能单独证明 Apple TV 自己的会话错误完全相同。

设备端 L1 信任 / 密钥盒异常是明确存在的重要嫌疑，但没有拿到 Apple TV 具体许可证错误码，所以不认定唯一根因。相关诊断记录早于本次按需 root 验证，不能简单写成“这次 root 把它弄坏了”。

安装 Google 框架不能修复 Widevine L1、密钥盒或可信环境。没有替换 DRM 证书、复制其他设备密钥或绕过授权，**没有完成正片成功播放验证**。

诊断结束后卸载临时诊断应用、清理临时文件，代理恢复关闭。

参考：[Google Widevine 设备集成说明](https://developers.google.com/widevine/drm/overview#ecosystem)。
