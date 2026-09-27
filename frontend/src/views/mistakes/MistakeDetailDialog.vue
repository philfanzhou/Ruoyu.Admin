<template>
  <el-dialog
    :model-value="mistakeId !== null"
    width="700px"
    append-to-body
    :close-on-click-modal="false"
    @update:model-value="(v: boolean) => { if (!v) emit('close') }"
  >
    <template #header>
      <div class="dialog-title">
        错题详情
        <span class="rec-id">{{ current?.id?.slice(0, 12) || '-' }}</span>
      </div>
    </template>
    <div v-if="detailLoading" v-loading="true" class="detail-loading" element-loading-text="加载详情..." />
    <template v-else-if="current">
      <MistakeDetail
        :mistake="current"
        :grade-label="gradeLabel"
        :review-status-label="reviewStatusLabel"
        :type-label="typeLabel"
      />
      <MistakeImageGallery
        v-model:selected-index="selectedImageIndex"
        :regions="current.sourceRegions || []"
        :image-count="current.imageCount"
        :migrating="migrating"
        @migrate="migrateImages"
      />
      <MistakeEditForm
        v-model:subject="editForm.subject"
        v-model:grade="editForm.grade"
        v-model:student-id="editForm.studentId"
        v-model:student-name="editForm.studentName"
        :subject-options="subjectOptions"
        :grade-options="gradeOptions"
        :grade-label="gradeLabel"
      />
    </template>
    <template #footer>
      <el-button @click="emit('close')">关闭</el-button>
      <el-button type="primary" :icon="Check" :loading="saving" :disabled="detailLoading" @click="saveEdit">
        {{ saving ? '保存中...' : '保存修改' }}
      </el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { Check } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import studentAdminApi from '../../services/studentAdminApi'
import type { MistakeItemDto } from '../../services/studentAdminApi'
import MistakeDetail from './MistakeDetail.vue'
import MistakeImageGallery from './MistakeImageGallery.vue'
import MistakeEditForm from './MistakeEditForm.vue'
import { getErrorMessage, type Option } from './mistake'

const props = defineProps<{
  /** Opens the dialog and loads this mistake; null closes it. */
  mistakeId: string | null
  subjectOptions: Option[]
  gradeOptions: Option[]
  gradeLabel: (grade: number) => string
  reviewStatusLabel: (status: number) => string
  typeLabel: (type: number) => string
  /** Refresh list + stats after a successful save (awaited before the detail is re-fetched). */
  afterSave: () => Promise<void>
  /** Refresh the list after a successful image migration. */
  afterMigrate: () => Promise<void>
}>()

const emit = defineEmits<{
  close: []
}>()

const current = ref<MistakeItemDto | null>(null)
const detailLoading = ref(false)
const selectedImageIndex = ref(0)
const migrating = ref(false)
const saving = ref(false)
const editForm = ref({ id: '', studentId: '', studentName: '', subject: 0, grade: 0 })

watch(
  () => props.mistakeId,
  async (id) => {
    current.value = null
    if (!id) return
    detailLoading.value = true
    selectedImageIndex.value = 0
    try {
      const detail = await studentAdminApi.getMistakeItem(id)
      current.value = detail
      editForm.value = {
        id: detail.id,
        studentId: detail.studentId,
        studentName: detail.studentName || '',
        subject: detail.subject,
        grade: detail.grade,
      }
    } catch (error) {
      ElMessage.error(getErrorMessage(error, '加载详情失败'))
      emit('close')
    } finally {
      detailLoading.value = false
    }
  },
)

async function saveEdit() {
  if (!current.value) return
  if (!editForm.value.subject || editForm.value.subject <= 0) {
    ElMessage.warning('请选择学科')
    return
  }
  if (!editForm.value.grade || editForm.value.grade <= 0) {
    ElMessage.warning('请选择年级')
    return
  }
  if (!editForm.value.studentId) {
    ElMessage.warning('请选择学生')
    return
  }
  saving.value = true
  try {
    await studentAdminApi.updateMistakeItem(editForm.value.id, {
      studentId: editForm.value.studentId,
      subject: editForm.value.subject,
      grade: editForm.value.grade,
    })
    ElMessage.success('更新成功')
    await props.afterSave()
    // Refresh the detail view
    try {
      current.value = await studentAdminApi.getMistakeItem(editForm.value.id)
    } catch {
      // ignore refresh errors
    }
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '更新失败'))
  } finally {
    saving.value = false
  }
}

async function migrateImages() {
  if (!current.value) return
  migrating.value = true
  try {
    const result = await studentAdminApi.migrateMistakeImages(current.value.id)
    if (result.success) {
      ElMessage.success(result.message || `迁移成功，${result.migratedCount} 张图片`)
      // Reload detail to reflect new paths
      current.value = await studentAdminApi.getMistakeItem(current.value.id)
      await props.afterMigrate()
    } else {
      ElMessage.error(result.message || '迁移失败')
    }
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '迁移图片失败'))
  } finally {
    migrating.value = false
  }
}
</script>

<style scoped>
.dialog-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--adm-text-primary);
  display: flex;
  align-items: center;
  gap: 8px;
}
.rec-id {
  font-size: 11px;
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  color: var(--adm-text-muted);
  font-weight: 400;
  background: var(--adm-surface-subtle);
  padding: 2px 8px;
  border-radius: 4px;
}
.detail-loading { height: 160px; }
</style>
