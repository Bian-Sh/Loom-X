# Provider 模型只读目录设计

## 背景

Provider 的模型列表来自上游模型目录接口。当前 Loom-x 同步时只读取模型 ID，随后用本地默认值补齐 Family、上下文长度、最大输出和视觉能力，并在选中模型后提供编辑表单。这会把上游事实与本地猜测混在一起，也容易让中转站配置覆盖 Provider 的模型声明。

LiveAgent 的实现表明，模型目录应优先采信 Provider 返回的元数据：通用字段包括 `id`、`owned_by`，OpenRouter 风格常见 `context_length`、`top_provider.max_completion_tokens`，Gemini 使用 `inputTokenLimit`、`outputTokenLimit`。这些字段并非所有 OpenAI 兼容 Provider 都保证提供，因此缺失时应显示未提供，而不是伪造固定值。

## 目标

- 模型元数据由 Provider 模型列表响应驱动，在 Loom-x 中只读展示。
- 保留本地运行控制：模型启用状态、目录排序、删除本地目录项，以及已有 Combo 路由关系。
- 同步时保存 Provider 返回的规范化元数据，并继续兼容 `data`、`models`、数组三种列表外层格式。
- 模型列表支持拖拽排序，排序结果持久化到现有 `ModelEntity.SortOrder`。
- 删除模型配置编辑表单和手工添加模型入口，避免本地字段覆盖上游模型定义。

## 数据与行为

模型响应新增只读元数据字段：`OwnedBy`、可空的 `ContextLength`、可空的 `MaxTokens`、可空的 `Family`、可空的 `Vision`。同步解析规则如下：

1. ID 优先取 `id`，兼容 `name` 和 `model`。
2. 所有者兼容 `owned_by` 和 `ownedBy`。
3. 上下文优先取 `context_length`，兼容 `top_provider.context_length`；Gemini 取 `inputTokenLimit`。
4. 最大输出优先取 `top_provider.max_completion_tokens`，再取顶层 `max_completion_tokens`；Gemini 取 `outputTokenLimit`。
5. 能力字段只在响应明确声明时保存；无法识别时保持空值。
6. 同步成功后按 ID 合并：上游字段更新，`Enabled` 与 `SortOrder` 等本地运行字段保留。
7. 同步失败或响应无法解析时保留现有模型目录，不删除本地数据。

## 界面

模型目录按 LiveAgent 的模型行布局展示：顶部是与行内开关对齐的“启用全部模型”开关和 `已启用 X / Y 个模型` 统计；每个 cell 依次为点阵拖拽抓手、启用开关、模型 ID、右侧能力图标与 `ctx/out` 摘要、删除图标。图标几何直接采用 LiveAgent `IconSet.tsx` 引入的 Lucide `grip-vertical`、`image` 和 `trash-2`，抓手保留 Lucide 的 24px viewBox 后按 16px 显示。模型搜索按 Model ID 实时筛选，搜索期间禁用排序；能力图标只在上游返回对应能力时显示并提示 `支持图片输入`，上下文元数据单独提示 `AI 提供商模型接口未返回上下文配置`。缺失的上游数值显示为 `-`，元数据列右对齐并向左扩张。选中 cell 不再显示“模型配置”编辑区，也不提供编辑/铅笔按钮或“添加模型”按钮。同步按钮和搜索保留。

模型排序复用网关 Combo 路由的交互模型。开始拖动时从真实集合取出模型并插入固定高度占位项，使用独立预览层跟随指针；移动时按行中心计算插入槽，更新占位项并对受影响行做短动画；释放时移除占位项、恢复模型到目标槽位，并一次性保存完整排序。取消或丢失指针捕获时恢复原始位置。占位项不会计入模型统计、全选开关或同步合并。

## 验证

- 单元测试覆盖 OpenAI 兼容响应、Gemini 响应、缺失元数据和同步合并行为。
- 视图契约测试确认没有编辑表单/手工添加入口，存在拖拽抓手和排序事件绑定。
- 运行 `dotnet test` 和桌面端构建，确认现有网关路由仍按启用模型和排序读取。
