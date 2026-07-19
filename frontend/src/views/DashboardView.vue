<template>
  <div class="adm-fade-in">
    <!-- Page Header -->
    <div class="adm-page-header">
      <div>
        <h1 class="adm-page-title">数据概览</h1>
        <p class="adm-page-subtitle">欢迎回来，管理员。这是平台今日运营数据一览。</p>
      </div>
      <div class="adm-page-date">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>
        {{ todayString }}
      </div>
    </div>

    <!-- ROW 1: KPI Cards -->
    <div class="kpi-grid">
      <!-- KPI 1: 总学生数 -->
      <div class="kpi-card">
        <div class="kpi-icon blue">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
        </div>
        <div class="kpi-body">
          <div class="kpi-label">总学生数</div>
          <div class="kpi-value">{{ formatNumber(stats.students) }}</div>
          <div class="kpi-delta up">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="18 15 12 9 6 15"/></svg>
            实时数据
          </div>
        </div>
        <svg class="kpi-sparkline" viewBox="0 0 200 44" preserveAspectRatio="none">
          <defs>
            <linearGradient id="spkBlue" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stop-color="#3b82f6" stop-opacity="0.3"/>
              <stop offset="100%" stop-color="#3b82f6" stop-opacity="0"/>
            </linearGradient>
          </defs>
          <polyline points="0,32 28,28 56,30 84,22 112,24 140,16 168,12 200,8" fill="none" stroke="#3b82f6" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
          <polygon points="0,32 28,28 56,30 84,22 112,24 140,16 168,12 200,8 200,44 0,44" fill="url(#spkBlue)"/>
        </svg>
      </div>

      <!-- KPI 2: 总教师数 -->
      <div class="kpi-card">
        <div class="kpi-icon green">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/><path d="m9 12 2 2 4-4"/></svg>
        </div>
        <div class="kpi-body">
          <div class="kpi-label">总教师数</div>
          <div class="kpi-value">{{ formatNumber(stats.teachers) }}</div>
          <div class="kpi-delta up">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="18 15 12 9 6 15"/></svg>
            助教 {{ stats.assistants }} 人
          </div>
        </div>
        <svg class="kpi-sparkline" viewBox="0 0 200 44" preserveAspectRatio="none">
          <defs>
            <linearGradient id="spkGreen" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stop-color="#10b981" stop-opacity="0.3"/>
              <stop offset="100%" stop-color="#10b981" stop-opacity="0"/>
            </linearGradient>
          </defs>
          <polyline points="0,30 28,28 56,24 84,26 112,20 140,18 168,14 200,12" fill="none" stroke="#10b981" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
          <polygon points="0,30 28,28 56,24 84,26 112,20 140,18 168,14 200,12 200,44 0,44" fill="url(#spkGreen)"/>
        </svg>
      </div>

      <!-- KPI 3: 待处理上传 -->
      <div class="kpi-card">
        <div class="kpi-icon orange">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>
        </div>
        <div class="kpi-body">
          <div class="kpi-label">待处理上传</div>
          <div class="kpi-value">{{ formatNumber(stats.pendingUploads) }}</div>
          <div class="kpi-delta warn">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="18 15 12 9 6 15"/></svg>
            等待审核
          </div>
        </div>
        <svg class="kpi-sparkline" viewBox="0 0 200 44" preserveAspectRatio="none">
          <defs>
            <linearGradient id="spkOrange" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stop-color="#f59e0b" stop-opacity="0.3"/>
              <stop offset="100%" stop-color="#f59e0b" stop-opacity="0"/>
            </linearGradient>
          </defs>
          <g fill="#f59e0b" opacity="0.7">
            <rect x="10" y="28" width="12" height="16" rx="2"/>
            <rect x="30" y="22" width="12" height="22" rx="2"/>
            <rect x="50" y="32" width="12" height="12" rx="2"/>
            <rect x="70" y="18" width="12" height="26" rx="2"/>
            <rect x="90" y="24" width="12" height="20" rx="2"/>
            <rect x="110" y="14" width="12" height="30" rx="2"/>
            <rect x="130" y="20" width="12" height="24" rx="2"/>
            <rect x="150" y="10" width="12" height="34" rx="2"/>
            <rect x="170" y="16" width="12" height="28" rx="2"/>
          </g>
        </svg>
      </div>

      <!-- KPI 4: 待审核错题 -->
      <div class="kpi-card">
        <div v-if="stats.pendingMistakes > 0" class="kpi-pulse-dot"></div>
        <div class="kpi-icon red">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
        </div>
        <div class="kpi-body">
          <div class="kpi-label">待审核错题</div>
          <div class="kpi-value">{{ formatNumber(stats.pendingMistakes) }}</div>
          <div class="kpi-delta neutral">
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
            <span v-if="stats.pendingMistakes > 0">需要及时处理</span>
            <span v-else>暂无待办</span>
          </div>
        </div>
        <svg class="kpi-sparkline" viewBox="0 0 200 44" preserveAspectRatio="none">
          <defs>
            <linearGradient id="spkRed" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stop-color="#ef4444" stop-opacity="0.2"/>
              <stop offset="100%" stop-color="#ef4444" stop-opacity="0"/>
            </linearGradient>
          </defs>
          <polyline points="0,24 28,20 56,26 84,18 112,22 140,14 168,18 200,10" fill="none" stroke="#ef4444" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="4,3"/>
          <polygon points="0,24 28,20 56,26 84,18 112,22 140,14 168,18 200,10 200,44 0,44" fill="url(#spkRed)"/>
          <circle cx="200" cy="10" r="4" fill="#ef4444"/>
        </svg>
      </div>
    </div>

    <!-- ROW 2: Upload Trend + Pending Tasks -->
    <div class="row-2">
      <!-- LEFT: Upload Trend Chart -->
      <div class="adm-card">
        <div class="adm-card-header">
          <div>
            <div class="adm-card-title">近期上传趋势</div>
            <div class="adm-card-subtitle">最近 7 天上传与批阅数据</div>
          </div>
          <div class="time-range">
            <button :class="{ active: trendRange === 'day' }" @click="trendRange = 'day'">今日</button>
            <button :class="{ active: trendRange === 'week' }" @click="trendRange = 'week'">本周</button>
            <button :class="{ active: trendRange === 'month' }" @click="trendRange = 'month'">本月</button>
          </div>
        </div>
        <div class="chart-container">
          <div class="chart-y-axis">
            <span>{{ trendMax }}</span>
            <span>{{ Math.round(trendMax * 0.75) }}</span>
            <span>{{ Math.round(trendMax * 0.5) }}</span>
            <span>{{ Math.round(trendMax * 0.25) }}</span>
            <span>0</span>
          </div>
          <div class="chart-area">
            <div class="chart-grid">
              <div class="chart-grid-line"></div>
              <div class="chart-grid-line"></div>
              <div class="chart-grid-line"></div>
              <div class="chart-grid-line"></div>
              <div class="chart-grid-line"></div>
            </div>
            <div class="chart-bars">
              <div v-for="(item, idx) in trendData" :key="idx" class="bar-col">
                <div class="bar-wrap">
                  <div class="bar student" :style="{ height: (item.student / trendMax * 100) + '%' }">
                    <div class="bar-tooltip">学生上传: {{ item.student }}</div>
                  </div>
                  <div class="bar teacher" :style="{ height: (item.teacher / trendMax * 100) + '%' }">
                    <div class="bar-tooltip">教师批阅: {{ item.teacher }}</div>
                  </div>
                </div>
                <div class="bar-label">{{ item.label }}</div>
              </div>
            </div>
          </div>
          <div class="chart-legend">
            <div class="legend-item"><span class="legend-dot blue"></span>学生上传</div>
            <div class="legend-item"><span class="legend-dot green"></span>教师批阅</div>
          </div>
        </div>
      </div>

      <!-- RIGHT: Pending Tasks -->
      <div class="adm-card">
        <div class="adm-card-header">
          <div>
            <div class="adm-card-title">待处理事项</div>
            <div class="adm-card-subtitle">共 {{ totalPending }} 项待处理</div>
          </div>
          <span class="adm-card-extra" @click="$router.push('/upload-records')">查看全部</span>
        </div>
        <div class="task-list">
          <div class="task-item" @click="$router.push('/upload-records?status=1')">
            <div class="task-dot orange"></div>
            <div class="task-icon" style="background:var(--adm-warning-bg);color:var(--adm-warning);">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>
            </div>
            <div class="task-info">
              <div class="task-name">上传记录待审核</div>
              <div class="task-desc">{{ stats.pendingUploads }} 条记录等待审核</div>
            </div>
            <span class="task-count" :class="{ highlight: stats.pendingUploads > 0 }">{{ stats.pendingUploads }}</span>
            <span class="task-action">处理</span>
          </div>
          <div class="task-item" @click="$router.push('/oss-audit?status=0')">
            <div class="task-dot red"></div>
            <div class="task-icon" style="background:var(--adm-error-bg);color:var(--adm-error);">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
            </div>
            <div class="task-info">
              <div class="task-name">OSS 审计异常文件</div>
              <div class="task-desc">{{ stats.pendingOssAudit }} 个文件需要核查</div>
            </div>
            <span class="task-count" :class="{ highlight: stats.pendingOssAudit > 0 }">{{ stats.pendingOssAudit }}</span>
            <span class="task-action">处理</span>
          </div>
          <div class="task-item" @click="$router.push('/mistakes?reviewStatus=1')">
            <div class="task-dot yellow"></div>
            <div class="task-icon" style="background:#fef9c3;color:#ca8a04;">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"/></svg>
            </div>
            <div class="task-info">
              <div class="task-name">错题待审核</div>
              <div class="task-desc">{{ stats.pendingMistakes }} 条错题等待审核</div>
            </div>
            <span class="task-count" :class="{ highlight: stats.pendingMistakes > 0 }">{{ stats.pendingMistakes }}</span>
            <span class="task-action">处理</span>
          </div>
          <div class="task-item" @click="$router.push('/teachers')">
            <div class="task-dot blue"></div>
            <div class="task-icon" style="background:var(--adm-primary-bg);color:var(--adm-primary);">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="m22 11-3 3-2-2"/></svg>
            </div>
            <div class="task-info">
              <div class="task-name">教师管理</div>
              <div class="task-desc">管理教师权限与科目</div>
            </div>
            <span class="task-count">{{ stats.teachers }}</span>
            <span class="task-action">查看</span>
          </div>
          <div class="task-item" @click="$router.push('/assistants')">
            <div class="task-dot purple"></div>
            <div class="task-icon" style="background:#f3e8ff;color:#8b5cf6;">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><line x1="19" y1="8" x2="19" y2="14"/><line x1="22" y1="11" x2="16" y2="11"/></svg>
            </div>
            <div class="task-info">
              <div class="task-name">助教管理</div>
              <div class="task-desc">管理助教权限与学生关联</div>
            </div>
            <span class="task-count">{{ stats.assistants }}</span>
            <span class="task-action">查看</span>
          </div>
        </div>
      </div>
    </div>

    <!-- ROW 3: Subject Distribution + Recent Activity -->
    <div class="row-3">
      <!-- LEFT: Subject Distribution (static demo data) -->
      <div class="adm-card">
        <div class="adm-card-header">
          <div>
            <div class="adm-card-title">学科分布</div>
            <div class="adm-card-subtitle">各学科教师覆盖情况（演示数据）</div>
          </div>
          <span class="adm-card-extra" @click="$router.push('/teachers')">详细分析</span>
        </div>
        <div class="subject-chart">
          <div v-for="subj in subjectDistribution" :key="subj.name" class="subject-row">
            <div class="subject-name">{{ subj.name }}</div>
            <div class="subject-bar-wrap">
              <div class="subject-bar" :class="subj.cssClass" :style="{ width: subj.percent + '%' }">
                <span class="subject-bar-val">{{ subj.count }}</span>
              </div>
            </div>
            <div class="subject-count">{{ subj.count }}人</div>
          </div>
        </div>
      </div>

      <!-- RIGHT: Recent Activity (static demo data) -->
      <div class="adm-card">
        <div class="adm-card-header">
          <div>
            <div class="adm-card-title">最近活动</div>
            <div class="adm-card-subtitle">平台实时动态（演示数据）</div>
          </div>
          <span class="adm-card-extra">查看全部</span>
        </div>
        <div class="activity-list">
          <div v-for="(act, idx) in recentActivities" :key="idx" class="activity-item">
            <div class="activity-avatar" :class="act.avatarClass">
              <svg v-if="act.icon === 'system'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
              <span v-else>{{ act.avatarText }}</span>
            </div>
            <div class="activity-body">
              <div class="activity-text">
                <span v-if="act.name" class="name">{{ act.name }}</span>
                <span class="action">{{ act.action }}</span>
                <span v-if="act.target" class="target">{{ act.target }}</span>
                <span v-if="act.action2" class="action">{{ act.action2 }}</span>
                <span v-if="act.obj" class="obj">{{ act.obj }}</span>
              </div>
              <div class="activity-time">{{ act.time }}</div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- ROW 4: Quick Actions -->
    <div class="row-4">
      <div class="section-header">
        <div class="section-title">快捷操作</div>
      </div>
      <div class="quick-actions-grid">
        <div class="quick-action" @click="$router.push('/students')">
          <div class="qa-icon blue">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><line x1="19" y1="8" x2="19" y2="14"/><line x1="22" y1="11" x2="16" y2="11"/></svg>
          </div>
          <div class="qa-text">
            <div class="qa-title">添加学生</div>
            <div class="qa-desc">录入新学生账户信息</div>
          </div>
          <svg class="qa-arrow" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>
        </div>
        <div class="quick-action" @click="$router.push('/teachers')">
          <div class="qa-icon green">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="m22 11-3 3-2-2"/></svg>
          </div>
          <div class="qa-text">
            <div class="qa-title">授予教师权限</div>
            <div class="qa-desc">审批教师注册申请</div>
          </div>
          <svg class="qa-arrow" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>
        </div>
        <div class="quick-action" @click="$router.push('/oss-audit')">
          <div class="qa-icon orange">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
          </div>
          <div class="qa-text">
            <div class="qa-title">运行 OSS 审计</div>
            <div class="qa-desc">检查文件存储一致性</div>
          </div>
          <svg class="qa-arrow" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>
        </div>
        <div class="quick-action" @click="$router.push('/upload-records')">
          <div class="qa-icon purple">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>
          </div>
          <div class="qa-text">
            <div class="qa-title">查看上传记录</div>
            <div class="qa-desc">审核待处理的上传内容</div>
          </div>
          <svg class="qa-arrow" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="9 18 15 12 9 6"/></svg>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import studentAdminClient from '../services/studentAdminApi'
import teacherPortalClient from '../services/teacherPortalApi'
import assistantPortalClient from '../services/assistantPortalApi'
import ossAuditApi from '../services/ossAuditApi'

interface DashboardStats {
  students: number
  teachers: number
  assistants: number
  pendingUploads: number
  pendingMistakes: number
  pendingOssAudit: number
}

const stats = ref<DashboardStats>({
  students: 0,
  teachers: 0,
  assistants: 0,
  pendingUploads: 0,
  pendingMistakes: 0,
  pendingOssAudit: 0,
})

const loading = ref(true)
const trendRange = ref<'day' | 'week' | 'month'>('week')

const trendData = ref([
  { label: '周一', student: 85, teacher: 60 },
  { label: '周二', student: 120, teacher: 90 },
  { label: '周三', student: 95, teacher: 110 },
  { label: '周四', student: 145, teacher: 80 },
  { label: '周五', student: 165, teacher: 130 },
  { label: '周六', student: 78, teacher: 45 },
  { label: '周日', student: 156, teacher: 95 },
])

const trendMax = computed(() => {
  const max = Math.max(...trendData.value.flatMap(d => [d.student, d.teacher]))
  return Math.ceil(max / 50) * 50 || 200
})

const subjectDistribution = ref([
  { name: '英语', count: 0, percent: 100, cssClass: 'subj-english' },
  { name: '数学', count: 0, percent: 71, cssClass: 'subj-math' },
  { name: '语文', count: 0, percent: 62, cssClass: 'subj-chinese' },
  { name: '物理', count: 0, percent: 35, cssClass: 'subj-physics' },
  { name: '化学', count: 0, percent: 27, cssClass: 'subj-chemistry' },
  { name: '生物', count: 0, percent: 22, cssClass: 'subj-biology' },
  { name: '历史', count: 0, percent: 17, cssClass: 'subj-history' },
  { name: '地理', count: 0, percent: 12, cssClass: 'subj-geography' },
  { name: '政治', count: 0, percent: 11, cssClass: 'subj-politics' },
])

const recentActivities = ref([
  { name: '李老师', action: '审核通过了', target: '张小明', action2: '的', obj: '数学错题', time: '2 分钟前', avatarClass: 'av-blue', avatarText: '李', icon: '' },
  { name: '', action: '新学生', target: '王小红', action2: '完成注册，已关联账户', obj: '', time: '15 分钟前', avatarClass: 'av-pink', avatarText: '王', icon: '' },
  { name: '陈老师', action: '发布了新作业', target: '', action2: '', obj: '"期末复习卷(三)"', time: '32 分钟前', avatarClass: 'av-green', avatarText: '陈', icon: '' },
  { name: '系统', action: '自动完成 OSS 审计，发现', target: '3 个异常文件', action2: '', obj: '', time: '1 小时前', avatarClass: 'av-purple', avatarText: '', icon: 'system' },
  { name: '张小明', action: '上传了', target: '', action2: '，等待处理', obj: '3 张图片', time: '2 小时前', avatarClass: 'av-orange', avatarText: '张', icon: '' },
])

const totalPending = computed(() =>
  stats.value.pendingUploads + stats.value.pendingMistakes + stats.value.pendingOssAudit
)

const todayString = computed(() => {
  const d = new Date()
  const weekdays = ['周日', '周一', '周二', '周三', '周四', '周五', '周六']
  return `${d.getFullYear()}年${d.getMonth() + 1}月${d.getDate()}日 ${weekdays[d.getDay()]}`
})

function formatNumber(n: number): string {
  return n.toLocaleString('zh-CN')
}

async function loadStats() {
  loading.value = true
  try {
    const [studentsRes, teachersRes, assistantsRes, uploadsRes, mistakesRes, ossRes] = await Promise.all([
      studentAdminClient.getStudents({ page: 1, pageSize: 1 }).catch(() => ({ total: 0 })),
      teacherPortalClient.getTeachers().catch(() => ({ success: false, data: [] as any[] })),
      assistantPortalClient.getAssistants().catch(() => ({ success: false, data: [] as any[] })),
      studentAdminClient.getUploadRecords({ page: 1, pageSize: 1, status: 1 }).catch(() => ({ totalCount: 0 })),
      studentAdminClient.getMistakeItems({ reviewStatus: 1, page: 1, size: 1 }).catch(() => ({ total: 0 })),
      ossAuditApi.getRecords(1, 1, 0).catch(() => ({ totalCount: 0 })),
    ])

    stats.value = {
      students: (studentsRes as any).total ?? 0,
      teachers: (teachersRes as any).data?.length ?? 0,
      assistants: (assistantsRes as any).data?.length ?? 0,
      pendingUploads: (uploadsRes as any).totalCount ?? 0,
      pendingMistakes: (mistakesRes as any).total ?? 0,
      pendingOssAudit: (ossRes as any).totalCount ?? 0,
    }
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  loadStats()
})
</script>

<style scoped>
.adm-page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 24px;
}
.adm-page-title {
  font-size: 22px;
  font-weight: 700;
  color: var(--adm-text-primary);
  margin: 0 0 4px;
}
.adm-page-subtitle {
  font-size: 13px;
  color: var(--adm-text-tertiary);
  line-height: 1.6;
  margin: 0;
}
.adm-page-date {
  font-size: 13px;
  color: var(--adm-text-tertiary);
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 12px;
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
}

/* KPI Cards */
.kpi-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
  margin-bottom: 20px;
  position: relative;
}
.kpi-card {
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-lg);
  padding: 20px;
  transition: all 0.2s ease;
  position: relative;
  overflow: hidden;
}
.kpi-card:hover {
  box-shadow: var(--adm-shadow-md);
  transform: translateY(-2px);
  border-color: var(--adm-primary-bg);
}
.kpi-icon {
  position: absolute;
  top: 16px;
  right: 16px;
  width: 44px;
  height: 44px;
  border-radius: var(--adm-radius-md);
  display: flex;
  align-items: center;
  justify-content: center;
}
.kpi-icon.blue { background: var(--adm-primary-bg); color: var(--adm-primary); }
.kpi-icon.green { background: var(--adm-success-bg); color: var(--adm-success); }
.kpi-icon.orange { background: var(--adm-warning-bg); color: var(--adm-warning); }
.kpi-icon.red { background: var(--adm-error-bg); color: var(--adm-error); }
.kpi-body { padding-right: 56px; }
.kpi-label {
  font-size: 13px;
  color: var(--adm-text-tertiary);
  margin-bottom: 8px;
  font-weight: 500;
}
.kpi-value {
  font-size: 28px;
  font-weight: 700;
  color: var(--adm-text-primary);
  line-height: 1.2;
  letter-spacing: -0.5px;
}
.kpi-delta {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  margin-top: 8px;
  font-weight: 500;
  padding: 2px 8px;
  border-radius: 6px;
}
.kpi-delta.up { color: var(--adm-success); background: var(--adm-success-bg); }
.kpi-delta.warn { color: var(--adm-warning); background: var(--adm-warning-bg); }
.kpi-delta.neutral { color: var(--adm-error); background: var(--adm-error-bg); }
.kpi-sparkline {
  margin-top: 12px;
  width: 100%;
  height: 44px;
}
.kpi-pulse-dot {
  position: absolute;
  top: 20px;
  right: 64px;
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--adm-error);
  animation: pulseDot 1.8s infinite;
}
@keyframes pulseDot {
  0%, 100% { box-shadow: 0 0 0 2px rgba(239,68,68,0.25); }
  50% { box-shadow: 0 0 0 5px rgba(239,68,68,0.1); }
}

/* Row 2 */
.row-2 {
  display: grid;
  grid-template-columns: 3fr 2fr;
  gap: 16px;
  margin-bottom: 20px;
}

/* Card base */
.adm-card {
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-lg);
  padding: 20px;
  transition: box-shadow 0.2s;
}
.adm-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 16px;
}
.adm-card-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--adm-text-primary);
}
.adm-card-subtitle {
  font-size: 12px;
  color: var(--adm-text-tertiary);
  margin-top: 2px;
}
.adm-card-extra {
  font-size: 12px;
  color: var(--adm-primary);
  cursor: pointer;
  font-weight: 500;
  transition: color 0.15s;
}
.adm-card-extra:hover { color: var(--adm-primary-dark); }

/* Time range pills */
.time-range {
  display: flex;
  gap: 2px;
  background: var(--adm-surface-subtle);
  padding: 3px;
  border-radius: var(--adm-radius-md);
}
.time-range button {
  padding: 5px 14px;
  border-radius: 5px;
  font-size: 12px;
  font-weight: 500;
  color: var(--adm-text-tertiary);
  transition: all 0.2s;
  white-space: nowrap;
  border: none;
  background: transparent;
  cursor: pointer;
}
.time-range button:hover { color: var(--adm-text-secondary); }
.time-range button.active {
  background: var(--adm-primary);
  color: #fff;
  box-shadow: 0 1px 3px rgba(59,130,246,0.3);
}

/* Bar chart */
.chart-container {
  position: relative;
  padding-left: 36px;
  margin-top: 8px;
}
.chart-y-axis {
  position: absolute;
  left: 0;
  top: 0;
  bottom: 28px;
  width: 32px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  font-size: 10px;
  color: var(--adm-text-muted);
  text-align: right;
  padding-right: 6px;
}
.chart-area {
  position: relative;
  height: 220px;
  border-bottom: 1px solid var(--adm-border);
  border-left: 1px solid var(--adm-border);
}
.chart-grid {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  pointer-events: none;
}
.chart-grid-line {
  height: 1px;
  background: var(--adm-border-light);
  width: 100%;
}
.chart-bars {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: flex-end;
  padding: 0 8px;
  gap: 8px;
}
.bar-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  height: 100%;
  justify-content: flex-end;
  position: relative;
}
.bar-wrap {
  width: 100%;
  display: flex;
  align-items: flex-end;
  justify-content: center;
  flex: 1;
  gap: 4px;
}
.bar {
  width: 42%;
  border-radius: 4px 4px 0 0;
  transition: all 0.25s ease;
  position: relative;
  min-height: 4px;
  cursor: pointer;
}
.bar.student { background: linear-gradient(180deg, #60a5fa, #3b82f6); }
.bar.teacher { background: linear-gradient(180deg, #34d399, #10b981); }
.bar:hover { filter: brightness(1.1); transform: scaleY(1.03); }
.bar-tooltip {
  position: absolute;
  bottom: calc(100% + 6px);
  left: 50%;
  transform: translateX(-50%);
  background: var(--adm-text-primary);
  color: #fff;
  padding: 4px 8px;
  border-radius: 4px;
  font-size: 11px;
  white-space: nowrap;
  opacity: 0;
  pointer-events: none;
  transition: opacity 0.15s;
  z-index: 5;
}
.bar:hover .bar-tooltip { opacity: 1; }
.bar-label {
  font-size: 11px;
  color: var(--adm-text-tertiary);
  text-align: center;
  position: absolute;
  bottom: -22px;
  width: 100%;
}
.chart-legend {
  display: flex;
  gap: 20px;
  justify-content: center;
  margin-top: 30px;
  font-size: 12px;
  color: var(--adm-text-tertiary);
}
.legend-item { display: flex; align-items: center; gap: 6px; }
.legend-dot { width: 10px; height: 10px; border-radius: 2px; }
.legend-dot.blue { background: var(--adm-primary); }
.legend-dot.green { background: var(--adm-success); }

/* Task list */
.task-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
  margin-top: -4px;
}
.task-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 10px;
  border-radius: var(--adm-radius-md);
  cursor: pointer;
  transition: all 0.15s;
}
.task-item:hover { background: var(--adm-surface-subtle); }
.task-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  flex-shrink: 0;
}
.task-dot.orange { background: var(--adm-warning); box-shadow: 0 0 0 3px var(--adm-warning-bg); }
.task-dot.red { background: var(--adm-error); box-shadow: 0 0 0 3px var(--adm-error-bg); }
.task-dot.yellow { background: #eab308; box-shadow: 0 0 0 3px #fef9c3; }
.task-dot.blue { background: var(--adm-primary); box-shadow: 0 0 0 3px var(--adm-primary-bg); }
.task-dot.purple { background: #8b5cf6; box-shadow: 0 0 0 3px #f3e8ff; }
.task-icon {
  width: 34px;
  height: 34px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  background: var(--adm-surface-subtle);
  color: var(--adm-text-secondary);
}
.task-info { flex: 1; min-width: 0; }
.task-name {
  font-size: 13px;
  color: var(--adm-text-primary);
  font-weight: 500;
  margin-bottom: 2px;
}
.task-desc { font-size: 12px; color: var(--adm-text-tertiary); }
.task-count {
  background: var(--adm-surface-subtle);
  color: var(--adm-text-secondary);
  font-size: 12px;
  font-weight: 600;
  padding: 3px 10px;
  border-radius: 12px;
  margin-right: 4px;
}
.task-count.highlight { background: var(--adm-error-bg); color: var(--adm-error); }
.task-action {
  font-size: 12px;
  color: var(--adm-primary);
  font-weight: 500;
  white-space: nowrap;
  padding: 4px 10px;
  border-radius: 6px;
  transition: all 0.15s;
}
.task-item:hover .task-action { background: var(--adm-primary-bg); }

/* Row 3 */
.row-3 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-bottom: 20px;
}

/* Subject Distribution */
.subject-chart { margin-top: 4px; }
.subject-row {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
}
.subject-row:last-child { margin-bottom: 0; }
.subject-name {
  width: 44px;
  font-size: 13px;
  color: var(--adm-text-secondary);
  font-weight: 500;
  text-align: right;
  flex-shrink: 0;
}
.subject-bar-wrap {
  flex: 1;
  height: 26px;
  background: var(--adm-surface-subtle);
  border-radius: 6px;
  overflow: hidden;
  position: relative;
}
.subject-bar {
  height: 100%;
  border-radius: 6px;
  transition: width 0.8s cubic-bezier(0.34, 1.56, 0.64, 1);
  display: flex;
  align-items: center;
  padding-left: 10px;
  position: relative;
  min-width: 36px;
}
.subject-bar-val {
  font-size: 12px;
  font-weight: 600;
  color: #fff;
  text-shadow: 0 1px 2px rgba(0,0,0,0.2);
}
.subj-english { background: linear-gradient(90deg, #34d399, #10b981); }
.subj-math { background: linear-gradient(90deg, #60a5fa, #3b82f6); }
.subj-chinese { background: linear-gradient(90deg, #fb923c, #f97316); }
.subj-physics { background: linear-gradient(90deg, #a78bfa, #8b5cf6); }
.subj-chemistry { background: linear-gradient(90deg, #f0abfc, #ec4899); }
.subj-biology { background: linear-gradient(90deg, #86efac, #22c55e); }
.subj-history { background: linear-gradient(90deg, #fca5a5, #ef4444); }
.subj-geography { background: linear-gradient(90deg, #7dd3fc, #0ea5e9); }
.subj-politics { background: linear-gradient(90deg, #c4b5fd, #7c3aed); }
.subject-count {
  width: 48px;
  text-align: right;
  font-size: 13px;
  color: var(--adm-text-secondary);
  font-weight: 600;
  flex-shrink: 0;
}

/* Activity Feed */
.activity-list { margin-top: -4px; }
.activity-item {
  display: flex;
  gap: 12px;
  padding: 12px 0;
  position: relative;
}
.activity-item:not(:last-child)::after {
  content: '';
  position: absolute;
  left: 17px;
  top: 42px;
  bottom: -2px;
  width: 2px;
  background: var(--adm-border);
}
.activity-avatar {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 600;
  font-size: 13px;
  color: #fff;
  flex-shrink: 0;
  position: relative;
  z-index: 1;
}
.av-blue { background: linear-gradient(135deg, #60a5fa, #3b82f6); }
.av-green { background: linear-gradient(135deg, #34d399, #10b981); }
.av-orange { background: linear-gradient(135deg, #fb923c, #f97316); }
.av-purple { background: linear-gradient(135deg, #a78bfa, #8b5cf6); }
.av-pink { background: linear-gradient(135deg, #f0abfc, #ec4899); }
.activity-body { flex: 1; min-width: 0; padding-top: 2px; }
.activity-text {
  font-size: 13px;
  color: var(--adm-text-primary);
  line-height: 1.6;
  margin-bottom: 2px;
}
.activity-text :deep(.name) { font-weight: 600; color: var(--adm-text-primary); }
.activity-text :deep(.target) { color: var(--adm-primary); font-weight: 500; }
.activity-text :deep(.action) { color: var(--adm-text-secondary); }
.activity-text :deep(.obj) { font-weight: 500; color: var(--adm-text-primary); }
.activity-time { font-size: 11px; color: var(--adm-text-muted); }

/* Row 4: Quick Actions */
.row-4 { margin-bottom: 8px; }
.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}
.section-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--adm-text-primary);
}
.quick-actions-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}
.quick-action {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 18px;
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-lg);
  cursor: pointer;
  transition: all 0.2s ease;
}
.quick-action:hover {
  border-color: var(--adm-primary);
  box-shadow: 0 6px 16px rgba(59,130,246,0.12);
  transform: translateY(-2px);
}
.qa-icon {
  width: 44px;
  height: 44px;
  border-radius: var(--adm-radius-md);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.qa-icon.blue { background: var(--adm-primary-bg); color: var(--adm-primary); }
.qa-icon.green { background: var(--adm-success-bg); color: var(--adm-success); }
.qa-icon.orange { background: var(--adm-warning-bg); color: var(--adm-warning); }
.qa-icon.purple { background: #f3e8ff; color: #8b5cf6; }
.qa-text { flex: 1; min-width: 0; }
.qa-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--adm-text-primary);
  margin-bottom: 3px;
}
.qa-desc { font-size: 12px; color: var(--adm-text-tertiary); }
.qa-arrow {
  color: var(--adm-text-muted);
  transition: all 0.15s;
  flex-shrink: 0;
}
.quick-action:hover .qa-arrow {
  color: var(--adm-primary);
  transform: translateX(3px);
}

/* Responsive */
@media (max-width: 1200px) {
  .kpi-grid { grid-template-columns: repeat(2, 1fr); }
  .row-2, .row-3 { grid-template-columns: 1fr; }
  .quick-actions-grid { grid-template-columns: repeat(2, 1fr); }
}
</style>
