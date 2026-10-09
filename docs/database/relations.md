# 实体关系图

Admin Portal 本地数据库仅包含 2 张表，且它们之间**没有外键关系**。

## 关系图

```
+---------------------+          +---------------------+
|   OssAuditRecords   |          |    OssAuditRuns     |
+---------------------+          +---------------------+
| PK  Id              |          | PK  Id              |
|     ObjectPath (UQ) |          |     StartedAt       |
|     Bucket          |          |     CompletedAt     |
|     Size            |          |     Status          |
|     LastModified    |          |     NewZombieCount  |
|     Status          |          |     TriggerType     |
|     CreatedAt       |          |     ErrorMessage    |
|     ResolvedAt      |          +---------------------+
|     Note            |
+---------------------+          无外键关联
        |                               |
        |    逻辑关联（非 FK）            |
        |                               |
        +---------- 一次 AuditRun ------+
                    产生多条
                    AuditRecords
```

## 关系说明

### OssAuditRuns → OssAuditRecords（逻辑关联）

- **关系类型**：一对多（逻辑），无外键
- **说明**：一次审计运行（`OssAuditRun`）会产生零到多条审计记录（`OssAuditRecord`）
- **无 FK 原因**：`OssAuditRecord` 中没有 `AuditRunId` 字段，无法追溯某条记录由哪次运行产生
- **业务影响**：新运行记录完整保存三个服务的快照元数据；审计记录没有 RunId，仍通过原业务逻辑关联，`resolve` 不删除对象或记录

### 表间独立性

两张表在数据库层面完全独立：

- `OssAuditRecords`：保存历史记录和新状态 3 的只读观察；`resolve` 共用引用检查，引用完整时返回 409，不可用时返回 502，只有历史 Pending 可 `ignore`
- `OssAuditRuns`：存储审计任务本身的执行状态，用于并发控制和状态查询

## 外部数据引用

Admin Portal 的数据库不包含来自外部服务（Student、Mistake、Identity）的数据表。业务数据通过 HTTP 查询；完整引用由 StorageReferences v1 收集，运行记录持久保存三个服务的元数据和版本，完整引用路径仍由各提供服务负责保存。

详见 [DataOwnership.md](../overview/DataOwnership.md)。
