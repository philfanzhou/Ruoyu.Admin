# 数据库文档

Admin Portal 服务的本地数据库文档。本文档是数据库结构的**唯一真实来源**（Single Source of Truth）。

## 数据库概览

| 属性 | 值 |
|------|------|
| 数据库类型 | PostgreSQL（生产）/ SQLite（测试） |
| 数据库名 | `ruoyu_study_admin`（PostgreSQL）/ `admin.db`（SQLite） |
| 连接字符串配置键 | `ConnectionStrings:AuditDb` |
| ORM | Entity Framework Core 8.0 |
| DbContext | `AuditDbContext` |

## 表清单

| 表名 | 说明 | 详细文档 |
|------|------|----------|
| `OssAuditRecords` | OSS 僵尸对象审计记录 | [tables/oss_audit_records.md](tables/oss_audit_records.md) |
| `OssAuditRuns` | OSS 审计运行记录 | [tables/oss_audit_runs.md](tables/oss_audit_runs.md) |

## 实体关系

详见 [relations.md](relations.md)。

## 迁移历史

详见 [migrations.md](migrations.md)。

本项目不使用 EF Core Migrations，而是通过 `DatabaseInitializer` 在启动时执行手动 DDL 创建表结构。

## 已移除的表

无。本项目自创建以来未移除过任何表。

## 声明

> 本文档是 Admin Portal 数据库结构的**唯一真实来源**。任何表结构变更必须先更新本文档及对应的表文档，再修改代码。
> 代码中的实体类（`OssAuditRecord`、`OssAuditRun`）和 `AuditDbContext.OnModelCreating` 以本文档为准。
