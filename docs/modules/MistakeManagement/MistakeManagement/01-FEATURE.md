# 错题管理 (MistakeManagement)

## 功能名称和一句话概括

**MistakeManagement** — 管理员查看、编辑错题记录及迁移错题图片存储路径。

## 核心用户故事

作为管理员，我希望查看和编辑错题记录并迁移图片存储路径，以便维护错题数据的准确性和存储一致性。

## 补充约束

1. **幂等性**：图片迁移为幂等操作，所有路径已在 `mistakes/` 下时返回 migratedCount=0
2. **并发**：迁移操作通过 `pathMapping` 字典缓存同一请求内相同旧路径只迁移一次，但跨请求无并发控制
3. **事务边界**：单张图片迁移失败时不中断整体流程，仅记录 Error 日志并保留原路径；已迁移的图片不会回滚
4. **失败降级**：查询学生姓名失败时回退显示 studentId
5. **分页参数**：page ≤ 0 重置为 1，size ≤ 0 重置为 10，size > 100 限制为 100
6. **迁移路径映射**：由 Student gRPC `MigrateImagesToMistake` 服务端处理（`uploads/xxx` → `mistakes/xxx`），Admin Portal 根据返回结果更新路径
7. **UpdateMistakeItem 可选字段**：StudentId、Subject、Grade 均为可选，未传时使用默认值

## 关键验收条件摘要

1. 可分页浏览错题记录列表，支持按学生、学科、年级、审核状态筛选
2. 可查看单条错题详细信息，包含 sourceRegions 和 boundingBox
3. 可根据上传记录查看关联的所有错题
4. 可编辑错题的学生、学科、年级信息
5. 可将错题图片从 `uploads/` 路径迁移到 `mistakes/` 路径
6. 图片迁移幂等：所有路径已迁移时返回 migratedCount=0
7. 列表接口不返回 sourceRegions 和 boundingBox，仅详情接口返回

## 范围外

- 错题的创建（由上传记录分配产生）
- 错题审核状态变更（由教师端完成）
- 错题的批量删除
- 错题内容的智能识别与标注

## 文档索引

- [02-SPEC.md](./02-SPEC.md) — 详细需求与验收标准清单
- [03-DESIGN.md](./03-DESIGN.md) — 架构设计、接口签名与数据流
- [04-TASKS.md](./04-TASKS.md) — 开发/验证任务列表
- [05-TESTS.md](./05-TESTS.md) — 单元测试与集成测试计划
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — 命名约定、日志安全与代码风格
