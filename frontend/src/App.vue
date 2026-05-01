<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  createIdentityAdminApiClient,
  getIdentityErrorMessage,
  type IdentityCredentials,
  type IdentityUser,
} from './services/identityApi'
import {
  studentAdminClient,
  getStudentErrorMessage,
  type StudentDto,
  type GradeOption,
  type OpenSubjectDto,
  type SubjectOption,
} from './services/studentAdminApi'

const IDENTITY_STORAGE_KEY = 'student-admin-identity-credentials'

const appTitle = (window as any).__APP_TITLE__ || 'Student Management Console'

function loadSavedCredentials(key: string): { appId: string; appSecret: string } {
  try {
    const saved = localStorage.getItem(key)
    if (saved) {
      const parsed = JSON.parse(saved) as { appId: string; appSecret: string }
      if (parsed.appId && parsed.appSecret) return parsed
    }
  } catch {
    localStorage.removeItem(key)
  }
  return { appId: '', appSecret: '' }
}

function saveCredentials(key: string, creds: { appId: string; appSecret: string }) {
  try {
    localStorage.setItem(key, JSON.stringify(creds))
  } catch {
    ElMessage.warning('Cannot save credentials to local storage.')
  }
}

const identityCreds = reactive<IdentityCredentials>(loadSavedCredentials(IDENTITY_STORAGE_KEY))
const showConnectionPanel = ref(!loadSavedCredentials(IDENTITY_STORAGE_KEY).appId)

const studentFilters = reactive({ name: '' })
const accountSearch = ref('')
const searchLoading = ref(false)
const searchResults = ref<IdentityUser[]>([])

const createStudentForm = reactive({
  name: '',
  grade: 1,
  selectedAccountIds: [] as string[],
})

const editStudentForm = reactive({
  visible: false,
  studentId: '',
  name: '',
  grade: 1,
  selectedAccountIds: [] as string[],
  accountSearch: '',
  searchLoading: false,
  searchResults: [] as IdentityUser[],
})
const editManagedAccountsLoading = ref(false)

const linkAccountForm = reactive({
  visible: false,
  studentId: '',
  studentName: '',
  accountSearch: '',
  searchLoading: false,
  searchResults: [] as IdentityUser[],
  selectedAccountId: '',
})

const identityConnected = ref(false)
const connectingIdentity = ref(false)
const loadingStudents = ref(false)
const creatingStudent = ref(false)
const updatingStudent = ref(false)
const deletingStudent = ref(false)
const linkingAccount = ref(false)
const unlinkingAccountId = ref<string | null>(null)

const students = ref<StudentDto[]>([])
const gradeOptions = ref<GradeOption[]>([])
const studentTotal = ref(0)
const studentPage = ref(1)
const studentPageSize = ref(20)

const identityClient = ref<ReturnType<typeof createIdentityAdminApiClient> | null>(null)
const editManagedAccountCache = ref<Map<string, IdentityUser>>(new Map())

// 学科相关
const subjectOptions = ref<SubjectOption[]>([])
const selectedOpenSubjects = ref<OpenSubjectDto[]>([])
const loadingOpenSubjects = ref(false)

const gradeLabelMap = computed(() => {
  const map = new Map<number, string>()
  for (const g of gradeOptions.value) {
    map.set(g.value, g.label)
  }
  return map
})

function getGradeLabel(grade: number): string {
  return gradeLabelMap.value.get(grade) ?? String(grade)
}

function formatAccountLabel(user: IdentityUser): string {
  const name = user.displayName || user.username || user.phone || user.userId
  if (user.remark) return `${name}(${user.remark})`
  return name
}

function formatDate(timestamp?: number | null) {
  if (!timestamp) return '-'
  return new Date(timestamp * 1000).toLocaleString()
}

// 学科相关辅助函数
function getSubjectDisplayName(subjectValue: number): string {
  const option = subjectOptions.value.find(s => s.value === subjectValue)
  return option?.displayName ?? `学科${subjectValue}`
}

function isSubjectSelected(subjectValue: number): boolean {
  return selectedOpenSubjects.value.some(s => s.subject === subjectValue)
}

function toggleSubject(subjectValue: number) {
  const existing = selectedOpenSubjects.value.find(s => s.subject === subjectValue)
  if (existing) {
    selectedOpenSubjects.value = selectedOpenSubjects.value.filter(s => s.subject !== subjectValue)
  } else {
    const today = new Date().toISOString().split('T')[0]
    selectedOpenSubjects.value.push({
      id: '',
      subject: subjectValue,
      openStartDate: today,
      openEndDate: null,
      isActive: true,
    })
  }
}

function isSubjectActive(subject: OpenSubjectDto): boolean {
  if (!subject.openStartDate) return false
  const today = new Date().toISOString().split('T')[0]
  const startOk = subject.openStartDate <= today
  const endOk = !subject.openEndDate || subject.openEndDate >= today
  return startOk && endOk
}

function removeOpenSubject(subjectValue: number) {
  selectedOpenSubjects.value = selectedOpenSubjects.value.filter(s => s.subject !== subjectValue)
}

async function loadSubjectOptions() {
  try {
    subjectOptions.value = await studentAdminClient.getSubjectOptions()
  } catch (error) {
    console.warn('Failed to load subject options:', error)
  }
}

async function loadStudentOpenSubjects(studentId: string) {
  loadingOpenSubjects.value = true
  try {
    selectedOpenSubjects.value = await studentAdminClient.getStudentOpenSubjects(studentId, false)
  } catch (error) {
    console.warn('Failed to load open subjects:', error)
    selectedOpenSubjects.value = []
  } finally {
    loadingOpenSubjects.value = false
  }
}

async function connectIdentity() {
  if (!identityCreds.appId || !identityCreds.appSecret) {
    ElMessage.warning('Please enter Identity AppId and AppSecret.')
    return
  }

  connectingIdentity.value = true
  try {
    const client = createIdentityAdminApiClient(identityCreds)
    await client.testConnection()
    identityClient.value = client
    identityConnected.value = true
    studentAdminClient.setIdentityCredentials({ appId: identityCreds.appId, appSecret: identityCreds.appSecret })
    saveCredentials(IDENTITY_STORAGE_KEY, identityCreds)
    ElMessage.success('Identity service connected.')
  } catch (error) {
    identityConnected.value = false
    identityClient.value = null
    studentAdminClient.clearIdentityCredentials()
    ElMessage.error(`Connection failed: ${getIdentityErrorMessage(error)}`)
  } finally {
    connectingIdentity.value = false
  }
}

async function handleClearAllCredentials() {
  try {
    await ElMessageBox.confirm(
      'Clear all saved credentials? You will need to re-enter them.',
      'Confirm Clear',
      { confirmButtonText: 'Confirm', cancelButtonText: 'Cancel', type: 'warning' },
    )
    localStorage.removeItem(IDENTITY_STORAGE_KEY)
    identityCreds.appId = ''
    identityCreds.appSecret = ''
    identityClient.value = null
    identityConnected.value = false
    ElMessage.success('All credentials cleared.')
  } catch {
    // cancelled
  }
}

function isPhoneLikeQuery(query: string): boolean {
  return /^[+\d\s\-()]+$/.test(query)
}

async function searchIdentityAccounts(query: string): Promise<IdentityUser[]> {
  if (!identityClient.value || !query.trim()) return []

  const trimmedQuery = query.trim()
  const requests = [
    identityClient.value.getUsers({
      username: trimmedQuery,
      page: 1,
      pageSize: 20,
    }),
  ]

  if (isPhoneLikeQuery(trimmedQuery)) {
    requests.push(
      identityClient.value.getUsers({
        phone: trimmedQuery,
        page: 1,
        pageSize: 20,
      }),
    )
  }

  try {
    const responses = await Promise.all(requests)
    const merged = new Map<string, IdentityUser>()
    for (const response of responses) {
      for (const user of response.items) {
        merged.set(user.userId, user)
      }
    }
    return [...merged.values()]
  } catch (error) {
    ElMessage.error(`Search failed: ${getIdentityErrorMessage(error)}`)
    return []
  }
}

async function handleCreateSearch() {
  searchLoading.value = true
  searchResults.value = await searchIdentityAccounts(accountSearch.value)
  searchLoading.value = false
}

async function handleEditSearch() {
  editStudentForm.searchLoading = true
  editStudentForm.searchResults = await searchIdentityAccounts(editStudentForm.accountSearch)
  editStudentForm.searchLoading = false
}

async function handleLinkSearch() {
  linkAccountForm.searchLoading = true
  linkAccountForm.searchResults = await searchIdentityAccounts(linkAccountForm.accountSearch)
  linkAccountForm.searchLoading = false
}

function addAccountToCreate(userId: string) {
  if (!createStudentForm.selectedAccountIds.includes(userId)) {
    createStudentForm.selectedAccountIds.push(userId)
  }
}

function removeAccountFromCreate(userId: string) {
  createStudentForm.selectedAccountIds = createStudentForm.selectedAccountIds.filter(id => id !== userId)
}

function addAccountToEdit(userId: string) {
  if (!editStudentForm.selectedAccountIds.includes(userId)) {
    editStudentForm.selectedAccountIds.push(userId)
  }

  const selectedUser = editStudentForm.searchResults.find(user => user.userId === userId)
  if (selectedUser) {
    cacheEditManagedAccount(selectedUser)
  }
}

function removeAccountFromEdit(userId: string) {
  editStudentForm.selectedAccountIds = editStudentForm.selectedAccountIds.filter(id => id !== userId)
  const next = new Map(editManagedAccountCache.value)
  next.delete(userId)
  editManagedAccountCache.value = next
}

async function loadStudents() {
  loadingStudents.value = true
  try {
    const response = await studentAdminClient.getStudents({
      name: studentFilters.name || undefined,
      page: studentPage.value,
      pageSize: studentPageSize.value,
    })
    students.value = response.items
    studentTotal.value = response.total
  } catch (error) {
    ElMessage.error(`Failed to load students: ${getStudentErrorMessage(error)}`)
  } finally {
    loadingStudents.value = false
  }
}

function cacheEditManagedAccount(user: IdentityUser) {
  const next = new Map(editManagedAccountCache.value)
  next.set(user.userId, user)
  editManagedAccountCache.value = next
}

async function loadEditManagedAccounts(studentId: string, accountIds: string[]) {
  editManagedAccountCache.value = new Map()
  editManagedAccountsLoading.value = false

  if (!identityConnected.value || accountIds.length === 0) {
    return
  }

  editManagedAccountsLoading.value = true
  try {
    if (!identityClient.value) return

    const accounts = await identityClient.value.getUsersByIds(accountIds)
    if (editStudentForm.studentId !== studentId) return

    const next = new Map<string, IdentityUser>()
    for (const account of accounts) {
      next.set(account.userId, account)
    }
    editManagedAccountCache.value = next
  } catch (error) {
    console.warn('Failed to load managed account names for edit dialog:', error)
  } finally {
    if (editStudentForm.studentId === studentId) {
      editManagedAccountsLoading.value = false
    }
  }
}

async function loadGradeOptions() {
  try {
    gradeOptions.value = await studentAdminClient.getGrades()
  } catch {
    // grades endpoint may not be available in dev
  }
}

async function handleCreateStudent() {
  if (!studentAdminClient) return
  if (!createStudentForm.name) {
    ElMessage.warning('Student name is required.')
    return
  }
  if (createStudentForm.selectedAccountIds.length === 0) {
    ElMessage.warning('At least one Identity account must be selected.')
    return
  }

  creatingStudent.value = true
  try {
    await studentAdminClient.createStudent({
      name: createStudentForm.name.trim(),
      grade: createStudentForm.grade,
      identityAccountIds: createStudentForm.selectedAccountIds,
    })
    ElMessage.success('Student created successfully.')
    createStudentForm.name = ''
    createStudentForm.grade = 1
    createStudentForm.selectedAccountIds = []
    accountSearch.value = ''
    searchResults.value = []
    await loadStudents()
  } catch (error) {
    ElMessage.error(`Failed to create student: ${getStudentErrorMessage(error)}`)
  } finally {
    creatingStudent.value = false
  }
}

async function openEditDialog(row: StudentDto) {
  editStudentForm.visible = true
  editStudentForm.studentId = row.id
  editStudentForm.name = row.name
  editStudentForm.grade = row.grade
  editStudentForm.selectedAccountIds = [...row.identityAccountIds]
  editStudentForm.accountSearch = ''
  editStudentForm.searchLoading = false
  editStudentForm.searchResults = []
  await loadEditManagedAccounts(row.id, editStudentForm.selectedAccountIds)
  await loadStudentOpenSubjects(row.id)
}

async function handleUpdateStudent() {
  if (!studentAdminClient) return
  if (!editStudentForm.name) {
    ElMessage.warning('Student name is required.')
    return
  }

  updatingStudent.value = true
  try {
    await studentAdminClient.updateStudent(editStudentForm.studentId, {
      name: editStudentForm.name.trim(),
      grade: editStudentForm.grade,
      identityAccountIds: editStudentForm.selectedAccountIds,
    })
    await studentAdminClient.setStudentOpenSubjects(editStudentForm.studentId, {
      subjects: selectedOpenSubjects.value.map(s => ({
        subject: s.subject,
        openStartDate: s.openStartDate,
        openEndDate: s.openEndDate,
      })),
    })
    ElMessage.success('Student updated successfully.')
    editStudentForm.visible = false
    editManagedAccountCache.value = new Map()
    await loadStudents()
  } catch (error) {
    ElMessage.error(`Failed to update student: ${getStudentErrorMessage(error)}`)
  } finally {
    updatingStudent.value = false
  }
}

async function handleDeleteStudent(row: StudentDto) {
  if (!studentAdminClient) return

  try {
    await ElMessageBox.confirm(
      `Are you sure you want to delete student "${row.name}"?`,
      'Confirm Delete',
      { confirmButtonText: 'Delete', cancelButtonText: 'Cancel', type: 'warning' },
    )

    deletingStudent.value = true
    await studentAdminClient.deleteStudent(row.id)
    ElMessage.success('Student deleted.')
    await loadStudents()
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') {
      ElMessage.error(`Failed to delete student: ${getStudentErrorMessage(error)}`)
    }
  } finally {
    deletingStudent.value = false
  }
}

function openLinkAccountDialog(row: StudentDto) {
  linkAccountForm.visible = true
  linkAccountForm.studentId = row.id
  linkAccountForm.studentName = row.name
  linkAccountForm.selectedAccountId = ''
  linkAccountForm.accountSearch = ''
  linkAccountForm.searchResults = []
}

async function handleLinkAccount() {
  if (!studentAdminClient) return
  if (!linkAccountForm.selectedAccountId) {
    ElMessage.warning('Please select an Identity account to link.')
    return
  }

  linkingAccount.value = true
  try {
    await studentAdminClient.linkIdentityAccount(linkAccountForm.studentId, linkAccountForm.selectedAccountId)
    ElMessage.success('Identity account linked successfully.')
    linkAccountForm.visible = false
    await loadStudents()
  } catch (error) {
    ElMessage.error(`Failed to link account: ${getStudentErrorMessage(error)}`)
  } finally {
    linkingAccount.value = false
  }
}

async function handleUnlinkAccount(studentId: string, accountId: string) {
  if (!studentAdminClient) return

  try {
    await ElMessageBox.confirm(
      'Are you sure you want to unlink this Identity account?',
      'Confirm Unlink',
      { confirmButtonText: 'Unlink', cancelButtonText: 'Cancel', type: 'warning' },
    )

    unlinkingAccountId.value = accountId
    await studentAdminClient.unlinkIdentityAccount(studentId, accountId)
    ElMessage.success('Identity account unlinked.')
    await loadStudents()
  } catch (error) {
    if (error !== 'cancel' && error !== 'close') {
      ElMessage.error(`Failed to unlink account: ${getStudentErrorMessage(error)}`)
    }
  } finally {
    unlinkingAccountId.value = null
  }
}

function getAccountLabel(accountId: string): string {
  const allUsers = [...searchResults.value, ...editStudentForm.searchResults, ...linkAccountForm.searchResults]
  const user = allUsers.find(u => u.userId === accountId)
  if (user) return formatAccountLabel(user)

  return accountId
}

function getEditManagedAccountLabel(accountId: string): string {
  const cachedUser = editManagedAccountCache.value.get(accountId)
  if (cachedUser) {
    return `${formatAccountLabel(cachedUser)} [${accountId}]`
  }

  const searchedUser = editStudentForm.searchResults.find(user => user.userId === accountId)
  if (searchedUser) {
    return `${formatAccountLabel(searchedUser)} [${accountId}]`
  }

  return accountId
}

function handleStudentPageChange(page: number) {
  studentPage.value = page
  void loadStudents()
}

function handleStudentPageSizeChange(size: number) {
  studentPageSize.value = size
  studentPage.value = 1
  void loadStudents()
}

onMounted(() => {
  const savedIdentity = loadSavedCredentials(IDENTITY_STORAGE_KEY)
  if (savedIdentity.appId && savedIdentity.appSecret) connectIdentity()
  void loadGradeOptions()
  void loadStudents()
  void loadSubjectOptions()
})
</script>

<template>
  <div class="app-shell">
    <!-- Header -->
    <header class="hero">
      <div class="hero-main">
        <div class="hero-icon">
          <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
        </div>
        <div>
          <h1>{{ appTitle }}</h1>
          <p class="hero-sub">Manage student profiles and bind them to Identity accounts</p>
        </div>
      </div>
      <div class="hero-actions">
        <el-button
          v-if="identityConnected"
          text
          size="small"
          @click="showConnectionPanel = !showConnectionPanel"
        >
          {{ showConnectionPanel ? 'Hide' : 'Show' }} Config
        </el-button>
        <el-tag :type="identityConnected ? 'success' : 'info'" class="status-tag" effect="light">
          <span class="status-dot" :class="{ connected: identityConnected }"></span>
          {{ identityConnected ? 'Connected' : 'Disconnected' }}
        </el-tag>
      </div>
    </header>

    <!-- Connection Panel -->
    <el-card v-if="showConnectionPanel || !identityConnected" shadow="never" class="panel connection-panel">
      <template #header>
        <div class="panel-header">
          <div class="panel-title">
            <el-icon><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M512 64a448 448 0 1 1 0 896 448 448 0 0 1 0-896zm0 64a384 384 0 1 0 0 768 384 384 0 0 0 0-768z"/></svg></el-icon>
            <span>Identity Authentication</span>
          </div>
          <div class="header-actions">
            <el-button type="danger" text size="small" @click="handleClearAllCredentials">Clear</el-button>
            <el-button type="primary" size="small" :loading="connectingIdentity" @click="connectIdentity">
              {{ identityConnected ? 'Reconnect' : 'Connect' }}
            </el-button>
          </div>
        </div>
      </template>

      <el-form class="connection-form" label-position="top">
        <el-form-item label="Admin AppId">
          <el-input v-model="identityCreds.appId" placeholder="Enter Identity AppId" size="small" />
        </el-form-item>
        <el-form-item label="Admin AppSecret">
          <el-input v-model="identityCreds.appSecret" show-password placeholder="Enter Identity AppSecret" size="small" />
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Main Content -->
    <el-card shadow="never" class="panel main-panel">
      <div class="split-layout">
        <!-- Create Student -->
        <el-card shadow="never" class="compact-card create-card">
          <template #header>
            <div class="panel-header">
              <div class="panel-title">
                <el-icon><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M480 480V128a32 32 0 0 1 64 0v352h352a32 32 0 1 1 0 64H544v352a32 32 0 1 1-64 0V544H128a32 32 0 0 1 0-64h352z"/></svg></el-icon>
                <span>Create Student</span>
              </div>
            </div>
          </template>
          <el-form class="compact-form" label-position="top">
            <el-form-item label="Student Name">
              <el-input v-model="createStudentForm.name" placeholder="Enter student name" size="small" />
            </el-form-item>
            <el-form-item label="Grade">
              <el-select v-model="createStudentForm.grade" placeholder="Select grade" size="small" style="width: 100%">
                <el-option
                  v-for="g in gradeOptions" :key="g.value"
                  :label="g.label" :value="g.value"
                />
              </el-select>
            </el-form-item>
            <el-form-item label="Link Identity Accounts">
              <div class="form-control-wrap">
                <div class="account-search-bar">
                  <el-input
                    v-model="accountSearch"
                    placeholder="Search by username or phone..."
                    size="small"
                    :disabled="!identityConnected"
                    @keyup.enter="handleCreateSearch"
                  />
                  <el-button
                    type="primary"
                    size="small"
                    :loading="searchLoading"
                    :disabled="!identityConnected || !accountSearch.trim()"
                    @click="handleCreateSearch"
                  >
                    Search
                  </el-button>
                </div>
                <div v-if="searchResults.length" class="search-results">
                  <div
                    v-for="user in searchResults" :key="user.userId"
                    class="search-result-item"
                    :class="{ selected: createStudentForm.selectedAccountIds.includes(user.userId) }"
                    @click="addAccountToCreate(user.userId)"
                  >
                    <div class="result-info">
                      <span class="result-name">{{ formatAccountLabel(user) }}</span>
                      <span class="result-id">{{ user.userId }}</span>
                    </div>
                    <el-icon v-if="createStudentForm.selectedAccountIds.includes(user.userId)" class="check-icon"><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M406.656 706.944l-195.2-195.2 60.330667-60.330667 134.869333 134.869334 300.8-300.8 60.330667 60.330666z"/></svg></el-icon>
                  </div>
                </div>
                <div v-if="createStudentForm.selectedAccountIds.length" class="selected-accounts">
                  <el-tag
                    v-for="accountId in createStudentForm.selectedAccountIds" :key="accountId"
                    closable size="small" type="info" effect="light"
                    @close="removeAccountFromCreate(accountId)"
                  >
                    {{ getAccountLabel(accountId) }}
                  </el-tag>
                </div>
                <div v-if="!identityConnected" class="hint-text">
                  Connect to Identity service to search accounts by username or phone.
                </div>
              </div>
            </el-form-item>
            <el-button
              type="primary" size="small" :loading="creatingStudent"
              class="submit-btn"
              @click="handleCreateStudent"
            >
              Create Student
            </el-button>
          </el-form>
        </el-card>

        <!-- Student List -->
        <el-card shadow="never" class="compact-card list-card">
          <template #header>
            <div class="panel-header">
              <div class="panel-title">
                <el-icon><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M128 224h768v64H128zM128 448h768v64H128zM128 672h768v64H128z"/></svg></el-icon>
                <span>Student List</span>
              </div>
              <el-button size="small" text @click="loadStudents">
                <el-icon class="refresh-icon" :class="{ spinning: loadingStudents }"><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M784.512 230.272v-50.56a32 32 0 1 1 64 0v149.056a32 32 0 0 1-32 32H667.52a32 32 0 1 1 0-64h92.992A362.24 362.24 0 0 0 512 149.824C296.32 149.824 121.216 325.056 121.216 540.8c0 215.68 175.104 390.848 390.784 390.848a390.208 390.208 0 0 0 338.304-194.752 32 32 0 1 1 55.36 32.256A454.144 454.144 0 0 1 512 963.648c-233.152 0-422.848-189.632-422.848-422.848S278.848 117.952 512 117.952c124.544 0 236.608 53.952 314.24 139.712l-41.728-27.392z"/></svg></el-icon>
                Refresh
              </el-button>
            </div>
          </template>

          <div class="filter-bar">
            <el-input v-model="studentFilters.name" placeholder="Search by name" clearable size="small" />
            <el-button type="primary" size="small" @click="loadStudents">Search</el-button>
          </div>

          <el-table :data="students" v-loading="loadingStudents" empty-text="No data" class="data-table" size="small">
            <el-table-column prop="name" label="Name" min-width="120" show-overflow-tooltip />
            <el-table-column label="Grade" width="90">
              <template #default="{ row }"><span class="grade-badge">{{ getGradeLabel(row.grade) }}</span></template>
            </el-table-column>
            <el-table-column label="Linked Accounts" min-width="180">
              <template #default="{ row }">
                <div class="account-tags">
                  <el-tag
                    v-for="accountId in row.identityAccountIds" :key="accountId" size="small" type="info" effect="light"
                  >
                    {{ accountId }}
                  </el-tag>
                  <span v-if="!row.identityAccountIds.length" class="empty-text">None</span>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="Created" min-width="140">
              <template #default="{ row }"><span class="time-text">{{ formatDate(row.createdAt) }}</span></template>
            </el-table-column>
            <el-table-column label="Actions" width="200" fixed="right">
              <template #default="{ row }">
                <div class="table-actions">
                  <el-button link type="primary" size="small" @click="openEditDialog(row)">Edit</el-button>
                  <el-button link type="warning" size="small" :disabled="!identityConnected" @click="openLinkAccountDialog(row)">Link</el-button>
                  <el-button link type="danger" size="small" :loading="deletingStudent" @click="handleDeleteStudent(row)">Delete</el-button>
                </div>
              </template>
            </el-table-column>
          </el-table>

          <div class="pagination-bar">
            <el-pagination
              background layout="total, sizes, prev, pager, next"
              :total="studentTotal" :page-size="studentPageSize" :current-page="studentPage"
              :page-sizes="[10, 20, 50, 100]"
              @current-change="handleStudentPageChange"
              @size-change="handleStudentPageSizeChange"
            />
          </div>
        </el-card>
      </div>
    </el-card>

    <!-- Edit Student Dialog -->
    <el-dialog v-model="editStudentForm.visible" title="Edit Student" width="640px" destroy-on-close class="modern-dialog">
      <div class="dialog-body">
        <div class="form-section">
          <div class="section-title">Basic Information</div>
          <el-form class="modern-form" label-position="top">
            <div class="form-row">
              <el-form-item label="Student Name" class="form-col">
                <el-input v-model="editStudentForm.name" size="default" placeholder="Enter student name" />
              </el-form-item>
              <el-form-item label="Grade" class="form-col">
                <el-select v-model="editStudentForm.grade" placeholder="Select grade" size="default" style="width: 100%">
                  <el-option
                    v-for="g in gradeOptions" :key="g.value"
                    :label="g.label" :value="g.value"
                  />
                </el-select>
              </el-form-item>
            </div>
          </el-form>
        </div>

        <div class="form-section">
          <div class="section-title">Linked Identity Accounts</div>
          <div class="form-control-wrap">
            <div class="account-search-bar">
              <el-input
                v-model="editStudentForm.accountSearch"
                placeholder="Search by username or phone..."
                size="default"
                :disabled="!identityConnected"
                @keyup.enter="handleEditSearch"
              />
              <el-button
                type="primary"
                size="default"
                :loading="editStudentForm.searchLoading"
                :disabled="!identityConnected || !editStudentForm.accountSearch.trim()"
                @click="handleEditSearch"
              >
                Search
              </el-button>
            </div>
            <div v-if="editStudentForm.searchResults.length" class="search-results">
              <div
                v-for="user in editStudentForm.searchResults" :key="user.userId"
                class="search-result-item"
                :class="{ selected: editStudentForm.selectedAccountIds.includes(user.userId) }"
                @click="addAccountToEdit(user.userId)"
              >
                <div class="result-info">
                  <span class="result-name">{{ formatAccountLabel(user) }}</span>
                  <span class="result-id">{{ user.userId }}</span>
                </div>
                <el-icon v-if="editStudentForm.selectedAccountIds.includes(user.userId)" class="check-icon"><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M406.656 706.944l-195.2-195.2 60.330667-60.330667 134.869333 134.869334 300.8-300.8 60.330667 60.330666z"/></svg></el-icon>
              </div>
            </div>
            <div v-if="editManagedAccountsLoading" class="hint-text">
              Loading usernames for linked accounts...
            </div>
            <div v-if="editStudentForm.selectedAccountIds.length" class="selected-accounts">
              <el-tag
                v-for="accountId in editStudentForm.selectedAccountIds" :key="accountId"
                closable size="small" type="info" effect="light"
                @close="removeAccountFromEdit(accountId)"
              >
                {{ getEditManagedAccountLabel(accountId) }}
              </el-tag>
            </div>
          </div>
        </div>

        <div class="form-section">
          <div class="section-title">Open Subjects</div>
          <div v-if="loadingOpenSubjects" class="hint-text">Loading...</div>
          <div v-else class="form-control-wrap">
            <div class="subjects-grid">
              <div
                v-for="subject in subjectOptions"
                :key="subject.value"
                class="subject-chip"
                :class="{ active: isSubjectSelected(subject.value) }"
                @click="toggleSubject(subject.value)"
              >
                <el-icon v-if="isSubjectSelected(subject.value)" class="chip-check"><svg viewBox="0 0 1024 1024" width="12" height="12"><path fill="currentColor" d="M406.656 706.944l-195.2-195.2 60.330667-60.330667 134.869333 134.869334 300.8-300.8 60.330667 60.330666z"/></svg></el-icon>
                {{ subject.displayName }}
              </div>
            </div>
            <div v-if="selectedOpenSubjects.length" class="subject-dates">
              <div class="subject-table-header">
                <span>Subject</span>
                <span>Start Date</span>
                <span>End Date</span>
                <span>Status</span>
                <span></span>
              </div>
              <div
                v-for="subject in selectedOpenSubjects"
                :key="subject.subject"
                class="subject-row"
              >
                <span class="subject-name">{{ getSubjectDisplayName(subject.subject) }}</span>
                <el-date-picker
                  v-model="subject.openStartDate"
                  type="date"
                  placeholder="Select date"
                  format="YYYY-MM-DD"
                  value-format="YYYY-MM-DD"
                  size="small"
                  style="width: 130px;"
                />
                <el-date-picker
                  v-model="subject.openEndDate"
                  type="date"
                  placeholder="Permanent"
                  format="YYYY-MM-DD"
                  value-format="YYYY-MM-DD"
                  size="small"
                  clearable
                  style="width: 130px;"
                />
                <el-tag :type="isSubjectActive(subject) ? 'success' : 'info'" size="small" effect="light">
                  {{ isSubjectActive(subject) ? 'Active' : 'Expired' }}
                </el-tag>
                <el-button link type="danger" size="small" @click="removeOpenSubject(subject.subject)">Remove</el-button>
              </div>
            </div>
            <div v-else class="hint-text">
              Click subject chips above to select open subjects, then set validity period
            </div>
          </div>
        </div>
      </div>
      <template #footer>
        <div class="dialog-footer">
          <el-button size="default" @click="editStudentForm.visible = false">Cancel</el-button>
          <el-button type="primary" size="default" :loading="updatingStudent" @click="handleUpdateStudent">Save Changes</el-button>
        </div>
      </template>
    </el-dialog>

    <!-- Link Account Dialog -->
    <el-dialog v-model="linkAccountForm.visible" :title="`Link Account: ${linkAccountForm.studentName}`" width="520px" destroy-on-close class="modern-dialog">
      <div class="dialog-body">
        <el-alert type="info" :closable="false" class="info-alert">
          Search for an Identity account by username or phone and select it to link with this student.
        </el-alert>
        <div class="form-section">
          <div class="section-title">Search Identity Account</div>
          <div class="form-control-wrap">
            <div class="account-search-bar">
              <el-input
                v-model="linkAccountForm.accountSearch"
                placeholder="Search by username or phone..."
                size="default"
                :disabled="!identityConnected"
                @keyup.enter="handleLinkSearch"
              />
              <el-button
                type="primary"
                size="default"
                :loading="linkAccountForm.searchLoading"
                :disabled="!identityConnected || !linkAccountForm.accountSearch.trim()"
                @click="handleLinkSearch"
              >
                Search
              </el-button>
            </div>
            <div v-if="linkAccountForm.searchResults.length" class="search-results">
              <el-radio-group v-model="linkAccountForm.selectedAccountId">
                <div
                  v-for="user in linkAccountForm.searchResults" :key="user.userId"
                  class="search-result-item radio-item"
                  :class="{ selected: linkAccountForm.selectedAccountId === user.userId }"
                >
                  <el-radio :value="user.userId" size="small">
                    <div class="result-info">
                      <span class="result-name">{{ formatAccountLabel(user) }}</span>
                      <span class="result-id">{{ user.userId }}</span>
                    </div>
                  </el-radio>
                </div>
              </el-radio-group>
            </div>
          </div>
        </div>
      </div>
      <template #footer>
        <div class="dialog-footer">
          <el-button size="default" @click="linkAccountForm.visible = false">Cancel</el-button>
          <el-button type="primary" size="default" :loading="linkingAccount" :disabled="!linkAccountForm.selectedAccountId" @click="handleLinkAccount">
            Link Account
          </el-button>
        </div>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
/* ========== App Shell ========== */
.app-shell {
  max-width: 1600px;
  margin: 0 auto;
  padding: 20px 24px 28px;
}

/* ========== Header ========== */
.hero {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
  padding: 14px 20px;
  background: #ffffff;
  border-radius: 10px;
  border: 1px solid #e5e7eb;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
}

.hero-main {
  display: flex;
  align-items: center;
  gap: 14px;
}

.hero-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 40px;
  height: 40px;
  background: linear-gradient(135deg, #3b82f6 0%, #2563eb 100%);
  border-radius: 10px;
  color: #ffffff;
  flex-shrink: 0;
}

.hero h1 {
  margin: 0;
  font-size: 17px;
  font-weight: 700;
  color: #111827;
  line-height: 1.2;
  letter-spacing: -0.2px;
}

.hero-sub {
  margin: 2px 0 0;
  font-size: 12.5px;
  color: #6b7280;
  line-height: 1.4;
}

.hero-actions {
  display: flex;
  align-items: center;
  gap: 10px;
}

.status-tag {
  font-size: 12px;
  height: 26px;
  padding: 0 10px;
  border-radius: 20px;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-weight: 500;
}

.status-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #9ca3af;
  display: inline-block;
}

.status-dot.connected {
  background: #22c55e;
  box-shadow: 0 0 0 2px rgba(34, 197, 94, 0.2);
}

/* ========== Panels ========== */
.panel {
  margin-bottom: 16px;
  border-radius: 10px;
}

.panel:deep(.el-card__header) {
  padding: 12px 18px;
  border-bottom: 1px solid #f3f4f6;
}

.panel:deep(.el-card__body) {
  padding: 16px 18px;
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.panel-title {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  font-size: 14px;
  color: #1f2937;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

/* ========== Connection Panel ========== */
.connection-panel :deep(.el-card__body) {
  padding: 14px 18px;
}

.connection-form {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  align-items: flex-end;
}

.connection-form .el-form-item {
  margin-bottom: 0;
}

.connection-form .el-form-item__label {
  padding-bottom: 6px;
  font-size: 12.5px;
  font-weight: 500;
  color: #4b5563;
  line-height: 1;
}

/* ========== Main Layout ========== */
.split-layout {
  display: grid;
  grid-template-columns: 360px minmax(0, 1fr);
  gap: 16px;
}

/* ========== Cards ========== */
.compact-card {
  border-radius: 10px;
}

.compact-card :deep(.el-card__header) {
  padding: 12px 16px;
  font-size: 14px;
  border-bottom: 1px solid #f3f4f6;
  background: #fafafa;
  border-radius: 10px 10px 0 0;
}

.compact-card :deep(.el-card__body) {
  padding: 16px;
}

.create-card :deep(.el-card__body) {
  padding-bottom: 20px;
}

/* ========== Form ========== */
.compact-form .el-form-item {
  margin-bottom: 14px;
}

.compact-form .el-form-item__label {
  padding-bottom: 6px;
  font-size: 12.5px;
  font-weight: 500;
  color: #4b5563;
  line-height: 1;
}

.compact-form .el-input__wrapper,
.compact-form .el-select .el-input__wrapper {
  padding: 0 10px;
  min-height: 32px;
  border-radius: 6px;
}

.compact-form .el-input__inner {
  font-size: 13px;
}

.form-control-wrap {
  width: 100%;
}

.submit-btn {
  width: 100%;
  margin-top: 4px;
}

/* ========== Modern Dialog Form ========== */
.modern-dialog :deep(.el-dialog__header) {
  padding: 16px 24px;
  margin-right: 0;
  border-bottom: 1px solid #f3f4f6;
}

.modern-dialog :deep(.el-dialog__title) {
  font-size: 15px;
  font-weight: 700;
  color: #111827;
}

.modern-dialog :deep(.el-dialog__body) {
  padding: 0;
}

.modern-dialog :deep(.el-dialog__footer) {
  padding: 14px 24px;
  border-top: 1px solid #f3f4f6;
}

.dialog-body {
  padding: 20px 24px;
  max-height: 70vh;
  overflow-y: auto;
}

.dialog-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
}

.form-section {
  margin-bottom: 24px;
}

.form-section:last-child {
  margin-bottom: 0;
}

.section-title {
  font-size: 13px;
  font-weight: 600;
  color: #374151;
  margin-bottom: 12px;
  padding-bottom: 8px;
  border-bottom: 1px solid #f3f4f6;
  display: flex;
  align-items: center;
  gap: 6px;
}

.modern-form .el-form-item {
  margin-bottom: 0;
}

.modern-form .el-form-item__label {
  padding-bottom: 6px;
  font-size: 12.5px;
  font-weight: 500;
  color: #4b5563;
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

/* ========== Table ========== */
.data-table :deep(.el-table__cell) {
  padding: 8px 0 !important;
}

.data-table :deep(th.el-table__cell) {
  padding: 10px 0 !important;
  font-size: 12px;
  font-weight: 600;
  color: #4b5563;
  background: #f9fafb;
}

.data-table :deep(.cell) {
  font-size: 13px;
  line-height: 1.5;
  padding-left: 12px;
  padding-right: 12px;
}

.grade-badge {
  display: inline-flex;
  align-items: center;
  padding: 2px 10px;
  background: #eff6ff;
  color: #2563eb;
  border-radius: 20px;
  font-size: 12px;
  font-weight: 500;
}

.time-text {
  color: #6b7280;
  font-size: 12.5px;
}

.empty-text {
  color: #d1d5db;
  font-size: 12px;
}

/* ========== Filter Bar ========== */
.filter-bar {
  display: flex;
  gap: 10px;
  margin-bottom: 12px;
  align-items: center;
}

.filter-bar .el-input {
  width: 220px;
}

.filter-bar .el-input__wrapper {
  padding: 0 10px;
  min-height: 30px;
  border-radius: 6px;
}

/* ========== Table Actions ========== */
.table-actions {
  display: flex;
  gap: 6px;
  align-items: center;
}

.table-actions .el-button {
  padding: 2px 8px;
  height: auto;
  font-size: 12.5px;
}

/* ========== Pagination ========== */
.pagination-bar {
  display: flex;
  justify-content: flex-end;
  margin-top: 12px;
  padding-top: 10px;
  border-top: 1px solid #f3f4f6;
}

/* ========== Account Tags ========== */
.account-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.account-tags .el-tag {
  font-size: 11px;
  padding: 0 6px;
  height: 22px;
  border-radius: 4px;
}

/* ========== Search Results ========== */
.account-search-bar {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
}

.account-search-bar .el-input__wrapper {
  padding: 0 10px;
  min-height: 32px;
  border-radius: 6px;
}

.search-results {
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  max-height: 180px;
  overflow-y: auto;
  margin-bottom: 8px;
  background: #ffffff;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);
}

.search-result-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  cursor: pointer;
  transition: all 0.15s ease;
  font-size: 12.5px;
  border-bottom: 1px solid #f3f4f6;
}

.search-result-item:last-child {
  border-bottom: none;
}

.search-result-item:hover {
  background-color: #f9fafb;
}

.search-result-item.selected {
  background-color: #eff6ff;
}

.result-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.result-name {
  font-weight: 500;
  color: #1f2937;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.result-id {
  font-size: 11px;
  color: #9ca3af;
  font-family: 'Courier New', monospace;
}

.check-icon {
  color: #3b82f6;
  flex-shrink: 0;
}

.radio-item :deep(.el-radio) {
  width: 100%;
  margin-right: 0;
  align-items: center;
}

.radio-item :deep(.el-radio__label) {
  flex: 1;
  min-width: 0;
  padding-left: 8px;
}

/* ========== Selected Accounts ========== */
.selected-accounts {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 6px;
}

.selected-accounts .el-tag {
  font-size: 11.5px;
  padding: 0 6px;
  height: 24px;
  border-radius: 4px;
}

/* ========== Hints ========== */
.hint-text {
  color: #9ca3af;
  font-size: 12px;
  margin-top: 6px;
  line-height: 1.4;
}

.info-alert {
  margin-bottom: 16px;
  padding: 10px 14px;
  font-size: 12.5px;
  border-radius: 8px;
}

/* ========== Subjects ========== */
.subjects-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.subject-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 6px 14px;
  border: 1.5px solid #e5e7eb;
  border-radius: 20px;
  cursor: pointer;
  transition: all 0.2s ease;
  font-size: 13px;
  font-weight: 500;
  color: #4b5563;
  background: #ffffff;
  user-select: none;
}

.subject-chip:hover {
  border-color: #3b82f6;
  color: #3b82f6;
  background: #eff6ff;
}

.subject-chip.active {
  background: #3b82f6;
  color: #ffffff;
  border-color: #3b82f6;
}

.chip-check {
  font-size: 12px;
}

.subject-dates {
  margin-top: 16px;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  overflow: hidden;
}

.subject-table-header {
  display: grid;
  grid-template-columns: 90px 140px 140px 70px 70px;
  gap: 8px;
  padding: 8px 12px;
  background: #f9fafb;
  font-size: 11.5px;
  font-weight: 600;
  color: #4b5563;
  border-bottom: 1px solid #f3f4f6;
}

.subject-row {
  display: grid;
  grid-template-columns: 90px 140px 140px 70px 70px;
  gap: 8px;
  padding: 8px 12px;
  align-items: center;
  border-bottom: 1px solid #f3f4f6;
  font-size: 12.5px;
}

.subject-row:last-child {
  border-bottom: none;
}

.subject-name {
  font-weight: 500;
  color: #1f2937;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* ========== Refresh Icon ========== */
.refresh-icon {
  transition: transform 0.3s ease;
}

.refresh-icon.spinning {
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* ========== Responsive ========== */
@media (max-width: 1200px) {
  .split-layout {
    grid-template-columns: 320px minmax(0, 1fr);
  }
}

@media (max-width: 992px) {
  .split-layout {
    grid-template-columns: 1fr;
  }

  .connection-form {
    grid-template-columns: 1fr 1fr;
  }
}

@media (max-width: 768px) {
  .app-shell {
    padding: 12px;
  }

  .hero {
    flex-direction: column;
    align-items: flex-start;
    gap: 10px;
  }

  .connection-form {
    grid-template-columns: 1fr;
  }

  .filter-bar {
    flex-wrap: wrap;
  }

  .filter-bar .el-input {
    width: 100%;
  }

  .pagination-bar {
    justify-content: center;
  }

  .form-row {
    grid-template-columns: 1fr;
  }

  .subject-table-header,
  .subject-row {
    grid-template-columns: 80px 120px 120px 60px 60px;
  }
}
</style>
