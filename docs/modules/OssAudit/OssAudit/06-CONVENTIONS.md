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

- Resolve 操作：从数据库**移除**记录（文件已从OSS删除，记录无需保留）
- Ignore 操作：**更新**记录状态为 Ignored（文件仍存在，需保留记录供追溯）

### 桶范围

审计扫描固定覆盖三个桶：uploads、mistakes、questions。
