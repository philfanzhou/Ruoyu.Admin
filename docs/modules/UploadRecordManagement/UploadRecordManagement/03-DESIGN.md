# UploadRecordManagement 数据模型

## 请求模型

### RotateImageRequest

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| StudentId | string | 是 | 学生 ID |
| ImageIndex | int | 是 | 图片索引 |
| Rotation | int | 是 | 旋转角度 |

定义位置：`backend/Controllers/OssUploadRecordController.cs`

### ResetStatusRequest

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| StudentId | string | 是 | 学生 ID |
| TargetStatus | int | 是 | 目标状态（对应 UploadStatus 枚举） |

定义位置：`backend/Controllers/OssUploadRecordController.cs`

### ImageAssignment

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| ImageIndices | List\<int\> | 是 | 该 assignment 引用的图片索引列表，至少 1 个元素 |
| Subject | int | 是 | 学科 |
| Grade | int | 是 | 年级 |
| Comments | string? | 否 | 备注 |

定义位置：`backend/Controllers/OssUploadRecordController.cs`

### AssignUploadRecordRequest

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| StudentId | string | 是 | 学生 ID |
| Assignments | List\<ImageAssignment\> | 是 | 分配条目列表 |

定义位置：`backend/Controllers/OssUploadRecordController.cs`

## gRPC 消息映射

### 上传记录查询

| Admin API 请求 | gRPC 请求 | gRPC 服务 |
|----------------|-----------|-----------|
| GetAllUploadRecords | GetAllUploadRecordsRequest | StudentManagement |
| GetUploadRecord (assign 内部) | GetUploadRecordRequest | StudentLearning |
| ResetUploadRecordStatus | ResetUploadRecordStatusRequest | StudentLearning |
| MarkUploadRecordCompleted | MarkUploadRecordCompletedRequest | StudentLearning |
| RotateUploadImage | RotateUploadImageRequest | StudentLearning |
| RemoveImageFromRecord | RemoveImageFromRecordRequest | StudentLearning |
| AnalyzeUploadRecord | AnalyzeUploadRecordRequest | StudentManagement |

### 错题创建

| Admin API 请求 | gRPC 请求 | gRPC 服务 |
|----------------|-----------|-----------|
| SubmitMistakeUpload (assign 内部) | SubmitMistakeUploadRequest | Mistake |

## 枚举值

### UploadStatus

| 值 | 名称 | 说明 |
|----|------|------|
| 0 | Unspecified | 未指定 |
| 1 | Uploaded | 已上传 |

[待确认] 完整的 UploadStatus 枚举值列表

### Subject / Grade

通过 `GET /api/admin/enum-options` 获取完整枚举选项。

## 响应模型说明

### GetAllUploadRecords 响应

- `studentName`：通过批量查询 StudentManagement.GetStudent 获取，查询失败时回退为 studentId
- `createdAt` / `updatedAt`：从 gRPC Timestamp 解析为 Unix 秒数，支持数字字符串和 ISO 8601 格式

### AssignUploadRecord 响应

- `createdItems`：每个 assignment 对应一个元素，包含 `itemId`（新创建的 mistake_item ID）
- `remainingImageCount`：分配后 `image_upload_record.image_paths` 中剩余的图片数量
- `totalImageCount`：上传记录原始总图片数
- `warnings`：部分 assignment 失败时的警告信息列表
