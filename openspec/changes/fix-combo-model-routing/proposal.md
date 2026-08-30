# Combo 模型 404 修复

## 问题

网关页 Ollama 卡片把协议路径 `/api` 拼进了地址。Ollama 客户端通常会基于 Base URL 自行追加 `/api`，导致实际请求路径错误；同时请求处理必须严格依据请求 URL 确定 Endpoint，再在该 Endpoint 的 Combo 中解析模型，否则会出现模型发现与请求入口边界不一致。

## 根因

请求虽然已经进入网关，但 Combo 解析必须遵循请求 URL 对应的 Endpoint。当前实现需要明确先确定 Endpoint，再在该 Endpoint 的 Combo 列表中解析请求声明的模型；不能因为其他 Endpoint 存在同名 Combo 就跨边界路由。

## 目标

让各网关协议从请求 URL 对应 Endpoint 的 Combo 目录按请求声明的 Combo 名称解析到最终 Provider/Model，并保留 Combo 成员路由顺序和故障转移行为。
