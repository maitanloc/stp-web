<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { FileSpreadsheet, RefreshCw, ShieldCheck, Users, X } from '@lucide/vue'
import { fetchRecap, type RecapItem } from '../api/client'
import { teamIdentity } from '../composables/useTeamIdentity'
import { relativeTime } from '../lib/team'

/**
 * Thẻ tổng kết sau màn chờ vào hệ thống (yêu cầu 11/11/2026): "bạn vừa làm gì,
 * ở trang nào, khu nào, lúc nào" — đọc từ nhật ký nhóm thật (AuditLogs) giống
 * kiểu hoạt động của Google Sheets. Chỉ hiện 1 lần sau khi vào, tự tấu sau 25s
 * hoặc bấm tắt.
 */
const emit = defineEmits<{ closed: [] }>()

const route = useRoute()
const items = ref<RecapItem[]>([])
const loaded = ref(false)

let autoCloseTimer: number | undefined

const greeting = computed(() => {
  const hour = new Date().getHours()
  const period = hour < 11 ? 'Chào buổi sáng' : hour < 14 ? 'Chào buổi trưa' : hour < 18 ? 'Chào buổi chiều' : 'Chào buổi tối'
  return `${period}, ${teamIdentity.value?.displayName ?? 'bạn'}!`
})

const pageTitle = computed(() => {
  const title = route.meta.title as string | undefined
  return title ?? 'hệ thống'
})

function iconFor(action: string): typeof FileSpreadsheet {
  if (action.startsWith('sync')) return RefreshCw
  if (action.startsWith('admin')) return Users
  if (action.startsWith('auth')) return ShieldCheck
  return FileSpreadsheet
}

function atLabel(item: RecapItem): string {
  return relativeTime(item.at)
}

onMounted(async () => {
  try {
    const response = await fetchRecap()
    items.value = response.items
  } catch {
    // Tổng kết chỉ là phụ — tải không được thì im lặng, app vẫn vào bình thường.
  } finally {
    loaded.value = true
  }
  autoCloseTimer = window.setTimeout(() => emit('closed'), 25_000)
})

onUnmounted(() => {
  window.clearTimeout(autoCloseTimer)
})
</script>

<template>
  <Transition name="recap">
    <section v-if="loaded" class="recap" role="region" aria-label="Tổng kết việc vừa làm">
      <header class="recap-head">
        <div>
          <h2>{{ greeting }}</h2>
          <p>
            Bạn đang ở trang <strong>{{ pageTitle }}</strong>
            <template v-if="items.length > 0"> — việc gần đây của bạn:</template>
            <template v-else> — đây là nơi bạn bắt đầu.</template>
          </p>
        </div>
        <button class="close" type="button" title="Tắt thẻ tổng kết" @click="emit('closed')">
          <X :size="15" />
        </button>
      </header>

      <ul v-if="items.length > 0" class="recap-list">
        <li v-for="(item, index) in items" :key="item.action + item.at + index">
          <span class="ico" :class="'ico-' + item.action.split('.')[0]">
            <component :is="iconFor(item.action)" :size="15" />
          </span>
          <span class="what">
            <strong>{{ item.label }}</strong>
            <em>{{ item.area }}</em>
            <span v-if="item.details" class="detail">{{ item.details }}</span>
          </span>
          <span class="when">{{ atLabel(item) }}</span>
        </li>
      </ul>
      <p v-else class="recap-empty">
        Chưa có việc nào ghi nhật ký trong kỳ này — xuất bản lọc, đồng bộ Excel…
        sẽ hiện ngay ở đây lần sau.
      </p>
    </section>
  </Transition>
</template>

<style scoped>
.recap {
  position: fixed;
  top: 16px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 85;
  width: min(560px, calc(100vw - 28px));
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg, 16px);
  box-shadow: 0 24px 60px rgba(15, 23, 42, 0.28);
  padding: 16px 18px 14px;
}

.recap-head {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}

.recap-head h2 {
  margin: 0 0 3px;
  font-size: 15.5px;
  color: var(--text);
}

.recap-head p {
  margin: 0;
  font-size: 12.5px;
  color: var(--muted);
}

.recap-head strong {
  color: var(--text);
  font-weight: 700;
}

.close {
  margin-left: auto;
  border: none;
  background: transparent;
  color: var(--muted);
  cursor: pointer;
  border-radius: 8px;
  padding: 5px;
  display: grid;
  place-items: center;
}

.close:hover {
  background: var(--surface-3, rgba(0, 0, 0, 0.05));
  color: var(--text);
}

.recap-list {
  list-style: none;
  margin: 12px 0 0;
  padding: 10px 0 0;
  border-top: 1px dashed var(--border);
  display: flex;
  flex-direction: column;
  gap: 9px;
  max-height: 250px;
  overflow: auto;
}

.recap-list li {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}

.ico {
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  border-radius: 9px;
  display: grid;
  place-items: center;
  background: var(--brand-soft, rgba(13, 148, 136, 0.12));
  color: var(--brand, #0d9488);
}

.ico-admin {
  background: rgba(99, 102, 241, 0.12);
  color: #6366f1;
}

.what {
  display: flex;
  flex-direction: column;
  min-width: 0;
  font-size: 13px;
  color: var(--text);
}

.what strong {
  font-weight: 700;
}

.what em {
  font-style: normal;
  font-size: 11.5px;
  color: var(--muted);
}

.detail {
  font-size: 11.5px;
  color: var(--muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 340px;
}

.when {
  margin-left: auto;
  flex-shrink: 0;
  font-size: 11.5px;
  color: var(--muted);
  padding-top: 6px;
  white-space: nowrap;
}

.recap-empty {
  margin: 12px 0 0;
  padding: 10px 0 0;
  border-top: 1px dashed var(--border);
  font-size: 12.5px;
  color: var(--muted);
  line-height: 1.55;
}

/* Vào/ra mượt */
.recap-enter-active {
  transition: opacity 0.45s ease, transform 0.45s cubic-bezier(0.22, 0.9, 0.26, 1);
}

.recap-leave-active {
  transition: opacity 0.3s ease, transform 0.3s ease;
}

.recap-enter-from,
.recap-leave-to {
  opacity: 0;
  transform: translateX(-50%) translateY(-16px);
}
</style>
