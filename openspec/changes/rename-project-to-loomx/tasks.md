## 1. 结构与技术标识改名

- [x] 1.1 使用 `git mv` 将 `OllamaHub.slnx`、`OllamaHub.Desktop/`、`OllamaHub.Tests/`、项目文件和 `OllamaHubHost.cs` 重命名为 LoomX 对应名称，并保留既有流程产物
- [x] 1.2 更新解决方案项目路径、项目引用、程序集属性和 `InternalsVisibleTo`，使 `LoomX.slnx` 仅引用 `LoomX` 与 `LoomX.Tests`
- [x] 1.3 将活动源码和测试中的 `OllamaHub.*` namespace、`using`、类型名、Avalonia `x:Class`、`using:` 声明和 `avares://` URI 改为 `LoomX.*`
- [x] 1.4 更新静态源码路径断言、测试临时目录名称和所有构建/测试命令引用，确保测试不依赖旧目录

## 2. 运行时身份与用户界面

- [x] 2.1 将窗口标题、导航品牌、设置页、诊断摘要、日志模板和启动提示统一为 `Loom-x`
- [x] 2.2 将单实例互斥锁、Shell 引导互斥锁、环境变量、启动参数和临时快捷方式名称统一为 LoomX 标识
- [x] 2.3 更新网关根响应的产品名、模型列表 `owned_by`、设置页项目主页/问题链接和 `app.manifest` 程序集身份
- [x] 2.4 保持既有 HTTP 路由、请求/响应结构和 Provider/Model 配置语义不变，并补齐相关契约断言

## 3. 应用数据迁移

- [x] 3.1 更新 `AppDataPaths`，定义 `%LOCALAPPDATA%\\LoomX`、`LoomX.db`、`LoomX.Activity.db`、新日志目录和初始化锁路径，并保留旧路径只读常量
- [x] 3.2 实现幂等应用数据迁移组件，使用迁移锁、SQLite 备份 API、临时文件、完整性检查和原子提交
- [x] 3.3 在 `LoomXHost` 创建数据库连接前接入迁移，处理新库优先、旧库保留、无旧库初始化和失败阻止启动规则
- [x] 3.4 为配置库、活动库、WAL、重复启动、目标已存在、源库损坏、完整性失败和 DPAPI 密文保留新增测试入口与测试用例

## 4. 文档与发布脚本

- [x] 4.1 更新 README、`AGENTS.md`、升级说明和相关设计引用中的产品名、路径、命令、日志名和发布入口
- [x] 4.2 更新 `scripts/publish-desktop.ps1`，发布 LoomX 项目并严格校验唯一应用入口为 `LoomX.exe`
- [x] 4.3 在活动源码、项目文件、脚本、用户文档和测试范围内搜索旧名称，确认仅保留迁移兼容代码、迁移测试和升级说明

## 5. 验证与交付

- [x] 5.1 运行 `dotnet test LoomX.slnx`，修复所有编译或测试失败
- [x] 5.2 运行 `dotnet build LoomX.slnx`，确认桌面应用和测试程序集完整构建
- [x] 5.3 运行发布脚本输出到带时间戳的 `outputs` 目录，确认发布包包含 `LoomX.exe` 且不包含旧应用入口
- [x] 5.4 使用发布包验证新安装、旧库迁移、单实例、网关健康检查、配置读取、活动读取和 Loom-x UI 品牌
- [x] 5.5 汇总验证结果，更新 Comet/OpenSpec 状态并准备提交、推送和后续归档

<!-- review skipped: skill unavailable -->
