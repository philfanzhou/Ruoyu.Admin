<template>
  <div v-if="imagePaths.length === 0" class="panel-empty-hint">当前记录无图片</div>
  <template v-else>
    <div class="img-preview-lg">
      <img
        v-if="currentImageSrc"
        :src="currentImageSrc"
        :style="{ transform: `rotate(${rotations[selectedIndex] || 0}deg)` }"
        class="preview-img"
        alt="预览"
        @error="hideBrokenImage"
      />
      <el-icon v-else :size="48"><Picture /></el-icon>
    </div>
    <div class="preview-caption">
      图片 #{{ selectedIndex + 1 }}
      <span v-if="imagePaths[selectedIndex]"> · {{ imagePaths[selectedIndex] }}</span>
    </div>
    <div class="img-thumb-row">
      <div
        v-for="(img, idx) in imagePaths"
        :key="idx"
        class="img-thumb-sm"
        :class="{ selected: selectedIndex === idx, 'assign-selected': assignIndices.includes(idx) }"
        @click="emit('update:selectedIndex', idx)"
      >
        <img :src="getImageUrl(img, 'small')" class="thumb-img" alt="" @error="hideBrokenImage" />
        <span class="img-num-sm">#{{ idx + 1 }}</span>
        <button
          type="button"
          class="img-check"
          :class="{ checked: assignIndices.includes(idx) }"
          :title="assignIndices.includes(idx) ? '从指派中移除' : '加入指派'"
          @click.stop="emit('toggleAssign', idx)"
        >
          <el-icon v-if="assignIndices.includes(idx)" :size="10"><Check /></el-icon>
        </button>
        <div class="img-thumb-actions">
          <button type="button" class="img-action-btn" title="向左旋转" @click.stop="emit('rotate', idx, -90)">
            <el-icon :size="11"><RefreshLeft /></el-icon>
          </button>
          <button type="button" class="img-action-btn" title="向右旋转" @click.stop="emit('rotate', idx, 90)">
            <el-icon :size="11"><RefreshRight /></el-icon>
          </button>
          <button type="button" class="img-action-btn danger" title="删除图片" @click.stop="emit('delete', idx)">
            <el-icon :size="11"><Delete /></el-icon>
          </button>
        </div>
      </div>
    </div>
    <div class="assign-image-summary">
      已选 <strong>{{ assignIndices.length }}</strong> / {{ imagePaths.length }} 张用于指派
    </div>
  </template>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { Check, Delete, Picture, RefreshLeft, RefreshRight } from '@element-plus/icons-vue'
import { getImageUrl, hideBrokenImage } from './uploadRecord'

const props = defineProps<{
  imagePaths: string[]
  rotations: number[]
  selectedIndex: number
  assignIndices: number[]
}>()

const emit = defineEmits<{
  'update:selectedIndex': [index: number]
  toggleAssign: [index: number]
  rotate: [index: number, delta: number]
  delete: [index: number]
}>()

const currentImageSrc = computed(() => {
  const path = props.imagePaths[props.selectedIndex]
  return path ? getImageUrl(path, 'medium') : ''
})
</script>

<style scoped>
.panel-empty-hint {
  padding: 24px;
  text-align: center;
  color: var(--adm-text-muted);
  font-size: 13px;
  background: var(--adm-surface-subtle);
  border-radius: var(--adm-radius-md);
}
.img-preview-lg {
  width: 100%;
  height: 220px;
  border-radius: var(--adm-radius-md);
  background: linear-gradient(135deg, #f1f5f9, #e2e8f0);
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--adm-text-muted);
  border: 1px solid var(--adm-border);
  margin-bottom: 4px;
  overflow: hidden;
}
.preview-img {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
  transition: transform 0.3s;
}
.preview-caption {
  font-size: 11px;
  color: var(--adm-text-muted);
  text-align: center;
  margin-bottom: 10px;
  word-break: break-all;
}
.img-thumb-row {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}
.img-thumb-sm {
  width: 100px;
  height: 100px;
  border-radius: 8px;
  background: linear-gradient(135deg, #e2e8f0, #cbd5e1);
  cursor: pointer;
  border: 2px solid var(--adm-border);
  position: relative;
  transition: all 0.15s;
  overflow: hidden;
}
.img-thumb-sm:hover { border-color: var(--adm-primary-light); }
.img-thumb-sm.selected {
  border-color: var(--adm-primary);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.15);
}
.img-thumb-sm.assign-selected { border-color: var(--adm-success); }
.thumb-img { width: 100%; height: 100%; object-fit: cover; }
.img-num-sm {
  position: absolute;
  top: 4px;
  left: 4px;
  font-size: 10px;
  color: #fff;
  font-weight: 600;
  background: rgba(0, 0, 0, 0.4);
  padding: 1px 6px;
  border-radius: 4px;
}
.img-check {
  position: absolute;
  top: 4px;
  right: 4px;
  width: 18px;
  height: 18px;
  border-radius: 4px;
  border: 1.5px solid #fff;
  background: rgba(255, 255, 255, 0.7);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
  cursor: pointer;
}
.img-check.checked {
  background: var(--adm-success);
  border-color: var(--adm-success);
}
.img-thumb-actions {
  position: absolute;
  bottom: 4px;
  left: 50%;
  transform: translateX(-50%);
  display: flex;
  gap: 3px;
  opacity: 0;
  transition: opacity 0.15s;
}
.img-thumb-sm:hover .img-thumb-actions,
.img-thumb-sm:focus-within .img-thumb-actions { opacity: 1; }
.img-action-btn {
  width: 22px;
  height: 22px;
  border-radius: 4px;
  background: rgba(0, 0, 0, 0.5);
  color: #fff;
  border: none;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0;
}
.img-action-btn:hover { background: rgba(0, 0, 0, 0.7); }
.img-action-btn.danger:hover { background: var(--adm-error); }
.assign-image-summary {
  margin-top: 8px;
  padding: 6px 10px;
  background: var(--adm-success-bg);
  color: var(--adm-success);
  border-radius: var(--adm-radius-sm);
  font-size: 12px;
}
</style>
