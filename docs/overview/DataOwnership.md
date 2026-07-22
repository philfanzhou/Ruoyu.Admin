# 数据所有权

> **Phase 4 计划变更（尚未实施）**：本文档已更新为 Phase 4 目标状态。主要变更：
> - Admin Portal 现拥有 OSS 审计逻辑（路径聚合、僵尸检测），不再依赖下游服务专用接口
> - `StudentManagementGrpcService` 的 10 个 Admin 专用接口被移除，改为通用 gRPC + 直接 OSS 操作
> - `MistakeGrpcService.GetAllReferencedImagePaths` 被移除，Admin Portal 通过通用 gRPC 分页遍历后自行聚合

## 概述

Admin Portal 作为管理聚合层，仅拥有少量自有数据，大部分数据来自下游服务的实时查询。

## 自有数据实体

以下数据实体由 Admin Portal 服务**拥有**，存储在本地 PostgreSQL 数据库中：

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
| OSS 注册路径 | Admin Portal 自行聚合：调用通用 gRPC `GetAllUploadRecords` 分页获取后提取图片路径 | 审计时判断对象是否被引用 |
| OSS 对象列表 | 直接调用 `IOssService.ListObjectsAsync` | 审计时扫描 OSS 对象 |
| OSS 对象删除 | 直接调用 `IOssService.DeleteAsync` + 通用 gRPC 清理指纹 | 审计 Resolve 时删除僵尸对象 |
| OSS 对象移动 | 直接调用 `IOssService.CopyObjectAsync` + `DeleteAsync` | 迁移辅助 |
| 上传记录 | gRPC: `GetAllUploadRecords`, `GetUploadRecord` 等 | 上传记录管理 |
| 图片预签名 URL | gRPC: `GetPresignedUrl` | 图片查看 |
| 图片迁移 | gRPC: `MigrateImagesToMistake` | 图片迁移（uploads→mistakes） |

> **Phase 4 变更**：`StudentManagementGrpcService` 整体移除。原 Admin 专用接口（`GetRegisteredOssPaths`、`ListOssObjects`、`DeleteOssObject`、`MoveOssObject`）改为 Admin Portal 自行实现（直接 IOssService + 通用 gRPC 聚合）。原 `GetAllUploadRecords`、`AdminDeleteUploadRecord` 等接口改为通用 gRPC 调用。

### Mistake（来自 Mistake Service）

| 数据 | 获取方式 | 用途 |
|------|----------|------|
| 错题列表/详情 | gRPC: `GetMistakeItemList`, `GetMistakeItem`, `GetMistakeItemsByUpload` | 错题查询/管理 |
| 错题更新 | gRPC: `UpdateMistakeItem` | 错题信息修改 |
| 错题图片引用路径 | Admin Portal 自行聚合：调用通用 gRPC `GetMistakeItemList` 分页遍历后提取图片路径 | 审计时判断对象是否被引用 |
| 提交错题上传 | gRPC: `SubmitMistakeUpload` | 上传记录分配为错题 |
| 完成上传审核 | gRPC: `CompleteUploadReview` | 遗留数据清理 |
| 图片预签名 URL | gRPC: `GetPresignedUrl` | 图片查看 |

> **Phase 4 变更**：`MistakeGrpcService.GetAllReferencedImagePaths` 被移除。Admin Portal 通过通用 gRPC 分页遍历错题条目后自行聚合图片路径。

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
| 删除 OSS 僵尸对象 | Admin Portal 直接操作 | `IOssService.DeleteAsync`（自动清理关联缩略图） + 通用 gRPC 清理指纹 | 审计 Resolve 时调用 |
| 移动 OSS 对象 | Admin Portal 直接操作 | `IOssService.CopyObjectAsync` + `DeleteAsync` | 迁移辅助 |
| 更新错题信息 | Mistake Service | gRPC UpdateMistakeItem | 代理管理员操作 |
| 提交错题上传 | Mistake Service | gRPC SubmitMistakeUpload | 上传记录分配 |
| 完成上传审核 | Mistake Service | gRPC CompleteUploadReview | 遗留数据清理 |
| 标记上传记录完成 | Student Service | gRPC MarkUploadRecordCompleted | 上传记录分配后 |
| 删除上传记录 | Student Service | gRPC DeleteUploadRecordAfterReview | 遗留数据清理 |
| 图片迁移 | Student Service | gRPC MigrateImagesToMistake | 图片迁移（uploads→mistakes） |

> **Phase 4 变更**：原通过 `StudentManagementGrpcService.DeleteOssObject` 删除僵尸对象，改为 Admin Portal 直接调用 `IOssService.DeleteAsync` + 通用 gRPC 清理指纹。原通过 `StudentManagementGrpcService.MoveOssObject` 移动对象，改为 Admin Portal 直接操作 OSS。图片迁移从直接 OSS Copy+Delete 改为调用 Student gRPC `MigrateImagesToMistake`。

### 直接操作的外部资源

| 操作 | 目标 | 说明 |
|------|------|------|
| OSS ListObjects | OSS 存储 | 审计浏览：扫描 OSS 对象列表 |
| OSS DeleteObject | OSS 存储 | 僵尸文件清理（`DeleteAsync` 自动清理关联缩略图） |
| OSS CopyObject + Delete | OSS 存储 | 迁移辅助（MoveOssObject 场景） |

> **Phase 4 变更**：Admin Portal 直接操作 OSS 的范围从"仅图片迁移"扩展为"审计浏览 + 僵尸清理 + 迁移辅助"，但凭证权限降级为只读 + 有限写（Delete 仅限僵尸清理，CopyObject 仅限迁移辅助）。图片迁移不再直接操作 OSS，改为通过 Student gRPC `MigrateImagesToMistake`。
