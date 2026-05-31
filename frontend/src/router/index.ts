import { createRouter, createWebHistory } from 'vue-router'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/',
      redirect: '/students'
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
      path: '/legacy-data',
      name: 'legacy-data',
      component: () => import('../views/LegacyDataView.vue'),
      meta: { title: '历史遗留数据' }
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
  next()
})

export default router
