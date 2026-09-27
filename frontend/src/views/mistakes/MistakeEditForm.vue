<template>
  <div class="edit-form-section">
    <div class="edit-form-title">
      <el-icon><EditPen /></el-icon>
      编辑错题信息
    </div>
    <el-form label-position="top" @submit.prevent>
      <div class="form-row-2">
        <el-form-item label="学科" required>
          <el-select v-model="subjectModel" placeholder="请选择学科">
            <el-option v-for="s in subjectOptions" :key="s.value" :label="s.label" :value="s.value" />
          </el-select>
        </el-form-item>
        <el-form-item label="年级" required>
          <el-select v-model="gradeModel" placeholder="请选择年级">
            <el-option v-for="g in gradeOptions" :key="g.value" :label="g.label" :value="g.value" />
          </el-select>
        </el-form-item>
      </div>
      <el-form-item label="学生" required>
        <el-select
          :model-value="studentId || undefined"
          class="full-width"
          filterable
          remote
          clearable
          :remote-method="onStudentQuery"
          placeholder="搜索学生姓名或ID..."
          no-data-text="无匹配学生"
          @change="onStudentChange"
        >
          <el-option v-for="s in studentOptions" :key="s.id" :label="s.name" :value="s.id">
            <span class="option-name">{{ s.name }}</span>
            <span class="option-meta">ID: {{ s.id }}<template v-if="s.grade"> · {{ gradeLabel(s.grade) }}</template></span>
          </el-option>
        </el-select>
        <div v-if="studentId && studentName" class="selected-user-banner full-width">
          <div class="mini-avatar" :style="{ background: getAvatarGradient(studentName) }">{{ getAvatarChar(studentName) }}</div>
          <div class="sel-info">
            <div class="sel-name">{{ studentName }}</div>
            <div class="sel-meta">ID: {{ studentId }}</div>
          </div>
        </div>
      </el-form-item>
    </el-form>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { EditPen } from '@element-plus/icons-vue'
import studentAdminApi from '../../services/studentAdminApi'
import type { StudentDto } from '../../services/studentAdminApi'
import { getAvatarGradient, getAvatarChar } from '../../utils/subject'
import type { Option } from './mistake'

defineProps<{
  subjectOptions: Option[]
  gradeOptions: Option[]
  gradeLabel: (grade: number) => string
}>()

// 0 / '' mean "not selected" (validated by the caller); the selects show their placeholder for them.
const subject = defineModel<number>('subject', { required: true })
const grade = defineModel<number>('grade', { required: true })
const studentId = defineModel<string>('studentId', { required: true })
const studentName = defineModel<string>('studentName', { required: true })

const subjectModel = computed({
  get: () => subject.value || undefined,
  set: (v: number | undefined) => { subject.value = v ?? 0 },
})
const gradeModel = computed({
  get: () => grade.value || undefined,
  set: (v: number | undefined) => { grade.value = v ?? 0 },
})

const searchResults = ref<StudentDto[]>([])
let searchTimer: number | null = null

// el-select remote-method: same rules as before (trimmed, empty clears, 250ms debounce, pageSize 20).
function onStudentQuery(query: string) {
  if (searchTimer) clearTimeout(searchTimer)
  const name = query.trim()
  if (!name) {
    searchResults.value = []
    return
  }
  searchTimer = window.setTimeout(async () => {
    try {
      const result = await studentAdminApi.getStudents({ name, pageSize: 20 })
      searchResults.value = result.items
    } catch (error) {
      console.error('Search students failed:', error)
    }
  }, 250)
}

// Keep the current student among the options so its name stays as the select label.
const studentOptions = computed(() => {
  if (!studentId.value) return searchResults.value
  const current = { id: studentId.value, name: studentName.value } as StudentDto
  return [current, ...searchResults.value.filter((s) => s.id !== current.id)]
})

function onStudentChange(id: string | undefined) {
  const s = id ? studentOptions.value.find((o) => o.id === id) : undefined
  studentId.value = s?.id ?? ''
  studentName.value = s?.name ?? ''
  searchResults.value = []
}
</script>

<style scoped>
.edit-form-section {
  border-top: 1px solid var(--adm-border);
  padding-top: 16px;
}
.edit-form-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--adm-text-primary);
  margin-bottom: 12px;
  display: flex;
  align-items: center;
  gap: 6px;
}
.form-row-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}
.full-width { width: 100%; }
.option-name { margin-right: 8px; color: var(--adm-text-primary); }
.option-meta { color: var(--adm-text-tertiary); font-size: 12px; }
</style>
