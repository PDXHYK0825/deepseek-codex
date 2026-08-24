# 贡献指南

感谢参与 Codex Model Switcher。提交变更前，请先确认改动仍满足以下原则：不把 API Key 写入普通文件或日志；所有 Codex 配置写入都可回滚；不执行从网络下载的脚本代码。

## 本地开发

需要 Windows 10/11 和 .NET 8 SDK。

```powershell
dotnet restore CodexModelSwitcher.sln
dotnet build CodexModelSwitcher.sln --configuration Release --no-restore
dotnet run --project tests/CodexModelSwitcher.SelfTests --configuration Release --no-build
```

提交前应确保构建没有警告，且全部自测通过。涉及配置编辑、备份恢复或凭据处理的变更，应在 `tests/CodexModelSwitcher.SelfTests/Program.cs` 中补充隔离测试。

## 提交 Pull Request

1. 从 `main` 创建主题分支。
2. 保持提交范围单一，并使用清晰的提交说明。
3. 在 Pull Request 中描述行为变化、测试结果，以及是否影响用户配置或凭据。
4. 不要提交 `bin/`、`obj/`、`artifacts/`、`.tools/`、`.tmp/` 或任何真实配置与密钥。

如果改动会改变备份格式、恢复语义或外部配置冲突处理，请同时更新架构文档和 `CHANGELOG.md`。
