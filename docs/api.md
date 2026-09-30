# Admin Portal REST API 文档

## 概述

Admin Portal 提供 REST API 接口，用于管理学生、错题记录、OSS 上传记录、审计等功能。

## 基础信息

- 基础路径: `/api/admin`
- 数据格式: JSON
- 认证: JWT Bearer（`Authorization: Bearer <token>`）。Program.cs 设置 `FallbackPolicy = RequireAuthenticatedUser()`，所有 `/api/*` 端点默认需要认证
- 匿名入口：SPA 静态文件、`/` 首页回退、`/api/auth/login`、`/api/auth/callback`、`/api/auth/oidc/start`、`/api/auth/oidc/callback`、`/api/auth/session` 标记 `[AllowAnonymous]`，无需 JWT（见下方"认证流程"）

---

## 认证流程

Admin Portal 采用 **mode-1 集成部署**：同一容器（端口 5020）既提供后端 REST API，又通过 `wwwroot` 提供前端静态文件与 SPA 路由回退。因此认证按请求路径区分：

- **前端入口（匿名）**：`/` 及所有非 `/api/*` 路径由 SPA fallback 处理，无需登录即可加载登录页
- **API（需认证）**：`/api/*` 受 `FallbackPolicy = RequireAuthenticatedUser()` 保护；默认使用 JWT，显式迁移的管理路径按下文使用管理员会话。匿名协议入口按各自规则豁免。

### 可选托管登录基础（#38）

默认 `AdminOidc:UseSessionForAdminApi=false`：前端和管理 API 继续使用旧 JWT，新 `adminSession` 单独访问管理 API 或三个代理仍返回 401。显式设 true 后，只有 `/api/admin/*` 与 `/api/auth/csrf` 选择 AdminSession；三个代理仍用 Bearer，新 Cookie 单独访问仍 401。前端切换、代理凭据转发、会话续接与登出由 #41、#43/#44、#45/#46 交付。

`AdminOidc:Enabled=false` 默认关闭，新 start/callback/session 入口返回 `503 {"error":"oidc_disabled"}`，旧配置可继续启动。启用配置与部署门禁见 [Deployment.md](development/Deployment.md#可选-signacore-托管登录)。

| 匿名入口 | 行为 |
|---|---|
| `GET /api/auth/oidc/start?returnUrl=/students` | 校验 Discovery，发起 Confidential code + PKCE S256；scope 仅 `openid profile`，return 路径留服务器，默认 `/dashboard` |
| `GET /api/auth/oidc/callback` | 框架处理单值 state/iss/code 或 error；原子消费 5 分钟 state，独立校验 correlation、nonce、严格 RS256 ID Token，再创建 8 小时绝对本地票据并快速 redirect |
| `GET /api/auth/session` | 显式读取 AdminSession：`200 {"authenticated":true,"displayName":"..."}`；无/过期/重启失效票据：`200 {"authenticated":false,"displayName":null}`，不刷新时限；不会把 Bearer 当此会话 |

返回目标只允许绝对站内路径，拒绝外部、编码外部、控制字符、反斜线与 `/api/auth/*`、`/login` 循环；错误目标回退 `/dashboard`。取消返回 `/login?authError=cancelled`；start 的 Discovery/JWKS 不可用返回 `identity_unavailable`；回调协议/兑换/签名等失败统一 `sign_in_failed`，不反射 error_description 或异常。state 无论成功/失败都不重用；失败兑换必须重新 start，不能重试旧 code。入口和 redirect 响应均 no-store/no-cache/no-referrer。

Cookie 为 HttpOnly、Path=/、SameSite=Lax，生产 Secure，只含数据保护后的不透明票据引用；access/id token、截止时间、verifier、已验证 iss+sub 只在单进程内存。请求取消不发布部分票据；存储有界并定期回收，重启/多副本不支持状态延续。展示名取 nickname/name，SMS 身份缺少它们时 null；ID Token 角色不作为本地授权。Cookie challenge/forbid 返回 401/403。此阶段不请求 offline_access、refresh 或上游 logout。

### 可选管理员会话与 CSRF（#42）

`AdminOidc:UseSessionForAdminApi=true` 必须同时 `AdminOidc:Enabled=true`，否则启动失败（仅列配置键）。它是阶段验证开关，默认关闭，不代表生产 audience/注册或前端已联调。

管理路径先显式读取服务器 AdminSession 票据，要求唯一 `iss/sub` 与已验证票据 stamp 相同，issuer 与 Authority 严格相同；只按**当前** `AdminPortal:AdminUserIds`（ID 大小写不敏感）授权，不信任 ID Token role 或浏览器头。任何入站 `Authorization`（空、重复、合法或非法）固定 401，不回落新 Cookie 或旧 `adminAuthToken`。无/过期/丢失票据、缺 access token、无效/到期 `expires_at` 为 `401 {"error":"unauthorized"}`；有效身份不在白名单为 `403 {"error":"forbidden"}`；均为 JSON，不 redirect HTML。8 小时票据寿命不能延长 access token 期限，当前不 refresh。

`GET /api/auth/csrf` 要求相同管理员会话与有效 token，拒绝 Authorization。关闭边界时 `503 {"error":"session_api_disabled"}`；成功 `200 {"requestToken":"..."}`，no-store/no-cache，并设置独立 `adminCsrf` Cookie（HttpOnly、Path=/、SameSite=Lax；生产 Secure，开发仅已校验的数字 loopback 可 HTTP）。request token 绑定主体与 CSRF Cookie，不输出服务器 access/id token。

所有管理写方法（GET/HEAD/OPTIONS/TRACE 之外，包括 POST/PUT/PATCH/DELETE 与 OSS trigger/resolve/batch-resolve）在 Controller/业务/出站/删除前显式验证框架 antiforgery，要求单值 `X-CSRF-TOKEN`。缺/错/重复 header、错 Cookie、异主体 token、只提交 form token 为 `400 {"error":"csrf_invalid"}`；CSRF 成功只继续本次请求，不重放写请求。审计删除前引用复核与来源不可达 502 拒删继续生效；匿名 claims/OIDC callback 不套此浏览器 CSRF 边界。默认 CORS 不变，不启用跨源 Cookie。

true 时 `POST /api/auth/login` 在读取密码/模型绑定前固定 `410 {"error":"legacy_login_disabled"}`，不转发 Identity、不发 JWT Cookie。旧 `/api/auth/logout` 仍按 Bearer 认证且只清旧 JWT Cookie，**不能作为新会话已退出的证明**；新会话撤销/prepared logout 属 #46。三个未迁移代理仍可接受旧 Bearer，关联查询的服务器 token 转发属 #44；不要用空聚合列表判断已完成门户迁移。SPA/非 API 继续匿名，health 语义不变。

### 登录（获取 JWT，默认 legacy 模式）

`POST /api/auth/login` — `[AllowAnonymous]`

请求体：`{ "username": "...", "password": "..." }`

`UseSessionForAdminApi=false` 时后端向 Identity 服务发起 password grant 取 JWT。Identity 回调 `POST /api/auth/callback`（`[AllowAnonymous]`），对白名单 `AdminPortal:AdminUserIds` 中的用户注入 `role:admin`。

成功返回：

```json
{
  "success": true,
  "accessToken": "...",
  "refreshToken": "...",
  "expiresIn": 3600,
  "expiresAt": 1710000000
}
```

前端将 `accessToken` 存入 `localStorage`，后续请求通过 `Authorization: Bearer` 头发送。legacy 路径未携带或 token 无效时返回 **401 Unauthorized**；session 路径按上文拒绝入站 Bearer。

> **集成部署下首次访问**：浏览器直接访问后端端口时，`/` 必须匿名返回 `index.html` 才能加载出登录页（否则陷入「未登录→无法加载登录页→无法登录」的死锁）。API 端点的 401 由前端 axios 拦截器捕获后跳转 `/login`。

---

## 目录

1. [枚举选项](#枚举选项)
2. [身份账户](#身份账户)
3. [图片](#图片)
4. [错题](#错题)
5. [OSS 审计](#oss-审计)
6. [OSS 上传记录](#oss-上传记录)
7. [学生](#学生)
8. [教师/助教关联账户](#教师助教关联账户)

---

## 枚举选项

### 获取所有枚举选项

获取系统中所有可用的枚举选项，包括上传状态、年级、科目、分类、审核状态。

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
  "reviewStatuses": [...]
}
```

> **变更**：2026 年 6 月起不再返回 `mistakeTypes` 字段，错题错误类型已下线（该字段的定义与下线记录由 Ruoyu.Study 仓库的共享常量文档主责）。

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
      "imageRotations": []
    }
  ],
  "totalCount": 100,
  "page": 1,
  "pageSize": 20
}
```

### 获取上传记录关联的业务实体

获取指定上传记录关联的错题/作业条目，用于展示上传记录的分类归属。

**接口:** `GET /api/admin/oss-upload-records/{id}/linked-items`

**路径参数:**
- `id` (string): 上传记录 ID

**响应示例:**

```json
{
  "mistakeItems": [
    {
      "id": "string",
      "subject": 1,
      "grade": 1,
      "reviewStatus": 0,
      "imageIndices": [0, 1]
    }
  ],
  "homeworkItems": [],
  "remainingImageCount": 7,
  "totalImageCount": 9
}
```

> **说明**：`imageIndices` 表示该业务实体引用了上传记录中的哪些图片（**原始索引**，即上传时的顺序，不受移除操作影响）。`remainingImageCount` 表示 `image_paths` 中剩余图片数量，`totalImageCount` 表示原始上传时的总图片数。

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

### 按图片粒度分配上传记录

将上传记录中的指定图片分配为错题条目。一次请求可以包含多个 assignment，每个 assignment 内的图片共享同一道错题（一条 `mistake_item`）。

**接口:** `POST /api/admin/oss-upload-records/{id}/assign`

**路径参数:**
- `id` (string): 上传记录 ID

**请求体:**

```json
{
  "studentId": "string",
  "assignments": [
    {
      "subject": 1,
      "grade": 7,
      "imageIndices": [0, 1],
      "comments": "第 1、2 张图是同一道语文错题"
    },
    {
      "subject": 3,
      "grade": 7,
      "imageIndices": [2],
      "comments": "第 3 张图是另一道英语错题"
    }
  ]
}
```

**请求字段说明:**
- `studentId` (string, 必需): 学生 ID
- `assignments` (array, 必需): 分配条目列表
  - `subject` (int, 必需): 学科 (1-9)
  - `grade` (int, 必需): 年级 (1-12)
  - `imageIndices` (array, 必需): 该 assignment 引用的图片索引（基于上传记录的原始索引）。`imageIndices` 至少包含 1 个元素。
  - `comments` (string, 可选): 备注

**核心语义**：
- **一个 assignment = 一道错题 = 一条 `mistake_item`**。
- 一个 assignment 内的多张图片（如题目+解答）会被打包到同一条 `mistake_item` 的 `source_regions` 中。
- 多个 assignment 可以分别指定不同的 `subject` / `grade`（实现跨学科分配）。
- 同一上传记录可被多次分配（每次请求都会创建新的 `mistake_item`）。
- 任何已被审核通过的图片（即不在 `image_upload_record.image_paths` 中的图片）不能再次被分配。

**响应示例:**

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
    },
    {
      "subject": 3,
      "grade": 7,
      "itemId": "mistake-item-id-2",
      "imageCount": 1
    }
  ],
  "remainingImageCount": 4,
  "totalImageCount": 9
}
```

**响应字段说明:**
- `createdItems[]` (array): 每个 assignment 对应一个元素
  - `subject` (int): 学科
  - `grade` (int): 年级
  - `itemId` (string): 新创建的 `mistake_item` ID
  - `imageCount` (int): 该 item 引用的图片数量
- `remainingImageCount` (int): 分配后 `image_upload_record.image_paths` 中剩余的图片数量（用于前端判断是否需要继续分配）
- `totalImageCount` (int): 上传记录原始总图片数

> **后续流程**：分配后的 `mistake_item` 进入 `PendingReview` 状态，由教师审核。教师完成整批审核（`CompleteUploadReview`）后：
> 1. `Confirmed` 的图片被复制到 `mistakes/`。
> 2. `Rejected` 和 `Confirmed` 的图片路径都会从 `image_upload_record.image_paths` 中移除。
> 3. 当 `image_paths` 清空时，整条上传记录自动删除。

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

### 获取学生关联的教师/助教

根据学生 ID 查询该学生关联的所有教师和助教（通过学生 identityAccountIds 反查 TeacherPortal/AssistantPortal）。

**接口:** `GET /api/admin/students/{studentId:guid}/linked-accounts`

**路径参数:**
- `studentId` (UUID): 学生 ID

**响应示例:**

```json
{
  "teachers": [
    {
      "userId": "550e8400-e29b-41d4-a716-446655440000",
      "displayName": "张老师",
      "phone": "13800138000",
      "subjects": "1,3",
      "role": "教师"
    }
  ],
  "assistants": [
    {
      "userId": "660e8400-e29b-41d4-a716-446655440001",
      "displayName": "李助教",
      "phone": "13900139000",
      "subjects": "2",
      "role": "助教"
    }
  ]
}
```

> **说明**：当 TeacherPortal 或 AssistantPortal 不可用时，对应列表降级为空数组。

---

## 教师/助教关联账户

### 更换教师关联账户

更换教师当前关联的身份账户。

**接口:** `PUT /api/teacher-portal/admin/teachers/{userId}/user-id`

**路径参数:**
- `userId` (string): 教师当前关联的用户 ID

**请求体:**

```json
{
  "newUserId": "660e8400-e29b-41d4-a716-446655440001"
}
```

**响应示例:**

```json
{
  "success": true,
  "data": {
    "id": 1,
    "userId": "660e8400-e29b-41d4-a716-446655440001",
    "phone": "13800138000",
    "username": "张老师",
    "subjects": [1, 3],
    "createdAt": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-02T00:00:00Z"
  }
}
```

**错误情况:**
- NewUserId 已被其他教师占用：`{ "success": false, "message": "NewUserId is already bound to another teacher" }`
- 教师不存在：`{ "success": false, "message": "Teacher not found" }`

### 更换助教关联账户

更换助教当前关联的身份账户。

**接口:** `PUT /api/assistant-portal/admin/assistants/{userId}/user-id`

**路径参数:**
- `userId` (string): 助教当前关联的用户 ID

**请求体:**

```json
{
  "newUserId": "770e8400-e29b-41d4-a716-446655440002"
}
```

**响应示例:**

```json
{
  "success": true,
  "data": {
    "id": 1,
    "userId": "770e8400-e29b-41d4-a716-446655440002",
    "phone": "13900139000",
    "username": "李助教",
    "isActive": true,
    "subjects": [2],
    "createdAt": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-02T00:00:00Z"
  }
}
```

**错误情况:**
- NewUserId 已被其他助教占用：`{ "success": false, "message": "NewUserId is already bound to another assistant" }`
- 助教不存在：`{ "success": false, "message": "Assistant not found" }`

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

### Prepared logout（#46）

`AdminOidc:UseSessionForLogout` 默认 false；true 必须 Enabled 与 UseSessionForAdminApi 同开，并精确注册/配置同 Admin RedirectUri origin 的 `AdminOidc:PostLogoutRedirectUri`，路径固定 `/api/auth/oidc/logout-callback`、生产 HTTPS、无 query/fragment/userinfo。数字 loopback 开发例外沿 OIDC；关闭时保持旧 Bearer logout，不撤新票据。

本人先 GET `/api/auth/logout/csrf` 取得 adminCsrf Cookie 与单值 X-CSRF-TOKEN，再 POST `/api/auth/logout`。专用入口只验证服务器 8h 有效身份票据/唯一 Authority iss/sub/stamp，允许 access token 到期或移出管理员白名单后退出本人，不授业务权限；普通 `/api/auth/csrf` 与管理 API 仍要求有效管理员 token。任何 Authorization 401，CSRF 缺/错/重复/异主体 400、零撤票/HTTP。受保护 Cookie 引用由服务器 TicketDataFormat 读取，锁内 Take 只允许一个并发 winner、Renew 不能复活；先清新 adminSession 和旧 adminAuthToken，再向固定 Authority `/oauth2/logout/requests` 发服务端 client_secret_post、服务器 id_token_hint、精确 PostLogout URI 及随机 state，不自动重试/跟随 redirect，不向浏览器发 ID token/secret。

成功 JSON `{success:true,upstreamLogout:true,logoutUrl:...}` 仅导航验证后同源或相对 `/oauth2/logout?logout_handle=<43base64url>` 的唯一 URL；相对 URL 解析成 Authority 同源绝对 URL。准备失败、畸形/超大响应、错误 URL、超时、取消、容量不足或缺 ID token 均永久保持本地退出，响应可写时 `{success:true,upstreamLogout:false}`，断连不能保证浏览器收到结果。重新查询本地状态恢复，不推定远端成功。整体发送/读取 10 秒上限、JSON 最多 4096B；私有 HTTP 无浏览器 Cookie/Authorization、日志或 redirect，协议路径不进入 telemetry。

完成回跳只支持 GET、单值 state + 独立 HttpOnly/Secure/SameSite=Lax/Path=callback 绑定 Cookie，进程内 4096 容量/5 分钟绝对时限/原子单用；正确 302 `/login?loggedOut=1`，缺/错/重复/到期/错浏览器 400 `logout_callback_invalid`，GET 不发起本地撤票。退出和回跳 no-store/no-cache/no-referrer，回滚不能恢复已撤票据/state。SignaCore 验证 ID token hint 的 iat 24h、忽略 exp，本地 8h 更短；下游已发令牌不保证立即失效。真实 PostLogout/audience 注册与 SPA 激活仍由维护者/#41/IKJ8MO 完成，fake Authority 专项不能代替生产联调。专项 `PreparedLogout_` 与 `AdminLogoutStoresTests` 使用隔离数据库/虚构凭据，无生产 OSS 删除。
