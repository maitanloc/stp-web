<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  Clock,
  Dices,
  Hand,
  Moon,
  Music,
  Power,
  Sparkles,
  Sun,
  Utensils,
  Volume2,
  VolumeX,
  Wind,
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
  activeSpeaker,
  isLightsDimmed,
  fanSpeed,
  catPetCount,
  catPurrText,
  mosquitoCaught,
  isMosquitoAlive,
  lastSlapPos,
  stampCount,
  waterBubbleClicks,
  currentDream,
  selectedDish,
  isRollingDish,
  activeAmbientSound,
  togglePreview,
  enableDevBypass,
  pokeStickman,
  pokeWorker,
  toggleLights,
  cycleFan,
  petCat,
  clickLizard,
  clickWaterDispenser,
  rustlePlant,
  nextDream,
  slapMosquito,
  rollDish,
  setAmbientSound,
  stopAllAmbient,
} = useLunchBreak()

const isDevPinModalOpen = ref(false)
const devPinInput = ref('')
const pinError = ref(false)
const isPokedEffect = ref(false)

type ActiveTab = 'food' | 'asmr' | 'mini-game'
const activeTab = ref<ActiveTab>('mini-game')

const formattedCurrentTime = computed(() => {
  return now.value.toLocaleTimeString('vi-VN', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
})

const fanSpeedLabel = computed(() => {
  if (fanSpeed.value === 0) return 'Tắt quạt'
  if (fanSpeed.value === 2) return 'Gió mạnh'
  return 'Gió êm'
})

function handleGeneralPoke() {
  isPokedEffect.value = true
  pokeStickman()
  window.setTimeout(() => {
    isPokedEffect.value = false
  }, 600)
}

function handleWorkerPoke(character: 'A' | 'B' | 'C' | 'D') {
  isPokedEffect.value = true
  pokeWorker(character)
  window.setTimeout(() => {
    isPokedEffect.value = false
  }, 600)
}

function handleSlap(pos: { x: number; y: number }) {
  slapMosquito(pos.x, pos.y)
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
  <div class="lunch-screen-root" :class="{ 'is-night-mode': isLightsDimmed }">
    <!-- Header thanh trạng thái -->
    <header class="lunch-screen-header">
      <div class="lunch-logo-brand">
        <span class="brand-pill">STP ARCHIVE</span>
        <span class="brand-mode">GIỜ NGHỈ TRƯA VĂN PHÒNG & BẢO TRÌ</span>
      </div>

      <div class="lunch-header-right">
        <!-- Nút tắt / bật đèn -->
        <button
          class="btn btn-secondary btn-sm header-tool-btn"
          :class="{ 'is-active': isLightsDimmed }"
          type="button"
          :title="isLightsDimmed ? 'Bật đèn văn phòng' : 'Tắt đèn cho dễ ngủ'"
          @click="toggleLights"
        >
          <component :is="isLightsDimmed ? Sun : Moon" :size="14" />
          <span>{{ isLightsDimmed ? 'Bật đèn' : 'Tắt đèn' }}</span>
        </button>

        <!-- Nút quạt máy -->
        <button
          class="btn btn-secondary btn-sm header-tool-btn"
          :class="{ 'is-active': fanSpeed > 0 }"
          type="button"
          :title="`Quạt máy: ${fanSpeedLabel} (Bấm để đổi số)`"
          @click="cycleFan"
        >
          <Wind :size="14" />
          <span>Quạt: {{ fanSpeedLabel }}</span>
        </button>

        <!-- Nút âm thanh ASMR nhanh -->
        <button
          class="btn btn-secondary btn-sm header-tool-btn"
          :class="{ 'is-active': activeAmbientSound !== 'none' }"
          type="button"
          title="Bật/tắt âm thanh ru ngủ"
          @click="activeAmbientSound === 'none' ? setAmbientSound('rain') : stopAllAmbient()"
        >
          <component :is="activeAmbientSound !== 'none' ? Volume2 : VolumeX" :size="14" />
          <span>{{ activeAmbientSound !== 'none' ? 'ASMR đang bật' : 'Bật ASMR' }}</span>
        </button>

        <!-- Nếu đang Preview thì có nút thoát -->
        <button
          v-if="isPreview"
          class="btn btn-warning btn-sm"
          type="button"
          @click="togglePreview"
        >
          <Power :size="14" />
          <span>Thoát Preview</span>
        </button>

        <!-- Nút Dành cho Dev -->
        <button
          class="btn btn-ghost btn-sm dev-door-btn"
          type="button"
          title="Dành cho lập trình viên phát triển hệ thống"
          @click="isDevPinModalOpen = true"
        >
          <Wrench :size="14" />
          <span>Dev Bypass</span>
        </button>
      </div>
    </header>

    <!-- Nội dung chính -->
    <main class="lunch-screen-main">
      <div class="lunch-title-group">
        <div class="lunch-clock-badge">
          <Clock :size="15" />
          <span>BÂY GIỜ LÀ: <strong>{{ formattedCurrentTime }}</strong></span>
        </div>

        <h1 class="lunch-main-heading">
          😴 VĂN PHÒNG ĐANG NGHỈ TRƯA (12:00 – 13:00)
        </h1>
        <p class="lunch-sub-heading">
          Góc lưu trữ sổ sách hộ tịch và máy chủ tạm "ngủ đông" để đội ngũ kỹ thuật nâng cấp hệ thống.
          Mời các đồng chí nghỉ ngơi, đúng <strong>13:00:00</strong> hệ thống sẽ tự động mở lại!
        </p>
      </div>

      <!-- Đồng hồ đếm ngược điện tử -->
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

      <!-- Toàn cảnh phòng làm việc Stickman sống động -->
      <div class="stickman-wrapper">
        <StickmanSleepers
          :poked="isPokedEffect"
          :quote="pokeQuote"
          :active-speaker="activeSpeaker"
          :is-lights-dimmed="isLightsDimmed"
          :fan-speed="fanSpeed"
          :cat-pet-count="catPetCount"
          :cat-purr-text="catPurrText"
          :current-dream="currentDream"
          :is-mosquito-alive="isMosquitoAlive"
          :last-slap-pos="lastSlapPos"
          :stamp-count="stampCount"
          :water-bubble-clicks="waterBubbleClicks"
          @poke="handleGeneralPoke"
          @poke-worker="handleWorkerPoke"
          @pet-cat="petCat"
          @click-lizard="clickLizard"
          @click-water-dispenser="clickWaterDispenser"
          @rustle-plant="rustlePlant"
          @cycle-fan="cycleFan"
          @toggle-lights="toggleLights"
          @next-dream="nextDream"
          @slap-mosquito="handleSlap"
        />
      </div>

      <!-- ==================== KHU VỰC TIỆN ÍCH TRƯA NAY ==================== -->
      <section class="lunch-feature-deck">
        <!-- Thanh Tab chuyển đổi -->
        <div class="feature-tabs-nav">
          <button
            class="tab-btn"
            :class="{ 'is-active': activeTab === 'mini-game' }"
            type="button"
            @click="activeTab = 'mini-game'"
          >
            <Sparkles :size="16" />
            <span>👥 4 Nhân Viên & Tiểu Cảnh</span>
            <span v-if="pokeCount > 0" class="badge-count">{{ pokeCount }}</span>
          </button>

          <button
            class="tab-btn"
            :class="{ 'is-active': activeTab === 'food' }"
            type="button"
            @click="activeTab = 'food'"
          >
            <Utensils :size="16" />
            <span>🍱 Trưa Nay Ăn Gì?</span>
          </button>

          <button
            class="tab-btn"
            :class="{ 'is-active': activeTab === 'asmr' }"
            type="button"
            @click="activeTab = 'asmr'"
          >
            <Music :size="16" />
            <span>🎧 Góc ASMR Ru Ngủ</span>
            <span v-if="activeAmbientSound !== 'none'" class="active-dot"></span>
          </button>
        </div>

        <!-- TAB 1: 4 NHÂN VIÊN & TIỂU CẢNH VĂN PHÒNG -->
        <div v-if="activeTab === 'mini-game'" class="tab-panel game-panel">
          <div class="game-header">
            <h3 class="panel-title">Hệ Sinh Thái Nhân Viên Công Sở Buổi Trưa 🏛️</h3>
            <p class="panel-desc">
              Bấm vào từng nhân vật hoặc tiểu cảnh để kích hoạt các tình huống hài hước:
            </p>
          </div>

          <div class="characters-grid">
            <!-- Chuyên viên C: Sổ sách 1999 -->
            <div class="char-card">
              <div class="char-header">
                <span class="char-icon">📑</span>
                <div>
                  <h4 class="char-name">Chuyên viên C (Sổ Sách)</h4>
                  <span class="char-desc">Chìm trong đống hồ sơ 1999, kính cận trễ mũi</span>
                </div>
              </div>
              <button class="btn btn-secondary btn-sm w-full" type="button" @click="handleWorkerPoke('C')">
                <span>🔴 Đóng dấu cộp cộp ({{ stampCount }})</span>
              </button>
            </div>

            <!-- Chuyên viên D: Ngủ gầm bàn -->
            <div class="char-card">
              <div class="char-header">
                <span class="char-icon">⛺</span>
                <div>
                  <h4 class="char-name">Chuyên viên D (Gầm Bàn)</h4>
                  <span class="char-desc">Trùm kén áo gió, tất cọc cạch sọc xanh chấm bi</span>
                </div>
              </div>
              <button class="btn btn-secondary btn-sm w-full" type="button" @click="handleWorkerPoke('D')">
                <span>🧦 Xem ngón chân ngúng nguẩy</span>
              </button>
            </div>

            <!-- Chuyên viên A: Chăm chỉ gục bàn -->
            <div class="char-card">
              <div class="char-header">
                <span class="char-icon">💻</span>
                <div>
                  <h4 class="char-name">Chuyên viên A (Chăm Chỉ)</h4>
                  <span class="char-desc">Gục đầu bên laptop STP, bong bóng mũi phồng to</span>
                </div>
              </div>
              <button class="btn btn-secondary btn-sm w-full" type="button" @click="handleWorkerPoke('A')">
                <Hand :size="14" />
                <span>Chọt snot-bubble nổ</span>
              </button>
            </div>

            <!-- Chuyên viên B: Ngủ lăn lóc sofa -->
            <div class="char-card">
              <div class="char-header">
                <span class="char-icon">🩴</span>
                <div>
                  <h4 class="char-name">Chuyên viên B (Lăn Lóc)</h4>
                  <span class="char-desc">Chân gác thành ghế, dép tổ ong rơi, chảy nước miếng</span>
                </div>
              </div>
              <button class="btn btn-secondary btn-sm w-full" type="button" @click="handleWorkerPoke('B')">
                <span>💤 Chọt chân gác trời</span>
              </button>
            </div>
          </div>

          <!-- Các tiểu cảnh phụ -->
          <div class="scenery-quick-bar">
            <button class="scenery-btn" type="button" @click="petCat">
              <span class="scenery-icon">🐱</span>
              <span>Xoa cằm Boss Mèo ({{ catPetCount }}❤️)</span>
            </button>
            <button class="scenery-btn" type="button" @click="clickWaterDispenser">
              <span class="scenery-icon">🚰</span>
              <span>Bình nước sủi bọt ({{ waterBubbleClicks }}🫧)</span>
            </button>
            <button class="scenery-btn" type="button" @click="clickLizard">
              <span class="scenery-icon">🦎</span>
              <span>Thạch sùng tặc lưỡi</span>
            </button>
            <button class="scenery-btn" type="button" @click="rustlePlant">
              <span class="scenery-icon">🌿</span>
              <span>Cây kim tiền may mắn</span>
            </button>
            <button class="scenery-btn" type="button" @click="nextDream">
              <span class="scenery-icon">{{ currentDream.icon }}</span>
              <span>Đổi giấc mơ trưa</span>
            </button>
            <button class="scenery-btn" type="button" :disabled="!isMosquitoAlive" @click="slapMosquito(620, 220)">
              <span class="scenery-icon">🦟</span>
              <span>Đập muỗi ({{ mosquitoCaught }}💥)</span>
            </button>
          </div>
        </div>

        <!-- TAB 2: CỖ MÁY ẨM THỰC "HÔM NAY ĂN GÌ?" -->
        <div v-if="activeTab === 'food'" class="tab-panel food-panel">
          <div class="food-intro">
            <div>
              <h3 class="panel-title">Cỗ máy ẩm thực công sở STP 🍜</h3>
              <p class="panel-desc">
                12h trưa không biết ăn gì? Bấm nút để vũ trụ quyết định món ăn trưa nay của bạn!
              </p>
            </div>
            <button
              class="btn btn-primary btn-md roll-dish-btn"
              :disabled="isRollingDish"
              type="button"
              @click="rollDish"
            >
              <Dices :size="18" :class="{ 'is-spinning': isRollingDish }" />
              <span>{{ isRollingDish ? 'Đang chọn món ngon…' : '🎲 Bấm Chọn Món Trưa!' }}</span>
            </button>
          </div>

          <div v-if="selectedDish" class="dish-card-result">
            <div class="dish-emoji">{{ selectedDish.emoji }}</div>
            <div class="dish-info">
              <div class="dish-header-line">
                <h4 class="dish-name">{{ selectedDish.name }}</h4>
                <span class="dish-tag">{{ selectedDish.tag }}</span>
              </div>
              <p class="dish-desc">{{ selectedDish.desc }}</p>
            </div>
          </div>
          <div v-else class="dish-empty-state">
            <span class="empty-icon">🍱</span>
            <span>Bấm nút <strong>"Bấm Chọn Món Trưa!"</strong> để quay thực đơn hôm nay nào!</span>
          </div>
        </div>

        <!-- TAB 3: GÓC ASMR RU NGỦ -->
        <div v-if="activeTab === 'asmr'" class="tab-panel asmr-panel">
          <div class="asmr-header">
            <h3 class="panel-title">Âm Thanh Trắng (ASMR) Thư Giãn Buổi Trưa 🌿</h3>
            <p class="panel-desc">
              Tổng hợp trực tiếp qua Web Audio API, không cần tải nhạc ngoài. Giúp bạn chợp mắt 20 phút cực êm!
            </p>
          </div>

          <div class="asmr-presets-grid">
            <button
              class="asmr-card"
              :class="{ 'is-playing': activeAmbientSound === 'lullaby' }"
              type="button"
              @click="setAmbientSound('lullaby')"
            >
              <div class="asmr-card-icon">🎵</div>
              <div class="asmr-card-info">
                <strong>Hộp Nhạc Ru Ngủ</strong>
                <span>Giai điệu chuông gió êm đềm</span>
              </div>
              <span class="asmr-status-badge">{{ activeAmbientSound === 'lullaby' ? 'Đang phát' : 'Phát' }}</span>
            </button>

            <button
              class="asmr-card"
              :class="{ 'is-playing': activeAmbientSound === 'rain' }"
              type="button"
              @click="setAmbientSound('rain')"
            >
              <div class="asmr-card-icon">🌧️</div>
              <div class="asmr-card-info">
                <strong>Mưa Rơi Rả Rích</strong>
                <span>Tiếng mưa rơi mát rượi ngoài hiên</span>
              </div>
              <span class="asmr-status-badge">{{ activeAmbientSound === 'rain' ? 'Đang phát' : 'Phát' }}</span>
            </button>

            <button
              class="asmr-card"
              :class="{ 'is-playing': activeAmbientSound === 'fan' }"
              type="button"
              @click="setAmbientSound('fan')"
            >
              <div class="asmr-card-icon">💨</div>
              <div class="asmr-card-info">
                <strong>Quạt Gió Vù Vù</strong>
                <span>Tiếng gió quạt ngủ say sưa</span>
              </div>
              <span class="asmr-status-badge">{{ activeAmbientSound === 'fan' ? 'Đang phát' : 'Phát' }}</span>
            </button>

            <button
              class="asmr-card"
              :class="{ 'is-playing': activeAmbientSound === 'none' }"
              type="button"
              @click="stopAllAmbient"
            >
              <div class="asmr-card-icon">🔇</div>
              <div class="asmr-card-info">
                <strong>Yên Lặng Tuyệt Đối</strong>
                <span>Không phát âm thanh nào</span>
              </div>
              <span class="asmr-status-badge">{{ activeAmbientSound === 'none' ? 'Đang chọn' : 'Tắt' }}</span>
            </button>
          </div>
        </div>
      </section>
    </main>

    <!-- Modal PIN dành cho Dev -->
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
  transition: background 0.5s ease;
}

.lunch-screen-root.is-night-mode {
  background: radial-gradient(circle at 50% 15%, #0f172a 0%, #050814 60%, #020308 100%);
}

.lunch-screen-header {
  height: 64px;
  padding: 0 24px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(15, 23, 42, 0.65);
  backdrop-filter: blur(12px);
  position: sticky;
  top: 0;
  z-index: 100;
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
  gap: 8px;
}

.header-tool-btn {
  background: rgba(255, 255, 255, 0.08);
  color: #cbd5e1;
  border: 1px solid rgba(255, 255, 255, 0.12);
  transition: all 0.2s ease;
}

.header-tool-btn:hover {
  background: rgba(255, 255, 255, 0.15);
  color: #ffffff;
}

.header-tool-btn.is-active {
  background: rgba(56, 189, 248, 0.2);
  border-color: #38bdf8;
  color: #7dd3fc;
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
  justify-content: flex-start;
  padding: 24px 20px 48px;
  gap: 20px;
  max-width: 1060px;
  margin: 0 auto;
  width: 100%;
}

.lunch-title-group {
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
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
  font-size: 13.5px;
  color: #94a3b8;
  margin: 0;
  max-width: 680px;
  line-height: 1.6;
}

.lunch-timer-card {
  background: rgba(30, 41, 59, 0.7);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 18px;
  padding: 10px 24px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
  box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.3);
}

.timer-label {
  font-size: 10.5px;
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
  font-size: 30px;
  font-weight: 900;
  color: #ffffff;
  background: #0f172a;
  padding: 3px 10px;
  border-radius: 8px;
  border: 1px solid #334155;
  min-width: 55px;
  text-align: center;
  box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.5);
}

.timer-text {
  font-size: 9px;
  font-weight: 700;
  color: #64748b;
  margin-top: 3px;
}

.timer-sep {
  font-size: 26px;
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

/* Feature Deck */
.lunch-feature-deck {
  width: 100%;
  max-width: 1040px;
  background: rgba(15, 23, 42, 0.7);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 20px;
  overflow: hidden;
  box-shadow: 0 20px 40px -15px rgba(0, 0, 0, 0.4);
}

.feature-tabs-nav {
  display: flex;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  background: rgba(15, 23, 42, 0.9);
}

.tab-btn {
  flex: 1;
  padding: 13px 16px;
  background: transparent;
  border: none;
  color: #94a3b8;
  font-size: 13px;
  font-weight: 600;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  cursor: pointer;
  transition: all 0.2s ease;
  position: relative;
}

.tab-btn:hover {
  color: #f8fafc;
  background: rgba(255, 255, 255, 0.04);
}

.tab-btn.is-active {
  color: #38bdf8;
  background: rgba(56, 189, 248, 0.08);
}

.tab-btn.is-active::after {
  content: '';
  position: absolute;
  bottom: 0;
  left: 0;
  right: 0;
  height: 2px;
  background: #38bdf8;
}

.active-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #22c55e;
  animation: pulse-dot 1.5s infinite;
}

@keyframes pulse-dot {
  0%, 100% { opacity: 1; transform: scale(1); }
  50% { opacity: 0.4; transform: scale(1.3); }
}

.badge-count {
  font-size: 10px;
  background: #ef4444;
  color: white;
  padding: 2px 6px;
  border-radius: 999px;
  font-weight: bold;
}

.tab-panel {
  padding: 18px 22px;
}

.panel-title {
  margin: 0 0 4px;
  font-size: 15px;
  font-weight: 700;
  color: #ffffff;
}

.panel-desc {
  margin: 0;
  font-size: 12.5px;
  color: #94a3b8;
}

/* 4 Nhân viên Grid */
.characters-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px;
  margin-top: 14px;
}

.char-card {
  background: rgba(30, 41, 59, 0.55);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 14px;
  padding: 12px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  gap: 10px;
}

.char-header {
  display: flex;
  align-items: flex-start;
  gap: 10px;
}

.char-icon {
  font-size: 26px;
  line-height: 1;
}

.char-name {
  margin: 0 0 2px 0;
  font-size: 13px;
  font-weight: 700;
  color: #ffffff;
}

.char-desc {
  font-size: 11px;
  color: #94a3b8;
  line-height: 1.4;
  display: block;
}

.scenery-quick-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 14px;
  flex-wrap: wrap;
  border-top: 1px solid rgba(255, 255, 255, 0.06);
  padding-top: 12px;
}

.scenery-btn {
  background: rgba(30, 41, 59, 0.5);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 999px;
  padding: 6px 12px;
  color: #cbd5e1;
  font-size: 11.5px;
  font-weight: 600;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.scenery-btn:hover {
  background: rgba(56, 189, 248, 0.15);
  border-color: #38bdf8;
  color: #ffffff;
  transform: translateY(-1px);
}

.scenery-icon {
  font-size: 14px;
}

/* Food Panel */
.food-intro {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.roll-dish-btn {
  background: linear-gradient(135deg, #f59e0b, #d97706);
  border: none;
  font-weight: 700;
  color: #ffffff;
  box-shadow: 0 4px 15px rgba(245, 158, 11, 0.35);
}

.roll-dish-btn:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 6px 20px rgba(245, 158, 11, 0.5);
}

.is-spinning {
  animation: spin 0.4s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.dish-card-result {
  display: flex;
  align-items: center;
  gap: 18px;
  background: rgba(30, 41, 59, 0.6);
  border: 1px solid rgba(245, 158, 11, 0.3);
  border-radius: 14px;
  padding: 16px 20px;
  box-shadow: 0 8px 25px rgba(0, 0, 0, 0.25);
  animation: dish-pop 0.3s cubic-bezier(0.16, 1, 0.3, 1);
}

@keyframes dish-pop {
  0% { transform: scale(0.95); opacity: 0; }
  100% { transform: scale(1); opacity: 1; }
}

.dish-emoji {
  font-size: 42px;
  line-height: 1;
}

.dish-info {
  flex: 1;
}

.dish-header-line {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 4px;
}

.dish-name {
  margin: 0;
  font-size: 16px;
  font-weight: 700;
  color: #fef08a;
}

.dish-tag {
  font-size: 10px;
  background: rgba(245, 158, 11, 0.2);
  color: #fbbf24;
  border: 1px solid rgba(245, 158, 11, 0.4);
  padding: 2px 8px;
  border-radius: 999px;
  font-weight: 600;
}

.dish-desc {
  margin: 0;
  font-size: 13px;
  color: #cbd5e1;
}

.dish-empty-state {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  padding: 24px;
  background: rgba(30, 41, 59, 0.4);
  border: 1px dashed rgba(255, 255, 255, 0.12);
  border-radius: 12px;
  color: #94a3b8;
  font-size: 13px;
}

.empty-icon {
  font-size: 20px;
}

/* ASMR Panel */
.asmr-presets-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 12px;
  margin-top: 14px;
}

.asmr-card {
  display: flex;
  align-items: center;
  gap: 12px;
  background: rgba(30, 41, 59, 0.5);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 12px;
  padding: 12px 14px;
  cursor: pointer;
  text-align: left;
  transition: all 0.2s ease;
  color: inherit;
}

.asmr-card:hover {
  background: rgba(30, 41, 59, 0.85);
  border-color: rgba(255, 255, 255, 0.18);
  transform: translateY(-2px);
}

.asmr-card.is-playing {
  background: rgba(99, 102, 241, 0.2);
  border-color: #6366f1;
}

.asmr-card-icon {
  font-size: 26px;
  line-height: 1;
}

.asmr-card-info {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.asmr-card-info strong {
  font-size: 13px;
  color: #ffffff;
}

.asmr-card-info span {
  font-size: 11px;
  color: #94a3b8;
}

.asmr-status-badge {
  font-size: 10px;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.08);
  color: #94a3b8;
}

.asmr-card.is-playing .asmr-status-badge {
  background: #6366f1;
  color: #ffffff;
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
