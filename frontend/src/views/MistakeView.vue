<template>
  <div class="adm-fade-in mistake-view">
    <PageHeader title="错题管理" description="查看错题记录，修改学科、年级与所属学生，迁移图片">
      <template #actions>
        <el-button :icon="Refresh" @click="loadAll">刷新</el-button>
      </template>
    </PageHeader>

    <MistakeStatCards :counts="stats" :loading="statsLoading" :active="filterReviewStatus" @select="setFilterStatus" />

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
            <span class="option-meta">ID: {{ s.id }} · {{ getGradeLabel(s.grade) }}</span>
          </el-option>
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-select v-model="filterSubject" class="filter-control" placeholder="全部学科" clearable @change="onFilterChange">
          <el-option v-for="s in subjectOptions" :key="s.value" :label="s.label" :value="s.value" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-select v-model="filterGrade" class="filter-control" placeholder="全部年级" clearable @change="onFilterChange">
          <el-option v-for="g in gradeOptions" :key="g.value" :label="g.label" :value="g.value" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-select v-model="filterReviewStatus" class="filter-control filter-review" placeholder="审核状态：全部" clearable @change="onFilterChange">
          <el-option v-for="s in reviewStatusOptions" :key="s.value" :label="s.label" :value="s.value" />
        </el-select>
      </el-form-item>
    </FilterBar>

    <DataTableCard
      :data="mistakes"
      :loading="loading"
      :error="loadError"
      empty-text="暂无错题记录"
      row-key="id"
      :row-class-name="rowClassName"
      @retry="loadMistakes"
    >
      <el-table-column label="首图" width="84">
        <template #default="{ row }">
          <div class="thumb">
            <img v-if="row.firstImagePath" :src="getMistakeImageUrl(row.firstImagePath, 'small')" :alt="row.studentName" class="thumb-img" @error="hideBrokenImage" />
            <el-icon v-else :size="22"><Picture /></el-icon>
          </div>
        </template>
      </el-table-column>
      <el-table-column label="学生" min-width="150">
        <template #default="{ row }">
          <div class="student-cell">
            <div class="stu-avatar-sm" :style="{ background: getAvatarGradient(row.studentName || row.studentId) }">{{ getAvatarChar(row.studentName || '?') }}</div>
            <div>
              <div class="student-name">{{ row.studentName || '未知' }}</div>
              <div class="student-grade">{{ getGradeLabel(row.grade) }}</div>
            </div>
          </div>
        </template>
      </el-table-column>
      <el-table-column label="学科" width="90">
        <template #default="{ row }"><SubjectTag :subject="row.subject" /></template>
      </el-table-column>
      <el-table-column label="年级" width="90">
        <template #default="{ row }">{{ getGradeLabel(row.grade) }}</template>
      </el-table-column>
      <el-table-column label="审核状态" width="110">
        <template #default="{ row }">
          <StatusTag :label="getReviewStatusLabel(row.reviewStatus)" :type="getReviewStatusTagType(row.reviewStatus)" />
        </template>
      </el-table-column>
      <el-table-column label="图片数" width="90">
        <template #default="{ row }"><span class="img-count">{{ row.imageCount || 0 }} 张</span></template>
      </el-table-column>
      <el-table-column label="创建时间" width="160">
        <template #default="{ row }"><span class="time-cell">{{ formatDateTime(row.createdAt) }}</span></template>
      </el-table-column>
      <el-table-column label="操作" width="130" align="right" fixed="right">
        <template #default="{ row }">
          <el-button link type="primary" @click="detailMistakeId = row.id">详情</el-button>
          <el-button link type="primary" @click="detailMistakeId = row.id">编辑</el-button>
        </template>
      </el-table-column>
      <template v-if="mistakes.length > 0" #footer>
        <ListPagination v-model:page="page" v-model:page-size="pageSize" :total="total" @change="loadMistakes" />
      </template>
    </DataTableCard>

    <MistakeDetailDialog
      :mistake-id="detailMistakeId"
      :subject-options="subjectOptions"
      :grade-options="gradeOptions"
      :grade-label="getGradeLabel"
      :review-status-label="getReviewStatusLabel"
      :type-label="getTypeLabel"
      :after-save="reloadAfterSave"
      :after-migrate="loadMistakes"
      @close="detailMistakeId = null"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { Picture, Refresh } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import studentAdminApi from '../services/studentAdminApi'
import type { MistakeItemDto, StudentDto } from '../services/studentAdminApi'
import { getAvatarGradient, getAvatarChar, formatDateTime } from '../utils/subject'
import { parseEnumQuery, useEnumQueryFilter } from '../utils/routeQuery'
import PageHeader from '../components/list/PageHeader.vue'
import FilterBar from '../components/list/FilterBar.vue'
import DataTableCard from '../components/list/DataTableCard.vue'
import ListPagination from '../components/list/ListPagination.vue'
import StatusTag from '../components/list/StatusTag.vue'
import SubjectTag from '../components/list/SubjectTag.vue'
import MistakeStatCards, { type MistakeStats } from './mistakes/MistakeStatCards.vue'
import MistakeDetailDialog from './mistakes/MistakeDetailDialog.vue'
import {
  DEFAULT_REVIEW_STATUS_OPTIONS, type Option,
  getReviewStatusTagType, getMistakeImageUrl, hideBrokenImage, getErrorMessage,
} from './mistakes/mistake'

const REVIEW_STATUS_VALUES = [1, 2, 3] as const
const route = useRoute()

const mistakes = ref<MistakeItemDto[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)

// Filters
const filterStudentId = ref<string | undefined>(undefined)
const filterStudentOptions = ref<StudentDto[]>([])
const filterSubject = ref<number | undefined>(undefined)
const filterGrade = ref<number | undefined>(undefined)
// Initialised from ?reviewStatus= so the first list request already carries the deep-link filter.
const filterReviewStatus = ref<number | undefined>(parseEnumQuery(route.query.reviewStatus, REVIEW_STATUS_VALUES))
let filterSearchTimer: number | null = null

// Stats
const statsLoading = ref(false)
const stats = ref<MistakeStats>({ total: 0, pending: 0, confirmed: 0, rejected: 0 })

// Options
const subjectOptions = ref<Option[]>([])
const gradeOptions = ref<Option[]>([])
const reviewStatusOptions = ref<Option[]>([...DEFAULT_REVIEW_STATUS_OPTIONS])
const classificationOptions = ref<Option[]>([])

const detailMistakeId = ref<string | null>(null)

// ===== Data loading =====
async function loadEnumOptions() {
  try {
    const enums = await studentAdminApi.getEnumOptions()
    const toOptions = (items: any[]) => items.map((s: any) => ({ value: s.value, label: s.label || s.displayName }))
    if (enums.subjects) subjectOptions.value = toOptions(enums.subjects)
    if (enums.grades) gradeOptions.value = toOptions(enums.grades)
    if (enums.reviewStatuses && enums.reviewStatuses.length > 0) reviewStatusOptions.value = toOptions(enums.reviewStatuses)
    if (enums.classifications) classificationOptions.value = toOptions(enums.classifications)
  } catch (error) {
    console.error('Failed to load enum options:', error)
  }
}

async function loadMistakes() {
  loading.value = true
  loadError.value = null
  try {
    const result = await studentAdminApi.getMistakeItems({
      page: page.value,
      size: pageSize.value,
      studentId: filterStudentId.value || undefined,
      subject: filterSubject.value,
      grade: filterGrade.value,
      reviewStatus: filterReviewStatus.value,
    })
    mistakes.value = result.items
    total.value = result.total
  } catch (error) {
    // Drop stale rows so a failure is never mistaken for an (outdated) result.
    mistakes.value = []
    total.value = 0
    loadError.value = getErrorMessage(error, '加载错题列表失败')
    ElMessage.error(loadError.value)
  } finally {
    loading.value = false
  }
}

async function loadStats() {
  statsLoading.value = true
  try {
    const [all, p1, p2, p3] = await Promise.all([
      studentAdminApi.getMistakeItems({ page: 1, size: 1 }),
      studentAdminApi.getMistakeItems({ page: 1, size: 1, reviewStatus: 1 }),
      studentAdminApi.getMistakeItems({ page: 1, size: 1, reviewStatus: 2 }),
      studentAdminApi.getMistakeItems({ page: 1, size: 1, reviewStatus: 3 }),
    ])
    stats.value = { total: all.total, pending: p1.total, confirmed: p2.total, rejected: p3.total }
  } catch (error) {
    console.error('Failed to load stats:', error)
  } finally {
    statsLoading.value = false
  }
}

async function loadAll() {
  await Promise.all([loadMistakes(), loadStats()])
}

// Same order as before: list, then stats (the dialog re-fetches the detail afterwards).
async function reloadAfterSave() {
  await loadMistakes()
  await loadStats()
}

// ===== Helpers =====
// Pending-review rows are highlighted, as before.
function rowClassName({ row }: { row: MistakeItemDto }) {
  return row.reviewStatus === 1 ? 'pending-row' : ''
}

function getGradeLabel(grade: number) {
  return gradeOptions.value.find(g => g.value === grade)?.label || (grade ? `年级${grade}` : '-')
}

function getReviewStatusLabel(status: number) {
  return reviewStatusOptions.value.find(s => s.value === status)?.label || `状态${status}`
}

function getTypeLabel(type: number) {
  return classificationOptions.value.find(c => c.value === type)?.label || `类型${type}`
}

// ===== Filters =====
const { syncToUrl: syncReviewStatusToUrl } = useEnumQueryFilter('reviewStatus', REVIEW_STATUS_VALUES, filterReviewStatus, () => {
  page.value = 1
  loadMistakes()
})

function setFilterStatus(value: number | undefined) {
  filterReviewStatus.value = filterReviewStatus.value === value ? undefined : value
  syncReviewStatusToUrl()
  page.value = 1
  loadMistakes()
}

function onFilterChange() {
  syncReviewStatusToUrl()
  page.value = 1
  loadMistakes()
}

function onSearch() {
  page.value = 1
  loadMistakes()
}

function resetFilters() {
  filterReviewStatus.value = undefined
  syncReviewStatusToUrl()
  filterSubject.value = undefined
  filterGrade.value = undefined
  filterStudentId.value = undefined
  filterStudentOptions.value = []
  selectedFilterStudent.value = null
  page.value = 1
  loadMistakes()
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
      const result = await studentAdminApi.getStudents({ name, pageSize: 20 })
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

// Selecting or clearing a student reloads from page 1 right away, as before.
function onStudentChange(studentId: string | undefined) {
  const student = studentId ? studentFilterOptions.value.find((s) => s.id === studentId) : undefined
  selectedFilterStudent.value = student ?? null
  filterStudentId.value = student?.id
  filterStudentOptions.value = []
  page.value = 1
  loadMistakes()
}

onMounted(async () => {
  await loadEnumOptions()
  await loadAll()
})
</script>

<style scoped>
.filter-student { width: 220px; }
.filter-control { width: 140px; }
.filter-review { width: 160px; }
.option-name { margin-right: 8px; color: var(--adm-text-primary); }
.option-meta { color: var(--adm-text-tertiary); font-size: 12px; }
.mistake-view :deep(.el-table__row.pending-row > td.el-table__cell) { background: var(--adm-warning-bg); }
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
.student-grade { font-size: 11px; color: var(--adm-text-muted); margin-top: 2px; }
.img-count { color: var(--adm-text-secondary); font-weight: 500; }
</style>
