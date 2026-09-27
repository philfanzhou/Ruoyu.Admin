<template>
  <el-dialog
    :model-value="record !== null"
    title="遗留数据检查"
    width="500px"
    append-to-body
    :close-on-click-modal="false"
    @update:model-value="(v: boolean) => { if (!v) emit('close') }"
  >
    <div v-if="!result" class="checking">
      <el-icon class="is-loading"><Loading /></el-icon>
      <span>检查中...</span>
    </div>
    <template v-else>
      <el-alert
        :type="result.isLegacy ? 'warning' : 'success'"
        :title="result.message"
        :closable="false"
        show-icon
      />
      <el-descriptions v-if="result.isLegacy" :column="1" border size="small" class="legacy-desc">
        <el-descriptions-item label="关联错题数">{{ result.mistakeCount }}</el-descriptions-item>
        <el-descriptions-item label="图片未迁移">{{ result.hasUploadPathImage ? '是' : '否' }}</el-descriptions-item>
      </el-descriptions>
      <div v-if="result.isLegacy" class="legacy-hint">
        <p>清理操作将：</p>
        <ol>
          <li>迁移图片到 OSS 并更新错题中的图片路径</li>
          <li>删除该上传记录</li>
        </ol>
      </div>
    </template>
    <template #footer>
      <el-button @click="emit('close')">关闭</el-button>
      <el-button v-if="result?.isLegacy" type="danger" :loading="cleaning" @click="clean">确认清理</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { Loading } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { studentAdminClient } from '../../services/studentAdminApi'
import type { UploadRecordDto } from '../../services/studentAdminApi'
import { getErrorMessage } from './uploadRecord'

// Two steps, as before: opening runs legacy-check; only "确认清理" in this dialog calls legacy-clean.
const props = defineProps<{
  record: UploadRecordDto | null
}>()

const emit = defineEmits<{
  close: []
  cleaned: []
}>()

const result = ref<{ isLegacy: boolean; mistakeCount?: number; hasUploadPathImage?: boolean; message: string } | null>(null)
const cleaning = ref(false)

watch(
  () => props.record,
  async (record) => {
    result.value = null
    if (!record) return
    try {
      result.value = await studentAdminClient.legacyCheck(record.id)
    } catch (error) {
      ElMessage.error(getErrorMessage(error, '检查失败', true))
      emit('close')
    }
  },
)

async function clean() {
  if (!props.record) return
  cleaning.value = true
  try {
    const res = await studentAdminClient.legacyClean(props.record.id)
    if (res.success) {
      ElMessage.success('清理成功')
      emit('cleaned')
    } else {
      ElMessage.error(res.message || '清理失败')
    }
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '清理失败', true))
  } finally {
    cleaning.value = false
  }
}
</script>

<style scoped>
.checking {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 32px 0;
  color: var(--adm-text-tertiary);
  font-size: 13px;
}
.legacy-desc { margin-top: 16px; }
.legacy-hint {
  margin-top: 16px;
  padding: 12px 14px;
  background: var(--adm-surface-subtle);
  border-radius: var(--adm-radius-md);
  font-size: 12px;
  color: var(--adm-text-tertiary);
}
.legacy-hint p { margin: 0 0 6px; }
.legacy-hint ol { margin: 0; padding-left: 20px; }
.legacy-hint li { margin-bottom: 4px; }
</style>
