# 助教管理

## 概述

Admin Portal 新增助教管理功能，允许管理员在统一入口管理助教账号，包括授予/撤销助教权限、管理助教科目、以及指定助教关联的学生。

## 背景

- 教师管理已通过 TeacherPortalProxyMiddleware 接入 Admin Portal
- 助教 Portal（AssistantWebApi）已有完整的 Admin API（grant/revoke/subjects），但 Admin Portal 未接入
- 当前助教数据模型只有科目（Subjects），没有学生关联

## 目标

1. Admin Portal 可管理助教权限（授予、撤销、科目管理）
2. Admin Portal 可为助教指定关联学生
3. 与教师管理保持一致的交互模式

## 范围

### 包含

- Admin 后端：AssistantPortalProxyMiddleware，代理 /api/assistant-portal/* → Assistant Portal
- Admin 前端：AssistantView.vue 页面 + assistantPortalApi.ts 客户端
- 助教 Portal 后端：新增 assistant_students 关联表 + 学生关联 API
- 助教 Portal 后端：新增 AdminController 端点管理学生关联

### 不包含

- 教师-学生关联（本次不做）
- 助教 Portal 前端改造（独立迭代）
