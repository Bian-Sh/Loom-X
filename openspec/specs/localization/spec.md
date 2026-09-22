# localization Specification

## Purpose
TBD - created by archiving change i18n-phase-2-views-and-en-us. Update Purpose after archive.
## Requirements
### Requirement: UI text must not be hardcoded in Chinese

The localization system MUST resolve all user-visible UI text (AXAML control content, ViewModel Status/toast/derived labels) through the localization layer (`Strings.resx` via `{l:Locale}` or `IStringLocalizer<T>`). The test suite must fail when any hardcoded CJK character is found in `LoomX/Views/**/*.axaml` or in user-visible ViewModel fields.

Log messages (`logger.Log*`) and code comments are exempt — they are not user-facing.

#### Scenario: New AXAML file contains Chinese text

- **Given** an AXAML file under `LoomX/Views/` is added or modified to include a Chinese string literal
- **When** the test suite runs `LocalizationNoCjkTest`
- **Then** the test fails with the file path and line number of the first CJK hit
- **And** the developer must extract the string to `Strings.resx` and reference it via `{l:Locale}` before merging

#### Scenario: ViewModel assigns Chinese string to Status or toast

- **Given** a ViewModel under `LoomX/ViewModels/` assigns a Chinese string literal to a user-visible property (e.g. `Status = "..."`) or passes it to `toastService.Show(...)`
- **When** the test suite runs `LocalizationNoCjkTest`
- **Then** the test fails and the developer must replace the literal with `Loc("<key>")` and add the key to `Strings.resx`

#### Scenario: Log message contains Chinese

- **Given** a ViewModel calls `logger.LogInformation("概览刷新失败")`
- **When** the test suite runs `LocalizationNoCjkTest`
- **Then** the test skips this line because the file content on that line contains `logger.` (log statements are exempt per `AGENTS.md`)

### Requirement: en-US translation coverage parity

`Strings.en-US.resx` MUST contain at least every key defined in `Strings.resx` (zh-CN), and every en-US value must be non-empty. The test suite must fail when coverage drops.

#### Scenario: New key added to zh-CN without en-US counterpart

- **Given** a developer adds `<data name="providers.new.key"><value>新键</value></data>` to `Strings.resx`
- **And** `Strings.en-US.resx` is not updated
- **When** the test suite runs `LocalizationResourceParityTest`
- **Then** the test fails listing the missing key

#### Scenario: en-US value is empty

- **Given** `<data name="providers.x"><value></value></data>` exists in `Strings.en-US.resx`
- **When** the test suite runs `LocalizationResourceParityTest`
- **Then** the test fails listing the empty value

### Requirement: Reactive culture switching

Changing `LocaleService.CurrentCulture` MUST trigger `CultureChanged`, causing all reactive bindings (`{l:Locale}` via `LocaleBinding`) and derived ViewModel properties (via `OnCultureChanged` handlers) to refresh within the same UI frame without app restart.

#### Scenario: User switches language in Settings

- **Given** the app is running with `CurrentCulture = zh-CN`
- **When** the user selects `en-US` in `Settings → Language` and confirms
- **Then** within 100 ms all UI text updates to English without app restart
- **And** any subsequent UI update (page navigation, action status) uses the new language

#### Scenario: Culture changes while an operation is running

- **Given** a long-running operation (e.g. model sync) is in progress with `Status = "正在同步模型…"` (zh-CN)
- **When** the user switches to `en-US`
- **Then** the in-flight Status text stays as-is until the operation completes (non-reactive by design, aligned with Phase 1)
- **And** the operation's next Status assignment uses English

### Requirement: Culture fallback chain

The runtime MUST fall back for missing translations in this order: current culture satellite assembly → neutral (zh-CN) assembly → key name itself. The key-name fallback ensures translators and developers can spot missing translations at runtime without a crash.

#### Scenario: Key missing from en-US satellite

- **Given** a key exists in `Strings.resx` (zh-CN) but not in `Strings.en-US.resx`
- **And** the current culture is `en-US`
- **When** the UI resolves the key via `{l:Locale}` or `Loc(key)`
- **Then** the zh-CN value is returned (satellite missing → neutral fallback)
- **And** the application does not throw

#### Scenario: Key missing from both resources

- **Given** a key does not exist in either `Strings.resx` or `Strings.en-US.resx`
- **When** the UI resolves the key
- **Then** the raw key string is returned (e.g. `providers.save.unknown`)
- **And** the application does not throw

### Requirement: Brand and technical terms are not translated

The localization resources MUST preserve domain-specific terms used across both languages verbatim in en-US to match developer-tool conventions and avoid translation drift:

- Product name: `Loom-X`
- Domain nouns: `Provider`, `Gateway`, `Endpoint`, `Combo`
- Upstream protocol names: `Ollama`, `OpenAI`, `Anthropic`, `DeepSeek`
- Technical identifiers: `Base URL`, `API Key`, `DPAPI`, `HTTP`, `HTTPS`, `URL`
- Provider identifiers shown to users: `openai`, `responses`, `chat`, `agents`, `responses-bridge`

#### Scenario: en-US translation preserves brand terms

- **Given** a zh-CN string reads `添加 Provider`
- **When** translated to en-US
- **Then** the value reads `Add Provider` (not `Add Provider(s)` or `Add supplier`)

### Requirement: Satellite assembly is produced for en-US

The build output MUST include an `en-US/LoomX.resources.dll` satellite assembly alongside the neutral `LoomX.dll`.

#### Scenario: Fresh build after adding en-US.resx

- **Given** `Strings.en-US.resx` is checked in under `LoomX/Resources/`
- **When** `dotnet build` completes
- **Then** `bin/Debug/<tfm>/<rid>/en-US/LoomX.resources.dll` exists on disk
- **And** the main `LoomX.dll` continues to embed the neutral `Strings.resx`

