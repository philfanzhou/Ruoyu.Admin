<template>
  <el-pagination
    :current-page="page"
    :page-size="pageSize"
    :total="total"
    :page-sizes="[10, 20, 50]"
    layout="total, sizes, prev, pager, next"
    background
    @current-change="onPageChange"
    @size-change="onSizeChange"
  />
</template>

<script setup lang="ts">
defineProps<{
  page: number
  pageSize: number
  total: number
}>()

const emit = defineEmits<{
  'update:page': [value: number]
  'update:pageSize': [value: number]
  /** Fired once per user interaction, after page/pageSize are updated. */
  change: []
}>()

function onPageChange(value: number) {
  emit('update:page', value)
  emit('change')
}

// Changing the page size always returns to the first page.
function onSizeChange(value: number) {
  emit('update:pageSize', value)
  emit('update:page', 1)
  emit('change')
}
</script>
