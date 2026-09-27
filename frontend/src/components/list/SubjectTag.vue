<template>
  <span class="adm-subj-tag" :class="[getSubjectCssClass(subject), { closable }]">
    {{ getSubjectLabel(subject) }}
    <button
      v-if="closable"
      type="button"
      class="subj-x"
      :title="`移除 ${getSubjectLabel(subject)}`"
      :aria-label="`移除 ${getSubjectLabel(subject)}`"
      @click="emit('close')"
    >
      <el-icon :size="10"><Close /></el-icon>
    </button>
  </span>
</template>

<script setup lang="ts">
import { Close } from '@element-plus/icons-vue'
import { getSubjectCssClass, getSubjectLabel } from '../../utils/subject'

defineProps<{
  subject: number
  closable?: boolean
}>()

const emit = defineEmits<{
  close: []
}>()
</script>
