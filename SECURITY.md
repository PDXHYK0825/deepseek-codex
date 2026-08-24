# 安全策略

## 支持范围

安全修复以 `main` 分支的最新版本为准。早期提交与本地旧构建不单独维护。

## 报告漏洞

请使用 GitHub 仓库的 **Security → Report a vulnerability** 私下提交报告，不要创建公开 Issue。报告中请包含受影响版本、复现步骤、可能影响和建议修复方式，但不要附带真实 API Key、Codex 配置或凭据导出。

本项目会读写 Codex 用户配置、调用 Windows Credential Manager，并可能重启 ChatGPT。若怀疑密钥已经泄露，请先在 DeepSeek 控制台撤销该密钥，再清理本机凭据和相关日志。
