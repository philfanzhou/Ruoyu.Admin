# 迁移历史

## 迁移策略

本项目使用 **EF Core Migrations**（单一基线迁移 `20260930190608_InitialCreate`），由消费方自有的 `AuditMigrationExecutor`（`backend/Admin.WebApi/Database/AuditMigrationExecutor.cs`）实现 ServiceMantle 的 `IDatabaseMigrationExecutor` SPI，在启动时经共享的 `DatabaseMigrationOrchestrator`（PostgreSQL advisory lock 串行化多实例启动）执行。旧的共享 `DatabaseInitializer` + 内联 `CREATE TABLE IF NOT EXISTS` DDL 路径已于 issue #57 移除。

## 启动流程（三阶段）

1. **目标准备**（`AuditDatabaseTargetPreparer`）：观察解析出的连接串——已有数据库原样使用（不建 maintenance 连接、不需要 CREATEDB 权限）；**可证实缺失**的数据库仅当 `Database:AllowCreate=true`（默认 `false`）时创建，否则以固定错误码 `RUOYU_ADMIN_DB_CREATION_NOT_ALLOWED` 拒绝启动且零写入；服务器不可达 / 认证 / 权限 / 身份冲突一律拒绝，绝不回退为建库。
2. **迁移编排**（ServiceMantle）：以 ServiceId `ruoyu-admin` 派生的 advisory lock（30 秒获取预算）覆盖初始检查、执行与持锁终检；失败以安全错误码（`migration.lock_*` / `migration.inspection_failed` / `migration.version_too_new` / `migration.execution_failed` / `migration.final_state_invalid`）非零码退出。
3. **执行器**（`AuditMigrationExecutor`）的接管规则：
   - **空库**（无业务表、无历史）→ 执行基线迁移建表；
   - **完整旧库**（两张表齐全、无 `__EFMigrationsHistory`、结构逐列验证通过）→ 仅补安全 backfill（缺失索引的 `CREATE INDEX IF NOT EXISTS`）并在事务中写入基线历史，**数据保留**；
   - **版本过新**（历史含未知迁移 id）或 **结构未知/冲突**（多列、类型/可空性/identity 形态不符、主键/索引不符、部分表）→ 以固定错误码 `RUOYU_ADMIN_DB_SCHEMA_INCOMPATIBLE` 拒绝接管，零写入，不自动修复；
   - 基线结构（列、类型、`GENERATED ALWAYS AS IDENTITY`、默认值、`<表名>_pkey` 主键名、`IX_*` 索引）与旧内联 DDL 逐项一致，由 `AuditMigrationTests` 的黄金对照测试保证。

## 变更历史

| 日期 | 变更内容 | 影响表 |
|------|----------|--------|
| 2026-09-30 | 建立基线迁移 `20260930190608_InitialCreate` 并接入 ServiceMantle 迁移编排，移除内联 DDL 与共享初始化器（issue #57） | `OssAuditRecords`, `OssAuditRuns` |
| [待确认] | 初始创建数据库和表结构（旧内联 DDL 路径） | `OssAuditRecords`, `OssAuditRuns` |

## 注意事项

- 新增或修改迁移必须同步 `AuditMigrationExecutor.KnownMigrationIds` 契约（执行器启动时校验迁移装配与契约一致）。
- 升级前备份是调用方责任；任意手工改动过的库不保证被接管。
- readiness 的 `migrationStatus` 仍来自 `AdminStartupReceipt`：只有编排成功返回后才会标记（见 `AdminHealthSnapshotSource`）。
