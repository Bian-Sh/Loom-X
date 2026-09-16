## 1. 清理无意义控制台日志

- [x] 1.1 增加失败测试，验证 Shell 回退消息不再使用 `Warning`，透明外观消息不再使用 `Information`
- [x] 1.2 将目标启动回退和透明外观日志调整为 `Debug`，运行相关测试与构建确认行为不变

## 2. 删除旧数据库迁移能力

- [x] 2.1 先增加负向回归测试，验证启动入口、Host、运行时路径和品牌契约不再引用 `ApplicationDataMigration`、`EnsureMigratedAsync`、旧 OllamaHub 数据库路径或迁移锁
- [x] 2.2 删除 `ApplicationDataMigration` 及其异常、启动调用、旧路径属性和迁移专用测试，保持当前 LoomX 数据库初始化流程不变
- [x] 2.3 更新 `app-data-migration` 主规格、升级说明和 Comet 设计/任务产物，确认其他 JSON 迁移逻辑不受影响
- [x] 2.4 运行相关测试、完整测试、Release 构建和 win-x64 发布，检查日志中不再出现旧库迁移提示