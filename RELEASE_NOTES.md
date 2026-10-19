# PCPulseGuard v0.3.0

First public release. Configurable no-input heartbeats for Windows workstations, with mouse/CPU/GPU actions, compute-load guards, tray status, seven UI languages and GitHub update checks.

**More options → Startup** includes Windows auto-start and silent tray startup. Portable copies use a current-user startup entry; installed copies manage their existing boot/logon tasks with administrator approval.

Download **PCPulseGuard-0.3.0-windows-x64.zip**, extract it and run PCPulseGuard.exe. To install boot/logon tasks, run Install.ps1 as administrator. Existing custom installations must retain their original installation directory and task prefix. The app is currently unsigned. Compare the download with SHA256SUMS.txt.

Updates are checked only; the app never automatically downloads or installs them. Periodic heartbeats are not a guarantee against crashes or hardware faults. GPU compatibility beyond the tested RTX 3060 still needs field validation.

---

首次公开发布：支持无人操作后执行鼠标、CPU、GPU 心跳，计算高负载避让、托盘状态、七种界面语言和 GitHub 更新检查。

“更多选项 → 启动设置”提供开机自启动及静默启动。便携版使用当前用户启动项；安装版通过管理员授权管理原有的开机／登录任务。

下载 **PCPulseGuard-0.3.0-windows-x64.zip**，解压后运行 PCPulseGuard.exe。需要开机／登录任务时，以管理员身份运行 Install.ps1；旧的自定义安装需沿用原安装目录和任务前缀。当前程序未进行代码签名，请核对 SHA256SUMS.txt。

更新检查不会自动下载安装。心跳不能保证电脑不崩溃，也不能替代硬件故障排查。目前 GPU 路径已在 RTX 3060 验证，其他显卡仍需实际验证。
