<template>
  <div class="assign-form">
    <div class="panel-section-title">
      指派为错题
      <el-button v-if="canApplyVl" link type="primary" class="apply-vl" @click="emit('applyVl')">应用 VL 分组</el-button>
    </div>
    <el-form label-position="top" @submit.prevent>
      <div class="form-row-2">
        <el-form-item label="学科" required>
          <el-select v-model="subjectModel" placeholder="请选择学科">
            <el-option v-for="s in subjectOptions" :key="s.value" :label="s.displayName || s.name" :value="s.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="年级" required>
          <el-select v-model="gradeModel" placeholder="请选择年级">
            <el-option v-for="g in gradeOptions" :key="g.value" :label="g.label" :value="g.value" />
          </el-select>
        </el-form-item>
      </div>
      <el-form-item label="备注">
        <el-input v-model="comments" type="textarea" :rows="2" placeholder="添加审核备注（可选）..." />
      </el-form-item>
    </el-form>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import type { GradeOption, SubjectOption } from '../../services/studentAdminApi'

defineProps<{
  subjectOptions: SubjectOption[]
  gradeOptions: GradeOption[]
  canApplyVl: boolean
}>()

// 0 means "not selected" (validated by the caller); the selects show their placeholder for it.
const subject = defineModel<number>('subject', { required: true })
const grade = defineModel<number>('grade', { required: true })
const comments = defineModel<string>('comments', { required: true })

const subjectModel = computed({
  get: () => subject.value || undefined,
  set: (v: number | undefined) => { subject.value = v ?? 0 },
})
const gradeModel = computed({
  get: () => grade.value || undefined,
  set: (v: number | undefined) => { grade.value = v ?? 0 },
})

const emit = defineEmits<{
  applyVl: []
}>()
</script>

<style scoped>
.assign-form {
  border-top: 1px solid var(--adm-border);
  padding-top: 16px;
  margin-top: 8px;
}
.panel-section-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--adm-text-primary);
  margin-bottom: 10px;
  display: flex;
  align-items: center;
  gap: 6px;
}
.apply-vl {
  margin-left: 4px;
  font-size: 12px;
}
.form-row-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}
</style>
