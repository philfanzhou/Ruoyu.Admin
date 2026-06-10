# OssAuditRecords

OSS 僵尸对象审计记录表，存储被检测到未被任何业务引用的 OSS 对象信息。

## 表信息

| 属性 | 值 |
|------|------|
| 表名 | `OssAuditRecords` |
| 所属数据库 | `ruoyu_study_admin`（PostgreSQL）/ `admin.db`（SQLite） |
| 实体类 | `Admin.WebApi.Data.OssAuditRecord` |
| DbContext | `AuditDbContext` |

## 字段定义

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| `Id` | `bigint` | PK | 主键，自增 |
| `ObjectPath` | `string` | NOT NULL, UNIQUE | OSS 对象路径，如 `uploads/xxx.jpg`、`mistakes/xxx.jpg` |
| `Bucket` | `string` | NOT NULL | 所属 Bucket 名称，取值：`uploads`、`mistakes`、`questions` |
| `Size` | `bigint` | NOT NULL | 对象大小（字节） |
| `LastModified` | `bigint` | NOT NULL | 对象最后修改时间（Unix 秒） |
| `Status` | `int` | NOT NULL, DEFAULT 0 | 审计状态，见下方枚举 |
| `CreatedAt` | `bigint` | NOT NULL | 记录创建时间（Unix 秒） |
| `ResolvedAt` | `bigint` | NULL | 记录处理时间（Unix 秒），未处理时为 null |
| `Note` | `string` | NULL | 备注，忽略记录时可填写原因 |

## Status 枚举

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | `Pending` | 待处理，新发现的僵尸对象 |
| 1 | `Resolved` | 已解决，OSS 对象已被删除，审计记录也随之删除 |
| 2 | `Ignored` | 已忽略，管理员确认保留该对象 |

> **注意**：`Resolved` 状态的记录在实际流程中会被直接从数据库中删除（`Remove`），而非仅更新状态。因此数据库中通常只会存在 `Pending` 和 `Ignored` 状态的记录。

## 索引

| 索引名 | 字段 | 类型 | 说明 |
|--------|------|------|------|
| PK | `Id` | UNIQUE | 主键索引 |
| IX_OssAuditRecords_Status | `Status` | NON-UNIQUE | 按状态筛选审计记录 |
| IX_OssAuditRecords_Bucket | `Bucket` | NON-UNIQUE | 按 Bucket 筛选 |
| IX_OssAuditRecords_ObjectPath | `ObjectPath` | UNIQUE | 确保同一对象路径不会重复记录 |
| IX_OssAuditRecords_CreatedAt | `CreatedAt` | NON-UNIQUE | 按创建时间排序查询 |

## DDL（PostgreSQL）

```sql
CREATE TABLE IF NOT EXISTS "OssAuditRecords" (
    "Id"        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "ObjectPath" text    NOT NULL,
    "Bucket"     text    NOT NULL,
    "Size"       bigint  NOT NULL,
    "LastModified" bigint NOT NULL,
    "Status"     integer NOT NULL DEFAULT 0,
    "CreatedAt"  bigint  NOT NULL,
    "ResolvedAt" bigint  NULL,
    "Note"       text    NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS "IX_OssAuditRecords_ObjectPath" ON "OssAuditRecords" ("ObjectPath");
CREATE INDEX IF NOT EXISTS "IX_OssAuditRecords_Status" ON "OssAuditRecords" ("Status");
CREATE INDEX IF NOT EXISTS "IX_OssAuditRecords_Bucket" ON "OssAuditRecords" ("Bucket");
CREATE INDEX IF NOT EXISTS "IX_OssAuditRecords_CreatedAt" ON "OssAuditRecords" ("CreatedAt");
```

> **注意**：`OssAuditRecords` 表由 EF Core `EnsureCreated` 自动创建，不使用 `DatabaseInitializer` 中的手动 DDL。`OssAuditRuns` 表则通过 `DatabaseInitializer` 手动创建。

## 业务规则

- 同一 `ObjectPath` 只能存在一条记录（UNIQUE 约束）
- 审计发现新僵尸对象时，先检查 `ObjectPath` 是否已存在，避免重复插入
- Resolve 操作会先验证对象是否仍被 Student/Mistake 服务引用，验证通过后调用 `DeleteOssObject` 删除 OSS 文件，然后删除审计记录
- Ignore 操作仅更新状态为 2，可选填写 Note，不删除 OSS 文件
