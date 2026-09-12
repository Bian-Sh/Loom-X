# 修复方案

在 `GatewayViewModel` 中增加按 route ID 搜索所属 Combo 的局部解析，删除和开关操作先从 `Combos` 找到 route owner，再在现有网关 mutation 锁内调用数据层。若 route 不属于当前集合，继续静默忽略；成功删除后仅更新所属 Combo 的内存列表并重编号。

不修改 XAML、服务端 API 或数据库结构。回归测试使用临时 SQLite 配置库写入 Provider、模型、Combo 和 route，验证未选中 Combo 时 route 删除、Combo 删除及 Provider/模型删除的持久化结果。
