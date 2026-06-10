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
- **业务影响**：审计记录 Resolve 时会被直接删除，不保留历史关联

### 表间独立性

两张表在数据库层面完全独立：

- `OssAuditRecords`：存储僵尸对象的快照信息，由审计运行写入，由管理员操作（Resolve/Ignore）更新
- `OssAuditRuns`：存储审计任务本身的执行状态，用于并发控制和状态查询

## 外部数据引用

Admin Portal 的数据库不包含来自外部服务（Student、Mistake、Identity）的数据表。外部数据通过 gRPC/HTTP 实时查询获取，不在本地持久化。

详见 [DataOwnership.md](../DataOwnership.md)。
