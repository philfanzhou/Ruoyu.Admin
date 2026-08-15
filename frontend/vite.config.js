import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve } from 'path'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src'),
    },
  },
  server: {
    host: '0.0.0.0',
    port: 8090,
    proxy: {
      '/api/identity': {
        target: 'http://localhost:5020',
        changeOrigin: true,
      },
      '/api/admin': {
        target: 'http://localhost:5020',
        changeOrigin: true,
      },
      '/api/teacher-portal': {
        target: 'http://localhost:5020',
        changeOrigin: true,
      },
      '/api/assistant-portal': {
        target: 'http://localhost:5020',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    assetsDir: 'assets',
    sourcemap: false,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (id.includes('node_modules/vue')) return 'vue'
          if (id.includes('node_modules/element-plus')) return 'element-plus'
        },
        chunkFileNames: 'assets/js/[name]-[hash].js',
        entryFileNames: 'assets/js/[name]-[hash].js',
        assetFileNames: 'assets/[ext]/[name]-[hash].[ext]',
      },
    },
  },
})
