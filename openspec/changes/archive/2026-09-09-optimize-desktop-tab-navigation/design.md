# 修复方案

将控制台日志列表和活动记录列表由 `ItemsControl` 替换为 Avalonia `ListBox`，保留现有数据模板与滚动容器，并通过列表样式隐藏选择态、保持原有行外观。`ListBox` 使用 Avalonia 默认的 `VirtualizingStackPanel`，长列表切换和滚动只实例化可见项。

在 `ConsoleViewModel` 中抽取单条记录匹配逻辑。新增日志时仅将匹配当前筛选条件的记录追加到 `VisibleLogs`；淘汰最旧记录时同步移除对应可见项；只有用户修改筛选条件时才执行现有全量筛选。计数和状态属性继续在增量路径更新。
