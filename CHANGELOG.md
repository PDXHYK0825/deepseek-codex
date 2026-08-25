# 变更记录

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 的组织方式，并使用语义化版本号。

## [0.1.1] - 2026-08-25

### 变更

- 发布脚本默认把 `CodexModelSwitcher.exe` 生成到项目根目录，便于直接双击启动。
- 安全凭据桥移动到 `runtime/`，主程序同时兼容新版目录和旧版同目录布局。
- 发布中间文件改用系统临时目录，并在完成后自动清理。
- GitHub Release 提供 Windows x64 自包含压缩包，无需预装 .NET 运行时。

## [0.1.0] - 2026-08-24

### 新增

- Windows WPF 模型切换界面，以及状态、进度和中文错误提示。
- GPT、DeepSeek Flash、Pro 与 Vision 配置切换。
- Windows Credential Manager API Key 存储和独立凭据桥。
- 配置基线、操作前快照、完整性校验、外部修改检测与失败回滚。
- 官方 DeepSeek 模型目录的数据化提取和缓存回退。
- 命令行调试入口与无第三方测试依赖的自测程序。

### 安全

- 不执行下载的官方配置脚本，只提取经过允许列表验证的数据。
- 备份快照主动移除 DeepSeek 明文 bearer token。

[0.1.1]: https://github.com/PDXHYK0825/deepseek-codex/releases/tag/v0.1.1
[0.1.0]: https://github.com/PDXHYK0825/deepseek-codex/releases/tag/v0.1.0
