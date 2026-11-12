<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { MessageSquare, Printer, Search, Send, Users, X } from '@lucide/vue'
import { queryString, request } from '../api/client'
import type {
  DossierCommentResponse,
  DossierHistoryResponse,
  DossierSummaryResponse,
} from '../api/types'
import { formatDateTime, formatNumber, numberDisplay } from '../lib/format'
import { colorForName, renderAvatar } from '../lib/team'
import {
  lastDossierComment,
  lastGridCursor,
  lastGridFilter,
  sendGridCursor,
  sendGridFilter,
  teamOnline,
} from '../composables/useTeamPresence'
import { teamIdentity } from '../composables/useTeamIdentity'
import { useToast } from '../composables/useToast'

/**
 * Bảng sống kho hồ sơ (Trụ D — J1 + J2, plan "brainstorm-workspace-2-0"):
 *  - Virtualized: chỉ render ~40 dòng trong khung nhìn cho 67k dòng thật;
 *    dữ liệu lấy theo trang 200 khi cuộn (windowed fetch, cache theo trang).
 *  - Header đóng băng (sticky thead), resize từng cột (nhớ localStorage).
 *  - "Cả phòng nhìn một bảng": đổi lọc → broadcast hub cho đồng đội (chip
 *    "X đang lọc…" + Áp dụng theo); trỏ vào dòng → con trỏ màu của mình.
 *  - Comment theo dòng (J2): bong bóng 💬n cạnh dòng, mở panel ghi chú +
 *    lịch sử giá rẻ (sync gần nhất), realtime qua hub.
 * KHÔNG dùng Vue Transition (tab nền/automation: rAF kẹt — v-if thuần).
 */

const props = defineProps<{
  bookId: string
  year: string
  type: string
  search: string
}>()

const emit = defineEmits<{
  'apply-filter': [filter: { year: string; type: string; search: string }]
  'total-change': [total: number]
}>()

const toast = useToast()

// ---------------------------- Định nghĩa cột ----------------------------

interface GridCol {
  key: string
  label: string
  width: number
  min: number
  align?: 'num'
}

const DEFAULT_COLS: GridCol[] = [
  { key: 'number', label: 'Số hồ sơ', width: 150, min: 90 },
  { key: 'year', label: 'Năm', width: 72, min: 56 },
  { key: 'type', label: 'Loại', width: 150, min: 90 },
  { key: 'group', label: 'Nhóm', width: 110, min: 60 },
  { key: 'box', label: 'Hộp', width: 80, min: 52 },
  { key: 'summary', label: 'Trích yếu', width: 420, min: 180 },
  { key: 'docs', label: 'Văn bản', width: 84, min: 60, align: 'num' },
  { key: 'pages', label: 'Tờ', width: 70, min: 52, align: 'num' },
  { key: 'comment', label: '', width: 56, min: 44 },
]

const COLS_KEY = 'stp.liveGridCols'

const cols = ref<GridCol[]>(readCols())

function readCols(): GridCol[] {
  try {
    const raw = localStorage.getItem(COLS_KEY)
    const saved = raw ? (JSON.parse(raw) as Record<string, number>) : {}
    return DEFAULT_COLS.map((col) => ({
      ...col,
      width: typeof saved[col.key] === 'number' ? clampWidth(col, saved[col.key]) : col.width,
    }))
  } catch {
    return DEFAULT_COLS.map((col) => ({ ...col }))
  }
}

function clampWidth(col: GridCol, width: number): number {
  return Math.min(700, Math.max(col.min, Math.round(width)))
}

function persistCols(): void {
  try {
    const saved: Record<string, number> = {}
    for (const col of cols.value) saved[col.key] = col.width
    localStorage.setItem(COLS_KEY, JSON.stringify(saved))
  } catch {
    // Resize là sở thích — không lưu được thì bỏ qua.
  }
}

function resetCols(): void {
  cols.value = DEFAULT_COLS.map((col) => ({ ...col }))
  persistCols()
}

// Resize: mousedown trên mép cột → kéo → mouseup lưu.
const resizing = ref<{ key: string; startX: number; startWidth: number } | null>(null)

function startResize(event: MouseEvent, col: GridCol): void {
  event.preventDefault()
  event.stopPropagation()
  resizing.value = { key: col.key, startX: event.clientX, startWidth: col.width }
  window.addEventListener('mousemove', onResizeMove)
  window.addEventListener('mouseup', endResize)
}

function onResizeMove(event: MouseEvent): void {
  if (!resizing.value) return
  const col = cols.value.find((item) => item.key === resizing.value!.key)
  if (!col) return
  col.width = clampWidth(col, resizing.value.startWidth + event.clientX - resizing.value.startX)
}

function endResize(): void {
  window.removeEventListener('mousemove', onResizeMove)
  window.removeEventListener('mouseup', endResize)
  if (resizing.value) {
    persistCols()
    resizing.value = null
  }
}

// ---------------------------- Dữ liệu virtualized ----------------------------

const ROW_H = 44
const OVERSCAN = 6
const PAGE_SIZE = 200

const scrollHost = ref<HTMLDivElement | null>(null)
const totalRows = ref<number | null>(null)
const listError = ref<string | null>(null)

const windowStart = ref(0)
const windowEnd = ref(60)

const pageCache = ref(new Map<number, DossierSummaryResponse[]>())
const inFlight = new Map<number, Promise<void>>()

const spacers = computed(() => {
  const total = totalRows.value ?? 0
  const top = windowStart.value * ROW_H
  const visible = Math.min(windowEnd.value + 1, total) - windowStart.value
  const bottom = Math.max(0, (total - windowStart.value - visible) * ROW_H)
  return { top, bottom, visible }
})

const indexes = computed(() => {
  const total = totalRows.value ?? 0
  const list: number[] = []
  const from = windowStart.value
  const to = Math.min(windowEnd.value, total - 1)
  for (let index = from; index <= to; index += 1) list.push(index)
  return list
})

function rowAt(index: number): DossierSummaryResponse | undefined {
  const page = Math.floor(index / PAGE_SIZE)
  const items = pageCache.value.get(page)
  return items?.[index - page * PAGE_SIZE]
}

async function fetchPage(page: number): Promise<void> {
  if (pageCache.value.has(page) || inFlight.has(page)) return
  const task = (async () => {
    try {
      const query = queryString({
        year: props.year ? Number(props.year) : undefined,
        type: props.type || undefined,
        search: props.search.trim() || undefined,
        page: page + 1,
        pageSize: PAGE_SIZE,
      })
      const response = await request<{ items: DossierSummaryResponse[]; total: number }>(
        `/api/workbooks/${props.bookId}/dossiers${query}`,
      )
      pageCache.value.set(page, response.items)
      if (totalRows.value === null) {
        totalRows.value = response.total
        emit('total-change', response.total)
      }
      listError.value = null
    } catch (caught) {
      // Lỗi 1 trang: hiển thị, các trang khác vẫn dùng được.
      if (listError.value === null) {
        listError.value = caught instanceof Error ? caught.message : 'Không tải được hồ sơ'
      }
    } finally {
      inFlight.delete(page)
    }
  })()
  inFlight.set(page, task)
  await task
}

function ensureWindow(): void {
  const total = totalRows.value
  if (total === null) return
  const from = Math.floor(windowStart.value / PAGE_SIZE)
  const to = Math.floor(Math.min(windowEnd.value, Math.max(0, total - 1)) / PAGE_SIZE)
  for (let page = from; page <= to; page += 1) void fetchPage(page)
}

function onScroll(): void {
  const host = scrollHost.value
  if (!host) return
  const start = Math.max(0, Math.floor(host.scrollTop / ROW_H) - OVERSCAN)
  const end = Math.floor((host.scrollTop + host.clientHeight) / ROW_H) + OVERSCAN
  windowStart.value = start
  windowEnd.value = end
  ensureWindow()
}

function resetData(): void {
  pageCache.value = new Map()
  inFlight.clear()
  totalRows.value = null
  listError.value = null
  commentCountBumps.value = {}
  if (scrollHost.value) scrollHost.value.scrollTop = 0
  windowStart.value = 0
  windowEnd.value = 60
  void bootstrap()
}

async function bootstrap(): Promise<void> {
  // Lấy trang đầu để biết total (spacer + hiển thị), rồi fill khung nhìn.
  await fetchPage(0)
  ensureWindow()
}

let reloadToken = 0

watch(
  () => [props.bookId, props.year, props.type, props.search],
  () => {
    const token = ++reloadToken
    void Promise.resolve().then(() => {
      if (token === reloadToken) resetData()
    })
  },
)

onMounted(() => {
  void bootstrap()
})

onBeforeUnmount(() => {
  endResize()
})

// ---------------------------- Presence: cả phòng nhìn một bảng ----------------------------

const myName = computed(() => teamIdentity.value?.displayName ?? '')

/** Đang cùng xem khu kho (route /kho...) — loại trừ chính mình. */
const roommates = computed(() =>
  teamOnline.value.filter(
    (entry) =>
      entry.actor !== myName.value &&
      entry.where.length > 0 &&
      (entry.where.includes('Kho') || entry.where.includes('hồ sơ')),
  ),
)

// Chip lọc của đồng đội — TTL 5 phút, áp cùng workbook mới hiện.
const sharedFilter = ref<{
  actor: string
  year: string
  type: string
  search: string
  at: number
} | null>(null)

watch(lastGridFilter, (payload) => {
  if (!payload || payload.workbookId !== props.bookId) return
  if (payload.actor === myName.value) return
  sharedFilter.value = {
    actor: payload.actor,
    year: payload.year ? String(payload.year) : '',
    type: payload.type ?? '',
    search: payload.search ?? '',
    at: payload.at,
  }
})

// Đổi lọc của mình → broadcast (debounce 800ms) + chip đồng đội hết hạn ý nghĩa.
let filterSendTimer: number | undefined
watch(
  () => [props.bookId, props.year, props.type, props.search],
  () => {
    if (filterSendTimer) window.clearTimeout(filterSendTimer)
    filterSendTimer = window.setTimeout(() => {
      filterSendTimer = undefined
      if (!myName.value) return
      sendGridFilter(
        myName.value,
        props.bookId,
        props.year ? Number(props.year) : null,
        props.type || null,
        props.search.trim() || null,
      )
    }, 800)
  },
)

function adoptSharedFilter(): void {
  const shared = sharedFilter.value
  if (!shared) return
  emit('apply-filter', {
    year: shared.year,
    type: shared.type,
    search: shared.search,
  })
  sharedFilter.value = null
  toast.success('Đã theo bộ lọc của ' + shared.actor, 'Cả phòng đang nhìn một bảng.')
}

// Con trỏ cộng tác viên: dossierId → {actor, at}, TTL 6 giây.
const cursors = ref(new Map<string, { actor: string; at: number }>())

watch(lastGridCursor, (payload) => {
  if (!payload || payload.workbookId !== props.bookId) return
  if (payload.actor === myName.value) return
  const next = new Map(cursors.value)
  next.set(payload.dossierId, { actor: payload.actor, at: payload.at })
  cursors.value = next
})

let cursorSweepTimer: number | undefined

function sweepCursors(): void {
  const now = Date.now()
  let changed = false
  const next = new Map(cursors.value)
  for (const [dossierId, mark] of next) {
    if (now - mark.at > 6000) {
      next.delete(dossierId)
      changed = true
    }
  }
  if (changed) cursors.value = next
  cursorSweepTimer = window.setTimeout(sweepCursors, 2000)
}

onMounted(() => {
  cursorSweepTimer = window.setTimeout(sweepCursors, 2000)
})

onBeforeUnmount(() => {
  endResize()
  if (cursorSweepTimer) window.clearTimeout(cursorSweepTimer)
})

let lastCursorSent: { id: string; at: number } | null = null

function announceCursor(dossierId: string): void {
  if (!myName.value) return
  const now = Date.now()
  if (lastCursorSent && lastCursorSent.id === dossierId && now - lastCursorSent.at < 700) return
  lastCursorSent = { id: dossierId, at: now }
  sendGridCursor(myName.value, props.bookId, dossierId)
}

// ---------------------------- Comment theo dòng (J2) ----------------------------

const openCommentsId = ref<string | null>(null)
const commentsByDossier = ref(new Map<string, DossierCommentResponse[]>())
const commentsLoading = ref(false)
const commentCountBumps = ref<Record<string, number>>({})
const commentDraft = ref('')
const commentSending = ref(false)

function commentCountFor(row: DossierSummaryResponse): number {
  const bump = commentCountBumps.value[row.id]
  return typeof bump === 'number' ? bump : row.commentCount
}

async function toggleComments(row: DossierSummaryResponse): Promise<void> {
  if (openCommentsId.value === row.id) {
    openCommentsId.value = null
    return
  }
  openCommentsId.value = row.id
  if (!commentsByDossier.value.has(row.id)) {
    commentsLoading.value = true
    try {
      const items = await request<DossierCommentResponse[]>(
        `/api/dossiers/${row.id}/comments`,
      )
      commentsByDossier.value.set(row.id, items)
    } catch {
      commentsByDossier.value.set(row.id, [])
    } finally {
      commentsLoading.value = false
    }
  }
  void loadHistory(row)
}

async function sendComment(row: DossierSummaryResponse): Promise<void> {
  const body = commentDraft.value.trim()
  const actor = myName.value || 'Không rõ'
  if (body.length === 0 || commentSending.value) return
  commentSending.value = true
  try {
    const created = await request<DossierCommentResponse>(`/api/dossiers/${row.id}/comments`, {
      method: 'POST',
      body: { actor, body },
    })
    addCommentLocal(row.id, created)
    bumpCommentCount(row.id)
    commentDraft.value = ''
  } catch (caught) {
    toast.error('Không gửi được ghi chú', caught instanceof Error ? caught.message : 'Lỗi lạ')
  } finally {
    commentSending.value = false
  }
}

/** Thêm comment vào list local — dedupe theo id (hub broadcast có thể tới trước POST response). */
function addCommentLocal(dossierId: string, comment: DossierCommentResponse): void {
  const list = commentsByDossier.value.get(dossierId)
  if (list && list.some((item) => item.id === comment.id)) return
  commentsByDossier.value.set(dossierId, [...(list ?? []), comment])
}

/** Tăng đếm bong bóng: ưu tiên bump đã có, thiếu thì tra đếm gốc trong cache trang. */
function bumpCommentCount(dossierId: string): void {
  const current = commentCountBumps.value[dossierId]
  if (typeof current === 'number') {
    commentCountBumps.value = { ...commentCountBumps.value, [dossierId]: current + 1 }
    return
  }
  for (const items of pageCache.value.values()) {
    const row = items.find((item) => item.id === dossierId)
    if (row) {
      commentCountBumps.value = {
        ...commentCountBumps.value,
        [dossierId]: row.commentCount + 1,
      }
      return
    }
  }
}

// Comment mới của đồng đội qua hub → hiện ngay nếu đang mở đúng dòng.
watch(lastDossierComment, (payload) => {
  if (!payload) return
  addCommentLocal(payload.dossierId, { ...payload })
  bumpCommentCount(payload.dossierId)
})

// ---------------------------- Lịch sử giá rẻ (hover mở panel lần đầu) ----------------------------

const historyByDossier = ref(new Map<string, DossierHistoryResponse | null>())

async function loadHistory(row: DossierSummaryResponse): Promise<void> {
  if (historyByDossier.value.has(row.id)) return
  try {
    const history = await request<DossierHistoryResponse>(`/api/dossiers/${row.id}/history`)
    historyByDossier.value.set(row.id, history)
  } catch {
    historyByDossier.value.set(row.id, null)
  }
}

function historyLineFor(row: DossierSummaryResponse): string {
  const history = historyByDossier.value.get(row.id)
  if (!history) return 'Đang tải lịch sử…'
  const parts: string[] = []
  if (history.lastCommentAt) {
    parts.push(
      `${history.lastCommentActor ?? 'Ai đó'} vừa ghi chú ${formatDateTime(history.lastCommentAt)}`,
    )
  }
  if (history.lastSyncedAt) {
    const actor = history.lastSyncActor ? ` (bởi ${history.lastSyncActor})` : ''
    parts.push(
      `đồng bộ ${history.lastSyncChange === 'Added' ? 'thêm' : history.lastSyncChange === 'Deleted' ? 'xoá' : 'sửa'} ${formatDateTime(history.lastSyncedAt)}${actor}`,
    )
  }
  return parts.length > 0 ? parts.join(' · ') : 'Chưa có đồng bộ hay ghi chú nào chạm dòng này.'
}

const router = useRouter()

function openDossier(id: string): void {
  void router.push({ name: 'dossier', params: { id } })
}

defineExpose({ resetData })
</script>

<template>
  <div class="live-grid">
    <!-- Thanh presence + chip lọc đồng đội -->
    <div class="live-grid-toolbar">
      <div class="live-grid-room">
        <Users :size="14" class="muted" />
        <span class="text-xs muted">
          Bảng sống · {{ totalRows === null ? '…' : formatNumber(totalRows) }} hồ sơ
        </span>
        <span v-if="roommates.length > 0" class="live-grid-avatars">
          <span
            v-for="entry in roommates.slice(0, 4)"
            :key="entry.actor"
            class="tag-avatar"
            :title="`${entry.actor} · ${entry.where}`"
            :style="renderAvatar(entry.actor).style"
          >
            <img
              v-if="renderAvatar(entry.actor).imageUrl"
              :src="renderAvatar(entry.actor).imageUrl"
              alt=""
              class="avatar-img"
              loading="lazy"
            />
            <template v-else>{{ renderAvatar(entry.actor).initials }}</template>
          </span>
          <span v-if="roommates.length > 4" class="tag-avatar more">
            +{{ roommates.length - 4 }}
          </span>
        </span>
      </div>
      <div
        v-if="sharedFilter && Date.now() - sharedFilter.at < 5 * 60_000"
        class="shared-filter-chip"
      >
        <span class="shared-filter-dot" />
        <strong>{{ sharedFilter.actor }}</strong>
        <span class="muted">đang lọc:</span>
        <span class="shared-filter-detail">
          {{
            [
              sharedFilter.year ? 'năm ' + sharedFilter.year : '',
              sharedFilter.type,
              sharedFilter.search ? `“${sharedFilter.search}”` : '',
            ].filter(Boolean).join(' · ') || 'tất cả'
          }}
        </span>
        <button class="btn btn-secondary btn-xs" type="button" @click="adoptSharedFilter">
          Áp dụng theo
        </button>
        <button
          class="btn btn-ghost btn-xs"
          type="button"
          title="Bỏ qua gợi ý này"
          @click="sharedFilter = null"
        >
          <X :size="12" />
        </button>
      </div>
      <button
        class="btn btn-ghost btn-xs"
        type="button"
        title="Trả cột về kích thước gốc"
        @click="resetCols"
      >
        Vừa khung
      </button>
    </div>

    <div v-if="listError" class="alert alert-danger" style="margin-top: 10px">
      <div class="grow">{{ listError }} — cuộn tiếp hoặc đổi lọc để thử lại.</div>
    </div>

    <!-- Khung cuộn: chỉ render ~40 dòng trong khung nhìn -->
    <div
      ref="scrollHost"
      class="live-grid-scroll"
      :class="{ 'is-resizing': resizing !== null }"
      @scroll="onScroll"
    >
      <table class="live-grid-table">
        <colgroup>
          <col
            v-for="col in cols"
            :key="col.key"
            :style="{ width: col.width + 'px' }"
          />
        </colgroup>
        <thead>
          <tr>
            <th
              v-for="col in cols"
              :key="col.key"
              :class="{ num: col.align === 'num' }"
            >
              <span class="th-label">{{ col.label }}</span>
              <span
                class="col-resizer"
                :aria-label="`Đổi rộng cột ${col.label}`"
                @mousedown="startResize($event, col)"
              />
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="spacers.top > 0" class="spacer-row" :style="{ height: spacers.top + 'px' }">
            <td :colspan="cols.length" />
          </tr>

          <template v-for="index in indexes" :key="index">
            <tr
              v-if="rowAt(index)"
              class="grid-row"
              :class="{ 'has-cursor': cursors.has(rowAt(index)!.id) }"
              @click="openDossier(rowAt(index)!.id)"
              @mouseenter="announceCursor(rowAt(index)!.id)"
            >
              <td>
                <span class="strong">{{ numberDisplay(rowAt(index)!) }}</span>
                <span class="cell-sub">ML {{ rowAt(index)!.mlRow }}</span>
              </td>
              <td>{{ rowAt(index)!.year }}</td>
              <td><span class="badge badge-brand cell-badge">{{ rowAt(index)!.type || 'Chưa phân loại' }}</span></td>
              <td>{{ rowAt(index)!.group || '—' }}</td>
              <td>{{ rowAt(index)!.boxNumber || '—' }}</td>
              <td class="summary-cell" :title="rowAt(index)!.originalSummary">
                <span v-if="cursors.has(rowAt(index)!.id)" class="cursor-chip" :style="{ background: colorForName(cursors.get(rowAt(index)!.id)!.actor) }">
                  {{ cursors.get(rowAt(index)!.id)!.actor }} đang xem
                </span>
                {{ rowAt(index)!.originalSummary || '—' }}
              </td>
              <td class="num">{{ formatNumber(rowAt(index)!.documentCount) }}</td>
              <td class="num">
                <Printer
                  v-if="rowAt(index)!.hasPrintLayout"
                  :size="13"
                  class="print-ok"
                  title="Có dữ liệu in"
                />
                <span v-else class="muted">{{ rowAt(index)!.totalPages > 0 ? formatNumber(rowAt(index)!.totalPages) : '—' }}</span>
              </td>
              <td>
                <button
                  type="button"
                  class="comment-bubble"
                  :class="{ 'has-comments': commentCountFor(rowAt(index)!) > 0 }"
                  :title="commentCountFor(rowAt(index)!) > 0 ? `${commentCountFor(rowAt(index)!)} ghi chú — mở panel` : 'Thêm ghi chú cho dòng này'"
                  @click.stop="toggleComments(rowAt(index)!)"
                >
                  <MessageSquare :size="13" />
                  <span v-if="commentCountFor(rowAt(index)!) > 0">{{ commentCountFor(rowAt(index)!) }}</span>
                </button>
              </td>
            </tr>
            <tr v-else class="grid-row skeleton-row">
              <td v-for="col in cols" :key="col.key"><span class="skeleton-cell">…</span></td>
            </tr>

            <!-- Panel comment + lịch sử của dòng (J2) -->
            <tr v-if="rowAt(index) && openCommentsId === rowAt(index)!.id" class="comment-row">
              <td :colspan="cols.length">
                <div class="comment-panel">
                  <div class="comment-panel-head">
                    <strong>Ghi chú dòng {{ numberDisplay(rowAt(index)!) }}</strong>
                    <span class="text-xs muted" :title="historyLineFor(rowAt(index)!)">
                      {{ historyLineFor(rowAt(index)!) }}
                    </span>
                    <button class="btn btn-ghost btn-xs" type="button" @click="openCommentsId = null">
                      <X :size="12" /> Đóng
                    </button>
                  </div>
                  <div class="comment-list">
                    <p v-if="commentsLoading" class="text-xs muted">Đang tải ghi chú…</p>
                    <template v-else>
                      <p
                        v-for="comment in commentsByDossier.get(rowAt(index)!.id) ?? []"
                        :key="comment.id"
                        class="comment-item"
                      >
                        <span
                          class="tag-avatar"
                          :style="renderAvatar(comment.actor).style"
                        >
                          <img
                            v-if="renderAvatar(comment.actor).imageUrl"
                            :src="renderAvatar(comment.actor).imageUrl"
                            alt=""
                            class="avatar-img"
                            loading="lazy"
                          />
                          <template v-else>{{ renderAvatar(comment.actor).initials }}</template>
                        </span>
                        <span class="comment-meta">
                          <strong>{{ comment.actor }}</strong>
                          <span class="text-xs muted">{{ formatDateTime(comment.createdAt) }}</span>
                        </span>
                        <span class="comment-body">{{ comment.body }}</span>
                      </p>
                      <p
                        v-if="(commentsByDossier.get(rowAt(index)!.id) ?? []).length === 0"
                        class="text-xs muted"
                      >
                        Chưa ai ghi chú dòng này — thêm một câu để nhắc đồng đội.
                      </p>
                    </template>
                  </div>
                  <div class="comment-composer">
                    <textarea
                      v-model="commentDraft"
                      class="input"
                      rows="2"
                      placeholder="Ví dụ: hồ sơ này thiếu số tờ, nhờ Lan check giùm… (Enter để gửi)"
                      @keydown.enter.exact.prevent="sendComment(rowAt(index)!)"
                    />
                    <button
                      class="btn btn-primary btn-sm"
                      type="button"
                      :disabled="commentSending || commentDraft.trim().length === 0"
                      @click="sendComment(rowAt(index)!)"
                    >
                      <Send :size="13" />
                      {{ commentSending ? 'Đang gửi…' : 'Ghi chú' }}
                    </button>
                  </div>
                </div>
              </td>
            </tr>
          </template>

          <tr v-if="spacers.bottom > 0" class="spacer-row" :style="{ height: spacers.bottom + 'px' }">
            <td :colspan="cols.length" />
          </tr>
        </tbody>
      </table>

      <div
        v-if="totalRows === 0"
        class="live-grid-empty"
      >
        <Search :size="18" class="muted" />
        Không có hồ sơ khớp bộ lọc — thử xoá lọc hoặc đổi năm/loại.
      </div>
    </div>

    <div class="live-grid-foot">
      <span class="text-xs muted">
        Cuộn để tải thêm (mỗi trang {{ formatNumber(PAGE_SIZE) }} hồ sơ) — click dòng để mở chi tiết.
      </span>
    </div>
  </div>
</template>
