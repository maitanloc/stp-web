import { ref } from 'vue'
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr'
import { getUserName, request } from '../api/client'
import { teamIdentity } from './useTeamIdentity'
import { avatarMap, fetchAvatarMap } from './useAvatarUpload'
import type {
  PresenceEntryResponse,
  PresencePingResponse,
  PresenceRowMarkResponse,
} from '../api/types'

// Nhịp ping: 8 giây khi đang mở/đang sửa dòng, 20 giây khi chỉ đứng xem.
// Tick 3 giây, đáo hạn mới gọi — mạng chập chờn cũng không dồn request.
const TICK_MS = 3000
const PING_ACTIVE_MS = 8000
const PING_IDLE_MS = 20000
// Hub sống thì ping giữ nhiệt nhàn hơn hẳn — broadcast đẩy cập nhật tức thời,
// poll chỉ còn là dự phòng khi hub rớt.
const PING_HUB_LIVE_MS = 40000

/** Sự kiện hành động broadcast qua hub (F3) — ai vừa làm gì, tức thời. */
export interface LiveActivity {
  actor: string
  action: string
  entityType?: string | null
  entityId?: string | null
  details?: string | null
  timestamp: string
}

/** Ai online (trang nào) — cập nhật sau mỗi ping hoặc broadcast hub. */
export const teamOnline = ref<PresenceEntryResponse[]>([])

/** Ai đang giữ dòng nào (toàn app). */
export const teamRowMarks = ref<PresenceRowMarkResponse[]>([])

/** F1: trạng thái hub realtime — 'live' khi SignalR nối được qua tunnel. */
export const hubStatus = ref<'off' | 'connecting' | 'live'>('off')

/** F3: sự kiện hành động đổ về realtime (mới nhất đầu tiên). */
export const liveActivities = ref<LiveActivity[]>([])

/** F3: chip hành động đáng kể nổi lên cho đồng đội, tự tan sau 8 giây. */
export const actionChips = ref<Array<LiveActivity & { key: number }>>([])

/** G2: thông báo đẩy qua hub — bell tự lọc forUser === mình và cập nhật badge. */
export interface LiveNotification {
  forUser: string
  kind: string
  title: string
  body: string
  link: string | null
  timestamp: string
}

export const lastLiveNotification = ref<LiveNotification | null>(null)

/** G3: người vừa bấm phím chat (hub đẩy về) — dock hiển thị "đang gõ…" 5s. */
export const lastTypingActor = ref<{ actor: string; at: number } | null>(null)

/** J1: bộ lọc mới nhất của đồng đội trên bảng sống kho hồ sơ (theo workbook). */
export interface GridFilterShare {
  actor: string
  workbookId: string
  year: number | null
  type: string | null
  search: string | null
  at: number
}

export const lastGridFilter = ref<GridFilterShare | null>(null)

/** J1: con trỏ cộng tác viên — ai đang trỏ vào hồ sơ nào (TTL ngắn phía nhận). */
export interface GridCursorMark {
  actor: string
  workbookId: string
  dossierId: string
  at: number
}

export const lastGridCursor = ref<GridCursorMark | null>(null)

/** J2: comment vừa được ai đó thêm (bong bóng hiện realtime trên bảng sống). */
export interface DossierCommentLive {
  id: string
  dossierId: string
  actor: string
  body: string
  createdAt: string
}

export const lastDossierComment = ref<DossierCommentLive | null>(null)

/** Sự kiện con trỏ ô Google Sheets: ai đang click vào ô nào */
export interface SummaryCellFocusEvent {
  sessionId: string
  rowNumber: number
  column: string
  actor: string
}

/** Sự kiện gõ chữ realtime: text đang gõ chạy theo thời gian thực */
export interface SummaryCellTypingEvent {
  sessionId: string
  rowNumber: number
  column: string
  text: string
  actor: string
}

/** Sự kiện ô vừa được lưu / áp chip / gõ xong */
export interface SummaryCellChangedEvent {
  sessionId: string
  rowNumber: number
  items: string[]
  summaryText: string | null
  sheetNumber: string | null
  documentNumber: string | null
  numberLabel: string | null
  actor: string
  timestamp: string
}

/** Sự kiện áp hàng loạt (bulk / áp bộ loại) */
export interface SummaryBulkChangedEvent {
  sessionId: string
  count: number
  actor: string
  timestamp: string
}

/** Sự kiện file làm chung của phòng vừa được đổi/tải lên */
export interface TeamSessionChangedEvent {
  sessionId: string
  fileName: string
  uploadedBy: string
  uploadedAt: string
  rowCount: number
  editCount: number
}

export const lastSummaryCellFocus = ref<SummaryCellFocusEvent | null>(null)
export const lastSummaryCellTyping = ref<SummaryCellTypingEvent | null>(null)
export const lastSummaryCellChanged = ref<SummaryCellChangedEvent | null>(null)
export const lastSummaryBulkChanged = ref<SummaryBulkChangedEvent | null>(null)
export const lastTeamSessionChanged = ref<TeamSessionChangedEvent | null>(null)

export function joinSummarySession(sessionId: string, actor: string): void {
  if (hubConnection && hubConnection.state === 'Connected') {
    void hubConnection.invoke('joinSummarySession', sessionId, actor).catch(() => {})
  }
}

export function leaveSummarySession(sessionId: string, actor: string): void {
  if (hubConnection && hubConnection.state === 'Connected') {
    void hubConnection.invoke('leaveSummarySession', sessionId, actor).catch(() => {})
  }
}

export function sendSummaryCellFocus(sessionId: string, rowNumber: number, column: string, actor: string): void {
  if (hubConnection && hubConnection.state === 'Connected') {
    void hubConnection.invoke('summaryCellFocus', sessionId, rowNumber, column, actor).catch(() => {})
  }
}

export function sendSummaryCellTyping(sessionId: string, rowNumber: number, column: string, text: string, actor: string): void {
  if (hubConnection && hubConnection.state === 'Connected') {
    void hubConnection.invoke('summaryCellTyping', sessionId, rowNumber, column, text, actor).catch(() => {})
  }
}

export function sendSummaryCellCommit(
  sessionId: string,
  request: { rowNumber: number; items: string[]; summaryText?: string | null; sheetNumber?: string | null; documentNumber?: string | null; numberLabel?: string | null },
  actor: string,
): void {
  if (hubConnection && hubConnection.state === 'Connected') {
    void hubConnection.invoke('summaryCellCommit', sessionId, request, actor).catch(() => {})
  }
}

/** J1: gửi bộ lọc bảng sống của mình cho cả nhóm (debounce ở nơi gọi). */
export function sendGridFilter(
  actor: string,
  workbookId: string,
  year: number | null,
  type: string | null,
  search: string | null,
): void {
  if (hubConnection && hubConnection.state === 'Connected') {
    void hubConnection
      .invoke('gridFilter', actor, workbookId, year, type, search)
      .catch(() => {})
  }
}

/** J1: báo "mình đang trỏ dòng này" — throttle 700ms ở nơi gọi. */
export function sendGridCursor(actor: string, workbookId: string, dossierId: string): void {
  if (hubConnection && hubConnection.state === 'Connected') {
    void hubConnection.invoke('gridCursor', actor, workbookId, dossierId).catch(() => {})
  }
}

/** G3: báo "mình đang gõ" cho cả nhóm qua hub — gọi throttle 2s ở nơi gõ chat. */
export function sendTeamTyping(actor: string): void {
  if (hubConnection && hubConnection.state === 'Connected') {
    void hubConnection.invoke('typing', actor).catch(() => {})
  }
}

const SIGNIFICANT_ACTIONS = new Set([
  'export',
  'export-filtered',
  'export-print',
  'sync',
  'bulk',
])

// Trạng thái "đang đứng ở đâu" của chính mình — các view có dòng sửa sẽ cập nhật;
// để ở cấp module để vòng ping đọc được mà không cần qua event.
let currentSessionId: string | null = null
let currentEditingRow: number | null = null

let intervalId: ReturnType<typeof setInterval> | null = null
let lastPingAt = 0
let hubConnection: HubConnection | null = null

/** Nhãn "where" — tên trang route.user đang đứng. */
function whereLabel(): string {
  if (typeof window === 'undefined') return ''
  const path = window.location.pathname
  const map: Array<[RegExp, string]> = [
    [/\/trich-yeu/, 'Bổ sung trích yếu'],
    [/\/dong-bo/, 'Đồng bộ kho'],
    [/\/kho/, 'Kho hồ sơ'],
    [/\/ho-so/, 'Chi tiết hồ sơ'],
    [/\/chuyen-doi/, 'Chuyển đổi Excel'],
    [/\/gop-file/, 'Gộp nhiều file'],
    [/\/in-hang-loat/, 'In hàng loạt'],
    [/\/nang-cao/, 'Không gian nâng cao'],
  ]
  for (const [pattern, label] of map) {
    if (pattern.test(path)) return label
  }
  return path.replace(/^\//, '') || 'Tổng quan'
}

function applyPresenceResponse(response: PresencePingResponse): void {
  teamOnline.value = response.online
  teamRowMarks.value = response.rowMarks
}

export async function pingPresence(): Promise<void> {
  // Đã đăng nhập thì presence gắn đúng tài khoản; chưa thì dùng tên tự khai như cũ.
  const actor = teamIdentity.value?.displayName ?? getUserName()
  if (!actor) {
    // Chưa khai tên: chỉ xoá dấu cũ, không ping (server cũng không biết ghi cho ai).
    teamOnline.value = []
    teamRowMarks.value = []
    return
  }

  // F1: ưu tiên hub realtime — invoke trả trạng thái cho mình và server
  // broadcast cho mọi client cùng lúc. Thất bại thì lùi về REST như cũ.
  if (hubConnection && hubConnection.state === 'Connected') {
    try {
      const response = await hubConnection.invoke<PresencePingResponse>(
        'ping',
        actor,
        whereLabel(),
        currentSessionId,
        currentEditingRow,
      )
      applyPresenceResponse(response)
      return
    } catch {
      // Hub blip: nhịp sau thử lại, tick này đi đường REST.
    }
  }

  try {
    const response = await request<PresencePingResponse>('/api/team/presence', {
      method: 'POST',
      body: {
        actor,
        where: whereLabel(),
        sessionId: currentSessionId,
        rowNumber: currentEditingRow,
      },
    })
    applyPresenceResponse(response)
  } catch {
    // API blip: giữ trạng thái cũ, nhịp tick sau thử lại.
  }
}

async function tick(): Promise<void> {
  const hubLive = hubConnection?.state === 'Connected'
  const target = hubLive
    ? PING_HUB_LIVE_MS
    : currentEditingRow != null
      ? PING_ACTIVE_MS
      : PING_IDLE_MS
  if (lastPingAt !== 0 && Date.now() - lastPingAt < target) return
  lastPingAt = Date.now()
  await pingPresence()
}

/* ===== F1: hub SignalR — nối /hubs/team, nghe presence + activity realtime ===== */

async function startTeamHub(): Promise<void> {
  if (typeof window === 'undefined' || hubConnection) return

  hubStatus.value = 'connecting'
  try {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/team')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .build()

    connection.on('presence', (payload: PresencePingResponse) => {
      applyPresenceResponse(payload)
    })

    connection.on('activity', (payload: LiveActivity) => {
      if (!payload?.actor || !payload?.action) return
      liveActivities.value = [payload, ...liveActivities.value].slice(0, 100)
      if (SIGNIFICANT_ACTIONS.has(payload.action.split(':')[0])) {
        const key = Date.now() + Math.random()
        actionChips.value = [...actionChips.value.slice(-3), { ...payload, key }]
        window.setTimeout(() => {
          actionChips.value = actionChips.value.filter((chip) => chip.key !== key)
        }, 8000)
      }
    })

    // G2: bell realtime — server chỉ định người nhận, client tự lọc của mình.
    connection.on('notification', (payload: {
      forUser?: string
      kind?: string
      title?: string
      body?: string
      link?: string
      timestamp?: string
    }) => {
      if (!payload?.forUser) return
      lastLiveNotification.value = {
        forUser: payload.forUser,
        kind: payload.kind ?? 'system',
        title: payload.title ?? '',
        body: payload.body ?? '',
        link: payload.link ?? null,
        timestamp: payload.timestamp ?? new Date().toISOString(),
      }
    })

    // G3: "ai đang gõ" — sự kiện thoáng qua, dock tự quản TTL hiển thị.
    connection.on('typing', (payload: { actor?: string }) => {
      if (payload?.actor) {
        lastTypingActor.value = { actor: payload.actor, at: Date.now() }
      }
    })

    // J1: bộ lọc mới nhất của đồng đội trên bảng sống.
    connection.on('gridFilter', (payload: {
      actor?: string
      workbookId?: string
      year?: number | null
      type?: string | null
      search?: string | null
    }) => {
      if (!payload?.actor || !payload?.workbookId) return
      lastGridFilter.value = {
        actor: payload.actor,
        workbookId: payload.workbookId,
        year: payload.year ?? null,
        type: payload.type ?? null,
        search: payload.search ?? null,
        at: Date.now(),
      }
    })

    // J1: con trỏ cộng tác viên trên bảng sống.
    connection.on('gridCursor', (payload: {
      actor?: string
      workbookId?: string
      dossierId?: string
    }) => {
      if (!payload?.actor || !payload?.workbookId || !payload?.dossierId) return
      lastGridCursor.value = {
        actor: payload.actor,
        workbookId: payload.workbookId,
        dossierId: payload.dossierId,
        at: Date.now(),
      }
    })

    // J2: comment mới của đồng đội — bảng sống tự thêm bong bóng.
    connection.on('dossierComment', (payload: {
      id?: string
      dossierId?: string
      actor?: string
      body?: string
      createdAt?: string
    }) => {
      if (!payload?.id || !payload?.dossierId) return
      lastDossierComment.value = {
        id: payload.id,
        dossierId: payload.dossierId,
        actor: payload.actor ?? 'Ai đó',
        body: payload.body ?? '',
        createdAt: payload.createdAt ?? new Date().toISOString(),
      }
    })

    // K1: ảnh đại diện của thành viên vừa được cập nhật — nạp lại URL ảnh tức thời.
    connection.on('teamAvatar', (payload: {
      userId?: string
      displayName?: string
      avatarVersion?: number
    }) => {
      if (payload?.displayName && payload?.userId && payload?.avatarVersion) {
        avatarMap.value = {
          ...avatarMap.value,
          [payload.displayName]: `/api/team/avatar/${payload.userId}?v=${payload.avatarVersion}`,
        }
      }
    })

    // Multiplayer: phiên làm việc chung của cả phòng được cập nhật / đổi file
    connection.on('teamSessionChanged', (payload: TeamSessionChangedEvent) => {
      lastTeamSessionChanged.value = payload
    })

    // Multiplayer: con trỏ ô Google Sheets của đồng đội
    connection.on('summaryCellFocus', (payload: SummaryCellFocusEvent) => {
      lastSummaryCellFocus.value = payload
    })

    // Multiplayer: gõ chữ realtime từ đồng đội
    connection.on('summaryCellTyping', (payload: SummaryCellTypingEvent) => {
      lastSummaryCellTyping.value = payload
    })

    // Multiplayer: ô vừa được lưu / áp chip / gõ xong từ đồng đội
    connection.on('summaryCellChanged', (payload: SummaryCellChangedEvent) => {
      lastSummaryCellChanged.value = payload
    })

    // Multiplayer: thay đổi hàng loạt (áp bộ loại / bulk)
    connection.on('summaryBulkChanged', (payload: SummaryBulkChangedEvent) => {
      lastSummaryBulkChanged.value = payload
    })

    connection.onreconnecting(() => {
      hubStatus.value = 'connecting'
    })

    connection.onreconnected(async () => {
      hubStatus.value = 'live'
      lastPingAt = 0
      await pingPresence()
    })

    connection.onclose(() => {
      hubStatus.value = 'off'
    })

    await connection.start()
    hubConnection = connection
    hubStatus.value = 'live'
    // Nối hub là gửi ping ngay — cả nhóm thấy mình trong ~1 giây.
    await pingPresence()
  } catch {
    // Hub không nối được (tunnel chặn, mạng chập chờn) — poll REST vẫn chạy.
    hubStatus.value = 'off'
    hubConnection = null
  }
}

/** Bắt đầu vòng ping + hub realtime (gọi 1 lần ở TeamDock mount; gọi lại vô hại). */
export function startTeamPresence(): void {
  void fetchAvatarMap()
  if (intervalId !== null) return
  lastPingAt = 0
  intervalId = setInterval(() => void tick(), TICK_MS)
  void tick()
  void startTeamHub()
}

/** F2: throttle 300ms cho ping "lập tức" khi nhảy nhanh qua các dòng. */
const INSTANT_PING_THROTTLE_MS = 300
let lastInstantPingAt = 0
let throttleTimer: ReturnType<typeof setTimeout> | null = null

/** Cập nhật tồn tại từ view có bảng dòng (gọi trong watch). */
export function setTeamEditingState(sessionId: string | null, rowNumber: number | null): void {
  const changed = currentSessionId !== sessionId || currentEditingRow !== rowNumber
  currentSessionId = sessionId
  currentEditingRow = rowNumber
  // Mở/đóng bảng sửa → phản ánh dấu dòng lập tức, khỏi chờ nhịp 3s —
  // nhưng nhảy liên tục thì chỉ ping mỗi 300ms một lần.
  if (!changed) return
  const now = Date.now()
  const elapsed = now - lastInstantPingAt
  if (throttleTimer) window.clearTimeout(throttleTimer)
  if (elapsed >= INSTANT_PING_THROTTLE_MS) {
    lastInstantPingAt = now
    void pingPresence()
  } else {
    throttleTimer = setTimeout(() => {
      throttleTimer = null
      lastInstantPingAt = Date.now()
      void pingPresence()
    }, INSTANT_PING_THROTTLE_MS - elapsed)
  }
}

/** Rời trang: gỡ sạch trạng thái của mình (gọi trong onUnmounted của view). */
export function clearTeamEditingState(): void {
  setTeamEditingState(null, null)
}
