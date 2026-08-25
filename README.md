# Codex Model Switcher

这是一个 Windows 桌面版 Codex GPT / DeepSeek 快速切换器，包含简单的 WPF 前端、可复用后端、凭据桥接程序和用于调试的命令行入口。

> [!IMPORTANT]
> 本项目是非官方工具，会修改当前用户的 Codex 配置并重启 ChatGPT Windows 应用。首次使用前请阅读“数据位置”和“当前边界”；程序会为配置建立基线备份与操作前快照。

## 运行要求

- Windows 10/11 x64。
- 从源码构建需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
- 运行默认发布版本需要 .NET 8 Desktop Runtime；使用 `-SelfContained` 发布则无需预装运行时。

## 已实现

- 识别 `CODEX_HOME` 或默认的 `%USERPROFILE%\.codex`。
- 检测 OpenAI、DeepSeek Flash、Pro、Vision 及官方脚本配置状态。
- 切换 `deepseek-v4-flash`、`deepseek-v4-pro`、`deepseek-v4-flash-vision-exp`。
- 从 DeepSeek 官方 PowerShell 脚本提取并验证模型目录，不执行下载到的脚本代码。
- 保留用户无关的 `config.toml` 设置，只更新模型和 DeepSeek provider 字段。
- 使用 Windows Credential Manager 保存 API Key，通过 Codex 的命令式认证读取。
- 基线备份、每次操作前的安全快照、SHA-256 完整性检查和失败回滚。
- 接管 DeepSeek 官方脚本创建的 `backup-deepseek`，并恢复脚本执行前的配置。
- 检测外部配置修改，默认拒绝静默覆盖。
- 完整退出并重新启动 ChatGPT Windows 应用。
- WPF 一键切换界面、API Key 独立保存与状态回显、明文/星号显示切换、进度和中文错误提示。
- 独立凭据桥接程序，Codex 无需从界面进程或明文配置读取 Key。
- 凭据桥使用精确的 `get deepseek` 参数，并兼容旧版本误生成的 `credential get deepseek` 配置。
- 恢复 GPT 时保留一个不激活的 DeepSeek provider 定义，历史 DeepSeek 对话仍可加载。
- 独立自测程序，不依赖第三方测试包。

Codex 的自定义 provider、`auth.command` 和用户级配置约束以 [OpenAI 配置参考](https://developers.openai.com/codex/config-reference)及[高级配置](https://developers.openai.com/codex/config-advanced)为准。DeepSeek 模型目录来自[官方配置脚本](https://cdn.deepseek.com/api-docs/codex-deepseek-setup-en.ps1)。

## 目录

```text
src/
  CodexModelSwitcher.App/       Windows WPF 前端
  CodexModelSwitcher.Backend/   可被 WPF 引用的后端类库
  CodexModelSwitcher.CredentialBridge/ Codex 安全取 Key 的控制台桥接
  CodexModelSwitcher.Cli/       命令行和凭据读取入口
tests/
  CodexModelSwitcher.SelfTests/ 无第三方依赖的隔离测试
docs/
  backend-architecture.md       后端设计与 UI 接入说明
  frontend.md                   WPF 前端结构与交互说明
scripts/
  publish-app.ps1               Windows x64 发布脚本
```

## 构建

需要 .NET 8 SDK：

```powershell
dotnet build CodexModelSwitcher.sln --configuration Release
dotnet run --project tests/CodexModelSwitcher.SelfTests --configuration Release
```

## 启动桌面界面

```powershell
dotnet run --project src/CodexModelSwitcher.App --configuration Release
```

或直接运行构建结果：

```text
src\CodexModelSwitcher.App\bin\Release\net8.0-windows\CodexModelSwitcher.exe
```

使用步骤：

1. 输入 DeepSeek API Key，点击“保存 API Key”。
2. 勾选“使用 * 隐藏 API Key”可掩码显示；取消勾选可查看完整值。
3. 点击 Flash、Pro 或 Vision；配置完成后会自动重启 ChatGPT，然后新建任务使用 DeepSeek。
4. 后续启动时会显示 Key 是否已保存，并从 Windows 凭据库安全回显。
5. 点击 GPT 恢复第一次切换前的原始 Codex 设置；历史 DeepSeek 任务仍可查看和继续。

Codex 会把模型和 provider 固定在任务元数据中，所以切换只决定新任务的默认 provider。已有 GPT/DeepSeek 任务继续使用创建时的 provider；不要在切换后用旧任务判断新 provider 是否生效。

## 命令行用法

查看状态：

```powershell
dotnet run --project src/CodexModelSwitcher.Cli -- status
```

切换模型：

```powershell
dotnet run --project src/CodexModelSwitcher.Cli -- switch flash
dotnet run --project src/CodexModelSwitcher.Cli -- switch pro
dotnet run --project src/CodexModelSwitcher.Cli -- switch vision
```

切回原始 OpenAI/GPT 配置：

```powershell
dotnet run --project src/CodexModelSwitcher.Cli -- switch gpt
```

调试期间跳过 ChatGPT 重启：

```powershell
dotnet run --project src/CodexModelSwitcher.Cli -- switch pro --no-restart
```

API Key 不支持命令行参数传入。首次切换会出现隐藏输入框，也可以通过标准输入或当前进程的 `DEEPSEEK_API_KEY` 环境变量提供。

## 发布桌面程序

生成依赖本机 .NET 8 Desktop Runtime 的单文件程序：

```powershell
.\scripts\publish-app.ps1
```

发布结果位于：

```text
CodexModelSwitcher.exe              双击启动
runtime\
  codex-model-switcher-credential.exe
```

主程序直接位于项目根目录，便于双击启动。`runtime/` 中的安全凭据组件必须与主程序一起保留。根目录程序、`runtime/`、`artifacts/`、`bin/` 和 `obj/` 都是可复现产物，不提交到 Git。

不想安装 SDK 或运行时，可从 [GitHub v0.1.1 Release](https://github.com/PDXHYK0825/deepseek-codex/releases/tag/v0.1.1) 下载 Windows x64 自包含压缩包，解压后直接双击根目录的 `CodexModelSwitcher.exe`。

如需生成包含运行时的独立版本：

```powershell
.\scripts\publish-app.ps1 -SelfContained
```

## 数据位置

Codex 文件：

```text
%CODEX_HOME%\config.toml
%CODEX_HOME%\models.json
```

后端状态与备份：

```text
%LOCALAPPDATA%\CodexModelSwitcher\<Codex路径哈希>\
```

API Key 位于 Windows Credential Manager，目标名称为：

```text
CodexModelSwitcher/deepseek-api-key
```

日志、状态和备份清单中不写入 API Key。接管官方 DeepSeek 脚本时，安全快照会主动擦除其 TOML 中的明文 bearer token。

## 当前边界

- 当前提供普通窗口界面，尚未加入系统托盘和 MSIX 安装包。
- ChatGPT 进程管理按当前用户会话中的 `ChatGPT`/`ChatGPT-Desktop` 进程识别。
- 自动重启需要系统能够通过开始菜单应用 ID或原可执行文件重新打开 ChatGPT。
- GPT 模式会保留 DeepSeek provider 的认证定义以兼容历史任务，但不会把它设为当前 provider，也不会用 DeepSeek 模型目录替换 GPT 目录。
- `--accept-external-changes` 会在继续前建立安全快照，UI 层应先向用户展示确认对话框。

## 开发与安全

- 后端设计见 [`docs/backend-architecture.md`](docs/backend-architecture.md)，前端说明见 [`docs/frontend.md`](docs/frontend.md)。
- 贡献流程见 [`CONTRIBUTING.md`](CONTRIBUTING.md)，版本变更见 [`CHANGELOG.md`](CHANGELOG.md)。
- 请勿在 Issue 中提交真实 API Key、`config.toml`、备份内容或其他凭据；安全问题请按 [`SECURITY.md`](SECURITY.md) 私下报告。

本项目与 OpenAI、DeepSeek 均无隶属或背书关系。
