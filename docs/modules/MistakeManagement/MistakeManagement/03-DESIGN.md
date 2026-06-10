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
| MigrateImages (步骤4) | UpdateMistakeItemRequest | Mistake | 更新 sourceRegions |

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
