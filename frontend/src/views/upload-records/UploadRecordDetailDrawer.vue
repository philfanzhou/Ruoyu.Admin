<template>
  <div v-if="recoverableOperations.length" class="managed-recovery">
    <span>本页面仍有未解决的指派，关闭详情不会证明远端未提交：</span>
    <el-button v-for="operation in recoverableOperations" :key="operation.payload.assignments[0].requestKey"
      size="small" @click="resumeOperation(operation)">恢复 {{ operation.record.id }}</el-button>
  </div>
  <!-- Non-modal like the old side panel: no mask, and modal-penetrable keeps the list
       clickable so another row can be opened while the drawer stays open. -->
  <el-drawer
    :model-value="record !== null || recoveryOpen"
    size="480px"
    :modal="false"
    modal-penetrable
    append-to-body
    class="upload-detail-drawer"
    @update:model-value="(v: boolean) => { if (!v) closeDrawer() }"
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
        <el-descriptions-item v-if="managed" label="来源版本">{{ current.contentRevision }}</el-descriptions-item>
      </el-descriptions>
      <el-alert v-if="!assignmentAvailable" title="指派信息不可用，请重新加载可信记录。" type="warning" :closable="false" />
      <div v-if="managed" class="source-entries">
        <div v-for="(entry, index) in current.imageEntries" :key="index">#{{ index + 1 }} · {{ entry.type }} · {{ entry.path }}</div>
      </div>

      <div class="panel-section-title">
        图片预览
        <span class="section-meta">共 {{ current.imagePaths?.length || 0 }} 张</span>
      </div>
      <UploadImageGallery
        v-model:selected-index="selectedImageIndex"
        :image-paths="current.imagePaths || []"
        :rotations="current.imageRotations || []"
        :assign-indices="assignForm.imageIndices"
        :assignable-indices="managed ? eligibleIndices : undefined"
        :selection-disabled="assignmentLocked || !assignmentAvailable"
        :mutations-disabled="assignmentLocked"
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
        :disabled="assignmentLocked || !assignmentAvailable"
        @apply-vl="applyVlGrouping"
      />
      <template v-if="managed">
        <el-button v-if="!activeOperation" :disabled="!assignmentAvailable || assigning || draftGroups.length >= 100"
          @click="addManagedGroup">添加分组</el-button>
        <div v-for="(group, index) in draftGroups" :key="index" class="managed-group">
          分组 {{ index + 1 }} · {{ getSubjectLabel(group.subject) }} · {{ gradeLabel(group.grade) }} · 图片 {{ group.imageIndices.map(i => i + 1).join(', ') }}
          <el-button v-if="!activeOperation" size="small" text @click="draftGroups.splice(index, 1)">移除分组</el-button>
        </div>
        <div v-if="activeOperation" class="managed-progress">
          <p>已固定来源版本 {{ activeOperation.payload.expectedContentRevision }}；重新发送会保留原路径、分组和请求。</p>
          <div v-for="(result, index) in activeOperation.results" :key="result.requestKey" class="managed-group-result">
            分组 {{ index + 1 }}：{{ groupLabel(result.state) }}
            <span v-if="result.statusCode">（{{ result.statusCode }}）</span>
            <div v-for="id in result.createdItemIds" :key="id">已创建：{{ id }}</div>
            <div v-if="result.errorKind">{{ result.errorKind }}</div>
          </div>
          <el-button v-if="activeOperation.running" @click="cancelManagedWait">取消等待（结果可能已提交）</el-button>
          <el-button v-else-if="!activeOperation.results.some(result => result.state === 'Unknown')"
            @click="acknowledgeOperation">保留结果，开始新分组</el-button>
        </div>
      </template>
    </template>

    <template #footer>
      <el-button @click="closeDrawer">关闭</el-button>
      <el-button type="warning" plain :icon="Warning" :disabled="resetting" @click="returnRecord">退回</el-button>
      <el-button type="success" :icon="Check" :loading="assigning" :disabled="!assignmentAvailable || assigning" @click="confirmAssign">
        {{ assigning ? '指派中...' : activeOperation ? '重新发送原请求' : '确认指派' }}
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
import { computed, ref, watch } from 'vue'
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
import { freezeOperation, hasManagedMetadata, sendFixedOperation } from './managedAssignment'
import type { AssignmentChoice, FixedOperation, GroupState } from './managedAssignment'

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
const legacyAssigning = ref(false)
const assignForm = ref({ subject: 0, grade: 0, imageIndices: [] as number[], comments: '' })
const showResetDialog = ref(false)
const resetting = ref(false)
const analyzingId = ref<string | null>(null)
const vlResult = ref<VlAnalysisResponse | null>(null)
const showVlResultDialog = ref(false)
const operations = ref<FixedOperation[]>([])
const draftGroups = ref<AssignmentChoice[]>([])
const recoveryOpen = ref(false)
let generation = 0
const waiting = new Map<string, AbortController>()
const managed = computed(() => current.value?.assignmentProtocol === 'managed-v1')
const assignmentAvailable = computed(() => current.value?.assignmentProtocol === 'legacy'
  || !!current.value && hasManagedMetadata(current.value))
const activeOperation = computed(() => operations.value.find(operation => !operation.acknowledged
  && operation.record.id === current.value?.id))
const assigning = computed(() => legacyAssigning.value || !!activeOperation.value?.running)
const assignmentLocked = computed(() => !!activeOperation.value)
const eligibleIndices = computed(() => current.value?.imageEntries?.flatMap((entry, index) => entry.type === 'mistake' ? [index] : []) ?? [])
const recoverableOperations = computed(() => operations.value.filter(operation => !operation.acknowledged
  && operation.results.some(result => result.state !== 'Completed')))

watch(
  () => props.record,
  (record) => {
    generation++
    recoveryOpen.value = false
    if (!record) {
      vlResult.value = null
      return
    }
    const existing = operations.value.find(operation => !operation.acknowledged && operation.record.id === record.id)
    const displayed = existing?.record ?? record
    current.value = displayed
    selectedImageIndex.value = 0
    vlResult.value = null
    assignForm.value = {
      subject: existing?.payload.assignments[0].subject ?? 0,
      grade: existing?.payload.assignments[0].grade ?? props.initialGrade,
      imageIndices: displayed.assignmentProtocol === 'managed-v1'
        ? displayed.imageEntries?.flatMap((entry, index) => entry.type === 'mistake' ? [index] : []) ?? []
        : displayed.imagePaths?.map((_, idx) => idx) ?? [],
      comments: existing?.payload.assignments[0].comments ?? displayed.comments ?? '',
    }
    draftGroups.value = []
  },
  { immediate: true },
)

function gradeLabel(grade: number) {
  return getGradeLabel(props.gradeOptions, grade)
}

function toggleImageForAssign(idx: number) {
  if (assignmentLocked.value || !assignmentAvailable.value || managed.value && !eligibleIndices.value.includes(idx)) return
  const i = assignForm.value.imageIndices.indexOf(idx)
  if (i >= 0) {
    assignForm.value.imageIndices.splice(i, 1)
  } else {
    assignForm.value.imageIndices.push(idx)
    assignForm.value.imageIndices.sort((a, b) => a - b)
  }
}

async function rotateImage(record: UploadRecordDto, imageIndex: number, rotation: number) {
  if (assignmentLocked.value) return
  try {
    const currentRotation = record.imageRotations?.[imageIndex] || 0
    const newRotation = (currentRotation + rotation + 360) % 360
    await studentAdminClient.rotateUploadImage(record.id, {
      studentId: record.studentId,
      imageIndex,
      rotation: newRotation,
    })
    if (record.assignmentProtocol === 'managed-v1') { closeDrawer(); emit('recordsChanged'); return }
    const rotations = [...(record.imageRotations || [])]
    rotations[imageIndex] = newRotation
    record.imageRotations = rotations
    ElMessage.success('图片旋转成功')
  } catch (error) {
    ElMessage.error(getErrorMessage(error, '旋转图片失败'))
  }
}

async function confirmDeleteImage(imageIndex: number) {
  if (assignmentLocked.value) return
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
      if (record.assignmentProtocol === 'managed-v1') { closeDrawer(); emit('recordsChanged'); return }
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
  if (assigning.value || !assignmentAvailable.value) return
  if (managed.value) { await confirmManaged(); return }
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
  legacyAssigning.value = true
  const enteredGeneration = generation
  try {
    const result = await studentAdminClient.assignUploadRecord(current.value.id, {
      studentId: current.value.studentId,
      assignments: [{
        imageIndices: [...assignForm.value.imageIndices],
        subject: assignForm.value.subject,
        grade: assignForm.value.grade,
        comments: assignForm.value.comments || undefined,
      }],
    })
    if (enteredGeneration !== generation) return
    if (result.success !== true) { ElMessage.warning('指派未全部完成，请核对已创建结果。'); return }
    ElMessage.success('指派成功')
    closeDrawer()
    emit('changed')
  } catch (error) {
    if (enteredGeneration === generation) ElMessage.error(getErrorMessage(error, '指派失败'))
  } finally {
    legacyAssigning.value = false
  }
}

function groupLabel(state: GroupState) {
  return { Completed: '已完成', Failed: '已拒绝', Unknown: '结果未知', NotAttempted: '尚未尝试' }[state]
}

function closeDrawer() { generation++; recoveryOpen.value = false; emit('close') }

function resumeOperation(operation: FixedOperation) {
  generation++; current.value = operation.record; recoveryOpen.value = true
  draftGroups.value = []; selectedImageIndex.value = 0
}

function acknowledgeOperation() {
  if (!activeOperation.value || activeOperation.value.running
    || activeOperation.value.results.some(result => result.state === 'Unknown')) return
  activeOperation.value.acknowledged = true
  draftGroups.value = []
}

function addManagedGroup() {
  if (!current.value || assignmentLocked.value) return
  const choice = { ...assignForm.value, imageIndices: [...assignForm.value.imageIndices] }
  try { freezeOperation(current.value, [choice]); draftGroups.value.push(choice) }
  catch { ElMessage.warning('请选择有效的错题图片、学科和年级。') }
}

function cancelManagedWait() {
  const key = activeOperation.value?.payload.assignments[0]?.requestKey
  if (key) waiting.get(key)?.abort()
}

async function confirmManaged() {
  if (!current.value || !hasManagedMetadata(current.value)) return
  let operation = activeOperation.value
  if (!operation) {
    const choices = draftGroups.value.length ? draftGroups.value : [{ ...assignForm.value, imageIndices: [...assignForm.value.imageIndices] }]
    try { operations.value.push(freezeOperation(current.value, choices)); operation = activeOperation.value }
    catch { ElMessage.warning('请选择有效的错题图片、学科和年级。'); return }
  }
  if (!operation || operation.running) return
  const enteredGeneration = generation
  const controller = new AbortController()
  const key = operation.payload.assignments[0]!.requestKey
  waiting.set(key, controller)
  const complete = await sendFixedOperation(operation,
    (source, payload, signal) => studentAdminClient.assignManagedUploadRecord(source, payload, signal), controller.signal)
  if (waiting.get(key) === controller) waiting.delete(key)
  if (enteredGeneration !== generation) return
  if (complete) { ElMessage.success('全部分组已完成'); closeDrawer(); emit('changed') }
  else ElMessage.warning('指派未全部完成；已保留原请求和已创建结果，请核对后显式重试。')
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
  const enteredGeneration = generation
  analyzingId.value = record.id
  try {
    const result = await studentAdminClient.analyzeUploadRecord(record.id)
    if (enteredGeneration !== generation) return
    vlResult.value = result
    if (result.success && !result.skipped && result.groups.length > 0) {
      ElMessage.success(`VL 分析完成，识别到 ${result.groups.length} 个分组`)
    }
  } catch (error) {
    if (enteredGeneration === generation) ElMessage.error(getErrorMessage(error, 'VL 分析失败', true))
  } finally {
    if (enteredGeneration === generation) analyzingId.value = null
  }
}

function applyVlGrouping() {
  if (assignmentLocked.value || !assignmentAvailable.value) return
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
.managed-recovery, .managed-progress, .managed-group, .source-entries {
  padding: 10px;
  margin: 8px 0;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-sm);
  color: var(--adm-text-secondary);
  overflow-wrap: anywhere;
  font-size: var(--adm-font-size-sm);
}
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
