<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { FolderOpen } from '@lucide/vue'

/**
 * Màn chờ "đợi vào hệ thống" — là một cuốn hồ sơ tự lật từng trang (yêu cầu
 * 11/11/2026): trang hồ sơ nhấc lên, xoay 3D mượt từ phải sang trái, lật hết
 * đống hồ sơ là vào app. Chạy đúng số giây prop truyền vào (lần đầu ~2.6s,
 * quay lại có phiên ~1.1s) rồi báo App.vue mở hệ thống.
 */
const props = defineProps<{ durationMs?: number }>()
const emit = defineEmits<{ done: [] }>()

const steps = [
  'Đang mở kho hồ sơ…',
  'Nạp khu làm việc của bạn…',
  'Bắt tay phiên nhóm…',
  'Sẵn sàng!',
]

const stepIndex = ref(0)

const totalMs = computed(() => props.durationMs ?? 2600)

// Số trang lật: đợi lâu lật 3 trang cho đủ chuyện, đợi nhanh lật 2 trang.
const leafCount = computed(() => (totalMs.value >= 2000 ? 3 : 2))

// Thời gian một đường lật — mỗi trang nhấc, xoay 180°, đặt xuống nhịp nhàng.
const flipMs = computed(() =>
  Math.round(Math.min(760, Math.max(340, totalMs.value / (leafCount.value + 0.8)))),
)

// Khoảng cách giữa hai đường lật: đường cuối kết thúc đúng lúc vào app.
const flipGapMs = computed(() =>
  Math.round((totalMs.value - flipMs.value) / Math.max(1, leafCount.value - 1)),
)

let stepTimer: number | undefined
let doneTimer: number | undefined

function leafStyle(index: number): Record<string, string> {
  const delay = (index - 1) * flipGapMs.value
  return {
    // Trang đầu nằm trên cùng để lật đè các trang dưới; sau khi nằm xuống thì
    // trang kế tiếp (thứ tự DOM) tự nhiên đè lên nó như một cuốn sách thật.
    zIndex: String((leafCount.value - index + 1) * 10),
    '--flip-ms': `${flipMs.value}ms`,
    '--delay-ms': `${delay}ms`,
  }
}

onMounted(() => {
  stepTimer = window.setInterval(() => {
    stepIndex.value = Math.min(stepIndex.value + 1, steps.length - 1)
  }, Math.max(500, totalMs.value / steps.length))

  doneTimer = window.setTimeout(() => emit('done'), totalMs.value)
})

onUnmounted(() => {
  window.clearInterval(stepTimer)
  window.clearTimeout(doneTimer)
})
</script>

<template>
  <div class="splash" role="status" aria-label="Đang vào hệ thống">
    <div class="scene">
      <div class="book">
        <!-- Bìa trái: nơi các trang nằm xuống sau khi lật -->
        <div class="cover">
          <FolderOpen :size="24" />
          <span>HỒ SƠ</span>
        </div>
        <!-- Trang đáy bên phải: lộ dần khi các trang trên lần lượt lật qua -->
        <div class="page-base">
          <FolderOpen class="wm" :size="34" />
        </div>
        <!-- Các tờ hồ sơ lật lần lượt -->
        <div
          v-for="index in leafCount"
          :key="index"
          class="leaf"
          :style="leafStyle(index)"
        >
          <div class="face leaf-front">
            <span class="doc-tag">HỒ SƠ {{ String(index).padStart(2, '0') }}</span>
            <span class="line w82"></span>
            <span class="line w95"></span>
            <span class="line w68"></span>
            <span class="line w88"></span>
            <span class="line w55"></span>
          </div>
          <div class="face leaf-back">
            <span class="stamp"></span>
            <span class="line w76"></span>
            <span class="line w92"></span>
            <span class="line w62"></span>
          </div>
        </div>
      </div>
      <div class="floor"></div>
    </div>

    <h1>STP Archive</h1>
    <Transition name="step" mode="out-in">
      <p :key="stepIndex" class="step">{{ steps[stepIndex] }}</p>
    </Transition>
  </div>
</template>

<style scoped>
.splash {
  position: fixed;
  inset: 0;
  z-index: 110;
  display: grid;
  place-content: center;
  justify-items: center;
  background:
    radial-gradient(900px 500px at 50% -10%, rgba(20, 184, 166, 0.25), transparent 60%),
    linear-gradient(155deg, #052e2c 0%, #073a37 50%, #0a4640 100%);
  color: #eafff9;
}

/* ===== Cuốn hồ sơ ===== */
.scene {
  display: grid;
  justify-items: center;
}

.book {
  position: relative;
  width: 228px;
  height: 296px;
  perspective: 1150px;
}

/* Bìa trái */
.cover {
  position: absolute;
  left: 0;
  top: 0;
  width: 50%;
  height: 100%;
  border-radius: 10px 0 0 10px;
  background:
    radial-gradient(220px 160px at 0% -20%, rgba(255, 255, 255, 0.1), transparent 60%),
    linear-gradient(160deg, #0b5f57 0%, #0c7263 60%, #0a5a50 100%);
  box-shadow: inset -14px 0 24px rgba(0, 0, 0, 0.22);
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 9px;
  color: rgba(236, 254, 250, 0.9);
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.22em;
}

/* Trang đáy phải — nội dung "cuối cùng" lộ ra khi lật xong */
.page-base {
  position: absolute;
  right: 0;
  top: 0;
  width: 50%;
  height: 100%;
  border-radius: 0 10px 10px 0;
  background: #fdfefc;
  display: grid;
  place-items: center;
}

.page-base .wm {
  color: rgba(13, 148, 136, 0.3);
}

/* ===== Tờ hồ sơ lật ===== */
.leaf {
  position: absolute;
  left: 50%;
  top: 0;
  width: 50%;
  height: 100%;
  transform-origin: left center;
  transform-style: preserve-3d;
  animation: leaf-flip var(--flip-ms, 680ms) cubic-bezier(0.34, 0.06, 0.22, 1) var(--delay-ms, 0ms) 1 forwards;
  will-change: transform;
}

.face {
  position: absolute;
  inset: 0;
  backface-visibility: hidden;
  display: flex;
  flex-direction: column;
  gap: 11px;
  padding: 18px 16px;
  box-sizing: border-box;
  overflow: hidden;
}

.leaf-front {
  border-radius: 0 8px 8px 0;
  background: #ffffff;
  box-shadow: 2px 6px 18px rgba(2, 24, 22, 0.25);
  justify-content: flex-start;
}

/* Bóng gáy: mép gần cột sách tối nhẹ cho cảm giác thật */
.leaf-front::after {
  content: '';
  position: absolute;
  inset: 0;
  background: linear-gradient(90deg, rgba(15, 23, 42, 0.1) 0%, transparent 18%);
  pointer-events: none;
}

.leaf-back {
  border-radius: 8px 0 0 8px;
  background: #f6f9f7;
  transform: rotateY(180deg);
  box-shadow: -2px 6px 16px rgba(2, 24, 22, 0.2);
}

.leaf-back::after {
  content: '';
  position: absolute;
  inset: 0;
  background: linear-gradient(270deg, rgba(15, 23, 42, 0.08) 0%, transparent 18%);
  pointer-events: none;
}

.doc-tag {
  font-size: 9px;
  font-weight: 800;
  letter-spacing: 0.18em;
  color: #0d9488;
  border: 1px solid rgba(13, 148, 136, 0.35);
  border-radius: 5px;
  padding: 3px 7px;
  align-self: flex-start;
  margin-bottom: 4px;
}

.line {
  height: 7px;
  border-radius: 4px;
  background: #e2e8ee;
}

.w55 { width: 55%; }
.w60 { width: 60%; }
.w62 { width: 62%; }
.w68 { width: 68%; }
.w70 { width: 70%; }
.w75 { width: 75%; }
.w76 { width: 76%; }
.w80 { width: 80%; }
.w82 { width: 82%; }
.w88 { width: 88%; }
.w90 { width: 90%; }
.w92 { width: 92%; }
.w95 { width: 95%; }

.stamp {
  width: 42px;
  height: 42px;
  border-radius: 999px;
  border: 2.5px dashed rgba(13, 148, 136, 0.45);
  align-self: center;
  margin: 6px 0 8px;
}

/* Đường lật: nhấc trang (translateZ) rồi đặt xuống, kèm quản lý z-index
   để trang đang bay luôn trên đống trang đã nằm */
@keyframes leaf-flip {
  0% {
    transform: rotateY(0deg) translateZ(0);
    z-index: 45;
  }
  50% {
    transform: rotateY(-92deg) translateZ(20px);
    z-index: 60;
  }
  100% {
    transform: rotateY(-180deg) translateZ(0);
    z-index: 5;
  }
}

/* Bóng dưới sách */
.floor {
  width: 200px;
  height: 18px;
  margin-top: 14px;
  border-radius: 999px;
  background: radial-gradient(closest-side, rgba(0, 0, 0, 0.4), transparent);
  filter: blur(3px);
}

/* ===== Chữ ===== */
h1 {
  margin: 26px 0 10px;
  font-size: 21px;
  font-weight: 800;
  letter-spacing: 0.12em;
}

.step {
  margin: 0;
  font-size: 13px;
  color: rgba(234, 255, 249, 0.75);
  min-height: 20px;
}

.step-enter-active,
.step-leave-active {
  transition: opacity 0.28s ease, transform 0.28s ease;
}

.step-enter-from {
  opacity: 0;
  transform: translateY(7px);
}

.step-leave-to {
  opacity: 0;
  transform: translateY(-7px);
}
</style>
