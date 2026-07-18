<template>
  <div class="app-container">
    <el-container>
      <el-aside width="200px">
        <div class="logo">
          <h3>管理后台</h3>
          <p>Admin Portal</p>
        </div>
        <el-menu
          :default-active="currentRoute"
          router
          class="sidebar-menu"
        >
          <el-menu-item index="/students">
            <el-icon><svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z"/></svg></el-icon>
            <span>学生管理</span>
          </el-menu-item>
          <el-menu-item index="/teachers">
            <el-icon><svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M5 13.18v4L12 21l7-3.82v-4L12 17l-7-3.82zM12 3L1 9l11 6 9-4.91V17h2V9L12 3z"/></svg></el-icon>
            <span>教师管理</span>
          </el-menu-item>
          <el-menu-item index="/assistants">
            <el-icon><svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M16 11c1.66 0 2.99-1.34 2.99-3S17.66 5 16 5c-1.66 0-3 1.34-3 3s1.34 3 3 3zm-8 0c1.66 0 2.99-1.34 2.99-3S9.66 5 8 5C6.34 5 5 6.34 5 8s1.34 3 3 3zm0 2c-2.33 0-7 1.17-7 3.5V19h14v-2.5c0-2.33-4.67-3.5-7-3.5zm8 0c-.29 0-.62.02-.97.05 1.16.84 1.97 1.97 1.97 3.45V19h6v-2.5c0-2.33-4.67-3.5-7-3.5z"/></svg></el-icon>
            <span>助教管理</span>
          </el-menu-item>
          <el-sub-menu index="data-management">
            <template #title>
              <el-icon><svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M12 3L2 9l10 6 10-6-10-6zm0 8L2 9l10 6 10-6M12 19l-10-6v6l10 6 10-6v-6"/></svg></el-icon>
              <span>数据管理</span>
            </template>
            <el-menu-item index="/upload-records">
              <el-icon><svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor"><path d="M9 16h6v-6h4l-7-7-7 7h4zm-4 2h14v2H5z"/></svg></el-icon>
              <span>上传记录</span>
            </el-menu-item>
            <el-menu-item index="/oss-audit">
              <el-icon><svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-2 15l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z"/></svg></el-icon>
              <span>OSS 审计</span>
            </el-menu-item>
          </el-sub-menu>
          <el-menu-item index="/mistakes">
            <el-icon><svg viewBox="0 0 24 24" width="16" height="16" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"/></svg></el-icon>
            <span>错题管理</span>
          </el-menu-item>
        </el-menu>
        <div class="sidebar-footer">
          <el-button type="danger" size="small" plain @click="handleLogout">退出登录</el-button>
        </div>
      </el-aside>
      <el-container>
        <el-main>
          <router-view />
        </el-main>
      </el-container>
    </el-container>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { clearAuth } from './services/auth'

const route = useRoute()
const router = useRouter()
const currentRoute = computed(() => route.path)

const handleLogout = () => {
  clearAuth()
  router.push('/login')
}
</script>

<style scoped>
.app-container {
  height: 100vh;
  background: #f5f5f5;
}

.el-aside {
  background-color: #304156;
  color: #fff;
  height: 100vh;
  overflow-y: auto;
}

.logo {
  padding: 20px 16px;
  text-align: center;
  border-bottom: 1px solid #4a5568;
}

.logo h3 {
  margin: 0;
  color: #fff;
  font-size: 18px;
  font-weight: 600;
}

.logo p {
  margin: 4px 0 0;
  color: #a0aec0;
  font-size: 12px;
}

.sidebar-menu {
  border-right: none;
  background-color: transparent;
}

.sidebar-menu .el-menu-item {
  color: #e2e8f0;
  height: 50px;
  line-height: 50px;
}

.sidebar-menu .el-menu-item:hover,
.sidebar-menu .el-menu-item.is-active {
  background-color: #263445;
  color: #409eff;
}

.sidebar-menu :deep(.el-sub-menu__title) {
  color: #e2e8f0 !important;
  height: 50px;
  line-height: 50px;
}

.sidebar-menu :deep(.el-sub-menu__title:hover) {
  background-color: #263445 !important;
  color: #409eff !important;
}

.sidebar-menu :deep(.el-sub-menu__title .el-sub-menu__icon-arrow) {
  color: #a0aec0;
}

.sidebar-menu :deep(.el-sub-menu .el-menu-item) {
  background-color: #1f2d3d;
  color: #cbd5e0;
}

.sidebar-menu :deep(.el-sub-menu .el-menu-item:hover) {
  background-color: #263445;
  color: #409eff;
}

.sidebar-footer {
  padding: 16px;
  text-align: center;
  border-top: 1px solid #4a5568;
}

.el-main {
  padding: 20px;
  overflow-y: auto;
}
</style>
