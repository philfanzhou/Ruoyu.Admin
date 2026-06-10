# UploadRecordManagement API

## 接口列表

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | /api/admin/oss-upload-records | 获取所有上传记录 |
| POST | /api/admin/oss-upload-records/{id}/reset-status | 重置上传记录状态 |
| POST | /api/admin/oss-upload-records/{id}/assign | 按图片粒度分配上传记录 |
| GET | /api/admin/oss-upload-records/image | 获取上传记录图片 |
| POST | /api/admin/oss-upload-records/{id}/rotate | 旋转上传记录图片 |
| DELETE | /api/admin/oss-upload-records/{id}/images/{imageIndex} | 从上传记录中移除图片 |
| POST | /api/admin/oss-upload-records/{id}/analyze | VL 分析上传记录 |

---

### GET /api/admin/oss-upload-records

分页获取所有上传记录，支持按状态和学生筛选。

**查询参数：**

| 参数 | 类型 | 必需 | 默认值 | 说明 |
|------|------|------|--------|------|
| page | int | 否 | 1 | 页码 |
| pageSize | int | 否 | 20 | 每页大小 |
| status | int | 否 | -1 | 上传状态（-1 表示所有） |
| studentId | string | 否 | - | 学生 ID |

**gRPC 调用：** `StudentManagement.GetAllUploadRecords`

**响应：**

```json
{
  "items": [
    {
      "id": "string",
      "studentId": "string",
      "studentName": "张三",
      "status": 1,
      "imagePaths": ["path/to/image.jpg"],
      "comments": "string",
      "createdAt": 1234567890,
      "updatedAt": 1234567890
    }
  ],
  "totalCount": 100,
  "page": 1,
  "pageSize": 20
}
```

**错误响应：** 500 - Failed to get upload records

---

### POST /api/admin/oss-upload-records/{id}/reset-status

重置上传记录的状态。

**路径参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| id | string | 上传记录 ID |

**请求体：**

```json
{
  "studentId": "string",
  "targetStatus": 1
}
```

**gRPC 调用：** `StudentLearning.ResetUploadRecordStatus`

**响应：**

```json
{
  "success": true,
  "message": "Status reset successfully"
}
```

**错误响应：**
- 400 - Failed to reset status（gRPC 返回失败）
- 500 - Failed to reset upload record status

---

### POST /api/admin/oss-upload-records/{id}/assign

将上传记录中的指定图片分配为错题条目。一次请求可以包含多个 assignment，每个 assignment 内的图片共享同一道错题。

**路径参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| id | string | 上传记录 ID |

**请求体：**

```json
{
  "studentId": "string",
  "assignments": [
    {
      "imageIndices": [0, 1],
      "subject": 1,
      "grade": 7,
      "comments": "第 1、2 张图是同一道语文错题"
    }
  ]
}
```

**请求字段说明：**
- `studentId` (string, 必需): 学生 ID
- `assignments` (array, 必需): 分配条目列表
  - `imageIndices` (array, 必需): 该 assignment 引用的图片索引（基于上传记录的原始索引），至少包含 1 个元素
  - `subject` (int, 必需): 学科
  - `grade` (int, 必需): 年级
  - `comments` (string, 可选): 备注，为空时回退到上传记录的 comments

**gRPC 调用流程：**
1. `StudentLearning.GetUploadRecord` - 获取上传记录以取得图片路径
2. 对每个 assignment 调用 `Mistake.SubmitMistakeUpload` - 创建错题条目
3. `StudentLearning.MarkUploadRecordCompleted` - 标记上传记录为完成

**响应（成功）：**

```json
{
  "success": true,
  "message": "Assignment successful",
  "createdItems": [
    {
      "subject": 1,
      "grade": 7,
      "itemId": "mistake-item-id-1",
      "imageCount": 2
    }
  ],
  "remainingImageCount": 4,
  "totalImageCount": 9
}
```

**响应（有警告）：**

```json
{
  "success": false,
  "message": "Assignment completed with warnings",
  "warnings": ["No valid images found for assignment with indices: 5"],
  "createdItems": [...],
  "remainingImageCount": 4,
  "totalImageCount": 9
}
```

**错误响应：**
- 400 - Upload record not found
- 500 - Failed to assign upload record

---

### GET /api/admin/oss-upload-records/image

从 OSS 存储中获取图片文件。

**查询参数：**

| 参数 | 类型 | 必需 | 说明 |
|------|------|------|------|
| path | string | 是 | OSS 对象路径 |

**依赖：** `IOssService.DownloadAsync`

**响应：** 图片二进制流（Content-Type 根据扩展名自动判断）

**支持的图片格式：** .jpg/.jpeg, .png, .gif, .webp, .bmp, .svg（其他格式返回 application/octet-stream）

**错误响应：**
- 400 - Path is required
- 404 - Image not found
- 500 - Failed to get image

---

### POST /api/admin/oss-upload-records/{id}/rotate

旋转上传记录中的图片。

**路径参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| id | string | 上传记录 ID（必须是合法 GUID） |

**请求体：**

```json
{
  "studentId": "string",
  "imageIndex": 0,
  "rotation": 90
}
```

**gRPC 调用：** `StudentLearning.RotateUploadImage`

**响应：**

```json
{
  "success": true
}
```

**错误响应：**
- 400 - Invalid record ID / StudentId is required / gRPC 返回失败
- 500 - Failed to rotate image

---

### DELETE /api/admin/oss-upload-records/{id}/images/{imageIndex}

从上传记录中移除指定索引的图片。

**路径参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| id | string | 上传记录 ID |
| imageIndex | int | 图片索引 |

**查询参数：**

| 参数 | 类型 | 必需 | 说明 |
|------|------|------|------|
| studentId | string | 是 | 学生 ID |

**gRPC 调用：** `StudentLearning.RemoveImageFromRecord`

**响应：**

```json
{
  "success": true,
  "message": "string",
  "recordDeleted": false,
  "remainingImageCount": 3
}
```

**说明：** `recordDeleted` 表示移除图片后上传记录是否已被自动删除（图片全部移除时）；`remainingImageCount` 为剩余图片数量。

**错误响应：**
- 400 - studentId is required / gRPC 返回失败
- 500 - Failed to remove image

---

### POST /api/admin/oss-upload-records/{id}/analyze

对上传记录进行 VL（视觉语言）分析，自动识别图片分组和学科/年级。

**路径参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| id | string | 上传记录 ID |

**gRPC 调用：** `StudentManagement.AnalyzeUploadRecord`

**响应：**

```json
{
  "success": true,
  "errorMessage": "",
  "rawResponse": "string",
  "skipped": false,
  "prompt": "string",
  "compressedImages": ["path1", "path2"],
  "groups": [
    {
      "imageIndices": [0, 1],
      "subject": 1,
      "grade": 7,
      "description": "string"
    }
  ]
}
```

**错误响应：** 500 - VL 分析失败

---

## 通用错误格式

```json
{
  "message": "错误描述"
}
```
