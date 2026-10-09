# 迁移历史

## 迁移策略

本项目使用 **EF Core Migrations**（按顺序精确匹配的迁移链 `20260930190608_InitialCreate` → `20261003030002_AddReferenceObservations`），由本仓库维护的 `AuditMigrationExecutor`（`backend/Admin.WebApi/Database/AuditMigrationExecutor.cs`）实现 ServiceMantle 的 `IDatabaseMigrationExecutor` 扩展接口，在启动时由 ServiceMantle 0.3.1-rc.1 的 `StartupDatabaseGate` 调用共享的 `DatabaseMigrationOrchestrator` 执行（通过 PostgreSQL advisory lock 使多个实例依次迁移）。旧的共享 `DatabaseInitializer` + 内联 `CREATE TABLE IF NOT EXISTS` DDL 路径已于 issue #57 移除。

## 启动流程

配置在注册启动检查前由 `AuditDatabaseStartupConfiguration` 解析：`Database:AllowCreate` 缺省/空值为 false，只接受 true/false；空或不可解析的连接、非法开关以 `database_target_preparation.invalid_target` 拒绝，无数据库 I/O，不回显输入。部署固定为 PostgreSQL `MultiInstance`，由共享 `PostgreSqlDatabaseDeploymentCapabilityProvider` 显式声明能力，使用真实 PostgreSQL advisory lock。

使用官方同版 `ServiceMantle 0.3.1-rc.1` 的 PostgreSQL 选项预设与显式能力注册；产品配置仍由本地边界校验，`AddStartupDatabaseGate` 仍是原唯一宿主入口。锁等待和准备预算各 30 秒、健康分类、执行器与迁移不变。共享组件的单实例规范身份（canonical identity）是不含凭据的 TCP 目标 SHA-256 摘要；原本地入口直接拒绝单实例身份。Admin 固定选择多实例模式下的真实锁租约，不调用单实例身份功能，因此这次组件替换不影响实际执行路径。回滚只需恢复应用代码和原同版包，不涉及数据库结构或数据迁移，也不会撤销已经提交的操作。

1. **共享启动门**（`StartupDatabaseGate`）：检查解析出的连接串所指向的数据库——已有数据库原样使用（不建 maintenance 连接、不需要 CREATEDB 权限）；**可证实缺失**的数据库仅当 `Database:AllowCreate=true`（默认 `false`）时创建，否则以固定错误码 `database_target_preparation.creation_not_allowed` 拒绝启动且零写入；服务器不可达 / 认证 / 权限 / 身份冲突一律拒绝，绝不回退为建库。维护连接由共享 `PostgreSqlMaintenanceConnection` 派生，只把数据库名改为 `postgres`，沿用同一凭据；准备预算固定 30 秒，成功后重新检查目标数据库，仅可连接时才继续。
2. **迁移执行顺序与锁**（ServiceMantle）：以 ServiceId `ruoyu-admin` 派生的 advisory lock（30 秒获取预算）覆盖初始检查、执行与持锁终检；失败以安全错误码（`migration.lock_*` / `migration.inspection_failed` / `migration.version_too_new` / `migration.execution_failed` / `migration.final_state_invalid`）非零码退出。
3. **执行器**（`AuditMigrationExecutor`）的接管规则：
   - **空库**（无业务表、无历史）→ 执行已知迁移链建表；
   - **完整旧库**（两张表齐全、无 `__EFMigrationsHistory`、结构逐列验证通过）→ 仅补建缺失索引（`CREATE INDEX IF NOT EXISTS`）并在事务中写入基线历史，再执行新观察迁移，**数据保留**；
   - **版本过新**（历史含未知迁移 id）或 **结构未知/冲突**（多列、类型/可空性/identity 形态不符、主键/索引不符、部分表）→ 以固定错误码 `RUOYU_ADMIN_DB_SCHEMA_INCOMPATIBLE` 拒绝接管，零写入，不自动修复；
   - 基线结构（列、类型、`GENERATED ALWAYS AS IDENTITY`、默认值、`<表名>_pkey` 主键名、`IX_*` 索引）与旧内联 DDL 逐项一致，由 `AuditMigrationTests` 对迁移结果与旧内联 DDL 的逐项比较测试保证。

共享 `IHostedLifecycleService.StartingAsync` 在 WebHost 与 `OssAuditWorker.StartAsync` 之前执行唯一一次启动数据库检查；任一失败只以白名单安全码拒绝宿主启动，不开始监听，也不启动 worker。宿主的取消 token 贯穿所有阶段，取消时抛出 `OperationCanceledException`，不进入后续阶段、不记成功。启动检查本身不新增表或文件，不回滚已建库或已提交的迁移。

## 变更历史

| 日期 | 变更内容 | 影响表 |
|------|----------|--------|
| 2026-10-02 | 升级同版 ServiceMantle 0.3.0，接入共享启动门与 EF Core 健康快照，删除本地数据库准备、启动结果记录和健康快照适配代码（issue #66）；执行器和迁移不变 | 无数据库结构变更 |
| 2026-09-30 | 建立基线迁移 `20260930190608_InitialCreate` 并接入 ServiceMantle 迁移执行组件，移除内联 DDL 与共享初始化器（issue #57） | `OssAuditRecords`, `OssAuditRuns` |
| [待确认] | 初始创建数据库和表结构（旧内联 DDL 路径） | `OssAuditRecords`, `OssAuditRuns` |

## 注意事项

- 新增或修改迁移必须同步 `AuditMigrationExecutor.KnownMigrationIds` 契约（执行器启动时校验程序中的迁移与已知编号清单一致）。
- 升级前必须由执行者备份；任意手工改动过的库不保证被接管。
- readiness 使用共享单例 `StartupDatabaseReceipt` 与 scoped `EfCoreHealthSnapshotSource<AuditDbContext>`（MappedSchema）。NotStarted / Running / Failed 时零数据库访问；Succeeded 时对 EF 映射表列执行只读零行查询，不证明约束、索引或数据正确；每请求重新采样，3 秒预算。

## #66 发布兼容说明

| 事件 | 旧码/状态 | 当前码/状态 |
|---|---|---|
| 缺库，创建默认拒绝 | `RUOYU_ADMIN_DB_CREATION_NOT_ALLOWED` | `database_target_preparation.creation_not_allowed` |
| 非法 `Database:AllowCreate` | `RUOYU_ADMIN_DB_ALLOW_CREATE_INVALID` | `database_target_preparation.invalid_target` |
| 准备后仍不可连接 | 观测失败码（如 `database_target_preparation.connection_failed`） | `database_target_preparation.not_connectable_after_preparation` |
| 新建、未运行回执 | Running | NotStarted，503 `ruoyu-admin.startup_incomplete` |
| 运行中的回执 | Running，503 `ruoyu-admin.startup_incomplete` | 保持 |
| 失败回执 | 未完成，503 `ruoyu-admin.startup_incomplete` | Failed，503 `ruoyu-admin.startup_failed` |
| 取消运行 | 不记成功 | Running，不记成功 |

正常 Succeeded/ready JSON、迁移 `migration.*` 与 `RUOYU_ADMIN_DB_SCHEMA_INCOMPATIBLE`、端口和认证不变。探针由 PostgreSQL classifier 分类；无法分类的异常仍以固定错误码拒绝就绪： `health.probe_failed`，不伪造连接状态。回滚上一版本恢复旧启动/探针码，不回滚数据库结构；不会撤销已创建数据库或已提交迁移。

## StorageReferences v1 观察升级

精确匹配旧基线迁移历史的数据库，以及结构合法但没有迁移历史的旧库，都先按旧基线检查；后者必须登记基线迁移历史后才执行追加迁移。新迁移为 `OssAuditRuns` 增加可空 text 列 `ReferenceContractVersion` 和 `ReferenceSnapshots`。原业务与审计数据不改写，也不为旧记录补造新版引用快照或验证结果。迁移历史必须按顺序精确匹配已知迁移编号的前缀；缺少基线、包含未知编号或结构冲突时拒绝，执行器的 `KnownMigrationIds` 必须同步维护。已有非空 `ReferenceSnapshots` 或状态 3 记录时，`Down` 拒绝执行，以免丢失历史；恢复须使用整个数据库的 `pg_dump`。行为规则见 [StorageAudit](../modules/OssAudit/StorageAudit.md)。

## #67 数据库结构读取与比较组件替换

执行器通过 `PostgreSqlSchemaEvidenceReader`（显式注册的扩展组件，只读 `public` 下两张已知业务表）
读取结构与迁移历史；`EfCoreExpectedSchemaDerivation` 从 EF 设计时模型推导期望，基线期望继续排除
后续迁移的两列。共享比较器检查名称、identity、索引总键数和 INCLUDE；Admin 将读取结果转换为比较所需结构时，继续按原规则
忽略名称大小写、无关附加索引与 foreign key。类型与列名仍精确匹配，不比较默认值。

只缺具名索引的合法旧库继续原 DDL 回填；基线历史由 `EfCoreMigrationBaselineWriter` 在独立事务
中幂等写入，Admin 继续负责先验证结构、持有 advisory lock 租约，并在写入前重新检查。读取器的多次查询不保证得到同一时刻的快照。
迁移清单、结构、配置键和固定错误码不变；回滚应用代码及同版包即可，无新增数据迁移，已提交的
迁移不会随应用回滚撤销。

共享读取器先在不指定业务表的情况下读取迁移历史，优先拒绝未知迁移编号和不符合已知编号顺序的历史，再读取完整业务表结构。即使未知版本同时存在无法表示的零列表，仍返回 `VersionTooNew`。合法迁移历史不能绕过结构检查；拒绝时不补建索引，也不登记基线迁移历史。
