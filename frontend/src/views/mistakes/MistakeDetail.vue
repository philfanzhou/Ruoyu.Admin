<template>
  <el-descriptions :column="2" border size="small" class="info-grid">
    <el-descriptions-item label="学生">
      <span class="student-line">
        <span class="stu-avatar-sm" :style="{ background: getAvatarGradient(mistake.studentName || mistake.studentId) }">{{ getAvatarChar(mistake.studentName || '?') }}</span>
        {{ mistake.studentName || '未知' }}
      </span>
    </el-descriptions-item>
    <el-descriptions-item label="学科"><SubjectTag :subject="mistake.subject" /></el-descriptions-item>
    <el-descriptions-item label="年级">{{ gradeLabel(mistake.grade) }}</el-descriptions-item>
    <el-descriptions-item label="状态">
      <StatusTag :label="reviewStatusLabel(mistake.reviewStatus)" :type="getReviewStatusTagType(mistake.reviewStatus)" />
    </el-descriptions-item>
    <el-descriptions-item label="创建时间"><span class="muted">{{ formatDateTime(mistake.createdAt) }}</span></el-descriptions-item>
    <el-descriptions-item label="更新时间"><span class="muted">{{ formatDateTime(mistake.updatedAt) }}</span></el-descriptions-item>
    <el-descriptions-item label="来源上传">
      <span class="mono-id" :title="mistake.sourceUploadId || ''">{{ mistake.sourceUploadId ? mistake.sourceUploadId.slice(0, 12) + '...' : '-' }}</span>
    </el-descriptions-item>
    <el-descriptions-item label="问题ID">
      <span class="mono-id" :title="mistake.questionId || ''">{{ mistake.questionId ? mistake.questionId.slice(0, 12) + '...' : '-' }}</span>
    </el-descriptions-item>
  </el-descriptions>

  <!-- Review information (read-only: this page does not review mistakes). -->
  <div v-if="mistake.reviewComment || mistake.type || mistake.reviewerId" class="review-card">
    <div class="review-card-header">
      <span class="review-icon"><el-icon :size="16"><Stamp /></el-icon></span>
      <span class="review-title">审核信息</span>
    </div>
    <div v-if="mistake.type" class="review-field">
      <div class="review-field-label">错因类型</div>
      <div class="review-field-value"><el-tag type="danger" size="small" disable-transitions>{{ typeLabel(mistake.type) }}</el-tag></div>
    </div>
    <div v-if="mistake.reviewComment" class="review-field">
      <div class="review-field-label">审核评论</div>
      <div class="review-field-value">{{ mistake.reviewComment }}</div>
    </div>
    <div v-if="mistake.reviewerId" class="review-field">
      <div class="review-field-label">审核人</div>
      <div class="review-field-value mono-id">{{ mistake.reviewerId }}</div>
    </div>
    <div v-if="mistake.reviewedAt" class="review-field">
      <div class="review-field-label">审核时间</div>
      <div class="review-field-value muted">{{ formatDateTime(mistake.reviewedAt) }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { Stamp } from '@element-plus/icons-vue'
import type { MistakeItemDto } from '../../services/studentAdminApi'
import { getAvatarGradient, getAvatarChar, formatDateTime } from '../../utils/subject'
import StatusTag from '../../components/list/StatusTag.vue'
import SubjectTag from '../../components/list/SubjectTag.vue'
import { getReviewStatusTagType } from './mistake'

defineProps<{
  mistake: MistakeItemDto
  gradeLabel: (grade: number) => string
  reviewStatusLabel: (status: number) => string
  typeLabel: (type: number) => string
}>()
</script>

<style scoped>
.info-grid { margin-bottom: 18px; }
.info-grid :deep(.el-descriptions__label) { width: 84px; }
.student-line {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-weight: 500;
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
.muted { color: var(--adm-text-secondary); }
.review-card {
  background: var(--adm-surface-subtle);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  padding: 16px;
  margin-bottom: 18px;
}
.review-card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}
.review-icon {
  width: 28px;
  height: 28px;
  border-radius: 8px;
  background: var(--adm-warning-bg);
  color: var(--adm-warning);
  display: flex;
  align-items: center;
  justify-content: center;
}
.review-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--adm-text-primary);
}
.review-field { margin-bottom: 10px; }
.review-field:last-child { margin-bottom: 0; }
.review-field-label {
  font-size: 11px;
  color: var(--adm-text-tertiary);
  margin-bottom: 4px;
  font-weight: 500;
}
.review-field-value {
  font-size: 13px;
  color: var(--adm-text-primary);
  line-height: 1.7;
}
</style>
