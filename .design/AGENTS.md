# `.design` 原型维护规则

- 使用中文维护文档、代码注释和提交说明。
- `.design` 是 Codex 的唯一原型编辑源；`.kun-design` 只读。
- 修改前先阅读 `manifest.json`、`design.md` 和相关页面。
- 当前主线固定为五页：`overview`、`gateway`、`providers`、`activity`、`console`。
- 页面路径固定为 `pages/<key>/index.html`，不要改成随机 ID 或复制出第二个主源。
- 保持 HTML 自包含和无构建依赖；不要无需求引入框架。
- 所有页面导航必须指向 `.design/pages/*/index.html`。
- 修改页面或清单后运行 `pwsh -File .design/scripts/validate.ps1`。
- 不删除、移动或清理 `.kun-design`、`.kun-canvas` 或其他历史产物。
