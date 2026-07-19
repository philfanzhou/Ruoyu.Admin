<template>
  <div class="adm-fade-in oss-audit-view">
    <!-- Page Header -->
    <div class="adm-page-header">
      <div>
        <h1 class="adm-page-title">OSS 文件审计</h1>
        <p class="adm-page-subtitle">扫描 OSS 存储中的孤儿文件、异常文件，清理冗余存储</p>
      </div>
      <div class="adm-page-actions">
        <button
          class="adm-btn adm-btn-warning"
          :disabled="auditStatus.isRunning || auditLoading"
          @click="handleTriggerAudit"
        >
          <svg
            v-if="auditLoading"
            class="spin-icon"
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2.5"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <path d="M21 12a9 9 0 1 1-6.219-8.56" />
          </svg>
          <svg
            v-else
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <circle cx="12" cy="12" r="10" />
            <line x1="12" y1="8" x2="12" y2="12" />
            <line x1="12" y1="16" x2="12.01" y2="16" />
          </svg>
          {{ auditLoading ? '触发中...' : (auditStatus.isRunning ? '扫描进行中' : '触发全量扫描') }}
        </button>
        <button class="adm-btn adm-btn-danger-outline" disabled title="当前后端未提供停止接口">
          <svg
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <rect x="6" y="4" width="4" height="16" />
            <rect x="14" y="4" width="4" height="16" />
          </svg>
          停止扫描
        </button>
      </div>
    </div>

    <!-- Status Panel -->
    <div v-if="statusLoaded" class="adm-card status-panel">
      <div class="status-top">
        <div class="status-indicator">
          <span class="status-dot" :class="auditStatus.isRunning ? 'orange' : 'green'"></span>
          <span class="status-text">
            {{ auditStatus.isRunning ? '扫描进行中...' : '扫描已完成' }}
          </span>
          <span v-if="auditStatus.lastCompleted" class="status-meta">
            （耗时 {{ formatDuration(auditStatus.lastCompleted.durationSeconds) }}，发现
            {{ auditStatus.lastCompleted.newZombieCount }} 个僵尸文件，
            {{ auditStatus.lastCompleted.triggerType === 'manual' ? '手动触发' : '定时触发' }}）
          </span>
          <span v-else-if="auditStatus.lastFailed" class="status-meta error">
            上次失败：{{ auditStatus.lastFailed.errorMessage }}
          </span>
        </div>
        <span class="auto-refresh-hint">
          <svg
            class="spin-icon"
            width="12"
            height="12"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <polyline points="23 4 23 10 17 10" />
            <polyline points="1 20 1 14 7 14" />
            <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
          </svg>
          每 10 秒自动刷新
        </span>
      </div>
      <div class="oss-stats">
        <div class="oss-stat">
          <div class="oss-stat-label">上次完成</div>
          <div class="oss-stat-value green">
            {{ auditStatus.lastCompleted
              ? formatDateTime(auditStatus.lastCompleted.completedAt || 0)
              : '无' }}
          </div>
        </div>
        <div class="oss-stat">
          <div class="oss-stat-label">上次失败</div>
          <div class="oss-stat-value gray">
            {{ auditStatus.lastFailed
              ? formatDateTime(auditStatus.lastFailed.completedAt || 0)
              : '无' }}
          </div>
        </div>
        <div class="oss-stat">
          <div class="oss-stat-label">待处理</div>
          <div class="oss-stat-value red">{{ auditStatus.pendingCount }}</div>
        </div>
        <div class="oss-stat">
          <div class="oss-stat-label">已清理</div>
          <div class="oss-stat-value green">{{ cleanedCount }}</div>
        </div>
      </div>
    </div>

    <!-- Filter Bar -->
    <div class="adm-card filter-bar">
      <select v-model="filterStatus" class="filter-select" @change="handleFilterChange">
        <option :value="undefined">状态：全部</option>
        <option v-for="s in statusOptions" :key="s.value" :value="s.value">{{ s.label }}</option>
      </select>
      <select v-model="filterBucket" class="filter-select bucket-select" @change="handleFilterChange">
        <option :value="undefined">Bucket：全部</option>
        <option v-for="b in bucketOptions" :key="b" :value="b">{{ b }}</option>
      </select>
      <div class="filter-input-wrap">
        <svg
          class="filter-input-icon"
          width="15"
          height="15"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        >
          <circle cx="11" cy="11" r="8" />
          <line x1="21" y1="21" x2="16.65" y2="16.65" />
        </svg>
        <input
          v-model="filterPath"
          class="filter-input"
          placeholder="按文件名搜索..."
          @keyup.enter="onSearch"
        />
      </div>
      <div class="filter-spacer"></div>
      <button class="adm-btn adm-btn-primary adm-btn-sm" @click="onSearch">
        <svg
          width="13"
          height="13"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2.5"
          stroke-linecap="round"
          stroke-linejoin="round"
        >
          <circle cx="11" cy="11" r="8" />
          <line x1="21" y1="21" x2="16.65" y2="16.65" />
        </svg>
        搜索
      </button>
      <button class="adm-btn adm-btn-secondary adm-btn-sm" @click="resetFilters">
        <svg
          width="13"
          height="13"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          stroke-linejoin="round"
        >
          <polyline points="1 4 1 10 7 10" />
          <path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10" />
        </svg>
        重置
      </button>
    </div>

    <!-- Data Table -->
    <div class="adm-card table-card">
      <div v-if="loading" class="loading-row">
        <span class="spinner-lg"></span>
        <span>加载中...</span>
      </div>
      <div v-else-if="records.length === 0" class="adm-empty">
        <div class="adm-empty-icon">
          <svg
            width="28"
            height="28"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z" />
            <polyline points="3.27 6.96 12 12.01 20.73 6.96" />
            <line x1="12" y1="22.08" x2="12" y2="12" />
          </svg>
        </div>
        <div class="adm-empty-text">暂无审计记录</div>
      </div>
      <div v-else class="table-wrap">
        <table class="adm-table">
          <thead>
            <tr>
              <th class="col-check">
                <div
                  class="adm-checkbox"
                  :class="{ checked: isAllSelected }"
                  @click="toggleSelectAll"
                >
                  <svg
                    v-if="isAllSelected"
                    width="10"
                    height="10"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="#fff"
                    stroke-width="3"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                  >
                    <polyline points="20 6 9 17 4 12" />
                  </svg>
                </div>
              </th>
              <th class="col-id">ID</th>
              <th style="width: 150px;">Bucket</th>
              <th>文件路径</th>
              <th class="col-size">大小</th>
              <th>最后修改</th>
              <th style="width: 100px;">状态</th>
              <th>发现时间</th>
              <th class="col-actions">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="r in records"
              :key="r.id"
              :class="{ 'selected-row': isSelected(r.id) }"
            >
              <td>
                <div
                  class="adm-checkbox"
                  :class="{ checked: isSelected(r.id) }"
                  @click="toggleSelect(r.id)"
                >
                  <svg
                    v-if="isSelected(r.id)"
                    width="10"
                    height="10"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="#fff"
                    stroke-width="3"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                  >
                    <polyline points="20 6 9 17 4 12" />
                  </svg>
                </div>
              </td>
              <td class="col-id">{{ r.id }}</td>
              <td>
                <span class="bucket-tag" :style="getBucketStyle(r.bucket)">{{ r.bucket }}</span>
              </td>
              <td>
                <div class="path-cell">
                  <div class="thumb-mini" :style="{ opacity: r.status === 1 ? 0.4 : 1 }">
                    <img
                      v-if="isImagePath(r.objectPath)"
                      :src="getImageUrl(r.objectPath)"
                      :alt="r.objectPath"
                      class="thumb-img"
                      @error="onThumbError"
                    />
                    <svg
                      v-else
                      width="16"
                      height="16"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      stroke-width="2"
                      stroke-linecap="round"
                      stroke-linejoin="round"
                    >
                      <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                      <polyline points="14 2 14 8 20 8" />
                    </svg>
                  </div>
                  <div class="path-info">
                    <div class="file-path" :class="{ strike: r.status === 1 }">
                      <span class="file-path-text">{{ r.objectPath }}</span>
                      <button
                        class="copy-btn"
                        title="复制路径"
                        @click="copyPath(r.objectPath)"
                      >
                        <svg
                          width="13"
                          height="13"
                          viewBox="0 0 24 24"
                          fill="none"
                          stroke="currentColor"
                          stroke-width="2"
                          stroke-linecap="round"
                          stroke-linejoin="round"
                        >
                          <rect x="9" y="9" width="13" height="13" rx="2" ry="2" />
                          <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" />
                        </svg>
                      </button>
                    </div>
                    <span v-if="r.status === 2 && r.note" class="ignore-reason-inline">
                      忽略原因：{{ r.note }}
                    </span>
                  </div>
                </div>
              </td>
              <td>
                <span class="file-size" :class="{ strike: r.status === 1 }">
                  {{ formatFileSize(r.size) }}
                </span>
              </td>
              <td><span class="time-cell">{{ formatDateTime(r.lastModified) }}</span></td>
              <td>
                <span class="oss-status" :class="getOssStatusClass(r.status)">
                  <svg
                    v-if="r.status === 0"
                    width="12"
                    height="12"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="2.5"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                  >
                    <circle cx="12" cy="12" r="10" />
                    <line x1="12" y1="8" x2="12" y2="12" />
                    <line x1="12" y1="16" x2="12.01" y2="16" />
                  </svg>
                  <svg
                    v-else-if="r.status === 1"
                    width="12"
                    height="12"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="2.5"
                    stroke-linecap="round"
                    stroke-linejoin="round"
                  >
                    <polyline points="3 6 5 6 21 6" />
                    <path d="M19 6l-2 14a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2L5 6" />
                  </svg>
                  {{ r.statusText || getOssStatusLabel(r.status) }}
                </span>
              </td>
              <td><span class="time-cell">{{ formatDateTime(r.createdAt) }}</span></td>
              <td>
                <div class="actions-cell">
                  <template v-if="r.status === 0">
                    <button class="adm-btn-link danger" @click="handleResolve(r)">删除</button>
                    <button class="adm-btn-link warning" @click="handleIgnore(r)">忽略</button>
                  </template>
                  <span v-else class="text-muted">—</span>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
        <!-- Pagination -->
        <div class="pagination-bar">
          <div class="pagination-info">
            共 <strong>{{ total }}</strong> 条记录
          </div>
          <div class="pagination-controls">
            <button class="page-btn" :disabled="page === 1" @click="onPageChange(page - 1)">
              <svg
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
              >
                <polyline points="15 18 9 12 15 6" />
              </svg>
            </button>
            <template v-for="(p, idx) in pageButtons" :key="idx">
              <span v-if="p === -1" class="page-ellipsis">...</span>
              <button
                v-else
                class="page-btn"
                :class="{ active: p === page }"
                @click="onPageChange(p)"
              >
                {{ p }}
              </button>
            </template>
            <button
              class="page-btn"
              :disabled="page === totalPages"
              @click="onPageChange(page + 1)"
            >
              <svg
                width="14"
                height="14"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
              >
                <polyline points="9 18 15 12 9 6" />
              </svg>
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Floating Bulk Action Bar -->
    <transition name="bulk-bar-transition">
      <div v-if="selectedIds.length > 0" class="bulk-bar">
        <div class="bulk-bar-info">
          已选择 <strong>{{ selectedIds.length }}</strong> 个文件
        </div>
        <button
          class="adm-btn adm-btn-danger adm-btn-sm"
          :disabled="batchLoading"
          @click="handleBatchResolve"
        >
          <svg
            width="13"
            height="13"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <polyline points="3 6 5 6 21 6" />
            <path d="M19 6l-2 14a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2L5 6" />
          </svg>
          {{ batchLoading ? '处理中...' : '批量删除' }}
        </button>
        <button
          class="adm-btn adm-btn-warning-outline adm-btn-sm"
          :disabled="batchLoading"
          @click="handleBatchIgnore"
        >
          <svg
            width="13"
            height="13"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
            stroke-linecap="round"
            stroke-linejoin="round"
          >
            <circle cx="12" cy="12" r="10" />
            <line x1="4.93" y1="4.93" x2="19.07" y2="19.07" />
          </svg>
          批量忽略
        </button>
        <button class="bulk-cancel-btn" :disabled="batchLoading" @click="clearSelection">取消</button>
      </div>
    </transition>

    <!-- Ignore File Modal -->
    <transition name="modal-transition">
      <div v-if="showIgnoreDialog" class="modal-overlay" @click.self="showIgnoreDialog = false">
        <div class="modal">
          <div class="modal-header">
            <div class="modal-title">
              <svg
                width="18"
                height="18"
                viewBox="0 0 24 24"
                fill="none"
                stroke="var(--adm-warning)"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
              >
                <circle cx="12" cy="12" r="10" />
                <line x1="4.93" y1="4.93" x2="19.07" y2="19.07" />
              </svg>
              忽略文件
            </div>
            <button class="modal-close" @click="showIgnoreDialog = false">
              <svg
                width="18"
                height="18"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2"
                stroke-linecap="round"
                stroke-linejoin="round"
              >
                <line x1="18" y1="6" x2="6" y2="18" />
                <line x1="6" y1="6" x2="18" y2="18" />
              </svg>
            </button>
          </div>
          <div class="modal-body">
            <div class="form-label">文件路径</div>
            <div class="file-path-display">
              {{ ignoreForm.bucket }}/{{ ignoreForm.objectPath }}
            </div>
            <div class="form-group">
              <label class="form-label">忽略原因（可选）</label>
              <textarea
                v-model="ignoreForm.note"
                class="form-textarea"
                placeholder="例如：用户头像、系统生成文件等"
              ></textarea>
              <div class="form-helper">
                忽略的文件将不再出现在待处理列表中，但保留记录以便审计追溯。
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="adm-btn adm-btn-secondary" @click="showIgnoreDialog = false">
              取消
            </button>
            <button class="adm-btn adm-btn-warning" @click="confirmIgnore">
              <svg
                width="13"
                height="13"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="2.5"
                stroke-linecap="round"
                stroke-linejoin="round"
              >
                <circle cx="12" cy="12" r="10" />
                <line x1="4.93" y1="4.93" x2="19.07" y2="19.07" />
              </svg>
              确认忽略
            </button>
          </div>
        </div>
      </div>
    </transition>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  ossAuditApi,
  getOssAuditErrorMessage,
  formatFileSize,
  type OssAuditRecordDto,
  type AuditStatusResponse
} from '../services/ossAuditApi'
import { formatDateTime } from '../utils/subject'

const records = ref<OssAuditRecordDto[]>([])
const loading = ref(false)
const auditLoading = ref(false)
const batchLoading = ref(false)
const total = ref(0)
const page = ref(1)
const pageSize = 20
const cleanedCount = ref(0)

const filterStatus = ref<number>()
const filterBucket = ref<string>()
const filterPath = ref<string>('')
const bucketOptions = ref<string[]>([])

const selectedIds = ref<number[]>([])

const showIgnoreDialog = ref(false)
const ignoreForm = ref({
  id: 0,
  bucket: '',
  objectPath: '',
  note: ''
})

const statusLoaded = ref(false)
const auditStatus = ref<AuditStatusResponse>({
  isRunning: false,
  lastCompleted: null,
  lastFailed: null,
  pendingCount: 0
})

let pollTimer: ReturnType<typeof setInterval> | null = null
let wasRunning = false

const statusOptions = [
  { value: 0, label: '待处理' },
  { value: 1, label: '已删除' },
  { value: 2, label: '已忽略' }
]

// ===== Computed =====

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))

const pageButtons = computed<number[]>(() => {
  const tp = totalPages.value
  const cur = page.value
  const buttons: number[] = []
  if (tp <= 7) {
    for (let i = 1; i <= tp; i++) buttons.push(i)
    return buttons
  }
  buttons.push(1)
  if (cur > 4) buttons.push(-1) // ellipsis
  const start = Math.max(2, cur - 1)
  const end = Math.min(tp - 1, cur + 1)
  for (let i = start; i <= end; i++) buttons.push(i)
  if (cur < tp - 3) buttons.push(-1) // ellipsis
  buttons.push(tp)
  return buttons
})

const isAllSelected = computed(() => {
  if (records.value.length === 0) return false
  return records.value.every((r) => selectedIds.value.includes(r.id))
})

// ===== Helpers =====

function isImagePath(path: string): boolean {
  if (!path) return false
  const ext = path.split('.').pop()?.toLowerCase() || ''
  return ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp'].includes(ext)
}

function getImageUrl(path: string): string {
  return `/api/admin/image?path=${encodeURIComponent(path)}`
}

function onThumbError(event: Event): void {
  const img = event.target as HTMLImageElement
  img.style.display = 'none'
}

function getOssStatusClass(status: number): string {
  const map: Record<number, string> = { 0: 'pending', 1: 'deleted', 2: 'ignored' }
  return map[status] || 'pending'
}

function getOssStatusLabel(status: number): string {
  const map: Record<number, string> = { 0: '待处理', 1: '已删除', 2: '已忽略' }
  return map[status] || '未知'
}

function getBucketStyle(bucket: string): Record<string, string> {
  const b = (bucket || '').toLowerCase()
  if (b.includes('mistake')) {
    return { background: '#faf5ff', color: '#7c3aed', borderColor: '#ddd6fe' }
  }
  if (b.includes('homework') || b.includes('hw')) {
    return { background: '#f0fdf4', color: '#16a34a', borderColor: '#bbf7d0' }
  }
  return {}
}

function formatDuration(seconds: number | null | undefined): string {
  if (seconds == null) return '-'
  if (seconds < 60) return `${seconds} 秒`
  if (seconds < 3600) return `${Math.floor(seconds / 60)} 分 ${seconds % 60} 秒`
  return `${Math.floor(seconds / 3600)} 时 ${Math.floor((seconds % 3600) / 60)} 分`
}

function isSelected(id: number): boolean {
  return selectedIds.value.includes(id)
}

function toggleSelect(id: number): void {
  const idx = selectedIds.value.indexOf(id)
  if (idx >= 0) {
    selectedIds.value.splice(idx, 1)
  } else {
    selectedIds.value.push(id)
  }
}

function toggleSelectAll(): void {
  if (isAllSelected.value) {
    selectedIds.value = []
  } else {
    selectedIds.value = records.value.map((r) => r.id)
  }
}

function clearSelection(): void {
  selectedIds.value = []
}

async function copyPath(path: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(path)
    ElMessage.success('已复制路径')
  } catch {
    ElMessage.error('复制失败')
  }
}

// ===== Data Loading =====

async function loadAuditStatus(): Promise<void> {
  try {
    auditStatus.value = await ossAuditApi.getStatus()
    statusLoaded.value = true
  } catch {
    // silent
  }
}

async function loadCleanedCount(): Promise<void> {
  try {
    // status=1 means "deleted" — get total count via pageSize=1
    const result = await ossAuditApi.getRecords(1, 1, 1)
    cleanedCount.value = result.totalCount
  } catch {
    // silent
  }
}

async function loadRecords(): Promise<void> {
  loading.value = true
  try {
    const result = await ossAuditApi.getRecords(
      page.value,
      pageSize,
      filterStatus.value,
      filterBucket.value
    )
    bucketOptions.value = Object.keys(result.bucketCounts)
    // Apply client-side path filter (API does not support path search)
    let items = result.items
    const kw = filterPath.value.trim().toLowerCase()
    if (kw) {
      items = items.filter((r) => r.objectPath.toLowerCase().includes(kw))
    }
    records.value = items
    total.value = result.totalCount
  } catch (error) {
    ElMessage.error(getOssAuditErrorMessage(error))
  } finally {
    loading.value = false
  }
}

// ===== Actions =====

async function handleTriggerAudit(): Promise<void> {
  if (auditStatus.value.isRunning) {
    ElMessage.warning('扫描正在进行中')
    return
  }
  try {
    await ElMessageBox.confirm(
      '确定要触发全量 OSS 扫描吗？这可能需要较长时间。',
      '确认',
      { type: 'warning' }
    )
  } catch {
    return
  }
  auditLoading.value = true
  try {
    const result = await ossAuditApi.triggerAudit()
    if (result.success) {
      ElMessage.success(result.message || '审计任务已触发')
      await Promise.all([loadAuditStatus(), loadRecords()])
      wasRunning = auditStatus.value.isRunning
    } else {
      ElMessage.error(result.message || '触发失败')
    }
  } catch (error) {
    ElMessage.error(getOssAuditErrorMessage(error))
  } finally {
    auditLoading.value = false
  }
}

function handleFilterChange(): void {
  page.value = 1
  loadRecords()
}

function onSearch(): void {
  page.value = 1
  loadRecords()
}

function onPageChange(p: number): void {
  if (p < 1 || p > totalPages.value || p === page.value) return
  page.value = p
  loadRecords()
}

function resetFilters(): void {
  filterStatus.value = undefined
  filterBucket.value = undefined
  filterPath.value = ''
  page.value = 1
  loadRecords()
}

async function handleResolve(record: OssAuditRecordDto): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确定要删除文件 "${record.objectPath}" 吗？`,
      '确认删除',
      { type: 'warning' }
    )
  } catch {
    return
  }
  try {
    const result = await ossAuditApi.resolveRecord(record.id)
    if (result.success) {
      ElMessage.success('删除成功')
      await Promise.all([loadRecords(), loadAuditStatus(), loadCleanedCount()])
    } else {
      ElMessage.error(result.message || '删除失败')
    }
  } catch (error) {
    ElMessage.error(getOssAuditErrorMessage(error))
  }
}

function handleIgnore(record: OssAuditRecordDto): void {
  ignoreForm.value = {
    id: record.id,
    bucket: record.bucket,
    objectPath: record.objectPath,
    note: ''
  }
  showIgnoreDialog.value = true
}

async function confirmIgnore(): Promise<void> {
  try {
    const result = await ossAuditApi.ignoreRecord(
      ignoreForm.value.id,
      ignoreForm.value.note || undefined
    )
    if (result.success) {
      ElMessage.success('已忽略')
      showIgnoreDialog.value = false
      await Promise.all([loadRecords(), loadAuditStatus()])
    } else {
      ElMessage.error(result.message || '操作失败')
    }
  } catch (error) {
    ElMessage.error(getOssAuditErrorMessage(error))
  }
}

async function handleBatchResolve(): Promise<void> {
  if (selectedIds.value.length === 0) {
    ElMessage.warning('请选择要删除的记录')
    return
  }
  try {
    await ElMessageBox.confirm(
      `确定要删除选中的 ${selectedIds.value.length} 条记录吗？`,
      '确认批量删除',
      { type: 'warning' }
    )
  } catch {
    return
  }
  batchLoading.value = true
  try {
    const result = await ossAuditApi.batchResolve(selectedIds.value)
    if (result.resolvedCount > 0) {
      ElMessage.success(`成功删除 ${result.resolvedCount} 条记录`)
    }
    if (result.errors.length > 0) {
      ElMessage.warning(`有 ${result.errors.length} 条记录删除失败`)
    }
    selectedIds.value = []
    await Promise.all([loadRecords(), loadAuditStatus(), loadCleanedCount()])
  } catch (error) {
    ElMessage.error(getOssAuditErrorMessage(error))
  } finally {
    batchLoading.value = false
  }
}

async function handleBatchIgnore(): Promise<void> {
  if (selectedIds.value.length === 0) {
    ElMessage.warning('请选择要忽略的记录')
    return
  }
  // Only pending records (status === 0) can be ignored
  const pendingRecords = records.value.filter(
    (r) => selectedIds.value.includes(r.id) && r.status === 0
  )
  if (pendingRecords.length === 0) {
    ElMessage.warning('选中的记录中没有可忽略的待处理项')
    return
  }
  try {
    await ElMessageBox.confirm(
      `确定要忽略选中的 ${pendingRecords.length} 条待处理记录吗？`,
      '确认批量忽略',
      { type: 'warning' }
    )
  } catch {
    return
  }
  batchLoading.value = true
  try {
    let successCount = 0
    let failCount = 0
    for (const r of pendingRecords) {
      try {
        const result = await ossAuditApi.ignoreRecord(r.id)
        if (result.success) successCount++
        else failCount++
      } catch {
        failCount++
      }
    }
    if (successCount > 0) {
      ElMessage.success(`成功忽略 ${successCount} 条记录`)
    }
    if (failCount > 0) {
      ElMessage.warning(`${failCount} 条记录忽略失败`)
    }
    selectedIds.value = []
    await Promise.all([loadRecords(), loadAuditStatus()])
  } catch (error) {
    ElMessage.error(getOssAuditErrorMessage(error))
  } finally {
    batchLoading.value = false
  }
}

// ===== Lifecycle =====

onMounted(async () => {
  await Promise.all([loadRecords(), loadAuditStatus(), loadCleanedCount()])
  wasRunning = auditStatus.value.isRunning
  // Poll audit status every 10 seconds
  pollTimer = setInterval(async () => {
    await loadAuditStatus()
    if (wasRunning && !auditStatus.value.isRunning) {
      // Audit just completed, refresh records
      await Promise.all([loadRecords(), loadCleanedCount()])
    }
    wasRunning = auditStatus.value.isRunning
  }, 10000)
})

onUnmounted(() => {
  if (pollTimer) {
    clearInterval(pollTimer)
    pollTimer = null
  }
})
</script>

<style scoped>
.oss-audit-view {
  padding-bottom: 80px; /* space for bulk action bar */
}

/* ===== Status Panel ===== */
.status-panel {
  padding: 20px 24px;
  margin-bottom: 16px;
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
.status-dot.green {
  background: var(--adm-success);
  color: var(--adm-success);
}
.status-dot.orange {
  background: var(--adm-warning);
  color: var(--adm-warning);
}
.status-dot::after {
  content: '';
  position: absolute;
  inset: -4px;
  border-radius: 50%;
  border: 2px solid currentColor;
  opacity: 0.4;
}
@keyframes pulse-ring {
  0% { transform: scale(0.8); opacity: 0.8; }
  100% { transform: scale(2); opacity: 0; }
}
.status-dot.orange::after {
  animation: pulse-ring 2s ease-out infinite;
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
.status-meta.error {
  color: var(--adm-error);
}
.auto-refresh-hint {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: var(--adm-text-muted);
}
@keyframes spin { to { transform: rotate(360deg); } }
.spin-icon { animation: spin 1s linear infinite; }

.oss-stats {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 0;
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

/* ===== Filter Bar ===== */
.filter-bar {
  padding: 16px 20px;
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}
.filter-input-wrap {
  position: relative;
  flex: 1;
  min-width: 220px;
  max-width: 320px;
}
.filter-input {
  width: 100%;
  padding: 8px 12px 8px 36px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  font-size: 13px;
  color: var(--adm-text-primary);
  background: var(--adm-surface-elevated);
  transition: all 0.15s;
  outline: none;
  height: 36px;
}
.filter-input:focus {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}
.filter-input::placeholder { color: var(--adm-text-muted); }
.filter-input-icon {
  position: absolute;
  left: 10px;
  top: 50%;
  transform: translateY(-50%);
  color: var(--adm-text-muted);
  pointer-events: none;
}
.filter-select {
  padding: 8px 30px 8px 12px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  font-size: 13px;
  color: var(--adm-text-primary);
  background: var(--adm-surface-elevated);
  appearance: none;
  cursor: pointer;
  transition: all 0.15s;
  outline: none;
  height: 36px;
  min-width: 130px;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%2394a3b8' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 10px center;
}
.filter-select.bucket-select { min-width: 170px; }
.filter-select:focus {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}
.filter-spacer { flex: 1; }

/* ===== Bucket tag ===== */
.bucket-tag {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 500;
  line-height: 1.6;
  background: var(--adm-primary-bg);
  color: var(--adm-primary);
  border: 1px solid var(--adm-info-border);
  white-space: nowrap;
}

/* ===== OSS status badges ===== */
.oss-status {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 4px 10px;
  border-radius: 6px;
  font-size: 12px;
  font-weight: 500;
  white-space: nowrap;
}
.oss-status.pending {
  background: var(--adm-warning-bg);
  color: var(--adm-warning);
}
.oss-status.deleted {
  background: var(--adm-error-bg);
  color: var(--adm-error);
}
.oss-status.ignored {
  background: #fef9c3;
  color: #a16207;
  border: 1px solid #fde047;
}

/* ===== Path cell ===== */
.path-cell {
  display: flex;
  align-items: center;
  gap: 10px;
}
.path-info {
  min-width: 0;
  flex: 1;
}
.thumb-mini {
  width: 32px;
  height: 32px;
  border-radius: 4px;
  background: linear-gradient(135deg, #e2e8f0, #cbd5e1);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-muted);
  flex-shrink: 0;
  border: 1px solid var(--adm-border);
  overflow: hidden;
}
.thumb-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.file-path {
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--adm-text-secondary);
  display: flex;
  align-items: center;
  gap: 6px;
}
.file-path-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.file-path.strike {
  color: var(--adm-text-muted);
  text-decoration: line-through;
}
.copy-btn {
  width: 22px;
  height: 22px;
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-muted);
  cursor: pointer;
  transition: all 0.15s;
  background: transparent;
  border: none;
  flex-shrink: 0;
}
.copy-btn:hover {
  background: var(--adm-surface-subtle);
  color: var(--adm-primary);
}
.ignore-reason-inline {
  display: block;
  font-size: 11px;
  color: #a16207;
  margin-top: 2px;
}

/* ===== Data Table ===== */
.table-card { overflow: hidden; }
.table-wrap { overflow-x: auto; }
.adm-table {
  width: 100%;
  border-collapse: collapse;
}
.adm-table thead th {
  background: var(--adm-surface-subtle);
  padding: 12px 16px;
  text-align: left;
  font-size: 12px;
  font-weight: 600;
  color: var(--adm-text-tertiary);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  border-bottom: 1px solid var(--adm-border);
  white-space: nowrap;
}
.adm-table tbody td {
  padding: 12px 16px;
  font-size: 13px;
  color: var(--adm-text-primary);
  border-bottom: 1px solid var(--adm-border-light);
  vertical-align: middle;
}
.adm-table tbody tr { transition: background 0.12s; }
.adm-table tbody tr:hover { background: var(--adm-surface-subtle); }
.adm-table tbody tr.selected-row { background: #eff6ff; }
.adm-table tbody tr.selected-row:hover { background: #dbeafe; }
.adm-table tbody tr:last-child td { border-bottom: none; }
.col-check { width: 44px; }
.col-id {
  width: 70px;
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--adm-text-tertiary);
}
.col-size { width: 90px; }
.col-actions { width: 160px; text-align: right; }
.file-size {
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--adm-text-secondary);
}
.file-size.strike {
  color: var(--adm-text-muted);
  text-decoration: line-through;
}
.time-cell {
  color: var(--adm-text-tertiary);
  font-size: 12px;
  white-space: nowrap;
}
.actions-cell {
  display: flex;
  align-items: center;
  gap: 4px;
  justify-content: flex-end;
}
.text-muted {
  color: var(--adm-text-muted);
  font-size: 13px;
}

/* ===== Checkbox ===== */
.adm-checkbox {
  width: 16px;
  height: 16px;
  border: 1.5px solid var(--adm-border);
  border-radius: 4px;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s;
  background: var(--adm-surface-elevated);
}
.adm-checkbox:hover { border-color: var(--adm-primary); }
.adm-checkbox.checked {
  background: var(--adm-primary);
  border-color: var(--adm-primary);
}

/* ===== Pagination ===== */
.pagination-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 20px;
  border-top: 1px solid var(--adm-border-light);
}
.pagination-info {
  font-size: 13px;
  color: var(--adm-text-tertiary);
}
.pagination-info strong {
  color: var(--adm-text-primary);
}
.pagination-controls {
  display: flex;
  align-items: center;
  gap: 6px;
}
.page-btn {
  min-width: 32px;
  height: 32px;
  padding: 0 8px;
  border-radius: var(--adm-radius-sm);
  font-size: 13px;
  color: var(--adm-text-secondary);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: all 0.15s;
  border: 1px solid transparent;
  background: transparent;
}
.page-btn:hover:not(:disabled):not(.active) {
  background: var(--adm-surface-subtle);
  color: var(--adm-text-primary);
}
.page-btn.active {
  background: var(--adm-primary);
  color: #fff;
  font-weight: 500;
}
.page-btn:disabled {
  color: var(--adm-text-muted);
  cursor: not-allowed;
}
.page-ellipsis {
  color: var(--adm-text-muted);
  padding: 0 4px;
  font-size: 13px;
}

/* ===== Floating Bulk Action Bar ===== */
.bulk-bar {
  position: fixed;
  bottom: 24px;
  left: 50%;
  transform: translateX(calc(-50% + 120px)); /* account for sidebar */
  background: var(--adm-text-primary);
  border-radius: var(--adm-radius-lg);
  padding: 12px 20px;
  display: flex;
  align-items: center;
  gap: 12px;
  box-shadow: 0 12px 32px rgba(15, 23, 42, 0.25);
  z-index: 90;
  animation: bulkBarIn 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
}
@keyframes bulkBarIn {
  from {
    opacity: 0;
    transform: translateX(calc(-50% + 120px)) translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateX(calc(-50% + 120px)) translateY(0);
  }
}
.bulk-bar-info {
  color: #fff;
  font-size: 13px;
  font-weight: 500;
  padding-right: 8px;
  border-right: 1px solid rgba(255, 255, 255, 0.15);
}
.bulk-bar-info strong {
  font-size: 15px;
  font-weight: 700;
  color: #fbbf24;
  margin: 0 4px;
}
.bulk-cancel-btn {
  background: transparent;
  color: #fff;
  font-size: 12px;
  padding: 6px 10px;
  border: none;
  border-radius: var(--adm-radius-sm);
  cursor: pointer;
  transition: background 0.15s;
}
.bulk-cancel-btn:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.1);
}
.bulk-cancel-btn:disabled { opacity: 0.5; cursor: not-allowed; }
.bulk-bar-transition-enter-active,
.bulk-bar-transition-leave-active {
  transition: opacity 0.2s ease, transform 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
}
.bulk-bar-transition-enter-from,
.bulk-bar-transition-leave-to {
  opacity: 0;
  transform: translateX(calc(-50% + 120px)) translateY(20px);
}

/* ===== Modal ===== */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.55);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 200;
  backdrop-filter: blur(3px);
  animation: overlayIn 0.2s ease;
}
@keyframes overlayIn {
  from { opacity: 0; }
  to { opacity: 1; }
}
.modal {
  background: var(--adm-surface-elevated);
  border-radius: var(--adm-radius-lg);
  width: 500px;
  max-width: 92vw;
  box-shadow: 0 24px 60px rgba(15, 23, 42, 0.25);
  animation: modalIn 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
  overflow: hidden;
}
@keyframes modalIn {
  from {
    opacity: 0;
    transform: translateY(20px) scale(0.97);
  }
  to {
    opacity: 1;
    transform: translateY(0) scale(1);
  }
}
.modal-header {
  padding: 18px 24px;
  border-bottom: 1px solid var(--adm-border);
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.modal-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--adm-text-primary);
  display: flex;
  align-items: center;
  gap: 8px;
}
.modal-close {
  width: 30px;
  height: 30px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-tertiary);
  cursor: pointer;
  transition: all 0.15s;
  background: transparent;
  border: none;
}
.modal-close:hover {
  background: var(--adm-surface-subtle);
  color: var(--adm-text-primary);
}
.modal-body {
  padding: 20px 24px;
}
.modal-footer {
  padding: 14px 24px;
  border-top: 1px solid var(--adm-border);
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  background: var(--adm-surface-elevated);
}

/* ===== Modal form elements ===== */
.file-path-display {
  background: var(--adm-surface-subtle);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-sm);
  padding: 10px 12px;
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--adm-text-secondary);
  word-break: break-all;
  line-height: 1.5;
  margin-bottom: 16px;
}
.form-group { margin-bottom: 14px; }
.form-label {
  display: block;
  font-size: 12px;
  font-weight: 500;
  color: var(--adm-text-secondary);
  margin-bottom: 6px;
}
.form-textarea {
  width: 100%;
  padding: 9px 12px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-sm);
  font-size: 13px;
  color: var(--adm-text-primary);
  background: var(--adm-surface-elevated);
  transition: all 0.15s;
  outline: none;
  font-family: inherit;
  resize: vertical;
  min-height: 80px;
  line-height: 1.6;
}
.form-textarea:focus {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}
.form-helper {
  font-size: 11px;
  color: var(--adm-text-muted);
  margin-top: 6px;
  line-height: 1.5;
}

.modal-transition-enter-active,
.modal-transition-leave-active {
  transition: opacity 0.2s ease;
}
.modal-transition-enter-from,
.modal-transition-leave-to {
  opacity: 0;
}

/* ===== Loading row ===== */
.loading-row {
  padding: 60px 24px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  color: var(--adm-text-tertiary);
  font-size: 14px;
}
.spinner-lg {
  width: 24px;
  height: 24px;
  border: 2.5px solid var(--adm-border);
  border-top-color: var(--adm-primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
</style>
