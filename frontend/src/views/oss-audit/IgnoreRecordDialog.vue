<template>
  <el-dialog
    :model-value="record !== null"
    title="忽略文件"
    width="480px"
    append-to-body
    :close-on-click-modal="false"
    @update:model-value="(v: boolean) => { if (!v) emit('close') }"
    @open="note = ''"
  >
    <el-form label-position="top" @submit.prevent>
      <el-form-item label="文件路径">
        <div class="file-path-display">{{ record?.bucket }}/{{ record?.objectPath }}</div>
      </el-form-item>
      <el-form-item label="忽略原因（可选）">
        <el-input v-model="note" type="textarea" :rows="3" placeholder="例如：用户头像、系统生成文件等" />
        <div class="form-helper">忽略的文件将不再出现在待处理列表中，但保留记录以便审计追溯。</div>
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="emit('close')">取消</el-button>
      <el-button type="warning" :icon="CircleClose" :loading="submitting" @click="confirmIgnore">确认忽略</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { CircleClose } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ossAuditApi, getOssAuditErrorMessage, type OssAuditRecordDto } from '../../services/ossAuditApi'

const props = defineProps<{
  record: OssAuditRecordDto | null
}>()

const emit = defineEmits<{
  close: []
  /** The record was ignored; the parent closes the dialog and reloads. */
  ignored: []
}>()

const note = ref('')
const submitting = ref(false)

async function confirmIgnore() {
  if (!props.record) return
  submitting.value = true
  try {
    const result = await ossAuditApi.ignoreRecord(props.record.id, note.value || undefined)
    if (result.success) {
      ElMessage.success('已忽略')
      emit('ignored')
    } else {
      ElMessage.error(result.message || '操作失败')
    }
  } catch (error) {
    ElMessage.error(getOssAuditErrorMessage(error))
  } finally {
    submitting.value = false
  }
}
</script>

<style scoped>
.file-path-display {
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
</style>
