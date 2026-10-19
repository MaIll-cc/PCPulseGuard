# Security / 安全

Supported security-fix line: the latest public release.

Please use this repository's private vulnerability-reporting feature in the **Security** tab for vulnerabilities. Do not publish exploit details, credentials or unredacted logs in public issues. If private reporting is unavailable, open an issue asking for a private reporting channel without technical exploit details.

The SYSTEM engine accepts bounded numeric/boolean action preferences and session metadata. It does not accept executable paths, shell commands or network addresses. Installed program files and task definitions require administrator rights to replace. The per-user tray performs an HTTPS request to this repository's GitHub Releases endpoint for version checks. It never downloads or executes a release automatically.

Binaries are currently unsigned. Releases include SHA-256 checksums. A checksum detects a mismatching file; it is not a code-signing identity. Obtain artifacts from this repository and inspect the public build workflow.

安全问题请优先使用仓库 Security 页中的私密漏洞报告，不要在公开问题中提交攻击细节、凭据或未脱敏日志。当前仅维护最新公开版本。后台不接受任意命令、可执行文件路径或自定义网络地址；安装文件需管理员权限替换。更新功能只检查并打开官方 Release 页面，不会自动下载安装。当前程序未签名，校验值不等同于签名身份认证。
