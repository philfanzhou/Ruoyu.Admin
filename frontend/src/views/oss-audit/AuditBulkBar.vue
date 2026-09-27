<template>
  <transition name="bulk-bar">
    <div v-if="count > 0" class="bulk-bar" role="region" aria-label="批量操作">
      <div class="bulk-bar-info">已选择 <strong>{{ count }}</strong> 个文件</div>
      <el-button type="danger" size="small" :icon="Delete" :loading="loading" @click="emit('resolve')">
        {{ loading ? '处理中...' : '批量删除' }}
      </el-button>
      <el-button type="warning" plain size="small" :icon="CircleClose" :disabled="loading" @click="emit('ignore')">批量忽略</el-button>
      <el-button text size="small" class="bulk-cancel" :disabled="loading" @click="emit('cancel')">取消</el-button>
    </div>
  </transition>
</template>

<script setup lang="ts">
import { CircleClose, Delete } from '@element-plus/icons-vue'

defineProps<{
  count: number
  loading: boolean
}>()

const emit = defineEmits<{
  resolve: []
  ignore: []
  cancel: []
}>()
</script>

<style scoped>
.bulk-bar {
  position: fixed;
  bottom: 24px;
  left: 50%;
  transform: translateX(-50%);
  background: var(--adm-text-primary);
  border-radius: var(--adm-radius-lg);
  padding: 10px 16px 10px 20px;
  display: flex;
  align-items: center;
  gap: 12px;
  box-shadow: 0 12px 32px rgba(15, 23, 42, 0.25);
  z-index: 90;
}
.bulk-bar .el-button + .el-button { margin-left: 0; }
.bulk-bar-info {
  color: #fff;
  font-size: 13px;
  font-weight: 500;
  white-space: nowrap;
}
.bulk-cancel { color: #cbd5e1; }
.bulk-cancel:hover { color: #fff; }
.bulk-bar-enter-active,
.bulk-bar-leave-active { transition: opacity 0.2s, transform 0.2s; }
.bulk-bar-enter-from,
.bulk-bar-leave-to { opacity: 0; transform: translate(-50%, 12px); }
</style>
