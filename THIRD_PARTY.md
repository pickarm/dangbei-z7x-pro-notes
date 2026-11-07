# 第三方工具与来源

此仓库只整理记录和自写代码，不重新分发下列工具、厂商程序或服务。使用时应遵循它们各自的许可和使用条件。

- [Android Platform Tools](https://developer.android.com/tools/releases/platform-tools)：ADB。
- [Android SDK Build Tools](https://developer.android.com/tools/releases/build-tools)：aapt、D8、zipalign、apksigner；本地原实现使用 JDK 21。
- [jadx](https://github.com/skylot/jadx)：仅用于本机静态分析，分析结果以接口事实和结论记录，未上传厂商反编译源码。
- [Frida](https://github.com/frida/frida)：实际使用 17.26.0 android-arm。原下载归档摘要曾核验为 `09d8d2cb3c6d01ce1d3a125dd165859fc56b9b3df3fc261288caea2c5202027e`，但这不是让读者忽略官方来源和自身校验。
- [frida-java-bridge](https://github.com/frida-packages/frida-java-bridge)：实际依赖 7.0.13，ARMv7 兼容问题参考对应上游问题；没有把依赖源码或注入二进制一起放入本仓库。
- [esbuild](https://esbuild.github.io/)：语音脚本打包。
- [Windows 显示配置 API](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setdisplayconfig)：C# 中使用系统原生接口，不替换显示驱动。

Google Play、Apple TV、Clash、当贝桌面、原厂语音服务和固件组件不由本仓库提供或授权。本仓库公开源码不包含这些第三方程序的重新授权。
