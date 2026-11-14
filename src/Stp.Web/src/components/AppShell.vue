<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { RouterLink, RouterView, useRoute } from 'vue-router'
import {
  CaseUpper,
  FolderOpen,
  Moon,
  Menu,
  Pin,
  RefreshCw,
  Search,
  ShieldCheck,
  Sun,
  X,
} from '@lucide/vue'
import { getUserName, request } from '../api/client'
import type { HealthResponse } from '../api/types'
import { initTeamIdentity, teamIdentity } from '../composables/useTeamIdentity'
import { renderAvatar } from '../lib/team'
import { useTheme } from '../composables/useTheme'
import {
  isPinned,
  startBadgePolling,
  stopBadgePolling,
  toolBadge,
  toolSections,
  togglePin,
  type ToolDef,
} from '../composables/useToolRegistry'
import CommandPalette from './CommandPalette.vue'
import NotificationBell from './NotificationBell.vue'
import { useLunchBreak } from '../composables/useLunchBreak'
import { useToast } from '../composables/useToast'

const route = useRoute()
const { theme, toggleTheme } = useTheme()
const toast = useToast()
const { isLunchHour, isDevBypass } = useLunchBreak()

const sidebarOpen = ref(false)
const healthy = ref<boolean | null>(null)
const largeText = ref(false)
const palette = ref<InstanceType<typeof CommandPalette> | null>(null)
let healthTimer: number | undefined

function toggleLargeText(): void {
  largeText.value = !largeText.value
  document.documentElement.classList.toggle('large-text', largeText.value)
  try {
    localStorage.setItem('stp.largeText', largeText.value ? '1' : '0')
  } catch {
    // Bỏ qua.
  }
}

function pinLabel(tool: ToolDef): string {
  return isPinned(tool) ? `Bỏ ghim "${tool.label}"` : `Ghim "${tool.label}" lên đầu`
}

const pageTitle = computed(() => String(route.meta.title ?? 'STP Archive'))
const pageSubtitle = computed(() => String(route.meta.subtitle ?? ''))
const currentUserName = computed(() => teamIdentity.value?.displayName ?? getUserName())

function isActive(path: string): boolean {
  if (path === '/') return route.path === '/'
  return route.path === path || route.path.startsWith(`${path}/`)
}

function handleToolNavClick(event: MouseEvent, to: string): void {
  sidebarOpen.value = false
  if (isLunchHour.value && !isDevBypass.value && to !== '/nghi-trua') {
    event.preventDefault()
    toast.warning(
      'Giờ nghỉ trưa (12:00 – 13:00)',
      'Hệ thống đang tạm khóa để bảo trì & nghỉ trưa. Vui lòng nghỉ ngơi hoặc nhập mã Dev (stp2026) để vào!',
    )
  }
}

async function checkHealth(): Promise<void> {
  try {
    const health = await request<HealthResponse>('/api/health')
    healthy.value = health.database === 'connected' || health.status === 'ok'
  } catch {
    healthy.value = false
  }
}

onMounted(() => {
  void checkHealth()
  void initTeamIdentity()
  healthTimer = window.setInterval(() => void checkHealth(), 60_000)
  startBadgePolling()
  try {
    largeText.value = localStorage.getItem('stp.largeText') === '1'
  } catch {
    largeText.value = false
  }
  document.documentElement.classList.toggle('large-text', largeText.value)
})

onUnmounted(() => {
  if (healthTimer) window.clearInterval(healthTimer)
  stopBadgePolling()
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
        <template v-for="section in toolSections" :key="section.label">
          <div class="nav-section">{{ section.label }}</div>
          <RouterLink
            v-for="item in section.items"
            :key="item.to"
            :to="item.to"
            class="nav-item"
            :class="{ 'is-active': isActive(item.to), 'is-lunch-blocked': isLunchHour && !isDevBypass }"
            @click="handleToolNavClick($event, item.to)"
          >
            <component :is="item.icon" :size="17" />
            <span class="grow">{{ item.label }}</span>
            <span v-if="toolBadge(item)" class="nav-badge">{{ toolBadge(item) }}</span>
            <button
              type="button"
              class="nav-pin"
              :class="{ 'is-pinned': isPinned(item) }"
              :title="pinLabel(item)"
              :aria-label="pinLabel(item)"
              @click.prevent.stop="togglePin(item)"
            >
              <Pin :size="13" />
            </button>
          </RouterLink>
        </template>
      </nav>

      <div class="sidebar-footer">
        <RouterLink
          class="nav-item"
          :class="{ 'is-active': isActive('/nghi-trua') }"
          to="/nghi-trua"
          @click="sidebarOpen = false"
        >
          <Moon :size="17" />
          <span class="grow">Giờ nghỉ trưa (12h-13h)</span>
          <span v-if="isLunchHour && !isDevBypass" class="lunch-live-badge">12h-13h</span>
        </RouterLink>
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
      <header v-if="route.path !== '/nghi-trua'" class="topbar">
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
          <button
            class="btn btn-secondary btn-sm palette-trigger"
            type="button"
            title="Thanh lệnh nhanh (Ctrl+K)"
            @click="palette?.openPalette()"
          >
            <Search :size="14" />
            <span class="palette-trigger-label">Tìm nhanh</span>
            <kbd class="palette-kbd">Ctrl K</kbd>
          </button>
          <NotificationBell />
          <RouterLink
            class="btn btn-secondary btn-sm topbar-user-btn"
            :class="{ 'is-active': isActive('/ca-nhan') }"
            to="/ca-nhan"
            :title="currentUserName ? `Trang cá nhân: ${currentUserName}` : 'Trang cá nhân'"
          >
            <span
              class="topbar-user-avatar"
              :style="currentUserName ? renderAvatar(currentUserName).style : {}"
            >
              <img
                v-if="currentUserName && renderAvatar(currentUserName).imageUrl"
                :src="renderAvatar(currentUserName).imageUrl"
                alt=""
                class="avatar-img"
              />
              <template v-else>{{ currentUserName ? renderAvatar(currentUserName).initials : '👤' }}</template>
            </span>
            <span class="topbar-user-name">{{ currentUserName || 'Cá nhân' }}</span>
          </RouterLink>
          <RouterLink class="btn btn-secondary btn-sm" to="/dong-bo">
            <RefreshCw :size="14" />
            Đồng bộ
          </RouterLink>
          <button
            class="btn btn-secondary btn-icon"
            :class="{ 'is-active': largeText }"
            type="button"
            aria-label="Bật/tắt chữ lớn"
            :title="largeText ? 'Tắt chữ lớn' : 'Bật chữ lớn cho dễ đọc'"
            @click="toggleLargeText"
          >
            <CaseUpper :size="18" />
          </button>
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

      <main class="page" :class="{ 'page-lunch': route.path === '/nghi-trua' }">
        <RouterView v-slot="{ Component }">
          <Transition name="route" mode="out-in">
            <component :is="Component" :key="route.name?.toString() ?? route.path" />
          </Transition>
        </RouterView>
      </main>
    </div>

    <CommandPalette ref="palette" />
  </div>
</template>

<style scoped>
.topbar-user-btn {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  padding: 4px 10px 4px 5px;
  border-radius: 999px;
  font-weight: 500;
  text-decoration: none;
}

.topbar-user-avatar {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.72rem;
  font-weight: 700;
  color: #fff;
  background: var(--color-primary, #3b82f6);
  flex-shrink: 0;
}

.topbar-user-avatar img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.topbar-user-name {
  max-width: 110px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 0.82rem;
}
</style>
