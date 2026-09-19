# 任务

- [x] 移除 Settings/Providers ViewModel 对 `DebouncedAutoSaver` 的依赖，改为事件驱动串行保存。
- [x] 为 Provider/Model 连续编辑增加版本化脏状态和数据库回填抑制，保证旧响应不覆盖新编辑。
- [x] 统一可编辑 TextBox 的源更新策略，补充 Provider、Settings、Gateway 交互契约与回归测试，并完成构建验证。
- [x] 修复模型 Enable Toggle 的窄更新与事件过滤，禁止单模型切换触发全量配置读取和透明度应用，并补充日志回归测试。
