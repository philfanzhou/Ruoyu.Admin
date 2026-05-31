# Admin Portal 前端 Spec

## 技术栈

| 项目 | 技术 |
|------|------|
| 框架 | Vue 3 + TypeScript |
| UI 库 | Element Plus |
| HTTP 客户端 | Axios |
| 构建工具 | Vite |
| 路由 | Vue Router 4 |
| 入口 | `src/main.ts` |

---

## 项目结构

```
admin_portal/frontend/src/
├── main.ts                      # 入口，注册 ElementPlus + VueRouter
├── App.vue                      # 根组件，布局容器（含二级菜单）
├── router/
│   └── index.ts                 # 路由配置
├── views/
│   ├── StudentView.vue          # 学生管理页面
│   ├── TeacherView.vue          # 教师管理页面
│   ├── UploadRecordView.vue      # 上传记录管理页面（含遗留数据检查清理）
│   ├── MistakeView.vue           # 错题管理页面
│   └── OssAuditView.vue          # OSS 审计页面
├── services/
│   ├── identityApi.ts           # Identity 用户管理 API
│   ├── studentAdminApi.ts       # 学生管理 + 上传记录 + 错题查询 API
│   ├── teacherPortalApi.ts      # 教师权限管理 API
│   └── ossAuditApi.ts           # OSS 审计 API
└── components/
    ├── ImagePreview.vue          # 图片预览组件
    └── ImageViewer.vue           # 图片查看器组件
```

---

## 页面架构

### 整体布局

```
┌──────────────────────────────────────────────────────────┐
│  Header: 应用标题 + 导航菜单                              │
├────────────┬─────────────────────────────────────────────┤
│            │                                             │
│  Sidebar   │         Main Content Area                  │
│  导航菜单   │         (根据路由显示对应页面)               │
│            │                                             │
│  - 学生管理 │                                             │
│  - 教师管理 │                                             │
│  - 数据管理 │ (可展开)                                    │
│    ├─上传记录 │                                           │
│    └─OSS审计 │                                             │
│  - 错题管理 │                                             │
│            │                                             │
└────────────┴─────────────────────────────────────────────┘
```

### 页面清单

| 路由 | 页面 | 所属分组 | 说明 |
|------|------|----------|------|
| `/students` | StudentView.vue | - | 学生管理 |
| `/teachers` | TeacherView.vue | - | 教师管理 |
| `/upload-records` | UploadRecordView.vue | 数据管理 | 上传记录管理（含遗留数据检查清理） |
| `/mistakes` | MistakeView.vue | - | 错题查询与管理 |
| `/oss-audit` | OssAuditView.vue | 数据管理 | OSS 僵尸文件审计 |

---

## 页面详细说明

### 1. 学生管理页面 (`/students`)

**功能列表**：
- 创建学生（姓名、年级、关联账户）
- 学生列表（搜索、年级筛选、分页）
- 编辑学生（修改信息、管理关联账户、管理开放学科）
- 删除学生

**API 调用**（按需加载）：
- `getGrades()` - 加载年级选项
- `getStudents(params)` - 查询学生列表
- `getIdentityAccountsByStudentId(id)` - 获取关联账户
- `createStudent(payload)` - 创建学生
- `updateStudent(id, payload)` - 更新学生
- `deleteStudent(id)` - 删除学生
- `linkIdentityAccount(id, accountId)` - 关联账户
- `unlinkIdentityAccount(id, accountId)` - 取消关联
- `getStudentOpenSubjects(id, activeOnly)` - 获取开放学科
- `setStudentOpenSubjects(id, payload)` - 设置开放学科

---

### 2. 教师管理页面 (`/teachers`)

**功能列表**：
- 授予教师权限（搜索 Identity 用户 → 选择科目 → 授予）
- 教师列表（查看科目、撤销权限）
- 科目管理（增删教师负责科目）
- 显示账号备注（从 Identity 服务批量获取用户备注信息，方便管理员识别教师身份）

**表格列**：
| 列 | 说明 |
|------|------|
| User ID | Identity 用户唯一标识 |
| 手机号 | 用户手机号 |
| 用户名 | 用户名 |
| 备注 | 从 Identity 服务获取的账号备注，帮助管理员识别教师身份 |
| 科目 | 教师负责的科目标签（可删除、可管理） |
| 创建时间 | 教师记录创建时间 |
| 更新时间 | 教师记录更新时间 |
| 操作 | 撤销权限 |

**数据加载流程**：
1. 调用 `getTeachers()` 获取教师列表
2. 收集所有教师的 `userId`，调用 `getUsersByIds(ids)` 批量获取 Identity 用户信息
3. 通过 `userId` 映射，在表格中显示备注等 Identity 用户字段

**API 调用**（按需加载）：
- `getUsers(params)` - 搜索 Identity 用户
- `getTeachers()` - 获取教师列表
- `addTeacherByUserId(payload)` - 新增教师
- `grantTeacher(userId, payload)` - 授予权限
- `removeTeacherByUserId(userId)` - 撤销权限
- `getTeacherSubjects(userId)` - 获取教师科目
- `setTeacherSubjects(userId, subjects)` - 设置科目
- `getAvailableSubjects()` - 获取可用科目

---

### 3. 上传记录管理页面 (`/upload-records`)

**所属分组**：数据管理

**功能列表**：
- 上传记录列表（学生搜索筛选、状态筛选、分页）
- 总记录数显示
- 缩略图列（显示第一张图片缩略图，点击可预览所有图片）
- 查看详情（图片缩略图、旋转、预览大图）
- 指派记录给学生
- 重置状态
- 旋转图片
- 遗留数据检查与清理（逐条检查）

#### 遗留数据检查清理

**问题背景**：系统早期存在一个 Bug：教师审核错题通过后，上传记录没有被删除，图片路径也没有迁移（仍在 `uploads/` 而非 `mistakes/`）。这些遗留数据需要安全清理。

**设计思路**：不再使用独立的全量扫描页面，而是在每条上传记录的操作列提供"检查清理"按钮，逐条检查该记录是否为遗留数据。这样做的好处是：
1. 每次只检查一条上传记录，而非全量扫描
2. 管理员可以在浏览上传记录时随时检查可疑记录
3. 减少不必要的全量扫描开销

**检查逻辑**：

| 条件 | 判定 | 返回信息 |
|------|------|----------|
| 无关联错题 | 非遗留 | "该记录没有关联错题" |
| 全部错题已审核（Confirmed/Rejected）且无待审核 | **遗留数据** | isLegacy=true, mistakeCount, hasUploadPathImage |
| 存在待审核错题 | 非遗留 | "该记录还有N条错题待审核" |

**清理流程**：

```
管理员点击"检查清理"按钮
    ↓
调用 GET /api/admin/oss-upload-records/legacy-check/{id}
    ↓
显示检查结果对话框：
    - 非遗留：显示提示信息，仅"关闭"按钮
    - 遗留数据：显示错题数量、图片迁移状态、清理操作说明
    ↓
（遗留数据时）管理员点击"确认清理"
    ↓
调用 POST /api/admin/oss-upload-records/legacy-clean/{id}
    ↓
后端处理流程：
┌─────────────────────────────────────────────────────┐
│ 1. 验证是否为遗留数据（错题全部已审核）               │
│    - 不满足条件则中止并返回错误                        │
└─────────────────────────────────────────────────────┘
    ↓
┌─────────────────────────────────────────────────────┐
│ 2. CompleteUploadReview（gRPC）                      │
│    - 迁移物理文件：uploads/ → mistakes/              │
│    - 更新错题记录中的图片路径                          │
│    - 保存到数据库                                      │
└─────────────────────────────────────────────────────┘
    ↓
┌─────────────────────────────────────────────────────┐
│ 3. DeleteUploadRecordAfterReview（gRPC）             │
│    - 删除上传记录                                      │
│    - 清理关联数据                                      │
└─────────────────────────────────────────────────────┘
    ↓
返回成功 → 刷新列表
```

**关键约束**：

1. **不直接操作数据库**：所有操作通过 gRPC 业务接口完成
2. **先验证后清理**：清理前必须先通过 legacy-check 验证
3. **不可逆操作**：删除前需管理员二次确认
4. **原子性**：图片迁移和路径更新在同一次请求中完成

**API 调用**（按需加载）：
- `getUploadRecords(params)` - 查询上传记录
- `getSubjectOptions()` - 学科选项
- `getClassificationOptions()` - 分类选项
- `getGrades()` - 年级选项
- `getStudents(params)` - 搜索学生（用于指派）
- `getUploadRecord(id)` - 获取单条记录
- `assignUploadRecord(id, payload)` - 指派记录
- `resetUploadRecordStatus(id, status)` - 重置状态
- `rotateUploadImage(id, payload)` - 旋转图片
- `legacyCheck(id)` - 检查单条上传记录是否为遗留数据
- `legacyClean(id)` - 清理单条遗留数据

---

### 4. 错题管理页面 (`/mistakes`)

**功能列表**：
- 错题列表（学生搜索、学科/年级/审核状态筛选、分页）
- 总记录数显示
- 查看详情（图片、完整信息）
- 编辑错题（修改学生、学科、年级）

**API 调用**（按需加载）：
- `getStudents(params)` - 搜索学生
- `getSubjectOptions()` - 学科选项
- `getGrades()` - 年级选项
- `getEnumOptions()` - 枚举选项（含审核状态）
- `getMistakeItems(params)` - 查询错题列表
- `getMistakeItem(id)` - 获取单个错题
- `updateMistakeItem(id, data)` - 更新错题

---

### 5. OSS 审计页面 (`/oss-audit`)

**所属分组**：数据管理

**功能列表**：
- 审计记录列表（状态筛选、Bucket 筛选、分页）
- 总记录数显示
- 触发审计（全量 OSS 扫描）
- 单条处理（删除僵尸文件/忽略）
- 批量处理（勾选多条后批量删除）

**API 调用**（按需加载）：
- `getRecords(params)` - 查询审计记录
- `triggerAudit()` - 触发审计
- `resolveRecord(id)` - 处理单条
- `ignoreRecord(id, note)` - 忽略单条
- `batchResolve(ids)` - 批量处理

**状态**：待处理(0) / 已删除(1) / 已忽略(2)

---

## API 服务层

### studentAdminApi.ts

| 方法 | HTTP | 路径 | 说明 |
|------|------|------|------|
| `getGrades()` | GET | `/api/admin/students/grades` | 获取年级选项 |
| `getStudents(params)` | GET | `/api/admin/students` | 分页查询学生 |
| `getStudent(id)` | GET | `/api/admin/students/:id` | 获取单个学生 |
| `createStudent(payload)` | POST | `/api/admin/students` | 创建学生 |
| `updateStudent(id, payload)` | PUT | `/api/admin/students/:id` | 更新学生 |
| `deleteStudent(id)` | DELETE | `/api/admin/students/:id` | 删除学生 |
| `getIdentityAccountsByStudentId(id)` | GET | `/api/admin/students/:id/accounts` | 获取关联账户 |
| `linkIdentityAccount(id, accountId)` | POST | `/api/admin/students/:id/accounts` | 关联账户 |
| `unlinkIdentityAccount(id, accountId)` | DELETE | `/api/admin/students/:id/accounts/:accountId` | 取消关联 |
| `getStudentsByAccountId(accountId)` | GET | `/api/admin/accounts/:accountId/students` | 按账户查学生 |
| `getIdentityAccountsBatch(ids)` | POST | `/api/admin/identity-accounts/batch` | 批量获取账户 |
| `getSubjectOptions()` | GET | `/api/admin/students/subject-options` | 学科选项 |
| `getClassificationOptions()` | GET | `/api/admin/oss-upload-records/classification-options` | 分类选项 |
| `getStudentOpenSubjects(id, activeOnly)` | GET | `/api/admin/students/:id/open-subjects` | 开放学科 |
| `setStudentOpenSubjects(id, payload)` | PUT | `/api/admin/students/:id/open-subjects` | 设置开放学科 |
| `getUploadRecords(params)` | GET | `/api/admin/oss-upload-records` | 查询上传记录 |
| `getUploadRecord(id)` | GET | `/api/admin/oss-upload-records/:id` | 获取单条上传记录 |
| `resetUploadRecordStatus(id, status)` | POST | `/api/admin/oss-upload-records/:id/reset-status` | 重置状态 |
| `assignUploadRecord(id, payload)` | POST | `/api/admin/oss-upload-records/:id/assign` | 指派记录 |
| `rotateUploadImage(id, payload)` | POST | `/api/admin/oss-upload-records/:id/rotate` | 旋转图片 |
| `getMistakeItems(params)` | GET | `/api/admin/mistakes` | 查询错题列表 |
| `getMistakeItem(id)` | GET | `/api/admin/mistakes/:id` | 获取单个错题 |
| `updateMistakeItem(id, data)` | PUT | `/api/admin/mistakes/:id` | 更新错题 |
| `getMistakesByUploadId(uploadId)` | GET | `/api/admin/mistakes/by-upload/:uploadId` | 按上传记录查错题 |
| `getEnumOptions()` | GET | `/api/admin/enum-options` | 获取枚举选项 |
| `legacyCheck(id)` | GET | `/api/admin/oss-upload-records/legacy-check/:id` | 检查单条上传记录是否为遗留数据 |
| `legacyClean(id)` | POST | `/api/admin/oss-upload-records/legacy-clean/:id` | 清理单条遗留数据 |

### identityApi.ts

| 方法 | HTTP | 路径 | 说明 |
|------|------|------|------|
| `testConnection()` | GET | `/api/identity/gateway/users/search` | 测试连通性 |
| `getUsers(params)` | GET | `/api/identity/gateway/users/search` | 搜索用户 |
| `getUsersByIds(ids)` | POST | `/api/identity/gateway/users/batch` | 批量获取用户 |

### teacherPortalApi.ts

| 方法 | HTTP | 路径 | 说明 |
|------|------|------|------|
| `getTeachers()` | GET | `/api/teacher-portal/admin/teachers` | 获取教师列表 |
| `addTeacherByUserId(payload)` | POST | `/api/teacher-portal/admin/teachers/by-user` | 新增教师 |
| `grantTeacher(userId, payload)` | POST | `/api/teacher-portal/admin/identity-users/:userId/grant-teacher` | 授予权限 |
| `removeTeacherByUserId(userId)` | DELETE | `/api/teacher-portal/admin/teachers/by-user/:userId` | 撤销权限 |
| `checkTeacher(userId, phone?)` | GET | `/api/teacher-portal/auth/check-teacher` | 检查教师身份 |
| `getTeacherSubjects(userId)` | GET | `/api/teacher-portal/admin/teachers/:userId/subjects` | 获取教师科目 |
| `setTeacherSubjects(userId, subjects)` | PUT | `/api/teacher-portal/admin/teachers/:userId/subjects` | 设置科目 |
| `addTeacherSubject(userId, subject)` | POST | `/api/teacher-portal/admin/teachers/:userId/subjects/:subject` | 添加科目 |
| `removeTeacherSubject(userId, subject)` | DELETE | `/api/teacher-portal/admin/teachers/:userId/subjects/:subject` | 移除科目 |
| `getAvailableSubjects()` | GET | `/api/teacher-portal/admin/available-subjects` | 可用科目 |

### ossAuditApi.ts

| 方法 | HTTP | 路径 | 说明 |
|------|------|------|------|
| `getRecords(params)` | GET | `/api/admin/oss-audit/records` | 查询审计记录 |
| `triggerAudit()` | POST | `/api/admin/oss-audit/trigger` | 触发审计 |
| `resolveRecord(id)` | POST | `/api/admin/oss-audit/records/:id/resolve` | 处理单条 |
| `ignoreRecord(id, note)` | POST | `/api/admin/oss-audit/records/:id/ignore` | 忽略单条 |
| `batchResolve(ids)` | POST | `/api/admin/oss-audit/records/batch-resolve` | 批量处理 |

---

## 核心类型定义

```typescript
interface PaginatedResponse<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

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

interface LegacyCheckResult {
  isLegacy: boolean;
  mistakeCount?: number;
  hasUploadPathImage?: boolean;
  message: string;
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

## 设计规范

- **UI 框架**：Element Plus，使用 `size="small"` 紧凑模式
- **布局**：侧边栏导航（含二级菜单）+ 主内容区，各页面独立
- **分页**：统一每页 20 条
- **懒加载路由**：按需加载页面组件
- **按需调用 API**：每个页面只调用自己需要的接口
- **错误处理**：Axios 错误统一提取 `response.data.message`，使用 `ElMessage.error` 提示
- **确认操作**：删除等危险操作使用 `ElMessageBox.confirm` 二次确认
- **认证**：依赖后端 Cookie/Session，前端 Axios 实例未设置全局 Authorization Header
