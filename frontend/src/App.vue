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
  type ClassificationOption,
  type UploadRecordDto,
  type MistakeItemDto,
  type EnumOption,
  type EnumOptionsResponse,
} from './services/studentAdminApi'
import {
  teacherPortalClient,
  getTeacherPortalErrorMessage,
  type TeacherAccountDto,
} from './services/teacherPortalApi'
import {
  ossAuditClient,
  getOssAuditErrorMessage,
  formatFileSize,
  type OssAuditRecordDto,
  type OssAuditRecordsResponse,
} from './services/ossAuditApi'

const appTitle = (window as any).__APP_TITLE__ || 'Student Management Console'

const enumOptions = ref<EnumOptionsResponse>({
  uploadStatuses: [],
  grades: [],
  subjects: [],
  classifications: [],
  reviewStatuses: [],
  mistakeTypes: [],
})

const enumMap = computed(() => {
  const toMap = (options: EnumOption[]) => new Map(options.map(o => [o.value, o.displayName]))
  return {
    uploadStatus: toMap(enumOptions.value.uploadStatuses),
    grade: toMap(enumOptions.value.grades),
    subject: toMap(enumOptions.value.subjects),
    classification: toMap(enumOptions.value.classifications),
    reviewStatus: toMap(enumOptions.value.reviewStatuses),
    mistakeType: toMap(enumOptions.value.mistakeTypes),
  }
})

async function loadEnumOptions() {
  try {
    enumOptions.value = await studentAdminClient.getEnumOptions()
  } catch {
    console.warn('Failed to load enum options from backend')
  }
}

// OSS Audit
const auditRecords = ref<OssAuditRecordDto[]>([])
const auditTotalCount = ref(0)
const auditPage = ref(1)
const auditPageSize = ref(20)
const auditStatusFilter = ref<number | undefined>(undefined)
const auditBucketFilter = ref<string | undefined>(undefined)
const auditStatusCounts = ref<Record<number, number>>({})
const auditBucketCounts = ref<Record<string, number>>({})
const loadingAuditRecords = ref(false)
const triggeringAudit = ref(false)
const resolvingRecord = ref<number | null>(null)
const batchResolving = ref(false)
const selectedRecordIds = ref<number[]>([])
const previewImage = ref('')
const showImagePreview = computed(() => !!previewImage.value)

// Upload Records
const uploadRecords = ref<UploadRecordDto[]>([])
const uploadTotalCount = ref(0)
const uploadPage = ref(1)
const uploadPageSize = ref(20)
const uploadStatusFilter = ref<number>(4)
const loadingUploadRecords = ref(false)
const resettingRecord = ref<string | null>(null)
const assigningRecord = ref<UploadRecordDto | null>(null)
const showRecordDetailDialog = ref(false)
const assignForm = reactive({
  classification: 1,
  subject: 3,
  grade: 1
})
const loadingAssign = ref(false)
const loadingRotation = ref(false)
const recordMistakes = ref<MistakeItemDto[]>([])
const loadingRecordMistakes = ref(false)

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
const classificationOptions = ref<ClassificationOption[]>([])
const selectedOpenSubjects = ref<OpenSubjectDto[]>([])
const loadingOpenSubjects = ref(false)

function getGradeLabel(grade: number): string {
  return enumMap.value.grade.get(grade) ?? String(grade)
}

function formatAccountLabel(user: IdentityUser): string {
  const name = user.displayName || user.username || user.phone || user.userId
  if (user.remark) return `${name}(${user.remark})`
  return name
}

function formatDate(timestamp?: number | string | null) {
  if (!timestamp) return '-'
  const date = typeof timestamp === 'string' ? new Date(timestamp) : new Date(timestamp * 1000)
  return isNaN(date.getTime()) ? '-' : date.toLocaleString()
}

function isImageFile(path: string): boolean {
  const ext = path.split('.').pop()?.toLowerCase() ?? ''
  return ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg', 'ico', 'tiff', 'tif', 'avif'].includes(ext)
}

function getSubjectDisplayName(subjectValue: number): string {
  return enumMap.value.subject.get(subjectValue) ?? `学科${subjectValue}`
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

async function loadClassificationOptions() {
  try {
    classificationOptions.value = await studentAdminClient.getClassificationOptions()
  } catch (error) {
    console.warn('Failed to load classification options:', error)
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
const editingTeacherSubjects = ref<number[]>([])
const editingTeacherId = ref<string | null>(null)
const editingTeacherName = ref<string>('')
const showSubjectsDialog = ref(false)
const savingSubjects = ref(false)
const grantSubjectDialog = ref(false)
const grantSubjectUserId = ref('')
const grantSubjectUserName = ref('')
const grantSubjectValues = ref<number[]>([])
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
  grantSubjectValues.value = []
  grantSubjectDialog.value = true
}

async function confirmGrantTeacher() {
  if (grantSubjectValues.value.length === 0) {
    ElMessage.warning('请至少选择一个科目')
    return
  }
  grantingTeacherLoading.value = true
  try {
    const result = await teacherPortalClient.grantTeacher(grantSubjectUserId.value, {
      subjects: grantSubjectValues.value,
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
      const result = await teacherPortalClient.getTeacherSubjects(teacher.userId)
      editingTeacherSubjects.value = result.success ? result.data : []
    } catch {
      editingTeacherSubjects.value = teacher.subjects ?? []
    }
  } else {
    editingTeacherSubjects.value = teacher.subjects ?? []
  }
  showSubjectsDialog.value = true
}

async function saveTeacherSubjects() {
  if (!editingTeacherId.value) return
  if (editingTeacherSubjects.value.length === 0) {
    ElMessage.warning('请至少选择一个科目')
    return
  }
  savingSubjects.value = true
  try {
    const result = await teacherPortalClient.setTeacherSubjects(editingTeacherId.value, editingTeacherSubjects.value)
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

function getSubjectNames(subjectValues: number[]): string {
  if (!subjectValues || subjectValues.length === 0) return '未分配'
  return subjectValues.map(v => getSubjectName(v)).join(', ')
}

function getSubjectName(subjectValue: number): string {
  const subject = availableSubjects.value.find(s => s.value === subjectValue)
  return subject?.name ?? String(subjectValue)
}

// OSS Audit Functions
async function loadAuditRecords() {
  loadingAuditRecords.value = true
  try {
    const result = await ossAuditClient.getRecords(auditPage.value, auditPageSize.value, auditStatusFilter.value, auditBucketFilter.value)
    auditRecords.value = result.items
    auditTotalCount.value = result.totalCount
    auditStatusCounts.value = result.statusCounts
    auditBucketCounts.value = result.bucketCounts
  } catch (error) {
    ElMessage.error('加载审计记录失败: ' + getOssAuditErrorMessage(error))
  } finally {
    loadingAuditRecords.value = false
  }
}

async function triggerAudit() {
  triggeringAudit.value = true
  try {
    await ossAuditClient.triggerAudit()
    ElMessage.success('审计已触发，结果将在稍后可用')
    setTimeout(() => loadAuditRecords(), 5000)
  } catch (error) {
    ElMessage.error('触发审计失败: ' + getOssAuditErrorMessage(error))
  } finally {
    triggeringAudit.value = false
  }
}

async function resolveRecord(id: number) {
  try {
    await ElMessageBox.confirm('确认删除这个僵尸文件吗？删除后不可恢复。', '删除确认', {
      confirmButtonText: '确认',
      cancelButtonText: '取消',
      type: 'warning'
    })
  } catch { return }

  resolvingRecord.value = id
  try {
    await ossAuditClient.resolveRecord(id)
    ElMessage.success('删除成功')
    await loadAuditRecords()
  } catch (error) {
    ElMessage.error('删除失败: ' + getOssAuditErrorMessage(error))
  } finally {
    resolvingRecord.value = null
  }
}

async function ignoreRecord(id: number) {
  try {
    await ossAuditClient.ignoreRecord(id)
    ElMessage.success('已忽略')
    await loadAuditRecords()
  } catch (error) {
    ElMessage.error('操作失败: ' + getOssAuditErrorMessage(error))
  }
}

async function batchResolveRecords() {
  if (selectedRecordIds.value.length === 0) {
    ElMessage.warning('请先选择要处理的记录')
    return
  }

  try {
    await ElMessageBox.confirm(`确认删除 ${selectedRecordIds.value.length} 个僵尸文件吗？`, '批量删除确认', {
      confirmButtonText: '确认',
      cancelButtonText: '取消',
      type: 'warning'
    })
  } catch { return }

  batchResolving.value = true
  try {
    const result = await ossAuditClient.batchResolve(selectedRecordIds.value)
    ElMessage.success(`成功删除 ${result.resolvedCount} 个文件`)
    selectedRecordIds.value = []
    await loadAuditRecords()
  } catch (error) {
    ElMessage.error('批量删除失败: ' + getOssAuditErrorMessage(error))
  } finally {
    batchResolving.value = false
  }
}

function toggleRecordSelection(id: number) {
  const index = selectedRecordIds.value.indexOf(id)
  if (index >= 0) {
    selectedRecordIds.value.splice(index, 1)
  } else {
    selectedRecordIds.value.push(id)
  }
}

function onAuditPageChange(page: number) {
  auditPage.value = page
  loadAuditRecords()
}

function onAuditFilterChange() {
  auditPage.value = 1
  loadAuditRecords()
}

// Upload Record Functions
function getUploadStatusText(status: number): string {
  return enumMap.value.uploadStatus.get(status) ?? '未知'
}

function getUploadStatusType(status: number): string {
  const name = enumOptions.value.uploadStatuses.find(o => o.value === status)?.name ?? ''
  if (name === 'PENDING') return 'warning'
  if (name === 'PROCESSING') return 'primary'
  if (name === 'COMPLETED') return 'success'
  if (name === 'FAILED') return 'danger'
  return 'info'
}

async function loadUploadRecords() {
  loadingUploadRecords.value = true
  try {
    const result = await studentAdminClient.getUploadRecords({
      page: uploadPage.value,
      pageSize: uploadPageSize.value,
      status: uploadStatusFilter.value >= 0 ? uploadStatusFilter.value : undefined,
    })
    uploadRecords.value = result.items
    uploadTotalCount.value = result.totalCount
  } catch (error) {
    ElMessage.error('加载上传记录失败: ' + getStudentErrorMessage(error))
  } finally {
    loadingUploadRecords.value = false
  }
}

async function resetUploadRecord(record: UploadRecordDto) {
  try {
    await ElMessageBox.confirm('确认将该记录状态重置为"待处理"吗？系统会自动重新分析。', '重置确认', {
      confirmButtonText: '确认',
      cancelButtonText: '取消',
      type: 'warning'
    })
  } catch { return }

  resettingRecord.value = record.id
  try {
    const pendingStatus = enumOptions.value.uploadStatuses.find(opt => opt.name === 'PENDING')?.value ?? 1
    await studentAdminClient.resetUploadRecordStatus(record.id, record.studentId, pendingStatus)
    ElMessage.success('已重置，系统将自动重新分析')
    await loadUploadRecords()
  } catch (error) {
    ElMessage.error('重置失败: ' + getStudentErrorMessage(error))
  } finally {
    resettingRecord.value = null
  }
}

async function openRecordDetail(record: UploadRecordDto) {
  assigningRecord.value = record
  assignForm.classification = record.classification || 1
  assignForm.subject = record.subject || 3
  const studentGrade = students.value.find(s => s.id === record.studentId)?.grade
  assignForm.grade = record.grade || studentGrade || 1
  showRecordDetailDialog.value = true
  
  // 加载关联的错题
  recordMistakes.value = []
  loadingRecordMistakes.value = true
  try {
    const result = await studentAdminClient.getMistakesByUploadId(record.id)
    recordMistakes.value = result.items
  } catch (error) {
    console.error('Failed to load record mistakes:', error)
  } finally {
    loadingRecordMistakes.value = false
  }
}

async function doAssign() {
  if (!assigningRecord.value) return
  loadingAssign.value = true
  try {
    const result = await studentAdminClient.assignUploadRecord(assigningRecord.value.id, {
      studentId: assigningRecord.value.studentId,
      classification: assignForm.classification,
      subject: assignForm.subject,
      grade: assignForm.grade
    })
    if (result.warning) {
      ElMessage.warning(result.warning)
    } else {
      ElMessage.success('指派成功')
    }
    showRecordDetailDialog.value = false
    await loadUploadRecords()
  } catch (error) {
    ElMessage.error('指派失败: ' + getStudentErrorMessage(error))
  } finally {
    loadingAssign.value = false
  }
}

async function rotateImage(imageIndex: number, rotation: number) {
  if (!assigningRecord.value) return
  loadingRotation.value = true
  try {
    await studentAdminClient.rotateUploadImage(assigningRecord.value.id, {
      studentId: assigningRecord.value.studentId,
      imageIndex: imageIndex,
      rotation: rotation
    })
    // Reload records to get updated image paths, but keep the current record open
    await loadUploadRecords()
    // Refresh the assigning record with new data
    const updatedRecord = uploadRecords.value.find(r => r.id === assigningRecord.value!.id)
    if (updatedRecord) {
      assigningRecord.value = updatedRecord
    }
    ElMessage.success('旋转成功')
  } catch (error) {
    ElMessage.error('旋转失败: ' + getStudentErrorMessage(error))
  } finally {
    loadingRotation.value = false
  }
}

function onUploadPageChange(page: number) {
  uploadPage.value = page
  loadUploadRecords()
}

function onUploadFilterChange() {
  uploadPage.value = 1
  loadUploadRecords()
}

// ========== Mistake Query ==========
const mistakeItems = ref<MistakeItemDto[]>([])
const mistakeTotal = ref(0)
const mistakePage = ref(1)
const mistakePageSize = ref(20)
const mistakeStudentId = ref<string>('')
const mistakeStudentName = ref<string>('')
const mistakeStudentSearch = ref<string>('')
const mistakeStudentResults = ref<StudentDto[]>([])
const loadingMistakeStudentSearch = ref(false)
const mistakeSubject = ref<number | undefined>(undefined)
const mistakeGrade = ref<number | undefined>(undefined)
const mistakeReviewStatus = ref<number | undefined>(undefined)
const loadingMistakes = ref(false)
const showMistakeDetailDialog = ref(false)
const currentMistakeDetail = ref<MistakeItemDto | null>(null)
const showMistakeEditDialog = ref(false)
const currentMistakeEdit = ref<{
  id: string
  studentId: string
  studentName: string
  subject: number
  grade: number
} | null>(null)

let mistakeStudentSearchTimeout: ReturnType<typeof setTimeout> | null = null

async function searchMistakeStudent(query: string) {
  if (mistakeStudentSearchTimeout) {
    clearTimeout(mistakeStudentSearchTimeout)
  }

  if (!query.trim()) {
    mistakeStudentResults.value = []
    return
  }

  mistakeStudentSearchTimeout = setTimeout(async () => {
    loadingMistakeStudentSearch.value = true
    try {
      const result = await studentAdminClient.getStudents({
        name: query.trim(),
        page: 1,
        pageSize: 10
      })
      mistakeStudentResults.value = result.items
    } catch (error) {
      console.warn('Failed to search students:', error)
      mistakeStudentResults.value = []
    } finally {
      loadingMistakeStudentSearch.value = false
    }
  }, 300)
}

function selectMistakeStudent(student: StudentDto) {
  mistakeStudentId.value = student.id
  mistakeStudentName.value = student.name
  mistakeStudentSearch.value = ''
  mistakeStudentResults.value = []
  onMistakeFilterChange()
}

function clearMistakeStudent() {
  mistakeStudentId.value = ''
  mistakeStudentName.value = ''
  mistakeStudentSearch.value = ''
  mistakeStudentResults.value = []
  onMistakeFilterChange()
}

async function loadMistakeItems() {
  loadingMistakes.value = true
  try {
    const result = await studentAdminClient.getMistakeItems({
      studentId: mistakeStudentId.value || undefined,
      subject: mistakeSubject.value,
      grade: mistakeGrade.value,
      reviewStatus: mistakeReviewStatus.value,
      page: mistakePage.value,
      size: mistakePageSize.value
    })
    mistakeItems.value = result.items
    mistakeTotal.value = result.total
  } catch (error) {
    ElMessage.error('加载错题列表失败: ' + getStudentErrorMessage(error))
  } finally {
    loadingMistakes.value = false
  }
}

async function viewMistakeDetail(item: MistakeItemDto) {
  try {
    currentMistakeDetail.value = await studentAdminClient.getMistakeItem(item.id)
    showMistakeDetailDialog.value = true
  } catch (error) {
    ElMessage.error('加载错题详情失败: ' + getStudentErrorMessage(error))
  }
}

function onMistakePageChange(page: number) {
  mistakePage.value = page
  loadMistakeItems()
}

function onMistakeFilterChange() {
  mistakePage.value = 1
  loadMistakeItems()
}

function clearMistakeFilters() {
  mistakeStudentId.value = ''
  mistakeSubject.value = undefined
  mistakeGrade.value = undefined
  mistakeReviewStatus.value = undefined
  mistakePage.value = 1
  loadMistakeItems()
}

function getReviewStatusText(status: number): string {
  return enumMap.value.reviewStatus.get(status) ?? '未知'
}

function getReviewStatusType(status: number): string {
  const name = enumOptions.value.reviewStatuses.find(o => o.value === status)?.name ?? ''
  if (name === 'PENDING_REVIEW') return 'warning'
  if (name === 'CONFIRMED') return 'success'
  if (name === 'REJECTED') return 'danger'
  return 'info'
}

function getMistakeTypeText(type: number): string {
  return enumMap.value.mistakeType.get(type) ?? '其他'
}

async function openMistakeEdit(item: MistakeItemDto) {
  currentMistakeEdit.value = {
    id: item.id,
    studentId: item.studentId,
    studentName: item.studentName,
    subject: item.subject,
    grade: item.grade
  }
  showMistakeEditDialog.value = true
}

async function saveMistakeEdit() {
  if (!currentMistakeEdit.value) return

  try {
    await studentAdminClient.updateMistakeItem(currentMistakeEdit.value.id, {
      studentId: currentMistakeEdit.value.studentId,
      subject: currentMistakeEdit.value.subject,
      grade: currentMistakeEdit.value.grade
    })
    ElMessage.success('更新成功')
    showMistakeEditDialog.value = false
    await loadMistakeItems()
  } catch (error) {
    ElMessage.error('更新失败: ' + getStudentErrorMessage(error))
  }
}

onMounted(async () => {
  await loadEnumOptions()
  void loadGradeOptions()
  void loadStudents()
  void loadSubjectOptions()
  void loadClassificationOptions()
  void loadTeachers()
  void loadAuditRecords()
  void loadAvailableSubjects()
  void loadUploadRecords()
  void loadMistakeItems()
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
            <el-table-column label="Subjects" min-width="180">
              <template #default="{ row }">
                <template v-if="row.subjects && row.subjects.length > 0">
                  <el-tag v-for="s in row.subjects" :key="typeof s === 'object' ? s.subject : s" size="small" type="info" effect="plain" class="subject-tag" style="margin-right: 4px">
                    {{ getSubjectName(typeof s === 'object' ? s.subject : s) }}
                  </el-tag>
                </template>
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
          <p class="hint-text">选择该教师负责的科目（可多选）</p>
          <div class="subject-select">
            <el-select v-model="editingTeacherSubjects" multiple placeholder="请选择科目" style="width: 100%">
              <el-option
                v-for="subject in availableSubjects"
                :key="subject.value"
                :label="subject.name"
                :value="subject.value"
              />
            </el-select>
          </div>
          <div v-if="editingTeacherSubjects.length > 0" class="selected-subject-preview">
            <span class="info-label">已选：</span>
            <el-tag v-for="s in editingTeacherSubjects" :key="s" size="small" type="primary" effect="light" class="subject-tag" closable @close="editingTeacherSubjects = editingTeacherSubjects.filter(x => x !== s)">
              {{ getSubjectName(s) }}
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
          <p class="hint-text">请选择该教师负责的科目（可多选）</p>
          <div class="subject-select">
            <el-select v-model="grantSubjectValues" multiple placeholder="请选择科目" style="width: 100%">
              <el-option
                v-for="subject in availableSubjects"
                :key="subject.value"
                :label="subject.name"
                :value="subject.value"
              />
            </el-select>
          </div>
          <div v-if="grantSubjectValues.length > 0" class="selected-subject-preview">
            <span class="info-label">已选：</span>
            <el-tag v-for="s in grantSubjectValues" :key="s" size="small" type="primary" effect="light" class="subject-tag" closable @close="grantSubjectValues = grantSubjectValues.filter(x => x !== s)">
              {{ getSubjectName(s) }}
            </el-tag>
          </div>
        </div>
      </div>
      <template #footer>
        <div class="dialog-footer">
          <el-button size="default" @click="grantSubjectDialog = false">取消</el-button>
          <el-button type="primary" size="default" :loading="grantingTeacherLoading" :disabled="grantSubjectValues.length < 1" @click="confirmGrantTeacher">确认授权</el-button>
        </div>
      </template>
    </el-dialog>

    <!-- Upload Records Panel -->
    <el-card shadow="never" class="panel upload-records-panel">
      <template #header>
        <div class="panel-header">
          <div class="panel-title">
            <el-icon><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg></el-icon>
            <span>上传记录管理</span>
          </div>
          <el-button size="small" text @click="loadUploadRecords">
            <el-icon class="refresh-icon" :class="{ spinning: loadingUploadRecords }"><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M784.512 230.272v-50.56a32 32 0 1 1 64 0v149.056a32 32 0 0 1-32 32H667.52a32 32 0 1 1 0-64h92.992A362.24 362.24 0 0 0 512 149.824C296.32 149.824 121.216 325.056 121.216 540.8c0 215.68 175.104 390.848 390.784 390.848a390.208 390.208 0 0 0 338.304-194.752 32 32 0 1 1 55.36 32.256A454.144 454.144 0 0 1 512 963.648c-233.152 0-422.848-189.632-422.848-422.848S278.848 117.952 512 117.952c124.544 0 236.608 53.952 314.24 139.712l-41.728-27.392z"/></svg></el-icon>
            刷新
          </el-button>
        </div>
      </template>

      <div class="upload-toolbar">
        <div class="upload-filters">
          <el-select v-model="uploadStatusFilter" placeholder="状态筛选" size="small" clearable style="width: 150px" @change="onUploadFilterChange">
            <el-option label="全部" :value="-1" />
            <el-option v-for="opt in enumOptions.uploadStatuses" :key="opt.value" :label="opt.displayName" :value="opt.value" />
          </el-select>
        </div>
      </div>

      <div v-if="loadingUploadRecords" class="empty-state">
        <el-empty description="加载中..."></el-empty>
      </div>
      <div v-else-if="uploadRecords.length === 0" class="empty-state">
        <el-empty description="暂无上传记录"></el-empty>
      </div>
      <div v-else>
        <el-table :data="uploadRecords" v-loading="loadingUploadRecords" empty-text="暂无数据" class="data-table" size="small">
          <el-table-column prop="id" label="记录ID" min-width="150" show-overflow-tooltip />
          <el-table-column prop="studentName" label="学生姓名" min-width="120" show-overflow-tooltip />
          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <el-tag :type="getUploadStatusType(row.status)" size="small" effect="light">
                {{ getUploadStatusText(row.status) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="图片数量" width="100">
            <template #default="{ row }">{{ row.imagePaths?.length || 0 }}</template>
          </el-table-column>
          <el-table-column label="学科" width="120">
            <template #default="{ row }">
              <span v-if="row.subject">{{ getSubjectDisplayName(row.subject) }}</span>
              <span v-else class="empty-text">-</span>
            </template>
          </el-table-column>
          <el-table-column label="年级" width="100">
            <template #default="{ row }">
              <span v-if="row.grade">{{ getGradeLabel(row.grade) }}</span>
              <span v-else class="empty-text">-</span>
            </template>
          </el-table-column>
          <el-table-column prop="comments" label="备注" min-width="150" show-overflow-tooltip />
          <el-table-column label="创建时间" min-width="150">
            <template #default="{ row }"><span class="time-text">{{ formatDate(row.createdAt) }}</span></template>
          </el-table-column>
          <el-table-column label="更新时间" min-width="150">
            <template #default="{ row }"><span class="time-text">{{ formatDate(row.updatedAt) }}</span></template>
          </el-table-column>
          <el-table-column label="操作" width="160" fixed="right">
            <template #default="{ row }">
              <div class="table-actions">
                <el-button
                  link
                  type="primary"
                  size="small"
                  @click="openRecordDetail(row)"
                >
                  查看指派
                </el-button>
                <el-button
                  v-if="row.status === 3"
                  link
                  type="danger"
                  size="small"
                  :loading="resettingRecord === row.id"
                  @click="resetUploadRecord(row)"
                >
                  重新处理
                </el-button>
              </div>
            </template>
          </el-table-column>
        </el-table>

        <div class="pagination-bar">
          <el-pagination
            background
            layout="total, sizes, prev, pager, next"
            :total="uploadTotalCount"
            :page-size="uploadPageSize"
            :current-page="uploadPage"
            :page-sizes="[10, 20, 50, 100]"
            @current-change="onUploadPageChange"
          />
        </div>
      </div>
    </el-card>

    <!-- OSS Audit Panel -->
    <el-card shadow="never" class="panel oss-audit-panel">
      <template #header>
        <div class="panel-header">
          <div class="panel-title">
            <el-icon><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg></el-icon>
            <span>OSS 僵尸文件审计</span>
          </div>
          <el-button size="small" type="primary" :loading="triggeringAudit" @click="triggerAudit">
            立即审计
          </el-button>
        </div>
      </template>

      <div class="audit-toolbar">
        <div class="audit-filters">
          <el-select v-model="auditStatusFilter" placeholder="状态筛选" size="small" clearable style="width: 120px" @change="onAuditFilterChange">
            <el-option label="待处理" :value="0" />
            <el-option label="已删除" :value="1" />
            <el-option label="已忽略" :value="2" />
          </el-select>
          <el-select v-model="auditBucketFilter" placeholder="Bucket 筛选" size="small" clearable style="width: 130px" @change="onAuditFilterChange">
            <el-option v-for="(count, bucket) in auditBucketCounts" :key="bucket" :label="`${bucket} (${count})`" :value="bucket" />
          </el-select>
        </div>
        <div class="audit-stats">
          <el-tag type="danger" effect="plain" size="small">待处理: {{ auditStatusCounts[0] || 0 }}</el-tag>
          <el-tag type="success" effect="plain" size="small">已删除: {{ auditStatusCounts[1] || 0 }}</el-tag>
          <el-tag type="info" effect="plain" size="small">已忽略: {{ auditStatusCounts[2] || 0 }}</el-tag>
        </div>
      </div>

      <div v-if="selectedRecordIds.length > 0" class="bulk-action-bar">
        <el-button size="small" type="danger" :loading="batchResolving" @click="batchResolveRecords">
          批量删除选中 ({{ selectedRecordIds.length }})
        </el-button>
      </div>

      <div v-if="loadingAuditRecords" class="empty-state">
        <el-empty description="加载中..."></el-empty>
      </div>
      <div v-else-if="auditRecords.length === 0" class="empty-state">
        <el-empty description="暂无审计记录"></el-empty>
      </div>
      <div v-else class="audit-record-list">
        <div
          v-for="record in auditRecords"
          :key="record.id"
          class="audit-record-item"
          :class="{ selected: selectedRecordIds.includes(record.id), resolved: record.status !== 0 }"
        >
          <el-checkbox
            v-if="record.status === 0"
            :model-value="selectedRecordIds.includes(record.id)"
            @click.stop="toggleRecordSelection(record.id)"
          />
          <el-tag v-else :type="record.status === 1 ? 'success' : 'info'" size="small" effect="dark" class="status-tag">{{ record.statusText }}</el-tag>
          <div v-if="isImageFile(record.objectPath)" class="file-thumbnail" @click.stop="previewImage = record.objectPath">
            <img :src="`/api/admin/image?path=${encodeURIComponent(record.objectPath)}`" loading="lazy" @error="($event.target as HTMLImageElement).style.display='none'" />
          </div>
          <div v-else class="file-thumbnail file-thumbnail-placeholder">
            <el-icon :size="20"><svg viewBox="0 0 1024 1024" width="20" height="20"><path fill="currentColor" d="M832 384H576V128H192v768h640V384zm-26.496-64L640 154.496V320h165.504zM160 64h480l256 256v608a32 32 0 0 1-32 32H160a32 32 0 0 1-32-32V96a32 32 0 0 1 32-32z"/></svg></el-icon>
          </div>
          <div class="file-info">
            <div class="file-path">{{ record.objectPath }}</div>
            <div class="file-meta">
              <el-tag size="small" effect="plain" type="info">{{ record.bucket }}</el-tag>
              {{ formatFileSize(record.size) }}
              <span v-if="record.lastModified"> · {{ formatDate(record.lastModified) }}</span>
            </div>
          </div>
          <div v-if="record.status === 0" class="file-actions">
            <el-button type="danger" size="small" link :loading="resolvingRecord === record.id" @click.stop="resolveRecord(record.id)">删除</el-button>
            <el-button size="small" link @click.stop="ignoreRecord(record.id)">忽略</el-button>
          </div>
        </div>
        <div v-if="auditTotalCount > auditPageSize" class="audit-pagination">
          <el-pagination size="small" layout="prev, pager, next" :total="auditTotalCount" :page-size="auditPageSize" :current-page="auditPage" @current-change="onAuditPageChange" />
        </div>
      </div>
    </el-card>

    <el-dialog v-model="showImagePreview" title="图片预览" width="auto" :max-width="'90vw'" destroy-on-close @close="previewImage = ''">
      <div class="image-preview-container">
        <img :src="`/api/admin/image?path=${encodeURIComponent(previewImage)}`" class="image-preview-full" />
        <div class="image-preview-path">{{ previewImage }}</div>
      </div>
    </el-dialog>

    <!-- 错题查询面板 -->
    <el-card shadow="never" class="panel mistake-query-panel">
      <template #header>
        <div class="panel-header">
          <div class="panel-title">
            <el-icon><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/></svg></el-icon>
            <span>错题查询</span>
          </div>
          <el-button size="small" text @click="loadMistakeItems">
            <el-icon class="refresh-icon" :class="{ spinning: loadingMistakes }"><svg viewBox="0 0 1024 1024" width="14" height="14"><path fill="currentColor" d="M784.512 230.272v-50.56a32 32 0 1 1 64 0v149.056a32 32 0 0 1-32 32H667.52a32 32 0 1 1 0-64h92.992A362.24 362.24 0 0 0 512 149.824C296.32 149.824 121.216 325.056 121.216 540.8c0 215.68 175.104 390.848 390.784 390.848a390.208 390.208 0 0 0 338.304-194.752 32 32 0 1 1 55.36 32.256A454.144 454.144 0 0 1 512 963.648c-233.152 0-422.848-189.632-422.848-422.848S278.848 117.952 512 117.952c124.544 0 236.608 53.952 314.24 139.712l-41.728-27.392z"/></svg></el-icon>
            刷新
          </el-button>
        </div>
      </template>

      <div class="mistake-toolbar">
        <div class="mistake-filters">
          <div class="student-search-wrapper">
            <el-input
              v-model="mistakeStudentSearch"
              placeholder="搜索学生姓名..."
              size="small"
              clearable
              style="width: 160px"
              @input="searchMistakeStudent"
              @clear="clearMistakeStudent"
            >
              <template #prefix>
                <el-icon><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/></svg></el-icon>
              </template>
            </el-input>
            <div v-if="mistakeStudentResults.length > 0" class="student-search-dropdown">
              <div
                v-for="student in mistakeStudentResults"
                :key="student.id"
                class="student-search-item"
                @click="selectMistakeStudent(student)"
              >
                <span class="student-name">{{ student.name }}</span>
                <span class="student-grade">{{ getGradeLabel(student.grade) }}</span>
              </div>
            </div>
            <div v-else-if="mistakeStudentSearch && !loadingMistakeStudentSearch" class="student-search-dropdown">
              <div class="student-search-empty">未找到学生</div>
            </div>
            <div v-if="mistakeStudentName" class="selected-student-tag">
              <el-tag size="small" closable @close="clearMistakeStudent">
                {{ mistakeStudentName }}
              </el-tag>
            </div>
          </div>
          <el-select
            v-model="mistakeSubject"
            placeholder="学科"
            size="small"
            clearable
            style="width: 120px"
            @change="onMistakeFilterChange"
          >
            <el-option
              v-for="subject in subjectOptions"
              :key="subject.value"
              :label="subject.displayName"
              :value="subject.value"
            />
          </el-select>
          <el-select
            v-model="mistakeGrade"
            placeholder="年级"
            size="small"
            clearable
            style="width: 120px"
            @change="onMistakeFilterChange"
          >
            <el-option
              v-for="grade in gradeOptions"
              :key="grade.value"
              :label="grade.label"
              :value="grade.value"
            />
          </el-select>
          <el-select
            v-model="mistakeReviewStatus"
            placeholder="审核状态"
            size="small"
            clearable
            style="width: 120px"
            @change="onMistakeFilterChange"
          >
            <el-option label="待审核" :value="1" />
            <el-option label="已确认" :value="2" />
            <el-option label="已拒绝" :value="3" />
          </el-select>
          <el-button type="primary" size="small" @click="onMistakeFilterChange">查询</el-button>
          <el-button size="small" @click="clearMistakeFilters">清除</el-button>
        </div>
      </div>

      <div v-if="loadingMistakes" class="empty-state">
        <el-empty description="加载中..."></el-empty>
      </div>
      <div v-else-if="mistakeItems.length === 0" class="empty-state">
        <el-empty description="暂无错题记录"></el-empty>
      </div>
      <div v-else>
        <el-table :data="mistakeItems" v-loading="loadingMistakes" empty-text="暂无数据" class="data-table" size="small">
          <el-table-column prop="id" label="错题ID" min-width="180" show-overflow-tooltip />
          <el-table-column prop="studentName" label="学生姓名" min-width="100" show-overflow-tooltip />
          <el-table-column prop="studentId" label="学生ID" min-width="150" show-overflow-tooltip />
          <el-table-column label="学科" width="100">
            <template #default="{ row }">
              {{ getSubjectDisplayName(row.subject) }}
            </template>
          </el-table-column>
          <el-table-column label="年级" width="80">
            <template #default="{ row }">
              {{ getGradeLabel(row.grade) }}
            </template>
          </el-table-column>
          <el-table-column label="类型" width="80">
            <template #default="{ row }">
              {{ getMistakeTypeText(row.type) }}
            </template>
          </el-table-column>
          <el-table-column label="审核状态" width="100">
            <template #default="{ row }">
              <el-tag :type="getReviewStatusType(row.reviewStatus)" size="small" effect="light">
                {{ getReviewStatusText(row.reviewStatus) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="图片数量" width="100">
            <template #default="{ row }">{{ row.imageCount }}</template>
          </el-table-column>
          <el-table-column label="创建时间" min-width="150">
            <template #default="{ row }"><span class="time-text">{{ formatDate(row.createdAt) }}</span></template>
          </el-table-column>
          <el-table-column label="操作" width="180" fixed="right">
            <template #default="{ row }">
              <el-button link type="primary" size="small" @click="viewMistakeDetail(row)">查看详情</el-button>
              <el-button link type="warning" size="small" @click="openMistakeEdit(row)">编辑</el-button>
            </template>
          </el-table-column>
        </el-table>

        <div class="pagination-bar">
          <el-pagination
            background
            layout="total, sizes, prev, pager, next"
            :total="mistakeTotal"
            :page-size="mistakePageSize"
            :current-page="mistakePage"
            :page-sizes="[10, 20, 50, 100]"
            @current-change="onMistakePageChange"
          />
        </div>
      </div>
    </el-card>

    <!-- 错题详情对话框 -->
    <el-dialog v-model="showMistakeDetailDialog" title="错题详情" width="900px" top="5vh" destroy-on-close>
      <div v-if="currentMistakeDetail" class="mistake-detail">
        <el-descriptions :column="2" border size="small">
          <el-descriptions-item label="错题ID">{{ currentMistakeDetail.id }}</el-descriptions-item>
          <el-descriptions-item label="学生姓名">{{ currentMistakeDetail.studentName }}</el-descriptions-item>
          <el-descriptions-item label="学生ID">{{ currentMistakeDetail.studentId }}</el-descriptions-item>
          <el-descriptions-item label="学科">{{ getSubjectDisplayName(currentMistakeDetail.subject) }}</el-descriptions-item>
          <el-descriptions-item label="年级">{{ getGradeLabel(currentMistakeDetail.grade) }}</el-descriptions-item>
          <el-descriptions-item label="类型">{{ getMistakeTypeText(currentMistakeDetail.type) }}</el-descriptions-item>
          <el-descriptions-item label="审核状态">
            <el-tag :type="getReviewStatusType(currentMistakeDetail.reviewStatus)" size="small" effect="light">
              {{ getReviewStatusText(currentMistakeDetail.reviewStatus) }}
            </el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="审核人ID">{{ currentMistakeDetail.reviewerId || '-' }}</el-descriptions-item>
          <el-descriptions-item label="审核时间">{{ currentMistakeDetail.reviewedAt || '-' }}</el-descriptions-item>
          <el-descriptions-item label="审核备注" :span="2">{{ currentMistakeDetail.reviewComment || '-' }}</el-descriptions-item>
          <el-descriptions-item label="来源上传ID" :span="2">{{ currentMistakeDetail.sourceUploadId }}</el-descriptions-item>
          <el-descriptions-item label="创建时间">{{ currentMistakeDetail.createdAt }}</el-descriptions-item>
          <el-descriptions-item label="更新时间">{{ currentMistakeDetail.updatedAt }}</el-descriptions-item>
        </el-descriptions>
      </div>
    </el-dialog>

    <!-- 错题编辑对话框 -->
    <el-dialog v-model="showMistakeEditDialog" title="编辑错题" width="500px" top="10vh" destroy-on-close>
      <div v-if="currentMistakeEdit" class="mistake-edit">
        <el-form label-width="100px">
          <el-form-item label="学生姓名">
            <el-input v-model="currentMistakeEdit.studentName" disabled />
          </el-form-item>
          <el-form-item label="学科">
            <el-select v-model="currentMistakeEdit.subject" placeholder="请选择学科">
              <el-option
                v-for="subject in subjectOptions"
                :key="subject.value"
                :label="subject.displayName"
                :value="subject.value"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="年级">
            <el-select v-model="currentMistakeEdit.grade" placeholder="请选择年级">
              <el-option
                v-for="grade in gradeOptions"
                :key="grade.value"
                :label="grade.label"
                :value="grade.value"
              />
            </el-select>
          </el-form-item>
        </el-form>
      </div>
      <template #footer>
        <el-button @click="showMistakeEditDialog = false">取消</el-button>
        <el-button type="primary" @click="saveMistakeEdit">保存</el-button>
      </template>
    </el-dialog>

    <!-- 上传记录详情：图片查看 + 指派 -->
    <el-dialog v-model="showRecordDetailDialog" title="查看图片 & 指派" width="1200px" top="5vh" destroy-on-close>
      <div v-if="assigningRecord" class="record-detail-layout">
        <div class="record-detail-images">
          <div class="images-info">
            <span>学生: {{ assigningRecord.studentName }}</span>
            <span>共 {{ assigningRecord.imagePaths?.length || 0 }} 张图片</span>
          </div>
          <div class="images-grid">
            <div
              v-for="(path, index) in assigningRecord.imagePaths"
              :key="index"
              class="image-item"
            >
              <div class="image-wrapper">
                <img :src="`/api/admin/image?path=${encodeURIComponent(path)}`" :alt="`图片${index + 1}`" loading="lazy" @click="previewImage = path" />
              </div>
              <div class="image-actions">
                <el-button
                  size="default"
                  type="primary"
                  :loading="loadingRotation"
                  @click.stop="rotateImage(index, -90)"
                >
                  <el-icon><svg viewBox="0 0 1024 1024" width="18" height="18"><path fill="currentColor" d="M768 341.333333a10.666667 10.666667 0 0 0-10.666666-10.666666H554.666666c-41.365333 0-74.666666 33.301333-74.666666 74.666666v202.666667a10.666667 10.666667 0 1 0 21.333333 0V405.333333c0-29.376 23.957333-53.333333 53.333333-53.333333h192a10.666667 10.666667 0 0 0 10.666667-10.666667zM512 64c245.76 0 448 202.24 448 448s-202.24 448-448 448S64 757.76 64 512 266.24 64 512 64z m0 85.333333c-199.146667 0-362.666667 163.52-362.666667 362.666667s163.52 362.666667 362.666667 362.666667 362.666667-163.52 362.666667-362.666667S711.146667 149.333333 512 149.333333z"/></svg></el-icon>
                  <span style="margin-left:4px">左旋</span>
                </el-button>
                <el-button
                  size="default"
                  type="success"
                  :loading="loadingRotation"
                  @click.stop="rotateImage(index, 90)"
                >
                  <span style="margin-right:4px">右旋</span>
                  <el-icon><svg viewBox="0 0 1024 1024" width="18" height="18"><path fill="currentColor" d="M256 341.333333a10.666667 10.666667 0 0 1 10.666667-10.666666h202.666666c41.365333 0 74.666667 33.301333 74.666667 74.666666v202.666667a10.666667 10.666667 0 0 1-21.333333 0V405.333333c0-29.376-23.957333-53.333333-53.333333-53.333333H266.666667a10.666667 10.666667 0 0 1-10.666667-10.666667zM512 64c245.76 0 448 202.24 448 448s-202.24 448-448 448S64 757.76 64 512 266.24 64 512 64z m0 85.333333c-199.146667 0-362.666667 163.52-362.666667 362.666667s163.52 362.666667 362.666667 362.666667 362.666667-163.52 362.666667-362.666667S711.146667 149.333333 512 149.333333z"/></svg></el-icon>
                </el-button>
              </div>
            </div>
          </div>
        </div>
        <div class="record-detail-assign">
          <h4 class="assign-title">指派信息</h4>
          <el-form label-width="80px" size="default">
            <el-form-item label="分类">
              <el-select v-model="assignForm.classification" style="width: 100%">
                <el-option
                  v-for="cls in classificationOptions"
                  :key="cls.value"
                  :value="cls.value"
                  :label="cls.displayName"
                />
              </el-select>
            </el-form-item>
            <el-form-item label="学科">
              <el-select v-model="assignForm.subject" style="width: 100%">
                <el-option
                  v-for="subject in subjectOptions"
                  :key="subject.value"
                  :value="subject.value"
                  :label="subject.displayName"
                />
              </el-select>
            </el-form-item>
            <el-form-item label="年级">
              <el-select v-model="assignForm.grade" style="width: 100%">
                <el-option
                  v-for="grade in gradeOptions"
                  :key="grade.value"
                  :value="grade.value"
                  :label="grade.label"
                />
              </el-select>
            </el-form-item>
          </el-form>
          <div class="assign-actions">
            <el-button type="primary" :loading="loadingAssign" @click="doAssign">确认指派</el-button>
          </div>
          
          <h4 class="assign-title" style="margin-top:24px">关联错题</h4>
          <div v-if="loadingRecordMistakes" class="hint-text">加载中...</div>
          <div v-else-if="recordMistakes.length === 0" class="hint-text">暂无关联错题</div>
          <div v-else class="mistakes-list">
            <div v-for="mistake in recordMistakes" :key="mistake.id" class="mistake-item">
              <div class="mistake-info">
                <div class="mistake-subject">
                  {{ getSubjectDisplayName(mistake.subject) }}
                  <el-tag size="small" :type="mistake.reviewStatus === 1 ? 'warning' : mistake.reviewStatus === 2 ? 'success' : 'danger'">
                    {{ mistake.reviewStatus === 1 ? '待审核' : mistake.reviewStatus === 2 ? '已确认' : '已拒绝' }}
                  </el-tag>
                </div>
                <div class="mistake-time">创建于: {{ formatDate(mistake.createdAt) }}</div>
              </div>
            </div>
          </div>
        </div>
      </div>
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

/* ========== 上传记录详情对话框样式 ========== */
.record-detail-layout {
  display: flex;
  gap: 24px;
  height: calc(85vh - 120px);
  min-height: 400px;
}

.record-detail-images {
  flex: 1;
  min-width: 0;
  overflow-y: auto;
  padding-right: 8px;
}

.record-detail-assign {
  width: 280px;
  flex-shrink: 0;
  border-left: 1px solid #e5e7eb;
  padding-left: 24px;
  position: sticky;
  top: 0;
  align-self: flex-start;
}

.assign-title {
  margin: 0 0 16px;
  font-size: 16px;
  font-weight: 600;
  color: #1f2937;
}

.assign-actions {
  margin-top: 20px;
  padding-top: 16px;
  border-top: 1px solid #f3f4f6;
}

.hint-text {
  font-size: 13px;
  color: #6b7280;
  margin: 8px 0;
}

.mistakes-list {
  max-height: 200px;
  overflow-y: auto;
}

.mistake-item {
  padding: 10px 12px;
  background: #f9fafb;
  border-radius: 6px;
  margin-bottom: 8px;
}

.mistake-info {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.mistake-subject {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 500;
  color: #1f2937;
  font-size: 13px;
}

.mistake-time {
  font-size: 11px;
  color: #6b7280;
}

.images-info {
  display: flex;
  justify-content: space-between;
  margin-bottom: 16px;
  padding: 8px 12px;
  background: #f9fafb;
  border-radius: 6px;
  font-size: 14px;
  color: #4b5563;
}

.images-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: 10px;
}

.image-item {
  aspect-ratio: 3/4;
  border-radius: 8px;
  overflow: hidden;
  cursor: pointer;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
  transition: transform 0.2s ease, box-shadow 0.2s ease;
  background: #f9fafb;
  position: relative;
  min-height: 200px;
}

.image-item:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
}

.image-wrapper {
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #fff;
  transition: transform 0.3s ease;
}

.image-wrapper img {
  width: 100%;
  height: 100%;
  object-fit: contain;
  background: #fff;
  max-width: 100%;
  max-height: 100%;
}

.image-actions {
  position: absolute;
  bottom: 8px;
  right: 8px;
  display: flex;
  gap: 12px;
  opacity: 0;
  transition: opacity 0.2s ease;
}

.image-item:hover .image-actions {
  opacity: 1;
}

@media (max-width: 900px) {
  .record-detail-layout {
    flex-direction: column;
    height: auto;
    max-height: calc(85vh - 120px);
  }
  .record-detail-images {
    max-height: 50vh;
  }
  .record-detail-assign {
    width: 100%;
    border-left: none;
    padding-left: 0;
    border-top: 1px solid #e5e7eb;
    padding-top: 16px;
    position: static;
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
}

/* ========== OSS Audit ========== */
.empty-state {
  padding: 40px;
  text-align: center;
}

.audit-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 8px;
}

.mistake-toolbar {
  margin-bottom: 16px;
}

.mistake-filters {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  align-items: flex-start;
}

.mistake-detail {
  padding: 8px 0;
}

.student-search-wrapper {
  position: relative;
}

.student-search-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  z-index: 1000;
  min-width: 200px;
  max-height: 240px;
  overflow-y: auto;
  background: white;
  border: 1px solid #e4e7ed;
  border-radius: 4px;
  box-shadow: 0 2px 12px 0 rgba(0, 0, 0, 0.1);
  margin-top: 4px;
}

.student-search-item {
  padding: 8px 12px;
  cursor: pointer;
  display: flex;
  justify-content: space-between;
  align-items: center;
  transition: background-color 0.15s;
}

.student-search-item:hover {
  background-color: #f5f7fa;
}

.student-name {
  font-weight: 500;
  color: #303133;
}

.student-grade {
  font-size: 12px;
  color: #909399;
}

.student-search-empty {
  padding: 12px;
  text-align: center;
  color: #909399;
  font-size: 13px;
}

.selected-student-tag {
  margin-top: 4px;
}

@media (max-width: 768px) {
  .app-shell {
    padding: 12px;
  }

  .mistake-filters {
    flex-direction: column;
    align-items: stretch;
  }

  .mistake-filters .el-input,
  .mistake-filters .el-select {
    width: 100% !important;
  }
}

.mistake-detail {
  padding: 8px 0;
}

@media (max-width: 768px) {
  .mistake-filters {
    flex-direction: column;
    align-items: stretch;
  }

  .mistake-filters .el-input,
  .mistake-filters .el-select {
    width: 100% !important;
  }
}

@media (max-width: 768px) {
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

.audit-filters {
  display: flex;
  gap: 8px;
}

.audit-stats {
  display: flex;
  gap: 6px;
}

.audit-record-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.audit-record-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border-radius: 8px;
  border: 1px solid #e4e7ed;
  transition: all 0.2s;
}

.audit-record-item.selected {
  border-color: #409eff;
  background: #ecf5ff;
}

.audit-record-item.resolved {
  opacity: 0.6;
}

.status-tag {
  min-width: 56px;
  text-align: center;
}

.file-actions {
  display: flex;
  gap: 4px;
  flex-shrink: 0;
}

.audit-pagination {
  display: flex;
  justify-content: center;
  margin-top: 16px;
}

.bulk-action-bar {
  background: #fef2f2;
  border: 1px solid #fecaca;
  padding: 12px 16px;
  border-radius: 8px;
  margin-bottom: 16px;
  display: flex;
  align-items: center;
}

.file-thumbnail {
  width: 48px;
  height: 48px;
  border-radius: 6px;
  overflow: hidden;
  flex-shrink: 0;
  border: 1px solid #e5e7eb;
  background: #f9fafb;
}

.file-thumbnail img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  cursor: pointer;
  transition: transform 0.2s ease;
}

.file-thumbnail img:hover {
  transform: scale(1.05);
}

.file-thumbnail-placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  color: #9ca3af;
}

.file-info {
  flex: 1;
  min-width: 0;
}

.file-path {
  font-size: 13px;
  color: #1f2937;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  font-family: 'Courier New', monospace;
}

.file-meta {
  font-size: 12px;
  color: #6b7280;
  margin-top: 2px;
}

.image-preview-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
}

.image-preview-full {
  max-width: 80vw;
  max-height: 70vh;
  object-fit: contain;
  border-radius: 8px;
}

.image-preview-path {
  font-size: 12px;
  color: #6b7280;
  word-break: break-all;
  text-align: center;
  font-family: 'Courier New', monospace;
}

@media (max-width: 768px) {
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
