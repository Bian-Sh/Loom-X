## 修复方案

在核心项目中增加一个共享的运行时路径类，以 `Environment.SpecialFolder.LocalApplicationData` 为根目录，统一公开应用数据目录、数据库路径和日志目录。服务端、命令行与桌面端只引用该路径类，避免多个入口再次产生路径偏差。

路径类负责创建应用数据目录和日志目录。SQLite 和 Serilog 保持现有初始化流程，仅替换输入路径。不实现旧应用目录数据迁移，首次运行会在新位置建立全新数据库。

测试直接验证路径位于当前用户 `%LOCALAPPDATA%\OllamaHub` 下，并验证数据库与日志目录的相对结构，防止未来回退到 `AppContext.BaseDirectory`。
