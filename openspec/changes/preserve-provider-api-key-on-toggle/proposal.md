# Provider 开关自动保存时保留 API Key

## 问题

Provider 已配置 API Key 后，页面加载时出于安全原因只保留 `HasApiKey` 标志，编辑框不回填明文。用户取消 Provider 的启用开关时会触发自动保存，当前 ViewModel 无法区分“未编辑的空框”和“用户主动清空”，后端因此把已保护的 API Key 清除。

## 根因

`ProviderEditorViewModel.ApplyResponse()` 为避免把明文密钥发送到桌面 UI，会将编辑框保持为空；原有 `ToInput()` 只按字符串是否为空判断，启用开关自动保存调用 `UpdateProviderAsync` 时进入 Provider 的空值清除分支。该空值是安全设计导致的，并非 Tab 切换丢失绑定数据。

## 修复目标

- 未编辑 API Key 时，Provider 自动保存应保留原有受保护密钥。
- 用户输入新值或主动清空后才替换或清除密钥。
- 通过配置服务显式提交空字符串的既有清除语义保持不变。
- 增加回归测试覆盖开关更新场景。

## 影响范围

仅涉及 Provider 编辑 ViewModel 的输入映射和配置管理服务测试，不改变存储格式或公开接口。
