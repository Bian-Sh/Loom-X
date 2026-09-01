# gateway-reorderable-list Specification

## Purpose
TBD - created by archiving change gateway-reorderable-list-ux. Update Purpose after archive.

## Requirements

### Requirement: Combo 成员列表使用底部操作栏

系统 SHALL 在每个已展开 Combo 的成员模型列表底部显示紧凑操作栏，并将较页面级新增操作更小的“添加成员模型”图标按钮放在操作栏右侧。成员列表首行不得仅用于显示新增操作。

#### Scenario: 打开已展开的 Combo

- **WHEN** 用户查看已展开 Combo 的成员模型
- **THEN** 路由单元格先于操作栏显示，新增图标位于底部右侧

#### Scenario: 操作栏保持次要视觉层级

- **WHEN** 用户同时看到页面级新增按钮和成员操作栏
- **THEN** 成员操作栏高度约为首版的三分之二，且其新增图标明显更小

### Requirement: 用户可从抓手拖拽路由排序

系统 SHALL 使用与 Provider 模型列表一致的 `⋮⋮` 图标作为路由抓手。用户从抓手开始拖拽并释放到其他路由上时，系统 SHALL 调整路由顺序、重新编号并保存。

#### Scenario: 拖拽到另一成员模型

- **WHEN** 用户拖拽一个路由的抓手并释放到另一个路由
- **THEN** 两者间的故障转移顺序更新并持久化

### Requirement: 模型选择器按 Provider 折叠展示

系统 SHALL 将模型按 Provider 分组；每个 Provider 标题 SHALL 横向填充弹窗，左侧显示轻量 `>` 折叠箭头，右侧显示模型数量。展开时箭头 SHALL 顺时针旋转 90 度，展开的模型选项 SHALL 相对标题统一缩进。

#### Scenario: 折叠 Provider

- **WHEN** 用户选择展开的 Provider 标题
- **THEN** 该 Provider 的模型选项隐藏且箭头显示折叠状态

### Requirement: 模型选择器使用图标化字母排序

系统 SHALL 不显示模型选择器的文字筛选下拉框。系统 SHALL 显示图标化字母排序按钮，并在点击后切换 Provider 分组的字母升序与降序。

#### Scenario: 切换排序方向

- **WHEN** 用户点击字母排序图标
- **THEN** Provider 分组按相反字母方向重新排序，搜索结果保持当前筛选范围

#### Scenario: 排序图标稳定居中

- **WHEN** 用户查看或切换字母排序方向
- **THEN** 排序图标始终在按钮内容区水平和垂直居中

### Requirement: 模型选择器提供清晰的选择与搜索反馈

系统 SHALL 在已选模型名称后显示绿色对勾，并在名称与对勾之间保留比首版更大的间距。搜索框 SHALL 仅按模型名称筛选，Provider 名称匹配不得使同组内不匹配的模型显示。

#### Scenario: 搜索模型名称片段

- **WHEN** 用户输入 `deep`
- **THEN** 仅显示模型名称包含 `deep` 的模型，名称不匹配的 `ChatGPT-5.6` 不显示

#### Scenario: 查看已选模型

- **WHEN** 模型已经属于当前 Combo
- **THEN** 模型名称后以绿色对勾表示选中，且名称与对勾之间留有清晰间距
