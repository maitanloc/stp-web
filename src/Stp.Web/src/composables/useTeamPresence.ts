import { ref } from 'vue'
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr'
import { getUserName, request } from '../api/client'
import { teamIdentity } from './useTeamIdentity'
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
