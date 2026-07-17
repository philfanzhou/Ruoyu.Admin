# 助教管理 — 功能规格

## 1. 助教权限管理

### 1.1 助教列表

- API: `GET /api/assistant-portal/admin/assistants`
- 返回所有助教账号，含 userId、phone、username、subjects、isActive、createdAt、updatedAt
- 表格列：User ID、手机号、用户名、科目（Tag 列表）、创建时间、操作

### 1.2 授予助教权限

- API: `POST /api/assistant-portal/admin/identity-users/{userId}/grant-assistant`
- 请求体: `{ subjects: number[] }`
- 交互: 弹窗搜索 Identity 用户 → 选择 → 选科目 → 确认授予

### 1.3 撤销助教权限

- API: `POST /api/assistant-portal/admin/identity-users/{userId}/revoke-assistant`
- 交互: 行操作"撤销权限"按钮，二次确认

### 1.4 科目管理

- 查询: `GET /api/assistant-portal/admin/assistants/{userId}/subjects`
- 整体设置: `PUT /api/assistant-portal/admin/assistants/{userId}/subjects`
- 添加: `POST /api/assistant-portal/admin/assistants/{userId}/subjects/{subject}`
- 删除: `DELETE /api/assistant-portal/admin/assistants/{userId}/subjects/{subject}`
- 可用科目: `GET /api/assistant-portal/admin/available-subjects`
- 交互: Tag closable 删除单个 + "管理"按钮弹窗批量编辑

## 2. 助教-学生关联

### 2.1 数据模型

新增 `assistant_students` 关联表：

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | bigint | PK, auto-increment |
| AssistantId | bigint | FK → assistant_accounts.Id |
| StudentId | varchar(36) | FK → 对应 Student 服务的 student.Id (GUID string) |
| CreatedAt | DateTimeOffset | 创建时间 |

- 一个助教可关联多个学生
- 一个学生可被多个助教关联（多对多）
- 联合唯一约束: (AssistantId, StudentId)

### 2.2 API

- `GET /api/admin/assistants/{userId}/students` — 获取助教关联的学生列表
- `POST /api/admin/assistants/{userId}/students` — 批量设置助教关联学生（替换式）
  - 请求体: `{ studentIds: string[] }` (GUID strings)
- `POST /api/admin/assistants/{userId}/students/{studentId}` — 添加单个关联 (studentId = GUID string)
- `DELETE /api/admin/assistants/{userId}/students/{studentId}` — 删除单个关联 (studentId = GUID string)

### 2.3 前端交互

- 助教列表操作栏新增"管理学生"按钮
- 弹窗展示当前关联学生列表（姓名、年级）
- 搜索学生（调用 Student gRPC 或现有 admin 接口）
- 添加/移除学生关联
- 撤销助教权限时自动清理关联关系

## 3. Admin Portal 代理

### 3.1 AssistantPortalProxyMiddleware

路径映射规则（与 TeacherPortalProxyMiddleware 对称）：

| Admin 请求路径 | 转发到 Assistant Portal |
|---------------|------------------------|
| `/api/assistant-portal/admin/*` | `/api/admin/*` |
| `/api/assistant-portal/auth/*` | `/api/auth/*` |

认证: 请求头 `X-Admin-Key: {AssistantPortal:AdminApiKey}`

### 3.2 配置

```json
{
  "AssistantPortal": {
    "Address": "http://ruoyu-assistant-api:5021",
    "AdminApiKey": "{key}"
  }
}
```

### 3.3 环境变量

- `AssistantPortal__Url`
- `AssistantPortal__AdminApiKey`
