<script setup lang="ts">
import { computed } from 'vue'
import type { FanSpeed, LunchDream } from '../composables/useLunchBreak'

const props = withDefaults(
  defineProps<{
    poked?: boolean
    quote?: string
    activeSpeaker?: 'A' | 'B' | 'C' | 'D' | 'cat' | 'dog' | 'lizard' | ''
    isLightsDimmed?: boolean
    fanSpeed?: FanSpeed
    catPetCount?: number
    catPurrText?: string
    dogPetCount?: number
    dogBarkText?: string
    currentDream?: LunchDream
    isMosquitoAlive?: boolean
    lastSlapPos?: { x: number; y: number } | null
    stampCount?: number
    waterBubbleClicks?: number
  }>(),
  {
    poked: false,
    quote: '',
    activeSpeaker: '',
    isLightsDimmed: false,
    fanSpeed: 1,
    catPetCount: 0,
    catPurrText: '',
    dogPetCount: 0,
    dogBarkText: '',
    currentDream: undefined,
    isMosquitoAlive: true,
    lastSlapPos: null,
    stampCount: 0,
    waterBubbleClicks: 0,
  },
)

const emit = defineEmits<{
  (e: 'pokeWorker', character: 'A' | 'B' | 'C' | 'D'): void
  (e: 'poke'): void
  (e: 'petCat'): void
  (e: 'petDog'): void
  (e: 'clickLizard'): void
  (e: 'clickWaterDispenser'): void
  (e: 'rustlePlant'): void
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
  emit('poke')
}

function onMosquitoClick(e: MouseEvent) {
  e.stopPropagation()
  const svg = (e.currentTarget as SVGElement).ownerSVGElement
  if (!svg) {
    emit('slapMosquito', { x: 620, y: 220 })
    return
  }
  const rect = svg.getBoundingClientRect()
  const x = ((e.clientX - rect.left) / rect.width) * 1100
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
      viewBox="0 0 1100 480"
      class="stickman-svg"
      xmlns="http://www.w3.org/2000/svg"
      role="img"
      aria-label="Toàn cảnh văn phòng STP 3D giờ nghỉ trưa với 4 nhân viên, cún Corgi và boss mèo"
    >
      <defs>
        <!-- ==================== 3D SPHERICAL & CAPSULE GRADIENTS ==================== -->
        
        <!-- Đầu 3D sứ mịn bóng (Porcelain 3D Sphere) -->
        <radialGradient id="headSphere3D" cx="32%" cy="26%" r="68%">
          <stop offset="0%" stop-color="#ffffff" />
          <stop offset="30%" stop-color="#f8fafc" />
          <stop offset="70%" stop-color="#cbd5e1" />
          <stop offset="92%" stop-color="#94a3b8" />
          <stop offset="100%" stop-color="#64748b" />
        </radialGradient>

        <!-- Khối chi 3D dạng trụ ống (Capsule 3D Limb) -->
        <linearGradient id="limb3D" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#ffffff" />
          <stop offset="25%" stop-color="#f8fafc" />
          <stop offset="65%" stop-color="#cbd5e1" />
          <stop offset="100%" stop-color="#94a3b8" />
        </linearGradient>

        <!-- Khớp nối cầu 3D (Ball Joint 3D) -->
        <radialGradient id="joint3D" cx="30%" cy="30%" r="70%">
          <stop offset="0%" stop-color="#ffffff" />
          <stop offset="50%" stop-color="#e2e8f0" />
          <stop offset="100%" stop-color="#94a3b8" />
        </radialGradient>

        <!-- Bong bóng mũi 3D lóng lánh (Iridescent Snot Bubble) -->
        <radialGradient id="snotBubble3D" cx="30%" cy="25%" r="75%">
          <stop offset="0%" stop-color="#ffffff" stop-opacity="0.95" />
          <stop offset="25%" stop-color="#a5f3fc" stop-opacity="0.75" />
          <stop offset="65%" stop-color="#38bdf8" stop-opacity="0.5" />
          <stop offset="90%" stop-color="#0284c7" stop-opacity="0.65" />
          <stop offset="100%" stop-color="#0369a1" stop-opacity="0.85" />
        </radialGradient>

        <!-- Giọt nước miếng 3D căng bóng -->
        <linearGradient id="drool3D" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stop-color="#e0f2fe" stop-opacity="0.9" />
          <stop offset="35%" stop-color="#ffffff" stop-opacity="0.95" />
          <stop offset="80%" stop-color="#38bdf8" stop-opacity="0.75" />
          <stop offset="100%" stop-color="#0284c7" stop-opacity="0.9" />
        </linearGradient>

        <!-- Lông cún Corgi 3D mượt mà -->
        <linearGradient id="corgiGold3D" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#fde047" />
          <stop offset="30%" stop-color="#f59e0b" />
          <stop offset="80%" stop-color="#d97706" />
          <stop offset="100%" stop-color="#b45309" />
        </linearGradient>

        <!-- Vùng ngực trắng Corgi -->
        <linearGradient id="corgiWhite3D" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#ffffff" />
          <stop offset="70%" stop-color="#f1f5f9" />
          <stop offset="100%" stop-color="#cbd5e1" />
        </linearGradient>

        <!-- Thảm nằm dệt vải của Corgi -->
        <radialGradient id="corgiRugGrad" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stop-color="#e0e7ff" />
          <stop offset="65%" stop-color="#c7d2fe" />
          <stop offset="100%" stop-color="#818cf8" />
        </radialGradient>

        <!-- Dép tổ ong 3D cao su đúc -->
        <linearGradient id="sandal3D" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#fef08a" />
          <stop offset="35%" stop-color="#facc15" />
          <stop offset="85%" stop-color="#ca8a04" />
          <stop offset="100%" stop-color="#854d0e" />
        </linearGradient>

        <!-- Môi trường & nội thất -->
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

        <linearGradient id="archiveDeskGrad" x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stop-color="#c2410c" />
          <stop offset="100%" stop-color="#9a3412" />
        </linearGradient>

        <linearGradient id="sofaGrad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#4f46e5" />
          <stop offset="100%" stop-color="#3730a3" />
        </linearGradient>

        <linearGradient id="screenGrad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#38bdf8" stop-opacity="0.8" />
          <stop offset="100%" stop-color="#0284c7" stop-opacity="0.3" />
        </linearGradient>

        <linearGradient id="waterGrad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#38bdf8" stop-opacity="0.85" />
          <stop offset="100%" stop-color="#0284c7" stop-opacity="0.65" />
        </linearGradient>

        <linearGradient id="lampConeGrad" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#fef08a" stop-opacity="0.7" />
          <stop offset="100%" stop-color="#fef08a" stop-opacity="0" />
        </linearGradient>

        <linearGradient id="moonlightCone" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#bae6fd" stop-opacity="0.35" />
          <stop offset="100%" stop-color="#bae6fd" stop-opacity="0" />
        </linearGradient>

        <linearGradient id="dreamGrad" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stop-color="#eff6ff" />
          <stop offset="100%" stop-color="#dbeafe" />
        </linearGradient>

        <!-- Filters 3D Soft Shadow -->
        <filter id="softShadow" x="-20%" y="-20%" width="140%" height="140%">
          <feDropShadow dx="0" dy="4" stdDeviation="3" flood-opacity="0.35" />
        </filter>

        <filter id="glow" x="-20%" y="-20%" width="140%" height="140%">
          <feGaussianBlur stdDeviation="3.5" result="blur" />
          <feComposite in="SourceGraphic" in2="blur" operator="over" />
        </filter>

        <filter id="slapGlow" x="-30%" y="-30%" width="160%" height="160%">
          <feGaussianBlur stdDeviation="4" result="blur" />
          <feComposite in="SourceGraphic" in2="blur" operator="over" />
        </filter>
      </defs>

      <!-- Tường & Sàn phòng -->
      <rect x="0" y="0" width="1100" height="350" fill="url(#wallGrad)" />
      <rect x="0" y="350" width="1100" height="130" fill="url(#floorGrad)" />
      <line x1="0" y1="350" x2="1100" y2="350" stroke="#475569" stroke-width="2" />

      <!-- Cửa sổ nhìn ra trời trưa yên ả -->
      <g id="window-group">
        <rect x="475" y="35" width="150" height="115" rx="8" fill="#090d16" stroke="#475569" stroke-width="3" />
        <path d="M550 35 L550 150 M475 92 L625 92" stroke="#334155" stroke-width="2" />
        <!-- Trăng khuyết & sao trưa -->
        <circle cx="590" cy="65" r="16" fill="#fbbf24" opacity="0.85" filter="url(#glow)" />
        <circle cx="596" cy="62" r="15" fill="#090d16" />
        <circle cx="505" cy="60" r="1.5" fill="#f8fafc" opacity="0.7" />
        <circle cx="525" cy="75" r="2" fill="#f8fafc" opacity="0.8" />
        <circle cx="565" cy="115" r="1.5" fill="#f8fafc" opacity="0.5" />
        <path d="M475 35 Q495 90 482 150 L475 150 Z" fill="#64748b" opacity="0.7" />
        <path d="M625 35 Q605 90 618 150 L625 150 Z" fill="#64748b" opacity="0.7" />
        <polygon v-if="isLightsDimmed" points="490,150 610,150 680,350 420,350" fill="url(#moonlightCone)" />
      </g>

      <!-- CHÚ THẠCH SÙNG TRÊN TƯỜNG -->
      <g
        id="wall-lizard"
        class="interactive-element lizard-group"
        transform="translate(650, 75)"
        @click.stop="emit('clickLizard')"
      >
        <title>Chú thạch sùng văn phòng - Bấm để nghe tặc lưỡi chắt chắt!</title>
        <ellipse cx="15" cy="10" rx="10" ry="4" fill="#94a3b8" />
        <circle cx="26" cy="8" r="3.5" fill="#94a3b8" />
        <circle cx="27" cy="7" r="1" fill="#0f172a" />
        <line x1="8" y1="6" x2="3" y2="2" stroke="#94a3b8" stroke-width="1.8" stroke-linecap="round" />
        <line x1="8" y1="14" x2="3" y2="18" stroke="#94a3b8" stroke-width="1.8" stroke-linecap="round" />
        <line x1="20" y1="6" x2="25" y2="2" stroke="#94a3b8" stroke-width="1.8" stroke-linecap="round" />
        <line x1="20" y1="14" x2="25" y2="18" stroke="#94a3b8" stroke-width="1.8" stroke-linecap="round" />
        <path d="M5 10 Q-5 6 -12 12" stroke="#94a3b8" stroke-width="2" fill="none" stroke-linecap="round" class="lizard-tail" />
      </g>

      <!-- Ghi chú trên tường -->
      <g transform="translate(930, 45) rotate(4)">
        <rect x="0" y="0" width="105" height="80" rx="3" fill="#fef08a" />
        <text x="8" y="20" font-size="10" font-weight="bold" fill="#854d0e">LƯU Ý 12H:</text>
        <text x="8" y="36" font-size="9" fill="#a16207">✓ Cơm sườn 🍱</text>
        <text x="8" y="50" font-size="9" fill="#a16207">✓ Ngủ lăn lóc 💤</text>
        <text x="8" y="64" font-size="9" fill="#a16207">✓ Cấm nhập liệu!</text>
      </g>

      <g transform="translate(230, 45) rotate(-3)">
        <rect x="0" y="0" width="105" height="80" rx="3" fill="#fed7aa" />
        <text x="8" y="20" font-size="10" font-weight="bold" fill="#9a3412">HỒ SƠ STP:</text>
        <text x="8" y="36" font-size="9" fill="#c2410c">Sổ Kết Hôn 1999 📑</text>
        <text x="8" y="50" font-size="9" fill="#c2410c">Trích yếu 67k bản</text>
        <text x="8" y="64" font-size="9" fill="#c2410c">13h dậy ký tiếp!</text>
      </g>

      <!-- Đồng hồ chỉ 12h -->
      <g transform="translate(390, 75)">
        <circle cx="0" cy="0" r="30" fill="#1e293b" stroke="#e2e8f0" stroke-width="3" />
        <circle cx="0" cy="0" r="26" fill="#0f172a" />
        <text x="-4" y="-17" font-size="8" font-weight="bold" fill="#94a3b8">12</text>
        <text x="16" y="3" font-size="8" font-weight="bold" fill="#94a3b8">3</text>
        <text x="-3" y="21" font-size="8" font-weight="bold" fill="#94a3b8">6</text>
        <text x="-22" y="3" font-size="8" font-weight="bold" fill="#94a3b8">9</text>
        <line x1="0" y1="0" x2="0" y2="-15" stroke="#f8fafc" stroke-width="2.5" stroke-linecap="round" />
        <line x1="0" y1="0" x2="6" y2="-17" stroke="#38bdf8" stroke-width="2" stroke-linecap="round" class="clock-minute-hand" />
        <circle cx="0" cy="0" r="3" fill="#f8fafc" />
      </g>

      <!-- ==================== GÓC TRÁI: TỦ HỒ SƠ & BÌNH NƯỚC ==================== -->
      <g id="archive-cabinet" transform="translate(15, 140)">
        <rect x="0" y="0" width="70" height="270" rx="3" fill="#475569" stroke="#334155" stroke-width="2" />
        <rect x="5" y="8" width="60" height="58" rx="2" fill="#64748b" stroke="#334155" stroke-width="1.5" />
        <rect x="18" y="22" width="34" height="14" rx="2" fill="#ffffff" />
        <text x="21" y="32" font-size="7" font-weight="bold" fill="#1e293b">1990-1995</text>
        <circle cx="35" cy="46" r="3" fill="#334155" />

        <rect x="5" y="72" width="60" height="58" rx="2" fill="#64748b" stroke="#334155" stroke-width="1.5" />
        <rect x="14" y="86" width="42" height="14" rx="2" fill="#fef08a" />
        <text x="16" y="96" font-size="6.5" font-weight="bold" fill="#854d0e">KẾT HÔN 1999</text>
        <circle cx="35" cy="110" r="3" fill="#334155" />

        <rect x="5" y="136" width="60" height="58" rx="2" fill="#64748b" stroke="#334155" stroke-width="1.5" />
        <rect x="15" y="150" width="40" height="14" rx="2" fill="#bae6fd" />
        <text x="18" y="160" font-size="6.5" font-weight="bold" fill="#0369a1">KHAI SINH</text>
        <circle cx="35" cy="174" r="3" fill="#334155" />

        <rect x="5" y="200" width="60" height="58" rx="2" fill="#64748b" stroke="#334155" stroke-width="1.5" />
        <polygon points="12,215 35,205 38,225 15,230" fill="#f8fafc" />
        <polygon points="30,212 52,204 55,222 33,226" fill="#fef9c3" />
        <rect x="22" y="235" width="26" height="4" rx="2" fill="#1e293b" />
      </g>

      <!-- Bình nước nóng lạnh 20L -->
      <g
        id="water-dispenser"
        class="interactive-element"
        transform="translate(95, 245)"
        @click.stop="emit('clickWaterDispenser')"
      >
        <ellipse cx="20" cy="5" rx="14" ry="4" fill="#38bdf8" />
        <path d="M6 5 Q5 -30 20 -35 Q35 -30 34 5 Z" fill="url(#waterGrad)" stroke="#0284c7" stroke-width="1.2" />
        <circle cx="18" cy="-10" r="2.5" fill="#ffffff" opacity="0.8" class="water-bubble-1" />
        <circle cx="23" cy="-20" r="2" fill="#ffffff" opacity="0.8" class="water-bubble-2" />
        <circle cx="15" cy="-28" r="1.5" fill="#ffffff" opacity="0.7" class="water-bubble-3" />
        <rect x="4" y="5" width="32" height="100" rx="3" fill="#f1f5f9" stroke="#cbd5e1" stroke-width="1.5" />
        <rect x="8" y="45" width="24" height="22" rx="2" fill="#e2e8f0" stroke="#94a3b8" stroke-width="1" />
        <rect x="12" y="42" width="4" height="6" fill="#ef4444" rx="1" />
        <rect x="24" y="42" width="4" height="6" fill="#38bdf8" rx="1" />
        <polygon points="17,56 23,56 22,64 18,64" fill="#ffffff" stroke="#cbd5e1" stroke-width="0.8" />
        <text x="20" y="85" font-size="6" font-weight="bold" fill="#64748b" text-anchor="middle">STP 20L</text>
      </g>

      <!-- ==================== GÓC SỔ SÁCH HỘ TỊCH: CHUYÊN VIÊN C (3D RENDER) ==================== -->
      <g
        id="stickman-archive-worker"
        class="interactive-element"
        @click.stop="emit('pokeWorker', 'C')"
      >
        <title>Chuyên viên C (Chiến thần sổ sách) - Gục ngã giữa đống hồ sơ 1999</title>
        <g id="archive-desk">
          <rect x="155" y="320" width="10" height="90" rx="3" fill="#94a3b8" />
          <rect x="345" y="320" width="10" height="90" rx="3" fill="#94a3b8" />
          <rect x="145" y="305" width="215" height="18" rx="4" fill="url(#archiveDeskGrad)" stroke="#7c2d12" stroke-width="1.5" />

          <!-- Chồng hồ sơ bên trái -->
          <g transform="translate(150, 245)">
            <rect x="0" y="50" width="40" height="10" rx="2" fill="#dc2626" stroke="#991b1b" stroke-width="1" />
            <text x="3" y="57" font-size="5" font-weight="bold" fill="#ffffff">ĐK KẾT HÔN 1999</text>
            <rect x="2" y="40" width="38" height="10" rx="2" fill="#2563eb" stroke="#1d4ed8" stroke-width="1" />
            <text x="5" y="47" font-size="5" font-weight="bold" fill="#ffffff">KHAI SINH 2000</text>
            <rect x="-1" y="30" width="41" height="10" rx="2" fill="#d97706" stroke="#b45309" stroke-width="1" />
            <text x="3" y="37" font-size="5" font-weight="bold" fill="#ffffff">HỘ TỊCH TẬP 1</text>
            <rect x="3" y="20" width="37" height="10" rx="2" fill="#16a34a" stroke="#15803d" stroke-width="1" />
            <text x="6" y="27" font-size="5" font-weight="bold" fill="#ffffff">TRÍCH YẾU STP</text>
            <rect x="1" y="10" width="39" height="10" rx="2" fill="#9333ea" stroke="#7e22ce" stroke-width="1" />
            <text x="4" y="17" font-size="5" font-weight="bold" fill="#ffffff">CHỨNG THỰC 98</text>
          </g>

          <!-- Chồng hồ sơ bên phải -->
          <g transform="translate(315, 235)">
            <rect x="0" y="60" width="42" height="10" rx="2" fill="#0284c7" stroke="#0369a1" stroke-width="1" />
            <rect x="2" y="50" width="40" height="10" rx="2" fill="#e11d48" stroke="#be123c" stroke-width="1" />
            <rect x="-2" y="40" width="43" height="10" rx="2" fill="#ca8a04" stroke="#a16207" stroke-width="1" />
            <rect x="4" y="30" width="38" height="10" rx="2" fill="#475569" stroke="#334155" stroke-width="1" />
            <rect x="1" y="20" width="40" height="10" rx="2" fill="#059669" stroke="#047857" stroke-width="1" />
            <line x1="30" y1="12" x2="36" y2="22" stroke="#2563eb" stroke-width="2.5" stroke-linecap="round" />
          </g>

          <!-- Cuốn sổ cái hộ tịch dày cộp làm gối -->
          <path d="M210 305 L245 298 L285 305 L285 308 L210 308 Z" fill="#fef08a" stroke="#ca8a04" stroke-width="1.2" />
          <line x1="245" y1="298" x2="245" y2="308" stroke="#ca8a04" stroke-width="1.5" />

          <!-- Vết mực đỏ đóng dấu -->
          <g v-if="stampCount > 0" transform="translate(290, 298)">
            <circle cx="0" cy="0" r="9" fill="none" stroke="#ef4444" stroke-width="1.5" stroke-dasharray="2 1" />
            <text x="0" y="-1" font-size="5" font-weight="900" fill="#ef4444" text-anchor="middle">ĐÃ DUYỆT</text>
            <text x="0" y="5" font-size="4" font-weight="bold" fill="#ef4444" text-anchor="middle">STP 1999</text>
          </g>

          <polygon points="190,385 205,375 215,390 200,400" fill="#f8fafc" stroke="#cbd5e1" stroke-width="1" />
          <polygon points="270,395 285,390 292,408 277,412" fill="#fef08a" stroke="#fde047" stroke-width="1" />
        </g>

        <!-- Thân Chuyên viên C 3D -->
        <g class="archive-sleeper-body" :class="{ 'is-startled': poked && activeSpeaker === 'C' }">
          <line x1="250" y1="330" x2="240" y2="385" stroke="url(#limb3D)" stroke-width="6" stroke-linecap="round" filter="url(#softShadow)" />
          <line x1="240" y1="385" x2="255" y2="410" stroke="url(#limb3D)" stroke-width="5" stroke-linecap="round" />
          <path d="M250 330 Q235 285 225 295" stroke="url(#limb3D)" stroke-width="7" stroke-linecap="round" fill="none" class="breathe-spine" filter="url(#softShadow)" />
          
          <path d="M230 295 Q215 302 205 302" stroke="url(#limb3D)" stroke-width="5.5" stroke-linecap="round" fill="none" />
          <path d="M235 295 Q270 295 285 305" stroke="url(#limb3D)" stroke-width="5.5" stroke-linecap="round" fill="none" />
          
          <!-- Con dấu đỏ 3D trong tay -->
          <rect x="282" y="298" width="8" height="12" rx="2" fill="#ef4444" stroke="#b91c1c" stroke-width="1" />
          <circle cx="286" cy="296" r="3" fill="#b91c1c" />

          <!-- Đầu người que C dạng khối cầu 3D có đổ bóng -->
          <circle cx="230" cy="276" r="16" fill="url(#headSphere3D)" stroke="#94a3b8" stroke-width="1" filter="url(#softShadow)" />
          <!-- Má hồng mệt mỏi -->
          <circle cx="224" cy="282" r="3.5" fill="#fda4af" opacity="0.6" />
          
          <!-- Cặp kính cận kim loại 3D trễ sống mũi -->
          <circle cx="223" cy="278" r="4.5" fill="none" stroke="#2563eb" stroke-width="1.8" />
          <circle cx="232" cy="278" r="4.5" fill="none" stroke="#2563eb" stroke-width="1.8" />
          <line x1="227.5" y1="278" x2="227.5" y2="278" stroke="#2563eb" stroke-width="1.8" />
          <path d="M221 278 Q223 281 225 278" stroke="#0f172a" stroke-width="1.4" fill="none" />
          <path d="M227 285 Q230 287 234 285" stroke="#0f172a" stroke-width="1.4" fill="none" />

          <g class="archive-zzz">
            <text x="210" y="250" font-size="13" font-weight="bold" fill="#f59e0b" class="zzz-item zzz-1">z</text>
            <text x="198" y="230" font-size="17" font-weight="bold" fill="#fbbf24" class="zzz-item zzz-2">Z</text>
            <text x="180" y="205" font-size="22" font-weight="bold" fill="#fde047" class="zzz-item zzz-3">Zzz</text>
          </g>

          <g transform="translate(180, 175)">
            <rect x="0" y="0" width="85" height="20" rx="4" fill="#1e293b" stroke="#f59e0b" stroke-width="1.2" />
            <text x="6" y="13" font-size="8.5" font-weight="bold" fill="#fbbf24">Chuyên viên C (Sổ sách)</text>
          </g>
        </g>
      </g>

      <!-- ==================== GÓC GIỮA: CHUYÊN VIÊN A & GẦM BÀN CHUYÊN VIÊN D ==================== -->
      <g id="stickman-desk-area">
        <!-- Đèn bàn -->
        <g id="desk-lamp" class="interactive-element" @click.stop="emit('toggleLights')">
          <ellipse cx="435" cy="305" rx="10" ry="4" fill="#64748b" />
          <path d="M435 305 Q425 240 445 220" stroke="#94a3b8" stroke-width="3" fill="none" stroke-linecap="round" />
          <path d="M435 220 L465 210 L455 230 Z" fill="#f59e0b" stroke="#d97706" stroke-width="1.5" />
          <line x1="450" y1="230" x2="450" y2="245" stroke="#cbd5e1" stroke-width="1.5" />
          <circle cx="450" cy="246" r="2.5" fill="#f59e0b" />
          <polygon points="445,225 550,310 435,310" fill="url(#lampConeGrad)" />
        </g>

        <!-- Thùng máy CPU & Boss Mèo -->
        <g id="cpu-and-cat" class="interactive-element" @click.stop="emit('petCat')">
          <rect x="395" y="325" width="30" height="65" rx="3" fill="#1e293b" stroke="#475569" stroke-width="1.5" />
          <line x1="400" y1="335" x2="400" y2="380" stroke="#06b6d4" stroke-width="2" opacity="0.8" />
          <circle cx="418" cy="335" r="2" fill="#22c55e" />
          <circle cx="418" cy="342" r="1.5" fill="#ef4444" />

          <!-- BOSS MÈO CÔNG SỞ 3D -->
          <g id="office-cat" class="cat-sleeper">
            <ellipse cx="410" cy="320" rx="14" ry="10" fill="#f97316" stroke="#c2410c" stroke-width="1.2" class="cat-body" filter="url(#softShadow)" />
            <circle cx="401" cy="317" r="7" fill="#f97316" stroke="#c2410c" stroke-width="1.2" />
            <polygon points="396,312 398,306 402,312" fill="#ea580c" />
            <polygon points="401,312 404,306 407,312" fill="#ea580c" />
            <path d="M398 317 Q400 319 402 317" stroke="#7c2d12" stroke-width="1" fill="none" />
            <circle cx="397" cy="319" r="1" fill="#fbcfe8" />
            <path d="M423 322 Q428 316 427 328" stroke="#f97316" stroke-width="2.5" fill="none" stroke-linecap="round" class="cat-tail" />
            <text x="394" y="303" font-size="8" font-weight="bold" fill="#fdba74" class="cat-zzz">z</text>
          </g>

          <g v-if="catPetCount > 0" class="cat-heart-burst">
            <text x="405" y="295" font-size="12" fill="#ef4444" class="cat-heart">❤️</text>
          </g>
        </g>

        <!-- Bàn làm việc số 2 -->
        <g id="desk-furniture-2">
          <rect x="445" y="320" width="10" height="90" rx="3" fill="#94a3b8" />
          <rect x="625" y="320" width="10" height="90" rx="3" fill="#94a3b8" />
          <rect x="430" y="305" width="215" height="18" rx="4" fill="url(#deskGrad)" stroke="#78350f" stroke-width="1.5" />
          
          <path d="M460 305 L505 305 L508 275 L463 275 Z" fill="#475569" />
          <rect x="465" y="277" width="40" height="25" rx="2" fill="url(#screenGrad)" />
          <line x1="470" y1="285" x2="495" y2="285" stroke="#bae6fd" stroke-width="1" opacity="0.6" />
          <line x1="470" y1="290" x2="488" y2="290" stroke="#bae6fd" stroke-width="1" opacity="0.6" />
          
          <rect x="525" y="290" width="14" height="16" rx="2" fill="#f1f5f9" />
          <path d="M539 294 Q545 298 539 302" stroke="#f1f5f9" stroke-width="2" fill="none" />
          <text x="527" y="299" font-size="6" font-weight="bold" fill="#64748b">STP</text>
        </g>

        <!-- ==================== GẦM BÀN: CHUYÊN VIÊN D ==================== -->
        <g
          id="stickman-under-desk"
          class="interactive-element under-desk-group"
          @click.stop="emit('pokeWorker', 'D')"
        >
          <title>Chuyên viên D (Ẩn sĩ gầm bàn) - Trùm áo khoác kín mít, chân đi tất lệch màu</title>
          <rect x="465" y="395" width="150" height="22" rx="3" fill="#15803d" opacity="0.85" stroke="#166534" stroke-width="1" />
          <line x1="475" y1="395" x2="475" y2="417" stroke="#fbbf24" stroke-width="1" stroke-dasharray="2 2" />
          <line x1="535" y1="395" x2="535" y2="417" stroke="#fbbf24" stroke-width="1" stroke-dasharray="2 2" />
          <line x1="595" y1="395" x2="595" y2="417" stroke="#fbbf24" stroke-width="1" stroke-dasharray="2 2" />

          <ellipse cx="485" cy="402" rx="14" ry="8" fill="#475569" stroke="#334155" stroke-width="1" />

          <!-- Áo phao kén tằm 3D có các múi chần bông phồng -->
          <ellipse cx="525" cy="400" rx="32" ry="12" fill="#1e3a5f" stroke="#0f2744" stroke-width="1.5" class="cocoon-body" filter="url(#softShadow)" />
          <path d="M505 392 Q520 397 535 392" stroke="#38bdf8" stroke-width="1.5" fill="none" opacity="0.8" />
          <path d="M515 408 Q530 403 545 408" stroke="#38bdf8" stroke-width="1.5" fill="none" opacity="0.8" />

          <!-- 2 Bàn chân đi tất cọc cạch 3D -->
          <g transform="translate(565, 395)" class="toes-wiggle" filter="url(#softShadow)">
            <rect x="0" y="0" width="18" height="9" rx="4.5" fill="#0284c7" />
            <line x1="5" y1="0" x2="5" y2="9" stroke="#ffffff" stroke-width="1.5" />
            <line x1="11" y1="0" x2="11" y2="9" stroke="#ffffff" stroke-width="1.5" />
          </g>

          <g transform="translate(568, 406)" class="toes-wiggle-alt" filter="url(#softShadow)">
            <rect x="0" y="0" width="18" height="9" rx="4.5" fill="#f43f5e" />
            <circle cx="5" cy="4.5" r="1.5" fill="#ffffff" />
            <circle cx="12" cy="4.5" r="1.5" fill="#ffffff" />
          </g>

          <text x="500" y="390" font-size="11" font-weight="bold" fill="#38bdf8" class="zzz-item zzz-1">z</text>
          <text x="515" y="380" font-size="14" font-weight="bold" fill="#60a5fa" class="zzz-item zzz-2">Z</text>

          <g transform="translate(485, 420)">
            <rect x="0" y="0" width="80" height="15" rx="3" fill="#0f172a" stroke="#38bdf8" stroke-width="1" />
            <text x="5" y="11" font-size="7.5" font-weight="bold" fill="#38bdf8">Chuyên viên D (Gầm bàn)</text>
          </g>
        </g>

        <!-- ==================== CHUYÊN VIÊN A (3D RENDER) ==================== -->
        <g
          class="desk-sleeper-body interactive-element"
          :class="{ 'is-startled': poked && activeSpeaker === 'A' }"
          @click.stop="emit('pokeWorker', 'A')"
        >
          <title>Chuyên viên A (Chăm chỉ) - Gục ngủ trên laptop, bong bóng mũi phồng to</title>
          <path d="M575 270 Q560 270 560 330 L605 330 Q605 270 575 270" fill="#334155" opacity="0.8" />
          <rect x="578" y="330" width="8" height="60" fill="#64748b" />
          <line x1="582" y1="390" x2="555" y2="405" stroke="#475569" stroke-width="4" stroke-linecap="round" />
          <line x1="582" y1="390" x2="610" y2="405" stroke="#475569" stroke-width="4" stroke-linecap="round" />

          <!-- Thân 3D & Chân -->
          <line x1="580" y1="330" x2="570" y2="385" stroke="url(#limb3D)" stroke-width="6" stroke-linecap="round" filter="url(#softShadow)" />
          <line x1="570" y1="385" x2="585" y2="410" stroke="url(#limb3D)" stroke-width="5" stroke-linecap="round" />
          <path d="M580 330 Q565 285 550 295" stroke="url(#limb3D)" stroke-width="7" stroke-linecap="round" fill="none" class="breathe-spine" filter="url(#softShadow)" />
          <path d="M555 292 Q545 305 530 305 L570 305" stroke="url(#limb3D)" stroke-width="5.5" stroke-linecap="round" fill="none" />
          
          <!-- Cà vạt xanh navy 3D thõng trên mặt bàn -->
          <polygon points="548,294 553,294 556,308 550,314 545,308" fill="#1e3a8a" stroke="#1d4ed8" stroke-width="0.8" />

          <!-- Đầu khối cầu 3D có đổ bóng -->
          <circle cx="550" cy="278" r="16" fill="url(#headSphere3D)" stroke="#94a3b8" stroke-width="1" class="desk-head" filter="url(#softShadow)" />
          <!-- Má hồng ngủ say -->
          <circle cx="543" cy="283" r="3.5" fill="#fda4af" opacity="0.65" />
          <path d="M542 277 Q546 281 550 277" stroke="#0f172a" stroke-width="1.8" fill="none" stroke-linecap="round" />
          <path d="M544 285 Q548 288 553 285" stroke="#0f172a" stroke-width="1.8" fill="none" stroke-linecap="round" />

          <!-- Bong bóng mũi 3D lóng lánh (Snot Bubble) -->
          <circle cx="541" cy="281" r="5" fill="url(#snotBubble3D)" stroke="#0284c7" stroke-width="0.8" class="snot-bubble" />
          <circle cx="539.5" cy="279.5" r="1.5" fill="#ffffff" opacity="0.9" />

          <g class="desk-zzz-group">
            <text x="530" y="255" font-size="14" font-weight="bold" fill="#38bdf8" class="zzz-item zzz-1">z</text>
            <text x="520" y="235" font-size="18" font-weight="bold" fill="#60a5fa" class="zzz-item zzz-2">Z</text>
            <text x="505" y="210" font-size="24" font-weight="bold" fill="#818cf8" class="zzz-item zzz-3">Zzz</text>
          </g>

          <g transform="translate(565, 235)">
            <rect x="0" y="0" width="80" height="22" rx="4" fill="#1e293b" stroke="#38bdf8" stroke-width="1.2" />
            <text x="6" y="14" font-size="9" font-weight="bold" fill="#38bdf8">Chuyên viên A</text>
          </g>
        </g>
      </g>

      <!-- ==================== CÂY KIM TIỀN & QUẠT MÁY (x = 670..770) ==================== -->
      <g
        id="money-plant"
        class="interactive-element plant-group"
        transform="translate(670, 290)"
        @click.stop="emit('rustlePlant')"
      >
        <polygon points="10,85 30,85 34,120 6,120" fill="#f8fafc" stroke="#cbd5e1" stroke-width="1.5" />
        <ellipse cx="20" cy="85" rx="10" ry="3" fill="#78350f" />
        <path d="M20 85 Q12 60 5 45" stroke="#15803d" stroke-width="3" fill="none" class="leaf-stem-1" />
        <ellipse cx="5" cy="45" rx="8" ry="5" fill="#22c55e" stroke="#16a34a" stroke-width="1" transform="rotate(-30 5 45)" />
        <ellipse cx="10" cy="60" rx="7" ry="4" fill="#22c55e" stroke="#16a34a" stroke-width="1" transform="rotate(-25 10 60)" />
        <path d="M20 85 Q20 50 20 35" stroke="#15803d" stroke-width="3" fill="none" class="leaf-stem-2" />
        <ellipse cx="20" cy="35" rx="8" ry="5" fill="#4ade80" stroke="#22c55e" stroke-width="1" />
        <ellipse cx="20" cy="55" rx="7" ry="4.5" fill="#4ade80" stroke="#22c55e" stroke-width="1" />
        <path d="M20 85 Q28 60 35 48" stroke="#15803d" stroke-width="3" fill="none" class="leaf-stem-3" />
        <ellipse cx="35" cy="48" rx="8" ry="5" fill="#16a34a" stroke="#15803d" stroke-width="1" transform="rotate(30 35 48)" />

        <g transform="translate(18, 42)">
          <ellipse cx="0" cy="0" rx="3.5" ry="4" fill="#ef4444" />
          <circle cx="0" cy="-3.5" r="2" fill="#0f172a" />
          <line x1="0" y1="-2" x2="0" y2="4" stroke="#0f172a" stroke-width="0.8" />
          <circle cx="-1.5" cy="0" r="0.7" fill="#0f172a" />
          <circle cx="1.5" cy="0" r="0.7" fill="#0f172a" />
        </g>
      </g>

      <!-- Quạt máy -->
      <g
        id="electric-fan"
        class="interactive-element"
        transform="translate(730, 275)"
        @click.stop="emit('cycleFan')"
      >
        <ellipse cx="20" cy="115" rx="18" ry="7" fill="#475569" stroke="#334155" stroke-width="1.5" />
        <rect x="18" y="45" width="4" height="70" fill="#94a3b8" />
        <circle cx="20" cy="45" r="9" fill="#334155" stroke="#1e293b" stroke-width="1.5" />
        
        <g class="fan-head-oscillation" :class="fanSpeedClass">
          <circle cx="20" cy="45" r="28" fill="none" stroke="#64748b" stroke-width="2" />
          <circle cx="20" cy="45" r="24" fill="none" stroke="#94a3b8" stroke-width="0.8" stroke-dasharray="2 3" />
          <line x1="20" y1="17" x2="20" y2="73" stroke="#64748b" stroke-width="1.2" />
          <line x1="-8" y1="45" x2="48" y2="45" stroke="#64748b" stroke-width="1.2" />

          <g class="fan-blades-group" :class="fanSpeedClass">
            <path d="M20 45 Q32 30 30 18 Q20 28 20 45 Z" fill="#38bdf8" opacity="0.85" />
            <path d="M20 45 Q35 55 45 60 Q32 60 20 45 Z" fill="#38bdf8" opacity="0.85" />
            <path d="M20 45 Q5 55 -5 50 Q10 40 20 45 Z" fill="#38bdf8" opacity="0.85" />
            <circle cx="20" cy="45" r="4" fill="#0284c7" />
          </g>

          <g v-if="fanSpeed > 0" class="fan-wind-streams" :class="{ 'wind-fast': fanSpeed === 2 }">
            <path d="M48 35 Q100 20 180 30" stroke="#38bdf8" stroke-width="1.5" stroke-dasharray="8 6" fill="none" class="wind-line wind-1" />
            <path d="M48 48 Q120 50 200 45" stroke="#38bdf8" stroke-width="1.8" stroke-dasharray="10 8" fill="none" class="wind-line wind-2" />
            <path d="M48 60 Q90 75 160 70" stroke="#38bdf8" stroke-width="1.5" stroke-dasharray="8 6" fill="none" class="wind-line wind-3" />
          </g>
        </g>

        <g transform="translate(11, 102)">
          <rect x="0" y="0" width="18" height="10" rx="2" fill="#1e293b" />
          <circle cx="5" cy="5" r="2.5" :fill="fanSpeed === 0 ? '#ef4444' : '#64748b'" />
          <circle cx="9" cy="5" r="2.5" :fill="fanSpeed === 1 ? '#22c55e' : '#64748b'" />
          <circle cx="13" cy="5" r="2.5" :fill="fanSpeed === 2 ? '#38bdf8' : '#64748b'" />
        </g>
      </g>

      <!-- ==================== CHÚ CÚN CORGI VĂN PHÒNG (3D SPLOOT POSTURE) ==================== -->
      <g
        id="office-corgi-dog"
        class="interactive-element dog-sleeper-group"
        transform="translate(685, 380)"
        @click.stop="emit('petDog')"
      >
        <title>Chú cún Corgi văn phòng - Nằm xoạc chân (sploot) ngủ phè phỡn. Bấm để vuốt ve!</title>
        <!-- Thảm tròn dệt vải của Corgi -->
        <ellipse cx="38" cy="22" rx="44" ry="18" fill="url(#corgiRugGrad)" stroke="#6366f1" stroke-width="1.5" stroke-dasharray="3 2" filter="url(#softShadow)" />

        <!-- Hai chân sau xoạc ra sau (Sploot legs) -->
        <g transform="translate(56, 12)" class="dog-back-legs">
          <ellipse cx="10" cy="2" rx="10" ry="4.5" fill="url(#corgiGold3D)" transform="rotate(25 10 2)" />
          <circle cx="18" cy="6" r="2.5" fill="#fbcfe8" />
        </g>
        <g transform="translate(56, 26)" class="dog-back-legs">
          <ellipse cx="10" cy="2" rx="10" ry="4.5" fill="url(#corgiGold3D)" transform="rotate(-25 10 2)" />
          <circle cx="18" cy="-2" r="2.5" fill="#fbcfe8" />
        </g>

        <!-- Thân cún béo tròn 3D nhấp nhô theo nhịp thở -->
        <ellipse cx="38" cy="18" rx="22" ry="13" fill="url(#corgiGold3D)" class="dog-body" filter="url(#softShadow)" />
        <ellipse cx="28" cy="19" rx="14" ry="8" fill="url(#corgiWhite3D)" />

        <!-- Hai chân trước duỗi thẳng -->
        <ellipse cx="14" cy="11" rx="8" ry="4" fill="url(#corgiWhite3D)" />
        <ellipse cx="14" cy="25" rx="8" ry="4" fill="url(#corgiWhite3D)" />

        <!-- Đầu cún Corgi gối lên chân trước -->
        <g class="dog-head-group">
          <circle cx="16" cy="18" r="11" fill="url(#corgiGold3D)" filter="url(#softShadow)" />
          <!-- Vệt lông trắng giữa trán xuống mõm -->
          <path d="M12 9 Q16 11 16 25 Q12 25 12 9" fill="url(#corgiWhite3D)" />
          
          <!-- Mũi cún đen bóng có chấm sáng phản chiếu -->
          <ellipse cx="6" cy="18" rx="2.5" ry="2" fill="#0f172a" />
          <circle cx="5.5" cy="17.2" r="0.7" fill="#ffffff" />

          <!-- Mắt nhắm hí hí ngủ say -->
          <path d="M13 13 Q16 15 18 13" stroke="#78350f" stroke-width="1.4" fill="none" stroke-linecap="round" />
          <path d="M13 23 Q16 25 18 23" stroke="#78350f" stroke-width="1.4" fill="none" stroke-linecap="round" />

          <!-- Chiếc lưỡi hồng thè nhẹ ra ngoài (Plep) -->
          <ellipse cx="5" cy="20.5" rx="3" ry="2" fill="#f43f5e" />

          <!-- Hai tai to vểnh sang hai bên mềm mại -->
          <path d="M14 8 Q18 0 24 6 Q20 11 15 9 Z" fill="url(#corgiGold3D)" />
          <polygon points="17,7 21,3 20,8" fill="#fda4af" />
          <path d="M14 28 Q18 36 24 30 Q20 25 15 27 Z" fill="url(#corgiGold3D)" />
          <polygon points="17,29 21,33 20,28" fill="#fda4af" />
        </g>

        <!-- Đuôi ngắn củn ngoe nguẩy trong giấc mơ -->
        <ellipse cx="62" cy="18" rx="5" ry="4" fill="url(#corgiWhite3D)" class="dog-tail" />

        <!-- Đồ chơi khúc xương gặm dở màu đỏ -->
        <g transform="translate(0, 5) rotate(-15)">
          <rect x="0" y="0" width="10" height="3" rx="1.5" fill="#ef4444" />
          <circle cx="-1" cy="-1" r="1.8" fill="#ef4444" />
          <circle cx="-1" cy="4" r="1.8" fill="#ef4444" />
          <circle cx="11" cy="-1" r="1.8" fill="#ef4444" />
          <circle cx="11" cy="4" r="1.8" fill="#ef4444" />
        </g>

        <!-- Tim bay lên khi vuốt cún -->
        <g v-if="dogPetCount > 0" class="dog-heart-burst">
          <text x="35" y="0" font-size="12" fill="#f43f5e" class="dog-heart">❤️</text>
        </g>
        <text x="25" y="-6" font-size="9" font-weight="bold" fill="#f59e0b" class="dog-zzz">z</text>
      </g>

      <!-- ==================== BONG BÓNG GIẤC MƠ TRƯA ==================== -->
      <g
        v-if="currentDream"
        id="dream-bubble"
        class="interactive-element dream-cloud-group"
        transform="translate(560, 160)"
        @click.stop="emit('nextDream')"
      >
        <circle cx="-60" cy="80" r="4" fill="#dbeafe" opacity="0.7" />
        <circle cx="-45" cy="55" r="6" fill="#dbeafe" opacity="0.8" />
        <circle cx="-25" cy="30" r="9" fill="#dbeafe" opacity="0.9" />

        <g class="dream-cloud-shape">
          <path
            d="M-50 0 Q-70 -25 -40 -45 Q-15 -65 25 -50 Q65 -65 95 -40 Q130 -30 125 5 Q135 40 95 45 Q65 60 20 50 Q-25 60 -50 35 Q-75 25 -50 0 Z"
            fill="url(#dreamGrad)"
            stroke="#93c5fd"
            stroke-width="2"
            filter="url(#glow)"
          />
          <text x="35" y="-12" font-size="28" text-anchor="middle">{{ currentDream.icon }}</text>
          <text x="35" y="14" font-size="11" font-weight="bold" fill="#1e3a8a" text-anchor="middle">
            {{ currentDream.title }}
          </text>
          <text x="35" y="30" font-size="8.5" fill="#3b82f6" text-anchor="middle">
            {{ currentDream.subtitle.length > 28 ? currentDream.subtitle.slice(0, 27) + '…' : currentDream.subtitle }}
          </text>
          <g transform="translate(75, -45)">
            <rect x="0" y="0" width="55" height="15" rx="7" fill="#3b82f6" />
            <text x="27" y="11" font-size="7.5" font-weight="bold" fill="#ffffff" text-anchor="middle">
              {{ currentDream.badge }}
            </text>
          </g>
        </g>
      </g>

      <!-- ==================== CON MUỖI CÔNG SỞ & VẾT ĐẬP ==================== -->
      <g
        v-if="isMosquitoAlive"
        id="flying-mosquito"
        class="interactive-element mosquito-character"
        @click="onMosquitoClick"
      >
        <title>Con muỗi vo ve - Bấm để đập bảo vệ giấc ngủ!</title>
        <circle cx="0" cy="0" r="16" fill="transparent" />
        <ellipse cx="-4" cy="-5" rx="2" ry="5" fill="#e2e8f0" opacity="0.7" class="mosquito-wing-l" />
        <ellipse cx="4" cy="-5" rx="2" ry="5" fill="#e2e8f0" opacity="0.7" class="mosquito-wing-r" />
        <ellipse cx="0" cy="0" rx="3" ry="5" fill="#0f172a" />
        <circle cx="0" cy="-4" r="2.5" fill="#ef4444" />
        <line x1="0" y1="-5" x2="0" y2="-10" stroke="#0f172a" stroke-width="1.2" />
        <line x1="-2" y1="2" x2="-6" y2="7" stroke="#0f172a" stroke-width="1" />
        <line x1="2" y1="2" x2="6" y2="7" stroke="#0f172a" stroke-width="1" />
      </g>

      <g
        v-if="lastSlapPos"
        class="slap-splash"
        :transform="`translate(${lastSlapPos.x}, ${lastSlapPos.y})`"
      >
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

      <!-- ==================== GÓC PHẢI: CHUYÊN VIÊN B (NGỦ LĂN LÓC 3D) ==================== -->
      <g
        id="stickman-sofa-area"
        class="interactive-element"
        @click.stop="emit('pokeWorker', 'B')"
      >
        <title>Chuyên viên B (Say như điếu đổ) - Nằm ngủ lăn lóc, chân gác ngược, dép tổ ong rơi</title>
        <g id="sofa-furniture">
          <rect x="790" y="260" width="290" height="60" rx="10" fill="url(#sofaGrad)" stroke="#312e81" stroke-width="2" filter="url(#softShadow)" />
          <rect x="775" y="270" width="28" height="65" rx="8" fill="#4338ca" stroke="#312e81" stroke-width="2" />
          <rect x="785" y="305" width="300" height="35" rx="8" fill="#4f46e5" stroke="#312e81" stroke-width="2" />
          <rect x="1070" y="270" width="24" height="65" rx="8" fill="#4338ca" stroke="#312e81" stroke-width="2" />
          <rect x="800" y="340" width="12" height="30" rx="3" fill="#1e293b" />
          <rect x="1055" y="340" width="12" height="30" rx="3" fill="#1e293b" />
          <ellipse cx="815" cy="295" rx="20" ry="14" fill="#fbbf24" stroke="#d97706" stroke-width="1.5" />
        </g>

        <!-- Thân Chuyên viên B 3D -->
        <g class="sofa-sleeper-body" :class="{ 'is-startled': poked && activeSpeaker === 'B' }">
          <line x1="825" y1="298" x2="945" y2="305" stroke="url(#limb3D)" stroke-width="7" stroke-linecap="round" class="breathe-torso" filter="url(#softShadow)" />
          <line x1="945" y1="305" x2="1030" y2="308" stroke="url(#limb3D)" stroke-width="6" stroke-linecap="round" />
          <line x1="1030" y1="308" x2="1075" y2="302" stroke="url(#limb3D)" stroke-width="5" stroke-linecap="round" />
          
          <!-- Chân co gác ngược lên tựa sofa 3D -->
          <line x1="945" y1="305" x2="975" y2="265" stroke="url(#limb3D)" stroke-width="6" stroke-linecap="round" />
          <line x1="975" y1="265" x2="1015" y2="245" stroke="url(#limb3D)" stroke-width="5" stroke-linecap="round" class="leg-twitched" />
          <circle cx="1017" cy="243" r="5" fill="url(#joint3D)" />

          <!-- Tay chạm đất & tay vắt trán -->
          <path d="M865 300 Q880 330 870 365" stroke="url(#limb3D)" stroke-width="5.5" stroke-linecap="round" fill="none" />
          <path d="M855 298 Q840 275 820 282" stroke="url(#limb3D)" stroke-width="5.5" stroke-linecap="round" fill="none" />

          <!-- Chăn mỏng xộc xệch -->
          <path d="M895 298 Q925 290 955 300 L950 320 Q920 325 890 318 Z" fill="#ec4899" opacity="0.85" stroke="#be185d" stroke-width="1.5" class="breathe-blanket" filter="url(#softShadow)" />

          <!-- Đầu khối cầu 3D -->
          <circle cx="820" cy="286" r="17" fill="url(#headSphere3D)" stroke="#94a3b8" stroke-width="1" class="sofa-head" filter="url(#softShadow)" />
          <circle cx="814" cy="292" r="3.5" fill="#fda4af" opacity="0.65" />
          <ellipse cx="828" cy="292" rx="4" ry="6" fill="#0f172a" />
          
          <!-- Giọt nước miếng 3D căng bóng -->
          <circle cx="831" cy="301" r="2.5" fill="url(#drool3D)" class="drool-drop" />
          <path d="M812 280 L818 286 M818 280 L812 286" stroke="#0f172a" stroke-width="1.8" stroke-linecap="round" />

          <!-- DÉP TỔ ONG 3D VĂNG DƯỚI SÀN CÓ ĐỔ BÓNG -->
          <g transform="translate(900, 395) rotate(25)" class="interactive-element" title="Dép tổ ong vàng 3D">
            <!-- Đổ bóng cao su xuống sàn -->
            <ellipse cx="0" cy="5" rx="16" ry="6" fill="#0f172a" opacity="0.35" />
            <!-- Thân đế dép dày 3D -->
            <ellipse cx="0" cy="0" rx="15" ry="8" fill="url(#sandal3D)" />
            <!-- Quai dép -->
            <path d="M-9 -3 Q0 -9 9 -3" stroke="#ca8a04" stroke-width="2" fill="none" />
            <!-- Lưới lỗ tổ ong 3D -->
            <circle cx="-6" cy="-2" r="1" fill="#854d0e" />
            <circle cx="-2" cy="-2" r="1" fill="#854d0e" />
            <circle cx="2" cy="-2" r="1" fill="#854d0e" />
            <circle cx="6" cy="-2" r="1" fill="#854d0e" />
            <circle cx="-4" cy="2" r="1" fill="#854d0e" />
            <circle cx="0" cy="2" r="1" fill="#854d0e" />
            <circle cx="4" cy="2" r="1" fill="#854d0e" />
          </g>

          <g transform="translate(980, 385) rotate(-15)">
            <ellipse cx="0" cy="5" rx="16" ry="6" fill="#0f172a" opacity="0.35" />
            <ellipse cx="0" cy="0" rx="15" ry="8" fill="url(#sandal3D)" />
            <path d="M-9 -3 Q0 -9 9 -3" stroke="#ca8a04" stroke-width="2" fill="none" />
          </g>

          <!-- Chữ Zzz bay lên -->
          <g class="sofa-zzz-group">
            <text x="840" y="260" font-size="16" font-weight="bold" fill="#f43f5e" class="zzz-item zzz-sofa-1">z</text>
            <text x="855" y="235" font-size="22" font-weight="bold" fill="#fb7185" class="zzz-item zzz-sofa-2">Z</text>
            <text x="875" y="200" font-size="30" font-weight="bold" fill="#fda4af" class="zzz-item zzz-sofa-3">Zzz!</text>
          </g>

          <g transform="translate(930, 205)">
            <rect x="0" y="0" width="80" height="22" rx="4" fill="#1e293b" stroke="#f43f5e" stroke-width="1.2" />
            <text x="6" y="14" font-size="9" font-weight="bold" fill="#fb7185">Chuyên viên B</text>
          </g>
        </g>
      </g>

      <!-- CẢNH BÁO BỊ CHỌT GIẬT MÌNH -->
      <g v-if="poked" class="startle-alert" transform="translate(540, 140)">
        <circle cx="20" cy="20" r="24" fill="#ef4444" stroke="#ffffff" stroke-width="2" filter="url(#glow)" />
        <text x="14" y="28" font-size="24" font-weight="bold" fill="#ffffff">!</text>
      </g>

      <!-- LỚP PHỦ TẮT ĐÈN NGHỈ TRƯA -->
      <rect
        v-if="isLightsDimmed"
        x="0"
        y="0"
        width="1100"
        height="480"
        fill="#050814"
        opacity="0.65"
        style="mix-blend-mode: multiply; pointer-events: none;"
      />
    </svg>

    <!-- Hộp thoại bóng nói của Boss Mèo -->
    <Transition name="speech-pop">
      <div v-if="catPurrText && activeSpeaker === 'cat'" class="cat-speech-bubble">
        <span>{{ catPurrText }}</span>
      </div>
    </Transition>

    <!-- Hộp thoại bóng nói của Cún Corgi -->
    <Transition name="speech-pop">
      <div v-if="dogBarkText && activeSpeaker === 'dog'" class="dog-speech-bubble">
        <span>{{ dogBarkText }}</span>
      </div>
    </Transition>

    <!-- Hộp thoại bóng nói chung cho các nhân vật -->
    <Transition name="speech-pop">
      <div v-if="quote && activeSpeaker !== 'cat' && activeSpeaker !== 'dog'" class="stickman-speech-bubble">
        <span class="speech-quote">{{ quote }}</span>
      </div>
    </Transition>

    <!-- Thanh hướng dẫn tương tác -->
    <div class="stickman-hint">
      <span class="hint-item"><span class="hint-icon">👆</span> Bấm nhân viên (A, B, C, D)</span>
      <span class="hint-sep">•</span>
      <span class="hint-item">🐶 Xoa cún Corgi (Sploot)</span>
      <span class="hint-sep">•</span>
      <span class="hint-item">🐱 Vuốt Boss Mèo</span>
      <span class="hint-sep">•</span>
      <span class="hint-item">🩴 Dép tổ ong 3D</span>
      <span class="hint-sep">•</span>
      <span class="hint-item">📑 Chuyên viên C đóng dấu</span>
      <span class="hint-sep">•</span>
      <span class="hint-item">🚰 Bình nước 20L</span>
    </div>
  </div>
</template>

<style scoped>
.stickman-stage {
  position: relative;
  width: 100%;
  max-width: 1060px;
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
  transition: transform 0.15s cubic-bezier(0.34, 1.56, 0.64, 1);
}

.interactive-element:hover {
  filter: drop-shadow(0 0 8px rgba(56, 189, 248, 0.7));
}

/* Animations mượt mà tự nhiên nhất */

.breathe-spine {
  animation: spine-breathe 3.6s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite;
  transform-origin: center bottom;
}

@keyframes spine-breathe {
  0%, 100% { transform: scaleY(1); }
  50% { transform: scaleY(1.04) translateY(-2px); }
}

.desk-head {
  animation: head-nod 3.6s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite;
  transform-origin: center;
}

@keyframes head-nod {
  0%, 100% { transform: rotate(0deg); }
  50% { transform: rotate(-3deg) translateY(-2px); }
}

.snot-bubble {
  animation: snot-pulse 3.6s cubic-bezier(0.34, 1.56, 0.64, 1) infinite;
  transform-origin: center;
}

@keyframes snot-pulse {
  0%, 100% { transform: scale(0.6); opacity: 0.2; }
  45% { transform: scale(1.6); opacity: 0.9; }
  50% { transform: scale(0.2); opacity: 0; }
}

.breathe-torso, .breathe-blanket {
  animation: torso-breathe 4s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite;
  transform-origin: center;
}

@keyframes torso-breathe {
  0%, 100% { transform: translateY(0); }
  50% { transform: translateY(-3px) scaleY(1.03); }
}

.leg-twitched {
  animation: leg-twitch 6s cubic-bezier(0.25, 1, 0.5, 1) infinite;
  transform-origin: center;
}

@keyframes leg-twitch {
  0%, 80%, 100% { transform: rotate(0deg); }
  85% { transform: rotate(4deg); }
  90% { transform: rotate(-3deg); }
}

.drool-drop {
  animation: drool-fall 2.8s cubic-bezier(0.4, 0, 1, 1) infinite;
}

@keyframes drool-fall {
  0% { opacity: 0; transform: translateY(0) scale(0.5); }
  30% { opacity: 0.9; transform: translateY(2px) scale(1); }
  70% { opacity: 0.9; transform: translateY(12px) scale(0.8); }
  100% { opacity: 0; transform: translateY(18px) scale(0.2); }
}

/* Cún Corgi animations */
.dog-body {
  animation: dog-breathe 2.8s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite;
  transform-origin: 38px 18px;
}

@keyframes dog-breathe {
  0%, 100% { transform: scale(1); }
  50% { transform: scale(1.06) translateY(-1px); }
}

.dog-head-group {
  animation: dog-head-nod 2.8s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite;
  transform-origin: 16px 18px;
}

@keyframes dog-head-nod {
  0%, 100% { transform: rotate(0deg); }
  50% { transform: rotate(-2deg); }
}

.dog-tail {
  animation: dog-tail-wag 2.2s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite;
  transform-origin: 60px 18px;
}

@keyframes dog-tail-wag {
  0%, 100% { transform: rotate(0deg); }
  25% { transform: rotate(15deg); }
  75% { transform: rotate(-15deg); }
}

.dog-heart {
  animation: heart-float 1.2s cubic-bezier(0.16, 1, 0.3, 1) forwards;
}

.dog-zzz {
  animation: float-zzz 2.6s cubic-bezier(0.25, 0.46, 0.45, 0.94) infinite;
}

/* Gầm bàn */
.cocoon-body {
  animation: cocoon-breathe 3.8s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite;
  transform-origin: 525px 400px;
}

@keyframes cocoon-breathe {
  0%, 100% { transform: scale(1); }
  50% { transform: scale(1.05) translateY(-1px); }
}

.toes-wiggle {
  animation: toes-wiggle-anim 4s cubic-bezier(0.25, 1, 0.5, 1) infinite;
  transform-origin: 565px 400px;
}

.toes-wiggle-alt {
  animation: toes-wiggle-anim 4s cubic-bezier(0.25, 1, 0.5, 1) infinite 0.6s;
  transform-origin: 568px 410px;
}

@keyframes toes-wiggle-anim {
  0%, 70%, 100% { transform: rotate(0deg); }
  75% { transform: rotate(8deg); }
  80% { transform: rotate(-6deg); }
  85% { transform: rotate(4deg); }
}

/* Bọt khí */
.water-bubble-1 { animation: bubble-rise 2.5s ease-in infinite; }
.water-bubble-2 { animation: bubble-rise 2.5s ease-in infinite 0.8s; }
.water-bubble-3 { animation: bubble-rise 2.5s ease-in infinite 1.6s; }

@keyframes bubble-rise {
  0% { transform: translateY(15px); opacity: 0; }
  20% { opacity: 0.8; }
  80% { opacity: 0.8; }
  100% { transform: translateY(-15px); opacity: 0; }
}

.lizard-tail {
  animation: lizard-tail-wag 3s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite;
  transform-origin: 5px 10px;
}

@keyframes lizard-tail-wag {
  0%, 100% { transform: rotate(0deg); }
  50% { transform: rotate(15deg); }
}

.leaf-stem-1 { animation: leaf-sway 3.2s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite alternate; transform-origin: 20px 85px; }
.leaf-stem-2 { animation: leaf-sway 3.8s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite alternate 0.5s; transform-origin: 20px 85px; }
.leaf-stem-3 { animation: leaf-sway 2.8s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite alternate 1s; transform-origin: 20px 85px; }

@keyframes leaf-sway {
  0% { transform: rotate(-3deg); }
  100% { transform: rotate(4deg); }
}

.zzz-item {
  opacity: 0;
  transform-origin: center;
}

.zzz-1 { animation: float-zzz 3.6s cubic-bezier(0.25, 0.46, 0.45, 0.94) infinite; animation-delay: 0s; }
.zzz-2 { animation: float-zzz 3.6s cubic-bezier(0.25, 0.46, 0.45, 0.94) infinite; animation-delay: 1.2s; }
.zzz-3 { animation: float-zzz 3.6s cubic-bezier(0.25, 0.46, 0.45, 0.94) infinite; animation-delay: 2.4s; }

.zzz-sofa-1 { animation: float-zzz-sofa 4s cubic-bezier(0.25, 0.46, 0.45, 0.94) infinite; animation-delay: 0.4s; }
.zzz-sofa-2 { animation: float-zzz-sofa 4s cubic-bezier(0.25, 0.46, 0.45, 0.94) infinite; animation-delay: 1.6s; }
.zzz-sofa-3 { animation: float-zzz-sofa 4s cubic-bezier(0.25, 0.46, 0.45, 0.94) infinite; animation-delay: 2.8s; }

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

.clock-minute-hand {
  animation: clock-tick 60s linear infinite;
  transform-origin: 0 0;
}

@keyframes clock-tick {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.cat-tail {
  animation: cat-tail-swish 3s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite;
  transform-origin: 423px 322px;
}

@keyframes cat-tail-swish {
  0%, 100% { transform: rotate(0deg); }
  50% { transform: rotate(18deg); }
}

.cat-body {
  animation: cat-breathe 2.8s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite;
  transform-origin: 410px 320px;
}

@keyframes cat-breathe {
  0%, 100% { transform: scale(1); }
  50% { transform: scale(1.05); }
}

.cat-zzz { animation: float-zzz 2.5s cubic-bezier(0.25, 0.46, 0.45, 0.94) infinite; }

.cat-heart {
  animation: heart-float 1.2s cubic-bezier(0.16, 1, 0.3, 1) forwards;
}

@keyframes heart-float {
  0% { opacity: 0; transform: translateY(0) scale(0.5); }
  40% { opacity: 1; transform: translateY(-10px) scale(1.2); }
  100% { opacity: 0; transform: translateY(-25px) scale(1.5); }
}

.fan-head-oscillation.fan-low,
.fan-head-oscillation.fan-high {
  animation: fan-swing 5s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite alternate;
  transform-origin: 20px 115px;
}

@keyframes fan-swing {
  0% { transform: rotate(-15deg); }
  100% { transform: rotate(15deg); }
}

.fan-blades-group.fan-low { animation: fan-spin 0.35s linear infinite; transform-origin: 20px 45px; }
.fan-blades-group.fan-high { animation: fan-spin 0.12s linear infinite; transform-origin: 20px 45px; }

@keyframes fan-spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

.wind-line { animation: wind-flow 1.5s linear infinite; }
.wind-1 { animation-delay: 0s; }
.wind-2 { animation-delay: 0.5s; }
.wind-3 { animation-delay: 1s; }

@keyframes wind-flow {
  0% { stroke-dashoffset: 30; opacity: 0; }
  50% { opacity: 0.8; }
  100% { stroke-dashoffset: -30; opacity: 0; }
}

.fan-wind-streams.wind-fast .wind-line { animation-duration: 0.7s; }

.dream-cloud-shape {
  animation: cloud-float 4s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite alternate;
  transform-origin: 35px 0px;
}

@keyframes cloud-float {
  0% { transform: translateY(0) rotate(-1deg); }
  100% { transform: translateY(-6px) rotate(1.5deg); }
}

.mosquito-character {
  animation: mosquito-fly 7s cubic-bezier(0.45, 0.05, 0.55, 0.95) infinite;
  cursor: crosshair;
}

@keyframes mosquito-fly {
  0% { transform: translate(620px, 220px); }
  20% { transform: translate(680px, 180px); }
  40% { transform: translate(570px, 200px); }
  60% { transform: translate(720px, 240px); }
  80% { transform: translate(630px, 260px); }
  100% { transform: translate(620px, 220px); }
}

.mosquito-wing-l, .mosquito-wing-r {
  animation: wing-beat 0.08s ease-in-out infinite alternate;
}

@keyframes wing-beat {
  from { transform: scaleY(1); }
  to { transform: scaleY(-0.3); }
}

.slap-splash {
  animation: slap-pop 0.6s cubic-bezier(0.175, 0.885, 0.32, 1.275) both;
}

@keyframes slap-pop {
  0% { transform: scale(0.2); opacity: 0; }
  50% { transform: scale(1.3); opacity: 1; }
  100% { transform: scale(1); opacity: 0.9; }
}

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
  0% { opacity: 0; transform: translate(540px, 160px) scale(0.2); }
  100% { opacity: 1; transform: translate(540px, 140px) scale(1); }
}

/* Hộp thoại bóng nói */
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

.cat-speech-bubble {
  position: absolute;
  bottom: 80px;
  left: 360px;
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

.dog-speech-bubble {
  position: absolute;
  bottom: 80px;
  left: 650px;
  background: #fefce8;
  color: #854d0e;
  padding: 8px 14px;
  border-radius: 12px;
  box-shadow: 0 8px 20px -4px rgba(0, 0, 0, 0.25);
  font-size: 13px;
  font-weight: 600;
  z-index: 10;
  border: 1.5px solid #eab308;
}

.dog-speech-bubble::after {
  content: '';
  position: absolute;
  bottom: -8px;
  left: 30px;
  border-width: 8px 8px 0;
  border-style: solid;
  border-color: #fefce8 transparent;
}

.stickman-hint {
  position: absolute;
  bottom: 8px;
  left: 50%;
  transform: translateX(-50%);
  background: rgba(15, 23, 42, 0.9);
  backdrop-filter: blur(8px);
  padding: 6px 16px;
  border-radius: 20px;
  border: 1px solid rgba(255, 255, 255, 0.12);
  font-size: 11px;
  color: #cbd5e1;
  display: flex;
  align-items: center;
  gap: 8px;
  pointer-events: none;
  white-space: nowrap;
}

.hint-sep { color: #475569; }

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
