# OSS僵尸文件审计 (OssAudit)

## 功能名称和一句话概括

**OssAudit** — 发现并清理 OSS 存储中未被业务服务引用的僵尸文件。

## 核心用户故事

作为管理员，我希望发现和清理 OSS 存储中未被引用的僵尸文件，以便节省存储空间且不误删有效数据。

## 补充约束

1. **幂等性**：resolve 操作对 Pending 状态记录执行前需再次校验引用，对 Resolved 状态记录跳过校验（幂等）
2. **并发**：审计互斥，同一时间只允许一个审计任务运行，通过 OssAuditRun 记录实现锁机制
3. **事务边界**：单次 resolve/ignore 为单条记录操作；审计扫描为后台长任务，无整体事务
4. **失败即中止**：Student、Homework、Mistake 三个引用来源任一不可达，审计必须中止（`OssAuditRun.Status=2`），且在扫描任何桶之前中止。引用聚合不完整时不得产出可处置结论——审计记录会被 resolve 成真实删除，噪声队列比没有审计更危险
5. **扫描桶范围**：只扫描存在引用来源的桶，当前为 uploads、mistakes（`OssAuditWorker.AuditedBuckets`）。questions（QuestionBank）与 documents（DocLibrary，原件已交由 StructaDoc 主责）没有引用来源，不在审计范围内；启动时会清理历史遗留的这两类误判记录
6. **清理安全校验**：对 Pending 状态记录执行 resolve / batch-resolve 前，需重新聚合 Student + Homework 与 Mistake 的引用路径再验证一次；任一来源不可达时返回 502 拒删。注意这道复核查询的是同一批来源，只能防下游临时故障，无法纠正审计判定本身的错误
7. **缩略图跳过**：审计扫描使用 `ThumbnailHelper.IsThumbnailPath` 跳过缩略图文件，避免将缩略图误报为僵尸文件（缩略图与原图关联，删原图时自动清理）
8. **缩略图自动清理**：`IOssService.DeleteAsync` 自动清理关联缩略图，删除僵尸原图时会连带删除同目录下的所有尺寸缩略图，无需调用方额外处理
9. **无路径前缀校验**：Admin Portal 的 `S3OssService` 不配置路径前缀校验（`allowedPrefixes` 为 null），因为审计浏览与运维操作需要访问任意路径前缀，包括当前不在审计范围内的 questions/、documents/。这与「扫描桶范围」是两件事：前者约束 `IOssService` 能读写哪些路径，后者约束审计扫描并判定哪些路径
10. **缩略图路径规则**：缩略图与原图同目录，路径格式 `{dirname}/{stem}_{size}.jpg`（旧规则 `uploads/thumbnails/` 已废弃）
11. **旧批改图定向清理**：Worker 启动后只自动删除严格匹配 `uploads/homework/{homeworkId}/{studentId}/reviews/{revisionId}.jpg` 的已废弃批改派生图及其残留审计记录；三个 ID 都必须为非空 UUID，删除原图时连带删除缩略图。学生提交原图和其他路径不进入该规则。

## 关键验收条件摘要

1. 可定期自动扫描 OSS 存储，发现未被引用的僵尸文件
2. 可手动触发审计扫描
3. 同一时间只允许一个审计任务运行
4. 可查看审计结果列表，支持按状态和桶筛选
5. 可对僵尸文件执行清理（resolve）或忽略（ignore）操作
6. 清理前需再次校验文件未被引用，防止误删
7. Student 服务不可用时审计中止，Mistake 服务不可用时仅警告
8. 审计扫描跳过缩略图文件，不将缩略图误报为僵尸文件
9. 清理僵尸原图时自动删除关联缩略图
10. Admin API 重启后自动清理旧批改派生图，不要求管理员逐条 resolve

## 范围外

- OSS 存储桶的创建与配置
- 文件上传流程
- 审计结果的导出与报表
- 普通僵尸文件的自动定时清理（旧批改派生图的定向启动清理除外）

## 文档索引

- [02-SPEC.md](./02-SPEC.md) — 详细需求与验收标准清单
- [03-DESIGN.md](./03-DESIGN.md) — 架构设计、接口签名与数据流
- [04-TASKS.md](./04-TASKS.md) — 开发/验证任务列表
- [05-TESTS.md](./05-TESTS.md) — 单元测试与集成测试计划
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — 命名约定、日志安全与代码风格
