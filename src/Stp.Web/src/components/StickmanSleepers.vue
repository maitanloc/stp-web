<script setup lang="ts">
import { computed } from 'vue'
import type { FanSpeed, LunchDream } from '../composables/useLunchBreak'

const props = withDefaults(
  defineProps<{
    poked?: boolean
    quote?: string
    isLightsDimmed?: boolean
    fanSpeed?: FanSpeed
    catPetCount?: number
    catPurrText?: string
    currentDream?: LunchDream
    isMosquitoAlive?: boolean
    lastSlapPos?: { x: number; y: number } | null
  }>(),
  {
    poked: false,
    quote: '',
    isLightsDimmed: false,
    fanSpeed: 1,
    catPetCount: 0,
    catPurrText: '',
    currentDream: undefined,
    isMosquitoAlive: true,
    lastSlapPos: null,
  },
)

const emit = defineEmits<{
  (e: 'poke'): void
  (e: 'petCat'): void
  (e: 'cycleFan'): void
  (e: 'toggleLights'): void
  (e: 'nextDream'): void
  (e: 'slapMosquito', payload: { x: number; y: number }): void
}>()

const fanSpeedClass = computed(() => {
  if (props.fanSpeed === 0) return 'fan-off'
  if (props.fanSpeed === 2) return 'fan-high'
  return 'fan-low'
})

function handleSvgClick() {
  // If clicked directly on the stage background without child stopPropagation
  emit('poke')
}

function onMosquitoClick(e: MouseEvent) {
  e.stopPropagation()
  const svg = (e.currentTarget as SVGElement).ownerSVGElement
  if (!svg) {
    emit('slapMosquito', { x: 520, y: 220 })
    return
  }
  const rect = svg.getBoundingClientRect()
  const x = ((e.clientX - rect.left) / rect.width) * 900
  const y = ((e.clientY - rect.top) / rect.height) * 480
  emit('slapMosquito', { x, y })
}
</script>

<template>
  <div
    class="stickman-stage"
    :class="{ 'is-dimmed': isLightsDimmed }"
    @click="handleSvgClick"
  >
    <svg
      viewBox="0 0 900 480"
      class="stickman-svg"
      xmlns="http://www.w3.org/2000/svg"
      role="img"
      aria-label="Khung cảnh văn phòng STP giờ nghỉ trưa với nhân viên stickman"
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
          <stop offset="0%" stop-color="#38bdf8" stop-opacity="0.7" />
          <stop offset="100%" stop-color="#0284c7" stop-opacity="0.3" />
        </linearGradient>

        <linearGradient id="lampConeGrad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#fef08a" stop-opacity="0.6" />
          <stop offset="100%" stop-color="#fef08a" stop-opacity="0" />
        </linearGradient>

        <linearGradient id="moonlightCone" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#bae6fd" stop-opacity="0.4" />
          <stop offset="100%" stop-color="#bae6fd" stop-opacity="0" />
        </linearGradient>

        <linearGradient id="dreamGrad" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stop-color="#eff6ff" />
          <stop offset="100%" stop-color="#dbeafe" />
        </linearGradient>

        <filter id="glow" x="-20%" y="-20%" width="140%" height="140%">
          <feGaussianBlur stdDeviation="3.5" result="blur" />
          <feComposite in="SourceGraphic" in2="blur" operator="over" />
        </filter>

        <filter id="slapGlow" x="-30%" y="-30%" width="160%" height="160%">
          <feGaussianBlur stdDeviation="4" result="blur" />
          <feComposite in="SourceGraphic" in2="blur" operator="over" />
        </filter>
      </defs>

      <!-- Tường & Sàn phòng làm việc -->
      <rect x="0" y="0" width="900" height="350" fill="url(#wallGrad)" />
      <rect x="0" y="350" width="900" height="130" fill="url(#floorGrad)" />
      <line x1="0" y1="350" x2="900" y2="350" stroke="#475569" stroke-width="2" />

      <!-- Cửa sổ nhìn ra bầu trời trưa yên ả -->
      <g id="window-group">
        <rect x="375" y="35" width="150" height="115" rx="8" fill="#090d16" stroke="#475569" stroke-width="3" />
        <path d="M450 35 L450 150 M375 92 L525 92" stroke="#334155" stroke-width="2" />
        <!-- Trăng khuyết & sao ban trưa -->
        <circle cx="490" cy="65" r="16" fill="#fbbf24" opacity="0.85" filter="url(#glow)" />
        <circle cx="496" cy="62" r="15" fill="#090d16" />
        <circle cx="405" cy="60" r="1.5" fill="#f8fafc" opacity="0.7" />
        <circle cx="425" cy="75" r="2" fill="#f8fafc" opacity="0.8" />
        <circle cx="465" cy="115" r="1.5" fill="#f8fafc" opacity="0.5" />
        <circle cx="505" cy="125" r="2" fill="#f8fafc" opacity="0.6" />
        <!-- Rèm cửa hai bên -->
        <path d="M375 35 Q395 90 382 150 L375 150 Z" fill="#64748b" opacity="0.7" />
        <path d="M525 35 Q505 90 518 150 L525 150 Z" fill="#64748b" opacity="0.7" />
        <!-- Ánh trăng hắt qua cửa sổ khi tắt đèn -->
        <polygon v-if="isLightsDimmed" points="390,150 510,150 570,350 330,350" fill="url(#moonlightCone)" />
      </g>

      <!-- Giấy ghi chú trên tường -->
      <g transform="translate(680, 45) rotate(4)">
        <rect x="0" y="0" width="95" height="75" rx="3" fill="#fef08a" />
        <text x="8" y="20" font-size="10" font-weight="bold" fill="#854d0e">LƯU Ý 12H:</text>
        <text x="8" y="36" font-size="9" fill="#a16207">✓ Cơm trưa 🍱</text>
        <text x="8" y="50" font-size="9" fill="#a16207">✓ Chợp mắt 💤</text>
        <text x="8" y="64" font-size="9" fill="#a16207">✓ Cấm gõ phím</text>
      </g>

      <g transform="translate(130, 45) rotate(-3)">
        <rect x="0" y="0" width="95" height="75" rx="3" fill="#fed7aa" />
        <text x="8" y="20" font-size="10" font-weight="bold" fill="#9a3412">DEV BẢO TRÌ:</text>
        <text x="8" y="36" font-size="9" fill="#c2410c">Đang vá máy chủ 🛠️</text>
        <text x="8" y="50" font-size="9" fill="#c2410c">Đúng 13:00</text>
        <text x="8" y="64" font-size="9" fill="#c2410c">Mở lại làm tiếp!</text>
      </g>

      <!-- Đồng hồ treo tường chỉ 12h -->
      <g transform="translate(285, 75)">
        <circle cx="0" cy="0" r="30" fill="#1e293b" stroke="#e2e8f0" stroke-width="3" />
        <circle cx="0" cy="0" r="26" fill="#0f172a" />
        <text x="-4" y="-17" font-size="8" font-weight="bold" fill="#94a3b8">12</text>
        <text x="16" y="3" font-size="8" font-weight="bold" fill="#94a3b8">3</text>
        <text x="-3" y="21" font-size="8" font-weight="bold" fill="#94a3b8">6</text>
        <text x="-22" y="3" font-size="8" font-weight="bold" fill="#94a3b8">9</text>
        <!-- Kim giờ chỉ số 12 -->
        <line x1="0" y1="0" x2="0" y2="-15" stroke="#f8fafc" stroke-width="2.5" stroke-linecap="round" />
        <!-- Kim phút quay chậm -->
        <line x1="0" y1="0" x2="6" y2="-17" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" class="clock-minute-hand" />
        <circle cx="0" cy="0" r="3" fill="#f8fafc" />
      </g>

      <!-- ==================== GÓC BÀN LÀM VIỆC & STICKMAN 1 ==================== -->
      <g id="stickman-desk-area" class="stickman-worker" :class="{ 'is-startled': poked }">
        <!-- Đèn bàn làm việc -->
        <g id="desk-lamp" class="interactive-element" @click.stop="emit('toggleLights')">
          <!-- Chân đèn trên bàn -->
          <ellipse cx="65" cy="305" rx="10" ry="4" fill="#64748b" />
          <path d="M65 305 Q55 240 75 220" stroke="#94a3b8" stroke-width="3" fill="none" stroke-linecap="round" />
          <!-- Chao đèn -->
          <path d="M65 220 L95 210 L85 230 Z" fill="#f59e0b" stroke="#d97706" stroke-width="1.5" />
          <!-- Dây giật công tắc đèn -->
          <line x1="80" y1="230" x2="80" y2="245" stroke="#cbd5e1" stroke-width="1.5" />
          <circle cx="80" cy="246" r="2.5" fill="#f59e0b" />
          <!-- Quầng sáng đèn bàn khi bật -->
          <polygon points="75,225 180,310 65,310" fill="url(#lampConeGrad)" />
        </g>

        <!-- Thùng máy CPU & Boss Mèo -->
        <g id="cpu-and-cat" class="interactive-element" @click.stop="emit('petCat')">
          <!-- Thùng máy CPU -->
          <rect x="25" y="325" width="30" height="65" rx="3" fill="#1e293b" stroke="#475569" stroke-width="1.5" />
          <!-- Dải LED RGB thùng máy -->
          <line x1="30" y1="335" x2="30" y2="380" stroke="#06b6d4" stroke-width="2" opacity="0.8" />
          <circle cx="48" cy="335" r="2" fill="#22c55e" />
          <circle cx="48" cy="342" r="1.5" fill="#ef4444" />

          <!-- BOSS MÈO CÔNG SỞ NẰM NGỦ TRÊN CPU -->
          <g id="office-cat" class="cat-sleeper">
            <!-- Thân mèo cuộn tròn -->
            <ellipse cx="40" cy="320" rx="14" ry="10" fill="#f97316" stroke="#c2410c" stroke-width="1.2" class="cat-body" />
            <!-- Đầu mèo -->
            <circle cx="31" cy="317" r="7" fill="#f97316" stroke="#c2410c" stroke-width="1.2" />
            <!-- Tai mèo -->
            <polygon points="26,312 28,306 32,312" fill="#ea580c" />
            <polygon points="31,312 34,306 37,312" fill="#ea580c" />
            <!-- Mắt nhắm hí hí -->
            <path d="M28 317 Q30 319 32 317" stroke="#7c2d12" stroke-width="1" fill="none" />
            <!-- Mũi hồng -->
            <circle cx="27" cy="319" r="1" fill="#fbcfe8" />
            <!-- Đuôi mèo ngoe nguẩy -->
            <path d="M53 322 Q58 316 57 328" stroke="#f97316" stroke-width="2.5" fill="none" stroke-linecap="round" class="cat-tail" />
            <!-- Chữ zzz của mèo -->
            <text x="24" y="303" font-size="8" font-weight="bold" fill="#fdba74" class="cat-zzz">z</text>
          </g>

          <!-- Tim bay lên khi vuốt mèo -->
          <g v-if="catPetCount > 0" class="cat-heart-burst">
            <text x="35" y="295" font-size="12" fill="#ef4444" class="cat-heart">❤️</text>
          </g>
        </g>

        <!-- Bàn làm việc & Laptop -->
        <g id="desk-furniture">
          <rect x="75" y="320" width="10" height="90" rx="3" fill="#94a3b8" />
          <rect x="250" y="320" width="10" height="90" rx="3" fill="#94a3b8" />
          <rect x="60" y="305" width="220" height="18" rx="4" fill="url(#deskGrad)" stroke="#78350f" stroke-width="1.5" />
          
          <!-- Laptop -->
          <path d="M90 305 L135 305 L138 275 L93 275 Z" fill="#475569" />
          <rect x="95" y="277" width="40" height="25" rx="2" fill="url(#screenGrad)" />
          <line x1="100" y1="285" x2="125" y2="285" stroke="#bae6fd" stroke-width="1" opacity="0.6" />
          <line x1="100" y1="290" x2="118" y2="290" stroke="#bae6fd" stroke-width="1" opacity="0.6" />
          <path d="M85 305 L140 305 L143 308 L82 308 Z" fill="#64748b" />
          
          <!-- Cốc cà phê STP rỗng -->
          <rect x="155" y="290" width="14" height="16" rx="2" fill="#f1f5f9" />
          <path d="M169 294 Q175 298 169 302" stroke="#f1f5f9" stroke-width="2" fill="none" />
          <text x="157" y="299" font-size="6" font-weight="bold" fill="#64748b">STP</text>

          <!-- Ghế xoay văn phòng -->
          <path d="M205 270 Q190 270 190 330 L235 330 Q235 270 205 270" fill="#334155" opacity="0.8" />
          <rect x="208" y="330" width="8" height="60" fill="#64748b" />
          <line x1="212" y1="390" x2="185" y2="405" stroke="#475569" stroke-width="4" stroke-linecap="round" />
          <line x1="212" y1="390" x2="240" y2="405" stroke="#475569" stroke-width="4" stroke-linecap="round" />
        </g>

        <!-- Stickman 1 (Gục trên bàn) -->
        <g class="desk-sleeper-body">
          <line x1="210" y1="330" x2="200" y2="385" stroke="#f8fafc" stroke-width="5" stroke-linecap="round" />
          <line x1="200" y1="385" x2="215" y2="410" stroke="#f8fafc" stroke-width="4.5" stroke-linecap="round" />
          <path d="M210 330 Q195 285 180 295" stroke="#f8fafc" stroke-width="6" stroke-linecap="round" fill="none" class="breathe-spine" />
          <path d="M185 292 Q175 305 160 305 L200 305" stroke="#f8fafc" stroke-width="4.5" stroke-linecap="round" fill="none" />
          <circle cx="180" cy="278" r="16" fill="#f8fafc" stroke="#0f172a" stroke-width="2" class="desk-head" />
          <path d="M172 277 Q176 281 180 277" stroke="#0f172a" stroke-width="2" fill="none" stroke-linecap="round" />
          <path d="M174 285 Q178 288 183 285" stroke="#0f172a" stroke-width="1.8" fill="none" stroke-linecap="round" />

          <!-- Bong bóng mũi phập phồng -->
          <circle cx="171" cy="281" r="5" fill="#38bdf8" opacity="0.65" stroke="#0284c7" stroke-width="1" class="snot-bubble" />

          <!-- Chữ ZZZ bay lên -->
          <g class="desk-zzz-group">
            <text x="160" y="255" font-size="14" font-weight="bold" fill="#38bdf8" class="zzz-item zzz-1">z</text>
            <text x="150" y="235" font-size="18" font-weight="bold" fill="#60a5fa" class="zzz-item zzz-2">Z</text>
            <text x="135" y="210" font-size="24" font-weight="bold" fill="#818cf8" class="zzz-item zzz-3">Zzz</text>
          </g>

          <!-- Bảng tên -->
          <g transform="translate(195, 235)">
            <rect x="0" y="0" width="80" height="22" rx="4" fill="#1e293b" stroke="#38bdf8" stroke-width="1.2" />
            <text x="6" y="14" font-size="9" font-weight="bold" fill="#38bdf8">Chuyên viên A</text>
          </g>
        </g>
      </g>

      <!-- ==================== KHU VỰC GIỮA: QUẠT MÁY & BONG BÓNG GIẤC MƠ ==================== -->
      
      <!-- CÂY QUẠT MÁY VĂN PHÒNG -->
      <g
        id="electric-fan"
        class="interactive-element"
        transform="translate(340, 275)"
        @click.stop="emit('cycleFan')"
      >
        <!-- Chân đế quạt -->
        <ellipse cx="20" cy="115" rx="18" ry="7" fill="#475569" stroke="#334155" stroke-width="1.5" />
        <rect x="18" y="45" width="4" height="70" fill="#94a3b8" />
        <!-- Bầu động cơ quạt -->
        <circle cx="20" cy="45" r="9" fill="#334155" stroke="#1e293b" stroke-width="1.5" />
        
        <!-- Cụm đầu lồng quạt & cánh quạt (lắc lư qua lại) -->
        <g class="fan-head-oscillation" :class="fanSpeedClass">
          <!-- Lồng quạt -->
          <circle cx="20" cy="45" r="28" fill="none" stroke="#64748b" stroke-width="2" />
          <circle cx="20" cy="45" r="24" fill="none" stroke="#94a3b8" stroke-width="0.8" stroke-dasharray="2 3" />
          <line x1="20" y1="17" x2="20" y2="73" stroke="#64748b" stroke-width="1.2" />
          <line x1="-8" y1="45" x2="48" y2="45" stroke="#64748b" stroke-width="1.2" />

          <!-- Cánh quạt 3 cánh quay tròn -->
          <g class="fan-blades-group" :class="fanSpeedClass">
            <path d="M20 45 Q32 30 30 18 Q20 28 20 45 Z" fill="#38bdf8" opacity="0.85" />
            <path d="M20 45 Q35 55 45 60 Q32 60 20 45 Z" fill="#38bdf8" opacity="0.85" />
            <path d="M20 45 Q5 55 -5 50 Q10 40 20 45 Z" fill="#38bdf8" opacity="0.85" />
            <circle cx="20" cy="45" r="4" fill="#0284c7" />
          </g>

          <!-- Luồng gió quạt phà sang 2 bên -->
          <g v-if="fanSpeed > 0" class="fan-wind-streams" :class="{ 'wind-fast': fanSpeed === 2 }">
            <path d="M48 35 Q100 20 180 30" stroke="#38bdf8" stroke-width="1.5" stroke-dasharray="8 6" fill="none" class="wind-line wind-1" />
            <path d="M48 48 Q120 50 200 45" stroke="#38bdf8" stroke-width="1.8" stroke-dasharray="10 8" fill="none" class="wind-line wind-2" />
            <path d="M48 60 Q90 75 160 70" stroke="#38bdf8" stroke-width="1.5" stroke-dasharray="8 6" fill="none" class="wind-line wind-3" />
          </g>
        </g>

        <!-- Nút bấm tốc độ quạt -->
        <g transform="translate(11, 102)">
          <rect x="0" y="0" width="18" height="10" rx="2" fill="#1e293b" />
          <circle cx="5" cy="5" r="2.5" :fill="fanSpeed === 0 ? '#ef4444' : '#64748b'" />
          <circle cx="9" cy="5" r="2.5" :fill="fanSpeed === 1 ? '#22c55e' : '#64748b'" />
          <circle cx="13" cy="5" r="2.5" :fill="fanSpeed === 2 ? '#38bdf8' : '#64748b'" />
        </g>
      </g>

      <!-- BONG BÓNG GIẤC MƠ TRƯA (DREAM BUBBLE) -->
      <g
        v-if="currentDream"
        id="dream-bubble"
        class="interactive-element dream-cloud-group"
        transform="translate(420, 160)"
        @click.stop="emit('nextDream')"
      >
        <!-- Chuỗi bong bóng nối từ chuyên viên A -->
        <circle cx="-130" cy="80" r="4" fill="#dbeafe" opacity="0.7" />
        <circle cx="-110" cy="55" r="6" fill="#dbeafe" opacity="0.8" />
        <circle cx="-85" cy="30" r="9" fill="#dbeafe" opacity="0.9" />

        <!-- Đám mây giấc mơ bồng bềnh -->
        <g class="dream-cloud-shape">
          <!-- Đám mây viền mềm -->
          <path
            d="M-50 0 Q-70 -25 -40 -45 Q-15 -65 25 -50 Q65 -65 95 -40 Q130 -30 125 5 Q135 40 95 45 Q65 60 20 50 Q-25 60 -50 35 Q-75 25 -50 0 Z"
            fill="url(#dreamGrad)"
            stroke="#93c5fd"
            stroke-width="2"
            filter="url(#glow)"
          />
          <!-- Nội dung giấc mơ -->
          <text x="35" y="-12" font-size="28" text-anchor="middle">{{ currentDream.icon }}</text>
          <text x="35" y="14" font-size="11" font-weight="bold" fill="#1e3a8a" text-anchor="middle">
            {{ currentDream.title }}
          </text>
          <text x="35" y="30" font-size="8.5" fill="#3b82f6" text-anchor="middle">
            {{ currentDream.subtitle.length > 28 ? currentDream.subtitle.slice(0, 27) + '…' : currentDream.subtitle }}
          </text>
          <!-- Badge nhỏ -->
          <g transform="translate(75, -45)">
            <rect x="0" y="0" width="55" height="15" rx="7" fill="#3b82f6" />
            <text x="27" y="11" font-size="7.5" font-weight="bold" fill="#ffffff" text-anchor="middle">
              {{ currentDream.badge }}
            </text>
          </g>
        </g>
      </g>

      <!-- CON MUỖI CÔNG SỞ BAY VO VE (CLICK ĐỂ ĐẬP) -->
      <g
        v-if="isMosquitoAlive"
        id="flying-mosquito"
        class="interactive-element mosquito-character"
        @click="onMosquitoClick"
      >
        <title>Con muỗi vo ve - Bấm để đập bảo vệ giấc ngủ!</title>
        <!-- Vòng hào quang nhỏ để dễ bấm -->
        <circle cx="0" cy="0" r="16" fill="transparent" />
        <!-- Cánh muỗi đập lia lịa -->
        <ellipse cx="-4" cy="-5" rx="2" ry="5" fill="#e2e8f0" opacity="0.7" class="mosquito-wing-l" />
        <ellipse cx="4" cy="-5" rx="2" ry="5" fill="#e2e8f0" opacity="0.7" class="mosquito-wing-r" />
        <!-- Thân muỗi -->
        <ellipse cx="0" cy="0" rx="3" ry="5" fill="#0f172a" />
        <circle cx="0" cy="-4" r="2.5" fill="#ef4444" />
        <!-- Vòi hút máu -->
        <line x1="0" y1="-5" x2="0" y2="-10" stroke="#0f172a" stroke-width="1.2" />
        <!-- Chân muỗi -->
        <line x1="-2" y1="2" x2="-6" y2="7" stroke="#0f172a" stroke-width="1" />
        <line x1="2" y1="2" x2="6" y2="7" stroke="#0f172a" stroke-width="1" />
      </g>

      <!-- VẾT ĐẬP MUỖI (BỐP!) KHI VỪA ĐẬP TRÚNG -->
      <g
        v-if="lastSlapPos"
        class="slap-splash"
        :transform="`translate(${lastSlapPos.x}, ${lastSlapPos.y})`"
      >
        <!-- Sao tóe lửa comic -->
        <polygon
          points="0,-22 6,-8 22,-8 9,2 14,18 0,7 -14,18 -9,2 -22,-8 -6,-8"
          fill="#ef4444"
          stroke="#fef08a"
          stroke-width="2"
          filter="url(#slapGlow)"
        />
        <text x="0" y="4" font-size="11" font-weight="900" fill="#ffffff" text-anchor="middle">BỐP!</text>
        <text x="0" y="24" font-size="9" font-weight="bold" fill="#22c55e" text-anchor="middle">+1 GIẤC NGỦ</text>
      </g>

      <!-- ==================== NHÂN VIÊN 2: NẰM NGỦ LĂN LÓC TRÊN SOFA ==================== -->
      <g id="stickman-sofa-area" class="stickman-worker" :class="{ 'is-startled': poked }">
        <g id="sofa-furniture">
          <rect x="530" y="260" width="310" height="60" rx="10" fill="url(#sofaGrad)" stroke="#312e81" stroke-width="2" />
          <rect x="515" y="270" width="28" height="65" rx="8" fill="#4338ca" stroke="#312e81" stroke-width="2" />
          <rect x="525" y="305" width="320" height="35" rx="8" fill="#4f46e5" stroke="#312e81" stroke-width="2" />
          <rect x="830" y="270" width="28" height="65" rx="8" fill="#4338ca" stroke="#312e81" stroke-width="2" />
          <rect x="540" y="340" width="12" height="30" rx="3" fill="#1e293b" />
          <rect x="815" y="340" width="12" height="30" rx="3" fill="#1e293b" />
          <ellipse cx="555" cy="295" rx="20" ry="14" fill="#fbbf24" stroke="#d97706" stroke-width="1.5" />
        </g>

        <!-- Thân Stickman 2 -->
        <g class="sofa-sleeper-body">
          <line x1="565" y1="298" x2="685" y2="305" stroke="#f8fafc" stroke-width="6" stroke-linecap="round" class="breathe-torso" />
          <line x1="685" y1="305" x2="770" y2="308" stroke="#f8fafc" stroke-width="5" stroke-linecap="round" />
          <line x1="770" y1="308" x2="820" y2="302" stroke="#f8fafc" stroke-width="4.5" stroke-linecap="round" />
          
          <!-- Chân gác ngược lên thành ghế sofa (lăn lóc) -->
          <line x1="685" y1="305" x2="715" y2="265" stroke="#f8fafc" stroke-width="5" stroke-linecap="round" />
          <line x1="715" y1="265" x2="755" y2="245" stroke="#f8fafc" stroke-width="4.5" stroke-linecap="round" class="leg-twitched" />
          <circle cx="757" cy="243" r="4" fill="#f8fafc" />

          <!-- Tay chạm sàn -->
          <path d="M605 300 Q620 330 610 365" stroke="#f8fafc" stroke-width="4.5" stroke-linecap="round" fill="none" />
          <!-- Tay vắt ngang trán -->
          <path d="M595 298 Q580 275 560 282" stroke="#f8fafc" stroke-width="4.5" stroke-linecap="round" fill="none" />

          <!-- Chăn mỏng xộc xệch -->
          <path d="M635 298 Q665 290 695 300 L690 320 Q660 325 630 318 Z" fill="#ec4899" opacity="0.85" stroke="#be185d" stroke-width="1.5" class="breathe-blanket" />

          <!-- Đầu người que -->
          <circle cx="560" cy="286" r="17" fill="#f8fafc" stroke="#0f172a" stroke-width="2" class="sofa-head" />
          <ellipse cx="568" cy="292" rx="4" ry="6" fill="#0f172a" />
          <circle cx="571" cy="301" r="2" fill="#38bdf8" class="drool-drop" />
          <path d="M552 280 L558 286 M558 280 L552 286" stroke="#0f172a" stroke-width="1.8" stroke-linecap="round" />

          <!-- DÉP TỔ ONG VĂNG DƯỚI SÀN -->
          <g transform="translate(640, 395) rotate(25)" class="interactive-element" title="Dép tổ ong huyền thoại">
            <ellipse cx="0" cy="0" rx="14" ry="7" fill="#facc15" stroke="#ca8a04" stroke-width="1.5" />
            <path d="M-6 -4 L6 -4" stroke="#ca8a04" stroke-width="1" />
            <path d="M-6 0 L6 0" stroke="#ca8a04" stroke-width="1" />
            <path d="M-6 4 L6 4" stroke="#ca8a04" stroke-width="1" />
          </g>
          <g transform="translate(730, 385) rotate(-15)">
            <ellipse cx="0" cy="0" rx="14" ry="7" fill="#facc15" stroke="#ca8a04" stroke-width="1.5" />
          </g>

          <!-- Chữ Zzz bay lên từ sofa -->
          <g class="sofa-zzz-group">
            <text x="580" y="260" font-size="16" font-weight="bold" fill="#f43f5e" class="zzz-item zzz-sofa-1">z</text>
            <text x="595" y="235" font-size="22" font-weight="bold" fill="#fb7185" class="zzz-item zzz-sofa-2">Z</text>
            <text x="615" y="200" font-size="30" font-weight="bold" fill="#fda4af" class="zzz-item zzz-sofa-3">Zzz!</text>
          </g>

          <!-- Bảng tên -->
          <g transform="translate(680, 205)">
            <rect x="0" y="0" width="80" height="22" rx="4" fill="#1e293b" stroke="#f43f5e" stroke-width="1.2" />
            <text x="6" y="14" font-size="9" font-weight="bold" fill="#fb7185">Chuyên viên B</text>
          </g>
        </g>
      </g>

      <!-- CẢNH BÁO BỊ CHỌT GIẬT MÌNH -->
      <g v-if="poked" class="startle-alert" transform="translate(420, 140)">
        <circle cx="20" cy="20" r="24" fill="#ef4444" stroke="#ffffff" stroke-width="2" />
        <text x="14" y="28" font-size="24" font-weight="bold" fill="#ffffff">!</text>
      </g>

      <!-- LỚP PHỦ TẮT ĐÈN NGHỈ TRƯA (DIMMED NIGHT OVERLAY) -->
      <rect
        v-if="isLightsDimmed"
        x="0"
        y="0"
        width="900"
        height="480"
        fill="#050814"
        opacity="0.65"
        style="mix-blend-mode: multiply; pointer-events: none;"
      />
    </svg>

    <!-- Hộp thoại bóng nói của Boss Mèo khi vuốt -->
    <Transition name="speech-pop">
      <div v-if="catPurrText" class="cat-speech-bubble">
        <span>{{ catPurrText }}</span>
      </div>
    </Transition>

    <!-- Hộp thoại bóng nói hài hước khi chọt người que -->
    <Transition name="speech-pop">
      <div v-if="quote" class="stickman-speech-bubble">
        <span class="speech-quote">{{ quote }}</span>
      </div>
    </Transition>

    <!-- Thanh gợi ý tương tác thông minh -->
    <div class="stickman-hint">
      <span class="hint-item"><span class="hint-icon">👆</span> Chọt nhân viên</span>
      <span class="hint-sep">•</span>
      <span class="hint-item">🐱 Vuốt Boss Mèo</span>
      <span class="hint-sep">•</span>
      <span class="hint-item">💨 Bấm quạt đổi số</span>
      <span class="hint-sep">•</span>
      <span class="hint-item">💭 Đổi giấc mơ</span>
      <span class="hint-sep">•</span>
      <span class="hint-item">🦟 Đập muỗi</span>
    </div>
  </div>
</template>

<style scoped>
.stickman-stage {
  position: relative;
  width: 100%;
  max-width: 900px;
  margin: 0 auto;
  border-radius: 20px;
  overflow: hidden;
  box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.45), 0 0 0 1px rgba(255, 255, 255, 0.08);
  user-select: none;
  background: #0f172a;
  transition: filter 0.4s ease;
}

.stickman-stage.is-dimmed {
  box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.8), 0 0 30px rgba(56, 189, 248, 0.15);
}

.stickman-svg {
  width: 100%;
  height: auto;
  display: block;
  cursor: pointer;
}

.interactive-element {
  cursor: pointer;
  transition: transform 0.15s ease;
}

.interactive-element:hover {
  filter: drop-shadow(0 0 6px rgba(56, 189, 248, 0.6));
}

/* Animations */

/* Nhịp thở bàn làm việc */
.breathe-spine {
  animation: spine-breathe 3.5s ease-in-out infinite;
  transform-origin: 210px 330px;
}

@keyframes spine-breathe {
  0%, 100% { transform: scaleY(1); }
  50% { transform: scaleY(1.04) translateY(-2px); }
}

.desk-head {
  animation: head-nod 3.5s ease-in-out infinite;
  transform-origin: 180px 278px;
}

@keyframes head-nod {
  0%, 100% { transform: rotate(0deg); }
  50% { transform: rotate(-3deg) translateY(-2px); }
}

/* Bong bóng mũi phập phồng */
.snot-bubble {
  animation: snot-pulse 3.5s ease-in-out infinite;
  transform-origin: 171px 281px;
}

@keyframes snot-pulse {
  0%, 100% { transform: scale(0.6); opacity: 0.2; }
  45% { transform: scale(1.6); opacity: 0.85; }
  50% { transform: scale(0.2); opacity: 0; }
}

/* Nhịp thở sofa */
.breathe-torso, .breathe-blanket {
  animation: torso-breathe 4s ease-in-out infinite;
  transform-origin: 625px 305px;
}

@keyframes torso-breathe {
  0%, 100% { transform: translateY(0); }
  50% { transform: translateY(-3px) scaleY(1.03); }
}

/* Chân sofa thi thoảng co giật */
.leg-twitched {
  animation: leg-twitch 6s ease-in-out infinite;
  transform-origin: 685px 305px;
}

@keyframes leg-twitch {
  0%, 80%, 100% { transform: rotate(0deg); }
  85% { transform: rotate(3deg); }
  90% { transform: rotate(-2deg); }
}

/* Nước miếng chảy giọt */
.drool-drop {
  animation: drool-fall 2.8s ease-in infinite;
}

@keyframes drool-fall {
  0% { opacity: 0; transform: translateY(0) scale(0.5); }
  30% { opacity: 0.8; transform: translateY(2px) scale(1); }
  70% { opacity: 0.8; transform: translateY(12px) scale(0.8); }
  100% { opacity: 0; transform: translateY(18px) scale(0.2); }
}

/* Chữ Zzz bay bổng */
.zzz-item {
  opacity: 0;
  transform-origin: center;
}

.zzz-1 { animation: float-zzz 3.6s ease-out infinite; animation-delay: 0s; }
.zzz-2 { animation: float-zzz 3.6s ease-out infinite; animation-delay: 1.2s; }
.zzz-3 { animation: float-zzz 3.6s ease-out infinite; animation-delay: 2.4s; }

.zzz-sofa-1 { animation: float-zzz-sofa 4s ease-out infinite; animation-delay: 0.4s; }
.zzz-sofa-2 { animation: float-zzz-sofa 4s ease-out infinite; animation-delay: 1.6s; }
.zzz-sofa-3 { animation: float-zzz-sofa 4s ease-out infinite; animation-delay: 2.8s; }

@keyframes float-zzz {
  0% { opacity: 0; transform: translateY(10px) scale(0.6) rotate(-5deg); }
  30% { opacity: 0.9; }
  80% { opacity: 0.6; }
  100% { opacity: 0; transform: translateY(-40px) scale(1.3) rotate(15deg); }
}

@keyframes float-zzz-sofa {
  0% { opacity: 0; transform: translateY(10px) scale(0.6) rotate(5deg); }
  30% { opacity: 0.9; }
  80% { opacity: 0.6; }
  100% { opacity: 0; transform: translateY(-45px) scale(1.4) rotate(-10deg); }
}

/* Đồng hồ quay kim phút nhẹ */
.clock-minute-hand {
  animation: clock-tick 60s linear infinite;
  transform-origin: 0 0;
}

@keyframes clock-tick {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* Boss Mèo văn phòng */
.cat-tail {
  animation: cat-tail-swish 3s ease-in-out infinite;
  transform-origin: 53px 322px;
}

@keyframes cat-tail-swish {
  0%, 100% { transform: rotate(0deg); }
  50% { transform: rotate(18deg); }
}

.cat-body {
  animation: cat-breathe 2.8s ease-in-out infinite;
  transform-origin: 40px 320px;
}

@keyframes cat-breathe {
  0%, 100% { transform: scale(1); }
  50% { transform: scale(1.05); }
}

.cat-zzz {
  animation: float-zzz 2.5s ease-out infinite;
}

.cat-heart {
  animation: heart-float 1.2s ease-out forwards;
}

@keyframes heart-float {
  0% { opacity: 0; transform: translateY(0) scale(0.5); }
  40% { opacity: 1; transform: translateY(-10px) scale(1.2); }
  100% { opacity: 0; transform: translateY(-25px) scale(1.5); }
}

/* Quạt máy quay */
.fan-head-oscillation.fan-low,
.fan-head-oscillation.fan-high {
  animation: fan-swing 5s ease-in-out infinite alternate;
  transform-origin: 20px 115px;
}

@keyframes fan-swing {
  0% { transform: rotate(-15deg); }
  100% { transform: rotate(15deg); }
}

.fan-blades-group.fan-low {
  animation: fan-spin 0.35s linear infinite;
  transform-origin: 20px 45px;
}

.fan-blades-group.fan-high {
  animation: fan-spin 0.12s linear infinite;
  transform-origin: 20px 45px;
}

@keyframes fan-spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* Luồng gió */
.wind-line {
  animation: wind-flow 1.5s linear infinite;
}
.wind-1 { animation-delay: 0s; }
.wind-2 { animation-delay: 0.5s; }
.wind-3 { animation-delay: 1s; }

@keyframes wind-flow {
  0% { stroke-dashoffset: 30; opacity: 0; }
  50% { opacity: 0.8; }
  100% { stroke-dashoffset: -30; opacity: 0; }
}

.fan-wind-streams.wind-fast .wind-line {
  animation-duration: 0.7s;
}

/* Đám mây giấc mơ */
.dream-cloud-shape {
  animation: cloud-float 4s ease-in-out infinite alternate;
  transform-origin: 35px 0px;
}

@keyframes cloud-float {
  0% { transform: translateY(0) rotate(-1deg); }
  100% { transform: translateY(-6px) rotate(1.5deg); }
}

/* Con muỗi bay vòng vòng */
.mosquito-character {
  animation: mosquito-fly 7s ease-in-out infinite;
  cursor: crosshair;
}

@keyframes mosquito-fly {
  0% { transform: translate(520px, 220px); }
  20% { transform: translate(560px, 180px); }
  40% { transform: translate(490px, 200px); }
  60% { transform: translate(620px, 240px); }
  80% { transform: translate(540px, 260px); }
  100% { transform: translate(520px, 220px); }
}

.mosquito-wing-l, .mosquito-wing-r {
  animation: wing-beat 0.08s ease-in-out infinite alternate;
}

@keyframes wing-beat {
  from { transform: scaleY(1); }
  to { transform: scaleY(-0.3); }
}

/* Vết đập muỗi */
.slap-splash {
  animation: slap-pop 0.6s cubic-bezier(0.175, 0.885, 0.32, 1.275) both;
}

@keyframes slap-pop {
  0% { transform: scale(0.2); opacity: 0; }
  50% { transform: scale(1.3); opacity: 1; }
  100% { transform: scale(1); opacity: 0.9; }
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
  0% { opacity: 0; transform: translate(420px, 160px) scale(0.2); }
  100% { opacity: 1; transform: translate(420px, 140px) scale(1); }
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

/* Hộp thoại Boss Mèo */
.cat-speech-bubble {
  position: absolute;
  bottom: 80px;
  left: 30px;
  background: #fff7ed;
  color: #9a3412;
  padding: 8px 14px;
  border-radius: 12px;
  box-shadow: 0 8px 20px -4px rgba(0, 0, 0, 0.25);
  font-size: 13px;
  font-weight: 600;
  z-index: 10;
  border: 1.5px solid #f97316;
}

.cat-speech-bubble::after {
  content: '';
  position: absolute;
  bottom: -8px;
  left: 20px;
  border-width: 8px 8px 0;
  border-style: solid;
  border-color: #fff7ed transparent;
}

.stickman-hint {
  position: absolute;
  bottom: 10px;
  left: 50%;
  transform: translateX(-50%);
  background: rgba(15, 23, 42, 0.85);
  backdrop-filter: blur(6px);
  padding: 6px 16px;
  border-radius: 20px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  font-size: 11.5px;
  color: #cbd5e1;
  display: flex;
  align-items: center;
  gap: 8px;
  pointer-events: none;
  white-space: nowrap;
}

.hint-sep {
  color: #475569;
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
