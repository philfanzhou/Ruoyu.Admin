import type { Component } from 'vue'

// Sidebar groups in display order. Routes join a group via `meta.group`.
export const NAV_GROUPS = [
  { key: 'overview', label: '概览' },
  { key: 'users', label: '用户' },
  { key: 'content', label: '内容与上传' },
  { key: 'storage', label: '存储审计' },
] as const

export type NavGroupKey = (typeof NAV_GROUPS)[number]['key']

declare module 'vue-router' {
  interface RouteMeta {
    title?: string
    group?: NavGroupKey
    icon?: Component
  }
}
