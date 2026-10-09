# Storage Audit

Storage Audit 使用 Ruoyu.Study 维护的 [StorageReferences v1](https://gitee.com/philfanzhou/Ruoyu.Study/blob/master/docs/integrations/StorageReferences.md) 及其 [JSON Schema](https://gitee.com/philfanzhou/Ruoyu.Study/blob/master/docs/integrations/storage-references-v1.schema.json)。本仓库只维护 Admin 调用这些接口的规则和手写 DTO 副本。引用范围、摘要的规范编码、专属 RS256 签名、分页、有效期和三个提供服务的迁移要求，均以同一版本的上游接口规范为准。

## 观察与持久化

所有扫描和删除请求共用 `StorageReferenceCollector`。它收齐 Student、Mistake、Homework 三个服务的完整快照元数据（`metadata`）与所有页，验证版本、身份、引用覆盖范围、条数、排序、唯一性、终页、摘要、有效期，以及恒为 `false` 的 `deletionAuthorized` 字段。任一依赖错误、重定向、TLS 错误、超时、取消或响应不符合接口规范，都会使整轮收集失败；不回退到旧 offset 分页、gRPC 或空集合。**这些验证必须在任何 S3 对象列举前全部完成。** 命名 HttpClient 禁止重定向，使用正常 CA 信任链和主机名验证；不接受 HTTP，也不向任意路由签发凭据。S3 列举失败必须抛出错误，不能作为成功的空集合处理。

`OssAuditWorker` 只列举 `uploads/` 与 `mistakes/`，继续跳过现有规则推导的缩略图。`Questions`、`Documents` 没有完整引用来源，不进入扫描。完整扫描在同一个数据库事务中提交全部新观察与已完成的运行记录；失败时回滚本轮观察，将运行记录设为 `Status=2`，只保存固定且不含敏感值的错误原因。运行记录 `Status=0/1/2` 分别表示运行中、完成、失败；保存三份 `ReferenceSnapshots` 元数据 JSON 和 `ReferenceContractVersion="storage-references-v1"`。兼容字段 `NewZombieCount` 在新版本表示新增观察数；旧运行记录保留原历史含义，不补造新版本快照或验证结果。

| Record.Status | 含义与操作 |
|---|---|
| 0 Pending | 历史待处理；仅可按原 `ignore` 操作保存备注，不能删除 |
| 1 Resolved | 历史已删除状态；保留原记录，不能据此再删除 |
| 2 Ignored | 历史已忽略；保留原记录 |
| 3 UnreferencedObservation | “未观察到引用”的只读报告；不可选择、`resolve` 或 `ignore` |

同一路径已有任意状态的记录时，不重复创建。启动时不删除历史 review 派生图或 questions/documents 记录；三个旧清理辅助方法均不产生副作用。采集之后可能出现新的业务引用，因此报告不能判定对象已经成为孤儿，也不授予删除许可。`deletionAuthorized=false` 不受配置、请求或旧状态影响。

## HTTP 与当前界面

现有认证和浏览器 CSRF 规则保持不变，见 [API 认证](../../api.md)。`GET records` 保留字段，支持分页及 status/bucket 筛选；`statusCounts` 包含状态 3，`bucketCounts` 统计状态 0 和 3。`GET status` 新增 `observationCount` 和恒为 `false` 的 `deletionAuthorized`；`lastCompleted` 新增版本字段和三份元数据。失败响应不公开内部异常。

已有单条记录及非空批量 `resolve` 请求始终经过同一个收集器，包括旧状态 0/1/2、新状态 3，以及部分或全部 ID 不存在的批量请求。引用不完整时返回 502 `references_unavailable`；完整 v1 引用验证成功后固定返回 409 `cleanup_not_authorized`。这些请求不执行对象 Delete、Copy 或审计记录删除。未知单条 ID 返回 404，空批量返回 400。`ignore` 仅允许历史状态 0，其他状态返回 400。手动触发仍使用现有进程内 `Task.Run` 与 worker 调度，运行中拒绝再次触发；这不保证持久排队或进程退出后的恢复。可靠排队、恢复与扩展策略见[现有未决项](../../pending-decisions.md)。

当前 OSS 审计视图不显示选择、删除或批量清理操作。新运行记录显示“未观察到引用”及三个引用提供服务的条数和采集时间；旧运行记录显示“历史发现”，不编造新版本验证结果。统计分别展示“历史待处理”和新观察数；旧 Pending 仅可通过 `ignore` 保存备注。请求失败时清空列表并提供重试，扫描完成后自动刷新；搜索仍只过滤当前页。界面规范见 [frontend-spec](../../../frontend/docs/frontend-spec.md#6-oss-审计页面-oss-audit)。

## 配置、升级、回退

`StorageReferences:Enabled` 默认 `false`。启用时必须配置 issuer/audience/subject、唯一 KeyId、权限为 0600 的只读私钥挂载，以及三个固定 HTTPS 根 BaseUrl。配置值、提供服务的配置与密钥轮换、正常 TLS 和凭据外置规则，均遵循上游接口规范。不得复用 Mistake 签名器或普通用户 JWT，也不得回退到旧客户端。关闭能力不释放历史 `pins`：这些是业务来源快照、回执与尝试记录持续保留的对象引用，不是可随审计快照到期丢弃的缓存，也不是删除授权。关闭能力同样不解除 `resolve` 的拒删规则。

EF Core 迁移链在原基线后追加 `20261003030002_AddReferenceObservations`，为运行记录增加两个可空 text 列。无迁移历史的合法旧库，必须先验证旧基线结构，再登记基线迁移历史，随后执行新迁移。迁移历史必须按顺序精确匹配已知迁移编号的前缀；未知迁移和不兼容结构仍须拒绝。已有非空 `ReferenceSnapshots` 或状态 3 记录时，`Down` 拒绝执行。详见[迁移文档](../../database/migrations.md)。

只在由执行者负责的隔离环境中验证升级：先升级引用提供服务，再部署新版 Admin，配置正常 TLS 与独立密钥，最后显式开启双方报告能力。用完整 `pg_dump` 备份数据库，包含运行记录、审计记录和迁移历史；恢复到由执行者创建并负责的新隔离数据库，逐项核对原数据、迁移历史及实际报告，不清理历史对象。停用新能力必须保留数据；禁止通过回滚到旧版可删除对象的 Admin 来执行清理。本能力不提供物理垃圾回收、`SourceReleased`、历史记录清理或业务写入与审计删除之间的全局并发协调，也不接管共享环境。

## 验证策略与命令

Controller 测试覆盖所有记录状态及缺失 ID 的统一引用检查和拒删规则；collector 测试覆盖三个服务的精确 HTTP 请求、签名、所有页的故障和边界；worker 测试覆盖验证完整后才列举、状态 3、S3 错误、取消时回滚及启动时不执行旧清理。真实 PostgreSQL 测试覆盖旧版与新版的精确迁移历史前缀、验证合法旧库后升级，以及已有新观察数据时拒绝 `Down`。前端使用真实 Element Plus DOM 验证无删除或选择操作，并检查三份元数据与历史记录的展示。

实际联合部署验收须记录双方确切提交和镜像 digest，使用三个实际服务宿主、PG 15.18、Seaweed 4.48、正常 CA 信任链与 RS256，以及真实 worker、Controller、当前界面和完整备份恢复。测试环境由执行者创建并负责清理；单元测试的故障替身不能代替实际验收。

```bash
cd backend
dotnet build Ruoyu.Admin.sln --configuration Release
dotnet test Ruoyu.Admin.sln --configuration Release --no-build
# ADMIN_MIGRATION_TEST_CONNECTION 从自有受限文件导入；未提供时使用原 Testcontainers。
cd ../frontend
npm ci
npm run build
npm test
```
