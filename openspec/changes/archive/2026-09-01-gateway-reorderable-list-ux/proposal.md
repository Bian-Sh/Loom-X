## Why

网关 Combo 的成员模型列表把新增操作置于列表首行，抓手无法稳定触发拖拽，模型选择器的层级和排序控件也不利于快速扫描。它与桌面端其他模型列表的交互语言不一致，影响故障转移顺序的维护效率。

## What Changes

- 将 Combo 成员模型的新增操作移至列表底部右侧的 footbar。
- 以 Provider 模型列表相同的 `⋮⋮` 抓手作为路由排序的唯一拖拽起点，并使其可稳定调整顺序并保存。
- 将模型选择器的筛选下拉框替换为图标按钮，切换字母升序与降序。
- 将模型选择器改为可折叠的 Provider 分组：分组标题横向填充、右侧显示展开箭头、模型项统一缩进。
- 收紧 footbar 与图标的视觉尺寸，使其弱于页面主新增操作。
- 调整模型选择器的排序图标、折叠箭头和选中标记，使状态表达更清晰且对齐稳定。
- 搜索模型时仅展示模型名称匹配项，不因 Provider 名称匹配而混入无关模型。

## Capabilities

### New Capabilities
- `gateway-reorderable-list`: 维护网关 Combo 成员模型顺序与选择模型的桌面端交互。

### Modified Capabilities

- 无。

## Impact

- `OllamaHub.Desktop/Views/GatewayView.axaml`
- `OllamaHub.Desktop/Views/GatewayView.axaml.cs`
- `OllamaHub.Desktop/ViewModels/GatewayViewModel.cs`
- 新增面向网关视图与 ViewModel 的契约测试。
