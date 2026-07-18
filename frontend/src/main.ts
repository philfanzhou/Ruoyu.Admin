import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import axios from 'axios'
import App from './App.vue'
import router from './router'
import { clearAuth } from './services/auth'
import './style.css'

// Redirect to login when the backend rejects the JWT (401).
axios.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      clearAuth()
      router.push('/login')
    }
    return Promise.reject(error)
  },
)

const app = createApp(App)
app.use(ElementPlus)
app.use(router)
app.mount('#app')
