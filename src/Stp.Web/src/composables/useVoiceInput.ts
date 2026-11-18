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
  onspeechstart?: (() => void) | null
  onsoundstart?: (() => void) | null
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
  /** Bắt đầu phát hiện tiếng nói/âm lượng micro (dùng để câm họng AI tức thì 16ms). */
  onSpeechOnset?: () => void
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
  let resultOffset = 0

  let audioCtx: AudioContext | null = null
  let analyser: AnalyserNode | null = null
  let mediaStream: MediaStream | null = null
  let volumeAnimId: number | null = null
  let analysisSessionId = 0

  async function startAudioAnalysis() {
    const sessionId = ++analysisSessionId
    try {
      if (!navigator.mediaDevices?.getUserMedia) return
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          echoCancellation: true,
          // Không bật noiseSuppression gắt để tránh triệt tiêu tiếng thì thầm (whisper).
          // Việc lọc ồn máy tính/quạt/điều hòa được xử lý thông minh qua bộ lọc dải tần giọng nói (Bandpass VAD).
          noiseSuppression: false,
          autoGainControl: true,
        },
      })

      // Ngăn chặn race-condition rò rỉ microphone: Nếu trong lúc chờ getUserMedia người dùng đã bấm tắt mic
      if (!wantListening || sessionId !== analysisSessionId) {
        stream.getTracks().forEach((track) => track.stop())
        return
      }

      mediaStream = stream

      const AudioCtxConstructor =
        window.AudioContext ||
        (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext
      if (!AudioCtxConstructor) return

      if (!wantListening || sessionId !== analysisSessionId) {
        stream.getTracks().forEach((track) => track.stop())
        mediaStream = null
        return
      }

      audioCtx = new AudioCtxConstructor()
      const source = audioCtx.createMediaStreamSource(mediaStream)
      analyser = audioCtx.createAnalyser()
      // FFT 256 -> 128 dải tần số (độ phân giải ~187Hz/bin ở 48kHz)
      analyser.fftSize = 256
      analyser.smoothingTimeConstant = 0.3
      source.connect(analyser)
      const data = new Uint8Array(analyser.frequencyBinCount)

      // VAD thích ứng: theo dõi mức ồn nền động (Ambient Noise Floor)
      let ambientNoiseFloor = 10
      let consecutiveSpeechFrames = 0

      const tick = () => {
        if (!listening.value && !wantListening) return
        analyser?.getByteFrequencyData(data)

        // 1. Lọc bỏ dải tần siêu trầm (< 250Hz, Bins 0-1): nơi tập trung tiếng ù quạt, máy lạnh, rung bàn
        // 2. Tách riêng dải tần giọng nói người & tiếng thì thầm (Vocal Formants ~ 350Hz - 3400Hz, Bins 2 đến 18)
        let vocalSum = 0
        const startBin = 2
        const endBin = Math.min(18, data.length - 1)
        const binCount = endBin - startBin + 1

        for (let i = startBin; i <= endBin; i++) {
          vocalSum += data[i]
        }
        const vocalAvg = vocalSum / binCount

        // 3. Đo delta năng lượng giọng nói so với mức ồn nền thực tế của phòng (hỗ trợ cả tiếng thì thầm)
        const delta = vocalAvg - ambientNoiseFloor
        const isSpeechActivity = (delta > 3.5 && vocalAvg > 8) || (vocalAvg > 14)

        if (isSpeechActivity) {
          consecutiveSpeechFrames++
          // Kích hoạt ngay trong 1-2 khung hình (~16-32ms) để ngắt giọng đọc AI (Barge-in) không độ trễ
          if (consecutiveSpeechFrames >= 1) {
            options.onSpeechOnset?.()
          }
        } else {
          consecutiveSpeechFrames = 0
          // Cập nhật mức ồn nền phòng (Exponential Moving Average) khi đang im lặng
          ambientNoiseFloor = ambientNoiseFloor * 0.95 + vocalAvg * 0.05
        }

        // 4. Hiển thị sóng âm trên giao diện theo dải tần giọng nói thực
        const displayEnergy = Math.max(0, vocalAvg - (ambientNoiseFloor * 0.4))
        volume.value = Math.min(100, Math.round((displayEnergy / 45) * 100))

        volumeAnimId = requestAnimationFrame(tick)
      }
      volumeAnimId = requestAnimationFrame(tick)
    } catch {
      // Bỏ qua nếu getUserMedia không được cấp quyền hoặc không khả dụng,
      // Web Speech API vẫn tiếp tục chạy độc lập.
    }
  }

  function stopAudioAnalysis() {
    analysisSessionId++
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
    resultOffset = 0
    options.onInterim?.('')

    if (textToCommit) {
      options.onFinal(textToCommit)
    }

    // Tự động ngắt và khởi động lại SpeechRecognition bằng abort() để Chrome xóa sạch 100% bộ đệm
    // kết quả cũ, câu nói tiếp theo luôn bắt đầu mới 100%, không bao giờ bị dính index cũ hay kẹt chữ!
    if (wantListening && recognition) {
      try {
        recognition.abort()
      } catch {}
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
      resultOffset = 0
      listening.value = true
      options.onStateChange?.(true)
    }

    instance.onspeechstart = () => {
      options.onSpeechOnset?.()
    }

    instance.onsoundstart = () => {
      options.onSpeechOnset?.()
    }

    instance.onresult = (event) => {
      let finalChunk = ''
      let interimChunk = ''

      // Chỉ đọc results từ resultOffset trở đi, ngăn chặn tuyệt đối Chrome nối các câu trước đó vào câu mới
      const startIdx = Math.min(resultOffset, event.results.length)
      for (let index = startIdx; index < event.results.length; index++) {
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

      // Hẹn giờ chốt câu thông minh (Semantic Endpointing / Dynamic Silence Window):
      // Người dùng không bị ngắt lời khi đang ngập ngừng hoặc đọc danh sách dài mười mấy giấy tờ
      if (silenceTimer) {
        window.clearTimeout(silenceTimer)
        silenceTimer = null
      }

      if (fullLiveText.length > 0) {
        const lower = fullLiveText.toLowerCase().trim()
        const wordCount = lower.split(/\s+/).filter(Boolean).length

        // 1. Nếu câu kết thúc bằng "xong" hoặc "lưu" -> Người dùng đã chủ động chốt lệnh -> gửi ngay trong 350ms
        const endsWithCommit =
          lower.endsWith(' xong') ||
          lower === 'xong' ||
          lower.endsWith(' lưu') ||
          lower === 'lưu'

        // 2. Lệnh điều hướng siêu ngắn (1-2 từ): "tiếp", "quay lại", "dòng 1" -> chốt nhanh sau 700ms
        const isQuickCommand =
          wordCount <= 2 &&
          ['tiếp', 'kế tiếp', 'bộ bảy', 'quay lại', 'trước', 'dòng 1', 'dòng 2'].some(
            (cmd) => lower.includes(cmd),
          )

        // 3. Đang đọc danh sách giấy tờ hoặc câu dài (có từ nối "và", "với", "gồm", "bao gồm", "giấy", "hồ sơ", "áp"...)
        // Cho khoảng nghỉ hào phóng 2400ms để người dùng thoải mái nhìn hồ sơ, lấy hơi, ngập ngừng đọc mười mấy giấy tờ mà không bị cắt!
        const isListingOrComplex =
          wordCount >= 3 ||
          lower.includes('gồm') ||
          lower.includes('bao gồm') ||
          lower.includes('và') ||
          lower.includes('với') ||
          lower.includes('có') ||
          lower.includes('áp') ||
          lower.includes('giấy') ||
          lower.includes(',')

        let delay = 1600
        if (endsWithCommit) {
          delay = 350
        } else if (isQuickCommand) {
          delay = 700
        } else if (isListingOrComplex) {
          delay = 2400
        }

        silenceTimer = window.setTimeout(() => {
          silenceTimer = null
          // Cắt bỏ chữ "xong" / "lưu" ở cuối nếu có trước khi chuyển AI
          let cleanedCommit = fullLiveText
          if (cleanedCommit.toLowerCase().endsWith(' xong')) {
            cleanedCommit = cleanedCommit.slice(0, -5).trim()
          } else if (cleanedCommit.toLowerCase().endsWith(' lưu')) {
            cleanedCommit = cleanedCommit.slice(0, -4).trim()
          }
          commitUtterance(cleanedCommit)
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
      resultOffset = 0
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

      // Tự động khởi động lại trong 40ms khi người dùng vẫn bật mic
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
        }, 40)
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
    resultOffset = 0
    options.onInterim?.('')
    if (wantListening && recognition) {
      try {
        recognition.abort()
      } catch {}
    }
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
