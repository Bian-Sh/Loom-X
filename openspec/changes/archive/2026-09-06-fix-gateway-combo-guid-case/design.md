# 修复方案

在 `ConfigurationDatabase.InitializeAsync` 完成表结构和默认值处理后，使用同一数据库连接执行一次幂等的 Guid 文本规范化：关闭当前连接的外键检查，在 Provider、Model、Combo、Route 和 Combo 绑定表中将 Guid 列转换为 `lower(...)`，完成后恢复外键检查。该操作只改变历史文本的大小写，不改变 Guid 值、表结构或业务关系；已是小写的数据库不会产生有效写入。

保留现有 `HasConversion<string>()` 映射，使新写入继续使用同一规范格式。通过服务层回归测试先构造大写历史行，再重新初始化并更新 Combo 与 Route，验证读取、关系加载和保存均成功。
