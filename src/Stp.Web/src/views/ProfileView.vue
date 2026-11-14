<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import {
  Activity,
  AlertCircle,
  Calendar,
  Camera,
  CaseUpper,
  CheckCircle2,
  Clock,
  Edit3,
  FileSpreadsheet,
  KeyRound,
  LogIn,
  LogOut,
  MessageSquare,
  Moon,
  RefreshCw,
  Save,
  Shield,
  Sun,
  Volume2,
  VolumeX,
} from '@lucide/vue'
import { getUserName, request, setUserName } from '../api/client'
import type { ActivityRecapResponse, ProfileStatsResponse } from '../api/types'
import { useToast } from '../composables/useToast'
import { useTheme } from '../composables/useTheme'
import {
  logoutTeamIdentity,
  refreshTeamIdentity,
  teamIdentity,
} from '../composables/useTeamIdentity'
import { avatarMap, uploadAvatar } from '../composables/useAvatarUpload'
import { relativeTime, renderAvatar } from '../lib/team'
import { pingPresence, teamOnline } from '../composables/useTeamPresence'
import LoginModal from '../components/LoginModal.vue'
import UserNameModal from '../components/UserNameModal.vue'

const router = useRouter()
const toast = useToast()
const { theme, toggleTheme } = useTheme()

// State
const stats = ref<ProfileStatsResponse | null>(null)
const recaps = ref<ActivityRecapResponse[]>([])
const loadingStats = ref(false)
const loadingRecaps = ref(false)

// Modals
const loginModalOpen = ref(false)
const nameModalOpen = ref(false)

// Avatar upload
const avatarFileInput = ref<HTMLInputElement | null>(null)
const avatarUploading = ref(false)

// Edit display name
const isEditingName = ref(false)
const newDisplayName = ref('')
const savingName = ref(false)

// Change password
const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const changingPassword = ref(false)

// Preferences
const largeText = ref(false)
const soundEnabled = ref(true)

// Current effective user name
const currentUserName = computed(() => teamIdentity.value?.displayName ?? getUserName())
const isOnline = computed(() => {
  const name = currentUserName.value
  return !!name && teamOnline.value.some((item) => item.actor === name)
})

function triggerAvatarSelect(): void {
  avatarFileInput.value?.click()
}

async function onAvatarChange(event: Event): Promise<void> {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) return

  if (!file.type.startsWith('image/')) {
    toast.error('Vui lòng chọn tệp hình ảnh')
    return
  }

  const actorName = currentUserName.value?.trim()
  if (!actorName) {
    nameModalOpen.value = true
    toast.info('Vui lòng đặt tên trước khi đổi ảnh đại diện')
    target.value = ''
    return
  }

  avatarUploading.value = true
  try {
    const result = await uploadAvatar(file, actorName)
    avatarMap.value = {
      ...avatarMap.value,
      [actorName]: `/api/team/avatar/${result.userId}?v=${result.avatarVersion}`,
    }
    if (stats.value) {
      stats.value.avatarVersion = result.avatarVersion
    }
    toast.success('Đã cập nhật ảnh đại diện mới thành công!')
  } catch (err: unknown) {
    const msg = err instanceof Error ? err.message : 'Không tải ảnh đại diện lên được'
    toast.error(msg)
  } finally {
    avatarUploading.value = false
    target.value = ''
  }
}

async function loadProfileData(): Promise<void> {
  if (!teamIdentity.value) {
    stats.value = null
    recaps.value = []
    return
  }

  loadingStats.value = true
  loadingRecaps.value = true

  try {
    const data = await request<ProfileStatsResponse>('/api/auth/profile-stats')
    stats.value = data
  } catch {
    // Không có stats hoặc lỗi kết nối
  } finally {
    loadingStats.value = false
  }

  try {
    const recapData = await request<{ items: ActivityRecapResponse[] }>('/api/auth/recap?take=12')
    recaps.value = recapData.items || []
  } catch {
    // Bỏ qua
  } finally {
    loadingRecaps.value = false
  }
}

function startEditName(): void {
  newDisplayName.value = currentUserName.value || ''
  isEditingName.value = true
}

function cancelEditName(): void {
  isEditingName.value = false
  newDisplayName.value = ''
}

async function saveDisplayName(): Promise<void> {
  const name = newDisplayName.value.trim()
  if (!name) {
    toast.error('Tên hiển thị không được để trống')
    return
  }

  if (teamIdentity.value) {
    savingName.value = true
    try {
      await request('/api/auth/profile', {
        method: 'POST',
        body: { displayName: name },
      })
      await refreshTeamIdentity()
      if (stats.value) {
        stats.value.displayName = name
      }
      isEditingName.value = false
      void pingPresence()
      toast.success(`Đã cập nhật tên hiển thị thành "${name}"`)
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Không thể cập nhật tên hiển thị'
      toast.error(msg)
    } finally {
      savingName.value = false
    }
  } else {
    // Khách tự do (unauthenticated)
    setUserName(name)
    isEditingName.value = false
    void pingPresence()
    toast.success(`Đã cập nhật tên hiển thị thành "${name}"`)
  }
}

async function submitChangePassword(): Promise<void> {
  if (!currentPassword.value || !newPassword.value) {
    toast.error('Vui lòng nhập đầy đủ mật khẩu hiện tại và mật khẩu mới')
    return
  }
  if (newPassword.value !== confirmPassword.value) {
    toast.error('Mật khẩu mới và mật khẩu xác nhận không khớp')
    return
  }
  if (newPassword.value.length < 6) {
    toast.error('Mật khẩu mới phải có ít nhất 6 ký tự')
    return
  }

  changingPassword.value = true
  try {
    await request('/api/auth/change-password', {
      method: 'POST',
      body: {
        currentPassword: currentPassword.value,
        newPassword: newPassword.value,
      },
    })
    toast.success('Đổi mật khẩu thành công!')
    currentPassword.value = ''
    newPassword.value = ''
    confirmPassword.value = ''
  } catch (err: unknown) {
    const msg = err instanceof Error ? err.message : 'Không thể đổi mật khẩu'
    toast.error(msg)
  } finally {
    changingPassword.value = false
  }
}

function toggleLargeText(): void {
  largeText.value = !largeText.value
  document.documentElement.classList.toggle('large-text', largeText.value)
  try {
    localStorage.setItem('stp.largeText', largeText.value ? '1' : '0')
  } catch {}
}

function toggleSound(): void {
  soundEnabled.value = !soundEnabled.value
  try {
    localStorage.setItem('stp.chatSound', soundEnabled.value ? '1' : '0')
  } catch {}
}

function testChime(): void {
  try {
    const AudioContextClass = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext
    if (!AudioContextClass) return
    const ctx = new AudioContextClass()
    const now = ctx.currentTime
    const osc = ctx.createOscillator()
    const gain = ctx.createGain()
    osc.type = 'sine'
    osc.frequency.setValueAtTime(587.33, now) // D5
    osc.frequency.exponentialRampToValueAtTime(880, now + 0.12) // A5
    gain.gain.setValueAtTime(0.12, now)
    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.3)
    osc.connect(gain)
    gain.connect(ctx.destination)
    osc.start(now)
    osc.stop(now + 0.32)
    toast.info('Âm thanh thông báo tin nhắn đang hoạt động tốt!')
  } catch {
    toast.error('Trình duyệt không hỗ trợ phát âm thanh Web Audio')
  }
}

async function doLogout(): Promise<void> {
  await logoutTeamIdentity()
  stats.value = null
  recaps.value = []
  toast.success('Đã đăng xuất tài khoản')
}

function onLogged(): void {
  loginModalOpen.value = false
  void loadProfileData()
  void pingPresence()
  toast.success('Đăng nhập thành công!')
}

function onNameSaved(name: string): void {
  nameModalOpen.value = false
  setUserName(name)
  void pingPresence()
  toast.success(`Đã đặt tên "${name}"`)
}

onMounted(() => {
  void loadProfileData()
  try {
    largeText.value = localStorage.getItem('stp.largeText') === '1'
    soundEnabled.value = localStorage.getItem('stp.chatSound') !== '0'
  } catch {}
})
</script>

<template>
  <div class="profile-view">
    <!-- Hero Profile Header Card -->
    <header class="profile-hero card">
      <div class="hero-left">
        <div class="avatar-wrapper">
          <div
            class="avatar-display"
            :style="currentUserName ? renderAvatar(currentUserName).style : {}"
          >
            <img
              v-if="currentUserName && renderAvatar(currentUserName).imageUrl"
              :src="renderAvatar(currentUserName).imageUrl"
              alt="Avatar"
              class="avatar-img"
            />
            <span v-else class="avatar-initials">
              {{ currentUserName ? renderAvatar(currentUserName).initials : '?' }}
            </span>
          </div>
          <button
            type="button"
            class="avatar-camera-btn"
            :disabled="avatarUploading"
            :title="avatarUploading ? 'Đang cập nhật ảnh...' : 'Bấm để đổi ảnh đại diện'"
            @click="triggerAvatarSelect"
          >
            <Camera :size="16" />
          </button>
          <input
            ref="avatarFileInput"
            type="file"
            accept="image/*"
            style="display: none"
            @change="onAvatarChange"
          />
        </div>

        <div class="hero-identity">
          <div class="hero-name-row">
            <template v-if="!isEditingName">
              <h1 class="hero-name">{{ currentUserName || 'Chưa đặt tên' }}</h1>
              <button
                type="button"
                class="btn-icon-subtle"
                title="Sửa tên hiển thị"
                @click="startEditName"
              >
                <Edit3 :size="15" />
              </button>
            </template>
            <template v-else>
              <div class="name-edit-inline">
                <input
                  v-model="newDisplayName"
                  type="text"
                  class="input input-sm"
                  placeholder="Nhập tên hiển thị mới"
                  maxlength="80"
                  @keydown.enter.prevent="saveDisplayName"
                  @keydown.esc="cancelEditName"
                />
                <button
                  type="button"
                  class="btn btn-primary btn-sm"
                  :disabled="savingName"
                  @click="saveDisplayName"
                >
                  <Save :size="14" />
                  <span>Lưu</span>
                </button>
                <button
                  type="button"
                  class="btn btn-ghost btn-sm"
                  @click="cancelEditName"
                >
                  Hủy
                </button>
              </div>
            </template>
          </div>

          <div class="hero-meta">
            <span v-if="teamIdentity" class="badge-role" :class="teamIdentity.role">
              <Shield :size="12" />
              {{ teamIdentity.role === 'admin' ? 'Quản trị viên' : 'Thành viên chính thức' }}
            </span>
            <span v-else class="badge-guest">
              Thành viên trực tiếp (Khách)
            </span>

            <span v-if="teamIdentity" class="username-tag">
              @{{ teamIdentity.username }}
            </span>

            <span class="status-indicator" :class="{ online: isOnline }">
              <span class="status-dot"></span>
              {{ isOnline ? 'Đang trực tuyến' : 'Ngoại tuyến' }}
            </span>
          </div>

          <div v-if="stats" class="hero-timestamps">
            <span class="timestamp-item" title="Thời điểm khởi tạo tài khoản">
              <Calendar :size="13" />
              Tham gia: {{ new Date(stats.createdAt).toLocaleDateString('vi-VN') }}
            </span>
            <span v-if="stats.lastLoginAt" class="timestamp-item" title="Đăng nhập gần nhất">
              <Clock :size="13" />
              Đăng nhập gần nhất: {{ relativeTime(stats.lastLoginAt) }}
            </span>
          </div>
        </div>
      </div>

      <div class="hero-actions">
        <template v-if="teamIdentity">
          <button
            v-if="teamIdentity.role === 'admin'"
            type="button"
            class="btn btn-secondary btn-sm"
            @click="router.push('/quan-ly')"
          >
            <Shield :size="14" />
            <span>Quản lý thành viên</span>
          </button>
          <button
            type="button"
            class="btn btn-outline btn-sm text-danger"
            @click="doLogout"
          >
            <LogOut :size="14" />
            <span>Đăng xuất</span>
          </button>
        </template>
        <template v-else>
          <button
            type="button"
            class="btn btn-primary btn-sm"
            @click="loginModalOpen = true"
          >
            <LogIn :size="14" />
            <span>Đăng nhập tài khoản</span>
          </button>
          <button
            type="button"
            class="btn btn-secondary btn-sm"
            @click="nameModalOpen = true"
          >
            <Edit3 :size="14" />
            <span>Đổi tên khách</span>
          </button>
        </template>
      </div>
    </header>

    <!-- Quick Stats Grid -->
    <div class="stats-grid">
      <div class="stat-card card">
        <div class="stat-icon-wrap bg-blue">
          <Activity :size="20" />
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ stats?.totalActions ?? '—' }}</div>
          <div class="stat-label">Thao tác hệ thống</div>
          <div class="stat-sub">Ghi nhận qua nhật ký AuditLog</div>
        </div>
      </div>

      <div class="stat-card card">
        <div class="stat-icon-wrap bg-emerald">
          <MessageSquare :size="20" />
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ stats?.chatMessagesCount ?? '—' }}</div>
          <div class="stat-label">Tin nhắn nhóm</div>
          <div class="stat-sub">Đã trao đổi trong kênh chat</div>
        </div>
      </div>

      <div class="stat-card card">
        <div class="stat-icon-wrap bg-amber">
          <FileSpreadsheet :size="20" />
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ stats?.summarySessionsCount ?? '—' }}</div>
          <div class="stat-label">Phiên xử lý hồ sơ</div>
          <div class="stat-sub">Trích yếu và xuất bản Excel</div>
        </div>
      </div>

      <div class="stat-card card">
        <div class="stat-icon-wrap bg-purple">
          <CheckCircle2 :size="20" />
        </div>
        <div class="stat-content">
          <div class="stat-value">{{ isOnline ? 'Online' : 'Sẵn sàng' }}</div>
          <div class="stat-label">Trạng thái kết nối</div>
          <div class="stat-sub">Realtime SignalR & Presence</div>
        </div>
      </div>
    </div>

    <!-- Main Content Grid -->
    <div class="profile-grid">
      <!-- Left Column: Settings & Passwords -->
      <div class="profile-col">
        <!-- Section: Tuỳ biến giao diện & Thiết lập -->
        <section class="card config-card">
          <div class="card-head">
            <h2 class="card-title">Thiết lập cá nhân</h2>
            <span class="card-subtitle">Tuỳ biến trải nghiệm làm việc trên máy này</span>
          </div>

          <div class="setting-list">
            <!-- Theme Toggle -->
            <div class="setting-item">
              <div class="setting-info">
                <span class="setting-title">Giao diện màu</span>
                <span class="setting-desc">Chuyển đổi giữa chế độ ban ngày và ban đêm</span>
              </div>
              <button
                type="button"
                class="btn btn-secondary btn-sm"
                @click="toggleTheme"
              >
                <Sun v-if="theme === 'dark'" :size="15" />
                <Moon v-else :size="15" />
                <span>{{ theme === 'dark' ? 'Giao diện tối' : 'Giao diện sáng' }}</span>
              </button>
            </div>

            <!-- Large Text Toggle -->
            <div class="setting-item">
              <div class="setting-info">
                <span class="setting-title">Chế độ chữ lớn</span>
                <span class="setting-desc">Tăng kích cỡ phông chữ giúp đỡ mỏi mắt khi đọc sổ sách</span>
              </div>
              <button
                type="button"
                class="btn btn-secondary btn-sm"
                :class="{ 'is-active': largeText }"
                @click="toggleLargeText"
              >
                <CaseUpper :size="16" />
                <span>{{ largeText ? 'Đang bật' : 'Đang tắt' }}</span>
              </button>
            </div>

            <!-- Sound chime toggle -->
            <div class="setting-item">
              <div class="setting-info">
                <span class="setting-title">Chuông báo tin nhắn mới</span>
                <span class="setting-desc">Phát âm chuông nhẹ khi đồng nghiệp gửi tin nhắn đến</span>
              </div>
              <div class="setting-actions">
                <button
                  type="button"
                  class="btn btn-ghost btn-sm"
                  title="Bấm để phát thử âm chuông"
                  @click="testChime"
                >
                  <Volume2 :size="14" />
                  <span>Nghe thử</span>
                </button>
                <button
                  type="button"
                  class="btn btn-secondary btn-sm"
                  :class="{ 'is-active': soundEnabled }"
                  @click="toggleSound"
                >
                  <Volume2 v-if="soundEnabled" :size="15" />
                  <VolumeX v-else :size="15" />
                  <span>{{ soundEnabled ? 'Bật chuông' : 'Tắt tiếng' }}</span>
                </button>
              </div>
            </div>
          </div>
        </section>

        <!-- Section: Đổi mật khẩu (chỉ cho người đã đăng nhập) -->
        <section v-if="teamIdentity" class="card password-card">
          <div class="card-head">
            <h2 class="card-title">Đổi mật khẩu tài khoản</h2>
            <span class="card-subtitle">Mật khẩu được mã hoá an toàn chuẩn PBKDF2</span>
          </div>

          <form class="password-form" @submit.prevent="submitChangePassword">
            <div class="field">
              <label class="label">Mật khẩu hiện tại</label>
              <input
                v-model="currentPassword"
                type="password"
                class="input"
                placeholder="Nhập mật khẩu đang dùng"
                autocomplete="current-password"
                required
              />
            </div>

            <div class="field">
              <label class="label">Mật khẩu mới</label>
              <input
                v-model="newPassword"
                type="password"
                class="input"
                placeholder="Tối thiểu 6 ký tự"
                autocomplete="new-password"
                required
              />
            </div>

            <div class="field">
              <label class="label">Xác nhận mật khẩu mới</label>
              <input
                v-model="confirmPassword"
                type="password"
                class="input"
                placeholder="Nhập lại mật khẩu mới"
                autocomplete="new-password"
                required
              />
            </div>

            <button
              type="submit"
              class="btn btn-primary btn-block"
              :disabled="changingPassword"
            >
              <KeyRound :size="15" />
              <span>{{ changingPassword ? 'Đang lưu mật khẩu...' : 'Cập nhật mật khẩu mới' }}</span>
            </button>
          </form>
        </section>

        <!-- Banner thông báo cho khách chưa đăng nhập -->
        <section v-else class="card guest-notice-card">
          <div class="notice-icon">
            <AlertCircle :size="24" />
          </div>
          <div class="notice-body">
            <h3>Bạn đang dùng tài khoản tạm thời</h3>
            <p>
              Tên hiển thị hiện tại được lưu tạm trong trình duyệt này. Đăng nhập hoặc tạo tài khoản để gắn liền mọi lịch sử, nhận thông báo chuông và cá nhân hóa an toàn trên mọi thiết bị.
            </p>
            <div class="notice-buttons">
              <button
                type="button"
                class="btn btn-primary btn-sm"
                @click="loginModalOpen = true"
              >
                Đăng nhập ngay
              </button>
              <button
                type="button"
                class="btn btn-secondary btn-sm"
                @click="nameModalOpen = true"
              >
                Đổi tên hiển thị
              </button>
            </div>
          </div>
        </section>
      </div>

      <!-- Right Column: Activity Recap & Recent Actions -->
      <div class="profile-col">
        <section class="card activity-card">
          <div class="card-head between">
            <div>
              <h2 class="card-title">Nhật ký hoạt động gần đây</h2>
              <span class="card-subtitle">Những công việc bạn vừa thực hiện trong hệ thống</span>
            </div>
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              title="Tải lại nhật ký"
              :disabled="loadingRecaps"
              @click="loadProfileData"
            >
              <RefreshCw :size="14" :class="{ 'spin-icon': loadingRecaps }" />
              <span>Làm mới</span>
            </button>
          </div>

          <div v-if="loadingRecaps" class="loading-state">
            <div class="spinner"></div>
            <span>Đang lấy nhật ký hoạt động...</span>
          </div>

          <div v-else-if="recaps.length === 0" class="empty-state">
            <Activity :size="32" class="empty-icon" />
            <p>Chưa có thao tác nào được ghi nhận gần đây.</p>
            <span class="empty-sub">Khi bạn xuất file, bổ sung trích yếu hoặc đồng bộ dữ liệu, nhật ký sẽ xuất hiện ở đây.</span>
          </div>

          <div v-else class="recap-timeline">
            <div
              v-for="(item, idx) in recaps"
              :key="idx"
              class="timeline-item"
            >
              <div class="timeline-dot"></div>
              <div class="timeline-content">
                <div class="timeline-header">
                  <span class="timeline-badge">{{ item.area }}</span>
                  <span class="timeline-action">{{ item.label }}</span>
                  <span class="timeline-time">{{ relativeTime(item.at) }}</span>
                </div>
                <div v-if="item.details" class="timeline-detail">
                  {{ item.details }}
                </div>
              </div>
            </div>
          </div>
        </section>
      </div>
    </div>

    <!-- Modals -->
    <LoginModal
      :open="loginModalOpen"
      @logged="onLogged"
      @closed="loginModalOpen = false"
    />
    <UserNameModal
      :open="nameModalOpen"
      @saved="onNameSaved"
      @closed="nameModalOpen = false"
    />
  </div>
</template>

<style scoped>
.profile-view {
  display: flex;
  flex-direction: column;
  gap: 20px;
  max-width: 1200px;
  margin: 0 auto;
  padding-bottom: 40px;
}

/* Card base */
.card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-lg, 12px);
  padding: 22px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
}

.card-head {
  margin-bottom: 18px;
}

.card-head.between {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.card-title {
  font-size: 1.08rem;
  font-weight: 600;
  color: var(--color-text);
  margin: 0 0 4px 0;
}

.card-subtitle {
  font-size: 0.85rem;
  color: var(--color-text-muted);
}

/* Hero Section */
.profile-hero {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 24px;
  background: linear-gradient(135deg, var(--color-surface) 0%, var(--color-surface-hover, rgba(0, 0, 0, 0.02)) 100%);
}

.hero-left {
  display: flex;
  align-items: center;
  gap: 20px;
  flex-wrap: wrap;
}

.avatar-wrapper {
  position: relative;
  width: 92px;
  height: 92px;
  flex-shrink: 0;
}

.avatar-display {
  width: 100%;
  height: 100%;
  border-radius: 50%;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 2rem;
  font-weight: 700;
  color: #fff;
  background: var(--color-primary, #3b82f6);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
  border: 3px solid var(--color-surface);
}

.avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.avatar-camera-btn {
  position: absolute;
  right: 0;
  bottom: 0;
  width: 30px;
  height: 30px;
  border-radius: 50%;
  background: var(--color-primary, #3b82f6);
  color: #fff;
  border: 2px solid var(--color-surface);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.15);
  transition: transform 0.15s ease, background 0.15s ease;
}

.avatar-camera-btn:hover {
  transform: scale(1.08);
}

.hero-identity {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.hero-name-row {
  display: flex;
  align-items: center;
  gap: 10px;
}

.hero-name {
  font-size: 1.45rem;
  font-weight: 700;
  color: var(--color-text);
  margin: 0;
}

.btn-icon-subtle {
  background: none;
  border: none;
  color: var(--color-text-muted);
  cursor: pointer;
  padding: 4px;
  border-radius: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.btn-icon-subtle:hover {
  background: var(--color-surface-hover, rgba(0, 0, 0, 0.05));
  color: var(--color-text);
}

.name-edit-inline {
  display: flex;
  align-items: center;
  gap: 8px;
}

.hero-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.badge-role {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 0.78rem;
  font-weight: 600;
  padding: 3px 9px;
  border-radius: 999px;
  background: rgba(59, 130, 246, 0.12);
  color: #2563eb;
}

.badge-role.admin {
  background: rgba(239, 68, 68, 0.12);
  color: #dc2626;
}

.badge-guest {
  font-size: 0.78rem;
  font-weight: 500;
  padding: 3px 9px;
  border-radius: 999px;
  background: var(--color-border);
  color: var(--color-text-muted);
}

.username-tag {
  font-size: 0.84rem;
  color: var(--color-text-muted);
  font-family: var(--font-mono, monospace);
}

.status-indicator {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8rem;
  color: var(--color-text-muted);
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #9ca3af;
}

.status-indicator.online {
  color: #10b981;
}

.status-indicator.online .status-dot {
  background: #10b981;
  box-shadow: 0 0 6px rgba(16, 185, 129, 0.6);
}

.hero-timestamps {
  display: flex;
  align-items: center;
  gap: 16px;
  margin-top: 4px;
}

.timestamp-item {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 0.78rem;
  color: var(--color-text-muted);
}

.hero-actions {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

/* Stats Grid */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
}

.stat-card {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 18px;
}

.stat-icon-wrap {
  width: 44px;
  height: 44px;
  border-radius: 10px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.bg-blue {
  background: rgba(59, 130, 246, 0.12);
  color: #3b82f6;
}

.bg-emerald {
  background: rgba(16, 185, 129, 0.12);
  color: #10b981;
}

.bg-amber {
  background: rgba(245, 158, 11, 0.12);
  color: #f59e0b;
}

.bg-purple {
  background: rgba(168, 85, 247, 0.12);
  color: #a855f7;
}

.stat-content {
  display: flex;
  flex-direction: column;
}

.stat-value {
  font-size: 1.45rem;
  font-weight: 700;
  color: var(--color-text);
  line-height: 1.2;
}

.stat-label {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--color-text);
  margin-top: 2px;
}

.stat-sub {
  font-size: 0.74rem;
  color: var(--color-text-muted);
}

/* Main Grid */
.profile-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
}

@media (max-width: 900px) {
  .profile-grid {
    grid-template-columns: 1fr;
  }
}

.profile-col {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

/* Setting Items */
.setting-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.setting-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding-bottom: 14px;
  border-bottom: 1px solid var(--color-border);
}

.setting-item:last-child {
  padding-bottom: 0;
  border-bottom: none;
}

.setting-info {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.setting-title {
  font-size: 0.92rem;
  font-weight: 600;
  color: var(--color-text);
}

.setting-desc {
  font-size: 0.78rem;
  color: var(--color-text-muted);
}

.setting-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

/* Password Form */
.password-form {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.label {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--color-text);
}

/* Guest Notice */
.guest-notice-card {
  display: flex;
  gap: 16px;
  background: rgba(245, 158, 11, 0.05);
  border-color: rgba(245, 158, 11, 0.3);
}

.notice-icon {
  color: #f59e0b;
  flex-shrink: 0;
  margin-top: 2px;
}

.notice-body h3 {
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--color-text);
  margin: 0 0 6px 0;
}

.notice-body p {
  font-size: 0.82rem;
  color: var(--color-text-muted);
  line-height: 1.45;
  margin: 0 0 12px 0;
}

.notice-buttons {
  display: flex;
  gap: 10px;
}

/* Timeline */
.recap-timeline {
  display: flex;
  flex-direction: column;
  gap: 16px;
  position: relative;
  padding-left: 12px;
}

.recap-timeline::before {
  content: '';
  position: absolute;
  left: 17px;
  top: 8px;
  bottom: 8px;
  width: 2px;
  background: var(--color-border);
}

.timeline-item {
  display: flex;
  align-items: flex-start;
  gap: 14px;
  position: relative;
}

.timeline-dot {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: var(--color-primary, #3b82f6);
  border: 2px solid var(--color-surface);
  flex-shrink: 0;
  margin-top: 4px;
  z-index: 1;
}

.timeline-content {
  display: flex;
  flex-direction: column;
  gap: 4px;
  flex-grow: 1;
  background: var(--color-surface-hover, rgba(0, 0, 0, 0.02));
  padding: 10px 14px;
  border-radius: 8px;
  border: 1px solid var(--color-border);
}

.timeline-header {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.timeline-badge {
  font-size: 0.72rem;
  font-weight: 600;
  padding: 2px 7px;
  border-radius: 4px;
  background: rgba(59, 130, 246, 0.1);
  color: #2563eb;
}

.timeline-action {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--color-text);
}

.timeline-time {
  font-size: 0.74rem;
  color: var(--color-text-muted);
  margin-left: auto;
}

.timeline-detail {
  font-size: 0.8rem;
  color: var(--color-text-muted);
  line-height: 1.4;
}

/* Loading & Empty states */
.loading-state, .empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px 20px;
  text-align: center;
  gap: 10px;
  color: var(--color-text-muted);
}

.empty-icon {
  opacity: 0.4;
}

.empty-sub {
  font-size: 0.78rem;
}

.spin-icon {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.text-danger {
  color: #ef4444;
}
</style>
