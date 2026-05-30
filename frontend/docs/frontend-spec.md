# Admin Portal 前端 Spec

## 技术栈

| 项目 | 技术 |
|------|------|
| 框架 | Vue 3 + TypeScript |
| UI 库 | Element Plus |
| HTTP 客户端 | Axios |
| 构建工具 | Vite |
| 入口 | `src/main.ts` |

---

## 项目结构

```
admin_portal/frontend/src/
├── App.vue                    # 单文件应用，包含所有面板和对话框
├── main.ts                    # 入口，注册 ElementPlus
└── services/
    ├── identityApi.ts         # Identity 用户管理 API
    ├── studentAdminApi.ts     # 学生管理 + 上传记录 + 错题查询 API
    ├── teacherPortalApi.ts    # 教师权限管理 API
    └── ossAuditApi.ts         # OSS 僵尸文件审计 API
```

> **架构说明**：Admin Portal 采用单组件架构（Monolithic SFC），所有面板和对话框均内联在 `App.vue` 中，无路由、无状态管理库、无独立组件文件。

---

## 页面布局

### 整体结构

```
┌──────────────────────────────────────────────────────────┐
│  Header: 应用标题 + 副标题                                 │
├──────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌──────────────────────────────────┐  │
│  │ 创建学生     │  │ 学生列表                          │  │
│  │ - 姓名       │  │ - 搜索/年级筛选                   │  │
│  │ - 年级       │  │ - 分页表格                        │  │
│  │ - 关联账户   │  │ - 编辑/删除/开放学科               │  │
│  └─────────────┘  └──────────────────────────────────┘  │
├──────────────────────────────────────────────────────────┤
│  教师权限管理                                             │
│  - 授予教师权限（搜索 Identity 用户 → 授予）              │
│  - 教师列表（科目管理/撤销权限）                           │
├──────────────────────────────────────────────────────────┤
│  上传记录管理                                             │
│  - 状态筛选下拉框                                         │
│  - 记录卡片列表（分页）                                   │
│  - 指派/重置状态/旋转图片                                  │
├──────────────────────────────────────────────────────────┤
│  错题查询                                                 │
│  - 学生搜索 + 学科/年级/审核状态筛选                       │
│  - 错题卡片列表（分页）                                   │
│  - 查看/编辑错题详情                                      │
├──────────────────────────────────────────────────────────┤
│  OSS 僵尸文件审计                                         │
│  - 状态筛选 + Bucket 筛选                                 │
│  - 审计记录表格（分页）                                   │
│  - 触发审计/单条处理/批量处理                              │
└──────────────────────────────────────────────────────────┘
```

---

## 功能模块详细说明

### 1. 学生管理

| 功能 | 说明 |
|------|------|
| 创建学生 | 输入姓名、选择年级、搜索并关联 Identity 账户 |
| 学生列表 | 分页展示，支持姓名搜索和年级筛选 |
| 编辑学生 | 对话框中修改姓名、年级、管理关联账户 |
| 删除学生 | 二次确认后删除 |
| 开放学科管理 | 为学生设置/修改开放学科及起止日期 |
| 关联账户管理 | 搜索 Identity 用户 → 添加/移除关联 |

**关键交互**：
- 搜索 Identity 账户时，同时按 username 和 phone 搜索（phone 格式自动判断）
- 关联账户显示为 Tag 列表，可逐个移除
- 开放学科以复选框列表展示，每个学科可设置起止日期

### 2. 教师权限管理

| 功能 | 说明 |
|------|------|
| 授予权限 | 搜索 Identity 用户 → 选择科目 → 授予教师权限 |
| 教师列表 | 展示所有教师及其负责科目 |
| 科目管理 | 对话框中增删教师负责的科目 |
| 撤销权限 | 删除教师记录 |

**关键交互**：
- 搜索用户时调用 Identity Gateway API
- 授予权限支持两种路径：新增教师（`addTeacherByUserId`）或给已有 Identity 用户授权（`grantTeacher`）
- 科目以 Tag 形式展示，点击管理弹出科目选择对话框

### 3. 上传记录管理

| 功能 | 说明 |
|------|------|
| 记录列表 | 分页展示，默认筛选状态为"失败"(status=4) |
| 状态筛选 | 下拉框选择上传状态 |
| 查看详情 | 对话框展示图片缩略图（支持旋转、预览大图） |
| 指派记录 | 将上传记录指派给学生，设置分类/学科/年级 |
| 重置状态 | 将记录状态重置为指定值 |
| 旋转图片 | 对单张图片执行 90° 旋转 |

**状态枚举**：由后端 `/api/admin/enum-options` 返回

### 4. 错题查询

| 功能 | 说明 |
|------|------|
| 筛选 | 学生姓名搜索 + 学科/年级/审核状态筛选 |
| 列表 | 分页展示错题卡片，显示首图缩略图 |
| 查看详情 | 对话框展示错题完整信息 |
| 编辑错题 | 对话框修改学生、学科、年级 |

**审核状态**：待审核(1) / 已确认(2) / 已驳回(3)

### 5. OSS 僵尸文件审计

| 功能 | 说明 |
|------|------|
| 触发审计 | 手动触发一次全量 OSS 扫描 |
| 记录列表 | 分页展示，支持状态和 Bucket 筛选 |
| 单条处理 | 删除僵尸文件（resolve）或忽略（ignore，可附注） |
| 批量处理 | 勾选多条记录后批量 resolve |

**状态**：待处理(0) / 已删除(1) / 已忽略(2)

---

## API 服务层

### identityApi.ts

| 方法 | HTTP | 路径 | 说明 |
|------|------|------|------|
| `testConnection()` | GET | `/api/identity/gateway/users/search` | 测试 Identity 连通性 |
| `getUsers(params)` | GET | `/api/identity/gateway/users/search` | 搜索用户（username/phone） |
| `getUsersByIds(ids)` | POST | `/api/identity/gateway/users/batch` | 批量获取用户信息 |

### studentAdminApi.ts

| 方法 | HTTP | 路径 | 说明 |
|------|------|------|------|
| `getGrades()` | GET | `/api/admin/students/grades` | 获取年级选项 |
| `getStudents(params)` | GET | `/api/admin/students` | 分页查询学生 |
| `getStudent(id)` | GET | `/api/admin/students/:id` | 获取单个学生 |
| `createStudent(payload)` | POST | `/api/admin/students` | 创建学生 |
| `updateStudent(id, payload)` | PUT | `/api/admin/students/:id` | 更新学生 |
| `deleteStudent(id)` | DELETE | `/api/admin/students/:id` | 删除学生 |
| `getIdentityAccountsByStudentId(id)` | GET | `/api/admin/students/:id/accounts` | 获取学生关联账户ID列表 |
| `linkIdentityAccount(id, accountId)` | POST | `/api/admin/students/:id/accounts` | 关联 Identity 账户 |
| `unlinkIdentityAccount(id, accountId)` | DELETE | `/api/admin/students/:id/accounts/:accountId` | 取消关联 |
| `getStudentsByAccountId(accountId)` | GET | `/api/admin/accounts/:accountId/students` | 按账户查学生 |
| `getIdentityAccountsBatch(ids)` | POST | `/api/admin/identity-accounts/batch` | 批量获取账户信息 |
| `getSubjectOptions()` | GET | `/api/admin/students/subject-options` | 学科选项 |
| `getClassificationOptions()` | GET | `/api/admin/oss-upload-records/classification-options` | 分类选项 |
| `getStudentOpenSubjects(id, activeOnly)` | GET | `/api/admin/students/:id/open-subjects` | 获取开放学科 |
| `setStudentOpenSubjects(id, payload)` | PUT | `/api/admin/students/:id/open-subjects` | 设置开放学科 |
| `getUploadRecords(params)` | GET | `/api/admin/oss-upload-records` | 分页查询上传记录 |
| `resetUploadRecordStatus(id, ...)` | POST | `/api/admin/oss-upload-records/:id/reset-status` | 重置记录状态 |
| `assignUploadRecord(id, payload)` | POST | `/api/admin/oss-upload-records/:id/assign` | 指派记录 |
| `rotateUploadImage(id, payload)` | POST | `/api/admin/oss-upload-records/:id/rotate` | 旋转图片 |
| `getMistakeItems(params)` | GET | `/api/admin/mistakes` | 分页查询错题 |
| `getMistakeItem(id)` | GET | `/api/admin/mistakes/:id` | 获取单个错题 |
| `updateMistakeItem(id, data)` | PUT | `/api/admin/mistakes/:id` | 更新错题 |
| `getMistakesByUploadId(uploadId)` | GET | `/api/admin/mistakes/by-upload/:uploadId` | 按上传记录查错题 |
| `getEnumOptions()` | GET | `/api/admin/enum-options` | 获取所有枚举选项 |

### teacherPortalApi.ts

| 方法 | HTTP | 路径 | 说明 |
|------|------|------|------|
| `getTeachers()` | GET | `/api/teacher-portal/admin/teachers` | 获取教师列表 |
| `addTeacherByUserId(payload)` | POST | `/api/teacher-portal/admin/teachers/by-user` | 新增教师 |
| `grantTeacher(userId, payload)` | POST | `/api/teacher-portal/admin/identity-users/:userId/grant-teacher` | 授予教师权限 |
| `removeTeacherByUserId(userId)` | DELETE | `/api/teacher-portal/admin/teachers/by-user/:userId` | 撤销教师权限 |
| `checkTeacher(userId, phone?)` | GET | `/api/teacher-portal/auth/check-teacher` | 检查教师身份 |
| `getTeacherSubjects(userId)` | GET | `/api/teacher-portal/admin/teachers/:userId/subjects` | 获取教师科目 |
| `setTeacherSubjects(userId, subjects)` | PUT | `/api/teacher-portal/admin/teachers/:userId/subjects` | 设置教师科目 |
| `addTeacherSubject(userId, subject)` | POST | `/api/teacher-portal/admin/teachers/:userId/subjects/:subject` | 添加科目 |
| `removeTeacherSubject(userId, subject)` | DELETE | `/api/teacher-portal/admin/teachers/:userId/subjects/:subject` | 移除科目 |
| `getAvailableSubjects()` | GET | `/api/teacher-portal/admin/available-subjects` | 可用科目列表 |

### ossAuditApi.ts

| 方法 | HTTP | 路径 | 说明 |
|------|------|------|------|
| `getRecords(page, pageSize, status?, bucket?)` | GET | `/api/admin/oss-audit/records` | 分页查询审计记录 |
| `triggerAudit()` | POST | `/api/admin/oss-audit/trigger` | 触发审计 |
| `resolveRecord(id)` | POST | `/api/admin/oss-audit/records/:id/resolve` | 处理单条（删除文件） |
| `ignoreRecord(id, note?)` | POST | `/api/admin/oss-audit/records/:id/ignore` | 忽略单条 |
| `batchResolve(ids)` | POST | `/api/admin/oss-audit/records/batch-resolve` | 批量处理 |

---

## 核心类型定义

```typescript
interface StudentDto {
  id: string; name: string; grade: number;
  identityAccountIds: string[]; createdAt: number; updatedAt: number;
}

interface IdentityUser {
  userId: string; username: string; phone: string;
  isActive: boolean; remark: string; createdAt: number; displayName: string;
}

interface TeacherAccountDto {
  id: number; userId: string | null; phone: string | null;
  username: string | null; subjects: number[]; createdAt: string; updatedAt: string;
}

interface UploadRecordDto {
  id: string; studentId: string; studentName: string; status: number;
  imagePaths: string[]; comments: string; createdAt: number | string;
  updatedAt: number | string; classification: number; subject: number;
  grade: number; imageRotations: number[];
}

interface MistakeItemDto {
  id: string; studentId: string; studentName: string; subject: number;
  grade: number; sourceUploadId: string; reviewStatus: number;
  reviewerId: string; reviewedAt: string; reviewComment: string;
  type: number; questionId: string; createdAt: string; updatedAt: string;
  imageCount: number; firstImagePath: string;
}

interface OssAuditRecordDto {
  id: number; objectPath: string; bucket: string; size: number;
  lastModified: number; status: number; statusText: string;
  createdAt: number; resolvedAt: number | null; note: string | null;
}

interface EnumOptionsResponse {
  uploadStatuses: EnumOption[]; grades: EnumOption[];
  subjects: EnumOption[]; classifications: EnumOption[];
  reviewStatuses: EnumOption[]; mistakeTypes: EnumOption[];
}
```

---

## 对话框清单

| 对话框 | 触发方式 | 功能 |
|--------|---------|------|
| Edit Student | 学生列表点击编辑 | 修改姓名/年级/关联账户/开放学科 |
| 管理教师科目 | 教师列表点击科目 Tag | 增删教师负责科目 |
| 授予教师权限 | 搜索用户后点击授予权限 | 选择科目并授权 |
| 图片预览 | 上传记录/错题中点击缩略图 | 全屏预览大图 |
| 错题详情 | 错题列表点击查看 | 展示错题完整信息和图片 |
| 编辑错题 | 错题详情中点击编辑 | 修改学生/学科/年级 |
| 查看图片 & 指派 | 上传记录点击详情 | 查看图片、指派学生、旋转、重置状态 |

---

## 设计规范

- **UI 框架**：Element Plus，使用 `size="small"` 紧凑模式
- **布局**：单页纵向滚动，各面板使用 `el-card` 分隔
- **分页**：统一每页 20 条
- **错误处理**：Axios 错误统一提取 `response.data.message`，使用 `ElMessage.error` 提示
- **确认操作**：删除等危险操作使用 `ElMessageBox.confirm` 二次确认
- **认证**：依赖后端 Cookie/Session，前端 Axios 实例未设置全局 Authorization Header
