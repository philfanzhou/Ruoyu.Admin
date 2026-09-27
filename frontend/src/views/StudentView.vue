<template>
  <div class="adm-fade-in student-view">
    <PageHeader title="学生管理" description="管理学生档案、关联账户与开放学科">
      <template #actions>
        <el-button type="primary" :icon="Plus" @click="openCreateDialog">创建学生</el-button>
      </template>
    </PageHeader>

    <FilterBar @search="onSearch" @reset="resetFilters">
      <el-form-item>
        <el-input
          v-model="searchName"
          class="filter-keyword"
          :prefix-icon="Search"
          placeholder="搜索学生姓名或 ID"
          clearable
          @keyup.enter="onSearch"
        />
      </el-form-item>
      <el-form-item>
        <el-select v-model="searchGrade" class="filter-control" placeholder="全部年级" clearable @change="onSearch">
          <el-option v-for="g in gradeOptions" :key="g.value" :label="g.label" :value="g.value" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-select v-model="searchSubject" class="filter-control" placeholder="全部学科" clearable @change="onSearch">
          <el-option v-for="s in subjectOptions" :key="s.value" :label="s.displayName || s.name" :value="s.value" />
        </el-select>
      </el-form-item>
    </FilterBar>

    <el-alert
      v-if="searchSubject !== undefined"
      class="subject-filter-hint"
      type="info"
      :closable="false"
      show-icon
      title="学科筛选仅作用于当前页结果：服务端不支持按学科筛选，页码与总数只反映当前页。"
    />

    <DataTableCard
      :data="students"
      :loading="loading"
      :error="loadError"
      empty-text="暂无学生数据"
      row-key="id"
      @retry="loadStudents"
    >
      <el-table-column label="头像" width="72">
        <template #default="{ row }">
          <div class="user-avatar" :style="{ background: getAvatarGradient(row.name || row.id) }">{{ getAvatarChar(row.name || '?') }}</div>
        </template>
      </el-table-column>
      <el-table-column label="ID" min-width="200">
        <template #default="{ row }"><span class="mono-id">{{ row.id }}</span></template>
      </el-table-column>
      <el-table-column label="姓名" min-width="120">
        <template #default="{ row }"><span class="user-name">{{ row.name }}</span></template>
      </el-table-column>
      <el-table-column label="年级" width="130">
        <template #default="{ row }"><span class="grade-text">{{ getGradeLabel(row.grade) }}</span></template>
      </el-table-column>
      <el-table-column label="开放学科" min-width="200">
        <template #default="{ row }">
          <div v-if="getStudentSubjects(row).length > 0" class="subj-tags">
            <SubjectTag v-for="subj in getStudentSubjects(row)" :key="subj" :subject="subj" />
          </div>
          <span v-else class="text-muted">未设置</span>
        </template>
      </el-table-column>
      <el-table-column label="创建时间" width="130">
        <template #default="{ row }"><span class="time-cell">{{ formatDate(row.createdAt) }}</span></template>
      </el-table-column>
      <el-table-column label="操作" width="260" align="right" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="openEditDialog(row)">编辑</el-button>
          <el-button v-if="getStudentSubjects(row).length > 0" link type="primary" @click="openSubjectsDialog(row)">学科</el-button>
          <el-button v-else type="primary" size="small" @click="openSubjectsDialog(row)">分配</el-button>
          <el-button link type="primary" @click="openLinkedAccountsDialog(row)">关联账户</el-button>
          <el-button link type="danger" @click="deleteStudent(row)">删除</el-button>
        </template>
      </el-table-column>
      <template v-if="students.length > 0" #footer>
        <ListPagination v-model:page="page" v-model:page-size="pageSize" :total="total" @change="onPageChange" />
      </template>
    </DataTableCard>

    <!-- Create Student -->
    <el-dialog append-to-body v-model="showCreateDialog" title="创建学生" width="520px" :close-on-click-modal="false">
      <el-form label-position="top" @submit.prevent>
        <el-form-item label="关联账户" required>
          <el-select
            v-model="createForm.identityAccountIds"
            multiple
            filterable
            remote
            :reserve-keyword="false"
            :remote-method="onCreateAccountQuery"
            placeholder="输入用户名或手机号搜索（至少 2 个字符）"
            no-data-text="无匹配账户"
            class="full-width"
            @change="onCreateAccountsChange"
          >
            <el-option
              v-for="u in createAccountOptions"
              :key="u.userId"
              :label="u.username || u.userId"
              :value="u.userId"
            >
              <span class="option-name">{{ u.username || u.displayName || u.userId }}</span>
              <span class="option-meta">{{ u.phone || '无手机号' }}<template v-if="u.displayName && u.displayName !== u.username"> · {{ u.displayName }}</template></span>
            </el-option>
          </el-select>
        </el-form-item>
        <el-form-item label="学生姓名" required>
          <el-input v-model="createForm.name" placeholder="请输入学生姓名" />
        </el-form-item>
        <el-form-item label="年级">
          <el-select v-model="createForm.grade" class="full-width">
            <el-option :value="0" label="请选择年级" />
            <el-option v-for="g in gradeOptions" :key="g.value" :label="g.label" :value="g.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="开放学科">
          <el-checkbox-group v-model="createOpenSubjects" class="subject-checks">
            <el-checkbox v-for="s in subjectOptions" :key="s.value" :value="s.value" border>{{ s.displayName || s.name }}</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="closeCreateDialog">取消</el-button>
        <el-button type="primary" :loading="creating" @click="createStudent">创建</el-button>
      </template>
    </el-dialog>

    <!-- Edit Student -->
    <el-dialog append-to-body v-model="showEditDialog" title="编辑学生" width="460px" :close-on-click-modal="false">
      <el-form label-position="top" @submit.prevent>
        <el-form-item label="学生姓名" required>
          <el-input v-model="editForm.name" placeholder="请输入学生姓名" />
        </el-form-item>
        <el-form-item label="年级">
          <el-select v-model="editForm.grade" class="full-width">
            <el-option :value="0" label="请选择年级" />
            <el-option v-for="g in gradeOptions" :key="g.value" :label="g.label" :value="g.value" />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showEditDialog = false">取消</el-button>
        <el-button type="primary" @click="saveEdit">保存</el-button>
      </template>
    </el-dialog>

    <!-- Linked Accounts -->
    <el-dialog
      append-to-body
      v-model="showLinkedAccountsDialog"
      title="关联账户"
      width="640px"
      :close-on-click-modal="false"
      @close="closeLinkedAccountsDialog"
    >
      <div v-if="currentStudent" class="user-info-banner">
        <div class="user-avatar" :style="{ background: getAvatarGradient(currentStudent.name || currentStudent.id) }">{{ getAvatarChar(currentStudent.name || '?') }}</div>
        <div>
          <div class="info-name">{{ currentStudent.name }}</div>
          <div class="info-sub">{{ getGradeLabel(currentStudent.grade) }} · {{ currentStudent.id }}</div>
        </div>
      </div>

      <div v-if="linkedAccountsLoading" v-loading="true" class="drawer-loading" />

      <div v-else>
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
            <el-button
              link
              type="danger"
              :icon="Close"
              title="移除关联"
              aria-label="移除关联"
              :loading="busyLinkedUserId === a.userId"
              :disabled="busyLinkedUserId === a.userId"
              @click="runLinkAccount(a.userId, 'unlink')"
            />
          </div>
        </div>

        <div class="drawer-section">
          <div class="drawer-section-title">添加账户</div>
          <el-input
            v-model="accountSearchKeyword"
            :prefix-icon="Search"
            placeholder="搜索用户名或手机号添加（至少 2 个字符）"
            clearable
            @input="searchAccountsForLink"
          >
            <template v-if="searchingAccounts" #suffix><el-icon class="is-loading"><Loading /></el-icon></template>
          </el-input>
          <div v-if="accountSearchKeyword && availableAccountsToAdd.length === 0 && !searchingAccounts" class="drawer-empty-hint">无符合条件的可添加账户</div>
          <div v-for="u in availableAccountsToAdd" :key="u.userId" class="account-row addable">
            <div class="mini-avatar" :style="{ background: getAvatarGradient(u.username || u.userId) }">{{ getAvatarChar(u.username || '?') }}</div>
            <div class="account-row-info">
              <div class="account-row-name">{{ u.username || u.userId }}</div>
              <div class="account-row-meta">{{ u.phone || '无手机号' }}</div>
            </div>
            <el-button
              link
              type="primary"
              :icon="Plus"
              title="添加关联"
              aria-label="添加关联"
              :loading="busyLinkedUserId === u.userId"
              :disabled="busyLinkedUserId === u.userId"
              @click="runLinkAccount(u.userId, 'link')"
            />
          </div>
        </div>
      </div>
      <template #footer>
        <el-button @click="closeLinkedAccountsDialog">关闭</el-button>
      </template>
    </el-dialog>

    <!-- Open Subjects -->
    <el-dialog append-to-body v-model="showOpenSubjectsDialogVisible" title="管理开放学科" width="560px" :close-on-click-modal="false">
      <div v-if="currentStudent" class="user-info-banner">
        <div class="user-avatar" :style="{ background: getAvatarGradient(currentStudent.name || currentStudent.id) }">{{ getAvatarChar(currentStudent.name || '?') }}</div>
        <div>
          <div class="info-name">{{ currentStudent.name }}</div>
          <div class="info-sub">{{ getGradeLabel(currentStudent.grade) }}</div>
        </div>
      </div>
      <el-form label-position="top" @submit.prevent>
        <el-form-item label="已开放学科">
          <el-checkbox-group v-model="openSubjects" class="subject-checks">
            <el-checkbox v-for="s in subjectOptions" :key="s.value" :value="s.value" border>{{ s.displayName || s.name }}</el-checkbox>
          </el-checkbox-group>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showOpenSubjectsDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="savingSubjects" @click="saveOpenSubjects">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { Close, Loading, Plus, Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { confirmDanger } from '../utils/confirm'
import studentAdminApi, { type IdentityAccountDto, type SubjectOption, type GradeOption } from '../services/studentAdminApi'
import identityApi, { type IdentityUser } from '../services/identityApi'
import { extractMsg } from '../services/apiBase'
import {
  getAvatarGradient, getAvatarChar, formatDate,
} from '../utils/subject'
import PageHeader from '../components/list/PageHeader.vue'
import FilterBar from '../components/list/FilterBar.vue'
import DataTableCard from '../components/list/DataTableCard.vue'
import ListPagination from '../components/list/ListPagination.vue'
import SubjectTag from '../components/list/SubjectTag.vue'

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
const loadError = ref<string | null>(null)
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
  loadError.value = null
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
    // Drop stale rows so a failure is never mistaken for an (outdated) result.
    students.value = []
    total.value = 0
    loadError.value = '加载学生列表失败'
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
  if (action === 'unlink') {
    const account = linkedAccountDetails.value.find(a => a.userId === accountId)
    const accountName = account?.displayName || account?.username || accountId
    const confirmed = await confirmDanger({
      title: '解除账户关联',
      message: `将解除学生「${currentStudent.value.name}」与账户「${accountName}」的关联。`,
      confirmText: '解除关联',
    })
    if (!confirmed || !currentStudent.value) return
  }
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

// page / pageSize are already updated by ListPagination (size changes reset to page 1).
function onPageChange() {
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

// el-select remote-method: same rules as before (>= 2 chars, digits search by phone, pageSize 20).
function onCreateAccountQuery(query: string) {
  createSearchKeyword.value = query
  searchCreateIdentityUsers()
}

// Options = already selected accounts (so their tags keep a label) + current search results.
const createAccountOptions = computed(() => {
  const selected = createForm.value.identityAccountIds.map(id => ({
    userId: id,
    username: getAccountDisplayName(id),
    phone: accountMap.value.get(id)?.phone || '',
    displayName: accountMap.value.get(id)?.displayName || '',
  }))
  const selectedIds = new Set(createForm.value.identityAccountIds)
  return [...selected, ...createIdentityUsers.value.filter(u => !selectedIds.has(u.userId))]
})

// Remember display info for newly selected accounts (used by the tags and getAccountDisplayName).
function onCreateAccountsChange(ids: string[]) {
  for (const userId of ids) {
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
  const confirmed = await confirmDanger({
    title: '删除学生',
    message: `将删除学生「${s.name}」（ID：${s.id}）。`,
    confirmText: '删除',
  })
  if (!confirmed) return
  try {
    await studentAdminApi.deleteStudent(s.id)
    ElMessage.success('删除成功')
    await loadStudents()
  } catch (error: unknown) {
    ElMessage.error(extractMsg(error) || '删除失败')
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

onMounted(async () => {
  await Promise.all([loadGradeOptions(), loadSubjectOptions()])
  await loadStudents()
})
</script>

<style scoped>
.filter-keyword {
  width: 260px;
}
.filter-control {
  width: 140px;
}
.full-width {
  width: 100%;
}
.subject-filter-hint {
  margin-bottom: 12px;
}
.subject-checks {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 8px;
  width: 100%;
}
.subject-checks :deep(.el-checkbox) {
  margin-right: 0;
}
.option-name {
  margin-right: 8px;
  color: var(--adm-text-primary);
}
.option-meta {
  color: var(--adm-text-tertiary);
  font-size: 12px;
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
.account-row.addable:first-of-type {
  margin-top: 8px;
}

.drawer-loading {
  min-height: 80px;
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
</style>
