<template>
  <div class="data-table-card">
    <!-- State precedence: loading, then error, then empty/data. -->
    <div v-if="error && !loading" class="data-table-error" role="alert">
      <el-result icon="error" title="加载失败" :sub-title="error">
        <template #extra>
          <el-button type="primary" :icon="Refresh" @click="emit('retry')">重试</el-button>
        </template>
      </el-result>
    </div>
    <el-table v-else v-loading="loading" :data="data" v-bind="$attrs">
      <slot />
      <template #empty>
        <el-empty v-if="!loading" :description="emptyText" :image-size="72" />
        <div v-else class="data-table-placeholder" />
      </template>
    </el-table>
    <div v-if="$slots.footer && !(error && !loading)" class="data-table-footer">
      <slot name="footer" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { Refresh } from '@element-plus/icons-vue'

defineOptions({ inheritAttrs: false })

withDefaults(
  defineProps<{
    data: unknown[]
    loading?: boolean
    error?: string | null
    emptyText?: string
  }>(),
  { loading: false, error: null, emptyText: '暂无数据' },
)

const emit = defineEmits<{
  retry: []
}>()
</script>

<style scoped>
.data-table-card {
  /* Size to the container, never to the columns: wide tables scroll inside the card
     instead of stretching the page (no page-level horizontal scroll). */
  contain: inline-size;
  overflow: hidden;
  background: var(--adm-surface-elevated);
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
}
.data-table-card :deep(.el-table th.el-table__cell) {
  background: var(--adm-surface-subtle);
  color: var(--adm-text-tertiary);
  font-size: 12px;
  font-weight: 600;
}
.data-table-placeholder {
  height: 120px;
}
.data-table-footer {
  display: flex;
  justify-content: flex-end;
  padding: 12px 20px;
  border-top: 1px solid var(--adm-border-light);
}
</style>
