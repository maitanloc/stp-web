<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { Bell } from '@lucide/vue'
import { request, getUserName } from '../api/client'
import { teamIdentity } from '../composables/useTeamIdentity'
import { lastLiveNotification } from '../composables/useTeamPresence'
import { relativeTime } from '../lib/team'

// Bell topbar (G2): badge số chưa đọc, 2 tab Hệ thống / Tin nhắn, bấm item
// là deep link tới đúng trang việc vừa xảy ra. Poll 20s dự phòng + realtime
// qua hub 'notification'.
const POLL_MS = 20000

interface NotificationItem {
  id: string
  kind: 'system' | 'chat' | string
  title: string
  body: string
  link: string | null
  createdAt: string
  isRead: boolean
}

const router = useRouter()

const open = ref(false)
const tab = ref<'system' | 'chat'>('system')
const items = ref<NotificationItem[]>([])
const unread = ref(0)
const wrapElement = ref<HTMLElement | null>(null)

const selfName = computed(() => teamIdentity.value?.displayName ?? getUserName())
const visibleItems = computed(() =>
  items.value.filter((item) => (tab.value === 'chat' ? item.kind === 'chat' : item.kind !== 'chat')),
)
const unreadByTab = computed(() => ({
  system: items.value.filter((item) => item.kind !== 'chat' && !item.isRead).length,
  chat: items.value.filter((item) => item.kind === 'chat' && !item.isRead).length,
}))

async function fetchNotifications(): Promise<void> {
  try {
    const response = await request<{ items: NotificationItem[]; unreadCount: number }>(
      '/api/team/notifications?take=40',
    )
    items.value = response.items
    unread.value = response.unreadCount
  } catch {
    // Bell phụ — blip bỏ qua, nhịp sau thử lại.
  }
}

async function markRead(ids?: string[]): Promise<void> {
  try {
    await request('/api/team/notifications/read', {
      method: 'POST',
      body: ids && ids.length > 0 ? { ids } : { all: true },
    })
    await fetchNotifications()
  } catch {
    // Bỏ qua.
  }
}

async function markTabRead(): Promise<void> {
  const pending = visibleItems.value.filter((item) => !item.isRead).map((item) => item.id)
  if (pending.length === 0) return
  await markRead(pending)
}

function openItem(item: NotificationItem): void {
  if (!item.isRead) void markRead([item.id])
  open.value = false
  if (item.link) {
    void router.push(item.link)
  }
}

function toggleOpen(): void {
  open.value = !open.value
  if (open.value) {
    tab.value = unreadByTab.value.chat > 0 ? 'chat' : 'system'
    void fetchNotifications()
  }
}

function onDocumentClick(event: MouseEvent): void {
  if (open.value && wrapElement.value && !wrapElement.value.contains(event.target as Node)) {
    open.value = false
  }
}

// Realtime: hub đẩy notification của chính mình → cập nhật badge + list ngay.
watch(lastLiveNotification, (payload) => {
  if (!payload || payload.forUser !== selfName.value) return
  unread.value += 1
  items.value = [
    {
      id: `live-${payload.timestamp}-${payload.kind}` as unknown as string,
      kind: payload.kind,
      title: payload.title,
      body: payload.body,
      link: payload.link,
      createdAt: payload.timestamp,
      isRead: false,
    },
    ...items.value,
  ]
})

let pollTimer: ReturnType<typeof setInterval> | null = null

onMounted(() => {
  document.addEventListener('click', onDocumentClick)
  void fetchNotifications()
  pollTimer = setInterval(() => void fetchNotifications(), POLL_MS)
})

onUnmounted(() => {
  document.removeEventListener('click', onDocumentClick)
  if (pollTimer) clearInterval(pollTimer)
})
</script>

<template>
  <div ref="wrapElement" class="bell-wrap">
    <button
      class="btn btn-secondary btn-icon bell-trigger"
      type="button"
      :aria-label="unread > 0 ? `${unread} thông báo chưa đọc` : 'Không có thông báo mới'"
      :title="unread > 0 ? `${unread} thông báo chưa đọc` : 'Thông báo'"
      @click="toggleOpen"
    >
      <Bell :size="18" />
      <span v-if="unread > 0" class="bell-badge">{{ unread > 99 ? '99+' : unread }}</span>
    </button>

    <div v-if="open" class="bell-panel">
      <header class="bell-head">
        <span class="bell-title">Thông báo</span>
        <button class="bell-link" type="button" @click="markTabRead">
          Đọc hết tab này
        </button>
      </header>
      <nav class="bell-tabs">
        <button
          type="button"
          class="bell-tab"
          :class="{ 'is-on': tab === 'system' }"
          @click="tab = 'system'"
        >
          Hệ thống
          <span v-if="unreadByTab.system > 0" class="bell-tab-badge">{{ unreadByTab.system }}</span>
        </button>
        <button
          type="button"
          class="bell-tab"
          :class="{ 'is-on': tab === 'chat' }"
          @click="tab = 'chat'"
        >
          Tin nhắn
          <span v-if="unreadByTab.chat > 0" class="bell-tab-badge">{{ unreadByTab.chat }}</span>
        </button>
      </nav>
      <div class="bell-list">
        <p v-if="visibleItems.length === 0" class="bell-blank">
          Chưa có thông báo nào — khi đồng đội xuất file, đồng bộ hay nhắn tin sẽ hiện ở đây.
        </p>
        <button
          v-for="item in visibleItems"
          :key="item.id"
          type="button"
          class="bell-item"
          :class="{ 'is-unread': !item.isRead }"
          @click="openItem(item)"
        >
          <span class="bell-item-dot" :data-kind="item.kind"></span>
          <span class="bell-item-main">
            <span class="bell-item-title">{{ item.title }}</span>
            <span v-if="item.body" class="bell-item-body">{{ item.body }}</span>
            <span class="bell-item-time">{{ relativeTime(item.createdAt) }}</span>
          </span>
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.bell-wrap {
  position: relative;
}

.bell-trigger {
  position: relative;
}

.bell-badge {
  position: absolute;
  top: -6px;
  right: -7px;
  min-width: 17px;
  height: 17px;
  padding: 0 4px;
  border-radius: 999px;
  background: var(--danger, #dc2626);
  color: #fff;
  font-size: 10px;
  font-weight: 700;
  line-height: 17px;
  text-align: center;
}

.bell-panel {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  width: 340px;
  max-height: 420px;
  display: flex;
  flex-direction: column;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 12px;
  box-shadow: var(--shadow-lg);
  z-index: 80;
  overflow: hidden;
}

.bell-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
}

.bell-title {
  font-weight: 700;
  font-size: 13.5px;
}

.bell-link {
  appearance: none;
  border: none;
  background: none;
  color: var(--brand);
  font-size: 12px;
  cursor: pointer;
}

.bell-link:hover {
  text-decoration: underline;
}

.bell-tabs {
  display: flex;
  gap: 4px;
  padding: 0 10px;
  border-bottom: 1px solid var(--border);
}

.bell-tab {
  appearance: none;
  border: none;
  background: none;
  padding: 7px 10px 8px;
  font-size: 12.5px;
  font-weight: 600;
  color: var(--muted);
  border-bottom: 2px solid transparent;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 5px;
}

.bell-tab.is-on {
  color: var(--text);
  border-bottom-color: var(--brand);
}

.bell-tab-badge {
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: 999px;
  background: var(--danger, #dc2626);
  color: #fff;
  font-size: 10px;
  font-weight: 700;
  line-height: 16px;
  text-align: center;
}

.bell-list {
  overflow-y: auto;
  flex: 1;
  min-height: 0;
}

.bell-blank {
  padding: 22px 16px;
  font-size: 12.5px;
  color: var(--muted-2);
  text-align: center;
}

.bell-item {
  display: flex;
  gap: 9px;
  width: 100%;
  padding: 10px 14px;
  border: none;
  border-bottom: 1px solid var(--border);
  background: none;
  text-align: left;
  cursor: pointer;
  align-items: flex-start;
}

.bell-item:hover {
  background: var(--surface-2);
}

.bell-item.is-unread {
  background: var(--accent-soft);
}

.bell-item-dot {
  flex: none;
  width: 8px;
  height: 8px;
  border-radius: 999px;
  margin-top: 6px;
  background: var(--brand);
}

.bell-item-dot[data-kind='chat'] {
  background: var(--success);
}

.bell-item.is-read .bell-item-dot {
  background: var(--border);
}

.bell-item-main {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.bell-item-title {
  font-weight: 700;
  font-size: 12.5px;
  color: var(--text);
}

.bell-item-body {
  font-size: 12px;
  color: var(--muted);
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.bell-item-time {
  font-size: 11px;
  color: var(--muted-2);
}
</style>
