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
| X-Admin-Key 头注入 | 请求头包含配置的 AdminApiKey |
| POST 请求体转发 | 请求体正确转发 |

## 2. AssistantDbService 学生关联测试

| 方法 | 场景 | 预期 |
|------|------|------|
| GetStudentsByUserId | 有关联 | 返回 studentId 列表 |
| GetStudentsByUserId | 无关联 | 返回空列表 |
| SetStudentsByUserId | 替换式设置 | 旧关联清除，新关联写入 |
| AddStudentByUserId | 新增 | 关联成功 |
| AddStudentByUserId | 重复 | 幂等，不报错 |
| RemoveStudentByUserId | 存在 | 删除成功 |
| RemoveStudentByUserId | 不存在 | 返回 false |
| RemoveAllStudentsByUserId | 撤销时 | 清除所有关联 |

## 3. AdminController 学生关联端点测试

| 端点 | 场景 | 预期 |
|------|------|------|
| GET /assistants/{userId}/students | 正常 | 200 + 学生列表 |
| POST /assistants/{userId}/students | 正常 | 200 + 成功 |
| POST /assistants/{userId}/students/{studentId} | 正常 | 200 |
| DELETE /assistants/{userId}/students/{studentId} | 正常 | 200 |
| RevokeAssistantRole | 撤销后 | 学生关联被清理 |
