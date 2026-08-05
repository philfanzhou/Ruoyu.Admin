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
| `NewZombieCount` | `int` | NOT NULL, DEFAULT 0 | 本次审计发现的新僵尸对象数量 |
| `TriggerType` | `text` | NOT NULL, DEFAULT 'scheduled' | 触发方式：`scheduled`（定时）/ `manual`（手动） |
| `ErrorMessage` | `text` | NULL | 错误信息，仅在失败时填写 |

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
    "ErrorMessage"  text    NULL
);

CREATE INDEX IF NOT EXISTS "IX_OssAuditRuns_StartedAt" ON "OssAuditRuns" ("StartedAt");
```

## 业务规则

- **并发控制**：同一时间只允许一个审计运行。创建新运行记录时先尝试 `SaveChanges`，若失败（DbUpdateException）则认为已有并发审计在运行，直接退出
- **双重检查**：插入记录后再查询是否存在其他 `Status == 0` 且 `Id != 当前Id` 的记录，若存在则删除当前记录并退出
- **失败处理**：审计过程中若下游服务（Student/Mistake）不可用，将 Status 设为 2（Failed），并记录 ErrorMessage
- **Student 服务不可用**：直接中止审计，标记为 Failed
- **Mistake 服务不可用**：仅记录警告，审计继续执行，但结果可能存在误报（false positives）
- `Id` 使用 `GENERATED ALWAYS AS IDENTITY`，不允许手动指定

## 与 OssAuditRecords 的关系

`OssAuditRuns` 和 `OssAuditRecords` 之间**没有外键关系**。它们通过业务逻辑关联：

- 一次审计运行（`OssAuditRun`）会向 `OssAuditRecords` 中插入新发现的僵尸对象记录
- 但 `OssAuditRecord` 不记录是由哪次 `OssAuditRun` 发现的
- 这种设计意味着无法直接追溯某条审计记录是由哪次运行产生的
