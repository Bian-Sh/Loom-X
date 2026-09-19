# 配置控件增量保存与定向刷新

## 背景

Provider、Model、Settings 和 Gateway 页面中的单个 Toggle、CheckBox、ComboBox、TextBox 或数值输入保存后，会触发完整配置读取、运行时配置重载和多个页面刷新。一次模型启用切换因此产生大量无关数据库读取与日志，甚至重复应用窗口透明度。

## 目标

- 单字段编辑只修改目标记录的必要字段或关系行。
- 数据库写入成功后只更新受影响的桌面快照和运行时投影，并发布一次带实体、标识和字段的 `LocalSave` 事件。
- 各页面只响应自己关心的配置变化，保留正在编辑的控件实例、搜索和展开状态。
- Settings 外观控件继续即时预览，但其它配置变化不触发透明度应用。
- 网关运行中使用局部原子替换，不再因为单字段编辑完整 `ReloadAsync`。

## 范围

覆盖现有 Provider/Model、Settings、Gateway Endpoint/Combo/Route 的持久化单字段控件和逐字输入；覆盖 AppDataStore、ConfigSnapshotService、DatabaseConfigurationProvider 及相关 ViewModel 的事件和快照链路。

## 非目标

不修改数据库路径或 schema，不改变手动刷新和首次初始化语义，不新增通用事件总线，不重做创建/删除/同步的业务流程。

## 验收

1. 单字段保存不调用 `ReloadCoreAsync`、`LoadAsync` 或运行时完整 `ReloadAsync`。
2. SQLite 只更新目标行/列，Endpoint 绑定只更新目标关系。
3. 一次操作最多产生一次定向 `LocalSave`，非外观事件不应用透明度。
4. 快速切换、连续输入、保存失败和模型重新启用均保持最终值、编辑版本和网关路由一致。
