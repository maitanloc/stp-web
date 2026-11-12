<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  ArrowLeft,
  ArrowRight,
  Check,
  CircleCheck,
  Clock,
  Database,
  Download,
  Eraser,
  FilePenLine,
  FileSpreadsheet,
  Info,
  Mic,
  Radio,
  RefreshCw,
  Search,
  Star,
  TriangleExclamationPoint,
  Users,
  X,
} from '@lucide/vue'
import { download, queryString, request, saveBlob } from '../api/client'
import type {
  SummaryActiveSessionResponse,
  SummaryCatalogItem,
  SummaryChangeRequest,
  SummaryDirectSyncResponse,
  SummaryFilteredPreviewResponse,
  SummaryInspectResponse,
  SummaryNumberMode,
  SummaryPreviewRowResponse,
  SummaryPrintMode,
  SummaryPrintPreviewResponse,
  SummaryPrintYearStatusResponse,
  SummaryRowResponse,
  SummaryRowsResponse,
  SummaryScopeRequest,
  SummarySessionInfoResponse,
  SyncResponse,
} from '../api/types'
import { errorMessage } from '../lib/errors'
import { formatBytes, formatNumber } from '../lib/format'
import {
  applyLearned,
  normalizeSpeech,
  parseVoiceTranscript,
} from '../lib/voiceParser'
import { useVoiceInput } from '../composables/useVoiceInput'
import EmptyState from '../components/EmptyState.vue'
import AppSelect from '../components/AppSelect.vue'
import FilePicker from '../components/FilePicker.vue'
import SummaryItemPicker from '../components/SummaryItemPicker.vue'
import { useToast } from '../composables/useToast'
import {
  clearTeamEditingState,
  setTeamEditingState,
  teamOnline,
  teamRowMarks,
  joinSummarySession,
  leaveSummarySession,
  sendSummaryCellFocus,
  sendSummaryCellTyping,
  sendSummaryCellCommit,
  lastSummaryCellFocus,
  lastSummaryCellTyping,
  lastSummaryCellChanged,
  lastSummaryBulkChanged,
  lastTeamSessionChanged,
} from '../composables/useTeamPresence'
import { getUserName } from '../api/client'
import { teamIdentity } from '../composables/useTeamIdentity'
import { colorForName, renderAvatar } from '../lib/team'

interface RowEdit {
  row: SummaryRowResponse
  items: string[]
  /** null = ghép từ items; chuỗi = người dùng sửa toàn bộ câu. */
  summaryText: string | null
  /** null = giữ nguyên; chuỗi rỗng = xoá số tờ. */
  sheetNumber: string | null
  /** null = giữ nguyên; chuỗi rỗng = xoá hồ sơ số (cột C). */
  documentNumber: string | null
  /** null = giữ nguyên; chuỗi rỗng = xoá số hồ sơ (cột B). */
  numberLabel: string | null
}

interface DraftEntry {
  row: SummaryRowResponse
  items: string[]
  summaryText: string | null
  sheetNumber: string | null
  documentNumber: string | null
  numberLabel: string | null
}

interface DraftPayload {
  fileName: string
  savedAt: string
  edits: DraftEntry[]
}

const DRAFT_PREFIX = 'stp.summary.draft.'
const PAGE_SIZES = [20, 50, 100, 200]

const toast = useToast()

const files = ref<File[]>([])
const session = ref<SummaryInspectResponse | null>(null)
const inspecting = ref(false)
const inspectError = ref<string | null>(null)

const rows = ref<SummaryRowResponse[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const loadingRows = ref(false)
const rowsError = ref<string | null>(null)

/** Phiên ghép nhiều sheet (file nhập + sheet năm) → hiện badge sheet từng dòng. */
const multiSheet = computed(() => (session.value?.sheets?.length ?? 0) > 1)

function sheetBadgeFor(row: SummaryRowResponse): string {
  return row.sheet || session.value?.sheetName || ''
}

function sourceRowLabelFor(row: SummaryRowResponse): string {
  return row.sourceRowNumber && row.sourceRowNumber !== row.rowNumber
    ? `dòng ${row.sourceRowNumber} của file Excel gốc`
    : `dòng ${row.rowNumber}`
}

const year = ref<number | ''>('')
const yearFrom = ref<number | ''>('')
const yearTo = ref<number | ''>('')
const type = ref('')
const search = ref('')
const onlyExisting = ref(false)
const numberMode = ref<SummaryNumberMode>('all')
const numberFrom = ref<number | ''>('')
const numberTo = ref<number | ''>('')
const singleNumber = ref<number | ''>('')
const onlyMissingSheetNumber = ref(false)
const onlyMissingDocumentNumber = ref(false)
const onlyMissingNumber = ref(false)
const onlyNeedsNormalization = ref(false)
const missingSheetNumberFirst = ref(false)

const edits = ref(new Map<number, RowEdit>())
const changedCount = computed(() => edits.value.size)
const selected = ref(new Set<number>())
const bulkItems = ref<string[]>([])
const bulkApplying = ref(false)

const editingSummaryRow = ref<number | null>(null)
const summaryDraft = ref('')

// ===== Presence nhóm: dòng nào đang có ai sửa =====
// Map dòng → tên người đang giữ (bỏ qua dấu của chính mình). F2: tối đa 8 dấu
// cùng lúc — lấy 8 người gần đây nhất, phần còn lại tự rơi khi hết TTL.
const teamMarkRows = computed(() => {
  const sessionId = session.value?.sessionId
  const map = new Map<number, string>()
  if (!sessionId) return map
  const self = teamIdentity.value?.displayName ?? getUserName()
  const marks = teamRowMarks.value
    .filter((mark) => mark.sessionId === sessionId && mark.actor && mark.actor !== self)
    .sort((a, b) => a.lastSeenSecondsAgo - b.lastSeenSecondsAgo)
    .slice(0, 8)
  for (const mark of marks) {
    map.set(mark.rowNumber, mark.actor)
  }
  return map
})

// Người khác đang giữ đúng dòng mình đang mở → banner cảnh báo trong bảng sửa.
const editingConflict = computed(() =>
  editingSummaryRow.value != null ? (teamMarkRows.value.get(editingSummaryRow.value) ?? null) : null,
)

// Báo cho vòng presence biết mình đang mở dòng nào của phiên nào.
watch(
  [session, editingSummaryRow],
  ([currentSession, currentRow]) => {
    setTeamEditingState(currentSession?.sessionId ?? null, currentRow)
  },
)

// ===== Multiplayer Google Sheets & Active Session toàn phòng =====
const activeTeamSession = ref<SummaryActiveSessionResponse | null>(null)
const settingActiveSession = ref(false)
const directSyncModalOpen = ref(false)
const directSyncing = ref(false)
const directSyncResult = ref<SummaryDirectSyncResponse | null>(null)

// Con trỏ ô realtime: key = `${rowNumber}:${col}` -> { actor, at }
const remoteFocusedCells = ref<Map<string, { actor: string; at: number }>>(new Map())
// Gõ chữ realtime: key = `${rowNumber}:${col}` -> { actor, text, at }
const remoteTypingCells = ref<Map<string, { actor: string; text: string; at: number }>>(new Map())
// Hiệu ứng chớp sáng dòng vừa được sửa (Google Sheets pulse): Set<rowNumber>
const flashingRows = ref<Set<number>>(new Set())

const myActorName = computed(() => teamIdentity.value?.displayName ?? getUserName())
const isAdmin = computed(() => {
  const name = (getUserName() || '').toLowerCase()
  return name === 'admin' || name === 'quan tri' || name === 'quản trị'
})

const roomCollaborators = computed(() => {
  const me = myActorName.value
  return teamOnline.value.filter((user) => user.actor !== me && (user.where || '').includes('trích yếu'))
})

function triggerRowFlash(rowNumber: number): void {
  flashingRows.value.add(rowNumber)
  window.setTimeout(() => {
    flashingRows.value.delete(rowNumber)
  }, 2500)
}

const previewOpen = ref(false)
const previewMode = ref<'full' | 'filtered'>('full')
const previewLoading = ref(false)
const previewRows = ref<SummaryPreviewRowResponse[]>([])
const previewError = ref<string | null>(null)
const filteredPreview = ref<SummaryFilteredPreviewResponse | null>(null)
const exporting = ref(false)
const filteredExporting = ref(false)
const exportingIssues = ref(false)

// Chỉ xuất các dòng đã tích (tránh xuất thừa)
const onlySelectedRows = ref(false)
const exportScope = ref<'all' | 'edited' | 'selected' | 'filtered'>('all')

const selectedRowNumbers = computed(() => [...selected.value].sort((a, b) => a - b))
const editedRowNumbers = computed(() => [...edits.value.keys()].sort((a, b) => a - b))

function effectiveOnlyRowNumbers(): number[] | undefined {
  if (exportScope.value === 'selected' && selectedRowNumbers.value.length > 0) {
    return selectedRowNumbers.value
  }
  if (exportScope.value === 'edited' && editedRowNumbers.value.length > 0) {
    return editedRowNumbers.value
  }
  return undefined
}

function onExportScopeChange(): void {
  if (exportScope.value === 'filtered') {
    void openFilteredPreview()
  } else {
    void openPreview()
  }
}

// A3: đổi tên file xuất
const exportNameOpen = ref(false)
const exportName = ref('')
const pendingExportMode = ref<'full' | 'filtered'>('full')

// D3: báo cáo thiếu theo năm
const yearReport = ref<Array<{ year: number; total: number; missingSheetNumber: number; missingDocumentNumber: number; missingNumber: number; needsNormalization: number }>>([])
const reportLoading = ref(false)

// E: xuất bản in theo năm (sheet BANG_IN_HANG_LOAT + macro lọc/in)
const printOpen = ref(false)
const printLoading = ref(false)
const printExporting = ref(false)
const printError = ref<string | null>(null)
const printPreview = ref<SummaryPrintPreviewResponse | null>(null)
const printYears = ref<number[]>([])
const printMode = ref<SummaryPrintMode>('perYear')
const printAllowIncomplete = ref(false)
const printFileName = ref('')

const printSelectedStatuses = computed<SummaryPrintYearStatusResponse[]>(() => {
  const statuses = printPreview.value?.years ?? []
  const chosen = new Set(printYears.value)
  return statuses.filter((item) => chosen.has(item.year))
})
const printSelectedRows = computed(() =>
  printSelectedStatuses.value.reduce((total, item) => total + item.total, 0),
)
const printSelectedMissing = computed(() =>
  printSelectedStatuses.value.reduce((total, item) => total + item.missingNumber + item.missingDocumentCode + item.missingDate + item.missingSheetNumber + item.missingSummary, 0),
)
const printCanExport = computed(() =>
  printYears.value.length > 0
  && printSelectedRows.value > 0
  && (printAllowIncomplete.value || printSelectedMissing.value === 0),
)

// Khôi phục phiên sau F5 / mất kết nối + lưu vào Kho + lịch sử quá trình
const ACTIVE_SESSION_KEY = 'stp.summary.activeSession'
const HISTORY_PREFIX = 'stp.summary.history.'
const resumeCandidates = ref<SummarySessionInfoResponse[]>([])
const resuming = ref(false)
const archiving = ref(false)
const saveToArchiveOpen = ref(false)
const lastExportedBlob = ref<Blob | null>(null)
const lastExportedName = ref('')

interface HistoryEntry {
  at: string
  action: string
  detail: string
}

const historyEntries = ref<HistoryEntry[]>([])

function rememberSession(sessionId: string, fileName: string): void {
  // Phiên mới/đổi phiên — trạng thái đồng bộ server của phiên cũ vô nghĩa.
  lastSyncedEditCount = -1
  try {
    localStorage.setItem(ACTIVE_SESSION_KEY, JSON.stringify({ sessionId, fileName }))
  } catch {
    // Bỏ qua.
  }
}

function forgetSession(): void {
  try {
    localStorage.removeItem(ACTIVE_SESSION_KEY)
  } catch {
    // Bỏ qua.
  }
}

function historyKey(fileName: string): string {
  return `${HISTORY_PREFIX}${fileName}`
}

function loadHistory(fileName: string): void {
  try {
    const raw = localStorage.getItem(historyKey(fileName))
    historyEntries.value = raw ? (JSON.parse(raw) as HistoryEntry[]) : []
  } catch {
    historyEntries.value = []
  }
}

function pushHistoryEntry(action: string, detail: string): void {
  const active = session.value
  if (!active) return

  const entry: HistoryEntry = { at: new Date().toISOString(), action, detail }
  historyEntries.value = [entry, ...historyEntries.value].slice(0, 50)
  try {
    localStorage.setItem(historyKey(active.fileName), JSON.stringify(historyEntries.value))
  } catch {
    // Bỏ qua.
  }
}

function loadResumeCandidates(): void {
  request<SummarySessionInfoResponse[]>('/api/summary/sessions')
    .then((list) => {
      resumeCandidates.value = list
    })
    .catch(() => {
      resumeCandidates.value = []
    })
}

async function fetchSharedEdits(sessionId: string): Promise<void> {
  try {
    const list = await request<SummaryChangeRequest[]>(`/api/summary/${sessionId}/edits`)
    if (!list || list.length === 0) return
    const next = new Map(edits.value)
    for (const item of list) {
      const existing = next.get(item.rowNumber)
      const rowRef = existing?.row ?? rows.value.find((r) => r.rowNumber === item.rowNumber)
      if (rowRef) {
        next.set(item.rowNumber, {
          row: rowRef,
          items: item.items ?? [],
          summaryText: item.summaryText ?? null,
          sheetNumber: item.sheetNumber ?? null,
          documentNumber: item.documentNumber ?? null,
          numberLabel: item.numberLabel ?? null,
        })
      }
    }
    edits.value = next
  } catch {
    // Bỏ qua nếu lỗi mạng
  }
}

async function restoreFromServer(sessionId: string): Promise<boolean> {
  try {
    const data = await request<SummaryInspectResponse>(`/api/summary/${sessionId}`)
    session.value = data
    rememberSession(data.sessionId, data.fileName)
    resetHistory()
    loadHistory(data.fileName)
    page.value = 1
    await fetchRows()
    await fetchYearReport()
    // Nạp toàn bộ sửa đổi của cả phòng đã lưu trên server
    await fetchSharedEdits(data.sessionId)
    restoreDraft(data.fileName)
    resetHistory()
    // Tham gia nhóm làm việc chung qua SignalR
    joinSummarySession(data.sessionId, myActorName.value ?? 'Quản trị')
    return true
  } catch {
    return false
  }
}

async function resumeLastSession(): Promise<void> {
  resuming.value = true
  try {
    // 1. Kiểm tra trước tiên: Có file chung của cả phòng không?
    try {
      const active = await request<SummaryActiveSessionResponse>('/api/summary/active')
      if (active && active.hasActiveSession && active.sessionId) {
        activeTeamSession.value = active
        const ok = await restoreFromServer(active.sessionId)
        if (ok) {
          toast.success(
            'Đã vào file làm việc chung',
            `File "${active.fileName}" do ${active.uploadedBy} tải lên cho cả phòng cùng làm.`,
          )
          return
        }
      }
    } catch {
      // Bỏ qua lỗi mạng
    }

    // 2. Không có file chung phòng -> Khôi phục phiên cá nhân lưu trong localStorage
    let saved: { sessionId: string; fileName: string } | null = null
    try {
      const raw = localStorage.getItem(ACTIVE_SESSION_KEY)
      if (raw) saved = JSON.parse(raw) as { sessionId: string; fileName: string }
    } catch {
      saved = null
    }

    if (!saved) {
      loadResumeCandidates()
      return
    }

    const ok = await restoreFromServer(saved.sessionId)
    if (ok) {
      toast.success('Đã khôi phục phiên đang dở', `File ${saved.fileName} được mở lại — không cần import.`)
    } else {
      forgetSession()
      loadResumeCandidates()
    }
  } finally {
    resuming.value = false
  }
}

async function openResumeCandidate(candidate: SummarySessionInfoResponse): Promise<void> {
  resuming.value = true
  try {
    const ok = await restoreFromServer(candidate.sessionId)
    if (ok) {
      toast.success('Đã mở lại phiên', candidate.fileName)
    } else {
      toast.error('Không mở lại được phiên', 'Phiên đã hết hạn hoặc file đã bị xoá.')
    }
  } finally {
    resuming.value = false
  }
}

async function archiveLastExport(): Promise<void> {
  if (!lastExportedBlob.value || !lastExportedName.value) return

  archiving.value = true
  try {
    const form = new FormData()
    form.append('file', new File([lastExportedBlob.value], lastExportedName.value))
    const result = await request<SyncResponse>('/api/sync', { method: 'POST', form })

    if (result.success) {
      pushHistoryEntry('Lưu vào Kho', `${lastExportedName.value} · +${result.addedCount} / sửa ${result.updatedCount} / xoá ${result.deletedCount}`)
      saveToArchiveOpen.value = false
      toast.success(
        'Đã lưu vào Kho hồ sơ',
        `${lastExportedName.value} · thêm ${formatNumber(result.addedCount)} · sửa ${formatNumber(result.updatedCount)} · xoá ${formatNumber(result.deletedCount)}. Lần sau mở Kho là thấy, không cần import.`,
      )
    } else {
      toast.error('Chưa lưu được vào Kho', result.errorMessage ?? 'Không rõ lỗi.')
    }
  } catch (caught) {
    // /api/sync trả SyncResponse dạng 422 có errorMessage — trích ra để hiện
    // lý do thật thay vì dòng chung chung "Unprocessable Entity".
    const payload =
      caught && typeof caught === 'object'
        ? (caught as { payload?: unknown }).payload
        : null
    const detail =
      payload && typeof payload === 'object' && !Array.isArray(payload)
        ? (payload as { errorMessage?: unknown }).errorMessage
        : null
    toast.error(
      'Chưa lưu được vào Kho',
      typeof detail === 'string' && detail.length > 0 ? detail : errorMessage(caught),
    )
  } finally {
    archiving.value = false
  }
}

// A5: hoàn tác / làm lại
const editHistory = ref<Map<number, RowEdit>[]>([])
const historyIndex = ref(-1)
const canUndo = computed(() => historyIndex.value > 0)
const canRedo = computed(() => historyIndex.value < editHistory.value.length - 1)

function cloneEdits(source: Map<number, RowEdit>): Map<number, RowEdit> {
  const next = new Map<number, RowEdit>()
  for (const [key, value] of source) {
    next.set(key, { ...value, items: [...value.items] })
  }
  return next
}

function pushHistory(): void {
  editHistory.value = editHistory.value.slice(0, historyIndex.value + 1)
  editHistory.value.push(cloneEdits(edits.value))
  if (editHistory.value.length > 50) editHistory.value.shift()
  historyIndex.value = editHistory.value.length - 1
}

function undoEdits(): void {
  if (!canUndo.value) return
  historyIndex.value -= 1
  edits.value = cloneEdits(editHistory.value[historyIndex.value])
  toast.info('Đã hoàn tác', `Còn ${formatNumber(edits.value.size)} dòng thay đổi.`)
}

function redoEdits(): void {
  if (!canRedo.value) return
  historyIndex.value += 1
  edits.value = cloneEdits(editHistory.value[historyIndex.value])
  toast.info('Đã làm lại', `${formatNumber(edits.value.size)} dòng thay đổi.`)
}

function resetHistory(): void {
  editHistory.value = [cloneEdits(edits.value)]
  historyIndex.value = 0
}

const draftSavedAt = ref<Date | null>(null)
const draftStatus = ref<'idle' | 'saved'>('idle')

const searchInput = ref<HTMLInputElement | null>(null)

const scopeReady = computed(() => {
  if (numberMode.value === 'range') {
    return numberFrom.value !== '' || numberTo.value !== ''
  }

  if (numberMode.value === 'single') {
    return singleNumber.value !== ''
  }

  return true
})

const scopeRequest = computed<SummaryScopeRequest>(() => ({
  year: year.value === '' ? null : Number(year.value),
  type: type.value || null,
  search: search.value.trim() || null,
  onlyExisting: onlyExisting.value,
  numberMode: numberMode.value,
  numberFrom:
    numberMode.value === 'range' && numberFrom.value !== '' ? Number(numberFrom.value) : null,
  numberTo: numberMode.value === 'range' && numberTo.value !== '' ? Number(numberTo.value) : null,
  number:
    numberMode.value === 'single' && singleNumber.value !== ''
      ? Number(singleNumber.value)
      : null,
  yearFrom: yearFrom.value === '' ? null : Number(yearFrom.value),
  yearTo: yearTo.value === '' ? null : Number(yearTo.value),
  onlyMissingSheetNumber: onlyMissingSheetNumber.value,
  onlyMissingDocumentNumber: onlyMissingDocumentNumber.value,
  onlyMissingNumber: onlyMissingNumber.value,
  onlyNeedsNormalization: onlyNeedsNormalization.value,
  missingSheetNumberFirst: missingSheetNumberFirst.value,
}))

const scopeLabel = computed(() => {
  const parts: string[] = []

  if (year.value !== '') {
    parts.push(`năm ${year.value}`)
  }

  if (yearFrom.value !== '' || yearTo.value !== '') {
    parts.push(
      `năm ${yearFrom.value === '' ? '…' : yearFrom.value}–${yearTo.value === '' ? '…' : yearTo.value}`,
    )
  }

  if (numberMode.value === 'range') {
    if (numberFrom.value !== '' && numberTo.value !== '') {
      parts.push(`hồ sơ ${numberFrom.value}–${numberTo.value}`)
    } else if (numberFrom.value !== '') {
      parts.push(`hồ sơ từ ${numberFrom.value}`)
    } else if (numberTo.value !== '') {
      parts.push(`hồ sơ đến ${numberTo.value}`)
    }
  } else if (numberMode.value === 'single' && singleNumber.value !== '') {
    parts.push(`hồ sơ số ${singleNumber.value}`)
  }

  if (type.value) {
    parts.push(`loại ${type.value}`)
  }

  if (search.value.trim()) {
    parts.push(`từ khoá “${search.value.trim()}”`)
  }

  if (onlyExisting.value) {
    parts.push('chỉ dòng đã có danh mục')
  }

  if (onlyMissingSheetNumber.value) {
    parts.push('thiếu số tờ')
  }

  if (onlyMissingDocumentNumber.value) {
    parts.push('thiếu hồ sơ số')
  }

  if (onlyMissingNumber.value) {
    parts.push('thiếu số hồ sơ')
  }

  if (onlyNeedsNormalization.value) {
    parts.push('sai mở đầu câu')
  }

  return parts.length > 0 ? parts.join(' · ') : 'toàn bộ workbook'
})

const allChanges = computed<SummaryChangeRequest[]>(() =>
  [...edits.value.values()]
    .sort((left, right) => left.row.rowNumber - right.row.rowNumber)
    .map((edit) => {
      const summary = edit.summaryText ?? composeSummary(edit.row, edit.items)
      return {
        rowNumber: edit.row.rowNumber,
        items: edit.items,
        summaryText: summary,
        sheetNumber: edit.sheetNumber ?? edit.row.sheetNumber,
        documentNumber: edit.documentNumber ?? edit.row.documentNumber,
        numberLabel: edit.numberLabel ?? edit.row.numberLabel,
      }
    }),
)

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))
const rangeStart = computed(() => (total.value === 0 ? 0 : (page.value - 1) * pageSize.value + 1))
const rangeEnd = computed(() => Math.min(total.value, page.value * pageSize.value))

const allPageSelected = computed(
  () => rows.value.length > 0 && rows.value.every((row) => selected.value.has(row.rowNumber)),
)

const selectedNormalizableCount = computed(
  () =>
    rows.value.filter((row) => selected.value.has(row.rowNumber) && row.needsNormalization).length,
)

const yearOptions = computed(() => [
  { value: '', label: 'Tất cả các năm' },
  ...(session.value?.years ?? []).map((item) => ({
    value: String(item.year),
    label: String(item.year),
    countLabel: formatNumber(item.count),
  })),
])

const typeOptions = computed(() => [
  { value: '', label: 'Tất cả loại' },
  ...(session.value?.types ?? []).map((item) => ({
    value: item.name,
    label: item.name,
    countLabel: formatNumber(item.count),
  })),
])

const numberModeOptions = [
  { value: 'all', label: 'Tất cả số' },
  { value: 'range', label: 'Từ số – đến số' },
  { value: 'single', label: 'Số chỉ định' },
]

const pageSizeOptions = PAGE_SIZES.map((size) => ({
  value: String(size),
  label: `${size} dòng`,
}))

const hasAliasParenthesis = (row: SummaryRowResponse): boolean =>
  !row.hasDocumentList && row.summary.trimEnd().endsWith(')')

let searchTimer: number | undefined
let filterTimer: number | undefined
let autosaveTimer: number | undefined

function normalizeText(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase()
}

/** Sắp xếp mục theo đúng thứ tự danh mục như backend để bản xem trước khớp bản xuất. */
function orderItems(items: string[]): string[] {
  const order = new Map<string, number>()
  session.value?.catalog.forEach((item, index) => {
    const key = normalizeText(item)
    if (!order.has(key)) order.set(key, index)
  })

  const seen = new Set<string>()
  return items
    .map((value, index) => ({ value: value.trim().replace(/\s+/g, ' '), index }))
    .filter((item) => item.value.length > 0)
    .filter((item) => {
      const key = normalizeText(item.value)
      if (seen.has(key)) return false
      seen.add(key)
      return true
    })
    .sort((left, right) => {
      const leftOrder = order.get(normalizeText(left.value)) ?? Number.MAX_SAFE_INTEGER
      const rightOrder = order.get(normalizeText(right.value)) ?? Number.MAX_SAFE_INTEGER
      return leftOrder - rightOrder || left.index - right.index
    })
    .map((item) => item.value)
}

/**
 * Bộ 7 giấy tờ xuất hiện cùng nhau ở 98% hồ sơ "Đăng ký kết hôn" có danh sách
 * (theo thống kê thật 2026-11: 20.798/21.132 hồ sơ). Dùng cho áp nhanh hàng loạt.
 */
const STANDARD_SEVEN = [
  'giấy khai sinh',
  'sơ yếu lý lịch',
  'bản công hàm',
  'phiếu đề xuất',
  'hộ khẩu',
  'giấy khai đăng ký kết hôn',
  'chứng chỉ chuyên khoa',
]

/** Tần suất từng mục theo meta phiên → badge ×N trong picker "hay gặp nhất". */
const catalogCounts = computed<Record<string, number>>(() => {
  const map: Record<string, number> = {}
  for (const meta of session.value?.catalogMeta ?? []) {
    map[normalizeText(meta.name)] = meta.usageCount
  }
  return map
})

/** Chọn sẵn bộ 7 chuẩn cho bulk picker (bấm "Áp dụng" để áp). */
function bulkApplyStandardSeven(): void {
  if (!session.value) return
  bulkItems.value = [...STANDARD_SEVEN]
  toast.info('Đã chọn bộ 7 chuẩn', 'Bấm "Áp dụng cho dòng đã chọn" để xác nhận.')
}

/**
 * Lưu mục người dùng tự gõ vào "danh mục học được" — tự lưu ngay,
 * không cần bước riêng; thất bại chỉ hiện cảnh báo, chip vẫn dùng được.
 */
async function persistCatalogCustom(display: string): Promise<void> {
  const active = session.value
  if (!display.trim()) return

  const key = normalizeText(display)
  const known = (active?.catalogMeta ?? []).some((meta) => normalizeText(meta.name) === key)
  if (known) return

  try {
    const created = await request<SummaryCatalogItem>('/api/summary/catalog/items', {
      method: 'POST',
      body: { displayName: display, dossierTypeHint: type.value || null },
    })

    if (session.value) {
      const next = [...(session.value.catalogMeta ?? [])]
      next.unshift({
        name: created.displayName,
        usageCount: created.usageCount,
        source: 'user',
      })
      if (!session.value.catalog.some((name) => normalizeText(name) === normalizeText(created.displayName))) {
        session.value = { ...session.value, catalog: [...session.value.catalog, created.displayName] }
      }
      session.value = { ...session.value, catalogMeta: next }
    }
  } catch (caught) {
    toast.warning(
      'Mục chưa lưu được vào danh mục',
      `${display}: ${errorMessage(caught)}. Dùng tạm cho dòng này.`,
    )
  }
}

/** Phần tiền tố hiển thị: dùng bản đã chuẩn hoá nếu backend phát hiện cần sửa. */
function baseSummaryFor(row: SummaryRowResponse): string {
  return row.needsNormalization ? row.normalizedBaseSummary : row.baseSummary
}

/** Ghép câu trích yếu giống hệt SummaryToolService.BuildSummary. */
function composeSummary(row: SummaryRowResponse, items: string[]): string {
  const ordered = orderItems(items)
  if (ordered.length === 0) {
    if (row.hasDocumentList) return baseSummaryFor(row)
    return row.needsNormalization ? row.normalizedSummary : row.summary
  }

  const baseText = baseSummaryFor(row).trimEnd()
  const list = ordered.join(', ')

  if (baseText.length === 0) {
    return `(${list})`
  }

  let separator = ' '
  if (row.hasDocumentList) {
    const original = row.summary.trimEnd()
    const openIndex = original.lastIndexOf('(')
    if (openIndex > 0 && !/\s/.test(original[openIndex - 1] ?? '')) {
      separator = ''
    }
  }

  return `${baseText}${separator}(${list})`
}

function editFor(row: SummaryRowResponse): RowEdit {
  return (
    edits.value.get(row.rowNumber) ?? {
      row,
      items: row.items,
      summaryText: null,
      sheetNumber: null,
      documentNumber: null,
      numberLabel: null,
    }
  )
}

function itemsFor(row: SummaryRowResponse): string[] {
  return editFor(row).items
}

function summaryFor(row: SummaryRowResponse): string {
  const edit = editFor(row)
  return edit.summaryText ?? composeSummary(row, edit.items)
}

function sheetFor(row: SummaryRowResponse): string {
  return editFor(row).sheetNumber ?? row.sheetNumber
}

function docFor(row: SummaryRowResponse): string {
  return editFor(row).documentNumber ?? row.documentNumber
}

function numFor(row: SummaryRowResponse): string {
  return editFor(row).numberLabel ?? row.numberLabel
}

function commit(row: SummaryRowResponse, patch: Partial<Omit<RowEdit, 'row'>>): void {
  const current = editFor(row)
  const nextEdit: RowEdit = { ...current, ...patch }
  const summary = nextEdit.summaryText ?? composeSummary(row, nextEdit.items)
  const sheet = nextEdit.sheetNumber ?? row.sheetNumber
  const doc = nextEdit.documentNumber ?? row.documentNumber
  const num = nextEdit.numberLabel ?? row.numberLabel
  // Dòng đăng ký kết hôn được backend chuẩn hoá tự động khi xuất, nên so sánh
  // với bản đã chuẩn hoá để không tạo thay đổi thừa.
  const baseline = row.needsNormalization ? row.normalizedSummary : row.summary
  const changed =
    summary !== baseline ||
    sheet !== row.sheetNumber ||
    doc !== row.documentNumber ||
    num !== row.numberLabel

  const next = new Map(edits.value)
  if (changed) {
    next.set(row.rowNumber, nextEdit)
  } else {
    next.delete(row.rowNumber)
  }

  edits.value = next
  pushHistory()

  // Multiplayer: Gửi cập nhật tức thời lên server để lưu vào shared edits và broadcast cho toàn phòng
  if (session.value) {
    const changeReq: SummaryChangeRequest = {
      rowNumber: row.rowNumber,
      items: nextEdit.items,
      summaryText: nextEdit.summaryText,
      sheetNumber: nextEdit.sheetNumber,
      documentNumber: nextEdit.documentNumber,
      numberLabel: nextEdit.numberLabel,
    }
    void request(`/api/summary/${session.value.sessionId}/change`, {
      method: 'POST',
      body: changeReq,
    }).catch(() => {})
    sendSummaryCellCommit(session.value.sessionId, changeReq, myActorName.value ?? 'Quản trị')
  }
}

function onCellFocus(row: SummaryRowResponse, col: string): void {
  if (!session.value) return
  sendSummaryCellFocus(session.value.sessionId, row.rowNumber, col, myActorName.value ?? 'Quản trị')
}

function onCellBlur(row: SummaryRowResponse, _col?: string): void {
  if (!session.value) return
  sendSummaryCellFocus(session.value.sessionId, row.rowNumber, '', myActorName.value ?? 'Quản trị')
}

function onCellInput(row: SummaryRowResponse, col: string, event: Event): void {
  if (!session.value) return
  const val = (event.target as HTMLInputElement).value
  sendSummaryCellTyping(session.value.sessionId, row.rowNumber, col, val, myActorName.value ?? 'Quản trị')
}

async function toggleActiveTeamSession(): Promise<void> {
  const active = session.value
  if (!active) return

  settingActiveSession.value = true
  try {
    if (activeTeamSession.value?.sessionId === active.sessionId) {
      await request('/api/summary/active', { method: 'DELETE' })
      activeTeamSession.value = null
      toast.info('Đã dừng chia sẻ file chung', 'File này không còn tự động nạp cho cả phòng.')
    } else {
      const res = await request<SummaryActiveSessionResponse>('/api/summary/active', {
        method: 'POST',
        body: { sessionId: active.sessionId },
      })
      activeTeamSession.value = res
      toast.success(
        'Đã đặt làm file làm việc chung',
        'Tất cả thành viên trong phòng khi mở app sẽ tự động vào chung file này.',
      )
    }
  } catch (caught) {
    toast.error('Thao tác thất bại', errorMessage(caught))
  } finally {
    settingActiveSession.value = false
  }
}

async function doDirectSyncToArchive(): Promise<void> {
  const active = session.value
  if (!active) return

  directSyncing.value = true
  try {
    const res = await request<SummaryDirectSyncResponse>(
      `/api/summary/${active.sessionId}/sync-to-archive`,
      {
        method: 'POST',
        body: { confirmSync: true },
      },
    )

    directSyncResult.value = res
    if (res.success) {
      toast.success(
        'Đã nạp thẳng vào Kho hồ sơ!',
        `Thêm mới ${formatNumber(res.addedCount)} · Sửa ${formatNumber(res.updatedCount)} · Xung đột ${formatNumber(res.conflictCount)}. Toàn bộ dữ liệu đã được ghi vào Kho CSDL an toàn.`,
      )
      pushHistoryEntry(
        'Nạp thẳng vào Kho',
        `${active.fileName} · +${res.addedCount} / sửa ${res.updatedCount} / xung đột ${res.conflictCount}`,
      )
    } else {
      toast.error('Chưa nạp được vào Kho', res.errorMessage ?? 'Không rõ lỗi')
    }
  } catch (caught) {
    toast.error('Lỗi khi nạp vào Kho', errorMessage(caught))
  } finally {
    directSyncing.value = false
  }
}

function setRowItems(row: SummaryRowResponse, items: string[]): void {
  commit(row, { items, summaryText: null })
}

function onSheetChange(row: SummaryRowResponse, event: Event): void {
  const value = (event.target as HTMLInputElement).value.trim()
  commit(row, { sheetNumber: value === row.sheetNumber ? null : value })
}

function onDocChange(row: SummaryRowResponse, event: Event): void {
  const value = (event.target as HTMLInputElement).value.trim()
  commit(row, { documentNumber: value === row.documentNumber ? null : value })
}

function onNumChange(row: SummaryRowResponse, event: Event): void {
  const value = (event.target as HTMLInputElement).value.trim()
  commit(row, { numberLabel: value === row.numberLabel ? null : value })
}

function openSummaryEditor(row: SummaryRowResponse): void {
  editingSummaryRow.value = row.rowNumber
  summaryDraft.value = summaryFor(row)
}

function closeSummaryEditor(): void {
  editingSummaryRow.value = null
  summaryDraft.value = ''
  voiceLearnCandidate.value = null
}

function saveSummaryEditor(row: SummaryRowResponse): void {
  const text = summaryDraft.value.trim()
  const candidate = voiceLearnCandidate.value
  commit(row, { summaryText: text })
  closeSummaryEditor()
  // I2 học lỗi nghe: người dùng nắn bản voice khác bản điền → nhớ cặp
  // {nghe sai → người sửa}; lần sau parser tự thay trước khi match.
  if (candidate && candidate.rowNumber === row.rowNumber && !voiceSuppressLearn) {
    const prefill = candidate.prefill.trim()
    if (prefill.length > 0 && text !== prefill) {
      rememberVoiceCorrection(candidate.heard, text)
    }
  }
  voiceLearnCandidate.value = null
  toast.success('Đã cập nhật trích yếu', `Dòng ${row.rowNumber} sẽ được ghi khi xuất.`)
  jumpToNextMissing(row.rowNumber)
}

/* ==================== GĐ 0 — trích yếu siêu tốc (11/11/2026) ==================== */

/** C4: tự nhảy sang dòng thiếu danh mục kế tiếp sau khi lưu (mặc định bật). */
const autoNextRow = ref((localStorage.getItem('summary.autoNext') ?? '1') === '1')

function toggleAutoNext(): void {
  autoNextRow.value = !autoNextRow.value
  localStorage.setItem('summary.autoNext', autoNextRow.value ? '1' : '0')
  toast.info(
    autoNextRow.value ? 'Đã bật tự nhảy' : 'Đã tắt tự nhảy',
    autoNextRow.value
      ? 'Sau khi lưu, editor tự mở dòng thiếu danh mục kế tiếp.'
      : 'Sau khi lưu, editor đóng lại.',
  )
}

/** Dòng chưa có danh mục kế tiếp trong danh sách đang xem (vòng về đầu). */
function nextMissingRow(afterRowNumber: number): SummaryRowResponse | null {
  const ordered = [...rows.value].sort((a, b) => a.rowNumber - b.rowNumber)
  const index = ordered.findIndex((row) => row.rowNumber === afterRowNumber)
  if (index < 0) return null
  for (let step = 1; step <= ordered.length; step++) {
    const candidate = ordered[(index + step) % ordered.length]
    if (candidate.rowNumber === afterRowNumber) return null
    if (itemsFor(candidate).length === 0) return candidate
  }
  return null
}

async function scrollEditorIntoView(): Promise<void> {
  await nextTick()
  document
    .querySelector('.summary-editor-text')
    ?.scrollIntoView({ block: 'center', behavior: 'smooth' })
}

function jumpToNextMissing(fromRowNumber: number): void {
  if (!autoNextRow.value) return
  const next = nextMissingRow(fromRowNumber)
  if (!next) {
    toast.success('Hết dòng thiếu', 'Không còn dòng thiếu danh mục trong phạm vi đang xem.')
    return
  }
  openSummaryEditor(next)
  void scrollEditorIntoView()
}

/** C1: chip 1-chạm = bộ 7 chuẩn + mục hay gặp nhất của phiên. */
const quickChipItems = computed<string[]>(() => {
  const base = [...STANDARD_SEVEN]
  const metas = (session.value?.catalogMeta ?? [])
    .filter(
      (meta) =>
        meta.usageCount > 0 && !base.some((b) => normalizeText(b) === normalizeText(meta.name)),
    )
    .slice(0, 4)
    .map((meta) => meta.name)
  return [...base, ...metas]
})

function hasQuickItem(row: SummaryRowResponse, item: string): boolean {
  return itemsFor(row).some((x) => normalizeText(x) === normalizeText(item))
}

function toggleQuickItem(row: SummaryRowResponse, item: string): void {
  const current = itemsFor(row)
  const next = hasQuickItem(row, item)
    ? current.filter((x) => normalizeText(x) !== normalizeText(item))
    : [...current, item]
  commit(row, { items: next, summaryText: null })
  if (editingSummaryRow.value === row.rowNumber) {
    jumpToNextMissing(row.rowNumber)
  }
}

/** C3: bộ items phổ biến nhất của cùng loại hồ sơ (học từ trang đang xem). */
function defaultSetForType(row: SummaryRowResponse): string[] | null {
  if (!row.type) return null
  const key = normalizeText(row.type)
  const counts = new Map<string, { items: string[]; count: number }>()
  for (const other of rows.value) {
    if (other.rowNumber === row.rowNumber || !other.type) continue
    if (normalizeText(other.type) !== key) continue
    const items = itemsFor(other)
    if (items.length === 0) continue
    const signature = items.map((item) => normalizeText(item)).sort().join('|')
    const found = counts.get(signature)
    if (found) {
      found.count++
    } else {
      counts.set(signature, { items, count: 1 })
    }
  }

  let best: { items: string[]; count: number } | null = null
  for (const value of counts.values()) {
    if (!best || value.count > best.count) best = value
  }
  return best && best.count >= 2 ? best.items : null
}

/** C3: áp bộ đang chọn cho MỌI dòng còn thiếu danh mục trong phạm vi đang lọc. */
const applyingMissing = ref(false)

async function applyToMissingInScope(): Promise<void> {
  const active = session.value
  if (!active || !scopeReady.value) return
  if (bulkItems.value.length === 0) {
    toast.warning('Chưa chọn giấy tờ', 'Chọn danh mục giấy tờ cần áp dụng trước.')
    return
  }

  applyingMissing.value = true
  try {
    const pageSize = 200
    const next = new Map(edits.value)
    let applied = 0
    let total = 0
    let pageNo = 1
    do {
      const response = await request<SummaryRowsResponse>(
        `/api/summary/${active.sessionId}/rows${queryString({
          ...queryParams(),
          page: pageNo,
          pageSize,
        })}`,
      )
      total = response.total
      for (const row of response.items) {
        if (itemsFor(row).length > 0) continue
        const summary = composeSummary(row, bulkItems.value)
        const baseline = row.needsNormalization ? row.normalizedSummary : row.summary
        if (summary === baseline) continue
        const current = next.get(row.rowNumber)
        next.set(row.rowNumber, {
          row,
          items: [...bulkItems.value],
          summaryText: null,
          sheetNumber: current?.sheetNumber ?? null,
          documentNumber: current?.documentNumber ?? null,
          numberLabel: current?.numberLabel ?? null,
        })
        applied++
      }
      pageNo++
    } while ((pageNo - 1) * pageSize < total && pageNo <= 400)

    edits.value = next
    pushHistory()
    if (applied === 0) {
      toast.info('Không có dòng nào cần áp', 'Mọi dòng còn thiếu đã trùng bộ đang chọn.')
    } else {
      toast.success(
        'Đã áp cho dòng còn thiếu',
        `${formatNumber(applied)} dòng trong phạm vi đang lọc nhận bộ ${bulkItems.value.join(', ')}.`,
      )
      void announceAction(
        'bulk',
        `Gán bộ cho ${formatNumber(applied)} dòng còn thiếu trong phạm vi lọc`,
      )
      // Broadcast bulk change cho cả phòng
      broadcastBulkChanges(allChanges.value)
    }
  } catch (caught) {
    toast.error('Không áp được', errorMessage(caught))
  } finally {
    applyingMissing.value = false
  }
}

/** C5: xuất nhanh 1 click theo phạm vi đang lọc, nhớ tên file của lần trước. */
const QUICK_NAME_KEY = 'summary.quickName'
const quickName = ref(localStorage.getItem(QUICK_NAME_KEY) ?? '')

async function quickExportFiltered(): Promise<void> {
  const active = session.value
  if (!active || !scopeReady.value) return
  if (allChanges.value.length === 0) {
    toast.warning('Chưa có thay đổi nào', 'Sửa ít nhất một dòng rồi mới xuất nhanh.')
    return
  }

  exportName.value = quickName.value.trim() || suggestedFileName('filtered')
  await doExportFiltered()
  quickName.value = exportName.value.trim()
  localStorage.setItem(QUICK_NAME_KEY, quickName.value)
}

/* ==================== Nhóm I — phiên đọc trích yếu bằng giọng nói ====================
 * GĐ 1: Web Speech API (Chrome/Edge, vi-VN, qua tunnel HTTPS được). Mọi câu
 * nói chỉ PREFILL vào khung soạn để mắt duyệt — lưu/xuất không đổi một pixel.
 * Parser + từ vựng nằm ở lib/voiceParser.ts (thuần, test được).
 */

const {
  supported: voiceSupported,
  listening: voiceListening,
  interim: voiceInterim,
  toggle: toggleVoiceInput,
} = useVoiceInput({
  onFinal: (text) => handleVoiceFinal(text),
  onInterim: () => undefined,
  onError: (message) => {
    if (message) toast.error('Không nghe được', message)
  },
})

const voiceLastHeard = ref('')

/** Học lỗi nghe (I2): {máy nghe sai → người sửa} — tối đa 100 cặp. */
interface VoiceLearnedPair {
  heard: string
  fixed: string
}

const VOICE_LEARN_KEY = 'stp.voiceLearn'
const voiceLearned = ref<VoiceLearnedPair[]>(readVoiceLearned())
let voiceLearnTimer: number | undefined

function readVoiceLearned(): VoiceLearnedPair[] {
  try {
    const raw = localStorage.getItem(VOICE_LEARN_KEY)
    const list = raw ? (JSON.parse(raw) as unknown) : []
    if (!Array.isArray(list)) return []
    return list.filter(
      (item): item is VoiceLearnedPair =>
        !!item &&
        typeof item === 'object' &&
        typeof (item as VoiceLearnedPair).heard === 'string' &&
        typeof (item as VoiceLearnedPair).fixed === 'string',
    )
  } catch {
    return []
  }
}

function rememberVoiceCorrection(heard: string, fixed: string): void {
  const normalizedHeard = normalizeSpeech(heard)
  if (!normalizedHeard) return
  voiceLearned.value = [
    { heard, fixed },
    ...voiceLearned.value.filter((pair) => normalizeSpeech(pair.heard) !== normalizedHeard),
  ].slice(0, 100)
  if (voiceLearnTimer) window.clearTimeout(voiceLearnTimer)
  voiceLearnTimer = window.setTimeout(() => {
    try {
      localStorage.setItem(VOICE_LEARN_KEY, JSON.stringify(voiceLearned.value))
    } catch {
      // Học là tiện ích — không lưu được thì bỏ qua.
    }
  }, 700)
}

/** Dòng vừa được voice điền — nếu người dùng nắn khác thì thành bài học. */
const voiceLearnCandidate = ref<{
  rowNumber: number
  heard: string
  prefill: string
} | null>(null)

let voiceSuppressLearn = false

function toggleVoice(): void {
  if (!voiceSupported.value) {
    toast.warning(
      'Trình duyệt chưa hỗ trợ',
      'Đọc giọng nói cần Chrome hoặc Edge (Web Speech API, tiếng Việt).',
    )
    return
  }
  toggleVoiceInput()
  if (!voiceListening.value) {
    voiceInterim.value = ''
    voiceLastHeard.value = ''
  }
}

/** Dòng đang mở editor — mục tiêu mặc định của câu nói. */
const voiceTargetRow = computed<SummaryRowResponse | null>(() => {
  if (editingSummaryRow.value == null) return null
  return rows.value.find((row) => row.rowNumber === editingSummaryRow.value) ?? null
})

function runVoiceCommand(command: 'commit' | 'next' | 'prev' | 'seven'): void {
  const current = voiceTargetRow.value
  if (command === 'commit') {
    if (!current) {
      toast.info('Chưa mở dòng nào', 'Mở "Sửa toàn bộ" một dòng rồi nói "xong".')
      return
    }
    if (!summaryDraft.value.trim()) {
      toast.info('Khung soạn trống', 'Không có gì để lưu.')
      return
    }
    saveSummaryEditor(current)
    return
  }
  if (command === 'next') {
    const from = current?.rowNumber ?? rows.value[0]?.rowNumber ?? 0
    const next = nextMissingRow(from)
    if (!next) {
      toast.info('Hết dòng thiếu', 'Không còn dòng thiếu danh mục trong phạm vi đang xem.')
      return
    }
    openSummaryEditor(next)
    void scrollEditorIntoView()
    return
  }
  if (command === 'prev') {
    const ordered = [...rows.value].sort((a, b) => a.rowNumber - b.rowNumber)
    const index = current ? ordered.findIndex((row) => row.rowNumber === current.rowNumber) : -1
    const previous = index > 0 ? ordered[index - 1] : null
    if (!previous) {
      toast.info('Đây là dòng đầu', 'Không còn dòng nào phía trước.')
      return
    }
    openSummaryEditor(previous)
    void scrollEditorIntoView()
    return
  }
  // "bộ bảy" — điền Bộ 7 chuẩn vào dòng đang mở.
  if (!current) {
    toast.info('Chưa mở dòng nào', 'Mở "Sửa toàn bộ" một dòng rồi nói "bộ bảy".')
    return
  }
  voiceSuppressLearn = true
  setRowItems(current, [...STANDARD_SEVEN])
  voiceSuppressLearn = false
  openSummaryEditor(current)
  toast.success('Đã điền Bộ 7 chuẩn', 'Kiểm rồi bấm Lưu hoặc nói "xong".')
}

function handleVoiceFinal(text: string): void {
  const active = session.value
  if (!active) return
  voiceLastHeard.value = text

  const learned = applyLearned(text, voiceLearned.value)
  const context = {
    catalog: active.catalog,
    types: active.types.map((item) => item.name),
  }
  const result = parseVoiceTranscript(learned, context)
  if (!result) {
    toast.info(
      'Không hiểu câu này',
      `"${text}" — nói loại hồ sơ + giấy tờ ("kết hôn — căn cước, hộ khẩu") hoặc lệnh "xong", "kế tiếp".`,
    )
    return
  }

  if (result.kind === 'command') {
    runVoiceCommand(result.command)
    return
  }

  if (result.kind === 'filter') {
    if (result.type !== null) type.value = result.type
    if (result.year !== null) year.value = result.year
    const parts: string[] = []
    if (result.type) parts.push(`loại ${result.type}`)
    if (result.year !== null) parts.push(`năm ${result.year}`)
    toast.success('Đã lọc bằng giọng nói', parts.length > 0 ? parts.join(' · ') : 'bỏ lọc loại')
    return
  }

  // Prefill: điền vào dòng đang mở, hoặc dòng thiếu kế tiếp nếu chưa mở ai.
  let row = voiceTargetRow.value
  if (!row) row = nextMissingRow(rows.value[0]?.rowNumber ?? 0)
  if (!row) {
    toast.info('Không có dòng để điền', 'Bấm "Sửa toàn bộ" một dòng rồi nói tiếp.')
    return
  }

  const spokenType = result.type
  if (spokenType && row.type && normalizeSpeech(spokenType) !== normalizeSpeech(row.type)) {
    toast.info(
      'Loại nói khác loại dòng',
      `Nói "${spokenType}" nhưng dòng ${row.rowNumber} là "${row.type}" — vẫn điền giấy tờ, kiểm trước khi lưu.`,
    )
  }

  voiceSuppressLearn = true
  if (result.sheetNumber !== null) {
    commit(row, { sheetNumber: result.sheetNumber })
  }
  if (result.items.length > 0) {
    setRowItems(row, result.items)
  }
  voiceSuppressLearn = false

  openSummaryEditor(row)
  void scrollEditorIntoView()
  voiceLearnCandidate.value = {
    rowNumber: row.rowNumber,
    heard: text,
    prefill: summaryFor(row),
  }
  toast.success(
    'Đã điền — kiểm rồi lưu',
    `Dòng ${row.rowNumber}: ${result.items.join(', ') || '(giữ nguyên danh mục)'}${
      result.sheetNumber ? ' · số tờ ' + result.sheetNumber : ''
    }`,
  )
}

/** C2: chép bộ giấy tờ của dòng đứng ngay trên (trong danh sách đang xem). */
function applyFromAbove(row: SummaryRowResponse): void {
  const ordered = [...rows.value].sort((a, b) => a.rowNumber - b.rowNumber)
  const index = ordered.findIndex((item) => item.rowNumber === row.rowNumber)
  const above = index > 0 ? ordered[index - 1] : null
  const aboveItems = above ? itemsFor(above) : []
  if (aboveItems.length === 0) {
    toast.info(
      'Dòng trên chưa có danh mục',
      `Dòng ${above?.rowNumber ?? 'trước đó'} trống — sửa dòng trên trước rồi bấm C.`,
    )
    return
  }

  commit(row, { items: aboveItems, summaryText: null })
  toast.success(
    'Đã chép như dòng trên',
    `Dòng ${row.rowNumber} nhận ${aboveItems.length} mục từ dòng ${above!.rowNumber}.`,
  )
  jumpToNextMissing(row.rowNumber)
}

/** C3: áp bộ phổ biến nhất của loại hồ sơ cho dòng này. */
function applyDefaultForType(row: SummaryRowResponse): void {
  const items = defaultSetForType(row)
  if (!items || items.length === 0) {
    toast.info('Chưa học được bộ cho loại này', 'Sửa vài dòng cùng loại trước, máy sẽ học bộ.')
    return
  }

  commit(row, { items: [...items], summaryText: null })
  toast.success(
    'Đã dùng bộ của loại này',
    `Dòng ${row.rowNumber} nhận ${items.length} mục: ${items.join(', ')}.`,
  )
  jumpToNextMissing(row.rowNumber)
}

/** C2: phím C (đứng ngoài ô nhập) — chép bộ giấy tờ của dòng trên xuống dòng đang sửa. */
function onSpeedKeydown(event: KeyboardEvent): void {
  if (event.key !== 'c' && event.key !== 'C') return
  if (event.ctrlKey || event.metaKey || event.altKey) return
  const target = event.target as HTMLElement | null
  if (
    target &&
    (target.tagName === 'INPUT' ||
      target.tagName === 'TEXTAREA' ||
      target.tagName === 'SELECT' ||
      target.isContentEditable)
  ) {
    return
  }

  const ordered = [...rows.value].sort((a, b) => a.rowNumber - b.rowNumber)
  const anchor =
    editingSummaryRow.value !== null
      ? ordered.find((row) => row.rowNumber === editingSummaryRow.value)
      : ordered.find((row) => itemsFor(row).length === 0)
  if (!anchor) {
    toast.info('Không có dòng nào cần chép', 'Mọi dòng trong phạm vi này đều đã có danh mục.')
    return
  }

  applyFromAbove(anchor)
}

function resetEdits(): void {
  if (edits.value.size === 0) return
  if (!window.confirm('Bỏ toàn bộ thay đổi chưa xuất?')) return
  edits.value = new Map()
  pushHistory()
  closeSummaryEditor()
  selected.value = new Set()
  clearDraft()
}

function toggleSelect(rowNumber: number): void {
  const next = new Set(selected.value)
  if (next.has(rowNumber)) next.delete(rowNumber)
  else next.add(rowNumber)
  selected.value = next
}

function toggleSelectPage(): void {
  const next = new Set(selected.value)
  if (allPageSelected.value) {
    rows.value.forEach((row) => next.delete(row.rowNumber))
  } else {
    rows.value.forEach((row) => next.add(row.rowNumber))
  }
  selected.value = next
}

function clearSelection(): void {
  selected.value = new Set()
}

function applyBulk(): void {
  if (selected.value.size === 0) {
    toast.warning('Chưa chọn dòng', 'Tích chọn ít nhất một dòng trong trang hiện tại.')
    return
  }

  if (bulkItems.value.length === 0) {
    toast.warning('Chưa chọn giấy tờ', 'Chọn danh mục giấy tờ cần áp dụng cho các dòng đã tích.')
    return
  }

  bulkApplying.value = true
  let applied = 0
  const next = new Map(edits.value)
  const changesApplied: SummaryChangeRequest[] = []

  for (const row of rows.value) {
    if (!selected.value.has(row.rowNumber)) continue
    const current = next.get(row.rowNumber) ?? {
      row,
      items: row.items,
      summaryText: null,
      sheetNumber: null,
      documentNumber: null,
      numberLabel: null,
    }
    const nextEdit: RowEdit = { ...current, items: bulkItems.value, summaryText: null }
    const summary = composeSummary(row, nextEdit.items)
    const sheet = nextEdit.sheetNumber ?? row.sheetNumber
    const doc = nextEdit.documentNumber ?? row.documentNumber
    const num = nextEdit.numberLabel ?? row.numberLabel
    const baseline = row.needsNormalization ? row.normalizedSummary : row.summary
    if (
      summary !== baseline ||
      sheet !== row.sheetNumber ||
      doc !== row.documentNumber ||
      num !== row.numberLabel
    ) {
      next.set(row.rowNumber, nextEdit)
      changesApplied.push({
        rowNumber: row.rowNumber,
        items: nextEdit.items,
        summaryText: nextEdit.summaryText,
        sheetNumber: nextEdit.sheetNumber,
        documentNumber: nextEdit.documentNumber,
        numberLabel: nextEdit.numberLabel,
      })
    } else {
      next.delete(row.rowNumber)
    }
    applied++
  }

  edits.value = next
  pushHistory()
  bulkApplying.value = false
  if (changesApplied.length > 0) {
    broadcastBulkChanges(changesApplied)
  }
  toast.success(
    'Đã áp dụng danh mục',
    `${formatNumber(applied)} dòng trong trang hiện tại đã nhận ${bulkItems.value.length} loại giấy tờ.`,
  )
  void announceAction(
    'bulk',
    `Gán ${bulkItems.value.length} loại giấy tờ cho ${formatNumber(applied)} dòng đã chọn`,
  )
}

/** F3: báo cho cả nhóm biết một hành động đáng chú ý làm phía client (bulk…). */
async function announceAction(verb: string, detail: string): Promise<void> {
  try {
    await request('/api/team/action', { method: 'POST', body: { verb, detail } })
  } catch {
    // Chip là phụ — lỗi bỏ qua, hành động chính không phụ thuộc nó.
  }
}

/** Đưa các dòng đang chọn về đúng tiền tố chuẩn, giữ nguyên danh mục giấy tờ. */
function normalizeSelected(): void {
  if (selectedNormalizableCount.value === 0) {
    toast.info('Không có dòng sai mở đầu', 'Các dòng đang chọn đã đúng mẫu.')
    return
  }

  const next = new Map(edits.value)
  let applied = 0
  const changesApplied: SummaryChangeRequest[] = []

  for (const row of rows.value) {
    if (!selected.value.has(row.rowNumber) || !row.needsNormalization) continue

    const current = next.get(row.rowNumber) ?? {
      row,
      items: row.items,
      summaryText: null,
      sheetNumber: null,
      documentNumber: null,
      numberLabel: null,
    }

    // Không ghi đè câu người dùng đã sửa tay; bản xuất vẫn tự chuẩn hoá tiền tố.
    if (current.summaryText !== null) continue

    const summaryText = composeSummary(row, current.items)
    const nextEdit = {
      ...current,
      summaryText,
    }
    next.set(row.rowNumber, nextEdit)
    changesApplied.push({
      rowNumber: row.rowNumber,
      items: nextEdit.items,
      summaryText: nextEdit.summaryText,
      sheetNumber: nextEdit.sheetNumber,
      documentNumber: nextEdit.documentNumber,
      numberLabel: nextEdit.numberLabel,
    })
    applied++
  }

  edits.value = next
  pushHistory()
  if (changesApplied.length > 0) {
    broadcastBulkChanges(changesApplied)
  }
  toast.success(
    'Đã sửa mở đầu đúng mẫu',
    `${formatNumber(applied)} dòng đang chọn đã về đúng mẫu.`,
  )
}

async function inspect(): Promise<void> {
  const file = files.value[0]
  if (!file) {
    toast.warning('Chưa chọn file', 'Hãy chọn workbook Excel cần bổ sung trích yếu.')
    return
  }

  inspecting.value = true
  inspectError.value = null
  const previousSession = session.value?.sessionId

  try {
    const form = new FormData()
    form.append('file', file)
    const data = await request<SummaryInspectResponse>('/api/summary/inspect', {
      method: 'POST',
      form,
    })

    if (previousSession) {
      void deleteSessionSilent(previousSession)
    }

    session.value = data
    rememberSession(data.sessionId, data.fileName)
    loadHistory(data.fileName)
    edits.value = new Map()
    resetHistory()
    selected.value = new Set()
    bulkItems.value = []
    previewRows.value = []
    previewOpen.value = false
    previewMode.value = 'full'
    filteredPreview.value = null
    year.value = ''
    yearFrom.value = ''
    yearTo.value = ''
    type.value = ''
    search.value = ''
    onlyExisting.value = false
    numberMode.value = 'all'
    numberFrom.value = ''
    numberTo.value = ''
    singleNumber.value = ''
    onlyMissingSheetNumber.value = false
    onlyMissingDocumentNumber.value = false
    onlyMissingNumber.value = false
    onlyNeedsNormalization.value = false
    missingSheetNumberFirst.value = false
    page.value = 1
    pageSize.value = 20
    files.value = []
    closeSummaryEditor()
    draftSavedAt.value = null
    draftStatus.value = 'idle'

    toast.success(
      'Đã đọc workbook',
      `${formatNumber(data.rowCount)} hồ sơ · ${formatNumber(data.missingSheetNumberCount)} dòng thiếu số tờ · ${formatNumber(data.normalizableCount)} dòng sai mở đầu.`,
    )

    restoreDraft(data.fileName)
    resetHistory()
    await fetchRows()
    await fetchYearReport()
  } catch (caught) {
    inspectError.value = errorMessage(caught)
  } finally {
    inspecting.value = false
  }
}

async function fetchYearReport(): Promise<void> {
  const active = session.value
  if (!active) return

  reportLoading.value = true
  try {
    yearReport.value = await request<Array<{ year: number; total: number; missingSheetNumber: number; missingDocumentNumber: number; missingNumber: number; needsNormalization: number }>>(
      `/api/summary/${active.sessionId}/report`,
    )
  } catch {
    yearReport.value = []
  } finally {
    reportLoading.value = false
  }
}

function exportYearReportCsv(): void {
  if (yearReport.value.length === 0) return

  const header = ['Năm', 'Tổng hồ sơ', 'Thiếu số tờ', 'Thiếu hồ sơ số', 'Thiếu số hồ sơ', 'Sai mở đầu']
  const lines = [
    header.map((cell) => `"${cell}"`).join(','),
    ...yearReport.value.map((item) =>
      [item.year, item.total, item.missingSheetNumber, item.missingDocumentNumber, item.missingNumber, item.needsNormalization]
        .map((cell) => `"${cell}"`)
        .join(','),
    ),
  ]
  const csv = `\uFEFF${lines.join('\r\n')}`
  const baseName = session.value?.fileName.replace(/\.[^.]+$/, '') ?? 'bao-cao'
  saveBlob(new Blob([csv], { type: 'text/csv;charset=utf-8' }), `${baseName}_bao-cao-theo-nam.csv`)
  toast.success('Đã xuất báo cáo', `${formatNumber(yearReport.value.length)} năm đã được ghi vào CSV.`)
}

function suggestedPrintFileName(): string {
  const base = (session.value?.fileName ?? 'ban-in').replace(/\.[^.]+$/, '')
  return `${base}_ban_in`
}

function openPrintDialog(): void {
  const active = session.value
  if (!active) return

  printError.value = null
  printPreview.value = null
  printYears.value = yearReport.value.length > 0
    ? yearReport.value.map((item) => item.year)
    : active.years.map((item) => item.year)
  printMode.value = 'perYear'
  printAllowIncomplete.value = false
  printFileName.value = suggestedPrintFileName()
  printOpen.value = true
  void fetchPrintPreview(true)
}

async function fetchPrintPreview(selectAll = false): Promise<void> {
  const active = session.value
  if (!active) return

  printLoading.value = true
  printError.value = null
  try {
    // Luôn lấy đủ mọi năm để danh sách checkbox không co lại khi bỏ chọn một năm.
    printPreview.value = await request<SummaryPrintPreviewResponse>(
      `/api/summary/${active.sessionId}/export-print/preview`,
      { method: 'POST', body: { changes: allChanges.value } },
    )

    if (selectAll) {
      printYears.value = (printPreview.value?.years ?? []).map((item) => item.year)
    }
  } catch (caught) {
    printError.value = errorMessage(caught)
  } finally {
    printLoading.value = false
  }
}

function togglePrintYear(year: number): void {
  const next = new Set(printYears.value)
  if (next.has(year)) {
    next.delete(year)
  } else {
    next.add(year)
  }
  printYears.value = [...next].sort((a, b) => a - b)
}

function selectAllPrintYears(): void {
  printYears.value = (printPreview.value?.years ?? []).map((item) => item.year)
}

function clearPrintYears(): void {
  printYears.value = []
}

function printYearMissing(status: SummaryPrintYearStatusResponse): number {
  return status.missingNumber
    + status.missingDocumentCode
    + status.missingDate
    + status.missingSheetNumber
    + status.missingSummary
}

async function exportPrint(): Promise<void> {
  const active = session.value
  if (!active) return

  if (printYears.value.length === 0) {
    printError.value = 'Chọn ít nhất một năm để xuất bản in.'
    return
  }

  if (printSelectedMissing.value > 0 && !printAllowIncomplete.value) {
    printError.value = 'Còn dữ liệu thiếu. Bổ sung hoặc chọn “Vẫn xuất dù còn thiếu”.'
    return
  }

  printExporting.value = true
  printError.value = null
  try {
    const result = await download(`/api/summary/${active.sessionId}/export-print`, {
      method: 'POST',
      body: {
        changes: allChanges.value,
        years: printYears.value,
        mode: printMode.value,
        allowIncomplete: printAllowIncomplete.value,
        outputFileName: printFileName.value.trim() || undefined,
      },
    })

    saveBlob(result.blob, result.fileName)
    pushHistoryEntry(
      'Xuất bản in',
      `${result.fileName} · ${formatNumber(printSelectedRows.value)} hồ sơ`,
    )
    toast.success(
      'Đã xuất bản in',
      `${result.fileName} · ${formatNumber(printSelectedRows.value)} hồ sơ, ${formatNumber(printYears.value.length)} năm.`,
    )
    printOpen.value = false
  } catch (caught) {
    printError.value = errorMessage(caught)
  } finally {
    printExporting.value = false
  }
}

function queryParams(): Record<string, string | number | boolean | null | undefined> {
  // Chỉ gửi numberMode khi bộ lọc số đủ dữ liệu — tránh 400
  // "Nhập ít nhất một đầu của khoảng số hồ sơ." khi người dùng mới chọn chế độ.
  const rangeReady =
    numberMode.value === 'all' ||
    (numberMode.value === 'range' && (numberFrom.value !== '' || numberTo.value !== ''))
  const singleReady = numberMode.value !== 'single' || singleNumber.value !== ''

  return {
    year: year.value === '' ? undefined : year.value,
    yearFrom: yearFrom.value === '' ? undefined : yearFrom.value,
    yearTo: yearTo.value === '' ? undefined : yearTo.value,
    type: type.value || undefined,
    search: search.value.trim() || undefined,
    onlyExisting: onlyExisting.value || undefined,
    numberMode: rangeReady && singleReady && numberMode.value !== 'all' ? numberMode.value : undefined,
    numberFrom:
      numberMode.value === 'range' && numberFrom.value !== '' ? numberFrom.value : undefined,
    numberTo:
      numberMode.value === 'range' && numberTo.value !== '' ? numberTo.value : undefined,
    number:
      numberMode.value === 'single' && singleNumber.value !== ''
        ? singleNumber.value
        : undefined,
    onlyMissingSheetNumber: onlyMissingSheetNumber.value || undefined,
    onlyMissingDocumentNumber: onlyMissingDocumentNumber.value || undefined,
    onlyMissingNumber: onlyMissingNumber.value || undefined,
    onlyNeedsNormalization: onlyNeedsNormalization.value || undefined,
    missingSheetNumberFirst: missingSheetNumberFirst.value || undefined,
  }
}

async function fetchRows(): Promise<void> {
  const active = session.value
  // Bộ lọc số chưa nhập đủ (chọn chế độ khoảng/số đơn mà để trống) thì không
  // gọi API — server trả 400. Bảng hiển thị hướng dẫn nhập số hồ sơ.
  if (!scopeReady.value) return
  if (!active) return

  loadingRows.value = true
  rowsError.value = null

  try {
    const response = await request<SummaryRowsResponse>(
      `/api/summary/${active.sessionId}/rows${queryString({
        ...queryParams(),
        page: page.value,
        pageSize: pageSize.value,
      })}`,
    )

    if (response.items.length === 0 && response.total > 0 && page.value > 1) {
      page.value = Math.max(1, Math.ceil(response.total / response.pageSize))
      await fetchRows()
      return
    }

    rows.value = response.items
    total.value = response.total
    pageSize.value = response.pageSize
    page.value = response.page
    selected.value = new Set()
  } catch (caught) {
    rowsError.value = errorMessage(caught)
  } finally {
    loadingRows.value = false
  }
}

async function deleteSessionSilent(sessionId: string): Promise<void> {
  try {
    await request<void>(`/api/summary/${sessionId}`, { method: 'DELETE' })
  } catch {
    // Phiên sẽ tự hết hạn và được dọn sau 12 giờ.
  }
}

function closeSession(): void {
  const active = session.value
  if (!active) return

  if (changedCount.value > 0 && !window.confirm('Bỏ các thay đổi chưa xuất và đóng workbook?')) {
    return
  }

  leaveSummarySession(active.sessionId, myActorName.value ?? 'Quản trị')
  if (isAdmin.value && activeTeamSession.value?.sessionId === active.sessionId) {
    void request('/api/summary/active', { method: 'DELETE' }).catch(() => {})
    activeTeamSession.value = null
  }

  void deleteSessionSilent(active.sessionId)
  clearDraft()
  forgetSession()
  historyEntries.value = []
  session.value = null
  rows.value = []
  total.value = 0
  edits.value = new Map()
  selected.value = new Set()
  previewRows.value = []
  previewOpen.value = false
  previewMode.value = 'full'
  filteredPreview.value = null
  inspectError.value = null
  closeSummaryEditor()
  draftSavedAt.value = null
  draftStatus.value = 'idle'
}

function goToPage(next: number): void {
  const target = Math.min(Math.max(1, next), totalPages.value)
  if (target === page.value) return
  page.value = target
  void fetchRows()
}

async function openPreview(): Promise<void> {
  const active = session.value
  if (!active) return

  previewMode.value = 'full'
  previewOpen.value = true
  previewLoading.value = true
  previewError.value = null
  previewRows.value = []
  filteredPreview.value = null

  try {
    const only = effectiveOnlyRowNumbers() ?? (onlySelectedRows.value ? selectedRowNumbers.value : undefined)
    const result = await request<SummaryPreviewRowResponse[]>(
      `/api/summary/${active.sessionId}/preview`,
      { method: 'POST', body: { changes: allChanges.value, onlyRowNumbers: only } },
    )

    previewRows.value = result
    if (result.length === 0) {
      previewError.value =
        'Không có dòng nào cần ghi: trích yếu và số tờ đều đã đúng chuẩn. Hãy sửa ít nhất một dòng.'
    }
  } catch (caught) {
    previewError.value = errorMessage(caught)
  } finally {
    previewLoading.value = false
  }
}

async function openFilteredPreview(): Promise<void> {
  const active = session.value
  if (!active) return

  if (!scopeReady.value) {
    toast.warning(
      'Phạm vi chưa đủ',
      'Nhập khoảng số hồ sơ hoặc số hồ sơ chỉ định trước khi xuất bản lọc.',
    )
    return
  }

  exportScope.value = 'filtered'
  previewMode.value = 'filtered'
  previewOpen.value = true
  previewLoading.value = true
  previewError.value = null
  previewRows.value = []
  filteredPreview.value = null

  try {
    const result = await request<SummaryFilteredPreviewResponse>(
      `/api/summary/${active.sessionId}/export-filtered/preview`,
      { method: 'POST', body: { changes: allChanges.value, scope: scopeRequest.value } },
    )

    filteredPreview.value = result
    previewRows.value = result.items

    if (result.totalRows === 0) {
      previewError.value =
        'Phạm vi hiện tại không có hồ sơ nào. Hãy nới điều kiện lọc trước khi xuất.'
    } else if (result.items.length === 0) {
      previewError.value = null
    }
  } catch (caught) {
    previewError.value = errorMessage(caught)
  } finally {
    previewLoading.value = false
  }
}

function suggestedFileName(mode: 'full' | 'filtered'): string {
  const base = (session.value?.fileName ?? 'xuat').replace(/\.[^.]+$/, '')
  const ext = (session.value?.fileName.match(/\.[^.]+$/)?.[0] ?? '.xlsx')
  if (mode === 'full') return `${base}_trichyeu${ext}`
  const suffix =
    session.value?.fileName && filteredPreview.value
      ? '_loc'
      : '_loc'
  return `${base}${suffix}${ext}`
}

function openExportName(mode: 'full' | 'filtered'): void {
  pendingExportMode.value = mode
  exportName.value = suggestedFileName(mode)
  exportNameOpen.value = true
}

function confirmExportName(): void {
  exportNameOpen.value = false
  if (pendingExportMode.value === 'full') {
    void doExportFull()
  } else {
    void doExportFiltered()
  }
}

async function doExportFull(): Promise<void> {
  const active = session.value
  if (!active) return

  exporting.value = true
  try {
    const only = effectiveOnlyRowNumbers() ?? (onlySelectedRows.value ? selectedRowNumbers.value : undefined)
    const result = await download(`/api/summary/${active.sessionId}/export`, {
      method: 'POST',
      body: {
        changes: allChanges.value,
        outputFileName: exportName.value.trim() || undefined,
        onlyRowNumbers: only,
      },
    })

    saveBlob(result.blob, result.fileName)
    lastExportedBlob.value = result.blob
    lastExportedName.value = result.fileName
    const normalized = previewRows.value.filter((row) => row.summaryChanged).length
    pushHistoryEntry(
      'Xuất file',
      `${result.fileName} · ${formatNumber(previewRows.value.length)} dòng${normalized > 0 ? ` · ${formatNumber(normalized)} sửa mở đầu` : ''}`,
    )
    toast.success(
      'Đã xuất file Excel',
      `${result.fileName} · ${formatNumber(previewRows.value.length)} dòng được cập nhật${normalized > 0 ? ` · ${formatNumber(normalized)} dòng chuẩn hoá` : ''}.`,
    )
    previewOpen.value = false
    saveToArchiveOpen.value = true
    clearDraft()
    draftSavedAt.value = null
    draftStatus.value = 'idle'
  } catch (caught) {
    toast.error('Không xuất được file', errorMessage(caught))
  } finally {
    exporting.value = false
  }
}

async function doExportFiltered(): Promise<void> {
  const active = session.value
  if (!active || !scopeReady.value) return

  filteredExporting.value = true
  try {
    const result = await download(`/api/summary/${active.sessionId}/export-filtered`, {
      method: 'POST',
      body: { changes: allChanges.value, scope: scopeRequest.value, outputFileName: exportName.value.trim() || undefined },
    })

    saveBlob(result.blob, result.fileName)
    lastExportedBlob.value = result.blob
    lastExportedName.value = result.fileName
    // Xuất nhanh không chạy preview — đếm trực tiếp số dòng có thay đổi.
    const keptRows = filteredPreview.value?.totalRows ?? total.value
    const changedRows =
      filteredPreview.value?.changedRows ?? new Set(allChanges.value.map((c) => c.rowNumber)).size
    const normalizedRows = filteredPreview.value?.normalizedRows ?? 0
    pushHistoryEntry(
      'Xuất bản lọc',
      `${result.fileName} · giữ ${formatNumber(keptRows)} hồ sơ · ${formatNumber(changedRows)} dòng cập nhật`,
    )
    toast.success(
      'Đã xuất bản lọc',
      `${result.fileName} · ${formatNumber(keptRows)} hồ sơ được giữ · ${formatNumber(changedRows)} dòng cập nhật${normalizedRows > 0 ? ` · ${formatNumber(normalizedRows)} dòng chuẩn hoá` : ''}.`,
    )
    previewOpen.value = false
    saveToArchiveOpen.value = true
    clearDraft()
    draftSavedAt.value = null
    draftStatus.value = 'idle'
  } catch (caught) {
    toast.error('Không xuất được bản lọc', errorMessage(caught))
  } finally {
    filteredExporting.value = false
  }
}

function csvCell(value: string | number | null): string {
  const text = value === null || value === undefined ? '' : String(value)
  return `"${text.replace(/"/g, '""')}"`
}

/** Xuất danh sách các dòng còn thiếu số tờ / hồ sơ số / số hồ sơ trong phạm vi hiện tại. */
async function exportIssues(): Promise<void> {
  const active = session.value
  if (!active) return

  exportingIssues.value = true
  try {
    const all: SummaryRowResponse[] = []
    let currentPage = 1
    let pageCount = 1

    do {
      const response = await request<SummaryRowsResponse>(
        `/api/summary/${active.sessionId}/rows${queryString({
          ...queryParams(),
          page: currentPage,
          pageSize: 200,
        })}`,
      )
      all.push(...response.items)
      pageCount = Math.max(1, Math.ceil(response.total / response.pageSize))
      currentPage++
    } while (currentPage <= pageCount && currentPage <= 500)

    const issues = all.filter(
      (row) =>
        !row.sheetNumber.trim() || !row.documentNumber.trim() || !row.numberLabel.trim(),
    )

    if (issues.length === 0) {
      toast.info('Không có dòng thiếu dữ liệu', 'Mọi hồ sơ trong phạm vi đều đã đủ thông tin.')
      return
    }

    const header = [
      'Dòng',
      'Số hồ sơ',
      'Hồ sơ số',
      'Số tờ',
      'Năm',
      'Loại',
      'Trích yếu',
      'Thiếu',
    ]
    const lines = [
      header.map(csvCell).join(','),
      ...issues.map((row) => {
        const missing: string[] = []
        if (!row.numberLabel.trim()) missing.push('số hồ sơ')
        if (!row.documentNumber.trim()) missing.push('hồ sơ số')
        if (!row.sheetNumber.trim()) missing.push('số tờ')
        return [
          row.rowNumber,
          row.numberLabel,
          row.documentNumber,
          row.sheetNumber,
          row.year ?? '',
          row.type ?? '',
          row.summary,
          missing.join('; '),
        ]
          .map(csvCell)
          .join(',')
      }),
    ]

    const csv = `\uFEFF${lines.join('\r\n')}`
    const baseName = active.fileName.replace(/\.[^.]+$/, '')
    saveBlob(
      new Blob([csv], { type: 'text/csv;charset=utf-8' }),
      `${baseName}_thieu-du-lieu.csv`,
    )
    toast.success(
      'Đã xuất danh sách thiếu dữ liệu',
      `${formatNumber(issues.length)} dòng trong phạm vi · mở bằng Excel để bổ sung.`,
    )
  } catch (caught) {
    toast.error('Không xuất được danh sách', errorMessage(caught))
  } finally {
    exportingIssues.value = false
  }
}

function draftKey(fileName: string): string {
  return `${DRAFT_PREFIX}${fileName}`
}

function buildDraftPayload(): DraftPayload | null {
  const active = session.value
  if (!active || edits.value.size === 0) return null

  return {
    fileName: active.fileName,
    savedAt: new Date().toISOString(),
    edits: [...edits.value.values()].map((edit) => ({
      row: edit.row,
      items: edit.items,
      summaryText: edit.summaryText,
      sheetNumber: edit.sheetNumber,
      documentNumber: edit.documentNumber,
      numberLabel: edit.numberLabel,
    })),
  }
}

function saveDraft(): void {
  const active = session.value
  if (!active) return

  try {
    const payload = buildDraftPayload()
    if (!payload) {
      localStorage.removeItem(draftKey(active.fileName))
      draftSavedAt.value = null
      draftStatus.value = 'idle'
      return
    }

    localStorage.setItem(draftKey(active.fileName), JSON.stringify(payload))
    draftSavedAt.value = new Date(payload.savedAt)
    draftStatus.value = 'saved'
  } catch {
    // localStorage có thể bị chặn hoặc đầy; bỏ qua và vẫn làm việc trong bộ nhớ.
  }
}

/* ===== E1: bản nháp sao lưu server — sống qua F5/restart/máy khác ===== */

/** Khoảng lặng giữa các lần sửa trước khi đẩy bản nháp lên server (payload lớn). */
const SERVER_SYNC_DELAY = 8000

let serverDraftTimer: number | undefined
let serverDraftInFlight = false
/** Số dòng edit đã đồng bộ lần cuối (-1 = chưa từng) — tránh PUT/DELETE lặp cùng trạng thái. */
let lastSyncedEditCount = -1

function scheduleServerDraftSync(delay = SERVER_SYNC_DELAY): void {
  if (serverDraftTimer) window.clearTimeout(serverDraftTimer)
  serverDraftTimer = window.setTimeout(() => {
    serverDraftTimer = undefined
    void syncServerDraft()
  }, delay)
}

async function syncServerDraft(): Promise<void> {
  const active = session.value
  if (!active || serverDraftInFlight) return

  const count = edits.value.size
  if (count === lastSyncedEditCount) return
  if (count === 0 && lastSyncedEditCount <= 0) return

  const payload = buildDraftPayload()
  serverDraftInFlight = true
  try {
    if (payload) {
      await request(`/api/summary/${active.sessionId}/draft`, {
        method: 'PUT',
        body: { fileName: payload.fileName, payload },
      })
    } else {
      // Bản nháp đã rỗng — xoá luôn bản cũ trên server nếu có.
      await request(`/api/summary/${active.sessionId}/draft`, { method: 'DELETE' })
    }
    lastSyncedEditCount = count
  } catch {
    // Sao lưu server là lưới an toàn: thất bại thì im lặng, localStorage vẫn giữ.
  } finally {
    serverDraftInFlight = false
  }
}

async function fetchServerDraft(sessionId: string): Promise<DraftPayload | null> {
  try {
    const data = await request<{ fileName: string; savedAt: string; payload: DraftPayload } | undefined>(
      `/api/summary/${sessionId}/draft`,
    )
    return data?.payload ?? null
  } catch {
    return null
  }
}

function restoreDraft(fileName: string): void {
  void restoreDraftAsync(fileName)
}

async function restoreDraftAsync(fileName: string): Promise<void> {
  try {
    let payload = readLocalDraft(fileName)
    let source = 'máy này'

    const active = session.value
    if (active) {
      const serverDraft = await fetchServerDraft(active.sessionId)
      if (serverDraft && serverDraft.edits.length > 0) {
        // Chọn bản mới hơn giữa localStorage và server (đổi máy / xoá localStorage).
        if (!payload || new Date(serverDraft.savedAt) > new Date(payload.savedAt)) {
          payload = serverDraft
          source = 'server'
        }
      }
    }

    if (!payload || payload.edits.length === 0) {
      if (source === 'máy này') localStorage.removeItem(draftKey(fileName))
      return
    }

    const savedAt = new Date(payload.savedAt)
    const label = savedAt.toLocaleString('vi-VN')
    if (
      !window.confirm(
        `Tìm thấy bản nháp ${payload.edits.length} dòng cho file này (${source}, ${label}). Khôi phục?`,
      )
    ) {
      localStorage.removeItem(draftKey(fileName))
      return
    }

    const next = new Map<number, RowEdit>()
    for (const entry of payload.edits) {
      if (!entry?.row) continue
      next.set(entry.row.rowNumber, {
        row: entry.row,
        items: entry.items ?? entry.row.items,
        summaryText: entry.summaryText ?? null,
        sheetNumber: entry.sheetNumber ?? null,
        documentNumber: entry.documentNumber ?? null,
        numberLabel: entry.numberLabel ?? null,
      })
    }

    edits.value = next
    draftSavedAt.value = savedAt
    draftStatus.value = 'saved'
    toast.success(
      'Đã khôi phục bản nháp',
      `${formatNumber(next.size)} dòng thay đổi đã được nạp (từ ${source}).`,
    )
  } catch {
    // Bản nháp hỏng: bỏ qua để không chặn mở file.
  }
}

function readLocalDraft(fileName: string): DraftPayload | null {
  try {
    const raw = localStorage.getItem(draftKey(fileName))
    if (!raw) return null
    const payload = JSON.parse(raw) as DraftPayload
    if (!payload.edits || payload.edits.length === 0) {
      localStorage.removeItem(draftKey(fileName))
      return null
    }
    return payload
  } catch {
    return null
  }
}

function clearDraft(): void {
  const active = session.value
  if (!active) return
  try {
    localStorage.removeItem(draftKey(active.fileName))
  } catch {
    // Bỏ qua.
  }
  // Bản nháp đã xuất xong — xoá luôn bản sao trên server (fire-and-forget).
  void request(`/api/summary/${active.sessionId}/draft`, { method: 'DELETE' }).catch(() => {})
}

function onGlobalKeydown(event: KeyboardEvent): void {
  const target = event.target as HTMLElement | null
  const typing =
    !!target &&
    (target.tagName === 'INPUT' ||
      target.tagName === 'TEXTAREA' ||
      target.tagName === 'SELECT' ||
      target.isContentEditable)

  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
    event.preventDefault()
    if (session.value) void openPreview()
    return
  }

  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') {
    event.preventDefault()
    if (!typing) undoEdits()
    return
  }

  if ((event.ctrlKey || event.metaKey) && (event.key.toLowerCase() === 'y' || (event.shiftKey && event.key.toLowerCase() === 'z'))) {
    event.preventDefault()
    if (!typing) redoEdits()
    return
  }

  if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
    event.preventDefault()
    if (session.value && scopeReady.value) void openFilteredPreview()
    return
  }

  // Nhóm I: Ctrl+M bật/tắt phiên đọc — bấm nút 🎶 hoặc nói thay gõ.
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'm') {
    event.preventDefault()
    toggleVoice()
    return
  }

  if (event.key === 'Escape') {
    if (editingSummaryRow.value !== null) {
      closeSummaryEditor()
      return
    }
    if (previewOpen.value) {
      previewOpen.value = false
      return
    }
  }

  if (typing) return

  if (event.key === '/') {
    event.preventDefault()
    searchInput.value?.focus()
    return
  }

  if (event.key === 'ArrowLeft' && session.value && page.value > 1) {
    event.preventDefault()
    goToPage(page.value - 1)
  }

  if (event.key === 'ArrowRight' && session.value && page.value < totalPages.value) {
    event.preventDefault()
    goToPage(page.value + 1)
  }
}

watch(edits, () => {
  if (!session.value) return
  if (autosaveTimer) window.clearTimeout(autosaveTimer)
  autosaveTimer = window.setTimeout(saveDraft, 700)
  // E1: sau khi localStorage đã lưu, đẩy thêm bản nháp lên server (8s lặng).
  scheduleServerDraftSync()
})

watch(onlySelectedRows, () => {
  if (previewOpen.value && previewMode.value === 'full') {
    void openPreview()
  }
})

watch([year, yearFrom, yearTo, type, onlyExisting, onlyMissingSheetNumber, onlyMissingDocumentNumber, onlyMissingNumber, onlyNeedsNormalization, missingSheetNumberFirst], () => {
  if (!session.value) return
  if (filterTimer) window.clearTimeout(filterTimer)
  filterTimer = window.setTimeout(() => {
    page.value = 1
    void fetchRows()
    if (previewOpen.value && exportScope.value === 'filtered') {
      void openFilteredPreview()
    }
  }, 300)
})

watch(search, () => {
  if (!session.value) return
  if (searchTimer) window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(() => {
    page.value = 1
    void fetchRows()
    if (previewOpen.value && exportScope.value === 'filtered') {
      void openFilteredPreview()
    }
  }, 350)
})

watch([numberMode, numberFrom, numberTo, singleNumber], () => {
  if (!session.value) return
  if (filterTimer) window.clearTimeout(filterTimer)
  filterTimer = window.setTimeout(() => {
    page.value = 1
    void fetchRows()
    if (previewOpen.value && exportScope.value === 'filtered') {
      void openFilteredPreview()
    }
  }, 350)
})

watch(pageSize, () => {
  if (!session.value) return
  page.value = 1
  void fetchRows()
})

function broadcastBulkChanges(changes: SummaryChangeRequest[]): void {
  if (!session.value || changes.length === 0) return
  void request(`/api/summary/${session.value.sessionId}/changes`, {
    method: 'POST',
    body: changes,
  }).catch(() => {})
}

// Lắng nghe con trỏ ô của đồng đội trong phòng
watch(lastSummaryCellFocus, (evt) => {
  if (!evt || evt.sessionId !== session.value?.sessionId || evt.actor === myActorName.value) return
  // Xoá vị trí cũ của actor này
  for (const [key, val] of remoteFocusedCells.value.entries()) {
    if (val.actor === evt.actor) remoteFocusedCells.value.delete(key)
  }
  // Nếu có ô được chọn
  if (evt.column) {
    remoteFocusedCells.value.set(`${evt.rowNumber}:${evt.column}`, {
      actor: evt.actor,
      at: Date.now(),
    })
  }
})

// Lắng nghe gõ chữ realtime
watch(lastSummaryCellTyping, (evt) => {
  if (!evt || evt.sessionId !== session.value?.sessionId || evt.actor === myActorName.value) return
  remoteTypingCells.value.set(`${evt.rowNumber}:${evt.column}`, {
    actor: evt.actor,
    text: evt.text,
    at: Date.now(),
  })
})

// Lắng nghe ô được lưu / commit
watch(lastSummaryCellChanged, (evt) => {
  if (!evt || evt.sessionId !== session.value?.sessionId || evt.actor === myActorName.value) return
  const targetRow = rows.value.find((r) => r.rowNumber === evt.rowNumber)
  const existing = edits.value.get(evt.rowNumber)
  const rowRef = targetRow ?? existing?.row
  if (rowRef) {
    const next = new Map(edits.value)
    next.set(evt.rowNumber, {
      row: rowRef,
      items: evt.items,
      summaryText: evt.summaryText,
      sheetNumber: evt.sheetNumber,
      documentNumber: evt.documentNumber,
      numberLabel: evt.numberLabel,
    })
    edits.value = next
  }
  triggerRowFlash(evt.rowNumber)
  // Xoá trạng thái gõ của ô đó
  remoteTypingCells.value.delete(`${evt.rowNumber}:sheetNumber`)
  remoteTypingCells.value.delete(`${evt.rowNumber}:documentNumber`)
  remoteTypingCells.value.delete(`${evt.rowNumber}:numberLabel`)
  remoteTypingCells.value.delete(`${evt.rowNumber}:summary`)
})

// Lắng nghe áp dụng hàng loạt từ đồng đội
watch(lastSummaryBulkChanged, (evt) => {
  if (!evt || evt.sessionId !== session.value?.sessionId || evt.actor === myActorName.value) return
  void fetchSharedEdits(evt.sessionId)
  toast.info('Đồng đội cập nhật', `${evt.actor} vừa áp dụng thay đổi cho ${formatNumber(evt.count)} dòng.`)
})

// Lắng nghe admin đổi/tải file làm việc chung
watch(lastTeamSessionChanged, async (evt) => {
  if (!evt) return
  if (evt.sessionId) {
    activeTeamSession.value = {
      hasActiveSession: true,
      sessionId: evt.sessionId,
      fileName: evt.fileName,
      uploadedBy: evt.uploadedBy,
      uploadedAt: evt.uploadedAt,
      rowCount: evt.rowCount,
      editCount: evt.editCount,
    }
    if (session.value?.sessionId !== evt.sessionId) {
      toast.info(
        'File làm chung mới',
        `${evt.uploadedBy} vừa mở file "${evt.fileName}" cho cả phòng cùng làm. Đang tự nạp...`,
      )
      await restoreFromServer(evt.sessionId)
    }
  } else {
    activeTeamSession.value = null
  }
})

let presenceCleanTimer: number | undefined

onMounted(() => {
  window.addEventListener('keydown', onGlobalKeydown)
  window.addEventListener('keydown', onSpeedKeydown)
  // E1: chuyển tab / thu nhỏ cửa sổ — đẩy bản nháp lên server ngay.
  document.addEventListener('visibilitychange', onVisibilityChange)
  // Dọn dẹp con trỏ và typing rác nếu đồng đội đóng tab
  presenceCleanTimer = window.setInterval(() => {
    const now = Date.now()
    for (const [k, v] of remoteFocusedCells.value.entries()) {
      if (now - v.at > 10000) remoteFocusedCells.value.delete(k)
    }
    for (const [k, v] of remoteTypingCells.value.entries()) {
      if (now - v.at > 6000) remoteTypingCells.value.delete(k)
    }
  }, 3000)
  void resumeLastSession().then(() => {
    if (!session.value) loadResumeCandidates()
  })
})

function onVisibilityChange(): void {
  if (document.visibilityState === 'hidden') {
    void syncServerDraft()
  }
}

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onGlobalKeydown)
  window.removeEventListener('keydown', onSpeedKeydown)
  document.removeEventListener('visibilitychange', onVisibilityChange)
  clearTeamEditingState()
  if (presenceCleanTimer) window.clearInterval(presenceCleanTimer)
  if (searchTimer) window.clearTimeout(searchTimer)
  if (filterTimer) window.clearTimeout(filterTimer)
  if (autosaveTimer) window.clearTimeout(autosaveTimer)
  if (serverDraftTimer) window.clearTimeout(serverDraftTimer)
  // Rời trang — cố đẩy nốt bản nháp lên server (fire-and-forget).
  void syncServerDraft()
  if (session.value) {
    saveDraft()
    leaveSummarySession(session.value.sessionId, myActorName.value ?? 'Quản trị')
    // KHÔNG xoá phiên trên server khi rời trang: F5/mất kết nối vẫn khôi phục được.
    // Phiên tự hết hạn sau 12 giờ (CleanupExpired ở backend).
  }
})
</script>

<template>
  <div class="stack summary-page" style="gap: 18px">
    <section v-if="!session" class="card summary-upload">
      <div class="card-body stack" style="gap: 16px">
        <div class="row" style="gap: 12px; align-items: flex-start">
          <span class="summary-step"><FilePenLine :size="20" /></span>
          <div>
            <h1 class="page-title" style="font-size: 20px">Bổ sung trích yếu và số tờ</h1>
            <p class="page-subtitle" style="margin-top: 4px">
              Chọn workbook, sửa trích yếu hoặc bổ sung số tờ còn thiếu, rồi xuất lại đúng file
              gốc. Mở đầu câu hồ sơ đăng ký kết hôn sẽ tự sửa đúng mẫu khi xuất.
            </p>
          </div>
        </div>

        <FilePicker
          v-model="files"
          :multiple="false"
          hint="Chọn workbook có sheet ML và cột Trích yếu nội dung"
        />

        <div v-if="resumeCandidates.length > 0" class="summary-resume card">
          <div class="card-header">
            <div>
              <div class="card-title">Phiên đang dở</div>
              <div class="card-subtitle">
                F5 hoặc mất kết nối cũng không mất — mở lại tại đây, không cần import.
              </div>
            </div>
          </div>
          <div class="card-body stack" style="gap: 8px">
            <div
              v-for="candidate in resumeCandidates"
              :key="candidate.sessionId"
              class="row-between"
              style="padding: 8px 10px; border: 1px solid var(--border); border-radius: 10px"
            >
              <div style="min-width: 0">
                <div class="truncate"><strong>{{ candidate.fileName }}</strong></div>
                <div class="hint">
                  {{ formatNumber(candidate.rowCount) }} hồ sơ · {{ new Date(candidate.createdAt).toLocaleString('vi-VN') }}
                </div>
              </div>
              <button
                class="btn btn-secondary btn-sm"
                type="button"
                :disabled="resuming"
                @click="openResumeCandidate(candidate)"
              >
                {{ resuming ? 'Đang mở...' : 'Mở lại' }}
              </button>
            </div>
          </div>
        </div>

        <div class="alert alert-info summary-note">
          <Info :size="17" />
          <div class="grow">
            <strong>Không đụng chạm phần còn lại</strong>
            <div>
              Toàn bộ sheet, công thức, định dạng, hình ảnh, ghi chú và macro được giữ nguyên.
              Chỉ các ô trích yếu và ô số tờ cần sửa mới được ghi lại.
            </div>
          </div>
        </div>

        <div v-if="inspectError" class="alert alert-danger">
          <TriangleExclamationPoint :size="17" />
          <div class="grow">{{ inspectError }}</div>
        </div>

        <div class="row-between wrap">
          <span class="hint">
            Hỗ trợ .xlsx, .xlsm, .xltx, .xltm. File mẫu 22.000 dòng vẫn xử lý bình thường.
          </span>
          <button
            class="btn btn-primary"
            type="button"
            :disabled="files.length === 0 || inspecting"
            @click="inspect"
          >
            <FileSpreadsheet :size="16" />
            {{ inspecting ? 'Đang đọc workbook...' : 'Đọc workbook' }}
          </button>
        </div>
      </div>
    </section>

    <template v-else>
      <!-- Banner phiên làm việc chung toàn phòng (Google Sheets multiplayer) -->
      <div v-if="activeTeamSession && activeTeamSession.sessionId === session.sessionId" class="team-session-banner">
        <div class="team-session-banner-main">
          <div class="team-live-indicator">
            <span class="live-dot-pulse" />
            <Radio :size="16" />
            <span>PHIÊN LÀM VIỆC CHUNG CỦA PHÒNG</span>
          </div>
          <div class="team-session-desc">
            File <strong>{{ activeTeamSession.fileName }}</strong> do <strong>{{ activeTeamSession.uploadedBy }}</strong> chia sẻ. Mọi người trong phòng đang thao tác chung trên file này trực tiếp như Google Sheets — ai sửa ô nào cả phòng đều thấy ngay!
          </div>
        </div>
        <div class="team-session-banner-actions">
          <div v-if="roomCollaborators.length > 0" class="team-session-collaborators">
            <span class="hint" style="font-size: 12px">Cùng mở:</span>
            <div class="avatar-group">
              <span
                v-for="user in roomCollaborators"
                :key="user.actor"
                class="tag-avatar"
                :title="`${user.actor} đang xem`"
                :style="renderAvatar(user.actor).style"
              >
                <img v-if="renderAvatar(user.actor).imageUrl" :src="renderAvatar(user.actor).imageUrl" class="avatar-img" />
                <template v-else>{{ renderAvatar(user.actor).initials }}</template>
              </span>
            </div>
          </div>
          <button
            class="btn btn-primary btn-sm"
            type="button"
            title="Nạp trực tiếp toàn bộ dữ liệu file này vào Kho hồ sơ SQL Server"
            @click="directSyncModalOpen = true"
          >
            <Database :size="14" />
            Nạp thẳng vào Kho hồ sơ
          </button>
          <button
            v-if="isAdmin"
            class="btn btn-ghost btn-sm"
            type="button"
            :disabled="settingActiveSession"
            title="Dừng phiên làm việc chung cho cả phòng"
            @click="toggleActiveTeamSession"
          >
            Dừng chia sẻ file này
          </button>
        </div>
      </div>

      <section class="card summary-session">
        <div class="card-body row-between wrap" style="gap: 14px">
          <div class="row" style="gap: 12px; min-width: 0">
            <span class="summary-step"><FileSpreadsheet :size="20" /></span>
            <div style="min-width: 0">
              <div class="row wrap" style="gap: 8px">
                <strong class="truncate" style="max-width: 420px">{{ session.fileName }}</strong>
                <span class="badge badge-brand">{{ session.sheetName }}!{{ session.summaryColumn }}</span>
                <span class="badge badge-neutral">Số tờ: cột {{ session.sheetNumberColumn }}</span>
                <span class="badge badge-neutral">{{ formatBytes(session.fileSizeBytes) }}</span>
              </div>
              <div class="hint" style="margin-top: 4px">
                {{ formatNumber(session.rowCount) }} hồ sơ · {{ formatNumber(session.catalog.length) }} loại giấy tờ ·
                {{ formatNumber(session.missingSheetNumberCount) }} dòng thiếu số tờ
              </div>
              <div class="hint" style="margin-top: 2px">
                Mở đầu câu trích yếu sai sẽ tự sửa về đúng mẫu khi xuất file, không cần gõ tay.
              </div>
            </div>
          </div>

          <div class="row wrap">
            <button
              v-if="isAdmin && (!activeTeamSession || activeTeamSession.sessionId !== session.sessionId)"
              class="btn btn-secondary btn-sm"
              type="button"
              :disabled="settingActiveSession"
              title="Chia sẻ file này để toàn bộ anh em khi mở app sẽ vào chung một file"
              @click="toggleActiveTeamSession"
            >
              <Users :size="14" />
              Đặt làm file làm chung
            </button>
            <button
              v-if="!activeTeamSession || activeTeamSession.sessionId !== session.sessionId"
              class="btn btn-secondary btn-sm"
              type="button"
              title="Nạp dữ liệu file này trực tiếp vào Kho hồ sơ SQL Server"
              @click="directSyncModalOpen = true"
            >
              <Database :size="14" />
              Nạp vào Kho
            </button>
            <span v-if="draftStatus === 'saved' && draftSavedAt" class="badge badge-info" title="Bản nháp tự lưu trên máy này">
              <Clock :size="13" />
              Nháp {{ draftSavedAt.toLocaleTimeString('vi-VN') }}
            </span>
            <button class="btn btn-secondary btn-sm" type="button" @click="closeSession">
              <X :size="14" />
              Đóng workbook
            </button>
          </div>
        </div>

        <div v-if="historyEntries.length > 0" class="summary-history">
          <details>
            <summary class="hint">Lưu lại quá trình ({{ historyEntries.length }} lượt)</summary>
            <div class="stack" style="gap: 6px; margin-top: 8px">
              <div v-for="(entry, index) in historyEntries" :key="`${entry.at}-${index}`" class="hint">
                {{ new Date(entry.at).toLocaleString('vi-VN') }} · <strong>{{ entry.action }}</strong> ·
                {{ entry.detail }}
              </div>
            </div>
          </details>
        </div>
      </section>

      <div class="summary-guide">
        <div class="summary-guide-step">
          <span class="summary-guide-index">1</span>
          <div>
            <strong>Chọn phạm vi</strong>
            <span>Lọc năm, loại, số hồ sơ hoặc dòng còn thiếu.</span>
          </div>
        </div>
        <div class="summary-guide-step">
          <span class="summary-guide-index">2</span>
          <div>
            <strong>Sửa dữ liệu</strong>
            <span>Sửa trích yếu, bổ sung số tờ, gán giấy tờ nhiều dòng cùng lúc.</span>
          </div>
        </div>
        <div class="summary-guide-step">
          <span class="summary-guide-index">3</span>
          <div>
            <strong>Xem trước</strong>
            <span>Đối chiếu file gốc với file sẽ xuất.</span>
          </div>
        </div>
        <div class="summary-guide-step">
          <span class="summary-guide-index">4</span>
          <div>
            <strong>Xuất file</strong>
            <span>Giữ nguyên định dạng gốc, chỉ ghi ô đã sửa.</span>
          </div>
        </div>
      </div>

      <div class="grid grid-4 summary-stats">
        <div class="card summary-stat">
          <div class="card-body">
            <span class="summary-stat-label">Hồ sơ</span>
            <strong class="summary-stat-value">{{ formatNumber(session.rowCount) }}</strong>
            <span class="hint">Dòng dữ liệu đọc được</span>
          </div>
        </div>
        <div class="card summary-stat">
          <div class="card-body">
            <span class="summary-stat-label">Thiếu số tờ</span>
            <strong class="summary-stat-value">{{ formatNumber(session.missingSheetNumberCount) }}</strong>
            <span class="hint">Cột {{ session.sheetNumberColumn }} đang trống</span>
          </div>
        </div>
        <div class="card summary-stat">
          <div class="card-body">
            <span class="summary-stat-label">Sai mở đầu câu</span>
            <strong class="summary-stat-value">{{ formatNumber(session.normalizableCount) }}</strong>
            <span class="hint">Sẽ tự sửa khi xuất</span>
          </div>
        </div>
        <div class="card summary-stat">
          <div class="card-body">
            <span class="summary-stat-label">Đang chờ xuất</span>
            <strong class="summary-stat-value">{{ formatNumber(changedCount) }}</strong>
            <span class="hint">Dòng đã sửa trên máy này</span>
          </div>
        </div>
      </div>

      <section class="card">
        <div class="card-header summary-filter-header">
          <div>
            <div class="card-title">Sửa trích yếu và số tờ</div>
            <div class="card-subtitle">
              Sửa ô số tờ ngay tại chỗ, bấm “Sửa toàn bộ” để gõ lại câu, hoặc tích đầu dòng để gán
              giấy tờ cho nhiều dòng cùng lúc.
            </div>
          </div>
          <div class="row" style="gap: 8px; align-items: center">
            <button
              class="btn btn-sm"
              :class="voiceListening ? 'btn-danger voice-mic-on' : 'btn-secondary'"
              type="button"
              :disabled="!voiceSupported"
              :title="
                voiceSupported
                  ? 'Phiên đọc giọng nói: nói “kết hôn — căn cước, hộ khẩu, hai ảnh” rồi “xong”. Ctrl+M để bật/tắt'
                  : 'Đọc giọng nói cần Chrome hoặc Edge (Web Speech API)'
              "
              @click="toggleVoice"
            >
              <Mic :size="14" />
              {{ voiceListening ? 'Đang nghe…' : 'Đọc giọng nói' }}
            </button>
            <button class="btn btn-ghost btn-sm" type="button" :disabled="loadingRows" @click="fetchRows">
              <RefreshCw :size="14" />
              Làm mới
            </button>
          </div>
        </div>

        <div v-if="voiceListening" class="voice-bar" role="status" aria-live="polite">
          <span class="voice-dot" />
          <span class="voice-live">
            {{ voiceInterim || 'Đang nghe… nói “kết hôn — căn cước, hộ khẩu, hai ảnh”, hoặc “xong”, “kế tiếp”, “bộ bảy”, “lọc kết hôn 2025”' }}
          </span>
          <span v-if="voiceLastHeard" class="voice-heard" :title="`Câu vừa nghe: ${voiceLastHeard}`">
            Đã nghe: {{ voiceLastHeard }}
          </span>
          <button class="btn btn-ghost btn-sm" type="button" @click="toggleVoice">Tắt mic</button>
        </div>

        <div class="card-body stack" style="gap: 14px">
          <div class="summary-filters">
            <label class="field">
              <span class="label">Năm</span>
              <AppSelect
                :model-value="year === '' ? '' : String(year)"
                :options="yearOptions"
                placeholder="Tất cả các năm"
                @update:model-value="(value) => { year = value === '' ? '' : Number(value) }"
              />
            </label>

            <label class="field">
              <span class="label">Loại hồ sơ</span>
              <AppSelect
                v-model="type"
                :options="typeOptions"
                placeholder="Tất cả loại"
              />
            </label>

            <label class="field summary-search">
              <span class="label">Tìm trong trích yếu</span>
              <span class="search-box">
                <Search :size="15" />
                <input
                  ref="searchInput"
                  v-model="search"
                  class="input"
                  placeholder="Số hồ sơ, hồ sơ số, số tờ, tên người, trích yếu... (phím /)"
                />
              </span>
            </label>

            <label class="checkbox summary-existing">
              <input v-model="onlyExisting" type="checkbox" />
              <span class="checkbox-text">
                <span class="checkbox-title">Chỉ dòng đã có danh mục</span>
                <span class="checkbox-desc">{{ formatNumber(session.existingListCount) }} dòng</span>
              </span>
            </label>
          </div>

          <details class="summary-advanced">
            <summary class="summary-advanced-title">Lọc nâng cao: theo số hồ sơ và khoảng năm</summary>
            <div class="summary-number-filters">
            <label class="field">
              <span class="label">Lọc theo số hồ sơ</span>
              <AppSelect
                v-model="numberMode"
                :options="numberModeOptions"
                :searchable="false"
              />
            </label>

            <template v-if="numberMode === 'range'">
              <label class="field">
                <span class="label">Từ số</span>
                <input v-model.number="numberFrom" class="input" type="number" min="1" placeholder="VD: 36" />
              </label>
              <label class="field">
                <span class="label">Đến số</span>
                <input v-model.number="numberTo" class="input" type="number" min="1" placeholder="VD: 40" />
              </label>
            </template>

            <label v-else-if="numberMode === 'single'" class="field">
              <span class="label">Số hồ sơ</span>
              <input v-model.number="singleNumber" class="input" type="number" min="1" placeholder="VD: 38" />
            </label>

            <label class="field">
              <span class="label">Năm từ</span>
              <input v-model.number="yearFrom" class="input" type="number" min="1900" max="2200" placeholder="VD: 2015" />
            </label>

            <label class="field">
              <span class="label">Năm đến</span>
              <input v-model.number="yearTo" class="input" type="number" min="1900" max="2200" placeholder="VD: 2024" />
            </label>

            <div class="summary-number-hint hint">
              Số hồ sơ lấy từ phần đầu cột {{ session.documentNumberColumn }} (Hồ sơ số) hoặc cột
              {{ session.numberColumn }} (ví dụ “36A” tính là 36). Bản lọc chỉ giữ sheet dữ liệu và
              các hồ sơ trong phạm vi.
            </div>
            </div>
          </details>

          <div class="summary-missing-filters">
            <label class="checkbox">
              <input v-model="onlyMissingSheetNumber" type="checkbox" />
              <span class="checkbox-text">
                <span class="checkbox-title">Chỉ dòng thiếu số tờ</span>
                <span class="checkbox-desc">{{ formatNumber(session.missingSheetNumberCount) }} dòng cột {{ session.sheetNumberColumn }}</span>
              </span>
            </label>

            <label class="checkbox">
              <input v-model="onlyMissingDocumentNumber" type="checkbox" />
              <span class="checkbox-text">
                <span class="checkbox-title">Chỉ dòng thiếu hồ sơ số</span>
                <span class="checkbox-desc">{{ formatNumber(session.missingDocumentNumberCount) }} dòng cột {{ session.documentNumberColumn }}</span>
              </span>
            </label>

            <label class="checkbox">
              <input v-model="onlyMissingNumber" type="checkbox" />
              <span class="checkbox-text">
                <span class="checkbox-title">Chỉ dòng thiếu số hồ sơ</span>
                <span class="checkbox-desc">{{ formatNumber(session.missingNumberCount) }} dòng</span>
              </span>
            </label>

            <label class="checkbox">
              <input v-model="onlyNeedsNormalization" type="checkbox" />
              <span class="checkbox-text">
                <span class="checkbox-title">Chỉ dòng sai mở đầu câu</span>
                <span class="checkbox-desc">{{ formatNumber(session.normalizableCount) }} dòng</span>
              </span>
            </label>

            <label class="checkbox">
              <input v-model="missingSheetNumberFirst" type="checkbox" />
              <span class="checkbox-text">
                <span class="checkbox-title">Ưu tiên dòng thiếu số tờ</span>
                <span class="checkbox-desc">Đưa lên đầu danh sách</span>
              </span>
            </label>
          </div>

          <div class="summary-scope">
            <div class="summary-scope-info">
              <Info :size="15" />
              <span>
                Đang xem: <strong>{{ scopeLabel }}</strong>
                <template v-if="scopeReady">
                  · <strong>{{ formatNumber(total) }}</strong> hồ sơ
                </template>
                <template v-else> · nhập số hồ sơ để tiếp tục</template>
              </span>
            </div>
            <div class="row wrap" style="gap: 8px">
              <button
                class="btn btn-secondary btn-sm"
                type="button"
                :disabled="exportingIssues"
                @click="exportIssues"
              >
                <Download :size="14" />
                {{ exportingIssues ? 'Đang lập danh sách...' : 'Xuất danh sách thiếu' }}
              </button>
              <button
                class="btn btn-secondary btn-sm"
                type="button"
                :disabled="!scopeReady || total === 0 || loadingRows"
                @click="openFilteredPreview"
              >
                <Download :size="14" />
                Xuất bản lọc
              </button>
              <button
                class="btn btn-primary btn-sm"
                type="button"
                title="Xuất ngay theo phạm vi đang lọc với tên file đã nhớ từ lần trước (không cần xem trước)"
                :disabled="!scopeReady || total === 0 || loadingRows || filteredExporting"
                @click="quickExportFiltered"
              >
                <Download :size="14" />
                {{ filteredExporting ? 'Đang xuất...' : 'Xuất nhanh' }}
              </button>
              <button
                class="btn btn-accent btn-sm"
                type="button"
                :disabled="loadingRows || exporting || total === 0"
                @click="openPreview"
              >
                <FilePenLine :size="14" />
                Xem trước rồi xuất toàn bộ
              </button>
            </div>
          </div>

          <div v-if="rows.length > 0 && !loadingRows" class="summary-missing-bar">
            <span class="badge badge-brand">Áp nhanh</span>
            <SummaryItemPicker
              v-model="bulkItems"
              :catalog="session.catalog"
              :counts="catalogCounts"
              placeholder="Bộ giấy tờ gán cho mọi dòng còn thiếu..."
              @custom-added="persistCatalogCustom"
            />
            <button
              class="btn btn-primary btn-sm"
              type="button"
              :disabled="bulkApplying || applyingMissing || !scopeReady"
              title="Gán bộ đang chọn cho MỌI dòng còn thiếu danh mục trong phạm vi đang lọc — duyệt hết các trang, không cần tích từng dòng"
              @click="applyToMissingInScope"
            >
              <Check :size="14" />
              {{ applyingMissing ? 'Đang áp...' : 'Áp dụng cho dòng lọc còn thiếu' }}
            </button>
          </div>

          <div v-if="rowsError" class="alert alert-danger">
            <TriangleExclamationPoint :size="17" />
            <div class="grow">{{ rowsError }}</div>
            <button class="btn btn-secondary btn-sm" type="button" @click="fetchRows">Thử lại</button>
          </div>

          <div v-if="loadingRows" class="loading-block" style="padding: 24px 0">
            <span class="spinner" />
            <span>Đang tải hồ sơ...</span>
          </div>

          <div v-else-if="rows.length === 0" class="table-wrap">
            <EmptyState
              title="Không có hồ sơ phù hợp"
              description="Thử bỏ bộ lọc năm, loại hồ sơ, số hồ sơ hoặc từ khoá tìm kiếm."
            />
          </div>

          <template v-else>
            <div class="summary-table-hint hint">
              Tích ô ở đầu dòng để sửa nhiều dòng cùng lúc; muốn chỉ xuất đúng các dòng đã tích, bật
              “Chỉ các dòng đã chọn” ở bước xem trước.
            </div>
            <div v-if="selected.size > 0" class="summary-bulkbar">
              <div class="row wrap" style="gap: 8px; align-items: center">
                <span class="badge badge-brand">{{ formatNumber(selected.size) }} dòng đã chọn</span>
                <SummaryItemPicker
                  v-model="bulkItems"
                  :catalog="session.catalog"
                  :counts="catalogCounts"
                  placeholder="Giấy tờ áp dụng hàng loạt..."
                  @custom-added="persistCatalogCustom"
                />
                <button class="btn btn-primary btn-sm" type="button" :disabled="bulkApplying" @click="applyBulk">
                  <Check :size="14" />
                  Áp dụng cho dòng đã chọn
                </button>
                <button
                  class="btn btn-secondary btn-sm"
                  type="button"
                  :disabled="bulkApplying"
                  title="7 giấy tờ luôn xuất hiện cùng nhau ở 98% hồ sơ đăng ký kết hôn có danh sách — đánh dấu từ dữ liệu thật"
                  @click="bulkApplyStandardSeven"
                >
                  <Star :size="14" />
                  Bộ 7 chuẩn
                </button>
                <button
                  v-if="selectedNormalizableCount > 0"
                  class="btn btn-secondary btn-sm"
                  type="button"
                  @click="normalizeSelected"
                >
                  <CircleCheck :size="14" />
                  Sửa mở đầu đúng mẫu ({{ formatNumber(selectedNormalizableCount) }})
                </button>
              </div>
              <button class="btn btn-ghost btn-sm" type="button" @click="clearSelection">
                <X :size="14" />
                Bỏ chọn
              </button>
            </div>

            <div class="table-wrap">
              <table class="table table-hover summary-table">
                <thead>
                  <tr>
                    <th style="width: 42px">
                      <input
                        type="checkbox"
                        :checked="allPageSelected"
                        aria-label="Chọn tất cả dòng trong trang"
                        @change="toggleSelectPage"
                      />
                    </th>
                    <th style="width: 116px">Hồ sơ</th>
                    <th style="width: 74px">Năm</th>
                    <th style="width: 108px">Số tờ ({{ session.sheetNumberColumn }})</th>
                    <th style="width: 108px">Hồ sơ số ({{ session.documentNumberColumn }})</th>
                    <th style="width: 96px">Số ({{ session.numberColumn ?? 'B' }})</th>
                    <th>Trích yếu</th>
                    <th style="width: 320px">Giấy tờ trong ngoặc</th>
                  </tr>
                </thead>
                <tbody>
                  <template v-for="row in rows" :key="row.rowNumber">
                    <tr
                      :class="{
                        'is-edited': edits.has(row.rowNumber),
                        'is-team-marked': teamMarkRows.has(row.rowNumber),
                        'is-remote-flashing': flashingRows.has(row.rowNumber),
                      }"
                      :style="
                        teamMarkRows.has(row.rowNumber)
                          ? ({ '--mark-color': colorForName(teamMarkRows.get(row.rowNumber)!) } as Record<string, string>)
                          : undefined
                      "
                    >
                      <td>
                        <input
                          type="checkbox"
                          :checked="selected.has(row.rowNumber)"
                          :aria-label="`Chọn dòng ${row.rowNumber}`"
                          @change="toggleSelect(row.rowNumber)"
                        />
                      </td>
                      <td>
                        <div class="summary-number">{{ row.numberLabel || '—' }}</div>
                        <div v-if="row.documentNumber" class="hint truncate" :title="row.documentNumber">
                          HS {{ row.documentNumber }}
                        </div>
                        <div class="hint">Dòng {{ row.rowNumber }}</div>
                        <div
                          v-if="teamMarkRows.get(row.rowNumber)"
                          class="team-lock-tag"
                          :title="`${teamMarkRows.get(row.rowNumber)} đang sửa dòng này`"
                        >
                          <span
                            class="tag-avatar"
                            :style="renderAvatar(teamMarkRows.get(row.rowNumber)!).style"
                          >
                            <img
                              v-if="renderAvatar(teamMarkRows.get(row.rowNumber)!).imageUrl"
                              :src="renderAvatar(teamMarkRows.get(row.rowNumber)!).imageUrl"
                              alt=""
                              class="avatar-img"
                              loading="lazy"
                            />
                            <template v-else>{{ renderAvatar(teamMarkRows.get(row.rowNumber)!).initials }}</template>
                          </span>
                          {{ teamMarkRows.get(row.rowNumber) }} đang nắn
                        </div>
                      </td>
                      <td>
                        <span class="badge badge-neutral">{{ row.year ?? '—' }}</span>
                        <span
                          v-if="multiSheet"
                          class="badge badge-neutral summary-sheet-badge"
                          :title="`Sheet ${sheetBadgeFor(row)} — ${sourceRowLabelFor(row)}`"
                        >
                          {{ sheetBadgeFor(row) }}
                        </span>
                      </td>
                      <td>
                        <div
                          class="cell-wrapper"
                          :class="{
                            'is-remote-focused': !!remoteFocusedCells.get(`${row.rowNumber}:sheetNumber`),
                          }"
                          :style="
                            remoteFocusedCells.get(`${row.rowNumber}:sheetNumber`)
                              ? ({ '--cursor-color': colorForName(remoteFocusedCells.get(`${row.rowNumber}:sheetNumber`)!.actor) } as Record<string, string>)
                              : undefined
                          "
                        >
                          <div
                            v-if="remoteFocusedCells.get(`${row.rowNumber}:sheetNumber`)"
                            class="cell-cursor-tag"
                            :style="{ backgroundColor: colorForName(remoteFocusedCells.get(`${row.rowNumber}:sheetNumber`)!.actor) }"
                          >
                            {{ remoteFocusedCells.get(`${row.rowNumber}:sheetNumber`)!.actor }}
                          </div>
                          <input
                            class="input summary-sheet-input"
                            :class="{ 'is-missing': !sheetFor(row).trim() }"
                            :value="remoteTypingCells.get(`${row.rowNumber}:sheetNumber`)?.text ?? sheetFor(row)"
                            placeholder="VD: 01-20"
                            :aria-label="`Số tờ dòng ${row.rowNumber}`"
                            @focus="onCellFocus(row, 'sheetNumber')"
                            @blur="onCellBlur(row, 'sheetNumber')"
                            @input="onCellInput(row, 'sheetNumber', $event)"
                            @change="onSheetChange(row, $event)"
                          />
                          <span v-if="!sheetFor(row).trim()" class="summary-missing-tag">Thiếu</span>
                        </div>
                      </td>
                      <td>
                        <div
                          class="cell-wrapper"
                          :class="{
                            'is-remote-focused': !!remoteFocusedCells.get(`${row.rowNumber}:documentNumber`),
                          }"
                          :style="
                            remoteFocusedCells.get(`${row.rowNumber}:documentNumber`)
                              ? ({ '--cursor-color': colorForName(remoteFocusedCells.get(`${row.rowNumber}:documentNumber`)!.actor) } as Record<string, string>)
                              : undefined
                          "
                        >
                          <div
                            v-if="remoteFocusedCells.get(`${row.rowNumber}:documentNumber`)"
                            class="cell-cursor-tag"
                            :style="{ backgroundColor: colorForName(remoteFocusedCells.get(`${row.rowNumber}:documentNumber`)!.actor) }"
                          >
                            {{ remoteFocusedCells.get(`${row.rowNumber}:documentNumber`)!.actor }}
                          </div>
                          <input
                            class="input summary-sheet-input"
                            :class="{ 'is-missing': !docFor(row).trim() }"
                            :value="remoteTypingCells.get(`${row.rowNumber}:documentNumber`)?.text ?? docFor(row)"
                            placeholder="VD: HS-01"
                            :aria-label="`Hồ sơ số dòng ${row.rowNumber}`"
                            @focus="onCellFocus(row, 'documentNumber')"
                            @blur="onCellBlur(row, 'documentNumber')"
                            @input="onCellInput(row, 'documentNumber', $event)"
                            @change="onDocChange(row, $event)"
                          />
                          <span v-if="!docFor(row).trim()" class="summary-missing-tag">Thiếu</span>
                        </div>
                      </td>
                      <td>
                        <div
                          class="cell-wrapper"
                          :class="{
                            'is-remote-focused': !!remoteFocusedCells.get(`${row.rowNumber}:numberLabel`),
                          }"
                          :style="
                            remoteFocusedCells.get(`${row.rowNumber}:numberLabel`)
                              ? ({ '--cursor-color': colorForName(remoteFocusedCells.get(`${row.rowNumber}:numberLabel`)!.actor) } as Record<string, string>)
                              : undefined
                          "
                        >
                          <div
                            v-if="remoteFocusedCells.get(`${row.rowNumber}:numberLabel`)"
                            class="cell-cursor-tag"
                            :style="{ backgroundColor: colorForName(remoteFocusedCells.get(`${row.rowNumber}:numberLabel`)!.actor) }"
                          >
                            {{ remoteFocusedCells.get(`${row.rowNumber}:numberLabel`)!.actor }}
                          </div>
                          <input
                            class="input summary-sheet-input"
                            :class="{ 'is-missing': !numFor(row).trim() }"
                            :value="remoteTypingCells.get(`${row.rowNumber}:numberLabel`)?.text ?? numFor(row)"
                            placeholder="VD: 36"
                            :aria-label="`Số hồ sơ dòng ${row.rowNumber}`"
                            @focus="onCellFocus(row, 'numberLabel')"
                            @blur="onCellBlur(row, 'numberLabel')"
                            @input="onCellInput(row, 'numberLabel', $event)"
                            @change="onNumChange(row, $event)"
                          />
                          <span v-if="!numFor(row).trim()" class="summary-missing-tag">Thiếu</span>
                        </div>
                      </td>
                      <td>
                        <div
                          class="summary-base is-editable"
                          :title="`${summaryFor(row)} — Bấm để sửa`"
                          role="button"
                          tabindex="0"
                          @click="openSummaryEditor(row)"
                          @keydown.enter="openSummaryEditor(row)"
                        >{{ summaryFor(row) }}</div>
                        <div class="summary-meta">
                          <span v-if="row.hasDocumentList" class="badge badge-info">
                            Đã có {{ itemsFor(row).length }} mục
                          </span>
                          <span
                            v-if="hasAliasParenthesis(row)"
                            class="badge badge-warning"
                            title="Ngoặc cuối không được nhận diện là danh mục giấy tờ. Nếu bổ sung, một ngoặc mới sẽ được thêm sau ngoặc này."
                          >
                            Ngoặc không phải danh mục
                          </span>
                          <span
                            v-if="row.isBrokenSummary"
                            class="badge badge-danger"
                            title="Ô trích yếu đang chứa chuỗi công thức lỗi #REF!. Phần này được giữ nguyên."
                          >
                            Ô lỗi #REF!
                          </span>
                          <span v-if="row.type" class="hint truncate" style="max-width: 240px">{{ row.type }}</span>
                          <span
                            v-if="row.needsNormalization"
                            class="badge badge-success"
                            :title="`Mở đầu câu sẽ tự sửa về đúng mẫu khi xuất. File gốc: ${row.summary}`"
                          >
                            <CircleCheck :size="12" />
                            Sẽ tự sửa mở đầu
                          </span>
                          <button
                            class="btn btn-ghost btn-sm summary-edit-summary"
                            type="button"
                            @click="openSummaryEditor(row)"
                          >
                            <FilePenLine :size="13" />
                            Sửa toàn bộ
                          </button>
                        </div>
                      </td>
                      <td>
                        <SummaryItemPicker
                          :model-value="itemsFor(row)"
                          :catalog="session.catalog"
                          :counts="catalogCounts"
                          :placeholder="row.hasDocumentList ? 'Sửa danh mục...' : 'Chọn giấy tờ...'"
                          @update:model-value="(items) => setRowItems(row, items)"
                          @custom-added="persistCatalogCustom"
                        />

                        <div class="quick-chips" title="Bấm 1 lần để thêm/gỡ mục — bấm C để chép như dòng trên">
                          <button
                            v-for="item in quickChipItems"
                            :key="item"
                            type="button"
                            class="quick-chip"
                            :class="{ 'is-on': hasQuickItem(row, item) }"
                            @click="toggleQuickItem(row, item)"
                          >
                            {{ item }}
                          </button>
                        </div>

                        <div v-if="itemsFor(row).length > 0" class="summary-chips" :title="itemsFor(row).join(', ')">
                          <span v-for="item in itemsFor(row).slice(0, 3)" :key="item" class="chip">
                            {{ item }}
                          </span>
                          <span v-if="itemsFor(row).length > 3" class="chip chip-more">
                            +{{ itemsFor(row).length - 3 }}
                          </span>
                        </div>
                      </td>
                    </tr>
                    <tr v-if="editingSummaryRow === row.rowNumber" class="summary-editor-row">
                      <td colspan="8">
                        <div class="summary-editor">
                          <div class="summary-editor-head">
                            <strong>Sửa toàn bộ trích yếu · dòng {{ row.rowNumber }}</strong>
                            <span class="hint">Hồ sơ {{ row.numberLabel || '—' }}</span>
                            <span
                              v-if="voiceLearnCandidate?.rowNumber === row.rowNumber"
                              class="badge badge-info"
                              title="Giọng nói vừa điền — nắn khác đi rồi lưu, app sẽ tự nhớ lỗi nghe cho lần sau"
                            >
                              <Mic :size="12" />
                              Điền từ giọng nói — nắn rồi lưu
                            </span>
                            <button
                              class="btn btn-ghost btn-sm"
                              type="button"
                              :title="autoNextRow ? 'Sau khi lưu sẽ tự mở dòng thiếu kế tiếp' : 'Sau khi lưu sẽ đóng editor'"
                              @click="toggleAutoNext"
                            >
                              {{ autoNextRow ? '⏭ Tự nhảy: bật' : 'Tự nhảy: tắt' }}
                            </button>
                          </div>
                          <div class="row wrap" style="gap: 6px; align-items: center">
                            <span class="hint" style="font-size: 12px">Siêu tốc:</span>
                            <button
                              class="quick-chip"
                              type="button"
                              title="Bấm phím C (đứng ngoài ô nhập) cũng chạy lệnh này"
                              @click="applyFromAbove(row)"
                            >
                              C — như dòng trên
                            </button>
                            <button
                              v-if="defaultSetForType(row)"
                              class="quick-chip"
                              type="button"
                              :title="`Bộ phổ biến nhất của loại ${row.type} (học từ dòng đang xem)`"
                              @click="applyDefaultForType(row)"
                            >
                              Bộ loại: {{ defaultSetForType(row)!.join(', ') }}
                            </button>
                          </div>
                          <div v-if="editingConflict" class="alert alert-warning" style="margin: 0">
                            <TriangleExclamationPoint :size="15" />
                            <div class="grow">
                              <strong>{{ editingConflict }}</strong> cũng đang mở dòng này. Ai lưu sau
                              sẽ ghi đè phần của người trước — nhắn nhau 1 câu trong khung chat nhóm
                              rồi mới nắn.
                            </div>
                          </div>
                          <div v-if="row.needsNormalization" class="alert alert-info" style="margin: 0">
                            <Info :size="15" />
                            <div class="grow">
                              Mở đầu câu này sẽ tự sửa về đúng mẫu
                              “Quyết định về việc cho phép công dân Việt Nam đăng ký kết hôn với
                              người nước ngoài của…” khi xuất file.
                            </div>
                          </div>
                          <textarea
                            v-model="summaryDraft"
                            class="input summary-editor-text"
                            rows="4"
                            placeholder="Nhập toàn bộ câu trích yếu..."
                          />
                          <div class="row wrap" style="gap: 8px; justify-content: flex-end">
                            <button class="btn btn-secondary btn-sm" type="button" @click="closeSummaryEditor">
                              Huỷ
                            </button>
                            <button
                              class="btn btn-primary btn-sm"
                              type="button"
                              :disabled="!summaryDraft.trim()"
                              @click="saveSummaryEditor(row)"
                            >
                              <CircleCheck :size="14" />
                              Lưu trích yếu
                            </button>
                          </div>
                        </div>
                      </td>
                    </tr>
                  </template>
                </tbody>
              </table>
            </div>
          </template>

          <div class="summary-pager">
            <span class="hint">
              {{ formatNumber(rangeStart) }}–{{ formatNumber(rangeEnd) }} / {{ formatNumber(total) }} hồ sơ
            </span>
            <div class="row wrap" style="gap: 8px; align-items: center">
              <label class="summary-page-size">
                <span class="hint">Mỗi trang</span>
                <AppSelect
                  :model-value="String(pageSize)"
                  :options="pageSizeOptions"
                  :searchable="false"
                  aria-label="Số dòng mỗi trang"
                  @update:model-value="(value) => { pageSize = Number(value) }"
                />
              </label>
              <button
                class="btn btn-secondary btn-sm"
                type="button"
                :disabled="page <= 1 || loadingRows"
                @click="goToPage(page - 1)"
              >
                <ArrowLeft :size="14" />
                Trước
              </button>
              <span class="hint">Trang {{ formatNumber(page) }} / {{ formatNumber(totalPages) }}</span>
              <button
                class="btn btn-secondary btn-sm"
                type="button"
                :disabled="page >= totalPages || loadingRows"
                @click="goToPage(page + 1)"
              >
                Sau
                <ArrowRight :size="14" />
              </button>
            </div>
          </div>

          <div class="summary-shortcuts hint">
            Phím tắt: <kbd>Ctrl</kbd> + <kbd>S</kbd> xem trước · <kbd>Ctrl</kbd> + <kbd>Enter</kbd>
            xuất bản lọc · <kbd>/</kbd> tìm kiếm · <kbd>←</kbd> <kbd>→</kbd> chuyển trang ·
            <kbd>Ctrl</kbd> + <kbd>Z</kbd> hoàn tác · <kbd>Ctrl</kbd> + <kbd>Y</kbd> làm lại ·
            <kbd>Esc</kbd> đóng.
          </div>
        </div>
      </section>

      <section v-if="yearReport.length > 0" class="card">
        <div class="card-header">
          <div>
            <div class="card-title">Báo cáo dữ liệu thiếu theo năm</div>
            <div class="card-subtitle">Giúp trưởng phòng ra quyết định xử lý — bấm năm để lọc ngay.</div>
          </div>
          <button class="btn btn-secondary btn-sm" type="button" :disabled="reportLoading" @click="exportYearReportCsv">
            <Download :size="14" />
            {{ reportLoading ? 'Đang lập...' : 'Xuất CSV' }}
          </button>
        </div>
        <div class="card-body">
          <div class="table-wrap">
            <table class="table table-hover summary-table">
              <thead>
                <tr>
                  <th>Năm</th>
                  <th>Tổng hồ sơ</th>
                  <th>Thiếu số tờ</th>
                  <th>Thiếu hồ sơ số</th>
                  <th>Thiếu số hồ sơ</th>
                  <th>Sai mở đầu</th>
                  <th style="width: 140px"></th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="item in yearReport" :key="item.year">
                  <td><strong>{{ item.year }}</strong></td>
                  <td>{{ formatNumber(item.total) }}</td>
                  <td>{{ item.missingSheetNumber ? formatNumber(item.missingSheetNumber) : '—' }}</td>
                  <td>{{ item.missingDocumentNumber ? formatNumber(item.missingDocumentNumber) : '—' }}</td>
                  <td>{{ item.missingNumber ? formatNumber(item.missingNumber) : '—' }}</td>
                  <td>{{ item.needsNormalization ? formatNumber(item.needsNormalization) : '—' }}</td>
                  <td>
                    <button
                      class="btn btn-secondary btn-sm"
                      type="button"
                      @click="year = item.year; page = 1; fetchRows()"
                    >
                      Lọc năm {{ item.year }}
                    </button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </section>

      <section class="card">
        <div class="card-header">
          <div>
            <div class="card-title">Xuất bản in theo năm</div>
            <div class="card-subtitle">
              Sinh sheet in BANG_IN_HANG_LOAT kèm macro lọc/in cho từng năm, dùng đúng trích yếu đã chuẩn hoá.
            </div>
          </div>
          <button class="btn btn-accent btn-sm" type="button" @click="openPrintDialog">
            <FileSpreadsheet :size="14" />
            Chuẩn bị bản in
          </button>
        </div>
        <div class="card-body">
          <div class="alert alert-info" style="margin: 0">
            <Info :size="15" />
            <div class="grow">
              Chọn năm, kiểm tra dữ liệu thiếu rồi tải về. Mỗi năm một file .xlsm (đóng gói .zip)
              hoặc gộp tất cả vào một .xlsm. Macro in/lọc được chèn sẵn.
            </div>
          </div>
        </div>
      </section>

      <div v-if="changedCount > 0" class="summary-actionbar">
        <div>
          <strong>{{ formatNumber(changedCount) }}</strong>
          <span> dòng đã sửa, chưa xuất</span>
        </div>
        <div class="row wrap" style="gap: 8px">
          <button class="btn btn-ghost btn-sm" type="button" :disabled="!canUndo" @click="undoEdits" title="Ctrl + Z">
            <ArrowLeft :size="14" />
            Hoàn tác
          </button>
          <button class="btn btn-ghost btn-sm" type="button" :disabled="!canRedo" @click="redoEdits" title="Ctrl + Y">
            <ArrowRight :size="14" />
            Làm lại
          </button>
          <button class="btn btn-ghost btn-sm" type="button" @click="resetEdits">
            <Eraser :size="14" />
            Bỏ thay đổi
          </button>
          <button
            class="btn btn-secondary"
            type="button"
            :disabled="!scopeReady || total === 0"
            @click="openFilteredPreview"
          >
            <Download :size="16" />
            Xuất bản lọc
          </button>
          <button
            v-if="selectedRowNumbers.length > 0"
            class="btn btn-secondary"
            type="button"
            @click="onlySelectedRows = true; openPreview()"
          >
            <FilePenLine :size="16" />
            Xem trước chỉ dòng đã chọn ({{ formatNumber(selectedRowNumbers.length) }})
          </button>
          <button class="btn btn-accent" type="button" @click="onlySelectedRows = false; openPreview()">
            <FilePenLine :size="16" />
            Xem trước rồi xuất toàn bộ
          </button>
        </div>
      </div>
    </template>

    <Teleport to="body">
      <Transition name="modal">
      <div v-if="previewOpen" class="modal-backdrop" @click.self="previewOpen = false">
        <div class="modal summary-preview-modal">
          <div class="modal-header">
            <div>
              <div class="card-title">
                {{ previewMode === 'filtered' ? 'Xem trước bản xuất lọc' : 'Kiểm tra trước khi xuất file' }}
              </div>
              <div class="card-subtitle">
                <template v-if="previewMode === 'filtered'">
                  Chỉ giữ sheet ML và các hồ sơ trong phạm vi hiện tại; xoá mọi sheet và hồ sơ còn lại.
                </template>
                <template v-else>
                  Bên trái là file gốc, bên phải là file sẽ tải về. Không có gì thay đổi trước khi bạn bấm Xuất.
                </template>
              </div>
            </div>
            <button class="btn btn-ghost btn-icon" type="button" aria-label="Đóng" @click="previewOpen = false">
              <X :size="18" />
            </button>
          </div>

          <div class="modal-body">
            <div v-if="previewLoading" class="loading-block" style="padding: 30px 0">
              <span class="spinner" />
              <span>Đang tạo bản xem trước...</span>
            </div>

            <div v-else-if="previewError" class="alert alert-danger">
              <TriangleExclamationPoint :size="17" />
              <div class="grow">{{ previewError }}</div>
            </div>

            <template v-else>
              <div class="summary-scope-picker">
                <div class="summary-preview-label">Phạm vi xuất</div>
                <div class="row wrap" style="gap: 8px">
                  <label class="radio-card" :class="{ selected: exportScope === 'all' }">
                    <input v-model="exportScope" value="all" type="radio" @change="onExportScopeChange" />
                    <span class="checkbox-text">
                      <span class="checkbox-title">Toàn bộ workbook</span>
                      <span class="checkbox-desc">Giữ mọi sheet; chuẩn hoá mọi dòng kết hôn.</span>
                    </span>
                  </label>
                  <label class="radio-card" :class="{ selected: exportScope === 'edited' }">
                    <input v-model="exportScope" value="edited" type="radio" :disabled="editedRowNumbers.length === 0" @change="onExportScopeChange" />
                    <span class="checkbox-text">
                      <span class="checkbox-title">Chỉ dòng đã sửa ({{ formatNumber(editedRowNumbers.length) }})</span>
                      <span class="checkbox-desc">Không đụng dòng bạn không chạm tới.</span>
                    </span>
                  </label>
                  <label class="radio-card" :class="{ selected: exportScope === 'selected' }">
                    <input v-model="exportScope" value="selected" type="radio" :disabled="selectedRowNumbers.length === 0" @change="onExportScopeChange" />
                    <span class="checkbox-text">
                      <span class="checkbox-title">Chỉ dòng đã tích ({{ formatNumber(selectedRowNumbers.length) }})</span>
                      <span class="checkbox-desc">Đúng danh sách bạn chọn tay.</span>
                    </span>
                  </label>
                  <label class="radio-card" :class="{ selected: exportScope === 'filtered' }">
                    <input v-model="exportScope" value="filtered" type="radio" @change="onExportScopeChange" />
                    <span class="checkbox-text">
                      <span class="checkbox-title">Theo bộ lọc hiện tại</span>
                      <span class="checkbox-desc">Năm / từ số–đến số / loại… như thanh lọc ở trên; bản lọc bỏ sheet thừa.</span>
                    </span>
                  </label>
                </div>
              </div>

              <div v-if="previewMode === 'full' && selectedRowNumbers.length > 0" class="alert alert-info" style="margin: 0 0 12px">
                <Info :size="15" />
                <div class="grow">
                  <strong>Không xuất thừa:</strong> chỉ ghi đúng {{ formatNumber(selectedRowNumbers.length) }} dòng bạn đã tích.
                </div>
                <label class="checkbox">
                  <input v-model="onlySelectedRows" type="checkbox" />
                  <span class="checkbox-title">Chỉ các dòng đã chọn</span>
                </label>
              </div>

              <div v-if="previewMode === 'filtered' && filteredPreview" class="summary-scope-stats">
                <span class="badge badge-brand">
                  {{ formatNumber(filteredPreview.totalRows) }} hồ sơ được giữ
                </span>
                <span class="badge badge-success">
                  {{ formatNumber(filteredPreview.changedRows) }} dòng cập nhật
                </span>
                <span v-if="filteredPreview.normalizedRows > 0" class="badge badge-info">
                  {{ formatNumber(filteredPreview.normalizedRows) }} dòng sửa mở đầu
                </span>
                <span v-if="filteredPreview.ignoredChanges > 0" class="badge badge-warning">
                  {{ formatNumber(filteredPreview.ignoredChanges) }} thay đổi ngoài phạm vi bị bỏ qua
                </span>
                <span class="badge badge-neutral">Chỉ còn sheet ML</span>
              </div>

              <div v-if="previewRows.length === 0" class="alert alert-info">
                <Info :size="17" />
                <div class="grow">
                  Không có dòng nào cần ghi trong phạm vi hiện tại.
                  <template v-if="previewMode === 'filtered' && filteredPreview">
                    Bản lọc vẫn giữ {{ formatNumber(filteredPreview.totalRows) }} hồ sơ.
                  </template>
                </div>
              </div>

              <div v-else class="summary-preview-list">
                <article v-for="row in previewRows" :key="row.rowNumber" class="summary-preview-item">
                  <header class="row-between wrap">
                    <div class="row wrap" style="gap: 8px">
                      <span class="badge badge-brand">Dòng {{ row.rowNumber }}</span>
                      <span v-if="row.summaryChanged" class="badge badge-info">Trích yếu</span>
                      <span v-if="row.sheetNumberChanged" class="badge badge-warning">Số tờ</span>
                      <span v-if="row.documentNumberChanged" class="badge badge-warning">Hồ sơ số</span>
                      <span v-if="row.numberLabelChanged" class="badge badge-warning">Số hồ sơ</span>
                    </div>
                    <span class="badge badge-success">Sẽ cập nhật</span>
                  </header>

                  <div class="summary-diff">
                    <div class="summary-diff-col">
                      <div class="summary-preview-label">File gốc</div>
                      <p class="summary-preview-text muted">{{ row.originalSummary }}</p>
                      <p v-if="row.sheetNumberChanged" class="summary-preview-text muted">
                        Số tờ: {{ row.originalSheetNumber || '(trống)' }}
                      </p>
                      <p v-if="row.documentNumberChanged" class="summary-preview-text muted">
                        Hồ sơ số: {{ row.originalDocumentNumber || '(trống)' }}
                      </p>
                      <p v-if="row.numberLabelChanged" class="summary-preview-text muted">
                        Số hồ sơ: {{ row.originalNumberLabel || '(trống)' }}
                      </p>
                    </div>
                    <ArrowRight class="summary-diff-arrow" :size="20" aria-hidden="true" />
                    <div class="summary-diff-col">
                      <div class="summary-preview-label">File sẽ tải về</div>
                      <p class="summary-preview-text">{{ row.summary }}</p>
                      <p v-if="row.sheetNumberChanged" class="summary-preview-text">
                        Số tờ: {{ row.sheetNumber || '(xoá)' }}
                      </p>
                      <p v-if="row.documentNumberChanged" class="summary-preview-text">
                        Hồ sơ số: {{ row.documentNumber || '(xoá)' }}
                      </p>
                      <p v-if="row.numberLabelChanged" class="summary-preview-text">
                        Số hồ sơ: {{ row.numberLabel || '(xoá)' }}
                      </p>
                    </div>
                  </div>
                </article>
              </div>

              <div v-if="previewMode === 'filtered' && filteredPreview?.truncated" class="alert alert-info" style="margin-top: 12px">
                <Info :size="17" />
                <div class="grow">
                  Danh sách xem trước được rút gọn còn 500 dòng đầu. Tất cả
                  {{ formatNumber(filteredPreview.changedRows) }} dòng vẫn được ghi khi xuất.
                </div>
              </div>

              <div v-else-if="previewMode === 'full' && previewRows.length >= 500" class="alert alert-info" style="margin-top: 12px">
                <Info :size="17" />
                <div class="grow">
                  Đang hiển thị 500 dòng đầu. Tất cả các dòng cần chuẩn hoá hoặc đã sửa vẫn
                  được ghi đầy đủ khi xuất file.
                </div>
              </div>
            </template>
          </div>

          <div class="modal-footer">
            <span class="hint grow">
              <template v-if="previewMode === 'filtered'">
                Giữ {{ formatNumber(filteredPreview?.totalRows ?? total) }} hồ sơ · ghi
                {{ formatNumber(filteredPreview?.changedRows ?? 0) }} dòng · chỉ còn sheet ML.
              </template>
              <template v-else>
                {{ formatNumber(previewRows.length) }} dòng sẽ được ghi. Các phần khác của workbook giữ nguyên.
              </template>
            </span>
            <button class="btn btn-secondary" type="button" @click="previewOpen = false">Quay lại</button>
            <button
              v-if="previewMode === 'filtered'"
              class="btn btn-accent"
              type="button"
              :disabled="filteredExporting || !filteredPreview || filteredPreview.totalRows === 0"
              @click="openExportName('filtered')"
            >
              <Download :size="16" />
              {{ filteredExporting ? 'Đang xuất bản lọc...' : 'Xuất bản lọc' }}
            </button>
            <button
              v-else
              class="btn btn-accent"
              type="button"
              :disabled="exporting || previewRows.length === 0"
              @click="openExportName('full')"
            >
              <Download :size="16" />
              {{ exporting ? 'Đang xuất file...' : 'Xuất file Excel' }}
            </button>
          </div>
        </div>
      </div>
      </Transition>

      <!-- E: xuất bản in theo năm -->
      <Transition name="modal">
      <div v-if="printOpen" class="modal-backdrop" @click.self="printOpen = false">
        <div class="modal" style="width: min(760px, 100%)">
          <div class="modal-header">
            <div>
              <div class="card-title">Xuất bản in theo năm</div>
              <div class="card-subtitle">
                Bản in dùng trích yếu đã sửa, số tờ đã kiểm tra và macro lọc/in của phòng.
              </div>
            </div>
            <button class="btn btn-ghost btn-icon" type="button" aria-label="Đóng" @click="printOpen = false">
              <X :size="18" />
            </button>
          </div>

          <div class="modal-body stack" style="gap: 14px">
            <div v-if="printLoading" class="loading-block" style="padding: 24px 0">
              <span class="spinner" />
              <span>Đang kiểm tra dữ liệu in...</span>
            </div>

            <template v-else>
              <div v-if="printError" class="alert alert-danger">
                <TriangleExclamationPoint :size="17" />
                <div class="grow">{{ printError }}</div>
              </div>

              <div class="row-between wrap">
                <div class="summary-preview-label">Năm xuất bản in</div>
                <div class="row" style="gap: 6px">
                  <button class="btn btn-ghost btn-sm" type="button" @click="selectAllPrintYears">
                    Chọn tất cả
                  </button>
                  <button class="btn btn-ghost btn-sm" type="button" @click="clearPrintYears">
                    Bỏ chọn
                  </button>
                </div>
              </div>

              <div class="stack" style="gap: 8px; max-height: 260px; overflow-y: auto">
                <label v-for="item in printPreview?.years ?? []" :key="item.year" class="checkbox">
                  <input
                    type="checkbox"
                    :checked="printYears.includes(item.year)"
                    @change="togglePrintYear(item.year)"
                  />
                  <span class="checkbox-text">
                    <span class="checkbox-title">
                      Năm {{ item.year }} · {{ formatNumber(item.total) }} hồ sơ
                    </span>
                    <span class="checkbox-desc">
                      <template v-if="printYearMissing(item) === 0">
                        Đủ dữ liệu in ({{ formatNumber(item.ready) }}/{{ formatNumber(item.total) }})
                      </template>
                      <template v-else>
                        Còn thiếu: {{ formatNumber(item.missingNumber) }} số hồ sơ ·
                        {{ formatNumber(item.missingDocumentCode) }} số ký hiệu ·
                        {{ formatNumber(item.missingDate) }} ngày ·
                        {{ formatNumber(item.missingSheetNumber) }} số tờ ·
                        {{ formatNumber(item.missingSummary) }} trích yếu
                      </template>
                    </span>
                  </span>
                </label>
              </div>

              <div class="alert" :class="printSelectedMissing > 0 ? 'alert-warning' : 'alert-info'">
                <TriangleExclamationPoint v-if="printSelectedMissing > 0" :size="17" />
                <Info v-else :size="17" />
                <div class="grow">
                  <template v-if="printSelectedMissing > 0">
                    Đang chọn {{ formatNumber(printYears.length) }} năm,
                    {{ formatNumber(printSelectedRows) }} hồ sơ; còn
                    {{ formatNumber(printSelectedMissing) }} trường thiếu. Bật “Vẫn xuất dù còn thiếu”
                    nếu muốn in ngay.
                  </template>
                  <template v-else>
                    Đang chọn {{ formatNumber(printYears.length) }} năm,
                    {{ formatNumber(printSelectedRows) }} hồ sơ — đủ dữ liệu in.
                  </template>
                </div>
              </div>

              <label class="checkbox" :class="{ 'is-disabled': printSelectedMissing === 0 }">
                <input
                  v-model="printAllowIncomplete"
                  type="checkbox"
                  :disabled="printSelectedMissing === 0"
                />
                <span class="checkbox-text">
                  <span class="checkbox-title">Vẫn xuất dù còn thiếu</span>
                  <span class="checkbox-desc">Các ô thiếu sẽ để trống trong bản in.</span>
                </span>
              </label>

              <div class="summary-scope-picker">
                <div class="summary-preview-label">Cách đóng gói</div>
                <div class="row wrap" style="gap: 8px">
                  <label class="radio-card" :class="{ selected: printMode === 'perYear' }">
                    <input v-model="printMode" value="perYear" type="radio" />
                    <span class="checkbox-text">
                      <span class="checkbox-title">Mỗi năm một file</span>
                      <span class="checkbox-desc">Tải về .zip gồm các .xlsm theo từng năm.</span>
                    </span>
                  </label>
                  <label class="radio-card" :class="{ selected: printMode === 'combined' }">
                    <input v-model="printMode" value="combined" type="radio" />
                    <span class="checkbox-text">
                      <span class="checkbox-title">Gộp một file</span>
                      <span class="checkbox-desc">Một .xlsm chứa tất cả sheet năm đã chọn.</span>
                    </span>
                  </label>
                </div>
              </div>

              <label class="field">
                <span class="label">Tên file (không gồm đuôi)</span>
                <input v-model="printFileName" class="input" type="text" autocomplete="off" />
              </label>
            </template>
          </div>

          <div class="modal-footer">
            <span class="hint grow">
              <template v-if="printMode === 'perYear'">Kết quả: .zip nhiều .xlsm</template>
              <template v-else>Kết quả: một .xlsm</template>
            </span>
            <button class="btn btn-secondary" type="button" @click="printOpen = false">Huỷ</button>
            <button
              class="btn btn-accent"
              type="button"
              :disabled="printExporting || printLoading || !printCanExport"
              @click="exportPrint"
            >
              <Download :size="16" />
              {{ printExporting ? 'Đang dựng bản in...' : 'Xuất bản in' }}
            </button>
          </div>
        </div>
      </div>
      </Transition>

      <!-- A3: đổi tên file xuất -->
      <Transition name="modal">
      <div v-if="exportNameOpen" class="modal-backdrop" @click.self="exportNameOpen = false">
        <div class="modal" style="width: min(520px, 100%)">
          <div class="modal-header">
            <div>
              <div class="card-title">Tên file sẽ tải về</div>
              <div class="card-subtitle">
                File gốc không thay đổi. Bản xuất giữ nguyên định dạng — chỉ các ô đã sửa được ghi lại.
              </div>
            </div>
            <button class="btn btn-ghost btn-icon" type="button" aria-label="Đóng" @click="exportNameOpen = false">
              <X :size="18" />
            </button>
          </div>
          <div class="modal-body stack" style="gap: 12px">
            <label class="field">
              <span class="label">Tên file</span>
              <input
                v-model="exportName"
                class="input"
                type="text"
                placeholder="VD: DK KET HON 1993_trichyeu.xlsx"
                autocomplete="off"
              />
            </label>
            <div class="alert alert-info">
              <Info :size="15" />
              <div class="grow">
                Đuôi file được giữ như file gốc (.xlsx/.xlsm). Để trống sẽ dùng tên mặc định.
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-secondary" type="button" @click="exportNameOpen = false">Huỷ</button>
            <button
              class="btn btn-accent"
              type="button"
              :disabled="exporting || filteredExporting"
              @click="confirmExportName"
            >
              <Download :size="16" />
              Tải file xuống
            </button>
          </div>
        </div>
      </div>
      </Transition>

      <!-- Lưu file vừa xuất vào Kho hồ sơ để lần sau khỏi import lại -->
      <Transition name="modal">
      <div v-if="saveToArchiveOpen" class="modal-backdrop" @click.self="saveToArchiveOpen = false">
        <div class="modal" style="width: min(520px, 100%)">
          <div class="modal-header">
            <div>
              <div class="card-title">Lưu vào Kho hồ sơ?</div>
              <div class="card-subtitle">
                File {{ lastExportedName }} sẽ được đồng bộ vào SQL Server. Lần sau mở Kho là thấy,
                không cần import lại. Tên file có hậu tố _trichyeu/_loc để nhận biết là file chỉnh trích yếu.
              </div>
            </div>
            <button class="btn btn-ghost btn-icon" type="button" aria-label="Đóng" @click="saveToArchiveOpen = false">
              <X :size="18" />
            </button>
          </div>
          <div class="modal-body">
            <div class="alert alert-info">
              <Info :size="15" />
              <div class="grow">
                Đồng bộ theo nguyên tắc “Excel thắng”: thêm/sửa/xoá hồ sơ trong Kho theo đúng file đã xuất.
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-secondary" type="button" @click="saveToArchiveOpen = false">Để sau</button>
            <button
              class="btn btn-accent"
              type="button"
              :disabled="archiving"
              @click="archiveLastExport"
            >
              {{ archiving ? 'Đang lưu...' : 'Lưu vào Kho' }}
            </button>
          </div>
        </div>
      </div>
      </Transition>
      <!-- Modal: Nạp thẳng vào Kho hồ sơ không cần qua bước tải file -->
      <Transition name="modal">
      <div v-if="directSyncModalOpen && session" class="modal-backdrop" @click.self="directSyncModalOpen = false">
        <div class="modal" style="width: min(560px, 100%)">
          <div class="modal-header">
            <div>
              <div class="card-title">Nạp thẳng vào Kho hồ sơ</div>
              <div class="card-subtitle">
                Đưa toàn bộ dữ liệu đang sửa trực tiếp vào bảng Dossiers trong SQL Server — không cần tải file về rồi upload lại.
              </div>
            </div>
            <button class="btn btn-ghost btn-icon" type="button" aria-label="Đóng" @click="directSyncModalOpen = false">
              <X :size="18" />
            </button>
          </div>
          <div class="modal-body stack" style="gap: 14px">
            <div class="card" style="padding: 12px 14px; background: var(--surface)">
              <div class="row-between">
                <span class="muted">Workbook nguồn:</span>
                <strong>{{ session.fileName }}</strong>
              </div>
              <div class="row-between" style="margin-top: 6px">
                <span class="muted">Tổng số hồ sơ:</span>
                <strong>{{ formatNumber(session.rowCount) }}</strong>
              </div>
              <div class="row-between" style="margin-top: 6px">
                <span class="muted">Số dòng đã sửa:</span>
                <span class="badge badge-brand">{{ formatNumber(changedCount) }} dòng</span>
              </div>
            </div>

            <div v-if="directSyncResult" class="alert alert-success">
              <CircleCheck :size="18" />
              <div class="grow">
                <strong>Nạp thành công vào Kho!</strong>
                <div style="margin-top: 2px">
                  Thêm mới <strong>{{ formatNumber(directSyncResult.addedCount) }}</strong> ·
                  Sửa <strong>{{ formatNumber(directSyncResult.updatedCount) }}</strong> ·
                  Xung đột <strong>{{ formatNumber(directSyncResult.conflictCount) }}</strong>.
                </div>
              </div>
            </div>

            <div v-else class="alert alert-info">
              <Info :size="16" />
              <div class="grow">
                Hệ thống sẽ tổng hợp file gốc cùng toàn bộ thay đổi của anh em, tạo phiên bản đồng bộ và nạp an toàn vào SQL Server. Cả phòng đều sẽ nhận được thông báo.
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-secondary" type="button" @click="directSyncModalOpen = false">
              {{ directSyncResult ? 'Đóng' : 'Huỷ' }}
            </button>
            <RouterLink v-if="directSyncResult" class="btn btn-primary" to="/kho">
              Mở Kho hồ sơ ngay
            </RouterLink>
            <button
              v-else
              class="btn btn-primary"
              type="button"
              :disabled="directSyncing"
              @click="doDirectSyncToArchive"
            >
              <Database :size="16" />
              {{ directSyncing ? 'Đang nạp vào CSDL...' : 'Xác nhận nạp vào Kho' }}
            </button>
          </div>
        </div>
      </div>
      </Transition>
    </Teleport>
  </div>
</template>
