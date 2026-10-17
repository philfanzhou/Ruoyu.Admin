<template>
  <div class="adm-fade-in settings-view">
    <PageHeader title="系统设置" description="查看应用设置的有效值与来源，管理数据库覆盖" />

    <el-alert type="info" show-icon :closable="false" class="layer-hint">
      设置生效顺序：数据库覆盖 → 静态配置 → 默认值；删除覆盖后自动回退到下一层。
    </el-alert>

    <DataTableCard
      :data="settings"
      :loading="loading"
      :error="loadError"
      empty-text="暂无注册设置"
      row-key="key"
      @retry="loadSettings"
    >
      <el-table-column label="设置键" min-width="260">
        <template #default="{ row }">
          <span class="mono" :title="row.key">{{ row.key }}</span>
        </template>
      </el-table-column>
      <el-table-column label="有效值" width="120">
        <template #default="{ row }"><span class="mono">{{ row.value }}</span></template>
      </el-table-column>
      <el-table-column label="来源" width="130">
        <template #default="{ row }">
          <StatusTag :label="getSourceMeta(row.source).label" :type="getSourceMeta(row.source).tagType" />
        </template>
      </el-table-column>
      <el-table-column label="覆盖时间" width="170">
        <template #default="{ row }">
          <span class="time-cell">{{ row.source === 'db' && row.updatedAt ? formatDateTime(row.updatedAt) : '—' }}</span>
        </template>
      </el-table-column>
      <el-table-column label="操作人" width="150">
        <template #default="{ row }">
          <span :title="row.updatedBy ?? undefined">{{ row.source === 'db' && row.updatedBy ? row.updatedBy : '—' }}</span>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="150" align="right" fixed="right">
        <template #default="{ row }">
          <!-- Only a database override can be edited or removed; lower layers are read-only. -->
          <template v-if="row.source === 'db'">
            <el-button link type="primary" title="编辑" @click="openEdit(row)">编辑</el-button>
            <el-button link type="warning" title="恢复默认" :disabled="restoring" @click="restoreDefault(row)">恢复默认</el-button>
          </template>
          <span v-else class="text-muted">—</span>
        </template>
      </el-table-column>
    </DataTableCard>

    <el-dialog
      v-model="editOpen"
      title="编辑覆盖值"
      width="480px"
      append-to-body
      :close-on-click-modal="false"
    >
      <el-form label-position="top" @submit.prevent>
        <el-form-item label="设置键">
          <div class="key-display">{{ editTarget?.key }}</div>
        </el-form-item>
        <el-form-item :label="`覆盖值${editRange ? `（${editRange.hint}）` : ''}`">
          <el-input v-model="editForm.value" placeholder="请输入覆盖值" @input="editError = ''" />
          <div v-if="editError" class="edit-error" role="alert">{{ editError }}</div>
        </el-form-item>
        <div class="form-helper">保存后立即生效；删除覆盖即可回退到静态配置或默认值。</div>
      </el-form>
      <template #footer>
        <el-button @click="editOpen = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitEdit">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { settingsApi, type SettingDto } from '../services/settingsApi'
import { extractApiErrorMessage } from '../services/apiBase'
import { formatDateTime } from '../utils/subject'
import PageHeader from '../components/list/PageHeader.vue'
import DataTableCard from '../components/list/DataTableCard.vue'
import StatusTag, { type StatusTagType } from '../components/list/StatusTag.vue'

// Sidebar-visible source labels. Unknown future sources fall back to a neutral
// tag showing the raw source string instead of guessing a layer name.
const SOURCE_META: Record<string, { label: string; tagType: StatusTagType }> = {
  db: { label: '数据库覆盖', tagType: 'warning' },
  static: { label: '静态配置', tagType: 'primary' },
  default: { label: '默认值', tagType: 'info' },
}

function getSourceMeta(source: string): { label: string; tagType: StatusTagType } {
  return SOURCE_META[source] ?? { label: source, tagType: 'info' }
}

// Front-end mirror of the first-version value domains from the backend
// `AppSettingsRegistry` (issue #118). The registry itself is not exposed over
// the API, so this copy carries the range and the Chinese hint; keys without an
// entry here are only checked for non-emptiness and left to the backend's
// fixed 400 message.
const SETTING_RANGES: Record<string, { min: number; max: number; hint: string }> = {
  'OssAudit:ScheduledHour': { min: 0, max: 23, hint: '本地时间小时，0-23 的整数' },
  'OssAudit:ScheduledMinute': { min: 0, max: 59, hint: '本地时间分钟，0-59 的整数' },
}

const settings = ref<SettingDto[]>([])
const loading = ref(false)
const loadError = ref<string | null>(null)

const editOpen = ref(false)
const editTarget = ref<SettingDto | null>(null)
const editForm = reactive({ value: '' })
const editError = ref('')
const saving = ref(false)
const restoring = ref(false)

const editRange = computed(() => (editTarget.value ? SETTING_RANGES[editTarget.value.key] : undefined))

// Caller-side validation (the repository pattern — see MistakeEditForm): the
// front-end mirror of the backend domain blocks obvious typos; the backend
// still validates everything and answers with its fixed 400 message.
function validateEditValue(raw: string): string | null {
  const trimmed = raw.trim()
  if (!trimmed) return '请输入覆盖值'
  const range = editRange.value
  if (!range) return null
  const message = `请输入 ${range.min}-${range.max} 之间的整数`
  if (!/^-?\d+$/.test(trimmed)) return message
  const parsed = Number(trimmed)
  if (parsed < range.min || parsed > range.max) return message
  return null
}

// ===== Data Loading =====

async function loadSettings(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    settings.value = await settingsApi.listSettings()
  } catch (error) {
    settings.value = []
    loadError.value = extractApiErrorMessage(error)
    ElMessage.error(loadError.value)
  } finally {
    loading.value = false
  }
}

// ===== Actions =====

function openEdit(row: SettingDto): void {
  editTarget.value = row
  editForm.value = row.value
  editError.value = ''
  editOpen.value = true
}

async function submitEdit(): Promise<void> {
  if (!editTarget.value) return
  // Invalid input is intercepted here and never reaches the API.
  const error = validateEditValue(editForm.value)
  if (error) {
    editError.value = error
    return
  }
  saving.value = true
  try {
    const result = await settingsApi.updateSetting(editTarget.value.key, editForm.value.trim())
    applyResult(result)
    ElMessage.success('已保存')
    editOpen.value = false
  } catch (error) {
    // Backend 400/404 messages surface verbatim through the shared extractor.
    ElMessage.error(extractApiErrorMessage(error))
  } finally {
    saving.value = false
  }
}

async function restoreDefault(row: SettingDto): Promise<void> {
  restoring.value = true
  try {
    const result = await settingsApi.deleteSetting(row.key)
    // The response is the post-delete effective setting: apply it directly so
    // the source tag and value refresh to the fallback layer.
    applyResult(result)
    ElMessage.success('已恢复默认')
  } catch (error) {
    ElMessage.error(extractApiErrorMessage(error))
  } finally {
    restoring.value = false
  }
}

function applyResult(result: SettingDto): void {
  const index = settings.value.findIndex((item) => item.key === result.key)
  if (index >= 0) settings.value[index] = result
}

loadSettings()
</script>

<style scoped>
/* Keep the page footer clear. */
.settings-view { padding-bottom: 64px; }
.layer-hint { margin-bottom: 12px; }
.mono {
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--adm-text-secondary);
}
.time-cell { font-size: 12px; color: var(--adm-text-secondary); }
.key-display {
  width: 100%;
  padding: 8px 12px;
  background: var(--adm-surface-subtle);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-sm);
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--adm-text-secondary);
  word-break: break-all;
  line-height: 1.6;
}
.form-helper {
  margin-top: 6px;
  font-size: 12px;
  color: var(--adm-text-muted);
  line-height: 1.5;
}
.edit-error {
  margin-top: 6px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--el-color-danger, #f56c6c);
}
</style>
