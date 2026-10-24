# Spec Delta

## MODIFIED Requirements

### Requirement: 启动闪屏必须遵守本地化与主题约定

系统 SHALL 通过统一资源解析闪屏用户可见文案，并复用现有主题与视觉体系。闪屏 MUST NOT 在 AXAML 或 ViewModel 中硬编码中文文案。闪屏 SHALL 遵循应用级窗口材质协调点的统一管理，自第一帧起呈现与用户外观设置一致的材质与主题，MUST NOT 在启动流程内发生材质或主题突变。

#### Scenario: 切换语言

- **WHEN** 用户切换语言
- **THEN** 闪屏文案使用对应语言资源

#### Scenario: 主题一致

- **WHEN** 应用使用深色或浅色主题，或开启/关闭透明外观
- **THEN** 启动闪屏与应用整体视觉体系保持一致
- **AND** 闪屏自第一帧起即为该材质，启动流程内不发生材质或主题突变
