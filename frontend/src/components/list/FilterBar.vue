<template>
  <div class="filter-bar">
    <el-form class="filter-bar-form" inline @submit.prevent>
      <slot />
    </el-form>
    <div class="filter-bar-actions">
      <el-button type="primary" :icon="Search" @click="emit('search')">搜索</el-button>
      <el-button :icon="RefreshLeft" @click="emit('reset')">重置</el-button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { RefreshLeft, Search } from '@element-plus/icons-vue'

const emit = defineEmits<{
  search: []
  reset: []
}>()
</script>

<style scoped>
.filter-bar {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 16px;
  padding: 16px 20px 4px;
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
}
.filter-bar-form {
  display: flex;
  flex-wrap: wrap;
  flex: 1;
  min-width: 0;
}
.filter-bar-form :deep(.el-form-item) {
  margin-right: 12px;
  margin-bottom: 12px;
}
.filter-bar-actions {
  display: flex;
  margin-bottom: 12px;
}
</style>
