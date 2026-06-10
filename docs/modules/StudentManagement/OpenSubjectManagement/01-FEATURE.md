# 开放科目管理 (OpenSubjectManagement)

## 功能名称和一句话概括

**OpenSubjectManagement** — 管理员配置学生的开放科目及其生效日期范围。

## 核心用户故事

作为管理员，我希望设置和查看学生的开放科目配置，以便控制学生可以学习哪些学科及生效时间。

## 补充约束

1. **幂等性**：SetStudentOpenSubjects 为全量替换语义，多次调用同一列表结果一致（幂等）
2. **并发**：全量替换无显式并发控制，后写入覆盖先写入
3. **事务边界**：设置操作为单次 gRPC 调用，全量替换在服务端完成
4. **失败降级**：科目选项通过 gRPC 获取，服务不可用时返回错误
5. **科目有效性**：科目值必须通过 `SubjectConstants.IsValid` 校验
6. **日期规则**：OpenStartDate 必填且为合法 DateOnly 格式；OpenEndDate 可选，为空表示无限期开放
7. **活跃状态计算**：IsActive 由 gRPC 服务端根据当前日期与 OpenStartDate/OpenEndDate 计算得出

## 关键验收条件摘要

1. 可查看学生的开放科目列表，包含生效日期和活跃状态
2. 可通过 activeOnly=true 仅返回当前活跃的科目
3. 设置学生开放科目为全量替换，传入列表替换该学生所有现有配置
4. 每个科目的 OpenStartDate 必填，OpenEndDate 可选
5. 科目值必须通过 `SubjectConstants.IsValid` 校验
6. IsActive 由服务端根据当前日期与日期范围计算

## 范围外

- 单个科目的增量添加/删除（仅支持全量替换）
- 科目开放日期的部分更新
- 开放科目的审批流程
- 科目定义的管理（由 Student 服务维护）

## 文档索引

- [02-SPEC.md](./02-SPEC.md) — 详细需求与验收标准清单
- [03-DESIGN.md](./03-DESIGN.md) — 架构设计、接口签名与数据流
- [04-TASKS.md](./04-TASKS.md) — 开发/验证任务列表
- [05-TESTS.md](./05-TESTS.md) — 单元测试与集成测试计划
- [06-CONVENTIONS.md](./06-CONVENTIONS.md) — 命名约定、日志安全与代码风格
