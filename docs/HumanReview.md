# Human Review

> 仅保留本轮结束后仍需人工确认/决策的事项。已完成项和历史记录不在此保留。

## 1. 仍需人工确认事项

### HR-01: 前端测试框架缺失

- **问题**：项目无前端测试框架配置（无 Vitest/Jest 等），前端 Vue 组件和 API 调用逻辑无法通过自动化测试验证
- **已核查证据**：本轮已完成后端 API 全部 UT 补齐（180+ 测试通过），前端代码变更已通过代码审查确认正确性
- **为何仍需人工**：需决策是否引入前端测试框架及选择哪个框架
- **建议下一步**：评估引入 Vitest + Vue Test Utils 的成本收益，若决定引入可按模块逐步补齐

### HR-02: AssignUploadRecord 并发与幂等性

- **问题**：该方法涉及多步骤 gRPC 调用（CompleteUploadReview → SubmitMistake × N），极端并发场景下的幂等性需集成测试验证
- **已核查证据**：已编写 28 个 UT 覆盖正常路径、部分失败路径、异常路径；已验证单线程下的正确性
- **为何仍需人工**：需运行中的 gRPC 服务端和数据库实例进行集成测试
- **建议下一步**：在 staging 环境中执行并发提交测试，验证重复提交不会产生重复错题记录

## 2. 风险提示

- OssAuditWorker 并发控制在 SQLite 下行为可能不同（依赖 DbUpdateException），生产环境使用 PostgreSQL 时需确认行为一致
- gRPC 合约引用方式已改为直接 proto 引用 + ProtoRoot，若 Contract 项目新增 proto 文件需同步更新 Admin.WebApi.csproj
