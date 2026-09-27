<template>
  <div class="adm-fade-in oss-audit-view">
    <PageHeader title="OSS 文件审计" description="扫描 OSS 存储中的孤儿文件、异常文件，清理冗余存储">
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
      @toggle="toggleSelect"
      @toggle-all="toggleSelectAll"
      @resolve="handleResolve"
      @ignore="(r) => (ignoreTarget = r)"
      @copy="copyPath"
      @retry="loadRecords"
    >
      <template #footer>
        <ListPagination v-model:page="page" v-model:page-size="pageSize" :total="total" @change="loadRecords" />
      </template>
    </AuditRecordTable>

    <AuditBulkBar
      :count="selectedIds.length"
      :loading="batchLoading"
      @resolve="handleBatchResolve"
      @ignore="handleBatchIgnore"
      @cancel="clearSelection"
    />

    <IgnoreRecordDialog :record="ignoreTarget" @close="ignoreTarget = null" @ignored="onIgnored" />
  </div>
</template>

<script setup lang="ts">
import { ref, h } from 'vue'
import { useRoute } from 'vue-router'
import { Search } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { confirmDanger } from '../utils/confirm'
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
import AuditBulkBar from './oss-audit/AuditBulkBar.vue'
import IgnoreRecordDialog from './oss-audit/IgnoreRecordDialog.vue'
import { AUDIT_STATUSES, AUDIT_STATUS_VALUES } from './oss-audit/ossAudit'
import { useAuditStatus } from './oss-audit/useAuditStatus'

const route = useRoute()

const records = ref<OssAuditRecordDto[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)
const batchLoading = ref(false)
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

// Invariant: selectedIds ⊆ ids of the pending (status 0) rows currently displayed.
// Only pending rows are selectable, and the selection is cleared whenever
// loadRecords replaces or clears `records` (paging, page size, filters, search,
// reset, refresh, polling, retry — on success and on failure).
const selectedIds = ref<number[]>([])
// Rows with a single-record delete in flight; their actions are disabled.
const resolvingIds = ref<Set<number>>(new Set())

const ignoreTarget = ref<OssAuditRecordDto | null>(null)

// Scan status, 10 s polling (reloads records when a scan completes) and the trigger.
const { auditStatus, statusLoaded, auditLoading, loadAuditStatus, handleTriggerAudit } = useAuditStatus(loadRecords)

// ===== Selection =====

function toggleSelect(record: OssAuditRecordDto): void {
  if (record.status !== 0) return
  const id = record.id
  const idx = selectedIds.value.indexOf(id)
  if (idx >= 0) {
    selectedIds.value.splice(idx, 1)
  } else {
    selectedIds.value.push(id)
  }
}

function toggleSelectAll(): void {
  const selectableIds = records.value.filter((r) => r.status === 0).map((r) => r.id)
  const allSelected = selectableIds.length > 0 && selectableIds.every((id) => selectedIds.value.includes(id))
  selectedIds.value = allSelected ? [] : selectableIds
}

function clearSelection(): void {
  selectedIds.value = []
}

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

async function handleResolve(record: OssAuditRecordDto): Promise<void> {
  if (resolvingIds.value.has(record.id)) return
  const confirmed = await confirmDanger({
    title: '删除存储文件',
    // Inline styles: this VNode renders inside the message box, outside this component's scoped CSS.
    message: h('div', [
      h('p', { style: 'margin: 0 0 8px' }, '将从对象存储中永久删除以下文件及其缩略图：'),
      h('p', { style: 'margin: 0 0 8px' }, h('code', { style: 'word-break: break-all' }, `${record.bucket}/${record.objectPath}`)),
      h('p', { style: 'margin: 0' }, '删除前服务端会复核引用，文件仍被引用时会拒绝删除。'),
    ]),
    confirmText: '删除',
  })
  if (!confirmed || resolvingIds.value.has(record.id)) return
  resolvingIds.value.add(record.id)
  try {
    const result = await ossAuditApi.resolveRecord(record.id)
    if (result.success) {
      ElMessage.success('删除成功')
      await Promise.all([loadRecords(), loadAuditStatus()])
    } else {
      ElMessage.error(result.message || '删除失败')
    }
  } catch (error) {
    ElMessage.error(getOssAuditErrorMessage(error))
  } finally {
    resolvingIds.value.delete(record.id)
  }
}

async function onIgnored(): Promise<void> {
  ignoreTarget.value = null
  await Promise.all([loadRecords(), loadAuditStatus()])
}

async function handleBatchResolve(): Promise<void> {
  if (batchLoading.value) return
  if (selectedIds.value.length === 0) {
    ElMessage.warning('请选择要删除的记录')
    return
  }
  // Snapshot what the user confirms; a list refresh (or failure) while the dialog
  // is open clears the live selection but must not change what gets deleted.
  const ids = [...selectedIds.value]
  const confirmed = await confirmDanger({
    title: '批量删除存储文件',
    message: h('div', [
      h('p', { style: 'margin: 0 0 8px' }, ['将从对象存储中永久删除 ', h('strong', String(ids.length)), ' 个文件及其缩略图。']),
      h('p', { style: 'margin: 0' }, '删除前服务端会逐条复核引用，仍被引用的文件会被跳过并计为失败。'),
    ]),
    confirmText: `删除 ${ids.length} 个文件`,
  })
  if (!confirmed) return
  batchLoading.value = true
  try {
    const result = await ossAuditApi.batchResolve(ids)
    if (result.resolvedCount > 0) {
      ElMessage.success(`成功删除 ${result.resolvedCount} 条记录`)
    }
    if (result.errors.length > 0) {
      ElMessage.warning(`有 ${result.errors.length} 条记录删除失败`)
    }
    selectedIds.value = []
    await Promise.all([loadRecords(), loadAuditStatus()])
  } catch (error) {
    ElMessage.error(getOssAuditErrorMessage(error))
  } finally {
    batchLoading.value = false
  }
}

// Ignoring is not destructive (records stay for auditing), so it keeps a plain confirmation.
async function handleBatchIgnore(): Promise<void> {
  if (selectedIds.value.length === 0) {
    ElMessage.warning('请选择要忽略的记录')
    return
  }
  // Only pending records (status === 0) can be ignored
  const pendingRecords = records.value.filter(
    (r) => selectedIds.value.includes(r.id) && r.status === 0
  )
  if (pendingRecords.length === 0) {
    ElMessage.warning('选中的记录中没有可忽略的待处理项')
    return
  }
  try {
    await ElMessageBox.confirm(
      `确定要忽略选中的 ${pendingRecords.length} 条待处理记录吗？`,
      '确认批量忽略',
      { type: 'warning' }
    )
  } catch {
    return
  }
  batchLoading.value = true
  try {
    let successCount = 0
    let failCount = 0
    for (const r of pendingRecords) {
      try {
        const result = await ossAuditApi.ignoreRecord(r.id)
        if (result.success) successCount++
        else failCount++
      } catch {
        failCount++
      }
    }
    if (successCount > 0) {
      ElMessage.success(`成功忽略 ${successCount} 条记录`)
    }
    if (failCount > 0) {
      ElMessage.warning(`${failCount} 条记录忽略失败`)
    }
    selectedIds.value = []
    await Promise.all([loadRecords(), loadAuditStatus()])
  } catch (error) {
    ElMessage.error(getOssAuditErrorMessage(error))
  } finally {
    batchLoading.value = false
  }
}

</script>

<style scoped>
/* Room below the pagination so the floating bulk bar never covers it. */
.oss-audit-view { padding-bottom: 64px; }
.filter-control { width: 140px; }
.filter-bucket { width: 180px; }
.filter-path { width: 240px; }
.path-hint { margin-bottom: 12px; }
</style>
