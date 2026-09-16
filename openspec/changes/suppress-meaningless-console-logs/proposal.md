## Why

桌面端当前会把两类不影响正常运行的诊断事件展示到用户可见控制台：Windows Shell 子进程启动失败后的正常回退，以及透明外观每次应用时的开始/完成明细。同时，启动流程仍保留从旧版 OllamaHub 数据目录复制数据库的迁移能力，会在目标库已存在时输出“跳过旧库迁移”信息。当前产品已固定使用 `%LOCALAPPDATA%\LoomX`，不再需要兼容旧库迁移。

## What Changes

- 将 Windows Shell 自启动子进程失败但继续当前进程的日志从 `Warning` 调整为 `Debug`。
- 将透明外观应用开始和完成日志从 `Information` 调整为 `Debug`。
- 删除旧版 OllamaHub 配置库和活动库到 LoomX 数据库的自动迁移逻辑、迁移锁、临时快照和迁移异常类型。
- 删除启动流程中的迁移调用；应用只初始化并使用 `%LOCALAPPDATA%\LoomX` 下的目标数据库。
- 删除仅用于旧库迁移的测试、路径常量和过时的升级说明，并更新 `app-data-migration` 规格。

## Capabilities

### New Capabilities

无。

### Modified Capabilities

- `app-data-migration`: 保留 LoomX 当前数据路径约束，移除旧 OllamaHub 数据库自动迁移及其安全复制、冲突处理和失败回滚要求。

## Impact

影响 `LoomX/App.axaml.cs`、`LoomX/LoomXHost.cs`、`LoomX/AppDataPaths.cs`、`LoomX/ApplicationDataMigration.cs`、相关测试、`openspec/specs/app-data-migration/spec.md` 和 `docs/loomx-upgrade.md`。不新增 API；会删除仅供内部启动迁移使用的类型和路径属性。与助手偏好、CLI 缓存等其他 JSON 迁移无关的逻辑保持不变。