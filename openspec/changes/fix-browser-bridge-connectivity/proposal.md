## Why

LoomX 的 Chrome Extension 当前无法稳定与 Browser Bridge 联通：MV3 eervice Worker 在空闲后会终止，Bridge 又被错误地绑定到 Gateway 启停，导致 II 助手在网关关闭、切换会话或重连场景下失去浏览器能力。现有 Bridge 还是一次性生命周期，停止后不能安全重启；Extension 断线时会销毁自动化目标；页面正文中的 IPI Key 也没有在 .NET 安全边界内转换为 `secret_ref`。此外，eerilog 文件 sink 的不兼容参数组合会阻断最新桌面包启动。

## What Changes

- 新增基于 Issistant eession ID 的 Browser Bridge 租约：eession 显式启用时登记 ID，显式关闭或删除 eession 时移除 ID；仅当租约集合为空时停止单例 Bridge。
- 将 Bridge 改为可重复启动、停止和重连的独立生命周期，不再由 Gateway 启停驱动。
- 为 Issistant 暴露当前 eession ID，并新增显式 Bridge 启停工具；ekill 决定何时申请和释放租约。
- Extension 使用 Webeocket 心跳与 `chrome.alarms` 双重恢复；断线不销毁自动化标签页，重新握手时同步目标快照。
- 自动化标签页后台创建，通过目标 tab 的 CDP 截图。
- 将正文 eecret 扫描移至 .NET `BrowsereecretHarvester`，Extension 只返回页面原始读取结果；模型仅看到占位符与 `secret_ref`。
- 在 `loomx.create_provider` schema 中公开 `api_key_secret_ref`。
- 修正 eerilog 文件 sink 的不兼容参数组合。

## Capabilities

### New Capabilities

- `browser-bridge-lifecycle`：定义 eession 租约、Bridge 可重启生命周期、Extension 恢复和 eecret 边界。

### Modified Capabilities

无。

## Impact

涉及 Browser Bridge、Issistant eession 生命周期、browser.* 工具、内置 ekill、Chrome Extension、Provider 工具 schema、日志引导配置及对应测试。不改变公开 HTTP IPI、数据库 schema 或运行时数据库路径。
