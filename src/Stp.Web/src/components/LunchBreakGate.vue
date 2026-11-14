<script setup lang="ts">
import { Wrench } from '@lucide/vue'
import { useLunchBreak } from '../composables/useLunchBreak'
import LunchBreakPopup from './LunchBreakPopup.vue'
import LunchBreakScreen from './LunchBreakScreen.vue'

const {
  isLunchBreakActive,
  showWarningPopup,
  isDevBypass,
  disableDevBypass,
} = useLunchBreak()
</script>

<template>
  <div class="lunch-gate-container">
    <!-- Popup nhắc nhở nghỉ trưa ban đầu -->
    <LunchBreakPopup v-if="showWarningPopup" />

    <!-- Màn hình nghỉ trưa toàn diện nếu đang trong khung giờ hoặc preview -->
    <Teleport to="body">
      <Transition name="lunch-screen-fade">
        <LunchBreakScreen v-if="isLunchBreakActive" />
      </Transition>
    </Teleport>

    <!-- Floating badge cho Dev khi đã bypass -->
    <Teleport to="body">
      <div v-if="isDevBypass && !isLunchBreakActive" class="dev-bypass-pill">
        <div class="dev-pill-inner">
          <Wrench :size="13" class="text-amber-400" />
          <span>Dev Mode: Đang bỏ qua giờ nghỉ trưa</span>
          <button
            class="btn btn-ghost btn-xs text-amber-300"
            type="button"
            title="Bật lại chế độ nghỉ trưa để kiểm tra"
            @click="disableDevBypass"
          >
            Bật lại
          </button>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.lunch-screen-fade-enter-active,
.lunch-screen-fade-leave-active {
  transition: opacity 0.3s ease;
}

.lunch-screen-fade-enter-from,
.lunch-screen-fade-leave-to {
  opacity: 0;
}

.dev-bypass-pill {
  position: fixed;
  bottom: 16px;
  right: 16px;
  z-index: 9990;
  animation: pill-slide 0.3s ease-out;
}

@keyframes pill-slide {
  from {
    transform: translateY(20px);
    opacity: 0;
  }
  to {
    transform: translateY(0);
    opacity: 1;
  }
}

.dev-pill-inner {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  border-radius: 999px;
  background: rgba(15, 23, 42, 0.9);
  color: #f8fafc;
  font-size: 12px;
  font-weight: 600;
  border: 1px solid #f59e0b;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
  backdrop-filter: blur(8px);
}
</style>
