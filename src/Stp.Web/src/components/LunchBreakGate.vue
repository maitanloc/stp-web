<script setup lang="ts">
import { onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Wrench } from '@lucide/vue'
import { useLunchBreak } from '../composables/useLunchBreak'
import LunchBreakPopup from './LunchBreakPopup.vue'
import PreLunchWarningBanner from './PreLunchWarningBanner.vue'

const router = useRouter()
const route = useRoute()

const {
  isLunchBreakActive,
  showWarningPopup,
  isDevBypass,
  disableDevBypass,
} = useLunchBreak()

// Cứ đến 12h: Tự động chuyển hướng sang tab nghỉ trưa (/nghi-trua)
function checkAutoNavigate(active: boolean) {
  if (active && !isDevBypass.value) {
    if (route.path !== '/nghi-trua') {
      try {
        sessionStorage.setItem('stp.lunch.auto_navigated', 'true')
      } catch {}
      void router.push('/nghi-trua')
    }
  } else if (!active && route.path === '/nghi-trua') {
    // 13:00 hết giờ nghỉ trưa: tự động đưa người dùng trở lại Dashboard
    try {
      if (sessionStorage.getItem('stp.lunch.auto_navigated') === 'true') {
        sessionStorage.removeItem('stp.lunch.auto_navigated')
        void router.push('/')
      }
    } catch {}
  }
}

watch(isLunchBreakActive, (active) => {
  checkAutoNavigate(active)
}, { immediate: true })

watch(isDevBypass, (bypass) => {
  if (!bypass && isLunchBreakActive.value && route.path !== '/nghi-trua') {
    void router.push('/nghi-trua')
  }
})

onMounted(() => {
  checkAutoNavigate(isLunchBreakActive.value)
})
</script>

<template>
  <div class="lunch-gate-container">
    <!-- Cảnh báo đóng server trước 10 phút (11:50) cho toàn cơ quan -->
    <PreLunchWarningBanner />

    <!-- Popup nhắc nhở nghỉ trưa ban đầu lúc 12:00 -->
    <LunchBreakPopup v-if="showWarningPopup" />

    <!-- Floating badge cho Dev khi đã bypass -->
    <Teleport to="body">
      <div v-if="isDevBypass" class="dev-bypass-pill">
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
