# MistakeManagement 数据模型

## 请求模型

### UpdateMistakeRequest

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| StudentId | string? | 否 | 学生 ID，为空时传空字符串给 gRPC |
| Subject | int? | 否 | 科目，为空时传 0 给 gRPC |
| Grade | int? | 否 | 年级，为空时传 0 给 gRPC |

定义位置：`backend/Controllers/MistakeController.cs`

## gRPC 消息映射

| Admin API 请求 | gRPC 请求 | gRPC 服务 | 说明 |
|----------------|-----------|-----------|------|
| GetMistakeItems | GetMistakeItemListRequest | Mistake | 分页查询错题列表 |
| GetMistakeItem | IdRequest | Mistake | 获取单个错题详情 |
| GetMistakesByUploadId | GetMistakeItemsByUploadRequest | Mistake | 按上传记录查询错题 |
| UpdateMistakeItem | UpdateMistakeItemRequest | Mistake | 更新错题信息 |
| MigrateImages (步骤1) | IdRequest → GetMistakeItem | Mistake | 获取错题详情 |
| MigrateImages (步骤3) | MigrateImagesToMistakeRequest | Student | 通过 Student gRPC 代理图片迁移 |
| MigrateImages (步骤5) | UpdateMistakeItemRequest | Mistake | 更新 sourceRegions |

## 关键 gRPC 请求/响应字段

### GetMistakeItemListRequest

| 字段 | 类型 | 说明 |
|------|------|------|
| StudentId | string | 学生 ID（空字符串表示不过滤） |
| Subject | int | 科目（0 表示不过滤） |
| Grade | int | 年级（0 表示不过滤） |
| ReviewStatus | ReviewStatus | 审核状态（可选） |
| Page | int | 页码 |
| Size | int | 每页大小 |

### MistakeItemDto（gRPC 响应中的错题条目）

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | string | 错题 ID |
| StudentId | string | 学生 ID |
| Subject | int | 科目 |
| Grade | int | 年级 |
| SourceUploadId | string | 来源上传记录 ID |
| ReviewStatus | ReviewStatus | 审核状态 |
| ReviewerId | string | 审核者 ID |
| ReviewedAt | long | 审核时间 |
| ReviewComment | string | 审核评论 |
| Type | int | 错题类型 |
| QuestionId | string | 关联题目 ID |
| CreatedAt | long | 创建时间 |
| UpdatedAt | long | 更新时间 |
| SourceRegions | repeated SourceRegion | 图片区域列表 |

### SourceRegion

| 字段 | 类型 | 说明 |
|------|------|------|
| SourceImagePath | string | 图片路径 |
| BoundingBox | BoundingBox? | 边界框（可为 null） |

### BoundingBox

| 字段 | 类型 | 说明 |
|------|------|------|
| X1 | int | 左上角 X 坐标 |
| Y1 | int | 左上角 Y 坐标 |
| X2 | int | 右下角 X 坐标 |
| Y2 | int | 右下角 Y 坐标 |

## 枚举值

### ReviewStatus (MistakeProto)

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | PendingReview | 待审核 |
| 1 | Confirmed | 已确认 |
| 2 | Rejected | 已拒绝 |

## 数据转换说明

- 列表接口不返回 `sourceRegions`，仅返回 `imageCount`（= sourceRegions.Count）和 `firstImagePath`
- 详情接口返回完整的 `sourceRegions`，包含 `boundingBox`（可为 null）
- `studentName` 通过 `StudentManagement.GetStudent` 获取，查询失败时回退为 studentId
- `by-upload` 接口的 `total` 为客户端计数（items.Count），非服务端分页

## 学生姓名获取的 N+1 查询分析

### 当前实现

GetMistakeItems、GetMistakesByUploadId 和 GetMistakeItem 三个端点都需要获取学生姓名以丰富响应。当前实现方式：

```
1. 获取错题列表 → 提取去重 studentId 列表
2. 对每个 studentId 逐个调用 GetStudentAsync
3. 构建 studentId → studentName 映射
4. 在响应中填充 studentName
```

### 性能影响

- **典型场景**：一页 20 条错题，涉及 3-5 个不同学生 → 3-5 次 gRPC 调用
- **极端场景**：一页 100 条错题，涉及 50+ 个不同学生 → 50+ 次 gRPC 调用
- **当前缓解**：代码使用 `Distinct()` 去重 studentId，避免同一学生重复查询

### 为什么不使用批量查询

Student gRPC 服务的 proto 定义中没有 `ListStudentsByIds` 或类似的批量查询方法。当前可用的方法只有：
- `GetStudent` — 单个查询
- `ListStudents` — 分页列表查询（不支持按 ID 列表过滤）

### 优化建议

1. **短期**：在 Student proto 中新增 `GetStudentsByIds` RPC，接受 ID 列表，返回 Student 列表
2. **中期**：在 Admin Portal 中引入内存缓存（IMemoryCache），缓存 studentId → studentName 映射（TTL 5-10 分钟）
3. **长期**：在 Student 服务中提供变更通知机制，使缓存可以主动失效

> **当前状态**：N+1 查询在典型使用场景下性能可接受（3-5 次额外 gRPC 调用），但大规模使用时可能成为瓶颈。

## MigrateImages 原子性分析

### 当前实现（gRPC 代理模式）

MigrateImages 通过 Student gRPC `MigrateImagesToMistake` 代理图片迁移，整体无事务保证：

```
1. GetMistakeItem → 获取错题详情
2. 收集 uploads/ 前缀的图片路径 → sourcePaths
3. 调用 StudentLearningGrpcServiceClient.MigrateImagesToMistakeAsync(sourcePaths)
4. 遍历迁移结果：成功 → 更新 region.SourceImagePath = newPath
5. UpdateMistakeItemAsync → 将更新后的 regions 写回 Mistake 服务
```

### 失败场景分析

| 失败位置 | 已完成操作 | 数据状态 | 恢复方式 |
|---------|-----------|---------|---------|
| 步骤 3 失败（gRPC 调用） | 无 | 一致 | 重试即可 |
| 步骤 3 部分成功 | 部分图片已迁移 | 已迁移图片路径已更新，未迁移图片路径未更新 | 重新执行迁移 |
| 步骤 5 失败（Update） | 迁移完成 | OSS 中文件已迁移，但 Mistake 服务仍指向旧路径 | 需手动更新 Mistake 记录指向新路径 |

### 缓解机制

1. **Student 服务内部处理**：`MigrateImagesToMistake` 逐文件处理 Copy+Delete，单文件失败不影响其他文件
2. **结果反馈**：gRPC 返回每张图片的迁移结果（成功/失败），Admin Portal 可据此决定是否更新路径
3. **幂等性**：重新执行迁移时，已迁移到 mistakes/ 的文件 CopyObject 幂等

### 风险评估

- **最大风险**：步骤 5（UpdateMistakeItemAsync）失败时，OSS 文件已迁移但 Mistake 记录仍指向旧路径
- **发生概率**：低（仅在 Mistake 服务不可用时发生）
- **恢复难度**：中等（需要手动更新 Mistake 记录或重新执行迁移）

> **相比旧实现的改进**：旧实现中 Admin Portal 直接操作 OSS（Copy+Delete），现在改为通过 Student gRPC 代理，Admin Portal 不再持有 OSS 写权限（CopyObject/Upload）。
