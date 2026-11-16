import { onUnmounted, ref } from 'vue'

/**
 * Voice GĐ 1 (plan "trich-yeu-sieu-toc"): Web Speech API có sẵn trong
 * Chrome/Edge — miễn phí, không thêm dependency, hỗ trợ vi-VN, chạy tốt
 * trên tunnel HTTPS. Không lưu âm thanh — chỉ giữ text đã nghe.
 *
 * Chế độ ĐỌC LIÊN TỤC: SpeechRecognition của Chrome tự ngắt sau vài giây
 * lặng — composable tự nối lại cho tới khi người dùng tắt mic.
 */

interface SpeechRecognitionAlternativeLike {
  transcript: string
}

interface SpeechRecognitionResultLike {
  isFinal: boolean
  length: number
  [index: number]: SpeechRecognitionAlternativeLike
}

interface SpeechRecognitionEventLike {
  resultIndex: number
  results: {
    length: number
    [index: number]: SpeechRecognitionResultLike
  }
}

interface SpeechRecognitionErrorEventLike {
  error: string
}

interface SpeechRecognitionLike {
  lang: string
  continuous: boolean
  interimResults: boolean
  maxAlternatives: number
  start(): void
  stop(): void
  abort(): void
  onresult: ((event: SpeechRecognitionEventLike) => void) | null
  onerror: ((event: SpeechRecognitionErrorEventLike) => void) | null
  onend: (() => void) | null
  onstart: (() => void) | null
}

type SpeechRecognitionConstructor = new () => SpeechRecognitionLike

interface SpeechWindow {
  SpeechRecognition?: SpeechRecognitionConstructor
  webkitSpeechRecognition?: SpeechRecognitionConstructor
}

export interface VoiceInputOptions {
  /** Câu nói đã chốt (final result) — dùng cho parser. */
  onFinal: (text: string) => void
  /** Câu đang nói dở — dùng hiển thị live. */
  onInterim?: (text: string) => void
  /** Lỗi nghe (mic bị chặn, mạng đứt...) — UI hiện toast. */
  onError?: (message: string) => void
  /** Mic thực sự bắt đầu nghe / dừng nghe. */
  onStateChange?: (listening: boolean) => void
}

const ERROR_MESSAGES: Record<string, string> = {
  'not-allowed': 'Máy chặn mic — mở quyền micro cho trình duyệt rồi thử lại.',
  'service-not-allowed': 'Dịch vụ nhận dạng tiếng nói bị chặn — kiểm tra quyền micro.',
  network: 'Mất nối tới dịch vụ nhận dạng tiếng nói — kiểm tra internet.',
  'audio-capture': 'Không tìm thấy micro. Cắm mic rồi thử lại.',
  aborted: '',
}

export function useVoiceInput(options: VoiceInputOptions) {
  const supported = ref(false)
  const listening = ref(false)
  const interim = ref('')
  const volume = ref(0) // 0 - 100

  let recognition: SpeechRecognitionLike | null = null
  let wantListening = false
  let restarting = false
  let silenceTimer: number | null = null
  let activeUtterance = ''

  let audioCtx: AudioContext | null = null
  let analyser: AnalyserNode | null = null
  let mediaStream: MediaStream | null = null
  let volumeAnimId: number | null = null

  async function startAudioAnalysis() {
    try {
      if (!navigator.mediaDevices?.getUserMedia) return
      mediaStream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          // Tắt lọc ồn gắt để không triệt tiêu giọng nói nhỏ/nói thầm
          noiseSuppression: false,
          // Tự động khuếch đại gain khi âm lượng nói nhỏ
          autoGainControl: true,
        },
      })
      const AudioCtxConstructor =
        window.AudioContext ||
        (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext
      if (!AudioCtxConstructor) return
      audioCtx = new AudioCtxConstructor()
      const source = audioCtx.createMediaStreamSource(mediaStream)
      analyser = audioCtx.createAnalyser()
      analyser.fftSize = 64
      source.connect(analyser)
      const data = new Uint8Array(analyser.frequencyBinCount)

      const tick = () => {
        if (!listening.value && !wantListening) return
        analyser?.getByteFrequencyData(data)
        let sum = 0
        for (let i = 0; i < data.length; i++) sum += data[i]
        const avg = sum / data.length
        // Tăng độ nhạy hiển thị sóng âm lên gấp đôi để người dùng thấy rõ khi nói nhỏ
        volume.value = Math.min(100, Math.round((avg / 64) * 100))
        volumeAnimId = requestAnimationFrame(tick)
      }
      volumeAnimId = requestAnimationFrame(tick)
    } catch {
      // Bỏ qua nếu getUserMedia không được cấp quyền hoặc không khả dụng,
      // Web Speech API vẫn tiếp tục chạy độc lập.
    }
  }

  function stopAudioAnalysis() {
    if (volumeAnimId) {
      cancelAnimationFrame(volumeAnimId)
      volumeAnimId = null
    }
    if (mediaStream) {
      mediaStream.getTracks().forEach((track) => track.stop())
      mediaStream = null
    }
    if (audioCtx) {
      audioCtx.close().catch(() => {})
      audioCtx = null
    }
    volume.value = 0
  }

  function detectConstructor(): SpeechRecognitionConstructor | null {
    const scope = window as unknown as SpeechWindow
    return scope.SpeechRecognition ?? scope.webkitSpeechRecognition ?? null
  }

  function commitUtterance(explicitText?: string): void {
    if (silenceTimer) {
      window.clearTimeout(silenceTimer)
      silenceTimer = null
    }
    const textToCommit = (explicitText !== undefined ? explicitText : activeUtterance).trim()
    activeUtterance = ''
    interim.value = ''
    options.onInterim?.('')

    if (textToCommit) {
      options.onFinal(textToCommit)
    }

    if (wantListening && recognition) {
      try {
        recognition.abort()
      } catch {
        // Bỏ qua
      }
    }
  }

  function createRecognition(): SpeechRecognitionLike | null {
    const Constructor = detectConstructor()
    if (!Constructor) return null
    const instance = new Constructor()
    instance.lang = 'vi-VN'
    instance.continuous = true
    instance.interimResults = true
    instance.maxAlternatives = 1

    instance.onstart = () => {
      listening.value = true
      options.onStateChange?.(true)
    }

    instance.onresult = (event) => {
      let finalChunk = ''
      let interimChunk = ''

      // Tích lũy toàn bộ kết quả của câu đang nói (không làm mất từ trước đó)
      for (let index = 0; index < event.results.length; index++) {
        const result = event.results[index]
        const transcript = result[0]?.transcript ?? ''
        if (result.isFinal) {
          finalChunk += transcript + ' '
        } else {
          interimChunk += transcript
        }
      }

      const fullLiveText = (finalChunk + interimChunk).trim()
      activeUtterance = fullLiveText
      interim.value = fullLiveText
      options.onInterim?.(fullLiveText)

      // Hẹn giờ im lặng (silence detection): nếu ngừng nói 950ms (hoặc 550ms với lệnh ngắn) thì chốt câu
      if (silenceTimer) {
        window.clearTimeout(silenceTimer)
        silenceTimer = null
      }

      if (fullLiveText.length > 0) {
        const lower = fullLiveText.toLowerCase()
        const isQuickCommand = ['xong', 'lưu', 'tiếp', 'kế tiếp', 'bộ bảy', 'quay lại', 'trước'].includes(lower)
        const delay = isQuickCommand ? 550 : 950

        silenceTimer = window.setTimeout(() => {
          silenceTimer = null
          commitUtterance()
        }, delay)
      }
    }

    instance.onerror = (event) => {
      const message = ERROR_MESSAGES[event.error] ?? `Không nghe được: ${event.error}`
      if (message) options.onError?.(message)
      // Lỗi quyền/mạng: ngừng hẳn, không nối lại vô hạn.
      if (event.error === 'not-allowed' || event.error === 'service-not-allowed') {
        wantListening = false
      }
    }

    instance.onend = () => {
      if (silenceTimer) {
        window.clearTimeout(silenceTimer)
        silenceTimer = null
      }
      const pending = activeUtterance.trim()
      if (pending) {
        activeUtterance = ''
        interim.value = ''
        options.onInterim?.('')
        options.onFinal(pending)
      }

      listening.value = false
      options.onStateChange?.(false)

      // Chrome tự ngắt sau khoảng lặng — nối lại nếu người dùng vẫn bật mic.
      if (wantListening && !restarting) {
        restarting = true
        window.setTimeout(() => {
          restarting = false
          if (wantListening) {
            try {
              recognition?.start()
            } catch {
              // Đã chạy rồi — bỏ qua nhịp này.
            }
          }
        }, 150)
      }
    }

    return instance
  }

  supported.value = detectConstructor() !== null

  function start(): void {
    if (!supported.value) {
      options.onError?.('Trình duyệt này chưa hỗ trợ đọc tiếng nói — dùng Chrome hoặc Edge.')
      return
    }
    wantListening = true
    void startAudioAnalysis()
    if (!recognition) recognition = createRecognition()
    if (!recognition) return
    try {
      recognition.start()
    } catch {
      // Đã đang chạy — coi như đang nghe.
      listening.value = true
      options.onStateChange?.(true)
    }
  }

  function stop(commitPending = true): void {
    wantListening = false
    if (silenceTimer) {
      window.clearTimeout(silenceTimer)
      silenceTimer = null
    }
    const pending = activeUtterance.trim()
    activeUtterance = ''
    interim.value = ''
    options.onInterim?.('')
    stopAudioAnalysis()

    if (commitPending && pending) {
      options.onFinal(pending)
    }

    try {
      recognition?.stop()
    } catch {
      // Bỏ qua.
    }
    listening.value = false
    options.onStateChange?.(false)
  }

  function toggle(): void {
    if (listening.value || wantListening) stop()
    else start()
  }

  function commitNow(text?: string): void {
    commitUtterance(text)
  }

  function clear(): void {
    if (silenceTimer) {
      window.clearTimeout(silenceTimer)
      silenceTimer = null
    }
    activeUtterance = ''
    interim.value = ''
    options.onInterim?.('')
  }

  onUnmounted(() => {
    wantListening = false
    if (silenceTimer) {
      window.clearTimeout(silenceTimer)
      silenceTimer = null
    }
    stopAudioAnalysis()
    try {
      recognition?.abort()
    } catch {
      // Bỏ qua.
    }
  })

  return { supported, listening, interim, volume, start, stop, toggle, commitNow, clear }
}
