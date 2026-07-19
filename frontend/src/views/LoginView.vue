<template>
  <div class="login-page">
    <!-- Floating geometric shapes -->
    <div class="geo-shape s1"></div>
    <div class="geo-shape s2"></div>
    <div class="geo-shape s3"></div>

    <div class="login-wrapper adm-fade-in">
      <!-- Left Brand Panel -->
      <div class="login-brand">
        <div class="brand-top">
          <div class="brand-logo">
            <div class="brand-logo-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z"/>
                <path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z"/>
              </svg>
            </div>
            <div class="brand-logo-text">若愚智库</div>
          </div>
        </div>

        <div class="brand-center">
          <div class="brand-tagline">智能英语<br>学习平台</div>
          <div class="brand-desc">为教师与学生提供一站式错题管理、作业发布与学情分析服务，让教学更高效。</div>
          <div class="brand-features">
            <div class="brand-feature">
              <span class="brand-feature-dot"></span>
              全平台数据实时监控
            </div>
            <div class="brand-feature">
              <span class="brand-feature-dot"></span>
              用户权限精细化管理
            </div>
            <div class="brand-feature">
              <span class="brand-feature-dot"></span>
              OSS 存储审计与安全
            </div>
          </div>
        </div>

        <div class="brand-footer">
          <span class="version-badge">
            <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
            Admin Console v2.0
          </span>
        </div>
      </div>

      <!-- Right Login Form -->
      <div class="login-card">
        <div class="login-header">
          <div class="login-icon-wrap">
            <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
              <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
            </svg>
          </div>
          <h1 class="login-title">管理员登录</h1>
          <p class="login-subtitle">请输入您的管理员凭证</p>
        </div>

        <form @submit.prevent="handleLogin">
          <div class="form-group">
            <label class="form-label" for="username">用户名 / 手机号</label>
            <div class="input-wrap">
              <span class="input-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
              </span>
              <input
                id="username"
                v-model="form.username"
                type="text"
                class="form-input"
                placeholder="请输入用户名"
                autocomplete="username"
              />
            </div>
          </div>

          <div class="form-group">
            <label class="form-label" for="password">密码</label>
            <div class="input-wrap">
              <span class="input-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
              </span>
              <input
                id="password"
                v-model="form.password"
                :type="showPassword ? 'text' : 'password'"
                class="form-input"
                placeholder="请输入密码"
                autocomplete="current-password"
                @keyup.enter="handleLogin"
              />
              <span class="input-toggle" title="显示/隐藏密码" @click="showPassword = !showPassword">
                <svg v-if="!showPassword" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>
                <svg v-else width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>
              </span>
            </div>
          </div>

          <!-- Error alert -->
          <div v-if="error" class="error-alert">
            <span class="error-alert-icon">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
            </span>
            <span>{{ error }}</span>
          </div>

          <div class="form-options">
            <label class="checkbox-wrap">
              <input v-model="form.remember" type="checkbox" />
              <span class="custom-checkbox">
                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
              </span>
              记住我
            </label>
            <span class="forgot-link">忘记密码？</span>
          </div>

          <button
            type="submit"
            class="login-btn"
            :class="{ loading: loading }"
            :disabled="!canSubmit || loading"
          >
            <span class="spinner"></span>
            <span class="btn-text">登 录</span>
          </button>
        </form>

        <div class="login-footer">
          © 2024 若愚智库 · English Learning Platform
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { login } from '../services/auth'

const router = useRouter()
const loading = ref(false)
const error = ref('')
const showPassword = ref(false)

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
  position: relative;
  min-height: 100vh;
  background: linear-gradient(135deg, #1e293b 0%, #1e3a5f 40%, #2563eb 100%);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'PingFang SC', 'Hiragino Sans GB', 'Microsoft YaHei', sans-serif;
}

.login-page::before {
  content: '';
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(rgba(255,255,255,0.03) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255,255,255,0.03) 1px, transparent 1px);
  background-size: 48px 48px;
  -webkit-mask-image: radial-gradient(ellipse at center, black 30%, transparent 75%);
  mask-image: radial-gradient(ellipse at center, black 30%, transparent 75%);
  pointer-events: none;
}

.geo-shape {
  position: absolute;
  border-radius: 50%;
  filter: blur(60px);
  opacity: 0.3;
  pointer-events: none;
}
.geo-shape.s1 { width: 400px; height: 400px; background: #3b82f6; top: -100px; right: -80px; }
.geo-shape.s2 { width: 300px; height: 300px; background: #6366f1; bottom: -80px; left: -60px; }
.geo-shape.s3 { width: 200px; height: 200px; background: #0ea5e9; top: 40%; left: 15%; opacity: 0.2; }

.login-wrapper {
  display: flex;
  width: 900px;
  max-width: 95vw;
  background: #fff;
  border-radius: 20px;
  box-shadow: 0 32px 80px -12px rgba(0,0,0,0.35), 0 0 0 1px rgba(255,255,255,0.08);
  overflow: hidden;
  position: relative;
  z-index: 1;
  min-height: 540px;
}

/* ========== Left Brand Panel ========== */
.login-brand {
  flex: 1;
  background: linear-gradient(160deg, #0f172a 0%, #1e3a5f 60%, #1e40af 100%);
  padding: 48px 40px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  color: #fff;
  position: relative;
  overflow: hidden;
}
.login-brand::before {
  content: '';
  position: absolute;
  inset: 0;
  background-image:
    linear-gradient(rgba(255,255,255,0.04) 1px, transparent 1px),
    linear-gradient(90deg, rgba(255,255,255,0.04) 1px, transparent 1px);
  background-size: 32px 32px;
  opacity: 0.6;
  pointer-events: none;
}
.login-brand > * { position: relative; z-index: 1; }

.brand-logo {
  display: flex;
  align-items: center;
  gap: 12px;
}
.brand-logo-icon {
  width: 44px;
  height: 44px;
  background: linear-gradient(135deg, #3b82f6, #60a5fa);
  border-radius: 12px;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 8px 20px rgba(59,130,246,0.4);
  color: #fff;
}
.brand-logo-text {
  font-size: 22px;
  font-weight: 700;
  letter-spacing: 1px;
}

.brand-center { margin-top: 60px; }
.brand-tagline {
  font-size: 28px;
  font-weight: 700;
  line-height: 1.4;
  margin-bottom: 12px;
  background: linear-gradient(135deg, #fff 0%, #93c5fd 100%);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
  background-clip: text;
}
.brand-desc {
  font-size: 14px;
  color: rgba(255,255,255,0.6);
  line-height: 1.8;
  max-width: 280px;
}
.brand-features {
  margin-top: 32px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.brand-feature {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 13px;
  color: rgba(255,255,255,0.75);
}
.brand-feature-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: #60a5fa;
  box-shadow: 0 0 8px rgba(96,165,250,0.6);
  flex-shrink: 0;
}

.brand-footer {
  font-size: 12px;
  color: rgba(255,255,255,0.4);
  display: flex;
  align-items: center;
  gap: 8px;
}
.version-badge {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 4px 10px;
  background: rgba(59,130,246,0.2);
  border: 1px solid rgba(59,130,246,0.3);
  border-radius: 6px;
  font-size: 11px;
  color: #93c5fd;
  font-weight: 500;
}

/* ========== Right Login Card ========== */
.login-card {
  width: 440px;
  padding: 48px 44px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  background: #fff;
}

.login-header { margin-bottom: 32px; }
.login-icon-wrap {
  width: 52px;
  height: 52px;
  background: linear-gradient(135deg, #eff6ff, #dbeafe);
  border-radius: 14px;
  display: flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 20px;
  color: var(--adm-primary);
}
.login-title {
  font-size: 24px;
  font-weight: 700;
  color: var(--adm-text-primary);
  margin: 0 0 6px;
}
.login-subtitle {
  font-size: 14px;
  color: var(--adm-text-tertiary);
  margin: 0;
}

.form-group { margin-bottom: 18px; }
.form-label {
  display: block;
  font-size: 13px;
  font-weight: 500;
  color: var(--adm-text-secondary);
  margin-bottom: 8px;
}
.input-wrap {
  position: relative;
  display: flex;
  align-items: center;
}
.input-icon {
  position: absolute;
  left: 14px;
  color: var(--adm-text-muted);
  display: flex;
  align-items: center;
  pointer-events: none;
}
.form-input {
  width: 100%;
  height: 46px;
  padding: 0 14px 0 42px;
  border: 1.5px solid var(--adm-border);
  border-radius: 10px;
  font-size: 14px;
  color: var(--adm-text-primary);
  background: var(--adm-surface-subtle);
  transition: all 0.2s ease;
  outline: none;
  box-sizing: border-box;
}
.form-input:focus {
  border-color: var(--adm-primary);
  background: #fff;
  box-shadow: 0 0 0 4px rgba(59,130,246,0.1);
}
.form-input::placeholder { color: var(--adm-text-muted); }
.input-toggle {
  position: absolute;
  right: 12px;
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-muted);
  cursor: pointer;
  border-radius: 6px;
  transition: all 0.15s;
}
.input-toggle:hover {
  background: var(--adm-surface-subtle);
  color: var(--adm-text-secondary);
}

.form-options {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 24px;
}
.checkbox-wrap {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
  font-size: 13px;
  color: var(--adm-text-secondary);
  user-select: none;
}
.checkbox-wrap input { display: none; }
.custom-checkbox {
  width: 16px;
  height: 16px;
  border: 1.5px solid var(--adm-border);
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.15s;
  flex-shrink: 0;
  background: #fff;
}
.checkbox-wrap input:checked + .custom-checkbox {
  background: var(--adm-primary);
  border-color: var(--adm-primary);
}
.custom-checkbox svg { opacity: 0; transition: opacity 0.1s; }
.checkbox-wrap input:checked + .custom-checkbox svg { opacity: 1; }
.forgot-link {
  font-size: 13px;
  color: var(--adm-primary);
  font-weight: 500;
  cursor: pointer;
  transition: color 0.15s;
}
.forgot-link:hover { color: var(--adm-primary-dark); }

.error-alert {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 12px 14px;
  background: var(--adm-error-bg);
  border: 1px solid var(--adm-error-border);
  border-radius: 10px;
  margin-bottom: 20px;
  font-size: 13px;
  color: var(--adm-error);
  line-height: 1.5;
}
.error-alert-icon {
  flex-shrink: 0;
  margin-top: 1px;
  display: flex;
  align-items: center;
}

.login-btn {
  width: 100%;
  height: 46px;
  background: linear-gradient(135deg, var(--adm-primary), var(--adm-primary-dark));
  color: #fff;
  border: none;
  border-radius: 10px;
  font-size: 15px;
  font-weight: 600;
  letter-spacing: 4px;
  cursor: pointer;
  transition: all 0.2s ease;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  box-shadow: 0 4px 14px rgba(59,130,246,0.35);
}
.login-btn:hover:not(:disabled) {
  background: linear-gradient(135deg, var(--adm-primary-dark), #1d4ed8);
  box-shadow: 0 6px 20px rgba(59,130,246,0.45);
  transform: translateY(-1px);
}
.login-btn:active:not(:disabled) { transform: translateY(0); }
.login-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}
.spinner {
  width: 16px;
  height: 16px;
  border: 2px solid rgba(255,255,255,0.3);
  border-top-color: #fff;
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
  display: none;
}
.login-btn.loading .spinner { display: block; }
.login-btn.loading .btn-text { opacity: 0.8; }
@keyframes spin { to { transform: rotate(360deg); } }

.login-footer {
  margin-top: 28px;
  text-align: center;
  font-size: 12px;
  color: var(--adm-text-muted);
}

/* Responsive */
@media (max-width: 768px) {
  .login-wrapper { flex-direction: column; width: 95vw; }
  .login-brand { padding: 32px 28px; min-height: 200px; }
  .login-card { width: 100%; padding: 32px 28px; }
  .brand-center { margin-top: 24px; }
  .brand-features { display: none; }
}
</style>
