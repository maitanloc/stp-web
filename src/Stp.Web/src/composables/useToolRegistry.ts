import { computed, ref } from 'vue'
import {
  Combine,
  FileSpreadsheet,
  FolderOpen,
  LayoutDashboard,
  ListChecks,
  RefreshCw,
  Users,
  Wrench,
} from '@lucide/vue'
import { request } from '../api/client'
import type { OverviewResponse, SummarySessionInfoResponse } from '../api/types'
import { teamIdentity } from './useTeamIdentity'

/**
 * Tool registry (H1 + H2): một nguồn duy nhất khai báo mọi tool của shell —
 * sidebar hiển thị kèm badge đếm động, Ctrl+K palette dùng để nhảy tool.
 * Thêm tool mới = thêm 1 object, badge tự gắn theo `badgeKey`.
 */

export interface ToolDef {
  label: string
  to: string
  icon: typeof LayoutDashboard
  group: 'Quy trình' | 'Tra cứu' | 'Hệ thống'
  /** Cờ nhỏ gắn cạnh tên: số phiên / số job đang chạy / tổng hồ sơ. */
  badgeKey?: 'summary' | 'jobs' | 'archive'
  /** Chỉ hiện cho quản trị viên. */
  adminOnly?: boolean
}

export interface ToolSection {
  label: ToolDef['group']
  items: ToolDef[]
}

const TOOLS: ToolDef[] = [
  { label: 'Tổng quan', to: '/', icon: LayoutDashboard, group: 'Quy trình' },
  { label: 'Chuyển đổi Excel', to: '/chuyen-doi', icon: FileSpreadsheet, group: 'Quy trình' },
  { label: 'Gộp nhiều file', to: '/gop-file', icon: Combine, group: 'Quy trình' },
  { label: 'Bổ sung trích yếu', to: '/trich-yeu', icon: ListChecks, group: 'Quy trình', badgeKey: 'summary' },
  { label: 'Đồng bộ từ Excel', to: '/dong-bo', icon: RefreshCw, group: 'Quy trình', badgeKey: 'jobs' },
  { label: 'Kho hồ sơ', to: '/kho', icon: FolderOpen, group: 'Tra cứu', badgeKey: 'archive' },
  { label: 'Quản lý tài khoản', to: '/quan-ly', icon: Users, group: 'Hệ thống', adminOnly: true },
  { label: 'Không gian nâng cao', to: '/nang-cao', icon: Wrench, group: 'Hệ thống' },
]

// ---------------------------- Badge đếm (H1) ----------------------------

const overview = ref<OverviewResponse | null>(null)
const summarySessions = ref<SummarySessionInfoResponse[] | null>(null)
let badgeTimer: number | undefined

export function toolBadge(tool: ToolDef): string | null {
  if (!tool.badgeKey) return null
  if (tool.badgeKey === 'jobs') {
    const running = overview.value?.runningJobCount ?? 0
    return running > 0 ? String(running) : null
  }
  if (tool.badgeKey === 'archive') {
    const total = overview.value?.dossierCount ?? 0
    return total > 0 ? formatBadge(total) : null
  }
  // Trích yếu: số phiên đã inspect gần đây (lịch sử tool).
  const count = summarySessions.value?.length ?? 0
  return count > 0 ? String(Math.min(count, 99)) : null
}

/** 67.718 → "67,7k"; dưới 1000 giữ nguyên. */
function formatBadge(value: number): string {
  if (value >= 1000) {
    const k = Math.round(value / 100) / 10
    return `${k.toLocaleString('vi-VN')}k`
  }
  return String(value)
}

async function loadBadges(): Promise<void> {
  // Badge job/kho và phiên trích yếu: mỗi API 1 nhịp, lỗi thì badge im lặng.
  const overviewTask = request<OverviewResponse>('/api/overview')
    .then((data) => (overview.value = data))
    .catch(() => undefined)
  const sessionsTask = request<SummarySessionInfoResponse[]>('/api/summary/sessions')
    .then((data) => (summarySessions.value = data))
    .catch(() => undefined)
  await Promise.allSettled([overviewTask, sessionsTask])
}

/** Nhịp badge 60 giây — trùng cadence health chip, không thêm gánh API. */
export function startBadgePolling(): void {
  if (badgeTimer) return
  void loadBadges()
  badgeTimer = window.setInterval(() => void loadBadges(), 60_000)
}

export function stopBadgePolling(): void {
  if (badgeTimer) {
    window.clearInterval(badgeTimer)
    badgeTimer = undefined
  }
}

// ---------------------------- Ghim tool ưa thích (H1) ----------------------------

const PIN_KEY = 'stp.pinnedTools'
const pinned = ref<string[]>(readPins())

function readPins(): string[] {
  try {
    const raw = localStorage.getItem(PIN_KEY)
    const list = raw ? (JSON.parse(raw) as unknown) : []
    return Array.isArray(list) ? list.filter((item): item is string => typeof item === 'string') : []
  } catch {
    return []
  }
}

function persistPins(): void {
  try {
    localStorage.setItem(PIN_KEY, JSON.stringify(pinned.value))
  } catch {
    // Bỏ qua — ghim là sở thích, mất cũng không sao.
  }
}

export function isPinned(tool: ToolDef): boolean {
  return pinned.value.includes(tool.to)
}

export function togglePin(tool: ToolDef): void {
  pinned.value = isPinned(tool)
    ? pinned.value.filter((to) => to !== tool.to)
    : [...pinned.value, tool.to]
  persistPins()
}

/** Ghim nổi lên đầu trong đúng nhóm của nó, các tool còn lại giữ thứ tự. */
export function pinnedFirst(items: ToolDef[]): ToolDef[] {
  return [...items].sort(
    (a, b) => Number(isPinned(b)) - Number(isPinned(a)),
  )
}

// ---------------------------- Sections dùng chung ----------------------------

export const toolSections = computed<ToolSection[]>(() =>
  ['Quy trình', 'Tra cứu', 'Hệ thống'].map((label) => ({
    label: label as ToolSection['label'],
    items: pinnedFirst(
      TOOLS.filter(
        (tool) =>
          tool.group === label && (!tool.adminOnly || teamIdentity.value?.role === 'admin'),
      ),
    ),
  })),
)

export const allTools = computed<ToolDef[]>(() =>
  TOOLS.filter((tool) => !tool.adminOnly || teamIdentity.value?.role === 'admin'),
)
