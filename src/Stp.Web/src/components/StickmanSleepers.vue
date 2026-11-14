<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    poked?: boolean
    quote?: string
  }>(),
  {
    poked: false,
    quote: '',
  },
)

const emit = defineEmits<{
  (e: 'poke'): void
}>()
</script>

<template>
  <div class="stickman-stage" @click="emit('poke')">
    <svg
      viewBox="0 0 900 480"
      class="stickman-svg"
      xmlns="http://www.w3.org/2000/svg"
      role="img"
      aria-label="Hình vẽ người que nhân viên đang ngủ trưa"
    >
      <defs>
        <!-- Gradients -->
        <linearGradient id="wallGrad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#1e293b" />
          <stop offset="100%" stop-color="#0f172a" />
        </linearGradient>

        <linearGradient id="floorGrad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#334155" />
          <stop offset="100%" stop-color="#1e293b" />
        </linearGradient>

        <linearGradient id="deskGrad" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stop-color="#d97706" />
          <stop offset="100%" stop-color="#b45309" />
        </linearGradient>

        <linearGradient id="sofaGrad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#4f46e5" />
          <stop offset="100%" stop-color="#3730a3" />
        </linearGradient>

        <linearGradient id="screenGrad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#38bdf8" stop-opacity="0.6" />
          <stop offset="100%" stop-color="#0284c7" stop-opacity="0.2" />
        </linearGradient>

        <!-- Filter Glow -->
        <filter id="glow" x="-20%" y="-20%" width="140%" height="140%">
          <feGaussianBlur stdDeviation="3" result="blur" />
          <feComposite in="SourceGraphic" in2="blur" operator="over" />
        </filter>
      </defs>

      <!-- Sàn nhà & tường văn phòng -->
      <rect x="0" y="0" width="900" height="350" fill="url(#wallGrad)" />
      <rect x="0" y="350" width="900" height="130" fill="url(#floorGrad)" />
      <line x1="0" y1="350" x2="900" y2="350" stroke="#475569" stroke-width="2" />

      <!-- Cửa sổ & Rèm khép hờ -->
      <rect x="380" y="40" width="140" height="110" rx="8" fill="#090d16" stroke="#475569" stroke-width="3" />
      <path d="M450 40 L450 150 M380 95 L520 95" stroke="#334155" stroke-width="2" />
      <!-- Trăng lưỡi liềm & sao trưa -->
      <circle cx="485" cy="70" r="14" fill="#fbbf24" opacity="0.8" filter="url(#glow)" />
      <circle cx="490" cy="67" r="13" fill="#090d16" />
      <circle cx="410" cy="65" r="1.5" fill="#f8fafc" opacity="0.6" />
      <circle cx="430" cy="80" r="1.5" fill="#f8fafc" opacity="0.6" />
      <circle cx="470" cy="120" r="1.5" fill="#f8fafc" opacity="0.4" />
      <!-- Rèm cửa hai bên -->
      <path d="M380 40 Q400 90 388 150 L380 150 Z" fill="#64748b" opacity="0.7" />
      <path d="M520 40 Q500 90 512 150 L520 150 Z" fill="#64748b" opacity="0.7" />

      <!-- Giấy ghi chú trên tường -->
      <g transform="translate(680, 50) rotate(4)">
        <rect x="0" y="0" width="90" height="75" rx="3" fill="#fef08a" />
        <text x="8" y="20" font-size="10" font-weight="bold" fill="#854d0e">LƯU Ý 12H:</text>
        <text x="8" y="36" font-size="9" fill="#a16207">✓ Cơm sườn 🍱</text>
        <text x="8" y="50" font-size="9" fill="#a16207">✓ Chợp mắt 💤</text>
        <text x="8" y="64" font-size="9" fill="#a16207">✗ CẤM GÕ CODE</text>
      </g>

      <g transform="translate(130, 45) rotate(-3)">
        <rect x="0" y="0" width="95" height="75" rx="3" fill="#fed7aa" />
        <text x="8" y="20" font-size="10" font-weight="bold" fill="#9a3412">DEV PHÒNG THỦ:</text>
        <text x="8" y="36" font-size="9" fill="#c2410c">Đang vá server 🛠️</text>
        <text x="8" y="50" font-size="9" fill="#c2410c">Đúng 13:00</text>
        <text x="8" y="64" font-size="9" fill="#c2410c">Mở lại quẩy tiếp!</text>
      </g>

      <!-- Đồng hồ treo tường -->
      <g transform="translate(290, 80)">
        <circle cx="0" cy="0" r="32" fill="#1e293b" stroke="#e2e8f0" stroke-width="3" />
        <circle cx="0" cy="0" r="28" fill="#0f172a" />
        <!-- Số đồng hồ -->
        <text x="-4" y="-18" font-size="9" font-weight="bold" fill="#94a3b8">12</text>
        <text x="18" y="3" font-size="9" font-weight="bold" fill="#94a3b8">3</text>
        <text x="-3" y="23" font-size="9" font-weight="bold" fill="#94a3b8">6</text>
        <text x="-24" y="3" font-size="9" font-weight="bold" fill="#94a3b8">9</text>
        <!-- Kim giờ chỉ số 12 -->
        <line x1="0" y1="0" x2="0" y2="-16" stroke="#f8fafc" stroke-width="2.5" stroke-linecap="round" />
        <!-- Kim phút nhích nhẹ -->
        <line x1="0" y1="0" x2="8" y2="-18" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" class="clock-minute-hand" />
        <circle cx="0" cy="0" r="3" fill="#f8fafc" />
      </g>

      <!-- ==================== NHÂN VIÊN 1: CHĂM CHỈ GỤC BÀN ==================== -->
      <g id="stickman-desk" class="stickman-worker" :class="{ 'is-startled': poked }">
        <!-- Bàn làm việc -->
        <g id="desk-furniture">
          <!-- Chân bàn -->
          <rect x="75" y="320" width="10" height="90" rx="3" fill="#94a3b8" />
          <rect x="250" y="320" width="10" height="90" rx="3" fill="#94a3b8" />
          <!-- Mặt bàn -->
          <rect x="60" y="305" width="220" height="18" rx="4" fill="url(#deskGrad)" stroke="#78350f" stroke-width="1.5" />
          
          <!-- Laptop -->
          <path d="M90 305 L135 305 L138 275 L93 275 Z" fill="#475569" />
          <rect x="95" y="277" width="40" height="25" rx="2" fill="url(#screenGrad)" />
          <line x1="100" y1="285" x2="125" y2="285" stroke="#bae6fd" stroke-width="1" opacity="0.6" />
          <line x1="100" y1="290" x2="118" y2="290" stroke="#bae6fd" stroke-width="1" opacity="0.6" />
          <path d="M85 305 L140 305 L143 308 L82 308 Z" fill="#64748b" />
          
          <!-- Cốc cà phê hết nhẵn -->
          <rect x="155" y="290" width="14" height="16" rx="2" fill="#f1f5f9" />
          <path d="M169 294 Q175 298 169 302" stroke="#f1f5f9" stroke-width="2" fill="none" />
          <text x="157" y="299" font-size="6" font-weight="bold" fill="#64748b">STP</text>

          <!-- Ghế xoay văn phòng -->
          <path d="M205 270 Q190 270 190 330 L235 330 Q235 270 205 270" fill="#334155" opacity="0.8" />
          <rect x="208" y="330" width="8" height="60" fill="#64748b" />
          <!-- Chân ghế xoay -->
          <line x1="212" y1="390" x2="185" y2="405" stroke="#475569" stroke-width="4" stroke-linecap="round" />
          <line x1="212" y1="390" x2="240" y2="405" stroke="#475569" stroke-width="4" stroke-linecap="round" />
        </g>

        <!-- Nhân viên người que 1 (Gục trên bàn) -->
        <g class="desk-sleeper-body">
          <!-- Chân dưới gầm bàn -->
          <line x1="210" y1="330" x2="200" y2="385" stroke="#f8fafc" stroke-width="5" stroke-linecap="round" />
          <line x1="200" y1="385" x2="215" y2="410" stroke="#f8fafc" stroke-width="4.5" stroke-linecap="round" />
          
          <!-- Lưng gập về trước (thở phập phồng) -->
          <path d="M210 330 Q195 285 180 295" stroke="#f8fafc" stroke-width="6" stroke-linecap="round" fill="none" class="breathe-spine" />

          <!-- Tay gối đầu trên bàn -->
          <path d="M185 292 Q175 305 160 305 L200 305" stroke="#f8fafc" stroke-width="4.5" stroke-linecap="round" fill="none" />

          <!-- Đầu người que (gục nghiêng trên cánh tay) -->
          <circle cx="180" cy="278" r="16" fill="#f8fafc" stroke="#0f172a" stroke-width="2" class="desk-head" />
          
          <!-- Mắt nhắm tít vui vẻ (đang mơ đẹp) -->
          <path d="M172 277 Q176 281 180 277" stroke="#0f172a" stroke-width="2" fill="none" stroke-linecap="round" />
          <!-- Miệng mỉm cười nhẹ khi ngủ -->
          <path d="M174 285 Q178 288 183 285" stroke="#0f172a" stroke-width="1.8" fill="none" stroke-linecap="round" />

          <!-- Bong bóng ngủ phập phồng (Snot bubble) -->
          <circle cx="171" cy="281" r="5" fill="#38bdf8" opacity="0.6" stroke="#0284c7" stroke-width="1" class="snot-bubble" />

          <!-- Bong bóng bay chữ Zzz... -->
          <g class="desk-zzz-group">
            <text x="160" y="255" font-size="14" font-weight="bold" fill="#38bdf8" class="zzz-item zzz-1">z</text>
            <text x="150" y="235" font-size="18" font-weight="bold" fill="#60a5fa" class="zzz-item zzz-2">Z</text>
            <text x="135" y="210" font-size="24" font-weight="bold" fill="#818cf8" class="zzz-item zzz-3">Zzz</text>
          </g>

          <!-- Bảng tên nhân viên -->
          <g transform="translate(195, 235)">
            <rect x="0" y="0" width="75" height="22" rx="4" fill="#1e293b" stroke="#38bdf8" stroke-width="1.2" />
            <text x="6" y="14" font-size="9" font-weight="bold" fill="#38bdf8">Chuyên viên A</text>
          </g>
        </g>
      </g>


      <!-- ==================== NHÂN VIÊN 2: NẰM NGỦ LĂN LÓC ==================== -->
      <g id="stickman-sofa" class="stickman-worker" :class="{ 'is-startled': poked }">
        <!-- Chiếc ghế sofa dài / băng nghỉ -->
        <g id="sofa-furniture">
          <!-- Đệm tựa lưng sofa -->
          <rect x="530" y="260" width="310" height="60" rx="10" fill="url(#sofaGrad)" stroke="#312e81" stroke-width="2" />
          <!-- Tay vịn bên trái -->
          <rect x="515" y="270" width="28" height="65" rx="8" fill="#4338ca" stroke="#312e81" stroke-width="2" />
          <!-- Đệm ngồi sofa -->
          <rect x="525" y="305" width="320" height="35" rx="8" fill="#4f46e5" stroke="#312e81" stroke-width="2" />
          <!-- Tay vịn bên phải -->
          <rect x="830" y="270" width="28" height="65" rx="8" fill="#4338ca" stroke="#312e81" stroke-width="2" />
          <!-- Chân sofa -->
          <rect x="540" y="340" width="12" height="30" rx="3" fill="#1e293b" />
          <rect x="815" y="340" width="12" height="30" rx="3" fill="#1e293b" />
          <!-- Gối tựa đầu hình tròn -->
          <ellipse cx="555" cy="295" rx="20" ry="14" fill="#fbbf24" stroke="#d97706" stroke-width="1.5" />
        </g>

        <!-- Nhân viên người que 2 (Dáng ngủ lăn lóc siêu hài) -->
        <g class="sofa-sleeper-body">
          <!-- Thân người que nằm dài trên sofa -->
          <line x1="565" y1="298" x2="685" y2="305" stroke="#f8fafc" stroke-width="6" stroke-linecap="round" class="breathe-torso" />

          <!-- Chân 1: Duỗi dài xuôi theo sofa -->
          <line x1="685" y1="305" x2="770" y2="308" stroke="#f8fafc" stroke-width="5" stroke-linecap="round" />
          <line x1="770" y1="308" x2="820" y2="302" stroke="#f8fafc" stroke-width="4.5" stroke-linecap="round" />

          <!-- Chân 2: Co gác ngược lên tựa lưng sofa (lăn lóc) -->
          <line x1="685" y1="305" x2="715" y2="265" stroke="#f8fafc" stroke-width="5" stroke-linecap="round" />
          <line x1="715" y1="265" x2="755" y2="245" stroke="#f8fafc" stroke-width="4.5" stroke-linecap="round" class="leg-twitched" />
          <!-- Bàn chân chổng lên trời -->
          <circle cx="757" cy="243" r="4" fill="#f8fafc" />

          <!-- Tay 1: Buông thõng chạm xuống sàn -->
          <path d="M605 300 Q620 330 610 365" stroke="#f8fafc" stroke-width="4.5" stroke-linecap="round" fill="none" />

          <!-- Tay 2: Vắt ngang trán che mắt -->
          <path d="M595 298 Q580 275 560 282" stroke="#f8fafc" stroke-width="4.5" stroke-linecap="round" fill="none" />

          <!-- Chăn mỏng đắp xộc xệch ngang rốn -->
          <path d="M635 298 Q665 290 695 300 L690 320 Q660 325 630 318 Z" fill="#ec4899" opacity="0.85" stroke="#be185d" stroke-width="1.5" class="breathe-blanket" />

          <!-- Đầu người que (gối đầu lên chiếc gối vàng) -->
          <circle cx="560" cy="286" r="17" fill="#f8fafc" stroke="#0f172a" stroke-width="2" class="sofa-head" />
          
          <!-- Miệng há to ngáy sảng khoái -->
          <ellipse cx="568" cy="292" rx="4" ry="6" fill="#0f172a" />
          <!-- Giọt nước miếng nhỏ giọt hài hước -->
          <circle cx="571" cy="301" r="2" fill="#38bdf8" class="drool-drop" />

          <!-- Mắt nhắm hình dấu X nhắm tịt (ngủ say như chết) -->
          <path d="M552 280 L558 286 M558 280 L552 286" stroke="#0f172a" stroke-width="1.8" stroke-linecap="round" />

          <!-- Chiếc dép tổ ong văng xa dưới sàn -->
          <g transform="translate(640, 395) rotate(25)">
            <ellipse cx="0" cy="0" rx="14" ry="7" fill="#facc15" stroke="#ca8a04" stroke-width="1.5" />
            <path d="M-6 -4 L6 -4" stroke="#ca8a04" stroke-width="1" />
            <path d="M-6 0 L6 0" stroke="#ca8a04" stroke-width="1" />
            <path d="M-6 4 L6 4" stroke="#ca8a04" stroke-width="1" />
          </g>

          <!-- Dép còn lại nằm cạnh sofa -->
          <g transform="translate(730, 385) rotate(-15)">
            <ellipse cx="0" cy="0" rx="14" ry="7" fill="#facc15" stroke="#ca8a04" stroke-width="1.5" />
          </g>

          <!-- Bong bóng chữ ZZZ khổng lồ bay lên -->
          <g class="sofa-zzz-group">
            <text x="580" y="260" font-size="16" font-weight="bold" fill="#f43f5e" class="zzz-item zzz-sofa-1">z</text>
            <text x="595" y="235" font-size="22" font-weight="bold" fill="#fb7185" class="zzz-item zzz-sofa-2">Z</text>
            <text x="615" y="200" font-size="30" font-weight="bold" fill="#fda4af" class="zzz-item zzz-sofa-3">Zzz!</text>
          </g>

          <!-- Bảng tên nhân viên 2 -->
          <g transform="translate(680, 205)">
            <rect x="0" y="0" width="75" height="22" rx="4" fill="#1e293b" stroke="#f43f5e" stroke-width="1.2" />
            <text x="6" y="14" font-size="9" font-weight="bold" fill="#fb7185">Chuyên viên B</text>
          </g>
        </g>
      </g>

      <!-- Dấu chấm hỏi / chấm than khi bị chọt giật mình -->
      <g v-if="poked" class="startle-alert" transform="translate(420, 160)">
        <circle cx="20" cy="20" r="24" fill="#ef4444" stroke="#ffffff" stroke-width="2" />
        <text x="14" y="28" font-size="24" font-weight="bold" fill="#ffffff">!</text>
      </g>
    </svg>

    <!-- Hộp thoại bóng nói hài hước khi chọt -->
    <Transition name="speech-pop">
      <div v-if="quote" class="stickman-speech-bubble">
        <span class="speech-quote">{{ quote }}</span>
      </div>
    </Transition>

    <div class="stickman-hint">
      <span class="hint-icon">👆</span>
      <span>Bấm vào nhân viên để "chọt" nhẹ đánh thức!</span>
    </div>
  </div>
</template>

<style scoped>
.stickman-stage {
  position: relative;
  width: 100%;
  max-width: 860px;
  margin: 0 auto;
  border-radius: 20px;
  overflow: hidden;
  box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.4), 0 0 0 1px rgba(255, 255, 255, 0.08);
  cursor: pointer;
  user-select: none;
  background: #0f172a;
}

.stickman-svg {
  width: 100%;
  height: auto;
  display: block;
}

/* Animations */

/* Nhịp thở bàn làm việc */
.breathe-spine {
  animation: spine-breathe 3.5s ease-in-out infinite;
  transform-origin: 210px 330px;
}

@keyframes spine-breathe {
  0%, 100% {
    transform: scaleY(1);
  }
  50% {
    transform: scaleY(1.04) translateY(-2px);
  }
}

.desk-head {
  animation: head-nod 3.5s ease-in-out infinite;
  transform-origin: 180px 278px;
}

@keyframes head-nod {
  0%, 100% {
    transform: rotate(0deg);
  }
  50% {
    transform: rotate(-3deg) translateY(-2px);
  }
}

/* Bong bóng mũi phập phồng */
.snot-bubble {
  animation: snot-pulse 3.5s ease-in-out infinite;
  transform-origin: 171px 281px;
}

@keyframes snot-pulse {
  0%, 100% {
    transform: scale(0.6);
    opacity: 0.2;
  }
  45% {
    transform: scale(1.6);
    opacity: 0.8;
  }
  50% {
    transform: scale(0.2);
    opacity: 0;
  }
}

/* Nhịp thở sofa */
.breathe-torso, .breathe-blanket {
  animation: torso-breathe 4s ease-in-out infinite;
  transform-origin: 625px 305px;
}

@keyframes torso-breathe {
  0%, 100% {
    transform: translateY(0);
  }
  50% {
    transform: translateY(-3px) scaleY(1.03);
  }
}

/* Chân sofa thi thoảng co giật */
.leg-twitched {
  animation: leg-twitch 6s ease-in-out infinite;
  transform-origin: 685px 305px;
}

@keyframes leg-twitch {
  0%, 80%, 100% {
    transform: rotate(0deg);
  }
  85% {
    transform: rotate(3deg);
  }
  90% {
    transform: rotate(-2deg);
  }
}

/* Nước miếng chảy giọt */
.drool-drop {
  animation: drool-fall 2.8s ease-in infinite;
}

@keyframes drool-fall {
  0% {
    opacity: 0;
    transform: translateY(0) scale(0.5);
  }
  30% {
    opacity: 0.8;
    transform: translateY(2px) scale(1);
  }
  70% {
    opacity: 0.8;
    transform: translateY(12px) scale(0.8);
  }
  100% {
    opacity: 0;
    transform: translateY(18px) scale(0.2);
  }
}

/* Chữ Zzz bay bổng */
.zzz-item {
  opacity: 0;
  transform-origin: center;
}

.zzz-1 {
  animation: float-zzz 3.6s ease-out infinite;
  animation-delay: 0s;
}

.zzz-2 {
  animation: float-zzz 3.6s ease-out infinite;
  animation-delay: 1.2s;
}

.zzz-3 {
  animation: float-zzz 3.6s ease-out infinite;
  animation-delay: 2.4s;
}

.zzz-sofa-1 {
  animation: float-zzz-sofa 4s ease-out infinite;
  animation-delay: 0.4s;
}

.zzz-sofa-2 {
  animation: float-zzz-sofa 4s ease-out infinite;
  animation-delay: 1.6s;
}

.zzz-sofa-3 {
  animation: float-zzz-sofa 4s ease-out infinite;
  animation-delay: 2.8s;
}

@keyframes float-zzz {
  0% {
    opacity: 0;
    transform: translateY(10px) scale(0.6) rotate(-5deg);
  }
  30% {
    opacity: 0.9;
  }
  80% {
    opacity: 0.6;
  }
  100% {
    opacity: 0;
    transform: translateY(-40px) scale(1.3) rotate(15deg);
  }
}

@keyframes float-zzz-sofa {
  0% {
    opacity: 0;
    transform: translateY(10px) scale(0.6) rotate(5deg);
  }
  30% {
    opacity: 0.9;
  }
  80% {
    opacity: 0.6;
  }
  100% {
    opacity: 0;
    transform: translateY(-45px) scale(1.4) rotate(-10deg);
  }
}

/* Đồng hồ quay kim phút nhẹ */
.clock-minute-hand {
  animation: clock-tick 60s linear infinite;
  transform-origin: 0 0;
}

@keyframes clock-tick {
  from {
    transform: rotate(0deg);
  }
  to {
    transform: rotate(360deg);
  }
}

/* Khi bị chọt giật mình */
.is-startled {
  animation: worker-startle 0.4s cubic-bezier(0.36, 0.07, 0.19, 0.97) both;
}

@keyframes worker-startle {
  10%, 90% { transform: translate3d(-2px, -3px, 0); }
  20%, 80% { transform: translate3d(3px, 2px, 0); }
  30%, 50%, 70% { transform: translate3d(-3px, -4px, 0); }
  40%, 60% { transform: translate3d(3px, 3px, 0); }
}

.startle-alert {
  animation: startle-pop 0.35s cubic-bezier(0.175, 0.885, 0.32, 1.275) both;
}

@keyframes startle-pop {
  0% {
    opacity: 0;
    transform: translate(420px, 180px) scale(0.2);
  }
  100% {
    opacity: 1;
    transform: translate(420px, 160px) scale(1);
  }
}

/* Hộp thoại nói chuyện khi bị chọt */
.stickman-speech-bubble {
  position: absolute;
  top: 18px;
  left: 50%;
  transform: translateX(-50%);
  background: #ffffff;
  color: #0f172a;
  padding: 10px 18px;
  border-radius: 14px;
  box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.3);
  font-size: 14px;
  font-weight: 600;
  z-index: 10;
  max-width: 80%;
  text-align: center;
  border: 2px solid #38bdf8;
}

.stickman-speech-bubble::after {
  content: '';
  position: absolute;
  bottom: -10px;
  left: 50%;
  transform: translateX(-50%);
  border-width: 10px 10px 0;
  border-style: solid;
  border-color: #ffffff transparent;
}

.stickman-hint {
  position: absolute;
  bottom: 12px;
  left: 50%;
  transform: translateX(-50%);
  background: rgba(15, 23, 42, 0.75);
  backdrop-filter: blur(4px);
  padding: 6px 14px;
  border-radius: 20px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  font-size: 12px;
  color: #94a3b8;
  display: flex;
  align-items: center;
  gap: 6px;
  pointer-events: none;
}

.hint-icon {
  animation: bounce-hand 1.5s infinite;
}

@keyframes bounce-hand {
  0%, 100% { transform: translateY(0); }
  50% { transform: translateY(-3px); }
}

.speech-pop-enter-active,
.speech-pop-leave-active {
  transition: all 0.25s cubic-bezier(0.16, 1, 0.3, 1);
}

.speech-pop-enter-from,
.speech-pop-leave-to {
  opacity: 0;
  transform: translateX(-50%) translateY(-10px) scale(0.9);
}
</style>
