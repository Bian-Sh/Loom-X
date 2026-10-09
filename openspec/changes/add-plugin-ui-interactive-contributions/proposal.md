# 扩展插件声明式 UI 支持交互，并新增 maskit 插件设置页

## Why

插件声明式 UI 目前只能贡献只读展示内容（文本、图标、布局），插件自有的配置数据没有落地入口。
maskit 需要「规则增删改查」的设置页，而这个页面必须是**运行时动态加载**的插件 Contribution，
不能静态打包进桌面端 XAML 页面——否则插件就不能自带设置页，宿主会被插件的配置项反向污染。

## What Changes

- 声明式 UI 协议升级到 schema v2：
  - 新增四类交互节点 `PluginUiButtonNode` / `PluginUiTextFieldNode` / `PluginUiToggleFieldNode` / `PluginUiChoiceFieldNode`；
  - 新增命令回传契约 `PluginUiCommand` / `PluginUiCommandResult` / `IPluginUiCommandHandler`；
  - v1 继续受支持（只允许展示型节点），v1 树里出现交互节点会被隔离。
- Manifest UI Contribution 新增 `interactive` 声明；宿主只为声明为交互的 Contribution 挂载命令通道。
- 宿主渲染器支持交互控件与表单字段状态：按钮点击、开关/下拉值变化回传命令，命令结果由 Toast 呈现。
- 新增第一方插件 `loomx.maskit`：规则存储、脱敏/还原引擎，以及贡献在详情正文的声明式设置页
  （自定义规则增删改查、内置形态规则开关、密钥前缀管理）。

## Impact

- 契约程序集 `LoomX.Plugin.Abstractions`：新增类型与常量，向后兼容。
- `LoomX.PluginHost`：`PluginUiValidator` 增加交互节点校验与 v1/v2 版本判定；`PluginRuntime` 增加命令路由与隔离。
- `LoomX` 桌面端：`PluginUiPresenter` 渲染交互控件，`PluginsViewModel` 转发命令并 Toast。
- 新增 `plugins/LoomX.Maskit`，并纳入解决方案与第一方插件负载复制（输出 `plugins/loomx.maskit/`）。
