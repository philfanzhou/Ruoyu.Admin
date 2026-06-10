# 数据所有权

## 概述

Admin Portal 作为管理聚合层，仅拥有少量自有数据，大部分数据来自下游服务的实时查询。

## 自有数据实体

以下数据实体由 Admin Portal 服务**拥有**，存储在本地 PostgreSQL/SQLite 数据库中：

### OssAuditRecord

| 属性 | 值 |
|------|------|
| 数据库表 | `OssAuditRecords` |
| 生命周期 | 创建 → Pending → Resolved（删除）/ Ignored |
| 写入方 | Admin Portal（OssAuditWorker / OssAuditController） |
| 读取方 | Admin Portal（OssAuditController） |
| 说明 | OSS 僵尸对象审计记录，Resolved 时记录被删除 |

### OssAuditRun

| 属性 | 值 |
|------|------|
| 数据库表 | `OssAuditRuns` |
| 生命周期 | 创建 → Running → Completed / Failed |
| 写入方 | Admin Portal（OssAuditWorker） |
| 读取方 | Admin Portal（OssAuditController） |
| 说明 | 审计任务执行记录，用于并发控制和状态查询 |

## 引用的外部数据

以下数据实体**不属于** Admin Portal，通过下游服务实时查询获取，不在本地持久化：

### Student（来自 Student Service）

| 数据 | 获取方式 | 用途 |
|------|----------|------|
| 学生列表/详情 | gRPC: `ListStudents`, `GetStudent` | 学生管理 CRUD |
| 身份账户关联 | gRPC: `GetIdentityAccountsByStudentId`, `LinkIdentityAccountToStudent`, `UnlinkIdentityAccountFromStudent` | 账户关联管理 |
| 开放科目 | gRPC: `GetStudentOpenSubjects`, `SetStudentOpenSubjects` | 科目开放配置 |
| OSS 注册路径 | gRPC: `GetRegisteredOssPaths` | 审计时判断对象是否被引用 |
| OSS 对象列表 | gRPC: `ListOssObjects` | 审计时扫描 OSS 对象 |
| OSS 对象删除 | gRPC: `DeleteOssObject` | 审计 Resolve 时删除僵尸对象 |
| 上传记录 | gRPC: `GetAllUploadRecords`, `GetUploadRecord` 等 | 上传记录管理 |

### Mistake（来自 Mistake Service）

| 数据 | 获取方式 | 用途 |
|------|----------|------|
| 错题列表/详情 | gRPC: `GetMistakeItemList`, `GetMistakeItem`, `GetMistakeItemsByUpload` | 错题查询/管理 |
| 错题更新 | gRPC: `UpdateMistakeItem` | 错题信息修改 |
| 错题图片引用路径 | gRPC: `GetAllReferencedImagePaths` | 审计时判断对象是否被引用 |
| 提交错题上传 | gRPC: `SubmitMistakeUpload` | 上传记录分配为错题 |
| 完成上传审核 | gRPC: `CompleteUploadReview` | 遗留数据清理 |

### Identity Account（来自 Identity Service）

| 数据 | 获取方式 | 用途 |
|------|----------|------|
| 账户信息 | HTTP: `POST /api/gateway/users/batch` | 批量查询账户显示名 |
| 认证/授权 | HTTP Proxy: `/api/identity/*` | 代理认证请求 |

### Teacher Portal（来自 Teacher Portal Service）

| 数据 | 获取方式 | 用途 |
|------|----------|------|
| 教师端数据 | HTTP Proxy: `/api/teacher-portal/*` | 代理教师端管理请求 |

## 数据写入权限边界

### 严格禁止

Admin Portal **绝不**直接写入以下外部服务的数据存储：

- ❌ Student Service 的数据库
- ❌ Mistake Service 的数据库
- ❌ Identity Service 的数据库
- ❌ Teacher Portal 的数据库

所有对外部数据的修改必须通过对应的 gRPC/HTTP 接口进行，由下游服务负责数据一致性。

### 允许的间接写入

Admin Portal 通过下游服务 API 间接修改外部数据：

| 操作 | 目标服务 | API | 说明 |
|------|----------|-----|------|
| 创建/更新/删除学生 | Student Service | gRPC CRUD | 代理管理员操作 |
| 关联/解绑身份账户 | Student Service | gRPC Link/Unlink | 代理管理员操作 |
| 设置开放科目 | Student Service | gRPC SetOpenSubjects | 代理管理员操作 |
| 删除 OSS 僵尸对象 | Student Service | gRPC DeleteOssObject | 审计 Resolve 时调用 |
| 更新错题信息 | Mistake Service | gRPC UpdateMistakeItem | 代理管理员操作 |
| 提交错题上传 | Mistake Service | gRPC SubmitMistakeUpload | 上传记录分配 |
| 完成上传审核 | Mistake Service | gRPC CompleteUploadReview | 遗留数据清理 |
| 标记上传记录完成 | Student Service | gRPC MarkUploadRecordCompleted | 上传记录分配后 |
| 删除上传记录 | Student Service | gRPC DeleteUploadRecordAfterReview | 遗留数据清理 |

### 直接操作的外部资源

| 操作 | 目标 | 说明 |
|------|------|------|
| OSS 图片 Copy + Delete | OSS 存储 | 图片迁移时直接操作 OSS，然后通过 Mistake Service 更新路径引用 |
