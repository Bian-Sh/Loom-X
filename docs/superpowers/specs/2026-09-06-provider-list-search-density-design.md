# Provider 列表搜索与紧凑布局设计

## 背景

Provider 页面左侧目前直接展示全部 Provider，搜索框没有绑定实际状态；列表单元格包含额外的 `P` 图标和独立删除行，导致单元格高度偏大。Provider 删除按钮的图标几何也与模型列表中的删除图标不一致。

## 目标

1. 将左侧目录标题改为“供应商列表”。
2. 让搜索框实时过滤 Provider 列表。
3. 复用模型 Tab 删除模型按钮的 `trash-2` 图标几何。
4. 压缩 Provider 单元格高度并移除左侧 `P` 图标。

## 方案

在 `ProvidersViewModel` 中增加独立的搜索状态和派生列表：

- `ProviderSearchQuery` 保存搜索框内容，使用 `UpdateSourceTrigger=PropertyChanged` 实时更新。
- `FilteredProviders` 根据查询返回原始 `Providers` 的过滤视图，不修改集合本身。
- 查询先执行 `Trim()`；空查询返回全部 Provider，否则以不区分大小写的方式匹配显示名称、Provider ID、Base URL 和协议类型。
- Provider 集合变化、选中 Provider 属性变化和搜索词变化时通知 `FilteredProviders`，保证新增、删除、编辑和刷新后列表及时更新。

XAML 将左侧 `ListBox.ItemsSource` 改为 `FilteredProviders`，搜索框绑定 `ProviderSearchQuery`。Provider 单元格的真实内容保持三行：第一行放显示名称和启用开关，第二行放 Base URL，第三行放协议、模型数和密钥状态；移除 `P` 图标及其占位边距，使用更小的内边距。删除按钮通过叠放方式悬浮在 cell 右下角，不参与行高计算，右边缘与第一行启用开关的右边缘对齐。删除按钮的 `Path` 直接使用模型删除按钮现有的几何数据。

Provider 选中态保持与未选中态相同的 cell 背景，直接使用 `ListBoxItem` 原生选中边框在最左侧显示约 3px 的 `AccentBrush`；鼠标悬停在选中 cell 上时使用 `SurfaceMutedBrush` 提供反馈；模型列表的整块选中背景保持不变。

第一行名称组与启用开关垂直居中；启用状态绿点随名称组居中显示，并通过悬浮提示说明“绿色表示已启用”。ToggleSwitch 的视觉轨道右边缘与删除按钮右边缘对齐，视觉偏移不改变其布局占位。

## 错误处理与边界

- 搜索只影响显示，不会修改 Provider 数据或持久化状态。
- 空白搜索词按空查询处理，恢复完整列表。
- 删除、刷新和自动保存行为保持不变。
- 当搜索无匹配项时，列表自然为空，不引入额外状态或占位组件。

## 验证

- 增加 ViewModel 搜索行为测试，覆盖空查询、大小写不敏感匹配、四个字段匹配和无匹配结果。
- 更新 ProvidersView XAML 契约测试，覆盖标题、搜索绑定、过滤集合、紧凑布局约束、移除 `P` 图标以及删除图标复用。
- 运行 `dotnet test LoomX.Tests/LoomX.Tests.csproj`，并执行桌面项目构建确认 Avalonia XAML 编译通过。
