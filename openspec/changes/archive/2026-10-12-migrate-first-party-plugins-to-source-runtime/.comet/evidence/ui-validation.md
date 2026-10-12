# 完整产品 CUA 验收

- 2026-10-12：新正式发布目录 outputs/source-plugin-migration-2026-10-12_18-57-37，Start-Process 绝对exe/--loomx-child/Hidden 启动；进程Path核对指向该新包，PID4252。
- cua-driver后台Invoke进入完整MainWindow插件页：已加载2插件、2已启用、0诊断；两卡片分别展示源码插件可信代码边界，没有把ALC/Bridge称为OS沙箱。
- 进入凭据保护和工具结果压缩详情，规则/压缩设置控件及说明正常呈现；仅导航，不切换开关、不编辑/保存真实用户配置，不调用外部Provider、不执行生产Vault内容测试。
- 仅截取该APP窗口；透明主题下截图颜色不用于判断真实主题配色。
- UI验收是完整产品MainWindow，不是隔离壳。业务用合成凭据的动态Pipeline/历史Vault/重载/SSE测试证明；UI不是安全边界证明。
- 安全交付截图仅采用插件总览（mainwindow-source-plugins.png）；真实设置页和初始活动树保留为本地证据，不纳入Git提交。
## 最终修复后包复验

- 新包：`outputs/source-plugin-migration-2026-10-12_19-11-28`；含首个与每64次 Debug 日志采样修复。旧包保留。
- Start-Process 使用绝对 exe、`--loomx-child` 与 Hidden，Process.Path 校验指向新包。最终 PID 30444、HWND 63967560。
- cua-driver 后台 Invoke 导航：完整 MainWindow 的插件页显示 2 个插件、2 个启用、0 条诊断；两个卡片均有源码可信边界披露。
- 最新包分别打开凭据保护规则详情与工具结果压缩详情：动态 UI 正常呈现。仅导航，不修改真实规则/开关/配置，不调用外部 Provider。
- 总览截图经目视核对；透明主题不用于判定真实配色。最终安全截图和树为 final-mainwindow-source-plugins.*；真实详情截图和树保存到 ignored 根 `.comet/evidence/`，不提交。
- 结束时停留压缩详情，用户其它进程未动；只停止本次旧验收包进程以替换新包。