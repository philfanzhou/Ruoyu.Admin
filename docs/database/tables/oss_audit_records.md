# OssAuditRecords

OSS审计观察与历史处置记录表。v1新行只表示捕获时未观察到引用。

## 表信息

| 属性 | 值 |
|------|------|
| 表名 | `OssAuditRecords` |
| 所属数据库 | `ruoyu_admin`（PostgreSQL） |
| 实体类 | `Admin.WebApi.Persistence.OssAuditRecord` |
| DbContext | `AuditDbContext` |

## 字段定义

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| `Id` | `bigint` | PK | 主键，自增 |
| `ObjectPath` | `string` | NOT NULL, UNIQUE | OSS 对象路径，如 `uploads/xxx.jpg`、`mistakes/xxx.jpg` |
| `Bucket` | `string` | NOT NULL | 所属 Bucket 名称。新记录取值只有 `uploads`、`mistakes`（由 `OssAuditWorker.AuditedBucketNames` 决定）；`questions`、`documents` 保留原历史值，startup不删除 |
| `Size` | `bigint` | NOT NULL | 对象大小（字节） |
| `LastModified` | `bigint` | NOT NULL | 对象最后修改时间（Unix 秒） |
| `Status` | `int` | NOT NULL, DEFAULT 0 | 审计状态，见下方枚举 |
| `CreatedAt` | `bigint` | NOT NULL | 记录创建时间（Unix 秒） |
| `ResolvedAt` | `bigint` | NULL | 记录处理时间（Unix 秒），未处理时为 null |
| `Note` | `string` | NULL | 备注，忽略记录时可填写原因 |

## Status 枚举

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | `Pending` | 历史待处理 |
| 1 | `Resolved` | 历史已删除状态，当前不再次删除 |
| 2 | `Ignored` | 历史已忽略 |
| 3 | `UnreferencedObservation` | 未观察到引用，只读报告、非删除许可 |

旧0/1/2保持历史，不回填为新观察；任何状态都不能跳过共同collector或授予删除。

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

表结构由exact EF迁移链与ServiceMantle启动门管理，见[迁移](../migrations.md)。

## 业务规则

- ObjectPath精确UNIQUE；已有任何状态同路径不重复创建。
- 新v1发现仅Status3，完整扫描事务提交，不产生可处置结论。
- Resolve所有状态及非空batch均由shared collector后409或502拒删，不Delete/Copy/删除记录。
- Ignore仅历史Status0→2，可选Note，Status3拒绝。startup不清旧review/旧桶记录。
- 原状态语义与当前消费者保证见[StorageAudit](../../modules/OssAudit/StorageAudit.md)。
