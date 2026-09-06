# 高层架构决策

本 change 复用 Phase 1 已建立的本地化基础设施，只补齐视图迁移与英文资源，不改动基线机制。

## 关键决策

### D1. 键命名沿用现有约定

沿用 `docs/i18n.md` 定义的命名空间：

| 视图 | 前缀 |
|---|---|
| Providers | `providers.*` |
| Gateway | `gateway.*` |
| Placeholder | `placeholder.*` |
| 应用级共享 | `app.*` |

新增键必须落在已有命名空间下，避免引入新的 top-level 命名空间。

### D2. AXAML 用 `{l:Locale}`，ViewModel 用 `IStringLocalizer<T>`

- 静态 UI 文案：`Text="{l:Locale providers.create.button}"`
- 动态/格式化文案：ViewModel 内 `_loc["providers.test.success"]` 或 `Loc("providers.test.failure", args)`
- 不引入 `ResourceManager` 直接调用，避免破坏响应式刷新

### D3. en-US 翻译策略

- 品牌/专名保留：`Loom-X`、`Provider`、`Gateway`、`Endpoint`、`Combo`、`Ollama`、`OpenAI`、`Anthropic`
- 语气：开发者工具的简洁正式风格（不俏皮、不过度礼貌）
- 占位符：`{0}`、`{1}` 保留原位置与命名风格
- 大小写：标题（`Title`、`Header`）用 Title Case，正文/按钮用 Sentence case

### D4. 翻译交付方式

- 由助手逐条翻译 zh-CN → en-US，直接写入 `Strings.en-US.resx`
- 本轮不接入 `XliffTasks` NuGet，`.xlf` 协作工作流放到后续 change
- 翻译完成后按 `docs/i18n.md` 命名规范校验键一致性

### D5. 中文残留扫描测试

新增 `LoomX.Tests/LocalizationNoCjkTest.cs`：
- 遍历 `LoomX/**/*.axaml`（排除 `.g.axaml` 生成物）
- 遍历 `LoomX/**/*.cs`（排除 `Resources/`、`*.Designer.cs`、`*.AssemblyInfo.cs`）
- 使用正则 `[一-鿿㐀-䶿]` 检测 CJK 字符
- 命中即失败，输出首个命中位置供定位

测试本身排除路径需在断言说明中清晰列出，防止误伤。

### D6. `.csproj` 卫星程序集

Phase 1 已配置 `<EmbeddedResource>` + `<GenerateResxSource>true</GenerateResxSource>`。Phase 2 需确认：
- `Strings.en-US.resx` 自动识别为 `NeutralResourcesLanguage` 之外的文化文件
- 构建后 `bin/Debug/<tfm>/en-US/LoomX.resources.dll` 存在

## 数据流

```
┌─────────────────────┐         ┌────────────────────┐
│  Strings.resx       │         │  Strings.en-US.resx│
│  (zh-CN, neutral)   │         │  (en-US satellite) │
└──────────┬──────────┘         └─────────┬──────────┘
           │                              │
           └──────────────┬───────────────┘
                          │ compile → satellite assembly
                          ▼
              ┌────────────────────────┐
              │ ResourceManager        │
              │ (LoomX.Strings)        │
              └────────────┬───────────┘
                           │
              ┌────────────┴────────────┐
              ▼                         ▼
   ┌────────────────────┐    ┌────────────────────────┐
   │ LocaleService      │    │ {l:Locale} /            │
   │ (CurrentCulture)   │───▶│ IStringLocalizer<T>     │
   └─────────┬──────────┘    └───────────┬────────────┘
             │ CultureChanged             │
             ▼                            ▼
   ┌─────────────────────────────────────────┐
   │ AXAML 视图 + ViewModel 响应式刷新       │
   └─────────────────────────────────────────┘
```

## 与 Phase 1 的关系

- Phase 1 完成基础设施、Settings 与 Overview/Activity/Console 视图迁移
- Phase 2 补齐 Providers/Gateway 视图与剩余 ViewModel 硬编码，同时补齐英文卫星程序集
- Phase 2 完成后，桌面端可以切换 en-US 并看到全英文 UI；XLIFF 工作流接入放在 Phase 3
