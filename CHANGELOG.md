# Changelog / 更新日志

## 0.3.1 — 2026-10-19

- Fix mouse movement, clicks and wheel activity failing to reset inactivity when a low-level hook silently stops delivering callbacks.
- Receive mouse/keyboard Raw Input and run hooks plus continuous session polling on a dedicated thread, isolated from settings and status-file I/O.
- Continue excluding the app's own mouse pulse from inactivity, including in polling-only fallback mode.
- Reject stale/unhealthy input-monitor records and expose timestamp-only input-source counters for troubleshooting.
- Add input-clock and polling-fallback regression coverage.

修复重启后鼠标操作不重置无人操作倒计时的问题。输入监测改为独立线程，通过 Raw Input、事件钩子和持续会话轮询相互补充；保留自身鼠标心跳过滤，并补充钩子失效、计时器回绕及兜底模式回归检查。

## 0.3.0 — 2026-10-19

- First public release as **PCPulseGuard**, under the MIT license.
- Seven UI languages with immediate switching and saved preference.
- In-app Windows startup and silent tray startup controls, for portable and installed copies.
- Manual and optional daily stable-release checks through GitHub; no automatic installation.
- Generic no-input trigger, configurable mouse distance, CPU/GPU targets, timing units and busy thresholds.
- Tray countdown, status/log view, boot installation, uninstall and legacy-settings migration.
- Windows CI, packaged portable/source releases and SHA-256 checksums.

首次以 PCPulseGuard 名称公开发布，采用 MIT 许可；支持七种界面语言、GitHub 版本检查、通用无人操作触发、可配置鼠标距离与 CPU/GPU 心跳、高负载避让、托盘状态、开机安装、卸载及旧设置迁移，并提供 Windows CI 与校验值。

## 0.2.0 — local prototype / 本地原型

Earlier local Workstation Pulse prototype. No public release or compatibility guarantee for external copies.
早期本地“工作站心跳”原型，未公开发布。
