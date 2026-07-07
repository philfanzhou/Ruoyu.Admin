<template>
  <div class="assistant-view">
    <el-card shadow="never">
      <template #header>
        <div class="card-header">
          <span>助教管理</span>
          <el-button type="primary" size="small" @click="showGrantDialog = true">
            授予助教权限
          </el-button>
        </div>
      </template>

      <el-table
        :data="assistants"
        v-loading="loading"
        size="small"
      >
        <el-table-column prop="userId" label="User ID" width="220" show-overflow-tooltip />
        <el-table-column prop="phone" label="手机号" width="120" />
        <el-table-column prop="username" label="用户名" width="120" />
        <el-table-column label="备注" min-width="150">
          <template #default="{ row }">
            {{ getUserRemark(row.userId) }}
          </template>
        </el-table-column>
        <el-table-column label="科目" min-width="200">
          <template #default="{ row }">
            <el-tag
              v-for="subjectId in row.subjects"
              :key="subjectId"
              size="small"
              style="margin-right: 4px; margin-bottom: 2px"
              closable
              @close="removeSubject(row, subjectId)"
            >
              {{ getSubjectName(subjectId) }}
            </el-tag>
            <el-button
              size="small"
              link
              type="primary"
              @click="showSubjectsDialog(row)"
            >
              + 管理
            </el-button>
          </template>
        </el-table-column>
        <el-table-column label="学生" min-width="160">
          <template #default="{ row }">
            <el-tag type="info" size="small" style="margin-right: 4px">
              {{ getStudentCount(row.userId) }} 人
            </el-tag>
            <el-button
              size="small"
              link
              type="primary"
              @click="showStudentsDialog(row)"
            >
              管理学生
            </el-button>
          </template>
        </el-table-column>
        <el-table-column prop="createdAt" label="创建时间" width="180">
          <template #default="{ row }">
            {{ formatDate(row.createdAt) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="120" fixed="right">
          <template #default="{ row }">
            <el-button link type="danger" size="small" @click="revokeAssistant(row)">
              撤销权限
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- Grant dialog -->
    <el-dialog v-model="showGrantDialog" title="授予助教权限" width="600px">
      <el-form :model="grantForm" label-width="100px">
        <el-form-item label="搜索用户">
          <el-input
            v-model="grantForm.keyword"
            placeholder="输入 username 或手机号搜索"
            clearable
            @input="searchUsers"
          />
        </el-form-item>
        <el-form-item label="选择用户" v-if="searchResults.length > 0">
          <el-select
            v-model="grantForm.selectedUserId"
            placeholder="请选择用户"
            style="width: 100%"
            @change="onUserSelect"
          >
            <el-option
              v-for="user in searchResults"
              :key="user.userId"
              :label="`${user.username} (${user.phone || '无手机'})`"
              :value="user.userId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="选择科目">
          <el-checkbox-group v-model="grantForm.selectedSubjects">
            <el-checkbox
              v-for="subject in availableSubjects"
              :key="subject.value"
              :label="subject.value"
              style="width: 120px; margin: 8px 0"
            >
              {{ subject.name }}
            </el-checkbox>
          </el-checkbox-group>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="closeGrantDialog">取消</el-button>
        <el-button type="primary" :loading="granting" @click="grantAssistant">授予</el-button>
      </template>
    </el-dialog>

    <!-- Subjects management dialog -->
    <el-dialog v-model="showSubjectsDialogVisible" title="管理助教科目" width="600px">
      <div v-if="currentAssistant">
        <p style="margin-bottom: 16px">
          助教：{{ currentAssistant.username || currentAssistant.userId }}
        </p>
        <el-checkbox-group v-model="selectedSubjects">
          <el-checkbox
            v-for="subject in availableSubjects"
            :key="subject.value"
            :label="subject.value"
            style="width: 120px; margin: 8px 0"
          >
            {{ subject.name }}
          </el-checkbox>
        </el-checkbox-group>
      </div>
      <template #footer>
        <el-button @click="showSubjectsDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="savingSubjects" @click="saveAssistantSubjects">保存</el-button>
      </template>
    </el-dialog>

    <!-- Students management dialog -->
    <el-dialog v-model="showStudentsDialogVisible" title="管理关联学生" width="700px">
      <div v-if="currentAssistant">
        <p style="margin-bottom: 16px">
          助教：{{ currentAssistant.username || currentAssistant.userId }}
        </p>

        <!-- Current students list -->
        <div style="margin-bottom: 16px">
          <h4 style="margin: 0 0 8px">已关联学生</h4>
          <el-table :data="currentStudentDetails" size="small" v-loading="loadingStudents" max-height="240">
            <el-table-column prop="name" label="姓名" width="120" />
            <el-table-column label="年级" width="140">
              <template #default="{ row }">
                {{ getGradeLabel(row.grade) }}
              </template>
            </el-table-column>
            <el-table-column prop="id" label="ID" show-overflow-tooltip />
            <el-table-column label="操作" width="80">
              <template #default="{ row }">
                <el-button link type="danger" size="small" @click="removeStudent(row.id)">移除</el-button>
              </template>
            </el-table-column>
          </el-table>
          <el-empty v-if="!loadingStudents && currentStudentDetails.length === 0" description="暂无关联学生" :image-size="40" />
        </div>

        <!-- Search & add students -->
        <div>
          <h4 style="margin: 0 0 8px">添加学生</h4>
          <div style="display: flex; gap: 8px; margin-bottom: 8px">
            <el-input
              v-model="studentSearchKeyword"
              placeholder="输入学生姓名搜索"
              clearable
              size="small"
              style="width: 200px"
              @input="searchStudents"
            />
            <el-select
              v-model="studentSearchGrade"
              placeholder="筛选年级"
              clearable
              size="small"
              style="width: 150px"
              @change="searchStudents"
            >
              <el-option
                v-for="g in gradeOptions"
                :key="g.value"
                :label="g.label"
                :value="g.value"
              />
            </el-select>
          </div>
          <el-table :data="studentSearchResults" size="small" v-loading="searchingStudents" max-height="200">
            <el-table-column prop="name" label="姓名" width="120" />
            <el-table-column label="年级" width="140">
              <template #default="{ row }">
                {{ getGradeLabel(row.grade) }}
              </template>
            </el-table-column>
            <el-table-column prop="id" label="ID" show-overflow-tooltip />
            <el-table-column label="操作" width="80">
              <template #default="{ row }">
                <el-button
                  link
                  type="primary"
                  size="small"
                  :disabled="isStudentAlreadyLinked(row.id)"
                  @click="addStudent(row.id)"
                >
                  {{ isStudentAlreadyLinked(row.id) ? '已关联' : '添加' }}
                </el-button>
              </template>
            </el-table-column>
          </el-table>
        </div>
      </div>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { assistantPortalClient, type AssistantAccountDto, type SubjectOption } from '../services/assistantPortalApi'
import { getIdentityAdminApiClient, type IdentityUser } from '../services/identityApi'
import { studentAdminClient, type StudentDto, type GradeOption } from '../services/studentAdminApi'

const assistants = ref<AssistantAccountDto[]>([])
const loading = ref(false)
const identityUserMap = ref<Record<string, IdentityUser>>({})

const availableSubjects = ref<SubjectOption[]>([])

const showGrantDialog = ref(false)
const grantForm = ref({
  keyword: '',
  selectedUserId: '',
  selectedSubjects: [] as number[]
})
const searchResults = ref<IdentityUser[]>([])
const granting = ref(false)

const showSubjectsDialogVisible = ref(false)
const currentAssistant = ref<AssistantAccountDto | null>(null)
const selectedSubjects = ref<number[]>([])
const savingSubjects = ref(false)

// Student management
const showStudentsDialogVisible = ref(false)
const currentStudentIds = ref<string[]>([])
const currentStudentDetails = ref<StudentDto[]>([])
const loadingStudents = ref(false)
const gradeOptions = ref<GradeOption[]>([])

const studentSearchKeyword = ref('')
const studentSearchGrade = ref<number | undefined>(undefined)
const studentSearchResults = ref<StudentDto[]>([])
const searchingStudents = ref(false)

const gradeLabels: Record<number, string> = {
  1: '小学一年级', 2: '小学二年级', 3: '小学三年级',
  4: '小学四年级', 5: '小学五年级', 6: '小学六年级',
  7: '初中一年级', 8: '初中二年级', 9: '初中三年级',
  10: '高中一年级', 11: '高中二年级', 12: '高中三年级',
}

function getGradeLabel(grade: number): string {
  return gradeLabels[grade] || `年级${grade}`
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

function getUserRemark(userId: string | null): string {
  if (!userId) return '-'
  return identityUserMap.value[userId]?.remark || '-'
}

async function loadAvailableSubjects() {
  try {
    const result = await assistantPortalClient.getAvailableSubjects()
    availableSubjects.value = result.data
  } catch (error) {
    console.error('Failed to load subjects:', error)
  }
}

async function loadGradeOptions() {
  try {
    const result = await studentAdminClient.getGrades()
    gradeOptions.value = result
  } catch (error) {
    console.error('Failed to load grade options:', error)
  }
}

function getSubjectName(subjectId: number): string {
  const subject = availableSubjects.value.find(s => s.value === subjectId)
  return subject?.name || `科目${subjectId}`
}

function formatDate(dateStr: string | number): string {
  if (!dateStr) return '-'
  if (typeof dateStr === 'string') {
    if (/^\d+$/.test(dateStr.trim())) {
      return new Date(Number(dateStr) * 1000).toLocaleString('zh-CN', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit'
      })
    }
    const date = new Date(dateStr)
    return date.toLocaleString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit'
    })
  }
  return new Date(dateStr * 1000).toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  })
}

function getStudentCount(userId: string | null): number {
  if (!userId) return 0
  // We load student counts on demand, but for the table display we store them
  // The count will be populated when the students dialog is opened
  return assistantStudentCounts.value[userId] ?? 0
}

const assistantStudentCounts = ref<Record<string, number>>({})

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
      pageSize: 20
    })
    searchResults.value = result.items
  } catch (error) {
    console.error('Search users failed:', error)
    ElMessage.error('搜索用户失败')
  }
}

function onUserSelect() {
  grantForm.value.selectedSubjects = []
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
      subjects: grantForm.value.selectedSubjects
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

function closeGrantDialog() {
  showGrantDialog.value = false
  grantForm.value = {
    keyword: '',
    selectedUserId: '',
    selectedSubjects: []
  }
  searchResults.value = []
}

async function revokeAssistant(assistant: AssistantAccountDto) {
  if (!assistant.userId) {
    ElMessage.warning('该助教没有关联的用户ID')
    return
  }

  try {
    await ElMessageBox.confirm(
      `确认撤销 ${assistant.username || assistant.userId} 的助教权限？关联的学生也将被清除。`,
      '确认撤销',
      { type: 'warning' }
    )
    await assistantPortalClient.revokeAssistant(assistant.userId)
    ElMessage.success('撤销权限成功')
    await loadAssistants()
    await loadStudentCounts()
  } catch (error: any) {
    if (error !== 'cancel') {
      ElMessage.error(error.response?.data?.message || '撤销权限失败')
    }
  }
}

async function showSubjectsDialog(assistant: AssistantAccountDto) {
  currentAssistant.value = assistant
  selectedSubjects.value = [...assistant.subjects]
  showSubjectsDialogVisible.value = true
}

async function removeSubject(assistant: AssistantAccountDto, subjectId: number) {
  if (!assistant.userId) {
    ElMessage.warning('该助教没有关联的用户ID')
    return
  }

  try {
    await assistantPortalClient.removeAssistantSubject(assistant.userId, subjectId)
    ElMessage.success('删除科目成功')
    await loadAssistants()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '删除科目失败')
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

// Student management methods
async function showStudentsDialog(assistant: AssistantAccountDto) {
  currentAssistant.value = assistant
  showStudentsDialogVisible.value = true
  studentSearchKeyword.value = ''
  studentSearchGrade.value = undefined
  studentSearchResults.value = []
  await loadAssistantStudents(assistant.userId)
}

async function loadAssistantStudents(userId: string | null) {
  if (!userId) return
  loadingStudents.value = true
  currentStudentDetails.value = []
  try {
    const result = await assistantPortalClient.getAssistantStudents(userId)
    currentStudentIds.value = result.data

    if (currentStudentIds.value.length > 0) {
      // Fetch student details from admin student API
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
    // Update the count cache
    assistantStudentCounts.value[userId] = currentStudentIds.value.length
  } catch (error) {
    console.error('Failed to load assistant students:', error)
    ElMessage.error('加载学生列表失败')
  } finally {
    loadingStudents.value = false
  }
}

function isStudentAlreadyLinked(studentId: string): boolean {
  return currentStudentIds.value.includes(studentId)
}

async function addStudent(studentId: string) {
  if (!currentAssistant.value?.userId) return
  try {
    await assistantPortalClient.addAssistantStudent(currentAssistant.value.userId, studentId)
    ElMessage.success('添加成功')
    await loadAssistantStudents(currentAssistant.value.userId)
    // Refresh search results to update "已关联" status
    await searchStudents()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '添加失败')
  }
}

async function removeStudent(studentId: string) {
  if (!currentAssistant.value?.userId) return
  try {
    await assistantPortalClient.removeAssistantStudent(currentAssistant.value.userId, studentId)
    ElMessage.success('移除成功')
    await loadAssistantStudents(currentAssistant.value.userId)
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '移除失败')
  }
}

async function searchStudents() {
  if (!studentSearchKeyword.value || studentSearchKeyword.value.length < 1) {
    if (studentSearchGrade.value === undefined) {
      studentSearchResults.value = []
      return
    }
  }

  searchingStudents.value = true
  try {
    const result = await studentAdminClient.getStudents({
      name: studentSearchKeyword.value || undefined,
      grade: studentSearchGrade.value,
      page: 1,
      pageSize: 20
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
  await loadAvailableSubjects()
  await loadGradeOptions()
  await loadAssistants()
  await loadStudentCounts()
})
</script>

<style scoped>
.assistant-view {
  max-width: 1400px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
</style>
