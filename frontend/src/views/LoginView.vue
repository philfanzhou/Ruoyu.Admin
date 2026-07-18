<template>
  <div class="login-container">
    <el-card class="login-card">
      <h2 class="login-title">管理后台登录</h2>
      <el-form :model="form" label-position="top" @submit.prevent="handleLogin">
        <el-form-item label="用户名">
          <el-input v-model="form.username" placeholder="请输入用户名" autocomplete="username" />
        </el-form-item>
        <el-form-item label="密码">
          <el-input
            v-model="form.password"
            type="password"
            placeholder="请输入密码"
            autocomplete="current-password"
            show-password
            @keyup.enter="handleLogin"
          />
        </el-form-item>
        <el-button
          type="primary"
          :loading="loading"
          :disabled="!canSubmit"
          @click="handleLogin"
          class="login-button"
        >
          登录
        </el-button>
        <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
      </el-form>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { login } from '../services/auth'

const router = useRouter()
const loading = ref(false)
const error = ref('')

const form = reactive({
  username: '',
  password: '',
})

const canSubmit = computed(() => form.username.trim() && form.password)

const handleLogin = async () => {
  if (!canSubmit.value) return
  loading.value = true
  error.value = ''
  try {
    await login(form.username, form.password)
    router.push('/students')
  } catch (e) {
    error.value = e instanceof Error ? e.message : '登录失败'
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-container {
  display: flex;
  justify-content: center;
  align-items: center;
  height: 100vh;
  background: #f5f5f5;
}

.login-card {
  width: 380px;
}

.login-title {
  text-align: center;
  margin: 0 0 24px;
  color: #304156;
}

.login-button {
  width: 100%;
  margin-top: 8px;
}
</style>
