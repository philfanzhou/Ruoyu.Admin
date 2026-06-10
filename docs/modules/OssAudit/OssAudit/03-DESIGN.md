# OssAudit 数据模型

## 数据库表

### OssAuditRecord

僵尸文件审计记录，记录每个被识别为僵尸的OSS对象。

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | | PK | 主键 |
| ObjectPath | string | UNIQUE | OSS对象路径 |
| Bucket | string | | 所属桶名（uploads/mistakes/questions） |
| Size | long | | 对象大小（字节） |
| LastModified | DateTime | | 对象最后修改时间 |
| Status | int | | 0=Pending, 1=Resolved, 2=Ignored |
| CreatedAt | DateTime | | 记录创建时间 |
| ResolvedAt | DateTime? | | 处理时间（resolve或ignore时设置） |
| Note | string? | | 忽略备注（ignore时设置） |

**唯一约束**：`ObjectPath` — 同一对象路径只会产生一条审计记录。

### OssAuditRun

审计运行记录，同时作为互斥锁使用。

| 字段 | 类型 | 约束 | 说明 |
|------|------|------|------|
| Id | int | PK, IDENTITY | 自增主键 |
| StartedAt | DateTime | | 审计开始时间 |
| CompletedAt | DateTime? | | 审计完成时间 |
| Status | int | | 0=Running, 1=Completed, 2=Failed |
| NewZombieCount | int | | 本次审计发现的僵尸文件数量 |
| TriggerType | string | | 触发方式：scheduled / manual |
| ErrorMessage | string? | | 失败时的错误信息 |

## API 请求模型

### IgnoreRecordRequest

```json
{
  "note": "string?"  // 忽略备注，可选
}
```

### BatchResolveRequest

```json
{
  "ids": [1, 2, 3]  // 要批量清理的记录ID列表
}
```

## API 响应数据

### GET /records 响应

返回分页数据，包含：
- `items`：记录列表，每条记录附带 statusText（状态文本描述）
- `statusCounts`：各状态的记录数统计
- `bucketCounts`：各桶的记录数统计

### GET /status 响应

```json
{
  "isRunning": true,
  "lastCompleted": "2024-01-01T02:00:00",
  "lastFailed": null,
  "pendingCount": 42
}
```

## 数据生命周期

```
OssAuditRecord:
  创建 → RunAuditAsync 发现僵尸文件时创建（Status=Pending）
  删除 → ResolveRecord 成功后从DB移除（文件已从OSS删除）
  更新 → IgnoreRecord 设置 Status=Ignored, ResolvedAt, Note

OssAuditRun:
  创建 → 每次审计开始时创建（Status=Running）
  更新 → 审计完成或失败时更新 Status/CompletedAt/ErrorMessage
```
