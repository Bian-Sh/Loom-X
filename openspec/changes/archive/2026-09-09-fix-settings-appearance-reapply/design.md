# 修复方案

在 `MainWindowViewModel` 中缓存一个 `SettingsViewModel`，构造主窗口时创建并开始一次配置加载，`ShowSettings` 只切换到该实例。

在 `SettingsViewModel` 的透明外观属性 setter 中，仅当 `suppressAutoSave` 为 false 时调用 `ApplyAppearancePreview`；`LoadAsync` 保持抑制标记期间只赋值字段，并移除加载完成后的显式预览调用。这样应用启动负责初始外观，用户交互负责后续实时变化，页面导航不会产生外观副作用。
