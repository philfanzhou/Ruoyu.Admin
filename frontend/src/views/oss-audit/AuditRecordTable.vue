<template>
  <!-- Presentational: selection lives in the parent (`selectedIds` is the single source of truth). -->
  <DataTableCard
    :data="records"
    :loading="loading"
    :error="error"
    empty-text="暂无审计记录"
    row-key="id"
    :row-class-name="rowClassName"
    @retry="emit('retry')"
  >
    <!-- Custom checkbox column rather than el-table's built-in selection column: that one keeps
         a second, internal selection which DataTableCard gives us no handle to clear. -->
    <el-table-column width="48" align="center">
      <template #header>
        <el-checkbox
          :model-value="allSelected"
          :indeterminate="someSelected"
          :disabled="selectableIds.length === 0"
          title="选择本页全部待处理记录"
          aria-label="选择本页全部待处理记录"
          @change="emit('toggleAll')"
        />
      </template>
      <template #default="{ row }">
        <el-checkbox
          :model-value="selectedIds.includes(row.id)"
          :disabled="row.status !== 0"
          :title="row.status !== 0 ? '仅待处理记录可选择' : undefined"
          :aria-label="`选择 ${row.objectPath}`"
          @change="emit('toggle', row)"
        />
      </template>
    </el-table-column>
    <el-table-column label="ID" width="80">
      <template #default="{ row }"><span class="mono">{{ row.id }}</span></template>
    </el-table-column>
    <el-table-column label="Bucket" width="140">
      <template #default="{ row }"><span class="bucket-tag" :style="getBucketStyle(row.bucket)">{{ row.bucket }}</span></template>
    </el-table-column>
    <el-table-column label="文件路径" min-width="240">
      <template #default="{ row }">
        <div class="path-cell">
          <div class="thumb-mini" :style="{ opacity: row.status === 1 ? 0.4 : 1 }">
            <img v-if="isImagePath(row.objectPath)" :src="getImageUrl(row.objectPath)" :alt="row.objectPath" class="thumb-img" @error="hideBrokenImage" />
            <el-icon v-else :size="16"><Document /></el-icon>
          </div>
          <div class="path-info">
            <div class="file-path" :class="{ strike: row.status === 1 }">
              <span class="file-path-text" :title="row.objectPath">{{ row.objectPath }}</span>
              <el-button link :icon="CopyDocument" title="复制路径" aria-label="复制路径" @click="emit('copy', row.objectPath)" />
            </div>
            <span v-if="row.status === 2 && row.note" class="ignore-reason-inline">忽略原因：{{ row.note }}</span>
          </div>
        </div>
      </template>
    </el-table-column>
    <el-table-column label="大小" width="100">
      <template #default="{ row }"><span class="mono" :class="{ strike: row.status === 1 }">{{ formatFileSize(row.size) }}</span></template>
    </el-table-column>
    <el-table-column label="最后修改" width="150">
      <template #default="{ row }"><span class="time-cell">{{ formatDateTime(row.lastModified) }}</span></template>
    </el-table-column>
    <el-table-column label="状态" width="100">
      <template #default="{ row }">
        <StatusTag :label="getAuditStatusMeta(row.status).label" :type="getAuditStatusMeta(row.status).tagType" />
      </template>
    </el-table-column>
    <el-table-column label="发现时间" width="150">
      <template #default="{ row }"><span class="time-cell">{{ formatDateTime(row.createdAt) }}</span></template>
    </el-table-column>
    <el-table-column label="操作" width="140" align="right" fixed="right">
      <template #default="{ row }">
        <template v-if="row.status === 0">
          <el-button link type="danger" :disabled="resolvingIds.has(row.id)" @click="emit('resolve', row)">
            {{ resolvingIds.has(row.id) ? '删除中…' : '删除' }}
          </el-button>
          <el-button link type="warning" :disabled="resolvingIds.has(row.id)" @click="emit('ignore', row)">忽略</el-button>
        </template>
        <span v-else class="text-muted">—</span>
      </template>
    </el-table-column>
    <template v-if="$slots.footer && records.length > 0" #footer>
      <slot name="footer" />
    </template>
  </DataTableCard>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { CopyDocument, Document } from '@element-plus/icons-vue'
import { formatFileSize, type OssAuditRecordDto } from '../../services/ossAuditApi'
import { formatDateTime } from '../../utils/subject'
import DataTableCard from '../../components/list/DataTableCard.vue'
import StatusTag from '../../components/list/StatusTag.vue'
import { getAuditStatusMeta, getBucketStyle, getImageUrl, isImagePath } from './ossAudit'

const props = defineProps<{
  records: OssAuditRecordDto[]
  loading: boolean
  error: string | null
  selectedIds: number[]
  resolvingIds: Set<number>
}>()

const emit = defineEmits<{
  toggle: [record: OssAuditRecordDto]
  toggleAll: []
  resolve: [record: OssAuditRecordDto]
  ignore: [record: OssAuditRecordDto]
  copy: [path: string]
  retry: []
}>()

const selectableIds = computed(() => props.records.filter((r) => r.status === 0).map((r) => r.id))
const allSelected = computed(() => selectableIds.value.length > 0 && selectableIds.value.every((id) => props.selectedIds.includes(id)))
const someSelected = computed(() => !allSelected.value && props.selectedIds.length > 0)

function rowClassName({ row }: { row: OssAuditRecordDto }) {
  return props.selectedIds.includes(row.id) ? 'selected-row' : ''
}

function hideBrokenImage(e: Event) {
  ;(e.target as HTMLImageElement).style.display = 'none'
}
</script>

<style scoped>
:deep(.el-table__row.selected-row > td.el-table__cell) { background: var(--adm-primary-bg); }
.mono {
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--adm-text-secondary);
}
.strike {
  color: var(--adm-text-muted) !important;
  text-decoration: line-through;
}
.bucket-tag {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 500;
  line-height: 1.6;
  background: var(--adm-primary-bg);
  color: var(--adm-primary);
  border: 1px solid var(--adm-info-border);
  white-space: nowrap;
}
.path-cell {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
}
.thumb-mini {
  width: 32px;
  height: 32px;
  border-radius: 4px;
  background: linear-gradient(135deg, #e2e8f0, #cbd5e1);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-muted);
  flex-shrink: 0;
  border: 1px solid var(--adm-border);
  overflow: hidden;
}
.thumb-img { width: 100%; height: 100%; object-fit: cover; }
.path-info { min-width: 0; }
.file-path {
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--adm-text-secondary);
  display: flex;
  align-items: center;
  gap: 6px;
}
.file-path-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ignore-reason-inline {
  display: block;
  font-size: 11px;
  color: #a16207;
  margin-top: 2px;
}
</style>
