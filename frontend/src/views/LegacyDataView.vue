<template>
  <div class="legacy-data-view">
    <el-card shadow="never">
      <template #header>
        <div class="card-header">
          <span>历史遗留数据清理</span>
          <el-button type="primary" size="small" @click="loadLegacyData" :loading="loading">
            刷新
          </el-button>
        </div>
      </template>

      <el-alert
        title="问题说明"
        type="warning"
        :closable="false"
        show-icon
        style="margin-bottom: 16px"
      >
        <template #default>
          <div>
            <p style="margin: 0 0 8px 0">
              <strong>问题背景：</strong>系统中存在一些"错题已审核但上传记录还在"的历史遗留数据。
              这些数据需要人工清理以保持数据一致性。
            </p>
            <p style="margin: 0">
              <strong>清理操作：</strong>点击"清理"按钮将删除对应的上传记录。操作不可逆，请谨慎确认。
            </p>
          </div>
        </template>
      </el-alert>

      <el-table
        :data="legacyDataList"
        v-loading="loading"
        size="small"
        style="margin-top: 16px"
      >
        <el-table-column prop="uploadRecordId" label="上传记录ID" width="200" show-overflow-tooltip />
        <el-table-column prop="studentName" label="学生姓名" width="120" />
        <el-table-column prop="studentId" label="学生ID" width="180" show-overflow-tooltip />
        <el-table-column prop="mistakeCount" label="错题数量" width="100" align="center">
          <template #default="{ row }">
            <el-tag type="info" size="small">{{ row.mistakeCount }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="图片路径状态" width="120" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.hasUploadPathImage" type="warning" size="small">uploads/</el-tag>
            <el-tag v-else type="success" size="small">已迁移</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="createdAt" label="创建时间" width="180">
          <template #default="{ row }">
            {{ formatDate(row.createdAt) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="100" fixed="right">
          <template #default="{ row }">
            <el-button
              type="danger"
              size="small"
              :loading="cleaningId === row.uploadRecordId"
              @click="handleClean(row)"
            >
              清理
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="info-bar">
        <span>总记录数：{{ total }}</span>
      </div>

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
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import legacyDataApi from '../services/legacyDataApi'

interface LegacyDataItem {
  uploadRecordId: string
  studentName: string
  studentId: string
  mistakeCount: number
  hasUploadPathImage: boolean
  createdAt: number | string
}

const legacyDataList = ref<LegacyDataItem[]>([])
const loading = ref(false)
const total = ref(0)
const page = ref(1)
const pageSize = 20
const cleaningId = ref<string | null>(null)

async function loadLegacyData() {
  loading.value = true
  try {
    const result = await legacyDataApi.scanLegacyData({
      page: page.value,
      pageSize
    })
    legacyDataList.value = result.items || result.data || []
    total.value = result.totalCount || result.total || legacyDataList.value.length
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '加载遗留数据失败')
  } finally {
    loading.value = false
  }
}

function formatDate(dateValue: number | string): string {
  if (!dateValue) return '-'
  if (typeof dateValue === 'string') {
    if (/^\d+$/.test(dateValue.trim())) {
      return new Date(Number(dateValue) * 1000).toLocaleString('zh-CN', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit'
      })
    }
    return new Date(dateValue).toLocaleString('zh-CN', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    })
  }
  return new Date(dateValue * 1000).toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  })
}

async function handleClean(row: LegacyDataItem) {
  try {
    await ElMessageBox.confirm(
      `<div>
        <p><strong>确认清理以下数据？</strong></p>
        <p>上传记录ID：${row.uploadRecordId}</p>
        <p>学生姓名：${row.studentName}</p>
        <p>错题数量：${row.mistakeCount}</p>
        <p style="color: #E6A23C; margin-top: 12px;">⚠️ 此操作将删除对应的上传记录，操作不可逆！</p>
      </div>`,
      '清理确认',
      {
        confirmButtonText: '确认清理',
        cancelButtonText: '取消',
        type: 'warning',
        dangerouslyUseHTMLString: true
      }
    )

    cleaningId.value = row.uploadRecordId
    await legacyDataApi.cleanLegacyData(row.uploadRecordId)
    ElMessage.success('清理成功')
    await loadLegacyData()
  } catch (error: any) {
    if (error !== 'cancel' && error !== 'esc') {
      ElMessage.error(error.response?.data?.message || '清理失败')
    }
  } finally {
    cleaningId.value = null
  }
}

function onPageChange(p: number) {
  page.value = p
  loadLegacyData()
}

onMounted(() => {
  loadLegacyData()
})
</script>

<style scoped>
.legacy-data-view {
  max-width: 1400px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.info-bar {
  margin-top: 16px;
  font-size: 14px;
  color: #606266;
}

.pagination-bar {
  margin-top: 8px;
  display: flex;
  justify-content: flex-end;
}
</style>
