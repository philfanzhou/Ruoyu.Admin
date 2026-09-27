<template>
  <div class="adm-fade-in upload-record-view">
    <PageHeader title="上传记录" description="审核学生上传的作业图片，指派为错题或退回处理">
      <template #actions>
        <el-button :icon="Refresh" @click="loadAll">刷新</el-button>
      </template>
    </PageHeader>

    <UploadStatCards :counts="stats" :loading="statsLoading" :active="filterStatus" @select="setFilterStatus" />

    <FilterBar @search="onSearch" @reset="resetFilters">
      <el-form-item>
        <el-select
          v-model="filterStudentId"
          class="filter-student"
          filterable
          remote
          clearable
          :remote-method="onStudentQuery"
          placeholder="搜索学生姓名..."
          no-data-text="无匹配学生"
          @change="onStudentChange"
        >
          <el-option v-for="s in studentFilterOptions" :key="s.id" :label="s.name" :value="s.id">
            <span class="option-name">{{ s.name }}</span>
            <span class="option-meta">ID: {{ s.id }} · {{ gradeLabel(s.grade) }}</span>
          </el-option>
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-select v-model="filterStatus" class="filter-control" placeholder="全部状态" clearable @change="onFilterChange">
          <el-option v-for="s in UPLOAD_STATUSES" :key="s.value" :label="s.label" :value="s.value" />
        </el-select>
      </el-form-item>
    </FilterBar>

    <DataTableCard
      :data="uploadRecords"
      :loading="loading"
      :error="loadError"
      empty-text="暂无上传记录"
      row-key="id"
      :row-class-name="rowClassName"
      @row-click="(row: UploadRecordDto) => openDetail(row)"
      @retry="loadUploadRecords"
    >
      <el-table-column label="缩略图" width="84">
        <template #default="{ row }">
          <div class="thumb">
            <img v-if="row.imagePaths?.length" :src="getImageUrl(row.imagePaths[0], 'small')" class="thumb-img" alt="缩略图" @error="hideBrokenImage" />
            <el-icon v-else :size="22"><Picture /></el-icon>
          </div>
        </template>
      </el-table-column>
      <el-table-column label="学生" min-width="160">
        <template #default="{ row }">
          <div class="student-cell">
            <div class="stu-avatar-sm" :style="{ background: getAvatarGradient(row.studentName || row.studentId) }">{{ getAvatarChar(row.studentName || '?') }}</div>
            <div>
              <div class="student-name">{{ row.studentName }}</div>
              <div class="student-grade">{{ gradeLabel(studentGradeMap[row.studentId] || 0) }}</div>
            </div>
          </div>
        </template>
      </el-table-column>
      <el-table-column label="状态" width="110">
        <template #default="{ row }">
          <StatusTag :label="getUploadStatusLabel(row.status)" :type="getUploadStatusTagType(row.status)" />
        </template>
      </el-table-column>
      <el-table-column label="图片数" width="90">
        <template #default="{ row }"><span class="img-count">{{ row.imagePaths?.length || 0 }} 张</span></template>
      </el-table-column>
      <!-- UploadRecordDto has no subject: it is chosen when assigning. -->
      <el-table-column label="学科" width="90">
        <template #default><span class="text-muted">-</span></template>
      </el-table-column>
      <el-table-column label="创建时间" width="160">
        <template #default="{ row }"><span class="time-cell">{{ formatDateTime(row.createdAt) }}</span></template>
      </el-table-column>
      <el-table-column label="操作" width="220" align="right" fixed="right">
        <template #default="{ row }">
          <div class="row-actions" @click.stop>
            <el-button v-if="canAssign(row)" type="primary" size="small" :icon="Check" @click="openDetail(row, true)">指派</el-button>
            <el-button link type="primary" @click="openDetail(row)">详情</el-button>
            <el-button link type="primary" @click="legacyRecord = row">检查清理</el-button>
          </div>
        </template>
      </el-table-column>
      <template v-if="uploadRecords.length > 0" #footer>
        <ListPagination v-model:page="page" v-model:page-size="pageSize" :total="total" @change="loadUploadRecords" />
      </template>
    </DataTableCard>

    <UploadRecordDetailDrawer
      :record="detailRecord"
      :initial-grade="detailInitialGrade"
      :student-grade="detailRecord ? studentGradeMap[detailRecord.studentId] || 0 : 0"
      :grade-options="gradeOptions"
      :subject-options="subjectOptions"
      @close="detailRecord = null"
      @records-changed="loadUploadRecords"
      @changed="loadAll"
    />

    <LegacyCheckDialog :record="legacyRecord" @close="legacyRecord = null" @cleaned="onLegacyCleaned" />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { Check, Picture, Refresh } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { studentAdminClient } from '../services/studentAdminApi'
import type { UploadRecordDto, StudentDto, GradeOption, SubjectOption } from '../services/studentAdminApi'
import { getAvatarGradient, getAvatarChar, formatDateTime } from '../utils/subject'
import { parseEnumQuery, useEnumQueryFilter } from '../utils/routeQuery'
import PageHeader from '../components/list/PageHeader.vue'
import FilterBar from '../components/list/FilterBar.vue'
import DataTableCard from '../components/list/DataTableCard.vue'
import ListPagination from '../components/list/ListPagination.vue'
import StatusTag from '../components/list/StatusTag.vue'
import UploadRecordDetailDrawer from './upload-records/UploadRecordDetailDrawer.vue'
import LegacyCheckDialog from './upload-records/LegacyCheckDialog.vue'
import UploadStatCards from './upload-records/UploadStatCards.vue'
import {
  UPLOAD_STATUSES, UPLOAD_STATUS_VALUES,
  getUploadStatusLabel, getUploadStatusTagType, getGradeLabel, getImageUrl, hideBrokenImage, getErrorMessage,
} from './upload-records/uploadRecord'

const route = useRoute()

// ===== State =====
const uploadRecords = ref<UploadRecordDto[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)

// Initialised from ?status= so the first list request already carries the deep-link filter.
const filterStatus = ref<number | undefined>(parseEnumQuery(route.query.status, UPLOAD_STATUS_VALUES))
const filterStudentId = ref<string | undefined>(undefined)
const filterStudentOptions = ref<StudentDto[]>([])
let filterSearchTimer: number | null = null

const subjectOptions = ref<SubjectOption[]>([])
const gradeOptions = ref<GradeOption[]>([])
const studentGradeMap = ref<Record<string, number>>({})

const stats = ref<Record<number, number>>({})
const statsLoading = ref(false)

const detailRecord = ref<UploadRecordDto | null>(null)
const detailInitialGrade = ref(0)
const legacyRecord = ref<UploadRecordDto | null>(null)

// ===== API =====
async function loadEnumOptions() {
  try {
    const [grades, subjects] = await Promise.all([
      studentAdminClient.getGrades(),
      studentAdminClient.getSubjectOptions(),
    ])
    gradeOptions.value = grades
    subjectOptions.value = subjects
  } catch (error) {
    console.error('Failed to load enum options:', error)
  }
}

async function loadUploadRecords() {
  loading.value = true
  loadError.value = null
  try {
    const result = await studentAdminClient.getUploadRecords({
      page: page.value,
      pageSize: pageSize.value,
      status: filterStatus.value,
      studentId: filterStudentId.value || undefined,
    })
    uploadRecords.value = result.items
    total.value = result.totalCount
  } catch (error) {
    // Drop stale rows so a failure is never mistaken for an (outdated) result.
    uploadRecords.value = []
    total.value = 0
    loadError.value = getErrorMessage(error, '加载上传记录失败')
    ElMessage.error(loadError.value)
  } finally {
    loading.value = false
  }
}

async function loadStats() {
  statsLoading.value = true
  try {
    const results = await Promise.all(
      UPLOAD_STATUS_VALUES.map((status) => studentAdminClient.getUploadRecords({ page: 1, pageSize: 1, status })),
    )
    stats.value = Object.fromEntries(UPLOAD_STATUS_VALUES.map((status, i) => [status, results[i].totalCount]))
  } catch (error) {
    console.error('Failed to load stats:', error)
  } finally {
    statsLoading.value = false
  }
}

async function loadAll() {
  await Promise.all([loadUploadRecords(), loadStats()])
}

async function loadStudentGrade(studentId: string) {
  if (!studentId || studentGradeMap.value[studentId]) return
  try {
    const s = await studentAdminClient.getStudent(studentId)
    if (s) studentGradeMap.value[studentId] = s.grade
  } catch (error) {
    console.error('Failed to load student grade:', error)
  }
}

// ===== Helpers =====
function gradeLabel(grade: number) {
  return getGradeLabel(gradeOptions.value, grade)
}

// Assign only when the record is pending (1) or returned (5) — matches the backend rule.
function canAssign(record: UploadRecordDto) {
  return record.status === 1 || record.status === 5
}

function rowClassName({ row }: { row: UploadRecordDto }) {
  return detailRecord.value?.id === row.id ? 'active-row' : ''
}

// ===== Filters =====
const { syncToUrl: syncStatusToUrl } = useEnumQueryFilter('status', UPLOAD_STATUS_VALUES, filterStatus, () => {
  page.value = 1
  loadUploadRecords()
})

function setFilterStatus(value: number | undefined) {
  filterStatus.value = filterStatus.value === value ? undefined : value
  syncStatusToUrl()
  page.value = 1
  loadUploadRecords()
}

function onFilterChange() {
  syncStatusToUrl()
  page.value = 1
  loadUploadRecords()
}

function onSearch() {
  page.value = 1
  loadUploadRecords()
}

function resetFilters() {
  filterStatus.value = undefined
  syncStatusToUrl()
  filterStudentId.value = undefined
  filterStudentOptions.value = []
  page.value = 1
  loadUploadRecords()
}

// el-select remote-method: same rules as before (trimmed, empty clears, 250ms debounce, pageSize 20).
function onStudentQuery(query: string) {
  if (filterSearchTimer) clearTimeout(filterSearchTimer)
  const name = query.trim()
  if (!name) {
    filterStudentOptions.value = []
    return
  }
  filterSearchTimer = window.setTimeout(async () => {
    try {
      const result = await studentAdminClient.getStudents({ name, pageSize: 20 })
      filterStudentOptions.value = result.items
    } catch (error) {
      console.error('Search students failed:', error)
    }
  }, 250)
}

// Keep the selected student among the options so its label survives clearing the results.
const selectedFilterStudent = ref<StudentDto | null>(null)
const studentFilterOptions = computed(() => {
  const selected = selectedFilterStudent.value
  if (!selected || filterStudentId.value !== selected.id) return filterStudentOptions.value
  return [selected, ...filterStudentOptions.value.filter((s) => s.id !== selected.id)]
})

function onStudentChange(studentId: string | undefined) {
  const student = studentId ? studentFilterOptions.value.find((s) => s.id === studentId) : undefined
  if (student) {
    selectedFilterStudent.value = student
    filterStudentId.value = student.id
  } else {
    selectedFilterStudent.value = null
    filterStudentId.value = undefined
  }
  filterStudentOptions.value = []
  page.value = 1
  loadUploadRecords()
}

// ===== Detail / Legacy =====
function openDetail(record: UploadRecordDto, withAssign = false) {
  // "指派" pre-fills the student's grade when it is already known.
  detailInitialGrade.value = withAssign ? studentGradeMap.value[record.studentId] || 0 : 0
  detailRecord.value = { ...record }
  loadStudentGrade(record.studentId)
}

async function onLegacyCleaned() {
  legacyRecord.value = null
  await loadAll()
}

onMounted(async () => {
  await loadEnumOptions()
  await loadAll()
})
</script>

<style scoped>
.filter-student { width: 240px; }
.filter-control { width: 140px; }
.option-name { margin-right: 8px; color: var(--adm-text-primary); }
.option-meta { color: var(--adm-text-tertiary); font-size: 12px; }
.upload-record-view :deep(.el-table__row) { cursor: pointer; }
.upload-record-view :deep(.el-table__row.active-row > td.el-table__cell) { background: var(--adm-primary-bg); }
.thumb {
  width: 50px;
  height: 50px;
  border-radius: 8px;
  background: linear-gradient(135deg, #e2e8f0, #cbd5e1);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-muted);
  border: 1px solid var(--adm-border);
  overflow: hidden;
}
.thumb-img { width: 100%; height: 100%; object-fit: cover; }
.student-cell { display: flex; align-items: center; gap: 10px; }
.stu-avatar-sm {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  font-weight: 600;
  color: #fff;
  flex-shrink: 0;
}
.student-name { font-weight: 500; }
.student-grade { font-size: 11px; color: var(--adm-text-muted); }
.img-count { color: var(--adm-text-secondary); font-weight: 500; }
.row-actions { display: inline-flex; align-items: center; gap: 4px; }
.row-actions .el-button + .el-button { margin-left: 0; }
</style>
