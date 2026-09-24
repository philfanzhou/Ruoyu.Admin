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
- Student服务不可用时：审计中止，`OssAuditRun.Status=2`，`ErrorMessage="Student service unavailable"`
- Homework服务不可用时：审计中止，`OssAuditRun.Status=2`，`ErrorMessage="Homework service unavailable"`
- Mistake服务不可用时：审计中止，`OssAuditRun.Status=2`，`ErrorMessage="Mistake service unavailable"`

三个引用来源任一不可达都必须中止整轮审计，且中止发生在扫描任何桶之前。

Student 与 Homework 一直是这个语义。Mistake 原先是「仅警告、降级继续」，本次改为一致中止：`mistakes/` 下的对象都是从 `uploads/` 迁移出去的，不在 Student 上传记录里，因此 Mistake 聚合为空时该桶下每个对象都会被判为孤儿，而整轮审计仍然报告成功并给出失真的 `NewZombieCount`。

降级本身不构成删除路径——`OssAuditController` 在 `DeleteAsync` 之前会重新聚合同一批引用来源，任一不可达就返回 502 拒删，所以 Mistake 恢复后被真实引用的文件仍受保护。但一个待处置队列里全是噪声的审计比没有审计更糟：它会训练运维人员习惯性 batch-resolve，而这层保护的有效性完全取决于引用聚合是否完整。显式失败可以廉价重试，误导性审计不行。

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

审计只扫描**有引用来源的桶**，当前为 `uploads` 和 `mistakes`，由 `OssAuditWorker.AuditedBuckets` 定义。

| 桶 | 前缀 | 归属 | 引用来源 | 是否审计 |
|----|------|------|----------|----------|
| `Uploads` | `uploads/` | Student | Student 上传记录分页聚合 + Homework 图片引用 | ✅ |
| `Mistakes` | `mistakes/` | Mistake | Mistake 错题条目 `source_regions.source_image_path` 分页聚合 | ✅ |
| `Questions` | `questions/` | QuestionBank | **无** | ❌ |
| `Documents` | `documents/` | DocLibrary（原件与产物已交由 StructaDoc 主责） | **无** | ❌ |

**不变量：一个桶只有在存在可达的引用来源时才允许进入 `AuditedBuckets`。**

审计记录不是报告，而是待处置项：`POST records/{id}/resolve` 与 `POST records/batch-resolve` 会调用 `IOssService.DeleteAsync` 真实删除文件并连带清理缩略图。删除前有一道复核（见下节），但**复核只查询已知的引用来源**：对一个没有任何来源的桶，复核必然查不到引用，因而必然放行删除。所以扫描一个无引用来源的桶，等价于把该桶下每一个对象都标记为可删除。

历史缺陷（迁出 monorepo 时修复）：早期实现固定扫描 `uploads`、`mistakes`、`questions` 三个桶，但只聚合 Student、Homework、Mistake 三个来源。`questions/` 归 QuestionBank 所有，没有任何来源覆盖它，于是全部题目图片被误判为孤儿，且复核拦不住——resolve 时 Student/Homework/Mistake 都会回答"不是我引用的"。修复方式是移除该桶的扫描，并在启动时清理历史误判记录（`CleanupUnauditedBucketAuditRecordsAsync`）。

要把 `Questions` 或 `Documents` 加回审计范围，必须在**同一个变更**里落地其引用来源，且该来源不可达时按 Student/Homework/Mistake 的语义中止整轮审计。加回来源时也要同步 `OssAuditController` 的删除前复核，否则审计能发现、复核却拦不住。

### 删除前复核

`ResolveRecord` 与 `BatchResolve` 在调用 `DeleteAsync` 之前，对 `Status=0`（Pending）的记录重新聚合一次引用来源：

| 来源 | 查询内容 | 命中时 | 不可达时 |
|------|----------|--------|----------|
| Student + Homework | `GetRegisteredOssPathsAsync()` | 单条返回 400；批量加入 `errors` 并跳过 | 返回 502，整批不删 |
| Mistake | `GetMistakeImagePathsAsync()` | 同上 | 返回 502，整批不删 |

`Status=2`（Ignored）的记录被 `BatchResolve` 直接排除。非 Pending 记录跳过复核——按「记录删除 vs 状态更新」的约定，Resolve 成功后记录即被移除，留在库里且非 Pending 的记录说明上一次删除已发生。

这道复核是**下游临时故障的保护**，不是审计判定错误的保护：它和审计用的是同一批来源，因此无法纠正来源缺失（如上文的 `questions/`）或来源静默返回不完整结果导致的误判。

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
- 原因：审计浏览与运维操作需要访问任意路径前缀，包括当前不在审计范围内的 `questions/`、`documents/`
- 与其他服务不同：Student 配置 `uploads/` + `mistakes/`，Mistake 配置 `mistakes/`
- 注意这与「桶范围」是两件事：`allowedPrefixes` 约束的是 `IOssService` 能读写哪些路径，`AuditedBuckets` 约束的是审计**扫描并判定**哪些路径。前者宽、后者窄，不可混淆
