# LegacyDataCleanup 数据模型

## gRPC 消息映射

### LegacyCheck

| 步骤 | gRPC 请求 | gRPC 服务 | 说明 |
|------|-----------|-----------|------|
| 1 | GetMistakeItemsByUploadRequest | Mistake | 获取上传记录关联的所有错题 |

### LegacyClean

| 步骤 | gRPC 请求 | gRPC 服务 | 说明 |
|------|-----------|-----------|------|
| 1 | GetMistakeItemsByUploadRequest | Mistake | 获取关联错题，验证前置条件 |
| 2 | CompleteUploadReviewRequest | Mistake | 完成审核流程 |
| 3 | RemoveImagesFromRecordRequest | StudentLearning | 移除已审核图片（可选步骤） |
| 4 | DeleteUploadRecordAfterReviewRequest | StudentLearning | 删除上传记录 |

## 关键 gRPC 请求/响应字段

### CompleteUploadReviewRequest

| 字段 | 类型 | 说明 |
|------|------|------|
| SourceUploadId | string | 上传记录 ID |
| ReviewerId | string | 审核者 ID（LegacyClean 中固定为 "admin-legacy-cleanup"） |

### CompleteUploadReviewResponse

| 字段 | 类型 | 说明 |
|------|------|------|
| Success | bool | 是否成功 |
| ErrorMessage | string | 失败时的错误消息 |
| RemovedImagePaths | repeated string | 需要从上传记录中移除的图片路径列表 |

### DeleteUploadRecordAfterReviewRequest

| 字段 | 类型 | 说明 |
|------|------|------|
| RecordId | string | 上传记录 ID |
| StudentId | string | 学生 ID |

### RemoveImagesFromRecordRequest

| 字段 | 类型 | 说明 |
|------|------|------|
| RecordId | string | 上传记录 ID |
| StudentId | string | 学生 ID |
| ImagePaths | repeated string | 要移除的图片路径列表 |

## 枚举值

### ReviewStatus (MistakeProto)

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | PendingReview | 待审核 |
| 1 | Confirmed | 已确认 |
| 2 | Rejected | 已拒绝 |

LegacyCheck/LegacyClean 中判断"已审核"的条件：ReviewStatus 为 Confirmed 或 Rejected。

## 数据流

```
LegacyCheck:
  GetMistakeItemsByUpload → Items → 判断 allReviewed / hasPending → 返回 isLegacy

LegacyClean:
  GetMistakeItemsByUpload → 验证前置条件
    → CompleteUploadReview → RemovedImagePaths?
      → 有: RemoveImagesFromRecord
      → 无: 跳过
    → DeleteUploadRecordAfterReview → 返回结果
```
