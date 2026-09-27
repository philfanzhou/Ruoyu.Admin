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
| 图标 | `@element-plus/icons-vue` |
| 设计令牌 | `src/styles/tokens.css`（`--adm-*`），Element Plus 主题映射见 `src/styles/element-theme.css` |

---

## 项目结构

```
frontend/src/
├── main.ts                      # 入口，注册 ElementPlus + VueRouter；样式引入顺序：EP css → tokens.css → element-theme.css → style.css
├── App.vue                      # 根组件，应用外壳（可折叠侧边栏 + 56px 顶部栏 + 面包屑）
├── styles/
│   ├── tokens.css               # 设计令牌（色板、字号、间距、圆角、阴影、z-index、布局尺寸）
│   └── element-theme.css        # 把 --el-* 变量（含派生色）映射到 --adm-* 令牌
├── style.css                    # 基础元素样式 + 各页面仍在使用的共享类
├── composables/
│   └── useSidebar.ts            # 侧边栏折叠状态（视口断点 + localStorage 偏好）
├── router/
│   ├── index.ts                 # 路由配置（meta: title / group / icon）
│   └── navigation.ts            # 导航分组常量表与 RouteMeta 类型声明
├── views/
│   ├── LoginView.vue            # 登录页（单卡片）
│   ├── DashboardView.vue        # 待办与概况（待处理 + 规模指标，只展示真实数据）
│   ├── StudentView.vue          # 学生管理页面
│   ├── TeacherView.vue          # 教师管理页面
│   ├── AssistantView.vue        # 助教管理页面
│   ├── UploadRecordView.vue      # 上传记录管理页面（列表与编排：状态统计卡 + 公共列表组件）
│   ├── upload-records/           # 上传记录子组件：详情抽屉、图片画廊、VL 分析卡/结果弹窗、指派表单、重置/遗留检查弹窗、状态映射
│   ├── MistakeView.vue           # 错题管理页面（列表与编排：统计卡 + 公共列表组件）
│   ├── mistakes/                 # 错题子组件：详情弹窗、信息与审核信息、图片画廊（含迁移横幅）、编辑表单
│   ├── OssAuditView.vue          # OSS 审计页面（列表、选择与删除/忽略编排）
│   └── oss-audit/                # OSS 审计子组件：状态面板、记录表格、浮动批量栏、忽略弹窗、扫描状态轮询（useAuditStatus）
├── services/
│   ├── httpClient.ts            # 共享 axios 实例（含 JWT 注入 + 401 重定向拦截器）
│   ├── identityApi.ts           # Identity 用户管理 API
│   ├── studentAdminApi.ts       # 学生管理 + 上传记录 + 错题查询 API
│   ├── teacherPortalApi.ts      # 教师权限管理 API
│   ├── assistantPortalApi.ts    # 助教权限管理 API
│   └── ossAuditApi.ts           # OSS 审计 API
└── utils/
    └── subject.ts               # 学科名称与颜色映射
```

---

## 设计规范

定位为内部运维工具：信息清晰、操作高效、危险操作醒目。

### Design Tokens（CSS 变量）

所有 `--adm-*` 令牌统一定义在 `src/styles/tokens.css`，其他样式只引用令牌，不写裸色值。主要取值：

| 类别 | 变量 | 值 |
|------|------|----|
| 主色 | `--adm-primary` / `-hover` / `-bg` / `-border` | `#2563eb` / `#1d4ed8` / `#eff6ff` / `#bfdbfe` |
| 成功 | `--adm-success` / `-hover` / `-bg` / `-border` | `#15803d` / `#166534` / `#f0fdf4` / `#bbf7d0` |
| 警告 | `--adm-warning` / `-hover` / `-bg` / `-border` | `#b45309` / `#92400e` / `#fffbeb` / `#fde68a` |
| 危险 | `--adm-danger` / `-hover` / `-bg` / `-border` | `#dc2626` / `#b91c1c` / `#fef2f2` / `#fecaca` |
| 信息 | `--adm-info*` | 同主色 |
| 中性 | `--adm-neutral` | `#64748b`（Element Plus `info` 色） |
| 侧边栏 | `--adm-sidebar` / `--adm-sidebar-hover` | `#0f172a` / `#1e293b` |
| 背景 | `--adm-surface` / `-elevated` / `-subtle` | `#f1f5f9` / `#ffffff` / `#f8fafc` |
| 边框 | `--adm-border` / `-light` / `-hover` | `#e2e8f0` / `#f1f5f9` / `#cbd5e1` |
| 文本 | `--adm-text-primary` / `-secondary` / `-tertiary` / `-muted` | `#0f172a` / `#475569` / `#64748b` / `#7d8ba1` |
| 字号 | `--adm-font-size-xs/sm/base/md/lg/xl` | 12 / 13 / 14 / 16 / 18 / 22px |
| 间距 | `--adm-space-1…8` | 4px 基准 |
| 圆角 | `--adm-radius-sm/md/lg` | 4 / 6 / 8px |
| 布局 | `--adm-sidebar-width` / `--adm-sidebar-collapsed-width` / `--adm-header-height` | 220 / 64 / 56px |

- `--adm-error*` 是 `--adm-danger*` 的兼容别名，仅供尚未迁移的页面使用；新代码使用 `--adm-danger*`。
- 对比度约束：正文文本（`--adm-text-primary`/`-secondary`）与语义色文字在白色背景上 ≥ 4.5:1，次级文本（`-tertiary`/`-muted`）在 `--adm-surface` 与白色背景上 ≥ 3:1。修改令牌时需重新核对。

### Element Plus 主题映射

`src/styles/element-theme.css` 在 `:root` 中覆盖 `--el-color-{primary,success,warning,danger,error,info}` 及其派生色 `-light-3/-light-5/-light-7/-light-8/-light-9/-dark-2`，并映射字号、圆角、文本色、边框色与背景色。Element Plus 的 hover/active 色是预计算的静态值，**必须显式覆盖派生色**，只改基色会让 hover 仍是默认蓝。派生色用 `color-mix()` 从令牌计算（与 Element Plus 自身的混色比例一致）；`--el-color-*-rgb` 三元组无法从十六进制变量派生，需与令牌手动保持一致。

### 整体布局

```
┌──────────────────────────────────────────────────────────────┐
│ Sidebar 220px │ 56px Header：折叠按钮 + 面包屑 ……… 用户名 + 退出登录 │
│ (折叠为 64px)  ├──────────────────────────────────────────────┤
│  概览          │                                              │
│   数据仪表盘   │                                              │
│  用户          │       Main Content Area                      │
│   学生/教师/助教│       (max-width: 1400px, padding: 24px)     │
│  内容与上传    │                                              │
│   上传记录/错题 │                                              │
│  存储审计      │                                              │
│   OSS 审计     │                                              │
└───────────────┴──────────────────────────────────────────────┘
```

- **导航单一来源**：路由 `meta` 声明 `title`、`group`、`icon`（`@element-plus/icons-vue`），分组显示名在 `router/navigation.ts` 的 `NAV_GROUPS` 常量表中。侧边栏、分组和面包屑（首页 / 分组 / 页面）都由此生成，不在外壳中硬编码路径表。
- **侧边栏**：`el-menu` 实现，深色背景；激活项按路由 `name` 判断，带 query 的地址（如 `/upload-records?status=1`）同样高亮。点击导航项跳转到无 query 的页面地址。
- **折叠**：视口 < 1024px 时自动折叠为 64px 图标栏，在此宽度下的手动切换只是临时的；视口 ≥ 1024px 时使用用户偏好（默认展开），手动切换写入 `localStorage.adminSidebarCollapsed`（`'1'`/`'0'`），刷新后保持。`localStorage` 读写失败时按宽度默认值处理，不报错。折叠态悬停导航项显示名称提示；折叠按钮带 `aria-label`。
- **顶部栏**：56px 高，sticky；只包含折叠按钮、面包屑、用户名和「退出登录」，不放无功能的按钮。
- **内容区**：`max-width: 1400px`，居中，24px 内边距；外壳本身不产生横向滚动，页面自身的最小宽度由各列表页负责。

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

整体布局见上文「设计规范 → 整体布局」。

### 页面清单

| 路由 | 页面 | 所属分组 | 说明 |
|------|------|----------|------|
| `/login` | LoginView.vue | 认证 | 单卡片登录页 |
| `/dashboard` | DashboardView.vue | 概览 | 待办与概况（待处理 + 规模指标，只展示真实数据） |
| `/students` | StudentView.vue | 用户 | 学生管理 |
| `/teachers` | TeacherView.vue | 用户 | 教师管理 |
| `/assistants` | AssistantView.vue | 用户 | 助教管理 |
| `/upload-records` | UploadRecordView.vue | 内容与上传 | 上传记录管理（含状态统计卡 + 非模态详情抽屉） |
| `/mistakes` | MistakeView.vue | 内容与上传 | 错题查询与管理（统计卡 + 详情弹窗） |
| `/oss-audit` | OssAuditView.vue | 存储审计 | OSS 僵尸文件审计（状态面板 + 浮动批量栏） |

默认重定向：`/` → `/dashboard`（已登录）/ `/login`（未登录）。

---

## 页面详细说明

### 0. 登录页 (`/login`)

**布局**：居中单卡片（品牌名 + 标题 + 表单），使用 Element Plus 表单组件与设计令牌，不包含营销文案、版本号或版权年份。

- **表单**：用户名 / 手机号、密码（可切换显示）、「记住用户名」复选框、登录按钮
- **记住用户名**：勾选时登录成功后写入 `localStorage.adminUsername`（外壳显示用户名用），取消勾选时删除；与 token 是否保存无关
- **提交**：用户名或密码为空时按钮禁用；回车可提交；登录中按钮进入 loading 状态并忽略重复提交
- **错误提示**：内联 `el-alert` 显示后端返回的 `message`

**API 调用**：
- `login(username, password)` - 提交登录表单，成功后写入 token 到 localStorage

---

### 0.5 数据仪表盘 (`/dashboard`)

页面标题「待办与概况」。只展示能从现有接口取得的真实数字，不包含任何示例数据、趋势图或活动流（需要新增后端统计接口，不在前端范围）。

**指标**（每张卡片独立加载，可点击进入对应列表）：

| 分组 | 指标 | 数据来源 | 跳转 |
|------|------|----------|------|
| 待处理 | 待处理上传 | `getUploadRecords({ page: 1, pageSize: 1, status: 1 }).totalCount` | `/upload-records?status=1` |
| 待处理 | 待审核错题 | `getMistakeItems({ page: 1, size: 1, reviewStatus: 1 }).total` | `/mistakes?reviewStatus=1` |
| 待处理 | 待处置 OSS 审计记录 | `ossAuditApi.getStatus().pendingCount` | `/oss-audit?status=0` |
| 规模 | 学生 | `getStudents({ page: 1, pageSize: 1 }).total` | `/students` |
| 规模 | 教师 | `getTeachers({ page: 1, pageSize: 1 }).total` | `/teachers` |
| 规模 | 助教 | `getAssistants({ page: 1, pageSize: 1 }).total` | `/assistants` |

**加载与失败**：各卡片并行请求、互不影响（`Promise.allSettled`）。请求失败或响应中缺少数值时，该卡显示「—」与「加载失败」并提供单卡「重试」；**失败不得显示为 0**。页头「刷新」重新加载全部卡片。

**列表页深链**（`src/utils/routeQuery.ts`）：上传记录（`status` ∈ 1–5）、错题（`reviewStatus` ∈ 1–3）、OSS 审计（`status` ∈ 0–2）从 URL 读取状态筛选，并在首次列表请求前生效；缺失、重复、非整数或不在集合中的值视为无筛选。页面存活期间 URL 中该键变化（如点击侧边栏无参链接）时重新应用筛选并回到第 1 页；页面内切换状态筛选或重置时用 `router.replace` 同步该键（其他 query 键保留），刷新后筛选与 URL 一致，且每次切换只发出一次列表请求。

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

### 列表页规范

所有列表页（学生、教师、助教、上传记录、错题、OSS 审计已迁移）统一使用 `src/components/list/` 下的公共组件，不再各写一套原生表格、手写模态框或分页器：

| 组件 | 接口 | 约定 |
|------|------|------|
| `PageHeader.vue` | props `title`、`description`；插槽 `#actions` | 页面标题区，主操作按钮放在 `#actions` |
| `FilterBar.vue` | 默认插槽放 `el-form-item`；emit `search` / `reset` | 卡片容器 + `el-form inline`，自带标准「搜索」「重置」按钮，窄屏自动换行；筛选控件需显式设置宽度 |
| `DataTableCard.vue` | props `data`、`loading`、`error: string \| null`、`emptyText`；默认插槽放 `el-table-column`；插槽 `#footer`；emit `retry`；其余属性透传给 `el-table` | 状态由 props 唯一决定：`loading` 优先（`v-loading`），其次 `error`（显示错误信息与「重试」，**不显示空态**），最后按 `data.length` 显示空态。卡片宽度跟随容器（`contain: inline-size`），宽表在卡片内横向滚动，页面本身不出现横向滚动 |
| `ListPagination.vue` | `v-model:page`、`v-model:page-size`、`total`；emit `change` | `el-pagination`，`layout="total, sizes, prev, pager, next"`，每页 10/20/50；改变每页条数时回到第 1 页；每次交互只触发一次 `change` |
| `SubjectTag.vue` | props `subject`、`closable`；emit `close` | 沿用 `utils/subject.ts` 的学科色映射（全局 `.adm-subj-tag`） |
| `StatusTag.vue` | props `label`、`type: 'success' \| 'warning' \| 'danger' \| 'info' \| 'primary'` | 基于 `el-tag` 的通用状态标签 |

使用约定：

- **加载失败**：列表加载失败时清空旧行并设置 `error`，由 `DataTableCard` 显示错误态与「重试」；可以同时保留 toast。失败与「暂无数据」必须可区分。
- **弹窗**：表单与详情使用 `el-dialog`（或 `el-drawer`），设置 `append-to-body` 与 `:close-on-click-modal="false"`（避免点遮罩丢失填写内容）；Esc 可关闭，打开时焦点在弹窗内。
- **表单**：`el-form label-position="top"`，必填项用 `el-form-item required` 标记；校验提示沿用各页现有的 `ElMessage.warning` 规则。多选学科用 `el-checkbox-group` + `el-checkbox border`。
- **远程搜索**：选择 Identity 用户/账户用 `el-select filterable remote`，`remote-method` 调用 `identityApi.getUsers`：关键字至少 2 个字符，纯数字按手机号查询，`pageSize: 20`。已选项需保留在 options 中以显示标签。（学生「关联账户」弹窗中的添加列表每行有独立的添加按钮与进行中状态，保留 `el-input` 搜索 + 列表的形式。）
- **行操作**：操作列 `fixed="right"`；普通操作用 `el-button link type="primary"`，删除/撤销/解除等破坏性操作用 `el-button link type="danger"` 并经过危险二次确认：调用 `src/utils/confirm.ts` 的 `confirmDanger`（约定见「设计规范 → 确认操作」），不得直接使用 `ElMessageBox.confirm`。
- **Element Plus 语言**：`main.ts` 以 `zh-cn` locale 注册 Element Plus，分页、空态等内置文案为中文。
- **已知限制**：学生页的「学科」筛选在客户端过滤当前页（后端 `GET /api/admin/students` 只支持 `name`、`grade`），启用时页面显示「学科筛选仅作用于当前页结果」提示；每行一次开放学科请求（N+1）为现有行为。两者需要服务端支持，不在前端范围。

---

### 4. 上传记录管理页面 (`/upload-records`)

**所属分组**：内容与上传

**布局**：页头（刷新）+ 5 张状态统计卡（点击按该状态筛选，再次点击取消）+ 筛选栏（学生 `el-select filterable remote`，规则沿用原实现：去空格后为空不请求、250ms 防抖、`getStudents({ name, pageSize: 20 })`；状态）+ 数据表格 + 服务端分页。点击行或「详情」打开右侧 480px 详情抽屉（`src/views/upload-records/UploadRecordDetailDrawer.vue`）：抽屉**非模态**（`:modal="false" modal-penetrable`），打开时列表仍可点击其他行切换详情，当前行高亮；Esc 关闭。重置状态、遗留检查、VL 完整结果为 `el-dialog`。

**状态映射**（`src/views/upload-records/uploadRecord.ts` 的 `UPLOAD_STATUSES`，与后端 `UploadStatusConstants.DisplayNames` 一致，统计卡、筛选、表格、详情共用）：1 待处理 / 2 处理中 / 3 审核中 / 4 处理失败 / 5 已退回。

**确认**：删除图片使用 `confirmDanger`（文案说明若为最后一张，整条上传记录一并删除）；退回、重置状态为非破坏性操作，使用普通确认或弹窗；遗留清理为两步（检查结果弹窗 → 弹窗内「确认清理」）。

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

**布局**：页头（刷新）+ 4 张统计卡（总错题 / 待审核 / 已确认 / 已退回；点击按该审核状态筛选，再次点击或点「总错题」取消）+ 筛选栏（学生 `el-select filterable remote`，规则沿用原实现：去空格后为空不请求、250ms 防抖、`getStudents({ name, pageSize: 20 })`；学科、年级、审核状态）+ 数据表格（待审核行高亮）+ 服务端分页（`size` 10/20/50）。「详情」「编辑」打开同一个 700px `el-dialog`（`src/views/mistakes/MistakeDetailDialog.vue`）：信息与审核信息（只读，本页不提供审核操作）、题目图片（含 `uploads/` 路径的迁移横幅）、编辑表单（学科、年级、学生必填）。审核状态标签取自 `enum-options` 的 `reviewStatuses`，失败时回退到本地默认。

**功能列表**：
- 错题列表（学生搜索、学科/年级/审核状态筛选、分页）
- 总记录数显示
- 查看详情（图片、完整信息）
- 编辑错题（修改学生、学科、年级）
- 迁移图片（`uploads/` → `mistakes/`）

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

**所属分组**：存储审计

**布局**：页头（「触发全量扫描」，经普通确认；运行中或触发中禁用）+ 状态面板 + 筛选栏（状态、Bucket、路径）+ 记录表格 + 服务端分页（`pageSize` 10/20/50）+ 浮动批量操作栏 + 忽略弹窗（`el-dialog`）。子组件位于 `src/views/oss-audit/`。

**状态面板**：区分三种状态——运行中「扫描进行中...」、从未完成过扫描「尚无完成的扫描」、否则「上次完成于 …」（附耗时、发现数、触发方式）；存在 `lastFailed` 时始终显示上次失败信息。统计为「上次完成」「上次失败」「待处理」（`getStatus().pendingCount`）「已忽略」（记录响应的 `statusCounts[2]`）。不展示「已清理」：处置成功后记录被删除而不是置为已删除，该计数没有真实数据源。页面每 10 秒轮询状态，检测到扫描从运行中变为完成时刷新列表。

**状态标签**：统一使用中文（0 待处理 / 1 已删除 / 2 已忽略，`src/views/oss-audit/ossAudit.ts`），不显示后端 `statusText`。

**路径搜索**：接口不支持路径参数，路径关键字只在客户端过滤当前页；启用时列表上方提示「路径搜索仅作用于当前页结果：本页匹配 X 条（共 N 条）」，分页总数仍为服务端总数。

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

**批量选择与删除（删除会真实删除生产对象存储文件）**：
- 只有待处理（status=0）的行可以勾选，其他行复选框禁用；全选只选当前显示的待处理行。
- 不变量：选择集合 ⊆ 当前显示的待处理行。`loadRecords` 成功替换列表时清空选择，覆盖翻页、改每页条数、筛选、路径搜索、重置、处置后刷新、轮询刷新与重试；`loadRecords` 失败时清空列表（显示错误态与「重试」）并同时清空选择。
- 选择列是自定义的 `el-checkbox` 列，`selectedIds` 是唯一数据源；不使用 `el-table` 自带的选择列（它在表格内部另存一份选择状态）。
- 批量删除在打开确认框时对选中 id 做快照，确认框显示快照数量，确认后只发送该快照；确认框打开期间列表刷新（或刷新失败）不改变要删除的集合。取消/关闭/Esc 不发任何请求。
- 单条删除请求进行中，该行的「删除/忽略」禁用。
- 服务端仍对每条记录做引用复核（被引用则拒绝，来源不可达返回 502），前端确认不替代服务端复核。

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
- **布局**：可折叠侧边栏（按功能分组，由路由 meta 生成）+ 顶部栏 + 主内容区，详见上文「设计规范 → 整体布局」
- **分页**：用户管理列表默认每页 20 条；学生由 Student 服务分页，教师/助教分别由 Teacher Portal / Assistant Portal 在数据库查询中完成过滤、计数与分页，前端只渲染服务端返回的当前页
- **懒加载路由**：按需加载页面组件
- **按需调用 API**：每个页面只调用自己需要的接口
- **错误处理**：Axios 错误统一提取 `response.data.message`，使用 `ElMessage.error` 提示
- **确认操作**：删除、撤销权限、移除授权、解除关联等破坏性操作必须使用 `src/utils/confirm.ts` 的 `confirmDanger({ title, message, confirmText })`：危险图标与危险按钮、打开时确认按钮不获得焦点（回车不会执行）、点击遮罩不关闭、Esc/取消返回 `false` 且不抛异常。文案写明对象与后果；需要强调时用 `h()` 构造 VNode，**禁止** `dangerouslyUseHTMLString`（姓名、路径来自后端数据）。调用方写 `if (!(await confirmDanger(...))) return`，API 错误在自己的 try/catch 中处理。非破坏性操作（退回、重置状态、忽略、触发扫描等）不使用危险样式。
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
