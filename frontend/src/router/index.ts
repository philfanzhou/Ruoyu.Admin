import { markRaw } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import { Box, Collection, DataBoard, Reading, Service, Upload, User } from '@element-plus/icons-vue'
import { initializeSession, onSessionInvalidated, safeReturnUrl, session } from '../services/auth'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('../views/LoginView.vue'),
      meta: { title: '登录' }
    },
    {
      path: '/',
      redirect: '/dashboard'
    },
    {
      path: '/dashboard',
      name: 'dashboard',
      component: () => import('../views/DashboardView.vue'),
      meta: { title: '数据仪表盘', group: 'overview', icon: markRaw(DataBoard) }
    },
    {
      path: '/students',
      name: 'students',
      component: () => import('../views/StudentView.vue'),
      meta: { title: '学生管理', group: 'users', icon: markRaw(User) }
    },
    {
      path: '/teachers',
      name: 'teachers',
      component: () => import('../views/TeacherView.vue'),
      meta: { title: '教师管理', group: 'users', icon: markRaw(Reading) }
    },
    {
      path: '/assistants',
      name: 'assistants',
      component: () => import('../views/AssistantView.vue'),
      meta: { title: '助教管理', group: 'users', icon: markRaw(Service) }
    },
    {
      path: '/upload-records',
      name: 'upload-records',
      component: () => import('../views/UploadRecordView.vue'),
      meta: { title: '上传记录', group: 'content', icon: markRaw(Upload) }
    },
    {
      path: '/mistakes',
      name: 'mistakes',
      component: () => import('../views/MistakeView.vue'),
      meta: { title: '错题管理', group: 'content', icon: markRaw(Collection) }
    },
    {
      path: '/oss-audit',
      name: 'oss-audit',
      component: () => import('../views/OssAuditView.vue'),
      meta: { title: 'OSS 审计', group: 'storage', icon: markRaw(Box) }
    }
  ]
})

onSessionInvalidated(() => {
  if (router.currentRoute.value.path !== '/login') {
    void router.replace({ name: 'login', query: { returnUrl: safeReturnUrl(router.currentRoute.value.fullPath) } })
  }
})

router.beforeEach(async to => {
  document.title = `${to.meta.title || '管理后台'} - Admin Portal`
  await initializeSession()
  if (to.name !== 'login' && session.status !== 'authenticated')
    return { name: 'login', query: { returnUrl: safeReturnUrl(to.fullPath) } }
  // Callback status and logout completion stay visible until the user acts.
  if (to.name === 'login' && session.status === 'authenticated' && !to.query.authError && !to.query.loggedOut)
    return safeReturnUrl(to.query.returnUrl)
  return true
})

export default router
