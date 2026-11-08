<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  ArrowLeft,
  ArrowRight,
  Check,
  CircleCheck,
  Clock,
  Download,
  Eraser,
  FilePenLine,
  FileSpreadsheet,
  Info,
  RefreshCw,
  Search,
  TriangleExclamationPoint,
  X,
} from '@lucide/vue'
import { download, queryString, request, saveBlob } from '../api/client'
import type {
  SummaryChangeRequest,
  SummaryFilteredPreviewResponse,
  SummaryInspectResponse,
  SummaryNumberMode,
  SummaryPreviewRowResponse,
  SummaryRowResponse,
  SummaryRowsResponse,
  SummaryScopeRequest,
} from '../api/types'
import { errorMessage } from '../lib/errors'
import { formatBytes, formatNumber } from '../lib/format'
import EmptyState from '../components/EmptyState.vue'
import AppSelect from '../components/AppSelect.vue'
import FilePicker from '../components/FilePicker.vue'
import SummaryItemPicker from '../components/SummaryItemPicker.vue'
import { useToast } from '../composables/useToast'

interface RowEdit {
  row: SummaryRowResponse
  items: string[]
  /** null = ghép từ items; chuỗi = người dùng sửa toàn bộ câu. */
  summaryText: string | null
  /** null = giữ nguyên; chuỗi rỗng = xoá số tờ. */
  sheetNumber: string | null
}

interface DraftEntry {
  row: SummaryRowResponse
  items: string[]
  summaryText: string | null
  sheetNumber: string | null
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

const previewOpen = ref(false)
const previewMode = ref<'full' | 'filtered'>('full')
const previewLoading = ref(false)
const previewRows = ref<SummaryPreviewRowResponse[]>([])
const previewError = ref<string | null>(null)
const filteredPreview = ref<SummaryFilteredPreviewResponse | null>(null)
const exporting = ref(false)
const filteredExporting = ref(false)
const exportingIssues = ref(false)

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

function commit(row: SummaryRowResponse, patch: Partial<Omit<RowEdit, 'row'>>): void {
  const current = editFor(row)
  const nextEdit: RowEdit = { ...current, ...patch }
  const summary = nextEdit.summaryText ?? composeSummary(row, nextEdit.items)
  const sheet = nextEdit.sheetNumber ?? row.sheetNumber
  // Dòng đăng ký kết hôn được backend chuẩn hoá tự động khi xuất, nên so sánh
  // với bản đã chuẩn hoá để không tạo thay đổi thừa.
  const baseline = row.needsNormalization ? row.normalizedSummary : row.summary
  const changed = summary !== baseline || sheet !== row.sheetNumber

  const next = new Map(edits.value)
  if (changed) {
    next.set(row.rowNumber, nextEdit)
  } else {
    next.delete(row.rowNumber)
  }

  edits.value = next
}

function setRowItems(row: SummaryRowResponse, items: string[]): void {
  commit(row, { items, summaryText: null })
}

function onSheetChange(row: SummaryRowResponse, event: Event): void {
  const value = (event.target as HTMLInputElement).value.trim()
  commit(row, { sheetNumber: value === row.sheetNumber ? null : value })
}

function openSummaryEditor(row: SummaryRowResponse): void {
  editingSummaryRow.value = row.rowNumber
  summaryDraft.value = summaryFor(row)
}

function closeSummaryEditor(): void {
  editingSummaryRow.value = null
  summaryDraft.value = ''
}

function saveSummaryEditor(row: SummaryRowResponse): void {
  const text = summaryDraft.value.trim()
  commit(row, { summaryText: text })
  closeSummaryEditor()
  toast.success('Đã cập nhật trích yếu', `Dòng ${row.rowNumber} sẽ được ghi khi xuất.`)
}

function resetEdits(): void {
  if (edits.value.size === 0) return
  if (!window.confirm('Bỏ toàn bộ thay đổi chưa xuất?')) return
  edits.value = new Map()
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

  for (const row of rows.value) {
    if (!selected.value.has(row.rowNumber)) continue
    const current = next.get(row.rowNumber) ?? {
      row,
      items: row.items,
      summaryText: null,
      sheetNumber: null,
    }
    const nextEdit: RowEdit = { ...current, items: bulkItems.value, summaryText: null }
    const summary = composeSummary(row, nextEdit.items)
    const sheet = nextEdit.sheetNumber ?? row.sheetNumber
    if (summary !== row.summary || sheet !== row.sheetNumber) {
      next.set(row.rowNumber, nextEdit)
    } else {
      next.delete(row.rowNumber)
    }
    applied++
  }

  edits.value = next
  bulkApplying.value = false
  toast.success(
    'Đã áp dụng danh mục',
    `${formatNumber(applied)} dòng trong trang hiện tại đã nhận ${bulkItems.value.length} loại giấy tờ.`,
  )
}

/** Đưa các dòng đang chọn về đúng tiền tố chuẩn, giữ nguyên danh mục giấy tờ. */
function normalizeSelected(): void {
  if (selectedNormalizableCount.value === 0) {
    toast.info('Không có dòng sai mở đầu', 'Các dòng đang chọn đã đúng mẫu.')
    return
  }

  const next = new Map(edits.value)
  let applied = 0

  for (const row of rows.value) {
    if (!selected.value.has(row.rowNumber) || !row.needsNormalization) continue

    const current = next.get(row.rowNumber) ?? {
      row,
      items: row.items,
      summaryText: null,
      sheetNumber: null,
    }

    // Không ghi đè câu người dùng đã sửa tay; bản xuất vẫn tự chuẩn hoá tiền tố.
    if (current.summaryText !== null) continue

    next.set(row.rowNumber, {
      ...current,
      summaryText: composeSummary(row, current.items),
    })
    applied++
  }

  edits.value = next
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
    edits.value = new Map()
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
    await fetchRows()
  } catch (caught) {
    inspectError.value = errorMessage(caught)
  } finally {
    inspecting.value = false
  }
}

function queryParams(): Record<string, string | number | boolean | null | undefined> {
  return {
    year: year.value === '' ? undefined : year.value,
    yearFrom: yearFrom.value === '' ? undefined : yearFrom.value,
    yearTo: yearTo.value === '' ? undefined : yearTo.value,
    type: type.value || undefined,
    search: search.value.trim() || undefined,
    onlyExisting: onlyExisting.value || undefined,
    numberMode: numberMode.value === 'all' ? undefined : numberMode.value,
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

  void deleteSessionSilent(active.sessionId)
  clearDraft()
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
    const result = await request<SummaryPreviewRowResponse[]>(
      `/api/summary/${active.sessionId}/preview`,
      { method: 'POST', body: { changes: allChanges.value } },
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

async function exportFile(): Promise<void> {
  const active = session.value
  if (!active) return

  exporting.value = true
  try {
    const result = await download(`/api/summary/${active.sessionId}/export`, {
      method: 'POST',
      body: { changes: allChanges.value },
    })

    saveBlob(result.blob, result.fileName)
    const normalized = previewRows.value.filter((row) => row.summaryChanged).length
    toast.success(
      'Đã xuất file Excel',
      `${result.fileName} · ${formatNumber(previewRows.value.length)} dòng được cập nhật${normalized > 0 ? ` · ${formatNumber(normalized)} dòng chuẩn hoá` : ''}.`,
    )
    previewOpen.value = false
    clearDraft()
    draftSavedAt.value = null
    draftStatus.value = 'idle'
  } catch (caught) {
    toast.error('Không xuất được file', errorMessage(caught))
  } finally {
    exporting.value = false
  }
}

async function exportFilteredFile(): Promise<void> {
  const active = session.value
  if (!active || !scopeReady.value) return

  filteredExporting.value = true
  try {
    const result = await download(`/api/summary/${active.sessionId}/export-filtered`, {
      method: 'POST',
      body: { changes: allChanges.value, scope: scopeRequest.value },
    })

    saveBlob(result.blob, result.fileName)
    const keptRows = filteredPreview.value?.totalRows ?? total.value
    const changedRows = filteredPreview.value?.changedRows ?? 0
    const normalizedRows = filteredPreview.value?.normalizedRows ?? 0
    toast.success(
      'Đã xuất bản lọc',
      `${result.fileName} · ${formatNumber(keptRows)} hồ sơ được giữ · ${formatNumber(changedRows)} dòng cập nhật${normalizedRows > 0 ? ` · ${formatNumber(normalizedRows)} dòng chuẩn hoá` : ''}.`,
    )
    previewOpen.value = false
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

function saveDraft(): void {
  const active = session.value
  if (!active) return

  try {
    if (edits.value.size === 0) {
      localStorage.removeItem(draftKey(active.fileName))
      draftSavedAt.value = null
      draftStatus.value = 'idle'
      return
    }

    const payload: DraftPayload = {
      fileName: active.fileName,
      savedAt: new Date().toISOString(),
      edits: [...edits.value.values()].map((edit) => ({
        row: edit.row,
        items: edit.items,
        summaryText: edit.summaryText,
        sheetNumber: edit.sheetNumber,
      })),
    }
    localStorage.setItem(draftKey(active.fileName), JSON.stringify(payload))
    draftSavedAt.value = new Date(payload.savedAt)
    draftStatus.value = 'saved'
  } catch {
    // localStorage có thể bị chặn hoặc đầy; bỏ qua và vẫn làm việc trong bộ nhớ.
  }
}

function restoreDraft(fileName: string): void {
  try {
    const raw = localStorage.getItem(draftKey(fileName))
    if (!raw) return

    const payload = JSON.parse(raw) as DraftPayload
    if (!payload.edits || payload.edits.length === 0) {
      localStorage.removeItem(draftKey(fileName))
      return
    }

    const savedAt = new Date(payload.savedAt)
    const label = savedAt.toLocaleString('vi-VN')
    if (!window.confirm(`Tìm thấy bản nháp ${payload.edits.length} dòng cho file này (${label}). Khôi phục?`)) {
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
      })
    }

    edits.value = next
    draftSavedAt.value = savedAt
    draftStatus.value = 'saved'
    toast.success('Đã khôi phục bản nháp', `${formatNumber(next.size)} dòng thay đổi đã được nạp.`)
  } catch {
    // Bản nháp hỏng: bỏ qua để không chặn mở file.
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

  if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
    event.preventDefault()
    if (session.value && scopeReady.value) void openFilteredPreview()
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
})

watch([year, yearFrom, yearTo, type, onlyExisting, onlyMissingSheetNumber, onlyMissingDocumentNumber, onlyMissingNumber, onlyNeedsNormalization, missingSheetNumberFirst], () => {
  if (!session.value) return
  if (filterTimer) window.clearTimeout(filterTimer)
  filterTimer = window.setTimeout(() => {
    page.value = 1
    void fetchRows()
  }, 300)
})

watch(search, () => {
  if (!session.value) return
  if (searchTimer) window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(() => {
    page.value = 1
    void fetchRows()
  }, 350)
})

watch([numberMode, numberFrom, numberTo, singleNumber], () => {
  if (!session.value) return
  if (filterTimer) window.clearTimeout(filterTimer)
  filterTimer = window.setTimeout(() => {
    page.value = 1
    void fetchRows()
  }, 350)
})

watch(pageSize, () => {
  if (!session.value) return
  page.value = 1
  void fetchRows()
})

onMounted(() => {
  window.addEventListener('keydown', onGlobalKeydown)
})

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onGlobalKeydown)
  if (searchTimer) window.clearTimeout(searchTimer)
  if (filterTimer) window.clearTimeout(filterTimer)
  if (autosaveTimer) window.clearTimeout(autosaveTimer)
  if (session.value) {
    saveDraft()
    void deleteSessionSilent(session.value.sessionId)
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
          <button class="btn btn-ghost btn-sm" type="button" :disabled="loadingRows" @click="fetchRows">
            <RefreshCw :size="14" />
            Làm mới
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
              Số hồ sơ lấy từ phần đầu cột B (ví dụ “36A” tính là 36). Bản lọc chỉ giữ sheet ML và
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
              Muốn sửa nhiều dòng cùng lúc: tích ô ở đầu dòng rồi chọn giấy tờ ở thanh trên.
            </div>
            <div v-if="selected.size > 0" class="summary-bulkbar">
              <div class="row wrap" style="gap: 8px; align-items: center">
                <span class="badge badge-brand">{{ formatNumber(selected.size) }} dòng đã chọn</span>
                <SummaryItemPicker
                  v-model="bulkItems"
                  :catalog="session.catalog"
                  placeholder="Giấy tờ áp dụng hàng loạt..."
                />
                <button class="btn btn-primary btn-sm" type="button" :disabled="bulkApplying" @click="applyBulk">
                  <Check :size="14" />
                  Áp dụng cho dòng đã chọn
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
                    <th>Trích yếu</th>
                    <th style="width: 320px">Giấy tờ trong ngoặc</th>
                  </tr>
                </thead>
                <tbody>
                  <template v-for="row in rows" :key="row.rowNumber">
                    <tr :class="{ 'is-edited': edits.has(row.rowNumber) }">
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
                      </td>
                      <td>
                        <span class="badge badge-neutral">{{ row.year ?? '—' }}</span>
                      </td>
                      <td>
                        <input
                          class="input summary-sheet-input"
                          :class="{ 'is-missing': !sheetFor(row).trim() }"
                          :value="sheetFor(row)"
                          placeholder="VD: 01-20"
                          :aria-label="`Số tờ dòng ${row.rowNumber}`"
                          @change="onSheetChange(row, $event)"
                        />
                        <span v-if="!sheetFor(row).trim()" class="summary-missing-tag">Thiếu</span>
                      </td>
                      <td>
                        <div class="summary-base" :title="summaryFor(row)">{{ summaryFor(row) }}</div>
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
                          :placeholder="row.hasDocumentList ? 'Sửa danh mục...' : 'Chọn giấy tờ...'"
                          @update:model-value="(items) => setRowItems(row, items)"
                        />

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
                      <td colspan="6">
                        <div class="summary-editor">
                          <div class="summary-editor-head">
                            <strong>Sửa toàn bộ trích yếu · dòng {{ row.rowNumber }}</strong>
                            <span class="hint">Hồ sơ {{ row.numberLabel || '—' }}</span>
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
            <kbd>Esc</kbd> đóng.
          </div>
        </div>
      </section>

      <div v-if="changedCount > 0" class="summary-actionbar">
        <div>
          <strong>{{ formatNumber(changedCount) }}</strong>
          <span> dòng đã sửa, chưa xuất</span>
        </div>
        <div class="row wrap" style="gap: 8px">
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
          <button class="btn btn-accent" type="button" @click="openPreview">
            <FilePenLine :size="16" />
            Xem trước rồi xuất toàn bộ
          </button>
        </div>
      </div>
    </template>

    <Teleport to="body">
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
                    </div>
                    <ArrowRight class="summary-diff-arrow" :size="20" aria-hidden="true" />
                    <div class="summary-diff-col">
                      <div class="summary-preview-label">File sẽ tải về</div>
                      <p class="summary-preview-text">{{ row.summary }}</p>
                      <p v-if="row.sheetNumberChanged" class="summary-preview-text">
                        Số tờ: {{ row.sheetNumber || '(xoá)' }}
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
              @click="exportFilteredFile"
            >
              <Download :size="16" />
              {{ filteredExporting ? 'Đang xuất bản lọc...' : 'Xuất bản lọc' }}
            </button>
            <button
              v-else
              class="btn btn-accent"
              type="button"
              :disabled="exporting || previewRows.length === 0"
              @click="exportFile"
            >
              <Download :size="16" />
              {{ exporting ? 'Đang xuất file...' : 'Xuất file Excel' }}
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>
