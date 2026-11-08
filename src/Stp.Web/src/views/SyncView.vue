<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  ChevronRight,
  CircleCheck,
  CircleX,
  Database,
  FileSpreadsheet,
  Info,
  ListChecks,
  RefreshCw,
  TriangleExclamationPoint,
} from '@lucide/vue'
import { request, ApiError } from '../api/client'
import type {
  SyncResponse,
  SyncSessionDetailResponse,
  SyncSessionSummaryResponse,
  WorkbookResponse,
} from '../api/types'
import { errorMessage } from '../lib/errors'
import { formatDateTime, formatNumber } from '../lib/format'
import AppSelect from '../components/AppSelect.vue'
import EmptyState from '../components/EmptyState.vue'
import FilePicker from '../components/FilePicker.vue'
import StatusBadge from '../components/StatusBadge.vue'
import { useToast } from '../composables/useToast'

const toast = useToast()

const files = ref<File[]>([])
const workbooks = ref<WorkbookResponse[]>([])
const workbookId = ref('')
const syncing = ref(false)
const result = ref<SyncResponse | null>(null)
const syncError = ref<string | null>(null)

const sessions = ref<SyncSessionSummaryResponse[]>([])
const sessionsLoading = ref(false)
const selectedSession = ref<SyncSessionDetailResponse | null>(null)
const sessionLoading = ref(false)

const selectedWorkbook = computed(() =>
  workbooks.value.find((workbook) => workbook.id === workbookId.value) ?? null,
)

const workbookOptions = computed(() => [
  { value: '', label: 'Tự nhận diện / tạo workbook mới' },
  ...workbooks.value.map((book) => ({
    value: book.id,
    label: `${book.name} · v${book.version}`,
    countLabel: `${formatNumber(book.dossierCount)} hồ sơ`,
  })),
])

function changeTone(changeType: string): string {
  switch (changeType) {
    case 'Added':
      return 'badge-success'
    case 'Updated':
      return 'badge-info'
    case 'Deleted':
      return 'badge-danger'
    default:
      return 'badge-neutral'
  }
}

async function loadWorkbooks(): Promise<void> {
  try {
    workbooks.value = await request<WorkbookResponse[]>('/api/workbooks')
  } catch (caught) {
    toast.error('Không tải được danh sách workbook', errorMessage(caught))
  }
}

async function loadSessions(): Promise<void> {
  sessionsLoading.value = true
  try {
    sessions.value = await request<SyncSessionSummaryResponse[]>('/api/sync/sessions?take=30')
  } catch (caught) {
    toast.error('Không tải được lịch sử đồng bộ', errorMessage(caught))
  } finally {
    sessionsLoading.value = false
  }
}

async function openSession(id: string): Promise<void> {
  sessionLoading.value = true
  try {
    selectedSession.value = await request<SyncSessionDetailResponse>(`/api/sync/sessions/${id}`)
  } catch (caught) {
    toast.error('Không tải được chi tiết phiên', errorMessage(caught))
  } finally {
    sessionLoading.value = false
  }
}

async function submit(): Promise<void> {
  const file = files.value[0]
  if (!file) {
    syncError.value = 'Hãy chọn một workbook .xlsx hoặc .xlsm để đồng bộ.'
    return
  }

  syncing.value = true
  syncError.value = null
  result.value = null

  try {
    const form = new FormData()
    form.append('file', file, file.name)

    if (workbookId.value) {
      form.append('workbookId', workbookId.value)
      if (selectedWorkbook.value) {
        form.append('baseVersion', String(selectedWorkbook.value.version))
      }
    }

    result.value = await request<SyncResponse>('/api/sync', { method: 'POST', form })

    if (result.value.success) {
      toast.success(
        'Đồng bộ thành công',
        `Thêm ${result.value.addedCount}, cập nhật ${result.value.updatedCount}, xoá ${result.value.deletedCount}.`,
      )
      files.value = []
      await Promise.all([loadWorkbooks(), loadSessions()])
    } else {
      syncError.value = result.value.errorMessage ?? 'Đồng bộ thất bại.'
    }
  } catch (caught) {
    if (caught instanceof ApiError && caught.payload && typeof caught.payload === 'object') {
      const payload = caught.payload as Partial<SyncResponse>
      if (typeof payload.success === 'boolean') {
        result.value = payload as SyncResponse
        syncError.value = payload.errorMessage ?? 'Đồng bộ thất bại.'
      } else {
        syncError.value = errorMessage(caught)
      }
    } else {
      syncError.value = errorMessage(caught)
    }
    toast.error('Đồng bộ thất bại', syncError.value ?? undefined)
  } finally {
    syncing.value = false
  }
}

onMounted(() => {
  void loadWorkbooks()
  void loadSessions()
})
</script>

<template>
  <div class="stack" style="gap: 18px">
    <div class="alert alert-info">
      <Database :size="17" />
      <div>
        <strong>Excel là nguồn chính.</strong>
        Hệ thống chỉ đọc sheet <strong>ML</strong> trong file tải lên, sau đó thêm/sửa/xoá hồ sơ trong SQL Server.
        Sheet in trong Excel vẫn do file quản lý. Xung đột được xử lý theo nguyên tắc
        <strong>ghi sau thắng</strong> và ghi vào nhật ký đối chiếu.
      </div>
    </div>

    <div class="convert-layout">
      <div class="stack">
        <div class="card">
          <div class="card-header">
            <div>
              <div class="card-title">Tải workbook đã chỉnh sửa</div>
              <div class="card-subtitle">Hỗ trợ .xlsx và .xlsm; chỉ sheet ML được đọc</div>
            </div>
            <FileSpreadsheet :size="20" class="muted" />
          </div>
          <div class="card-body">
            <FilePicker v-model="files" :multiple="false" hint="Chọn đúng file Excel đã sửa trên máy" />

            <label class="field" style="margin-top: 14px">
              <span class="label">Áp vào workbook nào?</span>
              <AppSelect
                v-model="workbookId"
                :options="workbookOptions"
                aria-label="Chọn workbook áp dụng"
              />
              <span class="hint">
                Chọn đúng workbook để kiểm tra phiên bản. Nếu để trống, hệ thống tìm theo tên file nguồn
                hoặc tạo workbook mới.
              </span>
            </label>

            <div v-if="selectedWorkbook" class="row wrap" style="margin-top: 10px">
              <span class="chip">Phiên bản hiện tại: v{{ selectedWorkbook.version }}</span>
              <span class="chip">Đồng bộ lần cuối: {{ formatDateTime(selectedWorkbook.lastSyncedAt) }}</span>
            </div>

            <div v-if="syncError" class="alert alert-danger" style="margin-top: 12px">
              <TriangleExclamationPoint :size="17" />
              <div>{{ syncError }}</div>
            </div>

            <button
              class="btn btn-primary btn-block btn-lg"
              type="button"
              style="margin-top: 14px"
              :disabled="syncing || files.length === 0"
              @click="submit"
            >
              <span v-if="syncing" class="spinner" />
              <RefreshCw v-else :size="17" />
              {{ syncing ? 'Đang đồng bộ...' : 'Đồng bộ vào SQL Server' }}
            </button>
            <div class="hint" style="margin-top: 8px">
              File lớn (hơn 20.000 hồ sơ) có thể mất 20–30 giây. Không đóng trang trong lúc chờ.
            </div>
          </div>
        </div>

        <div v-if="result" class="card">
          <div class="card-header">
            <div class="row">
              <component
                :is="result.success ? CircleCheck : CircleX"
                :size="20"
                :style="{ color: result.success ? 'var(--success)' : 'var(--danger)' }"
              />
              <div>
                <div class="card-title">
                  {{ result.success ? 'Đồng bộ hoàn tất' : 'Đồng bộ thất bại' }}
                </div>
                <div class="card-subtitle">
                  Phiên {{ result.syncSessionId }}
                  <template v-if="result.workbookId"> · workbook {{ result.workbookId }}</template>
                </div>
              </div>
            </div>
            <button class="btn btn-ghost btn-sm" type="button" @click="openSession(result.syncSessionId)">
              Xem chi tiết
              <ChevronRight :size="14" />
            </button>
          </div>
          <div class="card-body">
            <div class="metric-grid">
              <div class="metric">
                <div class="metric-label">Thêm mới</div>
                <div class="metric-value">{{ formatNumber(result.addedCount) }}</div>
              </div>
              <div class="metric">
                <div class="metric-label">Cập nhật</div>
                <div class="metric-value">{{ formatNumber(result.updatedCount) }}</div>
              </div>
              <div class="metric">
                <div class="metric-label">Đã xoá</div>
                <div class="metric-value">{{ formatNumber(result.deletedCount) }}</div>
              </div>
              <div class="metric">
                <div class="metric-label">Xung đột</div>
                <div class="metric-value">{{ formatNumber(result.conflictCount) }}</div>
              </div>
            </div>
            <div v-if="result.errorMessage" class="alert alert-danger" style="margin-top: 12px">
              {{ result.errorMessage }}
            </div>
          </div>
        </div>

        <div class="alert">
          <Info :size="17" />
          <div>
            Muốn đưa dữ liệu từ web trở lại Excel? Hãy dùng nút <strong>Đồng bộ</strong> trong file
            .xlsm (bộ công cụ đã nhúng) hoặc tải lại file từ kho hồ sơ. Luồng hiện tại ưu tiên
            Excel → web để tránh ghi đè nhầm.
          </div>
        </div>
      </div>

      <div class="card">
        <div class="card-header">
          <div>
            <div class="card-title">Lịch sử đồng bộ</div>
            <div class="card-subtitle">30 phiên gần nhất</div>
          </div>
          <button class="btn btn-ghost btn-sm" type="button" :disabled="sessionsLoading" @click="loadSessions">
            <RefreshCw :size="14" />
            Làm mới
          </button>
        </div>
        <div class="card-body" style="padding-top: 10px">
          <div v-if="sessionsLoading" class="loading-block"><span class="spinner" /> Đang tải...</div>
          <EmptyState
            v-else-if="sessions.length === 0"
            title="Chưa có lần đồng bộ"
            description="Kết quả các lần đồng bộ sẽ xuất hiện tại đây."
          />
          <div v-else class="stack" style="gap: 0">
            <button
              v-for="session in sessions"
              :key="session.id"
              type="button"
              class="session-row"
              :class="{ selected: selectedSession?.summary.id === session.id }"
              @click="openSession(session.id)"
            >
              <StatusBadge kind="sync" :value="session.status" />
              <span class="grow" style="min-width: 0; text-align: left">
                <span class="file-name">{{ session.sourceFileName }}</span>
                <span class="file-meta">{{ formatDateTime(session.uploadedAt) }}</span>
              </span>
              <span class="text-right text-xs">
                <span class="strong">+{{ session.addedCount }} ~{{ session.updatedCount }} -{{ session.deletedCount }}</span>
                <span class="muted" style="display: block">{{ session.conflictCount }} xung đột</span>
              </span>
              <ChevronRight :size="15" class="muted" />
            </button>
          </div>
        </div>
      </div>
    </div>

    <div v-if="sessionLoading" class="loading-block"><span class="spinner lg" /> Đang tải chi tiết phiên...</div>

    <div v-else-if="selectedSession" class="card">
      <div class="card-header">
        <div>
          <div class="card-title">Chi tiết phiên đồng bộ</div>
          <div class="card-subtitle">
            {{ selectedSession.summary.sourceFileName }} · {{ formatDateTime(selectedSession.summary.uploadedAt) }}
          </div>
        </div>
        <StatusBadge kind="sync" :value="selectedSession.summary.status" />
      </div>
      <div class="card-body">
        <div class="row wrap" style="margin-bottom: 14px">
          <span class="chip">Thêm {{ formatNumber(selectedSession.summary.addedCount) }}</span>
          <span class="chip">Cập nhật {{ formatNumber(selectedSession.summary.updatedCount) }}</span>
          <span class="chip">Xoá {{ formatNumber(selectedSession.summary.deletedCount) }}</span>
          <span class="chip">Xung đột {{ formatNumber(selectedSession.summary.conflictCount) }}</span>
        </div>

        <div v-if="selectedSession.summary.errorMessage" class="alert alert-danger" style="margin-bottom: 12px">
          {{ selectedSession.summary.errorMessage }}
        </div>

        <EmptyState
          v-if="selectedSession.changes.length === 0"
          title="Không có thay đổi"
          description="Lần đồng bộ này không phát hiện khác biệt nào so với dữ liệu đang lưu."
        />
        <div v-else class="table-wrap">
          <table class="table">
            <thead>
              <tr>
                <th>Đối tượng</th>
                <th>Khoá</th>
                <th>Thay đổi</th>
                <th>Xung đột</th>
                <th>Xử lý</th>
                <th>Dữ liệu</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="change in selectedSession.changes.slice(0, 200)" :key="change.id">
                <td class="text-sm">{{ change.entityType }}</td>
                <td class="mono text-xs">{{ change.entityKey }}</td>
                <td><span class="badge" :class="changeTone(change.changeType)">{{ change.changeType }}</span></td>
                <td>
                  <span v-if="change.isConflict" class="badge badge-warning">Có</span>
                  <span v-else class="muted text-xs">—</span>
                </td>
                <td class="text-xs">{{ change.resolution ?? '—' }}</td>
                <td>
                  <details v-if="change.oldValueJson || change.newValueJson" class="details-block">
                    <summary>JSON</summary>
                    <pre class="json-box">{{ change.newValueJson ?? change.oldValueJson }}</pre>
                  </details>
                  <span v-else class="muted text-xs">—</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-if="selectedSession.changes.length > 200" class="hint" style="margin-top: 8px">
          Chỉ hiển thị 200 / {{ formatNumber(selectedSession.changes.length) }} thay đổi.
        </div>
      </div>
    </div>

    <div class="alert">
      <ListChecks :size="17" />
      <div>
        Sau khi đồng bộ, mở trang <strong>Kho hồ sơ</strong> để kiểm tra số lượng theo năm và loại.
        Nếu cần in, mở từng hồ sơ và dùng bản in A4 dọc.
      </div>
    </div>
  </div>
</template>
