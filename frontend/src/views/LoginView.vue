<template>
  <div class="login-page">
    <div class="login-card">
      <div class="login-brand">
        <span class="login-mark">若</span>
        <div>
          <div class="login-brand-title">若愚智库</div>
          <div class="login-brand-sub">管理后台</div>
        </div>
      </div>

      <h1 class="login-title">管理员登录</h1>

      <el-form class="login-form" label-position="top" @submit.prevent="handleLogin">
        <el-form-item label="用户名 / 手机号" for="username">
          <el-input
            id="username"
            v-model="form.username"
            size="large"
            placeholder="请输入用户名"
            autocomplete="username"
            :prefix-icon="User"
          />
        </el-form-item>

        <el-form-item label="密码" for="password">
          <el-input
            id="password"
            v-model="form.password"
            size="large"
            type="password"
            show-password
            placeholder="请输入密码"
            autocomplete="current-password"
            :prefix-icon="Lock"
          />
        </el-form-item>

        <el-alert
          v-if="error"
          class="login-error"
          type="error"
          :title="error"
          :closable="false"
          show-icon
        />

        <el-checkbox v-model="form.remember" class="login-remember">记住用户名</el-checkbox>

        <el-button
          class="login-submit"
          type="primary"
          size="large"
          native-type="submit"
          :loading="loading"
          :disabled="!canSubmit"
        >
          登录
        </el-button>
      </el-form>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { Lock, User } from '@element-plus/icons-vue'
import { login } from '../services/auth'

const router = useRouter()
const loading = ref(false)
const error = ref('')

const form = reactive({
  username: '',
  password: '',
  remember: true,
})

const canSubmit = computed(() => !!(form.username.trim() && form.password))

const handleLogin = async () => {
  if (!canSubmit.value || loading.value) return
  loading.value = true
  error.value = ''
  try {
    await login(form.username.trim(), form.password)
    if (form.remember) {
      localStorage.setItem('adminUsername', form.username.trim())
    } else {
      localStorage.removeItem('adminUsername')
    }
    router.push('/dashboard')
  } catch (e) {
    error.value = e instanceof Error ? e.message : '登录失败'
  } finally {
    loading.value = false
  }
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
.login-remember {
  margin-bottom: var(--adm-space-4);
}
.login-submit {
  width: 100%;
}
</style>
