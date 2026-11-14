<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  Clock,
  Hand,
  Music,
  Power,
  Sparkles,
  Volume2,
  Wrench,
} from '@lucide/vue'
import { useLunchBreak } from '../composables/useLunchBreak'
import StickmanSleepers from './StickmanSleepers.vue'

const {
  now,
  timeRemaining,
  isPreview,
  pokeCount,
  pokeQuote,
  isLullabyPlaying,
  togglePreview,
  enableDevBypass,
  pokeStickman,
  toggleLullaby,
} = useLunchBreak()

const isDevPinModalOpen = ref(false)
const devPinInput = ref('')
const pinError = ref(false)
const isPokedEffect = ref(false)

const formattedCurrentTime = computed(() => {
  return now.value.toLocaleTimeString('vi-VN', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
})

function handlePoke() {
  isPokedEffect.value = true
  pokeStickman()
  window.setTimeout(() => {
    isPokedEffect.value = false
  }, 600)
}

function submitDevPin() {
  const ok = enableDevBypass(devPinInput.value)
  if (ok) {
    isDevPinModalOpen.value = false
    devPinInput.value = ''
  } else {
    pinError.value = true
    window.setTimeout(() => {
      pinError.value = false
    }, 1500)
  }
}
</script>

<template>
  <div class="lunch-screen-root">
    <!-- Header thanh trạng thái nhẹ nhàng -->
    <header class="lunch-screen-header">
      <div class="lunch-logo-brand">
        <span class="brand-pill">STP ARCHIVE</span>
        <span class="brand-mode">CHẾ ĐỘ NGHỈ TRƯA & BẢO TRÌ</span>
      </div>

      <div class="lunch-header-right">
        <!-- Nút nhạc ru ngủ -->
        <button
          class="btn btn-secondary btn-sm lullaby-btn"
          :class="{ 'is-active': isLullabyPlaying }"
          type="button"
          :title="isLullabyPlaying ? 'Tắt nhạc ru ngủ' : 'Bật nhạc ru ngủ thư giãn'"
          @click="toggleLullaby"
        >
          <component :is="isLullabyPlaying ? Volume2 : Music" :size="14" />
          <span>{{ isLullabyPlaying ? 'Đang phát nhạc ru…' : 'Bật nhạc ru ngủ' }}</span>
        </button>

        <!-- Nếu đang Preview thì có nút tắt Preview -->
        <button
          v-if="isPreview"
          class="btn btn-warning btn-sm"
          type="button"
          @click="togglePreview"
        >
          <Power :size="14" />
          Thoát Preview
        </button>

        <!-- Nút Kỹ thuật viên / Dev -->
        <button
          class="btn btn-ghost btn-sm dev-door-btn"
          type="button"
          title="Dành cho lập trình viên phát triển hệ thống"
          @click="isDevPinModalOpen = true"
        >
          <Wrench :size="14" />
          <span>Dành cho Dev</span>
        </button>
      </div>
    </header>

    <!-- Nội dung chính -->
    <main class="lunch-screen-main">
      <div class="lunch-title-group">
        <div class="lunch-clock-badge">
          <Clock :size="16" />
          <span>BÂY GIỜ LÀ: <strong>{{ formattedCurrentTime }}</strong></span>
        </div>

        <h1 class="lunch-main-heading">
          😴 VĂN PHÒNG ĐANG NGHỈ TRƯA (12:00 – 13:00)
        </h1>
        <p class="lunch-sub-heading">
          Máy chủ tạm thời "ngủ đông" để đội ngũ kỹ thuật nâng cấp máy chủ & cấy thêm tính năng mới.
          Mời bà con nghỉ ngơi, đúng <strong>13:00:00</strong> hệ thống sẽ tự động mở lại!
        </p>
      </div>

      <!-- Khung đồng hồ đếm ngược phong cách điện tử -->
      <div class="lunch-timer-card">
        <span class="timer-label">HỆ THỐNG SẼ TỰ ĐỘNG MỞ LẠI SAU:</span>
        <div class="timer-digits-row">
          <div class="timer-unit">
            <span class="timer-num">00</span>
            <span class="timer-text">GIỜ</span>
          </div>
          <span class="timer-sep">:</span>
          <div class="timer-unit">
            <span class="timer-num">{{ String(timeRemaining.minutes).padStart(2, '0') }}</span>
            <span class="timer-text">PHÚT</span>
          </div>
          <span class="timer-sep">:</span>
          <div class="timer-unit">
            <span class="timer-num">{{ String(timeRemaining.seconds).padStart(2, '0') }}</span>
            <span class="timer-text">GIÂY</span>
          </div>
        </div>
      </div>

      <!-- Hoạt hoạ Stickman nhân viên ngủ trưa -->
      <div class="stickman-wrapper">
        <StickmanSleepers
          :poked="isPokedEffect"
          :quote="pokeQuote"
          @poke="handlePoke"
        />
      </div>

      <!-- Các nút tương tác vui nhộn -->
      <div class="lunch-interactive-bar">
        <button class="btn btn-secondary btn-md poke-btn" type="button" @click="handlePoke">
          <Hand :size="16" />
          <span>Chọt nhân viên tỉnh dậy ({{ pokeCount }})</span>
        </button>

        <div class="lunch-funny-tip">
          <Sparkles :size="15" class="text-amber-400" />
          <span>Tip: Chợp mắt 20 phút buổi trưa giúp tăng 34% sự tập trung buổi chiều!</span>
        </div>
      </div>
    </main>

    <!-- Modal nhập PIN dành cho Dev -->
    <Teleport to="body">
      <div
        v-if="isDevPinModalOpen"
        class="dev-modal-backdrop"
        @click.self="isDevPinModalOpen = false"
      >
        <div class="dev-modal-card">
          <div class="dev-modal-header">
            <div class="dev-modal-icon">
              <Wrench :size="20" />
            </div>
            <div>
              <h3 class="dev-modal-title">Chế độ Nhà phát triển</h3>
              <p class="dev-modal-desc">
                Bỏ qua màn hình nghỉ trưa để truy cập hệ thống phát triển & kiểm thử.
              </p>
            </div>
          </div>

          <div class="dev-modal-body">
            <label class="field">
              <span class="label">Mã mở khoá Dev</span>
              <input
                v-model="devPinInput"
                type="password"
                class="input"
                placeholder="Nhập mã Dev (mặc định: stp2026)"
                :class="{ 'is-invalid': pinError }"
                autofocus
                @keyup.enter="submitDevPin"
              />
              <span v-if="pinError" class="pin-error-text">
                Mã không chính xác! Thử "stp2026", "dev123" hoặc đăng nhập tài khoản Admin.
              </span>
            </label>
          </div>

          <div class="dev-modal-actions">
            <button
              class="btn btn-secondary btn-sm"
              type="button"
              @click="isDevPinModalOpen = false"
            >
              Huỷ
            </button>
            <button class="btn btn-primary btn-sm" type="button" @click="submitDevPin">
              Vào hệ thống phát triển
            </button>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.lunch-screen-root {
  min-height: 100vh;
  width: 100%;
  background: radial-gradient(circle at 50% 20%, #1e1b4b 0%, #0f172a 60%, #020617 100%);
  color: #f8fafc;
  display: flex;
  flex-direction: column;
  position: relative;
  z-index: 9999;
}

.lunch-screen-header {
  height: 64px;
  padding: 0 24px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(15, 23, 42, 0.6);
  backdrop-filter: blur(10px);
}

.lunch-logo-brand {
  display: flex;
  align-items: center;
  gap: 12px;
}

.brand-pill {
  font-weight: 800;
  font-size: 13px;
  letter-spacing: 1px;
  background: linear-gradient(135deg, #38bdf8, #818cf8);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
}

.brand-mode {
  font-size: 12px;
  color: #94a3b8;
  padding-left: 10px;
  border-left: 1px solid rgba(255, 255, 255, 0.15);
}

.lunch-header-right {
  display: flex;
  align-items: center;
  gap: 10px;
}

.lullaby-btn {
  background: rgba(255, 255, 255, 0.08);
  color: #e2e8f0;
  border: 1px solid rgba(255, 255, 255, 0.12);
}

.lullaby-btn.is-active {
  background: rgba(99, 102, 241, 0.3);
  border-color: #6366f1;
  color: #a5b4fc;
}

.dev-door-btn {
  color: #94a3b8;
}

.dev-door-btn:hover {
  color: #f8fafc;
  background: rgba(255, 255, 255, 0.08);
}

.lunch-screen-main {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 32px 20px 48px;
  gap: 24px;
  max-width: 980px;
  margin: 0 auto;
  width: 100%;
}

.lunch-title-group {
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
}

.lunch-clock-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 14px;
  border-radius: 999px;
  background: rgba(56, 189, 248, 0.12);
  color: #38bdf8;
  font-size: 12px;
  font-weight: 600;
  border: 1px solid rgba(56, 189, 248, 0.25);
}

.lunch-main-heading {
  font-size: 26px;
  font-weight: 800;
  margin: 0;
  background: linear-gradient(135deg, #ffffff, #cbd5e1);
  -webkit-background-clip: text;
  -webkit-text-fill-color: transparent;
}

.lunch-sub-heading {
  font-size: 14px;
  color: #94a3b8;
  margin: 0;
  max-width: 620px;
  line-height: 1.6;
}

.lunch-timer-card {
  background: rgba(30, 41, 59, 0.7);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 18px;
  padding: 14px 28px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.3);
}

.timer-label {
  font-size: 11px;
  letter-spacing: 1px;
  font-weight: 700;
  color: #fbbf24;
}

.timer-digits-row {
  display: flex;
  align-items: center;
  gap: 12px;
}

.timer-unit {
  display: flex;
  flex-direction: column;
  align-items: center;
}

.timer-num {
  font-family: 'Courier New', Courier, monospace;
  font-size: 32px;
  font-weight: 900;
  color: #ffffff;
  background: #0f172a;
  padding: 4px 12px;
  border-radius: 8px;
  border: 1px solid #334155;
  min-width: 60px;
  text-align: center;
  box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.5);
}

.timer-text {
  font-size: 9px;
  font-weight: 700;
  color: #64748b;
  margin-top: 4px;
}

.timer-sep {
  font-size: 28px;
  font-weight: 900;
  color: #f59e0b;
  animation: blink-sep 1s infinite;
}

@keyframes blink-sep {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.2; }
}

.stickman-wrapper {
  width: 100%;
}

.lunch-interactive-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  max-width: 860px;
  gap: 16px;
  flex-wrap: wrap;
}

.poke-btn {
  background: linear-gradient(135deg, #4f46e5, #4338ca);
  border: 1px solid #6366f1;
  color: #ffffff;
  font-weight: 600;
  padding: 8px 18px;
  border-radius: 12px;
  box-shadow: 0 4px 14px rgba(79, 70, 229, 0.4);
  transition: all 0.15s ease;
}

.poke-btn:hover {
  transform: translateY(-2px);
  box-shadow: 0 6px 20px rgba(79, 70, 229, 0.6);
}

.lunch-funny-tip {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: #94a3b8;
  background: rgba(15, 23, 42, 0.6);
  padding: 8px 16px;
  border-radius: 12px;
  border: 1px solid rgba(255, 255, 255, 0.08);
}

/* Modal Dev */
.dev-modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 10005;
  background: rgba(2, 6, 23, 0.8);
  backdrop-filter: blur(8px);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
}

.dev-modal-card {
  width: 100%;
  max-width: 420px;
  background: #1e293b;
  border: 1px solid #334155;
  border-radius: 18px;
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 18px;
  box-shadow: 0 20px 40px rgba(0, 0, 0, 0.5);
  color: #f8fafc;
}

.dev-modal-header {
  display: flex;
  align-items: flex-start;
  gap: 14px;
}

.dev-modal-icon {
  width: 42px;
  height: 42px;
  border-radius: 10px;
  background: rgba(56, 189, 248, 0.15);
  color: #38bdf8;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.dev-modal-title {
  margin: 0 0 4px 0;
  font-size: 16px;
  font-weight: 700;
  color: #ffffff;
}

.dev-modal-desc {
  margin: 0;
  font-size: 13px;
  color: #94a3b8;
  line-height: 1.5;
}

.pin-error-text {
  font-size: 12px;
  color: #f87171;
  margin-top: 4px;
  display: block;
}

.dev-modal-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 10px;
}
</style>
