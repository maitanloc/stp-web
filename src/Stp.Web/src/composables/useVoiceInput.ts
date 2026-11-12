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

  let recognition: SpeechRecognitionLike | null = null
  let wantListening = false
  let restarting = false

  function detectConstructor(): SpeechRecognitionConstructor | null {
    const scope = window as unknown as SpeechWindow
    return scope.SpeechRecognition ?? scope.webkitSpeechRecognition ?? null
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
      let interimText = ''
      for (let index = event.resultIndex; index < event.results.length; index++) {
        const result = event.results[index]
        const transcript = result[0]?.transcript ?? ''
        if (result.isFinal) {
          const finalText = transcript.trim()
          if (finalText.length > 0) options.onFinal(finalText)
        } else {
          interimText += transcript
        }
      }
      interim.value = interimText.trim()
      options.onInterim?.(interim.value)
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
      listening.value = false
      options.onStateChange?.(false)
      interim.value = ''
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
        }, 350)
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

  function stop(): void {
    wantListening = false
    interim.value = ''
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

  onUnmounted(() => {
    wantListening = false
    try {
      recognition?.abort()
    } catch {
      // Bỏ qua.
    }
  })

  return { supported, listening, interim, start, stop, toggle }
}
