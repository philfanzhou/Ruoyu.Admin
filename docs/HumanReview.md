# HumanReview — admin\_portal

> 代码审查发现项，按优先级排列。已解决项必须移除。

## P0 — 必须修复

| ID | 问题 | 方案 A | 方案 B | 方案 C | 批复 |
|----|------|--------|--------|--------|------|
| HR-01 | 所有 API 端点无认证/授权，任何请求均可访问 | (推荐) 添加 JWT 认证中间件 + [Authorize] 属性，对接 Identity 服务 | 使用 API Key 认证，在中间件层统一校验 | 暂不处理，依赖内网隔离 |
| HR-02 | appsettings.Testing.json 泄露真实 AppId/AppSecret | (推荐) 删除该文件，测试配置改用环境变量或 user secrets | 保留但加入 .gitignore，仅本地使用 | 暂不处理，标记为测试专用 |
| HR-03 | OssAuditController、MistakeController N+1 查询 | (推荐) 批量预加载关联数据，用 EF Core Include/Select N+1 改为批量查询 | 在 gRPC 层新增批量查询接口，一次返回所有关联数据 | 暂不处理，数据量小时影响有限 |
| HR-05 | OssAuditController fire-and-forget Task.Run，异常丢失 | (推荐) 改用 IHostedService + Channel 队列，异常可追踪可重试 | 改用 Hangfire/后台任务框架 | 暂不处理，当前审计频率低 |

## P1 — 应尽快修复

| ID | 问题 | 方案 A | 方案 B | 方案 C | 批复 |
|----|------|--------|--------|--------|------|
| HR-06 | CORS 未配置来源时 AllowAnyOrigin | (推荐) 未配置时拒绝所有跨域请求，强制显式配置 | 未配置时只允许 localhost 开发来源 | 暂不处理，当前仅内网使用 |
| HR-07 | TeacherPortalProxyMiddleware 丢弃所有请求头（foreach 未赋值） | (推荐) 修复循环逻辑，将非 Content- 头转发到代理请求 | 使用 HeaderUtilities 过滤已知跳过头，其余全部转发 | 暂不处理，当前功能可用 |
| HR-08 | 多个 Controller 未捕获 RpcException，返回 500 | (推荐) 添加全局 RpcException 中间件，统一映射 gRPC 状态码到 HTTP | 在每个 Controller 方法加 try-catch RpcException | 暂不处理，前端已有错误处理 |
| HR-09 | 前端无认证流程 | (推荐) 实现 OAuth2 PKCE 流程，对接 Identity 服务 | 使用 API Key + 简单登录页 | 暂不处理，依赖内网隔离 |
| HR-10 | 多个 API 客户端实例无共享拦截器 | (推荐) 统一注册 gRPC 拦截器（日志、重试、认证） | 使用 DelegatingHandler 在 HttpClient 层统一处理 | 暂不处理，当前调用频率低 |
| HR-11 | AssignUploadRecord 并发与幂等性需集成测试验证 | (推荐) 添加数据库唯一约束 + 乐观并发控制，编写集成测试 | 使用分布式锁（Redis）保证幂等 | 暂不处理，当前并发量低 |
| HR-12 | 前端测试框架缺失 | (推荐) 引入 Vitest + Testing Library，覆盖核心组件 | 引入 Playwright E2E 测试 | 暂不处理 |

## P2 — 变更待审查

| ID | 问题 | 方案 A | 方案 B | 方案 C | 批复 |
|----|------|--------|--------|--------|------|
| HR-15 | Admin Portal 自实现路径聚合（分页遍历全量数据），大数据量下性能和超时风险 | (推荐) 在 Student/Mistake gRPC 新增专用批量路径查询接口，服务端聚合 | 增加分页并行请求 + 超时控制 + 缓存 | 暂不处理，当前数据量可接受 |
| HR-16 | OssUploadRecordController 改用通用 gRPC 后，原 Admin 专用接口的权限校验逻辑缺失 | (推荐) 在 gRPC 层添加调用方身份校验（Admin 角色检查） | 在 Admin Portal Controller 层添加权限校验中间件 | 暂不处理，当前仅内网访问 |
