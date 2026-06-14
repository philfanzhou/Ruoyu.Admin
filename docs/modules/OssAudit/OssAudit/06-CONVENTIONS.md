# OssAudit 约定与模式

## 编码约定

### 状态枚举

使用整数表示状态，未使用枚举类型：

| 实体 | 字段 | 值映射 |
|------|------|--------|
| OssAuditRecord | Status | 0=Pending, 1=Resolved, 2=Ignored |
| OssAuditRun | Status | 0=Running, 1=Completed, 2=Failed |

### 互斥锁模式

使用数据库记录（OssAuditRun）作为分布式锁：
- 创建 Status=Running 的记录表示获取锁
- 审计完成/失败时更新记录状态表示释放锁
- 二次检查确保无其他运行中的审计

### 触发类型

审计触发方式通过字符串区分：
- `"scheduled"` — 定时调度触发
- `"manual"` — 手动API触发

## API 约定

### 路由前缀

所有端点位于 `/api/admin/oss-audit` 下。

### 响应格式

- GET /records 返回分页数据，附带聚合统计（statusCounts, bucketCounts）
- 记录列表中每条记录包含 statusText 人类可读状态描述

### 错误处理

- 审计已在运行时：返回冲突响应[推断]
- Student服务不可用时：审计中止，记录错误信息
- Mistake服务不可用时：仅警告，不中止

## 后台任务约定

### BackgroundService 模式

OssAuditWorker 继承 BackgroundService：
- 启动后延迟执行（2分钟），避免应用启动时立即运行
- 基于配置的调度时间计算等待时长
- 循环执行，每次完成后重新计算下次运行时间

### 配置项

| 配置键 | 默认值 | 说明 |
|--------|--------|------|
| OssAudit:ScheduledHour | 2 | 审计调度小时 |
| OssAudit:ScheduledMinute | 0 | 审计调度分钟 |

## 数据约定

### 唯一性保证

OssAuditRecord.ObjectPath 设置 UNIQUE 约束，确保同一对象路径只产生一条审计记录。

### 记录删除 vs 状态更新

- Resolve 操作：从数据库**移除**记录（文件已从OSS删除，关联缩略图由 `IOssService.DeleteAsync` 自动清理，记录无需保留）
- Ignore 操作：**更新**记录状态为 Ignored（文件仍存在，需保留记录供追溯）

### 桶范围

审计扫描固定覆盖三个桶：uploads、mistakes、questions。

### 缩略图路径约定

缩略图与原图同目录，路径格式 `{dirname}/{stem}_{size}.jpg`：
- 支持的尺寸：thumbnail（200px）、small（400px）、medium（800px）
- 示例：原图 `uploads/2025/06/14/abc/image.jpg` → 缩略图 `uploads/2025/06/14/abc/image_small.jpg`
- 旧规则（已废弃）：缩略图存储在 `uploads/thumbnails/{path}_{size}.jpg`

### 缩略图跳过约定

审计扫描使用 `ThumbnailHelper.IsThumbnailPath(path)` 判断是否为缩略图：
- 匹配 `_{size}.jpg` 模式的路径被视为缩略图
- 缩略图文件被跳过，不创建 OssAuditRecord
- 原因：缩略图与原图关联，删除原图时由 `IOssService.DeleteAsync` 自动清理

### 缩略图自动清理约定

`IOssService.DeleteAsync` 删除原图时自动清理关联缩略图：
- `S3OssService.DeleteAsync` 内部调用 `ThumbnailHelper.IsThumbnailPath` 判断是否为缩略图
- 若为原图，调用 `ThumbnailHelper.GetAllThumbnailPaths` 获取所有缩略图路径并逐一删除
- 若为缩略图，只删除该文件本身，不触发递归清理
- Admin Portal 无需手动调用 `ThumbnailHelper.DeleteWithThumbnailsAsync`

### 路径前缀校验约定

Admin Portal 的 `S3OssService` 不配置路径前缀校验（`allowedPrefixes` 为 null）：
- 原因：审计需要访问所有路径前缀（uploads/、mistakes/、questions/）
- 与其他服务不同：Student 配置 `uploads/` + `mistakes/`，Mistake 配置 `mistakes/`
