<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import {
  Combine,
  FileSpreadsheet,
  FolderOpen,
  LayoutDashboard,
  ListChecks,
  Menu,
  Moon,
  RefreshCw,
  ShieldCheck,
  Sun,
  Wrench,
  X,
} from '@lucide/vue'
import { request } from '../api/client'
import type { HealthResponse } from '../api/types'
import { useTheme } from '../composables/useTheme'

interface NavItem {
  label: string
  to: string
  icon: typeof LayoutDashboard
}

interface NavSection {
  label: string
  items: NavItem[]
}

const route = useRoute()
const { theme, toggleTheme } = useTheme()

const sidebarOpen = ref(false)
const healthy = ref<boolean | null>(null)
let healthTimer: number | undefined

const sections: NavSection[] = [
  {
    label: 'Quy trình',
    items: [
      { label: 'Tổng quan', to: '/', icon: LayoutDashboard },
      { label: 'Chuyển đổi Excel', to: '/chuyen-doi', icon: FileSpreadsheet },
      { label: 'Gộp nhiều file', to: '/gop-file', icon: Combine },
      { label: 'Bổ sung trích yếu', to: '/trich-yeu', icon: ListChecks },
      { label: 'Đồng bộ từ Excel', to: '/dong-bo', icon: RefreshCw },
    ],
  },
  {
    label: 'Tra cứu',
    items: [{ label: 'Kho hồ sơ', to: '/kho', icon: FolderOpen }],
  },
  {
    label: 'Hệ thống',
    items: [{ label: 'Không gian nâng cao', to: '/nang-cao', icon: Wrench }],
  },
]

const pageTitle = computed(() => String(route.meta.title ?? 'STP Archive'))
const pageSubtitle = computed(() => String(route.meta.subtitle ?? ''))

function isActive(path: string): boolean {
  if (path === '/') return route.path === '/'
  return route.path === path || route.path.startsWith(`${path}/`)
}

async function checkHealth(): Promise<void> {
  try {
    const health = await request<HealthResponse>('/api/health')
    healthy.value = health.status === 'ok' && health.database === 'connected'
  } catch {
    healthy.value = false
  }
}

onMounted(() => {
  void checkHealth()
  healthTimer = window.setInterval(() => void checkHealth(), 60_000)
})

onUnmounted(() => {
  if (healthTimer) window.clearInterval(healthTimer)
})
</script>

<template>
  <div class="app-shell">
    <aside class="sidebar" :class="{ open: sidebarOpen }">
      <RouterLink class="brand" to="/" @click="sidebarOpen = false">
        <span class="brand-mark">
          <FolderOpen :size="23" />
        </span>
        <span class="brand-text">
          <span class="brand-eyebrow">Lưu trữ</span>
          <span class="brand-title">STP Archive</span>
        </span>
      </RouterLink>

      <nav class="nav" aria-label="Điều hướng chính">
        <template v-for="section in sections" :key="section.label">
          <div class="nav-section">{{ section.label }}</div>
          <RouterLink
            v-for="item in section.items"
            :key="item.to"
            :to="item.to"
            class="nav-item"
            :class="{ 'is-active': isActive(item.to) }"
            @click="sidebarOpen = false"
          >
            <component :is="item.icon" :size="17" />
            <span>{{ item.label }}</span>
          </RouterLink>
        </template>
      </nav>

      <div class="sidebar-footer">
        <RouterLink class="nav-item" to="/developer" target="_blank" rel="noopener">
          <ShieldCheck :size="17" />
          <span>Trang developer</span>
        </RouterLink>
        <div class="server-chip">
          <span class="dot" :class="healthy === null ? '' : healthy ? 'ok' : 'err'" />
          <span class="grow">
            {{ healthy === null ? 'Đang kiểm tra máy chủ...' : healthy ? 'Máy chủ hoạt động' : 'Mất kết nối máy chủ' }}
          </span>
        </div>
        <div class="text-xs muted" style="padding: 0 4px">STP Archive · Bản nội bộ</div>
      </div>
    </aside>

    <div v-if="sidebarOpen" class="sidebar-backdrop" @click="sidebarOpen = false" />

    <div class="main">
      <header class="topbar">
        <button
          class="btn btn-ghost btn-icon mobile-only"
          type="button"
          aria-label="Mở menu"
          @click="sidebarOpen = !sidebarOpen"
        >
          <X v-if="sidebarOpen" :size="20" />
          <Menu v-else :size="20" />
        </button>

        <div class="topbar-heading">
          <div class="topbar-title">{{ pageTitle }}</div>
          <div v-if="pageSubtitle" class="topbar-subtitle">{{ pageSubtitle }}</div>
        </div>

        <div class="topbar-actions">
          <RouterLink class="btn btn-secondary btn-sm" to="/dong-bo">
            <RefreshCw :size="14" />
            Đồng bộ
          </RouterLink>
          <button
            class="btn btn-secondary btn-icon"
            type="button"
            :aria-label="theme === 'dark' ? 'Chuyển giao diện sáng' : 'Chuyển giao diện tối'"
            :title="theme === 'dark' ? 'Giao diện sáng' : 'Giao diện tối'"
            @click="toggleTheme"
          >
            <Sun v-if="theme === 'dark'" :size="18" />
            <Moon v-else :size="18" />
          </button>
        </div>
      </header>

      <main class="page">
        <RouterView />
      </main>
    </div>
  </div>
</template>
