# OssAuditRuns

OSS 审计运行记录表，记录每次审计任务的执行状态和结果。

## 表信息

| 属性 | 值 |
|------|------|
| 表名 | `OssAuditRuns` |
| 所属数据库 | `ruoyu_admin`（PostgreSQL） |
| 实体类 | `Admin.WebApi.Persistence.OssAuditRun` |
| DbContext | `AuditDbContext` |

## 字段定义

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| `Id` | `bigint` | PK, GENERATED ALWAYS AS IDENTITY | 主键，数据库自动生成 |
| `StartedAt` | `bigint` | NOT NULL | 审计开始时间（Unix 秒） |
| `CompletedAt` | `bigint` | NULL | 审计完成时间（Unix 秒），运行中为 null |
| `Status` | `int` | NOT NULL, DEFAULT 0 | 运行状态，见下方枚举 |
| `NewZombieCount` | `int` | NOT NULL, DEFAULT 0 | v1本次新增只读观察数；旧Run保留历史含义 |
| `TriggerType` | `text` | NOT NULL, DEFAULT 'scheduled' | 触发方式：`scheduled`（定时）/ `manual`（手动） |
| `ErrorMessage` | `text` | NULL | 失败时固定安全原因 |
| `ReferenceContractVersion` | `text` | NULL | 新完整Run的storage-references-v1；旧Run为null |
| `ReferenceSnapshots` | `text` | NULL | 三provider完整metadata JSON；旧Run为null |

## Status 枚举

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | `Running` | 运行中 |
| 1 | `Completed` | 已完成 |
| 2 | `Failed` | 失败 |

## 索引

| 索引名 | 字段 | 类型 | 说明 |
|--------|------|------|------|
| PK | `Id` | UNIQUE | 主键索引 |
| IX_OssAuditRuns_StartedAt | `StartedAt` | NON-UNIQUE | 按开始时间排序查询 |

## DDL（PostgreSQL）

```sql
CREATE TABLE IF NOT EXISTS "OssAuditRuns" (
    "Id"            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "StartedAt"     bigint  NOT NULL,
    "CompletedAt"   bigint  NULL,
    "Status"        integer NOT NULL DEFAULT 0,
    "NewZombieCount" integer NOT NULL DEFAULT 0,
    "TriggerType"   text    NOT NULL DEFAULT 'scheduled',
    "ErrorMessage"  text    NULL,
    "ReferenceContractVersion" text NULL,
    "ReferenceSnapshots" text NULL
);

CREATE INDEX IF NOT EXISTS "IX_OssAuditRuns_StartedAt" ON "OssAuditRuns" ("StartedAt");
```

## 业务规则

- 现有Run创建/双重检查是进程内触发方式的检查，不保证持久排队或进程终止恢复，边界见[未决事项](../../pending-decisions.md)。
- 任一Student/Mistake/Homework完整snapshot验证失败，在任何S3列举前Status=2；S3列举/DB事务失败也失败。取消保存audit_cancelled，其他业务失败只存references_or_scan_unavailable。
- 完整扫描的三份metadata/v1、新观察和完成Run一起提交；旧Run不回填假proof。新metadata非空时Down拒绝丢失历史。
- Id保持GENERATED ALWAYS AS IDENTITY，具体链见[迁移](../migrations.md)。

## 与 OssAuditRecords 的关系

`OssAuditRuns` 和 `OssAuditRecords` 之间**没有外键关系**。它们通过业务逻辑关联：

- 一次审计运行（`OssAuditRun`）会向 `OssAuditRecords` 中插入新只读观察记录
- 但 `OssAuditRecord` 不记录是由哪次 `OssAuditRun` 发现的
- 这种设计意味着无法直接追溯某条审计记录是由哪次运行产生的
