# HumanReview — admin\_portal

> 代码审查发现项，按优先级排列。已解决项必须移除。

## P0 — 必须修复

| ID    | 问题                                                 | 位置                       | 状态  | 批复       |
| ----- | -------------------------------------------------- | ------------------------ | --- | -------- |
| HR-01 | 所有 API 端点无认证/授权                                    | 多个 Controller            | 待修复 | <br />   |
| HR-02 | appsettings.Testing.json 泄露真实 AppId/AppSecret      | appsettings.Testing.json | 待修复 | 删除这个测试配置 |
| HR-03 | OssAuditController、MistakeController N+1 查询 | 多个 Controller            | 待修复 | 需要更多说明   |
| HR-05 | OssAuditController fire-and-forget Task.Run        | OssAuditController.cs    | 待修复 | 需要更多说明   |

## P1 — 应尽快修复

| ID    | 问题                                   | 位置                              | 状态  | 批复     |
| ----- | ------------------------------------ | ------------------------------- | --- | ------ |
| HR-06 | CORS 未配置来源时 AllowAnyOrigin           | Program.cs                      | 待修复 | <br /> |
| HR-07 | TeacherPortalProxyMiddleware 丢弃所有请求头 | TeacherPortalProxyMiddleware.cs | 待修复 | <br /> |
| HR-08 | 多个 Controller 未捕获 RpcException       | 多个 Controller                   | 待修复 | <br /> |
| HR-09 | 前端无认证流程                              | 前端代码                            | 待修复 | <br /> |
| HR-10 | 多个 API 客户端实例无共享拦截器                   | 多处                              | 待修复 | <br /> |
| HR-11 | AssignUploadRecord 并发与幂等性需集成测试验证     | OssUploadRecordController.cs    | 待修复 | <br /> |
| HR-12 | 前端测试框架缺失                             | 前端项目                            | 待修复 | <br /> |

## P2 — 变更待审查

| ID    | 问题                                                                                    | 位置                        | 状态  | 批复     |
| ----- | ------------------------------------------------------------------------------------- | ------------------------- | --- | ------ |
| HR-15 | Admin Portal 自实现路径聚合：大量数据分页遍历的性能和超时风险                                                 | OssAuditWorker            | 待审查 | <br /> |
| HR-16 | OssUploadRecordController 改用通用 gRPC 后，原 Admin 专用接口的权限校验逻辑如何替代？                        | OssUploadRecordController | 待审查 | <br /> |
