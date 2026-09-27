<template>
  <el-dialog
    :model-value="modelValue"
    title="重置状态"
    width="400px"
    append-to-body
    :close-on-click-modal="false"
    @update:model-value="emit('update:modelValue', $event)"
    @open="targetStatus = initialStatus"
  >
    <el-form label-position="top" @submit.prevent>
      <el-form-item label="目标状态" required>
        <el-select v-model="targetStatus" class="full-width">
          <el-option v-for="s in UPLOAD_STATUSES" :key="s.value" :label="s.label" :value="s.value" />
        </el-select>
      </el-form-item>
    </el-form>
    <p class="modal-hint">重置后记录将回到指定状态，已有错题关联不会受影响。</p>
    <template #footer>
      <el-button @click="emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="loading" @click="emit('confirm', targetStatus)">确认重置</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { UPLOAD_STATUSES } from './uploadRecord'

const props = defineProps<{
  modelValue: boolean
  initialStatus: number
  loading: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  confirm: [targetStatus: number]
}>()

const targetStatus = ref(props.initialStatus)
</script>

<style scoped>
.full-width { width: 100%; }
.modal-hint {
  margin: 0;
  font-size: 12px;
  color: var(--adm-text-muted);
  line-height: 1.6;
}
</style>
