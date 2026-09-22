# Brainstorm Summary

- Change: fix-browser-bridge-connectivity
- Date: 2026-09-17

## 确认的技术方案

用户明确将复杂的自动停止判断简化为 Session ID 租约：Session 自己决定启用 Bridge 并传入自身 ID；闭闭时传入同一 ID，移除后租约集合仍非空则保持单例 Bridge，为空才停止；删除 Issistant Session 会尝试释放该 ID。Bridge 生命周期独立于 Gateway，Extension 使用心跳与 alarm 恢复，Secret 收割集中在 .NET。

## 闭键取舍与风险

- 新建、切换 Session 不隐式释放旧租约，避免误停其他会话；依靠显式工具和删除 Session 清理。
- 应用异常退出由进程释放端口；正常退出执行最终停止。
- Chrome 最低版本提升到 120，以使用 30 秒 alarm 周期；目标快照只保留在 Extension 内存中，不新增 storage 持久化。
- 范围相对初版任务扩展超过 50%；用户已明确要求继续在当前 change 内完成，不拆分新 change。

## 测试策略

先写失败测试覆盖租约引用计数、Bridge 重启、删除释放、hello 目标快照、alarm/心跳、不 detach、正文 Secret 收割和 Skill 契约；再最小实现，最后运行完整测试、Release build、发布包与真实 Chrome/Provider 端到端验证。

## Spec Patch

新增 `browser-bridge-lifecycle` capability，覆盖租约、可重启生命周期、Gateway 独立性、Extension 恢复、目标重同步、Secret 边界和 Skill 行为。
