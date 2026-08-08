<template>
  <div class="adm-fade-in user-management-view">
    <!-- Page Header -->
    <div class="adm-page-header">
      <div>
        <h1 class="adm-page-title">助教管理</h1>
        <p class="adm-page-subtitle">管理助教账户、学科授权和权限撤销</p>
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
        <option :value="undefined">全部学科</option>
        <option v-for="s in availableSubjects" :key="s.value" :value="s.value">{{ s.name }}</option>
      </select>
      <div class="filter-spacer"></div>
      <button class="adm-btn adm-btn-primary adm-btn-sm" @click="onSearch">
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
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
      <div v-else-if="assistants.length === 0" class="adm-empty">
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
              <th>学科</th>
              <th class="col-time">创建时间</th>
              <th class="col-actions">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="a in assistants"
              :key="a.userId || a.id"
            >
              <td>
                <div class="user-avatar" :style="{ background: getAvatarGradient(getDisplayName(a)) }">{{ getAvatarChar(getDisplayName(a)) }}</div>
              </td>
              <td><span class="mono-id">{{ a.userId || '-' }}</span></td>
              <td><span class="phone-cell">{{ getPhone(a) }}</span></td>
              <td><span class="user-name">{{ getDisplayName(a) }}</span></td>
              <td>
                <span v-if="getRemark(a)" class="note-cell" :title="getRemark(a)">{{ getRemark(a) }}</span>
                <span v-else class="note-cell empty">-</span>
              </td>
              <td>
                <div v-if="a.subjects && a.subjects.length > 0" class="subj-tags">
                  <span v-for="subj in a.subjects" :key="subj" class="adm-subj-tag closable" :class="getSubjectCssClass(subj)">
                    {{ getSubjectLabel(subj) }}
                    <button class="subj-x" :title="`移除 ${getSubjectLabel(subj)}`" @click="removeSubject(a, subj)">
                      <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                    </button>
                  </span>
                </div>
                <span v-else class="subj-warning">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
                  未分配学科
                </span>
              </td>
              <td><span class="time-cell">{{ formatDate(a.createdAt) }}</span></td>
              <td>
                <div class="actions-cell">
                  <button v-if="a.subjects && a.subjects.length > 0" class="adm-btn-link" @click="openSubjectsDialog(a)">学科</button>
                  <button v-else class="adm-btn adm-btn-primary adm-btn-xs" @click="openSubjectsDialog(a)">
                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                    分配
                  </button>
                  <button class="adm-btn-link danger" @click="revokeAssistant(a)">撤销权限</button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="!loading && assistants.length > 0" class="adm-pagination-bar">
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
                  class="user-search-option"
                  :class="{ selected: grantForm.selectedUserId === u.userId }"
                  @click="selectUser(u)"
                >
                  <div class="mini-avatar" :style="{ background: getAvatarGradient(u.username || u.userId) }">{{ getAvatarChar(u.username || u.userId) }}</div>
                  <div class="user-search-option-info">
                    <div class="user-search-option-name">{{ u.username || u.displayName || u.userId }}</div>
                    <div class="user-search-option-meta">{{ u.phone || '无手机号' }}<span v-if="u.displayName && u.displayName !== u.username"> · {{ u.displayName }}</span></div>
                  </div>
                  <div v-if="grantForm.selectedUserId === u.userId" class="user-search-option-state">
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
            <label class="form-label">授权学科 <span class="required">*</span></label>
            <div class="subj-grid">
              <label
                v-for="s in availableSubjects"
                :key="s.value"
                class="subject-check"
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
          <h3>学科管理</h3>
          <button class="modal-close" @click="showSubjectsDialogVisible = false">×</button>
        </div>
        <div class="modal-body">
          <div v-if="currentAssistant" class="user-info-banner">
            <div class="user-avatar" :style="{ background: getAvatarGradient(getDisplayName(currentAssistant)) }">{{ getAvatarChar(getDisplayName(currentAssistant)) }}</div>
            <div>
              <div class="info-name">{{ getDisplayName(currentAssistant) }}</div>
              <div class="info-sub">{{ getPhone(currentAssistant) }} · 当前 {{ currentAssistant.subjects.length }} 个学科</div>
            </div>
          </div>
          <div class="form-group">
            <label class="form-label">授权学科</label>
            <div class="subj-grid">
              <label
                v-for="s in availableSubjects"
                :key="s.value"
                class="subject-check"
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
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  assistantPortalClient,
  getAssistantPortalErrorMessage,
  type AssistantAccountDto,
  type SubjectOption,
} from '../services/assistantPortalApi'
import { getIdentityAdminApiClient, type IdentityUser } from '../services/identityApi'
import {
  getSubjectLabel, getSubjectCssClass,
  getAvatarGradient, getAvatarChar, formatDate,
} from '../utils/subject'

const assistants = ref<AssistantAccountDto[]>([])
const loading = ref(false)
const identityUserMap = ref<Record<string, IdentityUser>>({})
const availableSubjects = ref<SubjectOption[]>([])

const searchKeyword = ref('')
const searchSubject = ref<number | undefined>(undefined)
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))

const showGrantDialog = ref(false)
const grantForm = ref({ keyword: '', selectedUserId: '', selectedSubjects: [] as number[] })
const searchResults = ref<IdentityUser[]>([])
const granting = ref(false)

const showSubjectsDialogVisible = ref(false)
const currentAssistant = ref<AssistantAccountDto | null>(null)
const selectedSubjects = ref<number[]>([])
const savingSubjects = ref(false)

// Keep the selected account separate from searchResults. Selecting an account
// clears the dropdown results, so deriving this value from searchResults made
// the selection banner disappear immediately after the click.
const selectedUser = ref<IdentityUser | null>(null)

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

async function loadAssistants() {
  loading.value = true
  try {
    const result = await assistantPortalClient.getAssistants({
      keyword: searchKeyword.value.trim() || undefined,
      subject: searchSubject.value,
      page: page.value,
      pageSize: pageSize.value,
    })
    assistants.value = result.data
    total.value = result.total
    page.value = result.page
    pageSize.value = result.pageSize
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
  if (userIds.length === 0) {
    identityUserMap.value = {}
    return
  }
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

function onSearch() {
  page.value = 1
  loadAssistants()
}

function resetFilters() {
  searchKeyword.value = ''
  searchSubject.value = undefined
  page.value = 1
  loadAssistants()
}

function onPageChange(nextPage: number) {
  page.value = nextPage
  loadAssistants()
}

function openGrantDialog() {
  grantForm.value = { keyword: '', selectedUserId: '', selectedSubjects: [] }
  searchResults.value = []
  selectedUser.value = null
  showGrantDialog.value = true
}

function closeGrantDialog() {
  showGrantDialog.value = false
  grantForm.value = { keyword: '', selectedUserId: '', selectedSubjects: [] }
  searchResults.value = []
  selectedUser.value = null
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
  selectedUser.value = u
  searchResults.value = []
  grantForm.value.keyword = ''
}

function clearSelectedUser() {
  grantForm.value.selectedUserId = ''
  selectedUser.value = null
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
    ElMessage.warning('请至少选择一个学科')
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
  } catch (error: unknown) {
    ElMessage.error(getAssistantPortalErrorMessage(error) || '授予权限失败')
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
      `确认撤销 ${getDisplayName(a)} 的助教权限？该操作将移除其所有学科授权。`,
      '确认撤销',
      { type: 'warning' }
    )
    await assistantPortalClient.revokeAssistant(a.userId)
    ElMessage.success('撤销权限成功')
    await loadAssistants()
  } catch (error: unknown) {
    if (error !== 'cancel') {
      ElMessage.error(getAssistantPortalErrorMessage(error) || '撤销权限失败')
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
      `确认移除「${getDisplayName(a)}」的「${label}」学科授权？`,
      '确认移除',
      { type: 'warning' }
    )
    await assistantPortalClient.removeAssistantSubject(a.userId, subjectId)
    ElMessage.success(`已移除 ${label}`)
    await loadAssistants()
  } catch (error: unknown) {
    if (error !== 'cancel') {
      ElMessage.error(getAssistantPortalErrorMessage(error) || '删除学科失败')
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
  } catch (error: unknown) {
    ElMessage.error(getAssistantPortalErrorMessage(error) || '保存失败')
  } finally {
    savingSubjects.value = false
  }
}

onMounted(async () => {
  await loadAvailableSubjects()
  await loadAssistants()
})
</script>
