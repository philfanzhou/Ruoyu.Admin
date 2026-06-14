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
         注意：缩略图文件（ThumbnailHelper.IsThumbnailPath 返回 true）被跳过，不创建记录
  删除 → ResolveRecord 成功后从DB移除（文件已从OSS删除，关联缩略图由 IOssService.DeleteAsync 自动清理）
  更新 → IgnoreRecord 设置 Status=Ignored, ResolvedAt, Note

OssAuditRun:
  创建 → 每次审计开始时创建（Status=Running）
  更新 → 审计完成或失败时更新 Status/CompletedAt/ErrorMessage
```

## 缩略图处理设计

### 审计扫描中的缩略图跳过

审计扫描遍历 OSS 对象时，使用 `ThumbnailHelper.IsThumbnailPath(path)` 判断对象是否为缩略图：
- 缩略图路径匹配 `_{size}.jpg` 模式（如 `image_small.jpg`、`image_thumbnail.jpg`、`image_medium.jpg`）
- 缩略图与原图关联，删除原图时自动清理，不应单独标记为僵尸文件
- 跳过缩略图避免了将缩略图误报为僵尸文件的问题

### 清理僵尸原图时的缩略图自动清理

`IOssService.DeleteAsync` 内部实现了缩略图自动清理逻辑（`S3OssService.DeleteAsync`）：
1. 调用 `ThumbnailHelper.IsThumbnailPath(objectPath)` 判断是否为缩略图路径
2. 若非缩略图路径（即原图），调用 `ThumbnailHelper.GetAllThumbnailPaths(objectPath)` 获取所有尺寸缩略图路径
3. 逐一删除缩略图（不存在时忽略错误）
4. 删除原图
5. 若为缩略图路径，只删除该文件本身，不触发递归清理

Admin Portal 的 `S3OssService` 不配置路径前缀校验（`allowedPrefixes` 为 null），因为审计需要访问所有路径前缀（uploads/、mistakes/、questions/）。

### 缩略图路径规则

缩略图与原图同目录，路径格式 `{dirname}/{stem}_{size}.jpg`：
- 原图 `uploads/2025/06/14/abc/image.jpg` → 缩略图 `uploads/2025/06/14/abc/image_small.jpg`
- 原图 `mistakes/2025/06/14/def/photo.png` → 缩略图 `mistakes/2025/06/14/def/photo_thumbnail.jpg`

旧规则（已废弃）：缩略图存储在 `uploads/thumbnails/` 下，与原图不在同目录。
