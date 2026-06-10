# 功能域索引

本目录按业务域组织 Admin Portal 的内部业务功能文档。每个功能点包含 6 件套（01-FEATURE → 06-CONVENTIONS），覆盖从需求到测试的完整开发信息。

> **本目录只包含 Admin Portal 自身的业务功能。** 外部系统代理逻辑（Identity Proxy、Teacher Portal Proxy）在 [Integration/](../Integration/) 中维护；数据库结构在 [database/](../database/) 中维护。

## 业务域清单

| 业务域 | 说明 | 功能点 |
|--------|------|--------|
| StudentManagement | 学生档案管理 | 3 个功能点 |
| UploadRecordManagement | 上传记录管理 | 2 个功能点 |
| MistakeManagement | 错题管理 | 1 个功能点 |
| OssAudit | OSS 僵尸文件审计 | 1 个功能点 |

---

## StudentManagement（学生管理）

| 功能点 | 核心用户故事 | 文档入口 |
|--------|-------------|----------|
| StudentCRUD | 管理学生档案（增删改查） | [01-FEATURE](./StudentManagement/StudentCRUD/01-FEATURE.md) |
| AccountLinking | 关联/解除身份账户 | [01-FEATURE](./StudentManagement/AccountLinking/01-FEATURE.md) |
| OpenSubjectManagement | 管理学生开放科目 | [01-FEATURE](./StudentManagement/OpenSubjectManagement/01-FEATURE.md) |

## UploadRecordManagement（上传记录管理）

| 功能点 | 核心用户故事 | 文档入口 |
|--------|-------------|----------|
| UploadRecordManagement | 查看/分配/操作上传记录 | [01-FEATURE](./UploadRecordManagement/UploadRecordManagement/01-FEATURE.md) |
| LegacyDataCleanup | 检查和清理遗留数据 | [01-FEATURE](./UploadRecordManagement/LegacyDataCleanup/01-FEATURE.md) |

## MistakeManagement（错题管理）

| 功能点 | 核心用户故事 | 文档入口 |
|--------|-------------|----------|
| MistakeManagement | 查看/编辑错题记录 | [01-FEATURE](./MistakeManagement/MistakeManagement/01-FEATURE.md) |

## OssAudit（OSS 审计）

| 功能点 | 核心用户故事 | 文档入口 |
|--------|-------------|----------|
| OssAudit | 发现和清理僵尸文件 | [01-FEATURE](./OssAudit/OssAudit/01-FEATURE.md) |

---

## 功能点内部阅读顺序

进入某个功能点后，按以下顺序阅读 6 件套：

| 顺序 | 文件 | 回答的问题 |
|------|------|-----------|
| 1 | 01-FEATURE.md | 这个功能做什么？不做什么？验收条件是什么？ |
| 2 | 02-SPEC.md | 详细需求是什么？每条需求如何验证？ |
| 3 | 03-DESIGN.md | 代码在哪里？接口签名是什么？调用链是什么？ |
| 4 | 04-TASKS.md | 有哪些待办/待修复/待评审的任务？ |
| 5 | 05-TESTS.md | 已有测试覆盖了什么？还缺什么测试？ |
| 6 | 06-CONVENTIONS.md | 命名、日志、错误消息遵循什么约定？ |

## 文档结构约定

每个功能点包含 6 件套文档：

| 文件 | 内容 |
|------|------|
| 01-FEATURE.md | 功能概述、用户故事、验收条件、范围外 |
| 02-SPEC.md | 详细需求规格、验收标准（Given-When-Then） |
| 03-DESIGN.md | 文件结构、接口签名、数据依赖 |
| 04-TASKS.md | 任务清单（JSON 格式） |
| 05-TESTS.md | 测试计划（Given-When-Then） |
| 06-CONVENTIONS.md | 命名约定、日志规范、错误消息模板 |
