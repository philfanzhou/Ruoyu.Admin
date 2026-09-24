# OssAudit 测试用例

## 当前状态：部分实现

### OssAuditController 测试（**未实现**）

下表是目标用例清单。截至本次迁出，`backend/Tests/Controllers/` 下**没有** `OssAuditControllerTests`，这些方法在源 monorepo 中也不存在——原文档标注的「已实现」与代码不符，现予更正。`ResolveRecord` / `BatchResolve` 的删除前复核目前**没有任何测试覆盖**，而它是唯一阻止误删的运行时保护，属于优先补齐项。

| 目标测试方法 | 验证内容 |
| --- | --- |
| `GetRecords_ReturnsPaginatedRecords_WithStatusAndBucketCounts` | 获取审计记录列表，返回分页结果和状态/桶计数 |
| `GetRecords_FilterByStatus_ReturnsOnlyMatchingRecords` | 按状态筛选审计记录 |
| `GetRecords_FilterByBucket_ReturnsOnlyMatchingRecords` | 按桶筛选审计记录 |
| `TriggerAudit_NoRunningAudit_ReturnsOk` | 成功触发审计 |
| `TriggerAudit_AlreadyRunning_ReturnsBadRequest` | 审计已在运行时返回 400 |
| `GetStatus_ReturnsIsRunning_LastCompleted_LastFailed_PendingCount` | 获取当前审计状态 |
| `GetStatus_NoRuns_ReturnsDefaults` | 无运行记录时返回默认值 |
| `ResolveRecord_NotFound_Returns404` | 记录不存在返回 404 |
| `ResolveRecord_PendingWithNoReferences_DeletesAndRemovesFromDb` | Pending 记录无引用时删除并移除 |
| `ResolveRecord_ReferencedByStudentService_Returns400` | 被 Student 服务引用时返回 400 |
| `ResolveRecord_ReferencedByMistakeService_Returns400` | 被 Mistake 服务引用时返回 400 |
| `ResolveRecord_StudentServiceUnavailable_Returns502AndDoesNotDelete` | 引用来源不可达时返回 502 且不删除 |
| `ResolveRecord_AlreadyResolved_SkipsReferenceCheck_DeletesDirectly` | 已 Resolved 记录跳过引用检查直接删除 |
| `IgnoreRecord_NotFound_Returns404` | 记录不存在返回 404 |
| `IgnoreRecord_PendingRecord_SetsStatus2AndNote` | Pending 记录设置 Status=2 和 Note |
| `IgnoreRecord_NonPendingRecord_Returns400` | 非 Pending 记录返回 400 |
| `BatchResolve_EmptyIds_Returns400` | 空 ids 列表返回 400 |
| `BatchResolve_NullIds_Returns400` | null ids 返回 400 |
| `BatchResolve_Success_ResolvesMultipleRecords` | 批量清理多条记录成功 |
| `BatchResolve_SomeReferenced_SkipsReferencedResolvesOthers` | 部分被引用时跳过引用的，清理其余的 |
| `BatchResolve_ExcludesIgnoredRecords` | Status=2 的记录不参与批量清理 |
| `BatchResolve_ReferenceSourceUnavailable_Returns502AndDeletesNothing` | 任一来源不可达时整批不删 |

### OssAuditWorker 审计范围测试（已实现）

`backend/Tests/Services/OssAuditWorkerScopeTests.cs`：

| 测试方法 | 验证内容 |
| --- | --- |
| `AuditedBuckets_ExcludeEveryBucketWithoutAReferenceSource` | 审计范围只含 Uploads、Mistakes，排除 Questions、Documents |
| `AuditedBucketNames_CoverExactlyTheAuditedBuckets` | 桶名映射与审计范围一一对应 |
| `CleanupUnauditedBucketAuditRecords_RemovesRecordsFromUnauditedBuckets` | 启动清理移除 questions/documents 历史误判记录 |
| `CleanupUnauditedBucketAuditRecords_KeepsRecordsWhoseBucketIsUnrecognised` | 桶名为空的记录保留，不做无依据删除 |
| `CleanupUnauditedBucketAuditRecords_IsIdempotentWhenNothingIsStale` | 无陈旧记录时不清理 |
| `RunAudit_NeverScansTheQuestionsBucket_AndNeverFlagsQuestionImages` | 从不调用 `ListObjectsWithBucketAsync(Questions)`；同一路径出现在受审计桶下仍会被正常标记 |
| `RunAudit_AbortsWhenMistakeServiceIsUnavailable_InsteadOfFlaggingEveryMistakeImage` | Mistake 不可达 → Status=2、ErrorMessage、零记录、零扫描 |
| `RunAudit_AbortsWhenStudentServiceIsUnavailable` | Student 不可达 → Status=2、零记录 |
| `RunAudit_AbortsWhenHomeworkServiceIsUnavailable` | Homework 返回非 2xx → Status=2、零记录 |
| `RunAudit_FlagsAnObjectNoReferenceSourceCovers_AndSparesReferencedOnes` | 被引用对象与派生缩略图不标记，真实孤儿标记且 Size 正确 |

### OssAuditWorker 旧 Homework 批改图清理测试（已实现）

`backend/Tests/Services/OssAuditWorkerTests.cs`：

| 测试方法 | 验证内容 |
| --- | --- |
| `IsLegacyHomeworkReviewImagePath_OnlyMatchesObsoleteDerivedOriginals` | 仅匹配 UUID 结构严格一致的旧批改派生图 |
| `CleanupLegacyHomeworkReviewImages_DeletesOnlyMatchingObjects` | 只删除匹配对象，学生提交原图不受影响 |
| `CleanupLegacyHomeworkReviewAuditRecords_RemovesOnlyObsoletePaths` | 只移除匹配路径的审计记录 |

### 鉴权回归测试（已实现）

测试代码位于 `Tests/Controllers/ControllerAuthorizationTests.cs`，通过反射验证 controller 级 `[Authorize]` 属性存在，防止重构时误删导致鉴权失效。FallbackPolicy 兜底不足以替代显式标注（项目约定：所有 `/api/admin/*` controller 必须显式 `[Authorize]`，作为后续 `[Authorize(Roles = "admin")]` 收紧的扩展点）。

| 测试方法 | 验证内容 |
| --- | --- |
| `OssAuditController_HasAuthorizeAttribute` | OssAuditController 必须携带 `[Authorize]` 属性（清理接口可删除 OSS 对象，鉴权强制） |

### OssAuditWorker 测试（已实现）

`OssAuditWorker` 的测试分两个文件，方法清单见上文两节：

- `Tests/Services/OssAuditWorkerTests.cs` — 旧 Homework 批改派生图的路径匹配与清理
- `Tests/Services/OssAuditWorkerScopeTests.cs` — 审计桶范围、启动清理、三个引用来源的失败语义

仍未覆盖的部分：`ExecuteAsync` 的调度计算（`OssAudit:ScheduledHour` / `ScheduledMinute` 与跨日推进）和并发互斥（`Status=0` 记录作为锁、`DbUpdateException` 分支）。

---

## OssAuditController 测试

### GET /records

```gherkin
Feature: 查看审计记录列表

  Scenario: 获取所有审计记录（默认分页）
    Given 数据库中存在审计记录
    When 发送 GET /api/admin/oss-audit/records
    Then 返回200和分页结果，包含items、statusCounts、bucketCounts

  Scenario: 按状态筛选审计记录
    Given 数据库中存在不同状态的审计记录
    When 发送 GET /api/admin/oss-audit/records?status=0
    Then 返回200，仅包含Pending状态的记录

  Scenario: 按桶筛选审计记录
    Given 数据库中存在不同桶的审计记录
    When 发送 GET /api/admin/oss-audit/records?bucket=uploads
    Then 返回200，仅包含uploads桶的记录
```

### POST /trigger

```gherkin
Feature: 手动触发审计

  Scenario: 成功触发审计
    Given 当前没有运行中的审计任务
    When 发送 POST /api/admin/oss-audit/trigger
    Then 返回200，后台启动审计任务

  Scenario: 审计已在运行时触发
    Given 当前已有运行中的审计任务
    When 发送 POST /api/admin/oss-audit/trigger
    Then 返回冲突错误，提示审计已在运行中
```

### GET /status

```gherkin
Feature: 查看审计状态

  Scenario: 获取当前审计状态
    Given 数据库中存在审计运行记录
    When 发送 GET /api/admin/oss-audit/status
    Then 返回200，包含isRunning、lastCompleted、lastFailed、pendingCount
```

### POST /records/{id}/resolve

```gherkin
Feature: 清理单条僵尸文件

  Scenario: 成功清理Pending记录
    Given 存在一条Pending状态的审计记录
    And 该文件未被Student服务注册
    And 该文件未被Mistake服务引用
    When 发送 POST /api/admin/oss-audit/records/{id}/resolve
    Then 调用Student服务DeleteOssObject删除OSS对象
    And 从数据库移除该记录
    And 返回200

  Scenario: 清理仍被Student服务引用的文件
    Given 存在一条Pending状态的审计记录
    And 该文件仍被Student服务注册
    When 发送 POST /api/admin/oss-audit/records/{id}/resolve
    Then 返回错误，拒绝清理

  Scenario: 清理仍被Mistake服务引用的文件
    Given 存在一条Pending状态的审计记录
    And 该文件仍被Mistake服务引用
    When 发送 POST /api/admin/oss-audit/records/{id}/resolve
    Then 返回错误，拒绝清理

  Scenario: 清理已Resolved的记录
    Given 存在一条Resolved状态的审计记录
    When 发送 POST /api/admin/oss-audit/records/{id}/resolve
    Then 跳过引用校验[推断]
```

### POST /records/{id}/ignore

```gherkin
Feature: 忽略单条僵尸文件

  Scenario: 成功忽略记录
    Given 存在一条Pending状态的审计记录
    When 发送 POST /api/admin/oss-audit/records/{id}/ignore
    Then 记录Status设为Ignored(2)
    And 设置ResolvedAt为当前时间
    And 返回200

  Scenario: 忽略记录并添加备注
    Given 存在一条Pending状态的审计记录
    When 发送 POST /api/admin/oss-audit/records/{id}/ignore
    And 请求体包含note="暂时保留"
    Then 记录Status设为Ignored(2)
    And Note字段设为"暂时保留"
```

### POST /records/batch-resolve

```gherkin
Feature: 批量清理僵尸文件

  Scenario: 批量清理多条记录
    Given 存在多条Pending状态的审计记录
    And 这些文件均未被引用
    When 发送 POST /api/admin/oss-audit/batch-resolve
    And 请求体包含ids列表
    Then 一次性获取引用路径
    And 逐条校验并删除符合条件的记录
    And 返回200

  Scenario: 批量清理中部分文件仍被引用
    Given 存在多条Pending状态的审计记录
    And 其中部分文件仍被引用
    When 发送 POST /api/admin/oss-audit/batch-resolve
    Then 仅清理未被引用的记录
    And 被引用的记录保留[推断]
```

---

## OssAuditWorker 测试

```gherkin
Feature: 定时审计调度

  Scenario: 按调度时间自动执行审计
    Given OssAuditWorker已启动
    And 配置的调度时间为凌晨2:00
    When 到达调度时间
    Then 自动调用RunAuditAsync("scheduled")

  Scenario: 启动后延迟2分钟
    Given OssAuditWorker刚启动
    Then 等待2分钟后才开始调度计算

  Scenario: Student服务不可用时中止审计
    Given 审计任务正在运行
    And Student服务不可用
    When RunAuditAsync执行
    Then 审计标记为Failed
    And ErrorMessage记录Student服务不可用信息

  Scenario: Mistake服务不可用时中止审计
    Given 审计任务正在运行
    And Student服务可用
    And Mistake服务不可用
    When RunAuditAsync执行
    Then 审计标记为Failed
    And ErrorMessage记录Mistake服务不可用信息
    And 不扫描任何桶，不产生审计记录

  Scenario: Homework服务不可用时中止审计
    Given 审计任务正在运行
    And Student服务可用
    And Homework服务不可用
    When RunAuditAsync执行
    Then 审计标记为Failed
    And ErrorMessage记录Homework服务不可用信息
    And 不扫描任何桶，不产生审计记录

  Scenario: 不扫描没有引用来源的桶
    Given 审计任务正在运行
    And Student、Homework、Mistake 服务均可用
    When RunAuditAsync执行
    Then 仅扫描 uploads 和 mistakes 两个桶
    And 从不调用 ListObjectsWithBucketAsync(Questions) 或 (Documents)
    And 不产生 Bucket 为 questions 或 documents 的审计记录

  Scenario: 启动时清理历史误判记录
    Given 数据库中存在 Bucket 为 questions 的历史审计记录
    When OssAuditWorker 启动延迟结束
    Then 这些记录被移除
    And Bucket 为 uploads、mistakes 的记录保留
    And Bucket 为空或无法识别的记录保留
```
