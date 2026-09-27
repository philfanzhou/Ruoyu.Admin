<template>
  <el-tag :type="type" effect="light" disable-transitions>{{ label }}</el-tag>
</template>

<script setup lang="ts">
export type StatusTagType = 'success' | 'warning' | 'danger' | 'info' | 'primary'

defineProps<{
  label: string
  type: StatusTagType
}>()
</script>
