<template>
  <div class="page-header">
    <div class="page-header-text">
      <h1 class="page-header-title">{{ title }}</h1>
      <p v-if="description" class="page-header-desc">{{ description }}</p>
    </div>
    <div v-if="$slots.actions" class="page-header-actions">
      <slot name="actions" />
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  title: string
  description?: string
}>()
</script>

<style scoped>
.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}
.page-header-text {
  min-width: 0;
}
.page-header-title {
  margin: 0 0 4px;
  color: var(--adm-text-primary);
  font-size: 22px;
  font-weight: 700;
}
.page-header-desc {
  margin: 0;
  color: var(--adm-text-tertiary);
  font-size: 13px;
}
.page-header-actions {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
}
</style>
