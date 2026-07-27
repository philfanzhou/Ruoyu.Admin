<template>
  <div class="adm-fade-in student-view">
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

    <!-- Stats Pills -->
    <div class="stats-pills">
      <div class="stat-pill" :class="{ active: filterStatus === 'all' }" @click="setFilterStatus('all')">
        <div class="sp-body">
          <div class="sp-label">全部学生</div>
          <div class="sp-value">{{ stats.total }}</div>
        </div>
      </div>
      <div class="stat-pill linked" :class="{ active: filterStatus === 'linked' }" @click="setFilterStatus('linked')">
        <div class="sp-body">
          <div class="sp-label">已绑定账户</div>
          <div class="sp-value">{{ stats.linked }}</div>
        </div>
        <svg class="sp-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg>
      </div>
      <div class="stat-pill unlinked" :class="{ active: filterStatus === 'unlinked' }" @click="setFilterStatus('unlinked')">
        <div class="sp-body">
          <div class="sp-label">未绑定账户</div>
          <div class="sp-value">{{ stats.unlinked }}</div>
        </div>
        <svg class="sp-icon" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="4.93" y1="4.93" x2="19.07" y2="19.07"/></svg>
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
              <th class="col-avatar">学生</th>
              <th class="col-id">ID</th>
              <th class="col-name">姓名</th>
              <th class="col-grade">年级</th>
              <th class="col-account">关联账户</th>
              <th class="col-teachers">关联教师/助教</th>
              <th class="col-subjects">开放学科</th>
              <th class="col-time">创建时间</th>
              <th class="col-actions">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="s in students" :key="s.id">
              <td>
                <div class="avatar-cell">
                  <div class="stu-avatar" :style="{ background: getAvatarGradient(s.name || s.id) }">{{ getAvatarChar(s.name || '?') }}</div>
                </div>
              </td>
              <td><span class="mono-id">{{ s.id }}</span></td>
              <td><span class="stu-name">{{ s.name }}</span></td>
              <td><span class="grade-text">{{ getGradeLabel(s.grade) }}</span></td>
              <td>
                <div v-if="s.identityAccountIds && s.identityAccountIds.length > 0" class="account-cell">
                  <span class="status-tag linked">
                    <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                    {{ getAccountDisplayName(s.identityAccountIds[0]) }}
                  </span>
                  <span v-if="s.identityAccountIds.length > 1" class="more-count">+{{ s.identityAccountIds.length - 1 }}</span>
                </div>
                <div v-else class="account-cell">
                  <span class="status-tag unlinked">未绑定</span>
                  <button class="link-btn" @click="openLinkDialog(s)">关联账户</button>
                </div>
              </td>
              <td>
                <div class="linked-accounts-cell">
                  <span v-if="getLinkedAccountCount(s) > 0" class="status-tag linked" :title="getLinkedAccountsTooltip(s)">
                    <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                    {{ getLinkedAccountCount(s) }}人
                  </span>
                  <span v-else class="status-tag unlinked">无</span>
                  <button class="adm-btn adm-btn-ghost adm-btn-xs" @click="openLinkedAccountsDialog(s)">管理</button>
                </div>
              </td>
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
                  <button class="adm-btn-link" @click="openSubjectsDialog(s)">学科</button>
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
                <div v-for="u in createIdentityUsers" :key="u.userId" class="user-option" @click="addCreateIdentityAccount(u.userId)">
                  <div class="user-option-main">
                    <span class="user-option-name">{{ u.username }}</span>
                    <span class="user-option-phone">{{ u.phone || '无手机号' }}</span>
                  </div>
                  <span v-if="createForm.identityAccountIds.includes(u.userId)" class="user-option-checked">已选</span>
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
              <label v-for="s in subjectOptions" :key="s.value" class="subj-checkbox" :class="{ checked: createOpenSubjects.includes(s.value) }">
                <input type="checkbox" :value="s.value" v-model="createOpenSubjects" />
                <span class="adm-subj-tag" :class="getSubjectCssClass(s.value)">{{ s.displayName || s.name }}</span>
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

    <!-- Link Account Modal -->
    <div v-if="showLinkDialogVisible" class="modal-mask" @click.self="showLinkDialogVisible = false">
      <div class="modal-box" style="width: 460px;">
        <div class="modal-header">
          <h3>关联账户</h3>
          <button class="modal-close" @click="showLinkDialogVisible = false">×</button>
        </div>
        <div class="modal-body">
          <div v-if="currentStudent" class="student-info-banner">
            <div class="stu-avatar" :style="{ background: getAvatarGradient(currentStudent.name || currentStudent.id) }">{{ getAvatarChar(currentStudent.name || '?') }}</div>
            <div>
              <div class="info-name">{{ currentStudent.name }}</div>
              <div class="info-sub">{{ getGradeLabel(currentStudent.grade) }}</div>
            </div>
          </div>
          <div class="form-group">
            <label class="form-label">搜索用户</label>
            <div class="user-search-wrap">
              <input v-model="linkForm.keyword" class="form-input" placeholder="输入用户名或手机号搜索" @input="searchIdentityUsers" />
              <div v-if="identityUsers.length > 0" class="user-dropdown">
                <div v-for="u in identityUsers" :key="u.userId" class="user-option" @click="linkForm.selectedUserId = u.userId">
                  <div class="user-option-main">
                    <span class="user-option-name">{{ u.username }}</span>
                    <span class="user-option-phone">{{ u.phone || '无手机号' }}</span>
                  </div>
                  <span v-if="linkForm.selectedUserId === u.userId" class="user-option-checked">已选</span>
                </div>
              </div>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="adm-btn adm-btn-secondary" @click="showLinkDialogVisible = false">取消</button>
          <button class="adm-btn adm-btn-primary" @click="linkAccount">关联</button>
        </div>
      </div>
    </div>

    <!-- Linked Accounts Modal -->
    <div v-if="showLinkedAccountsDialog" class="modal-mask" @click.self="closeLinkedAccountsDialog">
      <div class="modal-box" style="width: 560px;">
        <div class="modal-header">
          <h3>关联教师/助教</h3>
          <button class="modal-close" @click="closeLinkedAccountsDialog">×</button>
        </div>
        <div class="modal-body">
          <div v-if="currentStudent" class="student-info-banner">
            <div class="stu-avatar" :style="{ background: getAvatarGradient(currentStudent.name || currentStudent.id) }">{{ getAvatarChar(currentStudent.name || '?') }}</div>
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
            <!-- 教师列表 -->
            <div class="drawer-section">
              <div class="drawer-section-title">
                教师
                <span class="count">{{ linkedAccountsData.teachers.length }}</span>
              </div>
              <div v-if="linkedAccountsData.teachers.length === 0" class="drawer-empty">暂无关联教师</div>
              <div v-for="t in linkedAccountsData.teachers" :key="t.userId" class="account-row">
                <div class="mini-avatar" :style="{ background: getAvatarGradient(t.displayName || t.userId) }">{{ getAvatarChar(t.displayName || '?') }}</div>
                <div class="account-row-info">
                  <div class="account-row-name">{{ t.displayName }}</div>
                  <div class="account-row-meta">{{ t.phone || '无手机号' }} · {{ getSubjectLabels(t.subjects) }}</div>
                </div>
              </div>
            </div>

            <!-- 助教列表 -->
            <div class="drawer-section">
              <div class="drawer-section-title">
                助教
                <span class="count">{{ linkedAccountsData.assistants.length }}</span>
              </div>
              <div v-if="linkedAccountsData.assistants.length === 0" class="drawer-empty">暂无关联助教</div>
              <div v-for="a in linkedAccountsData.assistants" :key="a.userId" class="account-row">
                <div class="mini-avatar" :style="{ background: getAvatarGradient(a.displayName || a.userId) }">{{ getAvatarChar(a.displayName || '?') }}</div>
                <div class="account-row-info">
                  <div class="account-row-name">{{ a.displayName }}</div>
                  <div class="account-row-meta">{{ a.phone || '无手机号' }} · {{ getSubjectLabels(a.subjects) }}</div>
                </div>
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
          <div v-if="currentStudent" class="student-info-banner">
            <div class="stu-avatar" :style="{ background: getAvatarGradient(currentStudent.name || currentStudent.id) }">{{ getAvatarChar(currentStudent.name || '?') }}</div>
            <div>
              <div class="info-name">{{ currentStudent.name }}</div>
              <div class="info-sub">{{ getGradeLabel(currentStudent.grade) }}</div>
            </div>
          </div>
          <div class="form-group">
            <label class="form-label">已开放学科</label>
            <div class="subj-grid">
              <label v-for="s in subjectOptions" :key="s.value" class="subj-checkbox" :class="{ checked: openSubjects.includes(s.value) }">
                <input type="checkbox" :value="s.value" v-model="openSubjects" />
                <span class="adm-subj-tag" :class="getSubjectCssClass(s.value)">{{ s.displayName || s.name }}</span>
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
import studentAdminApi, { type IdentityAccountDto, type LinkedAccountDto, type SubjectOption, type GradeOption } from '../services/studentAdminApi'
import identityApi from '../services/identityApi'
import {
  getSubjectMeta, getSubjectLabel, getSubjectCssClass,
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
const filterStatus = ref<'all' | 'linked' | 'unlinked'>('all')

const gradeOptions = ref<GradeOption[]>([])
const subjectOptions = ref<SubjectOption[]>([])
const accountMap = ref<Map<string, IdentityAccountDto>>(new Map())
const studentSubjectMap = ref<Map<string, number[]>>(new Map())

const stats = computed(() => {
  const totalNum = total.value
  // Approximate stats — use loaded data when filter is "all"; otherwise the
  // stats are just for the current page. This is acceptable because the
  // backend does not provide aggregate counts.
  const currentPageLinked = students.value.filter(s => s.identityAccountIds && s.identityAccountIds.length > 0).length
  const currentPageUnlinked = students.value.length - currentPageLinked
  return {
    total: totalNum,
    linked: currentPageLinked,
    unlinked: currentPageUnlinked,
  }
})

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))

const showCreateDialog = ref(false)
const createForm = ref({ name: '', grade: 0, identityAccountIds: [] as string[] })
const createIdentityUsers = ref<any[]>([])
const createSearchKeyword = ref('')
const createOpenSubjects = ref<number[]>([])
const creating = ref(false)

const showEditDialog = ref(false)
const editForm = ref({ id: '', name: '', grade: 0 })

const showLinkDialogVisible = ref(false)
const currentStudent = ref<StudentRow | null>(null)
const linkForm = ref({ keyword: '', selectedUserId: '' })
const identityUsers = ref<any[]>([])

const showOpenSubjectsDialogVisible = ref(false)
const openSubjects = ref<number[]>([])
const savingSubjects = ref(false)

// Linked accounts dialog
const showLinkedAccountsDialog = ref(false)
const linkedAccountsLoading = ref(false)
const linkedAccountsData = ref<{ teachers: LinkedAccountDto[], assistants: LinkedAccountDto[] }>({ teachers: [], assistants: [] })
const linkedAccountCounts = ref<Record<string, number>>({})

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
    // Apply client-side filter by account-link status (backend has no such filter)
    if (filterStatus.value === 'linked') {
      items = items.filter(s => s.identityAccountIds && s.identityAccountIds.length > 0)
    } else if (filterStatus.value === 'unlinked') {
      items = items.filter(s => !s.identityAccountIds || s.identityAccountIds.length === 0)
    }
    // Apply client-side subject filter
    if (searchSubject.value !== undefined) {
      // We may need to fetch open subjects per student; do it lazily
      await loadOpenSubjectsForList(items)
      items = items.filter(s => (studentSubjectMap.value.get(s.id) || []).includes(searchSubject.value as number))
    }
    students.value = items
    total.value = filterStatus.value === 'all' && searchSubject.value === undefined
      ? result.total
      : items.length
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

function getLinkedAccountCount(s: StudentRow): number {
  return linkedAccountCounts.value[s.id] ?? 0
}

function getLinkedAccountsTooltip(s: StudentRow): string {
  const count = getLinkedAccountCount(s)
  if (count > 0) return `关联教师/助教 ${count} 人`
  return s.identityAccountIds && s.identityAccountIds.length > 0 ? '暂无关联教师/助教' : '无身份账户'
}

function getSubjectLabels(subjectsStr: string): string {
  if (!subjectsStr) return '未分配科目'
  const ids = subjectsStr.split(',').map(s => parseInt(s.trim(), 10)).filter(n => !isNaN(n))
  if (ids.length === 0) return '未分配科目'
  return ids.map(id => getSubjectLabel(id)).join('、')
}

async function openLinkedAccountsDialog(s: StudentRow) {
  currentStudent.value = s
  showLinkedAccountsDialog.value = true
  linkedAccountsLoading.value = true
  linkedAccountsData.value = { teachers: [], assistants: [] }
  try {
    const result = await studentAdminApi.getLinkedAccounts(s.id)
    linkedAccountsData.value = { teachers: [...result.teachers], assistants: [...result.assistants] }
    const count = result.teachers.length + result.assistants.length
    linkedAccountCounts.value = { ...linkedAccountCounts.value, [s.id]: count }
  } catch (e) {
    console.error('Failed to load linked accounts:', e)
    ElMessage.error('加载关联教师/助教失败')
  } finally {
    linkedAccountsLoading.value = false
  }
}

function closeLinkedAccountsDialog() {
  showLinkedAccountsDialog.value = false
  linkedAccountsData.value = { teachers: [], assistants: [] }
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

function setFilterStatus(s: 'all' | 'linked' | 'unlinked') {
  if (filterStatus.value === s) return
  filterStatus.value = s
  page.value = 1
  loadStudents()
}

function onSearch() {
  page.value = 1
  loadStudents()
}

function resetFilters() {
  searchName.value = ''
  searchGrade.value = undefined
  searchSubject.value = undefined
  filterStatus.value = 'all'
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
  if (!createSearchKeyword.value || createSearchKeyword.value.length < 2) return
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
    await studentAdminApi.createStudent({
      name: createForm.value.name,
      grade: createForm.value.grade,
      identityAccountIds: createForm.value.identityAccountIds,
    })
    if (createOpenSubjects.value.length > 0) {
      // We need student id — createStudent returns the new student. Re-query to find by account.
      // For simplicity, just reload list; open subjects can be set later via the management dialog.
      ElMessage.warning('学生已创建，请通过"学科"按钮设置开放学科')
    } else {
      ElMessage.success('创建成功')
    }
    showCreateDialog.value = false
    await loadStudents()
  } catch (e: any) {
    ElMessage.error(e.response?.data?.message || '创建失败')
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
  } catch (e: any) {
    ElMessage.error(e.response?.data?.message || '更新失败')
  }
}

async function deleteStudent(s: StudentRow) {
  try {
    await ElMessageBox.confirm(`确认删除学生「${s.name}」？`, '确认', { type: 'warning' })
    await studentAdminApi.deleteStudent(s.id)
    ElMessage.success('删除成功')
    await loadStudents()
  } catch (e: any) {
    if (e !== 'cancel') {
      ElMessage.error(e.response?.data?.message || '删除失败')
    }
  }
}

function openLinkDialog(s: StudentRow) {
  currentStudent.value = s
  linkForm.value = { keyword: '', selectedUserId: '' }
  identityUsers.value = []
  showLinkDialogVisible.value = true
}

async function searchIdentityUsers() {
  if (!linkForm.value.keyword || linkForm.value.keyword.length < 2) return
  try {
    const keyword = linkForm.value.keyword
    const isPhone = /^\d+$/.test(keyword)
    const result = await identityApi.getUsers({
      username: isPhone ? undefined : keyword,
      phone: isPhone ? keyword : undefined,
      page: 1,
      pageSize: 20,
    })
    identityUsers.value = result.items
  } catch (e) {
    console.error('Search users failed:', e)
  }
}

async function linkAccount() {
  if (!linkForm.value.selectedUserId) {
    ElMessage.warning('请选择用户')
    return
  }
  if (!currentStudent.value) return
  try {
    await studentAdminApi.linkIdentityAccount(currentStudent.value.id, linkForm.value.selectedUserId)
    ElMessage.success('关联成功')
    showLinkDialogVisible.value = false
    await loadStudents()
  } catch (e: any) {
    ElMessage.error(e.response?.data?.message || '关联失败')
  }
}

async function unlinkAccount(studentId: string, accountId: string) {
  try {
    await studentAdminApi.unlinkIdentityAccount(studentId, accountId)
    ElMessage.success('取消关联成功')
    await loadStudents()
  } catch (e: any) {
    ElMessage.error(e.response?.data?.message || '取消关联失败')
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
  } catch (e: any) {
    ElMessage.error(e.response?.data?.message || '保存失败')
  } finally {
    savingSubjects.value = false
  }
}

onMounted(async () => {
  await Promise.all([loadGradeOptions(), loadSubjectOptions()])
  await loadStudents()
})
</script>

<style scoped>
.student-view {
  min-width: 1100px;
}

/* Stats Pills */
.stats-pills {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
  margin-bottom: 16px;
}
.stat-pill {
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  padding: 16px 20px;
  display: flex;
  align-items: center;
  gap: 12px;
  cursor: pointer;
  transition: all 0.2s;
  position: relative;
  overflow: hidden;
}
.stat-pill::before {
  content: '';
  position: absolute;
  left: 0; top: 0; bottom: 0;
  width: 3px;
  background: var(--adm-primary);
}
.stat-pill.linked::before { background: var(--adm-success); }
.stat-pill.unlinked::before { background: var(--adm-error); }
.stat-pill:hover {
  box-shadow: var(--adm-shadow-sm);
  transform: translateY(-1px);
}
.stat-pill.active {
  border-color: var(--adm-primary);
  background: var(--adm-primary-bg);
}
.stat-pill.linked.active {
  border-color: var(--adm-success);
  background: var(--adm-success-bg);
}
.stat-pill.unlinked.active {
  border-color: var(--adm-error);
  background: var(--adm-error-bg);
}
.sp-body { flex: 1; }
.sp-label {
  font-size: 12px;
  color: var(--adm-text-tertiary);
  margin-bottom: 4px;
}
.sp-value {
  font-size: 24px;
  font-weight: 700;
  line-height: 1;
  color: var(--adm-text-primary);
}
.stat-pill.linked .sp-value { color: var(--adm-success); }
.stat-pill.unlinked .sp-value { color: var(--adm-error); }
.sp-icon {
  color: var(--adm-text-muted);
  flex-shrink: 0;
}

/* Filter Bar */
.filter-input-wrap {
  position: relative;
  flex: 1;
  min-width: 220px;
  max-width: 320px;
}
.filter-input {
  width: 100%;
  padding: 8px 12px 8px 36px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  font-size: 13px;
  color: var(--adm-text-primary);
  background: var(--adm-surface-elevated);
  transition: all 0.15s;
  outline: none;
  height: 36px;
}
.filter-input:focus {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}
.filter-input::placeholder { color: var(--adm-text-muted); }
.filter-input-icon {
  position: absolute;
  left: 10px;
  top: 50%;
  transform: translateY(-50%);
  color: var(--adm-text-muted);
  pointer-events: none;
}
.filter-select {
  padding: 8px 30px 8px 12px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  font-size: 13px;
  color: var(--adm-text-primary);
  background: var(--adm-surface-elevated);
  appearance: none;
  cursor: pointer;
  transition: all 0.15s;
  outline: none;
  height: 36px;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%2394a3b8' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 10px center;
  min-width: 130px;
}
.filter-select:focus {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}

/* Table Card */
.table-card {
  overflow: hidden;
}
.loading-row {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 48px;
  color: var(--adm-text-muted);
  font-size: 14px;
}
.spinner-lg {
  width: 18px; height: 18px;
  border: 2px solid var(--adm-border);
  border-top-color: var(--adm-primary);
  border-radius: 50%;
  animation: adm-spin 0.7s linear infinite;
}
@keyframes adm-spin { to { transform: rotate(360deg); } }

.table-wrap {
  overflow-x: auto;
}
.adm-table {
  width: 100%;
  border-collapse: collapse;
}
.adm-table thead th {
  background: var(--adm-surface-subtle);
  padding: 12px 16px;
  text-align: left;
  font-size: 12px;
  font-weight: 600;
  color: var(--adm-text-tertiary);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  border-bottom: 1px solid var(--adm-border);
  white-space: nowrap;
}
.adm-table tbody td {
  padding: 14px 16px;
  font-size: 13px;
  color: var(--adm-text-primary);
  border-bottom: 1px solid var(--adm-border-light);
  vertical-align: middle;
}
.adm-table tbody tr {
  transition: background 0.12s;
}
.adm-table tbody tr:hover {
  background: var(--adm-surface-subtle);
}
.adm-table tbody tr:last-child td {
  border-bottom: none;
}
.col-avatar { width: 56px; }
.col-id { width: 130px; }
.col-name { width: 120px; }
.col-grade { width: 130px; }
.col-account { min-width: 200px; }
.col-teachers { min-width: 160px; }
.col-subjects { min-width: 200px; }
.col-time { width: 130px; }
.col-actions { width: 160px; text-align: right; }

.avatar-cell { display: flex; }
.stu-avatar {
  width: 32px; height: 32px;
  border-radius: 50%;
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  font-weight: 600;
  flex-shrink: 0;
}
.mono-id {
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--adm-text-secondary);
}
.stu-name {
  font-weight: 500;
  color: var(--adm-text-primary);
}
.grade-text {
  font-size: 12px;
  color: var(--adm-text-secondary);
}
.account-cell {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.status-tag {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 12px;
  font-weight: 500;
}
.status-tag.linked {
  background: var(--adm-success-bg);
  color: var(--adm-success);
}
.status-tag.unlinked {
  background: var(--adm-error-bg);
  color: var(--adm-error);
}
.more-count {
  font-size: 11px;
  color: var(--adm-text-tertiary);
  padding: 2px 6px;
  background: var(--adm-surface-subtle);
  border-radius: 4px;
}
.link-btn {
  background: transparent;
  color: var(--adm-primary);
  border: 1px dashed var(--adm-primary);
  border-radius: 4px;
  padding: 2px 8px;
  font-size: 12px;
  cursor: pointer;
  transition: all 0.15s;
}
.link-btn:hover {
  background: var(--adm-primary-bg);
}

.linked-accounts-cell {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.adm-btn-xs {
  padding: 3px 8px;
  font-size: 11px;
  gap: 3px;
}

.account-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 10px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  margin-bottom: 6px;
  background: #fff;
}

.account-row-info { flex: 1; min-width: 0; }

.account-row-name {
  font-size: 13px;
  font-weight: 500;
  color: var(--adm-text-primary);
}

.account-row-meta {
  font-size: 11px;
  color: var(--adm-text-tertiary);
  margin-top: 1px;
}
.subj-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.text-muted { color: var(--adm-text-muted); font-size: 12px; }
.time-cell {
  color: var(--adm-text-tertiary);
  font-size: 12px;
  white-space: nowrap;
}
.actions-cell {
  display: flex;
  align-items: center;
  gap: 2px;
  justify-content: flex-end;
}

/* Pagination */
.adm-pagination-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-top: 1px solid var(--adm-border-light);
}
.page-info {
  font-size: 12px;
  color: var(--adm-text-tertiary);
}
.page-controls {
  display: flex;
  align-items: center;
  gap: 8px;
}
.page-size-select {
  padding: 6px 24px 6px 10px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-sm);
  font-size: 12px;
  color: var(--adm-text-primary);
  background: var(--adm-surface-elevated);
  appearance: none;
  cursor: pointer;
  outline: none;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='10' height='10' viewBox='0 0 24 24' fill='none' stroke='%2394a3b8' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 6px center;
}
.page-btn {
  padding: 6px 12px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-sm);
  background: var(--adm-surface-elevated);
  color: var(--adm-text-secondary);
  font-size: 12px;
  cursor: pointer;
  transition: all 0.15s;
}
.page-btn:hover:not(:disabled) {
  background: var(--adm-surface-subtle);
  border-color: var(--adm-text-muted);
}
.page-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.page-current {
  font-size: 13px;
  color: var(--adm-text-primary);
  font-weight: 500;
  padding: 0 4px;
}

/* Modal */
.modal-mask {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  animation: maskIn 0.2s;
}
@keyframes maskIn { from { opacity: 0; } to { opacity: 1; } }
.modal-box {
  background: var(--adm-surface-elevated);
  border-radius: var(--adm-radius-lg);
  box-shadow: var(--adm-shadow-lg);
  max-width: calc(100vw - 32px);
  max-height: calc(100vh - 64px);
  display: flex;
  flex-direction: column;
  animation: modalIn 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
}
@keyframes modalIn {
  from { opacity: 0; transform: scale(0.95) translateY(-10px); }
  to { opacity: 1; transform: scale(1) translateY(0); }
}
.modal-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--adm-border-light);
}
.modal-header h3 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--adm-text-primary);
}
.modal-close {
  width: 28px; height: 28px;
  border: none;
  background: transparent;
  color: var(--adm-text-muted);
  font-size: 22px;
  line-height: 1;
  cursor: pointer;
  border-radius: 4px;
  transition: all 0.15s;
}
.modal-close:hover {
  background: var(--adm-surface-subtle);
  color: var(--adm-text-primary);
}
.modal-body {
  padding: 20px;
  overflow-y: auto;
}
.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 12px 20px;
  border-top: 1px solid var(--adm-border-light);
}

/* Form */
.form-group {
  margin-bottom: 16px;
}
.form-group:last-child { margin-bottom: 0; }
.form-label {
  display: block;
  font-size: 12px;
  font-weight: 500;
  color: var(--adm-text-secondary);
  margin-bottom: 6px;
}
.required { color: var(--adm-error); }
.form-input {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  font-size: 13px;
  color: var(--adm-text-primary);
  background: var(--adm-surface-elevated);
  outline: none;
  transition: all 0.15s;
  height: 36px;
  box-sizing: border-box;
}
.form-input:focus {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}
textarea.form-input {
  height: auto;
  min-height: 72px;
  resize: vertical;
  padding: 8px 12px;
}
select.form-input {
  appearance: none;
  padding-right: 30px;
  cursor: pointer;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%2394a3b8' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 10px center;
}

/* User search dropdown */
.user-search-wrap { position: relative; }
.user-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  box-shadow: var(--adm-shadow-md);
  max-height: 240px;
  overflow-y: auto;
  z-index: 10;
}
.user-option {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 12px;
  cursor: pointer;
  transition: background 0.12s;
  border-bottom: 1px solid var(--adm-border-light);
}
.user-option:last-child { border-bottom: none; }
.user-option:hover { background: var(--adm-surface-subtle); }
.user-option-main { display: flex; flex-direction: column; gap: 2px; }
.user-option-name { font-size: 13px; color: var(--adm-text-primary); font-weight: 500; }
.user-option-phone { font-size: 11px; color: var(--adm-text-muted); }
.user-option-checked {
  font-size: 11px;
  color: var(--adm-success);
  font-weight: 600;
}

.selected-accounts {
  margin-top: 8px;
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.account-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 4px 8px 4px 10px;
  background: var(--adm-primary-bg);
  color: var(--adm-primary);
  border-radius: 6px;
  font-size: 12px;
  font-weight: 500;
  border: 1px solid var(--adm-info-border);
}
.chip-x {
  background: transparent;
  border: none;
  color: var(--adm-primary);
  cursor: pointer;
  font-size: 14px;
  line-height: 1;
  padding: 0;
  width: 14px;
  height: 14px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
}
.chip-x:hover {
  background: var(--adm-primary);
  color: #fff;
}

/* Subject grid */
.subj-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 6px;
}
.subj-checkbox {
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  padding: 6px 4px;
  border-radius: var(--adm-radius-sm);
  border: 1px solid var(--adm-border);
  background: var(--adm-surface-elevated);
  transition: all 0.15s;
}
.subj-checkbox:hover {
  background: var(--adm-surface-subtle);
}
.subj-checkbox.checked {
  border-color: var(--adm-primary);
  background: var(--adm-primary-bg);
}
.subj-checkbox input[type="checkbox"] {
  display: none;
}

/* Student info banner */
.student-info-banner {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px;
  background: var(--adm-surface-subtle);
  border-radius: var(--adm-radius-md);
  margin-bottom: 16px;
}
.student-info-banner .stu-avatar {
  width: 40px;
  height: 40px;
  font-size: 14px;
}
.info-name {
  font-size: 14px;
  font-weight: 600;
  color: var(--adm-text-primary);
}
.info-sub {
  font-size: 12px;
  color: var(--adm-text-tertiary);
  margin-top: 2px;
}

/* Linked accounts dialog */
.spinner-sm {
  width: 14px; height: 14px;
  border: 2px solid var(--adm-border);
  border-top-color: var(--adm-primary);
  border-radius: 50%;
  animation: adm-spin 0.7s linear infinite;
  display: inline-block;
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

.drawer-section { margin-bottom: 20px; }
.drawer-section:last-child { margin-bottom: 0; }

.drawer-section-title {
  font-size: 12px;
  font-weight: 600;
  color: var(--adm-text-tertiary);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin-bottom: 10px;
  display: flex;
  align-items: center;
  gap: 6px;
}

.drawer-section-title .count {
  background: var(--adm-primary-bg);
  color: var(--adm-primary);
  padding: 1px 7px;
  border-radius: 10px;
  font-size: 11px;
  font-weight: 600;
}

.drawer-empty {
  text-align: center;
  padding: 12px;
  color: var(--adm-text-muted);
  font-size: 12px;
}
</style>
