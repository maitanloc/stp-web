import { computed, onMounted, onUnmounted, ref } from 'vue'
import { teamIdentity } from './useTeamIdentity'
import { getDevToken } from '../api/client'

const BYPASS_KEY = 'stp.lunch.bypass'
const PREVIEW_KEY = 'stp.lunch.preview'

const now = ref(new Date())
const isDevBypass = ref(sessionStorage.getItem(BYPASS_KEY) === 'true')
const isPreview = ref(sessionStorage.getItem(PREVIEW_KEY) === 'true')
const showWarningPopup = ref(false)
const pokeCount = ref(0)
const pokeQuote = ref('')
const isLullabyPlaying = ref(false)

let timer: number | undefined
let audioCtx: AudioContext | null = null
let lullabyInterval: number | undefined

const POKE_QUOTES = [
  'Ối sếp ơi! Em thề em chỉ chợp mắt 2 phút thôi mừ... 😴',
  'Để yên cho em ngủ nốt giấc mộng trưa, 13h em dậy cày bù 200%! 💤',
  'Cơm sườn 35k no căng bụng rồi, mắt tự động sụp xuống sếp ơi... 🍱',
  'Dev đang hì hục sửa máy chủ ngoài kia kìa, sếp qua chọt Dev đi! 🛠️',
  'Nhân viên chăm chỉ cũng cần sạc pin mà sếp... zZz...',
  '12h trưa là đặc quyền bất khả xâm phạm! 13h00 rồi tính tiếp! 🛌',
  'Ngủ một tí cho da dẻ hồng hào, chiều nhập hồ sơ không bị lú! ✨',
  'Ai đó gọi một ly trà sữa full topping đánh thức em đi... 🧋',
]

export function useLunchBreak() {
  function updateTime() {
    now.value = new Date()
    // Nếu vào đúng 12h00 hoặc đang trong giờ 12h và chưa bypass
    if (isLunchHour.value && !isDevBypass.value && !sessionStorage.getItem('stp.lunch.popup_shown')) {
      showWarningPopup.value = true
      sessionStorage.setItem('stp.lunch.popup_shown', 'true')
    }
  }

  // Khung giờ nghỉ trưa: 12:00:00 -> 12:59:59 (giờ địa phương)
  const isLunchHour = computed(() => {
    const hours = now.value.getHours()
    return hours === 12
  })

  // Hiển thị chế độ nghỉ trưa khi: (đang trong giờ 12h HOẶC đang bật Preview) VÀ chưa Dev Bypass
  const isLunchBreakActive = computed(() => {
    return (isLunchHour.value || isPreview.value) && !isDevBypass.value
  })

  // Thời gian còn lại đến 13:00:00
  const timeRemaining = computed(() => {
    if (!isLunchBreakActive.value && !isPreview.value) {
      return { totalSeconds: 0, minutes: 0, seconds: 0, formatted: '00:00' }
    }

    const currentMinutes = now.value.getMinutes()
    const currentSeconds = now.value.getSeconds()
    const remainingSeconds = Math.max(0, (59 - currentMinutes) * 60 + (60 - currentSeconds))

    const mm = Math.floor(remainingSeconds / 60)
    const ss = remainingSeconds % 60
    return {
      totalSeconds: remainingSeconds,
      minutes: mm,
      seconds: ss,
      formatted: `${String(mm).padStart(2, '0')}:${String(ss).padStart(2, '0')}`,
    }
  })

  function togglePreview() {
    isPreview.value = !isPreview.value
    sessionStorage.setItem(PREVIEW_KEY, isPreview.value ? 'true' : 'false')
    if (isPreview.value) {
      isDevBypass.value = false
      sessionStorage.removeItem(BYPASS_KEY)
      showWarningPopup.value = true
    }
  }

  function dismissWarningPopup() {
    showWarningPopup.value = false
  }

  function enableDevBypass(pin?: string): boolean {
    const isAdmin = teamIdentity.value?.role === 'admin'
    const hasDevToken = Boolean(getDevToken())
    const validPins = ['stp2026', 'dev123', '123456', 'stp', 'admin']

    if (isAdmin || hasDevToken || (pin && validPins.includes(pin.trim().toLowerCase()))) {
      isDevBypass.value = true
      sessionStorage.setItem(BYPASS_KEY, 'true')
      showWarningPopup.value = false
      stopLullaby()
      return true
    }
    return false
  }

  function disableDevBypass() {
    isDevBypass.value = false
    sessionStorage.removeItem(BYPASS_KEY)
  }

  function pokeStickman() {
    pokeCount.value++
    const idx = Math.floor(Math.random() * POKE_QUOTES.length)
    pokeQuote.value = POKE_QUOTES[idx] ?? POKE_QUOTES[0]!
    playSnoozeChime()
  }

  // Nhạc ru ngủ thư giãn bằng Web Audio API (không cần tải file mp3 nào)
  function toggleLullaby() {
    if (isLullabyPlaying.value) {
      stopLullaby()
    } else {
      startLullaby()
    }
  }

  function playSnoozeChime() {
    try {
      const ctx = new (window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext)()
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'sine'
      osc.frequency.setValueAtTime(523.25, ctx.currentTime) // C5
      osc.frequency.exponentialRampToValueAtTime(659.25, ctx.currentTime + 0.15) // E5
      gain.gain.setValueAtTime(0.08, ctx.currentTime)
      gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.3)
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start()
      osc.stop(ctx.currentTime + 0.3)
    } catch {
      // Bỏ qua nếu audio bị chặn
    }
  }

  function startLullaby() {
    try {
      audioCtx = new (window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext)()
      isLullabyPlaying.value = true

      // Giai điệu ru ngủ nhẹ nhàng (nhạc chuông hộp nhạc)
      const notes = [261.63, 329.63, 392.00, 523.25, 392.00, 329.63, 293.66, 349.23, 440.00, 349.23]
      let noteIndex = 0

      lullabyInterval = window.setInterval(() => {
        if (!audioCtx || audioCtx.state === 'closed') return
        const freq = notes[noteIndex % notes.length]!
        noteIndex++

        const osc = audioCtx.createOscillator()
        const gain = audioCtx.createGain()
        osc.type = 'triangle'
        osc.frequency.setValueAtTime(freq, audioCtx.currentTime)
        gain.gain.setValueAtTime(0.04, audioCtx.currentTime)
        gain.gain.exponentialRampToValueAtTime(0.0001, audioCtx.currentTime + 1.2)
        osc.connect(gain)
        gain.connect(audioCtx.destination)
        osc.start()
        osc.stop(audioCtx.currentTime + 1.2)
      }, 700)
    } catch {
      isLullabyPlaying.value = false
    }
  }

  function stopLullaby() {
    isLullabyPlaying.value = false
    if (lullabyInterval) {
      clearInterval(lullabyInterval)
      lullabyInterval = undefined
    }
    if (audioCtx) {
      try {
        void audioCtx.close()
      } catch {}
      audioCtx = null
    }
  }

  onMounted(() => {
    if (!timer) {
      updateTime()
      timer = window.setInterval(updateTime, 1000)
    }
  })

  onUnmounted(() => {
    // Không xoá interval toàn cục vì nhiều component có thể dùng chung
  })

  return {
    now,
    isLunchHour,
    isLunchBreakActive,
    timeRemaining,
    isDevBypass,
    isPreview,
    showWarningPopup,
    pokeCount,
    pokeQuote,
    isLullabyPlaying,
    togglePreview,
    dismissWarningPopup,
    enableDevBypass,
    disableDevBypass,
    pokeStickman,
    toggleLullaby,
    stopLullaby,
  }
}
