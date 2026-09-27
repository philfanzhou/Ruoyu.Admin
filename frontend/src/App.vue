<template>
  <div v-if="isLoginPage">
    <router-view />
  </div>
  <div v-else class="adm-layout" :class="{ 'is-collapsed': collapsed }">
    <aside class="adm-sidebar">
      <div class="adm-sidebar-logo">
        <span class="logo-mark">若</span>
        <span v-if="!collapsed" class="logo-text">
          <span class="logo-title">若愚智库</span>
          <span class="logo-sub">管理后台</span>
        </span>
      </div>

      <el-scrollbar class="adm-sidebar-scroll">
        <el-menu
          class="adm-nav"
          :default-active="activeName"
          :collapse="collapsed"
          :collapse-transition="false"
          @select="handleSelect"
        >
          <el-menu-item-group v-for="group in navGroups" :key="group.key">
            <template #title>
              <span class="nav-group-label">{{ group.label }}</span>
            </template>
            <el-menu-item v-for="item in group.items" :key="item.name" :index="item.name">
              <el-icon><component :is="item.icon" /></el-icon>
              <template #title>{{ item.title }}</template>
            </el-menu-item>
          </el-menu-item-group>
        </el-menu>
      </el-scrollbar>
    </aside>

    <div class="adm-main">
      <header class="adm-header">
        <div class="adm-header-left">
          <el-button
            class="adm-collapse-btn"
            text
            :icon="collapsed ? Expand : Fold"
            :aria-label="collapsed ? '展开侧边栏' : '折叠侧边栏'"
            :title="collapsed ? '展开侧边栏' : '折叠侧边栏'"
            @click="toggle"
          />
          <el-breadcrumb separator="/">
            <el-breadcrumb-item :to="{ path: '/dashboard' }">首页</el-breadcrumb-item>
            <el-breadcrumb-item v-if="currentGroupLabel">{{ currentGroupLabel }}</el-breadcrumb-item>
            <el-breadcrumb-item>{{ currentTitle }}</el-breadcrumb-item>
          </el-breadcrumb>
        </div>
        <div class="adm-header-right">
          <span class="adm-header-user">
            <span class="adm-avatar">{{ userInitial }}</span>
            <span class="name">{{ username || 'Admin' }}</span>
          </span>
          <el-button text :icon="SwitchButton" @click="handleLogout">退出登录</el-button>
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
import { Expand, Fold, SwitchButton } from '@element-plus/icons-vue'
import { clearAuth, getAuthToken } from './services/auth'
import httpClient from './services/httpClient'
import { NAV_GROUPS } from './router/navigation'
import { useSidebar } from './composables/useSidebar'

const route = useRoute()
const router = useRouter()
const { collapsed, toggle } = useSidebar()

const isLoginPage = computed(() => route.path === '/login')

// Sidebar, groups and breadcrumb are all derived from route meta.
const navGroups = NAV_GROUPS.map((group) => ({
  ...group,
  items: router.options.routes
    .filter((r) => r.meta?.group === group.key && typeof r.name === 'string')
    .map((r) => ({ name: r.name as string, title: r.meta!.title ?? '', icon: r.meta!.icon })),
})).filter((group) => group.items.length > 0)

// Match by route name so links carrying a query string still highlight.
const activeName = computed(() => (typeof route.name === 'string' ? route.name : ''))

const currentTitle = computed(() => route.meta.title || '管理后台')
const currentGroupLabel = computed(
  () => NAV_GROUPS.find((group) => group.key === route.meta.group)?.label ?? '',
)

const handleSelect = (name: string) => {
  router.push({ name })
}

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
  --sidebar-w: var(--adm-sidebar-width);
  min-height: 100vh;
  background: var(--adm-surface);
}
.adm-layout.is-collapsed {
  --sidebar-w: var(--adm-sidebar-collapsed-width);
}

/* Sidebar */
.adm-sidebar {
  position: fixed;
  top: 0;
  left: 0;
  width: var(--sidebar-w);
  height: 100vh;
  display: flex;
  flex-direction: column;
  background: var(--adm-sidebar);
  color: var(--adm-text-on-dark);
  z-index: var(--adm-z-sidebar);
  transition: width 0.2s var(--adm-ease);
  overflow: hidden;
}
.adm-sidebar-logo {
  height: var(--adm-header-height);
  display: flex;
  align-items: center;
  gap: var(--adm-space-3);
  padding: 0 var(--adm-space-4);
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
  flex-shrink: 0;
}
.logo-mark {
  width: 32px;
  height: 32px;
  flex-shrink: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--adm-radius-md);
  background: var(--adm-primary);
  color: #fff;
  font-weight: 700;
  font-size: var(--adm-font-size-md);
}
.logo-text {
  display: flex;
  flex-direction: column;
  line-height: 1.25;
  white-space: nowrap;
}
.logo-title {
  color: #fff;
  font-size: var(--adm-font-size-md);
  font-weight: 600;
}
.logo-sub {
  color: var(--adm-text-on-dark-muted);
  font-size: var(--adm-font-size-xs);
}
.adm-sidebar-scroll {
  flex: 1;
}

.adm-nav {
  --el-menu-bg-color: var(--adm-sidebar);
  --el-menu-text-color: var(--adm-text-on-dark);
  --el-menu-hover-text-color: #fff;
  --el-menu-hover-bg-color: var(--adm-sidebar-hover);
  --el-menu-active-color: #fff;
  --el-menu-border-color: transparent;
  --el-menu-item-height: 40px;
  --el-menu-item-font-size: var(--adm-font-size-base);
  border-right: none;
  padding: var(--adm-space-2) var(--adm-space-2) var(--adm-space-4);
}
.adm-nav:not(.el-menu--collapse) {
  width: 100%;
}
.adm-nav :deep(.el-menu-item-group__title) {
  padding: var(--adm-space-3) var(--adm-space-3) var(--adm-space-1) !important;
}
.nav-group-label {
  color: var(--adm-text-on-dark-muted);
  font-size: var(--adm-font-size-xs);
  font-weight: 600;
  letter-spacing: 0.5px;
}
.adm-nav.el-menu--collapse :deep(.el-menu-item-group__title) {
  height: 1px;
  margin: var(--adm-space-2) var(--adm-space-3);
  padding: 0 !important;
  background: rgba(255, 255, 255, 0.08);
  overflow: hidden;
}
.adm-nav.el-menu--collapse .nav-group-label {
  display: none;
}
.adm-nav :deep(.el-menu-item) {
  margin-bottom: 2px;
  border-radius: var(--adm-radius-md);
}
.adm-nav :deep(.el-menu-item.is-active) {
  background: var(--adm-primary);
  font-weight: 500;
}
.adm-nav :deep(.el-menu-item:focus-visible) {
  outline: 2px solid var(--adm-primary-light);
  outline-offset: -2px;
}

/* Main */
.adm-main {
  margin-left: var(--sidebar-w);
  min-width: 0;
  min-height: 100vh;
  display: flex;
  flex-direction: column;
  transition: margin-left 0.2s var(--adm-ease);
}

/* Header */
.adm-header {
  position: sticky;
  top: 0;
  z-index: var(--adm-z-header);
  height: var(--adm-header-height);
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--adm-space-4);
  padding: 0 var(--adm-space-6) 0 var(--adm-space-3);
  background: var(--adm-surface-elevated);
  border-bottom: 1px solid var(--adm-border);
}
.adm-header-left,
.adm-header-right {
  display: flex;
  align-items: center;
  gap: var(--adm-space-2);
  min-width: 0;
}
.adm-collapse-btn {
  font-size: var(--adm-font-size-lg);
}
.adm-header-user {
  display: inline-flex;
  align-items: center;
  gap: var(--adm-space-2);
  padding-right: var(--adm-space-2);
  border-right: 1px solid var(--adm-border);
}
.adm-header-user .name {
  color: var(--adm-text-primary);
  font-size: var(--adm-font-size-sm);
  font-weight: 500;
  white-space: nowrap;
}
.adm-header-user .adm-avatar {
  width: 28px;
  height: 28px;
  font-size: var(--adm-font-size-xs);
}

/* Content */
.adm-content {
  flex: 1;
  width: 100%;
  max-width: var(--adm-content-max-width);
  margin: 0 auto;
  padding: var(--adm-space-6);
}
</style>
