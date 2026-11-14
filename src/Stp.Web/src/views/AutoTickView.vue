<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  AlertTriangle,
  Bell,
  Check,
  CheckCheck,
  Cloud,
  Download,
  ExternalLink,
  FileSpreadsheet,
  Link2,
  Monitor,
  Play,
  RefreshCw,
  Sparkles,
  StopCircle,
  Terminal,
  Tv,
  Zap,
} from '@lucide/vue'
import { request, download } from '../api/client'
import type {
  AutoTickInspectResponse,
  AutoTickInspectRequest,
  AutoTickStartRequest,
  AutoTickProgressEvent,
  AutoTickJobStatusResponse,
  AutoTickWatcherCheckRequest,
  AutoTickWatcherCheckResponse,
  AutoTickRowResult,
  AutoTickSaveToOneDriveResponse,
} from '../api/types'
import { useToast } from '../composables/useToast'
import { errorMessage } from '../lib/errors'

const toast = useToast()

// Default OneDrive URL from user
const ONEDRIVE_STORAGE_KEY = 'stp.autotick.onedrive_url'
const DEFAULT_URL =
  'https://onedrive.live.com/:x:/g/personal/52960fb7b88aa4ef/IQDder6_nmp6QI5JsHL1gk1oAV2fQl2Dv7Nt09mpsdj3lJ0?e=5ggKl7'

const oneDriveUrl = ref(localStorage.getItem(ONEDRIVE_STORAGE_KEY) || DEFAULT_URL)
watch(oneDriveUrl, (val) => {
  if (val && val.trim()) {
    localStorage.setItem(ONEDRIVE_STORAGE_KEY, val.trim())
  }
})
const isInspecting = ref(false)
const inspectData = ref<AutoTickInspectResponse | null>(null)
const inspectError = ref<string | null>(null)

// Filters
const selectedSheet = ref('1997')
const numberFrom = ref<number | ''>('')
const numberTo = ref<number | ''>('')
const selectedHeaderTitles = ref<string[]>([])
const onlyUnticked = ref(false)

// Job state
const activeJobId = ref<string | null>(null)
const isRunning = ref(false)
const progress = ref<AutoTickProgressEvent | null>(null)
const liveRow = ref<AutoTickRowResult | null>(null)
const recentRows = ref<AutoTickRowResult[]>([])
const terminalLogs = ref<string[]>([])
const isSavingOneDrive = ref(false)

// Studio View Tabs
const activeStudioTab = ref<'grid' | 'browser' | 'terminal'>('grid')

// Watcher state
const isWatcherEnabled = ref(false)
const watcherAlert = ref<AutoTickWatcherCheckResponse | null>(null)
let watcherTimer: number | undefined
let pollStatusTimer: number | undefined

// Metrics & Speed
const startTime = ref<number>(0)
const speed = ref<number>(0) // rows/sec
const etaSeconds = computed(() => {
  if (!progress.value || speed.value <= 0) return 0
  const remaining = progress.value.totalRows - progress.value.processedRows
  return Math.max(0, Math.round(remaining / speed.value))
})

const currentActiveCellCoordinate = computed(() => {
  if (!liveRow.value) return 'D1'
  if (liveRow.value.newlyTickedColumns.length > 0) {
    const colName = liveRow.value.newlyTickedColumns[0]
    const header = inspectData.value?.documentHeaders.find((h) => h.title === colName)
    if (header) {
      return `${header.columnLetter}${liveRow.value.rowIndex}`
    }
  }
  return `D${liveRow.value.rowIndex}`
})

function formatEta(sec: number): string {
  if (sec <= 0) return 'Hoàn thành'
  if (sec < 60) return `${sec}s`
  const mins = Math.floor(sec / 60)
  const remSec = sec % 60
  return `${mins}m ${remSec}s`
}

function appendLog(msg: string): void {
  const time = new Date().toLocaleTimeString('vi-VN')
  terminalLogs.value.unshift(`[${time}] ${msg}`)
  if (terminalLogs.value.length > 100) {
    terminalLogs.value.pop()
  }
}

// Quick paste from clipboard
async function pasteFromClipboard(): Promise<void> {
  try {
    if (navigator.clipboard?.readText) {
      const text = await navigator.clipboard.readText()
      if (text.trim().startsWith('http')) {
        oneDriveUrl.value = text.trim()
        toast.success('Đã dán liên kết', 'Đang nạp file...')
        void inspect()
      }
    }
  } catch {
    toast.info('Gợi ý', 'Nhấn Ctrl+V để dán liên kết vào ô.')
  }
}

function applyPresetRange(from: number, to: number): void {
  numberFrom.value = from
  numberTo.value = to
  toast.info('Đã chọn khoảng kiểm tra', `Từ số ${from} đến số ${to}`)
}

// Inspect OneDrive file
async function inspect(sheetOverride?: string): Promise<void> {
  if (!oneDriveUrl.value.trim()) return
  isInspecting.value = true
  inspectError.value = null

  try {
    const payload: AutoTickInspectRequest = {
      oneDriveUrl: oneDriveUrl.value.trim(),
      sheetName: sheetOverride || selectedSheet.value || undefined,
    }
    const data = await request<AutoTickInspectResponse>('/api/auto-tick/inspect', {
      method: 'POST',
      body: payload,
    })

    inspectData.value = data
    selectedSheet.value = data.activeSheet
    // Select all headers by default
    selectedHeaderTitles.value = data.documentHeaders.map((h) => h.title)

    toast.success('Quét thành công', `Tìm thấy ${data.totalRows} hồ sơ (${data.rowsWithParentheses} có ngoặc) trên sheet ${data.activeSheet}`)
  } catch (err) {
    inspectError.value = errorMessage(err)
    toast.error('Lỗi quét file OneDrive', inspectError.value)
  } finally {
    isInspecting.value = false
  }
}

function onSheetChange(sheetName: string): void {
  selectedSheet.value = sheetName
  void inspect(sheetName)
}

function toggleHeader(title: string): void {
  const idx = selectedHeaderTitles.value.indexOf(title)
  if (idx >= 0) {
    selectedHeaderTitles.value.splice(idx, 1)
  } else {
    selectedHeaderTitles.value.push(title)
  }
}

function selectAllHeaders(): void {
  if (!inspectData.value) return
  selectedHeaderTitles.value = inspectData.value.documentHeaders.map((h) => h.title)
}

function clearAllHeaders(): void {
  selectedHeaderTitles.value = []
}

// Start AutoTick Macro
async function startAutoTick(): Promise<void> {
  if (!oneDriveUrl.value.trim() || isRunning.value) return

  isRunning.value = true
  startTime.value = Date.now()
  progress.value = null
  recentRows.value = []
  terminalLogs.value = []
  appendLog('⚡ Khởi động Macro tự động đánh tick...')

  try {
    const payload: AutoTickStartRequest = {
      oneDriveUrl: oneDriveUrl.value.trim(),
      sheetName: selectedSheet.value,
      numberFrom: numberFrom.value !== '' ? Number(numberFrom.value) : undefined,
      numberTo: numberTo.value !== '' ? Number(numberTo.value) : undefined,
      selectedHeaders:
        selectedHeaderTitles.value.length < (inspectData.value?.documentHeaders.length ?? 0)
          ? selectedHeaderTitles.value
          : undefined,
      onlyUnticked: onlyUnticked.value,
    }

    const jobId = await request<string>('/api/auto-tick/start', {
      method: 'POST',
      body: payload,
    })

    activeJobId.value = jobId
    appendLog(`Job ID: ${jobId}. Đang tự động xử lý trên sheet ${selectedSheet.value}...`)
    toast.info('Tiến trình bắt đầu', 'Hệ thống đang tự động đánh tick...')

    startPollingJobStatus(jobId)
  } catch (err) {
    isRunning.value = false
    const msg = errorMessage(err)
    appendLog(`❌ Lỗi khởi chạy: ${msg}`)
    toast.error('Không thể bắt đầu', msg)
  }
}

// Polling status
function startPollingJobStatus(jobId: string): void {
  stopPollingJobStatus()
  pollStatusTimer = window.setInterval(async () => {
    try {
      const status = await request<AutoTickJobStatusResponse>(`/api/auto-tick/${jobId}/status`)
      if (status) {
        progress.value = {
          jobId: status.jobId,
          sheetName: status.sheetName,
          processedRows: status.processedRows,
          totalRows: status.totalRows,
          newlyTickedRowsCount: status.newlyTickedRowsCount,
          newlyTickedCellsCount: status.newlyTickedCellsCount,
          skippedRowsCount: status.skippedRowsCount,
          percent: status.percent,
          isCompleted: status.status === 'Completed',
          isCancelled: status.status === 'Cancelled',
          errorMessage: status.errorMessage,
        }

        if (status.recentRowResults.length > 0) {
          liveRow.value = status.recentRowResults[status.recentRowResults.length - 1]
          recentRows.value = status.recentRowResults.slice(-25).reverse()
        }

        const elapsed = (Date.now() - startTime.value) / 1000
        if (elapsed > 0) {
          speed.value = Math.round((status.processedRows / elapsed) * 10) / 10
        }

        if (status.status === 'Completed' || status.status === 'Failed' || status.status === 'Cancelled') {
          stopPollingJobStatus()
          isRunning.value = false

          if (status.status === 'Completed') {
            appendLog(`🎉 HOÀN THÀNH: Đã tick ${status.newlyTickedCellsCount} ô trên ${status.newlyTickedRowsCount} hồ sơ!`)
            toast.success(
              'Macro hoàn thành!',
              `Đã tick ${status.newlyTickedCellsCount} ô trên ${status.newlyTickedRowsCount} hồ sơ. Bấm "Tải file" hoặc "Đồng bộ OneDrive máy" để cập nhật ngay.`,
            )
            void inspect()
          } else if (status.status === 'Cancelled') {
            appendLog('⏹ Đã dừng Macro theo yêu cầu.')
            toast.warning('Đã huỷ', 'Tiến trình đã dừng lại.')
          } else {
            appendLog(`❌ Thất bại: ${status.errorMessage}`)
            toast.error('Thất bại', status.errorMessage ?? 'Có lỗi xảy ra.')
          }
        }
      }
    } catch {
      // Ignore poll transient error
    }
  }, 350)
}

function stopPollingJobStatus(): void {
  if (pollStatusTimer) {
    window.clearInterval(pollStatusTimer)
    pollStatusTimer = undefined
  }
}

async function cancelAutoTick(): Promise<void> {
  if (!activeJobId.value) return
  try {
    await request(`/api/auto-tick/${activeJobId.value}/cancel`, { method: 'POST' })
    appendLog('⏹ Yêu cầu dừng tiến trình...')
  } catch (err) {
    toast.error('Lỗi dừng tiến trình', errorMessage(err))
  }
}

function downloadResultFile(): void {
  if (!activeJobId.value) return
  download(`/api/auto-tick/${activeJobId.value}/download`)
}

async function saveToOneDrive(): Promise<void> {
  if (!activeJobId.value) return
  isSavingOneDrive.value = true
  try {
    const res = await request<AutoTickSaveToOneDriveResponse>(`/api/auto-tick/${activeJobId.value}/save-to-onedrive`, {
      method: 'POST',
    })
    if (res.success) {
      toast.success('Đồng bộ OneDrive thành công!', res.message)
      appendLog(`☁️ ${res.message}`)
    } else {
      toast.warning('Chưa đồng bộ được', res.message)
      appendLog(`⚠️ ${res.message}`)
    }
  } catch (err) {
    toast.error('Lỗi lưu vào OneDrive', errorMessage(err))
  } finally {
    isSavingOneDrive.value = false
  }
}

function toggleWatcher(): void {
  isWatcherEnabled.value = !isWatcherEnabled.value
  if (isWatcherEnabled.value) {
    toast.success('Bật Giám sát thông minh', 'Hệ thống sẽ tự động quét file OneDrive khi có dữ liệu mới.')
    startWatcher()
  } else {
    stopWatcher()
    watcherAlert.value = null
    toast.info('Đã tắt Giám sát', 'Đã ngừng theo dõi thay đổi.')
  }
}

function startWatcher(): void {
  stopWatcher()
  watcherTimer = window.setInterval(async () => {
    if (!oneDriveUrl.value.trim() || isRunning.value) return
    try {
      const payload: AutoTickWatcherCheckRequest = {
        oneDriveUrl: oneDriveUrl.value.trim(),
        sheetName: selectedSheet.value,
        lastETag: inspectData.value?.eTag,
        knownRowCount: inspectData.value?.totalRows,
      }
      const res = await request<AutoTickWatcherCheckResponse>('/api/auto-tick/watcher/check', {
        method: 'POST',
        body: payload,
      })

      if (res.hasChanges && res.newUntickedRowsCount > 0) {
        watcherAlert.value = res
      }
    } catch {
      // Silent check error
    }
  }, 20_000)
}

function stopWatcher(): void {
  if (watcherTimer) {
    window.clearInterval(watcherTimer)
    watcherTimer = undefined
  }
}

function acceptWatcherPrompt(): void {
  watcherAlert.value = null
  toast.success('Đang thực thi', 'Tự động chạy đánh tick cho các dòng mới theo cài đặt...')
  void startAutoTick()
}

function dismissWatcherPrompt(): void {
  watcherAlert.value = null
}

function isHeaderMatchingRow(headerTitle: string, row: AutoTickRowResult): boolean {
  return (
    row.newlyTickedColumns.includes(headerTitle) ||
    row.alreadyTickedColumns.includes(headerTitle)
  )
}

onMounted(() => {
  void inspect()
})

onBeforeUnmount(() => {
  stopPollingJobStatus()
  stopWatcher()
})
</script>

<template>
  <div class="autotick-page stack">
    <!-- Watcher Floating Action Alert -->
    <Transition name="fade">
      <div v-if="watcherAlert" class="watcher-floating-card">
        <div class="row" style="gap: 12px; align-items: flex-start">
          <div class="watcher-icon-box">
            <Bell :size="20" class="pulse-bell" />
          </div>
          <div class="stack" style="gap: 4px; flex: 1">
            <h4 class="watcher-title">Phát hiện dữ liệu mới trên OneDrive!</h4>
            <p class="watcher-desc">
              Vừa có <strong>{{ watcherAlert.newUntickedRowsCount }} hồ sơ mới</strong> được dán vào sheet
              <strong>{{ watcherAlert.sheetName }}</strong>. Bạn có muốn tự động đánh tick ngay không?
            </p>
            <div class="row" style="gap: 8px; margin-top: 6px">
              <button type="button" class="btn btn-primary btn-sm" @click="acceptWatcherPrompt">
                <Zap :size="14" />
                Đồng ý thực thi ngay
              </button>
              <button type="button" class="btn btn-ghost btn-sm" @click="dismissWatcherPrompt">
                Để sau
              </button>
            </div>
          </div>
        </div>
      </div>
    </Transition>

    <!-- Top Hero Card: Input Link & Quick Connect -->
    <section class="card hero-panel">
      <div class="card-body stack" style="gap: 16px">
        <div class="row-between wrap" style="gap: 12px">
          <div class="stack" style="gap: 6px">
            <div class="row wrap" style="gap: 8px">
              <span class="badge badge-brand">
                <Sparkles :size="13" />
                Auto-Tick 2.0
              </span>
              <span class="badge badge-accent">
                <Tv :size="13" />
                Live Spreadsheet Studio
              </span>
            </div>
            <h1 class="page-title" style="margin: 0">Tự Động Đánh Dấu OneDrive</h1>
            <p class="page-subtitle" style="margin: 0">
              Đối soát danh mục giấy tờ trong ngoặc trích yếu <code class="tag-code">(...)</code> và chiếu trực tiếp màn hình bảng tính đánh tick tự động.
            </p>
          </div>

          <!-- Watcher Toggle Button -->
          <div>
            <button
              type="button"
              class="btn btn-sm"
              :class="isWatcherEnabled ? 'btn-success watcher-active' : 'btn-secondary'"
              @click="toggleWatcher"
            >
              <Bell :size="15" :class="{ 'pulse-bell': isWatcherEnabled }" />
              <span>{{ isWatcherEnabled ? 'Đang giám sát ngầm' : 'Bật giám sát tự động' }}</span>
            </button>
          </div>
        </div>

        <!-- URL Input Box with Search Bar style -->
        <div class="url-input-container">
          <Link2 :size="18" class="url-icon" />
          <input
            v-model="oneDriveUrl"
            type="text"
            class="input url-field"
            placeholder="Dán liên kết chia sẻ file Excel OneDrive tại đây..."
            @keydown.enter="inspect()"
          />
          <button type="button" class="btn btn-secondary btn-sm url-paste-btn" @click="pasteFromClipboard">
            Dán link
          </button>
          <button
            type="button"
            class="btn btn-primary url-submit-btn"
            :disabled="isInspecting || isRunning"
            @click="inspect()"
          >
            <RefreshCw :size="16" :class="{ 'spin-icon': isInspecting }" />
            <span>{{ isInspecting ? 'Đang quét...' : 'Quét & Kết nối' }}</span>
          </button>
        </div>

        <!-- Status Bar -->
        <div v-if="inspectData" class="row wrap status-bar" style="gap: 16px">
          <div class="row" style="gap: 6px; color: var(--brand)">
            <CheckCheck :size="15" />
            <span>File: <strong>{{ inspectData.fileName }}</strong></span>
          </div>
          <div class="row" style="gap: 6px">
            <span class="dot-online" />
            <span>Quyền ghi: <strong>{{ inspectData.userCanWrite ? 'Cho phép ghi đè' : 'Chỉ xem' }}</strong></span>
          </div>
          <div v-if="inspectData.eTag" style="color: var(--muted); font-size: 12px">
            ETag: <code>{{ inspectData.eTag.substring(0, 16) }}...</code>
          </div>
        </div>
      </div>
    </section>

    <!-- Loading State -->
    <section v-if="isInspecting" class="card empty-card stack" style="gap: 12px; align-items: center; padding: 40px 20px">
      <RefreshCw :size="32" class="spin-icon" style="color: var(--brand)" />
      <h3 style="font-size: 16px; margin: 0; color: var(--text)">Đang kết nối OneDrive và phân tích cấu trúc file...</h3>
      <p style="font-size: 13px; color: var(--muted); margin: 0; max-width: 500px; text-align: center">
        Hệ thống đang tải trực tiếp file Excel từ OneDrive, giải mã danh sách các sheet, đọc danh mục cột giấy tờ và chuẩn bị màn hình bảng tính trực tiếp.
      </p>
    </section>

    <!-- Error State -->
    <section v-else-if="inspectError" class="card error-panel">
      <div class="card-body stack" style="gap: 12px">
        <div class="row" style="gap: 12px; align-items: flex-start">
          <div class="error-icon-box">
            <AlertTriangle :size="24" />
          </div>
          <div class="stack" style="gap: 6px; flex: 1">
            <h3 class="error-title">Không thể kết nối hoặc quét file OneDrive</h3>
            <p class="error-desc">{{ inspectError }}</p>
            <div class="error-tips">
              <strong>Gợi ý kiểm tra:</strong>
              <ul>
                <li>Đảm bảo link OneDrive là liên kết chia sẻ hợp lệ (ví dụ: <code>https://onedrive.live.com/...</code>).</li>
                <li>Kiểm tra xem file Excel có bị khoá quyền chia sẻ hoặc đang mở độc quyền không.</li>
                <li>Kiểm tra kết nối mạng Internet của máy chủ.</li>
              </ul>
            </div>
            <div>
              <button type="button" class="btn btn-secondary btn-sm" @click="inspect()">
                <RefreshCw :size="14" />
                Thử kết nối lại
              </button>
            </div>
          </div>
        </div>
      </div>
    </section>

    <!-- Empty State -->
    <section v-else-if="!inspectData" class="card empty-card stack" style="gap: 12px; align-items: center; padding: 48px 20px">
      <div class="empty-icon-box">
        <Link2 :size="28" />
      </div>
      <h3 style="font-size: 16px; margin: 0; color: var(--text)">Sẵn sàng kết nối file OneDrive</h3>
      <p style="font-size: 13px; color: var(--muted); margin: 0; max-width: 520px; text-align: center">
        Dán liên kết OneDrive chia sẻ file Excel vào ô phía trên rồi bấm <strong style="color: var(--brand)">"Quét & Kết nối"</strong> để hiển thị màn hình Livestream bảng tính thật và bắt đầu tự động đánh tick.
      </p>
    </section>

    <!-- Main Content when Inspected -->
    <template v-if="inspectData">
      <!-- Sheet Tabs -->
      <div class="row wrap sheet-tabs-bar" style="gap: 8px">
        <span class="sheet-tabs-label">Sheet:</span>
        <button
          v-for="s in inspectData.sheets"
          :key="s"
          type="button"
          class="btn btn-sm"
          :class="selectedSheet === s ? 'btn-primary' : 'btn-ghost'"
          @click="onSheetChange(s)"
        >
          {{ s }}
        </button>
      </div>

      <!-- 4 Stat Cards -->
      <div class="stat-grid">
        <div class="card stat-card">
          <div class="card-body">
            <span class="stat-label">Tổng hồ sơ trên sheet</span>
            <div class="stat-value">{{ inspectData.totalRows.toLocaleString('vi-VN') }}</div>
          </div>
        </div>
        <div class="card stat-card">
          <div class="card-body">
            <span class="stat-label">Có danh mục trong ngoặc</span>
            <div class="stat-value" style="color: var(--info)">{{ inspectData.rowsWithParentheses.toLocaleString('vi-VN') }}</div>
          </div>
        </div>
        <div class="card stat-card">
          <div class="card-body">
            <span class="stat-label">Đã tick đủ (Bỏ qua)</span>
            <div class="stat-value" style="color: var(--muted)">{{ inspectData.rowsFullyTicked.toLocaleString('vi-VN') }}</div>
          </div>
        </div>
        <div class="card stat-card stat-card-highlight">
          <div class="card-body">
            <span class="stat-label" style="color: var(--brand)">Cần đánh tick (Trống/Thiếu)</span>
            <div class="stat-value" style="color: var(--brand)">
              {{ (inspectData.rowsUnticked + inspectData.rowsPartiallyTicked).toLocaleString('vi-VN') }}
            </div>
          </div>
        </div>
      </div>

      <!-- Filters & Column Selection Card -->
      <section class="card">
        <div class="card-body stack" style="gap: 16px">
          <div class="row-between wrap" style="gap: 12px">
            <div class="row wrap" style="gap: 12px; align-items: flex-end">
              <label class="field" style="margin: 0">
                <span class="label">Từ số hồ sơ</span>
                <input v-model="numberFrom" type="number" class="input input-sm" placeholder="VD: 2502" style="width: 110px" />
              </label>
              <label class="field" style="margin: 0">
                <span class="label">Đến số hồ sơ</span>
                <input v-model="numberTo" type="number" class="input input-sm" placeholder="VD: 2504" style="width: 110px" />
              </label>

              <!-- Quick Presets -->
              <div class="row" style="gap: 4px; margin-bottom: 2px">
                <button type="button" class="btn btn-ghost btn-sm preset-btn" @click="applyPresetRange(2502, 2504)">
                  Mẫu 2502 - 2504
                </button>
                <button type="button" class="btn btn-ghost btn-sm preset-btn" @click="applyPresetRange(1, 100)">
                  1 - 100
                </button>
                <button type="button" class="btn btn-ghost btn-sm preset-btn" @click="numberFrom = ''; numberTo = ''">
                  Tất cả
                </button>
              </div>

              <label class="checkbox-row" style="margin-bottom: 6px; margin-left: 6px">
                <input v-model="onlyUnticked" type="checkbox" />
                <span style="font-size: 13px">Chỉ quét các dòng chưa có tick nào</span>
              </label>
            </div>

            <!-- Start / Stop Button -->
            <div class="row" style="gap: 8px">
              <button
                v-if="!isRunning"
                type="button"
                class="btn btn-success"
                style="padding: 10px 24px; font-size: 14px; font-weight: 700; box-shadow: 0 4px 14px rgba(4, 120, 87, 0.3)"
                @click="startAutoTick()"
              >
                <Play :size="16" />
                Bắt đầu tự động đánh tick
              </button>
              <button
                v-else
                type="button"
                class="btn btn-danger"
                style="padding: 10px 24px; font-size: 14px; font-weight: 700"
                @click="cancelAutoTick()"
              >
                <StopCircle :size="16" />
                Dừng tiến trình
              </button>
            </div>
          </div>

          <!-- Document Headers Picker -->
          <div class="stack" style="gap: 8px">
            <div class="row-between wrap" style="gap: 8px">
              <span style="font-size: 12px; font-weight: 700; color: var(--text-soft)">
                DANH MỤC CỘT GIẤY TỜ ĐƯỢC CHỌN ĐỐI SOÁT ({{ selectedHeaderTitles.length }}/{{ inspectData.documentHeaders.length }} CỘT)
              </span>
              <div class="row" style="gap: 6px">
                <button type="button" class="btn btn-ghost btn-sm" @click="selectAllHeaders">Chọn tất cả</button>
                <button type="button" class="btn btn-ghost btn-sm" @click="clearAllHeaders">Bỏ chọn hết</button>
              </div>
            </div>

            <div class="header-chips-container">
              <button
                v-for="h in inspectData.documentHeaders"
                :key="h.columnIndex"
                type="button"
                class="header-chip"
                :class="{ 'header-chip-active': selectedHeaderTitles.includes(h.title) }"
                @click="toggleHeader(h.title)"
              >
                <Check v-if="selectedHeaderTitles.includes(h.title)" :size="13" />
                <span class="chip-letter">{{ h.columnLetter }}</span>
                <span class="chip-title">{{ h.title }}</span>
                <span class="chip-count">({{ h.existingTicksCount }})</span>
              </button>
            </div>
          </div>
        </div>
      </section>

      <!-- EXCEL LIVESTREAM STUDIO SCREEN (MÀN HÌNH CHIẾU BẢNG TÍNH THẬT) -->
      <section class="card studio-panel">
        <!-- Studio Top Toolbar -->
        <div class="studio-header row-between wrap">
          <div class="row wrap" style="gap: 12px; align-items: center">
            <div class="studio-live-badge" :class="{ 'studio-live-running': isRunning }">
              <span class="live-dot" />
              <span>{{ isRunning ? '🔴 LIVESTREAM ĐANG THAO TÁC TRỰC TIẾP' : '📺 MÀN HÌNH BẢNG TÍNH TRỰC TIẾP' }}</span>
            </div>

            <div class="studio-tabs row" style="gap: 4px">
              <button
                type="button"
                class="studio-tab-btn"
                :class="{ 'is-active': activeStudioTab === 'grid' }"
                @click="activeStudioTab = 'grid'"
              >
                <FileSpreadsheet :size="15" />
                <span>Bảng tính Excel Online</span>
              </button>
              <button
                type="button"
                class="studio-tab-btn"
                :class="{ 'is-active': activeStudioTab === 'browser' }"
                @click="activeStudioTab = 'browser'"
              >
                <Monitor :size="15" />
                <span>Cửa sổ OneDrive Cloud</span>
              </button>
              <button
                type="button"
                class="studio-tab-btn"
                :class="{ 'is-active': activeStudioTab === 'terminal' }"
                @click="activeStudioTab = 'terminal'"
              >
                <Terminal :size="15" />
                <span>Nhật ký Terminal ({{ terminalLogs.length }})</span>
              </button>
            </div>
          </div>

          <!-- Studio Actions & Save Options -->
          <div class="row wrap" style="gap: 8px; align-items: center">
            <span v-if="speed > 0" class="metric-badge">Tốc độ: <strong>{{ speed }} dòng/giây</strong></span>
            <span v-if="isRunning && etaSeconds > 0" class="metric-badge">Còn lại: <strong>{{ formatEta(etaSeconds) }}</strong></span>

            <!-- Sync & Download buttons -->
            <button
              v-if="progress?.isCompleted || activeJobId"
              type="button"
              class="btn btn-success btn-sm studio-action-btn"
              :disabled="isSavingOneDrive"
              title="Ghi đè trực tiếp vào thư mục OneDrive máy để tự đồng bộ lên cloud"
              @click="saveToOneDrive"
            >
              <Cloud :size="14" />
              <span>{{ isSavingOneDrive ? 'Đang lưu...' : 'Đồng bộ đè OneDrive máy' }}</span>
            </button>
            <button
              v-if="progress?.isCompleted || activeJobId"
              type="button"
              class="btn btn-primary btn-sm studio-action-btn"
              @click="downloadResultFile()"
            >
              <Download :size="14" />
              <span>Tải file Excel đã tick (.xlsx)</span>
            </button>
          </div>
        </div>

        <!-- Progress Indicator -->
        <div v-if="progress" class="studio-progress-bar">
          <div class="studio-progress-fill" :style="{ width: `${progress.percent}%` }" />
        </div>

        <!-- Excel Formula Bar (fx) -->
        <div class="formula-bar">
          <div class="name-box">{{ currentActiveCellCoordinate }}</div>
          <div class="fx-symbol">fx</div>
          <div class="fx-content">
            <span v-if="liveRow">
              <strong style="color: var(--brand)">=TRUE</strong>
              <span style="color: var(--muted); margin-left: 8px">
                // Khớp giấy tờ: {{ liveRow.newlyTickedColumns.join(', ') || 'Đã có sẵn / Bỏ qua' }}
              </span>
            </span>
            <span v-else style="color: var(--muted)">Chọn dòng để xem công thức và dữ liệu trích yếu</span>
          </div>
        </div>

        <!-- TAB 1: BẢNG TÍNH EXCEL TRỰC TIẾP (VIRTUAL EXCEL ONLINE GRID) -->
        <div v-if="activeStudioTab === 'grid'" class="excel-viewport">
          <div class="excel-table-scroll">
            <table class="excel-table">
              <thead>
                <!-- Top Letter Header Row (A, B, C, D... I, J, K...) -->
                <tr class="excel-letter-header">
                  <th class="col-row-num">#</th>
                  <th class="col-box">A</th>
                  <th class="col-num">B</th>
                  <th class="col-summary">D</th>
                  <th
                    v-for="h in inspectData.documentHeaders.filter(h => selectedHeaderTitles.includes(h.title))"
                    :key="h.columnIndex"
                    class="col-doc-header"
                  >
                    {{ h.columnLetter }}
                  </th>
                </tr>
                <!-- Title Header Row -->
                <tr class="excel-title-header">
                  <th class="col-row-num">Hàng</th>
                  <th class="col-box">Hộp số</th>
                  <th class="col-num">Số hồ sơ</th>
                  <th class="col-summary">Trích yếu nội dung văn bản</th>
                  <th
                    v-for="h in inspectData.documentHeaders.filter(h => selectedHeaderTitles.includes(h.title))"
                    :key="h.columnIndex"
                    class="col-doc-title"
                    :title="h.title"
                  >
                    <div class="col-title-wrap">{{ h.title }}</div>
                  </th>
                </tr>
              </thead>
              <tbody>
                <!-- Current Live Row (Highlight at Top if processing) -->
                <tr v-if="liveRow" class="excel-row is-current-live">
                  <td class="col-row-num live-cell-num">
                    <span class="live-row-indicator">▶</span>
                    {{ liveRow.rowIndex }}
                  </td>
                  <td class="col-box">-</td>
                  <td class="col-num">
                    <span class="bold-number">{{ liveRow.numberLabel || '-' }}</span>
                  </td>
                  <td class="col-summary">
                    <div class="summary-live-cell">
                      <span class="citizen-tag">{{ liveRow.citizenNames || 'Hồ sơ kết hôn' }}</span>
                      <span class="summary-text-display">{{ liveRow.summaryText }}</span>
                    </div>
                  </td>
                  <!-- Document Ticked Columns -->
                  <td
                    v-for="h in inspectData.documentHeaders.filter(h => selectedHeaderTitles.includes(h.title))"
                    :key="h.columnIndex"
                    class="col-doc-cell"
                    :class="{
                      'cell-newly-ticked': liveRow.newlyTickedColumns.includes(h.title),
                      'cell-already-ticked': liveRow.alreadyTickedColumns.includes(h.title),
                      'cell-dimmed': !isHeaderMatchingRow(h.title, liveRow),
                    }"
                  >
                    <span v-if="liveRow.newlyTickedColumns.includes(h.title)" class="tick-badge new-tick-pulse">
                      ✓ TRUE
                    </span>
                    <span v-else-if="liveRow.alreadyTickedColumns.includes(h.title)" class="tick-badge exist-tick">
                      ✓
                    </span>
                    <span v-else class="cell-blank">·</span>
                  </td>
                </tr>

                <!-- History / Recent Processed Rows -->
                <tr
                  v-for="r in recentRows.filter(r => !liveRow || r.rowIndex !== liveRow.rowIndex)"
                  :key="r.rowIndex"
                  class="excel-row"
                  :class="{ 'is-ticked-row': r.status === 'Ticked', 'is-skipped-row': r.status !== 'Ticked' }"
                >
                  <td class="col-row-num">{{ r.rowIndex }}</td>
                  <td class="col-box">-</td>
                  <td class="col-num">{{ r.numberLabel || '-' }}</td>
                  <td class="col-summary">
                    <div class="summary-row-preview">
                      <span v-if="r.citizenNames" class="citizen-sub-tag">{{ r.citizenNames }}</span>
                      <span class="truncate-summary">{{ r.summaryText }}</span>
                    </div>
                  </td>
                  <!-- Document Columns -->
                  <td
                    v-for="h in inspectData.documentHeaders.filter(h => selectedHeaderTitles.includes(h.title))"
                    :key="h.columnIndex"
                    class="col-doc-cell"
                  >
                    <span v-if="r.newlyTickedColumns.includes(h.title)" class="tick-badge new-tick">
                      ✓ TRUE
                    </span>
                    <span v-else-if="r.alreadyTickedColumns.includes(h.title)" class="tick-badge exist-tick">
                      ✓
                    </span>
                    <span v-else class="cell-blank">·</span>
                  </td>
                </tr>

                <!-- Sample Initial Rows if no execution yet -->
                <tr
                  v-if="!liveRow && recentRows.length === 0"
                  v-for="sRow in inspectData.sampleRows"
                  :key="sRow.rowIndex"
                  class="excel-row"
                >
                  <td class="col-row-num">{{ sRow.rowIndex }}</td>
                  <td class="col-box">-</td>
                  <td class="col-num">{{ sRow.numberLabel || '-' }}</td>
                  <td class="col-summary">
                    <span class="truncate-summary">{{ sRow.summaryText }}</span>
                  </td>
                  <td
                    v-for="h in inspectData.documentHeaders.filter(h => selectedHeaderTitles.includes(h.title))"
                    :key="h.columnIndex"
                    class="col-doc-cell"
                  >
                    <span class="cell-blank">·</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- TAB 2: CỬA SỔ ONEDRIVE CLOUD TRỰC TIẾP (LIVE CLOUD STUDIO) -->
        <div v-else-if="activeStudioTab === 'browser'" class="onedrive-browser-wrapper">
          <div class="browser-address-bar row-between">
            <div class="row" style="gap: 8px; align-items: center; flex: 1">
              <span class="browser-dot red" />
              <span class="browser-dot yellow" />
              <span class="browser-dot green" />
              <span class="browser-url-text">{{ oneDriveUrl }}</span>
            </div>
            <a :href="oneDriveUrl" target="_blank" rel="noopener noreferrer" class="btn btn-secondary btn-sm">
              <ExternalLink :size="14" />
              Mở trực tiếp trên Microsoft Excel Online
            </a>
          </div>

          <div class="cloud-studio-stage">
            <div v-if="isRunning" class="cloud-robot-active stack" style="gap: 16px; align-items: center; justify-content: center">
              <div class="row" style="gap: 10px; align-items: center">
                <span class="live-pulse-dot" />
                <span class="live-title">ROBOT PLAYWRIGHT ĐANG ĐIỀU KHIỂN &amp; ĐÁNH DẤU TRỰC TIẾP TRÊN ONEDRIVE</span>
              </div>
              
              <div class="live-cell-card stack" style="gap: 8px; align-items: center">
                <div class="live-cell-badge">
                  <span>TỌA ĐỘ Ô HIỆN TẠI</span>
                  <strong class="cell-coord-highlight">{{ currentActiveCellCoordinate }}</strong>
                </div>
                <div v-if="liveRow" class="live-citizen-row">
                  Hồ sơ số: <strong>{{ liveRow.numberLabel }}</strong> · {{ liveRow.citizenNames }}
                </div>
              </div>

              <div class="row wrap" style="gap: 20px; font-size: 13px; color: var(--muted)">
                <div>Đã tick: <strong style="color: var(--brand)">{{ progress?.newlyTickedCellsCount || 0 }}</strong> ô</div>
                <div>Tốc độ: <strong style="color: var(--brand)">{{ speed }}</strong> dòng/s</div>
                <div>Thời gian còn lại: <strong>{{ formatEta(etaSeconds) }}</strong></div>
              </div>

              <p class="live-note">
                Robot đang mở session Chromium bảo mật, truy cập Name Box và kích hoạt các ô checkbox tương ứng.
              </p>
            </div>

            <div v-else class="cloud-idle-stage stack" style="gap: 16px; align-items: center; justify-content: center">
              <div class="cloud-logo-circle">
                <Cloud :size="40" style="color: #0284c7" />
              </div>
              <h3 style="font-size: 18px; margin: 0; color: var(--text)">Đồng Bộ &amp; Đánh Dấu Trực Tuyến OneDrive</h3>
              <p style="font-size: 13px; color: var(--muted); max-width: 540px; text-align: center; margin: 0; line-height: 1.6">
                Chính sách bảo mật đám mây Microsoft 365 (CSP) chặn nhúng trực tiếp iframe ra ngoài để bảo vệ dữ liệu tài khoản. Robot Playwright kết nối trực tiếp qua phiên Chromium an toàn, tự động tick các checkbox trên server Microsoft mà không làm gián đoạn người dùng khác.
              </p>
              <div class="row" style="gap: 12px; margin-top: 8px">
                <a :href="oneDriveUrl" target="_blank" rel="noopener noreferrer" class="btn btn-secondary">
                  <ExternalLink :size="16" />
                  Mở file Excel trên OneDrive
                </a>
                <button type="button" class="btn btn-primary" @click="activeStudioTab = 'grid'">
                  <FileSpreadsheet :size="16" />
                  Xem bảng tính mô phỏng thời gian thực
                </button>
              </div>
            </div>
          </div>
        </div>

        <!-- TAB 3: TERMINAL LOG CHI TIẾT -->
        <div v-else-if="activeStudioTab === 'terminal'" class="terminal-wrapper">
          <div class="terminal-box">
            <div v-for="(log, idx) in terminalLogs" :key="idx" class="terminal-line">
              {{ log }}
            </div>
          </div>
        </div>
      </section>
    </template>
  </div>
</template>

<style scoped>
.autotick-page {
  gap: 20px;
  padding-bottom: 50px;
}

/* Hero Panel */
.hero-panel {
  background: linear-gradient(135deg, var(--surface) 0%, var(--surface-2) 100%);
  border: 1px solid var(--border);
  box-shadow: var(--shadow);
}

.tag-code {
  background: var(--brand-soft);
  color: var(--brand);
  padding: 2px 6px;
  border-radius: var(--radius-sm);
  font-family: var(--font-mono);
  font-size: 12px;
}

/* URL Input row */
.url-input-container {
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 6px 8px;
}

.url-icon {
  color: var(--muted);
  margin-left: 6px;
  flex-shrink: 0;
}

.url-field {
  flex: 1;
  border: none !important;
  background: transparent !important;
  box-shadow: none !important;
  padding: 8px 10px;
  font-size: 14px;
}

.url-field:focus {
  outline: none !important;
}

.url-paste-btn {
  flex-shrink: 0;
}

.url-submit-btn {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 6px;
  font-weight: 600;
}

.status-bar {
  font-size: 13px;
  padding-top: 6px;
  border-top: 1px solid var(--border);
}

.dot-online {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--success);
  display: inline-block;
  animation: pulse 2s infinite;
}

/* Watcher active glow */
.watcher-active {
  box-shadow: 0 0 12px rgba(45, 212, 191, 0.3);
}

.pulse-bell {
  animation: bell-bounce 1s infinite alternate;
}

@keyframes bell-bounce {
  0% { transform: rotate(-10deg); }
  100% { transform: rotate(10deg); }
}

@keyframes pulse {
  0% { opacity: 0.6; transform: scale(0.95); }
  50% { opacity: 1; transform: scale(1.05); }
  100% { opacity: 0.6; transform: scale(0.95); }
}

.spin-icon {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  100% { transform: rotate(360deg); }
}

/* Floating Action Card */
.watcher-floating-card {
  position: fixed;
  bottom: 24px;
  right: 24px;
  z-index: 999;
  max-width: 440px;
  background: var(--surface);
  border: 1px solid var(--brand);
  border-radius: var(--radius);
  padding: 16px;
  box-shadow: var(--shadow-lg);
}

.watcher-icon-box {
  width: 38px;
  height: 38px;
  border-radius: var(--radius-sm);
  background: var(--brand-soft);
  color: var(--brand);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.watcher-title {
  margin: 0;
  font-size: 14px;
  font-weight: 700;
  color: var(--text);
}

.watcher-desc {
  margin: 0;
  font-size: 13px;
  color: var(--text-soft);
  line-height: 1.4;
}

/* Error Card */
.error-panel {
  border: 1px solid var(--danger);
  background: var(--danger-soft);
}

.error-icon-box {
  width: 44px;
  height: 44px;
  border-radius: var(--radius-sm);
  background: rgba(185, 28, 28, 0.15);
  color: var(--danger);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.error-title {
  margin: 0;
  font-size: 15px;
  font-weight: 700;
  color: var(--danger);
}

.error-desc {
  margin: 0;
  font-size: 13px;
  color: var(--text);
  font-weight: 500;
}

.error-tips {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  padding: 10px 14px;
  font-size: 12px;
  color: var(--text-soft);
}

.error-tips ul {
  margin: 4px 0 0 16px;
  padding: 0;
}

/* Empty Card */
.empty-card {
  border: 2px dashed var(--border);
  background: var(--surface-2);
}

.empty-icon-box {
  width: 52px;
  height: 52px;
  border-radius: var(--radius);
  background: var(--brand-soft);
  color: var(--brand);
  display: flex;
  align-items: center;
  justify-content: center;
}

/* Sheet Tabs Bar */
.sheet-tabs-bar {
  align-items: center;
  padding-bottom: 6px;
  border-bottom: 1px solid var(--border);
}

.sheet-tabs-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--muted);
  margin-right: 4px;
}

/* Stat Grid */
.stat-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 14px;
}

.stat-card {
  background: var(--surface);
  border: 1px solid var(--border);
}

.stat-card-highlight {
  border-color: var(--brand);
  background: var(--brand-soft);
}

.stat-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--muted);
}

.stat-value {
  font-size: 24px;
  font-weight: 800;
  color: var(--text);
  margin-top: 4px;
}

.preset-btn {
  font-size: 11px;
  padding: 2px 8px;
  background: var(--surface-2);
  border: 1px solid var(--border);
}

/* Header chips picker */
.header-chips-container {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  max-height: 180px;
  overflow-y: auto;
  padding: 8px;
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
}

.header-chip {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 4px 8px;
  border-radius: 6px;
  font-size: 12px;
  border: 1px solid var(--border);
  background: var(--surface);
  color: var(--text-soft);
  cursor: pointer;
  transition: all 0.15s ease;
}

.header-chip:hover {
  border-color: var(--brand);
  color: var(--text);
}

.header-chip-active {
  background: var(--brand-soft);
  border-color: var(--brand);
  color: var(--brand);
  font-weight: 600;
}

.chip-letter {
  font-family: var(--font-mono);
  font-weight: 700;
  opacity: 0.7;
}

.chip-count {
  font-size: 10px;
  opacity: 0.6;
}

/* STUDIO PANEL (VIRTUAL EXCEL LIVESTREAM STUDIO) */
.studio-panel {
  border: 2px solid var(--brand);
  box-shadow: 0 10px 40px rgba(45, 212, 191, 0.15);
  overflow: hidden;
  border-radius: var(--radius);
}

.studio-header {
  background: var(--surface);
  border-bottom: 1px solid var(--border);
  padding: 12px 16px;
}

.studio-live-badge {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  border-radius: 20px;
  background: var(--surface-2);
  color: var(--muted);
  font-weight: 800;
  font-size: 12px;
  letter-spacing: 0.5px;
}

.studio-live-running {
  background: rgba(239, 68, 68, 0.15);
  color: #ef4444;
  border: 1px solid rgba(239, 68, 68, 0.3);
}

.live-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #ef4444;
  animation: pulse 1s infinite;
}

.studio-tabs {
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 3px;
}

.studio-tab-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 12px;
  border-radius: 6px;
  border: none;
  background: transparent;
  color: var(--muted);
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s ease;
}

.studio-tab-btn.is-active {
  background: var(--surface);
  color: var(--brand);
  box-shadow: var(--shadow-sm);
}

.studio-action-btn {
  font-weight: 700;
  box-shadow: var(--shadow-sm);
}

.metric-badge {
  font-size: 12px;
  padding: 4px 8px;
  background: var(--surface-2);
  border-radius: var(--radius-sm);
  color: var(--text-soft);
}

.studio-progress-bar {
  width: 100%;
  height: 6px;
  background: var(--surface-2);
  overflow: hidden;
}

.studio-progress-fill {
  height: 100%;
  background: linear-gradient(90deg, var(--brand) 0%, var(--accent) 100%);
  transition: width 0.3s ease;
}

/* Formula Bar (fx) */
.formula-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--surface-2);
  border-bottom: 1px solid var(--border);
  padding: 6px 16px;
  font-family: var(--font-mono);
  font-size: 13px;
}

.name-box {
  width: 70px;
  padding: 3px 6px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--surface);
  color: var(--text);
  font-weight: 700;
  text-align: center;
}

.fx-symbol {
  font-weight: 800;
  color: var(--muted);
  font-style: italic;
}

.fx-content {
  flex: 1;
  padding: 3px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--surface);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* EXCEL VIRTUAL GRID */
.excel-viewport {
  background: var(--surface);
  min-height: 380px;
  position: relative;
}

.excel-table-scroll {
  max-height: 480px;
  overflow: auto;
}

.excel-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.excel-letter-header th {
  background: var(--surface-3);
  color: var(--muted);
  font-family: var(--font-mono);
  font-weight: 700;
  font-size: 11px;
  text-align: center;
  border: 1px solid var(--border);
  padding: 4px 6px;
}

.excel-title-header th {
  background: var(--surface-2);
  color: var(--text-soft);
  font-weight: 700;
  font-size: 12px;
  text-align: left;
  border: 1px solid var(--border);
  padding: 8px 10px;
}

.col-row-num {
  width: 60px;
  text-align: center;
}

.col-box {
  width: 60px;
  text-align: center;
}

.col-num {
  width: 90px;
  text-align: center;
}

.col-summary {
  min-width: 380px;
}

.col-doc-header, .col-doc-title, .col-doc-cell {
  min-width: 130px;
  max-width: 160px;
  text-align: center;
}

.col-title-wrap {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.excel-row td {
  border: 1px solid var(--border);
  padding: 6px 10px;
  vertical-align: middle;
}

/* Live Row Styling */
.excel-row.is-current-live {
  background: rgba(45, 212, 191, 0.12) !important;
  box-shadow: inset 0 0 0 2px var(--brand);
}

.live-cell-num {
  font-weight: 800;
  color: var(--brand);
  background: var(--brand-soft);
}

.live-row-indicator {
  color: #ef4444;
  margin-right: 2px;
  font-size: 10px;
  animation: pulse 1s infinite;
}

.summary-live-cell {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.citizen-tag {
  display: inline-block;
  align-self: flex-start;
  font-size: 11px;
  font-weight: 700;
  background: var(--surface);
  border: 1px solid var(--brand);
  color: var(--brand);
  padding: 1px 6px;
  border-radius: 4px;
}

.summary-text-display {
  font-size: 13px;
  color: var(--text);
  line-height: 1.35;
}

.bold-number {
  font-weight: 800;
  color: var(--text);
}

.citizen-sub-tag {
  font-size: 11px;
  font-weight: 700;
  color: var(--text-soft);
  margin-right: 6px;
}

.truncate-summary {
  color: var(--text-soft);
  font-size: 12px;
}

/* Tick Badges in Cells */
.tick-badge {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 4px;
  font-family: var(--font-mono);
  font-weight: 800;
  font-size: 11px;
}

.new-tick {
  background: var(--brand-soft);
  color: var(--brand);
  border: 1px solid var(--brand);
}

.new-tick-pulse {
  background: var(--brand);
  color: #ffffff;
  border: 1px solid var(--brand-strong);
  box-shadow: 0 0 10px var(--brand);
  animation: pulse 1s infinite alternate;
}

.exist-tick {
  background: var(--surface-2);
  color: var(--muted);
  border: 1px solid var(--border);
}

.cell-blank {
  color: var(--border-strong);
  font-size: 14px;
}

.cell-dimmed {
  opacity: 0.4;
}

/* Tab 2: Embedded OneDrive Browser */
.onedrive-browser-wrapper {
  background: #111b2b;
  height: 520px;
  display: flex;
  flex-direction: column;
}

.browser-address-bar {
  background: #0a1220;
  padding: 8px 16px;
  border-bottom: 1px solid var(--border);
}

.browser-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  display: inline-block;
}
.browser-dot.red { background: #ef4444; }
.browser-dot.yellow { background: #f59e0b; }
.browser-dot.green { background: #10b981; }

.browser-url-text {
  font-family: var(--font-mono);
  font-size: 11px;
  color: var(--muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.cloud-studio-stage {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 32px 24px;
  background: radial-gradient(circle at 50% 30%, #152238 0%, #0c1424 100%);
}

.cloud-robot-active {
  animation: fadeIn 0.3s ease;
}

.live-pulse-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: #10b981;
  box-shadow: 0 0 10px #10b981;
  animation: pulseDot 1.4s infinite;
}

@keyframes pulseDot {
  0%, 100% { opacity: 1; transform: scale(1); }
  50% { opacity: 0.4; transform: scale(0.85); }
}

.live-title {
  font-size: 13px;
  font-weight: 700;
  letter-spacing: 0.5px;
  color: #34d399;
}

.live-cell-card {
  background: var(--surface);
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-lg);
  padding: 20px 32px;
  box-shadow: 0 8px 30px rgba(0, 0, 0, 0.4);
}

.live-cell-badge {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
}

.live-cell-badge span {
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 1px;
  color: var(--muted);
}

.cell-coord-highlight {
  font-family: var(--font-mono);
  font-size: 36px;
  font-weight: 800;
  color: #38bdf8;
  text-shadow: 0 0 20px rgba(56, 189, 248, 0.4);
}

.live-citizen-row {
  font-size: 14px;
  color: var(--text);
}

.live-citizen-row strong {
  color: var(--brand);
}

.live-note {
  font-size: 12px;
  color: var(--muted);
  margin: 0;
}

.cloud-logo-circle {
  width: 80px;
  height: 80px;
  border-radius: 50%;
  background: rgba(2, 132, 199, 0.12);
  border: 1px solid rgba(2, 132, 199, 0.3);
  display: flex;
  align-items: center;
  justify-content: center;
}

/* Tab 3: Terminal */
.terminal-wrapper {
  padding: 16px;
  background: #090d16;
}

.terminal-box {
  background: transparent;
  height: 380px;
  overflow-y: auto;
  font-family: var(--font-mono);
  font-size: 12px;
  color: #94a3b8;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.terminal-line {
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-all;
}
</style>
