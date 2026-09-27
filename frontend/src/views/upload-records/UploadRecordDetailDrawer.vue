<template>
  <!-- Non-modal like the old side panel: no mask, and modal-penetrable keeps the list
       clickable so another row can be opened while the drawer stays open. -->
  <el-drawer
    :model-value="record !== null"
    size="480px"
    :modal="false"
    modal-penetrable
    append-to-body
    class="upload-detail-drawer"
    @update:model-value="(v: boolean) => { if (!v) emit('close') }"
  >
    <template #header>
      <div class="drawer-title">
        上传记录详情
        <span v-if="current" class="rec-id">{{ current.id }}</span>
      </div>
      <div v-if="current" class="drawer-header-actions">
        <el-button size="small" :loading="analyzingId === current.id" :icon="MagicStick" @click="handleVlAnalyze(current)">
          {{ analyzingId === current.id ? '分析中' : 'VL 分析' }}
        </el-button>
        <el-button size="small" :icon="RefreshLeft" @click="showResetDialog = true">重置</el-button>
      </div>
    </template>

    <template v-if="current">
      <div class="panel-section-title">基本信息</div>
      <el-descriptions :column="1" border size="small" class="basic-info">
        <el-descriptions-item label="学生">
          <span class="student-line">
            <span class="stu-avatar-sm" :style="{ background: getAvatarGradient(current.studentName || current.studentId) }">{{ getAvatarChar(current.studentName || '?') }}</span>
            {{ current.studentName }}
            <span class="desc-meta">({{ gradeLabel(studentGrade) }})</span>
          </span>
        </el-descriptions-item>
        <el-descriptions-item label="状态">
          <StatusTag :label="getUploadStatusLabel(current.status)" :type="getUploadStatusTagType(current.status)" />
        </el-descriptions-item>
        <el-descriptions-item label="图片数量"><strong>{{ current.imagePaths?.length || 0 }}</strong> 张</el-descriptions-item>
        <el-descriptions-item label="上传时间">{{ formatDateTime(current.createdAt) }}</el-descriptions-item>
        <el-descriptions-item label="更新时间">{{ formatDateTime(current.updatedAt) }}</el-descriptions-item>
        <el-descriptions-item label="备注">{{ current.comments || '无' }}</el-descriptions-item>
      </el-descriptions>

      <div class="panel-section-title">
        图片预览
        <span class="section-meta">共 {{ current.imagePaths?.length || 0 }} 张</span>
      </div>
      <UploadImageGallery
        v-model:selected-index="selectedImageIndex"
        :image-paths="current.imagePaths || []"
        :rotations="current.imageRotations || []"
        :assign-indices="assignForm.imageIndices"
        @toggle-assign="toggleImageForAssign"
        @rotate="(idx, delta) => current && rotateImage(current, idx, delta)"
        @delete="confirmDeleteImage"
      />

      <div class="panel-section-title">
        <el-icon class="vl-icon"><MagicStick /></el-icon>
        AI 智能分组
        <span class="vl-badge">VL</span>
      </div>
      <VlAnalysisCard
        :result="vlResult"
        :analyzing="analyzingId === current.id"
        :grade-label="gradeLabel"
        @analyze="current && handleVlAnalyze(current)"
        @show-detail="showVlResultDialog = true"
      />

      <AssignForm
        v-model:subject="assignForm.subject"
        v-model:grade="assignForm.grade"
        v-model:comments="assignForm.comments"
        :subject-options="subjectOptions"
        :grade-options="gradeOptions"
        :can-apply-vl="!!vlResult?.groups?.length"
        @apply-vl="applyVlGrouping"
      />
    </template>

    <template #footer>
      <el-button @click="emit('close')">取消</el-button>
      <el-button type="warning" plain :icon="Warning" :disabled="resetting" @click="returnRecord">退回</el-button>
      <el-button type="success" :icon="Check" :loading="assigning" @click="confirmAssign">
        {{ assigning ? '指派中...' : '确认指派' }}
      </el-button>
    </template>
  </el-drawer>

  <ResetStatusDialog
    v-model="showResetDialog"
    :initial-status="current?.status || 1"
    :loading="resetting"
    @confirm="confirmReset"
  />
  <VlResultDialog v-model="showVlResultDialog" :result="vlResult" :grade-label="gradeLabel" />
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { Check, MagicStick, RefreshLeft, Warning } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { confirmDanger } from '../../utils/confirm'
import { studentAdminClient } from '../../services/studentAdminApi'
import type { UploadRecordDto, VlAnalysisResponse, GradeOption, SubjectOption } from '../../services/studentAdminApi'
import { getSubjectLabel, getAvatarGradient, getAvatarChar, formatDateTime } from '../../utils/subject'
import StatusTag from '../../components/list/StatusTag.vue'
import UploadImageGallery from './UploadImageGallery.vue'
import VlAnalysisCard from './VlAnalysisCard.vue'
import VlResultDialog from './VlResultDialog.vue'
import AssignForm from './AssignForm.vue'
import ResetStatusDialog from './ResetStatusDialog.vue'
import { getErrorMessage, getGradeLabel, getUploadStatusLabel, getUploadStatusTagType } from './uploadRecord'

const props = defineProps<{
  /** A fresh copy per open (also when reopening the same row); null closes the drawer. */
  record: UploadRecordDto | null
  /** Pre-filled assign grade ("指派" opens with the student's grade when already known). */
  initialGrade: number
  studentGrade: number
  gradeOptions: GradeOption[]
  subjectOptions: SubjectOption[]
}>()

const emit = defineEmits<{
  close: []
  /** The list changed (image removed); stats are unaffected. */
  recordsChanged: []
  /** Status changed (assign / reset / return): reload list and stats. */
  changed: []
}>()

// Keeps the last record while the drawer animates closed.
const current = ref<UploadRecordDto | null>(null)
const selectedImageIndex = ref(0)
const assigning = ref(false)
const assignForm = ref({ subject: 0, grade: 0, imageIndices: [] as number[], comments: '' })
const showResetDialog = ref(false)
const resetting = ref(false)
const analyzingId = ref<string | null>(null)
const vlResult = ref<VlAnalysisResponse | null>(null)
const showVlResultDialog = ref(false)

watch(
  () => props.record,
  (record) => {
    if (!record) {
      vlResult.value = null
      return
    }
    current.value = record
    selectedImageIndex.value = 0
    vlResult.value = null
    assignForm.value = {
      subject: 0,
      grade: props.initialGrade,
      imageIndices: record.imagePaths?.map((_, idx) => idx) ?? [],
      comments: record.comments || '',
    }
  },
  { immediate: true },
)

function gradeLabel(grade: number) {
  return getGradeLabel(props.gradeOptions, grade)
}

function toggleImageForAssign(idx: number) {
  const i = assignForm.value.imageIndices.indexOf(idx)
  if (i >= 0) {
    assignForm.value.imageIndices.splice(i, 1)
  } else {
    assignForm.value.imageIndices.push(idx)
    assignForm.value.imageIndices.sort((a, b) => a - b)
  }
}

async function rotateImage(record: UploadRecordDto, imageIndex: number, rotation: number) {
  try {
    const currentRotation = record.imageRotations?.[imageIndex] || 0
    const newRotation = (currentRotation + rotation + 360) % 360
    await studentAdminClient.rotateUploadImage(record.id, {
      studentId: record.studentId,
      imageIndex,
      rotation: newRotation,
    })
    const rotations = [...(record.imageRotations || [])]
    rotations[imageIndex] = newRotation
    record.imageRotations = rotations
    ElMessage.success('图片旋转成功')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '旋转图片失败'))
  }
}

async function confirmDeleteImage(imageIndex: number) {
  if (!current.value) return
  const confirmed = await confirmDanger({
    title: '删除图片',
    message: `从该上传记录中删除第 ${imageIndex + 1} 张图片；若这是最后一张，整条上传记录将一并删除。`,
    confirmText: '删除',
  })
  if (!confirmed || !props.record || !current.value) return
  const record = current.value
  try {
    const result = await studentAdminClient.removeImageFromRecord(record.id, record.studentId, imageIndex)
    if (result.success) {
      ElMessage.success(result.recordDeleted ? '已删除最后一张图片，记录已自动删除' : '图片已删除')
      if (result.recordDeleted) {
        emit('close')
      } else if (props.record) {
        record.imagePaths.splice(imageIndex, 1)
        record.imageRotations?.splice(imageIndex, 1)
        if (selectedImageIndex.value >= record.imagePaths.length) {
          selectedImageIndex.value = Math.max(0, record.imagePaths.length - 1)
        }
        assignForm.value.imageIndices = assignForm.value.imageIndices
          .filter(i => i !== imageIndex)
          .map(i => (i > imageIndex ? i - 1 : i))
      }
      emit('recordsChanged')
    } else {
      ElMessage.error(result.message || '删除失败')
    }
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '删除图片失败'))
  }
}

async function confirmAssign() {
  if (!props.record || !current.value) {
    ElMessage.error('记录信息缺失')
    return
  }
  if (assignForm.value.subject <= 0) {
    ElMessage.warning('请选择学科')
    return
  }
  if (assignForm.value.grade <= 0) {
    ElMessage.warning('请选择年级')
    return
  }
  if (!assignForm.value.imageIndices.length) {
    ElMessage.warning('请至少选择一张图片')
    return
  }
  assigning.value = true
  try {
    await studentAdminClient.assignUploadRecord(current.value.id, {
      studentId: current.value.studentId,
      assignments: [{
        imageIndices: [...assignForm.value.imageIndices],
        subject: assignForm.value.subject,
        grade: assignForm.value.grade,
        comments: assignForm.value.comments || undefined,
      }],
    })
    ElMessage.success('指派成功')
    emit('close')
    emit('changed')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '指派失败'))
  } finally {
    assigning.value = false
  }
}

async function confirmReset(targetStatus: number) {
  if (!props.record || !current.value) return
  resetting.value = true
  try {
    await studentAdminClient.resetUploadRecordStatus(current.value.id, current.value.studentId, targetStatus)
    ElMessage.success('重置成功')
    showResetDialog.value = false
    emit('close')
    emit('changed')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '重置失败'))
  } finally {
    resetting.value = false
  }
}

// Returning is not destructive (the student can upload again), so it keeps a plain confirmation.
async function returnRecord() {
  if (!props.record || !current.value) return
  try {
    await ElMessageBox.confirm('确定要退回此记录吗？退回后学生可重新上传。', '退回确认', {
      confirmButtonText: '确认退回',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }
  if (!current.value) return
  resetting.value = true
  try {
    await studentAdminClient.resetUploadRecordStatus(current.value.id, current.value.studentId, 5)
    ElMessage.success('已退回')
    emit('close')
    emit('changed')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '退回失败'))
  } finally {
    resetting.value = false
  }
}

async function handleVlAnalyze(record: UploadRecordDto) {
  analyzingId.value = record.id
  try {
    const result = await studentAdminClient.analyzeUploadRecord(record.id)
    vlResult.value = result
    if (result.success && !result.skipped && result.groups.length > 0) {
      ElMessage.success(`VL 分析完成，识别到 ${result.groups.length} 个分组`)
    }
  } catch (error) {
    ElMessage.error(getErrorMessage(error, 'VL 分析失败', true))
  } finally {
    analyzingId.value = null
  }
}

function applyVlGrouping() {
  if (!vlResult.value?.groups?.length) return
  const firstGroup = vlResult.value.groups[0]
  assignForm.value.subject = firstGroup.subject
  assignForm.value.grade = firstGroup.grade
  assignForm.value.imageIndices = [...firstGroup.imageIndices].sort((a, b) => a - b)
  ElMessage.success(`已应用分组 1：${getSubjectLabel(firstGroup.subject)} · ${gradeLabel(firstGroup.grade)}`)
}
</script>

<style scoped>
:global(.upload-detail-drawer .el-drawer__header) {
  margin-bottom: 0;
  padding: 14px 20px;
  border-bottom: 1px solid var(--adm-border);
}
:global(.upload-detail-drawer .el-drawer__footer) {
  border-top: 1px solid var(--adm-border);
  padding: 14px 20px;
}
.drawer-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--adm-text-primary);
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
  min-width: 0;
}
.rec-id {
  font-size: 11px;
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  color: var(--adm-text-muted);
  font-weight: 400;
  background: var(--adm-surface-subtle);
  padding: 2px 8px;
  border-radius: 4px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.drawer-header-actions {
  display: flex;
  gap: 4px;
  flex-shrink: 0;
}
.drawer-header-actions .el-button + .el-button { margin-left: 0; }
.panel-section-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--adm-text-primary);
  margin: 20px 0 10px;
  display: flex;
  align-items: center;
  gap: 6px;
}
.panel-section-title:first-child { margin-top: 0; }
.section-meta {
  font-size: 11px;
  color: var(--adm-text-muted);
  font-weight: 400;
  margin-left: 4px;
}
.basic-info :deep(.el-descriptions__label) { width: 90px; }
.student-line {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.stu-avatar-sm {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  font-weight: 600;
  color: #fff;
}
.desc-meta { color: var(--adm-text-muted); font-size: 12px; }
.vl-icon { color: #7c3aed; }
.vl-badge {
  padding: 1px 6px;
  border-radius: 4px;
  background: #7c3aed;
  color: #fff;
  font-size: 10px;
  font-weight: 600;
}
</style>
