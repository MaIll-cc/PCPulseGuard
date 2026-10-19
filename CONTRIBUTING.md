# Contributing / 贡献

Please open an issue describing the behavior before a substantial change. Include Windows version, CPU/GPU models, selected actions and expected/actual behavior. Redact usernames, session timestamps and private paths from logs. Never attach passwords, tokens, service configuration or crash dumps containing private memory to a public issue.

Build with `Build.ps1` on Windows. Run relevant checks. Real GPU and integration checks are opt-in because they load hardware and move the mouse; an unlocked, unused desktop is required. Keep the heartbeat engine offline and preserve numeric-only preferences. Update both README.md and README.zh-CN.md when behavior changes.

Translations live in Languages.tsv: one row per source string, seven columns separated by `|`, ordered Simplified Chinese, English, Traditional Chinese, Japanese, German, Spanish and French. Preserve numbered placeholders such as `{0}`. The build validates completeness and placeholders. Review UI layout in the changed language.

版本号集中在 AppInfo.cs。贡献时请先说明问题和预期行为，运行相关检查，并同时更新中英文文档。翻译位于 Languages.tsv，需保留七列顺序和格式占位符。公开日志前务必去除个人路径、账户与时间信息；不要在公开问题中提交密码、令牌、服务配置或包含私人内存的转储。
