# 修复方案

1. `LocaleService.SetCulture` 保留默认线程文化设置，并同步设置调用线程的 `CurrentCulture` 与 `CurrentUICulture`，保证 UI 事件回调中的格式化和资源读取立即使用新文化。
2. `ResourceLookup.Resolve(string?)` 改为读取 `LocaleService.CurrentCulture`，避免后台线程或 UI 线程的隐式文化状态造成不一致。
3. `LocaleBinding` 在处理 `CultureChanged` 时使用事件参数中的文化显式解析，保证绑定更新不依赖回调线程的当前文化。
4. 增加 Localization 回归测试，验证即使当前线程 UI 文化被改回旧值，资源默认解析仍跟随 `LocaleService.CurrentCulture`。

不修改资源键、下拉业务值、数据库结构或公开 API。
