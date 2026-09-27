<template>
  <!-- Click a card to filter by that review status; clicking the active card (or 总错题) clears it. -->
  <div class="stats-row">
    <div
      v-for="s in CARDS"
      :key="s.key"
      class="stat-card"
      :class="[s.key, { active: active === s.value }]"
      :aria-pressed="active === s.value"
      role="button"
      tabindex="0"
      @click="emit('select', s.value)"
      @keydown.enter.prevent="emit('select', s.value)"
    >
      <div>
        <div class="st-label">{{ s.label }}</div>
        <div class="st-value">{{ loading ? '—' : counts[s.key] }}</div>
      </div>
      <div class="st-icon">
        <el-icon :size="20" :class="{ 'is-loading': loading }">
          <Loading v-if="loading" />
          <component :is="s.icon" v-else />
        </el-icon>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { Check, Clock, Close, Loading, Notebook } from '@element-plus/icons-vue'

export interface MistakeStats {
  total: number
  pending: number
  confirmed: number
  rejected: number
}

defineProps<{
  counts: MistakeStats
  loading: boolean
  active: number | undefined
}>()

const emit = defineEmits<{
  select: [status: number | undefined]
}>()

const CARDS = [
  { key: 'total', label: '总错题', value: undefined, icon: Notebook },
  { key: 'pending', label: '待审核', value: 1, icon: Clock },
  { key: 'confirmed', label: '已确认', value: 2, icon: Check },
  { key: 'rejected', label: '已退回', value: 3, icon: Close },
] as const
</script>

<style scoped>
.stats-row {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 16px;
}
.stat-card {
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  padding: 16px 18px;
  display: flex;
  align-items: center;
  position: relative;
  overflow: hidden;
  cursor: pointer;
  transition: all 0.2s;
}
.stat-card::before {
  content: '';
  position: absolute;
  left: 0; top: 0; bottom: 0;
  width: 3px;
}
.stat-card.total::before { background: var(--adm-primary); }
.stat-card.pending::before { background: var(--adm-warning); }
.stat-card.confirmed::before { background: var(--adm-success); }
.stat-card.rejected::before { background: var(--adm-error); }
.stat-card:hover { box-shadow: var(--adm-shadow-sm); transform: translateY(-1px); }
.stat-card:focus-visible { outline: 2px solid var(--adm-primary); outline-offset: 2px; }
.stat-card.active {
  border-color: var(--adm-primary);
  background: var(--adm-primary-bg);
}
.st-label {
  font-size: 12px;
  color: var(--adm-text-tertiary);
  margin-bottom: 6px;
}
.st-value {
  font-size: 24px;
  font-weight: 700;
  line-height: 1;
  color: var(--adm-text-primary);
}
.stat-card.total .st-value { color: var(--adm-primary); }
.stat-card.pending .st-value { color: var(--adm-warning); }
.stat-card.confirmed .st-value { color: var(--adm-success); }
.stat-card.rejected .st-value { color: var(--adm-error); }
.st-icon {
  width: 40px;
  height: 40px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-left: auto;
}
.stat-card.total .st-icon { background: var(--adm-primary-bg); color: var(--adm-primary); }
.stat-card.pending .st-icon { background: var(--adm-warning-bg); color: var(--adm-warning); }
.stat-card.confirmed .st-icon { background: var(--adm-success-bg); color: var(--adm-success); }
.stat-card.rejected .st-icon { background: var(--adm-error-bg); color: var(--adm-error); }
</style>
