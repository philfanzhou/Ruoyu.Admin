# LegacyDataCleanup API

## 接口列表

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | /api/admin/oss-upload-records/legacy-check/{id} | 检查上传记录是否为遗留数据 |
| POST | /api/admin/oss-upload-records/legacy-clean/{id} | 清理遗留上传记录数据 |

---

### GET /api/admin/oss-upload-records/legacy-check/{id}

检查指定上传记录是否属于遗留数据。

**路径参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| id | string | 上传记录 ID |

**gRPC 调用：** `Mistake.GetMistakeItemsByUpload`

**响应（无关联错题）：**

```json
{
  "isLegacy": false,
  "message": "该记录没有关联错题"
}
```

**响应（有待审核错题）：**

```json
{
  "isLegacy": false,
  "pendingCount": 3,
  "message": "该记录还有3条错题待审核"
}
```

**响应（全部已审核 - 遗留数据）：**

```json
{
  "isLegacy": true,
  "mistakeCount": 5,
  "hasUploadPathImage": true,
  "message": "该记录的错题已全部审核完毕，但上传记录仍存在，属于遗留数据"
}
```

**响应字段说明：**
- `isLegacy` (bool): 是否为遗留数据
- `mistakeCount` (int): 关联错题数量（仅 isLegacy=true 时返回）
- `hasUploadPathImage` (bool): 是否有图片仍在 `uploads/` 路径下（仅 isLegacy=true 时返回）
- `pendingCount` (int): 待审核错题数量（仅 isLegacy=false 且有待审核时返回）
- `message` (string): 状态描述

**错误响应：** 500 - Failed to check legacy status

---

### POST /api/admin/oss-upload-records/legacy-clean/{id}

清理遗留的上传记录数据。执行完整的审核完成和记录删除流程。

**路径参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| id | string | 上传记录 ID |

**gRPC 调用流程：**
1. `Mistake.GetMistakeItemsByUpload` - 获取关联错题，验证前置条件
2. `Mistake.CompleteUploadReview` - 完成审核流程（ReviewerId = "admin-legacy-cleanup"）
3. `StudentLearning.RemoveImagesFromRecord` - 移除已审核图片（仅当 CompleteUploadReview 返回 RemovedImagePaths 时）
4. `StudentLearning.DeleteUploadRecordAfterReview` - 删除上传记录

**响应（成功）：**

```json
{
  "success": true,
  "message": "清理完成",
  "uploadRecordId": "string"
}
```

**错误响应：**
- 400 - 该记录没有关联错题，无法清理
- 400 - 该记录还有N条错题待审核，无法清理
- 400 - 无法找到该上传记录的学生ID
- 400 - CompleteUploadReview 失败
- 400 - DeleteUploadRecordAfterReview 失败
- 500 - Failed to clean legacy data

**执行步骤日志：**
- Step 1: `Starting legacy data cleanup for upload record: {RecordId}`
- Step 1: `Calling CompleteUploadReview for {RecordId}`
- Step 2: `Removing {Count} image paths from upload record {RecordId}`（仅当有 RemovedImagePaths 时）
- Step 3: `Calling DeleteUploadRecordAfterReview for {RecordId}`
- 完成: `Legacy data cleanup completed successfully for {RecordId}`
