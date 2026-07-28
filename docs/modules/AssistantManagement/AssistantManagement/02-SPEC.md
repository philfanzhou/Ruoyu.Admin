# 助教管理 — 功能规格

## 1. 助教权限管理

### 1.1 助教列表

- API: `GET /api/assistant-portal/admin/assistants`
- 返回所有助教账号，含 userId、phone、username、subjects、isActive、createdAt、updatedAt
- 表格列：User ID、手机号、用户名、备注、科目（Tag 列表）、创建时间、操作

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

## 2. Admin Portal 代理

### 2.1 AssistantPortalProxyMiddleware

路径映射规则（与 TeacherPortalProxyMiddleware 对称）：

| Admin 请求路径 | 转发到 Assistant Portal |
|---------------|------------------------|
| `/api/assistant-portal/admin/*` | `/api/admin/*` |
| `/api/assistant-portal/auth/*` | `/api/auth/*` |

认证: 透传调用方 `Authorization: Bearer`，下游 `[Authorize(Roles="admin")]` 校验；不再使用静态 `X-Admin-Key`

### 2.2 配置

```json
{
  "AssistantPortal": {
    "Address": "http://ruoyu-assistant-api:5021"
  }
}
```

### 2.3 环境变量

- `AssistantPortal__Url`

## 3. 已移除功能

- **关联账户管理（PUT /api/assistant-portal/admin/assistants/{userId}/user-id）**：原设计在助教管理页提供"关联/更换 Identity 账户"功能。该功能未被要求且引入了额外的 userId 变更复杂度，已于 2026-07-27 删除前后端实现。助教的 `userId` 在 `grant-assistant` 授予时绑定，不再支持事后更换。
- **助教-学生关联（GET/POST/DELETE /api/admin/assistants/{userId}/students[/{studentId}]）**：原设计在助教管理页提供"关联学生"列展示学生数量并提供"管理"按钮。该功能与学生管理页"关联账户"对话框语义重复，且数量加载采用 N+1 查询存在性能问题，已于 2026-07-27 删除前后端实现。关联管理统一在学生管理页"关联账户"对话框完成，详见 [StudentManagement/AccountLinking](../../StudentManagement/AccountLinking/01-FEATURE.md)。
