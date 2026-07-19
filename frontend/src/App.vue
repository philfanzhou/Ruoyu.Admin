<template>
  <div v-if="isLoginPage">
    <router-view />
  </div>
  <div v-else class="adm-layout">
    <!-- Sidebar -->
    <aside class="adm-sidebar">
      <div class="adm-sidebar-logo">
        <div class="logo-title">若愚智库</div>
        <div class="logo-sub">ADMIN CONSOLE</div>
      </div>

      <nav class="adm-sidebar-nav">
        <!-- 概览 -->
        <div class="nav-group">
          <div class="nav-group-label">概览</div>
          <router-link to="/dashboard" class="nav-item" :class="{ active: currentPath === '/dashboard' }">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></svg>
            <span>数据仪表盘</span>
          </router-link>
        </div>

        <!-- 用户管理 -->
        <div class="nav-group">
          <div class="nav-group-label">用户管理</div>
          <router-link to="/students" class="nav-item" :class="{ active: currentPath === '/students' }">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
            <span>学生管理</span>
          </router-link>
          <router-link to="/teachers" class="nav-item" :class="{ active: currentPath === '/teachers' }">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M22 10v6M2 10l10-5 10 5-10 5z"/><path d="M6 12v5c3 3 9 3 12 0v-5"/></svg>
            <span>教师管理</span>
          </router-link>
          <router-link to="/assistants" class="nav-item" :class="{ active: currentPath === '/assistants' }">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 11l-3-3m0 6l3-3"/></svg>
            <span>助教管理</span>
          </router-link>
        </div>

        <!-- 数据管理 -->
        <div class="nav-group">
          <div class="nav-group-label">数据管理</div>
          <router-link to="/upload-records" class="nav-item" :class="{ active: currentPath === '/upload-records' }">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/></svg>
            <span>上传记录</span>
          </router-link>
          <router-link to="/mistakes" class="nav-item" :class="{ active: currentPath === '/mistakes' }">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
            <span>错题管理</span>
          </router-link>
          <router-link to="/oss-audit" class="nav-item" :class="{ active: currentPath === '/oss-audit' }">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 7l-8-4-8 4 8 4 8-4z"/><path d="M4 7v10l8 4 8-4V7"/><path d="M12 11v10"/></svg>
            <span>OSS 审计</span>
          </router-link>
        </div>
      </nav>

      <div class="adm-sidebar-footer">
        <div class="adm-avatar">{{ userInitial }}</div>
        <div class="adm-user-info">
          <div class="adm-user-name">{{ username || 'Admin' }}</div>
          <div class="adm-user-role">管理员</div>
        </div>
        <button class="adm-logout-btn" title="退出登录" @click="handleLogout">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/></svg>
        </button>
      </div>
    </aside>

    <!-- Main -->
    <div class="adm-main">
      <header class="adm-header">
        <nav class="adm-breadcrumb">
          <router-link to="/dashboard">首页</router-link>
          <span class="sep">/</span>
          <span v-if="currentGroup" class="group">{{ currentGroup }}</span>
          <span v-if="currentGroup" class="sep">/</span>
          <span class="current">{{ currentTitle }}</span>
        </nav>
        <div class="adm-header-actions">
          <button class="adm-icon-btn" title="通知">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/></svg>
            <span class="adm-notif-badge">0</span>
          </button>
          <div class="adm-header-divider"></div>
          <div class="adm-header-user">
            <div class="adm-avatar" style="width: 28px; height: 28px; font-size: 12px;">{{ userInitial }}</div>
            <span class="name">{{ username || 'Admin' }}</span>
          </div>
        </div>
      </header>

      <main class="adm-content">
        <router-view />
      </main>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { clearAuth, getAuthToken } from './services/auth'
import httpClient from './services/httpClient'

const route = useRoute()
const router = useRouter()

const currentPath = computed(() => route.path)
const isLoginPage = computed(() => route.path === '/login')

// Read username from localStorage (set alongside token on login if available)
const username = computed(() => {
  try {
    return localStorage.getItem('adminUsername') || ''
  } catch {
    return ''
  }
})
const userInitial = computed(() => {
  const name = username.value || 'A'
  return name.charAt(0).toUpperCase()
})

const currentTitle = computed(() => (route.meta.title as string) || '管理后台')

const currentGroup = computed(() => {
  const path = route.path
  if (path === '/dashboard') return '概览'
  if (['/students', '/teachers', '/assistants'].includes(path)) return '用户管理'
  if (['/upload-records', '/mistakes', '/oss-audit'].includes(path)) return '数据管理'
  return ''
})

const handleLogout = async () => {
  // Notify backend to clear the adminAuthToken cookie (set by Login on success)
  // so subsequent browser-native <img> requests stop carrying the JWT.
  // localStorage tokens are cleared client-side by clearAuth(). Best-effort.
  if (getAuthToken()) {
    try {
      await httpClient.post('/api/auth/logout')
    } catch {
      // ignore — frontend state is the source of truth for UI navigation
    }
  }
  clearAuth()
  router.push('/login')
}
</script>

<style scoped>
.adm-layout {
  display: flex;
  min-height: 100vh;
  background: var(--adm-surface);
}

/* Sidebar */
.adm-sidebar {
  position: fixed;
  top: 0;
  left: 0;
  width: 240px;
  height: 100vh;
  background: var(--adm-sidebar);
  color: var(--adm-text-on-dark);
  display: flex;
  flex-direction: column;
  z-index: 100;
}
.adm-sidebar-logo {
  padding: 20px 20px 18px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
}
.adm-sidebar-logo .logo-title {
  font-size: 18px;
  font-weight: 700;
  color: #fff;
  letter-spacing: 0.5px;
}
.adm-sidebar-logo .logo-sub {
  font-size: 11px;
  color: var(--adm-text-on-dark-muted);
  margin-top: 2px;
  letter-spacing: 2px;
  font-weight: 500;
}
.adm-sidebar-nav {
  flex: 1;
  padding: 12px 10px;
  overflow-y: auto;
}
.nav-group {
  margin-bottom: 16px;
}
.nav-group-label {
  font-size: 11px;
  text-transform: uppercase;
  color: var(--adm-text-on-dark-muted);
  padding: 8px 12px 6px;
  letter-spacing: 1.2px;
  font-weight: 600;
}
.nav-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 12px;
  margin-bottom: 2px;
  border-radius: var(--adm-radius-md);
  color: var(--adm-text-on-dark);
  font-size: 14px;
  cursor: pointer;
  transition: all 0.15s var(--adm-ease);
  text-decoration: none;
  position: relative;
}
.nav-item:hover {
  background: var(--adm-sidebar-hover);
  color: #fff;
}
.nav-item.active {
  background: var(--adm-primary);
  color: #fff;
  font-weight: 500;
  box-shadow: 0 2px 8px rgba(59, 130, 246, 0.35);
}
.nav-item svg {
  flex-shrink: 0;
}
.adm-sidebar-footer {
  padding: 14px 16px;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
  display: flex;
  align-items: center;
  gap: 10px;
}
.adm-user-info {
  flex: 1;
  min-width: 0;
}
.adm-user-name {
  font-size: 13px;
  color: #fff;
  font-weight: 500;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.adm-user-role {
  font-size: 11px;
  color: var(--adm-text-on-dark-muted);
}
.adm-logout-btn {
  width: 30px;
  height: 30px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-on-dark-muted);
  transition: all 0.15s;
  cursor: pointer;
  background: transparent;
  border: none;
}
.adm-logout-btn:hover {
  background: rgba(255, 255, 255, 0.08);
  color: #fff;
}

/* Main */
.adm-main {
  margin-left: 240px;
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 100vh;
}

/* Header */
.adm-header {
  height: 56px;
  background: var(--adm-surface-elevated);
  border-bottom: 1px solid var(--adm-border);
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 24px;
  position: sticky;
  top: 0;
  z-index: 50;
}
.adm-breadcrumb {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  color: var(--adm-text-tertiary);
}
.adm-breadcrumb a {
  color: var(--adm-text-tertiary);
  transition: color 0.15s;
  text-decoration: none;
}
.adm-breadcrumb a:hover {
  color: var(--adm-primary);
}
.adm-breadcrumb .sep {
  color: var(--adm-text-muted);
  font-size: 12px;
}
.adm-breadcrumb .current {
  color: var(--adm-text-primary);
  font-weight: 500;
}
.adm-header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}
.adm-icon-btn {
  width: 36px;
  height: 36px;
  border-radius: var(--adm-radius-md);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-secondary);
  transition: all 0.15s;
  cursor: pointer;
  position: relative;
  background: transparent;
  border: none;
}
.adm-icon-btn:hover {
  background: var(--adm-surface-subtle);
  color: var(--adm-text-primary);
}
.adm-notif-badge {
  position: absolute;
  top: 4px;
  right: 4px;
  background: var(--adm-error);
  color: #fff;
  font-size: 10px;
  font-weight: 700;
  min-width: 16px;
  height: 16px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0 4px;
  border: 2px solid var(--adm-surface-elevated);
}
.adm-header-divider {
  width: 1px;
  height: 24px;
  background: var(--adm-border);
  margin: 0 4px;
}
.adm-header-user {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 8px 4px 4px;
  border-radius: var(--adm-radius-md);
  cursor: pointer;
  transition: background 0.15s;
}
.adm-header-user:hover {
  background: var(--adm-surface-subtle);
}
.adm-header-user .name {
  font-size: 13px;
  font-weight: 500;
  color: var(--adm-text-primary);
}

/* Content */
.adm-content {
  flex: 1;
  padding: 24px;
  max-width: 1400px;
  width: 100%;
  margin: 0 auto;
}
</style>
