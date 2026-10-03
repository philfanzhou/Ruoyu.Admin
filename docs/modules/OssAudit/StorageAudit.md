# Storage Audit

Storage Audit 使用 Study 主责的 [StorageReferences v1](https://gitee.com/philfanzhou/Ruoyu.Study/blob/master/docs/integrations/StorageReferences.md) 及其[机器合同](https://gitee.com/philfanzhou/Ruoyu.Study/blob/master/docs/integrations/storage-references-v1.schema.json)。本仓库仅维护消费者责任和手写DTO镜像，不另立provider语义。引用全集、canonical/hash、专属RS256、页/expiry及三提供方迁移由该同版本合同定义。

## 观察与持久化

shared `StorageReferenceCollector` 收齐Student/Mistake/Homework三个完整metadata与所有页，验证版本/身份/coverage/count/排序/唯一/终页/摘要/有效期以及恒false permit。任一依赖错误、redirect、TLS、timeout、取消或坏合同都整体失败，无旧offset/gRPC/空集fallback；必须在任何S3列举前完成。named HttpClient禁止redirect，使用正常CA/SAN，不接受HTTP或任意路由签发。实际S3列举错误必须抛出，不作为成功空集。

worker只列举 `uploads/` 与 `mistakes/`，继续跳过现有推导缩略图。Questions/Documents没有完整引用来源，不进入扫描。完整扫描在一个DB事务提交全部新观察和完成Run；失败回滚本轮观察，Run.Status=2、固定安全error。Run.Status=0/1/2为运行/完成/失败，保存三方 ReferenceSnapshots metadata JSON与ReferenceContractVersion=`storage-references-v1`。NewZombieCount兼容字段在新版本表示新增观察数，旧Run保留原历史含义，不回填新版本证据。

| Record.Status | 含义与操作 |
|---|---|
| 0 Pending | 历史待处理；仅可按原ignore保存备注，不能删除 |
| 1 Resolved | 历史已删除状态；保留原记录，不能据此再删除 |
| 2 Ignored | 历史已忽略；保留原记录 |
| 3 UnreferencedObservation | “未观察到引用”，只读报告，不可选/resolve/ignore |

已有任意状态同路径记录不重复创建。历史review派生图和questions/documents记录不在startup删除；三个旧cleanup helper明确零副作用。capture之后新业务引用可能出现，因此新报告不是孤儿判定或删除授权，`deletionAuthorized=false`不受配置、请求或旧状态影响。

## HTTP 与当前界面

现有认证/浏览器CSRF边界保持，见 [API认证](../../api.md)。GET records保留字段、支持分页/status/bucket；statusCounts包含3，bucketCounts统计0/3。GET status新增observationCount与常量false，lastCompleted新增v1与三metadata；失败不公开内部异常。

非空 single/batch resolve始终经过同shared collector，包括旧0/1/2、新3和部分/全missing batch：依赖不全502 references_unavailable，完整v1固定409 cleanup_not_authorized，零Delete/Copy/审计record删除。未知single404，空batch400。ignore仅旧status0合法，其余400。trigger仍沿现有进程内Task.Run与worker调度，运行中拒绝；可靠排队/进程退出恢复与扩展策略仍见[现有未决项](../../pending-decisions.md)，本能力不把该触发方式宣传为持久作业系统。

当前OSS审计视图不显示选择/删除/批量清理。新Run显示“未观察到引用”和完整三provider的条数/捕获时间；旧Run显示“历史发现”，不编造新版本证据。统计区分旧“历史待处理”与新观察数；旧Pending仅ignore记备注。错误态清空列表并可重试，扫描完成自动刷新；搜索仍仅过滤当前页。UI详细规范见 [frontend-spec](../../../frontend/docs/frontend-spec.md#6-oss-审计页面-oss-audit)。

## 配置、升级、回退

`StorageReferences:Enabled`默认false；启用时必须有issuer/audience/subject、唯一KeyId、0600私钥只读mount，以及三个固定HTTPS根BaseUrl。值与provider配置、轮换、正常TLS及秘密外置规则沿唯一上游合同。不得借Mistake signer/普通用户JWT或回退旧客户端。关闭不释放历史pins，也不解除resolve拒删。

EF known链在原baseline后追加 `20261003030002_AddReferenceObservations`，Run新增两个nullable text列；旧合法无history库先验证旧baseline、stamp，再迁移。严格prefix、unknown拒绝和原结构分类不放宽。新metadata/status3非空时Down拒绝。细节见[迁移](../../database/migrations.md)。

只在自有环境先升级provider→部署新consumer→配正常TLS/独立密钥→同开报告。完整pg_dump包含Runs/Records/history，恢复到fresh owned DB，核对原facts与实际report，不清理历史对象。停新能力保留数据；禁止把旧destructiveconsumer作为清理回滚。未覆盖physicalGC、SourceReleased、history GC、全局writer/collector删除栅栏，不接管共享栈。

## 验证策略与命令

Controller覆盖所有状态与missing IDs的共同门禁；collector覆盖三provider精确HTTP/签名及全部页故障/边界；worker覆盖完整后列举、Status3、S3错误/取消回滚、startup零副作用；真实PG测试覆盖exact旧/新prefix、legacy验证后升级、非空Down拒绝。前端使用真实Element Plus DOM验证当前视图无删除/选择，三provider proof与历史展示。实际联合部署须固定双方head与镜像digest，使用三个生产Host/PG15.18/Seaweed4.48/正常CA+RS256、真实worker/controller/当前UI与完整backup/restore。unit故障替身不代替该实际验收。

```bash
cd backend
dotnet build Ruoyu.Admin.sln --configuration Release
dotnet test Ruoyu.Admin.sln --configuration Release --no-build
# ADMIN_MIGRATION_TEST_CONNECTION 从自有受限文件导入；未提供时使用原Testcontainers。
cd ../frontend
npm ci
npm run build
npm test
```
