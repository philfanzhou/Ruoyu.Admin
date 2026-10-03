<template>
  <div class="adm-fade-in oss-audit-view">
    <PageHeader title="OSS 文件审计" description="查看存储对象引用核对的观察结果">
      <template #actions>
        <el-button
          type="warning"
          :icon="Search"
          :loading="auditLoading"
          :disabled="auditStatus.isRunning"
          @click="handleTriggerAudit"
        >
          {{ auditLoading ? '触发中...' : (auditStatus.isRunning ? '扫描进行中' : '触发全量扫描') }}
        </el-button>
      </template>
    </PageHeader>

    <el-alert type="info" show-icon :closable="false" class="path-hint">
      未观察到引用只表示扫描时的结果，不能据此删除文件。
    </el-alert>

    <AuditStatusPanel v-if="statusLoaded" :status="auditStatus" :ignored-count="ignoredCount" />

    <FilterBar @search="onSearch" @reset="resetFilters">
      <el-form-item>
        <el-select v-model="filterStatus" class="filter-control" placeholder="状态：全部" clearable @change="handleFilterChange">
          <el-option v-for="s in AUDIT_STATUSES" :key="s.value" :label="s.label" :value="s.value" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-select v-model="filterBucket" class="filter-bucket" placeholder="Bucket：全部" clearable @change="handleFilterChange">
          <el-option v-for="b in bucketOptions" :key="b" :label="b" :value="b" />
        </el-select>
      </el-form-item>
      <el-form-item>
        <el-input v-model="filterPath" class="filter-path" :prefix-icon="Search" placeholder="按文件名搜索..." @keyup.enter="onSearch" />
      </el-form-item>
    </FilterBar>

    <!-- The API has no path parameter: the path search filters the current page only. -->
    <el-alert v-if="appliedPath" type="info" show-icon :closable="false" class="path-hint">
      路径搜索仅作用于当前页结果：本页匹配 {{ records.length }} 条（共 {{ total }} 条）
    </el-alert>

    <AuditRecordTable
      :records="records"
      :loading="loading"
      :error="loadError"
      :selected-ids="selectedIds"
      :resolving-ids="resolvingIds"
      @ignore="(r) => (ignoreTarget = r)"
      @copy="copyPath"
      @retry="loadRecords"
    >
      <template #footer>
        <ListPagination v-model:page="page" v-model:page-size="pageSize" :total="total" @change="loadRecords" />
      </template>
    </AuditRecordTable>

    <IgnoreRecordDialog :record="ignoreTarget" @close="ignoreTarget = null" @ignored="onIgnored" />
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useRoute } from 'vue-router'
import { Search } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import {
  ossAuditApi,
  getOssAuditErrorMessage,
  type OssAuditRecordDto,
} from '../services/ossAuditApi'
import { parseEnumQuery, useEnumQueryFilter } from '../utils/routeQuery'
import PageHeader from '../components/list/PageHeader.vue'
import FilterBar from '../components/list/FilterBar.vue'
import ListPagination from '../components/list/ListPagination.vue'
import AuditStatusPanel from './oss-audit/AuditStatusPanel.vue'
import AuditRecordTable from './oss-audit/AuditRecordTable.vue'
import IgnoreRecordDialog from './oss-audit/IgnoreRecordDialog.vue'
import { AUDIT_STATUSES, AUDIT_STATUS_VALUES } from './oss-audit/ossAudit'
import { useAuditStatus } from './oss-audit/useAuditStatus'

const route = useRoute()

const records = ref<OssAuditRecordDto[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const ignoredCount = ref(0)

// Initialised from ?status= so the first list request already carries the deep-link filter.
const filterStatus = ref<number | undefined>(parseEnumQuery(route.query.status, AUDIT_STATUS_VALUES))
const filterBucket = ref<string>()
const filterPath = ref<string>('')
// Path keyword of the rows currently shown (the input may have changed since).
const appliedPath = ref('')
const bucketOptions = ref<string[]>([])

// v1 observations never authorize object deletion or selection.
const selectedIds = ref<number[]>([])
const resolvingIds = ref<Set<number>>(new Set())

const ignoreTarget = ref<OssAuditRecordDto | null>(null)

// Scan status, 10 s polling (reloads records when a scan completes) and the trigger.
const { auditStatus, statusLoaded, auditLoading, loadAuditStatus, handleTriggerAudit } = useAuditStatus(loadRecords)

async function copyPath(path: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(path)
    ElMessage.success('已复制路径')
  } catch {
    ElMessage.error('复制失败')
  }
}

// ===== Data Loading =====

async function loadRecords(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    const result = await ossAuditApi.getRecords(
      page.value,
      pageSize.value,
      filterStatus.value,
      filterBucket.value
    )
    bucketOptions.value = Object.keys(result.bucketCounts)
    ignoredCount.value = result.statusCounts?.[2] ?? 0
    // Apply client-side path filter (API does not support path search)
    let items = result.items
    const kw = filterPath.value.trim().toLowerCase()
    if (kw) {
      items = items.filter((r) => r.objectPath.toLowerCase().includes(kw))
    }
    records.value = items
    appliedPath.value = kw
    total.value = result.totalCount
    selectedIds.value = []
  } catch (error) {
    // The error state shows no rows, so nothing may stay selected either.
    records.value = []
    selectedIds.value = []
    appliedPath.value = ''
    total.value = 0
    loadError.value = getOssAuditErrorMessage(error)
    ElMessage.error(loadError.value)
  } finally {
    loading.value = false
  }
}

// ===== Actions =====

const { syncToUrl: syncStatusToUrl } = useEnumQueryFilter('status', AUDIT_STATUS_VALUES, filterStatus, () => {
  page.value = 1
  loadRecords()
})

function handleFilterChange(): void {
  syncStatusToUrl()
  page.value = 1
  loadRecords()
}

function onSearch(): void {
  page.value = 1
  loadRecords()
}

function resetFilters(): void {
  filterStatus.value = undefined
  syncStatusToUrl()
  filterBucket.value = undefined
  filterPath.value = ''
  page.value = 1
  loadRecords()
}

async function onIgnored(): Promise<void> {
  ignoreTarget.value = null
  await Promise.all([loadRecords(), loadAuditStatus()])
}

</script>

<style scoped>
/* Keep the page footer clear. */
.oss-audit-view { padding-bottom: 64px; }
.filter-control { width: 140px; }
.filter-bucket { width: 180px; }
.filter-path { width: 240px; }
.path-hint { margin-bottom: 12px; }
</style>
