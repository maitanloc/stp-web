import { ref } from 'vue'
import { getUserName, request } from '../api/client'
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

/** Ai online (trang nào) — cập nhật sau mỗi ping. */
export const teamOnline = ref<PresenceEntryResponse[]>([])

/** Ai đang giữ dòng nào (toàn app). */
export const teamRowMarks = ref<PresenceRowMarkResponse[]>([])

// Trạng thái "đang đứng ở đâu" của chính mình — các view có dòng sửa sẽ cập nhật;
// để ở cấp module để vòng ping đọc được mà không cần qua event.
let currentSessionId: string | null = null
let currentEditingRow: number | null = null

let intervalId: ReturnType<typeof setInterval> | null = null
let lastPingAt = 0

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

export async function pingPresence(): Promise<void> {
  const actor = getUserName()
  if (!actor) {
    // Chưa khai tên: chỉ xoá dấu cũ, không ping (server cũng không biết ghi cho ai).
    teamOnline.value = []
    teamRowMarks.value = []
    return
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
    teamOnline.value = response.online
    teamRowMarks.value = response.rowMarks
  } catch {
    // API blip: giữ trạng thái cũ, nhịp tick sau thử lại.
  }
}

async function tick(): Promise<void> {
  const target = currentEditingRow != null ? PING_ACTIVE_MS : PING_IDLE_MS
  if (lastPingAt !== 0 && Date.now() - lastPingAt < target) return
  lastPingAt = Date.now()
  await pingPresence()
}

/** Bắt đầu vòng ping (gọi 1 lần ở TeamDock mount; gọi lại vô hại). */
export function startTeamPresence(): void {
  if (intervalId !== null) return
  lastPingAt = 0
  intervalId = setInterval(() => void tick(), TICK_MS)
  void tick()
}

/** Cập nhật tồn tại từ view có bảng dòng (gọi trong watch). */
export function setTeamEditingState(sessionId: string | null, rowNumber: number | null): void {
  const changed = currentSessionId !== sessionId || currentEditingRow !== rowNumber
  currentSessionId = sessionId
  currentEditingRow = rowNumber
  // Mở/đóng bảng sửa → phản ánh dấu dòng lập tức, khỏi chờ nhịp 3s.
  if (changed) {
    lastPingAt = 0
    void pingPresence()
  }
}

/** Rời trang: gỡ sạch trạng thái của mình (gọi trong onUnmounted của view). */
export function clearTeamEditingState(): void {
  setTeamEditingState(null, null)
}
