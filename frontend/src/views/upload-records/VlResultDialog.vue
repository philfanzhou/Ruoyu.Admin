<template>
  <el-dialog
    :model-value="modelValue"
    title="VL 图片分析完整结果"
    width="750px"
    append-to-body
    :close-on-click-modal="false"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div v-if="result">
      <div v-if="result.prompt" class="vl-section">
        <div class="vl-section-title">分析提示词</div>
        <pre class="vl-pre">{{ result.prompt }}</pre>
      </div>
      <div v-if="result.compressedImages?.length" class="vl-section">
        <div class="vl-section-title">发送给 AI 的压缩图片</div>
        <div class="vl-compressed-grid">
          <div v-for="(imgData, idx) in result.compressedImages" :key="idx" class="vl-compressed-item">
            <img :src="imgData" class="vl-compressed-img" alt="" />
            <span class="vl-compressed-label">图片 {{ idx + 1 }}</span>
          </div>
        </div>
      </div>
      <div v-if="result.groups.length" class="vl-section">
        <div class="vl-section-title">分组详情（{{ result.groups.length }} 个）</div>
        <div v-for="(g, idx) in result.groups" :key="idx" class="vl-group-detail">
          <div class="vl-group-detail-header">
            <el-tag effect="dark" size="small" disable-transitions>分组 {{ idx + 1 }}</el-tag>
            <span class="vl-group-meta">{{ getSubjectLabel(g.subject) }} · {{ gradeLabel(g.grade) }}</span>
          </div>
          <div v-if="g.description" class="vl-group-desc">{{ g.description }}</div>
          <div class="vl-group-images">
            包含图片：
            <el-tag v-for="imgIdx in g.imageIndices" :key="imgIdx" size="small" disable-transitions>图片 {{ imgIdx + 1 }}</el-tag>
          </div>
        </div>
      </div>
      <div v-if="result.rawResponse" class="vl-section">
        <div class="vl-section-title">原始响应</div>
        <pre class="vl-pre">{{ result.rawResponse }}</pre>
      </div>
    </div>
    <template #footer>
      <el-button @click="emit('update:modelValue', false)">关闭</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import type { VlAnalysisResponse } from '../../services/studentAdminApi'
import { getSubjectLabel } from '../../utils/subject'

defineProps<{
  modelValue: boolean
  result: VlAnalysisResponse | null
  gradeLabel: (grade: number) => string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
}>()
</script>

<style scoped>
.vl-section { margin-bottom: 16px; }
.vl-section-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--adm-text-primary);
  margin-bottom: 8px;
}
.vl-pre {
  background: var(--adm-surface-subtle);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-sm);
  padding: 10px;
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 11px;
  color: var(--adm-text-secondary);
  white-space: pre-wrap;
  word-break: break-all;
  max-height: 240px;
  overflow-y: auto;
  margin: 0;
}
.vl-compressed-grid {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}
.vl-compressed-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
}
.vl-compressed-img {
  width: 150px;
  height: 150px;
  object-fit: contain;
  border-radius: var(--adm-radius-sm);
  border: 1px solid var(--adm-border);
  background: var(--adm-surface-subtle);
}
.vl-compressed-label {
  font-size: 11px;
  color: var(--adm-text-muted);
}
.vl-group-detail {
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-sm);
  padding: 12px;
  margin-bottom: 10px;
  background: var(--adm-surface-subtle);
}
.vl-group-detail-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}
.vl-group-meta { font-size: 13px; color: var(--adm-text-secondary); }
.vl-group-desc {
  font-size: 13px;
  color: var(--adm-text-primary);
  margin-bottom: 8px;
  padding: 8px;
  background: var(--adm-surface-elevated);
  border-radius: 4px;
  border: 1px solid var(--adm-border);
}
.vl-group-images {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
  font-size: 12px;
  color: var(--adm-text-tertiary);
}
</style>
