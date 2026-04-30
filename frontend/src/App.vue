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
const usernameSearch = ref('')
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
  usernameSearch: '',
  searchLoading: false,
  searchResults: [] as IdentityUser[],
})

const linkAccountForm = reactive({
  visible: false,
  studentId: '',
  studentName: '',
  usernameSearch: '',
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
  if (user.username && user.remark) return `${user.username}(${user.remark})`
  if (user.username) return user.username
  if (user.phone && user.remark) return `${user.phone}(${user.remark})`
  return user.phone || user.userId.substring(0, 8) + '...'
}

function formatDate(timestamp?: number | null) {
  if (!timestamp) return '-'
  return new Date(timestamp * 1000).toLocaleString()
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
    saveCredentials(IDENTITY_STORAGE_KEY, identityCreds)
    ElMessage.success('Identity service connected.')
  } catch (error) {
    identityConnected.value = false
    identityClient.value = null
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

async function searchUsersByUsername(query: string): Promise<IdentityUser[]> {
  if (!identityClient.value || !query.trim()) return []

  try {
    const response = await identityClient.value.getUsers({
      username: query.trim(),
      page: 1,
      pageSize: 20,
    })
    return response.items
  } catch (error) {
    ElMessage.error(`Search failed: ${getIdentityErrorMessage(error)}`)
    return []
  }
}

async function handleCreateSearch() {
  searchLoading.value = true
  searchResults.value = await searchUsersByUsername(usernameSearch.value)
  searchLoading.value = false
}

async function handleEditSearch() {
  editStudentForm.searchLoading = true
  editStudentForm.searchResults = await searchUsersByUsername(editStudentForm.usernameSearch)
  editStudentForm.searchLoading = false
}

async function handleLinkSearch() {
  linkAccountForm.searchLoading = true
  linkAccountForm.searchResults = await searchUsersByUsername(linkAccountForm.usernameSearch)
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
}

function removeAccountFromEdit(userId: string) {
  editStudentForm.selectedAccountIds = editStudentForm.selectedAccountIds.filter(id => id !== userId)
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
    usernameSearch.value = ''
    searchResults.value = []
    await loadStudents()
  } catch (error) {
    ElMessage.error(`Failed to create student: ${getStudentErrorMessage(error)}`)
  } finally {
    creatingStudent.value = false
  }
}

function openEditDialog(row: StudentDto) {
  editStudentForm.visible = true
  editStudentForm.studentId = row.id
  editStudentForm.name = row.name
  editStudentForm.grade = row.grade
  editStudentForm.selectedAccountIds = [...row.identityAccountIds]
  editStudentForm.usernameSearch = ''
  editStudentForm.searchResults = []
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
    ElMessage.success('Student updated successfully.')
    editStudentForm.visible = false
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
  linkAccountForm.usernameSearch = ''
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
  return accountId.substring(0, 8) + '...'
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
})
</script>

<template>
  <div class="app-shell">
    <!-- Header -->
    <header class="hero">
      <div class="hero-main">
        <div>
          <h1>{{ appTitle }}</h1>
          <p class="hero-sub">Manage student profiles and bind them to Identity accounts</p>
        </div>
      </div>
      <div style="display: flex; align-items: center; gap: 8px;">
        <el-button
          v-if="identityConnected"
          text
          size="small"
          @click="showConnectionPanel = !showConnectionPanel"
        >
          {{ showConnectionPanel ? 'Hide' : 'Show' }} Config
        </el-button>
        <el-tag :type="identityConnected ? 'success' : 'info'" class="status-tag">
          {{ identityConnected ? 'Connected' : 'Disconnected' }}
        </el-tag>
      </div>
    </header>

    <!-- Connection Panel -->
    <el-card v-if="showConnectionPanel || !identityConnected" shadow="never" class="panel connection-panel">
      <template #header>
        <div class="panel-header">
          <span>Identity Authentication</span>
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
    <el-card shadow="never" class="panel">
      <div class="split-layout">
        <!-- Create Student -->
        <el-card shadow="never" class="compact-card">
          <template #header>
            <div class="panel-header"><span>Create Student</span></div>
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
              <div style="width: 100%">
                <div class="account-search-bar">
                  <el-input
                    v-model="usernameSearch"
                    placeholder="Search by username..."
                    size="small"
                    :disabled="!identityConnected"
                    @keyup.enter="handleCreateSearch"
                  />
                  <el-button
                    type="primary"
                    size="small"
                    :loading="searchLoading"
                    :disabled="!identityConnected || !usernameSearch.trim()"
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
                    <span>{{ formatAccountLabel(user) }}</span>
                    <el-icon v-if="createStudentForm.selectedAccountIds.includes(user.userId)" style="color: #67c23a;"><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M406.656 706.944l-195.2-195.2 60.330667-60.330667 134.869333 134.869334 300.8-300.8 60.330667 60.330666z"/></svg></el-icon>
                  </div>
                </div>
                <div v-if="createStudentForm.selectedAccountIds.length" class="selected-accounts">
                  <el-tag
                    v-for="accountId in createStudentForm.selectedAccountIds" :key="accountId"
                    closable size="small" type="info"
                    @close="removeAccountFromCreate(accountId)"
                  >
                    {{ getAccountLabel(accountId) }}
                  </el-tag>
                </div>
                <div v-if="!identityConnected" style="color: #909399; font-size: 11px; margin-top: 4px;">
                  Connect to Identity service to search accounts.
                </div>
              </div>
            </el-form-item>
            <el-button
              type="primary" size="small" :loading="creatingStudent"
              @click="handleCreateStudent"
            >
              Create Student
            </el-button>
          </el-form>
        </el-card>

        <!-- Student List -->
        <el-card shadow="never" class="compact-card">
          <template #header>
            <div class="panel-header">
              <span>Student List</span>
              <el-button size="small" @click="loadStudents">Refresh</el-button>
            </div>
          </template>

          <div class="filter-bar">
            <el-input v-model="studentFilters.name" placeholder="Search by name" clearable size="small" />
            <el-button type="primary" size="small" @click="loadStudents">Search</el-button>
          </div>

          <el-table :data="students" v-loading="loadingStudents" empty-text="No data" class="data-table" size="small">
            <el-table-column prop="name" label="Name" min-width="120" />
            <el-table-column label="Grade" width="100">
              <template #default="{ row }">{{ getGradeLabel(row.grade) }}</template>
            </el-table-column>
            <el-table-column label="Linked Accounts" min-width="180">
              <template #default="{ row }">
                <div class="account-tags">
                  <el-tag
                    v-for="accountId in row.identityAccountIds" :key="accountId" size="small" type="info"
                  >
                    {{ accountId.substring(0, 8) }}...
                  </el-tag>
                  <span v-if="!row.identityAccountIds.length" style="color: #c0c4cc; font-size: 12px;">None</span>
                </div>
              </template>
            </el-table-column>
            <el-table-column label="Created" min-width="140">
              <template #default="{ row }">{{ formatDate(row.createdAt) }}</template>
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
    <el-dialog v-model="editStudentForm.visible" title="Edit Student" width="520px" destroy-on-close class="compact-dialog">
      <el-form class="compact-form" label-position="top">
        <el-form-item label="Student Name"><el-input v-model="editStudentForm.name" size="small" /></el-form-item>
        <el-form-item label="Grade">
          <el-select v-model="editStudentForm.grade" placeholder="Select grade" size="small" style="width: 100%">
            <el-option
              v-for="g in gradeOptions" :key="g.value"
              :label="g.label" :value="g.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="Linked Identity Accounts">
          <div style="width: 100%">
            <div class="account-search-bar">
              <el-input
                v-model="editStudentForm.usernameSearch"
                placeholder="Search by username..."
                size="small"
                :disabled="!identityConnected"
                @keyup.enter="handleEditSearch"
              />
              <el-button
                type="primary"
                size="small"
                :loading="editStudentForm.searchLoading"
                :disabled="!identityConnected || !editStudentForm.usernameSearch.trim()"
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
                <span>{{ formatAccountLabel(user) }}</span>
                <el-icon v-if="editStudentForm.selectedAccountIds.includes(user.userId)" style="color: #67c23a;"><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M406.656 706.944l-195.2-195.2 60.330667-60.330667 134.869333 134.869334 300.8-300.8 60.330667 60.330666z"/></svg></el-icon>
              </div>
            </div>
            <div v-if="editStudentForm.selectedAccountIds.length" class="selected-accounts">
              <el-tag
                v-for="accountId in editStudentForm.selectedAccountIds" :key="accountId"
                closable size="small" type="info"
                @close="removeAccountFromEdit(accountId)"
              >
                {{ getAccountLabel(accountId) }}
              </el-tag>
            </div>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button size="small" @click="editStudentForm.visible = false">Cancel</el-button>
        <el-button type="primary" size="small" :loading="updatingStudent" @click="handleUpdateStudent">Save Changes</el-button>
      </template>
    </el-dialog>

    <!-- Link Account Dialog -->
    <el-dialog v-model="linkAccountForm.visible" :title="`Link Account: ${linkAccountForm.studentName}`" width="480px" destroy-on-close class="compact-dialog">
      <el-form class="compact-form" label-position="top">
        <el-alert type="info" :closable="false" style="margin-bottom: 12px; padding: 8px 12px; font-size: 12px;">
          Search for an Identity account by username and select it to link with this student.
        </el-alert>
        <el-form-item label="Search Identity Account">
          <div style="width: 100%">
            <div class="account-search-bar">
              <el-input
                v-model="linkAccountForm.usernameSearch"
                placeholder="Search by username..."
                size="small"
                :disabled="!identityConnected"
                @keyup.enter="handleLinkSearch"
              />
              <el-button
                type="primary"
                size="small"
                :loading="linkAccountForm.searchLoading"
                :disabled="!identityConnected || !linkAccountForm.usernameSearch.trim()"
                @click="handleLinkSearch"
              >
                Search
              </el-button>
            </div>
            <div v-if="linkAccountForm.searchResults.length" class="search-results">
              <el-radio-group v-model="linkAccountForm.selectedAccountId">
                <div
                  v-for="user in linkAccountForm.searchResults" :key="user.userId"
                  class="search-result-item"
                >
                  <el-radio :value="user.userId" size="small">{{ formatAccountLabel(user) }}</el-radio>
                </div>
              </el-radio-group>
            </div>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button size="small" @click="linkAccountForm.visible = false">Cancel</el-button>
        <el-button type="primary" size="small" :loading="linkingAccount" :disabled="!linkAccountForm.selectedAccountId" @click="handleLinkAccount">
          Link Account
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>
