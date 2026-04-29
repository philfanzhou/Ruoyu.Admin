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
} from './services/studentAdminApi'

const IDENTITY_STORAGE_KEY = 'student-admin-identity-credentials'

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

const studentFilters = reactive({ name: '' })
const identityPhoneSearch = ref('')

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
})

const linkAccountForm = reactive({
  visible: false,
  studentId: '',
  studentName: '',
  selectedAccountId: '',
})

const activeTab = ref('students')
const identityConnected = ref(false)
const loadingStudents = ref(false)
const loadingIdentityUsers = ref(false)
const creatingStudent = ref(false)
const updatingStudent = ref(false)
const deletingStudent = ref(false)
const linkingAccount = ref(false)
const unlinkingAccountId = ref<string | null>(null)

const students = ref<StudentDto[]>([])
const identityUsers = ref<IdentityUser[]>([])
const identityUserTotal = ref(0)
const studentTotal = ref(0)
const studentPage = ref(1)
const studentPageSize = ref(20)

const identityClient = ref<ReturnType<typeof createIdentityAdminApiClient> | null>(null)

const availableIdentityAccounts = computed(() =>
  identityUsers.value
    .filter(u => u.phone || u.username)
    .map(u => ({
      userId: u.userId,
      label: u.phone ? `${u.phone}${u.username ? ` (${u.username})` : ''}` : u.username,
      phone: u.phone,
      username: u.username,
    })),
)

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
    await loadIdentityUsers()
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

async function loadIdentityUsers() {
  if (!identityClient.value) return

  loadingIdentityUsers.value = true
  try {
    const response = await identityClient.value.getUsers({
      phone: identityPhoneSearch.value || undefined,
      page: 1,
      pageSize: 100,
    })
    identityUsers.value = response.items
    identityUserTotal.value = response.total
  } catch (error) {
    ElMessage.error(`Failed to load Identity users: ${getIdentityErrorMessage(error)}`)
  } finally {
    loadingIdentityUsers.value = false
  }
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
  if (!identityUsers.value.length && identityConnected.value) loadIdentityUsers()
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
  const account = availableIdentityAccounts.value.find(a => a.userId === accountId)
  return account?.label ?? accountId.substring(0, 8) + '...'
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
  void loadStudents()
})
</script>

<template>
  <div class="app-shell">
    <header class="hero">
      <div>
        <p class="eyebrow">Ruoyu.Student Admin</p>
        <h1>Student Management Console</h1>
        <p class="hero-description">
          Manage student profiles and bind them to Identity phone accounts.
        </p>
      </div>
      <div style="display: flex; gap: 8px;">
        <el-tag :type="identityConnected ? 'success' : 'info'" size="large">
          {{ identityConnected ? 'Identity Connected' : 'Identity Disconnected' }}
        </el-tag>
      </div>
    </header>

    <el-card shadow="never" class="panel">
      <template #header>
        <div class="panel-header">
          <span>Identity Authentication</span>
          <div class="header-actions">
            <el-button type="danger" text @click="handleClearAllCredentials">Clear</el-button>
            <el-button type="primary" :loading="connectingIdentity" @click="connectIdentity">
              {{ identityConnected ? 'Reconnect' : 'Connect' }}
            </el-button>
          </div>
        </div>
      </template>

      <el-form label-position="top">
        <h4 style="margin: 0 0 12px; color: #4f46e5;">Identity Service</h4>
        <el-form-item label="Admin AppId">
          <el-input v-model="identityCreds.appId" placeholder="Enter Identity AppId" />
        </el-form-item>
        <el-form-item label="Admin AppSecret">
          <el-input v-model="identityCreds.appSecret" show-password placeholder="Enter Identity AppSecret" />
        </el-form-item>
      </el-form>
    </el-card>

    <el-tabs v-model="activeTab" class="panel" stretch>
      <el-tab-pane label="Student Management" name="students">
        <div class="split-layout">
          <el-card shadow="never">
            <template #header><div class="panel-header"><span>Create Student</span></div></template>
            <el-form label-position="top">
              <el-form-item label="Student Name">
                <el-input v-model="createStudentForm.name" placeholder="Enter student name" />
              </el-form-item>
              <el-form-item label="Grade">
                <el-input-number v-model="createStudentForm.grade" :min="1" :max="12" />
              </el-form-item>
              <el-form-item label="Link Identity Accounts">
                <el-select
                  v-model="createStudentForm.selectedAccountIds"
                  multiple filterable
                  placeholder="Search by phone/username..."
                  :disabled="!identityConnected"
                  style="width: 100%"
                >
                  <el-option
                    v-for="acc in availableIdentityAccounts"
                    :key="acc.userId"
                    :label="acc.label"
                    :value="acc.userId"
                  />
                </el-select>
                <div v-if="!identityConnected" style="color: #909399; font-size: 12px; margin-top: 4px;">
                  Connect to Identity service to browse accounts.
                </div>
              </el-form-item>
              <el-button
                type="primary" :loading="creatingStudent"
                @click="handleCreateStudent"
              >
                Create Student
              </el-button>
            </el-form>
          </el-card>

          <el-card shadow="never">
            <template #header>
              <div class="panel-header">
                <span>Student List</span>
                <el-button @click="loadStudents">Refresh</el-button>
              </div>
            </template>

            <div class="filter-bar">
              <el-input v-model="studentFilters.name" placeholder="Search by name" clearable />
              <el-button type="primary" @click="loadStudents">Search</el-button>
            </div>

            <el-table :data="students" v-loading="loadingStudents" empty-text="No data">
              <el-table-column prop="name" label="Name" min-width="140" />
              <el-table-column prop="grade" label="Grade" width="80" />
              <el-table-column label="Linked Accounts" min-width="220">
                <template #default="{ row }">
                  <div class="account-tags">
                    <el-tag
                      v-for="accountId in row.identityAccountIds" :key="accountId" size="small" type="info"
                    >
                      {{ getAccountLabel(accountId) }}
                    </el-tag>
                    <span v-if="!row.identityAccountIds.length" style="color: #c0c4cc;">None</span>
                  </div>
                </template>
              </el-table-column>
              <el-table-column label="Created" min-width="160">
                <template #default="{ row }">{{ formatDate(row.createdAt) }}</template>
              </el-table-column>
              <el-table-column label="Actions" width="260" fixed="right">
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
      </el-tab-pane>

      <el-tab-pane label="Identity Accounts" name="accounts">
        <el-card shadow="never">
          <template #header>
            <div class="panel-header">
              <span>Browse Identity Accounts (for binding)</span>
              <el-button :disabled="!identityConnected" @click="loadIdentityUsers">Refresh</el-button>
            </div>
          </template>

          <div class="filter-bar">
            <el-input v-model="identityPhoneSearch" placeholder="Search by phone number" clearable />
            <el-button type="primary" :disabled="!identityConnected" @click="loadIdentityUsers">Search</el-button>
          </div>

          <el-table :data="identityUsers" v-loading="loadingIdentityUsers" empty-text="No accounts found">
            <el-table-column label="Account ID" min-width="240" show-overflow-tooltip>
              <template #default="{ row }"><code>{{ row.userId }}</code></template>
            </el-table-column>
            <el-table-column prop="phone" label="Phone" min-width="140" />
            <el-table-column prop="username" label="Username" min-width="140" />
            <el-table-column label="Status" width="80">
              <template #default="{ row }">
                <el-tag :type="row.isActive ? 'success' : 'danger'" size="small">
                  {{ row.isActive ? 'Active' : 'Disabled' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="remark" label="Remark" min-width="160" show-overflow-tooltip />
            <el-table-column label="Created" min-width="160">
              <template #default="{ row }">{{ formatDate(row.createdAt) }}</template>
            </el-table-column>
          </el-table>

          <div style="margin-top: 12px; color: #909399; font-size: 13px;">
            Total: {{ identityUserTotal }} accounts. Use Account IDs when creating or editing students.
          </div>
        </el-card>
      </el-tab-pane>
    </el-tabs>

    <!-- Edit Student Dialog -->
    <el-dialog v-model="editStudentForm.visible" title="Edit Student" width="560px" destroy-on-close>
      <el-form label-position="top">
        <el-form-item label="Student Name"><el-input v-model="editStudentForm.name" /></el-form-item>
        <el-form-item label="Grade"><el-input-number v-model="editStudentForm.grade" :min="1" :max="12" /></el-form-item>
        <el-form-item label="Linked Identity Accounts">
          <el-select
            v-model="editStudentForm.selectedAccountIds" multiple filterable
            placeholder="Search by phone/username..." :disabled="!identityConnected" style="width: 100%"
          >
            <el-option
              v-for="acc in availableIdentityAccounts" :key="acc.userId"
              :label="acc.label" :value="acc.userId"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editStudentForm.visible = false">Cancel</el-button>
        <el-button type="primary" :loading="updatingStudent" @click="handleUpdateStudent">Save Changes</el-button>
      </template>
    </el-dialog>

    <!-- Link Account Dialog -->
    <el-dialog v-model="linkAccountForm.visible" :title="`Link Account to: ${linkAccountForm.studentName}`" width="500px" destroy-on-close>
      <el-form label-position="top">
        <el-alert type="info" :closable="false" style="margin-bottom: 16px;">
          Select an Identity account to link with this student.
        </el-alert>
        <el-form-item label="Select Identity Account">
          <el-select
            v-model="linkAccountForm.selectedAccountId" filterable
            placeholder="Type to search accounts..." :disabled="!identityConnected" style="width: 100%"
          >
            <el-option
              v-for="acc in availableIdentityAccounts" :key="acc.userId"
              :label="acc.label" :value="acc.userId"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="linkAccountForm.visible = false">Cancel</el-button>
        <el-button type="primary" :loading="linkingAccount" :disabled="!linkAccountForm.selectedAccountId" @click="handleLinkAccount">
          Link Account
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>
