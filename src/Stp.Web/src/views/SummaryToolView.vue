<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import {
  ArrowLeft,
  ArrowRight,
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
  SummaryInspectResponse,
  SummaryPreviewRowResponse,
  SummaryRowResponse,
  SummaryRowsResponse,
} from '../api/types'
import { errorMessage } from '../lib/errors'
import { formatBytes, formatNumber } from '../lib/format'
import EmptyState from '../components/EmptyState.vue'
import FilePicker from '../components/FilePicker.vue'
import SummaryItemPicker from '../components/SummaryItemPicker.vue'
import { useToast } from '../composables/useToast'

interface RowEdit {
  row: SummaryRowResponse
  items: string[]
}

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
const type = ref('')
const search = ref('')
const onlyExisting = ref(false)

const edits = ref(new Map<number, RowEdit>())
const changedCount = computed(() => edits.value.size)

const previewOpen = ref(false)
const previewLoading = ref(false)
const previewRows = ref<SummaryPreviewRowResponse[]>([])
const previewError = ref<string | null>(null)
const exporting = ref(false)

const totalPages = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))
const rangeStart = computed(() => (total.value === 0 ? 0 : (page.value - 1) * pageSize.value + 1))
const rangeEnd = computed(() => Math.min(total.value, page.value * pageSize.value))
const hasAliasParenthesis = (row: SummaryRowResponse): boolean =>
  !row.hasDocumentList && row.summary.trimEnd().endsWith(')')

const previewItems = computed(() =>
  previewRows.value
    .filter((row) => row.changed)
    .map((row) => ({
      ...row,
      edit: edits.value.get(row.rowNumber) ?? null,
    })),
)

const exportChanges = computed(() =>
  previewItems.value.map((row) => ({
    rowNumber: row.rowNumber,
    items: row.edit?.items ?? [],
  })),
)

let searchTimer: number | undefined

function itemsFor(row: SummaryRowResponse): string[] {
  return edits.value.get(row.rowNumber)?.items ?? row.items
}

function normalizeText(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
    .toLowerCase()
}

function sameItems(left: string[], right: string[]): boolean {
  if (left.length !== right.length) return false
  const keys = new Set(left.map(normalizeText))
  return right.every((item) => keys.has(normalizeText(item)))
}

function setRowItems(row: SummaryRowResponse, items: string[]): void {
  const next = new Map(edits.value)
  if (sameItems(items, row.items)) {
    next.delete(row.rowNumber)
  } else {
    next.set(row.rowNumber, { row, items })
  }

  edits.value = next
}

function resetEdits(): void {
  edits.value = new Map()
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
    previewRows.value = []
    previewOpen.value = false
    year.value = ''
    type.value = ''
    search.value = ''
    onlyExisting.value = false
    page.value = 1
    files.value = []

    toast.success(
      'Đã đọc workbook',
      `${formatNumber(data.rowCount)} hồ sơ · ${formatNumber(data.catalog.length)} loại giấy tờ · ${formatNumber(data.existingListCount)} dòng đã có danh mục.`,
    )

    await fetchRows()
  } catch (caught) {
    inspectError.value = errorMessage(caught)
  } finally {
    inspecting.value = false
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
        year: year.value === '' ? undefined : year.value,
        type: type.value || undefined,
        search: search.value.trim() || undefined,
        onlyExisting: onlyExisting.value || undefined,
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
  session.value = null
  rows.value = []
  total.value = 0
  edits.value = new Map()
  previewRows.value = []
  previewOpen.value = false
  inspectError.value = null
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

  const changes = [...edits.value.values()]
    .sort((left, right) => left.row.rowNumber - right.row.rowNumber)
    .map((edit) => ({ rowNumber: edit.row.rowNumber, items: edit.items }))

  if (changes.length === 0) {
    toast.info('Chưa có thay đổi', 'Chọn giấy tờ cho ít nhất một hồ sơ trước khi xem trước.')
    return
  }

  previewOpen.value = true
  previewLoading.value = true
  previewError.value = null
  previewRows.value = []

  try {
    const result = await request<SummaryPreviewRowResponse[]>(
      `/api/summary/${active.sessionId}/preview`,
      { method: 'POST', body: { changes } },
    )

    previewRows.value = result
    if (result.every((row) => !row.changed)) {
      previewError.value = 'Các lựa chọn trùng với nội dung hiện có, không có gì để xuất.'
    }
  } catch (caught) {
    previewError.value = errorMessage(caught)
  } finally {
    previewLoading.value = false
  }
}

async function exportFile(): Promise<void> {
  const active = session.value
  if (!active || exportChanges.value.length === 0) return

  exporting.value = true
  try {
    const result = await download(`/api/summary/${active.sessionId}/export`, {
      method: 'POST',
      body: { changes: exportChanges.value },
    })

    saveBlob(result.blob, result.fileName)
    toast.success(
      'Đã xuất file Excel',
      `${result.fileName} · ${formatNumber(exportChanges.value.length)} dòng được cập nhật.`,
    )
    previewOpen.value = false
  } catch (caught) {
    toast.error('Không xuất được file', errorMessage(caught))
  } finally {
    exporting.value = false
  }
}

watch([year, type, onlyExisting], () => {
  if (!session.value) return
  page.value = 1
  void fetchRows()
})

watch(search, () => {
  if (!session.value) return
  if (searchTimer) window.clearTimeout(searchTimer)
  searchTimer = window.setTimeout(() => {
    page.value = 1
    void fetchRows()
  }, 350)
})

onBeforeUnmount(() => {
  if (searchTimer) window.clearTimeout(searchTimer)
  if (session.value) {
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
            <h1 class="page-title" style="font-size: 20px">Bổ sung danh mục giấy tờ vào trích yếu</h1>
            <p class="page-subtitle" style="margin-top: 4px">
              Chọn workbook, tích các loại giấy tờ cần thêm, rồi xuất lại đúng file gốc.
              Công cụ chỉ thay phần trong ngoặc cuối cột trích yếu.
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
              Chỉ các ô trích yếu được chọn mới được ghi lại.
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
                <span class="badge badge-neutral">{{ formatBytes(session.fileSizeBytes) }}</span>
              </div>
              <div class="hint" style="margin-top: 4px">
                {{ formatNumber(session.rowCount) }} hồ sơ · {{ formatNumber(session.catalog.length) }} loại giấy tờ ·
                {{ formatNumber(session.existingListCount) }} dòng đã có danh mục
              </div>
            </div>
          </div>

          <div class="row wrap">
            <button class="btn btn-secondary btn-sm" type="button" @click="closeSession">
              <X :size="14" />
              Đóng workbook
            </button>
          </div>
        </div>
      </section>

      <div class="grid grid-4 summary-stats">
        <div class="card summary-stat">
          <div class="card-body">
            <span class="summary-stat-label">Hồ sơ</span>
            <strong class="summary-stat-value">{{ formatNumber(session.rowCount) }}</strong>
            <span class="hint">Dòng có số hồ sơ</span>
          </div>
        </div>
        <div class="card summary-stat">
          <div class="card-body">
            <span class="summary-stat-label">Đã có danh mục</span>
            <strong class="summary-stat-value">{{ formatNumber(session.existingListCount) }}</strong>
            <span class="hint">Ngoặc cuối là danh mục giấy tờ</span>
          </div>
        </div>
        <div class="card summary-stat">
          <div class="card-body">
            <span class="summary-stat-label">Năm hồ sơ</span>
            <strong class="summary-stat-value">{{ formatNumber(session.years.length) }}</strong>
            <span class="hint">Từ {{ session.years[0]?.year ?? '—' }} đến {{ session.years.at(-1)?.year ?? '—' }}</span>
          </div>
        </div>
        <div class="card summary-stat">
          <div class="card-body">
            <span class="summary-stat-label">Đang chờ xuất</span>
            <strong class="summary-stat-value">{{ formatNumber(changedCount) }}</strong>
            <span class="hint">Hồ sơ đã đổi danh mục</span>
          </div>
        </div>
      </div>

      <section class="card">
        <div class="card-header summary-filter-header">
          <div>
            <div class="card-title">Chọn giấy tờ cho từng hồ sơ</div>
            <div class="card-subtitle">
              Bấm vào ô giấy tờ để mở danh mục, tích một hoặc nhiều mục. Danh sách chỉ được ghép vào ngoặc cuối.
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
              <select v-model="year" class="select">
                <option value="">Tất cả các năm</option>
                <option v-for="item in session.years" :key="item.year" :value="item.year">
                  {{ item.year }} ({{ formatNumber(item.count) }})
                </option>
              </select>
            </label>

            <label class="field">
              <span class="label">Loại hồ sơ</span>
              <select v-model="type" class="select">
                <option value="">Tất cả loại</option>
                <option v-for="item in session.types" :key="item.name" :value="item.name">
                  {{ item.name }} ({{ formatNumber(item.count) }})
                </option>
              </select>
            </label>

            <label class="field summary-search">
              <span class="label">Tìm kiếm</span>
              <span class="search-box">
                <Search :size="15" />
                <input
                  v-model="search"
                  class="input"
                  placeholder="Số hồ sơ, tên người, nội dung trích yếu..."
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
              description="Thử bỏ bộ lọc năm, loại hồ sơ hoặc từ khoá tìm kiếm."
            />
          </div>

          <div v-else class="table-wrap">
            <table class="table table-hover summary-table">
              <thead>
                <tr>
                  <th style="width: 96px">Hồ sơ</th>
                  <th style="width: 74px">Năm</th>
                  <th>Trích yếu gốc (không thay đổi)</th>
                  <th style="width: 330px">Giấy tờ trong ngoặc</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in rows"
                  :key="row.rowNumber"
                  :class="{ 'is-edited': edits.has(row.rowNumber) }"
                >
                  <td>
                    <div class="summary-number">{{ row.numberLabel }}</div>
                    <div class="hint">Dòng {{ row.rowNumber }}</div>
                  </td>
                  <td>
                    <span class="badge badge-neutral">{{ row.year ?? '—' }}</span>
                  </td>
                  <td>
                    <div class="summary-base" :title="row.summary">{{ row.baseSummary }}</div>
                    <div class="summary-meta">
                      <span v-if="row.hasDocumentList" class="badge badge-info">
                        Đã có {{ row.items.length }} mục
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
              </tbody>
            </table>
          </div>

          <div class="summary-pager">
            <span class="hint">
              {{ formatNumber(rangeStart) }}–{{ formatNumber(rangeEnd) }} / {{ formatNumber(total) }} hồ sơ
            </span>
            <div class="row" style="gap: 8px">
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
        </div>
      </section>

      <div v-if="changedCount > 0" class="summary-actionbar">
        <div>
          <strong>{{ formatNumber(changedCount) }}</strong>
          <span> hồ sơ đã thay đổi danh mục giấy tờ</span>
        </div>
        <div class="row" style="gap: 8px">
          <button class="btn btn-ghost btn-sm" type="button" @click="resetEdits">
            <Eraser :size="14" />
            Hoàn tác tất cả
          </button>
          <button class="btn btn-accent" type="button" @click="openPreview">
            <FilePenLine :size="16" />
            Xem trước &amp; xuất
          </button>
        </div>
      </div>
    </template>

    <Teleport to="body">
      <div v-if="previewOpen" class="modal-backdrop" @click.self="previewOpen = false">
        <div class="modal summary-preview-modal">
          <div class="modal-header">
            <div>
              <div class="card-title">Xem trước thay đổi trích yếu</div>
              <div class="card-subtitle">
                Chỉ phần trong ngoặc cuối thay đổi. Phần trích yếu phía trước giữ nguyên từng ký tự.
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

            <div v-else class="summary-preview-list">
              <article v-for="row in previewItems" :key="row.rowNumber" class="summary-preview-item">
                <header class="row-between wrap">
                  <div class="row" style="gap: 8px">
                    <span class="badge badge-brand">Dòng {{ row.rowNumber }}</span>
                    <span class="hint">Hồ sơ {{ row.edit?.row.numberLabel }}</span>
                    <span v-if="row.edit?.row.year" class="hint">Năm {{ row.edit.row.year }}</span>
                  </div>
                  <span class="badge badge-success">Sẽ cập nhật</span>
                </header>

                <div class="summary-preview-label">Trích yếu gốc</div>
                <p class="summary-preview-text muted">{{ row.edit?.row.summary }}</p>

                <div class="summary-preview-label">Sau khi bổ sung</div>
                <p class="summary-preview-text">{{ row.summary }}</p>

                <div v-if="row.edit" class="summary-chips">
                  <span v-for="item in row.edit.items" :key="item" class="chip">{{ item }}</span>
                </div>
              </article>
            </div>
          </div>

          <div class="modal-footer">
            <span class="hint grow">
              {{ formatNumber(exportChanges.length) }} dòng sẽ được ghi. Các phần khác của workbook giữ nguyên.
            </span>
            <button class="btn btn-secondary" type="button" @click="previewOpen = false">Quay lại</button>
            <button
              class="btn btn-accent"
              type="button"
              :disabled="exporting || exportChanges.length === 0"
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
