<template>
  <div class="section-title">
    <el-icon><Picture /></el-icon>
    题目图片
    <span class="section-count">共 {{ regions.length || imageCount || 0 }} 张</span>
  </div>
  <el-alert v-if="hasUploadPrefixImages" type="warning" :closable="false" show-icon class="migration-banner">
    <template #title>
      <span class="migration-text">检测到部分图片仍在 uploads/ 路径下，建议迁移到 mistakes/ 路径</span>
      <el-button type="warning" size="small" :loading="migrating" @click="emit('migrate')">
        {{ migrating ? '迁移中...' : '迁移图片' }}
      </el-button>
    </template>
  </el-alert>
  <template v-if="regions.length > 0">
    <div class="img-gallery">
      <div
        v-for="(region, idx) in regions"
        :key="idx"
        class="img-gallery-item"
        :class="{ selected: selectedIndex === idx }"
        @click="emit('update:selectedIndex', idx)"
      >
        <img :src="getMistakeImageUrl(region.sourceImagePath, 'medium')" :alt="`图片${idx + 1}`" class="gallery-img" @error="hideBrokenImage" />
        <span class="g-num">#{{ idx + 1 }}</span>
      </div>
    </div>
    <div class="img-preview-lg">
      <img :src="getMistakeImageUrl(selectedPath)" :alt="`预览${selectedIndex + 1}`" @error="dimBrokenPreview" />
      <div class="img-path-row">
        <span class="image-path-text" :title="selectedPath">{{ selectedPath }}</span>
        <el-button link type="primary" :icon="CopyDocument" @click="copyPath(selectedPath)">复制路径</el-button>
      </div>
    </div>
  </template>
  <div v-else class="no-images">暂无图片数据</div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { CopyDocument, Picture } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import type { SourceRegionDto } from '../../services/studentAdminApi'
import { getMistakeImageUrl, hideBrokenImage } from './mistake'

const props = defineProps<{
  regions: SourceRegionDto[]
  imageCount: number
  selectedIndex: number
  migrating: boolean
}>()

const emit = defineEmits<{
  'update:selectedIndex': [index: number]
  migrate: []
}>()

const selectedPath = computed(() => props.regions[props.selectedIndex]?.sourceImagePath ?? '')

const hasUploadPrefixImages = computed(() =>
  props.regions.some((r) => r.sourceImagePath && r.sourceImagePath.startsWith('uploads/')),
)

function dimBrokenPreview(e: Event) {
  ;(e.target as HTMLImageElement).style.opacity = '0.3'
}

function copyPath(path: string | undefined) {
  if (!path) return
  navigator.clipboard.writeText(path).then(() => {
    ElMessage.success('已复制路径')
  }).catch(() => {
    ElMessage.error('复制失败')
  })
}
</script>

<style scoped>
.section-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--adm-text-primary);
  margin-bottom: 10px;
  display: flex;
  align-items: center;
  gap: 6px;
}
.section-count {
  font-size: 11px;
  color: var(--adm-text-muted);
  font-weight: 400;
}
.migration-banner { margin-bottom: 12px; }
.migration-banner :deep(.el-alert__title) {
  display: flex;
  align-items: center;
  gap: 8px;
}
.migration-text { flex: 1; }
.img-gallery {
  display: flex;
  gap: 12px;
  margin-bottom: 18px;
  flex-wrap: wrap;
}
.img-gallery-item {
  width: 120px;
  height: 120px;
  border-radius: 8px;
  background: linear-gradient(135deg, #e2e8f0, #cbd5e1);
  cursor: pointer;
  border: 2px solid var(--adm-border);
  position: relative;
  transition: all 0.15s;
  flex-shrink: 0;
  overflow: hidden;
}
.img-gallery-item:hover { border-color: var(--adm-primary-light); }
.img-gallery-item.selected {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.15);
}
.gallery-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.g-num {
  position: absolute;
  top: 6px;
  left: 6px;
  font-size: 10px;
  color: #fff;
  font-weight: 600;
  background: rgba(0, 0, 0, 0.4);
  padding: 1px 6px;
  border-radius: 4px;
}
.no-images {
  padding: 24px;
  text-align: center;
  color: var(--adm-text-muted);
  font-size: 13px;
  background: var(--adm-surface-subtle);
  border-radius: var(--adm-radius-md);
  margin-bottom: 18px;
}
.img-preview-lg {
  margin-bottom: 18px;
  border: 1px solid var(--adm-border);
  border-radius: var(--adm-radius-md);
  overflow: hidden;
}
.img-preview-lg img {
  width: 100%;
  max-height: 300px;
  object-fit: contain;
  background: var(--adm-surface-subtle);
  display: block;
}
.img-path-row {
  display: flex;
  align-items: center;
  padding: 8px 12px;
  background: var(--adm-surface-subtle);
  border-top: 1px solid var(--adm-border);
  gap: 8px;
}
.image-path-text {
  flex: 1;
  font-size: 11px;
  color: var(--adm-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: 'SF Mono', Menlo, Consolas, monospace;
}
</style>
