## 实现设计

- 逐页删除 `<header class="topbar">` 内右侧状态/操作节点，仅保留面包屑与移动端菜单。
- 将各页 `.toast` 从 `position: fixed` 右下角改为顶栏右侧定位：使用 `position: absolute`、`top: 50%`、`right` 间距和 `transform` 过渡；顶栏增加 `position: relative`。
- 保留现有 `showToast`/`notice` 逻辑、`role="status"` 与 `aria-live="polite"`，只删除失效 DOM 引用和事件绑定。
- 移动端将气泡限制在视口宽度内，仍锚定顶栏右侧，不遮挡面包屑。

## 验证

- 运行 `.design/scripts/validate.ps1`。
- 搜索五页确认不存在顶栏右侧常驻状态/刷新节点及其失效脚本引用。
