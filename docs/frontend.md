# WPF 前端说明

## 功能

- 显示当前 GPT/DeepSeek 状态和模型名称。
- 输入 DeepSeek API Key；输入内容由 `PasswordBox` 屏蔽。
- 一键切换 GPT、Flash、Pro、Vision。
- 默认在切换成功后完整重启 ChatGPT。
- 显示 API Key 是否已经安全保存。
- 支持清除 Windows Credential Manager 中保存的 Key。
- 切换期间锁定界面并显示进度，避免重复点击。

## 安全取 Key

WPF 应用把 Key 写入 Windows Credential Manager。生成的 Codex 配置使用：

```toml
[model_providers.deepseek.auth]
command = "C:/.../codex-model-switcher-credential.exe"
args = ["get", "deepseek"]
```

`codex-model-switcher-credential.exe` 只实现这个读取动作，不接受写入操作，也不会输出日志。构建 WPF 项目时，该程序会自动复制到应用输出目录。

Codex 桌面端会把 provider 固定到任务元数据。切换完成后的提示必须引导用户新建任务；已有任务继续使用创建时的 provider。恢复 GPT 后不要移除 DeepSeek provider 定义，否则历史 DeepSeek 任务无法重新打开。

## 发布目录要求

至少保持以下两个可执行文件在同一目录：

```text
CodexModelSwitcher.exe
codex-model-switcher-credential.exe
```

如果缺少凭据桥接程序，前端会在 DeepSeek 切换前停止操作，不会写入不完整配置。
