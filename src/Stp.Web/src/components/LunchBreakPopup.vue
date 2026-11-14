<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import { Clock, Coffee, Sparkles, Utensils, Wrench, X } from '@lucide/vue'
import { useLunchBreak } from '../composables/useLunchBreak'

const {
  timeRemaining,
  dismissWarningPopup,
  enableDevBypass,
} = useLunchBreak()

const autoDismissSeconds = ref(15)
let autoDismissTimer: number | undefined
const isDevPinOpen = ref(false)
const devPinInput = ref('')
const pinError = ref(false)

function submitPin() {
  const ok = enableDevBypass(devPinInput.value)
  if (!ok) {
    pinError.value = true
    window.setTimeout(() => {
      pinError.value = false
    }, 1500)
  }
}

onMounted(() => {
  autoDismissTimer = window.setInterval(() => {
    if (autoDismissSeconds.value > 1) {
      autoDismissSeconds.value--
    } else {
      dismissWarningPopup()
    }
  }, 1000)
})

onUnmounted(() => {
  if (autoDismissTimer) clearInterval(autoDismissTimer)
})
</script>

<template>
  <div class="lunch-popup-backdrop" @click.self="dismissWarningPopup">
    <div class="lunch-popup-card">
      <!-- Glow & Badge -->
      <div class="lunch-popup-header">
        <div class="lunch-badge-pulse">
          <Utensils :size="16" />
          <span>GIỜ NGHỈ TRƯA TOÀN CƠ QUAN</span>
        </div>
        <button
          class="lunch-popup-close"
          type="button"
          title="Vào màn hình nghỉ trưa"
          aria-label="Đóng"
          @click="dismissWarningPopup"
        >
          <X :size="16" />
        </button>
      </div>

      <div class="lunch-popup-body">
        <div class="lunch-mascot-icon">
          <span class="mascot-emoji">🍱</span>
          <span class="mascot-star"><Sparkles :size="18" /></span>
        </div>

        <h2 class="lunch-popup-title">
          KENG KENG KENG! 12H RỒI NGHỈ ĐÊ! 🔔
        </h2>

        <p class="lunch-popup-desc">
          Dừng chuột, buông phím, đi ăn cơm nạp năng lượng ngay! Cố quá là quá cố đấy.
          Hệ thống xin phép tạm ngủ cùng bà con đến <strong>13:00</strong> chiều nay nha!
        </p>

        <div class="lunch-countdown-box">
          <Clock :size="16" class="text-amber-500" />
          <span>Thời gian nghỉ ngơi còn lại:</span>
          <strong class="countdown-digits">{{ timeRemaining.formatted }}</strong>
        </div>

        <div class="lunch-notice-box">
          <Coffee :size="16" />
          <span>Server đang được Dev tranh thủ nâng cấp tính năng mới. Vui lòng không nhập dữ liệu lúc này!</span>
        </div>

        <!-- Khung nhập PIN Dev nếu mở -->
        <div v-if="isDevPinOpen" class="dev-pin-container">
          <div class="row" style="gap: 8px">
            <input
              v-model="devPinInput"
              type="password"
              placeholder="Nhập mã Dev (stp2026)"
              class="input input-sm"
              :class="{ 'is-invalid': pinError }"
              @keyup.enter="submitPin"
            />
            <button class="btn btn-primary btn-sm" type="button" @click="submitPin">
              Vào Dev
            </button>
            <button class="btn btn-ghost btn-sm" type="button" @click="isDevPinOpen = false">
              Đóng
            </button>
          </div>
          <span v-if="pinError" class="pin-error-text">Mã Dev không đúng!</span>
        </div>
      </div>

      <div class="lunch-popup-actions">
        <button
          v-if="!isDevPinOpen"
          class="btn btn-ghost btn-sm text-muted"
          type="button"
          @click="isDevPinOpen = true"
        >
          <Wrench :size="14" />
          Em là Dev, để em sửa máy chủ!
        </button>

        <button
          class="btn btn-primary btn-md lunch-action-btn"
          type="button"
          @click="dismissWarningPopup"
        >
          <span>Đi ăn cơm & ngủ trưa thôi! 😴</span>
          <span class="auto-count-tag">({{ autoDismissSeconds }}s)</span>
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.lunch-popup-backdrop {
  position: fixed;
  inset: 0;
  z-index: 10001;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
  background: rgba(15, 23, 42, 0.75);
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
  animation: popup-fade 0.25s ease-out;
}

@keyframes popup-fade {
  from { opacity: 0; }
  to { opacity: 1; }
}

.lunch-popup-card {
  position: relative;
  width: 100%;
  max-width: 480px;
  background: var(--bg-card, #ffffff);
  border: 2px solid #f59e0b;
  border-radius: 24px;
  box-shadow: 0 25px 50px -12px rgba(245, 158, 11, 0.35), 0 0 0 1px rgba(255, 255, 255, 0.1);
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 18px;
  animation: popup-bounce 0.35s cubic-bezier(0.175, 0.885, 0.32, 1.275);
}

@keyframes popup-bounce {
  from {
    opacity: 0;
    transform: scale(0.85) translateY(20px);
  }
  to {
    opacity: 1;
    transform: scale(1) translateY(0);
  }
}

.lunch-popup-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.lunch-badge-pulse {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 4px 12px;
  border-radius: 999px;
  background: rgba(245, 158, 11, 0.15);
  color: #d97706;
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.5px;
  text-transform: uppercase;
}

.lunch-popup-close {
  width: 32px;
  height: 32px;
  border-radius: 10px;
  border: none;
  background: transparent;
  color: var(--text-muted, #64748b);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: all 0.15s ease;
}

.lunch-popup-close:hover {
  background: var(--bg-hover, #f1f5f9);
  color: var(--text-primary, #0f172a);
}

.lunch-popup-body {
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  gap: 12px;
}

.lunch-mascot-icon {
  position: relative;
  width: 72px;
  height: 72px;
  border-radius: 20px;
  background: linear-gradient(135deg, #fef3c7, #fde68a);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 38px;
  box-shadow: 0 10px 20px -5px rgba(245, 158, 11, 0.3);
  animation: wobble-mascot 3s ease-in-out infinite;
}

@keyframes wobble-mascot {
  0%, 100% { transform: rotate(0deg) scale(1); }
  25% { transform: rotate(-5deg) scale(1.05); }
  75% { transform: rotate(5deg) scale(1.05); }
}

.mascot-star {
  position: absolute;
  top: -6px;
  right: -6px;
  color: #f59e0b;
  animation: spin-star 4s linear infinite;
}

@keyframes spin-star {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.lunch-popup-title {
  font-size: 20px;
  font-weight: 800;
  color: var(--text-primary, #0f172a);
  margin: 4px 0 0 0;
  line-height: 1.3;
}

.lunch-popup-desc {
  font-size: 14px;
  line-height: 1.6;
  color: var(--text-secondary, #475569);
  margin: 0;
  max-width: 400px;
}

.lunch-countdown-box {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 16px;
  border-radius: 12px;
  background: var(--bg-hover, #f8fafc);
  border: 1px solid var(--border, #e2e8f0);
  font-size: 13px;
  color: var(--text-secondary, #475569);
}

.countdown-digits {
  font-family: monospace;
  font-size: 16px;
  color: #d97706;
}

.lunch-notice-box {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  border-radius: 10px;
  background: rgba(59, 130, 246, 0.08);
  border: 1px dashed rgba(59, 130, 246, 0.3);
  font-size: 12px;
  color: #2563eb;
  text-align: left;
}

.dev-pin-container {
  width: 100%;
  padding: 10px;
  background: var(--bg-hover, #f1f5f9);
  border-radius: 12px;
  margin-top: 4px;
}

.pin-error-text {
  font-size: 11px;
  color: #ef4444;
  font-weight: 600;
  margin-top: 4px;
  display: block;
}

.lunch-popup-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding-top: 6px;
  border-top: 1px solid var(--border, #e2e8f0);
}

.lunch-action-btn {
  background: linear-gradient(135deg, #f59e0b, #d97706);
  border: none;
  font-weight: 700;
  padding: 10px 20px;
  border-radius: 12px;
  display: flex;
  align-items: center;
  gap: 8px;
}

.lunch-action-btn:hover {
  background: linear-gradient(135deg, #d97706, #b45309);
  transform: translateY(-1px);
}

.auto-count-tag {
  font-size: 11px;
  opacity: 0.8;
  font-weight: normal;
}
</style>
