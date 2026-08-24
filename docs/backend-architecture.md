# 后端架构与 UI 接入

## 调用入口

桌面 UI 应在应用启动时创建一个 `BackendRuntime`，在退出时释放：

```csharp
using CodexModelSwitcher.Application;
using CodexModelSwitcher.Domain;
using CodexModelSwitcher.Infrastructure;

using var backend = BackendRuntime.CreateDefault();
var paths = CodexPaths.Resolve();
var status = await backend.Status.GetStatusAsync(paths);
```

切换 DeepSeek：

```csharp
var result = await backend.Switcher.SwitchToDeepSeekAsync(
    paths,
    ModelProfile.DeepSeekPro,
    apiKeyFromPasswordBox,
    credentialCommand,
    new SwitchOptions(RestartChatGpt: true),
    cancellationToken);
```

恢复 GPT：

```csharp
var result = await backend.Switcher.RestoreOpenAiAsync(
    paths,
    credentialCommand,
    new SwitchOptions(RestartChatGpt: true),
    cancellationToken);
```

`credentialCommand` 包含可执行文件和完整参数，不能再由配置编辑器隐式追加参数。当前 CLI 自身实现了：

```text
credential get deepseek
```

WPF 的轻量 CredentialBridge 使用 `get deepseek`；为修复旧版本已落盘的错误参数，它同时兼容 `credential get deepseek`。

恢复 GPT 时，后端恢复基线中的顶层 GPT 设置和原始 `models.json`，但会重新附加一个不激活的 `[model_providers.deepseek]` 认证定义。Codex 桌面端按任务恢复原 provider；缺少这个定义时，历史 DeepSeek 任务会因 `Model provider deepseek not found` 而无法加载。

## 切换事务

```text
获取跨进程文件锁
  → 检查外部修改
  → 保存/确认 API Key
  → 建立或导入基线备份
  → 获取并验证官方模型目录
  → 生成 TOML
  → 校验 TOML 与 JSON
  → 建立安全快照
  → 原子写入 models.json、config.toml、state.json
  → 失败时恢复操作前文件
  → 重启 ChatGPT
```

配置文件落盘和 ChatGPT 重启是分离的：如果重启失败，已经验证成功的配置会保留，UI 应提供“手动启动 ChatGPT”和“重新尝试重启”按钮。

## 备份策略

- `baseline`：第一次进入受管状态前的原始配置，不自动删除。
- `snapshots`：每次切换或恢复前的安全快照。
- 官方脚本接管：读取 `%CODEX_HOME%\backup-deepseek\manifest.txt` 和原始 `config.toml`，不会把官方脚本写入的明文 Key 导入基线。
- 完整性：基线清单记录 SHA-256，读取时必须校验。
- 外部修改：受管文件哈希发生变化时，默认抛出 `external_changes_detected`。

## 异常码

| 异常码 | 含义 |
|---|---|
| `api_key_required` | 没有可用的 DeepSeek Key |
| `invalid_api_key` | Key 格式不符合要求 |
| `codex_home_missing` | Codex 配置目录尚未创建 |
| `external_changes_detected` | 文件被其他程序修改 |
| `baseline_missing` | 无法执行恢复，因为基线不存在 |
| `baseline_corrupt` | 备份缺失或哈希不匹配 |
| `catalog_unavailable` | 官方模型目录和本地缓存均不可用 |
| `configuration_validation_failed` | 生成的 TOML/JSON 未通过校验 |
| `operation_busy` | 另一个切换操作仍在进行 |

UI 应针对这些异常码提供中文提示，不应直接显示内部堆栈。

## UI 状态映射

| `ProviderState` | 推荐显示 |
|---|---|
| `OpenAI` | OpenAI / GPT |
| `DeepSeekFlash` | DeepSeek V4 Flash |
| `DeepSeekPro` | DeepSeek V4 Pro |
| `DeepSeekVision` | DeepSeek Vision 实验版 |
| `VendorScriptManaged` | 已检测到官方脚本配置，可接管 |
| `Unknown` | 未受支持的自定义 Provider |
| `Broken` | 配置损坏，需要诊断 |

## 安全约束

- UI 不得把 Key 放进命令行参数、日志、分析事件或错误报告。
- API Key 输入使用密码框，并尽快从 ViewModel 清空。
- 不允许用户在 MVP 中修改 DeepSeek API 域名。
- “接受外部修改”必须由明确的用户操作触发。
- 强制结束 ChatGPT 前，UI 应提示可能丢失未提交输入。
- 卸载前如果当前仍是 DeepSeek，应提示先恢复原始配置。
