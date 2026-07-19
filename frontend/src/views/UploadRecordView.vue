<template>
  <div class="adm-fade-in upload-record-view">
    <!-- Page Header -->
    <div class="adm-page-header">
      <div>
        <h1 class="adm-page-title">上传记录</h1>
        <p class="adm-page-subtitle">审核学生上传的作业图片，指派为错题或退回处理</p>
      </div>
      <div class="adm-page-actions">
        <button class="adm-btn adm-btn-secondary" @click="loadAll">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="23 4 23 10 17 10"/><polyline points="1 20 1 14 7 14"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/></svg>
          刷新
        </button>
      </div>
    </div>

    <!-- Stats Row - 5 pills with colored left border -->
    <div class="stats-row">
      <div
        v-for="s in statCards"
        :key="s.key"
        class="adm-stat-pill"
        :class="[s.cssClass, { active: filterStatus === s.value }]"
        @click="setFilterStatus(s.value)"
      >
        <div class="s-icon">
          <span v-if="statsLoading" class="spinner-sm"></span>
          <svg v-else width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" v-html="s.icon"></svg>
        </div>
        <div>
          <div class="sp-label">{{ s.label }}</div>
          <div class="sp-value">{{ statsLoading ? '—' : stats[s.key] }}</div>
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
      <select v-model="filterStatus" class="filter-select" @change="onFilterChange">
        <option :value="undefined">全部状态</option>
        <option v-for="s in statusOptions.filter(o => o.value !== -1)" :key="s.value" :value="s.value">{{ s.label }}</option>
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
      <div v-else-if="uploadRecords.length === 0" class="adm-empty">
        <div class="adm-empty-icon">
          <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>
        </div>
        <div class="adm-empty-text">暂无上传记录</div>
      </div>
      <div v-else class="table-wrap">
        <table class="adm-table">
          <thead>
            <tr>
              <th class="col-thumb">缩略图</th>
              <th>学生</th>
              <th>状态</th>
              <th>图片数</th>
              <th>学科</th>
              <th class="col-time">创建时间</th>
              <th class="col-actions">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="r in uploadRecords"
              :key="r.id"
              :class="{ 'active-row': currentRecord?.id === r.id && showDetailPanel }"
              @click="openDetailPanel(r)"
            >
              <td>
                <div class="thumb">
                  <img
                    v-if="r.imagePaths && r.imagePaths.length > 0"
                    :src="getImageUrl(r.imagePaths[0], 'small')"
                    class="thumb-img"
                    alt="缩略图"
                    @error="onThumbError"
                  />
                  <svg v-else width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>
                </div>
              </td>
              <td>
                <div class="student-cell">
                  <div class="stu-avatar-sm" :style="{ background: getAvatarGradient(r.studentName || r.studentId) }">{{ getAvatarChar(r.studentName || '?') }}</div>
                  <div>
                    <div class="student-name">{{ r.studentName }}</div>
                    <div class="student-grade">{{ getGradeLabel(getStudentGrade(r)) }}</div>
                  </div>
                </div>
              </td>
              <td>
                <span class="status-badge" :class="getStatusCssClass(r.status)">
                  <span v-if="r.status === 2" class="spinner-sm"></span>
                  <span v-else class="dot"></span>
                  {{ getStatusLabel(r.status) }}
                </span>
              </td>
              <td><span class="img-count">{{ r.imagePaths?.length || 0 }} 张</span></td>
              <td>
                <span v-if="getRecordSubject(r)" class="adm-subj-tag" :class="getSubjectCssClass(getRecordSubject(r))">
                  {{ getSubjectLabel(getRecordSubject(r)) }}
                </span>
                <span v-else class="no-subject">-</span>
              </td>
              <td><span class="time-cell">{{ formatDateTime(r.createdAt) }}</span></td>
              <td>
                <div class="actions-cell" @click.stop>
                  <button
                    v-if="canAssign(r)"
                    class="adm-btn-assign"
                    @click="openDetailPanelWithAssign(r)"
                  >
                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                    指派
                  </button>
                  <button class="adm-btn-link" @click="openDetailPanel(r)">详情</button>
                  <button class="adm-btn-link" @click="checkLegacy(r)">检查清理</button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Pagination -->
      <div v-if="!loading && uploadRecords.length > 0" class="pagination-bar">
        <span class="pagination-info">共 <strong>{{ total }}</strong> 条记录</span>
        <div class="pagination-controls">
          <select v-model="pageSize" class="page-size-select" @change="onPageChange(1)">
            <option :value="10">10 / 页</option>
            <option :value="20">20 / 页</option>
            <option :value="50">50 / 页</option>
          </select>
          <button class="page-btn" :disabled="page <= 1" @click="onPageChange(page - 1)">上一页</button>
          <span class="page-current">{{ page }} / {{ totalPages }}</span>
          <button class="page-btn" :disabled="page >= totalPages" @click="onPageChange(page + 1)">下一页</button>
        </div>
      </div>
    </div>

    <!-- ========== Detail Side Panel (480px) ========== -->
    <transition name="slide-panel">
      <aside v-if="showDetailPanel && currentRecord" class="detail-panel">
        <div class="detail-panel-header">
          <div class="detail-panel-title">
            上传记录详情
            <span class="rec-id">{{ currentRecord.id }}</span>
          </div>
          <div class="panel-header-actions">
            <button
              class="adm-btn adm-btn-ghost adm-btn-sm"
              :disabled="analyzingId === currentRecord.id"
              @click="handleVlAnalyze(currentRecord)"
            >
              <span v-if="analyzingId === currentRecord.id" class="spinner-sm"></span>
              <svg v-else width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="#7c3aed" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M12 3l1.9 5.8a2 2 0 0 0 1.3 1.3L21 12l-5.8 1.9a2 2 0 0 0-1.3 1.3L12 21l-1.9-5.8a2 2 0 0 0-1.3-1.3L3 12l5.8-1.9a2 2 0 0 0 1.3-1.3z"/>
              </svg>
              {{ analyzingId === currentRecord.id ? '分析中' : 'VL 分析' }}
            </button>
            <button class="adm-btn adm-btn-ghost adm-btn-sm" @click="openResetDialog">
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/></svg>
              重置
            </button>
            <button class="detail-panel-close" @click="closeDetailPanel">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
            </button>
          </div>
        </div>

        <div class="detail-panel-body">
          <!-- Basic Info -->
          <div class="panel-section-title">基本信息</div>
          <div class="desc-list">
            <div class="desc-row">
              <div class="desc-label">学生</div>
              <div class="desc-value">
                <div class="stu-avatar-sm" :style="{ background: getAvatarGradient(currentRecord.studentName || currentRecord.studentId) }">{{ getAvatarChar(currentRecord.studentName || '?') }}</div>
                {{ currentRecord.studentName }}
                <span class="desc-meta">({{ getGradeLabel(getStudentGrade(currentRecord)) }})</span>
              </div>
            </div>
            <div class="desc-row">
              <div class="desc-label">状态</div>
              <div class="desc-value">
                <span class="status-badge" :class="getStatusCssClass(currentRecord.status)">
                  <span v-if="currentRecord.status === 2" class="spinner-sm"></span>
                  <span v-else class="dot"></span>
                  {{ getStatusLabel(currentRecord.status) }}
                </span>
              </div>
            </div>
            <div class="desc-row">
              <div class="desc-label">图片数量</div>
              <div class="desc-value"><strong>{{ currentRecord.imagePaths?.length || 0 }}</strong> 张</div>
            </div>
            <div class="desc-row">
              <div class="desc-label">上传时间</div>
              <div class="desc-value">{{ formatDateTime(currentRecord.createdAt) }}</div>
            </div>
            <div class="desc-row">
              <div class="desc-label">更新时间</div>
              <div class="desc-value">{{ formatDateTime(currentRecord.updatedAt) }}</div>
            </div>
            <div class="desc-row">
              <div class="desc-label">备注</div>
              <div class="desc-value">{{ currentRecord.comments || '无' }}</div>
            </div>
          </div>

          <!-- Image Preview -->
          <div class="panel-section-title">
            图片预览
            <span class="section-meta">共 {{ currentRecord.imagePaths?.length || 0 }} 张</span>
          </div>
          <div v-if="!currentRecord.imagePaths || currentRecord.imagePaths.length === 0" class="panel-empty-hint">
            当前记录无图片
          </div>
          <template v-else>
            <div class="img-preview-lg">
              <img
                v-if="currentImageSrc"
                :src="currentImageSrc"
                :style="{ transform: `rotate(${currentImageRotation}deg)` }"
                class="preview-img"
                alt="预览"
                @error="onPreviewError"
              />
              <svg v-else width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"/><circle cx="8.5" cy="8.5" r="1.5"/><polyline points="21 15 16 10 5 21"/></svg>
            </div>
            <div class="preview-caption">
              图片 #{{ selectedImageIndex + 1 }}
              <span v-if="currentRecord.imagePaths?.[selectedImageIndex]"> · {{ currentRecord.imagePaths[selectedImageIndex] }}</span>
            </div>
            <div class="img-thumb-row">
              <div
                v-for="(img, idx) in currentRecord.imagePaths"
                :key="idx"
                class="img-thumb-sm"
                :class="{ selected: selectedImageIndex === idx, 'assign-selected': assignForm.imageIndices.includes(idx) }"
                @click="selectImage(idx)"
              >
                <img :src="getImageUrl(img, 'small')" class="thumb-img" alt="" @error="onThumbError" />
                <span class="img-num-sm">#{{ idx + 1 }}</span>
                <button
                  class="img-check"
                  :class="{ checked: assignForm.imageIndices.includes(idx) }"
                  :title="assignForm.imageIndices.includes(idx) ? '从指派中移除' : '加入指派'"
                  @click.stop="toggleImageForAssign(idx)"
                >
                  <svg v-if="assignForm.imageIndices.includes(idx)" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                </button>
                <div class="img-thumb-actions">
                  <button class="img-action-btn" title="向左旋转" @click.stop="rotateImage(currentRecord, idx, -90)">
                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/></svg>
                  </button>
                  <button class="img-action-btn" title="向右旋转" @click.stop="rotateImage(currentRecord, idx, 90)">
                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg>
                  </button>
                  <button class="img-action-btn danger" title="删除图片" @click.stop="confirmDeleteImage(idx)">
                    <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-2 14a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2L5 6"/></svg>
                  </button>
                </div>
              </div>
            </div>
            <div class="assign-image-summary">
              已选 <strong>{{ assignForm.imageIndices.length }}</strong> / {{ currentRecord.imagePaths.length }} 张用于指派
            </div>
          </template>

          <!-- VL Analysis (inline) -->
          <div class="panel-section-title">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#7c3aed" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M12 3l1.9 5.8a2 2 0 0 0 1.3 1.3L21 12l-5.8 1.9a2 2 0 0 0-1.3 1.3L12 21l-1.9-5.8a2 2 0 0 0-1.3-1.3L3 12l5.8-1.9a2 2 0 0 0 1.3-1.3z"/>
            </svg>
            AI 智能分组
            <span class="vl-badge">VL</span>
          </div>
          <div v-if="analyzingId === currentRecord.id" class="vl-card loading">
            <span class="spinner-sm"></span>
            <span>VL 模型分析中，请稍候...</span>
          </div>
          <div v-else-if="!vlResult" class="vl-card empty">
            <p>暂未运行 VL 分析</p>
            <button class="adm-btn adm-btn-primary adm-btn-sm" @click="handleVlAnalyze(currentRecord)">
              运行 VL 分析
            </button>
          </div>
          <div v-else-if="!vlResult.success" class="vl-card error">
            <div class="vl-card-header">
              <span class="vl-badge error">分析失败</span>
            </div>
            <p>{{ vlResult.errorMessage || 'VL 分析失败' }}</p>
            <button class="adm-btn adm-btn-secondary adm-btn-sm" @click="handleVlAnalyze(currentRecord)">重试</button>
          </div>
          <div v-else-if="vlResult.skipped" class="vl-card warning">
            <div class="vl-card-header">
              <span class="vl-badge warning">已跳过</span>
            </div>
            <p>{{ vlResult.errorMessage || '该记录没有图片或 VL 未配置' }}</p>
          </div>
          <div v-else-if="vlResult.groups.length === 0" class="vl-card warning">
            <div class="vl-card-header">
              <span class="vl-badge warning">无有效分组</span>
            </div>
            <p>模型返回了空内容，可能是图片质量问题或模型未能识别</p>
          </div>
          <div v-else class="vl-card">
            <div class="vl-card-header">
              <span class="vl-badge">VL 分析结果</span>
              <span class="vl-meta">识别到 {{ vlResult.groups.length }} 个分组</span>
            </div>
            <div class="vl-groups">
              <span v-for="(g, idx) in vlResult.groups" :key="idx" class="vl-group">
                <span class="g-imgs">图片 {{ g.imageIndices.map(i => i + 1).join(',') }}</span>
                <span class="g-subj">· {{ getSubjectLabel(g.subject) }} · {{ getGradeLabel(g.grade) }}</span>
              </span>
            </div>
            <p v-if="vlResult.groups[0]?.description" class="vl-desc">{{ vlResult.groups[0].description }}</p>
            <button class="adm-btn-link" @click="showVlResultModal = true">查看完整响应</button>
          </div>

          <!-- Assign Form -->
          <div class="assign-form">
            <div class="panel-section-title">
              指派为错题
              <span v-if="vlResult?.groups?.length" class="section-meta">
                <button class="adm-btn-link" @click="applyVlGrouping">应用 VL 分组</button>
              </span>
            </div>
            <div class="form-row-2">
              <div class="form-group">
                <label class="form-label">学科<span class="required">*</span></label>
                <select v-model="assignForm.subject" class="form-select">
                  <option :value="0" disabled>请选择学科</option>
                  <option v-for="s in subjectOptions" :key="s.value" :value="s.value">{{ s.displayName || s.name }}</option>
                </select>
              </div>
              <div class="form-group">
                <label class="form-label">年级<span class="required">*</span></label>
                <select v-model="assignForm.grade" class="form-select">
                  <option :value="0" disabled>请选择年级</option>
                  <option v-for="g in gradeOptions" :key="g.value" :value="g.value">{{ g.label }}</option>
                </select>
              </div>
            </div>
            <div class="form-group">
              <label class="form-label">备注</label>
              <textarea v-model="assignForm.comments" class="form-textarea" placeholder="添加审核备注（可选）..."></textarea>
            </div>
          </div>
        </div>

        <div class="detail-panel-footer">
          <button class="adm-btn adm-btn-secondary" @click="closeDetailPanel">取消</button>
          <button
            class="adm-btn adm-btn-ghost"
            style="color: var(--adm-warning);"
            :disabled="resetting"
            @click="returnRecord"
          >
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
            退回
          </button>
          <button
            class="adm-btn adm-btn-success"
            :disabled="assigning"
            @click="confirmAssign"
          >
            <span v-if="assigning" class="spinner-sm"></span>
            <svg v-else width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
            {{ assigning ? '指派中...' : '确认指派' }}
          </button>
        </div>
      </aside>
    </transition>

    <!-- ========== Reset Status Modal ========== -->
    <div v-if="showResetDialog" class="modal-mask" @click.self="showResetDialog = false">
      <div class="modal-box" style="width: 400px;">
        <div class="modal-header">
          <h3>重置状态</h3>
          <button class="modal-close" @click="showResetDialog = false">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label">目标状态<span class="required">*</span></label>
            <select v-model="resetForm.targetStatus" class="form-select">
              <option v-for="s in statusOptions.filter(o => o.value !== -1)" :key="s.value" :value="s.value">{{ s.label }}</option>
            </select>
          </div>
          <p class="modal-hint">重置后记录将回到指定状态，已有错题关联不会受影响。</p>
        </div>
        <div class="modal-footer">
          <button class="adm-btn adm-btn-secondary" @click="showResetDialog = false">取消</button>
          <button class="adm-btn adm-btn-primary" :disabled="resetting" @click="confirmReset">
            {{ resetting ? '重置中...' : '确认重置' }}
          </button>
        </div>
      </div>
    </div>

    <!-- ========== Legacy Check Modal ========== -->
    <div v-if="showLegacyDialog" class="modal-mask" @click.self="showLegacyDialog = false">
      <div class="modal-box" style="width: 500px;">
        <div class="modal-header">
          <h3>遗留数据检查</h3>
          <button class="modal-close" @click="showLegacyDialog = false">×</button>
        </div>
        <div class="modal-body">
          <div v-if="!legacyCheckResult" class="loading-row">
            <span class="spinner-sm"></span>
            <span>检查中...</span>
          </div>
          <template v-else>
            <div class="legacy-alert" :class="legacyCheckResult.isLegacy ? 'warning' : 'success'">
              <svg v-if="legacyCheckResult.isLegacy" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
              <svg v-else width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
              <span>{{ legacyCheckResult.message }}</span>
            </div>
            <div v-if="legacyCheckResult.isLegacy" class="desc-list">
              <div class="desc-row">
                <div class="desc-label">关联错题数</div>
                <div class="desc-value">{{ legacyCheckResult.mistakeCount }}</div>
              </div>
              <div class="desc-row">
                <div class="desc-label">图片未迁移</div>
                <div class="desc-value">{{ legacyCheckResult.hasUploadPathImage ? '是' : '否' }}</div>
              </div>
            </div>
            <div v-if="legacyCheckResult.isLegacy" class="legacy-hint">
              <p>清理操作将：</p>
              <ol>
                <li>迁移图片到 OSS 并更新错题中的图片路径</li>
                <li>删除该上传记录</li>
              </ol>
            </div>
          </template>
        </div>
        <div class="modal-footer">
          <button class="adm-btn adm-btn-secondary" @click="showLegacyDialog = false">关闭</button>
          <button
            v-if="legacyCheckResult?.isLegacy"
            class="adm-btn adm-btn-danger"
            :disabled="legacyCleaning"
            @click="cleanLegacy"
          >
            {{ legacyCleaning ? '清理中...' : '确认清理' }}
          </button>
        </div>
      </div>
    </div>

    <!-- ========== VL Result Detail Modal ========== -->
    <div v-if="showVlResultModal" class="modal-mask" @click.self="showVlResultModal = false">
      <div class="modal-box" style="width: 750px;">
        <div class="modal-header">
          <h3>VL 图片分析完整结果</h3>
          <button class="modal-close" @click="showVlResultModal = false">×</button>
        </div>
        <div class="modal-body">
          <div v-if="vlResult">
            <div v-if="vlResult.prompt" class="vl-section">
              <div class="vl-section-title">分析提示词</div>
              <pre class="vl-pre">{{ vlResult.prompt }}</pre>
            </div>
            <div v-if="vlResult.compressedImages?.length" class="vl-section">
              <div class="vl-section-title">发送给 AI 的压缩图片</div>
              <div class="vl-compressed-grid">
                <div v-for="(imgData, idx) in vlResult.compressedImages" :key="idx" class="vl-compressed-item">
                  <img :src="imgData" class="vl-compressed-img" alt="" />
                  <span class="vl-compressed-label">图片 {{ idx + 1 }}</span>
                </div>
              </div>
            </div>
            <div v-if="vlResult.groups.length" class="vl-section">
              <div class="vl-section-title">分组详情（{{ vlResult.groups.length }} 个）</div>
              <div v-for="(g, idx) in vlResult.groups" :key="idx" class="vl-group-detail">
                <div class="vl-group-detail-header">
                  <span class="vl-badge">分组 {{ idx + 1 }}</span>
                  <span class="vl-group-meta">{{ getSubjectLabel(g.subject) }} · {{ getGradeLabel(g.grade) }}</span>
                </div>
                <div v-if="g.description" class="vl-group-desc">{{ g.description }}</div>
                <div class="vl-group-images">
                  包含图片：
                  <span v-for="imgIdx in g.imageIndices" :key="imgIdx" class="vl-img-tag">图片 {{ imgIdx + 1 }}</span>
                </div>
              </div>
            </div>
            <div v-if="vlResult.rawResponse" class="vl-section">
              <div class="vl-section-title">原始响应</div>
              <pre class="vl-pre">{{ vlResult.rawResponse }}</pre>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="adm-btn adm-btn-secondary" @click="showVlResultModal = false">关闭</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { studentAdminClient } from '../services/studentAdminApi'
import type { UploadRecordDto, StudentDto, VlAnalysisResponse, GradeOption, SubjectOption, EnumOption } from '../services/studentAdminApi'
import {
  getSubjectLabel, getSubjectCssClass,
  getAvatarGradient, getAvatarChar, formatDateTime,
} from '../utils/subject'

// ===== State =====
const uploadRecords = ref<UploadRecordDto[]>([])
const loading = ref(false)
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)

const filterStatus = ref<number | undefined>(undefined)
const filterStudentId = ref<string | undefined>(undefined)
const filterStudentName = ref('')
const filterStudentOptions = ref<StudentDto[]>([])
let filterSearchTimer: number | null = null

const statusOptions = ref<{ value: number; label: string }[]>([
  { value: -1, label: '全部' },
  { value: 1, label: '待处理' },
  { value: 2, label: '处理中' },
  { value: 3, label: '已完成' },
  { value: 4, label: '失败' },
  { value: 5, label: '已退回' }
])
const subjectOptions = ref<SubjectOption[]>([])
const gradeOptions = ref<GradeOption[]>([])

// Stats
const stats = ref<{ pending: number; processing: number; completed: number; failed: number; returned: number }>({
  pending: 0, processing: 0, completed: 0, failed: 0, returned: 0
})
const statsLoading = ref(false)

const statCards = computed(() => [
  {
    key: 'pending' as const, label: '待处理', value: 1, cssClass: 'pending',
    icon: '<circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>'
  },
  {
    key: 'processing' as const, label: '处理中', value: 2, cssClass: 'processing',
    icon: '<path d="M21 12a9 9 0 1 1-6.219-8.56"/>'
  },
  {
    key: 'completed' as const, label: '已完成', value: 3, cssClass: 'completed',
    icon: '<polyline points="20 6 9 17 4 12"/>'
  },
  {
    key: 'failed' as const, label: '失败', value: 4, cssClass: 'failed',
    icon: '<line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>'
  },
  {
    key: 'returned' as const, label: '已退回', value: 5, cssClass: 'returned',
    icon: '<path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>'
  }
])

// Detail panel
const showDetailPanel = ref(false)
const currentRecord = ref<UploadRecordDto | null>(null)
const selectedImageIndex = ref(0)

// Assign form
const assigning = ref(false)
const assignForm = ref({
  subject: 0,
  grade: 0,
  imageIndices: [] as number[],
  comments: ''
})

// Reset
const showResetDialog = ref(false)
const resetting = ref(false)
const resetForm = ref({ targetStatus: 1 })

// Legacy check
const showLegacyDialog = ref(false)
const legacyCheckResult = ref<{ isLegacy: boolean; mistakeCount?: number; hasUploadPathImage?: boolean; message: string } | null>(null)
const legacyCleaning = ref(false)
const legacyTarget = ref<UploadRecordDto | null>(null)

// VL analysis
const analyzingId = ref<string | null>(null)
const vlResult = ref<VlAnalysisResponse | null>(null)
const showVlResultModal = ref(false)

// ===== Computed =====
const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))

const currentImageSrc = computed(() => {
  if (!currentRecord.value?.imagePaths?.length) return ''
  const path = currentRecord.value.imagePaths[selectedImageIndex.value]
  return path ? getImageUrl(path, 'medium') : ''
})

const currentImageRotation = computed(() => {
  if (!currentRecord.value?.imageRotations?.length) return 0
  return currentRecord.value.imageRotations[selectedImageIndex.value] || 0
})

// ===== API =====
async function loadEnumOptions() {
  try {
    const [grades, subjects] = await Promise.all([
      studentAdminClient.getGrades(),
      studentAdminClient.getSubjectOptions()
    ])
    gradeOptions.value = grades
    subjectOptions.value = subjects
  } catch (error) {
    console.error('Failed to load enum options:', error)
  }
}

async function loadUploadRecords() {
  loading.value = true
  try {
    const result = await studentAdminClient.getUploadRecords({
      page: page.value,
      pageSize: pageSize.value,
      status: filterStatus.value === -1 ? undefined : filterStatus.value,
      studentId: filterStudentId.value || undefined
    })
    uploadRecords.value = result.items
    total.value = result.totalCount
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '加载上传记录失败')
  } finally {
    loading.value = false
  }
}

async function loadStats() {
  statsLoading.value = true
  try {
    const [p1, p2, p3, p4, p5] = await Promise.all([
      studentAdminClient.getUploadRecords({ page: 1, pageSize: 1, status: 1 }),
      studentAdminClient.getUploadRecords({ page: 1, pageSize: 1, status: 2 }),
      studentAdminClient.getUploadRecords({ page: 1, pageSize: 1, status: 3 }),
      studentAdminClient.getUploadRecords({ page: 1, pageSize: 1, status: 4 }),
      studentAdminClient.getUploadRecords({ page: 1, pageSize: 1, status: 5 })
    ])
    stats.value = {
      pending: p1.totalCount,
      processing: p2.totalCount,
      completed: p3.totalCount,
      failed: p4.totalCount,
      returned: p5.totalCount
    }
  } catch (error) {
    console.error('Failed to load stats:', error)
  } finally {
    statsLoading.value = false
  }
}

async function loadAll() {
  await Promise.all([loadUploadRecords(), loadStats()])
}

// ===== Helpers =====
function getStatusLabel(status: number) {
  const map: Record<number, string> = { 1: '待处理', 2: '处理中', 3: '已完成', 4: '失败', 5: '已退回' }
  return map[status] || `状态${status}`
}

function getStatusCssClass(status: number) {
  const map: Record<number, string> = { 1: 'pending', 2: 'processing', 3: 'completed', 4: 'failed', 5: 'returned' }
  return map[status] || 'pending'
}

function getGradeLabel(grade: number) {
  return gradeOptions.value.find(g => g.value === grade)?.label || `年级${grade || '-'}`
}

function getStudentGrade(record: UploadRecordDto): number {
  // UploadRecordDto doesn't include grade; we'd need to look up the student.
  // For display only — fall back to 0 (unknown) until student record is fetched.
  // Lazy load via loadStudentGrades when panel opens.
  return studentGradeMap.value[record.studentId] || 0
}

const studentGradeMap = ref<Record<string, number>>({})

async function loadStudentGrade(studentId: string) {
  if (!studentId || studentGradeMap.value[studentId]) return
  try {
    const s = await studentAdminClient.getStudent(studentId)
    if (s) studentGradeMap.value[studentId] = s.grade
  } catch (error) {
    console.error('Failed to load student grade:', error)
  }
}

function getRecordSubject(_record: UploadRecordDto): number {
  // UploadRecordDto itself has no subject field; subject is determined at assign time.
  // Display "-" in the table; the panel shows VL-suggested subject.
  return 0
}

function canAssign(record: UploadRecordDto) {
  // Allow assign only when record is pending (1) or returned (5) — matches backend rule
  return record.status === 1 || record.status === 5
}

function getImageUrl(path: string, size?: 'small' | 'medium') {
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

function onPreviewError(e: Event) {
  const img = e.target as HTMLImageElement
  img.style.display = 'none'
}

// ===== Filters =====
function setFilterStatus(value: number | undefined) {
  filterStatus.value = filterStatus.value === value ? undefined : value
  page.value = 1
  loadUploadRecords()
}

function onFilterChange() {
  page.value = 1
  loadUploadRecords()
}

function onSearch() {
  page.value = 1
  loadUploadRecords()
}

function resetFilters() {
  filterStatus.value = undefined
  filterStudentId.value = undefined
  filterStudentName.value = ''
  filterStudentOptions.value = []
  page.value = 1
  loadUploadRecords()
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
      const result = await studentAdminClient.getStudents({ name: query, pageSize: 20 })
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
  loadUploadRecords()
}

function clearFilterStudent() {
  filterStudentId.value = undefined
  filterStudentName.value = ''
  filterStudentOptions.value = []
  page.value = 1
  loadUploadRecords()
}

function onPageChange(p: number) {
  page.value = p
  loadUploadRecords()
}

// ===== Detail Panel =====
function openDetailPanel(record: UploadRecordDto) {
  currentRecord.value = { ...record }
  selectedImageIndex.value = 0
  vlResult.value = null
  assignForm.value = {
    subject: 0,
    grade: 0,
    imageIndices: record.imagePaths?.map((_, idx) => idx) ?? [],
    comments: record.comments || ''
  }
  showDetailPanel.value = true
  loadStudentGrade(record.studentId)
}

function openDetailPanelWithAssign(record: UploadRecordDto) {
  openDetailPanel(record)
  // Auto-fill grade from student record if available
  const grade = studentGradeMap.value[record.studentId]
  if (grade) assignForm.value.grade = grade
}

function closeDetailPanel() {
  showDetailPanel.value = false
  currentRecord.value = null
  vlResult.value = null
}

function selectImage(idx: number) {
  selectedImageIndex.value = idx
}

function toggleImageForAssign(idx: number) {
  const i = assignForm.value.imageIndices.indexOf(idx)
  if (i >= 0) {
    assignForm.value.imageIndices.splice(i, 1)
  } else {
    assignForm.value.imageIndices.push(idx)
    assignForm.value.imageIndices.sort((a, b) => a - b)
  }
}

async function rotateImage(record: UploadRecordDto, imageIndex: number, rotation: number) {
  try {
    const current = record.imageRotations?.[imageIndex] || 0
    const newRotation = (current + rotation + 360) % 360
    await studentAdminClient.rotateUploadImage(record.id, {
      studentId: record.studentId,
      imageIndex,
      rotation: newRotation
    })
    if (!record.imageRotations) record.imageRotations = []
    record.imageRotations[imageIndex] = newRotation
    if (currentRecord.value?.id === record.id) {
      currentRecord.value.imageRotations = [...record.imageRotations]
    }
    ElMessage.success('图片旋转成功')
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '旋转图片失败')
  }
}

async function confirmDeleteImage(imageIndex: number) {
  if (!currentRecord.value) return
  try {
    await ElMessageBox.confirm('确定要删除这张图片吗？', '提示', {
      confirmButtonText: '删除',
      cancelButtonText: '取消',
      type: 'warning'
    })
  } catch {
    return
  }
  const record = currentRecord.value
  try {
    const result = await studentAdminClient.removeImageFromRecord(record.id, record.studentId, imageIndex)
    if (result.success) {
      ElMessage.success(result.recordDeleted ? '已删除最后一张图片，记录已自动删除' : '图片已删除')
      if (result.recordDeleted) {
        closeDetailPanel()
      } else if (currentRecord.value) {
        currentRecord.value.imagePaths.splice(imageIndex, 1)
        currentRecord.value.imageRotations?.splice(imageIndex, 1)
        if (selectedImageIndex.value >= currentRecord.value.imagePaths.length) {
          selectedImageIndex.value = Math.max(0, currentRecord.value.imagePaths.length - 1)
        }
        assignForm.value.imageIndices = assignForm.value.imageIndices
          .filter(i => i !== imageIndex)
          .map(i => (i > imageIndex ? i - 1 : i))
      }
      await loadUploadRecords()
    } else {
      ElMessage.error(result.message || '删除失败')
    }
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '删除图片失败')
  }
}

async function confirmAssign() {
  if (!currentRecord.value) {
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
  assigning.value = true
  try {
    await studentAdminClient.assignUploadRecord(currentRecord.value.id, {
      studentId: currentRecord.value.studentId,
      assignments: [{
        imageIndices: [...assignForm.value.imageIndices],
        subject: assignForm.value.subject,
        grade: assignForm.value.grade,
        comments: assignForm.value.comments || undefined
      }]
    })
    ElMessage.success('指派成功')
    closeDetailPanel()
    await Promise.all([loadUploadRecords(), loadStats()])
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '指派失败')
  } finally {
    assigning.value = false
  }
}

// ===== Reset / Return =====
function openResetDialog() {
  resetForm.value = { targetStatus: currentRecord.value?.status || 1 }
  showResetDialog.value = true
}

async function confirmReset() {
  if (!currentRecord.value) return
  resetting.value = true
  try {
    await studentAdminClient.resetUploadRecordStatus(
      currentRecord.value.id,
      currentRecord.value.studentId,
      resetForm.value.targetStatus
    )
    ElMessage.success('重置成功')
    showResetDialog.value = false
    closeDetailPanel()
    await Promise.all([loadUploadRecords(), loadStats()])
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '重置失败')
  } finally {
    resetting.value = false
  }
}

async function returnRecord() {
  if (!currentRecord.value) return
  try {
    await ElMessageBox.confirm('确定要退回此记录吗？退回后学生可重新上传。', '退回确认', {
      confirmButtonText: '确认退回',
      cancelButtonText: '取消',
      type: 'warning'
    })
  } catch {
    return
  }
  resetting.value = true
  try {
    await studentAdminClient.resetUploadRecordStatus(
      currentRecord.value.id,
      currentRecord.value.studentId,
      5
    )
    ElMessage.success('已退回')
    closeDetailPanel()
    await Promise.all([loadUploadRecords(), loadStats()])
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || '退回失败')
  } finally {
    resetting.value = false
  }
}

// ===== Legacy =====
async function checkLegacy(record: UploadRecordDto) {
  legacyTarget.value = record
  legacyCheckResult.value = null
  showLegacyDialog.value = true
  try {
    const result = await studentAdminClient.legacyCheck(record.id)
    legacyCheckResult.value = result
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || error.message || '检查失败')
    showLegacyDialog.value = false
  }
}

async function cleanLegacy() {
  if (!legacyTarget.value) return
  legacyCleaning.value = true
  try {
    const result = await studentAdminClient.legacyClean(legacyTarget.value.id)
    if (result.success) {
      ElMessage.success('清理成功')
      showLegacyDialog.value = false
      await Promise.all([loadUploadRecords(), loadStats()])
    } else {
      ElMessage.error(result.message || '清理失败')
    }
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || error.message || '清理失败')
  } finally {
    legacyCleaning.value = false
  }
}

// ===== VL Analysis =====
async function handleVlAnalyze(record: UploadRecordDto) {
  analyzingId.value = record.id
  try {
    const result = await studentAdminClient.analyzeUploadRecord(record.id)
    vlResult.value = result
    if (result.success && !result.skipped && result.groups.length > 0) {
      ElMessage.success(`VL 分析完成，识别到 ${result.groups.length} 个分组`)
    }
  } catch (error: any) {
    ElMessage.error(error.response?.data?.message || error.message || 'VL 分析失败')
  } finally {
    analyzingId.value = null
  }
}

function applyVlGrouping() {
  if (!vlResult.value?.groups?.length) return
  // Apply first group's suggestion to the assign form
  const firstGroup = vlResult.value.groups[0]
  assignForm.value.subject = firstGroup.subject
  assignForm.value.grade = firstGroup.grade
  assignForm.value.imageIndices = [...firstGroup.imageIndices].sort((a, b) => a - b)
  ElMessage.success(`已应用分组 1：${getSubjectLabel(firstGroup.subject)} · ${getGradeLabel(firstGroup.grade)}`)
}

// ===== Init =====
onMounted(async () => {
  await loadEnumOptions()
  await loadAll()
})
</script>

<style scoped>
.upload-record-view {
  position: relative;
}

/* ===== Stats Row ===== */
.stats-row {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 12px;
  margin-bottom: 16px;
}
.stats-row .adm-stat-pill {
  padding: 14px 18px;
}
.stats-row .s-icon {
  width: 32px;
  height: 32px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.stats-row .adm-stat-pill.pending .s-icon { background: var(--adm-warning-bg); color: var(--adm-warning); }
.stats-row .adm-stat-pill.processing .s-icon { background: var(--adm-info-bg); color: var(--adm-info); }
.stats-row .adm-stat-pill.completed .s-icon { background: var(--adm-success-bg); color: var(--adm-success); }
.stats-row .adm-stat-pill.failed .s-icon { background: var(--adm-error-bg); color: var(--adm-error); }
.stats-row .adm-stat-pill.returned .s-icon { background: #fff7ed; color: #ea580c; }
.stats-row .sp-value { color: var(--adm-text-primary); }

/* ===== Filter Bar ===== */
.filter-input-wrap {
  position: relative;
  width: 260px;
}
.filter-input {
  width: 100%;
  padding: 8px 12px 8px 34px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  font-size: 13px;
  color: var(--adm-text-primary);
  background: var(--adm-surface-elevated);
  transition: all 0.15s;
  outline: none;
  height: 36px;
}
.filter-input:focus { border-color: var(--adm-primary); box-shadow: 0 0 0 3px rgba(59,130,246,0.1); }
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
  min-width: 120px;
  height: 36px;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%2394a3b8' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 10px center;
}
.filter-select:focus { border-color: var(--adm-primary); box-shadow: 0 0 0 3px rgba(59,130,246,0.1); }
.filter-spacer { flex: 1; }
.filter-tag {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  background: var(--adm-primary-bg);
  color: var(--adm-primary);
  border-radius: var(--adm-radius-sm);
  font-size: 12px;
  font-weight: 500;
}
.filter-tag-x {
  background: transparent;
  border: none;
  color: var(--adm-primary);
  cursor: pointer;
  font-size: 14px;
  line-height: 1;
  padding: 0;
}
.filter-tag-x:hover { color: var(--adm-primary-dark); }

/* ===== User dropdown ===== */
.user-dropdown {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  margin-top: 4px;
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  box-shadow: var(--adm-shadow-md);
  max-height: 280px;
  overflow-y: auto;
  z-index: 30;
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
  font-size: 12px;
  font-weight: 600;
  color: #fff;
  flex-shrink: 0;
}
.opt-info { flex: 1; min-width: 0; }
.opt-name { font-size: 13px; color: var(--adm-text-primary); font-weight: 500; }
.opt-meta { font-size: 11px; color: var(--adm-text-muted); }

/* ===== Table ===== */
.table-card { overflow: hidden; }
.table-wrap { overflow-x: auto; }
.adm-table { width: 100%; border-collapse: collapse; }
.adm-table thead th {
  background: var(--adm-surface-subtle);
  padding: 12px 16px;
  text-align: left;
  font-size: 12px;
  font-weight: 600;
  color: var(--adm-text-tertiary);
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
.adm-table tbody tr { transition: background 0.12s; cursor: pointer; }
.adm-table tbody tr:hover { background: var(--adm-surface-subtle); }
.adm-table tbody tr.active-row { background: #eff6ff; }
.adm-table tbody tr.active-row:hover { background: #dbeafe; }
.adm-table tbody tr:last-child td { border-bottom: none; }
.col-thumb { width: 70px; }
.col-time { width: 140px; white-space: nowrap; }
.col-actions { width: 220px; text-align: right; }

.thumb {
  width: 50px;
  height: 50px;
  border-radius: 8px;
  background: linear-gradient(135deg, #e2e8f0, #cbd5e1);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-muted);
  border: 1px solid var(--adm-border);
  overflow: hidden;
}
.thumb-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: 7px;
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
.student-cell { display: flex; align-items: center; gap: 10px; }
.student-name { font-weight: 500; }
.student-grade { font-size: 11px; color: var(--adm-text-muted); }
.img-count { color: var(--adm-text-secondary); font-weight: 500; }
.time-cell { color: var(--adm-text-tertiary); font-size: 12px; white-space: nowrap; }
.no-subject { color: var(--adm-text-muted); font-size: 12px; }
.actions-cell {
  display: flex;
  align-items: center;
  gap: 2px;
  justify-content: flex-end;
}
.adm-btn-assign {
  background: var(--adm-primary);
  color: #fff;
  padding: 5px 12px;
  border-radius: 6px;
  font-size: 12px;
  font-weight: 500;
  display: inline-flex;
  align-items: center;
  gap: 4px;
  cursor: pointer;
  transition: all 0.15s;
  border: none;
}
.adm-btn-assign:hover { background: var(--adm-primary-dark); box-shadow: 0 2px 6px rgba(59,130,246,0.3); }
.adm-btn-assign:disabled { opacity: 0.5; cursor: not-allowed; }

/* Status badge */
.status-badge {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 4px 10px;
  border-radius: 6px;
  font-size: 12px;
  font-weight: 500;
}
.status-badge .dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
}
.status-badge.pending { background: var(--adm-warning-bg); color: var(--adm-warning); }
.status-badge.processing { background: var(--adm-info-bg); color: var(--adm-info); }
.status-badge.completed { background: var(--adm-success-bg); color: var(--adm-success); }
.status-badge.failed { background: var(--adm-error-bg); color: var(--adm-error); }
.status-badge.returned { background: #fff7ed; color: #ea580c; }

/* Spinner */
@keyframes spin { to { transform: rotate(360deg); } }
.spinner-sm {
  display: inline-block;
  width: 12px;
  height: 12px;
  border: 1.5px solid currentColor;
  border-top-color: transparent;
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
}
.spinner-lg {
  display: inline-block;
  width: 24px;
  height: 24px;
  border: 2.5px solid var(--adm-primary);
  border-top-color: transparent;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
.loading-row {
  padding: 48px 24px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  color: var(--adm-text-tertiary);
  font-size: 13px;
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
.pagination-info strong { color: var(--adm-text-primary); }
.pagination-controls { display: flex; align-items: center; gap: 8px; }
.page-size-select {
  padding: 6px 28px 6px 10px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-sm);
  font-size: 12px;
  color: var(--adm-text-secondary);
  background: var(--adm-surface-elevated);
  appearance: none;
  cursor: pointer;
  outline: none;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='10' height='10' viewBox='0 0 24 24' fill='none' stroke='%2394a3b8' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 8px center;
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
.page-btn:hover:not(:disabled) { background: var(--adm-surface-subtle); color: var(--adm-text-primary); }
.page-btn:disabled { color: var(--adm-text-muted); cursor: not-allowed; }
.page-current { font-size: 13px; color: var(--adm-text-primary); font-weight: 500; padding: 0 8px; }

/* ===== Detail Side Panel (480px) ===== */
.slide-panel-enter-active, .slide-panel-leave-active {
  transition: transform 0.25s ease;
}
.slide-panel-enter-from, .slide-panel-leave-to {
  transform: translateX(100%);
}
.detail-panel {
  position: fixed;
  top: 0;
  right: 0;
  width: 480px;
  height: 100vh;
  background: var(--adm-surface-elevated);
  border-left: 1px solid var(--adm-border);
  box-shadow: -8px 0 24px rgba(15, 23, 42, 0.08);
  z-index: 80;
  display: flex;
  flex-direction: column;
}
.detail-panel-header {
  padding: 14px 20px;
  border-bottom: 1px solid var(--adm-border);
  display: flex;
  align-items: center;
  justify-content: space-between;
  background: var(--adm-surface-elevated);
}
.detail-panel-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--adm-text-primary);
  display: flex;
  align-items: center;
  gap: 8px;
}
.detail-panel-title .rec-id {
  font-size: 11px;
  font-family: 'SF Mono', Menlo, Consolas, monospace;
  color: var(--adm-text-muted);
  font-weight: 400;
  background: var(--adm-surface-subtle);
  padding: 2px 8px;
  border-radius: 4px;
}
.panel-header-actions {
  display: flex;
  align-items: center;
  gap: 4px;
}
.detail-panel-close {
  width: 28px;
  height: 28px;
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
.detail-panel-close:hover { background: var(--adm-surface-subtle); color: var(--adm-text-primary); }

.detail-panel-body {
  flex: 1;
  overflow-y: auto;
  padding: 20px;
}

.panel-section-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--adm-text-primary);
  margin-bottom: 10px;
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 20px;
}
.panel-section-title:first-child { margin-top: 0; }
.section-meta {
  font-size: 11px;
  color: var(--adm-text-muted);
  font-weight: 400;
  margin-left: 4px;
}

/* Description list */
.desc-list {
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  overflow: hidden;
}
.desc-row {
  display: grid;
  grid-template-columns: 90px 1fr;
}
.desc-row:not(:last-child) { border-bottom: 1px solid var(--adm-border); }
.desc-label {
  padding: 10px 14px;
  font-size: 12px;
  color: var(--adm-text-tertiary);
  background: var(--adm-surface-subtle);
  font-weight: 500;
  border-right: 1px solid var(--adm-border);
}
.desc-value {
  padding: 10px 14px;
  font-size: 13px;
  color: var(--adm-text-primary);
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.desc-meta { color: var(--adm-text-muted); font-size: 12px; }

/* Image preview */
.panel-empty-hint {
  padding: 24px;
  text-align: center;
  color: var(--adm-text-muted);
  font-size: 13px;
  background: var(--adm-surface-subtle);
  border-radius: var(--adm-radius-md);
}
.img-preview-lg {
  width: 100%;
  height: 220px;
  border-radius: var(--adm-radius-md);
  background: linear-gradient(135deg, #f1f5f9, #e2e8f0);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-muted);
  border: 1px solid var(--adm-border);
  margin-bottom: 4px;
  overflow: hidden;
}
.preview-img {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
  transition: transform 0.3s;
}
.preview-caption {
  font-size: 11px;
  color: var(--adm-text-muted);
  text-align: center;
  margin-bottom: 10px;
  word-break: break-all;
}
.img-thumb-row {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}
.img-thumb-sm {
  width: 100px;
  height: 100px;
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
  overflow: hidden;
}
.img-thumb-sm:hover { border-color: var(--adm-primary-light); }
.img-thumb-sm.selected {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59,130,246,0.15);
}
.img-thumb-sm.assign-selected {
  border-color: var(--adm-success);
}
.img-thumb-sm .thumb-img { width: 100%; height: 100%; object-fit: cover; }
.img-num-sm {
  position: absolute;
  top: 4px;
  left: 4px;
  font-size: 10px;
  color: #fff;
  font-weight: 600;
  text-shadow: 0 1px 3px rgba(0,0,0,0.5);
  background: rgba(0,0,0,0.4);
  padding: 1px 6px;
  border-radius: 4px;
}
.img-check {
  position: absolute;
  top: 4px;
  right: 4px;
  width: 18px;
  height: 18px;
  border-radius: 4px;
  border: 1.5px solid #fff;
  background: rgba(255,255,255,0.7);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: all 0.15s;
}
.img-check.checked {
  background: var(--adm-success);
  border-color: var(--adm-success);
}
.img-thumb-actions {
  position: absolute;
  bottom: 4px;
  left: 50%;
  transform: translateX(-50%);
  display: flex;
  gap: 3px;
  opacity: 0;
  transition: opacity 0.15s;
}
.img-thumb-sm:hover .img-thumb-actions { opacity: 1; }
.img-action-btn {
  width: 22px;
  height: 22px;
  border-radius: 4px;
  background: rgba(0,0,0,0.5);
  color: #fff;
  border: none;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  transition: background 0.15s;
}
.img-action-btn:hover { background: rgba(0,0,0,0.7); }
.img-action-btn.danger:hover { background: var(--adm-error); }
.assign-image-summary {
  margin-top: 8px;
  padding: 6px 10px;
  background: var(--adm-success-bg);
  color: var(--adm-success);
  border-radius: var(--adm-radius-sm);
  font-size: 12px;
}
.assign-image-summary strong { font-weight: 600; }

/* VL card */
.vl-card {
  background: linear-gradient(135deg, #faf5ff, #eff6ff);
  border: 1px solid #ddd6fe;
  border-radius: var(--adm-radius-md);
  padding: 14px;
  margin-bottom: 16px;
}
.vl-card.loading,
.vl-card.empty,
.vl-card.error,
.vl-card.warning {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 10px;
}
.vl-card.loading,
.vl-card.empty {
  flex-direction: row;
  align-items: center;
  justify-content: space-between;
}
.vl-card p { font-size: 12px; color: var(--adm-text-secondary); line-height: 1.7; margin: 0; }
.vl-card-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}
.vl-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 3px 8px;
  border-radius: 4px;
  background: #7c3aed;
  color: #fff;
  font-size: 11px;
  font-weight: 600;
}
.vl-badge.error { background: var(--adm-error); }
.vl-badge.warning { background: var(--adm-warning); color: #fff; }
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
  margin-top: 6px;
  padding: 6px 10px;
  background: #fff;
  border-radius: 4px;
  border: 1px solid #e2e8f0;
  font-size: 12px;
  color: #7c3aed;
}

/* Assign form */
.assign-form {
  border-top: 1px solid var(--adm-border);
  padding-top: 16px;
  margin-top: 8px;
}
.form-group { margin-bottom: 14px; }
.form-label {
  display: block;
  font-size: 12px;
  font-weight: 500;
  color: var(--adm-text-secondary);
  margin-bottom: 6px;
}
.form-label .required { color: var(--adm-error); margin-left: 2px; }
.form-input, .form-select, .form-textarea {
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
}
.form-input:focus, .form-select:focus, .form-textarea:focus {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59,130,246,0.1);
}
.form-select {
  appearance: none;
  padding-right: 32px;
  cursor: pointer;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 24 24' fill='none' stroke='%2394a3b8' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'/%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 10px center;
}
.form-textarea {
  resize: vertical;
  min-height: 60px;
  line-height: 1.6;
}
.form-row-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.detail-panel-footer {
  padding: 14px 20px;
  border-top: 1px solid var(--adm-border);
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  background: var(--adm-surface-elevated);
}

/* ===== Modal (Reset/Legacy/VL Result) ===== */
.modal-mask {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.45);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 100;
  animation: fade-in 0.15s ease;
}
@keyframes fade-in { from { opacity: 0; } to { opacity: 1; } }
@keyframes modal-in {
  from { opacity: 0; transform: scale(0.96) translateY(-8px); }
  to { opacity: 1; transform: scale(1) translateY(0); }
}
.modal-box {
  background: var(--adm-surface-elevated);
  border-radius: var(--adm-radius-lg);
  box-shadow: var(--adm-shadow-lg);
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  animation: modal-in 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
}
.modal-header {
  padding: 16px 20px;
  border-bottom: 1px solid var(--adm-border);
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.modal-header h3 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--adm-text-primary);
}
.modal-close {
  width: 28px;
  height: 28px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-tertiary);
  cursor: pointer;
  background: transparent;
  border: none;
  font-size: 20px;
  line-height: 1;
  transition: all 0.15s;
}
.modal-close:hover { background: var(--adm-surface-subtle); color: var(--adm-text-primary); }
.modal-body {
  padding: 20px;
  overflow-y: auto;
  flex: 1;
}
.modal-footer {
  padding: 12px 20px;
  border-top: 1px solid var(--adm-border-light);
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
.modal-hint {
  margin-top: 12px;
  font-size: 12px;
  color: var(--adm-text-muted);
  line-height: 1.6;
}

/* Legacy modal */
.legacy-alert {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 14px;
  border-radius: var(--adm-radius-md);
  font-size: 13px;
  margin-bottom: 16px;
}
.legacy-alert.warning { background: var(--adm-warning-bg); color: var(--adm-warning); border: 1px solid var(--adm-warning-border); }
.legacy-alert.success { background: var(--adm-success-bg); color: var(--adm-success); border: 1px solid var(--adm-success-border); }
.legacy-hint {
  margin-top: 16px;
  padding: 12px 14px;
  background: var(--adm-surface-subtle);
  border-radius: var(--adm-radius-md);
  font-size: 12px;
  color: var(--adm-text-tertiary);
}
.legacy-hint p { margin: 0 0 6px; }
.legacy-hint ol { margin: 0; padding-left: 20px; }
.legacy-hint li { margin-bottom: 4px; }

/* VL Result Modal */
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
.vl-img-tag {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  background: var(--adm-primary-bg);
  color: var(--adm-primary);
  border-radius: 4px;
  font-size: 11px;
  font-weight: 500;
}
</style>
