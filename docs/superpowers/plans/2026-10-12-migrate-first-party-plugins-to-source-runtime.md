---
change: migrate-first-party-plugins-to-source-runtime
design-doc: openspec/changes/migrate-first-party-plugins-to-source-runtime/design.md
base-ref: 355f31d3cbd6e57b53cf35be18521a2fa19a4e3a
archived-with: 2026-10-12-migrate-first-party-plugins-to-source-runtime
---

# 第一方插件源码迁移实施计划

<!-- comet-task-authority: openspec/changes/migrate-first-party-plugins-to-source-runtime/tasks.md -->

设计与约束以正式design/spec为准。当前目录是唯一开发位置，串行实现核心依赖，standard独立审查；保留其他会话产物。任务完成只有tasks.md一份权威。本计划不复写实现代码。

## Protection契约
<!-- comet-task-ref:ddb0af83-e457-40da-940e-c5339e893511 -->

范围公共契约和Scope。先添加反射兼容/授权/退休测试，运行PluginProtectionContractTests确认缺失接口RED，再新增默认成员和可选构造参数，验证GREEN。旧构造不得破坏；未知版本和取消需稳定处理。

## 宿主保护适配器
<!-- comet-task-ref:4e3aeb40-0929-4a21-89fc-d5b21ad89279 -->

依赖契约。PluginHost持有ProtectedData实现和引用；宿主、Playground和自测组合点接入。Windows历史密文、失败/取消/日志canary测试先RED后GREEN。非Windows只验证不可用，不扩展后端。

## 凭据业务组合
<!-- comet-task-ref:9271a759-7110-47cf-99f6-51cb56e7b59b -->

依赖保护能力。只替换加解密调用及构造，不改变SQL/哈希/冲突/生命周期。先测试缺少显式保护或保护失败行为，再实现；运行CredentialStorageBridgeTests、CredentialStorageMigrationTests与凭据相关全集。回滚代码无需迁库。

## 凭据动态负载
<!-- comet-task-ref:26e1c0e4-e1b1-443d-a969-6253a8fd4da9 -->

依赖业务组合。精确manifest sources、显式using与英文资源；真实SourcePluginCompiler/Runtime测试在旧assembly声明下RED，再迁移通过。验证历史Vault、请求mask/完整性、响应恢复、普通禁用和重载。Runtime段落交独立审查者审查，最多一次定向修复复查。

## 压缩动态负载
<!-- comet-task-ref:154ae18d-77ae-4e44-85d3-b85cd711dd82 -->

在凭据迁移后切换压缩源码manifest与显式using。真实动态压缩测试先RED后GREEN，验证设置、本地化及UI contribution；不修改压缩业务。

## 精确负载与发布
<!-- comet-task-ref:a2b28ecd-5ac4-47ce-b016-934575f8a876 -->

宿主、测试、Playground输出一致；先以源码布局断言RED，再改目标精确拷manifest、四语言和源码；禁止递归bin带依赖。运行PluginPayloadLayoutTests/PluginPublishContractTests与正式publish。不能清理既存outputs，只创建当前时间新目录。

## 完整验收
<!-- comet-task-ref:7b021879-ef1a-4925-9236-a5ac3ad473ed -->

完成相关完整测试与风险段独立审查；Feedback最多一次修复原审查者复查。产品发布到outputs时间目录，后台启动并通过CUA验收完整MainWindow可加载两插件；透明主题不以截图判断配色。保存实际证据和既存警告，不冒称零警告。最终集成审查由Verify唯一执行。

## 交付闭环
<!-- comet-task-ref:376a839a-5174-4c26-aa6c-cfbc0affba52 -->

保存检查点、报告与中文提交；Comet Build/Verify守卫后归档，同步delta规格，精确提交推送当前分支并记录远端验证。不合并master、不提交evidence既存未跟踪项；最终报告实际完成/未完成范围。
