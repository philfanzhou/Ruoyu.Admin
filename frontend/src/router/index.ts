import { createRouter, createWebHistory } from 'vue-router'
import { isAuthenticated } from '../services/auth'

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
      meta: { title: '数据仪表盘' }
    },
    {
      path: '/students',
      name: 'students',
      component: () => import('../views/StudentView.vue'),
      meta: { title: '学生管理' }
    },
    {
      path: '/teachers',
      name: 'teachers',
      component: () => import('../views/TeacherView.vue'),
      meta: { title: '教师管理' }
    },
    {
      path: '/assistants',
      name: 'assistants',
      component: () => import('../views/AssistantView.vue'),
      meta: { title: '助教管理' }
    },
    {
      path: '/upload-records',
      name: 'upload-records',
      component: () => import('../views/UploadRecordView.vue'),
      meta: { title: '上传记录' }
    },
    {
      path: '/mistakes',
      name: 'mistakes',
      component: () => import('../views/MistakeView.vue'),
      meta: { title: '错题管理' }
    },
    {
      path: '/oss-audit',
      name: 'oss-audit',
      component: () => import('../views/OssAuditView.vue'),
      meta: { title: 'OSS 审计' }
    }
  ]
})

router.beforeEach((to, _from, next) => {
  document.title = `${to.meta.title || '管理后台'} - Admin Portal`

  // Redirect unauthenticated users to the login page.
  if (to.name !== 'login' && !isAuthenticated()) {
    next({ name: 'login' })
    return
  }

  // Authenticated users should not stay on the login page.
  if (to.name === 'login' && isAuthenticated()) {
    next('/dashboard')
    return
  }

  next()
})

export default router
