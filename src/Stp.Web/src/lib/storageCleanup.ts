/**
 * L2: Dọn localStorage history và draft quá hạn (mặc định 30 ngày).
 * Quét các key 'stp.summary.history.' và 'stp.summary.draft.'
 * Nếu mục có mốc thời gian cũ hơn 30 ngày (hoặc JSON lỗi) thì dọn sạch để không tích luỹ rác trên máy người dùng.
 */
export function cleanupExpiredLocalStorage(maxAgeDays = 30): number {
  if (typeof window === 'undefined' || !window.localStorage) return 0

  const maxAgeMs = maxAgeDays * 24 * 60 * 60 * 1000
  const now = Date.now()
  let cleanedCount = 0

  try {
    const keysToRemove: string[] = []
    const total = localStorage.length

    for (let index = 0; index < total; index++) {
      const key = localStorage.key(index)
      if (!key) continue

      if (key.startsWith('stp.summary.draft.') || key.startsWith('stp.summary.history.')) {
        try {
          const raw = localStorage.getItem(key)
          if (!raw) {
            keysToRemove.push(key)
            continue
          }
          const parsed = JSON.parse(raw)
          // Draft thường có savedAt; History thường có timestamp/savedAt
          const timeCandidate =
            parsed.savedAt ??
            parsed.timestamp ??
            parsed.updatedAt ??
            (Array.isArray(parsed) && parsed.length > 0 ? parsed[0]?.timestamp : null)

          if (timeCandidate) {
            const time = new Date(timeCandidate).getTime()
            if (!Number.isNaN(time) && now - time > maxAgeMs) {
              keysToRemove.push(key)
            }
          }
        } catch {
          // JSON bị hỏng hoặc corrupt -> dọn luôn
          keysToRemove.push(key)
        }
      }
    }

    for (const key of keysToRemove) {
      localStorage.removeItem(key)
      cleanedCount++
    }
  } catch {
    // Trình duyệt chặn localStorage trong chế độ bảo mật nghiêm ngặt
  }

  return cleanedCount
}
