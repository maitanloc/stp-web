<script setup lang="ts">
import { computed } from 'vue'
import {
  AlertTriangle,
  Clock,
  Save,
  Sparkles,
  X,
} from '@lucide/vue'
import { useLunchBreak } from '../composables/useLunchBreak'

const {
  now,
  isPreLunchWarning,
  timeToLunch,
  show10MinModal,
  is10MinBannerDismissed,
  dismiss10MinModal,
  dismiss10MinBanner,
} = useLunchBreak()

const formattedCurrentTime = computed(() => {
  return now.value.toLocaleTimeString('vi-VN', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
})
</script>

<template>
  <!-- 1. Thanh thông báo dính trên cùng (Sticky Banner 11:50 - 12:00) -->
  <Transition name="banner-slide">
    <aside
      v-if="isPreLunchWarning && !is10MinBannerDismissed"
      class="pre-lunch-banner"
      role="alert"
      aria-live="assertive"
    >
      <div class="banner-content">
        <div class="banner-badge">
          <AlertTriangle :size="15" class="banner-pulse-icon" />
          <span>BÁO TRƯỚC 10 PHÚT (11:50)</span>
        </div>

        <div class="banner-message">
          <span>⏰ Bây giờ là <strong>{{ formattedCurrentTime }}</strong>. Hệ thống sẽ đóng máy chủ nghỉ trưa & bảo trì lúc <strong>12:00:00</strong>.</span>
          <span class="banner-sub">Vui lòng lưu lại toàn bộ hồ sơ, dữ liệu đang nhập dở!</span>
        </div>

        <!-- Bộ đếm ngược đến 12h -->
        <div class="banner-countdown">
          <Clock :size="14" />
          <span>ĐÓNG SERVER SAU:</span>
          <strong class="countdown-num">{{ timeToLunch.formatted }}</strong>
        </div>

        <button
          class="banner-close-btn"
          type="button"
          title="Ẩn thanh thông báo"
          aria-label="Đóng thanh thông báo"
          @click="dismiss10MinBanner"
        >
          <X :size="15" />
        </button>
      </div>
    </aside>
  </Transition>

  <!-- 2. Modal Popup Cảnh báo bung ra đúng 11:50:00 -->
  <Teleport to="body">
    <Transition name="modal-pop">
      <div
        v-if="show10MinModal"
        class="warning-modal-backdrop"
        @click.self="dismiss10MinModal"
      >
        <div class="warning-modal-card" role="dialog" aria-modal="true">
          <div class="warning-modal-header">
            <div class="warning-modal-icon">
              <AlertTriangle :size="24" />
            </div>
            <div>
              <span class="warning-pill">THÔNG BÁO QUAN TRỌNG</span>
              <h3 class="warning-modal-title">Sắp Đến Giờ Nghỉ Trưa & Bảo Trì!</h3>
            </div>
          </div>

          <div class="warning-modal-body">
            <p class="warning-lead">
              Đồng hồ đã điểm <strong>11:50</strong>. Đúng <strong>12:00:00</strong> trưa nay, máy chủ sẽ tạm thời chuyển sang chế độ <strong>Nghỉ trưa & Nâng cấp hệ thống</strong>.
            </p>

            <div class="warning-countdown-card">
              <div class="countdown-label">THỜI GIAN CÒN LẠI ĐỂ HOÀN TẤT CÔNG VIỆC:</div>
              <div class="countdown-timer-display">
                <div class="timer-box">
                  <span class="box-num">{{ String(timeToLunch.minutes).padStart(2, '0') }}</span>
                  <span class="box-unit">PHÚT</span>
                </div>
                <span class="box-sep">:</span>
                <div class="timer-box">
                  <span class="box-num">{{ String(timeToLunch.seconds).padStart(2, '0') }}</span>
                  <span class="box-unit">GIÂY</span>
                </div>
              </div>
            </div>

            <div class="warning-advice-box">
              <Save :size="16" class="text-amber-400 shrink-0" />
              <span>
                <strong>Khuyến nghị:</strong> Bấm Lưu (Save) các sổ sách, trích yếu hoặc macro đang xử lý. Từ 12h00 đến 13h00, hệ thống sẽ chỉ mở cho Nhà phát triển (Dev).
              </span>
            </div>
          </div>

          <div class="warning-modal-footer">
            <button
              class="btn btn-primary btn-md w-full confirm-btn"
              type="button"
              @click="dismiss10MinModal"
            >
              <Sparkles :size="16" />
              <span>Đã hiểu, tôi sẽ lưu công việc ngay!</span>
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
/* Sticky Top Banner */
.pre-lunch-banner {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  z-index: 9998;
  background: linear-gradient(135deg, #b45309, #d97706, #b45309);
  background-size: 200% 200%;
  animation: gradient-shift 4s ease infinite;
  color: #ffffff;
  padding: 8px 16px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.35), 0 1px 0 rgba(255, 255, 255, 0.2);
}

@keyframes gradient-shift {
  0% { background-position: 0% 50%; }
  50% { background-position: 100% 50%; }
  100% { background-position: 0% 50%; }
}

.banner-content {
  max-width: 1280px;
  margin: 0 auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  flex-wrap: wrap;
}

.banner-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: rgba(0, 0, 0, 0.35);
  padding: 3px 10px;
  border-radius: 999px;
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.5px;
  border: 1px solid rgba(255, 255, 255, 0.25);
  color: #fef08a;
  white-space: nowrap;
}

.banner-pulse-icon {
  animation: pulse-warn 1.2s infinite;
}

@keyframes pulse-warn {
  0%, 100% { transform: scale(1); opacity: 1; }
  50% { transform: scale(1.25); opacity: 0.7; }
}

.banner-message {
  flex: 1;
  font-size: 13px;
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 280px;
}

.banner-sub {
  color: #fef08a;
  font-weight: 500;
  opacity: 0.95;
}

.banner-countdown {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  background: #0f172a;
  padding: 4px 12px;
  border-radius: 8px;
  border: 1px solid #ca8a04;
  font-size: 11px;
  font-weight: 700;
  color: #fef08a;
}

.countdown-num {
  font-family: 'Courier New', Courier, monospace;
  font-size: 16px;
  font-weight: 900;
  color: #ffffff;
}

.banner-close-btn {
  background: rgba(0, 0, 0, 0.2);
  border: 1px solid rgba(255, 255, 255, 0.2);
  color: #ffffff;
  width: 26px;
  height: 26px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: all 0.15s ease;
}

.banner-close-btn:hover {
  background: rgba(0, 0, 0, 0.4);
}

.banner-slide-enter-active,
.banner-slide-leave-active {
  transition: transform 0.3s ease, opacity 0.3s ease;
}

.banner-slide-enter-from,
.banner-slide-leave-to {
  transform: translateY(-100%);
  opacity: 0;
}

/* Modal Popup 11:50 */
.warning-modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 10006;
  background: rgba(2, 6, 23, 0.85);
  backdrop-filter: blur(8px);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 16px;
}

.warning-modal-card {
  width: 100%;
  max-width: 480px;
  background: #1e293b;
  border: 1px solid #f59e0b;
  border-radius: 20px;
  padding: 24px;
  color: #f8fafc;
  box-shadow: 0 25px 60px -15px rgba(245, 158, 11, 0.3), 0 0 0 1px rgba(245, 158, 11, 0.2);
  display: flex;
  flex-direction: column;
  gap: 18px;
  animation: card-pop 0.35s cubic-bezier(0.16, 1, 0.3, 1);
}

@keyframes card-pop {
  0% { transform: scale(0.9); opacity: 0; }
  100% { transform: scale(1); opacity: 1; }
}

.warning-modal-header {
  display: flex;
  align-items: center;
  gap: 14px;
}

.warning-modal-icon {
  width: 48px;
  height: 48px;
  border-radius: 14px;
  background: rgba(245, 158, 11, 0.2);
  color: #fbbf24;
  border: 1px solid rgba(245, 158, 11, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.warning-pill {
  font-size: 10px;
  font-weight: 800;
  letter-spacing: 0.5px;
  color: #fbbf24;
  background: rgba(245, 158, 11, 0.15);
  padding: 2px 8px;
  border-radius: 999px;
  display: inline-block;
  margin-bottom: 4px;
}

.warning-modal-title {
  margin: 0;
  font-size: 17px;
  font-weight: 800;
  color: #ffffff;
}

.warning-modal-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.warning-lead {
  margin: 0;
  font-size: 13.5px;
  color: #cbd5e1;
  line-height: 1.5;
}

.warning-countdown-card {
  background: #0f172a;
  border: 1px solid #334155;
  border-radius: 14px;
  padding: 12px 18px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
}

.countdown-label {
  font-size: 10px;
  letter-spacing: 0.8px;
  font-weight: 700;
  color: #fbbf24;
}

.countdown-timer-display {
  display: flex;
  align-items: center;
  gap: 10px;
}

.timer-box {
  display: flex;
  flex-direction: column;
  align-items: center;
}

.box-num {
  font-family: 'Courier New', Courier, monospace;
  font-size: 32px;
  font-weight: 900;
  color: #f8fafc;
  background: #1e293b;
  padding: 4px 12px;
  border-radius: 8px;
  border: 1px solid #475569;
  min-width: 55px;
  text-align: center;
}

.box-unit {
  font-size: 9px;
  font-weight: 700;
  color: #94a3b8;
  margin-top: 3px;
}

.box-sep {
  font-size: 28px;
  font-weight: 900;
  color: #f59e0b;
  animation: blink-sep 1s infinite;
}

@keyframes blink-sep {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.2; }
}

.warning-advice-box {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  background: rgba(245, 158, 11, 0.1);
  border: 1px solid rgba(245, 158, 11, 0.25);
  border-radius: 12px;
  padding: 10px 14px;
  font-size: 12px;
  color: #fef08a;
  line-height: 1.5;
}

.confirm-btn {
  background: linear-gradient(135deg, #f59e0b, #d97706);
  border: none;
  font-weight: 700;
  color: #ffffff;
  padding: 10px 20px;
  border-radius: 12px;
  box-shadow: 0 4px 15px rgba(245, 158, 11, 0.35);
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}

.confirm-btn:hover {
  transform: translateY(-1px);
  box-shadow: 0 6px 20px rgba(245, 158, 11, 0.5);
}

.modal-pop-enter-active,
.modal-pop-leave-active {
  transition: opacity 0.25s ease;
}

.modal-pop-enter-from,
.modal-pop-leave-to {
  opacity: 0;
}
</style>

