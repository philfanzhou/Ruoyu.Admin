# 关键业务流程

## 1. OSS 审计流程

OSS 审计用于发现 OSS 存储中未被任何业务引用的"僵尸"文件。支持定时触发和手动触发两种方式。

### 时序图

```
定时触发 (OssAuditWorker)              手动触发 (OssAuditController)
    │                                        │
    │  每天凌晨 2:00 (可配置)                  │  POST /api/admin/oss-audit/trigger
    │                                        │
    ▼                                        ▼
┌──────────────────────────────────────────────────────┐
│ 1. 创建 OssAuditRun (Status=Running)                  │
│    → 若 SaveChanges 失败 (DbUpdateException)          │
│      说明有并发审计，直接退出                           │
│    → 双重检查：查询是否有其他 Status=0 的 Run           │
│      若有，删除当前 Run 并退出                          │
└──────────────────────┬───────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────┐
│ 2. 获取 Student 服务的已注册 OSS 路径                  │
│    gRPC: GetRegisteredOssPaths                        │
│    → 若 Student 服务不可用 → 标记 Run 为 Failed，退出  │
└──────────────────────┬───────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────┐
│ 3. 获取 Mistake 服务的已引用图片路径                    │
│    gRPC: GetAllReferencedImagePaths                   │
│    → 若 Mistake 服务不可用 → 记录警告，继续审计         │
│      （结果可能存在误报）                               │
└──────────────────────┬───────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────┐
│ 4. 遍历 OSS Buckets (uploads/mistakes/questions)      │
│    gRPC: ListOssObjects (per bucket)                  │
│                                                       │
│    对每个对象：                                        │
│    ├─ 在 registeredPaths 中？ → 跳过                  │
│    ├─ 在 mistakePaths 中？ → 跳过                     │
│    └─ 都不在 → 检查 OssAuditRecords 是否已存在        │
│       └─ 不存在 → 插入新记录 (Status=Pending)         │
└──────────────────────┬───────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────┐
│ 5. 更新 OssAuditRun                                  │
│    Status=Completed, NewZombieCount=N                 │
└──────────────────────────────────────────────────────┘
```

### Resolve 流程

```
管理员                     OssAuditController          Student Svc        Mistake Svc        DB
  │                              │                        │                  │              │
  │ POST /records/{id}/resolve   │                        │                  │              │
  │─────────────────────────────>│                        │                  │              │
  │                              │ GetRegisteredOssPaths  │                  │              │
  │                              │───────────────────────>│                  │              │
  │                              │<──────────────────────│                  │              │
  │                              │                        │                  │              │
  │                              │ GetAllReferencedImagePaths                │              │
  │                              │─────────────────────────────────────────>│              │
  │                              │<────────────────────────────────────────│              │
  │                              │                        │                  │              │
  │                              │ [验证：路径仍被引用？]   │                  │              │
  │                              │ 若被引用 → 400 BadReq  │                  │              │
  │                              │                        │                  │              │
  │                              │ DeleteOssObject        │                  │              │
  │                              │───────────────────────>│                  │              │
  │                              │<──────────────────────│                  │              │
  │                              │                        │                  │              │
  │                              │ Remove AuditRecord     │                  │              │
  │                              │──────────────────────────────────────────────────────>│
  │                              │<─────────────────────────────────────────────────────│
  │                              │                        │                  │              │
  │<─────────────────────────────│ 200 OK                 │                  │              │
```

---

## 2. 上传记录分配流程

将上传记录中的图片按分组分配为错题条目，支持跨学科分配。

### 时序图

```
管理员                     OssUploadRecordController   Student Svc        Mistake Svc
  │                              │                        │                  │
  │ POST /{id}/assign            │                        │                  │
  │ {assignments: [...]}         │                        │                  │
  │─────────────────────────────>│                        │                  │
  │                              │                        │                  │
  │                              │ GetUploadRecord        │                  │
  │                              │───────────────────────>│                  │
  │                              │<──────────────────────│                  │
  │                              │                        │                  │
  │                              │ ┌─ 对每个 assignment ─┐│                  │
  │                              │ │                     ││                  │
  │                              │ │ SubmitMistakeUpload ││                  │
  │                              │ │─────────────────────────────────────>│
  │                              │ │<────────────────────────────────────│
  │                              │ │ (返回 createdItemIds)││              │
  │                              │ └─────────────────────┘│                  │
  │                              │                        │                  │
  │                              │ MarkUploadRecordCompleted               │
  │                              │───────────────────────>│                  │
  │                              │<──────────────────────│                  │
  │                              │                        │                  │
  │<─────────────────────────────│ 200 OK                 │                  │
  │   { createdItems,            │                        │                  │
  │     remainingImageCount,     │                        │                  │
  │     totalImageCount }        │                        │                  │
```

### 关键语义

- 一个 `assignment` = 一道错题 = 一条 `mistake_item`
- 一个 `assignment` 内的多张图片打包到同一条 `mistake_item` 的 `source_regions`
- 多个 `assignment` 可分别指定不同的 `subject` / `grade`（跨学科分配）
- 同一上传记录可被多次分配（每次创建新的 `mistake_item`）
- 分配后上传记录被标记为 Completed，`mistake_item` 进入 `PendingReview` 状态

---

## 3. 遗留数据清理流程

清理已审核完毕但仍残留的上传记录。当上传记录关联的错题已全部审核（Confirmed/Rejected），但上传记录本身未被删除时，属于遗留数据。

### 时序图

```
管理员                     OssUploadRecordController   Mistake Svc        Student Svc
  │                              │                        │                  │
  │ 1. GET /legacy-check/{id}    │                        │                  │
  │─────────────────────────────>│                        │                  │
  │                              │ GetMistakeItemsByUpload│                  │
  │                              │───────────────────────>│                  │
  │                              │<──────────────────────│                  │
  │                              │                        │                  │
  │                              │ [判断：是否遗留数据？]   │                  │
  │  - 无关联错题 → isLegacy=false│                        │                  │
  │  - 有待审核 → isLegacy=false  │                        │                  │
  │  - 全部已审核 → isLegacy=true │                        │                  │
  │<─────────────────────────────│                        │                  │
  │                              │                        │                  │
  │ 2. POST /legacy-clean/{id}   │                        │                  │
  │─────────────────────────────>│                        │                  │
  │                              │                        │                  │
  │                              │ Step 1: CompleteUploadReview             │
  │                              │───────────────────────>│                  │
  │                              │<──────────────────────│                  │
  │                              │  (RemovedImagePaths)   │                  │
  │                              │                        │                  │
  │                              │ Step 2: RemoveImagesFromRecord [若有]    │
  │                              │───────────────────────────────────────>│
  │                              │<──────────────────────────────────────│
  │                              │                        │                  │
  │                              │ Step 3: DeleteUploadRecordAfterReview   │
  │                              │───────────────────────────────────────>│
  │                              │<──────────────────────────────────────│
  │                              │                        │                  │
  │<─────────────────────────────│ 200 OK { success: true }               │
```

### 清理三步骤

1. **CompleteUploadReview**：调用 Mistake 服务完成上传审核，返回需要移除的图片路径列表
2. **RemoveImagesFromRecord**：调用 Student 服务从上传记录中移除已处理的图片路径
3. **DeleteUploadRecordAfterReview**：调用 Student 服务删除上传记录

---

## 4. 学生 CRUD 与账户关联流程

学生的创建、查询、更新、删除操作，以及与 Identity 账户的关联/解绑。

### 创建学生时序图

```
管理员                     StudentsController          Student Svc (gRPC)
  │                              │                        │
  │ POST /api/admin/students     │                        │
  │ { name, grade,              │                        │
  │   identityAccountIds }       │                        │
  │─────────────────────────────>│                        │
  │                              │                        │
  │                              │ [验证]                  │
  │                              │ - name 非空             │
  │                              │ - grade 有效 (1-12)     │
  │                              │ - accountIds 非空        │
  │                              │ - accountIds 格式合法    │
  │                              │                        │
  │                              │ CreateStudent           │
  │                              │───────────────────────>│
  │                              │<──────────────────────│
  │                              │                        │
  │<─────────────────────────────│ 200 OK (StudentDto)    │
```

### 关联/解绑账户时序图

```
管理员                     StudentsController          Student Svc (gRPC)
  │                              │                        │
  │ 关联：                       │                        │
  │ POST /{studentId}/accounts   │                        │
  │ { identityAccountId }        │                        │
  │─────────────────────────────>│                        │
  │                              │ LinkIdentityAccount    │
  │                              │───────────────────────>│
  │                              │<──────────────────────│
  │<─────────────────────────────│ 200 OK                 │
  │                              │                        │
  │ 解绑：                       │                        │
  │ DELETE /{sid}/accounts/{aid} │                        │
  │─────────────────────────────>│                        │
  │                              │ UnlinkIdentityAccount  │
  │                              │───────────────────────>│
  │                              │<──────────────────────│
  │<─────────────────────────────│ 200 OK                 │
```

### 关键约束

- 创建学生时必须提供至少一个 `identityAccountId`
- `identityAccountId` 必须是合法的 GUID 格式
- 年级取值范围 1-12（1-6 小学，7-9 初中，10-12 高中）
- 更新学生时 `identityAccountIds` 可选，提供时会替换整个关联列表
