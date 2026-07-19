<template>
  <div class="adm-fade-in mistake-view">
    <!-- Page Header -->
    <div class="adm-page-header">
      <div>
        <h1 class="adm-page-title">错题管理</h1>
        <p class="adm-page-subtitle">管理所有已确认的错题记录，支持审核、编辑和图片迁移</p>
      </div>
      <div class="adm-page-actions">
        <button class="adm-btn adm-btn-secondary" @click="loadAll">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="23 4 23 10 17 10"/><polyline points="1 20 1 14 7 14"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/></svg>
          刷新
        </button>
      </div>
    </div>

    <!-- Stats Row - 4 cards -->
    <div class="stats-row">
      <div
        v-for="s in statCards"
        :key="s.key"
        class="stat-card"
        :class="[s.cssClass, { active: filterReviewStatus === s.value }]"
        @click="setFilterStatus(s.value)"
      >
        <div>
          <div class="st-label">{{ s.label }}</div>
          <div class="st-value" :style="statsLoading ? '' : `color:${s.color}`">{{ statsLoading ? '—' : stats[s.key] }}</div>
        </div>
        <div class="st-icon">
          <span v-if="statsLoading" class="spinner-sm"></span>
          <svg v-else width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" v-html="s.icon"></svg>
        </div>
      </div>
    </div>

    <!-- Filter Bar -->
    <div class="adm-filter-bar">
      <div class="filter-input-wrap">
        <svg class="filter-input-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
        <input
          v-model="filterStudentName"
          class="filter-input"
          placeholder="搜索学生姓名..."
          @input="searchStudentsForFilter"
        />
        <div v-if="filterStudentOptions.length > 0" class="user-dropdown">
          <div
            v-for="s in filterStudentOptions"
            :key="s.id"
            class="search-option"
            @click="selectFilterStudent(s)"
          >
            <div class="mini-avatar" :style="{ background: getAvatarGradient(s.name || s.id) }">{{ getAvatarChar(s.name || '?') }}</div>
            <div class="opt-info">
              <div class="opt-name">{{ s.name }}</div>
              <div class="opt-meta">ID: {{ s.id }} · {{ getGradeLabel(s.grade) }}</div>
            </div>
          </div>
        </div>
      </div>
      <select v-model="filterSubject" class="filter-select" @change="onFilterChange">
        <option :value="undefined">全部学科</option>
        <option v-for="s in subjectOptions" :key="s.value" :value="s.value">{{ s.label }}</option>
      </select>
      <select v-model="filterGrade" class="filter-select" @change="onFilterChange">
        <option :value="undefined">全部年级</option>
        <option v-for="g in gradeOptions" :key="g.value" :value="g.value">{{ g.label }}</option>
      </select>
      <select v-model="filterReviewStatus" class="filter-select" style="min-width:130px;" @change="onFilterChange">
        <option :value="undefined">审核状态：全部</option>
        <option v-for="s in reviewStatusOptions" :key="s.value" :value="s.value">{{ s.label }}</option>
      </select>
      <div v-if="filterStudentName" class="filter-tag">
        {{ filterStudentName }}
        <button class="filter-tag-x" @click="clearFilterStudent">×</button>
      </div>
      <div class="filter-spacer"></div>
      <button class="adm-btn adm-btn-primary adm-btn-sm" @click="onSearch">
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
        搜索
      </button>
      <button class="adm-btn adm-btn-secondary adm-btn-sm" @click="resetFilters">
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/></svg>
        重置
      </button>
    </div>

    <!-- Data Table -->
    <div class="adm-card table-card">
      <div v-if="loading" class="loading-row">
        <span class="spinner-lg"></span>
        <span>加载中...</span>
      </div>
      <div v-else-if="mistakes.length === 0" class="adm-empty">
        <div class="adm-empty-icon">
          <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
        </div>
        <div class="adm-empty-text">暂无错题记录</div>
      </div>
      <div v-else class="table-wrap">
        <table class="adm-table">
          <thead>
            <tr>
              <th class="col-thumb">首图</th>
              <th>学生</th>
              <th>学科</th>
              <th>年级</th>
              <th>审核状态</th>
              <th>图片数</th>
              <th>创建时间</th>
              <th class="col-actions">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="m in mistakes"
              :key="m.id"
              :class="{ 'pending-row': m.reviewStatus === 1 }"
            >
              <td>
                <div class="thumb">
                  <img
                    v-if="m.firstImagePath"
                    :src="getMistakeImageUrl(m.firstImagePath, 'small')"
                    :alt="m.studentName"
                    class="thumb-img"
                    @error="onThumbError"
                  />
                  <svg v-else width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>
                </div>
              </td>
              <td>
                <div class="student-cell">
                  <div class="stu-avatar-sm" :style="{ background: getAvatarGradient(m.studentName || m.studentId) }">{{ getAvatarChar(m.studentName || '?') }}</div>
                  <div>
                    <div class="student-name">{{ m.studentName || '未知' }}</div>
                    <div class="student-grade">{{ getGradeLabel(m.grade) }}</div>
                  </div>
                </div>
              </td>
              <td>
                <span class="subj-tag" :class="getSubjectCssClass(m.subject)">{{ getSubjectLabel(m.subject) }}</span>
              </td>
              <td>{{ getGradeLabel(m.grade) }}</td>
              <td>
                <span class="audit-badge" :class="getReviewStatusCssClass(m.reviewStatus)">
                  <svg v-if="m.reviewStatus === 1" class="spin-icon" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12a9 9 0 1 1-6.219-8.56"/></svg>
                  <span v-else class="dot"></span>
                  {{ getReviewStatusLabel(m.reviewStatus) }}
                </span>
              </td>
              <td><span class="img-count">{{ m.imageCount || 0 }} 张</span></td>
              <td><span class="time-cell">{{ formatDateTime(m.createdAt) }}</span></td>
              <td>
                <div class="actions-cell">
                  <button class="adm-btn-link" @click="showDetailDialog(m)">详情</button>
                  <button class="adm-btn-link" @click="openEditInline(m)">编辑</button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
        <!-- Pagination -->
        <div class="pagination-bar">
          <div class="pagination-info">共 <strong style="color:var(--adm-text-primary);">{{ total }}</strong> 条记录</div>
          <div class="pagination-controls">
            <button class="page-btn" :disabled="page <= 1" @click="onPageChange(page - 1)">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="15 18 9 12 15 6"/></svg>
            </button>
            <button
              v-for="p in pageButtons"
              :key="p"
              class="page-btn"
              :class="{ active: p === page, ellipsis: p === -1 }"
              :disabled="p === -1"
              @click="p > 0 && onPageChange(p)"
            >
              <span v-if="p === -1">...</span>
              <span v-else>{{ p }}</span>
            </button>
            <button class="page-btn" :disabled="page >= totalPages" @click="onPageChange(page + 1)">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Detail Modal (700px) -->
    <div v-if="showDetailDialogVisible" class="modal-mask" @click.self="closeDetailDialog">
      <div class="modal-box" style="width: 700px;">
        <div class="modal-header">
          <div class="modal-title">
            错题详情
            <span class="rec-id">{{ currentMistake?.id?.slice(0, 12) || '-' }}</span>
          </div>
          <button class="modal-close" @click="closeDetailDialog">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
          </button>
        </div>
        <div class="modal-body">
          <div v-if="detailLoading" class="loading-row">
            <span class="spinner-lg"></span>
            <span>加载详情...</span>
          </div>
          <template v-else-if="currentMistake">
            <!-- Info grid 2 columns -->
            <div class="info-grid">
              <div class="info-cell">
                <span class="info-label">学生</span>
                <span class="info-value">
                  <div class="stu-avatar-sm" :style="{ background: getAvatarGradient(currentMistake.studentName || currentMistake.studentId) }">{{ getAvatarChar(currentMistake.studentName || '?') }}</div>
                  {{ currentMistake.studentName || '未知' }}
                </span>
              </div>
              <div class="info-cell">
                <span class="info-label">学科</span>
                <span class="info-value">
                  <span class="subj-tag" :class="getSubjectCssClass(currentMistake.subject)">{{ getSubjectLabel(currentMistake.subject) }}</span>
                </span>
              </div>
              <div class="info-cell">
                <span class="info-label">年级</span>
                <span class="info-value">{{ getGradeLabel(currentMistake.grade) }}</span>
              </div>
              <div class="info-cell">
                <span class="info-label">状态</span>
                <span class="info-value">
                  <span class="audit-badge" :class="getReviewStatusCssClass(currentMistake.reviewStatus)" style="padding:2px 8px;font-size:11px;">
                    <span class="dot"></span>
                    {{ getReviewStatusLabel(currentMistake.reviewStatus) }}
                  </span>
                </span>
              </div>
              <div class="info-cell">
                <span class="info-label">创建时间</span>
                <span class="info-value" style="font-weight:400;color:var(--adm-text-secondary);">{{ formatDateTime(currentMistake.createdAt) }}</span>
              </div>
              <div class="info-cell">
                <span class="info-label">更新时间</span>
                <span class="info-value" style="font-weight:400;color:var(--adm-text-secondary);">{{ formatDateTime(currentMistake.updatedAt) }}</span>
              </div>
              <div class="info-cell">
                <span class="info-label">来源上传</span>
                <span class="info-value mono-id" :title="currentMistake.sourceUploadId || ''">{{ currentMistake.sourceUploadId ? currentMistake.sourceUploadId.slice(0, 12) + '...' : '-' }}</span>
              </div>
              <div class="info-cell">
                <span class="info-label">问题ID</span>
                <span class="info-value mono-id" :title="currentMistake.questionId || ''">{{ currentMistake.questionId ? currentMistake.questionId.slice(0, 12) + '...' : '-' }}</span>
              </div>
            </div>

            <!-- Audit info / AI-style analysis card -->
            <div v-if="currentMistake.reviewComment || currentMistake.type || currentMistake.reviewerId" class="ai-card">
              <div class="ai-card-header">
                <div class="ai-icon">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18h6M10 22h4M12 2a7 7 0 0 0-4 12.7V17a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1v-2.3A7 7 0 0 0 12 2z"/></svg>
                </div>
                <span class="ai-title">审核信息</span>
                <span class="ai-tag">INFO</span>
              </div>
              <div v-if="currentMistake.type" class="ai-field">
                <div class="ai-field-label">错因类型</div>
                <div class="ai-field-value">
                  <span class="err-type-tag">{{ getTypeLabel(currentMistake.type) }}</span>
                </div>
              </div>
              <div v-if="currentMistake.reviewComment" class="ai-field">
                <div class="ai-field-label">审核评论</div>
                <div class="ai-field-value">{{ currentMistake.reviewComment }}</div>
              </div>
              <div v-if="currentMistake.reviewerId" class="ai-field">
                <div class="ai-field-label">审核人</div>
                <div class="ai-field-value mono-id">{{ currentMistake.reviewerId }}</div>
              </div>
              <div v-if="currentMistake.reviewedAt" class="ai-field">
                <div class="ai-field-label">审核时间</div>
                <div class="ai-field-value" style="color:var(--adm-text-secondary);">{{ formatDateTime(currentMistake.reviewedAt) }}</div>
              </div>
            </div>

            <!-- Image gallery -->
            <div class="section-title">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>
              题目图片
              <span class="section-count">共 {{ currentMistake.sourceRegions?.length || currentMistake.imageCount || 0 }} 张</span>
            </div>
            <div v-if="hasUploadPrefixImages" class="migration-banner">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
              <span>检测到部分图片仍在 uploads/ 路径下，建议迁移到 mistakes/ 路径</span>
              <button class="adm-btn adm-btn-warning adm-btn-sm" :disabled="migrating" @click="migrateImages">
                {{ migrating ? '迁移中...' : '迁移图片' }}
              </button>
            </div>
            <div v-if="currentMistake.sourceRegions && currentMistake.sourceRegions.length > 0" class="img-gallery">
              <div
                v-for="(region, idx) in currentMistake.sourceRegions"
                :key="idx"
                class="img-gallery-item"
                :class="{ selected: selectedImageIndex === idx }"
                @click="selectedImageIndex = idx"
              >
                <img
                  :src="getMistakeImageUrl(region.sourceImagePath, 'medium')"
                  :alt="`图片${idx + 1}`"
                  class="gallery-img"
                  @error="onGalleryError"
                />
                <span class="g-num">#{{ idx + 1 }}</span>
              </div>
            </div>
            <div v-else class="no-images">暂无图片数据</div>

            <!-- Large preview of selected image -->
            <div v-if="currentMistake.sourceRegions && currentMistake.sourceRegions.length > 0" class="img-preview-lg">
              <img
                :src="getMistakeImageUrl(currentMistake.sourceRegions[selectedImageIndex]?.sourceImagePath)"
                :alt="`预览${selectedImageIndex + 1}`"
                @error="onPreviewError"
              />
              <div class="img-path-row">
                <span class="image-path-text" :title="currentMistake.sourceRegions[selectedImageIndex]?.sourceImagePath">
                  {{ currentMistake.sourceRegions[selectedImageIndex]?.sourceImagePath }}
                </span>
                <button class="adm-btn-link" @click="copyPath(currentMistake.sourceRegions[selectedImageIndex]?.sourceImagePath)">
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>
                  复制路径
                </button>
              </div>
            </div>

            <!-- Edit form -->
            <div class="edit-form-section">
              <div class="edit-form-title">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                编辑错题信息
              </div>
              <div class="form-row-2">
                <div class="form-group">
                  <label class="form-label">学科<span class="required">*</span></label>
                  <select v-model="editForm.subject" class="form-select">
                    <option :value="0" disabled>请选择学科</option>
                    <option v-for="s in subjectOptions" :key="s.value" :value="s.value">{{ s.label }}</option>
                  </select>
                </div>
                <div class="form-group">
                  <label class="form-label">年级<span class="required">*</span></label>
                  <select v-model="editForm.grade" class="form-select">
                    <option :value="0" disabled>请选择年级</option>
                    <option v-for="g in gradeOptions" :key="g.value" :value="g.value">{{ g.label }}</option>
                  </select>
                </div>
              </div>
              <div class="form-group">
                <label class="form-label">学生</label>
                <div class="form-input-wrap">
                  <input
                    v-model="studentSearchQuery"
                    class="form-input"
                    placeholder="搜索学生姓名或ID..."
                    @input="searchStudentsForEdit"
                  />
                  <div v-if="editStudentOptions.length > 0" class="user-dropdown">
                    <div
                      v-for="s in editStudentOptions"
                      :key="s.id"
                      class="search-option"
                      @click="selectEditStudent(s)"
                    >
                      <div class="mini-avatar" :style="{ background: getAvatarGradient(s.name || s.id) }">{{ getAvatarChar(s.name || '?') }}</div>
                      <div class="opt-info">
                        <div class="opt-name">{{ s.name }}</div>
                        <div class="opt-meta">ID: {{ s.id }} · {{ getGradeLabel(s.grade) }}</div>
                      </div>
                    </div>
                  </div>
                </div>
                <div v-if="editForm.studentId && editSelectedStudentName" class="selected-user-banner">
                  <div class="mini-avatar" :style="{ background: getAvatarGradient(editSelectedStudentName) }">{{ getAvatarChar(editSelectedStudentName) }}</div>
                  <div class="sel-info">
                    <div class="sel-name">{{ editSelectedStudentName }}</div>
                    <div class="sel-meta">ID: {{ editForm.studentId }}</div>
                  </div>
                  <button class="sel-clear" @click="clearEditStudent" title="清除选择">×</button>
                </div>
              </div>
            </div>
          </template>
        </div>
        <div class="modal-footer">
          <button class="adm-btn adm-btn-secondary" @click="closeDetailDialog">关闭</button>
          <button class="adm-btn adm-btn-primary" :disabled="saving || detailLoading" @click="saveEdit">
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
            {{ saving ? '保存中...' : '保存修改' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage } from 'element-plus'
import studentAdminApi from '../services/studentAdminApi'
import type { MistakeItemDto, StudentDto } from '../services/studentAdminApi'
import { getSubjectLabel, getSubjectCssClass, getAvatarGradient, getAvatarChar, formatDateTime } from '../utils/subject'

interface MistakeItem extends MistakeItemDto {}

const mistakes = ref<MistakeItem[]>([])
const loading = ref(false)
const total = ref(0)
const page = ref(1)
const pageSize = 20
const totalPages = ref(0)

// Filters
const filterStudentId = ref<string | undefined>(undefined)
const filterStudentName = ref('')
const filterStudentOptions = ref<StudentDto[]>([])
const filterSubject = ref<number | undefined>(undefined)
const filterGrade = ref<number | undefined>(undefined)
const filterReviewStatus = ref<number | undefined>(undefined)

// Stats
const statsLoading = ref(false)
const stats = ref({ total: 0, pending: 0, confirmed: 0, rejected: 0 })

const statCards = computed(() => [
  {
    key: 'total' as const,
    label: '总错题',
    value: undefined,
    cssClass: 'total',
    color: 'var(--adm-primary)',
    icon: '<path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"/>'
  },
  {
    key: 'pending' as const,
    label: '待审核',
    value: 1,
    cssClass: 'pending',
    color: 'var(--adm-warning)',
    icon: '<circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>'
  },
  {
    key: 'confirmed' as const,
    label: '已确认',
    value: 2,
    cssClass: 'confirmed',
    color: 'var(--adm-success)',
    icon: '<polyline points="20 6 9 17 4 12"/>'
  },
  {
    key: 'rejected' as const,
    label: '已退回',
    value: 3,
    cssClass: 'rejected',
    color: 'var(--adm-error)',
    icon: '<line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>'
  }
])

// Options
const subjectOptions = ref<{ value: number; label: string }[]>([])
const gradeOptions = ref<{ value: number; label: string }[]>([])
const reviewStatusOptions = ref<{ value: number; label: string }[]>([
  { value: 1, label: '待审核' },
  { value: 2, label: '已确认' },
  { value: 3, label: '已退回' }
])
const classificationOptions = ref<{ value: number; label: string }[]>([])

// Detail Modal
const showDetailDialogVisible = ref(false)
const currentMistake = ref<MistakeItem | null>(null)
const detailLoading = ref(false)
const selectedImageIndex = ref(0)
const migrating = ref(false)
const saving = ref(false)

const editForm = ref({
  id: '',
  studentId: '',
  subject: 0,
  grade: 0
})

const studentSearchQuery = ref('')
const editStudentOptions = ref<StudentDto[]>([])
const editSelectedStudentName = ref('')
let editSearchTimer: number | null = null
let filterSearchTimer: number | null = null

const hasUploadPrefixImages = computed(() => {
  if (!currentMistake.value?.sourceRegions) return false
  return currentMistake.value.sourceRegions.some(
    r => r.sourceImagePath && r.sourceImagePath.startsWith('uploads/')
  )
})

const pageButtons = computed(() => {
  const buttons: number[] = []
  const tp = totalPages.value
  const cur = page.value
  if (tp <= 7) {
    for (let i = 1; i <= tp; i++) buttons.push(i)
  } else {
    buttons.push(1)
    if (cur > 3) buttons.push(-1)
    const start = Math.max(2, cur - 1)
    const end = Math.min(tp - 1, cur + 1)
    for (let i = start; i <= end; i++) buttons.push(i)
    if (cur < tp - 2) buttons.push(-1)
    buttons.push(tp)
  }
  return buttons
})

// ===== Data loading =====
async function loadEnumOptions() {
  try {
    const enums = await studentAdminApi.getEnumOptions()
    if (enums.subjects) {
      subjectOptions.value = enums.subjects.map((s: any) => ({ value: s.value, label: s.label || s.displayName }))
    }
    if (enums.grades) {
      gradeOptions.value = enums.grades.map((g: any) => ({ value: g.value, label: g.label || g.displayName }))
    }
    if (enums.reviewStatuses && enums.reviewStatuses.length > 0) {
      reviewStatusOptions.value = enums.reviewStatuses.map((s: any) => ({ value: s.value, label: s.label || s.displayName }))
    }
    if (enums.classifications) {
      classificationOptions.value = enums.classifications.map((c: any) => ({ value: c.value, label: c.label || c.displayName }))
    }
  } catch (error) {
    console.error('Failed to load enum options:', error)
  }
}

async function loadMistakes() {
  loading.value = true
  try {
    const result = await studentAdminApi.getMistakeItems({
      page: page.value,
      size: pageSize,
      studentId: filterStudentId.value || undefined,
      subject: filterSubject.value,
      grade: filterGrade.value,
      reviewStatus: filterReviewStatus.value
    })
    mistakes.value = result.items
    total.value = result.total
    totalPages.value = result.totalPages || Math.ceil(result.total / pageSize)
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '加载错题列表失败')
  } finally {
    loading.value = false
  }
}

async function loadStats() {
  statsLoading.value = true
  try {
    const [all, p1, p2, p3] = await Promise.all([
      studentAdminApi.getMistakeItems({ page: 1, size: 1 }),
      studentAdminApi.getMistakeItems({ page: 1, size: 1, reviewStatus: 1 }),
      studentAdminApi.getMistakeItems({ page: 1, size: 1, reviewStatus: 2 }),
      studentAdminApi.getMistakeItems({ page: 1, size: 1, reviewStatus: 3 })
    ])
    stats.value = {
      total: all.total,
      pending: p1.total,
      confirmed: p2.total,
      rejected: p3.total
    }
  } catch (error) {
    console.error('Failed to load stats:', error)
  } finally {
    statsLoading.value = false
  }
}

async function loadAll() {
  await Promise.all([loadMistakes(), loadStats()])
}

// ===== Helpers =====
function getGradeLabel(grade: number) {
  return gradeOptions.value.find(g => g.value === grade)?.label || (grade ? `年级${grade}` : '-')
}

function getReviewStatusLabel(status: number) {
  return reviewStatusOptions.value.find(s => s.value === status)?.label || `状态${status}`
}

function getReviewStatusCssClass(status: number) {
  const map: Record<number, string> = { 1: 'pending-audit', 2: 'confirmed', 3: 'returned' }
  return map[status] || 'pending-audit'
}

function getTypeLabel(type: number) {
  return classificationOptions.value.find(c => c.value === type)?.label || `类型${type}`
}

function getMistakeImageUrl(path: string, size?: 'small' | 'medium') {
  if (!path) return ''
  if (path.startsWith('http://') || path.startsWith('https://')) return path
  const params = new URLSearchParams({ path })
  if (size) params.set('size', size)
  return `/api/admin/image?${params.toString()}`
}

function onThumbError(e: Event) {
  const img = e.target as HTMLImageElement
  img.style.display = 'none'
}

function onGalleryError(e: Event) {
  const img = e.target as HTMLImageElement
  img.style.display = 'none'
}

function onPreviewError(e: Event) {
  const img = e.target as HTMLImageElement
  img.style.opacity = '0.3'
}

function copyPath(path: string | undefined) {
  if (!path) return
  navigator.clipboard.writeText(path).then(() => {
    ElMessage.success('已复制路径')
  }).catch(() => {
    ElMessage.error('复制失败')
  })
}

// ===== Filters =====
function setFilterStatus(value: number | undefined) {
  filterReviewStatus.value = filterReviewStatus.value === value ? undefined : value
  page.value = 1
  loadMistakes()
}

function onFilterChange() {
  page.value = 1
  loadMistakes()
}

function onSearch() {
  page.value = 1
  loadMistakes()
}

function resetFilters() {
  filterReviewStatus.value = undefined
  filterSubject.value = undefined
  filterGrade.value = undefined
  filterStudentId.value = undefined
  filterStudentName.value = ''
  filterStudentOptions.value = []
  page.value = 1
  loadMistakes()
}

function searchStudentsForFilter() {
  if (filterSearchTimer) clearTimeout(filterSearchTimer)
  const query = filterStudentName.value.trim()
  if (!query) {
    filterStudentOptions.value = []
    return
  }
  filterSearchTimer = window.setTimeout(async () => {
    try {
      const result = await studentAdminApi.getStudents({ name: query, pageSize: 20 })
      filterStudentOptions.value = result.items
    } catch (error) {
      console.error('Search students failed:', error)
    }
  }, 250)
}

function selectFilterStudent(s: StudentDto) {
  filterStudentId.value = s.id
  filterStudentName.value = s.name
  filterStudentOptions.value = []
  page.value = 1
  loadMistakes()
}

function clearFilterStudent() {
  filterStudentId.value = undefined
  filterStudentName.value = ''
  filterStudentOptions.value = []
  page.value = 1
  loadMistakes()
}

function onPageChange(p: number) {
  if (p < 1 || p > totalPages.value) return
  page.value = p
  loadMistakes()
}

// ===== Detail Modal =====
async function showDetailDialog(mistake: MistakeItem) {
  showDetailDialogVisible.value = true
  currentMistake.value = null
  detailLoading.value = true
  selectedImageIndex.value = 0
  try {
    const detail = await studentAdminApi.getMistakeItem(mistake.id)
    currentMistake.value = detail
    editForm.value = {
      id: detail.id,
      studentId: detail.studentId,
      subject: detail.subject,
      grade: detail.grade
    }
    editSelectedStudentName.value = detail.studentName || ''
    studentSearchQuery.value = ''
    editStudentOptions.value = []
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '加载详情失败')
    showDetailDialogVisible.value = false
  } finally {
    detailLoading.value = false
  }
}

function openEditInline(mistake: MistakeItem) {
  showDetailDialog(mistake)
}

function closeDetailDialog() {
  showDetailDialogVisible.value = false
  currentMistake.value = null
}

function searchStudentsForEdit() {
  if (editSearchTimer) clearTimeout(editSearchTimer)
  const query = studentSearchQuery.value.trim()
  if (!query) {
    editStudentOptions.value = []
    return
  }
  editSearchTimer = window.setTimeout(async () => {
    try {
      const result = await studentAdminApi.getStudents({ name: query, pageSize: 20 })
      editStudentOptions.value = result.items
    } catch (error) {
      console.error('Search students failed:', error)
    }
  }, 250)
}

function selectEditStudent(s: StudentDto) {
  editForm.value.studentId = s.id
  editSelectedStudentName.value = s.name
  studentSearchQuery.value = ''
  editStudentOptions.value = []
}

function clearEditStudent() {
  editForm.value.studentId = ''
  editSelectedStudentName.value = ''
}

async function saveEdit() {
  if (!currentMistake.value) return
  if (!editForm.value.subject || editForm.value.subject <= 0) {
    ElMessage.warning('请选择学科')
    return
  }
  if (!editForm.value.grade || editForm.value.grade <= 0) {
    ElMessage.warning('请选择年级')
    return
  }
  if (!editForm.value.studentId) {
    ElMessage.warning('请选择学生')
    return
  }
  saving.value = true
  try {
    await studentAdminApi.updateMistakeItem(editForm.value.id, {
      studentId: editForm.value.studentId,
      subject: editForm.value.subject,
      grade: editForm.value.grade
    })
    ElMessage.success('更新成功')
    await loadMistakes()
    await loadStats()
    // Refresh the detail view
    try {
      const detail = await studentAdminApi.getMistakeItem(editForm.value.id)
      currentMistake.value = detail
    } catch (e) {
      // ignore refresh errors
    }
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '更新失败')
  } finally {
    saving.value = false
  }
}

async function migrateImages() {
  if (!currentMistake.value) return
  migrating.value = true
  try {
    const result = await studentAdminApi.migrateMistakeImages(currentMistake.value.id)
    if (result.success) {
      ElMessage.success(result.message || `迁移成功，${result.migratedCount} 张图片`)
      // Reload detail to reflect new paths
      const detail = await studentAdminApi.getMistakeItem(currentMistake.value.id)
      currentMistake.value = detail
      await loadMistakes()
    } else {
      ElMessage.error(result.message || '迁移失败')
    }
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '迁移图片失败')
  } finally {
    migrating.value = false
  }
}

// ===== Init =====
onMounted(async () => {
  await loadEnumOptions()
  await loadAll()
})
</script>

<style scoped>
.mistake-view {
  width: 100%;
}

/* Stats row - 4 cards */
.stats-row {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
  margin-bottom: 16px;
}
.stat-card {
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  padding: 16px 20px;
  display: flex;
  align-items: center;
  gap: 14px;
  transition: all 0.2s;
  cursor: pointer;
  position: relative;
  overflow: hidden;
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
.stat-card.active {
  border-color: var(--adm-primary);
  background: var(--adm-primary-bg);
}
.stat-card .st-label {
  font-size: 12px;
  color: var(--adm-text-tertiary);
  margin-bottom: 4px;
}
.stat-card .st-value {
  font-size: 24px;
  font-weight: 700;
  color: var(--adm-text-primary);
  line-height: 1;
}
.stat-card .st-icon {
  width: 40px;
  height: 40px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-left: auto;
  flex-shrink: 0;
}
.stat-card.total .st-icon { background: var(--adm-primary-bg); color: var(--adm-primary); }
.stat-card.pending .st-icon { background: var(--adm-warning-bg); color: var(--adm-warning); }
.stat-card.confirmed .st-icon { background: var(--adm-success-bg); color: var(--adm-success); }
.stat-card.rejected .st-icon { background: var(--adm-error-bg); color: var(--adm-error); }

.spinner-sm {
  width: 18px;
  height: 18px;
  border: 2px solid var(--adm-border);
  border-top-color: var(--adm-primary);
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
  display: inline-block;
}
@keyframes spin { to { transform: rotate(360deg); } }

/* Filter bar inputs (shared styles) */
.filter-input-wrap {
  position: relative;
  flex: 0 0 240px;
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
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%2394a3b8' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 10px center;
}
.filter-select:focus {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}
.filter-spacer { flex: 1; }
.filter-tag {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  border-radius: var(--adm-radius-sm);
  background: var(--adm-primary-bg);
  color: var(--adm-primary);
  font-size: 12px;
  font-weight: 500;
  border: 1px solid var(--adm-info-border);
}
.filter-tag-x {
  background: transparent;
  border: none;
  color: var(--adm-primary);
  cursor: pointer;
  font-size: 16px;
  line-height: 1;
  padding: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 16px;
  height: 16px;
  border-radius: 50%;
}
.filter-tag-x:hover { background: rgba(59, 130, 246, 0.2); }

/* User dropdown (search results) */
.user-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  box-shadow: var(--adm-shadow-md);
  max-height: 280px;
  overflow-y: auto;
  z-index: 50;
}
.search-option {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  cursor: pointer;
  transition: background 0.12s;
}
.search-option:hover { background: var(--adm-surface-subtle); }
.mini-avatar {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  font-size: 12px;
  font-weight: 600;
  flex-shrink: 0;
}
.opt-info { flex: 1; min-width: 0; }
.opt-name { font-size: 13px; font-weight: 500; color: var(--adm-text-primary); }
.opt-meta { font-size: 11px; color: var(--adm-text-muted); margin-top: 2px; }

/* Table card */
.table-card { overflow: hidden; }
.loading-row {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  padding: 48px 24px;
  color: var(--adm-text-tertiary);
  font-size: 13px;
}
.spinner-lg {
  width: 28px;
  height: 28px;
  border: 3px solid var(--adm-border);
  border-top-color: var(--adm-primary);
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
}
.table-wrap { overflow-x: auto; }

.adm-table { width: 100%; border-collapse: collapse; }
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
  padding: 14px 16px;
  font-size: 13px;
  color: var(--adm-text-primary);
  border-bottom: 1px solid var(--adm-border-light);
  vertical-align: middle;
}
.adm-table tbody tr { transition: background 0.12s; }
.adm-table tbody tr:hover { background: var(--adm-surface-subtle); }
.adm-table tbody tr:last-child td { border-bottom: none; }
.adm-table tbody tr.pending-row { background: #fffbeb; }
.adm-table tbody tr.pending-row:hover { background: #fef3c7; }
.adm-table .col-thumb { width: 70px; }
.adm-table .col-actions { width: 140px; text-align: right; }
.adm-table .student-cell {
  display: flex;
  align-items: center;
  gap: 10px;
}
.adm-table .student-name { font-weight: 500; }
.adm-table .student-grade { font-size: 11px; color: var(--adm-text-muted); margin-top: 2px; }
.adm-table .img-count { color: var(--adm-text-secondary); font-weight: 500; }
.adm-table .time-cell { color: var(--adm-text-tertiary); font-size: 12px; white-space: nowrap; }
.actions-cell {
  display: flex;
  align-items: center;
  gap: 2px;
  justify-content: flex-end;
}

/* Thumb */
.thumb {
  width: 50px;
  height: 50px;
  border-radius: 8px;
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
.stu-avatar-sm {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  font-weight: 600;
  color: #fff;
  flex-shrink: 0;
}

/* Subject tag */
.subj-tag {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 500;
  border: 1px solid;
  line-height: 1.6;
  white-space: nowrap;
}
.subj-tag.math { background: #eff6ff; color: #2563eb; border-color: #bfdbfe; }
.subj-tag.chinese { background: #fff7ed; color: #ea580c; border-color: #fed7aa; }
.subj-tag.english { background: #f0fdf4; color: #16a34a; border-color: #bbf7d0; }
.subj-tag.physics { background: #faf5ff; color: #7c3aed; border-color: #ddd6fe; }
.subj-tag.chemistry { background: #fdf4ff; color: #a21caf; border-color: #f0abfc; }
.subj-tag.biology { background: #f0fdf4; color: #15803d; border-color: #86efac; }
.subj-tag.history { background: #fef2f2; color: #b91c1c; border-color: #fecaca; }
.subj-tag.geography { background: #eff6ff; color: #1d4ed8; border-color: #bfdbfe; }
.subj-tag.politics { background: #faf5ff; color: #7c3aed; border-color: #ddd6fe; }

/* Audit status badges */
.audit-badge {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 4px 10px;
  border-radius: 6px;
  font-size: 12px;
  font-weight: 500;
  white-space: nowrap;
}
.audit-badge .dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
  flex-shrink: 0;
}
.audit-badge.pending-audit { background: var(--adm-warning-bg); color: var(--adm-warning); }
.audit-badge.confirmed { background: var(--adm-success-bg); color: var(--adm-success); }
.audit-badge.returned { background: var(--adm-error-bg); color: var(--adm-error); }
.spin-icon { animation: spin 1s linear infinite; }

/* Mono ID */
.mono-id {
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  font-size: 12px;
  color: var(--adm-text-secondary);
}

/* Pagination */
.pagination-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 20px;
  border-top: 1px solid var(--adm-border-light);
}
.pagination-info { font-size: 13px; color: var(--adm-text-tertiary); }
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
.page-btn.ellipsis {
  cursor: default;
  background: transparent;
}

/* ===== Modal ===== */
.modal-mask {
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
@keyframes overlayIn { from { opacity: 0; } to { opacity: 1; } }
.modal-box {
  background: var(--adm-surface-elevated);
  border-radius: var(--adm-radius-lg);
  max-width: 92vw;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  box-shadow: 0 24px 60px rgba(15, 23, 42, 0.25);
  animation: modalIn 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
  overflow: hidden;
}
@keyframes modalIn {
  from { opacity: 0; transform: translateY(20px) scale(0.97); }
  to { opacity: 1; transform: translateY(0) scale(1); }
}
.modal-header {
  padding: 18px 24px;
  border-bottom: 1px solid var(--adm-border);
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-shrink: 0;
}
.modal-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--adm-text-primary);
  display: flex;
  align-items: center;
  gap: 10px;
}
.modal-title .rec-id {
  font-size: 11px;
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  color: var(--adm-text-muted);
  font-weight: 400;
  background: var(--adm-surface-subtle);
  padding: 2px 8px;
  border-radius: 4px;
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
  overflow-y: auto;
  flex: 1;
}
.modal-footer {
  padding: 14px 24px;
  border-top: 1px solid var(--adm-border);
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  flex-shrink: 0;
  background: var(--adm-surface-elevated);
}

/* Info grid 2-col */
.info-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  overflow: hidden;
  margin-bottom: 18px;
}
.info-cell {
  padding: 10px 14px;
  display: flex;
  align-items: center;
  gap: 8px;
  border-bottom: 1px solid var(--adm-border);
  border-right: 1px solid var(--adm-border);
}
.info-cell:nth-child(2n) { border-right: none; }
.info-cell:nth-last-child(-n+2) { border-bottom: none; }
.info-label {
  font-size: 12px;
  color: var(--adm-text-tertiary);
  min-width: 64px;
  font-weight: 500;
}
.info-value {
  font-size: 13px;
  color: var(--adm-text-primary);
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 6px;
}

/* AI/Info Analysis card */
.ai-card {
  background: linear-gradient(135deg, #faf5ff, #eff6ff);
  border: 1px solid #ddd6fe;
  border-radius: var(--adm-radius-md);
  padding: 16px;
  margin-bottom: 18px;
}
.ai-card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 12px;
}
.ai-icon {
  width: 28px;
  height: 28px;
  border-radius: 8px;
  background: #fef3c7;
  color: #d97706;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.ai-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--adm-text-primary);
}
.ai-tag {
  margin-left: auto;
  font-size: 10px;
  padding: 2px 8px;
  border-radius: 4px;
  background: #7c3aed;
  color: #fff;
  font-weight: 600;
  letter-spacing: 0.5px;
}
.ai-field { margin-bottom: 10px; }
.ai-field:last-child { margin-bottom: 0; }
.ai-field-label {
  font-size: 11px;
  color: var(--adm-text-tertiary);
  margin-bottom: 4px;
  font-weight: 500;
}
.ai-field-value {
  font-size: 13px;
  color: var(--adm-text-primary);
  line-height: 1.7;
}
.err-type-tag {
  display: inline-flex;
  align-items: center;
  padding: 3px 10px;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 500;
  background: var(--adm-error-bg);
  color: var(--adm-error);
  border: 1px solid var(--adm-error-border);
}

/* Section title */
.section-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--adm-text-primary);
  margin-bottom: 10px;
  display: flex;
  align-items: center;
  gap: 6px;
}
.section-count {
  font-size: 11px;
  color: var(--adm-text-muted);
  font-weight: 400;
}

/* Migration banner */
.migration-banner {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  background: var(--adm-warning-bg);
  border: 1px solid var(--adm-warning-border);
  border-radius: var(--adm-radius-md);
  color: var(--adm-warning);
  font-size: 12px;
  margin-bottom: 12px;
}
.migration-banner span { flex: 1; }

/* Image gallery */
.img-gallery {
  display: flex;
  gap: 12px;
  margin-bottom: 18px;
  flex-wrap: wrap;
}
.img-gallery-item {
  width: 120px;
  height: 120px;
  border-radius: 8px;
  background: linear-gradient(135deg, #e2e8f0, #cbd5e1);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-muted);
  cursor: pointer;
  border: 2px solid var(--adm-border);
  position: relative;
  transition: all 0.15s;
  flex-shrink: 0;
  overflow: hidden;
}
.img-gallery-item:hover { border-color: var(--adm-primary-light); }
.img-gallery-item.selected {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.15);
}
.gallery-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.img-gallery-item .g-num {
  position: absolute;
  top: 6px;
  left: 6px;
  font-size: 10px;
  color: #fff;
  font-weight: 600;
  text-shadow: 0 1px 3px rgba(0, 0, 0, 0.5);
  background: rgba(0, 0, 0, 0.4);
  padding: 1px 6px;
  border-radius: 4px;
}

.no-images {
  padding: 24px;
  text-align: center;
  color: var(--adm-text-muted);
  font-size: 13px;
  background: var(--adm-surface-subtle);
  border-radius: var(--adm-radius-md);
  margin-bottom: 18px;
}

/* Large preview */
.img-preview-lg {
  margin-bottom: 18px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  overflow: hidden;
}
.img-preview-lg img {
  width: 100%;
  max-height: 300px;
  object-fit: contain;
  background: var(--adm-surface-subtle);
  display: block;
}
.img-path-row {
  display: flex;
  align-items: center;
  padding: 8px 12px;
  background: var(--adm-surface-subtle);
  border-top: 1px solid var(--adm-border);
  gap: 8px;
}
.image-path-text {
  flex: 1;
  font-size: 11px;
  color: var(--adm-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: 'SF Mono', Menlo, Consolas, monospace;
}

/* Edit form */
.edit-form-section {
  border-top: 1px solid var(--adm-border);
  padding-top: 16px;
}
.edit-form-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--adm-text-primary);
  margin-bottom: 12px;
  display: flex;
  align-items: center;
  gap: 6px;
}
.form-group { margin-bottom: 12px; }
.form-label {
  display: block;
  font-size: 12px;
  font-weight: 500;
  color: var(--adm-text-secondary);
  margin-bottom: 6px;
}
.form-label .required {
  color: var(--adm-error);
  margin-left: 2px;
}
.form-input,
.form-select,
.form-textarea {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-sm);
  font-size: 13px;
  color: var(--adm-text-primary);
  background: var(--adm-surface-elevated);
  transition: all 0.15s;
  outline: none;
  font-family: inherit;
  height: 36px;
}
.form-input:focus,
.form-select:focus,
.form-textarea:focus {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}
.form-select {
  appearance: none;
  padding-right: 32px;
  cursor: pointer;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%2394a3b8' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 10px center;
}
.form-row-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}
.form-input-wrap {
  position: relative;
}

/* Selected user banner (in edit form) */
.selected-user-banner {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  margin-top: 8px;
  border: 1px solid var(--adm-info-border);
  background: var(--adm-primary-bg);
  border-radius: var(--adm-radius-md);
}
.sel-info { flex: 1; min-width: 0; }
.sel-name {
  font-size: 13px;
  font-weight: 500;
  color: var(--adm-text-primary);
}
.sel-meta {
  font-size: 11px;
  color: var(--adm-text-muted);
  margin-top: 2px;
}
.sel-clear {
  width: 22px;
  height: 22px;
  border-radius: 50%;
  background: transparent;
  border: none;
  color: var(--adm-text-tertiary);
  cursor: pointer;
  font-size: 16px;
  line-height: 1;
  display: flex;
  align-items: center;
  justify-content: center;
}
.sel-clear:hover {
  background: rgba(59, 130, 246, 0.15);
  color: var(--adm-primary);
}
</style>
