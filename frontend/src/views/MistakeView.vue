<template>
  <div class="mistake-view">
    <el-card shadow="never">
      <template #header>
        <div class="card-header">
          <span>错题管理</span>
        </div>
      </template>

      <div class="search-bar">
        <el-select
          v-model="searchStudentId"
          placeholder="搜索学生姓名"
          style="width: 200px; margin-right: 8px"
          clearable
          filterable
          remote
          reserve-keyword
          :remote-method="searchStudentsForFilter"
          :loading="searchingStudent"
        >
          <el-option
            v-for="student in filterStudentOptions"
            :key="student.id"
            :label="student.name"
            :value="student.id"
          >
            <span>{{ student.name }}</span>
            <span style="color: #999; font-size: 12px; margin-left: 8px">年级{{ student.grade }}</span>
          </el-option>
        </el-select>
        <el-select
          v-model="searchSubject"
          placeholder="学科"
          style="width: 150px; margin-right: 8px"
          clearable
        >
          <el-option
            v-for="subject in subjectOptions"
            :key="subject.value"
            :label="subject.label"
            :value="subject.value"
          />
        </el-select>
        <el-select
          v-model="searchGrade"
          placeholder="年级"
          style="width: 120px; margin-right: 8px"
          clearable
        >
          <el-option
            v-for="grade in gradeOptions"
            :key="grade.value"
            :label="grade.label"
            :value="grade.value"
          />
        </el-select>
        <el-select
          v-model="searchReviewStatus"
          placeholder="审核状态"
          style="width: 120px; margin-right: 8px"
          clearable
        >
          <el-option
            v-for="status in reviewStatusOptions"
            :key="status.value"
            :label="status.label"
            :value="status.value"
          />
        </el-select>
        <el-button type="primary" @click="loadMistakes">搜索</el-button>
      </div>

      <el-table
        :data="mistakes"
        v-loading="loading"
        size="small"
        style="margin-top: 16px"
      >
        <el-table-column label="首图" width="80">
          <template #default="{ row }">
            <el-image
              v-if="row.firstImagePath"
              :src="getMistakeImageUrl(row.firstImagePath)"
              :preview-src-list="[getMistakeImageUrl(row.firstImagePath)]"
              fit="cover"
              style="width: 50px; height: 50px; cursor: pointer"
            />
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column prop="id" label="ID" width="220" show-overflow-tooltip />
        <el-table-column prop="studentName" label="学生姓名" width="120" />
        <el-table-column label="学科" width="100">
          <template #default="{ row }">
            {{ getSubjectLabel(row.subject) }}
          </template>
        </el-table-column>
        <el-table-column label="年级" width="100">
          <template #default="{ row }">
            {{ getGradeLabel(row.grade) }}
          </template>
        </el-table-column>
        <el-table-column label="审核状态" width="100">
          <template #default="{ row }">
            <el-tag
              :type="getReviewStatusTagType(row.reviewStatus)"
              size="small"
            >
              {{ getReviewStatusLabel(row.reviewStatus) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="图片数量" width="80">
          <template #default="{ row }">
            {{ row.imageCount || 0 }}
          </template>
        </el-table-column>
        <el-table-column label="创建时间" width="160">
          <template #default="{ row }">
            {{ formatDate(row.createdAt) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="120" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="showDetailDialog(row)">
              详情
            </el-button>
            <el-button link type="primary" size="small" @click="showEditDialog(row)">
              编辑
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="pagination-bar">
        <span class="total-text">共 {{ total }} 条记录</span>
        <el-pagination
          background
          layout="prev, pager, next"
          :total="total"
          :page-size="pageSize"
          :current-page="page"
          @current-change="onPageChange"
        />
      </div>
    </el-card>

    <el-dialog v-model="showDetailDialogVisible" title="错题详情" width="700px">
      <div v-if="currentMistake" class="detail-content">
        <el-descriptions :column="2" border size="small">
          <el-descriptions-item label="ID">{{ currentMistake.id }}</el-descriptions-item>
          <el-descriptions-item label="学生姓名">{{ currentMistake.studentName }}</el-descriptions-item>
          <el-descriptions-item label="学科">{{ getSubjectLabel(currentMistake.subject) }}</el-descriptions-item>
          <el-descriptions-item label="年级">{{ getGradeLabel(currentMistake.grade) }}</el-descriptions-item>
          <el-descriptions-item label="审核状态">
            <el-tag :type="getReviewStatusTagType(currentMistake.reviewStatus)" size="small">
              {{ getReviewStatusLabel(currentMistake.reviewStatus) }}
            </el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="图片数量">{{ currentMistake.imageCount || 0 }}</el-descriptions-item>
          <el-descriptions-item label="来源上传ID">{{ currentMistake.sourceUploadId || '-' }}</el-descriptions-item>
          <el-descriptions-item label="问题ID">{{ currentMistake.questionId || '-' }}</el-descriptions-item>
          <el-descriptions-item label="创建时间" :span="2">
            {{ formatDate(currentMistake.createdAt) }}
          </el-descriptions-item>
          <el-descriptions-item label="更新时间" :span="2">
            {{ formatDate(currentMistake.updatedAt) }}
          </el-descriptions-item>
          <el-descriptions-item label="审核评论" :span="2">
            {{ currentMistake.reviewComment || '-' }}
          </el-descriptions-item>
        </el-descriptions>

        <div v-if="currentMistake.imageCount > 0" class="image-section">
          <h4>错题图片 ({{ currentMistake.imageCount }})</h4>
          <div class="image-list">
            <el-image
              v-for="(img, index) in currentMistakeImages"
              :key="index"
              :src="img"
              :preview-src-list="currentMistakeImages"
              fit="contain"
              class="detail-image"
            />
          </div>
        </div>
      </div>
    </el-dialog>

    <el-dialog v-model="showEditDialogVisible" title="编辑错题" width="500px">
      <el-form :model="editForm" label-width="80px">
        <el-form-item label="学生">
          <el-select
            v-model="editForm.studentId"
            filterable
            remote
            reserve-keyword
            placeholder="搜索学生"
            :remote-method="searchStudents"
            :loading="studentLoading"
            style="width: 100%"
          >
            <el-option
              v-for="student in studentOptions"
              :key="student.id"
              :label="student.name"
              :value="student.id"
            >
              <span>{{ student.name }}</span>
              <span style="color: #999; margin-left: 8px">年级{{ student.grade }}</span>
            </el-option>
          </el-select>
        </el-form-item>
        <el-form-item label="学科">
          <el-select v-model="editForm.subject" style="width: 100%">
            <el-option
              v-for="subject in subjectOptions"
              :key="subject.value"
              :label="subject.label"
              :value="subject.value"
            />
          </el-select>
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
        <el-button @click="showEditDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="saveEdit">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import studentAdminApi from '../services/studentAdminApi'
import type { MistakeItemDto } from '../services/studentAdminApi'

interface MistakeItem extends MistakeItemDto {
  images?: string[]
}

const mistakes = ref<MistakeItem[]>([])
const loading = ref(false)
const total = ref(0)
const page = ref(1)
const pageSize = 20

const searchStudentId = ref<string>()
const searchSubject = ref<number>()
const searchGrade = ref<number>()
const searchReviewStatus = ref<number>()

const filterStudentOptions = ref<any[]>([])
const searchingStudent = ref(false)

const subjectOptions = ref<{ value: number; label: string }[]>([])
const gradeOptions = ref<{ value: number; label: string }[]>([])
const reviewStatusOptions = ref<{ value: number; label: string }[]>([
  { value: 1, label: '待审核' },
  { value: 2, label: '已确认' },
  { value: 3, label: '已驳回' }
])

const showDetailDialogVisible = ref(false)
const currentMistake = ref<MistakeItem | null>(null)
const currentMistakeImages = ref<string[]>([])

const showEditDialogVisible = ref(false)
const editForm = ref({
  id: '',
  studentId: '',
  subject: 0,
  grade: 0
})

const studentOptions = ref<any[]>([])
const studentLoading = ref(false)

async function loadSubjectOptions() {
  try {
    const subjects = await studentAdminApi.getSubjectOptions()
    subjectOptions.value = subjects.map((s: any) => ({
      value: s.value,
      label: s.label || s.displayName
    }))
  } catch (error) {
    console.error('Failed to load subjects:', error)
  }
}

async function searchStudentsForFilter(query: string) {
  if (!query || query.length < 1) {
    filterStudentOptions.value = []
    return
  }
  searchingStudent.value = true
  try {
    const result = await studentAdminApi.getStudents({ name: query, pageSize: 20 })
    filterStudentOptions.value = result.items
  } catch (error) {
    console.error('Search students failed:', error)
  } finally {
    searchingStudent.value = false
  }
}

async function loadGradeOptions() {
  try {
    const grades = await studentAdminApi.getGrades()
    gradeOptions.value = grades.map((g: any) => ({ value: g.value, label: g.label }))
  } catch (error) {
    console.error('Failed to load grades:', error)
  }
}

async function loadEnumOptions() {
  try {
    const enums = await studentAdminApi.getEnumOptions()
    if (enums.reviewStatuses && enums.reviewStatuses.length > 0) {
      reviewStatusOptions.value = enums.reviewStatuses.map((s: any) => ({
        value: s.value,
        label: s.label || s.displayName
      }))
    }
  } catch (error) {
    console.error('Failed to load enum options:', error)
  }
}

async function loadMistakes() {
  loading.value = true
  try {
    const result = await studentAdminApi.getMistakeItems({
      page: page.value,
      size: pageSize,
      studentId: searchStudentId.value || undefined,
      subject: searchSubject.value,
      grade: searchGrade.value,
      reviewStatus: searchReviewStatus.value
    })
    mistakes.value = result.items
    total.value = result.total
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '加载错题列表失败')
  } finally {
    loading.value = false
  }
}

function getSubjectLabel(subject: number) {
  return subjectOptions.value.find(s => s.value === subject)?.label || `学科${subject}`
}

function getGradeLabel(grade: number) {
  return gradeOptions.value.find(g => g.value === grade)?.label || `年级${grade}`
}

function getReviewStatusLabel(status: number) {
  return reviewStatusOptions.value.find(s => s.value === status)?.label || `状态${status}`
}

function getReviewStatusTagType(status: number) {
  switch (status) {
    case 1: return 'warning'
    case 2: return 'success'
    case 3: return 'danger'
    default: return 'info'
  }
}

function formatDate(dateStr: string | number | undefined) {
  if (!dateStr) return '-'
  if (typeof dateStr === 'string') {
    if (/^\d+$/.test(dateStr.trim())) {
      return new Date(Number(dateStr) * 1000).toLocaleString('zh-CN')
    }
    return new Date(dateStr).toLocaleString('zh-CN')
  }
  return new Date(dateStr * 1000).toLocaleString('zh-CN')
}

function getMistakeImageUrl(path: string) {
  if (!path) return ''
  if (path.startsWith('http://') || path.startsWith('https://')) return path
  return `/api/admin/oss-upload-records/image?path=${encodeURIComponent(path)}`
}

async function showDetailDialog(mistake: MistakeItem) {
  try {
    const detail = await studentAdminApi.getMistakeItem(mistake.id)
    currentMistake.value = detail
    
    const images: string[] = []
    if (detail.firstImagePath) {
      const basePath = detail.firstImagePath.replace(/\/[^/]+\.[^.]+$/, '')
      const ext = detail.firstImagePath.match(/\.[^.]+$/)?.[0] || '.jpg'
      for (let i = 0; i < (detail.imageCount || 0); i++) {
        images.push(getMistakeImageUrl(`${basePath}/${i}${ext}`))
      }
    }
    currentMistakeImages.value = images
    
    showDetailDialogVisible.value = true
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '加载详情失败')
  }
}

function showEditDialog(mistake: MistakeItem) {
  editForm.value = {
    id: mistake.id,
    studentId: mistake.studentId,
    subject: mistake.subject,
    grade: mistake.grade
  }
  studentOptions.value = [{
    id: mistake.studentId,
    name: mistake.studentName,
    grade: mistake.grade
  }]
  showEditDialogVisible.value = true
}

async function searchStudents(query: string) {
  if (!query || query.length < 1) {
    studentOptions.value = []
    return
  }
  
  studentLoading.value = true
  try {
    const result = await studentAdminApi.getStudents({ name: query, pageSize: 20 })
    studentOptions.value = result.items
  } catch (error) {
    console.error('Search students failed:', error)
  } finally {
    studentLoading.value = false
  }
}

async function saveEdit() {
  try {
    await studentAdminApi.updateMistakeItem(editForm.value.id, {
      studentId: editForm.value.studentId,
      subject: editForm.value.subject,
      grade: editForm.value.grade
    })
    ElMessage.success('更新成功')
    showEditDialogVisible.value = false
    await loadMistakes()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '更新失败')
  }
}

function onPageChange(p: number) {
  page.value = p
  loadMistakes()
}

onMounted(async () => {
  await Promise.all([
    loadSubjectOptions(),
    loadGradeOptions(),
    loadEnumOptions()
  ])
  await loadMistakes()
})
</script>

<style scoped>
.mistake-view {
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
  justify-content: space-between;
  align-items: center;
}

.total-text {
  color: #666;
  font-size: 14px;
}

.detail-content {
  padding: 0;
}

.image-section {
  margin-top: 20px;
}

.image-section h4 {
  margin-bottom: 12px;
  color: #333;
}

.image-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.detail-image {
  width: 120px;
  height: 120px;
  border: 1px solid #eee;
  border-radius: 4px;
  cursor: pointer;
}
</style>
