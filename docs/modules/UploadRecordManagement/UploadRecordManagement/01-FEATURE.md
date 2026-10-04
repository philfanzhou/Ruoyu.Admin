# 上传记录管理 (UploadRecordManagement)

## 功能名称和一句话概括

**UploadRecordManagement** — 管理员查看、审核学生上传记录并将图片分配为错题条目。

## 核心用户故事

作为管理员，我希望查看和管理学生的上传记录并将图片分配为错题条目，以便完成上传图片的审核与错题归档。

## 补充约束

1. **幂等性**：legacyfalse 兼容原入口；managed-v1 固定 revision/requestKey/payload，同组重放返回 Mistake 保存的原 IDs。
2. **并发**：managed 由 Mistake canonical alias 与来源 lease/guard 决定，不能只按来源去重。当前 upload wire 仅 whole-image paths；共享原图不同 crop 的原验收存在明确契约前置阻塞，见集成契约。
3. **事务边界**：逐组 HTTP 调用，不提供整批跨请求原子性；部分/Unknown 保留原请求并由用户显式重试。
4. **失败降级**：查询学生姓名失败时回退显示 studentId
5. **已审核图片保护**：已审核通过的图片不能再次被分配
6. **图片索引**：legacy 取当前记录索引；managed 的索引仅用于可信详情选择，发送精确固定 sourcePaths，后续不按 current 重映射。
7. **跨学科分配**：多个 assignment 可分别指定不同的 subject/grade

## 关键验收条件摘要

1. 可分页浏览所有学生的上传记录，支持按状态和学生筛选
2. 可查看上传记录中的图片
3. 可旋转上传记录中的图片以调整方向
4. 可从上传记录中移除不需要的图片
5. 可将上传记录中的图片按组分配为错题条目，支持跨学科分配
6. 可重置上传记录的状态
7. 可对上传记录进行 VL 分析，自动识别图片分组和学科/年级
8. 已审核通过的图片不能再次被分配

## 范围外

- 上传记录的创建（由学生端完成）
- 错题条目的审核确认（由教师端完成）
- VL 模型的训练与优化
- 上传记录的批量导出

## 文档索引

- [受管指派集成契约](../../../Integration/ManagedAssignment.md) — 配置、可信 metadata、managed HTTP、结果与恢复边界

- [02-SPEC.md](./02-SPEC.md) — 详细需求与验收标准清单
- [03-DESIGN.md](./03-DESIGN.md) — 架构设计、接口签名与数据流
- [04-TASKS.md](./04-TASKS.md) — 开发/验证任务列表
- [05-TESTS.md](./05-TESTS.md) — 单元测试与集成测试计划
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — 命名约定、日志安全与代码风格
