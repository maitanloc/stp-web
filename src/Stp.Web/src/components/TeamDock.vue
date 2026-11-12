<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { getUserName, request } from '../api/client'
import { initTeamIdentity, logoutTeamIdentity, teamIdentity } from '../composables/useTeamIdentity'
import type { ActivityEventResponse, ChatMessageResponse, ChatMessagesResponse, ChatSeenResponse } from '../api/types'
import { errorMessage } from '../lib/errors'
import { actionLabel, clockTime, colorForName, renderAvatar, relativeTime } from '../lib/team'
import {
  lastTypingActor,
  pingPresence,
  sendTeamTyping,
  startTeamPresence,
  teamOnline,
  liveActivities,
  actionChips,
  hubStatus,
} from '../composables/useTeamPresence'
import { useToast } from '../composables/useToast'
import LoginModal from './LoginModal.vue'
import UserNameModal from './UserNameModal.vue'

// Nhịp dò: chat 3 giây khi mở, 12 giây khi thu gọn (vẫn đếm tin chưa đọc);
// hoạt động 12 giây khi đang mở panel.
const CHAT_OPEN_MS = 3000
const CHAT_HIDDEN_MS = 12000
const ACTIVITY_MS = 12000

const toast = useToast()
const router = useRouter()

const open = ref(false)
const tab = ref<'chat' | 'activity'>('chat')

const storedName = ref<string | null>(null)
const userName = computed(() => teamIdentity.value?.displayName ?? storedName.value)
const nameModalOpen = ref(false)
const loginModalOpen = ref(false)
const accountMenuOpen = ref(false)

const messages = ref<ChatMessageResponse[]>([])
const activities = ref<ActivityEventResponse[]>([])
const draft = ref('')
const sending = ref(false)

// G4: lật page chat cũ hơn — cursor + cờ còn page.
const olderCursor = ref<string | null>(null)
const hasOlder = ref(false)
const loadingOlder = ref(false)

// G4: emoji picker trong khung soạn chat.
const EMOJIS = ['😀', '😂', '🙂', '😉', '😍', '🤔', '😴', '😅', '👍', '👏', '🙏', '💪', '🔥', '🎉', '✅', '❌', '⏳', '📌', '❤️', '😭', '😮', '😎', '🫡', '🤝']
const emojiOpen = ref(false)

// G3: typing qua hub — ai vừa bấm phím soạn tin (tự tan sau 5 giây).
const typingWho = ref<string | null>(null)
let typingTimer: ReturnType<typeof setTimeout> | null = null

const listElement = ref<HTMLElement | null>(null)
let lastSeenAt: number | null = null

// G3: mốc seen nằm trên server (LastReadChatAt) — đếm tin chưa đọc đúng trên
// mọi máy; chưa đăng nhập thì lùi về mốc localStorage như trước.
const serverUnread = ref<number | null>(null)

const unread = computed(() => {
  if (serverUnread.value !== null) return serverUnread.value
  if (lastSeenAt === null) return 0
  return messages.value.filter((message) => new Date(message.createdAt).getTime() > lastSeenAt!).length
})

const onlineOthers = computed(() =>
  teamOnline.value.filter((entry) => entry.actor !== userName.value),
)

async function fetchChatSeen(): Promise<void> {
  if (!teamIdentity.value) return
  try {
    const response = await request<ChatSeenResponse>('/api/team/chat/seen')
    serverUnread.value = response.unreadCount
  } catch {
    // Bỏ qua — badge vẫn hoạt động theo mốc local.
  }
}

function markChatRead(): void {
  lastSeenAt = Date.now()
  if (teamIdentity.value) {
    // Seen server-side: POST gọn, không cần đợi — badge cập nhật qua poll sau.
    serverUnread.value = 0
    void request('/api/team/chat/seen', { method: 'POST' }).catch(() => {})
  }
}

/** G3: gõ chat → báo cho cả nhóm (throttle 2 giây, qua hub realtime). */
let lastTypingSentAt = 0
function onDraftInput(): void {
  const name = userName.value
  if (!name) return
  const now = Date.now()
  if (now - lastTypingSentAt < 2000) return
  lastTypingSentAt = now
  sendTeamTyping(name)
}

/** G3: nghe người khác gõ — hiển thị "X đang gõ…" 5 giây. */
watch(lastTypingActor, (payload) => {
  if (!payload || payload.actor === userName.value) return
  typingWho.value = payload.actor
  if (typingTimer) clearTimeout(typingTimer)
  typingTimer = setTimeout(() => {
    typingTypingExpired(payload.actor)
  }, 5000)
})

function typingTypingExpired(actor: string): void {
  if (typingWho.value === actor) typingWho.value = null
}

/** G4: tách @mention trong thân tin để tô đậm (không dùng v-html). */
function mentionParts(body: string): Array<{ text: string; isMention: boolean }> {
  return body
    .split(/(@[^\s@]{1,60})/g)
    .filter((part) => part.length > 0)
    .map((part) => ({ text: part, isMention: part.startsWith('@') }))
}

function insertEmoji(emoji: string): void {
  draft.value += emoji
  emojiOpen.value = false
}

function toggleEmoji(): void {
  emojiOpen.value = !emojiOpen.value
}

async function scrollChatBottom(): Promise<void> {
  await nextTick()
  const element = listElement.value
  if (element) {
    element.scrollTop = element.scrollHeight
  }
}

async function fetchChat(): Promise<void> {
  try {
    const response = await request<ChatMessagesResponse>('/api/team/chat?take=100')
    messages.value = response.items
    olderCursor.value = response.nextCursor ?? null
    hasOlder.value = response.hasMore ?? false
    if (open.value && tab.value === 'chat') {
      markChatRead()
      void scrollChatBottom()
    }
  } catch {
    // Bỏ qua blip — lỗi gửi tin mới toast rõ.
  }
}

/** G4: lật page tin cũ hơn — nối lên đầu danh sách, giữ vị trí đọc ở cuối. */
async function loadOlder(): Promise<void> {
  if (!olderCursor.value || loadingOlder.value) return
  loadingOlder.value = true
  try {
    const response = await request<ChatMessagesResponse>(
      `/api/team/chat?take=100&before=${encodeURIComponent(olderCursor.value)}`,
    )
    const currentBottom = messages.value
    messages.value = [...currentBottom, ...response.items]
    olderCursor.value = response.nextCursor ?? null
    hasOlder.value = response.hasMore ?? false
  } catch {
    // Bỏ qua blip.
  } finally {
    loadingOlder.value = false
  }
}

async function fetchActivity(): Promise<void> {
  try {
    const response = await request<{ items: ActivityEventResponse[] }>('/api/team/activity?take=60')
    activities.value = response.items
  } catch {
    // Bỏ qua blip, nhịp sau thử lại.
  }
}

// F3: sự kiện hub đổ về realtime — ghim ngay lên đầu feed (kể cả lúc đóng dock),
// mở panel là thấy không cần đợi poll 12s.
watch(liveActivities, (list) => {
  const latest = list[0]
  if (!latest) return
  const exists = activities.value.some(
    (event) =>
      event.actor === latest.actor &&
      event.action === latest.action &&
      Math.abs(new Date(event.timestamp).getTime() - new Date(latest.timestamp).getTime()) < 3000,
  )
  if (exists) return
  activities.value = [
    {
      id: crypto.randomUUID(),
      actor: latest.actor,
      action: latest.action,
      entityType: latest.entityType ?? null,
      entityId: latest.entityId ?? null,
      details: latest.details ?? null,
      timestamp: latest.timestamp,
    },
    ...activities.value.slice(0, 60),
  ]
})

async function sendDraft(): Promise<void> {
  const actor = userName.value
  const body = draft.value.trim()
  if (!actor) {
    nameModalOpen.value = true
    return
  }
  if (body.length === 0 || sending.value) return

  sending.value = true
  // G3: optimistic — hiện tin của mình ngay, server xác nhận rồi thay bản thật.
  const tmpId = `tmp-${Date.now()}` as unknown as `${string}-${string}-${string}-${string}-${string}`
  const tmpMessage: ChatMessageResponse = {
    id: tmpId,
    actor,
    body,
    createdAt: new Date().toISOString(),
  }
  messages.value = [...messages.value, tmpMessage]
  draft.value = ''
  markChatRead()
  void scrollChatBottom()
  try {
    await request('/api/team/chat', { method: 'POST', body: { actor, body } })
    typingTypingExpired(actor)
    await fetchChat()
  } catch (error) {
    // Gửi hỏng — gỡ tin optimistic, trả lại khung soạn để người dùng thử lại.
    messages.value = messages.value.filter((message) => message.id !== tmpId)
    draft.value = body
    toast.error('Không gửi được tin nhắn', errorMessage(error))
  } finally {
    sending.value = false
  }
}

function toggleOpen(): void {
  open.value = !open.value
  if (open.value) {
    markChatRead()
    void fetchChat()
    void fetchActivity()
    if (tab.value === 'chat') void scrollChatBottom()
  }
}

function onModalClosed(): void {
  // "Để sau" — dock vẫn hoạt động, chat/presence đợi tới khi có tên.
  nameModalOpen.value = false
}

function saveName(name: string): void {
  storedName.value = name
  nameModalOpen.value = false
  void pingPresence()
  void fetchChat()
  void fetchChatSeen()
  toast.success(`Đã dùng tên "${name}"`)
}

function onLogged(displayName: string): void {
  loginModalOpen.value = false
  accountMenuOpen.value = false
  void pingPresence()
  void fetchChat()
  void fetchChatSeen()
  toast.success(`Xin chào ${displayName}!`)
}

async function doLogout(): Promise<void> {
  accountMenuOpen.value = false
  await logoutTeamIdentity()
  void pingPresence()
  void fetchChat()
  serverUnread.value = null
  toast.success('Đã đăng xuất')
  if (!userName.value) {
    nameModalOpen.value = true
  }
}

function goAdmin(): void {
  accountMenuOpen.value = false
  void router.push('/quan-ly')
}

function onDocumentKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    accountMenuOpen.value = false
    if (loginModalOpen.value) {
      loginModalOpen.value = false
      return
    }
    if (open.value && !nameModalOpen.value) {
      open.value = false
    }
  }
}

let chatTimer: ReturnType<typeof setInterval> | null = null
let activityTimer: ReturnType<typeof setInterval> | null = null

function restartChatTimer(): void {
  if (chatTimer) clearInterval(chatTimer)
  // G3: dock đóng chỉ dò mốc seen (badge đếm đúng), dock mở thì tải tin.
  chatTimer = setInterval(
    () => (open.value ? void fetchChat() : void fetchChatSeen()),
    open.value ? CHAT_OPEN_MS : CHAT_HIDDEN_MS,
  )
}

onMounted(() => {
  storedName.value = getUserName()
  startTeamPresence()
  // B3 lazy: không tải chat/hoạt động lúc mounted — lần đầu mở dock mới tải
  // (toggleOpen); khi dock đóng, nhịp 12 giây của timer chat vẫn lấy dữ liệu
  // ngầm để đếm tin chưa đọc, activity chỉ tải khi panel mở.
  restartChatTimer()
  activityTimer = setInterval(() => {
    if (open.value) void fetchActivity()
  }, ACTIVITY_MS)
  window.addEventListener('keydown', onDocumentKeydown)
  // Hỏi cookie phiên: có tài khoản nhóm thì presence/chat gắn ngay; không thì
  // khuyến khích khai tên như giai đoạn đầu.
  void initTeamIdentity().then((identity) => {
    if (identity) {
      void pingPresence()
      return
    }
    if (!storedName.value) {
      nameModalOpen.value = true
    }
  })
})

onUnmounted(() => {
  if (chatTimer) clearInterval(chatTimer)
  if (activityTimer) clearInterval(activityTimer)
  window.removeEventListener('keydown', onDocumentKeydown)
})

watch(open, () => restartChatTimer())

watch(tab, (value) => {
  if (open.value && value === 'chat') {
    markChatRead()
    void scrollChatBottom()
  }
})
</script>

<template>
  <div class="team-dock">
    <!-- F3: chip hành động đáng kể của đồng đội, nổi góc phải dưới, tự tan 8s. -->
    <TransitionGroup name="chip" tag="div" class="action-chips" aria-live="polite">
      <div
        v-for="chip in actionChips"
        :key="chip.key"
        class="action-chip"
      >
        <span class="avatar mini" :style="renderAvatar(chip.actor).style">
          {{ renderAvatar(chip.actor).initials }}
        </span>
        <span class="chip-text">
          <strong :style="{ color: colorForName(chip.actor) }">{{ chip.actor }}</strong>
          {{ actionLabel(chip.action) }}
          <span v-if="chip.details" class="chip-detail">{{ chip.details }}</span>
        </span>
      </div>
    </TransitionGroup>

    <!-- Panel chính — v-if/v-else thuần, KHÔNG bọc Transition:
         Transition (nextFrame = requestAnimationFrame) kẹt vĩnh viễn khi tab
         không focus / bị throttle → với mode="out-in" panel không bao giờ
         được render. Mở/đóng tức thì ổn hơn fade 0.15s. -->
    <section v-if="open" class="dock-panel" aria-label="Liên lạc nhóm">
      <header class="dock-head">
        <div class="head-title">
          <span class="panel-title">Nhóm làm việc</span>
          <span class="online-count">
            <span class="online-dot"></span>
            {{ onlineOthers.length + (userName ? 1 : 0) }} online
            <span v-if="hubStatus === 'live'" class="hub-dot live" title="Realtime qua hub SignalR">⚡</span>
            <span
              v-else
              class="hub-dot poll"
              title="Không nối được hub realtime — đang dùng nhịp dự phòng"
            ></span>
          </span>
        </div>
        <div class="head-tools">
          <template v-if="teamIdentity">
            <div class="account-wrap">
              <button
                type="button"
                class="tool"
                :title="teamIdentity.displayName + ' · ' + (teamIdentity.role === 'admin' ? 'Quản trị' : 'Thành viên')"
                @click="accountMenuOpen = !accountMenuOpen"
              >
                <span class="tool-avatar" :style="renderAvatar(teamIdentity.displayName).style">
                  {{ renderAvatar(teamIdentity.displayName).initials }}
                </span>
              </button>
              <div v-if="accountMenuOpen" class="account-menu">
                <div class="account-head">
                  <span class="avatar" :style="renderAvatar(teamIdentity.displayName).style">
                    {{ renderAvatar(teamIdentity.displayName).initials }}
                  </span>
                  <div class="account-info">
                    <span class="account-name">{{ teamIdentity.displayName }}</span>
                    <span class="account-user">
                      @{{ teamIdentity.username }} ·
                      {{ teamIdentity.role === 'admin' ? 'Quản trị' : 'Thành viên' }}
                    </span>
                  </div>
                </div>
                <button
                  v-if="teamIdentity.role === 'admin'"
                  type="button"
                  class="account-item"
                  @click="goAdmin()"
                >
                  Quản lý tài khoản
                </button>
                <button type="button" class="account-item danger" @click="doLogout()">
                  Đăng xuất
                </button>
              </div>
            </div>
          </template>
          <template v-else>
            <button type="button" class="tool" title="Đổi tên hiển thị" @click="nameModalOpen = true">
              <span
                class="tool-avatar"
                :style="userName ? renderAvatar(userName).style : { color: '#fff' }"
              >
                {{ userName ? renderAvatar(userName).initials : '?' }}
              </span>
            </button>
            <button
              type="button"
              class="tool slim login-hint"
              title="Đăng nhập bằng tài khoản nhóm"
              @click="loginModalOpen = true"
            >
              Đăng nhập
            </button>
          </template>
          <button type="button" class="tool slim" title="Thu gọn" @click="open = false">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round">
              <path d="M6 9l6 6 6-6" />
            </svg>
          </button>
        </div>
      </header>

      <nav class="dock-tabs">
        <button type="button" class="tab" :class="{ active: tab === 'chat' }" @click="tab = 'chat'">
          Chat
          <span v-if="unread > 0" class="unread-badge">{{ unread > 9 ? '9+' : unread }}</span>
        </button>
        <button
          type="button"
          class="tab"
          :class="{ active: tab === 'activity' }"
          @click="tab = 'activity'"
        >
          Hoạt động
        </button>
      </nav>

      <!-- Tab: Chat -->
      <div v-show="tab === 'chat'" class="chat-area">
        <button
          v-if="hasOlder"
          type="button"
          class="load-older"
          :disabled="loadingOlder"
          @click="loadOlder"
        >
          {{ loadingOlder ? 'Đang tải…' : 'Xem tin cũ hơn' }}
        </button>
        <div ref="listElement" class="chat-list">
          <p v-if="messages.length === 0" class="blank">
            Chưa có tin nhắn nào — chào cả nhóm ở đây nhé.
          </p>
          <div
            v-for="message in messages"
            :key="message.id"
            class="chat-line"
            :class="{ own: message.actor === userName }"
          >
            <span
              v-if="message.actor !== userName"
              class="avatar"
              :style="renderAvatar(message.actor).style"
            >
              {{ renderAvatar(message.actor).initials }}
            </span>
            <div class="bubble">
              <span v-if="message.actor !== userName" class="who" :style="{ color: colorForName(message.actor) }">
                {{ message.actor }}
              </span>
              <p class="body">
                <template v-for="(part, index) in mentionParts(message.body)" :key="index">
                  <span v-if="part.isMention" class="mention">{{ part.text }}</span>
                  <template v-else>{{ part.text }}</template>
                </template>
              </p>
              <span class="time">{{ clockTime(message.createdAt) }}</span>
            </div>
          </div>
        </div>
        <div v-if="typingWho" class="typing-line">
          <span class="typing-dot" /><span class="typing-dot" /><span class="typing-dot" />
          {{ typingWho }} đang gõ…
        </div>
        <div class="chat-compose">
          <div v-if="emojiOpen" class="emoji-panel">
            <button
              v-for="emoji in EMOJIS"
              :key="emoji"
              type="button"
              class="emoji-cell"
              @click="insertEmoji(emoji)"
            >
              {{ emoji }}
            </button>
          </div>
          <textarea
            v-model="draft"
            rows="1"
            :placeholder="userName ? 'Nhập tin nhắn cho cả nhóm... @tên để nhắc ai đó' : 'Khai tên trước để chat'"
            @keydown.enter.exact.prevent="sendDraft()"
            @input="onDraftInput"
          />
          <button
            type="button"
            class="send send-emoji"
            title="Chèn biểu tượng"
            @click="toggleEmoji"
          >
            🙂
          </button>
          <button
            type="button"
            class="send"
            :disabled="sending || draft.trim().length === 0"
            @click="sendDraft()"
          >
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M22 2L11 13" />
              <path d="M22 2l-7 20-4-9-9-4 20-7z" />
            </svg>
          </button>
        </div>
      </div>

      <!-- Tab: Hoạt động -->
      <div v-show="tab === 'activity'" class="activity-area">
        <p v-if="activities.length === 0" class="blank">
          Chưa có sự kiện nào — xuất file hoặc đồng bộ sẽ hiện ở đây.
        </p>
        <ol v-else class="activity-list">
          <li v-for="event in activities" :key="event.id">
            <span class="event-dot" :style="{ background: colorForName(event.actor) }"></span>
            <div class="event-main">
              <span class="event-head">
                <span class="event-actor" :style="{ color: colorForName(event.actor) }">{{ event.actor }}</span>
                <span class="event-action">{{ actionLabel(event.action) }}</span>
                <span class="event-time">{{ relativeTime(event.timestamp) }}</span>
              </span>
              <span v-if="event.details" class="event-detail">{{ event.details }}</span>
            </div>
          </li>
        </ol>
      </div>
    </section>

    <!-- Pill thu gọn -->
    <button v-else type="button" class="dock-pill" @click="toggleOpen()">
      <span v-if="onlineOthers.length > 0" class="pill-avatars">
        <span
          v-for="entry in onlineOthers.slice(0, 3)"
          :key="entry.actor"
          class="avatar mini"
          :style="renderAvatar(entry.actor).style"
          :title="entry.actor + ' · ' + entry.where"
        >
          {{ renderAvatar(entry.actor).initials }}
        </span>
        <span v-if="onlineOthers.length > 3" class="pill-more">+{{ onlineOthers.length - 3 }}</span>
      </span>
      <span v-else class="pill-icon">
        <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
        </svg>
      </span>
      <span v-if="onlineOthers.length === 0" class="pill-label">Chat nhóm</span>
      <span v-if="unread > 0" class="unread-badge strong">{{ unread > 9 ? '9+' : unread }}</span>
    </button>

    <UserNameModal :open="nameModalOpen" @saved="saveName" @closed="onModalClosed" />
    <LoginModal :open="loginModalOpen" @logged="onLogged" @closed="loginModalOpen = false" />
  </div>
</template>

<style scoped>
.team-dock {
  position: fixed;
  right: 20px;
  bottom: 20px;
  z-index: 60;
  font-family: var(--font-sans);
}

/* ===== Pill thu gọn ===== */
.dock-pill {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  border: 1px solid var(--border);
  background: var(--surface);
  color: var(--text-soft);
  border-radius: 999px;
  padding: 9px 14px;
  cursor: pointer;
  box-shadow: var(--shadow);
  font-size: 13px;
}

.dock-pill:hover {
  border-color: var(--brand);
  color: var(--brand-text);
}

.pill-avatars {
  display: inline-flex;
  align-items: center;
}

.pill-more {
  font-size: 11px;
  color: var(--muted);
  margin-left: 4px;
}

.pill-icon {
  display: inline-flex;
  color: var(--brand-text);
}

.pill-label {
  font-weight: 600;
}

/* ===== Panel ===== */
.dock-panel {
  width: 344px;
  max-height: min(520px, calc(100vh - 48px));
  display: flex;
  flex-direction: column;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  box-shadow: var(--shadow-lg);
  overflow: hidden;
}

.dock-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 12px 8px 16px;
  border-bottom: 1px solid var(--border);
  background: var(--surface-2);
}

.head-title {
  display: flex;
  align-items: baseline;
  gap: 8px;
}

.panel-title {
  font-weight: 700;
  font-size: 14px;
  color: var(--text);
}

.online-count {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 11.5px;
  color: var(--muted);
}

.online-dot {
  width: 7px;
  height: 7px;
  border-radius: 999px;
  background: var(--success);
  box-shadow: 0 0 0 3px var(--success-soft);
}

/* F1: dấu trạng thái hub realtime bên cạnh số online. */
.hub-dot.live {
  font-size: 10px;
  line-height: 1;
}

.hub-dot.poll {
  width: 7px;
  height: 7px;
  border-radius: 999px;
  background: var(--muted-2);
  box-shadow: 0 0 0 3px var(--surface-2);
}

/* F3: chip hành động nổi góc phải dưới, trên cả pill thu gọn. */
.action-chips {
  position: fixed;
  right: 16px;
  bottom: 64px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  z-index: 60;
  pointer-events: none;
}

.action-chip {
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 999px;
  box-shadow: var(--shadow-lg);
  padding: 6px 12px 6px 6px;
  font-size: 12.5px;
  max-width: 320px;
}

.chip-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.chip-text strong {
  font-weight: 700;
}

.chip-detail {
  color: var(--muted);
}

.chip-enter-active,
.chip-leave-active {
  transition:
    opacity 0.25s ease,
    transform 0.25s ease;
}

.chip-enter-from,
.chip-leave-to {
  opacity: 0;
  transform: translateY(8px);
}

.head-tools {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.tool {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--border);
  background: var(--surface);
  border-radius: 999px;
  min-width: 28px;
  height: 28px;
  padding: 0 6px;
  cursor: pointer;
  color: var(--muted);
  font-size: 11px;
}

.tool:hover {
  border-color: var(--brand);
  color: var(--brand-text);
}

.tool-avatar {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  border-radius: 999px;
  color: #fff;
  font-size: 10px;
  font-weight: 700;
}

.login-hint {
  font-size: 11px;
  font-weight: 600;
}

/* ===== Menu tài khoản ===== */
.account-wrap {
  position: relative;
}

.account-menu {
  position: absolute;
  top: calc(100% + 6px);
  right: 0;
  width: 244px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  box-shadow: var(--shadow-lg);
  padding: 6px;
  z-index: 70;
}

.account-head {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 10px 10px;
  border-bottom: 1px solid var(--border);
  margin-bottom: 6px;
}

.account-info {
  display: flex;
  flex-direction: column;
  min-width: 0;
}

.account-name {
  font-size: 13px;
  font-weight: 700;
  color: var(--text);
}

.account-user {
  font-size: 11px;
  color: var(--muted);
}

.account-item {
  display: block;
  width: 100%;
  text-align: left;
  border: none;
  background: transparent;
  color: var(--text-soft);
  font-size: 12.5px;
  padding: 7px 10px;
  border-radius: 6px;
  cursor: pointer;
}

.account-item:hover {
  background: var(--surface-3);
  color: var(--text);
}

.account-item.danger:hover {
  color: var(--danger);
}

.dock-tabs {
  display: flex;
  gap: 4px;
  padding: 8px 12px 0;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
}

.tab {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  border: none;
  background: transparent;
  color: var(--muted);
  font-size: 13px;
  font-weight: 600;
  padding: 8px 12px;
  cursor: pointer;
  border-bottom: 2px solid transparent;
}

.tab.active {
  color: var(--brand-text);
  border-bottom-color: var(--brand);
}

.unread-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 18px;
  height: 18px;
  padding: 0 5px;
  border-radius: 999px;
  background: var(--danger);
  color: #fff;
  font-size: 10.5px;
  font-weight: 700;
}

/* ===== Chat ===== */
.chat-area {
  display: flex;
  flex-direction: column;
  min-height: 0;
  flex: 1;
}

.chat-list {
  flex: 1;
  overflow-y: auto;
  padding: 12px 14px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.blank {
  margin: 24px 8px;
  text-align: center;
  color: var(--muted-2);
  font-size: 12.5px;
}

.chat-line {
  display: flex;
  gap: 8px;
  align-items: flex-end;
}

.chat-line.own {
  justify-content: flex-end;
}

.avatar {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border-radius: 999px;
  color: #fff;
  font-size: 10.5px;
  font-weight: 700;
}

.bubble {
  max-width: 78%;
  padding: 7px 11px 6px;
  border-radius: 12px;
  background: var(--surface-3);
}

.chat-line.own .bubble {
  background: var(--brand);
}

.who {
  display: block;
  font-size: 11px;
  font-weight: 700;
  margin-bottom: 2px;
}

.body {
  margin: 0;
  font-size: 13px;
  line-height: 1.45;
  color: var(--text);
  white-space: pre-wrap;
  word-break: break-word;
}

.chat-line.own .body {
  color: #fff;
}

.time {
  display: block;
  font-size: 10px;
  color: var(--muted-2);
  text-align: right;
  margin-top: 2px;
}

.chat-line.own .time {
  color: rgba(255, 255, 255, 0.75);
}

.load-older {
  appearance: none;
  border: none;
  border-bottom: 1px solid var(--border);
  background: var(--surface-2);
  color: var(--brand);
  font-size: 12px;
  font-weight: 600;
  padding: 8px;
  cursor: pointer;
}

.load-older:hover {
  text-decoration: underline;
}

.load-older:disabled {
  color: var(--muted-2);
  cursor: default;
}

.typing-line {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 14px 2px;
  font-size: 11.5px;
  color: var(--muted);
}

.typing-dot {
  width: 5px;
  height: 5px;
  border-radius: 999px;
  background: var(--muted-2);
  animation: typing-bounce 1.1s infinite ease-in-out;
}

.typing-dot:nth-child(2) {
  animation-delay: 0.15s;
}

.typing-dot:nth-child(3) {
  animation-delay: 0.3s;
}

@keyframes typing-bounce {
  0%,
  60%,
  100% {
    transform: translateY(0);
    opacity: 0.5;
  }
  30% {
    transform: translateY(-3px);
    opacity: 1;
  }
}

.mention {
  color: var(--brand);
  font-weight: 700;
  background: var(--accent-soft);
  border-radius: 4px;
  padding: 0 3px;
}

.emoji-panel {
  position: absolute;
  bottom: 52px;
  left: 10px;
  display: grid;
  grid-template-columns: repeat(8, 30px);
  gap: 2px;
  padding: 6px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: 10px;
  box-shadow: var(--shadow-lg);
  z-index: 5;
}

.emoji-cell {
  appearance: none;
  border: none;
  background: none;
  font-size: 17px;
  line-height: 30px;
  height: 30px;
  cursor: pointer;
  border-radius: 6px;
}

.emoji-cell:hover {
  background: var(--surface-2);
}

.send-emoji {
  font-size: 15px;
}

.chat-compose {
  position: relative;
  display: flex;
  gap: 8px;
  padding: 10px;
  border-top: 1px solid var(--border);
  background: var(--surface-2);
  align-items: flex-end;
}

.chat-compose textarea {
  flex: 1;
  resize: none;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text);
  font-size: 13px;
  font-family: inherit;
  padding: 8px 10px;
  max-height: 90px;
  outline: none;
}

.chat-compose textarea:focus {
  border-color: var(--brand);
}

.send {
  flex: none;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  border: none;
  border-radius: 999px;
  background: var(--brand);
  color: #fff;
  cursor: pointer;
}

.send:hover {
  background: var(--brand-strong);
}

.send:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

/* ===== Hoạt động ===== */
.activity-area {
  overflow-y: auto;
  padding: 10px 14px 14px;
}

.activity-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
}

.activity-list li {
  display: flex;
  gap: 10px;
  padding: 9px 0;
  border-bottom: 1px dashed var(--border);
}

.activity-list li:last-child {
  border-bottom: none;
}

.event-dot {
  flex: none;
  width: 9px;
  height: 9px;
  border-radius: 999px;
  margin-top: 5px;
}

.event-main {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.event-head {
  display: flex;
  align-items: baseline;
  gap: 6px;
  flex-wrap: wrap;
}

.event-actor {
  font-size: 12.5px;
  font-weight: 700;
}

.event-action {
  font-size: 12.5px;
  color: var(--text-soft);
  font-weight: 600;
}

.event-time {
  font-size: 10.5px;
  color: var(--muted-2);
  margin-left: auto;
}

.event-detail {
  font-size: 11.5px;
  color: var(--muted);
  line-height: 1.4;
}
</style>
