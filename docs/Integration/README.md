# 外部系统集成索引

本目录按外部系统组织 Admin Portal 的集成文档。每个集成点包含 6 件套（同 modules/ 约定），覆盖代理逻辑、接口边界和测试信息。

> **何时放在这里 vs modules/**：如果功能的核心逻辑是代理请求到外部系统（路径重写、认证注入、错误转发），放在本目录；如果功能的核心逻辑是业务操作（CRUD、审核、分配），放在 [modules/](../modules/)。Student/Mistake Service 通过 gRPC 客户端直接调用，属于业务依赖，文档在 modules/ 中维护。

## 集成点清单

| 外部系统 | 协议 | 方向 | 功能点 |
|----------|------|------|--------|
| IdentityService | HTTP（反向代理） | 出 | 1 个功能点 |
| TeacherPortal | HTTP（反向代理） | 出 | 1 个功能点 |

---

## IdentityService（身份服务）

| 功能点 | 核心用户故事 | 文档入口 |
|--------|-------------|----------|
| IdentityProxy | 代理请求到身份服务 | [01-FEATURE](./IdentityService/IdentityProxy/01-FEATURE.md) |

## TeacherPortal（教师门户）

| 功能点 | 核心用户故事 | 文档入口 |
|--------|-------------|----------|
| TeacherPortalProxy | 代理请求到教师门户 | [01-FEATURE](./TeacherPortal/TeacherPortalProxy/01-FEATURE.md) |

---

## 文档结构约定

每个集成功能点包含 6 件套文档（同 modules/ 约定）：

| 文件 | 内容 |
|------|------|
| 01-FEATURE.md | 功能概述、用户故事、验收条件、范围外 |
| 02-SPEC.md | 详细需求规格、验收标准（Given-When-Then） |
| 03-DESIGN.md | 文件结构、接口签名、数据依赖 |
| 04-TASKS.md | 任务清单（JSON 格式） |
| 05-TESTS.md | 测试计划（Given-When-Then） |
| 06-CONVENTIONS.md | 命名约定、日志规范、错误消息模板 |
