<template>
  <div v-if="analyzing" class="vl-card inline">
    <span class="vl-loading"><el-icon class="is-loading"><Loading /></el-icon>VL 模型分析中，请稍候...</span>
  </div>
  <div v-else-if="!result" class="vl-card inline">
    <p>暂未运行 VL 分析</p>
    <el-button type="primary" size="small" @click="emit('analyze')">运行 VL 分析</el-button>
  </div>
  <div v-else-if="!result.success" class="vl-card stacked">
    <span class="vl-badge error">分析失败</span>
    <p>{{ result.errorMessage || 'VL 分析失败' }}</p>
    <el-button size="small" @click="emit('analyze')">重试</el-button>
  </div>
  <div v-else-if="result.skipped" class="vl-card stacked">
    <span class="vl-badge warning">已跳过</span>
    <p>{{ result.errorMessage || '该记录没有图片或 VL 未配置' }}</p>
  </div>
  <div v-else-if="result.groups.length === 0" class="vl-card stacked">
    <span class="vl-badge warning">无有效分组</span>
    <p>模型返回了空内容，可能是图片质量问题或模型未能识别</p>
  </div>
  <div v-else class="vl-card">
    <div class="vl-card-header">
      <span class="vl-badge">VL 分析结果</span>
      <span class="vl-meta">识别到 {{ result.groups.length }} 个分组</span>
    </div>
    <div class="vl-groups">
      <span v-for="(g, idx) in result.groups" :key="idx" class="vl-group">
        <span class="g-imgs">图片 {{ g.imageIndices.map(i => i + 1).join(',') }}</span>
        <span class="g-subj">· {{ getSubjectLabel(g.subject) }} · {{ gradeLabel(g.grade) }}</span>
      </span>
    </div>
    <p v-if="result.groups[0]?.description" class="vl-desc">{{ result.groups[0].description }}</p>
    <el-button link type="primary" @click="emit('showDetail')">查看完整响应</el-button>
  </div>
</template>

<script setup lang="ts">
import { Loading } from '@element-plus/icons-vue'
import type { VlAnalysisResponse } from '../../services/studentAdminApi'
import { getSubjectLabel } from '../../utils/subject'

defineProps<{
  result: VlAnalysisResponse | null
  analyzing: boolean
  gradeLabel: (grade: number) => string
}>()

const emit = defineEmits<{
  analyze: []
  showDetail: []
}>()
</script>

<style scoped>
.vl-card {
  background: linear-gradient(135deg, #faf5ff, #eff6ff);
  border: 1px solid #ddd6fe;
  border-radius: var(--adm-radius-md);
  padding: 14px;
  margin-bottom: 16px;
}
.vl-card.inline {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  font-size: 12px;
  color: var(--adm-text-secondary);
}
.vl-card.stacked {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 10px;
}
.vl-card p { font-size: 12px; color: var(--adm-text-secondary); line-height: 1.7; margin: 0; }
.vl-loading {
  display: inline-flex;
  align-items: center;
  gap: 8px;
}
.vl-card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}
.vl-badge {
  display: inline-flex;
  align-items: center;
  padding: 3px 8px;
  border-radius: 4px;
  background: #7c3aed;
  color: #fff;
  font-size: 11px;
  font-weight: 600;
}
.vl-badge.error { background: var(--adm-error); }
.vl-badge.warning { background: var(--adm-warning); }
.vl-meta { font-size: 11px; color: var(--adm-text-muted); }
.vl-groups { margin-bottom: 6px; }
.vl-group {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  background: #fff;
  padding: 3px 10px;
  border-radius: 4px;
  margin: 2px 4px 2px 0;
  font-size: 12px;
  border: 1px solid #e2e8f0;
}
.vl-group .g-imgs {
  color: var(--adm-primary);
  font-weight: 600;
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 11px;
}
.vl-group .g-subj { color: var(--adm-text-secondary); }
.vl-desc {
  margin: 6px 0;
  padding: 6px 10px;
  background: #fff;
  border-radius: 4px;
  border: 1px solid #e2e8f0;
  color: #7c3aed !important;
}
</style>
