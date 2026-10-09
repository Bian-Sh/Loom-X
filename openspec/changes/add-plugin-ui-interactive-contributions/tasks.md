# 任务清单

## 1. 协议扩展（LoomX.Plugin.Abstractions）

- [x] 新增交互节点：Button / TextField / ToggleField / ChoiceField
- [x] 新增命令契约：PluginUiCommand / PluginUiCommandResult / IPluginUiCommandHandler
- [x] SchemaVersion 提升到 2，保留 MinimumSchemaVersion = 1 与 InteractiveSchemaVersion = 2
- [x] PluginManifestUiContribution 增加 Interactive 声明

## 2. 宿主校验与命令路由（LoomX.PluginHost）

- [x] PluginUiValidator 支持 v1/v2 判定与交互节点校验，节点上限提升到 512
- [x] PluginRuntime.ExecuteUiCommandAsync：插件/contribution/参数校验、异常隔离与诊断
- [x] ManifestParser 解析 interactive 字段
- [x] LoadedPluginInfo 暴露 InteractiveUiContributions 供宿主挂载命令通道

## 3. 宿主渲染（LoomX 桌面端）

- [x] PluginUiPresenter 渲染交互控件并维护表单字段状态
- [x] 按钮点击与开关/下拉值变化回传命令
- [x] PluginsViewModel 转发命令并按 Tone Toast 结果
- [x] PluginsView.axaml 绑定 CommandSink / IsInteractive
- [x] App.axaml 补齐交互控件主题样式

## 4. maskit 插件（plugins/LoomX.Maskit）

- [x] 规则模型与内置形态基线（21 项，默认开关对齐 maskit 口径）
- [x] MaskitRuleStore：自定义规则与前缀增删改查、内置开关、JSON 持久化
- [x] MaskitEngine：脱敏/还原，占位符映射只驻留内存
- [x] 声明式设置页：新增表单、规则列表（编辑/删除/启停）、内置开关、密钥前缀
- [x] 命令处理：add_rule / begin_edit / cancel_edit / save_edit / delete_rule / toggle_rule / toggle_builtin / add_prefix / delete_prefix
- [x] plugin.manifest.json 声明 card-body 概览与 detail-body interactive 设置页
- [x] 纳入 LoomX.slnx 与第一方插件负载复制

## 5. 测试与验证

- [x] 规则存储与引擎单测（CRUD、重复/非法拒绝、整词边界、还原边界、凭据类打码）
- [x] 设置页声明式 UI 与命令 CRUD 单测
- [x] Runtime 端到端：动态加载 maskit、命令生效、未声明交互被拒、Pipeline 脱敏
- [x] 交互协议边界单测（v1 隔离、空白命令、超长参数、处理器异常）
- [x] 全解决方案构建通过、插件相关测试全绿
