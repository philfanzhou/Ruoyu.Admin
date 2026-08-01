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
| 设计规范 | `ruoyu-prototype/assets/design-system.css` 中的 `--adm-*` 令牌 |

---

## 项目结构

```
src/admin_portal/frontend/src/
├── main.ts                      # 入口，注册 ElementPlus + VueRouter
├── App.vue                      # 根组件，主布局（240px 侧边栏 + 56px 顶部栏 + 面包屑）
├── router/
│   └── index.ts                 # 路由配置
├── views/
│   ├── LoginView.vue            # 登录页（双栏：品牌渐变 + 表单）
│   ├── DashboardView.vue        # 数据仪表盘（KPI + 趋势 + 待办）
│   ├── StudentView.vue          # 学生管理页面
│   ├── TeacherView.vue          # 教师管理页面
│   ├── AssistantView.vue        # 助教管理页面
│   ├── UploadRecordView.vue      # 上传记录管理页面（状态统计条 + 侧滑详情面板）
│   ├── MistakeView.vue           # 错题管理页面（统计卡 + 详情 Modal）
│   └── OssAuditView.vue          # OSS 审计页面（状态面板 + 浮动批量操作栏）
├── services/
│   ├── httpClient.ts            # 共享 axios 实例（含 JWT 注入 + 401 重定向拦截器）
│   ├── identityApi.ts           # Identity 用户管理 API
│   ├── studentAdminApi.ts       # 学生管理 + 上传记录 + 错题查询 API
│   ├── teacherPortalApi.ts      # 教师权限管理 API
│   ├── assistantPortalApi.ts    # 助教权限管理 API
│   └── ossAuditApi.ts           # OSS 审计 API
└── components/
    ├── ImagePreview.vue          # 图片预览组件
    ├── ImageViewer.vue           # 图片查看器组件
```

---

## 设计规范（Professional Blue-Gray）

参考原型：`ruoyu-prototype/pages/admin-*.html` + `ruoyu-prototype/assets/design-system.css`。

### Design Tokens（CSS 变量）

所有 `--adm-*` 变量统一定义在 `src/style.css` 的 `:root` 中，供全局使用：

| 类别 | 变量 | 值 |
|------|------|----|
| 主色 | `--adm-primary` | `#3b82f6` (Blue 500) |
| 主色深 | `--adm-primary-dark` | `#2563eb` |
| 主色浅 | `--adm-primary-bg` | `#eff6ff` |
| 侧边栏 | `--adm-sidebar` | `#1e293b` |
| 侧边栏悬停 | `--adm-sidebar-hover` | `#334155` |
| 内容区背景 | `--adm-surface` | `#f1f5f9` |
| 卡片背景 | `--adm-surface-elevated` | `#ffffff` |
| 次级背景 | `--adm-surface-subtle` | `#f8fafc` |
| 边框 | `--adm-border` | `#e2e8f0` |
| 边框浅 | `--adm-border-light` | `#f1f5f9` |
| 主文本 | `--adm-text-primary` | `#0f172a` |
| 次文本 | `--adm-text-secondary` | `#475569` |
| 三级文本 | `--adm-text-tertiary` | `#64748b` |
| 静音文本 | `--adm-text-muted` | `#94a3b8` |
| 暗背景文本 | `--adm-text-on-dark` | `#e2e8f0` |
| 暗背景静音 | `--adm-text-on-dark-muted` | `#94a3b8` |
| 成功 | `--adm-success` / `*-bg` / `*-border` | `#10b981` / `#ecfdf5` / `#a7f3d0` |
| 警告 | `--adm-warning` / `*-bg` / `*-border` | `#f59e0b` / `#fffbeb` / `#fde68a` |
| 错误 | `--adm-error` / `*-bg` / `*-border` | `#ef4444` / `#fef2f2` / `#fecaca` |
| 圆角 | `--adm-radius-sm/md/lg` | `6px` / `8px` / `12px` |
| 阴影 | `--adm-shadow-sm/md/lg` | `0 1px 2px` / `0 4px 12px` / `0 8px 24px` |

### 整体布局

```
┌──────────────────────────────────────────────────────────────┐
│ 240px Sidebar │ 56px Header (面包屑 + 通知 + 用户)              │
│               ├──────────────────────────────────────────────┤
│  - 仪表盘     │                                              │
│  - 学生管理   │                                              │
│  - 教师管理   │       Main Content Area                      │
│  - 助教管理   │       (max-width: 1400px, padding: 24px)     │
│  - 数据管理   │                                              │
│    ├─上传记录 │                                              │
│    ├─错题管理 │                                              │
│    └─OSS审计 │                                              │
│               │                                              │
│  Footer:      │                                              │
│  用户+退出    │                                              │
└───────────────┴──────────────────────────────────────────────┘
```

- **侧边栏**：固定 240px，深色 `#1e293b`，分组标签 + 菜单项；激活项用主色背景 + 阴影
- **顶部栏**：56px 高，sticky，左侧面包屑（首页 / 分组 / 当前页），右侧通知铃铛 + 用户头像
- **内容区**：`max-width: 1400px`，居中，24px 内边距

### 组件规范

| 组件 | 规范 |
|------|------|
| 按钮 | `primary`（蓝底白字）/ `secondary`（白底边框）/ `ghost`（透明）/ `danger` / `warning` / `link` 文字按钮 |
| 卡片 | `background: #fff` + `border: 1px solid #e2e8f0` + `border-radius: 12px` |
| 表格 | 表头背景 `#f8fafc`，单元格 padding `14px 16px`，行悬停背景 `#f8fafc` |
| 状态徽章 | 双编码：颜色 + 圆点（`dot::before`），色盲友好 |
| 学科标签 | 统一色彩（数学蓝/语文橙/英语绿/物理紫/化学粉紫/生物绿/历史红/地理蓝/政治紫），跨门户一致 |
| Modal | 居中弹窗，用于详情/编辑表单 |
| Drawer | 右侧滑出面板，宽度 400-480px，用于详情/批量操作 |
| 浮动批量操作栏 | 底部 fixed，显示"已选择 N 项 + 批量操作按钮" |
| 空状态 | 居中图标 + 文案 + 引导按钮 |
| 骨架屏 | shimmer 动画（`linear-gradient` + `background-position` 动画） |

### 状态徽章双编码

所有状态徽章同时使用颜色 + 圆点：

| 状态 | 颜色 | 圆点 |
|------|------|------|
| pending（待处理/待审核） | `--adm-warning` | 圆点 |
| processing（处理中） | `--adm-info` | 旋转 spinner |
| completed/confirmed（已完成/已确认） | `--adm-success` | 圆点 |
| failed/returned/rejected（失败/已退回/已退回） | `--adm-error` | 圆点 |
| ignored（已忽略） | `#a16207` | 圆点 |

---

## 页面架构

### 整体布局

```
┌──────────────────────────────────────────────────────────┐
│  Header: 面包屑 + 通知铃铛 + 用户头像                       │
├────────────┬─────────────────────────────────────────────┤
│            │                                             │
│  Sidebar   │         Main Content Area                  │
│  240px     │         (max-width: 1400px, padding: 24px)  │
│  深色       │                                             │
│            │                                             │
│  概览       │                                             │
│  - 仪表盘   │                                             │
│  用户管理   │                                             │
│  - 学生     │                                             │
│  - 教师     │                                             │
│  - 助教     │                                             │
│  数据管理   │                                             │
│  - 上传记录 │                                             │
│  - 错题     │                                             │
│  - OSS审计  │                                             │
│            │                                             │
│  ──────    │                                             │
│  用户+退出  │                                             │
└────────────┴─────────────────────────────────────────────┘
```

### 页面清单

| 路由 | 页面 | 所属分组 | 说明 |
|------|------|----------|------|
| `/login` | LoginView.vue | 认证 | 双栏品牌+表单登录页 |
| `/dashboard` | DashboardView.vue | 概览 | 数据仪表盘（KPI + 趋势 + 待办） |
| `/students` | StudentView.vue | 用户管理 | 学生管理 |
| `/teachers` | TeacherView.vue | 用户管理 | 教师管理 |
| `/assistants` | AssistantView.vue | 用户管理 | 助教管理 |
| `/upload-records` | UploadRecordView.vue | 数据管理 | 上传记录管理（含状态统计条 + 侧滑详情面板） |
| `/mistakes` | MistakeView.vue | 数据管理 | 错题查询与管理（统计卡 + 详情 Modal） |
| `/oss-audit` | OssAuditView.vue | 数据管理 | OSS 僵尸文件审计（状态面板 + 浮动批量栏） |

默认重定向：`/` → `/dashboard`（已登录）/ `/login`（未登录）。

---

## 页面详细说明

### 0. 登录页 (`/login`)

**布局**：双栏式（左侧品牌渐变面板 + 右侧表单）。

- **左侧品牌面板**：深色渐变背景（`#0f172a → #1e3a5f → #1e40af`），展示 logo、品牌标语、特性列表、版本徽章
- **右侧表单**：登录图标 + 标题、用户名输入（带头像图标）、密码输入（带锁图标 + 显示/隐藏切换）、记住我复选框、登录按钮（渐变背景 + 阴影）
- **错误提示**：内联错误提示框（红色背景 + 警告图标）
- **加载状态**：登录中显示 spinner

**API 调用**：
- `login(username, password)` - 提交登录表单，成功后写入 token 到 localStorage

---

### 0.5 数据仪表盘 (`/dashboard`)

**功能列表**：
- 4 个 KPI 卡片（学生总数 / 教师总数 / 待处理上传 / 待审核错题），带 sparkline 趋势线
- 上传趋势柱状图（学生/教师双柱对比，最近 7 天）
- 待处理事项列表（点击跳转对应管理页）
- 学科分布条形图
- 活动时间线（最近 5 条系统活动）
- 快捷操作卡片（跳转到常用页面）

**API 调用**（全部复用现有 API，不新增后端接口）：
- `studentAdminApi.getStudents({ pageSize: 1 })` - 获取学生总数
- `teacherPortalApi.getTeachers()` - 获取教师列表
- `assistantPortalApi.getAssistants()` - 获取助教列表
- `studentAdminApi.getUploadRecords({ status: 1, pageSize: 1 })` - 待处理上传数
- `studentAdminApi.getMistakeItems({ reviewStatus: 1, size: 1 })` - 待审核错题数
- `ossAuditApi.getRecords(1, 1, 0)` - 待处理 OSS 审计数

---

### 1. 学生管理页面 (`/students`)

**布局**：筛选栏 + 数据表格 + 服务端分页 + 弹窗（创建/编辑/关联账户/开放学科）。

**功能列表**：
- 创建学生（姓名、年级、关联账户、开放学科）；创建接口返回学生 ID 后立即保存勾选的开放学科
- 若学生创建成功但开放学科保存失败，保留已创建学生并提示管理员稍后通过“学科”补充，不得把整个创建操作提示为失败
- 学生列表（搜索、年级/学科筛选、服务端分页）
- 编辑学生（修改信息、管理关联账户、管理开放学科）
- 删除学生
- 不展示“全部/已绑定/未绑定”统计概览条：现有后端未提供全量绑定聚合，禁止用当前页数量冒充全量统计

**表格列**：头像、ID、姓名、年级、开放学科、创建时间、操作。关联账户不单独占列，入口合并到最右侧操作列。

**操作列**：编辑、学科（无开放学科时为强调样式“分配”）、关联账户、删除。

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

**布局**：筛选栏 + 数据表格 + 服务端分页 + 授权弹窗 + 学科管理弹窗。

**功能列表**：
- 授予教师权限（搜索 Identity 用户 → 选择科目 → 授予）
- 教师列表（查看科目、撤销权限）
- 学科管理（增删教师负责学科，可关闭标签 X 按钮）
- 未分配学科警告徽章
- 显示账号备注（从 Identity 服务批量获取用户备注信息，方便管理员识别教师身份）
- 关键词、学科、页码和每页条数均传给 Teacher Portal，由数据库完成过滤、总数统计和分页；前端不得对单页结果做假分页

**表格列**：
| 列 | 说明 |
|------|------|
| 头像 | 36×36 用户头像 |
| User ID | Identity 用户唯一标识 |
| 手机号 | 用户手机号 |
| 用户名 | 用户名 |
| 备注 | 从 Identity 服务获取的账号备注，帮助管理员识别教师身份 |
| 学科 | 教师负责的学科标签（可删除、可管理） |
| 创建时间 | 教师记录创建时间 |
| 操作 | 学科（无学科时为强调样式“分配”）、撤销权限 |

**数据加载流程**：
1. 调用 `getTeachers({ keyword, subject, page, pageSize })` 获取数据库分页结果
2. 收集当前页教师的 `userId`，调用 `getUsersByIds(ids)` 批量获取 Identity 用户信息
3. 通过 `userId` 映射，在表格中显示备注等 Identity 用户字段

**API 调用**（按需加载）：
- `getUsers(params)` - 搜索 Identity 用户
- `getTeachers(params)` - 按关键词/学科分页获取教师列表
- `addTeacherByUserId(payload)` - 新增教师
- `grantTeacher(userId, payload)` - 授予权限
- `removeTeacherByUserId(userId)` - 撤销权限
- `getTeacherSubjects(userId)` - 获取教师学科
- `setTeacherSubjects(userId, subjects)` - 设置学科
- `getAvailableSubjects()` - 获取可用学科

---

### 3. 助教管理页面 (`/assistants`)

**布局**：筛选栏 + 数据表格 + 服务端分页 + 授权弹窗 + 学科管理弹窗。

**功能列表**：
- 授予助教权限（搜索 Identity 用户 → 选择科目 → 授予）
- 助教列表（查看科目、撤销权限）
- 学科管理（增删助教负责学科）
- 未分配学科时显示警告徽章，并将“学科”操作替换为强调样式“分配”
- 关键词、学科、页码和每页条数均传给 Assistant Portal，由数据库完成过滤、总数统计和分页；前端不得对单页结果做假分页

**表格列**：头像、ID、手机号、用户名、备注、学科、创建时间、操作。

**操作列**：学科（无学科时为强调样式“分配”）、撤销权限。

**API 调用**：
- `assistantPortalApi.getAssistants(params)` - 按关键词/学科分页获取助教列表
- `assistantPortalApi.grantAssistant(userId, payload)` - 授予
- `assistantPortalApi.revokeAssistant(userId)` - 撤销
- `assistantPortalApi.setAssistantSubjects(userId, subjects)` - 设置科目

---

### 用户管理页面统一视觉约定

- 三个页面第一列表头统一为“头像”，列表头像统一为 36×36；弹窗信息横幅头像统一为 40×40
- ID 列宽统一为 200px，创建时间列宽统一为 130px，操作列统一预留 260px 并右对齐
- 姓名/用户名保持各自业务命名，但单元格均左对齐、`font-weight: 500`
- 筛选栏统一使用主按钮“搜索”和次级按钮“重置”，筛选控件高度统一为 36px
- 表单统一使用 `--adm-surface-elevated` 背景、主文本标签、18px 组间距、`9px 12px` 输入内边距和 2px 必填标记左间距
- 列表学科标签统一使用全局 `.adm-subj-tag`；可删除标签通过统一的关闭按钮扩展样式实现
- 学科选择网格统一使用带 16×16 自定义勾选框的结构
- Identity 用户搜索项统一包含 30×30 头像、主名称、手机号/显示名等次要信息和选中状态
- 表格、筛选控件、弹窗、表单、头像、用户搜索项、学科标签/网格、信息横幅等三页共用样式统一定义在 `src/style.css`；组件 `<style scoped>` 仅保留页面专属样式

---

### 4. 上传记录管理页面 (`/upload-records`)

**所属分组**：数据管理

**功能列表**：
- 上传记录列表（学生搜索筛选、状态筛选、分页）
- 总记录数显示
- 缩略图列（显示第一张图片缩略图，点击可预览所有图片）
- 查看详情（图片缩略图、旋转、预览大图）
- 按图片粒度分配记录（选择图片 → 指定学科/年级/类型 → 创建错题/作业条目）
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
- `getSubjectOptions()` - 学科选项（用于分配时选择学科）
- `getGrades()` - 年级选项（用于分配时选择年级）
- `getLinkedItems(id)` - 获取上传记录关联的业务实体
- `getStudents(params)` - 搜索学生（用于指派）
- `getUploadRecord(id)` - 获取单条记录
- `assignUploadRecord(id, payload)` - 按图片粒度分配记录（items 数组，每个 item 指定 imageIndices + subject + grade + classification）
- `resetUploadRecordStatus(id, status)` - 重置状态
- `rotateUploadImage(id, payload)` - 旋转图片
- `legacyCheck(id)` - 检查单条上传记录是否为遗留数据
- `legacyClean(id)` - 清理单条遗留数据

---

### 5. 错题管理页面 (`/mistakes`)

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

### 6. OSS 审计页面 (`/oss-audit`)

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
| `getStudentOpenSubjects(id, activeOnly)` | GET | `/api/admin/students/:id/open-subjects` | 开放学科 |
| `setStudentOpenSubjects(id, payload)` | PUT | `/api/admin/students/:id/open-subjects` | 设置开放学科 |
| `getUploadRecords(params)` | GET | `/api/admin/oss-upload-records` | 查询上传记录 |
| `getUploadRecord(id)` | GET | `/api/admin/oss-upload-records/:id` | 获取单条上传记录 |
| `resetUploadRecordStatus(id, status)` | POST | `/api/admin/oss-upload-records/:id/reset-status` | 重置状态 |
| `assignUploadRecord(id, payload)` | POST | `/api/admin/oss-upload-records/:id/assign` | 按图片粒度分配记录（items 数组，每个 item 指定 imageIndices（使用原始索引）+ subject + grade + classification） |
| `getLinkedItems(id)` | GET | `/api/admin/oss-upload-records/:id/linked-items` | 获取关联业务实体 |
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
| `getTeachers(params)` | GET | `/api/teacher-portal/admin/teachers` | 按 keyword/subject/page/pageSize 分页获取教师 |
| `addTeacherByUserId(payload)` | POST | `/api/teacher-portal/admin/teachers/by-user` | 新增教师 |
| `grantTeacher(userId, payload)` | POST | `/api/teacher-portal/admin/identity-users/:userId/grant-teacher` | 授予权限 |
| `removeTeacherByUserId(userId)` | DELETE | `/api/teacher-portal/admin/teachers/by-user/:userId` | 撤销权限 |
| `checkTeacher(userId, phone?)` | GET | `/api/teacher-portal/auth/check-teacher` | 检查教师身份 |
| `getTeacherSubjects(userId)` | GET | `/api/teacher-portal/admin/teachers/:userId/subjects` | 获取教师科目 |
| `setTeacherSubjects(userId, subjects)` | PUT | `/api/teacher-portal/admin/teachers/:userId/subjects` | 设置科目 |
| `addTeacherSubject(userId, subject)` | POST | `/api/teacher-portal/admin/teachers/:userId/subjects/:subject` | 添加科目 |
| `removeTeacherSubject(userId, subject)` | DELETE | `/api/teacher-portal/admin/teachers/:userId/subjects/:subject` | 移除科目 |
| `getAvailableSubjects()` | GET | `/api/teacher-portal/admin/available-subjects` | 可用科目 |

### assistantPortalApi.ts

| 方法 | HTTP | 路径 | 说明 |
|------|------|------|------|
| `getAssistants(params)` | GET | `/api/assistant-portal/admin/assistants` | 按 keyword/subject/page/pageSize 分页获取助教 |
| `grantAssistant(userId, payload)` | POST | `/api/assistant-portal/admin/identity-users/:userId/grant-assistant` | 授予权限 |
| `revokeAssistant(userId)` | POST | `/api/assistant-portal/admin/identity-users/:userId/revoke-assistant` | 撤销权限 |
| `getAssistantSubjects(userId)` | GET | `/api/assistant-portal/admin/assistants/:userId/subjects` | 获取助教学科 |
| `setAssistantSubjects(userId, subjects)` | PUT | `/api/assistant-portal/admin/assistants/:userId/subjects` | 设置学科 |
| `addAssistantSubject(userId, subject)` | POST | `/api/assistant-portal/admin/assistants/:userId/subjects/:subject` | 添加学科 |
| `removeAssistantSubject(userId, subject)` | DELETE | `/api/assistant-portal/admin/assistants/:userId/subjects/:subject` | 移除学科 |
| `getAvailableSubjects()` | GET | `/api/assistant-portal/admin/available-subjects` | 可用学科 |

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

interface PortalPagedResponse<T> {
  success: boolean;
  data: T[];
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
  updatedAt: number | string; imageRotations: number[];
}

interface AssignItemRequest {
  classification: number;
  subject: number;
  grade: number;
  imageIndices: number[];
}

interface LinkedItemDto {
  id: string;
  subject: number;
  grade: number;
  reviewStatus: number;
  imageIndices: number[];
}

// 注意：`imageIndices` 使用的是原始索引（上传时的顺序），不受后续移除操作影响。
interface LinkedItemsResponse {
  mistakeItems: LinkedItemDto[];
  homeworkItems: LinkedItemDto[];
  remainingImageCount: number;
  totalImageCount: number;
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
  reviewStatuses: EnumOption[];
}
```

---

## 设计规范

- **UI 框架**：Element Plus，使用 `size="small"` 紧凑模式
- **布局**：侧边栏导航（含二级菜单）+ 主内容区，各页面独立
- **分页**：用户管理列表默认每页 20 条；学生由 Student 服务分页，教师/助教分别由 Teacher Portal / Assistant Portal 在数据库查询中完成过滤、计数与分页，前端只渲染服务端返回的当前页
- **懒加载路由**：按需加载页面组件
- **按需调用 API**：每个页面只调用自己需要的接口
- **错误处理**：Axios 错误统一提取 `response.data.message`，使用 `ElMessage.error` 提示
- **确认操作**：删除等危险操作使用 `ElMessageBox.confirm` 二次确认
- **认证**：JWT Bearer。登录后前端将 access token 存入 localStorage，通过共享 axios 实例（`services/httpClient.ts`）的请求拦截器统一附加 `Authorization: Bearer` 头；后端使用 `[Authorize]` / `[Authorize(Roles="admin")]` 校验 Identity 签发的 JWT
- **共享 HTTP 客户端**：所有 API 服务（`studentAdminApi` / `teacherPortalApi` / `assistantPortalApi` / `identityApi` / `ossAuditApi`）必须复用 `services/httpClient.ts` 导出的共享 axios 实例，不得各自 `axios.create()` 单独建实例。原因：axios 实例间不共享拦截器，单独建实例会导致 JWT 未注入 → 后端返回 401。共享实例同时配置：
  - 请求拦截器：从 localStorage 读取 token，附加 `Authorization: Bearer <token>` 头
  - 响应拦截器：收到 401 时清除 token 并重定向到 `/login`
- **登录流程例外**：`services/auth.ts` 的 `login()` 使用原生 `fetch`（不经 axios），登录成功后写入 localStorage，后续 axios 请求才能读到 token
- **图片加载（Cookie + JWT 双通道）**：浏览器 `<img>` / `<el-image>` 标签发起的图片请求**无法携带自定义 Authorization 头**（W3C 标准限制），但会自动携带同源 cookie。为此 admin_portal 采用双通道：
  - **Bearer 通道**：axios 请求（API 调用）走 `Authorization: Bearer <jwt>`，token 从 localStorage 读取（由 `services/httpClient.ts` 拦截器注入）
  - **Cookie 通道**：登录成功时后端 `AdminAuthController.Login` 把同一份 JWT 写入 HttpOnly cookie（`adminAuthToken`，SameSite=Strict，Path=/）。`<img>` 标签的图片请求自动携带该 cookie。后端 `AddJwtBearer` 的 `OnMessageReceived` 事件优先读 Authorization 头，缺失时回退读 cookie，保证 `[Authorize]` 端点对两种通道都生效
  - **退出登录时**：后端 `AdminAuthController.Logout` 清除 cookie；前端 `clearAuth()` 清除 localStorage
- **图片端点 URL 约定**：`GET /api/admin/image?path=<ossPath>&size=<small|medium|空>`，保留 `[Authorize]`（由 cookie 通道鉴权），返回 302 重定向到 OSS presigned URL；浏览器随后从平台公共 `https://oss.example.com/oss/` 入口拉取图片，该入口由 User Web Nginx 统一代理。
  - 列表缩略图：`size=small`
  - 详情页中等图：`size=medium`
  - 详情页点击放大预览：不传 `size`（返回原图）
  - 路径分流：`mistakes/` 前缀走 Mistake 服务，其他路径走 Student 服务
