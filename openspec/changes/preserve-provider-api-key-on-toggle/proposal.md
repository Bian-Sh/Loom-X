# Provider 开关自动保存时保留 API Key

## 问题

Provider 已配置 API Key 后，页面加载时出于安全原因只保留 `HasApiKey` 标志，编辑框不回填明文。用户取消 Provider 的启用开关时会触发自动保存，当前 ViewModel 将空编辑框作为空字符串提交，后端因此把已保护的 API Key 清除。

## 根因

`ProviderEditorViewModel.ToInput()` 直接传递 `ApiKey`。已配置密钥的 Provider 在界面中 `ApiKey` 为空，启用开关自动保存调用 `UpdateProviderAsync` 时进入 Provider 的空值清除分支。

## 修复目标

- 未输入新 API Key 时，Provider 自动保存应保留原有受保护密钥。
- 通过配置服务显式提交空字符串的既有清除语义保持不变。
- 增加回归测试覆盖开关更新场景。

## 影响范围

仅涉及 Provider 编辑 ViewModel 的输入映射和配置管理服务测试，不改变存储格式或公开接口。
