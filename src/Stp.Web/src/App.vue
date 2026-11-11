<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { RouterView } from 'vue-router'
import { FolderOpen } from '@lucide/vue'
import { getDevToken } from './api/client'
import { initTeamIdentity, teamIdentity } from './composables/useTeamIdentity'
import AuthGate from './components/AuthGate.vue'
import BootSplash from './components/BootSplash.vue'
import WelcomeRecap from './components/WelcomeRecap.vue'
import TeamDock from './components/TeamDock.vue'
import ToastHost from './components/ToastHost.vue'

/**
 * Nghi thức ra vào hệ thống (yêu cầu 11/11/2026):
 *   checking → hỏi cookie phiên (/api/auth/me)
 *   gate     → chưa có phiên: mở "cuốn hồ sơ" (đăng nhập / lật trang đăng ký)
 *   splash   → vừa xong: màn chờ 2.6s (lần đầu) hoặc 1.1s (quay lại có sẵn phiên)
 *   app      → vào hệ thống + thẻ tổng kết "bạn vừa làm gì, ở khu nào"
 * Đăng xuất giữa phiên → quay về gate. Máy dev có token JWT vẫn vào thẳng.
 */
type Phase = 'checking' | 'gate' | 'splash' | 'app'

const phase = ref<Phase>('checking')
const splashMs = ref(2600)
const showRecap = ref(false)

onMounted(async () => {
  const identity = await initTeamIdentity()
  if (identity || getDevToken()) {
    // Quay lại mà phiên còn (ghi nhớ 24 giờ sliding) — vào nhanh, không vướng đăng nhập.
    splashMs.value = 1100
    phase.value = 'splash'
  } else {
    phase.value = 'gate'
  }
})

function onEntered(): void {
  splashMs.value = 2600
  phase.value = 'splash'
}

function onSplashDone(): void {
  phase.value = 'app'
  showRecap.value = true
}

watch(teamIdentity, (value) => {
  if (!value && phase.value === 'app') {
    showRecap.value = false
    phase.value = 'gate'
  }
})
</script>

<template>
  <div class="app-root">
    <AuthGate v-if="phase === 'gate'" @entered="onEntered" />
    <Transition name="splash-fade">
      <BootSplash v-if="phase === 'splash'" :duration-ms="splashMs" @done="onSplashDone" />
    </Transition>
    <template v-if="phase === 'app'">
      <RouterView />
      <TeamDock />
      <WelcomeRecap v-if="showRecap" @closed="showRecap = false" />
    </template>
    <div v-if="phase === 'checking'" class="boot-check" aria-label="Đang kiểm tra phiên">
      <div class="boot-mark">
        <FolderOpen :size="24" />
      </div>
    </div>
    <ToastHost v-if="phase === 'app'" />
  </div>
</template>

<style scoped>
.boot-check {
  position: fixed;
  inset: 0;
  display: grid;
  place-items: center;
  background: linear-gradient(155deg, #052e2c 0%, #073a37 50%, #0a4640 100%);
}

.boot-mark {
  width: 52px;
  height: 52px;
  border-radius: 15px;
  display: grid;
  place-items: center;
  background: linear-gradient(135deg, #0d9488, #115e59);
  color: #ccfbf1;
  animation: pulse 1.1s ease-in-out infinite;
}

@keyframes pulse {
  0%, 100% { transform: scale(1); opacity: 0.85; }
  50% { transform: scale(1.08); opacity: 1; }
}

/* Màn chờ (cuốn hồ sơ lật) tắt mượt khi vào app */
.splash-fade-leave-active {
  transition: opacity 0.4s ease, transform 0.4s ease;
}

.splash-fade-leave-to {
  opacity: 0;
  transform: scale(1.04);
}
</style>
