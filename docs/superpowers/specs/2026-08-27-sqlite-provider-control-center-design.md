# OllamaHub SQLite 配置中心与 Provider 管理设计

## 1. 背景与目标

OllamaHub 当前从可执行文件同目录的 `settings.json` 一次性加载服务监听、日志、Provider、Model、API Key、Header 与协议配置。代理运行路径已经支持 Provider/Model 继承、OpenAI/Anthropic/Ollama 协议选择、DPAPI 密钥保护和模型元数据暴露，但缺少可写的配置存储、运行时刷新、连接诊断、模型发现与生产管理界面。

本次变更一次性完成当前全部配置向 SQLite 的迁移，并以 LiveAgent 的 Provider 管理交互为参考，交付可实际使用的 OllamaHub Control Center。完成后：

- `OllamaHub.db` 是全部运行配置的唯一真源。
- `settings.json` 只作为首次导入来源，不再参与运行时读取。
- Provider、Model、Header、密钥和日志等级保存后立即生效。
- 监听地址写入数据库后在下次启动生效，并由 API/UI 明确提示需要重启。
- 管理界面可以增删改 Provider/Model、测试连接、同步模型、调整模型顺序和能力。
- 现有 Ollama/OpenAI/Anthropic 代理接口及模型匹配语义保持兼容。

## 2. 范围

### 2.1 本次包含

- SQLite 初始化、版本迁移、事务写入和一致性校验。
- 将当前 `settings.json` 中的所有配置导入 SQLite：
  - `host`、`port`、`url` 及其解析后的监听地址；
  - `logging.level`；
  - 根级 `baseUrl`；
  - Provider、Provider 协议、Header 和 API Key；
  - Model、Model 覆盖项、协议、Header、能力和 `extra`。
- 数据库配置提供者和无锁读取的内存快照。
- 管理 API、本机访问限制和密钥脱敏。
- Provider/Model 生产管理界面。
- 连接测试和远程模型发现。
- 现有 `SetApiKey` 命令改为写入 SQLite。
- 自动化测试、静态界面检查、发布包和人工验收。

### 2.2 本次不包含

- 余额、套餐或 Token Plan 查询脚本。
- 多用户、账号、RBAC 或远程管理后台。
- 多进程同时管理同一个数据库。
- 运行中无中断切换 Kestrel 监听地址。
- 自动删除或改名首次导入使用的 `settings.json`。
- 导入 LiveAgent、Cherry Studio 或 CC-Switch 的配置。

这些能力若以后需要，应在现有数据库迁移框架和管理 API 上独立扩展。

## 3. 总体架构

系统分为五个边界清晰的单元：

1. **配置数据库层**：负责 SQLite 连接、迁移、事务和行级读写，不包含代理协议逻辑。
2. **配置领域层**：把数据库记录解析为经过校验的 `ResolvedAppConfig` 和不可变运行快照，保持现有 Provider/Model 继承语义。
3. **配置管理层**：负责 Provider/Model CRUD、密钥更新、连接测试和模型同步；所有写操作先构造并校验候选快照。
4. **代理运行层**：继续通过 `IOllamaHubConfigProvider` 获取快照，不在请求热路径直接查询 SQLite。
5. **Control Center**：通过同源管理 API 管理配置，不直接访问数据库。

启动数据流：

```text
启动进程
  -> 打开/创建 OllamaHub.db
  -> 执行 schema migrations
  -> 必要时从 settings.json 原子导入
  -> 从数据库构造并验证完整快照
  -> 按快照配置日志与 Kestrel
  -> 启动代理接口和 Control Center
```

运行时写入数据流：

```text
Control Center
  -> 本机管理 API
  -> 串行化写操作
  -> SQLite 事务内写入
  -> 事务内读取并构造候选快照
  -> 校验候选快照
  -> 提交事务
  -> 原子替换内存快照
  -> 返回生效状态和 restartRequired
```

## 4. SQLite 设计

### 4.1 数据库位置与连接策略

- 默认文件：`AppContext.BaseDirectory/OllamaHub.db`。
- 使用 `Microsoft.Data.Sqlite`，不引入 ORM。
- 每次操作创建短生命周期连接；不跨线程共享 `SqliteConnection`。
- 初始化连接时设置：
  - `PRAGMA foreign_keys = ON`；
  - `PRAGMA journal_mode = WAL`；
  - `PRAGMA synchronous = NORMAL`；
  - `PRAGMA busy_timeout = 5000`。
- 写操作由进程内 `SemaphoreSlim` 串行化，并使用显式事务。
- 不支持在应用运行时使用外部 SQLite 工具直接修改数据库；外部修改不会被视为受支持的刷新入口。

### 4.2 迁移表

`schema_migrations`

| 字段 | 类型 | 约束 | 说明 |
| --- | --- | --- | --- |
| `version` | INTEGER | PRIMARY KEY | 单调递增版本号 |
| `name` | TEXT | NOT NULL | 中文或英文迁移标识 |
| `applied_at_utc` | TEXT | NOT NULL | ISO 8601 时间 |

迁移按程序集内固定顺序运行，每个版本单独使用事务。未知的更高数据库版本必须阻止启动，避免旧程序破坏新数据库。

### 4.3 通用应用设置

`app_settings`

| 字段 | 类型 | 约束 | 说明 |
| --- | --- | --- | --- |
| `section` | TEXT | PRIMARY KEY 组成 | 配置域，如 `server`、`logging`、`defaults` |
| `key` | TEXT | PRIMARY KEY 组成 | 配置键 |
| `value_json` | TEXT | NOT NULL | 经过验证的 JSON 值 |
| `updated_at_utc` | TEXT | NOT NULL | 更新时间 |

主键为 `(section, key)`。当前写入：

- `server.urls`：规范化后的监听地址数组；
- `logging.level`：`none/error/warning/info`；
- `defaults.baseUrl`：兼容旧根级 `baseUrl`。

使用通用设置表是为了让以后迁移新配置时不必重复建立新的单行表，但 Provider/Model 等核心实体仍使用关系表和数据库约束。

### 4.4 Provider

`providers`

| 字段 | 类型 | 约束 | 说明 |
| --- | --- | --- | --- |
| `provider_key` | TEXT | PRIMARY KEY | 应用生成的稳定 UUID |
| `provider_id` | TEXT | UNIQUE NOT NULL | 兼容现有配置和 Model 引用的业务 ID |
| `display_name` | TEXT | NOT NULL | UI 名称，旧配置默认等于 `provider_id` |
| `provider_kind` | TEXT | NOT NULL | `openai/anthropic/ollama/custom` |
| `base_url` | TEXT | NULL | Provider 默认上游地址 |
| `enabled` | INTEGER | NOT NULL | 0/1 |
| `sort_order` | INTEGER | NOT NULL | UI 顺序 |
| `created_at_utc` | TEXT | NOT NULL | 创建时间 |
| `updated_at_utc` | TEXT | NOT NULL | 更新时间 |

`provider_protocols`

| 字段 | 类型 | 约束 |
| --- | --- | --- |
| `provider_key` | TEXT | FOREIGN KEY |
| `protocol` | TEXT | `openai/anthropic/ollama` |
| `sort_order` | INTEGER | NOT NULL |

主键为 `(provider_key, protocol)`。

`provider_headers`

| 字段 | 类型 | 约束 | 说明 |
| --- | --- | --- | --- |
| `header_key` | TEXT | PRIMARY KEY | UUID，支持稳定排序和编辑 |
| `provider_key` | TEXT | FOREIGN KEY | 所属 Provider |
| `name` | TEXT | NOT NULL | Header 名称 |
| `value` | TEXT | NOT NULL | Header 值 |
| `is_sensitive` | INTEGER | NOT NULL | UI/API 是否脱敏 |
| `sort_order` | INTEGER | NOT NULL | 请求注入顺序 |

`provider_secrets`

| 字段 | 类型 | 约束 | 说明 |
| --- | --- | --- | --- |
| `provider_key` | TEXT | PRIMARY KEY/FOREIGN KEY | 所属 Provider |
| `api_key_protected` | TEXT | NULL | DPAPI CurrentUser 密文 |
| `updated_at_utc` | TEXT | NOT NULL | 更新时间 |

数据库不保存 Provider 明文 API Key。管理查询只返回 `apiKeyConfigured`。

### 4.5 Model

`models`

| 字段 | 类型 | 约束 | 说明 |
| --- | --- | --- | --- |
| `model_key` | TEXT | PRIMARY KEY | 稳定 UUID |
| `model_id` | TEXT | NOT NULL | 上游模型 ID |
| `config_id` | TEXT | NULL | 同模型多配置别名 |
| `display_name` | TEXT | NOT NULL | UI/匹配显示名 |
| `provider_key` | TEXT | FOREIGN KEY | 所属 Provider |
| `family` | TEXT | NOT NULL | 模型家族 |
| `base_url_override` | TEXT | NULL | 覆盖 Provider 地址 |
| `context_length` | INTEGER | NOT NULL | 上下文窗口 |
| `max_tokens` | INTEGER | NOT NULL | 最大输出 |
| `vision` | INTEGER | NOT NULL | 视觉能力 |
| `tools` | INTEGER | NOT NULL | 工具能力，旧配置默认 true |
| `reasoning` | INTEGER | NOT NULL | 推理能力，导入时可从 `extra` 推断，否则 false |
| `temperature` | REAL | NULL | 默认温度 |
| `top_p` | REAL | NULL | 默认 top_p |
| `enabled` | INTEGER | NOT NULL | 是否暴露和路由 |
| `sort_order` | INTEGER | NOT NULL | `/api/tags` 与 UI 顺序 |
| `extra_json` | TEXT | NOT NULL | 兼容任意上游扩展字段 |
| `created_at_utc` | TEXT | NOT NULL | 创建时间 |
| `updated_at_utc` | TEXT | NOT NULL | 更新时间 |

唯一约束使用 `(model_id, COALESCE(config_id, ''))` 的等价规范化索引，确保 Ollama 暴露名 `model_id` 或 `model_id::config_id` 唯一。

`model_protocols` 与 `provider_protocols` 形状一致，用于模型级覆盖；没有记录时继承 Provider 协议。

`model_headers` 与 `provider_headers` 形状一致。解析快照时先载入 Provider Header，再按不区分大小写的 Header 名用 Model Header 覆盖。

`model_secrets` 保存模型级 DPAPI API Key 覆盖，接口同样只暴露 `apiKeyConfigured`。

### 4.6 导入元数据

`legacy_imports`

| 字段 | 类型 | 约束 | 说明 |
| --- | --- | --- | --- |
| `source_kind` | TEXT | PRIMARY KEY | 固定为 `settings-json-v1` |
| `source_path` | TEXT | NOT NULL | 导入来源绝对路径 |
| `source_sha256` | TEXT | NOT NULL | 导入时文件指纹 |
| `imported_at_utc` | TEXT | NOT NULL | 导入时间 |

此表只记录导入事实，不用于后续同步或覆盖。

## 5. 首次导入与启动规则

启动时严格按以下顺序处理：

1. 打开或创建数据库并完成 schema migration。
2. 判断数据库是否已经拥有有效配置。
3. 若数据库为新建或核心配置为空，且存在 `settings.json`：
   - 完整解析 JSON；
   - 解析旧字段别名和继承关系；
   - 校验 Provider 引用、模型唯一性、URL、协议和数值范围；
   - 将所有明文 API Key 转为 DPAPI CurrentUser 密文；
   - 在单一事务中写入全部设置、Provider、Model 和导入元数据；
   - 从事务内数据构造候选快照，验证成功后提交。
4. 若数据库为新建且不存在 `settings.json`，创建默认配置：
   - `server.urls = ["http://127.0.0.1:11434"]`；
   - `logging.level = "none"`；
   - Provider/Model 为空。
5. 若数据库非空但缺少导入元数据，不自动覆盖现有数据。
6. 从数据库构造最终运行快照。

任何 schema migration、JSON 解析、DPAPI 加密、事务写入或快照校验失败都会阻止启动，并写入尽可能明确且不包含密钥的错误日志。程序不得静默回退到 JSON，也不得留下部分导入数据。

导入成功后 `settings.json` 原样保留。后续对该文件的修改不会生效，README 和 UI 必须明确提示这一点。

## 6. 运行快照与配置生效

`SqliteOllamaHubConfigProvider` 取代当前启动时固定的 JSON Loader，并持有一个不可变 `ResolvedAppConfig` 快照。

- 代理请求只读取当前快照，不直接访问 SQLite。
- 快照替换使用原子引用交换，读取方不加锁。
- 管理写操作在事务提交前构造候选快照，确保提交的数据一定能被运行层解析。
- Provider、Model、协议、Header、密钥和默认上游地址在快照替换后立即用于新请求。
- 已经发出的流式请求继续使用请求开始时获取的模型配置，不在中途切换。
- 日志等级通过动态日志等级提供者即时更新。
- `server.urls` 保存成功后返回 `restartRequired: true`；当前监听保持不变，下次启动读取新值。

## 7. 管理 API

生产 UI 位于 `/admin/`，管理 API 位于 `/api/admin`。现有根路径和代理协议接口保持兼容。

### 7.1 应用设置

- `GET /api/admin/settings`
- `PUT /api/admin/settings/server`
- `PUT /api/admin/settings/logging`

Server 更新响应包含当前监听、已保存监听和 `restartRequired`。日志更新响应包含实际生效等级。

### 7.2 Provider

- `GET /api/admin/providers`
- `POST /api/admin/providers`
- `GET /api/admin/providers/{providerKey}`
- `PUT /api/admin/providers/{providerKey}`
- `DELETE /api/admin/providers/{providerKey}`
- `PUT /api/admin/providers/{providerKey}/api-key`
- `DELETE /api/admin/providers/{providerKey}/api-key`
- `POST /api/admin/providers/{providerKey}/test`
- `POST /api/admin/providers/{providerKey}/models/sync`

删除仍被 Model 引用的 Provider 返回 `409 Conflict`，并列出关联模型摘要；不进行隐式级联删除。

### 7.3 Model

- `GET /api/admin/models`
- `POST /api/admin/models`
- `GET /api/admin/models/{modelKey}`
- `PUT /api/admin/models/{modelKey}`
- `DELETE /api/admin/models/{modelKey}`
- `PUT /api/admin/models/{modelKey}/api-key`
- `DELETE /api/admin/models/{modelKey}/api-key`
- `PUT /api/admin/models/order`

API 使用稳定的 UUID Key 定位实体，允许业务 `provider_id`、`model_id` 或 `config_id` 在校验通过时修改。

### 7.4 错误契约

管理 API 统一返回：

```json
{
  "error": {
    "code": "provider_in_use",
    "message": "Provider 仍被 3 个模型使用。",
    "field": null,
    "details": {}
  }
}
```

- `400`：请求格式或字段校验失败；
- `404`：实体不存在；
- `409`：唯一性冲突、引用冲突或并发版本冲突；
- `422`：配置结构合法但无法形成可运行快照；
- `502`：上游连接测试或模型同步失败；
- `500`：数据库或未预期错误。

错误消息不得包含 API Key、敏感 Header 值或完整上游响应正文。

## 8. 本机安全边界

管理能力比代理能力更敏感，必须同时满足：

- 请求远端地址是 IPv4/IPv6 loopback；
- 不为管理 API 启用跨域 CORS；
- 浏览器发起的写请求必须同源，并携带 `X-OllamaHub-Admin: 1`；
- 管理 UI 只通过相对路径调用同源 API；
- 所有读取响应对 API Key 和敏感 Header 脱敏；
- 日志中只记录 Provider/Model 标识、状态码、耗时和安全摘要；
- DPAPI 密文只在实际发起上游请求时解密，明文不进入长期快照序列化、API 响应或日志。

本次不提供远程管理开关。即使代理监听配置为 `0.0.0.0`，管理 API 仍只接受本机请求。

## 9. 连接测试与模型同步

### 9.1 连接测试

连接测试根据 `provider_kind` 和协议配置选择最小、只读请求：

- OpenAI：优先 `GET /v1/models`，使用 Bearer Key；
- Anthropic：`GET /v1/models`，使用 `x-api-key` 和 `anthropic-version`；
- Ollama：`GET /api/tags`，API Key 可为空；
- Custom：按已选择的发现协议执行对应策略。

结果返回成功状态、HTTP 状态、耗时、实际协议和安全错误摘要。默认超时 15 秒，不自动重试，避免一次点击造成多次上游请求。

### 9.2 模型同步

同步只返回远程发现结果和本地差异预览，不直接隐式删除本地模型：

- 新模型：可批量选择后添加；
- 已存在模型：保留本地显示名、能力、Header、`extra` 和启用状态；
- 远程已消失模型：标记为“远程未发现”，由用户决定是否停用或删除；
- 导入模型继承 Provider 协议和密钥，不复制 Provider 密钥到 Model。

用户确认新增/更新后才通过普通 Model 写入事务保存。

## 10. Control Center 设计语言

生产界面复用现有 `.design/pages/providers/index.html` 的 OllamaHub 深色视觉语言，并吸收 LiveAgent Provider 管理中的信息架构：

- 左侧 Provider 目录，显示品牌/类型、连接状态、Base URL、协议、模型数和密钥状态。
- 右侧编辑区按三个面板组织：
  - **基础**：名称、ID、类型、Base URL、启用状态；
  - **请求**：协议、API Key、自定义 Header、连接测试；
  - **模型**：远程同步、搜索、启停、排序、能力和参数编辑。
- API Key 永远以“未配置/已配置”表达，更新动作进入单独输入状态，不回显旧值。
- 模型同步先展示差异，再由用户确认写入。
- 保存、测试、同步分别拥有独立的加载、成功和失败反馈。
- 小屏幕下 Provider 目录折叠为顶部选择区，编辑面板单列显示。
- 键盘焦点、错误字段关联、状态区域 `aria-live` 和减少动画偏好保持可访问。

为控制复杂度，生产前端采用 ASP.NET Core 静态文件承载的原生 HTML/CSS/JavaScript，不引入 Node 构建链或 SPA 框架。`.design` 仍是原型源，生产资源放在应用自身的 `wwwroot/admin`，不让运行代码依赖原型目录。

## 11. 验证规则

候选快照至少验证：

- 监听地址非空且为受支持的 HTTP/HTTPS URL；
- 日志等级属于已支持集合；
- Provider ID 和显示名非空且唯一；
- Provider Kind 和协议值受支持；
- Header 名称/值可安全用于 .NET HTTP 请求，禁止 CR/LF；
- Model ID、Ollama 暴露名唯一；
- Model 引用存在的 Provider；
- 上下文窗口和最大输出为正数；
- `temperature`、`top_p` 为有限数值；
- `extra_json` 是 JSON Object；
- 启用的模型最终能解析出 Base URL、支持协议和 API Key；Ollama 或明确无需鉴权的 Provider 可为空 Key。

无效配置不能写入数据库，也不能替换运行快照。

## 12. 测试与验收

### 12.1 自动测试

- SQLite schema 创建与逐版本迁移。
- 新数据库默认配置。
- 完整 `settings.json` 首次导入。
- 明文/受保护 API Key 导入与数据库无明文断言。
- 导入失败事务回滚。
- 数据库存在后忽略 JSON 后续变化。
- Provider/Model 继承、协议、Header、`extra` 和别名兼容。
- CRUD、唯一性、引用拒绝、排序和候选快照校验。
- 快照原子更新与并发读取。
- 动态日志等级更新。
- 监听地址保存后的 `restartRequired`。
- 管理 API loopback、同源、脱敏和错误契约。
- OpenAI/Anthropic/Ollama 连接测试与模型同步的模拟 HTTP 测试。
- 现有代理、转换、流式响应和 `/api/tags`、`/api/show` 回归测试。

### 12.2 UI 验收

- 桌面与窄屏布局。
- 新增、编辑、删除 Provider。
- API Key 状态、更新和清除。
- Header 编辑与校验。
- 连接测试成功/失败/超时。
- 模型同步差异预览、批量添加、启停、排序和能力编辑。
- 日志等级即时生效提示。
- 监听地址重启提示。
- 键盘操作、焦点、屏幕阅读器状态和减少动画。

### 12.3 发布验收

修改完成后执行 Release 发布，将带时间戳的可运行包放入 `outputs` 目录。发布包必须包含静态管理资源，并验证：

- 无数据库时可从同目录 `settings.json` 导入；
- 导入后代理接口和 Control Center 均可使用；
- 重启后从 SQLite 恢复全部配置；
- 发布目录中的 `settings.json` 修改不再影响运行配置；
- 数据库和日志文件路径可预测且有明确文档。

## 13. 实施边界与兼容策略

- 保留现有配置 DTO 和 JSON 解析器作为“旧配置导入模型”，但不再把它注册为运行时配置提供者。
- 尽量保持 `IOllamaHubConfigProvider` 和 `ResolvedModelConfig` 的调用方式，减少代理代码改动。
- `SetApiKey <providerOrModelId> <apiKey>` 保持命令形式，内部改为通过业务 ID 查找并写入 SQLite；歧义时明确报错。
- `/api/tags` 顺序改为数据库 `sort_order`，默认导入顺序与旧 JSON 一致。
- `/api/show` 与聊天请求的模型匹配优先级保持：Ollama 暴露名、显示名、模型 ID。
- 不修改与本次配置中心无关的代理转换规则和中文文案。

## 14. 完成标准

满足以下条件才视为完成：

1. 所有当前配置均只从 SQLite 读取，运行代码不再回读 `settings.json`。
2. 首次导入具备原子性、幂等判定和密钥保护。
3. Control Center 的 Provider/Model 核心流程可用。
4. 管理 API 只允许本机访问且不泄露密钥。
5. 配置热刷新、动态日志和监听重启语义符合设计。
6. 现有代理测试和新增数据库/API/UI 测试通过。
7. Release 发布包已生成到带可读时间戳的 `outputs` 目录，并完成真实运行验收。
