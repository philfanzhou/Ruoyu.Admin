<template>
  <div class="student-view">
    <el-card shadow="never">
      <template #header>
        <div class="card-header">
          <span>学生管理</span>
          <el-button type="primary" size="small" @click="showCreateDialog = true">
            创建学生
          </el-button>
        </div>
      </template>

      <div class="search-bar">
        <el-input
          v-model="searchName"
          placeholder="搜索学生姓名"
          style="width: 200px; margin-right: 8px"
          clearable
          @keyup.enter="loadStudents"
        />
        <el-select
          v-model="searchGrade"
          placeholder="年级"
          style="width: 150px; margin-right: 8px"
          clearable
        >
          <el-option
            v-for="grade in gradeOptions"
            :key="grade.value"
            :label="grade.label"
            :value="grade.value"
          />
        </el-select>
        <el-button type="primary" @click="loadStudents">搜索</el-button>
      </div>

      <el-table
        :data="students"
        v-loading="loading"
        size="small"
        style="margin-top: 16px"
      >
        <el-table-column prop="id" label="ID" width="220" show-overflow-tooltip />
        <el-table-column prop="name" label="姓名" width="120" />
        <el-table-column label="年级" width="100">
          <template #default="{ row }">
            {{ getGradeLabel(row.grade) }}
          </template>
        </el-table-column>
        <el-table-column label="关联账户" min-width="200">
          <template #default="{ row }">
            <el-tag
              v-for="accountId in row.identityAccountIds"
              :key="accountId"
              size="small"
              style="margin-right: 4px"
              closable
              @close="unlinkAccount(row.id, accountId)"
            >
              {{ accountId }}
            </el-tag>
            <el-button
              size="small"
              link
              type="primary"
              @click="showLinkDialog(row)"
            >
              + 添加
            </el-button>
          </template>
        </el-table-column>
        <el-table-column label="开放学科" width="150">
          <template #default="{ row }">
            <el-button size="small" link @click="showOpenSubjectsDialog(row)">
              管理
            </el-button>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="editStudent(row)">
              编辑
            </el-button>
            <el-button link type="danger" size="small" @click="deleteStudent(row.id)">
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="pagination-bar">
        <el-pagination
          background
          layout="total, prev, pager, next"
          :total="total"
          :page-size="pageSize"
          :current-page="page"
          @current-change="onPageChange"
        />
      </div>
    </el-card>

    <el-dialog v-model="showCreateDialog" title="创建学生" width="500px">
      <el-form :model="createForm" label-width="80px">
        <el-form-item label="姓名">
          <el-input v-model="createForm.name" />
        </el-form-item>
        <el-form-item label="年级">
          <el-select v-model="createForm.grade" style="width: 100%">
            <el-option
              v-for="grade in gradeOptions"
              :key="grade.value"
              :label="grade.label"
              :value="grade.value"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showCreateDialog = false">取消</el-button>
        <el-button type="primary" @click="createStudent">创建</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="showEditDialog" title="编辑学生" width="500px">
      <el-form :model="editForm" label-width="80px">
        <el-form-item label="姓名">
          <el-input v-model="editForm.name" />
        </el-form-item>
        <el-form-item label="年级">
          <el-select v-model="editForm.grade" style="width: 100%">
            <el-option
              v-for="grade in gradeOptions"
              :key="grade.value"
              :label="grade.label"
              :value="grade.value"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showEditDialog = false">取消</el-button>
        <el-button type="primary" @click="saveEdit">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="showLinkDialogVisible" title="关联账户" width="500px">
      <el-form :model="linkForm" label-width="80px">
        <el-form-item label="搜索用户">
          <el-input
            v-model="linkForm.keyword"
            placeholder="输入 username 或手机号"
            @input="searchIdentityUsers"
          />
        </el-form-item>
        <el-form-item label="选择用户">
          <el-select v-model="linkForm.selectedUserId" style="width: 100%">
            <el-option
              v-for="user in identityUsers"
              :key="user.userId"
              :label="`${user.username} (${user.phone || '无手机'})`"
              :value="user.userId"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showLinkDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="linkAccount">关联</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="showOpenSubjectsDialogVisible" title="管理开放学科" width="600px">
      <div v-if="currentStudent">
        <p style="margin-bottom: 16px">学生：{{ currentStudent.name }}</p>
        <el-checkbox-group v-model="openSubjects">
          <el-checkbox
            v-for="subject in subjectOptions"
            :key="subject.value"
            :label="subject.value"
            style="width: 120px; margin: 8px 0"
          >
            {{ subject.label }}
          </el-checkbox>
        </el-checkbox-group>
      </div>
      <template #footer>
        <el-button @click="showOpenSubjectsDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="saveOpenSubjects">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import studentAdminApi from '../services/studentAdminApi'
import identityApi from '../services/identityApi'

const students = ref<any[]>([])
const loading = ref(false)
const total = ref(0)
const page = ref(1)
const pageSize = 20

const searchName = ref('')
const searchGrade = ref<number>()

const gradeOptions = ref<any[]>([])
const subjectOptions = ref<any[]>([])

const showCreateDialog = ref(false)
const createForm = ref({ name: '', grade: 0 })

const showEditDialog = ref(false)
const editForm = ref({ id: '', name: '', grade: 0 })

const showLinkDialogVisible = ref(false)
const currentStudentId = ref('')
const linkForm = ref({ keyword: '', selectedUserId: '' })
const identityUsers = ref<any[]>([])

const showOpenSubjectsDialogVisible = ref(false)
const currentStudent = ref<any>(null)
const openSubjects = ref<number[]>([])

async function loadGradeOptions() {
  try {
    const grades = await studentAdminApi.getGrades()
    gradeOptions.value = grades.map((g: any) => ({ value: g.value, label: g.label }))
  } catch (error) {
    console.error('Failed to load grades:', error)
  }
}

async function loadSubjectOptions() {
  try {
    const subjects = await studentAdminApi.getSubjectOptions()
    subjectOptions.value = subjects.map((s: any) => ({ value: s.value, label: s.label }))
  } catch (error) {
    console.error('Failed to load subjects:', error)
  }
}

async function loadStudents() {
  loading.value = true
  try {
    const result = await studentAdminApi.getStudents({
      page: page.value,
      pageSize,
      name: searchName.value || undefined,
      grade: searchGrade.value
    })
    students.value = result.items
    total.value = result.total
  } catch (error) {
    ElMessage.error('加载学生列表失败')
  } finally {
    loading.value = false
  }
}

function getGradeLabel(grade: number) {
  return gradeOptions.value.find(g => g.value === grade)?.label || `年级${grade}`
}

async function createStudent() {
  try {
    await studentAdminApi.createStudent(createForm.value)
    ElMessage.success('创建成功')
    showCreateDialog.value = false
    createForm.value = { name: '', grade: 0 }
    await loadStudents()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '创建失败')
  }
}

function editStudent(student: any) {
  editForm.value = {
    id: student.id,
    name: student.name,
    grade: student.grade
  }
  showEditDialog.value = true
}

async function saveEdit() {
  try {
    await studentAdminApi.updateStudent(editForm.value.id, {
      name: editForm.value.name,
      grade: editForm.value.grade
    })
    ElMessage.success('更新成功')
    showEditDialog.value = false
    await loadStudents()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '更新失败')
  }
}

async function deleteStudent(id: string) {
  try {
    await ElMessageBox.confirm('确认删除该学生？', '确认', { type: 'warning' })
    await studentAdminApi.deleteStudent(id)
    ElMessage.success('删除成功')
    await loadStudents()
  } catch (error: any) {
    if (error !== 'cancel') {
      ElMessage.error(error.response?.data?.message || '删除失败')
    }
  }
}

function showLinkDialog(student: any) {
  currentStudentId.value = student.id
  linkForm.value = { keyword: '', selectedUserId: '' }
  identityUsers.value = []
  showLinkDialogVisible.value = true
}

async function searchIdentityUsers() {
  if (!linkForm.value.keyword || linkForm.value.keyword.length < 2) {
    return
  }
  try {
    const users = await identityApi.getUsers({ keyword: linkForm.value.keyword })
    identityUsers.value = users
  } catch (error) {
    console.error('Search users failed:', error)
  }
}

async function linkAccount() {
  if (!linkForm.value.selectedUserId) {
    ElMessage.warning('请选择用户')
    return
  }
  try {
    await studentAdminApi.linkIdentityAccount(currentStudentId.value, linkForm.value.selectedUserId)
    ElMessage.success('关联成功')
    showLinkDialogVisible.value = false
    await loadStudents()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '关联失败')
  }
}

async function unlinkAccount(studentId: string, accountId: string) {
  try {
    await studentAdminApi.unlinkIdentityAccount(studentId, accountId)
    ElMessage.success('取消关联成功')
    await loadStudents()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '取消关联失败')
  }
}

function showOpenSubjectsDialog(student: any) {
  currentStudent.value = student
  openSubjects.value = []
  studentAdminApi.getStudentOpenSubjects(student.id, true).then((subjects: any) => {
    openSubjects.value = subjects.map((s: any) => s.subject)
  })
  showOpenSubjectsDialogVisible.value = true
}

async function saveOpenSubjects() {
  try {
    await studentAdminApi.setStudentOpenSubjects(currentStudent.value.id, {
      subjects: openSubjects.value.map(s => ({ subject: s }))
    })
    ElMessage.success('保存成功')
    showOpenSubjectsDialogVisible.value = false
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '保存失败')
  }
}

function onPageChange(p: number) {
  page.value = p
  loadStudents()
}

onMounted(async () => {
  await loadGradeOptions()
  await loadSubjectOptions()
  await loadStudents()
})
</script>

<style scoped>
.student-view {
  max-width: 1400px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.search-bar {
  display: flex;
  align-items: center;
}

.pagination-bar {
  margin-top: 16px;
  display: flex;
  justify-content: flex-end;
}
</style>
