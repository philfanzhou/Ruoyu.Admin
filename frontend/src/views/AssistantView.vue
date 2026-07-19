<template>
  <div class="adm-fade-in assistant-view">
    <!-- Page Header -->
    <div class="adm-page-header">
      <div>
        <h1 class="adm-page-title">助教管理</h1>
        <p class="adm-page-subtitle">管理助教账户、学科授权和学生关联</p>
      </div>
      <div class="adm-page-actions">
        <button class="adm-btn adm-btn-primary" @click="openGrantDialog">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
          授予助教权限
        </button>
      </div>
    </div>

    <!-- Filter Bar -->
    <div class="adm-filter-bar">
      <div class="filter-input-wrap">
        <svg class="filter-input-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
        <input v-model="searchKeyword" class="filter-input" placeholder="搜索助教姓名 / 手机号 / 用户ID" @keyup.enter="onSearch" />
      </div>
      <select v-model="searchSubject" class="filter-select" @change="onSearch">
        <option :value="undefined">全部科目</option>
        <option v-for="s in availableSubjects" :key="s.value" :value="s.value">{{ s.name }}</option>
      </select>
      <div class="filter-spacer"></div>
      <button class="adm-btn adm-btn-primary adm-btn-sm" @click="onSearch">
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
        搜索
      </button>
      <button class="adm-btn adm-btn-ghost adm-btn-sm" @click="resetFilters">
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
      <div v-else-if="filteredAssistants.length === 0" class="adm-empty">
        <div class="adm-empty-icon">
          <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
        </div>
        <div class="adm-empty-text">暂无助教数据</div>
      </div>
      <div v-else class="table-wrap">
        <table class="adm-table">
          <thead>
            <tr>
              <th class="col-avatar">头像</th>
              <th class="col-id">ID</th>
              <th class="col-phone">手机号</th>
              <th>用户名</th>
              <th>备注</th>
              <th>科目</th>
              <th class="col-students">关联学生</th>
              <th class="col-time">创建时间</th>
              <th class="col-actions">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="a in filteredAssistants"
              :key="a.userId || a.id"
              :class="{ highlighted: currentAssistant?.userId === a.userId && showStudentsDrawer }"
            >
              <td>
                <div class="ast-avatar" :style="{ background: getAvatarGradient(getDisplayName(a)) }">{{ getAvatarChar(getDisplayName(a)) }}</div>
              </td>
              <td><span class="mono-id">{{ a.userId || '-' }}</span></td>
              <td><span class="phone-cell">{{ getPhone(a) }}</span></td>
              <td><span class="ast-name">{{ getDisplayName(a) }}</span></td>
              <td>
                <span v-if="getRemark(a)" class="note-cell" :title="getRemark(a)">{{ getRemark(a) }}</span>
                <span v-else class="note-cell empty">-</span>
              </td>
              <td>
                <div v-if="a.subjects && a.subjects.length > 0" class="subj-tags">
                  <span v-for="subj in a.subjects" :key="subj" class="subj-tag" :class="getSubjectCssClass(subj)">
                    {{ getSubjectLabel(subj) }}
                    <button class="subj-x" :title="`移除 ${getSubjectLabel(subj)}`" @click="removeSubject(a, subj)">
                      <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                    </button>
                  </span>
                </div>
                <span v-else class="subj-warning">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
                  未分配科目
                </span>
              </td>
              <td>
                <span class="student-count" :class="{ zero: getStudentCount(a.userId) === 0 }">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
                  {{ getStudentCount(a.userId) }}人
                  <button class="manage-btn" @click="openStudentsDrawer(a)">管理</button>
                </span>
              </td>
              <td><span class="time-cell">{{ formatDate(a.createdAt) }}</span></td>
              <td>
                <div class="actions-cell">
                  <button class="adm-btn-link" @click="openSubjectsDialog(a)">科目</button>
                  <button class="adm-btn-link danger" @click="revokeAssistant(a)">撤销</button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Grant Assistant Permission Modal -->
    <div v-if="showGrantDialog" class="modal-mask" @click.self="closeGrantDialog">
      <div class="modal-box" style="width: 600px;">
        <div class="modal-header">
          <h3>授予助教权限</h3>
          <button class="modal-close" @click="closeGrantDialog">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label">选择用户 <span class="required">*</span></label>
            <div class="user-search-wrap">
              <svg class="form-input-icon" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
              <input
                v-model="grantForm.keyword"
                class="form-input has-icon"
                placeholder="输入手机号或用户名搜索"
                @input="searchUsers"
              />
              <div v-if="searchResults.length > 0" class="user-dropdown">
                <div
                  v-for="u in searchResults"
                  :key="u.userId"
                  class="search-option"
                  :class="{ selected: grantForm.selectedUserId === u.userId }"
                  @click="selectUser(u)"
                >
                  <div class="mini-avatar" :style="{ background: getAvatarGradient(u.username || u.userId) }">{{ getAvatarChar(u.username || u.userId) }}</div>
                  <div class="opt-info">
                    <div class="opt-phone">{{ u.phone || '无手机号' }}</div>
                    <div class="opt-name">{{ u.username }}<span v-if="u.displayName && u.displayName !== u.username"> · {{ u.displayName }}</span></div>
                  </div>
                  <div v-if="grantForm.selectedUserId === u.userId" class="check-mark">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                  </div>
                </div>
              </div>
            </div>
            <div v-if="grantForm.selectedUserId && selectedUser" class="selected-user-banner">
              <div class="mini-avatar" :style="{ background: getAvatarGradient(selectedUser.username || selectedUser.userId) }">{{ getAvatarChar(selectedUser.username || selectedUser.userId) }}</div>
              <div class="sel-info">
                <div class="sel-name">{{ selectedUser.username }}</div>
                <div class="sel-meta">{{ selectedUser.phone || '无手机号' }}<span v-if="selectedUser.remark"> · {{ selectedUser.remark }}</span></div>
              </div>
              <button class="sel-clear" @click="clearSelectedUser" title="清除选择">×</button>
            </div>
          </div>

          <div class="form-group">
            <label class="form-label">授权科目 <span class="required">*</span></label>
            <div class="subj-grid">
              <label
                v-for="s in availableSubjects"
                :key="s.value"
                class="subj-check"
                :class="{ checked: grantForm.selectedSubjects.includes(s.value) }"
                @click.prevent="toggleGrantSubject(s.value)"
              >
                <span class="chk-box">
                  <svg v-if="grantForm.selectedSubjects.includes(s.value)" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                </span>
                <span>{{ s.name }}</span>
              </label>
            </div>
          </div>

          <div v-if="selectedUser && selectedUser.remark" class="form-group">
            <label class="form-label">备注</label>
            <div class="readonly-note">{{ selectedUser.remark }}</div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="adm-btn adm-btn-secondary" @click="closeGrantDialog">取消</button>
          <button class="adm-btn adm-btn-primary" :disabled="granting" @click="grantAssistant">
            {{ granting ? '授权中...' : '确认授权' }}
          </button>
        </div>
      </div>
    </div>

    <!-- Manage Subjects Modal -->
    <div v-if="showSubjectsDialogVisible" class="modal-mask" @click.self="showSubjectsDialogVisible = false">
      <div class="modal-box" style="width: 560px;">
        <div class="modal-header">
          <h3>科目管理</h3>
          <button class="modal-close" @click="showSubjectsDialogVisible = false">×</button>
        </div>
        <div class="modal-body">
          <div v-if="currentAssistant" class="ast-info-banner">
            <div class="ast-avatar" :style="{ background: getAvatarGradient(getDisplayName(currentAssistant)) }">{{ getAvatarChar(getDisplayName(currentAssistant)) }}</div>
            <div>
              <div class="info-name">{{ getDisplayName(currentAssistant) }}</div>
              <div class="info-sub">{{ getPhone(currentAssistant) }} · 当前 {{ currentAssistant.subjects.length }} 个科目</div>
            </div>
          </div>
          <div class="form-group">
            <label class="form-label">授权科目</label>
            <div class="subj-grid">
              <label
                v-for="s in availableSubjects"
                :key="s.value"
                class="subj-check"
                :class="{ checked: selectedSubjects.includes(s.value) }"
                @click.prevent="toggleSelectedSubject(s.value)"
              >
                <span class="chk-box">
                  <svg v-if="selectedSubjects.includes(s.value)" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                </span>
                <span>{{ s.name }}</span>
              </label>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="adm-btn adm-btn-secondary" @click="showSubjectsDialogVisible = false">取消</button>
          <button class="adm-btn adm-btn-primary" :disabled="savingSubjects" @click="saveAssistantSubjects">
            {{ savingSubjects ? '保存中...' : '保存' }}
          </button>
        </div>
      </div>
    </div>

    <!-- Student Association Drawer -->
    <div v-if="showStudentsDrawer" class="drawer-overlay" @click.self="closeStudentsDrawer"></div>
    <div v-if="showStudentsDrawer" class="drawer">
      <div class="drawer-header">
        <div>
          <div class="drawer-title">管理助教学生{{ currentAssistant ? ' - ' + getDisplayName(currentAssistant) : '' }}</div>
          <div class="drawer-subtitle">当前关联 {{ currentStudentIds.length }} 名学生</div>
        </div>
        <button class="drawer-close" @click="closeStudentsDrawer" title="关闭">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
        </button>
      </div>

      <div class="drawer-search">
        <div class="drawer-search-wrap">
          <svg class="drawer-search-icon" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
          <input
            v-model="studentSearchKeyword"
            class="drawer-search-input"
            placeholder="搜索学生姓名..."
            @input="searchStudents"
          />
        </div>
      </div>

      <div class="drawer-body">
        <!-- Already associated -->
        <div class="drawer-section">
          <div class="drawer-section-title">
            已关联学生
            <span class="count">{{ currentStudentIds.length }}</span>
          </div>
          <div v-if="loadingStudents" class="drawer-loading">
            <span class="spinner-sm"></span>
            <span>加载中...</span>
          </div>
          <div v-else-if="currentStudentDetails.length === 0" class="drawer-empty">暂无关联学生</div>
          <template v-else>
            <div v-for="s in currentStudentDetails" :key="s.id" class="student-chip linked">
              <div class="mini-avatar" :style="{ background: getAvatarGradient(s.name || s.id) }">{{ getAvatarChar(s.name || '?') }}</div>
              <div class="stu-info">
                <div class="stu-name">{{ s.name }}</div>
                <div class="stu-grade">{{ getGradeLabel(s.grade) }} · {{ s.id }}</div>
              </div>
              <button class="chip-remove" title="移除关联" :disabled="removingStudentId === s.id" @click="removeStudent(s.id)">
                <svg v-if="removingStudentId !== s.id" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                <span v-else class="spinner-sm"></span>
              </button>
            </div>
          </template>
        </div>

        <!-- Add students -->
        <div class="drawer-section">
          <div class="drawer-section-title">
            添加学生
            <span v-if="studentSearchResults.length > 0" class="count">{{ availableToAdd.length }}</span>
          </div>
          <div v-if="searchingStudents" class="drawer-loading">
            <span class="spinner-sm"></span>
            <span>搜索中...</span>
          </div>
          <div v-else-if="studentSearchResults.length === 0" class="drawer-empty-hint">
            输入学生姓名搜索可添加的学生
          </div>
          <template v-else>
            <div v-for="s in availableToAdd" :key="s.id" class="student-chip addable">
              <div class="mini-avatar" :style="{ background: getAvatarGradient(s.name || s.id) }">{{ getAvatarChar(s.name || '?') }}</div>
              <div class="stu-info">
                <div class="stu-name">{{ s.name }}</div>
                <div class="stu-grade">{{ getGradeLabel(s.grade) }} · {{ s.id }}</div>
              </div>
              <button class="chip-add" title="添加关联" :disabled="addingStudentId === s.id" @click="addStudent(s.id)">
                <svg v-if="addingStudentId !== s.id" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                <span v-else class="spinner-sm"></span>
              </button>
            </div>
            <div v-if="availableToAdd.length === 0" class="drawer-empty-hint">搜索结果均已是关联学生</div>
          </template>
        </div>
      </div>

      <div class="drawer-footer">
        <button class="adm-btn adm-btn-primary" @click="closeStudentsDrawer">完成</button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { assistantPortalClient, type AssistantAccountDto, type SubjectOption } from '../services/assistantPortalApi'
import { getIdentityAdminApiClient, type IdentityUser } from '../services/identityApi'
import { studentAdminClient, type StudentDto, type GradeOption } from '../services/studentAdminApi'
import {
  getSubjectLabel, getSubjectCssClass,
  getAvatarGradient, getAvatarChar, formatDate,
} from '../utils/subject'

const assistants = ref<AssistantAccountDto[]>([])
const loading = ref(false)
const identityUserMap = ref<Record<string, IdentityUser>>({})
const availableSubjects = ref<SubjectOption[]>([])
const gradeOptions = ref<GradeOption[]>([])
const assistantStudentCounts = ref<Record<string, number>>({})

const searchKeyword = ref('')
const searchSubject = ref<number | undefined>(undefined)

const filteredAssistants = computed(() => {
  let list = assistants.value
  const kw = searchKeyword.value.trim().toLowerCase()
  if (kw) {
    list = list.filter(a => {
      const name = getDisplayName(a).toLowerCase()
      const phone = getPhone(a).toLowerCase()
      const uid = (a.userId || '').toLowerCase()
      return name.includes(kw) || phone.includes(kw) || uid.includes(kw)
    })
  }
  if (searchSubject.value !== undefined) {
    list = list.filter(a => a.subjects.includes(searchSubject.value as number))
  }
  return list
})

const showGrantDialog = ref(false)
const grantForm = ref({ keyword: '', selectedUserId: '', selectedSubjects: [] as number[] })
const searchResults = ref<IdentityUser[]>([])
const granting = ref(false)

const showSubjectsDialogVisible = ref(false)
const currentAssistant = ref<AssistantAccountDto | null>(null)
const selectedSubjects = ref<number[]>([])
const savingSubjects = ref(false)

// Drawer state
const showStudentsDrawer = ref(false)
const currentStudentIds = ref<string[]>([])
const currentStudentDetails = ref<StudentDto[]>([])
const loadingStudents = ref(false)
const removingStudentId = ref<string | null>(null)
const addingStudentId = ref<string | null>(null)

const studentSearchKeyword = ref('')
const studentSearchResults = ref<StudentDto[]>([])
const searchingStudents = ref(false)

const selectedUser = computed(() => {
  if (!grantForm.value.selectedUserId) return null
  return searchResults.value.find(u => u.userId === grantForm.value.selectedUserId) || null
})

const availableToAdd = computed(() =>
  studentSearchResults.value.filter(s => !currentStudentIds.value.includes(s.id))
)

function getDisplayName(a: AssistantAccountDto): string {
  if (a.username) return a.username
  if (a.userId && identityUserMap.value[a.userId]) {
    return identityUserMap.value[a.userId].displayName || identityUserMap.value[a.userId].username || a.userId
  }
  return a.userId || `#${a.id}`
}

function getPhone(a: AssistantAccountDto): string {
  if (a.phone) return a.phone
  if (a.userId && identityUserMap.value[a.userId]?.phone) return identityUserMap.value[a.userId].phone
  return '-'
}

function getRemark(a: AssistantAccountDto): string {
  if (a.userId && identityUserMap.value[a.userId]?.remark) return identityUserMap.value[a.userId].remark || ''
  return ''
}

function getStudentCount(userId: string | null): number {
  if (!userId) return 0
  return assistantStudentCounts.value[userId] ?? 0
}

function getGradeLabel(grade: number): string {
  return gradeOptions.value.find(g => g.value === grade)?.label || `年级 ${grade}`
}

async function loadAssistants() {
  loading.value = true
  try {
    const result = await assistantPortalClient.getAssistants()
    assistants.value = result.data
    await loadIdentityUsers()
  } catch (error) {
    ElMessage.error('加载助教列表失败')
  } finally {
    loading.value = false
  }
}

async function loadIdentityUsers() {
  const userIds = assistants.value
    .map(t => t.userId)
    .filter((id): id is string => !!id)
  if (userIds.length === 0) return
  try {
    const users = await getIdentityAdminApiClient().getUsersByIds(userIds)
    const map: Record<string, IdentityUser> = {}
    for (const u of users) {
      map[u.userId] = u
    }
    identityUserMap.value = map
  } catch (error) {
    console.error('Failed to load identity users:', error)
  }
}

async function loadAvailableSubjects() {
  try {
    const result = await assistantPortalClient.getAvailableSubjects()
    availableSubjects.value = result.data
  } catch (error) {
    console.error('Failed to load subjects:', error)
    ElMessage.error('加载助教学科列表失败')
  }
}

async function loadGradeOptions() {
  try {
    gradeOptions.value = await studentAdminClient.getGrades()
  } catch (error) {
    console.error('Failed to load grade options:', error)
  }
}

async function loadStudentCounts() {
  for (const a of assistants.value) {
    if (a.userId) {
      try {
        const result = await assistantPortalClient.getAssistantStudents(a.userId)
        assistantStudentCounts.value[a.userId] = result.data.length
      } catch {
        assistantStudentCounts.value[a.userId] = 0
      }
    }
  }
}

function onSearch() {
  // filteredAssistants is computed
}

function resetFilters() {
  searchKeyword.value = ''
  searchSubject.value = undefined
}

function openGrantDialog() {
  grantForm.value = { keyword: '', selectedUserId: '', selectedSubjects: [] }
  searchResults.value = []
  showGrantDialog.value = true
}

function closeGrantDialog() {
  showGrantDialog.value = false
  grantForm.value = { keyword: '', selectedUserId: '', selectedSubjects: [] }
  searchResults.value = []
}

async function searchUsers() {
  if (!grantForm.value.keyword || grantForm.value.keyword.length < 2) {
    searchResults.value = []
    return
  }
  try {
    const keyword = grantForm.value.keyword
    const isPhone = /^\d+$/.test(keyword)
    const result = await getIdentityAdminApiClient().getUsers({
      username: isPhone ? undefined : keyword,
      phone: isPhone ? keyword : undefined,
      page: 1,
      pageSize: 20,
    })
    searchResults.value = result.items
  } catch (error) {
    console.error('Search users failed:', error)
    ElMessage.error('搜索用户失败')
  }
}

function selectUser(u: IdentityUser) {
  grantForm.value.selectedUserId = u.userId
  searchResults.value = []
  grantForm.value.keyword = ''
}

function clearSelectedUser() {
  grantForm.value.selectedUserId = ''
}

function toggleGrantSubject(subject: number) {
  const idx = grantForm.value.selectedSubjects.indexOf(subject)
  if (idx >= 0) grantForm.value.selectedSubjects.splice(idx, 1)
  else grantForm.value.selectedSubjects.push(subject)
}

async function grantAssistant() {
  if (!grantForm.value.selectedUserId) {
    ElMessage.warning('请选择用户')
    return
  }
  if (grantForm.value.selectedSubjects.length === 0) {
    ElMessage.warning('请选择至少一个科目')
    return
  }
  granting.value = true
  try {
    await assistantPortalClient.grantAssistant(grantForm.value.selectedUserId, {
      subjects: grantForm.value.selectedSubjects,
    })
    ElMessage.success('授予权限成功')
    closeGrantDialog()
    await loadAssistants()
    await loadStudentCounts()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '授予权限失败')
  } finally {
    granting.value = false
  }
}

async function revokeAssistant(a: AssistantAccountDto) {
  if (!a.userId) {
    ElMessage.warning('该助教没有关联的用户ID')
    return
  }
  try {
    await ElMessageBox.confirm(
      `确认撤销 ${getDisplayName(a)} 的助教权限？关联的学生也将被清除。`,
      '确认撤销',
      { type: 'warning' }
    )
    await assistantPortalClient.revokeAssistant(a.userId)
    ElMessage.success('撤销权限成功')
    await loadAssistants()
    await loadStudentCounts()
  } catch (error: any) {
    if (error !== 'cancel') {
      ElMessage.error(error.response?.data?.message || '撤销权限失败')
    }
  }
}

function openSubjectsDialog(a: AssistantAccountDto) {
  currentAssistant.value = a
  selectedSubjects.value = [...a.subjects]
  showSubjectsDialogVisible.value = true
}

function toggleSelectedSubject(subject: number) {
  const idx = selectedSubjects.value.indexOf(subject)
  if (idx >= 0) selectedSubjects.value.splice(idx, 1)
  else selectedSubjects.value.push(subject)
}

async function removeSubject(a: AssistantAccountDto, subjectId: number) {
  if (!a.userId) {
    ElMessage.warning('该助教没有关联的用户ID')
    return
  }
  const label = getSubjectLabel(subjectId)
  try {
    await ElMessageBox.confirm(
      `确认移除「${getDisplayName(a)}」的「${label}」科目授权？`,
      '确认移除',
      { type: 'warning' }
    )
    await assistantPortalClient.removeAssistantSubject(a.userId, subjectId)
    ElMessage.success(`已移除 ${label}`)
    await loadAssistants()
  } catch (error: any) {
    if (error !== 'cancel') {
      ElMessage.error(error.response?.data?.message || '删除科目失败')
    }
  }
}

async function saveAssistantSubjects() {
  if (!currentAssistant.value?.userId) {
    ElMessage.warning('该助教没有关联的用户ID')
    return
  }
  savingSubjects.value = true
  try {
    await assistantPortalClient.setAssistantSubjects(currentAssistant.value.userId, selectedSubjects.value)
    ElMessage.success('保存成功')
    showSubjectsDialogVisible.value = false
    await loadAssistants()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '保存失败')
  } finally {
    savingSubjects.value = false
  }
}

async function openStudentsDrawer(a: AssistantAccountDto) {
  currentAssistant.value = a
  showStudentsDrawer.value = true
  studentSearchKeyword.value = ''
  studentSearchResults.value = []
  await loadAssistantStudents(a.userId)
}

function closeStudentsDrawer() {
  showStudentsDrawer.value = false
  studentSearchKeyword.value = ''
  studentSearchResults.value = []
}

async function loadAssistantStudents(userId: string | null) {
  if (!userId) return
  loadingStudents.value = true
  currentStudentDetails.value = []
  try {
    const result = await assistantPortalClient.getAssistantStudents(userId)
    currentStudentIds.value = result.data
    if (currentStudentIds.value.length > 0) {
      const details: StudentDto[] = []
      for (const sid of currentStudentIds.value) {
        try {
          const student = await studentAdminClient.getStudent(sid)
          details.push(student)
        } catch {
          details.push({ id: sid, name: '未知', grade: 0, identityAccountIds: [], createdAt: 0, updatedAt: 0 })
        }
      }
      currentStudentDetails.value = details
    }
    assistantStudentCounts.value[userId] = currentStudentIds.value.length
  } catch (error) {
    console.error('Failed to load assistant students:', error)
    ElMessage.error('加载学生列表失败')
  } finally {
    loadingStudents.value = false
  }
}

async function addStudent(studentId: string) {
  if (!currentAssistant.value?.userId) return
  addingStudentId.value = studentId
  try {
    await assistantPortalClient.addAssistantStudent(currentAssistant.value.userId, studentId)
    ElMessage.success('添加成功')
    await loadAssistantStudents(currentAssistant.value.userId)
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '添加失败')
  } finally {
    addingStudentId.value = null
  }
}

async function removeStudent(studentId: string) {
  if (!currentAssistant.value?.userId) return
  removingStudentId.value = studentId
  try {
    await assistantPortalClient.removeAssistantStudent(currentAssistant.value.userId, studentId)
    ElMessage.success('移除成功')
    await loadAssistantStudents(currentAssistant.value.userId)
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '移除失败')
  } finally {
    removingStudentId.value = null
  }
}

async function searchStudents() {
  if (!studentSearchKeyword.value || studentSearchKeyword.value.length < 1) {
    studentSearchResults.value = []
    return
  }
  searchingStudents.value = true
  try {
    const result = await studentAdminClient.getStudents({
      name: studentSearchKeyword.value || undefined,
      page: 1,
      pageSize: 20,
    })
    studentSearchResults.value = result.items
  } catch (error) {
    console.error('Search students failed:', error)
    ElMessage.error('搜索学生失败')
  } finally {
    searchingStudents.value = false
  }
}

onMounted(async () => {
  await Promise.all([loadAvailableSubjects(), loadGradeOptions()])
  await loadAssistants()
  await loadStudentCounts()
})
</script>

<style scoped>
.assistant-view {
  min-width: 1100px;
}

/* Filter Bar */
.filter-input-wrap {
  position: relative;
  flex: 1;
  min-width: 260px;
  max-width: 360px;
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
.filter-spacer { flex: 1; }

/* Table */
.table-card { overflow: hidden; }
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
.spinner-sm {
  width: 14px; height: 14px;
  border: 2px solid var(--adm-border);
  border-top-color: var(--adm-primary);
  border-radius: 50%;
  animation: adm-spin 0.7s linear infinite;
  display: inline-block;
}
@keyframes adm-spin { to { transform: rotate(360deg); } }

.table-wrap { overflow-x: auto; }
.adm-table { width: 100%; border-collapse: collapse; }
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
.adm-table tbody tr { transition: background 0.12s; }
.adm-table tbody tr:hover { background: var(--adm-surface-subtle); }
.adm-table tbody tr.highlighted { background: var(--adm-primary-bg); }
.adm-table tbody tr.highlighted td:first-child { border-left: 3px solid var(--adm-primary); }
.adm-table tbody tr:last-child td { border-bottom: none; }
.col-avatar { width: 56px; }
.col-id { width: 200px; }
.col-phone { width: 140px; }
.col-students { width: 160px; }
.col-time { width: 130px; }
.col-actions { width: 160px; text-align: right; }

.ast-avatar {
  width: 36px; height: 36px;
  border-radius: 50%;
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 14px;
  font-weight: 600;
  flex-shrink: 0;
}
.mono-id {
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--adm-text-secondary);
}
.phone-cell {
  color: var(--adm-text-secondary);
  font-size: 13px;
}
.ast-name {
  font-weight: 500;
  color: var(--adm-text-primary);
}
.note-cell {
  color: var(--adm-text-tertiary);
  font-size: 12px;
  max-width: 180px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  display: inline-block;
}
.note-cell.empty { color: var(--adm-text-muted); }

/* Subject tags (closable) */
.subj-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.subj-tag {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 4px 2px 8px;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 500;
  border: 1px solid;
  line-height: 1.6;
  white-space: nowrap;
}
.subj-tag .subj-x {
  width: 14px;
  height: 14px;
  border-radius: 3px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  opacity: 0.6;
  transition: all 0.1s;
  margin-left: 2px;
  background: transparent;
  border: none;
  color: inherit;
  padding: 0;
}
.subj-tag .subj-x:hover {
  opacity: 1;
  background: rgba(0, 0, 0, 0.08);
}
.subj-warning {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 500;
  background: var(--adm-warning-bg, #fff7ed);
  color: var(--adm-warning, #ea580c);
  border: 1px solid var(--adm-warning-border, #fed7aa);
}

/* Student count badge */
.student-count {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 3px 4px 3px 10px;
  border-radius: 6px;
  font-size: 12px;
  font-weight: 600;
  background: var(--adm-primary-bg);
  color: var(--adm-primary);
}
.student-count.zero {
  background: var(--adm-surface-subtle);
  color: var(--adm-text-muted);
}
.student-count .manage-btn {
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 4px;
  background: rgba(59, 130, 246, 0.12);
  color: var(--adm-primary-dark, #1d4ed8);
  cursor: pointer;
  font-weight: 500;
  transition: all 0.1s;
  border: none;
}
.student-count .manage-btn:hover {
  background: var(--adm-primary);
  color: #fff;
}
.student-count.zero .manage-btn {
  background: var(--adm-border);
  color: var(--adm-text-tertiary);
}

.time-cell {
  color: var(--adm-text-tertiary);
  font-size: 12px;
  white-space: nowrap;
}
.actions-cell {
  display: flex;
  align-items: center;
  gap: 4px;
  justify-content: flex-end;
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
.form-group { margin-bottom: 18px; }
.form-group:last-child { margin-bottom: 0; }
.form-label {
  display: block;
  font-size: 13px;
  font-weight: 500;
  color: var(--adm-text-primary);
  margin-bottom: 6px;
}
.required { color: var(--adm-error); margin-left: 2px; }
.form-input {
  width: 100%;
  padding: 9px 12px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  font-size: 13px;
  color: var(--adm-text-primary);
  background: #fff;
  outline: none;
  transition: all 0.15s;
  height: 36px;
  box-sizing: border-box;
}
.form-input.has-icon { padding-left: 36px; }
.form-input:focus {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}
.form-input::placeholder { color: var(--adm-text-muted); }
.form-input-icon {
  position: absolute;
  left: 11px;
  top: 50%;
  transform: translateY(-50%);
  color: var(--adm-text-muted);
  pointer-events: none;
}

/* User search dropdown */
.user-search-wrap { position: relative; z-index: 11; }
.user-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  background: #fff;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  box-shadow: var(--adm-shadow-lg);
  max-height: 280px;
  overflow-y: auto;
  z-index: 10;
}
.search-option {
  padding: 10px 12px;
  display: flex;
  align-items: center;
  gap: 10px;
  cursor: pointer;
  transition: background 0.1s;
  font-size: 13px;
  border-bottom: 1px solid var(--adm-border-light);
}
.search-option:last-child { border-bottom: none; }
.search-option:hover { background: var(--adm-surface-subtle); }
.search-option.selected { background: var(--adm-primary-bg); }
.mini-avatar {
  width: 30px; height: 30px;
  border-radius: 50%;
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  font-weight: 600;
  flex-shrink: 0;
}
.opt-info { flex: 1; min-width: 0; }
.opt-phone { color: var(--adm-text-primary); font-weight: 500; }
.opt-name { color: var(--adm-text-tertiary); font-size: 12px; margin-top: 2px; }
.check-mark { margin-left: auto; color: var(--adm-primary); flex-shrink: 0; }

.selected-user-banner {
  margin-top: 10px;
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  background: var(--adm-primary-bg);
  border: 1px solid var(--adm-info-border, #bfdbfe);
  border-radius: var(--adm-radius-md);
}
.sel-info { flex: 1; min-width: 0; }
.sel-name { font-size: 13px; font-weight: 600; color: var(--adm-text-primary); }
.sel-meta { font-size: 12px; color: var(--adm-text-tertiary); margin-top: 2px; }
.sel-clear {
  width: 22px; height: 22px;
  border: none;
  background: transparent;
  color: var(--adm-text-muted);
  font-size: 18px;
  line-height: 1;
  cursor: pointer;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
}
.sel-clear:hover { background: rgba(0,0,0,0.08); color: var(--adm-text-primary); }

/* Subject checkbox grid */
.subj-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 8px;
}
.subj-check {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 9px 10px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  cursor: pointer;
  transition: all 0.15s;
  font-size: 13px;
  color: var(--adm-text-secondary);
  user-select: none;
}
.subj-check:hover {
  border-color: var(--adm-primary-light, #93c5fd);
  background: var(--adm-primary-bg);
}
.subj-check.checked {
  border-color: var(--adm-primary);
  background: var(--adm-primary-bg);
  color: var(--adm-primary-dark, #1d4ed8);
  font-weight: 500;
}
.chk-box {
  width: 16px;
  height: 16px;
  border: 1.5px solid var(--adm-border);
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  transition: all 0.15s;
  background: #fff;
}
.subj-check.checked .chk-box {
  background: var(--adm-primary);
  border-color: var(--adm-primary);
}

.readonly-note {
  padding: 10px 12px;
  background: var(--adm-surface-subtle);
  border: 1px solid var(--adm-border-light);
  border-radius: var(--adm-radius-md);
  font-size: 13px;
  color: var(--adm-text-secondary);
  line-height: 1.5;
}

.ast-info-banner {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px;
  background: var(--adm-surface-subtle);
  border-radius: var(--adm-radius-md);
  margin-bottom: 16px;
}
.ast-info-banner .ast-avatar {
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

/* Drawer */
.drawer-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.4);
  z-index: 150;
  animation: maskIn 0.2s;
}
.drawer {
  position: fixed;
  top: 0;
  right: 0;
  width: 400px;
  height: 100vh;
  background: var(--adm-surface-elevated);
  z-index: 200;
  display: flex;
  flex-direction: column;
  box-shadow: -8px 0 32px rgba(15, 23, 42, 0.15);
  animation: drawerIn 0.25s ease;
  max-width: calc(100vw - 32px);
}
@keyframes drawerIn {
  from { transform: translateX(100%); }
  to { transform: translateX(0); }
}
.drawer-header {
  padding: 18px 20px;
  border-bottom: 1px solid var(--adm-border);
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
}
.drawer-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--adm-text-primary);
}
.drawer-subtitle {
  font-size: 12px;
  color: var(--adm-text-tertiary);
  margin-top: 4px;
}
.drawer-close {
  width: 32px;
  height: 32px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-tertiary);
  cursor: pointer;
  transition: all 0.15s;
  flex-shrink: 0;
  background: transparent;
  border: none;
}
.drawer-close:hover {
  background: var(--adm-surface-subtle);
  color: var(--adm-text-primary);
}

.drawer-search {
  padding: 14px 20px;
  border-bottom: 1px solid var(--adm-border);
}
.drawer-search-wrap {
  position: relative;
}
.drawer-search-input {
  width: 100%;
  padding: 8px 12px 8px 34px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  font-size: 13px;
  outline: none;
  transition: all 0.15s;
  background: var(--adm-surface-subtle);
  box-sizing: border-box;
  height: 36px;
}
.drawer-search-input:focus {
  border-color: var(--adm-primary);
  background: #fff;
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}
.drawer-search-icon {
  position: absolute;
  left: 10px;
  top: 50%;
  transform: translateY(-50%);
  color: var(--adm-text-muted);
  pointer-events: none;
}

.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 16px 20px;
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

.student-chip {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  margin-bottom: 8px;
  transition: all 0.15s;
  background: #fff;
}
.student-chip:hover {
  border-color: var(--adm-primary-light, #93c5fd);
  background: var(--adm-primary-bg);
}
.student-chip.addable {
  border-style: dashed;
  border-color: var(--adm-primary-light, #93c5fd);
  background: #fafcff;
}
.student-chip .stu-info { flex: 1; min-width: 0; }
.student-chip .stu-name {
  font-size: 13px;
  font-weight: 500;
  color: var(--adm-text-primary);
}
.student-chip .stu-grade {
  font-size: 11px;
  color: var(--adm-text-tertiary);
  margin-top: 1px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.chip-remove {
  width: 26px;
  height: 26px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-muted);
  cursor: pointer;
  transition: all 0.15s;
  background: transparent;
  border: none;
  padding: 0;
}
.chip-remove:hover:not(:disabled) {
  background: var(--adm-error-bg, #fef2f2);
  color: var(--adm-error);
}
.chip-remove:disabled { cursor: wait; opacity: 0.6; }
.chip-add {
  width: 28px;
  height: 28px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--adm-primary-bg);
  color: var(--adm-primary);
  cursor: pointer;
  transition: all 0.15s;
  font-size: 13px;
  font-weight: 600;
  border: none;
  padding: 0;
  flex-shrink: 0;
}
.chip-add:hover:not(:disabled) {
  background: var(--adm-primary);
  color: #fff;
}
.chip-add:disabled { cursor: wait; opacity: 0.6; }

.drawer-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 16px;
  color: var(--adm-text-muted);
  font-size: 12px;
}
.drawer-empty {
  text-align: center;
  padding: 12px;
  color: var(--adm-text-muted);
  font-size: 12px;
}
.drawer-empty-hint {
  text-align: center;
  padding: 12px;
  color: var(--adm-text-muted);
  font-size: 12px;
  background: var(--adm-surface-subtle);
  border-radius: var(--adm-radius-md);
}

.drawer-footer {
  padding: 14px 20px;
  border-top: 1px solid var(--adm-border);
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  background: var(--adm-surface-subtle, #fafbfc);
}
</style>
