# 实施裁定

- 2026-10-12：旧Scope构造必须保留五参数元数据签名；仅在末尾添加optional参数会使反射/已编译调用找不到原构造。已用既有PluginCapabilityTests真实失败确认，并恢复五参数转发重载。
- 2026-10-12：采用现有`csharp-source` manifest，sources显式枚举插件顶层业务cs及GlobalUsings.cs，不移动源码、不增加清单格式；动态编译器已有英文资源嵌入，不复制宿主本地化特判。
- 2026-10-12：PowerShell comet.ps1 shim会丢失子命令分隔符；公开comet.cmd入口可正常`check run`。避免给cmd shim传含管道字符的筛选；Comet检查以单一筛选或全集执行，不调用内部Runtime文件绕过。
- 2026-10-12：隔离运行与测试只创建本次临时目录，不清理历史outputs/evidence；正式发布使用新时间目录。源码、配置和Vault的正常运行时唯一位置约束不变。
- 2026-10-12：动态流式补充测试首轮16/17通过，唯一失败为测试期待InvalidDataException，而既有生产SSE包装器明确在EOF抛InvalidOperationException（ProviderExecutionPipeline.cs:515）。依据真实栈和代码纠正测试类型，不改业务、不将该失败冒称功能RED。

- 2026-10-12：任务4.3只验收交付资料准备，实际Verify/Archive/推送由后续Comet阶段执行，避免Build守卫必须全勾选与尚未进入归档形成循环。范围与最终交付要求不变。

- 2026-10-12：Comet子进程确实拒绝创建文件/目录symlink及Junction（b23a899f日志），同用户直接运行则成功且九项链接回归9/9通过。未修改安全代码或降低断言。Runtime记录相关检查显式排除环境不能执行的链接夹具，直接完整测试覆盖其真实安全行为；两种结果均报告。默认输出运行全集避免既有View测试固定目录假设，不扩展修改无关UI测试。

- 2026-10-12：主会话复核发现Protection成功Info会随每个历史placeholder逐引用写日志，与高频日志约束冲突。新增合成128次恢复测试真实RED显示129事件；改为首个及每64次采样Debug，不改安全/加密语义。算Runtime一次定向修复，交原reviewer复查，不接受该日志缺陷。
