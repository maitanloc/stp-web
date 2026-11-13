/**
 * K1: pipeline avatar upload — cắt vuông client (canvas 96×96 webp) + POST
 * /api/team/avatar + cập nhật map avatarMap toàn app.
 * renderAvatar đọc map này để biết chỗ nào dùng ảnh, chỗ nào giữ vòng chữ cái.
 */
import { ref } from 'vue'
import { request } from '../api/client'

/** Map { displayName → avatarUrl } cho cả app. */
export const avatarMap = ref<Record<string, string>>({})

/** Đang nạp danh sách avatar lần đầu hay chưa. */
let avatarsFetched = false

/** Nạp danh sách avatar từ server vào avatarMap. Gọi 1 lần sau login. */
export async function fetchAvatarMap(): Promise<void> {
  if (avatarsFetched) return
  avatarsFetched = true
  try {
    const list = await request<Array<{ userId: string; displayName: string; avatarVersion: number }>>(
      '/api/team/avatars',
    )
    const map: Record<string, string> = {}
    for (const entry of list) {
      map[entry.displayName] = `/api/team/avatar/${entry.userId}?v=${entry.avatarVersion}`
    }
    avatarMap.value = map
  } catch {
    // Chưa đăng nhập hoặc mạng chập chờn — avatarMap vẫn rỗng, renderAvatar
    // tự fallback về vòng chữ cái đầu, không ảnh hưởng gì.
  }
}

/** Tải lại map sau khi mình vừa upload ảnh mới. */
export function invalidateAvatarMap(): void {
  avatarsFetched = false
}

/** Cắt canvas thành Blob webp 96×96, quality 0.9. */
function cropToBlob(src: string, size = 96): Promise<Blob> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    img.onload = () => {
      const canvas = document.createElement('canvas')
      canvas.width = size
      canvas.height = size
      const ctx = canvas.getContext('2d')
      if (!ctx) {
        reject(new Error('Canvas không khả dụng'))
        return
      }
      // Cắt hình vuông từ tâm ảnh gốc
      const side = Math.min(img.naturalWidth, img.naturalHeight)
      const sx = (img.naturalWidth - side) / 2
      const sy = (img.naturalHeight - side) / 2
      ctx.drawImage(img, sx, sy, side, side, 0, 0, size, size)
      canvas.toBlob(
        (blob) => {
          if (blob) resolve(blob)
          else reject(new Error('Không tạo được blob ảnh'))
        },
        'image/webp',
        0.9,
      )
    }
    img.onerror = () => reject(new Error('Không đọc được ảnh'))
    img.src = src
  })
}

/** Upload ảnh đại diện: nhận File từ input → cắt → POST → trả về { userId, avatarVersion }. */
export async function uploadAvatar(file: File, actor?: string): Promise<{ userId: string; avatarVersion: number }> {
  // Đọc file thành data URL để vẽ canvas
  const dataUrl = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = () => reject(new Error('Không đọc được file'))
    reader.readAsDataURL(file)
  })

  const blob = await cropToBlob(dataUrl, 96)

  const form = new FormData()
  form.append('file', blob, 'avatar.webp')
  if (actor) {
    form.append('actor', actor)
  }

  const result = await request<{ userId: string; avatarVersion: number }>('/api/team/avatar', {
    method: 'POST',
    form,
  })

  return result
}

