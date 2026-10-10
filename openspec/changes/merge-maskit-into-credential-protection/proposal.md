# 合并 maskit 到凭据保护并开放停用

## Why

maskit 与 Credential Protection 是同一件事的两个实现。maskit 提供了更完整的规则模型与设置页，但两者同时存在会造成：

- 同一份"脱敏"能力有两套规则存储、两套引擎、两套设置页，行为不一致
- 用户无法判断该在哪配置
- 凭据保护插件被硬编码为不可停用（`CanToggle => !IsCredentialProtection`），而这条限制的理由是"停用后历史 token仍依赖兼容解析"

最后一条正是关键：旧的停用实现会关闭插件的全部 Pipeline Entry，导致响应侧恢复能力一并消失，历史会话中的凭据引用永久失效。这个限制是**用错误的方式表达了一个正确的担忧**。

## What Changes

### 1. 删除 maskit，成果并入凭据保护

- 删除 `plugins/LoomX.Maskit`，清理 `LoomX.slnx`、`LoomX.csproj`、`LoomX.Tests.csproj` 中的引用
- `SensitiveRuleStore` 吸收 maskit 的能力：
  - `UpdateRule`（maskit 有、凭据保护原本没有，构成"增删改查"缺口）
  - 21 项内置形态规则基线（`CredentialBuiltinRules`），只可开关不可删除
  - 自定义密钥前缀（`SecretPrefixes`），命中此前缀的连续片段整体替换
  - 规则变更事件 `Changed`
- `CredentialEngine` 编译内置形态规则与密钥前缀，参与 `Detect` 与 `Sanitize`
- 凭据保护新增交互式 `detail-body` 设置页（规则增删改查 + 内置开关 + 前缀管理）

### 2. 停用改为语义停用

- 新增 `IPluginEnableStateAware`：实现该契约的插件，宿主停用时只回调而不关闭 Entry
- `CredentialProtectionPlugin.OnPluginEnabledChanged` 切换 `ActiveProtectionEnabled`
- 停用后的行为边界：
  - **停止**：识别新明文、token 化新明文
  - **保留**：历史 placeholder 的识别、完整性指令注入、响应恢复
- 判定依据：上下文中是否存在插件注入的完整性提示词，而不是插件是否启用
- `CanToggle` 放开为 `true`，四种语言的 `plugins.credential.lifecycle` 改为说明停用语义

### 3. 停用利害关系在设置页显式声明

设置页"主动保护状态"区块分三段声明：停用行为、仍然保留的能力、停用的代价。

## Impact

- Affected specs: `credential-protection`、`plugin-ui-contributions`
- Affected code:
  - `plugins/LoomX.CredentialProtection/`（规则存储、引擎、设置 UI、命令、插件主体、manifest 0.7.0）
  - `LoomX.Plugin.Abstractions/ILoomXPlugin.cs`（新增 `IPluginEnableStateAware`）
  - `LoomX.PluginHost/PluginRuntime.cs`（停用分支）
  - `LoomX/ViewModels/PluginsViewModel.cs`（`CanToggle`）
  - `LoomX/Resources/Strings*.resx`（四种语言 lifecycle 文案）
- 测试：新增 `CredentialProtectionDisableTests`、`CredentialProtectionSettingsTests`；更新 3 处过时断言

## 遗留

- 固定电话规则 `LANDLINE` 原正则会把 API Key 内的数字段误判为固话，已收紧为字母数字边界（`(?<![A-Za-z0-9])...(?![A-Za-z0-9])`）。其余内置形态规则未逐一验证跨形态误命中，后续应补一轮形态间的互斥性测试。