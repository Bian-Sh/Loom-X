---
comet_change: fix-browser-bridge-connectivity
role: technical-design
canonical_spec: openspec
language: zh-CN
---

# LoomX Browser Bridge Session 租约与 Chrome 恢复技术设计

## 1. 目标与边界

本变更让 Browser Bridge 成为桌面进程内的可重启单例，由 Assistant Session 显式租约控制，而不是跟随 Gateway 或 UI 导航。Chrome Extension 负责 Chrome API、WebSocket 和 target 映射；LoomX .NET 侧负责生命周期、工具、Secret 收割与 Provider 写入。

不新增远程控制入口，不读取用户普通标签页，不持久化浏览器明文 Secret，不让 Gateway 状态参与 Bridge 决策。

## 2. 组件

```text
Assistant Session（系统提示含 Assistant Session ID）
        │ browser.bridge_start/stop(assistant_session_id)
        ▼
BrowserBridgeLeaseManager（Singleton，HashSet<string>）
        │ first acquire / last release
        ▼
BrowserBridge（Singleton，可 StartAsync/StopAsync/Restart）
        │ WebSocket 127.0.0.1:17831
        ▼
Chrome MV3 Extension（heartbeat + alarms + target snapshot）
```

- `BrowserBridgeLeaseManager` 是租约集合唯一写入口，串行化集合与 Bridge 状态变更。
- `BrowserBridge` 不理解 Assistant Session，只管理监听器、socket、命令和 Chrome target 快照。
- `BrowserTools` 注册生命周期工具与现有 browser.* 操作；生命周期工具返回监听状态、连接状态和安全租约摘要。
- `AssistantService` 为每个新建/载入会话生成包含自身 ID 的系统提示；删除会话时调用租约管理器释放。

## 3. 租约算法

`AcquireAsync(id)`：

1. 校验 ID 非空。
2. 在互斥区内判断集合是否已有 ID；已有则直接返回当前状态。
3. 若集合为空，先启动 Bridge；启动成功后再加入 ID，失败不修改集合。
4. 若集合非空，直接加入 ID。

`ReleaseAsync(id)`：

1. 在互斥区内移除 ID；不存在时幂等返回。
2. 若移除后集合非空，保持 Bridge。
3. 若集合为空，调用 `StopAsync()`。

删除 Session 仅作为意外残留兜底：先尝试释放再删除文件；释放失败记录 Warning 并继续删除，避免临时端口清理问题阻止用户删除历史。应用退出直接 Dispose Bridge，租约集合不落盘。

## 4. Bridge 状态与并发

Bridge 用异步互斥保护生命周期，状态最少包含 `IsListening`、`IsExtensionConnected`。每次启动创建新的生命周期 `CancellationTokenSource` 和监听/接收任务；`HttpListener.Stop()` 后允许再次 `Start()`。最终 dispose 才关闭 listener 和 semaphore。

停止步骤：标记停止意图、取消 token、关闭/abort socket、停止 listener、等待接收和监听任务、清理服务端 target/待响应状态。命令发送链接调用方 token 与当前生命周期 token；因停止取消时返回安全的 offline/停止错误，不泄露内部异常。

## 5. Extension 恢复

Manifest 增加 `alarms` 权限和 `minimum_chrome_version: "120"`。Service Worker 初始化时确保创建 0.5 分钟周期 alarm；`onInstalled`、`onStartup`、`onAlarm` 和脚本加载调用同一幂等 `connect()`。WebSocket 打开后每 20 秒发送 keepAlive。

断线时不调用 `detachAllTargets()`。hello 参数包含 targets 数组；服务端验证协议后以快照替换自身 target 表。由于 Chrome 120 中活动 WebSocket、alarm 与 debugger 会话共同维持恢复，本次不引入 chrome.storage。

## 6. Secret 数据流

```text
页面/Network 原始结果
  → BrowserBridge
  → BrowserSecretHarvester
      ├─ 敏感字段值
      └─ content/html/markdown 严格 Key regex
  → BrowserSecretVault(DPAPI)
  → 占位符 + harvested_secrets[{secret_ref,hint}]
  → 模型
  → loomx.create_provider(api_key_secret_ref)
```

Extension 不包含 Key 正则。Harvester 必须先 clone，再替换所有命中；不得把明文放入日志、异常或测试失败消息。正文扫描只使用严格形态并设置合理最小长度，降低误收割。

## 7. Skill 行为

`LoomX/Skills/relays/new-api/SKILL.md` 增加：

1. 从系统提示读取当前 Assistant Session ID。
2. 需要浏览器时先 `browser.bridge_start`。
3. 完成后关闭本 Session 创建的 automation tabs，再 `browser.bridge_stop`。
4. 若后续步骤仍需浏览器，可保持租约；不得释放其他 Session ID。
5. 删除 Session 作为意外残留的被动兜底，不替代 AI 主动调用 `browser.bridge_stop` 的正常路径。

## 8. 测试与交付

- 租约管理器使用真实可控 fake lifecycle，验证 first/last、去重、失败回滚。
- BrowserBridge 使用真实回环 WebSocket，验证停止重启、连接状态、hello 快照。
- AssistantService/ViewModel 验证删除 Session 释放租约和系统提示包含 ID。
- BrowserSecretHarvester 验证正文替换、多个命中、结构化头、无误收割和 vault 可解析。
- 源码契约验证 manifest、alarm、heartbeat、不 detach、Skill、Gateway 无 Bridge 启停。
- 完整测试与 Release build 通过后，按可读时间命名发布到 `outputs/`，再做真实 Chrome 和 Provider 录入。
