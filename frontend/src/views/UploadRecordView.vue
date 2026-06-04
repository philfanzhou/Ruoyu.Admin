<template>
  <div class="upload-record-view">
    <el-card shadow="never">
      <template #header>
        <div class="card-header">
          <span>上传记录管理</span>
        </div>
      </template>

      <div class="filter-bar">
        <el-select
          v-model="filterStudentId"
          placeholder="搜索学生姓名"
          style="width: 200px; margin-right: 8px"
          clearable
          filterable
          remote
          reserve-keyword
          :remote-method="searchStudentsForFilter"
          :loading="searchingFilterStudent"
          size="small"
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
          v-model="filterStatus"
          placeholder="状态筛选"
          style="width: 200px; margin-right: 8px"
          clearable
          size="small"
        >
          <el-option
            v-for="status in statusOptions"
            :key="status.value"
            :label="status.label"
            :value="status.value"
          />
        </el-select>
        <el-button type="primary" size="small" @click="loadUploadRecords">查询</el-button>
      </div>

      <el-table
        :data="uploadRecords"
        v-loading="loading"
        size="small"
        style="margin-top: 16px"
      >
        <el-table-column label="缩略图" width="80">
          <template #default="{ row }">
            <el-image
              v-if="row.imagePaths && row.imagePaths.length > 0"
              :src="getImageUrl(row.imagePaths[0])"
              :preview-src-list="getPreviewList(row.imagePaths)"
              preview-teleported
              fit="cover"
              style="width: 50px; height: 50px; cursor: pointer"
            />
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column prop="studentName" label="学生姓名" width="120" />
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="getStatusType(row.status)" size="small">
              {{ getStatusLabel(row.status) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="图片数量" width="100">
          <template #default="{ row }">
            {{ row.imagePaths?.length || 0 }}
          </template>
        </el-table-column>
        <el-table-column label="创建时间" width="160">
          <template #default="{ row }">
            {{ formatDate(row.createdAt) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="280" fixed="right">
          <template #default="{ row }">
            <el-button link type="success" size="small" @click="checkLegacy(row)">
              检查清理
            </el-button>
            <el-button link type="primary" size="small" @click="showDetailDialog(row)">
              详情
            </el-button>
            <el-button link type="primary" size="small" @click="showAssignDialog(row)">
              指派
            </el-button>
            <el-button link type="warning" size="small" @click="showResetDialog(row)">
              重置
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

    <el-dialog v-model="showDetail" title="上传记录详情" width="800px">
      <div v-if="currentRecord">
        <el-descriptions :column="2" border size="small">
          <el-descriptions-item label="记录ID">{{ currentRecord.id }}</el-descriptions-item>
          <el-descriptions-item label="学生姓名">{{ currentRecord.studentName }}</el-descriptions-item>
          <el-descriptions-item label="状态">
            <el-tag :type="getStatusType(currentRecord.status)" size="small">
              {{ getStatusLabel(currentRecord.status) }}
            </el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="创建时间">{{ formatDate(currentRecord.createdAt) }}</el-descriptions-item>
          <el-descriptions-item label="更新时间">{{ formatDate(currentRecord.updatedAt) }}</el-descriptions-item>
          <el-descriptions-item label="备注" :span="2">{{ currentRecord.comments || '无' }}</el-descriptions-item>
        </el-descriptions>

        <div class="image-section">
          <h4>上传图片</h4>
          <div class="image-grid">
            <div
              v-for="(img, index) in currentRecord.imagePaths"
              :key="index"
              class="image-item"
            >
              <el-image
                :src="getImageUrl(img)"
                :style="{ transform: `rotate(${currentRecord.imageRotations?.[index] || 0}deg)` }"
                fit="cover"
                :preview-src-list="getPreviewList(currentRecord.imagePaths)"
                :initial-index="index"
                preview-teleported
                class="record-image"
              >
                <template #error>
                  <div class="image-error">
                    <el-icon><Picture /></el-icon>
                    <span>加载失败</span>
                  </div>
                </template>
              </el-image>
              <div class="image-path-row">
                <span class="image-path-text" :title="img">{{ img }}</span>
                <el-button link type="primary" size="small" @click="copyPath(img)">
                  <el-icon><DocumentCopy /></el-icon>
                </el-button>
              </div>
              <div class="image-actions">
                <el-button
                  size="small"
                  type="primary"
                  @click="rotateImage(currentRecord, index, -90)"
                  circle
                >
                  <el-icon><RefreshLeft /></el-icon>
                </el-button>
                <el-button
                  size="small"
                  type="primary"
                  @click="rotateImage(currentRecord, index, 90)"
                  circle
                >
                  <el-icon><RefreshRight /></el-icon>
                </el-button>
              </div>
            </div>
          </div>
        </div>
      </div>
      <template #footer>
        <el-button @click="showDetail = false">关闭</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="showAssign" title="指派记录" width="600px">
      <el-form :model="assignForm" label-width="100px" size="small">
        <el-form-item label="学生">
          <el-select
            v-model="assignForm.studentId"
            filterable
            remote
            reserve-keyword
            placeholder="搜索学生姓名"
            :remote-method="searchStudents"
            :loading="searchingStudent"
            style="width: 100%"
            @focus="loadStudentOptions"
          >
            <el-option
              v-for="student in studentOptions"
              :key="student.id"
              :label="student.name"
              :value="student.id"
            >
              <span>{{ student.name }}</span>
              <span style="color: #999; font-size: 12px; margin-left: 8px">年级{{ student.grade }}</span>
            </el-option>
          </el-select>
        </el-form-item>
        <el-form-item label="选择图片">
          <div class="assign-image-picker">
            <el-checkbox-group v-model="assignForm.imageIndices">
              <el-checkbox
                v-for="(img, idx) in currentRecord?.imagePaths"
                :key="idx"
                :value="idx"
                :label="`图片 ${idx + 1}`"
              />
            </el-checkbox-group>
            <div v-if="!currentRecord?.imagePaths?.length" style="color: #909399; font-size: 12px">
              当前记录无图片
            </div>
          </div>
        </el-form-item>
        <el-form-item label="学科" required>
          <el-select v-model="assignForm.subject" style="width: 100%" placeholder="请选择学科">
            <el-option
              v-for="subject in subjectOptions"
              :key="subject.value"
              :label="subject.label"
              :value="subject.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="年级" required>
          <el-select v-model="assignForm.grade" style="width: 100%" placeholder="请选择年级">
            <el-option
              v-for="grade in gradeOptions"
              :key="grade.value"
              :label="grade.label"
              :value="grade.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="备注">
          <el-input
            v-model="assignForm.comments"
            type="textarea"
            :rows="2"
            placeholder="可选备注"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showAssign = false">取消</el-button>
        <el-button type="primary" @click="confirmAssign" :loading="assigning">确认指派</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="showReset" title="重置状态" width="400px">
      <el-form :model="resetForm" label-width="100px" size="small">
        <el-form-item label="目标状态">
          <el-select v-model="resetForm.targetStatus" style="width: 100%">
            <el-option
              v-for="status in statusOptions.filter(s => s.value !== -1)"
              :key="status.value"
              :label="status.label"
              :value="status.value"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showReset = false">取消</el-button>
        <el-button type="primary" @click="confirmReset" :loading="resetting">确认重置</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="showLegacyDialog" title="遗留数据检查" width="500px">
      <div v-if="legacyCheckResult">
        <el-alert
          :type="legacyCheckResult.isLegacy ? 'warning' : 'success'"
          :title="legacyCheckResult.message"
          show-icon
          :closable="false"
          style="margin-bottom: 16px"
        />
        <el-descriptions v-if="legacyCheckResult.isLegacy" :column="1" border size="small">
          <el-descriptions-item label="关联错题数">{{ legacyCheckResult.mistakeCount }}</el-descriptions-item>
          <el-descriptions-item label="图片未迁移">{{ legacyCheckResult.hasUploadPathImage ? '是' : '否' }}</el-descriptions-item>
        </el-descriptions>
        <div v-if="legacyCheckResult.isLegacy" style="margin-top: 16px; color: #909399; font-size: 13px">
          <p>清理操作将：</p>
          <ol>
            <li>迁移图片到 OSS 并更新错题中的图片路径</li>
            <li>删除该上传记录</li>
          </ol>
        </div>
      </div>
      <template #footer>
        <el-button @click="showLegacyDialog = false">关闭</el-button>
        <el-button
          v-if="legacyCheckResult?.isLegacy"
          type="danger"
          @click="cleanLegacy"
          :loading="legacyCleaning"
        >
          确认清理
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { Picture, RefreshLeft, RefreshRight, DocumentCopy } from '@element-plus/icons-vue'
import { studentAdminClient } from '../services/studentAdminApi'
import type { UploadRecordDto, StudentDto } from '../services/studentAdminApi'

const uploadRecords = ref<UploadRecordDto[]>([])
const loading = ref(false)
const total = ref(0)
const page = ref(1)
const pageSize = 20

const filterStatus = ref<number>()
const filterStudentId = ref<string>()
const filterStudentOptions = ref<StudentDto[]>([])
const searchingFilterStudent = ref(false)

const statusOptions = ref<{ value: number; label: string }[]>([])
const subjectOptions = ref<{ value: number; label: string }[]>([])
const gradeOptions = ref<{ value: number; label: string }[]>([])

const showDetail = ref(false)
const currentRecord = ref<UploadRecordDto | null>(null)

const showAssign = ref(false)
const assigning = ref(false)
const assignForm = ref({
  studentId: '',
  subject: 0,
  grade: 0,
  imageIndices: [] as number[],
  comments: ''
})
const studentOptions = ref<StudentDto[]>([])
const searchingStudent = ref(false)

const showReset = ref(false)
const resetting = ref(false)
const resetForm = ref({
  recordId: '',
  studentId: '',
  targetStatus: 1
})

const legacyChecking = ref(false)
const legacyCheckResult = ref<{ isLegacy: boolean; mistakeCount?: number; hasUploadPathImage?: boolean; message: string } | null>(null)
const showLegacyDialog = ref(false)
const legacyCleaning = ref(false)
const legacyTarget = ref<UploadRecordDto | null>(null)

async function loadEnumOptions() {
  try {
    const options = await studentAdminClient.getGrades()
    gradeOptions.value = options.map(g => ({ value: g.value, label: g.label }))
  } catch (error) {
    console.error('Failed to load grades:', error)
  }

  try {
    const subjects = await studentAdminClient.getSubjectOptions()
    subjectOptions.value = subjects.map(s => ({
      value: s.value,
      label: s.displayName || s.name
    }))
  } catch (error) {
    console.error('Failed to load subjects:', error)
  }

  statusOptions.value = [
    { value: -1, label: '全部' },
    { value: 1, label: '待处理' },
    { value: 2, label: '处理中' },
    { value: 3, label: '已完成' },
    { value: 4, label: '失败' },
    { value: 5, label: '已退回' }
  ]
}

async function loadUploadRecords() {
  loading.value = true
  try {
    const result = await studentAdminClient.getUploadRecords({
      page: page.value,
      pageSize,
      status: filterStatus.value === -1 ? undefined : filterStatus.value,
      studentId: filterStudentId.value || undefined
    })
    uploadRecords.value = result.items
    total.value = result.totalCount
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '加载上传记录失败')
  } finally {
    loading.value = false
  }
}

function getStatusLabel(status: number) {
  const statusMap: Record<number, string> = {
    1: '待处理',
    2: '处理中',
    3: '已完成',
    4: '失败',
    5: '已退回'
  }
  return statusMap[status] || `状态${status}`
}

function getStatusType(status: number) {
  const typeMap: Record<number, string> = {
    1: 'info',
    2: 'warning',
    3: 'success',
    4: 'danger',
    5: 'warning'
  }
  return typeMap[status] || 'info'
}

function getSubjectLabel(subject: number) {
  return subjectOptions.value.find(s => s.value === subject)?.label || `学科${subject}`
}

function getGradeLabel(grade: number) {
  return gradeOptions.value.find(g => g.value === grade)?.label || `年级${grade}`
}

function formatDate(date: number | string | undefined) {
  if (!date) return '-'
  if (typeof date === 'string') {
    if (/^\d+$/.test(date.trim())) {
      return new Date(Number(date) * 1000).toLocaleString('zh-CN')
    }
    return new Date(date).toLocaleString('zh-CN')
  }
  return new Date(date * 1000).toLocaleString('zh-CN')
}

function getImageUrl(path: string) {
  if (!path) return ''
  if (path.startsWith('http://') || path.startsWith('https://')) {
    return path
  }
  return `/api/admin/oss-upload-records/image?path=${encodeURIComponent(path)}`
}

function getPreviewList(imagePaths: string[]) {
  return imagePaths.map(path => getImageUrl(path))
}

function copyPath(path: string) {
  navigator.clipboard.writeText(path).then(() => {
    ElMessage.success('已复制路径')
  }).catch(() => {
    ElMessage.error('复制失败')
  })
}

function showDetailDialog(record: UploadRecordDto) {
  currentRecord.value = { ...record }
  showDetail.value = true
}

async function rotateImage(record: UploadRecordDto, imageIndex: number, rotation: number) {
  try {
    const currentRotation = record.imageRotations?.[imageIndex] || 0
    const newRotation = (currentRotation + rotation + 360) % 360

    await studentAdminClient.rotateUploadImage(record.id, {
      studentId: record.studentId,
      imageIndex,
      rotation: newRotation
    })

    if (!record.imageRotations) {
      record.imageRotations = []
    }
    record.imageRotations[imageIndex] = newRotation

    if (currentRecord.value?.id === record.id) {
      currentRecord.value.imageRotations = [...record.imageRotations]
    }

    ElMessage.success('图片旋转成功')
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '旋转图片失败')
  }
}

async function searchStudentsForFilter(query: string) {
  if (!query || query.length < 1) {
    filterStudentOptions.value = []
    return
  }
  searchingFilterStudent.value = true
  try {
    const result = await studentAdminClient.getStudents({ name: query, pageSize: 20 })
    filterStudentOptions.value = result.items
  } catch (error) {
    console.error('Search students for filter failed:', error)
  } finally {
    searchingFilterStudent.value = false
  }
}

async function loadStudentOptions() {
  if (studentOptions.value.length === 0) {
    try {
      const result = await studentAdminClient.getStudents({ page: 1, pageSize: 100 })
      studentOptions.value = result.items
    } catch (error) {
      console.error('Failed to load students:', error)
    }
  }
}

async function searchStudents(keyword: string) {
  if (!keyword) {
    await loadStudentOptions()
    return
  }

  searchingStudent.value = true
  try {
    const result = await studentAdminClient.getStudents({
      name: keyword,
      page: 1,
      pageSize: 50
    })
    studentOptions.value = result.items
  } catch (error) {
    console.error('Search students failed:', error)
  } finally {
    searchingStudent.value = false
  }
}

function showAssignDialog(record: UploadRecordDto) {
  assignForm.value = {
    studentId: '',
    subject: 0,
    grade: 0,
    imageIndices: record.imagePaths?.map((_, idx) => idx) ?? [],
    comments: record.comments || ''
  }
  currentRecord.value = record
  showAssign.value = true
}

async function confirmAssign() {
  if (!assignForm.value.studentId) {
    ElMessage.warning('请选择学生')
    return
  }

  if (assignForm.value.subject <= 0) {
    ElMessage.warning('请选择学科')
    return
  }

  if (assignForm.value.grade <= 0) {
    ElMessage.warning('请选择年级')
    return
  }

  if (!currentRecord.value) {
    ElMessage.error('记录信息缺失')
    return
  }

  const imageIndices = assignForm.value.imageIndices
  if (!imageIndices || imageIndices.length === 0) {
    ElMessage.warning('请至少选择一张图片')
    return
  }

  assigning.value = true
  try {
    await studentAdminClient.assignUploadRecord(currentRecord.value.id, {
      studentId: assignForm.value.studentId,
      assignments: [
        {
          imageIndices: [...imageIndices],
          subject: assignForm.value.subject,
          grade: assignForm.value.grade,
          comments: assignForm.value.comments || undefined
        }
      ]
    })
    ElMessage.success('指派成功')
    showAssign.value = false
    await loadUploadRecords()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '指派失败')
  } finally {
    assigning.value = false
  }
}

function showResetDialog(record: UploadRecordDto) {
  currentRecord.value = record
  resetForm.value = {
    recordId: record.id,
    studentId: record.studentId,
    targetStatus: 1
  }
  showReset.value = true
}

async function confirmReset() {
  if (!currentRecord.value) {
    ElMessage.error('记录信息缺失')
    return
  }

  resetting.value = true
  try {
    await studentAdminClient.resetUploadRecordStatus(
      resetForm.value.recordId,
      currentRecord.value.studentId,
      resetForm.value.targetStatus
    )
    ElMessage.success('重置成功')
    showReset.value = false
    await loadUploadRecords()
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '重置失败')
  } finally {
    resetting.value = false
  }
}

async function checkLegacy(record: UploadRecordDto) {
  legacyChecking.value = true
  try {
    const result = await studentAdminClient.legacyCheck(record.id)
    legacyTarget.value = record
    legacyCheckResult.value = result
    showLegacyDialog.value = true
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || error.message || '检查失败')
  } finally {
    legacyChecking.value = false
  }
}

async function cleanLegacy() {
  if (!legacyTarget.value) return
  legacyCleaning.value = true
  try {
    const result = await studentAdminClient.legacyClean(legacyTarget.value.id)
    if (result.success) {
      ElMessage.success('清理成功')
      showLegacyDialog.value = false
      await loadUploadRecords()
    } else {
      ElMessage.error(result.message || '清理失败')
    }
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || error.message || '清理失败')
  } finally {
    legacyCleaning.value = false
  }
}

function onPageChange(p: number) {
  page.value = p
  loadUploadRecords()
}

onMounted(async () => {
  await loadEnumOptions()
  await loadUploadRecords()
})
</script>

<style scoped>
.upload-record-view {
  max-width: 1600px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.filter-bar {
  display: flex;
  align-items: center;
}

.pagination-bar {
  margin-top: 16px;
  display: flex;
  justify-content: flex-end;
}

.image-section {
  margin-top: 20px;
}

.image-section h4 {
  margin-bottom: 12px;
  font-size: 14px;
  font-weight: 500;
}

.image-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
  gap: 16px;
}

.image-item {
  position: relative;
  border: 1px solid #ebeef5;
  border-radius: 4px;
  overflow: hidden;
  background: #f5f7fa;
}

.record-image {
  width: 100%;
  height: 200px;
  display: block;
  transition: transform 0.3s;
}

.image-error {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 200px;
  color: #909399;
  font-size: 12px;
}

.image-error .el-icon {
  font-size: 32px;
  margin-bottom: 8px;
}

.image-actions {
  position: absolute;
  bottom: 8px;
  left: 50%;
  transform: translateX(-50%);
  display: flex;
  gap: 8px;
  opacity: 0;
  transition: opacity 0.3s;
}

.image-item:hover .image-actions {
  opacity: 1;
}

.image-path-row {
  display: flex;
  align-items: center;
  padding: 4px 6px;
  background: #f5f7fa;
  border-top: 1px solid #ebeef5;
}

.image-path-text {
  flex: 1;
  font-size: 11px;
  color: #606266;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: monospace;
}
</style>
