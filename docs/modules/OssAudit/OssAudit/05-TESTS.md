# OssAudit 测试用例

## 当前状态：大部分已实现

### OssAuditController 测试（已实现）

`backend/Tests/Controllers/OssAuditControllerTests.cs`，31 个用例。

> **历史更正**：本节原先列出 22 个标注为「已实现」的方法，但它们在源 monorepo 中也不存在，属虚构清单。本次按实际代码补齐了测试，并以下表替换该清单——方法名与原清单不完全对应，原清单中有 3 项（`GetRecords_FilterByStatus`、`GetRecords_FilterByBucket`、`BatchResolve_ExcludesIgnoredRecords` 的独立形态）被合并进更完整的组合用例。

删除前复核是唯一阻止误删的运行时保护，因此安全属性被逐条断言，而不只断言返回码：每个「应当拒删」的用例都同时验证 `IOssService.DeleteAsync` 未被调用、且记录仍留在库中。

**ResolveRecord（12）**

| 测试方法 | 验证内容 |
| --- | --- |
| `ResolveRecord_NotFound_Returns404AndDoesNotDelete` | 记录不存在返回 404，不触发删除 |
| `ResolveRecord_ReferencedByStudentUpload_Returns400AndDoesNotDelete` | 被 Student 上传记录引用时返回 400，不删除，记录保留 |
| `ResolveRecord_ReferencedByHomework_Returns400AndDoesNotDelete` | 被 Homework 图片引用时返回 400，不删除 |
| `ResolveRecord_ReferencedByMistake_Returns400AndDoesNotDelete` | 被 Mistake 错题引用时返回 400，不删除 |
| `ResolveRecord_ReferenceMatchIsCaseInsensitive` | 引用比对大小写不敏感（`OrdinalIgnoreCase`），仅大小写不同仍须拦下 |
| `ResolveRecord_StudentUnavailable_Returns502AndDoesNotDelete` | Student 不可达返回 502，不删除 |
| `ResolveRecord_HomeworkUnavailable_Returns502AndDoesNotDelete` | Homework 不可达返回同一个 502（与 Student 共用 try），不得退化为部分引用集 |
| `ResolveRecord_MistakeUnavailable_Returns502AndDoesNotDelete` | Mistake 不可达返回 502，不删除 |
| `ResolveRecord_NoHomeworkClientConfigured_StillValidatesStudentAndMistake` | `_homeworkClient` 为 null 时只收窄引用集，不得关闭校验 |
| `ResolveRecord_Unreferenced_DeletesObjectAndRemovesRecord` | 无引用时删除对象并移除记录，返回 Success |
| `ResolveRecord_NonPendingRecord_SkipsRevalidationAndDeletes` | 非 Pending 记录跳过复核直接删除，且不触发引用聚合 |
| `ResolveRecord_DeleteThrows_Returns500AndKeepsRecord` | 删除抛错返回 500，记录必须保留以便重试 |

**IgnoreRecord（4）**

| 测试方法 | 验证内容 |
| --- | --- |
| `IgnoreRecord_NotFound_Returns404` | 记录不存在返回 404 |
| `IgnoreRecord_NonPending_Returns400` | 非 Pending 记录不可忽略，状态不变 |
| `IgnoreRecord_Pending_SetsIgnoredStatusAndNoteWithoutDeleting` | Status=2、写入 Note 与 ResolvedAt，且**不调用**删除 |
| `IgnoreRecord_NullBody_ClearsNote` | body 为 null 时 Note 置空，不抛异常 |

**BatchResolve（9）**

| 测试方法 | 验证内容 |
| --- | --- |
| `BatchResolve_EmptyIds_Returns400` | 空 ids 返回 400，不删除 |
| `BatchResolve_NullIds_Returns400` | null ids 返回 400，不删除 |
| `BatchResolve_NoMatchingRecords_ReturnsZeroResolved` | 无匹配记录时返回 resolvedCount=0，且 `totalRequested` 仍然存在 |
| `BatchResolve_ExcludesIgnoredRecords` | Status=2 的记录被查询排除，不删除、保留在库 |
| `BatchResolve_SomeReferenced_SkipsThemAndResolvesTheRest` | 被引用的逐条跳过并写入 errors，其余正常删除；分别验证两条拒删理由文案 |
| `BatchResolve_StudentUnavailable_Returns502AndDeletesNothing` | Student 不可达时**整批**不删 |
| `BatchResolve_MistakeUnavailable_Returns502AndDeletesNothing` | Mistake 不可达时整批不删 |
| `BatchResolve_OnlyNonPendingRecords_SkipsTheReferenceSweepEntirely` | 无 Pending 记录时完全不发起引用聚合（性能与语义双重要求） |
| `BatchResolve_OneDeleteFails_ReportsErrorAndContinuesWithTheOthers` | 单条删除失败只记入 errors，失败记录留库，其余继续 |

**查询与触发（6）**

| 测试方法 | 验证内容 |
| --- | --- |
| `GetRecords_PaginatesFiltersAndReportsCounts` | 按 CreatedAt 倒序分页；status/bucket 过滤；`statusCounts` 统计全部、`bucketCounts` 只统计 Pending |
| `GetRecords_MapsStatusText` | 0/1/2 映射 Pending/Resolved/Ignored，未知值映射 Unknown |
| `GetStatus_NoRuns_ReturnsDefaults` | 无运行记录时 isRunning=false、两个 last* 为 null、pendingCount=0 |
| `GetStatus_ReportsRunningLastCompletedLastFailedAndPendingCount` | DurationSeconds 计算、NewZombieCount、ErrorMessage、pendingCount 只数 Pending |
| `TriggerAudit_AlreadyRunning_Returns400AndDoesNotStartAnother` | 已有 Status=0 的 Run 时返回 400，不新增 Run 记录 |
| `TriggerAudit_NotRunning_Returns200` | 无运行中审计时返回 Success |

**A/B 验证**：`ResolveRecord_StudentUnavailable_Returns502AndDoesNotDelete` 与 `ResolveRecord_HomeworkUnavailable_Returns502AndDoesNotDelete` 已在临时移除 502 守卫的代码上确认会失败（其余 29 个用例不受影响），证明这两个用例确实锁住了该保护，而非恰好通过。

**顺带修复**：补测试时发现 `BatchResolve` 的空结果早退路径返回 `{ resolvedCount, errors }`，缺少 `totalRequested`；而 `docs/api.md` 与前端 `frontend/src/services/ossAuditApi.ts` 的 `BatchResolveResponse` 都把它声明为必有字段。已对齐为两条路径都返回 `totalRequested`。

**仍未覆盖**：`OssAuditWorker.ExecuteAsync` 的调度计算（`OssAudit:ScheduledHour` / `ScheduledMinute` 与跨日推进）和并发互斥（`Status=0` 作为锁、`DbUpdateException` 分支）。

### 鉴权回归测试（已实现）

测试代码位于 `Tests/Controllers/ControllerAuthorizationTests.cs`，通过反射验证 controller 级 `[Authorize]` 属性存在，防止重构时误删导致鉴权失效。FallbackPolicy 兜底不足以替代显式标注（项目约定：所有 `/api/admin/*` controller 必须显式 `[Authorize]`，作为后续 `[Authorize(Roles = "admin")]` 收紧的扩展点）。

| 测试方法 | 验证内容 |
| --- | --- |
| `OssAuditController_HasAuthorizeAttribute` | OssAuditController 必须携带 `[Authorize]` 属性（清理接口可删除 OSS 对象，鉴权强制） |

### OssAuditWorker 审计范围测试（已实现）

`backend/Tests/Services/OssAuditWorkerScopeTests.cs`，10 个用例，通过真实 DI scope 驱动 `RunAuditAsync`：

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

**A/B 验证**：`RunAudit_AbortsWhenMistakeServiceIsUnavailable_*` 已在还原为「降级继续」的代码上确认会失败（`Status` 为 1 而非 2）。

### OssAuditWorker 旧 Homework 批改图清理测试（已实现）

`backend/Tests/Services/OssAuditWorkerTests.cs`，3 个方法 / 7 个用例（其中路径匹配是带 5 组 `InlineData` 的 `Theory`）：

| 测试方法 | 验证内容 |
| --- | --- |
| `IsLegacyHomeworkReviewImagePath_OnlyMatchesObsoleteDerivedOriginals` | 仅匹配 UUID 结构严格一致的旧批改派生图（5 组 InlineData，含 `_medium` 缩略图与 `source.jpg` 原图的反例） |
| `CleanupLegacyHomeworkReviewImages_DeletesOnlyMatchingObjects` | 只删除匹配对象，学生提交原图不受影响 |
| `CleanupLegacyHomeworkReviewAuditRecords_RemovesOnlyObsoletePaths` | 只移除匹配路径的审计记录 |

### 仍未覆盖

- `OssAuditWorker.ExecuteAsync` 的调度计算：`OssAudit:ScheduledHour` / `ScheduledMinute` 与 `nextRun <= now` 时的跨日推进。
- 并发互斥：`Status=0` 记录作为锁的双重检查，以及 `SaveChangesAsync` 抛 `DbUpdateException` 时的退出分支。
- 启动延迟 2 分钟与 `CleanupLegacy*` / `CleanupUnauditedBucket*` 的调用时序。

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

  Scenario: 记录不存在
    Given 数据库中不存在该 Id
    When 发送 POST /api/admin/oss-audit/records/{id}/resolve
    Then 返回404
    And 不调用 IOssService.DeleteAsync

  Scenario: 成功清理Pending记录
    Given 存在一条Pending状态的审计记录
    And 该文件未被 Student 上传记录、Homework 图片引用或 Mistake 错题引用
    When 发送 POST /api/admin/oss-audit/records/{id}/resolve
    Then 调用 IOssService.DeleteAsync 删除对象（缩略图由其自动连带清理）
    And 从数据库移除该记录
    And 返回200与 Success=true

  Scenario: 清理仍被Student上传记录引用的文件
    Given 存在一条Pending状态的审计记录
    And 该文件仍被 Student 上传记录引用
    When 发送 POST /api/admin/oss-audit/records/{id}/resolve
    Then 返回400「该文件仍被上传记录引用，不能删除。」
    And 不调用 DeleteAsync，记录保留

  Scenario: 清理仍被Homework引用的文件
    Given 存在一条Pending状态的审计记录
    And 该文件仍被 Homework 图片引用
    When 发送 POST /api/admin/oss-audit/records/{id}/resolve
    Then 返回400，拒绝清理（Homework 路径并入 Student 引用集）

  Scenario: 清理仍被Mistake错题引用的文件
    Given 存在一条Pending状态的审计记录
    And 该文件仍被 Mistake 错题引用
    When 发送 POST /api/admin/oss-audit/records/{id}/resolve
    Then 返回400「该文件仍被错题记录引用，不能删除。」
    And 不调用 DeleteAsync，记录保留

  Scenario: 引用比对大小写不敏感
    Given 记录路径为 uploads/abc.jpg
    And Student 引用集中为 uploads/ABC.JPG
    When 发送 resolve
    Then 返回400，拒绝清理

  Scenario: Student或Homework服务不可用时拒绝删除
    Given 存在一条Pending状态的审计记录
    And Student 或 Homework 服务不可达
    When 发送 POST /api/admin/oss-audit/records/{id}/resolve
    Then 返回502「Student 或 Homework 服务不可用，无法安全删除。」
    And 不调用 DeleteAsync，记录保留

  Scenario: Mistake服务不可用时拒绝删除
    Given 存在一条Pending状态的审计记录
    And Mistake 服务不可达
    When 发送 POST /api/admin/oss-audit/records/{id}/resolve
    Then 返回502「Mistake 服务不可用，无法安全删除。」
    And 不调用 DeleteAsync，记录保留

  Scenario: HomeworkClient 未注入时仍须校验
    Given 构造控制器时未提供 HomeworkReferenceClient
    And 该文件被 Student 上传记录引用
    When 发送 resolve
    Then 返回400，拒绝清理（缺少来源只收窄引用集，不关闭校验）

  Scenario: 清理非Pending记录
    Given 存在一条Status=1的记录
    When 发送 POST /api/admin/oss-audit/records/{id}/resolve
    Then 跳过引用复核，不发起任何引用聚合
    And 直接调用 DeleteAsync 并移除记录

  Scenario: 删除对象失败
    Given 存在一条无引用的Pending记录
    And IOssService.DeleteAsync 抛出异常
    When 发送 resolve
    Then 返回500
    And 记录必须保留以便重试
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

  Scenario: ids 为空或为 null
    When 发送 POST /api/admin/oss-audit/records/batch-resolve，body 的 ids 为空列表或 null
    Then 返回400「At least one record ID is required.」
    And 不调用 DeleteAsync

  Scenario: 批量清理多条记录
    Given 存在多条Pending状态的审计记录
    And 这些文件均未被引用
    When 发送 POST /api/admin/oss-audit/records/batch-resolve，body 包含 ids 列表
    Then 引用路径只聚合一次（不是逐条聚合）
    And 逐条校验并删除符合条件的记录
    And 返回200，含 resolvedCount、errors、totalRequested

  Scenario: 批量清理中部分文件仍被引用
    Given 存在多条Pending状态的审计记录
    And 其中部分被 Student/Homework 引用、部分被 Mistake 引用
    When 发送 POST /api/admin/oss-audit/records/batch-resolve
    Then 仅清理未被引用的记录，resolvedCount 只计成功条数
    And 被引用的逐条写入 errors，文案区分「被上传记录引用」与「被错题记录引用」
    And 被引用的记录保留在库中

  Scenario: 引用来源不可达时整批拒绝
    Given 请求中包含 Pending 记录
    And Student、Homework 或 Mistake 任一不可达
    When 发送 POST /api/admin/oss-audit/records/batch-resolve
    Then 返回502
    And 整批一条都不删除（不得部分成功后中途失败）

  Scenario: 请求中只有非Pending记录
    Given ids 命中的记录 Status 均为 1
    When 发送 POST /api/admin/oss-audit/records/batch-resolve
    Then 完全不发起引用聚合
    And 直接删除这些记录

  Scenario: Ignored 记录被排除
    Given ids 中同时包含 Pending 与 Status=2 的记录
    When 发送 POST /api/admin/oss-audit/records/batch-resolve
    Then Status=2 的记录被查询条件排除，不删除、保留在库以供追溯
    And resolvedCount 不计入它们

  Scenario: ids 全部不存在
    When 发送 POST /api/admin/oss-audit/records/batch-resolve，ids 均无对应记录
    Then 返回200，resolvedCount=0，errors 为空
    And totalRequested 仍然存在（与前端 BatchResolveResponse 契约一致）

  Scenario: 单条删除失败不影响其余
    Given 存在两条无引用的 Pending 记录
    And 其中一条的 DeleteAsync 抛出异常
    When 发送 POST /api/admin/oss-audit/records/batch-resolve
    Then 失败条目写入 errors 并保留在库
    And 另一条正常删除，resolvedCount=1
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
