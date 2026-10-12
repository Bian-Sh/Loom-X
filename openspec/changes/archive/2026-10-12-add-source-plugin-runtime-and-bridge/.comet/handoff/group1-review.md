# 首组审查与修复记录

2026-10-12：standard 独立首审 Mencius 报告 IMPORTANT 2、CRITICAL 0。

- 注册中途失败遗留 Entry：补原子注册回归，失败清理先撤销候选归属，再释放实例/Bridge/ALC。
- 已声明能力缺少适配器：Scope 拒绝缺失实现，Runtime 在执行插件构造前创建和校验作用域。
- 真实修复 RED：contracts/REVIEW-RED.trx，2 失败、3 通过。
- 修复 GREEN：contracts/REVIEW-GREEN.trx，108 通过；首组仍等待唯一一轮定向复查，不预先验收。
- Manifest RED 原始结果尚未定位，只有前代理说明，不能视为已独立核实。
- feedback/RED.trx：6 失败，原因是 Logging/Notifications 服务缺失；feedback/GREEN.trx：6 通过。
- 恢复构建前 NETSDK1004 属于缺失 SQLite 项目恢复资产，不计为 TDD RED。
- 宿主与旧 Credential 插件仍有 SQLitePCLRaw 2.1.11 NU1903，后续迁移及宿主统一版本时必须验证消除；新 SQLite 项目已使用 2.1.13。
定向复查 Mencius 已关闭两 IMPORTANT，允许1.1–1.5验收。先提交公共契约与Manifest（1.1–1.3）；Runtime正新增代际实现，1.4–1.5将随对应完整可构建提交验收，不把在途代码混入首个进度提交。
