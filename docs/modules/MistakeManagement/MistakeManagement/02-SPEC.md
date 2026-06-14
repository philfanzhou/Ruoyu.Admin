# MistakeManagement API

## 接口列表

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | /api/admin/mistakes | 获取错题列表 |
| GET | /api/admin/mistakes/{id} | 获取单个错题详情 |
| GET | /api/admin/mistakes/by-upload/{uploadId} | 根据上传记录获取错题 |
| PUT | /api/admin/mistakes/{id} | 更新错题信息 |
| POST | /api/admin/mistakes/migrate-images/{id} | 迁移错题图片 |

---

### GET /api/admin/mistakes

分页获取错题记录列表，支持多种筛选条件。

**查询参数：**

| 参数 | 类型 | 必需 | 默认值 | 说明 |
|------|------|------|--------|------|
| studentId | string | 否 | - | 学生 ID |
| subject | int | 否 | - | 科目 |
| grade | int | 否 | - | 年级 |
| reviewStatus | int | 否 | - | 审核状态（对应 ReviewStatus 枚举） |
| page | int | 否 | 1 | 页码（≤0 重置为 1） |
| size | int | 否 | 20 | 每页大小（≤0 重置为 10，>100 限制为 100） |

**gRPC 调用：** `Mistake.GetMistakeItemList`

**响应：**

```json
{
  "items": [
    {
      "id": "string",
      "studentId": "string",
      "studentName": "张三",
      "subject": 1,
      "grade": 1,
      "sourceUploadId": "string",
      "reviewStatus": 0,
      "reviewerId": "string",
      "reviewedAt": 0,
      "reviewComment": "string",
      "type": 0,
      "questionId": "string",
      "createdAt": 0,
      "updatedAt": 0,
      "imageCount": 1,
      "firstImagePath": "string"
    }
  ],
  "total": 100,
  "page": 1,
  "pageSize": 20,
  "totalPages": 5
}
```

**说明：** `imageCount` 为 `sourceRegions.Count`，`firstImagePath` 为第一个 sourceRegion 的 `SourceImagePath`。

**错误响应：** 500 - Failed to get mistake items

---

### GET /api/admin/mistakes/{id}

获取指定错题的详细信息，包含 sourceRegions 和 boundingBox。

**路径参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| id | string | 错题 ID |

**gRPC 调用：** `Mistake.GetMistakeItem`

**响应：**

```json
{
  "id": "string",
  "studentId": "string",
  "studentName": "张三",
  "subject": 1,
  "grade": 1,
  "sourceUploadId": "string",
  "reviewStatus": 0,
  "reviewerId": "string",
  "reviewedAt": 0,
  "reviewComment": "string",
  "type": 0,
  "questionId": "string",
  "createdAt": 0,
  "updatedAt": 0,
  "imageCount": 1,
  "firstImagePath": "string",
  "sourceRegions": [
    {
      "sourceImagePath": "string",
      "boundingBox": {
        "x1": 0,
        "y1": 0,
        "x2": 100,
        "y2": 100
      }
    }
  ]
}
```

**说明：** `boundingBox` 可能为 null（当 gRPC 响应中 BoundingBox 为 null 时）。

**错误响应：** 500 - Failed to get mistake item

---

### GET /api/admin/mistakes/by-upload/{uploadId}

获取指定上传记录关联的所有错题。

**路径参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| uploadId | string | 上传记录 ID |

**gRPC 调用：** `Mistake.GetMistakeItemsByUpload`

**响应：**

```json
{
  "items": [...],
  "total": 10
}
```

**说明：** `total` 为 items.Count（客户端计数，非服务端分页），不支持分页参数。

**错误响应：** 500 - Failed to get mistakes by upload id

---

### PUT /api/admin/mistakes/{id}

更新指定错题的学生、学科、年级信息。

**路径参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| id | string | 错题 ID |

**请求体：**

```json
{
  "studentId": "string",
  "subject": 1,
  "grade": 1
}
```

**请求字段说明：**
- `studentId` (string?, 可选): 学生 ID，为空时传空字符串给 gRPC
- `subject` (int?, 可选): 科目，为空时传 0 给 gRPC
- `grade` (int?, 可选): 年级，为空时传 0 给 gRPC

**gRPC 调用：** `Mistake.UpdateMistakeItem`

**响应：**

```json
{
  "id": "string",
  "studentId": "string",
  "studentName": "张三",
  "subject": 1,
  "grade": 1,
  "sourceUploadId": "string",
  "reviewStatus": 0
}
```

**错误响应：** 500 - Failed to update mistake item

---

### POST /api/admin/mistakes/migrate-images/{id}

将错题图片从 `uploads/` 路径迁移到 `mistakes/` 路径。

**路径参数：**

| 参数 | 类型 | 说明 |
|------|------|------|
| id | string | 错题 ID |

**执行流程：**
1. 调用 `Mistake.GetMistakeItem` 获取错题详情
2. 筛选 `sourceRegions` 中 `SourceImagePath` 以 `uploads/` 开头的区域，去重得到 `sourcePaths`
3. 调用 `StudentLearning.MigrateImagesToMistake` gRPC 接口，传入 `sourcePaths`
4. 遍历 gRPC 返回的每张图片迁移结果：
   - 成功 → 将 `sourcePath → newPath` 记入 pathMapping，更新 region 的 `SourceImagePath` 为新路径
   - 失败 → 记录 Error 日志，保留原路径
5. 调用 `Mistake.UpdateMistakeItem` 更新错题的 sourceRegions

**响应（无需迁移）：**

```json
{
  "success": true,
  "message": "所有图片路径已在 mistakes 下，无需迁移",
  "migratedCount": 0
}
```

**响应（有迁移）：**

```json
{
  "success": true,
  "message": "已迁移 3 张图片",
  "migratedCount": 3
}
```

**错误响应：**
- 500 - Failed to migrate images（gRPC 调用异常、GetMistakeItem 异常、UpdateMistakeItem 异常等）

---

## 通用错误格式

```json
{
  "message": "错误描述"
}
```
