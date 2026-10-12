# AI 助手 Provider 管理与危险操作确认需求

## 原因

内置助手已经注册 Provider 增删改查工具，但危险操作仅在 AskEachTime 模式进入审批；自动批准模式缺少确定性的强制确认。需要让管理能力可用，并保证删除、清除凭据等高影响操作不会仅依赖模型提示或权限模式。

## 变更内容

- 复用 loomx.list_providers/get_provider/create_provider/update_provider/delete_provider，补齐可发现性、参数语义与错误反馈。
- 对删除 Provider、删除依赖模型、清除或替换凭据、修改接入地址/模型列表URL/协议、停用正在使用的 Provider 等操作，在执行前强制显示应用内确认卡，列出对象、安全变更摘要及依赖影响。
- 强制确认独立于自动批准/逐条批准模式；无 UI、取消、超时、会话切换或参数变化时不得执行。确认绑定精确操作和对象，不能复用旧确认。
- 查询和普通新增流程不反复打断；新增持久凭据可用明确影响说明的名称提交承担确认。
- 默认保留原凭据；更新要先读取、保留未修改字段，明确整体替换语义，避免意外清空 Header、模型或绑定。
- 覆盖依赖、失败、取消、并发变更、日志脱敏与 CUA 交互验收。

## 能力

### 新增能力

- assistant-provider-management：Provider 管理工具和独立于权限模式的危险操作执行前确认。

### 修改能力

暂无；复用 assistant-user-decisions 的卡片和生命周期，在后续设计中确认接入边界。

## 影响与状态

涉及 LoomXTools、AssistantService、AgentLoop、ConfigurationManagementService、审批 UI 和相关测试。2026-10-13 创建，按用户要求仅记录需求，后续实现；当前 phase=open，没有实现或完成验收。
