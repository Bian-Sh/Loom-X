# 实现设计

`ProvidersView` 根节点使用两行 Grid：摘要行 `Auto`，工作区行 `*`。工作区使用 `310px + 14px + *` 两列，目录和详情面板 Stretch 填满高度；列表与 Tab 内容在各自的 ScrollViewer 内滚动。主窗口现有内容边距提供 18px 底部留白。

目录卡片继续绑定现有 Provider ViewModel。启用 ToggleSwitch 设置空的 On/Off 内容并添加 Tooltip；右下角删除按钮通过当前卡片对象触发 View 中的确认窗口，确认后复用删除命令和现有持久化服务。右侧详情区以 `HasSelectedProvider` 与新增 `HasNoSelectedProvider` 互斥显示。
