# Brainstorm Summary

- Change: suppress-meaningless-console-logs
- Date: 2026-09-16

## 确认的技术方案

用户确认将当前日志清理 hotfix 升级为完整 Comet change，并彻底删除旧版 OllamaHub 配置库/活动库自动迁移能力。实现只保留 `%LOCALAPPDATA%\LoomX` 当前数据库路径和既有数据库初始化流程；不删除用户机器上的旧目录。

具体删除 `ApplicationDataMigration.cs`、迁移异常、迁移锁和 `AppDataPaths` 的 `Legacy*` 属性；从 `App` 与 `LoomXHost` 删除迁移调用；删除迁移专用测试；更新当前主规格和升级说明。助手偏好与 CLI 缓存等其他 JSON 迁移不在范围内。

## 关键取舍与风险

- 旧数据不再自动迁移，但旧目录不被应用删除或修改。
- 不保留空迁移包装层，避免旧库扫描和无意义“跳过迁移”日志回归。
- 历史归档文档不改写；仅更新当前主规格、当前 change 和当前升级说明。

## 测试策略

先新增负向契约测试并确认失败，再删除实现。目标覆盖 AppDataPaths、App、LoomXHost 和品牌启动契约；随后执行完整测试、Release 构建、win-x64 发布和 CodeGraph 引用清理检查。

## Spec Patch

已创建 `specs/app-data-migration/spec.md`：修改当前 LoomX 数据路径要求和数据库初始化要求，移除旧库迁移、安全复制、冲突处理和迁移失败处理要求。