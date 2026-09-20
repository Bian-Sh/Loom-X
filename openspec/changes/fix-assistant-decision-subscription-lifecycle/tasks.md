## 1. 生命周期修复

- [x] 1.1 将 Assistant 决策订阅从视图挂载和通用服务初始化迁移到单次 SendAsync 请求边界，并验证请求结束与页面离开均幂等解除

## 2. 回归测试

- [x] 2.1 更新 AssistantView/AssistantViewModel 生命周期测试，验证页面挂载与导航不会订阅、显式请求激活后仍可提交/取消决策
- [x] 2.2 运行 Assistant 决策定向测试、OpenSpec strict validate 和发布构建，确认无生命周期回归

