<template>
  <div class="teacher-view">
    <el-card shadow="never">
      <template #header>
        <div class="card-header">
          <span>教师管理</span>
          <el-button type="primary" size="small" @click="showGrantDialog = true">
            授予教师权限
          </el-button>
        </div>
      </template>

      <el-table
        :data="teachers"
        v-loading="loading"
        size="small"
      >
        <el-table-column prop="userId" label="User ID" width="220" show-overflow-tooltip />
        <el-table-column prop="phone" label="手机号" width="120" />
        <el-table-column prop="username" label="用户名" width="120" />
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
        <el-table-column prop="createdAt" label="创建时间" width="180">
          <template #default="{ row }">
            {{ formatDate(row.createdAt) }}
          </template>
        </el-table-column>
        <el-table-column prop="updatedAt" label="更新时间" width="180">
          <template #default="{ row }">
            {{ formatDate(row.updatedAt) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="120" fixed="right">
          <template #default="{ row }">
            <el-button link type="danger" size="small" @click="revokeTeacher(row)">
              撤销权限
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="showGrantDialog" title="授予教师权限" width="600px">
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
        <el-button type="primary" :loading="granting" @click="grantTeacher">授予</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="showSubjectsDialogVisible" title="管理教师科目" width="600px">
      <div v-if="currentTeacher">
        <p style="margin-bottom: 16px">
          教师：{{ currentTeacher.username || currentTeacher.userId }}
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
        <el-button type="primary" :loading="savingSubjects" @click="saveTeacherSubjects">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { teacherPortalClient, type TeacherAccountDto, type SubjectOption } from '../services/teacherPortalApi'
import { getIdentityAdminApiClient, type IdentityUser } from '../services/identityApi'

const teachers = ref<TeacherAccountDto[]>([])
const loading = ref(false)

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
const currentTeacher = ref<TeacherAccountDto | null>(null)
const selectedSubjects = ref<number[]>([])
const savingSubjects = ref(false)

async function loadTeachers() {
  loading.value = true
  try {
    const result = await teacherPortalClient.getTeachers()
    teachers.value = result.data
  } catch (error) {
    ElMessage.error('加载教师列表失败')
  } finally {
    loading.value = false
  }
}

async function loadAvailableSubjects() {
  try {
    const result = await teacherPortalClient.getAvailableSubjects()
    availableSubjects.value = result.data
  } catch (error) {
    console.error('Failed to load subjects:', error)
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

async function grantTeacher() {
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
    await teacherPortalClient.grantTeacher(grantForm.value.selectedUserId, {
      subjects: grantForm.value.selectedSubjects
    })
    ElMessage.success('授予权限成功')
    closeGrantDialog()
    await loadTeachers()
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

async function revokeTeacher(teacher: TeacherAccountDto) {
  if (!teacher.userId) {
    ElMessage.warning('该教师没有关联的用户ID')
    return
  }

  try {
    await ElMessageBox.confirm(
      `确认撤销 ${teacher.username || teacher.userId} 的教师权限？`,
      '确认撤销',
      { type: 'warning' }
    )
    await teacherPortalClient.removeTeacherByUserId(teacher.userId)
    ElMessage.success('撤销权限成功')
    await loadTeachers()
  } catch (error: any) {
    if (error !== 'cancel') {
      ElMessage.error(error.response?.data?.message || '撤销权限失败')
    }
  }
}

async function showSubjectsDialog(teacher: TeacherAccountDto) {
  currentTeacher.value = teacher
  selectedSubjects.value = [...teacher.subjects]
  showSubjectsDialogVisible.value = true
}

async function removeSubject(teacher: TeacherAccountDto, subjectId: number) {
  if (!teacher.userId) {
    ElMessage.warning('该教师没有关联的用户ID')
    return
  }

  try {
    await teacherPortalClient.removeTeacherSubject(teacher.userId, subjectId)
    ElMessage.success('删除科目成功')
    await loadTeachers()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '删除科目失败')
  }
}

async function saveTeacherSubjects() {
  if (!currentTeacher.value?.userId) {
    ElMessage.warning('该教师没有关联的用户ID')
    return
  }

  savingSubjects.value = true
  try {
    await teacherPortalClient.setTeacherSubjects(currentTeacher.value.userId, selectedSubjects.value)
    ElMessage.success('保存成功')
    showSubjectsDialogVisible.value = false
    await loadTeachers()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '保存失败')
  } finally {
    savingSubjects.value = false
  }
}

onMounted(async () => {
  await loadAvailableSubjects()
  await loadTeachers()
})
</script>

<style scoped>
.teacher-view {
  max-width: 1400px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
</style>
