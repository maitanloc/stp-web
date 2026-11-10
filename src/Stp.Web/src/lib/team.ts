// Hàm tiện ích cho "nền tảng nhóm" — màu đại diện, viết tắt tên, thời gian tương đối.

/** Màu ổn định theo tên (cùng tên luôn ra cùng màu, khác tên thường ra khác màu). */
export function colorForName(name: string): string {
  let hash = 0
  for (let index = 0; index < name.length; index += 1) {
    hash = (hash * 31 + name.charCodeAt(index)) >>> 0
  }
  const hue = hash % 360
  return `hsl(${hue}, 62%, 42%)`
}

/** Viết tắt 1–2 chữ cái để làm avatar. */
export function initialsFor(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean)
  if (words.length === 0) return '?'
  const firstWord = [...words[0]]
  if (words.length === 1) {
    return (firstWord[0] ?? '?') + (firstWord[1] ?? '')
  }
  return (firstWord[0] ?? '?') + ([...words[1]][0] ?? '')
}

/** "3 giây trước" / "5 phút trước" / "2 giờ trước" / "10:42, 11/11". */
export function relativeTime(iso: string): string {
  const then = new Date(iso).getTime()
  if (Number.isNaN(then)) return ''
  const seconds = Math.max(0, Math.round((Date.now() - then) / 1000))
  if (seconds < 45) return 'vừa xong'
  if (seconds < 3600) return `${Math.round(seconds / 60)} phút trước`
  if (seconds < 86400) return `${Math.round(seconds / 3600)} giờ trước`
  if (seconds < 7 * 86400) return `${Math.round(seconds / 86400)} ngày trước`
  return new Date(iso).toLocaleString('vi-VN', {
    hour: '2-digit',
    minute: '2-digit',
    day: '2-digit',
    month: '2-digit',
  })
}

/** Giờ:phút cho bong bóng chat. */
export function clockTime(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''
  return date.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })
}

/** Nhận diện hành động nhật ký thành chữ dễ đọc. */
export function actionLabel(action: string): string {
  switch (action) {
    case 'export':
      return 'Xuất trích yếu'
    case 'export-filtered':
      return 'Xuất bản lọc'
    case 'export-print':
      return 'Xuất in hàng loạt'
    case 'sync':
      return 'Đồng bộ kho'
    default:
      return action
  }
}
