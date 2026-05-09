<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  getIdentityAdminApiClient,
  getIdentityErrorMessage,
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
import {
  teacherPortalClient,
  getTeacherPortalErrorMessage,
  type TeacherAccountDto,
  type SubjectOption,
} from './services/teacherPortalApi'

const appTitle = (window as any).__APP_TITLE__ || 'Student Management Console'

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

const loadingStudents = ref(false)
const creatingStudent = ref(false)
const updatingStudent = ref(false)

const students = ref<StudentDto[]>([])
const gradeOptions = ref<GradeOption[]>([])
const studentTotal = ref(0)
const studentPage = ref(1)
const studentPageSize = ref(20)

const identityClient = getIdentityAdminApiClient()
const editManagedAccountCache = ref<Map<string, IdentityUser>>(new Map())
const listAccountCache = ref<Map<string, IdentityUser>>(new Map())
const loadingListAccounts = ref(false)

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

function isPhoneLikeQuery(query: string): boolean {
  return /^[+\d\s\-()]+$/.test(query)
}

async function searchIdentityAccounts(query: string): Promise<IdentityUser[]> {
  if (!query.trim()) return []

  const trimmedQuery = query.trim()
  const requests = [
    identityClient.getUsers({
      username: trimmedQuery,
      page: 1,
      pageSize: 20,
    }),
  ]

  if (isPhoneLikeQuery(trimmedQuery)) {
    requests.push(
      identityClient.getUsers({
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
    void loadListAccountNames(response.items)
  } catch (error) {
    ElMessage.error(`Failed to load students: ${getStudentErrorMessage(error)}`)
  } finally {
    loadingStudents.value = false
  }
}

async function loadListAccountNames(items: StudentDto[]) {
  const allAccountIds = new Set<string>()
  for (const item of items) {
    for (const id of item.identityAccountIds) {
      if (!listAccountCache.value.has(id)) {
        allAccountIds.add(id)
      }
    }
  }

  if (allAccountIds.size === 0) return

  loadingListAccounts.value = true
  try {
    const accounts = await identityClient.getUsersByIds([...allAccountIds])
    const next = new Map(listAccountCache.value)
    for (const account of accounts) {
      next.set(account.userId, account)
    }
    listAccountCache.value = next
  } catch (error) {
    console.warn('Failed to load account names for student list:', error)
  } finally {
    loadingListAccounts.value = false
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

  if (accountIds.length === 0) {
    return
  }

  editManagedAccountsLoading.value = true
  try {
    const accounts = await identityClient.getUsersByIds(accountIds)
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

function getAccountLabel(accountId: string): string {
  const allUsers = [...searchResults.value, ...editStudentForm.searchResults]
  const user = allUsers.find(u => u.userId === accountId)
  if (user) return formatAccountLabel(user)

  return accountId
}

function getAccountNamesPreview(row: StudentDto): string {
  if (!row.identityAccountIds.length) return ''

  const names: string[] = []
  for (const accountId of row.identityAccountIds.slice(0, 2)) {
    const user = listAccountCache.value.get(accountId) ?? editManagedAccountCache.value.get(accountId)
    if (user) {
      names.push(formatAccountLabel(user))
    }
  }

  if (names.length === 0) {
    return row.identityAccountIds[0].slice(0, 8) + '...'
  }

  if (row.identityAccountIds.length > names.length) {
    return `${names[0]}, +${row.identityAccountIds.length - 1}`
  }

  return names.join(', ')
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

const teacherSearch = ref('')
const teacherSearchLoading = ref(false)
const teacherSearchResults = ref<IdentityUser[]>([])
const teachers = ref<TeacherAccountDto[]>([])
const loadingTeachers = ref(false)
const availableSubjects = ref<SubjectOption[]>([])
const editingTeacherSubject = ref(0)
const editingTeacherId = ref<string | null>(null)
const editingTeacherName = ref<string>('')
const showSubjectsDialog = ref(false)
const savingSubjects = ref(false)
const grantSubjectDialog = ref(false)
const grantSubjectUserId = ref('')
const grantSubjectUserName = ref('')
const grantSubjectValue = ref(0)
const grantingTeacherLoading = ref(false)
const teacherAccountCache = ref<Map<string, IdentityUser>>(new Map())

async function loadTeachers() {
  loadingTeachers.value = true
  try {
    const result = await teacherPortalClient.getTeachers()
    if (result.success && result.data) {
      teachers.value = result.data
      void loadTeacherAccountNames(result.data)
    }
  } catch (error) {
    console.warn('Failed to load teachers:', error)
  } finally {
    loadingTeachers.value = false
  }
}

async function loadTeacherAccountNames(teachers: TeacherAccountDto[]) {
  const missingIds = new Set<string>()
  for (const t of teachers) {
    if (t.userId && !teacherAccountCache.value.has(t.userId)) {
      missingIds.add(t.userId)
    }
  }
  if (missingIds.size === 0) return

  try {
    const accounts = await identityClient.getUsersByIds([...missingIds])
    const next = new Map(teacherAccountCache.value)
    for (const account of accounts) {
      next.set(account.userId, account)
    }
    teacherAccountCache.value = next
  } catch (error) {
    console.warn('Failed to load teacher account names:', error)
  }
}

async function handleTeacherSearch() {
  if (!teacherSearch.value.trim()) return
  teacherSearchLoading.value = true
  teacherSearchResults.value = await searchIdentityAccounts(teacherSearch.value)
  teacherSearchLoading.value = false
}

async function grantTeacherPermission(user: IdentityUser) {
  grantSubjectUserId.value = user.userId
  grantSubjectUserName.value = formatAccountLabel(user)
  grantSubjectValue.value = 0
  grantSubjectDialog.value = true
}

async function confirmGrantTeacher() {
  if (grantSubjectValue.value < 1) {
    ElMessage.warning('请选择科目后再授予权限')
    return
  }
  grantingTeacherLoading.value = true
  try {
    const result = await teacherPortalClient.grantTeacher(grantSubjectUserId.value, {
      subject: grantSubjectValue.value,
    })
    if (result.success) {
      ElMessage.success(`已为 ${grantSubjectUserName.value} 开通教师权限`)
      grantSubjectDialog.value = false
      await loadTeachers()
    } else {
      ElMessage.warning(result.message || '操作失败')
    }
  } catch (error) {
    ElMessage.error(`操作失败: ${getTeacherPortalErrorMessage(error)}`)
  } finally {
    grantingTeacherLoading.value = false
  }
}

async function revokeTeacherPermission(teacher: TeacherAccountDto) {
  const label = teacher.username || teacher.phone || teacher.userId || String(teacher.id)
  try {
    await ElMessageBox.confirm(`确定要撤销 ${label} 的教师权限吗？`, '撤销教师权限', {
      confirmButtonText: '确定',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }

  const userId = teacher.userId
  if (!userId) return

  try {
    const result = await teacherPortalClient.removeTeacherByUserId(userId)
    if (result.success) {
      ElMessage.success('已撤销教师权限')
      await loadTeachers()
    } else {
      ElMessage.warning(result.message || '操作失败')
    }
  } catch (error) {
    ElMessage.error(`操作失败: ${getTeacherPortalErrorMessage(error)}`)
  }
}

function isUserTeacher(userId: string): boolean {
  return teachers.value.some(t => t.userId === userId)
}

function getTeacherAccountLabel(teacher: TeacherAccountDto): string {
  if (teacher.userId && teacherAccountCache.value.has(teacher.userId)) {
    return formatAccountLabel(teacherAccountCache.value.get(teacher.userId)!)
  }
  return teacher.username || teacher.phone || teacher.userId || String(teacher.id)
}

async function loadAvailableSubjects() {
  try {
    const result = await teacherPortalClient.getAvailableSubjects()
    if (result.success && result.data) {
      availableSubjects.value = result.data
    }
  } catch (error) {
    console.warn('Failed to load available subjects:', error)
  }
}

async function openSubjectsDialog(teacher: TeacherAccountDto) {
  editingTeacherId.value = teacher.userId
  editingTeacherName.value = getTeacherAccountLabel(teacher)
  if (teacher.userId) {
    try {
      const result = await teacherPortalClient.getTeacherSubject(teacher.userId)
      editingTeacherSubject.value = result.success ? result.data : 0
    } catch {
      editingTeacherSubject.value = teacher.subject ?? 0
    }
  } else {
    editingTeacherSubject.value = teacher.subject ?? 0
  }
  showSubjectsDialog.value = true
}

async function saveTeacherSubjects() {
  if (!editingTeacherId.value) return
  if (editingTeacherSubject.value <= 0) {
    ElMessage.warning('请选择一个科目')
    return
  }
  savingSubjects.value = true
  try {
    const result = await teacherPortalClient.setTeacherSubject(editingTeacherId.value, editingTeacherSubject.value)
    if (result.success) {
      ElMessage.success('科目设置已保存')
      showSubjectsDialog.value = false
      await loadTeachers()
    } else {
      ElMessage.warning(result.message || '保存失败')
    }
  } catch (error) {
    ElMessage.error(`保存失败: ${getTeacherPortalErrorMessage(error)}`)
  } finally {
    savingSubjects.value = false
  }
}

function getSubjectName(subjectValue: number): string {
  const subject = availableSubjects.value.find(s => s.value === subjectValue)
  return subject?.name ?? String(subjectValue)
}

onMounted(() => {
  void loadGradeOptions()
  void loadStudents()
  void loadSubjectOptions()
  void loadTeachers()
  void loadAvailableSubjects()
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
    </header>

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
                    @keyup.enter="handleCreateSearch"
                  />
                  <el-button
                    type="primary"
                    size="small"
                    :loading="searchLoading"
                    :disabled="!accountSearch.trim()"
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
            <el-table-column label="Grade" width="100">
              <template #default="{ row }"><span class="grade-text">{{ getGradeLabel(row.grade) }}</span></template>
            </el-table-column>
            <el-table-column label="Linked Accounts" min-width="160">
              <template #default="{ row }">
                <div class="account-summary">
                  <span class="account-count" :class="{ zero: !row.identityAccountIds.length }">
                    {{ row.identityAccountIds.length }}
                  </span>
                  <span v-if="row.identityAccountIds.length" class="account-names">
                    {{ getAccountNamesPreview(row) }}
                  </span>
                  <span v-else class="empty-text">Not linked</span>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="Created" min-width="140">
              <template #default="{ row }"><span class="time-text">{{ formatDate(row.createdAt) }}</span></template>
            </el-table-column>
            <el-table-column label="Actions" width="80" fixed="right">
              <template #default="{ row }">
                <div class="table-actions">
                  <el-button link type="primary" size="small" @click="openEditDialog(row)">Edit</el-button>
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

    <!-- Teacher Permission Management -->
    <el-card shadow="never" class="panel teacher-panel">
      <template #header>
        <div class="panel-header">
          <div class="panel-title">
            <el-icon><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg></el-icon>
            <span>Teacher Permissions</span>
          </div>
          <el-button size="small" text @click="loadTeachers">
            <el-icon class="refresh-icon" :class="{ spinning: loadingTeachers }"><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M784.512 230.272v-50.56a32 32 0 1 1 64 0v149.056a32 32 0 0 1-32 32H667.52a32 32 0 1 1 0-64h92.992A362.24 362.24 0 0 0 512 149.824C296.32 149.824 121.216 325.056 121.216 540.8c0 215.68 175.104 390.848 390.784 390.848a390.208 390.208 0 0 0 338.304-194.752 32 32 0 1 1 55.36 32.256A454.144 454.144 0 0 1 512 963.648c-233.152 0-422.848-189.632-422.848-422.848S278.848 117.952 512 117.952c124.544 0 236.608 53.952 314.24 139.712l-41.728-27.392z"/></svg></el-icon>
            Refresh
          </el-button>
        </div>
      </template>

      <div class="teacher-layout">
        <div class="teacher-grant-section">
          <div class="section-label">Grant Teacher Permission</div>
          <div class="account-search-bar">
            <el-input
              v-model="teacherSearch"
              placeholder="Search by username or phone..."
              size="small"
              @keyup.enter="handleTeacherSearch"
            />
            <el-button
              type="primary"
              size="small"
              :loading="teacherSearchLoading"
              :disabled="!teacherSearch.trim()"
              @click="handleTeacherSearch"
            >
              Search
            </el-button>
          </div>
          <div v-if="teacherSearchResults.length" class="search-results">
            <div
              v-for="user in teacherSearchResults" :key="user.userId"
              class="search-result-item"
            >
              <div class="result-info">
                <span class="result-name">{{ formatAccountLabel(user) }}</span>
                <span class="result-id">{{ user.userId }}</span>
              </div>
              <el-button
                v-if="isUserTeacher(user.userId)"
                type="info"
                size="small"
                disabled
              >
                Already Teacher
              </el-button>
              <el-button
                v-else
                type="primary"
                size="small"
                @click="grantTeacherPermission(user)"
              >
                Grant
              </el-button>
            </div>
          </div>
        </div>

        <div class="teacher-list-section">
          <div class="section-label">Teacher Accounts ({{ teachers.length }})</div>
          <el-table :data="teachers" v-loading="loadingTeachers" empty-text="No teachers" class="data-table" size="small">
            <el-table-column label="Account" min-width="140">
              <template #default="{ row }">
                <span class="teacher-name">{{ getTeacherAccountLabel(row) }}</span>
              </template>
            </el-table-column>
            <el-table-column prop="phone" label="Phone" width="120">
              <template #default="{ row }">
                <span class="time-text">{{ row.phone || '-' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="Subject" min-width="120">
              <template #default="{ row }">
                <el-tag v-if="row.subject > 0" size="small" type="info" effect="plain" class="subject-tag">
                  {{ getSubjectName(row.subject) }}
                </el-tag>
                <span v-else class="empty-text">未分配</span>
              </template>
            </el-table-column>
            <el-table-column label="Actions" width="140" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" size="small" @click="openSubjectsDialog(row)">管理科目</el-button>
                <el-button link type="danger" size="small" @click="revokeTeacherPermission(row)">Revoke</el-button>
              </template>
            </el-table-column>
          </el-table>
        </div>
      </div>
    </el-card>

    <!-- Edit Student Dialog -->
    <el-dialog v-model="editStudentForm.visible" title="Edit Student" width="640px" destroy-on-close class="modern-dialog">
      <div class="dialog-body">
        <div class="form-section">
          <div class="section-title">
            <el-icon><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M512 64a448 448 0 1 1 0 896 448 448 0 0 1 0-896zm0 64a384 384 0 1 0 0 768 384 384 0 0 0 0-768z"/></svg></el-icon>
            Basic Information
          </div>
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
          <div class="section-title">
            <el-icon><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M480 480V128a32 32 0 0 1 64 0v352h352a32 32 0 1 1 0 64H544v352a32 32 0 1 1-64 0V544H128a32 32 0 0 1 0-64h352z"/></svg></el-icon>
            Linked Identity Accounts
          </div>
          <div class="form-control-wrap">
            <div class="account-search-bar">
              <el-input
                v-model="editStudentForm.accountSearch"
                placeholder="Search by username or phone..."
                size="default"
                @keyup.enter="handleEditSearch"
              />
              <el-button
                type="primary"
                size="default"
                :loading="editStudentForm.searchLoading"
                :disabled="!editStudentForm.accountSearch.trim()"
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
          <div class="section-title">
            <el-icon><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M128 224h768v64H128zM128 448h768v64H128zM128 672h768v64H128z"/></svg></el-icon>
            Open Subjects
          </div>
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

    <!-- Edit Teacher Subjects Dialog -->
    <el-dialog v-model="showSubjectsDialog" title="管理教师科目" width="480px" destroy-on-close class="modern-dialog">
      <div class="dialog-body">
        <div class="form-section">
          <div class="section-title">
            <el-icon><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M512 64a448 448 0 1 1 0 896 448 448 0 0 1 0-896zm0 64a384 384 0 1 0 0 768 384 384 0 0 0 0-768z"/></svg></el-icon>
            教师信息
          </div>
          <div class="teacher-info-row">
            <span class="info-label">教师：</span>
            <span class="info-value">{{ editingTeacherName }}</span>
          </div>
        </div>
        <div class="form-section">
          <div class="section-title">
            <el-icon><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M512 64a448 448 0 1 1 0 896 448 448 0 0 1 0-896zm0 64a384 384 0 1 0 0 768 384 384 0 0 0 0-768z"/></svg></el-icon>
            科目设置
          </div>
          <p class="hint-text">选择该教师负责的科目（单选）</p>
          <div class="subject-select">
            <el-select v-model="editingTeacherSubject" placeholder="请选择科目" style="width: 100%">
              <el-option
                v-for="subject in availableSubjects"
                :key="subject.value"
                :label="subject.name"
                :value="subject.value"
              />
            </el-select>
          </div>
          <div v-if="editingTeacherSubject > 0" class="selected-subject-preview">
            <span class="info-label">已选：</span>
            <el-tag size="small" type="primary" effect="light" class="subject-tag">
              {{ getSubjectName(editingTeacherSubject) }}
            </el-tag>
          </div>
        </div>
      </div>
      <template #footer>
        <div class="dialog-footer">
          <el-button size="default" @click="showSubjectsDialog = false">取消</el-button>
          <el-button type="primary" size="default" :loading="savingSubjects" @click="saveTeacherSubjects">保存</el-button>
        </div>
      </template>
    </el-dialog>

    <!-- Grant Teacher with Subject Dialog -->
    <el-dialog v-model="grantSubjectDialog" title="授予教师权限" width="480px" destroy-on-close class="modern-dialog">
      <div class="dialog-body">
        <div class="form-section">
          <div class="section-title">
            <el-icon><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M512 64a448 448 0 1 1 0 896 448 448 0 0 1 0-896zm0 64a384 384 0 1 0 0 768 384 384 0 0 0 0-768z"/></svg></el-icon>
            用户信息
          </div>
          <div class="teacher-info-row">
            <span class="info-label">用户：</span>
            <span class="info-value">{{ grantSubjectUserName }}</span>
          </div>
        </div>
        <div class="form-section">
          <div class="section-title">
            <el-icon><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M512 64a448 448 0 1 1 0 896 448 448 0 0 1 0-896zm0 64a384 384 0 1 0 0 768 384 384 0 0 0 0-768z"/></svg></el-icon>
            选择科目
          </div>
          <p class="hint-text">请选择该教师负责的科目（必选）</p>
          <div class="subject-select">
            <el-select v-model="grantSubjectValue" placeholder="请选择科目" style="width: 100%">
              <el-option
                v-for="subject in availableSubjects"
                :key="subject.value"
                :label="subject.name"
                :value="subject.value"
              />
            </el-select>
          </div>
          <div v-if="grantSubjectValue > 0" class="selected-subject-preview">
            <span class="info-label">已选：</span>
            <el-tag size="small" type="primary" effect="light" class="subject-tag">
              {{ getSubjectName(grantSubjectValue) }}
            </el-tag>
          </div>
        </div>
      </div>
      <template #footer>
        <div class="dialog-footer">
          <el-button size="default" @click="grantSubjectDialog = false">取消</el-button>
          <el-button type="primary" size="default" :loading="grantingTeacherLoading" :disabled="grantSubjectValue < 1" @click="confirmGrantTeacher">确认授权</el-button>
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

/* ========== Main Layout ========== */
.split-layout {
  display: grid;
  grid-template-columns: 360px minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}

/* ========== Cards ========== */
.compact-card {
  border-radius: 10px;
  display: flex;
  flex-direction: column;
}

.compact-card :deep(.el-card__header) {
  padding: 12px 16px;
  font-size: 14px;
  border-bottom: 1px solid #f3f4f6;
  background: #fafafa;
  border-radius: 10px 10px 0 0;
  flex-shrink: 0;
}

.compact-card :deep(.el-card__body) {
  padding: 16px;
  flex: 1;
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

.grade-text {
  font-size: 12.5px;
  color: #4b5563;
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

/* ========== Account Summary ========== */
.account-summary {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 12.5px;
  color: #4b5563;
}

.account-count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 20px;
  height: 20px;
  padding: 0 6px;
  border-radius: 10px;
  background: #f3f4f6;
  color: #6b7280;
  font-size: 11px;
  font-weight: 600;
}

.account-count.zero {
  background: #fef2f2;
  color: #f87171;
}

.account-names {
  color: #6b7280;
  max-width: 140px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
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

/* ========== Teacher Panel ========== */
.teacher-panel {
  margin-top: 16px;
}

.teacher-layout {
  display: grid;
  grid-template-columns: 360px minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}

.teacher-grant-section {
  padding: 4px 0;
}

.section-label {
  font-size: 12.5px;
  font-weight: 600;
  color: #4b5563;
  margin-bottom: 10px;
}

.teacher-list-section {
  min-width: 0;
}

.teacher-name {
  font-size: 13px;
  font-weight: 500;
  color: #1f2937;
}

/* ========== Teacher Subjects ========== */
.subjects-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  align-items: center;
}

.subject-tag {
  margin: 0;
}

.subject-select {
  margin: 12px 0;
}

.selected-subject-preview {
  margin-top: 12px;
  padding-top: 12px;
  border-top: 1px dashed #e5e7eb;
  display: flex;
  align-items: center;
  gap: 8px;
}

.teacher-info-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: #f9fafb;
  border-radius: 6px;
}

.info-label {
  color: #6b7280;
  font-size: 13px;
  flex-shrink: 0;
}

.info-value {
  color: #1f2937;
  font-size: 13px;
  font-weight: 500;
}

@media (max-width: 992px) {
  .teacher-layout {
    grid-template-columns: 1fr;
  }
}
</style>
