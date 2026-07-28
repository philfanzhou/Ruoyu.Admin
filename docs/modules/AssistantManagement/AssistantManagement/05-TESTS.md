# 助教管理 — 测试规格

## 1. AssistantPortalProxyMiddleware 测试

与 TeacherPortalProxyMiddleware 测试对称:

| 场景 | 预期 |
|------|------|
| 非 /api/assistant-portal 请求 | 直接 next，不代理 |
| /api/assistant-portal/admin/assistants | 转发到 {Address}/api/admin/assistants |
| /api/assistant-portal/auth/check-assistant | 转发到 {Address}/api/auth/check-assistant |
| Assistant Portal 未配置 | 返回 503 |
| Assistant Portal 不可达 | 返回 502 |
| Authorization Bearer 透传 | 透传调用方的 Authorization 头，下游校验 role:admin |
| POST 请求体转发 | 请求体正确转发 |

## 2. AssistantDbService 测试

| 方法 | 场景 | 预期 |
|------|------|------|
| AddAssistantByUserId | 新增 | 助教创建成功，含科目 |
| GetByUserId | 存在 | 返回助教实体，含科目列表 |
| GetByUserId | 不存在 | 返回 null |
| SetSubjectsByUserId | 整体设置 | 旧科目清除，新科目写入 |
| AddSubjectByUserId | 新增 | 科目关联成功 |
| AddSubjectByUserId | 重复 | 幂等，不报错 |
| RemoveSubjectByUserId | 存在 | 删除成功 |
| RemoveSubjectByUserId | 不存在 | 返回 null |
| RemoveAssistantByUserId | 撤销 | 助教及其科目被删除 |

## 3. AdminController 端点测试

| 端点 | 场景 | 预期 |
|------|------|------|
| GET /assistants | 正常 | 200 + 助教列表 |
| POST /identity-users/{userId}/grant-assistant | 正常 | 200 + 成功 |
| POST /identity-users/{userId}/revoke-assistant | 正常 | 200 + 成功 |
| GET /assistants/{userId}/subjects | 正常 | 200 + 科目列表 |
| PUT /assistants/{userId}/subjects | 正常 | 200 + 成功 |
| POST /assistants/{userId}/subjects/{subject} | 正常 | 200 |
| DELETE /assistants/{userId}/subjects/{subject} | 正常 | 200 |
| GET /available-subjects | 正常 | 200 + 科目选项 |

## 4. 已移除测试

以下测试已随功能一起删除（2026-07-27）：

- `GetAssistantStudents_*` — 随 `GET /api/admin/assistants/{userId}/students` 端点删除
- `AddAssistantStudent_*` — 随 `POST /api/admin/assistants/{userId}/students/{studentId}` 端点删除
- `RemoveAssistantStudent_*` — 随 `DELETE /api/admin/assistants/{userId}/students/{studentId}` 端点删除
