# Admin Portal REST API 文档

## 概述

Admin Portal 提供 REST API 接口，用于管理学生、错题记录、OSS 上传记录、审计等功能。

## 基础信息

- 基础路径: `/api/admin`
- 数据格式: JSON
- 认证: 管理 API 只使用服务器 AdminSession；任何入站 `Authorization` 固定401，不采用浏览器 JWT。
- 匿名入口：SPA 静态文件、`/` 首页回退、`/api/auth/login`、`/api/auth/callback`、`/api/auth/oidc/start`、`/api/auth/oidc/callback`、`/api/auth/session` 标记 `[AllowAnonymous]`，按各自协议匿名处理（见下方"认证流程"）

---

## 认证流程

Admin Portal 采用 **mode-1 集成部署**：同一容器（端口 5020）既提供后端 REST API，又通过 `wwwroot` 提供前端静态文件与 SPA 路由回退。因此认证按请求路径区分：

- **前端入口（匿名）**：`/` 及所有非 `/api/*` 路径由 SPA fallback 处理，无需登录即可加载登录页
- **API（需认证）**：`/api/*` 受 `FallbackPolicy = RequireAuthenticatedUser()` 保护；管理路径与代理按下文使用管理员会话。匿名协议入口按各自规则豁免。

### 托管登录基础（#38）

当前版本只运行服务器会话托管登录，协议与安全职责由官方包 `SignaCore.Client.AspNetCore`（#83 起替换自写实现）承担：授权码 + PKCE S256 回调、服务器票据存储、pending state、prepared logout 与其端点的 user-neutral antiforgery 边界。必须配置 `IdentityService:Authority/AppId/AppSecret`、`AdminOidc:RedirectUri/PostLogoutRedirectUri`、当前管理员白名单；`IdentityService:ClockSkewSeconds` 仍参与启动校验但运行时固定 30 秒（包内值）。管理员白名单通过包的 PreSignInAuthorizationDecision（新会话签入前）与 AuthorizationDecision（会话状态读取）两个适配器执行：非白名单 subject 不签入、无票据无 Cookie。五个旧键 `AdminOidc:Enabled/UseSessionForAdminApi/UseSessionForLogout/UseSessionForIdentityProxy/UseSessionForPortalProxies` 不再选择运行模式：应删除；仅缺省或规范小写 `true` 可通过迁移检验，显式 `false`、空值或畸形值在所有环境启动拒绝。密码入口永久 `410 legacy_login_disabled`，API/图片/三个代理/关联查询始终使用同一管理员会话与 CSRF 边界，任何入站 Authorization 固定401；不存在关闭能力或浏览器 JWT 回退模式。

缺失或非法必需认证配置在任何宿主、迁移、worker 或监听前拒绝，不创建 disabled 实例。配置与只读预检见 [Deployment.md](development/Deployment.md#signacore-托管登录唯一模式)。

| 匿名入口 | 行为 |
|---|---|
| `GET /api/auth/oidc/start?returnUrl=/students` | 包端点：读取 Discovery 后发起 Confidential code + PKCE S256；scope 仅 `openid profile`，return 路径为单值本地路径，缺省 `/`；Discovery 失败重定向 `/login?authError=identity_unavailable` |
| `GET /api/auth/oidc/callback` | 包端点：单值 state/iss/code/error 检查、原子消费 5 分钟服务器端 pending state、严格校验 RS256 ID Token 与（配置门禁后）at+jwt access token 并做 iss/sub 关联，白名单 Decision 通过后才写票据与 Cookie |
| `GET /api/auth/session` | 始终只返回三字段 `authenticated/displayName/requiresReauthentication`；匿名/坏票据 false/null/false，合法 token 到期 false/展示名/true，不刷新时限；不会把 Bearer 当此会话 |

返回目标只允许绝对站内路径，拒绝外部、编码外部、控制字符、反斜线与 `/api/auth/*`、`/login` 循环；错误目标回退 `/dashboard`。取消返回 `/login?authError=cancelled`；start 的 Discovery/JWKS 不可用返回 `identity_unavailable`；回调协议/兑换/签名等失败统一 `sign_in_failed`，不反射 error_description 或异常。state 无论成功/失败都不重用；失败兑换必须重新 start，不能重试旧 code。入口和 redirect 响应均 no-store/no-cache/no-referrer。

会话 Cookie（名 `adminSession`）为 HttpOnly、Path=/、SameSite=Lax、Secure（`AdminOidc:IntranetHttpOrigins` 清单生效的内网 HTTP profile 下包 Cookie 不带 Secure，切换 profile 需重新登录），值即包内票据存储的不透明键；access/id token、票据主体与截止时间只在单进程内存。会话寿命不超过 access token 到期；到期票据由存储在读时回收，过期与不存在不可区分。请求取消不发布部分票据；存储有界（默认容量 10000）并定期回收，重启/多副本不支持状态延续。展示名取 nickname/name，缺失时 null；ID Token 角色不进入业务边界主体（accessor 只重建 iss/sub/display_name）。挑战/拒绝由包 session scheme 处理（challenge 重定向 start、forbid 403），业务面短路仍为 401/403 JSON。基础 Code 请求不包含 offline_access/refresh；SPA 退出走下文的 prepared logout。

### 管理员会话与 CSRF（#42）

API 始终使用 AdminSession；旧键仅用于启动迁移检验，不能恢复任何旧凭据路径。

管理路径先显式读取服务器 AdminSession 票据，要求唯一 `iss/sub` 与已验证票据 stamp 相同，issuer 与 Authority 严格相同；只按**当前** `AdminPortal:AdminUserIds`（ID 大小写不敏感）授权，不信任 ID Token role 或浏览器头。任何入站 `Authorization`（空、重复、合法或非法）固定 401，不回落新 Cookie 或旧 `adminAuthToken`。无/过期/丢失票据、缺 access token、无效/到期 `expires_at` 为 `401 {"error":"unauthorized"}`；有效身份不在白名单为 `403 {"error":"forbidden"}`；均为 JSON，不 redirect HTML。8 小时票据寿命不能延长 access token 期限，当前不 refresh。

`GET /api/auth/csrf` 要求相同管理员会话与有效 token，拒绝 Authorization。成功 `200 {"requestToken":"..."}`，no-store/no-cache，并设置独立 `adminCsrf` Cookie（HttpOnly、Path=/、SameSite=Lax；生产 Secure，允许 HTTP 入口的仅开发数字 loopback 与 `AdminOidc:IntranetHttpOrigins` 清单生效的内网 profile，两者均 `SameAsRequest`）。request token 绑定主体与 CSRF Cookie，不输出服务器 access/id token。

所有管理写方法（GET/HEAD/OPTIONS/TRACE 之外，包括 POST/PUT/PATCH/DELETE 与 OSS trigger/resolve/batch-resolve）在 Controller/业务/出站/删除前显式验证框架 antiforgery，要求单值 `X-CSRF-TOKEN`。缺/错/重复 header、错 Cookie、异主体 token、只提交 form token 为 `400 {"error":"csrf_invalid"}`；CSRF 成功只继续本次请求，不重放写请求。审计所有resolve状态都执行三provider共同collector，完整v1为409，来源不可达502，恒拒删；匿名 claims/OIDC callback 不套此浏览器 CSRF 边界。默认 CORS 不变，不启用跨源 Cookie。

`POST /api/auth/login` 在读取密码/模型绑定前固定 `410 {"error":"legacy_login_disabled"}`，不转发 Identity、不发 JWT Cookie。登出走包端点 `POST /api/auth/oidc/logout`（见 Prepared logout）；非 POST 固定405且不撤票或 prepare，并在所有 POST 尝试上清理旧 `adminAuthToken` Cookie。三个代理与关联查询只使用服务器票据内 token。SPA/非 API 继续匿名，health 语义不变。

### 已退役的密码登录

`POST /api/auth/login` 永久返回 `410 {"error":"legacy_login_disabled"}`。所有合法配置以及大小写/尾斜线、空或畸形请求体、任意 Cookie/Authorization 都在认证和模型绑定前拒绝；不读取密码、不兑换 Identity token、不签发 Cookie，也不返回 access/refresh token。配置不能恢复密码入口。请使用 `/api/auth/oidc/start` 的 SignaCore 托管登录。

`POST /api/auth/callback` 的 `{ "userId": "..." }` → `{ "roles": ["admin"] }` 白名单契约保持；非白名单返回空 roles。SPA 和非 API 路径保持匿名。旧完整镜像回滚属于独立部署动作，本版不能复活密码登录或已撤票据。最终发布必须包含 #41 的托管登录 SPA，不部署仍含旧密码表单的中间镜像。

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

`MistakeService:UseSessionToken=true` 时，下列管理调用及上传分类/LegacyCheck/LegacyClean/错题图片共同使用当前服务器票据出站；任何入站 Authorization 仍 401，unsafe 仍先校验 CSRF。发送前票据/截止时间无效为 401（合法 token 到期 `reauthentication_required`），当前白名单拒绝为 403，均零 Mistake HTTP。

目标 401/403/3xx、非法/缺失必需 data、JSON/TLS/网络/超时或未知写结果为安全 502。JSON controller 使用固定 ProblemDetails：`mistake.unavailable`（502）、`mistake.request_rejected`（合法业务失败 400）、`mistake.conflict`（合法业务冲突 409）；图片使用相同状态与固定安全 error，合法结果为签名 URL redirect，URL 不携带 server Bearer。真实详情 GET 404 与合法空列表保留，不把目标拒绝伪装成空 DTO。取消传播，每个 unsafe 方法应用发送最多一次，失败后不继续 Student/S3 写，无 refresh/replay；已发送写的提交状态可能未知。默认 false 保留原 Mistake 下游兼容，不改变管理入站身份。

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

详细语义见[StorageAudit](./modules/OssAudit/StorageAudit.md)，上游v1字段合同由Study主责。

| 方法与路径 | 当前行为 |
|---|---|
| GET `/api/admin/oss-audit/records` | page/pageSize/status/bucket分页；保留items/totalCount/statusCounts/bucketCounts，Status3为UnreferencedObservation；bucketCounts包含旧0和新3 |
| GET `/api/admin/oss-audit/status` | isRunning、lastCompleted/lastFailed、pendingCount、observationCount、deletionAuthorized=false；完成Run包含referenceContractVersion/referenceSnapshots（三provider metadata JSON） |
| POST `/api/admin/oss-audit/trigger` | 沿现认证/CSRF边界触发异步Run；运行中400，正常200 OperationResponse |
| POST `/api/admin/oss-audit/records/{id}/resolve` | 已存在任何0/1/2/3状态先完整collector；v1恒409 cleanup_not_authorized，依赖不全502 references_unavailable；未知id404，零S3写和record删除 |
| POST `/api/admin/oss-audit/records/batch-resolve` | 非空ids同collector与409/502，包括部分/全missing；空ids400，零S3写和record删除 |
| POST `/api/admin/oss-audit/records/{id}/ignore` | body `{note}`；仅旧status0更新为2并记ResolvedAt/Note，其他状态400、未知404 |

拒绝响应为 `{success:false,errorKind:...}`。新Run的NewZombieCount兼容字段仅表示新增只读观察数量；旧Run/version=null保留历史原意。v1不是GC或物理删除许可。

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

### Prepared logout（#46，#83 起由包承担）

退出始终使用 prepared logout，由包端点 `POST /api/auth/oidc/logout` 处理。必需的 `AdminOidc:PostLogoutRedirectUri` 与 RedirectUri 同 origin，路径固定 `/api/auth/oidc/logout/return`、生产 HTTPS、无 query/fragment/userinfo；维护者需在 SignaCore 应用注册把 PostLogout 回调改为该值。数字 loopback 开发例外沿 OIDC；`AdminOidc:IntranetHttpOrigins` 清单内 origin 同样豁免 HTTP。非 POST logout 固定405（本地守卫先于授权管线），且每次 POST 尝试都清理旧 `adminAuthToken` Cookie。

SPA 先 GET `/api/auth/oidc/csrf`（公开签发，响应 `{"token":"..."}` 并轮换共享 `adminCsrf` Cookie；user-neutral 对，与业务面 principal 绑定对共用同一 Cookie），再以导航式 form POST 提交 `__RequestVerificationToken` 到 `/api/auth/oidc/logout`；antiforgery 校验同时接受单值 `X-CSRF-TOKEN` header。缺/错 token 为固定 `400 {"outcome":"csrf_rejected"}`，不触碰会话。登出本身不以 access token 期限或管理员白名单阻止本人退出（票据存在即可）；无会话 Cookie 时直接 `200 {"outcome":"local_only"}`。

包在按会话键加锁的串行门内先移除服务器票据并删除会话 Cookie（本地撤销先行、永不回滚、并发登出至多一次撤销与一次准备），再向 Authority `/oauth2/logout/requests` 发服务端 HTTP Basic 认证、服务器保留的 id_token_hint、精确 PostLogout URI 与一次性 state，不自动重试/跟随 redirect，不向浏览器发 ID token/secret；backchannel 与其余包协议流量共享命名客户端并传播 correlation id，但不进入 telemetry。

成功为 302 到验证过的同源 `logout_uri`（唯一 43 字符 base64url `logout_handle`）；准备失败、畸形/跨源/超大 URL、错误状态、超时或取消均保持本地退出并响应固定 `200 {"outcome":"local_only"}`（浏览器导航式提交时直接显示该 JSON）。回跳 `GET /api/auth/oidc/logout/return?state=...` 只以单值 state + 独立 `adminSession-logout-return` 绑定 Cookie（HttpOnly/Secure/SameSite=Lax/Path=`/api/auth/oidc/logout`、5 分钟）原子单用消费；正确 302 `/login`，其余固定 400 HTML 页。下游已发令牌不保证立即失效。真实 PostLogout 注册变更由维护者部署时执行；专项 `PreparedLogout_` 测试使用隔离数据库/虚构凭据，无生产 OSS 删除。

### 令牌过期与显式重认证（#45）

在唯一会话模式，票据随 access token 到期被存储回收，过期与不存在不可区分：业务门禁为 `401 {"error":"unauthorized"}`，零下游/OSS 删除、不重放写请求（#83 起不再有独立的 reauthentication_required 业务错误；SPA 对过期会话按匿名跳登录）。`GET /api/auth/session` 为 `{authenticated,displayName,requiresReauthentication}`：有效 token true/false；票据缺失但会话 Cookie 仍在（过期/撤票）为 false/true 且显示名 null；从未登录 false/false。当前非管理员403优先；任意 Authorization401。浏览器仅显式进入 `/api/auth/oidc/start?returnUrl=...` 受控授权（returnUrl 为单值本地路径，缺省 `/`）；上游会话可复用立即回跳，否则显示托管登录；取消/失败保持既有固定结果（cancelled/sign_in_failed/identity_unavailable 映射不变），BFF不自动挑战。

### 门户服务端凭据（#44）

Teacher/Assistant 全代理前缀始终在认证/授权前共用管理员/CSRF 门禁，合法 access token 到期返回 `401 reauthentication_required` 且零出站，仅发送服务器 Bearer，剥离浏览器 Cookie、Host、CSRF 与 gateway 头、下游 Set-Cookie。保留 admin/auth/其他路径映射、query/body、503/502 和下游401/403；取消传递且不重放。关联查询共用相同管理员边界，只转发服务器 token，失败门禁在 Student/门户调用前拒绝。业务单侧失败/畸形响应继续该侧空列表，另一侧正常，此聚合不保证失败可见性或两门户一致。生产 rollout 不在 #75 实现范围；当前组合的正式验证使用官方 Provider 与真实下游。专项 `PortalSession_`、`AssociationsSession_` 在隔离数据库/fake HTTP 上运行，不触生产。

### Identity 代理服务端会话（#43）

全 `/api/identity` 前缀（大小写、根和尾斜线）在认证/授权前经过共享管理员与 CSRF 边界。拒绝任何入站 Authorization；合法 access token 到期时返回 `401 reauthentication_required` 且零出站；只转发服务器票据内有效 access token，剥离浏览器 Cookie、Host、X-CSRF-TOKEN 与伪造 gateway 头后注入本服务 AppId/AppSecret。下游 Set-Cookie 不传给浏览器；401/403/结构化503和 Retry-After 原样，网络失败502，取消传递且不重放。AppSecret 剥离始终保持，三个代理共用相同会话边界。生产平台注册不由本项执行；正式组合门禁归 #75。专项 `FullyQualifiedName~IdentitySession_` 使用 fake HTTP 与隔离数据库，无生产 OSS 删除。

Identity 代理退役浏览器凭据兼容（#73）：middleware 的 settings 为必需依赖，直接调用缺 TrustedSessionKey/有效服务器 token 也401拒绝。所有路径使用 StartsWithSegments remaining 映射 `/api`，根/尾斜线/大小写/query/body 保持；始终 RequestAborted，预取消/发送中取消传播且无应用层重试。非代理的 IdentityAccountsController 不在此切片范围。中间镜像不部署，最终须包含 #41/#75；回滚只选择明确旧完整镜像，不复活票据。

门户传输退役浏览器凭据兼容（#74）：两 middleware 只接受 TrustedSessionKey 的有效 server token，settings 为必需依赖，直接调用缺可信项也401。统一 remaining 的 admin/auth/其他三类映射，始终 RequestAborted、剥离浏览器Authorization/Cookie/Host/CSRF/gateway，隔离Set-Cookie；取消传播且无应用层重试。关联查询只随API-session，即使门户proxy开关false也用server token；当前账户筛选、单侧失败保另一侧、无关联短路与Student失败响应保持。此中间镜像不部署，最终须组合#41/#75；整版回滚不恢复本版浏览器凭据路径或复活撤票。

### 管理本地会话组收敛（#72）

`/api/admin/*`（含 native image）、普通 CSRF、三个代理和关联查询始终共用 AdminSession 边界，任何 Authorization401、白名单403、期限401、普通 unsafe CSRF400 与三字段会话状态保持。全局入站 Bearer handler 与 JWT Cookie 验证均未注册；默认认证和 FallbackPolicy 也只选择服务器会话。

登出面（`/api/auth/oidc/csrf|logout|logout/return`）由 SignaCore 包端点拥有，不携带六头安全基线（应答自带 no-store；antiforgery 签发可附 X-Frame-Options: SAMEORIGIN）；非 POST logout405、POST 恒清理旧 adminAuthToken。退出身份不以 access 期限或管理员白名单阻止本人退出；成功/失败/取消与一次性 state 按包协议。SPA/health 匿名，不更改业务/OSS/三个代理。API+SPA 必须使用最终完整集成镜像；回滚只选择明确旧完整镜像，不复活撤票状态。
