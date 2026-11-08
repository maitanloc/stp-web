<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import {
  ArrowLeft,
  Copy,
  Cpu,
  Database,
  Download,
  Eye,
  EyeOff,
  ExternalLink,
  FileText,
  GitCompare,
  HardDrive,
  LayoutDashboard,
  ListChecks,
  Lock,
  LogOut,
  Play,
  RefreshCw,
  Save,
  ScanLine,
  Server,
  ShieldCheck,
  Terminal,
  TriangleExclamationPoint,
  User,
} from '@lucide/vue'
import { ApiError, request } from '../api/client'
import type {
  InspectionResponse,
  JobDetailResponse,
  JobSummaryResponse,
  MacroDiffResponse,
  MacroValidationResponse,
  MacroVersionDetailResponse,
  MacroVersionSummaryResponse,
  PagedResponse,
  SyncSessionDetailResponse,
  SyncSessionSummaryResponse,
  SystemInfoResponse,
} from '../api/types'
import AppSelect from '../components/AppSelect.vue'
import { useDevAuth } from '../composables/useDevAuth'
import { useToast } from '../composables/useToast'
import { errorMessage } from '../lib/errors'
import { formatDateTime, formatNumber, shortHash } from '../lib/format'
import EmptyState from '../components/EmptyState.vue'
import FilePicker from '../components/FilePicker.vue'
import InspectionResult from '../components/InspectionResult.vue'
import JobDetailPanel from '../components/JobDetailPanel.vue'
import StatusBadge from '../components/StatusBadge.vue'

const props = withDefaults(
  defineProps<{
    standalone?: boolean
  }>(),
  { standalone: false },
)

type TabKey = 'overview' | 'macros' | 'jobs' | 'sync' | 'inspect'

interface TabDef {
  key: TabKey
  label: string
  icon: typeof LayoutDashboard
}

const tabs: TabDef[] = [
  { key: 'overview', label: 'Hệ thống', icon: LayoutDashboard },
  { key: 'macros', label: 'Phiên bản macro', icon: Terminal },
  { key: 'jobs', label: 'Job & nhật ký', icon: ListChecks },
  { key: 'sync', label: 'Đồng bộ', icon: RefreshCw },
  { key: 'inspect', label: 'Kiểm tra file', icon: ScanLine },
]

const toast = useToast()
const { state, isAuthenticated, refreshStatus, login, setup, logout, messageOf } = useDevAuth()

const tab = ref<TabKey>('overview')
const initialized = ref(false)

// ---------------------------- Auth gate ----------------------------

const password = ref('')
const confirmPassword = ref('')
const showPassword = ref(false)
const authBusy = ref(false)
const authError = ref<string | null>(null)

const setupRequired = computed(() => state.status?.setupRequired ?? false)
const authSource = computed(() => {
  switch (state.status?.source) {
    case 'AppSettings':
      return 'appsettings.json'
    case 'LocalFile':
      return 'File cấu hình cục bộ'
    default:
      return 'Chưa cấu hình'
  }
})

async function submitAuth(): Promise<void> {
  authBusy.value = true
  authError.value = null
  try {
    if (setupRequired.value) {
      if (password.value !== confirmPassword.value) {
        authError.value = 'Hai lần nhập mật khẩu không khớp.'
        return
      }
      await setup(password.value, confirmPassword.value)
      toast.success('Đã thiết lập tài khoản developer')
    } else {
      await login(password.value)
      toast.success('Đăng nhập developer thành công')
    }
    password.value = ''
    confirmPassword.value = ''
  } catch (caught) {
    authError.value = messageOf(caught)
  } finally {
    authBusy.value = false
  }
}

function signOut(): void {
  logout()
  toast.info('Đã đăng xuất khỏi không gian developer')
  tab.value = 'overview'
}

// ---------------------------- System overview ----------------------------

const system = ref<SystemInfoResponse | null>(null)
const systemLoading = ref(false)
const systemError = ref<string | null>(null)

async function loadSystem(): Promise<void> {
  systemLoading.value = true
  systemError.value = null
  try {
    system.value = await request<SystemInfoResponse>('/api/dev/system')
  } catch (caught) {
    systemError.value = errorMessage(caught)
  } finally {
    systemLoading.value = false
  }
}

// ---------------------------- Macro versions ----------------------------

const macroVersions = ref<MacroVersionSummaryResponse[]>([])
const selectedMacroId = ref<string | null>(null)
const selectedMacro = ref<MacroVersionDetailResponse | null>(null)
const macroCode = ref('')
const macroOriginalCode = ref('')
const changeNote = ref('')
const macroListLoading = ref(false)
const macroDetailLoading = ref(false)
const macroSaving = ref(false)
const macroActivating = ref(false)
const macroValidating = ref(false)
const macroDiffLoading = ref(false)
const macroError = ref<string | null>(null)
const validation = ref<MacroValidationResponse | null>(null)
const macroDiff = ref<MacroDiffResponse | null>(null)

const macroDirty = computed(
  () => selectedMacro.value !== null && macroCode.value !== macroOriginalCode.value,
)

async function loadMacros(selectId?: string): Promise<void> {
  macroListLoading.value = true
  macroError.value = null
  try {
    macroVersions.value = await request<MacroVersionSummaryResponse[]>('/api/dev/macros')
    const target =
      selectId ??
      selectedMacroId.value ??
      macroVersions.value.find((version) => version.isActive)?.id ??
      macroVersions.value[0]?.id ??
      null

    if (target) {
      await selectMacro(target, true)
    } else {
      selectedMacroId.value = null
      selectedMacro.value = null
    }
  } catch (caught) {
    macroError.value = errorMessage(caught)
  } finally {
    macroListLoading.value = false
  }
}

async function selectMacro(id: string, force = false): Promise<void> {
  if (!force && macroDirty.value && !window.confirm('Bỏ các thay đổi chưa lưu của macro?')) {
    return
  }

  selectedMacroId.value = id
  macroDetailLoading.value = true
  macroError.value = null
  validation.value = null
  macroDiff.value = null

  try {
    const detail = await request<MacroVersionDetailResponse>(`/api/dev/macros/${id}`)
    selectedMacro.value = detail
    macroCode.value = detail.code
    macroOriginalCode.value = detail.code
    changeNote.value = ''
  } catch (caught) {
    macroError.value = errorMessage(caught)
  } finally {
    macroDetailLoading.value = false
  }
}

async function validateMacro(): Promise<void> {
  macroValidating.value = true
  macroError.value = null
  try {
    validation.value = await request<MacroValidationResponse>('/api/dev/macros/validate', {
      method: 'POST',
      body: { code: macroCode.value },
    })
    if (validation.value.isValid) {
      toast.success('Kiểm tra tĩnh thành công', 'Không phát hiện lỗi cấu trúc.')
    }
  } catch (caught) {
    macroError.value = errorMessage(caught)
  } finally {
    macroValidating.value = false
  }
}

async function saveMacro(): Promise<void> {
  if (!selectedMacro.value) return

  macroSaving.value = true
  macroError.value = null
  validation.value = null

  try {
    const saved = await request<MacroVersionDetailResponse>('/api/dev/macros', {
      method: 'POST',
      body: {
        code: macroCode.value,
        changeNote: changeNote.value.trim() || null,
        parentVersionId: selectedMacro.value.id,
        activate: true,
      },
    })
    toast.success(`Đã lưu phiên bản v${saved.versionNumber}`, 'Phiên bản mới đã được kích hoạt.')
    await loadMacros(saved.id)
  } catch (caught) {
    if (caught instanceof ApiError && caught.isValidation) {
      validation.value = {
        isValid: false,
        errors: caught.validationErrors,
        warnings: caught.validationWarnings,
      }
      toast.error('Macro chưa hợp lệ', caught.validationErrors[0])
    } else {
      macroError.value = errorMessage(caught)
      toast.error('Không lưu được macro', macroError.value)
    }
  } finally {
    macroSaving.value = false
  }
}

async function activateMacro(): Promise<void> {
  if (!selectedMacro.value) return
  macroActivating.value = true
  try {
    const active = await request<MacroVersionDetailResponse>(
      `/api/dev/macros/${selectedMacro.value.id}/activate`,
      { method: 'POST' },
    )
    toast.success(`Đã kích hoạt v${active.versionNumber}`)
    await loadMacros(active.id)
  } catch (caught) {
    toast.error('Không kích hoạt được', errorMessage(caught))
  } finally {
    macroActivating.value = false
  }
}

async function loadMacroDiff(): Promise<void> {
  if (!selectedMacro.value) return
  macroDiffLoading.value = true
  macroError.value = null
  try {
    macroDiff.value = await request<MacroDiffResponse>(
      `/api/dev/macros/${selectedMacro.value.id}/diff`,
    )
  } catch (caught) {
    macroError.value = errorMessage(caught)
  } finally {
    macroDiffLoading.value = false
  }
}

function downloadMacro(): void {
  if (!selectedMacro.value) return
  const blob = new Blob([macroCode.value], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `StpArchiveMacro-v${selectedMacro.value.versionNumber}.bas`
  anchor.click()
  URL.revokeObjectURL(url)
}

async function copyMacro(): Promise<void> {
  try {
    await navigator.clipboard.writeText(macroCode.value)
    toast.success('Đã sao chép mã macro')
  } catch {
    toast.error('Trình duyệt không cho phép sao chép')
  }
}

// ---------------------------- Jobs ----------------------------

const jobs = ref<JobSummaryResponse[]>([])
const jobsLoading = ref(false)
const jobStatusFilter = ref('')
const jobStatusOptions = [
  { value: '', label: 'Tất cả trạng thái' },
  { value: 'Queued', label: 'Đang chờ' },
  { value: 'Running', label: 'Đang xử lý' },
  { value: 'Completed', label: 'Hoàn tất' },
  { value: 'Failed', label: 'Thất bại' },
  { value: 'Cancelled', label: 'Đã huỷ' },
]
const selectedJobId = ref<string | null>(null)
const jobDetail = ref<JobDetailResponse | null>(null)
const jobDetailLoading = ref(false)

async function loadJobs(): Promise<void> {
  jobsLoading.value = true
  try {
    const query = jobStatusFilter.value ? `&status=${encodeURIComponent(jobStatusFilter.value)}` : ''
    const response = await request<PagedResponse<JobSummaryResponse>>(`/api/jobs?pageSize=50${query}`)
    jobs.value = response.items
  } catch (caught) {
    toast.error('Không tải được danh sách job', errorMessage(caught))
  } finally {
    jobsLoading.value = false
  }
}

async function openJob(id: string): Promise<void> {
  selectedJobId.value = id
  jobDetailLoading.value = true
  try {
    jobDetail.value = await request<JobDetailResponse>(`/api/jobs/${id}`)
  } catch (caught) {
    toast.error('Không tải được job', errorMessage(caught))
  } finally {
    jobDetailLoading.value = false
  }
}

async function cancelJob(): Promise<void> {
  if (!selectedJobId.value) return
  if (!window.confirm('Huỷ job này?')) return
  try {
    await request(`/api/jobs/${selectedJobId.value}/cancel`, { method: 'POST' })
    toast.warning('Đã huỷ job')
    await openJob(selectedJobId.value)
    await loadJobs()
  } catch (caught) {
    toast.error('Không huỷ được job', errorMessage(caught))
  }
}

// ---------------------------- Sync sessions ----------------------------

const syncSessions = ref<SyncSessionSummaryResponse[]>([])
const syncLoading = ref(false)
const selectedSync = ref<SyncSessionDetailResponse | null>(null)
const syncDetailLoading = ref(false)

async function loadSyncSessions(): Promise<void> {
  syncLoading.value = true
  try {
    syncSessions.value = await request<SyncSessionSummaryResponse[]>('/api/sync/sessions?take=50')
  } catch (caught) {
    toast.error('Không tải được phiên đồng bộ', errorMessage(caught))
  } finally {
    syncLoading.value = false
  }
}

async function openSyncSession(id: string): Promise<void> {
  syncDetailLoading.value = true
  try {
    selectedSync.value = await request<SyncSessionDetailResponse>(`/api/sync/sessions/${id}`)
  } catch (caught) {
    toast.error('Không tải được chi tiết phiên', errorMessage(caught))
  } finally {
    syncDetailLoading.value = false
  }
}

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

// ---------------------------- Inspect ----------------------------

const inspectFiles = ref<File[]>([])
const inspection = ref<InspectionResponse | null>(null)
const inspectLoading = ref(false)
const inspectError = ref<string | null>(null)

async function inspectFile(): Promise<void> {
  const file = inspectFiles.value[0]
  if (!file) return

  inspectLoading.value = true
  inspectError.value = null
  inspection.value = null

  try {
    const form = new FormData()
    form.append('file', file, file.name)
    inspection.value = await request<InspectionResponse>('/api/inspect', { method: 'POST', form })
  } catch (caught) {
    inspectError.value = errorMessage(caught)
  } finally {
    inspectLoading.value = false
  }
}

// ---------------------------- Lifecycle ----------------------------

async function initializeWorkspace(): Promise<void> {
  if (initialized.value) return
  initialized.value = true
  await Promise.allSettled([loadSystem(), loadMacros()])
}

watch(isAuthenticated, (authed) => {
  if (authed) {
    void initializeWorkspace()
  } else {
    initialized.value = false
    system.value = null
    macroVersions.value = []
  }
})

watch(tab, (value) => {
  if (!isAuthenticated.value) return
  if (value === 'overview' && !system.value) void loadSystem()
  if (value === 'jobs' && jobs.value.length === 0) void loadJobs()
  if (value === 'sync' && syncSessions.value.length === 0) void loadSyncSessions()
})

onMounted(async () => {
  if (!state.initialized) {
    await refreshStatus()
  }
  if (isAuthenticated.value) {
    await initializeWorkspace()
  }
})
</script>

<template>
  <div :class="standalone ? 'dev-shell' : 'dev-embedded'">
    <header v-if="standalone" class="dev-topbar">
      <div class="dev-brand">
        <span class="dev-brand-mark">
          <ShieldCheck :size="20" />
        </span>
        <div>
          <div class="dev-brand-title">STP Archive · Developer</div>
          <div class="dev-brand-sub">Không gian kỹ thuật</div>
        </div>
      </div>

      <div class="grow" />

      <RouterLink class="btn btn-secondary btn-sm" to="/">
        <ArrowLeft :size="14" />
        Về ứng dụng
      </RouterLink>
      <RouterLink class="btn btn-ghost btn-sm" to="/nang-cao">
        <ExternalLink :size="14" />
        Mở trong app
      </RouterLink>
      <button v-if="isAuthenticated" class="btn btn-secondary btn-sm" type="button" @click="signOut">
        <LogOut :size="14" />
        Đăng xuất
      </button>
    </header>

    <div :class="standalone ? 'dev-container' : ''">
      <div v-if="!state.initialized && state.checking" class="loading-block">
        <span class="spinner lg" />
        Đang kiểm tra cấu hình developer...
      </div>

      <div v-else-if="!isAuthenticated" class="dev-gate">
        <div class="dev-gate-card">
          <div class="dev-gate-icon">
            <ShieldCheck :size="26" />
          </div>

          <h1 class="dev-gate-title">
            {{ setupRequired ? 'Thiết lập tài khoản developer' : 'Đăng nhập developer' }}
          </h1>
          <p class="dev-gate-text">
            <template v-if="setupRequired">
              Đây là lần đầu mở không gian kỹ thuật trên máy này. Hãy tạo mật khẩu riêng để bảo vệ
              trình soạn macro và các API vận hành. Mật khẩu chỉ lưu cục bộ dưới dạng băm PBKDF2.
            </template>
            <template v-else>
              Trang này chỉ dành cho kỹ thuật viên. Cần mật khẩu developer để xem job, macro và
              nhật ký đồng bộ.
            </template>
          </p>

          <form class="dev-gate-form" @submit.prevent="submitAuth">
            <label class="field">
              <span class="label">Mật khẩu</span>
              <div class="password-field">
                <input
                  v-model="password"
                  class="input"
                  :type="showPassword ? 'text' : 'password'"
                  autocomplete="current-password"
                  placeholder="Nhập mật khẩu developer"
                />
                <button
                  class="password-toggle"
                  type="button"
                  :aria-label="showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'"
                  @click="showPassword = !showPassword"
                >
                  <EyeOff v-if="showPassword" :size="16" />
                  <Eye v-else :size="16" />
                </button>
              </div>
            </label>

            <label v-if="setupRequired" class="field">
              <span class="label">Nhập lại mật khẩu</span>
              <input
                v-model="confirmPassword"
                class="input"
                :type="showPassword ? 'text' : 'password'"
                autocomplete="new-password"
                placeholder="Nhập lại mật khẩu"
              />
            </label>

            <div v-if="authError" class="alert alert-danger">
              <TriangleExclamationPoint :size="17" />
              <div>{{ authError }}</div>
            </div>

            <button class="btn btn-accent btn-lg btn-block" type="submit" :disabled="authBusy">
              <span v-if="authBusy" class="spinner" />
              <Lock v-else :size="17" />
              {{ authBusy ? 'Đang xử lý...' : setupRequired ? 'Tạo mật khẩu & đăng nhập' : 'Đăng nhập' }}
            </button>

            <div class="hint" v-if="setupRequired">
              Tối thiểu 10 ký tự, có ít nhất một chữ cái và một chữ số.
            </div>
          </form>

          <div class="divider" />
          <div class="row-between text-xs muted">
            <span>Nguồn cấu hình: {{ authSource }}</span>
            <span v-if="!standalone">
              <RouterLink class="strong" to="/developer" target="_blank">Mở trang riêng</RouterLink>
            </span>
          </div>
        </div>
      </div>

      <div v-else class="stack" style="gap: 18px">
        <div class="row-between wrap">
          <div class="row wrap">
            <span class="dev-brand-mark" style="width: 42px; height: 42px">
              <ShieldCheck :size="21" />
            </span>
            <div>
              <h1 class="page-title" style="font-size: 23px">Không gian phát triển</h1>
              <p class="page-subtitle" style="margin-top: 2px">
                Quản lý macro, theo dõi job và đối chiếu dữ liệu đồng bộ.
              </p>
            </div>
          </div>
          <div class="row wrap">
            <span class="chip">
              <User :size="14" />
              {{ state.username ?? 'Developer' }}
            </span>
            <span v-if="state.expiresAt" class="chip">Hết hạn: {{ formatDateTime(state.expiresAt) }}</span>
            <RouterLink v-if="!standalone" class="btn btn-secondary btn-sm" to="/developer" target="_blank">
              <ExternalLink :size="14" />
              Trang riêng
            </RouterLink>
            <button class="btn btn-secondary btn-sm" type="button" @click="signOut">
              <LogOut :size="14" />
              Đăng xuất
            </button>
          </div>
        </div>

        <div class="tabs">
          <button
            v-for="item in tabs"
            :key="item.key"
            class="tab"
            :class="{ active: tab === item.key }"
            type="button"
            @click="tab = item.key"
          >
            <component :is="item.icon" :size="15" />
            {{ item.label }}
          </button>
        </div>

        <!-- ---------------------------- Tab: overview ---------------------------- -->
        <template v-if="tab === 'overview'">
          <div v-if="systemError" class="alert alert-danger">
            <TriangleExclamationPoint :size="17" />
            <div class="grow">{{ systemError }}</div>
            <button class="btn btn-secondary btn-sm" type="button" @click="loadSystem">Thử lại</button>
          </div>

          <div v-if="systemLoading && !system" class="loading-block"><span class="spinner lg" /> Đang đọc thông tin hệ thống...</div>

          <template v-else-if="system">
            <div class="metric-grid">
              <div class="metric">
                <div class="metric-label">Phiên bản ứng dụng</div>
                <div class="metric-value">{{ system.appVersion }}</div>
              </div>
              <div class="metric">
                <div class="metric-label">Môi trường</div>
                <div class="metric-value" style="font-size: 20px">{{ system.environment }}</div>
              </div>
              <div class="metric">
                <div class="metric-label">Thời gian máy chủ</div>
                <div class="metric-value" style="font-size: 16px">{{ formatDateTime(system.serverTime) }}</div>
              </div>
              <div class="metric">
                <div class="metric-label">Kết nối SQL Server</div>
                <div class="metric-value" style="font-size: 18px">
                  <span class="badge" :class="system.databaseConnected ? 'badge-success' : 'badge-danger'">
                    {{ system.databaseConnected ? 'Đã kết nối' : 'Mất kết nối' }}
                  </span>
                </div>
              </div>
            </div>

            <div class="grid grid-2">
              <div class="card">
                <div class="card-header">
                  <div>
                    <div class="card-title">Máy chủ xử lý Excel</div>
                    <div class="card-subtitle">WPS ưu tiên, Microsoft Excel dự phòng</div>
                  </div>
                  <Cpu :size="18" class="muted" />
                </div>
                <div class="card-body">
                  <div class="row-between wrap">
                    <span class="strong">{{ system.excelHost }}</span>
                    <span class="badge" :class="system.excelAvailable ? 'badge-success' : 'badge-danger'">
                      {{ system.excelAvailable ? 'Sẵn sàng' : 'Không khả dụng' }}
                    </span>
                  </div>
                  <div class="mono text-xs muted" style="margin-top: 8px; word-break: break-all">
                    {{ system.excelPath ?? 'Không tìm thấy EXCEL.EXE / et.exe' }}
                  </div>
                  <div class="divider" />
                  <div class="text-xs muted">Thư mục lưu trữ</div>
                  <div class="mono text-xs" style="word-break: break-all">{{ system.storageRoot }}</div>
                </div>
              </div>

              <div class="card">
                <div class="card-header">
                  <div>
                    <div class="card-title">Dữ liệu trong hệ thống</div>
                    <div class="card-subtitle">Số liệu hiện tại của SQL Server</div>
                  </div>
                  <Database :size="18" class="muted" />
                </div>
                <div class="card-body">
                  <div class="metric-grid" style="grid-template-columns: repeat(2, 1fr)">
                    <div class="metric">
                      <div class="metric-label">Workbook</div>
                      <div class="metric-value">{{ formatNumber(system.workbookCount) }}</div>
                    </div>
                    <div class="metric">
                      <div class="metric-label">Hồ sơ</div>
                      <div class="metric-value">{{ formatNumber(system.dossierCount) }}</div>
                    </div>
                    <div class="metric">
                      <div class="metric-label">Job</div>
                      <div class="metric-value">{{ formatNumber(system.jobCount) }}</div>
                    </div>
                    <div class="metric">
                      <div class="metric-label">Phiên đồng bộ</div>
                      <div class="metric-value">{{ formatNumber(system.syncSessionCount) }}</div>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div class="card">
              <div class="card-header">
                <div>
                  <div class="card-title">Macro đang áp dụng</div>
                  <div class="card-subtitle">Phiên bản này được chèn vào mọi file chuyển đổi</div>
                </div>
                <button class="btn btn-secondary btn-sm" type="button" :disabled="systemLoading" @click="loadSystem">
                  <RefreshCw :size="14" />
                  Làm mới
                </button>
              </div>
              <div class="card-body">
                <div v-if="system.activeMacro" class="row-between wrap">
                  <div>
                    <div class="row">
                      <span class="macro-version">v{{ system.activeMacro.versionNumber }}</span>
                      <span class="badge badge-success">Active</span>
                      <span class="text-xs muted">{{ system.activeMacro.lineCount }} dòng</span>
                    </div>
                    <div class="text-sm muted" style="margin-top: 4px">
                      {{ system.activeMacro.changeNote || 'Không có ghi chú' }} ·
                      {{ formatDateTime(system.activeMacro.createdAt) }}
                    </div>
                  </div>
                  <button class="btn btn-secondary btn-sm" type="button" @click="tab = 'macros'">
                    <Terminal :size="14" />
                    Quản lý macro
                  </button>
                </div>
                <div v-else class="text-sm muted">Chưa có phiên bản macro nào trong DB.</div>
              </div>
            </div>

            <div class="grid grid-3">
              <div class="card">
                <div class="card-body">
                  <div class="row">
                    <span class="stat-icon"><Server :size="17" /></span>
                    <div>
                      <div class="text-sm strong">API nội bộ</div>
                      <div class="text-xs muted">localhost:5215 · ASP.NET Core</div>
                    </div>
                  </div>
                </div>
              </div>
              <div class="card">
                <div class="card-body">
                  <div class="row">
                    <span class="stat-icon"><HardDrive :size="17" /></span>
                    <div>
                      <div class="text-sm strong">Lưu trữ file</div>
                      <div class="text-xs muted">data/uploads · outputs · backups</div>
                    </div>
                  </div>
                </div>
              </div>
              <div class="card">
                <div class="card-body">
                  <div class="row">
                    <span class="stat-icon"><FileText :size="17" /></span>
                    <div>
                      <div class="text-sm strong">Nhật ký & đối chiếu</div>
                      <div class="text-xs muted">Job log và SyncChange trong SQL Server</div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </template>
        </template>

        <!-- ---------------------------- Tab: macros ---------------------------- -->
        <template v-else-if="tab === 'macros'">
          <div v-if="macroError" class="alert alert-danger">
            <TriangleExclamationPoint :size="17" />
            <div class="grow">{{ macroError }}</div>
          </div>

          <div class="macro-layout">
            <div class="card">
              <div class="card-header">
                <div>
                  <div class="card-title">Phiên bản</div>
                  <div class="card-subtitle">{{ macroVersions.length }} phiên bản trong DB</div>
                </div>
                <button class="btn btn-ghost btn-sm" type="button" :disabled="macroListLoading" @click="loadMacros()">
                  <RefreshCw :size="14" />
                </button>
              </div>
              <div class="card-body" style="padding-top: 10px">
                <div v-if="macroListLoading && macroVersions.length === 0" class="loading-block">
                  <span class="spinner" /> Đang tải...
                </div>
                <EmptyState
                  v-else-if="macroVersions.length === 0"
                  title="Chưa có macro"
                  description="Macro mặc định sẽ được tạo khi hệ thống khởi động."
                />
                <div v-else class="macro-list">
                  <button
                    v-for="version in macroVersions"
                    :key="version.id"
                    type="button"
                    class="macro-item"
                    :class="{ active: selectedMacroId === version.id }"
                    @click="selectMacro(version.id)"
                  >
                    <div class="macro-item-head">
                      <span class="macro-version">v{{ version.versionNumber }}</span>
                      <span v-if="version.isActive" class="badge badge-success">Active</span>
                    </div>
                    <div class="macro-note">{{ version.changeNote || 'Không có ghi chú' }}</div>
                    <div class="macro-meta">
                      <span>{{ formatDateTime(version.createdAt) }}</span>
                      <span>{{ version.lineCount }} dòng</span>
                      <span class="mono">{{ shortHash(version.codeHash, 8) }}</span>
                    </div>
                  </button>
                </div>
              </div>
            </div>

            <div class="stack">
              <div v-if="macroDetailLoading" class="card">
                <div class="loading-block"><span class="spinner lg" /> Đang tải mã macro...</div>
              </div>

              <template v-else-if="selectedMacro">
                <div class="card">
                  <div class="card-header">
                    <div>
                      <div class="card-title">
                        StpArchiveMacro v{{ selectedMacro.versionNumber }}
                        <span v-if="selectedMacro.isActive" class="badge badge-success" style="margin-left: 6px">Active</span>
                      </div>
                      <div class="card-subtitle">
                        {{ selectedMacro.lineCount }} dòng · {{ formatDateTime(selectedMacro.createdAt) }} ·
                        hash {{ shortHash(selectedMacro.codeHash) }}
                      </div>
                    </div>
                    <div class="row wrap">
                      <button class="btn btn-secondary btn-sm" type="button" @click="copyMacro">
                        <Copy :size="14" />
                        Sao chép
                      </button>
                      <button class="btn btn-secondary btn-sm" type="button" @click="downloadMacro">
                        <Download :size="14" />
                        Tải .bas
                      </button>
                      <button
                        v-if="!selectedMacro.isActive"
                        class="btn btn-primary btn-sm"
                        type="button"
                        :disabled="macroActivating"
                        @click="activateMacro"
                      >
                        <Play :size="14" />
                        Kích hoạt
                      </button>
                      <button
                        class="btn btn-secondary btn-sm"
                        type="button"
                        :disabled="macroDiffLoading"
                        @click="loadMacroDiff"
                      >
                        <GitCompare :size="14" />
                        So sánh
                      </button>
                    </div>
                  </div>
                  <div class="card-body">
                    <div v-if="!selectedMacro.isActive && macroDirty" class="alert alert-warning" style="margin-bottom: 12px">
                      <TriangleExclamationPoint :size="17" />
                      <div>Bạn đang sửa bản chưa kích hoạt. Lưu phiên bản mới để áp dụng cho lần chuyển đổi tiếp theo.</div>
                    </div>

                    <textarea
                      v-model="macroCode"
                      class="code-editor"
                      spellcheck="false"
                      aria-label="Mã macro VBA"
                    />

                    <div class="row-between wrap" style="margin-top: 12px">
                      <div class="row wrap">
                        <span v-if="macroDirty" class="badge badge-warning">Có thay đổi chưa lưu</span>
                        <span v-else class="badge badge-neutral">Không thay đổi</span>
                        <span class="text-xs muted">Tổng {{ macroCode.split('\n').length }} dòng</span>
                      </div>
                      <div class="row wrap">
                        <button
                          class="btn btn-secondary btn-sm"
                          type="button"
                          :disabled="macroValidating"
                          @click="validateMacro"
                        >
                          <span v-if="macroValidating" class="spinner" />
                          <ScanLine v-else :size="14" />
                          Kiểm tra tĩnh
                        </button>
                      </div>
                    </div>

                    <div v-if="validation" class="stack" style="gap: 8px; margin-top: 12px">
                      <div v-if="validation.isValid" class="alert alert-success">
                        <ShieldCheck :size="17" />
                        <div><strong>Mã hợp lệ.</strong> Không phát hiện lỗi cấu trúc trong kiểm tra tĩnh.</div>
                      </div>
                      <div v-if="validation.errors.length > 0" class="alert alert-danger">
                        <TriangleExclamationPoint :size="17" />
                        <div>
                          <strong>Lỗi</strong>
                          <ul style="margin-top: 4px">
                            <li v-for="error in validation.errors" :key="error">{{ error }}</li>
                          </ul>
                        </div>
                      </div>
                      <div v-if="validation.warnings.length > 0" class="alert alert-warning">
                        <TriangleExclamationPoint :size="17" />
                        <div>
                          <strong>Cảnh báo</strong>
                          <ul style="margin-top: 4px">
                            <li v-for="warning in validation.warnings" :key="warning">{{ warning }}</li>
                          </ul>
                        </div>
                      </div>
                    </div>

                    <div class="divider" />

                    <div class="row wrap" style="align-items: flex-end">
                      <label class="field grow" style="min-width: 220px">
                        <span class="label">Ghi chú phiên bản</span>
                        <input
                          v-model="changeNote"
                          class="input"
                          placeholder="Ví dụ: thêm nút in nhanh theo năm"
                        />
                      </label>
                      <button
                        class="btn btn-primary"
                        type="button"
                        :disabled="macroSaving || !macroDirty"
                        @click="saveMacro"
                      >
                        <span v-if="macroSaving" class="spinner" />
                        <Save v-else :size="16" />
                        {{ macroSaving ? 'Đang lưu...' : 'Lưu phiên bản mới' }}
                      </button>
                    </div>
                    <div class="hint" style="margin-top: 8px">
                      Lưu sẽ tạo phiên bản mới, kế thừa bản đang chọn và kích hoạt ngay. Kiểm tra tĩnh không thay thế
                      bước biên dịch trong Excel; hãy mở thử file .xlsm trên máy có Excel để chắc chắn.
                    </div>
                  </div>
                </div>

                <div v-if="macroDiff" class="card">
                  <div class="card-header">
                    <div>
                      <div class="card-title">So sánh phiên bản</div>
                      <div class="card-subtitle">
                        +{{ macroDiff.added }} dòng · −{{ macroDiff.removed }} dòng · {{ macroDiff.unchanged }} dòng giữ nguyên
                      </div>
                    </div>
                    <button class="btn btn-ghost btn-sm" type="button" @click="macroDiff = null">Đóng</button>
                  </div>
                  <div class="card-body" style="padding-top: 10px">
                    <div class="diff-view">
                      <div
                        v-for="(line, index) in macroDiff.lines"
                        :key="index"
                        class="diff-line"
                        :class="line.kind"
                      >
                        <span class="diff-num">{{ line.oldLineNumber ?? '' }}</span>
                        <span class="diff-num">{{ line.newLineNumber ?? '' }}</span>
                        <span class="diff-sign">{{ line.kind === 'added' ? '+' : line.kind === 'removed' ? '−' : ' ' }}</span>
                        <span class="grow">{{ line.text }}</span>
                      </div>
                    </div>
                  </div>
                </div>
              </template>

              <div v-else class="card">
                <EmptyState title="Chọn một phiên bản macro" description="Chọn phiên bản ở danh sách bên trái để xem và sửa mã." />
              </div>
            </div>
          </div>
        </template>

        <!-- ---------------------------- Tab: jobs ---------------------------- -->
        <template v-else-if="tab === 'jobs'">
          <div class="card">
            <div class="card-header">
              <div>
                <div class="card-title">Job gần đây</div>
                <div class="card-subtitle">Chuyển đổi, gộp file và nhật ký xử lý</div>
              </div>
              <div class="row">
                <div style="width: 180px">
                  <AppSelect
                    v-model="jobStatusFilter"
                    :options="jobStatusOptions"
                    :searchable="false"
                    aria-label="Lọc trạng thái job"
                    @change="loadJobs"
                  />
                </div>
                <button class="btn btn-secondary btn-sm" type="button" :disabled="jobsLoading" @click="loadJobs">
                  <RefreshCw :size="14" />
                  Làm mới
                </button>
              </div>
            </div>
            <div class="card-body" style="padding-top: 12px">
              <div v-if="jobsLoading" class="loading-block"><span class="spinner" /> Đang tải job...</div>
              <EmptyState v-else-if="jobs.length === 0" title="Chưa có job" description="Job sẽ xuất hiện sau khi chuyển đổi hoặc gộp file." />
              <div v-else class="table-wrap">
                <table class="table table-hover">
                  <thead>
                    <tr>
                      <th>Thời gian</th>
                      <th>Loại</th>
                      <th>Trạng thái</th>
                      <th class="num">File</th>
                      <th class="num">Thành công</th>
                      <th class="num">Lỗi</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr
                      v-for="job in jobs"
                      :key="job.id"
                      class="clickable"
                      :style="selectedJobId === job.id ? { background: 'var(--surface-3)' } : undefined"
                      @click="openJob(job.id)"
                    >
                      <td class="text-sm">{{ formatDateTime(job.createdAt) }}</td>
                      <td><StatusBadge kind="jobType" :value="job.type" /></td>
                      <td><StatusBadge :value="job.status" /></td>
                      <td class="num">{{ job.totalItems }}</td>
                      <td class="num">{{ job.succeededItems }}</td>
                      <td class="num">{{ job.failedItems }}</td>
                      <td class="text-xs muted mono">{{ job.id.slice(0, 8) }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>

          <JobDetailPanel
            v-if="selectedJobId"
            :detail="jobDetail"
            :loading="jobDetailLoading"
            @cancel="cancelJob"
            @refresh="openJob(selectedJobId)"
          />
          <div v-else class="alert">
            <ListChecks :size="17" />
            <div>Chọn một job trong bảng để xem tiến độ, từng file và nhật ký chi tiết.</div>
          </div>
        </template>

        <!-- ---------------------------- Tab: sync ---------------------------- -->
        <template v-else-if="tab === 'sync'">
          <div class="card">
            <div class="card-header">
              <div>
                <div class="card-title">Phiên đồng bộ Excel → SQL Server</div>
                <div class="card-subtitle">Đối chiếu thay đổi và xung đột ghi sau thắng</div>
              </div>
              <button class="btn btn-secondary btn-sm" type="button" :disabled="syncLoading" @click="loadSyncSessions">
                <RefreshCw :size="14" />
                Làm mới
              </button>
            </div>
            <div class="card-body" style="padding-top: 12px">
              <div v-if="syncLoading" class="loading-block"><span class="spinner" /> Đang tải...</div>
              <EmptyState v-else-if="syncSessions.length === 0" title="Chưa có phiên đồng bộ" />
              <div v-else class="table-wrap">
                <table class="table table-hover">
                  <thead>
                    <tr>
                      <th>Thời gian</th>
                      <th>File nguồn</th>
                      <th>Trạng thái</th>
                      <th class="num">Thêm</th>
                      <th class="num">Sửa</th>
                      <th class="num">Xoá</th>
                      <th class="num">Xung đột</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr
                      v-for="session in syncSessions"
                      :key="session.id"
                      class="clickable"
                      @click="openSyncSession(session.id)"
                    >
                      <td class="text-sm">{{ formatDateTime(session.uploadedAt) }}</td>
                      <td class="text-sm">{{ session.sourceFileName }}</td>
                      <td><StatusBadge kind="sync" :value="session.status" /></td>
                      <td class="num">{{ session.addedCount }}</td>
                      <td class="num">{{ session.updatedCount }}</td>
                      <td class="num">{{ session.deletedCount }}</td>
                      <td class="num">{{ session.conflictCount }}</td>
                      <td class="text-xs muted mono">{{ session.id.slice(0, 8) }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>

          <div v-if="syncDetailLoading" class="loading-block"><span class="spinner lg" /> Đang tải chi tiết...</div>

          <div v-else-if="selectedSync" class="card">
            <div class="card-header">
              <div>
                <div class="card-title">Chi tiết phiên</div>
                <div class="card-subtitle">
                  {{ selectedSync.summary.sourceFileName }} · {{ formatDateTime(selectedSync.summary.uploadedAt) }}
                </div>
              </div>
              <StatusBadge kind="sync" :value="selectedSync.summary.status" />
            </div>
            <div class="card-body" style="padding-top: 12px">
              <EmptyState
                v-if="selectedSync.changes.length === 0"
                title="Không có thay đổi"
                description="Dữ liệu trong file trùng với DB."
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
                      <th>JSON</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="change in selectedSync.changes.slice(0, 300)" :key="change.id">
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
                          <summary>Xem</summary>
                          <pre class="json-box">{{ change.newValueJson ?? change.oldValueJson }}</pre>
                        </details>
                        <span v-else class="muted text-xs">—</span>
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </template>

        <!-- ---------------------------- Tab: inspect ---------------------------- -->
        <template v-else-if="tab === 'inspect'">
          <div class="card">
            <div class="card-header">
              <div>
                <div class="card-title">Kiểm tra workbook</div>
                <div class="card-subtitle">Đọc cấu trúc, đếm hồ sơ và xác định loại file</div>
              </div>
              <ScanLine :size="18" class="muted" />
            </div>
            <div class="card-body">
              <FilePicker v-model="inspectFiles" :multiple="false" hint="Chọn một file .xlsx/.xlsm để kiểm tra" />
              <button
                class="btn btn-primary"
                type="button"
                style="margin-top: 12px"
                :disabled="inspectFiles.length === 0 || inspectLoading"
                @click="inspectFile"
              >
                <span v-if="inspectLoading" class="spinner" />
                <ScanLine v-else :size="16" />
                {{ inspectLoading ? 'Đang đọc...' : 'Kiểm tra file' }}
              </button>

              <div v-if="inspectError" class="alert alert-danger" style="margin-top: 12px">
                <TriangleExclamationPoint :size="17" />
                <div>{{ inspectError }}</div>
              </div>

              <div v-if="inspection" style="margin-top: 16px">
                <InspectionResult :inspection="inspection" />
              </div>
            </div>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>
