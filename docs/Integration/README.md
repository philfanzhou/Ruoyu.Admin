# 外部系统集成索引

本目录按外部系统组织 Admin Portal 的集成文档。

## 集成点清单

| 外部系统 | 协议 | 方向 | 功能点 |
|----------|------|------|--------|
| IdentityService | HTTP（反向代理） | 出 | 1 个功能点 |
| TeacherPortal | HTTP（反向代理） | 出 | 1 个功能点 |

> 注意：Student Service 和 Mistake Service 通过 gRPC 客户端直接调用，属于内部业务依赖，文档在 [modules/](../modules/) 中维护。

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
| 01-FEATURE.md | 功能概述、用户故事、验收条件 |
| 02-SPEC.md | 详细需求规格、验收标准 |
| 03-DESIGN.md | 文件结构、接口签名、数据依赖 |
| 04-TASKS.md | 任务清单（JSON 格式） |
| 05-TESTS.md | 测试计划（Given-When-Then） |
| 06-CONVENTIONS.md | 命名约定、日志规范、错误消息模板 |
