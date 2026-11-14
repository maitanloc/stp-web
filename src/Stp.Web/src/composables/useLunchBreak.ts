import { computed, onMounted, ref } from 'vue'
import { teamIdentity } from './useTeamIdentity'
import { getDevToken } from '../api/client'

const BYPASS_KEY = 'stp.lunch.bypass'
const PREVIEW_KEY = 'stp.lunch.preview'

export interface LunchDish {
  id: string
  name: string
  emoji: string
  desc: string
  tag: string
}

export interface LunchDream {
  id: string
  title: string
  subtitle: string
  icon: string
  badge: string
}

export type AmbientSoundType = 'none' | 'lullaby' | 'rain' | 'fan'
export type FanSpeed = 0 | 1 | 2

const now = ref(new Date())
const isDevBypass = ref(sessionStorage.getItem(BYPASS_KEY) === 'true')
const isPreview = ref(sessionStorage.getItem(PREVIEW_KEY) === 'true')
const showWarningPopup = ref(false)

// Tương tác văn phòng
const pokeCount = ref(0)
const pokeQuote = ref('')
const isLightsDimmed = ref(false)
const fanSpeed = ref<FanSpeed>(1)
const catPetCount = ref(0)
const catPurrText = ref('')
const mosquitoCaught = ref(0)
const isMosquitoAlive = ref(true)
const lastSlapPos = ref<{ x: number; y: number } | null>(null)

// Giấc mơ trưa
const currentDreamIndex = ref(0)
const dreamsList: LunchDream[] = [
  {
    id: 'bonus',
    title: 'Thưởng nóng 50 triệu',
    subtitle: 'Tài khoản ting ting ting, sếp bảo tiêu xài thoải mái!',
    icon: '💰',
    badge: 'Ước mơ #1',
  },
  {
    id: 'resort',
    title: 'Resort 5 sao Maldives',
    subtitle: 'Nằm võng đung đưa ngắm biển xanh, sếp bao trọn gói!',
    icon: '🏖️',
    badge: 'Chill hết nấc',
  },
  {
    id: 'auto-dossier',
    title: 'Hồ sơ tự động trích yếu',
    subtitle: '10.000 hồ sơ tự động scan xong trong 1 cái chớp mắt!',
    icon: '⚡',
    badge: 'Ước mơ công sở',
  },
  {
    id: 'beef-pho',
    title: 'Tô Phở Bò 10 Trứng Chần',
    subtitle: 'Nước dùng béo ngậy thơm nức mũi, thịt bò ngập mặt!',
    icon: '🍲',
    badge: 'Đại tiệc trưa',
  },
  {
    id: 'cat-boss',
    title: 'Làm Trợ lý cho Boss Mèo',
    subtitle: 'Cả ngày chỉ việc vuốt ve boss và ăn pate miễn phí!',
    icon: '🐱',
    badge: 'Cuộc sống lý tưởng',
  },
  {
    id: 'no-bug',
    title: 'Code chạy mượt 0 Warning 0 Bug',
    subtitle: 'Dev compile 1 phát ăn ngay, khách hàng khen nức nở!',
    icon: '✨',
    badge: 'Phép màu IT',
  },
  {
    id: 'sleep-infinity',
    title: 'Ngủ mãi không cần dậy',
    subtitle: 'Đồng hồ dừng mãi ở 12h, gối êm chăn ấm ngủ tới mai!',
    icon: '🛌',
    badge: 'Chân lý cuộc đời',
  },
]

// Thực đơn trưa "Hôm nay ăn gì?"
const dishesList: LunchDish[] = [
  { id: 'com-tam', name: 'Cơm tấm sườn bì chả ốp la', emoji: '🍛', desc: 'Sườn nướng thơm ngậy, nước mắm kẹo sền sệt, ăn xong no căng rốn!', tag: 'No lâu' },
  { id: 'bun-bo', name: 'Bún bò Huế thêm chả cua', emoji: '🍜', desc: 'Nước dùng cay nồng ngập sả ớt, xì xụp ấm bụng tỉnh cả người!', tag: 'Đậm đà' },
  { id: 'pho-bo', name: 'Phở bò tái nạm gầu giòn', emoji: '🍲', desc: 'Nước dùng trong veo ngọt từ xương ống, thêm quẩy giòn rụm.', tag: 'Truyền thống' },
  { id: 'banh-mi', name: 'Bánh mì chảo pa-tê xíu mại', emoji: '🥖', desc: 'Bánh mì nóng giòn chấm lòng đỏ trứng béo ngậy, nhanh gọn lẹ.', tag: 'Ngon rẻ' },
  { id: 'com-ga', name: 'Cơm gà xối mỡ da giòn rụm', emoji: '🍗', desc: 'Cơm vàng ươm hạt dẻo thơm, da gà giòn tan kích thích vị giác.', tag: 'Giòn rụm' },
  { id: 'bun-cha', name: 'Bún chả Hà Nội nem cua bể', emoji: '🥗', desc: 'Chả nướng than hoa thơm lừng chấm nước mắm đu đủ chua ngọt.', tag: 'Thanh mát' },
  { id: 'bun-dau', name: 'Bún đậu mắm tôm thập cẩm', emoji: '🥢', desc: 'Dồi rán béo ngậy, đậu hũ chiên phồng, chấm mắm tôm đánh sủi bọt!', tag: 'Gây nghiện' },
  { id: 'banh-canh', name: 'Bánh canh cua giò heo', emoji: '🦀', desc: 'Nước súp đỏ sánh gạch cua, sợi bánh canh dai dẻo chuẩn bài.', tag: 'Bổ dưỡng' },
  { id: 'hu-tieu', name: 'Hủ tiếu Nam Vang sườn tôm', emoji: '🦐', desc: 'Tỏi phi thơm nức mũi, thịt bằm và tôm tươi sần sật.', tag: 'Dễ ăn' },
  { id: 'mi-quang', name: 'Mì quảng tôm thịt trứng cút', emoji: '🍜', desc: 'Bánh tráng mè nướng giòn tan bẻ vụn, nước lèo keo sánh đậm đà.', tag: 'Đặc sản' },
  { id: 'mi-tom', name: 'Mì tôm 2 trứng xúc xích', emoji: '🍜', desc: 'Huyền thoại cứu đói cuối tháng của mọi dân văn phòng!', tag: 'Tiết kiệm' },
  { id: 'tra-sua', name: 'Trà sữa 1 lít full topping', emoji: '🧋', desc: 'Bỏ ăn cơm uống trà sữa, nạp đường và béo ngậy bù năng lượng!', tag: 'Đam mê' },
]

const selectedDish = ref<LunchDish | null>(null)
const isRollingDish = ref(false)

// Âm thanh ASMR
const activeAmbientSound = ref<AmbientSoundType>('none')
let audioCtx: AudioContext | null = null
let lullabyInterval: number | undefined
let rainSource: AudioBufferSourceNode | null = null
let rainGain: GainNode | null = null
let fanSource: AudioBufferSourceNode | null = null
let fanGain: GainNode | null = null

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

const CAT_PURRS = [
  'Meo~ Boss đang ngủ, cấm bấm bàn phím! 🐾',
  'Grừ grừ... Xoa cằm thích ghê, thưởng cho 1 tim! ❤️',
  'Meo meo! Trưa nay nhớ phần boss nửa miếng sườn nhé! 🍱',
  'Boss ngủ cạnh CPU cho ấm, ấm như lò sưởi ấy... 💤',
  'Ngoan lắm sen, chiều làm tiếp kiếm tiền mua hạt cho boss! ✨',
]

function getAudioContext(): AudioContext | null {
  try {
    if (!audioCtx || audioCtx.state === 'closed') {
      audioCtx = new (window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext)()
    }
    if (audioCtx.state === 'suspended') {
      void audioCtx.resume()
    }
    return audioCtx
  } catch {
    return null
  }
}

export function useLunchBreak() {
  function updateTime() {
    now.value = new Date()
    if (isLunchHour.value && !isDevBypass.value && !sessionStorage.getItem('stp.lunch.popup_shown')) {
      showWarningPopup.value = true
      sessionStorage.setItem('stp.lunch.popup_shown', 'true')
    }
  }

  const isLunchHour = computed(() => {
    return now.value.getHours() === 12
  })

  const isLunchBreakActive = computed(() => {
    return (isLunchHour.value || isPreview.value) && !isDevBypass.value
  })

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

  const currentDream = computed(() => {
    return dreamsList[currentDreamIndex.value % dreamsList.length]!
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
      stopAllAmbient()
      return true
    }
    return false
  }

  function disableDevBypass() {
    isDevBypass.value = false
    sessionStorage.removeItem(BYPASS_KEY)
  }

  // Tương tác văn phòng: Chọt nhân viên
  function pokeStickman() {
    pokeCount.value++
    const idx = Math.floor(Math.random() * POKE_QUOTES.length)
    pokeQuote.value = POKE_QUOTES[idx] ?? POKE_QUOTES[0]!
    playSnoozeChime()
  }

  // Tương tác văn phòng: Tắt / Bật đèn
  function toggleLights() {
    isLightsDimmed.value = !isLightsDimmed.value
    playSwitchSound()
  }

  // Tương tác văn phòng: Bật / Chỉnh quạt máy
  function cycleFan() {
    fanSpeed.value = ((fanSpeed.value + 1) % 3) as FanSpeed
    playClickSound()
    if (activeAmbientSound.value === 'fan') {
      if (fanSpeed.value === 0) {
        stopAllAmbient()
      } else {
        setAmbientSound('fan')
      }
    }
  }

  // Tương tác văn phòng: Boss mèo
  function petCat() {
    catPetCount.value++
    const idx = Math.floor(Math.random() * CAT_PURRS.length)
    catPurrText.value = CAT_PURRS[idx] ?? CAT_PURRS[0]!
    playCatMeow()
  }

  // Tương tác: Đổi giấc mơ
  function nextDream() {
    currentDreamIndex.value++
    playChimeUp()
  }

  // Tương tác: Đập muỗi
  function slapMosquito(x?: number, y?: number) {
    if (!isMosquitoAlive.value) return
    isMosquitoAlive.value = false
    mosquitoCaught.value++
    if (x !== undefined && y !== undefined) {
      lastSlapPos.value = { x, y }
    }
    playSlapSound()

    // Hồi sinh muỗi sau 3.5 giây để tiếp tục chơi
    window.setTimeout(() => {
      isMosquitoAlive.value = true
      lastSlapPos.value = null
    }, 3500)
  }

  // Tiện ích: Hôm nay ăn gì?
  function rollDish() {
    if (isRollingDish.value) return
    isRollingDish.value = true
    let rolls = 0
    const maxRolls = 14
    const interval = window.setInterval(() => {
      const randIdx = Math.floor(Math.random() * dishesList.length)
      selectedDish.value = dishesList[randIdx]!
      playTickSound()
      rolls++
      if (rolls >= maxRolls) {
        clearInterval(interval)
        isRollingDish.value = false
        playWinnerSound()
      }
    }, 90)
  }

  // ==================== HỆ THỐNG ÂM THANH ASMR BẰNG WEB AUDIO API ====================

  function setAmbientSound(type: AmbientSoundType) {
    if (activeAmbientSound.value === type) {
      stopAllAmbient()
      return
    }

    stopAllAmbient()
    activeAmbientSound.value = type

    if (type === 'lullaby') {
      startLullaby()
    } else if (type === 'rain') {
      startRainNoise()
    } else if (type === 'fan') {
      startFanNoise()
    }
  }

  function stopAllAmbient() {
    activeAmbientSound.value = 'none'
    stopLullaby()
    stopRainNoise()
    stopFanNoise()
  }

  // 1. Hộp nhạc ru ngủ
  function startLullaby() {
    const ctx = getAudioContext()
    if (!ctx) return
    const notes = [261.63, 329.63, 392.00, 523.25, 392.00, 329.63, 293.66, 349.23, 440.00, 349.23]
    let noteIndex = 0

    lullabyInterval = window.setInterval(() => {
      if (!ctx || ctx.state === 'closed') return
      const freq = notes[noteIndex % notes.length]!
      noteIndex++

      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'triangle'
      osc.frequency.setValueAtTime(freq, ctx.currentTime)
      gain.gain.setValueAtTime(0.04, ctx.currentTime)
      gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 1.2)
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start()
      osc.stop(ctx.currentTime + 1.2)
    }, 700)
  }

  function stopLullaby() {
    if (lullabyInterval) {
      clearInterval(lullabyInterval)
      lullabyInterval = undefined
    }
  }

  // 2. Tiếng mưa rơi ASMR
  function startRainNoise() {
    const ctx = getAudioContext()
    if (!ctx) return
    try {
      const bufferSize = ctx.sampleRate * 2
      const buffer = ctx.createBuffer(1, bufferSize, ctx.sampleRate)
      const data = buffer.getChannelData(0)
      let b0 = 0, b1 = 0, b2 = 0
      for (let i = 0; i < bufferSize; i++) {
        const white = Math.random() * 2 - 1
        b0 = 0.99 * b0 + white * 0.05
        b1 = 0.95 * b1 + white * 0.1
        b2 = 0.85 * b2 + white * 0.25
        data[i] = (b0 + b1 + b2 + white * 0.2) * 0.04
      }

      rainSource = ctx.createBufferSource()
      rainSource.buffer = buffer
      rainSource.loop = true

      const filter = ctx.createBiquadFilter()
      filter.type = 'lowpass'
      filter.frequency.setValueAtTime(1100, ctx.currentTime)

      rainGain = ctx.createGain()
      rainGain.gain.setValueAtTime(0.05, ctx.currentTime)

      rainSource.connect(filter)
      filter.connect(rainGain)
      rainGain.connect(ctx.destination)
      rainSource.start()
    } catch {
      // Audio not supported
    }
  }

  function stopRainNoise() {
    if (rainSource) {
      try {
        rainSource.stop()
        rainSource.disconnect()
      } catch {}
      rainSource = null
    }
    rainGain = null
  }

  // 3. Tiếng quạt gió ASMR
  function startFanNoise() {
    const ctx = getAudioContext()
    if (!ctx) return
    try {
      const bufferSize = ctx.sampleRate * 2
      const buffer = ctx.createBuffer(1, bufferSize, ctx.sampleRate)
      const data = buffer.getChannelData(0)
      for (let i = 0; i < bufferSize; i++) {
        data[i] = (Math.random() * 2 - 1) * 0.06
      }

      fanSource = ctx.createBufferSource()
      fanSource.buffer = buffer
      fanSource.loop = true

      const filter = ctx.createBiquadFilter()
      filter.type = 'lowpass'
      filter.frequency.setValueAtTime(180, ctx.currentTime)

      fanGain = ctx.createGain()
      const vol = fanSpeed.value === 2 ? 0.09 : 0.05
      fanGain.gain.setValueAtTime(vol, ctx.currentTime)

      fanSource.connect(filter)
      filter.connect(fanGain)
      fanGain.connect(ctx.destination)
      fanSource.start()
    } catch {
      // Audio not supported
    }
  }

  function stopFanNoise() {
    if (fanSource) {
      try {
        fanSource.stop()
        fanSource.disconnect()
      } catch {}
      fanSource = null
    }
    fanGain = null
  }

  // Sound effects
  function playSnoozeChime() {
    const ctx = getAudioContext()
    if (!ctx) return
    try {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'sine'
      osc.frequency.setValueAtTime(523.25, ctx.currentTime)
      osc.frequency.exponentialRampToValueAtTime(659.25, ctx.currentTime + 0.15)
      gain.gain.setValueAtTime(0.06, ctx.currentTime)
      gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + 0.3)
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start()
      osc.stop(ctx.currentTime + 0.3)
    } catch {}
  }

  function playSwitchSound() {
    const ctx = getAudioContext()
    if (!ctx) return
    try {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'triangle'
      const t = ctx.currentTime
      osc.frequency.setValueAtTime(1000, t)
      osc.frequency.exponentialRampToValueAtTime(180, t + 0.04)
      gain.gain.setValueAtTime(0.08, t)
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.04)
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start()
      osc.stop(t + 0.04)
    } catch {}
  }

  function playClickSound() {
    const ctx = getAudioContext()
    if (!ctx) return
    try {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'sine'
      const t = ctx.currentTime
      osc.frequency.setValueAtTime(800, t)
      gain.gain.setValueAtTime(0.04, t)
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.03)
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start()
      osc.stop(t + 0.03)
    } catch {}
  }

  function playCatMeow() {
    const ctx = getAudioContext()
    if (!ctx) return
    try {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'sine'
      const t = ctx.currentTime
      osc.frequency.setValueAtTime(500, t)
      osc.frequency.exponentialRampToValueAtTime(850, t + 0.15)
      osc.frequency.exponentialRampToValueAtTime(580, t + 0.35)
      gain.gain.setValueAtTime(0.06, t)
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.35)
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start()
      osc.stop(t + 0.35)
    } catch {}
  }

  function playSlapSound() {
    const ctx = getAudioContext()
    if (!ctx) return
    try {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'square'
      const t = ctx.currentTime
      osc.frequency.setValueAtTime(320, t)
      osc.frequency.exponentialRampToValueAtTime(40, t + 0.07)
      gain.gain.setValueAtTime(0.12, t)
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.08)
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start()
      osc.stop(t + 0.08)
    } catch {}
  }

  function playTickSound() {
    const ctx = getAudioContext()
    if (!ctx) return
    try {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'sine'
      const t = ctx.currentTime
      osc.frequency.setValueAtTime(600, t)
      gain.gain.setValueAtTime(0.03, t)
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.02)
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start()
      osc.stop(t + 0.02)
    } catch {}
  }

  function playChimeUp() {
    const ctx = getAudioContext()
    if (!ctx) return
    try {
      const osc = ctx.createOscillator()
      const gain = ctx.createGain()
      osc.type = 'triangle'
      const t = ctx.currentTime
      osc.frequency.setValueAtTime(440, t)
      osc.frequency.exponentialRampToValueAtTime(880, t + 0.15)
      gain.gain.setValueAtTime(0.06, t)
      gain.gain.exponentialRampToValueAtTime(0.001, t + 0.2)
      osc.connect(gain)
      gain.connect(ctx.destination)
      osc.start()
      osc.stop(t + 0.2)
    } catch {}
  }

  function playWinnerSound() {
    const ctx = getAudioContext()
    if (!ctx) return
    try {
      const chords = [523.25, 659.25, 783.99, 1046.50]
      chords.forEach((freq, idx) => {
        const osc = ctx.createOscillator()
        const gain = ctx.createGain()
        const t = ctx.currentTime + idx * 0.08
        osc.type = 'triangle'
        osc.frequency.setValueAtTime(freq, t)
        gain.gain.setValueAtTime(0.06, t)
        gain.gain.exponentialRampToValueAtTime(0.001, t + 0.3)
        osc.connect(gain)
        gain.connect(ctx.destination)
        osc.start(t)
        osc.stop(t + 0.3)
      })
    } catch {}
  }

  let timer: number | undefined
  onMounted(() => {
    if (!timer) {
      updateTime()
      timer = window.setInterval(updateTime, 1000)
    }
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
    isLightsDimmed,
    fanSpeed,
    catPetCount,
    catPurrText,
    mosquitoCaught,
    isMosquitoAlive,
    lastSlapPos,
    currentDream,
    currentDreamIndex,
    dreamsList,
    dishesList,
    selectedDish,
    isRollingDish,
    activeAmbientSound,
    togglePreview,
    dismissWarningPopup,
    enableDevBypass,
    disableDevBypass,
    pokeStickman,
    toggleLights,
    cycleFan,
    petCat,
    nextDream,
    slapMosquito,
    rollDish,
    setAmbientSound,
    stopAllAmbient,
  }
}
