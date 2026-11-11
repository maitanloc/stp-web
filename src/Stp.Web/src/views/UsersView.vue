<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { request } from '../api/client'
import { initTeamIdentity, teamIdentity } from '../composables/useTeamIdentity'
import { errorMessage } from '../lib/errors'
import { useToast } from '../composables/useToast'

interface UserAccount {
  id: string
  username: string
  displayName: string
  role: 'admin' | 'member'
  isActive: boolean
  createdAt: string
  lastLoginAt: string | null
}

const router = useRouter()
const toast = useToast()

const loading = ref(true)
const users = ref<UserAccount[]>([])
const isAdmin = computed(() => teamIdentity.value?.role === 'admin')

const createUsername = ref('')
const createDisplayName = ref('')
const createPassword = ref('')
const createRole = ref<'admin' | 'member'>('member')
const createError = ref<string | null>(null)
const creating = ref(false)

const resetTarget = ref<UserAccount | null>(null)
const resetPassword = ref('')

function timeLabel(value: string | null): string {
  if (!value) return 'Chưa đăng nhập'
  return new Date(value).toLocaleString('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

async function loadUsers(): Promise<void> {
  try {
    const response = await request<{ items: UserAccount[] }>('/api/admin/users')
    users.value = response.items
  } catch (error) {
    toast.error('Không tải được danh sách tài khoản', errorMessage(error))
  } finally {
    loading.value = false
  }
}

async function submitCreate(): Promise<void> {
  if (creating.value) return
  createError.value = null
  creating.value = true
  try {
    const created = await request<UserAccount>('/api/admin/users', {
      method: 'POST',
      body: {
        username: createUsername.value.trim(),
        displayName: createDisplayName.value.trim(),
        password: createPassword.value,
        role: createRole.value,
      },
    })
    toast.success(`Đã tạo tài khoản ${created.username}`, `Gửi cho ${created.displayName}: đăng nhập bằng tên đăng nhập này và mật khẩu đã đặt`)
    createUsername.value = ''
    createDisplayName.value = ''
    createPassword.value = ''
    createRole.value = 'member'
    await loadUsers()
  } catch (error) {
    createError.value = errorMessage(error)
  } finally {
    creating.value = false
  }
}

async function patchUser(
  user: UserAccount,
  changes: { displayName?: string; role?: string; isActive?: boolean },
  successMessage: string,
): Promise<void> {
  try {
    await request(`/api/admin/users/${user.id}`, { method: 'PATCH', body: changes })
    toast.success(successMessage)
  } catch (error) {
    toast.error('Không lưu được thay đổi', errorMessage(error))
  } finally {
    await loadUsers()
  }
}

function toggleActive(user: UserAccount): void {
  const nextActive = !user.isActive
  if (!nextActive && !window.confirm(`Ngừng kích hoạt tài khoản "${user.username}"? Người này sẽ không đăng nhập được nữa.`)) {
    return
  }
  void patchUser(
    user,
    { isActive: nextActive },
    nextActive ? `Đã kích hoạt lại ${user.username}` : `Đã ngừng kích hoạt ${user.username}`,
  )
}

function changeRole(user: UserAccount, role: string): void {
  if (role === user.role) return
  void patchUser(user, { role }, `Đã đổi quyền ${user.username} → ${role === 'admin' ? 'Quản trị' : 'Thành viên'}`)
}

function saveDisplayName(user: UserAccount, event: Event): void {
  const value = (event.target as HTMLInputElement).value.trim()
  if (value === user.displayName) return
  void patchUser(user, { displayName: value }, `Đã đổi tên hiển thị của ${user.username}`)
}

function openReset(user: UserAccount): void {
  resetTarget.value = user
  resetPassword.value = ''
}

async function submitReset(): Promise<void> {
  const target = resetTarget.value
  if (!target) return
  if (resetPassword.value.length < 6) {
    toast.error('Mật khẩu cần tối thiểu 6 ký tự')
    return
  }
  try {
    await request(`/api/admin/users/${target.id}/password`, {
      method: 'POST',
      body: { password: resetPassword.value },
    })
    toast.success(`Đã đặt lại mật khẩu cho ${target.username}`)
    resetTarget.value = null
    resetPassword.value = ''
  } catch (error) {
    toast.error('Không đổi được mật khẩu', errorMessage(error))
  }
}

onMounted(async () => {
  await initTeamIdentity()
  if (!isAdmin.value) {
    loading.value = false
    return
  }
  await loadUsers()
})
</script>

<template>
  <div class="users-view">
    <div v-if="!isAdmin" class="card need-admin">
      <div class="card-body">
        <h2>Cần quyền quản trị</h2>
        <p>Trang này dành cho quản trị viên. Đăng nhập bằng tài khoản có quyền "admin" để quản lý tài khoản nhóm.</p>
        <button class="btn btn-secondary btn-sm" type="button" @click="router.push('/')">Về tổng quan</button>
      </div>
    </div>

    <template v-else>
      <div class="card create-card">
        <div class="card-header">
          <div class="card-title">Tạo tài khoản mới</div>
          <div class="card-subtitle">Gửi tên đăng nhập và mật khẩu cho thành viên — người này đổi mật khẩu qua quản trị khi cần</div>
        </div>
        <div class="card-body">
          <form class="create-form" @submit.prevent="submitCreate()">
            <input
              v-model="createUsername"
              type="text"
              maxlength="50"
              autocomplete="off"
              placeholder="Tên đăng nhập (vd: lan, minh.quang)"
            />
            <input
              v-model="createDisplayName"
              type="text"
              maxlength="100"
              autocomplete="off"
              placeholder="Tên hiển thị (vd: Lan, Minh Quang)"
            />
            <input
              v-model="createPassword"
              type="text"
              maxlength="128"
              autocomplete="off"
              placeholder="Mật khẩu (tối thiểu 6 ký tự)"
            />
            <select v-model="createRole">
              <option value="member">Thành viên</option>
              <option value="admin">Quản trị</option>
            </select>
            <button class="btn btn-primary btn-sm" type="submit" :disabled="creating">
              {{ creating ? 'Đang tạo…' : 'Tạo tài khoản' }}
            </button>
          </form>
          <p v-if="createError" class="form-error">{{ createError }}</p>
        </div>
      </div>

      <div class="card">
        <div class="card-header">
          <div class="card-title">Tài khoản nhóm</div>
          <div class="card-subtitle">{{ users.length }} tài khoản · chat, nhật ký và presence gắn theo các tài khoản này</div>
        </div>
        <div class="card-body">
          <div v-if="resetTarget" class="reset-panel">
            <span class="reset-label">
              Đặt lại mật khẩu cho <strong>{{ resetTarget.username }}</strong>
            </span>
            <input
              v-model="resetPassword"
              type="text"
              maxlength="128"
              placeholder="Mật khẩu mới (tối thiểu 6 ký tự)"
              @keydown.enter="submitReset()"
            />
            <button class="btn btn-primary btn-sm" type="button" @click="submitReset()">Đặt lại</button>
            <button class="btn btn-secondary btn-sm" type="button" @click="resetTarget = null">Bỏ</button>
          </div>

          <div class="table-wrap">
            <table class="table table-hover">
              <thead>
                <tr>
                  <th>Tên đăng nhập</th>
                  <th>Tên hiển thị</th>
                  <th>Quyền</th>
                  <th>Trạng thái</th>
                  <th>Đăng nhập gần nhất</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr v-if="loading">
                  <td colspan="6" class="table-empty">Đang tải danh sách…</td>
                </tr>
                <tr v-else-if="users.length === 0">
                  <td colspan="6" class="table-empty">Chưa có tài khoản nào — tạo ở khung trên.</td>
                </tr>
                <tr v-for="user in users" v-else :key="user.id">
                  <td>
                    <span class="username-cell">@{{ user.username }}</span>
                  </td>
                  <td>
                    <input
                      class="inline-input"
                      type="text"
                      maxlength="100"
                      :value="user.displayName"
                      @change="saveDisplayName(user, $event)"
                    />
                  </td>
                  <td>
                    <select class="inline-input role-select" :value="user.role" @change="changeRole(user, ($event.target as HTMLSelectElement).value)">
                      <option value="member">Thành viên</option>
                      <option value="admin">Quản trị</option>
                    </select>
                  </td>
                  <td>
                    <button
                      type="button"
                      class="status-chip"
                      :class="user.isActive ? 'active' : 'inactive'"
                      :title="user.isActive ? 'Bấm để ngừng kích hoạt' : 'Bấm để kích hoạt lại'"
                      @click="toggleActive(user)"
                    >
                      <span class="chip-dot"></span>
                      {{ user.isActive ? 'Đang hoạt động' : 'Đã ngừng' }}
                    </button>
                  </td>
                  <td class="time-cell">{{ timeLabel(user.lastLoginAt) }}</td>
                  <td>
                    <button
                      class="btn btn-secondary btn-sm"
                      type="button"
                      @click="openReset(user)"
                    >
                      Đổi mật khẩu
                    </button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.users-view {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.need-admin .card-body {
  display: flex;
  flex-direction: column;
  gap: 10px;
  align-items: flex-start;
}

.need-admin h2 {
  margin: 0;
  font-size: 17px;
}

.need-admin p {
  margin: 0;
  color: var(--muted);
  font-size: 13.5px;
}

.create-form {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr auto auto;
  gap: 10px;
  align-items: center;
}

.create-form input,
.create-form select {
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  color: var(--text);
  padding: 9px 11px;
  font-size: 13px;
  outline: none;
  min-width: 0;
}

.create-form input:focus,
.create-form select:focus {
  border-color: var(--brand);
  box-shadow: 0 0 0 3px var(--brand-soft);
}

.form-error {
  margin: 10px 0 0;
  color: var(--danger);
  font-size: 12.5px;
}

.reset-panel {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  padding: 10px 12px;
  margin-bottom: 12px;
}

.reset-label {
  font-size: 13px;
  color: var(--text-soft);
}

.reset-panel input {
  flex: 1;
  min-width: 220px;
  border: 1px solid var(--border-strong);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text);
  padding: 8px 11px;
  font-size: 13px;
  outline: none;
}

.username-cell {
  font-weight: 700;
  color: var(--text);
  font-size: 13px;
}

.inline-input {
  width: 100%;
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  background: transparent;
  color: var(--text);
  padding: 6px 8px;
  font-size: 13px;
  outline: none;
}

.inline-input:hover {
  border-color: var(--border);
}

.inline-input:focus {
  border-color: var(--brand);
  background: var(--surface-2);
}

.role-select {
  width: auto;
  cursor: pointer;
}

.status-chip {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  border: 1px solid var(--border);
  border-radius: 999px;
  background: var(--surface-2);
  color: var(--text-soft);
  font-size: 11.5px;
  font-weight: 600;
  padding: 4px 10px;
  cursor: pointer;
}

.chip-dot {
  width: 7px;
  height: 7px;
  border-radius: 999px;
}

.status-chip.active .chip-dot {
  background: var(--success);
  box-shadow: 0 0 0 3px var(--success-soft);
}

.status-chip.inactive .chip-dot {
  background: var(--muted-2);
}

.status-chip.active {
  color: var(--text);
}

.time-cell {
  font-size: 12px;
  color: var(--muted);
  white-space: nowrap;
}

@media (max-width: 900px) {
  .create-form {
    grid-template-columns: 1fr;
  }
}
</style>
