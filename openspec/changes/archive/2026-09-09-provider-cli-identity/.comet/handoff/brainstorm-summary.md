# Brainstorm Summary

- Change: provider-cli-identity
- Date: 2026-09-07

## 确认的技术方案

在 LoomX Provider「请求」tab 加入「模拟 CLI」下拉按钮（Menu 组件），一键写入三家官方 CLI 的整套身份请求头。三家 CLI 的版本号走三层来源：Claude 从 npm registry、Codex 从 GitHub releases 动态获取，Grok 写死默认 + 用户可手改。结果直接写入现有 `Provider.HeadersJson`，不新增数据库字段。24h AppData 缓存 + 网络失败降级到缓存 → 默认。

6 个关键决策：
- **D1 存储**：直接写 HeadersJson，不新增字段
- **D2 版本来源**：npm / GitHub / 默认 + 手改
- **D3 缓存**：`%LocalAppData%/LoomX/cli-versions.json`，24h TTL
- **D4 家族剥除**：沿用 LiveAgent `CLI_IDENTITY_HEADER_FAMILIES` 思路
- **D5 UI**：Avalonia `Menu` 组件（不用 ToggleButton+Popup）
- **D6 命令**：`IAsyncRelayCommand` 沿用现有 ViewModel 模式

## 关键取舍与风险

- **不新增数据库字段** → 无迁移成本；代价：需通过 header 内容匹配反推当前身份
- **Menu 组件** → 交互可靠、样式可微调；代价：不如 ToggleButton+Popup 灵活
- **Grok 无公开源** → 写死 `1.0.6` + 用户手改；代价：版本落后无法自动更新
- **家族剥除** → 避免拼假指纹；代价：三家 CLI 头结构演进时需同步更新常量
- **走全局代理** → 复用 `LoomX.UseProxy`；代价：npm/GitHub 访问受限地区需手动配置代理

## 测试策略

- `CliIdentityServiceTests`：家族剥除、版本解析、Detect 反推
- `CliVersionServiceTests`：npm/GitHub JSON 解析、失败降级
- `CliVersionCacheTests`：TTL、用户手改保护
- 手动验证：跑 proposal.md 10 条验收场景

## Spec Patch

无。本变更是自包含新能力，不需要修改 delta spec 的现有描述。
