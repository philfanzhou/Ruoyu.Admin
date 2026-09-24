# Pending Decisions

本文件是 Ruoyu.Admin 唯一的未决设计入口，只记录当前代码仍能证实、且需要用户或架构取舍的问题。

- 决定形成后，迁入对应 ADR、能力文档或实施 Issue，并从本文件移除。
- 未决事项只阻塞各节声明的范围，不阻塞无关修复、测试或功能开发。
- 不在此记录执行日志、已完成事项、普通代码卫生建议或测试结果。
- 跨仓库契约类问题若主责在 Ruoyu.Study，记录在该仓库的 `docs/pending-decisions.md`，本文件只保留 Admin 侧的取舍。

PD-001 至 PD-004 于 2026-09-24 随产品从 Ruoyu.Study monorepo 迁入，原编号分别为 PD-009、PD-010、PD-011、PD-013（见 Ruoyu.Study 仓库的 ADR-0010（extract-ruoyu-admin，该仓库未公开））。

## PD-001 OSS 审计任务的可靠调度与互斥

- **状态**：待决定
- **影响范围**：Storage Audit
- **当前证据**：`OssAuditController.TriggerAudit` 使用 fire-and-forget `Task.Run`；`OssAuditWorker.RunAuditAsync` 的运行中检查（查询其他 `Status=0` 的 Run）与 Run 记录创建不是单一原子互斥操作，仅靠 `DbUpdateException` 兜底，进程退出时任务可能丢失。
- **A（推荐）**：使用有界 Channel 向现有 BackgroundService 排队，并用数据库唯一运行约束保证互斥。
- **B**：使用持久化作业表或专用任务框架，支持重试和恢复。
- **C**：保留当前进程内触发方式。
- **阻塞范围**：只阻塞手动 OSS 审计触发和任务恢复模型。
- **关联**：`OssAuditWorker` 的并发互斥路径目前没有测试覆盖（见 `docs/modules/OssAudit/OssAudit/05-TESTS.md` 的未覆盖清单），决定落地时需同时补齐。

## PD-002 OSS 审计全量扫描的扩展策略

- **状态**：待决定
- **影响范围**：Storage Audit；跨仓库依赖 Learner、Mistake Learning、Homework 与对象存储
- **当前证据**：`OssAuditWorker` 分页读取全部上传记录（`GetAllUploadRecordsAsync`，每页 100）和全部错题条目（`GetMistakeItemListAsync`，每页 100），在内存聚合全部路径，再对每个桶逐对象比对。`OssAuditController` 的删除前复核在**每次** resolve 时重新执行同一套全量聚合，批量处置时为每条记录重复查询。
- **A（推荐）**：数据主责服务提供游标式引用路径流，Storage Audit 使用分块集合运算和批量 upsert。
- **B**：保留现有 API，但增加分页并发、超时、缓存和批量数据库查询。
- **C**：接受当前全量内存聚合。
- **阻塞范围**：只阻塞大数据量 OSS 审计的扩展性改造。
- **跨仓库说明**：方案 A 需要 Student、Mistake、Homework 三个服务新增或改造端点，属 Ruoyu.Study 的实施范围；本仓库只能选择消费方式，不能单方面落地 A。方案 B、C 可独立实施。

## PD-003 未配置 CORS 时的默认策略

- **状态**：待决定
- **影响范围**：Admin Web API 部署
- **当前证据**：`Program.cs` 中 `AdminWeb:AllowedOrigins` 为空时，`AdminWeb` 策略调用 `AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()`。仓库迁出为公开开源项目后，该默认值的暴露面比在私有 monorepo 中更大。
- **A（推荐）**：非 Development 环境启动失败；Development 只允许显式 localhost 来源。
- **B**：未配置时完全不启用跨域策略。
- **C**：保留 AllowAnyOrigin 默认值。
- **阻塞范围**：只阻塞 CORS 默认行为和部署配置。
- **关联**：集成部署模式下 SPA 与 API 同源，浏览器不需要跨域；`AllowedOrigins` 只服务于前后端分离部署。这使方案 A 的启动失败风险可控。

## PD-004 前端测试基线

- **状态**：待决定
- **影响范围**：`frontend/`（Vue 3 + Element Plus SPA）
- **当前证据**：`frontend/package.json` 只有 `dev`/`build`/`preview` 三个脚本，没有单元、组件或 E2E 测试框架，也没有类型检查脚本（`build` 直接调用 `vite build`，不经过 `vue-tsc`）。
- **A（推荐）**：引入 Vitest 与 Vue Test Utils，先覆盖认证、路由守卫和关键表单；跨服务流程继续由 Ruoyu.Study 仓库的 `tests/e2e/` 承担。
- **B**：只增加 Playwright E2E，以真实页面覆盖核心管理流程。
- **C**：暂不建立前端自动测试基线。
- **阻塞范围**：只阻塞前端测试框架与首批测试。
- **关联**：跨服务用例 `04-admin-bind-student.py` 保留在 Ruoyu.Study 的 `tests/e2e/`，通过 `E2E_ADMIN_URL` 访问已部署实例，因此本仓库的前端测试只需覆盖单仓内可验证的行为。
