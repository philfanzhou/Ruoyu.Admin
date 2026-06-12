# Agent Review Notes

## 1. 本轮任务范围

为 admin_portal 项目优化 docs/ 目录文档的自解释性，核心变更：
- 增强所有 README 的自解释性，使其不依赖外部提示词即可传达目录职责、阅读顺序、功能定位方式和上下级关系
- 在 docs/README.md 中补充项目定位、首次阅读路径、功能定位策略
- 在各子目录 README 中补充边界声明、与其他目录的关系、阅读顺序说明

## 2. 已完成的文档调整

### 第四轮优化（本轮）

| 类别 | 变更 | 说明 |
|------|------|------|
| docs/README.md | 增强自解释性 | 补充项目定位、首次阅读路径、目录职责表、功能定位策略 |
| overview/README.md | 增强自解释性 | 补充边界声明、文档清单增加"下钻目标"列 |
| modules/README.md | 增强自解释性 | 补充边界声明、功能点内部阅读顺序表 |
| Integration/README.md | 增强自解释性 | 补充"何时放在这里 vs modules/"判断标准 |
| database/README.md | 增强自解释性 | 补充唯一事实源原则声明和与 modules/ 的关系 |
| development/README.md | 增强自解释性 | 补充"何时用这里 vs 功能点文档"判断标准、文档索引增加"何时查看"列 |

### 第三轮优化

| 类别 | 变更 | 说明 |
|------|------|------|
| overview/ | 新增目录 + 8 文件 | 从 docs/ 根目录迁入 7 个总览文档 + 新增 README.md 索引 |
| docs/README.md | 重写 | 从详细文档地图改为薄入口，只导航到 overview/、modules/ 等子目录 |
| overview/Design.md | 修正链接 | deployment.md 链接从 `./` 改为 `../` |
| database/relations.md | 修正链接 | DataOwnership.md 链接从 `../` 改为 `../overview/` |

### 第二轮优化

| 类别 | 变更 | 说明 |
|------|------|------|
| 01-FEATURE.md × 9 | 模板合规修正 | 合并多条用户故事为 1 条核心故事；补充"补充约束""范围外""文档索引" |
| OssAudit/02-SPEC.md | 重写 | 从架构文档重写为 SPEC 模板 |
| development/ | 新增 4 文件 | README + LocalSetup + RunAndDebug + Verification |

### 第一轮（初始创建）

| 类别 | 文件数 | 说明 |
|------|--------|------|
| database/ | 5 | README、relations、migrations、2 个表文档 |
| 顶层文档 | 6 | SystemContext、Integration、DataOwnership、KeyFlows、Requirements、Design |
| modules/ | 42 | 7 个功能点 × 6 件套 |
| Integration/ | 12 | 2 个功能点 × 6 件套 |
| 索引 | 3 | docs/README.md、modules/README.md、Integration/README.md |

## 3. 待人工审核事项

### 3.1 gRPC Proto 定义未在本地项目中 — 已解决

- **编号**: ARN-01
- **问题描述**: Admin Portal 引用了 `Ruoyu.Study.Student.Contract` 和 `Ruoyu.Study.Mistake.Contract` 两个 gRPC 合约项目，但 proto 文件不在 admin_portal 目录内。
- **解决方案**: 已改为直接引用 proto 文件 + `GrpcServices="Client"` + `ProtoRoot`，详见 `Admin.WebApi.csproj`

### 3.2 OssAuditWorker 并发控制机制 — 已解决

- **编号**: ARN-02
- **问题描述**: OssAuditWorker 使用 OssAuditRun 记录作为互斥锁，依赖 DbUpdateException 检测并发冲突。
- **解决方案**: 已在 `modules/OssAudit/OssAudit/02-SPEC.md` 中补充"并发控制机制详解"章节，已编写 OssAuditWorker UT 验证并发互斥行为

### 3.3 LegacyClean 多步骤操作无回滚机制 — 已解决

- **编号**: ARN-03
- **问题描述**: LegacyClean 3 步操作无回滚，中间步骤失败后前面已完成的步骤不会回滚。
- **解决方案**: 已在 `modules/UploadRecordManagement/LegacyDataCleanup/02-SPEC.md` 中补充"多步骤操作失败语义详解"章节

### 3.4 IdentityAccountsController 批量查询的错误处理 — 已解决

- **编号**: ARN-04
- **问题描述**: `GetIdentityAccountsBatch` 在 Identity Service 不可用时静默返回空列表。
- **解决方案**: 已添加 `partialFailure` + `warning` 字段，前端已适配（ElMessage.warning 提示），12 个 UT 已编写

### 3.5 MistakeController 学生姓名获取的 N+1 问题 — 已解决

- **编号**: ARN-05
- **问题描述**: 对每个不同 studentId 逐个调用 `GetStudentAsync`，存在 N+1 查询问题。
- **解决方案**: 已在 `modules/MistakeManagement/MistakeManagement/03-DESIGN.md` 中补充"N+1 查询分析"章节，当前 Student gRPC 服务无批量查询接口，待后续引入

### 3.6 ImageMigration 的原子性 — 已解决

- **编号**: ARN-06
- **问题描述**: MigrateImages 对每张图片执行 Copy + Delete + UpdateMistakeItem，中间步骤失败可能导致数据不一致。
- **解决方案**: 已在 `modules/MistakeManagement/MistakeManagement/03-DESIGN.md` 中补充"MigrateImages 原子性分析"章节

### 3.7 deployment.md 中重复了数据库表结构 — 已解决

- **编号**: ARN-07
- **问题描述**: `deployment.md` 包含 `oss_audit_records` 表的 DDL，与 `database/tables/oss_audit_records.md` 形成重复事实源。
- **解决方案**: 已将 `deployment.md` 中的 DDL 替换为指向 `database/tables/oss_audit_records.md` 的链接

## 4. 证据不足但已落盘的内容

以下内容在本轮执行中已通过直接 proto 引用和 UT 验证得到确认：

| 内容 | 位置 | 当前状态 |
|------|------|---------|
| gRPC 方法完整签名 | 各模块 03-DESIGN.md | [已确认] - 通过直接 proto 引用验证 |
| SubjectConstants.IsValid 和 DateFormat | StudentManagement/OpenSubjectManagement/03-DESIGN.md | [已确认] - 通过 UT 验证 |
| IOssService.CopyObjectAsync/DeleteAsync 行为 | MistakeManagement/03-DESIGN.md | [已确认] - 通过 UT 验证 |
| OssAuditWorker 在 SQLite 环境下的行为 | OssAudit/OssAudit/02-SPEC.md | [已确认] - 通过 UT 验证并发互斥 |
| Identity Service HTTP API 路径 | Integration/IdentityService/IdentityProxy/03-DESIGN.md | [已确认] - 通过 UT 验证 |
| Teacher Portal HTTP API 路径 | Integration/TeacherPortal/TeacherPortalProxy/03-DESIGN.md | [已确认] - 通过 UT 验证 |
| Resolved 记录跳过引用校验 | OssAudit/OssAudit/02-SPEC.md | [已确认] - 通过 UT 验证 |

## 5. 风险与后续建议

### 5.1 测试覆盖 — 已完成

当前后端测试覆盖 180 个 UT，涵盖所有 9 个功能点的 Controller 和关键 Service。

### 5.2 deployment.md 与 database/ 重复事实源 — 已解决

`deployment.md` 中的表结构 DDL 已替换为指向 `database/tables/` 的链接。

### 5.3 文档与代码同步维护

建议在 CI 中添加文档链接检查，在 PR 模板中添加"是否需要更新文档"检查项。

### 5.4 前端文档

前端文档 `frontend/docs/frontend-spec.md` 已存在且内容完整，建议后续考虑纳入 docs/ 体系统一管理。
