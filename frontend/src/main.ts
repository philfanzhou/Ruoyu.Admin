import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import 'element-plus/dist/index.css'
import './styles/tokens.css'
import './styles/element-theme.css'
import './style.css'
import App from './App.vue'
import router from './router'

// Session invalidation and CSRF are centralized in services/auth.ts and httpClient.ts.

const app = createApp(App)
app.use(ElementPlus, { locale: zhCn })
app.use(router)
app.mount('#app')
