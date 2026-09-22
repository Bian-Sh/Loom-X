# Loom-x 升级说明

## 数据目录

Loom-x 当前版本只使用 `%LOCALAPPDATA%\LoomX\`：

- 配置库：`LoomX.db`
- 活动库：`LoomX.Activity.db`
- 日志目录：`logs`

应用启动时只创建或初始化上述当前目录和数据库，不再检查、读取或自动迁移 `%LOCALAPPDATA%\OllamaHub\` 下的旧版数据库。已有旧目录和数据库不会被 Loom-x 删除或修改；如需读取旧版数据，请使用旧版本应用或外部数据库工具处理。

## 其他兼容数据

助手偏好和 CLI 版本缓存等独立 JSON 兼容逻辑不属于数据库迁移，仍按各自服务的现有规则处理。