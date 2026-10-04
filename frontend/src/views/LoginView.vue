<template>
  <div class="login-page">
    <div class="login-card">
      <div class="login-brand">
        <span class="login-mark">若</span>
        <div><div class="login-brand-title">若愚智库</div><div class="login-brand-sub">管理后台</div></div>
      </div>
      <h1 class="login-title">{{ title }}</h1>
      <el-alert class="login-error" :title="message" :type="session.status === 'forbidden' ? 'error' : 'info'" :closable="false" show-icon />
      <el-alert v-if="session.logoutNotice" class="login-error" :title="session.logoutNotice" type="warning" :closable="false" show-icon />
      <div class="login-actions">
        <el-button v-if="canSignIn" class="login-submit" tag="a" :href="hostedLoginUrl(returnUrl)" type="primary" size="large" :disabled="session.signingOut">前往身份服务登录</el-button>
        <el-button v-if="session.status === 'authenticated'" type="primary" @click="router.replace(returnUrl)">返回管理页面</el-button>
        <el-button :loading="checking" :disabled="checking || session.signingOut" @click="checkSession">重新检查会话</el-button>
        <el-button v-if="canSignOut" :loading="session.signingOut" :disabled="session.signingOut" @click="handleLogout">退出当前会话</el-button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { hostedLoginUrl, initializeSession, logoutSession, safeReturnUrl, session } from '../services/auth'

const route = useRoute()
const router = useRouter()
const checking = ref(false)
const returnUrl = computed(() => safeReturnUrl(route.query.returnUrl))
const canSignIn = computed(() => session.status === 'anonymous' || session.status === 'reauthentication')
const canSignOut = computed(() => ['authenticated', 'reauthentication', 'forbidden'].includes(session.status))
const title = computed(() => session.status === 'forbidden' ? '无管理权限' : session.status === 'reauthentication' ? '需要重新认证' : '管理员登录')
const message = computed(() => {
  if (session.status === 'forbidden') return '当前账户没有管理权限。可以退出本人会话后使用其他账户。'
  if (session.status === 'reauthentication') return '登录凭据已到期，请主动重新认证；之前的操作不会自动重试。'
  if (session.status === 'unavailable') return '会话服务不可用，请重新检查。'
  if (session.status === 'unknown') return '正在确认当前会话。'
  if (session.status === 'authenticated') return '当前管理员会话有效。'
  const errors: Record<string, string> = { cancelled: '已取消登录，可以再次前往身份服务。', sign_in_failed: '登录未完成，请重新登录。', identity_unavailable: '身份服务暂时不可用，请稍后重试。' }
  if (typeof route.query.authError === 'string') return errors[route.query.authError] ?? '登录未完成，请重试。'
  if (route.query.loggedOut === '1') return '已退出管理后台。'
  return '请前往身份服务完成登录。此页面不收集密码。'
})
const checkSession = async () => {
  if (checking.value) return
  checking.value = true
  try { if (await initializeSession(true) === 'authenticated') await router.replace(returnUrl.value) }
  finally { checking.value = false }
}
const handleLogout = async () => {
  const result = await logoutSession()
  if (result.kind === 'redirect') window.location.assign(result.url)
  else if (session.status !== 'authenticated') await router.replace('/login')
}
</script>

<style scoped>
.login-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: var(--adm-space-6) var(--adm-space-4);
  background: var(--adm-surface);
}
.login-card {
  width: 100%;
  max-width: 400px;
  padding: var(--adm-space-8);
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-lg);
  box-shadow: var(--adm-shadow-md);
}
.login-brand {
  display: flex;
  align-items: center;
  gap: var(--adm-space-3);
  margin-bottom: var(--adm-space-6);
}
.login-mark {
  width: 40px;
  height: 40px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: var(--adm-radius-md);
  background: var(--adm-sidebar);
  color: #fff;
  font-size: var(--adm-font-size-lg);
  font-weight: 700;
}
.login-brand-title {
  color: var(--adm-text-primary);
  font-size: var(--adm-font-size-md);
  font-weight: 600;
  line-height: 1.3;
}
.login-brand-sub {
  color: var(--adm-text-tertiary);
  font-size: var(--adm-font-size-xs);
}
.login-title {
  margin: 0 0 var(--adm-space-5);
  color: var(--adm-text-primary);
  font-size: var(--adm-font-size-xl);
  font-weight: 600;
}
.login-error {
  margin-bottom: var(--adm-space-3);
}
.login-actions { display: grid; gap: var(--adm-space-3); }
.login-submit {
  width: 100%;
}
</style>
