<template>
  <div class="status-panel">
    <div class="status-top">
      <div class="status-indicator">
        <span class="status-dot" :class="state" />
        <span class="status-text">{{ statusText }}</span>
        <span v-if="state === 'done' && status.lastCompleted" class="status-meta">
          （耗时 {{ formatDuration(status.lastCompleted.durationSeconds) }}，发现
          {{ status.lastCompleted.newZombieCount }} 个{{ status.lastCompleted.referenceContractVersion === 'storage-references-v1' ? '未观察到引用的对象' : '历史发现' }}，
          {{ status.lastCompleted.triggerType === 'manual' ? '手动触发' : '定时触发' }}）
        </span>
        <span v-if="status.lastFailed" class="status-meta error">
          上次失败：{{ status.lastFailed.errorMessage }}
        </span>
      </div>
      <span class="auto-refresh-hint">
        <el-icon class="is-loading"><Refresh /></el-icon>
        每 10 秒自动刷新
      </span>
    </div>
    <div v-if="referenceProofs.length === 3" class="reference-proofs">
      <span v-for="proof in referenceProofs" :key="proof.provider">
        {{ proof.provider }}：{{ proof.entryCount }} 条引用，采集于 {{ proof.createdAt }}
      </span>
    </div>
    <div class="oss-stats">
      <div class="oss-stat">
        <div class="oss-stat-label">上次完成</div>
        <div class="oss-stat-value green">
          {{ status.lastCompleted ? formatDateTime(status.lastCompleted.completedAt || 0) : '无' }}
        </div>
      </div>
      <div class="oss-stat">
        <div class="oss-stat-label">上次失败</div>
        <div class="oss-stat-value gray">
          {{ status.lastFailed ? formatDateTime(status.lastFailed.completedAt || 0) : '无' }}
        </div>
      </div>
      <div class="oss-stat">
        <div class="oss-stat-label">历史待处理</div>
        <div class="oss-stat-value red">{{ status.pendingCount }}</div>
      </div>
      <div class="oss-stat">
        <div class="oss-stat-label">未观察到引用</div>
        <div class="oss-stat-value gray">{{ status.observationCount ?? 0 }}</div>
      </div>
      <div class="oss-stat">
        <div class="oss-stat-label">已忽略</div>
        <div class="oss-stat-value amber">{{ ignoredCount }}</div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Refresh } from '@element-plus/icons-vue'
import type { AuditStatusResponse } from '../../services/ossAuditApi'
import { formatDateTime } from '../../utils/subject'
import { formatDuration } from './ossAudit'

const props = defineProps<{
  status: AuditStatusResponse
  /** statusCounts[2] of the last records response. */
  ignoredCount: number
}>()

// Only show complete typed evidence from the persisted run, never infer old history.
const referenceProofs = computed<Array<{ provider: string; entryCount: number; createdAt: string }>>(() => {
  if (props.status.lastCompleted?.referenceContractVersion !== 'storage-references-v1') return []
  try {
    const proofs: unknown = JSON.parse(props.status.lastCompleted.referenceSnapshots ?? '')
    if (!Array.isArray(proofs) || proofs.length !== 3) return []
    const names = new Set<string>()
    for (const proof of proofs) {
      if (!proof || !['student', 'mistake', 'homework'].includes(proof.provider)
        || names.has(proof.provider) || !Number.isInteger(proof.entryCount) || proof.entryCount < 0
        || typeof proof.createdAt !== 'string') return []
      names.add(proof.provider)
    }
    return proofs
  } catch { return [] }
})

// Three distinct states: never report "completed" when no scan has ever completed.
const state = computed<'running' | 'never' | 'done'>(() => {
  if (props.status.isRunning) return 'running'
  return props.status.lastCompleted ? 'done' : 'never'
})

const statusText = computed(() => {
  if (state.value === 'running') return '扫描进行中...'
  if (state.value === 'never') return '尚无完成的扫描'
  return `上次完成于 ${formatDateTime(props.status.lastCompleted?.completedAt || 0)}`
})
</script>

<style scoped>
.status-panel {
  padding: 20px 24px;
  margin-bottom: 16px;
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
}
.status-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
  gap: 16px;
  flex-wrap: wrap;
}
.status-indicator {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}
.status-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  position: relative;
  flex-shrink: 0;
}
.status-dot.done { background: var(--adm-success); color: var(--adm-success); }
.status-dot.running { background: var(--adm-warning); color: var(--adm-warning); }
.status-dot.never { background: var(--adm-text-muted); color: var(--adm-text-muted); }
.status-dot::after {
  content: '';
  position: absolute;
  inset: -4px;
  border-radius: 50%;
  border: 2px solid currentColor;
  opacity: 0.4;
}
.status-dot.running::after { animation: pulse-ring 2s ease-out infinite; }
@keyframes pulse-ring {
  0% { transform: scale(0.8); opacity: 0.6; }
  100% { transform: scale(1.8); opacity: 0; }
}
.status-text {
  font-size: 15px;
  font-weight: 600;
  color: var(--adm-text-primary);
}
.status-meta {
  font-size: 12px;
  color: var(--adm-text-tertiary);
}
.status-meta.error { color: var(--adm-error); }
.reference-proofs { display: flex; flex-wrap: wrap; gap: 16px; font-size: 12px; margin-bottom: 16px; color: var(--adm-text-secondary); }
.auto-refresh-hint {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: var(--adm-text-muted);
}
.oss-stats {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  overflow: hidden;
}
.oss-stat {
  padding: 14px 18px;
  border-right: 1px solid var(--adm-border);
}
.oss-stat:last-child { border-right: none; }
.oss-stat-label {
  font-size: 12px;
  color: var(--adm-text-tertiary);
  margin-bottom: 4px;
}
.oss-stat-value {
  font-size: 18px;
  font-weight: 700;
  line-height: 1.2;
}
.oss-stat-value.green { color: var(--adm-success); }
.oss-stat-value.gray { color: var(--adm-text-muted); }
.oss-stat-value.red { color: var(--adm-error); }
.oss-stat-value.amber { color: #a16207; }
</style>
