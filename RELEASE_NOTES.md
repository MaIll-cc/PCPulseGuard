# PCPulseGuard v0.3.1

Fix mouse activity failing to reset the inactivity countdown after boot or after a low-level hook stops delivering callbacks. The former monitor could report hook handles as present while missing mouse movement and clicks.

- Mouse/keyboard Raw Input, low-level hooks and continuous Windows session polling now complement each other on an independent thread. Settings/status-file I/O cannot block that input message pump.
- Movement, buttons and wheel events count as activity. This app's own marked mouse pulse remains excluded, including in polling-only fallback mode.
- Local diagnostics contain timestamps and per-source event counts; no key contents, device identities or pointer-position history are recorded.
- Added regression coverage for absent callbacks, own-pulse filtering and Windows tick wrap. Integration readers allow atomic status-file replacement.

Download **PCPulseGuard-0.3.1-windows-x64.zip**. For an installed copy, rerun Install.ps1 as administrator with the original installation directory and task prefix. Settings and startup choices are preserved. The app is unsigned; verify SHA256SUMS.txt.

---

修复开机后，或鼠标钩子不再回调时，鼠标移动、点击和滚轮不能重置无人操作倒计时的问题。旧版可能仍显示钩子句柄存在，却没有真正收到鼠标活动。

- 独立线程同时使用 Raw Input、低级事件钩子和持续 Windows 会话轮询，设置和状态文件读写不再阻塞输入消息循环。
- 保留自身鼠标心跳过滤；即使只剩兜底轮询，也不会把自己的心跳当成用户操作。
- 本地诊断仅增加时间戳和各输入来源的事件计数，不记录按键内容、设备身份或鼠标位置历史。
- 补充钩子失效、心跳过滤及系统计时器回绕的回归检查；集成检查读取状态文件时允许原子替换。

下载 **PCPulseGuard-0.3.1-windows-x64.zip**。已安装用户需以管理员身份重新运行 Install.ps1，并沿用原来的安装目录和任务前缀；升级会保留设置和启动方式。程序暂未签名，请核对 SHA256SUMS.txt。
