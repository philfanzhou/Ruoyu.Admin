# HumanReview — admin_portal

> 代码审查发现项，按优先级排列。已解决项必须移除。

## P0 — 必须修复

| ID | 问题 | 位置 | 状态 |
|----|------|------|------|
| HR-01 | 所有 API 端点无认证/授权 | 多个 Controller | 待修复 |
| HR-02 | appsettings.Testing.json 泄露真实 AppId/AppSecret | appsettings.Testing.json | 待修复 |
| HR-03 | OssUploadRecordController、MistakeController N+1 查询 | 多个 Controller | 待修复 |
| HR-04 | ImageController 路径遍历 | ImageController.cs | 待修复 |
| HR-05 | OssAuditController fire-and-forget Task.Run | OssAuditController.cs | 待修复 |

## P1 — 应尽快修复

| ID | 问题 | 位置 | 状态 |
|----|------|------|------|
| HR-06 | CORS 未配置来源时 AllowAnyOrigin | Program.cs | 待修复 |
| HR-07 | TeacherPortalProxyMiddleware 丢弃所有请求头 | TeacherPortalProxyMiddleware.cs | 待修复 |
| HR-08 | 多个 Controller 未捕获 RpcException | 多个 Controller | 待修复 |
| HR-09 | 前端无认证流程 | 前端代码 | 待修复 |
| HR-10 | 多个 API 客户端实例无共享拦截器 | 多处 | 待修复 |
| HR-11 | AssignUploadRecord 并发与幂等性需集成测试验证 | OssUploadRecordController.cs | 待修复 |
| HR-12 | 前端测试框架缺失 | 前端项目 | 待修复 |
