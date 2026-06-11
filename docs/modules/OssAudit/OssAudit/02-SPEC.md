# OssAudit — 详细需求规格 (SPEC)

## 功能概述和用户故事

**核心用户故事**：作为系统管理员，我希望能够自动检测并清理 OSS 存储中不再被业务系统引用的孤立对象（僵尸文件），以便释放存储空间、降低存储成本，并保持存储与业务数据的一致性。

**补充约束**：
- 审计任务必须互斥执行，同一时间只允许一个审计运行
- 清理操作必须验证对象确实未被引用后才可执行，防止误删
- 定时审计仅在 PostgreSQL 环境下启用，SQLite 环境不注册后台服务[待确认：是否需要在 SQLite 环境提供替代方案]
- Mistake 服务的引用查询为可选依赖，不可用时审计仍可继续（仅基于 Student 注册路径判断）

## 功能要求清单

- [ ] FR-01：系统每日定时自动执行 OSS 审计扫描，检测孤立对象
- [ ] FR-02：管理员可手动触发一次 OSS 审计扫描
- [ ] FR-03：审计扫描结果以分页列表展示，支持按状态和桶名筛选
- [ ] FR-04：管理员可查看审计运行状态（是否运行中、上次完成/失败时间、待处理数量）
- [ ] FR-05：管理员可对 Pending 状态的僵尸记录执行"清理"操作，系统验证无引用后删除 OSS 对象并移除记录
- [ ] FR-06：管理员可对僵尸记录执行"忽略"操作，附加备注说明
- [ ] FR-07：管理员可批量清理多条僵尸记录
- [ ] FR-08：同一时间只允许一个审计运行，重复触发应被拒绝
- [ ] FR-09：清理 Pending 记录时必须验证对象未被 Student 和 Mistake 服务引用，若仍被引用则拒绝清理
- [ ] FR-10：清理 Resolved 状态的记录时跳过引用校验[推断：允许对已标记为 Resolved 的记录执行清理]

## 详细的验收标准

### AC-FR-01：每日定时自动审计
- **Given** 系统配置了 `OssAudit:ScheduledHour` 和 `OssAudit:ScheduledMinute`（默认 2:00 AM）
- **When** 到达调度时间
- **Then** 系统自动执行 `RunAuditAsync("scheduled")`，创建 `OssAuditRun` 记录（TriggerType=scheduled），扫描所有桶并生成僵尸记录

### AC-FR-02：手动触发审计
- **Given** 当前没有审计任务正在运行
- **When** 管理员调用 `POST /api/admin/oss-audit/trigger`
- **Then** 系统异步启动审计任务（`Task.Run`），立即返回成功响应，创建 `OssAuditRun` 记录（TriggerType=manual）

- **Given** 当前已有审计任务正在运行
- **When** 管理员调用 `POST /api/admin/oss-audit/trigger`
- **Then** 返回冲突错误，提示审计已在运行中

### AC-FR-03：审计记录分页查询
- **Given** 数据库中存在审计记录
- **When** 管理员调用 `GET /api/admin/oss-audit/records?page=1&pageSize=20&status=0&bucket=uploads`
- **Then** 返回对应筛选条件下的分页记录列表，包含 ObjectPath、Bucket、Size、LastModified、Status、CreatedAt、ResolvedAt、Note

### AC-FR-04：审计运行状态查询
- **Given** 系统已运行过审计
- **When** 管理员调用 `GET /api/admin/oss-audit/status`
- **Then** 返回 `isRunning`（布尔）、`lastCompleted`（上次完成时间）、`lastFailed`（上次失败时间）、`pendingCount`（待处理记录数）

### AC-FR-05：清理单条 Pending 僵尸记录
- **Given** 存在一条 Status=Pending 的 OssAuditRecord，且该 ObjectPath 未被 Student 服务注册、未被 Mistake 服务引用
- **When** 管理员调用 `POST /api/admin/oss-audit/records/{id}/resolve`
- **Then** 系统调用 `Student.DeleteOssObject` 删除 OSS 对象及指纹，从数据库移除该 OssAuditRecord

- **Given** 存在一条 Status=Pending 的 OssAuditRecord，且该 ObjectPath 仍被 Student 或 Mistake 服务引用
- **When** 管理员调用 `POST /api/admin/oss-audit/records/{id}/resolve`
- **Then** 拒绝清理操作，返回错误提示对象仍被引用[推断]

### AC-FR-06：忽略僵尸记录
- **Given** 存在一条 OssAuditRecord
- **When** 管理员调用 `POST /api/admin/oss-audit/records/{id}/ignore`，body 包含 `note`
- **Then** 记录的 Status 设为 2（Ignored），ResolvedAt 设为当前时间，Note 设为传入的备注

### AC-FR-07：批量清理僵尸记录
- **Given** 存在多条 OssAuditRecord
- **When** 管理员调用 `POST /api/admin/oss-audit/records/batch-resolve`，body 包含 `ids` 列表
- **Then** 系统一次性获取引用路径集合，逐条处理记录（复用引用数据避免重复查询），对每条记录执行与 FR-05 相同的验证和清理逻辑

### AC-FR-08：审计互斥
- **Given** 一个审计运行正在进行（OssAuditRun.Status=Running）
- **When** 尝试触发新的审计（手动或定时）
- **Then** 新的审计不被启动，手动触发返回冲突错误，定时触发跳过本次[推断]

### AC-FR-09：清理前引用验证
- **Given** 一条 Pending 记录的 ObjectPath 在 Student 服务的 `GetRegisteredOssPaths` 结果中存在
- **When** 尝试清理该记录
- **Then** 操作被拒绝，返回对象仍被 Student 服务注册的错误

- **Given** 一条 Pending 记录的 ObjectPath 在 Mistake 服务的 `GetAllReferencedImagePaths` 结果中存在
- **When** 尝试清理该记录
- **Then** 操作被拒绝，返回对象仍被 Mistake 服务引用的错误

### AC-FR-10：Resolved 记录清理跳过校验
- **Given** 存在一条 Status=Resolved 的 OssAuditRecord
- **When** 管理员调用 `POST /api/admin/oss-audit/records/{id}/resolve`
- **Then** 跳过引用验证，直接执行删除操作[推断]

## 非功能需求

### 性能
- 审计扫描应支持分页列举 OSS 对象（`ListOssObjectsPaged`），避免一次性加载大量对象导致内存溢出
- 批量清理应复用引用路径查询结果，避免逐条重复调用 gRPC 服务
- 审计扫描不应阻塞 API 请求，手动触发使用 `Task.Run` 异步执行

### 安全
- 清理接口仅限管理员角色访问（路由前缀 `/api/admin/`）
- 清理前必须验证引用关系，防止误删业务数据
- ObjectPath 设为 UNIQUE 约束，防止重复审计记录

### 可靠性
- 审计运行使用 `OssAuditRun` 记录作为互斥锁，防止并发执行
- Student 服务不可用时审计应中止并标记 Failed，不产生不完整的审计结果
- Mistake 服务不可用时审计应继续执行（仅警告），因为其引用数据为可选
- 审计 Worker 启动后延迟 2 分钟，避免应用启动时资源竞争

### 可观测性
- `OssAuditRun` 表记录每次审计的开始时间、完成时间、状态、新增僵尸数量、触发类型、错误信息
- `OssAuditRecord` 表记录僵尸对象的完整元数据（路径、桶、大小、最后修改时间、状态、备注）
- 应通过 ILogger 记录关键操作和异常

### 幂等性
- 对同一 ObjectPath 重复审计不应创建重复记录（UNIQUE 约束保证）
- 忽略操作幂等：重复设置 Status=Ignored 无副作用
- 清理操作需考虑 OSS 删除的幂等性（对象已不存在时应视为成功）[待确认]

## 测试策略

### 覆盖率目标
- OssAuditController 所有端点的单元测试覆盖
- OssAuditWorker.RunAuditAsync 核心逻辑的单元测试覆盖
- 目标行覆盖率 ≥ 80%

### 必须测试的错误路径
1. 手动触发审计时已有审计运行中 → 返回冲突错误
2. Student gRPC 服务不可用时执行审计 → 审计中止，OssAuditRun 标记 Failed
3. Mistake gRPC 服务不可用时执行审计 → 审计继续，仅记录警告
4. 清理 Pending 记录时对象仍被 Student 引用 → 拒绝清理
5. 清理 Pending 记录时对象仍被 Mistake 引用 → 拒绝清理
6. 审计扫描过程中异常 → OssAuditRun 标记 Failed，ErrorMessage 记录异常信息
7. 批量清理中部分记录验证失败 → [待确认：是整体回滚还是部分成功部分失败]

### 测试环境要求
- 需要 mock Student gRPC 服务和 Mistake gRPC 服务
- 需要 mock OSS SDK 的 ListObjects 和 DeleteObject 操作
- 需要使用内存数据库（InMemory SQLite 或 InMemory EF Core Provider）模拟 AuditDbContext
- 当前无任何测试代码，需从零搭建测试基础设施

## 并发控制机制详解

OssAuditWorker 使用数据库记录作为互斥锁，确保同一时间只有一个审计在运行。具体机制：

1. **创建运行记录（加锁）**：RunAuditAsync 首先创建一条 OssAuditRun 记录（Status=0/Running），通过 `dbContext.SaveChangesAsync` 写入数据库
2. **捕获并发冲突**：如果另一个审计同时创建记录，`DbUpdateException` 会被捕获，当前审计直接返回（代码第 74-79 行）
3. **双重检查**：写入成功后，再次查询是否存在其他 Running 状态的记录（Id != 当前记录），如果存在则删除当前记录并返回（代码第 82-90 行）
4. **失败时标记**：审计过程中任何异常都会将 OssAuditRun.Status 设为 2（Failed）并记录 ErrorMessage

### SQLite 下的行为差异

- PostgreSQL：`SaveChangesAsync` 在并发写入时会正确抛出 `DbUpdateException`，互斥锁可靠
- SQLite：单写入者模式下并发写入行为不同，`DbUpdateException` 可能不被抛出，双重检查成为主要保护机制
- **当前代码**：OssAuditWorker 仅在 PostgreSQL 模式下注册为 HostedService（Program.cs 中有条件判断），SQLite 环境下不会自动运行定时审计

### 手动触发时的并发保护

OssAuditController.TriggerAudit 使用 `Task.Run(_auditWorker.RunAuditAsync("manual"))`，与定时审计共享同一互斥锁机制。如果定时审计正在运行，手动触发会因为无法创建 OssAuditRun 记录而自动跳过。
