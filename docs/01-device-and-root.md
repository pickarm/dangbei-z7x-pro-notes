# 设备环境与按需 root

设备日常名称按 Z7X PRO 记录，系统型号却是 D7XPG，产品为 Product_mudanpro2 / goat_elegant，包含较多 `com.xgimi`、`com.dangbei` 组件。不能仅凭外壳名称判断固件接口。

## 起点

- Android 13，固件 v6.11.3。
- 系统可见内存约 1.81 GiB；只提供 armeabi-v7a / armeabi。
- 普通 ADB shell 是 uid 2000。
- `adb root` 返回 `adbd cannot run as root in production builds`。
- 原始观察中 SELinux 已是 Permissive，启动校验状态为 orange。不是这次为了获得权限才改成这样。
- system/vendor 使用只读 EROFS；有 uid 0 不代表可直接写这些分区。

## 找到的入口

本机原厂 init 配置中存在 `debugserver`，运行用户为 root，脚本从诊断属性取命令执行。使用的文件和属性为：

```text
/vendor/etc/init/hw/init.xgimi.common.rc
/system/bin/debugserver.sh
vendor.xgimi.system.debug.cmd
vendor.xgimi.system.debugserver
```

只在已经授权连接的本机 ADB 会话中调用，实测任务身份为 uid 0，并能读取普通 shell 无权读取的启动分区少量字节。读验证输出送往 `/dev/null`，没有把启动分区镜像放进仓库。

这不是 Magisk，也没有安装 su 管理器或改写 boot。后续持久语音方案是普通后台在需要时短时调用原厂入口加载补丁，加载后关闭入口。

## 任务执行方式

本机使用过自写任务执行器：拒绝打断已在运行的诊断服务，保存原诊断属性，把任务放在唯一临时路径，等待结束标记，并在 finally 中恢复属性、清理该任务的临时文件。它只负责执行指定脚本，不会自动安装持久化机制。此处保留机制和恢复边界，未附这个高权限任务执行入口。

执行前必须自己核对设备、固件和脚本内容。这个固件暴露了高权限诊断能力本身就是需要注意的安全问题，不能从一次成功推断其他版本也适用。

## 没做的事

没有刷写固件、安装 Magisk、重新签发设备认证、替换 DRM 密钥盒或关闭热保护。没有制作完整恢复固件，也没有证明任意镜像修改可以安全刷回。
