# 助教管理

## 概述

Admin Portal 新增助教管理功能，允许管理员在统一入口管理助教账号，包括授予/撤销助教权限、管理助教科目。

## 背景

- 教师管理已通过 TeacherPortalProxyMiddleware 接入 Admin Portal
- 助教 Portal（AssistantWebApi）已有完整的 Admin API（grant/revoke/subjects），但 Admin Portal 未接入
- 助教与学生的关联管理统一在学生管理页"关联账户"对话框完成，不在助教管理页提供

## 目标

1. Admin Portal 可管理助教权限（授予、撤销、科目管理）
2. 与教师管理保持一致的交互模式

## 范围

### 包含

- Admin 后端：AssistantPortalProxyMiddleware，代理 /api/assistant-portal/* → Assistant Portal
- Admin 前端：AssistantView.vue 页面 + assistantPortalApi.ts 客户端

### 不包含

- 助教-学生关联管理（统一在学生管理页"关联账户"对话框完成，详见 [StudentManagement/AccountLinking](../../StudentManagement/AccountLinking/01-FEATURE.md)）
- 助教 Portal 前端改造（独立迭代）

## 已移除功能

- **助教-学生关联**：原设计在助教管理页提供"关联学生"列和管理按钮。已于 2026-07-27 删除前后端实现，关联管理统一在学生管理页完成。
- **关联账户管理（PUT /api/admin/assistants/{userId}/user-id）**：原设计提供"关联/更换 Identity 账户"功能。已于 2026-07-27 删除。
