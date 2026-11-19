<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  ArrowLeft,
  ArrowRight,
  Bell,
  BellOff,
  Check,
  CircleCheck,
  Clock,
  Cloud,
  Copy,
  Database,
  Download,
  Eraser,
  ExternalLink,
  FilePenLine,
  FileSpreadsheet,
  FileText,
  FolderClock,
  Info,
  LogOut,
  MessageSquare,
  Mic,
  Radio,
  RefreshCw,
  Save,
  Search,
  Settings,
  Sparkles,
  Star,
  TriangleExclamationPoint,
  Users,
  Volume2,
  VolumeX,
  ChevronDown,
  ChevronUp,
  X,
  Zap,
} from '@lucide/vue'
import { download, queryString, request, saveBlob } from '../api/client'
import type {
  SummaryActiveSessionResponse,
  SummaryCatalogItem,
  SummaryCatalogPresetResponse,
  SummaryCatalogPresetUpsertRequest,
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
  SummaryAiParseVoiceResponse,
  SyncResponse,
  AutoTickSyncSummaryItem,
  AutoTickSyncSummaryRequest,
  AutoTickSyncSummaryResponse,
} from '../api/types'
import { errorMessage } from '../lib/errors'
import { formatBytes, formatNumber } from '../lib/format'
import {
  applyLearned,
  normalizeSpeech,
  parseVoiceTranscript,
  preprocessSpokenText,
} from '../lib/voiceParser'
import { useVoiceInput } from '../composables/useVoiceInput'
import EmptyState from '../components/EmptyState.vue'
import AppSelect from '../components/AppSelect.vue'
import FilePicker from '../components/FilePicker.vue'
import SummaryItemPicker from '../components/SummaryItemPicker.vue'
import { useToast } from '../composables/useToast'
import { useConfirm } from '../composables/useConfirm'
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
  lastOneDriveSyncCompleted,
  lastOneDriveSyncQueueStarted,
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
const { confirmDialog } = useConfirm()

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
const sortByNumber = ref(false)

function resetScopeFilters(): void {
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
  sortByNumber.value = false
  page.value = 1
}

const hasActiveFilter = computed(() => {
  return (
    year.value !== '' ||
    yearFrom.value !== '' ||
    yearTo.value !== '' ||
    Boolean(type.value) ||
    Boolean(search.value.trim()) ||
    onlyExisting.value ||
    numberMode.value !== 'all' ||
    onlyMissingSheetNumber.value ||
    onlyMissingDocumentNumber.value ||
    onlyMissingNumber.value ||
    onlyNeedsNormalization.value ||
    missingSheetNumberFirst.value ||
    sortByNumber.value
  )
})

function clearAllFilters(): void {
  resetScopeFilters()
  void fetchRows()
}

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

const isCurrentSessionActiveTeam = computed(() => {
  return !!(
    session.value &&
    activeTeamSession.value?.hasActiveSession &&
    activeTeamSession.value.sessionId === session.value.sessionId
  )
})

const isLiveOwnerOrAdmin = computed(() => {
  const me = (myActorName.value || '').trim().toLowerCase()
  const uploader = (activeTeamSession.value?.uploadedBy || '').trim().toLowerCase()
  const role = teamIdentity.value?.role
  return (uploader && me && uploader === me) || role === 'admin' || isAdmin.value
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

// OneDrive Live Sync state
const ONEDRIVE_STORAGE_KEY = 'stp.autotick.onedrive_url'
const oneDriveUrl = ref(localStorage.getItem(ONEDRIVE_STORAGE_KEY) || '')
const oneDriveSyncModalOpen = ref(false)
const syncingOneDrive = ref(false)
const oneDriveSyncResult = ref<AutoTickSyncSummaryResponse | null>(null)
const oneDriveSyncError = ref<string | null>(null)
const rowsToSyncPreview = ref<AutoTickSyncSummaryItem[]>([])
const selectedSyncSheet = ref('1997')
const oneDriveSyncScope = ref<'number_range' | 'selected' | 'changes_only' | 'current_filter'>('number_range')
const syncNumberFrom = ref<number | ''>('')
const syncNumberTo = ref<number | ''>('')
const syncStatusA = ref('đã nhập')
const syncingInBackground = ref(false)
const isQueuedForOneDrive = ref(false)

watch(lastOneDriveSyncQueueStarted, (started) => {
  if (!started) return
  isQueuedForOneDrive.value = false
  syncingInBackground.value = true
  toast.info('Robot bắt đầu làm việc', started.message)
})

watch(lastOneDriveSyncCompleted, (completed) => {
  if (!completed) return
  syncingInBackground.value = false
  isQueuedForOneDrive.value = false
  if (completed.success) {
    toast.success('Đồng bộ OneDrive thành công!', completed.message)
  } else {
    toast.error('Đồng bộ OneDrive chưa hoàn tất', completed.message)
  }
})

// Kiểm tra quyền Sửa / Đăng nhập Google tức thì cho link Cloud
interface LinkPermissionStatus {
  isGoogleSheet: boolean
  canEdit: boolean
  needsLogin: boolean
  title?: string
  message: string
}

const checkingLinkPermission = ref(false)
const linkPermissionStatus = ref<LinkPermissionStatus | null>(null)
const openingLoginWindow = ref(false)
let checkPermissionDebounceTimer: ReturnType<typeof setTimeout> | null = null

async function checkLinkPermission(urlToCheck?: string): Promise<void> {
  const target = (urlToCheck ?? oneDriveUrl.value).trim()
  if (!target) {
    linkPermissionStatus.value = null
    return
  }
  checkingLinkPermission.value = true
  try {
    const res = await request<LinkPermissionStatus>('/api/auto-tick/check-permission', {
      method: 'POST',
      body: { url: target },
    })
    linkPermissionStatus.value = res
  } catch (err) {
    linkPermissionStatus.value = {
      isGoogleSheet: target.includes('docs.google.com') || target.includes('google.com/spreadsheets'),
      canEdit: false,
      needsLogin: true,
      message: errorMessage(err) || 'Không thể kiểm tra quyền truy cập link.',
    }
  } finally {
    checkingLinkPermission.value = false
  }
}

async function openInteractiveLogin(): Promise<void> {
  const target = oneDriveUrl.value.trim()
  if (!target) return
  openingLoginWindow.value = true
  try {
    await request('/api/auto-tick/open-login', {
      method: 'POST',
      body: { url: target },
    })
    toast.info('Cửa sổ đăng nhập đã mở', 'Sau khi đăng nhập trên cửa sổ Chrome, hệ thống sẽ tự động nhận diện quyền Sửa mà không cần bấm kiểm tra lại.')

    // Tự động kiểm tra liên tục mỗi 3 giây để tự chuyển sang Xanh ngay khi vừa đăng nhập xong
    let pollCount = 0
    const pollTimer = setInterval(async () => {
      pollCount++
      if (pollCount > 40 || !oneDriveSyncModalOpen.value || linkPermissionStatus.value?.canEdit) {
        clearInterval(pollTimer)
        return
      }
      await checkLinkPermission(target)
      if (linkPermissionStatus.value?.canEdit) {
        clearInterval(pollTimer)
        toast.success('Đã xác thực thành công!', 'Tài khoản có quyền Chỉnh sửa sẵn sàng.')
      }
    }, 3000)
  } catch (err) {
    toast.error('Lỗi mở cửa sổ', errorMessage(err))
  } finally {
    openingLoginWindow.value = false
  }
}

watch(oneDriveUrl, (newVal) => {
  if (checkPermissionDebounceTimer) clearTimeout(checkPermissionDebounceTimer)
  checkPermissionDebounceTimer = setTimeout(() => {
    if (oneDriveSyncModalOpen.value && newVal.trim()) {
      void checkLinkPermission(newVal)
    }
  }, 700)
})

watch(oneDriveSyncModalOpen, (isOpen) => {
  if (isOpen && oneDriveUrl.value.trim()) {
    void checkLinkPermission()
  }
})


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
  void request<SummaryActiveSessionResponse>('/api/summary/active')
    .then((act) => {
      activeTeamSession.value = act && act.hasActiveSession ? act : null
    })
    .catch(() => {})

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
    resetScopeFilters()
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

async function joinActiveTeamSession(): Promise<void> {
  if (!activeTeamSession.value?.sessionId) return
  resuming.value = true
  try {
    const ok = await restoreFromServer(activeTeamSession.value.sessionId)
    if (ok) {
      toast.success(
        'Đã vào file làm việc chung',
        `File "${activeTeamSession.value.fileName}" do ${activeTeamSession.value.uploadedBy} chia sẻ.`
      )
    } else {
      toast.error('Không thể mở file làm việc chung', 'Phiên có thể đã kết thúc hoặc bị gỡ.')
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
  sortByNumber: sortByNumber.value,
}))

const scopeLabel = computed(() => {
  const parts: string[] = []

  if (sortByNumber.value) {
    parts.push('xếp theo số hồ sơ tăng dần')
  }

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
    .filter((edit) => rows.value.length === 0 || rows.value.some((r) => r.rowNumber === edit.row.rowNumber))
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
 * Bộ 12 giấy tờ chuẩn cho hồ sơ "Đăng ký kết hôn" (100% có theo quy chuẩn)
 * Dùng cho áp nhanh hàng loạt và gán mặc định.
 */
const STANDARD_TWELVE = [
  'giấy khai sinh',
  'bộ công hàm',
  'hộ khẩu',
  'phiếu đề xuất',
  'chứng chỉ chuyên khoa',
  'phiếu khám sức khỏe',
  'CMND',
  'phiếu nhập xuất cảnh',
  'hộ chiếu',
  'thị thực',
  'tờ khai đăng ký kết hôn',
  'đơn xin xác nhận độc thân và hôn nhân thực tế',
]
const STANDARD_SEVEN = STANDARD_TWELVE

/** Tần suất từng mục theo meta phiên → badge ×N trong picker "hay gặp nhất". */
const catalogCounts = computed<Record<string, number>>(() => {
  const map: Record<string, number> = {}
  for (const meta of session.value?.catalogMeta ?? []) {
    map[normalizeText(meta.name)] = meta.usageCount
  }
  return map
})

/** Chọn sẵn bộ 12 chuẩn cho bulk picker (bấm "Áp dụng" để áp). */
function bulkApplyStandardSeven(): void {
  if (!session.value) return
  bulkItems.value = [...STANDARD_TWELVE]
  toast.info('Đã chọn bộ 12 chuẩn', 'Bấm "Áp dụng cho dòng đã chọn" để xác nhận.')
}

// ===== Quản lý Bộ Mẫu Giấy Tờ do Admin cấu hình (Presets Studio) =====
const catalogPresets = ref<SummaryCatalogPresetResponse[]>([])
const presetsLoading = ref(false)
const presetModalOpen = ref(false)
const editingPreset = ref<SummaryCatalogPresetUpsertRequest | null>(null)

async function fetchCatalogPresets(): Promise<void> {
  presetsLoading.value = true
  try {
    const list = await request<SummaryCatalogPresetResponse[]>('/api/summary/catalog/presets')
    if (list && list.length > 0) {
      catalogPresets.value = list
    }
  } catch (err) {
    console.warn('Lỗi đọc catalog presets', err)
  } finally {
    presetsLoading.value = false
  }
}

function applyPreset(preset: SummaryCatalogPresetResponse): void {
  if (!session.value) return
  bulkItems.value = [...preset.items]
  toast.info(`Đã chọn "${preset.name}"`, 'Bấm "Áp dụng cho dòng đã chọn" để xác nhận.')
}

function openCreatePresetModal(): void {
  editingPreset.value = {
    id: null,
    name: '',
    dossierType: type.value || 'Đăng ký kết hôn',
    items: bulkItems.value.length > 0 ? [...bulkItems.value] : [...STANDARD_SEVEN],
    isDefault: false,
    order: catalogPresets.value.length + 1,
  }
  presetModalOpen.value = true
}

function openEditPresetModal(preset: SummaryCatalogPresetResponse): void {
  editingPreset.value = {
    id: preset.id,
    name: preset.name,
    dossierType: preset.dossierType,
    items: [...preset.items],
    isDefault: preset.isDefault,
    order: preset.order,
  }
  presetModalOpen.value = true
}

async function savePreset(): Promise<void> {
  if (!editingPreset.value || !editingPreset.value.name.trim()) {
    toast.warning('Tên bộ mẫu', 'Vui lòng nhập tên bộ mẫu.')
    return
  }
  if (!editingPreset.value.items || editingPreset.value.items.length === 0) {
    toast.warning('Giấy tờ rỗng', 'Chọn ít nhất một loại giấy tờ cho bộ mẫu.')
    return
  }

  try {
    await request<SummaryCatalogPresetResponse>('/api/summary/catalog/presets', {
      method: 'POST',
      body: editingPreset.value,
    })
    toast.success('Đã lưu bộ mẫu', `Bộ mẫu "${editingPreset.value.name}" đã được lưu cho cả phòng dùng chung.`)
    presetModalOpen.value = false
    editingPreset.value = null
    void fetchCatalogPresets()
  } catch (err) {
    toast.error('Lỗi lưu bộ mẫu', errorMessage(err))
  }
}

async function deletePreset(id: string, name: string): Promise<void> {
  const ok = await confirmDialog({
    title: 'Xoá bộ mẫu?',
    message: `Bạn có chắc muốn xoá bộ mẫu "${name}"? Các thành viên khác sẽ không thể dùng bộ mẫu này nữa.`,
    variant: 'danger',
    confirmText: 'Xoá bộ mẫu',
  })
  if (!ok) return
  try {
    await request(`/api/summary/catalog/presets/${id}`, { method: 'DELETE' })
    toast.info('Đã xoá bộ mẫu', `Đã xoá bộ mẫu "${name}".`)
    void fetchCatalogPresets()
  } catch (err) {
    toast.error('Lỗi xoá bộ mẫu', errorMessage(err))
  }
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

async function removeActiveTeamSession(): Promise<void> {
  if (!activeTeamSession.value?.hasActiveSession) return

  const ok = await confirmDialog({
    title: 'Gỡ file làm việc chung?',
    message: `Bạn có chắc muốn GỠ file "${activeTeamSession.value.fileName}" khỏi chế độ làm việc chung của phòng không? Sau khi gỡ, các thành viên khác sẽ không còn thấy file này để làm việc chung nữa.`,
    variant: 'danger',
    confirmText: 'Gỡ file làm chung',
    cancelText: 'Giữ lại',
  })
  if (!ok) return

  settingActiveSession.value = true
  try {
    await request('/api/summary/active', { method: 'DELETE' })
    activeTeamSession.value = null
    toast.info('Đã gỡ file làm việc chung', 'File không còn chia sẻ trực tiếp cho cả phòng.')
  } catch (caught) {
    toast.error('Không thể gỡ file', errorMessage(caught))
  } finally {
    settingActiveSession.value = false
  }
}

async function toggleActiveTeamSession(): Promise<void> {
  const active = session.value
  if (!active) return

  if (activeTeamSession.value?.sessionId === active.sessionId) {
    await removeActiveTeamSession()
    return
  }

  settingActiveSession.value = true
  try {
    const res = await request<SummaryActiveSessionResponse>('/api/summary/active', {
      method: 'POST',
      body: { sessionId: active.sessionId },
    })
    activeTeamSession.value = res
    toast.success(
      'Đã đặt làm file làm việc chung',
      'Tất cả thành viên trong phòng khi mở app sẽ tự động vào chung file này.',
    )
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
  if (ordered.length === 0) return null
  if (afterRowNumber <= 0) {
    const firstMissing = ordered.find((row) => itemsFor(row).length === 0)
    return firstMissing ?? ordered[0]
  }
  const index = ordered.findIndex((row) => row.rowNumber === afterRowNumber)
  if (index < 0) {
    const firstMissing = ordered.find((row) => itemsFor(row).length === 0)
    return firstMissing ?? ordered[0]
  }
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
    bulkItems.value = [...STANDARD_SEVEN]
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
        if (selected.value.size > 0 && !selected.value.has(row.rowNumber)) continue
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
      const scopeDesc = selected.value.size > 0 ? `${formatNumber(applied)} dòng đã chọn` : `${formatNumber(applied)} dòng trong phạm vi đang lọc`
      toast.success(
        'Đã áp cho dòng còn thiếu',
        `${scopeDesc} nhận bộ ${bulkItems.value.join(', ')}.`,
      )
      void announceAction(
        'bulk',
        `Gán bộ cho ${scopeDesc}`,
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

const VOICE_TTS_KEY = 'stp.voiceTts'
const VOICE_CHIME_KEY = 'stp.voiceChime'
const voiceTtsEnabled = ref<boolean>(readVoiceTts())
const voiceChimeEnabled = ref<boolean>(readVoiceChime())
const voiceConsoleOpen = ref(true)

function readVoiceChime(): boolean {
  try {
    const val = localStorage.getItem(VOICE_CHIME_KEY)
    return val === null ? true : val === 'true'
  } catch {
    return true
  }
}

function toggleVoiceChime(): void {
  voiceChimeEnabled.value = !voiceChimeEnabled.value
  try {
    localStorage.setItem(VOICE_CHIME_KEY, String(voiceChimeEnabled.value))
  } catch {}
  if (voiceChimeEnabled.value) {
    playNotificationSound('success')
  }
}

function readVoiceTts(): boolean {
  try {
    const val = localStorage.getItem(VOICE_TTS_KEY)
    return val === null ? true : val === 'true'
  } catch {
    return true
  }
}

function toggleVoiceTts(): void {
  voiceTtsEnabled.value = !voiceTtsEnabled.value
  try {
    localStorage.setItem(VOICE_TTS_KEY, String(voiceTtsEnabled.value))
  } catch {}
  if (voiceTtsEnabled.value) {
    speakVoiceFeedback('Đã bật giọng nói phản hồi.')
  } else {
    stopAnySpeech()
  }
}

function playNotificationSound(type: 'start' | 'success' = 'success'): void {
  if (!voiceChimeEnabled.value) return
  try {
    const AudioCtx =
      window.AudioContext ||
      (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext
    if (!AudioCtx) return
    const ctx = new AudioCtx()
    const now = ctx.currentTime

    if (type === 'start') {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'sine'
      osc.frequency.setValueAtTime(587.33, now) // D5
      osc.frequency.exponentialRampToValueAtTime(880, now + 0.08) // A5
      gain.gain.setValueAtTime(0.05, now)
      gain.gain.exponentialRampToValueAtTime(0.001, now + 0.1)
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start(now)
      osc.stop(now + 0.1)
    } else if (type === 'success') {
      // Chuông báo êm dịu (Harp chime) E5 -> B5
      const notes = [659.25, 987.77]
      notes.forEach((freq, i) => {
        const osc = ctx.createOscillator()
        const gain = ctx.createGain()
        osc.type = 'triangle'
        osc.frequency.setValueAtTime(freq, now + i * 0.07)
        gain.gain.setValueAtTime(0.07, now + i * 0.07)
        gain.gain.exponentialRampToValueAtTime(0.001, now + i * 0.07 + 0.2)
        osc.connect(gain)
        gain.connect(ctx.destination)
        osc.start(now + i * 0.07)
        osc.stop(now + i * 0.07 + 0.2)
      })
    }
  } catch {
    // Bỏ qua nếu audio context bị chặn
  }
}

const isAiSpeaking = ref(false)
let sharedTtsAudio: HTMLAudioElement | null = null
let voiceClearOnNextSpeech = false
let voiceAutoClearTimer: number | null = null

function scheduleVoiceInputClear(): void {
  voiceClearOnNextSpeech = true
  if (voiceAutoClearTimer) {
    window.clearTimeout(voiceAutoClearTimer)
    voiceAutoClearTimer = null
  }
  // Tự động dọn dẹp ô văn bản sau 2.5 giây khi câu lệnh đã được thực thi và người dùng đang im lặng,
  // tránh tình trạng chữ cũ ("Xin chào", câu trước) bị kẹt mãi trên màn hình!
  voiceAutoClearTimer = window.setTimeout(() => {
    voiceAutoClearTimer = null
    voiceInputText.value = ''
    voiceInterim.value = ''
    voiceClearOnNextSpeech = false
  }, 2500)
}

function getOrCreateTtsAudio(): HTMLAudioElement {
  if (!sharedTtsAudio && typeof Audio !== 'undefined') {
    sharedTtsAudio = new Audio()
    sharedTtsAudio.preload = 'auto'
    sharedTtsAudio.addEventListener('playing', () => {
      isAiSpeaking.value = true
    })
    sharedTtsAudio.addEventListener('ended', () => {
      isAiSpeaking.value = false
    })
    sharedTtsAudio.addEventListener('pause', () => {
      isAiSpeaking.value = false
    })
    sharedTtsAudio.addEventListener('error', (e) => {
      console.warn('[TTS Audio Error]', e)
      isAiSpeaking.value = false
    })
  }
  return sharedTtsAudio!
}

function stopAnySpeech(): void {
  if (sharedTtsAudio) {
    try {
      sharedTtsAudio.pause()
      sharedTtsAudio.removeAttribute('src')
      sharedTtsAudio.load()
      sharedTtsAudio.currentTime = 0
    } catch {}
  }
  if ('speechSynthesis' in window) {
    try {
      window.speechSynthesis.cancel()
    } catch {}
  }
  isAiSpeaking.value = false
}

let vietnameseVoice: SpeechSynthesisVoice | null = null
const vietnameseVoiceName = ref<string>('')

function detectVietnameseVoice(): SpeechSynthesisVoice | null {
  if (!('speechSynthesis' in window)) return null
  const all = window.speechSynthesis.getVoices()
  if (all.length === 0) return null

  // 1. Ưu tiên các giọng tự nhiên cao cấp: HoaiMy/NamMinh (Edge) hoặc Google tiếng Việt (Chrome)
  const exact = all.find(
    (v) =>
      (v.lang === 'vi-VN' || v.lang === 'vi_VN') &&
      (v.name.includes('HoaiMy') || v.name.includes('NamMinh') || v.name.includes('Google') || v.name.includes('Natural')),
  )
  if (exact) return exact

  // 2. Bất kỳ giọng vi-VN nào
  const anyViVn = all.find((v) => v.lang === 'vi-VN' || v.lang === 'vi_VN')
  if (anyViVn) return anyViVn

  // 3. Tiền tố vi hoặc tên có chứa chữ Vietnam / tiếng Việt
  const anyVi = all.find(
    (v) =>
      v.lang.startsWith('vi') ||
      v.name.toLowerCase().includes('vietnam') ||
      v.name.toLowerCase().includes('tiếng việt'),
  )
  return anyVi ?? null
}

function initVoices(): void {
  if (!('speechSynthesis' in window)) return
  const update = () => {
    vietnameseVoice = detectVietnameseVoice()
    if (vietnameseVoice) {
      const cleanName = vietnameseVoice.name
        .replace(/(Microsoft|Google|Online|\(Natural\)|Vietnam|Desktop)/gi, '')
        .trim()
      vietnameseVoiceName.value = cleanName || 'Tiếng Việt'
    } else {
      vietnameseVoiceName.value = ''
    }
  }
  update()
  if (window.speechSynthesis.onvoiceschanged !== undefined) {
    window.speechSynthesis.onvoiceschanged = update
  }
}

function speakVoiceFeedback(text: string): void {
  // Chuông báo độc lập: phát ting-ting khi chuông bật
  if (voiceChimeEnabled.value) {
    playNotificationSound('success')
  }

  // Giọng nói AI: nếu tắt tiếng thì không phát âm thanh giọng đọc
  if (!voiceTtsEnabled.value) return

  const cleanText = text.trim()
  if (!cleanText) return

  // Dừng mọi âm thanh TTS đang phát dở trước khi nói câu mới
  stopAnySpeech()

  // 1. Ưu tiên cao nhất: Phát giọng đọc tiếng Việt chuẩn (Vietnamese TTS qua /api/summary/ai/tts)
  try {
    const audio = getOrCreateTtsAudio()
    audio.src = `/api/summary/ai/tts?text=${encodeURIComponent(cleanText)}`
    isAiSpeaking.value = true
    const playPromise = audio.play()
    if (playPromise !== undefined) {
      playPromise.catch((playErr) => {
        console.warn('[TTS] Audio play bị chặn hoặc lỗi:', playErr)
        isAiSpeaking.value = false
        // CHỈ fallback sang browser Web Speech nếu máy tính THẬT SỰ CÓ GIỌNG TIẾNG VIỆT
        // Tuyệt đối không để Microsoft David (tiếng Anh) đọc bậy!
        fallbackBrowserSpeech(cleanText)
      })
    }
  } catch (err) {
    console.warn('[TTS] Lỗi phát Audio:', err)
    isAiSpeaking.value = false
    fallbackBrowserSpeech(cleanText)
  }
}

function fallbackBrowserSpeech(text: string): void {
  if (!('speechSynthesis' in window)) return
  if (!vietnameseVoice) {
    vietnameseVoice = detectVietnameseVoice()
  }
  // TUYỆT ĐỐI không gọi nếu không có voice tiếng Việt! Tránh bị Microsoft David (English) đọc tiếng Việt
  if (!vietnameseVoice) {
    console.warn('[TTS] Không có voice tiếng Việt cục bộ, bỏ qua Web Speech fallback để tránh tiếng Anh.')
    return
  }
  try {
    const utterance = new SpeechSynthesisUtterance(text)
    utterance.voice = vietnameseVoice
    utterance.lang = vietnameseVoice.lang || 'vi-VN'
    utterance.rate = 1.02
    utterance.pitch = 1.0
    utterance.onstart = () => {
      isAiSpeaking.value = true
    }
    utterance.onend = () => {
      isAiSpeaking.value = false
    }
    utterance.onerror = () => {
      isAiSpeaking.value = false
    }
    window.speechSynthesis.speak(utterance)
  } catch (err) {
    console.warn('Speech synthesis error:', err)
    isAiSpeaking.value = false
  }
}

const voiceInputText = ref('')
const voiceTextAreaRef = ref<HTMLTextAreaElement | null>(null)

const {
  supported: voiceSupported,
  listening: voiceListening,
  interim: voiceInterim,
  volume: voiceVolume,
  start: startVoiceInput,
  stop: stopVoiceInput,
  toggle: toggleVoiceInput,
  clear: clearVoiceInput,
} = useVoiceInput({
  onFinal: (text) => {
    void handleVoiceFinal(text)
  },
  onSpeechOnset: () => {
    // 1. CÂM HỌNG AI NGAY LẬP TỨC TRONG 16MS khi micro phát hiện tiếng nói (không đợi transcript, không độ trễ)
    stopAnySpeech()

    // 2. Xóa sạch text cũ của câu trước ngay khi người dùng mở miệng nói câu mới
    if (voiceClearOnNextSpeech || voiceAutoClearTimer) {
      voiceInputText.value = ''
      voiceInterim.value = ''
      voiceClearOnNextSpeech = false
    }
    if (voiceAutoClearTimer) {
      window.clearTimeout(voiceAutoClearTimer)
      voiceAutoClearTimer = null
    }
  },
  onInterim: (text) => {
    // Nếu AI vẫn đang phát âm thanh thì dập tắt ngay
    stopAnySpeech()

    voiceInterim.value = text
    if (text) {
      voiceClearOnNextSpeech = false
      voiceInputText.value = text
      nextTick(() => {
        if (voiceTextAreaRef.value) {
          voiceTextAreaRef.value.scrollTop = voiceTextAreaRef.value.scrollHeight
        }
      })
    }
  },
  onError: (message) => {
    if (message) toast.error('Không nghe được', message)
  },
})

const voiceLastHeard = ref('')
const voiceAiParsing = ref(false)
const voicePttActive = ref(false)
const voiceLastAiModel = ref('')
let pttMouseDownTimer: number | undefined

function startVoice(): void {
  if (!voiceSupported.value) {
    toast.warning('Trình duyệt chưa hỗ trợ', 'Đọc giọng nói cần Chrome hoặc Edge (Web Speech API, tiếng Việt).')
    return
  }
  // Câm miệng AI ngay lập tức khi mở mic
  stopAnySpeech()
  voiceConsoleOpen.value = true
  playNotificationSound('start')
  startVoiceInput()
}

function stopVoice(): void {
  stopVoiceInput(true)
}

function submitVoiceText(): void {
  stopAnySpeech()
  const text = (voiceInputText.value || voiceInterim.value || '').trim()
  if (!text) return
  if (voiceListening.value) {
    stopVoiceInput(false)
  }
  clearVoiceInput()
  void handleVoiceFinal(text)
}

function clearVoiceText(): void {
  stopAnySpeech()
  clearVoiceInput()
  voiceInterim.value = ''
  voiceInputText.value = ''
  voiceClearOnNextSpeech = false
  if (voiceAutoClearTimer) {
    window.clearTimeout(voiceAutoClearTimer)
    voiceAutoClearTimer = null
  }
}

function onPttMouseDown(): void {
  pttMouseDownTimer = window.setTimeout(() => {
    if (!voiceListening.value) {
      voicePttActive.value = true
      startVoice()
    }
  }, 180)
}

function onPttMouseUp(): void {
  if (pttMouseDownTimer) {
    window.clearTimeout(pttMouseDownTimer)
    pttMouseDownTimer = undefined
  }
  if (voicePttActive.value) {
    voicePttActive.value = false
    stopVoice()
  }
}

const voiceLastAiResult = ref<{
  items: string[]
  command?: string | null
  sheetNumber?: string | null
  model?: string | null
  targetRowNumber?: number | null
  targetLabel?: string | null
  reply?: string | null
} | null>(null)

function onMicPillClick(): void {
  stopAnySpeech()
  voiceConsoleOpen.value = true
  toggleVoice()
}

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

/** Hành động chờ người dùng xác nhận (Interactive Confirmation Turn) */
export interface VoicePendingAction {
  rawPrompt: string
  cleanedPrompt: string
  command: string
  items: string[]
  targetRowNumber?: number | null
  targetDocumentNumber?: number | null
  sheetNumber?: string | null
  question: string
}

const voicePendingAction = ref<VoicePendingAction | null>(null)

/** Bộ nhớ lưu trữ câu prompt & bài học thông minh */
export interface VoicePromptMemory {
  id: string
  time: string
  raw: string
  fixed: string
  action: string
  items: string[]
}

const VOICE_MEMORY_KEY = 'stp.voiceMemory'
const voiceMemoryList = ref<VoicePromptMemory[]>(readVoiceMemory())

function readVoiceMemory(): VoicePromptMemory[] {
  try {
    const raw = localStorage.getItem(VOICE_MEMORY_KEY)
    const list = raw ? (JSON.parse(raw) as unknown) : []
    if (!Array.isArray(list)) return []
    return (list as VoicePromptMemory[]).slice(0, 50)
  } catch {
    return []
  }
}

function saveVoiceMemory(item: VoicePromptMemory): void {
  voiceMemoryList.value = [item, ...voiceMemoryList.value.filter((m) => m.raw !== item.raw)].slice(0, 50)
  try {
    localStorage.setItem(VOICE_MEMORY_KEY, JSON.stringify(voiceMemoryList.value))
  } catch {
    // Tiện ích lưu trữ - bỏ qua nếu không lưu được
  }
}

async function executeVoicePendingAction(): Promise<void> {
  const pending = voicePendingAction.value
  if (!pending) return

  // 1. Thực hiện lệnh tương ứng
  if (pending.command === 'apply_missing') {
    selected.value.clear()
    if (pending.items && pending.items.length > 0) {
      bulkItems.value = [...pending.items]
    } else if (bulkItems.value.length === 0) {
      bulkItems.value = [...STANDARD_SEVEN]
    }
    await applyToMissingInScope()
    const fb = `Đã xác nhận và áp nhanh ${bulkItems.value.length} giấy tờ cho toàn bộ các dòng lọc còn thiếu.`
    if (voiceLastAiResult.value) {
      voiceLastAiResult.value.items = [...bulkItems.value]
      voiceLastAiResult.value.reply = fb
    }
    toast.success('⚡ AI: Áp nhanh thành công', fb)
    speakVoiceFeedback(fb)
  } else if (pending.command === 'clear_items') {
    bulkItems.value = []
    const fb = 'Đã xóa sạch giấy tờ.'
    if (voiceLastAiResult.value) {
      voiceLastAiResult.value.items = []
      voiceLastAiResult.value.reply = fb
    }
    toast.info('⚡ AI: ' + fb)
    speakVoiceFeedback(fb)
  }

  // 2. Học từ nghe sai (Continuous Learning Memory)
  if (pending.rawPrompt !== pending.cleanedPrompt) {
    rememberVoiceCorrection(pending.rawPrompt, pending.cleanedPrompt)
  }
  saveVoiceMemory({
    id: Math.random().toString(36).slice(2, 9),
    time: new Date().toLocaleTimeString('vi-VN'),
    raw: pending.rawPrompt,
    fixed: pending.cleanedPrompt,
    action: pending.command,
    items: [...pending.items],
  })

  // 3. Lưu vĩnh viễn vào CSDL SQL Server để hệ thống tự động học tập (Dynamic Few-Shot Learning)
  try {
    void request('/api/summary/ai/memories', {
      method: 'POST',
      body: {
        rawSpeech: pending.rawPrompt,
        command: pending.command,
        items: pending.items,
        sheetNumber: pending.sheetNumber,
        targetRowNumber: pending.targetRowNumber,
        targetDocumentNumber: pending.targetDocumentNumber,
      },
    })
  } catch {}

  voicePendingAction.value = null
  scheduleVoiceInputClear()
}

function cancelVoicePendingAction(): void {
  voicePendingAction.value = null
  const fb = 'Đã hủy thao tác.'
  if (voiceLastAiResult.value) {
    voiceLastAiResult.value.reply = fb
  }
  toast.info('⚡ AI: ' + fb)
  speakVoiceFeedback(fb)
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
  voiceConsoleOpen.value = true
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
      speakVoiceFeedback('Chưa mở dòng nào để lưu.')
      return
    }
    if (!summaryDraft.value.trim()) {
      toast.info('Khung soạn trống', 'Không có gì để lưu.')
      speakVoiceFeedback('Khung soạn trống.')
      return
    }
    saveSummaryEditor(current)
    speakVoiceFeedback('Đã lưu dòng.')
    return
  }
  if (command === 'next') {
    const from = current?.rowNumber ?? rows.value[0]?.rowNumber ?? 0
    const next = nextMissingRow(from)
    if (!next) {
      toast.info('Hết dòng thiếu', 'Không còn dòng thiếu danh mục trong phạm vi đang xem.')
      speakVoiceFeedback('Không còn dòng thiếu.')
      return
    }
    openSummaryEditor(next)
    void scrollEditorIntoView()
    speakVoiceFeedback(`Dòng kế tiếp ${next.rowNumber}.`)
    return
  }
  if (command === 'prev') {
    const ordered = [...rows.value].sort((a, b) => a.rowNumber - b.rowNumber)
    const index = current ? ordered.findIndex((row) => row.rowNumber === current.rowNumber) : -1
    const previous = index > 0 ? ordered[index - 1] : null
    if (!previous) {
      toast.info('Đây là dòng đầu', 'Không còn dòng nào phía trước.')
      speakVoiceFeedback('Đây là dòng đầu tiên.')
      return
    }
    openSummaryEditor(previous)
    void scrollEditorIntoView()
    speakVoiceFeedback(`Dòng trước ${previous.rowNumber}.`)
    return
  }
  // "bộ bảy" — điền Bộ 7 chuẩn vào dòng đang mở.
  if (!current) {
    toast.info('Chưa mở dòng nào', 'Mở "Sửa toàn bộ" một dòng rồi nói "bộ bảy".')
    speakVoiceFeedback('Chưa mở dòng nào để điền.')
    return
  }
  voiceSuppressLearn = true
  setRowItems(current, [...STANDARD_TWELVE])
  voiceSuppressLearn = false
  openSummaryEditor(current)
  toast.success('Đã điền Bộ 12 chuẩn', 'Kiểm rồi bấm Lưu hoặc nói "xong".')
  speakVoiceFeedback('Đã điền bộ 12 chuẩn kết hôn.')
}

function findTargetRow(rowNum?: number | null, docNum?: number | null): SummaryRowResponse | null {
  if (rows.value.length === 0) return null

  // 1. Kiểm tra số hồ sơ (docNum hoặc rowNum nếu rowNum > 50 hoặc khớp số hồ sơ)
  const targetDoc = docNum ?? (rowNum != null && rowNum > 50 ? rowNum : null)
  if (targetDoc != null) {
    const strDoc = String(targetDoc)
    const match = rows.value.find((r) => {
      if (r.numberLabel === strDoc || r.documentNumber === strDoc) return true
      const m = (r.numberLabel || '').match(/\d+/)
      if (m && parseInt(m[0], 10) === targetDoc) return true
      const mDoc = (r.documentNumber || '').match(/\d+/)
      if (mDoc && parseInt(mDoc[0], 10) === targetDoc) return true
      return false
    })
    if (match) return match
  }

  // 2. Nếu có chỉ định dòng
  if (rowNum != null) {
    // 2.1. Nói "dòng cuối", "dòng cuối cùng" (quy ước 999)
    if (rowNum === 999 && rows.value.length > 0) {
      return rows.value[rows.value.length - 1]
    }

    // 2.2. Dòng 1 / dòng đầu tiên luôn là dòng đầu tiên hiển thị trên màn hình
    if (rowNum === 1) {
      return rows.value[0]
    }

    // 2.3. Dòng theo thứ tự hiển thị trên màn hình hiện tại (dòng 1 -> index 0, dòng 2 -> index 1)
    if (rowNum >= 1 && rowNum <= rows.value.length && rowNum <= 50) {
      return rows.value[rowNum - 1]
    }

    // 2.4. Dòng theo số dòng Excel thật (ví dụ dòng 1601)
    const byRowNumber = rows.value.find((r) => r.rowNumber === rowNum)
    if (byRowNumber) return byRowNumber

    // 2.5. Fallback nếu số rowNum trùng với số hồ sơ nào đó
    const strNum = String(rowNum)
    const byDocFallback = rows.value.find((r) => r.numberLabel === strNum || r.documentNumber === strNum)
    if (byDocFallback) return byDocFallback
  }

  return null
}

async function handleVoiceFinal(text: string): Promise<void> {
  const active = session.value
  if (!active) return

  const spokenText = text.trim()
  if (!spokenText) return
  voiceConsoleOpen.value = true
  voiceLastHeard.value = spokenText
  voiceInputText.value = spokenText

  // 0. Nếu đang có thao tác chờ xác nhận (Interactive Confirmation Turn)
  if (voicePendingAction.value) {
    const cleaned = preprocessSpokenText(spokenText).toLowerCase().trim()
    if (/^(?:đúng\s*rồi|đồng\s*ý|áp\s*đi|làm\s*đi|xác\s*nhận|chuẩn\s*rồi|chính\s*xác|có|phải|ok|tiến\s*hành|thực\s*hiện)$/i.test(cleaned)) {
      await executeVoicePendingAction()
      return
    }
    if (/^(?:không|hủy|thôi|dừng\s*lại|bỏ\s*qua|không\s*phải|đừng\s*làm)$/i.test(cleaned)) {
      cancelVoicePendingAction()
      return
    }
    // Nếu là câu lệnh mới khác, hủy lệnh chờ cũ để phục vụ câu lệnh mới
    voicePendingAction.value = null
  }

  try {
    // 1. Chỉ match nhanh các lệnh đơn giản (tiếp, xong, bộ bảy, quay lại)
    const learned = applyLearned(spokenText, voiceLearned.value)
    const context = {
      catalog: active.catalog,
      types: active.types.map((item) => item.name),
    }
    const result = parseVoiceTranscript(learned, context)

    if (result?.kind === 'command') {
      runVoiceCommand(result.command)
      return
    }

    // 2. Toàn bộ lệnh Lọc và Điền giấy tờ đều đưa qua AI Local (Ollama Qwen 2.5) để xử lý thông minh
    try {
    voiceAiParsing.value = true
    const aiRes = await request<SummaryAiParseVoiceResponse>('/api/summary/ai/parse-voice', {
      method: 'POST',
      body: {
        text: spokenText,
        catalog: active.catalog,
        model: 'qwen2.5:3b',
        learned: voiceLearned.value,
      },
    })

    if (aiRes?.command === 'confirm') {
      if (voicePendingAction.value) {
        await executeVoicePendingAction()
        return
      }
    }
    if (aiRes?.command === 'cancel') {
      if (voicePendingAction.value) {
        cancelVoicePendingAction()
        return
      }
    }

    if (aiRes?.needsConfirmation) {
      voiceLastAiModel.value = aiRes.model || 'qwen2.5:3b'
      voiceLastAiResult.value = {
        items: aiRes.items || [],
        command: aiRes.command,
        sheetNumber: aiRes.sheetNumber,
        model: aiRes.model || 'qwen2.5:3b',
        targetRowNumber: aiRes.targetRowNumber,
        targetLabel:
          aiRes.targetDocumentNumber != null
            ? `Hồ sơ ${aiRes.targetDocumentNumber}`
            : aiRes.targetRowNumber != null
            ? `Dòng ${aiRes.targetRowNumber}`
            : null,
        reply: aiRes.confirmationPrompt || aiRes.reply || null,
      }

      voicePendingAction.value = {
        rawPrompt: spokenText,
        cleanedPrompt: preprocessSpokenText(spokenText),
        command: aiRes.command || 'apply_missing',
        items: [...(aiRes.items || [])],
        targetRowNumber: aiRes.targetRowNumber,
        targetDocumentNumber: aiRes.targetDocumentNumber,
        sheetNumber: aiRes.sheetNumber,
        question: aiRes.confirmationPrompt || aiRes.reply || 'Bạn có muốn thực hiện thao tác này không?',
      }

      const promptToSpeak = aiRes.confirmationPrompt || aiRes.reply || 'Bạn có đồng ý áp dụng không?'
      speakVoiceFeedback(promptToSpeak)
      return
    }

    if (
      aiRes &&
      aiRes.success &&
      (aiRes.items.length > 0 ||
        aiRes.command ||
        aiRes.sheetNumber ||
        aiRes.targetRowNumber != null ||
        aiRes.targetDocumentNumber != null ||
        aiRes.year != null ||
        aiRes.type != null ||
        aiRes.numberFrom != null ||
        aiRes.numberTo != null ||
        Boolean(aiRes.reply))
    ) {
      voiceLastAiModel.value = aiRes.model || 'qwen2.5:3b'
      voiceLastAiResult.value = {
        items: aiRes.items || [],
        command: aiRes.command,
        sheetNumber: aiRes.sheetNumber,
        model: aiRes.model || 'qwen2.5:3b',
        targetRowNumber: aiRes.targetRowNumber,
        targetLabel:
          aiRes.targetDocumentNumber != null
            ? `Hồ sơ ${aiRes.targetDocumentNumber}`
            : aiRes.targetRowNumber != null
            ? `Dòng ${aiRes.targetRowNumber}`
            : null,
        reply: aiRes.reply || null,
      }

      // 1. Xử lý Lọc bảng dữ liệu bằng giọng nói (Năm, Loại, Khoảng số, Tìm kiếm)
      let filterChanged = false
      const filterParts: string[] = []

      // Bỏ lọc năm (-1) hoặc đổi năm
      if (aiRes.year === -1) {
        if (year.value !== '') {
          year.value = ''
          filterParts.push('Tất cả các năm')
          filterChanged = true
        }
      } else if (aiRes.year != null && aiRes.year !== year.value) {
        year.value = aiRes.year
        filterParts.push(`Năm ${aiRes.year}`)
        filterChanged = true
      }

      // Bỏ lọc loại ("") hoặc đổi loại
      if (aiRes.type === '') {
        if (type.value !== '') {
          type.value = ''
          filterParts.push('Tất cả loại')
          filterChanged = true
        }
      } else if (aiRes.type != null && aiRes.type !== type.value) {
        type.value = aiRes.type
        filterParts.push(`Loại ${aiRes.type}`)
        filterChanged = true
      }

      // Bỏ lọc số (-1) hoặc đổi khoảng số
      if (aiRes.numberFrom === -1 || aiRes.numberTo === -1) {
        if (numberMode.value !== 'all' || numberFrom.value !== '' || numberTo.value !== '') {
          numberMode.value = 'all'
          numberFrom.value = ''
          numberTo.value = ''
          singleNumber.value = ''
          filterParts.push('Tất cả số hồ sơ')
          filterChanged = true
        }
      } else if (aiRes.numberFrom != null || aiRes.numberTo != null) {
        if (aiRes.numberFrom != null && aiRes.numberTo != null && aiRes.numberFrom === aiRes.numberTo) {
          numberMode.value = 'single'
          singleNumber.value = aiRes.numberFrom
          filterParts.push(`Hồ sơ số ${aiRes.numberFrom}`)
        } else {
          numberMode.value = 'range'
          numberFrom.value = aiRes.numberFrom ?? ''
          numberTo.value = aiRes.numberTo ?? ''
          if (aiRes.numberFrom != null && aiRes.numberTo != null) {
            filterParts.push(`Hồ sơ từ số ${aiRes.numberFrom} đến ${aiRes.numberTo}`)
          } else if (aiRes.numberFrom != null) {
            filterParts.push(`Hồ sơ từ số ${aiRes.numberFrom}`)
          } else if (aiRes.numberTo != null) {
            filterParts.push(`Hồ sơ đến số ${aiRes.numberTo}`)
          }
        }
        filterChanged = true
      }

      // Tìm kiếm (Search)
      if (aiRes.search !== undefined && aiRes.search !== null) {
        search.value = aiRes.search
        filterParts.push(aiRes.search ? `Tìm kiếm: "${aiRes.search}"` : 'Xóa tìm kiếm')
        filterChanged = true
      }

      if (filterChanged) {
        page.value = 1
        void fetchRows()
        toast.success('⚡ AI: Đã lọc bảng', filterParts.join(' · '))
        speakVoiceFeedback(`Đã lọc: ${filterParts.join(', ')}.`)
      }

      // 2. Xử lý các lệnh điều khiển AI thay thế mọi thao tác con người trên trang
      if (aiRes.command) {
        // Phân trang bằng giọng nói
        if (aiRes.command === 'next_page') {
          if (page.value < totalPages.value) {
            goToPage(page.value + 1)
            toast.info(`⚡ AI: Trang ${page.value}/${totalPages.value}`)
            speakVoiceFeedback(`Chuyển sang trang ${page.value}.`)
          } else {
            toast.info('⚡ AI: Đang ở trang cuối')
            speakVoiceFeedback('Hiện đang ở trang cuối cùng.')
          }
          return
        }
        if (aiRes.command === 'prev_page') {
          if (page.value > 1) {
            goToPage(page.value - 1)
            toast.info(`⚡ AI: Trang ${page.value}/${totalPages.value}`)
            speakVoiceFeedback(`Lùi về trang ${page.value}.`)
          } else {
            toast.info('⚡ AI: Đang ở trang đầu')
            speakVoiceFeedback('Hiện đang ở trang đầu tiên.')
          }
          return
        }
        if (aiRes.command === 'first_page') {
          goToPage(1)
          toast.info('⚡ AI: Về trang 1')
          speakVoiceFeedback('Đã về trang đầu tiên.')
          return
        }
        if (aiRes.command === 'last_page') {
          goToPage(totalPages.value)
          toast.info(`⚡ AI: Về trang cuối (${totalPages.value})`)
          speakVoiceFeedback(`Đã chuyển tới trang cuối cùng.`)
          return
        }
        if (aiRes.command === 'goto_page' && aiRes.targetPage) {
          goToPage(aiRes.targetPage)
          toast.info(`⚡ AI: Chuyển trang ${aiRes.targetPage}`)
          speakVoiceFeedback(`Đã chuyển tới trang ${page.value}.`)
          return
        }

        // Chọn dòng & checkbox bằng giọng nói
        if (aiRes.command === 'select_all') {
          toggleSelectPage()
          toast.info('⚡ AI: Đã chọn tất cả dòng')
          speakVoiceFeedback('Đã tích chọn tất cả dòng trên trang.')
          return
        }
        if (aiRes.command === 'clear_selection') {
          clearSelection()
          toast.info('⚡ AI: Đã bỏ chọn')
          speakVoiceFeedback('Đã bỏ tích chọn tất cả dòng.')
          return
        }
        if (aiRes.command === 'select_row') {
          selected.value.clear()
          const targetRowNum = aiRes.targetRowNumber ?? 1
          const row = findTargetRow(targetRowNum, aiRes.targetDocumentNumber) || rows.value[0]
          if (row) {
            selected.value.add(row.rowNumber)
            openSummaryEditor(row)
            void scrollEditorIntoView()
            const label = row.numberLabel ? `Hồ sơ ${row.numberLabel}` : `Dòng ${row.rowNumber}`
            toast.info(`⚡ AI: Đã chọn ${label}`)
            const fb = `Đã chọn và mở ${label}.`
            if (voiceLastAiResult.value) {
              voiceLastAiResult.value.targetLabel = label
              voiceLastAiResult.value.targetRowNumber = row.rowNumber
              voiceLastAiResult.value.reply = fb
            }
            speakVoiceFeedback(fb)
          }
          return
        }
        if (aiRes.command === 'toggle_select_current') {
          const row = voiceTargetRow.value || rows.value[0]
          if (row) {
            toggleSelect(row.rowNumber)
            const isSel = selected.value.has(row.rowNumber)
            toast.info(isSel ? 'Đã tích dòng' : 'Đã bỏ tích dòng', `Dòng ${row.numberLabel || row.rowNumber}`)
            speakVoiceFeedback(isSel ? 'Đã tích chọn dòng này.' : 'Đã bỏ tích chọn dòng này.')
          }
          return
        }

        // Sao chép như dòng trên (tương đương phím C)
        if (aiRes.command === 'copy_above') {
          const row = voiceTargetRow.value || rows.value[0]
          if (row) {
            applyFromAbove(row)
            speakVoiceFeedback('Đã sao chép như dòng trên.')
          }
          return
        }

        // Bộ lọc dòng thiếu & trạng thái bằng giọng nói
        if (aiRes.command === 'filter_missing_sheet') {
          onlyMissingSheetNumber.value = !onlyMissingSheetNumber.value
          page.value = 1
          void fetchRows()
          speakVoiceFeedback(onlyMissingSheetNumber.value ? 'Đã lọc dòng thiếu số tờ.' : 'Đã bỏ lọc thiếu số tờ.')
          return
        }
        if (aiRes.command === 'filter_missing_doc') {
          onlyMissingDocumentNumber.value = !onlyMissingDocumentNumber.value
          page.value = 1
          void fetchRows()
          speakVoiceFeedback(onlyMissingDocumentNumber.value ? 'Đã lọc dòng thiếu hồ sơ số.' : 'Đã bỏ lọc thiếu hồ sơ số.')
          return
        }
        if (aiRes.command === 'filter_missing_items') {
          onlyMissingNumber.value = !onlyMissingNumber.value
          page.value = 1
          void fetchRows()
          speakVoiceFeedback(onlyMissingNumber.value ? 'Đã lọc các dòng thiếu giấy tờ.' : 'Đã bỏ lọc thiếu giấy tờ.')
          return
        }
        if (aiRes.command === 'missing_sheet_first') {
          missingSheetNumberFirst.value = !missingSheetNumberFirst.value
          page.value = 1
          void fetchRows()
          speakVoiceFeedback(missingSheetNumberFirst.value ? 'Đã ưu tiên hiển thị dòng thiếu số tờ.' : 'Bỏ ưu tiên thiếu số tờ.')
          return
        }
        if (aiRes.command === 'filter_existing') {
          onlyExisting.value = !onlyExisting.value
          page.value = 1
          void fetchRows()
          speakVoiceFeedback(onlyExisting.value ? 'Chỉ xem các dòng đã sửa.' : 'Xem toàn bộ các dòng.')
          return
        }
        if (aiRes.command === 'clear_filter_missing') {
          onlyMissingSheetNumber.value = false
          onlyMissingDocumentNumber.value = false
          onlyMissingNumber.value = false
          onlyExisting.value = false
          page.value = 1
          void fetchRows()
          speakVoiceFeedback('Đã bỏ tất cả bộ lọc thiếu.')
          return
        }

        // Lệnh Áp nhanh: Áp bộ 7 chuẩn trực tiếp vào dòng đang xử lý (ô vàng) hoặc các dòng đã tích
        if (aiRes.command === 'apply_fast') {
          const itemsToApply =
            aiRes.items && aiRes.items.length > 0 ? [...aiRes.items] : [...STANDARD_SEVEN]

          // 1. Nếu có các dòng đã tích checkbox: áp cho các dòng đã tích
          if (selected.value.size > 0 && aiRes.targetRowNumber == null && aiRes.targetDocumentNumber == null) {
            bulkItems.value = [...itemsToApply]
            applyBulk()
            const fb = `Đã áp nhanh ${itemsToApply.length} giấy tờ cho ${selected.value.size} dòng đã chọn.`
            if (voiceLastAiResult.value) {
              voiceLastAiResult.value.items = itemsToApply
              voiceLastAiResult.value.reply = fb
            }
            speakVoiceFeedback(fb)
            return
          }

          // 2. Áp cho dòng đích (dòng chỉ định, hoặc dòng đang mở sửa ô vàng, hoặc dòng đầu tiên)
          const targetRow =
            aiRes.targetRowNumber != null || aiRes.targetDocumentNumber != null
              ? findTargetRow(aiRes.targetRowNumber, aiRes.targetDocumentNumber)
              : voiceTargetRow.value || rows.value[0]

          if (targetRow) {
            setRowItems(targetRow, itemsToApply)
            openSummaryEditor(targetRow)
            void scrollEditorIntoView()
            const label = targetRow.numberLabel ? `Hồ sơ ${targetRow.numberLabel}` : `Dòng ${targetRow.rowNumber}`
            toast.success(`⚡ AI: Áp nhanh ${label}`, itemsToApply.join(', '))
            const fb = `Đã áp nhanh bộ 7 giấy tờ chuẩn vào ${label}.`
            if (voiceLastAiResult.value) {
              voiceLastAiResult.value.items = itemsToApply
              voiceLastAiResult.value.targetLabel = label
              voiceLastAiResult.value.targetRowNumber = targetRow.rowNumber
              voiceLastAiResult.value.reply = fb
            }
            speakVoiceFeedback(fb)
          }
          return
        }

        // Lệnh Hàng loạt: Áp nhanh / Áp vào dòng lọc còn thiếu (nút '✓ Áp dụng cho dòng lọc còn thiếu' trong thanh Áp nhanh)
        if (aiRes.command === 'apply_missing') {
          // Bỏ chọn dòng đơn lẻ để lệnh áp dụng cho TOÀN BỘ các dòng đang lọc còn thiếu
          selected.value.clear()
          if (aiRes.items && aiRes.items.length > 0) {
            bulkItems.value = [...aiRes.items]
          } else if (bulkItems.value.length === 0) {
            bulkItems.value = [...STANDARD_SEVEN]
          }
          await applyToMissingInScope()
          const fb = 'Đã áp nhanh bộ 7 giấy tờ chuẩn cho toàn bộ các dòng lọc còn thiếu.'
          if (voiceLastAiResult.value) {
            voiceLastAiResult.value.items = [...bulkItems.value]
            voiceLastAiResult.value.reply = fb
          }
          toast.success('⚡ AI: Áp nhanh hàng loạt', `Đã áp dụng ${bulkItems.value.length} giấy tờ vào các dòng còn thiếu trong danh sách đang lọc.`)
          speakVoiceFeedback(fb)
          return
        }

        // Lệnh Hàng loạt: Áp cho các dòng đã chọn
        if (aiRes.command === 'apply_selected') {
          if (aiRes.items && aiRes.items.length > 0) {
            bulkItems.value = [...aiRes.items]
          }
          applyBulk()
          speakVoiceFeedback('Đã áp dụng cho các dòng đã chọn.')
          return
        }

        // Lưu / Hoàn tác / Đồng bộ kho / Xuất file
        if (aiRes.command === 'undo') {
          undoEdits()
          toast.info('⚡ AI: Hoàn tác')
          speakVoiceFeedback('Đã hoàn tác.')
          return
        }
        if (aiRes.command === 'redo') {
          redoEdits()
          toast.info('⚡ AI: Làm lại')
          speakVoiceFeedback('Đã làm lại thao tác.')
          return
        }
        if (aiRes.command === 'revert_edits') {
          resetEdits()
          toast.warning('⚡ AI: Đã hủy thay đổi')
          speakVoiceFeedback('Đã hủy các thay đổi chưa lưu.')
          return
        }
        if (aiRes.command === 'sync_to_archive') {
          void doDirectSyncToArchive()
          toast.info('⚡ AI: Đồng bộ vào kho')
          speakVoiceFeedback('Đang tiến hành đồng bộ vào kho hồ sơ.')
          return
        }
        if (aiRes.command === 'export_file') {
          void quickExportFiltered()
          toast.info('⚡ AI: Đang xuất file Excel')
          speakVoiceFeedback('Đang xuất file Excel bản lọc.')
          return
        }
        if (aiRes.command === 'preview_export') {
          onlySelectedRows.value = false
          openPreview()
          toast.info('⚡ AI: Xem trước bản xuất')
          speakVoiceFeedback('Đã mở cửa sổ xem trước rồi xuất.')
          return
        }
        if (aiRes.command === 'close_modal') {
          closeSummaryEditor()
          previewOpen.value = false
          toast.info('⚡ AI: Đã đóng cửa sổ')
          speakVoiceFeedback('Đã đóng.')
          return
        }

        // Lệnh điều hướng dòng: Bộ bảy, bỏ bộ bảy, xong, tiếp, lùi
        if (aiRes.command === 'seven') runVoiceCommand('seven')
        else if (aiRes.command === 'clear_seven') {
          const row = voiceTargetRow.value || rows.value[0]
          if (row) {
            const currentItems = itemsFor(row)
            const remaining = currentItems.filter(
              (it) => !STANDARD_SEVEN.some((s) => s.toLowerCase() === it.toLowerCase()),
            )
            setRowItems(row, remaining)
            openSummaryEditor(row)
            void scrollEditorIntoView()
          }
          bulkItems.value = bulkItems.value.filter(
            (it) => !STANDARD_SEVEN.some((s) => s.toLowerCase() === it.toLowerCase()),
          )
          const fb = 'Đã bỏ bộ 7 giấy tờ chuẩn.'
          if (voiceLastAiResult.value) {
            voiceLastAiResult.value.items = []
            voiceLastAiResult.value.reply = fb
          }
          toast.info('⚡ AI: Đã bỏ bộ 7', 'Đã xóa bộ 7 giấy tờ chuẩn.')
          speakVoiceFeedback(fb)
          return
        }
        else if (aiRes.command === 'next') runVoiceCommand('next')
        else if (aiRes.command === 'done') runVoiceCommand('commit')
        else if (aiRes.command === 'prev') runVoiceCommand('prev')
      }

      // 3. Nếu là câu hỏi trò chuyện, chào hỏi, hoặc giải thích của AI (không có lệnh đổi bảng)
      if (
        !aiRes.command &&
        aiRes.items.length === 0 &&
        !aiRes.sheetNumber &&
        aiRes.targetRowNumber == null &&
        aiRes.targetDocumentNumber == null &&
        aiRes.reply
      ) {
        if (voiceLastAiResult.value) {
          voiceLastAiResult.value.reply = aiRes.reply
        }
        speakVoiceFeedback(aiRes.reply)
        toast.info(`⚡ AI (${aiRes.model ?? 'Qwen 2.5'}): Trả lời`, aiRes.reply)
        return
      }

      // 4. Xử lý điền giấy tờ và số tờ vào dòng đích (CỘNG DỒN / XÓA / SỐ TỜ)
      if (
        aiRes.items.length > 0 ||
        aiRes.sheetNumber ||
        aiRes.targetRowNumber != null ||
        aiRes.targetDocumentNumber != null ||
        aiRes.command === 'clear_items' ||
        aiRes.command === 'remove_item'
      ) {
        let row: SummaryRowResponse | null = null

        // Ưu tiên dòng hoặc hồ sơ chỉ định đích danh (ví dụ "hồ sơ 5192", "dòng 1", "dòng đầu tiên")
        if (aiRes.targetDocumentNumber != null || aiRes.targetRowNumber != null) {
          row = findTargetRow(aiRes.targetRowNumber, aiRes.targetDocumentNumber)
          if (!row) {
            const targetDesc = aiRes.targetDocumentNumber
              ? `Hồ sơ ${aiRes.targetDocumentNumber}`
              : `Dòng ${aiRes.targetRowNumber}`
            toast.warning('Dòng / hồ sơ không có trong trang', `Không tìm thấy ${targetDesc} trong trang hiện tại.`)
          }
        }

        // Nếu không chỉ định dòng: dùng dòng đang mở editor
        if (!row) {
          row = voiceTargetRow.value
        }
        // Nếu vẫn chưa có dòng: tự nhảy vào dòng thiếu kế tiếp bắt đầu từ đầu trang (dòng 1)
        if (!row) {
          row = nextMissingRow(0) || rows.value[0]
        }

        if (row) {
          voiceSuppressLearn = true

          // Xử lý Số tờ (SheetNumber)
          const validSheet =
            aiRes.sheetNumber &&
            aiRes.sheetNumber.trim() &&
            !/^\d{4}$/.test(aiRes.sheetNumber.trim())
              ? aiRes.sheetNumber.trim()
              : null
          if (validSheet) {
            commit(row, { sheetNumber: validSheet })
          }

          // Xử lý Giấy tờ (Items): Xóa hết, Gỡ bỏ, Thay bằng, hoặc CỘNG DỒN (Stacking)
          if (aiRes.command === 'clear_items') {
            setRowItems(row, [])
            toast.info('Đã xóa hết giấy tờ', `Dòng ${row.numberLabel || row.rowNumber}`)
          } else if (aiRes.command === 'remove_item' && aiRes.items.length > 0) {
            const current = itemsFor(row)
            const remaining = current.filter((it) => !aiRes.items.includes(it))
            setRowItems(row, remaining)
            toast.info('Đã bỏ giấy tờ', `${aiRes.items.join(', ')}`)
          } else if (aiRes.command === 'replace_items' && aiRes.items.length > 0) {
            setRowItems(row, aiRes.items)
          } else if (aiRes.items.length > 0) {
            // MẶC ĐỊNH: CỘNG DỒN (STACKING) — Giữ nguyên giấy tờ cũ và thêm giấy tờ mới vào
            const current = itemsFor(row)
            const merged = [...current]
            for (const it of aiRes.items) {
              if (!merged.includes(it)) {
                merged.push(it)
              }
            }
            setRowItems(row, merged)
          }

          voiceSuppressLearn = false

          openSummaryEditor(row)
          void scrollEditorIntoView()
          voiceLearnCandidate.value = {
            rowNumber: row.rowNumber,
            heard: spokenText,
            prefill: summaryFor(row),
          }
          // Phát âm thanh phản hồi bằng giọng nói tiếng Việt
          let spokenFeedback = ''
          if (aiRes.targetDocumentNumber != null) {
            spokenFeedback = `Hồ sơ ${aiRes.targetDocumentNumber}: `
          } else if (aiRes.targetRowNumber != null) {
            spokenFeedback = `Dòng ${row.rowNumber}: `
          }
          if (aiRes.command === 'clear_items') {
            spokenFeedback += 'Đã xóa toàn bộ giấy tờ.'
          } else if (aiRes.command === 'remove_item') {
            spokenFeedback += `Đã bỏ ${aiRes.items.join(', ')}.`
          } else if (aiRes.items.length > 0) {
            spokenFeedback += `Đã thêm ${aiRes.items.join(', ')}.`
            if (aiRes.sheetNumber) spokenFeedback += `, số tờ ${aiRes.sheetNumber}`
          } else if (aiRes.sheetNumber) {
            spokenFeedback += `Số tờ ${aiRes.sheetNumber}`
          }
          if (aiRes.command === 'next') spokenFeedback += spokenFeedback ? ', chuyển dòng kế tiếp.' : 'Chuyển dòng kế tiếp.'
          else if (aiRes.command === 'done' || aiRes.command === 'commit') spokenFeedback += spokenFeedback ? ', đã lưu dòng.' : 'Đã lưu dòng.'
          else if (aiRes.command === 'seven') spokenFeedback = 'Đã điền bộ 7 chuẩn kết hôn.'

          const finalReply = aiRes.reply || spokenFeedback
          const rowItems = itemsFor(row)
          voiceLastAiResult.value = {
            items: rowItems,
            command: aiRes.command,
            sheetNumber: validSheet,
            model: aiRes.model || 'qwen2.5:3b',
            targetRowNumber: row.rowNumber,
            targetLabel: row.numberLabel ? `HS ${row.numberLabel}` : `Dòng ${row.rowNumber}`,
            reply: finalReply,
          }

          toast.success(
            `⚡ AI (${aiRes.model ?? 'Qwen 2.5'}): Hồ sơ ${row.numberLabel || row.rowNumber}`,
            `${rowItems.length > 0 ? rowItems.join(', ') : '(đã mở dòng)'}${
              validSheet ? ' · số tờ ' + validSheet : ''
            }`,
          )

          if (finalReply) {
            speakVoiceFeedback(finalReply)
          }
        }
      }
      return
    }
  } catch (err) {
    console.warn('Ollama AI voice parse fallback to regex parser:', err)
  } finally {
    voiceAiParsing.value = false
  }

  // 3. Dự phòng 100% bằng Parser Regex cục bộ nếu AI không phản hồi hoặc không nhận ra
  if (!result) {
    toast.info(
      'Không hiểu câu này',
      `"${text}" — nói loại hồ sơ + giấy tờ ("kết hôn — căn cước, hộ khẩu") hoặc lệnh "xong", "kế tiếp".`,
    )
    speakVoiceFeedback('Không nhận diện được giấy tờ trong câu nói.')
    return
  }

  if (result.kind === 'filter') {
    if (result.type !== null) type.value = result.type
    if (result.year !== null) year.value = result.year
    const parts: string[] = []
    if (result.type) parts.push(`loại ${result.type}`)
    if (result.year !== null) parts.push(`năm ${result.year}`)
    toast.success('Đã lọc bằng giọng nói', parts.length > 0 ? parts.join(' · ') : 'bỏ lọc loại')
    speakVoiceFeedback('Đã lọc theo yêu cầu.')
    return
  }

  // Prefill: điền vào dòng đang mở, hoặc dòng thiếu kế tiếp nếu chưa mở ai.
  let row = voiceTargetRow.value
  if (!row) row = nextMissingRow(rows.value[0]?.rowNumber ?? 0)
  if (!row) {
    toast.info('Không có dòng để điền', 'Bấm "Sửa toàn bộ" một dòng rồi nói tiếp.')
    speakVoiceFeedback('Chưa chọn dòng để điền.')
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
  voiceLastAiResult.value = {
    items: result.items,
    command: null,
    sheetNumber: result.sheetNumber,
    model: 'Bộ tách quy tắc (Regex)',
    targetRowNumber: row.rowNumber,
  }

  toast.success(
    'Đã điền — kiểm rồi lưu',
    `Dòng ${row.rowNumber}: ${result.items.join(', ') || '(giữ nguyên danh mục)'}${
      result.sheetNumber ? ' · số tờ ' + result.sheetNumber : ''
    }`,
  )

  let spokenRegexFeedback = ''
  if (result.items.length > 0) {
    spokenRegexFeedback = `Đã điền: ${result.items.join(', ')}`
    if (result.sheetNumber) spokenRegexFeedback += `, số tờ ${result.sheetNumber}`
  }
  if (spokenRegexFeedback) {
    speakVoiceFeedback(spokenRegexFeedback)
  }
  } finally {
    scheduleVoiceInputClear()
  }
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

async function resetEdits(): Promise<void> {
  if (edits.value.size === 0) return
  const ok = await confirmDialog({
    title: 'Bỏ thay đổi?',
    message: 'Bỏ toàn bộ thay đổi chưa xuất? Dữ liệu chưa lưu vào file sẽ bị mất.',
    variant: 'warning',
    confirmText: 'Bỏ toàn bộ thay đổi',
  })
  if (!ok) return
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

  let makeActiveParam: boolean | undefined = undefined

  if (activeTeamSession.value?.hasActiveSession) {
    if (isLiveOwnerOrAdmin.value) {
      const replace = await confirmDialog({
        title: 'Thay thế file đang LIVE?',
        message: `Phòng đang có file live "${activeTeamSession.value.fileName}". Bạn có muốn ĐẶT FILE MỚI NÀY LÀM FILE CHUNG THAY THẾ cho cả phòng không? (Nếu chọn "Chỉ mở riêng trên máy tôi", file chung đang live vẫn được giữ nguyên)`,
        variant: 'warning',
        confirmText: 'Thay thế làm file chung',
        cancelText: 'Chỉ mở riêng trên máy tôi',
      })
      makeActiveParam = replace
    } else {
      makeActiveParam = false
      toast.info('Đang mở file riêng', 'File làm việc chung của phòng vẫn được giữ nguyên cho mọi người.')
    }
  } else {
    makeActiveParam = true
  }

  inspecting.value = true
  inspectError.value = null
  const previousSession = session.value?.sessionId

  try {
    const form = new FormData()
    form.append('file', file)
    const inspectUrl = makeActiveParam !== undefined
      ? `/api/summary/inspect?makeActive=${makeActiveParam}`
      : '/api/summary/inspect'

    const data = await request<SummaryInspectResponse>(inspectUrl, {
      method: 'POST',
      form,
    })

    if (previousSession && previousSession !== activeTeamSession.value?.sessionId) {
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
    resetScopeFilters()
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
    sortByNumber: sortByNumber.value || undefined,
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

async function closeSession(): Promise<void> {
  const active = session.value
  if (!active) return

  const isLive = isCurrentSessionActiveTeam.value

  if (changedCount.value > 0) {
    const ok = await confirmDialog({
      title: isLive ? 'Rời phiên làm việc?' : 'Đóng workbook?',
      message: isLive
        ? 'Bạn có một số thay đổi chưa xuất. Bạn có chắc muốn rời màn hình xem? File vẫn tiếp tục LIVE trên hệ thống cho cả phòng và bạn có thể quay lại bất cứ lúc nào.'
        : 'Có thay đổi chưa xuất. Bạn có chắc muốn bỏ các thay đổi này và đóng workbook?',
      variant: 'warning',
      confirmText: isLive ? 'Rời phiên' : 'Bỏ thay đổi và đóng',
    })
    if (!ok) return
  }

  leaveSummarySession(active.sessionId, myActorName.value ?? 'Quản trị')

  // TUYỆT ĐỐI KHÔNG XÓA PHIÊN LIVE TRÊN SERVER KHI ĐÓNG XEM!
  if (!isLive) {
    void deleteSessionSilent(active.sessionId)
  }

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
  resetScopeFilters()

  if (isLive) {
    toast.info(
      'Đã rời màn hình xem',
      'File vẫn đang LIVE cho cả phòng. Bạn có thể bấm "Vào làm việc chung ngay" để quay lại.',
    )
  }
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

function extractParensItems(text: string): string[] {
  const match = text.match(/\(([^)]+)\)\s*$/)
  if (!match) return []
  return match[1].split(/[,;]/).map((s) => s.trim()).filter(Boolean)
}

interface SyncRowCandidate {
  rowNumber: number
  sourceRowNumber?: number
  sheet?: string
  summary: string
  numberLabel?: string | null
}

function updateSyncSheetName(candidates?: SyncRowCandidate[]): void {
  // 1. Nếu có hàng được chọn và hàng đó có tên sheet rõ ràng (phiên nhiều sheet), ưu tiên lấy đúng sheet đó
  if (candidates && candidates.length > 0) {
    const rowWithSheet = candidates.find((c) => c.sheet && c.sheet.trim())
    if (rowWithSheet && rowWithSheet.sheet) {
      selectedSyncSheet.value = rowWithSheet.sheet.trim()
      return
    }
  }

  // 2. Tìm trong danh sách dòng hiện có trên bảng
  const tableRowWithSheet = rows.value.find((r) => r.sheet && r.sheet.trim())
  if (tableRowWithSheet && tableRowWithSheet.sheet) {
    selectedSyncSheet.value = tableRowWithSheet.sheet.trim()
    return
  }

  // 3. Theo năm lọc
  if (scopeRequest.value.year && scopeRequest.value.year >= 1996) {
    selectedSyncSheet.value = String(scopeRequest.value.year)
  } else if (scopeRequest.value.year && scopeRequest.value.year < 1996) {
    selectedSyncSheet.value = 'file nhập'
  } else if (session.value?.sheetName && session.value.sheetName !== 'file nhập') {
    selectedSyncSheet.value = session.value.sheetName
  } else if (session.value?.sheetName) {
    selectedSyncSheet.value = session.value.sheetName
  } else {
    selectedSyncSheet.value = '1997'
  }
}

function prepareRowsForOneDriveSync(items: SyncRowCandidate[]): AutoTickSyncSummaryItem[] {
  return items.map((it) => {
    const change = allChanges.value.find((c) => c.rowNumber === it.rowNumber)
    const effectiveSummary = change?.summaryText || it.summary
    const itemsList = change?.items && change.items.length > 0
      ? change.items
      : extractParensItems(effectiveSummary)

    // Ưu tiên dòng thật Excel (>= 5), nếu không có mới lấy rowNumber
    const actualRowIndex = (it.sourceRowNumber && it.sourceRowNumber >= 5)
      ? it.sourceRowNumber
      : it.rowNumber

    return {
      rowIndex: actualRowIndex,
      sheetName: it.sheet || undefined,
      numberLabel: change?.numberLabel || it.numberLabel || '',
      summaryText: effectiveSummary,
      formulaH: effectiveSummary,
      statusA: syncStatusA.value || 'đã nhập',
      items: itemsList,
    }
  })
}

function startSyncToOneDriveFromPreview(): void {
  const items = previewRows.value
  if (!items || items.length === 0) {
    toast.warning('Không có dòng nào', 'Vui lòng chọn hoặc lọc các dòng cần đồng bộ.')
    return
  }
  if (numberMode.value === 'range' && (numberFrom.value !== '' || numberTo.value !== '')) {
    oneDriveSyncScope.value = 'number_range'
    syncNumberFrom.value = numberFrom.value !== '' ? numberFrom.value : ''
    syncNumberTo.value = numberTo.value !== '' ? numberTo.value : ''
  } else if (edits.value.size > 0) {
    oneDriveSyncScope.value = 'changes_only'
  } else if (selected.value.size > 0) {
    oneDriveSyncScope.value = 'selected'
  } else {
    oneDriveSyncScope.value = 'number_range'
  }
  updateSyncRowsPreview()
  oneDriveSyncResult.value = null
  oneDriveSyncError.value = null
  oneDriveSyncModalOpen.value = true
}

function updateSyncRowsPreview(): void {
  let targetItems: SyncRowCandidate[] = []

  if (oneDriveSyncScope.value === 'number_range') {
    // 0. Lọc theo khoảng số hồ sơ (Từ số - Đến số)
    const allPool: SyncRowCandidate[] = []
    const seen = new Set<number>()
    const poolRows = previewOpen.value && previewRows.value.length > 0 ? previewRows.value : rows.value
    for (const r of poolRows) {
      if (!seen.has(r.rowNumber)) {
        seen.add(r.rowNumber)
        const change = allChanges.value.find((c) => c.rowNumber === r.rowNumber)
        const matchingRow = rows.value.find((x) => x.rowNumber === r.rowNumber)
        allPool.push({
          rowNumber: r.rowNumber,
          sourceRowNumber: r.sourceRowNumber || matchingRow?.sourceRowNumber,
          sheet: r.sheet || matchingRow?.sheet,
          summary: change?.summaryText || r.summary,
          numberLabel: change?.numberLabel || r.numberLabel,
        })
      }
    }
    for (const ed of edits.value.values()) {
      if (!seen.has(ed.row.rowNumber)) {
        seen.add(ed.row.rowNumber)
        allPool.push({
          rowNumber: ed.row.rowNumber,
          sourceRowNumber: ed.row.sourceRowNumber,
          sheet: ed.row.sheet,
          summary: ed.summaryText || composeSummary(ed.row, ed.items) || ed.row.summary,
          numberLabel: ed.numberLabel || ed.row.numberLabel,
        })
      }
    }

    const fromNum = syncNumberFrom.value !== '' ? Number(syncNumberFrom.value) : null
    const toNum = syncNumberTo.value !== '' ? Number(syncNumberTo.value) : null

    const getItemNumber = (item: SyncRowCandidate): number | null => {
      const match = (item.numberLabel || '').match(/\d+/)
      return match ? parseInt(match[0], 10) : (item.rowNumber ?? null)
    }

    let startIndex = 0
    if (fromNum !== null) {
      const idx = allPool.findIndex((item) => getItemNumber(item) === fromNum)
      if (idx !== -1) {
        startIndex = idx
      } else {
        const fallbackIdx = allPool.findIndex((item) => {
          const n = getItemNumber(item)
          return n !== null && n >= fromNum
        })
        startIndex = fallbackIdx !== -1 ? fallbackIdx : 0
      }
    }

    let endIndex = allPool.length - 1
    if (toNum !== null) {
      let foundIdx = -1
      for (let i = startIndex; i < allPool.length; i++) {
        if (getItemNumber(allPool[i]) === toNum) {
          foundIdx = i
          break
        }
      }
      if (foundIdx !== -1) {
        endIndex = foundIdx
      } else {
        const anyIdx = allPool.findIndex((item) => getItemNumber(item) === toNum)
        if (anyIdx !== -1 && anyIdx >= startIndex) {
          endIndex = anyIdx
        } else {
          for (let i = startIndex; i < allPool.length; i++) {
            const n = getItemNumber(allPool[i])
            if (n !== null && n >= toNum) {
              endIndex = i
              break
            }
          }
        }
      }
    }

    if (startIndex <= endIndex && allPool.length > 0) {
      targetItems = allPool.slice(startIndex, endIndex + 1)
    } else {
      targetItems = []
    }
  } else if (oneDriveSyncScope.value === 'selected' && selected.value.size > 0) {
    // 1. Người dùng chọn: Chỉ các dòng đã tích chọn ở checkbox đầu dòng
    if (previewOpen.value && previewRows.value.length > 0) {
      targetItems = previewRows.value
        .filter((r) => selected.value.has(r.rowNumber))
        .map((r) => {
          const change = allChanges.value.find((c) => c.rowNumber === r.rowNumber)
          const matchingRow = rows.value.find((x) => x.rowNumber === r.rowNumber)
          return {
            rowNumber: r.rowNumber,
            sourceRowNumber: r.sourceRowNumber || matchingRow?.sourceRowNumber,
            sheet: r.sheet || matchingRow?.sheet,
            summary: change?.summaryText || r.summary,
            numberLabel: change?.numberLabel || r.numberLabel,
          }
        })
    } else {
      targetItems = rows.value
        .filter((r) => selected.value.has(r.rowNumber))
        .map((r) => {
          const change = allChanges.value.find((c) => c.rowNumber === r.rowNumber)
          return {
            rowNumber: r.rowNumber,
            sourceRowNumber: r.sourceRowNumber,
            sheet: r.sheet,
            summary: change?.summaryText || composeSummary(r, change?.items || r.items) || r.summary,
            numberLabel: change?.numberLabel || r.numberLabel,
          }
        })
    }
    for (const ed of edits.value.values()) {
      if (selected.value.has(ed.row.rowNumber) && !targetItems.some((t) => t.rowNumber === ed.row.rowNumber)) {
        targetItems.push({
          rowNumber: ed.row.rowNumber,
          sourceRowNumber: ed.row.sourceRowNumber,
          sheet: ed.row.sheet,
          summary: ed.summaryText || composeSummary(ed.row, ed.items) || ed.row.summary,
          numberLabel: ed.numberLabel || ed.row.numberLabel,
        })
      }
    }
  } else if (oneDriveSyncScope.value === 'changes_only' && edits.value.size > 0) {
    targetItems = [...edits.value.values()].map((ed) => ({
      rowNumber: ed.row.rowNumber,
      sourceRowNumber: ed.row.sourceRowNumber,
      sheet: ed.row.sheet,
      summary: ed.summaryText || composeSummary(ed.row, ed.items) || ed.row.summary,
      numberLabel: ed.numberLabel || ed.row.numberLabel,
    }))
  } else if (previewOpen.value && previewRows.value.length > 0) {
    targetItems = previewRows.value.map((r) => {
      const change = allChanges.value.find((c) => c.rowNumber === r.rowNumber)
      const matchingRow = rows.value.find((x) => x.rowNumber === r.rowNumber)
      return {
        rowNumber: r.rowNumber,
        sourceRowNumber: r.sourceRowNumber || matchingRow?.sourceRowNumber,
        sheet: r.sheet || matchingRow?.sheet,
        summary: change?.summaryText || r.summary,
        numberLabel: change?.numberLabel || r.numberLabel,
      }
    })
  } else if (rows.value.length > 0) {
    targetItems = rows.value.map((r) => {
      const change = allChanges.value.find((c) => c.rowNumber === r.rowNumber)
      return {
        rowNumber: r.rowNumber,
        sourceRowNumber: r.sourceRowNumber,
        sheet: r.sheet,
        summary: change?.summaryText || r.summary,
        numberLabel: change?.numberLabel || r.numberLabel,
      }
    })
    for (const ed of edits.value.values()) {
      if (!targetItems.some((t) => t.rowNumber === ed.row.rowNumber)) {
        targetItems.push({
          rowNumber: ed.row.rowNumber,
          sourceRowNumber: ed.row.sourceRowNumber,
          sheet: ed.row.sheet,
          summary: ed.summaryText || composeSummary(ed.row, ed.items) || ed.row.summary,
          numberLabel: ed.numberLabel || ed.row.numberLabel,
        })
      }
    }
  } else if (edits.value.size > 0) {
    targetItems = [...edits.value.values()].map((ed) => ({
      rowNumber: ed.row.rowNumber,
      sourceRowNumber: ed.row.sourceRowNumber,
      sheet: ed.row.sheet,
      summary: ed.summaryText || composeSummary(ed.row, ed.items) || ed.row.summary,
      numberLabel: ed.numberLabel || ed.row.numberLabel,
    }))
  }

  updateSyncSheetName(targetItems)
  rowsToSyncPreview.value = prepareRowsForOneDriveSync(targetItems)
}

function openOneDriveSyncFromToolbar(): void {
  if (numberMode.value === 'range' && (numberFrom.value !== '' || numberTo.value !== '')) {
    oneDriveSyncScope.value = 'number_range'
    syncNumberFrom.value = numberFrom.value !== '' ? numberFrom.value : ''
    syncNumberTo.value = numberTo.value !== '' ? numberTo.value : ''
  } else if (edits.value.size > 0) {
    oneDriveSyncScope.value = 'changes_only'
  } else if (selected.value.size > 0) {
    oneDriveSyncScope.value = 'selected'
  } else {
    oneDriveSyncScope.value = 'number_range'
  }

  updateSyncRowsPreview()

  if (rowsToSyncPreview.value.length === 0) {
    toast.warning('Chưa có dữ liệu', 'Hãy lọc hoặc chọn hồ sơ để đồng bộ lên OneDrive.')
    return
  }

  oneDriveSyncResult.value = null
  oneDriveSyncError.value = null
  oneDriveSyncModalOpen.value = true
}

async function executeOneDriveSync(): Promise<void> {
  if (rowsToSyncPreview.value.length === 0 || syncingOneDrive.value) return

  if (!oneDriveUrl.value.trim()) {
    toast.warning('Thiếu liên kết', 'Vui lòng nhập hoặc dán liên kết OneDrive / Excel Online.')
    return
  }

  syncingOneDrive.value = true
  oneDriveSyncError.value = null
  oneDriveSyncResult.value = null

  localStorage.setItem(ONEDRIVE_STORAGE_KEY, oneDriveUrl.value.trim())

  try {
    const payload: AutoTickSyncSummaryRequest = {
      oneDriveUrl: oneDriveUrl.value.trim(),
      sheetName: selectedSyncSheet.value.trim() || '1997',
      rows: rowsToSyncPreview.value,
      ensureFormulaH: false,
      skipTicking: true,
      statusA: syncStatusA.value || 'đã nhập',
      numberFrom: oneDriveSyncScope.value === 'number_range' && syncNumberFrom.value !== '' ? Number(syncNumberFrom.value) : undefined,
      numberTo: oneDriveSyncScope.value === 'number_range' && syncNumberTo.value !== '' ? Number(syncNumberTo.value) : undefined,
    }

    const res = await request<AutoTickSyncSummaryResponse>('/api/auto-tick/sync-summary?background=true', {
      method: 'POST',
      body: payload,
    })

    oneDriveSyncResult.value = res
    // Đóng popup ngay lập tức để người dùng không phải chờ, chuyển sang chạy ngầm
    oneDriveSyncModalOpen.value = false
    syncingInBackground.value = true
    if (res.isQueued) {
      isQueuedForOneDrive.value = true
      toast.info('Đã vào hàng chờ đồng bộ', res.message)
    } else {
      isQueuedForOneDrive.value = false
      toast.success('Đã bắt đầu chạy ngầm trên OneDrive!', res.message)
    }
  } catch (err) {
    oneDriveSyncError.value = errorMessage(err)
    toast.error('Lỗi khởi động đồng bộ OneDrive', oneDriveSyncError.value)
  } finally {
    syncingOneDrive.value = false
  }
}

async function copySyncRowsToClipboard(): Promise<void> {
  if (rowsToSyncPreview.value.length === 0) return
  try {
    const tsvData = rowsToSyncPreview.value.map((r) => r.summaryText).join('\n')
    await navigator.clipboard.writeText(tsvData)
    toast.success(
      'Đã copy Trích yếu vào Clipboard!',
      `Đã sao chép ${rowsToSyncPreview.value.length} dòng. Chỉ cần click ô đầu tiên trên Cột D của Google Sheets rồi bấm Ctrl + V!`
    )
  } catch (err) {
    toast.error('Không thể copy vào clipboard', errorMessage(err))
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
    const ok = await confirmDialog({
      title: 'Khôi phục bản nháp?',
      message: `Tìm thấy bản nháp ${payload.edits.length} dòng cho file này (${source}, lúc ${label}). Bạn có muốn khôi phục lại không?`,
      variant: 'primary',
      confirmText: 'Khôi phục bản nháp',
      cancelText: 'Bỏ qua',
    })
    if (!ok) {
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
  // Khi AI đang nói mà người dùng bấm phím bất kỳ (Space, Esc, Enter...) -> Câm họng AI ngay lập tức
  if (isAiSpeaking.value && (event.key === ' ' || event.key === 'Escape' || event.key === 'Enter')) {
    stopAnySpeech()
  }

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

  // Push-to-talk: Giữ phím Cách (Space) để nói (khi không đứng trong ô nhập text)
  if (event.code === 'Space' && !event.repeat && !event.ctrlKey && !event.metaKey && !event.altKey) {
    stopAnySpeech()
    if (session.value && voiceSupported.value && !voiceListening.value) {
      event.preventDefault()
      voicePttActive.value = true
      startVoice()
      return
    }
  }

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

function onGlobalKeyup(event: KeyboardEvent): void {
  if (event.code === 'Space' && voicePttActive.value) {
    event.preventDefault()
    voicePttActive.value = false
    stopVoice()
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

watch([numberMode, numberFrom, numberTo, singleNumber, sortByNumber], () => {
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
  void fetchSharedEdits(evt.sessionId).catch(() => {})
  toast.info('Đồng đội cập nhật', `${evt.actor} vừa áp dụng thay đổi cho ${formatNumber(evt.count)} dòng.`)
})

// ===== Quản lý an toàn phiên làm việc nhóm & Switcher =====
const incomingTeamSession = ref<SummaryActiveSessionResponse | null>(null)
const incomingSessionModalOpen = ref(false)
const showIncomingSessionBanner = ref(false)
const sessionSwitcherOpen = ref(false)
const pendingSwitchTarget = ref<SummarySessionInfoResponse | null>(null)
const safetyConfirmModalOpen = ref(false)
const switchingSession = ref(false)

function openSessionSwitcher() {
  loadResumeCandidates()
  sessionSwitcherOpen.value = true
}

async function doSwitchToSession(targetSessionId: string, syncArchiveFirst = false) {
  switchingSession.value = true
  try {
    if (session.value) {
      // 1. Tự động lưu bản nháp của phiên hiện tại lên server
      try {
        saveDraft()
        await syncServerDraft()
      } catch {}

      // 2. Nạp kho trước nếu người dùng yêu cầu
      if (syncArchiveFirst) {
        try {
          await request(`/api/summary/${session.value.sessionId}/sync-to-archive`, { method: 'POST' })
          toast.success('Đã nạp vào Kho hồ sơ', 'Dữ liệu phiên cũ đã được lưu trữ an toàn.')
        } catch (e) {
          toast.error('Nạp kho chưa hoàn tất', errorMessage(e))
        }
      }
    }

    sessionSwitcherOpen.value = false
    incomingSessionModalOpen.value = false
    showIncomingSessionBanner.value = false
    safetyConfirmModalOpen.value = false
    pendingSwitchTarget.value = null

    const ok = await restoreFromServer(targetSessionId)
    if (ok) {
      toast.success('Đã chuyển phiên làm việc', 'Toàn bộ dữ liệu phiên mới đã sẵn sàng.')
    }
  } finally {
    switchingSession.value = false
  }
}

function requestSwitchToSession(target: SummarySessionInfoResponse) {
  if (target.sessionId === session.value?.sessionId) return

  // Nếu phiên hiện tại có thay đổi chưa nạp kho: hiện modal an toàn
  if (session.value && changedCount.value > 0) {
    pendingSwitchTarget.value = target
    safetyConfirmModalOpen.value = true
    return
  }

  void doSwitchToSession(target.sessionId, false)
}

function handleAcceptIncomingTeamSession() {
  if (!incomingTeamSession.value?.sessionId) return
  if (session.value && changedCount.value > 0) {
    pendingSwitchTarget.value = {
      sessionId: incomingTeamSession.value.sessionId,
      fileName: incomingTeamSession.value.fileName ?? 'File làm chung',
      fileSizeBytes: 0,
      createdAt: incomingTeamSession.value.uploadedAt ?? new Date().toISOString(),
      rowCount: incomingTeamSession.value.rowCount,
    }
    incomingSessionModalOpen.value = false
    safetyConfirmModalOpen.value = true
    return
  }

  void doSwitchToSession(incomingTeamSession.value.sessionId, false)
}

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
      if (!session.value) {
        // Chưa mở file nào: tự động nạp luôn
        toast.info(
          'File làm chung mới',
          `${evt.uploadedBy} vừa mở file "${evt.fileName}" cho cả phòng cùng làm. Đang tự nạp...`,
        )
        await restoreFromServer(evt.sessionId)
      } else {
        // Đang mở file khác: KHÔNG cưỡng bức chuyển, hiện banner & modal hỏi ý kiến
        incomingTeamSession.value = activeTeamSession.value
        incomingSessionModalOpen.value = true
        showIncomingSessionBanner.value = true
        toast.info(
          'Có file chung mới',
          `${evt.uploadedBy} vừa chia sẻ file "${evt.fileName}". Bạn có thể chọn chuyển hoặc tiếp tục làm file hiện tại.`,
        )
      }
    }
  } else {
    activeTeamSession.value = null
    incomingTeamSession.value = null
    incomingSessionModalOpen.value = false
    showIncomingSessionBanner.value = false
  }
})

let presenceCleanTimer: number | undefined

onMounted(() => {
  initVoices()
  void fetchCatalogPresets()
  window.addEventListener('keydown', onGlobalKeydown)
  window.addEventListener('keyup', onGlobalKeyup)
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
  window.removeEventListener('keyup', onGlobalKeyup)
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

        <!-- Banner nổi bật nếu đang có file LIVE của cả phòng -->
        <div
          v-if="activeTeamSession && activeTeamSession.hasActiveSession"
          class="card active-live-room-banner"
        >
          <div class="card-body stack" style="gap: 12px">
            <div class="row-between wrap" style="gap: 12px">
              <div class="row" style="gap: 12px; min-width: 0; align-items: center">
                <span class="live-dot-pulse" />
                <div style="min-width: 0">
                  <div class="row wrap" style="gap: 8px; align-items: center">
                    <span class="live-pulse-badge">Đang phát trực tiếp</span>
                    <strong class="truncate" style="font-size: 15px; max-width: 450px">
                      {{ activeTeamSession.fileName }}
                    </strong>
                  </div>
                  <div class="hint" style="margin-top: 4px; font-size: 13px">
                    Do <strong>{{ activeTeamSession.uploadedBy }}</strong> chia sẻ ·
                    {{ formatNumber(activeTeamSession.rowCount) }} hồ sơ
                    <template v-if="activeTeamSession.uploadedAt">
                      · Tải lên {{ new Date(activeTeamSession.uploadedAt).toLocaleTimeString('vi-VN') }}
                    </template>
                  </div>
                </div>
              </div>

              <div class="row wrap" style="gap: 8px; align-items: center">
                <button
                  class="btn btn-primary"
                  type="button"
                  :disabled="resuming"
                  @click="joinActiveTeamSession"
                >
                  <Users :size="16" />
                  {{ resuming ? 'Đang vào...' : 'Vào làm việc chung ngay' }}
                </button>
                <button
                  v-if="isLiveOwnerOrAdmin"
                  class="btn btn-secondary btn-sm"
                  style="color: var(--danger)"
                  type="button"
                  :disabled="settingActiveSession"
                  title="Gỡ file này khỏi chế độ làm việc chung của phòng"
                  @click="removeActiveTeamSession"
                >
                  <X :size="14" />
                  Gỡ file chung
                </button>
              </div>
            </div>
            <div class="team-session-desc" style="font-size: 12px">
              File làm việc chung trực tiếp của cả phòng vẫn luôn hoạt động trên máy chủ. Bạn và mọi người có thể vào làm tiếp bất cứ lúc nào!
            </div>
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
      <!-- Banner thông báo khi có file làm chung mới từ đồng đội (không giật màn hình) -->
      <div
        v-if="showIncomingSessionBanner && incomingTeamSession && incomingTeamSession.sessionId !== session.sessionId"
        class="alert alert-info row-between wrap"
        style="margin-bottom: 12px; border-radius: 12px; background: rgba(59, 130, 246, 0.12); border: 1px solid rgba(59, 130, 246, 0.35); padding: 10px 14px"
      >
        <div class="row" style="gap: 10px; min-width: 0">
          <Sparkles :size="18" class="text-amber-400" />
          <div style="font-size: 13px">
            <strong>{{ incomingTeamSession.uploadedBy }}</strong> vừa tải file làm chung mới:
            <strong>{{ incomingTeamSession.fileName }}</strong> ({{ formatNumber(incomingTeamSession.rowCount) }} dòng).
          </div>
        </div>
        <div class="row" style="gap: 8px">
          <button class="btn btn-primary btn-sm" type="button" @click="handleAcceptIncomingTeamSession">
            Chuyển sang file mới
          </button>
          <button class="btn btn-ghost btn-sm" type="button" title="Đóng thông báo này (bạn có thể chuyển sau qua Các file gần đây)" @click="showIncomingSessionBanner = false">
            Để sau
          </button>
        </div>
      </div>

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
            v-if="isLiveOwnerOrAdmin"
            class="btn btn-secondary btn-sm"
            style="color: var(--danger)"
            type="button"
            :disabled="settingActiveSession"
            title="Gỡ file này khỏi chế độ làm việc chung của cả phòng"
            @click="removeActiveTeamSession"
          >
            <X :size="14" />
            Gỡ file làm việc chung
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
            <button
              class="btn btn-secondary btn-sm"
              type="button"
              title="Xem và chuyển đổi giữa các file đang xử lý gần đây"
              @click="openSessionSwitcher"
            >
              <FolderClock :size="14" />
              Các file gần đây
              <span v-if="resumeCandidates.length > 0" class="badge badge-neutral" style="margin-left: 4px">{{ resumeCandidates.length }}</span>
            </button>
            <button
              v-if="isCurrentSessionActiveTeam"
              class="btn btn-secondary btn-sm"
              type="button"
              title="Rời màn hình xem mà không tắt phiên chung của phòng"
              @click="closeSession"
            >
              <LogOut :size="14" />
              Rời phiên (File vẫn Live)
            </button>
            <button
              v-else
              class="btn btn-secondary btn-sm"
              type="button"
              title="Đóng workbook và dọn phiên"
              @click="closeSession"
            >
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

        <div v-if="voiceListening || voiceLastHeard" class="voice-bar" role="status" aria-live="polite">
          <span class="voice-dot" :class="{ 'is-listening': voiceListening }" />
          <span class="voice-live">
            <strong>{{ voiceListening ? '🎙️ Đang nghe:' : 'Đã nghe:' }}</strong>
            {{ voiceInputText || voiceInterim || voiceLastHeard || 'Nói tên giấy tờ hoặc lệnh xong, tiếp, bộ 12...' }}
          </span>
          <span v-if="voiceLastHeard && voiceListening" class="voice-heard" :title="`Câu vừa nghe: ${voiceLastHeard}`">
            (Trước đó: {{ voiceLastHeard }})
          </span>
          <button class="btn btn-ghost btn-sm" type="button" @click="toggleVoice">
            {{ voiceListening ? 'Tắt mic' : 'Bật mic' }}
          </button>
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

            <label class="checkbox summary-existing" :class="{ 'is-disabled': session.existingListCount === 0 }">
              <input v-model="onlyExisting" type="checkbox" :disabled="session.existingListCount === 0" />
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

            <label class="checkbox">
              <input v-model="sortByNumber" type="checkbox" />
              <span class="checkbox-text">
                <span class="checkbox-title">Sắp xếp số hồ sơ tăng dần</span>
                <span class="checkbox-desc">Thứ tự 1, 2, 3... (bỏ chọn để giữ theo dòng Excel gốc)</span>
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
                class="btn btn-success btn-sm"
                type="button"
                :title="syncingInBackground ? (isQueuedForOneDrive ? 'Tiến trình đang trong hàng chờ do có robot khác đang chạy...' : 'Robot đang chạy ngầm...') : 'Tự động điền Trích yếu Cột D, H và đánh dấu Cột A lên Cloud (chạy ngầm tự động, không đánh tick)'"
                :disabled="!scopeReady || total === 0 || loadingRows || syncingOneDrive"
                @click="openOneDriveSyncFromToolbar"
              >
                <Cloud :size="14" :class="{ spin: syncingInBackground && !isQueuedForOneDrive }" />
                {{ syncingInBackground ? (isQueuedForOneDrive ? '⏳ Đang trong hàng chờ...' : '⚡ Đang chạy ngầm...') : 'Đồng bộ OneDrive' }}
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
              :title="selected.size > 0 ? 'Gán bộ đang chọn cho các dòng đã chọn còn thiếu danh mục' : 'Gán bộ đang chọn cho MỌI dòng còn thiếu danh mục trong phạm vi đang lọc — duyệt hết các trang, không cần tích từng dòng'"
              @click="applyToMissingInScope"
            >
              <Check :size="14" />
              {{ applyingMissing ? 'Đang áp...' : (selected.size > 0 ? ('Áp dụng cho ' + selected.size + ' dòng đã chọn còn thiếu') : 'Áp dụng cho dòng lọc còn thiếu') }}
            </button>
            <div v-if="catalogPresets.length > 0" class="row wrap" style="gap: 4px; align-items: center">
              <button
                v-for="p in catalogPresets.slice(0, 3)"
                :key="'quick-m-' + p.id"
                class="btn btn-secondary btn-xs"
                type="button"
                :title="p.items.join(', ')"
                @click="applyPreset(p)"
              >
                {{ p.name }}
              </button>
            </div>
          </div>

          <div v-if="rowsError" class="alert alert-danger">
            <TriangleExclamationPoint :size="17" />
            <div class="grow">{{ rowsError }}</div>
            <button class="btn btn-secondary btn-sm" type="button" @click="fetchRows">Thử lại</button>
          </div>

          <div v-if="!loadingRows && rows.length > 0" class="summary-table-hint hint">
            Tích ô ở đầu dòng để sửa nhiều dòng cùng lúc; muốn chỉ xuất đúng các dòng đã tích, bật
            “Chỉ các dòng đã chọn” ở bước xem trước.
          </div>
          <div v-if="selected.size > 0 && !loadingRows" class="summary-bulkbar">
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
              <!-- Các bộ mẫu do Admin cấu hình -->
              <div class="row wrap" style="gap: 6px; align-items: center">
                <button
                  v-if="catalogPresets.length === 0"
                  class="btn btn-secondary btn-sm"
                  type="button"
                  :disabled="bulkApplying"
                  title="Bộ 12 giấy tờ chuẩn mặc định (Đăng ký kết hôn)"
                  @click="bulkApplyStandardSeven"
                >
                  <Star :size="14" />
                  Bộ 12 chuẩn
                </button>
                <button
                  v-for="preset in catalogPresets"
                  :key="preset.id"
                  class="btn btn-secondary btn-sm"
                  type="button"
                  :disabled="bulkApplying"
                  :title="`${preset.name} (${preset.items.length} giấy tờ): ${preset.items.join(', ')}`"
                  @click="applyPreset(preset)"
                >
                  <Star v-if="preset.isDefault" :size="14" style="color: #f59e0b" />
                  <FileText v-else :size="14" />
                  {{ preset.name }}
                </button>
                <button
                  class="btn btn-ghost btn-sm"
                  type="button"
                  title="Cấu hình / Thêm các bộ mẫu giấy tờ cho từng loại hồ sơ"
                  @click="presetModalOpen = true"
                >
                  <Settings :size="14" />
                  Bộ mẫu
                </button>
              </div>
              <button
                v-if="selectedNormalizableCount > 0"
                class="btn btn-secondary btn-sm"
                type="button"
                @click="normalizeSelected"
              >
                <CircleCheck :size="14" />
                Sửa mở đầu đúng mẫu ({{ formatNumber(selectedNormalizableCount) }})
              </button>
              <button
                class="btn btn-accent btn-sm"
                type="button"
                :disabled="syncingOneDrive"
                :title="`Mở hộp thoại đồng bộ ${formatNumber(selected.size)} dòng đã chọn lên OneDrive`"
                @click="openOneDriveSyncFromToolbar"
              >
                <Cloud :size="14" />
                Đồng bộ {{ formatNumber(selected.size) }} dòng đã chọn lên OneDrive
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
                  <th
                    style="width: 108px; cursor: pointer; user-select: none"
                    :title="sortByNumber ? 'Đang sắp xếp số hồ sơ tăng dần. Bấm để về thứ tự dòng file gốc' : 'Bấm để sắp xếp theo số hồ sơ tăng dần (1, 2, 3...)'"
                    @click="sortByNumber = !sortByNumber"
                  >
                    Hồ sơ số ({{ session.documentNumberColumn }})
                    <span v-if="sortByNumber" style="color: var(--accent, #3b82f6); margin-left: 2px">▲</span>
                  </th>
                  <th
                    style="width: 96px; cursor: pointer; user-select: none"
                    :title="sortByNumber ? 'Đang sắp xếp số hồ sơ tăng dần. Bấm để về thứ tự dòng file gốc' : 'Bấm để sắp xếp theo số hồ sơ tăng dần (1, 2, 3...)'"
                    @click="sortByNumber = !sortByNumber"
                  >
                    Số ({{ session.numberColumn ?? 'B' }})
                    <span v-if="sortByNumber" style="color: var(--accent, #3b82f6); margin-left: 2px">▲</span>
                  </th>
                  <th>Trích yếu</th>
                  <th style="width: 320px">Giấy tờ trong ngoặc</th>
                </tr>
              </thead>
              <tbody>
                <!-- SKELETON LOADING SHIMMER: hiển thị khung xương mượt mà khi đang nạp -->
                <template v-if="loadingRows">
                  <tr v-for="skelIdx in 8" :key="'skel-' + skelIdx" class="skeleton-tr">
                    <td><div class="skeleton-shimmer" style="width: 16px; height: 16px; border-radius: 3px;"></div></td>
                    <td>
                      <div class="skeleton-shimmer" style="width: 65px; height: 16px; margin-bottom: 4px;"></div>
                      <div class="skeleton-shimmer" style="width: 45px; height: 12px;"></div>
                    </td>
                    <td><div class="skeleton-shimmer" style="width: 42px; height: 16px;"></div></td>
                    <td><div class="skeleton-shimmer" style="width: 55px; height: 16px;"></div></td>
                    <td><div class="skeleton-shimmer" style="width: 50px; height: 16px;"></div></td>
                    <td><div class="skeleton-shimmer" style="width: 45px; height: 16px;"></div></td>
                    <td>
                      <div class="skeleton-shimmer" :style="{ width: (55 + (skelIdx * 11) % 35) + '%', height: '16px', marginBottom: '6px' }"></div>
                      <div class="skeleton-shimmer" :style="{ width: (40 + (skelIdx * 7) % 25) + '%', height: '12px' }"></div>
                    </td>
                    <td>
                      <div class="row wrap" style="gap: 4px">
                        <div class="skeleton-shimmer" style="width: 70px; height: 20px; border-radius: 999px;"></div>
                        <div class="skeleton-shimmer" style="width: 85px; height: 20px; border-radius: 999px;"></div>
                      </div>
                    </td>
                  </tr>
                </template>

                <template v-else-if="rows.length === 0">
                  <tr>
                    <td colspan="8" style="padding: 40px 16px; text-align: center;">
                      <EmptyState
                        title="Không có hồ sơ phù hợp"
                        description="Thử bỏ bộ lọc năm, loại hồ sơ, số hồ sơ hoặc từ khoá tìm kiếm."
                      >
                        <template #action>
                          <button
                            v-if="hasActiveFilter"
                            class="btn btn-secondary btn-sm"
                            type="button"
                            style="margin-top: 14px"
                            @click="clearAllFilters"
                          >
                            <Eraser :size="14" />
                            Xoá bộ lọc (Hiện toàn bộ {{ formatNumber(session?.rowCount ?? 0) }} hồ sơ)
                          </button>
                        </template>
                      </EmptyState>
                    </td>
                  </tr>
                </template>

                <template v-else>
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
                </template>
              </tbody>
            </table>
          </div>

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
              class="btn btn-success"
              type="button"
              :disabled="syncingOneDrive || previewRows.length === 0"
              title="Tự động điền Trích yếu vào Cột D, H và đánh dấu Cột A lên Cloud (chạy ngầm, không đánh tick)"
              @click="startSyncToOneDriveFromPreview"
            >
              <Cloud :size="16" />
              {{ syncingOneDrive ? 'Đang đồng bộ...' : 'Đồng bộ vào OneDrive' }}
            </button>
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

      <!-- Modal: Quản lý Bộ Mẫu Giấy Tờ do Admin cấu hình -->
      <Transition name="modal">
      <div v-if="presetModalOpen" class="modal-backdrop" @click.self="presetModalOpen = false">
        <div class="modal" style="width: min(680px, 100%)">
          <div class="modal-header">
            <div>
              <div class="card-title">Cấu hình Bộ mẫu Giấy tờ (Admin Presets)</div>
              <div class="card-subtitle">
                Admin tự thiết lập các bộ giấy tờ chuẩn theo từng loại hộ tịch (Kết hôn, Khai sinh, Khai tử...)
              </div>
            </div>
            <button class="btn btn-ghost btn-icon" type="button" aria-label="Đóng" @click="presetModalOpen = false">
              <X :size="18" />
            </button>
          </div>
          <div class="modal-body stack" style="gap: 16px">
            <!-- Danh sách các bộ mẫu hiện tại -->
            <div>
              <div style="font-weight: 600; font-size: 0.9rem; margin-bottom: 6px">Danh sách các bộ mẫu đang có:</div>
              <div style="display: flex; flex-direction: column; gap: 8px; max-height: 240px; overflow-y: auto">
                <div
                  v-for="p in catalogPresets"
                  :key="p.id"
                  class="card"
                  style="padding: 10px 14px; display: flex; justify-content: space-between; align-items: center"
                >
                  <div style="min-width: 0">
                    <div style="display: flex; align-items: center; gap: 6px">
                      <strong style="font-size: 0.95rem">{{ p.name }}</strong>
                      <span v-if="p.dossierType" class="badge">{{ p.dossierType }}</span>
                      <span v-if="p.isDefault" class="badge badge-brand">Mặc định</span>
                    </div>
                    <div class="hint truncate" style="margin-top: 3px" :title="p.items.join(', ')">
                      {{ p.items.join(', ') }} ({{ p.items.length }} giấy tờ)
                    </div>
                  </div>
                  <div class="row" style="gap: 6px; flex-shrink: 0; margin-left: 12px">
                    <button class="btn btn-secondary btn-xs" type="button" @click="openEditPresetModal(p)">
                      Sửa
                    </button>
                    <button
                      v-if="!p.isDefault"
                      class="btn btn-danger btn-xs"
                      type="button"
                      @click="deletePreset(p.id, p.name)"
                    >
                      Xoá
                    </button>
                  </div>
                </div>
              </div>
            </div>

            <!-- Form thêm / sửa bộ mẫu -->
            <div class="card" style="padding: 14px; background: var(--bg-surface-2, rgba(255,255,255,0.03))">
              <div style="font-weight: 600; margin-bottom: 10px">
                {{ editingPreset?.id ? 'Chỉnh sửa bộ mẫu' : 'Thêm bộ mẫu mới' }}
              </div>
              <div class="row wrap" style="gap: 10px">
                <label class="field grow">
                  <span class="label">Tên bộ mẫu</span>
                  <input
                    v-if="editingPreset"
                    v-model="editingPreset.name"
                    class="input"
                    placeholder="VD: Bộ hồ sơ Khai sinh chuẩn"
                  />
                </label>
                <label class="field" style="width: 200px">
                  <span class="label">Loại hồ sơ áp dụng</span>
                  <input
                    v-if="editingPreset"
                    v-model="editingPreset.dossierType"
                    class="input"
                    placeholder="VD: Đăng ký khai sinh"
                  />
                </label>
              </div>

              <div style="margin-top: 10px">
                <span class="label" style="display: block; margin-bottom: 4px">Danh sách giấy tờ thành phần</span>
                <SummaryItemPicker
                  v-if="editingPreset && session"
                  v-model="editingPreset.items"
                  :catalog="session.catalog"
                  :counts="catalogCounts"
                  placeholder="Gõ hoặc chọn giấy tờ đưa vào bộ mẫu..."
                  @custom-added="persistCatalogCustom"
                />
              </div>

              <div class="row-between" style="margin-top: 14px">
                <button
                  v-if="editingPreset?.id"
                  class="btn btn-ghost btn-sm"
                  type="button"
                  @click="openCreatePresetModal"
                >
                  Huỷ sửa / Tạo mới
                </button>
                <span v-else></span>
                <button class="btn btn-primary btn-sm" type="button" @click="savePreset">
                  <Save :size="14" />
                  {{ editingPreset?.id ? 'Cập nhật bộ mẫu' : 'Lưu bộ mẫu mới' }}
                </button>
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-secondary" type="button" @click="presetModalOpen = false">Đóng</button>
          </div>
        </div>
      </div>
      </Transition>

      <!-- Modal: Đồng bộ Trích yếu & Tự động Đánh Dấu vào OneDrive Cloud -->
      <Transition name="modal">
      <div v-if="oneDriveSyncModalOpen" class="modal-backdrop" @click.self="!syncingOneDrive && (oneDriveSyncModalOpen = false)">
        <div class="modal" style="width: min(720px, 100%)">
          <div class="modal-header">
            <div>
              <div class="card-title row" style="gap: 8px; align-items: center">
                <span class="badge badge-brand">
                  <Sparkles :size="13" />
                  Cloud Automation
                </span>
                <span>Đồng bộ Trích yếu vào OneDrive (Cột A, D, H)</span>
              </div>
              <div class="card-subtitle">
                Robot sẽ mở file Excel Online / OneDrive, tự động điền <strong>Cột D & H</strong> (Trích yếu nội dung) và đánh dấu <strong>Cột A là "đã nhập"</strong> theo khoảng số hoặc danh sách hồ sơ. Bỏ qua hoàn toàn việc tick checkbox.
              </div>
            </div>
            <button
              class="btn btn-ghost btn-icon"
              type="button"
              :disabled="syncingOneDrive"
              aria-label="Đóng"
              @click="oneDriveSyncModalOpen = false"
            >
              <X :size="18" />
            </button>
          </div>

          <div class="modal-body stack" style="gap: 16px">
            <!-- Mode Info Alert -->
            <div class="alert alert-info" style="padding: 10px 14px; font-size: 0.88rem; align-items: flex-start">
              <Info :size="16" style="margin-top: 2px" />
              <div>
                <strong>Quy trình mới tối ưu:</strong> Lấy dữ liệu vừa xong trong web đẩy vào <strong>Cột D & H</strong>, đánh <strong>Cột A là "đã nhập"</strong>. Không còn tick checkbox các cột giấy tờ giúp hạn chế tối đa lỗi và tăng tốc độ xử lý.
              </div>
            </div>

            <!-- Cloud Link Input -->
            <label class="field">
              <span class="label">Liên kết Google Sheets / Excel Online / OneDrive:</span>
              <input
                v-model="oneDriveUrl"
                class="input"
                type="text"
                placeholder="Dán liên kết Google Sheets hoặc Excel Online / OneDrive vào đây..."
                :disabled="syncingOneDrive"
              />
            </label>

            <!-- Permission Check Status Card -->
            <div v-if="oneDriveUrl.trim()" style="margin-top: -6px; margin-bottom: 4px">
              <div v-if="checkingLinkPermission" class="row" style="gap: 8px; align-items: center; font-size: 13px; color: var(--muted); padding: 6px 4px">
                <RefreshCw :size="14" class="spin" />
                <span>Đang kiểm tra quyền truy cập và phiên đăng nhập của Robot...</span>
              </div>
              <div v-else-if="linkPermissionStatus" class="card" :style="{
                padding: '10px 14px',
                background: linkPermissionStatus.canEdit ? 'rgba(34, 197, 94, 0.08)' : 'rgba(234, 179, 8, 0.12)',
                border: linkPermissionStatus.canEdit ? '1px solid rgba(34, 197, 94, 0.3)' : '1px solid rgba(234, 179, 8, 0.4)',
                borderRadius: '8px'
              }">
                <div class="row-between wrap" style="gap: 10px; align-items: center">
                  <div class="row" style="gap: 8px; align-items: center">
                    <CircleCheck v-if="linkPermissionStatus.canEdit" :size="18" style="color: var(--success); flex-shrink: 0" />
                    <TriangleExclamationPoint v-else :size="18" style="color: var(--warning); flex-shrink: 0" />
                    <div>
                      <strong :style="{ color: linkPermissionStatus.canEdit ? 'var(--success)' : 'var(--warning)', fontSize: '13px' }">
                        {{ linkPermissionStatus.canEdit ? 'Quyền Chỉnh sửa (Editor) sẵn sàng' : 'Chưa đăng nhập / Chỉ có quyền Xem' }}
                      </strong>
                      <div class="hint" style="font-size: 12px; margin-top: 2px">
                        {{ linkPermissionStatus.message }}
                      </div>
                    </div>
                  </div>
                  <div class="row" style="gap: 8px; align-items: center">
                    <button
                      v-if="!linkPermissionStatus.canEdit"
                      type="button"
                      class="btn btn-warning btn-sm"
                      :disabled="openingLoginWindow"
                      @click="openInteractiveLogin"
                    >
                      <Zap :size="14" />
                      {{ openingLoginWindow ? 'Đang mở Chrome...' : '🔑 Đăng nhập Google' }}
                    </button>
                    <button
                      type="button"
                      class="btn btn-secondary btn-sm"
                      :disabled="checkingLinkPermission"
                      title="Kiểm tra lại quyền truy cập"
                      @click="checkLinkPermission()"
                    >
                      <RefreshCw :size="13" :class="{ spin: checkingLinkPermission }" />
                      Kiểm tra lại
                    </button>
                  </div>
                </div>
              </div>
            </div>

            <!-- Sheet & Count Summary Card -->
            <div class="card" style="padding: 12px 16px; background: var(--surface)">
              <div class="row-between wrap" style="gap: 10px">
                <div class="row" style="gap: 16px; align-items: center">
                  <div>
                    <label class="hint" style="display: block; margin-bottom: 2px">Sheet đích (VD: 2001, 2002):</label>
                    <input
                      v-model="selectedSyncSheet"
                      type="text"
                      class="input input-sm"
                      style="width: 120px; font-weight: 700; color: var(--brand)"
                      placeholder="2002"
                      :disabled="syncingOneDrive"
                    />
                  </div>
                  <div>
                    <span class="hint" style="display: block">Số hồ sơ đồng bộ:</span>
                    <strong style="color: var(--info); font-size: 1.05rem">
                      {{ rowsToSyncPreview.length }} hồ sơ
                    </strong>
                  </div>
                </div>
                <div class="row" style="gap: 6px">
                  <span class="badge badge-accent">
                    <Cloud :size="12" />
                    Chế độ Live Cloud
                  </span>
                </div>
              </div>
            </div>

            <!-- Scope Selector (Khoảng số / dòng chọn / thay đổi / toàn bộ bộ lọc) -->
            <div class="row wrap" style="gap: 8px">
              <button
                type="button"
                class="btn btn-sm"
                :class="oneDriveSyncScope === 'number_range' ? 'btn-primary' : 'btn-secondary'"
                :disabled="syncingOneDrive"
                @click="oneDriveSyncScope = 'number_range'; updateSyncRowsPreview()"
              >
                <Calendar :size="14" />
                Theo khoảng số hồ sơ (Từ số - Đến số)
              </button>
              <button
                v-if="selected.size > 0"
                type="button"
                class="btn btn-sm"
                :class="oneDriveSyncScope === 'selected' ? 'btn-primary' : 'btn-secondary'"
                :disabled="syncingOneDrive"
                @click="oneDriveSyncScope = 'selected'; updateSyncRowsPreview()"
              >
                <Check :size="14" />
                Chỉ {{ selected.size }} dòng đã chọn
              </button>
              <button
                v-if="edits.size > 0"
                type="button"
                class="btn btn-sm"
                :class="oneDriveSyncScope === 'changes_only' ? 'btn-primary' : 'btn-secondary'"
                :disabled="syncingOneDrive"
                @click="oneDriveSyncScope = 'changes_only'; updateSyncRowsPreview()"
              >
                Chỉ {{ edits.size }} hồ sơ vừa chỉnh sửa{{ selected.size === 0 ? ' (Khuyến nghị)' : '' }}
              </button>
              <button
                v-if="rows.length > 0 || previewRows.length > 0"
                type="button"
                class="btn btn-sm"
                :class="oneDriveSyncScope === 'current_filter' ? 'btn-primary' : 'btn-secondary'"
                :disabled="syncingOneDrive"
                @click="oneDriveSyncScope = 'current_filter'; updateSyncRowsPreview()"
              >
                Toàn bộ {{ (previewOpen && previewRows.length > 0) ? previewRows.length : rows.length }} hồ sơ trong bộ lọc
              </button>
            </div>

            <!-- Range Filter Inputs when number_range is chosen -->
            <div v-if="oneDriveSyncScope === 'number_range'" class="card" style="padding: 10px 14px; background: rgba(255, 255, 255, 0.02)">
              <div class="row wrap" style="gap: 16px; align-items: center">
                <span class="hint" style="font-size: 12px; color: var(--text-muted); width: 100%; margin-bottom: 2px">
                  * Lấy liên tục từ trên xuống dưới theo thứ tự xuất hiện của hồ sơ trong bảng (kể cả số nhảy quãng).
                </span>
                <label class="row" style="gap: 8px; align-items: center">
                  <span class="hint" style="font-weight: 600">Từ số:</span>
                  <input
                    v-model.number="syncNumberFrom"
                    type="number"
                    class="input input-sm"
                    style="width: 100px; font-weight: 700; color: var(--brand)"
                    placeholder="VD: 1"
                    :disabled="syncingOneDrive"
                    @input="updateSyncRowsPreview"
                  />
                </label>
                <label class="row" style="gap: 8px; align-items: center">
                  <span class="hint" style="font-weight: 600">Đến số:</span>
                  <input
                    v-model.number="syncNumberTo"
                    type="number"
                    class="input input-sm"
                    style="width: 100px; font-weight: 700; color: var(--brand)"
                    placeholder="VD: 50"
                    :disabled="syncingOneDrive"
                    @input="updateSyncRowsPreview"
                  />
                </label>
                <label class="row" style="gap: 8px; align-items: center">
                  <span class="hint" style="font-weight: 600">Nội dung Cột A:</span>
                  <input
                    v-model="syncStatusA"
                    type="text"
                    class="input input-sm"
                    style="width: 110px; font-weight: 600"
                    placeholder="đã nhập"
                    :disabled="syncingOneDrive"
                    @input="updateSyncRowsPreview"
                  />
                </label>
              </div>
            </div>

            <!-- Preview Rows List -->
            <div>
              <div class="row-between" style="margin-bottom: 6px">
                <span style="font-weight: 600; font-size: 0.9rem">Danh sách hồ sơ chuẩn bị điền (Cột A, D, H):</span>
                <span class="hint">Hiển thị {{ Math.min(5, rowsToSyncPreview.length) }}/{{ rowsToSyncPreview.length }} hồ sơ</span>
              </div>
              <div style="display: flex; flex-direction: column; gap: 8px; max-height: 200px; overflow-y: auto">
                <div
                  v-for="(r, idx) in rowsToSyncPreview.slice(0, 10)"
                  :key="idx"
                  class="card"
                  style="padding: 8px 12px; background: rgba(255, 255, 255, 0.02)"
                >
                  <div class="row-between wrap" style="gap: 8px">
                    <div class="row" style="gap: 8px; align-items: center">
                      <strong style="color: var(--brand)">HS {{ r.numberLabel || r.rowIndex }}</strong>
                      <span class="hint">(Hàng {{ r.rowIndex }})</span>
                      <span class="badge badge-xs badge-neutral">Cột A: {{ r.statusA || 'đã nhập' }}</span>
                    </div>
                  </div>
                  <div class="truncate hint" style="font-size: 12px; margin-top: 4px" :title="r.summaryText">
                    {{ r.summaryText }}
                  </div>
                </div>
              </div>
            </div>

            <!-- Running State -->
            <div v-if="syncingOneDrive" class="alert alert-info">
              <RefreshCw :size="18" class="spin" />
              <div class="grow">
                <strong>Robot đang đồng bộ trực tiếp lên OneDrive Cloud...</strong>
                <div style="margin-top: 3px; font-size: 13px">
                  Đang mở phiên trình duyệt, đánh dấu Cột A là "đã nhập" và điền trích yếu vào Cột D, H. Quá trình mất khoảng 10-20 giây.
                </div>
              </div>
            </div>

            <!-- Success Result -->
            <div v-else-if="oneDriveSyncResult" class="alert alert-success">
              <CircleCheck :size="20" />
              <div class="grow">
                <strong>Đồng bộ OneDrive thành công rực rỡ!</strong>
                <div style="margin-top: 4px">
                  {{ oneDriveSyncResult.message }}
                </div>
                <div class="row" style="gap: 8px; margin-top: 8px">
                  <a :href="oneDriveUrl" target="_blank" rel="noopener noreferrer" class="btn btn-secondary btn-sm">
                    <ExternalLink :size="14" />
                    Mở kiểm tra trên OneDrive Cloud
                  </a>
                </div>
              </div>
            </div>

            <!-- Error State -->
            <div v-else-if="oneDriveSyncError" class="alert alert-danger">
              <TriangleExclamationPoint :size="18" />
              <div class="grow">
                <strong>Lỗi đồng bộ:</strong>
                <div style="margin-top: 2px">{{ oneDriveSyncError }}</div>
              </div>
            </div>
          </div>

          <div class="modal-footer" style="display: flex; justify-content: space-between; align-items: center; width: 100%; gap: 10px; flex-wrap: wrap">
            <button
              class="btn btn-secondary btn-sm"
              type="button"
              :disabled="syncingOneDrive || rowsToSyncPreview.length === 0"
              title="Sao chép toàn bộ cột Trích yếu (kèm ngoặc) vào Clipboard để mở Google Sheets dán ngay"
              @click="copySyncRowsToClipboard"
            >
              <Copy :size="15" />
              Copy Cột D (Dán vào Google Sheets)
            </button>

            <div class="row" style="gap: 8px">
              <button
                class="btn btn-secondary"
                type="button"
                :disabled="syncingOneDrive"
                @click="oneDriveSyncModalOpen = false"
              >
                {{ oneDriveSyncResult ? 'Đóng' : 'Huỷ' }}
              </button>
              <button
                v-if="!oneDriveSyncResult"
                class="btn btn-success"
                type="button"
                :disabled="syncingOneDrive || rowsToSyncPreview.length === 0"
                @click="executeOneDriveSync"
              >
                <Zap :size="16" />
                {{ syncingOneDrive ? 'Đang thực thi robot...' : 'Bắt đầu Điền Cột A, D, H vào Cloud' }}
              </button>
            </div>
          </div>
        </div>
      </div>
      </Transition>

      <!-- Modal 1: Nhắc nhở file làm chung mới từ đồng đội (Opt-in) -->
      <Transition name="modal">
      <div v-if="incomingSessionModalOpen && incomingTeamSession" class="modal-backdrop" @click.self="incomingSessionModalOpen = false">
        <div class="modal" style="width: min(540px, 100%)">
          <div class="modal-header">
            <div>
              <div class="card-title row" style="gap: 8px; align-items: center">
                <Sparkles :size="16" class="text-amber-400" />
                <span>File làm việc chung mới từ đồng đội</span>
              </div>
              <div class="card-subtitle">
                Đồng đội vừa mở file cho cả phòng cùng làm việc trực tiếp.
              </div>
            </div>
            <button class="btn btn-ghost btn-icon" type="button" aria-label="Đóng" @click="incomingSessionModalOpen = false">
              <X :size="18" />
            </button>
          </div>
          <div class="modal-body stack" style="gap: 12px">
            <div class="card" style="padding: 12px 14px; background: var(--surface)">
              <div class="row-between">
                <span class="muted">Người chia sẻ:</span>
                <strong>{{ incomingTeamSession.uploadedBy }}</strong>
              </div>
              <div class="row-between" style="margin-top: 6px">
                <span class="muted">File mới:</span>
                <strong style="color: var(--brand)">{{ incomingTeamSession.fileName }}</strong>
              </div>
              <div class="row-between" style="margin-top: 6px">
                <span class="muted">Tổng số hồ sơ:</span>
                <span>{{ formatNumber(incomingTeamSession.rowCount) }} dòng</span>
              </div>
            </div>

            <div v-if="session && changedCount > 0" class="alert alert-warning">
              <TriangleExclamationPoint :size="18" />
              <div class="grow">
                <strong>Bạn đang có {{ formatNumber(changedCount) }} dòng đã sửa ở file hiện tại!</strong>
                <div style="margin-top: 2px">
                  Hệ thống sẽ tự động lưu bản nháp an toàn lên máy chủ trước khi chuyển. Bạn vẫn có thể quay lại làm tiếp bất cứ lúc nào qua mục "Các file gần đây".
                </div>
              </div>
            </div>

            <div v-else class="alert alert-info">
              <Info :size="16" />
              <div class="grow">
                Bạn có thể chọn chuyển sang làm chung ngay bây giờ, hoặc bấm "Để sau" để tiếp tục làm dở file hiện tại.
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-secondary" type="button" @click="incomingSessionModalOpen = false">
              Tiếp tục làm file hiện tại
            </button>
            <button class="btn btn-primary" type="button" :disabled="switchingSession" @click="handleAcceptIncomingTeamSession">
              {{ switchingSession ? 'Đang chuyển...' : 'Lưu nháp & Chuyển sang file mới' }}
            </button>
          </div>
        </div>
      </div>
      </Transition>

      <!-- Modal 2: Cảnh báo an toàn trước khi chuyển phiên (Safety Gate) -->
      <Transition name="modal">
      <div v-if="safetyConfirmModalOpen && pendingSwitchTarget" class="modal-backdrop" @click.self="safetyConfirmModalOpen = false">
        <div class="modal" style="width: min(540px, 100%)">
          <div class="modal-header">
            <div>
              <div class="card-title row" style="gap: 8px; align-items: center">
                <TriangleExclamationPoint :size="18" class="text-amber-400" />
                <span>Xác nhận chuyển phiên làm việc</span>
              </div>
              <div class="card-subtitle">
                Bảo vệ tiến độ và dữ liệu trước khi chuyển sang file khác.
              </div>
            </div>
            <button class="btn btn-ghost btn-icon" type="button" aria-label="Đóng" @click="safetyConfirmModalOpen = false">
              <X :size="18" />
            </button>
          </div>
          <div class="modal-body stack" style="gap: 12px">
            <div class="alert alert-warning">
              <div class="grow">
                Bạn đang có <strong>{{ formatNumber(changedCount) }}</strong> dòng đã chỉnh sửa ở file hiện tại <strong>{{ session?.fileName }}</strong> chưa được Nạp vào Kho hồ sơ (CSDL SQL Server).
              </div>
            </div>
            <div class="hint">
              Bạn muốn nạp toàn bộ các dòng này vào Kho hồ sơ trước khi chuyển, hay chỉ lưu bản nháp để lát nữa quay lại làm tiếp?
            </div>
          </div>
          <div class="modal-footer wrap" style="gap: 8px">
            <button class="btn btn-ghost" type="button" @click="safetyConfirmModalOpen = false">
              Ở lại file này
            </button>
            <button class="btn btn-secondary" type="button" :disabled="switchingSession" @click="doSwitchToSession(pendingSwitchTarget.sessionId, false)">
              Chỉ lưu nháp & Chuyển
            </button>
            <button class="btn btn-primary" type="button" :disabled="switchingSession" @click="doSwitchToSession(pendingSwitchTarget.sessionId, true)">
              <Database :size="14" />
              Nạp vào Kho & Chuyển
            </button>
          </div>
        </div>
      </div>
      </Transition>

      <!-- Modal 3: Danh sách các file làm việc gần đây (Session Switcher) -->
      <Transition name="modal">
      <div v-if="sessionSwitcherOpen" class="modal-backdrop" @click.self="sessionSwitcherOpen = false">
        <div class="modal" style="width: min(640px, 100%)">
          <div class="modal-header">
            <div>
              <div class="card-title row" style="gap: 8px; align-items: center">
                <FolderClock :size="18" />
                <span>Các file làm việc gần đây</span>
              </div>
              <div class="card-subtitle">
                Danh sách các file đã nạp trên server. Bạn có thể tự do chuyển đổi qua lại giữa các file mà không sợ mất tiến độ.
              </div>
            </div>
            <button class="btn btn-ghost btn-icon" type="button" aria-label="Đóng" @click="sessionSwitcherOpen = false">
              <X :size="18" />
            </button>
          </div>
          <div class="modal-body stack" style="gap: 10px; max-height: 400px; overflow-y: auto">
            <div v-if="resumeCandidates.length === 0" class="muted" style="padding: 24px; text-align: center">
              Chưa có file nào khác được mở gần đây trên server.
            </div>
            <div
              v-for="candidate in resumeCandidates"
              :key="candidate.sessionId"
              class="card row-between wrap"
              style="padding: 10px 14px; gap: 10px"
              :style="candidate.sessionId === session?.sessionId ? 'border-color: var(--brand); background: rgba(59, 130, 246, 0.05)' : ''"
            >
              <div style="min-width: 0; flex: 1">
                <div class="row" style="gap: 8px; align-items: center">
                  <strong class="truncate" style="max-width: 320px">{{ candidate.fileName }}</strong>
                  <span v-if="candidate.sessionId === session?.sessionId" class="badge badge-brand">Đang mở</span>
                  <span v-if="activeTeamSession && activeTeamSession.sessionId === candidate.sessionId" class="badge badge-success">File cả phòng</span>
                </div>
                <div class="hint" style="margin-top: 4px; font-size: 12px">
                  {{ formatNumber(candidate.rowCount) }} hồ sơ · Nạp lúc {{ new Date(candidate.createdAt).toLocaleString('vi-VN') }}
                </div>
              </div>
              <div>
                <button
                  v-if="candidate.sessionId !== session?.sessionId"
                  class="btn btn-primary btn-sm"
                  type="button"
                  :disabled="switchingSession"
                  @click="requestSwitchToSession(candidate)"
                >
                  Chuyển sang file này
                </button>
                <span v-else class="hint" style="font-size: 12px">Đang làm việc</span>
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn btn-secondary" type="button" @click="sessionSwitcherOpen = false">Đóng</button>
          </div>
        </div>
      </div>
      </Transition>
    </Teleport>

    <!-- Professional Floating AI Voice Console & Mic widget -->
    <aside
      v-if="session"
      class="floating-voice-widget"
      :class="{ 'is-listening': voiceListening, 'is-parsing': voiceAiParsing, 'is-open': voiceConsoleOpen }"
    >
      <!-- Khung nhận diện giọng nói & Text Box trực tiếp -->
      <Transition name="voice-bubble">
        <div
          v-if="voiceConsoleOpen && (voiceListening || voiceAiParsing || voiceLastHeard || voiceInterim || voiceInputText || voiceLastAiResult)"
          class="voice-console-card"
          role="region"
          aria-label="Khung nhận diện giọng nói và AI"
        >
          <!-- Header thanh công cụ của Hộp thoại -->
          <div class="voice-console-header">
            <div class="voice-console-title">
              <div class="voice-status-indicator" :class="{ live: voiceListening, thinking: voiceAiParsing }">
                <span v-if="voiceListening" class="live-mic-dot" />
                <Sparkles v-else-if="voiceAiParsing" :size="13" class="spin-icon text-amber" />
                <span v-else class="ready-dot" />
              </div>
              <strong style="font-size: 13px">Hộp thoại Giọng nói & AI</strong>
            </div>

            <div class="voice-console-actions">
              <!-- Nút bật/tắt chuông báo ting ting -->
              <button
                type="button"
                class="voice-action-btn voice-chime-btn"
                :class="{ active: voiceChimeEnabled }"
                :title="
                  voiceChimeEnabled
                    ? 'Chuông âm thanh đang BẬT - bấm để tắt chuông'
                    : 'Chuông âm thanh đang TẮT - bấm để bật chuông'
                "
                @click="toggleVoiceChime"
              >
                <Bell v-if="voiceChimeEnabled" :size="13" class="text-amber" />
                <BellOff v-else :size="13" class="text-muted" />
                <span class="action-label">
                  {{ voiceChimeEnabled ? 'Chuông' : 'Tắt chuông' }}
                </span>
              </button>

              <!-- Nút bật/tắt đọc phản hồi bằng giọng nói (TTS) -->
              <button
                type="button"
                class="voice-action-btn voice-tts-btn"
                :class="{ active: voiceTtsEnabled }"
                :title="
                  voiceTtsEnabled
                    ? `Giọng nói AI đang BẬT ${vietnameseVoiceName ? '(' + vietnameseVoiceName + ')' : ''} - bấm để tắt tiếng`
                    : 'Giọng nói AI đang TẮT - bấm để bật giọng nói phản hồi'
                "
                @click="toggleVoiceTts"
              >
                <Volume2 v-if="voiceTtsEnabled" :size="13" class="text-brand" />
                <VolumeX v-else :size="13" class="text-muted" />
                <span class="action-label">
                  {{ voiceTtsEnabled ? 'Giọng nói' : 'Tắt tiếng' }}
                </span>
              </button>

              <!-- Nút thu gọn / đóng hộp thoại -->
              <button
                type="button"
                class="voice-action-btn voice-chevron-btn"
                title="Thu gọn khung nhận diện"
                @click="voiceConsoleOpen = false"
              >
                <ChevronDown :size="13" />
              </button>
            </div>
          </div>

          <!-- Thanh đo độ nhạy Micro thực tế (Audio VU Meter) -->
          <div class="voice-vu-meter-wrap" :title="`Mức âm lượng mic: ${voiceVolume}%`">
            <div
              class="voice-vu-meter-bar"
              :style="{ width: `${Math.max(4, Math.min(100, voiceVolume * 1.2))}%` }"
              :class="{ hot: voiceVolume > 70 }"
            />
          </div>

          <!-- Text Box hiển thị câu thoại người dùng theo thời gian thực -->
          <div class="voice-speech-textbox-wrap">
            <div class="textbox-label-row">
              <span class="textbox-label">
                <span v-if="voiceListening" class="live-recording-dot" />
                {{ voiceListening ? 'Đang thu âm lời nói (Live):' : 'Khung thoại & Lời nói:' }}
              </span>
              <div class="textbox-badge-group">
                <span v-if="voiceListening" class="stream-badge live-streaming">🔴 Đang nói trực tiếp</span>
                <span v-else-if="voiceAiParsing" class="stream-badge thinking">⚡ AI đang bóc tách…</span>
                <span v-else-if="isAiSpeaking" class="stream-badge speaking">🔊 AI đang nói…</span>
                <span v-else-if="voiceLastHeard" class="stream-badge success">✓ Đã nhận diện</span>
              </div>
            </div>

            <!-- Text Box trực tiếp: hiển thị lời nói chạy thời gian thực và cho phép chỉnh sửa/gõ phím -->
            <div class="voice-speech-input-container" :class="{ 'is-active': voiceListening, 'is-thinking': voiceAiParsing }">
              <textarea
                ref="voiceTextAreaRef"
                v-model="voiceInputText"
                class="voice-speech-textarea"
                rows="2"
                @focus="stopAnySpeech"
                @input="stopAnySpeech"
                :placeholder="
                  voiceListening
                    ? 'Đang lắng nghe... Nói tên giấy tờ hoặc số tờ (ví dụ: căn cước công dân, giấy khai sinh, số tờ 10)...'
                    : 'Bấm mic hoặc giữ phím Space để nói... (hoặc gõ vào đây rồi Enter)'
                "
                @keydown.enter.exact.prevent="submitVoiceText"
              />
              <span v-if="voiceListening && !voiceInputText" class="listening-pulse-hint">
                <Mic :size="13" class="text-pink pulse-anim" /> Đang lắng nghe giọng nói...
              </span>
            </div>

            <!-- Thanh công cụ nhỏ ngay dưới Text Box -->
            <div class="voice-textbox-toolbar">
              <div class="toolbar-left">
                <span class="toolbar-hint">
                  <kbd>Space</kbd> giữ nói · <kbd>Enter</kbd> gửi AI
                </span>
              </div>
              <div class="toolbar-right">
                <button
                  v-if="voiceInputText || voiceLastHeard"
                  type="button"
                  class="voice-tool-btn text-muted"
                  title="Xoá ô văn bản"
                  @click="clearVoiceText"
                >
                  <Eraser :size="12" /> Xoá
                </button>
                <button
                  v-if="voiceListening"
                  type="button"
                  class="voice-tool-btn btn-listening"
                  title="Dừng nghe & Chốt câu"
                  @click="stopVoice"
                >
                  <Mic :size="12" /> Dừng nghe
                </button>
                <button
                  type="button"
                  class="voice-tool-btn btn-submit-ai"
                  :disabled="voiceAiParsing || (!voiceInputText.trim() && !voiceInterim.trim())"
                  title="Gửi câu thoại cho AI bóc tách (Enter)"
                  @click="submitVoiceText"
                >
                  <Sparkles :size="12" /> Gửi AI
                </button>
              </div>
            </div>
          </div>

          <!-- Khung Xác nhận thao tác bằng giọng nói / click (Interactive Confirmation Card) -->
          <div v-if="voicePendingAction" class="voice-confirmation-card">
            <div class="confirmation-header">
              <span class="confirmation-badge">
                <TriangleExclamationPoint :size="13" class="text-amber" /> CẦN XÁC NHẬN THAO TÁC HÀNG LOẠT
              </span>
              <span class="confirmation-hint">AI đang chờ bạn trả lời (Nói "Đúng rồi" hoặc "Hủy")</span>
            </div>

            <div class="confirmation-body">
              <div class="confirmation-question">{{ voicePendingAction.question }}</div>
              
              <div v-if="voicePendingAction.items.length > 0" class="confirmation-chips">
                <span v-for="it in voicePendingAction.items" :key="it" class="badge badge-brand confirmation-chip">
                  <Check :size="11" /> {{ it }}
                </span>
              </div>

              <div class="confirmation-prompt-preview">
                <span class="text-muted" style="font-size: 11px">
                  Câu vừa nghe: <em>"{{ voicePendingAction.rawPrompt }}"</em>
                </span>
              </div>
            </div>

            <div class="confirmation-actions">
              <button
                type="button"
                class="btn-confirm-action btn-cancel"
                title="Hủy thao tác này (Nói 'Hủy' hoặc 'Không')"
                @click="cancelVoicePendingAction"
              >
                <X :size="12" /> Hủy bỏ (Nói "Hủy")
              </button>
              <button
                type="button"
                class="btn-confirm-action btn-agree"
                title="Đồng ý thực hiện ngay (Nói 'Đúng rồi' hoặc Enter)"
                @click="executeVoicePendingAction"
              >
                <Check :size="12" /> ✓ Đồng ý áp dụng (Nói "Đúng rồi")
              </button>
            </div>
          </div>

          <!-- Khung hiển thị kết quả AI đã bóc tách được từ câu thoại -->
          <div v-if="voiceLastAiResult" class="voice-ai-result-panel">
            <div class="ai-result-header">
              <Sparkles :size="12" class="text-brand" />
              <span class="ai-result-title">AI đã nhận diện và trích xuất:</span>
            </div>

            <!-- Danh sách chip giấy tờ đã được AI chọn -->
            <div v-if="voiceLastAiResult.items && voiceLastAiResult.items.length > 0" class="ai-chips-list">
              <span
                v-for="item in voiceLastAiResult.items"
                :key="item"
                class="badge badge-brand ai-item-chip"
              >
                <Check :size="11" /> {{ item }}
              </span>
            </div>
            <div v-else class="hint" style="font-size: 11.5px">
              (Không có giấy tờ nào trong câu nói)
            </div>

            <!-- Thông tin bổ sung: Lệnh / Số tờ / Dòng áp dụng -->
            <div class="ai-meta-row">
              <span v-if="voiceLastAiResult.targetLabel || voiceLastAiResult.targetRowNumber" class="ai-meta-tag">
                🎯 {{ voiceLastAiResult.targetLabel || ('Dòng ' + voiceLastAiResult.targetRowNumber) }}
              </span>
              <span v-if="voiceLastAiResult.sheetNumber" class="ai-meta-tag">
                📄 Số tờ: {{ voiceLastAiResult.sheetNumber }}
              </span>
              <span v-if="voiceLastAiResult.command" class="ai-meta-tag command">
                ⚡ Lệnh: {{ voiceLastAiResult.command }}
              </span>
            </div>

            <!-- Phản hồi đối thoại của AI (hiển thị rõ ràng kể cả khi TẮT GIỌNG NÓI để đọc trong văn phòng) -->
            <div v-if="voiceLastAiResult.reply" class="ai-reply-bubble">
              <div class="ai-reply-header">
                <MessageSquare :size="12" class="text-brand" />
                <span class="ai-reply-sender">AI Phản hồi:</span>
              </div>
              <div class="ai-reply-text">{{ voiceLastAiResult.reply }}</div>
            </div>
          </div>

          <!-- Hướng dẫn nhanh bên dưới -->
          <div class="voice-console-footer">
            <span class="hint" style="font-size: 11px">
              💡 <strong>Space</strong> để nói · Lệnh: <em>"bộ 12"</em>, <em>"áp dòng thiếu"</em>, <em>"như dòng trên"</em>, <em>"trang sau"</em>, <em>"xuất excel"</em>...
            </span>
          </div>
        </div>
      </Transition>

      <!-- Nút Mic AI với hiệu ứng sóng âm động (Click để bật/tắt hoặc mở lại hộp thoại) -->
      <div class="floating-voice-pill-wrap">
        <button
          type="button"
          class="floating-voice-btn"
          :class="{ listening: voiceListening, parsing: voiceAiParsing, ptt: voicePttActive }"
          :title="
            voiceSupported
              ? (voiceListening
                  ? 'Bấm để tắt mic (hoặc nhả phím Space)'
                  : 'Bấm để bật mic · Hoặc GIỮ PHÍM CÁCH (Space) để nói (Push-to-Talk)')
              : 'Trình duyệt chưa hỗ trợ Web Speech API'
          "
          :disabled="!voiceSupported"
          @click="onMicPillClick"
          @mousedown="onPttMouseDown"
          @mouseup="onPttMouseUp"
          @mouseleave="onPttMouseUp"
        >
          <!-- Hoạt ảnh sóng âm động hoặc biểu tượng AI / Mic -->
          <div class="voice-btn-icon-box">
            <div v-if="voiceListening" class="sound-wave-bars">
              <span class="bar" :style="{ height: `${Math.max(6, Math.min(22, 6 + (voiceVolume * 0.22)))}px` }" />
              <span class="bar" :style="{ height: `${Math.max(10, Math.min(26, 10 + (voiceVolume * 0.28)))}px` }" />
              <span class="bar" :style="{ height: `${Math.max(6, Math.min(20, 6 + (voiceVolume * 0.20)))}px` }" />
              <span class="bar" :style="{ height: `${Math.max(4, Math.min(16, 4 + (voiceVolume * 0.16)))}px` }" />
            </div>
            <Sparkles v-else-if="voiceAiParsing" :size="18" class="spin-icon text-amber" />
            <div v-else class="brain-mic-combo">
              <svg class="ai-brain-svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M12 2a4 4 0 0 0-4 4v6a4 4 0 0 0 8 0V6a4 4 0 0 0-4-4z" />
                <path d="M19 10v2a7 7 0 0 1-14 0v-2" />
                <line x1="12" y1="19" x2="12" y2="22" />
              </svg>
            </div>
          </div>
          <span class="voice-btn-label">
            {{ voiceAiParsing ? 'AI Đang nghĩ' : (voiceListening ? 'Đang nghe…' : 'Mic AI') }}
          </span>
          <span class="voice-model-badge" title="Tích hợp Local AI Ollama Qwen 2.5">3B</span>

          <!-- Nút nhỏ mở hộp thoại nếu đang đóng -->
          <span
            v-if="!voiceConsoleOpen && (voiceLastHeard || voiceLastAiResult)"
            class="open-box-indicator"
            title="Mở hộp thoại lời nói"
            @click.stop="voiceConsoleOpen = true"
          >
            <ChevronUp :size="12" />
          </span>
        </button>
      </div>
    </aside>
  </div>
</template>
