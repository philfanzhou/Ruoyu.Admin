<template>
  <div class="oss-audit-view">
    <el-card shadow="never">
      <template #header>
        <div class="card-header">
          <span>OSS 审计</span>
          <el-button
            type="danger"
            size="small"
            :loading="auditLoading"
            @click="handleTriggerAudit"
          >
            触发审计
          </el-button>
        </div>
      </template>

      <div class="search-bar">
        <el-select
          v-model="filterStatus"
          placeholder="状态筛选"
          style="width: 150px; margin-right: 8px"
          clearable
          @change="handleFilterChange"
        >
          <el-option
            v-for="status in statusOptions"
            :key="status.value"
            :label="status.label"
            :value="status.value"
          />
        </el-select>
        <el-select
          v-model="filterBucket"
          placeholder="Bucket 筛选"
          style="width: 180px; margin-right: 8px"
          clearable
          @change="handleFilterChange"
        >
          <el-option
            v-for="bucket in bucketOptions"
            :key="bucket"
            :label="bucket"
            :value="bucket"
          />
        </el-select>
        <el-button type="primary" @click="loadRecords">搜索</el-button>
      </div>

      <div class="action-bar" v-if="selectedRecords.length > 0">
        <el-tag type="info">已选择 {{ selectedRecords.length }} 项</el-tag>
        <el-button
          type="danger"
          size="small"
          :loading="batchLoading"
          @click="handleBatchResolve"
        >
          批量删除
        </el-button>
      </div>

      <el-table
        :data="records"
        v-loading="loading"
        size="small"
        style="margin-top: 16px"
        @selection-change="handleSelectionChange"
      >
        <el-table-column type="selection" width="55" />
        <el-table-column prop="id" label="ID" width="80" show-overflow-tooltip />
        <el-table-column prop="bucket" label="Bucket" width="150" />
        <el-table-column prop="objectPath" label="文件路径" min-width="300" show-overflow-tooltip />
        <el-table-column label="大小" width="100">
          <template #default="{ row }">
            {{ formatFileSize(row.size) }}
          </template>
        </el-table-column>
        <el-table-column label="最后修改" width="160">
          <template #default="{ row }">
            {{ formatDate(row.lastModified) }}
          </template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="getStatusTagType(row.status)" size="small">
              {{ row.statusText }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="创建时间" width="160">
          <template #default="{ row }">
            {{ formatDate(row.createdAt) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="160" fixed="right">
          <template #default="{ row }">
            <el-button
              link
              type="danger"
              size="small"
              :disabled="row.status !== 0"
              @click="handleResolve(row)"
            >
              删除
            </el-button>
            <el-button
              link
              type="warning"
              size="small"
              :disabled="row.status !== 0"
              @click="handleIgnore(row)"
            >
              忽略
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="pagination-bar">
        <span class="total-count">总记录数：{{ total }}</span>
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

    <el-dialog v-model="showIgnoreDialog" title="忽略文件" width="500px">
      <el-form :model="ignoreForm" label-width="80px">
        <el-form-item label="文件路径">
          <span>{{ ignoreForm.objectPath }}</span>
        </el-form-item>
        <el-form-item label="备注">
          <el-input
            v-model="ignoreForm.note"
            type="textarea"
            :rows="3"
            placeholder="可选，填写忽略原因"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="showIgnoreDialog = false">取消</el-button>
        <el-button type="warning" @click="confirmIgnore">忽略</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ossAuditApi,
  getOssAuditErrorMessage,
  formatFileSize,
  type OssAuditRecordDto
} from '../services/ossAuditApi'

const records = ref<OssAuditRecordDto[]>([])
const loading = ref(false)
const auditLoading = ref(false)
const batchLoading = ref(false)
const total = ref(0)
const page = ref(1)
const pageSize = 20

const filterStatus = ref<number>()
const filterBucket = ref<string>()
const bucketOptions = ref<string[]>([])

const selectedRecords = ref<OssAuditRecordDto[]>([])

const showIgnoreDialog = ref(false)
const ignoreForm = ref({
  id: 0,
  objectPath: '',
  note: ''
})

const statusOptions = [
  { value: 0, label: '待处理' },
  { value: 1, label: '已删除' },
  { value: 2, label: '已忽略' }
]

function formatDate(timestamp: number) {
  if (!timestamp) return '-'
  return new Date(timestamp).toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  })
}

function getStatusTagType(status: number) {
  const typeMap: Record<number, string> = {
    0: 'info',
    1: 'success',
    2: 'warning'
  }
  return typeMap[status] || 'info'
}

async function loadRecords() {
  loading.value = true
  try {
    const result = await ossAuditApi.getRecords(
      page.value,
      pageSize,
      filterStatus.value,
      filterBucket.value
    )
    records.value = result.items
    total.value = result.totalCount
    bucketOptions.value = Object.keys(result.bucketCounts)
  } catch (error) {
    ElMessage.error(getOssAuditErrorMessage(error))
  } finally {
    loading.value = false
  }
}

async function handleTriggerAudit() {
  try {
    await ElMessageBox.confirm(
      '确定要触发全量 OSS 扫描吗？这可能需要较长时间。',
      '确认',
      { type: 'warning' }
    )
    auditLoading.value = true
    const result = await ossAuditApi.triggerAudit()
    if (result.success) {
      ElMessage.success(result.message || '审计任务已触发')
      await loadRecords()
    } else {
      ElMessage.error(result.message || '触发失败')
    }
  } catch (error: any) {
    if (error !== 'cancel') {
      ElMessage.error(getOssAuditErrorMessage(error))
    }
  } finally {
    auditLoading.value = false
  }
}

function handleFilterChange() {
  page.value = 1
  loadRecords()
}

function onPageChange(p: number) {
  page.value = p
  loadRecords()
}

function handleSelectionChange(selection: OssAuditRecordDto[]) {
  selectedRecords.value = selection
}

async function handleResolve(record: OssAuditRecordDto) {
  try {
    await ElMessageBox.confirm(
      `确定要删除文件 "${record.objectPath}" 吗？`,
      '确认删除',
      { type: 'warning' }
    )
    const result = await ossAuditApi.resolveRecord(record.id)
    if (result.success) {
      ElMessage.success('删除成功')
      await loadRecords()
    } else {
      ElMessage.error(result.message || '删除失败')
    }
  } catch (error: any) {
    if (error !== 'cancel') {
      ElMessage.error(getOssAuditErrorMessage(error))
    }
  }
}

function handleIgnore(record: OssAuditRecordDto) {
  ignoreForm.value = {
    id: record.id,
    objectPath: record.objectPath,
    note: ''
  }
  showIgnoreDialog.value = true
}

async function confirmIgnore() {
  try {
    const result = await ossAuditApi.ignoreRecord(
      ignoreForm.value.id,
      ignoreForm.value.note || undefined
    )
    if (result.success) {
      ElMessage.success('已忽略')
      showIgnoreDialog.value = false
      await loadRecords()
    } else {
      ElMessage.error(result.message || '操作失败')
    }
  } catch (error) {
    ElMessage.error(getOssAuditErrorMessage(error))
  }
}

async function handleBatchResolve() {
  if (selectedRecords.value.length === 0) {
    ElMessage.warning('请选择要删除的记录')
    return
  }

  try {
    await ElMessageBox.confirm(
      `确定要删除选中的 ${selectedRecords.value.length} 条记录吗？`,
      '确认批量删除',
      { type: 'warning' }
    )
    batchLoading.value = true
    const ids = selectedRecords.value.map(r => r.id)
    const result = await ossAuditApi.batchResolve(ids)

    if (result.resolvedCount > 0) {
      ElMessage.success(`成功删除 ${result.resolvedCount} 条记录`)
    }
    if (result.errors.length > 0) {
      ElMessage.warning(`有 ${result.errors.length} 条记录删除失败`)
    }
    selectedRecords.value = []
    await loadRecords()
  } catch (error: any) {
    if (error !== 'cancel') {
      ElMessage.error(getOssAuditErrorMessage(error))
    }
  } finally {
    batchLoading.value = false
  }
}

onMounted(async () => {
  await loadRecords()
})
</script>

<style scoped>
.oss-audit-view {
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

.action-bar {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-top: 16px;
  padding: 12px;
  background-color: #f5f7fa;
  border-radius: 4px;
}

.pagination-bar {
  margin-top: 16px;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.total-count {
  color: #606266;
  font-size: 14px;
}
</style>
