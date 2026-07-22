# 需求文档

## 服务级需求

Admin Portal 是 Ruoyu.Study 平台的管理后台服务，为管理员提供统一的学生管理、错题管理、OSS 审计和上传记录管理等操作入口。

### 功能需求

| # | 需求 | 优先级 | 状态 | 详细模块 |
|---|------|--------|------|----------|
| FR-1 | 学生 CRUD 管理 | 高 | 已实现 | StudentsController |
| FR-2 | 学生与身份账户关联/解绑 | 高 | 已实现 | StudentsController |
| FR-3 | 学生开放科目管理 | 中 | 已实现 | StudentsController |
| FR-4 | 错题记录查询与更新 | 高 | 已实现 | MistakeController |
| FR-5 | 错题图片迁移（uploads → mistakes） | 中 | 已实现 | MistakeController |
| FR-6 | OSS 僵尸对象审计（定时 + 手动） | 高 | 已实现 | OssAuditController + OssAuditWorker |
| FR-7 | OSS 审计记录管理（Resolve/Ignore/Batch） | 高 | 已实现 | OssAuditController |
| FR-8 | 上传记录查看与分配 | 高 | 已实现 | OssUploadRecordController |
| FR-9 | 上传记录图片操作（旋转/移除） | 中 | 已实现 | OssUploadRecordController |
| FR-10 | 遗留数据检测与清理 | 中 | 已实现 | OssUploadRecordController |
| FR-11 | 上传记录 VL 分析 | 低 | 已实现 | OssUploadRecordController |
| FR-12 | 身份服务反向代理 | 高 | 已实现 | IdentityProxyMiddleware |
| FR-13 | 教师门户反向代理 | 中 | 已实现 | TeacherPortalProxyMiddleware |
| FR-14 | 枚举选项聚合查询 | 低 | 已实现 | EnumOptionsController |
| FR-15 | 图片代理（从 OSS） | 中 | 已实现 | ImageController / OssUploadRecordController |
| FR-16 | 身份账户批量查询 | 中 | 已实现 | IdentityAccountsController |

### 非功能需求

| # | 需求 | 说明 |
|------|------|------|
| NFR-1 | 审计并发安全 | 同一时间只允许一个审计运行，通过数据库记录实现互斥 |
| NFR-2 | 审计幂等性 | 审计 DDL 使用 `IF NOT EXISTS`，审计记录通过 `ObjectPath` UNIQUE 约束避免重复 |
| NFR-3 | 下游容错 | Student 服务不可用时审计中止；Mistake 服务不可用时审计继续但标记警告 |
| NFR-4 | 代理容错 | Identity/Teacher Portal 不可达时返回 502，不崩溃 |
| NFR-5 | 图片路径安全 | ImageController 检查路径中是否包含 `..` 或以 `/`、`\` 开头，防止路径遍历攻击 |
| NFR-6 | CORS 配置 | 生产环境通过 `AdminWeb:AllowedOrigins` 限制跨域来源 |
| NFR-7 | 数据库 | 支持 PostgreSQL，通过 Npgsql EF Core 提供程序 |
| NFR-8 | SPA 集成 | 支持前后端集成部署（wwwroot）和独立部署两种模式 |

### 约束

| # | 约束 | 说明 |
|------|------|------|
| C-1 | 不持有核心业务数据 | 学生、错题、身份等数据仅通过下游服务实时查询 |
| C-2 | 不直接写入外部数据库 | 所有外部数据修改通过 gRPC/HTTP 接口进行 |
| C-3 | 审计仅发现不自动删除 | 审计只标记僵尸对象，需管理员手动 Resolve 才会删除 |
| C-4 | Resolve 前必须验证引用 | 删除 OSS 对象前必须再次验证 Student/Mistake 服务是否仍引用该路径 |
