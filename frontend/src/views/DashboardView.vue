<template>
  <div class="dashboard-view">
    <div class="adm-page-header">
      <div>
        <h1 class="adm-page-title">待办与概况</h1>
        <p class="adm-page-subtitle">待处理事项与账号规模，数字均实时取自各列表接口；点击卡片进入对应列表。</p>
      </div>
      <div class="adm-page-actions">
        <el-button :icon="Refresh" :loading="anyLoading" @click="loadAll">刷新</el-button>
      </div>
    </div>

    <section v-for="group in groups" :key="group.key" class="metric-section">
      <h2 class="metric-section-title">{{ group.title }}</h2>
      <div class="metric-grid">
        <div
          v-for="m in group.metrics"
          :key="m.key"
          class="metric-card"
          :class="{
            'is-error': m.state === 'error',
            'has-pending': group.key === 'pending' && m.state === 'ok' && (m.value ?? 0) > 0,
          }"
        >
          <router-link :to="m.to" class="metric-link" :aria-label="`${m.label}，进入列表`">
            <span class="metric-icon"><el-icon><component :is="m.icon" /></el-icon></span>
            <span class="metric-body">
              <span class="metric-label">{{ m.label }}</span>
              <span v-if="m.state === 'loading'" class="metric-value is-muted">…</span>
              <span v-else-if="m.state === 'error'" class="metric-value is-muted">—</span>
              <span v-else class="metric-value">{{ formatNumber(m.value ?? 0) }}</span>
            </span>
          </router-link>
          <div v-if="m.state === 'error'" class="metric-error">
            <span>加载失败</span>
            <el-button link type="primary" size="small" @click="loadMetric(m)">重试</el-button>
          </div>
        </div>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, markRaw, onMounted, reactive, type Component } from 'vue'
import type { RouteLocationRaw } from 'vue-router'
import { Box, Collection, Reading, Refresh, Service, Upload, User } from '@element-plus/icons-vue'
import studentAdminClient from '../services/studentAdminApi'
import teacherPortalClient from '../services/teacherPortalApi'
import assistantPortalClient from '../services/assistantPortalApi'
import ossAuditApi from '../services/ossAuditApi'

type MetricState = 'loading' | 'ok' | 'error'

interface Metric {
  key: string
  label: string
  icon: Component
  to: RouteLocationRaw
  fetch: () => Promise<number>
  state: MetricState
  value: number | null
}

// A missing or non-numeric count means the upstream contract changed; treat it as a failure
// instead of rendering 0.
function requireCount(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error('count missing from response')
  return value
}

function metric(key: string, label: string, icon: Component, to: RouteLocationRaw, fetch: () => Promise<number>): Metric {
  return { key, label, icon: markRaw(icon), to, fetch, state: 'loading', value: null }
}

const groups = reactive([
  {
    key: 'pending',
    title: '待处理',
    metrics: [
      metric('pendingUploads', '待处理上传', Upload, { path: '/upload-records', query: { status: '1' } }, async () =>
        requireCount((await studentAdminClient.getUploadRecords({ page: 1, pageSize: 1, status: 1 })).totalCount)),
      metric('pendingMistakes', '待审核错题', Collection, { path: '/mistakes', query: { reviewStatus: '1' } }, async () =>
        requireCount((await studentAdminClient.getMistakeItems({ page: 1, size: 1, reviewStatus: 1 })).total)),
      metric('pendingOssAudit', '待处置 OSS 审计记录', Box, { path: '/oss-audit', query: { status: '0' } }, async () =>
        requireCount((await ossAuditApi.getStatus()).pendingCount)),
    ],
  },
  {
    key: 'scale',
    title: '规模',
    metrics: [
      metric('students', '学生', User, { path: '/students' }, async () =>
        requireCount((await studentAdminClient.getStudents({ page: 1, pageSize: 1 })).total)),
      metric('teachers', '教师', Reading, { path: '/teachers' }, async () => {
        const res = await teacherPortalClient.getTeachers({ page: 1, pageSize: 1 })
        if (res.success === false) throw new Error('teacher list failed')
        return requireCount(res.total)
      }),
      metric('assistants', '助教', Service, { path: '/assistants' }, async () => {
        const res = await assistantPortalClient.getAssistants({ page: 1, pageSize: 1 })
        if (res.success === false) throw new Error('assistant list failed')
        return requireCount(res.total)
      }),
    ],
  },
])

const allMetrics = computed(() => groups.flatMap((g) => g.metrics))
const anyLoading = computed(() => allMetrics.value.some((m) => m.state === 'loading'))

// Each card loads independently: one failing request only affects its own card.
async function loadMetric(m: Metric) {
  m.state = 'loading'
  try {
    m.value = await m.fetch()
    m.state = 'ok'
  } catch {
    m.value = null
    m.state = 'error'
  }
}

function loadAll() {
  return Promise.allSettled(allMetrics.value.map(loadMetric))
}

function formatNumber(n: number): string {
  return n.toLocaleString('zh-CN')
}

onMounted(() => {
  loadAll()
})
</script>

<style scoped>
.metric-section + .metric-section {
  margin-top: 24px;
}
.metric-section-title {
  margin: 0 0 12px;
  color: var(--adm-text-secondary);
  font-size: 13px;
  font-weight: 600;
}
.metric-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
  gap: 12px;
}
.metric-card {
  position: relative;
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  transition: border-color 0.15s, box-shadow 0.15s;
}
.metric-card:hover {
  border-color: var(--adm-border-hover);
  box-shadow: var(--adm-shadow-sm);
}
.metric-card.has-pending {
  border-left: 3px solid var(--adm-warning);
}
.metric-card.is-error {
  border-color: var(--adm-error-border);
}
.metric-link {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 16px 20px;
  color: inherit;
  border-radius: inherit;
}
.metric-link:focus-visible {
  outline: 2px solid var(--adm-primary);
  outline-offset: 2px;
}
.metric-icon {
  width: 40px;
  height: 40px;
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--adm-radius-md);
  background: var(--adm-primary-bg);
  color: var(--adm-primary);
  font-size: 18px;
}
.has-pending .metric-icon {
  background: var(--adm-warning-bg);
  color: var(--adm-warning);
}
.metric-body {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}
.metric-label {
  color: var(--adm-text-tertiary);
  font-size: 13px;
}
.metric-value {
  color: var(--adm-text-primary);
  font-size: 26px;
  font-weight: 700;
  line-height: 1.1;
  font-variant-numeric: tabular-nums;
}
.metric-value.is-muted {
  color: var(--adm-text-muted);
}
.metric-error {
  position: absolute;
  right: 16px;
  bottom: 14px;
  display: flex;
  align-items: center;
  gap: 8px;
  color: var(--adm-error);
  font-size: 12px;
}
</style>
