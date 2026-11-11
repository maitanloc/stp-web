// Hàm tiện ích cho "nền tảng nhóm" — màu đại diện, viết tắt tên, thời gian tương đối.

/**
 * Palette avatar chuẩn Google Workspace (Docs/Sheets collaborator colors) —
 * 14 màu bão hoà xen kẽ nóng/lạnh, chữ trắng luôn đọc được trên nền sáng lẫn
 * tối nên không cần biến thể dark mode riêng.
 */
const AVATAR_PALETTE = [
  '#e8710a', // cam
  '#246fdb', // xanh dương đậm
  '#f4518c', // hồng
  '#1e8e3e', // xanh lá
  '#a142f4', // tím
  '#e37400', // cam đậm
  '#1a73e8', // xanh dương
  '#d93025', // đỏ
  '#188038', // xanh lá đậm
  '#9334e6', // tím sáng
  '#ff8f00', // vàng cam
  '#8430cb', // tím đậm
  '#f5386a', // hồng đậm
  '#c5221f', // đỏ đậm
] as const

/** Màu ổn định theo tên: cùng tên luôn cùng màu, phân bố đều trên palette. */
export function colorForName(name: string): string {
  let hash = 0
  for (let index = 0; index < name.length; index += 1) {
    hash = (hash * 31 + name.charCodeAt(index)) >>> 0
  }
  return AVATAR_PALETTE[hash % AVATAR_PALETTE.length]
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

/** D1: render avatar 1 chỗ duy nhất — chữ viết tắt + nền palette + chữ trắng. */
export function renderAvatar(name: string): {
  initials: string
  style: Record<string, string>
} {
  return {
    initials: initialsFor(name),
    style: {
      background: colorForName(name),
      color: '#fff',
    },
  }
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
    case 'bulk':
      return 'Áp giấy tờ hàng loạt'
    default:
      return action
  }
}
