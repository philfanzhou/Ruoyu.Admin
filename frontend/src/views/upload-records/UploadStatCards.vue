<template>
  <!-- Click a card to filter by that status; clicking the active card clears the filter. -->
  <div class="stats-row">
    <div
      v-for="s in UPLOAD_STATUSES"
      :key="s.value"
      class="adm-stat-pill"
      :class="[s.key, { active: active === s.value }]"
      role="button"
      tabindex="0"
      :aria-pressed="active === s.value"
      @click="emit('select', s.value)"
      @keydown.enter.prevent="emit('select', s.value)"
    >
      <div class="s-icon">
        <el-icon :size="18" :class="{ 'is-loading': loading }">
          <Loading v-if="loading" />
          <component :is="STAT_ICONS[s.key]" v-else />
        </el-icon>
      </div>
      <div>
        <div class="sp-label">{{ s.label }}</div>
        <div class="sp-value">{{ loading ? '—' : counts[s.value] ?? 0 }}</div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { Component } from 'vue'
import { CircleClose, Clock, Loading, View, Warning } from '@element-plus/icons-vue'
import { UPLOAD_STATUSES, type UploadStatusMeta } from './uploadRecord'

defineProps<{
  counts: Record<number, number>
  loading: boolean
  active: number | undefined
}>()

const emit = defineEmits<{
  select: [status: number]
}>()

const STAT_ICONS: Record<UploadStatusMeta['key'], Component> = {
  pending: Clock, processing: Loading, review: View, failed: CircleClose, returned: Warning,
}
</script>

<style scoped>
.stats-row {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 16px;
}
.adm-stat-pill:focus-visible { outline: 2px solid var(--adm-primary); outline-offset: 2px; }
.s-icon {
  width: 32px;
  height: 32px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.adm-stat-pill.pending .s-icon { background: var(--adm-warning-bg); color: var(--adm-warning); }
.adm-stat-pill.processing .s-icon { background: var(--adm-info-bg); color: var(--adm-info); }
.adm-stat-pill.review .s-icon { background: var(--adm-success-bg); color: var(--adm-success); }
.adm-stat-pill.review::before { background: var(--adm-success); }
.adm-stat-pill.failed .s-icon { background: var(--adm-error-bg); color: var(--adm-error); }
.adm-stat-pill.returned .s-icon { background: #fff7ed; color: #ea580c; }
.sp-value { color: var(--adm-text-primary); }
@media (max-width: 1100px) {
  .stats-row { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}
</style>
