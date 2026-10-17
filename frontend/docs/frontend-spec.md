# Admin Portal Frontend Spec

## Tech Stack

| Item | Technology |
|------|------|
| Framework | Vue 3 + TypeScript |
| UI library | Element Plus |
| HTTP client | Axios |
| Build tool | Vite |
| Routing | Vue Router 4 |
| Entry point | `src/main.ts` |
| Icons | `@element-plus/icons-vue` |
| Design tokens | `src/styles/tokens.css` (`--adm-*`); the Element Plus theme mapping lives in `src/styles/element-theme.css` |

---

## Project Structure

```
frontend/src/
├── main.ts                      # Entry point, registers ElementPlus + VueRouter; style import order: EP css → tokens.css → element-theme.css → style.css
├── App.vue                      # Root component, application shell (collapsible sidebar + 56px top bar + breadcrumb)
├── styles/
│   ├── tokens.css               # Design tokens (palette, font sizes, spacing, radii, shadows, z-index, layout dimensions)
│   └── element-theme.css        # Maps --el-* variables (including derived colors) to --adm-* tokens
├── style.css                    # Base element styles + shared classes still used by pages
├── composables/
│   └── useSidebar.ts            # Sidebar collapse state (viewport breakpoint + localStorage preference)
├── router/
│   ├── index.ts                 # Route configuration (meta: title / group / icon)
│   └── navigation.ts            # Navigation group constant table and the RouteMeta type declaration
├── views/
│   ├── LoginView.vue            # Login page (single card)
│   ├── DashboardView.vue        # To-dos and overview (pending + scale metrics, shows only real data)
│   ├── StudentView.vue          # Student management page
│   ├── TeacherView.vue          # Teacher management page
│   ├── AssistantView.vue        # Assistant management page
│   ├── UploadRecordView.vue      # Upload record management page (list and actions: status stat cards + shared list components)
│   ├── upload-records/           # Upload record child components: detail drawer, image gallery, VL analysis card/result dialog, assign form, reset/legacy-check dialogs, status mapping
│   ├── MistakeView.vue           # Mistake management page (list and actions: stat cards + shared list components)
│   ├── mistakes/                 # Mistake child components: detail dialog, info and review info, image gallery (with migration banner), edit form
│   ├── OssAuditView.vue          # OSS audit page (list, read-only observations, and historical record ignoring)
│   └── oss-audit/                # OSS audit child components: read-only observation status panel, record table, historical ignore dialog, scan status polling (useAuditStatus)
├── services/
│   ├── httpClient.ts            # Shared axios instance (same-origin session, CSRF, and controlled invalidation navigation)
│   ├── identityApi.ts           # Identity user management API
│   ├── studentAdminApi.ts       # Student management + upload record + mistake query API
│   ├── teacherPortalApi.ts      # Teacher permission management API
│   ├── assistantPortalApi.ts    # Assistant permission management API
│   └── ossAuditApi.ts           # OSS audit API
└── utils/
    └── subject.ts               # Subject name and color mapping
```

---

## Design Guidelines

Positioned as an internal operations tool: clear information, efficient actions, and prominent dangerous operations.

### Design Tokens (CSS Variables)

All `--adm-*` tokens are defined once in `src/styles/tokens.css`; other styles reference tokens only and never hard-code raw color values. Main values:

| Category | Variable | Value |
|------|------|----|
| Primary | `--adm-primary` / `-hover` / `-bg` / `-border` | `#2563eb` / `#1d4ed8` / `#eff6ff` / `#bfdbfe` |
| Success | `--adm-success` / `-hover` / `-bg` / `-border` | `#15803d` / `#166534` / `#f0fdf4` / `#bbf7d0` |
| Warning | `--adm-warning` / `-hover` / `-bg` / `-border` | `#b45309` / `#92400e` / `#fffbeb` / `#fde68a` |
| Danger | `--adm-danger` / `-hover` / `-bg` / `-border` | `#dc2626` / `#b91c1c` / `#fef2f2` / `#fecaca` |
| Info | `--adm-info*` | Same as primary |
| Neutral | `--adm-neutral` | `#64748b` (the Element Plus `info` color) |
| Sidebar | `--adm-sidebar` / `--adm-sidebar-hover` | `#0f172a` / `#1e293b` |
| Backgrounds | `--adm-surface` / `-elevated` / `-subtle` | `#f1f5f9` / `#ffffff` / `#f8fafc` |
| Borders | `--adm-border` / `-light` / `-hover` | `#e2e8f0` / `#f1f5f9` / `#cbd5e1` |
| Text | `--adm-text-primary` / `-secondary` / `-tertiary` / `-muted` | `#0f172a` / `#475569` / `#64748b` / `#7d8ba1` |
| Font sizes | `--adm-font-size-xs/sm/base/md/lg/xl` | 12 / 13 / 14 / 16 / 18 / 22px |
| Spacing | `--adm-space-1…8` | 4px base |
| Radii | `--adm-radius-sm/md/lg` | 4 / 6 / 8px |
| Layout | `--adm-sidebar-width` / `--adm-sidebar-collapsed-width` / `--adm-header-height` | 220 / 64 / 56px |

- `--adm-error*` is a compatibility alias of `--adm-danger*`, reserved for pages that have not migrated yet; new code uses `--adm-danger*`.
- Contrast constraints: body text (`--adm-text-primary`/`-secondary`) and semantic-color text on white backgrounds must reach ≥ 4.5:1, and secondary text (`-tertiary`/`-muted`) must reach ≥ 3:1 on `--adm-surface` and white backgrounds. Re-verify whenever a token changes.

### Element Plus Theme Mapping

`src/styles/element-theme.css` overrides `--el-color-{primary,success,warning,danger,error,info}` and their derived colors `-light-3/-light-5/-light-7/-light-8/-light-9/-dark-2` in `:root`, and maps font sizes, radii, text colors, border colors, and background colors. Element Plus hover/active colors are precomputed static values, so **the derived colors must be overridden explicitly** — changing only the base color would leave hover at the default blue. Derived colors are computed from tokens with `color-mix()` (using the same mix ratios Element Plus itself uses); the `--el-color-*-rgb` triplets cannot be derived from hex variables and must be kept in sync with the tokens manually.

### Overall Layout

```
┌──────────────────────────────────────────────────────────────┐
│ Sidebar 220px │ 56px Header: collapse button + breadcrumb … Username + Sign out │
│ (64px when    ├──────────────────────────────────────────────┤
│  collapsed)   │                                              │
│  Overview     │                                              │
│   Dashboard   │       Main Content Area                      │
│  Users        │       (max-width: 1400px, padding: 24px)     │
│   Students/   │                                              │
│   Teachers/   │                                              │
│   Assistants  │                                              │
│  Content      │                                              │
│   Upload      │                                              │
│   records/    │                                              │
│   Mistakes    │                                              │
│  Storage      │                                              │
│   OSS audit   │                                              │
└───────────────┴──────────────────────────────────────────────┘
```

- **Single source of navigation**: routes declare `title`, `group`, and `icon` (`@element-plus/icons-vue`) in `meta`; group display names live in the `NAV_GROUPS` constant table in `router/navigation.ts`. The sidebar, groups, and breadcrumb (home / group / page) are all generated from this — the shell never hard-codes a path table.
- **Sidebar**: implemented with `el-menu` on a dark background; the active item is matched by route `name`, so addresses with a query (such as `/upload-records?status=1`) stay highlighted too. Clicking a navigation item navigates to the page address without the query.
- **Collapse**: below a 1024px viewport the sidebar auto-collapses to a 64px icon rail, and manual toggling at that width is only temporary; at ≥ 1024px the user preference applies (expanded by default), manual toggles write `localStorage.adminSidebarCollapsed` (`'1'`/`'0'`), and the state survives refresh. If reading or writing `localStorage` fails, fall back to the width default without raising an error. In the collapsed state, hovering a navigation item shows a name tooltip; the collapse button carries an `aria-label`.
- **Top bar**: 56px tall, sticky; contains only the collapse button, breadcrumb, username, and "Sign out" — no non-functional buttons.
- **Content area**: `max-width: 1400px`, centered, 24px padding; the shell itself never produces horizontal scrolling — each list page owns its own minimum width.

### Component Conventions

| Component | Convention |
|------|------|
| Buttons | `primary` (blue background, white text) / `secondary` (white background, border) / `ghost` (transparent) / `danger` / `warning` / `link` text button |
| Cards | `background: #fff` + `border: 1px solid #e2e8f0` + `border-radius: 12px` |
| Tables | Header background `#f8fafc`, cell padding `14px 16px`, row hover background `#f8fafc` |
| Status badges | Dual-encoded: color + dot (`dot::before`), colorblind-friendly |
| Subject tags | Unified colors (math blue / Chinese orange / English green / physics purple / chemistry pink-purple / biology green / history red / geography blue / politics purple), consistent across portals |
| Modal | Centered dialog for details/edit forms |
| Drawer | Right slide-out panel, 400-480px wide, for details/bulk operations |
| Floating bulk action bar | Fixed at the bottom, shows "N items selected + bulk action buttons" |
| Empty states | Centered icon + copy + a guiding button |
| Skeleton screens | Shimmer animation (`linear-gradient` + `background-position` animation) |

### Status Badge Dual Encoding

All status badges use both color and dot:

| Status | Color | Dot |
|------|------|------|
| pending (to-process / awaiting review) | `--adm-warning` | Dot |
| processing | `--adm-info` | Spinning spinner |
| completed/confirmed | `--adm-success` | Dot |
| failed/returned/rejected | `--adm-error` | Dot |
| ignored | `#a16207` | Dot |

---

## Page Architecture

For the overall layout see "Design Guidelines → Overall Layout" above.

### Page Inventory

| Route | Page | Group | Notes |
|------|------|----------|------|
| `/login` | LoginView.vue | Auth | Single-card login page |
| `/dashboard` | DashboardView.vue | Overview | To-dos and overview (pending + scale metrics, shows only real data) |
| `/students` | StudentView.vue | Users | Student management |
| `/teachers` | TeacherView.vue | Users | Teacher management |
| `/assistants` | AssistantView.vue | Users | Assistant management |
| `/upload-records` | UploadRecordView.vue | Content & Uploads | Upload record management (status stat cards + non-modal detail drawer) |
| `/mistakes` | MistakeView.vue | Content & Uploads | Mistake query and management (stat cards + detail dialog) |
| `/oss-audit` | OssAuditView.vue | Storage Audit | OSS reference-observation audit (status panel + read-only records) |

Default redirect: `/` → `/dashboard` (signed in) / `/login` (anonymous).

---

## Page Details

### 0. Login Page (`/login`)

**Layout**: a centered single card (brand name, session status, explicit actions), built with Element Plus and the design tokens.

- No username, password, remember-username, or token inputs; an explicit same-origin link goes to `/api/auth/oidc/start?returnUrl=...`, and credentials are submitted only on the SignaCore hosted page.
- The route guard first waits for the single `/api/auth/session` request. The full three-field status decides authenticated, anonymous, reauthentication, forbidden, or unavailable; the display name comes only from the current server session, and null stays blank.
- Deep links keep the in-site path and query; external URLs, double slashes, backslashes, control characters, dot paths, login/auth loops, and input still malformed after four decode levels fall back to `/dashboard`, with a length cap of 2048.
- Expiry only shows an explicit re-login, 403 only shows a permission notice and signing out of the current account; 503, network loss, and a disabled capability show unavailable with an explicit status retry — never an automatic authorization redirect.
- The callback recognizes only the existing fixed `authError` values and never reads code/state/verifier/token or echoes arbitrary raw errors. `loggedOut=1` stays on the signed-out page to avoid an immediate re-login.
- Signing out of the current account is single-flight: first fetch the dedicated `GET /api/auth/logout/csrf`, then one `POST /api/auth/logout`. The jump to the opaque prepared URL returned by the BFF happens only after the local part finishes; only a local sign-out explicitly states that the upstream outcome is unresolved. A lost or cancelled response only re-queries the local facts — it never replays the POST and never claims a global sign-out.

---

### 0.5 Dashboard (`/dashboard`)

Page title: "To-dos and Overview". Shows only real numbers obtainable from existing APIs; no sample data, trend charts, or activity feeds (those would need new backend statistics APIs, which are outside the frontend scope).

**Metrics** (each card loads independently and links into the corresponding list):

| Group | Metric | Data source | Link |
|------|------|----------|------|
| Pending | Pending uploads | `getUploadRecords({ page: 1, pageSize: 1, status: 1 }).totalCount` | `/upload-records?status=1` |
| Pending | Mistakes awaiting review | `getMistakeItems({ page: 1, size: 1, reviewStatus: 1 }).total` | `/mistakes?reviewStatus=1` |
| Pending | OSS audit records to handle | `ossAuditApi.getStatus().pendingCount` | `/oss-audit?status=0` |
| Scale | Students | `getStudents({ page: 1, pageSize: 1 }).total` | `/students` |
| Scale | Teachers | `getTeachers({ page: 1, pageSize: 1 }).total` | `/teachers` |
| Scale | Assistants | `getAssistants({ page: 1, pageSize: 1 }).total` | `/assistants` |

**Loading and failure**: each card requests in parallel without affecting the others (`Promise.allSettled`). When a request fails or the response lacks the number, the card shows "—" plus "failed to load" and offers a per-card retry; **a failure must never display as 0**. The header "Refresh" reloads all cards.

**List-page deep links** (`src/utils/routeQuery.ts`): upload records (`status` ∈ 1–5), mistakes (`reviewStatus` ∈ 1–3), and OSS audit (`status` ∈ 0–2) read the status filter from the URL and apply it before the first list request; missing, duplicated, non-integer, or out-of-set values count as no filter. While the page stays alive, a change of that URL key (for example clicking a parameter-less sidebar link) re-applies the filter and returns to page 1; switching or resetting the status filter inside the page syncs the key with `router.replace` (other query keys are preserved), so after a refresh the filter matches the URL, and each switch issues exactly one list request.

---

### 1. Student Management Page (`/students`)

**Layout**: filter bar + data table + server-side pagination + dialogs (create/edit/linked accounts/open subjects).

**Feature list**:
- Create a student (name, grade, linked account, open subjects); once the create API returns the student ID, immediately save the checked open subjects
- If the student was created but saving the open subjects failed, keep the created student and prompt the admin to complete it later via "Subjects" — never report the whole create operation as failed
- Student list (search, grade/subject filters, server-side pagination)
- Edit a student (modify info, manage linked accounts, manage open subjects)
- Delete a student
- No "all/linked/unlinked" stat overview bar: the current backend provides no account-wide linkage aggregate, and the current page's counts must not be passed off as totals

**Table columns**: avatar, ID, name, grade, open subjects, created time, actions. Linked accounts get no column of their own; the entry merges into the rightmost actions column.

**Actions column**: Edit, Subjects (emphasized "Assign" style when there are no open subjects yet), Linked Accounts, Delete.

**API calls** (loaded on demand):
- `getGrades()` - load grade options
- `getStudents(params)` - query the student list
- `getIdentityAccountsByStudentId(id)` - fetch linked accounts
- `createStudent(payload)` - create a student
- `updateStudent(id, payload)` - update a student
- `deleteStudent(id)` - delete a student
- `linkIdentityAccount(id, accountId)` - link an account
- `unlinkIdentityAccount(id, accountId)` - unlink an account
- `getStudentOpenSubjects(id, activeOnly)` - fetch open subjects
- `setStudentOpenSubjects(id, payload)` - set open subjects

---

### 2. Teacher Management Page (`/teachers`)

**Layout**: filter bar + data table + server-side pagination + grant dialog + subject management dialog.

**Feature list**:
- Grant teacher permission (search Identity users → choose subjects → grant)
- Teacher list (view subjects, revoke permission)
- Subject management (add/remove the subjects a teacher covers; tags can be closed with the X button)
- Warning badge for unassigned subjects
- Show account remarks (user remarks fetched in batch from the Identity service to help admins identify teachers)
- Keyword, subject, page, and page size are all passed to the Teacher Portal, which does the filtering, total counting, and pagination in the database; the frontend must never fake pagination over a single page of results

**Table columns**:
| Column | Notes |
|------|------|
| Avatar | 36×36 user avatar |
| User ID | Unique Identity user identifier |
| Phone | User phone number |
| Username | Username |
| Remark | Account remark from the Identity service, helping admins identify teachers |
| Subjects | Subject tags the teacher covers (removable, manageable) |
| Created time | When the teacher record was created |
| Actions | Subjects (emphasized "Assign" style when there are none), revoke permission |

**Data loading flow**:
1. Call `getTeachers({ keyword, subject, page, pageSize })` for the database-paginated result
2. Collect the current page's teacher `userId`s and call `getUsersByIds(ids)` to batch-fetch Identity user info
3. Map by `userId` to display remarks and other Identity user fields in the table

**API calls** (loaded on demand):
- `getUsers(params)` - search Identity users
- `getTeachers(params)` - paginated teacher list by keyword/subject
- `addTeacherByUserId(payload)` - add a teacher
- `grantTeacher(userId, payload)` - grant permission
- `removeTeacherByUserId(userId)` - revoke permission
- `getTeacherSubjects(userId)` - fetch a teacher's subjects
- `setTeacherSubjects(userId, subjects)` - set subjects
- `getAvailableSubjects()` - fetch available subjects

---

### 3. Assistant Management Page (`/assistants`)

**Layout**: filter bar + data table + server-side pagination + grant dialog + subject management dialog.

**Feature list**:
- Grant assistant permission (search Identity users → choose subjects → grant)
- Assistant list (view subjects, revoke permission)
- Subject management (add/remove the subjects an assistant covers)
- When no subject is assigned, show a warning badge and replace the "Subjects" action with the emphasized "Assign" style
- Keyword, subject, page, and page size are all passed to the Assistant Portal, which does the filtering, total counting, and pagination in the database; the frontend must never fake pagination over a single page of results

**Table columns**: avatar, ID, phone, username, remark, subjects, created time, actions.

**Actions column**: Subjects (emphasized "Assign" style when there are none), revoke permission.

**API calls**:
- `assistantPortalApi.getAssistants(params)` - paginated assistant list by keyword/subject
- `assistantPortalApi.grantAssistant(userId, payload)` - grant
- `assistantPortalApi.revokeAssistant(userId)` - revoke
- `assistantPortalApi.setAssistantSubjects(userId, subjects)` - set subjects

---

### List Page Conventions

All list pages (students, teachers, assistants, upload records, mistakes, OSS audit — all migrated) uniformly use the shared components under `src/components/list/` instead of each writing its own raw table, hand-rolled modal, or paginator:

| Component | Interface | Conventions |
|------|------|------|
| `PageHeader.vue` | props `title`, `description`; slot `#actions` | Page title area; primary action buttons go in `#actions` |
| `FilterBar.vue` | Default slot holds `el-form-item`s; emits `search` / `reset` | Card container + `el-form inline` with the standard "Search"/"Reset" buttons built in; wraps on narrow screens; filter controls need explicit widths |
| `DataTableCard.vue` | props `data`, `loading`, `error: string \| null`, `emptyText`; default slot holds `el-table-column`s; slot `#footer`; emits `retry`; other attrs pass through to `el-table` | State is decided solely by props: `loading` first (`v-loading`), then `error` (shows the error and "Retry", **not the empty state**), then the empty state based on `data.length`. The card follows its container's width (`contain: inline-size`); wide tables scroll horizontally inside the card, so the page itself never scrolls horizontally |
| `ListPagination.vue` | `v-model:page`, `v-model:page-size`, `total`; emits `change` | `el-pagination` with `layout="total, sizes, prev, pager, next"`, 10/20/50 per page; changing the page size returns to page 1; each interaction triggers exactly one `change` |
| `SubjectTag.vue` | props `subject`, `closable`; emits `close` | Reuses the subject color mapping from `utils/subject.ts` (global `.adm-subj-tag`) |
| `StatusTag.vue` | props `label`, `type: 'success' \| 'warning' \| 'danger' \| 'info' \| 'primary'` | Generic status tag based on `el-tag` |

Usage conventions:

- **Load failure**: when a list fails to load, clear the old rows and set `error`, letting `DataTableCard` show the error state with "Retry"; a toast may accompany it. Failure and "no data" must be distinguishable.
- **Dialogs**: forms and details use `el-dialog` (or `el-drawer`) with `append-to-body` and `:close-on-click-modal="false"` (so clicking the mask cannot lose entered content); Esc closes, and focus stays inside the dialog while open.
- **Forms**: `el-form label-position="top"`, required fields marked with `el-form-item required`; validation hints keep each page's existing `ElMessage.warning` rules. Multi-select subjects use `el-checkbox-group` + `el-checkbox border`.
- **Remote search**: picking Identity users/accounts uses `el-select filterable remote`, whose `remote-method` calls `identityApi.getUsers`: at least 2 characters, pure digits query by phone, `pageSize: 20`. Selected items must stay in options so their labels render. (The add list in the student "Linked Accounts" dialog keeps the `el-input` search + list form, with each row having its own add button and in-progress state.)
- **Row actions**: actions column uses `fixed="right"`; regular actions use `el-button link type="primary"`, while destructive actions such as delete/revoke/unlink use `el-button link type="danger"` and go through the danger double confirmation: call `confirmDanger` from `src/utils/confirm.ts` (conventions under "Design Guidelines → Confirmation Operations") — never `ElMessageBox.confirm` directly.
- **Element Plus language**: `main.ts` registers Element Plus with the `zh-cn` locale, so built-in copy such as pagination and empty states is Chinese.
- **Known limits**: the student page's subject filter filters the current page client-side (the backend `GET /api/admin/students` supports only `name` and `grade`), and the page shows the "subject filter applies only to the current page" notice when enabled; one open-subjects request per row (N+1) is existing behavior. Both need server-side support and are outside the frontend scope.

---

### 4. Upload Record Management Page (`/upload-records`)

**Group**: Content & Uploads

**Layout**: header (refresh) + 5 status stat cards (click to filter by that status, click again to clear) + filter bar (student `el-select filterable remote`, reusing the original rules: no request when empty after trimming, 250ms debounce, `getStudents({ name, pageSize: 20 })`; status) + data table + server-side pagination. Clicking a row or "Details" opens the right-side 480px detail drawer (`src/views/upload-records/UploadRecordDetailDrawer.vue`): the drawer is **non-modal** (`:modal="false" modal-penetrable`) — while open, the list stays clickable to switch details to another row, with the current row highlighted; Esc closes. Reset status, legacy check, and the full VL result are `el-dialog`s.

**Status mapping** (`UPLOAD_STATUSES` in `src/views/upload-records/uploadRecord.ts`, consistent with the backend `UploadStatusConstants.DisplayNames`, shared by stat cards, filters, table, and details): 1 pending / 2 processing / 3 in review / 4 failed / 5 returned.

**Confirmations**: deleting an image uses `confirmDanger` (the copy states that if it is the last image, the whole upload record is deleted with it); returning and resetting status are non-destructive and use a plain confirmation or dialog; legacy cleanup is two-step (check-result dialog → "Confirm Clean" inside that dialog).

**Managed assignment**: choose legacy or managed-v1 per the server-returned `assignmentProtocol`; when the protocol is missing or unknown, assigning is impossible. Managed mode shows the exact `contentRevision`, the ordered types and paths with their original indexes, and only allows selecting images with `type === "mistake"`. Before each group is confirmed, fix the UUID `requestKey`, revision, exact `sourcePaths` or `sourceRegions`, and subject/grade/comments. The two inputs are mutually exclusive; a region can be the whole image or a crop rectangle — see the [managed assignment notes](../../docs/Integration/ManagedAssignment.md) for the rules. While sending, modifications are forbidden and a double click sends only once; each group shows Completed/Failed/Unknown/NotAttempted with the real IDs, and only all-Completed closes with a success notice. On partial success or Unknown, the page keeps the original request and the existing IDs and explicitly retries the whole original batch instead of rebuilding from refreshed indexes. Switching rows or closing the drawer increments the operation number `generation` so a late response from an old operation cannot overwrite the current content; cancelling only stops waiting on the current operation, and the resume entry can still reopen the original request. After the whole page closes, recovering the in-memory record is not guaranteed; permanent results are kept by Mistake. Admin adds no browser-side persistent operation log, never proactively seals the source (seal: after source hand-over, reject new groups), and never runs the old cleanup.

**Feature list**:
- Upload record list (student search filter, status filter, pagination)
- Total record count
- Thumbnail column (shows the first image's thumbnail; click to preview all images)
- View details (image thumbnails, rotation, large-image preview)
- Assign records per image (select images → choose subject/grade/type → create mistake/homework entries)
- Reset status
- Rotate images
- Legacy data check and cleanup (checked record by record)

#### Legacy Data Check and Cleanup

**Background**: an early-system bug left upload records undeleted after a teacher approved a mistake review, with image paths unmigrated (still under `uploads/` instead of `mistakes/`). This legacy data needs safe cleanup.

**Design**: instead of a standalone full-scan page, each upload record's actions column offers a "Check & Clean" button that checks that single record for legacy status. Benefits:
1. Each check covers one upload record, not a full scan
2. Admins can inspect suspicious records at any time while browsing
3. Avoids unnecessary full-scan overhead

**Check logic**:

| Condition | Verdict | Returned info |
|------|------|----------|
| No linked mistakes | Not legacy | "This record has no linked mistakes" |
| All mistakes reviewed (Confirmed/Rejected) and none awaiting review | **Legacy data** | isLegacy=true, mistakeCount, hasUploadPathImage |
| Some mistakes awaiting review | Not legacy | "This record still has N mistakes awaiting review" |

**Cleanup flow**:

```
Admin clicks the "Check & Clean" button
    ↓
GET /api/admin/oss-upload-records/legacy-check/{id}
    ↓
Show the check-result dialog:
    - Not legacy: show the notice, "Close" button only
    - Legacy data: show mistake count, image migration status, cleanup instructions
    ↓
(For legacy data) Admin clicks "Confirm Clean"
    ↓
POST /api/admin/oss-upload-records/legacy-clean/{id}
    ↓
Backend processing flow:
┌─────────────────────────────────────────────────────┐
│ 1. Verify the record is legacy (all mistakes        │
│    reviewed)                                        │
│    - Abort with an error if the condition fails     │
└─────────────────────────────────────────────────────┘
    ↓
┌─────────────────────────────────────────────────────┐
│ 2. CompleteUploadReview (gRPC)                      │
│    - Migrate the physical files: uploads/ →         │
│      mistakes/                                      │
│    - Update image paths in the mistake records      │
│    - Persist to the database                        │
└─────────────────────────────────────────────────────┘
    ↓
┌─────────────────────────────────────────────────────┐
│ 3. DeleteUploadRecordAfterReview (gRPC)             │
│    - Delete the upload record                       │
│    - Clean up associated data                       │
└─────────────────────────────────────────────────────┘
    ↓
Success → refresh the list
```

**Key constraints**:

1. **No direct database access**: every operation goes through the gRPC business interfaces
2. **Verify before cleaning**: cleanup must be preceded by a legacy-check verification
3. **Irreversible**: deletion requires a second admin confirmation
4. **Atomicity**: image migration and path updates complete within the same request

**API calls** (loaded on demand):
- `getUploadRecords(params)` - query upload records
- `getSubjectOptions()` - subject options (for choosing a subject while assigning)
- `getGrades()` - grade options (for choosing a grade while assigning)
- `getLinkedItems(id)` - fetch the business entities linked to an upload record
- `getStudents(params)` - search students (for assigning)
- Details use the list item's metadata; no per-record GET detail route is added.
- `assignUploadRecord(id, payload)` - assign a record per image (items array; each item specifies imageIndices + subject + grade + classification)
- `resetUploadRecordStatus(id, status)` - reset status
- `rotateUploadImage(id, payload)` - rotate an image
- `legacyCheck(id)` - check whether one upload record is legacy data
- `legacyClean(id)` - clean one legacy record

---

### 5. Mistake Management Page (`/mistakes`)

**Layout**: header (refresh) + 4 stat cards (total mistakes / awaiting review / confirmed / returned; click to filter by that review status, click again or click "Total" to clear) + filter bar (student `el-select filterable remote`, reusing the original rules: no request when empty after trimming, 250ms debounce, `getStudents({ name, pageSize: 20 })`; subject, grade, review status) + data table (rows awaiting review highlighted) + server-side pagination (`size` 10/20/50). "Details" and "Edit" open the same 700px `el-dialog` (`src/views/mistakes/MistakeDetailDialog.vue`): info and review info (read-only — this page offers no review actions), mistake images (with a migration banner for `uploads/` paths), and the edit form (subject, grade, student required). The review status tags come from the `reviewStatuses` in `enum-options`, falling back to local defaults on failure.

**Feature list**:
- Mistake list (student search, subject/grade/review-status filters, pagination)
- Total record count
- View details (images, full info)
- Edit a mistake (change student, subject, grade)
- Migrate images (`uploads/` → `mistakes/`)

**API calls** (loaded on demand):
- `getStudents(params)` - search students
- `getSubjectOptions()` - subject options
- `getGrades()` - grade options
- `getEnumOptions()` - enum options (including review statuses)
- `getMistakeItems(params)` - query the mistake list
- `getMistakeItem(id)` - fetch a single mistake
- `updateMistakeItem(id, data)` - update a mistake

---

### 6. OSS Audit Page (`/oss-audit`)

**Group**: Storage Audit. Behavioral rules live in [StorageAudit](../../docs/modules/OssAudit/StorageAudit.md); the page calls the same version of the StorageReferences interface.

**Layout**: trigger full scan (plain confirmation; disabled while running or triggering) + status panel + status/bucket/current-page path filters + record table + server-side pagination (10/20/50) + the historical Pending ignore dialog. The current view has no selection column, delete buttons, bulk action bar, or `resolve` event handling; deletion cannot be triggered through an old status or forged component events.

**Status**: keeps the historical statuses 0 pending, 1 deleted, 2 ignored; status 3 is the read-only "no observed references" observation. Only historical status 0 shows `ignore`; every other action is an em dash. The status panel distinguishes historical pending from `observationCount`; new v1 runs show "no observed references" and old runs show "historical findings" — never fabricating results of the new verification. The per-service entry counts and collection times are shown only when all three snapshot metadata sets (student/mistake/homework) are complete; corrupted or missing metadata must not display as complete. `deletionAuthorized` is always `false` and is not a frontend-configurable switch.

`GET status` supports isRunning, lastCompleted/lastFailed, pendingCount, observationCount; lastCompleted keeps the NewZombieCount compatibility field and adds referenceContractVersion/referenceSnapshots. Poll every 10 seconds; refresh the list when a run completes. On API failure, clear the list and show the error state with retry; the path search filters only the current page and reports the match count, while the total stays the server-returned total. The page calls only getRecords/getStatus/triggerAudit/ignoreRecord; the compatibility `resolve` API still exists, but the backend returns 409 after the complete reference check (502 when references are unavailable), and the page never calls it.

---

## API Service Layer

### studentAdminApi.ts

| Method | HTTP | Path | Notes |
|------|------|------|------|
| `getGrades()` | GET | `/api/admin/students/grades` | Fetch grade options |
| `getStudents(params)` | GET | `/api/admin/students` | Paginated student query |
| `getStudent(id)` | GET | `/api/admin/students/:id` | Fetch a single student |
| `createStudent(payload)` | POST | `/api/admin/students` | Create a student |
| `updateStudent(id, payload)` | PUT | `/api/admin/students/:id` | Update a student |
| `deleteStudent(id)` | DELETE | `/api/admin/students/:id` | Delete a student |
| `getIdentityAccountsByStudentId(id)` | GET | `/api/admin/students/:id/accounts` | Fetch linked accounts |
| `linkIdentityAccount(id, accountId)` | POST | `/api/admin/students/:id/accounts` | Link an account |
| `unlinkIdentityAccount(id, accountId)` | DELETE | `/api/admin/students/:id/accounts/:accountId` | Unlink an account |
| `getStudentsByAccountId(accountId)` | GET | `/api/admin/accounts/:accountId/students` | Query students by account |
| `getIdentityAccountsBatch(ids)` | POST | `/api/admin/identity-accounts/batch` | Batch-fetch accounts |
| `getSubjectOptions()` | GET | `/api/admin/students/subject-options` | Subject options |
| `getStudentOpenSubjects(id, activeOnly)` | GET | `/api/admin/students/:id/open-subjects` | Open subjects |
| `setStudentOpenSubjects(id, payload)` | PUT | `/api/admin/students/:id/open-subjects` | Set open subjects |
| `getUploadRecords(params)` | GET | `/api/admin/oss-upload-records` | Query upload records |
| Detail data | GET | `/api/admin/oss-upload-records` | List items directly carry the detail's revision/ordered entries |
| `resetUploadRecordStatus(id, status)` | POST | `/api/admin/oss-upload-records/:id/reset-status` | Reset status |
| `assignUploadRecord(id, payload)` | POST | `/api/admin/oss-upload-records/:id/assign` | Assign a record per image (items array; each item specifies imageIndices (original indexes) + subject + grade + classification) |
| `getLinkedItems(id)` | GET | `/api/admin/oss-upload-records/:id/linked-items` | Fetch linked business entities |
| `rotateUploadImage(id, payload)` | POST | `/api/admin/oss-upload-records/:id/rotate` | Rotate an image |
| `getMistakeItems(params)` | GET | `/api/admin/mistakes` | Query the mistake list |
| `getMistakeItem(id)` | GET | `/api/admin/mistakes/:id` | Fetch a single mistake |
| `updateMistakeItem(id, data)` | PUT | `/api/admin/mistakes/:id` | Update a mistake |
| `getMistakesByUploadId(uploadId)` | GET | `/api/admin/mistakes/by-upload/:uploadId` | Query mistakes by upload record |
| `getEnumOptions()` | GET | `/api/admin/enum-options` | Fetch enum options |
| `legacyCheck(id)` | GET | `/api/admin/oss-upload-records/legacy-check/:id` | Check whether one upload record is legacy data |
| `legacyClean(id)` | POST | `/api/admin/oss-upload-records/legacy-clean/:id` | Clean one legacy record |

### identityApi.ts

| Method | HTTP | Path | Notes |
|------|------|------|------|
| `testConnection()` | GET | `/api/identity/gateway/users/search` | Test connectivity |
| `getUsers(params)` | GET | `/api/identity/gateway/users/search` | Search users |
| `getUsersByIds(ids)` | POST | `/api/identity/gateway/users/batch` | Batch-fetch users |

### teacherPortalApi.ts

| Method | HTTP | Path | Notes |
|------|------|------|------|
| `getTeachers(params)` | GET | `/api/teacher-portal/admin/teachers` | Paginated teacher list by keyword/subject/page/pageSize |
| `addTeacherByUserId(payload)` | POST | `/api/teacher-portal/admin/teachers/by-user` | Add a teacher |
| `grantTeacher(userId, payload)` | POST | `/api/teacher-portal/admin/identity-users/:userId/grant-teacher` | Grant permission |
| `removeTeacherByUserId(userId)` | DELETE | `/api/teacher-portal/admin/teachers/by-user/:userId` | Revoke permission |
| `checkTeacher(userId, phone?)` | GET | `/api/teacher-portal/auth/check-teacher` | Check teacher identity |
| `getTeacherSubjects(userId)` | GET | `/api/teacher-portal/admin/teachers/:userId/subjects` | Fetch a teacher's subjects |
| `setTeacherSubjects(userId, subjects)` | PUT | `/api/teacher-portal/admin/teachers/:userId/subjects` | Set subjects |
| `addTeacherSubject(userId, subject)` | POST | `/api/teacher-portal/admin/teachers/:userId/subjects/:subject` | Add a subject |
| `removeTeacherSubject(userId, subject)` | DELETE | `/api/teacher-portal/admin/teachers/:userId/subjects/:subject` | Remove a subject |
| `getAvailableSubjects()` | GET | `/api/teacher-portal/admin/available-subjects` | Available subjects |

### assistantPortalApi.ts

| Method | HTTP | Path | Notes |
|------|------|------|------|
| `getAssistants(params)` | GET | `/api/assistant-portal/admin/assistants` | Paginated assistant list by keyword/subject/page/pageSize |
| `grantAssistant(userId, payload)` | POST | `/api/assistant-portal/admin/identity-users/:userId/grant-assistant` | Grant permission |
| `revokeAssistant(userId)` | POST | `/api/assistant-portal/admin/identity-users/:userId/revoke-assistant` | Revoke permission |
| `getAssistantSubjects(userId)` | GET | `/api/assistant-portal/admin/assistants/:userId/subjects` | Fetch an assistant's subjects |
| `setAssistantSubjects(userId, subjects)` | PUT | `/api/assistant-portal/admin/assistants/:userId/subjects` | Set subjects |
| `addAssistantSubject(userId, subject)` | POST | `/api/assistant-portal/admin/assistants/:userId/subjects/:subject` | Add a subject |
| `removeAssistantSubject(userId, subject)` | DELETE | `/api/assistant-portal/admin/assistants/:userId/subjects/:subject` | Remove a subject |
| `getAvailableSubjects()` | GET | `/api/assistant-portal/admin/available-subjects` | Available subjects |

### ossAuditApi.ts

| Method | HTTP | Path | Notes |
|------|------|------|------|
| `getRecords(params)` | GET | `/api/admin/oss-audit/records` | Query audit records |
| `triggerAudit()` | POST | `/api/admin/oss-audit/trigger` | Trigger an audit |
| `resolveRecord(id)` | POST | `/api/admin/oss-audit/records/:id/resolve` | v1 refuses cleanup |
| `ignoreRecord(id, note)` | POST | `/api/admin/oss-audit/records/:id/ignore` | Ignore one record |
| `batchResolve(ids)` | POST | `/api/admin/oss-audit/records/batch-resolve` | v1 refuses batch cleanup |

---

## Core Type Definitions

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
  contentRevision?: string; imageEntries?: { path: string; type: string }[];
  assignmentProtocol?: 'legacy' | 'managed-v1';
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

// imageIndices only interprets the current trusted detail; managed sends fixed sourcePaths and never remaps against a later current.
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

## Design Guidelines

- **UI framework**: Element Plus in `size="small"` compact mode
- **Layout**: collapsible sidebar (grouped by function, generated from route meta) + top bar + main content area — see "Design Guidelines → Overall Layout" above
- **Pagination**: user-management lists default to 20 per page; students are paginated by the Student service, and teachers/assistants are filtered, counted, and paginated by the Teacher Portal / Assistant Portal in database queries — the frontend only renders the server-returned current page
- **Lazy routes**: page components load on demand
- **APIs called on demand**: each page calls only the endpoints it needs
- **Error handling**: Axios errors uniformly extract `response.data.message` and surface it with `ElMessage.error`
- **Confirmation operations**: destructive operations such as delete, revoke permission, remove grant, and unlink must use `confirmDanger({ title, message, confirmText })` from `src/utils/confirm.ts`: a danger icon and danger button, the confirm button gets no focus on open (Enter cannot execute), clicking the mask does not close, and Esc/cancel returns `false` without throwing. The copy states the object and the consequence; use `h()` to build VNodes when emphasis is needed — `dangerouslyUseHTMLString` is **forbidden** (names and paths come from backend data). Callers write `if (!(await confirmDanger(...))) return` and handle API errors in their own try/catch. Non-destructive operations (return, reset status, ignore, trigger scan, etc.) do not use the danger style.
- **Authentication**: the browser uses only the same-origin HttpOnly opaque `adminSession`; access/id tokens stay in BFF memory. Initialization only attempts to remove the historical `adminAuthToken`, `adminRefreshToken`, and `adminUsername`, never reading those values as identity; the sidebar layout preference is unaffected.
- **Shared HTTP client**: all API services reuse `services/httpClient.ts`. Only same-origin `/api/` is allowed; Authorization and axios auth are stripped; unsafe requests single-flight a plain CSRF token and carry the single-value `X-CSRF-TOKEN`; no write request is ever auto-retried.
- **Session number and failures**: every session invalidation or sign-out increments the session number `generation`; 401/403 responses from an old initialization or old API cannot overwrite the new session, and when multiple requests return 401 at once, the controlled login-page navigation runs exactly once. A cancelled request never replays partial writes.
- **Image loading**: same-origin `<img>` / `<el-image>` automatically carries the server session cookie, authorized by the existing image endpoint and then redirected; no JWT, Authorization, or browser token is ever concatenated. Images use the same current-admin boundary as the business APIs.
- **Image endpoint URL convention**: `GET /api/admin/image?path=<ossPath>&size=<small|medium|empty>`, keeping the existing server-side admin-session authorization, returns a 302 redirect to an OSS presigned URL; the browser then pulls the image from the platform's public `https://oss.example.com/oss/` entry, proxied uniformly by the User Web Nginx.
  - List thumbnails: `size=small`
  - Detail-page medium images: `size=medium`
  - Detail-page click-to-zoom preview: no `size` (returns the original image)
  - Path routing: `mistakes/`-prefixed paths go to the Mistake service; all other paths go to the Student service

Since #75, the backend uniformly uses the server session and refuses to start when required auth configuration is invalid; the five legacy switches accept only omission or the exact `true` as a migration check — disabled or JWT modes cannot be selected. The frontend keeps its same-origin cookie/plain and logout-dedicated CSRF, explicit hosted re-authentication, and no-write-replay behavior; the API+SPA must use the same complete integrated image. Before an upgrade, run a read-only preflight with the target image; a restart invalidates in-memory tickets, and a rollback needs the complete old image plus matching configuration. See the [deployment contract](../../docs/development/Deployment.md#authentication-read-only-preflight-and-upgrade).
