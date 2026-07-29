<template>
  <div class="adm-fade-in user-management-view">
    <!-- Page Header -->
    <div class="adm-page-header">
      <div>
        <h1 class="adm-page-title">学生管理</h1>
        <p class="adm-page-subtitle">管理学生档案、关联账户与开放学科</p>
      </div>
      <div class="adm-page-actions">
        <button class="adm-btn adm-btn-primary" @click="openCreateDialog">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
          创建学生
        </button>
      </div>
    </div>

    <!-- Filter Bar -->
    <div class="adm-filter-bar">
      <div class="filter-input-wrap">
        <svg class="filter-input-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
        <input v-model="searchName" class="filter-input" placeholder="搜索学生姓名或 ID" @keyup.enter="onSearch" />
      </div>
      <select v-model="searchGrade" class="filter-select" @change="onSearch">
        <option :value="undefined">全部年级</option>
        <option v-for="g in gradeOptions" :key="g.value" :value="g.value">{{ g.label }}</option>
      </select>
      <select v-model="searchSubject" class="filter-select" @change="onSearch">
        <option :value="undefined">全部学科</option>
        <option v-for="s in subjectOptions" :key="s.value" :value="s.value">{{ s.displayName || s.name }}</option>
      </select>
      <div class="filter-spacer"></div>
      <button class="adm-btn adm-btn-primary adm-btn-sm" @click="onSearch">
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
        搜索
      </button>
      <button class="adm-btn adm-btn-secondary adm-btn-sm" @click="resetFilters">
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/></svg>
        重置
      </button>
    </div>

    <!-- Data Table -->
    <div class="adm-card table-card">
      <div v-if="loading" class="loading-row">
        <span class="spinner-lg"></span>
        <span>加载中...</span>
      </div>
      <div v-else-if="students.length === 0" class="adm-empty">
        <div class="adm-empty-icon">
          <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/></svg>
        </div>
        <div class="adm-empty-text">暂无学生数据</div>
      </div>
      <div v-else class="table-wrap">
        <table class="adm-table">
          <thead>
            <tr>
              <th class="col-avatar">头像</th>
              <th class="col-id">ID</th>
              <th class="col-name">姓名</th>
              <th class="col-grade">年级</th>
              <th class="col-subjects">开放学科</th>
              <th class="col-time">创建时间</th>
              <th class="col-actions">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="s in students" :key="s.id">
              <td>
                <div class="avatar-cell">
                  <div class="user-avatar" :style="{ background: getAvatarGradient(s.name || s.id) }">{{ getAvatarChar(s.name || '?') }}</div>
                </div>
              </td>
              <td><span class="mono-id">{{ s.id }}</span></td>
              <td><span class="user-name">{{ s.name }}</span></td>
              <td><span class="grade-text">{{ getGradeLabel(s.grade) }}</span></td>
              <td>
                <div v-if="getStudentSubjects(s).length > 0" class="subj-tags">
                  <span v-for="subj in getStudentSubjects(s)" :key="subj" class="adm-subj-tag" :class="getSubjectCssClass(subj)">{{ getSubjectLabel(subj) }}</span>
                </div>
                <span v-else class="text-muted">未设置</span>
              </td>
              <td><span class="time-cell">{{ formatDate(s.createdAt) }}</span></td>
              <td>
                <div class="actions-cell">
                  <button class="adm-btn-link" @click="openEditDialog(s)">编辑</button>
                  <button v-if="getStudentSubjects(s).length > 0" class="adm-btn-link" @click="openSubjectsDialog(s)">学科</button>
                  <button v-else class="adm-btn adm-btn-primary adm-btn-xs" @click="openSubjectsDialog(s)">分配</button>
                  <button class="adm-btn-link" @click="openLinkedAccountsDialog(s)">关联账户</button>
                  <button class="adm-btn-link danger" @click="deleteStudent(s)">删除</button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Pagination -->
      <div v-if="!loading && students.length > 0" class="adm-pagination-bar">
        <span class="page-info">共 {{ total }} 条记录</span>
        <div class="page-controls">
          <select v-model="pageSize" class="page-size-select" @change="onPageChange(1)">
            <option :value="10">10 / 页</option>
            <option :value="20">20 / 页</option>
            <option :value="50">50 / 页</option>
          </select>
          <button class="page-btn" :disabled="page <= 1" @click="onPageChange(page - 1)">上一页</button>
          <span class="page-current">{{ page }} / {{ totalPages }}</span>
          <button class="page-btn" :disabled="page >= totalPages" @click="onPageChange(page + 1)">下一页</button>
        </div>
      </div>
    </div>

    <!-- Create Student Modal -->
    <div v-if="showCreateDialog" class="modal-mask" @click.self="closeCreateDialog">
      <div class="modal-box" style="width: 520px;">
        <div class="modal-header">
          <h3>创建学生</h3>
          <button class="modal-close" @click="closeCreateDialog">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label">关联账户</label>
            <div class="user-search-wrap">
              <input v-model="createSearchKeyword" class="form-input" placeholder="输入用户名或手机号搜索" @input="searchCreateIdentityUsers" />
              <div v-if="createIdentityUsers.length > 0" class="user-dropdown">
                <div
                  v-for="u in createIdentityUsers"
                  :key="u.userId"
                  class="user-search-option"
                  :class="{ selected: createForm.identityAccountIds.includes(u.userId) }"
                  @click="addCreateIdentityAccount(u.userId)"
                >
                  <div class="mini-avatar" :style="{ background: getAvatarGradient(u.displayName || u.username || u.userId) }">{{ getAvatarChar(u.displayName || u.username || '?') }}</div>
                  <div class="user-search-option-info">
                    <div class="user-search-option-name">{{ u.username || u.displayName || u.userId }}</div>
                    <div class="user-search-option-meta">{{ u.phone || '无手机号' }}<span v-if="u.displayName && u.displayName !== u.username"> · {{ u.displayName }}</span></div>
                  </div>
                  <span v-if="createForm.identityAccountIds.includes(u.userId)" class="user-search-option-state">已选</span>
                </div>
              </div>
            </div>
            <div v-if="createForm.identityAccountIds.length > 0" class="selected-accounts">
              <span v-for="id in createForm.identityAccountIds" :key="id" class="account-chip">
                {{ getAccountDisplayName(id) }}
                <button class="chip-x" @click="removeCreateIdentityAccount(id)">×</button>
              </span>
            </div>
          </div>
          <div class="form-group">
            <label class="form-label">学生姓名 <span class="required">*</span></label>
            <input v-model="createForm.name" class="form-input" placeholder="请输入学生姓名" />
          </div>
          <div class="form-group">
            <label class="form-label">年级</label>
            <select v-model="createForm.grade" class="form-input">
              <option :value="0">请选择年级</option>
              <option v-for="g in gradeOptions" :key="g.value" :value="g.value">{{ g.label }}</option>
            </select>
          </div>
          <div class="form-group">
            <label class="form-label">开放学科</label>
            <div class="subj-grid">
              <label
                v-for="s in subjectOptions"
                :key="s.value"
                class="subject-check"
                :class="{ checked: createOpenSubjects.includes(s.value) }"
                @click.prevent="toggleCreateSubject(s.value)"
              >
                <span class="chk-box">
                  <svg v-if="createOpenSubjects.includes(s.value)" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                </span>
                <span>{{ s.displayName || s.name }}</span>
              </label>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="adm-btn adm-btn-secondary" @click="closeCreateDialog">取消</button>
          <button class="adm-btn adm-btn-primary" :disabled="creating" @click="createStudent">{{ creating ? '创建中...' : '创建' }}</button>
        </div>
      </div>
    </div>

    <!-- Edit Student Modal -->
    <div v-if="showEditDialog" class="modal-mask" @click.self="showEditDialog = false">
      <div class="modal-box" style="width: 460px;">
        <div class="modal-header">
          <h3>编辑学生</h3>
          <button class="modal-close" @click="showEditDialog = false">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label">学生姓名 <span class="required">*</span></label>
            <input v-model="editForm.name" class="form-input" placeholder="请输入学生姓名" />
          </div>
          <div class="form-group">
            <label class="form-label">年级</label>
            <select v-model="editForm.grade" class="form-input">
              <option :value="0">请选择年级</option>
              <option v-for="g in gradeOptions" :key="g.value" :value="g.value">{{ g.label }}</option>
            </select>
          </div>
        </div>
        <div class="modal-footer">
          <button class="adm-btn adm-btn-secondary" @click="showEditDialog = false">取消</button>
          <button class="adm-btn adm-btn-primary" @click="saveEdit">保存</button>
        </div>
      </div>
    </div>

    <!-- Linked Accounts Modal -->
    <div v-if="showLinkedAccountsDialog" class="modal-mask" @click.self="closeLinkedAccountsDialog">
      <div class="modal-box" style="width: 640px;">
        <div class="modal-header">
          <h3>关联账户</h3>
          <button class="modal-close" @click="closeLinkedAccountsDialog">×</button>
        </div>
        <div class="modal-body">
          <div v-if="currentStudent" class="user-info-banner">
            <div class="user-avatar" :style="{ background: getAvatarGradient(currentStudent.name || currentStudent.id) }">{{ getAvatarChar(currentStudent.name || '?') }}</div>
            <div>
              <div class="info-name">{{ currentStudent.name }}</div>
              <div class="info-sub">{{ getGradeLabel(currentStudent.grade) }} · {{ currentStudent.id }}</div>
            </div>
          </div>

          <div v-if="linkedAccountsLoading" class="drawer-loading">
            <span class="spinner-sm"></span>
            <span>加载中...</span>
          </div>

          <div v-else>
            <!-- 已关联账户 -->
            <div class="drawer-section">
              <div class="drawer-section-title">
                已关联账户
                <span class="count">{{ linkedAccountDetails.length }}</span>
              </div>
              <div v-if="linkedAccountDetails.length === 0" class="drawer-empty">暂无关联账户</div>
              <div v-for="a in linkedAccountDetails" :key="a.userId" class="account-row">
                <div class="mini-avatar" :style="{ background: getAvatarGradient(a.displayName || a.username || a.userId) }">{{ getAvatarChar(a.displayName || a.username || '?') }}</div>
                <div class="account-row-info">
                  <div class="account-row-name">{{ a.displayName || a.username || a.userId }}</div>
                  <div class="account-row-meta">{{ a.phone || '无手机号' }}<span v-if="a.remark"> · {{ a.remark }}</span></div>
                </div>
                <button class="chip-remove" title="移除关联" :disabled="busyLinkedUserId === a.userId" @click="runLinkAccount(a.userId, 'unlink')">
                  <svg v-if="busyLinkedUserId !== a.userId" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                  <span v-else class="spinner-sm"></span>
                </button>
              </div>
            </div>

            <!-- 搜索添加账户 -->
            <div class="drawer-section">
              <div class="drawer-section-title">添加账户</div>
              <div class="link-search-wrap">
                <input v-model="accountSearchKeyword" class="form-input link-search-input" placeholder="搜索用户名或手机号添加（至少 2 个字符）" @input="searchAccountsForLink" />
                <span v-if="searchingAccounts" class="link-search-spinner"><span class="spinner-sm"></span></span>
              </div>
              <div v-if="accountSearchKeyword && availableAccountsToAdd.length === 0 && !searchingAccounts" class="drawer-empty-hint">无符合条件的可添加账户</div>
              <div v-for="u in availableAccountsToAdd" :key="u.userId" class="account-row addable">
                <div class="mini-avatar" :style="{ background: getAvatarGradient(u.username || u.userId) }">{{ getAvatarChar(u.username || '?') }}</div>
                <div class="account-row-info">
                  <div class="account-row-name">{{ u.username || u.userId }}</div>
                  <div class="account-row-meta">{{ u.phone || '无手机号' }}</div>
                </div>
                <button class="chip-add" title="添加关联" :disabled="busyLinkedUserId === u.userId" @click="runLinkAccount(u.userId, 'link')">
                  <svg v-if="busyLinkedUserId !== u.userId" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                  <span v-else class="spinner-sm"></span>
                </button>
              </div>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="adm-btn adm-btn-secondary" @click="closeLinkedAccountsDialog">关闭</button>
        </div>
      </div>
    </div>

    <!-- Manage Open Subjects Modal -->
    <div v-if="showOpenSubjectsDialogVisible" class="modal-mask" @click.self="showOpenSubjectsDialogVisible = false">
      <div class="modal-box" style="width: 560px;">
        <div class="modal-header">
          <h3>管理开放学科</h3>
          <button class="modal-close" @click="showOpenSubjectsDialogVisible = false">×</button>
        </div>
        <div class="modal-body">
          <div v-if="currentStudent" class="user-info-banner">
            <div class="user-avatar" :style="{ background: getAvatarGradient(currentStudent.name || currentStudent.id) }">{{ getAvatarChar(currentStudent.name || '?') }}</div>
            <div>
              <div class="info-name">{{ currentStudent.name }}</div>
              <div class="info-sub">{{ getGradeLabel(currentStudent.grade) }}</div>
            </div>
          </div>
          <div class="form-group">
            <label class="form-label">已开放学科</label>
            <div class="subj-grid">
              <label
                v-for="s in subjectOptions"
                :key="s.value"
                class="subject-check"
                :class="{ checked: openSubjects.includes(s.value) }"
                @click.prevent="toggleOpenSubject(s.value)"
              >
                <span class="chk-box">
                  <svg v-if="openSubjects.includes(s.value)" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                </span>
                <span>{{ s.displayName || s.name }}</span>
              </label>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="adm-btn adm-btn-secondary" @click="showOpenSubjectsDialogVisible = false">取消</button>
          <button class="adm-btn adm-btn-primary" :disabled="savingSubjects" @click="saveOpenSubjects">{{ savingSubjects ? '保存中...' : '保存' }}</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import studentAdminApi, { type IdentityAccountDto, type SubjectOption, type GradeOption } from '../services/studentAdminApi'
import identityApi, { type IdentityUser } from '../services/identityApi'
import { extractMsg } from '../services/apiBase'
import {
  getSubjectLabel, getSubjectCssClass,
  getAvatarGradient, getAvatarChar, formatDate,
} from '../utils/subject'

interface StudentRow {
  id: string
  name: string
  grade: number
  identityAccountIds: string[]
  createdAt: number | string
  openSubjects?: number[]
}

const students = ref<StudentRow[]>([])
const loading = ref(false)
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)

const searchName = ref('')
const searchGrade = ref<number | undefined>(undefined)
const searchSubject = ref<number | undefined>(undefined)

const gradeOptions = ref<GradeOption[]>([])
const subjectOptions = ref<SubjectOption[]>([])
const accountMap = ref<Map<string, IdentityAccountDto>>(new Map())
const studentSubjectMap = ref<Map<string, number[]>>(new Map())

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))

const showCreateDialog = ref(false)
const createForm = ref({ name: '', grade: 0, identityAccountIds: [] as string[] })
const createIdentityUsers = ref<IdentityUser[]>([])
const createSearchKeyword = ref('')
const createOpenSubjects = ref<number[]>([])
const creating = ref(false)

const showEditDialog = ref(false)
const editForm = ref({ id: '', name: '', grade: 0 })

const currentStudent = ref<StudentRow | null>(null)

const showOpenSubjectsDialogVisible = ref(false)
const openSubjects = ref<number[]>([])
const savingSubjects = ref(false)

// Linked accounts dialog — manages all Identity accounts linked to the student (no role filter)
const showLinkedAccountsDialog = ref(false)
const linkedAccountsLoading = ref(false)
const linkedAccountIds = ref<string[]>([])
const linkedAccountDetails = ref<IdentityAccountDto[]>([])
const accountSearchKeyword = ref('')
const accountSearchResults = ref<IdentityUser[]>([])
const searchingAccounts = ref(false)
const busyLinkedUserId = ref<string | null>(null)

const availableAccountsToAdd = computed(() => {
  const linkedIds = new Set(linkedAccountIds.value.map(id => id.toLowerCase()))
  return accountSearchResults.value.filter(u => !linkedIds.has(u.userId.toLowerCase()))
})

async function loadGradeOptions() {
  try {
    gradeOptions.value = await studentAdminApi.getGrades()
  } catch (e) {
    console.error('Failed to load grades:', e)
  }
}

async function loadSubjectOptions() {
  try {
    subjectOptions.value = await studentAdminApi.getSubjectOptions()
  } catch (e) {
    console.error('Failed to load subjects:', e)
  }
}

async function loadStudents() {
  loading.value = true
  try {
    const result = await studentAdminApi.getStudents({
      page: page.value,
      pageSize: pageSize.value,
      name: searchName.value || undefined,
      grade: searchGrade.value,
    })
    let items = result.items as StudentRow[]
    // Apply client-side subject filter
    if (searchSubject.value !== undefined) {
      // We may need to fetch open subjects per student; do it lazily
      await loadOpenSubjectsForList(items)
      items = items.filter(s => (studentSubjectMap.value.get(s.id) || []).includes(searchSubject.value as number))
    }
    students.value = items
    total.value = searchSubject.value === undefined ? result.total : items.length
    await loadAccountMap()
    await loadOpenSubjectsForList(items)
  } catch (e) {
    ElMessage.error('加载学生列表失败')
  } finally {
    loading.value = false
  }
}

async function loadOpenSubjectsForList(items: StudentRow[]) {
  for (const s of items) {
    if (studentSubjectMap.value.has(s.id)) continue
    try {
      const list = await studentAdminApi.getStudentOpenSubjects(s.id, true)
      studentSubjectMap.value.set(s.id, list.map(x => x.subject))
    } catch {
      studentSubjectMap.value.set(s.id, [])
    }
  }
}

function getStudentSubjects(s: StudentRow): number[] {
  return studentSubjectMap.value.get(s.id) || s.openSubjects || []
}

async function searchAccountsForLink() {
  if (!accountSearchKeyword.value || accountSearchKeyword.value.length < 2) {
    accountSearchResults.value = []
    return
  }
  searchingAccounts.value = true
  try {
    const keyword = accountSearchKeyword.value
    const isPhone = /^\d+$/.test(keyword)
    const result = await identityApi.getUsers({
      username: isPhone ? undefined : keyword,
      phone: isPhone ? keyword : undefined,
      page: 1,
      pageSize: 20,
    })
    accountSearchResults.value = result.items
  } catch (error) {
    console.error('Search identity users failed:', error)
    ElMessage.error('搜索用户失败')
  } finally {
    searchingAccounts.value = false
  }
}

async function runLinkAccount(accountId: string, action: 'link' | 'unlink') {
  if (!accountId || !currentStudent.value) return
  busyLinkedUserId.value = accountId
  try {
    await (action === 'link'
      ? studentAdminApi.linkIdentityAccount(currentStudent.value.id, accountId)
      : studentAdminApi.unlinkIdentityAccount(currentStudent.value.id, accountId))
    ElMessage.success(action === 'link' ? '添加成功' : '移除成功')
    await refreshLinkedAccounts()
    await loadStudents()
  } catch (error: unknown) {
    ElMessage.error(extractMsg(error))
  } finally {
    busyLinkedUserId.value = null
  }
}

async function refreshLinkedAccounts() {
  if (!currentStudent.value) return
  try {
    const ids = await studentAdminApi.getIdentityAccountsByStudentId(currentStudent.value.id)
    linkedAccountIds.value = ids
    if (ids.length > 0) {
      const batch = await studentAdminApi.getIdentityAccountsBatch(ids)
      linkedAccountDetails.value = batch.accounts
    } else {
      linkedAccountDetails.value = []
    }
  } catch (error) {
    console.error('Failed to refresh linked accounts:', error)
  }
}

async function openLinkedAccountsDialog(s: StudentRow) {
  currentStudent.value = s
  showLinkedAccountsDialog.value = true
  linkedAccountsLoading.value = true
  linkedAccountIds.value = []
  linkedAccountDetails.value = []
  accountSearchKeyword.value = ''
  accountSearchResults.value = []
  searchingAccounts.value = false
  busyLinkedUserId.value = null
  try {
    const ids = await studentAdminApi.getIdentityAccountsByStudentId(s.id)
    linkedAccountIds.value = ids
    if (ids.length > 0) {
      const batch = await studentAdminApi.getIdentityAccountsBatch(ids)
      linkedAccountDetails.value = batch.accounts
    }
  } catch (e) {
    console.error('Failed to load linked accounts:', e)
    ElMessage.error('加载关联账户失败')
  } finally {
    linkedAccountsLoading.value = false
  }
}

function closeLinkedAccountsDialog() {
  showLinkedAccountsDialog.value = false
  linkedAccountIds.value = []
  linkedAccountDetails.value = []
  accountSearchKeyword.value = ''
  accountSearchResults.value = []
  busyLinkedUserId.value = null
}

async function loadAccountMap() {
  const ids = new Set<string>()
  for (const s of students.value) {
    for (const id of s.identityAccountIds || []) ids.add(id)
  }
  if (ids.size === 0) {
    accountMap.value = new Map()
    return
  }
  try {
    const result = await studentAdminApi.getIdentityAccountsBatch([...ids])
    const map = new Map<string, IdentityAccountDto>()
    for (const acc of result.accounts) map.set(acc.userId, acc)
    accountMap.value = map
  } catch (e) {
    console.error('Failed to load account info:', e)
  }
}

function getAccountDisplayName(accountId: string): string {
  return accountMap.value.get(accountId)?.username || accountId.slice(0, 8)
}

function getGradeLabel(grade: number): string {
  return gradeOptions.value.find(g => g.value === grade)?.label || `年级 ${grade}`
}

function onSearch() {
  page.value = 1
  loadStudents()
}

function resetFilters() {
  searchName.value = ''
  searchGrade.value = undefined
  searchSubject.value = undefined
  page.value = 1
  loadStudents()
}

function onPageChange(p: number) {
  page.value = p
  loadStudents()
}

function openCreateDialog() {
  createForm.value = { name: '', grade: 0, identityAccountIds: [] }
  createSearchKeyword.value = ''
  createIdentityUsers.value = []
  createOpenSubjects.value = []
  showCreateDialog.value = true
}

function closeCreateDialog() {
  showCreateDialog.value = false
}

async function searchCreateIdentityUsers() {
  if (!createSearchKeyword.value || createSearchKeyword.value.length < 2) {
    createIdentityUsers.value = []
    return
  }
  try {
    const keyword = createSearchKeyword.value
    const isPhone = /^\d+$/.test(keyword)
    const result = await identityApi.getUsers({
      username: isPhone ? undefined : keyword,
      phone: isPhone ? keyword : undefined,
      page: 1,
      pageSize: 20,
    })
    createIdentityUsers.value = result.items
  } catch (e) {
    console.error('Search users failed:', e)
  }
}

function addCreateIdentityAccount(userId: string) {
  if (userId && !createForm.value.identityAccountIds.includes(userId)) {
    createForm.value.identityAccountIds.push(userId)
    const user = createIdentityUsers.value.find(u => u.userId === userId)
    if (user && !accountMap.value.has(userId)) {
      const newMap = new Map(accountMap.value)
      newMap.set(userId, {
        userId: user.userId,
        username: user.username,
        displayName: user.displayName || user.username,
        phone: user.phone || '',
        remark: user.remark || '',
      })
      accountMap.value = newMap
    }
  }
  createSearchKeyword.value = ''
  createIdentityUsers.value = []
}

function removeCreateIdentityAccount(userId: string) {
  createForm.value.identityAccountIds = createForm.value.identityAccountIds.filter(id => id !== userId)
}

function toggleCreateSubject(subject: number) {
  const index = createOpenSubjects.value.indexOf(subject)
  if (index >= 0) {
    createOpenSubjects.value.splice(index, 1)
  } else {
    createOpenSubjects.value.push(subject)
  }
}

async function createStudent() {
  if (!createForm.value.name) {
    ElMessage.warning('请填写学生姓名')
    return
  }
  if (createForm.value.identityAccountIds.length === 0) {
    ElMessage.warning('请至少选择一个关联账户')
    return
  }
  creating.value = true
  try {
    const createdStudent = await studentAdminApi.createStudent({
      name: createForm.value.name,
      grade: createForm.value.grade,
      identityAccountIds: createForm.value.identityAccountIds,
    })

    let subjectsSaved = true
    if (createOpenSubjects.value.length > 0) {
      try {
        const openStartDate = new Date().toISOString().split('T')[0]
        await studentAdminApi.setStudentOpenSubjects(createdStudent.id, {
          subjects: createOpenSubjects.value.map(subject => ({
            subject,
            openStartDate,
            openEndDate: null,
          })),
        })
      } catch (error: unknown) {
        subjectsSaved = false
        console.error('Failed to save subjects for the created student:', error)
      }
    }

    if (subjectsSaved) {
      ElMessage.success('创建成功')
    } else {
      ElMessage.warning('学生已创建，但开放学科保存失败，请稍后通过“学科”补充')
    }
    showCreateDialog.value = false
    await loadStudents()
  } catch (error: unknown) {
    ElMessage.error(extractMsg(error) || '创建失败')
  } finally {
    creating.value = false
  }
}

function openEditDialog(s: StudentRow) {
  editForm.value = { id: s.id, name: s.name, grade: s.grade }
  showEditDialog.value = true
}

async function saveEdit() {
  if (!editForm.value.name) {
    ElMessage.warning('请填写学生姓名')
    return
  }
  try {
    await studentAdminApi.updateStudent(editForm.value.id, {
      name: editForm.value.name,
      grade: editForm.value.grade,
    })
    ElMessage.success('更新成功')
    showEditDialog.value = false
    await loadStudents()
  } catch (error: unknown) {
    ElMessage.error(extractMsg(error) || '更新失败')
  }
}

async function deleteStudent(s: StudentRow) {
  try {
    await ElMessageBox.confirm(`确认删除学生「${s.name}」？`, '确认', { type: 'warning' })
    await studentAdminApi.deleteStudent(s.id)
    ElMessage.success('删除成功')
    await loadStudents()
  } catch (error: unknown) {
    if (error !== 'cancel') {
      ElMessage.error(extractMsg(error) || '删除失败')
    }
  }
}

async function openSubjectsDialog(s: StudentRow) {
  currentStudent.value = s
  openSubjects.value = []
  try {
    const list = await studentAdminApi.getStudentOpenSubjects(s.id, true)
    openSubjects.value = list.map(x => x.subject)
  } catch {
    openSubjects.value = []
  }
  showOpenSubjectsDialogVisible.value = true
}

async function saveOpenSubjects() {
  if (!currentStudent.value) return
  savingSubjects.value = true
  try {
    await studentAdminApi.setStudentOpenSubjects(currentStudent.value.id, {
      subjects: openSubjects.value.map(s => ({
        subject: s,
        openStartDate: new Date().toISOString().split('T')[0],
        openEndDate: null,
      })),
    })
    studentSubjectMap.value.set(currentStudent.value.id, [...openSubjects.value])
    ElMessage.success('保存成功')
    showOpenSubjectsDialogVisible.value = false
  } catch (error: unknown) {
    ElMessage.error(extractMsg(error) || '保存失败')
  } finally {
    savingSubjects.value = false
  }
}

function toggleOpenSubject(subject: number) {
  const index = openSubjects.value.indexOf(subject)
  if (index >= 0) {
    openSubjects.value.splice(index, 1)
  } else {
    openSubjects.value.push(subject)
  }
}

onMounted(async () => {
  await Promise.all([loadGradeOptions(), loadSubjectOptions()])
  await loadStudents()
})
</script>

<style scoped>
.avatar-cell {
  display: flex;
}

.selected-accounts {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 8px;
}
.account-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 4px 8px 4px 10px;
  border: 1px solid var(--adm-info-border);
  border-radius: 6px;
  color: var(--adm-primary);
  background: var(--adm-primary-bg);
  font-size: 12px;
  font-weight: 500;
}
.chip-x {
  width: 14px;
  height: 14px;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  border: none;
  border-radius: 50%;
  color: var(--adm-primary);
  background: transparent;
  cursor: pointer;
  font-size: 14px;
  line-height: 1;
}
.chip-x:hover {
  color: #fff;
  background: var(--adm-primary);
}

.account-row {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 6px;
  padding: 8px 10px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  background: var(--adm-surface-elevated);
}
.account-row-info {
  min-width: 0;
  flex: 1;
}
.account-row-name {
  color: var(--adm-text-primary);
  font-size: 13px;
  font-weight: 500;
}
.account-row-meta {
  margin-top: 1px;
  color: var(--adm-text-tertiary);
  font-size: 11px;
}
.account-row.addable {
  border-style: dashed;
  border-color: var(--adm-primary-light);
  background: var(--adm-surface-subtle);
}
.account-row.addable:hover {
  border-color: var(--adm-primary);
  background: var(--adm-primary-bg);
}

.drawer-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 16px;
  color: var(--adm-text-muted);
  font-size: 12px;
}
.drawer-section {
  margin-bottom: 20px;
}
.drawer-section:last-child {
  margin-bottom: 0;
}
.drawer-section-title {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 10px;
  color: var(--adm-text-tertiary);
  text-transform: uppercase;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.5px;
}
.drawer-section-title .count {
  padding: 1px 7px;
  border-radius: 10px;
  color: var(--adm-primary);
  background: var(--adm-primary-bg);
  font-size: 11px;
  font-weight: 600;
}
.drawer-empty {
  padding: 12px;
  color: var(--adm-text-muted);
  text-align: center;
  font-size: 12px;
}
.drawer-empty-hint {
  margin-top: 6px;
  padding: 10px;
  border-radius: var(--adm-radius-md);
  color: var(--adm-text-muted);
  background: var(--adm-surface-subtle);
  text-align: center;
  font-size: 12px;
}

.link-search-wrap {
  position: relative;
  margin: 8px 0 6px;
}
.link-search-input {
  height: 32px;
  padding-right: 30px;
  font-size: 12px;
}
.link-search-spinner {
  position: absolute;
  top: 50%;
  right: 8px;
  display: inline-flex;
  align-items: center;
  transform: translateY(-50%);
}

.chip-remove,
.chip-add {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  padding: 0;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s;
}
.chip-remove {
  width: 26px;
  height: 26px;
  color: var(--adm-text-muted);
  background: transparent;
}
.chip-remove:hover:not(:disabled) {
  color: var(--adm-error);
  background: var(--adm-error-bg);
}
.chip-add {
  width: 28px;
  height: 28px;
  color: var(--adm-primary);
  background: var(--adm-primary-bg);
}
.chip-add:hover:not(:disabled) {
  color: #fff;
  background: var(--adm-primary);
}
.chip-remove:disabled,
.chip-add:disabled {
  opacity: 0.6;
  cursor: wait;
}
</style>
