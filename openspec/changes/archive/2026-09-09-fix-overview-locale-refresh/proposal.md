# 修复概览页语言切换混合状态

## 问题

从中文切换到英文后，Overview 页的静态标签仍显示中文，而网关状态和操作按钮已经显示英文，形成中英混杂状态。

## 根因

`LocaleService.SetCulture` 只更新 `DefaultThreadCurrentCulture` / `DefaultThreadCurrentUICulture`。当前 UI 线程的 `CurrentUICulture` 不会随之改变，导致 `{l:Locale}` 使用的 `LocaleBinding` 继续按旧文化解析；ViewModel 使用默认文化解析，因此两类文案刷新结果不一致。

## 修复目标

- 语言切换后，静态 Locale 绑定和 ViewModel 派生文案使用同一份当前文化。
- Overview 页的标签、状态和按钮在切换后不再出现中英混杂。
- 增加回归测试覆盖当前线程文化同步和资源解析来源。
