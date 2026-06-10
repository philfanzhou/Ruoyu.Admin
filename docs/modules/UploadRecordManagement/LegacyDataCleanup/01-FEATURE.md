# 遗留数据清理 (LegacyDataCleanup)

## 功能名称和一句话概括

**LegacyDataCleanup** — 管理员检查并清理已审核完毕但仍残留的上传记录及相关图片。

## 核心用户故事

作为管理员，我希望检查和清理遗留的上传记录数据，以便在关联错题审核完毕后及时释放残留的存储和记录。

## 补充约束

1. **幂等性**：LegacyCheck 为幂等查询；LegacyClean 非幂等，重复执行会尝试再次调用删除接口
2. **并发**：无显式并发控制，同一记录同时清理可能导致重复操作
3. **事务边界**：LegacyClean 为多步串行 gRPC 调用（CompleteUploadReview → RemoveImagesFromRecord → DeleteUploadRecordAfterReview），无整体事务，中间步骤失败已执行操作不会回滚
4. **失败降级**：前置条件不满足时返回 400 错误，不执行清理
5. **LegacyCheck 前置条件**：无，即使没有关联错题也可检查（返回 isLegacy=false）
6. **LegacyClean 前置条件**：必须有关联错题且所有错题已审核完毕，否则返回 400
7. **ReviewerId**：LegacyClean 中 CompleteUploadReview 的 ReviewerId 固定为 `"admin-legacy-cleanup"`

## 关键验收条件摘要

1. 可检查上传记录是否属于遗留数据（所有关联错题已审核完毕但记录仍存在）
2. 遗留检查返回 isLegacy、mistakeCount 和 hasUploadPathImage
3. 无关联错题时 isLegacy=false；有待审核错题时 isLegacy=false 并返回 pendingCount
4. 清理操作需满足前置条件：有关联错题且全部已审核完毕
5. 清理流程依次执行：CompleteUploadReview → RemoveImagesFromRecord（可选）→ DeleteUploadRecordAfterReview
6. 可检测遗留数据中是否有图片仍存储在 `uploads/` 路径下
7. studentId 从关联错题中取第一个 item 的 StudentId，为空时返回 400

## 范围外

- 遗留数据的自动定时清理（仅支持手动触发）
- 批量清理多条遗留记录
- 清理操作的撤销/回滚
- 错题审核流程本身（由教师端完成）

## 文档索引

- [02-SPEC.md](./02-SPEC.md) — 详细需求与验收标准清单
- [03-DESIGN.md](./03-DESIGN.md) — 架构设计、接口签名与数据流
- [04-TASKS.md](./04-TASKS.md) — 开发/验证任务列表
- [05-TESTS.md](./05-TESTS.md) — 单元测试与集成测试计划
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — 命名约定、日志安全与代码风格
