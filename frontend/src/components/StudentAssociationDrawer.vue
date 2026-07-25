<template>
  <div v-if="visible" class="drawer-overlay" @click.self="close"></div>
  <div v-if="visible" class="drawer">
    <div class="drawer-header">
      <div>
        <div class="drawer-title">管理关联学生{{ displayName ? ' - ' + displayName : '' }}</div>
        <div class="drawer-subtitle">当前关联 {{ currentStudentIds.length }} 名学生</div>
      </div>
      <button class="drawer-close" @click="close" title="关闭">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
      </button>
    </div>

    <div class="drawer-search">
      <div class="drawer-search-wrap">
        <svg class="drawer-search-icon" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
        <input
          v-model="searchKeyword"
          class="drawer-search-input"
          placeholder="搜索学生姓名..."
          @input="searchStudents"
        />
      </div>
    </div>

    <div class="drawer-body">
      <!-- Already associated -->
      <div class="drawer-section">
        <div class="drawer-section-title">
          已关联学生
          <span class="count">{{ currentStudentIds.length }}</span>
        </div>
        <div v-if="loading" class="drawer-loading">
          <span class="spinner-sm"></span>
          <span>加载中...</span>
        </div>
        <div v-else-if="currentStudentDetails.length === 0" class="drawer-empty">暂无关联学生</div>
        <template v-else>
          <div v-for="s in currentStudentDetails" :key="s.id" class="student-chip linked">
            <div class="mini-avatar" :style="{ background: getAvatarGradient(s.name || s.id) }">{{ getAvatarChar(s.name || '?') }}</div>
            <div class="stu-info">
              <div class="stu-name">{{ s.name }}</div>
              <div class="stu-grade">{{ getGradeLabel(s.grade) }} · {{ s.id }}</div>
            </div>
            <button class="chip-remove" title="移除关联" :disabled="removingStudentId === s.id" @click="handleRemove(s.id)">
              <svg v-if="removingStudentId !== s.id" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
              <span v-else class="spinner-sm"></span>
            </button>
          </div>
        </template>
      </div>

      <!-- Add students -->
      <div class="drawer-section">
        <div class="drawer-section-title">
          添加学生
          <span v-if="searchResults.length > 0" class="count">{{ availableToAdd.length }}</span>
        </div>
        <div v-if="searching" class="drawer-loading">
          <span class="spinner-sm"></span>
          <span>搜索中...</span>
        </div>
        <div v-else-if="searchResults.length === 0" class="drawer-empty-hint">
          输入学生姓名搜索可添加的学生
        </div>
        <template v-else>
          <div v-for="s in availableToAdd" :key="s.id" class="student-chip addable">
            <div class="mini-avatar" :style="{ background: getAvatarGradient(s.name || s.id) }">{{ getAvatarChar(s.name || '?') }}</div>
            <div class="stu-info">
              <div class="stu-name">{{ s.name }}</div>
              <div class="stu-grade">{{ getGradeLabel(s.grade) }} · {{ s.id }}</div>
            </div>
            <button class="chip-add" title="添加关联" :disabled="addingStudentId === s.id" @click="handleAdd(s.id)">
              <svg v-if="addingStudentId !== s.id" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
              <span v-else class="spinner-sm"></span>
            </button>
          </div>
          <div v-if="availableToAdd.length === 0" class="drawer-empty-hint">搜索结果均已是关联学生</div>
        </template>
      </div>
    </div>

    <div class="drawer-footer">
      <button class="adm-btn adm-btn-primary" @click="close">完成</button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import { studentAssociationClient } from '../services/studentAssociationApi'
import { getAssistantPortalErrorMessage } from '../services/assistantPortalApi'
import { studentAdminClient, type StudentDto, type GradeOption } from '../services/studentAdminApi'
import { getAvatarGradient, getAvatarChar } from '../utils/subject'

const props = defineProps<{
  userId: string
  role: 'teacher' | 'assistant'
  visible: boolean
  displayName?: string
}>()

const emit = defineEmits<{
  (e: 'update:visible', value: boolean)
}>()

const currentStudentIds = ref<string[]>([])
const currentStudentDetails = ref<StudentDto[]>([])
const loading = ref(false)
const removingStudentId = ref<string | null>(null)
const addingStudentId = ref<string | null>(null)

const searchKeyword = ref('')
const searchResults = ref<StudentDto[]>([])
const searching = ref(false)

const gradeOptions = ref<GradeOption[]>([])

const availableToAdd = computed(() =>
  searchResults.value.filter(s => !currentStudentIds.value.includes(s.id))
)

function getGradeLabel(grade: number): string {
  return gradeOptions.value.find(g => g.value === grade)?.label || `年级 ${grade}`
}

async function loadGradeOptions() {
  try {
    gradeOptions.value = await studentAdminClient.getGrades()
  } catch (error) {
    console.error('Failed to load grade options:', error)
  }
}

async function loadStudents() {
  if (!props.userId) return
  loading.value = true
  currentStudentDetails.value = []
  try {
    currentStudentIds.value = await studentAssociationClient.getStudents(props.userId, props.role)
    if (currentStudentIds.value.length > 0) {
      const details: StudentDto[] = []
      for (const sid of currentStudentIds.value) {
        try {
          const student = await studentAdminClient.getStudent(sid)
          details.push(student)
        } catch {
          details.push({ id: sid, name: '未知', grade: 0, identityAccountIds: [], createdAt: 0, updatedAt: 0 })
        }
      }
      currentStudentDetails.value = details
    }
  } catch (error) {
    console.error('Failed to load students:', error)
    ElMessage.error('加载学生列表失败')
  } finally {
    loading.value = false
  }
}

async function searchStudents() {
  if (!searchKeyword.value || searchKeyword.value.length < 1) {
    searchResults.value = []
    return
  }
  searching.value = true
  try {
    const result = await studentAdminClient.getStudents({
      name: searchKeyword.value || undefined,
      page: 1,
      pageSize: 20,
    })
    searchResults.value = result.items
  } catch (error) {
    console.error('Search students failed:', error)
    ElMessage.error('搜索学生失败')
  } finally {
    searching.value = false
  }
}

async function handleAdd(studentId: string) {
  addingStudentId.value = studentId
  try {
    await studentAssociationClient.addStudent(props.userId, studentId, props.role)
    ElMessage.success('添加成功')
    await loadStudents()
  } catch (error: unknown) {
    ElMessage.error(getAssistantPortalErrorMessage(error))
  } finally {
    addingStudentId.value = null
  }
}

async function handleRemove(studentId: string) {
  removingStudentId.value = studentId
  try {
    await studentAssociationClient.removeStudent(props.userId, studentId, props.role)
    ElMessage.success('移除成功')
    await loadStudents()
  } catch (error: unknown) {
    ElMessage.error(getAssistantPortalErrorMessage(error))
  } finally {
    removingStudentId.value = null
  }
}

function close() {
  emit('update:visible', false)
  searchKeyword.value = ''
  searchResults.value = []
}

watch(() => props.visible, (val) => {
  if (val) {
    searchKeyword.value = ''
    searchResults.value = []
    loadStudents()
  }
})

onMounted(() => {
  loadGradeOptions()
})
</script>

<style scoped>
/* Drawer */
.drawer-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.4);
  z-index: 150;
  animation: maskIn 0.2s;
}
.drawer {
  position: fixed;
  top: 0;
  right: 0;
  width: 400px;
  height: 100vh;
  background: var(--adm-surface-elevated);
  z-index: 200;
  display: flex;
  flex-direction: column;
  box-shadow: -8px 0 32px rgba(15, 23, 42, 0.15);
  animation: drawerIn 0.25s ease;
  max-width: calc(100vw - 32px);
}
@keyframes drawerIn {
  from { transform: translateX(100%); }
  to { transform: translateX(0); }
}
.drawer-header {
  padding: 18px 20px;
  border-bottom: 1px solid var(--adm-border);
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
}
.drawer-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--adm-text-primary);
}
.drawer-subtitle {
  font-size: 12px;
  color: var(--adm-text-tertiary);
  margin-top: 4px;
}
.drawer-close {
  width: 32px;
  height: 32px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-tertiary);
  cursor: pointer;
  transition: all 0.15s;
  flex-shrink: 0;
  background: transparent;
  border: none;
}
.drawer-close:hover {
  background: var(--adm-surface-subtle);
  color: var(--adm-text-primary);
}

.drawer-search {
  padding: 14px 20px;
  border-bottom: 1px solid var(--adm-border);
}
.drawer-search-wrap {
  position: relative;
}
.drawer-search-input {
  width: 100%;
  padding: 8px 12px 8px 34px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  font-size: 13px;
  outline: none;
  transition: all 0.15s;
  background: var(--adm-surface-subtle);
  box-sizing: border-box;
  height: 36px;
}
.drawer-search-input:focus {
  border-color: var(--adm-primary);
  background: #fff;
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}
.drawer-search-icon {
  position: absolute;
  left: 10px;
  top: 50%;
  transform: translateY(-50%);
  color: var(--adm-text-muted);
  pointer-events: none;
}

.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 16px 20px;
}
.drawer-section { margin-bottom: 20px; }
.drawer-section:last-child { margin-bottom: 0; }
.drawer-section-title {
  font-size: 12px;
  font-weight: 600;
  color: var(--adm-text-tertiary);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  margin-bottom: 10px;
  display: flex;
  align-items: center;
  gap: 6px;
}
.drawer-section-title .count {
  background: var(--adm-primary-bg);
  color: var(--adm-primary);
  padding: 1px 7px;
  border-radius: 10px;
  font-size: 11px;
  font-weight: 600;
}

.spinner-sm {
  width: 14px; height: 14px;
  border: 2px solid var(--adm-border);
  border-top-color: var(--adm-primary);
  border-radius: 50%;
  animation: adm-spin 0.7s linear infinite;
  display: inline-block;
}
@keyframes adm-spin { to { transform: rotate(360deg); } }

.mini-avatar {
  width: 30px; height: 30px;
  border-radius: 50%;
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  font-weight: 600;
  flex-shrink: 0;
}

.student-chip {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  margin-bottom: 8px;
  transition: all 0.15s;
  background: #fff;
}
.student-chip:hover {
  border-color: var(--adm-primary-light, #93c5fd);
  background: var(--adm-primary-bg);
}
.student-chip.addable {
  border-style: dashed;
  border-color: var(--adm-primary-light, #93c5fd);
  background: #fafcff;
}
.student-chip .stu-info { flex: 1; min-width: 0; }
.student-chip .stu-name {
  font-size: 13px;
  font-weight: 500;
  color: var(--adm-text-primary);
}
.student-chip .stu-grade {
  font-size: 11px;
  color: var(--adm-text-tertiary);
  margin-top: 1px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.chip-remove {
  width: 26px;
  height: 26px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-muted);
  cursor: pointer;
  transition: all 0.15s;
  background: transparent;
  border: none;
  padding: 0;
}
.chip-remove:hover:not(:disabled) {
  background: var(--adm-error-bg, #fef2f2);
  color: var(--adm-error);
}
.chip-remove:disabled { cursor: wait; opacity: 0.6; }
.chip-add {
  width: 28px;
  height: 28px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: var(--adm-primary-bg);
  color: var(--adm-primary);
  cursor: pointer;
  transition: all 0.15s;
  font-size: 13px;
  font-weight: 600;
  border: none;
  padding: 0;
  flex-shrink: 0;
}
.chip-add:hover:not(:disabled) {
  background: var(--adm-primary);
  color: #fff;
}
.chip-add:disabled { cursor: wait; opacity: 0.6; }

.drawer-loading {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 16px;
  color: var(--adm-text-muted);
  font-size: 12px;
}
.drawer-empty {
  text-align: center;
  padding: 12px;
  color: var(--adm-text-muted);
  font-size: 12px;
}
.drawer-empty-hint {
  text-align: center;
  padding: 12px;
  color: var(--adm-text-muted);
  font-size: 12px;
  background: var(--adm-surface-subtle);
  border-radius: var(--adm-radius-md);
}

.drawer-footer {
  padding: 14px 20px;
  border-top: 1px solid var(--adm-border);
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  background: var(--adm-surface-subtle, #fafbfc);
}
</style>
