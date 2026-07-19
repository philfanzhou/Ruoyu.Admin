# HumanReview — admin\_portal

> 代码审查发现项，按优先级排列。已解决项必须移除。

## P0 — 必须修复

### HR-03: OssAuditController、MistakeController N+1 查询

- **问题**：多个 Controller 存在循环内逐条查询关联数据的 N+1 模式
- **A** (推荐)：批量预加载关联数据，用 EF Core Include/Select 改为批量查询
- **B**：在 gRPC 层新增批量查询接口，一次返回所有关联数据
- **C**：暂不处理，数据量小时影响有限
- **批复**：

### HR-05: OssAuditController fire-and-forget Task.Run

- **问题**：OssAuditController.TriggerAudit 使用 `_ = Task.Run(...)` 启动审计，异常丢失且无法追踪
- **A** (推荐)：改用 IHostedService + Channel 队列，异常可追踪可重试
- **B**：改用 Hangfire/后台任务框架
- **C**：暂不处理，当前审计频率低
- **批复**：

## P1 — 应尽快修复

### HR-06: CORS 未配置来源时 AllowAnyOrigin

- **问题**：Program.cs 中当 AllowedOrigins 为空时使用 AllowAnyOrigin，存在跨域安全风险
- **A** (推荐)：未配置时拒绝所有跨域请求，强制显式配置
- **B**：未配置时只允许 localhost 开发来源
- **C**：暂不处理，当前仅内网使用
- **批复**：

### HR-07: TeacherPortalProxyMiddleware 丢弃所有请求头 — 已修复

- **问题**：TeacherPortalProxyMiddleware.cs:77-81 的 foreach 循环未将请求头赋值到 requestMessage，导致代理请求丢失所有原始头
- **修复**：两个代理中间件（Teacher/Assistant）的 header 转发循环已修复，跳过 Content-* 和 Host 头，其余头用 TryAddWithoutValidation 转发

### HR-08: 多个 Controller 未捕获 RpcException

- **问题**：多个 Controller 直接调用 gRPC 客户端但未处理 RpcException，异常时返回 500
- **A** (推荐)：添加全局 RpcException 中间件，统一映射 gRPC 状态码到 HTTP
- **B**：在每个 Controller 方法加 try-catch RpcException
- **C**：暂不处理，前端已有错误处理
- **批复**：

### HR-09: 前端无认证流程

- **问题**：前端代码未实现登录/认证流程，无法保护管理端功能
- **A** (推荐)：实现 OAuth2 PKCE 流程，对接 Identity 服务
- **B**：使用 API Key + 简单登录页
- **C**：暂不处理，依赖内网隔离
- **批复**：

### HR-10: 多个 API 客户端实例无共享拦截器

- **问题**：gRPC 客户端注册时未配置共享拦截器（日志、重试、认证），各客户端独立运行
- **A** (推荐)：统一注册 gRPC 拦截器（日志、重试、认证）
- **B**：使用 DelegatingHandler 在 HttpClient 层统一处理
- **C**：暂不处理，当前调用频率低
- **批复**：

### HR-11: AssignUploadRecord 并发与幂等性

- **问题**：OssUploadRecordController.AssignUploadRecord 无并发控制，重复请求可能导致数据不一致
- **A** (推荐)：添加数据库唯一约束 + 乐观并发控制，编写集成测试
- **B**：使用分布式锁（Redis）保证幂等
- **C**：暂不处理，当前并发量低
- **批复**：

### HR-12: 前端测试框架缺失

- **问题**：前端项目无任何测试框架，无法保证 UI 代码质量
- **A** (推荐)：引入 Vitest + Testing Library，覆盖核心组件
- **B**：引入 Playwright E2E 测试
- **C**：暂不处理
- **批复**：

## P2 — 变更待审查

### HR-15: Admin Portal 自实现路径聚合性能风险

- **问题**：OssAuditWorker 通过分页遍历全量数据在本地聚合路径，大数据量下存在性能和超时风险
- **A** (推荐)：在 Student/Mistake gRPC 新增专用批量路径查询接口，服务端聚合
- **B**：增加分页并行请求 + 超时控制 + 缓存
- **C**：暂不处理，当前数据量可接受
- **批复**：
