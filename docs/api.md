# Admin Portal REST API 文档

## 概述

Admin Portal 提供 REST API 接口，用于管理学生、错题记录、OSS 上传记录、审计等功能。

## 基础信息

- 基础路径: `/api/admin`
- 数据格式: JSON
- 认证: 由 Identity 代理中间件处理

---

## 目录

1. [枚举选项](#枚举选项)
2. [身份账户](#身份账户)
3. [图片](#图片)
4. [错题](#错题)
5. [OSS 审计](#oss-审计)
6. [OSS 上传记录](#oss-上传记录)
7. [学生](#学生)

---

## 枚举选项

### 获取所有枚举选项

获取系统中所有可用的枚举选项，包括上传状态、年级、科目、分类、审核状态和错题类型。

**接口:** `GET /api/admin/enum-options`

**响应示例:**

```json
{
  "uploadStatuses": [
    { "value": 0, "name": "Unspecified", "displayName": "未指定" },
    { "value": 1, "name": "Uploaded", "displayName": "已上传" }
  ],
  "grades": [...],
  "subjects": [...],
  "classifications": [...],
  "reviewStatuses": [...],
  "mistakeTypes": [...]
}
```

---

## 身份账户

### 批量获取身份账户信息

根据账户 ID 列表批量获取身份账户信息。

**接口:** `POST /api/admin/identity-accounts/batch`

**请求体:**

```json
{
  "accountIds": ["550e8400-e29b-41d4-a716-446655440000", "..."]
}
```

**响应示例:**

```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "username": "john_doe",
    "displayName": "张三",
    "phone": "13800138000",
    "remark": "备注信息"
  }
]
```

### 根据身份账户获取学生列表

获取指定身份账户关联的所有学生。

**接口:** `GET /api/admin/accounts/{accountId:guid}/students`

**路径参数:**
- `accountId` (UUID): 身份账户 ID

**响应示例:**

```json
[
  {
    "id": "string",
    "name": "张三",
    "grade": 1,
    "identityAccountIds": ["550e8400-e29b-41d4-a716-446655440000"],
    "createdAt": 1234567890,
    "updatedAt": 1234567890
  }
]
```

---

## 图片

### 获取图片

从 OSS 存储中获取图片文件。

**接口:** `GET /api/admin/image`

**查询参数:**
- `path` (string, 必需): OSS 对象路径

**响应:** 图片二进制流

---

## 错题

### 获取错题列表

分页获取错题记录列表，支持多种筛选条件。

**接口:** `GET /api/admin/mistakes`

**查询参数:**
- `studentId` (string, 可选): 学生 ID
- `subject` (int, 可选): 科目
- `grade` (int, 可选): 年级
- `reviewStatus` (int, 可选): 审核状态
- `page` (int, 可选, 默认: 1): 页码
- `size` (int, 可选, 默认: 20, 最大: 100): 每页大小

**响应示例:**

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
      "reviewedAt": 1234567890,
      "reviewComment": "string",
      "type": 0,
      "questionId": "string",
      "createdAt": 1234567890,
      "updatedAt": 1234567890,
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

### 获取单个错题详情

获取指定错题 ID 的详细信息。

**接口:** `GET /api/admin/mistakes/{id}`

**路径参数:**
- `id` (string): 错题 ID

**响应示例:**

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
  "reviewedAt": 1234567890,
  "reviewComment": "string",
  "type": 0,
  "questionId": "string",
  "createdAt": 1234567890,
  "updatedAt": 1234567890,
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

### 根据上传记录获取错题

获取指定上传记录关联的所有错题。

**接口:** `GET /api/admin/mistakes/by-upload/{uploadId}`

**路径参数:**
- `uploadId` (string): 上传记录 ID

**响应示例:**

```json
{
  "items": [...],
  "total": 10
}
```

### 更新错题信息

更新指定错题的信息。

**接口:** `PUT /api/admin/mistakes/{id}`

**路径参数:**
- `id` (string): 错题 ID

**请求体:**

```json
{
  "studentId": "string",
  "subject": 1,
  "grade": 1
}
```

**响应示例:**

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

---

## OSS 审计

### 获取审计记录列表

获取 OSS 审计记录列表，支持按状态和 Bucket 筛选。

**接口:** `GET /api/admin/oss-audit/records`

**查询参数:**
- `page` (int, 可选, 默认: 1): 页码
- `pageSize` (int, 可选, 默认: 20): 每页大小
- `status` (int, 可选): 状态 (0: Pending, 1: Resolved, 2: Ignored)
- `bucket` (string, 可选): Bucket 名称

**响应示例:**

```json
{
  "items": [
    {
      "id": 1,
      "objectPath": "string",
      "bucket": "uploads",
      "size": 1024,
      "lastModified": 1234567890,
      "status": 0,
      "statusText": "Pending",
      "createdAt": 1234567890,
      "resolvedAt": null,
      "note": null
    }
  ],
  "totalCount": 100,
  "page": 1,
  "pageSize": 20,
  "statusCounts": {
    "0": 50,
    "1": 30,
    "2": 20
  },
  "bucketCounts": {
    "uploads": 50
  }
}
```

### 触发审计

手动触发 OSS 审计。

**接口:** `POST /api/admin/oss-audit/trigger`

**响应示例:**

```json
{
  "success": true,
  "message": "Audit triggered. Results will be available shortly."
}
```

### 解决审计记录

标记审计记录为已解决并删除对应的 OSS 对象。

**接口:** `POST /api/admin/oss-audit/records/{id}/resolve`

**路径参数:**
- `id` (long): 审计记录 ID

**响应示例:**

```json
{
  "success": true,
  "message": "Record resolved and object deleted."
}
```

### 忽略审计记录

标记审计记录为已忽略。

**接口:** `POST /api/admin/oss-audit/records/{id}/ignore`

**路径参数:**
- `id` (long): 审计记录 ID

**请求体:**

```json
{
  "note": "备注信息"
}
```

**响应示例:**

```json
{
  "success": true,
  "message": "Record ignored."
}
```

### 批量解决审计记录

批量解决审计记录。

**接口:** `POST /api/admin/oss-audit/records/batch-resolve`

**请求体:**

```json
{
  "ids": [1, 2, 3]
}
```

**响应示例:**

```json
{
  "resolvedCount": 2,
  "errors": ["path/to/file: 被上传记录引用，跳过"],
  "totalRequested": 3
}
```

---

## OSS 上传记录

### 获取所有上传记录

分页获取所有上传记录，支持按状态和学生筛选。

**接口:** `GET /api/admin/oss-upload-records`

**查询参数:**
- `page` (int, 可选, 默认: 1): 页码
- `pageSize` (int, 可选, 默认: 20): 每页大小
- `status` (int, 可选, 默认: -1): 上传状态 (-1 表示所有)
- `studentId` (string, 可选): 学生 ID

**响应示例:**

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
      "updatedAt": 1234567890,
      "classification": 1,
      "subject": 1,
      "grade": 1
    }
  ],
  "totalCount": 100,
  "page": 1,
  "pageSize": 20
}
```

### 获取分类选项

获取可用的分类选项。

**接口:** `GET /api/admin/oss-upload-records/classification-options`

**响应示例:**

```json
[
  {
    "value": 1,
    "name": "Homework",
    "displayName": "作业"
  }
]
```

### 重置上传记录状态

重置上传记录的状态。

**接口:** `POST /api/admin/oss-upload-records/{id}/reset-status`

**路径参数:**
- `id` (string): 上传记录 ID

**请求体:**

```json
{
  "studentId": "string",
  "targetStatus": 1
}
```

**响应示例:**

```json
{
  "success": true,
  "message": "Status reset successfully"
}
```

### 分配上传记录

分配上传记录给学生，并设置分类、科目和年级。

**接口:** `POST /api/admin/oss-upload-records/{id}/assign`

**路径参数:**
- `id` (string): 上传记录 ID

**请求体:**

```json
{
  "studentId": "string",
  "classification": 1,
  "subject": 1,
  "grade": 1
}
```

**响应示例:**

```json
{
  "success": true,
  "message": "Assignment successful"
}
```

### 获取上传记录图片

获取上传记录中的图片。

**接口:** `GET /api/admin/oss-upload-records/image`

**查询参数:**
- `path` (string, 必需): 图片路径

**响应:** 图片二进制流

### 旋转上传记录图片

旋转上传记录中的图片。

**接口:** `POST /api/admin/oss-upload-records/{id}/rotate`

**路径参数:**
- `id` (string): 上传记录 ID

**请求体:**

```json
{
  "studentId": "string",
  "imageIndex": 0,
  "rotation": 90
}
```

**响应示例:**

```json
{
  "success": true
}
```

---

## 学生

### 获取学生列表

分页获取学生列表，支持按姓名和年级筛选。

**接口:** `GET /api/admin/students`

**查询参数:**
- `name` (string, 可选): 学生姓名
- `grade` (int, 可选): 年级
- `page` (int, 可选, 默认: 1): 页码
- `pageSize` (int, 可选, 默认: 20, 最大: 100): 每页大小

**响应示例:**

```json
{
  "items": [
    {
      "id": "string",
      "name": "张三",
      "grade": 1,
      "identityAccountIds": ["550e8400-e29b-41d4-a716-446655440000"],
      "createdAt": 1234567890,
      "updatedAt": 1234567890
    }
  ],
  "totalCount": 100,
  "page": 1,
  "pageSize": 20
}
```

### 获取年级选项

获取所有可用的年级选项。

**接口:** `GET /api/admin/students/grades`

**响应示例:**

```json
[
  {
    "value": 1,
    "label": "小学一年级"
  }
]
```

### 获取学生详情

获取指定学生的详细信息。

**接口:** `GET /api/admin/students/{studentId:guid}`

**路径参数:**
- `studentId` (UUID): 学生 ID

**响应示例:**

```json
{
  "id": "string",
  "name": "张三",
  "grade": 1,
  "identityAccountIds": ["550e8400-e29b-41d4-a716-446655440000"],
  "createdAt": 1234567890,
  "updatedAt": 1234567890
}
```

### 创建学生

创建新的学生记录。

**接口:** `POST /api/admin/students`

**请求体:**

```json
{
  "name": "张三",
  "grade": 1,
  "identityAccountIds": ["550e8400-e29b-41d4-a716-446655440000"]
}
```

**响应示例:**

```json
{
  "id": "string",
  "name": "张三",
  "grade": 1,
  "identityAccountIds": ["550e8400-e29b-41d4-a716-446655440000"],
  "createdAt": 1234567890,
  "updatedAt": 1234567890
}
```

### 更新学生

更新学生信息。

**接口:** `PUT /api/admin/students/{studentId:guid}`

**路径参数:**
- `studentId` (UUID): 学生 ID

**请求体:**

```json
{
  "name": "张三",
  "grade": 1,
  "identityAccountIds": ["550e8400-e29b-41d4-a716-446655440000"]
}
```

**响应示例:**

```json
{
  "success": true,
  "message": "Student updated successfully."
}
```

### 删除学生

删除学生记录。

**接口:** `DELETE /api/admin/students/{studentId:guid}`

**路径参数:**
- `studentId` (UUID): 学生 ID

**响应示例:**

```json
{
  "success": true,
  "message": "Student deleted."
}
```

### 获取学生关联的身份账户

获取指定学生关联的所有身份账户 ID。

**接口:** `GET /api/admin/students/{studentId:guid}/accounts`

**路径参数:**
- `studentId` (UUID): 学生 ID

**响应示例:**

```json
["550e8400-e29b-41d4-a716-446655440000"]
```

### 关联身份账户到学生

将身份账户关联到学生。

**接口:** `POST /api/admin/students/{studentId:guid}/accounts`

**路径参数:**
- `studentId` (UUID): 学生 ID

**请求体:**

```json
{
  "identityAccountId": "550e8400-e29b-41d4-a716-446655440000"
}
```

**响应示例:**

```json
{
  "success": true,
  "message": "Identity account linked to student successfully."
}
```

### 解除身份账户与学生的关联

解除身份账户与学生的关联。

**接口:** `DELETE /api/admin/students/{studentId:guid}/accounts/{accountId:guid}`

**路径参数:**
- `studentId` (UUID): 学生 ID
- `accountId` (UUID): 身份账户 ID

**响应示例:**

```json
{
  "success": true,
  "message": "Identity account unlinked from student."
}
```

### 获取学生开放科目

获取学生的开放科目配置。

**接口:** `GET /api/admin/students/{studentId:guid}/open-subjects`

**路径参数:**
- `studentId` (UUID): 学生 ID

**查询参数:**
- `activeOnly` (bool, 可选, 默认: false): 是否仅返回活跃的科目

**响应示例:**

```json
[
  {
    "id": "string",
    "subject": 1,
    "openStartDate": "2024-01-01",
    "openEndDate": "2024-12-31",
    "isActive": true
  }
]
```

### 设置学生开放科目

设置学生的开放科目配置。

**接口:** `PUT /api/admin/students/{studentId:guid}/open-subjects`

**路径参数:**
- `studentId` (UUID): 学生 ID

**请求体:**

```json
{
  "subjects": [
    {
      "subject": 1,
      "openStartDate": "2024-01-01",
      "openEndDate": "2024-12-31"
    }
  ]
}
```

**响应示例:**

```json
{
  "success": true,
  "message": "Open subjects updated successfully."
}
```

### 获取科目选项

获取所有可用的科目选项。

**接口:** `GET /api/admin/students/subject-options`

**响应示例:**

```json
[
  {
    "value": 1,
    "name": "Math",
    "displayName": "数学"
  }
]
```

---

## 错误响应

所有错误响应都遵循以下格式：

```json
{
  "message": "错误描述"
}
```

常见 HTTP 状态码：
- `400 Bad Request`: 请求参数错误
- `404 Not Found`: 资源不存在
- `500 Internal Server Error`: 服务器内部错误
- `502 Bad Gateway`: 下游服务不可用
