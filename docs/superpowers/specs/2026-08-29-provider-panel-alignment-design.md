# Provider 面板对齐与卡片操作设计

## 目标

调整 Avalonia 桌面端 Provider 页面，使摘要、Provider 目录、Provider 详情三个区域明确分区并填满 Provider 面板可用空间。顶部摘要固定可见，目录和详情底边随窗口尺寸变化并与主内容区底部保持现有 18px 留白。

## 布局结构

`ProvidersView` 根节点改为两行 `Grid`，不再使用包住整个页面的根级 `ScrollViewer`：

- 第一行由摘要块内容决定高度，显示四个统计块。
- 第二行使用 `*` 占满剩余高度，内部为 `310px + 14px + *` 两列。
- 左列 Provider 目录和右列详情面板均 Stretch 到工作区底边。主窗口现有内容容器的 `Padding="32,18"` 继续提供底部 18px gap。
- 目录标题、数量、新增按钮、搜索框和详情标题属于固定区域；Provider 列表以及详情 Tab 的内容分别在自己的滚动容器中滚动。

## Provider 目录

目录卡片继续绑定 `Providers`、`SelectedProvider` 和 `ProviderEditorViewModel`，不复制保存逻辑。卡片保留现有启用开关和信息展示：

- 启用开关继续绑定 `Enabled`，保留现有位置，不添加第二个开关。
- 清空 `ToggleSwitch` 的 `OnContent` 与 `OffContent`，移除截图中的 `On` 文案。
- 为开关添加 `ToolTip.Tip="启用 Provider"` 和无障碍名称，悬停及辅助技术仍可识别其用途。
- 卡片右下角增加删除图标按钮，按钮操作对象为当前卡片对应的 Provider。
- 删除详情标题右上角的重复删除入口，避免同一操作出现两处。

删除图标点击后由 View 创建原生确认窗口，显示 Provider 名称并提供取消/删除两个动作。确认后调用现有删除命令及 `ConfigSnapshotService.DeleteProviderAsync`，取消不修改集合或配置。

## 详情与空状态

当 `HasSelectedProvider` 为真时，详情面板显示现有标题和基础、请求、模型三个 Tab；当 Provider 集合为空且没有选中项时，显示轻量空状态提示“暂无 Provider / 请先从左侧目录添加 Provider”。新增只读 `HasNoSelectedProvider` 属性并在选中项变化时发出属性通知，以支持互斥显示。

基础 Tab 移除 Provider“启用”复选框，Provider 启用状态只从目录卡片操作。请求 Tab、模型 Tab 及模型自身的启用开关保持现有绑定和行为。

## 数据流与错误处理

布局变更复用现有 ViewModel：刷新、自动保存、连接测试、删除及摘要统计逻辑不变。删除失败继续写入现有 `Status`；确认窗口只负责用户确认和命令触发，不新增配置接口、数据库字段或 HTTP API。

## 验证标准

- `dotnet build OllamaHub.slnx` 成功。
- `dotnet test` 成功。
- 在 1180x760、920x600 及缩放窗口检查：摘要固定可见，目录和详情底部对齐，底部保留 18px gap，列表/Tab 内容可独立滚动且无重叠。
- Provider 为 0 时右侧显示空状态；新增 Provider 后显示详情。
- 卡片删除按钮弹出确认窗口；取消不删除，确认删除对应卡片并更新摘要。
- 启用开关不显示 `On/Off` 文案，悬停提示可见；基础 Tab 不再显示 Provider 启用复选框。
- 运行 `scripts/publish-desktop.ps1` 生成带时间戳的 `outputs` 发布目录，并检查发布包可启动。

## 非目标

- 不调整既有配色、字体、持久化格式或远程模型同步能力。
- 不引入新的 UI 框架、对话框依赖或通用抽象。
