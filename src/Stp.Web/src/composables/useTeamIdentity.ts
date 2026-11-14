import { ref } from 'vue'
import {
  fetchTeamIdentity,
  loginTeamAccount as loginRequest,
  logoutTeamAccount as logoutRequest,
  registerTeamAccount as registerRequest,
  setUserName,
  type TeamIdentity,
} from '../api/client'

/** Danh tính người đã đăng nhập (tài khoản nhóm, cookie StpTeam) — null nếu chưa. */
export const teamIdentity = ref<TeamIdentity | null>(null)

/** True sau lần check /api/auth/me đầu tiên — tránh nhấp nháy màn hình đăng nhập. */
export const teamIdentityReady = ref(false)

/**
 * Gọi 1 lần khi app mở: hỏi backend xem cookie phiên còn hạn không.
 * Có tài khoản thì đồng bộ tên hiển thị vào localStorage để chat/presence
 * (kể cả code cũ vẫn dùng getUserName) gắn đúng tài khoản thật.
 */
export async function initTeamIdentity(): Promise<TeamIdentity | null> {
  if (teamIdentityReady.value) return teamIdentity.value
  teamIdentity.value = await fetchTeamIdentity()
  teamIdentityReady.value = true
  if (teamIdentity.value) {
    setUserName(teamIdentity.value.displayName)
  }
  return teamIdentity.value
}

/** Đăng nhập bằng tài khoản nhóm; lỗi ném ra cho màn hình ra vào hiện thông báo. */
export async function loginTeamIdentity(username: string, password: string): Promise<TeamIdentity> {
  const identity = await loginRequest(username, password)
  teamIdentity.value = identity
  teamIdentityReady.value = true
  setUserName(identity.displayName)
  return identity
}

/** Tự đăng ký tài khoản (mặt "Đăng ký" của cuốn hồ sơ) — backend tự đăng nhập luôn. */
export async function registerTeamIdentity(
  username: string,
  displayName: string,
  password: string,
): Promise<TeamIdentity> {
  const identity = await registerRequest(username, displayName, password)
  teamIdentity.value = identity
  teamIdentityReady.value = true
  setUserName(identity.displayName)
  return identity
}

/** Làm mới thông tin danh tính từ backend (sau khi đổi tên hiển thị...). */
export async function refreshTeamIdentity(): Promise<TeamIdentity | null> {
  teamIdentity.value = await fetchTeamIdentity()
  if (teamIdentity.value) {
    setUserName(teamIdentity.value.displayName)
  }
  return teamIdentity.value
}

/** Đăng xuất trên backend + xoá danh tính trong app; giữ tên tự khai cũ làm dự phòng. */
export async function logoutTeamIdentity(): Promise<void> {
  try {
    await logoutRequest()
  } catch {
    // Cookie có thể đã hết hạn sẵn — vẫn xoá danh tính phía app.
  }
  teamIdentity.value = null
  teamIdentityReady.value = true
}
