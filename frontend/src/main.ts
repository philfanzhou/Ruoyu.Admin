import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import App from './App.vue'
import router from './router'
import './style.css'

// 401 handling for API requests is centralized in services/httpClient.ts.
// The previous global axios.interceptors.response.use(...) handler was removed
// because API clients use a shared axios.create() instance, and interceptors
// registered on the global `axios` do not apply to separate instances.

const app = createApp(App)
app.use(ElementPlus)
app.use(router)
app.mount('#app')
